using System.Collections.Generic;
using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-PRG-002 ショップ: フォントへ字を足す / 確認パネル / UI・音の部品。本体は KD_ShopSetupEditor.cs。</summary>
    public static partial class KD_ShopSetupEditor
    {
        // ================================================================ フォントへ字を足す
        //
        // **フォントは Static なので、宣言に無い字は豆腐になる** (vrc-tmp-font §3)。
        // Dynamic は「エディタで表示しただけで焼かれて動いてしまう」ので、
        // 実機で初めて欠落が露見する。だから**焼くのを Setup の仕事にする。**

        const string FontAssetPath = "Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset";

        /// <summary>棚が使う字をフォントへ足す。**アセットを作り直さない** —
        /// `DeleteAsset` + `CreateAsset` は GUID が変わり、クレジット掲示板の
        /// 参照が切れて豆腐だらけになる (vrc-tmp-font §2)。
        /// 既存アセットを Dynamic へ一時的に戻して `TryAddCharacters` し、Static へ戻す。</summary>
        static void EnsureShopGlyphs(List<KD_ShopItem> items, StringBuilder log)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                log.AppendLine("WARN font asset が無いので字を足せない: " + FontAssetPath
                    + " — 先に Credit/1 - Build Font Asset を流すこと");
                return;
            }

            // **文言の正本は KD_ShopShelf の const 群。** ここで文字列を書き写すと
            // 「文言を直したのにフォントへ足し忘れる」が起きる (vrc-tmp-font §3 の実例)
            var chars = new StringBuilder();
            chars.Append(KD_ShopShelf.WordInUse).Append(KD_ShopShelf.WordUse)
                 .Append(KD_ShopShelf.WordBuy).Append(KD_ShopShelf.WordCancel)
                 .Append(KD_ShopShelf.WordSpecial).Append(KD_ShopShelf.WordLocked)
                 .Append(KD_ShopShelf.WordStock).Append(KD_ShopShelf.WordCount).Append(KD_ShopShelf.WordOwned)
                 .Append(KD_ShopShelf.WordRemain).Append(KD_ShopShelf.WordShort)
                 .Append(KD_ShopShelf.WordLoading).Append(KD_ShopShelf.WordBought)
                 .Append(KD_ShopShelf.WordNoLedger).Append(KD_ShopShelf.WordNotHere)
                 .Append(KD_ShopShelf.WordCantSelect).Append(KD_ShopShelf.WordCantBuy)
                 .Append(KD_ShopShelf.WordPack).Append(KD_ShopShelf.WordEnough);
            // **棚が 2 台あるので見出しも 2 つ。** 片方を足し忘れると、
            // その棚の見出しだけ実機で豆腐になる (エディタでは Dynamic 焼きで見えてしまう)。
            // **メニューの見出しも忘れない** — 2026-08-28 に Setup で文言を焼くようにして
            // 初めて「撫」「エ」「ェ」の 3 字が入っていないことが露見した
            // (それまでは実機でだけ豆腐になる潜在バグだった)
            chars.Append(ShelfTitle).Append(TreatShelfTitle).Append(MenuTitle);
            foreach (var it in items) if (it != null) chars.Append(it.displayName);
            for (char c = ' '; c <= '~'; c++) chars.Append(c);   // ASCII (数字・記号)

            AddGlyphs(chars.ToString(), log);
        }

        /// <summary>**フォントへ字を足す唯一の口。**
        ///
        /// 呼び手が増えても (棚 / 手元メニュー / 将来の売り場) 焼き方は 1 通りにする —
        /// `atlasPopulationMode` を Dynamic へ倒して戻す手順を写し取ると、
        /// **戻し忘れた側が「共有フォントがビルドへ ttf を持ち込む」状態を作る。**
        /// 呼び手が渡すのは「焼きたい文字列」だけ。</summary>
        public static void AddGlyphs(string wanted, StringBuilder log)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                log.AppendLine("WARN font asset が無いので字を足せない: " + FontAssetPath
                    + " — 先に Credit/1 - Build Font Asset を流すこと");
                return;
            }
            if (string.IsNullOrEmpty(wanted)) return;
            var chars = new StringBuilder(wanted);

            // 既に入っている字を除く (TryAddCharacters は冪等だが、ログを静かに保つ)
            var need = new StringBuilder();
            var seen = new HashSet<char>();
            foreach (var c in chars.ToString())
            {
                if (c == '\n' || c == '\r') continue;
                if (!seen.Add(c)) continue;
                if (!KD_LocalizationEditor.HasGlyph(font,c)) need.Append(c);
            }
            // **足す前に容量を測る** (`vrc-tmp-font` §1.1)。
            // アトラスが満杯だと `TryAddCharacters` は**入力を丸ごと missing で返す**ので、
            // 「フォントにこの字が無い」と読んで fallback を疑う方向へ 1 往復まるごと溶かす。
            // 測るのは**異なりグリフ数**だけ (アトラスが持つのはグリフなので、
            // 重複込みの文字数を見ても意味が無い)。**毎回出す** —
            // 「作成時にだけ出るログ」はアセットが在る限り一生走らない
            LogAtlasCapacity(font, need.Length, log);

            if (need.Length == 0)
            {
                log.AppendLine("font: 追加する字は無い (全部入っている)");
                return;
            }

            var prevMode = font.atlasPopulationMode;
            string missing = null;
            bool ok = false;
            try
            {
                font.atlasPopulationMode = TMPro.AtlasPopulationMode.Dynamic;
                ok = font.TryAddCharacters(need.ToString(), out missing);
            }
            finally
            {
                // **必ず元へ戻す** (2026-08-23 Codex 指摘)。例外で Dynamic のまま残ると、
                // **共有フォントがビルドへ ttf を持ち込む状態**になり、しかも
                // 「エディタで表示しただけで焼かれて動く」ので誰も気づかない
                font.atlasPopulationMode = prevMode;
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssets();
            }

            if (!ok && !string.IsNullOrEmpty(missing))
                // **アトラス満杯でも同じ症状が出る** (vrc-tmp-font §1) —
                // 収録済みの字まで missing で返るので、字が無いと即断しない
                log.AppendLine($"WARN 焼けなかった字 ({missing.Length} 字): {missing}"
                    + " — アトラス容量も疑うこと (2048² / 60pt で約 780 グリフ)");
            else
                log.AppendLine($"font: {need.Length} 字を追加 (glyph {font.glyphTable.Count})");
        }

        /// <summary>アトラスの容量と残りを測って出す (`vrc-tmp-font` §1.1)。
        ///
        /// <code>floor(atlasWidth / (pointSize + 2 * padding))^2 ≒ 収まるグリフ数</code>
        ///
        /// **95% を超えたら「先に広げろ」と言う** — 満杯になってからでは
        /// 「容量切れ」と「元 TTF に字が無い」を症状で見分けられない (§1.5)。</summary>
        static void LogAtlasCapacity(TMPro.TMP_FontAsset font, int adding, StringBuilder log)
        {
            int size = font.atlasWidth;
            int pt = (int)font.faceInfo.pointSize;
            int pad = font.atlasPadding;
            int cell = pt + 2 * pad;
            if (size <= 0 || cell <= 0) return;
            int per = size / cell;
            int cap = per * per;
            int have = font.glyphTable.Count;
            log.AppendLine($"font atlas: {size}^2 / {pt}pt / padding {pad} → 容量 ~{cap} グリフ"
                + $" (収録 {have} + 追加 {adding} = {have + adding})");
            if (cap > 0 && (have + adding) > cap * 95 / 100)
                log.AppendLine("WARN アトラスが容量の 95% を超える — **足す前に広げること**"
                    + " (満杯だと TryAddCharacters が入力を丸ごと missing で返す。vrc-tmp-font §1)");
        }

        /// <summary>フォントに**入っていない**字を返す (Verify 用。空なら全部入っている)。
        ///
        /// **エディタでは Dynamic 焼きのせいで見えてしまう**ので、目視では欠落が分からない。
        /// 焼く側 (<see cref="AddGlyphs"/>) と同じ文言の const 群を渡すこと。
        /// 判定は焼く側と同じ <see cref="KD_LocalizationEditor.HasGlyph"/> (主フォント + fallback 表。fallback アセットは直接参照で Bundle に入る)。</summary>
        public static string MissingGlyphs(string wanted)
        {
            if (string.IsNullOrEmpty(wanted)) return "";
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontAssetPath);
            if (font == null) return wanted;
            var missing = new StringBuilder();
            var seen = new HashSet<char>();
            foreach (var c in wanted)
            {
                if (c == '\n' || c == '\r') continue;
                if (!seen.Add(c)) continue;
                if (!KD_LocalizationEditor.HasGlyph(font, c)) missing.Append(c);
            }
            return missing.ToString();
        }

        /// <summary>共有フォントのパス (Setup が TMP へ差すのに使う)。
        /// **`TMP_Text.font` の setter は Udon に無い**ので、差せるのはエディタだけ。</summary>
        public static string SharedFontPath { get { return FontAssetPath; } }

        /// <summary>おやつ商品 <paramref name="i"/> のシーン上の名前 (陳列オブジェクト)。
        /// **手元メニューは棚と同じ `KD_ShopItem` を指す** — 値段・在庫・上限の正本を
        /// 2 つにしないため。</summary>
        public static string TreatItemNameAt(int i) { return TreatItemName(Treats[i]); }

        // ================================================================ 確認パネル
        //
        // **売り場の形はカテゴリで違うが、確認の型は 1 つに揃える** (2026-08-23 ユーザー決定)。
        // ここで作る Canvas は**おもちゃ棚だけのものではない** — Phase 3 の撫でエフェクトも
        // Phase 4b のおやつも、メニューから同じパネルを開く。
        //
        // World Space Canvas を VRChat で動かす要件 (`vrc-udonsharp` §5):
        //   Canvas + GraphicRaycaster + **VRCUiShape** + BoxCollider / layer は **Default**
        // ("UI" レイヤーだと動かない。これを外すと「押せるのに反応しない」になる)

        const string ConfirmName = "KD_ShopConfirm";

        /// <summary>「はい / いいえ」の確認パネルを作る。**既定は非アクティブ。**</summary>
        internal static GameObject EnsureConfirmPanel(GameObject shelfGo, KD_ShopShelf shelf,
            TMPro.TMP_FontAsset font, float faceYaw, float confirmHeight, StringBuilder log)
        {
            var t = shelfGo.transform.Find(ConfirmName);
            GameObject go = t != null ? t.gameObject : null;
            if (go == null)
            {
                go = new GameObject(ConfirmName, typeof(RectTransform));
                go.transform.SetParent(shelfGo.transform, false);
                log.AppendLine("created " + ConfirmName);
            }

            // **layer は Default。** VRChat のポインタは "UI" レイヤーでは動かない
            go.layer = 0;
            var canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (go.GetComponent<UnityEngine.UI.CanvasScaler>() == null)
                go.AddComponent<UnityEngine.UI.CanvasScaler>();
            if (go.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if (go.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null)
                go.AddComponent<VRC.SDK3.Components.VRCUiShape>();

            var rect = (RectTransform)go.transform;
            // **Canvas (= VRCUiShape の当たり) は常に 304 (ギフト行を含む高さ)。** 見える地 (Bg) だけを実行時に
            // 244 / 304 で切り替える (KD_ShopShelf.UpdateGiftRow)。VRCUiShape の当たりを実行時に変えない (IMP-PRG-012)
            rect.sizeDelta = new Vector2(420f, KD_ShopShelf.ConfirmBgHeightGift);
            // Canvas の 1 単位 = 1px なので、ワールドで 0.42m にするには 0.001 倍
            rect.localScale = Vector3.one * 0.001f;
            // **向きは棚ごとの面の向きへ揃える。** 恒等で書き潰していたので、
            // 手で回しても Setup を流すたびに戻っていた (2026-09-01)
            rect.localRotation = Quaternion.Euler(0f, faceYaw, 0f);

            // 背景 (押せる面でもある)。**上端に吸着** — ギフト行のときだけ下へ伸びる (中身は 244 のときの枠を基準に置く)
            var bg = EnsureUiChild(go.transform, "Bg", Vector2.zero, new Vector2(420f, KD_ShopShelf.ConfirmBgHeight));
            var bgRect = (RectTransform)bg.transform;
            bgRect.anchorMin = bgRect.anchorMax = new Vector2(0.5f, 1f);
            bgRect.pivot = new Vector2(0.5f, 1f);
            bgRect.anchoredPosition = Vector2.zero;
            var bgImg = bg.GetComponent<UnityEngine.UI.Image>();
            if (bgImg == null) bgImg = bg.AddComponent<UnityEngine.UI.Image>();
            bgImg.color = KD_UiSpriteEditor.SurfacePanel;
            bgImg.raycastTarget = false;

            // 品名と値段
            // 中身の Y は「244 の枠の中心」基準の値 + 30 (Canvas が 304 で中心が 30 下がったぶん)
            var titleGo = EnsureUiChild(go.transform, "Title", new Vector2(0f, 110f), new Vector2(372f, 48f));
            var titleTmp = titleGo.GetComponent<TMPro.TextMeshProUGUI>();
            if (titleTmp == null) titleTmp = titleGo.AddComponent<TMPro.TextMeshProUGUI>();
            if (font != null) titleTmp.font = font;
            titleTmp.fontSize = 28f;
            titleTmp.alignment = TMPro.TextAlignmentOptions.Left;
            titleTmp.color = Color.white;
            titleTmp.raycastTarget = false;
            titleTmp.enableWordWrapping = false;

            var shelfUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(shelf);
            var yes = EnsureButton(go.transform, "YesButton", new Vector2(-96f, -50f),
                new Vector2(180f, 52f), KD_UiSpriteEditor.ActionPrimary, font,
                KD_ShopShelf.WordBuy, shelfUdon, "_ConfirmYes", log);
            EnsureButton(go.transform, "NoButton", new Vector2(96f, -50f),
                new Vector2(180f, 52f), KD_UiSpriteEditor.ActionNeutral, font,
                KD_ShopShelf.WordCancel, shelfUdon, "_ConfirmNo", log);
            // [ギフトする] (IMP-PRG-012): ボタン列の下に 1 本。灰 = 何も消費せず別の画面を開く (D-010)。既定は非表示で、
            // 所有済みの買い切り CE のときだけ KD_ShopShelf.UpdateGiftRow が出す
            var gift = EnsureButton(go.transform, "GiftButton", new Vector2(0f, -110f),
                new Vector2(372f, 40f), KD_UiSpriteEditor.ActionNeutral, font,
                KD_ShopShelf.WordGift, shelfUdon, "_ConfirmGift", log);
            gift.SetActive(false);

            // ---- 資源バー (「あと N pt」の視覚化 — AvatarIdleArena IMP-UI-007 の移植) ----
            //
            // Title とボタン列の間の細帯。数字 (「あと N pt」) は Title に残す (数字併記)。
            // **幅は実行時に `anchorMax.x` だけで動かす** — `fillAmount` は sprite を要求し、
            // 引き伸ばしで角が破綻する (vrc-udonsharp §5「作り方」)。
            // 出す/出さない・色は KD_ShopShelf.UpdateConfirmBar が状態から決める
            var trough = EnsureUiChild(go.transform, "BarTrough",
                new Vector2(0f, -10f), new Vector2(372f, 8f));
            var troughImg = trough.GetComponent<UnityEngine.UI.Image>();
            if (troughImg == null) troughImg = trough.AddComponent<UnityEngine.UI.Image>();
            troughImg.color = KD_UiSpriteEditor.BarTrough;
            troughImg.raycastTarget = false;
            var fillGo = EnsureUiChild(trough.transform, "Fill", Vector2.zero, Vector2.zero);
            var fillRect = (RectTransform)fillGo.transform;
            // 左端へ貼り付け、右端 (anchorMax.x) だけが動く形
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fillGo.GetComponent<UnityEngine.UI.Image>();
            if (fillImg == null) fillImg = fillGo.AddComponent<UnityEngine.UI.Image>();
            fillImg.sprite = null;   // sprite 無し = 単色矩形。角の破綻が構造的に起きない
            fillImg.color = KD_UiSpriteEditor.BarFill;
            fillImg.raycastTarget = false;

            shelf.confirmRoot = go;
            shelf.confirmTitle = titleTmp;
            shelf.confirmDetail = ConfirmText(go.transform, "Detail", 60f, 38f, 22f, font);
            shelf.confirmPrice = ConfirmText(go.transform, "Price", 20f, 40f, 26f, font);
            KD_UiSpriteEditor.ApplyFlatSkin(bgImg);
            shelf.yesButton = yes;
            shelf.yesLabel = yes == null ? null
                : yes.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            shelf.giftButton = gift;
            shelf.giftLabel = gift == null ? null : gift.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            shelf.confirmBg = bgRect;
            shelf.confirmBar = trough;
            shelf.confirmBarFill = fillRect;
            shelf.confirmBarFillImage = fillImg;
            // **既定値も Setup が明示的に書く** (AvatarIdleArena IMP-SAV-002 の
            // 「沈黙既定 3 点セット」対策 — C# の既定値を設計値のつもりで放置しない)
            shelf.barColor = KD_UiSpriteEditor.BarFill;
            shelf.barFullColor = KD_UiSpriteEditor.AccentGold;
            // **商品からの高さも棚ごとに Setup が書く。** C# の既定 (0.42) を
            // 全棚の設計値のつもりで放置すると、商品の大きさが違う棚で
            // 「どの品の確認なのか」が切れる
            shelf.confirmHeight = confirmHeight;
            // メニュー行の逃がし先。**メニューを持たない棚 (おやつ) では使われない**が、
            // 値は両方の棚へ同じように書く — 「どちらの棚か」で挙動が変わるのは
            // `menuOnly` (商品の型) だけにしておく
            shelf.confirmMenuSideShift = MenuConfirmSideShift;
            shelf.confirmMenuLift = 0f;

            // **既定は閉じておく。** 開いたまま保存すると、入室した瞬間から
            // 宙にパネルが浮いている状態でワールドが始まる
            go.SetActive(false);
            return go;
        }

        static TMPro.TextMeshProUGUI ConfirmText(Transform parent, string name, float y,
            float height, float fontSize, TMPro.TMP_FontAsset font)
        {
            var go = EnsureUiChild(parent, name, new Vector2(0f, y), new Vector2(372f, height));
            var text = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (text == null) text = go.AddComponent<TMPro.TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TMPro.TextAlignmentOptions.Left;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        /// <summary>IMP-PET-006 Phase 3: **プレイヤーごとに複製される札。**
        ///
        /// `VRCPlayerObject` を付けた GameObject はシーンに 1 個置くだけで、
        /// **入室ごとに複製され、その人が Owner になる** (`vrc-networking` §9)。
        /// これが「A が選んだエフェクトを全員へ見せる」唯一の道 —
        /// 犬の同期変数は Owner しか書けず、Owner は撫で主の選択を知らないため。
        ///
        /// **雛形はシーンに残す** (実行時に複製元として使われる)。
        /// 中身は同期変数 1 本だけなので、人数ぶん増えても軽い。</summary>
        static void EnsurePlayerFxTag(StringBuilder log)
        {
            int found;
            var go = KD_SceneObjects.Find(PlayerFxTagName, out found);
            if (found > 1)
            {
                log.AppendLine("FAIL " + PlayerFxTagName + " が " + found + " 個");
                return;
            }
            if (go == null)
            {
                go = new GameObject(PlayerFxTagName);
                KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.SystemsPath);
                log.AppendLine("created " + PlayerFxTagName);
            }

            if (go.GetComponent<VRC.SDK3.Components.VRCPlayerObject>() == null)
            {
                go.AddComponent<VRC.SDK3.Components.VRCPlayerObject>();
                log.AppendLine("added VRCPlayerObject (入室ごとに複製される印)");
            }

            var tag = go.GetComponent<KD_PlayerFxTag>();
            if (tag == null)
            {
                try { tag = go.AddUdonSharpComponent<KD_PlayerFxTag>(); }
                catch (System.Exception e)
                {
                    log.AppendLine("FAIL KD_PlayerFxTag: " + e.Message
                        + " — 先に 1 - Create Program Assets を流して force refresh すること");
                    return;
                }
            }
            // **Manual。** 同期変数 1 本だが「1 人につき 1 本」なので性格が違う
            SetSync(tag, VRC.SDKBase.Networking.SyncType.Manual, log);
            TryCopy(tag, log);
        }

        /// <summary>**撫でエフェクトの近接メニューを撤去する** (2026-09-03。IMP-PET-007)。
        ///
        /// 切り替えは**手元メニューの「なで」タブ**へ移った
        /// ([hand-menu.md §3-3](../../../docs/development/design/hand-menu.md))。
        /// **撫でエフェクトは「個人装備」なので手元と相性がいい** —
        /// 共有物であるおもちゃの選択を手元へ入れないのと対照的で、
        /// 世界に出る物を切り替える操作は棚が正しい。
        ///
        /// **これで既知の問題が 2 つ同時に消える**:
        /// - [BUG-CAM-002] 写り込む板そのものが無くなる
        /// - [ui-map §5] 近接ゲートごと消えるので 7.4m ずれの論点が無くなる
        ///
        /// **棚の実物 6 個は残る** — 買うのは引き続き実物タッチ → 確認パネル。
        /// 陳列が「触れても何も起きない死んだ物」になる事態は起きない
        /// (npc-shop-menu §10 が名指しで嫌った形)。
        ///
        /// Setup は「作るだけ」が原則だが、**やめた物を消すのは作った側の後始末**
        /// (KD_TreatSetupEditor.RemovePouchBody と同じ判断)。ここでやらないと、
        /// 既にシーンへ置いた板が誰にも消されずに残る。</summary>
        static void RemoveEffectMenu(GameObject shelfGo, KD_ShopShelf shelf, StringBuilder log)
        {
            int removed = 0;
            foreach (var name in new[] { MenuName, MenuTitleName })
            {
                var t = shelfGo.transform.Find(name);
                if (t == null) continue;
                Object.DestroyImmediate(t.gameObject);
                removed++;
                log.AppendLine("removed " + name + " (撫でFXの切り替えは手元メニューへ — IMP-PET-007)");
            }
            // 商品アンカー (行のプレビューを置いていた子) の取りこぼしも掃除する
            for (int i = shelfGo.transform.childCount - 1; i >= 0; i--)
            {
                var c = shelfGo.transform.GetChild(i);
                if (!c.name.StartsWith(MenuName)) continue;
                Object.DestroyImmediate(c.gameObject);
                removed++;
                log.AppendLine("removed 近接メニューの残骸: " + c.name);
            }

            // (棚側の menuRoot / menu フィールドは KD_ShopShelf ごと削除した — IMP-PET-007)
            if (removed > 0)
                log.AppendLine("撫でFX近接メニューの撤去: " + removed + " 件 "
                    + "(BUG-CAM-002 の写り込みと ui-map §5 の 7.4m ずれが板ごと消える)");
        }

        /// <summary>行のプレビュー画像。**パーティクルのテクスチャそのものを出す。**
        ///
        /// UI の `Image` は Sprite しか受け取らないので、必要なら
        /// **インポート設定を Sprite へ変えて再インポートする** (元画像は触らない)。
        ///
        /// <paramref name="texName"/> が空 = 無料枠 (既存 ♡ を使う品目) なので
        /// 対応するテクスチャが無い。null を返して呼び手が薄く描く。</summary>
        static Sprite LoadPreviewSprite(string texName)
        {
            if (string.IsNullOrEmpty(texName)) return null;
            string path = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/Particle/" + texName + ".png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return null;
            if (imp.textureType != TextureImporterType.Sprite)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Canvas の子を 1 個。**売り場ごとに書き起こさない** —
        /// 手元メニュー (IMP-TRT-001 Phase 4b) も同じ部品を使う。</summary>
        public static GameObject EnsureUiChild(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var t = parent.Find(name);
            GameObject go = t != null ? t.gameObject : null;
            if (go == null)
            {
                go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
            }
            go.layer = parent.gameObject.layer;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            r.localScale = Vector3.one;
            r.localRotation = Quaternion.identity;
            return go;
        }

        /// <summary>ボタン 1 個。**onClick から Udon を叩く配線が肝。**
        /// `UnityEventTools.AddStringPersistentListener` は KanjiFind で実機確認済みの型。</summary>
        public static GameObject EnsureButton(Transform parent, string name, Vector2 pos, Vector2 size,
            Color color, TMPro.TMP_FontAsset font, string labelText,
            VRC.Udon.UdonBehaviour target, string customEvent, StringBuilder log)
        {
            var go = EnsureUiChild(parent, name, pos, size);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            if (img == null) img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            img.raycastTarget = true;

            var btn = go.GetComponent<UnityEngine.UI.Button>();
            if (btn == null) btn = go.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            KD_UiSpriteEditor.ApplyRoundSkin(img, KD_UiSpriteEditor.LoadRound());
            KD_UiSpriteEditor.ApplyButtonFeedback(btn);

            var lbl = EnsureUiChild(go.transform, "Label", Vector2.zero, size - new Vector2(24f, 4f));
            var tmp = lbl.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp == null) tmp = lbl.AddComponent<TMPro.TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize = size.y <= 44f ? 24f : 26f;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.text = labelText;

            // **毎回張り直す。** 追記していくと同じイベントが 2 回飛んで
            // 「1 回押したら 2 回買えた」になる
            for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(btn.onClick, i);
            if (target == null)
                log.AppendLine("WARN " + name + ": Udon が取れないので onClick を配線できない");
            else
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(
                    btn.onClick,
                    new UnityEngine.Events.UnityAction<string>(target.SendCustomEvent),
                    customEvent);
            return go;
        }

        /// <summary>効果音を読む。**無かったら黙って null にしない** —
        /// 音が鳴らないのは Play で最も気づきにくい壊れ方で、しかも
        /// 「買えた気がしない」という体験の判定そのものを濁らせる。</summary>
        static AudioClip LoadClip(string path, StringBuilder log)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
                log.AppendLine("WARN 効果音が見つからない: " + path
                    + " — 漢字間違い探し (KanjiFind) の Resources/Sounds から持ってきた "
                    + "効果音ラボの音源。消したなら再取得すること");
            else
                log.AppendLine("sfx: " + System.IO.Path.GetFileName(path));
            return clip;
        }

        /// <summary>棚の効果音。**2D で鳴らさない** — 棚の位置から鳴るほうが
        /// 「そこで何か起きた」と読める。</summary>
        static AudioSource EnsureAudio(GameObject shelf, StringBuilder log)
        {
            var a = shelf.GetComponent<AudioSource>();
            if (a == null)
            {
                a = shelf.AddComponent<AudioSource>();
                log.AppendLine("added AudioSource to " + shelf.name);
            }
            a.playOnAwake = false;
            a.loop = false;
            a.spatialBlend = 1f;      // 3D
            a.minDistance = 1.5f;
            a.maxDistance = 12f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.volume = 0.6f;
            return a;
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

        /// <summary>backing の SyncMethod を自分でそろえる。
        /// `AddUdonSharpComponent` が作る UdonBehaviour は既定の Continuous で生まれ、
        /// 直すのは UdonSharpEditorManager の検証パスに任される —
        /// **つまり Setup 直後の Verify が必ず赤くなる。** ここで確定させる。</summary>
        public static void SetSync(UdonSharpBehaviour b, VRC.SDKBase.Networking.SyncType want,
            StringBuilder log)
        {
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (ub == null)
            {
                log.AppendLine("WARN backing UdonBehaviour が取れない (" + b.GetType().Name
                    + ")。force refresh 後に再実行");
                return;
            }
            if (ub.SyncMethod == want) return;
            log.AppendLine(b.GetType().Name + " SyncMethod " + ub.SyncMethod + " -> " + want);
            ub.SyncMethod = want;
            EditorUtility.SetDirty(ub);
        }

    }
}
