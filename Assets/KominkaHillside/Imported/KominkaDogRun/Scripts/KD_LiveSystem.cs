using UdonSharp;
using UnityEngine;
using UnityEngine.Playables;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-PRG-004 Phase 1: **ParticleLive の台帳。** 設計の正本は
    /// [DES-PRG-002](../../../docs/development/issues/DES-PRG-002-particle-live-player.md) §1.1。
    ///
    /// 一文で: **「どの Live が、サーバー時刻いつから」の 1 組を持ち、全員がそこから今の状態を導出する。**
    ///
    /// 再生台帳は `liveId` / `startMs` / `hostId`。Candy の全犬集合には別途
    /// 準備中・世代・準備開始時刻を持つ (LiveSystem.Candy)。**再生中・終了・60 秒待機は
    /// 同期しない** — 全部 `startMs` と作品の長さから各クライアントが計算する
    /// (`vrc-networking` §6「導出できる状態はフラグとして同期しない」)。
    /// だから開催者が退室しても止まらないし (§1.1)、Late Joiner は途中から頭出しできる。
    ///
    /// 時系列 (t = サーバー時刻 - startMs):
    /// <code>
    ///   0 ......... NightIn ............................ NightIn + duration ..... + Cooldown
    ///   夜フェードイン   Timeline 再生 (粒 + 音)          夜フェードアウト / 60 秒待機   次を開催できる
    /// </code>
    ///
    /// 夜の演出そのもの (暗さ・空・木と塀) は <see cref="KD_LiveNight"/> がこの台帳を読んで
    /// ローカルで描く。ここは Timeline の開始・停止と時計だけ。
    ///
    /// **Ownership は「開催するときだけ」動く** (KD_SleepBoard と同じ)。
    /// 開催者が Owner を取って書き、以後戻さない。犬の Ownership は絶対に取らない。
    /// </summary>
    /// <remarks>SyncMode Manual・**専用 GameObject**。</remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public partial class KD_LiveSystem : UdonSharpBehaviour
    {
        public KominkaHillside.KH_HalloweenParade parade;
        public KD_LiveAccess access;
        public KD_GroupActivities activities;
        public KD_ShopItem[] hostingItems;
        // net: callable-ok: hosting permission is independent of viewing and work ownership
        public bool IsLocalAllowedToHost { get { return access != null && access.CanHost(Networking.LocalPlayer); } }
        /// <summary>部屋の管理者か (オーナー優先、不在時だけ Master)。開催者本人でない人が止められる条件。
        /// 判定は `access` → `KD_SleepBoard.CanControl` の 1 か所 (IMP-NET-007)。`access` 未配線なら誰も管理者でない。</summary>
        // net: callable-ok: permission read, no side effects (delegates to the shared judgement)
        public bool CanManage(VRCPlayerApi player) { return access != null && access.CanManage(player); }
        /// <summary><see cref="CanManage"/> の確定直前用 (在席を測り直す)。受信側の受理にだけ使う。</summary>
        // net: callable-ok: side effect is only the presence rescan; no synced value moves
        public bool CanManageNow(VRCPlayerApi player) { return access != null && access.CanManageNow(player); }
        private bool OwnsWork(int id)
        { return hostingItems != null && id >= 0 && id < hostingItems.Length && hostingItems[id] != null && hostingItems[id].Owned(); }
        [Header("配線 (Tools/KominkaDogRun/Live System が設定する)")]
        [Tooltip("作品ごとのステージ (PlayableDirector を持つルート)。添字 = liveId")]
        public PlayableDirector[] stageDirectors;

        [Tooltip("作品ごとのステージのルート。再生中だけ active にする")]
        public GameObject[] stageRoots;

        [Tooltip("作品の長さ (秒)。Setup が Timeline から写す (実行時に duration を読まない)")]
        public float[] stageDurations;

        [Tooltip("作品ごとの夜の暗さ (0 = 昼のまま / 1 = 真っ暗)。**作品ごとに変えられる** (2026-09-06 ユーザー決定)。" +
            "Setup が KD_LiveSetupEditor の品ぞろえ表から写す")]
        public float[] stageDarkness;

        /// <summary>夜へのフェードイン (秒)。この間に木と塀が消え、粒はまだ出ない。</summary>
        public const float NightInSeconds = 3f;

        /// <summary>再生完了から次を開催できるまで (秒)。§1.1 の 60 秒。</summary>
        public const float CooldownSeconds = 60f;

        /// <summary>Timeline 終了後、昼へ戻るフェード (秒)。KD_LiveNight が Elapsed から導く。</summary>
        public const float NightOutSeconds = 3f;

        /// <summary>今の Live。-1 = 何も無い。</summary>
        [UdonSynced] private int liveId = -1;

        /// <summary>夜フェードインが始まったサーバー時刻 (ms)。</summary>
        [UdonSynced] private int startMs;

        /// <summary>開催した人。表示用 (「〇〇さんが開催中」)。</summary>
        [UdonSynced] private int hostId = -1;

        // ---- 開催要求の保留 (KD_SleepBoard と同じ形。SetOwner 直後に同期変数へ書かない) ----
        const float PendingTimeoutSeconds = 1.0f;
        private int _pendingLiveId = -1;
        private bool _hasPending;
        public const int RequestIdle = 0;
        public const int RequestPending = 1;
        public const int RequestStarted = 2;
        public const int RequestRejected = 3;
        public const int RequestUnknown = 4;
        private int _localRequestState;
        // net: callable-ok: ローカル要求の結果を読むだけ。同期値は増やさない
        public int LocalRequestState { get { return activities != null ? activities.LocalRequestState : _localRequestState; } }
        private int _pendingGen;
        private int _timeoutGen;

        // ---- ローカルの再生状態 (同期しない) ----
        private int _playingId = -1;

        // ============================================================
        // 読み取り (副作用なし。**全クライアントが同じ結論を出す**)
        // ============================================================

        /// <summary>今の Live の番号。-1 = 無い (待機中も含む)。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public int CurrentLiveId { get { return liveId; } }

        /// <summary>開催した人の playerId。-1 = 無い。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public int HostId { get { return hostId; } }

        /// <summary>startMs からの経過 (秒)。Live が無ければ負の大きな値。
        /// **サーバー時刻の循環に耐えるよう符号付きの差で見る** (`KD_TreatBox.EarlierMs` と同じ作法)。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public float Elapsed
        {
            get
            {
                if (liveId < 0 || candyPreparing) return -1e9f;
                return (Networking.GetServerTimeInMilliseconds() - startMs) * 0.001f;
            }
        }

        /// <summary>今の Live の長さ (秒)。無ければ 0。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public float CurrentDuration { get { return DurationOf(liveId); } }

        /// <summary>今の Live の夜の暗さの上限 (0〜1)。作品ごとの値。無ければ 1。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public float CurrentDarkness
        {
            get
            {
                if (stageDarkness == null || liveId < 0 || liveId >= stageDarkness.Length) return 1f;
                return Mathf.Clamp01(stageDarkness[liveId]);
            }
        }

        /// <summary>**夜であってほしいか。** フェードインの頭から Timeline の終わりまで true。
        /// <see cref="KD_LiveNight"/> が毎フレーム引く唯一の口。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public bool IsNightWanted
        {
            get
            {
                if (liveId < 0) return false;
                float t = Elapsed;
                return t >= 0f && t < NightInSeconds + DurationOf(liveId);
            }
        }

        /// <summary>Timeline が流れている時間帯か (粒と音が出ている)。
        /// Phase 2 の犬の観客参加がこれを読む。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public bool IsPlaying
        {
            get
            {
                if (liveId < 0) return false;
                float t = Elapsed - NightInSeconds;
                return t >= 0f && t < DurationOf(liveId);
            }
        }

        /// <summary>次を開催できるまでの残り秒。0 = 今すぐ開催できる。
        /// メニューの「次の開催まであと ○ 秒」がこれを出す。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public float SecondsUntilAvailable
        {
            get
            {
                if (DogRecovering) return 1f;
                if (candyPreparing) return Mathf.Max(.1f, CandyGatherSeconds - CandyPreparationElapsed);
                if (liveId < 0) return 0f;
                float remain = NightInSeconds + DurationOf(liveId) + CooldownSeconds - Elapsed;
                return remain > 0f ? remain : 0f;
            }
        }

        /// <summary>今すぐ開催してよいか (§1.1: 現在の 1 本を保護し、完了後 60 秒は誰も開催できない)。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public bool CanStart { get { return (parade == null || (!parade.BlocksNewUses && !parade.HasRecoveryFailure)) && SecondsUntilAvailable <= 0f && (activities == null || !activities.Busy); } }

        // ============================================================
        // 書き込み (**ローカル専用。`_` はセキュリティの印**)
        // ============================================================

        /// <summary>開催する。**台帳の Owner を取るのはここだけ。**
        /// 所有判定 (Point / CE) は Phase 4 でメニュー側が済ませてから呼ぶ。</summary>
        public void _StartLive(int id)
        {
            if (activities != null) { activities._Request(4, id, activities.localFormation); return; }
            if (_hasPending) return;
            _localRequestState = RequestRejected;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (id < 0 || stageDirectors == null || id >= stageDirectors.Length) return;
            if (!IsLocalAllowedToHost || !OwnsWork(id)) return;
            if (!CanStart) return;           // 現在の 1 本を保護 + 60 秒待機

            if (Networking.IsOwner(gameObject))
            {
                _hasPending = false;
                Begin(id, lp.playerId);
                return;
            }

            // **SetOwner の直後に同期変数へ書かない** (KD_SleepBoard と同じ処置)
            _pendingLiveId = id;
            _hasPending = true;
            _localRequestState = RequestPending;
            _pendingGen++;
            Networking.SetOwner(lp, gameObject);   // net: setowner-ok: Live の台帳。犬ではない
            SendCustomEventDelayedSeconds(nameof(_PendingTimeout), PendingTimeoutSeconds);
        }

        /// <summary>開催者本人か管理者が途中で止める (Candy の準備取消 `KD_CandyDogPerformance._Cancel` もここへ来る)。
        /// 止めた時点から 60 秒待機に入る (`startMs` を「今 - 全長」へ寄せる)。
        /// **フェードイン中か再生中にだけ効く** (Codex 指摘 2026-09-06: 終了後に押すと待機が延びる)。
        ///
        /// 本番は `activities` 経由の認証済み要求 (<see cref="_StopFor"/>) で、認可は開催者本人または管理者。
        /// `activities == null` の直書き経路は **共有開催を配線しないプレビューシーン (Candy / Pumpkin の
        /// リハーサル) 専用**で、本番シーンでは Live System の Verify が `activities` の配線を要求する。
        /// 直書き経路でも認可は同じ規則 — Object Owner であることは書ける条件であって管理権限の代わりではない (IMP-NET-007)。</summary>
        public void _StopLive()
        {
            if (activities != null) { activities._Request(6, 0, 0); return; }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (liveId < 0 || (!IsNightWanted && !candyPreparing)) return;
            if (lp.playerId != hostId && !CanManageNow(lp)) return;   // 開催者本人または管理者 (確定直前なので在席を測り直す)
            if (!Networking.IsOwner(gameObject)) return;           // 単一書き手 (要求経路が無い構成では Owner しか書けない)
            if (candyPreparing) { CancelCandyPreparation(); return; }
            StopDogPerformance();
            int total = Mathf.RoundToInt((NightInSeconds + DurationOf(liveId)) * 1000f);
            startMs = Networking.GetServerTimeInMilliseconds() - total;
            RequestSerialization();
        }

        /// <summary>**再生中・待機中は Owner を渡さない** (Codex 指摘 2026-09-06)。
        /// A の開始が届く前に B が押すと、B の古い同期値では `CanStart` が true なので Owner を取りに来る。
        /// 現 Owner は最新の状態で断れる = 開始済みの Live が上書きされない。</summary>
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            if (activities != null) return false; // shared authority uses authenticated requests, never a start-time ownership race
            return CanStart && access != null && access.CanHostNow(requestedOwner);
        }

        /// <summary>移譲待ちの間に他人の開始が届いたら、自分の保留は捨てる (上と対の保険)。</summary>
        public override void OnDeserialization()
        {
            if (_hasPending && !CanStart)
            {
                _hasPending = false;
                _localRequestState = RequestRejected;
            }
        }

        public void _PendingTimeout()
        {
            _timeoutGen++;
            if (_timeoutGen < _pendingGen) return;
            if (!_hasPending) return;
            _hasPending = false;
            _localRequestState = RequestUnknown;
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || !CanStart) return;
            Begin(_pendingLiveId, lp.playerId);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            ResetCandyReadiness();
            if (!_hasPending) return;
            _hasPending = false;
            _localRequestState = RequestRejected;
            if (!CanStart) return;
            Begin(_pendingLiveId, player.playerId);
        }

        private void Begin(int id, int host)
        {
            // 確定直前なので在席を測り直す (代行 Master の保留中にオーナーが戻れば、開放 OFF の開始は通さない)
            if (access == null || !access.CanHostNow(Networking.LocalPlayer) || !OwnsWork(id) || !CanStart) { _localRequestState = RequestRejected; return; }
            if (DogPerformanceOf(id) != null)
            {
                _localRequestState = BeginCandyPreparation(id, host) ? RequestStarted : RequestRejected;
                return;
            }
            _localRequestState = RequestStarted;
            liveId = id;
            hostId = host;
            startMs = Networking.GetServerTimeInMilliseconds();
            RequestSerialization();
        }

        public bool _BeginFor(VRCPlayerApi caller, int id)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(caller) || access == null || !access.CanHostNow(caller)   // 受理直前は在席を測り直す
                || !CanStart || hostingItems == null || id < 0 || id >= hostingItems.Length || hostingItems[id] == null
                || !hostingItems[id].OwnedBy(caller) || stageDirectors == null || id >= stageDirectors.Length || stageDirectors[id] == null) return false;
            if (DogPerformanceOf(id) != null) return BeginCandyPreparation(id, caller.playerId);
            liveId = id; hostId = caller.playerId; startMs = Networking.GetServerTimeInMilliseconds();
            if (caller.isLocal) _localRequestState = RequestStarted;
            RequestSerialization(); return true;
        }

        /// <summary>認証済みの caller による停止 (`KD_GroupActivities` op 6)。**開催者本人または管理者**だけ受理する。
        /// 管理者は書く直前に在席を測り直して判定する (`CanControlNow`) — 代行 Master の要求が
        /// オーナー復帰の直後に届いても受理しない。</summary>
        public bool _StopFor(VRCPlayerApi caller)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(caller) || (!IsNightWanted && !candyPreparing)
                || (caller.playerId != hostId && !CanManageNow(caller))) return false;
            if (candyPreparing) { CancelCandyPreparation(); return true; }
            StopDogPerformance();
            startMs = Networking.GetServerTimeInMilliseconds() - Mathf.RoundToInt((NightInSeconds + DurationOf(liveId)) * 1000f);
            RequestSerialization(); return true;
        }

        // ============================================================
        // 毎フレーム: 同期値から「あるべき姿」を作り直す (差分ではなく突き合わせ)
        // ============================================================

        void Start()
        {
            int n = stageDirectors != null ? stageDirectors.Length : -1;
            if (n <= 0 || stageRoots == null || stageRoots.Length != n
                || stageDurations == null || stageDurations.Length != n)
            {
                Debug.LogError("[KD_LiveSystem] stageDirectors / stageRoots / stageDurations が揃っていない ("
                    + n + ") - Run Tools/KominkaDogRun/Live System setup.");
                return;
            }
            // 暗さは欠けても止めない (CurrentDarkness が 1 へ倒れる) が、黙らせもしない (IMP-PRG-005)
            if (stageDarkness == null || stageDarkness.Length != n)
                Debug.LogError("[KD_LiveSystem] stageDarkness の長さが作品数と違う ("
                    + (stageDarkness == null ? -1 : stageDarkness.Length) + " / " + n
                    + ") - 範囲外の曲は真っ暗 (1.0) になる。Run Tools/KominkaDogRun/Live System setup.");
            for (int i = 0; i < n; i++)
            {
                if (stageRoots[i] != null) stageRoots[i].SetActive(false);
                if (stageDirectors[i] != null) stageDirectors[i].playOnAwake = false;
            }
        }

        void Update()
        {
            // IMP-DOG-036 S5: no live chosen and nothing playing = the lines below do nothing: CandyPreparing and the mount need
            // DogPerformanceOf(liveId) != null (null for liveId < 0, so candyPreparing alone cannot start anything), want stays -1
            // and equals _playingId. Same result without the two IsOwner calls.
            if (liveId < 0 && _playingId < 0) return;
            // 2026-10-07 audit RC-13: 60 秒待機まで明けたら Owner が台帳を空 (-1) に戻す (上の休眠の早期 return を効かせる)。この時点で
            // SecondsUntilAvailable は既に 0 なので待機の判定は変わらない。準備中 (Elapsed = -1e9) と犬の復帰中は戻さない
            if (liveId >= 0 && !candyPreparing && !DogRecovering && Elapsed >= NightInSeconds + DurationOf(liveId) + CooldownSeconds
                && Networking.IsOwner(gameObject)) { liveId = -1; RequestSerialization(); }
            UpdateCandyAuthority();
            UpdateMountAuthority();
            var dogPerformance = DogPerformanceOf(liveId);
            if (dogPerformance != null) dogPerformance._ServiceLiveRecovery();
            int want = -1;
            float offset = 0f;
            if (liveId >= 0 && stageDirectors != null && liveId < stageDirectors.Length)
            {
                float t = Elapsed - NightInSeconds;
                if (t >= 0f && t < DurationOf(liveId)) { want = liveId; offset = t; }
            }
            if (want == _playingId) return;

            // 止める (別の作品へ切り替わった / 終わった)
            if (_playingId >= 0)
            {
                PlayableDirector old = stageDirectors[_playingId];
                if (old != null) old.Stop();
                if (stageRoots[_playingId] != null) stageRoots[_playingId].SetActive(false);
                _playingId = -1;
            }
            if (want < 0) return;

            // 始める。**頭出しは開始・合流のときだけ** (毎フレーム time を書かない)
            PlayableDirector d = stageDirectors[want];
            if (d == null) return;
            if (stageRoots[want] != null) stageRoots[want].SetActive(true);
            d.time = offset;
            d.Play();
            _playingId = want;
        }

        private float DurationOf(int id)
        {
            if (stageDurations == null || id < 0 || id >= stageDurations.Length) return 0f;
            return stageDurations[id];
        }
    }
}
