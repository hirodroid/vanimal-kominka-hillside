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
        // 02 入口: 前 (進行方向) / 谷側 (後ろ = 古民家側) / 山側
        int n = C.Path.GetLength(0);
        string[] names = { "entrance", "curve1", "viewpoint", "curve2", "plaza", "curve3" };
        for (int i = 0; i < n; i++)
        {
            var here = new Vector2(C.Path[i, 0], C.Path[i, 1]);
            var next = new Vector2(C.Path[(i + 1) % n, 0], C.Path[(i + 1) % n, 1]);
            var eye = EyeAt(terrain, here);
            var fwd = (next - here).normalized;
            Shot(string.Format("{0:00}-{1}-forward", 10 + i, names[i]), eye, eye + new Vector3(fwd.x, -0.15f, fwd.y) * 10f, Fov);
            Shot(string.Format("{0:00}-{1}-valley", 10 + i, names[i]), eye, eye + new Vector3(0f, -0.2f, -1f) * 10f, Fov);
            Shot(string.Format("{0:00}-{1}-hill", 10 + i, names[i]), eye, eye + new Vector3(0f, 0.05f, 1f) * 10f, Fov);
        }
        // 20 ダミーとボール: 広場中心を 3 m / 10 m から (南から北向き)
        var dummyFocus = KH_TerrainPrototypeEditor.GroundXZ(terrain, plaza, 0.3f);
        Shot("20-dummies-3m", EyeAt(terrain, plaza + new Vector2(0f, -3f)), dummyFocus, Fov);
        Shot("21-dummies-10m", EyeAt(terrain, plaza + new Vector2(0f, -10f)), dummyFocus, Fov);
        // 22 木陰のダミー (最後のダミー)
        int last = C.DummyOffsets.GetLength(0) - 1;
        var shade = plaza + new Vector2(C.DummyOffsets[last, 0], C.DummyOffsets[last, 1]);
        Shot("22-dummy-shade-3m", EyeAt(terrain, shade + new Vector2(3f, -1f)), KH_TerrainPrototypeEditor.GroundXZ(terrain, shade, 0.3f), Fov);
        // 30 端の確認: 谷側の柵越し (東端 / 西端) と北の土手
        Shot("30-edge-southeast", EyeAt(terrain, new Vector2(100f, 80f)), new Vector3(118f, 5f, 60f), Fov);
        Shot("31-edge-southwest", EyeAt(terrain, new Vector2(20f, 80f)), new Vector3(2f, 5f, 60f), Fov);
        Shot("32-edge-north", EyeAt(terrain, new Vector2(60f, 160f)), new Vector3(60f, 30f, 185f), Fov);
        Shot("33-edge-west", EyeAt(terrain, new Vector2(20f, 120f)), new Vector3(-5f, 25f, 120f), Fov);
        Shot("34-edge-east", EyeAt(terrain, new Vector2(100f, 120f)), new Vector3(125f, 25f, 120f), Fov);
        Debug.Log("KH_ViewShotsEditor done -> " + OutDir);
    }

    static Vector3 EyeAt(Terrain terrain, Vector2 xz) { return KH_TerrainPrototypeEditor.GroundXZ(terrain, xz, Eye); }

    static void Shot(string name, Vector3 pos, Vector3 lookAt, float fov)
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
            File.WriteAllBytes(OutDir + "/" + name + ".png", tex.EncodeToPNG());
            var e = cam.transform.eulerAngles;
            File.AppendAllText(OutDir + "/shots.log", "  " + name + " pos=" + pos.ToString("0.00") + " lookAt=" + lookAt.ToString("0.00") + " yaw=" + e.y.ToString("0.0") + " pitch=" + (e.x > 180f ? e.x - 360f : e.x).ToString("0.0") + " fov=" + fov + "\n");
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
