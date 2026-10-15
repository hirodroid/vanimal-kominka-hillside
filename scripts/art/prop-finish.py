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
def box(name,p,size,k=0,bevel=.003,angle=0,segments=2):
    bpy.ops.mesh.primitive_cube_add(size=1,location=U(p));o=bpy.context.object;o.name=name;o.dimensions=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    b=o.modifiers.new('Crafted edge','BEVEL');b.width=min(bevel,min(size)*.22);b.segments=segments;bpy.ops.object.modifier_apply(modifier=b.name)
    uvmap(o,k);o.rotation_euler.x=math.radians(angle)
    for face in o.data.polygons:face.use_smooth=True
    b=o.modifiers.new('Face weighted normals','WEIGHTED_NORMAL');b.keep_sharp=True;bpy.ops.object.modifier_apply(modifier=b.name)
    pieces.append(o);return o
def lathe(name,profile,k=2,sides=32,position=(0,0,0),direction=(0,1,0),label_uv=False,caps=(True,True),label_band=None):
    # Positive-height cross sections in Unity-local Y. Winding is outward.
    # caps=(bottom,top) drops an end disk that another part covers. label_band=(first ring,last ring,phase steps,tile):
    # those side faces map to the label tile on the same surface, so a printed label needs no second, parallel shell.
    verts=[];faces=[];uvs=[];d=U(direction).normalized();q=Vector((0,0,1)).rotation_difference(d)
    for h,r in profile:
        for j in range(sides):
            a=2*math.pi*j/sides;verts.append(q@Vector((r*math.sin(a),r*math.cos(a),h))+U(position))
    for i in range(len(profile)-1):
        for j in range(sides):faces.append((i*sides+j,(i+1)*sides+j,(i+1)*sides+(j+1)%sides,i*sides+(j+1)%sides))
    # Cap polygons use opposite winding to the side strip.
    if caps[0]:faces.append(tuple(range(sides-1,-1,-1)))
    if caps[1]:faces.append(tuple((len(profile)-1)*sides+j for j in range(sides)))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o)
    # Recalculate outward normals, including caps, independently of profile orientation.
    bpy.context.view_layer.objects.active=o;o.select_set(True);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT');o.select_set(False)
    uv=mesh.uv_layers.new(name='UVMap')
    for face in mesh.polygons:
        face.use_smooth=len(face.vertices)==4
        rings=[mesh.loops[li].vertex_index//sides for li in face.loop_indices]
        band=label_band if label_band and len(face.vertices)==4 and label_band[0]<=min(rings) and max(rings)<=label_band[1] else None
        shift=band[2] if band else 0
        js=[(mesh.loops[li].vertex_index+shift)%sides for li in face.loop_indices];seam=0 in js and sides-1 in js
        for li in face.loop_indices:
            vi=mesh.loops[li].vertex_index;j=(vi+shift)%sides
            u=(sides if seam and j==0 else j)/sides;v=(vi//sides)/(len(profile)-1)
            if band:v=(vi//sides-band[0])/(band[1]-band[0])
            if len(face.vertices)>4:
                u=.5+.42*math.sin(2*math.pi*j/sides);v=.5+.42*math.cos(2*math.pi*j/sides)
            if not (label_uv or band):u=.08+u*.84;v=.08+v*.84
            else:u=.02+(1-u)*.96;v=.02+v*.96
            if name in ('Cap','Outlet'):u=.04+u*.07;v=.45+v*.07
            kk=band[3] if band else k
            uv.data[li].uv=((kk%2+u)/2,(kk//2+v)/2)
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
def domed_bolt(x,y,z,r=.009):
    # Low dome head: no flat face parallel to the post face behind it (the tip disk is ~0.05 cm2).
    return lathe('Recessed fastener',[(0,r),(.0015,r),(.003,r*.75),(.004,r*.4),(.0045,r*.1)],1,12,(x,y,z),(0,0,-1))
def tray(name,cy,k=0):
    # Photo board back (BUG-ENV-005 tray): the wood face lies in the canvas plane (z 0) only around the two UI
    # holes, hole walls run back to a rear plate that faces +Z only, so nothing faces the readers behind the canvas.
    # The UI shows a dark backing 35 mm deep through empty photo slots (KH_PhotoGalleryEditor.Place.cs).
    # Rim edges reach 1.5 mm into the frame and the rear plate sits 2 mm in front of the frame's rear and runs 3 mm
    # into it, under the frame's chamfers only: closed seams, and no face parallel to a frame face over it.
    x0,x1=FRAME_IN_X+.0015,FRAME_IN_X+.003;rz=TRAY_REAR-.002
    holes=[((px-w/2)*Mm,(px+w/2)*Mm,cy+(py-h/2)*Mm,cy+(py+h/2)*Mm) for (px,py),(w,h) in CANVAS_HOLES]
    xs=[-x0,holes[0][0],holes[0][1],x0];ys=sorted({FRAME_IN_Y[0]-.0015,FRAME_IN_Y[1]+.0015,*[h[2] for h in holes],*[h[3] for h in holes]})
    verts=[];index={}
    def v(p):
        key=tuple(round(c,6) for c in p)
        if key not in index:index[key]=len(verts);verts.append(U(p))
        return index[key]
    faces=[]
    def quad(a,b,c,d,n):
        # a..d in order around the quad; flip to face n (Unity axes; U() is a proper rotation).
        e1=Vector(b)-Vector(a);e2=Vector(c)-Vector(b)
        q=(a,b,c,d) if e1.cross(e2).dot(Vector(n))>0 else (d,c,b,a)
        faces.append(tuple(v(p) for p in q))
    for i in range(3):
        for j in range(len(ys)-1):
            cx=(xs[i]+xs[i+1])/2;cyy=(ys[j]+ys[j+1])/2
            if any(h[0]<cx<h[1] and h[2]<cyy<h[3] for h in holes):continue
            quad((xs[i],ys[j],0),(xs[i+1],ys[j],0),(xs[i+1],ys[j+1],0),(xs[i],ys[j+1],0),(0,0,-1))
    for hx0,hx1,hy0,hy1 in holes:
        quad((hx0,hy0,0),(hx0,hy1,0),(hx0,hy1,rz),(hx0,hy0,rz),(1,0,0))
        quad((hx1,hy0,0),(hx1,hy1,0),(hx1,hy1,rz),(hx1,hy0,rz),(-1,0,0))
        quad((hx0,hy0,0),(hx1,hy0,0),(hx1,hy0,rz),(hx0,hy0,rz),(0,1,0))
        quad((hx0,hy1,0),(hx1,hy1,0),(hx1,hy1,rz),(hx0,hy1,rz),(0,-1,0))
    quad((-x1,FRAME_IN_Y[0]-.003,rz),(x1,FRAME_IN_Y[0]-.003,rz),(x1,FRAME_IN_Y[1]+.003,rz),(-x1,FRAME_IN_Y[1]+.003,rz),(0,0,1))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o)
    uvmap(o,k);pieces.append(o);return o
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
Mm=.001
# Frame opening: sides unchanged (x +-1.128); rails 29 mm (was 32) so the rim beside each UI hole is wider than 30 mm.
FRAME_IN_X=1.128;FRAME_IN_Y=(.839,1.901);TRAY_REAR=.07
# KH_PhotoGalleryEditor.Place.cs canvas layout (mm from the canvas centre): HeaderPlate and Paper. Keep in step.
CANVAS_HOLES=(((0,410),(2160,180)),((0,-110),(2160,780)))
survey=json.loads((ROOT/'art-source/prop-finish/config.json').read_text(encoding='utf8'))
for part in survey['parts']:
    if not part['path'].startswith('KH_PhotoBoard/Structure/'):continue
    name=part['path'].split('/')[-1];p=part['localPosition'];s=part['localScale'];a=part['localEuler']['x']
    # BUG-ENV-005: no two faces may be parallel, same-facing and overlapping within 30 mm (Quest depth precision).
    # Roof planks and the ridge use single 45-degree chamfers: plank faces then sit at 25 (mod 45) degrees and the
    # ridge's at 0 (mod 45), so no plank face is near-parallel to a ridge face where they interpenetrate.
    if name.startswith('Roof'):
        # Three long overlapping-looking planks along each roof slope, inside the old envelope. The top plank stops
        # 7 mm short of the apex so the two slopes' top planks no longer cross (their end faces were coplanar there);
        # the ridge covers the meeting line.
        for i in range(3):
            dz=(-1+i)*s['z']/3;ang=math.radians(a);trim=.007 if i!=1 and -math.sin(ang)*dz>0 else 0
            dz-=math.copysign(trim/2,dz)
            box(name+str(i),(p['x'],p['y']-math.sin(ang)*dz,p['z']+math.cos(ang)*dz),(s['x'],s['y'],s['z']/3-.0015-trim),bevel=.002,angle=a,segments=1)
    elif name=='Ridge':
        # Runs 40 mm past the plank ends on each side (was flush), so the ridge and plank end faces are not coplanar
        # and the ridge still covers the slopes' meeting line to the gable ends.
        box(name,(p['x'],p['y'],p['z']),(s['x']+.08,s['y'],s['z']),bevel=.003,angle=a,segments=1)
    elif name=='Back':
        # A tray inside the frame instead of a box (a box left its front 17 mm behind the frame front, its rims
        # 0-2 mm under the frame's, and the canvas 10-16 mm in front of it). Back's y is the canvas centre.
        tray(name,p['y'])
    else:box(name,(p['x'],p['y'],p['z']),(s['x'],s['y'],s['z']),bevel=.005 if name.startswith('Post') else .003,angle=a)
# Narrow lip sits outside the 2.24 x 1.04 m canvas; the photos and pager remain clear. The frame spans the full
# depth from its old front (z -.007) to the tray's rear (TRAY_REAR) on the old outline (x +-1.16, y .81-1.93);
# rails butt between the sides, so no frame face lies on or just behind another.
fz=(TRAY_REAR-.007)/2;fd=TRAY_REAR+.007
for x in (-1,1):box('Frame side',(x*(FRAME_IN_X+.016),1.37,fz),(.032,1.12,fd))
for y0,y1 in ((.81,FRAME_IN_Y[0]),(FRAME_IN_Y[1],1.93)):box('Frame rail',(0,(y0+y1)/2,fz),(2*FRAME_IN_X,y1-y0,fd))
for x in (-1.22,1.22):
    for y0 in (.9,1.86,2.07):domed_bolt(x,y0,-.022,.012)
finish('PhotoFrame',4000)
# Shampoo: smooth shoulder, bottom moulding, cap collar, slanted hollow outlet.
# BUG-ENV-005: the printed label is a UV band (rings 4-5, h -.054 to .027) of the body surface itself, not a
# second 0.3 mm shell; phase 8 keeps the label's old quarter-turn orientation. The body's top disk is dropped
# because the cap covers it (it sat 19 mm under the cap's top disk).
lathe('Bottle body',[(-.085,.024),(-.083,.028),(-.079,.03),(-.074,.03),(-.054,.03),(.027,.03),(.05,.03),(.059,.029),(.066,.0265),(.072,.020),(.075,.014)],2,32,caps=(True,False),label_band=(4,5,8,3))
lathe('Cap',[ (.073,.014),(.075,.015),(.091,.015),(.094,.013)],3,32)
direction=(0,math.cos(math.pi/6),math.sin(math.pi/6))
lathe('Outlet',[(0,.005),(.003,.006),(.028,.006),(.03,.005),(.03,.0034),(.024,.0034)],3,20,(0,.092,0),direction)
finish('Bottle',2000)
for o in models:o.hide_set(False)
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'PropFinish.blend'))
(REPORT/'geometry.json').write_text(json.dumps(reports,indent=2),encoding='utf8')
print('PROP_RESULT '+json.dumps(reports))
