using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-INF-003: シーン内オブジェクトの「探す」を 1 箇所へ集める。
    ///
    /// **名前で引く。パスでは引かない。**
    ///
    /// 名前は <see cref="KD_DogDefs"/> が正本として持っていて、引かれるものはシーン内で一意。
    /// パスで引くと「どこに置くか」が 11 個の Setup へ再び染み出し、置き場所を変えるたびに
    /// 全ツールを直すことになる。`Dog Unit/4 - Verify Wiring Intact` が名前にもパスにも
    /// 依存しないおかげで IMP-DOG-005 の一括改名を素通りしたのが手本 —
    /// **依存する構造は少ないほどよい。**
    ///
    /// 以前は 11 ファイルに `FindSceneRoot` (= `GetRootGameObjects()` だけを見る) が
    /// byte 単位で複製されていた。全体検索へ変えたことで**検出力は上がっている** —
    /// 別の親の下に紛れた同名オブジェクトは、以前は 1 個も見えていなかった。
    /// </summary>
    public static class KD_SceneObjects
    {
        /// <summary>アクティブシーン**全体**から名前で引く (**非アクティブも含む**)。
        /// 同名の個数も返す — 呼び手は従来どおり count == 1 を検査する。
        ///
        /// **一意な名前にだけ使う。** `KD_WanderArea` / `KD_PetHearts` / `KD_PetHearts2` /
        /// `KD_PetAnchor_*` / `KD_BarkSource` / `KD_FetchAnchor_Mouth` は犬 4 匹ぶんあるので、
        /// これらは従来どおり親からの `transform.Find` で引くこと
        /// (ここへ渡すと count=4 になり呼び手が FAIL にする)。</summary>
        public static GameObject Find(string name, out int count)
        {
            GameObject first = null;
            count = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != name) continue;
                    count++;
                    if (first == null) first = t.gameObject;
                }
            }
            return first;
        }

        /// <summary>個数を要らないとき用。**`GameObject.Find` の代わりに使う** —
        /// あちらは非アクティブを取りこぼすので、`KD_Debug` をまとめて非アクティブに
        /// した瞬間に Detach 系メニューが黙って no-op になる。</summary>
        public static GameObject Find(string name)
        {
            int ignored;
            return Find(name, out ignored);
        }

        /// <summary>プレハブの実体をシーン**全体**から探す (名前が付いていないものの引き取り用)。
        ///
        /// <paramref name="skip"/> が true を返したものは飛ばす (別の犬が既に確保している等)。
        /// 見つかるのは**最も外側のプレハブインスタンスのルート**だけ。</summary>
        public static GameObject FindByPrefabSource(Object prefab, System.Func<GameObject, bool> skip)
        {
            if (prefab == null) return null;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    if (PrefabUtility.GetCorrespondingObjectFromSource(go) != prefab) continue;
                    if (PrefabUtility.GetOutermostPrefabInstanceRoot(go) != go) continue;
                    if (skip != null && skip(go)) continue;
                    return go;
                }
            }
            return null;
        }

        // ================================================================ 置く
        //
        // **役割分担 (IMP-INF-003 の核心。破ると冪等性が壊れる):**
        //
        //   Setup   = 作るだけ。既存の親も位置も絶対に触らない。
        //   Layout  = 動かすだけ。作りはしない (コンテナを除く)。
        //   Verify  = 数えるだけ。
        //
        // だから Setup を何度流しても、人が意図して動かしたものを引き戻さないし、
        // 既に正しい場所にあるものを入れ子にし直すこともない。
        // 置き場所の是正は `Scene Layout/2 - Apply` の専任。

        /// <summary>`A/B/C` を掘る。既にあれば作らない。
        ///
        /// **Transform は毎回単位元へ揃える。** ここは「フォルダ」であって配置ではない —
        /// コンテナが動くと配下の <c>VRCObjectSync</c> の Respawn 先
        /// (= シーン開始時の world 位置) が全部ずれ、ベイク済みギャラリーの位置も動く。
        /// 「Setup は人の調整を潰さない」の意図的な例外で、<see cref="KD_DogDefs"/> が
        /// 触り心地 4 値をコード側の正本にしたのと同じ理屈 (人が決める値ではない)。</summary>
        public static Transform EnsureContainer(string path)
        {
            Transform parent = null;
            foreach (var seg in path.Split('/'))
            {
                Transform next = null;
                if (parent == null)
                {
                    foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                        if (root.name == seg) { next = root.transform; break; }
                }
                else
                {
                    next = parent.Find(seg);
                }

                if (next == null)
                {
                    var go = new GameObject(seg);
                    Undo.RegisterCreatedObjectUndo(go, "Create container " + seg);
                    next = go.transform;
                    if (parent != null)
                        Undo.SetTransformParent(next, parent, "Create container " + seg);
                }

                next.localPosition = Vector3.zero;
                next.localRotation = Quaternion.identity;
                next.localScale = Vector3.one;
                parent = next;
            }
            return parent;
        }

        /// <summary>作りたてのオブジェクト (<c>new GameObject</c> / <c>InstantiatePrefab</c> /
        /// <c>CreatePrimitive</c> の直後) をコンテナへ入れる。**Unpack しない。**
        ///
        /// **既に親がいるものには触らない。** Setup 各所は「名前で引く → 無ければ作る」の
        /// 形なので、これを呼ぶのは必ず「今作った」場合だけ — つまり
        /// **Setup 再実行が人の配置を引き戻さない**ことがこの 1 行で保証される。
        ///
        /// <c>worldPositionStays: true</c> だが、コンテナは <see cref="EnsureContainer"/> が
        /// 単位元へ強制しているので local Transform は変わらない。</summary>
        public static void PlaceIfNew(GameObject go, string containerPath)
        {
            if (go == null || go.transform.parent != null) return;
            Undo.SetTransformParent(go.transform, EnsureContainer(containerPath), true, "Place " + go.name);
        }

        // ================================================================ 検査
        //
        // `transform.parent == null` の置き換え。
        // **あの表明が守っていたのは「シーン直下であること」ではない** —
        // ①購入プレハブへ子を足していないこと ②動く親の下に居ないこと の 2 つ。

        /// <summary>いずれかの犬 (購入プレハブ) の**子孫**か。
        ///
        /// ループの中で呼ぶときは <paramref name="dogs"/> を外で 1 回作って渡すこと
        /// (省略するとシーンを犬の数だけ歩き直す)。</summary>
        public static bool IsUnderAnyDog(Transform t, Transform[] dogs = null)
        {
            if (t == null) return false;
            if (dogs == null) dogs = AllDogTransforms();
            foreach (var d in dogs)
                if (d != null && d != t && t.IsChildOf(d)) return true;
            return false;
        }

        /// <summary>シーン内の犬 4 匹の Transform (見つからない / 重複しているものは null)。</summary>
        public static Transform[] AllDogTransforms()
        {
            var defs = KD_DogDefs.All;
            var outv = new Transform[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                int c;
                var go = Find(defs[i].DogName, out c);
                outv[i] = (c == 1 && go != null) ? go.transform : null;
            }
            return outv;
        }

        /// <summary>先祖がすべて「動かないコンテナ」か。
        ///
        /// <c>VRCObjectSync</c> の Respawn 先はシーン開始時の world 位置なので、
        /// 親が動く (Transform が単位元でない / Animator・Animation・Rigidbody・
        /// UdonBehaviour が載っている / プレハブインスタンスの一部) と
        /// **Respawn の意味が黙って変わる。**
        /// ベイク済みギャラリーの配置も同じ理由で守る必要がある。</summary>
        public static bool HasStaticAncestry(GameObject go, out string offender)
        {
            offender = null;
            if (go == null) return false;
            for (var t = go.transform.parent; t != null; t = t.parent)
            {
                if (t.localPosition != Vector3.zero
                    || t.localRotation != Quaternion.identity
                    || t.localScale != Vector3.one)
                { offender = PathOf(t.gameObject) + " (Transform が単位元でない)"; return false; }

                if (t.GetComponent<Animator>() != null
                    || t.GetComponent<Animation>() != null
                    || t.GetComponent<Rigidbody>() != null
                    || t.GetComponent<VRC.Udon.UdonBehaviour>() != null)
                { offender = PathOf(t.gameObject) + " (動かしうるコンポーネントが載っている)"; return false; }

                if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject))
                { offender = PathOf(t.gameObject) + " (プレハブインスタンスの一部)"; return false; }
            }
            return true;
        }

        /// <summary>ログ用のシーンパス。</summary>
        public static string PathOf(GameObject go)
        {
            if (go == null) return "(null)";
            var s = go.name;
            for (var t = go.transform.parent; t != null; t = t.parent) s = t.name + "/" + s;
            return s;
        }
    }
}
