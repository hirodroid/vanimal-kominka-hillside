using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_DogWanderController の partial (Nudge)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_DogWanderController
    {
        // ================================================================ 投げる前の演技 (IMP-FET-005)

        /// <summary>
        /// 自分のボールが持たれていれば、寄って行って急かす。演技したら true。
        ///
        /// **設計書 §10 決定 2 / 4 / 5 の実装。** 同期変数は 1 つも使わない —
        /// 入力は「持たれているか (Pickup の同期済み保持状態)」「持ち主の位置 (同期済み)」
        /// 「フェイントの成立 (持ち主のクライアントが配る one-shot イベント)」だけ。
        ///
        /// 犬の位置は同期しないが、**間合いで止まるので止まる場所が一致点になる**
        /// (徘徊・じゃれ合いと同じ収束の論法)。持ち主が歩き回っている間はずれるが、
        /// 止まれば収束する。
        /// </summary>
        bool UpdateNudge()
        {
            if (_ball == null) return false;
            if (!_ball.IsHeldNow)
            {
                _holderId = -1;
                return false;
            }

            VRCPlayerApi holder = _ball.HolderPlayer;
            if (!Utilities.IsValid(holder))
            {
                _holderId = -1;
                return false;
            }

            // **持ち主が確定するまで反応しない** (設計書 §10 決定 4)。
            // 掴んだ直後は Ownership の移譲が伝播しておらず、旧 Owner
            // (= 最後に投げた人) を指しうる。そのまま寄ると**別人のところへ行く** —
            // 不変条件 4 が最初の 1 秒で崩れる。0.2 秒遅れて気づく方がむしろ犬らしい
            if (holder.playerId != _holderId)
            {
                _holderId = holder.playerId;
                _holderSince = Time.time;
                return false;
            }
            if (Time.time - _holderSince < holderStableTime) return false;

            _nudging = true;

            Vector3 dogPos = dogRoot.position;
            Vector3 holderPos = holder.GetPosition();
            Vector3 flat = holderPos - dogPos;
            flat.y = 0f;
            float dist = flat.magnitude;

            // 体格ぶん広く取る (名簿が壊れていて _selfRadius が 0 でも間合いが潰れないよう、
            // 0 のときは既定へ落とす — ラブが鼻先まで来るのを防ぐ)
            float radius = _selfRadius > 0.001f ? _selfRadius : 0.2f;
            float standoff = nudgeStandoff + radius;

            // どこまで進めるか = 「間合いの手前まで」と「壁の手前まで」の小さい方。
            // **壁は目標点を寄せることで表す** (設計書 §10 決定 7) — `StepToward` を
            // 改造しないので、徘徊・じゃれ合いは 1 行も変わらない。
            // 止まり方も既存の v=√(2ad) がそのまま効く (惰性で壁へめり込まない)
            Vector3 dir = flat;
            float advance = 0f;
            if (dist > 0.0001f)
            {
                dir = flat / dist;
                advance = dist - standoff;
                float wallClear = WallClearance(dogPos, dir, dist);
                if (advance > wallClear) advance = wallClear;
            }

            // 間合いの内側 / 壁で行き止まり = **その場で急かす。**
            // 後ずさりして取り直さない (Fetch の insideStop と同じ)。
            // 近づいてきた人が撫でられるのはむしろ正しいし、壁越しでも催促は続ける
            bool arrived = advance <= FaceSettleSlack;
            if (arrived)
            {
                // **`_look` は `Coast()` より先に立てる。** LookAt を Animator へ書くのは
                // `ApplyMoveAnim` (= `Coast` / `StepToward` の中) なので、後で立てると
                // そのフレームは false のまま書かれ、翌フレーム頭で false に戻される =
                // **LookAt が永久に立たない。** じゃれ合いの `PlayFace` も同じ順序で書いている
                if (_speed <= stopSpeedThreshold) _look = true;
                Coast();
                if (dist > 0.0001f) FaceTowards(holderPos);
            }
            else
            {
                StepToward(dogPos + dir * advance, 0f, walkSpeed);
            }

            ApplyNudgeGaze(holder, holderPos, arrived);
            return true;
        }

        /// <summary>催促 / フェイントの見せ方を決めて撫で層へ差し込む。
        /// **調停をここに置くことで、優先度が呼び出し順に依存しない** (設計書 §10 決定 3)。</summary>
        void ApplyNudgeGaze(VRCPlayerApi holder, Vector3 holderPos, bool arrived)
        {
            if (petController == null) return;

            // 頭の高さ。犬から見上げる先なので、腰ではなく頭を見る
            VRCPlayerApi.TrackingData head = holder.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 headPos = head.position;

            float since = Time.time - _ball.feintTime;
            if (since >= 0f && since < feintTrackDuration)
            {
                // 1) 飛んだはずの方向を目で追う
                Vector3 look = _ball.transform.position
                    + _ball.feintDir * feintGazeDistance + Vector3.up * feintGazeRise;
                petController._SetGaze(look, 0f);
                petController._SetUrge(1f);
                return;
            }
            if (since >= feintTrackDuration && since < feintTrackDuration + feintReturnDuration)
            {
                // 2) 見返す。**ここが「なげてないじゃん」の山場**
                petController._SetGaze(headPos, feintTiltDeg);
                petController._SetUrge(1f);
                // 吠えるのは見返した瞬間 (振った瞬間ではない — 抗議に聞こえる位置に置く)。
                // 1 回のフェイントで 1 度だけ
                if (_ball.feintBark && _barkedFeint != _ball.feintTime)
                {
                    _barkedFeint = _ball.feintTime;
                    Bark();
                }
                return;
            }

            // 3) 通常の催促。頭を小刻みに上下させて急かす (クリップは要らない)
            float bob = 0f;
            if (arrived && nudgeBobFrequency > 0.01f)
                bob = Mathf.Sin(Time.time * nudgeBobFrequency * 2f * Mathf.PI) * nudgeBobAmplitude;
            petController._SetGaze(headPos + Vector3.up * bob, 0f);
            petController._SetUrge(arrived ? 1f : 0.55f);
        }

        void Bark()
        {
            if (barkSource == null || barkClip == null) return;
            // ピッチの揺らぎは各クライアントがローカルに引いてよい
            // (**同時に聞き比べられないので誰にも差が分からない** — 設計書 §10 決定 6)
            barkSource.pitch = 1f + Random.Range(-barkPitchJitter, barkPitchJitter);
            barkSource.PlayOneShot(barkClip);
        }

        /// <summary>目標を「壁の手前」へ寄せる (設計書 §10 決定 7)。
        ///
        /// **レイは水平に、`nudgeStepHeight` の高さから打つ。** 地面近くから打つと
        /// 斜面や段差が先に当たって奥の壁が取れない (SphereCast は最初の衝突しか返さない)。
        /// 球が段差より上を通るので、**この高さが「登れる段差の上限」になる**。
        /// それでも斜面を拾う場合に備えて、法線で壁と床を分ける。
        ///
        /// 止まり方は既存の `v = √(2ad)` に任せる — 目標点が手前へ動くだけなので、
        /// **惰性で壁へめり込むことがない** (`Coast` で止めると惰性ぶん進んでしまう)。
        ///
        /// 返すのは「この向きへあと何 m 進めるか」。壁が無ければ <paramref name="dist"/>。</summary>
        float WallClearance(Vector3 dogPos, Vector3 dir, float dist)
        {
            Vector3 origin = dogPos + Vector3.up * nudgeStepHeight;
            RaycastHit hit;
            if (!Physics.SphereCast(origin, nudgeWallRadius, dir, out hit, dist,
                    groundLayerMask, QueryTriggerInteraction.Ignore))
                return dist;

            // 床・斜面は壁ではない (|法線の y| が大きいものは踏めるもの)。
            // 段差そのものは球の高さで避けている — **nudgeStepHeight より低いものは
            // そもそも当たらない** ので、ここへ来るのは「登れない高さのもの」だけ
            if (hit.normal.y > 0.5f || hit.normal.y < -0.5f) return dist;

            float clear = hit.distance - nudgeWallRadius;
            return clear < 0f ? 0f : clear;
        }

    }
}
