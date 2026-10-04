"""World-owned reproducible bench; run with Blender --background --factory-startup.
No GUI state or purchased source is edited. Existing generated bridge wood is reused.
"""
import bpy, json, pathlib, tempfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/KominkaHillside/Models/Environment/RoadsideBench'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
pieces=[]
def timber(name,location,size):
    bpy.ops.mesh.primitive_cube_add(size=1,location=location)
    o=bpy.context.object;o.name=name;o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    bevel=o.modifiers.new('Soft timber edges','BEVEL');bevel.width=.008;bevel.segments=1
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    # Face projection after bevel avoids collapsed bevel UVs; long grain along V.
    uv=o.data.uv_layers.active;major=max(range(3),key=lambda i:size[i]);offset=len(pieces)*.173
    for face in o.data.polygons:
        normal=max(range(3),key=lambda i:abs(face.normal[i]));axes=[i for i in range(3) if i!=normal]
        along=major if major in axes else axes[1];across=next(i for i in axes if i!=along)
        for li in face.loop_indices:
            p=o.data.vertices[o.data.loops[li].vertex_index].co;uv.data[li].uv=(p[across]*1.7+offset,p[along]*.35+offset)
    pieces.append(o)
for i in range(3):timber('Seat_%d'%i,(0,(i-1)*.17,.4125),(1.6,.16,.075))
for x in [-.57,.57]:
    timber('SeatBearer',(x,0,.3375),(.12,.48,.075))
    for y in [-.16,.16]:timber('Leg',(x,y,.165),(.095,.095,.33))
    timber('EndStretcher',(x,0,.12),(.075,.38,.075))
timber('LongStretcher',(0,0,.15),(1.20,.075,.08))
bpy.ops.object.select_all(action='DESELECT')
for o in pieces:o.select_set(True)
bpy.context.view_layer.objects.active=pieces[0];bpy.ops.object.join();o=bpy.context.object;o.name='KH_RoadsideBench'
bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
tri=o.modifiers.new('FinalTriangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
uv=o.data.uv_layers.active;bad=0
for p in o.data.polygons:
    a,b,c=[uv.data[i].uv for i in p.loop_indices]
    if abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))<1e-10:bad+=1
assert bad==0 and len(o.data.polygons)<1000
source=pathlib.Path(tempfile.gettempdir())/'KH_RoadsideBench.generated.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(source))
bpy.ops.export_scene.fbx(filepath=str(OUT/'KH_RoadsideBench.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,path_mode='AUTO')
report={'triangles':len(o.data.polygons),'vertices':len(o.data.vertices),'dimensions':list(o.dimensions),'degenerateUVTriangles':bad,'sourceRecipe':'scripts/art/roadside-bench.py'}
(ROOT/'Temp/scenery-20261004/bench-geometry.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('BENCH_RESULT '+json.dumps(report))
