using System.IO;
using System.Linq;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-FET-015: 壁の外へ飛んだボールの受け皿 (KD_BallCatch)。
    ///
    /// 外壁 (`====オブジェクト====/外壁`) の当たりの外側 <see cref="KD_DogDefs.BallCatchMargin"/> m を
    /// 4 枚のトリガー帯で囲む。ボールの Owner は帯に入った瞬間に水平速度を殺し (KD_BallController)、
    /// ボールはその場へ落ちる。犬は壁を通過してよい (2026-09-10 決定) ので、そこまで拾いに行って戻る。
    ///
    /// **物理の壁ではない** — ボールの物理材質 (KD_ToyPhysics) は bounceCombine=Maximum なので
    /// 反発 0 の壁を置いても 0.6 で跳ね、庭へ戻る経路が残る。トリガー + 速度殺しなら空中で跳ね返らない。
    /// 帯の外側 (テレインの端まで 8〜13m) は地面が平ら (-0.39〜0.0m。2026-09-16 実測) なので、床は要らない。
    ///
    /// 帯の内側の距離は外壁の**当たり (Collider) の AABB** から測る。Renderer ではない —
    /// Live 中は外壁の Renderer だけ消え、当たりは残る (particle-live-vessel.md)。</summary>
    public static partial class KD_FetchSetupEditor
    {
        static readonly string[] CatchSides = { "North", "South", "East", "West" };

        /// <summary>外壁の当たりの AABB。取れなければ false。</summary>
        static bool WallBounds(out Bounds bounds, out string why)
        {
            bounds = new Bounds(); why = "";
            GameObject wall = null; int found = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != KD_LiveSetupEditor.PurchasedObjectsRoot) continue;
                foreach (Transform c in root.transform)
                    if (c.name == KD_LiveSetupEditor.WallName) { found++; if (wall == null) wall = c.gameObject; }
            }
            if (wall == null || found != 1) { why = KD_LiveSetupEditor.PurchasedObjectsRoot + "/" + KD_LiveSetupEditor.WallName + " が 1 個でない (" + found + ")"; return false; }
            var cols = wall.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray();
            if (cols.Length == 0) { why = "外壁に Collider が無い"; return false; }
            bounds = cols[0].bounds;
            foreach (var c in cols) bounds.Encapsulate(c.bounds);
            return true;
        }

        /// <summary>帯 1 枚の中心と大きさ (world)。内面が壁の AABB + margin になる。</summary>
        static void CatchSide(int side, Bounds wall, out Vector3 center, out Vector3 size)
        {
            float m = KD_DogDefs.BallCatchMargin, t = KD_DogDefs.BallCatchThickness;
            float y0 = KD_DogDefs.BallCatchBottom, h = KD_DogDefs.BallCatchHeight;
            // 角も塞ぐ: 南北の帯を東西の帯の外面まで伸ばす
            float xLen = (wall.size.x + 2f * (m + t)), zLen = wall.size.z + 2f * m;
            float cy = y0 + h * .5f;
            switch (side)
            {
                case 0: center = new Vector3(wall.center.x, cy, wall.max.z + m + t * .5f); size = new Vector3(xLen, h, t); break;
                case 1: center = new Vector3(wall.center.x, cy, wall.min.z - m - t * .5f); size = new Vector3(xLen, h, t); break;
                case 2: center = new Vector3(wall.max.x + m + t * .5f, cy, wall.center.z); size = new Vector3(t, h, zLen); break;
                default: center = new Vector3(wall.min.x - m - t * .5f, cy, wall.center.z); size = new Vector3(t, h, zLen); break;
            }
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Fetch System/6 - Build Ball Catch")]
        public static void BuildBallCatch()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_FetchSetup] Build ball catch (IMP-FET-015)");
            if (!WallBounds(out var wall, out string why)) { log.AppendLine("FAIL " + why); Finish("Logs/imp-fet-015-build.txt", log); throw new System.Exception(why); }
            log.AppendLine("wall collider AABB min=" + wall.min.ToString("F2") + " max=" + wall.max.ToString("F2"));
            var host = KD_SceneObjects.Find(KD_DogDefs.BallCatchName, out int count);
            if (count > 1) throw new System.Exception("Duplicate " + KD_DogDefs.BallCatchName);
            if (host == null) { host = new GameObject(KD_DogDefs.BallCatchName); KD_SceneObjects.PlaceIfNew(host, KD_DogDefs.PlacesPath); log.AppendLine("created " + KD_DogDefs.BallCatchName); }
            host.transform.localPosition = Vector3.zero; host.transform.localRotation = Quaternion.identity; host.transform.localScale = Vector3.one;
            host.layer = KD_DogDefs.BallCatchLayer;
            for (int i = 0; i < CatchSides.Length; i++)
            {
                string name = KD_DogDefs.BallCatchName + "_" + CatchSides[i];
                var t = host.transform.Find(name);
                var go = t != null ? t.gameObject : new GameObject(name);
                go.transform.SetParent(host.transform, false); go.layer = KD_DogDefs.BallCatchLayer;
                foreach (var r in go.GetComponents<Renderer>()) Object.DestroyImmediate(r);
                CatchSide(i, wall, out Vector3 center, out Vector3 size);
                go.transform.position = center; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                var box = go.GetComponent<BoxCollider>(); if (box == null) box = go.AddComponent<BoxCollider>();
                box.isTrigger = true; box.center = Vector3.zero; box.size = size;
                log.AppendLine("  " + name + " center=" + center.ToString("F2") + " size=" + size.ToString("F2"));
            }
            // 壁の向こうまでの往復に chaseTimeout が足りるよう底上げ (庭の対角 + 外側 3m ≒ 35m / runSpeed 2.4 ≒ 14.6s)
            int raised = 0;
            foreach (var fetch in KD_ProductsEditor.SceneComponents<KD_DogFetchController>())
            {
                if (fetch.chaseTimeout >= KD_DogFetchController.DefaultChaseTimeout) continue;
                var so = new SerializedObject(fetch);
                so.FindProperty("chaseTimeout").floatValue = KD_DogFetchController.DefaultChaseTimeout;
                so.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(fetch); raised++;
            }
            log.AppendLine("chaseTimeout raised to " + KD_DogFetchController.DefaultChaseTimeout + "s on " + raised + " dogs");
            EditorSceneManager.MarkSceneDirty(host.scene);
            log.AppendLine("BUILD complete (not saved)");
            Finish("Logs/imp-fet-015-build.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Fetch System/7 - Verify Ball Catch")]
        public static void VerifyBallCatch()
        {
            var v = new KD_VerifyLog("[KD_FetchSetup] Verify ball catch (IMP-FET-015)");
            v.Check(WallBounds(out var wall, out string why), "outer wall collider AABB found " + why);
            var host = KD_SceneObjects.Find(KD_DogDefs.BallCatchName, out int count);
            v.Check(host != null && count == 1, KD_DogDefs.BallCatchName + " exists once (" + count + ")");
            string offender = null;
            v.Check(host != null && KD_SceneObjects.HasStaticAncestry(host, out offender) && host.transform.parent != null && host.transform.parent.name == "KD_Places", "placed under a static KD_Places " + (offender ?? ""));
            v.Check(!Physics.GetIgnoreLayerCollision(KD_DogDefs.BallCatchLayer, 13), "catch layer collides with Pickup (13) so the ball trigger fires");
            v.Check(((1 << KD_DogDefs.BallCatchLayer) & Physics.DefaultRaycastLayers) == 0, "catch layer is outside Unity DefaultRaycastLayers (the dog ground ray uses an explicit mask + trigger ignore; VRChat pickup/UI rays over the wall are a device check)");
            var terrain = Object.FindObjectOfType<Terrain>();
            v.Check(terrain != null, "terrain exists for the outside ground");
            if (host != null)
            {
                v.Check(host.GetComponentsInChildren<Renderer>(true).Length == 0, "no renderer (invisible)");
                var boxes = host.GetComponentsInChildren<BoxCollider>(true);
                v.Check(boxes.Length == 4 && boxes.All(b => b.isTrigger && b.gameObject.layer == KD_DogDefs.BallCatchLayer && b.gameObject.activeInHierarchy), "four active trigger boxes on the catch layer (" + boxes.Length + ")");
                for (int i = 0; i < CatchSides.Length; i++)
                {
                    var t = host.transform.Find(KD_DogDefs.BallCatchName + "_" + CatchSides[i]);
                    var box = t == null ? null : t.GetComponent<BoxCollider>();
                    v.Check(box != null, CatchSides[i] + " box exists");
                    if (box == null) continue;
                    CatchSide(i, wall, out Vector3 center, out Vector3 size);
                    v.Check((box.bounds.center - center).magnitude < .01f && (box.bounds.size - size).magnitude < .01f, CatchSides[i] + " matches wall AABB + margin " + KD_DogDefs.BallCatchMargin + "m (actual center " + box.bounds.center.ToString("F2") + ")");
                    // 帯の内面が壁の当たりの外にあり、帯の外面がテレインの中にある
                    float inner = i == 0 ? box.bounds.min.z - wall.max.z : i == 1 ? wall.min.z - box.bounds.max.z : i == 2 ? box.bounds.min.x - wall.max.x : wall.min.x - box.bounds.max.x;
                    v.Check(inner >= KD_DogDefs.BallCatchMargin - .01f, CatchSides[i] + " inner face is " + inner.ToString("F2") + "m outside the wall");
                    if (terrain != null)
                    {
                        Vector3 tMin = terrain.transform.position, tMax = tMin + terrain.terrainData.size;
                        v.Check(box.bounds.min.x >= tMin.x && box.bounds.max.x <= tMax.x && box.bounds.min.z >= tMin.z && box.bounds.max.z <= tMax.z, CatchSides[i] + " lies within the terrain (ground exists where the ball drops)");
                        // 落ちた先の地面が壁の外の地面 (犬が歩ける高さ) であること
                        Vector3 probe = box.bounds.center; probe.y = 0f;
                        float h = terrain.SampleHeight(probe) + tMin.y;
                        v.Check(h > KD_DogDefs.BallCatchBottom && h < wall.min.y + 1f, CatchSides[i] + " ground height outside " + h.ToString("F2") + "m");
                    }
                    v.Check(box.bounds.max.y - box.bounds.min.y >= KD_DogDefs.BallCatchHeight - .01f && box.bounds.min.y <= KD_DogDefs.BallCatchBottom + .01f, CatchSides[i] + " covers from below ground to " + box.bounds.max.y.ToString("F1") + "m");
                }
            }
            // 犬側: 壁の向こうまでの往復に足りる chaseTimeout、外の地面を拾えるレイ (Default はテレインの層)
            foreach (var fetch in KD_ProductsEditor.SceneComponents<KD_DogFetchController>())
            {
                v.Check(fetch.chaseTimeout >= KD_DogFetchController.DefaultChaseTimeout - .01f, fetch.gameObject.name + " chaseTimeout >= " + KD_DogFetchController.DefaultChaseTimeout + " (" + fetch.chaseTimeout + ")");
                v.Check((fetch.groundLayerMask.value & (1 << (terrain == null ? 0 : terrain.gameObject.layer))) != 0, fetch.gameObject.name + " ground ray includes the terrain layer");
                v.Check(fetch.ballController != null && fetch.ballController.dropNoticeDelay > 0f, fetch.gameObject.name + " ball dropNoticeDelay set (" + (fetch.ballController == null ? -1f : fetch.ballController.dropNoticeDelay) + ")");
            }
            v.Finish("Logs/imp-fet-015-verify.txt");
            if (v.Fail > 0) throw new System.Exception("Ball catch verification failed: " + v.Fail);
        }
    }
}
