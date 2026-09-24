using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// 裏山改変 (IMP-DOG-015): かざりの器を KH_Dogs に作る入口 (Menu/6e) と検査 (Menu/6f)。
    ///
    /// **見た目は古民家の合わせ込みをそのまま持ち込む。** 同じ TORINOSUKE の犬 6 匹なので、骨 (Bone.004 / Bone.003 / Bone.001 / 尻尾の付け根) の
    /// ローカル座標で置いた古民家の実測値 (<see cref="FitsPath"/>。古民家シーンの 144 ノードから抽出、2026-09-24) を同じ骨の下へ置く。
    /// 古民家の Accessories/4 Setup は比率の表から計算し、その後に人が手で詰め、キャンディ・パンプキンはライブの演出側で合わせていた —
    /// 計算し直すと手の調整とライブ側の合わせが消えるので、裏山は**計算しない**。
    ///
    /// 流れ: 骨の下に入れ物 (KD_Acc_* / KD_CandyCostume_* / KD_PumpkinCostume_*) を用意 (既存は位置を触らない) → 中身 (FBX / メッシュ) が無ければ作る →
    /// 首輪との干渉を測って むね (Chest) の 7 品だけ前へ逃がす (対策 (a)) → KH_Dogs/KD_AccessorySystem (KD_AccessoryBox、Manual) に 24 品 × 6 匹を配線 →
    /// 商品表・一覧を配線 (<see cref="KD_AccessorySetsEditor.HillsideWire"/>。商品は書き換えない)。**シーンは保存しない。**
    ///
    /// **首輪との干渉 (古民家に前例なし)**: 首輪 (KH_Collar_*) もむねの飾りも首の骨 (Bone.003) の子なので、相対位置は姿勢で変わらない。
    /// 首輪は首に巻いた輪 (帯 = 楕円の輪 + 留め具) なので、箱の重なりではなく「飾りの頂点が帯の肉 (+ <see cref="CollarClearance"/>) に入るか」で数える
    /// (輪の内側 = 首の中は数えない。首飾りは首を一周するので、後ろ側が首の中にあるのは正常)。当たりがあれば古民家の位置から
    /// 犬の前方向 (頭 → 鼻を水平へ射影) へ 1mm ずつずらして 0 になる最小量を採る (<see cref="CollarMaxShift"/> まで。超える品は動かさずに
    /// 「解消できない」として報告し、参考に対策 (b) = 前 + 下の組を出す。取り下げはユーザー)。
    /// </summary>
    public static partial class KD_AccessorySetupEditor
    {
        const string FitsPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Editor/KD_AccessoryFits.Kominka.json";
        const string HillsideSetupLog = KD_Hillside.LogDir + "setup-accessories.txt";
        const string HillsideVerifyLog = KD_Hillside.LogDir + "verify-accessories.txt";
        const string HillsideCollarLog = KD_Hillside.LogDir + "accessory-collar.txt";
        const string CollarPrefix = "KH_Collar_";

        /// <summary>首輪 (帯の肉・留め具の箱) の外側に空ける隙間 (m)。これより近い飾りの頂点は「当たる」と数える。</summary>
        public const float CollarClearance = 0.003f;
        /// <summary>首輪を避けるために前へずらす上限 (m)。古民家の「前へ 15cm」(2.1 × headScale) の 2 割。超える品は動かさずに報告。</summary>
        public const float CollarMaxShift = 0.03f;

        // ================================================================ 古民家の実測値 (JSON)

        [Serializable] class FitRef { public string guid; public string fileID; }
        [Serializable] class FitArt
        {
            public string kind; public string guid; public string name;
            public float[] lp; public float[] lr; public float[] ls;
            public string meshGuid; public string meshFileID; public FitRef[] mats;
            public int cast; public int receive; public int lightProbe; public int reflProbe;
        }
        [Serializable] class FitNode
        {
            public string name; public float[] lp; public float[] lr; public float[] ls;
            public string fatherGuid; public string fatherFileID; public FitArt art;
        }
        [Serializable] class FitData { public string source; public FitNode[] nodes; }

        static Dictionary<string, FitNode> LoadFits(StringBuilder log)
        {
            if (!File.Exists(FitsPath)) { log.AppendLine("FAIL 古民家の実測値 " + FitsPath + " が無い"); return null; }
            var data = JsonUtility.FromJson<FitData>(File.ReadAllText(FitsPath));
            if (data == null || data.nodes == null || data.nodes.Length == 0) { log.AppendLine("FAIL " + FitsPath + " が読めない"); return null; }
            var map = new Dictionary<string, FitNode>();
            foreach (var n in data.nodes) map[n.name] = n;
            return map;
        }

        static Vector3 V3(float[] a) { return a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]); }
        static Quaternion Q4(float[] a) { return a == null || a.Length < 4 ? Quaternion.identity : new Quaternion(a[0], a[1], a[2], a[3]); }

        static Object LoadByGuidFile(string guid, string fileID)
        {
            string path = string.IsNullOrEmpty(guid) ? "" : AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            long id;
            if (!long.TryParse(fileID, out id)) return null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o == null) continue;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string g, out long f) && f == id) return o;
            }
            return null;
        }

        // ================================================================ 品目表 (24 品)

        /// <summary>品目 ID の装着枠。0..17 は <see cref="Accessories"/>、18..23 はキャンディ・パンプキン (頭 / むね / しっぽ)。</summary>
        internal static Slot HillsideSlotOf(int product)
        {
            return product < Accessories.Length ? Accessories[product].slot : KD_AccessorySetsEditor.HillsideCandySlot(product);
        }

        /// <summary>品目 ID の表示名 (ログ用)。</summary>
        static string HillsideLabelOf(int product)
        {
            if (product < Accessories.Length) return Accessories[product].displayName + " (" + Accessories[product].key + ")";
            return KD_AccessorySetsEditor.HillsideNodeName(product, "").TrimEnd('_');
        }

        /// <summary>骨 (anchor) が古民家の実測時と同じ骨か (同じ犬 prefab の同じ骨 = 対応元の GUID と fileID が一致)。</summary>
        static bool SameAnchor(Transform anchor, FitNode fit, out string actual)
        {
            actual = "-";
            var src = PrefabUtility.GetCorrespondingObjectFromSource(anchor);
            if (src == null) return false;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(src, out string g, out long f)) return false;
            actual = g + "/" + f;
            return g == fit.fatherGuid && f.ToString() == fit.fatherFileID;
        }

        /// <summary>入れ物を用意する。**既にあれば位置を触らない** (古民家 Accessories/4 と同じ「作るだけ」)。無ければ古民家の実測値で作る。</summary>
        static GameObject EnsureHolder(Transform anchor, FitNode fit, bool undo, out bool created)
        {
            created = false;
            var t = anchor.Find(fit.name);
            if (t != null) return t.gameObject;
            var go = new GameObject(fit.name);
            if (undo) Undo.RegisterCreatedObjectUndo(go, "Create " + fit.name);
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = V3(fit.lp);
            go.transform.localRotation = Q4(fit.lr);
            go.transform.localScale = V3(fit.ls);
            go.layer = anchor.gameObject.layer;
            created = true;
            return go;
        }

        /// <summary>中身 (見た目) を用意する。子が 1 つでもあれば触らない。無ければ古民家の実測値で作る
        /// (FBX の prefab インスタンス = 0..17 / メッシュ + 材質 = キャンディ・パンプキン)。</summary>
        static bool EnsureArt(GameObject holder, FitNode fit, bool undo, StringBuilder log, out bool created)
        {
            created = false;
            if (holder.transform.childCount > 0) return true;
            var a = fit.art;
            if (a == null) { log.AppendLine("   FAIL " + fit.name + ": 実測値に中身が無い"); return false; }
            GameObject art;
            if (a.kind == "prefab")
            {
                string path = AssetDatabase.GUIDToAssetPath(a.guid);
                var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { log.AppendLine("   FAIL " + fit.name + ": FBX (guid " + a.guid + ") が無い"); return false; }
                art = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.scene);
                art.transform.SetParent(holder.transform, false);
            }
            else
            {
                var mesh = LoadByGuidFile(a.meshGuid, a.meshFileID) as Mesh;
                if (mesh == null) { log.AppendLine("   FAIL " + fit.name + ": メッシュ (guid " + a.meshGuid + " / " + a.meshFileID + ") が無い"); return false; }
                var mats = new Material[a.mats == null ? 0 : a.mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = LoadByGuidFile(a.mats[i].guid, a.mats[i].fileID) as Material;
                    if (mats[i] == null) { log.AppendLine("   FAIL " + fit.name + ": 材質 (guid " + a.mats[i].guid + ") が無い"); return false; }
                }
                art = new GameObject(a.name);
                art.transform.SetParent(holder.transform, false);
                art.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = art.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = (ShadowCastingMode)a.cast;
                mr.receiveShadows = a.receive != 0;
                mr.lightProbeUsage = (LightProbeUsage)a.lightProbe;
                mr.reflectionProbeUsage = (ReflectionProbeUsage)a.reflProbe;
            }
            if (undo) Undo.RegisterCreatedObjectUndo(art, "Create " + a.name);
            art.name = a.name;
            // 犬の子は犬と同じレイヤー (裏山の Walking/3 Build が犬の子階層を 14 = PickupNoEnvironment にそろえる。地面レイが自分を拾わない)
            foreach (var t in art.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = holder.layer;
            art.transform.localPosition = V3(a.lp);
            art.transform.localRotation = Q4(a.lr);
            art.transform.localScale = a.kind == "prefab" ? Vector3.one : V3(a.ls);
            if (a.kind == "prefab") PrefabUtility.RecordPrefabInstancePropertyModifications(art.transform);
            created = true;
            return true;
        }

        // ================================================================ 首輪との干渉

        /// <summary>犬の座標軸 (右 / 上 / 前)。前 = 頭 → 鼻を上に直交へ射影 (古民家の PlaceHolder と同じ)。</summary>
        static void DogFrame(GameObject dog, KD_DogRig rig, out Vector3 right, out Vector3 up, out Vector3 fwd)
        {
            up = dog.transform.up;
            fwd = Vector3.ProjectOnPlane(rig.nose.position - rig.head.position, up);
            if (fwd.sqrMagnitude < 1e-8f) fwd = dog.transform.forward;
            fwd.Normalize();
            right = Vector3.Cross(up, fwd).normalized;
        }

        /// <summary>root 以下の描画頂点 (ワールド)。非アクティブも含む (装着ノードは既定で非表示)。Skinned は今の姿勢で焼く。</summary>
        static List<Vector3> WorldVertices(Transform root)
        {
            var list = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
            }
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                var baked = new Mesh();
                try
                {
                    skin.BakeMesh(baked, true);
                    var m = skin.transform.localToWorldMatrix;
                    foreach (var v in baked.vertices) list.Add(m.MultiplyPoint3x4(v));
                }
                finally { Object.DestroyImmediate(baked); }
            }
            return list;
        }

        struct FrameBox
        {
            public Vector3 min, max;   // x = 右, y = 上, z = 前 (m)
            public bool any;
            public void Add(Vector3 p) { if (!any) { min = max = p; any = true; } else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); } }
        }

        /// <summary>首輪の立体 (当たりの近似)。帯 (Band) = 帯のローカル XY 面の輪 (厚み方向 = ローカル Z)。輪は楕円でも中心対称でもない
        /// (実測: 中心からの距離が 0.43〜1.07 倍に散る) ので、中心まわりの角度 5° ごとに帯の内径・外径を持つ。
        /// 留め具など小物 = それぞれのメッシュの境界箱。**首の中 (輪の内側) は数えない** — 首の毛の中で見えないので。</summary>
        sealed class CollarShape
        {
            const int Bins = 72;
            public Matrix4x4 bandInv; public Vector3 bandCenter; public float hz, margin, marginZ;
            public readonly float[] rin = new float[Bins], rout = new float[Bins];
            public bool hasBand;
            public readonly List<Matrix4x4> partInv = new List<Matrix4x4>();
            public readonly List<Bounds> partBounds = new List<Bounds>();

            public static CollarShape Of(Transform collar, float margin)
            {
                var s = new CollarShape();
                foreach (var mf in collar.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var b = mf.sharedMesh.bounds;
                    float scale = Mathf.Max(1e-6f, (mf.transform.lossyScale.x + mf.transform.lossyScale.y + mf.transform.lossyScale.z) / 3f);
                    if (mf.name == "Band" && !s.hasBand)
                    {
                        s.hasBand = true;
                        s.bandInv = mf.transform.worldToLocalMatrix;
                        s.bandCenter = b.center; s.hz = b.extents.z;
                        for (int i = 0; i < Bins; i++) { s.rin[i] = float.PositiveInfinity; s.rout[i] = -1f; }
                        foreach (var v in mf.sharedMesh.vertices)
                        {
                            s.Polar(v, out int bin, out float rad);
                            s.rin[bin] = Mathf.Min(s.rin[bin], rad); s.rout[bin] = Mathf.Max(s.rout[bin], rad);
                        }
                        // 頂点の無い角度は隣の角度から埋める (輪の途中が抜けて当たりを見落とさない)
                        for (int pass = 0; pass < Bins; pass++)
                            for (int i = 0; i < Bins; i++)
                                if (s.rout[i] < 0f)
                                {
                                    int a = (i + Bins - 1) % Bins, c = (i + 1) % Bins;
                                    int src = s.rout[a] >= 0f ? a : s.rout[c] >= 0f ? c : -1;
                                    if (src >= 0) { s.rin[i] = s.rin[src]; s.rout[i] = s.rout[src]; }
                                }
                        s.margin = margin / scale;
                        s.marginZ = margin / scale;
                        continue;
                    }
                    var bb = b; bb.Expand(2f * margin / scale);
                    s.partInv.Add(mf.transform.worldToLocalMatrix); s.partBounds.Add(bb);
                }
                return s;
            }

            void Polar(Vector3 local, out int bin, out float rad)
            {
                float x = local.x - bandCenter.x, y = local.y - bandCenter.y;
                rad = Mathf.Sqrt(x * x + y * y);
                float deg = Mathf.Atan2(y, x) * Mathf.Rad2Deg + 360f;
                bin = Mathf.FloorToInt(deg / (360f / Bins)) % Bins;
            }

            /// <summary>world の点が首輪の中 (帯の輪の肉 + 余白 / 小物の箱) か。</summary>
            public bool Hit(Vector3 world)
            {
                if (hasBand)
                {
                    var l = bandInv.MultiplyPoint3x4(world);
                    if (Mathf.Abs(l.z - bandCenter.z) <= hz + marginZ)
                    {
                        Polar(l, out int bin, out float rad);
                        if (rout[bin] >= 0f && rad >= rin[bin] - margin && rad <= rout[bin] + margin) return true;
                    }
                }
                for (int i = 0; i < partInv.Count; i++) if (partBounds[i].Contains(partInv[i].MultiplyPoint3x4(world))) return true;
                return false;
            }

            public int Count(List<Vector3> world, Vector3 offset)
            {
                int n = 0;
                for (int i = 0; i < world.Count; i++) if (Hit(world[i] + offset)) n++;
                return n;
            }
        }

        /// <summary>1 品 × 1 匹の干渉の実測。</summary>
        internal struct CollarFit
        {
            public bool measured;
            public string problem;
            /// <summary>犬の座標軸 (右 / 上 / 前) での AABB が重なる体積 (cm³) と、飾りの AABB に対する割合。**粗い目安** (首輪は輪なので箱が重なっても当たるとは限らない)。</summary>
            public float overlapCm3, overlapRatio;
            /// <summary>飾りの頂点の数と、そのうち首輪の中 (帯の肉 + <see cref="CollarClearance"/> / 留め具の箱) に入る数。0 = 当たらない。</summary>
            public int itemVerts, hits;
            /// <summary>対策 (a): 当たりが 0 になる最小の「前へ」(m)。<see cref="CollarMaxShift"/> まで 1mm 刻みで探す。見つからなければ resolvable = false。</summary>
            public float shift;
            public bool resolvable;
            /// <summary>(a) の上限内での最小の当たり数 (解消できないときの残り)。</summary>
            public int bestHits;
            /// <summary>対策 (b) の参考: 前・下の両方 (各 <see cref="CollarMaxShift"/> まで 2mm 刻み) で当たりが 0 になる最小の組 (m)。bFound = false なら (b) でも無理。</summary>
            public float bFwd, bDown;
            public bool bFound;
            /// <summary>リードの留め具 (ClipAnchor) から飾りの AABB までの距離 (m)。</summary>
            public float clipDistance;
        }

        /// <summary>今の位置で測る。holder / collar は同じ犬の骨の下にあること。</summary>
        internal static CollarFit MeasureCollar(GameObject dog, KD_DogRig rig, Transform holder, Transform collar)
        {
            var r = new CollarFit();
            if (holder == null || collar == null) { r.problem = holder == null ? "飾りが無い" : "首輪が無い"; return r; }
            DogFrame(dog, rig, out Vector3 right, out Vector3 up, out Vector3 fwd);
            Vector3 F(Vector3 w) { return new Vector3(Vector3.Dot(w, right), Vector3.Dot(w, up), Vector3.Dot(w, fwd)); }
            var itemW = WorldVertices(holder);
            var ringW = WorldVertices(collar);
            if (itemW.Count == 0 || ringW.Count == 0) { r.problem = itemW.Count == 0 ? "飾りの頂点が 0 (中身が無い)" : "首輪の頂点が 0"; return r; }
            var ib = new FrameBox(); foreach (var p in itemW) ib.Add(F(p));
            var cb = new FrameBox(); foreach (var p in ringW) cb.Add(F(p));
            Vector3 lo = Vector3.Max(ib.min, cb.min), hi = Vector3.Min(ib.max, cb.max);
            Vector3 ov = Vector3.Max(Vector3.zero, hi - lo);
            r.overlapCm3 = ov.x * ov.y * ov.z * 1e6f;
            Vector3 isz = ib.max - ib.min;
            r.overlapRatio = ov.x * ov.y * ov.z / Mathf.Max(1e-12f, isz.x * isz.y * isz.z);

            var shape = CollarShape.Of(collar, CollarClearance);
            r.itemVerts = itemW.Count;
            r.hits = shape.Count(itemW, Vector3.zero);
            r.bestHits = r.hits;
            r.resolvable = r.hits == 0;
            for (int mm = 1; !r.resolvable && mm <= Mathf.RoundToInt(CollarMaxShift * 1000f); mm++)
            {
                int n = shape.Count(itemW, fwd * (mm * .001f));
                if (n < r.bestHits) r.bestHits = n;
                if (n == 0) { r.shift = mm * .001f; r.resolvable = true; }
            }
            if (!r.resolvable)
            {
                // (b) の参考: 前・下の組を近い順に探す
                float best = float.PositiveInfinity;
                int steps = Mathf.RoundToInt(CollarMaxShift * 1000f / 2f);
                for (int f = 0; f <= steps; f++)
                    for (int d = 0; d <= steps; d++)
                    {
                        float fm = f * .002f, dm = d * .002f, len = fm * fm + dm * dm;
                        if (len >= best) continue;
                        if (shape.Count(itemW, fwd * fm - up * dm) == 0) { best = len; r.bFwd = fm; r.bDown = dm; r.bFound = true; }
                    }
            }

            var clip = collar.Find("ClipAnchor");
            if (clip != null)
            {
                var c = F(clip.position);
                var dd = Vector3.Max(Vector3.zero, Vector3.Max(ib.min - c, c - ib.max));
                r.clipDistance = dd.magnitude;
            }
            else r.clipDistance = -1f;
            r.measured = true;
            return r;
        }

        /// <summary>ログの 1 行ぶん (当たり / 対策)。</summary>
        static string CollarText(CollarFit r)
        {
            if (!r.measured) return "測れない: " + r.problem;
            string s = "AABB 重なり " + r.overlapCm3.ToString("F2") + "cm³ (" + (r.overlapRatio * 100f).ToString("F0") + "%) / 首輪に入る頂点 " + r.hits + "/" + r.itemVerts;
            if (r.hits == 0) return s;
            if (r.resolvable) return s + " / (a) 前へ " + Mm(r.shift) + " で 0";
            return s + " / (a) 上限 " + Mm(CollarMaxShift) + " でも残り " + r.bestHits + (r.bFound ? " / (b) 前 " + Mm(r.bFwd) + " + 下 " + Mm(r.bDown) + " なら 0" : " / (b) 前・下 各 " + Mm(CollarMaxShift) + " でも残る");
        }

        static Transform CollarOf(Transform neck, string dogKey)
        {
            var t = neck == null ? null : neck.Find(CollarPrefix + dogKey);
            if (t != null) return t;
            if (neck != null) for (int i = 0; i < neck.childCount; i++) if (neck.GetChild(i).name.StartsWith(CollarPrefix)) return neck.GetChild(i);
            return null;
        }

        /// <summary>むねの品の対策 (a)。**古民家の位置 (基準) で測り、基準 + 前へ shift に置く** (何度流しても同じ位置 = 足し込まない)。
        /// 今の位置が「基準」でも「基準 + 前回の shift」でもなければ人が手で詰めたものとして触らない。</summary>
        static CollarFit ApplyCollarShift(GameObject dog, KD_DogRig rig, Transform holder, Transform collar, FitNode fit, bool write, StringBuilder log, out string action)
        {
            action = "";
            Vector3 current = holder.localPosition, baseline = V3(fit.lp);
            holder.localPosition = baseline;
            var r = MeasureCollar(dog, rig, holder, collar);
            DogFrame(dog, rig, out _, out _, out Vector3 fwd);
            Vector3 desired = baseline + holder.parent.InverseTransformVector(fwd * (r.measured && r.resolvable ? r.shift : 0f));
            bool atBaseline = (current - baseline).sqrMagnitude < 1e-10f;
            bool atDesired = (current - desired).sqrMagnitude < 1e-10f;
            if (!write) { holder.localPosition = current; action = atDesired ? "採用値のまま" : atBaseline ? "古民家の位置のまま" : "手の調整あり"; return r; }
            if (!r.measured) { holder.localPosition = current; action = "測れない: " + r.problem; return r; }
            if (atBaseline || atDesired)
            {
                holder.localPosition = desired;
                if (!atDesired) EditorUtility.SetDirty(holder);
                action = !r.resolvable ? "解消できない ((a) 上限 " + Mm(CollarMaxShift) + " でも当たりが残る。動かさない)"
                    : r.shift > 0f ? "前へ " + Mm(r.shift) : "干渉なし (古民家の位置)";
            }
            else { holder.localPosition = current; action = "手の調整を保持 (基準からずれている。測り直しは基準の位置で行った)"; }
            return r;
        }

        static string Mm(float m) { return (m * 1000f).ToString("F1") + "mm"; }

        // ================================================================ Menu/6e Setup Accessories

        [MenuItem("Tools/KominkaHillside/Menu/6e Setup Accessories")]
        public static void SetupHillside()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_AccessorySetup] 6e Setup Accessories (裏山 IMP-DOG-015)");
            log.AppendLine("古民家の実測値 (" + FitsPath + ") で 24 品 × 6 匹の見た目ノードを用意し、KH_Dogs/" + KD_DogDefs.AccessoryBoxName + " に配線する。**既存ノードの位置は触らない** (むねの品の首輪対策だけ例外)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと");
                KD_VerifyLog.Write(HillsideSetupLog, log); return;
            }
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int dogsCount);
            if (dogsGo == null || dogsCount != 1) { log.AppendLine("FAIL " + KD_Hillside.DogsRootName + " が 1 個ない (" + dogsCount + ")"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            var dogs = dogsGo.transform;
            if (AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(BoxProgramPath) == null)
            { log.AppendLine("FAIL " + BoxProgramPath + " が無い"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            var fits = LoadFits(log);
            if (fits == null) { KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            // 商品が無いなら何も作らない (Menu/4 Setup Shop が先)
            if (KD_AccessorySetsEditor.HillsideFindGoods(log) == null)
            {
                log.AppendLine("SETUP RESULT: FAIL (飾りの商品が無い。先に Menu/4 Setup Shop。シーンは触っていない)");
                KD_VerifyLog.Write(HillsideSetupLog, log); return;
            }

            var defs = KD_DogDefs.All;
            int dogN = defs.Length, prodN = KD_AccessorySetsEditor.HillsideProductCount;
            var nodes = new GameObject[prodN * dogN];
            var slots = new int[prodN];
            int madeHolder = 0, madeArt = 0, kept = 0;
            bool ok = true;
            var dogGos = new GameObject[dogN]; var rigs = new KD_DogRig[dogN];
            for (int d = 0; d < dogN; d++)
            {
                var t = dogs.Find(defs[d].DogName);
                dogGos[d] = t == null ? null : t.gameObject;
                rigs[d] = dogGos[d] == null ? null : KD_DogRig.Resolve(dogGos[d]);
                if (rigs[d] == null || !rigs[d].Ok) { log.AppendLine("FAIL " + KD_Hillside.DogsRootName + "/" + defs[d].DogName + " の骨が解決できない"); ok = false; }
            }
            if (!ok) { log.AppendLine("SETUP RESULT: FAIL"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }

            for (int p = 0; p < prodN; p++)
            {
                var slot = HillsideSlotOf(p);
                slots[p] = (int)slot;
                for (int d = 0; d < dogN; d++)
                {
                    string name = KD_AccessorySetsEditor.HillsideNodeName(p, defs[d].key);
                    if (!fits.TryGetValue(name, out FitNode fit)) { log.AppendLine("FAIL 実測値に " + name + " が無い"); ok = false; continue; }
                    var anchor = AnchorFor(rigs[d], slot);
                    if (anchor == null) { log.AppendLine("FAIL " + defs[d].key + " の " + slot + " の骨が無い"); ok = false; continue; }
                    if (!SameAnchor(anchor, fit, out string actualAnchor))
                    { log.AppendLine("FAIL " + name + ": 骨 " + anchor.name + " が古民家の実測時の骨と違う (古民家 " + fit.fatherGuid + "/" + fit.fatherFileID + " / 裏山 " + actualAnchor + ")"); ok = false; continue; }
                    var existing = KD_SceneObjects.Find(name, out int count);
                    if (count > 1 || (existing != null && existing.transform.parent != anchor))
                    { log.AppendLine("FAIL " + name + " が " + count + " 個 / 別の親 (" + (existing != null && existing.transform.parent != null ? existing.transform.parent.name : "-") + ")"); ok = false; continue; }
                    var holder = EnsureHolder(anchor, fit, true, out bool createdHolder);
                    if (createdHolder) madeHolder++; else kept++;
                    if (!EnsureArt(holder, fit, true, log, out bool createdArt)) { ok = false; continue; }
                    if (createdArt) madeArt++;
                    if (holder.activeSelf) holder.SetActive(false);   // 既定は外した状態 (台帳の既定 Unset と一致)
                    nodes[p * dogN + d] = holder;
                }
            }
            log.AppendLine("   ノード: 入れ物 新規 " + madeHolder + " / 既存 " + kept + " (位置は触っていない)、中身 新規 " + madeArt);

            // ---- 首輪との干渉 (むね 7 品 × 6 匹)。対策 (a) = 古民家の位置から前へだけずらす ----
            log.AppendLine("   == 首輪との干渉 (むね。前へ逃がす上限 " + Mm(CollarMaxShift) + "、隙間 " + Mm(CollarClearance) + ") ==");
            int unresolved = 0;
            for (int p = 0; p < prodN; p++)
            {
                if (slots[p] != (int)Slot.Chest) continue;
                for (int d = 0; d < dogN; d++)
                {
                    var holder = nodes[p * dogN + d];
                    if (holder == null) continue;
                    var collar = CollarOf(holder.transform.parent, defs[d].key);
                    var fit = fits[holder.name];
                    var r = ApplyCollarShift(dogGos[d], rigs[d], holder.transform, collar, fit, true, log, out string action);
                    if (r.measured && !r.resolvable) unresolved++;
                    log.AppendLine("   " + holder.name + ": " + CollarText(r) + " → " + action);
                }
            }
            log.AppendLine("   首輪: 解消できない " + unresolved + " 件" + (unresolved > 0 ? " (動かしていない。PCVR で見て、取り下げはユーザーが判断)" : ""));

            // ---- 台帳 (Manual。犬の Ownership には触らない) ----
            var boxT = dogs.Find(KD_DogDefs.AccessoryBoxName);
            KD_SceneObjects.Find(KD_DogDefs.AccessoryBoxName, out int boxCount);
            if (boxCount > 1 || (boxCount == 1 && boxT == null))
            { log.AppendLine("FAIL " + KD_DogDefs.AccessoryBoxName + " が " + boxCount + " 個 / KH_Dogs 直下に無い"); log.AppendLine("SETUP RESULT: FAIL"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            GameObject boxGo;
            if (boxT == null)
            {
                boxGo = new GameObject(KD_DogDefs.AccessoryBoxName);
                Undo.RegisterCreatedObjectUndo(boxGo, "Create " + KD_DogDefs.AccessoryBoxName);
                boxGo.transform.SetParent(dogs, false);
                log.AppendLine("   created " + KD_Hillside.DogsRootName + "/" + KD_DogDefs.AccessoryBoxName);
            }
            else boxGo = boxT.gameObject;
            var box = boxGo.GetUdonSharpComponent<KD_AccessoryBox>();
            if (box == null)
            {
                try { box = boxGo.AddUdonSharpComponent<KD_AccessoryBox>(); log.AppendLine("   added KD_AccessoryBox"); }
                catch (Exception e) { log.AppendLine("FAIL KD_AccessoryBox: " + e.Message + " — force refresh してから再実行"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            }
            KD_ShopSetupEditor.SetSync(box, VRC.SDKBase.Networking.SyncType.Manual, log);
            box.productIds = Enumerable.Range(0, prodN).ToArray();   // 恒久 ID = 表の並び (古民家と同じ 0..23)
            box.productSlots = slots;
            box.nodes = nodes;
            box.dogCount = dogN;

            var pouchT = dogs.Find(KD_DogDefs.TreatPouchName);
            var pouch = pouchT == null ? null : pouchT.GetComponent<KD_TreatPouch>();
            ok &= KD_AccessorySetsEditor.HillsideWire(box, pouch, log);
            try { UdonSharpEditorUtility.CopyProxyToUdon(box); } catch (Exception e) { log.AppendLine("FAIL CopyProxyToUdon(box): " + e.Message); ok = false; }
            if (pouch != null) { try { UdonSharpEditorUtility.CopyProxyToUdon(pouch); } catch (Exception e) { log.AppendLine("FAIL CopyProxyToUdon(pouch): " + e.Message); ok = false; } }
            else log.AppendLine("   WARN KH_Dogs/" + KD_DogDefs.TreatPouchName + " が無い — 一覧は Menu/7 Build Menu が配線する");
            EditorUtility.SetDirty(boxGo);
            ok &= nodes.All(n => n != null);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("**シーンは保存していない。** 同期する器 (KD_AccessorySystem) を足したので Menu/19b Assign Network IDs を流すこと。次: 6g → (呼ぶ) → 18 Build Dog Previews → 7 Build Menu → 6f Verify");
            KD_VerifyLog.Write(HillsideSetupLog, log);
        }

        // ================================================================ Menu/6f Verify Accessories

        [MenuItem("Tools/KominkaHillside/Menu/6f Verify Accessories")]
        public static void VerifyHillside()
        {
            var v = new KD_VerifyLog("[KD_AccessorySetup] 6f Verify Accessories (裏山 IMP-DOG-015)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene()) { v.Check(false, KD_Hillside.ScenePath + " を Edit モードで開いている"); v.Finish(HillsideVerifyLog); return; }
            var log = v.Text;
            var fits = LoadFits(log);
            v.Check(fits != null && fits.Count == 144, "古民家の実測値 144 ノード (" + FitsPath + ")");
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int dogsCount);
            if (dogsGo == null || dogsCount != 1 || fits == null) { v.Check(false, KD_Hillside.DogsRootName + " が 1 個"); v.Finish(HillsideVerifyLog); return; }
            var dogs = dogsGo.transform;

            // ---- 台帳 ----
            var boxGo = KD_SceneObjects.Find(KD_DogDefs.AccessoryBoxName, out int boxCount);
            var box = boxGo == null ? null : boxGo.GetComponent<KD_AccessoryBox>();
            v.Check(boxCount == 1 && box != null && boxGo.transform.parent == dogs, "KH_Dogs/" + KD_DogDefs.AccessoryBoxName + " に KD_AccessoryBox が 1 個 (シーン内 " + boxCount + ")");
            if (box == null) { v.Finish(HillsideVerifyLog); return; }
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(box);
            v.Check(backing != null && backing.SyncMethod == VRC.SDKBase.Networking.SyncType.Manual, "台帳の SyncMethod Manual (_slotProduct が正本)");
            v.Check(boxGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1, "台帳の GameObject に UdonBehaviour 1 個 (同期方式を混ぜない)");
            var defs = KD_DogDefs.All; int dogN = defs.Length, prodN = KD_AccessorySetsEditor.HillsideProductCount;
            v.Check(Enumerable.Range(0, Accessories.Length).All(i => Accessories[i].key == KD_AccessorySetsEditor.Keys[i]) && prodN == Accessories.Length + 6,
                "品目表 (KD_AccessorySetupEditor 18 品) と商品表の Keys (24 = 18 + キャンディ 3 + パンプキン 3) の並びが一致");
            v.Check(box.dogCount == dogN, "dogCount = " + dogN);
            v.Check(box.productIds != null && box.productIds.SequenceEqual(Enumerable.Range(0, prodN)), "productIds 0.." + (prodN - 1));
            v.Check(box.productSlots != null && box.productSlots.Length == prodN && Enumerable.Range(0, prodN).All(p => box.productSlots[p] == (int)HillsideSlotOf(p)), "productSlots が品目表どおり (Chest = Bone.003)");
            v.Check(box.nodes != null && box.nodes.Length == prodN * dogN, "nodes " + (prodN * dogN) + " (品目 × 犬)");
            GameObject[] wired;
            v.Check(backing != null && backing.publicVariables.TryGetVariableValue("nodes", out wired) && box.nodes != null && wired.SequenceEqual(box.nodes), "backing の nodes が proxy と一致 (CopyProxyToUdon 済み)");

            // ---- ノード (24 × 6) ----
            int badNode = 0, badArt = 0, badAnchor = 0, moved = 0; string firstBad = "";
            var rigs = new KD_DogRig[dogN]; var dogGos = new GameObject[dogN];
            for (int d = 0; d < dogN; d++) { var t = dogs.Find(defs[d].DogName); dogGos[d] = t == null ? null : t.gameObject; rigs[d] = t == null ? null : KD_DogRig.Resolve(t.gameObject); }
            for (int p = 0; box.nodes != null && box.nodes.Length == prodN * dogN && p < prodN; p++)
                for (int d = 0; d < dogN; d++)
                {
                    var node = box.nodes[p * dogN + d];
                    string name = KD_AccessorySetsEditor.HillsideNodeName(p, defs[d].key);
                    var anchor = rigs[d] == null ? null : AnchorFor(rigs[d], HillsideSlotOf(p));
                    bool okNode = node != null && node.name == name && anchor != null && node.transform.parent == anchor && !node.activeSelf;
                    if (!okNode) { badNode++; if (firstBad.Length == 0) firstBad = name; continue; }
                    var renderers = node.GetComponentsInChildren<Renderer>(true);
                    bool okArt = renderers.Length == 1 && renderers[0] is MeshRenderer && renderers[0].GetComponent<MeshFilter>() != null && renderers[0].GetComponent<MeshFilter>().sharedMesh != null
                        && renderers[0].sharedMaterials.Length > 0 && renderers[0].sharedMaterials.All(mt => mt != null && mt.shader != null && mt.shader.name != "Hidden/InternalErrorShader");
                    if (!okArt) { badArt++; if (firstBad.Length == 0) firstBad = name + " (中身)"; }
                    if (fits.TryGetValue(name, out FitNode fit))
                    {
                        if (!SameAnchor(anchor, fit, out _)) badAnchor++;
                        if ((node.transform.localPosition - V3(fit.lp)).sqrMagnitude > 1e-10f || Quaternion.Angle(node.transform.localRotation, Q4(fit.lr)) > .01f
                            || (node.transform.localScale - V3(fit.ls)).sqrMagnitude > 1e-10f) moved++;
                    }
                }
            v.Check(badNode == 0, "ノード 144 が名前どおり・装着枠の骨の子・既定で非表示 (不良 " + badNode + (firstBad.Length > 0 ? " 例 " + firstBad : "") + ")");
            v.Check(badArt == 0, "中身が MeshRenderer 1 個 + メッシュ + 材質 (シェーダーあり) (不良 " + badArt + ")");
            v.Check(badAnchor == 0, "骨が古民家の実測時と同じ骨 (同じ犬 prefab の同じ fileID) (不一致 " + badAnchor + ")");
            v.Note("古民家の実測値から動いているノード " + moved + " 個 (むねの首輪対策 + 手の調整。内訳は下の首輪の表)");

            // ---- 商品表・一覧 ----
            var pouchT = dogs.Find(KD_DogDefs.TreatPouchName);
            var pouch = pouchT == null ? null : pouchT.GetComponent<KD_TreatPouch>();
            KD_AccessorySetsEditor.HillsideVerifyTables(v, box, pouch);
            if (pouch != null)
            {
                bool events = true;
                for (int i = 0; i < KD_AccessorySetsEditor.MenuCount; i++) events &= typeof(KD_TreatPouch).GetMethod("_Acc" + i) != null;
                events &= typeof(KD_TreatPouch).GetMethod("_AccBack") != null && typeof(KD_TreatPouch).GetMethod("_AccConfirmBuy") != null && typeof(KD_TreatPouch).GetMethod("_AccConfirmNo") != null;
                v.Check(events, "KD_TreatPouch に _Acc0.._Acc15 / _AccBack / _AccConfirmBuy / _AccConfirmNo がある");
                v.Check(pouch.accConfirmRoot != null && pouch.accConfirmRoot == pouch.confirmRoot, "かざりの確認は共有カード 1 枚 (accConfirmRoot = confirmRoot)");
            }

            // ---- 首輪との干渉 (むね 7 品 × 6 匹。今の位置で測る) ----
            v.Info("== 首輪との干渉 (むね。犬の座標軸 = 右 / 上 / 前。食い込み = 首輪の前面 − 飾りの後ろ面、正なら重なる) ==");
            int unresolved = 0, overlapping = 0, collars = 0;
            for (int d = 0; d < dogN; d++)
            {
                if (rigs[d] == null) continue;
                var neck = AnchorFor(rigs[d], Slot.Chest);
                var collar = CollarOf(neck, defs[d].key);
                if (collar != null) collars++;
                for (int p = 0; box.nodes != null && box.nodes.Length == prodN * dogN && p < prodN; p++)
                {
                    if (HillsideSlotOf(p) != Slot.Chest) continue;
                    var node = box.nodes[p * dogN + d];
                    if (node == null) continue;
                    var r = MeasureCollar(dogGos[d], rigs[d], node.transform, collar);
                    string shiftNote = "";
                    if (fits.TryGetValue(node.name, out FitNode fit))
                    {
                        DogFrame(dogGos[d], rigs[d], out _, out _, out Vector3 fwd);
                        var delta = node.transform.parent.TransformVector(node.transform.localPosition - V3(fit.lp));
                        shiftNote = " / 古民家の位置から前へ " + Mm(Vector3.Dot(delta, fwd));
                    }
                    bool bad = r.measured && r.hits > 0;
                    if (bad) overlapping++;
                    if (bad && !r.resolvable) unresolved++;
                    v.Info("   " + (bad ? "WARN " : "ok   ") + node.name + ": " + CollarText(r) + shiftNote
                        + (r.clipDistance >= 0f ? " / リードの留め具まで " + Mm(r.clipDistance) : ""));
                }
            }
            v.Check(collars == dogN, "首輪 KH_Collar_* が 6 匹の首の骨 (Bone.003) の子にある (" + collars + ")");
            v.Note("首輪に食い込むむねの品 " + overlapping + " 件 / 対策 (a) の上限 " + Mm(CollarMaxShift) + " で解消できない品 " + unresolved + " 件"
                + (overlapping > 0 ? " — 6e を流していなければ流す。流しても残る品は PCVR で見て、取り下げはユーザーが判断" : ""));
            v.Note("PCVR でしか分からないこと: 毛 (fur シェーダー) との重なり、リードが張ったときの線と飾りの交差、姿勢 (お座り・伏せ・へそ天) での見え方");
            v.Finish(HillsideVerifyLog);
        }

        /// <summary>**シーンを変えずに**首輪との干渉を測る (犬を Preview Scene へ複製し、中身の無いノードは複製側にだけ作る)。
        /// 6e を流す前の見立て用。結果は <see cref="HillsideCollarLog"/>。戻り値も同じ本文。</summary>
        public static string HillsideCollarReport()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_AccessorySetup] 首輪との干渉の見立て (Preview Scene。シーンは変えない)");
            var fits = LoadFits(log);
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName);
            if (fits == null || dogsGo == null) { log.AppendLine("FAIL 実測値か KH_Dogs が無い"); KD_VerifyLog.Write(HillsideCollarLog, log); return log.ToString(); }
            var defs = KD_DogDefs.All; int prodN = KD_AccessorySetsEditor.HillsideProductCount;
            int unresolved = 0, overlapping = 0;
            foreach (var def in defs)
            {
                var src = dogsGo.transform.Find(def.DogName);
                if (src == null) { log.AppendLine("FAIL " + def.DogName + " が無い"); continue; }
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var clone = Object.Instantiate(src.gameObject);
                    SceneManager.MoveGameObjectToScene(clone, scene);
                    clone.transform.SetPositionAndRotation(src.position, src.rotation);
                    var rig = KD_DogRig.Resolve(clone);
                    if (!rig.Ok) { log.AppendLine("FAIL " + def.key + " の骨"); continue; }
                    var neck = AnchorFor(rig, Slot.Chest);
                    var collar = CollarOf(neck, def.key);
                    log.AppendLine("== " + def.key + " (headScale " + rig.headScale.ToString("F4") + "、首輪 " + (collar != null ? collar.name : "なし") + ")");
                    for (int p = 0; p < prodN; p++)
                    {
                        if (HillsideSlotOf(p) != Slot.Chest) continue;
                        string name = KD_AccessorySetsEditor.HillsideNodeName(p, def.key);
                        if (!fits.TryGetValue(name, out FitNode fit)) { log.AppendLine("   FAIL 実測値に " + name + " が無い"); continue; }
                        var holder = EnsureHolder(neck, fit, false, out _);
                        if (!EnsureArt(holder, fit, false, log, out _)) continue;
                        var r = ApplyCollarShift(clone, rig, holder.transform, collar, fit, true, log, out string action);
                        if (r.measured && r.hits > 0) overlapping++;
                        if (r.measured && !r.resolvable) unresolved++;
                        log.AppendLine("   " + HillsideLabelOf(p) + ": " + CollarText(r) + " → " + action
                            + (r.clipDistance >= 0f ? " / リードの留め具まで " + Mm(r.clipDistance) : ""));
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            log.AppendLine("古民家の位置で首輪に食い込む " + overlapping + " 件 / 対策 (a) の上限 " + Mm(CollarMaxShift) + " で解消できない " + unresolved + " 件");
            KD_VerifyLog.Write(HillsideCollarLog, log);
            return log.ToString();
        }
    }
}
