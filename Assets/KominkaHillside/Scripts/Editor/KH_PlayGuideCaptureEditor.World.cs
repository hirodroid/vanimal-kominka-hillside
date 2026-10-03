using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KominkaDogRun;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KominkaHillside.Editor
{
    // Guide/13, the world cuts (plan §3.3 F2 / F3 / F4 / N1 / B2, §4.1 step 3). Everything placed in the production scene is a
    // render-only copy (MeshFilter + MeshRenderer, or a LineRenderer) created HideAndDontSave and destroyed at the end: no Udon,
    // no VRCObjectSync, no Collider, no Rigidbody ever enters the scene, and no original object is moved or edited. The posed dog
    // is cloned from the port source scene into a preview scene (as KH_FrisbeeJumpClipEditor does), posed with the jump / idle
    // clip, baked into static meshes and only the baked meshes are shown in the world.
    //
    // Second pass (coordinator review of the first run): every world cut now SEARCHES its viewpoint. A candidate is rejected
    // when a forbidden object (sign / board, wash station, leash rack, NPC or any other character, visible world text) is
    // visible in its frustum, or when a subject (disc, dog, ball, marker) does not project inside the cut. No clean candidate
    // = FAIL with the reasons, never a bad shot.
    public static partial class KH_PlayGuideCaptureEditor
    {
        // ---------------------------------------------------------------- viewpoint table (all relative to named scene objects)
        // Plaza frame: O = the plaza centre (KH_FrisbeeCatch.playAreaCenter of the guide dog's catcher, written by Walking/3 from
        // KH_TerrainPrototypeConfig.Flats[PlazaFlat] = (72, 19, 127), radius 12 m; "the centre stays as the plaza anchor",
        // KH_DogWalkingEditor.cs:350-351). D0 = horizontal direction from the plaza leash station "KH_Station_Plaza" (the plaza
        // EDGE, facing inward, KH_DogWalkingEditor.cs:371-375) to O. Candidate k: D = D0 turned by PlazaYawOffsets[k] about O,
        // S = O - D * StandBack (the player 3 m before the centre, looking across it; every subject <= ~12 m ahead stays on the
        // 12 m flat). The first candidate whose frustum is clean and whose subjects are in frame is used.
        public sealed class ViewSpec
        {
            public string cut, what, anchor;
            public float ahead;       // m along D from S to the subject
            public float eye;         // eye height above the ground under the camera (m)
            public float pitchDeg;    // + = looking down; NaN = solved from the subjects (keeps them in frame)
            public float vfov;        // vertical field of view (deg); 1024 x 768 render, 4:3
        }

        public static readonly ViewSpec[] Views =
        {
            new ViewSpec { cut = "F2", what = "the dog's disc lying on the ground where the menu puts it", anchor = "plaza S (searched yaw)", ahead = 1.2f /* nominal: the scene's KH_ToyRecall.recallDistance is used */, eye = EyeStanding, pitchDeg = float.NaN /* disc centre at F2DiscHeight */, vfov = 38f },
            new ViewSpec { cut = "F3", what = "the disc held low in front (line-drawn hand), aim line + ringed landing marker at power 0.5 / 15 deg, landing unobstructed and playable", anchor = "plaza S (searched yaw)", ahead = 0f, eye = EyeStanding, pitchDeg = float.NaN, vfov = 50f },
            new ViewSpec { cut = "F4", what = "the guide dog mid-jump with the disc held at the mouth as KH_FrisbeeCatch carries it", anchor = "plaza S + D * 6.5, camera 4.5 m away 40 deg off the dog's front, eye 1.9 m", ahead = CatchAhead, eye = 1.9f, pitchDeg = float.NaN, vfov = 30f },
            new ViewSpec { cut = "N1", what = "grey aim line + x marker wherever the game's own verdict (UpdateAim / _Reachable) greys the throw", anchor = "searched: plaza grid, the walking route every 25 m, every KH_Places child; every 15 deg", ahead = 0f, eye = EyeStanding, pitchDeg = float.NaN, vfov = 50f },
            new ViewSpec { cut = "B2", what = "the dog's ball on the ground 2 m ahead, the dog standing 4 m ahead facing the player", anchor = "plaza S (searched yaw)", ahead = BallAhead, eye = EyeStanding, pitchDeg = float.NaN, vfov = 40f },
        };

        const float EyeStanding = 1.6f, StandBack = 3f, CatchAhead = 6.5f, BallAhead = 2.0f, BallDogAhead = 4.0f;
        static readonly float[] PlazaYawOffsets = { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f, 125f, -125f, 150f, -150f, 180f };
        const float F2DiscHeight = 0.37f;                     // F2: the disc centre at 37 % of the height (lower-middle third)
        const float RouteStep = 25f; const int N1YawStep = 15, N1MaxViews = 150;
        const float PreferredPitch = 5f;                       // plan §4.1: look down >= 5 deg so little sky is in frame
        // The held disc relative to the eye (F3 / N1). Second pass: further and lower so the disc is <= 25 % of the cut's width
        // and the hand drawn under it stays inside the cut.
        const float HandForward = 0.9f, HandRight = 0.05f, HandDown = 0.25f;
        const float ThrowPower = 0.5f, ThrowPitchDeg = 15f;    // plan §3.3 F3: power 0.5 (= KH_Frisbee.aimSpeed 7.5 m/s), 15 deg up
        // The aim line (3 -> 1.5 cm) and the 34 cm marker are drawn this much larger than in the game: at 10 m in a 350 mm
        // print the true line is ~1 px wide. Set to 1 for true scale.
        const float AimLegibility = 2f;
        // The x bars use the same grey as the marker disc under them, so unlit they would vanish into it: drawn darker.
        const float CrossShade = 0.35f;
        const float MinRootRise = 0.19f;                       // KH_FrisbeeJumpClipEditor.MinRootRise (private there): the planner's lowest root rise
        const string PlazaStationName = "KH_Station_Plaza";    // KH_DogWalkingEditor.PlazaStation
        const float ForbidRange = 80f;                         // forbidden objects further than this are not tested
        // VRChat reserved layers left out of the shots: 10 PlayerLocal, 18 MirrorReflection
        const int ShotCullingMask = ~((1 << 10) | (1 << 18));

        // forbidden kinds, by object name (all copies in the scene): scene roots / KH_Dogs children read from the scene file
        static readonly string[][] ForbiddenNames =
        {
            new[] { "sign", "KH_GuideBoards" }, new[] { "sign", "KH_PhotoBoard" }, new[] { "sign", "KH_TravelMailbox" },
            new[] { "wash station", "KH_WashStation" }, new[] { "wash station", "KH_WashStations" },
            new[] { "leash rack", "KH_LeashRack" }, new[] { "leash rack", "KH_Station_Viewpoint" }, new[] { "leash rack", PlazaStationName }, new[] { "leash rack", "KH_LeashTools" },
            new[] { "npc", "KD_ShopKeeper" },
        };

        sealed class Forbid { public string kind, path; public Transform root; public Bounds b; }

        sealed class WorldCtx
        {
            public KD_TreatPouch pouch;
            public KH_ToyRecall recall;
            public KH_Frisbee disc;
            public KH_FrisbeeCatch catcher;
            public Transform ball;
            public string breed;
            public Vector3 plaza, dir;
            public Transform station;
            public float recallDistance;
            public LayerMask groundMask;
            public float runMaxSlopeDeg;
            public List<Forbid> forbid;
        }

        struct Frame { public Vector3 dir, right, stand; public string label; }

        static WorldCtx ResolveWorld(KD_TreatPouch p, Report report)
        {
            var c = new WorldCtx { pouch = p, breed = KH_FrisbeeJumpClipEditor.Breeds[GuideDog] };
            c.recall = p.recall;
            if (c.recall == null) throw new InvalidOperationException("KD_TreatPouch.recall (KH_ToyRecall) is not wired: the menu wiring (Menu builders, KD_TreatSetupEditor.Hillside) must run first");
            if (c.recall.discs == null || c.recall.discs.Length <= GuideDog || c.recall.discs[GuideDog] == null)
                throw new InvalidOperationException("KH_ToyRecall.discs[" + GuideDog + "] (the " + c.breed + "'s disc) is missing: the six discs are built by KH_DogWalkingEditor (Walking/3, refused in production); the scene looks damaged");
            c.disc = c.recall.discs[GuideDog];
            c.catcher = c.recall.discCatchers != null && c.recall.discCatchers.Length > GuideDog && c.recall.discCatchers[GuideDog] != null ? c.recall.discCatchers[GuideDog] : c.disc.catcher;
            if (c.catcher == null) throw new InvalidOperationException(c.disc.name + ": no KH_FrisbeeCatch (recall.discCatchers[" + GuideDog + "] / disc.catcher)");
            if (c.catcher.dogIndex != GuideDog) report.warnings.Add("catcher " + c.catcher.name + ".dogIndex = " + c.catcher.dogIndex + " (expected " + GuideDog + ")");
            if (c.catcher.dogRoot != null && c.catcher.dogRoot.name != "KD_Dog_" + c.breed) report.warnings.Add("catcher dogRoot is " + c.catcher.dogRoot.name + ", the posed dog is KD_Dog_" + c.breed);
            var missing = new List<string>();
            if (c.disc.aimLine == null) missing.Add("aimLine");
            if (c.disc.aimMarker == null) missing.Add("aimMarker");
            if (c.disc.aimMaterial == null) missing.Add("aimMaterial");
            if (c.disc.aimOutsideMaterial == null) missing.Add("aimOutsideMaterial");
            if (c.disc.aimMarkerCross == null) missing.Add("aimMarkerCross");
            if (missing.Count > 0) throw new InvalidOperationException(c.disc.name + " lacks " + string.Join(", ", missing) + " (KH_FrisbeePrototypeEditor.BuildDisc builds them; Frisbee / Walking builders must have run)");
            if (c.recall.balls == null || c.recall.balls.Length <= GuideDog || c.recall.balls[GuideDog] == null)
                throw new InvalidOperationException("KH_ToyRecall.balls[" + GuideDog + "] (the " + c.breed + "'s ball) is missing: the menu wiring must run first");
            c.ball = c.recall.balls[GuideDog].transform;
            c.recallDistance = c.recall.recallDistance > 0.2f ? c.recall.recallDistance : 1.2f;
            c.groundMask = c.catcher.groundLayerMask;
            c.runMaxSlopeDeg = c.catcher.runMaxSlopeDeg;
            c.station = FindUnique(PlazaStationName, "the plaza leash station (KH_DogWalkingEditor builds it; the scene looks damaged)");
            c.plaza = c.catcher.playAreaCenter;
            if (c.plaza.sqrMagnitude < 1f)
            {
                c.plaza = new Vector3(KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat, 0], KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat, 2], KH_TerrainPrototypeConfig.Flats[KH_TerrainPrototypeConfig.PlazaFlat, 1]);
                report.warnings.Add("catcher.playAreaCenter is zero; the plaza centre falls back to KH_TerrainPrototypeConfig.Flats[PlazaFlat] " + V(c.plaza));
            }
            var d = c.plaza - c.station.position; d.y = 0f;
            c.dir = d.sqrMagnitude > 0.25f ? d.normalized : Vector3.forward;
            c.forbid = BuildForbidden(report);
            report.notes.Add("plaza frame: centre " + V(c.plaza) + " (KH_FrisbeeCatch.playAreaCenter), station " + V(c.station.position) + ", D0 " + V(c.dir) + "; " + c.forbid.Count + " forbidden renderers / texts catalogued");
            return c;
        }

        /// <summary>The plaza candidates in order (D0 first, then turned both ways).</summary>
        static IEnumerable<Frame> PlazaFrames(WorldCtx c)
        {
            foreach (float off in PlazaYawOffsets)
            {
                var dir = Quaternion.Euler(0f, off, 0f) * c.dir;
                RaycastHit h;
                if (!GroundHit(c.plaza - dir * StandBack, c.groundMask, out h) || Slope(h.normal) > c.runMaxSlopeDeg) continue;
                yield return new Frame { dir = dir, right = Vector3.Cross(Vector3.up, dir).normalized, stand = h.point, label = "plaza yaw" + (off >= 0 ? "+" : "") + off.ToString("F0") };
            }
        }

        // ================================================================ forbidden objects
        static List<Forbid> BuildForbidden(Report report)
        {
            var list = new List<Forbid>();
            var scene = SceneManager.GetActiveScene();
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
            foreach (var kn in ForbiddenNames)
            {
                int found = 0;
                foreach (var t in all.Where(x => x.name == kn[1]))
                {
                    found++;
                    foreach (var r in t.GetComponentsInChildren<Renderer>(false))
                        if (r.enabled && r.gameObject.activeInHierarchy) list.Add(new Forbid { kind = kn[0], path = PathOf(t), root = t, b = r.bounds });
                }
                if (found == 0) report.notes.Add("forbidden '" + kn[1] + "' (" + kn[0] + ") not in the scene");
            }
            // any other character (NPC, avatar-like prop): an active skinned mesh not belonging to a dog
            foreach (var r in Object.FindObjectsOfType<SkinnedMeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                bool dog = false;
                for (var a = r.transform; a != null; a = a.parent) if (a.name.StartsWith("KD_Dog_", StringComparison.Ordinal)) { dog = true; break; }
                if (dog) continue;
                list.Add(new Forbid { kind = "character", path = PathOf(r.transform), root = r.transform, b = r.bounds });
            }
            // any visible world text
            foreach (var t in Object.FindObjectsOfType<TMP_Text>())
            {
                if (t == null || !t.isActiveAndEnabled || string.IsNullOrWhiteSpace(t.text)) continue;
                Bounds b;
                var r = t.GetComponent<Renderer>();
                if (r != null && !(t is TextMeshProUGUI)) b = r.bounds;
                else
                {
                    var corners = new Vector3[4]; t.rectTransform.GetWorldCorners(corners);
                    b = new Bounds(corners[0], Vector3.zero); for (int i = 1; i < 4; i++) b.Encapsulate(corners[i]);
                    b.Expand(0.01f);
                }
                list.Add(new Forbid { kind = "words", path = PathOf(t.transform), root = t.transform, b = b });
            }
            return list;
        }

        /// <summary>Forbidden objects visible from the camera: inside the frustum, within ForbidRange, and not hidden behind a
        /// collider (a line to the bounds' centre or upper part reaches it, or hits the object itself).</summary>
        static List<string> ForbiddenInView(Camera cam, List<Forbid> list)
        {
            var seen = new List<string>();
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var eye = cam.transform.position;
            foreach (var f in list)
            {
                if (f.root == null) continue;
                if (Mathf.Sqrt(f.b.SqrDistance(eye)) > ForbidRange) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, f.b)) continue;
                bool visible = false;
                foreach (var pt in new[] { f.b.center, f.b.center + Vector3.up * f.b.extents.y * 0.9f })
                {
                    if (!InFrustum(planes, pt) && f.b.size.magnitude < 3f) continue;
                    RaycastHit hit; float dist = Vector3.Distance(eye, pt);
                    if (!Physics.Linecast(eye, pt, out hit, ~0, QueryTriggerInteraction.Ignore) || hit.distance >= dist - 0.3f
                        || hit.transform == f.root || hit.transform.IsChildOf(f.root) || Grow3(f.b, 0.2f).Contains(hit.point)) { visible = true; break; }
                }
                if (!visible) continue;
                string label = f.kind + " " + f.path;
                if (!seen.Contains(label)) seen.Add(label);
                if (seen.Count >= 12) break;
            }
            return seen;
        }

        static bool InFrustum(Plane[] planes, Vector3 p) { foreach (var pl in planes) if (pl.GetDistanceToPoint(p) < 0f) return false; return true; }
        static Bounds Grow3(Bounds b, float by) { b.Expand(by * 2f); return b; }

        // ================================================================ camera
        sealed class WorldShot : IDisposable
        {
            public const int RW = CutW * 2, RH = CutH * 2;   // rendered at twice the cut size, AA 8, then a 2x2 box reduction
            public readonly Camera cam;
            readonly GameObject go;
            public string sky = "-";

            public WorldShot(Vector3 eye, Vector3 forward, float vfov)
            {
                go = NewTemp("Camera");
                cam = go.AddComponent<Camera>();
                cam.enabled = false;
                go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(forward, Vector3.up));
                cam.fieldOfView = vfov; cam.nearClipPlane = 0.03f; cam.farClipPlane = 1500f;
                cam.allowHDR = false; cam.allowMSAA = true; cam.useOcclusionCulling = false;
                cam.cullingMask = ShotCullingMask;
                cam.aspect = (float)RW / RH;
            }

            public static Vector3 Forward(Vector3 flatDir, float pitchDownDeg)
            {
                float r = pitchDownDeg * Mathf.Deg2Rad;
                return (flatDir * Mathf.Cos(r) - Vector3.up * Mathf.Sin(r)).normalized;
            }

            Img Pass(CameraClearFlags flags, Color bg, int aa)
            {
                var rt = new RenderTexture(RW, RH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = aa };
                var tex = new Texture2D(RW, RH, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                var prev = RenderTexture.active;
                try
                {
                    cam.clearFlags = flags; cam.backgroundColor = bg;
                    cam.targetTexture = rt;
                    cam.aspect = (float)RW / RH;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, RW, RH), 0, 0);
                    tex.Apply(false);
                    return Img.FromTexture(tex);
                }
                finally
                {
                    cam.targetTexture = null;
                    RenderTexture.active = prev;
                    Object.DestroyImmediate(tex);
                    rt.Release(); Object.DestroyImmediate(rt);
                }
            }

            /// <summary>Renders the shot. The sky: a second, unsampled pass with a key colour finds the sky pixels exactly; if the
            /// skybox pass is flat there (or there is no skybox) the shot is redrawn on the pale #CFE3F0.</summary>
            public Img Render()
            {
                Physics.SyncTransforms();
                bool hasSky = RenderSettings.skybox != null;
                var a = Pass(hasSky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor, PaleSky, 8);
                var key = Pass(CameraClearFlags.SolidColor, new Color(1f, 0f, 1f, 1f), 1);
                int n = 0; double sum = 0, sum2 = 0;
                for (int i = 0; i < key.px.Length; i++)
                {
                    var k = key.px[i];
                    if (k.r < 0.996f || k.g > 0.004f || k.b < 0.996f) continue;
                    n++;
                    var c = a.px[i]; double l = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    sum += l; sum2 += l * l;
                }
                float frac = (float)n / key.px.Length;
                if (n == 0) { sky = "no sky in frame"; return a; }
                double mean = sum / n, sd = Math.Sqrt(Math.Max(0.0, sum2 / n - mean * mean));
                if (!hasSky) { sky = "no RenderSettings.skybox: pale #CFE3F0, sky " + (frac * 100f).ToString("F1") + "%"; return a; }
                if (sd < 0.006)
                {
                    sky = "skybox drew flat (sd " + sd.ToString("F4") + ") in Edit: pale #CFE3F0, sky " + (frac * 100f).ToString("F1") + "%";
                    return Pass(CameraClearFlags.SolidColor, PaleSky, 8);
                }
                sky = "skybox " + RenderSettings.skybox.name + " (sd " + sd.ToString("F3") + "), sky " + (frac * 100f).ToString("F1") + "%";
                return a;
            }

            /// <summary>World point to cut pixels (512 x 384, bottom-left).</summary>
            public Vector2 ToCut(Vector3 world)
            {
                var v = cam.WorldToViewportPoint(world);
                return new Vector2(v.x * CutW, v.y * CutH);
            }

            public Rect ToCut(Bounds b)
            {
                var pts = new Vector2[8];
                int i = 0;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                            pts[i++] = ToCut(new Vector3(x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z));
                return BoundsOf(pts);
            }

            public string Describe()
            {
                var e = go.transform.eulerAngles;
                return "eye " + V(go.transform.position) + " yaw " + e.y.ToString("F1") + " pitch-down " + Mathf.DeltaAngle(0f, e.x).ToString("F1") + " vfov " + cam.fieldOfView.ToString("F0") + " (" + RW + "x" + RH + " AA8 -> " + CutW + "x" + CutH + ")";
            }

            public void Dispose() { DestroyTemp(go); }
        }

        /// <summary>A pitch (down, deg) that keeps every point inside the vertical field with a margin, as close to 'preferred' as
        /// possible (NaN preferred = the middle of the allowed range).</summary>
        static float SolvePitch(Vector3 eye, Vector3 flatDir, float vfov, float preferred, params Vector3[] keep)
        {
            float half = vfov * 0.5f - 6f, lo = float.MinValue, hi = float.MaxValue;
            foreach (var p in keep)
            {
                var d = p - eye; float along = Vector3.Dot(d, flatDir);
                float elevDown = Mathf.Atan2(-d.y, Mathf.Max(0.05f, along)) * Mathf.Rad2Deg;   // + = below the horizon
                lo = Mathf.Max(lo, elevDown - half); hi = Mathf.Min(hi, elevDown + half);
            }
            if (lo > hi) return (lo + hi) * 0.5f;
            if (float.IsNaN(preferred)) return (lo + hi) * 0.5f;
            return Mathf.Clamp(preferred, lo, hi);
        }

        /// <summary>A subject's projected rectangle; false (with the reason) when it is not wholly inside the cut by 'margin'
        /// px or smaller than minPx. Recorded in the cut's subject list either way.</summary>
        static bool Subject(CutWork w, WorldShot shot, string name, Bounds b, float margin, float minPx, out Rect r, List<string> why)
        {
            r = shot.ToCut(b);
            bool front = shot.cam.WorldToViewportPoint(b.center).z > 0.05f;
            bool inside = front && r.xMin >= margin && r.yMin >= margin && r.xMax <= CutW - margin && r.yMax <= CutH - margin;
            bool big = Mathf.Max(r.width, r.height) >= minPx;
            if (!inside) why.Add(name + " outside the cut " + r);
            else if (!big) why.Add(name + " too small " + r);
            return inside && big;
        }

        // ================================================================ render-only copies
        /// <summary>A HideAndDontSave copy of the renderers under src (meshes and materials shared, nothing else), with each
        /// renderer kept at its pose relative to src. The returned root sits at the origin; the caller places it.</summary>
        static GameObject RenderCopy(Transform src, string name, Func<Renderer, bool> include, Func<Renderer, Material[]> materials = null)
        {
            var root = NewTemp(name);
            var rootInv = Matrix4x4.TRS(src.position, src.rotation, Vector3.one).inverse;
            int n = 0;
            foreach (var r in src.GetComponentsInChildren<Renderer>(true))
            {
                if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer) continue;
                if (include != null && !include(r)) continue;
                Mesh mesh = null;
                Matrix4x4 rel;
                if (r is SkinnedMeshRenderer smr)
                {
                    mesh = TrackMesh(new Mesh { name = TmpPrefix + "Baked" });
                    smr.BakeMesh(mesh, true);
                    rel = rootInv * Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mesh = mf.sharedMesh;
                    rel = rootInv * r.transform.localToWorldMatrix;
                }
                var child = new GameObject(TmpPrefix + r.name) { hideFlags = HideFlags.HideAndDontSave };
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = rel.GetColumn(3);
                child.transform.localRotation = rel.rotation;
                child.transform.localScale = rel.lossyScale;
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials != null ? materials(r) : r.sharedMaterials;
                mr.shadowCastingMode = r.shadowCastingMode; mr.receiveShadows = r.receiveShadows;
                n++;
            }
            if (n == 0) throw new InvalidOperationException("no renderer to copy under " + PathOf(src));
            return root;
        }

        static bool ActiveRenderer(Renderer r) { return r.enabled && r.gameObject.activeInHierarchy; }

        static Bounds CopyBounds(GameObject root)
        {
            bool first = true; var b = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            if (first) throw new InvalidOperationException("copy " + root.name + " has no renderer");
            return b;
        }

        /// <summary>Puts a copy on the ground: aligned to the normal, yawed, its lowest point 4 mm above the hit.</summary>
        static void PlaceOnGround(GameObject copy, RaycastHit hit, Vector3 flatDir)
        {
            copy.transform.SetPositionAndRotation(hit.point, Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.LookRotation(flatDir, Vector3.up));
            var b = CopyBounds(copy);
            copy.transform.position += Vector3.up * (hit.point.y - b.min.y + 0.004f);
        }

        /// <summary>The aim materials shipped with the discs (KH_FrisbeeAim / KH_FrisbeeAimOut, "VRChat/Mobile/Standard Lite")
        /// are Metallic 1 / Smoothness 1: in an Edit render both mirror the blue sky, so the grey "cannot go there" line came out
        /// as blue as the normal one (first run). The cuts therefore draw the material's own colour unlit: the normal state is
        /// the light cyan of KH_FrisbeeAim, the blocked state the grey of KH_FrisbeeAimOut - the two states KH_Frisbee.SetAimOutside
        /// switches between.</summary>
        static Material Unlit(Material src, float shade)
        {
            var sh = Shader.Find("Unlit/Color");
            if (sh == null) return src;
            var c = src.HasProperty("_Color") ? src.color : Color.gray;
            var m = new Material(sh) { name = TmpPrefix + src.name, hideFlags = HideFlags.HideAndDontSave, color = new Color(c.r * shade, c.g * shade, c.b * shade, 1f) };
            s_temps.Add(m);
            return m;
        }

        /// <summary>The aim line copy (KH_Frisbee.aimLine's widths x AimLegibility, corner / cap vertices) through the points.</summary>
        static GameObject AimLineCopy(KH_Frisbee disc, List<Vector3> pts, Material m)
        {
            var go = NewTemp("AimLine");
            var src = disc.aimLine;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthCurve = src.widthCurve; line.widthMultiplier = src.widthMultiplier * AimLegibility;
            line.colorGradient = new Gradient();   // white: the colour is the material's (unlit)
            line.numCornerVertices = src.numCornerVertices; line.numCapVertices = src.numCapVertices;
            line.alignment = src.alignment; line.textureMode = src.textureMode;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            line.sharedMaterial = m;
            line.positionCount = pts.Count; line.SetPositions(pts.ToArray());
            line.enabled = true;
            return go;
        }

        /// <summary>KH_Frisbee.UpdateAim's preview: _PositionAt every aimStep, cut at the first obstacle (obstacleMask).</summary>
        static List<Vector3> Trajectory(KH_Frisbee disc, Vector3 p0, Vector3 v0, out Vector3 end, out bool hitObstacle)
        {
            int n = disc.aimPoints < 2 ? 2 : disc.aimPoints;
            var pts = new List<Vector3>(n);
            Vector3 prev = p0; end = p0; hitObstacle = false;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = disc._PositionAt(p0, v0, 0f, i * disc.aimStep);
                Vector3 d = p - prev; float len = d.magnitude;
                RaycastHit hit;
                if (i > 0 && len > 1e-4f && Physics.Raycast(prev, d / len, out hit, len, disc.obstacleMask, QueryTriggerInteraction.Ignore))
                { pts.Add(hit.point); end = hit.point; hitObstacle = !(hit.collider is TerrainCollider); break; }
                pts.Add(p); end = p; prev = p;
            }
            return pts;
        }

        static Vector3 ThrowVelocity(KH_Frisbee disc, Vector3 flatDir)
        {
            float speed = Mathf.Lerp(disc.aimSpeedMin, disc.aimSpeedMax, ThrowPower);   // KH_Frisbee.AimSpeedNow at power 0.5
            float r = ThrowPitchDeg * Mathf.Deg2Rad;
            return (flatDir * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r)) * speed;
        }

        static Vector3 HeldAt(Vector3 eye, Vector3 flatDir)
        {
            var right = Vector3.Cross(Vector3.up, flatDir).normalized;
            return eye + flatDir * HandForward + right * HandRight - Vector3.up * HandDown;
        }

        /// <summary>The disc held in front of the eye (F3 / N1), tilted halfway toward the camera so its face reads.</summary>
        static GameObject HeldDisc(WorldCtx c, Vector3 eye, Vector3 flatDir, out Vector3 at)
        {
            at = HeldAt(eye, flatDir);
            var copy = RenderCopy(c.disc.transform, "HeldDisc", ActiveRenderer);
            var n = Vector3.Slerp(Vector3.up, (eye - at).normalized, 0.45f);
            copy.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.up, n) * Quaternion.LookRotation(flatDir, Vector3.up));
            return copy;
        }

        static GameObject MarkerCopy(WorldCtx c, Vector3 end, bool outside)
        {
            var cross = c.disc.aimMarkerCross.transform;
            // the marker body takes the line's state material (KH_Frisbee.SetAimOutside); the x bars are aimOutsideMaterial, shaded
            var body = Unlit(outside ? c.disc.aimOutsideMaterial : c.disc.aimMaterial, 1f);
            var bars = Unlit(c.disc.aimOutsideMaterial, CrossShade);
            var copy = RenderCopy(c.disc.aimMarker, outside ? "AimMarkerOut" : "AimMarker", r => outside || !r.transform.IsChildOf(cross),
                r => Enumerable.Repeat(r.transform.IsChildOf(cross) ? bars : body, Mathf.Max(1, r.sharedMaterials.Length)).ToArray());
            copy.transform.SetPositionAndRotation(end + Vector3.up * 0.02f, Quaternion.identity);   // KH_Frisbee.UpdateAim
            copy.transform.localScale = Vector3.one * AimLegibility;
            return copy;
        }

        /// <summary>The hand drawing under the held disc, sized to stay wholly inside the cut.</summary>
        static void HandUnderDisc(CutWork w, Rect dr)
        {
            var grip = new Vector2(dr.center.x + dr.width * 0.05f, dr.yMin + dr.height * 0.30f);
            float s = dr.width * 0.5f;
            // DrawHand's box spans grip + (-0.8 .. 0.7, -1.6 .. 0.2) * s; keep 6 px inside the cut
            s = Mathf.Min(s, (grip.y - 6f) / 1.6f, (grip.x - 6f) / 0.8f, (CutW - 6f - grip.x) / 0.7f);
            if (s < 22f) { w.warnings.Add(w.id + ": too little room under the disc for the hand (scale " + s.ToString("F0") + "): hand left out"); return; }
            DrawHand(w.final, grip, s, w.marks, "hand holding the disc");
        }

        // ================================================================ the posed dog (preview scene -> baked meshes)
        /// <summary>Clones KD_Dog_{breed} from the port source scene into a preview scene, poses it (jump clip at CatchAt, or the
        /// idle clip), bakes the visible body into static meshes in the dog root's frame and returns a HideAndDontSave holder
        /// in the active scene. mouth / fwd are in the same frame (root at the origin, ground at y = 0).</summary>
        static GameObject PosedDog(string breed, bool jump, out Vector3 mouth, out Vector3 fwd, List<string> notes)
        {
            string sourcePath = KH_FrisbeeJumpClipEditor.SourceScene;
            if (!File.Exists(sourcePath)) throw new InvalidOperationException(sourcePath + " is missing: it holds the dog the jump clips were authored on (needed for F4 / B2; F4 can use Source~/F4.png instead)");
            AnimationClip clip; float time;
            if (jump)
            {
                var v = KH_FrisbeeJumpClipEditor.Variants(breed)[0];
                clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(v.ClipPath);
                if (clip == null) throw new InvalidOperationException(v.ClipPath + " is missing: run Frisbee/2 Build Jump Clip first");
                time = v.CatchAt;
            }
            else { clip = KH_FrisbeeJumpClipEditor.IdleClip(breed); time = 0f; }

            var preview = EditorSceneManager.NewPreviewScene();
            var source = SceneManager.GetSceneByPath(sourcePath);
            bool openedHere = !(source.IsValid() && source.isLoaded);
            try
            {
                var holder = new GameObject(TmpPrefix + "DogHolder");
                SceneManager.MoveGameObjectToScene(holder, preview);
                if (openedHere) source = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
                Transform found = null;
                foreach (var root in source.GetRootGameObjects())
                {
                    found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "KD_Dog_" + breed);
                    if (found != null) break;
                }
                if (found == null) throw new InvalidOperationException("KD_Dog_" + breed + " not found in " + sourcePath);
                // instantiated straight under a preview-scene parent: the clone never lives in the production scene.
                // Same order as KH_FrisbeeJumpClipEditor.CloneSourceDog: rigid while the source is open, then close, then pose.
                var dog = Object.Instantiate(found.gameObject, holder.transform);
                dog.name = "KD_Dog_" + breed;
                dog.transform.localPosition = Vector3.zero; dog.transform.localRotation = Quaternion.identity;
                dog.SetActive(true);
                KH_FrisbeeJumpClipEditor.MakeBodyRigid(dog);
                if (openedHere) { EditorSceneManager.CloseScene(source, true); openedHere = false; }
                clip.SampleAnimation(dog, time); KH_DogRig.ApplyConstraints(dog);
                var rig = KH_DogRig.Resolve(dog, KH_FrisbeeJumpClipEditor.MouthAnchorName);
                if (!rig.Ok) throw new InvalidOperationException(breed + " rig: " + string.Join("; ", rig.problems));
                mouth = rig.mouthAnchor.position; fwd = KH_FrisbeeJumpClipEditor.Forward(rig);
                notes.Add("dog KD_Dog_" + breed + " from " + sourcePath + ", clip " + clip.name + " t=" + time.ToString("F2") + ", mouth " + V(mouth));

                var baked = NewTemp("Dog_" + breed + (jump ? "_Catch" : "_Idle"));
                int parts = 0;
                foreach (var r in KH_FrisbeeJumpClipEditor.Bodies(dog))
                {
                    // KH_FrisbeeJumpClipEditor.RenderOne's bake (BakeMesh(useScale) then TransformPoint), keeping the real materials
                    var mesh = TrackMesh(new Mesh { name = TmpPrefix + "DogBody" });
                    r.BakeMesh(mesh, true);
                    var m = r.transform.localToWorldMatrix; var nm = m.inverse.transpose;
                    var v = mesh.vertices; var n = mesh.normals; var t = mesh.tangents;
                    for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                    for (int i = 0; i < n.Length; i++) n[i] = nm.MultiplyVector(n[i]).normalized;
                    for (int i = 0; i < t.Length; i++) { var tv = m.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized; t[i] = new Vector4(tv.x, tv.y, tv.z, t[i].w); }
                    mesh.vertices = v; if (n.Length == v.Length) mesh.normals = n; if (t.Length == v.Length) mesh.tangents = t;
                    mesh.RecalculateBounds();
                    var child = new GameObject(TmpPrefix + r.name) { hideFlags = HideFlags.HideAndDontSave };
                    child.transform.SetParent(baked.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = child.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = r.sharedMaterials; mr.shadowCastingMode = r.shadowCastingMode; mr.receiveShadows = r.receiveShadows;
                    parts++;
                }
                if (parts == 0) throw new InvalidOperationException("KD_Dog_" + breed + ": no visible body to bake (KH_FrisbeeJumpClipEditor.Bodies)");
                notes.Add("baked " + parts + " body mesh(es); accessories / collar / costumes left out (KH_FrisbeeJumpClipEditor.Bodies)");
                return baked;
            }
            finally
            {
                if (openedHere && source.IsValid() && source.isLoaded) EditorSceneManager.CloseScene(source, true);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // ================================================================ search scaffolding
        /// <summary>Camera placed, subjects checked: is the frustum clean? Records the reason when not. The shot stays alive
        /// when clean (the caller renders and disposes it).</summary>
        static bool CleanView(WorldCtx c, CutWork w, WorldShot shot, string label, List<string> rejects)
        {
            var bad = ForbiddenInView(shot.cam, c.forbid);
            if (bad.Count == 0) return true;
            rejects.Add(label + ": " + string.Join(", ", bad.Take(3)) + (bad.Count > 3 ? " (+" + (bad.Count - 3) + ")" : ""));
            return false;
        }

        static void Accept(WorldCtx c, CutWork w, WorldShot shot, string label, List<string> rejects, Dictionary<string, Rect> subjects)
        {
            w.raw = shot.Render();
            w.final = w.raw.Half();
            w.notes.Add("viewpoint " + label + " (" + rejects.Count + " candidate(s) rejected before it)");
            foreach (var r in rejects.Take(8)) w.notes.Add("  rejected " + r);
            w.notes.Add(shot.Describe()); w.notes.Add("sky: " + shot.sky);
            foreach (var kv in subjects) w.subjects.Add(kv.Key + " " + kv.Value);
            w.forbidden.AddRange(ForbiddenInView(shot.cam, c.forbid));   // re-tested: must be empty
        }

        static Exception NoView(CutWork w, List<string> rejects)
        {
            return new InvalidOperationException(w.id + ": no viewpoint with a clean frustum and the subjects in frame (" + rejects.Count + " rejected): "
                + string.Join(" | ", rejects.Take(6)));
        }

        // ================================================================ the cuts
        static void ShotF2(WorldCtx c, CutWork w)
        {
            var spec = View("F2");
            var rejects = new List<string>();
            foreach (var f in PlazaFrames(c))
            {
                DestroyAllTemps();
                RaycastHit h;
                if (!GroundHit(f.stand + f.dir * c.recallDistance, c.groundMask, out h)) { rejects.Add(f.label + ": no ground under the disc"); continue; }
                var disc = RenderCopy(c.disc.transform, "Disc", ActiveRenderer);
                PlaceOnGround(disc, h, f.dir);
                var eye = f.stand + Vector3.up * spec.eye;
                var db = CopyBounds(disc);
                // third pass: the pitch puts the disc's centre at F2DiscHeight of the cut (the first two runs had it ~20 px off
                // the bottom edge with a fixed 35 deg): tan(elev - pitch) = (0.5 - F2DiscHeight) * 2 tan(vfov / 2)
                var d = db.center - eye; float along = Mathf.Max(0.05f, Vector3.Dot(d, f.dir));
                float elev = Mathf.Atan2(-d.y, along) * Mathf.Rad2Deg;
                float pitch = elev - Mathf.Atan((0.5f - F2DiscHeight) * 2f * Mathf.Tan(spec.vfov * 0.5f * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
                var shot = new WorldShot(eye, WorldShot.Forward(f.dir, pitch), spec.vfov);
                var why = new List<string>(); Rect dr;
                bool ok = Subject(w, shot, "disc", db, 40f, 60f, out dr, why);
                if (ok && dr.width < 60f) { why.Add("disc only " + dr.width.ToString("F0") + " px wide"); ok = false; }
                if (!ok) { rejects.Add(f.label + ": " + string.Join(", ", why)); shot.Dispose(); continue; }
                if (!CleanView(c, w, shot, f.label, rejects)) { shot.Dispose(); continue; }
                Accept(c, w, shot, f.label, rejects, new Dictionary<string, Rect> { { "disc", dr } });
                shot.Dispose();
                w.notes.Add("F2 pitch " + pitch.ToString("F1") + " deg down: the disc centre at " + (dr.center.y / CutH * 100f).ToString("F0") + " % of the height (wanted " + (F2DiscHeight * 100f).ToString("F0") + " %)");
                var ring = DrawRing(w.final, dr, 10f, w.marks, "the disc in front of you");
                var from = ClampIn(ring.center + new Vector2(-Mathf.Max(110f, ring.width * 0.9f), Mathf.Max(80f, ring.height * 0.8f)), 30f);
                DrawArrow(w.final, from, EllipseEdge(ring, from, 2f), w.marks, "points at the disc");
                return;
            }
            throw NoView(w, rejects);
        }

        static void ShotF3(WorldCtx c, CutWork w)
        {
            var spec = View("F3");
            var rejects = new List<string>();
            foreach (var f in PlazaFrames(c))
            {
                DestroyAllTemps();
                var eye = f.stand + Vector3.up * spec.eye;
                var at0 = HeldAt(eye, f.dir);
                Vector3 end; bool blocked;
                var pts = Trajectory(c.disc, at0, ThrowVelocity(c.disc, f.dir), out end, out blocked);
                if (blocked) { rejects.Add(f.label + ": the path hits an obstacle at " + V(end)); continue; }
                RaycastHit lg;
                if (!GroundHit(end, c.groundMask, out lg) || Slope(lg.normal) > c.runMaxSlopeDeg) { rejects.Add(f.label + ": landing not playable"); continue; }
                if (Flat(end - at0) < 7f) { rejects.Add(f.label + ": landing only " + Flat(end - at0).ToString("F1") + " m away"); continue; }
                Vector3 at;
                var held = HeldDisc(c, eye, f.dir, out at);
                AimLineCopy(c.disc, pts, Unlit(c.disc.aimMaterial, 1f));
                var marker = MarkerCopy(c, end, false);
                float pitch = SolvePitch(eye, f.dir, spec.vfov, PreferredPitch, at - Vector3.up * 0.2f, end);
                var shot = new WorldShot(eye, WorldShot.Forward(f.dir, pitch), spec.vfov);
                var why = new List<string>(); Rect dr, mr;
                bool ok = Subject(w, shot, "held disc", CopyBounds(held), 6f, 20f, out dr, why) & Subject(w, shot, "landing marker", CopyBounds(marker), 30f, 6f, out mr, why);
                if (ok && dr.width > 0.25f * CutW) { why.Add("held disc wider than 25 % (" + dr.width.ToString("F0") + " px)"); ok = false; }
                if (ok && dr.yMin < 40f) { why.Add("held disc too low for the hand (" + dr.yMin.ToString("F0") + " px)"); ok = false; }
                if (!ok) { rejects.Add(f.label + ": " + string.Join(", ", why)); shot.Dispose(); continue; }
                if (!CleanView(c, w, shot, f.label, rejects)) { shot.Dispose(); continue; }
                Accept(c, w, shot, f.label, rejects, new Dictionary<string, Rect> { { "held disc", dr }, { "landing marker", mr } });
                shot.Dispose();
                w.notes.Add("throw: v0 " + Lerp(c.disc).ToString("F2") + " m/s at " + ThrowPitchDeg + " deg, " + pts.Count + " points, landing " + V(end) + " (" + Flat(end - at).ToString("F1") + " m), slope " + Slope(lg.normal).ToString("F1") + " deg; line/marker unlit KH_FrisbeeAim colour, x" + AimLegibility);
                HandUnderDisc(w, dr);
                var ring = DrawRing(w.final, mr, 10f, w.marks, "the landing marker");
                var from = ClampIn(ring.center + new Vector2(-95f, 90f), 30f);
                DrawArrow(w.final, from, EllipseEdge(ring, from, 2f), w.marks, "points at the landing marker");
                return;
            }
            throw NoView(w, rejects);
        }

        static void ShotF4(WorldCtx c, CutWork w)
        {
            if (File.Exists(F4OverridePath))
            {
                var bytes = File.ReadAllBytes(F4OverridePath);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    if (!tex.LoadImage(bytes)) throw new InvalidOperationException(F4OverridePath + " is not a readable PNG/JPG");
                    var photo = Img.FromTexture(tex);
                    float want = (float)CutW / CutH, have = (float)photo.w / photo.h;
                    var crop = have > want ? new Rect((photo.w - photo.h * want) * 0.5f, 0f, photo.h * want, photo.h) : new Rect(0f, (photo.h - photo.w / want) * 0.5f, photo.w, photo.w / want);
                    w.raw = photo; w.source = "user-photo";
                    w.final = photo.Resample(crop, CutW, CutH);
                    w.notes.Add("F4 from " + F4OverridePath + " (" + photo.w + "x" + photo.h + ", centre crop to 4:3); ring on the middle third (plan §4.2: the dog in the centre third). Forbidden objects not checked (a photo).");
                    DrawRing(w.final, new Rect(CutW / 3f, CutH * 0.22f, CutW / 3f, CutH * 0.56f), 6f, w.marks, "the dog catching (user photo, centre third)");
                }
                finally { Object.DestroyImmediate(tex); }
                return;
            }
            var spec = View("F4");
            var spec2 = KH_FrisbeeJumpClipEditor.SpecFor(c.breed);
            float rise = (MinRootRise + spec2.maxRootRise) * 0.5f;
            var rejects = new List<string>();
            var dogNotes = new List<string>();
            Vector3 mouth, fwd;
            var dog = PosedDog(c.breed, true, out mouth, out fwd, dogNotes);   // posed once, re-placed per candidate
            foreach (var f in PlazaFrames(c))
            {
                var catchXZ = f.stand + f.dir * spec.ahead;
                RaycastHit g;
                if (!GroundHit(catchXZ, c.groundMask, out g)) { rejects.Add(f.label + ": no ground under the catch"); continue; }
                foreach (float side in new[] { 1f, -1f })
                {
                    DestroyAllTemps(dog);
                    var rot = Yaw(fwd, f.dir);
                    var root = new Vector3(catchXZ.x, g.point.y + rise, catchXZ.z) - rot * new Vector3(mouth.x, 0f, mouth.z);
                    dog.transform.SetPositionAndRotation(root, rot);
                    var mouthWorld = root + rot * mouth;
                    // KH_FrisbeeCatch.PostLateUpdate (carrying): mouth + forward * carryForward + up * carryUp, LookRotation(fwd, up) * tilt
                    Vector3 dfw = rot * Vector3.forward, dup = rot * Vector3.up;
                    var disc = RenderCopy(c.disc.transform, "CaughtDisc", ActiveRenderer);
                    disc.transform.SetPositionAndRotation(mouthWorld + dfw * c.catcher.carryForward + dup * c.catcher.carryUp, Quaternion.LookRotation(dfw, dup) * Quaternion.Euler(c.catcher.carryTiltDeg, 0f, 0f));
                    var body = CopyBounds(dog); var discB = CopyBounds(disc); body.Encapsulate(discB);
                    // three-quarter front view, a little above the disc so its face shows
                    var camDir = (f.dir * Mathf.Cos(40f * Mathf.Deg2Rad) + f.right * side * Mathf.Sin(40f * Mathf.Deg2Rad)).normalized;
                    RaycastHit cg;
                    string label = f.label + (side > 0 ? " right" : " left");
                    if (!GroundHit(catchXZ + camDir * 4.5f, c.groundMask, out cg)) { rejects.Add(label + ": no ground under the camera"); continue; }
                    var eye = cg.point + Vector3.up * spec.eye;
                    RaycastHit block;
                    if (Physics.Linecast(eye, body.center, out block, ~0, QueryTriggerInteraction.Ignore)) { rejects.Add(label + ": view blocked by " + PathOf(block.transform)); continue; }
                    var look = body.center - eye; var flat = new Vector3(look.x, 0f, look.z).normalized;
                    float pitch = Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
                    var shot = new WorldShot(eye, WorldShot.Forward(flat, pitch), spec.vfov);
                    var why = new List<string>(); Rect br, dr;
                    bool ok = Subject(w, shot, "dog", body, 10f, 80f, out br, why) & Subject(w, shot, "disc at the mouth", discB, 10f, 16f, out dr, why);
                    if (!ok) { rejects.Add(label + ": " + string.Join(", ", why)); shot.Dispose(); continue; }
                    if (!CleanView(c, w, shot, label, rejects)) { shot.Dispose(); continue; }
                    w.notes.AddRange(dogNotes);
                    Accept(c, w, shot, label, rejects, new Dictionary<string, Rect> { { "dog", br }, { "disc at the mouth", dr } });
                    shot.Dispose();
                    w.notes.Add("catch pose: root rise " + rise.ToString("F2") + " m (between the planner's " + MinRootRise + " and maxRootRise " + spec2.maxRootRise.ToString("F2") + "), mouth " + V(mouthWorld) + ", disc carried as KH_FrisbeeCatch does (forward " + c.catcher.carryForward + ", up " + c.catcher.carryUp + ", tilt " + c.catcher.carryTiltDeg + ")");
                    DrawRing(w.final, br, 8f, w.marks, "the dog catching the disc");
                    return;
                }
            }
            DestroyAllTemps();
            throw NoView(w, rejects);
        }

        // ---------------------------------------------------------------- N1: the game's own "cannot go there" verdict
        // KH_Frisbee.UpdateAim (KH_Frisbee.cs:429-434) greys the line and shows the x when
        //   _aimBad = catcher._TooFar(p0) || !catcher._PlayableAt(p0) || !catcher._Reachable(end)
        // and _Reachable (KH_FrisbeeCatch.Planner.cs:132-137) is
        //   DiscInFloatWater(at) ? WaterFetchOn() && WaterDepthOk(at) : _PlayableAt(at) && _PathOk(dogRoot.position, (at.x, _playY, at.z)).
        // The verdict below runs exactly those members of the scene's catcher (public ones directly, the private ones by
        // reflection) in that order. Two deliberate differences: _TooFar is left out (the cut must show "the dog cannot go
        // there", not "your dog is far away" - the aimFarHint case), and the dog starts beside the player (dogPos) instead of at
        // dogRoot's saved Edit position, as it would when its owner throws. The catcher's private counters (_planRays, _playY)
        // are not serialized; the end-of-run dirty check still guards the scene.
        sealed class Verdict { public bool blocked, evaluated; public string reason; public float weight; }

        static object CallPrivate(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (m == null) throw new MissingMethodException(o.GetType().Name, name);
            try { return m.Invoke(o, args); }
            catch (System.Reflection.TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        static float ReadPrivateFloat(object o, string name)
        {
            var f = o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f == null) throw new MissingFieldException(o.GetType().Name, name);
            return (float)f.GetValue(o);
        }

        static Verdict GameVerdict(WorldCtx c, Vector3 p0, Vector3 end, Vector3 dogPos)
        {
            var k = c.catcher;
            var v = new Verdict { evaluated = true };
            try
            {
                if (!k._PlayableAt(p0)) { v.blocked = true; v.reason = "throw point not playable"; v.weight = -100f; return v; }
                if ((bool)CallPrivate(k, "DiscInFloatWater", end))
                {
                    bool on = (bool)CallPrivate(k, "WaterFetchOn");
                    if (on && (bool)CallPrivate(k, "WaterDepthOk", end)) { v.reason = "floats in water the dog fetches from"; return v; }
                    v.blocked = true; v.weight = 6f;
                    v.reason = on ? "lands in water too deep for this dog (WaterDepthOk false)" : "lands in water and the water fetch is off (WaterFetchOn false)";
                    return v;
                }
                if (!k._PlayableAt(end))
                {
                    v.blocked = true;
                    if ((bool)CallPrivate(k, "InRunRiver", end)) { v.reason = "lands on the river band where the dog may not run (InRunRiver)"; v.weight = 10f; return v; }
                    RaycastHit hit;
                    if (Physics.Raycast(end + Vector3.up * k.groundRayOffset, Vector3.down, out hit, k.groundRayDistance, k.groundLayerMask, QueryTriggerInteraction.Ignore))
                    { v.reason = "landing slope " + Slope(hit.normal).ToString("F0") + " deg > runMaxSlopeDeg " + k.runMaxSlopeDeg.ToString("F0"); v.weight = 8f; }
                    else { v.reason = "no ground within groundRayDistance under the landing"; v.weight = 1f; }
                    return v;
                }
                float playY = ReadPrivateFloat(k, "_playY");
                var to = new Vector3(end.x, playY, end.z);
                if (!k._PathOk(dogPos, to))
                {
                    v.blocked = true;
                    if ((bool)CallPrivate(k, "PathCrossesRiver", dogPos, to)) { v.reason = "the dog's way there crosses the river (PathCrossesRiver)"; v.weight = 10f; }
                    else { v.reason = "the dog's way there is cut by a step or drop (PathWalk)"; v.weight = 3f; }
                    return v;
                }
                v.reason = "reachable";
                return v;
            }
            catch (Exception ex) { v.evaluated = false; v.reason = "predicate threw " + ex.GetType().Name + ": " + ex.Message; return v; }
        }

        static void Count(Dictionary<string, int> hist, string reason) { int n; hist.TryGetValue(reason, out n); hist[reason] = n + 1; }

        static string Histogram(Dictionary<string, int> hist)
        {
            return string.Join("; ", hist.OrderByDescending(kv => kv.Value).Select(kv => kv.Value + " x " + kv.Key));
        }

        /// <summary>Stand points for N1: the plaza grid, the walking route every ~RouteStep m, and every named place.</summary>
        static List<KeyValuePair<string, Vector3>> N1Stands(WorldCtx c, CutWork w)
        {
            var stands = new List<KeyValuePair<string, Vector3>>();
            void Add(string name, Vector3 p)
            {
                RaycastHit sg;
                if (GroundHit(p, c.groundMask, out sg) && Slope(sg.normal) <= c.runMaxSlopeDeg) stands.Add(new KeyValuePair<string, Vector3>(name, sg.point));
            }
            foreach (float r in new[] { 0f, 4f, 8f })
                for (int a = 0; a < (r == 0f ? 1 : 8); a++) Add("plaza r" + r + " a" + a * 45, c.plaza + Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward * r);
            var route = c.pouch.manager != null ? c.pouch.manager.route : null;
            if (route != null && route.nodes != null)
            {
                int n = route.nodes.Length; float walked = RouteStep; Vector3 prev = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    var p = route.nodePositions != null && route.nodePositions.Length == n ? route.nodePositions[i] : route.nodes[i] != null ? route.nodes[i].position : prev;
                    if (i > 0) walked += Flat(p - prev);
                    prev = p;
                    if (walked >= RouteStep) { Add("route node " + i, p); walked = 0f; }
                }
            }
            else w.notes.Add("N1: KD_TreatPouch.manager.route (KH_WalkRoute) not wired; route stand points skipped");
            var places = FindOptional("KH_Places");
            if (places != null) foreach (Transform t in places) Add("place " + t.name, t.position);
            else w.notes.Add("N1: KH_World/KH_Places not found; named places skipped");
            return stands;
        }

        static void ShotN1(WorldCtx c, CutWork w)
        {
            var spec = View("N1");
            var stands = N1Stands(c, w);
            var hist = new Dictionary<string, int>();
            var cands = new List<KeyValuePair<float, object[]>>();   // score -> [label, stand, dir, end, Verdict, dogPos]
            int tried = 0;
            foreach (var st in stands)
            {
                var eye = st.Value + Vector3.up * spec.eye;
                for (int yaw = 0; yaw < 360; yaw += N1YawStep)
                {
                    var d = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    var right = Vector3.Cross(Vector3.up, d);
                    var p0 = HeldAt(eye, d);
                    Vector3 end; bool obstacle;
                    Trajectory(c.disc, p0, ThrowVelocity(c.disc, d), out end, out obstacle);
                    tried++;
                    float dist = Flat(end - p0);
                    if (dist < 4f || dist > 18f) { Count(hist, "landing " + (dist < 4f ? "under 4 m" : "over 18 m") + " away"); continue; }
                    RaycastHit dg;
                    var dogPos = GroundHit(st.Value + right * 1.2f, c.groundMask, out dg) ? dg.point : st.Value;
                    var v = GameVerdict(c, p0, end, dogPos);
                    if (!v.evaluated) { Count(hist, v.reason); continue; }
                    if (!v.blocked) { Count(hist, "not blocked: " + v.reason); continue; }
                    if (v.weight < 0f) { Count(hist, "blocked for the wrong reason: " + v.reason); continue; }
                    RaycastHit sight;
                    if (Physics.Linecast(eye, end + Vector3.up * 0.15f, out sight, ~0, QueryTriggerInteraction.Ignore) && Vector3.Distance(sight.point, end) > 0.6f) { Count(hist, "landing hidden from the eye"); continue; }
                    float score = v.weight - (obstacle ? 4f : 0f) - Mathf.Abs(dist - 10f) * 0.3f;
                    cands.Add(new KeyValuePair<float, object[]>(score, new object[] { st.Key + " yaw" + yaw, st.Value, d, end, v, dogPos }));
                }
            }
            var rejects = new List<string>();
            foreach (var cand in cands.OrderByDescending(x => x.Key).Take(N1MaxViews))
            {
                DestroyAllTemps();
                string label = (string)cand.Value[0]; var stand = (Vector3)cand.Value[1]; var dir = (Vector3)cand.Value[2]; var v = (Verdict)cand.Value[4];
                var eyeN = stand + Vector3.up * spec.eye;
                Vector3 at;
                var held = HeldDisc(c, eyeN, dir, out at);
                Vector3 endN; bool obstacleN;
                var pts = Trajectory(c.disc, at, ThrowVelocity(c.disc, dir), out endN, out obstacleN);
                AimLineCopy(c.disc, pts, Unlit(c.disc.aimOutsideMaterial, 1f));
                var marker = MarkerCopy(c, endN, true);
                float pitch = SolvePitch(eyeN, dir, spec.vfov, PreferredPitch, at - Vector3.up * 0.2f, endN);
                var shot = new WorldShot(eyeN, WorldShot.Forward(dir, pitch), spec.vfov);
                var why = new List<string>(); Rect dr, mr;
                bool okDisc = Subject(w, shot, "held disc", CopyBounds(held), 6f, 20f, out dr, why);
                bool okMark = Subject(w, shot, "x marker", CopyBounds(marker), 30f, 12f, out mr, why);
                if (!okMark) { Count(hist, "view: x marker outside the cut or under 12 px"); rejects.Add(label + ": " + string.Join(", ", why)); shot.Dispose(); continue; }
                if (!okDisc || dr.yMin < 40f) { Count(hist, "view: held disc outside / too low"); rejects.Add(label + ": " + string.Join(", ", why)); shot.Dispose(); continue; }
                var bad = ForbiddenInView(shot.cam, c.forbid);
                if (bad.Count > 0)
                {
                    foreach (var kind in bad.Select(b => b.Split(' ')[0]).Distinct()) Count(hist, "view: forbidden " + kind + " in frame");
                    rejects.Add(label + ": " + string.Join(", ", bad.Take(3)));
                    shot.Dispose(); continue;
                }
                Accept(c, w, shot, label, rejects, new Dictionary<string, Rect> { { "held disc", dr }, { "x marker", mr } });
                shot.Dispose();
                w.blocked = v.reason + " | stand " + label + " " + V(stand) + ", direction " + V(dir) + ", landing " + V(endN) + (obstacleN ? " (line stopped by an obstacle)" : "");
                w.notes.Add("N1 verdict (KH_Frisbee.UpdateAim / KH_FrisbeeCatch._Reachable, dog beside the player): " + w.blocked);
                w.notes.Add("N1 search: " + tried + " throws from " + stands.Count + " stand points, " + cands.Count + " blocked candidates; histogram: " + Histogram(hist));
                w.notes.Add("line / marker: unlit KH_FrisbeeAimOut grey, x bars x" + CrossShade + ", x" + AimLegibility + " size");
                HandUnderDisc(w, dr);
                DrawRing(w.final, mr, 12f, w.marks, "the x marker (the dog cannot go there)");
                return;
            }
            DestroyAllTemps();
            throw new InvalidOperationException("N1: no usable view. " + tried + " throws from " + stands.Count + " stand points (plaza, route every " + RouteStep + " m, places; every " + N1YawStep + " deg), "
                + cands.Count + " the game would grey out. Reasons: " + Histogram(hist));
        }

        static void ShotB2(WorldCtx c, CutWork w)
        {
            var spec = View("B2");
            var rejects = new List<string>();
            var dogNotes = new List<string>();
            Vector3 mouth, fwd;
            var dog = PosedDog(c.breed, false, out mouth, out fwd, dogNotes);   // posed once, re-placed per candidate
            foreach (var f in PlazaFrames(c))
            {
                DestroyAllTemps(dog);
                RaycastHit bg, dg;
                if (!GroundHit(f.stand + f.dir * spec.ahead, c.groundMask, out bg)) { rejects.Add(f.label + ": no ground under the ball"); continue; }
                if (!GroundHit(f.stand + f.dir * BallDogAhead, c.groundMask, out dg)) { rejects.Add(f.label + ": no ground under the dog"); continue; }
                var ball = RenderCopy(c.ball, "Ball", ActiveRenderer);
                PlaceOnGround(ball, bg, f.dir);
                dog.transform.SetPositionAndRotation(dg.point, Yaw(fwd, -f.dir));   // facing the player
                var ballB = CopyBounds(ball); var dogB = CopyBounds(dog);
                var eye = f.stand + Vector3.up * spec.eye;
                float pitch = SolvePitch(eye, f.dir, spec.vfov, float.NaN, ballB.min, ballB.max, dogB.min, dogB.max);
                var shot = new WorldShot(eye, WorldShot.Forward(f.dir, pitch), spec.vfov);
                var why = new List<string>(); Rect br, dr;
                bool ok = Subject(w, shot, "ball", ballB, 14f, 14f, out br, why) & Subject(w, shot, "dog", dogB, 14f, 50f, out dr, why);
                if (!ok) { rejects.Add(f.label + ": " + string.Join(", ", why)); shot.Dispose(); continue; }
                if (!CleanView(c, w, shot, f.label, rejects)) { shot.Dispose(); continue; }
                w.notes.AddRange(dogNotes);
                Accept(c, w, shot, f.label, rejects, new Dictionary<string, Rect> { { "ball", br }, { "dog", dr } });
                shot.Dispose();
                var ring = DrawRing(w.final, br, 8f, w.marks, "the ball in front of you");
                // the arc bows to the left and ends at the dog's flank (first run: a straight arc ran up the middle and hid the dog)
                Vector2 a = new Vector2(ring.xMin + ring.width * 0.25f, ring.yMax - 2f), b = new Vector2(dr.xMin - 10f, dr.center.y);
                var ctrl = new Vector2(Mathf.Min(a.x, b.x) - 110f, (a.y + b.y) * 0.5f + 40f);
                var pts = new List<Vector2>();
                for (int i = 0; i <= 24; i++) { float t = i / 24f; pts.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctrl + t * t * b); }
                for (int i = 0; i < pts.Count; i++) pts[i] = ClampIn(pts[i], 14f);
                DrawDashedArrow(w.final, pts, w.marks, "the throw toward the dog");
                return;
            }
            DestroyAllTemps();
            throw NoView(w, rejects);
        }

        // ================================================================ helpers
        static ViewSpec View(string cut) { return Views.First(v => v.cut == cut); }
        static float Lerp(KH_Frisbee d) { return Mathf.Lerp(d.aimSpeedMin, d.aimSpeedMax, ThrowPower); }

        /// <summary>A rotation about world up only that turns the dog's forward onto 'to' (FromToRotation could roll the dog
        /// over when the two are opposite).</summary>
        static Quaternion Yaw(Vector3 from, Vector3 to)
        {
            from.y = 0f; to.y = 0f;
            return Quaternion.Euler(0f, Vector3.SignedAngle(from, to, Vector3.up), 0f);
        }

        /// <summary>The point on a ring's ellipse (its rectangle) toward 'toward', pushed out by 'gap' px.</summary>
        static Vector2 EllipseEdge(Rect ring, Vector2 toward, float gap)
        {
            var c = ring.center; var u = (toward - c); if (u.sqrMagnitude < 1e-4f) u = Vector2.up; u.Normalize();
            float a = ring.width * 0.5f, b = ring.height * 0.5f;
            float t = 1f / Mathf.Sqrt((u.x / a) * (u.x / a) + (u.y / b) * (u.y / b));
            return c + u * (t + gap);
        }

        static Vector2 ClampIn(Vector2 p, float margin)
        {
            return new Vector2(Mathf.Clamp(p.x, margin, CutW - margin), Mathf.Clamp(p.y, margin, CutH - margin));
        }

        /// <summary>Ground with the dogs' own mask (KH_FrisbeeCatch.groundLayerMask): a ray from 30 m above down 80 m.</summary>
        static bool GroundHit(Vector3 p, LayerMask mask, out RaycastHit hit)
        {
            Physics.SyncTransforms();
            return Physics.Raycast(new Vector3(p.x, p.y + 30f, p.z), Vector3.down, out hit, 80f, mask, QueryTriggerInteraction.Ignore);
        }

        static float Slope(Vector3 normal) { return Vector3.Angle(normal, Vector3.up); }
        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
        static string V(Vector3 p) { return "(" + p.x.ToString("F2") + ", " + p.y.ToString("F2") + ", " + p.z.ToString("F2") + ")"; }

        static string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        /// <summary>Exactly one object of this name in the loaded scene (inactive included), or a clear exception.</summary>
        static Transform FindUnique(string name, string what)
        {
            var all = new List<Transform>();
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) all.Add(t);
            if (all.Count == 0) throw new InvalidOperationException("'" + name + "' not found in " + scene.path + ": " + what);
            if (all.Count > 1) throw new InvalidOperationException("'" + name + "' found " + all.Count + " times in " + scene.path + " (" + string.Join(", ", all.Select(PathOf)) + ")");
            return all[0];
        }

        static Transform FindOptional(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            return null;
        }
    }
}
