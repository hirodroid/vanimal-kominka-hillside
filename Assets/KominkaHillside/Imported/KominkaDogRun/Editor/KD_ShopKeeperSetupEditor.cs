using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-ENV-004: おじいさん NPC (説明・案内役) のセットアップと検証。
    /// 正本は [npc-shop-menu.md](../../../docs/development/design/npc-shop-menu.md) (v2.1)。
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    ///   1 - Create Program Assets → force refresh
    ///   2 - Setup Shop Keeper
    ///   3 - Verify Shop Keeper
    ///
    /// ## `Shop/1〜3` へ足さない理由
    ///
    /// おじいさんは**購入導線に一切関与しない** (残高を読むだけ)。
    /// Shop 側の Verify は「棚と確認パネルの配線」の成立証拠なので、
    /// 説明メニューを混ぜると検査の意味が変わる。**Shop/3 が無改造のまま PASS すること**が
    /// 「棚側を壊していない」の回帰ゲートになる (IMP-FET-009 Phase 1 と同じ手)。
    ///
    /// ## man_farm (購入アセット) の扱い
    ///
    /// **人間がシーンで `KD_ShopKeeper` の子へ置く。** Setup は Instantiate しない —
    /// ①NPC の位置・見た目の確認はユーザー作業 (設計 §9) ②polyperfect は `.gitignore`
    /// 対象なので、アセット未同期環境でも Setup が UI と配線だけは組めるように。
    /// Setup がやるのは**検出と是正だけ**: Controller を `AC_KD_Npc` へ差し替え、
    /// RootMotion を切り、デモ用コンポーネント (`People_WanderScript` / NavMeshAgent /
    /// CharacterController) をインスタンスから外す (アセット本体は不可触)。
    /// </summary>
    public static class KD_ShopKeeperSetupEditor
    {
        const string KeeperScript = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ShopKeeper.cs";
        const string KeeperProgram = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ShopKeeper.asset";

        const string KeeperName = KD_DogDefs.ShopKeeperName;
        const string PivotName = "KD_ShopKeeperPivot";
        const string MenuName = "KD_ShopKeeperMenu";
        const string LedgerHost = KD_DogDefs.ProgressSystemName;

        /// <summary>Interact できる距離 (m)。既定 2m より少し広め —
        /// 「近づいたときだけ表示」の設計値なので Setup が明示的に書く
        /// (沈黙既定にしない。Verify が実物を検査する)。</summary>
        const float Proximity = 2.5f;

        /// <summary>メニューを閉じる距離 (m)。**Proximity より大きい**こと (Verify が検査)。</summary>
        const float HideDistance = 4.0f;

        /// <summary>メニュー板の位置 (keeper ローカル)。おじいさんの右横・目の高さ弱。
        /// **設計値なので毎回書く** (棚の MenuBasePos と同じ扱い)。
        /// 立ち位置そのもの (keeper root) は人間が決めるので触らない。</summary>
        static readonly Vector3 PivotLocalPos = new Vector3(0.7f, 1.35f, 0f);

        // Canvas は 1px = 1mm (localScale 0.001)。720×500px = 0.72 × 0.50 m
        // (IMP-ENV-005 v2.4: よくあるサイドバー型 — **左にナビ 7 ボタンを固定**し、
        //  右の表示エリアだけが切り替わる。ページ遷移なし。初期表示は挨拶。
        //  900px の縦 1 枚板は「首の上下が疲れる」で差し戻し済み — 縦ではなく横に使う)
        const float MenuW = 720f;
        const float MenuH = 500f;

        // ---- 左ナビ列 (固定) ----
        const float NavX = -250f;          // 列の中心 x
        // IMP-ENV-020: 話題 7 → 8 (呼ぶこと)。8 段 × 50px ピッチ = 先頭 200 〜 末尾 −150 (下端 −172) で、
        // またね (−205、上端 −183) と重ならない。板 (720×500) は伸ばさない (v2.3 の差し戻し)
        static readonly Vector2 NavSize = new Vector2(200f, 44f);
        const float NavTopY = 200f;        // 先頭ボタンの中心 y
        const float NavPitch = 50f;
        const float NavByeY = -205f;       // またね (話題と間を空けて最下段)

        // ---- 右表示エリア ----
        const float ContentX = 110f;       // 右エリアの中心 x
        static readonly Vector2 ScrollAreaCenter = new Vector2(ContentX, -40f);
        static readonly Vector2 ScrollAreaSize = new Vector2(480f, 370f);
        const float ScrollbarW = 14f;

        /// <summary>スクロール content の幅 (px) = viewport 幅。
        /// **Verify の溢れ検査と Setup の生成が同じ値を見る** — 横に溢れた字は
        /// スクロールでは救えない (縦は制限なし)。</summary>
        const float ContentWidth = 480f - ScrollbarW - 6f;   // BuildScrollArea の導出と同じ
        const float InfoBodyFontSize = 26f;
        /// <summary>本文の左右 margin (px)。文字の有効幅 = ContentWidth − BodyMarginX × 2。
        /// Setup と Verify の行幅検査が同じ値を見る (IMP-ENV-020 Codex 指摘: 460 全体を有効幅と
        /// 誤認していた)。</summary>
        const float BodyMarginX = 10f;

        // スクロールの配色 (板の背景に馴染ませる。つまみだけ明るく = 触れるもの)
        static readonly Color ViewportColor = new Color(0.12f, 0.10f, 0.08f, 0.92f);
        static readonly Color TrackColor = new Color(0.07f, 0.06f, 0.05f, 1f);
        static readonly Color HandleColor = new Color(0.55f, 0.48f, 0.35f, 1f);

        // ---- 効果音 (棚と同じ効果音ラボの音源。開閉とも軽いクリックでよい —
        // 「決定 / キャンセル」の意味差を音で出す場面ではない) ----
        const string SfxDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/SE/";
        const string SfxOpen = SfxDir + "決定ボタンを押す2.mp3";
        const string SfxClose = SfxDir + "決定ボタンを押す2.mp3";

        // ================================================================ 1: Program assets

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Shop Keeper/1 - Create Program Assets")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ShopKeeperSetup] Create program assets");

            bool ok = true;
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(KeeperProgram) != null)
            {
                log.AppendLine("already exists: " + KeeperProgram);
            }
            else
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(KeeperScript);
                if (script == null)
                {
                    log.AppendLine("FAIL script not found: " + KeeperScript);
                    ok = false;
                }
                else
                {
                    var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    asset.sourceCsScript = script;
                    AssetDatabase.CreateAsset(asset, KeeperProgram);
                    log.AppendLine("created: " + KeeperProgram);
                }
            }
            AssetDatabase.SaveAssets();

            log.AppendLine(ok
                ? "RESULT: PASS (force refresh してから 2 - Setup を実行する)"
                : "RESULT: FAIL");
            Finish("Logs/imp-env-004-keeper-program.txt", log);
        }

        // ================================================================ 2: Setup

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Shop Keeper/2 - Setup Shop Keeper")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ShopKeeperSetup] Setup");

            // ---- 台帳 (残高の読み取り先。無くても UI は組む — Verify が赤くする) ----
            var ledgerGo = KD_SceneObjects.Find(LedgerHost);
            var ledger = ledgerGo == null ? null : ledgerGo.GetComponent<KD_ProgressLedger>();
            if (ledger == null)
                log.AppendLine("WARN " + LedgerHost + " に KD_ProgressLedger が無い。"
                    + "残高が「台帳が未配線」のまま — 先に Progress System/2 を流すこと");

            // ---- ホスト (位置に意味がある。既にあれば動かさない) ----
            var keeperGo = EnsureKeeperHost(log);
            if (keeperGo == null)
            {
                Finish("Logs/imp-env-004-keeper-setup.txt", log);
                return;
            }

            var keeper = EnsureU<KD_ShopKeeper>(keeperGo, log);
            if (keeper == null)
            {
                Finish("Logs/imp-env-004-keeper-setup.txt", log);
                return;
            }
            KD_ShopSetupEditor.SetSync(keeper, VRC.SDKBase.Networking.SyncType.None, log);

            // ---- Interact の当たりと表示 ----
            //
            // Collider はホスト側に持つ (man_farm には子を足さない —
            // `PrefabUtility.IsPartOfPrefabInstance` の原則)。胴を覆う程度のカプセル
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

            var keeperUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(keeper);
            if (keeperUdon != null)
            {
                // 既定文言 "Use" のまま放置しない (KD_FetchSetupEditor と同じ検査型)
                keeperUdon.interactText = KD_ShopKeeper.InteractWord;
                keeperUdon.proximity = Proximity;
                EditorUtility.SetDirty(keeperUdon);
                log.AppendLine("interactText/proximity 設定");
            }
            else
            {
                log.AppendLine("WARN backing UdonBehaviour が取れない — force refresh 後に再実行");
            }

            // ---- NPC (man_farm) の是正 ----
            FixNpcInstance(keeperGo, log);

            // ---- メニュー ----
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                KD_ShopSetupEditor.SharedFontPath);
            if (font == null)
                log.AppendLine("WARN 共有フォントが無い: " + KD_ShopSetupEditor.SharedFontPath);
            BuildMenu(keeperGo, keeper, keeperUdon, font, log);

            // ---- 音 ----
            var sfx = EnsureAudio(keeperGo, log);
            keeper.sfx = sfx;
            keeper.openClip = LoadSfx(SfxOpen, log);
            keeper.closeClip = LoadSfx(SfxClose, log);

            // ---- 残りの配線 ----
            keeper.ledger = ledger;
            keeper.hideDistance = HideDistance;
            keeper.refreshSeconds = 1f;

            // ---- フォントへ字を焼く (文言の正本は KD_ShopKeeper の const 群) ----
            KD_ShopSetupEditor.AddGlyphs(KeeperGlyphs(), log);

            bool copied = TryCopy(keeper, log);

            log.AppendLine(copied
                ? "SETUP RESULT: PASS (scene not saved yet)"
                : "SETUP RESULT: FAIL (上の WARN を見ること)");
            Finish("Logs/imp-env-004-keeper-setup.txt", log);
        }

        /// <summary>ホストを 1 個用意する。**位置に意味があるので、既にあれば動かさない。**</summary>
        static GameObject EnsureKeeperHost(StringBuilder log)
        {
            int found;
            var go = KD_SceneObjects.Find(KeeperName, out found);
            if (found > 1)
            {
                log.AppendLine($"FAIL '{KeeperName}' がシーン内に {found} 個ある。1 個に整理してから再実行");
                return null;
            }
            if (go != null)
            {
                log.AppendLine("reused " + KeeperName + " at "
                    + go.transform.position.ToString("F2") + " (位置は動かさない)");
                return go;
            }

            go = new GameObject(KeeperName);
            KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.PlacesPath);

            // **仮置きの初期位置はおもちゃ棚の脇** (棚をおじいさんの近くへ寄せる設計の逆順 —
            // どうせ人間が両方まとめてシーンで置き直す。要件「買う → 選ぶ → その場で投げるが
            // 切れない」を最初から満たしておく)。root は地面の高さ
            int cnt;
            var shelf = KD_SceneObjects.Find(KD_DogDefs.ShopShelfName, out cnt);
            Vector3 basePos = (shelf != null && cnt == 1)
                ? shelf.transform.position + new Vector3(0.9f, 0f, 0f)
                : Vector3.zero;
            basePos.y = 0f;
            go.transform.position = basePos;
            log.AppendLine("created " + KeeperName + " at " + basePos.ToString("F2")
                + " (仮置き。man_farm をこの子へ置き、場所はシーンで決める)");
            return go;
        }

        /// <summary>man_farm インスタンスの是正。**すべてインスタンスオーバーライド** —
        /// 購入アセット本体には触らない。見つからなければ WARN (Verify が FAIL にする)。</summary>
        static void FixNpcInstance(GameObject keeperGo, StringBuilder log)
        {
            // **破壊する前に対象を一意にする** (2026-09-01 Codex 指摘) —
            // 最初に見つかった Animator を無条件に NPC とみなすと、将来メニュー側などへ
            // Animator が増えたとき「別物を壊して本物が未是正」が黙って起きる
            var animators = keeperGo.GetComponentsInChildren<Animator>(true);
            if (animators.Length == 0)
            {
                log.AppendLine("WARN keeper の子に Animator が無い — "
                    + "Assets/polyperfect/Low Poly Animated People/- Prefabs/man_farm.prefab を "
                    + KeeperName + " の子へ置くこと (位置・向きは自由)");
                return;
            }
            if (animators.Length > 1)
            {
                log.AppendLine("WARN keeper の子に Animator が " + animators.Length
                    + " 個ある — どれが NPC か決められないので是正しない。1 個に整理すること");
                return;
            }
            var animator = animators[0];

            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                KD_NpcAnimatorBuilderEditor.ControllerPath);
            if (ctrl == null)
            {
                log.AppendLine("WARN AC_KD_Npc が無い — 先に Npc Controller/1 - Build を流すこと");
            }
            else if (animator.runtimeAnimatorController != ctrl)
            {
                // デモ用 override (既定 State が移動系) のままだと勝手に歩き出す
                animator.runtimeAnimatorController = ctrl;
                EditorUtility.SetDirty(animator);
                log.AppendLine("npc: Controller を AC_KD_Npc へ差し替え");
            }

            if (animator.applyRootMotion)
            {
                // In Place クリップなので切る。true のままだと位置が漏れ出す恐れ
                animator.applyRootMotion = false;
                EditorUtility.SetDirty(animator);
                log.AppendLine("npc: applyRootMotion を false へ");
            }

            // デモ用コンポーネントを外す (VRChat ワールドで MonoBehaviour は動かず、
            // NavMeshAgent / CharacterController は据え置き NPC に不要)。
            // **順序が要る** — People_WanderScript が RequireComponent で
            // CharacterController 等に依存しており、先に依存先を消そうとすると
            // **DestroyImmediate が黙って失敗する** (実測 2026-09-01: ログは removed と
            // 出るのに CharacterController だけ残った)。スクリプト → Agent → Controller の順
            int removed = 0;
            void RemoveAll(System.Func<Component, bool> match)
            {
                foreach (var c in animator.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || !match(c)) continue;
                    var tn = c.GetType().Name;
                    Object.DestroyImmediate(c);
                    if (c == null)
                    {
                        log.AppendLine("npc: removed " + tn);
                        removed++;
                    }
                    else
                    {
                        log.AppendLine("WARN npc: " + tn + " を外せなかった (依存が残っている?)");
                    }
                }
            }
            RemoveAll(c => c.GetType().Name == "People_WanderScript");
            RemoveAll(c => c is UnityEngine.AI.NavMeshAgent);
            RemoveAll(c => c is CharacterController);
            if (removed == 0) log.AppendLine("npc: デモ用コンポーネントは既に無い");
        }

        /// <summary>説明メニューを組む。Canvas 1 枚のサイドバー型 (v2.4) —
        /// 左にナビ 7 ボタン (話題 6 + またね) を固定し、右の表示エリア
        /// (題 + スクロール本文) だけが切り替わる。ページ遷移なし。初期表示は挨拶。
        /// **既定は非アクティブ** — `Photo System/3` の想定外 Canvas 検査の対象。</summary>
        internal static void BuildMenu(GameObject keeperGo, KD_ShopKeeper keeper,
            VRC.Udon.UdonBehaviour keeperUdon, TMPro.TMP_FontAsset font, StringBuilder log)
        {
            // ---- Pivot (ビルボード回転の受け皿) ----
            var pivotT = keeperGo.transform.Find(PivotName);
            GameObject pivotGo = pivotT != null ? pivotT.gameObject : null;
            if (pivotGo == null)
            {
                pivotGo = new GameObject(PivotName);
                pivotGo.transform.SetParent(keeperGo.transform, false);
                log.AppendLine("created " + PivotName);
            }
            // 板の位置は設計値なので毎回書く (回転は実行時に Interact が書く)
            pivotGo.transform.localPosition = PivotLocalPos;
            pivotGo.transform.localScale = Vector3.one;

            // ---- Canvas ----
            var menuT = pivotGo.transform.Find(MenuName);
            GameObject menuGo = menuT != null ? menuT.gameObject : null;
            if (menuGo == null)
            {
                menuGo = new GameObject(MenuName, typeof(RectTransform));
                menuGo.transform.SetParent(pivotGo.transform, false);
                log.AppendLine("created " + MenuName);
            }
            // **layer は Default。** VRChat のポインタは "UI" レイヤーでは動かない
            menuGo.layer = 0;
            var canvas = menuGo.GetComponent<Canvas>();
            if (canvas == null) canvas = menuGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (menuGo.GetComponent<UnityEngine.UI.CanvasScaler>() == null)
                menuGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            if (menuGo.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                menuGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if (menuGo.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null)
                menuGo.AddComponent<VRC.SDK3.Components.VRCUiShape>();

            var rect = (RectTransform)menuGo.transform;
            rect.sizeDelta = new Vector2(MenuW, MenuH);
            rect.localScale = Vector3.one * 0.001f;   // 1px = 1mm
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;

            // 背景
            var bg = KD_ShopSetupEditor.EnsureUiChild(menuGo.transform, "Bg",
                Vector2.zero, new Vector2(MenuW, MenuH));
            var bgImg = bg.GetComponent<UnityEngine.UI.Image>();
            if (bgImg == null) bgImg = bg.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = new Color(0.12f, 0.10f, 0.08f, 0.92f);
            bgImg.raycastTarget = false;

            // ---- 旧レイアウトの残骸を掃除 ----
            //
            // v2.3 以前は Page 2 枚構成だった — ページごと消して 1 枚構成で組み直す。
            // スクロール構成 (BuildScrollArea) は冪等ではなく毎回作るので、
            // ContentScroll も消してから組む
            void Nuke(Transform parent, string name)
            {
                if (parent == null) return;
                var t = parent.Find(name);
                if (t == null) return;
                Object.DestroyImmediate(t.gameObject);
                log.AppendLine("removed stale " + name);
            }
            Nuke(menuGo.transform, "PageMain");
            Nuke(menuGo.transform, "PageInfo");
            Nuke(menuGo.transform, "ContentScroll");

            // ---- 右上: 残高 (固定・常時見える) ----
            var balance = EnsureText(menuGo.transform, "Balance",
                new Vector2(ContentX, 220f), new Vector2(460f, 40f), 26f,
                TMPro.TextAlignmentOptions.Left, font);
            balance.text = KD_ShopShelf.WordLoading + "...";

            // ---- 右: 題 (固定) + 表示エリア (スクロール) ----
            var infoTitle = EnsureText(menuGo.transform, "InfoTitle",
                new Vector2(ContentX, 175f), new Vector2(460f, 40f), 26f,
                TMPro.TextAlignmentOptions.Left, font);
            infoTitle.text = "";   // 挨拶表示中は題なし (実行時は ShowTopic が書く)

            GameObject contentViewport;
            var contentScroll = KD_CreditSetupEditor.BuildScrollArea(menuGo.transform,
                "ContentScroll", ScrollAreaCenter, ScrollAreaSize, ScrollbarW,
                ViewportColor, TrackColor, HandleColor, out contentViewport);
            // 本文そのものを content にする (クレジット掲示板と同型 —
            // ContentSizeFitter が話題ごとの行数差を実行時に吸収する)。
            // 上詰め (Top) — 中央揃えだと短い話題だけ題と本文の間が空く
            var infoBody = EnsureText(contentViewport.transform, "InfoBody", Vector2.zero,
                new Vector2(ContentWidth, ScrollAreaSize.y), InfoBodyFontSize,
                TMPro.TextAlignmentOptions.TopLeft, font);
            // **本文だけ wrap を有効にする** (BUG-ENV-001 の安全網)。改行の正本は
            // 手動改行 (1 行 ≤ 16 全角。Verify が実測) のままで、規約が破れた行だけ
            // 折り返す — wrap 無効だと Viewport の RectMask2D で**黙って左右が欠ける**。
            // 増えた縦はスクロールが吸収する。
            // Balance / InfoTitle は wrap にしない — 1 行 40px の枠なので、折り返すと
            // 2 行目が枠外の下へ描かれて他の UI に重なる。あちらは幅の実測検査で守る
            infoBody.enableWordWrapping = true;
            infoBody.margin = new Vector4(BodyMarginX, 0f, BodyMarginX, 0f);
            var infoBodyRect = (RectTransform)infoBody.transform;
            infoBodyRect.anchorMin = new Vector2(0f, 1f);
            infoBodyRect.anchorMax = new Vector2(1f, 1f);
            infoBodyRect.pivot = new Vector2(0.5f, 1f);
            infoBodyRect.anchoredPosition = Vector2.zero;
            infoBodyRect.sizeDelta = new Vector2(0f, ScrollAreaSize.y);
            var fitter = infoBody.gameObject.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fitter == null)
                fitter = infoBody.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            infoBody.text = KD_ShopKeeper.WordGreeting;   // 初期表示は挨拶
            contentScroll.content = infoBodyRect;

            // ---- 左ナビ (固定): 話題 8 + またね ----
            //
            // **角丸 + 枠は手元メニューと同じスプライトを当てる** (IMP-ENV-006 —
            // 押せる面の文法を 2 枚の板で揃える。丸めるのは押せるものだけ)。
            // スプライトは乗算なので、選択中ハイライトの色コードは 1 行も変わらない
            var round = KD_UiSpriteEditor.LoadRound();
            if (round == null)
                log.AppendLine("WARN 角丸スプライトが無い — 先に "
                    + "Tools/KominkaHillside/Menu/1 Bake UI Sprite を流すこと "
                    + "(無くても動くが、意匠は直角矩形のまま)");
            var navColor = new Color(0.30f, 0.27f, 0.22f);
            string[] btnNames = { "HowToButton", "PettingButton", "DogsButton",
                "PhotosButton", "ShopButton", "TreatsButton", "WaysButton", "CallButton" };
            string[] btnWords = { KD_ShopKeeper.WordHowTo, KD_ShopKeeper.WordPetting,
                KD_ShopKeeper.WordDogs, KD_ShopKeeper.WordPhotos,
                KD_ShopKeeper.WordShop, KD_ShopKeeper.WordTreats, KD_ShopKeeper.WordWays,
                KD_ShopKeeper.WordCall };
            string[] btnEvents = { "_OpenHowTo", "_OpenPetting", "_OpenDogs",
                "_OpenPhotos", "_OpenShop", "_OpenTreatInfo", "_OpenWays", "_OpenCall" };
            var navImages = new UnityEngine.UI.Image[btnNames.Length];
            for (int i = 0; i < btnNames.Length; i++)
            {
                var b = KD_ShopSetupEditor.EnsureButton(menuGo.transform, btnNames[i],
                    new Vector2(NavX, NavTopY - NavPitch * NavRow(i)), NavSize,
                    navColor, font, btnWords[i], keeperUdon, btnEvents[i], log);
                navImages[i] = b.GetComponent<UnityEngine.UI.Image>();
                KD_UiSpriteEditor.ApplyRoundSkin(navImages[i], round);
            }
            var bye = KD_ShopSetupEditor.EnsureButton(menuGo.transform, "ByeButton",
                new Vector2(NavX, NavByeY), NavSize, new Color(0.34f, 0.30f, 0.28f),
                font, KD_ShopKeeper.WordBye, keeperUdon, "_Close", log);
            KD_UiSpriteEditor.ApplyRoundSkin(
                bye.GetComponent<UnityEngine.UI.Image>(), round);

            // ---- 配線と既定状態 ----
            keeper.pivot = pivotGo.transform;
            keeper.menuRoot = menuGo;
            keeper.balanceLabel = balance;
            keeper.infoTitle = infoTitle;
            keeper.infoBody = infoBody;
            keeper.contentScroll = contentScroll;
            keeper.navImages = navImages;
            keeper.navColor = navColor;
            keeper.navActiveColor = new Color(0.52f, 0.43f, 0.26f);

            // **既定は閉じておく** (開いたまま保存すると入室した瞬間から板が浮いている)
            menuGo.SetActive(false);
        }

        /// <summary>TMP テキスト 1 枚 (KD_TreatSetupEditor.EnsureText と同型)。</summary>
        static TMPro.TextMeshProUGUI EnsureText(Transform parent, string name, Vector2 pos,
            Vector2 size, float fontSize, TMPro.TextAlignmentOptions align,
            TMPro.TMP_FontAsset font)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(parent, name, pos, size);
            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp == null) tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            return tmp;
        }

        // ================================================================ 3: Verify

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Shop Keeper/3 - Verify Shop Keeper")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_ShopKeeperSetup] Verify");
            int fail = 0;
            void Check(bool cond, string what)
            {
                log.AppendLine((cond ? "  ok   " : "  FAIL ") + what);
                if (!cond) fail++;
            }

            int found;
            var keeperGo = KD_SceneObjects.Find(KeeperName, out found);
            Check(keeperGo != null && found == 1, KeeperName + " がシーンにちょうど 1 個");
            if (keeperGo == null)
            {
                FinishVerify(log, fail);
                return;
            }

            var keeper = keeperGo.GetComponent<KD_ShopKeeper>();
            Check(keeper != null, "KD_ShopKeeper が付いている");
            if (keeper == null)
            {
                FinishVerify(log, fail);
                return;
            }

            // ---- Interact ----
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(keeper);
            Check(ub != null && ub.SyncMethod == VRC.SDKBase.Networking.SyncType.None,
                "SyncMethod None");
            Check(ub != null && ub.interactText == KD_ShopKeeper.InteractWord,
                "interactText が設計値 (既定の \"Use\" のまま放置しない)");
            Check(ub != null && Mathf.Abs(ub.proximity - Proximity) < 0.01f,
                "proximity = " + Proximity);
            Check(keeperGo.GetComponent<Collider>() != null, "Interact 用の Collider がある");
            Check(keeper.hideDistance > Proximity,
                "hideDistance > proximity (話しかけた瞬間に閉じない)");

            // ---- 配線 ----
            Check(keeper.ledger != null, "ledger 配線 (残高の読み取り先)");
            Check(keeper.pivot != null, "pivot 配線");
            Check(keeper.menuRoot != null, "menuRoot 配線");
            Check(keeper.balanceLabel != null && keeper.infoTitle != null
                && keeper.infoBody != null, "TMP 3 枚の配線");
            Check(keeper.sfx != null, "AudioSource 配線");
            if (keeper.openClip == null || keeper.closeClip == null)
                log.AppendLine("  WARN 開閉音が未配線 (無音で動く — 音源を確認)");

            // ---- メニュー Canvas ----
            var menuGo = keeper.menuRoot;
            if (menuGo != null)
            {
                Check(!menuGo.activeSelf,
                    "メニューが既定非アクティブ (BUG-CAM-002 の完了条件を引き継ぐ)");
                Check(menuGo.layer == 0, "layer が Default (\"UI\" だとポインタが効かない)");
                Check(menuGo.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null,
                    "VRCUiShape がある");
                Check(menuGo.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null,
                    "GraphicRaycaster がある");

                // ボタン配線: 9 個 (左ナビ = 話題 8 + またね。もどる は v2.4 で消えた —
                // ナビが常時見えているので戻る操作が要らない)。
                // **本数だけでなく飛び先 (SendCustomEvent の文字列引数) まで照合する** —
                // 本数だけだと「あそびかた が _OpenDogs へ誤配線」でも通る (2026-09-01 Codex 指摘)
                var buttons = menuGo.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                Check(buttons.Length == 9, "ボタンが 9 個 (実際 " + buttons.Length + ")");
                string[] wantBtn = { "HowToButton", "PettingButton", "DogsButton",
                    "PhotosButton", "ShopButton", "TreatsButton", "WaysButton", "CallButton", "ByeButton" };
                string[] wantEvent = { "_OpenHowTo", "_OpenPetting", "_OpenDogs",
                    "_OpenPhotos", "_OpenShop", "_OpenTreatInfo", "_OpenWays", "_OpenCall", "_Close" };
                for (int i = 0; i < wantBtn.Length; i++)
                {
                    UnityEngine.UI.Button b = null;
                    foreach (var c in buttons) if (c.name == wantBtn[i]) b = c;
                    if (b == null)
                    {
                        Check(false, wantBtn[i] + " が存在する");
                        continue;
                    }
                    int n = b.onClick.GetPersistentEventCount();
                    Object tgt;
                    string arg = OnClickStringArg(b, out tgt);
                    Check(n == 1 && tgt == (Object)ub
                        && b.onClick.GetPersistentMethodName(0) == "SendCustomEvent"
                        && arg == wantEvent[i],
                        wantBtn[i] + " → SendCustomEvent(\"" + wantEvent[i]
                        + "\") がちょうど 1 本 (実際 " + n + " 本 / \"" + arg + "\")");
                }

                // ボタンの意匠: 押せるものは全部 角丸 + 枠 = Sliced (IMP-ENV-006 —
                // 手元メニューと同じ検査。`Filled` / `Simple` へ戻ると見た目だけ壊れて
                // 例外もログも出ない)
                var wantRound = KD_UiSpriteEditor.LoadRound();
                Check(wantRound != null,
                    "角丸スプライトが焼かれている (" + KD_UiSpriteEditor.RoundSpritePath + ")");
                foreach (var b in buttons)
                {
                    var bImg = b.GetComponent<UnityEngine.UI.Image>();
                    Check(bImg != null && bImg.sprite == wantRound
                        && bImg.type == UnityEngine.UI.Image.Type.Sliced,
                        b.name + " が角丸スプライト + Sliced (実際 "
                        + (bImg == null ? "Image無し" : bImg.type.ToString()) + ")");
                }

                // フォント: 共有フォントが刺さっているか (実機で豆腐にならないか)
                var wantFont = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                    KD_ShopSetupEditor.SharedFontPath);
                bool fontsOk = wantFont != null;
                foreach (var t in menuGo.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                    if (t.font != wantFont) fontsOk = false;
                Check(fontsOk, "全 TMP に共有フォントが刺さっている");

                // ---- スクロールとナビの配線 (IMP-ENV-005 v2.4) ----
                Check(keeper.contentScroll != null, "ContentScroll の配線");
                if (keeper.contentScroll != null)
                {
                    Check(keeper.contentScroll.content != null
                        && keeper.contentScroll.viewport != null
                        && keeper.contentScroll.verticalScrollbar != null
                        && keeper.contentScroll.vertical && !keeper.contentScroll.horizontal,
                        "ContentScroll が完全配線 (vertical only)");
                    // 本文 TMP そのものが content であること (別物を差すと
                    // ContentSizeFitter が効かず、長い話題の下端が切れる)
                    Check(keeper.infoBody != null && keeper.contentScroll.content ==
                        (RectTransform)keeper.infoBody.transform,
                        "InfoBody が ContentScroll の content");
                    // 溢れ検査は設計値 (InfoBodyFontSize / ContentWidth) で測るので、
                    // **実物が設計値どおりであることを先に固定する** (2026-09-01 Codex 指摘 —
                    // 実物の fontSize や幅がずれていたら、定数で測った検査は嘘になる)。
                    // wrap は**有効**であること (BUG-ENV-001 — 手動改行の規約が破れた行を
                    // 黙って欠けさせず折り返す安全網。改行の正本は手動のまま = 下の実測検査)
                    Check(keeper.infoBody != null
                        && Mathf.Abs(keeper.infoBody.fontSize - InfoBodyFontSize) < 0.01f
                        && Mathf.Abs(keeper.infoBody.characterSpacing) < 0.01f
                        && keeper.infoBody.enableWordWrapping,
                        "InfoBody が設計値 (fontSize " + InfoBodyFontSize
                        + " / 字間 0 / wrap 有効 = 溢れの安全網)");
                    Check(keeper.contentScroll.viewport != null && Mathf.Abs(
                        keeper.contentScroll.viewport.sizeDelta.x - ContentWidth) < 0.5f,
                        "viewport 幅が設計値 " + ContentWidth);
                }
                // navImages[i] は _Open* の navIndex と同じ順 (= Setup の btnNames の生成順)。
                // 長さと Button の有無だけでなく **どのボタンの背景か** まで照合する (Codex 指摘: 順序違い・重複だと
                // 押した話題と別のボタンが点灯するのに、本数検査は通ってしまう)
                string[] navOwners = { "HowToButton", "PettingButton", "DogsButton", "PhotosButton",
                    "ShopButton", "TreatsButton", "WaysButton", "CallButton" };
                bool navOk = keeper.navImages != null && keeper.navImages.Length == navOwners.Length;
                if (navOk)
                    for (int i = 0; i < navOwners.Length; i++)
                    {
                        var img = keeper.navImages[i];
                        if (img == null || img.GetComponent<UnityEngine.UI.Button>() == null
                            || img.gameObject.name != navOwners[i]) navOk = false;
                    }
                Check(navOk, "navImages 8 本が生成順どおり (" + string.Join("/", navOwners) + " の背景 Image。選択ハイライト用)");
                // ナビ 8 段 + またね が板に収まり、互いに重ならない (IMP-ENV-020 で詰めた寸法の回帰ゲート)。
                // **実物の RectTransform** を板 (menuRoot) の座標系で読む — 定数だけの計算だと、シーンで
                // ずらされたボタンが重なっていても通ってしまう (Codex 指摘)
                {
                    var menuRect = (RectTransform)menuGo.transform;
                    float lowest = float.MaxValue, byeTop = float.MinValue, byeBottom = float.MinValue;
                    var placed = new System.Collections.Generic.List<float[]>();
                    foreach (var b in buttons)
                    {
                        var rt = (RectTransform)b.transform;
                        Vector2 local = menuRect.InverseTransformPoint(rt.position);
                        float top = local.y + rt.rect.height * 0.5f, bottom = local.y - rt.rect.height * 0.5f;
                        if (b.name == "ByeButton") { byeTop = top; byeBottom = bottom; }
                        else { if (bottom < lowest) lowest = bottom; placed.Add(new[] { top, bottom }); }
                    }
                    bool disjoint = true;
                    for (int a = 0; a < placed.Count; a++)
                        for (int c = a + 1; c < placed.Count; c++)
                            if (placed[a][1] < placed[c][0] && placed[c][1] < placed[a][0]) disjoint = false;
                    Check(disjoint && lowest > byeTop + 4f && byeBottom > -MenuH * 0.5f,
                        "実物のナビ 8 段が互いに重ならず、下端 " + lowest.ToString("F0") + " > またね上端 "
                        + byeTop.ToString("F0") + " で、またね下端 " + byeBottom.ToString("F0") + " が板内");
                }

                // ---- 溢れ (IMP-ENV-005): **横幅だけ**実測する ----
                // 縦の溢れはスクロールが受けるが、横に溢れた字はスクロールでは
                // 救えない。本文は wrap が折り返してくれる (BUG-ENV-001 の安全網) が、
                // **折り返しは読みやすさの敗北**なので、手動改行が枠に収まることを
                // 従来どおり実測で守る (1 行 ≤ 16 全角の規約は据え置き)。
                //
                // 測り方は MaxLineWidth (グリフ送り幅の計算) — TMP の GetPreferredValues は
                // rect の状態に引きずられて 1/10 の値を返した (2026-09-01 実測。
                // 42 ≤ 430 で何でも通る「壊れた検出器」になっていた)。
                // **測定器そのものも対照群で検査する** — 既知の 10 全角が fontSize×10
                // 前後で測れることを先に確かめ、崩れたら測定ごと FAIL にする
                bool gaugeOk = false;
                {
                    float ctrl = MaxLineWidth(wantFont, InfoBodyFontSize, "あいうえおかきくけこ");
                    gaugeOk = ctrl > InfoBodyFontSize * 8f && ctrl < InfoBodyFontSize * 14f;
                    Check(gaugeOk, "溢れ測定器の対照群 (10 全角 = 実測 " + ctrl.ToString("F0")
                        + " / 期待 " + (InfoBodyFontSize * 8f).ToString("F0") + "〜"
                        + (InfoBodyFontSize * 14f).ToString("F0") + ")");

                    // 挨拶も表示エリアの本文として出る (v2.4 の初期表示) ので同じ枠で見る
                    string[] topics = { "挨拶", "挨拶 (はじめて)",
                        KD_ShopKeeper.WordHowTo, KD_ShopKeeper.WordPetting,
                        KD_ShopKeeper.WordDogs, KD_ShopKeeper.WordPhotos,
                        KD_ShopKeeper.WordShop, KD_ShopKeeper.WordTreats, KD_ShopKeeper.WordWays,
                        KD_ShopKeeper.WordCall };
                    // IMP-ENV-020: 測るのは**文言表** (表示の正本) の本文。const は ja の写しで、
                    // 一致は ConstMismatches が検査する (ズレると焼いた字・測った行が出る字と違う)。
                    // 文字の有効幅 = viewport 幅 − 本文 margin 左右 (Setup が BodyMarginX ずつ入れる)
                    var catalog = KD_LocalizationEditor.Load();
                    var mismatches = ConstMismatches(catalog);
                    Check(mismatches.Length == 0, "const が文言表 ja と一致 (" + CatalogKeys.Length
                        + " キー)" + (mismatches.Length == 0 ? "" : ": " + string.Join(", ", mismatches)));
                    float effective = ContentWidth - BodyMarginX * 2f;
                    for (int i = 0; i < BodyKeys.Length; i++)
                    {
                        float w = MaxLineWidth(wantFont, InfoBodyFontSize,
                            BodyText(catalog, "ja", BodyKeys[i], keeper));
                        Check(gaugeOk && w <= effective + 0.5f,
                            "本文 ja「" + topics[i] + "」の手動改行が有効幅に収まる (実測 "
                            + w.ToString("F0") + " / 幅 " + effective + ")");
                    }
                    // 実描画 (self 言語 = ja/en): 実物の InfoBody に流し込む。ja は自動折返しが
                    // 起きないこと (手動改行が正本)、en は段落だけ改行して自動折返しに任せるので
                    // 欠けが無いこと。client 言語は Editor で測れない (未収録字を LiberationSans の
                    // 幅で測ってしまう) ので CLIENT_UNMEASURED として記録だけ
                    if (keeper.infoBody != null && keeper.menuRoot != null)
                        foreach (var lang in catalog.languages)
                        {
                            if (!KD_LocalizationEditor.Bundled(lang))
                            {
                                log.AppendLine("  CLIENT_UNMEASURED " + lang.code + " bodies="
                                    + BodyKeys.Length + " (verify on device)");
                                continue;
                            }
                            for (int i = 0; i < BodyKeys.Length; i++)
                            {
                                string text = BodyText(catalog, lang.code, BodyKeys[i], keeper);
                                bool truncated; float height;
                                int lines = MeasureBody(keeper, text, out truncated, out height);
                                int manual = text.Split('\n').Length;
                                bool ok = lines > 0 && !truncated && (lang.code != "ja" || lines == manual);
                                Check(ok, "本文 " + lang.code + "「" + topics[i] + "」の実描画 (行 " + lines
                                    + " / 手動改行 " + manual + " / 高さ " + height.ToString("F0") + "px"
                                    + (truncated ? " / 欠け" : "") + ")");
                            }
                        }

                    // ---- スクロールの外に居るテキスト (BUG-ENV-001: 無検査だった) ----
                    //
                    // Balance / InfoTitle / ナビラベルは wrap 無効 + Overflow のまま置く
                    // (1 行 40〜60px の枠なので、折り返すと 2 行目が枠外の下へ描かれる)。
                    // **wrap で守れないぶん、ここで幅を実測する。**
                    // fontSize と枠は実物から読む — 定数で測ると実物とずれたとき嘘になる
                    if (keeper.balanceLabel != null)
                    {
                        float fs = keeper.balanceLabel.fontSize;
                        float frame = ((RectTransform)keeper.balanceLabel.transform)
                            .sizeDelta.x;
                        // 表示しうる 3 通りの最悪 (残高は 5 桁まで見ておく)
                        string[] worst = { KD_ShopShelf.WordRemain + " " + KD_ProgressLedger.MaxPoints + " pt",
                            KD_ShopShelf.WordLoading + "...", KD_ShopShelf.WordNoLedger };
                        float wMax = 0f;
                        foreach (var s in worst)
                        {
                            float w = MaxLineWidth(wantFont, fs, s);
                            if (w > wMax) wMax = w;
                        }
                        Check(gaugeOk && wMax <= frame + 0.5f
                            && !keeper.balanceLabel.enableWordWrapping,
                            "Balance の最悪ケースが枠に収まる (実測 " + wMax.ToString("F0")
                            + " / 幅 " + frame.ToString("F0") + " / wrap 無効)");
                    }
                    if (keeper.infoTitle != null)
                    {
                        float fs = keeper.infoTitle.fontSize;
                        float frame = ((RectTransform)keeper.infoTitle.transform)
                            .sizeDelta.x;
                        float wMax = 0f;
                        for (int i = 1; i < topics.Length; i++)   // [0] = 挨拶 (題なし)
                        {
                            float w = MaxLineWidth(wantFont, fs, topics[i]);
                            if (w > wMax) wMax = w;
                        }
                        Check(gaugeOk && wMax <= frame + 0.5f,
                            "InfoTitle の全題が枠に収まる (実測 " + wMax.ToString("F0")
                            + " / 幅 " + frame.ToString("F0") + ")");
                    }
                    foreach (var b in buttons)
                    {
                        var lblT = b.transform.Find("Label");
                        var lbl = lblT == null ? null
                            : lblT.GetComponent<TMPro.TextMeshProUGUI>();
                        if (lbl == null)
                        {
                            Check(false, b.name + " に Label が居る");
                            continue;
                        }
                        float w = MaxLineWidth(wantFont, lbl.fontSize, lbl.text);
                        float frame = ((RectTransform)b.transform).sizeDelta.x;
                        Check(gaugeOk && w <= frame + 0.5f,
                            b.name + " のラベル「" + lbl.text + "」がボタン幅に収まる (実測 "
                            + w.ToString("F0") + " / 幅 " + frame.ToString("F0") + ")");
                    }
                }
            }

            // ---- グリフ被覆 (焼く側と同じ関数で作った文字列を突き合わせる) ----
            var missing = KD_ShopSetupEditor.MissingGlyphs(KeeperGlyphs());
            Check(missing.Length == 0,
                missing.Length == 0 ? "フォントに全部の字が入っている"
                : "フォントに無い字 (" + missing.Length + "): " + missing);

            // ---- NPC (man_farm) ----
            var animators = keeperGo.GetComponentsInChildren<Animator>(true);
            Check(animators.Length == 1,
                "keeper の子に Animator がちょうど 1 個 (実際 " + animators.Length
                + ")。0 = man_farm を子へ置き Setup を再実行 / 2+ = 是正対象を決められない");
            var animator = animators.Length == 1 ? animators[0] : null;
            if (animator != null)
            {
                var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    KD_NpcAnimatorBuilderEditor.ControllerPath);
                Check(ctrl != null && animator.runtimeAnimatorController == ctrl,
                    "Controller が AC_KD_Npc (デモ用 override のままだと勝手に歩き出す)");
                Check(!animator.applyRootMotion, "applyRootMotion が false");

                int demo = 0;
                foreach (var c in animator.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var tn = c.GetType().Name;
                    if (tn == "People_WanderScript" || c is UnityEngine.AI.NavMeshAgent
                        || c is CharacterController) demo++;
                }
                Check(demo == 0, "デモ用コンポーネントが残っていない (実際 " + demo + ")");
            }

            FinishVerify(log, fail);
        }

        static void FinishVerify(StringBuilder log, int fail)
        {
            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : $"VERIFY RESULT: FAIL ({fail})");
            Finish("Logs/imp-env-004-keeper-verify.txt", log);
        }

        // ============================================================
        // 文言表との一致 (IMP-ENV-020)
        //
        // 表示の正本は UITranslations.json の KD_ShopKeeper.* (実行時は KD_Language 経由)。
        // const は ja の写し = 字形の宣言 (Setup が焼く) + 行幅検査の対象。両者がズレると
        // 「焼いた字 ≠ 出る字」「測った行 ≠ 出る行」になるので、Shop Keeper/3、Menu Finish/3・5、
        // KD_LocalizationBuildGuard が同じ関数で止める (Setup の迂回経路で漏れないように)。
        // ============================================================

        /// <summary>文言表の KD_ShopKeeper.* 20 キー (Interact 1・挨拶 2・題 9・本文 8)。</summary>
        public static readonly string[] CatalogKeys = {
            "KD_ShopKeeper.InteractWord", "KD_ShopKeeper.WordGreeting", "KD_ShopKeeper.WordGreetingGift",
            "KD_ShopKeeper.WordWays", "KD_ShopKeeper.WordHowTo", "KD_ShopKeeper.WordPetting",
            "KD_ShopKeeper.WordDogs", "KD_ShopKeeper.WordCall", "KD_ShopKeeper.WordPhotos", "KD_ShopKeeper.WordShop",
            "KD_ShopKeeper.WordTreats", "KD_ShopKeeper.WordBye",
            "KD_ShopKeeper.InfoWays", "KD_ShopKeeper.InfoHowTo", "KD_ShopKeeper.InfoPetting",
            "KD_ShopKeeper.InfoDogs", "KD_ShopKeeper.InfoCall", "KD_ShopKeeper.InfoPhotos", "KD_ShopKeeper.InfoShop",
            "KD_ShopKeeper.InfoTreats" };

        /// <summary>本文として表示エリアに出るキー (挨拶 2 + 話題 8)。Verify の topics と同順。</summary>
        static readonly string[] BodyKeys = {
            "KD_ShopKeeper.WordGreeting", "KD_ShopKeeper.WordGreetingGift",
            "KD_ShopKeeper.InfoHowTo", "KD_ShopKeeper.InfoPetting", "KD_ShopKeeper.InfoDogs",
            "KD_ShopKeeper.InfoPhotos", "KD_ShopKeeper.InfoShop", "KD_ShopKeeper.InfoTreats",
            "KD_ShopKeeper.InfoWays", "KD_ShopKeeper.InfoCall" };

        /// <summary>キーに対応する C# const の値。</summary>
        public static string ConstText(string key)
        {
            switch (key)
            {
                case "KD_ShopKeeper.InteractWord": return KD_ShopKeeper.InteractWord;
                case "KD_ShopKeeper.WordGreeting": return KD_ShopKeeper.WordGreeting;
                case "KD_ShopKeeper.WordGreetingGift": return KD_ShopKeeper.WordGreetingGift;
                case "KD_ShopKeeper.WordWays": return KD_ShopKeeper.WordWays;
                case "KD_ShopKeeper.WordHowTo": return KD_ShopKeeper.WordHowTo;
                case "KD_ShopKeeper.WordPetting": return KD_ShopKeeper.WordPetting;
                case "KD_ShopKeeper.WordDogs": return KD_ShopKeeper.WordDogs;
                case "KD_ShopKeeper.WordPhotos": return KD_ShopKeeper.WordPhotos;
                case "KD_ShopKeeper.WordShop": return KD_ShopKeeper.WordShop;
                case "KD_ShopKeeper.WordTreats": return KD_ShopKeeper.WordTreats;
                case "KD_ShopKeeper.WordBye": return KD_ShopKeeper.WordBye;
                case "KD_ShopKeeper.InfoWays": return KD_ShopKeeper.InfoWays;
                case "KD_ShopKeeper.InfoHowTo": return KD_ShopKeeper.InfoHowTo;
                case "KD_ShopKeeper.InfoPetting": return KD_ShopKeeper.InfoPetting;
                case "KD_ShopKeeper.InfoDogs": return KD_ShopKeeper.InfoDogs;
                case "KD_ShopKeeper.InfoPhotos": return KD_ShopKeeper.InfoPhotos;
                case "KD_ShopKeeper.InfoShop": return KD_ShopKeeper.InfoShop;
                case "KD_ShopKeeper.InfoTreats": return KD_ShopKeeper.InfoTreats;
                case "KD_ShopKeeper.WordCall": return KD_ShopKeeper.WordCall;
                case "KD_ShopKeeper.InfoCall": return KD_ShopKeeper.InfoCall;
            }
            throw new System.ArgumentException("not a KD_ShopKeeper const key: " + key);
        }

        /// <summary>const と文言表 ja が食い違うキーの一覧 (空 = 一致)。副作用なし。</summary>
        public static string[] ConstMismatches(KD_LocalizationEditor.Catalog catalog)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var key in CatalogKeys)
                if (KD_LocalizationEditor.Text(catalog, "ja", key).Replace("\r\n", "\n") != ConstText(key))
                    list.Add(key);
            return list.ToArray();
        }

        /// <summary>ビルドガード用: 食い違いがあれば例外で止める。</summary>
        public static void ValidateConstMatchesCatalog(KD_LocalizationEditor.Catalog catalog)
        {
            var m = ConstMismatches(catalog);
            if (m.Length > 0)
                throw new System.InvalidOperationException("KD_ShopKeeper const differs from UITranslations.json ja: "
                    + string.Join(", ", m) + ". Update the const in KD_ShopKeeper.cs to the catalog text (the const only declares glyphs).");
        }

        /// <summary>表示される本文。{0} は実行時 Body() と同じく台帳の keeperPoints の表示値に展開する。</summary>
        static string BodyText(KD_LocalizationEditor.Catalog catalog, string code, string key, KD_ShopKeeper keeper)
        {
            string text = KD_LocalizationEditor.Text(catalog, code, key).Replace("\r\n", "\n");
            if (text.Contains("{0}"))
                text = text.Replace("{0}", keeper != null && keeper.ledger != null
                    ? keeper.ledger.keeperPoints.ToString() : "100");
            return text;
        }

        /// <summary>実物の InfoBody に本文を流し込んで測る (非アクティブな板を一時的に起こす)。
        /// 戻り値は TMP の描画行数。文と表示状態は必ず元に戻す。</summary>
        static int MeasureBody(KD_ShopKeeper keeper, string text, out bool truncated, out float height)
        {
            var body = keeper.infoBody;
            string keep = body.text;
            bool wasActive = keeper.menuRoot.activeSelf;
            // 復元順 (Codex 指摘): 本文を戻す → レイアウト確定 → スクロール位置を戻す → active を戻す。
            // 本文の高さが変わったあとに verticalNormalizedPosition を戻さないと、最後に測った本文の高さで
            // 位置が決まったまま残る (ContentSizeFitter の再計算は遅延する)
            float keepScroll = keeper.contentScroll != null ? keeper.contentScroll.verticalNormalizedPosition : 1f;
            keeper.menuRoot.SetActive(true);
            try
            {
                body.text = text;
                UnityEngine.Canvas.ForceUpdateCanvases();
                body.ForceMeshUpdate(true, true);
                truncated = body.isTextTruncated;
                height = body.preferredHeight;
                return body.textInfo.lineCount;
            }
            finally
            {
                body.text = keep;
                UnityEngine.Canvas.ForceUpdateCanvases();
                body.ForceMeshUpdate(true, true);
                if (keeper.contentScroll != null) keeper.contentScroll.verticalNormalizedPosition = keepScroll;
                keeper.menuRoot.SetActive(wasActive);
            }
        }

        // ================================================================ 4: Render Topics

        /// <summary>IMP-ENV-020: 8 話題 + 挨拶 2 を self 言語 (ja/en) で描画して
        /// Temp/shop-keeper/npc-{code}-{key}.png に置く。本文が viewport より高い話題は末尾も
        /// -end.png に描く (Codex 指摘: 既存の npc-ja/en は InfoWays の先頭だけだった)。
        /// client 言語は Editor の字形が実機と違うので描かない。板・文・スクロール位置は元に戻す。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Shop Keeper/4 - Render Topics")]
        public static void RenderTopics()
        {
            const string outDir = "Temp/shop-keeper/";
            System.IO.Directory.CreateDirectory(outDir);
            var log = new StringBuilder();
            log.AppendLine("[KD_ShopKeeperSetup] Render Topics");
            var keeperGo = KD_SceneObjects.Find(KeeperName);
            var keeper = keeperGo == null ? null : keeperGo.GetComponent<KD_ShopKeeper>();
            if (keeper == null || keeper.menuRoot == null || keeper.infoBody == null || keeper.contentScroll == null)
            {
                log.AppendLine("FAIL keeper が未配線 — 先に 2 - Setup Shop Keeper");
                Finish(outDir + "render.txt", log);
                return;
            }
            var catalog = KD_LocalizationEditor.Load();
            string[] titleKeys = { "", "", "KD_ShopKeeper.WordHowTo", "KD_ShopKeeper.WordPetting",
                "KD_ShopKeeper.WordDogs", "KD_ShopKeeper.WordPhotos", "KD_ShopKeeper.WordShop",
                "KD_ShopKeeper.WordTreats", "KD_ShopKeeper.WordWays", "KD_ShopKeeper.WordCall" };
            string[] navNames = { "HowTo", "Petting", "Dogs", "Photos", "Shop", "Treats", "Ways", "Call", "Bye" };
            var menu = keeper.menuRoot;
            var labels = menu.GetComponentsInChildren<TMPro.TMP_Text>(true);
            var keepTexts = new string[labels.Length];
            for (int i = 0; i < labels.Length; i++) keepTexts[i] = labels[i].text;
            bool wasActive = menu.activeSelf;
            float keepScroll = keeper.contentScroll.verticalNormalizedPosition;
            try
            {
                menu.SetActive(true);
                var tr = menu.transform;
                float boardH = ((RectTransform)tr).rect.height * tr.lossyScale.y;
                Vector3 cam = tr.position - tr.forward * (boardH * 0.55f / Mathf.Tan(25f * Mathf.Deg2Rad));
                foreach (var lang in catalog.languages)
                {
                    if (!KD_LocalizationEditor.Bundled(lang)) { log.AppendLine("skip " + lang.code + " (client glyphs; verify on device)"); continue; }
                    foreach (var t in labels)
                        foreach (var n in navNames)
                            if (t.transform.parent != null && t.transform.parent.name == n + "Button")
                                t.text = KD_LocalizationEditor.Text(catalog, lang.code, "KD_ShopKeeper.Word" + n);
                    for (int i = 0; i < BodyKeys.Length; i++)
                    {
                        keeper.infoTitle.text = titleKeys[i] == "" ? "" : KD_LocalizationEditor.Text(catalog, lang.code, titleKeys[i]);
                        keeper.infoBody.text = BodyText(catalog, lang.code, BodyKeys[i], keeper);
                        UnityEngine.Canvas.ForceUpdateCanvases();
                        keeper.infoBody.ForceMeshUpdate(true, true);
                        string name = "npc-" + lang.code + "-" + BodyKeys[i].Substring("KD_ShopKeeper.".Length);
                        keeper.contentScroll.verticalNormalizedPosition = 1f;
                        UnityEngine.Canvas.ForceUpdateCanvases();
                        RenderAt(outDir + name + ".png", cam, tr.position, 50f);
                        float viewportH = keeper.contentScroll.viewport.rect.height;
                        float contentH = keeper.infoBody.preferredHeight;
                        if (contentH > viewportH + 1f)
                        {
                            keeper.contentScroll.verticalNormalizedPosition = 0f;
                            UnityEngine.Canvas.ForceUpdateCanvases();
                            RenderAt(outDir + name + "-end.png", cam, tr.position, 50f);
                        }
                        log.AppendLine(name + " lines=" + keeper.infoBody.textInfo.lineCount + " height="
                            + contentH.ToString("F0") + "/" + viewportH.ToString("F0") + (contentH > viewportH + 1f ? " (+end)" : ""));
                    }
                }
            }
            finally
            {
                // 復元順: 文 → レイアウト確定 → スクロール位置 → active (MeasureBody と同じ理由)
                for (int i = 0; i < labels.Length; i++) labels[i].text = keepTexts[i];
                UnityEngine.Canvas.ForceUpdateCanvases();
                keeper.infoBody.ForceMeshUpdate(true, true);
                keeper.contentScroll.verticalNormalizedPosition = keepScroll;
                menu.SetActive(wasActive);
            }
            Finish(outDir + "render.txt", log);
        }

        static void RenderAt(string path, Vector3 position, Vector3 target, float fov)
        {
            var go = new GameObject("KD_EditorKeeperPreview");
            var cam = go.AddComponent<Camera>();
            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                cam.enabled = false;
                cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                cam.fieldOfView = fov; cam.nearClipPlane = 0.03f; cam.farClipPlane = 60f; cam.cullingMask = ~0;
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; cam.targetTexture = null;
                Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
        }

        /// <summary>navImages の番号 (= ボタン生成順 = _Open* の navIndex) → 左ナビの表示段。
        /// 並びは「メニュー / 過ごし方 / なでかた / 犬のこと / 呼ぶこと / 写真のこと / おかいもの / おやつのこと」
        /// (開き方の次に「何ができるか」、その後に体験の詳細。IMP-INF-011 / IMP-ENV-020)。</summary>
        static int NavRow(int i)
        {
            switch (i)
            {
                case 0: return 0;   // HowTo
                case 6: return 1;   // Ways
                case 1: return 2;   // Petting
                case 2: return 3;   // Dogs
                case 7: return 4;   // Call
                case 3: return 5;   // Photos
                case 4: return 6;   // Shop
                case 5: return 7;   // Treats
            }
            return i;
        }

        // ---- helpers (他の SetupEditor と同型) ----

        /// <summary>Button の persistent onClick 1 本目の文字列引数と対象を読む
        /// (SendCustomEvent のイベント名照合用)。引数は public API に出ていないので
        /// SerializedProperty から読む。</summary>
        static string OnClickStringArg(UnityEngine.UI.Button b, out Object target)
        {
            target = null;
            var so = new SerializedObject(b);
            var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null || calls.arraySize < 1) return null;
            var call = calls.GetArrayElementAtIndex(0);
            target = call.FindPropertyRelative("m_Target").objectReferenceValue;
            return call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
        }

        /// <summary>フォントのグリフ送り幅から、テキスト内で最も長い行の幅 (px) を計算する。
        /// **TMP の GetPreferredValues を使わない** — rect の状態 (stretch アンカー +
        /// ContentSizeFitter + 非アクティブ) に引きずられて 1/10 の値を返すことがある
        /// (2026-09-01 実測)。レイアウト系を通さず字送りの足し算で測る。
        /// カーニングは無視 (JP はほぼ等幅なので実用上一致)。
        /// 未収録の字は全角幅で悲観する (収録の有無自体はグリフ被覆検査が見る)。</summary>
        static float MaxLineWidth(TMPro.TMP_FontAsset font, float fontSize, string text)
        {
            if (font == null || string.IsNullOrEmpty(text)) return 0f;
            float scale = fontSize / font.faceInfo.pointSize;
            float max = 0f, line = 0f;
            foreach (char c in text)
            {
                if (c == '\n') { if (line > max) max = line; line = 0f; continue; }
                TMPro.TMP_Character ch;
                if (font.characterLookupTable.TryGetValue(c, out ch))
                    line += ch.glyph.metrics.horizontalAdvance * scale;
                else
                    line += fontSize;
            }
            if (line > max) max = line;
            return max;
        }

        /// <summary>おじいさんが表示しうる字の全部。**焼く側 (Setup) と検査側 (Verify) が
        /// 同じ関数を使う** — 別々に列挙すると片方だけ足し忘れて「豆腐なのに PASS」になる
        /// (2026-09-01 Codex 指摘: 借用語 (もっている/よみこみ中/台帳が未配線) と
        /// 数字・"pt" が初稿で両方から漏れていた。Shop Setup が先に焼いていれば偶然通るが、
        /// Keeper Setup 単独で保証できる形にする)。</summary>
        static string KeeperGlyphs()
        {
            var chars = new StringBuilder();
            chars.Append(KD_ShopKeeper.InteractWord)
                 .Append(KD_ShopKeeper.WordGreeting).Append(KD_ShopKeeper.WordGreetingGift)
                 .Append(KD_ShopKeeper.WordHowTo).Append(KD_ShopKeeper.WordPetting)
                 .Append(KD_ShopKeeper.WordDogs).Append(KD_ShopKeeper.WordPhotos)
                 .Append(KD_ShopKeeper.WordShop).Append(KD_ShopKeeper.WordTreats)
                 .Append(KD_ShopKeeper.WordWays).Append(KD_ShopKeeper.InfoWays)
                 .Append(KD_ShopKeeper.WordBye)
                 .Append(KD_ShopKeeper.InfoHowTo).Append(KD_ShopKeeper.InfoPetting)
                 .Append(KD_ShopKeeper.InfoDogs).Append(KD_ShopKeeper.InfoPhotos)
                 .Append(KD_ShopKeeper.InfoShop).Append(KD_ShopKeeper.InfoTreats)
                 .Append(KD_ShopKeeper.WordCall).Append(KD_ShopKeeper.InfoCall)
                 // 残高行の借用語 (KD_ShopShelf の const を RedrawBalance が使う)
                 .Append(KD_ShopShelf.WordRemain).Append(KD_ShopShelf.WordLoading)
                 .Append(KD_ShopShelf.WordNoLedger);
            for (char c = ' '; c <= '~'; c++) chars.Append(c);   // ASCII (数字・"pt"・"...")
            return chars.ToString();
        }

        static T EnsureU<T>(GameObject go, StringBuilder log) where T : UdonSharpBehaviour
        {
            var c = go.GetComponent<T>();
            if (c != null) return c;
            try
            {
                c = go.AddUdonSharpComponent<T>();
                log.AppendLine("added " + typeof(T).Name);
                return c;
            }
            catch (System.Exception e)
            {
                log.AppendLine("FAIL " + typeof(T).Name + " を付けられない: " + e.Message
                    + " — 先に 1 - Create Program Assets を流して force refresh すること");
                return null;
            }
        }

        static AudioSource EnsureAudio(GameObject go, StringBuilder log)
        {
            var a = go.GetComponent<AudioSource>();
            if (a == null)
            {
                a = go.AddComponent<AudioSource>();
                log.AppendLine("added AudioSource to " + go.name);
            }
            a.playOnAwake = false;
            a.loop = false;
            a.spatialBlend = 1f;      // 3D — おじいさんの位置から鳴る
            a.minDistance = 1.5f;
            a.maxDistance = 12f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.volume = 0.6f;
            return a;
        }

        static AudioClip LoadSfx(string path, StringBuilder log)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
                log.AppendLine("WARN 効果音が見つからない: " + path);
            return clip;
        }

        static bool TryCopy(UdonSharpBehaviour proxy, StringBuilder log)
        {
            if (proxy == null) return false;
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                EditorUtility.SetDirty(proxy);
                return true;
            }
            catch (System.Exception e)
            {
                log.AppendLine("CopyProxyToUdon FAILED (" + proxy.GetType().Name + "): " + e.Message);
                return false;
            }
        }

        static void Finish(string file, StringBuilder log)
        {
            var dir = System.IO.Path.GetDirectoryName(file);
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
