using UdonSharp;
using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>DES-WLD-004 M1: おさんぽカート 1 台 (本体 GameObject。VRCStation と同居)。同期しない — 姿勢は KH_CartManager が毎フレーム書く。
    ///
    /// - 着座は KH_CartManager の割当 (cartRider == 自分) からだけ: <see cref="_Board"/> が station.UseStation(local)。
    ///   クリック着座は Start() の DisableInteractive = true で禁止する (UdonBehaviour.DisableInteractive はシリアライズされないので
    ///   Editor では残らない。KD_PhotoBoard と同じく実行時に入れる)。
    /// - 降車は <see cref="_Unboard"/>: 車体基準の左右の降り口のうち NavMesh.SamplePosition (1 m) が通る方を選び、そのアンカーを
    ///   NavMesh 上の点 (+0.05 m) へ置いて stationExitPlayerLocation に入れてから ExitStation。両方だめなら元の位置の exitL。ジャンプで降りたときも同じ側へ出るよう、座っている間は manager が 0.5 秒ごとに _PickExit を呼ぶ。
    /// - OnStationExited (ローカル) は manager._OnUnseated → 乗車中なら manager の降車処理 (冪等)。
    /// - 層: 本体は Setup が layer 2 (Ignore Raycast) に置く (接地 Raycast の TravelGroundMask に入れない)。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_CartSeat : UdonSharpBehaviour
    {
        public KH_CartManager manager;
        public int index;
        public VRC.SDK3.Components.VRCStation station;
        public Transform seat, exitL, exitR, stopL, stopR, signBack, axleFront, axleRear;
        public Renderer[] renderers;
        public KH_CartStopButton stopButton, acceptButton;   // 子 GameObject (Interact を受ける。左 = 停止、右 = 受付切替)

        public const float WheelRadius = 0.14f;
        const float ExitReach = 1.0f;

        private int _visible = -1;   // -1 = まだ書いていない (初回は必ず書く)
        private int _exitSide = -1;  // 0 = L, 1 = R (ログ用)
        private bool _exitLocalReady;
        private Vector3 _exitLocalL, _exitLocalR;
        const float ExitLift = 0.05f;

        void Start()
        {
            DisableInteractive = true;   // クリック着座の禁止 (着座はパネル経由だけ)
            EnsureExitLocal();
        }

        public void _SetVisible(bool on)
        {
            int want = on ? 1 : 0;
            if (want == _visible) return;
            _visible = want;
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = on;
        }

        /// <summary>前後車軸を X 軸まわりに回す (半径 0.14 m。前進 = 正)。</summary>
        public void _Spin(float deltaDistance)
        {
            float deg = deltaDistance / WheelRadius * Mathf.Rad2Deg;
            Quaternion r = Quaternion.Euler(deg, 0f, 0f);
            if (axleFront != null) axleFront.localRotation = axleFront.localRotation * r;
            if (axleRear != null) axleRear.localRotation = axleRear.localRotation * r;
        }

        /// <summary>ローカルプレイヤーをこの席へ (manager の割当からだけ呼ばれる)。</summary>
        public void _Board()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || station == null) { Debug.LogWarning("[KH_Cart] board skipped cart=" + index + " (no station or player)"); return; }
            _PickExit();
            station.UseStation(lp);
        }

        /// <summary>立てる側の exit を選んでから station を出る。</summary>
        public void _Unboard()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || station == null) return;
            _PickExit();
            Debug.Log("[KH_Cart] unboard cart=" + index + " exit=" + (_exitSide == 1 ? "R" : "L"));
            station.ExitStation(lp);
        }

        /// <summary>車体基準の左右の降り口 (Start で控えたローカル位置) のうち NavMesh に立てる方 (L 優先) を選び、その側のアンカーを
        /// NavMesh 上の点 (+0.05 m) へ置いてから station の exit に入れる (レビュー #6: 以前は hit.position を捨てて元のアンカーへ降ろしていた)。
        /// 両側とも不可なら、控えた位置に戻した exitL。毎回書く (車体は動くので、前回と同じ側でも点は変わる)。</summary>
        public void _PickExit()
        {
            if (station == null) return;
            EnsureExitLocal();
            NavMeshHit hit;
            int side = 0;
            Transform t = exitL;
            Vector3 put = exitL != null ? transform.TransformPoint(_exitLocalL) : Vector3.zero;
            if (exitL != null && NavMesh.SamplePosition(transform.TransformPoint(_exitLocalL), out hit, ExitReach, NavMesh.AllAreas))
            {
                put = hit.position + Vector3.up * ExitLift;
            }
            else if (exitR != null && NavMesh.SamplePosition(transform.TransformPoint(_exitLocalR), out hit, ExitReach, NavMesh.AllAreas))
            {
                side = 1; t = exitR; put = hit.position + Vector3.up * ExitLift;
            }
            _exitSide = side;
            if (t == null) return;
            t.position = put;
            station.stationExitPlayerLocation = t;
        }

        // 降り口のアンカーは _PickExit が動かすので、元の位置 (車体ローカル) を最初に 1 回だけ控える
        private void EnsureExitLocal()
        {
            if (_exitLocalReady) return;
            _exitLocalReady = true;
            if (exitL != null) _exitLocalL = transform.InverseTransformPoint(exitL.position);
            if (exitR != null) _exitLocalR = transform.InverseTransformPoint(exitR.position);
        }

        /// <summary>停止・受付ボタンを、このカートの乗員 (受付はマスター) にだけ出す。manager.ApplyState から。</summary>
        public void _SetRider(bool mine, bool master, bool accepting)
        {
            if (stopButton != null) stopButton._SetOn(mine);
            if (acceptButton != null) { acceptButton._SetAccepting(accepting); acceptButton._SetOn(mine && master); }
        }

        /// <summary>言語が変わった (manager が 1 秒ごとに見て呼ぶ)。</summary>
        public void _RefreshText()
        {
            if (stopButton != null) stopButton._RefreshText();
            if (acceptButton != null) acceptButton._RefreshText();
        }

        public override void OnStationEntered(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            Debug.Log("[KH_Cart] station-entered cart=" + index + " player=" + player.playerId);
            if (player.isLocal && manager != null) manager._OnSeated(index);
        }

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            Debug.Log("[KH_Cart] station-exited cart=" + index + " player=" + player.playerId);
            if (player.isLocal && manager != null) manager._OnUnseated(index);   // 乗車中なら降車処理 (冪等)
        }
    }
}
