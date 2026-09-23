using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-DOG-005: 犬の骨格を**実測で解決する**。犬種ごとの決め打ちを持たない。
    ///
    /// 背景 (2026-08-13 実測 — 3 犬種の `_MA` プレハブ):
    ///
    /// | | コーギー | もちぽめ | ラブラドール |
    /// |---|---|---|---|
    /// | 骨格 | 2 段 `inu>ko-gi-` + `inu>アーマチュア` | **1 段 `inu>アーマチュア` のみ** | 2 段 `inu>ラブラドール` + `inu>アーマチュア` |
    /// | RotationConstraint | 28 | **0** | 28 |
    /// | 耳 PhysBone | あり | **なし** | **なし** |
    /// | 尻尾 PB ルート | `Bone.020` | **`Bone.019`** | `Bone.020` |
    ///
    /// つまり `ko-gi-` 決め打ちは 3 犬種で通らず、設計書 §4 の
    /// 「Constraint のソース側へ書く」もポメでは対象が無い。
    ///
    /// **書き先の規則を 1 本にする** (<see cref="WriteBone"/>):
    /// - 2 段構え → メッシュ側ボーンの `RotationConstraint` ソース (従来どおり)
    /// - 1 段構え → **そのボーン自身**
    ///
    /// これで機構は破綻しない。VER-DOG-001 の実測は
    /// 「`LateUpdate` は Animator には勝つ / Constraint には負ける」であり、
    /// Constraint が無い犬は*より単純な*ケースに落ちるだけ。
    ///
    /// 解決できなかったものは <see cref="problems"/> に積む。**黙って別のボーンへ
    /// 配線するくらいなら止まって知らせる** (Setup 側が FAIL を出す)。
    /// </summary>
    public class KD_DogRig
    {
        // ---- 骨格 ----
        public GameObject dog;
        public Transform inu;           // hojo_born/Bone/Bone_end/PB3/inu
        public Transform meshSkelRoot;  // 2 段構えのメッシュ側 (ko-gi- / ラブラドール)。1 段なら null
        public Transform srcSkelRoot;   // inu/アーマチュア

        public bool TwoTier { get { return meshSkelRoot != null; } }
        /// <summary>ボーンを名前で探す起点 (2 段ならメッシュ側 / 1 段ならアーマチュア)</summary>
        public Transform BoneRoot { get { return meshSkelRoot != null ? meshSkelRoot : srcSkelRoot; } }

        // ---- 触る/見るボーン (すべてメッシュ側 = 表示に追従する側) ----
        public Transform head;    // Bone.004
        public Transform nose;    // Bone.007
        public Transform chin;    // Bone.006
        public Transform cheekL;  // Bone.009_L
        public Transform cheekR;  // Bone.009_R
        public Transform earL;    // Bone.008_L
        public Transform earR;    // Bone.008_R

        // ---- 書き先 (追加回転を書くボーン) ----
        public Transform headWriteBone;  // 設計書 §4 案 D の書き先
        public Transform tailWriteBone;  // 尻尾 PB ルートの「親」の書き先

        // ---- 実測した性質 ----
        public VRCPhysBone tailPhysBone;
        /// <summary>IMP-DOG-020: 尻尾 PB の有無。無くても <see cref="Ok"/> は落ちない
        /// (Setup が足す前のプレハブでも解決できるように)。**Verify がこれを数える。**</summary>
        public bool HasTailPhysBone { get { return tailPhysBone != null; } }
        public bool hasEarPhysBone;
        public SkinnedMeshRenderer face;   // metozi/egao を持つ karada
        public int metoziIndex = -1;
        public int egaoIndex = -1;
        /// <summary>頭の代表寸法 = |鼻 - 頭| (犬ローカル)。アンカーのオフセットはこれに比例させる。
        /// **体格ではない** — 頭と体幹の比は犬種で揃わない (ポメは小さい体に大きな頭)。
        /// 体格に比例させたいものには <see cref="bodyScale"/> を使う (2026-08-13 IMP-DOG-006 実測)</summary>
        public float headScale;

        /// <summary>体幹の代表寸法 = |頭 - 尻尾根元| (犬ローカル)。
        /// 実測 コーギー 0.4088 / ポメ 0.2348 / ラブ 0.6657。
        /// **移動速度・停止距離・ルート変位のしきい値はこちらに比例させる**</summary>
        public float bodyScale;

        /// <summary>体格比 (コーギー = 1.0)。しきい値の相対化に使う</summary>
        public float BodyRatio
        {
            get { return bodyScale > 0.001f ? bodyScale / CorgiTrunk : 1f; }
        }

        public readonly List<string> problems = new List<string>();
        public bool Ok { get { return problems.Count == 0; } }

        // ================================================================
        // アンカーのオフセット (headScale 倍)
        //
        // 値はコーギーの実測絶対値 (IMP-PET-002 / BUG-PET-001 / IMP-FET-001) を
        // コーギーの headScale = 0.07229 m で割った比。コーギーでは比が 1 になるので
        // **従来と同じ位置に置かれる = 機能等価**。
        //
        // 他犬種では近似でしかない。**体格が違えば目視で詰める前提**で、
        // 006/007 の実機確認に「アンカー位置の目視」を含める。
        // ここで狙うのは精度ではなく「初期値がだいたい合っていて手で直せる状態」。
        //
        // なお Setup は「既存アンカーの位置は触らない」ので、この値が効くのは新規作成時だけ。
        // ================================================================

        public const float CorgiHeadScale = 0.07229f;
        /// <summary>コーギーの体幹長 (2026-08-13 実測)。<see cref="BodyRatio"/> の基準</summary>
        public const float CorgiTrunk = 0.4088f;

        static readonly Vector3 HeadZoneRatio = new Vector3(0f, 0.84382f, 0.67783f);
        static readonly Vector3 CheekLRatio = new Vector3(-0.27666f, 0f, 0.13833f);
        static readonly Vector3 CheekRRatio = new Vector3(0.27666f, 0f, 0.13833f);
        static readonly Vector3 ChinRatio = new Vector3(0f, -0.48416f, 0.27666f);
        static readonly Vector3 MouthRatio = new Vector3(0f, -0.13833f, 0.82999f);
        /// <summary>おでこの代表点 (頭ボーンからの犬ローカル差)。
        /// BUG-PET-001 の「おでこが HEAD として拾えるか」を犬種非依存に判定するために使う</summary>
        static readonly Vector3 ForeheadRatio = new Vector3(0f, 0.85903f, 1.26435f);

        public Vector3 HeadZoneOffset { get { return HeadZoneRatio * headScale; } }
        public Vector3 CheekOffsetL { get { return CheekLRatio * headScale; } }
        public Vector3 CheekOffsetR { get { return CheekRRatio * headScale; } }
        public Vector3 ChinOffset { get { return ChinRatio * headScale; } }
        public Vector3 MouthOffset { get { return MouthRatio * headScale; } }

        /// <summary>おでこのワールド座標 (Verify 用)。</summary>
        public Vector3 ForeheadPoint
        {
            get { return head.position + dog.transform.TransformVector(ForeheadRatio * headScale); }
        }

        /// <summary>Verify の位置照合に使う許容 (体格に比例)。</summary>
        public float PositionTolerance { get { return headScale * 0.5f; } }

        // ================================================================ 解決

        public static KD_DogRig Resolve(GameObject dog)
        {
            var rig = new KD_DogRig();
            rig.dog = dog;
            if (dog == null)
            {
                rig.problems.Add("dog GameObject is null");
                return rig;
            }

            // ---- 骨格の親 (3 犬種とも hojo_born/Bone/Bone_end/PB3/inu) ----
            rig.inu = FindDescendant(dog.transform, "inu");
            if (rig.inu == null)
            {
                rig.problems.Add("'inu' ノードが見つからない (骨格の親)");
                return rig;
            }

            // ---- アーマチュア (ソース側。全犬種に必ずある) ----
            foreach (Transform c in rig.inu)
                if (c.name == "アーマチュア") { rig.srcSkelRoot = c; break; }
            if (rig.srcSkelRoot == null)
            {
                rig.problems.Add("inu/アーマチュア が見つからない");
                return rig;
            }

            // ---- メッシュ側骨格 = inu の子で、アーマチュアでなく、'Bone' を子に持つもの ----
            // (コーギー: ko-gi- / ラブラドール: ラブラドール / ポメ: 該当なし = 1 段構え)
            int meshCandidates = 0;
            foreach (Transform c in rig.inu)
            {
                if (c == rig.srcSkelRoot) continue;
                if (c.Find("Bone") == null) continue;
                meshCandidates++;
                if (rig.meshSkelRoot == null) rig.meshSkelRoot = c;
            }
            if (meshCandidates > 1)
            {
                rig.problems.Add("メッシュ側骨格の候補が " + meshCandidates +
                    " 個ある (1 個が前提)。inu の直下を確認する");
                return rig;
            }

            // ---- ボーン (メッシュ側 = 表示に追従する側から名前 + 親名で引く) ----
            rig.head = rig.Bone("Bone.004", "Bone.003");
            rig.nose = rig.Bone("Bone.007", "Bone.004");
            rig.chin = rig.Bone("Bone.006", "Bone.004");
            rig.cheekL = rig.Bone("Bone.009_L", "Bone.004");
            rig.cheekR = rig.Bone("Bone.009_R", "Bone.004");
            // 耳は親名を見ない (v2 の挿入親が残っていても拾えるように — KD_PetSetupEditor と同じ理由)
            rig.earL = rig.Bone("Bone.008_L", null);
            rig.earR = rig.Bone("Bone.008_R", null);

            if (rig.head == null) rig.problems.Add("頭ボーン Bone.004 (親 Bone.003) が見つからない");
            if (rig.nose == null) rig.problems.Add("鼻ボーン Bone.007 (親 Bone.004) が見つからない");
            if (rig.chin == null) rig.problems.Add("顎ボーン Bone.006 (親 Bone.004) が見つからない");
            if (rig.cheekL == null || rig.cheekR == null)
                rig.problems.Add("頬ボーン Bone.009_L/R が見つからない");
            if (rig.earL == null || rig.earR == null)
                rig.problems.Add("耳ボーン Bone.008_L/R が見つからない");

            // ---- 体格 (犬ローカルで測る = 犬のスケールに依存しない) ----
            if (rig.head != null && rig.nose != null)
            {
                Vector3 h = dog.transform.InverseTransformPoint(rig.head.position);
                Vector3 n = dog.transform.InverseTransformPoint(rig.nose.position);
                rig.headScale = (n - h).magnitude;
                if (rig.headScale < 0.001f)
                    rig.problems.Add("headScale が 0 に近い (頭と鼻のボーンが同じ位置)");
            }

            // ---- 書き先 ----
            if (rig.head != null) rig.headWriteBone = rig.WriteBone(rig.head, "頭");

            // ---- 尻尾: PB ルートの「親」が書き先 ----
            // 3 犬種で PB ルートが違う (コーギー/ラブ Bone.020 / ポメ Bone.019) ため、
            // 名前ではなく「尻尾チェーン (Bone.018 の子孫) 上の PhysBone」で見つける
            var tailBase = rig.Bone("Bone.018", null);

            // ---- 体幹 (犬ローカルで測る)。移動・停止距離・ルート変位の基準 ----
            if (tailBase != null && rig.head != null)
            {
                Vector3 h = dog.transform.InverseTransformPoint(rig.head.position);
                Vector3 t = dog.transform.InverseTransformPoint(tailBase.position);
                rig.bodyScale = (h - t).magnitude;
                if (rig.bodyScale < 0.001f)
                    rig.problems.Add("bodyScale が 0 に近い (頭と尻尾根元が同じ位置)");
            }

            if (tailBase == null)
            {
                rig.problems.Add("尻尾の根元 Bone.018 が見つからない");
            }
            else
            {
                foreach (var pb in rig.BoneRoot.GetComponentsInChildren<VRCPhysBone>(true))
                {
                    var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                    if (!IsDescendantOf(root, tailBase) && root != tailBase) continue;
                    rig.tailPhysBone = pb;
                    break;
                }
                if (rig.tailPhysBone == null)
                {
                    // IMP-DOG-020: **尻尾 PB が無いことは解決の失敗ではない。**
                    // 柴・サモエドの購入プレハブは PhysBone が 0 本で (2026-09-05 実測)、
                    // 尻尾 PB は Setup が足す (犬定義 `addTailPhysBone`)。ここで problem にすると
                    // Build / Place (プレハブから解決する) が足す前に落ちて手順が進まない。
                    // 代わりに <see cref="HasTailPhysBone"/> を Verify (Pet System/3 / Dog Unit/1) が数える。
                    // tailWriteBone は null のまま — 足した後に解決し直せば決まる
                }
                else
                {
                    var pbRoot = rig.tailPhysBone.rootTransform != null
                        ? rig.tailPhysBone.rootTransform : rig.tailPhysBone.transform;
                    if (pbRoot.parent == null)
                        rig.problems.Add("尻尾 PhysBone ルートに親が無い");
                    else
                        rig.tailWriteBone = rig.WriteBone(pbRoot.parent, "尻尾");
                }
            }

            // ---- 耳 PhysBone の有無 (コーギーだけ持つ。判断は IMP-DOG-006/007) ----
            rig.hasEarPhysBone = rig.earL != null && rig.earL.GetComponent<VRCPhysBone>() != null
                              && rig.earR != null && rig.earR.GetComponent<VRCPhysBone>() != null;

            // ---- 表情 ----
            rig.face = ResolveFace(dog, rig.problems);
            if (rig.face != null && rig.face.sharedMesh != null)
            {
                rig.metoziIndex = rig.face.sharedMesh.GetBlendShapeIndex("metozi");
                rig.egaoIndex = rig.face.sharedMesh.GetBlendShapeIndex("egao");
                if (rig.metoziIndex < 0 || rig.egaoIndex < 0)
                    rig.problems.Add("BlendShape metozi/egao の index が引けない");
            }

            return rig;
        }

        // ================================================================ 部品

        /// <summary>メッシュ側骨格から名前 (+ 親名) でボーンを引く。parentName が null なら親を見ない。</summary>
        public Transform Bone(string name, string parentName)
        {
            if (BoneRoot == null) return null;
            foreach (var t in BoneRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                if (parentName != null && (t.parent == null || t.parent.name != parentName)) continue;
                return t;
            }
            return null;
        }

        /// <summary>
        /// 追加回転の書き先を返す。
        /// **2 段構え** → メッシュ側ボーンの `RotationConstraint` ソース (設計書 §4 案 D)。
        /// **1 段構え** → そのボーン自身 (競合する書き手が居ないので直接書ける)。
        /// </summary>
        public Transform WriteBone(Transform meshBone, string label)
        {
            if (meshBone == null) return null;
            if (!TwoTier) return meshBone;

            var rc = meshBone.GetComponent<RotationConstraint>();
            if (rc == null)
            {
                // 2 段構えなのに Constraint が無い = 想定外。黙って直書きすると
                // メッシュ側がソースに上書きされて効かない。止めて知らせる
                problems.Add(label + ": 2 段構えだが " + meshBone.name +
                    " に RotationConstraint が無い (書き先が決まらない)");
                return null;
            }
            if (rc.sourceCount != 1)
            {
                problems.Add(label + ": " + meshBone.name +
                    " の RotationConstraint ソースが " + rc.sourceCount + " 個 (1 個が前提)");
                return null;
            }
            var src = rc.GetSource(0).sourceTransform;
            if (src == null || !IsDescendantOf(src, srcSkelRoot))
            {
                problems.Add(label + ": " + meshBone.name +
                    " の Constraint ソースがアーマチュア配下でない");
                return null;
            }
            if (!rc.constraintActive || rc.weight < 0.99f || rc.GetSource(0).weight < 0.99f)
            {
                problems.Add(label + ": " + meshBone.name +
                    " の RotationConstraint が無効か weight が 1 未満 (書いても伝播しない)");
                return null;
            }
            return src;
        }

        /// <summary>metozi/egao を持つ Renderer。複数候補なら karada 優先、決まらなければ null。</summary>
        static SkinnedMeshRenderer ResolveFace(GameObject dog, List<string> problems)
        {
            SkinnedMeshRenderer first = null, named = null;
            int count = 0;
            foreach (var smr in dog.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                if (mesh.GetBlendShapeIndex("metozi") < 0 || mesh.GetBlendShapeIndex("egao") < 0) continue;
                count++;
                if (first == null) first = smr;
                if (smr.name == "karada" && named == null) named = smr;
            }
            if (count == 0)
            {
                problems.Add("metozi/egao を持つ SkinnedMeshRenderer が無い");
                return null;
            }
            if (named != null) return named;
            if (count == 1) return first;
            problems.Add("metozi/egao を持つ Renderer が " + count + " 個あり karada が無い (曖昧)");
            return null;
        }

        public static Transform FindDescendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        public static bool IsDescendantOf(Transform t, Transform ancestor)
        {
            if (t == null || ancestor == null) return false;
            for (var u = t.parent; u != null; u = u.parent)
                if (u == ancestor) return true;
            return false;
        }

        /// <summary>ログ用の 1 行サマリ。</summary>
        public string Summary()
        {
            return string.Format(
                "{0}: {1} (mesh={2}) headScale={3:F4} body={8:F4} (x{9:F2}) tailPB={4} earPB={5} face={6} problems={7}",
                dog == null ? "null" : dog.name,
                TwoTier ? "2 段構え" : "1 段構え",
                meshSkelRoot == null ? "-" : meshSkelRoot.name,
                headScale,
                tailPhysBone == null ? "-" :
                    (tailPhysBone.rootTransform != null ? tailPhysBone.rootTransform.name : tailPhysBone.transform.name),
                hasEarPhysBone ? "あり" : "なし",
                face == null ? "-" : face.name,
                problems.Count,
                bodyScale,
                BodyRatio);
        }
    }
}
