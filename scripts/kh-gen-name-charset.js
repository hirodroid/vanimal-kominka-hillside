#!/usr/bin/env node
// 表示名の補助フォント (IMP-WLD-001 段階 4) の文字集合: node scripts/kh-gen-name-charset.js
// 出力: Assets/KominkaHillside/Font/KH_DisplayName_Charset.txt (UTF-8、1 行 1 区分: "区分<TAB>文字列")。
// Course/86 (KH_CourseTravelNameFontEditor) がこれを読んで Static アトラスへ焼く。シーンから参照しないのでビルドには入らない。
// 対象: ASCII / Latin-1 補助 / Latin 拡張 A / ひらがな / カタカナ (半角を含む) / 全角英数記号 / JIS 第 1 水準の漢字 2,965 字 / よく使う記号。
// 対象外 (□ で出る): 簡体字の多く・ハングル・絵文字・JIS 第 2 水準以降の漢字。
// JIS 第 1 水準は JIS X 0208 の 16〜47 区。Node の ICU の EUC-JP (区点 + 0xA0 の 2 バイト) で引くので、手で並べない。
'use strict';
const fs = require('fs');
const path = require('path');

const range = (a, b) => { let s = ''; for (let c = a; c <= b; c++) s += String.fromCodePoint(c); return s; };
const sections = [];
sections.push(['ascii', range(0x21, 0x7E)]);
sections.push(['latin1', range(0xA1, 0xFF).replace(/­/g, '')]);
sections.push(['latinExtA', range(0x100, 0x17F)]);
sections.push(['hiragana', range(0x3041, 0x3096) + range(0x309B, 0x309E)]);
sections.push(['katakana', range(0x30A1, 0x30FA) + range(0x30FC, 0x30FE) + range(0xFF61, 0xFF9F)]);
sections.push(['fullwidth', range(0xFF01, 0xFF5E)]);
const euc = new TextDecoder('euc-jp');
let jis1 = '';
for (let ku = 16; ku <= 47; ku++) for (let ten = 1; ten <= 94; ten++) {
  const s = euc.decode(new Uint8Array([0xA0 + ku, 0xA0 + ten]));
  if (s.length === 1 && s !== '�') jis1 += s;
}
if (jis1.length !== 2965) { console.error('JIS level 1 should have 2965 kanji, got ' + jis1.length + ' (Node without full ICU?)'); process.exit(1); }
sections.push(['jis1', jis1]);
sections.push(['symbols', '・ー〜～♪♫☆★♡♥♂♀※○●◎△▲▽▼□■◇◆→←↑↓〇々〆ヶ「」『』（）【】〔〕［］｛｝〈〉《》、。，．：；？！゛゜…‥‘’“”＋－±×÷＝≠＜＞≦≧∞∴℃＄￥％＃＆＊＠§☆彡ω∀ﾟ']);

// 区分をまたいだ重複は最初の区分に残す
const seen = new Set();
const lines = sections.map(([name, text]) => {
  let out = '';
  for (const ch of text) if (!seen.has(ch) && ch.trim().length > 0) { seen.add(ch); out += ch; }
  return name + '\t' + out;
});
const file = path.join(__dirname, '..', 'Assets', 'KominkaHillside', 'Font', 'KH_DisplayName_Charset.txt');
fs.writeFileSync(file, lines.join('\n') + '\n');
console.log('[kh-gen-name-charset] ' + seen.size + ' characters -> ' + path.relative(path.join(__dirname, '..'), file));
for (const l of lines) console.log('  ' + l.split('\t')[0] + ' ' + [...l.split('\t')[1]].length);
