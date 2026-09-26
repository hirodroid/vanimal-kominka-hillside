using System.Collections.Generic;
using System.IO;
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
    /// IMP-PRG-004 Phase 1: **ParticleLive の器 (台帳 + 夜) を作る / 数える。**
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    /// <code>
    ///   0 - Create Program Assets  → force refresh (コンパイル完了まで待つ)。初回だけ
    ///   1 - Adopt Imported Stages  生成器が root に置いた LiveStage_&lt;slug&gt; を採用 (旧を消す / 改名 / KD_Places / 定数の transform)
    ///   2 - Setup Live System      作品 N 本を台帳へ配線 (playOnAwake / inactive / 粒の queue / 長さ / 音源 Streaming)
    ///   3 - Verify Live System
    /// </code>
    ///
    /// **1 は Layout 系 (動かす・消すだけ)、2 は Setup (作る・配線する)、3 は数えるだけ** (CLAUDE.md の役割分担)。
    /// ステージの置き場所と向きは設計が持つ値 (`KD_DogDefs.LiveStagePosition / LiveStageEulerY`) なので
    /// 1 が毎回書く — Scene Layout/2 - Apply は改名をスキップするので差し替えには使えない (Codex 指摘 2026-09-06)。
    ///
    /// 前提: vrc-live-generator で `KD_DogDefs.LiveShows` の各 slug が import 済み
    /// (IMP-PRG-005。品ぞろえの正本は `KD_DogDefs.LiveShows` / `LiveProducts`)。
    ///
    /// シーンに置くもの:
    /// <code>
    /// KD_Systems/
    ///   KD_LiveSystem            台帳 (Manual。liveId / startMs / hostId)
    ///   KD_LiveNight             夜 (None)。木と塀 / 空 / 覆いを持つ
    ///     KD_LiveNightOverlay    夜の覆い (画面いっぱいの板。KD_NightOverlay の材質。queue 4999)
    /// </code>
    /// (代役ボタン KD_Debug/KD_LiveDebug_* は 2026-09-06 に廃止。開催は手元メニューのライブタブ)
    ///
    /// **購入プレハブにも購入材質にも何も足さない・書かない。** 木と塀は参照を持つだけ。
    /// Live の粒の材質だけは renderQueue を 5000 に書く (生成物。差し替えのたびに 2 を流す)。
    ///
    /// 2026-09-06 の経緯: 「アバターだけ明るく残す」層の制限 (世界の材質の描画順を下げる) は、
    /// Standard 系の ShaderGUI が import のたびに queue を作り直す・購入材質を書き換える、で複雑になりすぎたので
    /// **ユーザー決定で廃止** (暗くなるかならないかだけ。粒だけ上)。代わりに暗さの上限を作品ごとに持つ。
    /// Setup は当時差した複製材質 (Materials/NightQueue/) を元へ戻して消す。
    /// </summary>
    // 裏山改変 (DES-LIV-001): partial にして裏山の Menu/26・26a・26b を KD_LiveSetupEditor.Hillside.cs に置く (このファイルの部品を再利用する)
    public static partial class KD_LiveSetupEditor
    {
        public const string SystemScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_LiveSystem.cs";
        public const string SystemProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_LiveSystem.asset";
        public const string NightScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_LiveNight.cs";
        /// <summary>手元メニューの設定 (Menu Finish が置く)。LiveNight が SleepKit の明るさを読むための配線先。</summary>
        const string MenuSettingsName = "KD_MenuSettings";
        public const string NightProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_LiveNight.asset";

        public const string OverlayShaderPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Shaders/KD_NightOverlay.shader";
        public const string OverlayMaterialPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_NightOverlay.mat";
        public const string FullscreenQuadPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_FullscreenQuad.asset";
        public const string FullscreenQuadName = "KD_FullscreenQuad";
        /// <summary>空の材質 (プロジェクト側の複製)。**これが `RenderSettings.skybox` の実体** (IMP-ENV-010)。</summary>
        public const string SkyboxCopyPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_Skybox.mat";
        /// <summary>旧方式 (夜の空を 2 枚目として持つ) の複製材質。Setup が消す。</summary>
        public const string LegacyNightSkyboxPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_NightSkybox.mat";
        /// <summary>旧方式 (層の制限) の複製材質の置き場。Setup が元へ戻して消す。</summary>
        public const string LegacyNightQueueFolder = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/NightQueue";
        public const string LegacyGrassKeeperPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/KD_GrassOverrideKeeper.mat";


        /// <summary>Live 中に消すもの (購入シーンの名前。DES-PRG-002 §4.3)。
        /// 桜は IMP-ENV-016 で庭から外したが将来戻す可能性があるので**任意** (無ければ skip、あれば従来どおり消す)。</summary>
        public const string TreeName = "CQ_Sakura_2_with_Particle";
        /// <summary>庭の木 (IMP-ENV-016 の秋の広葉樹。`KD_Places` 直下)。IMP-ENV-018: Live 中はこちらも消す。</summary>
        public const string SeasonTreeName = "KD_SeasonTree";
        public const string WallName = "外壁";
        public const string PurchasedObjectsRoot = "====オブジェクト====";
        public const string PurchasedNonCc0Root = "==非CC0アセット用==";

        /// <summary>Live の粒の renderQueue。覆い (4999) より上で、**Unity の上限 5000 の内側**。</summary>
        public const int LiveParticleQueue = 5000;

        // ---- 品ぞろえ (IMP-PRG-005。**正本は KD_DogDefs.LiveShows / LiveProducts**) ----
        //
        // 作品 (行 = liveId) と商品 (productKey) は別表。Pack は 5 曲が同じ商品を指すので
        // **ショップへ渡すのは商品数 (2)、ライブタブの行数は作品数 (6)** (Codex 指摘 2026-09-06)。
        // 恒久 ID の枠と欠番 (24) の説明は KD_DogDefs 側。

        /// <summary>ショップ側 (KD_ShopSetupEditor.EnsureLiveItems) が読む商品。**添字は商品の並びであって liveId ではない。**</summary>
        public static void GetShopEntries(out string[] ids, out string[] labels, out int[] prices)
        {
            var p = KD_DogDefs.LiveProducts;
            ids = new string[p.Length];
            labels = new string[p.Length];
            prices = new int[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                ids[i] = p[i].productKey;
                labels[i] = p[i].displayName;
                prices[i] = p[i].price;
            }
        }

        /// <summary>作品ごとの夜の暗さ (添字 = liveId)。</summary>
        public static float[] GetDarkness()
        {
            var s = KD_DogDefs.LiveShows;
            var d = new float[s.Length];
            for (int i = 0; i < s.Length; i++) d[i] = s[i].darkness;
            return d;
        }

        /// <summary>生成器 (vrc-live-generator) の出力先。音源は `Shows/&lt;slug&gt;/audio.*`。</summary>
        public const string ShowsFolder = "Assets/VirtualCampers.LiveGenerator/Shows";
        public static string ShowFolderOf(string slug) { return ShowsFolder + "/" + slug; }

        // Authored shows include particle tails past the music. End the shared performance at the audio boundary.
        public static float PlaybackDuration(string slug, PlayableDirector director)
        {
            if (slug != "kd-candystep-002" && slug != "kd-gardenlights-004-lotus-r003" && slug != "kd-pumpkinparade-002") return (float)director.duration;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(FindAudioPath(slug));
            if (clip == null || clip.frequency <= 0) throw new System.InvalidOperationException("Missing show audio: " + slug);
            return (float)((double)clip.samples / clip.frequency);
        }

        /// <summary>旧作 (productKey 24 = 欠番)。商品・ステージ・フォルダのどれも残っていないことを Verify が数える
        /// (フォルダは Phase 5 で `git rm`。それまで Verify のこの 1 件は FAIL でよい)。</summary>
        public const string RetiredSlug = "all-together-now";
        public const int RetiredUnlockId = 24;

        /// <summary>音源の読み込み方式は Streaming (Decompress On Load は 10 曲で 232MB — `vrc-udonsharp` §7)。
        /// **既定だけでなく Standalone / Android / iOS の override も同じにする**
        /// (既存 override が DecompressOnLoad だと既定を直しても PC で効かない。Codex 指摘 2026-09-06)。</summary>
        public static readonly string[] AudioOverridePlatforms = { "Standalone", "Android", "iOS" };

        /// <summary>テスト予約の恒久 ID (Verify の自己テスト用)。商品に割り当てない (Codex 指摘)。</summary>

        const string LogFile = "Logs/imp-prg-004-live.txt";

        // ================================================================ 0

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Live System/0 - Create Program Assets")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_LiveSetup] Create program assets");
            CreateProgramAsset(SystemScriptPath, SystemProgramPath, log);
            CreateProgramAsset(NightScriptPath, NightProgramPath, log);
            log.AppendLine("次: force refresh (コンパイル完了まで待つ) してから 1 - Adopt Imported Stages → 2 - Setup Live System");
            Finish(log);
        }

        // ================================================================ 1

        /// <summary>**Layout 系: 動かす・消すだけ。作らない・保存しない。**
        ///
        /// 表の各 slug について、root に生成器が置いた `LiveStage_&lt;slug&gt;` があれば
        /// **既存の `KD_LiveStage_&lt;slug&gt;` を消してから**新を採用する (改名 → `KD_Places` へ → 定数の transform)。
        /// 既に採用済みのステージも transform を定数へ戻す (設計が持つ値。Inspector で詰めない)。
        /// 再 import で prefab GUID が変わるので、旧インスタンスを残すと Missing Prefab と重複が同時に出る。</summary>
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Live System/1 - Adopt Imported Stages")]
        public static void AdoptImportedStages()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_LiveSetup] Adopt imported stages");
            bool ok = true;
            int found;

            var places = FindPlaces(log, ref ok);
            if (places == null) { log.AppendLine("ADOPT RESULT: FAIL"); Finish(log); return; }

            int adopted = 0, replaced = 0, placed = 0, missing = 0;
            foreach (var show in KD_DogDefs.LiveShows)
            {
                string newName = KD_DogDefs.LiveStageNameOf(show.slug);
                string legacyName = KD_DogDefs.LegacyLiveStageNameOf(show.slug);

                // シーン全体・非アクティブ込みで引く (階層に依存しない — CLAUDE.md。生成器は root に置くが、人が動かしていても拾う)
                var fresh = KD_SceneObjects.Find(legacyName, out found);
                if (found > 1)
                {
                    log.AppendLine("   FAIL " + legacyName + " がシーンに " + found + " 個 (import を重ねた?)。手で 1 個にしてから再実行");
                    ok = false;
                    continue;
                }

                int oldCount;
                var stage = KD_SceneObjects.Find(newName, out oldCount);
                if (fresh != null)
                {
                    // **旧を先に消す** — 同名が 2 個ある瞬間を作らない (Find が最初の 1 個を返す作りなので)
                    foreach (var old in FindAllNamed(newName))
                    {
                        Undo.DestroyObjectImmediate(old);
                        replaced++;
                    }
                    if (oldCount > 0) log.AppendLine("   replaced " + newName + " (旧インスタンス " + oldCount + " 個を削除)");
                    Undo.RecordObject(fresh, "adopt stage");
                    fresh.name = newName;
                    Undo.SetTransformParent(fresh.transform, places, "adopt stage");
                    EditorUtility.SetDirty(fresh);
                    adopted++;
                    log.AppendLine("   adopted " + legacyName + " -> " + KD_SceneObjects.PathOf(fresh));
                    stage = fresh;
                }
                else if (stage == null)
                {
                    missing++;
                    log.AppendLine("   (none) " + newName + " — 未 import (vclive.sh import shows/" + show.slug + "/...)");
                    continue;
                }
                else if (oldCount != 1)
                {
                    log.AppendLine("   FAIL " + newName + " が " + oldCount + " 個 (手で 1 個にする)");
                    ok = false;
                    continue;
                }

                if (stage.transform.parent != places)
                {
                    Undo.SetTransformParent(stage.transform, places, "place stage");
                    log.AppendLine("   moved " + stage.name + " -> " + KD_DogDefs.PlacesPath);
                }
                if (PlaceStage(stage))
                {
                    placed++;
                    log.AppendLine("   placed " + stage.name + " local pos=" + KD_DogDefs.LiveStagePositionForName(stage.name)
                        + " rotY=" + KD_DogDefs.LiveStageEulerY);
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("ADOPT RESULT: " + (ok ? "PASS" : "FAIL") + " adopted=" + adopted + " replaced=" + replaced
                + " placed=" + placed + " missing=" + missing + " / " + KD_DogDefs.LiveShows.Length);
            log.AppendLine("**シーンは保存していない。** 次: 2 - Setup Live System → 3 - Verify Live System");
            Finish(log);
        }

        /// <summary>ステージを定数の transform へ (`KD_Places` の local)。変えたら true。
        /// 旧 `KD_TempLocalBuildEditor.PlaceStage` (2026-09-13 に削除) (地面へレイを撃って高さを測っていた) の昇格版 —
        /// 高さは測った値 (-0.39) を定数にした。</summary>
        static bool PlaceStage(GameObject stage)
        {
            var t = stage.transform;
            var wantRot = Quaternion.Euler(0f, KD_DogDefs.LiveStageEulerY, 0f);
            bool same = (t.localPosition - KD_DogDefs.LiveStagePositionForName(stage.name)).sqrMagnitude < 1e-6f
                && Quaternion.Angle(t.localRotation, wantRot) < 0.01f
                && (t.localScale - Vector3.one).sqrMagnitude < 1e-6f;
            if (same) return false;
            Undo.RecordObject(t, "place stage");
            t.localPosition = KD_DogDefs.LiveStagePositionForName(stage.name);
            t.localRotation = wantRot;
            t.localScale = Vector3.one;
            EditorUtility.SetDirty(t);
            return true;
        }

        /// <summary>ステージが定数の transform に居るか (Verify 用。Adopt の PlaceStage と同じ式)。</summary>
        static bool IsStagePlaced(GameObject stage)
        {
            if (stage == null) return false;
            var t = stage.transform;
            return (t.localPosition - KD_DogDefs.LiveStagePositionForName(stage.name)).sqrMagnitude < 1e-6f
                && Quaternion.Angle(t.localRotation, Quaternion.Euler(0f, KD_DogDefs.LiveStageEulerY, 0f)) < 0.01f
                && (t.localScale - Vector3.one).sqrMagnitude < 1e-6f;
        }

        /// <summary>`KD_Places` (置き場所)。**作らない** — 無ければ Scene Layout/2 - Apply が先。</summary>
        static Transform FindPlaces(StringBuilder log, ref bool ok)
        {
            string leaf = KD_DogDefs.PlacesPath.Substring(KD_DogDefs.PlacesPath.LastIndexOf('/') + 1);
            int found;
            var go = KD_SceneObjects.Find(leaf, out found);
            if (go == null || found != 1 || KD_SceneObjects.PathOf(go) != KD_DogDefs.PlacesPath)
            {
                log.AppendLine("FAIL " + KD_DogDefs.PlacesPath + " が取れない (" + found + " 個"
                    + (go != null ? " / " + KD_SceneObjects.PathOf(go) : "") + ")。先に Scene Layout/2 - Apply");
                ok = false;
                return null;
            }
            return go.transform;
        }

        /// <summary>シーン全体 (非アクティブ込み) の同名オブジェクトを全部。**消す側が使う** (Find は最初の 1 個しか返さない)。</summary>
        static List<GameObject> FindAllNamed(string name)
        {
            var list = new List<GameObject>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) list.Add(t.gameObject);
            return list;
        }

        /// <summary>シーンに残った `LiveStage_*` (採用されなかった生成物)。**階層を問わず・非アクティブ込み**で拾う
        /// (採用済み `KD_LiveStage_*` の子孫は生成物の中身なので除く)。</summary>
        static List<GameObject> FindLegacyStages()
        {
            var list = new List<GameObject>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith(KD_DogDefs.LegacyLiveStagePrefix)) continue;
                    bool inside = false;
                    for (var p = t.parent; p != null; p = p.parent)
                        if (p.name.StartsWith(KD_DogDefs.LiveStagePrefix) || p.name.StartsWith(KD_DogDefs.LegacyLiveStagePrefix))
                        { inside = true; break; }
                    if (!inside) list.Add(t.gameObject);
                }
            }
            return list;
        }

        // ---- NetworkIDs の残骸 (vrc-scene-edit-safety「一時オブジェクトの残骸は NetworkIDs に残る」) ----

        /// <summary>`VRCSceneDescriptor.NetworkIDCollection` の gameObject が null のエントリ数。
        /// <paramref name="typed"/> は型情報を持つもの (機械的に消さない)。</summary>
        static int CountNullNetworkIds(out int typed)
        {
            typed = 0;
            var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
            if (desc == null || desc.NetworkIDCollection == null) return 0;
            int n = 0;
            foreach (var e in desc.NetworkIDCollection)
            {
                if (e.gameObject != null) continue;
                n++;
                if (e.SerializedTypeNames != null && e.SerializedTypeNames.Count > 0) typed++;
            }
            return n;
        }

        /// <summary>ステージ / 商品を消したあとの後始末。gameObject が null で型情報も無いエントリだけ消す
        /// (旧 `KD_TempLocalBuildEditor.CleanNetworkIds` と同じ方針。あちらは 2026-09-13 に削除、保存時の掃除は `KD_NetworkIdHygieneEditor`)。型付きは警告に回す。Undo に記録。</summary>
        static void PruneNullNetworkIds(StringBuilder log)
        {
            var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
            if (desc == null || desc.NetworkIDCollection == null) return;
            int removed = 0, warned = 0;
            Undo.RecordObject(desc, "clean NetworkIDs");
            for (int i = desc.NetworkIDCollection.Count - 1; i >= 0; i--)
            {
                var e = desc.NetworkIDCollection[i];
                if (e.gameObject != null) continue;
                if (e.SerializedTypeNames != null && e.SerializedTypeNames.Count > 0)
                {
                    warned++;
                    log.AppendLine("   WARN NetworkIDs id=" + e.ID + " が gameObject null で型情報付き (" + string.Join(",", e.SerializedTypeNames)
                        + ")。人が確認して Temp/Clean NetworkIDs (typed too)");
                    continue;
                }
                desc.NetworkIDCollection.RemoveAt(i);
                removed++;
            }
            if (removed > 0) EditorUtility.SetDirty(desc);
            log.AppendLine("   NetworkIDs 残骸: removed=" + removed + " warned(typed)=" + warned);
        }

        /// <summary>シーン内の `KD_LiveStage_*` のうち表に無いもの (表にある物の子孫は除く)。</summary>
        static List<GameObject> FindStrayStages(HashSet<string> wanted)
        {
            var list = new List<GameObject>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith(KD_DogDefs.LiveStagePrefix) || wanted.Contains(t.name)) continue;
                    bool underWanted = false;
                    for (var p = t.parent; p != null; p = p.parent)
                        if (wanted.Contains(p.name)) { underWanted = true; break; }
                    if (!underWanted) list.Add(t.gameObject);
                }
            }
            return list;
        }

        // ---- 音源 (Streaming) ----

        static string FindAudioPath(string slug)
        {
            string dir = ShowFolderOf(slug);
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            if ((slug == "kd-candystep-002" || slug == "kd-gardenlights-004-lotus-r003")
                && File.Exists(dir + "/master.wav")) return dir + "/master.wav";
            foreach (var f in Directory.GetFiles(dir, "audio.*"))
            {
                if (f.EndsWith(".meta")) continue;
                return f.Replace('\\', '/');
            }
            return null;
        }

        /// <summary>既定 + Android + iOS を Streaming (preload なし) にする。変えたときだけ SaveAndReimport。</summary>
        static bool EnsureAudioStreaming(string slug, StringBuilder log)
        {
            return EnsureAudioStreamingAt(FindAudioPath(slug), ShowFolderOf(slug) + "/audio.*", log);
        }

        /// <summary>音源のパスを渡す版 (裏山は Timeline の AudioTrack から引く。KD_LiveSetupEditor.Hillside.cs)。</summary>
        static bool EnsureAudioStreamingAt(string path, string lookedFor, StringBuilder log)
        {
            if (path == null)
            {
                log.AppendLine("   FAIL 音源が無い: " + lookedFor);
                return false;
            }
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null)
            {
                log.AppendLine("   FAIL AudioImporter が取れない: " + path);
                return false;
            }
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

        /// <summary>Verify 用。既定 + override が全部 Streaming か。<paramref name="detail"/> に実測。</summary>
        static bool IsAudioStreaming(string slug, out string detail)
        {
            return IsAudioStreamingAt(FindAudioPath(slug), out detail);
        }

        /// <summary>音源のパスを渡す版 (Verify 用)。</summary>
        static bool IsAudioStreamingAt(string path, out string detail)
        {
            if (path == null) { detail = "音源なし"; return false; }
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) { detail = "AudioImporter なし"; return false; }
            var sb = new StringBuilder();
            bool ok = imp.defaultSampleSettings.loadType == AudioClipLoadType.Streaming
                && !imp.defaultSampleSettings.preloadAudioData;
            sb.Append("default=" + imp.defaultSampleSettings.loadType);
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

        static void CreateProgramAsset(string scriptPath, string programPath, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath) != null)
            {
                log.AppendLine("already exists: " + programPath);
                return;
            }
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (script == null)
            {
                log.AppendLine("FAIL script not found: " + scriptPath);
                return;
            }
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, programPath);
            AssetDatabase.SaveAssets();
            log.AppendLine("created: " + programPath);
        }

        // ================================================================ 2

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Live System/2 - Setup Live System")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_LiveSetup] Setup");
            bool ok = true;
            int found;

            // ---- ステージ (前提。vrc-live-generator の生成物を 1 - Adopt で採用済み。作品ごとに 1 本) ----
            //
            // **添字 = liveId** (= KD_DogDefs.LiveShows の並び)。1 本でも欠けたら台帳の配線には進まない —
            // 添字がずれた配列を書くと「別の曲が流れる」になり、Verify の長さ検査では見えない
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
                    log.AppendLine("FAIL [" + i + "] " + stageName + " が " + found + " 個 (1 個のはず)。"
                        + "先に vclive.sh import → 1 - Adopt Imported Stages");
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
                log.AppendLine("   [" + i + "] " + stageGo.name + " duration=" + duration.ToString("F1") + "s"
                    + " darkness=" + shows[i].darkness.ToString("F2") + " product=" + shows[i].productKey);

                // 粒の材質: 覆いより上に描く (生成物なので毎回書く。差し替えたら 2 を流し直す)
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

                // 音源: Streaming (既定 + Android / iOS override)
                if (!EnsureAudioStreaming(shows[i].slug, log)) ok = false;

                stageGos[i] = stageGo;
                directors[i] = director;
                durations[i] = duration;
            }
            if (!stagesOk)
            {
                log.AppendLine("SETUP RESULT: FAIL (ステージが揃っていない。台帳の配線は触らない)");
                Finish(log);
                return;
            }

            // ---- 旧方式 (層の制限) の片付け: 複製材質を元へ戻し、複製と草の参照材質を消す ----
            ok &= RestoreLegacyNightQueueCopies(log);

            // ---- 台帳 (Manual・専用 GameObject) ----
            var liveGo = EnsureSingle(KD_DogDefs.LiveSystemName, KD_DogDefs.SystemsPath, log, ref ok);
            if (liveGo == null) { Finish(log); return; }
            var live = liveGo.GetUdonSharpComponent<KD_LiveSystem>();
            if (live == null)
            {
                live = liveGo.AddUdonSharpComponent<KD_LiveSystem>();
                log.AppendLine("   added KD_LiveSystem");
            }
            live.stageDirectors = directors;
            live.stageRoots = stageGos;
            live.stageDurations = durations;
            live.stageDarkness = GetDarkness();
            ok &= KD_SleepAudioSetupEditor.RewireIfPresent(live, log);
            ok &= TryCopy(live, log);
            ok &= EnsureSync(live, KD_DogDefs.LiveSystemName, VRC.SDKBase.Networking.SyncType.Manual, log);

            // ---- 夜 (None) ----
            var nightGo = EnsureSingle(KD_DogDefs.LiveNightName, KD_DogDefs.SystemsPath, log, ref ok);
            if (nightGo == null) { Finish(log); return; }
            var night = nightGo.GetUdonSharpComponent<KD_LiveNight>();
            if (night == null)
            {
                night = nightGo.AddUdonSharpComponent<KD_LiveNight>();
                log.AppendLine("   added KD_LiveNight");
            }
            night.live = live;
            night.overlayRenderer = EnsureOverlay(nightGo, log, ref ok);

            // 木と塀
            var hideR = new List<Renderer>();
            var hideP = new List<ParticleSystem>();
            var tree = KD_SceneObjects.Find(TreeName, out found);
            if (found > 1)
            {
                log.AppendLine("   FAIL " + TreeName + " が " + found + " 個 (0 か 1 個のはず)");
                ok = false;
            }
            else if (tree == null)
            {
                log.AppendLine("   skip " + TreeName + " は無い (桜は任意。IMP-ENV-018)");
            }
            else
            {
                // 花びらの ParticleSystemRenderer も消す (Stop だけだと残粒が寿命まで見える — Codex 指摘)
                foreach (var r in tree.GetComponentsInChildren<Renderer>(true)) hideR.Add(r);
                foreach (var p in tree.GetComponentsInChildren<ParticleSystem>(true)) hideP.Add(p);
            }
            var seasonTree = KD_SceneObjects.Find(SeasonTreeName, out found);
            if (seasonTree == null || found != 1)
            {
                log.AppendLine("   FAIL " + SeasonTreeName + " が " + found + " 個 (1 個のはず)");
                ok = false;
            }
            else
            {
                // 幹 (TreeBare) と葉 (Leaves)。購入 Prefab には書かず Renderer の参照を持つだけ
                foreach (var r in seasonTree.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer)) hideR.Add(r);
            }
            var wall = FindWallRoot(out found);
            if (wall == null)
            {
                log.AppendLine("   FAIL " + PurchasedObjectsRoot + "/" + WallName + " が取れない (" + found + " 個)");
                ok = false;
            }
            else
            {
                foreach (var r in wall.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer)) hideR.Add(r);
            }
            night.hideRenderers = hideR.ToArray();
            night.hideParticles = hideP.ToArray();
            log.AppendLine("   hide renderers=" + hideR.Count + " particles=" + hideP.Count);

            // 空 (材質は 1 枚。露出と色味で暗くする。IMP-ENV-010)
            night.skyMaterial = EnsureSkybox(log, ref ok);

            // SleepKit ナイトモードとの積の下限 (破壊的監査 v4 FUNC-09/BUG-02)。Menu Finish 未実行なら空のまま (LiveNight は縮めない)
            var menuSettingsGo = KD_SceneObjects.Find(MenuSettingsName, out found);
            if (menuSettingsGo == null || found != 1)
            {
                night.menuSettings = null;
                log.AppendLine("   skip " + MenuSettingsName + " が " + found + " 個 (Menu Finish の後にもう一度流すと配線される)");
            }
            else
            {
                night.menuSettings = menuSettingsGo.GetUdonSharpComponent<KD_MenuSettings>();
                if (night.menuSettings == null) { log.AppendLine("   FAIL " + MenuSettingsName + " に KD_MenuSettings が無い"); ok = false; }
            }

            ok &= TryCopy(night, log);
            ok &= EnsureSync(night, KD_DogDefs.LiveNightName, VRC.SDKBase.Networking.SyncType.None, log);

            // ---- 犬の観客参加 (Phase 2) ----
            //
            // 集合地点の**向きは設計が持つ** (= ステージの向き) ので Setup が毎回写す。
            // 位置は人が置いたものを触らない
            var spotGo = KD_SceneObjects.Find(KD_DogDefs.LiveDogSpotName, out found);
            if (spotGo == null) spotGo = KD_SceneObjects.Find(KD_DogDefs.LegacyLiveDogSpotName, out found);
            if (spotGo == null || found != 1)
            {
                log.AppendLine("   FAIL 集合地点が取れない (" + KD_DogDefs.LiveDogSpotName + " / "
                    + KD_DogDefs.LegacyLiveDogSpotName + " が " + found + " 個) - 犬は集まらない");
                ok = false;
            }
            else
            {
                // 全曲同じ向き (KD_DogDefs.LiveStageEulerY) なので定数から写す (ステージ個体の値に依存しない)
                var stageRot = Quaternion.Euler(0f, KD_DogDefs.LiveStageEulerY, 0f);
                if (Quaternion.Angle(spotGo.transform.rotation, stageRot) > 0.5f)
                {
                    Undo.RecordObject(spotGo.transform, "face stage");
                    spotGo.transform.rotation = stageRot;
                    log.AppendLine("   " + spotGo.name + " の向きをステージに合わせた (forward = ステージの +Z。Y=" + KD_DogDefs.LiveStageEulerY + ")");
                }
                foreach (var def in KD_DogDefs.All)
                {
                    var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                    var wander = (found == 1 && wanderGo != null)
                        ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                    if (wander == null)
                    {
                        log.AppendLine("   FAIL " + def.WanderSystemName + " が取れない (" + found + " 個) - この犬は集まらない");
                        ok = false;
                    }
                    else if (wander.liveSystem != live || wander.liveSpot != spotGo.transform)
                    {
                        wander.liveSystem = live;
                        wander.liveSpot = spotGo.transform;
                        ok &= TryCopy(wander, log);
                        log.AppendLine("   wired " + def.WanderSystemName + ".liveSystem / .liveSpot");
                    }

                    var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out found);
                    var fetch = (found == 1 && fetchGo != null)
                        ? fetchGo.GetUdonSharpComponent<KD_DogFetchController>() : null;
                    if (fetch == null)
                    {
                        log.AppendLine("   FAIL " + def.FetchSystemName + " が取れない (" + found + " 個) - この犬は Live 中も投擲に走る");
                        ok = false;
                    }
                    else if (fetch.liveSystem != live)
                    {
                        fetch.liveSystem = live;
                        ok &= TryCopy(fetch, log);
                        log.AppendLine("   wired " + def.FetchSystemName + ".liveSystem");
                    }
                }
            }

            // ---- 後始末: 表に無い KD_LiveStage_* / root に残った LiveStage_* (IMP-PRG-005) ----
            //
            // **表にある slug の root `LiveStage_*` は消さない** — それは「import したが 1 - Adopt を流していない」
            // 状態で、消すと import が黙って失われる。FAIL で止めて Adopt へ回す
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
                        log.AppendLine("   FAIL " + KD_SceneObjects.PathOf(leftover) + " が残っている (表の曲) — 先に 1 - Adopt Imported Stages");
                        ok = false;
                        continue;
                    }
                    log.AppendLine("   removed " + KD_SceneObjects.PathOf(leftover) + " (残った表に無い生成物)");
                    Undo.DestroyObjectImmediate(leftover);
                }
                // 消したステージの NetworkIDs エントリ (gameObject null) を掃除
                PruneNullNetworkIds(log);
            }

            // ---- 代役ボタン (KD_Debug) は 2026-09-06 に廃止 (ライブタブができた) → 残っていれば消す ----
            foreach (var legacyName in new[] { "KD_LiveDebug_Start", "KD_LiveDebug_Stop", "KD_LiveDebug_AvatarDark" })
            {
                var legacy = KD_SceneObjects.Find(legacyName, out found);
                if (legacy == null) continue;
                Undo.DestroyObjectImmediate(legacy);
                log.AppendLine("   removed " + legacyName + " (代役ボタンは廃止)");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("**シーンは保存していない。** 3 - Verify Live System と Scene Layout/2 - Apply → 3 - Verify を通してから保存する");
            Finish(log);
        }

        // ================================================================ 3

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Live System/3 - Verify Live System")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_LiveSetup] Verify");
            int fail = 0;
            int found;

            var sysPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(SystemProgramPath);
            var nightPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(NightProgramPath);
            Check(ref fail, log, sysPa != null && sysPa.sourceCsScript != null, "program asset ok: KD_LiveSystem");
            Check(ref fail, log, nightPa != null && nightPa.sourceCsScript != null, "program asset ok: KD_LiveNight");
            Check(ref fail, log, sysPa != null && sysPa.behaviourSyncMode == BehaviourSyncMode.Manual,
                "KD_LiveSystem の behaviourSyncMode == Manual");
            Check(ref fail, log, nightPa != null && nightPa.behaviourSyncMode == BehaviourSyncMode.None,
                "KD_LiveNight の behaviourSyncMode == None");

            // ---- 表 (IMP-PRG-005): slug 一意 ----
            var shows = KD_DogDefs.LiveShows;
            int showCount = shows.Length;
            var wantedStages = new HashSet<string>();
            {
                var slugs = new HashSet<string>();
                bool uniq = true;
                foreach (var s in shows)
                {
                    if (!slugs.Add(s.slug)) uniq = false;
                    wantedStages.Add(KD_DogDefs.LiveStageNameOf(s.slug));
                }
                Check(ref fail, log, showCount > 0 && uniq, "LiveShows の slug が一意 (" + showCount + " 本)");
            }

            // ---- ステージ (作品ごと。添字 = liveId) ----
            var stageGos = new GameObject[showCount];
            var directors = new PlayableDirector[showCount];
            for (int i = 0; i < showCount; i++)
            {
                string tag = "[" + i + " " + shows[i].slug + "] ";
                string stageName = KD_DogDefs.LiveStageNameOf(shows[i].slug);
                var stageGo = KD_SceneObjects.Find(stageName, out found);
                Check(ref fail, log, stageGo != null && found == 1, tag + stageName + " が 1 個 (actual " + found + "。重複 0)");
                if (stageGo == null) continue;
                stageGos[i] = stageGo;
                var director = stageGo.GetComponent<PlayableDirector>();
                directors[i] = director;
                Check(ref fail, log, director != null && director.playableAsset != null, tag + "PlayableDirector + playableAsset");
                Check(ref fail, log, director != null && !director.playOnAwake, tag + "playOnAwake == false");
                Check(ref fail, log, !stageGo.activeSelf, tag + "inactive (台帳が ON にする)");
                Check(ref fail, log, ParentPathOf(stageGo) == KD_DogDefs.PlacesPath,
                    tag + KD_DogDefs.PlacesPath + " の直下 (actual " + ParentPathOf(stageGo) + "。違えば 1 - Adopt)");
                Check(ref fail, log, IsStagePlaced(stageGo),
                    tag + "transform == 定数 (pos " + KD_DogDefs.LiveStagePositionForName(stageGo.name) + " / rotY " + KD_DogDefs.LiveStageEulerY
                    + "。actual " + stageGo.transform.localPosition + " / " + stageGo.transform.localEulerAngles.y.ToString("F0") + ")");

                int missingPrefab = 0;
                foreach (var t in stageGo.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.GetPrefabInstanceStatus(t.gameObject) == PrefabInstanceStatus.MissingAsset) missingPrefab++;
                Check(ref fail, log, missingPrefab == 0, tag + "Missing Prefab == 0 (actual " + missingPrefab + "。再 import で GUID が変わった旧インスタンス)");

                int low = 0, total = 0;
                var seen = new HashSet<Material>();
                foreach (var r in stageGo.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || !seen.Add(m)) continue;
                        total++;
                        if (m.renderQueue != LiveParticleQueue) low++;
                    }
                Check(ref fail, log, total > 0 && low == 0,
                    tag + "粒の材質 " + total + " 本すべて renderQueue == " + LiveParticleQueue + " (違反: " + low + ")");

                string audioDetail;
                Check(ref fail, log, IsAudioStreaming(shows[i].slug, out audioDetail),
                    tag + "音源が Streaming (既定 + " + string.Join("/", AudioOverridePlatforms) + "。actual " + audioDetail + ")");
            }
            {
                var leftovers = FindLegacyStages();
                var names = new StringBuilder();
                foreach (var l in leftovers) names.Append(KD_SceneObjects.PathOf(l) + " ");
                Check(ref fail, log, leftovers.Count == 0, "シーンに LiveStage_* が残っていない (階層を問わず。actual " + leftovers.Count + " " + names + ")");
                var strays = FindStrayStages(wantedStages);
                names.Length = 0;
                foreach (var s in strays) names.Append(KD_SceneObjects.PathOf(s) + " ");
                Check(ref fail, log, strays.Count == 0, "表に無い KD_LiveStage_* が無い (actual " + strays.Count + " " + names + ")");
                // 消したステージ / 旧商品の残骸 (欠損参照)。Setup の後始末が消す。型付きは Temp/Clean NetworkIDs (typed too)
                int typedNull;
                int nullIds = CountNullNetworkIds(out typedNull);
                Check(ref fail, log, nullIds == 0, "NetworkIDs に gameObject null のエントリが無い (actual " + nullIds
                    + "。うち型付き " + typedNull + ")");
            }

            // ---- 商品 (IMP-PRG-005): productKey 一意 / 範囲 / 他カテゴリと非衝突 / 各曲の商品が存在 / 欠番 24 ----
            {
                var products = KD_DogDefs.LiveProducts;
                var productIds = new HashSet<string>();
                bool uniq = true, range = true;
                foreach (var p in products)
                {
                    if (!productIds.Add(p.productKey)) uniq = false;
                    if (KD_ProductDefinitions.Get(p.productKey).categoryKey != "live") range = false;
                }
                Check(ref fail, log, uniq, "LiveProducts の productKey が一意 (" + products.Length + " 商品)");
                Check(ref fail, log, range, "Live products use registered keys");
                var allItems = Object.FindObjectsOfType<KD_ShopItem>(true);
                int clash = 0;
                var clashNames = new StringBuilder();
                foreach (var it in allItems)
                    if (it.categoryKey != KD_ShopSetupEditor.CategoryLive && productIds.Contains(it.productKey))
                    { clash++; clashNames.Append(it.gameObject.name + " "); }
                Check(ref fail, log, clash == 0, "Live の productKey が他カテゴリの KD_ShopItem と衝突していない (actual " + clash + " " + clashNames + ")");
                foreach (var s in shows)
                {
                    Check(ref fail, log, productIds.Contains(s.productKey),
                        "[" + s.slug + "] productKey " + s.productKey + " が LiveProducts にある");
                    int c = 0;
                    foreach (var it in allItems)
                        if (it.categoryKey == KD_ShopSetupEditor.CategoryLive && it.productKey == s.productKey) c++;
                    Check(ref fail, log, c == 1, "[" + s.slug + "] 商品 " + KD_ShopSetupEditor.LiveItemName(s.productKey)
                        + " がシーンに 1 個 (actual " + c + "。Shop/2 - Setup が作る)");
                }
                Check(ref fail, log, KD_SceneObjects.Find("KD_ShopItem_Live_24") == null,
                    "欠番 " + RetiredUnlockId + " の商品 (" + "KD_ShopItem_Live_24" + ") が無い");
                Check(ref fail, log, KD_SceneObjects.Find(KD_DogDefs.LiveStageNameOf(RetiredSlug)) == null
                    && KD_SceneObjects.Find(KD_DogDefs.LegacyLiveStageNameOf(RetiredSlug)) == null,
                    "旧作 " + RetiredSlug + " のステージが無い");
                Check(ref fail, log, !AssetDatabase.IsValidFolder(ShowFolderOf(RetiredSlug)),
                    "旧作のフォルダ " + ShowFolderOf(RetiredSlug) + " が無い (Phase 5 で git rm)");
            }

            // 旧方式の残骸が無い
            Check(ref fail, log, !AssetDatabase.IsValidFolder(LegacyNightQueueFolder),
                "旧方式の複製材質 (" + LegacyNightQueueFolder + ") が無い (Setup が元へ戻して消す)");
            Check(ref fail, log, AssetDatabase.LoadAssetAtPath<Material>(LegacyGrassKeeperPath) == null,
                "旧方式の草の参照材質が無い");
            Check(ref fail, log, CountLegacyCopySlots() == 0,
                "世界の Renderer に旧方式の複製材質が刺さっていない (actual " + CountLegacyCopySlots() + ")");

            // 台帳
            var liveGo = KD_SceneObjects.Find(KD_DogDefs.LiveSystemName, out found);
            Check(ref fail, log, liveGo != null && found == 1, KD_DogDefs.LiveSystemName + " が 1 個 (actual " + found + ")");
            var live = liveGo != null ? liveGo.GetUdonSharpComponent<KD_LiveSystem>() : null;
            if (Check(ref fail, log, live != null, KD_DogDefs.LiveSystemName + ": KD_LiveSystem が付いている"))
            {
                Check(ref fail, log, liveGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == (live.activities != null ? 2 : 1),
                    KD_DogDefs.LiveSystemName + " は Live と配線済みの共有開催だけを持つ");
                // IMP-NET-007: 本番の停止は共有開催の認証済み要求 (_StopFor) だけ。activities 未配線の直書き経路はプレビューシーン専用
                Check(ref fail, log, live.activities != null,
                    KD_DogDefs.LiveSystemName + ".activities が配線済み (停止・開催の認可は KD_GroupActivities 経由)");
                Check(ref fail, log, live.access != null && live.access.sleepBoard != null
                    && live.access.sleepBoard.gameObject == KD_SceneObjects.Find(KD_DogDefs.SleepBoardName),
                    KD_DogDefs.LiveSystemName + ".access.sleepBoard が寝る台帳 (管理者判定の正本。オーナー優先、不在時だけ Master)");
                Check(ref fail, log, liveGo.GetComponent<VRCObjectSync>() == null,
                    KD_DogDefs.LiveSystemName + " に VRCObjectSync が無い");
                Check(ref fail, log, SceneSyncOf(liveGo) == VRC.SDKBase.Networking.SyncType.Manual,
                    KD_DogDefs.LiveSystemName + " の UdonBehaviour.SyncMethod == Manual (actual " + SceneSyncOf(liveGo) + ")");
                Check(ref fail, log, ParentPathOf(liveGo) == KD_DogDefs.SystemsPath,
                    KD_DogDefs.LiveSystemName + " が " + KD_DogDefs.SystemsPath + " の直下 (actual " + ParentPathOf(liveGo) + ")");
                // **stage* 4 配列の長さが全部 N** (KD_LiveSystem.Start が揃っていないと止まる)
                bool lengths = live.stageDirectors != null && live.stageDirectors.Length == showCount
                    && live.stageRoots != null && live.stageRoots.Length == showCount
                    && live.stageDurations != null && live.stageDurations.Length == showCount
                    && live.stageDarkness != null && live.stageDarkness.Length == showCount;
                Check(ref fail, log, lengths, KD_DogDefs.LiveSystemName + ".stage* 4 配列の長さ == " + showCount + " (actual "
                    + (live.stageDirectors == null ? -1 : live.stageDirectors.Length) + "/"
                    + (live.stageRoots == null ? -1 : live.stageRoots.Length) + "/"
                    + (live.stageDurations == null ? -1 : live.stageDurations.Length) + "/"
                    + (live.stageDarkness == null ? -1 : live.stageDarkness.Length) + ")");
                for (int i = 0; lengths && i < showCount; i++)
                {
                    string tag = "[" + i + " " + shows[i].slug + "] ";
                    Check(ref fail, log, directors[i] != null && live.stageDirectors[i] == directors[i]
                        && stageGos[i] != null && live.stageRoots[i] == stageGos[i],
                        tag + KD_DogDefs.LiveSystemName + ".stageDirectors / stageRoots がこのステージ (添字 = liveId)");
                    Check(ref fail, log, directors[i] != null
                        && Mathf.Abs(live.stageDurations[i] - PlaybackDuration(shows[i].slug, directors[i])) < 0.01f,
                        tag + "stageDurations == playback length (actual " + live.stageDurations[i].ToString("F1") + ")");
                }
                var wantDark = GetDarkness();
                bool darkOk = live.stageDarkness != null && live.stageDarkness.Length == wantDark.Length;
                for (int i = 0; darkOk && i < wantDark.Length; i++)
                    if (Mathf.Abs(live.stageDarkness[i] - wantDark[i]) > 0.001f) darkOk = false;
                Check(ref fail, log, darkOk,
                    KD_DogDefs.LiveSystemName + ".stageDarkness が品ぞろえ表どおり (作品ごとの暗さ。actual "
                    + (live.stageDarkness == null ? "-" : string.Join(",", live.stageDarkness)) + ")");
            }

            // 夜
            var nightGo = KD_SceneObjects.Find(KD_DogDefs.LiveNightName, out found);
            Check(ref fail, log, nightGo != null && found == 1, KD_DogDefs.LiveNightName + " が 1 個 (actual " + found + ")");
            var night = nightGo != null ? nightGo.GetUdonSharpComponent<KD_LiveNight>() : null;
            if (Check(ref fail, log, night != null, KD_DogDefs.LiveNightName + ": KD_LiveNight が付いている"))
            {
                Check(ref fail, log, SceneSyncOf(nightGo) == VRC.SDKBase.Networking.SyncType.None,
                    KD_DogDefs.LiveNightName + " の UdonBehaviour.SyncMethod == None (actual " + SceneSyncOf(nightGo) + ")");
                Check(ref fail, log, ParentPathOf(nightGo) == KD_DogDefs.SystemsPath,
                    KD_DogDefs.LiveNightName + " が " + KD_DogDefs.SystemsPath + " の直下 (actual " + ParentPathOf(nightGo) + ")");
                Check(ref fail, log, night.live != null && night.live == live, KD_DogDefs.LiveNightName + ".live 配線");
                // SleepKit ナイトモードとの積 (破壊的監査 v4 FUNC-09/BUG-02): 設定があるなら配線されていること + 上限の純関数
                int menuFound;
                var menuSettingsGo = KD_SceneObjects.Find(MenuSettingsName, out menuFound);
                if (menuFound == 1 && menuSettingsGo != null)
                    Check(ref fail, log, night.menuSettings != null && night.menuSettings.gameObject == menuSettingsGo,
                        KD_DogDefs.LiveNightName + ".menuSettings が " + MenuSettingsName + " を指す (Live の夜 × ナイトモードの下限)");
                float floorF = Mathf.Pow(KD_MenuSettings.MinNightBrightness, 2.5f);
                Check(ref fail, log, night.MaxDarknessUnderSleepKit(1f) >= 1f, "ナイトモード OFF では Live の暗さを縮めない");
                Check(ref fail, log, night.MaxDarknessUnderSleepKit(floorF) <= 0f, "ナイトモード 100% (明るさ 0.4) では Live の夜を重ねない");
                float half = Mathf.Pow(Mathf.Lerp(1f, KD_MenuSettings.MinNightBrightness, 0.5f), 2.5f); // = DarkToBrightness(0.5)
                float dHalf = night.MaxDarknessUnderSleepKit(half);
                Check(ref fail, log, dHalf > 0f && half * Mathf.Lerp(1f, KD_LiveNight.OverlayTintMin, dHalf) >= floorF - 0.001f,
                    "ナイトモード 50% では積が下限 (" + floorF.ToString("0.000") + ") を割らない (上限 " + dHalf.ToString("0.00") + ")");
                // IMP-ENV-018: 期待数はシーンから数える (秋の木 2 (幹 + 葉) + 外壁 5、桜があれば + 2 (Mesh + 花びら))。
                // 数える前に対象の個数を Setup と同じ条件で検査する (Codex 指摘: 数だけだと重複・外壁欠落を見逃す)
                int sakuraFound, seasonTreeFound, wallFound;
                KD_SceneObjects.Find(TreeName, out sakuraFound);
                KD_SceneObjects.Find(SeasonTreeName, out seasonTreeFound);
                var wallRoot = FindWallRoot(out wallFound);
                Check(ref fail, log, sakuraFound <= 1, TreeName + " は 0 か 1 個 (桜は任意。actual " + sakuraFound + ")");
                Check(ref fail, log, seasonTreeFound == 1, SeasonTreeName + " が 1 個 (actual " + seasonTreeFound + ")");
                Check(ref fail, log, wallRoot != null, PurchasedObjectsRoot + "/" + WallName + " がある (actual " + wallFound + ")");
                bool hasSakura;
                int expectRenderers = ExpectedHideRendererCount(out hasSakura);
                Check(ref fail, log, night.hideRenderers != null && night.hideRenderers.Length == expectRenderers,
                    "hideRenderers == " + expectRenderers + " (秋の木 + 外壁" + (hasSakura ? " + 桜" : "、桜なし") + "。actual " + (night.hideRenderers == null ? -1 : night.hideRenderers.Length) + ")");
                bool hideAllSet = night.hideRenderers != null;
                if (night.hideRenderers != null)
                    foreach (var r in night.hideRenderers) if (r == null) hideAllSet = false;
                Check(ref fail, log, hideAllSet, "hideRenderers に null が無い (桜を外した後に Setup を流し直している)");
                bool hasSeasonTree = false;
                var seasonTreeGo = KD_SceneObjects.Find(SeasonTreeName);
                if (night.hideRenderers != null && seasonTreeGo != null)
                    foreach (var r in night.hideRenderers)
                        if (r != null && r.transform.IsChildOf(seasonTreeGo.transform)) hasSeasonTree = true;
                Check(ref fail, log, hasSeasonTree, "hideRenderers に " + SeasonTreeName + " の Renderer が入っている (IMP-ENV-018)");
                Check(ref fail, log, night.hideParticles != null && (hasSakura ? night.hideParticles.Length >= 1 : night.hideParticles.Length == 0),
                    (hasSakura ? "hideParticles >= 1 (花びら" : "hideParticles == 0 (桜なし") + "。actual " + (night.hideParticles == null ? -1 : night.hideParticles.Length) + ")");
                bool hideEnabled = true;
                if (night.hideRenderers != null)
                    foreach (var r in night.hideRenderers) if (r != null && !r.enabled) hideEnabled = false;
                Check(ref fail, log, hideEnabled, "木と塀の Renderer は Editor では enabled (Play 中に落ちたまま保存していない)");
                Check(ref fail, log, night.skyMaterial != null && night.skyMaterial == RenderSettings.skybox,
                    "skyMaterial == 今の RenderSettings.skybox");
                Check(ref fail, log, night.skyMaterial != null
                    && AssetDatabase.GetAssetPath(night.skyMaterial) == SkyboxCopyPath,
                    "空の材質はプロジェクト側の複製 (購入アセットを実行時に書かない。actual "
                    + (night.skyMaterial == null ? "-" : AssetDatabase.GetAssetPath(night.skyMaterial)) + ")");
                Check(ref fail, log, night.skyMaterial != null && night.skyMaterial.HasProperty("_Exposure")
                    && Mathf.Abs(night.skyMaterial.GetFloat("_Exposure") - KD_LiveNight.DaySkyExposure) < 0.001f,
                    "空の露出 == " + KD_LiveNight.DaySkyExposure + " (Play 中に夜のまま保存していない。actual "
                    + (night.skyMaterial != null && night.skyMaterial.HasProperty("_Exposure") ? night.skyMaterial.GetFloat("_Exposure").ToString("F2") : "-") + ")");
                Check(ref fail, log, AssetDatabase.LoadAssetAtPath<Material>(LegacyNightSkyboxPath) == null,
                    "旧 " + LegacyNightSkyboxPath + " が消えている (Day Twilight の 6 面がビルドから外れる)");
                Check(ref fail, log, night.overlayRenderer != null
                    && night.overlayRenderer.transform.parent == nightGo.transform
                    && night.overlayRenderer.sharedMaterial != null
                    && night.overlayRenderer.sharedMaterial.shader != null
                    && night.overlayRenderer.sharedMaterial.shader.name == "KominkaDogRun/KD_NightOverlay",
                    "overlayRenderer が子で KD_NightOverlay の材質");
                Check(ref fail, log, night.overlayRenderer != null && night.overlayRenderer.sharedMaterial != null
                    && night.overlayRenderer.sharedMaterial.renderQueue == KD_LiveNight.OverlayQueue,
                    "覆いの材質の renderQueue == " + KD_LiveNight.OverlayQueue + " (粒以外の全部を暗くする)");
                Check(ref fail, log, night.overlayRenderer != null && !night.overlayRenderer.enabled,
                    "overlayRenderer は Editor では disabled");
                Check(ref fail, log, night.overlayRenderer != null
                    && night.overlayRenderer.GetComponent<Collider>() == null,
                    "覆いの板に Collider が無い");
                Check(ref fail, log, night.overlayRenderer != null && night.overlayRenderer.GetComponent<MeshFilter>() != null
                    && night.overlayRenderer.GetComponent<MeshFilter>().sharedMesh != null
                    && night.overlayRenderer.GetComponent<MeshFilter>().sharedMesh.name == FullscreenQuadName,
                    "覆いのメッシュが " + FullscreenQuadName + " (画面全体。bounds 巨大 = どのカメラでもカリングされない)");
            }

            // 犬の観客参加 (Phase 2)
            var spotGo = KD_SceneObjects.Find(KD_DogDefs.LiveDogSpotName, out found);
            Check(ref fail, log, spotGo != null && found == 1, KD_DogDefs.LiveDogSpotName + " が 1 個 (actual " + found + ")");
            if (spotGo != null)
            {
                Check(ref fail, log, ParentPathOf(spotGo) == KD_DogDefs.PlacesPath,
                    KD_DogDefs.LiveDogSpotName + " が " + KD_DogDefs.PlacesPath + " の直下 (actual " + ParentPathOf(spotGo) + ")");
                Check(ref fail, log,
                    Quaternion.Angle(spotGo.transform.rotation, Quaternion.Euler(0f, KD_DogDefs.LiveStageEulerY, 0f)) <= 0.5f,
                    KD_DogDefs.LiveDogSpotName + " の向き == ステージの向き (Y=" + KD_DogDefs.LiveStageEulerY
                    + "。犬はこの forward を向いて伏せる。actual " + spotGo.transform.eulerAngles.y.ToString("F0") + ")");
            }
            foreach (var def in KD_DogDefs.All)
            {
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out found);
                var wander = (found == 1 && wanderGo != null)
                    ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (Check(ref fail, log, wander != null, def.WanderSystemName + " が取れる (actual " + found + " 個)"))
                    Check(ref fail, log, wander.liveSystem != null && wander.liveSystem == live
                        && spotGo != null && wander.liveSpot == spotGo.transform,
                        def.WanderSystemName + ".liveSystem / .liveSpot 配線 (犬はここから IsPlaying を読む)");
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out found);
                var fetch = (found == 1 && fetchGo != null)
                    ? fetchGo.GetUdonSharpComponent<KD_DogFetchController>() : null;
                if (Check(ref fail, log, fetch != null, def.FetchSystemName + " が取れる (actual " + found + " 個)"))
                    Check(ref fail, log, fetch.liveSystem != null && fetch.liveSystem == live,
                        def.FetchSystemName + ".liveSystem 配線 (**Live 中は投擲に反応しない**)");
            }

            // 代役ボタンは廃止 (開催は手元メニューのライブタブ)
            foreach (var legacyName in new[] { "KD_LiveDebug_Start", "KD_LiveDebug_Stop", "KD_LiveDebug_AvatarDark" })
            {
                KD_SceneObjects.Find(legacyName, out found);
                Check(ref fail, log, found == 0, legacyName + " が無い (代役ボタンは廃止)");
            }

            // 置き場所の正本
            int rows = 0;
            foreach (var r in KD_DogDefs.LayoutTable)
            {
                if (r.name == KD_DogDefs.LiveSystemName && r.containerPath == KD_DogDefs.SystemsPath) rows++;
                if (r.name == KD_DogDefs.LiveNightName && r.containerPath == KD_DogDefs.SystemsPath) rows++;
                if (wantedStages.Contains(r.name) && r.containerPath == KD_DogDefs.PlacesPath) rows++;
                if (r.name == KD_DogDefs.LiveDogSpotName && r.containerPath == KD_DogDefs.PlacesPath) rows++;
            }
            Check(ref fail, log, rows == 3 + showCount,
                "LayoutTable に " + (3 + showCount) + " 行 (台帳 / 夜 / 集合地点 + ステージ " + showCount + "。actual " + rows + ")");

            log.AppendLine(fail == 0 ? "VERIFY RESULT: PASS" : "VERIFY RESULT: FAIL (" + fail + " checks)");
            Finish(log);
        }

        // ---- 部品 ----

        /// <summary>Verify 用: Setup と同じ規則で数えた Live 中に消す Renderer の期待数
        /// (桜 (あれば) の全 Renderer + 秋の木の非 Particle Renderer + 外壁の非 Particle Renderer)。
        /// 個数の条件 (桜 ≤ 1 / 秋の木 == 1 / 外壁あり) は見ない — 呼び手が先に Check する。</summary>
        static int ExpectedHideRendererCount(out bool hasSakura)
        {
            int n = 0;
            int found;
            var sakura = KD_SceneObjects.Find(TreeName, out found);
            hasSakura = sakura != null;
            if (sakura != null) n += sakura.GetComponentsInChildren<Renderer>(true).Length;
            var seasonTree = KD_SceneObjects.Find(SeasonTreeName, out found);
            if (seasonTree != null)
                foreach (var r in seasonTree.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer)) n++;
            var wall = FindWallRoot(out found);
            if (wall != null)
                foreach (var r in wall.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer)) n++;
            return n;
        }

        /// <summary>`====オブジェクト====/外壁` (親の方)。同名の子 (`外壁/外壁`) は拾わない。</summary>
        static GameObject FindWallRoot(out int found)
        {
            found = 0;
            GameObject hit = null;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != PurchasedObjectsRoot) continue;
                foreach (Transform c in root.transform)
                    if (c.name == WallName) { found++; if (hit == null) hit = c.gameObject; }
            }
            return hit;
        }

        // ---- 旧方式 (層の制限) の片付け ----

        static bool IsLegacyCopy(Material m)
        {
            var path = AssetDatabase.GetAssetPath(m);
            return path != null && path.StartsWith(LegacyNightQueueFolder + "/");
        }

        static int CountLegacyCopySlots()
        {
            if (!AssetDatabase.IsValidFolder(LegacyNightQueueFolder)) return 0;
            int n = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && IsLegacyCopy(m)) n++;
            return n;
        }

        /// <summary>複製材質 (`{元の名前}__{元 GUID 先頭 8 字}_{hex}.mat`) を元の材質へ戻し、複製と草の参照材質を消す。
        /// 元は「GUID 先頭 8 字が一致するアセットの中で同名の Material」で引く (FBX 埋め込みの材質も同じ手で戻る)。</summary>
        static bool RestoreLegacyNightQueueCopies(StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(LegacyNightQueueFolder))
            {
                if (AssetDatabase.LoadAssetAtPath<Material>(LegacyGrassKeeperPath) != null)
                {
                    AssetDatabase.DeleteAsset(LegacyGrassKeeperPath);
                    log.AppendLine("   removed " + LegacyGrassKeeperPath);
                }
                return true;
            }
            // GUID 先頭 8 字 → 候補の材質 (同じアセット内の Material 全部)
            var byPrefix = new Dictionary<string, List<Material>>();
            string[] allGuids = AssetDatabase.FindAssets("");
            int restored = 0, unresolved = 0;
            var unresolvedNames = new StringBuilder();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || !IsLegacyCopy(m)) continue;
                        string file = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m));
                        int sep = file.LastIndexOf("__");
                        if (sep < 0) { unresolved++; unresolvedNames.Append(file + " "); continue; }
                        string origName = file.Substring(0, sep);
                        string key = file.Substring(sep + 2);
                        string prefix = key.Length >= 8 ? key.Substring(0, 8) : key;
                        List<Material> candidates;
                        if (!byPrefix.TryGetValue(prefix, out candidates))
                        {
                            candidates = new List<Material>();
                            foreach (var guid in allGuids)
                            {
                                if (!guid.StartsWith(prefix)) continue;
                                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                                    if (a is Material) candidates.Add((Material)a);
                            }
                            byPrefix[prefix] = candidates;
                        }
                        Material orig = null;
                        foreach (var c in candidates) if (c != null && c.name == origName) { orig = c; break; }
                        if (orig == null && origName == "Default-Material")
                            orig = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                        if (orig == null) { unresolved++; unresolvedNames.Append(file + " "); continue; }
                        mats[i] = orig;
                        changed = true;
                        restored++;
                    }
                    if (changed)
                    {
                        Undo.RecordObject(r, "restore materials");
                        r.sharedMaterials = mats;
                        EditorUtility.SetDirty(r);
                    }
                }
            }
            log.AppendLine("   legacy copies restored=" + restored + " unresolved=" + unresolved
                + (unresolved > 0 ? " (" + unresolvedNames + ")" : ""));
            if (unresolved > 0) return false;   // 複製を消すと参照が切れるので残す
            AssetDatabase.DeleteAsset(LegacyNightQueueFolder);
            if (AssetDatabase.LoadAssetAtPath<Material>(LegacyGrassKeeperPath) != null)
                AssetDatabase.DeleteAsset(LegacyGrassKeeperPath);
            log.AppendLine("   removed " + LegacyNightQueueFolder + " / " + LegacyGrassKeeperPath);
            return true;
        }

        // ---- 覆い ----

        static Renderer EnsureOverlay(GameObject nightGo, StringBuilder log, ref bool ok)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(OverlayShaderPath);
            if (shader == null) { log.AppendLine("   FAIL シェーダが無い: " + OverlayShaderPath); ok = false; return null; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OverlayMaterialPath));
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, OverlayMaterialPath);
                log.AppendLine("   created " + OverlayMaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }
            if (mat.renderQueue != KD_LiveNight.OverlayQueue) { mat.renderQueue = KD_LiveNight.OverlayQueue; EditorUtility.SetDirty(mat); }

            Transform ot = nightGo.transform.Find(KD_DogDefs.LiveNightOverlayName);
            GameObject overlay;
            if (ot == null)
            {
                overlay = GameObject.CreatePrimitive(PrimitiveType.Cube);
                overlay.name = KD_DogDefs.LiveNightOverlayName;
                Undo.RegisterCreatedObjectUndo(overlay, "Create " + overlay.name);
                Undo.SetTransformParent(overlay.transform, nightGo.transform, "Place overlay");
                overlay.transform.localPosition = Vector3.zero;
                overlay.transform.localRotation = Quaternion.identity;
                var col = overlay.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                log.AppendLine("   created " + overlay.name);
            }
            else overlay = ot.gameObject;

            // **画面いっぱいの板** (頂点 xy をクリップ座標に使う)。bounds を巨大にしてどのカメラでもカリングされないようにする
            var mf = overlay.GetComponent<MeshFilter>();
            var quad = EnsureFullscreenQuad(log);
            if (mf != null && quad != null && mf.sharedMesh != quad) { mf.sharedMesh = quad; EditorUtility.SetDirty(mf); }
            overlay.transform.localScale = Vector3.one;

            var mr = overlay.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.enabled = false;
            GameObjectUtility.SetStaticEditorFlags(overlay, 0);
            EditorUtility.SetDirty(mr);
            return mr;
        }

        /// <summary>画面いっぱいの板 (xy -1〜1 の 4 頂点)。bounds を巨大にして常に描かれるようにする。</summary>
        static Mesh EnsureFullscreenQuad(StringBuilder log)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FullscreenQuadPath);
            if (mesh == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FullscreenQuadPath));
                mesh = new Mesh();
                mesh.name = FullscreenQuadName;
                mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200000f);
                AssetDatabase.CreateAsset(mesh, FullscreenQuadPath);
                log.AppendLine("   created " + FullscreenQuadPath);
            }
            else if (mesh.bounds.size.x < 100000f)
            {
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200000f);
                EditorUtility.SetDirty(mesh);
            }
            return mesh;
        }

        /// <summary>
        /// 空の材質を**プロジェクト側の複製**にして返す (IMP-ENV-010)。
        ///
        /// 複製する理由は**購入アセット (Allsky) の材質を実行時に書かないため**。
        /// Allsky は `.gitignore` 除外なので、Play 中に `_Exposure` を書くと購入物が汚れて git で戻せない。
        /// 複製は `_Tex` (キューブマップ) を共有するので**ビルドサイズは増えない**。
        /// `_Rotation` はライトマップと反射プローブを焼いた向きなので `new Material(src)` がそのまま写す。
        /// </summary>
        static Material EnsureSkybox(StringBuilder log, ref bool ok)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyboxCopyPath);
            if (mat == null)
            {
                var src = RenderSettings.skybox;
                if (src == null) { log.AppendLine("   FAIL RenderSettings.skybox が null - 複製元が無い"); ok = false; return null; }
                Directory.CreateDirectory(Path.GetDirectoryName(SkyboxCopyPath));
                mat = new Material(src);
                AssetDatabase.CreateAsset(mat, SkyboxCopyPath);
                log.AppendLine("   created " + SkyboxCopyPath + " (" + src.name + " の複製。テクスチャは共有)");
            }
            if (!mat.HasProperty("_Exposure"))
            {
                log.AppendLine("   FAIL " + SkyboxCopyPath + " に _Exposure が無い (shader="
                    + (mat.shader == null ? "-" : mat.shader.name) + ") - 空が暗くならない");
                ok = false;
            }
            if (ResetSkyToDay(mat)) log.AppendLine("   " + SkyboxCopyPath + " を昼へ戻した");
            if (RenderSettings.skybox != mat)
            {
                RenderSettings.skybox = mat;
                log.AppendLine("   RenderSettings.skybox -> " + SkyboxCopyPath);
            }
            // 旧方式 (夜の空を 2 枚目として持つ) の後片付け。Day Twilight の 6 面がビルドから外れる
            if (AssetDatabase.LoadAssetAtPath<Material>(LegacyNightSkyboxPath) != null)
            {
                AssetDatabase.DeleteAsset(LegacyNightSkyboxPath);
                log.AppendLine("   deleted " + LegacyNightSkyboxPath + " (IMP-ENV-010 で廃止)");
            }
            return mat;
        }

        /// <summary>
        /// 空を昼へ戻す。変えたら true。
        ///
        /// **旧方式より復帰が弱いので要る** — 旧方式は `RenderSettings.skybox` (シーン設定) を書いていたので
        /// Unity が Play 終了時に戻していたが、**材質のプロパティは戻らない**。
        /// </summary>
        static bool ResetSkyToDay(Material mat)
        {
            if (mat == null) return false;
            bool changed = false;
            if (mat.HasProperty("_Exposure")
                && Mathf.Abs(mat.GetFloat("_Exposure") - KD_LiveNight.DaySkyExposure) > 0.001f)
            {
                mat.SetFloat("_Exposure", KD_LiveNight.DaySkyExposure);
                changed = true;
            }
            var day = new Color(KD_LiveNight.DayTintR, KD_LiveNight.DayTintG, KD_LiveNight.DayTintB, KD_LiveNight.SkyTintA);
            if (mat.HasProperty("_Tint") && mat.GetColor("_Tint") != day)
            {
                mat.SetColor("_Tint", day);
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(mat);
            return changed;
        }

        /// <summary>Play を抜けたら空を昼へ戻す (上の理由)。Setup を通さずに Play しただけでも効く。</summary>
        [InitializeOnLoadMethod]
        static void HookSkyRestore()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChangedRestoreSky;
            EditorApplication.playModeStateChanged += OnPlayModeChangedRestoreSky;
        }

        static void OnPlayModeChangedRestoreSky(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyboxCopyPath);
            if (mat != null && ResetSkyToDay(mat))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[KD_LiveSetup] Play 終了 - 空を昼へ戻した (" + SkyboxCopyPath + ")");
            }
        }

        static GameObject EnsureSingle(string name, string containerPath, StringBuilder log, ref bool ok)
        {
            int found;
            var go = KD_SceneObjects.Find(name, out found);
            if (found > 1)
            {
                log.AppendLine("FAIL '" + name + "' がシーン内に " + found + " 個ある (手で解消する)");
                ok = false;
                return null;
            }
            if (go == null)
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Create " + name);
                KD_SceneObjects.PlaceIfNew(go, containerPath);
                log.AppendLine("created " + name);
            }
            return go;
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

        static VRC.SDKBase.Networking.SyncType SceneSyncOf(GameObject go)
        {
            if (go == null) return VRC.SDKBase.Networking.SyncType.None;
            var ub = go.GetComponent<VRC.Udon.UdonBehaviour>();
            return ub != null ? ub.SyncMethod : VRC.SDKBase.Networking.SyncType.None;
        }

        static string ParentPathOf(GameObject go)
        {
            if (go == null || go.transform.parent == null) return "(シーン直下)";
            return KD_SceneObjects.PathOf(go.transform.parent.gameObject);
        }

        static bool Check(ref int fail, StringBuilder log, bool ok, string label)
        {
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + label);
            if (!ok) fail++;
            return ok;
        }

        static bool TryCopy(UdonSharpBehaviour proxy, StringBuilder log)
        {
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                EditorUtility.SetDirty(proxy);
                return true;
            }
            catch (System.Exception e)
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
