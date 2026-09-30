using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-025: 犬を洗う試作 (代表 1 匹 = 柴) をシーンへ組み立てる。約束は docs/plans/2026-09-30-dog-wash-prototype.md。
    ///
    /// - Wash/10 Create Programs: KH_DogWash / KH_WashTool / KH_WashToolState の Program Asset を作る (あれば何もしない)。作ったら U# をコンパイルしてから 11 へ
    /// - Wash/11 Build Prototype (Shiba): 本番シーン (Idle 必須) を Temp へ退避してから、アセット (Data/Wash)、犬の側 (殻・部位 Collider・KH_Wash_{犬})、
    ///   道具の棚 (KH_WashStation) を組み、proxy → Udon へ写して保存し、続けて 12 と同じ検査をする。何度流しても同じ結果 (名前で見つけて作り直す)。
    ///   棚 (KH_WashStation) の位置と回転は、既にあれば保つ (人が動かした位置を上書きしない)。道具の棚の上での並びはコードが持つ
    /// - Wash/12 Verify Prototype: proxy と backing (publicVariables) を見る。Temp/KH_DogWashSetupVerify.json。失敗があれば最後に例外
    /// - Wash/13 - Paint Test/1〜7 (場面別の試し描き) / 14 Clear Test Stroke (Edit) / 15 Capture Shiba / 16 Capture All Scenes: Edit モードで殻を見る補助。シーンを保存しない
    /// - Wash/17 Reset Shell Material to Shader Defaults: 殻とマスクのマテリアルをシェーダーの既定値へ戻し、ビルダーの値を入れ直す
    ///
    /// 対象の犬は <see cref="Targets"/> (今は柴だけ)。6 匹へ広げるときはここへ行を足す (犬ごとの差は位置マップ・JSON・骨の名前から実測する)。</summary>
    public static partial class KH_DogWashSetupEditor
    {
        const string Menu = "Tools/KominkaHillside/Wash/";
        public const string DataDir = "Assets/KominkaHillside/Data/Wash";
        const string ScriptsDir = "Assets/KominkaHillside/Scripts";
        const string AudioDir = "Assets/KominkaHillside/Audio/Wash";
        const string BuildReportPath = "Temp/KH_DogWashSetupBuild.json";
        const string VerifyReportPath = "Temp/KH_DogWashSetupVerify.json";
        const string BackupDir = "Temp/imp-dog-025";
        static readonly string[] Programs = { "KH_DogWash", "KH_WashTool", "KH_WashToolState" };

        /// <summary>組み立てる犬。bodyMaterials = 胴体 (karada) の sharedMaterials の名前 (Verify が「変えていない」を確かめる。Wash/1 Survey の値)。</summary>
        class DogTarget
        {
            public string key; public string[] bodyMaterials;
            public DogTarget(string key, params string[] bodyMaterials) { this.key = key; this.bodyMaterials = bodyMaterials; }
        }
        static readonly DogTarget[] Targets =
        {
            new DogTarget("Shiba", "ke 1_Uzumore"),
        };

        // ---- 部位 (約束 §3)。骨の名前は 6 犬種で共通 ----
        public const int PartCount = 9, FallbackPart = 0;
        static readonly string[] PartNames = { "Hip", "Chest", "Neck", "FrontL", "FrontR", "HindL", "HindR", "Head", "Tail" };
        static readonly string[][] PartBoneNames =
        {
            new[] { "Bone.001" },
            new[] { "Bone.002" },
            new[] { "Bone.003" },
            new[] { "Bone.011_L", "Bone.012_L", "Bone.013_L", "Bone.014_L" },
            new[] { "Bone.011_R", "Bone.012_R", "Bone.013_R", "Bone.014_R" },
            new[] { "Bone.005_L", "Bone.010_L", "Bone.015_L", "Bone.016_L" },
            new[] { "Bone.005_R", "Bone.010_R", "Bone.015_R", "Bone.016_R" },
            new[] { "Bone.004" },
            new[] { "Bone.018", "Bone.019", "Bone.020", "Bone.021" },
        };
        /// <summary>隣の部位 (約束 §3)。左右の足は互いに隣にしない。partNeighbors は自分の bit も立てる。</summary>
        static readonly int[][] PartNeighborTable =
        {
            new[] { 1, 5, 6, 8 }, new[] { 0, 2, 3, 4 }, new[] { 1, 7 }, new[] { 1 }, new[] { 1 }, new[] { 0 }, new[] { 0 }, new[] { 2 }, new[] { 0 },
        };
        /// <summary>殻の泡の上限 (頭 0.35、約束 §5)。</summary>
        const float HeadFoamLimit = 0.35f;

        // ---- 層 ----
        /// <summary>部位 Collider の層 = 2 Ignore Raycast。判断の根拠は <see cref="LayerRationale"/>。</summary>
        public const int WashLayer = 2;
        const int PickupLayer = 13;
        const string LayerRationale = "部位 Collider (Trigger) は層 2 Ignore Raycast。このワールドで「Interact / Pickup の光線を妨げない Trigger」に使っている層 "
            + "(KD_DogDefs.BallCatchLayer = 2 のボールの受け皿、KD_DogPreviewEditor の no interaction、IMP-ENV-011 の寝息の Trigger)。犬の子階層の層 14 "
            + "(PickupNoEnvironment) は首輪 (KH_Collar_*, SphereCollider Trigger + Interact) と同じ層なので、首輪を覆う首・頭のカプセルが Interact の光線を先に受けるおそれがある。"
            + "TagManager は変えない (専用のユーザー層を足さない)。Unity の既定の Raycast は層 2 を見ないので、犬の地面レイ (groundLayerMask 0|11) にも当たらない。"
            + "KH_WashTool は washLayer (層 2 だけ) を明示して引く。残るおそれ: VRChat の Interact / Pickup の光線が層 2 の Trigger を無視することは公式に書かれていない (古民家 IMP-FET-015 と同じ未確認)";

        // ---- 見た目の実寸 (シェーダーの既定値は柴の実寸で書かれている: _BrushRadius 0.056 = 0.09 m / 1.615) ----
        const float ShibaReferenceScale = 1.6152f;
        const float FoamGrainPerMeter = 60f / ShibaReferenceScale;     // _FoamScale (1 / mesh unit) = これ × meshToWorldScale
        const float EdgeNoisePerMeter = 30f / ShibaReferenceScale;     // _EdgeNoiseScale (マスク) も同じ

        // ---- アセットの名前 ----
        public const int MaskSize = 256, CellGrid = 16, CellCount = CellGrid * CellGrid, BoneSlots = 64, AllowSlots = 16;
        const string MaskShaderName = "KominkaHillside/Wash Mask", ShellShaderName = "KominkaHillside/Wash Shell";
        static string MaskRtPath(string key, string ab) { return DataDir + "/KH_WashMask_" + key + "_" + ab + ".renderTexture"; }
        static string CellTexPath(string key) { return DataDir + "/KH_WashCells_" + key + ".asset"; }
        static string MaskMatPath(string key) { return DataDir + "/KH_WashMaskMat_" + key + ".mat"; }
        static string ShellMatPath(string key) { return DataDir + "/KH_WashShellMat_" + key + ".mat"; }
        const string SplashMatPath = DataDir + "/KH_WashParticle_Splash.mat", FoamMatPath = DataDir + "/KH_WashParticle_Foam.mat", SprayMatPath = DataDir + "/KH_WashParticle_Spray.mat";
        const string ToolMatDir = DataDir + "/Tools";

        // ---- シーンの名前 ----
        const string ShellName = "KH_WashShell", PartPrefix = "KH_WashPart_", StationName = "KH_WashStation", RackName = "KH_LeashRack";
        static string WashName(string key) { return "KH_Wash_" + key; }
        const float StationOffsetX = -2.5f;

        // ---- 音 (制作票 docs/plans/2026-09-30-wash-se-production.md の案) ----
        const string ClipSpray = "KH_Wash_Spray_Loop", ClipFur = "KH_Wash_ContactFur_Loop", ClipFoam = "KH_Wash_ContactFoam_Loop",
            ClipScrub = "KH_Wash_Scrub_Loop", ClipTowel = "KH_Wash_Towel_Loop", ClipShake = "KH_Wash_Shake";
        static readonly (string name, bool loop)[] Clips =
        {
            (ClipSpray, true), (ClipFur, true), (ClipFoam, true), (ClipScrub, true), (ClipTowel, true), (ClipShake, false),
        };
        static string ClipPath(string name) { return AudioDir + "/" + name + ".wav"; }
        const float AudioQuality = 0.4f, AudioMinDistance = 0.5f, AudioMaxDistance = 8f;
        static readonly string[] MobilePlatforms = { "Android", "iOS" };

        static readonly string[] NotCovered =
        {
            "見た目 (殻が毛の上に自然に乗るか、帯の形、泡の粒、色、前後関係) — 13 の画像と PCVR で見る",
            "Play での動作 (道具を持つ・Use・当たり・ストローク・犬が止まる・首をかしげる・音と飛沫)",
            "同期 (NetStroke、セルの積算、途中参加、Owner 交代、2 人同時) — ClientSim は疑似、実通信は未検査",
            "実機 (Quest 単体・iOS での RenderTexture ARGBHalf と Blit、殻の描画、負荷)",
            "VRChat の Interact / Pickup の光線が層 2 (Ignore Raycast) の Trigger を無視するか (首輪の Interact、道具・ボールを掴む光線)",
            "PhysBone が実際に揺れ続けるか (ignoreTransforms へ登録したことだけを見ている)",
            "動いた姿勢での部位 Collider の追従と遮蔽 (検査は保存された姿勢 1 つだけ)",
        };

        // ================================================================ 10

        [MenuItem(Menu + "10 - Create Programs")]
        public static void CreatePrograms()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("[KH Wash Setup] Edit mode only (not compiling)");
            var made = new List<string>();
            foreach (var name in Programs)
            {
                string asset = ScriptsDir + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(asset) != null) continue;
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScriptsDir + "/" + name + ".cs");
                if (script == null) throw new InvalidOperationException(name + ".cs not imported yet; refresh first");
                var created = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                created.sourceCsScript = script;
                AssetDatabase.CreateAsset(created, asset);
                made.Add(name);
            }
            if (made.Count > 0)
            {
                AssetDatabase.SaveAssets();
                UdonSharpProgramAsset.CompileAllCsPrograms(true);
                Debug.Log("[KH Wash Setup] created program assets: " + string.Join(", ", made) + ". Compile U# (scripts/unity-compile.sh --udon), then run 11.");
            }
            else Debug.Log("[KH Wash Setup] all program assets exist (" + string.Join(", ", Programs) + "); nothing changed");
        }

        // ================================================================ 11

        [Serializable] class BuildReport
        {
            public string utc, scene, status, backup, layer = LayerRationale;
            public Vector3 stationPosition; public bool stationKept;
            public List<DogBuild> dogs = new List<DogBuild>();
            public List<string> log = new List<string>();
        }
        [Serializable] class DogBuild
        {
            public string key; public int dogIndex; public float meshToWorldScale;
            public int validCells, colliders;
            public List<string> boneParts = new List<string>();
            public List<string> fits = new List<string>();
            public List<string> physBones = new List<string>();
            public List<string> notes = new List<string>();
        }

        [MenuItem(Menu + "11 - Build Prototype (Shiba)")]
        public static void Build()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            RequirePrograms();
            var ctx = ResolveScene();   // everything is looked up before anything changes (a failure here leaves the scene untouched)
            var r = new BuildReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Directory.CreateDirectory(BackupDir);
            r.backup = BackupDir + "/before-wash-setup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, false);
            try
            {
                // ---- assets ----
                var audio = ImportAudio(r);
                var shared = SharedAssets(r);
                foreach (var d in ctx.dogs) DogAssets(d, r);
                AssetDatabase.SaveAssets();
                // Wire the persistent asset objects, not the instances made with new (first run): reload them from the database
                // (2026-09-30: maskA / maskB / maskMaterial / cellTex created in the same run differed in the backing).
                foreach (var d in ctx.dogs) ReloadDogAssets(d);

                // ---- dog side ----
                var washes = new List<KH_DogWash>();
                foreach (var d in ctx.dogs) washes.Add(BuildDog(ctx, d, shared, audio, r));

                // ---- tools (the tools and the dogs point at each other: the tools first, then both sides) ----
                var tools = BuildStation(ctx, shared, audio, r);
                var allColliders = washes.SelectMany(w => w.partColliders).ToArray();
                foreach (var t in tools)
                {
                    t.washes = washes.ToArray();
                    t.washLayer = 1 << WashLayer;
                    if (t.spray != null) SetSprayTriggers(t.spray, allColliders);
                    EditorUtility.SetDirty(t);
                }
                foreach (var w in washes) { w.tools = tools.ToArray(); EditorUtility.SetDirty(w); }

                // ---- KD_DogWanderController.wash (only the built dogs; the others null) ----
                WireWanders(ctx, washes, r);

                // ---- proxy -> Udon (all proxies wired first, then copied; vrc-udonsharp §4) ----
                foreach (var w in washes) Copy(w);
                foreach (var t in tools) { Copy(t); if (t.state != null) Copy(t.state); }
                foreach (var w in ctx.wanders) Copy(w);

                var descriptor = UnityEngine.Object.FindObjectOfType<VRC_SceneDescriptor>();
                if (descriptor != null && descriptor.NetworkIDCollection.RemoveAll(x => x.gameObject == null && (x.SerializedTypeNames == null || x.SerializedTypeNames.Count == 0)) > 0)
                { EditorUtility.SetDirty(descriptor); r.log.Add("removed typeless NetworkID entries of deleted objects"); }

                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(ctx.dogsRoot.gameObject.scene);
                if (!EditorSceneManager.SaveScene(ctx.dogsRoot.gameObject.scene)) throw new IOException("Wash prototype scene save failed");
            }
            catch
            {
                Debug.LogError("[KH Wash Setup] Build failed; the scene before this run is at " + r.backup + ". Reopen the scene without saving to discard partial changes.");
                r.status = "FAIL (exception, not saved)";
                WriteJson(BuildReportPath, r);
                throw;
            }
            r.status = "BUILT";
            WriteJson(BuildReportPath, r);
            var v = Check();
            WriteJson(VerifyReportPath, v);
            if (v.failed > 0) throw new InvalidOperationException("[KH Wash Setup] Build saved but " + v.failed + " check(s) failed (" + VerifyReportPath + "):\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL"))));
            Debug.Log("[KH Wash Setup] BUILT and verified (" + v.lines.Count + " checks). " + BuildReportPath + " / " + VerifyReportPath);
        }

        // ================================================================ 12

        [MenuItem(Menu + "12 - Verify Prototype")]
        public static void Verify()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var v = Check();
            WriteJson(VerifyReportPath, v);
            if (v.failed > 0) throw new InvalidOperationException("[KH Wash Setup] Verify failed (" + v.failed + "), see " + VerifyReportPath + ":\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL"))));
            Debug.Log("[KH Wash Setup] VERIFY_OK (" + v.lines.Count + " checks) -> " + VerifyReportPath + ". Not covered: " + string.Join(" / ", NotCovered));
        }

        // ================================================================ 解決 (何も変えない)

        class SceneCtx
        {
            public Transform dogsRoot, rack;
            public KD_DogWanderController[] wanders;
            public List<DogCtx> dogs = new List<DogCtx>();
        }

        class DogCtx
        {
            public DogTarget target; public KD_DogDef def; public int dogIndex;
            public GameObject dog; public SkinnedMeshRenderer body; public KD_DogWanderController wander;
            public KH_DogWashBakeEditor.PosMapBones json; public Texture2D posMap; public Matrix4x4[] jsonBoneToMesh;
            public Geo geo; public BoneMap map;
            public RenderTexture maskA, maskB; public Texture2D cellTex; public Material maskMat, shellMat;
            public Cells cells; public List<Fit> fits;
        }

        static void RequirePrograms()
        {
            foreach (var name in Programs)
            {
                var a = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ScriptsDir + "/" + name + ".asset");
                if (a == null || a.sourceCsScript == null) throw new InvalidOperationException(name + ".asset missing: run Wash/10, compile U#, then 11 (nothing changed)");
            }
            // The C# side must already be the version this builder wires (fields added by Codex review etc.)
            if (typeof(KH_DogWash).GetField("tools") == null || typeof(KH_DogWash).GetField("maskMaterialHolder") == null)
                throw new InvalidOperationException("KH_DogWash has no tools / maskMaterialHolder field: compile first (nothing changed)");
            if (typeof(KD_DogWanderController).GetField("wash") == null)
                throw new InvalidOperationException("KD_DogWanderController.wash missing (HillsideWash partial not compiled; nothing changed)");
        }

        static SceneCtx ResolveScene()
        {
            var c = new SceneCtx();
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int dogsCount);
            if (dogsGo == null || dogsCount != 1 || dogsGo.transform.parent != null) throw new InvalidOperationException(KD_Hillside.DogsRootName + " root missing/ambiguous (" + dogsCount + ")");
            c.dogsRoot = dogsGo.transform;
            c.rack = c.dogsRoot.Find(RackName);
            if (c.rack == null) throw new InvalidOperationException(KD_Hillside.DogsRootName + "/" + RackName + " missing (the station is placed west of it)");
            c.wanders = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).OrderBy(w => w.dogIndex).ToArray();
            if (c.wanders.Length != KD_DogDefs.All.Length || c.wanders.Select(w => w.dogIndex).Distinct().Count() != c.wanders.Length)
                throw new InvalidOperationException("Expected " + KD_DogDefs.All.Length + " wander controllers with distinct dogIndex, found " + c.wanders.Length);
            if (new SerializedObject(c.wanders[0]).FindProperty("wash") == null) throw new InvalidOperationException("KD_DogWanderController.wash not serialized (compile first)");
            foreach (var clip in Clips) if (!File.Exists(ClipPath(clip.name))) throw new InvalidOperationException(ClipPath(clip.name) + " missing: node scripts/kh-gen-wash-se.js, then refresh");
            if (Shader.Find(MaskShaderName) == null || Shader.Find(ShellShaderName) == null) throw new InvalidOperationException("Wash shaders not found (" + MaskShaderName + " / " + ShellShaderName + ")");
            foreach (var t in Targets) c.dogs.Add(ResolveDog(c, t));
            return c;
        }

        static DogCtx ResolveDog(SceneCtx c, DogTarget t)
        {
            var d = new DogCtx { target = t };
            int idx = Array.FindIndex(KD_DogDefs.All, x => x.key == t.key);
            if (idx < 0) throw new InvalidOperationException("Unknown dog key " + t.key);
            d.def = KD_DogDefs.All[idx]; d.dogIndex = idx;
            d.dog = KD_SceneObjects.Find(d.def.DogName, out int count);
            if (d.dog == null || count != 1) throw new InvalidOperationException(d.def.DogName + " found " + count + " times (expected 1)");
            var bodies = d.dog.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name == KH_DogWashBakeEditor.BodyName).ToArray();
            if (bodies.Length != 1) throw new InvalidOperationException(t.key + ": " + bodies.Length + " '" + KH_DogWashBakeEditor.BodyName + "' renderers (expected 1)");
            d.body = bodies[0];
            d.wander = c.wanders.FirstOrDefault(w => w.dogIndex == idx);
            if (d.wander == null || d.wander.dogRoot != d.dog.transform) throw new InvalidOperationException(t.key + ": wander controller for dogIndex " + idx + " missing or its dogRoot is not " + d.def.DogName);
            if (d.dog.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null) throw new InvalidOperationException(d.def.DogName + " has no VRCObjectSync (KH_DogWash follows the dog root's Owner)");
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(KH_DogWashBakeEditor.BonesPath(t.key));
            d.posMap = AssetDatabase.LoadAssetAtPath<Texture2D>(KH_DogWashBakeEditor.TexturePath(t.key));
            if (ta == null || d.posMap == null) throw new InvalidOperationException(t.key + ": position map / json missing (run Wash/2 and Wash/3)");
            d.json = JsonUtility.FromJson<KH_DogWashBakeEditor.PosMapBones>(ta.text);
            var mesh = d.body.sharedMesh;
            if (mesh == null) throw new InvalidOperationException(t.key + ": body has no mesh");
            if (d.json == null || d.json.bones == null || d.json.boneCount != d.body.bones.Length || d.json.bones.Count != d.body.bones.Length || mesh.bindposes.Length != d.body.bones.Length)
                throw new InvalidOperationException(t.key + ": json bones / renderer bones / bindposes differ (re-run Wash/2)");
            if (!(d.json.meshToWorldScale > 0f)) throw new InvalidOperationException(t.key + ": json meshToWorldScale invalid");
            if (d.body.bones.Length > BoneSlots) throw new InvalidOperationException(t.key + ": " + d.body.bones.Length + " bones > " + BoneSlots + " (_BoneToPart size in the shaders)");
            d.jsonBoneToMesh = new Matrix4x4[d.body.bones.Length];
            for (int i = 0; i < d.body.bones.Length; i++)
            {
                var e = d.json.bones[i];
                if (d.body.bones[i] == null || e.name != d.body.bones[i].name || e.index != i || e.boneToMesh == null || e.boneToMesh.Length != 16)
                    throw new InvalidOperationException(t.key + ": json bone " + i + " (" + e.name + ") does not match the renderer (re-run Wash/2)");
                d.jsonBoneToMesh[i] = FromRowMajor(e.boneToMesh);
            }
            d.geo = ReadGeo(mesh);
            d.map = MapBones(d.body, d.geo);
            d.cells = BuildCells(d.geo, d.map);
            d.fits = FitColliders(d.geo, d.map);
            return d;
        }

        // ================================================================ 部品

        static void Copy(UdonSharpBehaviour b)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(b);
            EditorUtility.SetDirty(b);
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (backing == null) return;
            // UdonSharp hides the backing UdonBehaviour (HideInInspector = 2) the next time the editor looks at it, which
            // dirtied the scene right after the first Build's save (2026-09-30: 11 behaviours, m_ObjectHideFlags 0 -> 2). Set
            // it here, before the save, so a second Build finds the scene clean.
            if ((backing.hideFlags & HideFlags.HideInInspector) == 0) backing.hideFlags |= HideFlags.HideInInspector;
            EditorUtility.SetDirty(backing);
        }

        static T AddUdon<T>(GameObject go, Networking.SyncType sync) where T : UdonSharpBehaviour
        {
            var b = go.GetComponent<T>();
            if (b == null) b = go.AddUdonSharpComponent<T>();
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (backing == null) throw new InvalidOperationException(typeof(T).Name + " backing UdonBehaviour missing (compile U# and run 11 again)");
            if (backing.SyncMethod != sync) { backing.SyncMethod = sync; EditorUtility.SetDirty(backing); }
            return b;
        }

        static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int cut = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, cut));
            AssetDatabase.CreateFolder(path.Substring(0, cut), path.Substring(cut + 1));
        }

        static Matrix4x4 FromRowMajor(float[] a)
        {
            var m = new Matrix4x4();
            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) m[row, col] = a[row * 4 + col];
            return m;
        }

        static float MeanScale(Vector3 s) { return (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f; }

        static void WriteJson(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(o, true));
        }

        static int NeighborMask(int part)
        {
            int m = 1 << part;
            foreach (int q in PartNeighborTable[part]) m |= 1 << q;
            return m;
        }

        static float[] FoamLimits()
        {
            var l = new float[AllowSlots];
            for (int i = 0; i < AllowSlots; i++) l[i] = i == 7 ? HeadFoamLimit : 1f;
            return l;
        }

        /// <summary>KH_DogWash.PartDirtFactor の写し (13 の Restore が実行時の初期値と同じ分布を描くため。実行時を変えたらここも)。</summary>
        static float PartDirtFactor(int part)
        {
            if (part >= 3 && part <= 6) return 1f;
            if (part == 0 || part == 1) return 0.6f;
            if (part == 8) return 0.4f;
            if (part == 2) return 0.3f;
            return 0.15f;
        }

        static Color[] InitialCellColors(Cells cells, float initialDirt)
        {
            var cols = new Color[CellCount];
            for (int c = 0; c < CellCount; c++)
                cols[c] = new Color(cells.valid[c] ? Mathf.Clamp01(initialDirt * PartDirtFactor(cells.part[c])) : 0f, 0f, 0f, 1f);
            return cols;
        }
    }
}
