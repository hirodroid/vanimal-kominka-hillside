using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>手元メニュー 7 枚目のタブ「あそぶ」(IMP-INF-006 P2、2026-09-13〜16 ユーザー決定)。
    ///
    /// 裏山改変: **集合のみ** (KominkaHillside 段階 0-b、計画書 D-5)。 古民家の犬目線カメラ (KD_DogPovCamera)・
    /// みんなで遊ぶセット (かけっこ / 2 匹の遊び)・KD_ShopItem の所持判定は外した。トレーナー判定は KH_TrainerPass
    /// (`_Configured() && _OwnedBy(LocalPlayer)`)、未加入は案内 + [購入画面をひらく] → `hillsidePass._OpenStore()`。
    ///
    /// 面: 0 = 一覧 (群「ばにまるトレーナー」: 集合モード [使う] / 開催をおわる [おわる] / フリスビーの取り方 [切替])、1 = 隊形 6 行 + [ここに集合]。
    /// 裏山改変: フリスビーの取り方 (row2、DES-UI-002、2026-09-24 ユーザー決定) は frisbeeCatchers の全犬へ次のプリセットを
    /// _RequestVariant で頼む (旧 KH_HandMenu ヘッダの [試作] の移設。誰でも → トレーナーだけ)。
    /// 要求の結果は KD_GroupActivities.LocalRequestState (1 待ち / 2 受理 / 3 拒否 / 4 時間切れ) を読み、
    /// KH_HandMenu (退役) の GatherStart / GatherEnd / PollPending と同じ文言で状態行に出す。
    /// 隊形の印 (placementMarkers) は坂を考えて足元の地面へ落とし、地面が無い枠は出さない (KH_HandMenu と同じ)。
    /// 同期変数 0 本 (SyncMode None)。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_ActivitiesMenu : UdonSharpBehaviour
    {
        public KD_TreatPouch pouch;
        public KD_GroupActivities activities;
        // 裏山改変: トレーナーの権利は KH_TrainerPass (KD_ShopItem の所持判定の置き換え。復帰条件なし)
        [Tooltip("トレーナーの権利 (KH_Dogs/KH_TrainerPass)")]
        public KominkaHillside.KH_TrainerPass hillsidePass;
        public KD_Language language;
        // 裏山改変: 古民家の商品の口。裏山では使わない (null) が KD_ProductsEditor.HasMenuEntry が読むので宣言だけ残す
        [Tooltip("裏山では使わない (null のまま)。古民家の商品 (集合 / みんなで遊ぶ / 犬目線) の口。KD_ProductsEditor.HasMenuEntry が読むので宣言だけ残す")]
        public KD_ShopItem[] items;
        [Tooltip("タブの面 (KD_TreatPouch.tabPages[TabPlay])")]
        public GameObject page;
        public TMP_Text heading, status, actionLabel, backLabel;
        [Tooltip("群「ばにまるトレーナー」の見出しと、トレーナーの案内 (未加入) / 加入中の説明")]
        public TMP_Text groupTrainerLabel, introLabel;
        // 裏山改変: フリスビーの取り方の切替 (DES-UI-002、2026-09-24 ユーザー決定「トレーナー用の遊びの所へ」)。
        // 旧 KH_HandMenu ヘッダの [試作] (誰でも押せた) の後継。犬ごと、ディスクが無い犬は null (KD_GroupActivities.frisbeeCatchers の写し)
        [Tooltip("犬ごとのフリスビー (KD_GroupActivities.frisbeeCatchers の写し、6 要素、ディスクが無い犬は null)。全部 null なら行を隠す")]
        public KominkaHillside.KH_FrisbeeCatch[] frisbeeCatchers;
        [Tooltip("一覧: 0 = 集合モード [使う] / 1 = 開催をおわる [おわる] / 2 = フリスビーの取り方 [切替] (3..5 は隠す)。隊形: 0..5 = 隊形")]
        public Button[] rows;
        public TMP_Text[] rowLabels;
        public Button action;
        public GameObject back;
        public GameObject[] placementMarkers;
        [Tooltip("隊形の印を落とす地面 (Default + Environment)。KD_GroupActivities.groundMask と同じ")]
        public LayerMask groundMask = (1 << 0) | (1 << 11);

        // _page: 0 一覧 / 1 隊形
        private int _page, _shape;
        private float _next, _nextMarkers;
        private bool _wasVisible, _markersShown;
        // 要求 (KH_HandMenu の Pending の型): 4 集合を始める / 5 集合を終える
        private int _pendingKind;
        private float _pendingAt;
        private string _noteKey = "";
        private float _noteUntil;
        private string[] _shapeKeys = { "activities.shape.0", "activities.shape.1", "activities.shape.2", "activities.shape.3", "activities.shape.4", "activities.shape.5" };
        private const float RowPitchX = 355f;
        // 裏山改変: フリスビー行 (DES-UI-002)。一覧の行の添字と、連打で 2 つ進む楽観表示 (旧 KH_HandMenu.DevCurrent と同じ 1.5 秒)
        public const int RowFrisbee = 2;
        private int _frisbeeAsked = -1;
        private float _frisbeeAskedUntil;
        private string[] _frisbeeVariantKeys = { "activities.hillside.frisbeeVariant.0", "activities.hillside.frisbeeVariant.1", "activities.hillside.frisbeeVariant.2", "activities.hillside.frisbeeVariant.3", "activities.hillside.frisbeeVariant.4", "activities.hillside.frisbeeVariant.5" };

        /// <summary>タブが開いたとき (KD_TreatPouch.SetTab から)。一覧へ戻す。</summary>
        public void _Open() { _page = 0; Draw(); }
        public void _Back()
        {
            if (_page == 0) return;
            _page = 0; Draw();
        }
        /// <summary>確認カードの [やめる] (KD_TreatPouch._ConfirmNo から)。裏山は確認カードを使わないので閉じるだけ。ローカル専用なので `_` 始まり。</summary>
        public void _CancelConfirm() { if (pouch != null) pouch._HideConfirm(KD_TreatPouch.ConfirmActivities); }
        public void _Row0() { Press(0); }
        public void _Row1() { Press(1); }
        public void _Row2() { Press(2); }
        public void _Row3() { Press(3); }
        public void _Row4() { Press(4); }
        public void _Row5() { Press(5); }
        private string L(string key) { return language != null ? language._Text(key) : key; }
        private bool Configured() { return hillsidePass != null && hillsidePass._Configured(); }
        private bool Loading() { return Configured() && !hillsidePass._Loaded(); }
        private bool Trainer() { return Configured() && hillsidePass._OwnedBy(Networking.LocalPlayer); }
        private bool Hosting()
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            return activities != null && activities.Mode != 0 && Utilities.IsValid(me) && activities.HostId == me.playerId;
        }
        private int GatherCount()
        {
            if (activities == null) return 0;
            int n = 0;
            for (int i = 0; i < 6; i++) if (activities.Available(i)) n++;
            return n;
        }
        private void Note(string key) { _noteKey = key; _noteUntil = Time.time + 4f; }

        private void Press(int i)
        {
            if (page == null || !page.activeInHierarchy || activities == null) return;
            if (_page == 0)
            {
                if (i == 0) { if (Trainer()) _page = 1; else Note(Loading() ? "activities.hillside.loadingPass" : "activities.hillside.needTrainer"); }
                else if (i == 1) GatherEnd();
                // 裏山改変: フリスビーの取り方 (DES-UI-002)。権限は集合と同じ (トレーナーだけ)
                else if (i == RowFrisbee) { if (Trainer()) FrisbeeSwitch(); else Note(Loading() ? "activities.hillside.loadingPass" : "activities.needTrainer"); }
            }
            else if (_page == 1 && i >= 0 && i < 6) { _shape = i; activities.localFormation = i; }
            Draw();
        }

        /// <summary>下段の 1 ボタン: 一覧 = [購入画面をひらく] (未加入のときだけ出る) / 隊形 = [ここに集合]。</summary>
        public void _Action()
        {
            if (page == null || !page.activeInHierarchy) return;
            if (_page == 0) { if (!Trainer() && Configured()) hillsidePass._OpenStore(); }
            else if (_page == 1) GatherStart();
            Draw();
        }

        private void GatherStart()
        {
            if (activities == null) return;
            if (!Trainer()) { Note(Loading() ? "activities.hillside.loadingPass" : "activities.hillside.needTrainer"); return; }
            if (activities.Mode != 0) { Note("activities.hillside.hosting"); return; }
            if (GatherCount() == 0) { Note("activities.hillside.noDogs"); return; }
            if (RequestBusy()) return;   // 自分の要求の返事待ち: 送らない (受け付けた要求の結果だけを表示する)
            activities._Request(1, _shape, 0);
            Pending(4);
            Note("activities.hillside.requesting");
        }

        private void GatherEnd()
        {
            if (activities == null || activities.Mode == 0) return;
            if (!Hosting()) { Note("activities.hillside.notHost"); return; }
            if (RequestBusy()) return;   // 開始の返事待ちに終了を重ねない (KD_GroupActivities._Request は待機中は送らない)
            activities._Request(0, 0, 0);
            Pending(5);
            Note("activities.hillside.ending");
        }

        // ---- 裏山改変: フリスビーの取り方の切替 (DES-UI-002。旧 KH_HandMenu._DevVariant / DevCurrent の移設) ----
        // プリセットは KH_FrisbeeCatch が同期して持つ (Owner が書く)。この板は _RequestVariant で頼むだけで、同期変数は足さない。

        /// <summary>最初のディスクを持つ犬 (表示と「次」の起点)。0 匹なら null で行を隠す。</summary>
        private KominkaHillside.KH_FrisbeeCatch FirstCatcher()
        {
            if (frisbeeCatchers == null) return null;
            for (int i = 0; i < frisbeeCatchers.Length; i++) if (frisbeeCatchers[i] != null) return frisbeeCatchers[i];
            return null;
        }

        /// <summary>次の押下の起点。Owner の返事 (同期値か予約) が届くか 1.5 秒経つまでは頼んだ番号 (連打で 2 つ進む。Codex U)。表示は同期値のまま。</summary>
        private int FrisbeeCurrent(KominkaHillside.KH_FrisbeeCatch c)
        {
            int v = c.PendingVariant >= 0 ? c.PendingVariant : c.Variant;
            if (_frisbeeAsked < 0) return v;
            if (Time.time >= _frisbeeAskedUntil || c.Variant == _frisbeeAsked || c.PendingVariant == _frisbeeAsked) { _frisbeeAsked = -1; return v; }
            return _frisbeeAsked;
        }

        private void FrisbeeSwitch()
        {
            KominkaHillside.KH_FrisbeeCatch first = FirstCatcher();
            if (first == null) return;
            int next = KominkaHillside.KH_FrisbeeCatch.NextVariant(FrisbeeCurrent(first));   // 引退した v1 / v2 を飛ばす (2026-09-26)            _frisbeeAsked = next; _frisbeeAskedUntil = Time.time + 1.5f;
            bool engaged = false;
            for (int i = 0; i < frisbeeCatchers.Length; i++)
            {
                KominkaHillside.KH_FrisbeeCatch c = frisbeeCatchers[i];
                if (c == null) continue;
                c._RequestVariant(next);
                // PendingVariant は Owner の返事で非 Owner にはまだ届いていないことがあるので、通知はローカルの犬の状態で決める
                if (c.IsEngaged) engaged = true;
            }
            Note(engaged ? "activities.hillside.frisbeeAfterReturn" : "activities.hillside.frisbeeRequested");
        }

        private string FrisbeeVariantName(int v) { return v >= 0 && v < _frisbeeVariantKeys.Length ? L(_frisbeeVariantKeys[v]) : v.ToString(); }

        /// <summary>副文: 「いま: 走り込み: 横」、予約中は「帰ってから: ひねり」(同期値)。</summary>
        private string FrisbeeState(KominkaHillside.KH_FrisbeeCatch c)
        {
            if (c.PendingVariant >= 0) return L("activities.hillside.frisbeePending").Replace("{0}", FrisbeeVariantName(c.PendingVariant));
            return L("activities.hillside.frisbeeNow").Replace("{0}", FrisbeeVariantName(c.Variant));
        }

        private void Pending(int kind) { _pendingKind = kind; _pendingAt = Time.time; }

        /// <summary>自分の要求が返事待ちか (この板の未決、または権限者側の LocalRequestState == 1)。待機中は新しい要求を送らない。</summary>
        private bool RequestBusy() { return _pendingKind != 0 || (activities != null && activities.LocalRequestState == 1); }

        /// <summary>未決の要求を権限者の返事から解決する。4 秒で返事が無ければ「確認できませんでした」。板を閉じていても進む。</summary>
        private void PollPending()
        {
            if (_pendingKind == 0 || activities == null) return;
            bool done = false;
            string key = "";
            int state = activities.LocalRequestState;   // 1 waiting, 2 accepted, 3 rejected, 4 timed out
            if (state == 2) { done = true; key = _pendingKind == 4 ? "activities.hillside.started" : "activities.hillside.ended"; }
            else if (state == 3) { done = true; key = _pendingKind == 4 ? "activities.hillside.rejectStart" : "activities.hillside.rejectEnd"; }
            else if (state == 4) { done = true; key = "activities.hillside.noReply"; }
            if (!done && Time.time - _pendingAt > 4f) { done = true; key = "activities.hillside.unknown"; }
            if (!done) return;
            _pendingKind = 0;
            Note(key);
            Draw();
        }

        private void Update()
        {
            bool visible = page != null && page.activeInHierarchy;
            if (_wasVisible && !visible && pouch != null) pouch._HideConfirm(KD_TreatPouch.ConfirmActivities);
            _wasVisible = visible;
            PollPending();
            UpdateMarkers(visible);
            if (Time.time < _next) return; _next = Time.time + .25f;
            if (visible) Draw();
        }

        /// <summary>隊形の印: 10 Hz、加わる犬の枠だけ、地面がある所だけ (印が出ない枠 = そこには並べない)。KH_HandMenu と同じ。</summary>
        private void UpdateMarkers(bool visible)
        {
            if (placementMarkers == null) return;
            bool preview = visible && _page == 1 && activities != null && activities.Mode == 0 && Trainer();
            if (!preview)
            {
                if (!_markersShown) return;
                _markersShown = false;
                for (int i = 0; i < placementMarkers.Length; i++)
                    if (placementMarkers[i] != null && placementMarkers[i].activeSelf) placementMarkers[i].SetActive(false);
                return;
            }
            if (Time.time < _nextMarkers) return;
            _nextMarkers = Time.time + .1f;
            _markersShown = true;
            VRCPlayerApi lp = Networking.LocalPlayer;
            for (int i = 0; i < placementMarkers.Length; i++)
            {
                GameObject marker = placementMarkers[i];
                if (marker == null) continue;
                bool show = Utilities.IsValid(lp) && activities.Available(i);
                Vector3 p = Vector3.zero;
                RaycastHit hit;
                if (show)
                {
                    p = lp.GetPosition() + lp.GetRotation() * activities.Offset(_shape, i);
                    if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out hit, 4f, groundMask, QueryTriggerInteraction.Ignore)) p.y = hit.point.y; else show = false;
                }
                if (marker.activeSelf != show) marker.SetActive(show);
                if (!show) continue;
                marker.transform.position = p + Vector3.up * .02f;
                float d = activities.MarkerDiameter(i);
                marker.transform.localScale = new Vector3(d, .005f, d);
            }
        }

        private void Draw()
        {
            if (page == null || !page.activeInHierarchy || activities == null) return;
            bool trainer = Trainer();
            bool configured = Configured();
            bool loading = Loading();
            bool home = _page == 0;
            int mode = activities.Mode;
            bool hosting = Hosting();

            heading.text = L(home ? (trainer ? "activities.helpTrainer" : "activities.hillside.help") : "activities.hillside.formationHelp");
            backLabel.text = L("activities.back");
            if (back != null && back.activeSelf != !home) back.SetActive(!home);
            if (groupTrainerLabel != null)
            {
                if (groupTrainerLabel.gameObject.activeSelf != home) groupTrainerLabel.gameObject.SetActive(home);
                groupTrainerLabel.text = L(trainer ? "activities.group.trainer.on" : "activities.group.trainer.off");
            }
            if (introLabel != null)
            {
                if (introLabel.gameObject.activeSelf != home) introLabel.gameObject.SetActive(home);
                introLabel.text = L(trainer ? "activities.hillside.trainerActive" : "activities.hillside.trainerIntro");
            }

            // 裏山改変: フリスビー行 (DES-UI-002)。ディスクを持つ犬が 0 匹なら隠す
            KominkaHillside.KH_FrisbeeCatch catcher = FirstCatcher();
            for (int i = 0; i < rows.Length; i++)
            {
                bool show = home ? (i == 0 || (i == 1 && trainer) || (i == RowFrisbee && catcher != null)) : i < 6;
                if (rows[i].gameObject.activeSelf != show) rows[i].gameObject.SetActive(show);
                if (!show) continue;
                // 一覧: 群の見出し (Y 120) の下に 1 段 (古民家の上の群の座標)。隊形: 3 列 × 2 段 (古民家の座標)
                // 裏山改変: 未加入は [おわる] (row1) が隠れるので、フリスビー行を中央の枠へ詰める (添字は 2 のまま)
                RectTransform rt = (RectTransform)rows[i].transform;
                int column = home && i == RowFrisbee && !trainer ? 1 : i % 3;
                rt.anchoredPosition = new Vector2((column - 1) * RowPitchX, home ? 60f : (i < 3 ? 68f : -32f));
                string text;
                if (home)
                {
                    bool interactable;
                    string sub;
                    if (i == 0)
                    {
                        text = L("activities.home.0");
                        interactable = trainer;
                        sub = trainer ? L("activities.hillside.perk") : L("activities.needTrainer");
                    }
                    else if (i == RowFrisbee)
                    {
                        // 裏山改変: フリスビーの取り方 (DES-UI-002)。行全体が [切替]。権限と読み込み中の扱いは集合モード (row0) と同じ
                        text = L("activities.hillside.frisbee");
                        interactable = trainer;
                        sub = trainer ? FrisbeeState(catcher) : L("activities.needTrainer");
                    }
                    else
                    {
                        text = L("activities.home.3");
                        interactable = hosting && !RequestBusy();
                        sub = L("activities.endDesc");
                    }
                    rows[i].interactable = interactable;
                    text += "\n<size=17>" + sub + "</size>";
                }
                else
                {
                    rows[i].interactable = mode == 0 && !RequestBusy();
                    text = (_shape == i ? "● " : "") + L(_shapeKeys[i]);
                }
                rowLabels[i].text = text;
            }

            // 状態の 1 行 (KH_HandMenu の DrawGather と同じ優先順)。通知が生きていればそちら
            string line;
            if (mode == 1)
            {
                int left = Mathf.Max(0, 60 - Mathf.FloorToInt(activities.Elapsed));
                line = L(hosting ? "activities.hillside.activeMine" : "activities.hillside.activeOther")
                    .Replace("{0}", activities.ParticipantCount.ToString()).Replace("{1}", left.ToString());
            }
            else if (mode != 0) line = L("activities.hillside.otherActivity");
            else if (!configured) line = L("activities.hillside.notConfigured");
            else if (loading) line = L("activities.hillside.loading");
            else if (!trainer) line = L(home ? "activities.storeNote" : "activities.hillside.notTrainer");
            else line = L("activities.hillside.available").Replace("{0}", GatherCount().ToString());
            if (_noteKey.Length > 0 && Time.time < _noteUntil) line = L(_noteKey);
            status.text = line;

            // 下段の 1 ボタン: 一覧は未加入 (購入情報が届いている) のときだけ [購入画面をひらく]、隊形は [ここに集合]
            bool showAction = home ? (!trainer && configured && !loading) : true;
            if (action.gameObject.activeSelf != showAction) action.gameObject.SetActive(showAction);
            if (home) { actionLabel.text = L("activities.openStore"); action.interactable = true; }
            else
            {
                actionLabel.text = L("activities.gather");
                action.interactable = trainer && mode == 0 && !RequestBusy() && GatherCount() > 0;
            }
        }
    }
}
