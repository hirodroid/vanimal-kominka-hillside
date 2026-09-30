using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside
{
    // IMP-ENV-007 (user decision 2026-09-30): the tools stay where they were put; this small board on the shelf sends
    // every tool of its wash station home when someone presses it (Interact, like KH_MenuOpener / KH_LeashCollar).
    //
    // Local only: the presser asks each tool (KH_WashTool._RequestReturn). A tool somebody holds is skipped (never taken
    // out of a hand); for the others the presser takes the ownership and writes home after the transfer lands, stopping
    // the spray. VRCObjectSync carries the positions to everyone and to late joiners, so nothing here is synced and no
    // network event exists: anyone can press it, and the only entry points are Interact and the local _ResetTools.
    // IMP-ENV-008: every station (the main KH_WashStation and each KH_WashStations/{name} along the course) has its own
    // board wired to its own tools only; a press never moves another station's tools.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_WashStationReset : UdonSharpBehaviour
    {
        public KH_WashTool[] tools;
        [Tooltip("連打の抑制 (秒)")]
        public float cooldown = 1f;

        private float _lastAt = -100f;
        private int _lastSkippedHeld = -1;   // harness read-out: tools skipped as held on the last press

        public override void Interact() { _ResetTools(); }

        public void _ResetTools()
        {
            if (Time.time - _lastAt < cooldown) return;
            _lastAt = Time.time;
            if (tools == null) return;
            int skipped = 0;
            for (int i = 0; i < tools.Length; i++)
            {
                if (tools[i] == null) continue;
                if (!tools[i]._RequestReturn()) skipped++;
            }
            _lastSkippedHeld = skipped;
        }
    }
}
