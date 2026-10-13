using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using KominkaDogRun;

namespace KominkaHillside
{
    /// <summary>IMP-DOG-054 §3 (決定 7): 芸「バーン」「ジャンプ」が使える時だけ、本人にだけ出すガイド。
    /// 読むのは KD_CallPose の公開 API (§4.2) だけ。所有権を取らない・共有状態を書かない・要求を送らない。
    ///
    /// 出す・消す (状態は KD_CallPose._TrickStatus):
    /// - 0 使える: VR はゾーンの輪 2 つ + 芸名と合図の案内 + 小さい補助行 (メニューのボタンでもできる)。Desktop・Touch は補助行を主行として 1 行。
    /// - 5 間隔の残り: VR は輪を薄く・芸名だけ。どちらも「あと N 秒」。
    /// - 4 再生中: 受理された側の輪が一度光って消え、「再生中」。
    /// - 3 (要求中・おやつ中など): 消す。ただし対象犬を覚えておき、同じ犬が 3 → 4 に移ったら受理の光と「再生中」を出す (正常な要求の経路 0 → 3 → 4)。
    /// - 1・2・6・7・8 / 対象犬なし (-1): 消して、覚えている犬・光・フェードも捨てる。
    /// 表示の始まり (フェードイン) = 見えていない所から出る時。犬が変わった時・6 m 圏内へ戻った時・3 から 0 / 5 へ戻った時を含む。
    /// 4 → 5 → 0 と 3 → 4 は続きとして扱い、フェードし直さない。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_TrickGuide : UdonSharpBehaviour
    {
        public const int KindBang = 2, KindJump = 3;   // KD_SleepSlot.TrickBang / TrickJump (§2.1 の trickKind)
        public const int StatusUsable = 0, StatusBusy = 3, StatusPlaying = 4, StatusWait = 5;

        public KD_CallPose callPose;
        public KD_Language language;
        // 輪 (KH_WalkIntro の Ring メッシュ。外径 ringMeshRadius m)。色は MaterialPropertyBlock で変える (共有マテリアルは書かない)
        public Transform bangRing, jumpRing;
        public Renderer bangRingRenderer, jumpRingRenderer;
        // 読む専用の WorldSpace Canvas (Collider / Raycaster なし)。zone* は左端基準で輪の右へ並べる
        public Transform bangText, jumpText, statusText, menuText;
        public CanvasGroup bangFade, jumpFade, statusFade, menuFade;
        public TMP_Text bangLabel, jumpLabel, statusLabel, menuLabel;
        public Color bangColor = new Color(1f, .55f, .25f), jumpColor = new Color(.35f, .85f, 1f);
        public float ringMeshRadius = .075f;
        public float fadeSeconds = 2f, flashSeconds = .6f, showDistance = 6f, showHysteresis = .5f;
        public float menuScaleVr = .7f;    // VR の補助行は Desktop の主行より小さく

        private const float TickSeconds = .15f;
        private int _dog = -1, _status = -1, _flashKind;
        private bool _visible, _near, _waitFor4, _flashArmed, _vr, _vrKnown;
        private float _nextTick, _shownAt, _flashUntil, _bangRadius, _jumpRadius, _menuScale = .002f;
        private int _zoneMode = -1, _statusKey = -1;
        private string _shownLanguage;
        private Transform _root;
        private MaterialPropertyBlock _block;

        private void Start()
        {
            _block = new MaterialPropertyBlock();
            if (menuText != null) _menuScale = menuText.localScale.x;
            HideVisuals();
        }

        private void Update()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + TickSeconds;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || callPose == null) return;
            if (!_vrKnown) { _vr = lp.IsUserInVR(); _vrKnown = true; }
            Tick(lp);
        }

        private void Tick(VRCPlayerApi lp)
        {
            string lang = language != null ? language.CurrentLanguage : "ja";
            if (lang != _shownLanguage) { _shownLanguage = lang; _zoneMode = -1; _statusKey = -1; if (menuLabel != null) menuLabel.text = L("call.trick.hint.menu"); }

            int dog = callPose._TrickGuideDog();
            if (dog < 0)
            {
                // 正常な要求は 0 → 3 (要求中、2 s 以内) → 4。3 の間は消すが対象犬は覚えておく (3 → 4 で受理の光と「再生中」)
                if (_dog >= 0 && callPose._TrickStatus(_dog) == StatusBusy)
                {
                    if (_visible) _waitFor4 = true;
                    if (_status == StatusUsable || _status == StatusWait) _flashArmed = true;
                    _status = StatusBusy;
                    HideVisuals();
                    return;
                }
                Forget();
                return;
            }
            if (dog != _dog) { Forget(); _dog = dog; }   // 犬が変わった = 新しい始まり
            int status = callPose._TrickStatus(dog);
            KD_SleepSlot slot = callPose.board != null ? callPose.board.SlotOf(dog) : null;
            _root = slot != null ? slot.DogRoot() : null;
            Vector3 dogAt = _root != null ? _root.position : callPose._TrickZoneCenter(dog, KindBang);
            Vector3 offset = lp.GetPosition() - dogAt; offset.y = 0f;
            float limit = _near ? showDistance : showDistance - showHysteresis;
            _near = offset.sqrMagnitude <= limit * limit;
            if (!_near)
            {
                // 6 m の外: 消す。戻ったら新しい始まり。犬は覚えたまま (受理の光は捨てる)
                _status = status; _flashArmed = false; _waitFor4 = false;
                HideVisuals();
                return;
            }

            bool resume = _waitFor4 && status == StatusPlaying;   // 3 → 4 は続き
            if (!_visible && !resume) _shownAt = Time.time;        // 新しい始まり (フェードイン)
            if (resume && Time.time - _shownAt < fadeSeconds) _shownAt = Time.time - fadeSeconds;
            _waitFor4 = false;

            // 受理の光は 犬 × 開始 (状態 4 に入った時) で 1 回: 0 / 3 / 5 を見た後に 4 へ入った時だけ
            if (status == StatusUsable || status == StatusWait) _flashArmed = true;
            if (status == StatusPlaying && _status != StatusPlaying && _flashArmed)
            {
                _flashArmed = false;
                _flashKind = callPose._TrickLastAccepted(dog);
                _flashUntil = (_flashKind == KindBang || _flashKind == KindJump) ? Time.time + flashSeconds : 0f;
            }
            _status = status;
            _visible = true;
            _bangRadius = callPose._TrickZoneRadius(dog, KindBang);
            _jumpRadius = callPose._TrickZoneRadius(dog, KindJump);
            Apply(status);
        }

        // 表示する部品と文字をこの tick の状態に合わせる (位置と明るさは PostLateUpdate)
        private void Apply(int status)
        {
            bool flashing = _vr && status == StatusPlaying && Time.time < _flashUntil;   // 光る輪は VR だけ
            bool rings = _vr && (status == StatusUsable || status == StatusWait);
            SetVisible(bangRing, (rings && _bangRadius > 0f) || (flashing && _flashKind == KindBang));
            SetVisible(jumpRing, (rings && _jumpRadius > 0f) || (flashing && _flashKind == KindJump));
            SetVisible(bangText, rings && _bangRadius > 0f);
            SetVisible(jumpText, rings && _jumpRadius > 0f);
            if (rings)
            {
                int mode = status == StatusUsable ? 0 : 1;
                if (mode != _zoneMode)
                {
                    _zoneMode = mode;
                    bangLabel.text = mode == 0 ? Zone("call.trick.bang", "call.trick.hint.bang") : L("call.trick.bang");
                    jumpLabel.text = mode == 0 ? Zone("call.trick.jump", "call.trick.hint.jump") : L("call.trick.jump");
                }
            }
            SetVisible(menuText, status == StatusUsable);
            if (menuText != null) menuText.localScale = Vector3.one * (_menuScale * (_vr ? menuScaleVr : 1f));
            bool line = (status == StatusPlaying && !flashing) || status == StatusWait;
            SetVisible(statusText, line);
            if (line)
            {
                int key = 1;
                if (status == StatusWait) key = 1000 + Mathf.Max(1, Mathf.CeilToInt(callPose._TrickWaitSeconds(_dog)));
                if (key != _statusKey)
                {
                    _statusKey = key;
                    statusLabel.text = key == 1 ? L("call.trick.playing") : L("call.trick.wait").Replace("{0}", (key - 1000).ToString());
                }
            }
        }

        public override void PostLateUpdate()
        {
            if (!_visible || _dog < 0 || callPose == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            Vector3 eye = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            float alpha = fadeSeconds > 0f ? Mathf.Clamp01((Time.time - _shownAt) / fadeSeconds) : 1f;
            alpha = alpha * alpha * (3f - 2f * alpha);
            // 呼ばれた犬は配置点に凍結している。位置は毎フレーム根から計算し直す (KD_CallPose が根のローカルから出す)
            Vector3 bang = callPose._TrickZoneCenter(_dog, KindBang);
            Vector3 jump = callPose._TrickZoneCenter(_dog, KindJump);
            Vector3 head = _root != null ? _root.position + Vector3.up * 1.05f : bang;
            Vector3 anchor = jump != Vector3.zero ? jump + Vector3.up * (_jumpRadius + .12f) : head;
            Vector3 toward = anchor - eye; toward.y = 0f;
            Vector3 right = toward.sqrMagnitude > .0001f ? Vector3.Cross(Vector3.up, toward.normalized) : Vector3.right;
            bool flashing = _status == StatusPlaying && Time.time < _flashUntil;
            float flash = flashing ? 1f - (_flashUntil - Time.time) / Mathf.Max(.01f, flashSeconds) : 0f;
            if (_status == StatusPlaying && !flashing) { SetVisible(bangRing, false); SetVisible(jumpRing, false); }   // 光り終わり (次の tick を待たない)

            if (bangRing != null && bangRing.gameObject.activeSelf && bang != Vector3.zero)
                PlaceRing(bangRing, bangRingRenderer, bang, _bangRadius, bangColor, alpha, flashing, flash, eye);
            if (jumpRing != null && jumpRing.gameObject.activeSelf && jump != Vector3.zero)
                PlaceRing(jumpRing, jumpRingRenderer, jump, _jumpRadius, jumpColor, alpha, flashing, flash, eye);
            if (bangText != null && bangText.gameObject.activeSelf)
            { bangText.position = bang + right * (_bangRadius + .04f); Face(bangText, eye); bangFade.alpha = alpha; }
            if (jumpText != null && jumpText.gameObject.activeSelf)
            { jumpText.position = jump + right * (_jumpRadius + .04f); Face(jumpText, eye); jumpFade.alpha = alpha; }
            if (statusText != null && statusText.gameObject.activeSelf)
            { statusText.position = anchor; Face(statusText, eye); statusFade.alpha = alpha; }
            if (menuText != null && menuText.gameObject.activeSelf)
            {
                // VR: 芸名の下に小さく (補助行)。Desktop・Touch: 犬の頭の上に主行として
                menuText.position = _vr && bang != Vector3.zero ? bang + right * (_bangRadius + .04f) - Vector3.up * .14f : anchor;
                Face(menuText, eye); menuFade.alpha = alpha;
            }
        }

        private void PlaceRing(Transform ring, Renderer target, Vector3 center, float radius, Color color, float alpha, bool flashing, float flash, Vector3 eye)
        {
            ring.position = center;
            Vector3 look = center - eye;
            if (look.sqrMagnitude > .0001f) ring.rotation = Quaternion.LookRotation(look);   // 球のゾーンの輪郭として視線に正対
            float scale = radius / Mathf.Max(.001f, ringMeshRadius);
            Color c;
            if (flashing) { scale *= 1f + .5f * flash; c = Color.Lerp(Color.white, color, flash); }
            else
            {
                scale *= Mathf.Lerp(.5f, 1f, alpha);
                c = color * Mathf.Lerp(.25f, _status == StatusWait ? .45f : 1f, alpha);   // 間隔中は薄く (不透明の暗い色)
                c.a = 1f;
            }
            ring.localScale = Vector3.one * scale;
            if (target != null)
            {
                _block.Clear();   // この輪の MPB を書くのはこのスクリプトだけ
                _block.SetColor("_Color", c);
                target.SetPropertyBlock(_block);
            }
        }

        private string Zone(string name, string hint) { return L(name) + "\n<size=62%>" + L(hint) + "</size>"; }
        private string L(string key) { return language != null ? language._Text(key) : key; }
        private void Face(Transform target, Vector3 eye)
        {
            Vector3 forward = target.position - eye; forward.y = 0f;
            if (forward.sqrMagnitude > .001f) target.rotation = Quaternion.LookRotation(forward);
        }
        private void SetVisible(Transform target, bool visible)
        {
            if (target != null && target.gameObject.activeSelf != visible) target.gameObject.SetActive(visible);
        }
        private void HideVisuals()
        {
            _visible = false; _flashUntil = 0f;
            SetVisible(bangRing, false); SetVisible(jumpRing, false);
            SetVisible(bangText, false); SetVisible(jumpText, false);
            SetVisible(statusText, false); SetVisible(menuText, false);
        }
        // 対象犬なし・状態 1/2/6/7/8・犬の交代: 表示も、覚えている犬・光・フェードも捨てる
        private void Forget()
        {
            HideVisuals();
            _dog = -1; _status = -1; _root = null; _near = false;
            _flashArmed = false; _waitFor4 = false; _flashKind = 0;
        }
    }
}
