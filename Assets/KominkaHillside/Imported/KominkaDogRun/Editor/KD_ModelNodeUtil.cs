using System.Text;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// **FBX 1 本を「見た目だけの子ノード 1 個」へ畳む変換。**
    /// おもちゃにもおやつにも依存しない (IMP-TRT-001 Phase 4b Step 3b で切り出した)。
    ///
    /// ## なぜ正規化が要るのか
    ///
    /// Tripo から出した FBX は**3 つが同時に立つ**:
    ///
    ///   1. ピボットが底面 (口元や棚へ置くと半径ぶん浮く)
    ///   2. 軸が Z-up (Model ノードに Lcl Rotation が残る)
    ///   3. 非等方 (正規化スケールを root に入れると root の契約が壊れる)
    ///
    /// → **正規化・再センタリング・軸補正を子 Transform 1 個に閉じ込める。**
    /// root の localScale / Collider / Rigidbody / 同期の契約値には一切触らない。
    ///
    /// ## ここが正本。**呼び手側へ書き写さない**
    ///
    /// 長軸を 1.0 へ揃えて中心を原点へ寄せる処理は**メッシュ頂点の実測**に基づく
    /// (bounds の 8 隅を回す近似ではない)。二重に持つと、片方だけ直したときに
    /// 「棚の陳列」と「実際に出るもの」の見た目が黙ってずれる。
    ///
    /// 使い手:
    ///   <see cref="KD_ToySetupEditor"/>   ボールプレハブの見た目ノード / 棚の陳列
    ///   <see cref="KD_ShopSetupEditor"/>  おやつの棚の陳列
    /// </summary>
    public static class KD_ModelNodeUtil
    {
        /// <summary>FBX の Mesh と、モデル階層がメッシュに掛けている合成行列を取り出す。
        /// **軸変換もスケールもハードコードしない** — importer が実際にどこへ置いたかを実測する。</summary>
        public static bool ResolveMeshFromFbx(string fbxPath, out Mesh mesh,
            out Matrix4x4 modelMatrix, out string problem)
        {
            mesh = null; modelMatrix = Matrix4x4.identity; problem = null;
            if (string.IsNullOrEmpty(fbxPath)) { problem = "fbx path is empty"; return false; }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) { problem = "fbx not found: " + fbxPath; return false; }
            var mf = model.GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null)
            { problem = "MeshFilter/Mesh not found in " + fbxPath; return false; }
            mesh = mf.sharedMesh;

            // モデルルートまで遡って合成する (ルート自身の Transform も含める —
            // importer が軸変換をルートへ置くことがあるため)
            for (var t = mf.transform; t != null; t = t.parent)
                modelMatrix = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * modelMatrix;
            return true;
        }

        /// <summary>子 Transform を算出する。
        /// メッシュ頂点を実際に <paramref name="modelMatrix"/> で変換して AABB を取るので、
        /// 回転が軸そろえでなくても膨らまない (bounds の 8 隅を回す近似ではない)。</summary>
        public static bool ComputeChildTransform(Mesh mesh, Matrix4x4 modelMatrix, float visualScale,
            out Vector3 pos, out Quaternion rot, out Vector3 scale,
            out Vector3 measuredCenter, out Vector3 measuredSize, out string problem)
        {
            pos = Vector3.zero; rot = Quaternion.identity; scale = Vector3.one;
            problem = null;
            MeasureMeshUnder(mesh, modelMatrix, out measuredCenter, out measuredSize);

            float longest = Mathf.Max(measuredSize.x, Mathf.Max(measuredSize.y, measuredSize.z));
            if (longest <= 1e-6f) { problem = "mesh size is degenerate: " + V(measuredSize); return false; }

            // **分解可能性を「実際に組み直して照合する」ことで確かめる。**
            // lossyScale の等方性だけを見るのでは足りない — 祖先で回転と非等方スケールが
            // 混ざるとシアーになり、負スケール (鏡像) では rotation/lossyScale から
            // 線形部を再現できない。どちらの場合も pos の中心合わせが黙って崩れるので、
            // 「Transform 1 個に畳めるか」を直接検査して畳めなければ落とす
            var ls = modelMatrix.lossyScale;
            rot = modelMatrix.rotation;
            var rebuilt = Matrix4x4.TRS(Vector3.zero, rot, ls);
            float worst = 0f;
            for (int c = 0; c < 3; c++)
                for (int r = 0; r < 3; r++)
                    worst = Mathf.Max(worst, Mathf.Abs(rebuilt[r, c] - modelMatrix[r, c]));
            float span = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
            if (span <= 1e-6f || worst > span * 0.001f)
            {
                problem = "model matrix を Transform 1 個へ畳めない (rotation x scale に分解できない — " +
                          "シアー / 負スケール / 非等方が疑わしい。lossyScale " + V(ls) +
                          " / 最大誤差 " + worst.ToString("F6") + ")";
                rot = Quaternion.identity;
                return false;
            }

            float k = visualScale / longest;
            scale = ls * k;
            pos = -k * measuredCenter;
            return true;
        }

        /// <summary>メッシュ頂点を行列で変換した AABB。実測なので近似が入らない。</summary>
        public static void MeasureMeshUnder(Mesh mesh, Matrix4x4 m, out Vector3 center, out Vector3 size)
        {
            var verts = mesh.vertices;
            if (verts.Length == 0) { center = Vector3.zero; size = Vector3.zero; return; }
            var min = m.MultiplyPoint3x4(verts[0]);
            var max = min;
            for (int i = 1; i < verts.Length; i++)
            {
                var p = m.MultiplyPoint3x4(verts[i]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            center = (min + max) * 0.5f;
            size = max - min;
        }

        /// <summary>見た目の子ノードを作る (無ければ生成、あれば上書き)。
        /// **root には一切触らない。**</summary>
        public static bool BuildMeshNode(GameObject root, string nodeName, Mesh mesh, Matrix4x4 modelMatrix,
            float visualScale, Material mat, StringBuilder log)
        {
            Vector3 pos, scale, c, s; Quaternion rot; string problem;
            if (!ComputeChildTransform(mesh, modelMatrix, visualScale,
                    out pos, out rot, out scale, out c, out s, out problem))
            {
                if (log != null) log.AppendLine("   FAIL " + nodeName + ": " + problem);
                return false;
            }

            Transform t = root.transform.Find(nodeName);
            GameObject node;
            if (t == null)
            {
                node = new GameObject(nodeName);
                node.transform.SetParent(root.transform, false);
            }
            else node = t.gameObject;

            node.layer = root.layer;
            node.transform.localPosition = pos;
            node.transform.localRotation = rot;
            node.transform.localScale = scale;

            var mf = node.GetComponent<MeshFilter>();
            if (mf == null) mf = node.AddComponent<MeshFilter>();
            var mr = node.GetComponent<MeshRenderer>();
            if (mr == null) mr = node.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = mat;

            if (log != null)
                log.AppendLine("   " + nodeName + ": pos " + V(pos) + " / rot " + V(rot.eulerAngles) +
                               " / scale " + V(scale) + " / mat " + (mat == null ? "null" : mat.name));
            return true;
        }

        /// <summary>ログ用のベクタ整形。**桁を揃えて実測値を読めるようにするためだけ。**</summary>
        public static string V(Vector3 v)
        {
            return string.Format("({0:F4}, {1:F4}, {2:F4})", v.x, v.y, v.z);
        }

        /// <summary>期待する 1 個以外の見た目ノードを畳む。
        ///
        /// 恒久 ID を保ったままモデルを差し替える (`meshKey` だけ変える) のは通常の移行手順なので、
        /// **古いメッシュが残ると新旧が重なって出る** (2026-08-23 Codex 指摘)。</summary>
        public static void RemoveOtherMeshNodes(GameObject holder, string prefix, string keepName)
        {
            if (holder == null) return;
            for (int i = holder.transform.childCount - 1; i >= 0; i--)
            {
                var c = holder.transform.GetChild(i);
                if (!c.name.StartsWith(prefix) || c.name == keepName) continue;
                Object.DestroyImmediate(c.gameObject);
            }
        }
    }
}
