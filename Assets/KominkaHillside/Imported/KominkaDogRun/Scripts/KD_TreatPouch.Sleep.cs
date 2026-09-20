using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (Sleep)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_TreatPouch
    {
        // ============================================================
        // ねるタブ (IMP-DOG-019 Phase 5) — **呼ぶ / 起こす / 開放**
        //
        // 3 つとも**押す先は台帳と枠**で、この板は判定を 1 つも持たない:
        //
        //   [一緒に寝る]     slot._TryCall()             権限と占有は枠が見る
        //   [起こす]         slot._Release()             本人 / 権限者の判定も枠が見る
        //   [みんなにも開放] board._SetOpenToAll(bool)   権限の判定は台帳が見る
        //
        // **この板が持つのは「何を出すか」だけ。** 押せる条件を書き写すと、
        // オーナー不在のインスタンスでの結論が板と枠で割れる (KD_SleepBoard の注記)。
        // ============================================================

        /// <summary>空いていて呼べる → [一緒に寝る] が点灯。</summary>
        public const int SleepFree = 0;
        /// <summary>自分が寝かせている → [起こす]。</summary>
        public const int SleepMine = 1;
        /// <summary>他人が寝かせている → 名前を出す。権限者だけ [起こす] (取り上げ)。</summary>
        public const int SleepOther = 2;
        /// <summary>空いているが**権限が無く開放もされていない** → 暗いまま。</summary>
        public const int SleepNoPermission = 3;
        /// <summary>台帳が配線されていない。**無言にしない。**</summary>
        public const int SleepUnwired = 4;
        public const int SleepReserved = 5;

        /// <summary>おもちゃタブの理由ラベル (180px @18pt) に収まる表示名の字数。
        /// **`さんと寝ている` (7 字 = 126px) を引いた残り**から決めてある。</summary>
        const int ToyNameBudget = 5;

        /// <summary>ねるタブの状態ラベル (330px @19pt) に収まる表示名の字数。
        /// 行の 2 行目を丸ごと使えるので、こちらは切り詰めがほぼ起きない。</summary>
        const int SleepNameBudget = 16;

        public void _Sleep0() { Sleep(0); }
        public void _Sleep1() { Sleep(1); }
        public void _Sleep2() { Sleep(2); }
        public void _Sleep3() { Sleep(3); }
        // IMP-DOG-020: 6 匹化 (柴 / サモエド)
        public void _Sleep4() { Sleep(4); }
        public void _Sleep5() { Sleep(5); }

        /// <summary>**行の状態。表示も実行もここから作る** (おやつ / おもちゃと同じ規律 —
        /// 表示と実行で条件を書き分けない)。</summary>
        private int SleepGate(int row)
        {
            if (sleepBoard == null) return SleepUnwired;
            KD_SleepSlot slot = sleepBoard.SlotOf(row);
            if (slot == null) return SleepUnwired;
            // **自分の占有を先に見る。** 開放が切られた後でも自分の犬は起こせる
            // (開放を切るのは「これから呼べる人」の制限であって、
            // 既に成立している占有を畳む操作ではない — KD_SleepBoard._SetOpenToAll)
            if (slot.IsSleeperLocal) return SleepMine;
            if (slot.LiveReservationId >= 0) return SleepReserved;
            if (sleepBoard.IsSleeping(row)) return SleepOther;
            if (!sleepBoard.IsLocalAllowedToCall) return SleepNoPermission;
            return SleepFree;
        }

        /// <summary>寝る・起こす操作の結果を、開いたままの行へ反映する。</summary>
        private void Sleep(int row)
        {
            int gate = SleepGate(row);
            if (gate == SleepUnwired) { Feedback("ui.extra.043", false); Redraw(); return; }

            KD_SleepSlot slot = sleepBoard.SlotOf(row);
            if (slot == null) { Feedback("ui.extra.043", false); Redraw(); return; }

            if (callPose != null && (gate == SleepFree || gate == SleepMine)) { callPose._Open(row); return; }

            if (gate == SleepMine) { slot._Release(); Redraw(); return; }

            if (gate == SleepOther)
            {
                // **取り上げは権限者だけ。** 判定は台帳が持っているので写さない
                if (!sleepBoard.IsLocalController) { Feedback("ui.extra.043", false); Redraw(); return; }
                slot._Release();
                Redraw();
                return;
            }

            if (gate != SleepFree) { Feedback("ui.extra.043", false); Redraw(); return; }

            if (_pendingSleepRow >= 0) return;
            bool accepted = slot._TryCall();
            Feedback(accepted ? "ui.extra.039" : "ui.extra.043", accepted);
            if (accepted) { _pendingSleepRow = row; _sleepRequestAt = Time.time; }
            Redraw();
        }

        /// <summary>[みんなにも開放]。**権限者だけが押せる** (台帳が弾く)。
        /// ローカル専用なので `_` 始まり。</summary>
        public void _OpenToAll()
        {
            if (sleepBoard == null) return;
            sleepBoard._SetOpenToAll(!sleepBoard.IsOpenToAll);
            Redraw();
        }

        /// <summary>ねるタブ。**押せない行は隠さず暗くする** (おもちゃタブと同じ) —
        /// 4 行あること自体が「どの犬が空いているか」の一覧になっている。</summary>
        private void RedrawSleep()
        {
            if (callPose != null && callPose.PageOpen) return;
            int n = sleepLabels == null ? 0 : sleepLabels.Length;
            for (int i = 0; i < n; i++)
            {
                int gate = SleepGate(i);
                // **押せるのは「呼べる」「自分の解除」「権限者の取り上げ」の 3 つだけ**
                bool on = gate == SleepFree || gate == SleepMine
                    || (gate == SleepOther && sleepBoard != null && sleepBoard.IsLocalController);
                bool wake = gate == SleepMine
                    || (gate == SleepOther && sleepBoard != null && sleepBoard.IsLocalController);

                TextMeshProUGUI lbl = sleepLabels[i];
                if (lbl != null) lbl.text = ToyNameAt(i);   // 犬の名前は板の中で 1 か所から

                if (sleepStatusLabels != null && i < sleepStatusLabels.Length
                    && sleepStatusLabels[i] != null)
                    sleepStatusLabels[i].text = SleepStatusText(i, gate);

                // **ボタンは隠さない。** 押せないことは interactable と色で表す
                GameObject go = (sleepButtons != null && i < sleepButtons.Length)
                    ? sleepButtons[i] : null;
                if (go != null && !go.activeSelf) go.SetActive(true);

                if (sleepButtonComponents != null && i < sleepButtonComponents.Length
                    && sleepButtonComponents[i] != null)
                    sleepButtonComponents[i].interactable = on;

                // **点灯 / 消灯はおもちゃ行と同じ 2 色。** 同じ板の中で
                // 「押せる / 押せない」に 2 つの色体系を作らない
                if (sleepButtonImages != null && i < sleepButtonImages.Length
                    && sleepButtonImages[i] != null)
                    sleepButtonImages[i].color = on ? recallOnColor : recallOffColor;

                if (sleepButtonLabels != null && i < sleepButtonLabels.Length
                    && sleepButtonLabels[i] != null)
                    sleepButtonLabels[i].text = callPose != null && gate != SleepOther ? L("call.choose")
                        : wake ? L("KD_TreatPouch.WordSleepWake") : L("KD_TreatPouch.WordSleepCall");
            }

            // [呼ぶを全員に許可] は P5 (2026-09-16) で設定ホームへ移した → RedrawCallAccess

            // ---- 押せない理由は板の中に 1 行 ----
            //
            // **4 行が暗いだけだと「壊れている」と読める。** 押せない理由を書く
            if (sleepNoteLabel != null)
            {
                string note = "";
                if (sleepBoard == null) note = L("KD_ShopShelf.WordLoading") + "...";
                else if (!sleepBoard.IsLocalAllowedToCall) note = L("KD_TreatPouch.WordSleepOwnerOnly");
                sleepNoteLabel.text = _feedbackTab == TabSleep && Time.time < _feedbackUntil ? L(_feedbackText) : note;
            }
        }

        /// <summary>P5: 設定ホームの [呼ぶを全員に許可]。ライブ開催許可と同じ見せ方 —
        /// **隠さず、権限が無い人には押せない**。設定ホームの補足 1 行が「オーナーだけが変更できます」を持つ。</summary>
        private void RedrawCallAccess()
        {
            if (openToAllButton == null) return;
            bool controller = sleepBoard != null && sleepBoard.IsLocalController;
            bool open = sleepBoard != null && sleepBoard.IsOpenToAll;
            if (!openToAllButton.activeSelf) openToAllButton.SetActive(true);
            if (openToAllButtonComponent != null) openToAllButtonComponent.interactable = controller;
            if (openToAllLabel != null)
                openToAllLabel.text = sleepBoard == null ? L("KD_ShopShelf.WordLoading") + "..." : open ? L("KD_TreatPouch.WordOpenOn") : L("KD_TreatPouch.WordOpenOff");
            if (openToAllImage != null) openToAllImage.color = open ? openOnColor : openOffColor;
        }

        private string SleepStatusText(int row, int gate)
        {
            if (gate == SleepUnwired) return L("KD_ShopShelf.WordLoading") + "...";
            if (gate == SleepMine) return L("KD_TreatPouch.WordSleepingSelf");
            if (gate == SleepOther) return SleeperText(row, SleepNameBudget);
            if (gate == SleepReserved) return L("call.selectionBusy");
            return "";   // 空いている行は状態を出さない (押せることはボタンが語る)
        }

        /// <summary>**`〇〇さんと寝ている`。** 名前を出せないときは
        /// <see cref="WordSleepingSomeone"/> へ落とす。
        ///
        /// ## なぜ名前を出せないことがあるのか — **フォントは Static**
        ///
        /// この板の字は <c>KD_TreatSetupEditor</c> が const 群から焼き込んでいて、
        /// **焼いていない字は実機で豆腐になる** (`vrc-tmp-font` §3)。
        /// 表示名は任意の文字列なので、**焼ける集合をあらかじめ決められるのは
        /// ASCII 印字可能 (0x20〜0x7E) だけ** — そこは棚の Setup が全字焼いており、
        /// この板の Verify も全字を数える。
        ///
        /// だから **1 字でも ASCII の外に出たら名前ごと出さない。**
        /// 半分だけ豆腐の名前は「誰なのか」を伝えないうえ、板が壊れて見える。
        /// **`だれかと寝ている` でも「その犬は今取れない」は伝わる** (不変条件 4 の要求は
        /// そこまでで、名前は分かるならなお良い、という順序)。
        ///
        /// > `TMP_FontAsset.HasCharacter` を実行時に引く手も検討したが採らなかった —
        /// > **`TMP_Text.font` が Udon 未公開**でフォント資産を板から辿る経路が細く、
        /// > 得られるのは「アトラスに在る非 ASCII の日本語名も出せる」だけ。
        /// > その日本語名がアトラスに在るのは**別の文言のために焼いた字と偶然重なったとき**
        /// > で、名前として読める保証は無い。</summary>
        private string SleeperText(int row, int maxName)
        {
            if (sleepBoard == null) return L("KD_TreatPouch.WordSleepingSomeone");
            int id = sleepBoard.LiveSleeperOf(row);
            if (id < 0) return L("KD_TreatPouch.WordSleepingSomeone");
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(id);
            if (!Utilities.IsValid(p)) return L("KD_TreatPouch.WordSleepingSomeone");
            string name = AsciiNameOrNull(p.displayName, maxName);
            if (name == null) return L("KD_TreatPouch.WordSleepingSomeone");
            return name + L("KD_TreatPouch.WordSleepingSuffix");
        }

        /// <summary>表示名を**そのまま出せる形**にして返す。出せないなら null。
        ///
        /// - **ASCII 印字可能 (0x20〜0x7E) だけ**を通す (上の理由)
        /// - 長い名前は <paramref name="maxName"/> 字で切って `…` を付ける。
        ///   **切らないと隣の列へ描き出す** (`Overflow` なので黙って重なる)
        ///
        /// 文字列の添字 (`s[i]`) は Udon 未公開なので `ToCharArray` を通す
        /// (2026-09-04 ノード定義レジストリ + 対照群で確認:
        /// `SystemString.__ToCharArray__SystemCharArray` と
        /// `SystemString.__Substring__SystemInt32_SystemInt32__SystemString` は解決し、
        /// 対照群の `TMProTMP_Text.__set_font__...` は解決しない)。</summary>
        private string AsciiNameOrNull(string raw, int maxName)
        {
            if (raw == null || raw.Length == 0) return null;
            char[] cs = raw.ToCharArray();
            for (int i = 0; i < cs.Length; i++)
            {
                char c = cs[i];
                if (c < ' ' || c > '~') return null;
            }
            if (maxName <= 0 || raw.Length <= maxName) return raw;
            return raw.Substring(0, maxName) + NameEllipsis;
        }

    }
}
