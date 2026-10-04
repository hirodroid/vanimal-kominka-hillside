using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    // IMP-ENV-010 (temporary, remove before release): one Use button of the KH_PerfProbe row. Local only, no sync.
    // Udon cannot pass an argument through a call on another behaviour, so each group has its own _ToggleN.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_PerfProbeButton : UdonSharpBehaviour
    {
        public KH_PerfProbe probe;
        public int group;

        public override void Interact()
        {
            if (probe == null) return;
            switch (group)
            {
                case 0: probe._Toggle0(); break;
                case 1: probe._Toggle1(); break;
                case 2: probe._Toggle2(); break;
                case 3: probe._Toggle3(); break;
                case 4: probe._Toggle4(); break;
                case 5: probe._Toggle5(); break;
                case 6: probe._Toggle6(); break;
                case 7: probe._Toggle7(); break;
                case 8: probe._Toggle8(); break;
                case 9: probe._Toggle9(); break;
                case 10: probe._Toggle10(); break;
            }
        }
    }
}
