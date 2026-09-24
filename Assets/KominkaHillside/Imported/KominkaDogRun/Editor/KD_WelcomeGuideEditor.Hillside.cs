using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    /// <summary>裏山 (KominkaHillside) の ようこそ 最小版 (段階 1 IMP-PRG-001)。古民家の本体 (KD_WelcomeGuideEditor.cs) の共通ヘルパーを使い、
    /// 裏山の構築 (Menu/7 から呼ぶ BuildPage、Menu/11b) と検証 (Menu/11c) をここに隔離する (Codex M-3 2026-09-24)。</summary>
    public static partial class KD_WelcomeGuideEditor
    {
        // ================================================================ 裏山 (段階 1 IMP-PRG-001): ようこそ 最小版
        //
        // 古民家の IMP-INF-020 (案 1 庭に開いて置く + 案 2 ようこその面 + 案 4 手元ヒント) をそのまま使い、
        // 裏山で外すもの: 「はじめに」の再読面とインフォメーション (段階 3)・寝台の通知 (呼ぶは段階 2)・写真ビューア・ライブ。
        // 置き場は KH_Dogs 直下 ("====KominkaDogRun====" / KD_Places は作らない)。保存キー KD_Onboarding_v1 は古民家どおり (KD_HintDirector)。
        // 本文は文言表の welcome.* を裏山の値へ差し替えて使う (題「ようこそ、古民家の裏山へ」+ 本文 2 行)。

        const string HillsideMenu = "Tools/KominkaHillside/Menu/";
        /// <summary>庭の置き場の初期値: 入口のスポーン地点から前へ 1.8 m、板の中心は地面 + 1.3 m。**新規作成時だけ** (位置は人が Inspector で詰める)。</summary>
        const float SpotAhead = 1.8f, SpotHeight = 1.3f;
        /// <summary>11c が要求する文言キー (self 言語 ja / en で空でない)。</summary>
        static readonly string[] HillsideKeys = { "welcome.title", "welcome.body", "welcome.footer.vr", "welcome.footer.desktop", "welcome.footer.touch",
            "welcome.later", "welcome.menu", "welcome.language", "hint.hand.vr", "hint.hand.desktop", "hint.hand.touch",
            "hint.menu.vr", "hint.menu.desktop", "hint.menu.touch", "hint.pet", "hint.throw.vr", "hint.throw.desktop", "hint.throw.touch" };
        /// <summary>裏山の題 (文言表の welcome.title の ja)。差し替えの取り込み漏れを 11c が見る。</summary>
        public const string HillsideTitleJa = "ようこそ、古民家の裏山へ";

        /// <summary>ようこその面 (Page_Welcome) の器だけを作る。**Menu/7 Build Menu (段階 1) の板の再構築からも呼ぶ** —
        /// 文言の束縛 (UILocalizationBindings の Page_Welcome/...) は Menu Finish の Bind が毎回要求するので、面は 11b より前に居る必要がある。
        /// 言語の行 (&lt; &gt;) は KD_Language が居るときだけ配線する (無ければ見出しだけ作って WARN。Menu/11 の後に 7 か 11b を再実行)。</summary>
        internal static void BuildPage(KD_TreatPouch pouch, Transform panel, TMP_FontAsset font, UdonBehaviour pouchUdon, StringBuilder log)
        {
            var catalog = KD_LocalizationEditor.Load();
            string T(string key, string fallback) { try { return KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, key); } catch (InvalidOperationException) { log.AppendLine("   WARN 文言キー未登録: " + key); return fallback; } }
            var page = KD_ShopSetupEditor.EnsureUiChild(panel, PageName, Vector2.zero, new Vector2(KD_TreatSetupEditor.PanelWidth, KD_TreatSetupEditor.PanelHeight));
            var scroll = WelcomeBody(page.transform, font, T("welcome.body", ""));
            if (pouch.language != null)
            {
                pouch.language.welcomeScroll = scroll;
                pouch.language.welcomeChoiceLabel = LanguageRow(page.transform, pouch.language, font, log);
            }
            else
            {
                Label(page.transform, "LanguageHeading", new Vector2(-325, LanguageY), new Vector2(410, 48), T("welcome.language", "Language / 言語"), font, 24, TextAlignmentOptions.Left);
                log.AppendLine("   WARN ようこそ: KD_Language が無いので言語の行は見出しだけ (Menu/11 Setup Localization の後に Menu/11b を実行)");
            }
            pouch.welcomeFooterLabel = Label(page.transform, "Footer", FooterPos, FooterSize, T("welcome.footer.vr", ""), font, FooterPoints, TextAlignmentOptions.TopLeft);
            Button(page.transform, "LaterButton", LaterPos, LaterSize, KD_UiSpriteEditor.ActionNeutral, T("welcome.later", "あとで"), font, pouchUdon, "_WelcomeLater", log);
            Button(page.transform, "MenuButton", MenuPos, MenuSize, KD_UiSpriteEditor.ActionPrimary, T("welcome.menu", "メニューを見る"), font, pouchUdon, "_WelcomeMenu", log);
            if (pouch.confirmRoot != null) pouch.confirmRoot.transform.SetAsLastSibling();   // P13: 確認カードは常に最後の子
            page.SetActive(false);
            pouch.welcomePage = page;
            log.AppendLine("   welcome page: " + PageName + " (本文 / 言語 / 足元の 1 行 / [あとで] [メニューを見る])");
        }

        /// <summary>KH_Dogs 直下の子を 1 個だけ取る (無ければ作る)。シーン内に同名が 2 個以上なら例外。</summary>
        static GameObject DogsChild(Transform dogs, string name, StringBuilder log) { return DogsChild(dogs, name, log, out _); }

        static GameObject DogsChild(Transform dogs, string name, StringBuilder log, out bool created)
        {
            created = false;
            KD_SceneObjects.Find(name, out int count);
            var t = dogs.Find(name);
            if (count > 1 || (count == 1 && t == null)) throw new InvalidOperationException(name + " がシーン内に " + count + " 個 (" + KD_Hillside.DogsRootName + " 直下に 1 個だけ置く)");
            if (t != null) return t.gameObject;
            created = true;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(dogs, false);
            log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + name);
            return go;
        }

        /// <summary>World Space の通知・文脈ヒントの札 (KD_BedroomOnboardingEditor.HintPanel と同じ作り)。</summary>
        static TextMeshProUGUI HintPanel(Transform parent, string name, TMP_FontAsset font, Vector2 size)
        {
            var t = parent.Find(name); GameObject go;
            if (t == null) { go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); }
            else go = t.gameObject;
            go.layer = 5;
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.localScale = Vector3.one * .001f;
            var bg = go.GetComponent<Image>(); if (bg == null) bg = go.AddComponent<Image>();
            bg.color = KD_UiSpriteEditor.SurfacePanel; bg.raycastTarget = false;
            var labelGo = KD_ShopSetupEditor.EnsureUiChild(go.transform, "Label", Vector2.zero, size - new Vector2(32, 20));
            labelGo.layer = 5;
            var label = labelGo.GetComponent<TextMeshProUGUI>(); if (label == null) label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = 26; label.color = Color.white; label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = true; label.raycastTarget = false;
            go.SetActive(false); return label;
        }

        [MenuItem(HillsideMenu + "11b Build Welcome")]
        public static void BuildHillside()
        {
            Guard(); var log = new StringBuilder("[KD_WelcomeGuide] Build (裏山 段階 1: ようこそ 最小版)\n");
            var pouch = Find<KD_TreatPouch>(KD_DogDefs.TreatPouchName);
            if (pouch == null || pouch.panelRoot == null || pouch.sectionTitleLabel == null) throw new InvalidOperationException("KD_TreatPouch の板が無い (先に Tools/KominkaHillside/Menu/7 Build Menu)");
            var language = pouch.language;
            if (language == null) throw new InvalidOperationException("KD_TreatPouch.language が無い (先に Tools/KominkaHillside/Menu/11 Setup Localization)");
            var dogs = pouch.transform.parent;
            if (dogs == null || dogs.name != KD_Hillside.DogsRootName) throw new InvalidOperationException("KD_TreatPouch が " + KD_Hillside.DogsRootName + " 直下に無い");
            var font = pouch.sectionTitleLabel.font;
            var catalog = KD_LocalizationEditor.Load();
            string T(string key) { return KD_LocalizationEditor.Text(catalog, catalog.fallbackLanguage, key); }
            if (T("welcome.title") != HillsideTitleJa) log.AppendLine("WARN welcome.title が裏山の値でない (\"" + T("welcome.title") + "\")。司令塔の文言統合 (keys-P3.json の差替) を待つ");
            var missing = KD_ShopSetupEditor.MissingGlyphs(KD_LocalizationEditor.BundledCharacters(catalog));
            if (missing.Length > 0) KD_ShopSetupEditor.AddGlyphs(missing, log);
            var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            if (pouchUdon == null) throw new InvalidOperationException("KD_TreatPouch backing UdonBehaviour missing (force refresh first)");

            // ---- 案 2: ようこその面 (Menu/7 と同じ器。言語の行もここで配線) ----
            BuildPage(pouch, pouch.panelRoot.transform, font, pouchUdon, log);
            log.AppendLine("SKIP 「はじめに」の再読面・インフォメーションの行 (段階 3。足元の 1 行も再読の案内を持たない)");

            // ---- 案 1: 庭の置き場 (KH_Dogs/KD_WelcomeSpot。**位置は新規作成時だけ**) ----
            var spot = DogsChild(dogs, SpotName, log, out bool newSpot);
            if (newSpot)
            {
                var descriptor = Object.FindObjectOfType<VRCSceneDescriptor>();
                Transform spawn = descriptor != null && descriptor.spawns != null && descriptor.spawns.Length > 0 ? descriptor.spawns[0] : null;
                Vector3 origin = spawn != null ? spawn.position : dogs.position;
                Vector3 forward = spawn != null ? spawn.forward : Vector3.forward; forward.y = 0f;
                forward = forward.sqrMagnitude > .001f ? forward.normalized : Vector3.forward;
                Vector3 at = origin + forward * SpotAhead;
                float ground = origin.y;
                RaycastHit hit;
                if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out hit, 10f, ~0, QueryTriggerInteraction.Ignore)) ground = hit.point.y;
                spot.transform.position = new Vector3(at.x, ground + SpotHeight, at.z);
                spot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);   // 板の +Z = 読者から板へ向かう方向
                log.AppendLine("spot created at " + spot.transform.position + " (スポーン " + origin + " の前 " + SpotAhead + " m・地面 + " + SpotHeight + " m。人が Inspector で詰める)");
            }
            else log.AppendLine("spot kept at " + spot.transform.position + " rot=" + spot.transform.eulerAngles);
            pouch.welcomeSpot = spot.transform;
            var hits = Physics.OverlapBox(spot.transform.position, new Vector3(.6f, .4f, .15f), spot.transform.rotation, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(pouch.transform)).Select(c => c.name).Distinct().ToArray();
            log.AppendLine("spot overlaps: " + (hits.Length == 0 ? "none" : string.Join(", ", hits)));

            // ---- 手元ヒントと通知 (KD_HintDirector。古民家どおり。寝台の通知は呼ぶ (段階 2) まで出さない) ----
            var hintGo = DogsChild(dogs, HintDirectorName, log);
            var hints = hintGo.GetComponent<KD_HintDirector>();
            if (hints == null) hints = hintGo.AddUdonSharpComponent<KD_HintDirector>();
            var hintsUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(hints);
            hintsUdon.SyncMethod = VRC.SDKBase.Networking.SyncType.None; EditorUtility.SetDirty(hintsUdon);
            hints.language = language; hints.pouch = pouch;
            var keeperT = dogs.Find("KD_ShopKeeper");
            hints.keeper = keeperT == null ? null : keeperT.GetComponent<KD_ShopKeeper>();
            if (hints.keeper == null) log.AppendLine("NOTE KH_Dogs/KD_ShopKeeper が無い — おじいさんの会話中の「集中」判定なし (Menu/4d の後に 11b を再実行)");
            hints.viewer = null; hints.live = null;
            hints.sleepSlots = new KD_SleepSlot[0];
            hints.sleepNoticeEnabled = false;
            var pack = KD_SceneObjects.Find(KD_DogDefs.DogPackName)?.GetComponent<KD_DogPack>();
            hints.dogs = pack != null && pack.dogRoots != null ? pack.dogRoots : new Transform[0];
            var pets = new List<KD_DogPetController>();
            foreach (var pet in Resources.FindObjectsOfTypeAll<KD_DogPetController>()) if (pet != null && pet.gameObject.scene == pouch.gameObject.scene) pets.Add(pet);
            hints.pet = pets.ToArray();
            var recall = pouch.recall;
            hints.balls = recall != null && recall.balls != null ? recall.balls : new KD_BallController[0];
            hints.pickups = hints.balls.Select(b => b == null ? null : b.GetComponent<VRC.SDKBase.VRC_Pickup>()).ToArray();
            hints.noticeLabel = HintPanel(hintGo.transform, "Notice", font, new Vector2(780, 130)); hints.noticeRoot = hints.noticeLabel.transform.parent.gameObject;
            hints.hintLabel = HintPanel(hintGo.transform, "Context", font, new Vector2(650, 112)); hints.hintRoot = hints.hintLabel.transform.parent.gameObject;
            hints.noticeLabel.text = T("hint.menu.vr");
            hints.hintLabel.text = T("hint.pet");
            hints.handHintLabel = WorldLabel(hints.transform, HandHintName, font, HandHintSize, HandHintScale, 28f);
            hints.handHintRoot = hints.handHintLabel.transform.parent.gameObject;
            hints.handHintLabel.text = T("hint.hand.vr");
            hints.screenHintLabel = ScreenLabel(hints.transform, ScreenHintName, font, ScreenHintSize, 24f);
            hints.screenHintRoot = hints.screenHintLabel.transform.parent.parent.gameObject;
            hints.screenHintLabel.text = T("hint.hand.desktop");
            if (pouch.touchLauncherButton == null) throw new InvalidOperationException("KD_TreatPouch.touchLauncherButton missing (Menu/7 Build Menu)");
            hints.touchHintLabel = TouchLabel(pouch.touchLauncherButton, font, TouchHintSize, 20f);
            hints.touchHintRoot = hints.touchHintLabel.transform.parent.gameObject;
            hints.touchHintLabel.text = T("hint.hand.touch");
            hints.menuNoticeWithHandHint = false;
            hints.welcomeEnabled = true;
            pouch.hints = hints;
            log.AppendLine("hints: " + KD_Hillside.DogsRootName + "/" + HintDirectorName + " dogs=" + hints.dogs.Length + " pet=" + hints.pet.Length + " balls=" + hints.balls.Length
                + " keeper=" + (hints.keeper != null) + " sleepNotice=off (呼ぶは段階 2) / 手元ヒント VR・Desktop・Touch");

            // ---- 文言の束縛 ----
            KD_LocalizationEditor.Configure(language, catalog, log);
            KD_LocalizationEditor.Bind(language, catalog, log);
            foreach (var b in new UdonSharp.UdonSharpBehaviour[] { pouch, hints, language }) Copy(b);
            EditorSceneManager.MarkSceneDirty(pouch.gameObject.scene);
            log.AppendLine("BUILD RESULT: done (**シーンは保存していない**)。次: Menu/11c Verify Welcome");
            Write("build-hillside", log);
        }

        [MenuItem(HillsideMenu + "11c Verify Welcome")]
        public static void VerifyHillside()
        {
            var v = new KD_VerifyLog("[KD_WelcomeGuide] Verify (裏山 段階 1: ようこそ 最小版)");
            Action<bool, string> check = (ok, label) => v.Check(ok, label);
            var log = v.Text;
            if (!v.Check(!EditorApplication.isPlaying && KD_Hillside.InScene(), KD_Hillside.ScenePath + " を Edit モードで開いている")) { v.Finish(Output + "verify-hillside.txt"); return; }
            var pouch = Find<KD_TreatPouch>(KD_DogDefs.TreatPouchName);
            var catalog = KD_LocalizationEditor.Load();
            var panel = pouch.panelRoot.transform;
            var dogs = pouch.transform.parent;

            // 案 2: ようこその面
            var page = panel.Find(PageName);
            check(page != null && pouch.welcomePage == page.gameObject, "welcomePage = " + PageName);
            check(page != null && !page.gameObject.activeSelf && !pouch.panelRoot.activeSelf, "welcome page and panel inactive by default");
            check(page != null && page.Find(BodyPath) != null && pouch.welcomeFooterLabel != null && pouch.welcomeFooterLabel.transform.parent == page, "scrolling body + fixed footer on the page");
            check(page != null && Wired(page.Find("LaterButton"), pouch, "_WelcomeLater") && Wired(page.Find("MenuButton"), pouch, "_WelcomeMenu"), "[あとで] → _WelcomeLater / [メニューを見る] → _WelcomeMenu");
            check(page != null && pouch.language.welcomeScroll != null && pouch.language.welcomeScroll.content == page.Find(BodyPath), "welcome scrolling body wired");
            check(pouch.confirmRoot == null || pouch.confirmRoot.transform.GetSiblingIndex() == panel.childCount - 1, "confirm card still the last child (P13)");
            VerifyLanguageRow(page, pouch.language.welcomeChoiceLabel, pouch.language.welcomeScroll, pouch.language, check);
            log.AppendLine("  SKIP 「はじめに」の再読面・インフォメーションの行 (裏山では段階 3)");

            // 案 1: 庭の置き場
            var spot = dogs == null ? null : dogs.Find(SpotName);
            KD_SceneObjects.Find(SpotName, out int spots);
            check(spot != null && spots == 1 && pouch.welcomeSpot == spot, SpotName + " が " + KD_Hillside.DogsRootName + " 直下に 1 個・pouch.welcomeSpot に配線");
            check(spot != null && !PrefabUtility.IsPartOfPrefabInstance(spot.gameObject) && spot.GetComponents<Component>().Length == 1, "spot は Transform だけの素のオブジェクト");
            if (spot != null)
            {
                var d = Object.FindObjectOfType<VRCSceneDescriptor>();
                var sp = d != null && d.spawns != null && d.spawns.Length > 0 ? d.spawns[0] : null;
                float dist = sp == null ? -1f : Vector3.Distance(new Vector3(sp.position.x, 0, sp.position.z), new Vector3(spot.position.x, 0, spot.position.z));
                check(sp != null && dist < 8f, "spot はスポーンから 8 m 以内 (水平 " + dist.ToString("F1") + " m。見えないところに置かない)");
                log.AppendLine("  NOTE spot " + spot.position + " rot " + spot.eulerAngles + " (位置は人が Inspector で詰める)");
            }

            // 手元ヒントと通知 (KD_HintDirector)
            var hintT = dogs == null ? null : dogs.Find(HintDirectorName);
            var hints = hintT == null ? null : hintT.GetComponent<KD_HintDirector>();
            KD_SceneObjects.Find(HintDirectorName, out int hintCount);
            if (!v.Check(hints != null && hintCount == 1, HintDirectorName + " が " + KD_Hillside.DogsRootName + " 直下に 1 個")) { v.Finish(Output + "verify-hillside.txt"); return; }
            var hintsUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(hints);
            check(hintsUdon != null && hintsUdon.SyncMethod == VRC.SDKBase.Networking.SyncType.None, "KD_HintDirector の SyncMethod == None (ローカルだけ)");
            check(hints.welcomeEnabled && hints.pouch == pouch && pouch.hints == hints && hints.language == pouch.language, "hints ↔ pouch / language wired, welcomeEnabled");
            check(KD_HintDirector.SaveKey == "KD_Onboarding_v1", "保存キーは古民家どおり KD_Onboarding_v1");
            check(!hints.sleepNoticeEnabled && (hints.sleepSlots == null || hints.sleepSlots.Length == 0), "寝台の通知は出さない (呼ぶは段階 2 IMP-DOG-016)");
            check(hints.viewer == null && hints.live == null, "写真ビューア・ライブは参照しない (裏山に無い)");
            check(hints.dogs != null && hints.dogs.Length == KD_DogDefs.All.Length && hints.dogs.All(x => x != null), "dogs " + KD_DogDefs.All.Length + " 匹 (KD_DogPack.dogRoots)");
            check(hints.balls != null && hints.balls.Length == KD_DogDefs.All.Length && hints.balls.All(x => x != null)
                && hints.pickups != null && hints.pickups.Length == hints.balls.Length && hints.pickups.All(x => x != null), "balls / pickups " + KD_DogDefs.All.Length + " 本 (KH_ToyRecall.balls)");
            check(hints.pet != null && hints.pet.Length > 0 && hints.pet.All(x => x != null), "pet (KD_DogPetController) " + (hints.pet == null ? 0 : hints.pet.Length) + " 本");
            var keeperT = dogs.Find("KD_ShopKeeper");
            check(keeperT != null && hints.keeper == keeperT.GetComponent<KD_ShopKeeper>(), "keeper = KH_Dogs/KD_ShopKeeper (会話中は集中扱い。Menu/4d の後に 11b)");
            foreach (var root in new[] { hints.noticeRoot, hints.hintRoot })
                check(root != null && root.transform.parent == hints.transform && !root.activeSelf && root.GetComponent<Canvas>() != null && root.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace,
                    (root == null ? "notice/context" : root.name) + ": world-space canvas under KD_HintDirector, inactive");
            check(hints.noticeLabel != null && hints.hintLabel != null, "noticeLabel / hintLabel wired");
            check(hints.handHintRoot != null && hints.handHintLabel != null && hints.handHintRoot.transform.parent == hints.transform && !hints.handHintRoot.activeSelf
                && hints.handHintRoot.GetComponent<Canvas>() != null && hints.handHintRoot.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace, "VR hand hint: world-space canvas under KD_HintDirector, inactive");
            check(hints.screenHintRoot != null && hints.screenHintLabel != null && !hints.screenHintRoot.activeSelf && hints.screenHintRoot.GetComponent<Canvas>() != null
                && hints.screenHintRoot.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay && hints.screenHintRoot.GetComponent<GraphicRaycaster>() == null, "Desktop hint: overlay canvas, no raycaster, inactive");
            check(hints.touchHintRoot != null && hints.touchHintLabel != null && pouch.touchLauncherButton != null && hints.touchHintRoot.transform.parent == pouch.touchLauncherButton
                && !hints.touchHintRoot.activeSelf && hints.touchHintRoot.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "Touch hint: child of the launcher button, no raycast targets, inactive");
            check(!hints.menuNoticeWithHandHint, "N1 notice suppressed while the hand hint shows");
            check(hints.NoticeCandidate(180, true, 0, true, false, 0, 1000) == 0 && hints.NoticeCandidate(180, true, 0, false, false, 0, 1000) == 1, "NoticeCandidate: N1 only when the menu is not covered");

            // 文言
            check(catalog.languages.Where(KD_LocalizationEditor.Bundled).All(l => HillsideKeys.All(k => l.texts.Any(t => t.key == k && !string.IsNullOrEmpty(t.text)))), "catalog has all welcome/hint keys in every bundled language");
            string title = KD_LocalizationEditor.Text(catalog, "ja", "welcome.title");
            check(title == HillsideTitleJa, "welcome.title (ja) が裏山の題「" + HillsideTitleJa + "」(actual 「" + title + "」)");
            string footer = KD_LocalizationEditor.Text(catalog, "ja", "welcome.footer.vr");
            check(!footer.Contains("インフォメーション"), "足元の 1 行が再読 (設定 → インフォメーション) を案内しない (段階 3 まで面が無い)");
            string menuNotice = KD_LocalizationEditor.Text(catalog, "ja", "hint.menu.vr");
            check(!menuNotice.Contains("パーティクル") && menuNotice.Contains("さんぽ"), "メニュー紹介 (hint.menu.*) のタブ名が裏山の並び (さんぽ有り・パーティクル無し)");
            check(pouch.language.keys != null && HillsideKeys.All(k => pouch.language.keys.Contains(k)), "KD_Language tables carry the keys (Configure ran)");
            check(pouch.language.fixedKeys != null && pouch.language.fixedKeys.Contains("welcome.body") && pouch.language.fixedKeys.Contains("welcome.later") && pouch.language.fixedKeys.Contains("welcome.menu"),
                "fixed label bindings include the welcome labels (UILocalizationBindings の Page_Welcome/...)");
            try { KD_LocalizationEditor.ValidateScene(pouch.language, catalog); check(true, "localization scene validation"); }
            catch (Exception e) { check(false, "localization scene validation: " + e.Message); }
            var font = pouch.sectionTitleLabel.font;
            string need = string.Concat(HillsideKeys.SelectMany(k => catalog.languages.Where(KD_LocalizationEditor.Bundled).Select(l => KD_LocalizationEditor.Text(catalog, l.code, k))));
            string miss = KD_ShopSetupEditor.MissingGlyphs(need);
            check(miss.Length == 0, "欠字 0: ようこそ・手元ヒントの文言 (ja / en。欠け " + miss.Length + (miss.Length == 0 ? "" : ": " + miss) + ")");
            VerifyLanguageSwitch(catalog, font, check);
            v.Finish(Output + "verify-hillside.txt");
            if (v.Fail > 0) Debug.LogError("Welcome (裏山) verify: " + v.Fail + " failure(s). See " + Output + "verify-hillside.txt");
        }
    }
}
