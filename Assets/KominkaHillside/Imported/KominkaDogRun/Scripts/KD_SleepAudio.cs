using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>IMP-ENV-011: 寝室のカテゴリ別音量。現在位置から毎フレーム導出し、同期・再生時計には触れない。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DefaultExecutionOrder(-100)]
    public class KD_SleepAudio : UdonSharpBehaviour
    {
        [Tooltip("寝室を囲むTrigger。Center/Sizeで範囲を調整する。スケールは1にする")]
        public BoxCollider roomBounds;
        [Tooltip("寝室の外側で通常音量と寝室音量をつなぐ帯の幅 (m)")]
        public float fadeDistance = 0.6f;
        [Tooltip("室外へ移動したときの復帰時間 (秒)。テレポートでも急に鳴らさない")]
        public float returnSeconds = 0.4f;
        public AudioSource[] liveSources;
        public float[] liveBaseVolumes;

        // LocalPlayerが準備できるまでは消音。曲の初回起動時も音漏れを作らない。
        private float _outsideGain;
        private float _liveVolume = 0.5f;
        private float _bedroomLiveVolume = 0.1f;
        private float _bedroomBgmVolume = 0.1f;
        private float _bedroomVoiceVolume = 0.1f;
        private bool _positionKnown;
        // net: callable-ok: 個人の出力倍率の読み取りのみ
        public float VolumeFor(float outsideVolume, float bedroomVolume)
        {
            return _positionKnown ? Mathf.Lerp(bedroomVolume, outsideVolume, _outsideGain) : 0f;
        }
        // net: callable-ok: 個人設定の読み取りのみ
        public float LiveVolume { get { return _liveVolume; } }
        // net: callable-ok: 個人設定の読み取りのみ
        public float BedroomLiveVolume { get { return _bedroomLiveVolume; } }

        // net: callable-ok: 個人設定の読み取りのみ
        public float BedroomBgmVolume { get { return _bedroomBgmVolume; } }
        // net: callable-ok: 個人設定の読み取りのみ
        public float BedroomVoiceVolume { get { return _bedroomVoiceVolume; } }

        public void _SetBedroomBgmVolume(float value) { _bedroomBgmVolume = Mathf.Clamp01(value); }
        public void _SetBedroomVoiceVolume(float value) { _bedroomVoiceVolume = Mathf.Clamp01(value); }

        public void _SetLiveVolume(float value)
        {
            _liveVolume = Mathf.Clamp01(value);
            ApplyLive();
        }

        public void _SetBedroomLiveVolume(float value)
        {
            _bedroomLiveVolume = Mathf.Clamp01(value);
            ApplyLive();
        }

        void Update()
        {
            var player = Networking.LocalPlayer;
            bool known = Utilities.IsValid(player);
            float target = 0f;
            if (known)
            {
                Vector3 head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                target = Mathf.Min(GainAt(head), GainAt(player.GetPosition()));
            }
            UpdateGain(target, known, Time.deltaTime);
        }

        private void UpdateGain(float target, bool known, float deltaTime)
        {
            _positionKnown = known;
            // 入室は空間フェードに従い、テレポート入室は即時に寝室設定。退出だけ時間でも滑らかに戻す。
            _outsideGain = target < _outsideGain ? target : Mathf.MoveTowards(_outsideGain, target,
                returnSeconds > 0f ? deltaTime / returnSeconds : 1f);
            ApplyLive();
        }

        // net: callable-ok: 座標から音量を計算する純関数。Headと足元の両方を確認する
        public float GainAt(Vector3 worldPosition)
        {
            if (roomBounds == null) return 1f;
            Vector3 p = roomBounds.transform.InverseTransformPoint(worldPosition) - roomBounds.center;
            Vector3 roomHalfSize = roomBounds.size * 0.5f;
            Vector3 outside = new Vector3(Mathf.Max(0f, Mathf.Abs(p.x) - roomHalfSize.x),
                Mathf.Max(0f, Mathf.Abs(p.y) - roomHalfSize.y),
                Mathf.Max(0f, Mathf.Abs(p.z) - roomHalfSize.z));
            float distance = outside.magnitude;
            if (distance <= 0f) return 0f;
            float t = fadeDistance > 0f ? Mathf.Clamp01(distance / fadeDistance) : 1f;
            return t * t * (3f - 2f * t);
        }

        private void ApplyLive()
        {
            if (liveSources == null || liveBaseVolumes == null) return;
            float gain = VolumeFor(_liveVolume, _bedroomLiveVolume);
            for (int i = 0; i < liveSources.Length && i < liveBaseVolumes.Length; i++)
            {
                var source = liveSources[i];
                if (source == null) continue;
                // inactiveの曲も更新する。次の曲を有効化した瞬間も同じ消音が掛かる。
                source.volume = liveBaseVolumes[i] * gain;
                source.mute = gain <= 0f;
            }
        }
    }
}
