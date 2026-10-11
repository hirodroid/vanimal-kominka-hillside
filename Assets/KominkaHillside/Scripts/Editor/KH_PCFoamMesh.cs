using System;
using System.Collections.Generic;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // Editor-only deterministic mesh generation. Do not optimize/weld the collapsed anchor vertices.
    internal static class KH_PCFoamMesh
    {
        internal struct Lobe
        {
            internal int vertex, part;
            internal Vector3 position, normal;
            internal float radius, offset, seed;
            internal Vector2 uv;
            internal Vector4 supportA, supportB;
        }

        internal static Mesh Create(Mesh source, IList<Lobe> lobes)
        {
            Sphere(out var sphere, out var faces);
            int count = lobes.Count * sphere.Length;
            var positions = new Vector3[count]; var normals = new Vector3[count];
            var weights = new BoneWeight[count]; var sourceWeights = source.boneWeights;
            var uv = new List<Vector2>(count); var direction = new List<Vector4>(count);
            var supportA = new List<Vector4>(count); var supportB = new List<Vector4>(count);
            var config = new List<Vector4>(count); var triangles = new int[lobes.Count * faces.Length];
            for (int l = 0; l < lobes.Count; l++)
            {
                var v = lobes[l];
                for (int j = 0; j < sphere.Length; j++)
                {
                    int i = l * sphere.Length + j;
                    positions[i] = v.position; normals[i] = v.normal; weights[i] = sourceWeights[v.vertex];
                    uv.Add(v.uv); direction.Add(new Vector4(sphere[j].x,sphere[j].y,sphere[j].z,v.radius));
                    supportA.Add(v.supportA); supportB.Add(v.supportB);
                    config.Add(new Vector4(v.part,v.seed,v.offset,0));
                }
                for (int j = 0; j < faces.Length; j++) triangles[l*faces.Length+j] = l*sphere.Length+faces[j];
            }
            var mesh = new Mesh { name = "KH_PCFoam", vertices = positions, normals = normals,
                boneWeights = weights, bindposes = source.bindposes };
            mesh.SetUVs(0,uv); mesh.SetUVs(1,direction); mesh.SetUVs(2,supportA); mesh.SetUVs(3,supportB); mesh.SetUVs(4,config);
            mesh.triangles = triangles; mesh.bounds = source.bounds;
            return mesh;
        }

        internal static void Sphere(out Vector3[] vertices, out int[] triangles)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> { new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),
                new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),
                new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1) };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            int[] baseFaces = {0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var edges = new Dictionary<long,int>(); var f = new List<int>();
            int Mid(int a, int b)
            {
                long key = ((long)Math.Min(a,b)<<32) | (uint)Math.Max(a,b);
                if (edges.TryGetValue(key,out int found)) return found;
                int index = v.Count; v.Add((v[a]+v[b]).normalized); edges.Add(key,index); return index;
            }
            for (int i = 0; i < baseFaces.Length; i += 3)
            {
                int a = baseFaces[i], b = baseFaces[i+1], c = baseFaces[i+2];
                int ab = Mid(a,b), bc = Mid(b,c), ca = Mid(c,a);
                f.AddRange(new[] {a,ab,ca,b,bc,ab,c,ca,bc,ab,bc,ca});
            }
            vertices = v.ToArray(); triangles = f.ToArray();
        }
    }
}
