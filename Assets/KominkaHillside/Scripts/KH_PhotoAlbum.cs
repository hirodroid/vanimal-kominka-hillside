using KominkaDogRun;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside
{
    /// <summary>
    /// IMP-WLD-005 (小さい版): 自動写真の「並べ方と見せ方」。docs/plans/2026-10-03-auto-photo-gallery.md §4。
    ///
    /// 撮るのは古民家から移した仕組みのまま (KD_PhotoDirector → KD_PhotoCapture → KD_GalleryController)。IMP-WLD-007 W1 で 24 枠・出来事 3 カテゴリ
    /// (散歩 12 / 撫で 6 / 遊び 6) になった。このスクリプトは、撮れた写真を **新しい順に左上から**、8 枚ずつのページで ボード (門の看板 A の隣) と手元メニューの写真の面に並べ、
    /// 状態の文 (まだ無い / OFF / 読み込み中) と設定の [写真: ON / OFF] を描く。両方とも **同じ RenderTexture** を表示する (メモリは増えない)。
    ///
    /// - すべてローカル (同期変数 0、Ownership なし)。写真は各自の端末の中だけで、退室で消える
    /// - **Update を持たない。** 動くのは 撮れた (KD_PhotoCapture._FinishCapture) / 状態が変わった (KD_PhotoSettings.Refresh。
    ///   言語の切替は KD_PhotoSettings の 4 Hz の見回りが拾う) / メニューの操作 のときだけ
    /// - 撮影を止めている間 (設定 OFF・読み込み前・ボードとボタンを置いていない) は KD_PhotoDirector の UdonBehaviour ごと無効にする
    ///   (毎フレームの見回りを止める。IMP-DOG-034 / 036 の方針)
    /// - BUG-CAM-003: iOS では VRChat カメラを寄せた全画面化が黒くなるので、スマホ (VR でない Android / iOS) では板の吸着を切る
    ///   (古民家の KD_GalleryController.Start と同じ。板が裏山ではこのスクリプトの持ち物になったので、ここへ移した)
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_PhotoAlbum : UdonSharpBehaviour
    {
        // IMP-WLD-007 W1 (docs/plans/2026-10-03-photo-expansion.md §0・§4): 24 枚 (散歩 12 / 撫で 6 / 遊び 6。カテゴリの中で古い順に上書きするのは
        // KD_GalleryController.slotGroups)。見せるのはカテゴリを分けずに新しい順で、8 枚ずつのページ。ボードの Quad とメニューの縮小表示は 8 つのまま
        public const int Slots = 24;
        public const int PageSize = 8;

        [Header("写真の仕組み (Photo/1 が配線)")]
        public KD_GalleryController gallery;
        public KD_PhotoSettings settings;
        public KD_PhotoCapture capture;
        [Tooltip("KD_PhotoDirector の backing UdonBehaviour。撮ってよいときだけ有効")]
        public UdonBehaviour directorUdon;
        public KD_Language language;
        [Tooltip("ボード・メニューのボタン・設定の行を置いたか (Photo/4 が true、Photo/5 が false)。false の間は撮らない (見る場所が無い)")]
        public bool surfacesPlaced;

        [Header("ボード (Photo/4、任意)")]
        [Tooltip("写真の Quad (KD_PhotoPrint)。並びは位置 = 0 が左上 (いちばん新しい)、左→右、上→下")]
        public Renderer[] boardPhotos;
        public KD_PhotoBoard[] boardTaps;
        [Tooltip("boardTaps の backing (Interact の文言を言語に合わせる)")]
        public UdonBehaviour[] boardTapUdons;
        public TMP_Text boardCaption;
        public TMP_Text boardState;
        [Tooltip("KD_PhotoViewer の backing (閉じる Interact の文言)")]
        public UdonBehaviour viewerUdon;

        [Header("手元メニューの写真の面 (Photo/1) とヘッダのボタン (Photo/4)")]
        public GameObject menuPage;
        public RawImage[] menuThumbs;
        public GameObject[] menuThumbButtons;
        [Tooltip("IMP-WLD-009: 縮小表示 8 枚それぞれの手前の Quad (KD_PhotoPrint)。一覧のまま VRChat カメラを近づけた 1 枚が全画面。写真の無い位置と、大きい表示・説明を開いている間は隠す")]
        public Renderer[] menuThumbSnaps;
        public TMP_Text menuState;
        public GameObject menuSettingsButton;
        public TMP_Text menuSettingsLabel;
        public TMP_Text menuBackLabel;
        public TMP_Text menuHelpMarkLabel;
        public GameObject menuHelpRoot;
        public TMP_Text menuHelpTitle;
        public TMP_Text menuHelpBody;
        public TMP_Text menuHelpCloseLabel;
        public GameObject menuLargeRoot;
        public RawImage menuLarge;
        [Tooltip("IMP-WLD-007 W3: 大きく出した写真の手前の Quad (KD_PhotoPrint)。VRChat カメラを近づけると全画面。縮小表示の板は menuThumbSnaps (IMP-WLD-009)")]
        public Renderer menuSnapPhoto;
        public TMP_Text menuLargeCloseLabel;
        public TMP_Text headerLabel;

        [Header("ページ (IMP-WLD-007 W1。◀ ▶ と「1/3」。写真が 9 枚以上のときだけ出す)")]
        [Tooltip("メニューの写真の面の ◀ 1/3 ▶ (Photo/1)")]
        public GameObject menuPager;
        public TMP_Text menuPageLabel;
        [Tooltip("ボードの ◀ 1/3 ▶ (Photo/4。押せる Canvas)")]
        public GameObject boardPager;
        public TMP_Text boardPageLabel;
        [Tooltip("ボードのページを最後に送ってからこの秒数で 1 ページ目へ戻す (次に来た人が最新を見られるように)")]
        public float boardPageResetSeconds = 60f;

        [Header("設定の行 (Photo/4)")]
        public Button settingsToggleButton;
        public Image settingsToggleImage;
        public TMP_Text settingsToggleLabel;
        public Color onColor = new Color(0.30f, 0.42f, 0.52f);
        public Color offColor = new Color(0.20f, 0.18f, 0.16f);

        // 位置 k に出すスロット番号 (k = 0 がいちばん新しい)。_count 枚ぶん有効。ハーネスが読む
        private int[] _order;
        private int _count;
        private int _captures;        // 撮れた回数 (ハーネスが読む)
        private int _largeSlot = -1;
        private int _menuPage;        // 0 = いちばん新しい 8 枚。ハーネスが読む
        private int _boardPage;
        private float _boardPageAt;   // ボードのページを最後に送った時刻 (戻す遅延イベントの照合)

        private string L(string key) { return language != null ? language._Text(key) : key; }

        private void Init()
        {
            if (_order != null) return;
            _order = new int[Slots];
            for (int i = 0; i < Slots; i++) _order[i] = -1;
        }

        private void Start()
        {
            Init();
            // BUG-CAM-003: define で決めるのはフラグだけ (本体は全プラットフォームでコンパイルさせる。KD_GalleryController と同じ理由)
            bool mobile = false;
#if UNITY_ANDROID || UNITY_IOS
            mobile = true;
#endif
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (mobile && Utilities.IsValid(lp) && !lp.IsUserInVR() && boardPhotos != null)
                for (int k = 0; k < boardPhotos.Length; k++)
                    if (boardPhotos[k] != null) boardPhotos[k].material.SetFloat("_AttachDistance", 0f);   // 共有の材質ではなく複製を書く
            // IMP-WLD-007 W3: メニューの大きい写真も同じ規則 (スマホの VR でないときは吸着しない。画面のスクリーンショットで持ち帰る)
            if (mobile && Utilities.IsValid(lp) && !lp.IsUserInVR() && menuSnapPhoto != null) menuSnapPhoto.material.SetFloat("_AttachDistance", 0f);
            // IMP-WLD-009: 縮小表示 8 枚の板も同じ規則
            if (mobile && Utilities.IsValid(lp) && !lp.IsUserInVR() && menuThumbSnaps != null)
                for (int k = 0; k < menuThumbSnaps.Length; k++)
                    if (menuThumbSnaps[k] != null) menuThumbSnaps[k].material.SetFloat("_AttachDistance", 0f);
            if (menuLargeRoot != null) menuLargeRoot.SetActive(false);
            if (menuHelpRoot != null) menuHelpRoot.SetActive(false);
            ApplyGate();
            Rebind();
            DrawLabels();
        }

        // ================================================================ 受け口 (他の Behaviour から)

        /// <summary>KD_PhotoCapture が 1 枚確定させた直後 (色を写した後)。その枠をいちばん前へ。ローカル専用。</summary>
        public void _OnPhotoCaptured()
        {
            Init();
            if (gallery == null || capture == null) return;
            RenderTexture last = capture.LastSlot;
            RenderTexture[] tex = gallery.slotTextures;
            if (last == null || tex == null) return;
            int slot = -1;
            for (int i = 0; i < tex.Length; i++) if (tex[i] == last) { slot = i; break; }
            if (slot < 0) return;
            MoveToFront(slot);
            _captures++;
            Rebind();
            DrawLabels();
        }

        /// <summary>KD_PhotoSettings の状態が変わった (復元・ON/OFF・作り直しの完了・失敗・言語)。ローカル専用。</summary>
        public void _OnPhotoStateChanged()
        {
            Init();
            ApplyGate();
            Rebind();      // 実行時の RT へ差し替わった直後もここを通る
            DrawLabels();
        }

        /// <summary>手元メニューの写真の面を開いた (KD_TreatPouch._PhotoOpen)。ローカル専用。</summary>
        public void _RedrawMenu()
        {
            Init();
            _largeSlot = -1;
            _menuPage = 0;   // 開いたときはいつも新しい 8 枚から
            if (menuLargeRoot != null) menuLargeRoot.SetActive(false);
            if (menuHelpRoot != null) menuHelpRoot.SetActive(false);
            Rebind();
            DrawLabels();
        }

        // ---- 面のボタン (onClick → SendCustomEvent。引数のある呼び出しを使わない) ----
        // 番号は「今のページの中の位置」(0 = 左上)
        public void _MenuPick0() { ShowLarge(_menuPage * PageSize + 0); }
        public void _MenuPick1() { ShowLarge(_menuPage * PageSize + 1); }
        public void _MenuPick2() { ShowLarge(_menuPage * PageSize + 2); }
        public void _MenuPick3() { ShowLarge(_menuPage * PageSize + 3); }
        public void _MenuPick4() { ShowLarge(_menuPage * PageSize + 4); }
        public void _MenuPick5() { ShowLarge(_menuPage * PageSize + 5); }
        public void _MenuPick6() { ShowLarge(_menuPage * PageSize + 6); }
        public void _MenuPick7() { ShowLarge(_menuPage * PageSize + 7); }

        // ---- ページ (IMP-WLD-007 W1)。端で折り返す (最後の次は 1 ページ目)。ローカル専用 ----
        public void _MenuPagePrev() { _menuPage = Wrap(_menuPage - 1); _MenuLargeClose(); Rebind(); DrawLabels(); }
        public void _MenuPageNext() { _menuPage = Wrap(_menuPage + 1); _MenuLargeClose(); Rebind(); DrawLabels(); }
        public void _BoardPagePrev() { TurnBoard(-1); }
        public void _BoardPageNext() { TurnBoard(1); }

        /// <summary>ボードのページを送ってから boardPageResetSeconds 経った (遅延イベント)。その間にまた送っていれば、後の方の遅延が戻す。ローカル専用。</summary>
        public void _BoardPageTimeout()
        {
            if (_boardPage == 0) return;
            if (Time.time - _boardPageAt < boardPageResetSeconds - 0.05f) return;
            _boardPage = 0;
            Rebind();
        }

        public void _MenuLargeClose()
        {
            _largeSlot = -1;
            if (menuLargeRoot != null) menuLargeRoot.SetActive(false);
            BindThumbSnaps();
        }

        public void _MenuHelpOpen()
        {
            if (menuHelpRoot == null) return;
            _MenuLargeClose();
            menuHelpRoot.SetActive(true);
            BindThumbSnaps();
            DrawLabels();
        }

        public void _MenuHelpClose()
        {
            if (menuHelpRoot != null) menuHelpRoot.SetActive(false);
            BindThumbSnaps();
        }

        /// <summary>設定の [写真: ON / OFF]。OFF は新しい撮影だけを止める (撮った写真は見られる。KD_PhotoSettings の既存の意味)。</summary>
        public void _ToggleCapture()
        {
            if (settings == null || !settings.Ready) return;
            if (settings.CurrentMode == 0) settings._Standard();
            else settings._Off();
        }

        // ================================================================ 中身

        private void ShowLarge(int position)
        {
            Init();
            if (position < 0 || position >= _count || menuLargeRoot == null || menuLarge == null || gallery == null) return;
            int slot = _order[position];
            RenderTexture[] tex = gallery.slotTextures;
            if (slot < 0 || tex == null || slot >= tex.Length || tex[slot] == null) return;
            if (menuHelpRoot != null) menuHelpRoot.SetActive(false);
            _largeSlot = slot;
            menuLarge.texture = tex[slot];
            if (menuSnapPhoto != null) menuSnapPhoto.material.mainTexture = tex[slot];   // the snap quad shows the same RT (複製の材質)
            menuLargeRoot.SetActive(true);
            BindThumbSnaps();   // IMP-WLD-009: the thumbnails' snap quads hide behind the enlarged photo
            DrawLabels();
        }

        private void MoveToFront(int slot)
        {
            int at = -1;
            for (int k = 0; k < _count; k++) if (_order[k] == slot) { at = k; break; }
            if (at < 0)
            {
                // 新しい枠: 後ろへずらして先頭へ (8 枚そろうまで)
                at = _count < Slots ? _count : Slots - 1;
                if (_count < Slots) _count++;
            }
            for (int k = at; k > 0; k--) _order[k] = _order[k - 1];
            _order[0] = slot;
        }

        private int PageCount() { return _count <= PageSize ? 1 : (_count + PageSize - 1) / PageSize; }

        private int Wrap(int page)
        {
            int n = PageCount();
            if (page < 0) return n - 1;
            if (page >= n) return 0;
            return page;
        }

        private void TurnBoard(int step)
        {
            Init();
            _boardPage = Wrap(_boardPage + step);
            _boardPageAt = Time.time;
            Rebind();
            if (_boardPage != 0 && boardPageResetSeconds > 0f) SendCustomEventDelayedSeconds(nameof(_BoardPageTimeout), boardPageResetSeconds);
        }

        /// <summary>撮ってよいときだけ撮影の見回り (KD_PhotoDirector) を動かす。毎回書く (値が同じでも害は無い。読み取りを使わない)。</summary>
        private void ApplyGate()
        {
            bool allowed = surfacesPlaced && settings != null && settings.CaptureAllowed;
            if (directorUdon != null) directorUdon.enabled = allowed;
        }

        /// <summary>並びの位置 (0 = いちばん新しい) の写真の RT。無ければ null。</summary>
        private RenderTexture AtPosition(int position, RenderTexture[] tex)
        {
            int slot = position >= 0 && position < _count ? _order[position] : -1;
            return slot >= 0 && tex != null && slot < tex.Length ? tex[slot] : null;
        }

        /// <summary>ページの中の位置 k → 並びの位置 (ページ × 8 + k) → スロット の対応で、ボードの Quad とメニューの縮小表示へ同じ RT を張る。
        /// ボードとメニューはページを別々に持つ (ボードは誰かが送ったまま、メニューは開くたびに 1 ページ目)。</summary>
        private void Rebind()
        {
            Init();
            int pages = PageCount();
            if (_menuPage >= pages) _menuPage = pages - 1;
            if (_boardPage >= pages) _boardPage = pages - 1;
            RenderTexture[] tex = gallery != null ? gallery.slotTextures : null;
            for (int k = 0; k < PageSize; k++)
            {
                // ---- ボード ----
                int bp = _boardPage * PageSize + k;
                RenderTexture bt = AtPosition(bp, tex);
                bool bshow = bt != null;
                if (boardPhotos != null && k < boardPhotos.Length && boardPhotos[k] != null)
                {
                    GameObject go = boardPhotos[k].gameObject;
                    if (bshow) boardPhotos[k].material.mainTexture = bt;
                    if (go.activeSelf != bshow) go.SetActive(bshow);
                }
                if (bshow && boardTaps != null && k < boardTaps.Length && boardTaps[k] != null) boardTaps[k].slotIndex = _order[bp];
                // ---- メニュー ----
                RenderTexture mt = AtPosition(_menuPage * PageSize + k, tex);
                bool mshow = mt != null;
                if (menuThumbs != null && k < menuThumbs.Length && menuThumbs[k] != null) menuThumbs[k].texture = mt;
                if (menuThumbButtons != null && k < menuThumbButtons.Length && menuThumbButtons[k] != null && menuThumbButtons[k].activeSelf != mshow)
                    menuThumbButtons[k].SetActive(mshow);
            }
            if (_largeSlot >= 0 && menuLarge != null && tex != null && _largeSlot < tex.Length)
            {
                menuLarge.texture = tex[_largeSlot];
                if (menuSnapPhoto != null) menuSnapPhoto.material.mainTexture = tex[_largeSlot];
            }
            BindThumbSnaps();
            // ---- ページ送り (2 ページ以上のときだけ) ----
            bool many = pages > 1;
            if (menuPager != null && menuPager.activeSelf != many) menuPager.SetActive(many);
            if (boardPager != null && boardPager.activeSelf != many) boardPager.SetActive(many);
            string total = "/" + pages.ToString();
            if (menuPageLabel != null) menuPageLabel.text = (_menuPage + 1).ToString() + total;
            if (boardPageLabel != null) boardPageLabel.text = (_boardPage + 1).ToString() + total;
        }

        /// <summary>IMP-WLD-009: 縮小表示 8 枚の手前の吸着の板へ、表示中のページの同じ RT を張る (材質の複製)。写真の無い位置は隠す。
        /// 大きい表示・説明を開いている間は 8 枚とも隠す (板は深度を書くので前の UI を塞ぎ、後ろで吸着もしてしまう)。閉じたら戻す。</summary>
        private void BindThumbSnaps()
        {
            if (menuThumbSnaps == null) return;
            Init();
            RenderTexture[] tex = gallery != null ? gallery.slotTextures : null;
            bool covered = (menuLargeRoot != null && menuLargeRoot.activeSelf) || (menuHelpRoot != null && menuHelpRoot.activeSelf);
            for (int k = 0; k < PageSize && k < menuThumbSnaps.Length; k++)
            {
                Renderer r = menuThumbSnaps[k];
                if (r == null) continue;
                RenderTexture t = AtPosition(_menuPage * PageSize + k, tex);
                bool show = t != null && !covered;
                if (t != null) r.material.mainTexture = t;
                GameObject go = r.gameObject;
                if (go.activeSelf != show) go.SetActive(show);
            }
        }

        private string StateKey()
        {
            if (settings == null) return "";
            if (!settings.Ready) return "photo.settings.loading";
            if (settings.Failed) return "photo.settings.failed";
            if (settings.CurrentMode == 0) return "photo.settings.off";
            if (_count == 0) return "photo.empty";
            return "";
        }

        private void DrawLabels()
        {
            string key = StateKey();
            string state = key.Length == 0 ? "" : L(key);
            bool off = settings != null && settings.Ready && settings.CurrentMode == 0;
            // ---- ボード ----
            if (boardCaption != null) boardCaption.text = L("gallery.caption");
            if (boardState != null) boardState.text = state;
            string tap = L("KD_PhotoBoard.InteractWord");
            if (boardTapUdons != null)
                for (int k = 0; k < boardTapUdons.Length; k++)
                    if (boardTapUdons[k] != null) boardTapUdons[k].InteractionText = tap;
            if (viewerUdon != null) viewerUdon.InteractionText = L("KD_PhotoViewer.InteractWord");
            // ---- ヘッダのボタン・設定の行 ----
            string title = L("photo.menu");
            if (headerLabel != null) headerLabel.text = title;
            if (settingsToggleLabel != null)
                settingsToggleLabel.text = title + ": " + (settings == null || !settings.Ready ? "…" : off ? "OFF" : "ON");
            if (settingsToggleImage != null) settingsToggleImage.color = off ? offColor : onColor;
            if (settingsToggleButton != null) settingsToggleButton.interactable = settings != null && settings.Ready;
            // ---- メニューの面 ----
            if (menuState != null) menuState.text = state;
            if (menuSettingsButton != null && menuSettingsButton.activeSelf != off) menuSettingsButton.SetActive(off);
            if (menuSettingsLabel != null) menuSettingsLabel.text = L("KD_TreatPouch.WordSettings");
            if (menuBackLabel != null) menuBackLabel.text = L("gift.back");
            if (menuHelpMarkLabel != null) menuHelpMarkLabel.text = L("photo.help.mark");
            if (menuHelpTitle != null) menuHelpTitle.text = L("photo.help.title");
            if (menuHelpBody != null) menuHelpBody.text = L("photo.help.body");
            if (menuHelpCloseLabel != null) menuHelpCloseLabel.text = L("photo.help.close");
            if (menuLargeCloseLabel != null) menuLargeCloseLabel.text = L("photo.help.close");
        }
    }
}
