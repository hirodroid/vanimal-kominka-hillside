using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
// FBX の正規化と見た目ノード生成は **KD_ModelNodeUtil が正本** (おやつ棚も同じ口を使う)。
// `using static` にしてあるのは、切り出しで呼び出し側の字面を 1 文字も変えないため —
// 挙動不変の証拠を「Verify が無改造のまま PASS」だけでなく差分の形でも残す
using static KominkaDogRun.Editor.KD_ModelNodeUtil;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-FET-009 Phase 1: 投擲物の Sphere を「ちゃんとした犬用おもちゃ」の Mesh へ差し替える。
    ///
    /// **root Transform と物理・同期の契約値は一切変えない。描画だけを子 GameObject へ移す。**
    /// (root 自体は `m_Children` が増え MeshFilter/MeshRenderer が消えるので「無変更」ではない。
    ///  変えないのは localScale / SphereCollider / Rigidbody / VRCObjectSync / VRCPickup /
    ///  UdonBehaviour / layer — つまり Fetch と Verify が依存している値のほう)
    /// root 直差し替えが成立しない理由が 3 つ同時に立つため:
    ///   1. Tripo の FBX はピボットが底面 (口元へ置くと半径ぶん浮く)
    ///   2. 軸が Z-up (Model ノードに Lcl Rotation)
    ///   3. 非等方 → 正規化スケールを root に入れると
    ///      KD_FetchSetupEditor:405 の「localScale.x == ボール直径」契約が壊れる
    /// → 正規化・再センタリング・軸補正を**子の Transform 1 個に閉じ込める**。
    ///
    /// 基本ボール (Sphere) も子へ移す。Phase 2 の 6 択では「初期 1 = 基本ボール」が
    /// この Sphere そのものになるので、兄弟ノードにしておけば切り替えが
    /// 「兄弟のうち 1 つだけ SetActive(true)」で統一でき、Renderer.material 系の
    /// Udon API に一切依存しない (vrc-asset-bundle / vrc-udonsharp §6 の罠を回避)。
    /// 逆戻りも active フラグの反転で済む。
    ///
    /// **Fetch のコードは 1 行も変えない** (Phase 1 完了条件)。
    /// 咥え中の向きは root の world 回転が決める。Phase 1 で許した変更範囲
    /// (Fetch のコード無変更 + Verify:400 の「UdonBehaviour ちょうど 1 個」維持) の中では
    /// これを口相対で書く手が無い — Fetch へ書けば前者に、新規 U# を足せば後者に反する。
    /// Constraint や Animator は機構としては root 回転を書けるが、**咥えている間だけ**
    /// 効かせる切り替えがまた同じ壁に当たる。
    /// よって **Phase 1 のおもちゃは回転不変なものに限る** —
    /// TennisBall (実測 0.982 x 0.998 x 0.998) のみが条件を満たす。
    /// Bone / Rope の咥え向きは Phase 2 の仕事。
    ///
    /// KD_FetchSetupEditor に足さない理由: あちらの契約は「既存プレハブの中身は
    /// 作り直さない」(:161-165) で、こちらは逆に「意図的に書き換える可逆な変換」。
    /// 混ぜると 2 - Setup の冪等性の意味が壊れる。かつ 3 - Verify Fetch System を
    /// **無改造のまま回帰ゲート**として使いたい。
    ///
    /// 実行順:
    ///   1 - Survey Toy Assets      (書き込み無し。仮定を実測で潰す)
    ///   2 - Apply Toy Mesh
    ///   3 - Verify Toy Mesh
    ///   → Fetch System/3 - Verify と 5 - Verify Reset Button を再実行して回帰確認
    /// </summary>
    public static class KD_ToySetupEditor
    {
        const string ToysRoot = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Toys";
        const string CompareFolder = ToysRoot + "/_Compare";
        const string CompareRootName = "KD_ToyCompare_TEMP";

        /// <summary>子ノード名の接頭辞。**この接頭辞を持つ子が「見た目の候補」**で、
        /// 常にちょうど 1 個だけ active であることを Verify が検査する。</summary>
        const string MeshNodePrefix = "KD_ToyMesh_";

        /// <summary>基本ボール (Unity 組み込み Sphere) の子ノード名。
        /// Phase 2 の「初期 1」がこれ。**消さずに inactive で持ち越す**。</summary>
        const string BasicBallNode = MeshNodePrefix + "BasicBall";

        /// <summary>Phase 1 で実プレハブへ当てたおもちゃ。回転不変なものに限る (上記参照)。
        /// **Phase 2 では 1 品目固定をやめた** (<see cref="ShopToys"/>) が、
        /// `4 - Revert` と比較盤がまだ参照する。</summary>
        const string Phase1Toy = "TennisBall";

        /// <summary>**Phase 2: 棚に並ぶおもちゃ全部**。この順で子ノードを作り、
        /// **ちょうど 1 つだけ active** にする (切り替えは実行時の `SetActive` なので、
        /// ここでは「器を全部そろえる」ことだけが仕事)。
        ///
        /// **`KD_ShopSetupEditor.Toys` と品目をそろえること** — あちらが値段と恒久 ID の正本で、
        /// こちらは見た目の器の正本。ずれると「買えたのに見た目が変わらない」になるので、
        /// `Shop/3 - Verify` が突き合わせて落とす。
        ///
        /// **【2026-08-23 更新】Bone を入れた。** 咥えた向きが運任せになる件は
        /// **実機で見て許容と判断された**ので、扁平な品を外す理由が無くなった
        /// (`KD_DogFetchController` へ回転オフセットを入れる案は見送り = Fetch は無改造のまま)。
        ///
        /// **Rope はまだ入れない** — 1 カテゴリ 5〜6 択が上限
        /// ([progression-ce.md §4](../../../docs/development/design/progression-ce.md) の選択過多)。
        /// 基本ボール + この 5 品でちょうど 6。</summary>
        static readonly string[] ShopToys = { "TennisBall", "Donut", "Bone", "ClothBall",
            // VER-FET-001 (2026-09-05): 手毬 2 案を CE 候補として棚へ並べ、Play で選んで比べる。
            // 判定が済んだら落選側をここと KD_ShopSetupEditor.Toys から外し、フォルダを消す
            "Temari_Claude", "PawCushion", "TaiyakiPlush", "EbiTenPlush" };

        /// <summary>シーンの初期状態で active にする子ノード。
        ///
        /// **無料の初期品 = 基本ボール** ([progression-ce.md §6](../../../docs/development/design/progression-ce.md)
        /// の陳列例がこの形)。`KD_ToyBox` の同期変数は既定 -1 = 「誰も選んでいない =
        /// シーンの初期状態が正」なので、**ここが無料品と一致していないと
        /// 「誰も選んでいないのに有料品が出ている」**ことになる。</summary>
        const string DefaultActiveNode = BasicBallNode;

        /// <summary>識別層 手 A の tint 強度。Standard の albedo は _MainTex x _Color の
        /// **乗算 = 暗くする方向にしか効かない**ため、実機で 1.0 / 0.6 を見比べて決める。</summary>
        const float TintStrength = 1.0f;

        const float TintStrengthSoft = 0.6f;

        // ---- 比較盤のレイアウト (実機で見て動かす前提の初期値) ----
        // KD_ToyCompare_TEMP はルート 1 個なので、Scene ビューで手で動かせる
        const float CompareItemGap = 0.18f;  // 群内の間隔 (m)
        const float CompareGroupGap = 0.45f; // 群間の間隔 (m)

        struct ToyDef
        {
            public string key;
            public string displayName;
            /// <summary>長軸をボール直径の何倍にするか。1.0 = 今の球とシルエットの幅が揃う。
            /// **大きさのズレを詰めたいときの唯一のつまみ** (識別層 1 を壊さないため
            /// 犬ごとの ballDiameter 側は動かさない)。</summary>
            public float visualScale;

            public string FbxPath { get { return ToysRoot + "/" + key + "/" + key + ".fbx"; } }
            public string BaseMaterialPath { get { return ToysRoot + "/" + key + "/M_KD_Toy_" + key + ".mat"; } }
            public string NodeName { get { return MeshNodePrefix + key; } }
            public float Tint { get { return System.Array.IndexOf(KD_PlushShelfEditor.MeshKeys, key) >= 0 ? KD_PlushShelfEditor.TintStrength : TintStrength; } }

            public string TintMaterialPath(string dogKey)
            {
                return ToysRoot + "/" + key + "/M_KD_Toy_" + key + "_" + dogKey + ".mat";
            }
        }

        static readonly ToyDef[] Toys =
        {
            new ToyDef { key = "TennisBall", displayName = "テニスボール", visualScale = 1.0f },
            new ToyDef { key = "Bone",       displayName = "骨",           visualScale = 1.0f },
            new ToyDef { key = "Rope",       displayName = "ロープ",       visualScale = 1.0f },
            // 2026-08-23 追加 (ユーザーが Tripo から 3 品を投入)。
            // Donut = ポイント品 / PawPlush + ClothBall = CE 品
            new ToyDef { key = "Donut",      displayName = "ドーナツ",     visualScale = 1.0f },
            new ToyDef { key = "PawPlush",   displayName = "肉球",         visualScale = 1.0f },
            new ToyDef { key = "ClothBall",  displayName = "布ボール",     visualScale = 1.0f },
            // VER-FET-001: Blender 製の CE 候補 2 案 (Claude Code / Codex)。比較のため ShopToys にも入れてある。
            // 落選したらこの行と ShopToys / KD_ShopSetupEditor.Toys の行とフォルダを消す
            new ToyDef { key = "Temari_Claude", displayName = "手毬 (Claude)", visualScale = 1.0f },
            new ToyDef { key = "Temari_Codex", displayName = "手毬 (Codex)", visualScale = 1.0f },
            new ToyDef { key = "PawCushion", displayName = "肉球クッション", visualScale = 1.05f },
            new ToyDef { key = "TaiyakiPlush", displayName = "たい焼きぬいぐるみ", visualScale = 1.5f },
            new ToyDef { key = "EbiTenPlush", displayName = "エビ天ぬいぐるみ", visualScale = 1.6f },
        };

        static ToyDef FindToy(string key)
        {
            foreach (var t in Toys) if (t.key == key) return t;
            return default(ToyDef);
        }

        // Append only the new visual candidates; preserve existing children and physics.
        public static void ApplyPlushCandidates()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Edit mode only");
            var keys = KD_PlushShelfEditor.MeshKeys;
            var meshes = new Mesh[keys.Length]; var matrices = new Matrix4x4[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                if (!ResolveToyMesh(FindToy(keys[i]), out meshes[i], out matrices[i], out string error))
                    throw new System.InvalidOperationException(error);
            var log = new StringBuilder();
            foreach (var dog in KD_DogDefs.All)
            {
                var root = PrefabUtility.LoadPrefabContents(dog.BallPrefabPath);
                try
                {
                    for (int i = 0; i < keys.Length; i++)
                    {
                        var toy = FindToy(keys[i]);
                        var mat = EnsureTintMaterial(toy, dog, toy.Tint, toy.TintMaterialPath(dog.key), log);
                        if (mat == null || !BuildMeshNode(root, toy.NodeName, meshes[i], matrices[i], toy.visualScale, mat, log))
                            throw new System.InvalidOperationException("Plush visual failed: " + dog.key + "/" + toy.key);
                        root.transform.Find(toy.NodeName).gameObject.SetActive(false);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, dog.BallPrefabPath, out bool saved);
                    if (!saved) throw new System.InvalidOperationException("Failed to save " + dog.BallPrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }

        // =====================================================================
        // 1 - Survey (書き込み無し)
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Box/1 - Survey Toy Assets")]
        public static void Survey()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ToySetup] Survey (書き込み無し)");
            log.AppendLine("目的: 軸・ピボット面・bounds・マテリアル解決・三角数の仮定を実測で潰す");
            log.AppendLine();

            int fail = 0;

            foreach (var toy in Toys)
            {
                log.AppendLine("== " + toy.displayName + " (" + toy.key + ")");
                log.AppendLine("   fbx: " + toy.FbxPath);

                Mesh mesh;
                Matrix4x4 modelMatrix;
                string problem;
                if (!ResolveToyMesh(toy, out mesh, out modelMatrix, out problem))
                {
                    log.AppendLine("   FAIL " + problem);
                    fail++;
                    log.AppendLine();
                    continue;
                }

                // ---- メッシュ素の実測 (Unity インポート後のローカル空間) ----
                var mb = mesh.bounds;
                log.AppendLine(string.Format(
                    "   mesh '{0}': verts {1} / tris {2} / submesh {3}",
                    mesh.name, mesh.vertexCount, mesh.triangles.Length / 3, mesh.subMeshCount));
                log.AppendLine(string.Format(
                    "   mesh.bounds  center {0} / size {1}", V(mb.center), V(mb.size)));
                log.AppendLine("   ピボット面 (mesh local): " + DescribePivotFace(mb));

                // ---- モデル階層の合成行列 (importer が軸変換をどこへ置いたか) ----
                log.AppendLine("   model matrix: rot " + V(modelMatrix.rotation.eulerAngles) +
                               " / scale " + V(modelMatrix.lossyScale));

                // ---- 子 Transform の算出 ----
                Vector3 pos, scale; Quaternion rot; Vector3 measuredCenter, measuredSize;
                if (!ComputeChildTransform(mesh, modelMatrix, toy.visualScale,
                        out pos, out rot, out scale, out measuredCenter, out measuredSize, out problem))
                {
                    log.AppendLine("   FAIL " + problem);
                    fail++;
                    log.AppendLine();
                    continue;
                }
                log.AppendLine(string.Format(
                    "   model 空間の実測 AABB: center {0} / size {1}", V(measuredCenter), V(measuredSize)));
                log.AppendLine("   → 子 Transform: pos " + V(pos) + " / rot " + V(rot.eulerAngles) +
                               " / scale " + V(scale));

                // 検算: この Transform を通したメッシュが「中心 0 / 長軸 visualScale」になるか
                Vector3 vc, vs;
                MeasureMeshUnder(mesh, Matrix4x4.TRS(pos, rot, scale), out vc, out vs);
                float longest = Mathf.Max(vs.x, Mathf.Max(vs.y, vs.z));
                bool centered = vc.magnitude < 0.001f;
                bool sized = Mathf.Abs(longest - toy.visualScale) < 0.001f;
                log.AppendLine(string.Format(
                    "   検算: center {0} (中心 0: {1}) / size {2} 長軸 {3:F4} (== {4}: {5})",
                    V(vc), centered ? "OK" : "NG", V(vs), longest, toy.visualScale, sized ? "OK" : "NG"));
                if (!centered || !sized) fail++;

                // ---- 犬ごとのワールド実寸 ----
                foreach (var def in KD_DogDefs.All)
                {
                    log.AppendLine(string.Format(
                        "     {0,-6} 直径 {1:F3}m → おもちゃのワールド寸 {2} (長軸 {3:F4}m)",
                        def.key, def.ballDiameter,
                        V(vs * def.ballDiameter), longest * def.ballDiameter));
                }

                // ---- マテリアル ----
                var mat = AssetDatabase.LoadAssetAtPath<Material>(toy.BaseMaterialPath);
                if (mat == null)
                {
                    log.AppendLine("   FAIL material not found: " + toy.BaseMaterialPath);
                    fail++;
                }
                else
                {
                    var tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                    log.AppendLine("   material: " + mat.name + " / shader " + mat.shader.name +
                                   " / _MainTex " + (tex == null ? "**null**" : tex.name +
                                   " " + ((Texture2D)tex).width + "x" + ((Texture2D)tex).height));
                    if (tex == null) fail++;
                }
                log.AppendLine();
            }

            // ---- 現行ボールプレハブの現状 ----
            log.AppendLine("== 現行のボールプレハブ");
            foreach (var def in KD_DogDefs.All)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath);
                if (prefab == null) { log.AppendLine("   FAIL missing " + def.BallPrefabPath); fail++; continue; }
                var mf = prefab.GetComponent<MeshFilter>();
                int toyNodes = 0;
                foreach (Transform c in prefab.transform)
                    if (c.name.StartsWith(MeshNodePrefix)) toyNodes++;
                log.AppendLine(string.Format(
                    "   {0,-6} scale {1:F3} / root MeshFilter {2} / {3} 子 / {4} 個の {5}*",
                    def.key, prefab.transform.localScale.x,
                    mf == null ? "無し (適用済み?)" : "有り (" + (mf.sharedMesh == null ? "null" : mf.sharedMesh.name) + ")",
                    prefab.transform.childCount, toyNodes, MeshNodePrefix));
            }

            log.AppendLine();
            log.AppendLine(fail == 0
                ? "RESULT: PASS — 2 - Apply Toy Mesh へ進んでよい"
                : "RESULT: FAIL (" + fail + ") — 適用前に式か資産を直す");
            Finish("Logs/imp-fet-009-survey.txt", log);
        }

        // =====================================================================
        // 2 - Apply
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Box/2 - Apply Toy Mesh")]
        public static void Apply()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ToySetup] Apply — 棚のおもちゃ " + ShopToys.Length
                + " 品 / 識別層 手 A (tint " + TintStrength + ")");
            log.AppendLine("**シーンは触らない。** 書き込むのはボールプレハブ 4 本と派生マテリアルだけ");
            log.AppendLine("既定で active にするのは " + DefaultActiveNode + " (無料の初期品)");
            log.AppendLine();

            // **先に全品目のメッシュを解決する。** 1 品目でも欠けていたら
            // プレハブを 1 本も書き換えないで止める — 途中まで適用された状態が
            // いちばん厄介 (犬によって持っている器が違う) なので作らない
            var toys = new List<ToyDef>();
            var meshes = new List<Mesh>();
            var matrices = new List<Matrix4x4>();
            foreach (var key in ShopToys)
            {
                var t = FindToy(key);
                Mesh m; Matrix4x4 mx; string problem;
                if (!ResolveToyMesh(t, out m, out mx, out problem))
                {
                    log.AppendLine("FAIL " + key + ": " + problem);
                    log.AppendLine("RESULT: FAIL (プレハブは 1 本も書き換えていない)");
                    Finish("Logs/imp-fet-009-apply.txt", log);
                    return;
                }
                toys.Add(t); meshes.Add(m); matrices.Add(mx);
                log.AppendLine("resolved " + key + " (tris " + (m.triangles.Length / 3) + ")");
            }
            log.AppendLine();

            int fail = 0;
            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("-- " + def.displayName + " (" + def.key + ")");

                // 識別層 手 A: 犬色を乗せた派生マテリアル。**実行時に色を作らない** —
                // アセット時に持つので Udon の Renderer.material に依存しない。
                // **品目ごとに 1 枚ずつ要る** (4 犬 × 4 品 = 16 枚)
                var tintMats = new List<Material>();
                bool matOk = true;
                foreach (var t in toys)
                {
                    var m = EnsureTintMaterial(t, def, t.Tint, t.TintMaterialPath(def.key), log);
                    if (m == null) { matOk = false; break; }
                    tintMats.Add(m);
                }
                if (!matOk) { fail++; continue; }

                var root = PrefabUtility.LoadPrefabContents(def.BallPrefabPath);
                if (root == null)
                {
                    log.AppendLine("   FAIL LoadPrefabContents: " + def.BallPrefabPath);
                    fail++;
                    continue;
                }
                try
                {
                    // ---- root の描画を子へ退避する (基本ボール = Phase 2 の「初期 1」) ----
                    Mesh basicMesh = null;
                    Material basicMat = AssetDatabase.LoadAssetAtPath<Material>(def.BallMaterialPath);
                    // MeshFilter と MeshRenderer は**独立に**撤去する。
                    // 「片方だけ残っている」不完全な状態 (手作業や中断で起きうる) を
                    // 素通りさせると、root に Renderer が残ったまま子でも描く二重描画になる
                    var rootMf = root.GetComponent<MeshFilter>();
                    var rootMr = root.GetComponent<MeshRenderer>();
                    if (rootMf != null) basicMesh = rootMf.sharedMesh;
                    if (rootMr != null && rootMr.sharedMaterial != null) basicMat = rootMr.sharedMaterial;
                    if (rootMr != null) Object.DestroyImmediate(rootMr, true);
                    if (rootMf != null) Object.DestroyImmediate(rootMf, true);
                    if (rootMf != null || rootMr != null)
                        log.AppendLine("   root から撤去: " +
                                       (rootMf != null ? "MeshFilter" : "(MeshFilter 無し)") + " / " +
                                       (rootMr != null ? "MeshRenderer" : "(MeshRenderer 無し)") +
                                       " (mesh: " + (basicMesh == null ? "null" : basicMesh.name) + ")");
                    else
                        log.AppendLine("   root に MeshFilter/MeshRenderer 無し (適用済み。子だけ作り直す)");
                    if (basicMesh == null) basicMesh = BuiltinSphereMesh();

                    // ---- 基本ボール (inactive) ----
                    if (!BuildMeshNode(root, BasicBallNode, basicMesh, Matrix4x4.identity,
                            1.0f, basicMat, log)) { fail++; continue; }

                    // ---- 棚のおもちゃを全部 (器だけ作る。active は下で 1 つに絞る) ----
                    bool nodeOk = true;
                    for (int i = 0; i < toys.Count; i++)
                    {
                        if (BuildMeshNode(root, toys[i].NodeName, meshes[i], matrices[i],
                                toys[i].visualScale, tintMats[i], log)) continue;
                        nodeOk = false;
                        break;
                    }
                    if (!nodeOk) { fail++; continue; }

                    // ---- 見た目の候補はちょうど 1 個だけ active ----
                    int activated = 0;
                    foreach (Transform c in root.transform)
                    {
                        if (!c.name.StartsWith(MeshNodePrefix)) continue;
                        bool on = c.name == DefaultActiveNode;
                        c.gameObject.SetActive(on);
                        if (on) activated++;
                    }
                    log.AppendLine("   active な " + MeshNodePrefix + "* : " + activated
                        + " 個 (" + DefaultActiveNode + ")");

                    var saved = PrefabUtility.SaveAsPrefabAsset(root, def.BallPrefabPath);
                    if (saved == null)
                    {
                        log.AppendLine("   FAIL SaveAsPrefabAsset returned null");
                        fail++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            log.AppendLine();
            log.AppendLine(fail == 0
                ? "RESULT: PASS — 3 - Verify Toy Mesh と Fetch System/3 - Verify を回すこと"
                : "RESULT: FAIL (" + fail + ")");
            Finish("Logs/imp-fet-009-apply.txt", log);
        }

        // =====================================================================
        // 3 - Verify
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Box/3 - Verify Toy Mesh")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ToySetup] Verify");
            log.AppendLine("**Fetch System/3 - Verify が見ていない差分だけ**を見る");
            log.AppendLine();
            int fail = 0;
            System.Action<bool, string> Check = (ok, what) =>
            {
                log.AppendLine((ok ? "PASS " : "FAIL ") + what);
                if (!ok) fail++;
            };

            var tintColors = new List<Color>();
            var tintKeys = new List<string>();
            // 遠目判定の補助数値はテニスボール (Phase 1 で実機比較した品) を代表に採る
            var toy = FindToy(Phase1Toy);

            foreach (var def in KD_DogDefs.All)
            {
                string tag = "[" + def.key + "] ";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.BallPrefabPath);
                if (prefab == null) { Check(false, tag + "prefab exists"); continue; }

                // 1. root の契約 (Fetch Verify の入力が動いていないこと)
                Check(Mathf.Abs(prefab.transform.localScale.x - def.ballDiameter) < 0.0001f &&
                      prefab.transform.localScale == Vector3.one * def.ballDiameter,
                    tag + "root localScale == ballDiameter (" + def.ballDiameter + ", actual " +
                    V(prefab.transform.localScale) + ")");
                Check(prefab.GetComponent<MeshFilter>() == null && prefab.GetComponent<MeshRenderer>() == null,
                    tag + "root に MeshFilter/MeshRenderer が無い (描画は子へ移した)");
                Check(prefab.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1,
                    tag + "UdonBehaviour はまだ 1 個 (Fetch Verify:400 の契約)");

                // 2. 物理と Pickup が不変
                var sc = prefab.GetComponent<SphereCollider>();
                Check(sc != null && Mathf.Approximately(sc.radius, 0.5f) && sc.center == Vector3.zero &&
                      !sc.isTrigger,
                    tag + "SphereCollider r=0.5 / center 0 / not trigger (当たりは球のまま)");
                var rb = prefab.GetComponent<Rigidbody>();
                Check(rb != null && Mathf.Approximately(rb.mass, 0.2f) && rb.useGravity && !rb.isKinematic &&
                      rb.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic,
                    tag + "Rigidbody mass 0.2 / gravity / ContinuousDynamic が不変");
                int pickupLayer = LayerMask.NameToLayer("Pickup");
                int wantLayer = pickupLayer >= 0 ? pickupLayer : 13;
                Check(prefab.layer == wantLayer, tag + "root layer == Pickup");

                // 3. 見た目の候補はちょうど 1 個だけ active
                GameObject activeNode = null;
                int nodes = 0, actives = 0;
                bool layersOk = true;
                foreach (Transform c in prefab.transform)
                {
                    if (!c.name.StartsWith(MeshNodePrefix)) continue;
                    nodes++;
                    if (c.gameObject.layer != wantLayer) layersOk = false;
                    if (!c.gameObject.activeSelf) continue;
                    actives++;
                    activeNode = c.gameObject;
                }
                // **棚に並ぶ品目ぶん + 基本ボール**の器がそろっていること。
                // 1 品目でも欠けると「買えたのに見た目が変わらない」になる
                Check(nodes == ShopToys.Length + 1,
                    tag + MeshNodePrefix + "* が " + (ShopToys.Length + 1) + " 個 (基本ボール + 棚の "
                    + ShopToys.Length + " 品。actual " + nodes + ")");
                Check(actives == 1, tag + MeshNodePrefix + "* のうち active はちょうど 1 個 (actual " + actives + ")");
                Check(layersOk, tag + "子ノードの layer も Pickup");

                // **既定 active は無料の初期品。** ここが有料品だと
                // 「誰も選んでいないのに有料品が出ている」状態でワールドが始まる
                // (KD_ToyBox の同期変数 -1 = シーンの初期状態が正、なので)
                Check(activeNode != null && activeNode.name == DefaultActiveNode,
                    tag + "active な子が " + DefaultActiveNode + " (actual " +
                    (activeNode == null ? "無し" : activeNode.name) + ")");

                // **棚の品目が全部そろっているか、名指しで見る**
                foreach (var key in ShopToys)
                    Check(prefab.transform.Find(MeshNodePrefix + key) != null,
                        tag + MeshNodePrefix + key + " がある");

                // 基本ボールの中身が残っているか。
                //
                // **active かどうかはここでは見ない** — Phase 1 は「テニスボールが active
                // なので基本ボールは inactive」だったが、**Phase 2 で基本ボールが
                // 無料の初期品 = 既定 active になった**ので、その表明は逆になっている。
                // active の判定は上の DefaultActiveNode の検査 1 箇所に集約する
                var basicT = prefab.transform.Find(BasicBallNode);
                Check(basicT != null, tag + BasicBallNode + " が残っている (無料の初期品)");
                if (basicT != null)
                {
                    var bmf = basicT.GetComponent<MeshFilter>();
                    var bmr = basicT.GetComponent<MeshRenderer>();
                    Check(bmf != null && bmf.sharedMesh != null && bmf.sharedMesh.name == "Sphere",
                        tag + BasicBallNode + " の mesh が組み込み Sphere (actual " +
                        (bmf == null || bmf.sharedMesh == null ? "null" : bmf.sharedMesh.name) + ")");
                    Check(bmr != null && bmr.sharedMaterial != null &&
                          AssetDatabase.GetAssetPath(bmr.sharedMaterial) == def.BallMaterialPath,
                        tag + BasicBallNode + " の material == " + def.BallMaterialPath);
                }

                // 子に物理・同期・Udon を足していないこと。
                // root の GetComponents は 1 個でも、子に足せばプレハブ全体では増える
                foreach (Transform c in prefab.transform)
                {
                    if (!c.name.StartsWith(MeshNodePrefix)) continue;
                    Check(c.GetComponentsInChildren<Collider>(true).Length == 0 &&
                          c.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                          c.GetComponentsInChildren<VRC.SDK3.Components.VRCObjectSync>(true).Length == 0 &&
                          c.GetComponentsInChildren<VRC.SDK3.Components.VRCPickup>(true).Length == 0 &&
                          c.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length == 0,
                        tag + c.name + " に Collider/Rigidbody/ObjectSync/Pickup/Udon が無い (見た目だけのノード)");
                }
                Check(prefab.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length == 1,
                    tag + "プレハブ全体でも UdonBehaviour は 1 個 (actual " +
                    prefab.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true).Length + ")");

                // ---- 棚の品目**すべて**について中身を見る ----
                //
                // **active な 1 個だけ見ていては足りない。** 既定 active は基本ボールなので、
                // それだけ見ると**おもちゃの mesh も大きさも一度も検査されない**まま
                // PASS してしまう (買って初めて壊れているのが分かる)
                foreach (var key in ShopToys)
                {
                    var t = FindToy(key);
                    var nodeT = prefab.transform.Find(t.NodeName);
                    if (nodeT == null) continue;   // 上で FAIL 済み
                    string ntag = tag + t.NodeName + " ";

                    var nmf = nodeT.GetComponent<MeshFilter>();
                    var nmr = nodeT.GetComponent<MeshRenderer>();
                    Check(nmf != null && nmf.sharedMesh != null, ntag + "に MeshFilter + mesh");
                    Check(nmr != null && nmr.sharedMaterial != null, ntag + "に MeshRenderer + material");
                    if (nmf == null || nmf.sharedMesh == null || nmr == null || nmr.sharedMaterial == null)
                        continue;

                    // 6. Mesh / Material が期待どおりのアセットを直参照しているか
                    //    (model prefab 経由や、別の犬の派生マテリアルを掴んでいないか)
                    Check(AssetDatabase.GetAssetPath(nmf.sharedMesh) == t.FbxPath,
                        ntag + "mesh == " + t.FbxPath + " のサブアセット (actual " +
                        AssetDatabase.GetAssetPath(nmf.sharedMesh) + ")");
                    Check(AssetDatabase.GetAssetPath(nmr.sharedMaterial) == t.TintMaterialPath(def.key),
                        ntag + "material == " + t.TintMaterialPath(def.key) + " (actual " +
                        AssetDatabase.GetAssetPath(nmr.sharedMaterial) + ")");
                    var ntex = nmr.sharedMaterial.HasProperty("_MainTex")
                        ? nmr.sharedMaterial.GetTexture("_MainTex") : null;
                    Check(ntex != null, ntag + "material の _MainTex が非 null (" + nmr.sharedMaterial.name + ")");

                    // 4/5. 大きさと中心の機械検査 (root ローカル単位。1.0 == 直径)
                    var nm = Matrix4x4.TRS(nodeT.localPosition, nodeT.localRotation, nodeT.localScale);
                    Vector3 nc, ns;
                    MeasureMeshUnder(nmf.sharedMesh, nm, out nc, out ns);
                    float nlongest = Mathf.Max(ns.x, Mathf.Max(ns.y, ns.z));
                    Check(Mathf.Abs(nlongest - t.visualScale) < 0.02f,
                        ntag + string.Format("長軸が品目の visualScale {0:F4} (actual {1:F4})", t.visualScale, nlongest));
                    Check(nc.magnitude < 0.02f,
                        ntag + string.Format("中心が root 原点から 2% 未満 (actual {0:F4})", nc.magnitude));

                    // 9. 遠目判定の補助数値 (代表 1 品でよい)
                    if (key == Phase1Toy && nmr.sharedMaterial.HasProperty("_Color"))
                    {
                        tintColors.Add(nmr.sharedMaterial.GetColor("_Color"));
                        tintKeys.Add(def.key);
                    }
                }

                // 基本ボールの中身は上の「BasicBallNode が inactive で残っている」節が見る
                if (activeNode == null) continue;

                // 10. 既存の識別色資産を消していないか (リセットボタン / KD_DogUnitEditor が使う)
                Check(AssetDatabase.LoadAssetAtPath<Material>(def.BallMaterialPath) != null,
                    tag + def.BallMaterialPath + " がまだ存在する");
            }

            // 遠目の読みやすさの補助数値 (最終判断は人間。合格線は「今の球より落ちていないこと」)
            if (tintColors.Count >= 2)
            {
                log.AppendLine();
                log.AppendLine("-- 識別層 手 A の実効 _Color (ペアワイズ最大チャンネル距離。判断は実機で)");
                for (int i = 0; i < tintColors.Count; i++)
                    log.AppendLine(string.Format("   {0,-6} {1}", tintKeys[i], V(new Vector3(
                        tintColors[i].r, tintColors[i].g, tintColors[i].b))));
                float worst = float.MaxValue; string worstPair = "";
                for (int i = 0; i < tintColors.Count; i++)
                    for (int j = i + 1; j < tintColors.Count; j++)
                    {
                        float d = Mathf.Max(Mathf.Abs(tintColors[i].r - tintColors[j].r),
                            Mathf.Max(Mathf.Abs(tintColors[i].g - tintColors[j].g),
                                      Mathf.Abs(tintColors[i].b - tintColors[j].b)));
                        if (d < worst) { worst = d; worstPair = tintKeys[i] + " vs " + tintKeys[j]; }
                    }
                log.AppendLine(string.Format("   最も近い組: {0} = {1:F3}", worstPair, worst));
            }

            log.AppendLine();
            log.AppendLine(fail == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fail + ")");
            Finish("Logs/imp-fet-009-verify.txt", log);
        }

        // =====================================================================
        // 4 - Revert
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Box/4 - Revert To Basic Ball")]
        public static void RevertToBasicBall()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ToySetup] Revert to basic ball (active フラグの反転のみ。構造は残す)");
            int fail = 0;
            foreach (var def in KD_DogDefs.All)
            {
                var root = PrefabUtility.LoadPrefabContents(def.BallPrefabPath);
                if (root == null) { log.AppendLine("FAIL load " + def.BallPrefabPath); fail++; continue; }
                try
                {
                    bool found = false;
                    foreach (Transform c in root.transform)
                    {
                        if (!c.name.StartsWith(MeshNodePrefix)) continue;
                        bool on = c.name == BasicBallNode;
                        c.gameObject.SetActive(on);
                        if (on) found = true;
                    }
                    if (!found)
                    {
                        log.AppendLine("FAIL " + def.key + ": " + BasicBallNode + " が無い (4b を使う)");
                        fail++;
                        continue;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, def.BallPrefabPath);
                    log.AppendLine("   " + def.key + ": " + BasicBallNode + " を active に戻した");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            log.AppendLine(fail == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fail + ")");
            Finish("Logs/imp-fet-009-revert.txt", log);
        }

        /// <summary>子ノードを畳んで root 直描画のレイアウトへ戻す。
        ///
        /// **「元のプレハブへの完全復元」ではない。** MeshRenderer を作り直すので
        /// `m_PreserveUVs` などの Renderer 設定・fileID・コンポーネント順は元と一致しない
        /// (Mesh / Material / 構造だけが戻る)。バイト単位で戻したいときは
        /// `git checkout -- Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/` を使うこと。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Box/4b - Restore Root Layout (not byte-identical)")]
        public static void RevertFull()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ToySetup] Restore root layout (子を削除して root へ MeshFilter/MeshRenderer を戻す)");
            log.AppendLine("**完全復元ではない** — Renderer 設定 (m_PreserveUVs 等) と fileID は元と一致しない。");
            log.AppendLine("バイト単位で戻すなら git checkout -- Assets/KominkaHillside/Imported/KominkaDogRun/Prefabs/");
            int fail = 0;
            foreach (var def in KD_DogDefs.All)
            {
                var root = PrefabUtility.LoadPrefabContents(def.BallPrefabPath);
                if (root == null) { log.AppendLine("FAIL load " + def.BallPrefabPath); fail++; continue; }
                try
                {
                    Mesh mesh = null; Material mat = null;
                    var doomed = new List<GameObject>();
                    foreach (Transform c in root.transform)
                    {
                        if (!c.name.StartsWith(MeshNodePrefix)) continue;
                        if (c.name == BasicBallNode)
                        {
                            var mf = c.GetComponent<MeshFilter>();
                            var mr = c.GetComponent<MeshRenderer>();
                            if (mf != null) mesh = mf.sharedMesh;
                            if (mr != null) mat = mr.sharedMaterial;
                        }
                        doomed.Add(c.gameObject);
                    }
                    foreach (var g in doomed) Object.DestroyImmediate(g, true);

                    if (mesh == null) mesh = BuiltinSphereMesh();
                    if (mat == null) mat = AssetDatabase.LoadAssetAtPath<Material>(def.BallMaterialPath);
                    if (root.GetComponent<MeshFilter>() == null) root.AddComponent<MeshFilter>();
                    if (root.GetComponent<MeshRenderer>() == null) root.AddComponent<MeshRenderer>();
                    root.GetComponent<MeshFilter>().sharedMesh = mesh;
                    root.GetComponent<MeshRenderer>().sharedMaterial = mat;

                    PrefabUtility.SaveAsPrefabAsset(root, def.BallPrefabPath);
                    log.AppendLine("   " + def.key + ": root へ復元 (mesh " + mesh.name + " / mat " +
                                   (mat == null ? "null" : mat.name) + ")");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            log.AppendLine(fail == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fail + ")");
            Finish("Logs/imp-fet-009-revert-full.txt", log);
        }

        // =====================================================================
        // 5 / 6 - 識別層の比較盤 (シーンを保存しない)
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Box/5 - Build Identity Compare (TEMP)")]
        public static void BuildCompare()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ToySetup] Build identity compare board (TEMP)");
            log.AppendLine("**シーンを保存しないこと。** 判定が済んだら 6 - Remove Compare で消す");
            log.AppendLine("シーン内だけの一時表示ではない — tint 0.6 の比較用マテリアルを");
            log.AppendLine(CompareFolder + " へ**実アセットとして作る** (Play の domain reload を");
            log.AppendLine("跨がせるため)。6 - Remove Compare がフォルダごと消す");
            log.AppendLine("シーン内の " + CompareRootName + " は**同名なら無条件に消す** — この名前は使わないこと");
            log.AppendLine();

            RemoveCompareInternal(log);

            if (!AssetDatabase.IsValidFolder(CompareFolder))
                AssetDatabase.CreateFolder(ToysRoot, "_Compare");

            var tennis = FindToy("TennisBall");
            Mesh tennisMesh; Matrix4x4 tennisM; string problem;
            if (!ResolveToyMesh(tennis, out tennisMesh, out tennisM, out problem))
            {
                log.AppendLine("FAIL " + problem);
                log.AppendLine("RESULT: FAIL");
                Finish("Logs/imp-fet-009-compare.txt", log);
                return;
            }

            var compareRoot = new GameObject(CompareRootName);
            // IMP-INF-003: 開発専用の一時物なので KD_Debug の下へ。
            // ルートに置くと Scene Layout の orphanKD に出続ける
            KD_SceneObjects.PlaceIfNew(compareRoot, KD_DogDefs.DebugPath);
            compareRoot.transform.position = CompareOrigin(log);

            var defs = new List<KD_DogDef>(KD_DogDefs.All);
            float x = 0f;

            // G0 Control: 今の Sphere ×4 = 合格線
            x = BuildGroup(compareRoot.transform, "G0_Control", x, defs, (parent, def) =>
            {
                BuildMeshNode(parent, BasicBallNode, BuiltinSphereMesh(), Matrix4x4.identity, 1f,
                    AssetDatabase.LoadAssetAtPath<Material>(def.BallMaterialPath), null);
            });

            // G1 A_full: tint 1.0 (実プレハブに当てているもの)
            x = BuildGroup(compareRoot.transform, "G1_A_full", x, defs, (parent, def) =>
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(tennis.TintMaterialPath(def.key));
                if (mat == null) mat = EnsureTintMaterial(tennis, def, TintStrength, tennis.TintMaterialPath(def.key), log);
                BuildMeshNode(parent, tennis.NodeName, tennisMesh, tennisM, tennis.visualScale, mat, null);
            });

            // G2 A_soft: tint 0.6
            x = BuildGroup(compareRoot.transform, "G2_A_soft", x, defs, (parent, def) =>
            {
                var path = CompareFolder + "/M_KD_Cmp_TennisBall_" + def.key + "_soft.mat";
                var mat = EnsureTintMaterial(tennis, def, TintStrengthSoft, path, log);
                BuildMeshNode(parent, tennis.NodeName, tennisMesh, tennisM, tennis.visualScale, mat, null);
            });

            // G3 B_tag: 素の色 + 犬色のバンド (既存 M_KD_Ball_<Key>.mat を流用 = 新規アセット 0)
            var tennisBase = AssetDatabase.LoadAssetAtPath<Material>(tennis.BaseMaterialPath);
            x = BuildGroup(compareRoot.transform, "G3_B_tag", x, defs, (parent, def) =>
            {
                BuildMeshNode(parent, tennis.NodeName, tennisMesh, tennisM, tennis.visualScale, tennisBase, null);
                var tag = new GameObject("KD_ToyTag");
                tag.transform.SetParent(parent.transform, false);
                tag.transform.localScale = new Vector3(1.02f, 0.18f, 1.02f);
                tag.AddComponent<MeshFilter>().sharedMesh = BuiltinSphereMesh();
                tag.AddComponent<MeshRenderer>().sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(def.BallMaterialPath);
            });

            // G4 C_shape: 形だけで見分ける対照 (**素の色**。形単独の読みやすさを見る)
            var shapes = new[] { "TennisBall", "Bone", "Rope", null };
            int shapeIdx = 0;
            x = BuildGroup(compareRoot.transform, "G4_C_shape", x, defs, (parent, def) =>
            {
                string key = shapes[shapeIdx % shapes.Length];
                shapeIdx++;
                if (key == null)
                {
                    BuildMeshNode(parent, BasicBallNode, BuiltinSphereMesh(), Matrix4x4.identity, 1f,
                        AssetDatabase.LoadAssetAtPath<Material>(def.BallMaterialPath), null);
                    return;
                }
                var t = FindToy(key);
                Mesh m; Matrix4x4 mm; string p;
                if (!ResolveToyMesh(t, out m, out mm, out p)) { log.AppendLine("   FAIL " + p); return; }
                BuildMeshNode(parent, t.NodeName, m, mm, t.visualScale,
                    AssetDatabase.LoadAssetAtPath<Material>(t.BaseMaterialPath), null);
            });

            AssetDatabase.SaveAssets();
            log.AppendLine("生成: " + CompareRootName + " (KD_Debug の下に 1 個。手で動かしてよい)");
            log.AppendLine("   原点 " + V(compareRoot.transform.position) + " / 全幅 " + x.ToString("F2") + "m");
            log.AppendLine("   群は左 (+X) から: G0_Control / G1_A_full / G2_A_soft / G3_B_tag / G4_C_shape");
            log.AppendLine("   群内は " + string.Join(" / ", defs.ConvertAll(d => d.key).ToArray()) + " の順、各犬の実 ballDiameter");
            log.AppendLine("   G4 は**素の色**。形単独でどれだけ読めるかの対照 (実プレハブには当てない)");
            log.AppendLine();
            log.AppendLine("Collider・Rigidbody・Udon は一切付けていない (物理と NetworkIDs に触らないため)");
            log.AppendLine("RESULT: PASS — **シーンを保存せずに** Play で 3m / 6m / 10m から見る");
            Finish("Logs/imp-fet-009-compare.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Toy Box/6 - Remove Compare (TEMP)")]
        public static void RemoveCompare()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ToySetup] Remove compare board");
            int removed = RemoveCompareInternal(log);
            if (AssetDatabase.IsValidFolder(CompareFolder))
            {
                AssetDatabase.DeleteAsset(CompareFolder);
                log.AppendLine("deleted folder: " + CompareFolder);
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("removed roots: " + removed);
            log.AppendLine("RESULT: PASS — **シーンは保存しない**");
            Finish("Logs/imp-fet-009-compare-remove.txt", log);
        }

        static int RemoveCompareInternal(StringBuilder log)
        {
            // IMP-INF-003: ルート限定をやめ、シーン全体から引く (取りこぼすと比較盤が二重になる)。
            // 削除しながら探すので、1 個ずつ引き直して打ち止めまで回す
            int n = 0;
            while (true)
            {
                var go = KD_SceneObjects.Find(CompareRootName);
                if (go == null) break;
                Object.DestroyImmediate(go);
                n++;
            }
            if (n > 0 && log != null) log.AppendLine("既存の " + CompareRootName + " を " + n + " 個削除した");
            return n;
        }

        /// <summary>比較盤の置き場所。実照明・実背景で見るのが目的なので、
        /// **既存のボールの近く (庭の地面)** を初期値にする。展示台に浮かせない。</summary>
        static Vector3 CompareOrigin(StringBuilder log)
        {
            // IMP-INF-003: **ここはルート限定のままだと移行で確実に壊れる** —
            // ボールは KD_Props の下へ移るので、ルートを見ている限り 1 個も見つからず
            // 比較盤が原点 (庭の外) へ飛ぶ。シーン全体から名前で引く
            var sum = Vector3.zero; int n = 0;
            foreach (var def in KD_DogDefs.All)
            {
                var ball = KD_SceneObjects.Find(def.BallName);
                if (ball == null) continue;
                sum += ball.transform.position; n++;
            }
            if (n == 0)
            {
                log.AppendLine("   (シーンにボールが見つからないので原点へ置いた。手で動かすこと)");
                return Vector3.zero;
            }
            var p = sum / n;
            p.z -= 1.0f; // ボールの並びと重ならない位置へずらす

            // ボールは地面の上に半径ぶん浮いて静止しているので、その高さが地面そのもの。
            // **上から Raycast しない** — 古民家の屋根・庇に当たって空中へ飛ぶ (実測 y 2.45)
            float avgRadius = 0f; int m = 0;
            foreach (var def in KD_DogDefs.All) { avgRadius += def.ballDiameter * 0.5f; m++; }
            if (m > 0) p.y -= avgRadius / m;
            return p;
        }

        static float BuildGroup(Transform parent, string name, float x, List<KD_DogDef> defs,
            System.Action<GameObject, KD_DogDef> fill)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            group.transform.localPosition = new Vector3(x, 0f, 0f);
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                var item = new GameObject(def.key);
                item.transform.SetParent(group.transform, false);
                // 地面に接するように半径ぶん持ち上げる
                item.transform.localPosition = new Vector3(i * CompareItemGap, def.ballDiameter * 0.5f, 0f);
                item.transform.localScale = Vector3.one * def.ballDiameter; // 識別層 1 を実寸で見る
                fill(item, def);
            }
            return x + (defs.Count - 1) * CompareItemGap + CompareGroupGap;
        }

        // =====================================================================
        // 共通
        // =====================================================================

        /// <summary>
        /// 棚の陳列用に、**実物のメッシュ**を <paramref name="holder"/> の子として作る
        /// (IMP-PRG-002 / 2026-08-23)。
        ///
        /// **正規化の計算をショップ側へ書き写さないための口。** 長軸を 1.0 に揃えて
        /// 中心を原点へ寄せる処理は <see cref="KD_ModelNodeUtil"/> が正本で、
        /// メッシュ頂点の実測に基づく (bounds の 8 隅を回す近似ではない)。
        /// 二重に持つと、片方だけ直したときに棚と実物の見た目がずれる。
        ///
        /// <paramref name="holder"/> の localScale が陳列サイズになる
        /// (長軸 = holder のスケール)。
        ///
        /// 色は**犬ごとの tint ではなく素の材質**を使う — 棚に並ぶのは「商品」であって
        /// 特定の犬のボールではない。実際に出るときは犬ごとに色が付く (識別層 1)。
        /// </summary>
        public static bool BuildShelfDisplay(GameObject holder, string toyKey, out string problem)
        {
            problem = null;
            if (holder == null) { problem = "holder is null"; return false; }

            Mesh mesh; Matrix4x4 matrix; Material mat;
            if (toyKey == BasicBallKey)
            {
                // 基本ボールだけは FBX ではなく組み込み Sphere。
                // 色は代表として 1 匹目のボール材質を借りる (実際は犬ごとに変わる)
                mesh = BuiltinSphereMesh();
                matrix = Matrix4x4.identity;
                mat = AssetDatabase.LoadAssetAtPath<Material>(KD_DogDefs.All[0].BallMaterialPath);
                if (mesh == null) { problem = "builtin Sphere mesh not found"; return false; }
            }
            else
            {
                var toy = FindToy(toyKey);
                if (!ResolveToyMesh(toy, out mesh, out matrix, out problem)) return false;
                mat = AssetDatabase.LoadAssetAtPath<Material>(toy.BaseMaterialPath);
                if (mat == null) { problem = "base material not found: " + toy.BaseMaterialPath; return false; }
            }

            // **期待するもの以外の陳列ノードを畳む** (2026-08-23 Codex 指摘)。
            // 恒久 ID を保ったままモデルを差し替える (`meshKey` だけ変える) のは
            // 通常の移行手順なので、**古いメッシュが残ると新旧が重なって出る**
            string want = MeshNodePrefix + toyKey;
            RemoveOtherMeshNodes(holder, MeshNodePrefix, want);

            return BuildMeshNode(holder, want, mesh, matrix, 1.0f, mat, null);
        }

        /// <summary>棚とショップの両方が使う「基本ボール」の key。</summary>
        public const string BasicBallKey = "BasicBall";

        /// <summary>おもちゃ 1 品の Mesh と合成行列。**変換そのものは
        /// <see cref="KD_ModelNodeUtil"/> が正本** — ここは品目定義から FBX パスを引くだけ。</summary>
        static bool ResolveToyMesh(ToyDef toy, out Mesh mesh, out Matrix4x4 modelMatrix, out string problem)
        {
            mesh = null; modelMatrix = Matrix4x4.identity; problem = null;
            if (string.IsNullOrEmpty(toy.key)) { problem = "unknown toy"; return false; }
            return ResolveMeshFromFbx(toy.FbxPath, out mesh, out modelMatrix, out problem);
        }

        /// <summary>識別層 手 A: 犬色を乗せた派生マテリアル。
        /// Standard の albedo は _MainTex x _Color の**乗算 = 暗くする方向にしか効かない**ので、
        /// 白から犬色へ Lerp した中間色を入れる。色の正本は KD_DogDefs.ballColor (= 首輪と同色)。</summary>
        static Material EnsureTintMaterial(ToyDef toy, KD_DogDef def, float strength, string path, StringBuilder log)
        {
            var baseMat = AssetDatabase.LoadAssetAtPath<Material>(toy.BaseMaterialPath);
            if (baseMat == null)
            {
                if (log != null) log.AppendLine("   FAIL base material not found: " + toy.BaseMaterialPath);
                return null;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                if (!AssetDatabase.CopyAsset(toy.BaseMaterialPath, path))
                {
                    if (log != null) log.AppendLine("   FAIL CopyAsset -> " + path);
                    return null;
                }
                AssetDatabase.ImportAsset(path);
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (log != null) log.AppendLine("   created " + path);
            }
            if (mat == null) return null;
            mat.color = Color.Lerp(Color.white, def.ballColor, strength);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Mesh BuiltinSphereMesh()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return mesh;
        }

        static string DescribePivotFace(Bounds b)
        {
            var sb = new StringBuilder();
            string[] axis = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                float size = b.size[i];
                if (size <= 1e-6f) continue;
                float lo = b.min[i] / size, hi = b.max[i] / size;
                string where = Mathf.Abs(lo) < 0.02f ? "min≈0 (底面ピボット)"
                    : Mathf.Abs(hi) < 0.02f ? "max≈0"
                    : Mathf.Abs(b.center[i]) / size < 0.02f ? "中心"
                    : "オフセット";
                sb.Append(axis[i] + ": " + where + "  ");
            }
            return sb.ToString();
        }

        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
