using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_DogWanderController の partial (Shared)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_DogWanderController
    {
        // ================================================ 共有導出 (IMP-NET-002)
        //
        // ここから下のヘルパーは **サーバー時刻・名簿の静的設定・順位** だけを入力にする。
        // 自分の現在位置・進行方向・他犬の実位置を混ぜてはいけない —
        // 1 箇所でも混ざると、そこからローカル値が全目標へ再侵入する (DES-NET-001 §4-4)。
        // 相手の実位置を見てよいのは ContactSpeedCap (減速) と視線だけ。

        /// <summary>その時刻におけるその犬の「直前の共有アンカー」=
        /// 最後に共有された徘徊 waypoint 目標 (IMP-NET-002)。
        ///
        /// 徘徊レール (B7) と同じ導出を、その犬の種・調整値 (Start で名簿から写した
        /// キャッシュ) で再現する。入力がサーバー時刻と静的設定だけなので
        /// 全クライアントで同じ絶対座標になる。窓の外 (待機中) の時刻なら
        /// 直前の窓の最後の waypoint を返す — 犬はそこで止まったまま待機している
        /// はずの場所で、実位置と大きくは離れない。</summary>
        Vector3 WanderAnchorAt(double atTime, int i)
        {
            float cp = _packCyclePeriod[i];
            int ci = (int)(atTime / cp);
            float ph = (float)(atTime - (double)ci * cp);
            if (ph < 0f)
            {
                ci -= 1;
                ph += cp;
            }

            float len = Mathf.Lerp(_packWanderMin[i], _packWanderMax[i], Rand01Salt(ci, RandStreamWanderLen, _packSalt[i]));
            float latest = Mathf.Max(cp - len, 0f);
            float ws = Mathf.Lerp(latest * 0.25f, latest, Rand01Salt(ci, RandStreamWanderStart, _packSalt[i]));
            int wp;
            if (ph < ws)
            {
                // この周期の窓はまだ開いていない → 前の周期の最後の waypoint
                ci -= 1;
                len = Mathf.Lerp(_packWanderMin[i], _packWanderMax[i], Rand01Salt(ci, RandStreamWanderLen, _packSalt[i]));
                wp = (int)(Mathf.Max(len - 0.001f, 0f) / _packWaypointPeriod[i]);
            }
            else
            {
                float inWin = ph - ws;
                float lim = Mathf.Max(len - 0.001f, 0f);
                if (inWin > lim) inWin = lim; // 窓が閉じた後は最後の waypoint に留まる
                wp = (int)(inWin / _packWaypointPeriod[i]);
            }

            int hop = ci * WaypointHopStride + wp; // 徘徊 (B7) と同じ合成
            float r = Mathf.Sqrt(Rand01Salt(hop, RandStreamWaypointRadius, _packSalt[i])) * _packAreaRadius[i];
            float ang = Rand01Salt(hop, RandStreamWaypointAngle, _packSalt[i]) * Mathf.PI * 2f;
            return _packAreaCenter[i].position
                + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        }

        /// <summary>逃げ役 fleeIdx のホップ番号 hop の逃げ先 (共有の絶対座標)。
        ///
        /// **その犬の** fleeHopPeriod / areaRadius / playFleeInnerRatio で導出する —
        /// これらは Dog Pack Setup がその犬の庭から導出する犬ごとの値で、
        /// 自分の値で代用すると追う役と逃げ役が別の点を見る (IMP-NET-002)。
        /// PlayRand は seedSalt を混ぜないので、式全体が全犬・全クライアントで一致する。</summary>
        Vector3 FleeHopTargetByHop(int cyc, int hop, int fleeIdx)
        {
            float ang = PlayRand(cyc, PlayStreamFleeBaseAngle) * Mathf.PI * 2f
                + hop * FleeTurnRad
                + (PlayRand(cyc, PlayStreamFleeJitterBase + hop) - 0.5f) * FleeJitterRad;
            float rMax = _packAreaRadius[fleeIdx];
            float r = Mathf.Lerp(rMax * _packFleeInner[fleeIdx], rMax, PlayRand(cyc, PlayStreamFleeRadiusBase + hop));
            return _packAreaCenter[fleeIdx].position
                + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        }

        /// <summary>追う役の回り込み点 (IMP-NET-002 / DES-NET-001 §4-4 の案 C)。
        ///
        /// **P (逃げ先)・前ホップ・順位のみから導出する** — 進入方向は
        /// 「前ホップ → P」の共有ベクトルで代用し、自分の位置は使わない。
        /// P の手前 (逃げ役が来る側) の弧に、順位由来の扇で散らして立つ。
        /// 「逃げている犬の行き先へ回り込む」= 先読みして角を取る絵になる。</summary>
        Vector3 ChaseInterceptByHop(int cyc, int hop, int flee, int chaserIdx, int n)
        {
            Vector3 p = FleeHopTargetByHop(cyc, hop, flee);
            Vector3 approach = p - FleeHopTargetByHop(cyc, hop - 1, flee);
            approach.y = 0f;
            float len = approach.magnitude;
            Vector3 dir;
            if (len < 0.01f)
            {
                // 退化: 前ホップと同一点 (黄金角で回すのでまず起きない)。共有乱数で向きを作る
                float a = PlayRand(cyc, PlayStreamChaseAxisBase + hop) * Mathf.PI * 2f;
                dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            else
            {
                dir = approach / len;
            }

            // 順位 (共有値) から扇のオフセット: 0, -1, +1, -2, … × ChaseFanRad。
            // 逃げ役を除いた通し番号にするので、追う役同士が同じ点を取り合わない
            int myRank = PlayRankOf(chaserIdx, n);
            if (myRank > PlayRankOf(flee, n)) myRank -= 1;
            float off = ((myRank + 1) / 2) * ChaseFanRad * ((myRank & 1) == 1 ? -1f : 1f);
            float cos = Mathf.Cos(off);
            float sin = Mathf.Sin(off);
            Vector3 back = new Vector3(dir.x * cos + dir.z * sin, 0f, -dir.x * sin + dir.z * cos);

            float standoff = (_packRadius[chaserIdx] + _packRadius[flee]) * playContactSpacing;
            return p - back * standoff;
        }

        /// <summary>対面 (B10) の自分の停止点 (共有の絶対座標)。
        /// 待ち合わせ点 = **両犬の直前の共有アンカーの中点** (DES-NET-001 §5-2 の第一候補)。
        /// 窓の開始時刻で評価するので窓の間じゅう不動 — 中点が動かないから
        /// 「歩み寄って contact で止まる」が両クライアントで同じ点に落ちる。</summary>
        Vector3 FaceStopPoint(int cyc, float start, int me, int other)
        {
            double winAbs = (double)cyc * playCyclePeriod + start;
            Vector3 mineA = WanderAnchorAt(winAbs, me);
            Vector3 otherA = WanderAnchorAt(winAbs, other);
            Vector3 d = otherA - mineA;
            d.y = 0f;
            float dist = d.magnitude;
            Vector3 dir;
            if (dist < 0.01f)
            {
                // 退化: 2 匹のアンカーが同一点。共有乱数の軸で開く (番号の小さい方が負の側)
                float a = PlayRand(cyc, PlayStreamFaceAxis) * Mathf.PI * 2f;
                dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (me > other) dir = -dir;
            }
            else
            {
                dir = d / dist;
            }
            float contact = (_packRadius[me] + _packRadius[other]) * playContactSpacing;
            return (mineA + otherA) * 0.5f - dir * (contact * 0.5f);
        }

        /// <summary>本体終端 (t = coreLen) でのその犬の共有アンカー (IMP-NET-002)。
        /// 「離れる」(B11) の向きと起点はここから作る — 実位置ではなく
        /// 「本体の最後に居るはずだった共有の点」を使うことで、進入時のずれが
        /// 離脱の目標に入らない (旧実装が唯一の真の増幅器だった理由の裏返し)。</summary>
        Vector3 PlayEndAnchor(int cyc, float start, float coreLen, int want, int n, bool isFace, int who)
        {
            if (isFace)
            {
                int a0 = PlayDogOfRank(0, n);
                int a1 = PlayDogOfRank(1, n);
                if (a0 < 0 || a1 < 0)
                    return WanderAnchorAt((double)cyc * playCyclePeriod + start, who); // 保険
                return FaceStopPoint(cyc, start, who, who == a0 ? a1 : a0);
            }

            // 追いかけっこ: 本体末尾の区間の逃げ役と最後のホップで評価する
            float tEnd = coreLen - 0.001f;
            int flee = PlayFleeDogAt(cyc, coreLen, want, n, tEnd);
            if (flee < 0)
                return WanderAnchorAt((double)cyc * playCyclePeriod + start, who); // 保険
            int hop = (int)(tEnd / _packFleeHop[flee]);
            if (who == flee) return FleeHopTargetByHop(cyc, hop, flee);
            return ChaseInterceptByHop(cyc, hop, flee, who, n);
        }

        /// <summary>接触の間合いの手前で減速するための速度上限 (v = √(2a·余白))。
        ///
        /// **相手の実位置をロジックに使ってよいのはここだけ** (DES-NET-001 §4-4) —
        /// 目標 (どこへ向かうか) は共有導出のまま、目の前に見えている相手へ
        /// めり込まないよう速度だけを絞る。0 を返したら「間合いに入った」。
        /// ずれた画面では止まる位置が多少割れるが、それは有界で、
        /// 次の共有ホップ / 停止アンカー (IMP-NET-003) で回収される。</summary>
        float ContactSpeedCap(Vector3 otherPos, float contact, float maxSpeed)
        {
            Vector3 d = otherPos - dogRoot.position;
            d.y = 0f;
            float room = d.magnitude - contact;
            if (room <= FaceSettleSlack) return 0f;
            float slow = Mathf.Sqrt(2f * deceleration * room);
            return slow < maxSpeed ? slow : maxSpeed;
        }

        /// <summary>逃げ役の走り。**決定論の逃げ先**へ全力で走る。
        ///
        /// 逃げ先へ着くと減速して一瞬止まる = 追う側が追い付く = 追いかけっこに見える
        /// (2 匹とも同速なので、この「止まる間」が無いと一定距離で並走するだけになる)。
        ///
        /// **IMP-DOG-012 で追う側はここから出した** — 追う役が n-1 匹になり、
        /// 「役を 2 匹で分ける」形では書けなくなったため。
        /// IMP-NET-002 で逃げ先の式は <see cref="FleeHopTargetByHop"/> へ出した —
        /// 追う役が同じ式で P を導出する (式を複製すると黙ってズレる)。</summary>
        void PlayFlee(int cyc, float t)
        {
            // 逃げる: 庭の中の点を毎ホップ大きく振って渡り歩く
            int hop = (int)(t / _packFleeHop[dogIndex]);
            StepToward(FleeHopTargetByHop(cyc, hop, dogIndex), 0f, runSpeed);
        }

        /// <summary>向かい合って近寄る。
        ///
        /// IMP-NET-002 (B10): 待ち合わせ点は**両犬の直前の共有アンカーの中点**
        /// (<see cref="FaceStopPoint"/>)。旧実装の「2 匹のローカル位置の中点」は
        /// 距離方向しか収束せず、並進のずれをそのまま保存した (DES-NET-001 §2 = 等級 D')。
        /// 共有アンカーは窓開始時刻で評価するので不動 — 決定論のまま
        /// 「今いるあたりの間で落ち合う」絵は保たれる (§5-2 の第一候補)。
        ///
        /// 相手の実位置は**減速と視線にしか使わない**: 目の前に見えている相手へ
        /// めり込まないよう速度を絞り (<see cref="ContactSpeedCap"/>)、間合いに
        /// 入ったらそこで止まる。**後ずさりして取り直さない** (立ち位置は体の後ろに
        /// あるため、素直に追うと相手に背を向けて歩き去る — insideStop と同じ話)。</summary>
        void PlayFace(int cyc, float start, int other, float contact)
        {
            Vector3 op = _packRoots[other].position; // sync: local-ok: 減速と視線のみ — 目標は FaceStopPoint の共有導出 (DES-NET-001 §4-4)
            float cap = ContactSpeedCap(op, contact, walkSpeed);
            if (cap <= 0f)
            {
                // ---- 間合いに入った。**ここで固まらせない** (IMP-DOG-016 の 立% 対策) ----
                // 対面は「2 匹が向き合って立つ型」なので、放っておくと突っ立ちがそのまま
                // 計上される (実測: ラブ・ダックスで 13%)。**型は残したまま中身を足す** —
                // ゆっくり回り込み、ときどき止まって鼻を突き合わせる。
                // 犬が向き合ったときに実際やる動きでもある。
                //
                // 回る / 止まるの切り替えは**サーバー時刻**から導くので全クライアント一致
                // (ローカル時計を混ぜない — IMP-NET-002 の規律)
                float ft = (float)(Networking.GetServerTimeInSeconds() - start);
                if (Mathf.Repeat(ft, FaceCyclePeriod) < FacePauseSec)
                {
                    _look = true;   // 鼻を突き合わせる間 (taimen クリップ)
                    Coast();
                    FaceTowards(op);
                    if (petController != null) petController._SetUrge(1f); // 尻尾
                    return;
                }

                // **回り込む。** 目標は 2 匹の共有停止点の中点まわりを回した点なので、
                // ローカル位置を一切混ぜない (相手の実位置は減速と視線にしか使わない)
                Vector3 mine = FaceStopPoint(cyc, start, dogIndex, other);
                Vector3 theirs = FaceStopPoint(cyc, start, other, dogIndex);
                Vector3 mid = (mine + theirs) * 0.5f;
                Vector3 rad = mine - mid;
                rad.y = 0f;
                // 2 匹とも同じ向きに回るので、**回っている間も向かい合ったまま**になる
                float rot = ft * FaceOrbitRadPerSec;
                float cs = Mathf.Cos(rot);
                float sn = Mathf.Sin(rot);
                Vector3 orbit = new Vector3(rad.x * cs - rad.z * sn, 0f, rad.x * sn + rad.z * cs);
                if (petController != null) petController._SetUrge(0.8f);
                StepToward(mid + orbit, 0f, walkSpeed * FaceOrbitSpeedScale);
                return;
            }

            StepToward(FaceStopPoint(cyc, start, dogIndex, other), 0f, cap);
        }

        /// <summary>離れる (窓の最後)。**相手から視線を外して**歩いて離れる。
        /// これが無いと、対面したまま固まって窓が終わり、次の徘徊窓が開くまで
        /// 2 匹が向かい合って停止し続ける (唐突な終わり方の正体)。
        ///
        /// IMP-NET-002 (B11): 離脱先は**本体終端の共有アンカーから相手アンカーの
        /// 反対方向へ**の共有点。旧実装は「自分の実位置から相手の実位置の反対方向」で、
        /// 進入時のずれを起点にも向きにも取り込む**唯一の真の増幅器**だった
        /// (DES-NET-001 §2)。相手の実位置はもう一切使わない。</summary>
        void PlayBreakOff(int cyc, float start, float coreLen, int want, int n, bool isFace, int away)
        {
            Vector3 mineA = PlayEndAnchor(cyc, start, coreLen, want, n, isFace, dogIndex);
            Vector3 awayA = PlayEndAnchor(cyc, start, coreLen, want, n, isFace, away);
            Vector3 dir = mineA - awayA;
            dir.y = 0f;
            float d = dir.magnitude;
            if (d < 0.01f)
            {
                // 退化: アンカーが同一点。共有乱数で向きを作る (犬番号を混ぜて各自別方向へ)
                float a = PlayRand(cyc, PlayStreamBreakAxisBase + dogIndex) * Mathf.PI * 2f;
                dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            else
            {
                dir /= d;
            }

            Vector3 target = mineA + dir * (walkSpeed * playBreakOff);
            // 庭から出ない。円の外なら縁へ丸める (共有値同士の演算なので結果も共有)
            Vector3 fromCenter = target - areaCenter.position;
            fromCenter.y = 0f;
            float fc = fromCenter.magnitude;
            if (fc > areaRadius && fc > 0.0001f)
                target = areaCenter.position + fromCenter * (areaRadius / fc);

            // 目標が体のすぐそば = 歩く距離が無いとき。**歩けなくても視線は外す** —
            // 「離れる」の芯は距離ではなく、相手を見るのをやめることの方
            Vector3 to = target - dogRoot.position;
            to.y = 0f;
            if (to.magnitude < 0.1f)
            {
                Coast();
                FaceTowards(dogRoot.position + dir);
                return;
            }

            StepToward(target, 0f, walkSpeed);
        }

        /// <summary>その場で相手の方を向く (足は動かさない)。
        ///
        /// **止まっている間は旋回速度を落とす** (2026-08-14 Play フィードバック)。
        /// 呼び出し元 (PlayFace / PlayBreakOff) はどちらも直前に Coast() を呼ぶため、
        /// ここは基本的に静止ポーズ中の旋回になる。足を止めたまま 300deg/s で回すと
        /// 接地した足が地面を擦り、「アニメが止まっているのに動いている」= 滑って見える。
        /// KD_DogFetchController が Pickup/Drop 中の旋回を禁止しているのと同じ理由だが、
        /// こちらは向くこと自体が目的なので禁止せず減速で処す。
        /// 減速し切る前に呼ばれた場合 (間合いへ滑り込んだ直後) は足が動いているので、
        /// そのフレームだけは通常の旋回速度でよい。</summary>
        void FaceTowards(Vector3 worldPoint)
        {
            // IMP-NET-005: 向きも `VRCObjectSync` が配る
            if (!_dogOwner) return;
            Vector3 d = worldPoint - dogRoot.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) return;
            float rate = _speed > stopSpeedThreshold ? turnSpeedDegPerSec : idleTurnSpeedDegPerSec;
            Quaternion look = Quaternion.LookRotation(d.normalized, Vector3.up);
            dogRoot.rotation = Quaternion.RotateTowards(dogRoot.rotation, look, // net: owner-only: 冒頭の早期 return が非 Owner を弾く
                rate * Time.deltaTime);
        }

        // ================================================================

        /// <summary>移動アニメを実速度から決める (KD_DogFetchController.UpdateLocomotionAnim と同じ方針)。
        /// 待機中も毎フレーム呼ばれるため、変化したときだけ Animator へ書く。</summary>
        void ApplyMoveAnim()
        {
            bool moving = _speed > stopSpeedThreshold;
            bool running = moving && _speed >= runAnimSpeed;

            // 停止後の保持 (2026-08-14 Play フィードバック 3 巡目)。
            // **足が止まるより先に静止ポーズへ入らせない。**
            // 減速距離を 12 → 24 → 36 (停止 0.24m → 0.08m) と詰めても滑って見えたので、
            // 距離ではなく順序の問題として扱う。実速度が 0 になっても stopAnimHold の間は
            // 直前の脚 (Walk / Run) を続け、動きが完全に終わってからポーズへ落とす。
            // 保持中は足踏み側へずれるが、接地の厳密一致は要件ではない (方針の確認済み)。
            if (moving) _stopHoldUntil = Time.time + stopAnimHold;
            else if (Time.time < _stopHoldUntil)
            {
                moving = true;
                running = _heldRunning;
            }
            _heldRunning = running;

            // 移動中は LookAt を立てない。AC_KD_Dog の遷移は Run/Walk が LookAt より先に
            // 評価されるので立てても無意味なうえ、止まった瞬間に対面ポーズが出て見える
            bool look = _look && !moving;
            // IMP-TRT-001: 食べポーズ (Fetch のくわえポーズ流用)。Bool は一度に 1 つの規約
            // (設計書 §5) — ポーズが立つフレームは LookAt を諦める
            bool eatPose = _eatPose && !moving;
            if (eatPose) look = false;
            // IMP-DOG-010: 座っているフレームは座りが勝つ (Bool は一度に 1 つ)。
            // Sit そのものを書くのは LateUpdate の 1 箇所
            if (_wantSit) { look = false; eatPose = false; }

            // IMP-DOG-009: 走りクリップの再生速度を実速度へ追従させる (KH_FrisbeeCatch.ApplyAnimatorSpeed と同じ方針)。
            // 散歩の速い段 (走り 4.5 / 全力 7.2) は runSpeed の数倍なので、掛けないと足が地面を滑る。
            // **Bool が同じなら return する下の行より前に置く** — 速度は Bool が変わらないまま動き続ける。
            // 非 Owner も通す (_speed は ObserveSpeed が観測で埋めている) ので同期は要らない。
            // Fetch / フリスビーが Animator を持っている間は書かない — `Animator.speed` の書き手は常に 1 つ。
            // フリスビー (KH_FrisbeeCatch) は追走で自分の倍率を書き、Idle へ戻るとき 1 に戻して
            // _InvalidateLeashAnimation でこちらへ書き直させる。Fetch (KD_DogFetchController) は
            // 再生速度を一度も書かないので、**こちらが引き渡しの時点で 1 に返しておく** (ReleaseAnimatorSpeed)
            if (!AnimatorYielded())
            {
                float animSpeed = running && runSpeed > 0.01f ? Mathf.Clamp(_speed / runSpeed, 1f, runAnimScaleMax) : 1f;
                if (_leashAnimationDirty || Mathf.Abs(animSpeed - _wroteAnimSpeed) > 0.01f)
                {
                    dogAnimator.speed = animSpeed;
                    _wroteAnimSpeed = animSpeed;
                }
            }

            if (!_leashAnimationDirty && moving == _wasMoving && running == _wasRunning && look == _wasLooking
                && eatPose == _wasEatPose) return;

            dogAnimator.SetBool("Run", running);
            dogAnimator.SetBool("Walk", moving && !running);
            if (_leashAnimationDirty || look != _wasLooking) dogAnimator.SetBool("LookAt", look);
            if (_leashAnimationDirty || eatPose != _wasEatPose) dogAnimator.SetBool("Pickup", eatPose);
            _leashAnimationDirty = false;
            _wasEatPose = eatPose;
            _wasMoving = moving;
            if (running && !_wasRunning && photoDirector != null)
                photoDirector._OnDogChase(dogIndex); // 追跡ドローンの流用 (クールダウンは向こうが持つ)
            _wasRunning = running;
            _wasLooking = look;
        }

        /// <summary>走りクリップの再生速度を 1 倍へ返し、こちらの控えも畳む (IMP-DOG-009)。
        ///
        /// Fetch / フリスビーへ Animator を渡すとき、こちらが上げたままの再生速度を残さないための処理。
        /// **`_wroteAnimSpeed` は「最後に書いたのが自分」までしか証明しない** — 受け取った側が
        /// もう自分の速度を書いているかもしれないので、実物を読み直して**まだ自分の値のままのときだけ**
        /// 1 へ戻す (Codex D2)。控えはどの経路でも 1 に畳む (次に書くときの比較元を汚さない)。</summary>
        void ReleaseAnimatorSpeed()
        {
            if (dogAnimator != null)
            {
                float now = dogAnimator.speed;
                if (now != 1f && now == _wroteAnimSpeed) dogAnimator.speed = 1f;
            }
            _wroteAnimSpeed = 1f;
        }

        /// <summary>じゃれ合いの開始エッジを撮影へ渡す (IMP-CAM-002)。
        ///
        /// **Update から毎フレーム呼ばれる** — 早期 return するどの経路からも呼ぶので、
        /// 「遊びをやめた」も必ず拾える (拾わないと次の窓で二度と通知が出ない)。
        /// 通知は開始の 1 回だけで、クールダウンと「まだ遊んでいるか」の再確認は
        /// 撮影側が持つ (犬は撮影の都合を知らなくてよい)。
        ///
        /// <see cref="dogIndex"/> をそのまま渡す。撮影側の犬配列は名簿と同じ並びなので、
        /// ここで別の番号へ翻訳する層を作らない (翻訳は黙ってズレる)。</summary>
        void NotifyPlay()
        {
            if (_playing == _wasPlaying) return;
            _wasPlaying = _playing;
            if (_playing && photoDirector != null) photoDirector._OnDogsPlay(dogIndex);
        }

        float NearestPlayerSqrDistance()
        {
            int count = VRCPlayerApi.GetPlayerCount();
            if (count > _players.Length) count = _players.Length;
            VRCPlayerApi.GetPlayers(_players);
            float best = float.MaxValue;
            Vector3 dogPos = dogRoot.position;
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi p = _players[i];
                if (!Utilities.IsValid(p)) continue;
                float d = (p.GetPosition() - dogPos).sqrMagnitude;
                if (d < best) best = d;
            }
            return best;
        }

    }
}
