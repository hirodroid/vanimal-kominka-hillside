using KominkaDogRun;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-050 (2026-10-13 user decision "推奨で": a separate behaviour, not a 6th KD_AccessoryBox slot): the collar pattern
    // of each of the six shared dogs. Same rules as the accessories (IMP-DOG-048 answer 1): anybody who owns the set can put it
    // on any dog, a leashed dog included; the last changer may change again at once, everybody else waits 60 s after the last
    // change. The 60 s here is independent of KD_AccessoryBox's (changing a collar does not lock that dog's accessories).
    //
    //   who writes  the Owner of this GameObject only (scene default = instance master; the automatic hand-over when the Owner
    //               leaves is not a request). Nobody takes it over: OnOwnershipRequest answers false, nothing calls SetOwner.
    //               Non-owners ask with RequestPattern (NetworkCallable); the Owner checks the dog / option range, the 60 s
    //               rule against the caller (NetworkCalling.CallingPlayer, not an argument) and ownership of the set via the
    //               caller's ledger (KD_ShopItem.OwnedBy = own.{productKey} in that player's PlayerData), then writes.
    //               A purchase's PlayerData can arrive after the request: the Owner answers "pending" (5) and the requester
    //               retries within the original 5 s window (the KD_AccessoryBox procedure, KD_AccessoryBox.cs:192-325).
    //   state       [UdonSynced] int[6] _pattern (0 = collar.basic = the dog's own colour), _changerId, _changedMs (server ms).
    //               RequestSerialization only on a change. Late joiners get all three on their first OnDeserialization and
    //               draw from them; a fresh instance starts at 0 (the original texture, nothing written).
    //   leaving     the patterns stay on the dogs (the changer's departure only clears its _changerId so others need not wait).
    //   apply       per dog renderer, slot 0: GetPropertyBlock -> SetTexture _MainTex (band) / SetColor _Color (ring) ->
    //               SetPropertyBlock. No material instance, no sharedMaterial write. 0 writes the original texture / colour
    //               explicitly. Written only when the dog's value differs from what this client last wrote.
    //   textures    one BaseColor per option with the KH_Leash_BaseColor layout (scripts/art/leash-assets.cjs: six 128 px dog
    //               lanes in the lower 768 px, fittings / grip / rope above), so the band keeps each dog's identity colour.
    //               Pome and Samoyed hide the band in the fur (BUG-DOG-011 option 1); their visible hardware is the procedural
    //               ClipAnchor/AttachmentRing (KH_ClipNeutral), which gets ringColors[option] (only the leather & brass set
    //               differs from the material's own colour).
    //   data        written by Tools/KominkaHillside/Collar/1 - Setup Collar Pattern (KH_CollarPatternEditor). Non-synced
    //               reference arrays are declared null (vrc-udonsharp §4).
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_CollarPattern : UdonSharpBehaviour
    {
        public const int DogCount = 6;
        public const int Unset = -1;
        /// <summary>60 s, the accessories' user decision (DES-DOG-004 §1.1) applied to collars (IMP-DOG-048 answer 1).</summary>
        public const int WaitMs = 60000;
        public const string ObjectName = "KH_CollarPattern";
        public const string CategoryKey = "collar";
        private const float PendingTimeoutSeconds = 5f;

        [Header("Collar/1 - Setup Collar Pattern writes these (option index = row of the collar table, [0] = collar.basic)")]
        public string[] productKeys;
        public KD_ShopItem[] items;
        public Texture2D[] textures;
        public Color[] ringColors;
        [Tooltip("per dog (KD_DogDefs order): the collar Band renderer")]
        public Renderer[] bandRenderers;
        [Tooltip("per dog: ClipAnchor/AttachmentRing on the fur-buried breeds (Pome, Samoyed), null elsewhere")]
        public Renderer[] ringRenderers;

        [UdonSynced] private int[] _pattern = new int[0];
        [UdonSynced] private int[] _changerId = new int[0];
        [UdonSynced] private int[] _changedMs = new int[0];

        // request state for the hand menu (local, not synced): 0 idle, 1 pending, 2 accepted, 3 rejected, 4 result unknown
        public int requestState;
        public int requestDog = Unset;
        public int requestOption = Unset;
        private int _requestToken;
        private float _requestDeadline;
        private int _requestOwnerId;
        private int _requestSentMs;
        private bool _retryWaiting;

        private int[] _applied;
        private MaterialPropertyBlock _block;
        private bool _ready;
        private int _writes;   // harness read-out: dog applications made by this client

        void Start() { EnsureArrays(); ApplyAll(); }

        private void EnsureArrays()
        {
            if (!_ready) { _ready = true; _block = new MaterialPropertyBlock(); }
            if (_pattern == null || _pattern.Length != DogCount) _pattern = new int[DogCount];
            if (_changerId == null || _changerId.Length != DogCount)
            {
                _changerId = new int[DogCount];
                for (int i = 0; i < DogCount; i++) _changerId[i] = Unset;
            }
            if (_changedMs == null || _changedMs.Length != DogCount) _changedMs = new int[DogCount];
            if (_applied == null || _applied.Length != DogCount) _applied = new int[DogCount];   // 0 = the materials' own look
        }

        public override void OnDeserialization() { EnsureArrays(); ApplyAll(); }

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) { return false; }

        // ============================================================
        // hand menu (local)
        // ============================================================

        /// <summary>Put option <paramref name="option"/> on dog <paramref name="dog"/>. Local only; the Owner decides.</summary>
        public void _Request(int dog, int option)
        {
            EnsureArrays();
            if (requestState == 1 || dog < 0 || dog >= DogCount || option < 0 || option >= OptionCount()) return;
            if (!CanChange(dog) || PatternOf(dog) == option) return;
            KD_ShopItem item = ItemAt(option);
            if (item == null || !item.Owned()) return;
            VRCPlayerApi player = Networking.LocalPlayer;
            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            if (!Utilities.IsValid(player) || !Utilities.IsValid(owner)) return;
            _requestToken = _requestToken == int.MaxValue ? 1 : _requestToken + 1;
            requestState = 1; requestDog = dog; requestOption = option;
            _requestOwnerId = owner.playerId; _retryWaiting = false;
            _requestDeadline = Time.time + PendingTimeoutSeconds;
            int now = Networking.GetServerTimeInMilliseconds();
            _requestSentMs = now;
            if (Networking.IsOwner(gameObject)) Handle(player, dog, option, now, owner.playerId, _requestToken);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestPattern), dog, option, now, owner.playerId, _requestToken);
            SendCustomEventDelayedSeconds(nameof(_PendingTimeout), PendingTimeoutSeconds);
        }

        [NetworkCallable(maxEventsPerSecond: 8)]
        // net: callable-ok: authenticated request (CallingPlayer), only the Owner writes after range / 60 s / ownership checks
        public void RequestPattern(int dog, int option, int sentMs, int ownerId, int token)
        {
            Handle(NetworkCalling.CallingPlayer, dog, option, sentMs, ownerId, token);
        }

        private void Handle(VRCPlayerApi player, int dog, int option, int sentMs, int ownerId, int token)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(player)) return;
            if (Networking.LocalPlayer.playerId != ownerId) return;
            int age = Networking.GetServerTimeInMilliseconds() - sentMs;
            if (age < -1000 || age > 5000) return;
            EnsureArrays();
            bool ok = dog >= 0 && dog < DogCount && option >= 0 && option < OptionCount();
            bool entitlementPending = false;
            if (ok) ok = RemainingFor(dog, player.playerId) <= 0;
            if (ok)
            {
                KD_ShopItem item = ItemAt(option);
                bool owned = item != null && item.OwnedBy(player);
                // PlayerData and the request travel separately: retry only an unconfirmed entitlement, within the deadline
                entitlementPending = !owned && item != null && !player.isLocal && age < 3500;
                ok = owned;
            }
            if (ok && _pattern[dog] != option)
            {
                _pattern[dog] = option;
                _changerId[dog] = player.playerId;
                _changedMs[dog] = Networking.GetServerTimeInMilliseconds();
                RequestSerialization();
                ApplyDog(dog);
            }
            if (player.isLocal) requestState = ok ? 2 : 3;
            else SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PatternReply), player.playerId, token, ok ? 2 : entitlementPending ? 5 : 3);
        }

        [NetworkCallable(maxEventsPerSecond: 16)]
        // net: callable-ok: addressed reply, accepted only from the current Owner for this client's own open token
        public void PatternReply(int playerId, int token, int result)
        {
            VRCPlayerApi sender = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(sender) || !Networking.IsOwner(sender, gameObject)) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local) || local.playerId != playerId || token != _requestToken || requestState != 1) return;
            if (result == 5)
            {
                if (!_retryWaiting) { _retryWaiting = true; SendCustomEventDelayedSeconds(nameof(_RetryEntitlement), .5f); }
                return;
            }
            if (result == 2 || result == 3) requestState = result;
        }

        public void _RetryEntitlement()
        {
            if (!_retryWaiting || requestState != 1) return;
            _retryWaiting = false;
            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            if (Time.time >= _requestDeadline || !Utilities.IsValid(owner) || owner.playerId != _requestOwnerId) { requestState = 4; return; }
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestPattern), requestDog, requestOption, _requestSentMs, _requestOwnerId, _requestToken);
        }

        public void _PendingTimeout()
        {
            if (requestState != 1) return;
            float remaining = _requestDeadline - Time.time;
            if (remaining > .01f) { SendCustomEventDelayedSeconds(nameof(_PendingTimeout), remaining); return; }
            requestState = 4;   // never replay an uncertain request after an Owner change
        }

        // ============================================================
        // departures (patterns stay; only the 60 s lock of a departed changer is released)
        // ============================================================

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject) || player == null) return;
            EnsureArrays();
            bool changed = false;
            for (int dog = 0; dog < DogCount; dog++)
            {
                if (_changerId[dog] != player.playerId) continue;
                _changerId[dog] = Unset; _changedMs[dog] = 0; changed = true;
            }
            if (changed) RequestSerialization();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // covers either ordering of the departure and the automatic Owner hand-over
            if (Utilities.IsValid(player) && player.isLocal) SendCustomEventDelayedSeconds(nameof(_ReleaseDepartedChangers), .5f);
        }

        public void _ReleaseDepartedChangers()
        {
            if (!Networking.IsOwner(gameObject)) return;
            EnsureArrays();
            bool changed = false;
            for (int dog = 0; dog < DogCount; dog++)
            {
                if (_changerId[dog] == Unset || Utilities.IsValid(VRCPlayerApi.GetPlayerById(_changerId[dog]))) continue;
                _changerId[dog] = Unset; _changedMs[dog] = 0; changed = true;
            }
            if (changed) RequestSerialization();
        }

        // ============================================================
        // drawing
        // ============================================================

        private void ApplyAll()
        {
            for (int d = 0; d < DogCount; d++) ApplyDog(d);
        }

        private void ApplyDog(int dog)
        {
            if (_applied == null || dog < 0 || dog >= _applied.Length) return;
            int option = PatternOf(dog);
            if (_applied[dog] == option) return;
            Texture2D tex = TextureAt(option);
            if (tex == null) return;
            if (bandRenderers != null && dog < bandRenderers.Length && bandRenderers[dog] != null)
            {
                Renderer band = bandRenderers[dog];
                _block.Clear();
                band.GetPropertyBlock(_block, 0);
                _block.SetTexture("_MainTex", tex);
                band.SetPropertyBlock(_block, 0);
            }
            if (ringRenderers != null && dog < ringRenderers.Length && ringRenderers[dog] != null
                && ringColors != null && option < ringColors.Length)
            {
                Renderer ring = ringRenderers[dog];
                _block.Clear();
                ring.GetPropertyBlock(_block, 0);
                _block.SetColor("_Color", ringColors[option]);
                ring.SetPropertyBlock(_block, 0);
            }
            _applied[dog] = option;
            _writes++;
        }

        // ============================================================
        // read-only API (hand menu, KH_CollarLeash)
        // ============================================================

        // net: callable-ok: read only
        public int OptionCount()
        {
            if (productKeys == null || items == null || textures == null) return 0;
            int n = productKeys.Length;
            if (items.Length < n) n = items.Length;
            if (textures.Length < n) n = textures.Length;
            return n;
        }

        // net: callable-ok: read only (has a parameter, not a network event)
        public string ProductAt(int option)
        {
            return productKeys != null && option >= 0 && option < OptionCount() ? productKeys[option] : "";
        }

        // net: callable-ok: read only
        public KD_ShopItem ItemAt(int option)
        {
            return items != null && option >= 0 && option < OptionCount() ? items[option] : null;
        }

        // net: callable-ok: read only
        public Texture2D TextureAt(int option)
        {
            return textures != null && option >= 0 && option < OptionCount() ? textures[option] : null;
        }

        // net: callable-ok: read only
        public int IndexOfProduct(string key)
        {
            if (string.IsNullOrEmpty(key) || productKeys == null) return -1;
            int n = OptionCount();
            for (int i = 0; i < n; i++) if (productKeys[i] == key) return i;
            return -1;
        }

        /// <summary>The synced option, read as 0 when outside this build's table (an older / newer upload).</summary>
        // net: callable-ok: read only
        public int PatternOf(int dog)
        {
            if (_pattern == null || dog < 0 || dog >= _pattern.Length) return 0;
            int p = _pattern[dog];
            return p >= 0 && p < OptionCount() ? p : 0;
        }

        // net: callable-ok: read only
        public bool CanChange(int dog) { return RemainingMs(dog) <= 0; }

        // net: callable-ok: read only
        public int RemainingMs(int dog)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return RemainingFor(dog, Utilities.IsValid(lp) ? lp.playerId : Unset);
        }

        // net: callable-ok: read only
        public int RemainingSeconds(int dog)
        {
            int ms = RemainingMs(dog);
            return ms <= 0 ? 0 : (ms + 999) / 1000;
        }

        private int RemainingFor(int dog, int playerId)
        {
            if (_changerId == null || _changedMs == null || dog < 0 || dog >= _changerId.Length || dog >= _changedMs.Length) return 0;
            int changer = _changerId[dog];
            if (changer == Unset || changer == playerId) return 0;
            int elapsed = Networking.GetServerTimeInMilliseconds() - _changedMs[dog];
            if (elapsed < 0) return 0;   // a clock that seems to run backwards never locks a dog forever
            int left = WaitMs - elapsed;
            return left > 0 ? left : 0;
        }
    }
}
