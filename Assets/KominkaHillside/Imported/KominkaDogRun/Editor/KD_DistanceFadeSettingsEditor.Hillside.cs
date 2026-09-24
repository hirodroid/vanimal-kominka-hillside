using System;
using System.IO;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    /// <summary>裏山改変 (IMP-UI-001 段階 3): 距離フェード (PCのみ) の裏山の入口。
    /// 古民家との差: 系は KH_Dogs 直下 (KD_Systems は作らない)、猫の分岐なし、入口の位置は設定ホームの 3 群 (KD_MenuFinishEditor.Hillside が正本)、
    /// PlayerData のキーは KH_DistanceFade_v1。基準マテリアルは古民家と同じ TORINOSUKE ke_Uzumore.mat (KD_DogDefs.FurReferenceMaterialGuid)。
    /// Android / iOS は入口ボタンを押せなくする (KD_DistanceFadeSettings.Start → entryButton.interactable = Available())。文言「距離フェード (PCのみ)」は全端末で出す。</summary>
    public static partial class KD_DistanceFadeSettingsEditor
    {
        const string HillsideMenu = "Tools/KominkaHillside/Menu/";
        const string HillsideSetupLog = "Temp/menu-rebuild/distance-fade-setup.txt";
        const string HillsideVerifyLog = "Temp/menu-rebuild/distance-fade-verify.txt";

        /// <summary>系・配線・頁と入口を揃える (冪等)。**Menu/11 と Menu/7 の設定面の再構築 (KD_MenuFinishEditor.BuildHillsideSettings) からも呼ぶ** —
        /// 入口と頁の文言は UILocalizationBindings に束縛されているので、Bind より前に部品が居る必要がある。</summary>
        internal static KD_DistanceFadeSettings EnsureHillside(KD_TreatPouch pouch, StringBuilder log)
        {
            if (pouch == null || pouch.settingsHome == null || pouch.settingsPage == null) throw new InvalidOperationException("Settings home missing (先に Tools/KominkaHillside/Menu/7 Build Menu)");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) == null) throw new InvalidOperationException("KD_DistanceFadeSettings の Program Asset が無い (Tools/KominkaHillside/Menu/14a Create Distance Fade Programs → コンパイル)");
            var reference = Reference();
            Collect(reference, log, out var renderers, out var slots); // 変更前に 6 匹の毛を確かめる (基準と違えば止める)
            int count;
            var go = KD_SceneObjects.Find(SystemName, out count);
            if (count > 1) throw new InvalidOperationException("Duplicate " + SystemName);
            if (go == null)
            {
                go = new GameObject(SystemName);
                Undo.RegisterCreatedObjectUndo(go, "Create distance fade settings");
                KD_SceneObjects.PlaceIfNew(go, KD_Hillside.DogsRootName);
                log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + SystemName);
            }
            else if (go.transform.parent == null || go.transform.parent.name != KD_Hillside.DogsRootName)
                log.AppendLine("WARN " + SystemName + " is not directly under " + KD_Hillside.DogsRootName + " (人が動かした? Verify 14c が FAIL にする)");
            var settings = go.GetComponent<KD_DistanceFadeSettings>();
            if (settings == null) settings = go.AddUdonSharpComponent<KD_DistanceFadeSettings>();
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            udon.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            settings.targetRenderers = renderers; settings.targetSlots = slots; settings.targetShader = reference.shader;
            settings.standardColor = reference.GetColor("_DistanceFadeColor");
            Vector4 fade = reference.GetVector("_DistanceFade");
            settings.standardValues = new Vector3(fade.x, fade.y, fade.z);
            settings.selectedColor = pouch.tabOnColor; settings.unselectedColor = KD_UiSpriteEditor.ActionNeutral;
            // Menu/11 の「language を持つ U# へ一括で配る」ループより後に作られることがあるので、ここで明示的に配る
            settings.language = pouch.language;
            pouch.distanceFadeSettings = settings;
            KD_ShopSetupEditor.AddGlyphs(Words(), log);
            BuildUI(pouch, settings, log);
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(udon); EditorUtility.SetDirty(pouch);
            UdonSharpEditorUtility.CopyProxyToUdon(settings);
            log.AppendLine("Distance fade (裏山): targets=" + slots.Length + " key=" + KD_DistanceFadeSettings.SaveKey + " entry=" + KD_MenuFinishEditor.HillsideFadeEntryPos);
            return settings;
        }

        /// <summary>Menu/14。前提: Menu/7 Build Menu (設定面の器)、Program Asset (Menu/14a)。
        /// 系を揃えたあと設定面を組み直して文言を束縛し直す (KD_MenuSettings が居れば KD_MenuFinishEditor.RebuildIfPresent)。シーンは保存しない。</summary>
        [MenuItem(HillsideMenu + "14 Setup Distance Fade")]
        public static void SetupHillside()
        {
            KD_Hillside.Guard();
            var log = new StringBuilder("[KD_DistanceFade] Setup (裏山 段階 3)\n");
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName)?.GetComponent<KD_TreatPouch>();
            if (pouch == null) throw new InvalidOperationException("KD_TreatPouch missing (先に Tools/KominkaHillside/Menu/7 Build Menu)");
            var settings = EnsureHillside(pouch, log);
            KD_MenuFinishEditor.RebuildIfPresent(pouch, log);
            EditorUtility.SetDirty(pouch);
            UdonSharpEditorUtility.CopyProxyToUdon(settings); UdonSharpEditorUtility.CopyProxyToUdon(pouch);
            EditorSceneManager.MarkSceneDirty(settings.gameObject.scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(HillsideSetupLog));
            File.WriteAllText(HillsideSetupLog, log.ToString());
            Debug.Log(log.ToString());
        }

        /// <summary>Menu/14c。古民家の検査本体 (VerifyShared: 基準・対象 6 匹・配線・入口の束縛と「PC」表記と 1 行収まり・頁・入力欄・字形・論理) に、
        /// 裏山の条件 (KH_Dogs 直下・保存キー・入口が表示されている・言語の配線・猫なし) を足す。結果は Temp/menu-rebuild/distance-fade-verify.txt。</summary>
        [MenuItem(HillsideMenu + "14c Verify Distance Fade")]
        public static void VerifyHillside()
        {
            var log = new StringBuilder("[KD_DistanceFade] Verify (裏山 段階 3)\n"); int fail = 0;
            Action<bool, string> check = (ok, msg) => { log.AppendLine((ok ? "PASS " : "FAIL ") + msg); if (!ok) fail++; };
            var go = KD_SceneObjects.Find(SystemName, out int count);
            check(count == 1 && go != null, SystemName + " exists once");
            if (go != null && count == 1)
            {
                var settings = go.GetComponent<KD_DistanceFadeSettings>();
                var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName)?.GetComponent<KD_TreatPouch>();
                check(settings != null && pouch != null, "system and pouch");
                if (settings != null && pouch != null)
                {
                    VerifyShared(log, check);
                    check(go.transform.parent != null && go.transform.parent.name == KD_Hillside.DogsRootName, "system directly under " + KD_Hillside.DogsRootName + " (KD_Systems は作らない)");
                    check(KD_DistanceFadeSettings.SaveKey == "KH_DistanceFade_v1", "PlayerData key KH_DistanceFade_v1 (古民家の KD_DistanceFade_v1 と分ける)");
                    check(settings.targetRenderers.Length == KD_DogDefs.All.Length, "one fur target per dog (" + settings.targetRenderers.Length + " / " + KD_DogDefs.All.Length + ", 猫なし)");
                    var entry = pouch.settingsHome.transform.Find("DistanceFadeSettingsButton");
                    check(entry != null && entry.gameObject.activeSelf, "entry shown on every platform (interactable is decided by Start: Android / iOS = false)");
                    check(entry != null && Vector2.Distance(((RectTransform)entry).anchoredPosition, KD_MenuFinishEditor.HillsideFadeEntryPos) < .01f, "entry at the settings home layout (" + KD_MenuFinishEditor.HillsideFadeEntryPos + ")");
                    var volume = pouch.settingsHome.transform.Find("VolumeSettingsButton");
                    check(volume != null && volume.gameObject.activeSelf, "sibling volume entry shown (段階 3)");
                    check(settings.language != null && settings.language == pouch.language, "language wired (KD_Language)");
                    object wiredLanguage;
                    check(settings.language != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(settings).publicVariables.TryGetVariableValue("language", out wiredLanguage)
                        && (wiredLanguage as UnityEngine.Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(settings.language) || wiredLanguage as UnityEngine.Object == settings.language), "backing Udon holds language");
                    // Start() が Android / iOS で入口を閉じる分岐は U# の #if なので Editor では実行できない。ソースに分岐が残っていることだけ見る (T-011 は実機)
                    var source = File.ReadAllText(ScriptPath);
                    check(source.Contains("#if UNITY_ANDROID || UNITY_IOS") && source.Contains("entryButton.interactable = Available()"), "Android / iOS closes the entry in Start (source check; T-011 on device)");
                }
            }
            log.AppendLine("RESULT " + (fail == 0 ? "PASS" : "FAIL") + " failures=" + fail);
            Directory.CreateDirectory(Path.GetDirectoryName(HillsideVerifyLog));
            File.WriteAllText(HillsideVerifyLog, log.ToString());
            if (fail > 0) Debug.LogError("[KD_DistanceFade] Verify (裏山) failed: " + fail + " (" + HillsideVerifyLog + ")");
            else Debug.Log("[KD_DistanceFade] Verify (裏山) PASS (" + HillsideVerifyLog + ")");
        }
    }
}
