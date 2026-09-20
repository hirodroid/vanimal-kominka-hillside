using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (Live)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_TreatPouch
    {
        // ============================================================
        // ライブタブ — **商品の一覧 → (未所持) 詳細 → 買う / (所持) 開催**
        //
        // 2026-09-16 IMP-INF-006 P7 / P8: 一覧の行 = 商品 (`liveProducts`。安い → 高い、CE は最後)。
        // 作品 (liveId) と商品の対応は従来どおり `liveItems[liveId]` (Pack は 3 作品が同じ商品を指す)。
        // 未所持の行は [くわしく] → 詳細の面 (雰囲気・見どころ・曲・同梱の撫でFX / 飾り) → [買う] → 確認カード。
        // 所持の行は [開催] (Pack は曲を選ぶ面へ)。試聴は置かない (2026-09-14 ユーザー決定)。
        //
        // 開催の可否は `KD_LiveSystem.CanStart` (現在の 1 本を保護 + 完了後 60 秒) が正本。
        // 板は所有 (`Owned`) と可否を読んで描くだけで、判定の式を写さない。
        // 行の口 `_Live0.._Live8` は面で意味が変わる: 一覧 = 商品の添字 / 曲を選ぶ面 = その商品の何曲目か。
        // ============================================================

        public const int LivePageList = 0;
        public const int LivePageDetail = 1;
        public const int LivePageSongs = 2;

        public void _Live0() { Live(0); }
        public void _Live1() { Live(1); }
        public void _Live2() { Live(2); }
        public void _Live3() { Live(3); }
        public void _Live4() { Live(4); }
        public void _Live5() { Live(5); }
        public void _Live6() { Live(6); }
        public void _Live7() { Live(7); }
        public void _Live8() { Live(8); }

        /// <summary>行を押した。一覧: 所持なら開催 (Pack は曲を選ぶ面)、未所持なら詳細の面。曲を選ぶ面: その曲を開催。</summary>
        private void Live(int row)
        {
            if (_livePage == LivePageSongs)
            {
                int liveId = LiveShowOf(LiveProductAt(_liveProduct), row);
                if (liveId >= 0) LiveStartShow(liveId);
                return;
            }
            if (_livePage != LivePageList) return;
            KD_ShopItem it = LiveProductAt(row);
            if (it == null || catalog == null) return;
            if (it.Owned()) { LiveHost(row); return; }
            _liveProduct = row; _livePage = LivePageDetail; _liveConfirmRow = -1;
            Redraw();
        }

        /// <summary>所持している商品を開催する: 1 曲ならそのまま、Pack は曲を選ぶ面へ。</summary>
        private void LiveHost(int product)
        {
            KD_ShopItem it = LiveProductAt(product);
            if (it == null) return;
            int n = LiveShowCount(it);
            if (n > 1) { _liveProduct = product; _livePage = LivePageSongs; _liveConfirmRow = -1; Redraw(); return; }
            int liveId = LiveShowOf(it, 0);
            if (liveId >= 0) LiveStartShow(liveId);
        }

        private void LiveStartShow(int liveId)
        {
            _liveConfirmRow = -1;
            if (live == null || !live.CanStart || !live.IsLocalAllowedToHost) { Feedback("ui.extra.043", false); Redraw(); return; }
            if (live.LocalRequestState == KD_LiveSystem.RequestPending) return;
            _awaitingLive = true;
            live._StartLive(liveId);
            PollLiveRequest();
            Redraw();
        }

        // ---- P10: インフォメーション帯 (イベントがあるときだけ 1 件) ----
        public const string HalloweenProductKey = "live.pumpkin_parade_set";

        /// <summary>期間限定の販売が終わったか (パンプキン・パレード: 2026-10-31 23:59 JST = 14:59 UTC)。買った人は以後も使える。
        /// **同じ CE Listing の品 (かざりのセット行、パーティクルのかぼちゃ) も同じ判定** — 売り場が 3 つあるので 1 か所で持つ。</summary>
        private bool LiveSaleClosed(KD_ShopItem it)
        {
            if (it == null) return false;
            bool halloween = it.productKey == HalloweenProductKey;
            if (!halloween && it.IsCE())
            {
                KD_ShopItem hp = LiveProductAt(LiveProductIndexOf(HalloweenProductKey));
                halloween = hp != null && it.ceListingId == hp.ceListingId;
            }
            if (!halloween) return false;
            return System.DateTime.Compare(System.DateTime.UtcNow, new System.DateTime(2026, 10, 31, 14, 59, 0)) >= 0;
        }

        private int LiveProductIndexOf(string productKey)
        {
            if (liveProducts == null) return -1;
            for (int i = 0; i < liveProducts.Length; i++)
                if (liveProducts[i] != null && liveProducts[i].productKey == productKey) return i;
            return -1;
        }

        /// <summary>帯を押した: ライブタブのパンプキン・パレードの詳細へ。</summary>
        public void _EventBand()
        {
            int product = LiveProductIndexOf(HalloweenProductKey);
            if (product < 0) return;
            SetTab(TabLive);
            _liveProduct = product; _livePage = LivePageDetail; _liveConfirmRow = -1;
            Redraw();
        }

        /// <summary>帯の表示 (どのタブでも)。販売中だけ出す。所持していれば「所持済み」の文。</summary>
        private void RedrawEventBand()
        {
            if (eventBandRoot == null) return;
            KD_ShopItem it = LiveProductAt(LiveProductIndexOf(HalloweenProductKey));
            // ようこそ (IMP-INF-020) では出さない — 初回に売り物を先に見せない
            bool show = !_welcome && it != null && !LiveSaleClosed(it);
            if (eventBandRoot.activeSelf != show) eventBandRoot.SetActive(show);
            if (show && eventBandLabel != null) eventBandLabel.text = L(it.Owned() ? "event.halloween.owned" : "event.halloween.text");
        }

        /// <summary>詳細・曲を選ぶ面の [もどる]。</summary>
        public void _LiveBack()
        {
            _livePage = LivePageList; _liveProduct = -1; _liveConfirmRow = -1;
            Redraw();
        }

        /// <summary>詳細の面の右下: 未所持なら [買う] (確認カードへ)、所持なら [開催]。</summary>
        public void _LiveDetailAction()
        {
            if (_livePage != LivePageDetail) return;
            KD_ShopItem it = LiveProductAt(_liveProduct);
            if (it == null || catalog == null) return;
            if (it.Owned()) { LiveHost(_liveProduct); return; }
            // CE (Pack / 専用セット): 購入情報が届いていれば確認カードへ (値段は Credits、[購入画面へ] は Store を開く)
            if (it.IsCE()) { if (it.CePurchasesLoaded()) _liveConfirmRow = _liveProduct; Redraw(); return; }
            if (!it.CanBuy()) { Redraw(); return; }
            _liveConfirmRow = _liveProduct;
            Redraw();
        }

        /// <summary>詳細の面 [パーティクルで見る]: 同梱の撫でFX はパーティクルタブに並ぶ。</summary>
        public void _LiveSeeFx() { SetTab(TabPetFx); }

        /// <summary>詳細の面 [かざりで見る]: 同梱の飾りはかざりタブのセット (犬を選ぶ面) へ。</summary>
        public void _LiveSeeAcc()
        {
            KD_ShopItem it = LiveProductAt(_liveProduct);
            int row = AccRowOfLiveSet(it);
            SetTab(TabAcc);
            if (row >= 0) { _accSet = -1; _accSetMenu = 0; _accConfirm = false; _accProduct = row; ResetAccessoryScroll(); Redraw(); }
        }

        /// <summary>確認カードの [購入]。**pt が減る唯一の口** (なでタブと同じ 2 段階)。
        /// CE 品は pt を動かさず Storefront を開くだけ (IMP-FET-010)。</summary>
        public void _LiveBuy()
        {
            KD_ShopItem it = LiveProductAt(_liveConfirmRow);
            _liveConfirmRow = -1;
            if (it == null || catalog == null || LiveSaleClosed(it)) { Feedback("KD_ShopShelf.WordCantBuy", false); Redraw(); return; }
            if (it.IsCE()) { it._OpenStore(); Redraw(); return; }
            // 買っても開催はしない — 「買う」と「開催」を 1 回の押下で混ぜない (なでタブと同じ規律)
            bool bought = catalog._RequestBuy(it.productKey);
            Feedback(bought ? "KD_ShopShelf.WordBought" : "KD_ShopShelf.WordCantBuy", bought);
            Redraw();
        }

        public void _LiveCancel()
        {
            _liveConfirmRow = -1;
            Redraw();
        }

        private KD_ShopItem LiveAt(int i)
        {
            if (liveItems == null || i < 0 || i >= liveItems.Length) return null;
            return liveItems[i];
        }

        private KD_ShopItem LiveProductAt(int i)
        {
            if (liveProducts == null || i < 0 || i >= liveProducts.Length) return null;
            return liveProducts[i];
        }

        /// <summary>行の曲名。`liveTitleKeys` が無い / 空なら商品名で代用 (Setup 前でも行が読める)。</summary>
        private string LiveTitleOf(int i, KD_ShopItem it)
        {
            if (liveTitleKeys != null && i >= 0 && i < liveTitleKeys.Length)
            {
                string t = liveTitleKeys[i];
                if (t != null && t.Length > 0) return L(t);
            }
            return it != null ? L(it.displayNameKey) : "-";
        }

        /// <summary>同じ商品を指す作品の数。2 以上ならその商品は Pack。
        /// **表から導く** — 「Pack かどうか」のフラグを別に持つと表と食い違う。</summary>
        private int LiveShowCount(KD_ShopItem it)
        {
            if (liveItems == null || it == null) return 0;
            int c = 0;
            for (int k = 0; k < liveItems.Length; k++)
                if (liveItems[k] == it) c++;
            return c;
        }

        /// <summary>その商品の n 番目の作品 (liveId)。無ければ -1。</summary>
        private int LiveShowOf(KD_ShopItem it, int n)
        {
            if (liveItems == null || it == null || n < 0) return -1;
            int c = 0;
            for (int k = 0; k < liveItems.Length; k++)
                if (liveItems[k] == it) { if (c == n) return k; c++; }
            return -1;
        }

        private int LiveRowsSharing(KD_ShopItem it) { return LiveShowCount(it); }

        /// <summary>行の札: Pack「ライブ 3 + 撫でFX 3」/ 専用セット「ライブ + 飾り 3 + 撫でFX 1」/ 単品は無し。</summary>
        private string LiveTagOf(KD_ShopItem it)
        {
            if (it == null) return "";
            if (AccRowOfLiveSet(it) >= 0) return L("live.tag.set");
            if (LiveShowCount(it) > 1) return L("live.tag.pack");
            return "";
        }

        /// <summary>詳細の文言キー: live.detail.&lt;productKey の live. 以降&gt;.&lt;part&gt;。</summary>
        private string LiveDetailKey(KD_ShopItem it, string part)
        {
            string k = it == null ? "" : it.productKey;
            if (k.StartsWith("live.")) k = k.Substring(5);
            return "live.detail." + k + "." + part;
        }

        /// <summary>同梱の撫でFX (同じ CE Listing の撫でFX) の名前を「・」で繋ぐ。無ければ空。</summary>
        private string LiveFxNames(KD_ShopItem it)
        {
            string s = "";
            if (it == null || !it.IsCE() || fxItems == null) return s;
            for (int i = 0; i < fxItems.Length; i++)
            {
                KD_ShopItem fx = fxItems[i];
                if (fx == null || !fx.IsCE() || fx.ceListingId != it.ceListingId) continue;
                s = s.Length == 0 ? L(fx.displayNameKey) : s + "・" + L(fx.displayNameKey);
            }
            return s;
        }

        /// <summary>この商品が飾りのセットでもあるなら、かざり一覧の行 (accProductIds の添字)。違えば -1。</summary>
        private int AccRowOfLiveSet(KD_ShopItem it)
        {
            if (it == null || accessories == null || accessories.setItems == null || accessories.setIds == null || accProductIds == null) return -1;
            for (int s = 0; s < accessories.setItems.Length && s < accessories.setIds.Length; s++)
            {
                if (accessories.setItems[s] != it) continue;
                for (int r = 0; r < accProductIds.Length; r++)
                    if (accProductIds[r] == accessories.setIds[s]) return r;
            }
            return -1;
        }

        /// <summary>同梱の飾り (セットの構成品) の名前を「・」で繋ぐ。無ければ空。</summary>
        private string LiveAccNames(KD_ShopItem it)
        {
            string s = "";
            if (it == null || accessories == null || accessories.setItems == null || accessories.setMembers == null || accNameKeys == null || accProductIds == null) return s;
            for (int set = 0; set < accessories.setItems.Length; set++)
            {
                if (accessories.setItems[set] != it) continue;
                for (int m = 0; m < 3 && set * 3 + m < accessories.setMembers.Length; m++)
                {
                    int id = accessories.setMembers[set * 3 + m];
                    for (int r = 0; r < accProductIds.Length && r < accNameKeys.Length; r++)
                        if (accProductIds[r] == id) { s = s.Length == 0 ? L(accNameKeys[r]) : s + "・" + L(accNameKeys[r]); break; }
                }
            }
            return s;
        }

        /// <summary>曲名を「 / 」で繋ぐ (Pack の詳細)。</summary>
        private string LiveSongNames(KD_ShopItem it)
        {
            string s = "";
            if (liveItems == null || it == null) return s;
            for (int k = 0; k < liveItems.Length; k++)
                if (liveItems[k] == it) s = s.Length == 0 ? LiveTitleOf(k, it) : s + " / " + LiveTitleOf(k, it);
            return s;
        }

        private string LivePriceText(KD_ShopItem it)
        {
            if (it == null) return "";
            if (it.Owned()) return L("accessory.owned");
            if (it.IsCE()) return it.ceCredits > 0 ? it.ceCredits.ToString() + " C" : L("KD_ShopShelf.WordSpecial");
            int gap = it.Shortfall();
            if (gap > 0) return it.price.ToString() + " pt (" + L("KD_TreatPouch.WordAccWaitPrefix") + " " + gap.ToString() + " pt)";
            return it.price.ToString() + " pt";
        }

        /// <summary>2 語を 2 スペースで繋ぐ (先頭が空ならスペースを入れない)。</summary>
        private string LiveJoin(string a, string b)
        {
            if (a == null || a.Length == 0) return b;
            if (b == null || b.Length == 0) return a;
            return a + "  " + b;
        }

        /// <summary>見出し: 一覧は「ライブをひらく」、詳細と曲を選ぶ面は商品名。</summary>
        private string LiveSectionTitle()
        {
            KD_ShopItem it = LiveProductAt(_liveProduct);
            if (_livePage != LivePageList && it != null) return L(it.displayNameKey);
            return L("KD_TreatPouch.WordLiveTitle");
        }

        /// <summary>ライブタブ。一覧 (行 = 商品) / 詳細 / 曲を選ぶ面。状態の 1 行は台帳から。</summary>
        private void RedrawLive()
        {
            bool canStart = live != null && live.CanStart && live.IsLocalAllowedToHost;
            bool hostOn = canStart && live.LocalRequestState != KD_LiveSystem.RequestPending;
            KD_ShopItem current = LiveProductAt(_liveProduct);
            if (_livePage != LivePageList && current == null) { _livePage = LivePageList; }
            // 一覧と曲を選ぶ面は Scroll の行、詳細は固定の面
            if (tabScrolls != null && TabLive < tabScrolls.Length && tabScrolls[TabLive] != null)
            {
                GameObject scroll = tabScrolls[TabLive].gameObject;
                if (scroll.activeSelf != (_livePage != LivePageDetail)) scroll.SetActive(_livePage != LivePageDetail);
            }
            int n = liveLabels == null ? 0 : liveLabels.Length;
            for (int i = 0; i < n; i++)
            {
                bool rowOn = false;
                bool show = false;
                bool on = false;
                string title = "-";
                string sub = "";
                string buttonWord = L("KD_TreatPouch.WordLiveHost");
                if (_livePage == LivePageList)
                {
                    KD_ShopItem it = LiveProductAt(i);
                    if (it != null)
                    {
                        rowOn = true;
                        title = L(it.displayNameKey);
                        sub = LiveJoin(LiveTagOf(it), LivePriceText(it));
                        if (it.Owned()) { show = true; on = hostOn; }
                        else if (LiveSaleClosed(it)) { show = false; sub = LiveJoin(LiveTagOf(it), L("live.saleClosed")); }
                        else { show = true; on = true; buttonWord = L("KD_TreatPouch.WordLiveMore"); }
                    }
                }
                else if (_livePage == LivePageSongs)
                {
                    int liveId = LiveShowOf(current, i);
                    if (liveId >= 0)
                    {
                        rowOn = true;
                        title = LiveTitleOf(liveId, current);
                        show = true; on = hostOn;
                    }
                }
                GameObject rowGo = liveLabels[i] != null ? liveLabels[i].transform.parent.gameObject : null;
                if (rowGo != null && rowGo.activeSelf != rowOn) rowGo.SetActive(rowOn);
                if (!rowOn) continue;
                TextMeshProUGUI lbl = liveLabels[i];
                if (lbl != null) lbl.text = title;
                if (liveSubLabels != null && i < liveSubLabels.Length && liveSubLabels[i] != null)
                    liveSubLabels[i].text = sub;
                if (liveButtonLabels != null && i < liveButtonLabels.Length && liveButtonLabels[i] != null)
                    liveButtonLabels[i].text = buttonWord;
                GameObject btn = (liveButtons != null && i < liveButtons.Length) ? liveButtons[i] : null;
                if (btn != null && btn.activeSelf != show) btn.SetActive(show);
                if (liveButtonComponents != null && i < liveButtonComponents.Length
                    && liveButtonComponents[i] != null)
                    liveButtonComponents[i].interactable = on;
                if (liveButtonImages != null && i < liveButtonImages.Length && liveButtonImages[i] != null)
                    liveButtonImages[i].color = on ? recallOnColor : recallOffColor;
            }

            RedrawLiveDetail(current, hostOn);

            // ---- 状態の 1 行 (DES-PRG-002 §1.1「待機中は残り秒を表示」) ----
            if (liveStatusLabel != null)
            {
                string st = "";
                if (live == null) st = L("KD_ShopShelf.WordLoading");
                else if (live.DogRecovering) st = L("live.recovering");
                else if (live.CandyPreparing) st = L("live.gathering");
                else if (live.CurrentLiveId >= 0 && live.IsNightWanted)
                {
                    float remain = KD_LiveSystem.NightInSeconds + live.CurrentDuration - live.Elapsed;
                    int sec = remain > 0f ? Mathf.CeilToInt(remain) : 0;
                    // **表示名は ASCII のときだけ出す** (ねるタブと同じ契約 — 焼いてある字は ASCII 全字だけ。
                    // 日本語名をそのまま出すと実機で豆腐になる。`vrc-tmp-font` §3)
                    VRCPlayerApi host = VRCPlayerApi.GetPlayerById(live.HostId);
                    string name = Utilities.IsValid(host) ? AsciiNameOrNull(host.displayName, LiveNameBudget) : null;
                    string who = name != null ? name + L("KD_TreatPouch.WordLiveHostingSuffix") : L("KD_TreatPouch.WordLiveHostingSomeone");
                    st = who + "  " + L("KD_TreatPouch.WordLiveRemain") + " " + sec.ToString() + L("KD_TreatPouch.WordLiveSeconds");
                }
                else if (!live.CanStart)
                    st = L("KD_TreatPouch.WordLiveWait") + " " + Mathf.CeilToInt(live.SecondsUntilAvailable).ToString() + L("KD_TreatPouch.WordLiveSeconds");
                else if (_livePage == LivePageSongs) st = L("live.songs.help");
                else st = live.IsLocalAllowedToHost ? L("KD_TreatPouch.WordLiveReady") : L("live.ownerOnly");
                if (live != null && live.LocalRequestState == KD_LiveSystem.RequestPending)
                    st = L("KD_TreatPouch.WordLivePending");
                else if (_feedbackTab == TabLive && Time.time < _feedbackUntil) st = L(_feedbackText);
                liveStatusLabel.text = st;
            }

            // ---- 確認 (P13: 板の手前のカード。なで・かざりと同じ 1 枚) ----
            KD_ShopItem c = LiveProductAt(_liveConfirmRow);
            if (c != null && (c.Owned() || LiveSaleClosed(c))) { _liveConfirmRow = -1; c = null; }   // 買えた / 別経路で入手した / 開いている間に販売が終わった
            if (c != null) _ShowItemConfirm(ConfirmLive, c, LiveBundleLabel(c));
            else _HideConfirm(ConfirmLive);
        }

        /// <summary>詳細の面 (P8): 参考画像 (準備中) + 雰囲気 + 見どころ + 曲 + 同梱。[もどる] と [買う] / [開催]。</summary>
        private void RedrawLiveDetail(KD_ShopItem it, bool hostOn)
        {
            bool detail = _livePage == LivePageDetail && it != null;
            if (liveDetailRoot != null && liveDetailRoot.activeSelf != detail) liveDetailRoot.SetActive(detail);
            bool back = _livePage != LivePageList;
            if (liveDetailBack != null && liveDetailBack.activeSelf != back) liveDetailBack.SetActive(back);
            if (liveDetailAction != null && liveDetailAction.activeSelf != detail) liveDetailAction.SetActive(detail);
            if (!detail) return;
            bool owned = it.Owned();
            bool set = AccRowOfLiveSet(it) >= 0;
            string fx = LiveFxNames(it);
            string acc = LiveAccNames(it);
            int songs = LiveShowCount(it);
            if (liveDetailTitle != null) liveDetailTitle.text = L(LiveDetailKey(it, "jp"));
            if (liveDetailMeta != null) liveDetailMeta.text = LiveJoin(L(LiveDetailKey(it, "meta")), LivePriceText(it));
            if (liveDetailMood != null) liveDetailMood.text = L(LiveDetailKey(it, "mood"));
            if (liveDetailScene != null) liveDetailScene.text = L(LiveDetailKey(it, "scene"));
            // 参考画像: あれば写真、無ければ色面 + 作品名 + 準備中
            Sprite thumb = liveThumbs != null && _liveProduct >= 0 && _liveProduct < liveThumbs.Length ? liveThumbs[_liveProduct] : null;
            if (liveDetailPosterImage != null)
            {
                liveDetailPosterImage.sprite = thumb;
                liveDetailPosterImage.color = thumb != null ? Color.white : new Color(0.30f, 0.26f, 0.20f, 1f);
            }
            if (liveDetailPosterLabel != null)
            {
                liveDetailPosterLabel.gameObject.SetActive(thumb == null);
                if (thumb == null) liveDetailPosterLabel.text = L(LiveDetailKey(it, "jp")) + "\n" + L("live.detail.poster");
            }
            if (liveDetailSongsRow != null) liveDetailSongsRow.SetActive(songs > 1);
            if (liveDetailSongs != null && songs > 1) liveDetailSongs.text = L("live.detail.songs").Replace("{0}", songs.ToString()) + ": " + LiveSongNames(it);
            if (liveDetailFxRow != null) liveDetailFxRow.SetActive(fx.Length > 0);
            if (liveDetailFx != null && fx.Length > 0) liveDetailFx.text = L("live.detail.fx") + ": " + fx + "\n" + L("live.detail.fxNote");
            if (liveDetailAccRow != null) liveDetailAccRow.SetActive(acc.Length > 0);
            if (liveDetailAcc != null && acc.Length > 0) liveDetailAcc.text = L("live.detail.acc") + ": " + acc + "\n" + L("live.detail.accNote");
            if (liveDetailNote != null)
            {
                bool closed = LiveSaleClosed(it);
                string note = it.productKey == HalloweenProductKey ? L(closed && !owned ? "live.detail.limitedClosed" : "live.detail.limited")
                    : owned ? "" : it.IsCE() ? L("live.detail.buyNoteCe") : L("live.detail.buyNotePt");
                // 開催すると全員が見られる (particle-live-vessel §1 の商品価値) は所持の有無に関わらず必ず出す (2026-09-17 ユーザー指摘: どこにも書いていなかった)
                string shared = L("live.detail.shared");
                liveDetailNote.text = note.Length == 0 ? shared : shared + "\n" + note;
                if (liveDetailAction != null && closed && !owned) liveDetailAction.SetActive(false);
            }
            if (liveDetailActionLabel != null) liveDetailActionLabel.text = owned ? L("KD_TreatPouch.WordLiveHost") : L("KD_TreatPouch.WordBuyHere");
            bool on = owned ? hostOn : (it.IsCE() ? it.CePurchasesLoaded() : it.CanBuy());
            if (liveDetailActionButton != null) liveDetailActionButton.interactable = on;
            if (liveDetailActionImage != null) liveDetailActionImage.color = on ? recallOnColor : recallOffColor;
        }

        /// <summary>確認カードの中身の行: Pack は「ライブ 3 + 撫でFX 3」、専用セットは「ライブ + 飾り 3 + 撫でFX 1」+ 飾りの名前。</summary>
        private string LiveBundleLabel(KD_ShopItem item)
        {
            if (item == null) return "";
            string tag = LiveTagOf(item);
            string acc = LiveAccNames(item);
            if (acc.Length > 0) return tag + " (" + acc + ")";
            if (LiveShowCount(item) > 1) return tag + " (" + LiveSongNames(item) + ")";
            return tag;
        }

        /// <summary>表示名の字数予算 (ライブの状態行。700px に「さんが開催中  残り 180秒」が同居する)。</summary>
        const int LiveNameBudget = 12;

        private string ConfirmSummary(KD_ShopItem item)
        {
            int state = item.ConfirmationState(false);
            string name = L(item.displayNameKey);
            if (state == KD_ShopItem.ConfirmLoading) return name + "  " + L("KD_ShopShelf.WordLoading");
            if (state == KD_ShopItem.ConfirmSpecial) return name + "  " + (item.ceCredits > 0 ? item.ceCredits.ToString() + " C" : L("KD_ShopShelf.WordLocked"));
            if (state == KD_ShopItem.ConfirmFull) return name + "  " + L("KD_ShopShelf.WordEnough");
            if (state == KD_ShopItem.ConfirmShort) return name + "  " + L("KD_TreatPouch.WordAccWaitPrefix") + " " + item.Shortfall().ToString() + " pt";
            return name + "  " + item.price.ToString() + " pt";
        }

        private KD_ShopItem FxAt(int i)
        {
            if (fxItems == null || i < 0 || i >= fxItems.Length) return null;
            return fxItems[i];
        }

        // ============================================================
        // 表示 — **理由コードから作る。条件式を書き起こさない**
        // ============================================================

    }
}
