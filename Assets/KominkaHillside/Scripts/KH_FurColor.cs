using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // DES-DOG-009 (2026-10-06 user decisions, design docs/plans/2026-10-08-dog-book-and-fur-color.md): the fur color of each of the
    // six shared dogs, chosen by the instance master and the same for everyone (late joiners included). No points, no unlocks.
    //
    //   who writes  the Owner of this GameObject only. The default Owner of a scene object is the instance master, and when the
    //               Owner leaves VRChat hands the object to the (new) master, so "Owner" == "master" without reading isMaster
    //               (vrc-networking §4: do not gate a feature on Master; build it on Ownership). Nobody can take it over:
    //               OnOwnershipRequest always answers false, and nothing in the world calls SetOwner on it.
    //               _CanEdit additionally waits until this client knows the current state: received (OnDeserialization),
    //               Networking.IsNetworkSettled, or "first Owner" = Owner at its own OnPlayerJoined, alone, and no other player
    //               ever seen. A player count of 1 alone is NOT enough (Codex 2026-10-08): a late joiner whose old Owner left
    //               before anything arrived is also alone, and its zeros would reset every dog. Losing the ownership clears it.
    //   state       [UdonSynced] int[6] _colors, 0 = the dog's own (pack) color. RequestSerialization only on a change.
    //               The values survive an Owner change (the new Owner holds the last received array). A change made in the last
    //               moment before the master leaves can be lost if it never left that client (Manual sync, no retry).
    //   apply       per target slot: GetPropertyBlock(block, slot) -> SetTexture _MainTex / _BaseMap -> SetPropertyBlock(block, slot)
    //               (the same per-slot read-modify-write as KD_DistanceFadeSettings, so the fade's _DistanceFade / _DistanceFadeColor
    //               and this texture live in one block side by side). No material instance, no sharedMaterial write. Going back to 0
    //               writes the recorded original texture explicitly (it does not remove the property).
    //               Written only when a dog's value differs from what this client last wrote (Start: nothing; the materials already
    //               show the original). Never per frame.
    //   data        written by Tools/KominkaHillside/DogBook/5 - Build Fur Colors (KH_FurColorEditor). Non-synced reference arrays are
    //               declared null (vrc-udonsharp §4: an initialized array of the same length can lose its backing override).
    //               textures / nameKeys: one block per dog [textureStart[d], textureStart[d] + textureCount[d]); the first entry of a
    //               block is the original texture with the key "furcolor.default".
    //               targetRenderers / targetSlots / targetDogs: every fur slot of every dog whose original texture is that dog's
    //               body fur texture (the distance-fade slot's _MainTex). Eyes, mouth and the KD_Acc_ / KH_ parts are not targets.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_FurColor : UdonSharpBehaviour
    {
        public const int DogCount = 6;
        public const string DefaultKey = "furcolor.default";

        [Header("DogBook/5 - Build Fur Colors writes these")]
        public Renderer[] targetRenderers;
        public int[] targetSlots;
        public int[] targetDogs;
        public Texture2D[] textures;
        public int[] textureStart;
        public int[] textureCount;
        public string[] nameKeys;

        [UdonSynced] private int[] _colors = new int[DogCount];

        private int[] _applied;               // per dog: the index this client last wrote (0 at start = the material's own texture)
        private MaterialPropertyBlock _block;
        private bool _ready, _received;
        private bool _sawOthers, _firstOwner;  // first Owner: owned from its own join with nobody else ever present (see header)
        private int _writes;                  // harness read-out: how many dog applications this client made (int only)

        void Start() { Init(); ApplyAll(); }

        void Init()
        {
            if (_ready) return;
            _ready = true;
            _block = new MaterialPropertyBlock();
            _applied = new int[DogCount];
            if (_colors == null || _colors.Length != DogCount) _colors = new int[DogCount];
        }

        public override void OnDeserialization() { Init(); _received = true; ApplyAll(); }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            if (!player.isLocal) { _sawOthers = true; return; }
            // own join: the instance's first player owns the scene objects, and nobody had written values before it
            if (!_sawOthers && Networking.IsOwner(gameObject) && VRCPlayerApi.GetPlayerCount() <= 1) _firstOwner = true;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject)) _firstOwner = false;
        }

        // Nobody may take the color over (the automatic hand-over to the new master when the Owner leaves is not a request).
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) { return false; }

        /// <summary>Can the local player change colors now: the Owner (= master), and this client knows the current values
        /// (received / network settled / the first Owner since its own join).</summary>
        public bool _CanEdit()
        {
            if (!Networking.IsOwner(gameObject)) return false;
            return _received || Networking.IsNetworkSettled || _firstOwner;
        }

        // net: callable-ok: read-only (has a parameter, so not a network event)
        public int OptionCount(int dog)
        {
            if (textureCount == null || dog < 0 || dog >= textureCount.Length) return 1;
            return Mathf.Max(1, textureCount[dog]);
        }

        /// <summary>The synced index, read as 0 when it is outside this build's list (an older / newer upload).</summary>
        // net: callable-ok: read-only
        public int ColorOf(int dog)
        {
            if (_colors == null || dog < 0 || dog >= _colors.Length) return 0;
            int c = _colors[dog];
            return c >= 0 && c < OptionCount(dog) ? c : 0;
        }

        // net: callable-ok: read-only
        public string NameKey(int dog, int index)
        {
            int at = Index(dog, index);
            return at >= 0 && nameKeys != null && at < nameKeys.Length && !string.IsNullOrEmpty(nameKeys[at]) ? nameKeys[at] : DefaultKey;
        }

        /// <summary>The panel's ◀ ▶. Owner only; wraps around the dog's list.</summary>
        public void _Step(int dog, int delta)
        {
            int count = OptionCount(dog);
            if (count <= 1) return;
            int next = (ColorOf(dog) + delta) % count;
            if (next < 0) next += count;
            _Set(dog, next);
        }

        /// <summary>Owner only. Applies locally at once (OnDeserialization does not fire on the Owner) and serializes.</summary>
        public void _Set(int dog, int index)
        {
            Init();
            if (!_CanEdit() || dog < 0 || dog >= DogCount || index < 0 || index >= OptionCount(dog)) return;
            if (_colors[dog] == index) return;
            _colors[dog] = index;
            ApplyDog(dog);
            RequestSerialization();
        }

        void ApplyAll()
        {
            for (int d = 0; d < DogCount; d++) ApplyDog(d);
        }

        void ApplyDog(int dog)
        {
            int c = ColorOf(dog);
            if (_applied[dog] == c) return;
            int at = Index(dog, c);
            if (at < 0 || textures == null || at >= textures.Length || textures[at] == null) return;
            Texture2D tex = textures[at];
            if (targetRenderers == null || targetSlots == null || targetDogs == null) return;
            for (int i = 0; i < targetRenderers.Length && i < targetSlots.Length && i < targetDogs.Length; i++)
            {
                if (targetDogs[i] != dog) continue;
                Renderer target = targetRenderers[i];
                if (target == null) continue;
                int slot = targetSlots[i];
                if (slot < 0 || slot >= target.sharedMaterials.Length) continue;
                _block.Clear();
                target.GetPropertyBlock(_block, slot);   // keeps KD_DistanceFadeSettings' values in the same slot
                _block.SetTexture("_MainTex", tex);
                _block.SetTexture("_BaseMap", tex);
                target.SetPropertyBlock(_block, slot);
            }
            _applied[dog] = c;
            _writes++;
        }

        int Index(int dog, int index)
        {
            if (textureStart == null || dog < 0 || dog >= textureStart.Length || index < 0 || index >= OptionCount(dog)) return -1;
            return textureStart[dog] + index;
        }
    }
}
