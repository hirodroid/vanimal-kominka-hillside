using System;
using System.Collections.Generic;
using System.Linq;
using KominkaDogRun;
using KominkaDogRun.Editor;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    // IMP-DOG-025 builder: the checks (Wash/12 and the end of Wash/11). Reads proxies and backing UdonBehaviours
    // (publicVariables); never writes the scene. The transform check runs the runtime formula on the saved pose.
    public static partial class KH_DogWashSetupEditor
    {
        [Serializable] class VerifyReport
        {
            public string utc, scene, status, layer = LayerRationale;
            public string transformFormula = "world = sum(w x smr.bones[b].localToWorldMatrix x bindposes[b] x v) (the skinning of the current Transforms = what is drawn); "
                + "mesh = boneToMesh[i].MultiplyPoint3x4(partBones[i].InverseTransformPoint(world)) (the runtime formula, KH_DogWash._LocalContact); "
                + "error = |mesh - bind-pose vertex| x meshToWorldScale (m). Samples: up to 8 vertices with weight >= 0.99 on that bone (0.9, then 0.6 when fewer than 3). "
                + "Also recorded: |BakeMesh world - sum(w x bone.localToWorld x bindpose x v)| (m), and bone lossyScale x bindpose scale vs meshToWorldScale";
            public int failed;
            public List<string> lines = new List<string>();
            public List<string> notes = new List<string>();
            public List<ColliderCheck> colliders = new List<ColliderCheck>();
            public List<PartCheck> parts = new List<PartCheck>();
            public List<string> notCovered = new List<string>(NotCovered);
        }
        [Serializable] class ColliderCheck
        {
            public string name, bone; public int part, samples;
            public float radiusM, heightM, lossyScale, boneLossyScale, bindposeScale, metresPerMeshUnitViaBone, meshToWorldScale;
            public float maxRoundTripErrorM, maxBakeVsSkinM, maxRoundTripViaBakeM, minSampleWeight;
        }
        [Serializable] class PartCheck
        {
            public string part; public int colliders, validCells, rays, reachable, occluded, missed;
            public float maxRoundTripErrorM, reachableFraction; public string occludedBy = "";
        }

        const float RoundTripLimit = 0.01f;

        static VerifyReport Check()
        {
            var r = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            Action<bool, string> req = (ok, what) => { r.lines.Add((ok ? "PASS " : "FAIL ") + what); if (!ok) r.failed++; };
            foreach (var p in Programs)
                req(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ScriptsDir + "/" + p + ".asset") != null, "program asset " + p);
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int dogsCount);
            req(dogsGo != null && dogsCount == 1 && dogsGo.transform.parent == null, KD_Hillside.DogsRootName + " at the scene root");
            if (dogsGo == null) { r.status = "FAIL"; return r; }
            var ctx = new SceneCtx { dogsRoot = dogsGo.transform, rack = dogsGo.transform.Find(RackName) };
            ctx.wanders = UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true).OrderBy(w => w.dogIndex).ToArray();
            req(ctx.wanders.Length == KD_DogDefs.All.Length && ctx.wanders.Select(w => w.dogIndex).Distinct().Count() == ctx.wanders.Length, "6 wander controllers with distinct dogIndex (" + ctx.wanders.Length + ")");

            var washes = new List<KH_DogWash>();
            foreach (var t in Targets)
            {
                var w = CheckDog(r, req, ctx, t);
                if (w != null) washes.Add(w);
            }
            CheckWanders(req, ctx, washes);
            CheckStation(r, req, dogsGo.scene, washes);
            CheckStationLayout(r, req, dogsGo.scene); // IMP-ENV-007 (StationVerify.cs): homes, return timing, spacing from the hand-placed spots
            CheckAudio(req, washes);
            r.status = r.failed == 0 ? "PASS" : "FAIL";
            return r;
        }

        // ================================================================ 犬

        static KH_DogWash CheckDog(VerifyReport r, Action<bool, string> req, SceneCtx ctx, DogTarget t)
        {
            string k = t.key;
            DogCtx d;
            try { d = ResolveDog(ctx, t); }
            catch (Exception e) { req(false, k + ": resolve dog / mesh / position map: " + e.Message); return null; }
            float mts = d.json.meshToWorldScale;

            // ---- the body is untouched ----
            var mats = d.body.sharedMaterials;
            req(mats.Length == t.bodyMaterials.Length && mats.Select((m, i) => m != null && m.name == t.bodyMaterials[i]).All(x => x),
                k + " body (karada) materials unchanged: " + string.Join(", ", mats.Select(m => m != null ? m.name : "null")) + " (expected " + string.Join(", ", t.bodyMaterials) + ")");
            req(mats.All(m => m == null || m.shader == null || (m.shader.name != ShellShaderName && m.shader.name != MaskShaderName)), k + " body has no wash shader");
            req(d.body.enabled, k + " body renderer enabled");

            // ---- the KH_DogWash object ----
            var node = ctx.dogsRoot.Find(WashName(k));
            var wash = node != null ? node.GetComponent<KH_DogWash>() : null;
            req(wash != null, k + " " + KD_Hillside.DogsRootName + "/" + WashName(k) + " has KH_DogWash");
            if (wash == null) return null;
            req(!node.IsChildOf(d.dog.transform), k + " KH_DogWash is not under the dog root (the root carries VRCObjectSync)");
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(wash);
            req(backing != null && backing.SyncMethod == Networking.SyncType.Manual, k + " KH_DogWash backing sync = Manual");
            req(wash.dogIndex == d.dogIndex, k + " dogIndex " + wash.dogIndex + " == " + d.dogIndex);
            req(wash.dogRoot == d.dog.transform, k + " dogRoot = " + d.def.DogName);

            // ---- shell ----
            var shellT = d.body.transform.parent.Find(ShellName);
            var shell = shellT != null ? shellT.GetComponent<SkinnedMeshRenderer>() : null;
            req(shell != null, k + " shell " + ShellName + " beside karada");
            if (shell != null)
            {
                req(shell.sharedMesh == d.body.sharedMesh, k + " shell mesh = body mesh");
                req(shell.bones.Length == d.body.bones.Length && shell.bones.Zip(d.body.bones, (a, b) => a == b).All(x => x), k + " shell bones = body bones (" + shell.bones.Length + ")");
                req(shell.rootBone == d.body.rootBone, k + " shell rootBone = body rootBone");
                req(shell.quality == d.body.quality && shell.updateWhenOffscreen == d.body.updateWhenOffscreen, k + " shell quality / updateWhenOffscreen = body");
                req(shellT.localPosition == d.body.transform.localPosition && shellT.localRotation == d.body.transform.localRotation && shellT.localScale == d.body.transform.localScale, k + " shell local transform = body");
                req(shell.gameObject.layer == d.body.gameObject.layer, k + " shell layer = body layer (" + shell.gameObject.layer + ")");
                req(shell.shadowCastingMode == ShadowCastingMode.Off && !shell.receiveShadows, k + " shell casts / receives no shadow");
                req(shell.reflectionProbeUsage == ReflectionProbeUsage.Off && shell.lightProbeUsage == d.body.lightProbeUsage, k + " shell reflection probes off, light probes as body (" + shell.lightProbeUsage + ")");
                req(!shell.enabled, k + " shell enabled == false (KH_DogWash switches it)");
                var sm = shell.sharedMaterials;
                req(sm.Length == 1 && sm[0] != null && sm[0].shader != null && sm[0].shader.name == ShellShaderName, k + " shell material uses " + ShellShaderName);
                if (sm.Length == 1 && sm[0] != null)
                {
                    req(sm[0].GetTexture("_Mask") == wash.maskA && sm[0].GetTexture("_PosMap") == d.posMap, k + " shell material _Mask = mask A, _PosMap = position map");
                    req(Mathf.Abs(sm[0].GetFloat("_FoamScale") - FoamGrainPerMeter * mts) < 1e-3f, k + " shell _FoamScale " + sm[0].GetFloat("_FoamScale").ToString("F2") + " = " + (FoamGrainPerMeter * mts).ToString("F2"));
                    req(AssetDatabase.GetAssetPath(sm[0]) == ShellMatPath(k), k + " shell material asset " + ShellMatPath(k));
                }
                var lb = shell.localBounds; var bb = d.body.localBounds;
                req(lb.center == bb.center && lb.size.x >= bb.size.x && lb.size.y >= bb.size.y && lb.size.z >= bb.size.z, k + " shell bounds contain the body bounds");
                req(wash.shellRenderer == shell, k + " wash.shellRenderer = shell");
            }

            // ---- masks, cells, materials ----
            req(wash.maskA != null && MaskRtOk(wash.maskA) && AssetDatabase.GetAssetPath(wash.maskA) == MaskRtPath(k, "A"), k + " mask A 256² ARGBHalf linear, no depth / mips / MSAA, clamp bilinear (" + Rt(wash.maskA) + ")");
            req(wash.maskB != null && MaskRtOk(wash.maskB) && AssetDatabase.GetAssetPath(wash.maskB) == MaskRtPath(k, "B") && wash.maskB != wash.maskA, k + " mask B same format, not A (" + Rt(wash.maskB) + ")");
            var mm = wash.maskMaterial;
            req(mm != null && mm.shader != null && mm.shader.name == MaskShaderName && AssetDatabase.GetAssetPath(mm) == MaskMatPath(k), k + " mask material " + MaskMatPath(k) + " uses " + MaskShaderName);
            if (mm != null)
            {
                req(mm.GetTexture("_PosMap") == d.posMap && mm.GetTexture("_CellTex") == wash.cellTex, k + " mask material _PosMap = position map, _CellTex = cells");
                req(Mathf.Abs(mm.GetFloat("_EdgeNoiseScale") - EdgeNoisePerMeter * mts) < 1e-3f, k + " mask _EdgeNoiseScale = " + (EdgeNoisePerMeter * mts).ToString("F2"));
            }
            var holder = wash.maskMaterialHolder;
            req(holder != null && !holder.enabled && holder.sharedMaterial == mm && holder.transform.IsChildOf(node), k + " maskMaterialHolder: disabled renderer under " + WashName(k) + " with the mask material");
            req(wash.posMap == d.posMap, k + " posMap = " + KH_DogWashBakeEditor.TexturePath(k));
            var ct = wash.cellTex;
            req(ct != null && ct.width == CellGrid && ct.height == CellGrid && ct.format == TextureFormat.RGBA32 && !GraphicsFormatUtility.IsSRGBFormat(ct.graphicsFormat)
                && ct.isReadable && ct.mipmapCount == 1 && ct.wrapMode == TextureWrapMode.Clamp && ct.filterMode == FilterMode.Bilinear && AssetDatabase.GetAssetPath(ct) == CellTexPath(k),
                k + " cellTex 16x16 RGBA32 linear, readable, no mips, clamp bilinear (" + (ct != null ? ct.width + "x" + ct.height + " " + ct.graphicsFormat + " readable=" + ct.isReadable + " mips=" + ct.mipmapCount : "null") + ")");
            req(Mathf.Abs(wash.meshToWorldScale - mts) < 1e-5f, k + " meshToWorldScale " + wash.meshToWorldScale.ToString("R") + " = json " + mts.ToString("R"));
            req(wash.tools != null && wash.tools.Length == ToolSpecs.Length && wash.tools.All(x => x != null), k + " tools: " + (wash.tools != null ? wash.tools.Length : 0) + " / " + ToolSpecs.Length + ", none null");

            // ---- arrays ----
            int n = wash.partColliders != null ? wash.partColliders.Length : 0;
            bool lengths = n > 0 && wash.partBones != null && wash.partBones.Length == n && wash.partOfCollider != null && wash.partOfCollider.Length == n && wash.boneToMesh != null && wash.boneToMesh.Length == n;
            req(lengths, k + " partColliders / partBones / partOfCollider / boneToMesh lengths equal (" + n + ")");
            var b2p = BoneToPartArray(d.map);
            req(wash.boneToPart != null && wash.boneToPart.Length == BoneSlots && wash.boneToPart.SequenceEqual(b2p), k + " boneToPart[64] = the bone -> part table from the json names / parents");
            req(wash.partNeighbors != null && wash.partNeighbors.Length == PartCount && Enumerable.Range(0, PartCount).All(p => wash.partNeighbors[p] == NeighborMask(p) && ((wash.partNeighbors[p] >> p) & 1) == 1),
                k + " partNeighbors[9] = contract §3 incl. own bit (" + (wash.partNeighbors != null ? string.Join(",", wash.partNeighbors) : "null") + ")");
            req(wash.partFoamLimit != null && wash.partFoamLimit.Length == AllowSlots && wash.partFoamLimit.SequenceEqual(FoamLimits()), k + " partFoamLimit[16] (head " + HeadFoamLimit + ")");
            bool cellsLen = wash.cellValid != null && wash.cellValid.Length == CellCount && wash.cellCenter != null && wash.cellCenter.Length == CellCount && wash.cellPart != null && wash.cellPart.Length == CellCount;
            req(cellsLen, k + " cellValid / cellCenter / cellPart [256]");
            if (cellsLen)
            {
                bool same = true;
                for (int c = 0; c < CellCount && same; c++)
                    same = wash.cellValid[c] == d.cells.valid[c] && wash.cellPart[c] == d.cells.part[c] && (wash.cellCenter[c] - d.cells.center[c]).sqrMagnitude < 1e-12f;
                req(same, k + " cell table = a fresh rasterisation of the mesh (no dilated seam texels)");
                int valid = wash.cellValid.Count(x => x);
                req(valid > 60, k + " valid cells " + valid + " > 60");
                Vector3 mn = d.json.aabbMin - Vector3.one * 1e-3f, mx = d.json.aabbMax + Vector3.one * 1e-3f;
                bool inside = Enumerable.Range(0, CellCount).Where(c => wash.cellValid[c]).All(c => { var p = wash.cellCenter[c]; return p.x >= mn.x && p.y >= mn.y && p.z >= mn.z && p.x <= mx.x && p.y <= mx.y && p.z <= mx.z; });
                req(inside, k + " every valid cellCenter inside the position map range (json aabb)");
                req(Enumerable.Range(0, CellCount).All(c => wash.cellPart[c] >= 0 && wash.cellPart[c] < PartCount), k + " cellPart within 0.." + (PartCount - 1));
                for (int p = 0; p < PartCount; p++)
                {
                    int cellsOfPart = Enumerable.Range(0, CellCount).Count(c => wash.cellValid[c] && wash.cellPart[c] == p);
                    req(cellsOfPart > 0, k + " part " + PartNames[p] + " has valid cells (" + cellsOfPart + ")");
                }
            }

            // ---- effects ----
            CheckFx(req, k + " hitSplash", wash.hitSplash, node);
            CheckFx(req, k + " foamPuff", wash.foamPuff, node);

            // ---- colliders ----
            if (lengths) CheckColliders(r, req, d, wash, node);

            // ---- backing (the runtime reads this) ----
            Backing(req, backing, k + " KH_DogWash", "dogIndex", wash.dogIndex);
            foreach (var f in new[] { "dogRoot", "shellRenderer", "maskA", "maskB", "maskMaterial", "maskMaterialHolder", "posMap", "cellTex", "meshToWorldScale", "tools",
                "partBones", "partOfCollider", "partColliders", "boneToMesh", "boneToPart", "partNeighbors", "cellValid", "cellCenter", "cellPart", "partFoamLimit",
                "hitSplash", "foamPuff", "contactFur", "contactFoam", "scrub" })
            {
                var fi = typeof(KH_DogWash).GetField(f);
                if (fi == null) { req(false, k + " KH_DogWash." + f + " field missing (code changed?)"); continue; }
                Backing(req, backing, k + " KH_DogWash", f, fi.GetValue(wash));
            }
            return wash;
        }

        static void CheckFx(Action<bool, string> req, string what, ParticleSystem ps, Transform node)
        {
            req(ps != null && ps.transform.IsChildOf(node), what + " under the wash object");
            if (ps == null) return;
            var main = ps.main;
            req(main.simulationSpace == ParticleSystemSimulationSpace.World && !main.playOnAwake && !ps.emission.enabled, what + ": World, playOnAwake false, emission disabled (Emit only)");
        }

        // ================================================================ 部位 Collider と変換

        static void CheckColliders(VerifyReport r, Action<bool, string> req, DogCtx d, KH_DogWash wash, Transform node)
        {
            string k = d.target.key;
            float mts = d.json.meshToWorldScale;
            var g = d.geo; var bones = d.body.bones;
            var baked = new Mesh();
            Vector3[] bv;
            try { d.body.BakeMesh(baked, true); bv = baked.vertices; }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
            var l2w = d.body.transform.localToWorldMatrix;
            if (bv.Length != g.v.Length) { req(false, k + " BakeMesh vertex count " + bv.Length + " != " + g.v.Length); return; }
            var world = new Vector3[bv.Length];
            Vector3 wmin = Vector3.positiveInfinity, wmax = Vector3.negativeInfinity;
            for (int i = 0; i < bv.Length; i++) { world[i] = l2w.MultiplyPoint3x4(bv[i]); wmin = Vector3.Min(wmin, world[i]); wmax = Vector3.Max(wmax, world[i]); }
            float halfDiag = (wmax - wmin).magnitude * 0.5f;
            r.notes.Add(k + " baked world AABB size " + (wmax - wmin).ToString("F3") + " m, half diagonal " + halfDiag.ToString("F3") + " m");

            int n = wash.partColliders.Length;
            var index = new Dictionary<Collider, int>();
            for (int i = 0; i < n; i++) if (wash.partColliders[i] != null && !index.ContainsKey(wash.partColliders[i])) index[wash.partColliders[i]] = i;
            var pbs = PhysBones(d.dog);
            var parts = Enumerable.Range(0, PartCount).Select(p => new PartCheck { part = PartNames[p] }).ToArray();
            var occluders = Enumerable.Range(0, PartCount).Select(p => new Dictionary<string, int>()).ToArray();
            Physics.SyncTransforms();
            var dirs = RayDirs();

            for (int i = 0; i < n; i++)
            {
                var col = wash.partColliders[i] as CapsuleCollider;
                var bone = wash.partBones[i];
                int part = wash.partOfCollider[i];
                string name = col != null ? col.name : "#" + i;
                req(col != null, k + " collider " + i + " is a CapsuleCollider");
                if (col == null || bone == null || part < 0 || part >= PartCount) { req(false, k + " collider " + i + " bone / part valid"); continue; }
                int b = Array.IndexOf(bones, bone);
                req(b >= 0, k + " " + name + ": partBones[" + i + "] (" + bone.name + ") is a renderer bone");
                if (b < 0) continue;
                parts[part].colliders++;
                req(col.isTrigger && col.gameObject.layer == WashLayer, k + " " + name + ": trigger on layer " + WashLayer + " (layer " + col.gameObject.layer + ")");
                req(col.transform.parent == bone, k + " " + name + ": child of " + bone.name);
                req(col.name.StartsWith(PartPrefix + PartNames[part] + "_"), k + " " + name + ": name matches part " + PartNames[part]);
                req(col.gameObject.activeInHierarchy && col.enabled, k + " " + name + ": active");
                var ls = col.transform.lossyScale;
                req(Mathf.Abs(ls.x - 1f) < 0.02f && Mathf.Abs(ls.y - 1f) < 0.02f && Mathf.Abs(ls.z - 1f) < 0.02f, k + " " + name + ": lossyScale ~1 (" + ls.ToString("F4") + ")");
                float s = MeanScale(ls), rad = col.radius * s, h = Mathf.Max(col.height, 2f * col.radius) * s;
                req(rad > 0.004f && rad * 2f <= halfDiag && h <= halfDiag, k + " " + name + ": world size r " + rad.ToString("F3") + " / h " + h.ToString("F3") + " m within the dog (half diagonal " + halfDiag.ToString("F3") + ")");
                var cw = col.transform.TransformPoint(col.center);
                req(cw.x >= wmin.x - .05f && cw.y >= wmin.y - .05f && cw.z >= wmin.z - .05f && cw.x <= wmax.x + .05f && cw.y <= wmax.y + .05f && cw.z <= wmax.z + .05f, k + " " + name + ": centre inside the dog's baked AABB");
                // boneToMesh = inverse(bindposes[bone]) and = the json matrix
                var inv = g.bindposes[b].inverse;
                req(MaxAbs(wash.boneToMesh[i], inv) < 1e-4f && MaxAbs(wash.boneToMesh[i], d.jsonBoneToMesh[b]) < 1e-5f, k + " " + name + ": boneToMesh = inverse(bindposes[" + b + "]) = json (" + MaxAbs(wash.boneToMesh[i], inv).ToString("E1") + ")");
                // units: bone lossyScale x bindpose scale = metres per mesh unit
                float kb = AxisScaleOf(g.bindposes[b]), sb = MeanScale(bone.lossyScale);
                var cc = new ColliderCheck { name = name, bone = bone.name, part = part, radiusM = rad, heightM = h, lossyScale = s, boneLossyScale = sb, bindposeScale = kb, metresPerMeshUnitViaBone = sb * kb, meshToWorldScale = mts };
                req(Mathf.Abs(sb * kb - mts) / mts < 0.02f, k + " " + name + ": bone lossyScale " + sb.ToString("F4") + " x bindpose scale " + kb.ToString("F4") + " = " + (sb * kb).ToString("F4") + " m per mesh unit ~ meshToWorldScale " + mts.ToString("F4"));
                // PhysBone chain (vrc-character-rig §10)
                foreach (var pb in pbs)
                    if (InChain(pb, col.transform.parent))
                        req(PbIgnores(pb).Contains(col.transform), k + " " + name + ": in the chain of " + KD_SceneObjects.PathOf(pb.gameObject) + " -> registered in ignoreTransforms");

                // ---- the transform check (runtime formula) ----
                var samples = Samples(g, b, 8, out float minW);
                cc.samples = samples.Count; cc.minSampleWeight = minW;
                foreach (int vi in samples)
                {
                    // Reference = the GPU skinning of the current Transforms (sum w x bone.localToWorld x bindpose x v): what
                    // is drawn at run time, and what the colliders (children of the same bones) follow. BakeMesh in Edit
                    // mode disagreed on the Shiba tail only (2026-09-30: Bone.019 on, growing to 16 cm at Bone.021, while
                    // every other part matched to 0.0000 m even with 0.6-weight samples) - it is recorded, not judged.
                    Vector3 wpos = SkinWorld(g, bones, vi);
                    Vector3 mesh = wash.boneToMesh[i].MultiplyPoint3x4(bone.InverseTransformPoint(wpos));
                    float err = (mesh - g.v[vi]).magnitude * mts;
                    cc.maxRoundTripErrorM = Mathf.Max(cc.maxRoundTripErrorM, err);
                    cc.maxBakeVsSkinM = Mathf.Max(cc.maxBakeVsSkinM, (world[vi] - wpos).magnitude);
                    Vector3 meshBake = wash.boneToMesh[i].MultiplyPoint3x4(bone.InverseTransformPoint(world[vi]));
                    cc.maxRoundTripViaBakeM = Mathf.Max(cc.maxRoundTripViaBakeM, (meshBake - g.v[vi]).magnitude * mts);
                    // reachability from 17 directions (vrc-character-rig §14): the first wash-layer collider hit on the way in
                    foreach (var dir in dirs)
                    {
                        parts[part].rays++;
                        if (!Physics.Raycast(wpos + dir * 0.5f, -dir, out RaycastHit hit, 0.55f, 1 << WashLayer, QueryTriggerInteraction.Collide)) { parts[part].missed++; continue; }
                        if (index.TryGetValue(hit.collider, out int hi) && (wash.partOfCollider[hi] == part || ((NeighborMask(part) >> wash.partOfCollider[hi]) & 1) == 1)) parts[part].reachable++;
                        else
                        {
                            parts[part].occluded++;
                            string occ = index.TryGetValue(hit.collider, out int oi) ? PartNames[wash.partOfCollider[oi]] : hit.collider.name;
                            occluders[part].TryGetValue(occ, out int cnt); occluders[part][occ] = cnt + 1;
                        }
                    }
                }
                req(samples.Count > 0, k + " " + name + ": has sample vertices on " + bone.name + " (min weight " + minW.ToString("F2") + ")");
                req(cc.maxRoundTripErrorM <= RoundTripLimit, k + " " + name + ": runtime transform round trip (vs skinning of the current bones) max " + cc.maxRoundTripErrorM.ToString("F5") + " m <= " + RoundTripLimit);
                if (cc.maxBakeVsSkinM > 0.005f)
                    r.notes.Add(k + " " + name + ": Edit-mode BakeMesh differs from the skinning of the current bone Transforms by " + cc.maxBakeVsSkinM.ToString("F4")
                        + " m (round trip via BakeMesh " + cc.maxRoundTripViaBakeM.ToString("F4") + " m). Not judged: the runtime draws and the colliders follow the Transforms; check the tail in Play");
                parts[part].maxRoundTripErrorM = Mathf.Max(parts[part].maxRoundTripErrorM, cc.maxRoundTripErrorM);
                r.colliders.Add(cc);
            }

            // ---- per part ----
            for (int p = 0; p < PartCount; p++)
            {
                var pc = parts[p];
                pc.validCells = wash.cellValid != null ? Enumerable.Range(0, Math.Min(CellCount, wash.cellValid.Length)).Count(c => wash.cellValid[c] && wash.cellPart[c] == p) : 0;
                pc.reachableFraction = pc.rays > 0 ? pc.reachable / (float)pc.rays : 0f;
                pc.occludedBy = string.Join(", ", occluders[p].OrderByDescending(x => x.Value).Select(x => x.Key + " " + x.Value));
                req(pc.colliders > 0, k + " part " + pc.part + " has a collider (" + pc.colliders + ")");
                req(pc.rays == 0 || pc.reachableFraction >= 0.1f, k + " part " + pc.part + " reachable from outside: " + pc.reachableFraction.ToString("P0") + " of " + pc.rays + " rays (occluded " + pc.occluded + (pc.occludedBy.Length > 0 ? " by " + pc.occludedBy : "") + ", missed " + pc.missed + ")");
                if (pc.rays > 0 && pc.reachableFraction < 0.5f) r.notes.Add(k + " part " + pc.part + " reachable only " + pc.reachableFraction.ToString("P0") + " (occluded by " + pc.occludedBy + "): judge in Play (vrc-character-rig §14, not a failure above 10%)");
                r.parts.Add(pc);
            }

            // ---- the shell under a PhysBone chain ----
            var shellT = d.body.transform.parent.Find(ShellName);
            if (shellT != null)
                foreach (var pb in pbs)
                    if (InChain(pb, shellT.parent))
                        req(PbIgnores(pb).Contains(shellT), k + " shell: in the chain of " + KD_SceneObjects.PathOf(pb.gameObject) + " -> registered in ignoreTransforms");
            foreach (var pb in pbs)
            {
                var mct = new SerializedObject(pb).FindProperty("multiChildType");
                r.notes.Add(k + " PhysBone " + KD_SceneObjects.PathOf(pb.gameObject) + " root " + PbRoot(pb).name + " ignoreTransforms " + PbIgnores(pb).Count + " multiChildType " + (mct != null ? mct.intValue.ToString() : "?") + (mct != null && mct.intValue != 0 ? " (not Ignore: not changed by this builder)" : ""));
            }
            // stale / foreign part colliders
            var stray = d.dog.GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith(PartPrefix) && !wash.partColliders.Any(c => c != null && c.transform == x)).Select(x => x.name).ToArray();
            req(stray.Length == 0, k + " no unwired " + PartPrefix + "* objects under the dog (" + string.Join(", ", stray) + ")");
        }

        static List<int> Samples(Geo g, int bone, int max, out float minW)
        {
            // the purest vertices first (a 10 % weight on a bent neighbour already moves the point), at least 3 per tier
            var cand = new List<int>();
            foreach (float threshold in new[] { 0.99f, 0.9f, 0.6f })
            {
                cand.Clear();
                for (int vi = 0; vi < g.v.Length; vi++) if (g.Weight(vi, bone) >= threshold) cand.Add(vi);
                if (cand.Count >= 3) break;
            }
            var pick = new List<int>();
            if (cand.Count > 0)
            {
                float step = Mathf.Max(1f, cand.Count / (float)max);
                for (float f = 0; f < cand.Count && pick.Count < max; f += step) pick.Add(cand[(int)f]);
            }
            minW = pick.Count > 0 ? pick.Min(vi => g.Weight(vi, bone)) : 0f;
            return pick;
        }

        static Vector3 SkinWorld(Geo g, Transform[] bones, int vi)
        {
            Vector3 s = Vector3.zero; float tw = 0f;
            for (int k = 0; k < g.wCount[vi]; k++)
            {
                var b = g.w[g.wStart[vi] + k];
                if (b.boneIndex < 0 || b.boneIndex >= bones.Length || bones[b.boneIndex] == null) continue;
                s += (bones[b.boneIndex].localToWorldMatrix * g.bindposes[b.boneIndex]).MultiplyPoint3x4(g.v[vi]) * b.weight;
                tw += b.weight;
            }
            return tw > 0f ? s / tw : s;
        }

        static Vector3[] RayDirs()
        {
            var list = new List<Vector3> { Vector3.up };
            for (int i = 0; i < 8; i++) list.Add(Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward);
            for (int i = 0; i < 4; i++) list.Add(Quaternion.Euler(-45f, 45f + i * 90f, 0f) * Vector3.forward);
            for (int i = 0; i < 4; i++) list.Add(Quaternion.Euler(45f, 45f + i * 90f, 0f) * Vector3.forward);
            return list.ToArray();
        }

        static float MaxAbs(Matrix4x4 a, Matrix4x4 b)
        {
            float m = 0f;
            for (int i = 0; i < 16; i++) m = Mathf.Max(m, Mathf.Abs(a[i] - b[i]));
            return m;
        }

        static float AxisScaleOf(Matrix4x4 m)
        {
            return (((Vector3)m.GetColumn(0)).magnitude + ((Vector3)m.GetColumn(1)).magnitude + ((Vector3)m.GetColumn(2)).magnitude) / 3f;
        }

        static string Rt(RenderTexture rt)
        {
            return rt == null ? "null" : rt.width + "x" + rt.height + " " + rt.graphicsFormat + " depth " + rt.depth + " mips " + rt.useMipMap + " aa " + rt.antiAliasing + " sRGB " + rt.sRGB + " " + rt.wrapMode + " " + rt.filterMode;
        }

        // ================================================================ 犬の行動

        static void CheckWanders(Action<bool, string> req, SceneCtx ctx, List<KH_DogWash> washes)
        {
            foreach (var w in ctx.wanders)
            {
                var want = washes.FirstOrDefault(x => x.dogIndex == w.dogIndex);
                string who = "wander " + w.dogIndex + " (" + w.name + ")";
                req(w.wash == want, who + ".wash = " + (want != null ? want.name : "null") + " (proxy " + (w.wash != null ? w.wash.name : "null") + ")");
                Backing(req, UdonSharpEditorUtility.GetBackingUdonBehaviour(w), who, "wash", want);
            }
        }

        // ================================================================ 道具

        static void CheckStation(VerifyReport r, Action<bool, string> req, UnityEngine.SceneManagement.Scene scene, List<KH_DogWash> washes)
        {
            var roots = scene.GetRootGameObjects().Where(g => g.name == StationName).ToArray();
            req(roots.Length == 1, StationName + " once at the scene root (" + roots.Length + ")");
            if (roots.Length != 1) return;
            var st = roots[0].transform;
            r.notes.Add(StationName + " at " + st.position.ToString("F2") + " rotation " + st.eulerAngles.ToString("F1"));
            var shelf = st.Find("Shelf");
            req(shelf != null && shelf.GetComponent<Collider>() != null && !shelf.GetComponent<Collider>().isTrigger, StationName + "/Shelf with a solid collider");
            var allColliders = washes.SelectMany(w => w.partColliders ?? new Collider[0]).Where(c => c != null).ToArray();
            var tools = new List<KH_WashTool>();
            foreach (var spec in ToolSpecs)
            {
                var t = st.Find(spec.name);
                string who = spec.name;
                req(t != null, who + " exists under " + StationName);
                if (t == null) continue;
                var tool = t.GetComponent<KH_WashTool>();
                var pickup = t.GetComponent<VRCPickup>();
                var rb = t.GetComponent<Rigidbody>();
                var cols = t.GetComponents<Collider>();
                req(t.gameObject.layer == PickupLayer, who + " layer Pickup (" + t.gameObject.layer + ")");
                req(pickup != null && pickup.AutoHold == VRC_Pickup.AutoHoldMode.Yes && pickup.pickupable, who + " VRCPickup (AutoHold Yes)");
                req(t.GetComponent<VRCObjectSync>() != null, who + " VRCObjectSync");
                req(rb != null && rb.isKinematic && !rb.useGravity, who + " Rigidbody kinematic, no gravity");
                req(cols.Length == 1 && !cols[0].isTrigger, who + " one solid collider for the pickup (" + cols.Length + ")");
                req(tool != null, who + " KH_WashTool");
                if (tool == null) continue;
                tools.Add(tool);
                var tb = UdonSharpEditorUtility.GetBackingUdonBehaviour(tool);
                req(tb != null && tb.SyncMethod == Networking.SyncType.None, who + " KH_WashTool backing sync None");
                req(tool.tool == spec.tool, who + " tool id " + tool.tool + " = " + spec.tool);
                req(tool.nozzle != null && tool.nozzle.IsChildOf(t) && tool.nozzle != t, who + " nozzle child");
                req(tool.washes != null && tool.washes.Length == washes.Count && tool.washes.Zip(washes, (a, b) => a == b).All(x => x), who + " washes = built dogs (" + (tool.washes != null ? tool.washes.Length : 0) + ")");
                req(tool.washLayer.value == 1 << WashLayer, who + " washLayer = layer " + WashLayer + " only (" + tool.washLayer.value + ")");
                var stateT = t.Find("State");
                var state = stateT != null ? stateT.GetComponent<KH_WashToolState>() : null;
                req(state != null && tool.state == state && state.washTool == tool, who + " State child: KH_WashToolState wired both ways");
                if (state != null)
                {
                    var sb = UdonSharpEditorUtility.GetBackingUdonBehaviour(state);
                    req(sb != null && sb.SyncMethod == Networking.SyncType.Manual, who + " State backing sync Manual");
                    Backing(req, sb, who + " State", "washTool", tool);
                }
                if (spec.tool == KH_DogWash.ToolShower)
                {
                    var ps = tool.spray;
                    req(ps != null && ps.transform.IsChildOf(tool.nozzle), who + " spray under the nozzle");
                    if (ps != null)
                    {
                        var main = ps.main;
                        req(main.simulationSpace == ParticleSystemSimulationSpace.World && !main.playOnAwake && ps.collision.enabled && ps.collision.type == ParticleSystemCollisionType.World,
                            who + " spray: World, playOnAwake false, world collision");
                        req(ps.trigger.enabled && ps.trigger.colliderCount == allColliders.Length && ps.trigger.enter == ParticleSystemOverlapAction.Kill,
                            who + " spray trigger module kills on the " + allColliders.Length + " part colliders (" + ps.trigger.colliderCount + ")");
                    }
                    var a = tool.sprayAudio;
                    req(a != null && a.clip != null && a.clip.name == ClipSpray && a.loop && !a.playOnAwake && a.spatialBlend >= 0.99f, who + " sprayAudio: " + ClipSpray + ", loop, 3D, no playOnAwake");
                }
                else req(tool.spray == null && tool.sprayAudio == null, who + " no spray / sprayAudio (not a shower)");
                Backing(req, tb, who, "tool", tool.tool);
                Backing(req, tb, who, "nozzle", tool.nozzle);
                Backing(req, tb, who, "washes", tool.washes);
                Backing(req, tb, who, "washLayer", tool.washLayer);
                Backing(req, tb, who, "spray", tool.spray);
                Backing(req, tb, who, "sprayAudio", tool.sprayAudio);
                Backing(req, tb, who, "state", tool.state);
            }
            foreach (var w in washes)
                req(w.tools != null && w.tools.Length == tools.Count && tools.All(x => w.tools.Contains(x)), w.name + ".tools = the " + tools.Count + " tools of " + StationName);
        }

        // ================================================================ 音

        static void CheckAudio(Action<bool, string> req, List<KH_DogWash> washes)
        {
            foreach (var c in Clips)
            {
                string path = ClipPath(c.name);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                req(imp != null, "audio importer " + path);
                if (imp == null) continue;
                req(AudioMatches(imp.defaultSampleSettings, c.loop) && imp.forceToMono && imp.loadInBackground,
                    c.name + " import: Vorbis " + AudioQuality + ", " + (c.loop ? "CompressedInMemory" : "DecompressOnLoad") + ", mono, Load In Background (" + imp.defaultSampleSettings.compressionFormat + " q" + imp.defaultSampleSettings.quality.ToString("0.##") + " " + imp.defaultSampleSettings.loadType + ")");
                foreach (var pf in MobilePlatforms)
                    req(imp.ContainsSampleSettingsOverride(pf) && AudioMatches(imp.GetOverrideSampleSettings(pf), c.loop), c.name + " " + pf + " override same");
            }
            foreach (var w in washes)
            {
                CheckLoop(req, w.name + " contactFur", w.contactFur, ClipFur);
                CheckLoop(req, w.name + " contactFoam", w.contactFoam, ClipFoam);
                CheckLoop(req, w.name + " scrub", w.scrub, ClipScrub);
            }
        }

        static void CheckLoop(Action<bool, string> req, string what, AudioSource s, string clip)
        {
            req(s != null && s.clip != null && s.clip.name == clip && s.loop && !s.playOnAwake && s.volume == 0f && s.spatialBlend >= 0.99f
                && s.minDistance >= 0.49f && s.maxDistance <= 10.01f && s.maxDistance > s.minDistance,
                what + ": " + clip + ", loop, 3D, no playOnAwake, volume 0, " + AudioMinDistance + "-" + AudioMaxDistance + " m");
        }

        // ================================================================ backing (publicVariables)

        /// <summary>backing (実行時に読む側) の変数が期待する値か。null を期待するときは、変数が無い (U# は null の項目を消す) か null なら可。
        /// U# の参照は相手の backing UdonBehaviour で入ることがあるので、それも同一とみなす (vrc-udonsharp §4)。</summary>
        static void Backing(Action<bool, string> req, UdonBehaviour b, string owner, string field, object want)
        {
            object v = null; bool found = false;
            if (b != null) found = b.publicVariables.TryGetVariableValue(field, out v);
            bool ok = b != null && (IsNull(want) ? (!found || IsNull(v)) : found && SameValue(v, want));
            req(ok, owner + " backing " + field + (b == null ? " (no backing)" : !found ? " (no variable)" : ok ? "" : " (backing " + Describe(v) + " / proxy " + Describe(want) + ")"));
        }

        static bool IsNull(object o) { return o == null || (o is UnityEngine.Object uo && uo == null); }

        static bool SameValue(object got, object want)
        {
            if (IsNull(want)) return IsNull(got);
            if (want is UnityEngine.Object wo)
            {
                var go = got as UnityEngine.Object;
                if (go == null) return false;
                if (ReferenceEquals(go, wo) || go == wo) return true;   // Unity identity (instance ID), as KH_WaterAudioEditor compares
                if (wo is UdonSharpBehaviour usb) { var bk = UdonSharpEditorUtility.GetBackingUdonBehaviour(usb); return bk != null && go == bk; }
                // an asset: the same object of the same file (what the saved scene and the build resolve)
                return AssetId(go, out string g1, out long f1) && AssetId(wo, out string g2, out long f2) && g1 == g2 && f1 == f2;
            }
            if (want is Array wa)
            {
                if (!(got is Array ga) || ga.Length != wa.Length) return false;
                for (int i = 0; i < wa.Length; i++) if (!SameValue(ga.GetValue(i), wa.GetValue(i))) return false;
                return true;
            }
            if (want is float wf) return got is float gf && Mathf.Abs(gf - wf) <= 1e-5f * Mathf.Max(1f, Mathf.Abs(wf));
            if (want is Vector3 wv) return got is Vector3 gv && (gv - wv).sqrMagnitude < 1e-12f;
            if (want is Matrix4x4 wm) return got is Matrix4x4 gm && MaxAbs(gm, wm) < 1e-6f;
            if (want is LayerMask wl) return (got is LayerMask gl && gl.value == wl.value) || (got is int gi && gi == wl.value);
            return Equals(got, want);
        }

        static bool AssetId(UnityEngine.Object o, out string guid, out long fileId)
        {
            guid = null; fileId = 0;
            return o != null && EditorUtility.IsPersistent(o) && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out guid, out fileId) && !string.IsNullOrEmpty(guid);
        }

        static string Describe(object v)
        {
            if (IsNull(v)) return "null";
            if (v is UnityEngine.Object o)
                return o.name + " (" + o.GetType().Name + " id " + o.GetInstanceID() + (EditorUtility.IsPersistent(o) ? " " + AssetDatabase.GetAssetPath(o) : " not persistent") + ")";
            if (v is Array a) return v.GetType().Name + "[" + a.Length + "]";
            return v.ToString();
        }
    }
}
