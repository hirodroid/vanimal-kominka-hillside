"""Parallel-overlap (Z-fight candidate) check for the prop-finish FBX, read back in a fresh Blender.
BUG-ENV-005: Quest's depth precision makes any parallel, same-facing, overlapping pair of faces flicker
at distance, so the target is 0 such pairs (not a millimetre gap).
Run: blender --background --factory-startup --python-exit-code 1 --python scripts/art/check-prop-overlap.py [-- Bottle PhotoFrame ...]
Reports per model: triangles, bounds (Unity axes: x, y=Blender z, z=-Blender y) and every triangle pair with
|n1.n2|>0.999 and same facing, plane gap <= 30 mm, overlapping projected area >= 0.1 cm^2.
Coplanar neighbours that only share an edge have zero overlap and are not counted.
Gap: pre-filter keeps a pair when B's vertex distances to A's plane span any value within 30 mm (conservative for
near-parallel, wide triangles); the counted gap is the smallest distance along nA from A's plane to B's plane over the
overlap polygon's vertices (0 when the planes cross inside it), as the Unity detector (ZFight/7) measures it.
Exit code 1 when any pair is found (with --python-exit-code 1); pass --report-only after -- to always exit 0, --dir=<folder> for other FBX.
"""
import bpy,sys,json,pathlib
import numpy as np
ROOT=pathlib.Path(__file__).resolve().parents[2]
DIR=ROOT/'Assets/KominkaHillside/Models/PropFinish'
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
report_only='--report-only' in args;names=[a for a in args if not a.startswith('--')] or ['Bottle','PhotoFrame']
DIR=next((pathlib.Path(a[6:]) for a in args if a.startswith('--dir=')),DIR)  # --dir=<folder> checks other FBX (self-test)
GAP=.03;MINAREA=1e-5;PAR=.999
def clip(poly,a,b):
    # Keep the part of poly left of edge a->b (counter-clockwise clipper).
    out=[]
    def side(p):return (b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0])
    for i in range(len(poly)):
        p,q=poly[i],poly[(i+1)%len(poly)];sp,sq=side(p),side(q)
        if sp>=0:out.append(p)
        if (sp>=0)!=(sq>=0):
            t=sp/(sp-sq);out.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
    return out
def area(poly):
    return .5*sum(poly[i][0]*poly[(i+1)%len(poly)][1]-poly[(i+1)%len(poly)][0]*poly[i][1] for i in range(len(poly))) if len(poly)>2 else 0
def ccw(t):return t if area(t)>0 else t[::-1]
total=0;out=[]
for name in names:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(DIR/(name+'.fbx')))
    obs=[o for o in bpy.context.scene.objects if o.type=='MESH'];assert len(obs)==1,name
    o=obs[0];m=o.data;m.calc_loop_triangles();W=o.matrix_world
    V=np.array([(W@v.co)[:] for v in m.vertices]);T=np.array([t.vertices[:] for t in m.loop_triangles])
    P=V[T];N=np.cross(P[:,1]-P[:,0],P[:,2]-P[:,0]);A=np.linalg.norm(N,axis=1)*.5;N=N/np.maximum(np.linalg.norm(N,axis=1),1e-20)[:,None]
    lo=P.min(1);hi=P.max(1);C=P.mean(1)
    pairs=[]
    for i in range(len(T)):
        j=np.arange(i+1,len(T));d=N[j]@N[i]
        j=j[d>PAR]
        if not len(j):continue
        dist=(P[j]-P[i][0])@N[i];j=j[(dist.min(1)<=GAP)&(dist.max(1)>=-GAP)]   # any part of B can lie within GAP of A's plane
        j=j[np.all((lo[j]<=hi[i]+GAP)&(hi[j]>=lo[i]-GAP),axis=1)]
        if not len(j):continue
        n=N[i];u=np.cross(n,[1,0,0] if abs(n[0])<.9 else [0,1,0]);u/=np.linalg.norm(u);w=np.cross(n,u);h0=P[i][0]@n
        ti=ccw([tuple((p@u,p@w)) for p in P[i]])
        for k in j:
            poly=ccw([tuple((p@u,p@w)) for p in P[k]])
            for e in range(3):
                poly=clip(poly,ti[e],ti[(e+1)%3])
                if not poly:break
            a=abs(area(poly))
            if a<MINAREA:continue
            # distance along nA from A's plane to B's plane at each overlap vertex
            nb=N[k];db=P[k][0]@nb;t=[(db-(pu*u+pw*w+h0*n)@nb)/(n@nb) for pu,pw in poly]
            g=0. if min(t)<=0<=max(t) else min(abs(x) for x in t)
            if g<=GAP:pairs.append(dict(a=int(i),b=int(k),gap_mm=round(float(g)*1000,2),area_cm2=round(a*1e4,3),normal=[round(float(x),3) for x in (n[0],n[2],-n[1])],centre=[round(float(x),4) for x in (C[i][0],C[i][2],-C[i][1])]))
    b0=V.min(0);b1=V.max(0)
    res=dict(name=name,triangles=len(T),boundsMinUnity=[round(float(x),4) for x in (b0[0],b0[2],-b1[1])],boundsMaxUnity=[round(float(x),4) for x in (b1[0],b1[2],-b0[1])],overlapPairs=len(pairs),overlapAreaCm2=round(sum(p['area_cm2'] for p in pairs),2),pairs=sorted(pairs,key=lambda p:-p["area_cm2"]))
    total+=len(pairs);out.append(res)
print('OVERLAP_RESULT '+json.dumps(out))
if total and not report_only:raise SystemExit(1)
