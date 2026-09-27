"""Blender background generator. Own outputs only; no GUI scene is touched."""
import bpy, math, random, json, pathlib
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/KominkaHillside/Models/Environment/CourseBridge'
SOURCE=pathlib.Path('C:/work/unity/vrc-asset-lab/KominkaHillside/Bridge')
OUT.mkdir(parents=True,exist_ok=True); SOURCE.mkdir(parents=True,exist_ok=True)
random.seed(170927)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
pieces=[]
mat=bpy.data.materials.new('KH_WeatheredBridge'); mat.diffuse_color=(.34,.24,.15,1); mat.use_nodes=True
bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(.34,.24,.15,1);bsdf.inputs['Roughness'].default_value=.92
tex=ROOT/'Assets/KominkaHillside/Textures/Environment/CourseAutumn/BridgeWood.png'
if tex.exists():
    n=mat.node_tree.nodes.new('ShaderNodeTexImage'); n.image=bpy.data.images.load(str(tex));n.image.pack();mat.node_tree.links.new(n.outputs['Color'],bsdf.inputs['Base Color'])

def timber(name,loc,size,bevel=.015,angle=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    # Long grain is the V axis; projected UV per face and board-specific offset.
    dims=list(size);major=max(range(3),key=lambda i:dims[i]); offset=random.random()*8
    uv=o.data.uv_layers.active
    for p in o.data.polygons:
        normal=max(range(3),key=lambda i:abs(p.normal[i])); axes=[i for i in range(3) if i!=normal]
        along=major if major in axes else axes[1]; across=next(i for i in axes if i!=along)
        for li in p.loop_indices:
            v=o.data.vertices[o.data.loops[li].vertex_index].co;uv.data[li].uv=(v[across]*1.7+offset,v[along]*.35+offset*.3)
    if bevel:
        mod=o.modifiers.new('worn_edge','BEVEL');mod.width=bevel;mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    o.rotation_euler[2]=angle
    o.data.materials.append(mat);pieces.append(o);return o

# Deck ends and upper plane match the original gameplay surface exactly.
for i in range(28):
    step=10/28;x=-5+(i+.5)*step
    timber('Deck_%02d'%i,(x,0,-.125),(step-.010,3.5+random.uniform(-.022,.022),.25),.009)
for y in [-1.32,1.32]:timber('LongitudinalGirder',(0,y,-.34),(10.2,.25,.28),.025)
for x in [-4.7,-2.35,0,2.35,4.7]:timber('CrossBearer',(x,0,-.46),(.24,3.64,.2),.017)
for y in [-1.72,1.72]:
    for i,x in enumerate([-4.85,-2.425,0,2.425,4.85]):
        timber('Post',(x,y,.46),(.22,.22,1.38),.025)
        timber('PostCap',(x,y,1.17),(.27,.27,.10),.023)
        # Pegs protrude by millimetres on the outside face; 8 sides only.
        for z in [.43,.98]:
            bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=.034,depth=.235,location=(x,y,z),rotation=(math.pi/2,0,0))
            o=bpy.context.object;o.name='WoodPeg';o.data.materials.append(mat);pieces.append(o)
    timber('TopRail',(0,y,1.025),(10.28,.195,.15),.025)
    timber('MidRail',(0,y,.46),(10.04,.12,.12),.017)
    # Paired diagonal knee braces read as joined timber, outside the clear walking lane.
    for x,sign in [(-4.85,1),(4.85,-1)]:
        a=Vector((x,y,-.18));b=Vector((x+sign*.75,y,.42));mid=(a+b)/2
        o=timber('KneeBrace',mid,(.11,.13,(b-a).length),.012)
        o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()

bpy.ops.object.select_all(action='DESELECT')
for o in pieces:o.select_set(True)
bpy.context.view_layer.objects.active=pieces[0];bpy.ops.object.join();bridge=bpy.context.object;bridge.name='KH_CourseBridge'
bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
# Dedupe all identical material slots after joining.
bridge.data.materials.clear();bridge.data.materials.append(mat)
for p in bridge.data.polygons:p.material_index=0
tri=bridge.modifiers.new('triangulate','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
assert len(bridge.data.polygons)<=12000
assert len(bridge.data.uv_layers)>0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'KH_CourseBridge.generated.blend'))
bpy.ops.export_scene.fbx(filepath=str(OUT/'KH_CourseBridge.fbx'),use_selection=True,object_types={'MESH'},use_mesh_modifiers=True,bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,path_mode='AUTO',use_custom_props=False)
report={'triangles':len(bridge.data.polygons),'vertices':len(bridge.data.vertices),'materials':len(bridge.data.materials),'dimensionsBlender':list(bridge.dimensions),'uvLayers':len(bridge.data.uv_layers),'fbx':str(OUT/'KH_CourseBridge.fbx'),'source':str(SOURCE/'KH_CourseBridge.generated.blend')}
(SOURCE/'geometry.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('BRIDGE_RESULT '+json.dumps(report))
# Preview scene is separate from the exported mesh selection.
bpy.ops.object.camera_add(location=(12,-11,8));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,.1))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=13.5;bpy.context.scene.camera=cam
bpy.ops.object.light_add(type='AREA',location=(0,-4,8));bpy.context.object.data.energy=1800;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=9
bpy.context.scene.world.color=(.18,.18,.18)
sc=bpy.context.scene;sc.render.engine='CYCLES';sc.cycles.samples=24;sc.render.resolution_x=1200;sc.render.resolution_y=760;sc.render.resolution_percentage=100;sc.render.filepath=str(SOURCE/'bridge-preview.png')
bpy.ops.render.render(write_still=True)
