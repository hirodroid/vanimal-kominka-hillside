using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_DogWanderController の partial (Treat)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_DogWanderController
    {
        // ================================================================ おやつ (IMP-TRT-001)

        /// <summary>
        /// おやつへの反応 (設計書 §11 / Phase 4b)。演技したら true。
        ///
        /// 2 拍: **見せる** (持たれている → 出した人へ寄っておやつを見つめる) →
        /// **あげる** (着地 → そこへ歩いて食べる → 出した人を見る)。
        ///
        /// 入力は同期実体だけ — **どの枠へ向かうか** (`KD_TreatBox` の `dogSlot` /
        /// `dogSlotOutMs`)、**食べ始め** (`dogEatMs`)、おやつの位置 (VRCObjectSync)、
        /// **出した人** (枠の `giverId`)。**食べる点 = 着地したおやつの位置 =
        /// 全クライアント共通の絶対座標**なので、食べる場所そのものが収束点になる
        /// (催促・徘徊と同じ論法のより強い形)。犬をここで 1 mm も同期しない。
        ///
        /// ## Phase 1 から変わったのは「どのおやつを見るか」だけ
        ///
        /// <code>
        /// 旧: treatSystem.EaterDogIndex == dogIndex   世界に 1 個のおやつ
        /// 新: treatBox.LiveSlotOf(dogIndex)           **(枠, outMs) の対**
        /// </code>
        ///
        /// **世代 (`outMs`) が一致しない枠は自分の Claim ではない** — 枠は再利用されるので、
        /// 番号だけを覚えていると「食べ終わった枠に別の人が出した次のおやつ」へ
        /// 古い Claim が黙って継承される。判定は `LiveSlotOf` に閉じてある。
        ///
        /// **出した人を `Networking.GetOwner` から導出しない** — Owner は「現在の所有者」で
        /// あって「出した人」ではなく、Pickup / Respawn / 退室の移譲で意味が変わる。
        /// 正本は枠の `giverId` だけ (Issue「導出で済ませる案は 3 つとも成立しない」)。
        /// </summary>
        bool UpdateTreat()
        {
            if (treatBox == null || dogIndex < 0) return false;

            // ---- 食べの立ち上がり / 終わりのエッジ (同期値から導出) ----
            // 掴んでいるかの判定より先に見る — 食べ終わると枠が回収されて Claim ごと
            // 畳まれるので、Claim で先に return すると終わりのエッジを永久に拾えず、
            // 返事 (完了条件 3) が出ない
            bool eatingMine = treatBox.EatingNowOf(dogIndex);
            if (eatingMine && !_treatEating)
            {
                _treatEating = true;
                KD_TreatSlot eating = treatBox.LiveSlotOf(dogIndex);
                // 皿へ戻っても頭が追わないよう、食べ点は**食べ始めに 1 度だけ**固定する
                _treatEatPos = eating != null ? eating.BodyPosition() : dogRoot.position;
                // **返事の相手も食べ始めの瞬間に確定する** (Phase 1 と同じ作法)。
                // 食べている途中で枠が回収されても、返事の相手はすり替わらない
                _thanksPlayerId = eating != null ? eating.GiverId() : -1;
            }
            else if (!eatingMine && _treatEating)
            {
                // 食べ終わった (または途中で畳まれた) → 出した人を見る (返事 — 完了条件 3)
                _treatEating = false;
                _thanksUntil = Time.time + treatThanksDuration;
            }

            // ---- 返事: 出した人の方を向いて見る (ローカル演出。開始時刻は同期値由来なので
            // クライアント間のずれは食べ終わりの伝播ぶんだけ) ----
            if (Time.time < _thanksUntil)
            {
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(_thanksPlayerId);
                if (!Utilities.IsValid(p))
                {
                    _thanksUntil = -1f; // 相手が居ないなら演技ごと畳む
                    return false;
                }
                _treatBusy = true;
                Vector3 pPos = p.GetPosition();
                // `_look` は `Coast()` より先に立てる (UpdateNudge と同じ書き順の罠)
                if (_speed <= stopSpeedThreshold) _look = true;
                Coast();
                FaceTowards(pPos);
                if (petController != null)
                {
                    VRCPlayerApi.TrackingData thanksHead = p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    TreatGaze(thanksHead.position, 1f);   // IMP-DOG-006: held until the next decision on the leash (HillsideTreatCall.cs)
                }
                return true;
            }

            // **向かってよい枠かの判定は 1 箇所** (状態 Out + 世代一致)。
            // ここで null が返るのは「掴んでいない / 枠が再利用された / まだ Reserved」で、
            // どれも**犬が動き出してはいけない**場合
            KD_TreatSlot slot = treatBox.LiveSlotOf(dogIndex);
            if (slot == null) return false;
            _treatBusy = true;

            Vector3 dogPos = dogRoot.position;

            // ---- 見せられている: 出した人へ寄って、おやつを見つめる (催促と同型) ----
            if (slot.BodyHeld())
            {
                // **持ち主は giverId から引く。** 掴めるのは giver だけ
                // (`pickupable` を giverId から導出してある) なので、持ち主 = 出した人
                VRCPlayerApi holder = VRCPlayerApi.GetPlayerById(slot.GiverId());
                if (!Utilities.IsValid(holder))
                {
                    Coast();
                    return true; // 伝播の谷間。次フレームには確定する
                }
                Vector3 holderPos = holder.GetPosition();
                Vector3 flat = holderPos - dogPos;
                flat.y = 0f;
                float hDist = flat.magnitude;
                // 体格ぶん広く取る (0 フォールバックも UpdateNudge と同じ)
                float radius = _selfRadius > 0.001f ? _selfRadius : 0.2f;
                float standoff = TreatStandoff() + radius;   // IMP-DOG-006: closer on the leash (the hand reaches the head and the collar)

                Vector3 dir = flat;
                float advance = 0f;
                if (hDist > 0.0001f)
                {
                    dir = flat / hDist;
                    advance = hDist - standoff;
                    float wallClear = WallClearance(dogPos, dir, hDist);
                    if (advance > wallClear) advance = wallClear;
                }
                bool nearHolder = advance <= FaceSettleSlack;
                if (nearHolder)
                {
                    if (_speed <= stopSpeedThreshold) _look = true;
                    Coast();
                    if (hDist > 0.0001f) FaceTowards(holderPos);
                }
                else
                {
                    TreatStep(dogPos + dir * advance, 0f, walkSpeed);   // IMP-DOG-006: on the leash, clamped to the leash reach (HillsideTreatCall.cs)
                }
                // 見つめるのは持ち主の顔ではなく**おやつそのもの** (食い意地が読める)
                TreatGaze(slot.BodyPosition(), nearHolder ? 1f : 0.6f);
                return true;
            }

            // ---- あげられた: おやつの位置へ歩き、届いたら食べる ----
            // 食べ始めの同期が自分の到着より先に届いても、届くまでは歩く (結果整合性 —
            // 目標が共有座標なのですぐ追いつく)。食べ点は食べ始めに固定した値を使う
            Vector3 treatPos = _treatEating ? _treatEatPos : slot.BodyPosition();
            Vector3 to = treatPos - dogPos;
            to.y = 0f;
            float dist = to.magnitude;

            if (dist <= treatArriveRadius)
            {
                // 到着の報告 (**食べ始めを書くのはマスターだけ** — 犬の Owner はマスター固定
                // なので、実際に歩いている犬を見ている人と書く人が同じ 1 人になる)
                if (!_treatEating) treatBox._ReportArrived(dogIndex);
                // 食べる: 頭を下げるポーズ (Fetch のくわえポーズ流用 — 設計書 §11 決定 1)。
                // 頭の注視は差し込まない — ポーズが頭を下げているところへ注視の追加回転を
                // 重ねると喧嘩する (§4 の書き手は 1 つの規約)。尻尾だけ振る。
                // 食べポーズ中は旋回もしない (BUG-DOG-001 — 静止ポーズ中に回すと滑って見える)
                if (_speed <= stopSpeedThreshold) _eatPose = true;
                Coast();
                if (dist > 0.0001f && !_treatEating && !_eatPose) FaceTowards(treatPos);
                TreatUrge(1f);
                return true;
            }

            // 歩いて向かう。壁は目標点を手前へ寄せる (催促と同じ — StepToward は無改造)
            Vector3 dirT = to / dist;
            float advanceT = dist - treatArriveRadius * 0.7f; // 到着判定より少し奥まで詰める (Fetch と同じ)
            float wallClearT = WallClearance(dogPos, dirT, dist);
            if (advanceT > wallClearT) advanceT = wallClearT;
            if (advanceT <= FaceSettleSlack)
            {
                // 壁の向こうに落ちた: 行けるところまで来た。見つめて待つ
                // (食べ始められないまま KD_TreatSlot の abandonTimeout が枠を回収するのが出口)
                Coast();
                TreatGaze(treatPos, 0.6f);
                return true;
            }
            // IMP-DOG-006: on the leash the step stops at the leash reach and the dog looks at the treat from there (decision 5)
            TreatStep(dogPos + dirT * advanceT, 0f, dist > runDistance ? runSpeed : walkSpeed);
            TreatGaze(treatPos, 0.8f);
            return true;
        }

    }
}
