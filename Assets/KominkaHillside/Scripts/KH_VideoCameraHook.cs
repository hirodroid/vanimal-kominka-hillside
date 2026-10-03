using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-008 W1 (手直し 3): lives on the video camera's GameObject and forwards Unity's per-camera OnPostRender (the Udon event
    /// _onPostRender: UdonBehaviour.OnPostRender → RunEvent("_onPostRender"), Runtime/Udon/UdonBehaviour.cs; U# maps the method name through
    /// the Event_OnPostRender node, VRC.Udon.Graph.dll) to the rig, which disables the camera so it renders exactly once per tick.
    /// Local only, sync None, no state. Stays enabled (Unity sends camera messages to enabled behaviours).
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_VideoCameraHook : UdonSharpBehaviour
    {
        public KH_VideoCamera rig;

        void OnPostRender()
        {
            if (rig != null) rig._OnCamRendered();
        }
    }
}
