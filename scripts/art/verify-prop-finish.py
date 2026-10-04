"""Read back exported FBX in a fresh Blender process; independent mesh/UV validation."""
import bpy,json,pathlib,math
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[2]
reports=json.loads((ROOT/'Temp/prop-finish-20261004/geometry.json').read_text())
out=[]
for expected in reports:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/'Assets/KominkaHillside/Models/PropFinish'/(expected['name']+'.fbx')))
    obs=[o for o in bpy.context.scene.objects if o.type=='MESH'];assert len(obs)==1
    o=obs[0];m=o.data;m.calc_loop_triangles();uv=m.uv_layers.active
    assert len(m.loop_triangles)==expected['triangles']
    bad=0
    for t in m.loop_triangles:
        a,b,c=[uv.data[i].uv for i in t.loops]
        bad+=abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))<1e-12
    assert bad==0
    result=dict(name=expected['name'],triangles=len(m.loop_triangles),uvTrianglesDegenerate=bad,finiteVertices=all(math.isfinite(v) for p in m.vertices for v in p.co),normalLengths=[min(v.normal.length for v in m.vertices),max(v.normal.length for v in m.vertices)])
    if expected['name']=='Bottle':
        candidates=[]
        for i,v in enumerate(uv.data):
            if .735<v.uv.x<.765 and .50<v.uv.y<1:
                p=o.matrix_world@m.vertices[m.loops[i].vertex_index].co
                candidates.append(list(p))
        result['labelCentreEdgePositionsBlender']=candidates[:12]
    out.append(result)
(ROOT/'Temp/prop-finish-20261004/roundtrip.json').write_text(json.dumps(out,indent=2))
print(json.dumps(out))
