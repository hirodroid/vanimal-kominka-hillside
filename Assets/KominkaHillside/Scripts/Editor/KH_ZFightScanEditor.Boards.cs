using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    // BUG-ENV-005 (2026-10-15): ZFight/7 - Scan Boards And Props. Read-only (nothing is saved, nothing is added to the scene).
    // The AI-side completion check of the Quest-only flicker on the generated boards, posters, the photo board and the shampoo bottles.
    //
    // Targets (all required; Codex review 2026-10-15 中3): KH_GuideBoards (Asobikata, Map, Kominka), KH_ItemGuideBoards (4 boards),
    // KH_WallPosters (Torinosuke, GroupPoster), KH_PhotoBoard, KH_DogBookStand, each exactly once, and the 4 KH_WashTool with
    // tool == KH_DogWash.ToolBottle and a Body child (the shampoo bottles; the FoamCannon shares the tool id). Every enabled MeshRenderer must have a readable mesh. Anything missing, duplicated, unreadable,
    // or a detector run cut short (time budget / caps) prints BOARDS_ZFIGHT_INCOMPLETE, never OK.
    // UI: each Graphic (Image / RawImage / TextMeshProUGUI ..., enabled, alpha > 0, active or not: KD_DogBook's pages switch at run time)
    // is a quad from its RectTransform's world corners, facing its readable side (-forward); TMP sub-meshes are skipped (same rect).
    // Detector: KH_ZFightDetector, |dot| > NormalDot, gap <= 30 mm (0-3 mm "coplanar", above "near"). Per triangle pair only float slivers
    // are dropped (overlap < 0.1 cm2 or mean width < 0.1 mm: two faces meeting edge to edge through different transforms); a group (one
    // renderer / submesh pair, one plane cluster) counts when its total area is >= 1 cm2 (Codex review 低6: a 10 x 10 mm overlap of two
    // 0.5 cm2 triangles is caught). Smaller groups are listed, not counted.
    // Counted (BOARDS_ZFIGHT_FAIL n): same-facing groups with at least one mesh side. UI vs UI never fights (UI/Default ZWrite Off) and
    // is skipped. GUARANTEE = the readers' side only. UI is Cull Off, so its back is visible too: a UI quad vs an opposite-facing mesh
    // face (seen only from behind that face, e.g. a poster's plank from inside the farmhouse wall) is listed as "back side" and is NOT
    // counted (Codex review 低5): back-side flicker is outside this check. The terrain and the farmhouse wall are not targets.
    public static partial class KH_ZFightScanEditor
    {
        const float BoardsCoplanarGap = 0.003f, BoardsNearGap = 0.030f, BoardsCell = 0.1f, BoardsClusterLink = 0.1f, BoardsBudgetSeconds = 120;
        const float BoardsPairFloorM2 = 1e-5f /* 0.1 cm2 per triangle pair */, BoardsSliverWidth = 1e-4f /* 0.1 mm */, BoardsGroupMinCm2 = 1f;
        const int BoardsExpectedBottles = 4;
        static readonly string[] BoardsRootNames = { KH_GuideBoardsEditor.RootName, KH_GuideBoardsEditor.ItemGuideRootName, KH_GuideBoardsEditor.WallPostersName, "KH_PhotoBoard", KH_GuideBoardsEditor.DogBookStandName };
        const string BottleGroup = "bottles";

        [Serializable] public class BoardsReport
        {
            public string utc, scene, thresholds, rule, guarantee, status;
            public bool sceneDirtyBefore, sceneDirtyAfter, truncated, incomplete;
            public int renderers, graphics, triangles, uiUiSkipped, counted, bottles; public long candidates; public double seconds;
            public List<string> targets = new List<string>(), perGroup = new List<string>(), problems = new List<string>(), notes = new List<string>();
            public List<KH_ZFightDetector.Group> pairs = new List<KH_ZFightDetector.Group>();
            public List<KH_ZFightDetector.Group> small = new List<KH_ZFightDetector.Group>();
            public List<KH_ZFightDetector.Group> backSide = new List<KH_ZFightDetector.Group>();
        }

        /// <summary>The children each root must have (exactly once each); the item guide boards: exactly 4 named KH_ItemGuideBoard_*.</summary>
        static List<string> BoardsRootProblems(string name, Transform root)
        {
            var p = new List<string>();
            Func<string, int> n = child => root.Cast<Transform>().Count(t => t.name == child);
            if (name == KH_GuideBoardsEditor.RootName)
                foreach (var b in new[] { "Asobikata", "Map", "Kominka" }) if (n(b) != 1) p.Add(name + "/" + b + " x" + n(b) + " (expected 1)");
            if (name == KH_GuideBoardsEditor.ItemGuideRootName)
            {
                int k = root.Cast<Transform>().Count(t => t.name.StartsWith(KH_GuideBoardsEditor.ItemGuidePrefix));
                if (k != 4) p.Add(name + " has " + k + " " + KH_GuideBoardsEditor.ItemGuidePrefix + "* boards (expected 4)");
                foreach (var g in root.Cast<Transform>().GroupBy(t => t.name).Where(g => g.Count() > 1)) p.Add(name + "/" + g.Key + " x" + g.Count());
            }
            if (name == KH_GuideBoardsEditor.WallPostersName)
                foreach (var b in new[] { KH_GuideBoardsEditor.TorinosukePosterName, KH_GuideBoardsEditor.GroupPosterName }) if (n(b) != 1) p.Add(name + "/" + b + " x" + n(b) + " (expected 1)");
            return p;
        }

        [MenuItem(Menu + "7 - Scan Boards And Props")]
        public static void ScanBoards()
        {
            const string tag = "[KH ZFight 7]";
            var rep = new BoardsReport { utc = DateTime.UtcNow.ToString("o"),
                thresholds = "|dot(nA,nB)| > " + KH_ZFightDetector.NormalDot + "; gap <= " + BoardsNearGap * 1000 + " mm (coplanar <= " + BoardsCoplanarGap * 1000 + " mm), measured along nA at the overlap polygon's vertices; per triangle pair: overlap >= " + BoardsPairFloorM2 * 1e4f + " cm2 and mean width >= " + BoardsSliverWidth * 1000 + " mm (float slivers dropped); counted per group when its total area >= " + BoardsGroupMinCm2 + " cm2; clusters linked within " + BoardsClusterLink * 100 + " cm",
                rule = "counted = same-facing (or Cull Off material) groups with at least one MeshRenderer side and >= " + BoardsGroupMinCm2 + " cm2; UI vs UI skipped (ZWrite Off); smaller groups listed; required targets missing / duplicated / unreadable or a cut-short run = INCOMPLETE",
                guarantee = "the readers' (front) side only. UI is Cull Off, so a UI quad vs an opposite-facing mesh face can flicker when seen from behind that face; those pairs are listed under 'Back side' and are outside this check. Depth: Near 0.01 since BUG-WLD-002; Quest's depth bits unconfirmed, so any parallel overlap within 30 mm counts" };
            var temps = new List<Mesh>();
            var groupOf = new List<string>();
            try
            {
                if (!Guard(tag)) return;
                var scene = SceneManager.GetActiveScene(); rep.scene = scene.path; rep.sceneDirtyBefore = scene.isDirty;
                var inputs = new List<KH_ZFightDetector.Input>();
                var seenR = new HashSet<Renderer>(); var seenG = new HashSet<Graphic>();
                Action<string, Transform> add = (group, root) =>
                {
                    foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!r.enabled || !seenR.Add(r)) continue;
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) { rep.problems.Add("enabled MeshRenderer without a mesh: " + KD_SceneObjects.PathOf(r.gameObject)); continue; }
                        inputs.Add(new KH_ZFightDetector.Input { path = KD_SceneObjects.PathOf(r.gameObject), mesh = mf.sharedMesh, toWorld = r.transform.localToWorldMatrix, materials = r.sharedMaterials,
                            textUi = TextUi(r), inactive = !r.gameObject.activeInHierarchy });
                        groupOf.Add(group); rep.renderers++;
                    }
                    foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (sk.enabled) rep.problems.Add("skinned renderer (not scannable): " + KD_SceneObjects.PathOf(sk.gameObject));
                    foreach (var g in root.GetComponentsInChildren<Graphic>(true))
                    {
                        if (!g.enabled || g.color.a <= 0.004f || g is TMPro.TMP_SubMeshUI || !seenG.Add(g)) continue;
                        var quad = UiQuad(g);
                        if (quad == null) continue;
                        temps.Add(quad);
                        inputs.Add(new KH_ZFightDetector.Input { path = KD_SceneObjects.PathOf(g.gameObject) + " [" + g.GetType().Name + "]", mesh = quad, toWorld = Matrix4x4.identity, textUi = true, inactive = !g.gameObject.activeInHierarchy });
                        groupOf.Add(group); rep.graphics++;
                    }
                };
                foreach (var name in BoardsRootNames)
                {
                    int count;
                    var go = KD_SceneObjects.Find(name, out count);
                    if (go == null || count == 0) { rep.problems.Add(name + " not in the scene"); rep.targets.Add(name + ": MISSING"); continue; }
                    if (count > 1) rep.problems.Add(count + " objects named " + name + " (expected 1)");
                    rep.problems.AddRange(BoardsRootProblems(name, go.transform));
                    int before = inputs.Count;
                    add(name, go.transform);
                    rep.targets.Add(KD_SceneObjects.PathOf(go) + ": " + (inputs.Count - before) + " parts");
                }
                int bottleParts = inputs.Count;
                foreach (var tool in UnityEngine.Object.FindObjectsOfType<KH_WashTool>(true))
                {
                    if (tool.tool != KH_DogWash.ToolBottle || tool.gameObject.scene != scene || tool.transform.Find("Body") == null) continue;   // 2026-10-15: the premium FoamCannon shares tool == ToolBottle (BUG-DOG-013 covers it); only the 4 shampoo bottles have a Body child
                    rep.bottles++;
                    int before = inputs.Count;
                    add(BottleGroup, tool.transform);
                    if (inputs.Count == before) rep.problems.Add("bottle " + KD_SceneObjects.PathOf(tool.gameObject) + " has no enabled renderer");
                }
                if (rep.bottles != BoardsExpectedBottles) rep.problems.Add(rep.bottles + " KH_WashTool bottles (tool " + KH_DogWash.ToolBottle + "; expected " + BoardsExpectedBottles + ")");
                rep.targets.Add(rep.bottles + " KH_WashTool bottles: " + (inputs.Count - bottleParts) + " parts");
                Debug.Log(tag + " renderers=" + rep.renderers + " graphics=" + rep.graphics + " bottles=" + rep.bottles);

                var res = KH_ZFightDetector.Run(inputs, BoardsCell, BoardsPairFloorM2, BoardsClusterLink, BoardsBudgetSeconds, "KH ZFight: boards and props", BoardsCoplanarGap, BoardsNearGap, BoardsSliverWidth);
                rep.triangles = res.triangles; rep.candidates = res.candidates; rep.seconds = res.seconds; rep.truncated = res.truncated; rep.notes.AddRange(res.notes);
                foreach (var p in res.parts) if (!p.readable) rep.problems.Add("unreadable mesh: " + p.path + " " + p.note);
                if (res.incomplete) rep.problems.Add("detector run incomplete (time budget, cancel, or a candidate / accepted cap)");
                Func<KH_ZFightDetector.Group, bool> uiA = g => res.parts[g.partIndexA].textUi, uiB = g => res.parts[g.partIndexB].textUi;
                foreach (var g in res.groups)
                {
                    if (uiA(g) && uiB(g)) { rep.uiUiSkipped++; continue; }
                    g.flags = GroupLabel(groupOf, g);
                    (g.areaCm2 >= BoardsGroupMinCm2 ? rep.pairs : rep.small).Add(g);
                }
                foreach (var g in res.backToBack)
                    if (uiA(g) != uiB(g)) { g.flags = GroupLabel(groupOf, g); rep.backSide.Add(g); }
                rep.pairs = rep.pairs.OrderBy(g => g.gapMaxMm).ThenByDescending(g => g.areaCm2).ToList();
                for (int i = 0; i < rep.pairs.Count; i++) rep.pairs[i].rank = i + 1;
                rep.small = rep.small.OrderByDescending(g => g.areaCm2).ToList();
                rep.backSide = rep.backSide.OrderBy(g => g.gapMaxMm).ToList();
                rep.counted = rep.pairs.Count;
                foreach (var label in BoardsRootNames.Concat(new[] { BottleGroup }))
                {
                    var mine = rep.pairs.Where(g => g.flags.Split('+').Contains(label)).ToList();
                    rep.perGroup.Add(label + " " + mine.Count + (mine.Count > 0 ? " (" + mine.Sum(g => g.areaCm2).ToString("F1") + " cm2, gaps " + mine.Min(g => g.gapMaxMm).ToString("F1") + "-" + mine.Max(g => g.gapMaxMm).ToString("F1") + " mm)" : ""));
                }
            }
            catch (Exception e) { rep.problems.Add("exception: " + e.GetType().Name + " " + e.Message); Debug.LogException(e); }
            finally { foreach (var m in temps) if (m != null) UnityEngine.Object.DestroyImmediate(m); }
            rep.sceneDirtyAfter = SceneManager.GetActiveScene().isDirty;
            rep.incomplete = rep.problems.Count > 0;
            bool ok = rep.counted == 0 && !rep.incomplete;
            rep.status = rep.incomplete ? "BOARDS_ZFIGHT_INCOMPLETE " + rep.counted + " (" + rep.problems.Count + " problem(s): " + string.Join("; ", rep.problems.Take(4)) + ")"
                : (ok ? "BOARDS_ZFIGHT_OK " : "BOARDS_ZFIGHT_FAIL ") + rep.counted;
            WriteBoards(rep);
            string line = tag + " " + rep.status + " | " + string.Join(", ", rep.perGroup) + " | below " + BoardsGroupMinCm2 + " cm2 " + rep.small.Count + ", back side " + rep.backSide.Count + ", UI-UI skipped " + rep.uiUiSkipped
                + " | renderers " + rep.renderers + ", graphics " + rep.graphics + ", bottles " + rep.bottles + ", tris " + rep.triangles + ", " + rep.seconds.ToString("F1") + " s, dirty " + rep.sceneDirtyBefore + "->" + rep.sceneDirtyAfter + " -> " + OutDir + "/boards.md";
            if (ok) Debug.Log(line); else Debug.LogError(line);
        }

        /// <summary>The graphic's rect as a world-space quad whose front (Unity: Cross(b - a, c - a)) is the readable side (-forward). Null for an empty rect.</summary>
        static Mesh UiQuad(Graphic g)
        {
            var w = new Vector3[4]; g.rectTransform.GetWorldCorners(w);   // bottom-left, top-left, top-right, bottom-right
            var n = Vector3.Cross(w[1] - w[0], w[2] - w[0]);
            if (n.sqrMagnitude < 1e-14f) return null;
            var m = new Mesh { name = "KH_ZFightUiQuad", hideFlags = HideFlags.HideAndDontSave };
            m.vertices = w;
            m.triangles = Vector3.Dot(n, -g.transform.forward) >= 0f ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            return m;
        }

        static string GroupLabel(List<string> groupOf, KH_ZFightDetector.Group g)
        {
            string a = groupOf[g.partIndexA], b = groupOf[g.partIndexB];
            return a == b ? a : a + "+" + b;
        }

        static void BoardsTable(StringBuilder sb, List<KH_ZFightDetector.Group> list, int max)
        {
            sb.AppendLine("| # | group | tier | facing | relation | A | B | area cm2 | gap max / mean mm | centre | notes |").AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in list.Take(max))
                sb.AppendLine("| " + g.rank + " | " + g.flags + " | " + g.tier + " | " + g.facing + " | " + g.relation + " | " + KH_ZFightDetector.Md(g.partA) + " (" + g.subA + ", " + KH_ZFightDetector.Md(g.materialA) + ") | " + KH_ZFightDetector.Md(g.partB) + " (" + g.subB + ", " + KH_ZFightDetector.Md(g.materialB) + ") | "
                    + g.areaCm2.ToString("F2") + " | " + g.gapMaxMm.ToString("F2") + " / " + g.gapMeanMm.ToString("F2") + " | " + KH_ZFightDetector.V(g.centre) + " | " + KH_ZFightDetector.Md(g.notes) + " |");
            if (list.Count > max) sb.AppendLine().AppendLine("(" + (list.Count - max) + " more in boards.json)");
        }

        static void WriteBoards(BoardsReport rep)
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(OutDir + "/boards.json", JsonUtility.ToJson(rep, true));
                var sb = new StringBuilder();
                sb.AppendLine("# ZFight scan: boards, posters, photo board, bottles (" + rep.utc + ")").AppendLine();
                sb.AppendLine("**" + rep.status + "**").AppendLine();
                sb.AppendLine("- guarantee: " + rep.guarantee);
                sb.AppendLine("- scene: " + rep.scene + " (dirty " + rep.sceneDirtyBefore + " -> " + rep.sceneDirtyAfter + ")");
                sb.AppendLine("- thresholds: " + rep.thresholds);
                sb.AppendLine("- rule: " + rep.rule);
                sb.AppendLine("- per group: " + string.Join("; ", rep.perGroup));
                sb.AppendLine("- parts: " + rep.renderers + " renderers, " + rep.graphics + " UI graphics, " + rep.bottles + " bottles, " + rep.triangles + " triangles, " + rep.candidates + " candidates, " + rep.seconds.ToString("F1") + " s" + (rep.truncated ? " (TRUNCATED)" : ""));
                sb.AppendLine("- UI vs UI groups skipped: " + rep.uiUiSkipped);
                foreach (var t in rep.targets) sb.AppendLine("- target " + KH_ZFightDetector.Md(t));
                if (rep.problems.Count > 0)
                {
                    sb.AppendLine().AppendLine("## Problems (INCOMPLETE: the result does not cover every required target)").AppendLine();
                    foreach (var p in rep.problems) sb.AppendLine("- " + KH_ZFightDetector.Md(p));
                }
                sb.AppendLine().AppendLine("## Counted (same-facing, at least one mesh, >= " + BoardsGroupMinCm2 + " cm2)").AppendLine();
                if (rep.pairs.Count == 0) sb.AppendLine("None."); else BoardsTable(sb, rep.pairs, 200);
                sb.AppendLine().AppendLine("## Below " + BoardsGroupMinCm2 + " cm2 per group (listed, not counted)").AppendLine();
                if (rep.small.Count == 0) sb.AppendLine("None."); else BoardsTable(sb, rep.small, 100);
                sb.AppendLine().AppendLine("## Back side (UI vs an opposite-facing mesh face: seen only from behind that face; outside the guarantee, not counted)").AppendLine();
                if (rep.backSide.Count == 0) sb.AppendLine("None."); else BoardsTable(sb, rep.backSide, 100);
                if (rep.notes.Count > 0) { sb.AppendLine().AppendLine("## Notes").AppendLine(); foreach (var n in rep.notes) sb.AppendLine("- " + KH_ZFightDetector.Md(n)); }
                File.WriteAllText(OutDir + "/boards.md", sb.ToString());
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
