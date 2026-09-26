using System.Collections.Generic;
using System.Linq;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// 裏山改変 (DES-LIV-001、計画書 docs/plans/2026-09-26-hillside-live-wiring.md §4.1 C8): **裏山のライブを置く・配線する・数える。**
    ///
    /// 古民家の Live System 0〜3 (同じクラスの本体、Imported-Unused) は古民家のシーン構成 (KD_Places / 庭の木 / 外壁 / KD_Skybox) 専用なので使わない。
    /// 部品 (EnsureAudioStreaming / 粒の queue 5000 / PlaybackDuration / PruneNullNetworkIds / EnsureSync / TryCopy) は本体のものを再利用する。
    ///
    /// 実行順 (統括が Unity で。**どれもシーンを保存しない**):
    /// <code>
    ///   (生成器) vclive.sh import (--no-scene / saveScene:false)  root に LiveStage_&lt;slug&gt; ができる
    ///   26  Adopt Live Stage   root の LiveStage_&lt;slug&gt; を採用 (旧 KD_LiveStage_&lt;slug&gt; を消す → 改名 → KH_Dogs/KH_Live へ → 定数の transform)
    ///   26a Setup Live         KH_LiveNight の Program Asset が無ければ作って止まる (コンパイル後にもう一度)。
    ///                          台帳 KD_LiveSystem (Manual) / 集合地点 / 夜の橋 KH_LiveNight (None) を作り、§4.3 の配線を書く
    ///   26b Verify Live        26a が書くもの全部を数える (SKIP は理由付き)
    /// </code>
    ///
    /// 置き場所は **KH_Dogs/KH_Live** (KH_World の外。KH_World は地形の Build が作り直す)。
    /// <code>
    /// KH_Dogs/KH_Live/
    ///   KD_LiveSystem            台帳 (Manual。liveId / startMs / hostId)
    ///   KH_LiveNight             夜の橋 (None)。同期時計 → KH_NightMode.liveDarkness
    ///   KD_LiveStage_&lt;slug&gt;    生成物 (inactive。台帳が再生中だけ ON)
    ///   KD_LiveDogSpot           犬の集合地点 (ステージ local (0, 0, 2.5) = 観客の 2.5 m 前、谷側)
    /// </code>
    /// **作らないもの**: KD_LiveNight (夜は KH_NightMode が唯一の書き手。置くと覆いが 2 枚になる)、木と塀の非表示、KD_Skybox。
    /// </summary>
    public static partial class KD_LiveSetupEditor
    {
        // ---- 置き場所と transform (設計が持つ値。Inspector で詰めない。26 / 26a が毎回書き、26b が数える) ----

        /// <summary>KH_Dogs 直下のライブの器。</summary>
        public const string HillsideLiveRootName = "KH_Live";
        /// <summary>ステージのルート (world)。上の広場の中心 KH_Plaza (72, 19.04, 127) の地面 (計画書 §2.7、D6)。</summary>
        public static readonly Vector3 HillsideStagePosition = new Vector3(72f, 19.0f, 127f);
        /// <summary>ステージの向き (world Y)。+Z が谷と古民家の方向 (方位 190°)。</summary>
        public const float HillsideStageEulerY = 190f;
        /// <summary>犬の集合地点 (ステージ local)。演出側 shows/kh-hillside-dusk/config.json の anchors.dogs と同じ
        /// (0, 0, 2.5) = 観客の 2.5 m 前 (谷側)。最後の葉がここへ降りる (統括 2026-09-26)。向きはステージと同じ。</summary>
        public static readonly Vector3 HillsideDogSpotLocal = new Vector3(0f, 0f, 2.5f);

        public const string HillsideLiveNightName = "KH_LiveNight";
        public const string HillsideLiveNightScriptPath = "Assets/KominkaHillside/Scripts/KH_LiveNight.cs";
        public const string HillsideLiveNightProgramPath = "Assets/KominkaHillside/Scripts/KH_LiveNight.asset";
        const string HillsideLiveAccessName = "KD_LiveAccess";
        const string HillsideAdoptLog = KD_Hillside.LogDir + "live-adopt.txt";
        const string HillsideSetupLog = KD_Hillside.LogDir + "live-setup.txt";
        const string HillsideVerifyLog = KD_Hillside.LogDir + "live-verify.txt";

        public static Quaternion HillsideStageRotation { get { return Quaternion.Euler(0f, HillsideStageEulerY, 0f); } }
        /// <summary>集合地点の world 位置 (ステージの定数 transform で local → world)。(72, 19, 127)・Y 190° なら約 (71.57, 19.0, 124.54)。</summary>
        public static Vector3 HillsideDogSpotPosition { get { return HillsideStagePosition + HillsideStageRotation * HillsideDogSpotLocal; } }

        /// <summary>曲の頭から暗さが上限に届くまでの秒 (KH_LiveNight.rampSeconds。添字 = liveId)。表に無い曲は 0 = 古民家と同じ 3 秒フェードイン。</summary>
        public static float HillsideRampSecondsOf(string slug)
        {
            if (slug == KD_DogDefs.HillsideDuskSlug) return global::KominkaHillside.KH_LiveNight.DefaultRampSeconds;
            return 0f;
        }

        /// <summary>ステージの音源のパス。(1) PlayableDirector の TimelineAsset の AudioTrack のクリップ (AudioPlayableAsset.clip) の AudioClip、
        /// (2) 取れなければ Show フォルダ内の audio.* / *.wav / *.mp3 / *.ogg (1 本目)。無ければ null。26a と 26b が同じ探し方を使う。</summary>
        static string HillsideFindAudioPath(string slug, PlayableDirector director)
        {
            var timeline = director != null ? director.playableAsset as UnityEngine.Timeline.TimelineAsset : null;
            if (timeline != null)
                foreach (var track in timeline.GetOutputTracks())
                {
                    if (!(track is UnityEngine.Timeline.AudioTrack)) continue;
                    foreach (var clip in track.GetClips())
                    {
                        var audio = clip.asset as UnityEngine.Timeline.AudioPlayableAsset;
                        string path = audio != null && audio.clip != null ? AssetDatabase.GetAssetPath(audio.clip) : null;
                        if (!string.IsNullOrEmpty(path)) return path;
                    }
                }
            string dir = ShowFolderOf(slug);
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            foreach (var pattern in new[] { "audio.*", "*.wav", "*.mp3", "*.ogg" })
                foreach (var f in System.IO.Directory.GetFiles(dir, pattern))
                {
                    if (f.EndsWith(".meta")) continue;
                    return f.Replace(System.IO.Path.DirectorySeparatorChar, '/');
                }
            return null;
        }

        static float[] HillsideRampSeconds()
        {
            var shows = KD_DogDefs.LiveShows;
            var r = new float[shows.Length];
            for (int i = 0; i < shows.Length; i++) r[i] = HillsideRampSecondsOf(shows[i].slug);
            return r;
        }

        // ================================================================ 26: Adopt

        /// <summary>**動かす・消すだけ** (作らない・保存しない。器 KH_Live だけは無ければ作る)。
        /// 表の各 slug について root 等に生成器の `LiveStage_&lt;slug&gt;` があれば、旧 `KD_LiveStage_&lt;slug&gt;` を消してから採用する。
        /// 採用済みのステージも親と transform を定数へ戻す。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/26 Adopt Live Stage")]
        public static void HillsideAdopt()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_LiveSetup] 26 Adopt Live Stage (裏山)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと");
                KD_VerifyLog.Write(HillsideAdoptLog, log);
                return;
            }
            bool ok = true;
            var liveRoot = HillsideEnsureLiveRoot(log, ref ok);
            if (liveRoot == null) { log.AppendLine("ADOPT RESULT: FAIL"); KD_VerifyLog.Write(HillsideAdoptLog, log); return; }

            int adopted = 0, replaced = 0, placed = 0, missing = 0;
            foreach (var show in KD_DogDefs.LiveShows)
            {
                string newName = KD_DogDefs.LiveStageNameOf(show.slug);
                string legacyName = KD_DogDefs.LegacyLiveStageNameOf(show.slug);
                var freshList = FindLegacyStages().FindAll(g => g.name == legacyName);
                if (freshList.Count > 1)
                {
                    log.AppendLine("   FAIL " + legacyName + " がシーンに " + freshList.Count + " 個 (import を重ねた?)。手で 1 個にしてから再実行");
                    ok = false;
                    continue;
                }
                var fresh = freshList.Count == 1 ? freshList[0] : null;
                var olds = FindAllNamed(newName);
                GameObject stage;
                if (fresh != null)
                {
                    // 旧を先に消す — 同名が 2 個ある瞬間を作らない (再 import で prefab GUID が変わるので旧を残すと Missing Prefab と重複が同時に出る)
                    foreach (var old in olds) { Undo.DestroyObjectImmediate(old); replaced++; }
                    if (olds.Count > 0) log.AppendLine("   replaced " + newName + " (旧インスタンス " + olds.Count + " 個を削除)");
                    Undo.RecordObject(fresh, "adopt stage");
                    fresh.name = newName;
                    EditorUtility.SetDirty(fresh);
                    adopted++;
                    log.AppendLine("   adopted " + legacyName + " -> " + newName);
                    stage = fresh;
                }
                else if (olds.Count == 0)
                {
                    missing++;
                    log.AppendLine("   (none) " + newName + " — 未 import (vclive.sh import shows/" + show.slug + "/...)");
                    continue;
                }
                else if (olds.Count != 1)
                {
                    log.AppendLine("   FAIL " + newName + " が " + olds.Count + " 個 (手で 1 個にする)");
                    ok = false;
                    continue;
                }
                else stage = olds[0];

                if (HillsidePlaceStage(stage, liveRoot)) placed++;
                log.AppendLine("   placed " + KD_SceneObjects.PathOf(stage) + " pos=" + HillsideStagePosition + " rotY=" + HillsideStageEulerY);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("ADOPT RESULT: " + (ok && missing == 0 ? "PASS" : "FAIL") + " adopted=" + adopted + " replaced=" + replaced
                + " placed(changed)=" + placed + " missing=" + missing + " / " + KD_DogDefs.LiveShows.Length);
            log.AppendLine("**シーンは保存していない。** 次: Menu/26a Setup Live → 26b Verify Live");
            KD_VerifyLog.Write(HillsideAdoptLog, log);
        }

        /// <summary>KH_Dogs/KH_Live (無ければ作る。local identity)。KH_Dogs が無ければ null。</summary>
        static Transform HillsideEnsureLiveRoot(StringBuilder log, ref bool ok)
        {
            var dogs = KD_ProgressSetupEditor.FindHillsideRoot(log);
            if (dogs == null) { ok = false; return null; }
            var t = dogs.Find(HillsideLiveRootName);
            if (t == null)
            {
                int n;
                var stray = KD_SceneObjects.Find(HillsideLiveRootName, out n);
                if (stray != null)
                {
                    log.AppendLine("   FAIL " + HillsideLiveRootName + " が " + KD_Hillside.DogsRootName + " の外にある (" + KD_SceneObjects.PathOf(stray) + ")。手で移すか消してから再実行");
                    ok = false;
                    return null;
                }
                var go = new GameObject(HillsideLiveRootName);
                Undo.RegisterCreatedObjectUndo(go, "Create " + HillsideLiveRootName);
                Undo.SetTransformParent(go.transform, dogs, "Place " + HillsideLiveRootName);
                t = go.transform;
                log.AppendLine("   created " + KD_Hillside.DogsRootName + "/" + HillsideLiveRootName);
            }
            if (t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one)
            {
                Undo.RecordObject(t, "Reset " + HillsideLiveRootName);
                t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            }
            if (!t.gameObject.activeSelf) { Undo.RecordObject(t.gameObject, "Activate " + HillsideLiveRootName); t.gameObject.SetActive(true); }
            return t;
        }

        /// <summary>ステージ (と集合地点) を KH_Live の子にして world の定数へ置く。変えたら true。</summary>
        static bool HillsidePlaceStage(GameObject stage, Transform liveRoot)
        {
            return HillsidePlaceAt(stage.transform, liveRoot, HillsideStagePosition, HillsideStageRotation);
        }

        static bool HillsidePlaceAt(Transform t, Transform parent, Vector3 worldPos, Quaternion worldRot)
        {
            bool changed = false;
            if (t.parent != parent) { Undo.SetTransformParent(t, parent, "place live"); changed = true; }
            if ((t.position - worldPos).sqrMagnitude > 1e-8f || Quaternion.Angle(t.rotation, worldRot) > 0.001f || (t.localScale - Vector3.one).sqrMagnitude > 1e-8f)
            {
                Undo.RecordObject(t, "place live");
                t.position = worldPos;
                t.rotation = worldRot;
                t.localScale = Vector3.one;
                EditorUtility.SetDirty(t);
                changed = true;
            }
            return changed;
        }

        static bool HillsideIsPlacedAt(Transform t, Transform parent, Vector3 worldPos, Quaternion worldRot)
        {
            return t != null && t.parent == parent
                && (t.position - worldPos).sqrMagnitude < 1e-6f
                && Quaternion.Angle(t.rotation, worldRot) < 0.01f
                && (t.lossyScale - Vector3.one).sqrMagnitude < 1e-6f;
        }

        // ================================================================ 26a: Setup

        [MenuItem("Tools/KominkaHillside/Menu/26a Setup Live")]
        public static void HillsideSetup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_LiveSetup] 26a Setup Live (裏山)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            {
                log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと");
                KD_VerifyLog.Write(HillsideSetupLog, log);
                return;
            }
            bool ok = true;
            int found;

            // ---- Program Assets (前提)。KH_LiveNight は無ければ作って止まる (作った直後の AddUdonSharpComponent / CopyProxyToUdon は
            // 同期モードの焼き付き・outdated の失敗になる — vrc-udonsharp §2 / §4) ----
            var sysPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(SystemProgramPath);
            if (sysPa == null)
            {
                log.AppendLine("FAIL " + SystemProgramPath + " が無い (移植済みのはず)。SETUP RESULT: FAIL");
                KD_VerifyLog.Write(HillsideSetupLog, log);
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(HillsideLiveNightProgramPath) == null)
            {
                CreateProgramAsset(HillsideLiveNightScriptPath, HillsideLiveNightProgramPath, log);
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                log.AppendLine("SETUP RESULT: FAIL (KH_LiveNight の Program Asset を作った。U# のコンパイル完了を待って 26a をもう一度流す。シーンは触っていない)");
                KD_VerifyLog.Write(HillsideSetupLog, log);
                return;
            }

            var liveRoot = HillsideEnsureLiveRoot(log, ref ok);
            if (liveRoot == null) { log.AppendLine("SETUP RESULT: FAIL"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }

            // ---- ステージ (前提: 26 Adopt 済み。添字 = liveId。1 本でも欠けたら台帳の配線に進まない) ----
            var shows = KD_DogDefs.LiveShows;
            int showCount = shows.Length;
            var stageGos = new GameObject[showCount];
            var directors = new PlayableDirector[showCount];
            var durations = new float[showCount];
            bool stagesOk = true;
            for (int i = 0; i < showCount; i++)
            {
                string stageName = KD_DogDefs.LiveStageNameOf(shows[i].slug);
                var stageGo = KD_SceneObjects.Find(stageName, out found);
                if (stageGo == null || found != 1)
                {
                    log.AppendLine("FAIL [" + i + "] " + stageName + " が " + found + " 個 (1 個のはず)。先に vclive.sh import → Menu/26 Adopt Live Stage");
                    stagesOk = false;
                    continue;
                }
                var director = stageGo.GetComponent<PlayableDirector>();
                if (director == null || director.playableAsset == null)
                {
                    log.AppendLine("FAIL [" + i + "] " + stageGo.name + " に PlayableDirector (playableAsset 付き) が無い");
                    stagesOk = false;
                    continue;
                }
                if (HillsidePlaceStage(stageGo, liveRoot)) log.AppendLine("   placed " + stageGo.name + " (親 / transform を定数へ)");
                if (director.playOnAwake)
                {
                    Undo.RecordObject(director, "playOnAwake off");
                    director.playOnAwake = false;
                    EditorUtility.SetDirty(director);
                    log.AppendLine("   " + stageGo.name + ".playOnAwake -> false");
                }
                if (stageGo.activeSelf)
                {
                    Undo.RecordObject(stageGo, "stage inactive");
                    stageGo.SetActive(false);
                    log.AppendLine("   " + stageGo.name + " -> inactive (台帳が再生中だけ ON にする)");
                }
                float duration = PlaybackDuration(shows[i].slug, director);
                log.AppendLine("   [" + i + "] " + stageGo.name + " duration=" + duration.ToString("F2") + "s darkness=" + shows[i].darkness.ToString("F2")
                    + " product=" + shows[i].productKey + " ramp=" + HillsideRampSecondsOf(shows[i].slug).ToString("F1") + "s");

                // 粒の材質: 夜の覆い (KH_NightMode 4999) より上に描く (生成物なので毎回書く)
                int queued = 0, mats = 0;
                var seenMat = new HashSet<Material>();
                foreach (var r in stageGo.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || !seenMat.Add(m)) continue;
                        mats++;
                        if (m.renderQueue == LiveParticleQueue) continue;
                        Undo.RecordObject(m, "live queue");
                        m.renderQueue = LiveParticleQueue;
                        EditorUtility.SetDirty(m);
                        queued++;
                    }
                log.AppendLine("   live materials=" + mats + " queue->" + LiveParticleQueue + ": " + queued);
                if (mats == 0) { log.AppendLine("   FAIL " + stageGo.name + " に粒の材質が 1 本も無い"); ok = false; }

                // 音源: Streaming (既定 + Standalone / Android / iOS)
                // 裏山: 音源は Timeline の AudioTrack から引く (取り込みは元のファイル名 master.wav のまま置く。audio.* とは限らない)
                string audioPath = HillsideFindAudioPath(shows[i].slug, director);
                if (!EnsureAudioStreamingAt(audioPath, "Timeline の AudioTrack / " + ShowFolderOf(shows[i].slug) + "/{audio.*,*.wav,*.mp3,*.ogg}", log)) ok = false;

                stageGos[i] = stageGo;
                directors[i] = director;
                durations[i] = duration;
            }
            if (!stagesOk)
            {
                log.AppendLine("SETUP RESULT: FAIL (ステージが揃っていない。台帳の配線は触らない)");
                KD_VerifyLog.Write(HillsideSetupLog, log);
                return;
            }

            // ---- 叩く先 (前提) ----
            var activitiesAll = KD_ProductsEditor.SceneComponents<KD_GroupActivities>();
            var activities = activitiesAll.Length == 1 ? activitiesAll[0] : null;
            if (activities == null) { log.AppendLine("   FAIL KD_GroupActivities が " + activitiesAll.Length + " 個 (1 個のはず。Walking/3 Build → Menu/9)"); ok = false; }
            var accessGo = KD_SceneObjects.Find(HillsideLiveAccessName, out found);
            var access = (found == 1 && accessGo != null) ? accessGo.GetUdonSharpComponent<KD_LiveAccess>() : null;
            if (access == null) { log.AppendLine("   FAIL " + HillsideLiveAccessName + " が " + found + " 個 (Menu/11 Setup Localization が作る)"); ok = false; }
            var nightsAll = KD_ProductsEditor.SceneComponents<global::KominkaHillside.KH_NightMode>();
            var nightMode = nightsAll.Length == 1 ? nightsAll[0] : null;
            if (nightMode == null) { log.AppendLine("   FAIL KH_NightMode が " + nightsAll.Length + " 個 (1 個のはず。Night/1 Setup Sky And Night)"); ok = false; }
            // 開催の権利 (hostingItems。添字 = liveId)。Menu/11 も pouch.liveItems から同じものを書く
            var hosting = new KD_ShopItem[showCount];
            var shopItems = KD_ProductsEditor.SceneComponents<KD_ShopItem>();
            for (int i = 0; i < showCount; i++)
            {
                foreach (var it in shopItems)
                    if (it != null && it.categoryKey == KD_ShopSetupEditor.CategoryLive && it.productKey == shows[i].productKey) { hosting[i] = it; break; }
                if (hosting[i] == null) { log.AppendLine("   FAIL [" + i + "] 商品 " + shows[i].productKey + " の KD_ShopItem が無い (先に Menu/4 Setup Shop)"); ok = false; }
            }
            if (activities == null || access == null || nightMode == null || System.Array.IndexOf(hosting, null) >= 0)
            {
                log.AppendLine("SETUP RESULT: FAIL (前提が足りない。台帳の配線は触らない)");
                KD_VerifyLog.Write(HillsideSetupLog, log);
                return;
            }

            // ---- 台帳 KD_LiveSystem (Manual・専用 GameObject、KH_Live の子) ----
            var liveGo = HillsideEnsureChild(liveRoot, KD_DogDefs.LiveSystemName, log, ref ok);
            if (liveGo == null) { log.AppendLine("SETUP RESULT: FAIL"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            var live = liveGo.GetUdonSharpComponent<KD_LiveSystem>();
            if (live == null) { live = liveGo.AddUdonSharpComponent<KD_LiveSystem>(); log.AppendLine("   added KD_LiveSystem"); }
            HillsideEnsureEnabled(live, log);
            live.stageDirectors = directors;
            live.stageRoots = stageGos;
            live.stageDurations = durations;
            live.stageDarkness = GetDarkness();
            live.activities = activities;
            live.access = access;
            live.hostingItems = hosting;
            live.candyPerformance = null;
            live.candyLiveId = -1;
            live.dogPerformances = new KD_CandyDogPerformance[0];   // 犬の出演作品なし (今回の 1 曲)
            ok &= TryCopy(live, log);
            ok &= EnsureSync(live, KD_DogDefs.LiveSystemName, VRC.SDKBase.Networking.SyncType.Manual, log);
            log.AppendLine("   KD_LiveSystem: stages=" + showCount + " activities / access / hostingItems (" + showCount + ") / dogPerformances=0");

            // ---- 共有開催 (live が無いと開催要求 op 4 が常に拒否される。KD_GroupActivities.cs:105) ----
            if (activities.live != live)
            {
                Undo.RecordObject(activities, "wire live");
                activities.live = live;
                log.AppendLine("   wired KD_GroupActivities.live");
            }
            ok &= TryCopy(activities, log);

            // ---- 犬の集合地点 (KH_Live の子。位置・向きは定数 = ステージ local (0, 0, 2.5)、ステージと同じ向き) ----
            var spotGo = HillsideEnsureChild(liveRoot, KD_DogDefs.LiveDogSpotName, log, ref ok);
            if (spotGo == null) { log.AppendLine("SETUP RESULT: FAIL"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            if (HillsidePlaceAt(spotGo.transform, liveRoot, HillsideDogSpotPosition, HillsideStageRotation))
                log.AppendLine("   placed " + spotGo.name + " pos=" + HillsideDogSpotPosition.ToString("F2") + " rotY=" + HillsideStageEulerY);

            // ---- 犬 6 匹 (観客参加と、ライブ中の新しい Fetch 停止) ----
            foreach (var def in KD_DogDefs.All)
            {
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                var wander = (found == 1 && wanderGo != null) ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (wander == null) { log.AppendLine("   FAIL " + def.WanderSystemName + " が取れない (" + found + " 個) - この犬は集まらない"); ok = false; }
                else
                {
                    if (wander.liveSystem != live || wander.liveSpot != spotGo.transform)
                    {
                        Undo.RecordObject(wander, "wire live");
                        wander.liveSystem = live;
                        wander.liveSpot = spotGo.transform;
                        log.AppendLine("   wired " + def.WanderSystemName + ".liveSystem / .liveSpot");
                    }
                    ok &= TryCopy(wander, log);
                }
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out found);
                var fetch = (found == 1 && fetchGo != null) ? fetchGo.GetUdonSharpComponent<KD_DogFetchController>() : null;
                if (fetch == null) { log.AppendLine("   FAIL " + def.FetchSystemName + " が取れない (" + found + " 個) - この犬はライブ中も投擲に走る"); ok = false; }
                else
                {
                    if (fetch.liveSystem != live)
                    {
                        Undo.RecordObject(fetch, "wire live");
                        fetch.liveSystem = live;
                        log.AppendLine("   wired " + def.FetchSystemName + ".liveSystem");
                    }
                    ok &= TryCopy(fetch, log);
                }
            }

            // ---- 夜の橋 KH_LiveNight (None。KH_Live の子) ----
            var nightGo = HillsideEnsureChild(liveRoot, HillsideLiveNightName, log, ref ok);
            if (nightGo == null) { log.AppendLine("SETUP RESULT: FAIL"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            var bridge = nightGo.GetUdonSharpComponent<global::KominkaHillside.KH_LiveNight>();
            if (bridge == null) { bridge = nightGo.AddUdonSharpComponent<global::KominkaHillside.KH_LiveNight>(); log.AppendLine("   added KH_LiveNight"); }
            HillsideEnsureEnabled(bridge, log);
            bridge.live = live;
            bridge.nightMode = nightMode;
            bridge.rampSeconds = HillsideRampSeconds();
            bridge.useKominkaFade = global::KominkaHillside.KH_LiveNight.DefaultUseKominkaFade;
            ok &= TryCopy(bridge, log);
            ok &= EnsureSync(bridge, HillsideLiveNightName, VRC.SDKBase.Networking.SyncType.None, log);
            log.AppendLine("   KH_LiveNight: live / nightMode = " + KD_SceneObjects.PathOf(nightMode.gameObject) + " / rampSeconds = "
                + string.Join(",", System.Array.ConvertAll(bridge.rampSeconds, x => x.ToString("F1"))) + " / useKominkaFade = " + bridge.useKominkaFade);

            // ---- 古民家の夜 (KD_LiveNight) は置かない。残っていれば FAIL (覆いが 2 枚になり掛け算で濃くなる) ----
            int kdNights = KD_ProductsEditor.SceneComponents<KD_LiveNight>().Length;
            if (kdNights > 0) { log.AppendLine("   FAIL KD_LiveNight がシーンに " + kdNights + " 個ある — 裏山では使わない。手で消すこと"); ok = false; }

            // ---- 後始末: 表に無い KD_LiveStage_* / 残った LiveStage_* (表の曲なら FAIL で 26 へ回す) / NetworkIDs の残骸 ----
            {
                var wanted = new HashSet<string>();
                var slugs = new HashSet<string>();
                foreach (var s in shows) { wanted.Add(KD_DogDefs.LiveStageNameOf(s.slug)); slugs.Add(s.slug); }
                foreach (var stray in FindStrayStages(wanted))
                {
                    log.AppendLine("   removed " + KD_SceneObjects.PathOf(stray) + " (表に無いステージ)");
                    Undo.DestroyObjectImmediate(stray);
                }
                foreach (var leftover in FindLegacyStages())
                {
                    string slug = leftover.name.Substring(KD_DogDefs.LegacyLiveStagePrefix.Length);
                    if (slugs.Contains(slug))
                    {
                        log.AppendLine("   FAIL " + KD_SceneObjects.PathOf(leftover) + " が残っている (表の曲) — 先に Menu/26 Adopt Live Stage");
                        ok = false;
                        continue;
                    }
                    log.AppendLine("   removed " + KD_SceneObjects.PathOf(leftover) + " (残った表に無い生成物)");
                    Undo.DestroyObjectImmediate(leftover);
                }
                PruneNullNetworkIds(log);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SKIP 木と塀の非表示・KD_Skybox・夜の覆い (古民家の KD_LiveNight の仕事。裏山の空と覆いの書き手は KH_NightMode だけ)");
            log.AppendLine("SKIP SleepAudio の再配線 (KD_SleepAudioSetupEditor.RewireIfPresent。裏山に KD_SleepAudio は無い)");
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("**シーンは保存していない。** 次: Menu/26b Verify Live → Menu/7 Build Menu (pouch.live) → 13 → 10 → 11 → 19b Assign Network IDs");
            KD_VerifyLog.Write(HillsideSetupLog, log);
        }

        /// <summary><paramref name="parent"/> の子 <paramref name="name"/> を 1 個にする。シーンの別の場所に 1 個だけあれば移す (値は保つ)、
        /// 2 個以上なら FAIL、無ければ作る (local identity)。</summary>
        static GameObject HillsideEnsureChild(Transform parent, string name, StringBuilder log, ref bool ok)
        {
            int found;
            var go = KD_SceneObjects.Find(name, out found);
            if (found > 1)
            {
                log.AppendLine("   FAIL '" + name + "' がシーン内に " + found + " 個ある (手で 1 個にしてから再実行)");
                ok = false;
                return null;
            }
            if (go == null)
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Create " + name);
                Undo.SetTransformParent(go.transform, parent, "Place " + name);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                log.AppendLine("   created " + KD_SceneObjects.PathOf(go));
            }
            else if (go.transform.parent != parent)
            {
                log.AppendLine("   moved " + KD_SceneObjects.PathOf(go) + " -> " + KD_SceneObjects.PathOf(parent.gameObject));
                Undo.SetTransformParent(go.transform, parent, "Move " + name);
            }
            if (!go.activeSelf) { Undo.RecordObject(go, "Activate " + name); go.SetActive(true); }
            return go;
        }

        /// <summary>proxy と backing UdonBehaviour の enabled を true に (26b が数える)。</summary>
        static void HillsideEnsureEnabled(UdonSharpBehaviour proxy, StringBuilder log)
        {
            if (!proxy.enabled) { Undo.RecordObject(proxy, "enable"); proxy.enabled = true; log.AppendLine("   enabled " + proxy.GetType().Name); }
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (backing != null && !backing.enabled) { Undo.RecordObject(backing, "enable"); backing.enabled = true; EditorUtility.SetDirty(backing); log.AppendLine("   enabled backing " + proxy.GetType().Name); }
        }

        // ================================================================ 26b: Verify

        [MenuItem("Tools/KominkaHillside/Menu/26b Verify Live")]
        public static void HillsideVerify()
        {
            var v = new KD_VerifyLog("[KD_LiveSetup] 26b Verify Live (裏山)");
            var log = v.Text;
            if (!v.Check(!EditorApplication.isPlaying && KD_Hillside.InScene(), KD_Hillside.ScenePath + " を Edit モードで開いている"))
            { v.Finish(HillsideVerifyLog); return; }
            int found;

            // ---- Program Assets ----
            var sysPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(SystemProgramPath);
            var bridgePa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(HillsideLiveNightProgramPath);
            v.Check(sysPa != null && sysPa.sourceCsScript != null && sysPa.behaviourSyncMode == BehaviourSyncMode.Manual, "program asset KD_LiveSystem (Manual)");
            v.Check(bridgePa != null && bridgePa.sourceCsScript == AssetDatabase.LoadAssetAtPath<MonoScript>(HillsideLiveNightScriptPath)
                && bridgePa.behaviourSyncMode == BehaviourSyncMode.None, "program asset KH_LiveNight → KH_LiveNight.cs (None)");

            // ---- 表 ----
            var shows = KD_DogDefs.LiveShows;
            int showCount = shows.Length;
            var wantedStages = new HashSet<string>();
            {
                var slugs = new HashSet<string>();
                bool uniq = true;
                foreach (var s in shows) { if (!slugs.Add(s.slug)) uniq = false; wantedStages.Add(KD_DogDefs.LiveStageNameOf(s.slug)); }
                v.Check(showCount >= 1 && showCount <= 9 && uniq, "LiveShows の slug が一意で 1〜9 本 (" + showCount + " 本。板の口 _Live0.._Live8)");
            }

            // ---- 器 KH_Dogs/KH_Live ----
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out found);
            var liveRoot = (found == 1 && dogsGo != null) ? dogsGo.transform.Find(HillsideLiveRootName) : null;
            string rootPath = KD_Hillside.DogsRootName + "/" + HillsideLiveRootName;
            if (!v.Check(liveRoot != null && liveRoot.gameObject.activeSelf, rootPath + " がある (active)")) { v.Finish(HillsideVerifyLog); return; }
            v.Check(liveRoot.localPosition == Vector3.zero && liveRoot.localRotation == Quaternion.identity && liveRoot.localScale == Vector3.one, rootPath + " の local transform が identity");

            // ---- ステージ (作品ごと。添字 = liveId) ----
            var stageGos = new GameObject[showCount];
            var directors = new PlayableDirector[showCount];
            for (int i = 0; i < showCount; i++)
            {
                string tag = "[" + i + " " + shows[i].slug + "] ";
                string stageName = KD_DogDefs.LiveStageNameOf(shows[i].slug);
                var stageGo = KD_SceneObjects.Find(stageName, out found);
                v.Check(stageGo != null && found == 1, tag + stageName + " が 1 個 (actual " + found + ")");
                if (stageGo == null) continue;
                stageGos[i] = stageGo;
                var director = stageGo.GetComponent<PlayableDirector>();
                directors[i] = director;
                v.Check(HillsideIsPlacedAt(stageGo.transform, liveRoot, HillsideStagePosition, HillsideStageRotation),
                    tag + rootPath + " の子で world transform == 定数 (pos " + HillsideStagePosition + " / rotY " + HillsideStageEulerY
                    + "。actual " + KD_SceneObjects.PathOf(stageGo) + " " + stageGo.transform.position + " / " + stageGo.transform.eulerAngles.y.ToString("F1") + "。違えば 26)");
                v.Check(director != null && director.playableAsset != null, tag + "PlayableDirector + playableAsset");
                v.Check(director != null && !director.playOnAwake, tag + "playOnAwake == false");
                v.Check(!stageGo.activeSelf, tag + "inactive (台帳が再生中だけ ON)");
                int missingPrefab = 0;
                foreach (var t in stageGo.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.GetPrefabInstanceStatus(t.gameObject) == PrefabInstanceStatus.MissingAsset) missingPrefab++;
                v.Check(missingPrefab == 0, tag + "Missing Prefab == 0 (actual " + missingPrefab + ")");
                int low = 0, total = 0;
                var seen = new HashSet<Material>();
                foreach (var r in stageGo.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || !seen.Add(m)) continue;
                        total++;
                        if (m.renderQueue != LiveParticleQueue) low++;
                    }
                v.Check(total > 0 && low == 0, tag + "粒の材質 " + total + " 本すべて renderQueue == " + LiveParticleQueue + " (夜の覆い " + global::KominkaHillside.KH_NightMode.OverlayQueue + " より上。違反 " + low + ")");
                string audioDetail;
                string audioPath = HillsideFindAudioPath(shows[i].slug, director);
                v.Check(IsAudioStreamingAt(audioPath, out audioDetail), tag + "音源 " + (audioPath ?? "(なし)") + " が Streaming (既定 + " + string.Join("/", AudioOverridePlatforms) + "。actual " + audioDetail + ")");
            }
            {
                var leftovers = FindLegacyStages();
                var names = new StringBuilder();
                foreach (var l in leftovers) names.Append(KD_SceneObjects.PathOf(l) + " ");
                v.Check(leftovers.Count == 0, "シーンに LiveStage_* が残っていない (actual " + leftovers.Count + " " + names + "。26 が採用 / 26a が表に無いものを消す)");
                var strays = FindStrayStages(wantedStages);
                names.Length = 0;
                foreach (var s in strays) names.Append(KD_SceneObjects.PathOf(s) + " ");
                v.Check(strays.Count == 0, "表に無い KD_LiveStage_* が無い (actual " + strays.Count + " " + names + ")");
                int typedNull;
                int nullIds = CountNullNetworkIds(out typedNull);
                v.Check(nullIds == 0, "NetworkIDs に gameObject null のエントリが無い (actual " + nullIds + "。うち型付き " + typedNull + ")");
            }

            // ---- 商品 ----
            var shopItems = KD_ProductsEditor.SceneComponents<KD_ShopItem>();
            {
                var productIds = new HashSet<string>();
                bool uniq = true, registered = true;
                foreach (var p in KD_DogDefs.LiveProducts)
                {
                    if (!productIds.Add(p.productKey)) uniq = false;
                    try { if (KD_ProductDefinitions.Get(p.productKey).categoryKey != "live") registered = false; }
                    catch (System.InvalidOperationException) { registered = false; }
                }
                v.Check(uniq && registered, "LiveProducts の productKey が一意で KD_ProductDefinitions の live 行 (" + KD_DogDefs.LiveProducts.Length + " 商品)");
                foreach (var s in shows)
                {
                    v.Check(productIds.Contains(s.productKey), "[" + s.slug + "] productKey " + s.productKey + " が LiveProducts にある");
                    int c = 0;
                    foreach (var it in shopItems) if (it != null && it.categoryKey == KD_ShopSetupEditor.CategoryLive && it.productKey == s.productKey) c++;
                    v.Check(c == 1, "[" + s.slug + "] 商品 " + KD_ProductDefinitions.SceneName(s.productKey) + " がシーンに 1 個 (actual " + c + "。Menu/4 Setup Shop が作る)");
                }
            }

            // ---- 台帳 KD_LiveSystem ----
            var liveGo = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName, out found);
            v.Check(liveGo != null && found == 1, KD_DogDefs.LiveSystemName + " が 1 個 (actual " + found + ")");
            var live = liveGo != null ? liveGo.GetUdonSharpComponent<KD_LiveSystem>() : null;
            if (v.Check(live != null, KD_DogDefs.LiveSystemName + ": KD_LiveSystem が付いている"))
            {
                v.Check(liveGo.activeSelf && liveGo.activeInHierarchy && live.enabled && UdonSharpEditorUtility.GetBackingUdonBehaviour(live) != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(live).enabled, KD_DogDefs.LiveSystemName + " が active (activeSelf / activeInHierarchy / proxy と backing の enabled。無効だと開催も時計も動かない。26a が有効化する)");
                v.Check(liveGo.transform.parent == liveRoot, KD_DogDefs.LiveSystemName + " が " + rootPath + " の子 (actual " + KD_SceneObjects.PathOf(liveGo) + ")");
                v.Check(SceneSyncOf(liveGo) == VRC.SDKBase.Networking.SyncType.Manual, KD_DogDefs.LiveSystemName + " の UdonBehaviour.SyncMethod == Manual (actual " + SceneSyncOf(liveGo) + ")");
                v.Check(liveGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1, KD_DogDefs.LiveSystemName + " の UdonBehaviour は 1 個 (同期方式を混在させない)");
                v.Check(liveGo.GetComponent<VRCObjectSync>() == null, KD_DogDefs.LiveSystemName + " に VRCObjectSync が無い");
                var acts = KD_ProductsEditor.SceneComponents<KD_GroupActivities>();
                v.Check(acts.Length == 1 && live.activities == acts[0], KD_DogDefs.LiveSystemName + ".activities == シーンの KD_GroupActivities (停止・開催の認可の経路)");
                v.Check(acts.Length == 1 && acts[0].live == live, "KD_GroupActivities.live == " + KD_DogDefs.LiveSystemName + " (無いと開催要求 op 4 が常に拒否)");
                var accessGo = KD_SceneObjects.Find(HillsideLiveAccessName, out found);
                v.Check(found == 1 && live.access != null && live.access.gameObject == accessGo, KD_DogDefs.LiveSystemName + ".access == " + HillsideLiveAccessName);
                v.Check(live.access != null && live.access.sleepBoard != null && live.access.sleepBoard.gameObject == KD_SceneObjects.Find(KD_DogDefs.SleepBoardName),
                    HillsideLiveAccessName + ".sleepBoard が " + KD_DogDefs.SleepBoardName + " (管理者判定の正本。Menu/11 が書く)");
                bool hostOk = live.hostingItems != null && live.hostingItems.Length == showCount;
                for (int i = 0; hostOk && i < showCount; i++)
                    hostOk = live.hostingItems[i] != null && live.hostingItems[i].productKey == shows[i].productKey;
                v.Check(hostOk, KD_DogDefs.LiveSystemName + ".hostingItems が作品ごとの商品 (添字 = liveId。" + showCount + " 本)");
                v.Check(live.dogPerformances == null || live.dogPerformances.Length == 0, KD_DogDefs.LiveSystemName + ".dogPerformances が空 (犬の出演作品なし)");
                v.Check(live.candyPerformance == null && live.candyLiveId < 0, KD_DogDefs.LiveSystemName + ".candyPerformance なし / candyLiveId -1");
                bool lengths = live.stageDirectors != null && live.stageDirectors.Length == showCount
                    && live.stageRoots != null && live.stageRoots.Length == showCount
                    && live.stageDurations != null && live.stageDurations.Length == showCount
                    && live.stageDarkness != null && live.stageDarkness.Length == showCount;
                v.Check(lengths, KD_DogDefs.LiveSystemName + ".stage* 4 配列の長さ == " + showCount);
                var wantDark = GetDarkness();
                for (int i = 0; lengths && i < showCount; i++)
                {
                    string tag = "[" + i + " " + shows[i].slug + "] ";
                    v.Check(directors[i] != null && live.stageDirectors[i] == directors[i] && stageGos[i] != null && live.stageRoots[i] == stageGos[i],
                        tag + "stageDirectors / stageRoots がこのステージ (添字 = liveId)");
                    float want = directors[i] != null ? PlaybackDuration(shows[i].slug, directors[i]) : -1f;
                    v.Check(directors[i] != null && Mathf.Abs(live.stageDurations[i] - want) < 0.01f,
                        tag + "stageDurations == 再生長 " + want.ToString("F2") + "s (actual " + live.stageDurations[i].ToString("F2") + ")");
                    v.Check(Mathf.Abs(live.stageDarkness[i] - wantDark[i]) < 0.001f, tag + "stageDarkness == " + wantDark[i] + " (actual " + live.stageDarkness[i] + ")");
                }
            }

            // ---- 犬の集合地点 ----
            var spotGo = KD_SceneObjects.Find(KD_DogDefs.LiveDogSpotName, out found);
            v.Check(spotGo != null && found == 1, KD_DogDefs.LiveDogSpotName + " が 1 個 (actual " + found + ")");
            if (spotGo != null)
                v.Check(spotGo.activeSelf && spotGo.activeInHierarchy, KD_DogDefs.LiveDogSpotName + " が active (activeSelf / activeInHierarchy。26a が有効化する)");
            if (spotGo != null)
                v.Check(HillsideIsPlacedAt(spotGo.transform, liveRoot, HillsideDogSpotPosition, HillsideStageRotation),
                    KD_DogDefs.LiveDogSpotName + " が " + rootPath + " の子で world pos == ステージ local " + HillsideDogSpotLocal + " (" + HillsideDogSpotPosition.ToString("F2")
                    + ")・向き Y " + HillsideStageEulerY + " (actual " + spotGo.transform.position.ToString("F2") + " / " + spotGo.transform.eulerAngles.y.ToString("F1") + ")");
            foreach (var def in KD_DogDefs.All)
            {
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                var wander = (found == 1 && wanderGo != null) ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (v.Check(wander != null, def.WanderSystemName + " が取れる (actual " + found + " 個)"))
                    v.Check(live != null && wander.liveSystem == live && spotGo != null && wander.liveSpot == spotGo.transform,
                        def.WanderSystemName + ".liveSystem / .liveSpot 配線 (犬はここから IsPlaying を読む)");
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out found);
                var fetch = (found == 1 && fetchGo != null) ? fetchGo.GetUdonSharpComponent<KD_DogFetchController>() : null;
                if (v.Check(fetch != null, def.FetchSystemName + " が取れる (actual " + found + " 個)"))
                    v.Check(live != null && fetch.liveSystem == live, def.FetchSystemName + ".liveSystem 配線 (ライブ中は投擲に反応しない。フリスビーも KH_FrisbeeCatch.CanPlay がここを読む)");
            }

            // ---- 夜の橋 KH_LiveNight ----
            var nightGo = KD_SceneObjects.Find(HillsideLiveNightName, out found);
            v.Check(nightGo != null && found == 1, HillsideLiveNightName + " が 1 個 (actual " + found + ")");
            var bridge = nightGo != null ? nightGo.GetUdonSharpComponent<global::KominkaHillside.KH_LiveNight>() : null;
            if (v.Check(bridge != null, HillsideLiveNightName + ": KH_LiveNight が付いている"))
            {
                var nights = KD_ProductsEditor.SceneComponents<global::KominkaHillside.KH_NightMode>();
                v.Check(nightGo.activeSelf && nightGo.activeInHierarchy && bridge.enabled && UdonSharpEditorUtility.GetBackingUdonBehaviour(bridge) != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(bridge).enabled, HillsideLiveNightName + " が active (activeSelf / activeInHierarchy / proxy と backing の enabled。無効だと暗くならない。26a が有効化する)");
                v.Check(nightGo.transform.parent == liveRoot, HillsideLiveNightName + " が " + rootPath + " の子");
                v.Check(SceneSyncOf(nightGo) == VRC.SDKBase.Networking.SyncType.None, HillsideLiveNightName + " の UdonBehaviour.SyncMethod == None (actual " + SceneSyncOf(nightGo) + ")");
                v.Check(live != null && bridge.live == live, HillsideLiveNightName + ".live == " + KD_DogDefs.LiveSystemName);
                v.Check(nights.Length == 1 && bridge.nightMode == nights[0], HillsideLiveNightName + ".nightMode == シーンの KH_NightMode (" + nights.Length + " 個)");
                var wantRamp = HillsideRampSeconds();
                bool rampOk = bridge.rampSeconds != null && bridge.rampSeconds.Length == wantRamp.Length;
                for (int i = 0; rampOk && i < wantRamp.Length; i++) if (Mathf.Abs(bridge.rampSeconds[i] - wantRamp[i]) > 0.001f) rampOk = false;
                v.Check(rampOk, HillsideLiveNightName + ".rampSeconds == 表 (" + string.Join(",", System.Array.ConvertAll(wantRamp, x => x.ToString("F1")))
                    + "。actual " + (bridge.rampSeconds == null ? "null" : string.Join(",", System.Array.ConvertAll(bridge.rampSeconds, x => x.ToString("F1")))) + ")");
                v.Check(bridge.useKominkaFade == global::KominkaHillside.KH_LiveNight.DefaultUseKominkaFade,
                    HillsideLiveNightName + ".useKominkaFade == " + global::KominkaHillside.KH_LiveNight.DefaultUseKominkaFade + " (比較で切り替えたまま保存していない)");
                v.Check(nights.Length == 1 && nights[0].liveDarkness == 0f, "KH_NightMode.liveDarkness == 0 (Play 中の値を保存していない)");
                v.Check(!typeof(global::KominkaHillside.KH_LiveNight).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    .Any(f => f.GetCustomAttributes(typeof(UdonSyncedAttribute), true).Length > 0), "KH_LiveNight の同期変数 0 本");
            }
            v.Check(KD_ProductsEditor.SceneComponents<KD_LiveNight>().Length == 0, "KD_LiveNight がシーンに無い (裏山の空と覆いの書き手は KH_NightMode だけ。置くと 2 枚目の乗算)");
            v.Check(KD_SceneObjects.Find(KD_DogDefs.LiveNightOverlayName) == null, KD_DogDefs.LiveNightOverlayName + " が無い");

            log.AppendLine("  SKIP 木と塀の非表示 (hideRenderers) — 裏山では持ち込まない (葉が昇る木そのものが演出の起点。計画書 §2.3)");
            log.AppendLine("  SKIP KD_Skybox.mat / 空の露出 — 裏山の空は KH_Skybox.mat で、書き手は KH_NightMode (Night/2 Verify が数える)");
            log.AppendLine("  SKIP KD_DogDefs.LayoutTable の行 (KD_Places / KD_Systems) — 古民家の Scene Layout 用。裏山の置き場所は " + rootPath + " (上で検査)");
            log.AppendLine("  SKIP 欠番 24 / 旧作 all-together-now / 旧方式 NightQueue の残骸 — 古民家のシーンの履歴。裏山には無い");
            log.AppendLine("  SKIP SleepAudio (KD_SleepAudio) の再配線 — 裏山に無い");
            log.AppendLine("  NOTE 手元メニューのライブタブ (pouch.live / 行 / 詳細) は Menu/20 Verify Pouch、開催許可と hostingItems の一致は Menu/22 Verify Localization が数える");
            v.Finish(HillsideVerifyLog);
        }
    }
}
