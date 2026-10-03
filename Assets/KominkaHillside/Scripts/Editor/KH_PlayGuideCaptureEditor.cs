using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using KominkaDogRun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    /// <summary>Guide/13 - Capture Play Guide Cuts (IMP-UI-002, docs/plans/2026-10-03-onsite-guides.md §3.3 / §4).
    ///
    /// One Edit-mode run makes the eight cuts of the rebuilt board A「あそびかた」(frisbee F1-F4, notes N1-N2, ball B1-B2):
    /// the menu cuts from a text-less copy of the real hand menu (.Menu.cs), the world cuts from cameras and render-only copies
    /// in the production scene (.World.cs), the marks drawn in code (.Compose.cs), packed into one 2048 x 768 atlas with a JSON
    /// manifest the board builder (Guide/1, wave 2) reads instead of knowing about menus or cameras.
    ///
    /// Scene safety (vrc-scene-edit-safety): the scene must be saved and idle (KH_CourseGroundEditor.Idle); everything created is
    /// HideAndDontSave or lives in a preview scene, nothing original is moved, nothing is saved; at the end the scene must not be
    /// dirty and no temporary object may be left (else FAIL). The atlas and manifest are only written when all eight cuts
    /// succeeded, and only when their bytes changed. Review copies go to Temp/kh-play-guide/ (not committed).
    /// </summary>
    public static partial class KH_PlayGuideCaptureEditor
    {
        const string Menu = "Tools/KominkaHillside/Guide/";
        public const string GuideFolder = "Assets/KominkaHillside/Textures/Guide";
        public const string AtlasPath = GuideFolder + "/KH_PlayGuideCuts.png";
        public const string ManifestPath = GuideFolder + "/KH_PlayGuideCuts.json";
        /// <summary>User override for F4 (plan §4.2 / Q3). A folder ending in ~ is not imported by Unity but is kept by Git.</summary>
        public const string F4OverridePath = GuideFolder + "/Source~/F4.png";
        public const string OutDir = "Temp/kh-play-guide";
        /// <summary>Bump when the marks' look or the cut layout changes (the manifest carries it).</summary>
        public const int PlayGuideCutsVersion = 4;   // 2: searched viewpoints, dimmed menu, unlit aim states; 3: F2 framing, N1 by the game verdict; 4: board-scale marks (12 px strokes, dark edge)
        public const string ManifestFormat = "KH_PlayGuideCuts/1";
        /// <summary>Plan §3.2 / §3.3: the step numbers are UI on the board (a Ø56 circle + TMP digit at each cut's top-left), not
        /// pixels. Flip to bake a vermilion badge with a stroke digit into F1-F4 / B1-B2 instead (the manifest says which).</summary>
        static readonly bool BakeStepBadge = false;
        /// <summary>The dog every cut uses: index 2 = Lab (KH_FrisbeeJumpClipEditor.Breeds / KH_DogWalkingEditor.Keys order). The Lab
        /// jump table is the one the user approved in PCVR (stage 2); the other breeds' tables are provisional.</summary>
        public const int GuideDog = 2;
        const string TmpPrefix = "KH_PlayGuideTmp_";
        const string TranslationsPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/UITranslations.json";
        const int ImportMaxSize = 2048;   // the atlas's own width: no downscale; ~1.02 MB PC (DXT1) / ~0.93 MB Android+iOS (ASTC 6x6) with mips (plan §5)

        // ================================================================ manifest (read by the board builder)
        [Serializable]
        public class Manifest
        {
            public string format = ManifestFormat;
            public int drawVersion = PlayGuideCutsVersion;
            public string atlas = AtlasPath;
            public int atlasWidth = AtlasW, atlasHeight = AtlasH, cutWidth = CutW, cutHeight = CutH;
            public float uvInsetPx = UvInsetPx;
            public string atlasSha256;
            public string capturedUtc;
            public string coordinates = "uv*: RawImage.uvRect of the cut (0..1 of the atlas, origin bottom-left). Every other x/y/w/h: 0..1 of the cut's visible (uv) area, origin bottom-left, x/y = centre (use as RectTransform anchorMin = anchorMax, size = w/h x the RawImage size).";
            public List<CutEntry> cuts = new List<CutEntry>();
        }

        [Serializable]
        public class CutEntry
        {
            public string id;            // F1..F4, N1, N2, B1, B2
            public string group;         // "frisbee" | "ball"
            public int step;             // 1..4 (0 = a note cut without a number)
            public string captionKey;    // the board caption under / beside the cut (UITranslations.json, owned by the localisation work)
            public string source;        // "edit-menu" | "edit-world" | "user-photo"
            public float uvX, uvY, uvW, uvH;
            public float badgeX, badgeY, badgeR;   // where the step number goes (centre, radius as a share of the cut width)
            public bool badgeBaked;
            public List<Callout> callouts = new List<Callout>();
            public List<MarkEntry> marks = new List<MarkEntry>();
        }

        [Serializable]
        public class Callout
        {
            public string key;           // the menu's own translation key (bind the board's TMP to it)
            public string keySource;     // where the key was read from
            public string role;          // "tab" | "button" | "dogName" | "label"
            public float x, y, w, h;     // centre and size in the cut (see Manifest.coordinates)
            public bool framed;          // the image already carries a vermilion frame around this place
            public bool inInset;         // the place is inside the zoom inset (text scale = the inset's)
        }

        [Serializable]
        public class MarkEntry
        {
            public string kind;          // frame | ring | arrow | dashed-arrow | inset | hand | badge
            public float x, y, w, h;
            public string note;
        }

        // ================================================================ report (Temp/kh-play-guide/capture.json)
        [Serializable]
        public class Report
        {
            public string utc, scene, status = "RUNNING";
            public string atlas = "-", manifest = "-", importer = "-";
            public bool sceneDirtyBefore, sceneDirtyAfter;
            public int leftovers = -1, loadedScenes = -1;
            public List<string> errors = new List<string>();
            public List<string> warnings = new List<string>();
            public List<string> notes = new List<string>();
            public List<CutReport> cuts = new List<CutReport>();
            public string limitations = "Edit mode only: the cameras show the saved scene (dogs at their saved places, Edit-mode sky), poses are authored clips, not Play. Callout words are not drawn here; the board's TMP adds them. Words of the world (signs) are only detected, not hidden.";
        }

        [Serializable]
        public class CutReport
        {
            public string id, source, status, error, png, raw;
            public List<string> notes = new List<string>();
            public List<string> warnings = new List<string>();
            public List<string> callouts = new List<string>();
            public List<string> marks = new List<string>();
            public List<string> subjects = new List<string>();             // "disc (x, y, w, h)" in cut px (512 x 384, bottom-left)
            public List<string> forbiddenInFrustum = new List<string>();   // must be empty
            public string blocked;                                         // N1 only: which blocked condition, where
        }

        sealed class CalloutPx { public string key, keySource, role; public Rect rect; public bool framed, inInset; }

        sealed class CutWork
        {
            public string id, source = "edit-world", error;
            public Img raw, final;
            public readonly List<MarkPx> marks = new List<MarkPx>();
            public readonly List<CalloutPx> callouts = new List<CalloutPx>();
            public readonly List<string> notes = new List<string>();
            public readonly List<string> warnings = new List<string>();
            public readonly List<string> subjects = new List<string>();    // projected rect of each subject (cut px)
            public readonly List<string> forbidden = new List<string>();   // forbidden objects visible in the accepted frustum (must be empty)
            public string blocked;                                         // N1: the game condition the chosen throw hits, stand, direction, landing
            public bool Ok { get { return error == null && final != null; } }
        }

        static readonly Dictionary<string, string> CaptionKeys = new Dictionary<string, string>
        {
            { "F1", "guide.frisbee.cap1" }, { "F2", "guide.frisbee.cap2" }, { "F3", "guide.frisbee.cap3" }, { "F4", "guide.frisbee.cap4" },
            { "N1", "guide.frisbee.note1" }, { "N2", "guide.frisbee.note2" }, { "B1", "guide.ball.cap1" }, { "B2", "guide.ball.cap2" },
        };
        static readonly Dictionary<string, int> Steps = new Dictionary<string, int> { { "F1", 1 }, { "F2", 2 }, { "F3", 3 }, { "F4", 4 }, { "N1", 0 }, { "N2", 0 }, { "B1", 1 }, { "B2", 2 } };

        // ================================================================ temporaries
        static readonly List<Object> s_temps = new List<Object>();

        static GameObject NewTemp(string name)
        {
            var go = new GameObject(TmpPrefix + name) { hideFlags = HideFlags.HideAndDontSave };
            s_temps.Add(go);
            return go;
        }

        static Mesh TrackMesh(Mesh m) { m.hideFlags = HideFlags.HideAndDontSave; s_temps.Add(m); return m; }

        static void DestroyTemp(Object o)
        {
            if (o == null) return;
            s_temps.Remove(o);
            Object.DestroyImmediate(o);
        }

        /// <summary>Destroys every temporary except 'keep' (and the meshes / materials those keep using).</summary>
        static void DestroyAllTemps(params Object[] keep)
        {
            var kept = new HashSet<Object>();
            foreach (var k in keep)
            {
                if (k == null) continue;
                kept.Add(k);
                var go = k as GameObject;
                if (go == null) continue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) kept.Add(mf.sharedMesh);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m != null) kept.Add(m);
            }
            for (int i = s_temps.Count - 1; i >= 0; i--)
            {
                var o = s_temps[i];
                if (o != null && kept.Contains(o)) continue;
                if (o != null) Object.DestroyImmediate(o);
                s_temps.RemoveAt(i);
            }
        }

        /// <summary>Temporary objects still alive anywhere (scene, DontSave or preview), found by name.</summary>
        static int CountLeftovers(List<string> into)
        {
            int n = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name.StartsWith(TmpPrefix, StringComparison.Ordinal) && !EditorUtility.IsPersistent(go)) { n++; into.Add("leftover GameObject " + go.name); }
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
                if (m != null && m.name.StartsWith(TmpPrefix, StringComparison.Ordinal) && !EditorUtility.IsPersistent(m)) { n++; into.Add("leftover Mesh " + m.name); }
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.name.StartsWith(TmpPrefix, StringComparison.Ordinal) && !EditorUtility.IsPersistent(m)) { n++; into.Add("leftover Material " + m.name); }
            return n;
        }

        static void Guard(CutWork w, Action a)
        {
            try { a(); }
            catch (Exception ex) { w.error = ex.GetType().Name + ": " + ex.Message; Debug.LogException(ex); }
        }

        // ================================================================ the menu
        [MenuItem(Menu + "13 - Capture Play Guide Cuts")]
        public static void Capture()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);   // Edit mode, not compiling, the production scene alone and saved
            var scene = SceneManager.GetActiveScene();
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scene = scene.path, sceneDirtyBefore = scene.isDirty };
            var works = CutOrder.ToDictionary(id => id, id => new CutWork { id = id });
            KD_TreatPouch pouch = null;
            DestroyAllTemps();
            try
            {
                pouch = KH_CourseTravelEditor.FindPouch();   // exactly one menu, wired to route + language
                GateMenu(pouch);
                var world = ResolveWorld(pouch, report);
                foreach (var id in new[] { "F1", "B1", "N2" }) works[id].source = "edit-menu";
                CaptureMenuCuts(pouch, works, report);
                Guard(works["F2"], () => ShotF2(world, works["F2"])); DestroyAllTemps();
                Guard(works["F3"], () => ShotF3(world, works["F3"])); DestroyAllTemps();
                Guard(works["F4"], () => ShotF4(world, works["F4"])); DestroyAllTemps();
                Guard(works["N1"], () => ShotN1(world, works["N1"])); DestroyAllTemps();
                Guard(works["B2"], () => ShotB2(world, works["B2"])); DestroyAllTemps();
            }
            catch (Exception ex)
            {
                report.errors.Add("FAIL " + ex.Message);
                Debug.LogException(ex);
            }
            finally { DestroyAllTemps(); }

            // ---- scene safety (plan §4.1 step 4)
            var left = new List<string>();
            report.leftovers = CountLeftovers(left);
            report.loadedScenes = SceneManager.sceneCount;
            report.sceneDirtyAfter = scene.isDirty;
            if (report.sceneDirtyAfter) report.errors.Add("FAIL the scene became dirty during the capture: reopen it WITHOUT saving (File > Open Scene, Don't Save) and report this run");
            if (report.leftovers > 0) report.errors.Add("FAIL " + report.leftovers + " temporary object(s) left: " + string.Join("; ", left));
            if (report.loadedScenes != 1) report.errors.Add("FAIL " + report.loadedScenes + " scenes loaded after the capture (the port source scene was not closed?)");

            // ---- badges (optional), callout keys
            foreach (var w in works.Values)
            {
                if (w.final == null) continue;
                if (BakeStepBadge && Steps[w.id] > 0) DrawBadge(w.final, BadgeCentre(), BadgeRadius, Steps[w.id], w.marks);
            }
            if (pouch != null) CheckKeys(pouch, works, report);

            // ---- review copies
            Directory.CreateDirectory(OutDir + "/cuts");
            var finals = new Dictionary<string, Img>();
            foreach (var id in CutOrder)
            {
                var w = works[id];
                if (w.forbidden.Count > 0) w.error = w.error ?? ("forbidden objects in the frustum: " + string.Join(", ", w.forbidden));
                var cr = new CutReport { id = id, source = w.source, status = w.Ok ? "OK" : "FAIL", error = w.error };
                cr.notes.AddRange(w.notes); cr.warnings.AddRange(w.warnings);
                cr.subjects.AddRange(w.subjects); cr.forbiddenInFrustum.AddRange(w.forbidden); cr.blocked = w.blocked;
                report.warnings.AddRange(w.warnings);
                var img = w.final ?? Placeholder();   // a failed cut's image still goes to Temp for review (never to the atlas asset)
                finals[id] = img;
                cr.png = OutDir + "/cuts/" + id + ".png";
                WriteIfChanged(cr.png, img.Png());
                if (w.raw != null) { cr.raw = OutDir + "/cuts/" + id + "_raw.png"; WriteIfChanged(cr.raw, w.raw.Png()); }
                foreach (var c in w.callouts) cr.callouts.Add(c.role + " " + c.key + " (" + c.keySource + ") '" + (pouch != null && pouch.language != null ? pouch.language._TextForLanguage(c.key, "ja") : "?") + "' at " + c.rect + (c.inInset ? " [inset]" : "") + (c.framed ? " [framed]" : ""));
                foreach (var m in w.marks) cr.marks.Add(m.kind + " " + m.rect + " " + m.note);
                if (!w.Ok) report.errors.Add("FAIL cut " + id + ": " + (w.error ?? "no image"));
                report.cuts.Add(cr);
            }
            var atlas = PackAtlas(finals);
            var atlasPng = atlas.Png();
            WriteIfChanged(OutDir + "/KH_PlayGuideCuts_preview.png", atlasPng);

            // ---- the asset (only when everything above passed)
            if (report.errors.Count == 0)
            {
                try { WriteAssets(atlasPng, works, report); }
                catch (Exception ex) { report.errors.Add("FAIL writing the atlas / manifest: " + ex.Message); Debug.LogException(ex); }
            }
            else { report.atlas = "not written (FAIL)"; report.manifest = "not written (FAIL)"; }

            report.status = report.errors.Count == 0 ? "PLAY_GUIDE_CUTS_OK" : "FAIL";
            File.WriteAllText(OutDir + "/capture.json", JsonUtility.ToJson(report, true));
            string summary = "[KH PlayGuide] " + report.status + ": atlas " + report.atlas + ", manifest " + report.manifest + ", importer " + report.importer
                + ", " + report.warnings.Count + " warning(s); scene dirty " + report.sceneDirtyAfter + ", leftovers " + report.leftovers + " -> " + OutDir + "/capture.json, cuts in " + OutDir + "/cuts/";
            if (report.errors.Count == 0) Debug.Log(summary);
            else Debug.LogError(summary + "\n" + string.Join("\n", report.errors));
        }

        static Vector2 BadgeCentre() { return new Vector2(BadgeZonePx * 0.5f, CutH - BadgeZonePx * 0.5f); }
        const float BadgeRadius = 26f;

        /// <summary>Plan §7.1: every callout key must be in the menu's translation table (FAIL); caption keys belong to the
        /// localisation work running in parallel, so a missing one is only a warning.</summary>
        static void CheckKeys(KD_TreatPouch p, Dictionary<string, CutWork> works, Report report)
        {
            var lang = p.language;
            foreach (var w in works.Values)
                foreach (var c in w.callouts)
                {
                    string ja = lang != null ? lang._TextForLanguage(c.key, "ja") : null;
                    if (string.IsNullOrEmpty(ja) || ja == c.key) report.errors.Add("FAIL callout key '" + c.key + "' (" + w.id + " " + c.role + ") has no ja text in KD_Language");
                }
            string table = File.Exists(TranslationsPath) ? File.ReadAllText(TranslationsPath) : "";
            foreach (var kv in CaptionKeys)
                if (table.IndexOf("\"" + kv.Value + "\"", StringComparison.Ordinal) < 0)
                    report.warnings.Add("caption key " + kv.Value + " (" + kv.Key + ") not in " + TranslationsPath + " yet (the localisation work adds it)");
        }

        // ================================================================ writing
        static bool WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path))
            {
                var old = File.ReadAllBytes(path);
                if (old.Length == bytes.Length && old.SequenceEqual(bytes)) return false;
            }
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, bytes);
            return true;
        }

        static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static void EnsureGuideFolder()
        {
            if (AssetDatabase.IsValidFolder(GuideFolder)) return;
            if (Directory.Exists(GuideFolder)) { AssetDatabase.ImportAsset(GuideFolder, ImportAssetOptions.ForceSynchronousImport); if (AssetDatabase.IsValidFolder(GuideFolder)) return; }
            string parent = Path.GetDirectoryName(GuideFolder).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) throw new InvalidOperationException(parent + " is not an asset folder");
            AssetDatabase.CreateFolder(parent, Path.GetFileName(GuideFolder));
        }

        static void WriteAssets(byte[] atlasPng, Dictionary<string, CutWork> works, Report report)
        {
            EnsureGuideFolder();
            string sha = Sha256(atlasPng);
            bool atlasChanged = WriteIfChanged(AtlasPath, atlasPng);
            if (atlasChanged || AssetImporter.GetAtPath(AtlasPath) == null) AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            report.atlas = (atlasChanged ? "written " : "unchanged ") + AtlasPath + " (sha256 " + sha.Substring(0, 12) + ")";
            report.importer = ConfigureImporter(AtlasPath);

            // manifest; the capture time is carried over while the atlas bytes stay the same, so a rerun writes nothing
            var m = new Manifest { atlasSha256 = sha, capturedUtc = report.utc };
            if (File.Exists(ManifestPath))
            {
                try
                {
                    var old = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
                    if (old != null && old.atlasSha256 == sha && !string.IsNullOrEmpty(old.capturedUtc)) m.capturedUtc = old.capturedUtc;
                }
                catch (Exception ex) { report.warnings.Add("old manifest unreadable (" + ex.Message + "): rewritten"); }
            }
            for (int i = 0; i < CutOrder.Length; i++)
            {
                var w = works[CutOrder[i]];
                var cell = CellOf(i);
                var e = new CutEntry
                {
                    id = w.id, group = w.id[0] == 'B' ? "ball" : "frisbee", step = Steps[w.id], captionKey = CaptionKeys[w.id], source = w.source,
                    uvX = (cell.x + UvInsetPx) / AtlasW, uvY = (cell.y + UvInsetPx) / AtlasH,
                    uvW = (CutW - 2f * UvInsetPx) / AtlasW, uvH = (CutH - 2f * UvInsetPx) / AtlasH,
                    badgeBaked = BakeStepBadge && Steps[w.id] > 0,
                };
                var bc = Norm(BadgeCentre()); e.badgeX = R4(bc.x); e.badgeY = R4(bc.y); e.badgeR = R4(BadgeRadius / (CutW - 2f * UvInsetPx));
                e.uvX = R6(e.uvX); e.uvY = R6(e.uvY); e.uvW = R6(e.uvW); e.uvH = R6(e.uvH);
                foreach (var c in w.callouts)
                {
                    var ctr = Norm(c.rect.center); var size = NormSize(c.rect.size);
                    e.callouts.Add(new Callout { key = c.key, keySource = c.keySource, role = c.role, x = R4(ctr.x), y = R4(ctr.y), w = R4(size.x), h = R4(size.y), framed = c.framed, inInset = c.inInset });
                }
                foreach (var mk in w.marks)
                {
                    var ctr = Norm(mk.rect.center); var size = NormSize(mk.rect.size);
                    e.marks.Add(new MarkEntry { kind = mk.kind, x = R4(ctr.x), y = R4(ctr.y), w = R4(size.x), h = R4(size.y), note = mk.note });
                }
                m.cuts.Add(e);
            }
            var json = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(m, true) + "\n");
            bool manifestChanged = WriteIfChanged(ManifestPath, json);
            if (manifestChanged || AssetImporter.GetAtPath(ManifestPath) == null) AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            report.manifest = (manifestChanged ? "written " : "unchanged ") + ManifestPath;
        }

        static float R4(float v) { return (float)Math.Round(v, 4); }
        static float R6(float v) { return (float)Math.Round(v, 6); }

        /// <summary>Plan §4.4 (board column): sRGB, Read/Write off, mipmaps, Clamp, Bilinear, NPOT kept, no alpha; PC DXT1,
        /// Android / iOS ASTC 6x6, max 2048 everywhere. Only touches the importer when something differs.</summary>
        static string ConfigureImporter(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) throw new InvalidOperationException(path + " has no TextureImporter after import");
            var changes = new List<string>();
            void Set<T>(string name, T now, T want, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(now, want)) { apply(want); changes.Add(name); } }
            Set("textureType", ti.textureType, TextureImporterType.Default, v => ti.textureType = v);
            Set("sRGB", ti.sRGBTexture, true, v => ti.sRGBTexture = v);
            Set("readable", ti.isReadable, false, v => ti.isReadable = v);
            Set("mipmaps", ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set("wrap", ti.wrapMode, TextureWrapMode.Clamp, v => ti.wrapMode = v);
            Set("filter", ti.filterMode, FilterMode.Bilinear, v => ti.filterMode = v);
            Set("npot", ti.npotScale, TextureImporterNPOTScale.None, v => ti.npotScale = v);
            Set("alphaSource", ti.alphaSource, TextureImporterAlphaSource.None, v => ti.alphaSource = v);
            Set("alphaIsTransparency", ti.alphaIsTransparency, false, v => ti.alphaIsTransparency = v);
            Set("maxSize", ti.maxTextureSize, ImportMaxSize, v => ti.maxTextureSize = v);
            Set("compression", ti.textureCompression, TextureImporterCompression.Compressed, v => ti.textureCompression = v);
            foreach (var (platform, format) in new[] { ("Standalone", TextureImporterFormat.DXT1), ("Android", TextureImporterFormat.ASTC_6x6), ("iPhone", TextureImporterFormat.ASTC_6x6) })
            {
                var s = ti.GetPlatformTextureSettings(platform);
                bool diff = !s.overridden || s.format != format || s.maxTextureSize != ImportMaxSize;
                if (!diff) continue;
                s.overridden = true; s.format = format; s.maxTextureSize = ImportMaxSize;
                ti.SetPlatformTextureSettings(s);
                changes.Add(platform + " " + format);
            }
            if (changes.Count == 0) return "unchanged";
            ti.SaveAndReimport();
            return "updated (" + string.Join(", ", changes) + ")";
        }
    }
}
