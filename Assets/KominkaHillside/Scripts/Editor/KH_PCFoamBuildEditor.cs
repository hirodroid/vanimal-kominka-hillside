using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;

namespace KominkaHillside.Editor
{
    // Runs before UdonSharp's order-0 proxy transfer/removal. Never edits authoring assets/materials.
    public sealed class KH_PCFoamBuildEditor : IProcessSceneWithReport
    {
        public int callbackOrder=>-1100;
        internal static string ProofRun;
        internal static int ProofProcessed;
        internal static BuildTarget ProofProcessedTarget=BuildTarget.NoTarget;
        internal static bool ProofMobileStripped;
        public void OnProcessScene(Scene scene,BuildReport report)
        {
            bool eligible=BuildPipeline.isBuildingPlayer&&!EditorApplication.isPlayingOrWillChangePlaymode&&scene.path==KH_PCFoamSetupEditor.ScenePath;
            Directory.CreateDirectory("Temp/PCFoamProof");
            File.AppendAllText("Temp/PCFoamProof/build.txt",DateTime.UtcNow.ToString("O")+" run="+(ProofRun??"normal")+" path="+scene.path+" reportNull="+(report==null)+" building="+BuildPipeline.isBuildingPlayer+" play="+EditorApplication.isPlayingOrWillChangePlaymode+" eligible="+eligible+"\n");
            if(!eligible)return;
            var target=report!=null?report.summary.platform:EditorUserBuildSettings.activeBuildTarget;
            int count=ProcessCopy(scene,target);
            if(ProofRun!=null)
            {
                ProofProcessed+=count;ProofProcessedTarget=target;
                ProofMobileStripped=target==BuildTarget.Android||target==BuildTarget.iOS;
            }
            File.AppendAllText("Temp/PCFoamProof/build.txt",DateTime.UtcNow.ToString("O")+" run="+(ProofRun??"normal")+" "+target+" processed="+count+"\n");
        }

        internal static int ProcessCopy(Scene scene,BuildTarget target)
        {
            bool mobile=target==BuildTarget.Android||target==BuildTarget.iOS;
            var washes=KH_PCFoamSetupEditor.Washes(scene);int count=0;var removedMaterials=new HashSet<Material>();
            foreach(var wash in washes)
            {
                var so=new SerializedObject(wash);
                var root=KH_PCFoamSetupEditor.Root(wash);
                if(root==null)
                {
                    var orphanBacking=UdonSharpEditorUtility.GetBackingUdonBehaviour(wash);
                    bool wired=KH_PCFoamSetupEditor.Fields.Take(4).Any(f=>so.FindProperty(f)?.objectReferenceValue!=null
                        ||(orphanBacking!=null&&orphanBacking.publicVariables.TryGetVariableValue(f,out object value)&&value is UnityEngine.Object obj&&obj!=null));
                    if(wired)throw new BuildFailedException("PC foam root moved or renamed: "+wash.name);
                    continue;
                }
                KH_PCFoamSetupEditor.ValidateWiring(wash);
                if(!mobile)
                {
                    if(target!=BuildTarget.StandaloneWindows64)throw new BuildFailedException("PC foam currently supports Windows D3D11 only");
                    var fallback=so.FindProperty("pcFoamFallbackShellMaterial")?.objectReferenceValue as Material;
                    var thin=so.FindProperty("pcFoamThinMaterial")?.objectReferenceValue as Material;
                    if(fallback==null||thin==null||so.FindProperty("pcFoamVersion").intValue!=KH_PCFoamGenerateEditor.Version)throw new BuildFailedException("Stale PC foam setup: "+wash.name);
                    if(so.FindProperty("pcFoamRenderer").objectReferenceValue==null||so.FindProperty("pcFoamRunoff").objectReferenceValue==null)throw new BuildFailedException("Incomplete PC foam setup: "+wash.name);
                    wash.shellRenderer.sharedMaterial=thin;UdonSharpEditorUtility.CopyProxyToUdon(wash);count++;continue;
                }
                foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))foreach(var mat in renderer.sharedMaterials)if(mat!=null)removedMaterials.Add(mat);
                var thinMaterial=AssetDatabase.LoadAssetAtPath<Material>(KH_PCFoamGenerateEditor.ThinPath(wash));if(thinMaterial!=null)removedMaterials.Add(thinMaterial);
                // Fields may already be compiled out when the Editor target is mobile; stale backing overrides still need removal.
                foreach(string field in KH_PCFoamSetupEditor.Fields)
                {
                    var property=so.FindProperty(field);if(property==null)continue;
                    if(property.propertyType==SerializedPropertyType.ObjectReference)property.objectReferenceValue=null;else property.intValue=0;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(wash);
                if(backing!=null)foreach(string field in KH_PCFoamSetupEditor.Fields)backing.publicVariables.RemoveVariable(field);
                UnityEngine.Object.DestroyImmediate(root.gameObject);count++;
            }
            if(mobile)
            {
                var roots=scene.GetRootGameObjects();var stillUsed=new HashSet<Material>(roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials));
                if(removedMaterials.Any(stillUsed.Contains))throw new BuildFailedException("PC-only foam material still used after mobile strip");
                foreach(var descriptor in roots.SelectMany(g=>g.GetComponentsInChildren<VRCSceneDescriptor>(true)))
                    if(descriptor.DynamicMaterials!=null)descriptor.DynamicMaterials=descriptor.DynamicMaterials.Where(m=>m==null||!removedMaterials.Contains(m)).ToList();
                // Independent dependency scan catches renamed/moved orphan renderers and references outside the six roots.
                foreach(var dependency in EditorUtility.CollectDependencies(roots))
                    if(dependency!=null&&(AssetDatabase.GetAssetPath(dependency).StartsWith(KH_PCFoamGenerateEditor.Folder+"/",StringComparison.Ordinal)
                        ||dependency is Material material&&material.shader!=null&&(material.shader.name=="KominkaHillside/PC Foam"||material.shader.name=="KominkaHillside/PC Foam Runoff"||material.shader.name=="KominkaHillside/Wash Shell PC Thin")))
                        throw new BuildFailedException("PC foam dependency remains in mobile scene: "+dependency.name);
            }
            return count;
        }
    }
}
