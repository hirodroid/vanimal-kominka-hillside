using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>Entrance (spawn area) survey for layout work (docs/plans/2026-10-06-spawn-area-layout.md).
    /// Edit mode only, temporary camera, no scene mutation. Writes to Temp/spawn-survey/&lt;utc&gt;/:
    /// a 360-degree ring of 10 shots from the spawn eye point, two top-down orthographic maps, report.json
    /// (every KH_/KD_ anchored object within 45 m: position, bounds, bearing, programs), heights.csv
    /// (ground by raycast, 3 m grid, +-30 m), route.csv (KH_WalkRoute nodes within 60 m) and refs.txt.</summary>
    public static class KH_EntranceSurveyEditor
    {
        const int Width = 1600, Height = 900, MapSize = 1600;
        const int Shots = 10;
        const float Radius = 45f;

        [Serializable] public class Shot { public int index; public float yaw; public Vector3 position, euler; public string image; }
        [Serializable] public class Item
        {
            public string name, path; public Vector3 position, boundsCenter, boundsSize; public float yaw, terrainHeight, distance, bearing;
            public string programs; public int renderers;
        }
        [Serializable] public class Report
        {
            public string utc, scene, directory;
            public Vector3 spawn; public float spawnYaw, spawnGround;
            public float mapHalfExtentNear, mapHalfExtentFar;
            public List<Shot> shots = new List<Shot>();
            public List<Item> items = new List<Item>();
            public string limitations;
        }

        [MenuItem("Tools/KominkaHillside/Guide/15 - Survey Entrance (Ring, Maps, Heights)")]
        public static void Capture()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var descriptor = UnityEngine.Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            if (descriptor == null || descriptor.ReferenceCamera == null || descriptor.spawns == null || descriptor.spawns.Length == 0)
                throw new InvalidOperationException("descriptor with reference camera and spawn required");
            var spawnT = descriptor.spawns[0];
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("o"), scene = SceneManager.GetActiveScene().path,
                directory = "Temp/spawn-survey/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
                spawn = spawnT.position, spawnYaw = spawnT.eulerAngles.y,
                mapHalfExtentNear = 22f, mapHalfExtentFar = 45f,
                limitations = "Edit-mode daytime mono capture, FOV 70, eye 1.62 m, pitch +4 deg down. Ring every 36 deg from spawn yaw. Maps are orthographic top-down from 120 m. Not a VR or device image."
            };
            report.spawnGround = GroundAt(spawnT.position.x, spawnT.position.z, spawnT.position.y);
            Directory.CreateDirectory(report.directory);

            GameObject go = null; RenderTexture target = null, mapTarget = null; Texture2D texture = null, mapTexture = null; var previous = RenderTexture.active;
            try
            {
                go = new GameObject("KH_TempEntranceSurvey") { hideFlags = HideFlags.HideAndDontSave };
                var camera = go.AddComponent<Camera>();
                camera.CopyFrom(descriptor.ReferenceCamera.GetComponent<Camera>()); camera.enabled = false; camera.cameraType = CameraType.Game;
                camera.stereoTargetEye = StereoTargetEyeMask.None; camera.fieldOfView = 70; camera.aspect = (float)Width / Height;
                camera.nearClipPlane = .2f; camera.farClipPlane = 2200; camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave }; target.Create();
                texture = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                camera.targetTexture = target;
                Vector3 eye = spawnT.position + Vector3.up * 1.62f;
                for (int i = 0; i < Shots; i++)
                {
                    float yaw = (spawnT.eulerAngles.y + i * (360f / Shots)) % 360f;
                    var shot = new Shot { index = i, yaw = yaw, position = eye, euler = new Vector3(4, yaw, 0) };
                    camera.transform.SetPositionAndRotation(eye, Quaternion.Euler(shot.euler)); camera.Render(); RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply();
                    shot.image = report.directory + "/ring-" + i.ToString("D2") + "-yaw-" + Mathf.RoundToInt(yaw).ToString("D3") + ".png";
                    File.WriteAllBytes(shot.image, texture.EncodeToPNG());
                    report.shots.Add(shot);
                }
                mapTarget = new RenderTexture(MapSize, MapSize, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave }; mapTarget.Create();
                mapTexture = new Texture2D(MapSize, MapSize, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                camera.targetTexture = mapTarget; camera.aspect = 1f; camera.orthographic = true; camera.farClipPlane = 400;
                float[] extents = { report.mapHalfExtentNear, report.mapHalfExtentFar };
                string[] names = { "map-near", "map-far" };
                for (int m = 0; m < 2; m++)
                {
                    camera.orthographicSize = extents[m];
                    camera.transform.SetPositionAndRotation(spawnT.position + Vector3.up * 120f, Quaternion.Euler(90, 0, 0)); // north (+Z) up
                    camera.Render(); RenderTexture.active = mapTarget;
                    mapTexture.ReadPixels(new Rect(0, 0, MapSize, MapSize), 0, 0); mapTexture.Apply();
                    File.WriteAllBytes(report.directory + "/" + names[m] + ".png", mapTexture.EncodeToPNG());
                }
                var seen = new HashSet<Transform>();
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    var b = r.bounds; Vector3 flat = b.center - spawnT.position; flat.y = 0;
                    if (flat.magnitude > Radius) continue;
                    var anchor = Anchor(r.transform);
                    if (anchor == null || seen.Contains(anchor)) continue;
                    seen.Add(anchor);
                    var rs = anchor.GetComponentsInChildren<Renderer>(false);
                    Bounds ab = new Bounds(anchor.position, Vector3.zero); bool first = true; int count = 0;
                    foreach (var rr in rs) { if (rr is ParticleSystemRenderer || !rr.enabled) continue; if (first) { ab = rr.bounds; first = false; } else ab.Encapsulate(rr.bounds); count++; }
                    Vector3 d = anchor.position - spawnT.position; Vector3 dFlat = d; dFlat.y = 0;
                    var progs = new List<string>();
                    foreach (var u in anchor.GetComponentsInChildren<UdonBehaviour>(true)) if (u.programSource != null) progs.Add(u.programSource.name);
                    report.items.Add(new Item
                    {
                        name = anchor.name, path = Path(anchor), position = anchor.position, yaw = anchor.eulerAngles.y, boundsCenter = ab.center, boundsSize = ab.size,
                        terrainHeight = GroundAt(anchor.position.x, anchor.position.z, anchor.position.y),
                        distance = dFlat.magnitude, bearing = (Mathf.Atan2(dFlat.x, dFlat.z) * Mathf.Rad2Deg + 360f) % 360f,
                        programs = string.Join(",", progs.ToArray()), renderers = count
                    });
                }
                report.items.Sort((a, b) => a.distance.CompareTo(b.distance));
            }
            finally
            {
                RenderTexture.active = previous;
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (mapTarget != null) { mapTarget.Release(); UnityEngine.Object.DestroyImmediate(mapTarget); }
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (mapTexture != null) UnityEngine.Object.DestroyImmediate(mapTexture);
            }
            File.WriteAllText(report.directory + "/report.json", JsonUtility.ToJson(report, true));

            // ground heights (raycast), 3 m grid, +-30 m
            const int step = 3, half = 30;
            var sb = new System.Text.StringBuilder();
            for (int z = half; z >= -half; z -= step)
            {
                for (int x = -half; x <= half; x += step)
                {
                    if (x > -half) sb.Append(',');
                    sb.Append(GroundAt(spawnT.position.x + x, spawnT.position.z + z, -99f).ToString("0.0"));
                }
                sb.Append('\n');
            }
            File.WriteAllText(report.directory + "/heights.csv", sb.ToString());
            // route nodes near the spawn
            var route = UnityEngine.Object.FindObjectOfType<KH_WalkRoute>();
            var rb = new System.Text.StringBuilder();
            if (route != null)
                for (int i = 0; i < route.count; i++)
                {
                    Vector3 p = route._Position(i); Vector3 dd = p - spawnT.position; dd.y = 0;
                    if (dd.magnitude > 60f) continue;
                    rb.Append(i).Append(',').Append(p.x.ToString("0.00")).Append(',').Append(p.y.ToString("0.00")).Append(',').Append(p.z.ToString("0.00")).Append('\n');
                }
            File.WriteAllText(report.directory + "/route.csv", rb.ToString());
            File.WriteAllText(report.directory + "/refs.txt", "spawn," + spawnT.position.x.ToString("0.00") + "," + spawnT.position.y.ToString("0.00") + "," + spawnT.position.z.ToString("0.00") + "," + spawnT.eulerAngles.y.ToString("0.0") + "\n");
            File.WriteAllText("Temp/spawn-survey/latest.txt", report.directory);
            Debug.Log("[KH Entrance Survey] " + report.shots.Count + " shots, " + report.items.Count + " items -> " + report.directory);
        }

        static float GroundAt(float x, float z, float fallback)
        {
            RaycastHit hit;
            return Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out hit, 120f) ? hit.point.y : fallback;
        }

        // Highest ancestor named KH_*/KD_* below a scene root, else the scene-root child.
        static Transform Anchor(Transform t)
        {
            Transform best = null; Transform cur = t;
            while (cur != null)
            {
                if (cur.name.StartsWith("KH_") || cur.name.StartsWith("KD_")) best = cur;
                if (cur.parent == null) break;
                cur = cur.parent;
            }
            if (best != null)
            {
                if (best.parent == null && t != best)
                {
                    Transform c = t; while (c.parent != null && c.parent != best) c = c.parent; return c;
                }
                return best;
            }
            Transform top = t; while (top.parent != null && top.parent.parent != null) top = top.parent;
            return top;
        }

        static string Path(Transform t)
        {
            var parts = new List<string>(); while (t != null) { parts.Insert(0, t.name); t = t.parent; } return string.Join("/", parts.ToArray());
        }
    }
}
