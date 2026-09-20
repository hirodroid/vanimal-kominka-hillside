// Play せずに Editor でカメラをレンダリングして Temp/KH_ViewShots/ に PNG を書く (Terrain 試作の景観確認)。
// 撮影条件 (位置 / 注視点 / FOV / 目線 1.6 m) は shots.log に残す。
// 注意: 新規カメラ・HDR 無効の Editor レンダーなので、実機 (VRChat) の描画と一致する保証はない。配置・見え方の一次判定用。
// 前例: OurUsualPark OP_ViewShotsEditor。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using C = KH_TerrainPrototypeConfig;

public static class KH_ViewShotsEditor
{
    const string OutDir = "Temp/KH_ViewShots";
    const int Width = 1920, Height = 1080;
    const float Eye = 1.6f, Fov = 60f;

    [MenuItem("Tools/KominkaHillside/Terrain Prototype/Render View Shots")]
    public static void Run()
    {
        var root = GameObject.Find("KH_World");
        var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        if (terrain == null) { Debug.LogError("KH_ViewShotsEditor: KH_World/terrain not found"); return; }
        Directory.CreateDirectory(OutDir);
        foreach (var f in Directory.GetFiles(OutDir, "*.png")) File.Delete(f);
        File.WriteAllText(OutDir + "/shots.log", "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + Width + "x" + Height + " fov=" + Fov + " eye=" + Eye + " hdr=off msaa=4\n");

        var path = KH_TerrainShape.SamplePath();
        var kom = root.transform.Find("KH_Places/KH_FarKominka");
        Vector3 komCenter = kom != null ? KH_TerrainPrototypeEditor.ActiveBounds(kom.gameObject).center : new Vector3(C.KominkaPosition.x, 3f, C.KominkaPosition.z);
        var vp = new Vector2(C.Flats[C.ViewpointFlat, 0], C.Flats[C.ViewpointFlat, 1]);
        var plaza = new Vector2(C.Flats[C.PlazaFlat, 0], C.Flats[C.PlazaFlat, 1]);

        // 00 俯瞰 (配置確認)
        Shot("00-overview", new Vector3(60f, 150f, 60f), new Vector3(60f, 10f, 95f), 60f);
        // 01 展望 → 古民家 (項目 1)
        Shot("01-viewpoint-to-kominka", EyeAt(terrain, vp), komCenter, Fov);
        Shot("01b-viewpoint-to-kominka-fov40", EyeAt(terrain, vp), komCenter, 40f);
        // 02 スポーン (門の前): 前 / 振り返って門
        var ent = root.transform.Find("KH_Places/KH_Entrance");
        if (ent != null)
        {
            var eye = ent.position + Vector3.up * (Eye - C.SpawnGroundOffset);
            Shot("02-spawn-forward", eye, eye + ent.forward * 10f + Vector3.down * 0.5f, Fov);
            Shot("02b-spawn-back-to-gate", eye, eye - ent.forward * 6f + Vector3.down * 0.3f, Fov);
        }
        // 10.. 道の制御点ごとに進行方向 (歩く目線)。展望・広場では谷側 / 山側も
        int n = C.Path.GetLength(0);
        for (int i = 0; i < n; i++)
        {
            var here = new Vector2(C.Path[i, 0], C.Path[i, 1]);
            // 終点では来た道へ振り返る。存在しない帰路へカメラを向けない。
            int nextIndex = i + 1 < n ? i + 1 : i - 1;
            var next = new Vector2(C.Path[nextIndex, 0], C.Path[nextIndex, 1]);
            var eye = EyeAt(terrain, here);
            var fwd = (next - here).normalized;
            Shot(string.Format("{0:00}-p{1:00}-forward", 10 + i, i), eye, eye + new Vector3(fwd.x, -0.15f, fwd.y) * 10f, Fov);
        }
        Shot("30-viewpoint-hill", EyeAt(terrain, vp), EyeAt(terrain, vp) + new Vector3(0f, 0.05f, 1f) * 10f, Fov);
        Shot("31-plaza-valley", EyeAt(terrain, plaza), EyeAt(terrain, plaza) + new Vector3(0f, -0.2f, -1f) * 10f, Fov);
        Shot("32-plaza-hill", EyeAt(terrain, plaza), EyeAt(terrain, plaza) + new Vector3(0f, 0.05f, 1f) * 10f, Fov);
        Shot("33-arrival-to-play-area", EyeAt(terrain, plaza + new Vector2(5f, -11f)), KH_TerrainPrototypeEditor.GroundXZ(terrain, plaza, 0.5f), Fov);
        Shot("34-hillside-composition", new Vector3(125f, 68f, 160f), new Vector3(62f, 10f, 95f), Fov);
        // 40 ダミーとボール: 広場中心を 3 m / 10 m から (南から北向き)、木陰のダミー
        var dummyFocus = KH_TerrainPrototypeEditor.GroundXZ(terrain, plaza, 0.3f);
        Shot("40-dummies-3m", EyeAt(terrain, plaza + new Vector2(0f, -3f)), dummyFocus, Fov);
        Shot("41-dummies-10m", EyeAt(terrain, plaza + new Vector2(0f, -10f)), dummyFocus, Fov);
        int last = C.DummyOffsets.GetLength(0) - 1;
        var shade = plaza + new Vector2(C.DummyOffsets[last, 0], C.DummyOffsets[last, 1]);
        Shot("42-dummy-shade-3m", EyeAt(terrain, shade + new Vector2(3f, -1f)), KH_TerrainPrototypeEditor.GroundXZ(terrain, shade, 0.3f), Fov);
        // 50 端の確認: 谷の南西 / 南東の隅、北の土手、西・東
        Shot("50-edge-southwest", EyeAt(terrain, new Vector2(25f, 30f)), new Vector3(5f, 8f, 8f), Fov);
        Shot("51-edge-southeast", EyeAt(terrain, new Vector2(95f, 30f)), new Vector3(115f, 8f, 8f), Fov);
        Shot("52-edge-north", EyeAt(terrain, new Vector2(60f, 160f)), new Vector3(60f, 30f, 185f), Fov);
        Shot("53-edge-west", EyeAt(terrain, new Vector2(20f, 120f)), new Vector3(-5f, 25f, 120f), Fov);
        Shot("54-edge-east", EyeAt(terrain, new Vector2(100f, 120f)), new Vector3(125f, 25f, 120f), Fov);
        Shot("55-valley-from-slope", EyeAt(terrain, new Vector2(24f, 70f)), new Vector3(60f, 3f, 30f), Fov);
        Debug.Log("KH_ViewShotsEditor done -> " + OutDir);
    }

    static Vector3 EyeAt(Terrain terrain, Vector2 xz) { return KH_TerrainPrototypeEditor.GroundXZ(terrain, xz, Eye); }

    // 見た目の検討用。地形や木の配置を変更せず、同じ地点で目線の高さだけを比較する。
    [MenuItem("Tools/KominkaHillside/Terrain Prototype/Render Eye Level Study")]
    public static void RenderEyeLevelStudy()
    {
        const string studyDir = "Temp/KH_EyeLevelStudy";
        var root = GameObject.Find("KH_World");
        var terrain = root != null ? root.GetComponentInChildren<Terrain>() : null;
        var kom = root != null ? root.transform.Find("KH_Places/KH_FarKominka") : null;
        if (terrain == null || kom == null) { Debug.LogError("KH eye study: terrain/kominka missing"); return; }
        Directory.CreateDirectory(studyDir);
        File.WriteAllText(studyDir + "/shots.log", "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] Eye-level study; standing=1.6m seated=1.1m low=0.4m (height comparison only, not canine vision); fov=60; HDR off; no Play; scene unchanged\n");
        Vector3 house = KH_TerrainPrototypeEditor.ActiveBounds(kom.gameObject).center;
        Vector2 vp = new Vector2(C.Flats[C.ViewpointFlat, 0], C.Flats[C.ViewpointFlat, 1]);
        Vector2 plaza = new Vector2(C.Flats[C.PlazaFlat, 0], C.Flats[C.PlazaFlat, 1]);
        StudyShot("01-lookout-standing", terrain, vp, 1.6f, house, studyDir);
        StudyShot("02-lookout-seated", terrain, vp, 1.1f, house, studyDir);
        StudyShot("03-lookout-low", terrain, vp, 0.4f, house, studyDir);
        StudyShot("04-plaza-standing-to-house", terrain, plaza, 1.6f, house, studyDir);
        StudyShot("05-plaza-low-to-house", terrain, plaza, 0.4f, house, studyDir);
        Vector2 arrival = plaza + new Vector2(5f, -11f);
        Vector3 playCenter = KH_TerrainPrototypeEditor.GroundXZ(terrain, plaza, 0.4f);
        StudyShot("06-play-area-standing", terrain, arrival, 1.6f, playCenter, studyDir);
        StudyShot("07-play-area-low", terrain, arrival, 0.4f, playCenter, studyDir);
        Vector2 bend = new Vector2(C.Path[3, 0], C.Path[3, 1]);
        Vector3 uphill = KH_TerrainPrototypeEditor.GroundXZ(terrain, new Vector2(C.Path[4, 0], C.Path[4, 1]), 1.0f);
        StudyShot("08-trail-standing", terrain, bend, 1.6f, uphill, studyDir);
        StudyShot("09-trail-low", terrain, bend, 0.4f, uphill, studyDir);
        Debug.Log("KH eye study -> " + studyDir);
    }

    static void StudyShot(string name, Terrain terrain, Vector2 xz, float eyeHeight, Vector3 lookAt, string outDir)
    {
        File.AppendAllText(outDir + "/shots.log", "eyeAboveTerrain=" + eyeHeight.ToString("0.00") + " m ");
        Shot(name, KH_TerrainPrototypeEditor.GroundXZ(terrain, xz, eyeHeight), lookAt, Fov, outDir);
    }

    public static void Shot(string name, Vector3 pos, Vector3 lookAt, float fov, string outDir = OutDir)
    {
        var prevActive = RenderTexture.active;
        GameObject go = null; RenderTexture rt = null; Texture2D tex = null;
        try
        {
            go = new GameObject("KH_TempShotCam");
            go.hideFlags = HideFlags.HideAndDontSave;
            var cam = go.AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.farClipPlane = 1200f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = false;
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(outDir + "/" + name + ".png", tex.EncodeToPNG());
            var e = cam.transform.eulerAngles;
            File.AppendAllText(outDir + "/shots.log", "  " + name + " pos=" + pos.ToString("0.00") + " lookAt=" + lookAt.ToString("0.00") + " yaw=" + e.y.ToString("0.0") + " pitch=" + (e.x > 180f ? e.x - 360f : e.x).ToString("0.0") + " fov=" + fov + "\n");
        }
        finally
        {
            RenderTexture.active = prevActive;
            if (go != null) { var cam = go.GetComponent<Camera>(); if (cam != null) cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(go); }
            if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
