using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (Redraw)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_TreatPouch
    {
        private void Redraw()
        {
            if (panelRoot == null || !panelRoot.activeSelf) return;

            // ---- タブの外。**どのタブに居ても読める** ----
            if (sectionTitleLabel != null)
                sectionTitleLabel.text = _welcome ? L("welcome.title") : _settingsOpen ? (_extraSettingsPage != null ? L(_extraSettingsTitle) : _distanceFadeSettingsOpen ? L("KD_DistanceFadeSettings.Title") : _volumeSettingsOpen ? L("KD_TreatPouch.WordVolumeSettings") : L("KD_TreatPouch.WordSettings")) : _tab == TabToy ? L("KD_TreatPouch.WordToyTitle") : _tab == TabWalk ? L("KD_TreatPouch.WordWalkTitle")
                    : _tab == TabSleep ? L("KD_TreatPouch.WordSleepTitle") : _tab == TabLive ? LiveSectionTitle()
                    : _tab == TabAcc ? L("KD_TreatPouch.WordAccTitle") : _tab == TabPlay ? L("activities.title") : L("KD_TreatPouch.WordTitle");
            RedrawEventBand();   // P10: ヘッダとタブの間 (どのタブでも)
            if (feedbackLabel != null)
            {
                // 裏山改変 (IMP-DOG-015): 撫でFX の結果は かざりのカタログ階層で出す (古民家は _tab == TabPetFx。裏山のタブ 2 は さんぽ)。判定は KD_TreatPouch.PetFx.cs
                bool visible = HillsideFxFeedbackVisible();
                feedbackLabel.gameObject.SetActive(visible);
                if (visible) feedbackLabel.text = L(_feedbackText);
            }
            bool loaded = ledger != null && ledger.Loaded();
            if (balanceLabel != null)
            {
                // **復元前に 0 と書かない** — 「消えた」に見える
                balanceLabel.text = !loaded
                    ? L("KD_ShopShelf.WordLoading") + "..."
                    : L("KD_ShopShelf.WordRemain") + " " + ledger.Points().ToString() + " pt <size=60%>▶</size>";   // ▶ = 押すと「ポイント」の面 (IMP-PRG-010)。2026-09-17
            }

            // **[とじる] はタブにも状態にも依らず常に出す。** どの面に居ても板を消す道は要る
            if (closeButton != null && !closeButton.activeSelf) closeButton.SetActive(true);

            // Desktopには手元へ呼び戻すキーを案内する。閉じるのは常設ボタンだけ。
            if (closeHintLabel != null)
            {
                VRCPlayerApi me = Networking.LocalPlayer;
                // 「2D かつ非 Touch」= キーボード・マウスだけでなくコントローラ / Generic も含む。
                // それらでもキーは押せる (desktopToggleKey) ので、案内はこの条件で出す
                bool hint2DNonTouch = Utilities.IsValid(me) && !me.IsUserInVR() && !IsTouch();
                if (closeHintLabel.gameObject.activeSelf != hint2DNonTouch)
                    closeHintLabel.gameObject.SetActive(hint2DNonTouch);
                if (hint2DNonTouch) closeHintLabel.text = desktopToggleKey.ToString() + " " + L("KD_TreatPouch.WordToggleHint");
            }

            if (_welcome) { RedrawWelcome(); return; }
            // 裏山改変: ポイントの面 (段階 1。ギフトコードの器の代わり) は設定の枠の中の面。描画は KD_TreatPouch.Hillside.cs
            if (_settingsOpen) { RedrawVoiceSettings(); RedrawBgmSettings(); RedrawMusicSettings(); RedrawCallAccess(); HillsideRedrawPoints(); return; }

            // ---- 開いているタブだけ ----
            // 裏山改変: 未導入のバックエンドのタブは「準備中」1 行 (計画書 D-1 の段階表)。移動先 KD_TreatPouch.Hillside.cs。各バックエンドの配線で解除
            if (HillsidePreparing()) return;
            // 裏山改変: おもちゃは KH_ToyRecall のボール / ディスク (KD_TreatPouch.Toy.cs の RedrawHillsideToy)。タブ 2 は さんぽ (KD_TreatPouch.Walk.cs)。
            // 撫でFX は かざりへ合流 (RedrawAcc の中の RedrawFxRows。KD_TreatPouch.PetFx.cs / 計画書 D-2)
            if (_tab == TabToy) { RedrawHillsideToy(); return; }
            if (_tab == TabWalk) { RedrawWalk(); return; }
            if (_tab == TabSleep) { RedrawSleep(); return; }
            if (_tab == TabLive) { RedrawLive(); return; }
            if (_tab == TabAcc) { RedrawAcc(); return; }
            if (_tab == TabPlay) return; // P2: KD_ActivitiesMenu.Update が 0.25 秒ごとに描く
            RedrawTreat();
        }

        /// <summary>ようこそ (IMP-INF-020)。本文は固定ラベル (KD_Language の言語切替に乗る)。
        /// 足元の 1 行だけ端末で変わる — 呼び方は**今の端末の 1 つだけ** (3 つ並べない)。
        /// 端末の判定は closeHint と同じ 2 軸 (VR か / 2D なら Touch か)。</summary>
        private void RedrawWelcome()
        {
            if (welcomeFooterLabel == null) return;
            VRCPlayerApi me = Networking.LocalPlayer;
            bool vr = Utilities.IsValid(me) && me.IsUserInVR();
            welcomeFooterLabel.text = L(vr ? "welcome.footer.vr" : IsTouch() ? "welcome.footer.touch" : "welcome.footer.desktop");
        }

        /// <summary>おやつタブ。
        /// 可否は <see cref="KD_TreatBox.DispenseGateWith"/> の理由コードから作る。</summary>
        private void RedrawTreat()
        {

            int n = rowLabels == null ? 0 : rowLabels.Length;
            for (int i = 0; i < n; i++)
            {
                KD_ShopItem it = ItemAt(i);
                int gate = GateFor(it);

                TextMeshProUGUI lbl = rowLabels[i];
                if (lbl != null)
                {
                    string s = it == null ? "-" : L(it.displayNameKey) + " / " + it.Stock().ToString() + L("KD_ShopShelf.WordCount");
                    string hint = "";
                    if (gate == KD_TreatBox.GateNoStock && it != null)
                    {
                        int gap = it.Shortfall();
                        hint = gap > 0 ? L("KD_TreatPouch.WordAccWaitPrefix") + " " + gap.ToString() + " pt" : L("KD_TreatPouch.WordAccNotOwned");
                    }
                    if (rowHintLabels != null && i < rowHintLabels.Length && rowHintLabels[i] != null)
                        rowHintLabels[i].text = hint;
                    lbl.text = s;
                }

                GameObject btn = (rowButtons != null && i < rowButtons.Length) ? rowButtons[i] : null;
                if (btn != null)
                {
                    bool show = gate == KD_TreatBox.GateOk;
                    if (btn.activeSelf != show) btn.SetActive(show);
                }
            }

            if (putAwayButton != null)
            {
                bool show = _state == KD_TreatBox.DispOut;
                if (putAwayButton.activeSelf != show) putAwayButton.SetActive(show);
            }

            if (statusLabel != null) statusLabel.text = StatusText();
            // 裏山改変 (IMP-DOG-014): [買う]・行の補足・確認カード・購入結果の 1 行 (KD_TreatPouch.Treat.cs)
            HillsideRedrawTreat();
        }

        /// <summary>おもちゃタブ。**押せない行は隠さず暗くする。**
        ///
        /// おやつ行が「押せないボタンを隠す」のと逆にしているのは、
        /// **4 行あること自体が情報** (どの犬のボールが今使えるかの一覧) だから。
        /// 隠すと行が消えて並びが動き、押し間違える。</summary>
        private void RedrawToy()
        {
            int n = toyLabels == null ? 0 : toyLabels.Length;
            for (int i = 0; i < n; i++)
            {
                int gate = ToyGate(i);
                bool on = gate == ToyOk;

                TextMeshProUGUI lbl = toyLabels[i];
                if (lbl != null) lbl.text = ToyNameAt(i);

                // **理由は別ラベル。** 名前と同じ行へ押し込むと行幅を溢れる
                if (toyReasonLabels != null && i < toyReasonLabels.Length
                    && toyReasonLabels[i] != null)
                    toyReasonLabels[i].text = _requestToyRow == i && _feedbackTab == TabToy && Time.time < _feedbackUntil ? L(_feedbackText) : ToyReasonText(i, gate);

                // **ボタンは隠さない。** 押せないことは interactable と色で表す
                GameObject go = (toyButtons != null && i < toyButtons.Length) ? toyButtons[i] : null;
                if (go != null && !go.activeSelf) go.SetActive(true);

                if (toyButtonComponents != null && i < toyButtonComponents.Length
                    && toyButtonComponents[i] != null)
                    toyButtonComponents[i].interactable = on;

                if (toyButtonImages != null && i < toyButtonImages.Length
                    && toyButtonImages[i] != null)
                    toyButtonImages[i].color = on ? recallOnColor : recallOffColor;
            }
        }

        // 裏山改変 (IMP-DOG-015): 古民家の RedrawFx (パーティクル一覧) は KD_TreatPouch.PetFx.cs の RedrawFxRows へ移した (かざりのカタログ階層で描く。計画書 D-2)

        private string StatusText()
        {
            if (_state == KD_TreatBox.DispReserving || _state == KD_TreatBox.DispReserved
                || _state == KD_TreatBox.DispCommitting)
                return L("KD_TreatPouch.WordDispensing") + "...";
            if (_state == KD_TreatBox.DispRecalling) return L("KD_TreatPouch.WordRecalling") + "...";
            if (_state == KD_TreatBox.DispOut) return L("KD_TreatPouch.WordHolding");
            if (_reason != KD_TreatBox.GateOk) return ReasonText(_reason);
            return ReasonText(GlobalGate());
        }

        private string ReasonText(int gate)
        {
            if (gate == KD_TreatBox.GateLoading) return L("KD_ShopShelf.WordLoading") + "...";
            if (gate == KD_TreatBox.GateAlreadyOut) return L("KD_TreatPouch.WordAlreadyOut");
            if (gate == KD_TreatBox.GateNoStock) return L("KD_TreatPouch.WordNoStock");
            if (gate == KD_TreatBox.GateSlotsFull) return L("KD_TreatPouch.WordSlotsFull");
            // 裏山改変 (IMP-DOG-014): 全頭が来られない理由を分ける (リード中 / 集合中 / フリスビー中 / 活動の終わり待ち)。KD_TreatPouch.Treat.cs
            if (gate == KD_TreatBox.GateDogsBusy) return HillsideDogsBusyText();
            if (gate == KD_TreatBox.GateOwnerFailed) return L("KD_TreatPouch.WordSlotLost");
            if (gate == KD_TreatBox.GateCommitFailed) return L("KD_TreatPouch.WordCommitFailed");
            return "";
        }

        // ============================================================
        // デバッグ盤・Verify が読む口
        // ============================================================

        /// <summary>出す手順の今の状態 (<c>KD_TreatBox.Disp*</c>)。</summary>
        public int DispenseState() { return _state; } // net: callable-ok: 読み取りのみ

        /// <summary>最後の拒否理由 (<c>KD_TreatBox.Gate*</c>)。</summary>
        public int LastReason() { return _reason; } // net: callable-ok: 読み取りのみ
    }
}
