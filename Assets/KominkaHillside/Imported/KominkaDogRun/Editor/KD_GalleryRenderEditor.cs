using System.IO;
using UnityEditor;
using UnityEngine;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-CAM-005: 縁側ギャラリーの壁 (列ラベル + 板) を Editor カメラで 1 枚描く。
    /// 板は撮るまで非表示なので、撮影前の状態では列ラベルと Caption だけが写る —
    /// 「ラベルが列の中央に乗っているか」「2 列ラベルの字が伸びていないか」を目で見る用。
    /// VRChat の実機画面ではない。</summary>
    public static class KD_GalleryRenderEditor
    {
        const string Output = "Temp/gallery/";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo System/6 - Render Gallery Wall")]
        public static void Render()
        {
            Directory.CreateDirectory(Output);
            var gallery = KD_SceneObjects.Find(KD_DogDefs.PhotoSystemName).transform.Find("Gallery");
            var boards = gallery.GetComponent<Transform>().GetComponentsInChildren<Transform>(true);
            var shown = new System.Collections.Generic.List<GameObject>();
            // 板は非表示で保存されている。見た目の確認のため一時的に全部出し、必ず戻す
            foreach (var t in boards) if (t.name.StartsWith("Board") && !t.gameObject.activeSelf) { t.gameObject.SetActive(true); shown.Add(t.gameObject); }
            var go = new GameObject("KD_GalleryPreviewCamera"); var camera = go.AddComponent<Camera>();
            const int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24); var image = new Texture2D(W, H, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            try
            {
                camera.enabled = false; camera.fieldOfView = 45; camera.nearClipPlane = .05f; camera.farClipPlane = 50;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.10f, .09f, .08f);
                // 壁の中央 (板 4 段の真ん中あたり) を正面から。ギャラリーの -Z 側が表
                Vector3 centre = gallery.TransformPoint(new Vector3(0f, 0.9f, 0f));
                camera.transform.position = centre - gallery.forward * 2.2f;
                camera.transform.rotation = Quaternion.LookRotation(gallery.forward, gallery.up);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, W, H), 0, 0); image.Apply();
                File.WriteAllBytes(Output + "gallery-wall.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old; camera.targetTexture = null;
                Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
                foreach (var b in shown) b.SetActive(false);
            }
        }
    }
}
