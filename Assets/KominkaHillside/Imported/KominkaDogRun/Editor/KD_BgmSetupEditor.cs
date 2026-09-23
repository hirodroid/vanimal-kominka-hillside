using System;
using System.IO;
using System.Reflection;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// IMP-ENV-009: **BGM の器を作る / 数える。** 設計の正本は
    /// [IMP-ENV-009](../../../docs/development/issues/IMP-ENV-009-bgm.md)。
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    /// <code>
    ///   1 - Create Program Asset  → force refresh (コンパイル完了まで待つ)。初回だけ
    ///   2 - Setup BGM             音源を作って配線する (2D / loop / PlayOnAwake / Streaming)
    ///   3 - Verify BGM
    /// </code>
    ///
    /// シーンに置くもの:
    /// <code>
    /// KD_Systems/
    ///   KD_Bgm    AudioSource (2D / loop / PlayOnAwake) + KD_BgmPlayer (None)
    /// </code>
    ///
    /// **鳴らすのは AudioSource の PlayOnAwake で、Udon は `Play()` を呼ばない** —
    /// 入室した瞬間から鳴っていてほしいので Udon の初期化を待たせない。
    /// <see cref="KD_BgmPlayer"/> は音量と消音を毎フレーム作り直すだけ。
    ///
    /// 手元メニューとの配線は**両方向から張る** (ここで手元メニューを引き直し、
    /// 手元メニューの Setup でも BGM を引き直す) — どちらの順で流しても揃うようにする。
    /// </summary>
    public static class KD_BgmSetupEditor
    {
        public const string ScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BgmPlayer.cs";
        public const string ProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BgmPlayer.asset";

        /// <summary>BGM の素材置き場。**この中の AudioClip 1 本を使う** (曲を差し替えたら 2 を流す)。</summary>
        public const string ClipFolder = "Assets/KominkaHillside/Imported/KominkaDogRun/Data/BGM";

        /// <summary>読み込み方式は Streaming (ライブの曲と同じ。`DecompressOnLoad` はモバイルで効いてくる)。
        /// **既定だけでなく override も同じにする** (既存 override が残っていると既定を直しても効かない)。</summary>
        public static readonly string[] AudioOverridePlatforms = { "Standalone", "Android", "iOS" };

        const string LogFile = "Logs/imp-env-009-bgm.txt";

        // ================================================================ 1

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/BGM/1 - Create Program Asset")]
        public static void CreateProgramAsset()
        {
            var log = new StringBuilder("[KD_BgmSetup] Create program asset\n");
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) != null)
                log.AppendLine("already exists: " + ProgramPath);
            else
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptPath);
                if (script == null) log.AppendLine("FAIL script not found: " + ScriptPath);
                else
                {
                    var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    asset.sourceCsScript = script;
                    AssetDatabase.CreateAsset(asset, ProgramPath);
                    AssetDatabase.SaveAssets();
                    log.AppendLine("created: " + ProgramPath);
                }
            }
            log.AppendLine("次: force refresh (コンパイル完了まで待つ) してから 2 - Setup BGM");
            Finish(log);
        }

        // ================================================================ 2

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/BGM/2 - Setup BGM")]
        public static void Setup()
        {
            var log = new StringBuilder("[KD_BgmSetup] Setup\n");
            bool ok = true;

            // ---- 曲 ----
            var clip = FindClip(out string clipPath, log);
            if (clip == null) { log.AppendLine("SETUP RESULT: FAIL (曲が無い)"); Finish(log); return; }
            ok &= EnsureStreaming(clipPath, log);

            // ---- 器 ----
            int found;
            var go = KD_SceneObjects.Find(KD_DogDefs.BgmName, out found);
            if (found > 1)
            {
                log.AppendLine("FAIL '" + KD_DogDefs.BgmName + "' がシーン内に " + found + " 個ある (手で解消する)");
                Finish(log);
                return;
            }
            if (go == null)
            {
                go = new GameObject(KD_DogDefs.BgmName);
                Undo.RegisterCreatedObjectUndo(go, "Create " + KD_DogDefs.BgmName);
                KD_SceneObjects.PlaceIfNew(go, KD_DogDefs.SystemsPath);
                log.AppendLine("created " + KD_DogDefs.BgmName);
            }

            // ---- 音源。**ここが「鳴らす」の全部** (Udon は Play() を呼ばない) ----
            var source = go.GetComponent<AudioSource>();
            if (source == null) source = Undo.AddComponent<AudioSource>(go);
            Undo.RecordObject(source, "bgm source");
            source.clip = clip;
            source.playOnAwake = true;
            source.loop = true;
            source.spatialBlend = 0f;      // 2D — 庭でも家の中でも同じに聞こえる
            source.spatialize = false;
            source.dopplerLevel = 0f;
            source.bypassReverbZones = true;
            EditorUtility.SetDirty(source);

            // ---- 台帳 (None) ----
            var player = go.GetUdonSharpComponent<KD_BgmPlayer>();
            if (player == null)
            {
                player = go.AddUdonSharpComponent<KD_BgmPlayer>();
                log.AppendLine("   added KD_BgmPlayer");
            }
            if (player.baseVolume <= 0f) player.baseVolume = KD_BgmPlayer.DefaultBaseVolume;
            player.source = source;
            // **音量の正本は KD_BgmPlayer.baseVolume** (AudioSource.volume は毎フレーム作り直される)
            source.volume = player.baseVolume;

            var liveGo = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName, out found);
            var live = liveGo != null ? liveGo.GetUdonSharpComponent<KD_LiveSystem>() : null;
            if (live == null || found != 1)
            {
                log.AppendLine("   FAIL " + KD_DogDefs.LiveSystemName + " が取れない (" + found
                    + " 個) - ライブ中に BGM が下がらない");
                ok = false;
            }
            player.live = live;
            KD_SleepAudioSetupEditor.WireBgmIfPresent(player);
            ok &= TryCopy(player, log);
            ok &= EnsureSync(player, KD_DogDefs.BgmName, VRC.SDKBase.Networking.SyncType.None, log);
            log.AppendLine("   clip=" + clip.name + " length=" + clip.length.ToString("F1") + "s"
                + " baseVolume=" + player.baseVolume.ToString("F2") + " live=" + (live != null));

            // ---- 手元メニューの [設定] へ渡す (両方向から張る) ----
            KD_TreatPouch pouch = null;
            foreach (var p in Resources.FindObjectsOfTypeAll<KD_TreatPouch>())
                if (p.gameObject.scene == go.scene) pouch = p;
            if (pouch == null)
            {
                log.AppendLine("   FAIL 手元メニュー (KD_TreatPouch) が取れない - 設定から操作できない");
                ok = false;
            }
            else
            {
                pouch.bgmPlayer = player;
                ok &= TryCopy(pouch, log);
                log.AppendLine("   手元メニューへ配線した (設定の BGM の節が効く)");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "OK" : "FAIL"));
            log.AppendLine("次: 3 - Verify BGM");
            Finish(log);
        }

        // ================================================================ 3

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/BGM/3 - Verify BGM")]
        public static void Verify()
        {
            var scene = SceneManager.GetActiveScene();
            var log = new StringBuilder("PROJECT " + Application.dataPath + "\n[KD_BgmSetup] Verify\n");
            int fail = 0;
            Action<bool, string> check = (cond, label) =>
            {
                log.AppendLine((cond ? "PASS " : "FAIL ") + label);
                if (!cond) fail++;
            };

            int found;
            var go = KD_SceneObjects.Find(KD_DogDefs.BgmName, out found);
            check(go != null && found == 1, KD_DogDefs.BgmName + " が 1 個 (actual " + found + ")");
            if (go == null) { Finish(log); throw new InvalidOperationException("KD_Bgm が無い。2 - Setup BGM を流す"); }
            check(go.transform.parent != null && KD_SceneObjects.PathOf(go.transform.parent.gameObject) == KD_DogDefs.SystemsPath,
                "置き場所が " + KD_DogDefs.SystemsPath + " (actual "
                + (go.transform.parent != null ? KD_SceneObjects.PathOf(go.transform.parent.gameObject) : "(シーン直下)") + ")");
            check(go.activeInHierarchy, "有効 (無効だと鳴らない)");

            var source = go.GetComponent<AudioSource>();
            check(source != null, "AudioSource が居る");
            var player = go.GetUdonSharpComponent<KD_BgmPlayer>();
            check(player != null, "KD_BgmPlayer が居る");
            if (source == null || player == null) { Finish(log); throw new InvalidOperationException("BGM の器が揃っていない"); }

            check(source.clip != null, "曲が刺さっている (" + (source.clip != null ? source.clip.name : "null") + ")");
            check(source.playOnAwake, "PlayOnAwake (入室した瞬間から鳴る)");
            check(source.loop, "loop");
            check(source.spatialBlend == 0f, "2D (spatialBlend " + source.spatialBlend + ")");
            check(player.baseVolume > 0f, "baseVolume > 0 (actual " + player.baseVolume.ToString("F2") + ")");
            check(Mathf.Approximately(source.volume, player.baseVolume),
                "AudioSource.volume が baseVolume と一致 (" + source.volume.ToString("F2") + ")");
            check(player.source == source, "source が自分の AudioSource");
            check(player.live != null, "live が配線されている (ライブ中に下がる)");

            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(player);
            check(backing != null && backing.SyncMethod == VRC.SDKBase.Networking.SyncType.None, "SyncMode None (同期変数 0 本)");
            check(BackingMatches(backing, "source", source), "backing Udon の source が proxy と一致");
            check(player.live == null || BackingMatches(backing, "live", player.live)
                || BackingMatches(backing, "live", UdonSharpEditorUtility.GetBackingUdonBehaviour(player.live)),
                "backing Udon の live が proxy と一致");

            if (source.clip != null)
            {
                string path = AssetDatabase.GetAssetPath(source.clip);
                check(IsStreaming(path, out string detail), "読み込みが Streaming (" + detail + ")");
            }

            // ---- 手元メニューの [設定] の BGM の節 ----
            KD_TreatPouch pouch = null;
            foreach (var p in Resources.FindObjectsOfTypeAll<KD_TreatPouch>())
                if (p.gameObject.scene == scene) pouch = p;
            check(pouch != null, "手元メニューが居る");
            if (pouch != null)
            {
                check(pouch.bgmPlayer == player, "手元メニューが BGM を指している");
                var pouchBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
                check(BackingMatches(pouchBacking, "bgmPlayer", player)
                    || BackingMatches(pouchBacking, "bgmPlayer", backing), "backing Udon の bgmPlayer が一致");
                check(pouch.bgmValueLabel != null && pouch.bedroomBgmVolumeSlider != null && pouch.bedroomBgmVolumeValueLabel != null,
                    "外・寝室の BGM 表示 が配線されている");
                var slider = pouch.bgmSlider;
                check(slider != null && slider.minValue == 0f && slider.maxValue == 1f
                    && !slider.wholeNumbers && slider.value == 0.5f, "連続スライダーの既定が 50%");
                if (slider != null)
                {
                    var so = new SerializedObject(slider);
                    var calls = so.FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
                    check(calls.arraySize == 1
                        && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue
                            == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch)
                        && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue
                            == "_BgmChanged", "スライダーが自分の Udon を 1 回だけ叩く");
                    check(slider.handleRect != null && Mathf.Approximately(slider.handleRect.rect.height, 52f),
                        "つまみが 52 のまま (actual " + (slider.handleRect != null ? slider.handleRect.rect.height : -1f) + ")");
                }
                // **タブ帯と案内に食い込んでいないこと。** 設定を開いてもタブは出したままなので
                // (押せば戻る)、上は タブ帯の下端 132、下は [T で開く・閉じる] の上端 -221 が壁になる。
                // ここを緩めると「文字が重なった板」が黙って出荷される
                if (pouch.settingsPage != null)
                    foreach (RectTransform r in pouch.settingsPage.GetComponentsInChildren<RectTransform>(true))
                    {
                        // IMP-ENV-011: hub/page containers have no graphic. Check visible rows in each page.
                        if (r.parent != pouch.settingsPage.transform
                            && (pouch.settingsHome == null || r.parent != pouch.settingsHome.transform)
                            && (pouch.volumeSettingsPage == null || r.parent != pouch.volumeSettingsPage.transform)) continue;
                        if (r.GetComponent<UnityEngine.UI.Graphic>() == null) continue;
                        check(r.anchoredPosition.y + r.rect.height * 0.5f <= 130f
                            && r.anchoredPosition.y - r.rect.height * 0.5f >= -218f,
                            "設定の行がタブ帯と案内の間に収まる (" + r.name + " y=" + r.anchoredPosition.y + " h=" + r.rect.height + ")");
                    }
            }

            log.AppendLine("SCENE CHECKS DONE fail=" + fail);
            Finish(log);

            // ---- 掛け算の振る舞い (別シーンで実物のコードを呼ぶ) ----
            //
            // **ライブの掛け金は Update が同期時計から作る**ので、ここでは掛け金を直に入れて
            // 「設定を書き換えずに出力だけが変わる」ことを見る (時計そのものは Play で見る)。
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var host = new GameObject("BgmTest");
                SceneManager.MoveGameObjectToScene(host, preview);
                var testPlayer = host.AddUdonSharpComponent<KD_BgmPlayer>();
                var testSource = host.AddComponent<AudioSource>();
                testPlayer.source = testSource;
                testPlayer.baseVolume = 0.4f;
                var duck = typeof(KD_BgmPlayer).GetField("_duck", BindingFlags.Instance | BindingFlags.NonPublic);

                testPlayer._SetBgm(0.5f, true);
                check(Mathf.Approximately(testSource.volume, 0.2f) && !testSource.mute, "音量 50% = 素の音量の半分");

                duck.SetValue(testPlayer, 0f);
                testPlayer._SetBgm(0.5f, true);
                check(Mathf.Approximately(testSource.volume, 0f), "ライブ中は 0 になる");
                check(Mathf.Approximately(testPlayer.UserVolume, 0.5f) && testPlayer.UserEnabled,
                    "ライブ中もプレイヤーの設定は書き換わらない");

                duck.SetValue(testPlayer, 1f);
                testPlayer._SetBgm(testPlayer.UserVolume, testPlayer.UserEnabled);
                check(Mathf.Approximately(testSource.volume, 0.2f), "ライブが終われば元の音量へ戻る");

                // **元がオフなら、ライブを一周してもオフのまま** (復元処理を書かずに成り立つこと)
                testPlayer._SetBgm(0.5f, false);
                check(testSource.mute, "OFF で消える");
                duck.SetValue(testPlayer, 0f);
                testPlayer._SetBgm(testPlayer.UserVolume, testPlayer.UserEnabled);
                duck.SetValue(testPlayer, 1f);
                testPlayer._SetBgm(testPlayer.UserVolume, testPlayer.UserEnabled);
                check(testSource.mute && !testPlayer.UserEnabled, "元がオフならライブ後もオフのまま");

                testPlayer._SetBgm(0.5f, true);
                check(!testSource.mute, "ON で戻る");

                // live が未配線でも落ちない (掛け金が動かないだけ)
                testPlayer.live = null;
                testPlayer._SetBgm(1f, true);
                check(Mathf.Approximately(testSource.volume, 0.4f), "live 未配線でも設定は効く");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); Finish(log); }

            log.AppendLine("RESULT fail=" + fail);
            Finish(log);
            if (fail > 0) throw new InvalidOperationException("BGM verification failed: " + fail);
        }

        // ================================================================ 部品

        static AudioClip FindClip(out string path, StringBuilder log)
        {
            path = null;
            if (!AssetDatabase.IsValidFolder(ClipFolder))
            {
                log.AppendLine("FAIL " + ClipFolder + " が無い");
                return null;
            }
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { ClipFolder });
            if (guids.Length == 0)
            {
                log.AppendLine("FAIL " + ClipFolder + " に AudioClip が無い");
                return null;
            }
            Array.Sort(guids, (a, b) => string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));
            if (guids.Length > 1)
                log.AppendLine("   NOTE " + ClipFolder + " に AudioClip が " + guids.Length
                    + " 本ある - 名前順の先頭を使う (差し替えたいなら他を外す)");
            path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        /// <summary>既定 + override を Streaming (preload なし) にする。変えたときだけ SaveAndReimport。</summary>
        static bool EnsureStreaming(string path, StringBuilder log)
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) { log.AppendLine("   FAIL AudioImporter が取れない: " + path); return false; }
            bool changed = false;
            var def = imp.defaultSampleSettings;
            if (def.loadType != AudioClipLoadType.Streaming || def.preloadAudioData)
            {
                def.loadType = AudioClipLoadType.Streaming;
                def.preloadAudioData = false;
                imp.defaultSampleSettings = def;
                changed = true;
            }
            foreach (var platform in AudioOverridePlatforms)
            {
                var s = imp.GetOverrideSampleSettings(platform);
                if (!imp.ContainsSampleSettingsOverride(platform)
                    || s.loadType != AudioClipLoadType.Streaming || s.preloadAudioData)
                {
                    s.loadType = AudioClipLoadType.Streaming;
                    s.preloadAudioData = false;
                    imp.SetOverrideSampleSettings(platform, s);
                    changed = true;
                }
            }
            if (changed)
            {
                imp.SaveAndReimport();
                log.AppendLine("   audio " + path + " -> Streaming (既定 + " + string.Join("/", AudioOverridePlatforms) + ")");
            }
            else log.AppendLine("   audio " + path + " Streaming (変更なし)");
            return true;
        }

        static bool IsStreaming(string path, out string detail)
        {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) { detail = "AudioImporter なし"; return false; }
            var sb = new StringBuilder("default=" + imp.defaultSampleSettings.loadType);
            bool ok = imp.defaultSampleSettings.loadType == AudioClipLoadType.Streaming
                && !imp.defaultSampleSettings.preloadAudioData;
            foreach (var platform in AudioOverridePlatforms)
            {
                var s = imp.GetOverrideSampleSettings(platform);
                bool has = imp.ContainsSampleSettingsOverride(platform);
                if (!has || s.loadType != AudioClipLoadType.Streaming || s.preloadAudioData) ok = false;
                sb.Append(" " + platform + "=" + (has ? s.loadType.ToString() : "(override なし)"));
            }
            detail = sb.ToString();
            return ok;
        }

        /// <summary>proxy の値が backing UdonBehaviour にも入っているか
        /// (**CopyProxyToUdon を忘れると Inspector だけ正しく見える**)。</summary>
        static bool BackingMatches(VRC.Udon.UdonBehaviour backing, string field, UnityEngine.Object want)
        {
            if (backing == null) return false;
            object stored;
            if (!backing.publicVariables.TryGetVariableValue(field, out stored)) return false;
            return Equals(stored, want);
        }

        static bool EnsureSync(UdonSharpBehaviour proxy, string label, VRC.SDKBase.Networking.SyncType want,
            StringBuilder log)
        {
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (ub == null) { log.AppendLine("   FAIL " + label + ": backing UdonBehaviour が取れない"); return false; }
            if (ub.SyncMethod == want) return true;
            log.AppendLine("   " + label + ": SyncMethod " + ub.SyncMethod + " -> " + want);
            ub.SyncMethod = want;
            EditorUtility.SetDirty(ub);
            return true;
        }

        static bool TryCopy(UdonSharpBehaviour proxy, StringBuilder log)
        {
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                EditorUtility.SetDirty(proxy);
                return true;
            }
            catch (Exception e)
            {
                log.AppendLine("CopyProxyToUdon FAILED (" + proxy.GetType().Name + "): " + e.Message);
                return false;
            }
        }

        static void Finish(StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile));
            File.WriteAllText(LogFile, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
