using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (Settings)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_TreatPouch
    {
        public void _OpenVolumeSettings()
        {
            _extraSettingsPage = null;
            _settingsOpen = true;
            _volumeSettingsOpen = true;
            _distanceFadeSettingsOpen = false;
            ApplyTab();
            Redraw();
        }

        public void _OpenDistanceFadeSettings()
        {
            _extraSettingsPage = null;
            _settingsOpen = true;
            _volumeSettingsOpen = false;
            _distanceFadeSettingsOpen = true;
            ApplyTab();
            if (distanceFadeSettings != null) distanceFadeSettings._RefreshUI();
            Redraw();
        }

        public void _LiveVolumeChanged()
        {
            if (sleepAudio != null && liveVolumeSlider != null) sleepAudio._SetLiveVolume(liveVolumeSlider.value);
            RedrawMusicSettings();
        }

        public void _BedroomLiveVolumeChanged()
        {
            if (sleepAudio != null && bedroomLiveVolumeSlider != null) sleepAudio._SetBedroomLiveVolume(bedroomLiveVolumeSlider.value);
            RedrawMusicSettings();
        }

        public void _BedroomBgmVolumeChanged()
        {
            if (sleepAudio != null && bedroomBgmVolumeSlider != null) sleepAudio._SetBedroomBgmVolume(bedroomBgmVolumeSlider.value);
            ApplyBgmSettings();
            RedrawMusicSettings();
        }

        public void _BedroomVoiceVolumeChanged()
        {
            if (sleepAudio != null && bedroomVoiceVolumeSlider != null) sleepAudio._SetBedroomVoiceVolume(bedroomVoiceVolumeSlider.value);
            ApplyDogVoiceSettings();
            RedrawMusicSettings();
        }

        private void RedrawMusicSettings()
        {
            if (sleepAudio == null) return;
            if (bedroomBgmVolumeSlider != null) bedroomBgmVolumeSlider.SetValueWithoutNotify(sleepAudio.BedroomBgmVolume);
            if (bedroomVoiceVolumeSlider != null) bedroomVoiceVolumeSlider.SetValueWithoutNotify(sleepAudio.BedroomVoiceVolume);
            if (bedroomBgmVolumeValueLabel != null) bedroomBgmVolumeValueLabel.text = Mathf.RoundToInt(sleepAudio.BedroomBgmVolume * 100f).ToString() + "%";
            if (bedroomVoiceVolumeValueLabel != null) bedroomVoiceVolumeValueLabel.text = Mathf.RoundToInt(sleepAudio.BedroomVoiceVolume * 100f).ToString() + "%";
            if (liveVolumeSlider != null) liveVolumeSlider.SetValueWithoutNotify(sleepAudio.LiveVolume);
            if (bedroomLiveVolumeSlider != null) bedroomLiveVolumeSlider.SetValueWithoutNotify(sleepAudio.BedroomLiveVolume);
            if (liveVolumeValueLabel != null) liveVolumeValueLabel.text = Mathf.RoundToInt(sleepAudio.LiveVolume * 100f).ToString() + "%";
            if (bedroomLiveVolumeValueLabel != null) bedroomLiveVolumeValueLabel.text = Mathf.RoundToInt(sleepAudio.BedroomLiveVolume * 100f).ToString() + "%";
        }

        public void _DogVoiceChanged()
        {
            if (dogVoiceSlider == null) return;
            _voiceVolume = Mathf.Clamp01(dogVoiceSlider.value);
            ApplyDogVoiceSettings();
        }

        public void _ToggleDogVoice()
        {
            _voiceEnabled = !_voiceEnabled;
            ApplyDogVoiceSettings();
        }

        private float CurrentVoiceGain()
        {
            return sleepAudio != null ? sleepAudio.VolumeFor(_voiceVolume, sleepAudio.BedroomVoiceVolume) : _voiceVolume;
        }

        private void ApplyDogVoiceSettings()
        {
            _appliedVoiceGain = CurrentVoiceGain();
            if (dogVoiceSources != null && dogVoiceBaseVolumes != null)
                for (int i = 0; i < dogVoiceSources.Length && i < dogVoiceBaseVolumes.Length; i++)
                {
                    AudioSource source = dogVoiceSources[i];
                    if (source == null) continue;
                    source.volume = dogVoiceBaseVolumes[i] * _appliedVoiceGain;
                    source.mute = !_voiceEnabled || _appliedVoiceGain <= 0f;
                }
            if (voiceDogs != null)
                for (int i = 0; i < voiceDogs.Length; i++)
                {
                    KD_DogWanderController dog = voiceDogs[i];
                    if (dog == null) continue;
                    dog.localVoiceGain = _appliedVoiceGain;
                    dog._ApplyVoiceVolume();
                    if (dog.breathSource != null) dog.breathSource.mute = !_voiceEnabled || _appliedVoiceGain <= 0f;
                }
            RedrawVoiceSettings();
        }

        /// <summary>BGM のスライダーから。**ライブ中に動かしても構わない** —
        /// ライブの掛け金は別に持っているので、ここで選んだ値がライブ明けに戻る。</summary>
        public void _BgmChanged()
        {
            if (bgmSlider == null) return;
            _bgmVolume = Mathf.Clamp01(bgmSlider.value);
            ApplyBgmSettings();
        }

        public void _ToggleBgm()
        {
            _bgmEnabled = !_bgmEnabled;
            ApplyBgmSettings();
        }

        /// <summary>**選んだ値を渡すだけ。** ライブ中かどうかはここでは見ない
        /// (見ると「元がオフならオフのまま」を復元処理で作ることになる。IMP-ENV-009)。</summary>
        private void ApplyBgmSettings()
        {
            if (bgmPlayer != null) bgmPlayer._SetBgm(_bgmVolume, _bgmEnabled);
            RedrawBgmSettings();
        }

        private void RedrawBgmSettings()
        {
            if (bgmValueLabel != null)
                bgmValueLabel.text = Mathf.RoundToInt(_bgmVolume * 100f).ToString() + "%";
            if (bgmToggleLabel != null)
                bgmToggleLabel.text = _bgmEnabled ? WordVoiceOn : WordVoiceOff;
            if (bgmToggleImage != null)
                bgmToggleImage.color = _bgmEnabled ? equipOnColor : tabOffColor;
        }

        private void RedrawVoiceSettings()
        {
            if (dogVoiceValueLabel != null)
                dogVoiceValueLabel.text = Mathf.RoundToInt(_voiceVolume * 100f).ToString() + "%";
            if (dogVoiceToggleLabel != null)
                dogVoiceToggleLabel.text = _voiceEnabled ? WordVoiceOn : WordVoiceOff;
            if (dogVoiceToggleImage != null)
                dogVoiceToggleImage.color = _voiceEnabled ? equipOnColor : tabOffColor;
        }
    }
}
