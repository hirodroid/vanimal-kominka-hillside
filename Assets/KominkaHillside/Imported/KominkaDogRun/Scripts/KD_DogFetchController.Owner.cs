using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    /// <summary>KD_DogFetchController の partial (Owner)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_DogFetchController
    {
        // ================================================================ IMP-NET-005

        /// <summary>犬ユニットの Owner か。**見るのは常に dogRoot の Owner**
        /// (DES-NET-002 不変条件 8)。
        ///
        /// 自分の <c>gameObject</c> (`KD_FetchSystem_*`) を見てはいけない —
        /// dogRoot に `VRCObjectSync` が載ったことで Ownership の軸が 2 本になり、
        /// 「状態を決める人」と「位置を書く人」が割れると却下理由 (iv) が形を変えて再発する。
        /// 軸を割らないための規律は「**犬側で SetOwner を一度も呼ばない**」の 1 行で、
        /// それによって dogRoot も兄弟もインスタンスマスターに揃ったまま一緒に移る。</summary>
        bool IsDogOwner()
        {
            // VER-ENV-002 C wave 2: the two GameObjects are read once (Component.gameObject and the Unity null test are externs, and
            // this runs every frame and from every request). dogRoot is wired in the Editor and never reassigned at runtime, so the
            // cached answer is the one the old expression gave. Lazy (not Start) because a request can arrive before Start.
            if (_ownerGoCached) return _dogRootOk && Networking.IsOwner(_dogRootGo);
            CacheOwnerObjects();
            return _dogRootOk && Networking.IsOwner(_dogRootGo);
        }

        bool _ownerGoCached, _dogRootOk;
        GameObject _dogRootGo, _selfGo;

        void CacheOwnerObjects()
        {
            _ownerGoCached = true;
            _dogRootOk = dogRoot != null;
            _dogRootGo = _dogRootOk ? dogRoot.gameObject : null;
            _selfGo = gameObject;
        }

        /// <summary>Ownership の軸が割れていたら**一度だけ**警告する。
        ///
        /// 同期変数を書けるのは `KD_FetchSystem_*` の Owner だけなので、そこが
        /// dogRoot の Owner と食い違うと**「状態を決める人」の書き込みが黙って落ちる**。
        /// 隠すのではなく晒す — 起きたら必ず原因 (どこかに残った `SetOwner`) がある。</summary>
        void WarnIfOwnerSplit()
        {
            if (_ownerSplitWarned) return;
            // VER-ENV-002 C wave 2: the cached own GameObject (Update calls IsDogOwner just before, which fills it)
            if (_ownerGoCached) { if (_dogOwner == Networking.IsOwner(_selfGo)) return; }
            else if (_dogOwner == Networking.IsOwner(gameObject)) return;
            _ownerSplitWarned = true;
            Debug.LogError("[KD_DogFetchController] Ownership の軸が割れている (IMP-NET-005 不変条件 8): "
                + "dogRoot=" + OwnerName(dogRoot.gameObject)
                + " / " + gameObject.name + "=" + OwnerName(gameObject)
                + " — 犬側で SetOwner を呼んでいる箇所を探すこと。");
        }

        string OwnerName(GameObject go)
        {
            VRCPlayerApi o = Networking.GetOwner(go);
            return Utilities.IsValid(o) ? o.displayName : "(none)";
        }

        /// <summary>非 Owner の実速度を、**観測した Transform 差分**から測る (IMP-NET-005 A2)。
        ///
        /// Run/Walk は元々「状態ではなく実速度から決める」が方針なので、
        /// <see cref="_speed"/> の入力をシミュレーション値から観測値へ差し替えるだけで
        /// <see cref="UpdateLocomotionAnim"/> は 1 行も変わらない。
        /// シミュレーション値のまま残すと、**位置はネットワーク由来なのに脚は自前の予測**
        /// という食い違いが出て、BUG-DOG-001 の「静止ポーズのまま滑る」が非 Owner 側で再発する。
        ///
        /// ワープ (`VRCObjectSync` の補正・Late Join・リスポーン) を速度に数えないのは
        /// `KD_DogPack` / `KD_DogStopAnchor` と同じ作法。</summary>
        void ObserveSpeed()
        {
            Vector3 pos = dogRoot.position;
            float dt = Time.deltaTime;
            if (_obsPrevValid && dt > 0.0001f)
            {
                Vector3 moved = pos - _obsPrevPos;
                moved.y = 0f;
                float m = moved.magnitude;
                _speed = (m <= ObservedTeleportThreshold) ? m / dt : 0f;
            }
            else
            {
                _speed = 0f;
            }
            _obsPrevPos = pos;
            _obsPrevValid = true;
        }

        // ---- 遷移と状態適用 ----
        // 時間で進む遷移 (ChaseStart/Pickup/Drop) は SendCustomEventDelayedSeconds ではなく
        // Update の経過時間判定で行う: 遅延イベントは引数を持てず「古いタイマー」を厳密に
        // 識別できない (Codex 指摘)。Update 判定なら世代問題が構造的に消え、
        // Phase 2 の Owner 移譲でも再スケジュールが不要になる
        // (新 Owner は OnDeserialization で _stateEnteredTime を取り直すだけでよい)

        void TransitionTo(int next)
        {
            // Codex review 2026-09-28 中: a hold that goes on (4 / 7 / 8 / Drop -> another of them) keeps its loss clock
            bool holdGoesOn = IsHoldState(dogState) && IsHoldState(next);
            dogState = next;
            _stateEnteredTime = Time.time;
            _prevBallValid = false; // テレポート検知は状態を跨いで比較しない (前回値の持ち越しで誤検知する)
            _settleStartTime = -1f; // 着地点探しの計測は Return ごとに取り直す

            // **投擲の観測履歴も状態を跨いで比較しない** (破壊的監査 v2 RC-5)。
            // 残すと Fetch 一周後の Idle 1 フレーム目に「投げ地点 → 置き地点」の差分を
            // 1 フレームの dt で割り、毎 Fetch 後 2 秒間の偽証拠が立つ。
            // 保留中の開始要求も状態が動いた時点で無効 (要求は Idle でしか意味を持たない)
            _ballObsValid = false;
            _ballPrevMoveTime = -1000f;
            _ballLastMovingTime = -1000f;
            _fetchReqPending = false;
            _ballReleaseTime = -1000f; // 弱い放しの証拠も消費する (IMP-FET-015)
            // 今の保持状態を引き継ぐ (口から取られて Idle へ戻った直後の放しを取りこぼさない — Codex 指摘)
            _ballHeldPrev = _ballPickup != null && _ballPickup.IsHeld;
            // RC-17 (破壊監査 2026-09-28): a claim belongs to the state it was asked from; a hold watch to the carry it watched
            _claimNext = -1;
            if (!holdGoesOn) _holdLostAt = -1f;
            // IMP-DOG-021: the river-band wait belongs to one Chase; the longer land Return limit to one river fetch
            RiverBlockClear();
            if (next == StateChaseStart || next == StateIdle) _returnTimeoutExtra = 0f;

            if (next == StateReturn || next == StateWaterReturn)   // IMP-DOG-020: the water carry takes the ball the same way
            {
                // くわえた: 物理を止める (VRCObjectSync 経由でないと巻き戻される)。書けるのはボールの Owner だけ。
                // RC-17: 3 -> 4 and 6 -> 7 come only through BeginCarry, i.e. after the ball's ownership is confirmed; 8 -> 4
                // keeps the frozen ball. Asking for ownership here and writing at once (the old way) was the bug: a delayed or
                // refused transfer left 4 / 7 "holding" a dynamic ball. HoldTick repairs a lost or unfrozen ball from here on.
                if (_ballSync != null && BallOwned()) _ballSync.SetKinematic(true);
            }
            else if (next == StateDrop)
            {
                // すぐ放さない: 到着して dropDelay 経ってから (受け渡しの「間」)。
                // 放す処理は Update の Drop 分岐で行う
                _ballReleased = false;
            }
            else if (next == StateIdle)
            {
                attentionPlayerId = -1; // Drop で -1 へ戻す (設計書 §2)
                targetObjectId = -1;
                petState = -1;          // Idle へ戻った時点では誰も撫でていない (IMP-NET-001)
            }

            RequestSerialization();
            ApplyState(); // Owner には OnDeserialization が来ないため自分でも適用する
        }

        /// <summary>ボールに届かない・対象が居ない・取り上げられた、のときの復帰。</summary>
        void GiveUp()
        {
            // 誰かが掴んでいる最中は物理を触らない (破壊的監査 v2 RC-12) —
            // 保持者の Pickup と Ownership・kinematic を奪い合うことになる。
            // kinematic が残った場合は Idle 中の出しっぱなし保険が実態と突き合わせて放す
            bool held = BallHeld();
            if ((dogState == StateReturn || dogState == StateDrop || dogState == StateWaterReturn || dogState == StateWaterExit) && !held) ReleaseBall();
            TransitionTo(StateIdle);
        }

        // ---- RC-17 (破壊監査 2026-09-28, FUNC-05 BUG-01 / FUNC-06 BUG-04): the ball is held only once it is ours ----
        // Pickup (3) -> Return (4) and WaterChase (6) -> WaterReturn (7) wait in their own state until the ball's Owner is this
        // client, then freeze it and move on. Asking again once a second (never every frame), for at most BallClaimTimeout;
        // not ours by then = GiveUp from 3 / 6, which does not touch the physics of a ball someone else owns. A person picking
        // the ball up while we wait wins as before (the held check -> GiveUp). While carrying (4 / 7 / 8, Drop before the
        // release) HoldTick keeps checking: ours but not frozen -> freeze again; lost -> ask again, not back in time -> GiveUp.
        // Owner-local; nothing synced. The harness (Course/88 R17-*) delays / refuses the ownership through the _debug* fields.
        const float BallClaimTimeout = 2f, BallClaimRetry = 1f;
        int _claimNext = -1;            // the state waiting for the ball (4 / 7); -1 = none
        float _claimAt, _claimSentAt = -1000f, _holdLostAt = -1f;
        float _debugClaimDelay;         // harness: the next claim sees the ball as not ours for this long (one shot)
        bool _debugClaimRefuse;         // harness: the ball is never ours (claim refused / ownership lost while carrying)
        bool _debugBallHeld;            // harness: stands in for a person holding the ball (the Fetch's own held checks only)
        float _debugClaimUntil;

        bool BallOwned()
        {
            if (_ballT == null || _debugClaimRefuse || Time.time < _debugClaimUntil) return false;
            return Networking.IsOwner(_ballT.gameObject);
        }

        bool BallHeld() { return _debugBallHeld || (_ballPickup != null && _ballPickup.IsHeld); }

        /// <summary>Ask for the ball's ownership, at most once per BallClaimRetry.</summary>
        void RequestBall()
        {
            if (_ballT == null || Time.time - _claimSentAt < BallClaimRetry) return;
            _claimSentAt = Time.time;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp) && !Networking.IsOwner(_ballT.gameObject)) Networking.SetOwner(lp, _ballT.gameObject); // net: setowner-ok: the dog's own ball, never a dog; retried once a second
        }

        /// <summary>Owner, from 3 (next = 4) or 6 (next = 7), every frame until it settles: the dog has reached the ball.</summary>
        void BeginCarry(int next)
        {
            if (_claimNext < 0)
            {
                _claimNext = next; _claimAt = Time.time;   // _claimSentAt is kept: once a second across claims and holds (Codex 中)
                _debugClaimUntil = _debugClaimDelay > 0f ? Time.time + _debugClaimDelay : 0f; _debugClaimDelay = 0f;
                if (!BallOwned()) Debug.Log("[KD_DogFetchController] ball claim wait dog=" + dogIndex + " state=" + dogState + " next=" + next);
            }
            ClaimTick();
        }

        void ClaimTick()
        {
            if (_ballT == null) { _claimNext = -1; GiveUp(); return; }
            if (BallOwned())
            {
                int next = _claimNext;
                float waited = Time.time - _claimAt;
                _claimNext = -1;
                if (_ballSync != null) _ballSync.SetKinematic(true);
                if (waited > 0.05f) Debug.Log("[KD_DogFetchController] ball claim ok dog=" + dogIndex + " next=" + next + " after=" + waited.ToString("F2"));
                TransitionTo(next);
                return;
            }
            if (Time.time - _claimAt > BallClaimTimeout)
            {
                Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState + " reason=claim-timeout");
                _claimNext = -1;
                GiveUp();
                return;
            }
            RequestBall();
        }

        /// <summary>Owner, every frame the ball is in the mouth (4 / 7 / 8, Drop before the release). True = gave up.</summary>
        bool HoldTick()
        {
            if (BallOwned())
            {
                if (_holdLostAt >= 0f) { _holdLostAt = -1f; Debug.Log("[KD_DogFetchController] ball ownership back dog=" + dogIndex + " state=" + dogState); }
                if (_ballSync != null && _ballRb != null && !_ballRb.isKinematic) _ballSync.SetKinematic(true);   // a freeze lost in a handover
                return false;
            }
            if (_holdLostAt < 0f)
            {
                _holdLostAt = Time.time;
                Debug.Log("[KD_DogFetchController] ball ownership lost dog=" + dogIndex + " state=" + dogState);
            }
            if (Time.time - _holdLostAt > BallClaimTimeout)
            {
                Debug.Log("[KD_DogFetchController] give-up dog=" + dogIndex + " state=" + dogState + " reason=ball-lost");
                GiveUp();
                return true;
            }
            RequestBall();
            return false;
        }

        /// <summary>The ball is in the dog's mouth now (read by KH_ToyRecall, the ball's Owner): 4 / 7 / 8, and Drop until the
        /// release. RC-09: Drop after the release is not a hold (the old IsCarrying covered all of Drop, so the ball's gravity
        /// waited for Idle, FUNC-05 BUG-04). The dog Owner knows the release itself; another client uses the same timing as the
        /// LateUpdate glue.</summary>
        public bool _BallInMouth()
        {
            if (dogState == StateReturn || dogState == StateWaterReturn || dogState == StateWaterExit) return true;
            if (dogState != StateDrop) return false;
            if (_dogOwner && IsDogOwner()) return !_ballReleased;
            return Time.time - _stateEnteredTime < dropWaitDuration + dropDelay;
        }

        bool IsHoldState(int s) { return s == StateReturn || s == StateWaterReturn || s == StateWaterExit || s == StateDrop; }

        /// <summary>This dog has ball t in its mouth now (KH_ToyRecall: a kinematic ball no dog carries is unfrozen by its Owner;
        /// a race can bind another dog's ball, so the ball is compared, not the row).</summary>
        public bool _Carries(Transform t) { return t != null && _ballT == t && _BallInMouth(); }

        bool BallFloatsHere()
        {
            if (_ballT == null) return false;
            KD_DogWanderController w = MyWander();
            if (w != null && w.waterSpot != null && w.waterSpot._FloatsAt(_ballT.position)) return true;
            return riverData != null && riverData._FloatsAt(_ballT.position);   // IMP-DOG-021 (E-2): anywhere along the river
        }

        /// <summary>Preview-only caller opts this dog into the show before cancelling its work.</summary>
        public void _EndForCandyPerformance()
        {
            if (!IsDogOwner() || pack == null || pack.dogWander == null
                || dogIndex < 0 || dogIndex >= pack.dogWander.Length
                || pack.dogWander[dogIndex] == null || !pack.dogWander[dogIndex].candyPerformanceActive) return;
            GiveUp();
            if (petController != null) petController._SuppressPetting();
        }

        void ReleaseBall()
        {
            // 物理状態を書けるのはボールの Owner だけ。通常は 犬の Owner = 投げた本人 =
            // ボールの Owner (Pickup が移す) だが、旧 Owner の退室で犬だけ引き継いだ場合は
            // ずれる — そのときはボールの Ownership も取る (移譲確認を待たずに書く唯一の
            // 妥協点。効かなければボールが口の位置に浮いて残る → 実機 2 人で要確認)
            // RC-17 (Codex review 2026-09-28 高): only the ball's Owner writes its physics. Not ours = leave it and do not ask for
            // it (the old "take it and write at once" kept a frozen ball and a SetOwner every second from the Idle safety net).
            // The ball's current Owner unfreezes a kinematic ball that no dog carries (KH_ToyRecall.CheckRiverBalls, _Carries).
            if (_ballT == null || !Networking.IsOwner(_ballT.gameObject)) return;
            if (_ballSync != null)
            {
                _ballSync.SetKinematic(false);
                // RC-09 (FUNC-05 BUG-04): gravity follows where the ball is let go, at once, not at the next Idle. On land it
                // falls; in the water-play spot it floats again (KH_ToyRecall holds it there from its next look)
                _ballSync.SetGravity(!BallFloatsHere());
            }
            if (_ballRb != null)
            {
                _ballRb.velocity = Vector3.zero;
                _ballRb.angularVelocity = Vector3.zero;
            }
        }

        public override void OnDeserialization()
        {
            // 注意: dogState が変わっていない受信でもここは走る (状態比較はしていない)。
            // 非 Owner しか受信しないため Owner のタイムアウト基準には影響しない
            _stateEnteredTime = Time.time;
            ApplyState();
        }

        /// <summary>同期変数から表示 (Animator / 撫で抑制 / pickupable) を導出する。
        /// Owner の遷移直後と OnDeserialization の両方から呼ばれる。</summary>
        void ApplyState()
        {
            BindRaceBall();
            // Drop へ入った変化を検出して撮影へ通知する (Owner の遷移直後と
            // 非 Owner の OnDeserialization の両方で、各クライアントが一度ずつ)。
            // 初回適用 (Start / Late Joiner の復元) は「遷移」ではないので撮らない。
            // 非 Owner が中間状態を受信し損ねた場合の取りこぼしは許容 —
            // Drop 滞在は約 2.7 秒 (wait+delay+duration) あり、Manual 同期は遷移ごとに
            // 送っているため実運用ではまず落ちない (Codex 指摘への回答)
            bool enteredDrop = _stateAppliedOnce && dogState == StateDrop && _lastAppliedState != StateDrop;
            bool enteredChase = _stateAppliedOnce && dogState == StateChase && _lastAppliedState != StateChase;
            // **Idle の畳み込みも「変化したときだけ」** (破壊的監査 v2 RC-11 = 旧 RC-4b)。
            // IMP-NET-001 以降、撫での成立/解除のたびに 4 変数まとめて再送されるため、
            // 無条件に畳むと **dogState が Idle のままの受信のたびに** 全非 Owner で
            // Run/Walk が false に落ちる。Wander は変化検出で書くので書き直されず、
            // 徘徊中の犬が静止ポーズのまま滑る (前回監査 4b。当時は撫で同期の方式が
            // 未確定で保留 → Req 方式への変更で依存が解消し、今回実施)。
            // 初回適用 (Late Joiner の復元) は畳む — それが本来の目的
            bool enteredIdle = dogState == StateIdle
                && (!_stateAppliedOnce || _lastAppliedState != StateIdle);
            if (!_stateAppliedOnce || _lastAppliedState != dogState) _locomotionOutputValid = false;
            _lastAppliedState = dogState;
            _stateAppliedOnce = true;
            // IMP-CAM-002: **どの犬のイベントかを渡す。** 撮影は 1 個のままなので、
            // 名簿の番号 (dogIndex — Dog Pack Setup が配線する) がそのまま
            // 撮影側の犬配列の添字になる。渡さないと 4 匹ぶんのイベントが
            // 区別できず、撮影が常に同じ 1 匹を写す
            if (photoDirector != null)
            {
                if (enteredDrop) photoDirector._OnDogDrop(dogIndex);
                if (enteredChase) photoDirector._OnDogChase(dogIndex); // 追跡ドローンの 1 枚
            }

            // Run/Walk/Pickup は状態からではなく **実速度から** 毎フレーム導出する
            // (UpdateLocomotionAnim)。状態遷移の瞬間に切り替えると、まだ減速中の犬の
            // 脚が先に止まって滑る (2026-08-12 Play フィードバック)。
            // ここで書くのは Idle へ戻ったときの畳み込みだけ — Idle 中は Update が
            // 早期 return するため、誰も Bool を戻さなくなる
            if (enteredIdle)
            {
                // Both owner and receivers reset animation here. The hillside layer must reapply
                // its output even if its movement classification stayed the same across Fetch.
                if (hillsideLeashes != null)
                {
                    KD_DogWanderController wander = MyWander();
                    if (wander != null) wander._InvalidateLeashAnimation();
                }
                _speed = 0f;
                // 保持も畳む。Idle 中は Update が早期 return するので、残すと
                // 「Wander が動かしているのに Fetch の古い保持が残る」状態になる
                _stopHoldUntil = 0f;
                _animMoving = false;
                if (dogAnimator != null)
                {
                    // Bool は一度に 1 つ (排他はスクリプト側の責務 — 設計書 §5 Controller 構成)
                    dogAnimator.SetBool("Run", false);
                    dogAnimator.SetBool("Walk", false);
                    dogAnimator.SetBool("Pickup", false);
                }
            }

            // Fetch 中の撫では無視 (設計書 §1)。Drop で Idle に戻った瞬間から受け付ける。
            // **Candy 演目中は Idle でも再開しない** (破壊的監査 v4 FUNC-02/BUG-01) — 演目の開始そのものが
            // Owner の GiveUp → Idle 再送を起こし、非 Owner だけがここで抑制を解かれていた。
            // 再開は徘徊層の UpdateCandyPerformance が演目・着地・退場の終わりに全クライアントで行う
            if (petController != null)
            {
                if (dogState != StateIdle) petController._SuppressPetting();
                else if (CandyBusy()) MyWander()._SuppressPettingForCandy();
                // IMP-DOG-005: the disc chase owns the head. IMP-DOG-013: read the frisbee's own rule instead of
                // IsEngaged - Drop (brought the disc back, standing in front of the thrower) is engaged for every
                // other system and yet is exactly where 撫で belongs, so both controllers resume on one predicate.
                else if (frisbeeCatch != null && !frisbeeCatch.PetAllowed) petController._SuppressPetting();
                else petController._ResumePetting();
            }

            // Fetch 中はボールを掴めない (pickupable はローカルプロパティ)。
            // IMP-FET-014: **Drop (くわえて待つ) の間だけ、投げた本人のクライアントでは掴める** —
            // 口から受け取る操作。他人の画面では従来どおり Idle まで掴めない (不変条件 4)。
            // 掴まれた先の処理は既存の IsHeld → GiveUp (Owner) が引き受ける
            if (_ballPickup != null && (activities == null || !activities.BallClaimed(dogIndex, dogIndex)))
                _ballPickup.pickupable = dogState == StateIdle
                    || (dogState == StateDrop && attentionPlayerId >= 0 && IsLocalPlayerId(attentionPlayerId));
        }

        /// <summary>そのプレイヤー ID がこのクライアントの本人か (IMP-FET-014 の pickupable 判定)。</summary>
        bool IsLocalPlayerId(int playerId)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) && lp.playerId == playerId;
        }

        // ---- 移動・旋回 ----
        // **IMP-NET-005: 計算して書くのは Owner だけ。** 旧方式は「全クライアントが
        // 同期変数から同じ計算をする」だったが、同じ入力でも開始時刻・fps・経路の差で
        // 軌道が割れ、収束させる瞬間が滑り・ワープとして見えていた (DES-NET-002)。
        // 非 Owner はここで位置を書かず、VRCObjectSync が配る実位置を観測して脚を選ぶ

    }
}
