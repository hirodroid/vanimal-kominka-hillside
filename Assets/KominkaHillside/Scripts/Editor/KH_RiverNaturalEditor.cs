using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KominkaHillside.Editor
{
    public static partial class KH_RiverRepresentativeEditor
    {
        // Deliberate revision of owned procedural assets. Preserves GUIDs and original world assets.
        [MenuItem("Tools/KominkaHillside/Course/12 - Naturalize River Representative")]
        public static void Naturalize()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||BuildPipeline.isBuildingPlayer||SceneManager.GetActiveScene().path!=ScenePath)
                throw new InvalidOperationException("Representative in idle Edit mode required");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene; nothing changed");
            var root=GameObject.Find("KH_RiverRepresentative");
            if(root==null)throw new InvalidOperationException("Missing river root");
            var td=AssetDatabase.LoadAssetAtPath<TerrainData>(Dir+"/KH_RiverTerrain.asset");
            var terrain=root.GetComponentInChildren<Terrain>();
            var bridge=root.transform.Find("SmallBridge");
            var volumes=root.transform.Find("RiverbedNotWalkable");
            var shader=Shader.Find("KominkaHillside/Scenery Lite");
            if(td==null||terrain==null||terrain.terrainData!=td||bridge==null||volumes==null||volumes.childCount!=60||shader==null||ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Owned assets / river navigation / scenery shader not ready");
            for(int c=0;c<6;c++)if(AssetDatabase.LoadAssetAtPath<Mesh>(Dir+"/KH_Water_"+c+".asset")==null)
                throw new InvalidOperationException("Missing water mesh");
            if(AssetDatabase.GetAssetPath(bridge.GetComponent<MeshFilter>().sharedMesh)!=Dir+"/KH_SmallBridge.asset")
                throw new InvalidOperationException("Bridge is not owned");

            var heights=new float[257,257];
            for(int z=0;z<257;z++)for(int x=0;x<257;x++)heights[z,x]=Height(x*Width/256,z*Length/256)/30;
            td.SetHeights(0,0,heights);PaintBanks(td);EditorUtility.SetDirty(td);
            for(int c=0;c<6;c++)SaveMesh(WaterMesh(c*30,30),"KH_Water_"+c);
            var waterMaterial=AssetDatabase.LoadAssetAtPath<Material>(Dir+"/KH_RiverWater.mat");
            waterMaterial.SetFloat("_RippleStrength",.065f);EditorUtility.SetDirty(waterMaterial);

            CreateWoodTexture();
            var wood=OwnedMaterial("KH_RiverWood",shader);
            wood.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Dir+"/KH_RiverWood.png"));
            wood.SetColor("_Color",Color.white);EditorUtility.SetDirty(wood);
            var mesh=bridge.GetComponent<MeshFilter>().sharedMesh;
            var verts=mesh.vertices;var normals=mesh.normals;var uv=new Vector2[verts.Length];var colors=new Color[verts.Length];
            for(int i=0;i<verts.Length;i++)
            {
                var p=verts[i];var n=normals[i];
                // Each source box is 24 vertices: keep board tint constant, grain along its long axis.
                int box=i/24;float tint=.85f+.17f*Mathf.PerlinNoise(box*1.77f,9.3f);
                colors[i]=new Color(tint,tint,tint,1);
                if(box<24) uv[i]=Mathf.Abs(n.y)>.5f?new Vector2(p.x*2.4f+box*.37f,p.z*.28f):new Vector2(p.x*2.4f+box*.37f,p.y*.28f);
                else uv[i]=new Vector2((Mathf.Abs(n.y)>.5f?p.z:p.y)*3+box*.17f,p.x*.22f);
            }
            mesh.uv=uv;mesh.colors=colors;EditorUtility.SetDirty(mesh);
            bridge.GetComponent<MeshRenderer>().sharedMaterial=wood;

            BuildStones(root.transform,shader);
            for(int i=0;i<60;i++)
            {
                float z=(i+.5f)*3;var v=volumes.GetChild(i).GetComponent<NavMeshModifierVolume>();
                v.transform.localPosition=new Vector3(Center(z),WaterHeight(z)-.55f,z);
                v.center=Vector3.zero;v.size=new Vector3((HalfWidth(z)+1.4f)*2,1.4f,3.5f);
                EditorUtility.SetDirty(v);
            }
            terrain.Flush();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);
            if(!EditorSceneManager.SaveScene(root.scene))throw new IOException("River save failed");
            Debug.Log("[KH River] Natural banks, shallows, wood and stones saved. Rebake navigation and verify separately.");
        }

        static Material OwnedMaterial(string name,Shader shader)
        {
            string path=Dir+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(shader){name=name,enableInstancing=true};AssetDatabase.CreateAsset(m,path);}
            return m;
        }
        static Mesh SaveMesh(Mesh fresh,string name)
        {
            string path=Dir+"/"+name+".asset";var owned=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            fresh.name=name;
            if(owned==null){AssetDatabase.CreateAsset(fresh,path);return fresh;}
            // Assign mesh buffers explicitly so Unity invalidates the existing GPU buffers too.
            owned.Clear();owned.vertices=fresh.vertices;owned.normals=fresh.normals;owned.uv=fresh.uv;owned.colors=fresh.colors;
            owned.triangles=fresh.triangles;owned.RecalculateBounds();EditorUtility.SetDirty(owned);UnityEngine.Object.DestroyImmediate(fresh);return owned;
        }
        static void CreateWoodTexture()
        {
            var t=new Texture2D(128,128,TextureFormat.RGB24,true);var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float u=x*Mathf.PI*2/128,v=y*Mathf.PI*2/128;
                float grain=.5f+.5f*Mathf.Sin(u*18+1.5f*Mathf.Sin(v)+.45f*Mathf.Sin(u*3+v*2));
                float fine=.5f+.5f*Mathf.Sin(u*43+Mathf.Sin(v*2));
                float tone=.68f+.22f*grain+.10f*fine;
                pixels[y*128+x]=new Color(.52f*tone,.40f*tone,.28f*tone,1);
            }
            t.SetPixels(pixels);t.Apply();string path=Dir+"/KH_RiverWood.png";
            File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);AssetDatabase.ImportAsset(path);
            var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.sRGBTexture=true;ti.mipmapEnabled=true;ti.isReadable=false;ti.maxTextureSize=128;ti.wrapMode=TextureWrapMode.Repeat;
            foreach(var p in new[]{"Android","iPhone"})ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name=p,overridden=true,maxTextureSize=128,format=TextureImporterFormat.ASTC_6x6});
            ti.SaveAndReimport();
        }
        static void BuildStones(Transform root,Shader shader)
        {
            var v=new List<Vector3>();var ix=new List<int>();var colors=new List<Color>();
            var random=new System.Random(270927);
            for(int cluster=0;cluster<16;cluster++)
            {
                float clusterZ=8+(float)random.NextDouble()*164;if(Mathf.Abs(clusterZ-BridgeZ)<12)clusterZ+=25;
                float side=cluster%2==0?-1:1;
                for(int stone=0;stone<3;stone++)
                {
                float z=clusterZ+(float)random.NextDouble()*3-1.5f;
                float x=Center(z)+side*(HalfWidth(z)+.5f+(float)random.NextDouble()*1.9f);
                float radius=.18f+(float)random.NextDouble()*.42f;
                var p=new Vector3(x,Height(x,z)-radius*.25f,z);
                int first=v.Count;float tint=.78f+(float)random.NextDouble()*.28f;
                // Three rings form a rounded, irregular pebble; 38 triangles, shared smooth normals.
                for(int ring=0;ring<3;ring++)for(int k=0;k<8;k++)
                {
                    float a=k*Mathf.PI/4;float ringRadius=radius*(ring==1?1:.53f)*( .86f+(float)random.NextDouble()*.25f);
                    v.Add(p+new Vector3(Mathf.Cos(a)*ringRadius,ring*radius*.42f,Mathf.Sin(a)*ringRadius*.8f));
                    colors.Add(new Color(.48f*tint,.46f*tint,.40f*tint,1));
                }
                for(int ring=0;ring<2;ring++)for(int k=0;k<8;k++)
                {
                    int a=first+ring*8+k,b=first+ring*8+(k+1)%8;
                    ix.AddRange(new[]{a,a+8,b,b,a+8,b+8});
                }
                for(int k=1;k<7;k++)ix.AddRange(new[]{first+16,first+16+k+1,first+16+k});
                }
            }
            var mesh=new Mesh{name="KH_RiverStones"};mesh.SetVertices(v);mesh.SetColors(colors);mesh.SetTriangles(ix,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh=SaveMesh(mesh,"KH_RiverStones");
            var found=root.Find("BankStones");var go=found==null?Child("BankStones",root):found.gameObject;
            var f=go.GetComponent<MeshFilter>();if(f==null)f=go.AddComponent<MeshFilter>();f.sharedMesh=mesh;
            var r=go.GetComponent<MeshRenderer>();if(r==null)r=go.AddComponent<MeshRenderer>();r.sharedMaterial=OwnedMaterial("KH_RiverStone",shader);r.shadowCastingMode=ShadowCastingMode.Off;
            var collider=go.GetComponent<MeshCollider>();if(collider==null)collider=go.AddComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
            var modifier=go.GetComponent<NavMeshModifier>();if(modifier==null)modifier=go.AddComponent<NavMeshModifier>();modifier.overrideArea=true;modifier.area=NavMesh.GetAreaFromName("Not Walkable");
            go.layer=LayerMask.NameToLayer("Environment");
        }
    }
}
