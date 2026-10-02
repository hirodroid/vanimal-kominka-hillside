using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// 裏山改変 (IMP-PRG-001 段階 1): おじいさん (KD_ShopKeeper) を裏山に置く入口。
    ///
    /// 実行順 (前提 → 出力):
    ///   Menu/3a Setup Progress (KH_Dogs/KD_ProgressSystem。無くても 4d は WARN で進む)
    ///   → **Menu/4d Setup Shop Keeper** → **Menu/4e Verify Shop Keeper**
    ///   → Menu/11 Setup Localization (文言の配線。KD_Language.keeper もここで引き直される)。
    /// ログは Temp/menu-rebuild/keeper-setup.txt / keeper-verify.txt。**シーンは保存しない。**
    ///
    /// 古民家の入口 (Imported-Unused/Shop Keeper/2・3) との違い:
    /// ①置き場所は KH_Dogs 直下。**初回だけ**門の KH_LeashRack (45.00, 2.71, 43.00) の +X 3 m
    ///   (48.0, 地面へレイキャスト, 43.0) に置き、ラック側 (−X) を向ける。既存なら位置・向きは触らない
    ///   (人の調整を潰さない — vrc-scene-edit-safety)。
    /// ②man_farm (購入アセット、git 管理外) のインスタンスを子に**自動で置く** (古民家は人が置いた)。
    ///   既に Animator が子にあれば置かない。無ければ WARN で進む (板と配線は組む)。
    /// ③ledger は KH_Dogs/KD_ProgressSystem、language は KH_Dogs/KD_Language を配線する。
    /// ④左ナビは 5 項目 (過ごし方 / さんぽ / おやつ / 呼ぶ / またね)。表はこのファイルの NavButtons / NavEvents。
    /// Interact のみ・近接自動オープンなし・4 m で閉じる・Collider はホスト側・SyncMode None は古民家どおり。
    /// </summary>
    public static partial class KD_ShopKeeperSetupEditor
    {
        // ---- 左ナビ (生成順 = KD_ShopKeeper._Open* の navIndex = 表示段) ----
        internal static readonly string[] NavButtons = { "WaysButton", "WalkButton", "TreatsButton", "CallButton" };
        internal static readonly string[] NavEvents = { "_OpenWays", "_OpenWalk", "_OpenTreatInfo", "_OpenCall" };
        static string[] NavWords()
        {
            return new[] { KD_ShopKeeper.WordWays, KD_ShopKeeper.WordWalk,
                KD_ShopKeeper.WordTreats, KD_ShopKeeper.WordCall };
        }
        const string ByeButtonName = "ByeButton";
        const string ByeEvent = "_Close";
        /// <summary>古民家だけの話題のボタン。裏山では作らない (BuildMenu が残骸を消し、Verify が 0 個を確かめる)。</summary>
        static readonly string[] KominkaOnlyButtons = { "HowToButton", "PettingButton", "DogsButton", "PhotosButton", "ShopButton" };

        // ---- 置き場所 (初回だけ書く。以後は人の値) ----
        const string LanguageName = "KD_Language";
        const string LeashRackName = "KH_LeashRack";
        /// <summary>門のリード掛けの位置 (2026-09-24 実測)。ラックが見つからないときの向きの基準。</summary>
        static readonly Vector3 RackFallbackPos = new Vector3(45.00f, 2.71f, 43.00f);
        /// <summary>初回の立ち位置 (x, z)。y は地面へのレイキャストで決める。</summary>
        static readonly Vector2 InitialXZ = new Vector2(48.0f, 43.0f);

        const string NpcPrefabPath = "Assets/polyperfect/Low Poly Animated People/- Prefabs/man_farm.prefab";
        /// <summary>台帳の keeperPoints の決定値 (IMP-PRG-001。書くのは Setup Progress、ここは検査と本文の見積りだけ)。
        /// TODO(Codex M-2): 正本は KD_ProgressLedger.HillsideKeeperPoints (P1 が追加予定)。見えたら
        /// <c>const int HillsideKeeperPoints = KD_ProgressLedger.HillsideKeeperPoints;</c> へ差し替え、200 の二重管理をやめる。</summary>
        internal const int HillsideKeeperPoints = KD_ProgressLedger.HillsideKeeperPoints; // 正本は台帳 (Codex M-2、司令塔 2026-09-24 差替)

        const string HillsideSetupLog = KD_Hillside.LogDir + "keeper-setup.txt";
        const int KeeperLayer = 8;   // Interactive
        const string HillsideVerifyLog = KD_Hillside.LogDir + "keeper-verify.txt";

        // ================================================================ 4d: Setup

        [MenuItem("Tools/KominkaHillside/Menu/4d Setup Shop Keeper")]
        public static void SetupHillside()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ShopKeeperSetup] Setup (裏山 段階 1)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと");
                Finish(HillsideSetupLog, log);
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(KeeperProgram) == null)
            {
                log.AppendLine("FAIL " + KeeperProgram + " が無い。先に Imported-Unused/Shop Keeper/1 - Create Program Assets → force refresh");
                Finish(HillsideSetupLog, log);
                return;
            }

            int dogsCount;
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out dogsCount);
            if (dogs == null || dogsCount != 1 || dogs.transform.parent != null)
            {
                log.AppendLine("FAIL " + KD_Hillside.DogsRootName + " がルートに 1 個ない (" + dogsCount + ")");
                Finish(HillsideSetupLog, log);
                return;
            }

            // ---- 叩く先 (無くても板は組む。Verify が赤くする) ----
            var ledger = FindLedger(dogs);
            if (ledger == null)
                log.AppendLine("WARN " + KD_Hillside.DogsRootName + "/" + LedgerHost + " に KD_ProgressLedger が無い — "
                    + "残高が「台帳が未配線」のまま、200pt も渡せない。Menu/3a Setup Progress の後に 4d を流し直すこと");
            var language = FindLanguage(dogs, log);
            if (language == null)
                log.AppendLine("WARN " + KD_Hillside.DogsRootName + "/" + LanguageName + " が無い — 本文は const (ja) のまま");

            // ---- ホスト (初回だけ位置・向きを書く) ----
            var keeperGo = EnsureHillsideHost(dogs, log);
            if (keeperGo == null)
            {
                Finish(HillsideSetupLog, log);
                return;
            }
            var keeper = EnsureU<KD_ShopKeeper>(keeperGo, log);
            if (keeper == null)
            {
                Finish(HillsideSetupLog, log);
                return;
            }
            KD_ShopSetupEditor.SetSync(keeper, VRC.SDKBase.Networking.SyncType.None, log);

            // ---- Interact の当たり (ホスト側。man_farm には子を足さない) と表示 ----
            var col = keeperGo.GetComponent<CapsuleCollider>();
            if (col == null)
            {
                col = keeperGo.AddComponent<CapsuleCollider>();
                log.AppendLine("added CapsuleCollider");
            }
            col.center = new Vector3(0f, 0.9f, 0f);
            col.radius = 0.35f;
            col.height = 1.8f;
            col.isTrigger = false;
            // 犬の接地レイ (Default + Environment) に当てない。Default のままだと、移動の着地が重なった犬が
            // カプセルの頭上 (約 1.5 m) へ置かれ、歩き出すときに落ちる (Course/82 T12、2026-10-02)。
            // Interactive はプレイヤーと衝突し Interact も届く。子 (man_farm) は触らない
            keeperGo.layer = KeeperLayer;

            var keeperUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(keeper);
            if (keeperUdon != null)
            {
                keeperUdon.interactText = KD_ShopKeeper.InteractWord;
                keeperUdon.proximity = Proximity;
                EditorUtility.SetDirty(keeperUdon);
                log.AppendLine("interactText/proximity 設定");
            }
            else
            {
                log.AppendLine("WARN backing UdonBehaviour が取れない — force refresh 後に再実行");
            }

            // ---- NPC (man_farm): 無ければ置く → 是正 (Controller / RootMotion / デモ用コンポーネント) ----
            EnsureNpcInstance(keeperGo, log);
            FixNpcInstance(keeperGo, log);

            // ---- 板 (ナビ 5 項目) ----
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (font == null)
                log.AppendLine("WARN 共有フォントが無い: " + KD_ShopSetupEditor.SharedFontPath);
            BuildMenu(keeperGo, keeper, keeperUdon, font, log);

            // ---- 音 (無ければ WARN で null) ----
            keeper.sfx = EnsureAudio(keeperGo, log);
            keeper.openClip = LoadSfx(SfxOpen, log);
            keeper.closeClip = LoadSfx(SfxClose, log);

            // ---- 配線 ----
            keeper.ledger = ledger;
            keeper.language = language;
            keeper.hideDistance = HideDistance;
            keeper.refreshSeconds = 1f;
            if (language != null && language.keeper != keeper)
            {
                // 言語を切り替えたとき本文を引き直す口 (Menu/11 も同じ値を書く)
                language.keeper = keeper;
                TryCopy(language, log);
                log.AppendLine("wired " + LanguageName + ".keeper");
            }

            KD_ShopSetupEditor.AddGlyphs(KeeperGlyphs(), log);

            bool copied = TryCopy(keeper, log);
            EditorSceneManager.MarkSceneDirty(keeperGo.scene);
            log.AppendLine("ledger=" + (ledger != null ? "ok" : "null") + " language=" + (language != null ? "ok" : "null")
                + " position=" + keeperGo.transform.position.ToString("F2")
                + " forward=" + keeperGo.transform.forward.ToString("F2"));
            log.AppendLine(copied
                ? "SETUP RESULT: PASS (scene not saved yet。次は Menu/4e Verify Shop Keeper)"
                : "SETUP RESULT: FAIL (上の WARN を見ること)");
            Finish(HillsideSetupLog, log);
        }

        static KD_ProgressLedger FindLedger(GameObject dogs)
        {
            var t = dogs == null ? null : dogs.transform.Find(LedgerHost);
            return t == null ? null : t.GetComponent<KD_ProgressLedger>();
        }

        /// <summary>KH_Dogs/KD_Language。KH_Dogs 直下に無ければシーン全体で 1 個だけのものを使い、NOTE を残す。</summary>
        static KD_Language FindLanguage(GameObject dogs, StringBuilder log)
        {
            var t = dogs == null ? null : dogs.transform.Find(LanguageName);
            if (t != null) return t.GetComponent<KD_Language>();
            int n;
            var go = KD_SceneObjects.Find(LanguageName, out n);
            if (go == null || n != 1) return null;
            if (log != null) log.AppendLine("NOTE " + LanguageName + " が " + KD_SceneObjects.PathOf(go) + " にある (KH_Dogs 直下ではない)");
            return go.GetComponent<KD_Language>();
        }

        /// <summary>ホストを 1 個用意する。**作ったときだけ**位置と向きを書く (既存は触らない)。</summary>
        static GameObject EnsureHillsideHost(GameObject dogs, StringBuilder log)
        {
            int found;
            var go = KD_SceneObjects.Find(KeeperName, out found);
            if (found > 1)
            {
                log.AppendLine("FAIL '" + KeeperName + "' がシーン内に " + found + " 個ある。1 個に整理してから再実行");
                return null;
            }
            if (go != null)
            {
                log.AppendLine("reused " + KD_SceneObjects.PathOf(go) + " at "
                    + go.transform.position.ToString("F2") + " (位置・向きは動かさない)");
                if (go.transform.parent != dogs.transform)
                    log.AppendLine("WARN " + KeeperName + " が " + KD_Hillside.DogsRootName + " 直下にない (4e が FAIL にする)");
                return go;
            }

            go = new GameObject(KeeperName);
            Undo.RegisterCreatedObjectUndo(go, "Create " + KeeperName);
            // KD_SceneObjects.PlaceIfNew は器の Transform を単位元へ揃えるので使わない (KH_Dogs は犬の器。触らない)
            Undo.SetTransformParent(go.transform, dogs.transform, "Place " + KeeperName);

            string how;
            float y = GroundY(InitialXZ.x, InitialXZ.y, dogs.transform, out how);
            var pos = new Vector3(InitialXZ.x, y, InitialXZ.y);
            int rc;
            var rack = KD_SceneObjects.Find(LeashRackName, out rc);
            Vector3 rackPos = (rack != null && rc == 1) ? rack.transform.position : RackFallbackPos;
            Vector3 face = rackPos - pos;
            face.y = 0f;
            if (face.sqrMagnitude < 0.0001f) face = Vector3.left;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face.normalized, Vector3.up));
            log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + KeeperName + " at " + pos.ToString("F2")
                + " (地面: " + how + ")、" + LeashRackName + (rack != null && rc == 1 ? "" : " (見つからず既定値)")
                + " " + rackPos.ToString("F2") + " の方を向く。以後は人の値 (Setup は動かさない)");
            return go;
        }

        /// <summary>(x, z) の地面の高さ。TerrainCollider を優先し、無ければ KH_Dogs の外の最も高い非トリガー面。
        /// 何にも当たらなければラックの高さ (2.71) で置き、<paramref name="how"/> に WARN を書く。</summary>
        static float GroundY(float x, float z, Transform dogs, out string how)
        {
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(x, 500f, z), Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit best = default(RaycastHit);
            bool have = false, terrain = false;
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                if (dogs != null && h.collider.transform.IsChildOf(dogs)) continue;   // 犬・リード・拠点の当たりは地面ではない
                bool isTerrain = h.collider is TerrainCollider;
                if (!have || (isTerrain && !terrain) || (isTerrain == terrain && h.point.y > best.point.y))
                {
                    best = h;
                    have = true;
                    terrain = isTerrain;
                }
            }
            if (!have)
            {
                how = "WARN レイキャストが何にも当たらない — ラックの高さ " + RackFallbackPos.y.ToString("F2") + " で仮置き (シーンで直すこと)";
                return RackFallbackPos.y;
            }
            how = KD_SceneObjects.PathOf(best.collider.gameObject) + (terrain ? " (Terrain)" : "") + " y=" + best.point.y.ToString("F2");
            return best.point.y;
        }

        /// <summary>man_farm のインスタンスを子に置く。**子に Animator が既にあれば何もしない**
        /// (人が置き直した NPC を潰さない)。プレハブが無ければ WARN (git 管理外の購入アセット)。</summary>
        static void EnsureNpcInstance(GameObject keeperGo, StringBuilder log)
        {
            if (keeperGo.GetComponentsInChildren<Animator>(true).Length > 0)
            {
                log.AppendLine("npc: 子に Animator がある (置き直さない)");
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
            if (prefab == null)
            {
                log.AppendLine("WARN npc: " + NpcPrefabPath + " が無い (購入アセット polyperfect は git 管理外) — 板と配線だけ組む");
                return;
            }
            var npc = PrefabUtility.InstantiatePrefab(prefab, keeperGo.scene) as GameObject;
            if (npc == null)
            {
                log.AppendLine("WARN npc: InstantiatePrefab に失敗");
                return;
            }
            Undo.RegisterCreatedObjectUndo(npc, "Place man_farm");
            npc.transform.SetParent(keeperGo.transform, false);
            npc.transform.localPosition = Vector3.zero;
            npc.transform.localRotation = Quaternion.identity;
            log.AppendLine("npc: man_farm のインスタンスを " + KeeperName + " の子に置いた (Unpack しない)");
        }

        // ================================================================ 4e: Verify

        [MenuItem("Tools/KominkaHillside/Menu/4e Verify Shop Keeper")]
        public static void VerifyHillside()
        {
            var t = new VerifyTally();
            t.Log.AppendLine("[KD_ShopKeeperSetup] Verify (裏山 段階 1)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                t.Check(false, KD_Hillside.ScenePath + " を Edit モードで開いている");
                FinishVerify(t.Log, t.Fail, HillsideVerifyLog);
                return;
            }

            int found;
            var keeperGo = KD_SceneObjects.Find(KeeperName, out found);
            t.Check(keeperGo != null && found == 1, KeeperName + " がシーンにちょうど 1 個 (実際 " + found + ")");
            if (keeperGo == null)
            {
                FinishVerify(t.Log, t.Fail, HillsideVerifyLog);
                return;
            }
            int dogsCount;
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out dogsCount);
            t.Check(dogs != null && dogsCount == 1 && keeperGo.transform.parent == dogs.transform,
                KeeperName + " が " + KD_Hillside.DogsRootName + " 直下");

            // ---- 位置 (値は人のもの。有効であることだけ見る) ----
            var p = keeperGo.transform.position;
            bool finite = !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
                || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));
            t.Check(finite, "位置が有効 (NaN / Inf でない): " + p.ToString("F2"));
            int rc;
            var rack = KD_SceneObjects.Find(LeashRackName, out rc);
            if (rack != null && rc == 1)
            {
                Vector3 d = rack.transform.position - p;
                d.y = 0f;
                float facing = d.sqrMagnitude < 0.0001f ? 1f : Vector3.Dot(keeperGo.transform.forward, d.normalized);
                t.Log.AppendLine("  info " + LeashRackName + " まで水平 " + d.magnitude.ToString("F2")
                    + " m / ラック方向との内積 " + facing.ToString("F2") + " (初回値は 3.00 m・1.00。人が動かしたなら問題なし)");
            }
            else t.Warn(LeashRackName + " が 1 個でない (" + rc + ") — 門との位置関係を測れない");

            var keeper = keeperGo.GetComponent<KD_ShopKeeper>();
            t.Check(keeper != null, "KD_ShopKeeper が付いている");
            if (keeper == null)
            {
                FinishVerify(t.Log, t.Fail, HillsideVerifyLog);
                return;
            }

            // ---- 同期 0 (板は完全にローカル) ----
            int synced = 0;
            foreach (var fi in typeof(KD_ShopKeeper).GetFields(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                if (fi.IsDefined(typeof(UdonSyncedAttribute), true)) synced++;
            t.Check(synced == 0, "同期変数 0 (実際 " + synced + ")");

            // ---- 開閉の形: Interact のみ・近接で開かない・4 m で閉じる ----
            t.Check(typeof(KD_ShopKeeper).GetMethod("OnPlayerTriggerEnter") == null
                || typeof(KD_ShopKeeper).GetMethod("OnPlayerTriggerEnter").DeclaringType != typeof(KD_ShopKeeper),
                "近接で開く口 (OnPlayerTriggerEnter) が無い = Interact のみ");
            t.Check(Mathf.Abs(keeper.hideDistance - HideDistance) < 0.01f,
                "hideDistance = " + HideDistance + " m (実際 " + keeper.hideDistance + ")");

            // ---- 台帳・言語 ----
            var wantLedger = FindLedger(dogs);
            t.Check(wantLedger != null && keeper.ledger == wantLedger,
                "ledger = " + KD_Hillside.DogsRootName + "/" + LedgerHost
                + (wantLedger == null ? " (台帳が無い — Menu/3a Setup Progress の後に 4d)" : ""));
            t.Check(keeper.ledger != null && keeper.ledger.keeperPoints == HillsideKeeperPoints,
                "台帳の keeperPoints = " + HillsideKeeperPoints + " (実際 "
                + (keeper.ledger == null ? "台帳なし" : keeper.ledger.keeperPoints.ToString()) + ")");
            var wantLanguage = FindLanguage(dogs, null);
            t.Check(wantLanguage != null && keeper.language == wantLanguage,
                "language = " + KD_Hillside.DogsRootName + "/" + LanguageName);
            t.Check(wantLanguage != null && wantLanguage.keeper == keeper,
                LanguageName + ".keeper がこの keeper (言語を切り替えたとき本文を引き直す)");

            // ---- 付与の要求が板の開閉と分かれている (Codex D-2) ----
            // 復元前に閉じても 200pt を取りこぼさない構造: 要求フィールドが実在し、CloseMenu がそれを消さない
            t.Check(typeof(KD_ShopKeeper).GetField("_giftPending", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic) != null, "付与の要求フィールド _giftPending がある");
            {
                string body = CloseMenuSource();
                t.Check(body != null && !body.Contains("_giftPending"),
                    "CloseMenu が付与の要求を消さない (閉じても復元後に _Refresh が付与する)"
                    + (body == null ? " — CloseMenu の本文が読めない" : ""));
            }

            // ---- NPC の出自と Controller の中身 ----
            var animators = keeperGo.GetComponentsInChildren<Animator>(true);
            if (animators.Length == 1)
            {
                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(animators[0].gameObject);
                t.Check(src == NpcPrefabPath, "NPC が man_farm のインスタンス (実際 " + (string.IsNullOrEmpty(src) ? "プレハブではない" : src) + ")");
            }
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(KD_NpcAnimatorBuilderEditor.ControllerPath);
            if (ac != null)
            {
                int states = 0, empty = 0;
                foreach (var layer in ac.layers)
                    foreach (var s in layer.stateMachine.states)
                    {
                        states++;
                        if (s.state.motion == null) empty++;
                    }
                t.Check(states > 0 && empty == 0, "AC_KD_Npc の State " + states + " 個すべてに clip が刺さっている (polyperfect の GUID が解決。空 " + empty + ")");
            }

            // ---- 共通の核 (Interact・配線・板・ナビ 5 項目・文言の幅・字形・NPC の是正) ----
            VerifyCore(t, keeperGo, keeper);

            // ---- 裏山では対象外 (黙って落とさない) ----
            t.Skip("話題 メニュー / なでかた / 犬のこと / 写真のこと / おかいもの — 裏山では対象外 (ボタンを作らない。文言キーは残す)");
            t.Skip("棚 (KD_ShopShelf) のそばに立つ配置 — 裏山では対象外 (棚が無い。購入は手元メニュー)");
            t.Skip("Photo System の「想定外の active Canvas」検査 — 裏山では対象外 (写真が無い。板の既定非アクティブは上で検査済み)");
            t.Skip("Wave (手を振る) の駆動 — 古民家と同じく v1 では誰も叩かない");
            t.Log.AppendLine("  MANUAL man_farm の見た目・立ち位置・向き、板の読みやすさは PCVR で人が確認する");

            FinishVerify(t.Log, t.Fail, HillsideVerifyLog);
        }

        /// <summary>KD_ShopKeeper.cs の CloseMenu の本文 (波括弧の中)。読めなければ null。</summary>
        static string CloseMenuSource()
        {
            string path = System.IO.Path.GetDirectoryName(KeeperProgram) + "/KD_ShopKeeper.cs";
            if (!System.IO.File.Exists(path)) return null;
            string src = System.IO.File.ReadAllText(path);
            int a = src.IndexOf("private void CloseMenu()", System.StringComparison.Ordinal);
            if (a < 0) return null;
            int open = src.IndexOf('{', a);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) return src.Substring(open + 1, i - open - 1);
            }
            return null;
        }
    }
}
