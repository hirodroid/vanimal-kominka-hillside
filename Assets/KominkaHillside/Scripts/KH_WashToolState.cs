using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-DOG-025: the one synced bit of a wash tool (the shower is spraying). It lives on a child GameObject because
    // the tool itself carries VRCObjectSync (Continuous) and one GameObject cannot mix sync modes (vrc-networking §6).
    // A Pickup does not move this child's ownership, so the holder takes it before writing.
    //
    // The holder's wish is kept locally (_want) and written only while this client is the Owner: at once when it
    // already is, otherwise when OnOwnershipTransferred confirms the transfer (vrc-networking §4: do not rely on the
    // write right after SetOwner). The holder shows its own spray from the input without waiting (KH_WashTool).
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_WashToolState : UdonSharpBehaviour
    {
        public KH_WashTool washTool;
        [UdonSynced] public bool spraying;
        // IMP-DOG-049 (2026-10-11, Codex review x2): a one-shot tool (KH_WashTool.pulseSeconds > 0, the water pistol) does not use spraying:
        // a 0.4 s on / off could be lost while the ownership was on its way (the pending wish became false before it landed) or merge into
        // one serialization (the others kept it shown and never restarted the burst). Each shot advances a serial instead, with the server
        // time of the INPUT (the press) and the item fired (the water slot is shared). The others play one burst when the serial advances,
        // the input time is fresh (ShotFreshMs) and the item is the one their slot shows. The first state a client receives (the join state)
        // only records the serial, never plays. Toggle / hold tools keep spraying as before.
        // Accepted limitation: Manual sync may merge rapid shots into one update, so the others can see fewer bursts than were fired; the
        // shot is cosmetic for them (the holder's rays wash the dog, and KH_DogWash sends that on its own).
        [UdonSynced] public int shotSerial;
        [UdonSynced] public int shotServerMs;   // server time of the latest shot's input
        [UdonSynced] public int shotItem = -1;  // the KH_WashSelection item id of that shot
        public const int ShotFreshMs = 1500;
        const int ShotPendingMax = 3;
        private int _shotSeen;
        private bool _received;                 // the first OnDeserialization only records (no replay for a late joiner)
        private int _shotPendingCount;
        private int _shotPendingMs;
        private int _shotPendingItem = -1;

        private bool _want;
        private bool _pending;

        // Local only: the holder of the tool calls this (Use down / up, drop), or the Owner clears a spray whose
        // holder is gone.
        public void _SetSpraying(bool value)
        {
            _want = value;
            if (Networking.IsOwner(gameObject))
            {
                _pending = false;
                Write();
                return;
            }
            if (spraying == value && !_pending) return;   // nothing to change: do not take the ownership for it
            // A request is already on its way: the newest wish is written when it lands. Repeated SetOwner calls in
            // a short time fight and roll back (vrc-networking §4), so tapping Use asks once. There is no automatic
            // retry: a request that did not land (someone else took it) is asked again only by a call made 3 s or
            // more after it (the next Use down / up or drop).
            if (_pending && Time.time - _askedAt < 3f) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            _pending = true;
            _askedAt = Time.time;
            Networking.SetOwner(lp, gameObject); // net: setowner-ok: tool state only (the holder writes); written after the transfer is confirmed
        }

        private float _askedAt = -100f;

        // Local only: the holder of a one-shot tool calls this on every accepted shot (KH_WashTool.OnPickupUseDown), with the item it fired.
        // Not a network event. The input time is taken here (the press), not when the write happens.
        public void _Shot(int item)
        {
            int inputMs = Networking.GetServerTimeInMilliseconds();
            if (_shotPendingCount < ShotPendingMax) _shotPendingCount++;
            _shotPendingMs = inputMs;
            _shotPendingItem = item;
            if (Networking.IsOwner(gameObject)) { WriteShots(); return; }
            // written when the transfer lands (OnOwnershipTransferred); the receivers judge the freshness by the input time
            if (Time.time - _askedAt < 3f) return;          // a request (either path) is already on its way: do not fight (vrc-networking §4)
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            _askedAt = Time.time;
            Networking.SetOwner(lp, gameObject); // net: setowner-ok: tool state only (the holder writes); the shots are written after the transfer is confirmed
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!_pending && _shotPendingCount == 0) return;
            if (!Networking.IsOwner(gameObject)) return;   // someone else took it; the wish stays pending until the next call
            if (_pending) { _pending = false; Write(); }
            if (_shotPendingCount > 0) WriteShots();          // a late write carries the old input time: the receivers drop it as stale
        }

        void WriteShots()
        {
            shotSerial += _shotPendingCount;                // net: owner-only (pending shots, at most ShotPendingMax)
            shotServerMs = _shotPendingMs;                  // net: owner-only (the latest shot's input time)
            shotItem = _shotPendingItem;                    // net: owner-only
            _shotPendingCount = 0;
            _shotSeen = shotSerial;                         // the owner shows its own shot from the input (KH_WashTool)
            RequestSerialization();
        }

        void Write()
        {
            if (spraying == _want) return;
            spraying = _want;                              // net: owner-only
            RequestSerialization();
            if (washTool != null) washTool._OnStateChanged();
        }

        public override void OnDeserialization()
        {
            if (washTool != null) washTool._OnStateChanged();
            if (!_received) { _received = true; _shotSeen = shotSerial; return; }   // the join state: record only, never replay
            if (shotSerial == _shotSeen) return;
            _shotSeen = shotSerial;
            // one burst when the serial advances (several shots merged into one update show as one); a stale input time is not played
            int age = Networking.GetServerTimeInMilliseconds() - shotServerMs;
            if (age > -ShotFreshMs && age < ShotFreshMs && washTool != null) washTool._OnShot(shotItem);
        }
    }
}
