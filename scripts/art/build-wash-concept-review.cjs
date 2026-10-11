// Rebuild the portable, offline review sheet for the 2026-10-11 concept images (hose, water pistol, pool).
// Images come from vrc-asset-lab/assets/wash-premium/design/2026-10-11 and are embedded so the page works as one file.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const lab = path.resolve(root, '../../vrc-asset-lab/assets/wash-premium/design/2026-10-11');
const out = path.join(root, 'artifacts/wash-concept-review/2026-10-11');
const image = f => 'data:image/png;base64,' + fs.readFileSync(path.join(lab, f)).toString('base64');
const items = [
  {
    id: 'Hose', name: 'ホース', category: '水', issue: 'IMP-DOG-049', image: image('hose-concept.png'),
    caption: '左が案 A (アイボリーのドラムのリール、まっすぐなノズル)、右が案 B (木の枠のリール、首の長いノズル)。',
    decided: [
      '押している間ずっと水が出る。射程 2.5 m (仮)',
      'リールは棚に固定し、手元のノズルまで線 (ホース) でつなぐ (Q9)',
      '洗う効きは無料のシャワーと同じ。違いは射程・見た目・音・犬の反応 (Q7)',
      '泡銃と同じシリーズの見た目 (アイボリーの胴・深緑の握り) (Q8)',
      '水を当てると、台の上の犬が水を追ってパクパク・前足で掻く (Q10・Q11)',
    ],
    ask: ['リールとノズルの形 (A か B か、混ぜるか)', '素材と色 (アイボリーと木の配分、ホースの色)', '大きさ (ノズルは手に持って 20〜24 cm の想定)'],
  },
  {
    id: 'WaterPistol', name: '水鉄砲', category: '水', issue: 'IMP-DOG-049', image: image('water-pistol-concept.png'),
    caption: '左が案 A (横に水の窓、短い丸いノズル)、右が案 B (少し長い胴、上に木のポンプ、カップ形のノズル)。旧アヒル・魚・竹の形は使っていない。',
    decided: [
      '1 回押すごとに 1 発。水の玉が放物線で飛び、当たった所で弾ける。射程 4 m・0.4 秒に 1 発 (仮)',
      '洗う効きは無料のシャワーと同じ (Q7)',
      '泡銃と同じシリーズの見た目 (Q8)',
      '当たると、台の上の犬がその場で小さく跳ねて振り向く (Q10・Q11)',
      '持ち方は泡銃と同じ「Gun」(ノズルが肘から手の向き)',
    ],
    ask: ['胴とノズルの形 (A か B か、混ぜるか)', '素材と色 (木のポンプを入れるか)', '大きさ (手に持って 18〜22 cm の想定)', '「武器っぽさ」が出ていないか'],
  },
  {
    id: 'Pool', name: 'プール', category: '洗い場', issue: 'IMP-DOG-044', image: image('pool-concept.png'),
    caption: '左が案 A (木の板の外枠、丸い深緑の縁)、右が案 B (角の木の柱、帆布の壁、縫い付けた深緑の縁)。前側を低くする点と水位は画像では弱いので、モデルで決定どおりにする。',
    decided: [
      '今の台と同じ 1.3 × 0.7 m の角丸長方形。犬の立つ面の高さは変えず、縁だけが立ち上がる (縁は 12〜15 cm の仮値)',
      '人が洗う前側の縁は低くして、手と道具が届くようにする',
      '帆布 + 深緑の縁 + 木の外枠 (Q13)',
      '水は薄く透けて足先が見える。水位は足首より下 (Q14)',
      '中央の支柱 1 本で支える (ほかの台と同じ)。泳ぐ・浮く・水の物理は入れない',
      '犬ごとの台の大きさに合わせて一緒に拡縮する (IMP-ENV-013 Q2)',
    ],
    ask: ['外枠の作り (A の板か B の柱と帆布か)', '素材と色 (木と帆布の配分、縁の色)', '縁の高さの見え方 (高すぎる・低すぎる)'],
  },
];
const template = fs.readFileSync(path.join(__dirname, 'wash-concept-review.template.html'), 'utf8');
fs.mkdirSync(out, { recursive: true });
const footer = '企画画像：2026-10-11 生成 (Codex 内蔵の画像生成、既存の泡銃・じょうろ・檜の台の写真を参照)。ここでの回答は自動で Unity や Issue へ反映されません。';
function write(dir, list, page) {
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, 'index.html'), template.replace('/*ITEM_DATA*/', JSON.stringify(list)).replace('/*PAGE_DATA*/', JSON.stringify(page)));
  console.log(`Created ${list.length} review cards: ${path.relative(root, dir)}/index.html`);
}
write(out, items, {
  key: 'vanimal-wash-concept-review-20261011-v1', collection: 'wash-concept-2026-10-11', filePrefix: 'vanimal-wash-concept-',
  title: 'VAnimal｜新しいお手入れ道具の企画画像', heading: '新しいお手入れ道具の企画画像',
  sub: 'ホース・水鉄砲・プールの 3 品。各品 A / B の 2 案から選ぶか、直したいところを書いてください。', footer,
});

// Round 2 (2026-10-11): after the answer vanimal-wash-concept-2026-10-11T01-43-01-752Z.json
// (hose: nozzle from A, colors from B / water pistol: A / pool: A but round).
const round2 = [
  {
    id: 'Hose', name: 'ホース', category: '水', issue: 'IMP-DOG-049', image: image('hose-concept-r2.png'),
    choices: { A: '1 (A のドラム形を木の色に)', B: '2 (B の木の枠のまま)' },
    question: 'リールはどちらにしますか？',
    caption: '回答「色は B で、持ち手や先端は A」から。ノズルはどちらも A の形。「色は B」を 2 通りに読んだ: 1 は A の丸いドラム形のリールを B の木の色に、2 は B の木の枠のリールそのまま。',
    decided: ['ノズル (持ち手と先端) は A の形 (2026-10-11 回答)', '押している間ずっと水が出る。棚のリールから線でつなぐ (Q9)', '洗う効きは無料のシャワーと同じ (Q7)'],
    ask: ['リールの形 (1 のドラム形か 2 の枠か)', '木と深緑の配分'],
  },
  {
    id: 'Pool', name: 'プール', category: '洗い場', issue: 'IMP-DOG-044', image: image('pool-concept-r2.png'),
    choices: { A: '1 (円)', B: '2 (楕円)' },
    question: '形はどちらにしますか？',
    caption: '回答「A で、円形がいいかな」から。どちらも A の作り (木の板の外枠・丸い深緑の縁・帆布)。画像は縁が高めに見えるが、決定どおり 12〜15 cm・足首より下の水位にする。',
    decided: [
      'A の作り: 木の板の外枠、丸い深緑の縁、帆布 (2026-10-11 回答)',
      '水は薄く透けて足先が見える。足首より下 (Q14)。中央の支柱 1 本',
      '人が棚の側に立つ空き 0.5 m を残すため、円なら直径 0.95 m まで。楕円なら今の台と同じ 1.3 × 0.7 m',
      '犬ごとの台の大きさに合わせる (IMP-ENV-013): 円は縦横同じ倍率 (いちばん長い犬で決まる)、楕円は縦横別の倍率',
    ],
    ask: ['形 (1 の円か 2 の楕円か)', '円の場合: 縦横同じ倍率なので、細長い犬 (Lab・サモエド。入江の決め方で台の長さ 0.75〜0.77 m、幅 0.30〜0.37 m) では左右の余りが大きく、台が大きく見える。それでよいか'],
  },
];
write(path.join(out, 'round2'), round2, {
  key: 'vanimal-wash-concept-review-20261011-r2', collection: 'wash-concept-2026-10-11-r2', filePrefix: 'vanimal-wash-concept-r2-',
  title: 'VAnimal｜企画画像の確認 (2 回目)', heading: '企画画像の確認 (2 回目)',
  sub: '1 回目の回答から作り直したホースとプール。水鉄砲は案 A で決まり。', footer,
});
