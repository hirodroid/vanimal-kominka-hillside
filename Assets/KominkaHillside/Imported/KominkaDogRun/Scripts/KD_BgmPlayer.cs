using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-ENV-009: **BGM。** 設計の正本は
    /// [IMP-ENV-009](../../../docs/development/issues/IMP-ENV-009-bgm.md)。
    ///
    /// 一文で: **出力を掛け算で毎フレーム作り直すだけ。状態を控えて書き戻さない。**
    ///
    /// <code>
    ///   AudioSource.volume = baseVolume × 現在地の個人音量 × ライブの掛け金 (1 → 0)
    ///   AudioSource.mute   = プレイヤーが OFF、または現在地の個人音量が 0
    /// </code>
    ///
    /// **鳴らすのは `PlayOnAwake`** (Setup が音源に立てる)。ここは `Play()` を呼ばない —
    /// 入室した瞬間から鳴っていてほしいので、Udon の初期化を待たせない。
    ///
    /// ライブ中の消音を「開始時に今の ON/OFF を控えて終了時に書き戻す」で作らないのが要点。
    /// あの形は**復元漏れ・二重復元**が必ず出る (ライブ中に人が押したら / 途中入室したら /
    /// 2 本目が始まったら)。掛け金を別に持って掛けるだけにすると、
    /// **プレイヤーの設定を一度も書き換えない**ので「元がオフならオフのまま」が
    /// 復元処理なしで成り立つ。
    ///
    /// 掛け金は <see cref="KD_LiveSystem.IsNightWanted"/> — **同期時計の純関数**なので
    /// ローカルのフラグが 1 つも要らない。途中入室でもライブ中なら最初から下がっているし、
    /// 開催者が落ちても時計どおりに戻る (<see cref="KD_LiveNight"/> と同じ作法)。
    /// </summary>
    /// <remarks>SyncMode None。**同期変数 0 本** — 音量は各自の耳の話で、他人の耳を変えられてはいけない。</remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_BgmPlayer : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/BGM/2 - Setup BGM が設定する)")]
        [Tooltip("BGM の音源。2D (spatialBlend 0) / loop / PlayOnAwake")]
        public AudioSource source;

        [Tooltip("ライブの台帳。null だとライブ中も下がらない (設定は効く)")]
        public KD_LiveSystem live;

        // Hillside parade has its own consent clock. Personal settings remain independent.
        public KominkaHillside.KH_HalloweenParadePlayback paradePlayback;

        [Tooltip("裏山 IMP-UI-011: 動画プレイヤー (VizVid の Core)。再生中は BGM をライブと同じ掛け金で消す。Player/1 が配線。任意")]
        public JLChnToZ.VRC.VVMW.Core videoCore;

        [Tooltip("IMP-ENV-011: 外と寝室の独立した個人音量を現在位置で補間する")]
        public KD_SleepAudio sleepAudio;

        [Tooltip("素の音量 (作者が決めた大きさ)。プレイヤーの設定はこれに掛かる。**音量の正本はここ** — " +
            "AudioSource.volume は毎フレーム作り直されるので、あちらを直しても残らない")]
        public float baseVolume = DefaultBaseVolume;

        /// <summary>0 で入ると無言で鳴らなくなるので、Start が既定へ倒す値。</summary>
        public const float DefaultBaseVolume = 0.35f;

        /// <summary>ライブの掛け金を動かす時間 (秒)。ぶつ切りにしない。</summary>
        public const float FadeSeconds = 1f;

        private float _userVolume = 0.5f; // プレイヤーの音量 0〜1
        private bool _userEnabled = true; // プレイヤーの ON/OFF
        private float _duck = 1f;         // ライブの掛け金 (1 = 素通し / 0 = ライブ中で消えている)

        /// <summary>プレイヤーが選んだ音量 (0〜1)。**ライブ中も変わらない。**</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public float UserVolume { get { return _userVolume; } }

        /// <summary>プレイヤーが ON にしているか。**ライブ中も変わらない。**</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public bool UserEnabled { get { return _userEnabled; } }

        /// <summary>ライブの掛け金 (1 = 素通し / 0 = 消えている)。デバッグ・検査用。</summary>
        // net: callable-ok: 副作用の無い問い合わせ
        public float LiveDuck { get { return _duck; } }

        void Start()
        {
            if (source == null)
            {
                Debug.LogError("[KD_BgmPlayer] source が未配線 - BGM が鳴らない。Run Tools/KominkaDogRun/BGM/2 - Setup BGM.");
                return;
            }
            // 0 で入ると無言で鳴らなくなる (破壊的監査 RC-1 の 3 規則)
            if (baseVolume <= 0f) baseVolume = DefaultBaseVolume;
            // **途中入室はフェードを待たない** — ライブ中に入った人は最初から下がっている
            _duck = LiveSilences() ? 0f : 1f;
            Apply();
        }

        void Update()
        {
            float want = LiveSilences() ? 0f : 1f;
            float step = FadeSeconds > 0.01f ? Time.deltaTime / FadeSeconds : 1f;
            _duck = Mathf.MoveTowards(_duck, want, step);
            Apply();
        }

        /// <summary>手元メニューの [設定] から。**ローカル専用** (`_` はセキュリティの印)。
        /// 掛け金には触らない — ライブ中に動かしても、ライブが終わればここで選んだ値で戻る。</summary>
        public void _SetBgm(float volume, bool enabled)
        {
            _userVolume = Mathf.Clamp01(volume);
            _userEnabled = enabled;
            Apply();
        }

        /// <summary>ライブが BGM を消す時間帯か。**夜のフェードインの頭からタイムラインの終わりまで。**
        /// 曲が鳴り出す瞬間ではなく夜が始まった瞬間に引く (暗くなり始めてから BGM が鳴っていると
        /// 「これから何かが始まる」が濁る)。</summary>
        private bool LiveSilences()
        {
            return (live != null && live.IsNightWanted) || (paradePlayback != null && paradePlayback.IsAudible)
                || (videoCore != null && videoCore.IsPlaying && !videoCore.IsStatic);   // IMP-UI-011: 静止画 (ImageViewer) では消さない (VizVid の BGMVolumeControl と同じ除外)
        }

        /// <summary>**出力を作り直す。** 差分ではなく毎回の掛け算。</summary>
        private void Apply()
        {
            if (source == null) return;
            float roomGain = sleepAudio != null ? sleepAudio.VolumeFor(_userVolume, sleepAudio.BedroomBgmVolume) : _userVolume;
            float vol = baseVolume * _duck * roomGain;
            bool mute = !_userEnabled || roomGain <= 0f;
            // IMP-DOG-034 C1-misc: write the native AudioSource only when the value changes (first Apply always writes).
            // Unchanged behaviour: this behaviour is the only writer of `source` (no other Udon touches it), so the
            // AudioSource already holds the cached value whenever the computed one is equal.
            if (!_written || vol != _lastVolume) { source.volume = vol; _lastVolume = vol; }
            if (!_written || mute != _lastMute) { source.mute = mute; _lastMute = mute; }
            _written = true;
        }

        private bool _written;     // IMP-DOG-034 C1-misc: false until the first Apply wrote both values
        private float _lastVolume;
        private bool _lastMute;
    }
}
