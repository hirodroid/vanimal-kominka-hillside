using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    // BUG-DOG-013: which premium wash tool z-fights. 3 reads the prefab assets only (no scene); 4 renders them in a
    // preview scene (EditorSceneManager.NewPreviewScene), so the open scene is not touched (isDirty is logged before/after).
    // Coordinates are in each prefab root's space (metres).
    public static partial class KH_ZFightScanEditor
    {
        const string PremiumDir = "Assets/KominkaHillside/Prefabs/WashPremium/";
        static readonly string[] PremiumNames = { "WaterPistol", "DuckWaterGun", "FishWaterGun", "BambooWaterGun", "FoamCannon" };
        const float ToolCoplanarGap = 0.0005f, ToolNearGap = 0.003f, ToolMinAreaM2 = 1e-5f /* 0.1 cm2 */, ToolClusterLink = 0.02f, ToolBudgetSeconds = 60, ToolFov = 30f, ToolElevation = 20f;
        static readonly float[] ToolYaws = { 0f, 90f, 180f, 270f };

        [Serializable] public class PremiumEntry
        {
            public string name, path, note; public bool found, truncated; public int triangles, renderers; public double seconds;
            public float transparentOpaqueCm2; public int transparentOpaqueGroups, backToBackCount; public float backToBackAreaCm2;
            public List<KH_ZFightDetector.PartInfo> parts = new List<KH_ZFightDetector.PartInfo>();
            public List<KH_ZFightDetector.Group> groups = new List<KH_ZFightDetector.Group>();
            public List<KH_ZFightDetector.Group> backToBackTop = new List<KH_ZFightDetector.Group>();
            public List<string> notes = new List<string>();
        }

        [Serializable] public class PremiumReport
        {
            public string utc, thresholds, transparencyNote;
            public List<PremiumEntry> prefabs = new List<PremiumEntry>();
        }

        const string TransparencyNote = "A transparent (Fade, renderQueue > 2500) face coplanar with an opaque face is drawn after it with ZTest LEqual: with ZWrite Off it does not fight for the depth buffer, but per pixel it passes or fails the test against the opaque depth, so it shows as patchy show-through that crawls as the tool moves (looks like z-fighting). Two opaque coplanar faces are classic z-fighting. Two transparent coplanar faces flicker by sort order.";

        // ================================================================ 3 - Scan

        [MenuItem(Menu + "3 - Scan Premium Tools")]
        public static void ScanPremium()
        {
            const string tag = "[KH ZFight 3]";
            var rep = new PremiumReport { utc = DateTime.UtcNow.ToString("o"), transparencyNote = TransparencyNote,
                thresholds = "|dot| > " + KH_ZFightDetector.NormalDot + "; coplanar <= " + ToolCoplanarGap * 1000 + " mm, near <= " + ToolNearGap * 1000 + " mm (tighter than the farmhouse 3 / 20 mm: held at ~0.3-0.6 m, a gap above ~0.1 mm cannot fight even in a 16-bit buffer, so 3 mm is the outer bound the task gave); overlap > " + ToolMinAreaM2 * 1e4f + " cm2 per triangle pair; clusters linked within " + ToolClusterLink * 100 + " cm; ranked by area x (near 0.25)" };
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { Debug.LogWarning(tag + " skipped: playing or compiling"); return; }
            foreach (var name in PremiumNames)
            {
                var e = new PremiumEntry { name = name, path = PremiumDir + name + ".prefab" };
                rep.prefabs.Add(e);
                try
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(e.path);
                    if (prefab == null) { e.note = "prefab not found"; Debug.LogWarning(tag + " missing " + e.path); continue; }
                    e.found = true;
                    var rootW2L = prefab.transform.worldToLocalMatrix;
                    var inputs = new List<KH_ZFightDetector.Input>(); var ext = new Bounds(); bool has = false;
                    foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null) { e.notes.Add("MeshFilter without mesh: " + KH_ZFightDetector.LeafPath(mf.transform, prefab.transform)); continue; }
                        var mr = mf.GetComponent<MeshRenderer>();
                        bool inactive = mr == null || !mr.enabled; for (var t = mf.transform; t != null; t = t.parent) if (!t.gameObject.activeSelf) inactive = true;
                        var m = rootW2L * mf.transform.localToWorldMatrix;
                        inputs.Add(new KH_ZFightDetector.Input { path = KH_ZFightDetector.LeafPath(mf.transform, prefab.transform), mesh = mf.sharedMesh, toWorld = m, materials = mr != null ? mr.sharedMaterials : null, inactive = inactive });
                        var b = mf.sharedMesh.bounds; var c = m.MultiplyPoint3x4(b.center);
                        if (!has) { ext = new Bounds(c, Vector3.zero); has = true; }
                        foreach (var sx in new[] { -1, 1 }) foreach (var sy in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 })
                            ext.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(sx, sy, sz))));
                    }
                    e.renderers = inputs.Count;
                    float cell = Mathf.Clamp(ext.size.magnitude / 40f, 0.002f, 0.05f);
                    var res = KH_ZFightDetector.Run(inputs, cell, ToolMinAreaM2, ToolClusterLink, ToolBudgetSeconds, "KH ZFight: " + name, ToolCoplanarGap, ToolNearGap);
                    e.parts = res.parts; e.triangles = res.triangles; e.seconds = res.seconds; e.truncated = res.truncated; e.notes.AddRange(res.notes);
                    e.notes.Add("cell " + cell.ToString("F3") + " m, prefab extent " + KH_ZFightDetector.V(ext.size) + " m, candidates " + res.candidates + ", accepted " + res.accepted);
                    foreach (var g in res.groups.Concat(res.backToBack)) g.score = g.areaCm2 * (g.tier == "near" ? 0.25f : 1f);
                    e.groups = res.groups.OrderByDescending(g => g.score).ToList();
                    for (int i = 0; i < e.groups.Count; i++) e.groups[i].rank = i + 1;
                    var to = e.groups.Where(g => g.notes.Contains("transparent vs opaque")).ToList();
                    e.transparentOpaqueGroups = to.Count; e.transparentOpaqueCm2 = to.Sum(g => g.areaCm2);
                    e.backToBackCount = res.backToBack.Count; e.backToBackAreaCm2 = res.backToBack.Sum(g => g.areaCm2);
                    e.backToBackTop = res.backToBack.OrderByDescending(g => g.areaCm2).Take(10).ToList();
                }
                catch (Exception ex) { e.note = "exception: " + ex.Message; Debug.LogException(ex); }
            }
            WritePremium(rep);
            foreach (var e in rep.prefabs)
                Debug.Log(tag + " " + e.name + ": found=" + e.found + " tris=" + e.triangles + " groups=" + e.groups.Count + " (coplanar " + e.groups.Count(g => g.tier == "coplanar") + ", area "
                    + e.groups.Where(g => g.tier == "coplanar").Sum(g => g.areaCm2).ToString("F1") + " cm2) transparent-vs-opaque=" + e.transparentOpaqueGroups + " backToBack=" + e.backToBackCount + " " + e.seconds.ToString("F1") + " s" + (e.truncated ? " TRUNCATED" : ""));
            Debug.Log(tag + " -> " + OutDir + "/premium.md");
        }

        static void WritePremium(PremiumReport rep)
        {
            try
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(OutDir + "/premium.json", JsonUtility.ToJson(rep, true));
                var sb = new StringBuilder();
                sb.AppendLine("# ZFight scan: premium wash tools (" + rep.utc + ")").AppendLine();
                sb.AppendLine("- thresholds: " + rep.thresholds);
                sb.AppendLine("- coordinates: prefab-root space, metres");
                sb.AppendLine("- transparency: " + rep.transparencyNote).AppendLine();
                sb.AppendLine("| prefab | tris | renderers | coplanar groups / cm2 | near groups / cm2 | transparent-vs-opaque groups / cm2 | back-to-back groups / cm2 |").AppendLine("|---|---|---|---|---|---|---|");
                foreach (var e in rep.prefabs)
                {
                    var cop = e.groups.Where(g => g.tier == "coplanar").ToList(); var near = e.groups.Where(g => g.tier == "near").ToList();
                    sb.AppendLine("| " + e.name + (e.found ? "" : " (missing)") + " | " + e.triangles + " | " + e.renderers + " | " + cop.Count + " / " + cop.Sum(g => g.areaCm2).ToString("F2") + " | " + near.Count + " / " + near.Sum(g => g.areaCm2).ToString("F2")
                        + " | " + e.transparentOpaqueGroups + " / " + e.transparentOpaqueCm2.ToString("F2") + " | " + e.backToBackCount + " / " + e.backToBackAreaCm2.ToString("F2") + " |");
                }
                foreach (var e in rep.prefabs)
                {
                    sb.AppendLine().AppendLine("## " + e.name + (e.truncated ? " (TRUNCATED)" : "")).AppendLine();
                    if (!e.found) { sb.AppendLine("- " + e.path + ": " + e.note); continue; }
                    sb.AppendLine("| renderer | sub | material | shader | queue | ZWrite | Cull Off | transparent | tris |").AppendLine("|---|---|---|---|---|---|---|---|---|");
                    foreach (var p in e.parts)
                        for (int s = 0; s < p.materials.Count; s++)
                        {
                            var m = p.materials[s];
                            sb.AppendLine("| " + KH_ZFightDetector.Md(p.path) + (p.inactive ? " (inactive)" : "") + (p.readable ? "" : " (unreadable)") + " | " + s + " | " + KH_ZFightDetector.Md(m.name) + " | " + KH_ZFightDetector.Md(m.shader) + " | " + m.renderQueue + " | " + m.zWrite + " | " + m.doubleSided + " | " + m.transparent + " | " + (s == 0 ? p.triangles.ToString() : "") + " |");
                        }
                    sb.AppendLine();
                    if (e.groups.Count == 0) sb.AppendLine("No same-facing coplanar / near overlap above the threshold.");
                    else
                    {
                        sb.AppendLine("| rank | tier | facing | relation | A (sub, material) | B (sub, material) | area cm2 | local centre | gap max / mean mm | notes |").AppendLine("|---|---|---|---|---|---|---|---|---|---|");
                        foreach (var g in e.groups.Take(40))
                            sb.AppendLine("| " + g.rank + " | " + g.tier + " | " + g.facing + " | " + g.relation + " | " + KH_ZFightDetector.Md(g.partA) + " (" + g.subA + ", " + KH_ZFightDetector.Md(g.materialA) + ") | " + KH_ZFightDetector.Md(g.partB) + " (" + g.subB + ", " + KH_ZFightDetector.Md(g.materialB) + ") | "
                                + g.areaCm2.ToString("F2") + " | " + g.centre.x.ToString("F3") + ", " + g.centre.y.ToString("F3") + ", " + g.centre.z.ToString("F3") + " | " + g.gapMaxMm.ToString("F2") + " / " + g.gapMeanMm.ToString("F2") + " | " + g.notes + (g.capped ? " (capped)" : "") + " |");
                    }
                    sb.AppendLine().AppendLine("- back-to-back (opposite normals, culled): " + e.backToBackCount + " groups, " + e.backToBackAreaCm2.ToString("F2") + " cm2");
                    foreach (var n in e.notes) sb.AppendLine("- " + n);
                    if (!string.IsNullOrEmpty(e.note)) sb.AppendLine("- " + e.note);
                }
                File.WriteAllText(OutDir + "/premium.md", sb.ToString());
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        // ================================================================ 4 - Capture

        [MenuItem(Menu + "4 - Capture Premium Tools")]
        public static void CapturePremium()
        {
            const string tag = "[KH ZFight 4]";
            var rep = new CaptureReport { utc = DateTime.UtcNow.ToString("o") };
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { Debug.LogWarning(tag + " skipped: playing or compiling"); return; }
            var main = SceneManager.GetActiveScene(); rep.scene = main.path; rep.sceneDirtyBefore = main.isDirty;
            PremiumReport scan = null;
            try { if (File.Exists(OutDir + "/premium.json")) scan = JsonUtility.FromJson<PremiumReport>(File.ReadAllText(OutDir + "/premium.json")); }
            catch (Exception ex) { rep.notes.Add("premium.json unreadable: " + ex.Message); }
            rep.usedScan = scan != null;
            float near = 0.05f, far = 1000f; string camNote = "";
            try { var probe = PlayerCamera(out near, out far, out camNote); UnityEngine.Object.DestroyImmediate(probe.gameObject); } catch (Exception ex) { rep.notes.Add("player camera lookup failed: " + ex.Message); }
            rep.cameraNote = camNote + "; preview scene, solid grey background, key + fill directional lights, FOV " + ToolFov + ", elevation " + ToolElevation;
            Directory.CreateDirectory(EvidenceDir); Directory.CreateDirectory(FlickerDir);
            foreach (var name in PremiumNames)
            {
                Scene preview = default(Scene); bool open = false; var temps = new List<GameObject>();
                try
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PremiumDir + name + ".prefab");
                    if (prefab == null) { rep.notes.Add(name + ": prefab not found"); Debug.LogWarning(tag + " missing " + name); continue; }
                    preview = EditorSceneManager.NewPreviewScene(); open = true;
                    var inst = PrefabUtility.InstantiatePrefab(prefab, preview) as GameObject;
                    if (inst == null) { rep.notes.Add(name + ": InstantiatePrefab into the preview scene failed"); continue; }
                    inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var camGo = EditorUtility.CreateGameObjectWithHideFlags("KH_ZFightToolCam", HideFlags.HideAndDontSave, typeof(Camera));
                    temps.Add(camGo); SceneManager.MoveGameObjectToScene(camGo, preview);
                    var cam = camGo.GetComponent<Camera>();
                    cam.enabled = false; cam.scene = preview; cam.cameraType = CameraType.Preview; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.42f, 0.45f, 0.5f, 1f);
                    cam.nearClipPlane = near; cam.farClipPlane = far; cam.allowHDR = false; cam.allowMSAA = false; cam.useOcclusionCulling = false; cam.stereoTargetEye = StereoTargetEyeMask.None;
                    temps.Add(AddLight(preview, Quaternion.Euler(50f, -30f, 0f), 1.1f)); temps.Add(AddLight(preview, Quaternion.Euler(-20f, 150f, 0f), 0.5f));
                    var rs = inst.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) { rep.notes.Add(name + ": no renderers"); continue; }
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float radius = Mathf.Max(b.extents.magnitude, 0.02f), dist = Mathf.Max(radius / Mathf.Sin(ToolFov * 0.5f * Mathf.Deg2Rad) * 1.05f, near * 4f);
                    foreach (var yaw in ToolYaws)
                    {
                        var rot = Quaternion.Euler(ToolElevation, yaw, 0f); var eye = b.center - rot * Vector3.forward * dist;
                        ShootTool(rep, cam, name + "-" + Mathf.RoundToInt(yaw), eye, rot, ToolFov, near, far, name + " yaw " + yaw + " at " + dist.ToString("F2") + " m", b.center);
                    }
                    // close-ups on the scan's two largest same-facing groups (prefab-root space == preview-scene space here)
                    var entry = scan != null ? scan.prefabs.FirstOrDefault(p => p.name == name) : null;
                    if (entry != null)
                        foreach (var g in entry.groups.Take(2))
                        {
                            var n = g.normal.sqrMagnitude > 0.5f ? g.normal : Vector3.up; var c = inst.transform.TransformPoint(g.centre);
                            var eye = c + (n + Vector3.up * 0.25f).normalized * Mathf.Max(0.25f, near * 4f);
                            ShootTool(rep, cam, name + "-zf" + g.rank, eye, Quaternion.LookRotation(c - eye), NarrowFov, near, far,
                                name + " group " + g.rank + " " + g.tier + " " + g.partA + "#" + g.subA + " (" + g.materialA + ") x " + g.partB + "#" + g.subB + " (" + g.materialB + ") " + g.areaCm2.ToString("F2") + " cm2", c);
                        }
                }
                catch (Exception ex) { rep.notes.Add(name + ": " + ex.GetType().Name + " " + ex.Message); Debug.LogException(ex); }
                finally
                {
                    foreach (var t in temps) if (t != null) UnityEngine.Object.DestroyImmediate(t);   // DontSave objects can outlive the preview scene
                    if (open) EditorSceneManager.ClosePreviewScene(preview);
                }
            }
            rep.sceneDirtyAfter = SceneManager.GetActiveScene().isDirty;
            if (!rep.sceneDirtyBefore && rep.sceneDirtyAfter) rep.notes.Add("WARNING: the open scene became dirty during capture; do not save it");
            WriteCapture(rep, "premium-capture");
            Debug.Log(tag + " shots=" + rep.shots.Count + " errors=" + rep.shots.Count(s => s.error != null) + " usedScan=" + rep.usedScan + " dirty " + rep.sceneDirtyBefore + "->" + rep.sceneDirtyAfter + " -> " + EvidenceDir + ", " + OutDir + "/premium-capture.md");
        }

        static GameObject AddLight(Scene scene, Quaternion rot, float intensity)
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags("KH_ZFightToolLight", HideFlags.HideAndDontSave, typeof(Light));
            SceneManager.MoveGameObjectToScene(go, scene);
            var l = go.GetComponent<Light>(); l.type = LightType.Directional; l.intensity = intensity; l.color = Color.white; l.shadows = LightShadows.None; go.transform.rotation = rot;
            return go;
        }

        static void ShootTool(CaptureReport rep, Camera cam, string name, Vector3 eye, Quaternion rot, float fov, float near, float far, string target, Vector3 aim)
        {
            var rec = new ShotRecord { name = name, eye = eye, aim = aim, fov = fov, near = near, far = far, target = target };
            try
            {
                cam.transform.SetPositionAndRotation(eye, rot); cam.fieldOfView = fov;
                var shot = KH_ZFightDetector.Render(cam, EvidenceDir + "/" + name + ".jpg", FlickerDir + "/" + name + "-diff.jpg");
                rec.file = shot.file; rec.diffFile = shot.diffFile; rec.changedFraction = shot.changedFraction; rec.error = shot.error;
            }
            catch (Exception ex) { rec.error = ex.Message; }
            rep.shots.Add(rec);
        }
    }
}
