using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-003: 専用 BGM「KH_Komorebi 003 (木漏れ日の帰り道)」の取り込み・常時再生・設定 → 音量の BGM の行。
    ///
    /// Course/98 は次を行い、本番シーンを保存する (再実行しても同じ結果):
    /// 1. Assets/KominkaHillside/Audio/BGM/KH_Komorebi_003_Loop.wav の SHA256 を Issue 記載の値と照合 (違えば何も変えずに止まる)。
    /// 2. 取り込み設定: Vorbis・Streaming (preload なし)・サンプルレート維持 (48 kHz)・stereo。品質は PC <see cref="PcQuality"/> / Android・iOS <see cref="MobileQuality"/>
    ///    (既定 + Standalone / Android / iOS の上書きを全部同じ形に。古民家の BGM と同じ持ち方)。変わったときだけ再インポート。
    /// 3. KH_Dogs/KD_Bgm に AudioSource (2D・loop・PlayOnAwake) + KD_BgmPlayer (SyncMode None) を置き、live = KD_LiveSystem、baseVolume = <see cref="BaseVolume"/>。
    ///    **鳴らすのは PlayOnAwake、出力は KD_BgmPlayer が毎フレーム「素の音量 × 個人の音量 × ライブの掛け金」で作り直す** (古民家 IMP-ENV-009 と同じコード)。
    /// 4. 手元メニューの 設定 → 音量 に BGM の行 (スライダー / 値 / [ON/OFF] / 注記) を作り pouch.bgmPlayer を配線する。
    ///    行を作るのは Menu/16 と同じ本体 (KD_DogVoiceSettingsEditor.EnsureHillsideVolume) なので、Menu/7・11・16 を流し直しても消えない。
    ///    注記の束縛 (Data/UILocalizationBindings.json) を反映するため、文言の束縛 (KD_LocalizationEditor.Bind) だけを流し直す (文言表と拡張表には触らない)。
    /// 5. 保存後に Course/99 と同じ検査を行う。
    ///
    /// 保存・復元は古民家と同じく **しない** (PlayerData を使わない。入室のたびに ON・50%)。新しい保存仕様はこの Issue で足さない。</summary>
    public static class KH_BgmEditor
    {
        public const string ClipPath = "Assets/KominkaHillside/Audio/BGM/KH_Komorebi_003_Loop.wav";
        /// <summary>IMP-WLD-003「対象音源」の SHA256 (vrc-music-agent songs/KH_Komorebi/versions/003/output/KH_Komorebi_003_Loop.wav)。</summary>
        public const string ClipSha256 = "fca0b63df11744823972d0a5477db11fea4dd6a5dcd7bd0f8f8fc1fc1a201774";
        /// <summary>音源の事実 (review.md): 216 秒 = 10,368,000 フレーム、48 kHz、stereo。</summary>
        public const int ClipFrequency = 48000, ClipChannels = 2, ClipSamples = 10368000;
        /// <summary>作者の基準音量 (KD_BgmPlayer.baseVolume。個人の音量はこれに掛かる)。古民家の BGM と同じ 0.35 から始める。
        /// 003 は peak -3.0 dBFS / RMS -23.1 dBFS で、古民家の曲 (mp3・normalize あり) との聞こえの差は実機で見る (IMP-WLD-003 完了条件)。変えるならここだけ。</summary>
        public const float BaseVolume = 0.35f;
        /// <summary>Vorbis 品質。PC は余韻を残す側、モバイルは古民家の BGM (Android / iOS 0.5) と同じ。容量は実測で決め直す (IMP-WLD-003 §モバイル容量)。</summary>
        public const float PcQuality = 0.7f, MobileQuality = 0.5f;
        static readonly string[] MobilePlatforms = { "Android", "iOS" };
        const string OutDir = "Temp/imp-wld-003";

        [Serializable] class Report
        {
            public string utc, scene, status, clip, sha256, importer;
            public float clipSeconds, baseVolume;
            public int errors;
            public List<string> findings = new List<string>();
            public List<string> labelFit = new List<string>();
            public List<string> notes = new List<string>();
            /// <summary>「掛け算と設定保持の単体検査」が何を見て何を見ていないか。</summary>
            public string unitScope = UnitScope;
            /// <summary>この検査では確かめていないこと (ClientSim / 実機で見る)。</summary>
            public List<string> notCovered = new List<string>(NotCovered);
        }

        /// <summary>振る舞いの検査の項目名の頭。**ライブの時計・Start・Update は通っていない**ことを名前で分かるようにする。</summary>
        const string U = "unit (multiply + keep settings): ";
        const string UnitScope = "掛け算と設定保持の単体検査: 別シーンで KD_TreatPouch._BgmChanged / _ToggleBgm と KD_BgmPlayer._SetBgm を呼び、"
            + "ライブの掛け金 _duck は反射で直接 0 / 1 を書く。出力 = baseVolume × 個人の音量 × 掛け金、OFF / 0% の消音、個人設定が掛け金で書き換わらないことだけを見る。"
            + "KD_BgmPlayer.Start / Update と KD_LiveSystem.IsNightWanted は通らない";
        static readonly string[] NotCovered =
        {
            "ライブ開始で BGM が下がること (KD_LiveSystem.IsNightWanted → KD_BgmPlayer.Update の掛け金)",
            "ライブ終了で戻ること (タイムラインの終わりで IsNightWanted が false)",
            "ライブ中止で戻ること (startMs を終端へ送る経路)",
            "途中入室: ライブ中に入った人が Start で最初から 0",
            "掛け金の 1 秒フェード (FadeSeconds、Update の MoveTowards)",
            "実際の出音・ループの継ぎ目・他プレイヤーに影響しないこと (同期変数 0 本はコード上の事実。実通信は未検査)",
        };

        // ================================================================ 96

        [MenuItem("Tools/KominkaHillside/Course/98 - Build BGM")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            Directory.CreateDirectory(OutDir);
            var log = new StringBuilder("[KH BGM] Build (IMP-WLD-003)\n");
            if (!File.Exists(ClipPath)) throw new InvalidOperationException(ClipPath + " missing: copy KH_Komorebi_003_Loop.wav from vrc-music-agent, then refresh");
            string sha = Sha256(ClipPath);
            if (sha != ClipSha256) throw new InvalidOperationException(ClipPath + " SHA256 " + sha + " != " + ClipSha256 + " (001/002 や master.wav と取り違えていないか)。何も変えていない");
            EnsureImport(log);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            if (clip == null) throw new InvalidOperationException("AudioClip not imported: " + ClipPath);

            var pouch = FindPouch();
            var font = pouch.sectionTitleLabel != null ? pouch.sectionTitleLabel.font : null;
            if (font == null || pouch.language == null) throw new InvalidOperationException("KD_TreatPouch font / language missing (Menu/7・11 の後に流す)");
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int dogsCount);
            if (dogs == null || dogsCount != 1 || dogs.transform.parent != null) throw new InvalidOperationException(KD_Hillside.DogsRootName + " root missing/ambiguous (" + dogsCount + ")");
            var liveGo = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName, out int liveCount);
            var live = liveGo != null && liveCount == 1 ? liveGo.GetComponent<KD_LiveSystem>() : null;
            if (live == null) throw new InvalidOperationException(KD_DogDefs.LiveSystemName + " missing/ambiguous (" + liveCount + ") — ライブ中に BGM を下げられない");

            File.Copy(KH_CourseGroundEditor.ScenePath, OutDir + "/before-bgm.unity", true);
            try
            {
                // ---- 器: KH_Dogs/KD_Bgm ----
                var go = KD_SceneObjects.Find(KD_DogDefs.BgmName, out int bgmCount);
                if (bgmCount > 1) throw new InvalidOperationException(KD_DogDefs.BgmName + " が " + bgmCount + " 個ある (手で 1 つにしてから再実行)");
                if (go == null) { go = new GameObject(KD_DogDefs.BgmName); log.AppendLine("   created " + KD_DogDefs.BgmName); }
                if (go.transform.parent != dogs.transform) { go.transform.SetParent(dogs.transform, false); log.AppendLine("   placed under " + KD_Hillside.DogsRootName); }
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                go.SetActive(true);

                // ---- 音源。**ここが「鳴らす」の全部** (Udon は Play() を呼ばない。古民家と同じ) ----
                var source = go.GetComponent<AudioSource>(); if (source == null) source = go.AddComponent<AudioSource>();
                source.enabled = true;
                source.clip = clip; source.playOnAwake = true; source.loop = true; source.mute = false;
                source.spatialBlend = 0f; source.spatialize = false; source.dopplerLevel = 0f; source.bypassReverbZones = true;
                source.priority = 128;
                // 音量の正本は KD_BgmPlayer.baseVolume (AudioSource.volume は実行時に毎フレーム作り直される)
                source.volume = BaseVolume;
                EditorUtility.SetDirty(source);

                // ---- 台帳 (SyncMode None。同期変数 0 本 = 他人の耳を変えない) ----
                var player = go.GetComponent<KD_BgmPlayer>();
                if (player == null) { player = go.AddUdonSharpComponent<KD_BgmPlayer>(); log.AppendLine("   added KD_BgmPlayer"); }
                player.source = source; player.live = live; player.sleepAudio = null; player.baseVolume = BaseVolume;
                var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(player);
                if (backing == null) throw new InvalidOperationException("KD_BgmPlayer backing missing (KD_BgmPlayer.asset を U# でコンパイルしてから再実行)");
                if (backing.SyncMethod != Networking.SyncType.None) { backing.SyncMethod = Networking.SyncType.None; EditorUtility.SetDirty(backing); }
                UdonSharpEditorUtility.CopyProxyToUdon(player); EditorUtility.SetDirty(player);

                // ---- 設定 → 音量 の BGM の行 (Menu/16 と同じ本体) + 注記の束縛 ----
                KD_ShopSetupEditor.AddGlyphs(KD_TreatSetupEditor.PouchWords(), log);
                KD_DogVoiceSettingsEditor.EnsureHillsideVolume(pouch, font, log);
                KD_LocalizationEditor.Bind(pouch.language, KD_LocalizationEditor.Load(), log);
                UdonSharpEditorUtility.CopyProxyToUdon(pouch.language); EditorUtility.SetDirty(pouch.language);
                UdonSharpEditorUtility.CopyProxyToUdon(pouch); EditorUtility.SetDirty(pouch);

                EditorSceneManager.MarkSceneDirty(go.scene);
                if (!EditorSceneManager.SaveScene(go.scene)) throw new IOException("BGM build save failed");
            }
            catch
            {
                Debug.LogError("[KH BGM] Build failed; the scene before this run is at " + OutDir + "/before-bgm.unity. Reopen the scene without saving to discard partial changes.");
                File.WriteAllText(OutDir + "/bgm-build-log.txt", log.ToString());
                throw;
            }
            File.WriteAllText(OutDir + "/bgm-build-log.txt", log.ToString());
            var r = Check(false);
            r.status = r.findings.Count == 0 ? "BUILT" : "FAIL";
            Write(r, "bgm-build.json");
            if (r.findings.Count > 0) throw new InvalidOperationException("[KH BGM] Build saved but checks failed:\n" + string.Join("\n", r.findings));
            Debug.Log("[KH BGM] BUILT " + r.clip + " " + r.clipSeconds.ToString("F1") + " s, baseVolume " + BaseVolume + ". Run Course/99 after reopening the scene.");
        }

        // ================================================================ 97

        [MenuItem("Tools/KominkaHillside/Course/99 - Verify BGM")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = Check(true);
            r.status = r.findings.Count == 0 ? "BGM_OK" : "FAIL";
            Write(r, "bgm-verify.json");
            if (r.findings.Count > 0) throw new InvalidOperationException("[KH BGM] Verify failed (" + r.findings.Count + "):\n" + string.Join("\n", r.findings));
            Debug.Log("[KH BGM] BGM_OK (" + OutDir + "/bgm-verify.json). Live link itself not verified here (unit test writes _duck directly): live start / end / cancel, late join and the 1 s fade are in notCovered for ClientSim / device.");
        }

        // ================================================================ 検査 (96 の最後と 97)

        static Report Check(bool hash)
        {
            var r = new Report { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath, baseVolume = BaseVolume };
            var e = r.findings;
            // ---- 音源と取り込み設定 ----
            if (hash) { r.sha256 = File.Exists(ClipPath) ? Sha256(ClipPath) : "(missing)"; Req(e, r.sha256 == ClipSha256, "clip SHA256 = Issue value (" + r.sha256 + ")"); }
            else r.sha256 = "(checked before build)";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            Req(e, clip != null, "clip imported " + ClipPath);
            if (clip != null)
            {
                r.clip = clip.name; r.clipSeconds = clip.length;
                Req(e, clip.channels == ClipChannels && clip.frequency == ClipFrequency, "clip stereo 48 kHz (channels " + clip.channels + ", " + clip.frequency + " Hz)");
                // 圧縮後もフレーム数が元と同じ = ループの継ぎ目がずれない (違えば詰め物が入っている。聞こえ方は実機で確かめる)
                Req(e, clip.samples == ClipSamples, "clip keeps 10,368,000 frames after import (samples " + clip.samples + "; a difference moves the loop seam)");
                Req(e, clip.loadType == AudioClipLoadType.Streaming, "clip load type Streaming (" + clip.loadType + ")");
            }
            r.importer = ImporterSummary(e);

            // ---- 器 ----
            var go = KD_SceneObjects.Find(KD_DogDefs.BgmName, out int count);
            Req(e, go != null && count == 1, KD_DogDefs.BgmName + " exactly once (" + count + ")");
            if (go == null) return r;
            Req(e, go.transform.parent != null && go.transform.parent.name == KD_Hillside.DogsRootName && go.transform.parent.parent == null, "placed at " + KD_Hillside.DogsRootName + "/" + KD_DogDefs.BgmName);
            Req(e, go.activeInHierarchy, "active (inactive = silent)");
            var source = go.GetComponent<AudioSource>();
            var player = go.GetComponent<KD_BgmPlayer>();
            Req(e, source != null && player != null, "AudioSource + KD_BgmPlayer");
            if (source == null || player == null) return r;
            Req(e, source.enabled && source.clip == clip, "source enabled, clip = KH_Komorebi_003_Loop");
            Req(e, source.playOnAwake && source.loop && !source.mute, "PlayOnAwake + loop, not muted in the saved scene");
            Req(e, source.spatialBlend == 0f && !source.spatialize && source.dopplerLevel == 0f && source.bypassReverbZones, "2D (spatialBlend 0, no spatialize / doppler, bypass reverb zones)");
            Req(e, source.outputAudioMixerGroup == null, "no mixer group");
            Req(e, Mathf.Approximately(player.baseVolume, BaseVolume) && Mathf.Approximately(source.volume, BaseVolume), "baseVolume " + player.baseVolume + " / source.volume " + source.volume + " = " + BaseVolume);
            var liveGo = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName, out int liveCount);
            var live = liveGo != null && liveCount == 1 ? liveGo.GetComponent<KD_LiveSystem>() : null;
            Req(e, player.source == source && live != null && player.live == live && player.sleepAudio == null, "player.source / live = KD_LiveSystem / sleepAudio null");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(player);
            Req(e, backing != null && backing.SyncMethod == Networking.SyncType.None, "KD_BgmPlayer SyncMode None");
            Req(e, BackingIs(backing, "source", source), "backing source");
            Req(e, BackingIs(backing, "live", live) || BackingIs(backing, "live", live != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(live) : null), "backing live");
            Req(e, backing != null && backing.publicVariables.TryGetVariableValue("baseVolume", out object bv) && bv is float f && Mathf.Approximately(f, BaseVolume), "backing baseVolume");
            Req(e, UnityEngine.Object.FindObjectsOfType<KD_BgmPlayer>(true).Length == 1, "exactly one KD_BgmPlayer in the scene");

            // ---- 設定 → 音量 の BGM の行 ----
            KD_TreatPouch pouch = null;
            try { pouch = FindPouch(); } catch (InvalidOperationException ex) { e.Add("FAIL " + ex.Message); return r; }
            var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            var page = pouch.volumeSettingsPage;
            Req(e, page != null && page.name == "VolumeSettings" && pouch.settingsPage != null && page.transform.parent == pouch.settingsPage.transform && !page.activeSelf, "volume page Page_Settings/VolumeSettings (hidden)");
            Req(e, pb != null, "KD_TreatPouch backing UdonBehaviour exists");
            // 期待する実体は「音量頁の BGM の行の部品」と「KH_Dogs/KD_Bgm の KD_BgmPlayer」から引く (pouch の欄から引かない)。
            // proxy と backing (Udon 側。実行時に読むのはこちら) の両方がそれと**同一**であることを比べる — 非 null だけだと、
            // 例えば backing の bgmSlider が犬の声のスライダーを指していても通ってしまう
            Transform pageT = page != null ? page.transform : null;
            var wantSlider = pageT != null ? pageT.Find("BgmSlider")?.GetComponent<Slider>() : null;
            var wantValue = pageT != null ? pageT.Find("BgmValue")?.GetComponent<TextMeshProUGUI>() : null;
            var toggle = pageT != null ? pageT.Find(KD_DogVoiceSettingsEditor.HillsideBgmToggleName) : null;
            var wantToggleImage = toggle != null ? toggle.GetComponent<Image>() : null;
            var wantToggleLabel = toggle != null ? toggle.Find("Label")?.GetComponent<TextMeshProUGUI>() : null;
            var toggleButton = toggle != null ? toggle.GetComponent<Button>() : null;
            bool playerAtHome = go.transform.parent != null && go.transform.parent.name == KD_Hillside.DogsRootName && go.transform.parent.parent == null;
            Same(e, "bgmPlayer = KH_Dogs/KD_Bgm KD_BgmPlayer", pouch.bgmPlayer, pb, "bgmPlayer", playerAtHome ? player : null, backing);
            Same(e, "bgmSlider = VolumeSettings/BgmSlider", pouch.bgmSlider, pb, "bgmSlider", wantSlider, null);
            Same(e, "bgmValueLabel = VolumeSettings/BgmValue", pouch.bgmValueLabel, pb, "bgmValueLabel", wantValue, null);
            Same(e, "bgmToggleImage = VolumeSettings/BgmToggle (Image)", pouch.bgmToggleImage, pb, "bgmToggleImage", wantToggleImage, null);
            Same(e, "bgmToggleLabel = VolumeSettings/BgmToggle/Label", pouch.bgmToggleLabel, pb, "bgmToggleLabel", wantToggleLabel, null);
            // 犬の声の行と取り違えていないこと (上の同一性で足りるが、壊れ方を名指しで出す)
            Req(e, wantSlider == null || !ReferenceEquals(wantSlider, pouch.dogVoiceSlider), "BGM slider is not the dog voice slider");
            Req(e, wantSlider != null && wantSlider.minValue == 0f && wantSlider.maxValue == 1f && !wantSlider.wholeNumbers && Mathf.Approximately(wantSlider.value, .5f), "BGM slider 0..1 continuous, default 50%");
            if (wantSlider != null)
            {
                // イベントの宛先も実体で比べる: この 1 本の手元メニューの backing UdonBehaviour の SendCustomEvent("_BgmChanged") が 1 件だけ
                Req(e, EventIs(wantSlider, "m_OnValueChanged", wantSlider.onValueChanged, pb, "_BgmChanged"), "BGM slider event -> KD_TreatPouch backing SendCustomEvent(_BgmChanged), exactly one");
                // つまみのアンカーは Slider が駆動する値で、シーンには保存されない (読み込み直後は 0,0 で高さ 0、OnEnable の UpdateVisuals で縦いっぱいに戻る)。
                // 実行時の高さを決めるのは HandleArea の高さなので、そちらを見る。
                var handleArea = wantSlider.handleRect != null ? wantSlider.handleRect.parent as RectTransform : null;
                Req(e, handleArea != null && Mathf.Approximately(handleArea.rect.height, 52f) && Mathf.Approximately(wantSlider.handleRect.sizeDelta.y, 0f), "BGM slider thumb 52 tall (HandleArea 52, thumb stretches)");
            }
            Req(e, wantValue != null && wantValue.text == "50%", "BGM value label 50%");
            Req(e, toggleButton != null && toggleButton.interactable && EventIs(toggleButton, "m_OnClick", toggleButton.onClick, pb, "_ToggleBgm"),
                "BGM [ON/OFF] event -> KD_TreatPouch backing SendCustomEvent(_ToggleBgm), exactly one");
            Req(e, wantToggleLabel != null && wantToggleLabel.text == KD_TreatPouch.WordVoiceOn, "BGM [ON/OFF] label ON");
            Req(e, pouch.dogVoiceSlider != null && page != null && pouch.dogVoiceSlider.transform.parent == page.transform, "dog voice row kept");
            Req(e, pouch.liveVolumeSlider == null && pouch.bedroomBgmVolumeSlider == null && pouch.sleepAudio == null, "no live / bedroom rows (not in this world)");
            var language = pouch.language;
            var note = page != null ? page.transform.Find(KD_DogVoiceSettingsEditor.HillsideBgmNoteName)?.GetComponent<TMP_Text>() : null;
            int at = language == null || note == null ? -1 : Array.IndexOf(language.fixedLabels, note);
            Req(e, at >= 0 && language.fixedKeys[at] == KD_DogVoiceSettingsEditor.HillsideBgmNoteKey, "BGM note bound to " + KD_DogVoiceSettingsEditor.HillsideBgmNoteKey);
            try { KD_LocalizationEditor.ValidateScene(language, KD_LocalizationEditor.Load()); }
            catch (InvalidOperationException ex) { e.Add("FAIL localization: " + ex.Message); }
            if (page != null) CheckLayout(page.transform, e);
            if (language != null && page != null) FitLabels(pouch, page.transform, note, r);

            // ---- 掛け算と設定保持の単体検査 (別シーンで実物のコードを呼ぶ。掛け金 _duck は直に書く = ライブの時計・Start・Update は通らない。
            //      通っていないものは r.notCovered に出す) ----
            UnitMultiplyAndKeep(e);
            r.notes.Add("保存・復元: 古民家と同じく無し (KD_TreatPouch._bgmEnabled = true / _bgmVolume = 0.5 の初期値、PlayerData 不使用)。再入室で ON・50% に戻る");
            r.notes.Add("ライブ (コード上の事実。この検査では未検証 = notCovered): KD_BgmPlayer.Update が KD_LiveSystem.IsNightWanted (夜のフェードインの頭〜タイムラインの終わり、同期時計の純関数) で掛け金を 1 s かけて 0 へ。途中入室は Start で最初から 0。中止は startMs を終端へ送るので同じ式で戻る");
            r.errors = e.Count;
            return r;
        }

        /// <summary>掛け算と設定保持の単体検査 (<see cref="UnitScope"/>)。ライブとの連動そのものは見ない (<see cref="NotCovered"/>)。</summary>
        static void UnitMultiplyAndKeep(List<string> e)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var host = new GameObject("BgmTest"); SceneManager.MoveGameObjectToScene(host, preview);
                var player = host.AddUdonSharpComponent<KD_BgmPlayer>();
                var src = host.AddComponent<AudioSource>();
                player.source = src; player.baseVolume = .4f;
                var pouchHost = new GameObject("PouchTest"); SceneManager.MoveGameObjectToScene(pouchHost, preview);
                var pouch = pouchHost.AddUdonSharpComponent<KD_TreatPouch>();
                var sliderHost = new GameObject("BgmSliderTest", typeof(RectTransform), typeof(Slider)); SceneManager.MoveGameObjectToScene(sliderHost, preview);
                var slider = sliderHost.GetComponent<Slider>(); slider.minValue = 0f; slider.maxValue = 1f;
                pouch.bgmSlider = slider; pouch.bgmPlayer = player;
                var duck = typeof(KD_BgmPlayer).GetField("_duck", BindingFlags.Instance | BindingFlags.NonPublic);
                if (duck == null) { e.Add("FAIL " + U + "KD_BgmPlayer._duck not found (code changed?)"); return; }
                void Set(float v) { slider.value = v; pouch._BgmChanged(); }
                void Reapply() { player._SetBgm(player.UserVolume, player.UserEnabled); }

                Set(1f); Req(e, Mathf.Approximately(src.volume, .4f) && !src.mute, U+"100% = base volume");
                Set(.5f); Req(e, Mathf.Approximately(src.volume, .2f) && !src.mute, U+"50% = half");
                Set(0f); Req(e, src.mute, U+"0% is silent (muted)");
                Set(.6f); pouch._ToggleBgm();
                Req(e, src.mute && !player.UserEnabled && Mathf.Approximately(player.UserVolume, .6f), U+"OFF mutes and keeps the chosen 60%");
                Set(.3f); Req(e, src.mute && Mathf.Approximately(player.UserVolume, .3f), U+"slider while OFF stays muted, value remembered (30%)");
                pouch._ToggleBgm(); Req(e, !src.mute && player.UserEnabled && Mathf.Approximately(src.volume, .12f), U+"ON restores the value chosen while OFF (30%)");
                duck.SetValue(player, 0f); Reapply();
                Req(e, Mathf.Approximately(src.volume, 0f) && player.UserEnabled && Mathf.Approximately(player.UserVolume, .3f), U+"gain 0 (_duck written directly): output 0, personal settings untouched");
                Set(.8f); Req(e, Mathf.Approximately(src.volume, 0f) && Mathf.Approximately(player.UserVolume, .8f), U+"change while gain 0 stays silent, value remembered (80%)");
                duck.SetValue(player, 1f); Reapply();
                Req(e, Mathf.Approximately(src.volume, .32f) && !src.mute, U+"gain back to 1: the current personal setting (80%), not the earlier one");
                pouch._ToggleBgm(); duck.SetValue(player, 0f); Reapply(); duck.SetValue(player, 1f); Reapply();
                Req(e, src.mute && !player.UserEnabled, U+"OFF before gain 0 stays OFF after gain 1 (no restore step)");
                duck.SetValue(player, 0f); pouch._ToggleBgm(); duck.SetValue(player, 1f); Reapply();
                Req(e, !src.mute && Mathf.Approximately(src.volume, .32f), U+"turned ON while gain 0 -> plays at 80% after gain 1");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        /// <summary>頁の部品が [設定へ戻る] の上〜頁の上端に収まり (y -218..130、x ±380)、押せる部品どうしが重ならない。</summary>
        static void CheckLayout(Transform page, List<string> e)
        {
            var hits = new List<RectTransform>();
            foreach (Transform child in page)
            {
                if (!child.gameObject.activeSelf || child.GetComponent<Graphic>() == null && child.GetComponent<Selectable>() == null) continue;
                var rt = (RectTransform)child;
                Rect rc = RectOf(rt);
                Req(e, rc.yMax <= 130f && rc.yMin >= -218f && rc.xMin >= -380f && rc.xMax <= 380f, "volume page item inside the page: " + child.name + " " + rc);
                if (child.GetComponent<Selectable>() != null) hits.Add(rt);
            }
            for (int a = 0; a < hits.Count; a++)
                for (int b = a + 1; b < hits.Count; b++)
                    if (RectOf(hits[a]).Overlaps(RectOf(hits[b]))) e.Add("FAIL volume page controls overlap: " + hits[a].name + " / " + hits[b].name);
            Req(e, hits.Count == 4, "volume page controls = BGM slider, BGM toggle, dog voice slider, back (" + hits.Count + ")");
        }

        static void FitLabels(KD_TreatPouch pouch, Transform page, TMP_Text note, Report r)
        {
            var go = new GameObject("BgmMeasure", typeof(RectTransform)); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sample = go.AddComponent<TextMeshProUGUI>();
                var scope = page.Find("VolumeScope")?.GetComponent<TMP_Text>();
                foreach (string code in pouch.language.languageCodes)
                {
                    Fit(sample, note, pouch.language._TextForLanguage(KD_DogVoiceSettingsEditor.HillsideBgmNoteKey, code), code + " bgm note", r);
                    Fit(sample, scope, pouch.language._TextForLanguage(KD_DogVoiceSettingsEditor.HillsideScopeKey, code), code + " volume scope", r);
                }
                Fit(sample, pouch.bgmToggleLabel, KD_TreatPouch.WordVoiceOff, "toggle OFF", r);
                Fit(sample, pouch.bgmValueLabel, "100%", "value 100%", r);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void Fit(TextMeshProUGUI sample, TMP_Text target, string text, string name, Report r)
        {
            if (target == null) { r.findings.Add("FAIL label missing: " + name); return; }
            int result = KD_LocalizationEditor.MeasureLabelFit(sample, target, text, out Vector2 preferred);
            r.labelFit.Add(name + " preferred=" + preferred + " rect=" + target.rectTransform.rect.size + " fit=" + result);
            if (result == 2) r.findings.Add("Label overflow: " + name);
        }

        // ================================================================ 取り込み設定

        static void EnsureImport(StringBuilder log)
        {
            var imp = AssetImporter.GetAtPath(ClipPath) as AudioImporter;
            if (imp == null) { AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceSynchronousImport); imp = AssetImporter.GetAtPath(ClipPath) as AudioImporter; }
            if (imp == null) throw new InvalidOperationException("AudioImporter missing: " + ClipPath + " (refresh first)");
            bool changed = false;
            if (imp.forceToMono) { imp.forceToMono = false; changed = true; }
            if (imp.ambisonic) { imp.ambisonic = false; changed = true; }
            var def = imp.defaultSampleSettings;
            if (!Matches(def, PcQuality)) { imp.defaultSampleSettings = Wanted(def, PcQuality); changed = true; }
            if (!imp.ContainsSampleSettingsOverride("Standalone") || !Matches(imp.GetOverrideSampleSettings("Standalone"), PcQuality))
            { imp.SetOverrideSampleSettings("Standalone", Wanted(imp.GetOverrideSampleSettings("Standalone"), PcQuality)); changed = true; }
            foreach (var platform in MobilePlatforms)
                if (!imp.ContainsSampleSettingsOverride(platform) || !Matches(imp.GetOverrideSampleSettings(platform), MobileQuality))
                { imp.SetOverrideSampleSettings(platform, Wanted(imp.GetOverrideSampleSettings(platform), MobileQuality)); changed = true; }
            if (changed) { imp.SaveAndReimport(); log.AppendLine("   import " + ClipPath + " -> Vorbis Streaming, PC " + PcQuality + " / Android・iOS " + MobileQuality + " (reimported)"); }
            else log.AppendLine("   import " + ClipPath + " unchanged");
        }

        static AudioImporterSampleSettings Wanted(AudioImporterSampleSettings s, float quality)
        {
            s.loadType = AudioClipLoadType.Streaming;
            s.preloadAudioData = false;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = quality;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            return s;
        }

        static bool Matches(AudioImporterSampleSettings s, float quality)
        {
            return s.loadType == AudioClipLoadType.Streaming && !s.preloadAudioData && s.compressionFormat == AudioCompressionFormat.Vorbis
                && Mathf.Abs(s.quality - quality) < .001f && s.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate;
        }

        static string ImporterSummary(List<string> e)
        {
            var imp = AssetImporter.GetAtPath(ClipPath) as AudioImporter;
            if (imp == null) { e.Add("FAIL AudioImporter missing"); return "(none)"; }
            var sb = new StringBuilder("default " + Describe(imp.defaultSampleSettings));
            Req(e, Matches(imp.defaultSampleSettings, PcQuality), "import default Vorbis " + PcQuality + " Streaming (no preload) preserve rate");
            Req(e, !imp.forceToMono && !imp.ambisonic, "import keeps stereo (no force-to-mono, not ambisonic)");
            foreach (var (platform, q) in new[] { ("Standalone", PcQuality), ("Android", MobileQuality), ("iOS", MobileQuality) })
            {
                bool has = imp.ContainsSampleSettingsOverride(platform);
                var s = imp.GetOverrideSampleSettings(platform);
                Req(e, has && Matches(s, q), "import override " + platform + " Vorbis " + q + " Streaming");
                sb.Append("; " + platform + " " + (has ? Describe(s) : "(no override)"));
            }
            return sb.ToString();
        }

        static string Describe(AudioImporterSampleSettings s)
        {
            return s.compressionFormat + " q" + s.quality.ToString("0.##") + " " + s.loadType + (s.preloadAudioData ? " preload" : "") + " " + s.sampleRateSetting;
        }

        // ================================================================ 部品

        static KD_TreatPouch FindPouch()
        {
            var all = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (all.Length != 1) throw new InvalidOperationException("Exactly one KD_TreatPouch required (" + all.Length + ")");
            var p = all[0];
            if (p.settingsPage == null || p.settingsHome == null || p.sectionTitleLabel == null) throw new InvalidOperationException("Settings page missing (Menu/7 Build Menu)");
            return p;
        }

        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var bytes = sha.ComputeHash(stream);
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static bool BackingIs(VRC.Udon.UdonBehaviour backing, string field, UnityEngine.Object want)
        {
            if (backing == null || want == null) return false;
            return backing.publicVariables.TryGetVariableValue(field, out object v) && v as UnityEngine.Object == want;
        }

        /// <summary>proxy の欄と backing (Udon 側) の変数が、どちらも期待する実体と同一か。
        /// backing の Udon 参照は proxy のまま入る場合と UdonBehaviour で入る場合があるので、<paramref name="expectedBackingAlt"/> も受ける。</summary>
        static void Same(List<string> e, string what, UnityEngine.Object proxyValue, VRC.Udon.UdonBehaviour backing, string field, UnityEngine.Object expected, UnityEngine.Object expectedBackingAlt)
        {
            if (expected == null) { e.Add("FAIL " + what + ": expected object missing in the scene"); return; }
            Req(e, ReferenceEquals(proxyValue, expected), what + " (proxy is " + Describe(proxyValue) + ")");
            object v = null;
            bool found = backing != null && backing.publicVariables.TryGetVariableValue(field, out v);
            bool same = found && (ReferenceEquals(v, expected) || (expectedBackingAlt != null && ReferenceEquals(v, expectedBackingAlt)));
            Req(e, same, what + " (backing is " + (!found ? "(no variable)" : Describe(v as UnityEngine.Object)) + ")");
        }

        /// <summary>永続イベントが 1 件だけで、宛先がその UdonBehaviour の実体・メソッドが SendCustomEvent・引数が <paramref name="evt"/>。
        /// 宛先 null 同士の一致で通らないよう、target が null なら常に false。</summary>
        static bool EventIs(UnityEngine.Object owner, string eventProperty, UnityEngine.Events.UnityEventBase ev, VRC.Udon.UdonBehaviour target, string evt)
        {
            if (owner == null || ev == null || target == null) return false;
            if (ev.GetPersistentEventCount() != 1 || !ReferenceEquals(ev.GetPersistentTarget(0), target) || ev.GetPersistentMethodName(0) != "SendCustomEvent") return false;
            var calls = new SerializedObject(owner).FindProperty(eventProperty + ".m_PersistentCalls.m_Calls");
            return calls != null && calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == evt;
        }

        static string Describe(UnityEngine.Object o)
        {
            if (o == null) return "null";
            var c = o as Component;
            return c != null ? KD_SceneObjects.PathOf(c.gameObject) + " (" + c.GetType().Name + ")" : o.name;
        }

        static Rect RectOf(RectTransform t)
        {
            var size = t.sizeDelta; var pos = t.anchoredPosition;
            return new Rect(pos.x - size.x * t.pivot.x, pos.y - size.y * t.pivot.y, size.x, size.y);
        }

        static void Req(List<string> e, bool ok, string what) { if (!ok) e.Add("FAIL " + what); }

        static void Write(Report r, string name)
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/" + name, JsonUtility.ToJson(r, true));
        }
    }
}
