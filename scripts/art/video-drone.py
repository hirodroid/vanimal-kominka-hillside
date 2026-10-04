"""Original drone. blender --background --factory-startup --python scripts/art/video-drone.py
Only the authorized FBX, evidence and production brief are written. No external inputs.
"""
import bpy, bmesh, math, json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/KominkaHillside/Models/VideoDrone'
EV=ROOT/'docs/plans/evidence/2026-10-04-video-drone'
for d in (OUT,EV): d.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
PALETTE={'body':(.72,.67,.55,1),'frame':(.075,.084,.080,1),'rotor':(.24,.26,.25,1),'orange':(.53,.225,.075,1),'lens':(.008,.013,.016,1)}
mats=[]
for name,c in PALETTE.items():
 m=bpy.data.materials.new(name); m.diffuse_color=c; mats.append(m)
def prepare(o,name,color):
 o.name=name; bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
 for m in mats:o.data.materials.append(m)
 for p in o.data.polygons:p.material_index=list(PALETTE).index(color)
 return o
def box(name,loc,size,color,bevel=0,angle=0):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.dimensions=size; o.rotation_euler.z=angle
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:
  m=o.modifiers.new('Soft body edges','BEVEL'); m.width=bevel; m.segments=2; bpy.ops.object.modifier_apply(modifier=m.name)
 return prepare(o,name,color)
def cyl(name,loc,r,depth,color,n):
 bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=r,depth=depth,end_fill_type='NGON',location=loc)
 return prepare(bpy.context.object,name,color)
body=box('KH_VideoDrone',(0,.069,.012),(.118,.128,.072),'body',.016)
pieces=[]
for x in (-.112,.112):
 for y in (-.008,.146):
  a=Vector((math.copysign(.042,x),.069+math.copysign(.039,y-.069),.014)); b=Vector((x,y,.014)); delta=b-a
  pieces.append(box('Short arm',(a+b)/2,(delta.length+.012,.021,.018),'frame',angle=math.atan2(delta.y,delta.x)))
  pieces.append(cyl('Motor',(x,y,.0355),.017,.063,'frame',8))
  pieces.append(cyl('Rotor disc',(x,y,.0685),.048,.005,'rotor',12))
# Continuous lens barrel/rim/recess. Blender front=-Y, optical face centre=(0,0,0).
vs=[]; fs=[]; cs=[]; n=16
for r,y in [(.029,.022),(.029,-.004),(.020,-.004),(.019,0)]:
 vs.extend([(r*math.cos(i*math.tau/n),y,r*math.sin(i*math.tau/n)) for i in range(n)])
for ring in range(3):
 for i in range(n):
  j=(i+1)%n; fs.append((ring*n+i,ring*n+j,(ring+1)*n+j,(ring+1)*n+i)); cs.append('orange' if ring<2 else 'lens')
fs.extend([tuple(reversed(range(n))),tuple(range(3*n,4*n))]); cs.extend(['orange','lens'])
me=bpy.data.meshes.new('Camera nose'); me.from_pydata(vs,[],fs); me.update()
nose=bpy.data.objects.new('Camera nose',me); bpy.context.collection.objects.link(nose)
for m in mats:me.materials.append(m)
for p,c in zip(me.polygons,cs):p.material_index=list(PALETTE).index(c)
bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(me); bm.free(); pieces.append(nose)
bpy.context.view_layer.objects.active=body
for piece in pieces:
 m=body.modifiers.new('Remove internal contact surfaces','BOOLEAN'); m.operation='UNION'; m.solver='EXACT'; m.object=piece
 bpy.ops.object.modifier_apply(modifier=m.name); bpy.data.objects.remove(piece,do_unlink=True)
bm=bmesh.new(); bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-7)
bmesh.ops.dissolve_limit(bm,angle_limit=1e-5,verts=list(bm.verts),edges=list(bm.edges),delimit={'MATERIAL'})
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(body.data); bm.free()
m=body.modifiers.new('Final triangles','TRIANGULATE'); bpy.ops.object.modifier_apply(modifier=m.name)
me=body.data; attr=me.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='CORNER')
for p in me.polygons:
 for li in p.loop_indices:attr.data[li].color=mats[p.material_index].diffuse_color
 p.material_index=0
me.materials.clear(); mat=bpy.data.materials.new('KH_VideoDrone_VertexColor'); mat.use_nodes=True
bsdf=mat.node_tree.nodes.get('Principled BSDF'); bsdf.inputs['Metallic'].default_value=0; bsdf.inputs['Roughness'].default_value=.82
node=mat.node_tree.nodes.new('ShaderNodeVertexColor'); node.layer_name='Color'; mat.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color']); me.materials.append(mat)
# Non-degenerate local triangle projection, intentional UV overlap (no textures).
uv=me.uv_layers.active or me.uv_layers.new(name='UVMap')
for p in me.polygons:
 pts=[me.vertices[i].co for i in p.vertices]; u=(pts[1]-pts[0]).normalized(); v=p.normal.cross(u).normalized()
 xy=[Vector(((q-pts[0]).dot(u),(q-pts[0]).dot(v))) for q in pts]; lo=Vector((min(q.x for q in xy),min(q.y for q in xy)))
 span=max(max(q.x for q in xy)-lo.x,max(q.y for q in xy)-lo.y)
 for li,q in zip(p.loop_indices,xy):uv.data[li].uv=(q-lo)*(.9/span)+Vector((.05,.05))
def inspect(ob):
 me=ob.data; me.calc_loop_triangles(); ts=list(me.loop_triangles); uv=me.uv_layers.active.data
 bad_uv=bad_geo=dup=0; seen=set()
 for t in ts:
  a,b,c=[uv[i].uv for i in t.loops]; bad_uv+=abs((b-a).cross(c-a))<1e-12; bad_geo+=t.area<1e-12
  key=tuple(sorted(tuple(round(v,8) for v in me.vertices[i].co) for i in t.vertices)); dup+=key in seen; seen.add(key)
 bm=bmesh.new(); bm.from_mesh(me); non=sum(not e.is_manifold for e in bm.edges); flip=sum(e.is_manifold and not e.is_contiguous for e in bm.edges); vol=bm.calc_volume(signed=True); bm.free()
 tree=BVHTree.FromPolygons([v.co for v in me.vertices],[t.vertices for t in ts],all_triangles=True,epsilon=1e-8)
 crossing=[(a,b) for a,b in tree.overlap(tree) if a<b and not set(ts[a].vertices)&set(ts[b].vertices)]
 bounds=[[min((ob.matrix_world@v.co)[i] for v in me.vertices),max((ob.matrix_world@v.co)[i] for v in me.vertices)] for i in range(3)]
 return dict(triangles=len(ts),vertices=len(me.vertices),meshCount=1,materialCount=len(me.materials),dimensionsBlenderM=[hi-lo for lo,hi in bounds],degenerateUVTriangles=int(bad_uv),degenerateGeometryTriangles=int(bad_geo),duplicateFaces=int(dup),nonManifoldEdges=non,inconsistentWindingEdges=flip,signedVolumeM3=vol,nonAdjacentTriangleIntersections=len(crossing),uvBounds=[min(v.uv[i] for v in uv) for i in range(2)]+[max(v.uv[i] for v in uv) for i in range(2)],vertexColorLayers=[a.name for a in me.color_attributes])
report=inspect(body)
print('PRE_EXPORT '+json.dumps(report))
assert report['triangles']<=700,report
CHECKS=('degenerateUVTriangles','degenerateGeometryTriangles','duplicateFaces','nonManifoldEdges','inconsistentWindingEdges','nonAdjacentTriangleIntersections')
for key in CHECKS:assert report[key]==0,(key,report)
assert report['signedVolumeM3']>0
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active=body
fbx=OUT/'KH_VideoDrone.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',global_scale=1,bake_space_transform=True,path_mode='AUTO',colors_type='LINEAR')
# Inspect saved FBX coordinates independently of the roundtrip importer.
from io_scene_fbx import parse_fbx
from bpy_extras.io_utils import axis_conversion
fbx_tree, _ = parse_fbx.parse(str(fbx))
objects=next(e for e in fbx_tree.elems if e.id==b'Objects')
geometries=[e for e in objects.elems if e.id==b'Geometry']
assert len(geometries)==1
raw=next(e for e in geometries[0].elems if e.id==b'Vertices').props[0]
raw_points=[tuple(raw[i:i+3]) for i in range(0,len(raw),3)]
conversion=axis_conversion(to_forward='-Z',to_up='Y').to_4x4()
assert (conversion.to_3x3()@Vector((0,-1,0))-Vector((0,0,1))).length<1e-7
assert sorted(tuple(round(v,6) for v in p) for p in raw_points)==sorted(tuple(round(v,6) for v in conversion@p.co) for p in body.data.vertices)
# Locate the actual black front face; do not rely only on the design constant.
optical=[]
for p in body.data.polygons:
 if p.normal.y < -.99 and all(abs(body.data.vertices[i].co.y)<1e-7 for i in p.vertices):
  optical.extend(body.data.vertices[i].co.copy() for i in p.vertices)
assert optical
optical_center=[(min(v[i] for v in optical)+max(v[i] for v in optical))/2 for i in range(3)]
assert Vector(optical_center).length<1e-7
report['fbxStoredVertexCoordinatesMatchAxisConversion']=True
report['measuredOpticalFaceCenterBlenderM']=optical_center
bpy.ops.import_scene.fbx(filepath=str(fbx),colors_type='LINEAR')
imported=[o for o in bpy.context.selected_objects if o.type=='MESH']; assert len(imported)==1
copy=imported[0]; rt=inspect(copy)
assert rt['triangles']==report['triangles']
assert max(abs(a-b) for a,b in zip(rt['dimensionsBlenderM'],report['dimensionsBlenderM']))<1e-6
for key in CHECKS:assert rt[key]==0,(key,rt)
assert sorted(tuple(round(v,5) for v in c.color) for c in attr.data)==sorted(tuple(round(v,5) for v in c.color) for c in copy.data.color_attributes['Color'].data)
bpy.data.objects.remove(copy,do_unlink=True)
report.update(blenderVersion=bpy.app.version_string,executionDate='2026-10-04',dimensionsUnityM={'widthX':report['dimensionsBlenderM'][0],'heightY':report['dimensionsBlenderM'][2],'depthZ':report['dimensionsBlenderM'][1]},originBlenderM=list(body.location),lensOpticalFaceCenterBlenderM=[0,0,0],originUnityM=[0,0,0],lensOpticalFaceCenterUnityM=[0,0,0],frontAxisBlender='-Y',upAxisBlender='+Z',frontAxisUnity='+Z',upAxisUnity='+Y',exportAxes={'forward':'-Z','up':'Y','bakeSpaceTransform':True,'unitScaleMeters':1},transforms={'location':list(body.location),'rotationEuler':list(body.rotation_euler),'scale':list(body.scale)},colorSpace='LINEAR',paletteLinearRGBA=PALETTE,fbxRoundtrip=rt,fbxRoundtripColorsMatch=True,unityIntegration='NOT PERFORMED',visibilityAt3mInWorld='NOT VERIFIED',sourceRecipe='scripts/art/video-drone.py')
# Neutral studio evidence. The cameras and lights are not exported.
scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.use_denoising=True
scene.render.resolution_x=scene.render.resolution_y=600; scene.render.resolution_percentage=100
scene.world.use_nodes=True; scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.65,.65,1); scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
scene.view_settings.view_transform='Standard'
for name,loc,power,size in [('Key',(.25,-.3,.5),2,.45),('Fill',(-.3,-.1,.25),1,.4)]:
 data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
 light=bpy.data.objects.new(name,data); scene.collection.objects.link(light); light.location=loc; light.rotation_euler=(Vector((0,.06,.02))-light.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('Evidence'); cam=bpy.data.objects.new('Evidence',data); scene.collection.objects.link(cam); scene.camera=cam; data.type='ORTHO'; data.ortho_scale=.39
at=Vector((0,.069,.018)); views={'front':(0,-.7,.21),'top':(0,.069,.8),'side':(.75,.069,.13)}
for name,loc in views.items():
 cam.location=loc; cam.rotation_euler=(at-cam.location).to_track_quat('-Z','Y').to_euler(); scene.render.filepath=str(EV/f'preview-{name}.png'); bpy.ops.render.render(write_still=True)
report['preview']={'resolution':[600,600],'views':views,'target':list(at),'projection':'ORTHO','note':'Front and side slightly elevated; not an in-world distance test.'}
(EV/'validation.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf8')
brief=f'''# 小型撮影ドローン KH_VideoDrone

## 目的・用途と見た目
PROP / 撮影位置の目印・移動小物。撮影者と被写体だけに表示する仕様（表示制御は別担当）。[core-concept](../core-concept.md) §3（2026-09-22本制作基準、2026-09-27追記）に合わせ、生成り・濃灰・くすんだ橙、非金属・低光沢。丸い箱と短い腕、前だけに大きな橙の輪と黒いレンズを設ける。単体制作済み、ワールド統合未実施。

## 対象と予算
PC・Quest単体・iOS向け。距離0.5〜10m、3mで前が分かることを目標とする。指定上限700三角形、1メッシュ・1材質、テクスチャ・骨0。同時表示数は未定。実端末性能は未計測。

## 形状・寸法と作り方
幅{report['dimensionsBlenderM'][0]:.3f}m × 高さ{report['dimensionsBlenderM'][2]:.3f}m × 奥行{report['dimensionsBlenderM'][1]:.3f}m。Low-firstの箱・円筒・回転断面を生成し、胴の縁を2段面取り。Exact Booleanで統合して内部接触面を除去、三角化。原点は黒い光学面中心、橙のリムは4mm前へ突出。Blender -Y前/+Z上からFBX -Z forward/Y upでUnity +Z前/+Y上へ。1単位=1m、変換適用済み。Collider・LODなし、影を落とさない運用。

## 接続点・可動・適合
原点を動画カメラ位置へ接続。リグ・アニメーションなし。円盤は静止表示。追従・見える相手の制限は別担当。

## 材質・UV・テクスチャ
Color / FLOAT_COLOR / CORNERの頂点色、FBXはcolors_type=LINEAR。色値はvalidation.jsonに記録。1材質、Opaque、白のテクスチャ入力×頂点色、白Tint、Metallic=0、低いSmoothness（目安0.18）。Blenderノードはプレビュー用、Unity材質は別途作成。UV0は各三角形の直交投影を0.05〜0.95へ配置、意図的な重なりあり・退化なし。ライトマップUV・テクスチャ・ベイクなし。

## ファイルと引き渡し
原本は[生成スクリプト](../../scripts/art/video-drone.py)。`blender --background --factory-startup --python scripts/art/video-drone.py`でFBX・3画像・検査JSON・この制作票を再生成する。[FBX](../../Assets/KominkaHillside/Models/VideoDrone/KH_VideoDrone.fbx)、[正面](evidence/2026-10-04-video-drone/preview-front.png)、[上面](evidence/2026-10-04-video-drone/preview-top.png)、[側面](evidence/2026-10-04-video-drone/preview-side.png)。Unity取り込みはScale Factor=1、単位変換有効を出発点に実寸・+Z前・原点を確認。Normals Import、Generate Colliders/Lightmap UVs無効、Cast Shadows Off、不要ならRead/Write Off。`.meta`は制作しない。

## 参照元と権利
特定の商品や写真の参照なしの自作。一般的なクアッドコプターの構造とユーザー仕様から設計。購入素材・外部画像・画像生成サービス・外部送信なし。既存roadside-bench.pyとriver-verge.pyは生成・FBX出力の作法のみ参照。確認日2026-10-04。ロゴ・固有装飾なし。

## 仮決め・未確定
Unityでの統合確認は未実施。里山の昼/木陰/夜、0.5m・3m・10mの視認性、PC・Quest単体・iOSの実機、追従表示と撮影映像への写り込みは別担当が確認する。単体の拡大レンダーを3mの実機検証と見なさない。

## 確認結果
[validation.json](evidence/2026-10-04-video-drone/validation.json): {report['triangles']}三角形・{report['vertices']}頂点、1メッシュ・1材質。UV退化0、3D退化0、重複面0、非多様体辺0、不整合のある面方向0、非隣接三角形交差0、符号付き体積正。原点と光学面中心一致。保存FBXをBlenderへ再読込して面数・寸法・UV・形状検査・LINEAR頂点色一致を確認。Unityインポーターによる分割頂点数は未計測。画像は600角の単体レンダーで正面・側面はやや上方視点。

## 変更履歴
2026-10-04 / 初版 / scripts/art/video-drone.py、Blender {bpy.app.version_string}、独立background + factory-startup実行。形状・頂点色・FBX往復検査と証拠生成。Unity操作・git操作なし。
'''
(ROOT/'docs/plans/2026-10-04-video-drone-production.md').write_text(brief,encoding='utf8')
print('VIDEO_DRONE_RESULT '+json.dumps(report))



