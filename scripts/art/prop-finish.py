"""Close-range world props, original procedural art. Blender 5.x, metres.
Run with --background --factory-startup --python-exit-code 1 --python this_file.
Inputs are Unity dimensions; models/UV/normal textures are generated, no external art.
"""
import bpy, math, json, pathlib
import numpy as np
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/KominkaHillside/Models/PropFinish'
SRC=ROOT/'art-source/prop-finish/generated'
REPORT=ROOT/'Temp/prop-finish-20261004'
for p in (OUT,SRC,REPORT):p.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
rng=np.random.default_rng(10426)
N=1024
y,x=np.mgrid[0:N,0:N].astype(np.float32)/(N-1)
warp=x+.012*np.sin(y*7)+.005*np.sin(y*19+x*4)
grain=np.sin(warp*133+3*np.sin(warp*15))*.6+np.sin(warp*237+y*.8)*.25+np.sin(warp*81+y*1.3)*.15
fine=np.sin(warp*790+y*2)
knot=np.zeros_like(x)
for cx,cy in ((.26,.32),(.74,.81)):
    radius=np.sqrt(((x-cx)*1.4)**2+((y-cy)*.3)**2)
    knot+=np.sin(radius*255)*np.exp(-radius*radius/0.003)*.025
noise=rng.normal(0,.003,(N,N)).astype(np.float32)
tone=.025*grain+.004*fine+noise+knot
base=np.ones((N*2,N*2,4),np.float32)
norm=np.ones_like(base);norm[:,:,:3]=(.5,.5,1)
packed=np.zeros_like(base);packed[:,:,3]=.23
def tile(k):return (slice((k//2)*N,(k//2+1)*N),slice((k%2)*N,(k%2+1)*N))
base[tile(0)][:,:,:3]=np.clip(np.array([.52,.393,.275])+tone[:,:,None]*np.array([1,.87,.65]),0,1)
height=.045*grain+.012*fine+knot*.3
dy,dx=np.gradient(height)
v=np.stack((-dx*9,-dy*9,np.ones_like(x)),axis=2);v/=np.linalg.norm(v,axis=2)[:,:,None]
norm[tile(0)][:,:,:3]=v*.5+.5
packed[tile(0)][:,:,3]=np.clip(.22+tone*.8,.12,.32)
base[tile(1)][:,:,:3]=np.clip(np.array([.27,.29,.28])+noise[:,:,None]*.7,0,1)
packed[tile(1)][:,:,0]=.8;packed[tile(1)][:,:,3]=.43
base[tile(2)][:,:,:3]=np.clip(np.array([.68,.42,.48])+noise[:,:,None]*.4,0,1)
packed[tile(2)][:,:,3]=.34
label=base[tile(3)];label[:,:,:3]=(.92,.89,.8);packed[tile(3)][:,:,3]=.24
# Original paw seal and restrained rules; repeat once around the bottle label.
mask=(((x-.5)/.075)**2+((y-.55)/.12)**2<1)
for cx,cy,rx,ry in ((.4,.69,.032,.05),(.46,.76,.032,.052),(.54,.76,.032,.052),(.6,.69,.032,.05)):
    mask|=((x-cx)/rx)**2+((y-cy)/ry)**2<1
label[mask,:3]=(.38,.46,.4)
label[(abs(y-.18)<.008)|(abs(y-.88)<.008),:3]=(.66,.47,.43)
# Small original block lettering, rasterised at atlas resolution.
font={'D':['1110','1001','1001','1001','1110'],'O':['0110','1001','1001','1001','0110'],'G':['0111','1000','1011','1001','0111'],'W':['10001','10001','10101','10101','01010'],'A':['0110','1001','1111','1001','1001'],'S':['0111','1000','0110','0001','1110'],'H':['1001','1001','1111','1001','1001'],' ':['00']*5}
word='DOG WASH';scale=6;width=sum(len(font[c][0])+1 for c in word)*scale;xx=(N-width)//2
for c in word:
    glyph=font[c]
    for row,bits in enumerate(glyph):
        for col,bit in enumerate(bits):
            if bit=='1':label[280+(4-row)*scale:280+(5-row)*scale,xx+col*scale:xx+(col+1)*scale,:3]=(.32,.36,.31)
    xx+=(len(glyph[0])+1)*scale
def save(name,arr):
    im=bpy.data.images.new(name,width=N*2,height=N*2,alpha=True)
    im.colorspace_settings.name='Non-Color';im.pixels.foreach_set(np.clip(arr,0,1).reshape(-1));im.filepath_raw=str(OUT/(name+'.png'));im.file_format='PNG';im.save();return im
images=[save('Props_BaseColor',base),save('Props_Normal',norm),save('Props_MetallicSmoothness',packed)]
mat=bpy.data.materials.new('KH_PropFinish');mat.diffuse_color=(.52,.39,.28,1);mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF');tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=images[0];tex.image.colorspace_settings.name='sRGB';mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color']);bs.inputs['Roughness'].default_value=.72
pieces=[];models=[];reports=[]
def U(p):return Vector((p[0],-p[2],p[1]))
def uvmap(o,k,size=None):
    uv=o.data.uv_layers.active or o.data.uv_layers.new(name='UVMap')
    coords=[v.co for v in o.data.vertices];lo=Vector(tuple(min(v[i] for v in coords) for i in range(3)));hi=Vector(tuple(max(v[i] for v in coords) for i in range(3)));span=hi-lo
    major=max(range(3),key=lambda i:span[i]);offset=(len(pieces)*.137)% .35
    for p in o.data.polygons:
        normal=max(range(3),key=lambda i:abs(p.normal[i]));axes=[i for i in range(3) if i!=normal];along=major if major in axes else axes[1];across=next(i for i in axes if i!=along)
        for li in p.loop_indices:
            co=o.data.vertices[o.data.loops[li].vertex_index].co
            u=(co[across]-lo[across])/max(span[across],1e-6)*.48+.1+offset
            v=(co[along]-lo[along])/max(span[along],1e-6)*.92+.04
            uv.data[li].uv=((k%2+u)/2,(k//2+v)/2)
def box(name,p,size,k=0,bevel=.003,angle=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=U(p));o=bpy.context.object;o.name=name;o.dimensions=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    b=o.modifiers.new('Crafted edge','BEVEL');b.width=min(bevel,min(size)*.22);b.segments=2;bpy.ops.object.modifier_apply(modifier=b.name)
    uvmap(o,k);o.rotation_euler.x=math.radians(angle)
    for face in o.data.polygons:face.use_smooth=True
    b=o.modifiers.new('Face weighted normals','WEIGHTED_NORMAL');b.keep_sharp=True;bpy.ops.object.modifier_apply(modifier=b.name)
    pieces.append(o);return o
def lathe(name,profile,k=2,sides=32,position=(0,0,0),direction=(0,1,0),label_uv=False):
    # Positive-height cross sections in Unity-local Y. Winding is outward.
    verts=[];faces=[];uvs=[];d=U(direction).normalized();q=Vector((0,0,1)).rotation_difference(d)
    for h,r in profile:
        for j in range(sides):
            a=2*math.pi*j/sides;verts.append(q@Vector((r*math.sin(a),r*math.cos(a),h))+U(position))
    for i in range(len(profile)-1):
        for j in range(sides):faces.append((i*sides+j,(i+1)*sides+j,(i+1)*sides+(j+1)%sides,i*sides+(j+1)%sides))
    # Cap polygons use opposite winding to the side strip.
    faces.extend([tuple(range(sides-1,-1,-1)),tuple((len(profile)-1)*sides+j for j in range(sides))])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o)
    # Recalculate outward normals, including caps, independently of profile orientation.
    bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT');o.select_set(False)
    uv=mesh.uv_layers.new(name='UVMap')
    for face in mesh.polygons:
        face.use_smooth=len(face.vertices)==4
        js=[mesh.loops[li].vertex_index%sides for li in face.loop_indices];seam=0 in js and sides-1 in js
        for li in face.loop_indices:
            vi=mesh.loops[li].vertex_index;j=vi%sides
            u=(sides if seam and j==0 else j)/sides;v=(vi//sides)/(len(profile)-1)
            if len(face.vertices)>4:
                u=.5+.42*math.sin(2*math.pi*j/sides);v=.5+.42*math.cos(2*math.pi*j/sides)
            if not label_uv:u=.08+u*.84;v=.08+v*.84
            else:u=.02+(1-u)*.96;v=.02+v*.96
            if name in ('Cap','Outlet'):u=.04+u*.07;v=.45+v*.07
            uv.data[li].uv=((k%2+u)/2,(k//2+v)/2)
    # Analytic lathe normals prevent triangulation-weighted wedges on long cylindrical faces.
    ring_normals=[]
    for i,(h,r) in enumerate(profile):
        neighbours=[]
        for a,b in ((i-1,i),(i,i+1)):
            if a<0 or b>=len(profile):continue
            dh=profile[b][0]-profile[a][0];dr=profile[b][1]-profile[a][1]
            neighbours.append(Vector((dh,-dr)).normalized())
        ring_normals.append(sum(neighbours,Vector((0,0))).normalized())
    custom=[]
    for face in mesh.polygons:
        for li in face.loop_indices:
            vi=mesh.loops[li].vertex_index;j=vi%sides
            if len(face.vertices)>4:custom.append(face.normal[:]);continue
            nr,nh=ring_normals[vi//sides];a=2*math.pi*j/sides
            custom.append((q@Vector((nr*math.sin(a),nr*math.cos(a),nh))).normalized()[:])
    mesh.normals_split_custom_set(custom)
    pieces.append(o);return o
def tube(name,points,r=.006,k=1,sides=8):
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.resolution_u=1;curve.bevel_depth=r;curve.bevel_resolution=1
    spline=curve.splines.new('POLY');spline.points.add(len(points)-1)
    for v,p in zip(spline.points,points):v.co=(*U(p),1)
    o=bpy.data.objects.new(name,curve);bpy.context.collection.objects.link(o);bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.convert(target='MESH');o=bpy.context.object;uvmap(o,k);o.select_set(False);pieces.append(o)
def bolt(x,y,z,r=.009):return lathe('Recessed fastener',[(0,r),(.0015,r),(.003,r*.8)],1,12,(x,y,z),(0,0,-1))
def finish(name,budget):
    bpy.ops.object.select_all(action='DESELECT')
    for o in pieces:o.select_set(True)
    bpy.context.view_layer.objects.active=pieces[0];bpy.ops.object.join();o=bpy.context.object;o.name=name
    bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR');bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    o.data.materials.clear();o.data.materials.append(mat)
    t=o.modifiers.new('Export triangles','TRIANGULATE')
    if hasattr(t,'keep_custom_normals'):t.keep_custom_normals=True
    bpy.ops.object.modifier_apply(modifier=t.name)
    uv=o.data.uv_layers.active;bad=0
    for p in o.data.polygons:
        a,b,c=[uv.data[i].uv for i in p.loop_indices]
        if abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))<1e-12:bad+=1
    n=len(o.data.polygons);assert n<=budget and bad==0,(name,n,budget,bad)
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_space_transform=True,path_mode='AUTO')
    reports.append(dict(name=name,triangles=n,vertices=len(o.data.vertices),dimensionsBlender=list(o.dimensions),degenerateUVTriangles=bad));models.append(o);pieces.clear();o.hide_set(True)
# Shelf, exact existing top at .8 m, front -Z. Four original physics legs remain.
for z in (-.1725,-.0575,.0575,.1725):box('Top plank',(0,.78,z),(1.2,.04,.105),bevel=.004)
for x in (-.55,.55):
    for z in (-.18,.18):box('Leg',(x,.38,z),(.065,.76,.065),bevel=.005)
    box('Lower bearer',(x,.205,0),(.055,.055,.40))
for z in (-.18,.18):box('Apron',(0,.725,z),(1.1,.07,.045))
for z in (-.14,0,.14):box('Lower slat',(0,.245,z),(1.13,.028,.125),bevel=.003)
box('Back rail',(0,.835,.2075),(1.2,.075,.035))
for x in (-.5825,.5825):box('End rail',(x,.82,0),(.035,.04,.38))
for x in (-.55,.55):
    for y0 in (.725,.23):bolt(x,y0,-.206)
finish('Shelf',3000)
# Rack: matching two clip homes, hooks connect the hovering clips to the rail.
box('Crossbar',(0,.095,.055),(1.2,.06,.07),bevel=.004)
for x in (-.56,.56):
    box('Post',(x,-.37,.055),(.06,.92,.06),bevel=.004)
    bolt(x,.095,.018,.008)
for x in (-.2,.2):
    box('Hook plate',(x,.083,.016),(.045,.055,.007),1,.002)
    tube('Bent hook',[(x,.094,.011),(x,.053,-.004),(x,.024,-.012),(x,.012,-.026),(x,.021,-.036),(x,.037,-.036)],.004)
    bolt(x,.099,.011,.004)
finish('Rack',2500)
# Guide frame authored separately to preserve the existing guide parent's pose.
for x in (-.43,.43):box('Guide post',(x,.4575,0),(.04,.665,.04),bevel=.003)
box('Guide back',(0,.63,0),(.82,.28,.018),bevel=.002)
finish('RackGuide',1000)
# Photo frame uses the exact current beam placements exported by the read-only survey.
survey=json.loads((ROOT/'art-source/prop-finish/config.json').read_text(encoding='utf8'))
for part in survey['parts']:
    if not part['path'].startswith('KH_PhotoBoard/Structure/'):continue
    name=part['path'].split('/')[-1];p=part['localPosition'];s=part['localScale'];a=part['localEuler']['x']
    if name.startswith('Roof'):
        # Three long overlapping-looking planks along each roof slope, inside the old envelope.
        for i in range(3):
            dz=(-1+i)*s['z']/3;ang=math.radians(a)
            box(name+str(i),(p['x'],p['y']-math.sin(ang)*dz,p['z']+math.cos(ang)*dz),(s['x'],s['y'],s['z']/3-.0015),bevel=.002,angle=a)
    else:box(name,(p['x'],p['y'],p['z']),(s['x'],s['y'],s['z']),bevel=.005 if name.startswith('Post') else .003,angle=a)
# Narrow lip sits outside the 2.24 x 1.04 m canvas; the photos and pager remain clear.
for x in (-1.144,1.144):box('Frame side',(x,1.37,.006),(.032,1.12,.026))
for y0 in (.828,1.912):box('Frame rail',(0,y0,.006),(2.26,.032,.026))
for x in (-1.22,1.22):
    for y0 in (.9,1.86,2.07):bolt(x,y0,-.022,.012)
finish('PhotoFrame',4000)
# Shampoo: smooth shoulder, bottom moulding, cap collar, slanted hollow outlet.
lathe('Bottle body',[(-.085,.024),(-.083,.028),(-.079,.03),(-.074,.03),(.05,.03),(.059,.029),(.066,.0265),(.072,.020),(.075,.014)],2,32)
sleeve=lathe('Printed sleeve',[(-.054,.0303),(.027,.0303)],3,32,label_uv=True);sleeve.rotation_euler.z=math.pi/2
lathe('Cap',[ (.073,.014),(.075,.015),(.091,.015),(.094,.013)],3,32)
direction=(0,math.cos(math.pi/6),math.sin(math.pi/6))
lathe('Outlet',[(0,.005),(.003,.006),(.028,.006),(.03,.005),(.03,.0034),(.024,.0034)],3,20,(0,.092,0),direction)
finish('Bottle',2000)
for o in models:o.hide_set(False)
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'PropFinish.blend'))
(REPORT/'geometry.json').write_text(json.dumps(reports,indent=2),encoding='utf8')
print('PROP_RESULT '+json.dumps(reports))
