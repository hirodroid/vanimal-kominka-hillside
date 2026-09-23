using System.IO;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-NET-005: 犬の位置同期を Owner 権威 + <c>VRCObjectSync</c> へ移す
    /// ([DES-NET-002](../../../docs/development/issues/DES-NET-002-dog-sync-method-v2.md) の実装)。
    ///
    /// 実行順:
    ///   1 - Survey Dog Sync            (書き込み 0。**適用前に仮定を実測で潰す**)
    ///   2 - Apply Owner Authoritative  (dogRoot 4 個へ VRCObjectSync)
    ///   3 - Verify Dog Sync            (契約検査)
    ///   4 - Revert                     (VRCObjectSync 除去)
    ///
    /// **子を 1 つも足さない。** dogRoot は購入アセットのプレハブインスタンスだが、
    /// **コンポーネントの追加は Unpack を要求しない** (IMP-INF-003 で実証済み)。
    ///
    /// ## なぜ Fetch / Stop Anchor の Setup へ足さないのか
    ///
    /// あちらの契約は「作るだけ / 既存の親も位置も触らない」(IMP-INF-003 の役割分担)。
    /// こちらは**購入プレハブのインスタンスへ意図してコンポーネントを足す可逆な変換**で、
    /// 混ぜると向こうの冪等性の意味が壊れる。かつ **既存 Verify を無改造のまま
    /// 回帰ゲートに使いたい** — 変えていない検査が通ることが「壊していない」最強の証拠。
    /// </summary>
    public static class KD_DogSyncEditor
    {
        const string LogFile = "Logs/imp-net-005-dog-sync.txt";

        // ================================================================ 1 - Survey

        /// <summary>適用前に、計画が置いている仮定を**実測で潰す**。書き込みは一切しない。
        ///
        /// 潰したい仮定は 6 つ:
        ///   (a) dogRoot に UdonBehaviour が 1 つも載っていない (Manual 同居問題が無い)
        ///   (b) dogRoot に Rigidbody が無い (あると ObjectSync が物理同期モードへ入る)
        ///   (c) dogRoot の先祖がすべて単位元の「動かないコンテナ」
        ///   (d) Animator のクリップに**ルート位置カーブ**が無い
        ///       (applyRootMotion=false でも Animator が Transform を書いてしまう)
        ///   (e) dogRoot と兄弟 (Fetch / StopAnchor / Pet / Wander) が同じ犬ユニットの下にいる
        ///   (f) VRCObjectSync がまだ載っていない
        /// </summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Sync/1 - Survey Dog Sync")]
        public static void Survey()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogSync] Survey (IMP-NET-005)");
            log.AppendLine();

            var dogTransforms = KD_SceneObjects.AllDogTransforms();
            int problems = 0;

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("========= " + def.displayName + " (" + def.key + ") =========");

                int dogCount;
                var dog = KD_SceneObjects.Find(def.DogName, out dogCount);
                if (dogCount != 1 || dog == null)
                {
                    log.AppendLine("  PROBLEM " + def.DogName + " がシーン内に " + dogCount + " 個");
                    problems++;
                    continue;
                }

                log.AppendLine("  path            : " + KD_SceneObjects.PathOf(dog));

                // (f) VRCObjectSync
                var syncs = dog.GetComponents<VRCObjectSync>();
                log.AppendLine("  VRCObjectSync   : " + syncs.Length + " 個");

                // (a) UdonBehaviour — 0 であることが「Manual と同居しない」の最強の形
                var udons = dog.GetComponents<VRC.Udon.UdonBehaviour>();
                log.AppendLine("  UdonBehaviour   : " + udons.Length + " 個"
                    + (udons.Length == 0 ? "" : " ← **却下理由 (i) が復活する。中身を確認**"));
                if (udons.Length != 0) problems++;

                // (b) Rigidbody
                var rb = dog.GetComponent<Rigidbody>();
                log.AppendLine("  Rigidbody       : " + (rb == null ? "無し" : "**有り** (物理同期モードになる)"));
                if (rb != null) problems++;

                // Animator と applyRootMotion
                var anim = dog.GetComponent<Animator>();
                if (anim == null)
                {
                    log.AppendLine("  Animator        : **無し** (dogRoot に載っていない?)");
                    problems++;
                }
                else
                {
                    log.AppendLine("  Animator        : applyRootMotion=" + anim.applyRootMotion
                        + " controller=" + (anim.runtimeAnimatorController == null
                            ? "(null)" : anim.runtimeAnimatorController.name));
                    if (anim.applyRootMotion) problems++;

                    // (d) ルート位置カーブ
                    int rootPosClips;
                    float worstRange;
                    string worstName;
                    int scanned = ScanRootPositionCurves(anim.runtimeAnimatorController,
                        out rootPosClips, out worstRange, out worstName);
                    log.AppendLine("  ルート位置カーブ: " + rootPosClips + " / " + scanned + " clips"
                        + (rootPosClips == 0 ? "" :
                            " ← 最大 " + worstRange.ToString("F4") + "m (" + worstName + ")"
                            + " **Animator が Transform を書く**"));
                    if (rootPosClips != 0) problems++;
                }

                // (c) 先祖が動かないコンテナだけか
                string offender;
                bool staticAncestry = KD_SceneObjects.HasStaticAncestry(dog, out offender);
                log.AppendLine("  先祖            : " + (staticAncestry ? "すべて単位元の動かないコンテナ"
                    : "**動く親が居る** — " + offender));
                if (!staticAncestry) problems++;

                // プレハブインスタンスとしての素性 (Unpack していないこと)
                bool isInstance = PrefabUtility.IsPartOfPrefabInstance(dog);
                bool isOutermost = PrefabUtility.GetOutermostPrefabInstanceRoot(dog) == dog;
                log.AppendLine("  プレハブ        : instance=" + isInstance + " outermostRoot=" + isOutermost);
                if (!isInstance || !isOutermost) problems++;

                // (e) 兄弟が同じ犬ユニットの下にいるか
                var unit = dog.transform.parent;
                log.AppendLine("  犬ユニット      : " + (unit == null ? "(親なし)" : unit.name)
                    + (unit != null && unit.name == def.UnitName ? "" : " ← 期待 " + def.UnitName));
                if (unit == null || unit.name != def.UnitName) problems++;

                foreach (var sibling in new[] { def.FetchSystemName, def.StopAnchorName,
                                                def.PetSystemName, def.WanderSystemName })
                {
                    int c;
                    var go = KD_SceneObjects.Find(sibling, out c);
                    bool sameUnit = c == 1 && go != null && unit != null && go.transform.parent == unit;
                    log.AppendLine("    " + (sameUnit ? "ok   " : "PROBLEM ") + sibling
                        + (sameUnit ? " (同じ犬ユニットの下)"
                                    : " count=" + c + " parent="
                                      + (go == null || go.transform.parent == null
                                         ? "(なし)" : go.transform.parent.name)));
                    if (!sameUnit) problems++;
                }

                // 犬の子孫に紛れ込んでいる自作物が無いか (子を足していないことの確認)
                int strays = 0;
                foreach (var t in dog.GetComponentsInChildren<Transform>(true))
                    if (t != dog.transform && t.name.StartsWith("KD_")) strays++;
                log.AppendLine("  犬の子孫の KD_* : " + strays + " 個"
                    + (strays == 0 ? "" : " ← 撫で/Fetch のアンカーなら正常。要確認"));

                log.AppendLine();
            }

            // ---- 名簿の並び (押し出しの Owner ゲートが犬ごとに効くための前提) ----
            log.AppendLine("========= 名簿 (KD_DogPack) =========");
            int packCount;
            var packGo = KD_SceneObjects.Find("KD_DogPack", out packCount);
            if (packCount != 1 || packGo == null)
            {
                log.AppendLine("  PROBLEM KD_DogPack がシーン内に " + packCount + " 個");
                problems++;
            }
            else
            {
                var pack = packGo.GetUdonSharpComponent<KD_DogPack>();
                if (pack == null || pack.dogRoots == null)
                {
                    log.AppendLine("  PROBLEM KD_DogPack.dogRoots が引けない");
                    problems++;
                }
                else
                {
                    log.AppendLine("  dogRoots: " + pack.dogRoots.Length + " 匹");
                    for (int i = 0; i < pack.dogRoots.Length; i++)
                    {
                        string expect = i < dogTransforms.Length && dogTransforms[i] != null
                            ? dogTransforms[i].name : "(不明)";
                        string actual = pack.dogRoots[i] == null ? "(null)" : pack.dogRoots[i].name;
                        bool ok = actual == expect;
                        log.AppendLine("    " + (ok ? "ok   " : "PROBLEM ") + "[" + i + "] " + actual
                            + (ok ? "" : " ← 期待 " + expect));
                        if (!ok) problems++;
                    }
                }
            }

            log.AppendLine();
            log.AppendLine("問題 " + problems + " 件");
            log.AppendLine(problems == 0
                ? "SURVEY RESULT: PASS — 2 - Apply Owner Authoritative へ進んでよい"
                : "SURVEY RESULT: PROBLEMS — **適用の前に上を潰すこと**");
            log.AppendLine();
            log.AppendLine("NOTE ソース側の棚卸し (Transform 書き込みの Owner ゲート / SetOwner の対象) は");
            log.AppendLine("     `node scripts/check-sync-anchors.js` の担当 (IMP-NET-005 A7)。");
            Finish(log);
        }

        // ================================================================ 2 - Apply

        /// <summary>dogRoot 4 個へ <c>VRCObjectSync</c> を足す。**子は 1 つも作らない。**
        ///
        /// 購入プレハブのインスタンスだが、**コンポーネントの追加は Unpack を要求しない**
        /// (IMP-INF-003 で実証済み。プレハブ差分として記録される)。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Sync/2 - Apply Owner Authoritative")]
        public static void Apply()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogSync] Apply (IMP-NET-005)");
            int added = 0, reused = 0, skipped = 0;

            foreach (var def in KD_DogDefs.All)
            {
                int c;
                var dog = KD_SceneObjects.Find(def.DogName, out c);
                if (c != 1 || dog == null)
                {
                    log.AppendLine("  PROBLEM skip " + def.key + " (" + def.DogName + " が " + c + " 個)");
                    skipped++;
                    continue;
                }

                var sync = dog.GetComponent<VRCObjectSync>();
                if (sync == null)
                {
                    sync = Undo.AddComponent<VRCObjectSync>(dog);
                    added++;
                    log.AppendLine("  added  " + def.DogName);
                }
                else
                {
                    reused++;
                    log.AppendLine("  reused " + def.DogName);
                }

                ApplySyncSettings(sync, log, def.key + ": ");
                EditorUtility.SetDirty(dog);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine();
            log.AppendLine("added=" + added + " reused=" + reused + " skipped=" + skipped);
            // **部分適用を成功に見せない。** Verify を必ず通す運用でも、
            // ログ単体を後から読む人が「4 匹に載った」と誤読する
            log.AppendLine(skipped == 0
                ? "APPLY RESULT: DONE — **シーンは保存していない。**"
                : "APPLY RESULT: PARTIAL (" + skipped + " 匹に載っていない) — **シーンは保存していない。**");
            log.AppendLine("次: 3 - Verify Dog Sync → Verify スイート 20 本 → それから保存");
            Finish(log);
        }

        /// <summary>`VRCObjectSync` の設定を Owner 固定の前提へ揃える。
        ///
        /// **`AllowCollisionOwnershipTransfer` を false にするのが本体。**
        /// 既定のままだと、**プレイヤーや物が犬にぶつかっただけで Ownership が移る** —
        /// 犬側の `SetOwner` を全部消して不変条件 3 を守ったつもりでも、
        /// SDK 側の経路から黙って破られる。実機で「たまに犬の挙動がおかしい」に化ける類の穴。</summary>
        static void ApplySyncSettings(VRCObjectSync sync, StringBuilder log, string tag)
        {
            var so = new SerializedObject(sync);
            var prop = so.FindProperty("AllowCollisionOwnershipTransfer");
            if (prop == null)
            {
                log.AppendLine("  " + tag + "WARN AllowCollisionOwnershipTransfer が見つからない "
                    + "(SDK の版で名前が変わった?) — 手で false にすること");
                return;
            }
            if (prop.boolValue)
            {
                prop.boolValue = false;
                so.ApplyModifiedProperties();
                log.AppendLine("  " + tag + "AllowCollisionOwnershipTransfer = false へ変更 "
                    + "(衝突で Owner が移ると不変条件 3 が壊れる)");
            }
            else
            {
                log.AppendLine("  " + tag + "AllowCollisionOwnershipTransfer は既に false");
            }
        }

        // ================================================================ 3 - Verify

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Sync/3 - Verify Dog Sync")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogSync] Verify (IMP-NET-005)");
            int fail = 0;

            var dogTransforms = KD_SceneObjects.AllDogTransforms();

            foreach (var def in KD_DogDefs.All)
            {
                string tag = def.key + ": ";
                int c;
                var dog = KD_SceneObjects.Find(def.DogName, out c);
                if (!Check(ref fail, log, c == 1 && dog != null, tag + def.DogName + " がシーン内に 1 個")) continue;

                // 1. VRCObjectSync がちょうど 1 個
                var syncs = dog.GetComponents<VRCObjectSync>();
                Check(ref fail, log, syncs.Length == 1,
                    tag + "VRCObjectSync がちょうど 1 個 (actual " + syncs.Length + ")");

                // 1b. 衝突での Ownership 移譲が切ってあること (不変条件 3 を SDK 側から守る)
                if (syncs.Length == 1)
                {
                    var prop = new SerializedObject(syncs[0]).FindProperty("AllowCollisionOwnershipTransfer");
                    Check(ref fail, log, prop != null && !prop.boolValue,
                        tag + "AllowCollisionOwnershipTransfer == false "
                            + "(true だと衝突で Owner が移り、Owner 固定が黙って壊れる)");
                }

                // 2. dogRoot 上に UdonBehaviour が無い (Manual と Continuous の同居問題が消える)
                var udons = dog.GetComponents<VRC.Udon.UdonBehaviour>();
                Check(ref fail, log, udons.Length == 0,
                    tag + "dogRoot に UdonBehaviour が無い (actual " + udons.Length
                        + " — Manual と同居すると却下理由 (i) が復活する)");

                // 3. 先祖がすべて動かないコンテナ
                //    IMP-INF-003 の「VRCObjectSync を持つものは動かない親の下」が犬にも効くようになった
                string offender;
                Check(ref fail, log, KD_SceneObjects.HasStaticAncestry(dog, out offender),
                    tag + "先祖が動かないコンテナだけ"
                        + (offender == null ? "" : " (" + offender + ")"));

                // 4. Rigidbody が無い (あると ObjectSync が物理同期モードへ入る)
                Check(ref fail, log, dog.GetComponent<Rigidbody>() == null,
                    tag + "Rigidbody が無い (物理同期モードにしない)");

                // 5. Animator がルートを書かない
                //    applyRootMotion=false でも**ルート (path 空) の位置カーブがあれば書かれる**
                var anim = dog.GetComponent<Animator>();
                Check(ref fail, log, anim != null && !anim.applyRootMotion,
                    tag + "applyRootMotion == false");
                if (anim != null)
                {
                    int rootPosClips; float worstRange; string worstName;
                    int scanned = ScanRootPositionCurves(anim.runtimeAnimatorController,
                        out rootPosClips, out worstRange, out worstName);
                    Check(ref fail, log, rootPosClips == 0,
                        tag + "ルート位置カーブを持つクリップが 0 (actual " + rootPosClips
                            + " / " + scanned + (rootPosClips == 0 ? "" : "、最悪 " + worstName) + ")");
                }

                // 6. 犬ユニットの中に居て、兄弟も同じユニットの下 (Ownership を揃える単位)
                var unit = dog.transform.parent;
                Check(ref fail, log, unit != null && unit.name == def.UnitName,
                    tag + "犬ユニット " + def.UnitName + " の直下");
                foreach (var sibling in new[] { def.FetchSystemName, def.StopAnchorName,
                                                def.PetSystemName, def.WanderSystemName })
                {
                    int sc;
                    var go = KD_SceneObjects.Find(sibling, out sc);
                    Check(ref fail, log,
                        sc == 1 && go != null && unit != null && go.transform.parent == unit,
                        tag + sibling + " が同じ犬ユニットの下 (Owner を揃える単位)");
                }

                // 7. 見つかる VRCObjectSync が**すべてルート上**であること。
                //    入れ子の ObjectSync は成立しないので、子孫側に生えたら必ず落とす。
                //    (ルートの個数と部分木の個数を比べる形にすると、適用前 0 / 適用後 1 の
                //     どちらでも「子孫に無い」が正しく真になる)
                int inSubtree = dog.GetComponentsInChildren<VRCObjectSync>(true).Length;
                Check(ref fail, log, inSubtree == syncs.Length,
                    tag + "VRCObjectSync が子孫に無い (ルート " + syncs.Length
                        + " / 部分木 " + inSubtree + ")");
            }

            // 8. 名簿の並び (押し出しの Owner ゲートが犬ごとに効くための前提)
            int packCount;
            var packGo = KD_SceneObjects.Find("KD_DogPack", out packCount);
            var pack = packGo == null ? null : packGo.GetUdonSharpComponent<KD_DogPack>();
            if (Check(ref fail, log, packCount == 1 && pack != null && pack.dogRoots != null,
                    "KD_DogPack.dogRoots が引ける"))
            {
                bool ordered = pack.dogRoots.Length == dogTransforms.Length;
                for (int i = 0; ordered && i < pack.dogRoots.Length; i++)
                    ordered = pack.dogRoots[i] == dogTransforms[i];
                Check(ref fail, log, ordered,
                    "KD_DogPack.dogRoots の並びが KD_DogDefs.All と一致 "
                        + "(押し出しの Owner ゲートが犬ごとに効くための前提)");
            }

            // 9. ACOT の全数検査 (破壊的監査 v2 RC-1)。
            //    犬は 1b が個別に見るが、**ボール (プレハブ) とおやつ実体 (シーン) は
            //    落とし忘れたまま出荷ラインに載っていた** — ボールの Owner = 投げた人 /
            //    実体の Owner = 出した人 という認証・権威の軸が、物理接触 1 回で
            //    黙って書き換わる。このワールドに衝突で Ownership を移してよい物は無い。
            //    ⚠ YAML の grep では `VRC_Pickup` の同名フィールド (常に 0) と紛れる —
            //    ここは SerializedObject で VRCObjectSync だけを見る
            log.AppendLine();
            log.AppendLine("---- ACOT 全数検査 (シーン + Assets/KominkaHillside/Imported/KominkaDogRun のプレハブ) ----");
            int acotSeen = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var sync in root.GetComponentsInChildren<VRCObjectSync>(true))
                {
                    acotSeen++;
                    var p = new SerializedObject(sync).FindProperty("AllowCollisionOwnershipTransfer");
                    Check(ref fail, log, p != null && !p.boolValue,
                        "scene " + KD_SceneObjects.PathOf(sync.gameObject)
                            + ": AllowCollisionOwnershipTransfer == false");
                }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/KominkaHillside/Imported/KominkaDogRun" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                foreach (var sync in go.GetComponentsInChildren<VRCObjectSync>(true))
                {
                    acotSeen++;
                    var p = new SerializedObject(sync).FindProperty("AllowCollisionOwnershipTransfer");
                    Check(ref fail, log, p != null && !p.boolValue,
                        "prefab " + path + ": AllowCollisionOwnershipTransfer == false");
                }
            }
            // 全数検査が「対象 0 個で PASS」に化けていないことも見る (犬 4 + ボール 4 + おやつ 3)
            Check(ref fail, log, acotSeen >= 11,
                "ACOT 検査対象が 11 個以上 (actual " + acotSeen + " — 少なければ列挙が壊れている)");

            log.AppendLine();
            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : "VERIFY RESULT: FAIL (" + fail + " checks)");
            Finish(log);
        }

        // ================================================================ 5 - Apply ACOT

        /// <summary>シーンと自作プレハブの全 `VRCObjectSync` で
        /// `AllowCollisionOwnershipTransfer` を落とす (破壊的監査 v2 RC-1 = A-1)。
        ///
        /// 犬は 2 - Apply が個別に落とすが、**ボール 4 プレハブとおやつ実体 3 個は
        /// 開いたままだった** (プレハブ既定値 true がそのまま生きていた)。
        /// ボールはプレハブアセット側を直す — シーンのインスタンスはオーバーライド無しで追随し、
        /// シーン差分も出ない。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Sync/5 - Apply ACOT (Ball & Treat)")]
        public static void ApplyAcotEverywhere()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogSync] Apply ACOT (破壊的監査 v2 RC-1)");
            int sceneChanged = 0, assetChanged = 0, seen = 0;

            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var sync in root.GetComponentsInChildren<VRCObjectSync>(true))
                {
                    seen++;
                    if (ClearAcot(sync, log, "scene " + KD_SceneObjects.PathOf(sync.gameObject) + ": "))
                        sceneChanged++;
                }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/KominkaHillside/Imported/KominkaDogRun" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                foreach (var sync in go.GetComponentsInChildren<VRCObjectSync>(true))
                {
                    seen++;
                    if (ClearAcot(sync, log, "prefab " + path + ": "))
                    {
                        assetChanged++;
                        EditorUtility.SetDirty(sync);
                    }
                }
            }

            if (assetChanged > 0) AssetDatabase.SaveAssets();
            if (sceneChanged > 0) EditorSceneManager.MarkSceneDirty(scene);
            log.AppendLine();
            log.AppendLine("seen=" + seen + " sceneChanged=" + sceneChanged
                + " prefabChanged=" + assetChanged);
            log.AppendLine("APPLY RESULT: DONE — プレハブは保存済み。**シーンは保存していない。**");
            log.AppendLine("次: 3 - Verify Dog Sync (ACOT 全数検査を含む) → それから保存");
            Finish(log);
        }

        /// <summary>1 個の VRCObjectSync の ACOT を落とす。変えたら true。</summary>
        static bool ClearAcot(VRCObjectSync sync, StringBuilder log, string tag)
        {
            var so = new SerializedObject(sync);
            var prop = so.FindProperty("AllowCollisionOwnershipTransfer");
            if (prop == null)
            {
                log.AppendLine("  " + tag + "WARN AllowCollisionOwnershipTransfer が見つからない — 手で false にすること");
                return false;
            }
            if (!prop.boolValue)
            {
                log.AppendLine("  " + tag + "既に false");
                return false;
            }
            prop.boolValue = false;
            so.ApplyModifiedProperties();
            log.AppendLine("  " + tag + "true → false へ変更");
            return true;
        }

        // ================================================================ 4 - Revert

        /// <summary>`VRCObjectSync` を外して旧方式へ戻す。**逆戻りの最も軽い段。**
        ///
        /// **厳密には 2 - Apply の逆操作ではない** — dogRoot 上の `VRCObjectSync` を
        /// 「今回足したものか」に関係なく全部外す。IMP-NET-005 の時点では犬に
        /// ObjectSync は 1 つも無かった (`1 - Survey` で実測) ので実害は無いが、
        /// 将来この前提が変わるなら Apply 側に「足したもの」の記録を持たせること。
        ///
        /// **厳密には 2 - Apply の逆操作ではない** — dogRoot 上の `VRCObjectSync` を
        /// 「今回足したもの」かどうかに関係なく全部外す。IMP-NET-005 の時点では
        /// 犬に ObjectSync は 1 つも無かった (Survey で実測) ので実害は無いが、
        /// 将来この前提が変わったら Apply 側に記録を持たせること。
        /// スクリプト側の Owner ゲートは残るので、これだけでは
        /// 「誰も犬を動かさない」状態になる — コード側も `git checkout` で戻すこと。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Dog Sync/4 - Revert (remove VRCObjectSync)")]
        public static void Revert()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_DogSync] Revert (IMP-NET-005)");
            int removed = 0;
            foreach (var def in KD_DogDefs.All)
            {
                int c;
                var dog = KD_SceneObjects.Find(def.DogName, out c);
                if (c != 1 || dog == null) continue;
                foreach (var s in dog.GetComponents<VRCObjectSync>())
                {
                    Undo.DestroyObjectImmediate(s);
                    removed++;
                    log.AppendLine("  removed " + def.DogName);
                }
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine();
            log.AppendLine("removed=" + removed);
            log.AppendLine("REVERT RESULT: DONE — **シーンは保存していない。**");
            log.AppendLine("**スクリプト側の Owner ゲートは残っている。**");
            log.AppendLine("旧方式へ完全に戻すなら git checkout -- Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/ も要る。");
            Finish(log);
        }

        // ================================================================ helpers

        static bool Check(ref int fail, StringBuilder log, bool ok, string label)
        {
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + label);
            if (!ok) fail++;
            return ok;
        }

        /// <summary>Controller 配下の全クリップから**ルート (path 空) の Transform カーブ**を数える。
        ///
        /// `applyRootMotion` が false でも、このカーブがあると Animator が
        /// dogRoot の Transform を書き、<c>VRCObjectSync</c> と毎フレーム競合する
        /// (DES-NET-002 不変条件 7)。
        ///
        /// **位置だけでなく回転とスケールも見る** (Codex レビュー 2026-08-22) —
        /// 元にした `Debug/Dump Dog Clip Curves` は「滑り」を追っていたので位置だけを見ていたが、
        /// ここで守りたいのは「Animator が dogRoot を書かないこと」なので射程が違う。
        /// ルートの回転カーブは Owner の旋回と、スケールは見た目と、それぞれ競合する。</summary>
        static int ScanRootPositionCurves(RuntimeAnimatorController rac,
            out int clipsWithRootPos, out float worstRange, out string worstName)
        {
            clipsWithRootPos = 0;
            worstRange = 0f;
            worstName = "";
            if (rac == null) return 0;

            int scanned = 0;
            foreach (var clip in rac.animationClips)
            {
                if (clip == null) continue;
                scanned++;
                float range = 0f;
                bool has = false;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.propertyName.IndexOf("LocalPosition") < 0
                        && b.propertyName.IndexOf("LocalRotation") < 0
                        && b.propertyName.IndexOf("LocalEuler") < 0
                        && b.propertyName.IndexOf("LocalScale") < 0) continue;
                    if (b.path.Length != 0) continue; // ルート (犬そのもの) のカーブだけが問題
                    has = true;
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    if (curve == null || curve.keys.Length == 0) continue;
                    float mn = float.MaxValue, mx = float.MinValue;
                    foreach (var k in curve.keys)
                    {
                        if (k.value < mn) mn = k.value;
                        if (k.value > mx) mx = k.value;
                    }
                    if (mx - mn > range) range = mx - mn;
                }
                if (!has) continue;
                clipsWithRootPos++;
                if (range > worstRange) { worstRange = range; worstName = clip.name; }
            }
            return scanned;
        }

        static void Finish(StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile));
            File.WriteAllText(LogFile, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
