using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace KominkaHillside.Editor
{
    /// <summary>DES-DOG-006 / IMP-DOG-025: 犬を洗う見た目の「位置マップ」(設計書 docs/plans/2026-09-30-dog-wash-design.md §1-1、§7 賭け 1)。
    ///
    /// 位置マップ = 胴体 (`karada`) の UV 空間の各テクセルに、その点の**バインドポーズでのメッシュ座標**を入れたテクスチャ。
    /// 実行時は、当たった点を部位の骨のローカルへ直し、ここで JSON に書く「骨ローカル → バインドポーズ」の行列を掛けて
    /// マスクの Blit へ渡す。成り立つには UV の重なり・ミラーが少ないことが要る (賭け 1)。
    ///
    /// - Wash/1 Survey: 開いているシーンの 6 匹の `karada` を調べて Temp/KH_DogWashSurvey.json へ。**シーンもアセットも変えない**
    /// - Wash/2 Bake: 犬種ごとに位置マップ (RGBAHalf 256²) と骨の行列の JSON を `Data/Wash/PositionMaps/` へ。**シーンは変えない**
    /// - Wash/3 Verify: 焼いたアセットを読み直して検査。失敗が 1 つでもあれば最後に例外
    ///
    /// 犬の Renderer は `KD_Dog_{key}` (KD_DogDefs の命名。シーン全体で 1 個) の子の SkinnedMeshRenderer を名前で引く。
    /// 2 段構成 (Corgi / Lab / Shiba) でも Renderer の `bones` = メッシュ側の骨なので、そのまま使う。
    /// メッシュが読めない時は Import 設定を書き換えず、その犬を失敗にする (購入アセットの設定は変えない)。</summary>
    public static partial class KH_DogWashBakeEditor
    {
        public const string OutDir = "Assets/KominkaHillside/Data/Wash/PositionMaps";
        const string SurveyPath = "Temp/KH_DogWashSurvey.json", BakePath = "Temp/KH_DogWashBake.json", VerifyPath = "Temp/KH_DogWashVerify.json";
        public const string BodyName = "karada", MouthName = "ha", EyeName = "me";
        /// <summary>重なったテクセルで、覆った三角形どうしのバインドポーズ座標がこれ (m) より離れていれば有害 (ミラー・使い回し)。
        /// これ以下は継ぎ目の重複 (害が小さい)。</summary>
        public const float HarmfulSpread = 0.02f;
        public const int DilateTexels = 4;
        public const float MinFilledFraction = 0.2f;

        public static string TexturePath(string key) { return OutDir + "/KH_WashPos_" + key + ".asset"; }
        public static string BonesPath(string key) { return OutDir + "/KH_WashPos_" + key + ".json"; }
        static string PreviewPath(string key) { return "Temp/KH_WashPos_" + key + "_preview.png"; }

        static void Guard()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("[KH Wash] Edit mode only (stop Play first)");
        }

        // ================================================================ JSON の形

        [Serializable] class MatInfo { public string name, shader, mainTexture; public List<string> furProperties = new List<string>(); }
        [Serializable] class BoneInfo { public string name; public int index, dominantVertices, harmfulOverlapTexels; }

        [Serializable] class DogSurvey
        {
            public string key, status, rendererPath, meshName, meshAssetPath, readVia, rootBonePath;
            public List<string> errors = new List<string>();
            public bool meshIsReadable, hasUv1;
            public int vertexCount, triangleCount, subMeshCount, boneCount, bindposeCount;
            public List<MatInfo> materials = new List<MatInfo>();
            public Vector3 lossyScale, localBoundsCenter, localBoundsSize;
            // バインドポーズ (頂点そのまま) の AABB。Raw = メッシュの単位、Meters = meshToWorldScale を掛けた値
            public Vector3 bindAabbMin, bindAabbMax, bindAabbSizeRaw, bindAabbSizeMeters;
            // 実寸への直し方: meshToWorldScale = 皮を張っている骨ごとの |bone.localToWorld × bindpose| の中央値 (骨のスケールが動かない前提)。
            // rendererMatrixScale = Renderer の localToWorldMatrix の軸の長さ (lossyScale 由来。比較用)。
            // 2 つが食い違う時は bindpose 時の Renderer の配置と今の配置が違う。有害判定は meshToWorldScale を使う
            public float meshToWorldScale, meshToWorldScaleMin, meshToWorldScaleMax, rendererMatrixScale;
            public List<BoneInfo> bones = new List<BoneInfo>();
            public Vector2 uv0Min, uv0Max;
            public int uv0OutsideVertices, uvNegativeWindingTriangles, uvDegenerateTriangles;
            public int coveredTexels, overlapTexels, harmfulOverlapTexels;
            public float coveredFraction, maxOverlapSpreadRaw, maxOverlapSpreadMeters, harmfulThresholdRaw;
            public List<string> harmfulTopBones = new List<string>();
            public string karadaMainTexture, haRendererPath, haMainTexture, meRendererPath, meMainTexture;
            public bool haMeShareTexture;
        }
        [Serializable] class SurveyReport
        {
            public string utc, scene, status;
            public int failed;
            public List<string> notes = new List<string>();
            public List<DogSurvey> dogs = new List<DogSurvey>();
        }

        /// <summary>Wash/2 が位置マップの横に書く JSON (TextAsset)。ビルダーが Udon の配列へ配線する時に読む。</summary>
        [Serializable] public class BoneEntry { public string name; public int index; public float[] boneToMesh; }
        [Serializable] public class PosMapBones
        {
            public string key, meshName, meshAssetPath, rendererPath, rootBone;
            public string matrixLayout = "boneToMesh = inverse(Mesh.bindposes[index]) (bone local -> mesh bind-pose space), row-major m00 m01 m02 m03 m10 ... m33";
            public string alphaLayout = "texture A = bones index + 1 of the max-weight bone (0 = no surface); RGB = bind-pose mesh position, raw mesh units";
            public int size, boneCount;
            public float meshToWorldScale;
            public Vector3 aabbMin, aabbMax;
            public List<BoneEntry> bones = new List<BoneEntry>();
        }

        [Serializable] class BakeDog
        {
            public string key, status, texturePath, bonesPath, previewPath, readVia;
            public List<string> errors = new List<string>();
            public int validTexels, dilatedTexels, overlapTexels, noBoneTexels;
            public float validFraction, filledFraction, maxAbsCoordinate;
        }
        [Serializable] class BakeReport
        {
            public string utc, scene, status;
            public int failed;
            public List<BakeDog> dogs = new List<BakeDog>();
        }

        [Serializable] class VerifyReport
        {
            public string utc, scene, status;
            public int failed;
            public List<string> lines = new List<string>();
        }

        // ================================================================ 犬の解決

        static GameObject FindDog(KD_DogDef d, List<string> errors)
        {
            var go = KD_SceneObjects.Find(d.DogName, out int count);
            if (go == null || count != 1) { errors.Add(d.DogName + " found " + count + " times in the active scene (expected 1)"); return null; }
            return go;
        }

        static SkinnedMeshRenderer FindRenderer(GameObject dog, string name, List<string> errors, bool required)
        {
            var found = dog.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name == name).ToArray();
            if (found.Length == 1) return found[0];
            if (required || found.Length > 1) errors.Add(name + ": " + found.Length + " SkinnedMeshRenderer under " + dog.name + " (expected 1)");
            return null;
        }

        static string MainTexPath(Material m)
        {
            if (m == null || !m.HasProperty("_MainTex")) return "";
            var t = m.GetTexture("_MainTex");
            return t == null ? "" : AssetDatabase.GetAssetPath(t);
        }

        /// <summary>毛の長さに関わりそうなプロパティ (lilToon の `_FurVectorScale` / `_FurVector` など、名前に Fur を含む数値とベクトル)。存在するものだけ。</summary>
        static List<string> FurProps(Material m)
        {
            var list = new List<string>();
            if (m == null || m.shader == null) return list;
            var sh = m.shader;
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                string n = sh.GetPropertyName(i);
                if (n.IndexOf("Fur", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!(n.Contains("Length") || n.Contains("Scale") || n.Contains("Vector") || n.Contains("Offset") || n.Contains("Layer"))) continue;
                var type = sh.GetPropertyType(i);
                if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range) list.Add(n + "=" + m.GetFloat(n).ToString("R"));
                else if (type == ShaderPropertyType.Vector) list.Add(n + "=" + m.GetVector(n).ToString("F5"));
            }
            return list;
        }

        // ================================================================ 1 - Survey

        [MenuItem("Tools/KominkaHillside/Wash/1 - Survey Dog Meshes")]
        public static void Survey()
        {
            Guard();
            var r = new SurveyReport { utc = DateTime.UtcNow.ToString("o"), scene = EditorSceneManager.GetActiveScene().path };
            if (r.scene != KH_ProductionScene.Path) r.notes.Add("active scene is not the production scene " + KH_ProductionScene.Path);
            r.notes.Add("read only: no scene or asset was modified");
            r.notes.Add("overlap: texel centers of a " + Size + "^2 grid inside UV0 triangles (edges inclusive, UV outside 0..1 dropped). harmful = covering triangles' bind-pose positions differ by more than " + HarmfulSpread + " m (meshToWorldScale applied)");
            foreach (var d in KD_DogDefs.All)
            {
                DogSurvey s;
                try { s = SurveyDog(d); }
                catch (Exception e) { s = new DogSurvey { key = d.key }; s.errors.Add("exception: " + e.Message); }
                s.status = s.errors.Count == 0 ? "OK" : "FAIL";
                if (s.errors.Count > 0) r.failed++;
                r.dogs.Add(s);
                Debug.Log("[KH Wash Survey] " + d.key + " " + s.status + " covered=" + s.coveredTexels + " overlap=" + s.overlapTexels + " harmful=" + s.harmfulOverlapTexels + " scale=" + s.meshToWorldScale.ToString("R") + (s.errors.Count > 0 ? " errors=" + string.Join("; ", s.errors) : ""));
            }
            r.status = r.failed == 0 ? "OK" : "FAIL";
            Directory.CreateDirectory("Temp");
            File.WriteAllText(SurveyPath, JsonUtility.ToJson(r, true));
            if (r.failed > 0) throw new InvalidOperationException("[KH Wash Survey] " + r.failed + " dog(s) failed, see " + SurveyPath);
            Debug.Log("[KH Wash Survey] OK -> " + SurveyPath);
        }

        static DogSurvey SurveyDog(KD_DogDef d)
        {
            var s = new DogSurvey { key = d.key };
            var dog = FindDog(d, s.errors); if (dog == null) return s;
            var smr = FindRenderer(dog, BodyName, s.errors, true); if (smr == null) return s;
            s.rendererPath = KD_SceneObjects.PathOf(smr.gameObject);
            s.lossyScale = smr.transform.lossyScale;
            s.localBoundsCenter = smr.localBounds.center; s.localBoundsSize = smr.localBounds.size;
            s.rootBonePath = smr.rootBone != null ? KD_SceneObjects.PathOf(smr.rootBone.gameObject) : "(null)";
            s.rendererMatrixScale = AxisScale(smr.transform.localToWorldMatrix);
            foreach (var m in smr.sharedMaterials)
                s.materials.Add(new MatInfo { name = m != null ? m.name : "(null)", shader = m != null && m.shader != null ? m.shader.name : "(null)", mainTexture = MainTexPath(m), furProperties = FurProps(m) });
            s.karadaMainTexture = s.materials.Count > 0 ? s.materials[0].mainTexture : "";

            // 口と目: 同じテクスチャを使っているか (殻を足す時に口・目を巻き込まないかの判断材料)
            var ha = FindRenderer(dog, MouthName, s.errors, false); var me = FindRenderer(dog, EyeName, s.errors, false);
            s.haRendererPath = ha != null ? KD_SceneObjects.PathOf(ha.gameObject) : "(missing)";
            s.meRendererPath = me != null ? KD_SceneObjects.PathOf(me.gameObject) : "(missing)";
            s.haMainTexture = ha != null ? MainTexPath(ha.sharedMaterial) : "";
            s.meMainTexture = me != null ? MainTexPath(me.sharedMaterial) : "";
            s.haMeShareTexture = s.haMainTexture != "" && s.haMainTexture == s.meMainTexture;

            var mesh = smr.sharedMesh;
            if (mesh == null) { s.errors.Add("sharedMesh is null"); return s; }
            s.meshName = mesh.name; s.meshAssetPath = AssetDatabase.GetAssetPath(mesh); s.meshIsReadable = mesh.isReadable;
            s.vertexCount = mesh.vertexCount; s.subMeshCount = mesh.subMeshCount;
            s.boneCount = smr.bones.Length;
            var g = ReadGeo(mesh, s.errors); if (g == null) return s;
            s.readVia = g.readVia; s.bindposeCount = g.bindposes.Length; s.triangleCount = g.tri.Length / 3; s.hasUv1 = g.hasUv1;
            if (s.boneCount != s.bindposeCount) s.errors.Add("renderer bones " + s.boneCount + " != mesh bindposes " + s.bindposeCount);

            // バインドポーズの AABB と実寸
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (var v in g.v) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            s.bindAabbMin = mn; s.bindAabbMax = mx; s.bindAabbSizeRaw = mx - mn;
            s.meshToWorldScale = MeshToWorldScale(smr, g, out s.meshToWorldScaleMin, out s.meshToWorldScaleMax);
            s.bindAabbSizeMeters = s.bindAabbSizeRaw * s.meshToWorldScale;

            // 骨
            var bones = smr.bones; var dom = new int[Mathf.Max(bones.Length, g.bindposes.Length)];
            foreach (int b in g.domBone) if (b >= 0 && b < dom.Length) dom[b]++;

            if (g.uv == null) { s.errors.Add("mesh has no UV0"); return s; }
            Vector2 umn = Vector2.positiveInfinity, umx = Vector2.negativeInfinity;
            foreach (var u in g.uv)
            {
                umn = Vector2.Min(umn, u); umx = Vector2.Max(umx, u);
                if (u.x < 0f || u.x > 1f || u.y < 0f || u.y > 1f) s.uv0OutsideVertices++;
            }
            s.uv0Min = umn; s.uv0Max = umx;

            // UV の重なり
            var cells = Rasterize(g, out s.uvNegativeWindingTriangles, out s.uvDegenerateTriangles);
            float scale = s.meshToWorldScale > 0f ? s.meshToWorldScale : 1f;
            s.harmfulThresholdRaw = HarmfulSpread / scale;
            var harmByBone = new int[dom.Length];
            var seen = new List<int>(4);
            foreach (var h in cells)
            {
                if (h == null) continue;
                s.coveredTexels++;
                if (h.Count < 2) continue;
                s.overlapTexels++;
                float spread = Spread(h);
                s.maxOverlapSpreadRaw = Mathf.Max(s.maxOverlapSpreadRaw, spread);
                if (spread <= s.harmfulThresholdRaw) continue;
                s.harmfulOverlapTexels++;
                seen.Clear();
                foreach (var hit in h)
                    if (hit.bone >= 0 && hit.bone < harmByBone.Length && !seen.Contains(hit.bone)) { seen.Add(hit.bone); harmByBone[hit.bone]++; }
            }
            s.coveredFraction = s.coveredTexels / (float)(Size * Size);
            s.maxOverlapSpreadMeters = s.maxOverlapSpreadRaw * scale;
            for (int i = 0; i < dom.Length; i++)
                s.bones.Add(new BoneInfo { index = i, name = i < bones.Length && bones[i] != null ? bones[i].name : "(null)", dominantVertices = dom[i], harmfulOverlapTexels = harmByBone[i] });
            s.harmfulTopBones = s.bones.Where(b => b.harmfulOverlapTexels > 0).OrderByDescending(b => b.harmfulOverlapTexels).Take(5)
                .Select(b => b.index + ":" + b.name + "=" + b.harmfulOverlapTexels).ToList();
            return s;
        }

        // ================================================================ 2 - Bake

        [MenuItem("Tools/KominkaHillside/Wash/2 - Bake Position Maps")]
        public static void Bake()
        {
            Guard();
            var r = new BakeReport { utc = DateTime.UtcNow.ToString("o"), scene = EditorSceneManager.GetActiveScene().path };
            EnsureFolder(OutDir);
            foreach (var d in KD_DogDefs.All)
            {
                var b = new BakeDog { key = d.key, texturePath = TexturePath(d.key), bonesPath = BonesPath(d.key), previewPath = PreviewPath(d.key) };
                try { BakeDogMap(d, b); }
                catch (Exception e) { b.errors.Add("exception: " + e.Message); }
                b.status = b.errors.Count == 0 ? "BAKED" : "FAIL";
                if (b.errors.Count > 0) r.failed++;
                r.dogs.Add(b);
                Debug.Log("[KH Wash Bake] " + d.key + " " + b.status + " valid=" + b.validFraction.ToString("F3") + " filled=" + b.filledFraction.ToString("F3") + " dilated=" + b.dilatedTexels + (b.errors.Count > 0 ? " errors=" + string.Join("; ", b.errors) : ""));
            }
            AssetDatabase.SaveAssets();
            r.status = r.failed == 0 ? "BAKED" : "FAIL";
            Directory.CreateDirectory("Temp");
            File.WriteAllText(BakePath, JsonUtility.ToJson(r, true));
            if (r.failed > 0) throw new InvalidOperationException("[KH Wash Bake] " + r.failed + " dog(s) failed, see " + BakePath);
            Debug.Log("[KH Wash Bake] BAKED -> " + OutDir + " (summary " + BakePath + ")");
        }

        static void BakeDogMap(KD_DogDef d, BakeDog b)
        {
            var dog = FindDog(d, b.errors); if (dog == null) return;
            var smr = FindRenderer(dog, BodyName, b.errors, true); if (smr == null) return;
            var mesh = smr.sharedMesh;
            if (mesh == null) { b.errors.Add("sharedMesh is null"); return; }
            var g = ReadGeo(mesh, b.errors); if (g == null) return;
            b.readVia = g.readVia;
            if (g.uv == null) { b.errors.Add("mesh has no UV0"); return; }
            var bones = smr.bones;
            if (bones.Length != g.bindposes.Length) { b.errors.Add("renderer bones " + bones.Length + " != mesh bindposes " + g.bindposes.Length); return; }

            // 1. 描く。重なったテクセルは UV 面積の大きい三角形を優先 (先着でも後着でもなく、テクセル密度の高い側を残す)
            int n = Size * Size;
            var cells = Rasterize(g, out _, out _);
            var px = new Vector4[n]; var filled = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var h = cells[i]; if (h == null) continue;
                var best = h[0];
                for (int k = 1; k < h.Count; k++) if (h[k].uvArea > best.uvArea) best = h[k];
                if (h.Count > 1) b.overlapTexels++;
                if (best.bone < 0) { b.noBoneTexels++; continue; }   // 重みの無い面は位置があっても実行時に骨から戻せないので無効
                px[i] = new Vector4(best.p.x, best.p.y, best.p.z, best.bone + 1);
                filled[i] = true; b.validTexels++;
            }
            if (b.noBoneTexels > 0) b.errors.Add(b.noBoneTexels + " covered texels have no bone weight");

            // 2. 継ぎ目の広げ
            b.dilatedTexels = Dilate(px, filled, DilateTexels);
            b.validFraction = b.validTexels / (float)n;
            b.filledFraction = (b.validTexels + b.dilatedTexels) / (float)n;

            // 3. RGBAHalf で保存 (生の値。SetPixelData でビットを決めて書く)
            var raw = new ushort[n * 4];
            for (int i = 0; i < n; i++)
            {
                var p = px[i];
                b.maxAbsCoordinate = Mathf.Max(b.maxAbsCoordinate, Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z));
                raw[i * 4] = Mathf.FloatToHalf(p.x); raw[i * 4 + 1] = Mathf.FloatToHalf(p.y);
                raw[i * 4 + 2] = Mathf.FloatToHalf(p.z); raw[i * 4 + 3] = Mathf.FloatToHalf(p.w);
            }
            if (b.maxAbsCoordinate > 60000f) b.errors.Add("coordinate " + b.maxAbsCoordinate + " exceeds the half range");
            if (g.bindposes.Length + 1 > 2048) b.errors.Add("bone index + 1 is not exact in half above 2048");
            SaveTexture(b.texturePath, "KH_WashPos_" + d.key, raw);

            // 4. 骨の行列の JSON (TextAsset)
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (var v in g.v) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            var j = new PosMapBones
            {
                key = d.key, meshName = mesh.name, meshAssetPath = AssetDatabase.GetAssetPath(mesh), rendererPath = KD_SceneObjects.PathOf(smr.gameObject),
                rootBone = smr.rootBone != null ? smr.rootBone.name : "", size = Size, boneCount = bones.Length, aabbMin = mn, aabbMax = mx,
                meshToWorldScale = MeshToWorldScale(smr, g, out _, out _),
            };
            for (int i = 0; i < bones.Length; i++)
            {
                var inv = g.bindposes[i].inverse; var m16 = new float[16];
                for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) m16[row * 4 + col] = inv[row, col];
                j.bones.Add(new BoneEntry { name = bones[i] != null ? bones[i].name : "(null)", index = i, boneToMesh = m16 });
            }
            File.WriteAllText(b.bonesPath, JsonUtility.ToJson(j, true));
            AssetDatabase.ImportAsset(b.bonesPath);

            // 5. 確認用 PNG (座標を AABB で 0〜1 に。無効は黒)
            var prev = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            try
            {
                var cols = new Color32[n]; var ext = mx - mn;
                for (int i = 0; i < n; i++)
                {
                    if (!filled[i]) { cols[i] = new Color32(0, 0, 0, 255); continue; }
                    var p = px[i];
                    cols[i] = new Color(ext.x > 0 ? (p.x - mn.x) / ext.x : 0, ext.y > 0 ? (p.y - mn.y) / ext.y : 0, ext.z > 0 ? (p.z - mn.z) / ext.z : 0, 1f);
                }
                prev.SetPixels32(cols); prev.Apply(false);
                Directory.CreateDirectory("Temp");
                File.WriteAllBytes(b.previewPath, prev.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(prev); }
        }

        /// <summary>有効テクセルの外側へ passes テクセル広げる。1 回ごとに、前の回で埋まった 4 近傍 → 斜めの順で最初に見つかった値を写す
        /// (最寄りの近似。A も写す)。広げた数を返す。</summary>
        static int Dilate(Vector4[] px, bool[] filled, int passes)
        {
            int added = 0;
            var dx = new[] { 1, -1, 0, 0, 1, -1, 1, -1 }; var dy = new[] { 0, 0, 1, -1, 1, 1, -1, -1 };
            for (int pass = 0; pass < passes; pass++)
            {
                var prev = (bool[])filled.Clone();
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        int i = y * Size + x; if (prev[i]) continue;
                        for (int k = 0; k < 8; k++)
                        {
                            int nx = x + dx[k], ny = y + dy[k];
                            if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                            int ni = ny * Size + nx; if (!prev[ni]) continue;
                            px[i] = px[ni]; filled[i] = true; added++; break;
                        }
                    }
            }
            return added;
        }

        /// <summary>既存のアセットがあれば同じオブジェクトへ書き直す (GUID を保つ)。RGBAHalf は常に linear (sRGB の無い形式)。
        /// isReadable は true のまま (Verify が読み直すため)。</summary>
        static void SaveTexture(string path, string name, ushort[] raw)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool create = tex == null;
            if (create) tex = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            else if (tex.width != Size || tex.height != Size || tex.format != TextureFormat.RGBAHalf || tex.mipmapCount != 1)
                tex.Reinitialize(Size, Size, TextureFormat.RGBAHalf, false);
            tex.name = name; tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear; tex.anisoLevel = 0;
            tex.SetPixelData(raw, 0); tex.Apply(false, false);
            if (create) AssetDatabase.CreateAsset(tex, path);
            else EditorUtility.SetDirty(tex);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int cut = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, cut));
            AssetDatabase.CreateFolder(path.Substring(0, cut), path.Substring(cut + 1));
        }

        // ================================================================ 3 - Verify

        [MenuItem("Tools/KominkaHillside/Wash/3 - Verify Position Maps")]
        public static void Verify()
        {
            Guard();
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = EditorSceneManager.GetActiveScene().path };
            Action<bool, string> check = (ok, label) => { r.lines.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) r.failed++; };
            foreach (var d in KD_DogDefs.All)
            {
                string k = d.key;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(k));
                check(tex != null, k + " texture exists " + TexturePath(k));
                var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(BonesPath(k));
                check(ta != null, k + " bones json exists " + BonesPath(k));
                PosMapBones j = null;
                if (ta != null)
                {
                    try { j = JsonUtility.FromJson<PosMapBones>(ta.text); } catch (Exception e) { check(false, k + " bones json parses: " + e.Message); }
                    check(j != null && j.bones != null && j.bones.Count == j.boneCount && j.bones.All(x => x.boneToMesh != null && x.boneToMesh.Length == 16 && x.boneToMesh.All(f => !float.IsNaN(f) && !float.IsInfinity(f))), k + " bones json: " + (j != null ? j.boneCount : -1) + " bones with 16 finite values");
                }
                // メッシュの bones と一致 (開いているシーンの犬の Renderer)
                var errs = new List<string>();
                var dog = FindDog(d, errs); var smr = dog != null ? FindRenderer(dog, BodyName, errs, true) : null;
                check(smr != null, k + " body renderer in the active scene" + (errs.Count > 0 ? " (" + string.Join("; ", errs) + ")" : ""));
                int boneCount = smr != null ? smr.bones.Length : -1;
                if (smr != null && j != null)
                {
                    check(j.boneCount == boneCount && smr.sharedMesh != null && smr.sharedMesh.bindposes.Length == boneCount, k + " json bones " + j.boneCount + " == renderer bones " + boneCount + " == mesh bindposes");
                    bool names = j.bones.Count == boneCount;
                    for (int i = 0; names && i < boneCount; i++) names = smr.bones[i] != null && j.bones[i].name == smr.bones[i].name && j.bones[i].index == i;
                    check(names, k + " json bone names/order match the renderer");
                }
                if (tex == null) continue;
                check(tex.width == Size && tex.height == Size, k + " size " + tex.width + "x" + tex.height);
                check(tex.format == TextureFormat.RGBAHalf, k + " format " + tex.format);
                check(tex.mipmapCount == 1, k + " no mipmaps (" + tex.mipmapCount + ")");
                check(tex.wrapMode == TextureWrapMode.Clamp, k + " wrap " + tex.wrapMode);
                check(tex.filterMode == FilterMode.Bilinear, k + " filter " + tex.filterMode);
                check(!GraphicsFormatUtility.IsSRGBFormat(tex.graphicsFormat), k + " linear (" + tex.graphicsFormat + ")");
                check(tex.isReadable, k + " readable for verification");
                if (!tex.isReadable || tex.width != Size || tex.height != Size || tex.format != TextureFormat.RGBAHalf) continue;
                var data = tex.GetPixelData<ushort>(0);
                int nan = 0, filledCount = 0, badA = 0, n = Size * Size;
                for (int i = 0; i < n; i++)
                {
                    float x = Mathf.HalfToFloat(data[i * 4]), y = Mathf.HalfToFloat(data[i * 4 + 1]), z = Mathf.HalfToFloat(data[i * 4 + 2]), a = Mathf.HalfToFloat(data[i * 4 + 3]);
                    if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsNaN(a) || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z) || float.IsInfinity(a)) { nan++; continue; }
                    if (a <= 0f) continue;
                    filledCount++;
                    if (Mathf.Abs(a - Mathf.Round(a)) > 1e-3f || (boneCount > 0 && a > boneCount)) badA++;
                }
                float frac = filledCount / (float)n;
                check(nan == 0, k + " no NaN/Inf texels (" + nan + ")");
                check(frac >= MinFilledFraction, k + " surface fraction " + frac.ToString("F3") + " >= " + MinFilledFraction + " (includes the " + DilateTexels + "-texel dilation)");
                check(badA == 0, k + " alpha is an integer bone index + 1 within 1.." + boneCount + " (bad " + badA + ")");
            }
            r.status = r.failed == 0 ? "PASS" : "FAIL";
            Directory.CreateDirectory("Temp");
            File.WriteAllText(VerifyPath, JsonUtility.ToJson(r, true));
            Debug.Log("[KH Wash Verify] " + r.status + " failed=" + r.failed + " -> " + VerifyPath);
            if (r.failed > 0) throw new InvalidOperationException("[KH Wash Verify] " + r.failed + " check(s) failed:\n" + string.Join("\n", r.lines.Where(l => l.StartsWith("FAIL"))));
        }
    }
}
