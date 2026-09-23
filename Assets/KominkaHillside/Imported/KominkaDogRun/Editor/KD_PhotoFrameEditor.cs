using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    public static partial class KD_PhotoFrameEditor
    {
        internal const string Folder = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures/PhotoFrames";
        const string MeshPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/KD_PhotoFrameOverlay.asset";
        [Serializable] class FrameItem { public string key, nameKey, landscape, portrait; }
        [Serializable] class FrameCatalog { public FrameItem[] items; }
        static FrameItem[] Items()
        {
            var items=JsonUtility.FromJson<FrameCatalog>(File.ReadAllText(Folder+"/catalog.json")).items;
            if(items==null || items.Length==0 || items.Select(i=>i.key).Distinct().Count()!=items.Length ||
                items.Any(i=>string.IsNullOrEmpty(i.key) || i.key.Contains("|") || string.IsNullOrEmpty(i.nameKey) ||
                    string.IsNullOrEmpty(i.landscape) || string.IsNullOrEmpty(i.portrait) ||
                    Path.GetFileName(i.landscape)!=i.landscape || Path.GetFileName(i.portrait)!=i.portrait))
                throw new InvalidOperationException("Invalid photo frame catalog");
            return items;
        }
        static string[] Keys => Items().Select(i=>i.key).ToArray();
        // Read-only snapshot for comparing Setup runs without entering Play.
        public static string PreservationSnapshot()
        {
            var f=KD_ProductsEditor.SceneComponents<KD_PhotoFrame>().Single();
            var gallery=KD_SceneObjects.Find(KD_DogDefs.PhotoSystemName).GetComponent<KD_GalleryController>();
            var panel=f.toggleButton.GetComponentInParent<Canvas>().transform;
            return Newtonsoft.Json.JsonConvert.SerializeObject(new {
                panel=EditorJsonUtility.ToJson(panel), gallery=EditorJsonUtility.ToJson(gallery),
                camera=f.cameraOverlay.GetInstanceID(), viewer=f.viewerOverlay.GetInstanceID() });
        }
        static void Guard()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != "Assets/Scenes/KominkaDogRun.unity")
                throw new InvalidOperationException("Stopped KominkaDogRun scene required");
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo Frames/1 - Import Assets")]
        public static void ImportAssets()
        {
            Guard();
            foreach (var item in Items()) foreach (string filename in new[] { item.landscape, item.portrait })
            {
                string path = Folder + "/" + filename;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Generate frame textures first: " + path);
                importer.textureType = TextureImporterType.Default; importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true; importer.sRGBTexture = true; importer.mipmapEnabled = true;
                importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 4096; importer.textureCompression = TextureImporterCompression.CompressedHQ;
                foreach (string platform in new[]{"Android", "iPhone"})
                {
                    var settings = importer.GetPlatformTextureSettings(platform); settings.name = platform; settings.overridden = true;
                    settings.maxTextureSize = 4096; settings.format = TextureImporterFormat.ASTC_6x6; importer.SetPlatformTextureSettings(settings);
                }
                importer.SaveAndReimport();
            }
            KD_PhotoSetupEditor.CreateProgramAssets();
        }
        static Material MaterialAt(string name, string shaderName)
        {
            var shader = Shader.Find(shaderName); if (shader == null) throw new InvalidOperationException(shaderName);
            string path = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            if (mat.shader != shader) throw new InvalidOperationException("Unexpected shader on " + path);
            return mat;
        }
        static Mesh OverlayMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "KD_PhotoFrameOverlay", vertices = new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)},
                uv = new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up}, triangles = new[]{0,2,1,0,3,2} };
            // Shader is fullscreen; CPU frustum culling must not depend on the gallery's location.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            AssetDatabase.CreateAsset(mesh, MeshPath); return mesh;
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo Frames/2 - Setup")]
        public static void Setup()
        {
            Guard();
            var sys = KD_SceneObjects.Find(KD_DogDefs.PhotoSystemName);
            var gallery = sys == null ? null : sys.transform.Find("Gallery");
            var caption = gallery == null ? null : gallery.Find("Caption");
            var viewer = KD_ProductsEditor.SceneComponents<KD_PhotoViewer>().Single();
            var pouch = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName)?.GetComponent<KD_TreatPouch>();
            if (caption == null || viewer.photoRenderer == null || pouch == null || pouch.language == null)
                throw new InvalidOperationException("Photo viewer, gallery caption and localization required");
            if (PrefabUtility.IsPartOfPrefabInstance(sys) || PrefabUtility.IsPartOfPrefabInstance(viewer)) throw new InvalidOperationException("Owned scene objects only");
            var items=Items();
            var land = items.Select(i => AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + i.landscape)).ToArray();
            var portrait = items.Select(i => AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + i.portrait)).ToArray();
            if (land.Any(t=>t==null) || portrait.Any(t=>t==null)) throw new InvalidOperationException("Six frame textures required");
            var existing = KD_SceneObjects.Find("KD_PhotoFrame", out int count);
            if (count > 1 || existing != null && existing.GetComponent<KD_PhotoFrame>() == null) throw new InvalidOperationException("Unexpected frame object");
            var log = new StringBuilder(); var catalog = KD_LocalizationEditor.Load(); var font = pouch.sectionTitleLabel.font;
            var missing = KD_ShopSetupEditor.MissingGlyphs(KD_LocalizationEditor.BundledCharacters(catalog));
            if (missing.Length > 0) { KD_ShopSetupEditor.AddGlyphs(missing, log); if (KD_ShopSetupEditor.MissingGlyphs(missing).Length > 0) throw new InvalidOperationException("Missing frame label glyphs"); }
            if (existing == null) { existing = new GameObject("KD_PhotoFrame"); existing.transform.SetParent(sys.transform, false); existing.AddUdonSharpComponent<KD_PhotoFrame>(); }
            var frame = existing.GetComponent<KD_PhotoFrame>();
            frame.frameKeys = items.Select(i=>i.key).ToArray(); frame.nameKeys = items.Select(i=>i.nameKey).ToArray();
            frame.landscapeTextures = land; frame.portraitTextures = portrait; frame.language = pouch.language;
            var cameraMat = MaterialAt("M_KD_PhotoFrame", "KominkaDogRun/KD_PhotoFrame");
            var viewerMat = MaterialAt("M_KD_PhotoFrameViewer", "KominkaDogRun/KD_PhotoFrameViewer");
            cameraMat.SetTexture("_LandscapeTex",land[0]);cameraMat.SetTexture("_PortraitTex",portrait[0]);viewerMat.mainTexture=land[0];
            var quad = existing.transform.Find("CameraOverlay");
            if (quad == null) { var go = new GameObject("CameraOverlay",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(existing.transform,false);quad=go.transform; }
            // UI is excluded by the handheld camera unless its UI option is enabled.
            // TransparentFX is visible there, while our world photo camera excludes it.
            quad.gameObject.layer=1;quad.GetComponent<MeshFilter>().sharedMesh=OverlayMesh();
            var renderer=quad.GetComponent<MeshRenderer>();renderer.sharedMaterial=cameraMat;renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;renderer.allowOcclusionWhenDynamic=false;renderer.enabled=false;frame.cameraOverlay=renderer;
            var photo=viewer.photoRenderer.transform;var overlay=photo.Find("FrameOverlay");
            if(overlay==null){var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="FrameOverlay";Object.DestroyImmediate(go.GetComponent<Collider>());go.transform.SetParent(photo,false);overlay=go.transform;}
            overlay.localPosition=new Vector3(0,0,-.002f);overlay.localRotation=Quaternion.identity;overlay.localScale=Vector3.one;overlay.gameObject.layer=5;
            var vr=overlay.GetComponent<MeshRenderer>();vr.sharedMaterial=viewerMat;vr.enabled=false;vr.shadowCastingMode=ShadowCastingMode.Off;vr.receiveShadows=false;frame.viewerOverlay=vr;
            var panel=gallery.Find("FrameCanvas");
            if(panel==null)
            {
                var go=new GameObject("FrameCanvas",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster),typeof(VRC.SDK3.Components.VRCUiShape),typeof(BoxCollider));
                go.transform.SetParent(gallery,false);panel=go.transform;
                panel.localPosition=new Vector3(caption.localPosition.x-caption.localScale.x*.5f-.08f-.64f,caption.localPosition.y,-.025f);
                panel.localRotation=Quaternion.identity;panel.localScale=Vector3.one*.001f;
            }
            panel.gameObject.layer=0;var rect=(RectTransform)panel;rect.sizeDelta=new Vector2(1280,360);panel.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            var box=panel.GetComponent<BoxCollider>();box.isTrigger=true;box.center=Vector3.zero;box.size=new Vector3(1280,360,1);
            var bg=panel.GetComponent<Image>()??panel.gameObject.AddComponent<Image>();bg.color=KD_UiSpriteEditor.SurfacePanel;bg.raycastTarget=false;KD_UiSpriteEditor.ApplyFlatSkin(bg);
            Label(panel,"Title",new Vector2(0,136),new Vector2(1232,48),"photo.frame.title",font,32,catalog).alignment=TextAlignmentOptions.Left;
            var backdrop=KD_ShopSetupEditor.EnsureUiChild(panel,"Preview",new Vector2(-432,-20),new Vector2(336,189));
            var plate=backdrop.GetComponent<Image>()??backdrop.AddComponent<Image>();plate.color=new Color(.65f,.59f,.49f,1);plate.raycastTarget=false;
            var previewGo=KD_ShopSetupEditor.EnsureUiChild(backdrop.transform,"Frame",Vector2.zero,new Vector2(336,189));
            frame.preview=previewGo.GetComponent<RawImage>()??previewGo.AddComponent<RawImage>();frame.preview.texture=land[0];frame.preview.raycastTarget=false;
            frame.selectionLabel=Label(panel,"Selection",new Vector2(218,-32),new Vector2(420,64),"photo.frame.paw",font,30,catalog);
            frame.statusLabel=Label(panel,"Status",new Vector2(205,-126),new Vector2(730,60),"photo.frame.loading",font,24,catalog);
            var target=UdonSharpEditorUtility.GetBackingUdonBehaviour(frame);target.SyncMethod=VRC.SDKBase.Networking.SyncType.None;
            frame.toggleButton=KD_ShopSetupEditor.EnsureButton(panel,"Toggle",new Vector2(218,64),new Vector2(420,60),KD_UiSpriteEditor.ActionNeutral,font,"",target,"_Toggle",log).GetComponent<Button>();
            frame.toggleLabel=frame.toggleButton.GetComponentInChildren<TMP_Text>();frame.toggleLabel.fontSize=28;
            frame.toggleLabel.text=KD_LocalizationEditor.Text(catalog,catalog.defaultLanguage,"photo.frame.off");
            SetupToggleVisual(frame);
            frame.previousButton=KD_ShopSetupEditor.EnsureButton(panel,"Previous",new Vector2(-54,-32),new Vector2(96,60),KD_UiSpriteEditor.ActionNeutral,font,"",target,"_Previous",log).GetComponent<Button>();
            frame.nextButton=KD_ShopSetupEditor.EnsureButton(panel,"Next",new Vector2(490,-32),new Vector2(96,60),KD_UiSpriteEditor.ActionNeutral,font,"",target,"_Next",log).GetComponent<Button>();
            KD_LocalizationEditor.Configure(pouch.language,catalog,log);KD_LocalizationEditor.Bind(pouch.language,catalog,log);
            KD_ProductsEditor.Copy(pouch.language);KD_ProductsEditor.Copy(frame);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(sys.scene);
            KD_VerifyLog.Write("Logs/imp-cam-007-setup.txt",log);
        }
        static TMP_Text Label(Transform parent,string name,Vector2 pos,Vector2 size,string key,TMP_FontAsset font,int points,KD_LocalizationEditor.Catalog catalog)
        {
            var go=KD_ShopSetupEditor.EnsureUiChild(parent,name,pos,size);var label=go.GetComponent<TextMeshProUGUI>()??go.AddComponent<TextMeshProUGUI>();
            label.font=font;label.fontSize=points;label.color=Color.white;label.alignment=TextAlignmentOptions.Center;label.enableWordWrapping=false;label.raycastTarget=false;
            label.text=KD_LocalizationEditor.Text(catalog,catalog.defaultLanguage,key);return label;
        }
        internal static void SetupToggleVisual(KD_PhotoFrame frame)
        {
            var labelRect=frame.toggleLabel.rectTransform;
            labelRect.anchorMin=labelRect.anchorMax=new Vector2(.5f,.5f);
            labelRect.pivot=new Vector2(.5f,.5f);
            labelRect.anchoredPosition=new Vector2(-60,0);labelRect.sizeDelta=new Vector2(240,56);
            var track=KD_ShopSetupEditor.EnsureUiChild(frame.toggleButton.transform,"SwitchTrack",new Vector2(120,0),new Vector2(112,48));
            var trackImage=track.GetComponent<Image>()??track.AddComponent<Image>();
            KD_UiSpriteEditor.ApplyRoundSkin(trackImage,KD_UiSpriteEditor.LoadRound());
            trackImage.color=new Color(.16f,.14f,.12f);trackImage.raycastTarget=false;
            var thumb=KD_ShopSetupEditor.EnsureUiChild(track.transform,"Thumb",new Vector2(-32,0),new Vector2(36,36));
            var thumbImage=thumb.GetComponent<Image>()??thumb.AddComponent<Image>();
            KD_UiSpriteEditor.ApplyCircleSkin(thumbImage,KD_UiSpriteEditor.LoadCircle());
            thumbImage.color=Color.white;thumbImage.raycastTarget=false;
            frame.toggleThumb=(RectTransform)thumb.transform;
            frame.toggleButton.targetGraphic.color=new Color(.30f,.28f,.24f);
        }
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo Frames/3 - Verify")]
        public static void Verify()
        {
            Guard();var v=new KD_VerifyLog("Photo frame overlay");var f=KD_ProductsEditor.SceneComponents<KD_PhotoFrame>().Single();
            v.Check(f.frameKeys.SequenceEqual(Keys),"stable keys match catalog");
            v.Check(f.landscapeTextures.Length==Keys.Length && f.portraitTextures.Length==Keys.Length,"catalog horizontal/vertical pairs");
            for(int i=0;i<Keys.Length;i++)
            {
                VerifyTexture(v,f.landscapeTextures[i],2560,1440,Keys[i]+" landscape");
                VerifyTexture(v,f.portraitTextures[i],1440,2560,Keys[i]+" portrait");
            }
            v.Check(!f.cameraOverlay.enabled && !f.viewerOverlay.enabled,"saved scene OFF");
            v.Check(f.cameraOverlay.gameObject.layer==1 && f.viewerOverlay.gameObject.layer==5,"camera TransparentFX / viewer UI; both excluded from world photo camera");
            var viewer=KD_ProductsEditor.SceneComponents<KD_PhotoViewer>().Single();
            v.Check(f.viewerOverlay.transform.parent==viewer.photoRenderer.transform,"viewer frame exactly follows photograph");
            v.Check(f.viewerOverlay.transform.localScale==Vector3.one,"viewer frame covers photo, not black margins");
            v.Check(f.cameraOverlay.GetComponent<MeshFilter>().sharedMesh.bounds.size.x>=100000 && !f.cameraOverlay.allowOcclusionWhenDynamic,"fullscreen CPU bounds and occlusion guard");
            var panel=f.toggleButton.GetComponentInParent<Canvas>();
            v.Check(panel.gameObject.layer==0 && panel.GetComponent<VRC.SDK3.Components.VRCUiShape>()!=null && panel.GetComponent<BoxCollider>()!=null,"VR Desktop Touch canvas raycast");
            v.Check(panel.name=="FrameCanvas" && ((RectTransform)panel.transform).sizeDelta==new Vector2(1280,360),"gallery frame panel size");
            v.Check(f.toggleThumb!=null && f.toggleThumb.IsChildOf(f.toggleButton.transform) && f.toggleThumb.anchoredPosition==new Vector2(-32,0),"switch thumb is wired and saved on OFF left side");
            v.Check(f.toggleThumb!=null && !f.toggleThumb.GetComponent<Image>().raycastTarget && !f.toggleThumb.parent.GetComponent<Image>().raycastTarget,"switch graphics preserve button hit target");
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(f);
            backing.publicVariables.TryGetVariableValue(nameof(f.cameraOverlay),out object camera);
            backing.publicVariables.TryGetVariableValue(nameof(f.landscapeTextures),out object textures);
            v.Check(camera as Renderer==f.cameraOverlay && textures is Texture2D[] a && a.SequenceEqual(f.landscapeTextures),"runtime backing renderer and texture references");
            v.Check(backing.SyncMethod==VRC.SDKBase.Networking.SyncType.None,"local state, no synchronized settings");
            v.Finish("Logs/imp-cam-007-verify.txt");
        }
        static void VerifyTexture(KD_VerifyLog v,Texture2D texture,int sourceWidth,int sourceHeight,string label)
        {
            var importer=texture==null?null:AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if(importer==null){v.Check(false,label+" texture/importer missing");return;}
            importer.GetSourceTextureWidthAndHeight(out int width,out int height);
            v.Check(width==sourceWidth&&height==sourceHeight,label+" source "+width+"x"+height);
            // Source artwork and the platform import budget are separate. Never restore the old 4K cap here.
            string platform=EditorUserBuildSettings.activeBuildTarget==BuildTarget.Android?"Android":EditorUserBuildSettings.activeBuildTarget==BuildTarget.iOS?"iPhone":"Standalone";
            var settings=importer.GetPlatformTextureSettings(platform);
            int cap=settings.overridden?settings.maxTextureSize:importer.maxTextureSize;
            float scale=Mathf.Min(1f,(float)cap/Mathf.Max(width,height));
            v.Check(texture.width==Mathf.RoundToInt(width*scale)&&texture.height==Mathf.RoundToInt(height*scale),label+" imported "+texture.width+"x"+texture.height+" cap="+cap);
        }
    }
}
