using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // Fitting values are in metres in each dog's root coordinates. Keep the source rig untouched.
    public static class KH_LeashModelsEditor
    {
        static readonly Vector3[] Centres={new Vector3(0,.205f,.16f),new Vector3(0,.13f,.045f),
            new Vector3(0,.44f,.23f),new Vector3(0,.18f,.14f),new Vector3(0,.29f,.17f),new Vector3(0,.44f,.24f)};
        static readonly Vector2[] Radii={new Vector2(.095f,.09f),new Vector2(.09f,.08f),
            new Vector2(.115f,.125f),new Vector2(.07f,.07f),new Vector2(.095f,.105f),new Vector2(.16f,.16f)};
        static readonly float[] Tilt={-35,-15,-25,-30,-30,-20};
        static readonly float[] RingRise={.025f,.05f,.035f,.025f,.06f,.075f};

        // Rounded rectangular belt cross-section, hollow through the middle.
        static Mesh Ring(string path,float rx,float ry,float thickness,float width,int sides=24)
        {
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            const int profile=8;
            var vertices=new Vector3[sides*profile];var uv=new Vector2[vertices.Length];var tris=new int[sides*profile*6];
            float bevel=thickness*.22f;
            var section=new Vector2[]{new Vector2(thickness/2,-width/2+bevel),new Vector2(thickness/2,width/2-bevel),
                new Vector2(thickness/2-bevel,width/2),new Vector2(-thickness/2+bevel,width/2),
                new Vector2(-thickness/2,width/2-bevel),new Vector2(-thickness/2,-width/2+bevel),
                new Vector2(-thickness/2+bevel,-width/2),new Vector2(thickness/2-bevel,-width/2)};
            int n=0;
            for(int i=0;i<sides;i++)for(int j=0;j<profile;j++)
            {
                float a=i*Mathf.PI*2/sides;var s=section[j];int k=i*profile+j;
                vertices[k]=new Vector3((rx+s.x)*Mathf.Cos(a),(ry+s.x)*Mathf.Sin(a),s.y);
                uv[k]=new Vector2((float)i/sides,(float)j/profile);
                int b=((i+1)%sides)*profile+j,c=((i+1)%sides)*profile+(j+1)%profile,d=i*profile+(j+1)%profile;
                tris[n++]=k;tris[n++]=b;tris[n++]=c;tris[n++]=k;tris[n++]=c;tris[n++]=d;
            }
            var mesh=new Mesh{name=System.IO.Path.GetFileNameWithoutExtension(path)};
            mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=tris;mesh.RecalculateNormals();mesh.RecalculateBounds();
            if(existing!=null){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static Transform MeshChild(Transform parent,string name,Mesh mesh,Material material)
        {
            var t=parent.Find(name);if(t==null){t=new GameObject(name).transform;t.SetParent(parent,false);}
            t.localPosition=Vector3.zero;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;
            var filter=t.GetComponent<MeshFilter>();if(filter==null)filter=t.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
            var renderer=t.GetComponent<MeshRenderer>();if(renderer==null)renderer=t.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            t.gameObject.layer=14;return t;
        }
        public static void FitCollar(Transform collar,Transform anchor,Transform dog,int breed,string data,Material band,Material metal)
        {
            var current=collar.Find("Band");
            if(current!=null&&current.GetComponent<MeshFilter>().sharedMesh.name.EndsWith("_Blender"))
            {KH_LeashProductionEditor.Fit(collar,anchor,dog,breed);return;}
            collar.position=dog.TransformPoint(Centres[breed]);collar.rotation=dog.rotation*Quaternion.Euler(Tilt[breed],0,0);
            var scale=collar.parent.lossyScale;collar.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
            var radii=Radii[breed];
            MeshChild(collar,"Band",Ring(data+"/KH_Band_"+breed+".asset",radii.x,radii.y,.012f,.032f),band);
            // Dorsal loop marks the exact contact point above the fur, facing the incoming hand.
            anchor.localPosition=new Vector3(0,radii.y+RingRise[breed],0);anchor.localRotation=Quaternion.identity;
            MeshChild(anchor,"AttachmentRing",Ring(data+"/KH_AttachmentRing.asset",.018f,.022f,.006f,.006f,8),metal);
            // Short attachment tab clears dense fur without enlarging the whole collar.
            var mount=collar.Find("RingMount");
            if(mount==null){Cube(collar,"RingMount",Vector3.zero,Vector3.one,metal);mount=collar.Find("RingMount");}
            float tab=Mathf.Max(.006f,RingRise[breed]-.018f);
            mount.localPosition=new Vector3(0,radii.y+tab/2,0);mount.localScale=new Vector3(.01f,tab,.008f);
        }

        public static Transform MakeTip(Transform tip,string data,Material metal)
        {
            var filter=tip.GetComponent<MeshFilter>();
            if(filter.sharedMesh.name=="Capsule")
            {
                tip.localScale=Vector3.one;
                filter.sharedMesh=Ring(data+"/KH_LeashTip.asset",.025f,.045f,.009f,.012f);
                var hit=tip.GetComponent<CapsuleCollider>();hit.radius=.045f;hit.height=.14f;
                tip.GetComponent<Renderer>().sharedMaterial=metal;
            }
            var contact=tip.Find("Contact");
            if(contact==null){contact=new GameObject("Contact").transform;contact.SetParent(tip,false);contact.localPosition=new Vector3(0,.045f,0);}
            return contact;
        }

        public static void MakeRack(Transform rack,Material wood)
        {
            if(rack.Find("Crossbar")!=null)return;
            Cube(rack,"Crossbar",new Vector3(0,.095f,.055f),new Vector3(1.2f,.06f,.07f),wood);
            Cube(rack,"LeftPost",new Vector3(-.56f,-.37f,.055f),new Vector3(.06f,.92f,.06f),wood);
            Cube(rack,"RightPost",new Vector3(.56f,-.37f,.055f),new Vector3(.06f,.92f,.06f),wood);
        }
        static void Cube(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.layer=14;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }
    }
}
