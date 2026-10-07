import fs from 'node:fs';
import path from 'node:path';
const root=process.cwd();
const dest=path.join(root,'artifacts/hillside-introduction/001');
const reference='C:/work/unity/vcc/KominkaDogRun/artifacts/kominka-introduction/004';
for(const d of ['assets','output','qa'])fs.mkdirSync(path.join(dest,d),{recursive:true});
const assets={
 'cover.png':'docs/development/evidence/2026-09-20-leash-production/Shiba-front.png',
 'leash.png':'docs/plans/evidence/2026-10-04-prop-finish/rack-after.png',
 'discs.png':'docs/plans/evidence/2026-10-04-matte-materials/after-discs.png',
 'river.png':'docs/plans/evidence/2026-10-04-river-verge/water-access-adult-after.png',
 'wash.png':'docs/plans/evidence/2026-10-04-prop-finish/tools-after.png',
 'view.png':'docs/plans/evidence/2026-10-04-roadside/farmhouse-view-adult-after.png',
 'rest.png':'docs/plans/evidence/2026-10-04-roadside/river-rest-adult-after.png',
 'night.png':'docs/plans/evidence/2026-10-04-roadside/viewpoint-adult-night.png',
 'map.png':'Assets/KominkaHillside/Textures/Map/KH_TravelMap.png',
};
for(const [name,src] of Object.entries(assets))fs.copyFileSync(path.join(root,src),path.join(dest,'assets',name));
for(const dog of ['corgi','pome','lab','dachs','shiba','samoyed'])fs.copyFileSync(path.join(reference,'assets',`dog-${dog}.png`),path.join(dest,'assets',`dog-${dog}.png`));
const original=fs.readFileSync(path.join(reference,'KominkaDogRun-slides.html'),'utf8');
let css=original.match(/<style>([\s\S]*?)<\/style>/)[1];
css+='\nhtml{-webkit-print-color-adjust:exact;print-color-adjust:exact}.cover h1{font-size:78px}.cover h2{font-size:48px}.cover>img{object-position:center}.pet-photo{object-position:center}.wide-photo{position:absolute;left:695px;top:185px;width:840px;height:525px;object-fit:cover}.photo-caption{position:absolute;left:695px;top:740px;width:840px;font-size:22px;color:#7a7c71}.catalog .grid{grid-template-columns:repeat(3,1fr);top:155px;gap:20px}.catalog .feature{height:140px}.catalog .feature h3{font-size:28px}.catalog .feature p{font-size:22px}.catalog .feature img{width:100px;height:105px}.catalog .note{bottom:72px}.camera-slide .pair img{height:350px}.camera-slide .pair{top:245px}.camera-slide .pair figcaption{font-size:27px}.camera-slide .deck-note{font-size:23px}.cover .edition{font-size:22px;margin-top:26px;color:#e6e5cf}';
const footer=n=>`<footer><span>VAnimal</span><span>${String(n).padStart(2,'0')} / 08</span></footer>`;
const split=(n,label,title,body,sub,img,alt,wide=false)=>`<section class="slide" aria-label="${n} ${label}"><div class="split-copy"><p class="label">${label}</p><h2>${title}</h2><p class="body">${body}</p><p class="sub">${sub}</p></div><img class="photo ${wide?'wide-photo':'pet-photo'}" src="assets/${img}" alt="${alt}">${footer(n)}</section>`;
const slides=[];
slides.push(`<section class="slide cover" aria-label="1 古民家の裏山"><img src="assets/cover.png" alt="秋の裏山の入口にいる柴犬と仲間たち"><div class="copy"><p class="label">古民家の裏山</p><h1>KominkaHillside</h1><h2>犬と散歩を楽しむVRChatワールド</h2><p class="edition">古民家ドッグランの6匹と、秋の山へ。</p></div>${footer(1)}</section>`);
slides.push(split(2,'犬とお散歩','リードをつないで、<br>一緒に歩けます','気に入った犬と、<br>山道や川沿いへ出かけられます。','片手に3匹、両手で6匹まで。<br>行き先は手元メニューで選べます。','leash.png','裏山の入口にあるリード置き場と犬たち'));
slides.push(split(3,'フリスビー・おもちゃ','投げると、<br>追いかけます','フリスビーを投げると、<br>犬が追いかけてキャッチ。<br>くわえて持ってきます。','ボールなどのおもちゃでも、<br>遊べます。','discs.png','6匹それぞれの顔が付いたフリスビー',true));
slides.push(split(4,'川遊び','川辺でも<br>遊べます','散歩の途中で、犬と水遊び。<br>小さな犬は泳ぎ、<br>大きな犬は浅瀬を歩きます。','川沿いの道と小さな橋を通って、<br>入口へ戻れる周回コースです。','river.png','裏山の水遊び場と川沿いの道',true));
slides.push(split(5,'散歩帰りのお手入れ','遊んだあとは、<br>きれいに','泡をつけて、スポンジで洗い、<br>シャワーですすげます。<br>仕上げはタオルで。','入口と道中に洗い場があります。<br>犬を呼び、台の高さを調整できます。','wash.png','洗い場に並ぶボトル、スポンジ、シャワー、タオル'));
slides.push(`<section class="slide live-slide" aria-label="6 景色と休憩"><img class="photo" src="assets/view.png" style="object-position:40% center" alt="山から見える古民家"><div class="copy"><p class="label">景色と休憩</p><h2>山から古民家を<br>眺められます</h2><p class="body">登ってきた道の先に、<br>犬たちと出発した古民家が見えます。</p><p class="body">ベンチのそばで休んだり、<br>犬を呼んで撫でたり。<br>自分のペースで過ごせます。</p><p class="live-note">古民家の室内へは、<br>入口の案内から前作へ遊びに行けます。</p></div>${footer(6)}</section>`);
slides.push(`<section class="slide accessories camera-slide" aria-label="7 自動写真"><p class="label">散歩の思い出</p><h2>散歩や触れ合いを、自動で写真に</h2><div class="pair"><figure><img src="assets/rest.png" alt="散歩で立ち寄れる川辺の休憩場所"><figcaption><h3>犬目線や、自撮り風の写真</h3><p>リードでつないで歩くと自動撮影。<br>撫でたり遊んだりした場面も残ります。</p></figcaption></figure><figure><img src="assets/night.png" alt="夜の見晴らしの休憩場所"><figcaption><h3>写真は手元で見返せます</h3><p>手元メニューや、入口の写真ボードで。<br>最大24枚を、新しい順に見られます。</p></figcaption></figure></div><p class="deck-note">写真は自分だけに表示され、退出すると消えます。掲載画像は散歩コースの風景です。</p>${footer(7)}</section>`);
const features=[
['dog-corgi.png','6犬種に会う','前作と同じ犬たち'],['leash.png','リードで散歩','片手3匹・両手6匹まで'],['discs.png','フリスビー','追いかけてキャッチ'],
['dog-pome.png','おもちゃで遊ぶ','ボールなどを取ってくる'],['river.png','水遊び','犬と川の浅瀬へ'],['wash.png','犬を洗う','泡・すすぎ・タオル'],
['dog-shiba.png','撫でる・呼ぶ','そばで触れ合う'],['dog-dachs.png','おやつ・飾り','おやつや着せ替え'],['rest.png','自動写真','散歩と触れ合いを撮影'],
['map.png','地図と移動','行き先を選んでワープ'],['view.png','景色と休憩','山から古民家を眺める'],['night.png','ナイトモード','夜の景色も楽しめる']];
slides.push(`<section class="slide catalog" aria-label="8 できること一覧"><h2>できること一覧</h2><div class="grid">${features.map(([img,h,p])=>`<article class="feature"><img src="assets/${img}" alt=""><div><h3>${h}</h3><p>${p}</p></div></article>`).join('')}</div><p class="note">一部の飾りなどは、Pointでの解放や商品の購入が必要です。</p>${footer(8)}</section>`);
const nav=original.slice(original.indexOf('<nav>'));
const html=`<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>古民家の裏山 — KominkaHillside</title><style>${css}</style></head><body><main>${slides.join('\n')}</main>${nav}`;
fs.writeFileSync(path.join(dest,'KominkaHillside-slides.html'),html);
fs.writeFileSync(path.join(dest,'qa','sources.json'),JSON.stringify({reference,assets,content:['docs/core-concept.md','docs/game-design.md','docs/development/overview.md'],date:'2026-10-04'},null,2));
console.log('Created 8 slides at '+dest);
