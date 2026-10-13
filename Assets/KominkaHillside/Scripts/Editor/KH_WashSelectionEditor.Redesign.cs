using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UdonSharpEditor;

namespace KominkaHillside.Editor
{
public static partial class KH_WashSelectionEditor
{
    const string RedesignAudit="Temp/wash-redesign-v05/";
    static KH_WashSelection[] RedesignSelectors()
    {
        Guard();
        var scene=EditorSceneManager.GetActiveScene();
        var result=UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true)
            .Where(s=>s.gameObject.scene==scene).OrderBy(s=>s.transform.parent.name).ToArray();
        if(result.Length!=4)throw new Exception("Expected four wash selectors");
        return result;
    }
    static string RedesignRef(UnityEngine.Object obj)
    {
        if(obj==null)return "NULL";
        if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj,out string guid,out long id))return guid+":"+id;
        return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
    }
    static void RedesignTree(StringBuilder log,string prefix,GameObject root)
    {
        if(root==null)throw new Exception("Missing "+prefix);
        foreach(var t in root.GetComponentsInChildren<Transform>(true).OrderBy(t=>PathOf(t)))
        {
            log.AppendLine(prefix+"|"+PathOf(t)+"|"+t.localPosition.ToString("F6")+"|"+t.localRotation.ToString("F6")+"|"+t.localScale.ToString("F6"));
            var mf=t.GetComponent<MeshFilter>();
            if(mf!=null){if(mf.sharedMesh==null)throw new Exception("Missing mesh "+PathOf(t));log.AppendLine("mesh="+RedesignRef(mf.sharedMesh));}
            var renderer=t.GetComponent<MeshRenderer>();
            if(renderer!=null)log.AppendLine("materials="+string.Join(",",renderer.sharedMaterials.Select(RedesignRef)));
        }
    }
    static string RedesignIdentity()
    {
        var log=new StringBuilder();
        foreach(int id in Order)RedesignTree(log,"prefab",AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs+Names[id]+".prefab"));
        foreach(var sel in RedesignSelectors())foreach(int id in Order)
        {
            RedesignTree(log,sel.transform.parent.name,sel.models[id]);
            if(sel.itemSlots[id]>=0)log.AppendLine("anchors="+RedesignRef(sel.nozzleAnchors[id])+"|"+RedesignRef(sel.gripAnchors[id])+"|"+RedesignRef(sel.gunHolds[id]));
        }
        return log.ToString();
    }
    [MenuItem(Menu+"50 - Snapshot Redesign References")]
    public static void SnapshotRedesign()
    {
        Guard();if(EditorSceneManager.GetActiveScene().isDirty)throw new Exception("Reconcile existing dirty scene before snapshot");
        Directory.CreateDirectory(RedesignAudit);
        string path=RedesignAudit+"identity-before.txt";
        if(File.Exists(path))throw new Exception("Baseline already exists; never overwrite it");
        File.WriteAllText(path,RedesignIdentity());
    }
    [MenuItem(Menu+"51 - Verify Redesign References")]
    public static void VerifyRedesignReferences()
    {
        string actual=RedesignIdentity();File.WriteAllText(RedesignAudit+"identity-after.txt",actual);
        if(actual!=File.ReadAllText(RedesignAudit+"identity-before.txt"))throw new Exception("Redesign changed hierarchy, anchor, mesh identity or material references; inspect snapshots");
        File.WriteAllText(RedesignAudit+"references.txt","PASS 9 prefabs / 36 scene models: hierarchy, local TRS, mesh GUID/fileID, material refs and tool anchor refs unchanged\n");
    }
    [MenuItem(Menu+"52 - Refresh Redesign Pickup Bounds")]
    public static void RefreshRedesignBounds()
    {
        var sels=RedesignSelectors();var scene=EditorSceneManager.GetActiveScene();
        if(scene.isDirty)throw new Exception("Reconcile existing dirty scene before bounds update");
        VerifyRedesignReferences();
        // Resolve and validate every target before writing. Other setup values remain owned by their existing menus.
        var centers=new Vector3[sels.Length][];var sizes=new Vector3[sels.Length][];
        for(int s=0;s<sels.Length;s++)
        {
            var sel=sels[s];centers[s]=(Vector3[])sel.colliderCenters.Clone();sizes[s]=(Vector3[])sel.colliderSizes.Clone();
            foreach(int id in Order.Where(i=>sel.itemSlots[i]>=0))
            {
                if(sel.models[id]==null||sel.slots[sel.itemSlots[id]]==null||sel.pickupColliders[sel.itemSlots[id]]==null)throw new Exception("Missing target "+Names[id]);
                var b=TightBounds(sel.models[id],sel.slots[sel.itemSlots[id]].transform);
                centers[s][id]=b.center;sizes[s][id]=Vector3.Max(b.size,new Vector3(.03f,.03f,.03f));
            }
        }
        for(int s=0;s<sels.Length;s++)
        {
            var sel=sels[s];Undo.RecordObject(sel,"Redesign pickup bounds");
            sel.colliderCenters=centers[s];sel.colliderSizes=sizes[s];Copy(sel);
            for(int category=0;category<3;category++)
            {
                int id=sel._Selected(category);if(id<0)continue;
                var box=sel.pickupColliders[sel.itemSlots[id]];Undo.RecordObject(box,"Redesign pickup bounds");
                box.center=centers[s][id];box.size=sizes[s][id];EditorUtility.SetDirty(box);
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        File.WriteAllText(RedesignAudit+"bounds-applied.txt","Updated 28 per-item bounds across 4 selectors and selected pickup colliders. Not saved; run Verify Redesign Bounds first.\n");
    }
    [MenuItem(Menu+"53 - Verify Redesign Bounds")]
    public static void VerifyRedesignBounds()
    {
        var log=new StringBuilder();int count=0;
        foreach(var sel in RedesignSelectors())
        {
            var backing=UdonSharpEditorUtility.GetBackingUdonBehaviour(sel);
            if(!backing.publicVariables.TryGetVariableValue<Vector3[]>("colliderCenters",out var centers)||
               !backing.publicVariables.TryGetVariableValue<Vector3[]>("colliderSizes",out var sizes)||
               centers.Length!=sel.colliderCenters.Length||sizes.Length!=sel.colliderSizes.Length)
                throw new Exception("Missing backing bounds "+sel.name);
            foreach(int id in Order.Where(i=>sel.itemSlots[i]>=0))
            {
                var b=TightBounds(sel.models[id],sel.slots[sel.itemSlots[id]].transform);var want=Vector3.Max(b.size,new Vector3(.03f,.03f,.03f));
                if((centers[id]-b.center).sqrMagnitude>1e-10f||(sizes[id]-want).sqrMagnitude>1e-10f||
                   (sel.colliderCenters[id]-b.center).sqrMagnitude>1e-10f||(sel.colliderSizes[id]-want).sqrMagnitude>1e-10f)
                    throw new Exception("Bounds mismatch "+sel.name+" "+Names[id]);
                float trayGap=sel.categoryHomes[sel.itemCategories[id]].localPosition.y+b.min.y-(TrayLocal.y+TrayScale.y*.5f);
                log.AppendLine(sel.transform.parent.name+" "+Names[id]+" center="+centers[id].ToString("F5")+" size="+sizes[id].ToString("F5")+" bottomToTray="+trayGap.ToString("F6"));count++;
            }
            for(int category=0;category<3;category++)
            {
                int id=sel._Selected(category);if(id<0)continue;var box=sel.pickupColliders[sel.itemSlots[id]];
                if((box.center-centers[id]).sqrMagnitude>1e-10f||(box.size-sizes[id]).sqrMagnitude>1e-10f)throw new Exception("Active pickup bounds mismatch");
            }
        }
        File.WriteAllText(RedesignAudit+"bounds-verified.txt","PASS "+count+" mesh bounds / proxy / Udon backing, selected pickup colliders\n"+log);
    }
}
}
