using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>IMP-WLD-008 §15.5 (2026-10-04 user): the camera drone, a small marker at a filmer's video camera. One per player: a child
    /// ("FilmDrone") of the KH_TravelMailbox PlayerObject, so the owner is that player (built by Tools/KominkaHillside/Video/6).
    ///
    /// Why a separate Behaviour (not more synced fields on the mailbox): the pose goes out at 5 Hz while filming, and the mailbox's own
    /// RequestSerialization would resend the grant table every time. This one carries only the pose (Manual, ~37 bytes) and is serialised only
    /// by its owner, only while the owner shows the drone AND a film session with another person is in use (KH_VideoCamera.DroneTick).
    ///
    /// - The owner's screen (F): KH_VideoCamera places it at the LOCAL camera pose every render tick (_PlaceLocal); the synced values are not used there.
    /// - The subject's screen (S): S's mailbox (FilmDroneUpdate, only while S has grant rows) shows it when the S-side "撮影中" condition holds
    ///   (the row (F, seq) and F's declaration (S, seq)) and shown / forTarget / forSeq say this pose is for S; it eases toward the received pose
    ///   (_PlaceRemote). Otherwise _Hide.
    /// - Everyone else: the renderer stays off (the values arrive but are never used). No Update: nothing runs per frame on any client.
    /// The renderer is on the monitor's layer (17), which the video camera does not draw. No collider, no shadows. Every public method is local-only (_).</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_FilmDrone : UdonSharpBehaviour
    {
        [UdonSynced] public Vector3 camPos;
        [UdonSynced] public Quaternion camRot = Quaternion.identity;
        [UdonSynced] public bool shown;
        [UdonSynced] public int forTarget = -1;
        [UdonSynced] public int forSeq;

        [Header("配線 (Video/6 が書く)")]
        public Renderer droneRenderer;
        [Tooltip("相手の画面で、届いた姿勢へ寄せる時定数 (s)。5 Hz の間をなめらかにつなぐ")]
        public float remoteTau = 0.15f;

        private Vector3 _dispPos;
        private Quaternion _dispRot = Quaternion.identity;
        private bool _dispHave;
        private int _remotePlaces;   // harness read-out

        /// <summary>Owner: send this pose for (target, seq). Local only.</summary>
        public void _Send(Vector3 pos, Quaternion rot, int target, int seq)
        {
            if (!Networking.IsOwner(gameObject)) return;
            camPos = pos; camRot = rot; shown = true; forTarget = target; forSeq = seq;
            RequestSerialization();
        }

        /// <summary>Owner: stop (one last serialization with shown = false). Nothing is sent when already stopped. Local only.</summary>
        public void _StopSend()
        {
            if (!Networking.IsOwner(gameObject) || !shown) return;
            shown = false; forTarget = -1;
            RequestSerialization();
        }

        /// <summary>Owner's own screen: at the local camera pose. Local only.</summary>
        public void _PlaceLocal(Vector3 pos, Quaternion rot)
        {
            transform.SetPositionAndRotation(pos, rot);
            if (droneRenderer != null && !droneRenderer.enabled) droneRenderer.enabled = true;
        }

        /// <summary>The subject's screen: ease toward the received pose (snap on the first frame). Local only.</summary>
        public void _PlaceRemote(float dt)
        {
            if (!_dispHave) { _dispPos = camPos; _dispRot = camRot; _dispHave = true; }
            else
            {
                float k = remoteTau <= 0.0001f ? 1f : 1f - Mathf.Exp(-dt / remoteTau);
                _dispPos = Vector3.Lerp(_dispPos, camPos, k);
                _dispRot = Quaternion.Slerp(_dispRot, camRot, k);
            }
            transform.SetPositionAndRotation(_dispPos, _dispRot);
            _remotePlaces++;
            if (droneRenderer != null && !droneRenderer.enabled) droneRenderer.enabled = true;
        }

        /// <summary>Off this screen (the next show starts with a snap). Local only.</summary>
        public void _Hide()
        {
            _dispHave = false;
            if (droneRenderer != null && droneRenderer.enabled) droneRenderer.enabled = false;
        }

        public bool _Visible() { return droneRenderer != null && droneRenderer.enabled; }
    }
}
