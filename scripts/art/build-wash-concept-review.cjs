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
fs.writeFileSync(path.join(out, 'index.html'), template.replace('/*ITEM_DATA*/', JSON.stringify(items)));
console.log(`Created ${items.length} review cards: ${path.relative(root, out)}/index.html`);
