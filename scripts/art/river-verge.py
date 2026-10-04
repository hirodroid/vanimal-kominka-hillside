"""World-owned river verge kit. Blender background, metres, no external source.
Broad bent grass blades and layered, asymmetric river stones; vertex colour.
"""
import bpy, math, random, pathlib, json
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/KominkaHillside/Models/Environment/RiverVerge'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
report=[]
def save(name,verts,faces,colors):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(ob)
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    attr=mesh.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='POINT')
    for i,c in enumerate(colors):attr.data[i].color=(*c,1)
    uv=mesh.uv_layers.new(name='UVMap')
    for face in mesh.polygons:
        normal=face.normal;u=normal.cross(Vector((0,0,1)))
        if u.length<.01:u=normal.cross(Vector((0,1,0)))
        u.normalize();v=normal.cross(u).normalized()
        for li in face.loop_indices:
            p=mesh.vertices[mesh.loops[li].vertex_index].co;uv.data[li].uv=(p.dot(u),p.dot(v))
    tri=ob.modifiers.new('Triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
    bad=0
    for f in mesh.polygons:
        a,b,c=[mesh.uv_layers.active.data[i].uv for i in f.loop_indices]
        if abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))<1e-10:bad+=1
    assert bad==0
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,path_mode='AUTO',colors_type='LINEAR')
    report.append(dict(name=name,triangles=len(mesh.polygons),vertices=len(mesh.vertices),dimensions=list(ob.dimensions),degenerateUVTriangles=bad))
for variant in range(3):
    rng=random.Random(1030+variant);vs=[];fs=[];cs=[]
    # Three displaced rings give broad ledges and inclined faces, not noisy spheres.
    for ring,(z,rad,dx,dy) in enumerate([(0,.8,0,0),(.28,1,.08,-.03),(.7,.65,-.18,.05)]):
        for j in range(9):
            a=j*math.tau/9;rr=rad*rng.uniform(.88,1.1)
            vs.append((math.cos(a)*rr,math.sin(a)*rr*.67,z*rng.uniform(.89,1.12)))
            vs[-1]=(vs[-1][0]+dx,vs[-1][1]+dy,vs[-1][2])
            k=rng.uniform(.88,1.08);cs.append((.39*k,.39*k,.34*k))
    for r in range(2):
        for j in range(9):a=r*9+j;b=r*9+(j+1)%9;fs.append((a,b,b+9,a+9))
    fs.append(tuple(reversed(range(9))));fs.append(tuple(range(18,27)))
    save('KH_RiverStone_'+str(variant),vs,fs,cs)
for variant in range(2):
    rng=random.Random(2200+variant);vs=[];fs=[];cs=[]
    for blade in range(13):
        a=rng.random()*math.tau;h=rng.uniform(.20,.43)*(1 if variant==0 else 1.35);w=rng.uniform(.035,.07)
        root=Vector((rng.uniform(-.17,.17),rng.uniform(-.17,.17),0));out=Vector((math.cos(a),math.sin(a),0));side=Vector((-out.y,out.x,0));i=len(vs)
        for p in [root-side*w,root+side*w,root+out*h*.22+Vector((0,0,h*.60))-side*w*.58,root+out*h*.22+Vector((0,0,h*.60))+side*w*.58,root+out*h*.60+Vector((0,0,h))]:vs.append(tuple(p))
        shade=rng.uniform(.85,1.12)
        cs.extend([( .27*shade,.31*shade,.15*shade)]*2+[(.40*shade,.44*shade,.22*shade)]*2+[(.59*shade,.55*shade,.29*shade)])
        fs.extend([(i,i+1,i+3,i+2),(i+2,i+3,i+4)])
    save('KH_RiverGrass_'+str(variant),vs,fs,cs)
e=ROOT/'Temp/scenery-20261004/valley';e.mkdir(parents=True,exist_ok=True)
(e/'blender-geometry.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('VERGE_RESULT '+json.dumps(report))
