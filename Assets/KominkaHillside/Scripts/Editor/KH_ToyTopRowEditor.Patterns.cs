using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    /// <summary>DES-DOG-002 ボールの柄 (2026-10-03 ユーザー決定「推奨で」、docs/plans/2026-10-03-ball-patterns.md)。
    ///
    /// Course/124 (本番シーンの局所編集):
    /// - KH_Dogs/KD_ToyBox (Manual、古民家からコピー済みの KD_ToyBox。同期 string 1 本) を作り、柄 6 品 × 犬 6 匹の見た目の器 (ボールのプレハブの子
    ///   KD_ToyMesh_*。シーン側 → プレハブインスタンスの子の向きの参照) と、受け手が所有を確かめる商品 (KD_ShopCatalog の toy.* 行) を配線する
    /// - カタログの通知先に KD_ToyBox._OnShopSelectionChanged を足す (撫でFX の KD_PetFxBox は残す)。KD_Language.toyBox も配線
    /// - Page_Toy の上段の右半分に「ボールの柄」[◀] [名前 状態] [▶] を作り、pouch.ballPatterns へ配線
    /// - コースの翻訳表 (course.toy.pattern.*) を extension に入れ直す
    /// 新しい同期オブジェクトなので、このあと Menu/19b Assign Network IDs → 保存 → Course/125。
    /// ぬいぐるみ 3 点 (CE) とディスクの柄は初期公開に入れない (表に載せない)。</summary>
    public static partial class KH_ToyTopRowEditor
    {
        /// <summary>柄の表 (恒久 ID, ボールのプレハブの子 KD_ToyMesh_{mesh}, 価格 pt)。並びは値段順 = 古民家の棚 (Toy Catalog/2 Arrange By Price)。
        /// 価格は game-design §4 (2026-09-23 決定、古民家と同じ)。正本は KD_ProductDefinitions で、Verify が両方を突き合わせる。[0] は無料の倒れ先。</summary>
        public static readonly (string key, string mesh, int price)[] Patterns =
        {
            ("toy.basic_ball", "BasicBall", 0), ("toy.tennis_ball", "TennisBall", 10), ("toy.donut", "Donut", 20),
            ("toy.bone", "Bone", 30), ("toy.cloth_ball", "ClothBall", 40), ("toy.temari_claude", "Temari_Claude", 50),
        };
        public const string MeshPrefix = "KD_ToyMesh_", ToyBoxName = "KD_ToyBox", ToyEvent = "_OnShopSelectionChanged";
        public static readonly Vector2 PatternLabelPos = new Vector2(75f, TopRowY), PatternLabelSize = new Vector2(130f, RowHeight);
        public static readonly Vector2 PrevPos = new Vector2(172f, TopRowY), NextPos = new Vector2(496f, TopRowY), ArrowSize = new Vector2(52f, RowHeight);
        public static readonly Vector2 ActPos = new Vector2(334f, TopRowY), ActSize = new Vector2(260f, RowHeight);
        public const string PatternLabelName = "PatternLabel", PrevName = "PatternPrev", NextName = "PatternNext", ActName = "PatternAct";
        public const string PrevEvent = "_PatternPrev", NextEvent = "_PatternNext", ActEvent = "_PatternAct";
        static readonly string[] PatternKeys = { "course.toy.pattern.label", "course.toy.pattern.body", "course.toy.pattern.changed" };

        // ============================================================
        // Course/124 生成 (ボールの柄)
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Course/124 - Build Ball Patterns (toy box and toy tab row)")]
        public static void BuildBallPatterns()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = KH_CourseTravelEditor.FindPouch();
            if (p.catalog == null || p.catalog.items == null) throw new InvalidOperationException("pouch.catalog missing: run Menu/4 Setup Shop first");
            if (p.recall == null || p.recall.balls == null || p.recall.balls.Length != 6 || p.recall.balls.Any(b => b == null)) throw new InvalidOperationException("KH_ToyRecall.balls must be the six balls");
            if (p.throwStraightImage == null) throw new InvalidOperationException("Run Course/122 (throw style row) first: the two halves share the top row");
            if (new SerializedObject(p).FindProperty("ballPatterns") == null) throw new InvalidOperationException("KD_TreatPouch has no ballPatterns field yet: compile first");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ToyBox.asset") == null) throw new InvalidOperationException("KD_ToyBox program asset missing");
            var items = PatternItems(p.catalog);
            var nodes = PatternNodes(p.recall.balls);
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int nDogs);
            if (dogs == null || nDogs != 1) throw new InvalidOperationException(KD_Hillside.DogsRootName + " missing or duplicated");
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            var font = p.sectionTitleLabel.font;
            var log = new StringBuilder();
            Directory.CreateDirectory(OutDir);
            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-ball-patterns.unity", true);
            try
            {
                // ---- KD_ToyBox (KH_Dogs 直下、専用 GameObject、Manual) ----
                var boxes = KD_ProductsEditor.SceneComponents<KD_ToyBox>();
                if (boxes.Length > 1) throw new InvalidOperationException("More than one KD_ToyBox in the scene (" + boxes.Length + ")");
                KD_ToyBox box = boxes.Length == 1 ? boxes[0] : null;
                if (box != null && box.transform.parent != dogs.transform) throw new InvalidOperationException("KD_ToyBox exists outside " + KD_Hillside.DogsRootName + ": " + box.name);
                if (box == null)
                {
                    var t = dogs.transform.Find(ToyBoxName);
                    var go = t != null ? t.gameObject : new GameObject(ToyBoxName);
                    if (t == null) { Undo.RegisterCreatedObjectUndo(go, "Create " + ToyBoxName); go.transform.SetParent(dogs.transform, false); }
                    box = go.AddUdonSharpComponent<KD_ToyBox>();
                    log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + ToyBoxName);
                }
                if (box.gameObject.GetComponents<VRC.Udon.UdonBehaviour>().Length != 1) throw new InvalidOperationException(ToyBoxName + " must hold exactly one UdonBehaviour (sync modes cannot mix)");
                var boxBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(box);
                if (boxBacking == null) throw new InvalidOperationException("KD_ToyBox backing missing (compile and run again)");
                if (boxBacking.SyncMethod != Networking.SyncType.Manual) { boxBacking.SyncMethod = Networking.SyncType.Manual; EditorUtility.SetDirty(boxBacking); log.AppendLine("backing sync -> Manual (vrc-udonsharp §2)"); }
                box.catalog = p.catalog; box.categoryKey = "toy";
                box.productKeys = Patterns.Select(x => x.key).ToArray();
                box.productItems = items;
                box.meshNodes = nodes; box.dogCount = 6;
                box.language = p.language;
                box.pickups = null; box.productNameKeys = null; box.productNames = null; box.dogNameKeys = null; box.dogNames = null;   // 拾う文言は変えない (ボールの既存の文言を保つ)
                UdonSharpEditorUtility.CopyProxyToUdon(box); EditorUtility.SetDirty(box);

                // ---- カタログの通知先 (撫でFX の KD_PetFxBox を残して足す) と言語 ----
                var consumers = (p.catalog.consumers ?? new UdonSharpBehaviour[0]).ToList();
                var events = (p.catalog.consumerEvents ?? new string[0]).ToList();
                while (events.Count < consumers.Count) events.Add("");
                if (events.Count > consumers.Count) events.RemoveRange(consumers.Count, events.Count - consumers.Count);
                int at = consumers.IndexOf(box);
                if (at < 0) { consumers.Add(box); events.Add(ToyEvent); } else events[at] = ToyEvent;
                p.catalog.consumers = consumers.ToArray(); p.catalog.consumerEvents = events.ToArray();
                KD_ProductsEditor.Copy(p.catalog);
                p.language.toyBox = box; KD_ProductsEditor.Copy(p.language);
                log.AppendLine("catalog.consumers " + consumers.Count + " (toy box at " + consumers.IndexOf(box) + ")");

                // ---- おもちゃタブの上段の右半分 ----
                var page = p.tabPages[KD_TreatPouch.TabToy].transform;
                var label = MakeText(page, PatternLabelName, PatternLabelPos, PatternLabelSize, 22f, TextAlignmentOptions.MidlineLeft, font, Ja("course.toy.pattern.label"));
                label.enableAutoSizing = true; label.fontSizeMax = 22f; label.fontSizeMin = 14f; EditorUtility.SetDirty(label);
                var prev = KD_ShopSetupEditor.EnsureButton(page, PrevName, PrevPos, ArrowSize, KD_UiSpriteEditor.ActionNeutral, font, "◀", pouchBacking, PrevEvent, log);
                var next = KD_ShopSetupEditor.EnsureButton(page, NextName, NextPos, ArrowSize, KD_UiSpriteEditor.ActionNeutral, font, "▶", pouchBacking, NextEvent, log);
                ButtonLabel(prev, 20f); ButtonLabel(next, 20f);
                var act = KD_ShopSetupEditor.EnsureButton(page, ActName, ActPos, ActSize, KD_UiSpriteEditor.ActionNeutral, font, Ja("course.toy.pattern.label"), pouchBacking, ActEvent, log);
                var actLabel = ButtonLabel(act, 18f); actLabel.richText = true; EditorUtility.SetDirty(actLabel);
                p.ballPatterns = box; p.patternLabel = label; p.patternActImage = act.GetComponent<Image>(); p.patternActLabel = actLabel;

                KH_CourseTravelEditor.ApplyCatalog(p);
                UdonSharpEditorUtility.CopyProxyToUdon(p); EditorUtility.SetDirty(p);
                EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
                if (!EditorSceneManager.SaveScene(p.gameObject.scene)) throw new IOException("Ball pattern build save failed");
            }
            catch
            {
                Debug.LogError("[KH ToyRow] Ball pattern build failed; the scene before this run is at " + OutDir + "/before-ball-patterns.unity. Reopen the scene without saving to discard partial changes.");
                throw;
            }
            File.WriteAllText(OutDir + "/ball-patterns-build.txt", log.ToString());
            Debug.Log("[KH ToyRow] Built ball patterns (saved). Next: Menu/19b Assign Network IDs, save, Course/27 (glyphs), Course/125 Verify.");
        }

        /// <summary>柄の表の各行の商品 (カタログの行)。価格と区分を表 (Patterns / KD_ProductDefinitions) と突き合わせ、違えば止める。</summary>
        static KD_ShopItem[] PatternItems(KD_ShopCatalog catalog)
        {
            var items = new KD_ShopItem[Patterns.Length];
            for (int i = 0; i < Patterns.Length; i++)
            {
                var found = catalog.items.Where(x => x != null && x.productKey == Patterns[i].key).ToArray();
                if (found.Length != 1) throw new InvalidOperationException("Catalog row " + Patterns[i].key + ": " + found.Length + " (run Menu/4 Setup Shop)");
                var it = found[0]; var def = KD_ProductDefinitions.Get(Patterns[i].key);
                if (it.categoryKey != "toy" || it.price != Patterns[i].price || def.price != Patterns[i].price || it.IsCE() || it.consumable)
                    throw new InvalidOperationException("Pattern " + Patterns[i].key + " price/category differs: item " + it.price + " / definitions " + def.price + " / table " + Patterns[i].price);
                items[i] = it;
            }
            if (!items[0].IsFree()) throw new InvalidOperationException("Pattern [0] must be the free basic ball (the fallback)");
            return items;
        }

        /// <summary>見た目の器 (品目 i の犬 d = [i * 6 + d])。犬の順は KH_ToyRecall.balls (= KD_DogDefs.All の順)。</summary>
        static GameObject[] PatternNodes(KD_BallController[] balls)
        {
            var nodes = new GameObject[Patterns.Length * 6];
            for (int i = 0; i < Patterns.Length; i++)
                for (int d = 0; d < 6; d++)
                {
                    var n = balls[d].transform.Find(MeshPrefix + Patterns[i].mesh);
                    if (n == null) throw new InvalidOperationException("Missing toy visual " + balls[d].name + "/" + MeshPrefix + Patterns[i].mesh + " (ball prefab children)");
                    nodes[i * 6 + d] = n.gameObject;
                }
            return nodes;
        }

        // ============================================================
        // Course/125 検査 (ボールの柄)
        // ============================================================

        [MenuItem("Tools/KominkaHillside/Course/125 - Verify Ball Patterns")]
        public static void VerifyBallPatterns()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var p = KH_CourseTravelEditor.FindPouch();
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = p.gameObject.scene.path, mode = "verify-patterns" };
            var e = report.findings;
            try { ValidateBallPatterns(p, e, report); }
            catch (Exception ex) { e.Add("Exception: " + ex.Message); }
            report.errors = e.Count; report.status = e.Count == 0 ? "BALL_PATTERNS_OK" : "FAIL";
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/ball-patterns-verify.json", JsonUtility.ToJson(report, true));
            if (e.Count != 0) throw new InvalidOperationException("[KH ToyRow] FAIL " + e.Count + ":\n" + string.Join("\n", e));
            Debug.Log("[KH ToyRow] BALL_PATTERNS_OK (" + report.notes.Count + " notes)");
        }

        public static void ValidateBallPatterns(KD_TreatPouch p, List<string> e, Report report)
        {
            var boxes = KD_ProductsEditor.SceneComponents<KD_ToyBox>();
            Req(e, boxes.Length == 1, "exactly one KD_ToyBox in the scene (" + boxes.Length + ")");
            if (boxes.Length != 1) return;
            var box = boxes[0];
            Req(e, box.transform.parent != null && box.transform.parent.name == KD_Hillside.DogsRootName && box.name == ToyBoxName, "KD_ToyBox at " + KD_Hillside.DogsRootName + "/" + ToyBoxName);
            var ubs = box.GetComponents<VRC.Udon.UdonBehaviour>();
            Req(e, ubs.Length == 1 && ubs[0].SyncMethod == Networking.SyncType.Manual, "one UdonBehaviour on the toy box, SyncMethod Manual (actual)");
            var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ToyBox.asset");
            Req(e, pa != null && pa.behaviourSyncMode == BehaviourSyncMode.Manual, "KD_ToyBox program asset Manual");
            var synced = typeof(KD_ToyBox).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Where(f => f.GetCustomAttributes(typeof(UdonSyncedAttribute), true).Length > 0).ToArray();
            Req(e, synced.Length == 1 && synced[0].FieldType == typeof(string), "one synced variable, a string (" + string.Join(",", synced.Select(f => f.FieldType.Name + " " + f.Name)) + ")");
            // 表・商品・価格
            Req(e, box.productKeys != null && box.productKeys.SequenceEqual(Patterns.Select(x => x.key)), "productKeys = the six ball patterns in price order");
            Req(e, box.productItems != null && box.productItems.Length == Patterns.Length, "productItems has " + Patterns.Length + " entries");
            for (int i = 0; box.productItems != null && i < Patterns.Length && i < box.productItems.Length; i++)
            {
                var it = box.productItems[i];
                Req(e, it != null && it.productKey == Patterns[i].key && p.catalog.items.Contains(it) && it.categoryKey == "toy" && !it.IsCE() && !it.consumable
                    && it.price == Patterns[i].price && KD_ProductDefinitions.Get(Patterns[i].key).price == Patterns[i].price,
                    "pattern " + i + " " + Patterns[i].key + " = the catalog row, " + Patterns[i].price + " pt (game-design §4), not CE / consumable");
            }
            Req(e, box.productItems != null && box.productItems.Length > 0 && box.productItems[0] != null && box.productItems[0].IsFree(), "pattern 0 is the free fallback");
            Req(e, box.catalog == p.catalog && box.categoryKey == "toy" && box.dogCount == 6 && box.language == p.language, "catalog / category toy / six dogs / language wired");
            Req(e, box.pickups == null || box.pickups.Length == 0, "pickup prompts untouched (no pickups wired)");
            // 見た目の器: 6 品 × 6 匹、ボールの子、Collider なし。初期はどのボールも基本ボールだけが出ている
            var balls = p.recall.balls;
            Req(e, box.meshNodes != null && box.meshNodes.Length == Patterns.Length * 6, "meshNodes 6 x 6");
            for (int i = 0; box.meshNodes != null && i < Patterns.Length; i++)
                for (int d = 0; d < 6 && i * 6 + d < box.meshNodes.Length; d++)
                {
                    var n = box.meshNodes[i * 6 + d];
                    bool ok = n != null && balls != null && d < balls.Length && balls[d] != null && n.transform.parent == balls[d].transform && n.name == MeshPrefix + Patterns[i].mesh
                        && n.GetComponentsInChildren<Collider>(true).Length == 0 && n.GetComponentsInChildren<Renderer>(true).Length > 0;
                    Req(e, ok, "node " + Patterns[i].key + " / dog " + d + " = " + (balls != null && d < balls.Length && balls[d] != null ? balls[d].name : "?") + "/" + MeshPrefix + Patterns[i].mesh + ", visual only");
                }
            for (int d = 0; balls != null && d < balls.Length; d++)
            {
                if (balls[d] == null) continue;
                var active = balls[d].transform.Cast<Transform>().Where(c => c.name.StartsWith(MeshPrefix) && c.gameObject.activeSelf).Select(c => c.name).ToArray();
                Req(e, active.Length == 1 && active[0] == MeshPrefix + "BasicBall", balls[d].name + ": exactly one visual active, the basic ball (" + string.Join(",", active) + ")");
                Req(e, balls[d].GetComponents<VRC.Udon.UdonBehaviour>().Length == 1, balls[d].name + ": still one UdonBehaviour (no behaviour added to the ball)");
            }
            // 配線: カタログの通知先・言語・メニュー
            int at = p.catalog.consumers == null ? -1 : Array.IndexOf(p.catalog.consumers, (UdonSharpBehaviour)box);
            Req(e, at >= 0 && p.catalog.consumerEvents != null && at < p.catalog.consumerEvents.Length && p.catalog.consumerEvents[at] == ToyEvent && p.catalog.consumers.Length == p.catalog.consumerEvents.Length,
                "catalog.consumers has the toy box -> " + ToyEvent);
            Req(e, p.language.toyBox == box, "KD_Language.toyBox = the toy box");
            Req(e, p.ballPatterns == box, "pouch.ballPatterns = the toy box");
            var page = p.tabPages[KD_TreatPouch.TabToy].transform;
            var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(p);
            Req(e, p.patternLabel != null && p.patternLabel.name == PatternLabelName && p.patternLabel.transform.parent == page, "patternLabel = Page_Toy/" + PatternLabelName);
            Req(e, p.patternActImage != null && p.patternActImage.name == ActName && p.patternActImage.transform.parent == page, "patternActImage = Page_Toy/" + ActName);
            Req(e, p.patternActLabel != null && p.patternActImage != null && p.patternActLabel.transform.parent == p.patternActImage.transform && p.patternActLabel.richText, "pattern act label inside the button, rich text");
            var prev = page.Find(PrevName); var next = page.Find(NextName);
            Req(e, prev != null && next != null, "Page_Toy/" + PrevName + " and " + NextName);
            if (prev != null) CheckEvent(e, prev.gameObject, pouchBacking, PrevEvent);
            if (next != null) CheckEvent(e, next.gameObject, pouchBacking, NextEvent);
            if (p.patternActImage != null) CheckEvent(e, p.patternActImage.gameObject, pouchBacking, ActEvent);
            foreach (var m in new[] { PrevEvent, NextEvent, ActEvent, "_PatternConfirmBuy", "_PatternConfirmNo" })
                Req(e, typeof(KD_TreatPouch).GetMethod(m) != null, "KD_TreatPouch." + m);
            // 寸法: 右半分、上段の帯、左半分 (投げ方) と重ならない
            var rights = new[] { p.patternLabel == null ? null : p.patternLabel.rectTransform, prev as RectTransform, p.patternActImage == null ? null : p.patternActImage.rectTransform, next as RectTransform };
            foreach (var r in rights)
            {
                if (r == null) continue;
                float l = r.anchoredPosition.x - r.sizeDelta.x / 2, rr = r.anchoredPosition.x + r.sizeDelta.x / 2, b = r.anchoredPosition.y - r.sizeDelta.y / 2, t = r.anchoredPosition.y + r.sizeDelta.y / 2;
                Req(e, l >= Middle + 19.5f && rr <= RightEdge + .5f, r.name + " in the right half x " + l + ".." + rr + " (" + (Middle + 20f) + ".." + RightEdge + ")");
                Req(e, b >= ContentTopWithRow - .5f && t <= TabBottom(p) - 4f, r.name + " in the top band y " + b + ".." + t);
                report.notes.Add(r.name + " x " + l + ".." + rr + " y " + b + ".." + t);
            }
            var all = rights.Concat(new[] { p.throwStyleLabel == null ? null : p.throwStyleLabel.rectTransform, p.throwStraightImage == null ? null : p.throwStraightImage.rectTransform, p.throwCurveImage == null ? null : p.throwCurveImage.rectTransform }).ToArray();
            Overlaps(e, all);
            // NetworkID (新しい同期オブジェクト)
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
            Req(e, descriptor != null && descriptor.NetworkIDCollection.Any(x => x.gameObject == box.gameObject), "the toy box has a NetworkID (run Menu/19b Assign Network IDs, then save)");
            // 文言・字形・収まり
            ValidateCourseText(p, e, report, PatternKeys);
            string glyphs = KD_ShopSetupEditor.MissingGlyphs("◀▶");
            Req(e, glyphs.Length == 0, "◀ ▶ in the menu font (missing '" + glyphs + "')");
            var sample = SampleText();
            try
            {
                foreach (string code in p.language.languageCodes)
                {
                    bool own = code == "ja" || code == "en";
                    string T(string key) { return p.language._TextForLanguage(key, code); }
                    Fit(sample, p.patternLabel, T("course.toy.pattern.label"), code + " pattern label", own, e, report);
                    Fit(sample, p.confirmBodyLabel, T("course.toy.pattern.body"), code + " confirm body", own, e, report);
                    Fit(sample, p.feedbackLabel, T("course.toy.pattern.changed").Replace("{0}", T("item.temariClaude")), code + " changed note", own, e, report);
                    for (int i = 0; box.productItems != null && i < box.productItems.Length; i++)
                    {
                        if (box.productItems[i] == null) continue;
                        string name = T(box.productItems[i].displayNameKey);
                        foreach (var state in new[] { T("KD_ShopShelf.WordInUse"), T("KD_TreatPouch.WordAccChoose"), Patterns[i].price + " pt", T("KD_ShopShelf.WordLoading") })
                            Fit(sample, p.patternActLabel, name + "  <color=#D6AD52>" + state + "</color>", code + " pattern " + Patterns[i].key + " / " + state, own, e, report);
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(sample.gameObject); }
        }
    }
}
