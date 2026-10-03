using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008: what the modes look at. **The only file of the video camera that reads a player.**
    ///
    /// - W1 reads the LOCAL player only (SelfHead). No other player is ever read: Video/2 checks that no KH_VideoCamera*.cs calls
    ///   the player-lookup APIs (by id / the player list), and that GetTrackingData / GetPosition / GetRotation appear only here.
    /// - W4 adds TryPersonPose(playerId) here as the single entry for another player, with the permission gate of design §5.5
    ///   (current film session target, the subject's mailbox, its owner, the synced grant table). A mode that wants another person
    ///   calls that and nothing else. It does not exist in W1.
    /// - The dog geometry is the photo director's (KD_PhotoDirector.DogHead / DogSubject / DogSize / FrameDistance, KD_PhotoDirector.cs:918-969),
    ///   copied here (those are private there; design §2.4 allows the copy and asks to record it).
    /// </summary>
    public partial class KH_VideoCamera
    {
        // the local player's head (SelfHead writes them)
        private Vector3 _selfHead, _selfFwd, _selfFeet;

        /// <summary>The local player's head position, facing (head rotation) and feet. False when the local player is not valid yet.</summary>
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
            return true;
        }

        /// <summary>The local player's id (-1 before the player is valid). Compared with synced ids (the petter, the thrower, the washer, the
        /// caller of a wash station) to know whether a framing may use MY pose; another player's pose is never read.</summary>
        int SelfId()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) ? lp.playerId : -1;
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
