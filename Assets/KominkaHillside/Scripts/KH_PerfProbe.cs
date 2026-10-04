using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.Udon;

namespace KominkaHillside
{
    // IMP-ENV-010 (temporary measurement tool, REMOVE BEFORE RELEASE: Tools/KominkaHillside/Mobile/22).
    // A row of Use buttons near the spawn switches groups of scene content ON/OFF for the local player only, so the fps in
    // VRChat's debug overlay can be read with one group off at a time (baseline -> one group off -> back on -> next group).
    //
    // Local only: no synced variables, no network events. Everything here changes only this client's view.
    // The arrays are filled by KH_PerfProbeEditor (Mobile/20) from the scene; null entries are skipped.
    //
    // Udon exposure (2026-09-30): UnityEngine.Terrain is NOT exposed to Udon (no Terrain module in VRC.Udon.Wrapper.dll),
    // so terrains are held as Behaviour and only Behaviour.enabled is used (hides surface, trees and detail grass; the
    // TerrainCollider is a separate component and stays). Terrain.drawTreesAndFoliage cannot be reached, so group 3
    // switches the one tile that carries detail grass (KH_Terrain, surface + grass) instead of the grass alone.
    //
    // Group 6 (DogUdon) disables the dogs' UdonBehaviours locally: their Update/LateUpdate stop. After turning them back
    // on, the local dog state may disagree with other players (positions, leashes, wash) until rejoin. Accepted for a
    // measurement tool; do not use it in a session that matters.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_PerfProbe : UdonSharpBehaviour
    {
        public const int GroupCount = 11;
        public const int GroupTerrain = 0, GroupScenery = 1, GroupLeaves = 2, GroupEntrance = 3, GroupDogs = 4, GroupRiver = 5, GroupDogUdon = 6;
        // VER-ENV-002 (2026-10-04): the scenery added on 10-04 (KH_RoadsideAutumn + KH_RiverVerge) split by kind, and the finished
        // props (KH_PropFinish, Unity Standard). These renderers are in no other group (Leaves / Scenery are the older content only).
        public const int GroupNewGrass = 7, GroupNewBoards = 8, GroupNewOther = 9, GroupProps = 10;

        // Scene references: left null here (the builder allocates and wires them; see vrc-udonsharp §4 on array defaults).
        public Behaviour[] terrains;          // every Terrain (as Behaviour)
        public Behaviour entranceTerrain;     // KH_Terrain (the tile with detail grass)
        public Renderer[] scenery;
        public Renderer[] leaves;
        public Renderer[] dogs;
        public Renderer[] river;
        public Renderer[] newGrass;
        public Renderer[] newBoards;
        public Renderer[] newOther;
        public Renderer[] props;
        public UdonBehaviour[] dogUdon;
        public TextMeshPro[] labels;
        public string[] names;

        private bool[] _off;
        // enabled state of every element captured when its group goes OFF, restored on ON. Scans include components that
        // are disabled on purpose (wash shells, hidden tools); forcing them all to true would change the baseline.
        private bool[] _savedScenery, _savedLeaves, _savedDogs, _savedRiver, _savedDogUdon, _savedNewGrass, _savedNewBoards, _savedNewOther, _savedProps;

        public void _Toggle0() { Toggle(0); }
        public void _Toggle1() { Toggle(1); }
        public void _Toggle2() { Toggle(2); }
        public void _Toggle3() { Toggle(3); }
        public void _Toggle4() { Toggle(4); }
        public void _Toggle5() { Toggle(5); }
        public void _Toggle6() { Toggle(6); }
        public void _Toggle7() { Toggle(7); }
        public void _Toggle8() { Toggle(8); }
        public void _Toggle9() { Toggle(9); }
        public void _Toggle10() { Toggle(10); }

        private void Toggle(int group)
        {
            if (group < 0 || group >= GroupCount) return;
            if (_off == null) _off = new bool[GroupCount];
            _off[group] = !_off[group];
            bool on = !_off[group];
            switch (group)
            {
                case GroupTerrain:
                case GroupEntrance:
                    ApplyTerrain();
                    break;
                case GroupScenery: _savedScenery = SetRenderers(scenery, on, _savedScenery); break;
                case GroupLeaves: _savedLeaves = SetRenderers(leaves, on, _savedLeaves); break;
                case GroupDogs: _savedDogs = SetRenderers(dogs, on, _savedDogs); break;
                case GroupRiver: _savedRiver = SetRenderers(river, on, _savedRiver); break;
                case GroupDogUdon: _savedDogUdon = SetUdon(dogUdon, on, _savedDogUdon); break;
                case GroupNewGrass: _savedNewGrass = SetRenderers(newGrass, on, _savedNewGrass); break;
                case GroupNewBoards: _savedNewBoards = SetRenderers(newBoards, on, _savedNewBoards); break;
                case GroupNewOther: _savedNewOther = SetRenderers(newOther, on, _savedNewOther); break;
                case GroupProps: _savedProps = SetRenderers(props, on, _savedProps); break;
            }
            UpdateLabel(group);
        }

        // Groups 0 and 3 share KH_Terrain: it is drawn only when neither group is off.
        private void ApplyTerrain()
        {
            bool allOn = !_off[GroupTerrain];
            if (terrains != null)
            {
                for (int i = 0; i < terrains.Length; i++)
                {
                    if (terrains[i] == null) continue;
                    terrains[i].enabled = allOn;
                }
            }
            if (entranceTerrain != null) entranceTerrain.enabled = allOn && !_off[GroupEntrance];
        }

        // OFF: remember each element's enabled and disable it, returning the snapshot. ON: restore the snapshot (never a
        // blanket true) and return null.
        private bool[] SetRenderers(Renderer[] list, bool on, bool[] saved)
        {
            if (list == null) return saved;
            if (!on)
            {
                saved = new bool[list.Length];
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] == null) continue;
                    saved[i] = list[i].enabled;
                    list[i].enabled = false;
                }
                return saved;
            }
            if (saved == null) return null;
            for (int i = 0; i < list.Length && i < saved.Length; i++)
            {
                if (list[i] == null) continue;
                list[i].enabled = saved[i];
            }
            return null;
        }

        private bool[] SetUdon(UdonBehaviour[] list, bool on, bool[] saved)
        {
            if (list == null) return saved;
            if (!on)
            {
                saved = new bool[list.Length];
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] == null) continue;
                    saved[i] = list[i].enabled;
                    list[i].enabled = false;
                }
                return saved;
            }
            if (saved == null) return null;
            for (int i = 0; i < list.Length && i < saved.Length; i++)
            {
                if (list[i] == null) continue;
                list[i].enabled = saved[i];
            }
            return null;
        }

        private void UpdateLabel(int group)
        {
            if (labels == null || group >= labels.Length || labels[group] == null) return;
            string n = names != null && group < names.Length ? names[group] : "Group " + group;
            labels[group].text = n + (_off[group] ? "\nOFF" : "\nON");
        }
    }
}
