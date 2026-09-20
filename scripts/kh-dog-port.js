// Freeze the source scene for inspection; never read uncommitted source scene changes.
const fs = require('fs'), path = require('path'), cp = require('child_process');
const source = 'C:/work/unity/vcc/KominkaDogRun';
const scratch = path.resolve('Temp/KH_DogPort');
fs.mkdirSync(scratch, { recursive: true });
function git(...args) { return cp.execFileSync('git', ['-c', `safe.directory=${source}`, '-C', source, ...args], {maxBuffer: 512*1024*1024}); }
const manifestPath = path.join(scratch, 'source.json');
const manifest = fs.existsSync(manifestPath) ? JSON.parse(fs.readFileSync(manifestPath)) : { source, sha: '503dde8ff702e8d9f4dd73b4a6473ede7dcfd638', tag:'export/kominka-hillside-v1' };
fs.writeFileSync(manifestPath, JSON.stringify(manifest,null,2));
const scenePath = path.join(scratch, 'source.unity');
if (!fs.existsSync(scenePath)) fs.writeFileSync(scenePath,git('show',`${manifest.sha}:Assets/Scenes/KominkaDogRun.unity`));
const blocks = new Map();
for (const b of fs.readFileSync(scenePath,'utf8').split(/(?=^--- !u!)/m)) {
  const m=b.match(/^--- !u!(\d+) &(\d+)/); if(m) blocks.set(m[2],{id:m[2],type:+m[1],text:b});
}
const dogs=[];
for(const b of blocks.values()) {
  if(b.type!==1) continue;
  const n=b.text.match(/^  m_Name: (.*)/m)?.[1];
  if(n && (/^KD_(Dog|PetSystem|FetchSystem|WanderSystem|StopAnchor|Ball|DogPack|WanderArea)(_|$)/.test(n))) dogs.push({id:b.id,name:n,components:[...b.text.matchAll(/component: \{fileID: (\d+)\}/g)].map(x=>x[1])});
}
fs.writeFileSync(path.join(scratch,'objects.json'),JSON.stringify(dogs,null,2));
const selected=new Set();
function add(id) {if(id && blocks.has(id)) selected.add(id);}
dogs.forEach(d=>{add(d.id);d.components.forEach(add);});
// Include the six original model instances and six original toy instances.
for(const b of blocks.values()) if(b.type===1001 && /value: KD_(Dog|Ball)_(Corgi|Pome|Lab|Dachs|Shiba|Samoyed)\s/.test(b.text)) add(b.id);
const essential=['dogRoot','dogAnimator','mouthAnchor','ballController','petController','fetchController','wanderController','areaCenter','pack','stopAnchor','heartParticles','petAudioSource','barkAudioSource','breathAudioSource'];
let changed=true;
while(changed) {
  const before=selected.size;
  for(const b of blocks.values()) {
    const pi=b.text.match(/^  m_PrefabInstance: \{fileID: (\d+)\}/m)?.[1];
    const parent=b.text.match(/^  m_Father: \{fileID: (\d+)\}/m)?.[1];
    const go=b.text.match(/^  m_GameObject: \{fileID: (\d+)\}/m)?.[1];
    if((pi&&selected.has(pi))||(parent&&selected.has(parent))||(go&&selected.has(go))) add(b.id);
    if(!selected.has(b.id)) continue;
    if(go)add(go);
    if(b.type===1) for(const m of b.text.matchAll(/component: \{fileID: (\d+)\}/g)) add(m[1]);
    if(pi)add(pi);
    for(const key of essential) {const m=b.text.match(new RegExp('^  '+key+': \\{fileID: (\\d+)\\}', 'm'));if(m)add(m[1]);}
  }
  changed=selected.size!==before;
}
// Keep internal references. Unselected scene services (sleep rooms, shop, stage) become null.
let reduced='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n';
for(const b of blocks.values()) if(selected.has(b.id)) {
  let text=b.text.replace(/\{fileID: (\d+)\}/g,(v,id)=>id==='0'||selected.has(id)?v:'{fileID: 0}');
  text=text.replace(/(  m_Children:\r?\n)((?:  - .*\r?\n)*)/g,(_,head,items)=>{
    const valid=items.split(/(?=  - )/).filter(x=>x&&!x.includes('{fileID: 0}')).join('');
    return valid?head+valid:'  m_Children: []\n';
  });
  // Pruned room/costume objects must not remain as null added-child records.
  text=text.replace(/(    m_Added(?:GameObjects|Components):\r?\n)((?:    - [\s\S]*?)(?=    m_|  m_))/g,(_,head,items)=>{
    const valid=items.split(/(?=    - targetCorrespondingSourceObject:)/).filter(x=>x&&!/addedObject: \{fileID: 0\}/.test(x)).join('');
    return valid?head+valid:head.trimEnd()+' []\n';
  });
  reduced+=text;
}
fs.writeFileSync(path.join(scratch,'dogs.unity'),reduced);
if(process.argv.includes('--stage-only')) {
  fs.writeFileSync('Assets/KominkaHillside/Imported/KH_DogPortSource.unity',reduced);
  console.log('Rebuilt isolated port scene: '+selected.size+' blocks');process.exit(0);
}
const guids=new Set([...reduced.matchAll(/guid: ([a-f0-9]{32})/g)].map(m=>m[1]));
const indexPath=path.join(scratch,'guid-index.json');
let index;
if(fs.existsSync(indexPath))index=JSON.parse(fs.readFileSync(indexPath));
else {
  index={};
  const metas=cp.execFileSync('rg',['--files','--hidden','--no-ignore','Assets','Packages','-g','*.meta'],{cwd:source,maxBuffer:32*1024*1024}).toString().trim().split(/\r?\n/);
  for(const m of metas){const g=fs.readFileSync(path.join(source,m),'utf8').match(/^guid: ([a-f0-9]{32})/m)?.[1];if(g)index[g]=m.replaceAll('\\','/').slice(0,-5);}
  fs.writeFileSync(indexPath,JSON.stringify(index));
}
const tracked=new Set(git('ls-tree','-r','--name-only',manifest.sha).toString().trim().split(/\r?\n/));
const files=new Map(),missing=[];
function readAsset(p){
  const cache=path.join(scratch,'snapshot',p);
  if(fs.existsSync(cache))return fs.readFileSync(cache);
  const buf=tracked.has(p)?git('show',`${manifest.sha}:${p}`):fs.readFileSync(path.join(source,p));
  fs.mkdirSync(path.dirname(cache),{recursive:true});fs.writeFileSync(cache,buf);return buf;
}
function include(p){
  if(files.has(p)||!fs.existsSync(path.join(source,p))||fs.statSync(path.join(source,p)).isDirectory())return;
  if(p.startsWith('Packages/'))return;
  const buf=readAsset(p); files.set(p,buf);
  for(const file of [buf,fs.existsSync(path.join(source,p+'.meta'))?readAsset(p+'.meta'):Buffer.alloc(0)]) {
    // YAML references also live in importer metadata; avoid interpreting binary model/audio bytes.
    const s=file.toString('utf8');
    if(s.startsWith('%YAML')||s.startsWith('fileFormatVersion:')) for(const m of s.matchAll(/guid: ([a-f0-9]{32})/g)) guids.add(m[1]);
  }
}
// Preserve the original runtime classes and their program assets, including partial declarations.
for(const p of tracked) if(/^Assets\/KominkaDogRun\/Scripts\/.*\.(cs|asset)$/.test(p)&&!p.includes('/Debug/'))include(p);
const done=new Set();
while([...guids].some(g=>!done.has(g)))for(const g of guids){if(done.has(g))continue;done.add(g);if(index[g])include(index[g]);else if(!/^0+$/.test(g))missing.push(g);}
const fileList=[...files.keys()];
fs.writeFileSync(path.join(scratch,'asset-inventory.json'),JSON.stringify({source:manifest,selectedBlocks:selected.size,sceneMB:Buffer.byteLength(reduced)/1048576,files:fileList,missing},null,2));
console.log(JSON.stringify({source:manifest,selectedBlocks:selected.size,sceneMB:Buffer.byteLength(reduced)/1048576,files:fileList.length,MB:[...files.values()].reduce((n,b)=>n+b.length,0)/1048576,externalRoots:[...new Set(fileList.filter(p=>!p.startsWith('Assets/KominkaDogRun/')).map(p=>p.split('/').slice(0,2).join('/')))],missing},null,2));
if(process.argv.includes('--copy')) {
  for(const [p,buf] of files){
    const target=p.replace(/^Assets\/KominkaDogRun\//,'Assets/KominkaHillside/Imported/KominkaDogRun/');
    fs.mkdirSync(path.dirname(target),{recursive:true});
    if(fs.existsSync(target)) {if(!fs.readFileSync(target).equals(buf))throw Error('Refuse overwrite: '+target);}
    else fs.writeFileSync(target,buf);
    if(fs.existsSync(path.join(source,p+'.meta'))&&!fs.existsSync(target+'.meta'))fs.writeFileSync(target+'.meta',readAsset(p+'.meta'));
  }
  fs.mkdirSync('Assets/KominkaHillside/Imported',{recursive:true});
  fs.writeFileSync('Assets/KominkaHillside/Imported/KH_DogPortSource.unity',reduced);
  fs.mkdirSync('docs/development/imports',{recursive:true});
  fs.copyFileSync(path.join(scratch,'asset-inventory.json'),'docs/development/imports/kominka-dog-files.json');
}
