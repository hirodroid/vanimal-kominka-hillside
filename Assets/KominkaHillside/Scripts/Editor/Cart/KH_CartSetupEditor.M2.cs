using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KominkaHillside.Editor
{
    /// <summary>DES-WLD-004 M2 の Editor (implementation.md「M2」): 道幅の分類と待避点、調整 JSON (Cart/4)、背面の掲示画像、犬・撮影カメラへの cartManager 配線。</summary>
    public static partial class KH_CartSetupEditor
    {
        // ============================================================
        // Editor 3: 調整 JSON
        // ============================================================

        public const string TuningPath = "Assets/KominkaHillside/Data/Cart/tuning.json";
        [Serializable] public class Tuning { public float speed = 1.3f, gap = 3f, lateral = .6f, approach = 25f, holdTimeoutSec = 60f, seatStep = .05f, yawStep = 15f, laneOffset = .55f, groundSmoothTime = .08f;
            public int defaultDestination = 2;   // KH_LeashManager.defaultDestination (2 = 山からの周回、2026-10-12 ユーザー決定)
        }
        /// <summary>Runtime の KH_CartManager の public float と同じ名前 (impl-runtime と合意 2026-10-12)。</summary>
        public static readonly string[] TuningFields = { "speed", "gap", "lateral", "approach", "holdTimeoutSec", "seatStep", "yawStep", "laneOffset", "groundSmoothTime" };

        public static Tuning LoadTuning()
        {
            if (!File.Exists(TuningPath)) throw new InvalidOperationException("Missing " + TuningPath);
            var t = JsonUtility.FromJson<Tuning>(File.ReadAllText(TuningPath));
            if (t.speed <= 0f || t.gap <= 0f || t.lateral < 0f || t.approach <= 0f || t.holdTimeoutSec <= 0f || t.seatStep <= 0f || t.yawStep <= 0f || t.laneOffset < 0f || t.groundSmoothTime < 0f)
                throw new InvalidOperationException(TuningPath + ": a value is out of range");
            if (t.defaultDestination < 0 || t.defaultDestination > 5) throw new InvalidOperationException(TuningPath + ": defaultDestination must be 0..5");
            return t;
        }

        public static float TuningValue(Tuning t, string field) { return (float)typeof(Tuning).GetField(field).GetValue(t); }

        /// <summary>Manager の proxy へ書く (呼び元が CopyProxyToUdon)。無いフィールドは WARN に回し、書けた数を返す。</summary>
        /// <summary>KH_LeashManager.defaultDestination (さんぽの目的地の既定) を tuning の値に (SerializedObject → CopyProxyToUdon)。</summary>
        static void WriteDefaultDestination(KH_LeashManager leash, Tuning t, List<string> log)
        {
            var so = new SerializedObject(leash); var p = so.FindProperty("defaultDestination");
            if (p == null || p.propertyType != SerializedPropertyType.Integer) { log.Add("WARN KH_LeashManager.defaultDestination not found"); return; }
            if (p.intValue != t.defaultDestination) log.Add("leash defaultDestination " + p.intValue + " -> " + t.defaultDestination);
            p.intValue = t.defaultDestination; so.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(leash); EditorUtility.SetDirty(leash);
        }

        static int WriteTuning(KH_CartManager manager, Tuning t, List<string> log)
        {
            var so = new SerializedObject(manager); int n = 0;
            foreach (var f in TuningFields)
            {
                var p = so.FindProperty(f);
                if (p == null || p.propertyType != SerializedPropertyType.Float) { log.Add("WARN tuning: KH_CartManager." + f + " not found (Runtime not merged?)"); continue; }
                p.floatValue = TuningValue(t, f); n++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return n;
        }

        [MenuItem("Tools/KominkaHillside/Cart/4 Apply Tuning")]
        public static void ApplyTuning()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var managers = UnityEngine.Object.FindObjectsOfType<KH_CartManager>(true);
            if (managers.Length != 1) throw new InvalidOperationException("Exactly one KH_CartManager required (run Cart/1)");
            var t = LoadTuning(); var log = new List<string>();
            int n = WriteTuning(managers[0], t, log);
            // gap が変わると待機点 (区間端から 3 m + gap×4) と区間の併合が変わる: Build と同じ関数で道幅の分類から書き直す (Codex M2 レビュー b)
            var route = KH_CourseTravelEditor.FindPouch().manager.route;
            var traffic = ClassifyTrack(BuildTrack(route), t.gap);
            WriteTraffic(managers[0], traffic);
            log.Add("traffic recomputed with gap " + t.gap + ": zones=" + traffic.from.Length + " class0=" + traffic.lengthByClass[0].ToString("F0") + "m class1=" + traffic.lengthByClass[1].ToString("F0") + "m class2=" + traffic.lengthByClass[2].ToString("F0") + "m");
            log.AddRange(traffic.zones);
            UdonSharpEditorUtility.CopyProxyToUdon(managers[0]); EditorUtility.SetDirty(managers[0]);
            WriteDefaultDestination(KH_CourseTravelEditor.FindPouch().manager, t, log);
            var scene = managers[0].gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cart tuning save failed");
            Debug.Log("[KH Cart] TUNING applied " + n + "/" + TuningFields.Length + " from " + TuningPath + " (" + JsonUtility.ToJson(t) + ")" + (log.Count > 0 ? "\n" + string.Join("\n", log) : ""));
        }

        // ============================================================
        // Editor 1: 道幅の分類と待避点
        // ============================================================

        public const float WidthStep = .25f, WidthMax = 4f, WidthSampleRadius = .3f, WidthAcceptHorizontal = .125f, WideMin = 3.4f, SingleMin = 2.2f, NarrowPad = 1.5f, WaitExtra = 3f, ConvoyCars = 4f;

        public class TrafficMap
        {
            public int[] cls; public float[] width;
            public float[] from, to, waitBefore, waitAfter;
            public float[] lengthByClass = new float[3];
            public List<string> zones = new List<string>();
        }

        /// <summary>片側の通行幅: 中点から right * side へ 0.25 m 刻みに NavMesh.SamplePosition (半径 0.3) を当て、当たり点が標本点から水平 0.125 m 以内なら通る。最初に通らない点の手前まで (最大 4 m)。</summary>
        static float SideWidth(Vector3 p, Vector3 dir)
        {
            float w = 0f;
            for (float s = WidthStep; s <= WidthMax + 1e-4f; s += WidthStep)
            {
                var q = p + dir * s;
                if (!NavMesh.SamplePosition(q, out NavMeshHit hit, WidthSampleRadius, NavMesh.AllAreas)) break;
                var d = hit.position - q; d.y = 0f;
                if (d.magnitude > WidthAcceptHorizontal) break;
                w = s;
            }
            return w;
        }

        public static TrafficMap ClassifyTrack(Track t, float gap)
        {
            int m = t.positions.Length; var map = new TrafficMap { cls = new int[m], width = new float[m] };
            for (int i = 0; i < m; i++)
            {
                float a = t.distances[i], b = i + 1 < m ? t.distances[i + 1] : t.length;
                var p = TrackPointRef(t, (a + b) * .5f); var f = TrackDirectionRef(t, (a + b) * .5f); var right = new Vector3(f.z, 0f, -f.x);
                bool centre = NavMesh.SamplePosition(p, out NavMeshHit ch, WidthSampleRadius, NavMesh.AllAreas);
                float w = centre ? SideWidth(p, right) + SideWidth(p, -right) : 0f;
                map.width[i] = w;
                map.cls[i] = !t.detour[i] || w < SingleMin ? 2 : w < WideMin ? 1 : 0;
                map.lengthByClass[map.cls[i]] += b - a;
            }
            // 連続する class 2 をまとめる (閉路の継ぎ目を跨ぐものは 1 つ)。全区間が class 2 なら区間は作らない
            var zones = new List<Vector2>();
            int startSeg = Array.FindIndex(map.cls, c => c != 2);
            if (startSeg >= 0)
            {
                int k = 0;   // startSeg は class 2 でないので、連続区間は startSeg から一周の中で途切れる
                while (k < m)
                {
                    int i = (startSeg + k) % m;
                    if (map.cls[i] != 2) { k++; continue; }
                    int kk = k;
                    while (kk < m && map.cls[(startSeg + kk) % m] == 2) kk++;
                    int j = (startSeg + kk - 1) % m;
                    float zFrom = t.distances[i] - NarrowPad, zTo = (j + 1 < m ? t.distances[j + 1] : t.length) + NarrowPad;
                    if (j < i) zTo += t.length;   // 継ぎ目を跨ぐ
                    zones.Add(new Vector2(zFrom, zTo));
                    k = kk;
                }
            }
            // 間が「3 m + gap×4」(待避に要る長さ) 未満の区間は 1 つに併合する (間で待避できないため。Codex M2 レビュー a)。待機点は併合後の両端から取る
            float back = WaitExtra + gap * ConvoyCars;
            zones = zones.OrderBy(z => z.x).ToList();
            var merged = new List<Vector2>();
            foreach (var z in zones) { if (merged.Count > 0 && z.x - merged[merged.Count - 1].y < back) merged[merged.Count - 1] = new Vector2(merged[merged.Count - 1].x, Mathf.Max(merged[merged.Count - 1].y, z.y)); else merged.Add(z); }
            // 継ぎ目の両側 (最後と最初) も同じ基準でまとめる
            if (merged.Count > 1 && merged[0].x + t.length - merged[merged.Count - 1].y < back) { merged[0] = new Vector2(merged[merged.Count - 1].x, merged[0].y + t.length); merged.RemoveAt(merged.Count - 1); }
            map.from = new float[merged.Count]; map.to = new float[merged.Count]; map.waitBefore = new float[merged.Count]; map.waitAfter = new float[merged.Count];
            for (int z = 0; z < merged.Count; z++) { map.from[z] = TrackWrap(t, merged[z].x); map.to[z] = TrackWrap(t, merged[z].y); }
            for (int z = 0; z < merged.Count; z++)
            {
                float zf = map.from[z], zt = map.to[z];
                map.waitBefore[z] = WaitPoint(t, map, zf, -1, back);   // 順方向 (+1) から入る側 = from の手前
                map.waitAfter[z] = WaitPoint(t, map, zt, +1, back);    // 逆方向 (-1) から入る側 = to の先
                map.zones.Add("zone " + z + ": " + zf.ToString("F1") + "-" + zt.ToString("F1") + " m (" + ZoneLength(t, zf, zt).ToString("F1") + " m) at " + TrackPointRef(t, zf).ToString("F0")
                    + " wait +1 at " + map.waitBefore[z].ToString("F1") + ", -1 at " + map.waitAfter[z].ToString("F1"));
            }
            return map;
        }

        public static float ZoneLength(Track t, float from, float to) { float l = to - from; return l < 0f ? l + t.length : l; }

        public static bool InZone(Track t, float from, float to, float d) { d = TrackWrap(t, d); return from <= to ? d >= from && d <= to : d >= from || d <= to; }

        /// <summary>待機点: 区間端 edge から step 方向 (-1 = 距離が減る向き) へ back m 進んだ点から、さらに同じ向きへ 0.5 m 刻みで、class ≤ 1 かつどの narrow 区間にも入らない最初の点。</summary>
        static float WaitPoint(Track t, TrafficMap map, float edge, int step, float back)
        {
            for (float extra = 0f; extra < t.length * .5f; extra += .5f)
            {
                float d = TrackWrap(t, edge + step * (back + extra));
                if (map.cls[TrackSegment(t, d)] > 1) continue;
                bool inside = false;
                for (int z = 0; z < map.from.Length; z++) if (InZone(t, map.from[z], map.to[z], d)) { inside = true; break; }
                if (!inside) return d;
            }
            return -1f;
        }

        static void WriteTraffic(KH_CartManager manager, TrafficMap map)
        {
            var so = new SerializedObject(manager);
            var c = ArrayProp(so, "trackClass", map.cls.Length); for (int i = 0; i < map.cls.Length; i++) c.GetArrayElementAtIndex(i).intValue = map.cls[i];
            SetFloats(so, "narrowFrom", map.from); SetFloats(so, "narrowTo", map.to);
            SetFloats(so, "waitBefore", map.waitBefore); SetFloats(so, "waitAfter", map.waitAfter);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ============================================================
        // Editor 2: 掲示画像 (Data/Cart/KH_CartSign.png)
        // ============================================================

        public const string SignSourcePath = "Assets/KominkaHillside/Data/WashPremium/Photos/Set4.png";   // 全部入りセット (9 道具)
        public const string SignOutPath = "Assets/KominkaHillside/Data/Cart/KH_CartSign.png";
        public const int SignWidth = 512, SignHeight = 256, SignBorder = 8;
        // 元画像 (1024²) の文字の無い帯 (上端基準): 題 (〜185 px) と説明 (850 px〜) を避け、道具の並び (x 140〜880, y 290〜530) を含む
        public const int SignCropX = 112, SignCropTop = 205, SignCropW = 800;
        static readonly Color SignFrame = new Color(.42f, .30f, .19f);

        /// <summary>背面の掲示: Set4 の文字の無い帯を 496×240 の枠内へ縮小し、木の枠 8 px を付けて PNG に書く。価格・文字は入れない。毎回作り直す (GUID は保つ)。</summary>
        public static Texture2D ComposeSign(List<string> log)
        {
            if (!File.Exists(SignSourcePath)) throw new InvalidOperationException("Missing " + SignSourcePath);
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!src.LoadImage(File.ReadAllBytes(SignSourcePath))) throw new InvalidOperationException("Cannot decode " + SignSourcePath);
                int innerW = SignWidth - SignBorder * 2, innerH = SignHeight - SignBorder * 2;
                float cropH = SignCropW * (float)innerH / innerW;
                float x0 = SignCropX, yTop = SignCropTop, yBottom = src.height - (yTop + cropH);   // Texture2D は下端基準
                var dst = new Texture2D(SignWidth, SignHeight, TextureFormat.RGBA32, false);
                var px = new Color[SignWidth * SignHeight];
                for (int y = 0; y < SignHeight; y++)
                    for (int x = 0; x < SignWidth; x++)
                    {
                        Color c;
                        if (x < SignBorder || y < SignBorder || x >= SignWidth - SignBorder || y >= SignHeight - SignBorder) c = SignFrame;
                        else
                        {
                            float u = (x0 + (x - SignBorder + .5f) / innerW * SignCropW) / src.width;
                            float v = (yBottom + (y - SignBorder + .5f) / innerH * cropH) / src.height;
                            c = src.GetPixelBilinear(u, v);
                        }
                        c.a = 1f; px[y * SignWidth + x] = c;
                    }
                dst.SetPixels(px); dst.Apply(false, false);
                Directory.CreateDirectory(Path.GetDirectoryName(SignOutPath));
                File.WriteAllBytes(SignOutPath, dst.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(dst);
            }
            finally { UnityEngine.Object.DestroyImmediate(src); }
            AssetDatabase.ImportAsset(SignOutPath, ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(SignOutPath) as TextureImporter;
            if (ti != null)
            {
                bool changed = false;
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; changed = true; }
                if (!ti.sRGBTexture) { ti.sRGBTexture = true; changed = true; }
                if (ti.isReadable) { ti.isReadable = false; changed = true; }
                if (ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; changed = true; }
                foreach (var platform in new[] { "Android", "iPhone" })
                {
                    var ps = ti.GetPlatformTextureSettings(platform);
                    if (!ps.overridden || ps.format != TextureImporterFormat.ASTC_6x6) { ps.overridden = true; ps.format = TextureImporterFormat.ASTC_6x6; ti.SetPlatformTextureSettings(ps); changed = true; }
                }
                if (changed) ti.SaveAndReimport();
            }
            log.Add("sign composed: " + SignOutPath + " " + SignWidth + "x" + SignHeight + " from " + Path.GetFileName(SignSourcePath) + " crop x" + SignCropX + " top" + SignCropTop + " w" + SignCropW + " (no text, no price)");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(SignOutPath);
        }

        // ============================================================
        // 犬・撮影カメラへの cartManager (Dog / Camera 担当がフィールドを足す)
        // ============================================================

        static void WireCartManagerRefs(KH_CartManager manager, List<string> log)
        {
            int dogs = 0, dogMissing = 0;
            foreach (var d in UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true))
            {
                if (!d.gameObject.scene.IsValid()) continue;
                var so = new SerializedObject(d); var p = so.FindProperty("cartManager");
                if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) { dogMissing++; continue; }
                p.objectReferenceValue = manager;
                if (p.objectReferenceValue != manager) { log.Add("WARN KD_DogWanderController.cartManager type mismatch on " + d.name); continue; }
                so.ApplyModifiedPropertiesWithoutUndo(); UdonSharpEditorUtility.CopyProxyToUdon(d); EditorUtility.SetDirty(d); dogs++;
            }
            log.Add("dogs: cartManager wired on " + dogs + " KD_DogWanderController" + (dogMissing > 0 ? "; WARN " + dogMissing + " lack the field (Dog not merged?)" : "") + (dogs + dogMissing != 6 ? "; WARN expected 6 dogs, found " + (dogs + dogMissing) : ""));
            var videos = UnityEngine.Object.FindObjectsOfType<KH_VideoCamera>(true);
            if (videos.Length != 1) { log.Add("WARN KH_VideoCamera count " + videos.Length + "; cartManager not wired"); return; }
            var vso = new SerializedObject(videos[0]); var vp = vso.FindProperty("cartManager");
            if (vp == null || vp.propertyType != SerializedPropertyType.ObjectReference) { log.Add("WARN KH_VideoCamera.cartManager not found (Camera not merged?)"); return; }
            vp.objectReferenceValue = manager; vso.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(videos[0]); EditorUtility.SetDirty(videos[0]);
            log.Add("video: KH_VideoCamera.cartManager wired");
        }

        /// <summary>proxy にフィールドがあれば書く (無ければ WARN)。M2 で Runtime が足す任意の参照用。</summary>
        static void SetRefIfPresent(SerializedObject so, string name, UnityEngine.Object value, List<string> log)
        {
            var p = so.FindProperty(name);
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) { log.Add("WARN " + so.targetObject.GetType().Name + "." + name + " not found (Runtime not merged?)"); return; }
            p.objectReferenceValue = value;
        }
    }
}
