using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>Hosting permission only. Each participant must still own the requested work.
    ///
    /// **誰が設定を変えられるか**は <see cref="KD_SleepBoard.CanControl"/> (オーナー優先、不在時だけ Master)
    /// に問い合わせる (IMP-NET-007 / [インスタンスの管理権限](../../../docs/development/design/instance-permissions.md))。
    /// ここに `isMaster` を書かない。<see cref="sleepBoard"/> が未配線なら誰も変更できず、開催は開放時だけ —
    /// 参照欠落を Master 判定で埋めず、Menu Finish の Setup / Verify で数える。
    ///
    /// Ownership は許可された設定操作のときだけ取り、取得確認と権限の再確認 (<see cref="KD_SleepBoard.CanControlNow"/>)
    /// の後に書く。代行 Master の取得待ち中にオーナーが戻れば保留は捨てる。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_LiveAccess : UdonSharpBehaviour
    {
        [Tooltip("共通の管理者判定 (Tools/KominkaDogRun/Menu Finish が配線する)。未配線なら設定を変更できない")]
        public KD_SleepBoard sleepBoard;
        [UdonSynced] private bool openToAll;
        private bool _pending;
        private bool _target;
        private float _deadline;
        // net: callable-ok: shared permission read
        public bool IsOpenToAll { get { return openToAll; } }
        // net: callable-ok: local pending state read
        public bool Pending { get { return _pending; } }
        // net: callable-ok: permission check, no side effects (delegates to the shared judgement)
        public bool CanManage(VRCPlayerApi player)
        { return sleepBoard != null && sleepBoard.CanControl(player); }
        // net: callable-ok: side effect is only the presence rescan; used right before a write or an authoritative accept
        public bool CanManageNow(VRCPlayerApi player)
        { return sleepBoard != null && sleepBoard.CanControlNow(player); }
        // net: callable-ok: local read of the shared judgement (settings UI)
        public bool IsLocalManager { get { return CanManage(Networking.LocalPlayer); } }
        // net: callable-ok: permission check, no side effects (UI / cached presence)
        public bool CanHost(VRCPlayerApi player)
        { return Utilities.IsValid(player) && (openToAll || CanManage(player)); }
        // net: callable-ok: same rule with a presence rescan; for the authoritative start accept only (Codex 2026-09-16)
        public bool CanHostNow(VRCPlayerApi player)
        { return Utilities.IsValid(player) && (openToAll || CanManageNow(player)); }

        public void _SetOpen(bool value)
        {
            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player) || !CanManageNow(player)) return;
            _target = value;
            if (value == openToAll) { _pending = false; return; }
            if (Networking.IsOwner(gameObject)) { Apply(); return; }
            _pending = true; _deadline = Time.time + 5f;
            Networking.SetOwner(player, gameObject); // net: setowner-ok: separate hosting permission, never a dog or the playing live
        }
        private void Update()
        {
            if (!_pending) return;
            var player = Networking.LocalPlayer;
            // 取得待ちの間に権限を失った (オーナーが戻った / Master が交代した) 操作は破棄する
            if (!CanManage(player) || Time.time >= _deadline) { _pending = false; return; }
            if (Networking.IsOwner(gameObject)) Apply();
        }
        // 要求側と現 Owner 側の両方で評価される。現 Owner の在席キャッシュが古いと復帰したオーナーの要求を拒否し、
        // 再要求も無いので操作がタイムアウトする — 承認も測り直してから答える (Codex 2026-09-16)
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        { return CanManageNow(requestedOwner); }
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        { if (_pending && Utilities.IsValid(player) && player.isLocal) Apply(); }
        /// <summary>書く直前に Object Owner と管理権限の両方を見直す。権限が無ければ保留ごと捨てる。</summary>
        private void Apply()
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!CanManageNow(Networking.LocalPlayer)) { _pending = false; return; }
            _pending = false; openToAll = _target; RequestSerialization();
        }
    }
}
