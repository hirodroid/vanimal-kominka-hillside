using System.IO;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    public static class KD_SleepAudioSetupEditor
    {
        const string ScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepAudio.cs";
        const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepAudio.asset";
        public const string ReportPath = "Temp/sleep-audio-verify.txt";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Audio/1 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) != null) return;
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
            AssetDatabase.CreateAsset(asset, ProgramPath);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Audio/2 - Setup")]
        public static void Setup()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */) throw new InvalidOperationException("Wrong scene");
            int found;
            var liveGo = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName, out found);
            if (liveGo == null || found != 1) throw new InvalidOperationException("Live system missing/duplicate");
            var live = liveGo.GetComponent<KD_LiveSystem>();
            var bgmGo = KD_SceneObjects.Find(KD_DogDefs.BgmName, out found);
            if (bgmGo == null || found != 1) throw new InvalidOperationException("BGM missing/duplicate");
            var bgm = bgmGo.GetComponent<KD_BgmPlayer>();
            if (bgm == null || bgm.source == null) throw new InvalidOperationException("BGM not wired");
            CollectSources(live); // Validate all bindings before scene mutation.
            var go = KD_SceneObjects.Find(KD_DogDefs.SleepAudioName, out found);
            if (found > 1) throw new InvalidOperationException("Duplicate sleep audio");
            if (go == null)
            {
                go = new GameObject(KD_DogDefs.SleepAudioName);
                Undo.RegisterCreatedObjectUndo(go, "Create sleep audio");
                KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.PlacesPath);
                // Survey: 畳.001 center=(5.45,.03,1.48), half=(1.91,0,1.43).
                // 5cm margin around the floor; includes the bed and room boundary.
                go.transform.position = new Vector3(5.45f, 1.25f, 1.48f);
            }
            if (PrefabUtility.IsPartOfPrefabInstance(go)) throw new InvalidOperationException("Must be outside prefab");
            var room = go.GetComponent<KD_SleepAudio>();
            if (room == null) room = go.AddUdonSharpComponent<KD_SleepAudio>();
            var box = go.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = Undo.AddComponent<BoxCollider>(go);
                box.size = new Vector3(3.92f, 4.5f, 2.96f);
                box.isTrigger = true;
                go.layer = LayerMask.NameToLayer("Ignore Raycast");
            }
            room.roomBounds = box;
            Configure(room, live);
            WireBgm(bgm, room);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[SleepAudio] Setup applied. Run Verify before saving.");
        }

        // Called by the Live builder after import. Do not silently lose new/replaced tracks.
        public static bool RewireIfPresent(KD_LiveSystem live, StringBuilder log)
        {
            int found;
            var go = KD_SceneObjects.Find(KD_DogDefs.SleepAudioName, out found);
            if (go == null) return true;
            if (found != 1 || go.GetComponent<KD_SleepAudio>() == null) return false;
            Configure(go.GetComponent<KD_SleepAudio>(), live);
            log.AppendLine("   sleep audio: live sources and stereo Timeline refreshed");
            return true;
        }

        public static void WireBgmIfPresent(KD_BgmPlayer bgm)
        {
            int found;
            var go = KD_SceneObjects.Find(KD_DogDefs.SleepAudioName, out found);
            if (go != null && found == 1) WireBgm(bgm, go.GetComponent<KD_SleepAudio>());
        }

        static void WireBgm(KD_BgmPlayer bgm, KD_SleepAudio room)
        {
            Undo.RecordObject(bgm, "Wire sleep audio");
            var so = new SerializedObject(bgm);
            so.FindProperty("sleepAudio").objectReferenceValue = room;
            so.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(bgm);
            Undo.RecordObject(bgm.source, "Initialize music muted");
            bgm.source.mute = true; // Start will unmute only after the local room gain is known.
            EditorUtility.SetDirty(bgm.source);
        }

        static List<AudioSource> CollectSources(KD_LiveSystem live)
        {
            var sources = new List<AudioSource>();
            if (live == null || live.stageDirectors == null || live.stageDirectors.Length != KD_DogDefs.LiveShows.Length)
                throw new InvalidOperationException("Live directors not wired");
            foreach (var director in live.stageDirectors)
            {
                var timeline = director != null ? director.playableAsset as TimelineAsset : null;
                if (timeline == null) throw new InvalidOperationException("Missing timeline");
                int count = 0;
                foreach (var track in timeline.GetOutputTracks())
                {
                    if (!(track is AudioTrack)) continue;
                    var source = director.GetGenericBinding(track) as AudioSource;
                    if (source == null || !source.transform.IsChildOf(director.transform))
                        throw new InvalidOperationException("Audio binding outside stage: " + director.name);
                    if (sources.Contains(source)) throw new InvalidOperationException("Duplicate source binding");
                    sources.Add(source);
                    count++;
                }
                if (count == 0 || director.GetComponentsInChildren<AudioSource>(true).Length != count)
                    throw new InvalidOperationException("Unbound/missing stage audio: " + director.name);
            }
            return sources;
        }

        static void Configure(KD_SleepAudio room, KD_LiveSystem live)
        {
            var sources = CollectSources(live);
            var volumes = new float[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                int oldIndex = room.liveSources == null ? -1 : Array.IndexOf(room.liveSources, source);
                volumes[i] = oldIndex >= 0 && room.liveBaseVolumes != null && oldIndex < room.liveBaseVolumes.Length
                    ? room.liveBaseVolumes[oldIndex] : source.volume;
                Undo.RecordObject(source, "Live spatial audio");
                source.spatialBlend = 0f;
                source.spatialize = false;
                source.dopplerLevel = 0f;
                source.playOnAwake = false;
                source.mute = true;
                source.volume = volumes[i];
                var spatial = source.GetComponent<VRCSpatialAudioSource>();
                if (spatial == null) spatial = Undo.AddComponent<VRCSpatialAudioSource>(source.gameObject);
                Undo.RecordObject(spatial, "Live falloff");
                spatial.EnableSpatialization = false;
                spatial.UseAudioSourceVolumeCurve = true;
                spatial.Gain = 0f;
                spatial.VolumetricRadius = 0f;
                EditorUtility.SetDirty(source);
                EditorUtility.SetDirty(spatial);
            }
            foreach (var director in live.stageDirectors)
            foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks())
            {
                if (!(track is AudioTrack)) continue;
                var so = new SerializedObject(track);
                so.FindProperty("m_TrackProperties.spatialBlend").floatValue = 0f;
                so.ApplyModifiedProperties();
            }
            Undo.RecordObject(room, "Wire live audio");
            room.liveSources = sources.ToArray();
            room.liveBaseVolumes = volumes;
            EditorUtility.SetDirty(room);
            UdonSharpEditorUtility.CopyProxyToUdon(room);
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(room);
            udon.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            EditorUtility.SetDirty(udon);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Audio/3 - Verify")]
        public static void Verify()
        {
            var log = new StringBuilder();
            int fail = 0;
            int found;
            var go = KD_SceneObjects.Find(KD_DogDefs.SleepAudioName, out found);
            var room = go != null ? go.GetComponent<KD_SleepAudio>() : null;
            Check(room != null && found == 1, "one room controller", log, ref fail);
            if (room != null)
            {
                Check(!PrefabUtility.IsPartOfPrefabInstance(go) && KD_SceneObjects.HasStaticAncestry(go, out string offender), "static scene object outside prefab", log, ref fail);
                Check(go.transform.lossyScale == Vector3.one, "room scale 1", log, ref fail);
                var box = room.roomBounds;
                Check(box != null && box.gameObject == go && box.isTrigger && box.size.x > 0f && box.size.y > 0f && box.size.z > 0f && room.fadeDistance > 0f && room.returnSeconds > 0f, "valid trigger and fades", log, ref fail);
                var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(room);
                Check(udon != null && udon.SyncMethod == VRC.SDKBase.Networking.SyncType.None, "backing sync None", log, ref fail);
                CheckStored(room, "roomBounds", log, ref fail);
                CheckStored(room, "liveSources", log, ref fail);
                CheckStored(room, "liveBaseVolumes", log, ref fail);
                var live = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName, out found).GetComponent<KD_LiveSystem>();
                var expected = CollectSources(live);
                Check(room.liveSources != null && room.liveSources.Length == expected.Count && room.liveBaseVolumes.Length == expected.Count, "all sources wired count=" + expected.Count, log, ref fail);
                for (int i = 0; i < expected.Count; i++)
                {
                    var a = expected[i];
                    var s = a.GetComponent<VRCSpatialAudioSource>();
                    Check(room.liveSources[i] == a && room.liveBaseVolumes[i] >= 0f && room.liveBaseVolumes[i] <= 1f, a.name + " binding/base=" + room.liveBaseVolumes[i], log, ref fail);
                    Check(a.spatialBlend == 0f && !a.spatialize && a.dopplerLevel == 0f && !a.playOnAwake && a.mute, a.name + " stereo/initially muted", log, ref fail);
                    Check(s != null && !s.EnableSpatialization && s.UseAudioSourceVolumeCurve && s.Gain == 0f && s.VolumetricRadius == 0f, a.name + " VRC stereo", log, ref fail);
                }
                foreach (var d in live.stageDirectors)
                foreach (var track in ((TimelineAsset)d.playableAsset).GetOutputTracks())
                    if (track is AudioTrack) Check(new SerializedObject(track).FindProperty("m_TrackProperties.spatialBlend").floatValue == 0f, d.name + " Timeline stereo", log, ref fail);
                var bgm = KD_SceneObjects.Find(KD_DogDefs.BgmName, out found).GetComponent<KD_BgmPlayer>();
                Check(bgm.sleepAudio == room && bgm.source.mute, "BGM wired/initially muted", log, ref fail);
                CheckStored(bgm, "sleepAudio", log, ref fail);
                var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName, out found).GetComponent<KD_TreatPouch>();
                var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
                Check(pouch.sleepAudio == room, "pouch wired to room", log, ref fail);
                foreach (var voice in pouch.dogVoiceSources) Check(voice != null && voice.mute, "voice initially muted: " + voice.name, log, ref fail);
                foreach (var dog in pouch.voiceDogs) Check(dog.breathSource != null && dog.breathSource.mute, "breath initially muted: " + dog.name, log, ref fail);
                foreach (string field in new[] { "sleepAudio", "dogVoiceSlider", "bgmSlider", "liveVolumeSlider", "bedroomBgmVolumeSlider", "bedroomVoiceVolumeSlider", "bedroomLiveVolumeSlider", "bedroomBgmVolumeValueLabel", "bedroomVoiceVolumeValueLabel", "bedroomLiveVolumeValueLabel" }) CheckStored(pouch, field, log, ref fail);
                CheckEvent(pouch.bedroomBgmVolumeSlider, "m_OnValueChanged", "_BedroomBgmVolumeChanged", pouchUdon, log, ref fail);
                CheckEvent(pouch.bedroomVoiceVolumeSlider, "m_OnValueChanged", "_BedroomVoiceVolumeChanged", pouchUdon, log, ref fail);
                CheckEvent(pouch.liveVolumeSlider, "m_OnValueChanged", "_LiveVolumeChanged", pouchUdon, log, ref fail);
                CheckEvent(pouch.bedroomLiveVolumeSlider, "m_OnValueChanged", "_BedroomLiveVolumeChanged", pouchUdon, log, ref fail);
                CheckEvent(pouch.settingsHome.transform.Find("VolumeSettingsButton").GetComponent<UnityEngine.UI.Button>(), "m_OnClick", "_OpenVolumeSettings", pouchUdon, log, ref fail);
                CheckEvent(pouch.volumeSettingsPage.transform.Find("BackSettingsButton").GetComponent<UnityEngine.UI.Button>(), "m_OnClick", "_OpenSettings", pouchUdon, log, ref fail);
                Check(pouch.liveVolumeSlider.value == .5f && pouch.bgmSlider.value == .5f && pouch.dogVoiceSlider.value == .5f && pouch.bedroomLiveVolumeSlider.value == .1f && pouch.bedroomBgmVolumeSlider.value == .1f && pouch.bedroomVoiceVolumeSlider.value == .1f, "all six slider defaults outside 50% / bedroom 10%", log, ref fail);
                // Fixed observed room/bed and audience coordinates, independent of setup-generated arrays.
                Check(room.GainAt(new Vector3(5.18f, 0.30f, 1.86f)) == 0f, "bed lying head silent", log, ref fail);
                Check(room.GainAt(new Vector3(3.55f, 1.7f, .06f)) == 0f && room.GainAt(new Vector3(7.35f, 1.7f, 2.9f)) == 0f, "room corners silent", log, ref fail);
                Check(room.GainAt(new Vector3(-2.5f, 1.3f, 0f)) == 1f && room.GainAt(new Vector3(-5.58f, .5f, 0f)) == 1f, "engawa/garden audible", log, ref fail);
                Check(room.GainAt(new Vector3(5.45f, 1.3f, -1.44f)) == 1f, "adjacent south room unaffected", log, ref fail);
                Vector3 edge = box.transform.TransformPoint(box.center + new Vector3(box.size.x * .5f + room.fadeDistance * .5f, 0f, 0f));
                Check(Mathf.Abs(room.GainAt(edge) - .5f) < .001f, "doorway fade midpoint", log, ref fail);
                log.AppendLine("room center=" + box.transform.TransformPoint(box.center).ToString("F3") + " size=" + box.size.ToString("F3"));
                VerifyBehaviour(log, ref fail);
            }
            var descriptor = UnityEngine.Object.FindObjectOfType<VRCSceneDescriptor>();
            int broken = 0;
            if (descriptor != null) foreach (var entry in descriptor.NetworkIDCollection) if (entry.gameObject == null) broken++;
            log.AppendLine("NetworkIDs count=" + (descriptor == null ? -1 : descriptor.NetworkIDCollection.Count) + " broken=" + broken);
            log.AppendLine("RESULT: " + (fail == 0 ? "PASS" : "FAIL") + " failures=" + fail);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(ReportPath, log.ToString());
            Debug.Log("[SleepAudio] Verify failures=" + fail);
        }

        static void Check(bool ok, string label, StringBuilder log, ref int fail)
        {
            log.AppendLine((ok ? "PASS " : "FAIL ") + label);
            if (!ok) fail++;
        }

        static void CheckEvent(UnityEngine.Object component, string eventName, string method, VRC.Udon.UdonBehaviour target, StringBuilder log, ref int fail)
        {
            var calls = component == null ? null : new SerializedObject(component).FindProperty(eventName + ".m_PersistentCalls.m_Calls");
            bool ok = calls != null && calls.arraySize == 1;
            if (ok)
            {
                var call = calls.GetArrayElementAtIndex(0);
                ok = call.FindPropertyRelative("m_Target").objectReferenceValue == target
                    && call.FindPropertyRelative("m_MethodName").stringValue == "SendCustomEvent"
                    && call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == method;
            }
            Check(ok, "UI callback once: " + method, log, ref fail);
        }

        static void CheckStored(UdonSharpBehaviour proxy, string field, StringBuilder log, ref int fail)
        {
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            object actual = null;
            object expected = proxy.GetType().GetField(field).GetValue(proxy);
            bool ok = udon != null && udon.publicVariables.TryGetVariableValue(field, out actual);
            if (expected is UdonSharpBehaviour behaviour) expected = UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
            if (expected is Array a && actual is Array b)
            {
                ok &= a.Length == b.Length;
                for (int i = 0; i < a.Length && ok; i++) ok &= Equals(a.GetValue(i), b.GetValue(i));
            }
            else ok &= Equals(expected, actual);
            Check(ok, "backing reference: " + proxy.name + "." + field, log, ref fail);
        }

        static void VerifyBehaviour(StringBuilder log, ref int fail)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var host = new GameObject("SleepAudioTest");
                SceneManager.MoveGameObjectToScene(host, preview);
                var room = host.AddUdonSharpComponent<KD_SleepAudio>();
                Check(room.LiveVolume == .5f && room.BedroomLiveVolume == .1f && room.BedroomBgmVolume == .1f && room.BedroomVoiceVolume == .1f, "runtime music defaults 50%/10%", log, ref fail);
                room.roomBounds = host.AddComponent<BoxCollider>();
                room.roomBounds.size = new Vector3(4f, 4f, 4f);
                var source = host.AddComponent<AudioSource>();
                var next = new GameObject("InactiveTrack");
                SceneManager.MoveGameObjectToScene(next, preview);
                var inactive = next.AddComponent<AudioSource>();
                next.SetActive(false);
                room.liveSources = new[] { source, inactive };
                room.liveBaseVolumes = new[] { .75f, .5f };
                var step = typeof(KD_SleepAudio).GetMethod("UpdateGain", BindingFlags.Instance | BindingFlags.NonPublic);
                Action<float, bool, float> advance = (target, known, seconds) => step.Invoke(room, new object[] { target, known, seconds });
                room._SetLiveVolume(.8f);
                Check(source.mute && inactive.mute, "unknown local position silences current and next track", log, ref fail);
                advance(room.GainAt(Vector3.zero), true, .1f);
                room._SetBedroomLiveVolume(.5f);
                Check(Mathf.Approximately(source.volume, .375f) && Mathf.Approximately(inactive.volume, .25f) && !source.mute, "bedroom live 50% is independent of outside 80%", log, ref fail);
                inactive.gameObject.SetActive(true);
                Check(Mathf.Approximately(inactive.volume, .25f), "newly activated track already has room gain", log, ref fail);
                room._SetBedroomLiveVolume(1f);
                Check(Mathf.Approximately(source.volume, .75f), "bedroom 100% is full source base volume", log, ref fail);
                room._SetBedroomLiveVolume(0f);
                Check(source.mute && inactive.mute, "bedroom 0% silences ongoing tracks", log, ref fail);
                advance(room.GainAt(new Vector3(8f, 0f, 0f)), true, .1f);
                Check(Mathf.Approximately(room.VolumeFor(1f, 0f), .25f), "exit restores gradually in 0.4 seconds", log, ref fail);
                advance(1f, true, .3f);
                Check(Mathf.Approximately(source.volume, .6f), "outside restores selected live volume", log, ref fail);
                advance(0f, true, .01f);
                Check(source.mute, "teleport into bedroom immediately applies zero", log, ref fail);

                var bgmHost = new GameObject("BgmTest");
                SceneManager.MoveGameObjectToScene(bgmHost, preview);
                var bgm = bgmHost.AddUdonSharpComponent<KD_BgmPlayer>();
                bgm.source = bgmHost.AddComponent<AudioSource>();
                bgm.sleepAudio = room;
                bgm.baseVolume = .35f;
                room._SetBedroomBgmVolume(.5f);
                bgm._SetBgm(.6f, true);
                Check(Mathf.Approximately(bgm.source.volume, .175f), "bedroom BGM 50% ignores outside 60%", log, ref fail);
                var duck = typeof(KD_BgmPlayer).GetField("_duck", BindingFlags.Instance | BindingFlags.NonPublic);
                var apply = typeof(KD_BgmPlayer).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic);
                duck.SetValue(bgm, 0f); apply.Invoke(bgm, null);
                Check(bgm.source.volume == 0f, "live duck silences BGM", log, ref fail);
                room._SetBedroomBgmVolume(0f);
                duck.SetValue(bgm, 1f); apply.Invoke(bgm, null);
                Check(bgm.source.mute && bgm.UserVolume == .6f && bgm.UserEnabled, "live end in bedroom stays silent and preserves BGM setting", log, ref fail);
                bgm._SetBgm(.6f, false);
                advance(1f, true, 1f); apply.Invoke(bgm, null);
                Check(bgm.source.mute && !bgm.UserEnabled, "BGM OFF remains OFF outside", log, ref fail);

                var uiHost = new GameObject("UiTest");
                SceneManager.MoveGameObjectToScene(uiHost, preview);
                var pouch = uiHost.AddUdonSharpComponent<KD_TreatPouch>();
                pouch.sleepAudio = room;
                var panel = new GameObject("Panel");
                panel.transform.SetParent(uiHost.transform);
                panel.SetActive(false);
                pouch.panelRoot = panel;
                pouch.settingsPage = new GameObject("Settings"); pouch.settingsPage.transform.SetParent(panel.transform);
                pouch.settingsHome = new GameObject("Home"); pouch.settingsHome.transform.SetParent(pouch.settingsPage.transform);
                pouch.volumeSettingsPage = new GameObject("Volumes"); pouch.volumeSettingsPage.transform.SetParent(pouch.settingsPage.transform);
                pouch.tabPages = new[] { new GameObject("Tab") }; pouch.tabPages[0].transform.SetParent(panel.transform);
                var sliderGo = new GameObject("LiveSlider", typeof(RectTransform), typeof(UnityEngine.UI.Slider)); sliderGo.transform.SetParent(panel.transform);
                var roomSliderGo = new GameObject("RoomSlider", typeof(RectTransform), typeof(UnityEngine.UI.Slider)); roomSliderGo.transform.SetParent(panel.transform);
                pouch.liveVolumeSlider = sliderGo.GetComponent<UnityEngine.UI.Slider>();
                pouch.bedroomLiveVolumeSlider = roomSliderGo.GetComponent<UnityEngine.UI.Slider>();
                var bgmSliderGo = new GameObject("BgmSlider", typeof(RectTransform), typeof(UnityEngine.UI.Slider)); bgmSliderGo.transform.SetParent(panel.transform);
                var voiceSliderGo = new GameObject("VoiceSlider", typeof(RectTransform), typeof(UnityEngine.UI.Slider)); voiceSliderGo.transform.SetParent(panel.transform);
                var roomBgmGo = new GameObject("RoomBgm", typeof(RectTransform), typeof(UnityEngine.UI.Slider)); roomBgmGo.transform.SetParent(panel.transform);
                var roomVoiceGo = new GameObject("RoomVoice", typeof(RectTransform), typeof(UnityEngine.UI.Slider)); roomVoiceGo.transform.SetParent(panel.transform);
                pouch.bgmSlider = bgmSliderGo.GetComponent<UnityEngine.UI.Slider>();
                pouch.dogVoiceSlider = voiceSliderGo.GetComponent<UnityEngine.UI.Slider>();
                pouch.bedroomBgmVolumeSlider = roomBgmGo.GetComponent<UnityEngine.UI.Slider>();
                pouch.bedroomVoiceVolumeSlider = roomVoiceGo.GetComponent<UnityEngine.UI.Slider>();
                pouch.bgmPlayer = bgm;
                var bark = uiHost.AddComponent<AudioSource>();
                var dogHost = new GameObject("VoiceDog");
                SceneManager.MoveGameObjectToScene(dogHost, preview);
                var dog = dogHost.AddUdonSharpComponent<KD_DogWanderController>();
                dog.breathSource = dogHost.AddComponent<AudioSource>();
                typeof(KD_DogWanderController).GetField("_breathLevel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dog, .4f);
                pouch.voiceDogs = new[] { dog };
                pouch.dogVoiceSources = new[] { bark };
                pouch.dogVoiceBaseVolumes = new[] { .8f };
                // Unity lifecycle events are suppressed on UdonSharp editor proxies. Call the production step directly.
                var updatePouch = typeof(KD_TreatPouch).GetMethod("RefreshVoiceForPosition", BindingFlags.Instance | BindingFlags.NonPublic);
                pouch._OpenSettings();
                Check(pouch.settingsHome.activeSelf && !pouch.volumeSettingsPage.activeSelf && !pouch.tabPages[0].activeSelf, "settings hub navigation", log, ref fail);
                pouch._OpenVolumeSettings();
                Check(!pouch.settingsHome.activeSelf && pouch.volumeSettingsPage.activeSelf, "hub to volume navigation", log, ref fail);
                pouch.liveVolumeSlider.value = .7f; pouch._LiveVolumeChanged();
                pouch.bedroomLiveVolumeSlider.value = .2f; pouch._BedroomLiveVolumeChanged();
                pouch._OpenSettings(); pouch._OpenVolumeSettings();
                Check(room.LiveVolume == .7f && room.BedroomLiveVolume == .2f, "back and reopen preserves live settings", log, ref fail);
                // Outside zero must never multiply away a nonzero bedroom setting.
                pouch.bgmSlider.value = 0f; pouch._BgmChanged();
                pouch.dogVoiceSlider.value = 0f; pouch._DogVoiceChanged();
                pouch.liveVolumeSlider.value = 0f; pouch._LiveVolumeChanged();
                pouch.bedroomBgmVolumeSlider.value = .1f; pouch._BedroomBgmVolumeChanged();
                pouch.bedroomVoiceVolumeSlider.value = .2f; pouch._BedroomVoiceVolumeChanged();
                pouch.bedroomLiveVolumeSlider.value = .3f; pouch._BedroomLiveVolumeChanged();
                advance(0f, true, .01f); apply.Invoke(bgm, null); updatePouch.Invoke(pouch, null);
                Check(Mathf.Approximately(bgm.source.volume, .035f) && !bgm.source.mute, "bedroom BGM 10% plays even with outside 0%", log, ref fail);
                Check(Mathf.Approximately(source.volume, .225f) && !source.mute, "bedroom live 30% plays even with outside 0%", log, ref fail);
                log.AppendLine("VOICE inside bark=" + bark.volume + " mute=" + bark.mute + " dogGain=" + dog.localVoiceGain + " breath=" + dog.breathSource.volume + " mute=" + dog.breathSource.mute);
                Check(Mathf.Approximately(bark.volume, .16f) && !bark.mute && Mathf.Approximately(dog.localVoiceGain, .2f) && Mathf.Approximately(dog.breathSource.volume, .08f) && !dog.breathSource.mute, "bedroom voice 20% controls barking and breathing with closed panel", log, ref fail);
                pouch._OpenSettings(); pouch._OpenVolumeSettings();
                Check(pouch.bgmSlider.value == 0f && pouch.dogVoiceSlider.value == 0f && pouch.liveVolumeSlider.value == 0f
                    && pouch.bedroomBgmVolumeSlider.value == .1f && pouch.bedroomVoiceVolumeSlider.value == .2f && pouch.bedroomLiveVolumeSlider.value == .3f,
                    "all six values remain independent after reopening", log, ref fail);
                advance(1f, true, 1f); apply.Invoke(bgm, null); updatePouch.Invoke(pouch, null);
                Check(bgm.source.mute && source.mute && bark.mute && dog.breathSource.mute, "outside zero silences all three categories after exit", log, ref fail);
                pouch.bgmSlider.value = .6f; pouch._BgmChanged();
                pouch.dogVoiceSlider.value = .7f; pouch._DogVoiceChanged();
                pouch.liveVolumeSlider.value = .8f; pouch._LiveVolumeChanged();
                Check(Mathf.Approximately(bgm.source.volume, .21f) && Mathf.Approximately(bark.volume, .56f) && Mathf.Approximately(source.volume, .6f), "outside three values have distinct outputs", log, ref fail);
                pouch.bedroomBgmVolumeSlider.value = 0f; pouch._BedroomBgmVolumeChanged();
                pouch.bedroomVoiceVolumeSlider.value = 0f; pouch._BedroomVoiceVolumeChanged();
                pouch.bedroomLiveVolumeSlider.value = 0f; pouch._BedroomLiveVolumeChanged();
                advance(0f, true, .01f); apply.Invoke(bgm, null); updatePouch.Invoke(pouch, null);
                Check(bgm.source.mute && source.mute && bark.mute && dog.breathSource.mute, "bedroom zero silences all three regardless of outside values", log, ref fail);
                advance(1f, false, 1f); apply.Invoke(bgm, null); updatePouch.Invoke(pouch, null);
                Check(bgm.source.mute && source.mute && bark.mute && dog.breathSource.mute, "unknown local position silences all categories", log, ref fail);
                pouch._Tab0();
                Check(!pouch.settingsPage.activeSelf && pouch.tabPages[0].activeSelf, "tab exits volume settings", log, ref fail);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Sleep Audio/0 - Survey")]
        public static void Survey()
        {
            var scene = SceneManager.GetActiveScene();
            var log = new StringBuilder("scene=" + scene.path + " dirty=" + scene.isDirty + "\n");
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var a = t.GetComponent<AudioSource>();
                if (a != null) log.AppendLine("AUDIO " + PathOf(t) + " pos=" + t.position.ToString("F2") + " blend=" + a.spatialBlend + " volume=" + a.volume + " mute=" + a.mute + " near=" + a.minDistance + " far=" + a.maxDistance);
                if (t.name.Contains("布団") || t.name.Contains("畳") || t.name.Contains("Futon") || t.name.Contains("Room") || t.name.Contains("Area") || t.name.Contains("SleepKit") || t.name.Contains("防音"))
                {
                    log.AppendLine("PLACE " + PathOf(t) + " pos=" + t.position.ToString("F2") + " scale=" + t.lossyScale.ToString("F2"));
                    foreach (var c in t.GetComponents<Collider>()) log.AppendLine(" COLLIDER " + c.bounds);
                    foreach (var r in t.GetComponents<Renderer>()) log.AppendLine(" RENDERER " + r.bounds);
                }
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/sleep-audio-survey.txt", log.ToString());
        }

        static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    }
}
