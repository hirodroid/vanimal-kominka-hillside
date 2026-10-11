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
        public KH_CartStopButton stopButton, acceptButton;   // 子 GameObject (Interact を受ける。M1.1: 左右とも停止 (kind 0)。kind 1 ならマスターだけ)
        public KH_CartControls controls;   // M1.1: 左斜め前の操作帯 (null 可)
        public KH_CartDock dock;           // M1.1: 乗車中に板を寄せるドック (null 可)
        public TMPro.TextMeshProUGUI signCaption;   // M2: 背面掲示の下の 1 行 (cart.sign.caption。言語の変化で書き直す。null 可)

        public const float SeatStep = 0.05f, YawStep = 15f;   // 座席の 1 段 (m / 度)
        private bool _seatReady;
        private Vector3 _seatPos;
        private Quaternion _seatRot;
        private int _seatH = int.MinValue, _seatY = int.MinValue, _seatF = int.MinValue;   // 最後に置いた段数 (変わったときだけ書く)

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
            EnsureSeatDefault();
        }

        /// <summary>M1.1: 座席 (Anchor_Seat = station の入る位置) を既定 + 段数に置く。高さはローカル y、前後はローカル z (+ が前) にどちらも 5 cm/段、
        /// 向きは親の Y 軸まわりに 15°/段。
        /// 全クライアントが manager.ApplyState から同期値で呼ぶ (座っている人は station の入る位置に付いて動く)。</summary>
        public void _ApplySeat(int steps, int yawSteps, int fwdSteps)
        {
            if (seat == null) return;
            if (steps == _seatH && yawSteps == _seatY && fwdSteps == _seatF) return;
            EnsureSeatDefault();
            _seatH = steps; _seatY = yawSteps; _seatF = fwdSteps;
            // 前後は車体の前 (ローカル +z) へ 5 cm/段 (2026-10-12 追加要望)
            // M2: 1 段の量は manager の調整値 (seatStep / yawStep。tuning.json から Build が書く)。無ければ既定の定数
            float step = manager != null && manager.seatStep > 0f ? manager.seatStep : SeatStep;
            float yawStep = manager != null && manager.yawStep > 0f ? manager.yawStep : YawStep;
            seat.localPosition = new Vector3(_seatPos.x, _seatPos.y + steps * step, _seatPos.z + fwdSteps * step);
            seat.localRotation = Quaternion.Euler(0f, yawSteps * yawStep, 0f) * _seatRot;
        }

        // 既定の座席の姿勢は、最初に動かす前に 1 回だけ控える (Start の順は保証されないので _ApplySeat からも呼ぶ)
        private void EnsureSeatDefault()
        {
            if (_seatReady || seat == null) return;
            _seatReady = true;
            _seatPos = seat.localPosition;
            _seatRot = seat.localRotation;
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
            if (stopButton != null) stopButton._SetOn(mine && (stopButton.kind == 0 || master));
            if (acceptButton != null)
            {
                acceptButton._SetAccepting(accepting);
                acceptButton._SetOn(mine && (acceptButton.kind == 0 || master));   // M1.1: 右も停止 (kind 0) なら乗員全員に出す
            }
            if (dock != null) dock._SetRiding(mine);
            if (controls != null) controls._Refresh();
        }

        /// <summary>言語が変わった (manager が 1 秒ごとに見て呼ぶ)。</summary>
        public void _RefreshText()
        {
            if (stopButton != null) stopButton._RefreshText();
            if (acceptButton != null) acceptButton._RefreshText();
            if (controls != null) controls._RefreshText();
            // M2: 背面掲示の下の 1 行 (manager の最初の 1 秒の見回りで初回も書かれる)
            if (signCaption != null && manager != null && manager.language != null) signCaption.text = manager.language._Text("cart.sign.caption");
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
