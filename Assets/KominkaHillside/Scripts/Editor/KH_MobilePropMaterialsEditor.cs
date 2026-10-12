using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    // IMP-ENV-014: Android/iOS builds swap the cart / wash tool / prop Unity Standard materials to
    // VRChat/Mobile/Standard Lite copies (Assets/KominkaHillside/Materials/Mobile/<name>_Mobile.mat).
    // Same pattern as KH_MobileTerrainBuildEditor: only the build's scene copy changes; PC, Editor and Play keep Standard.
    // Source materials, the authoring scene and prefabs are never written by the hook.
    //
    // Mapping (Standard -> Standard Lite; Lite source: Packages/com.vrchat.base/.../Mobile/VRChat-Mobile-StandardLite.shader):
    // - Lite always does metallic = _MetallicGlossMap.r * _Metallic, smoothness = map.a * _Glossiness (surf, map defaults to white).
    //   Standard with _METALLICGLOSSMAP ignores _Metallic/_Glossiness and uses map.r and map.a * _GlossMapScale.
    //   So with a map: _Metallic = 1, _Glossiness = _GlossMapScale. Without: copy _Metallic / _Glossiness.
    // - Lite multiplies albedo by vertex color unless DISABLE_VERTEX_COLORING; Standard ignores vertex color -> keyword on.
    // - [ToggleOff] _SpecularHighlights / _GlossyReflections: the drawer (not the GUI) owns _SPECULARHIGHLIGHTS_OFF /
    //   _GLOSSYREFLECTIONS_OFF, so float and keyword are both written here. Albedo-only materials (cart): both OFF.
    //   Materials with a normal or metallic map keep the original toggles.
    // - _EnableGeometricSpecularAA: 0 + keyword off (Standard has no equivalent; cheapest on mobile). Flip ExpSpecularAA to change.
    // - Transparent (_Mode != 0) and albedo-alpha smoothness materials are skipped: Lite is opaque and has no such channel.
    public sealed class KH_MobilePropMaterialsEditor : IProcessSceneWithReport
    {
        internal const string LiteShaderName = "VRChat/Mobile/Standard Lite";
        internal const string StandardShaderName = "Standard";
        internal const string OwnRoot = "Assets/KominkaHillside/";
        internal const string MobileFolderParent = "Assets/KominkaHillside/Materials";
        internal const string MobileFolder = MobileFolderParent + "/Mobile";
        internal const string PremiumFolder = "Assets/KominkaHillside/Models/WashPremium";
        internal const string PremiumPrefix = "PremiumWash_";
        internal static readonly string[] FixedTargets = {
            "Assets/KominkaHillside/Materials/Cart/KH_OsanpoCart_Body.mat",
            "Assets/KominkaHillside/Materials/Cart/KH_OsanpoCart_Sign.mat",
            "Assets/KominkaHillside/Models/WashTools/KH_WashTools.mat",
            "Assets/KominkaHillside/Models/PropFinish/KH_PropFinish.mat",
            "Assets/KominkaHillside/Models/WashPremium/PremiumWash_Fixture.mat",
        };
        internal const string BuildLog = "Temp/kh-mobile-materials-build.txt";
        internal const string CreateLog = "Temp/kh-mobile-materials-create.txt";
        internal const string PreviewLog = "Temp/kh-mobile-materials-preview.txt";
        internal const string VerifyLog = "Temp/kh-mobile-materials-verify.txt";
        internal const float ExpSpecularAA = 0f;
        // Lite keywords this tool owns (all declared by #pragma shader_feature in the Lite shader).
        internal static readonly string[] ManagedKeywords = {
            "DISABLE_VERTEX_COLORING", "_SPECULARHIGHLIGHTS_OFF", "_GLOSSYREFLECTIONS_OFF", "_EMISSION", "_DETAIL",
            "_ENABLE_GEOMETRIC_SPECULAR_AA", "_MONOSH_SPECULAR", "_MONOSH_NOSPECULAR",
        };

        // After UdonSharp's [PostProcessScene] (order 0: copies proxy -> UdonBehaviour, destroys proxies) and the terrain hook (1000);
        // well before VRCWorldPostBuildValidator (int.MaxValue - 8).
        public int callbackOrder => 1100;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // AssetBundle builds may have a null report; null is not a Play-mode test.
            var target = report != null ? report.summary.platform : EditorUserBuildSettings.activeBuildTarget;
            if (!ShouldProcess(scene, target, BuildPipeline.isBuildingPlayer, EditorApplication.isPlayingOrWillChangePlaymode)) return;
            var map = BuildMap(); // Validate before any mutation (throws BuildFailedException).
            var counts = new SortedDictionary<string, int>();
            int replaced = ScanRenderers(Renderers(scene), map, true, counts);
            var udonHits = new List<string>();
            int udonFailed;
            int udon = ScanUdon(scene, map, true, udonHits, out udonFailed);
            int remaining = ScanRenderers(Renderers(scene), map, false, null);
            Directory.CreateDirectory("Temp");
            File.AppendAllText(BuildLog, DateTime.Now.ToString("s") + " target=" + target + " scene=" + scene.path
                + " replaced=" + replaced + " remainingTargets=" + remaining + " udonRemapped=" + udon + " udonFailed=" + udonFailed
                + " materials=" + map.Count + " perMaterial=[" + string.Join(", ", counts.Select(kv => kv.Key + ":" + kv.Value)) + "]"
                + (udonHits.Count > 0 ? " udon=[" + string.Join(", ", udonHits) + "]" : "") + "\n");
            if (udonFailed > 0) throw new BuildFailedException("[KH Mobile Props] " + udonFailed + " UdonBehaviour material variables could not be remapped; see " + BuildLog);
        }

        internal static bool ShouldProcess(Scene scene, BuildTarget target, bool building, bool playing)
            => building && !playing && (target == BuildTarget.Android || target == BuildTarget.iOS) && scene.IsValid()
                && scene.GetRootGameObjects().Any(g => g.name == KH_MobileTerrainBuildEditor.WorldRoot);

        // ---------- targets ----------

        internal sealed class Target
        {
            public string path, mobilePath, skip;
            public Material original, mobile;
        }

        internal static List<string> TargetPaths()
        {
            var paths = new List<string>(FixedTargets);
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { PremiumFolder }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                if ((Path.GetDirectoryName(p) ?? "").Replace('\\', '/') != PremiumFolder) continue; // no subfolders
                if (!Path.GetFileName(p).StartsWith(PremiumPrefix, StringComparison.Ordinal)) continue;
                if (!paths.Contains(p)) paths.Add(p);
            }
            return paths;
        }

        internal static string MobilePathFor(string path) => MobileFolder + "/" + Path.GetFileNameWithoutExtension(path) + "_Mobile.mat";

        // errors: missing fixed targets, purchased/foreign paths. Skipped (non-qualifying) materials are not errors.
        internal static List<Target> Targets(List<string> errors)
        {
            var list = new List<Target>();
            foreach (var p in TargetPaths())
            {
                if (!p.StartsWith(OwnRoot, StringComparison.Ordinal)) { errors.Add("target outside " + OwnRoot + ": " + p); continue; }
                var t = new Target { path = p, mobilePath = MobilePathFor(p), original = AssetDatabase.LoadAssetAtPath<Material>(p) };
                if (t.original == null) { errors.Add("missing target material " + p); continue; }
                t.skip = SkipReason(t.original);
                t.mobile = AssetDatabase.LoadAssetAtPath<Material>(t.mobilePath);
                list.Add(t);
            }
            return list;
        }

        internal static string SkipReason(Material o)
        {
            if (o.shader == null || o.shader.name != StandardShaderName) return "shader '" + (o.shader != null ? o.shader.name : "null") + "' is not Unity Standard";
            if ((o.HasProperty("_Mode") && o.GetFloat("_Mode") != 0f) || o.IsKeywordEnabled("_ALPHABLEND_ON")
                || o.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || o.IsKeywordEnabled("_ALPHATEST_ON"))
                return "rendering mode " + (o.HasProperty("_Mode") ? o.GetFloat("_Mode").ToString() : "?") + " is not Opaque (Standard Lite is opaque only)";
            if (o.HasProperty("_SmoothnessTextureChannel") && o.GetFloat("_SmoothnessTextureChannel") != 0f)
                return "smoothness from albedo alpha (Standard Lite has no equivalent)";
            return null;
        }

        // ---------- mapping rules (shared by Create, Verify, the staleness check and the hook) ----------

        static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
        static float F(Material m, string p, float fallback) => m.HasProperty(p) ? m.GetFloat(p) : fallback;
        static bool HasMetalMap(Material o) => Tex(o, "_MetallicGlossMap") != null;
        internal static bool AlbedoOnly(Material o) => Tex(o, "_BumpMap") == null && !HasMetalMap(o);
        internal static float ExpMetallic(Material o) => HasMetalMap(o) ? 1f : F(o, "_Metallic", 0f);
        internal static float ExpGloss(Material o) => HasMetalMap(o) ? F(o, "_GlossMapScale", 1f) : F(o, "_Glossiness", 0.5f);
        internal static float ExpHighlights(Material o) => AlbedoOnly(o) ? 0f : F(o, "_SpecularHighlights", 1f);
        internal static float ExpReflections(Material o) => AlbedoOnly(o) ? 0f : F(o, "_GlossyReflections", 1f);
        internal static bool EmissionOn(Material o) => o.IsKeywordEnabled("_EMISSION");
        internal static bool DetailOn(Material o) => o.IsKeywordEnabled("_DETAIL_MULX2") && (Tex(o, "_DetailAlbedoMap") != null || Tex(o, "_DetailNormalMap") != null);

        internal static HashSet<string> ExpectedKeywords(Material o)
        {
            var k = new HashSet<string> { "DISABLE_VERTEX_COLORING" };
            if (ExpHighlights(o) == 0f) k.Add("_SPECULARHIGHLIGHTS_OFF");
            if (ExpReflections(o) == 0f) k.Add("_GLOSSYREFLECTIONS_OFF");
            if (EmissionOn(o)) k.Add("_EMISSION");
            if (DetailOn(o)) k.Add("_DETAIL");
            if (ExpSpecularAA > 0f) k.Add("_ENABLE_GEOMETRIC_SPECULAR_AA");
            return k;
        }

        // The single list of values a mobile copy carries. ApplyMobile writes exactly these and Diff compares exactly these,
        // so the stale check cannot drift from what Create writes.
        internal sealed class Spec
        {
            public readonly List<KeyValuePair<string, Texture>> textures = new List<KeyValuePair<string, Texture>>();
            public readonly List<KeyValuePair<string, Vector4>> scaleOffsets = new List<KeyValuePair<string, Vector4>>(); // xy scale, zw offset
            public readonly List<KeyValuePair<string, float>> floats = new List<KeyValuePair<string, float>>();
            public readonly List<KeyValuePair<string, Color>> colors = new List<KeyValuePair<string, Color>>();
            public HashSet<string> keywords;
            public bool enableInstancing, doubleSidedGI;
            public int rawRenderQueue;
            public MaterialGlobalIlluminationFlags giFlags;
        }

        static Vector4 ST(Material o, string p) { var s = o.GetTextureScale(p); var f = o.GetTextureOffset(p); return new Vector4(s.x, s.y, f.x, f.y); }

        internal static Spec SpecFor(Material o)
        {
            var s = new Spec();
            void T(string p, Texture v) => s.textures.Add(new KeyValuePair<string, Texture>(p, v));
            void S(string p, Vector4 v) => s.scaleOffsets.Add(new KeyValuePair<string, Vector4>(p, v));
            void Fv(string p, float v) => s.floats.Add(new KeyValuePair<string, float>(p, v));
            void C(string p, Color v) => s.colors.Add(new KeyValuePair<string, Color>(p, v));
            bool emission = EmissionOn(o), detail = DetailOn(o);
            T("_MainTex", Tex(o, "_MainTex"));
            S("_MainTex", ST(o, "_MainTex"));
            C("_Color", o.GetColor("_Color"));
            T("_MetallicGlossMap", Tex(o, "_MetallicGlossMap"));
            Fv("_Metallic", ExpMetallic(o));
            Fv("_Glossiness", ExpGloss(o));
            T("_BumpMap", Tex(o, "_BumpMap"));
            Fv("_BumpScale", F(o, "_BumpScale", 1f));
            T("_OcclusionMap", Tex(o, "_OcclusionMap"));
            Fv("_OcclusionStrength", F(o, "_OcclusionStrength", 1f));
            T("_EmissionMap", emission ? Tex(o, "_EmissionMap") : null);
            C("_EmissionColor", emission ? o.GetColor("_EmissionColor") : Color.black);
            T("_DetailMask", detail ? Tex(o, "_DetailMask") : null);
            T("_DetailAlbedoMap", detail ? Tex(o, "_DetailAlbedoMap") : null);
            S("_DetailAlbedoMap", detail ? ST(o, "_DetailAlbedoMap") : new Vector4(1f, 1f, 0f, 0f));
            T("_DetailNormalMap", detail ? Tex(o, "_DetailNormalMap") : null);
            Fv("_DetailNormalMapScale", detail ? F(o, "_DetailNormalMapScale", 1f) : 1f);
            Fv("_UVSec", detail ? F(o, "_UVSec", 0f) : 0f);
            Fv("_SpecularHighlights", ExpHighlights(o));
            Fv("_GlossyReflections", ExpReflections(o));
            Fv("_EnableGeometricSpecularAA", ExpSpecularAA);
            Fv("_LightmapType", 0f);
            Fv("_Bicubic", 0f);
            s.keywords = ExpectedKeywords(o);
            s.enableInstancing = o.enableInstancing;
            s.doubleSidedGI = o.doubleSidedGI;
            s.rawRenderQueue = -1; // from the shader (Geometry)
            s.giFlags = emission ? o.globalIlluminationFlags : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            return s;
        }

        internal static void ApplyMobile(Material o, Material m, Shader lite)
        {
            var s = SpecFor(o);
            m.shader = lite;
            m.shaderKeywords = new string[0];
            foreach (var kv in s.textures) m.SetTexture(kv.Key, kv.Value);
            foreach (var kv in s.scaleOffsets) { m.SetTextureScale(kv.Key, new Vector2(kv.Value.x, kv.Value.y)); m.SetTextureOffset(kv.Key, new Vector2(kv.Value.z, kv.Value.w)); }
            foreach (var kv in s.floats) m.SetFloat(kv.Key, kv.Value);
            foreach (var kv in s.colors) m.SetColor(kv.Key, kv.Value);
            m.enableInstancing = s.enableInstancing;
            m.doubleSidedGI = s.doubleSidedGI;
            m.renderQueue = s.rawRenderQueue;
            m.globalIlluminationFlags = s.giFlags;
            foreach (var kw in s.keywords) m.EnableKeyword(kw);
            // Inert in this shader (no #pragma); written exactly as StandardLiteShaderGUI.SetMaterialKeywords does,
            // so opening the material in the Inspector does not change it. Not part of Spec/Diff on purpose.
            if (Tex(o, "_BumpMap") != null || (DetailOn(o) && Tex(o, "_DetailNormalMap") != null)) m.EnableKeyword("_NORMALMAP");
            if (HasMetalMap(o)) m.EnableKeyword("_METALLICGLOSSMAP");
        }

        static bool Same(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        // Empty list = the mobile copy matches what ApplyMobile would write for the current original.
        // The raw (serialized) render queue: -1 when the material takes it from the shader. Material.renderQueue would return the resolved 2000.
        static int RawRenderQueue(Material m)
        {
            var so = new SerializedObject(m);
            var prop = so.FindProperty("m_CustomRenderQueue");
            return prop != null ? prop.intValue : m.renderQueue;
        }

        internal static List<string> Diff(Material o, Material m)
        {
            var d = new List<string>();
            if (m.shader == null || m.shader.name != LiteShaderName) { d.Add("shader " + (m.shader != null ? m.shader.name : "null")); return d; }
            var s = SpecFor(o);
            foreach (var kv in s.textures)
                if (Tex(m, kv.Key) != kv.Value) d.Add(kv.Key + " " + Name(Tex(m, kv.Key)) + " != " + Name(kv.Value));
            foreach (var kv in s.scaleOffsets)
            {
                var have = ST(m, kv.Key);
                if ((have - kv.Value).sqrMagnitude > 1e-8f) d.Add(kv.Key + " scale/offset " + have + " != " + kv.Value);
            }
            foreach (var kv in s.floats)
            {
                float have = F(m, kv.Key, float.NaN);
                if (!Same(have, kv.Value)) d.Add(kv.Key + " " + have + " != " + kv.Value);
            }
            foreach (var kv in s.colors)
            {
                var have = m.HasProperty(kv.Key) ? (Vector4)m.GetColor(kv.Key) : new Vector4(float.NaN, 0f, 0f, 0f);
                if (!((have - (Vector4)kv.Value).sqrMagnitude <= 1e-8f)) d.Add(kv.Key + " " + have + " != " + kv.Value);
            }
            foreach (var kw in ManagedKeywords)
                if (m.IsKeywordEnabled(kw) != s.keywords.Contains(kw)) d.Add("keyword " + kw + (s.keywords.Contains(kw) ? " missing" : " unexpected"));
            if (m.enableInstancing != s.enableInstancing) d.Add("enableInstancing " + m.enableInstancing);
            if (m.doubleSidedGI != s.doubleSidedGI) d.Add("doubleSidedGI " + m.doubleSidedGI);
            int rawQueue = RawRenderQueue(m);   // Material.rawRenderQueue is not in Unity 2022.3: read m_CustomRenderQueue (-1 = from the shader)
            if (rawQueue != s.rawRenderQueue) d.Add("renderQueue " + rawQueue + " != " + s.rawRenderQueue);
            if (m.globalIlluminationFlags != s.giFlags) d.Add("globalIlluminationFlags " + m.globalIlluminationFlags + " != " + s.giFlags);
            return d;
        }

        static string Name(UnityEngine.Object o) => o != null ? o.name : "none";

        internal static Shader LiteShader()
        {
            var s = Shader.Find(LiteShaderName);
            if (s == null) throw new BuildFailedException("Shader not found: " + LiteShaderName + " (VRChat SDK base package)");
            return s;
        }

        // original -> mobile for every qualifying target. Throws BuildFailedException when anything is missing or stale.
        internal static Dictionary<Material, Material> BuildMap()
        {
            LiteShader();
            var errors = new List<string>();
            var targets = Targets(errors);
            foreach (var p in FixedTargets)
            {
                var t = targets.FirstOrDefault(x => x.path == p);
                if (t != null && t.skip != null) errors.Add(p + " no longer qualifies: " + t.skip + " (review IMP-ENV-014 targets)");
            }
            var map = new Dictionary<Material, Material>();
            foreach (var t in targets.Where(x => x.skip == null))
            {
                if (t.mobile == null) { errors.Add("missing " + t.mobilePath); continue; }
                var d = Diff(t.original, t.mobile);
                if (d.Count > 0) { errors.Add("stale " + t.mobilePath + ": " + string.Join("; ", d)); continue; }
                map[t.original] = t.mobile;
            }
            if (errors.Count > 0)
                throw new BuildFailedException("[KH Mobile Props] Run Tools/KominkaHillside/Mobile/13 - Create Mobile Prop Materials.\n" + string.Join("\n", errors));
            return map;
        }

        // ---------- scene scans ----------

        internal static Renderer[] Renderers(Scene scene)
            => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();

        // Counts (and with apply, replaces) every material slot whose material is a key of map. Returns the slot count.
        internal static int ScanRenderers(IEnumerable<Renderer> renderers, Dictionary<Material, Material> map, bool apply, SortedDictionary<string, int> counts)
        {
            int n = 0;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials; // a copy; assigned back as a whole
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !map.TryGetValue(mats[i], out var to)) continue;
                    n++;
                    if (counts != null) { counts.TryGetValue(mats[i].name, out int c); counts[mats[i].name] = c + 1; }
                    if (apply) { mats[i] = to; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
            return n;
        }

        static readonly MethodInfo SerializePublicVariablesMethod =
            typeof(UdonBehaviour).GetMethod("SerializePublicVariables", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        // UdonBehaviour public variables of type Material / Material[] that hold a target. In the build this runs after
        // UdonSharp copied proxy -> UdonBehaviour, so the backing variables are the ones that ship.
        internal static int ScanUdon(Scene scene, Dictionary<Material, Material> map, bool apply, List<string> hits, out int failed)
        {
            int n = 0;
            failed = 0;
            foreach (var u in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UdonBehaviour>(true)))
            {
                var vars = u.publicVariables;
                if (vars == null) continue;
                bool changed = false;
                foreach (var sym in vars.VariableSymbols.ToArray())
                {
                    if (!vars.TryGetVariableValue(sym, out object v) || v == null) continue;
                    if (v is Material m)
                    {
                        if (m == null || !map.TryGetValue(m, out var to)) continue;
                        n++;
                        hits.Add(PathOf(u.transform) + "." + sym);
                        if (apply) { if (vars.TrySetVariableValue(sym, to)) changed = true; else failed++; }
                    }
                    else if (v is Material[] arr)
                    {
                        int k = arr.Count(x => x != null && map.ContainsKey(x));
                        if (k == 0) continue;
                        n += k;
                        hits.Add(PathOf(u.transform) + "." + sym + "[" + k + "]");
                        if (!apply) continue;
                        var copy = arr.Select(x => x != null && map.TryGetValue(x, out var to) ? to : x).ToArray();
                        if (vars.TrySetVariableValue(sym, copy)) changed = true; else failed += k;
                    }
                }
                // OnBeforeSerialize does this too when the scene copy is written; done eagerly like UdonSharp's build step.
                if (changed && SerializePublicVariablesMethod != null) SerializePublicVariablesMethod.Invoke(u, Array.Empty<object>());
            }
            return n;
        }

        internal static int CountMaterialVariables(Scene scene, out string symbols)
        {
            var found = new List<string>();
            foreach (var u in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UdonBehaviour>(true)))
            {
                if (u.publicVariables == null) continue;
                foreach (var sym in u.publicVariables.VariableSymbols)
                    if (u.publicVariables.TryGetVariableType(sym, out Type t) && (t == typeof(Material) || t == typeof(Material[])))
                        found.Add(u.gameObject.name + "." + sym);
            }
            symbols = string.Join(", ", found.Distinct().Take(40)) + (found.Count > 40 ? ", ..." : "");
            return found.Count;
        }

        // ---------- menus ----------

        [MenuItem("Tools/KominkaHillside/Mobile/13 - Create Mobile Prop Materials")]
        public static void CreateMaterials()
        {
            var lite = Shader.Find(LiteShaderName);
            if (lite == null) throw new InvalidOperationException("Shader " + LiteShaderName + " not found (VRChat SDK base package)");
            var errors = new List<string>();
            var targets = Targets(errors);
            if (errors.Count > 0) throw new InvalidOperationException("[KH Mobile Props] " + string.Join("\n", errors));
            if (!AssetDatabase.IsValidFolder(MobileFolder)) AssetDatabase.CreateFolder(MobileFolderParent, "Mobile");
            var log = new StringBuilder();
            log.AppendLine(DateTime.Now.ToString("s") + " shader=" + LiteShaderName);
            int created = 0, refreshed = 0, skipped = 0;
            foreach (var t in targets)
            {
                if (t.skip != null) { skipped++; log.AppendLine("SKIP " + t.path + " : " + t.skip); continue; }
                var mob = t.mobile;
                bool isNew = mob == null;
                if (isNew)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(t.mobilePath) != null)
                        throw new InvalidOperationException(t.mobilePath + " exists but is not a Material");
                    mob = new Material(lite) { name = Path.GetFileNameWithoutExtension(t.mobilePath) };
                }
                ApplyMobile(t.original, mob, lite);
                if (isNew) { AssetDatabase.CreateAsset(mob, t.mobilePath); created++; }
                else { EditorUtility.SetDirty(mob); refreshed++; }
                log.AppendLine((isNew ? "NEW  " : "SYNC ") + t.path + " -> " + t.mobilePath
                    + " albedoOnly=" + AlbedoOnly(t.original) + " metallic=" + ExpMetallic(t.original) + " gloss=" + ExpGloss(t.original)
                    + " highlights=" + ExpHighlights(t.original) + " reflections=" + ExpReflections(t.original)
                    + " keywords=[" + string.Join(",", ExpectedKeywords(t.original).OrderBy(x => x)) + "]");
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("created=" + created + " refreshed=" + refreshed + " skipped=" + skipped + ". Source materials and the scene are unchanged.");
            Directory.CreateDirectory("Temp");
            File.WriteAllText(CreateLog, log.ToString());
            Debug.Log("[KH Mobile Props] Create: created=" + created + " refreshed=" + refreshed + " skipped=" + skipped + " -> " + CreateLog);
        }

        [MenuItem("Tools/KominkaHillside/Mobile/14 - Verify Mobile Prop Materials")]
        public static void Verify()
        {
            var scene = SceneManager.GetActiveScene();
            var log = new StringBuilder();
            var errors = new List<string>();
            void Req(bool ok, string what) { log.AppendLine((ok ? "ok   " : "FAIL ") + what); if (!ok) errors.Add(what); }
            log.AppendLine(DateTime.Now.ToString("s") + " scene=" + scene.path);

            var lite = Shader.Find(LiteShaderName);
            Req(lite != null, LiteShaderName + " found");
            var targets = Targets(errors);
            foreach (var e in errors.ToArray()) log.AppendLine("FAIL " + e);
            foreach (var p in FixedTargets)
            {
                var t = targets.FirstOrDefault(x => x.path == p);
                Req(t != null && t.skip == null, "fixed target qualifies (Standard, Opaque): " + p + (t != null && t.skip != null ? " : " + t.skip : ""));
            }
            var qualified = targets.Where(t => t.skip == null).ToList();
            foreach (var t in targets.Where(t => t.skip != null)) log.AppendLine("skip " + t.path + " : " + t.skip);
            log.AppendLine("targets=" + targets.Count + " qualified=" + qualified.Count + " skipped=" + (targets.Count - qualified.Count));
            var map = new Dictionary<Material, Material>();
            foreach (var t in qualified)
            {
                Req(t.mobile != null, "mobile copy exists: " + t.mobilePath);
                if (t.mobile == null) continue;
                var d = Diff(t.original, t.mobile);
                Req(d.Count == 0, "mobile copy matches original (shader, textures, color, metallic/gloss rule, toggles, keywords): "
                    + Path.GetFileName(t.mobilePath) + (d.Count > 0 ? " : " + string.Join("; ", d) : ""));
                map[t.original] = t.mobile;
                log.AppendLine("     " + t.original.name + ": MainTex=" + Name(Tex(t.mobile, "_MainTex")) + " Bump=" + Name(Tex(t.mobile, "_BumpMap"))
                    + " MetalGloss=" + Name(Tex(t.mobile, "_MetallicGlossMap")) + " metallic=" + F(t.mobile, "_Metallic", -1f)
                    + " gloss=" + F(t.mobile, "_Glossiness", -1f) + " highlights=" + F(t.mobile, "_SpecularHighlights", -1f)
                    + " reflections=" + F(t.mobile, "_GlossyReflections", -1f) + " instancing=" + t.mobile.enableInstancing);
            }

            Req(TypeCache.GetTypesDerivedFrom<IProcessSceneWithReport>().Contains(typeof(KH_MobilePropMaterialsEditor)), "build hook registered (IProcessSceneWithReport)");
            var hook = new KH_MobilePropMaterialsEditor();
            Req(hook.callbackOrder > new KH_MobileTerrainBuildEditor().callbackOrder && hook.callbackOrder > 0, "hook order " + hook.callbackOrder + " after UdonSharp (0) and terrain (1000)");
            bool a = ShouldProcess(scene, BuildTarget.Android, true, false), i = ShouldProcess(scene, BuildTarget.iOS, true, false);
            bool pc = ShouldProcess(scene, BuildTarget.StandaloneWindows64, true, false), nb = ShouldProcess(scene, BuildTarget.Android, false, false);
            bool play = ShouldProcess(scene, BuildTarget.Android, true, true);
            Req(a && i && !pc && !nb && !play, "ShouldProcess Android/iOS/PC/not-building/playing = " + a + "/" + i + "/" + pc + "/" + nb + "/" + play + " (expect true/true/false/false/false)");

            // Read-only dry run of the hook on the open scene.
            if (scene.IsValid())
            {
                var renderers = Renderers(scene);
                var counts = new SortedDictionary<string, int>();
                int slots = ScanRenderers(renderers, map, false, counts);
                log.AppendLine("dry run: renderers=" + renderers.Length + " targetSlots=" + slots + " perMaterial=[" + string.Join(", ", counts.Select(kv => kv.Key + ":" + kv.Value)) + "]");
                Req(slots > 0, "open scene has target material slots to replace");
                var reverse = targets.Where(t => t.mobile != null).ToDictionary(t => t.mobile, t => t.original); // every copy, eligible or not
                int onMobile = ScanRenderers(renderers, reverse, false, null);
                Req(onMobile == 0, "authoring scene uses no *_Mobile material (preview OFF, nothing saved on Lite): " + onMobile);
                var hits = new List<string>();
                int udon = ScanUdon(scene, map, false, hits, out _);
                int matVars = CountMaterialVariables(scene, out string syms);
                log.AppendLine("udon: Material/Material[] public variables=" + matVars + " holdingTargets=" + udon + (hits.Count > 0 ? " [" + string.Join(", ", hits) + "]" : ""));
                log.AppendLine("     variables: " + syms);
            }

            string status = errors.Count == 0 ? "PASS" : "FAIL";
            log.AppendLine("RESULT " + status + " errors=" + errors.Count + " qualified=" + qualified.Count);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(VerifyLog, log.ToString());
            Debug.Log("[KH Mobile Props] Verify " + status + " -> " + VerifyLog + (errors.Count > 0 ? "\n" + string.Join("\n", errors) : ""));
        }

        // Swaps renderer slots original <-> mobile in the open scene for a visual check. Marks the scene dirty (Undo-recorded):
        // DO NOT SAVE while ON. After OFF, prefab instances may keep redundant overrides in memory, so prefer reopening the
        // scene without saving rather than saving after OFF.
        [MenuItem("Tools/KominkaHillside/Mobile/15 - Preview Mobile Prop Materials (toggle)")]
        public static void PreviewToggle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.GetRootGameObjects().Any(g => g.name == KH_MobileTerrainBuildEditor.WorldRoot))
                throw new InvalidOperationException("Active scene has no " + KH_MobileTerrainBuildEditor.WorldRoot + " root");
            var errors = new List<string>();
            var targets = Targets(errors);
            if (errors.Count > 0) throw new InvalidOperationException("[KH Mobile Props] " + string.Join("\n", errors));
            // OFF must work even if a mobile copy went stale, or its original stopped qualifying (shader / Fade), while the
            // preview was on: every (original, existing copy) pair restores, regardless of current eligibility.
            var reverse = new Dictionary<Material, Material>();
            foreach (var t in targets) if (t.mobile != null) reverse[t.mobile] = t.original;
            var renderers = Renderers(scene);
            bool turnOff = ScanRenderers(renderers, reverse, false, null) > 0;
            var map = turnOff ? reverse : BuildMap();
            var touched = renderers.Where(r => r.sharedMaterials.Any(m => m != null && map.ContainsKey(m))).ToArray();
            if (touched.Length > 0) Undo.RecordObjects(touched.Cast<UnityEngine.Object>().ToArray(), "Preview Mobile Prop Materials");
            var counts = new SortedDictionary<string, int>();
            int n = ScanRenderers(touched, map, true, counts);
            EditorSceneManager.MarkSceneDirty(scene);
            string line = DateTime.Now.ToString("s") + (turnOff ? " PREVIEW OFF" : " PREVIEW ON") + " renderers=" + touched.Length + " slots=" + n
                + " perMaterial=[" + string.Join(", ", counts.Select(kv => kv.Key + ":" + kv.Value)) + "]"
                + (turnOff ? " back to Standard. Scene is dirty: reopen without saving." : " on " + LiteShaderName + ". DO NOT SAVE. Run again to turn off.");
            Directory.CreateDirectory("Temp");
            File.AppendAllText(PreviewLog, line + "\n");
            if (turnOff) Debug.Log("[KH Mobile Props] " + line);
            else Debug.LogWarning("[KH Mobile Props] " + line);
        }
    }
}
