using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>手元メニュー 7 枚目のタブ「あそぶ」(IMP-INF-006 P2、2026-09-13〜16 ユーザー決定)。
    ///
    /// 中身は 2 群 — **犬目線カメラ (Point、自分だけに見える)** が上、**ばにまるトレーナー (月額、CE Subscription)** が下
    /// (安い → 高い、Credits は最後。P9 と同じ向き)。トレーナー未加入のときは特典の説明 + 特典 2 群の予告 + [購入画面をひらく]、
    /// 加入中は 2 特典を [使う]。購入は VRChat の購入画面で行い、ワールド内では決済しない。
    /// 2026-09-16 までは上段の [あそぶ] ボタンから開く設定の面だった (Treat Setup がそのボタンを掃除して入口が消えていた)。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_ActivitiesMenu : UdonSharpBehaviour
    {
        public KD_TreatPouch pouch;
        public KD_GroupActivities activities;
        public KD_DogPovCamera pov;
        public KD_Language language;
        [Tooltip("0 集合モード (トレーナー) / 1 みんなで遊ぶセット (トレーナー。0 と同じ Listing) / 2 犬目線カメラ (Point)")]
        public KD_ShopItem[] items;
        [Tooltip("タブの面 (KD_TreatPouch.tabPages[TabPlay])")]
        public GameObject page;
        public TMP_Text heading, status, actionLabel, backLabel;
        [Tooltip("P2: 群の見出し 2 本と、トレーナーの案内 (未加入) / 加入中の説明")]
        public TMP_Text groupPovLabel, groupTrainerLabel, introLabel;
        public Button[] rows;
        public TMP_Text[] rowLabels;
        public Button action;
        public GameObject back;
        public string[] dogKeys;
        public GameObject[] placementMarkers;
        // _page: 0 ホーム / 1 隊形 / 2 犬 2 匹 / 3 犬目線 / 5 みんなで遊ぶ (かけっこ・2 匹)
        private int _page, _confirm = -1, _shape, _a = -1, _b = -1;
        private float _next;
        private bool _wasVisible;
        private string _feedback = "";
        // P2: ホームの行 — 0 犬目線カメラ / 1 モニターを呼び戻す / 2 モニターをしまう / 3 集合モード / 4 みんなで遊ぶセット / 5 開催をおわる
        private string[] _homeKeys = { "activities.home.2", "activities.home.4", "activities.home.5", "activities.home.0", "activities.home.1", "activities.home.3" };
        private string[] _homeSubKeys = { "activities.povDesc", "activities.recallDesc", "activities.closeDesc", "activities.perk.formation", "activities.perk.hosting", "activities.endDesc" };
        private string[] _shapeKeys = { "activities.shape.0", "activities.shape.1", "activities.shape.2", "activities.shape.3", "activities.shape.4", "activities.shape.5" };
        private string[] _activeKeys = { "activities.ready", "activities.active.1", "activities.active.2", "activities.active.3" };
        private const float RowPitchX = 355f;

        /// <summary>タブが開いたとき (KD_TreatPouch.SetTab から)。ホームへ戻す。</summary>
        public void _Open() { _page = 0; _confirm = -1; _feedback = ""; Draw(); }
        public void _Back()
        {
            _CancelConfirm(); _feedback = "";
            if (_page == 0) return;
            _page = 0; Draw();
        }
        /// <summary>確認カードの [やめる] (KD_TreatPouch._ConfirmNo から)。ローカル専用なので `_` 始まり。</summary>
        public void _CancelConfirm() { _confirm = -1; pouch._HideConfirm(KD_TreatPouch.ConfirmActivities); Draw(); }
        public void _Row0() { Press(0); }
        public void _Row1() { Press(1); }
        public void _Row2() { Press(2); }
        public void _Row3() { Press(3); }
        public void _Row4() { Press(4); }
        public void _Row5() { Press(5); }
        private string L(string key) { return language._Text(key); }
        private bool Trainer() { return items != null && items.Length > 0 && items[0].Owned(); }
        private void Press(int i)
        {
            if (!page.activeInHierarchy) return;
            _feedback = "";
            if (_page == 0)
            {
                bool trainer = Trainer();
                // 犬目線 (Point): 未所持なら確認カード、所持なら犬を選ぶ面
                if (i == 0) { if (!items[2].Owned()) _confirm = 2; else _page = 3; }
                else if (i == 1) pov._Recall();
                else if (i == 2) pov._Close();
                // トレーナー特典: 未加入は行を押せない (購入は [購入画面をひらく] から)
                else if (i == 3) { if (trainer) _page = 1; }
                else if (i == 4) { if (trainer) _page = 5; }
                else activities._Request(0, 0, 0);
            }
            else if (_page == 1) { _shape = i; activities.localFormation = i; }
            else if (_page == 2) { if (_a < 0 || _b >= 0 || _a == i) { _a = i; _b = -1; } else _b = i; }
            else if (_page == 3)
            {
                if (i < dogKeys.Length) pov._Show(i);
            }
            else if (_page == 5)
            {
                if (i == 0) activities._Request(2, 0, 0);
                else if (i == 1) { _page = 2; _a = -1; _b = -1; }
            }
            Draw();
        }
        public void _Action()
        {
            if (!page.activeInHierarchy) return;
            if (_confirm >= 0)
            {
                int product = _confirm;
                int buyState = items[product].ConfirmationState(false);
                // CE (トレーナー): Storefront を開くだけ。所有は購入イベントで戻る (IMP-FET-010)
                if (buyState == KD_ShopItem.ConfirmSpecial) { _confirm = -1; pouch._HideConfirm(KD_TreatPouch.ConfirmActivities); items[product]._OpenStore(); }
                else if (buyState == KD_ShopItem.ConfirmBuy && items[product]._Buy())
                { _confirm = -1; pouch._HideConfirm(KD_TreatPouch.ConfirmActivities); _feedback = "activities.bought"; }
            }
            // P2: ホームの [購入画面をひらく] (未加入のときだけ出る) → トレーナーの確認カード
            else if (_page == 0) { if (!Trainer()) _confirm = 0; }
            else if (_page == 1)
            {
                if (activities.live.IsNightWanted) activities._Request(5, _shape, 0);
                else activities._Request(1, _shape, 0);
            }
            else if (_page == 2 && _a >= 0 && _b >= 0) activities._Request(3, _a, _b);
            else if (_page == 3) pov._ToggleView();
            Draw();
        }
        private void Update()
        {
            bool visible = page != null && page.activeInHierarchy;
            if (_wasVisible && !visible) { _confirm = -1; pouch._HideConfirm(KD_TreatPouch.ConfirmActivities); }
            _wasVisible = visible;
            bool preview = visible && _page == 1 && items[0].Owned() && !activities.live.IsNightWanted;
            for (int i = 0; i < placementMarkers.Length; i++)
            {
                GameObject marker = placementMarkers[i]; marker.SetActive(preview);
                if (preview && Networking.LocalPlayer != null)
                {
                    VRCPlayerApi lp = Networking.LocalPlayer;
                    marker.transform.position = lp.GetPosition() + lp.GetRotation() * activities.Offset(_shape, i) + Vector3.up * .02f;
                    float d = activities.MarkerDiameter(i); // 印は体幅 (隊形を詰めたので固定 0.65 m では重なって 1 つの塊に見える。2026-09-13)
                    marker.transform.localScale = new Vector3(d, .005f, d);
                }
            }
            if (Time.time < _next) return; _next = Time.time + .25f;
            if (visible) Draw();
        }
        private void Draw()
        {
            if (page == null || !page.activeInHierarchy) return;
            bool trainer = Trainer();
            bool home = _page == 0;
            heading.text = L(_page == 1 ? "activities.formationHelp" : _page == 2 ? "activities.pairHelp" : _page == 3 ? "activities.povHelp" : trainer ? "activities.helpTrainer" : "activities.help");
            backLabel.text = L("activities.back");
            if (back != null && back.activeSelf != !home) back.SetActive(!home);
            // P2: 群の見出しと案内はホームだけ
            if (groupPovLabel != null) { groupPovLabel.gameObject.SetActive(home); groupPovLabel.text = L("activities.group.pov"); }
            if (groupTrainerLabel != null) { groupTrainerLabel.gameObject.SetActive(home); groupTrainerLabel.text = L(trainer ? "activities.group.trainer.on" : "activities.group.trainer.off"); }
            if (introLabel != null) { introLabel.gameObject.SetActive(home); introLabel.text = L(trainer ? "activities.trainerActive" : "activities.trainerIntro"); }
            // P13: 確認カード (一覧はそのまま見せ、暗幕が押下を受ける)。買えた / 別経路で入手したら閉じる
            if (_confirm >= 0 && items[_confirm].Owned()) _confirm = -1;
            if (_confirm == 2) pouch._ShowItemConfirm(KD_TreatPouch.ConfirmActivities, items[2], L("activities.buyHelp"));
            else if (_confirm >= 0)
            {
                // トレーナー (CE Subscription): 商品名ではなく「加入しますか？」。1 か月 500C から
                int st = items[_confirm].ConfirmationState(false);
                pouch._ShowConfirm(KD_TreatPouch.ConfirmActivities, L("activities.trainerConfirmTitle"), L("activities.trainerConfirmBody"),
                    st == KD_ShopItem.ConfirmLoading ? L("activities.loading") : L("confirm.storeNote"),
                    st == KD_ShopItem.ConfirmSpecial, L("confirm.storeButton"));
            }
            else pouch._HideConfirm(KD_TreatPouch.ConfirmActivities);
            for (int i = 0; i < rows.Length; i++)
            {
                bool show = _page != 5 || i < 2;
                rows[i].gameObject.SetActive(show); rows[i].interactable = true;
                // P2: ホームは 2 群 (上 3 行 = 犬目線、下 3 行 = トレーナー) の間に見出しと案内を挟む。ほかの面は詰める
                RectTransform rt = (RectTransform)rows[i].transform;
                rt.anchoredPosition = new Vector2((i % 3 - 1) * RowPitchX, home ? (i < 3 ? 60f : -150f) : (i < 3 ? 68f : -32f));
                if (!show) continue;
                string text;
                if (home)
                {
                    text = L(_homeKeys[i]);
                    string sub = L(_homeSubKeys[i]);
                    if (i == 0) sub = items[2].Owned() ? L("activities.povOwnedDesc") : items[2].price + " pt · " + sub;
                    if (i == 1 || i == 2) rows[i].interactable = pov.IsOpen;
                    if (i == 3 || i == 4) { rows[i].interactable = trainer; if (!trainer) sub = L("activities.needTrainer"); }
                    // 開催者本人または管理者 (受信側 KD_GroupActivities.Handle op 0 と同じ判定元: live → access → KD_SleepBoard)
                    if (i == 5) rows[i].interactable = activities.Mode != 0 && Networking.LocalPlayer != null && (activities.HostId == Networking.LocalPlayer.playerId || activities.live.CanManage(Networking.LocalPlayer));
                    text += "\n<size=17>" + sub + "</size>";
                }
                else if (_page == 1) text = (_shape == i ? "● " : "") + L(_shapeKeys[i]);
                else if (_page == 5) text = L(i == 0 ? "activities.race" : "activities.pair");
                else
                {
                    text = L(dogKeys[i]);
                    if (_page == 2) { if (i == _a || i == _b) text = "● " + text; rows[i].interactable = activities.Available(i); }
                    if (_page == 3 && pov.IsOpen && pov.selectedDog == i) text = "● " + text;
                }
                rowLabels[i].text = text;
            }
            bool sharedBusy = activities.Busy || activities.live.IsNightWanted;
            string state = activities.LocalRequestState == 1 ? "activities.pending" : activities.LocalRequestState == 3 ? "activities.rejected" : activities.LocalRequestState == 4 ? "activities.unknown" : "";
            status.text = _feedback.Length > 0 ? L(_feedback) : state.Length > 0 ? L(state) : activities.Mode != 0 ? L(_activeKeys[activities.Mode]) + " " + Mathf.CeilToInt(60f - activities.Elapsed) + "s"
                : home && !trainer ? L("activities.storeNote") : L("activities.ready");
            action.gameObject.SetActive((_page >= 1 && _page <= 3) || (home && !trainer));
            action.interactable = activities.LocalRequestState != 1;
            if (home) { actionLabel.text = L("activities.openStore"); action.interactable = true; }
            else if (_page == 1)
            {
                actionLabel.text = L(activities.live.IsNightWanted ? "activities.applyLive" : "activities.gather");
                action.interactable &= items[0].Owned() && (!sharedBusy || activities.live.IsNightWanted && Networking.LocalPlayer != null && activities.live.HostId == Networking.LocalPlayer.playerId);
                if (!activities.live.IsNightWanted && Networking.LocalPlayer != null)
                    action.interactable &= activities.ValidPlacement(Networking.LocalPlayer.GetPosition(), Networking.LocalPlayer.GetRotation().eulerAngles.y, _shape, Networking.LocalPlayer);
            }
            else if (_page == 2) { actionLabel.text = L("activities.startPair"); action.interactable &= !sharedBusy && _a >= 0 && _b >= 0 && activities.Available(_a) && activities.Available(_b); }
            else if (_page == 3) { actionLabel.text = L(pov.followView ? "activities.firstPerson" : "activities.follow"); action.interactable = true; }
            if (_page == 5) rows[0].interactable = !sharedBusy && activities.LocalRequestState != 1;
        }
    }
}
