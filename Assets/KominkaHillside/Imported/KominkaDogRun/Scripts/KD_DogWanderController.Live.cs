using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_DogWanderController の partial (Live)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_DogWanderController
    {
        // ================================================================ ライブの観客 (IMP-PRG-004 Phase 2)

        /// <summary>**集まって、ステージを向いて、伏せて見る。** 型は <see cref="UpdateSleep"/>
        /// (歩く → 到着で凍結 → 向き直る → 姿勢)。設計の正本は DES-PRG-002 §4.6。
        ///
        /// 入力は Live の同期値 (`IsPlaying`) と静的な集合地点だけ。
        /// 撫でられている犬は**その場に居る**なら伏せたまま (撫では観客の枝を崩さない)、
        /// **よそに居る**なら呼ばない (撫でが終わった瞬間に呼ばれる)。
        /// Live が終わったら凍結を解いて false = 通常の徘徊へ戻る。</summary>
        bool UpdateLive()
        {
            if (liveSystem == null || liveSpot == null || dogIndex < 0) return false;
            if (!liveSystem.IsPlaying) { _liveFrozen = false; return false; }
            if (activities != null && _lastFormation != activities.Formation)
            { _lastFormation = activities.Formation; _liveFrozen = false; }

            bool petting = petController != null && petController.IsPetting;
            if (petting && !_liveFrozen) return false;   // よそで撫でられている犬はそのまま

            _liveCalled = true;

            // **休憩 FSM は畳む** (寝る・Fetch と同じ処置)
            if (_restPhase != RestPhaseNone) EndRest();

            Vector3 dogPos = dogRoot.position;

            // ---- 凍結前: 並びのスロットへ歩く ----
            if (!_liveFrozen)
            {
                Vector3 spot = LiveSpot();
                Vector3 to = spot - dogPos;
                to.y = 0f;
                float dist = to.magnitude;

                if (dist <= arriveRadius && _speed <= stopSpeedThreshold)
                {
                    _liveFrozen = true;
                }
                else
                {
                    // 呼び出し・Fetch と同じく、壁や柱を通過して集合先へ進む。
                    // WallClearance で手前へ寄せると迂回できず止まり続ける (2026-09-10 ユーザー方針)。
                    // 接地・加減速・Owner だけの位置更新は StepToward に任せる。
                    StepToward(spot, 0f, dist > runDistance ? runSpeed : walkSpeed);
                    return true;
                }
            }

            // ---- 凍結後: ステージを向いて伏せる ----
            Coast();
            bool facing = true;
            if (!petting && !_wasLieDown && _speed <= stopSpeedThreshold)
            {
                Vector3 face = liveSpot.forward;
                face.y = 0f;
                if (face.sqrMagnitude > 0.0001f)
                {
                    facing = Vector3.Angle(dogRoot.forward, face) <= SleepFaceToleranceDeg;
                    if (!facing) FaceTowards(dogPos + face);
                }
            }
            if (_speed <= stopSpeedThreshold && facing)
            {
                _groupSit = activities != null && activities.Sit(dogIndex);
                _wantLieDown = !_groupSit;
            }
            return true;
        }

        /// <summary>**観客の並びのスロット。** 集合地点の right 方向に dogIndex 順で横一列
        /// (扇にしないのは、全員が同じ方向 (ステージ) を向くから)。
        /// **入力は静的な集合地点と名簿の頭数だけ** — ローカル値を 1 つも混ぜない
        /// (`scripts/check-sync-anchors.js` の SHARED_FUNCS が本体を機械検査する)。</summary>
        Vector3 LiveSpot()
        {
            if (activities != null) return liveSpot.position + liveSpot.rotation * activities.Offset(activities.Formation, dogIndex);
            int n = _packRadius != null ? _packRadius.Length : 1;
            float offset = (dogIndex - (n - 1) * 0.5f) * liveSpacing;
            Vector3 right = liveSpot.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            else right = right.normalized;
            return liveSpot.position + right * offset;
        }

        bool UpdateActivity()
        {
            if (activities == null || !activities.Available(dogIndex)) return false;
            int mode = activities.Mode;
            if (mode == 1)
            {
                _groupCalled = true;
                Vector3 goal = activities.Slot(dogIndex);
                Vector3 flat = goal - dogRoot.position; flat.y = 0;
                if (flat.magnitude > arriveRadius || _speed > stopSpeedThreshold)
                { StepToward(goal, 0f, flat.magnitude > runDistance ? runSpeed : walkSpeed); return true; }
                Coast();
                Vector3 face = activities.Formation == 1 ? activities.Anchor - dogRoot.position : Quaternion.Euler(0, activities.Yaw, 0) * Vector3.forward;
                face.y = 0;
                bool facing = face.sqrMagnitude < .001f || Vector3.Angle(dogRoot.forward, face) <= SleepFaceToleranceDeg;
                if (!facing) FaceTowards(dogRoot.position + face);
                _groupReady = facing && _speed <= stopSpeedThreshold;
                _groupSit = _groupReady && activities.Sit(dogIndex);
                _wantLieDown = _groupReady && !_groupSit;
                return true;
            }
            if (mode == 2 && activities.RaceBall >= 0 && activities.RaceWinner < 0)
            {
                Vector3 ball = activities.balls[activities.RaceBall].transform.position; // sync: local-ok: Owner alone moves toward the networked ball, like Fetch
                _activityBusy = true;
                StepToward(ball, .30f, runSpeed); return true;
            }
            if (mode != 3 || (dogIndex != activities.DogA && dogIndex != activities.DogB)) return false;
            int other = dogIndex == activities.DogA ? activities.DogB : activities.DogA;
            if (!activities.Available(other)) return false;
            _activityBusy = true;
            float t = activities.Elapsed;
            int phase = (int)(t / 6f) % 3;
            _playing = true;
            if (phase == 1)
            {
                Vector3 mid = activities.Anchor + Quaternion.Euler(0, activities.Yaw, 0) * new Vector3(0, 0, 2.5f);
                Vector3 side = Quaternion.Euler(0, activities.Yaw, 0) * Vector3.right;
                Vector3 goal = mid + side * (dogIndex == activities.DogA ? -.65f : .65f);
                StepToward(goal, .12f, walkSpeed);
                if (_speed <= stopSpeedThreshold) { _look = true; FaceTowards(mid); }
            }
            else
            {
                float angle = (t * .65f) + (dogIndex == activities.DogA ? 0 : .65f) + (phase == 2 ? Mathf.PI : 0);
                Vector3 goal = activities.Anchor + Quaternion.Euler(0, activities.Yaw, 0) * new Vector3(Mathf.Sin(angle) * 2f, 0, 2.5f + Mathf.Cos(angle) * 2f);
                StepToward(goal, .12f, phase == 2 ? walkSpeed : runSpeed);
            }
            return true;
        }

    }
}
