using KominkaDogRun;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-DOG-050: the leash pattern (IMP-DOG-048 answer 2: one per player, saved in PlayerData, others see it).
    // Modelled on KD_PetFxBox (the petting effect consumer of the catalog) and KD_PlayerFxTag (its broadcast).
    //
    //   own choice  the catalog's "collar" selection IS the choice: KD_ShopCatalog saves it as PlayerData Sel.collar, restores
    //               it after the ledger has loaded, falls back to the free default (collar.basic) when the saved key is not
    //               owned, and selects a set on purchase. This behaviour only reads it (_OnShopSelectionChanged from the
    //               catalog's consumers, _CheckRestored after joining) and publishes it on the player's own KH_CollarTag.
    //   others      every client draws each dog's rope / snap / handle with the pattern of the player holding that dog
    //               (KH_LeashManager.walkers[dog] = holder playerId). Remote holders: their KH_CollarTag (looked up once per
    //               holder change) -> the key must be a collar product the holder owns (KD_ShopItem.OwnedBy) -> else default.
    //               A 0.5 s local tick picks up attaches and remote changes; it writes only when a dog's option changes.
    //   sync        None. Nothing here is shared state; the broadcast lives on the PlayerObject (KH_CollarTag, Manual).
    //   apply       slot 0 GetPropertyBlock -> SetTexture _MainTex -> SetPropertyBlock on the three renderers of the dog's
    //               KH_LeashCollar (ropeMesh / snapVisual / handleVisual). The LineRenderer fallback and the station clips
    //               (KH_LeashClip) keep the shared material. NOTE (Codex review 2026-10-13): KH_LeashCollar draws ANOTHER
    //               player's rope as the LineRenderer when it is farther than manager.ropeStraightDistance from the local player
    //               (IMP-DOG-036 S6, even with lightRope == false), so a remote holder's pattern disappears at a distance and comes
    //               back near (ropeFullDistance). Whether the LineRenderer should carry the pattern is undecided (IMP-DOG-050).
    //   restore     _Tick keeps calling _CheckRestored until catalog.Resolved() has been seen once (no retry budget: a
    //               PlayerData restore slower than 30 s must still be adopted; the catalog does not notify on restore).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_CollarLeash : UdonSharpBehaviour
    {
        public const string ObjectName = "KH_CollarLeash";
        public const string Unset = "";
        private const float TickSeconds = 0.5f;

        [Header("Collar/1 - Setup Collar Pattern writes these")]
        public KD_ShopCatalog catalog;
        public string categoryKey = "collar";
        public string defaultProductKey = "collar.basic";
        [Tooltip("the collar table (product keys, items, textures) is read from here — one source")]
        public KH_CollarPattern patterns;
        public KH_LeashManager manager;
        [Tooltip("per dog (KD_DogDefs order): KH_LeashCollar.ropeMesh / snapVisual / handleVisual")]
        public Renderer[] ropeRenderers;
        public Renderer[] snapRenderers;
        public Renderer[] handleRenderers;

        private string _local = Unset;        // this player's resolved choice
        private string _published = Unset;    // what the own tag was last given
        private KH_CollarTag _localTag;
        private int[] _holder;
        private KH_CollarTag[] _tags;
        private int[] _applied;               // option index last written per dog (0 = the shared material's own texture)
        private MaterialPropertyBlock _block;
        private bool _ready;
        private bool _restoreSeen;            // catalog.Resolved() adopted once (checked from _Tick until then, no budget)
        private int _writes;                  // harness read-out

        void Start()
        {
            Init();
            SendCustomEventDelayedSeconds(nameof(_CheckRestored), 1f);
            SendCustomEventDelayedSeconds(nameof(_Tick), TickSeconds);
        }

        private void Init()
        {
            if (_ready) return;
            _ready = true;
            _block = new MaterialPropertyBlock();
            _holder = new int[KH_CollarPattern.DogCount];
            for (int i = 0; i < _holder.Length; i++) _holder[i] = -1;
            _tags = new KH_CollarTag[KH_CollarPattern.DogCount];
            _applied = new int[KH_CollarPattern.DogCount];
        }

        /// <summary>After joining: once the catalog has restored Sel.collar, use and publish it. Local only.</summary>
        public void _CheckRestored()
        {
            if (catalog == null || _restoreSeen || !catalog.Resolved()) return;   // _Tick calls again every 0.5 s until resolved
            _restoreSeen = true;
            _OnShopSelectionChanged();
        }

        /// <summary>The catalog's consumer event (any category; -1 / empty = unknown is fail-open like KD_PetFxBox). Local only.</summary>
        public void _OnShopSelectionChanged()
        {
            Init();
            if (catalog == null) return;
            string changed = catalog.ChangedCategory();
            if (!string.IsNullOrEmpty(changed) && changed != categoryKey) return;
            string want = catalog.SelectedProductOf(categoryKey);
            _local = string.IsNullOrEmpty(want) ? defaultProductKey : want;
            Publish();
            Refresh();
        }

        public void _Tick()
        {
            Init();
            if (!_restoreSeen) _CheckRestored();     // a restore later than any fixed budget is still adopted
            if (_published != _local) Publish();   // the own PlayerObject can appear after the restore
            Refresh();
            SendCustomEventDelayedSeconds(nameof(_Tick), TickSeconds);
        }

        private void Publish()
        {
            if (_local == Unset) return;
            if (_localTag == null) _localTag = FindTagOf(Networking.LocalPlayer);
            if (_localTag == null) return;
            _localTag._Publish(_local);
            _published = _local;
        }

        private void Refresh()
        {
            if (manager == null || patterns == null) return;
            int[] walkers = manager.walkers;
            if (walkers == null) return;
            for (int d = 0; d < KH_CollarPattern.DogCount && d < walkers.Length; d++)
            {
                int holder = walkers[d];
                if (holder != _holder[d]) { _holder[d] = holder; _tags[d] = null; }
                int want = holder < 0 ? _applied[d] : ResolveOption(d, holder);   // a released rope is hidden: leave it as is
                if (want != _applied[d]) ApplyDog(d, want);
            }
        }

        private int ResolveOption(int dog, int holder)
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(holder);
            if (!Utilities.IsValid(player)) return 0;
            string key;
            if (player.isLocal) key = _local;
            else
            {
                if (_tags[dog] == null) _tags[dog] = FindTagOf(player);
                key = _tags[dog] == null ? Unset : _tags[dog].Selected();
            }
            int option = patterns.IndexOfProduct(key);
            if (option <= 0) return 0;
            KD_ShopItem item = patterns.ItemAt(option);
            if (item == null || item.categoryKey != categoryKey) return 0;
            bool owned = player.isLocal ? item.Owned() : item.OwnedBy(player);
            return owned ? option : 0;
        }

        private void ApplyDog(int dog, int option)
        {
            Texture2D tex = patterns.TextureAt(option);
            if (tex == null) return;
            Write(ropeRenderers, dog, tex);
            Write(snapRenderers, dog, tex);
            Write(handleRenderers, dog, tex);
            _applied[dog] = option;
            _writes++;
        }

        private void Write(Renderer[] list, int dog, Texture2D tex)
        {
            if (list == null || dog < 0 || dog >= list.Length || list[dog] == null) return;
            Renderer r = list[dog];
            _block.Clear();
            r.GetPropertyBlock(_block, 0);
            _block.SetTexture("_MainTex", tex);
            r.SetPropertyBlock(_block, 0);
        }

        /// <summary>The player's KH_CollarTag (GetPlayerObjects + GetComponent, as KD_PetFxBox.FindTagOf).</summary>
        private KH_CollarTag FindTagOf(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return null;
            GameObject[] objs = Networking.GetPlayerObjects(player);
            if (objs == null) return null;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] == null) continue;
                KH_CollarTag tag = objs[i].GetComponent<KH_CollarTag>();
                if (tag != null) return tag;
            }
            return null;
        }

        /// <summary>This player's leash pattern (product key; the default before the restore).</summary>
        // net: callable-ok: read only
        public string LocalProduct() { return _local == Unset ? defaultProductKey : _local; }

        /// <summary>Harness read-out: the option index last written on this dog's rope / snap / handle (0 = basic).</summary>
        // net: callable-ok: read only
        public int AppliedOption(int dog) { return _applied != null && dog >= 0 && dog < _applied.Length ? _applied[dog] : -1; }

        // net: callable-ok: read only
        public bool RestoreSeen() { return _restoreSeen; }
    }
}
