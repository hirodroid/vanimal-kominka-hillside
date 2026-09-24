using System;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KominkaDogRun.Editor
{
    // 裏山改変: 裏山の入口 (Menu/14 / 14c) と構築は KD_DistanceFadeSettingsEditor.Hillside.cs (partial)。古民家の Setup / Verify は Imported-Unused へ
    public static partial class KD_DistanceFadeSettingsEditor
    {
        public const string SystemName = "KD_DistanceFadeSettings";
        const string ScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DistanceFadeSettings.cs";
        const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DistanceFadeSettings.asset";

        [MenuItem("Tools/KominkaHillside/Menu/14b Survey Distance Fade")]
        public static void Survey()
        {
            var log = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            log.AppendLine("PROJECT " + Application.dataPath + " scene=" + scene.path + " dirty=" + scene.isDirty);
            var reference = Reference();
            log.AppendLine("SHADER " + AssetDatabase.GetAssetPath(reference.shader));
            Collect(reference, log, out _, out _);
            File.WriteAllText("Temp/distance-fade-survey.txt", log.ToString());
        }

        static Material Reference()
        {
            var reference = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(KD_DogDefs.FurReferenceMaterialGuid));
            if (reference == null) reference = AssetDatabase.LoadAssetAtPath<Material>(KD_DogDefs.FurReferenceMaterialPath);
            if (reference == null || !reference.HasProperty("_DistanceFade") || !reference.HasProperty("_DistanceFadeColor"))
                throw new InvalidOperationException("Distance fade reference is missing");
            return reference;
        }

        static void Collect(Material reference, StringBuilder log, out Renderer[] renderers, out int[] slots)
        {
            var rs = new List<Renderer>(); var ss = new List<int>();
            foreach (var def in KD_DogDefs.All)
            {
                int count;
                var dog = KD_SceneObjects.Find(def.DogName, out count);
                if (count != 1 || dog == null) throw new InvalidOperationException("Dog missing/duplicate: " + def.DogName);
                int targets = 0;
                foreach (var r in dog.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var mat = mats[i];
                        if (mat == null || mat.shader != reference.shader) continue;
                        if (mat.GetVector("_DistanceFade") != reference.GetVector("_DistanceFade")
                            || mat.GetColor("_DistanceFadeColor") != reference.GetColor("_DistanceFadeColor"))
                            throw new InvalidOperationException("Different baseline: " + def.key + "/" + mat.name);
                        rs.Add(r); ss.Add(i); targets++;
                        log.AppendLine(def.key + "/" + r.name + "[" + i + "] " + AssetDatabase.GetAssetPath(mat)
                            + " fade=" + mat.GetVector("_DistanceFade").ToString("R") + " color=" + mat.GetColor("_DistanceFadeColor").ToString("R")
                            + " mode=" + mat.GetFloat("_DistanceFadeMode") + " rim=" + mat.GetColor("_DistanceFadeRimColor"));
                    }
                }
                if (targets == 0) throw new InvalidOperationException("No fade targets: " + def.key);
            }
            // 裏山改変: 猫は裏山に居ない (IMP-CAT-001 の分岐は通さない)。CollectCat は古民家の写しとして残す
            log.AppendLine("cat: none in Hillside (skipped)");
            renderers = rs.ToArray(); slots = ss.ToArray();
            log.AppendLine("TARGETS " + rs.Count);
        }

        // IMP-CAT-001: 猫はシーンに居るときだけ混ぜる (犬の収集・例外・ログは変えない)。
        // 居ない間は従来どおり犬だけで通るので、猫ユニットの投入順に依存しない。
        static void CollectCat(Material reference, StringBuilder log, List<Renderer> rs, List<int> ss)
        {
            int count;
            var cat = KD_SceneObjects.Find(KD_CatMaterialEditor.CatRootName, out count);
            if (count > 1) throw new InvalidOperationException("Cat duplicate: " + KD_CatMaterialEditor.CatRootName);
            if (count != 1 || cat == null) { log.AppendLine("cat: not in scene (skipped)"); return; }
            int targets = 0;
            foreach (var r in cat.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null || mat.shader != reference.shader) continue;
                    if (mat.GetVector("_DistanceFade") != reference.GetVector("_DistanceFade")
                        || mat.GetColor("_DistanceFadeColor") != reference.GetColor("_DistanceFadeColor"))
                        throw new InvalidOperationException("Different baseline: cat/" + mat.name);
                    rs.Add(r); ss.Add(i); targets++;
                    log.AppendLine("cat/" + r.name + "[" + i + "] " + AssetDatabase.GetAssetPath(mat)
                        + " fade=" + mat.GetVector("_DistanceFade").ToString("R") + " color=" + mat.GetColor("_DistanceFadeColor").ToString("R")
                        + " mode=" + mat.GetFloat("_DistanceFadeMode") + " rim=" + mat.GetColor("_DistanceFadeRimColor"));
                }
            }
            // 猫が居て毛が素のままなのは「Cat/4 未実行」であって距離フェードの欠陥ではない。
            // ここで throw すると犬の設定まで止まるので記録だけ残す (Cat/6 - Verify Cat Materials が FAIL を出す)
            if (targets == 0) log.AppendLine("cat: no fade targets (Cat/4 - Setup Cat Materials を先に流す)");
        }

        [MenuItem("Tools/KominkaHillside/Menu/14a Create Distance Fade Programs")]
        public static void CreateProgram()
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) != null) return;
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
            AssetDatabase.CreateAsset(asset, ProgramPath);
            AssetDatabase.SaveAssets();
        }

        // 裏山改変: 古民家の Setup (KD_Systems へ置く) は隔離。裏山は SetupHillside (Menu/14)
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Distance Fade/14 - Setup Distance Fade (Kominka)")]
        public static void Setup()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
            var reference = Reference();
            var log = new StringBuilder();
            Collect(reference, log, out var renderers, out var slots); // Preflight before mutation.
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            if (pouch.settingsHome == null) throw new InvalidOperationException("Settings home missing");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) == null) throw new InvalidOperationException("Create program and compile first");
            int count;
            var go = KD_SceneObjects.Find(SystemName, out count);
            if (count > 1) throw new InvalidOperationException("Duplicate fade settings");
            if (go == null)
            {
                go = new GameObject(SystemName);
                Undo.RegisterCreatedObjectUndo(go, "Create distance fade settings");
                KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.SystemsPath);
            }
            var settings = go.GetComponent<KD_DistanceFadeSettings>();
            if (settings == null) settings = go.AddUdonSharpComponent<KD_DistanceFadeSettings>();
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            udon.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            settings.targetRenderers = renderers; settings.targetSlots = slots; settings.targetShader = reference.shader;
            settings.standardColor = reference.GetColor("_DistanceFadeColor");
            Vector4 fade = reference.GetVector("_DistanceFade");
            settings.standardValues = new Vector3(fade.x, fade.y, fade.z);
            settings.selectedColor = pouch.tabOnColor; settings.unselectedColor = KD_UiSpriteEditor.ActionNeutral;
            pouch.distanceFadeSettings = settings;
            KD_ShopSetupEditor.AddGlyphs(Words(), log);
            BuildUI(pouch, settings, log);
            // IMP-DOG-036: 入口ボタンの Label は KD_DistanceFadeSettings.EntryButton に束縛する。この Setup 単体は KD_Language を
            // 更新しないので、Menu Finish の Configure/Bind を流し直して旧キーの束縛が残らないようにする
            KD_MenuFinishEditor.RebindIfPresent(log);
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(udon); EditorUtility.SetDirty(pouch);
            UdonSharpEditorUtility.CopyProxyToUdon(settings); UdonSharpEditorUtility.CopyProxyToUdon(pouch);
            EditorSceneManager.MarkSceneDirty(go.scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/distance-fade-setup.txt", log.ToString());
        }

        // Called by the existing UI builder too: regeneration must retain this page.
        internal static void RebuildIfPresent(KD_TreatPouch pouch, StringBuilder log)
        {
            var go = KD_SceneObjects.Find(SystemName);
            if (go == null) return;
            var settings = go.GetComponent<KD_DistanceFadeSettings>();
            if (settings == null) return;
            pouch.distanceFadeSettings = settings;
            BuildUI(pouch, settings, log);
            EditorUtility.SetDirty(settings);
            UdonSharpEditorUtility.CopyProxyToUdon(settings);
        }

        internal static string Words()
        {
            string words = "選択中: #0123456789ABCDEF.> =m";
            foreach (var field in typeof(KD_DistanceFadeSettings).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.IsLiteral && field.FieldType == typeof(string)) words += (string)field.GetRawConstantValue();
            return words;
        }

        static void BuildUI(KD_TreatPouch pouch, KD_DistanceFadeSettings settings, StringBuilder log)
        {
            var font = pouch.sectionTitleLabel.font;
            var pu = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            var su = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            var home = pouch.settingsHome.transform;
            // IMP-DOG-036: 文言は全端末で「距離フェード (PCのみ)」。押せるかは KD_DistanceFadeSettings.Start() が端末で決める
            settings.entryButton = KD_ShopSetupEditor.EnsureButton(home, "DistanceFadeSettingsButton", KD_MenuFinishEditor.HillsideFadeEntryPos /* 裏山改変: 設定ホームの配置の正本は KD_MenuFinishEditor.Hillside */, KD_MenuFinishEditor.HillsideEntrySize,
                KD_UiSpriteEditor.ActionNeutral, font, KD_DistanceFadeSettings.EntryButton, pu, "_OpenDistanceFadeSettings", log).GetComponent<Button>();
            var page = KD_ShopSetupEditor.EnsureUiChild(pouch.settingsPage.transform, "DistanceFadeSettings", Vector2.zero, new Vector2(760, 520));
            pouch.distanceFadeSettingsPage = page;
            string[] names = { KD_DistanceFadeSettings.Standard, KD_DistanceFadeSettings.Deep, KD_DistanceFadeSettings.Soft, KD_DistanceFadeSettings.Custom };
            string[] events = { "_SelectStandard", "_SelectDeep", "_SelectSoft", "_SelectCustom" };
            settings.presetButtons = new Button[4]; settings.presetImages = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var button = KD_ShopSetupEditor.EnsureButton(page.transform, "Preset" + i, new Vector2(-258 + 172*i, 100),
                    new Vector2(160, 52), i == 0 ? settings.selectedColor : settings.unselectedColor, font, names[i], su, events[i], log);
                settings.presetButtons[i] = button.GetComponent<Button>();
                settings.presetImages[i] = button.GetComponent<Image>();
                settings.presetButtons[i].interactable = false;
                button.transform.Find("Label").GetComponent<TextMeshProUGUI>().fontSize = 24;
            }
            settings.selectionLabel = Label(page.transform, "Selection", new Vector2(-175, 51), new Vector2(320, 32), 22, "選択中: 標準", font);
            settings.colorInput = Input(page.transform, "Color", -175, -10, KD_DistanceFadeSettings.ColorLabel, "#" + ColorUtility.ToHtmlStringRGB(settings.standardColor), font);
            settings.startInput = Input(page.transform, "Start", 175, -10, KD_DistanceFadeSettings.StartLabel, settings.standardValues.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), font);
            settings.endInput = Input(page.transform, "End", -175, -98, KD_DistanceFadeSettings.EndLabel, settings.standardValues.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), font);
            settings.strengthInput = Input(page.transform, "Strength", 175, -98, KD_DistanceFadeSettings.StrengthLabel, settings.standardValues.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture), font);
            settings.errorLabel = Label(page.transform, "Error", new Vector2(0, -158), new Vector2(688, 28), 18, KD_DistanceFadeSettings.Loading, font);
            KD_ShopSetupEditor.EnsureButton(page.transform, "BackSettingsButton", new Vector2(-250, -204), new Vector2(180, 44),
                KD_UiSpriteEditor.ActionNeutral, font, KD_TreatPouch.WordBackSettings, pu, "_OpenSettings", log);
            Label(page.transform, "Scope", new Vector2(175, 51), new Vector2(320, 27), 18, KD_DistanceFadeSettings.ScopeNote, font);
            Label(page.transform, "InputNote", new Vector2(160, -242), new Vector2(360, 27), 18, KD_DistanceFadeSettings.InputNote, font);
            var confirm = KD_ShopSetupEditor.EnsureButton(page.transform, "ConfirmCustomButton", new Vector2(200, -204), new Vector2(280, 44),
                KD_UiSpriteEditor.ActionEquip, font, KD_DistanceFadeSettings.Confirm, su, "_ConfirmCustom", log);
            settings.confirmButton = confirm.GetComponent<Button>();
            settings.confirmButton.interactable = false;
            confirm.SetActive(false);
            page.SetActive(false);
        }

        static TMP_InputField Input(Transform parent, string name, float x, float y, string title, string initial,
            TMP_FontAsset font)
        {
            Label(parent, name + "Title", new Vector2(x, y+26), new Vector2(320, 32), 22, title, font);
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name + "Input", new Vector2(x, y-18), new Vector2(320, 48));
            var bg = go.GetComponent<Image>(); if (bg == null) bg = go.AddComponent<Image>();
            bg.color = KD_UiSpriteEditor.ActionNeutral; bg.raycastTarget = true;
            KD_UiSpriteEditor.ApplyRoundSkin(bg, KD_UiSpriteEditor.LoadRound());
            var field = go.GetComponent<TMP_InputField>(); if (field == null) field = go.AddComponent<TMP_InputField>();
            var area = KD_ShopSetupEditor.EnsureUiChild(go.transform, "TextArea", Vector2.zero, new Vector2(296, 42));
            if (area.GetComponent<RectMask2D>() == null) area.AddComponent<RectMask2D>();
            var text = Label(area.transform, "Text", Vector2.zero, new Vector2(296, 42), 24, initial, font);
            field.textViewport = area.GetComponent<RectTransform>(); field.textComponent = text;
            field.targetGraphic = bg; field.contentType = TMP_InputField.ContentType.Standard;
            field.lineType = TMP_InputField.LineType.SingleLine; field.characterLimit = 32;
            field.richText = false; text.richText = false; field.onFocusSelectAll = true;
            field.readOnly = false; field.interactable = false;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.SetTextWithoutNotify(initial);
            while (field.onEndEdit.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(field.onEndEdit, 0);
            while (field.onSubmit.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(field.onSubmit, 0);
            while (field.onValueChanged.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(field.onValueChanged, 0);
            var system = KD_SceneObjects.Find(SystemName).GetComponent<KD_DistanceFadeSettings>();
            UnityEventTools.AddStringPersistentListener(field.onValueChanged, UdonSharpEditorUtility.GetBackingUdonBehaviour(system).SendCustomEvent, "_DraftChanged");
            return field;
        }

        static TextMeshProUGUI Label(Transform parent, string name, Vector2 pos, Vector2 size, float points, string value, TMP_FontAsset font)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var text = go.GetComponent<TextMeshProUGUI>(); if (text == null) text = go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = points; text.text = value; text.color = Color.white;
            text.enableAutoSizing = false; text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.MidlineLeft; text.raycastTarget = false;
            return text;
        }

        // IMP-DOG-036: 設定ホームの入口は entryButton に配線し、Label は KD_DistanceFadeSettings.EntryButton (全言語で "PC" を含む) に束縛。
        // Label の実 Rect (ボタン 340x64 から 24x4 縮んだ 316x60) で、自動縮小の下限 (14pt) まで縮めても 1 行に収まらなければ FAIL。押せるかは Start() が端末で決める
        static void VerifyEntryButton(KD_DistanceFadeSettings settings, KD_TreatPouch pouch, Action<bool, string> check)
        {
            var button = pouch.settingsHome.transform.Find("DistanceFadeSettingsButton")?.GetComponent<Button>();
            check(button != null && settings.entryButton == button, "entry button wired to settings home (IMP-DOG-036)");
            var language = settings.language;
            check(language != null, "language wired for entry button text");
            var label = button == null ? null : button.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            int binding = label == null || language == null ? -1 : Array.IndexOf(language.fixedLabels, label);
            check(binding >= 0 && language.fixedKeys[binding] == "KD_DistanceFadeSettings.EntryButton", "entry button label bound to KD_DistanceFadeSettings.EntryButton");
            if (binding < 0) return;
            var probe = new GameObject("TextMeasure", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            probe.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var t = probe.GetComponent<TextMeshProUGUI>(); t.isOrthographic = true;
                foreach (string code in language.languageCodes)
                {
                    string value = language._TextForLanguage("KD_DistanceFadeSettings.EntryButton", code);
                    check(value.Contains("PC"), "entry button says PC only (" + code + ")");
                    // IMP-INF-010: 自動縮小が入っているので fontSizeMax → fontSizeMin の 2 段で測る。
                    // 下限まで縮めても入らないときだけ FAIL (下限張り付きは「入っていない」= vrc-tmp-font §4)
                    int fit = KD_LocalizationEditor.MeasureLabelFit(t, label, value, out Vector2 preferred);
                    check(fit != 2, "entry button label fits one line (" + code + ") fit=" + fit + " preferred=" + preferred + " rect=" + label.rectTransform.rect.size);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
        }

        // 裏山改変: 古民家の Verify (音量の入口を兄弟に要求する) は隔離。裏山は VerifyHillside (Menu/14c)
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Distance Fade/14c - Verify Distance Fade (Kominka)")]
        public static void Verify()
        {
            var log = new StringBuilder(); int fail = 0;
            Action<bool,string> check = (ok,msg) => { log.AppendLine((ok ? "PASS " : "FAIL ") + msg); if (!ok) fail++; };
            VerifyShared(log, check);
            log.AppendLine("RESULT " + (fail == 0 ? "PASS" : "FAIL") + " failures=" + fail);
            File.WriteAllText("Temp/distance-fade-verify.txt", log.ToString());
            if (fail > 0) Debug.LogError("Distance fade verification failed: " + fail);
        }

        /// <summary>裏山改変: 古民家の検査本体 (配線・入口・頁・入力欄・字形・論理) を裏山の Menu/14c (VerifyHillside) からも使う。</summary>
        static void VerifyShared(StringBuilder log, Action<bool,string> check)
        {
            var settings = KD_SceneObjects.Find(SystemName).GetComponent<KD_DistanceFadeSettings>();
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var reference = Reference();
            Collect(reference, log, out var renderers, out var slots);
            check(settings.standardColor == reference.GetColor("_DistanceFadeColor") && settings.standardValues == (Vector3)reference.GetVector("_DistanceFade"), "exact material baseline");
            check(settings.targetRenderers.Length == renderers.Length && settings.targetSlots.Length == slots.Length, "target count=" + slots.Length);
            for (int i = 0; i < slots.Length; i++) check(settings.targetRenderers[i] == renderers[i] && settings.targetSlots[i] == slots[i], "target " + i);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(settings);
            check(backing != null && backing.SyncMethod == VRC.SDKBase.Networking.SyncType.None, "local sync=None");
            foreach (string fieldName in new[] { "standardColor", "standardValues", "targetShader", "confirmButton", "entryButton", "colorInput", "startInput", "endInput", "strengthInput", "errorLabel", "selectionLabel", "selectedColor", "unselectedColor" })
            {
                object value;
                check(backing.publicVariables.TryGetVariableValue(fieldName, out value)
                    && Equals(value, typeof(KD_DistanceFadeSettings).GetField(fieldName).GetValue(settings)), "backing value " + fieldName);
            }
            foreach (string fieldName in new[] { "targetRenderers", "targetSlots", "presetButtons", "presetImages" })
            {
                object value; backing.publicVariables.TryGetVariableValue(fieldName, out value);
                var actual = value as Array; var expected = (Array)typeof(KD_DistanceFadeSettings).GetField(fieldName).GetValue(settings);
                bool matches = actual != null && actual.Length == expected.Length;
                if (matches) for (int i = 0; i < actual.Length; i++) matches &= Equals(actual.GetValue(i), expected.GetValue(i));
                check(matches, "backing array " + fieldName);
            }
            check(!UnityEditor.PrefabUtility.IsPartOfPrefabInstance(settings) && KD_SceneObjects.HasStaticAncestry(settings.gameObject, out _), "static standalone system");
            check(pouch.distanceFadeSettings == settings && pouch.distanceFadeSettingsPage.transform.parent == pouch.settingsPage.transform, "page and runtime wired");
            check(pouch.settingsHome.transform.Find("DistanceFadeSettingsButton") != null && pouch.settingsHome.transform.Find("VolumeSettingsButton") != null, "sibling entries");
            var page = pouch.distanceFadeSettingsPage;
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            object fadeReference;
            check(pouchBacking.publicVariables.TryGetVariableValue("distanceFadeSettings", out fadeReference)
                && (Equals(fadeReference, backing) || Equals(fadeReference, settings)), "pouch backing references fade system");
            CheckButton(pouch.settingsHome.transform.Find("DistanceFadeSettingsButton").GetComponent<Button>(), pouchBacking, "_OpenDistanceFadeSettings", check);
            VerifyEntryButton(settings, pouch, check);
            CheckButton(pouch.settingsHome.transform.Find("VolumeSettingsButton").GetComponent<Button>(), pouchBacking, "_OpenVolumeSettings", check);
            CheckButton(page.transform.Find("BackSettingsButton").GetComponent<Button>(), pouchBacking, "_OpenSettings", check);
            CheckButton(settings.confirmButton, backing, "_ConfirmCustom", check);
            check(!settings.confirmButton.gameObject.activeSelf, "confirm hidden for initial standard preset");
            string[] presetEvents = { "_SelectStandard", "_SelectDeep", "_SelectSoft", "_SelectCustom" };
            for (int i = 0; i < 4; i++) CheckButton(settings.presetButtons[i], backing, presetEvents[i], check);
            check(!page.activeSelf && page.GetComponentsInChildren<Slider>(true).Length == 0, "hidden initial page; no sliders");
            var noteBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(pouch.panelRoot.transform, page.transform.Find("InputNote"));
            var hintBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(pouch.panelRoot.transform, pouch.closeHintLabel.transform);
            check(noteBounds.min.x > hintBounds.max.x, "draft note does not overlap Desktop close hint");
            var fields = page.GetComponentsInChildren<TMP_InputField>(true);
            check(fields.Length == 4, "four direct inputs");

            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                var calls = new SerializedObject(field).FindProperty("m_OnEndEdit.m_PersistentCalls.m_Calls");
                check(calls.arraySize == 0 && field.onSubmit.GetPersistentEventCount() == 0, "no automatic commit binding " + field.name);
                var changes = new SerializedObject(field).FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
                check(changes.arraySize == 1 && changes.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == backing
                    && changes.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == "_DraftChanged", "draft notice binding " + field.name);
                check(field.textViewport.GetComponent<RectMask2D>() != null && !field.richText && field.characterLimit == 32, "input mask/plain/limit " + field.name);
            }
            foreach (var text in page.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                bool glyphs = true; foreach (char c in text.text) if (!char.IsWhiteSpace(c) && !text.font.HasCharacter(c, true)) glyphs = false;
                check(glyphs, "glyphs " + text.name);
                // Set isOrthographic only on a temporary initialized text for reliable inactive UI measurement.
                var probe = new GameObject("TextMeasure", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                probe.hideFlags = HideFlags.HideAndDontSave;
                var t = probe.GetComponent<TextMeshProUGUI>(); t.font = text.font; t.fontSize = text.fontSize; t.isOrthographic = true;
                t.enableWordWrapping = false;
                Vector2 preferred = t.GetPreferredValues(text.text, 10000, 0);
                check(preferred.x <= text.rectTransform.rect.width + 1 && preferred.y <= text.rectTransform.rect.height + 1,
                    "text fits " + text.name + " preferred=" + preferred + " rect=" + text.rectTransform.rect.size);
                UnityEngine.Object.DestroyImmediate(probe);
            }
            VerifyLogic(reference, check);
        }

        static void VerifyLogic(Material reference, Action<bool,string> check)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("FadeFixture");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, preview);
            var mat = new Material(reference);
            try
            {
                var settings = go.AddComponent<KD_DistanceFadeSettings>();
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = mat;
                settings.standardColor = reference.GetColor("_DistanceFadeColor"); settings.standardValues = (Vector3)reference.GetVector("_DistanceFade");
                settings.targetRenderers = new Renderer[] { renderer }; settings.targetSlots = new[] { 0 }; settings.targetShader = mat.shader;
                Color color; Vector3 values;
                check(settings._TryColor(" #a0B123 ", out color) && !settings._TryColor("#GG0000", out color) && !settings._TryColor("#123", out color), "hex validation");
                check(settings._TryValues(".18", ".05", "1.10", out values) && settings._TryValues("1e-1", "0", "0", out values), "Unity floats; zero strength/end accepted");
                foreach (var invalid in new[] { "NaN", "Infinity", "1e999", "1,2", "", "-1" })
                    check(!settings._TryValues(invalid, ".05", "1", out values), "reject start=" + invalid);
                check(!settings._TryValues(".05", ".05", "1", out values) && !settings._TryValues(".04", ".05", "1", out values), "reject equal/reversed distances");
                const string custom = "1|3|#123456|0.3|0.02|1.25";
                check(settings._RestoreRecord(custom) && settings._CreateRecord() == custom, "custom persistence roundtrip");
                check(!settings._RestoreRecord("1|3|#123456|NaN|0.02|1.25") && settings._CreateRecord() == custom, "bad save is atomic and nonmutating");
                settings._SelectStandard();
                check(settings._CreateRecord() == custom, "selection blocked before PlayerData restore");
                // Exercise the actual UI entry points in an isolated Edit-mode fixture (no LocalPlayer or PlayerData writes).
                typeof(KD_DistanceFadeSettings).GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(settings, true);
                settings._SelectDeep(); settings._SelectSoft(); settings._SelectStandard(); settings._SelectCustom();
                check(settings._CreateRecord() == custom, "actual preset/custom buttons retain last custom");
                settings.colorInput = FixtureInput(go.transform); settings.startInput = FixtureInput(go.transform);
                settings.endInput = FixtureInput(go.transform); settings.strengthInput = FixtureInput(go.transform);
                var error = new GameObject("Error", typeof(RectTransform), typeof(TextMeshProUGUI)); error.transform.SetParent(go.transform, false);
                settings.errorLabel = error.GetComponent<TextMeshProUGUI>();
                var confirm = new GameObject("ConfirmFixture", typeof(RectTransform), typeof(Button)); confirm.transform.SetParent(go.transform, false);
                settings.confirmButton = confirm.GetComponent<Button>();
                settings._RefreshUI();
                check(settings.confirmButton.gameObject.activeSelf && settings.colorInput.interactable, "custom enables input and confirm");
                settings.startInput.SetTextWithoutNotify("NaN"); settings._ConfirmCustom();
                check(settings._CreateRecord() == custom && settings.startInput.text == "NaN" && settings.errorLabel.text != "", "invalid edit retains input with reason and no state mutation");
                settings.startInput.SetTextWithoutNotify("0.4"); settings._ConfirmCustom();
                check(settings._CreateRecord() == "1|3|#123456|0.4|0.02|1.25" && settings.errorLabel.text == "KD_DistanceFadeSettings.Saved", "corrected input commits custom"); // fixture has no KD_Language: L() returns the key
                settings._SelectStandard(); string beforeFocus = settings._CreateRecord(); settings._ConfirmCustom();
                check(settings._CreateRecord() == beforeFocus, "focus without edit keeps preset");
                check(!settings.confirmButton.gameObject.activeSelf && !settings.colorInput.interactable, "preset disables editing and hides confirm");
                settings._SelectCustom(); string beforeDraft = settings._CreateRecord();
                settings.colorInput.SetTextWithoutNotify("#000000");
                check(settings._CreateRecord() == beforeDraft, "draft does not mutate saved custom");
                settings._ConfirmCustom();
                check(settings._CreateRecord().StartsWith("1|3|#000000|"), "explicit confirm stores exact black");
                string savedBlack = settings._CreateRecord();
                settings.colorInput.SetTextWithoutNotify("#FFFFFF"); settings._DraftChanged();
                check(settings.errorLabel.text == "KD_DistanceFadeSettings.Draft" && settings._CreateRecord() == savedBlack, "editing again replaces saved notice without saving draft");
                settings._SelectSoft(); settings._SelectCustom();
                check(settings.colorInput.text == "#000000" && settings._CreateRecord().StartsWith("1|3|#000000|"), "reported black-soft-custom sequence retains black");
                settings.colorInput.SetTextWithoutNotify("#FFFFFF"); settings._SelectDeep(); settings._SelectCustom();
                check(settings.colorInput.text == "#000000", "unconfirmed draft discarded; last confirmed custom preserved");
                check(settings._RestoreRecord("1|1|#123456|0.3|0.02|1.25"), "preset with retained custom restored");
                settings._ApplyCurrent();
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
                check(block.GetVector("_DistanceFade").z == 1.10f && settings._CreateRecord().Contains("#123456|0.3|0.02|1.25"), "deep preset retains custom");
                settings._RestoreRecord(custom); settings._ApplyCurrent(); renderer.GetPropertyBlock(block, 0);
                check(block.GetVector("_DistanceFade").z == 1.25f, "custom reselected");
                settings._RestoreRecord("1|0|#123456|0.3|0.02|1.25"); settings._ApplyCurrent(); renderer.GetPropertyBlock(block, 0);
                check(block.GetVector("_DistanceFade") == reference.GetVector("_DistanceFade"), "standard exact vector including w");
                Color expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? reference.GetColor("_DistanceFadeColor").linear : reference.GetColor("_DistanceFadeColor");
                check(Vector4.Distance(block.GetVector("_DistanceFadeColor"), expected) < .00001f, "color converted exactly once");
                renderer.sharedMaterial = reference; go.SetActive(false); go.SetActive(true); renderer.GetPropertyBlock(block, 0);
                check(block.GetVector("_DistanceFade") == reference.GetVector("_DistanceFade"), "block survives material replacement and reactivation");
                check(mat.GetVector("_DistanceFade") == reference.GetVector("_DistanceFade") && mat.GetColor("_DistanceFadeColor") == reference.GetColor("_DistanceFadeColor"), "source material untouched");
            }
            finally { UnityEngine.Object.DestroyImmediate(mat); EditorSceneManager.ClosePreviewScene(preview); }
        }

        static TMP_InputField FixtureInput(Transform parent)
        {
            var go = new GameObject("InputFixture", typeof(RectTransform), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            return go.GetComponent<TMP_InputField>();
        }

        static void CheckButton(Button button, VRC.Udon.UdonBehaviour target, string handler, Action<bool,string> check)
        {
            var calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            check(calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == target
                && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue == "SendCustomEvent"
                && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == handler, "button binding " + handler);
        }
    
        // ---- KD_DistanceFadePreviewEditor から併合 (IMP-INF-019 C3、2026-09-13) ----
        [MenuItem("Tools/KominkaHillside/Menu/14d Preview Distance Fade UI")]
        public static void Render()
        {
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName).GetComponent<KD_TreatPouch>();
            var scene = EditorSceneManager.NewPreviewScene();
            var previous = RenderTexture.active;
            var rt = new RenderTexture(1140, 780, 24);
            Texture2D image = null;
            try
            {
                var clone = Object.Instantiate(pouch.panelRoot);
                clone.SetActive(false);
                SceneManager.MoveGameObjectToScene(clone, scene);
                foreach (var behaviour in clone.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true)) Object.DestroyImmediate(behaviour);
                foreach (var behaviour in clone.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true)) Object.DestroyImmediate(behaviour);
                foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clone.transform.localScale = Vector3.one * .001f;
                var canvas = clone.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                foreach (Transform child in clone.transform)
                    if (child.name.StartsWith("Page_")) child.gameObject.SetActive(child.name == "Page_Settings");
                var page = clone.transform.Find("Page_Settings");
                page.Find("VolumeSettings").gameObject.SetActive(false);
                page.Find("SettingsHome").gameObject.SetActive(false);
                var fade = page.Find("DistanceFadeSettings"); fade.gameObject.SetActive(true);
                foreach (var input in fade.GetComponentsInChildren<TMP_InputField>(true)) input.interactable = false;
                foreach (var button in fade.GetComponentsInChildren<Button>(true)) button.interactable = true;
                clone.transform.Find("SectionTitle").GetComponent<TextMeshProUGUI>().text = KD_DistanceFadeSettings.Title;
                var hint = clone.transform.Find("CloseHint");
                hint.gameObject.SetActive(true);
                hint.GetComponent<TextMeshProUGUI>().text = pouch.desktopToggleKey.ToString() + " " + KD_TreatPouch.WordToggleHint;
                fade.Find("Error").GetComponent<TextMeshProUGUI>().text = "";
                clone.SetActive(true);
                var camGo = new GameObject("PreviewCamera"); SceneManager.MoveGameObjectToScene(camGo, scene);
                camGo.transform.position = new Vector3(0, 0, -2);
                var cam = camGo.AddComponent<Camera>(); cam.scene = scene;
                cam.orthographic = true; cam.orthographicSize = .26f; cam.aspect = 760f/520f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.1f,.1f,.1f);
                cam.cullingMask = 1 << 31; cam.nearClipPlane = .1f; cam.farClipPlane = 10; cam.targetTexture = rt;
                Canvas.ForceUpdateCanvases();
                foreach (var text in clone.GetComponentsInChildren<TextMeshProUGUI>(true)) text.ForceMeshUpdate(true);
                cam.Render(); RenderTexture.active = rt;
                image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
                File.WriteAllBytes("Temp/distance-fade-ui.png", image.EncodeToPNG());
                foreach (var input in fade.GetComponentsInChildren<TMP_InputField>(true)) input.interactable = true;
                fade.Find("ConfirmCustomButton").gameObject.SetActive(true);
                fade.Find("ColorInput").GetComponent<TMP_InputField>().SetTextWithoutNotify("#000000");
                fade.Find("Selection").GetComponent<TextMeshProUGUI>().text = "選択中: カスタム";
                fade.Find("Preset0").GetComponent<Image>().color = pouch.distanceFadeSettings.unselectedColor;
                fade.Find("Preset3").GetComponent<Image>().color = pouch.distanceFadeSettings.selectedColor;
                Canvas.ForceUpdateCanvases(); cam.Render();
                image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
                File.WriteAllBytes("Temp/distance-fade-ui-custom.png", image.EncodeToPNG());
                fade.Find("Error").GetComponent<TextMeshProUGUI>().text = KD_DistanceFadeSettings.RangeError;
                fade.Find("StartInput").GetComponent<TMP_InputField>().SetTextWithoutNotify("0.01");
                Canvas.ForceUpdateCanvases(); cam.Render();
                image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
                File.WriteAllBytes("Temp/distance-fade-ui-error.png", image.EncodeToPNG());
                cam.targetTexture = null;
            }
            finally
            {
                RenderTexture.active = previous;
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(rt); if (image != null) Object.DestroyImmediate(image);
            }
        }

        // ---- KD_DistanceFadeVmCheckEditor から併合 (IMP-INF-019 C3、2026-09-13) ----
        [MenuItem("Tools/KominkaHillside/Menu/14e Verify VM Parsers")]
        public static void VerifyVmParsers()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DistanceFadeSettings.asset");
            var program = asset.SerializedProgramAsset.RetrieveProgram();
            var vm = VRC.Udon.Editor.UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            Action<string,object> set = (name,value) => program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name),value);
            Func<string,object> get = name => program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name));
            var log = new StringBuilder(); int fail = 0;
            Action<bool,string> check = (ok,msg) => { log.AppendLine((ok ? "PASS " : "FAIL ") + msg); if (!ok) fail++; };
            Action<string> run = name => { vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(name)); if(vm.Interpret()!=0)throw new InvalidOperationException(name); };
            foreach(var raw in new[]{"#000000","#123456","#GG0000"})
            {
                set("__0_raw__param",raw); run("__0__TryColor");
                bool accepted=(bool)get("__0___0__TryColor__ret");
                check(accepted==(raw!="#GG0000"),"VM color "+raw+" result="+accepted);
                if(raw=="#000000")check((Color)get("__0_color__param")==Color.black,"VM exact black output");
            }
            foreach(var strength in new[]{"0.95","0.75","1.10","NaN","Infinity"})
            {
                set("__0_start__param","0.18");set("__0_end__param","0.05");set("__0_strength__param",strength);
                run("__0__TryValues");
                bool accepted=(bool)get("__0___0__TryValues__ret");
                check(accepted==(strength!="NaN"&&strength!="Infinity"),"VM values strength="+strength+" result="+accepted);
            }
            log.AppendLine("RESULT "+(fail==0?"PASS":"FAIL")+" failures="+fail);
            File.WriteAllText("Temp/distance-fade-vm-check.txt",log.ToString());
            if(fail>0)throw new InvalidOperationException(log.ToString());
        }
}
}
