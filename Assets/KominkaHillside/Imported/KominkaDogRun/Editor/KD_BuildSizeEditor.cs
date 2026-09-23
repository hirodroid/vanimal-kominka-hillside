// Build size measurement. Answers "how big is the world per platform" and "what is heavy".
//
// The bundle menus build the working scene as a real AssetBundle and report the file size.
// Validated 2026-09-10: the LZMA bundle for StandaloneWindows64 came out 105,678,078 bytes while the
// SDK's own .vrcw for the same scene was 105,689,146 bytes -- 0.01% apart, so this is a stand-in for
// the download size the SDK checks against the platform limit. It does not switch the active platform,
// so the SDK's mobile PostProcessing shader strip does not run here (measured worth: 0.01MB).
//
// Group Cost builds one bundle per asset group so each group's compressed cost can be read directly.
// Ref Graph walks the scene object by object -- an FBX holding 112 meshes only ships the meshes that
// are actually referenced, so per-file sizes overstate. Its texture/mesh numbers are estimates;
// the bundle menus are the measurements.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class KD_BuildSizeEditor
{
    const string ScenePath = KominkaDogRun.Editor.KD_Hillside.ScenePath /* 裏山改変: シーンの正本は KD_Hillside */;
    const string OutDir = "Temp/kd-buildsize";

    // VRChat mobile download limit, read from the SDK on 2026-09-10 (ValidationHelpers
    // .GetAssetBundleSizeLimit(World, mobile: true) = 104857600). PC reports -1 = no limit.
    const long MobileLimitBytes = 104857600L;

    // ---------------------------------------------------------------- bundle size

    [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Build Size/1 - Bundle Size PC")]
    public static void BundlePC() { Bundle(BuildTarget.StandaloneWindows64, "pc"); }

    [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Build Size/2 - Bundle Size Android")]
    public static void BundleAndroid() { Bundle(BuildTarget.Android, "android"); }

    [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Build Size/3 - Bundle Size iOS")]
    public static void BundleIOS() { Bundle(BuildTarget.iOS, "ios"); }

    static void Bundle(BuildTarget target, string tag)
    {
        if (target == BuildTarget.Android && EditorUserBuildSettings.androidBuildSubtarget != MobileTextureSubtarget.ASTC)
        {
            // The SDK forces ASTC for Android (EnvConfig.cs), so match it or the numbers are wrong.
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        }

        string dir = OutDir + "/bundle-" + tag;
        string dirRaw = OutDir + "/bundle-" + tag + "-raw";
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(dirRaw);

        var build = new AssetBundleBuild();
        build.assetBundleName = "kdscene";
        build.assetNames = new string[] { ScenePath };
        var builds = new AssetBundleBuild[] { build };

        var started = DateTime.Now;
        long compressed = -1;
        long raw = -1;
        string err = "";
        // A failed build leaves the previous bundle on disk and would be reported as this run's size
        // (measured 2026-09-18, IMP-ENV-019: a BuildFailedException in a scene callback printed the old 80.64 MB).
        File.Delete(Path.Combine(dir, "kdscene"));
        File.Delete(Path.Combine(dirRaw, "kdscene"));
        try
        {
            BuildPipeline.BuildAssetBundles(dir, builds, BuildAssetBundleOptions.None, target);
            compressed = Len(Path.Combine(dir, "kdscene"));
            BuildPipeline.BuildAssetBundles(dirRaw, builds, BuildAssetBundleOptions.UncompressedAssetBundle, target);
            raw = Len(Path.Combine(dirRaw, "kdscene"));
        }
        catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }

        string headroom = "n/a";
        if (compressed > 0 && (target == BuildTarget.Android || target == BuildTarget.iOS))
            headroom = Mb(MobileLimitBytes - compressed) + " left of the 100MB mobile limit";

        string line = tag + " target=" + target
            + " download=" + Mb(compressed) + " uncompressed=" + Mb(raw)
            + " headroom=" + headroom
            + " secs=" + (int)(DateTime.Now - started).TotalSeconds
            + (err.Length > 0 ? (" err=" + err) : "");
        Append("bundle-sizes.txt", line);
        Debug.LogError("[KD build size] " + line);
    }

    // ---------------------------------------------------------------- group cost

    class Group
    {
        public string name;
        public string prefix;
        public string kinds; // t=texture a=audio f=font m=meshAsset p=playable c=animClip s=shader x=modelFile
        // Unity refuses to place one asset in two bundles, so a broad row must skip the sub-folders
        // that have a row of their own.
        public string[] except;
        public List<string> paths = new List<string>();
    }

    [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Build Size/4 - Group Cost PC")]
    public static void GroupCostPC() { GroupCost(BuildTarget.StandaloneWindows64, "pc"); }

    [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Build Size/5 - Group Cost Android")]
    public static void GroupCostAndroid() { GroupCost(BuildTarget.Android, "android"); }

    static List<Group> Groups()
    {
        var g = new List<Group>();
        g.Add(new Group { name = "dog-textures", prefix = "Assets/TORINOSUKE/", kinds = "t" });
        g.Add(new Group { name = "dog-animclips", prefix = "Assets/TORINOSUKE/", kinds = "c" });
        g.Add(new Group { name = "dogpreview-meshes", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/DogPreviews/", kinds = "m" });
        g.Add(new Group { name = "kd-model-textures", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/", kinds = "t",
            except = new string[] { "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/", "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Toys/" } });
        // Accessories are the thing being scaled up, so they get their own rows: what does one more
        // item / one more set cost? (2026-09-10)
        g.Add(new Group { name = "accessory-meshes", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/", kinds = "x" });
        g.Add(new Group { name = "accessory-textures", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/", kinds = "t" });
        g.Add(new Group { name = "toy-meshes", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Toys/", kinds = "x" });
        g.Add(new Group { name = "toy-textures", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Toys/", kinds = "t" });
        g.Add(new Group { name = "kd-textures", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/", kinds = "t" });
        g.Add(new Group { name = "kd-animclips", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Animations/", kinds = "c" });
        g.Add(new Group { name = "kd-audio", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/", kinds = "a" });
        g.Add(new Group { name = "kd-fonts", prefix = "Assets/KominkaHillside/Font/", kinds = "tf" });
        g.Add(new Group { name = "kd-shaders", prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Shaders/", kinds = "s" });
        g.Add(new Group { name = "lightmaps", prefix = "Assets/Scenes/", kinds = "t" });
        g.Add(new Group { name = "kominka-textures", prefix = "Assets/モサンゴ屋/", kinds = "t" });
        g.Add(new Group { name = "garden-textures", prefix = "Assets/IKA 3DCG art studio/", kinds = "t" });
        g.Add(new Group { name = "sky-cubemap", prefix = "Assets/Allsky/", kinds = "t" });
        g.Add(new Group { name = "sleepkit", prefix = "Assets/RBS/SleepKit2/", kinds = "taf" });
        g.Add(new Group { name = "trees-textures", prefix = "Assets/LMArt-StylizedEnvironmentKit/", kinds = "t" });
        g.Add(new Group { name = "blanket-textures", prefix = "Assets/Blanket/", kinds = "t" });
        g.Add(new Group { name = "people-textures", prefix = "Assets/polyperfect/", kinds = "t" });
        // 2026-09-10: friends shop / favourite-creator posters.
        g.Add(new Group { name = "poster-art", prefix = "Assets/_Originals/", kinds = "t" });
        g.Add(new Group { name = "poster-board", prefix = "Assets/Atelier_Fuyutsuki/", kinds = "t" });
        g.Add(new Group { name = "show-timelines", prefix = "Assets/VirtualCampers.LiveGenerator/Shows/", kinds = "p" });
        g.Add(new Group { name = "show-audio", prefix = "Assets/VirtualCampers.LiveGenerator/Shows/", kinds = "a" });
        g.Add(new Group { name = "show-textures", prefix = "Packages/com.virtualcampers.live-generator/", kinds = "t" });
        g.Add(new Group { name = "radio-textures", prefix = "Packages/com.shijukai.hoboradio/", kinds = "t" });
        g.Add(new Group { name = "qvpen-textures", prefix = "Packages/net.ureishi.qvpen/", kinds = "t" });
        g.Add(new Group { name = "vvmw-textures", prefix = "Packages/idv.jlchntoz.vvmw/", kinds = "t" });
        g.Add(new Group { name = "tmp-default-fonts", prefix = "Assets/TextMesh Pro/", kinds = "tf" });
        g.Add(new Group { name = "shaders-postprocessing", prefix = "Packages/com.unity.postprocessing/", kinds = "s" });
        g.Add(new Group { name = "shaders-liltoon", prefix = "Packages/jp.lilxyzw.liltoon/", kinds = "s" });
        g.Add(new Group { name = "shaders-uzumore", prefix = "Packages/jp.sigmal00.uzumore-shader/", kinds = "s" });
        return g;
    }

    static void GroupCost(BuildTarget target, string tag)
    {
        var groups = Groups();
        string[] deps = AssetDatabase.GetDependencies(ScenePath, true);
        for (int i = 0; i < deps.Length; i++)
        {
            char kind = KindOf(deps[i]);
            if (kind == '?') continue;
            for (int g = 0; g < groups.Count; g++)
            {
                if (!deps[i].StartsWith(groups[g].prefix)) continue;
                if (groups[g].kinds.IndexOf(kind) < 0) continue;
                if (Excluded(groups[g], deps[i])) continue;
                groups[g].paths.Add(deps[i]);
            }
        }

        var builds = new List<AssetBundleBuild>();
        for (int g = 0; g < groups.Count; g++)
        {
            if (groups[g].paths.Count == 0) continue;
            var b = new AssetBundleBuild();
            b.assetBundleName = groups[g].name;
            b.assetNames = groups[g].paths.ToArray();
            builds.Add(b);
        }

        string dir = OutDir + "/groupcost-" + tag;
        Directory.CreateDirectory(dir);
        string err = "";
        try { BuildPipeline.BuildAssetBundles(dir, builds.ToArray(), BuildAssetBundleOptions.None, target); }
        catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }

        var sb = new StringBuilder();
        sb.AppendLine("# group cost " + tag + " (" + target + ")  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("one standalone LZMA bundle per group. A group carries any dependency not held by another group,");
        sb.AppendLine("and shader rows only carry the variants Unity keeps for a bundle with no material in it.");
        sb.AppendLine("err=" + err);
        sb.AppendLine();
        sb.AppendLine("| group | files | compressed |");
        sb.AppendLine("|---|---|---|");
        long total = 0;
        var rows = new List<KeyValuePair<long, string>>();
        for (int g = 0; g < groups.Count; g++)
        {
            if (groups[g].paths.Count == 0) continue;
            long len = Len(Path.Combine(dir, groups[g].name));
            if (len > 0) total += len;
            rows.Add(new KeyValuePair<long, string>(len, "| " + groups[g].name + " | " + groups[g].paths.Count + " | " + Mb(len) + " |"));
        }
        rows.Sort((a, b) => b.Key.CompareTo(a.Key));
        for (int i = 0; i < rows.Count; i++) sb.AppendLine(rows[i].Value);
        sb.AppendLine();
        sb.AppendLine("sum of measured groups: " + Mb(total));
        Write("group-cost-" + tag + ".md", sb.ToString());
        Debug.LogError("[KD build size] group cost " + tag + " sum=" + Mb(total) + " " + err);
    }

    static bool Excluded(Group g, string path)
    {
        if (g.except == null) return false;
        for (int i = 0; i < g.except.Length; i++) if (path.StartsWith(g.except[i])) return true;
        return false;
    }

    static char KindOf(string p)
    {
        string lower = p.ToLowerInvariant();
        if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".tga") || lower.EndsWith(".psd") || lower.EndsWith(".exr") || lower.EndsWith(".tif")) return 't';
        if (lower.EndsWith(".mp3") || lower.EndsWith(".wav") || lower.EndsWith(".ogg")) return 'a';
        if (lower.EndsWith(".ttf") || lower.EndsWith(".otf")) return 'f';
        if (lower.EndsWith(".playable")) return 'p';
        if (lower.EndsWith(".anim")) return 'c';
        if (lower.EndsWith(".shader") || lower.EndsWith(".compute")) return 's';
        if (lower.EndsWith(".fbx") || lower.EndsWith(".obj")) return 'x'; // whole model file: every mesh in it
        if (lower.EndsWith(".asset"))
        {
            var t = AssetDatabase.GetMainAssetTypeAtPath(p);
            if (t == typeof(Mesh)) return 'm';
            if (t != null && t.Name == "TMP_FontAsset") return 'f';
        }
        return '?';
    }

    // ---------------------------------------------------------------- ref graph

    class Node
    {
        public string path;
        public string type;
        public string name;
        public long pcBytes;
        public long mobileBytes;
        public string via;
        public string note;
    }

    [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Build Size/6 - Ref Graph")]
    public static void RefGraph()
    {
        var visited = new HashSet<int>();
        var queue = new Queue<UnityEngine.Object>();
        var viaOf = new Dictionary<int, string>();
        var assets = new List<Node>();
        var editorOnly = new List<string>();

        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++) Enqueue(queue, visited, viaOf, roots[i], "scene root");

        int guard = 0;
        while (queue.Count > 0 && guard < 400000)
        {
            guard++;
            var o = queue.Dequeue();
            if (o == null) continue;

            var go = o as GameObject;
            if (go != null)
            {
                var comps = go.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null) continue;
                    Enqueue(queue, visited, viaOf, comps[i], "go:" + go.name);
                }
                for (int i = 0; i < go.transform.childCount; i++)
                    Enqueue(queue, visited, viaOf, go.transform.GetChild(i).gameObject, "child of " + go.name);
                continue;
            }

            string path = AssetDatabase.GetAssetPath(o);
            if (!string.IsNullOrEmpty(path))
            {
                if (o is MonoScript) continue;
                if (o is Shader) { assets.Add(NodeFor(o, path, viaOf, 0, 0, "variants not counted here")); continue; }
                assets.Add(Measure(o, path, viaOf));
            }

            SerializedObject so = null;
            try { so = new SerializedObject(o); }
            catch { so = null; }
            if (so == null) continue;
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                var r = it.objectReferenceValue;
                if (r == null) continue;
                if (it.propertyPath.Contains("EditorRef") || it.propertyPath.Contains("m_EditorClassIdentifier"))
                {
                    string rp = AssetDatabase.GetAssetPath(r);
                    if (!string.IsNullOrEmpty(rp)) editorOnly.Add(o.GetType().Name + "." + it.propertyPath + " -> " + rp);
                    continue;
                }
                Enqueue(queue, visited, viaOf, r, o.GetType().Name + "." + it.propertyPath);
            }
        }

        long pcTotal = 0;
        long mobTotal = 0;
        var byType = new Dictionary<string, long[]>();
        var byFolder = new Dictionary<string, long[]>();
        for (int i = 0; i < assets.Count; i++)
        {
            pcTotal += assets[i].pcBytes;
            mobTotal += assets[i].mobileBytes;
            Add(byType, assets[i].type, assets[i].pcBytes, assets[i].mobileBytes);
            Add(byFolder, TopFolder(assets[i].path), assets[i].pcBytes, assets[i].mobileBytes);
        }

        var sb = new StringBuilder();
        sb.AppendLine("# referenced-object graph  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Estimates, not measurements: crunched textures are stored crunch-compressed in the bundle,");
        sb.AppendLine("so their rows read high, and animation clips read far higher than they cost. Use it to find");
        sb.AppendLine("what is referenced and why (the `via` column); use Group Cost for what it weighs.");
        sb.AppendLine("visited objects: " + visited.Count + "  referenced assets: " + assets.Count);
        sb.AppendLine("PC estimate: " + Mb(pcTotal) + "  mobile estimate (ASTC 6x6): " + Mb(mobTotal));
        sb.AppendLine();
        sb.AppendLine("## by type");
        sb.AppendLine("| type | PC | mobile | count |");
        sb.AppendLine("|---|---|---|---|");
        AppendTable(sb, byType);
        sb.AppendLine();
        sb.AppendLine("## by folder");
        sb.AppendLine("| folder | PC | mobile | count |");
        sb.AppendLine("|---|---|---|---|");
        AppendTable(sb, byFolder);
        sb.AppendLine();
        assets.Sort((a, b) => b.pcBytes.CompareTo(a.pcBytes));
        sb.AppendLine("## heaviest 100 referenced objects");
        sb.AppendLine("| PC | mobile | type | name | path | via | note |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        for (int i = 0; i < assets.Count && i < 100; i++)
        {
            var a = assets[i];
            sb.AppendLine("| " + Mb(a.pcBytes) + " | " + Mb(a.mobileBytes) + " | " + a.type + " | " + a.name + " | " + a.path + " | " + a.via + " | " + a.note + " |");
        }
        sb.AppendLine();
        sb.AppendLine("## editor-only refs (NOT in the bundle)");
        for (int i = 0; i < editorOnly.Count && i < 60; i++) sb.AppendLine("- " + editorOnly[i]);
        sb.AppendLine();
        sb.AppendLine("## every referenced object over 8KB (type, pc, mobile, name, path, via, note)");
        for (int i = 0; i < assets.Count; i++)
        {
            var a = assets[i];
            if (a.pcBytes < 8192) continue;
            sb.AppendLine(a.type + "\t" + a.pcBytes + "\t" + a.mobileBytes + "\t" + a.name + "\t" + a.path + "\t" + a.via + "\t" + a.note);
        }
        Write("ref-graph.md", sb.ToString());
        Debug.LogError("[KD build size] ref graph assets=" + assets.Count + " pc=" + Mb(pcTotal) + " mobile=" + Mb(mobTotal));
    }

    static void AppendTable(StringBuilder sb, Dictionary<string, long[]> d)
    {
        var keys = new List<string>(d.Keys);
        keys.Sort((a, b) => d[b][0].CompareTo(d[a][0]));
        for (int i = 0; i < keys.Count; i++)
        {
            if (d[keys[i]][0] < 65536) continue;
            sb.AppendLine("| " + keys[i] + " | " + Mb(d[keys[i]][0]) + " | " + Mb(d[keys[i]][1]) + " | " + d[keys[i]][2] + " |");
        }
    }

    static void Enqueue(Queue<UnityEngine.Object> q, HashSet<int> visited, Dictionary<int, string> viaOf, UnityEngine.Object o, string via)
    {
        if (o == null) return;
        int id = o.GetInstanceID();
        if (visited.Contains(id)) return;
        visited.Add(id);
        viaOf[id] = via;
        q.Enqueue(o);
    }

    static Node NodeFor(UnityEngine.Object o, string path, Dictionary<int, string> viaOf, long pc, long mob, string note)
    {
        string via = "";
        viaOf.TryGetValue(o.GetInstanceID(), out via);
        return new Node { path = path, type = o.GetType().Name, name = o.name, pcBytes = pc, mobileBytes = mob, via = via, note = note };
    }

    static Node Measure(UnityEngine.Object o, string path, Dictionary<int, string> viaOf)
    {
        var tex = o as Texture2D;
        if (tex != null)
        {
            long pc = TexBytes(tex.width, tex.height, 1, tex.format, tex.mipmapCount);
            long mob = MobileTexBytes(path, tex.width, tex.height, 1, tex.format, tex.mipmapCount);
            return NodeFor(o, path, viaOf, pc, mob, tex.width + "x" + tex.height + " " + tex.format + (tex.mipmapCount > 1 ? " mip" : " nomip"));
        }
        var cube = o as Cubemap;
        if (cube != null)
        {
            long pc = TexBytes(cube.width, cube.height, 6, cube.format, cube.mipmapCount);
            long mob = MobileTexBytes(path, cube.width, cube.height, 6, cube.format, cube.mipmapCount);
            return NodeFor(o, path, viaOf, pc, mob, "cubemap " + cube.width + " " + cube.format);
        }
        var mesh = o as Mesh;
        if (mesh != null)
        {
            long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh);
            int tris = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)(mesh.GetIndexCount(s) / 3);
            return NodeFor(o, path, viaOf, b, b, "verts=" + mesh.vertexCount + " tris=" + tris);
        }
        var clip = o as AudioClip;
        if (clip != null)
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) return NodeFor(o, path, viaOf, 0, 0, "no importer");
            var s = imp.defaultSampleSettings;
            long b;
            if (s.compressionFormat == AudioCompressionFormat.PCM) b = (long)(clip.length * 44100.0 * 2.0 * clip.channels);
            else if (s.compressionFormat == AudioCompressionFormat.ADPCM) b = (long)(clip.length * 44100.0 * 0.5 * clip.channels);
            else b = (long)(clip.length * (32000.0 + 160000.0 * s.quality) / 8.0);
            return NodeFor(o, path, viaOf, b, b, clip.length.ToString("F1", CultureInfo.InvariantCulture) + "s " + s.compressionFormat
                + " q=" + s.quality.ToString("F2", CultureInfo.InvariantCulture) + " load=" + s.loadType);
        }
        var anim = o as AnimationClip;
        if (anim != null)
        {
            long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(anim);
            return NodeFor(o, path, viaOf, b, b, anim.length.ToString("F1", CultureInfo.InvariantCulture) + "s runtimeMem");
        }
        if (o is Font) return NodeFor(o, path, viaOf, FileLen(path), FileLen(path), "source font file, shipped when a TMP asset keeps m_SourceFontFile");
        if (o.GetType().Name == "TMP_FontAsset") return NodeFor(o, path, viaOf, FileLen(path), FileLen(path), "asset file size (atlas counted separately)");
        if (o.GetType().Name == "TimelineAsset") return NodeFor(o, path, viaOf, FileLen(path) / 3, FileLen(path) / 3, "yaml/3 rough, file=" + Mb(FileLen(path)));
        if (o.GetType().Name == "TerrainData") return NodeFor(o, path, viaOf, FileLen(path), FileLen(path), "file size");
        if (o is Material) return NodeFor(o, path, viaOf, 4096, 4096, "");
        if (o is ScriptableObject)
        {
            if (!AssetDatabase.IsMainAsset(o)) return NodeFor(o, path, viaOf, 0, 0, "sub-asset, counted in its file");
            long b = FileLen(path);
            if (b > 4194304) b = b / 3;
            return NodeFor(o, path, viaOf, b, b, "scriptable file size rough");
        }
        return NodeFor(o, path, viaOf, 0, 0, "not sized");
    }

    static long TexBytes(int w, int h, int faces, TextureFormat f, int mips)
    {
        double mip = mips > 1 ? 1.3333 : 1.0;
        return (long)((double)w * h * faces * Bpp(f) * mip);
    }

    static long MobileTexBytes(string path, int w, int h, int faces, TextureFormat f, int mips)
    {
        double mip = mips > 1 ? 1.3333 : 1.0;
        double bpp = IsCompressedColor(f) ? (16.0 / 36.0) : Bpp(f); // ASTC 6x6
        double scale = 1.0;
        int max = Math.Max(w, h);
        int mobMax = 0;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            var a = imp.GetPlatformTextureSettings("Android");
            mobMax = (a != null && a.overridden) ? a.maxTextureSize : imp.maxTextureSize;
        }
        if (mobMax > 0 && mobMax < max)
        {
            double s = (double)mobMax / max;
            scale = s * s;
        }
        return (long)((double)w * h * faces * bpp * mip * scale);
    }

    static bool IsCompressedColor(TextureFormat f)
    {
        switch (f)
        {
            case TextureFormat.DXT1:
            case TextureFormat.DXT1Crunched:
            case TextureFormat.DXT5:
            case TextureFormat.DXT5Crunched:
            case TextureFormat.BC7:
            case TextureFormat.BC4:
            case TextureFormat.BC5:
                return true;
        }
        return false;
    }

    static double Bpp(TextureFormat f)
    {
        switch (f)
        {
            case TextureFormat.DXT1:
            case TextureFormat.DXT1Crunched:
            case TextureFormat.BC4:
            case TextureFormat.ETC_RGB4:
            case TextureFormat.ETC2_RGB:
            case TextureFormat.ASTC_8x8:
                return 0.5;
            case TextureFormat.DXT5:
            case TextureFormat.DXT5Crunched:
            case TextureFormat.BC5:
            case TextureFormat.BC6H:
            case TextureFormat.BC7:
            case TextureFormat.ETC2_RGBA8:
            case TextureFormat.ASTC_4x4:
                return 1.0;
            case TextureFormat.ASTC_5x5: return 16.0 / 25.0;
            case TextureFormat.ASTC_6x6: return 16.0 / 36.0;
            case TextureFormat.ASTC_10x10: return 16.0 / 100.0;
            case TextureFormat.ASTC_12x12: return 16.0 / 144.0;
            case TextureFormat.Alpha8:
            case TextureFormat.R8: return 1.0;
            case TextureFormat.R16:
            case TextureFormat.RHalf:
            case TextureFormat.RG16: return 2.0;
            case TextureFormat.RGB24: return 3.0;
            case TextureFormat.RGBA32:
            case TextureFormat.ARGB32:
            case TextureFormat.BGRA32:
            case TextureFormat.RGHalf:
            case TextureFormat.RGB9e5Float:
            case TextureFormat.RFloat: return 4.0;
            case TextureFormat.RGBAHalf: return 8.0;
            case TextureFormat.RGBAFloat: return 16.0;
        }
        return 4.0;
    }

    // ---------------------------------------------------------------- mobile texture caps

    // Android / iOS only texture size caps. PC keeps every texture at its authored size --
    // invariant 6 (mobile support must not cost PCVR quality) and DES-INF-001 ("express the
    // difference as an Import Override, not as a reduced variant").
    //
    // **This table is the source of truth, not the .meta files.** Every folder below is
    // git-ignored (purchased assets and Packages/), so the overrides are lost on a fresh clone
    // exactly like the BGM clips and DogPreviews meshes are. Recover with `7 - Apply Mobile
    // Texture Caps`, the same way BGM recovers with its Setup menu.
    //
    // A rule also forces compression on the mobile side: one purchased dog texture ships
    // uncompressed (samoedo DefaultMaterial.001_Base_Color 2.png, 2048 RGB24 = 16MB), and any
    // future one would go unnoticed the same way.
    struct Cap
    {
        public string prefix;
        public int maxSize;
        // Also compress on PC when the texture is uncompressed. Off by default: the mobile rule never
        // touches PC (invariant 6). Opted in only for our own Textures/ (2026-09-18 user decision:
        // six 2560x1440 RGBA32 photo frames = 118MB of VRAM for overlays; BC7 keeps the alpha edges).
        public bool pcCompress;
        public string why;
    }

    static List<Cap> Caps()
    {
        var c = new List<Cap>();
        c.Add(new Cap { prefix = "Assets/TORINOSUKE/", maxSize = 1024, why = "6 dogs, the star of the world -- 2048 stays on PC" });
        c.Add(new Cap { prefix = "Assets/モサンゴ屋/", maxSize = 1024, why = "kominka building and its props" });
        c.Add(new Cap { prefix = "Assets/IKA 3DCG art studio/", maxSize = 1024, why = "miniature garden" });
        c.Add(new Cap { prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/", maxSize = 1024, why = "our toys and accessories" });
        // 2026-09-15 user decision: plush toys carry Diffuse + Normal + AO at 1024 and cost 2.2MB each on
        // mobile (60x an accessory). 512 is enough for a toy in the mouth of a dog on a phone.
        c.Add(new Cap { prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Toys/", maxSize = 512, why = "toys: full PBR sets, 512 on a phone" });
        // 2026-09-18: our own Textures/ folder had no row, so six 2560x1440 RGBA32 photo frames went in
        // uncompressed -- 118MB of texture memory on a phone for six overlays. 2048 is a ceiling here;
        // the row mostly exists so the compression half of the rule covers the folder.
        c.Add(new Cap { prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/", maxSize = 2048, pcCompress = true, why = "our effect / UI textures; forces compression on every platform" });
        c.Add(new Cap { prefix = "Assets/LMArt-StylizedEnvironmentKit/", maxSize = 1024, why = "trees" });
        c.Add(new Cap { prefix = "Assets/RBS/", maxSize = 1024, why = "sleep kit" });
        c.Add(new Cap { prefix = "Assets/Blanket/", maxSize = 1024, why = "blanket" });
        // 2026-09-10 user decision: 256 is enough for the radio. It is one small prop that shipped
        // 14 textures at 2048 (7.48MB of the Android download).
        c.Add(new Cap { prefix = "Packages/com.shijukai.hoboradio/", maxSize = 256, why = "one small prop" });
        // 2026-09-10: posters keep their authored size -- people read the shop names on them -- so 2048
        // is a ceiling, not a downscale. The row is a guard for the next poster somebody drops in at
        // 4096. Measured cost of the current four: 2.84MB of the Android download.
        c.Add(new Cap { prefix = "Assets/_Originals/", maxSize = 2048, why = "posters stay readable; 2048 is a ceiling for the next one" });
        c.Add(new Cap { prefix = "Assets/Atelier_Fuyutsuki/", maxSize = 2048, why = "poster board" });
        return c;
    }

    // Android / iOS only audio quality. PC keeps the authored quality, same rule as the texture caps:
    // the mobile difference is an Import Override, never a second copy of the asset.
    // Music is the whole target -- it is already compressed, so every byte saved here is a byte off
    // the download, unlike textures which LZMA squeezes again. Short SE are left alone: they are small
    // and quality artefacts show up worst on transients.
    struct AudioCap
    {
        public string prefix;
        public float quality;
        // PC-side quality, applied to the default settings -- the one place this tool touches PC.
        // 0 = leave the authored value. Only used when the source extension matches pcExtension:
        // a full-band .wav master makes Vorbis spend ~2.3x the bits of an mp3-derived song at the
        // same q (measured 333 vs 148 kbps at q=1.00), so q=0.7 on a master still lands above the
        // old songs. Songs that were already heard at q=1.00 are left alone (2026-09-15 user decision:
        // "lowering later is bad; from the start is fine").
        public float pcQuality;
        public string pcExtension;
        public string why;
    }

    static List<AudioCap> AudioCaps()
    {
        var c = new List<AudioCap>();
        c.Add(new AudioCap { prefix = "Assets/VirtualCampers.LiveGenerator/Shows/", quality = 0.5f, pcQuality = 0.7f, pcExtension = ".wav", why = "live songs, the biggest single block of the download" });
        c.Add(new AudioCap { prefix = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/BGM/", quality = 0.5f, why = "background music" });
        return c;
    }

    static readonly string[] MobilePlatforms = new string[] { "Android", "iPhone" };

    [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Build Size/7 - Apply Mobile Caps")]
    public static void ApplyMobileCaps() { MobileCaps(true); }

    [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Build Size/8 - Verify Mobile Caps")]
    public static void VerifyMobileCaps() { MobileCaps(false); }

    static void MobileCaps(bool apply)
    {
        var caps = Caps();
        string[] deps = AssetDatabase.GetDependencies(ScenePath, true);
        var sb = new StringBuilder();
        sb.AppendLine("# mobile texture caps " + (apply ? "APPLY" : "VERIFY") + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("scene dependencies only. Re-run after adding content or re-importing a purchased asset.");
        sb.AppendLine();

        int inScope = 0;
        int changed = 0;
        int alreadyOk = 0;
        int missing = 0;
        var rows = new List<string>();

        for (int i = 0; i < deps.Length; i++)
        {
            string path = deps[i];
            if (KindOf(path) != 't') continue;
            int cap = 0;
            string why = "";
            // Longest matching prefix wins, so a sub-folder row (Models/Toys/) can tighten its parent (Models/).
            int matched = -1;
            bool pcCompress = false;
            for (int c = 0; c < caps.Count; c++)
            {
                if (!path.StartsWith(caps[c].prefix)) continue;
                if (matched >= 0 && caps[matched].prefix.Length > caps[c].prefix.Length) continue;
                matched = c;
                cap = caps[c].maxSize;
                why = caps[c].why;
                pcCompress = caps[c].pcCompress;
            }
            if (cap == 0) continue;

            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) continue;
            int w = 0;
            int h = 0;
            GetSourceSize(imp, ref w, ref h);
            // Two reasons to act: the texture is bigger than its cap, or it is uncompressed. The second
            // is the trap -- a single 2048 RGB24 costs 16MB and nothing about it looks unusual in the
            // Inspector (samoedo Base_Color 2, nocotto_pos).
            bool oversized = Math.Max(w, h) > cap;
            bool uncompressed = imp.textureCompression == TextureImporterCompression.Uncompressed;
            // Unity silently falls back to RGBA32 on every platform for a non-power-of-two texture that
            // has mipmaps, whatever the compression setting says (the six 2560x1440 photo frames sat at
            // 118MB with "Compressed (HQ)" in the Inspector). Our own overlay / UI textures do not need
            // mipmaps, so drop them and let the compression apply. Purchased assets are left alone -- a
            // poster losing its mipmaps would shimmer at distance on PC.
            bool npot = !Mathf.IsPowerOfTwo(w) || !Mathf.IsPowerOfTwo(h);
            bool mipBlocks = pcCompress && npot && imp.mipmapEnabled;
            if (!oversized && !uncompressed && !mipBlocks) continue;
            inScope++;

            // PC: the default settings, plus an explicit Standalone override if one exists (it would mask the default).
            var pcOverride = imp.GetPlatformTextureSettings("Standalone");
            bool pcUncompressed = uncompressed || (pcOverride.overridden && pcOverride.textureCompression == TextureImporterCompression.Uncompressed);
            bool pcNeeds = pcCompress && (pcUncompressed || mipBlocks);
            bool needs = pcNeeds || mipBlocks;
            for (int p = 0; p < MobilePlatforms.Length; p++)
            {
                var s = imp.GetPlatformTextureSettings(MobilePlatforms[p]);
                if (!s.overridden || s.maxTextureSize != cap || s.textureCompression == TextureImporterCompression.Uncompressed) needs = true;
            }
            if (!needs) { alreadyOk++; continue; }

            if (!apply)
            {
                missing++;
                rows.Add("MISSING cap=" + cap + " src=" + w + "x" + h + (uncompressed ? " UNCOMPRESSED" : "") + (mipBlocks ? " NPOT+mip" : "") + (pcNeeds ? " (PC too)" : "") + " " + path);
                continue;
            }

            if (mipBlocks) imp.mipmapEnabled = false;
            if (pcNeeds)
            {
                // CompressedHQ = BC7 for RGBA on Standalone; the size is left as authored (no PC cap).
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                if (pcOverride.overridden)
                {
                    pcOverride.textureCompression = TextureImporterCompression.CompressedHQ;
                    pcOverride.format = TextureImporterFormat.Automatic;
                    imp.SetPlatformTextureSettings(pcOverride);
                }
            }
            for (int p = 0; p < MobilePlatforms.Length; p++)
            {
                var s = imp.GetPlatformTextureSettings(MobilePlatforms[p]);
                s.overridden = true;
                s.maxTextureSize = cap;
                s.textureCompression = TextureImporterCompression.Compressed;
                s.format = TextureImporterFormat.Automatic;
                imp.SetPlatformTextureSettings(s);
            }
            imp.SaveAndReimport();
            changed++;
            rows.Add("SET cap=" + cap + " src=" + w + "x" + h + (uncompressed ? " UNCOMPRESSED->compressed" : "") + (mipBlocks ? " NPOT: mipmaps off" : "") + (pcNeeds ? " (PC BC7 too)" : "") + " " + path + "  (" + why + ")");
            EditorUtility.DisplayProgressBar("mobile texture caps", path, (float)i / deps.Length);
        }
        EditorUtility.ClearProgressBar();

        // ---- audio ----
        var audioCaps = AudioCaps();
        int audioInScope = 0;
        int audioChanged = 0;
        int audioOk = 0;
        int audioMissing = 0;
        for (int i = 0; i < deps.Length; i++)
        {
            string path = deps[i];
            if (KindOf(path) != 'a') continue;
            float cap = 0f;
            float pcCap = 0f;
            string why = "";
            for (int c = 0; c < audioCaps.Count; c++)
            {
                if (!path.StartsWith(audioCaps[c].prefix)) continue;
                cap = audioCaps[c].quality;
                why = audioCaps[c].why;
                pcCap = (audioCaps[c].pcQuality > 0f && !string.IsNullOrEmpty(audioCaps[c].pcExtension)
                    && path.ToLowerInvariant().EndsWith(audioCaps[c].pcExtension)) ? audioCaps[c].pcQuality : 0f;
            }
            if (cap <= 0f) continue;
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) continue;
            // The generator writes an explicit Standalone override, which masks the default settings on
            // PC -- so read and write both, or the PC value never moves (bitten 2026-09-15).
            float pcEffective = imp.ContainsSampleSettingsOverride("Standalone")
                ? imp.GetOverrideSampleSettings("Standalone").quality : imp.defaultSampleSettings.quality;
            bool pcNeeds = pcCap > 0f && pcEffective > pcCap + .001f;
            bool mobileInScope = imp.defaultSampleSettings.quality > cap + .001f; // PC lighter than the cap needs no override
            if (!pcNeeds && !mobileInScope) continue;
            audioInScope++;

            bool needs = pcNeeds;
            if (mobileInScope)
                for (int m = 0; m < MobilePlatforms.Length; m++)
                {
                    var o = imp.GetOverrideSampleSettings(MobilePlatforms[m]);
                    if (!imp.ContainsSampleSettingsOverride(MobilePlatforms[m]) || o.quality > cap + .001f) needs = true;
                }
            if (!needs) { audioOk++; continue; }
            string pcNote = " pc=" + pcEffective.ToString("F2", CultureInfo.InvariantCulture)
                + (pcNeeds ? ("->" + pcCap.ToString("F2", CultureInfo.InvariantCulture) + " (wav master)") : "");
            if (!apply)
            {
                audioMissing++;
                rows.Add("MISSING audio quality=" + cap.ToString("F2", CultureInfo.InvariantCulture) + pcNote + " " + path);
                continue;
            }
            if (pcNeeds)
            {
                var d = imp.defaultSampleSettings;
                d.quality = pcCap;
                d.compressionFormat = AudioCompressionFormat.Vorbis;
                imp.defaultSampleSettings = d;
                if (imp.ContainsSampleSettingsOverride("Standalone"))
                {
                    var so = imp.GetOverrideSampleSettings("Standalone");
                    so.quality = pcCap;
                    so.compressionFormat = AudioCompressionFormat.Vorbis;
                    imp.SetOverrideSampleSettings("Standalone", so);
                }
            }
            for (int m = 0; m < MobilePlatforms.Length; m++)
            {
                var o = imp.GetOverrideSampleSettings(MobilePlatforms[m]);
                o.quality = cap;
                o.compressionFormat = AudioCompressionFormat.Vorbis;
                imp.SetOverrideSampleSettings(MobilePlatforms[m], o);
            }
            imp.SaveAndReimport();
            audioChanged++;
            rows.Add("SET audio quality=" + cap.ToString("F2", CultureInfo.InvariantCulture) + pcNote + " " + path + "  (" + why + ")");
        }

        sb.AppendLine("textures over their cap: " + inScope);
        sb.AppendLine(apply ? ("changed: " + changed + "  already correct: " + alreadyOk)
                            : ("without the override: " + missing + "  already correct: " + alreadyOk));
        sb.AppendLine("music over its cap: " + audioInScope);
        sb.AppendLine(apply ? ("changed: " + audioChanged + "  already correct: " + audioOk)
                            : ("without the override: " + audioMissing + "  already correct: " + audioOk));
        sb.AppendLine();
        for (int i = 0; i < rows.Count; i++) sb.AppendLine(rows[i]);
        Write("mobile-texture-caps.md", sb.ToString());

        string head = "[KD build size] mobile caps " + (apply ? "applied" : "verified")
            + " textures=" + inScope + (apply ? (" changed=" + changed) : (" missing=" + missing)) + " ok=" + alreadyOk
            + " | music=" + audioInScope + (apply ? (" changed=" + audioChanged) : (" missing=" + audioMissing)) + " ok=" + audioOk;
        if (!apply && (missing > 0 || audioMissing > 0)) Debug.LogError(head + " -- run `7 - Apply Mobile Caps`");
        else Debug.LogError(head);
    }

    static void GetSourceSize(TextureImporter imp, ref int w, ref int h)
    {
        // GetSourceTextureWidthAndHeight is internal in 2022.3, so ask the imported texture instead.
        var t = AssetDatabase.LoadAssetAtPath<Texture>(imp.assetPath);
        if (t == null) return;
        w = t.width;
        h = t.height;
    }

    // ---------------------------------------------------------------- shared

    static void Add(Dictionary<string, long[]> d, string k, long pc, long mob)
    {
        if (!d.ContainsKey(k)) d[k] = new long[3];
        d[k][0] += pc;
        d[k][1] += mob;
        d[k][2] += 1;
    }

    static string TopFolder(string p)
    {
        if (string.IsNullOrEmpty(p)) return "(none)";
        string[] parts = p.Split('/');
        if (parts.Length >= 3) return parts[0] + "/" + parts[1] + "/" + parts[2];
        if (parts.Length >= 2) return parts[0] + "/" + parts[1];
        return p;
    }

    static long FileLen(string p)
    {
        try
        {
            var fi = new FileInfo(p);
            return fi.Exists ? fi.Length : 0;
        }
        catch { return 0; }
    }

    static long Len(string p)
    {
        long l = FileLen(p);
        return l > 0 ? l : -1;
    }

    static string Mb(long b)
    {
        if (b < 0) return "n/a";
        if (b >= 1048576) return (b / 1048576.0).ToString("F2", CultureInfo.InvariantCulture) + " MB";
        return (b / 1024.0).ToString("F0", CultureInfo.InvariantCulture) + " KB";
    }

    static void Write(string file, string text)
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), text);
    }

    static void Append(string file, string line)
    {
        Directory.CreateDirectory(OutDir);
        File.AppendAllText(Path.Combine(OutDir, file), line + Environment.NewLine);
    }
}
