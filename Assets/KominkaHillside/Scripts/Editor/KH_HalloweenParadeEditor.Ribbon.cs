using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace KominkaHillside.Editor
{
    public static partial class KH_HalloweenParadeEditor
    {
        const string RibbonDir="Assets/KominkaHillside/Data/HalloweenParade/Ribbon/";
        [MenuItem(Menu+"11 - Refine Ribbon Fits")]
        public static void RefineRibbonFits(){SceneTransaction("Refine parade ribbon fits",()=>{
            Guard();var m=Only<KH_HalloweenParade>();var p=Only<KH_HalloweenParadePlayback>();
            float[] down={.20f,.17f,.27f,.18f,.27f,.32f};
            for(int i=0;i<6;i++){
                var visual=p.costumes[i].transform;var mount=visual.parent;var collar=m.leashes.collarAnchors[i];var dog=m.leashes.dogRoots[i];var pc=mount.GetComponent<ParentConstraint>();var neck=pc.GetSource(0).sourceTransform;
                Undo.RecordObject(mount,"Parade ribbon fit");Undo.RecordObject(pc,"Parade ribbon fit");Undo.RecordObject(visual,"Parade ribbon fit");
                float chestY=dog.InverseTransformPoint(collar.position-Vector3.up*down[i]).y;
                float chestZ=ChestFront(dog,chestY);
                pc.constraintActive=false;mount.SetPositionAndRotation(dog.TransformPoint(new Vector3(0,chestY,chestZ+.025f)),dog.rotation);
                pc.SetTranslationOffset(0,Quaternion.Inverse(neck.rotation)*(mount.position-neck.position));pc.SetRotationOffset(0,(Quaternion.Inverse(neck.rotation)*mount.rotation).eulerAngles);pc.locked=true;pc.constraintActive=true;
                visual.localRotation=Quaternion.Euler(0,180,0);
            }
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());Debug.Log("PARADE_RIBBON_FIT_OK six individual offsets; image verification required");
        });}
        static float ChestFront(Transform dog,float y)
        {
            float front=float.NegativeInfinity;
            foreach(var renderer in dog.GetComponentsInChildren<SkinnedMeshRenderer>()){
                var mesh=new Mesh();try{renderer.BakeMesh(mesh,true);var matrix=dog.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                    foreach(var vertex in mesh.vertices){var v=matrix.MultiplyPoint3x4(vertex);if(Mathf.Abs(v.x)<.045f&&Mathf.Abs(v.y-y)<.02f)front=Mathf.Max(front,v.z);}
                }finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
            if(float.IsNegativeInfinity(front))throw new Exception("No chest surface found for "+dog.name);
            return front;
        }
        [MenuItem(Menu+"6 - Attach Authored Ribbons")]
        public static void Ribbons()
        {SceneTransaction("Attach parade ribbons",RibbonsCore);}
        static void RibbonsCore()
        {
            Guard();var m=Only<KH_HalloweenParade>();var p=Only<KH_HalloweenParadePlayback>();
            var root=GameObject.Find(RootName).transform;
            if(root.Find("Ribbons")!=null)throw new Exception("Ribbons already fitted. Preserve individual adjustments.");
            var importer=(ModelImporter)AssetImporter.GetAtPath(RibbonDir+"KH_HalloweenRibbon.fbx");
            if(importer==null)throw new Exception("Authored Blender FBX missing");
            importer.importAnimation=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importNormals=ModelImporterNormals.Import;
            importer.isReadable=false;importer.bakeAxisConversion=true;importer.addCollider=false;importer.SaveAndReimport();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(RibbonDir+"KH_HalloweenRibbon.fbx");
            var material=AssetDatabase.LoadAssetAtPath<Material>(RibbonDir+"KH_HalloweenRibbon.mat");
            if(material==null){material=new Material(Shader.Find("VRChat/Mobile/Standard Lite"));AssetDatabase.CreateAsset(material,RibbonDir+"KH_HalloweenRibbon.mat");}
            material.color=Color.white;material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(RibbonDir+"KH_HalloweenRibbon_BaseColor.png");material.SetFloat("_Metallic",0);material.SetFloat("_Glossiness",.16f);EditorUtility.SetDirty(material);
            var group=Child(root,"Ribbons",Vector3.zero);p.costumes=new GameObject[6];
            // Individual starting fits, refined from six dog closeups. Mounts are outside bone trees,
            // so PhysBone traversal and existing collar interactions remain unaffected.
            float[] scale={.9f,.72f,1.05f,.75f,1f,1.12f};
            float[] down={.055f,.035f,.075f,.042f,.067f,.075f};
            float[] outwards={.045f,.080f,.042f,.045f,.055f,.095f};
            for(int i=0;i<6;i++)
            {
                var dog=m.leashes.dogRoots[i];var collar=m.leashes.collarAnchors[i];var neck=collar.parent.parent;
                var mount=Child(group,"RibbonMount_"+i,collar.position+dog.forward*outwards[i]-Vector3.up*down[i]);mount.rotation=dog.rotation;
                var pc=mount.gameObject.AddComponent<ParentConstraint>();pc.AddSource(new ConstraintSource{sourceTransform=neck,weight=1});pc.weight=1;
                pc.SetTranslationOffset(0,neck.InverseTransformPoint(mount.position));pc.SetRotationOffset(0,(Quaternion.Inverse(neck.rotation)*mount.rotation).eulerAngles);pc.locked=true;pc.constraintActive=true;
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(prefab,mount);visual.name="KH_HalloweenRibbon";visual.transform.localPosition=Vector3.zero;visual.transform.localRotation=Quaternion.identity;visual.transform.localScale=Vector3.one*scale[i];
                if(_recordUndo)Undo.RegisterCreatedObjectUndo(visual,"Parade ribbon");
                foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true)){renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;}
                p.costumes[i]=visual;visual.SetActive(false);
            }
            Copy(p);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("PARADE_RIBBONS_ATTACHED: six authored rigid accessories; fit requires visual review before final save");
        }
    }
}
