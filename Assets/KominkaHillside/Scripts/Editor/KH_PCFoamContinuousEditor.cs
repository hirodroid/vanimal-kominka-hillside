using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KominkaHillside.Editor
{
    // PC-only authoring and diagnostics for the continuous lather surface (IMP-DOG-052 v3).
    public static class KH_PCFoamContinuousEditor
    {
        internal sealed class Surface
        {
            internal Mesh mesh;
            internal int[] anchors, used, parentA, parentB;
            internal float[] heights;
            internal float maxHeight, maxSeamGap;
        }

        // The asset is a connected copy of the body, never a collection of bubble primitives.
        internal static Surface Create(KH_DogWash wash, Vector3[] positions, Vector3[] normals, bool[] excluded)
        {
            var source = KH_PCFoamAuditEditor.Body(wash).sharedMesh;
            var weights = source.boneWeights; var uv = source.uv; var faces = source.triangles;
            // One split gives the broad height field intermediate samples. Re-evaluate distances/normals below;
            // simply subdividing an already displaced straight edge would not soften its silhouette.
            var pv=positions.ToList();var nv=normals.ToList();var tv=uv.ToList();var bv=weights.ToList();var ev=excluded.ToList();
            var parentA=Enumerable.Range(0,source.vertexCount).ToList();var parentB=Enumerable.Range(0,source.vertexCount).ToList();
            var mids=new Dictionary<long,int>();var subdivided=new List<int>();
            string WeightKey(BoneWeight w)
            {
                var bone=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};var value=new[]{w.weight0,w.weight1,w.weight2,w.weight3};
                return string.Join("/",Enumerable.Range(0,4).Where(i=>value[i]>0).OrderBy(i=>bone[i]).Select(i=>bone[i]+":"+value[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            }
            int Mid(int a,int b)
            {
                long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);if(mids.TryGetValue(key,out int result))return result;
                // Equal effective weights make skin(mid) exactly the interpolated skinned edge.
                // This comparison also yields the same split decision on duplicate UV-seam edges.
                if(WeightKey(bv[a])!=WeightKey(bv[b]))return -1;
                result=pv.Count;mids.Add(key,result);pv.Add((pv[a]+pv[b])*.5f);nv.Add((nv[a]+nv[b]).normalized);tv.Add((tv[a]+tv[b])*.5f);bv.Add(bv[a]);ev.Add(false);parentA.Add(a);parentB.Add(b);return result;
            }
            for(int f=0;f<faces.Length;f+=3)
            {
                int a=faces[f],b=faces[f+1],c=faces[f+2];if(excluded[a]||excluded[b]||excluded[c])continue;
                int ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);
                if(ab>=0&&bc>=0&&ca>=0)subdivided.AddRange(new[]{a,ab,ca,b,bc,ab,c,ca,bc,ab,bc,ca});
                else if(ab>=0&&bc>=0)subdivided.AddRange(new[]{b,bc,ab,a,ab,c,ab,bc,c});
                else if(bc>=0&&ca>=0)subdivided.AddRange(new[]{c,ca,bc,b,bc,a,bc,ca,a});
                else if(ca>=0&&ab>=0)subdivided.AddRange(new[]{a,ab,ca,c,ca,b,ca,ab,b});
                else if(ab>=0)subdivided.AddRange(new[]{a,ab,c,ab,b,c});
                else if(bc>=0)subdivided.AddRange(new[]{b,bc,a,bc,c,a});
                else if(ca>=0)subdivided.AddRange(new[]{c,ca,b,ca,a,b});
                else subdivided.AddRange(new[]{a,b,c});
            }
            positions=pv.ToArray();normals=nv.ToArray();uv=tv.ToArray();weights=bv.ToArray();excluded=ev.ToArray();faces=subdivided.ToArray();
            int count = positions.Length;
            var graph = Enumerable.Range(0, count).Select(_ => new HashSet<int>()).ToArray();
            var kept = new List<int>(); var used = new bool[count];
            for (int f = 0; f < faces.Length; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                if (excluded[a] || excluded[b] || excluded[c]) continue;
                kept.AddRange(new[] {a, b, c}); used[a] = used[b] = used[c] = true;
                graph[a].Add(b); graph[a].Add(c); graph[b].Add(a); graph[b].Add(c); graph[c].Add(a); graph[c].Add(b);
            }
            var groups = new Dictionary<string, List<int>>();
            string Key(int i)
            {
                var p = positions[i]; var w = weights[i];
                return p.x.ToString("R") + ":" + p.y.ToString("R") + ":" + p.z.ToString("R") + ":" + WeightKey(w);
            }
            for (int i = 0; i < count; i++)
            {
                if (!used[i]) continue;
                string key = Key(i); if (!groups.TryGetValue(key, out var group)) groups[key] = group = new List<int>(); group.Add(i);
            }
            var canonical = Enumerable.Range(0, count).ToArray();
            foreach (var group in groups.Values)
            {
                var mean = Vector3.zero; foreach (int i in group) mean += normals[i]; mean.Normalize();
                if (group.Any(i => Vector3.Dot(normals[i], mean) < .8f))
                    throw new InvalidOperationException(wash.name + ": discontinuous surface normal requires explicit treatment");
                foreach (int i in group) { normals[i] = mean; canonical[i] = group[0]; foreach (int j in group) if (i != j) graph[i].Add(j); }
            }
            var heights = new float[count]; var anchors = new List<int>();
            float width = Mathf.Clamp(source.bounds.size.x * wash.meshToWorldScale * .82f, .10f, .24f);
            float peak = Mathf.Clamp(wash.shellRenderer.sharedMaterial.GetFloat("_FoamThickness") * .8f, .014f, .040f);
            // Each body part supplies one crown. Surface distances prevent influence jumping to a nearby leg.
            foreach (int part in new[] {0, 1, 2})
            {
                var candidates = Enumerable.Range(0, source.vertexCount).Where(i => used[i] && Mathf.RoundToInt(wash.boneToPart[weights[i].boneIndex0]) == part).ToArray();
                if (candidates.Length == 0) throw new InvalidOperationException("Missing torso part " + part);
                int seed = candidates.OrderByDescending(i => positions[i].z - Mathf.Abs(positions[i].x - (part == 1 ? -.015f : .009f)) * .25f).First();
                anchors.Add(seed);
                var distance = Enumerable.Repeat(float.PositiveInfinity, count).ToArray(); distance[seed] = 0;
                var pending = new HashSet<int> {seed};
                while (pending.Count > 0)
                {
                    int a = pending.OrderBy(i => distance[i]).First(); pending.Remove(a);
                    foreach (int b in graph[a])
                    {
                        float d = distance[a] + Vector3.Distance(positions[a], positions[b]) * wash.meshToWorldScale;
                        if (d >= width || d >= distance[b]) continue;
                        distance[b] = d; pending.Add(b);
                    }
                }
                for (int i = 0; i < count; i++)
                {
                    float q = Mathf.Clamp01(distance[i] / width); float k = 1 - q * q;
                    heights[i] += peak * k * k * (part == 1 ? .72f : 1);
                }
            }
            // Taper at triangle boundaries (including the excluded face), rather than leaving an upright rim.
            var edgeCount = new Dictionary<long, int>();
            long Edge(int a, int b) { a = canonical[a]; b = canonical[b]; return ((long)Math.Min(a,b) << 32) | (uint)Math.Max(a,b); }
            for (int f = 0; f < kept.Count; f += 3) for (int e = 0; e < 3; e++)
            { long key = Edge(kept[f+e], kept[f+(e+1)%3]); edgeCount.TryGetValue(key, out int n); edgeCount[key] = n+1; }
            var boundary = new HashSet<int>();
            foreach (var edge in edgeCount.Where(e => e.Value == 1)) { boundary.Add((int)(edge.Key >> 32)); boundary.Add((int)edge.Key); }
            var boundaryDistance = Enumerable.Repeat(float.PositiveInfinity, count).ToArray();
            var queue = new HashSet<int>();
            for (int i = 0; i < count; i++) if (boundary.Contains(canonical[i])) { boundaryDistance[i] = 0; queue.Add(i); }
            while (queue.Count > 0)
            {
                int a = queue.OrderBy(i => boundaryDistance[i]).First(); queue.Remove(a);
                foreach (int b in graph[a])
                {
                    float d = boundaryDistance[a] + Vector3.Distance(positions[a], positions[b]) * wash.meshToWorldScale;
                    if (d >= .04f || d >= boundaryDistance[b]) continue;
                    boundaryDistance[b] = d; queue.Add(b);
                }
            }
            float coat = Coat(wash);
            var expanded = new Vector3[count]; var config = new List<Vector4>(count);
            for (int i = 0; i < count; i++)
            {
                heights[i] = .050f*(1-Mathf.Exp(-heights[i]/.050f))*Mathf.SmoothStep(0, 1,boundaryDistance[i] / .04f);
                var w=weights[i];
                float Torso(int bone)=>wash.boneToPart[bone]<=2?1:0;
                float torso=Torso(w.boneIndex0)*w.weight0+Torso(w.boneIndex1)*w.weight1+Torso(w.boneIndex2)*w.weight2+Torso(w.boneIndex3)*w.weight3;
                float localCoat=Mathf.Lerp(wash.shellRenderer.sharedMaterial.GetFloat("_BaseOffset")+.009f,coat,torso);
                float Tail(int bone)=>wash.boneToPart[bone]==8?1:0;
                float tail=Tail(w.boneIndex0)*w.weight0+Tail(w.boneIndex1)*w.weight1+Tail(w.boneIndex2)*w.weight2+Tail(w.boneIndex3)*w.weight3;
                localCoat=Mathf.Lerp(localCoat,wash.shellRenderer.sharedMaterial.GetFloat("_BaseOffset")+wash.shellRenderer.sharedMaterial.GetFloat("_FoamThickness")*.6f,tail);
                localCoat=Mathf.Max(localCoat,FurCoatFloor(wash));
                // Meet the excluded face at its bed height instead of leaving a raised open collar.
                localCoat=Mathf.Lerp(wash.shellRenderer.sharedMaterial.GetFloat("_BaseOffset"),localCoat,Mathf.SmoothStep(0,1,boundaryDistance[i]/.04f));
                expanded[i] = positions[i] + normals[i] * ((localCoat + heights[i]) / wash.meshToWorldScale);
                config.Add(new Vector4(uv[canonical[i]].x, uv[canonical[i]].y, heights[i], localCoat));
            }
            var shade = new Vector3[count];
            for (int f = 0; f < kept.Count; f += 3)
            {
                int a=kept[f], b=kept[f+1], c=kept[f+2]; var n=Vector3.Cross(expanded[b]-expanded[a],expanded[c]-expanded[a]);
                shade[a]+=n; shade[b]+=n; shade[c]+=n;
            }
            foreach (var group in groups.Values)
            { var n=Vector3.zero; foreach(int i in group)n+=shade[i]; n.Normalize(); foreach(int i in group)shade[i]=n; }
            var tangents = new Vector4[count];
            for(int i=0;i<count;i++) { var n=shade[i].sqrMagnitude>.01f?shade[i].normalized:normals[i]; tangents[i]=new Vector4(n.x,n.y,n.z,1); }
            var mesh = new Mesh {name="KH_PCContinuousFoam",vertices=positions,normals=normals,tangents=tangents,uv=uv,boneWeights=weights,bindposes=source.bindposes};
            mesh.SetUVs(1,config);mesh.SetUVs(2,weights.Select(w=>new Vector2(wash.boneToPart[w.boneIndex0],0)).ToList());mesh.triangles=kept.ToArray();mesh.bounds=source.bounds;
            float gap=0; foreach(var group in groups.Values)foreach(int i in group)gap=Mathf.Max(gap,Vector3.Distance(expanded[i],expanded[group[0]])*wash.meshToWorldScale);
            return new Surface {mesh=mesh,anchors=anchors.ToArray(),used=Enumerable.Range(0,source.vertexCount).Where(i=>used[i]).ToArray(),parentA=parentA.ToArray(),parentB=parentB.ToArray(),heights=heights,maxHeight=config.Max(v=>v.z+v.w),maxSeamGap=gap};
        }

        internal static float Coat(KH_DogWash wash) => wash.shellRenderer.sharedMaterial.GetFloat("_BaseOffset") + wash.shellRenderer.sharedMaterial.GetFloat("_FoamThickness") * .28f;
        // Authored long-fur clearance; shared by the bed and displaced surface. See v3 visual evidence.
        internal static float FurCoatFloor(KH_DogWash wash) => KH_PCFoamGenerateEditor.Key(wash)=="Samoyed"?.045f:0;

        [MenuItem("Tools/KominkaHillside/PC Foam/30 - Survey Continuous Surface")]
        public static void Survey()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit required");
            var log = new StringBuilder();
            foreach (var wash in KH_PCFoamSetupEditor.Washes(UnityEngine.SceneManagement.SceneManager.GetActiveScene()))
            {
                var mesh = KH_PCFoamAuditEditor.Body(wash).sharedMesh;
                var p = mesh.vertices; var weights = mesh.boneWeights;
                log.AppendLine(wash.name + " scale=" + wash.meshToWorldScale + " bounds=" + mesh.bounds.ToString("F5")
                    + " triangles=" + mesh.triangles.Length / 3 + " base=" + wash.shellRenderer.sharedMaterial.GetFloat("_BaseOffset")
                    + " thickness=" + wash.shellRenderer.sharedMaterial.GetFloat("_FoamThickness"));
                for (int part = 0; part < 9; part++)
                {
                    var indices = Enumerable.Range(0, p.Length).Where(i => Mathf.RoundToInt(wash.boneToPart[weights[i].boneIndex0]) == part).ToArray();
                    if (indices.Length == 0) continue;
                    var bounds = new Bounds(p[indices[0]], Vector3.zero);
                    foreach (int i in indices) bounds.Encapsulate(p[i]);
                    log.AppendLine(part + " vertices=" + indices.Length + " " + bounds.ToString("F5"));
                }
            }
            Directory.CreateDirectory("Temp/PCFoamV3");
            File.WriteAllText("Temp/PCFoamV3/survey.txt", log.ToString());
        }
    }
}
