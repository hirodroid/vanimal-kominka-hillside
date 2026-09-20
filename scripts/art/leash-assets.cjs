// Project-specific mesh design; Synaptic constructs, unwraps, validates and exports it in Blender.
// Run node scripts/art/leash-assets.cjs. Requests go to Temp; source parameters remain reviewable here.
// --replace-owned regenerates these nine KH_* meshes only; first confirm they belong to KH_Leash_Production.
// Default mode requires those names to be absent. Original scene objects are never deleted.
const fs=require('fs'),path=require('path'),zlib=require('zlib');
const root=path.resolve(__dirname,'../..').replaceAll('\\','/');
const out=root+'/Temp/KH_LeashRelease';fs.mkdirSync(out,{recursive:true});
const tex=root+'/Assets/KominkaHillside/Textures/Walking';
const model=root+'/Assets/KominkaHillside/Models/Walking';
const colors=[[38,118,129],[195,114,65],[82,114,173],[161,74,87],[124,100,157],[111,139,93]];
const crcTable=Array.from({length:256},(_,n)=>{for(let k=0;k<8;k++)n=n&1?0xedb88320^(n>>>1):n>>>1;return n>>>0;});
function png(file,pixel){const w=1024,h=1024,raw=Buffer.alloc(h*(1+w*4));for(let y=0;y<h;y++)for(let x=0;x<w;x++){let p=pixel(x,h-1-y),o=y*(w*4+1)+1+x*4;for(let c=0;c<4;c++)raw[o+c]=p[c];}function chunk(t,b){let tb=Buffer.from(t),n=Buffer.alloc(4);n.writeUInt32BE(b.length);let crc=0xffffffff;for(let v of Buffer.concat([tb,b]))crc=crcTable[(crc^v)&255]^(crc>>>8);let cb=Buffer.alloc(4);cb.writeUInt32BE((crc^0xffffffff)>>>0);return Buffer.concat([n,tb,b,cb]);}let hdr=Buffer.alloc(13);hdr.writeUInt32BE(w);hdr.writeUInt32BE(h,4);hdr[8]=8;hdr[9]=6;fs.writeFileSync(file,Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',hdr),chunk('IDAT',zlib.deflateSync(raw)),chunk('IEND',Buffer.alloc(0))]));}
// Six padded webbing lanes, with edge stitching. Upper lanes are brushed metal, dark grip and rope.
function heightAt(x,y){if(y<768){let v=y%128;return .30*Math.sin(x*Math.PI/3)*Math.cos(y*Math.PI/3)+((v<19||v>109)?-.5:0);}if(y>=944)return .4*Math.sin((x+y*3)*Math.PI/8);return .04*Math.sin(x);}
png(tex+'/KH_Leash_BaseColor.png',(x,y)=>{let c;if(y<768){let row=Math.floor(y/128),v=y%128;c=colors[row];let weave=1+.055*Math.sin(x*Math.PI/3)*Math.cos(y*Math.PI/3);c=c.map(n=>n*weave);if(v<16||v>111)c=c.map(n=>n*.54);if((v>=22&&v<=25||v>=102&&v<=105)&&x%16<10)c=[224,211,181];}else if(y<864)c=[181,187,177];else if(y<944)c=[43,48,46];else {let a=1+.08*Math.sin((x+3*y)*Math.PI/8);c=[76*a,125*a,125*a];}return [...c.map(n=>Math.round(n)),255];});
png(tex+'/KH_Leash_Normal.png',(x,y)=>{let dx=heightAt(x+1,y)-heightAt(x-1,y),dy=heightAt(x,y+1)-heightAt(x,y-1),l=Math.hypot(dx,dy,1);return [Math.round(128-127*dx/l),Math.round(128-127*dy/l),Math.round(128+127/l),255];});
png(tex+'/KH_Leash_Metallic.png',(x,y)=>y>=768&&y<864?[190,190,190,140]:[0,0,0,y<768?38:55]);
const add=(a,b)=>a.map((v,i)=>v+b[i]),mul=(a,s)=>a.map(v=>v*s);
const norm=a=>mul(a,1/(Math.hypot(...a)||1));
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
class Mesh{
 constructor(name){this.name=name;this.v=[];this.f=[];this.uv=[];}
 face(indices,uv){let fi=this.f.length;this.f.push(indices);indices.forEach((vi,i)=>this.uv.push({face_index:fi,vertex_index:vi,uv:uv[i]}));}
 // Unity coordinate space; export conversion is explicit and verified by Unity import.
 // Synaptic's Unity FBX exporter maps Blender +Y to Unity +Z (verified by the one-metre rope).
 vertex(p){this.v.push([p[0],p[2],p[1]]);return this.v.length-1;}
 sweep(points,sections,uvLane,closed=true){let rows=points.length,cols=sections[0].length,base=this.v.length;for(let i=0;i<rows;i++)for(let s of sections[i])this.vertex(add(points[i],s));for(let i=0;i<(closed?rows:rows-1);i++)for(let j=0;j<cols;j++){let k=(i+1)%rows,n=(j+1)%cols;let u0=i/(closed?rows:rows-1),u1=(i+1)/(closed?rows:rows-1);let v0=uvLane[0]+uvLane[1]*j/cols,v1=uvLane[0]+uvLane[1]*(j+1)/cols;this.face([base+i*cols+j,base+k*cols+j,base+k*cols+n,base+i*cols+n],[[u0,v0],[u1,v0],[u1,v1],[u0,v1]]);}if(!closed){this.face(Array.from({length:cols},(_,j)=>base+cols-1-j),Array(cols).fill([.5,uvLane[0]]));this.face(Array.from({length:cols},(_,j)=>base+(rows-1)*cols+j),Array(cols).fill([.5,uvLane[0]]));}}
 tube(points,r=.003,lane=[.78,.03],closed=true,sides=6){let sections=points.map((p,i)=>{let prev=points[Math.max(0,i-1)],next=points[Math.min(points.length-1,i+1)];if(closed){prev=points[(i-1+points.length)%points.length];next=points[(i+1)%points.length];}let t=norm(add(next,mul(prev,-1))),a=norm(cross(t,Math.abs(t[2])<.9?[0,0,1]:[0,1,0])),b=cross(t,a);return Array.from({length:sides},(_,j)=>add(mul(a,r*Math.cos(j*2*Math.PI/sides)),mul(b,r*Math.sin(j*2*Math.PI/sides))));});this.sweep(points,sections,lane,closed);}
 belt(rx,ry,width,thick,center=[0,0,0],lane=0,segments=32){const bevel=thick*.28;let profile=[[thick/2,-width/2+bevel],[thick/2,width/2-bevel],[thick/2-bevel,width/2],[-thick/2+bevel,width/2],[-thick/2,width/2-bevel],[-thick/2,-width/2+bevel],[-thick/2+bevel,-width/2],[thick/2-bevel,-width/2]];let pts=[],sec=[];for(let i=0;i<segments;i++){let a=i*2*Math.PI/segments;pts.push(add(center,[rx*Math.cos(a),ry*Math.sin(a),0]));sec.push(profile.map(([r,z])=>[r*Math.cos(a),r*Math.sin(a),z]));}let start=this.uv.length;this.sweep(pts,sec,[lane*.125+.005,.115]); // Map the visible broad face across its lane, including two stitch lines.
 for(let j=start;j<this.uv.length;j++){let vi=(this.uv[j].vertex_index)%8;let z=profile[vi][1];this.uv[j].uv[1]=lane*.125+.008+((z/width)+.5)*.109;}}
 rectLoop(cx,cy,z,w,h,r,lane=[.78,.03]){let points=[];let bevel=Math.min(w,h)*.25;for(let [sx,sy,a0]of[[1,1,0],[-1,1,90],[-1,-1,180],[1,-1,270]])for(let j=0;j<4;j++){let a=(a0+j*30)*Math.PI/180;points.push([cx+sx*(w/2-bevel)+Math.cos(a)*bevel,cy+sy*(h/2-bevel)+Math.sin(a)*bevel,z]);}this.tube(points,r,lane);}
}
const meshes=[];
const breeds=['Corgi','Pome','Lab','Dachs','Shiba','Samoyed'];
const radii=[[.090,.075],[.09,.08],[.100,.125],[.07,.065],[.09,.080],[.16,.16]];
const rise=[.025,.025,.025,.025,.025,.025];
for(let i=0;i<6;i++){let [rx,ry]=radii[i],m=new Mesh('KH_Collar_'+breeds[i]);m.belt(rx,ry,.032,.009,[0,0,0],i);
 // Collar closure on the front-right quarter; low raised buckle and contrasting keepers.
 let buckleStart=m.v.length;m.rectLoop(0,0,0,.040,.025,.0028);m.tube([[-.018,0,0],[.018,0,0]],.0023,[.78,.03],false);
 for(let k=buckleStart;k<m.v.length;k++){let [x,z,y]=m.v[k];m.v[k]=[rx+.010+z,x,y];}
 // Dorsal folded fabric tab supports a real closed ring, replacing the exposed prototype stick.
 let top=ry+rise[i],base=ry-.004;
 let tab=[[0,base,-.012],[0,top-.019,-.012],[0,top-.019,.009],[0,base,.009]];
 let sect=tab.map((p,j)=>{let t=norm(add(tab[(j+1)%4],mul(tab[(j+3)%4],-1))),n=[0,-t[2],t[1]];return [[-.009,-1],[.009,-1],[.009,1],[-.009,1]].map(([x,s])=>add([x,0,0],mul(n,s*.0015)));});
 m.sweep(tab,sect,[i*.125+.035,.05]);
 m.rectLoop(0,top,0,.032,.036,.0032);
 meshes.push(m);}
let snap=new Mesh('KH_LeashSnap');
// Spring clip, gate, thumb slider, swivel eye. Tip still points +Y for the existing pickup contract.
let hook=[];for(let i=0;i<18;i++){let a=(-48+i*276/17)*Math.PI/180;hook.push([.018*Math.cos(a),.025+.027*Math.sin(a),0]);}
snap.tube(hook,.0042,[.78,.03],false,8);snap.tube([hook[0],[.014,.011,0],hook.at(-1)],.0027,[.81,.02],false,6);
snap.rectLoop(.015,.010,.001,.012,.014,.0022,[.85,.025]);
snap.tube([[0,.009,0],[0,-.022,0]],.006,[.78,.03],false,8);
snap.rectLoop(0,-.034,0,.024,.022,.0032);
meshes.push(snap);
let handle=new Mesh('KH_LeashHandle');handle.belt(.034,.062,.018,.006,[0,0,0],0,28);handle.rectLoop(0,-.068,0,.020,.016,.003);
meshes.push(handle);
let rope=new Mesh('KH_LeashRope');rope.tube(Array.from({length:12},(_,i)=>[0,0,i/11]),.006,[.928,.066],false,8);meshes.push(rope);
const cmd=(tool,params)=>({tool,params});
fs.writeFileSync(out+'/setup.json',JSON.stringify([
cmd('create_collection',{name:'KH_Leash_Production',color_tag:'COLOR_05'}),
cmd('hide_collection',{name:'Collection',hide_viewport:true,hide_render:true}),
cmd('create_styled_material',{style:'principled',name:'KH_Leash_WovenMetal',color:[1,1,1],roughness:.72,metallic:.0,base_color_texture:tex+'/KH_Leash_BaseColor.png',normal_texture:tex+'/KH_Leash_Normal.png',metallic_texture:tex+'/KH_Leash_Metallic.png'})]));
for(let i=0;i<meshes.length;i++){let m=meshes[i];let cmds=[cmd('create_mesh_from_data',{name:m.name,vertices:m.v,faces:m.f,smooth:true}),cmd('smart_uv_project',{name:m.name,island_margin:.02}),cmd('set_uv_coords',{name:m.name,uv_changes:m.uv}),cmd('recalculate_normals',{name:m.name}),cmd('assign_material',{object:m.name,material:'KH_Leash_WovenMetal'}),cmd('move_to_collection',{name:m.name,collection:'KH_Leash_Production'}),cmd('get_mesh_stats',{name:m.name}),cmd('export_fbx_for_unity',{name:m.name,filepath:model+'/'+m.name+'.fbx',include_armature:false})];if(process.argv.includes('--replace-owned'))cmds.unshift(cmd('delete_object',{name:m.name,delete_data:true}));fs.writeFileSync(out+'/model-'+i+'.json',JSON.stringify(cmds));}
fs.writeFileSync(out+'/manifest.json',JSON.stringify(meshes.map(m=>({name:m.name,vertices:m.v.length,triangles:m.f.reduce((a,f)=>a+f.length-2,0),uvCorners:m.uv.length})),null,2));
console.log(fs.readFileSync(out+'/manifest.json','utf8'));
