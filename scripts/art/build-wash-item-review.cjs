// Rebuild the portable, offline review sheet from existing Unity item photos.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const out = path.join(root, 'artifacts/wash-item-review/2026-10-10');
const names = ['泡もこもこ銃','アヒルの水鉄砲','魚の水鉄砲','竹の水鉄砲','花のシャワー','角型シャワー','じょうろ','ドライヤー','柄タオル','肉球ミトン','大きなスポンジ','ブラシ','コロン','檜の洗い台','石の洗い台','タイルの洗い台','小さなアヒル'];
const ids = ['FoamCannon','DuckWaterGun','FishWaterGun','BambooWaterGun','FlowerShower','RainShower','WateringCan','Dryer','PatternTowel','PawMitt','LargeSponge','GroomingBrush','Cologne','HinokiBath','StoneBath','TileBath','FloatingDuck'];
const groups = [0,1,1,1,1,1,1,2,2,0,0,2,2,3,3,3,3];
const categories = ['泡','水','仕上げ','洗い場'];
const items = ids.map((id,i)=>({id,name:names[i],category:categories[groups[i]],image:'data:image/png;base64,'+fs.readFileSync(path.join(root,`Assets/KominkaHillside/Data/WashPremium/Photos/Item${i}.png`)).toString('base64')}));
items.push({id:'PoolCandidate',name:'プール（追加候補）',category:'洗い場',image:null});
const template = fs.readFileSync(path.join(__dirname,'wash-item-review.template.html'),'utf8');
fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'index.html'),template.replace('/*ITEM_DATA*/',JSON.stringify(items)));
console.log(`Created ${items.length} review cards: ${path.relative(root,out)}/index.html`);
