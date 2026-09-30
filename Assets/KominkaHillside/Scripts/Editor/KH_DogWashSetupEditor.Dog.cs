using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VRC.SDKBase;

namespace KominkaHillside.Editor
{
    // IMP-DOG-025 builder: per-dog assets (masks, cells, materials) and the dog side of the scene (shell, part colliders,
    // PhysBone ignores, KH_Wash_{dog} with its effects).
    public static partial class KH_DogWashSetupEditor
    {
        class Shared
        {
            public Material splash, foam, spray;
            public Material sponge, showerBody, showerHead, towel, shelf;
        }

        class Audio { public Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(); }

        // ================================================================ アセット (犬ごと)

        static void DogAssets(DogCtx d, BuildReport r)
        {
            EnsureFolder(DataDir);
            string k = d.target.key;
            d.maskA = EnsureMaskRt(MaskRtPath(k, "A"), "KH_WashMask_" + k + "_A");
            d.maskB = EnsureMaskRt(MaskRtPath(k, "B"), "KH_WashMask_" + k + "_B");
            d.cellTex = EnsureCellTex(CellTexPath(k), "KH_WashCells_" + k, InitialCellColors(d.cells, 0.6f));
            float mts = d.json.meshToWorldScale;

            d.maskMat = EnsureMaterial(MaskMatPath(k), Shader.Find(MaskShaderName));
            d.maskMat.SetTexture("_PosMap", d.posMap);
            d.maskMat.SetTexture("_CellTex", d.cellTex);
            d.maskMat.SetFloat("_EdgeNoiseScale", EdgeNoisePerMeter * mts);
            EditorUtility.SetDirty(d.maskMat);

            d.shellMat = EnsureMaterial(ShellMatPath(k), Shader.Find(ShellShaderName));
            d.shellMat.SetTexture("_Mask", d.maskA);
            d.shellMat.SetTexture("_PosMap", d.posMap);
            d.shellMat.SetFloat("_FoamScale", FoamGrainPerMeter * mts);
            d.shellMat.enableInstancing = true;
            EditorUtility.SetDirty(d.shellMat);
            r.log.Add(k + ": masks " + MaskRtPath(k, "A") + " / B, cells " + CellTexPath(k) + ", _FoamScale " + (FoamGrainPerMeter * mts).ToString("F2") + ", _EdgeNoiseScale " + (EdgeNoisePerMeter * mts).ToString("F2"));
        }

        static void ReloadDogAssets(DogCtx d)
        {
            string k = d.target.key;
            T Load<T>(string path) where T : UnityEngine.Object
            {
                var o = AssetDatabase.LoadAssetAtPath<T>(path);
                if (o == null) throw new InvalidOperationException("asset not found after save: " + path);
                return o;
            }
            d.maskA = Load<RenderTexture>(MaskRtPath(k, "A"));
            d.maskB = Load<RenderTexture>(MaskRtPath(k, "B"));
            d.cellTex = Load<Texture2D>(CellTexPath(k));
            d.maskMat = Load<Material>(MaskMatPath(k));
            d.shellMat = Load<Material>(ShellMatPath(k));
        }

        /// <summary>256² ARGBHalf、linear、深度なし、ミップなし、MSAA なし、Clamp、Bilinear。既存は中身だけ直す (GUID を保つ)。</summary>
        static RenderTexture EnsureMaskRt(string path, string name)
        {
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (rt == null)
            {
                var desc = new RenderTextureDescriptor(MaskSize, MaskSize, GraphicsFormat.R16G16B16A16_SFloat, 0)
                { msaaSamples = 1, useMipMap = false, autoGenerateMips = false, sRGB = false };
                rt = new RenderTexture(desc);
                SetRtSampling(rt, name);
                AssetDatabase.CreateAsset(rt, path);
                return rt;
            }
            if (!MaskRtOk(rt))
            {
                rt.Release();
                rt.width = MaskSize; rt.height = MaskSize; rt.depth = 0;
                rt.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                rt.antiAliasing = 1; rt.useMipMap = false; rt.autoGenerateMips = false;
            }
            SetRtSampling(rt, name);
            EditorUtility.SetDirty(rt);
            return rt;
        }

        static void SetRtSampling(RenderTexture rt, string name)
        {
            rt.name = name; rt.wrapMode = TextureWrapMode.Clamp; rt.filterMode = FilterMode.Bilinear; rt.anisoLevel = 0;
        }

        static bool MaskRtOk(RenderTexture rt)
        {
            return rt.width == MaskSize && rt.height == MaskSize && rt.depth == 0 && rt.format == RenderTextureFormat.ARGBHalf
                && rt.graphicsFormat == GraphicsFormat.R16G16B16A16_SFloat && !rt.sRGB && !rt.useMipMap && rt.antiAliasing == 1
                && rt.wrapMode == TextureWrapMode.Clamp && rt.filterMode == FilterMode.Bilinear;
        }

        /// <summary>16×16 RGBA32 linear、読み書き可、ミップなし、Clamp、Bilinear。中身は実行時の初期値と同じ分布 (実行時は Start で書き直す)。</summary>
        static Texture2D EnsureCellTex(string path, string name, Color[] pixels)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool create = t == null;
            if (create) t = new Texture2D(CellGrid, CellGrid, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None);
            else if (t.width != CellGrid || t.height != CellGrid || t.graphicsFormat != GraphicsFormat.R8G8B8A8_UNorm || t.mipmapCount != 1)
                t.Reinitialize(CellGrid, CellGrid, GraphicsFormat.R8G8B8A8_UNorm, false);
            t.name = name; t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear; t.anisoLevel = 0;
            t.SetPixels(pixels); t.Apply(false, false);
            if (create) AssetDatabase.CreateAsset(t, path);
            else EditorUtility.SetDirty(t);
            return t;
        }

        static Material EnsureMaterial(string path, Shader shader)
        {
            if (shader == null) throw new InvalidOperationException("shader missing for " + path);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        // ================================================================ アセット (共有)

        static Shared SharedAssets(BuildReport r)
        {
            EnsureFolder(DataDir); EnsureFolder(ToolMatDir);
            var s = new Shared
            {
                splash = ParticleMaterial(SplashMatPath, new Color(.80f, .90f, 1f, .75f), r),
                foam = ParticleMaterial(FoamMatPath, new Color(1f, 1f, 1f, .9f), r),
                spray = ParticleMaterial(SprayMatPath, new Color(.78f, .88f, 1f, .55f), r),
                sponge = ToolMaterial("KH_WashTool_Sponge", new Color(.98f, .82f, .20f)),
                showerBody = ToolMaterial("KH_WashTool_Shower", new Color(.30f, .52f, .78f)),
                showerHead = ToolMaterial("KH_WashTool_ShowerHead", new Color(.80f, .82f, .84f)),
                towel = ToolMaterial("KH_WashTool_Towel", new Color(.96f, .96f, .94f)),
                shelf = ToolMaterial("KH_WashTool_Shelf", new Color(.45f, .30f, .17f)),
            };
            return s;
        }

        /// <summary>組み込みの Particles/Standard Unlit (Fade)。無ければ Legacy の Alpha Blended。テクスチャは組み込みの Default-Particle。</summary>
        static Material ParticleMaterial(string path, Color tint, BuildReport r)
        {
            var tex = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            var standard = Shader.Find("Particles/Standard Unlit");
            var shader = standard != null ? standard : Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            var m = EnsureMaterial(path, shader);
            m.mainTexture = tex;
            if (standard != null)
            {
                // Fade, the same state StandardParticlesShaderGUI writes for it
                m.SetFloat("_Mode", 2f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHAMODULATE_ON");
                m.SetColor("_Color", tint);
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else m.SetColor("_TintColor", tint);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            if (tex == null) r.log.Add("WARN Default-Particle texture not found for " + path);
            return m;
        }

        static Material ToolMaterial(string name, Color color)
        {
            var shader = Shader.Find("VRChat/Mobile/Standard Lite");
            if (shader == null) shader = Shader.Find("Standard");
            var m = EnsureMaterial(ToolMatDir + "/" + name + ".mat", shader);
            m.color = color; EditorUtility.SetDirty(m);
            return m;
        }

        // ================================================================ 音の取り込み (制作票の案)

        static Audio ImportAudio(BuildReport r)
        {
            var a = new Audio();
            foreach (var c in Clips)
            {
                string path = ClipPath(c.name);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); imp = AssetImporter.GetAtPath(path) as AudioImporter; }
                if (imp == null) throw new InvalidOperationException("AudioImporter missing: " + path + " (refresh first)");
                bool changed = false;
                if (!imp.forceToMono) { imp.forceToMono = true; changed = true; }
                if (!imp.loadInBackground) { imp.loadInBackground = true; changed = true; }
                if (imp.ambisonic) { imp.ambisonic = false; changed = true; }
                if (!AudioMatches(imp.defaultSampleSettings, c.loop)) { imp.defaultSampleSettings = AudioWanted(imp.defaultSampleSettings, c.loop); changed = true; }
                foreach (var pf in MobilePlatforms)
                    if (!imp.ContainsSampleSettingsOverride(pf) || !AudioMatches(imp.GetOverrideSampleSettings(pf), c.loop))
                    { imp.SetOverrideSampleSettings(pf, AudioWanted(imp.GetOverrideSampleSettings(pf), c.loop)); changed = true; }
                if (changed) { imp.SaveAndReimport(); r.log.Add("import " + path + " -> Vorbis " + AudioQuality + " " + (c.loop ? "CompressedInMemory" : "DecompressOnLoad") + ", mono, background (reimported)"); }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) throw new InvalidOperationException("AudioClip not imported: " + path);
                a.clips[c.name] = clip;
            }
            return a;
        }

        static AudioImporterSampleSettings AudioWanted(AudioImporterSampleSettings s, bool loop)
        {
            s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = AudioQuality;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            return s;
        }

        static bool AudioMatches(AudioImporterSampleSettings s, bool loop)
        {
            return s.loadType == (loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad)
                && s.compressionFormat == AudioCompressionFormat.Vorbis && Mathf.Abs(s.quality - AudioQuality) < .001f
                && s.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate;
        }

        // ================================================================ 犬の側

        static KH_DogWash BuildDog(SceneCtx c, DogCtx d, Shared shared, Audio audio, BuildReport r)
        {
            var dr = new DogBuild { key = d.target.key, dogIndex = d.dogIndex, meshToWorldScale = d.json.meshToWorldScale, validCells = d.cells.validCells };
            r.dogs.Add(dr);
            for (int b = 0; b < d.map.bones.Length; b++)
                dr.boneParts.Add(b + ":" + (d.map.bones[b] != null ? d.map.bones[b].name : "(null)") + " -> " + PartNames[d.map.part[b]]
                    + (d.map.owner[b] >= 0 ? " (collider " + d.map.bones[d.map.owner[b]].name + ")" : " (no collider)") + " v" + d.map.domCount[b]);
            dr.notes.AddRange(d.map.notes);

            var shell = BuildShell(d, dr);
            var colliders = BuildColliders(d, dr);
            var created = new List<Transform> { shell.transform };
            created.AddRange(colliders.Select(x => x.transform));
            RegisterPhysBoneIgnores(d.dog, created, dr);
            dr.colliders = colliders.Count;

            // ---- KH_Dogs/KH_Wash_{dog}: not on the dog root (its VRCObjectSync is Continuous; this is Manual) ----
            var node = Child(c.dogsRoot, WashName(d.target.key));
            node.localPosition = Vector3.zero; node.localRotation = Quaternion.identity; node.localScale = Vector3.one;
            var wash = AddUdon<KH_DogWash>(node.gameObject, Networking.SyncType.Manual);

            var holderT = Child(node, "MaskMaterialHolder");
            var holder = holderT.GetComponent<MeshRenderer>(); if (holder == null) holder = holderT.gameObject.AddComponent<MeshRenderer>();
            holder.sharedMaterial = d.maskMat; holder.enabled = false;
            holder.shadowCastingMode = ShadowCastingMode.Off; holder.receiveShadows = false;
            holder.lightProbeUsage = LightProbeUsage.Off; holder.reflectionProbeUsage = ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(holder);

            var splash = EffectParticles(node, "HitSplash", shared.splash, true);
            var puff = EffectParticles(node, "FoamPuff", shared.foam, false);
            var fur = LoopSource(Child(node, "ContactFur").gameObject, audio.clips[ClipFur], 0f);
            var foam = LoopSource(Child(node, "ContactFoam").gameObject, audio.clips[ClipFoam], 0f);
            var scrub = LoopSource(Child(node, "Scrub").gameObject, audio.clips[ClipScrub], 0f);

            // ---- wiring (proxy; copied to Udon after the tools are wired) ----
            int n = colliders.Count;
            wash.dogIndex = d.dogIndex;
            wash.dogRoot = d.dog.transform;
            wash.shellRenderer = shell;
            wash.maskA = d.maskA; wash.maskB = d.maskB;
            wash.maskMaterial = d.maskMat; wash.maskMaterialHolder = holder;
            wash.posMap = d.posMap; wash.cellTex = d.cellTex;
            wash.meshToWorldScale = d.json.meshToWorldScale;
            wash.partBones = new Transform[n]; wash.partOfCollider = new int[n]; wash.partColliders = new Collider[n]; wash.boneToMesh = new Matrix4x4[n];
            for (int i = 0; i < n; i++)
            {
                var f = d.fits[i];
                wash.partBones[i] = d.map.bones[f.bone];
                wash.partOfCollider[i] = f.part;
                wash.partColliders[i] = colliders[i];
                wash.boneToMesh[i] = d.jsonBoneToMesh[f.bone];   // bone local -> bind-pose mesh (= inverse(bindposes[bone])), not the collider's transform
            }
            wash.boneToPart = BoneToPartArray(d.map);
            wash.partNeighbors = new int[PartCount];
            for (int p = 0; p < PartCount; p++) wash.partNeighbors[p] = NeighborMask(p);
            wash.cellValid = (bool[])d.cells.valid.Clone();
            wash.cellCenter = (Vector3[])d.cells.center.Clone();
            wash.cellPart = (int[])d.cells.part.Clone();
            wash.partFoamLimit = FoamLimits();
            wash.hitSplash = splash; wash.foamPuff = puff;
            wash.contactFur = fur; wash.contactFoam = foam; wash.scrub = scrub;
            EditorUtility.SetDirty(wash);
            // the cell texture shows the runtime seed with this dog's initialDirt (Play tuning value, not written here)
            EnsureCellTex(CellTexPath(d.target.key), d.cellTex.name, InitialCellColors(d.cells, wash.initialDirt));
            return wash;
        }

        /// <summary>殻: 胴体の隣 (同じ親) の 2 つ目の SkinnedMeshRenderer。メッシュ・骨・rootBone・品質・Transform は胴体と同じ、bounds は押し出しの分だけ広げる。
        /// 影なし、Reflection Probe なし、Light Probe は胴体に合わせる (殻は ShadeSH9 で環境光を読む)。enabled = false (実行時に KH_DogWash が切り替える)。</summary>
        static SkinnedMeshRenderer BuildShell(DogCtx d, DogBuild dr)
        {
            var body = d.body; var parent = body.transform.parent;
            var t = Child(parent, ShellName);
            t.gameObject.layer = body.gameObject.layer;
            t.localPosition = body.transform.localPosition; t.localRotation = body.transform.localRotation; t.localScale = body.transform.localScale;
            var s = t.GetComponent<SkinnedMeshRenderer>(); if (s == null) s = t.gameObject.AddComponent<SkinnedMeshRenderer>();
            s.sharedMesh = body.sharedMesh;
            s.bones = body.bones;
            s.rootBone = body.rootBone;
            s.quality = body.quality;
            s.updateWhenOffscreen = body.updateWhenOffscreen;
            s.skinnedMotionVectors = body.skinnedMotionVectors;
            s.allowOcclusionWhenDynamic = body.allowOcclusionWhenDynamic;
            float push = d.shellMat.GetFloat("_BaseOffset") + d.shellMat.GetFloat("_FoamThickness") * 1.5f;   // metres (lump up to 1.45 x)
            var boundsSpace = body.rootBone != null ? body.rootBone : body.transform;
            float pad = push / Mathf.Max(1e-4f, MeanScale(boundsSpace.lossyScale));
            var lb = body.localBounds;
            s.localBounds = new Bounds(lb.center, lb.size + Vector3.one * (2f * pad));
            s.shadowCastingMode = ShadowCastingMode.Off; s.receiveShadows = false;
            s.lightProbeUsage = body.lightProbeUsage; s.probeAnchor = body.probeAnchor;
            s.reflectionProbeUsage = ReflectionProbeUsage.Off;
            s.sharedMaterials = new[] { d.shellMat };
            var mesh = body.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++) s.SetBlendShapeWeight(i, body.GetBlendShapeWeight(i));
            s.enabled = false;
            EditorUtility.SetDirty(s); EditorUtility.SetDirty(t.gameObject);
            if (mesh.blendShapeCount > 0) dr.notes.Add("shell copies " + mesh.blendShapeCount + " blendshape weights at build time only (KD_DogPetController drives metozi / egao on karada at run time; the shell does not follow)");
            return s;
        }

        /// <summary>部位 Collider: 骨の子 KH_WashPart_{部位}_{番号}。localScale = 1 / 骨の lossyScale で半径などを m で書く (vrc-character-rig §13)。
        /// 前回の生成物は名前で見つけて作り直し、今回使わない名前のものは消す。</summary>
        static List<CapsuleCollider> BuildColliders(DogCtx d, DogBuild dr)
        {
            var list = new List<CapsuleCollider>();
            var keep = new HashSet<GameObject>();
            var counter = new int[PartCount];
            foreach (var f in d.fits)
            {
                var bone = d.map.bones[f.bone];
                string name = PartPrefix + PartNames[f.part] + "_" + counter[f.part]++;
                var t = Child(bone, name);
                t.gameObject.layer = WashLayer;
                var bp = d.geo.bindposes[f.bone];            // mesh -> bone local
                Vector3 cBL = bp.MultiplyPoint3x4(f.center);
                Vector3 aBL = bp.MultiplyVector(f.axis); float kb = aBL.magnitude; aBL /= Mathf.Max(1e-8f, kb);
                float s = MeanScale(bone.lossyScale);
                t.localPosition = cBL;
                t.localRotation = Quaternion.FromToRotation(Vector3.up, aBL);
                t.localScale = Vector3.one / Mathf.Max(1e-6f, s);
                foreach (var other in t.GetComponents<Collider>()) if (!(other is CapsuleCollider)) UnityEngine.Object.DestroyImmediate(other);
                var cap = t.GetComponent<CapsuleCollider>(); if (cap == null) cap = t.gameObject.AddComponent<CapsuleCollider>();
                cap.isTrigger = true; cap.center = Vector3.zero; cap.direction = 1;
                cap.radius = f.radius * kb * s;
                cap.height = Mathf.Max(f.height * kb * s, 2f * cap.radius);
                EditorUtility.SetDirty(cap); EditorUtility.SetDirty(t.gameObject);
                keep.Add(t.gameObject); list.Add(cap);
                dr.fits.Add(name + " on " + bone.name + ": r " + cap.radius.ToString("F3") + " m, h " + cap.height.ToString("F3") + " m, " + f.verts + " vertices, axis " + f.axisFrom
                    + (f.worstSwallowPart >= 0 ? ", swallows " + PartNames[f.worstSwallowPart] + " " + f.worstSwallow.ToString("P0") : "") + (f.note.Length > 0 ? " (" + f.note + ")" : ""));
            }
            foreach (var t in d.dog.GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith(PartPrefix) && !keep.Contains(x.gameObject)).ToArray())
            {
                dr.notes.Add("removed stale " + t.name + " under " + (t.parent != null ? t.parent.name : "?"));
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
            return list;
        }

        // ================================================================ PhysBone (vrc-character-rig §10)

        /// <summary>VRCPhysBone は型名で引く (SDK の型を直に参照しない。KD_DogLoadProfilerEditor と同じ作法)。</summary>
        static List<Component> PhysBones(GameObject dog)
        {
            return dog.GetComponentsInChildren<Component>(true).Where(x => x != null && x.GetType().Name == "VRCPhysBone").ToList();
        }

        static Transform PbRoot(Component pb)
        {
            var p = new SerializedObject(pb).FindProperty("rootTransform");
            var t = p != null ? p.objectReferenceValue as Transform : null;
            return t != null ? t : pb.transform;
        }

        static List<Transform> PbIgnores(Component pb)
        {
            var list = new List<Transform>();
            var p = new SerializedObject(pb).FindProperty("ignoreTransforms");
            if (p == null || !p.isArray) return list;
            for (int i = 0; i < p.arraySize; i++) list.Add(p.GetArrayElementAtIndex(i).objectReferenceValue as Transform);
            return list;
        }

        /// <summary>parent の子を足すと、この PB のチェーンの子の数が変わるか (parent がルートかその子孫で、ignore の外)。</summary>
        static bool InChain(Component pb, Transform parent)
        {
            var root = PbRoot(pb);
            if (parent != root && !parent.IsChildOf(root)) return false;
            foreach (var ig in PbIgnores(pb)) if (ig != null && (parent == ig || parent.IsChildOf(ig))) return false;
            return true;
        }

        /// <summary>生成物の親が PB のチェーンの中なら、その PB の ignoreTransforms へ生成物を登録する。null の項目は掃除する。multiChildType は触らない。</summary>
        static void RegisterPhysBoneIgnores(GameObject dog, List<Transform> created, DogBuild dr)
        {
            foreach (var pb in PhysBones(dog))
            {
                var so = new SerializedObject(pb);
                var ig = so.FindProperty("ignoreTransforms");
                var mct = so.FindProperty("multiChildType");
                string desc = KominkaDogRun.Editor.KD_SceneObjects.PathOf(pb.gameObject) + " root=" + PbRoot(pb).name + " multiChildType=" + (mct != null ? mct.enumDisplayNames.ElementAtOrDefault(mct.enumValueIndex) ?? mct.intValue.ToString() : "?");
                if (ig == null || !ig.isArray) { dr.physBones.Add(desc + " (no ignoreTransforms property)"); continue; }
                int removed = 0;
                for (int i = ig.arraySize - 1; i >= 0; i--)
                    if (ig.GetArrayElementAtIndex(i).objectReferenceValue == null) { ig.DeleteArrayElementAtIndex(i); removed++; }
                var have = new HashSet<Transform>();
                for (int i = 0; i < ig.arraySize; i++) { var x = ig.GetArrayElementAtIndex(i).objectReferenceValue as Transform; if (x != null) have.Add(x); }
                var added = new List<string>();
                foreach (var t in created)
                {
                    if (t == null || t.parent == null || have.Contains(t)) continue;
                    // judged before the new entries are added, against the chain as it was
                    if (!InChainIgnoring(pb, t.parent, have)) continue;
                    ig.arraySize++;
                    ig.GetArrayElementAtIndex(ig.arraySize - 1).objectReferenceValue = t;
                    have.Add(t); added.Add(t.name);
                }
                so.ApplyModifiedProperties();
                if (mct != null && mct.intValue != 0) desc += " WARN multiChildType is not Ignore";
                dr.physBones.Add(desc + ": ignoreTransforms +" + added.Count + (added.Count > 0 ? " (" + string.Join(", ", added) + ")" : "") + (removed > 0 ? ", removed " + removed + " null" : ""));
            }
        }

        static bool InChainIgnoring(Component pb, Transform parent, HashSet<Transform> ignores)
        {
            var root = PbRoot(pb);
            if (parent != root && !parent.IsChildOf(root)) return false;
            foreach (var ig in ignores) if (ig != null && (parent == ig || parent.IsChildOf(ig))) return false;
            return true;
        }

        // ================================================================ 飛沫・泡の粒・音

        /// <summary>飛沫と泡の粒: Simulation Space = World、playOnAwake = false、emission 無効 (KH_DogWash が Emit で出す)。</summary>
        static ParticleSystem EffectParticles(Transform parent, string name, Material mat, bool splash)
        {
            var t = Child(parent, name);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.Euler(-90f, 0f, 0f); t.localScale = Vector3.one;   // cone opens upward
            var ps = t.GetComponent<ParticleSystem>(); if (ps == null) ps = t.gameObject.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = splash ? 160 : 80;
            main.startLifetime = splash ? new ParticleSystem.MinMaxCurve(.35f, .6f) : new ParticleSystem.MinMaxCurve(.8f, 1.4f);
            main.startSpeed = splash ? new ParticleSystem.MinMaxCurve(.6f, 1.6f) : new ParticleSystem.MinMaxCurve(.08f, .35f);
            main.startSize = splash ? new ParticleSystem.MinMaxCurve(.012f, .035f) : new ParticleSystem.MinMaxCurve(.01f, .03f);
            main.gravityModifier = splash ? 1.2f : -.03f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = true;
            if (splash) { sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 40f; sh.radius = .02f; }
            else { sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .03f; }
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(splash ? .9f : .95f, 0f), new GradientAlphaKey(splash ? .5f : .8f, .6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = !splash;
            if (!splash) size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .6f, 1f, 1.2f));
            var rend = t.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat; rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.shadowCastingMode = ShadowCastingMode.Off; rend.receiveShadows = false;
            EditorUtility.SetDirty(ps); EditorUtility.SetDirty(rend);
            return ps;
        }

        /// <summary>3D、ループ、playOnAwake なし、距離の減衰 0.5〜8 m (制作票の案、Logarithmic)。</summary>
        static AudioSource LoopSource(GameObject go, AudioClip clip, float volume)
        {
            var s = go.GetComponent<AudioSource>(); if (s == null) s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.playOnAwake = false; s.volume = volume; s.mute = false;
            s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = AudioMinDistance; s.maxDistance = AudioMaxDistance;
            s.dopplerLevel = 0f; s.priority = 128;
            EditorUtility.SetDirty(s);
            return s;
        }

        // ================================================================ KD_DogWanderController.wash

        static void WireWanders(SceneCtx c, List<KH_DogWash> washes, BuildReport r)
        {
            foreach (var w in c.wanders)
            {
                var want = washes.FirstOrDefault(x => x.dogIndex == w.dogIndex);
                var so = new SerializedObject(w);
                var p = so.FindProperty("wash");
                if (p == null) throw new InvalidOperationException("KD_DogWanderController.wash missing (compile first)");
                p.objectReferenceValue = want;
                so.ApplyModifiedProperties();
                r.log.Add("wander " + w.dogIndex + " (" + w.name + ").wash = " + (want != null ? want.name : "null"));
            }
        }
    }
}
