using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008: what the modes look at. **The only file of the video camera that reads a player's pose.**
    ///
    /// - SelfHead / SelfHandNear read the LOCAL player. Video/2 checks that GetTrackingData / GetPosition / GetRotation appear only in this file
    ///   and, here, on another player only inside TryPersonPose after its permission gate.
    /// - W4: TryPersonPose(playerId) is the single entry for another player (design §5.5 / §13). The gate is my mailbox's _FilmGate: the film
    ///   session is in use for that player, the subject's mailbox is owned by the subject, and its synced grant table holds (me, the session's seq).
    ///   It runs on every render tick; nothing is remembered between ticks (a revoked grant stops the next tick). The monitor preview goes through it too.
    ///   KH_VideoCamera.People.cs looks players up only for the picker list (names, ids), never their pose.
    /// - The dog geometry is the photo director's (KD_PhotoDirector.DogHead / DogSubject / DogSize / FrameDistance, KD_PhotoDirector.cs:918-969),
    ///   copied here (those are private there; design §2.4 allows the copy and asks to record it).
    /// </summary>
    public partial class KH_VideoCamera
    {
        // the local player's head (SelfHead writes them). _selfBody: the body's facing (GetRotation), horizontal (§16 背中ごし)
        private Vector3 _selfHead, _selfFwd, _selfFeet, _selfBody;

        /// <summary>The local player's head position, facing (head rotation), feet and body facing. False when the local player is not valid yet.</summary>
        bool SelfHead()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return false;
            VRCPlayerApi.TrackingData td = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            _selfHead = td.position;
            _selfFwd = td.rotation * Vector3.forward;
            _selfFeet = lp.GetPosition();
            Vector3 body = lp.GetRotation() * Vector3.forward;
            Vector3 flat = new Vector3(_selfFwd.x, 0f, _selfFwd.z);
            if (flat.sqrMagnitude < 0.01f) _selfFwd = body;   // looking straight up / down: the body's facing
            _selfBody = new Vector3(body.x, 0f, body.z);
            _selfBody = _selfBody.sqrMagnitude > 0.0001f ? _selfBody.normalized : new Vector3(flat.x, 0f, flat.z).normalized;
            if (_selfBody.sqrMagnitude < 0.0001f) _selfBody = Vector3.forward;
            return true;
        }

        /// <summary>The local player's id (-1 before the player is valid). Compared with synced ids (the petter, the thrower, the washer, the
        /// caller of a wash station) to know whether a framing may use MY pose; another player's pose is never read.</summary>
        int SelfId()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) ? lp.playerId : -1;
        }

        // the person being followed (TryPersonPose writes them; cleared when the permission ends)
        private Vector3 _personHead, _personFwd, _personFeet;
        private int _personReads;   // harness read-out: how many times another player's pose was read

        /// <summary>The ONLY way to read another player's pose (design §5.5). The local player needs no permission. Another player: my mailbox's
        /// film gate must pass on this very tick (session in use for pid, the subject's box owned by pid, (me, seq) in its synced grant table);
        /// otherwise nothing is read and false comes back (the caller falls back to a framing without the person).</summary>
        bool TryPersonPose(int pid)
        {
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
            if (!Utilities.IsValid(p)) return false;
            if (p.isLocal)
            {
                if (!SelfHead()) return false;
                _personHead = _selfHead; _personFwd = _selfFwd; _personFeet = _selfFeet;
                return true;
            }
            KH_TravelMailbox box = PersonMyBox();
            if (box == null || !box._FilmGate(pid)) return false;
            _personReads++;
            VRCPlayerApi.TrackingData td = p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            _personHead = td.position;
            _personFwd = td.rotation * Vector3.forward;
            _personFeet = p.GetPosition();
            Vector3 flat = new Vector3(_personFwd.x, 0f, _personFwd.z);
            if (flat.sqrMagnitude < 0.01f) _personFwd = p.GetRotation() * Vector3.forward;
            return true;
        }

        /// <summary>§17: I hold this dog's disc (a held VRC_Pickup is owned by its holder, so "held and mine" = in my hand). No player is read.</summary>
        bool SelfHoldsDisc(int dog)
        {
            KH_Frisbee d = discs != null && dog >= 0 && dog < discs.Length ? discs[dog] : null;
            return d != null && d.IsHeldNow && Networking.IsOwner(d.gameObject);
        }

        private Vector3 _selfHand;

        /// <summary>The local player's hand nearer to `point` (into _selfHand). False when the local player is not valid.</summary>
        bool SelfHandNear(Vector3 point)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return false;
            Vector3 l = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            Vector3 r = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
            _selfHand = (l - point).sqrMagnitude <= (r - point).sqrMagnitude ? l : r;
            return true;
        }

        bool DogValid(int i)
        {
            return dogRoots != null && i >= 0 && i < dogRoots.Length && dogRoots[i] != null && dogRoots[i].gameObject.activeInHierarchy;
        }

        Vector3 DogHead(int i)
        {
            Transform h = dogHeads != null && i >= 0 && i < dogHeads.Length ? dogHeads[i] : null;
            if (h != null) return h.position;
            Transform root = dogRoots[i];
            return root == null ? Vector3.zero : root.position + Vector3.up * 0.3f;
        }

        /// <summary>The body centre (a head-only aim cuts the body). Same as KD_PhotoDirector.DogSubject.</summary>
        Vector3 DogSubject(int i)
        {
            Transform root = dogRoots[i];
            if (root == null) return Vector3.zero;
            return Vector3.Lerp(root.position, DogHead(i), 0.75f);
        }

        /// <summary>Rough body length from the measured height (head anchor − root) × 1.5. Same as KD_PhotoDirector.DogSize (NaN-safe).</summary>
        float DogSize(int i)
        {
            Transform root = dogRoots[i];
            Transform h = dogHeads != null && i >= 0 && i < dogHeads.Length ? dogHeads[i] : null;
            float y = (root != null && h != null) ? h.position.y - root.position.y : 0f;
            if (!(y > 0.1f) || !(y < 2f)) y = 0.4f;
            return y * 1.5f;
        }
    }
}
