using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UdonSharpEditor;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // Human-operated ClientSim preview. Never enters Play, saves a scene, or starts a show.
    public static class KH_HalloweenParadeManualPreview
    {
        [MenuItem("Tools/KominkaHillside/Parade/13 - Open Manual ClientSim Preview")]
        public static void Open()
        {
            if(!EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Enter Play manually and wait for ClientSim before opening this preview.");
            if(SessionState.GetBool("KH.HalloweenParadeHarness",false))
                throw new InvalidOperationException("Wait for the bounded harness to finish.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!="Assets/Scenes/VRCDefaultWorldScene.unity")
                throw new InvalidOperationException("Expected hillside world scene.");
            var manager=UnityEngine.Object.FindObjectsOfType<KH_HalloweenParade>(true).Single(p=>p.gameObject.scene==scene);
            var panel=UnityEngine.Object.FindObjectsOfType<KH_HalloweenParadePanel>(true).Single(p=>p.gameObject.scene==scene);
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            var panelBacking=UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
            if(!Utilities.IsValid(Networking.LocalPlayer)||!Networking.IsOwner(manager.gameObject)||backing==null||panelBacking==null)
                throw new InvalidOperationException("Local ClientSim player and manager authority must be ready.");
            if(!(backing.GetProgramVariable("phase") is int phase)||phase!=KH_HalloweenParade.Idle)
                throw new InvalidOperationException("Finish the current request before enabling manual preview.");
            panelBacking.SendCustomEvent("_Open");
            Debug.Log("PARADE_MANUAL_PANEL_OPEN: paid entitlement is required; no free preview gate. No show started or player moved.");
        }
    }
}
