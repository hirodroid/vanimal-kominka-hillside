import fs from 'node:fs';
import path from 'node:path';
const base=path.resolve('artifacts/hillside-introduction');
const dest=path.join(base,'002');
for(const d of ['assets','output','qa'])fs.mkdirSync(path.join(dest,d),{recursive:true});
const source='C:/Users/simpl/Pictures/VRChat/2026-10/';
const photos={walk:'17-53-25.247',frisbee:'18-00-19.704',river:'17-55-48.065',pet:'17-57-35.617',wash:'18-02-01.866',gallery:'18-03-31.744'};
for(const [name,time] of Object.entries(photos))fs.copyFileSync(source+`VRChat_2026-10-04_${time}_1920x1080.png`,path.join(dest,'assets',name+'.png'));
for(const name of ['dog-corgi.png','dog-pome.png','dog-shiba.png','dog-dachs.png','map.png','view.png','night.png'])fs.copyFileSync(path.join(base,'001/assets',name),path.join(dest,'assets',name));
const old=fs.readFileSync(path.join(base,'001/KominkaHillside-slides.html'),'utf8');
let css=old.match(/<style>([\s\S]*?)<\/style>/)[1];
css+=`
.cover>img{filter:none}.cover:after{background:linear-gradient(180deg,rgba(14,29,22,.68),transparent 42%)}.cover .copy{top:45px;left:650px;right:55px}.cover h1{font-size:68px;margin-bottom:8px}.cover h2{font-size:34px}.cover .edition{margin-top:12px}.cover .label{margin-bottom:8px}
.panorama>.label{position:absolute;left:65px;top:35px;margin:0}.panorama>h2{position:absolute;left:65px;top:78px;font-size:52px}.panorama .pan-photo{position:absolute;left:65px;top:170px;width:1470px;height:570px;object-fit:cover;object-position:center 60%}.panorama .pan-copy{position:absolute;left:65px;top:769px;right:65px;font-size:28px;line-height:1.6;color:#576258}
.image-window{position:absolute;left:695px;top:95px;width:840px;height:680px;overflow:hidden}.image-window img{position:absolute;max-width:none;height:100%;width:auto}.frisbee-window img{height:175%;left:-625px;top:-345px}.wash-window img{left:-75px;top:0}.pet-window{left:65px;width:850px}.pet-window img{left:-40px}.pet-slide .copy{left:985px;right:65px;top:108px}.pet-slide h2{font-size:54px}.pet-slide .body{font-size:28px}.pet-slide .live-note{font-size:25px}
.gallery-slide .split-copy{width:470px}.gallery-slide h2{font-size:54px}.gallery-slide .body{font-size:29px}.gallery-slide .sub{font-size:25px}.gallery-photo{position:absolute;left:565px;top:153px;width:970px;height:615px;object-fit:cover;object-position:center}.gallery-slide .split-copy{top:108px}
.feature img{object-position:center}.feature.walk img{object-position:right center}.feature.frisbee img{object-position:50% 55%}.feature.wash img{object-position:25% center}.feature.pet img{object-position:48% center}
`;
const footer=n=>`<footer><span>VAnimal</span><span>${String(n).padStart(2,'0')} / 08</span></footer>`;
const photo=(name,alt,cls='')=>`<img class="${cls}" src="assets/${name}.png" alt="${alt}">`;
const slides=[];
slides.push(`<section class="slide cover" aria-label="1 古民家の裏山">${photo('walk','コーギーとリードでつながり、秋の裏山を散歩する')}<div class="copy"><p class="label">古民家の裏山</p><h1>KominkaHillside</h1><h2>犬と散歩を楽しむVRChatワールド</h2><p class="edition">古民家ドッグランの6匹と、秋の山へ。</p></div>${footer(1)}</section>`);
slides.push(`<section class="slide panorama" aria-label="2 犬とお散歩"><p class="label">犬とお散歩</p><h2>リードをつないで、一緒に歩けます</h2>${photo('walk','人とコーギーをつなぐリードと歩いている様子','pan-photo')}<p class="pan-copy">行き先は手元メニューで選べます。片手に3匹、両手で6匹まで連れて歩けます。</p>${footer(2)}</section>`);
slides.push(`<section class="slide" aria-label="3 フリスビー"><div class="split-copy"><p class="label">フリスビー・おもちゃ</p><h2>追いかけて、<br>キャッチ</h2><p class="body">投げたフリスビーを追いかけて、<br>犬がジャンプ。<br>くわえて持ってきます。</p><p class="sub">ボールなどのおもちゃでも、<br>遊べます。</p></div><div class="image-window frisbee-window">${photo('frisbee','フリスビーをくわえて宙に浮くダックスフンド')}</div>${footer(3)}</section>`);
slides.push(`<section class="slide panorama" aria-label="4 川遊び"><p class="label">川遊び</p><h2>散歩の途中で、犬と水遊び</h2>${photo('river','川を泳ぐ柴犬やポメラニアンと、岸辺の犬たち','pan-photo')}<p class="pan-copy">川に入って泳いだり、岸辺で過ごしたり。犬たちと川沿いの時間を楽しめます。</p>${footer(4)}</section>`);
slides.push(`<section class="slide" aria-label="5 犬を洗う"><div class="split-copy"><p class="label">散歩帰りのお手入れ</p><h2>泡で洗って、<br>きれいに</h2><p class="body">遊んで汚れた犬に泡をつけて、<br>手やスポンジで洗えます。</p><p class="sub">シャワーですすいで、<br>仕上げはタオルで。<br>入口と道中に洗い場があります。</p></div><div class="image-window wash-window">${photo('wash','泡だらけのコーギーを手で洗っている様子')}</div>${footer(5)}</section>`);
slides.push(`<section class="slide live-slide pet-slide" aria-label="6 犬との触れ合い"><div class="image-window pet-window">${photo('pet','木の下に座り、集まってきた犬たちを両手で撫でる')}</div><div class="copy"><p class="label">犬との触れ合い</p><h2>ひと休みして、<br>撫でる時間</h2><p class="body">犬をそばに呼んで、<br>座ったまま撫でられます。</p><p class="body">散歩の途中でも、<br>いつもの犬たちと<br>ゆっくり過ごせます。</p></div>${footer(6)}</section>`);
slides.push(`<section class="slide gallery-slide" aria-label="7 自動写真"><div class="split-copy"><p class="label">散歩の思い出</p><h2>遊んだ写真を、<br>手元で見返す</h2><p class="body">散歩や犬との触れ合いを<br>自動で撮影します。</p><p class="body">写真は手元メニューや<br>入口の写真ボードで<br>見られます。</p><p class="sub">写真は自分だけに表示され、<br>退出すると消えます。</p></div>${photo('gallery','散歩や洗浄、触れ合いの写真が並ぶ実際の手元ギャラリー','gallery-photo')}${footer(7)}</section>`);
const features=[
['dog-corgi','6犬種に会う','前作と同じ犬たち'],['walk','リードで散歩','片手3匹・両手6匹まで'],['frisbee','フリスビー','追いかけてキャッチ'],
['dog-pome','おもちゃで遊ぶ','ボールなどを取ってくる'],['river','水遊び','犬たちと川へ'],['wash','犬を洗う','泡・すすぎ・タオル'],
['pet','撫でる・呼ぶ','そばで触れ合う'],['dog-dachs','おやつ・飾り','おやつや着せ替え'],['gallery','自動写真','手元で思い出を見返す'],
['map','地図と移動','行き先を選んでワープ'],['view','景色と休憩','山から古民家を眺める'],['night','ナイトモード','夜の景色も楽しめる']];
slides.push(`<section class="slide catalog" aria-label="8 できること一覧"><h2>できること一覧</h2><div class="grid">${features.map(([img,h,p])=>`<article class="feature ${img}">${photo(img,'')}<div><h3>${h}</h3><p>${p}</p></div></article>`).join('')}</div><p class="note">一部の飾りなどは、Pointでの解放や商品の購入が必要です。</p>${footer(8)}</section>`);
fs.writeFileSync(path.join(dest,'KominkaHillside-slides.html'),`<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>古民家の裏山 — KominkaHillside</title><style>${css}</style></head><body><main>${slides.join('\n')}</main>${old.slice(old.indexOf('<nav>'))}`);
fs.copyFileSync(path.join(base,'001/qa/render-pdf.ps1'),path.join(dest,'qa/render-pdf.ps1'));
fs.writeFileSync(path.join(dest,'qa/sources.json'),JSON.stringify({photos:Object.fromEntries(Object.entries(photos).map(([k,v])=>[k,source+`VRChat_2026-10-04_${v}_1920x1080.png`])),reference:'../001/KominkaHillside-slides.html',notes:'User supplied six gameplay photos. Original files copied without pixel edits. CSS crops preserve activity subjects.'},null,2));
console.log(dest);
