---
id: IMP-DOG-055
type: IMP
system: DOG
status: todo
priority: Low
summary: 洗浄用の有料製品 (道具・台・仕上げ) のモデルと音声を見直してクオリティを上げる (Z ファイトの修正 BUG-DOG-013 とは別件)
next: "現状の一覧と優先順の案 (本文 §現状の一覧、2026-10-14) をユーザーが承認したら、1. 音 8 クリップ → 2. 洗い場の台とプール の順で着手。モデルはラボ 0.3.0 / 0.4.0 の元データ、音は scripts/art/wash-premium-audio.cjs"
updated: 2026-10-14
---

# IMP-DOG-055 洗浄用の製品のモデルと音声のクオリティアップ

## ユーザーの依頼 (2026-10-13、原文)

「あとは製品のモデル、製品というか、その洗浄用の製品はモデルと音声を見直し、クオリティアップっていうのは別で、ちょっとイシューを登録しておきたい。」

Z ファイトの修正 ([BUG-DOG-013](BUG-DOG-013-wash-premium-tool-zfight.md)) は不具合として別に扱う。こちらは**見た目と音の質そのもの**の見直し。範囲はまだ開いている (何をどこまで、はユーザーと決める)。

## いまの状態 (2026-10-13 に読んだ範囲)

- **モデル**: 有料の品は 20 品 (0.3.0 の 18 品 + 0.4.0 の水鉄砲・ホース・プール)。ラボ `C:/work/unity/vrc-asset-lab/assets/wash-premium/` で制作し、`WashPremium/10` (0.3.0) と `12` (0.4.0) で取り込んだ。Prefab は `Assets/KominkaHillside/Prefabs/WashPremium/`。材質は `PremiumWash_Tools` / `Fixture` / `Addons` (Standard) と透明 2 材質 (Fade、renderQueue 2990)。テクスチャは 1024 (Android ASTC 6x6)。三角形数と bounds は受領書 `Temp/WashPremium/import-*.txt` と `WashPremium/21` の検査値
- **音**: 泡・水・送風・ブラシ・コロン・出現・ボタンの音は `scripts/art/wash-premium-audio.cjs` で生成した **24 kHz モノラルの自作合成音** ([IMP-DOG-043](IMP-DOG-043-wash-premium-selection.md))。無料の道具の接触音は [IMP-DOG-028](IMP-DOG-028-wash-sound-and-reactions.md) と [洗いの SE 制作](../../plans/2026-09-30-wash-se-production.md)
- 企画は [DES-DOG-008](DES-DOG-008-wash-paid-tools-and-effects.md) (音は「泡の音・水の音の種類 (ASMR)」を採用)。実装の経緯は [IMP-DOG-043](IMP-DOG-043-wash-premium-selection.md) → [046](IMP-DOG-046-wash-tools-by-category.md) → [047](IMP-DOG-047-wash-item-review-changes.md) → [049](IMP-DOG-049-wash-water-tools-and-reactions.md)
- 制作の基準は `vrc-asset-production` Skill (分類 / 予算 / 形状 / UV・ベイク / 材質 / 検収)。音源の権利の記録は同 Skill の source-rights

## 範囲 (案。ユーザーと決める)

- 現状の一覧を作る: 品ごとに三角形数・テクスチャ・材質・音の種類と長さ。見劣りする点を挙げる (形の粗さ・UV の伸び・材質の質感・音の薄さ)
- 優先する品を決める (よく使う水鉄砲・泡銃・シャワー・台から)
- モデル: ラボの元データで形・UV・ベイクを直し、`vrc-asset-production` の検収を通して取り込み直す。0.3.0 の品は `WashPremium/10` を再実行しない決まりなので、品ごとの差し替え手順を BUG-DOG-013 と共通にする
- 音: 合成音を録音・制作音源に置き換えるか、合成のまま質を上げるかを決める。権利の記録を残す。端末負荷 (粒子 64・音源の数) は据え置き
- 容量: 取り込み直しの後に `Build Size/7 → 8 → 2` で Android の増分を測る ([ビルドサイズ実測](../../plans/2026-09-24-build-size-audit.md)。2026-10-13 時点 71.26 MB)

やらないこと: 品の追加・削除 (企画 DES-DOG-008)。握り・噴出口のアンカーの変更 (受領書の角度 1° 以内を保つ)。Z ファイトの修正 (BUG-DOG-013)。

## 現状の一覧 (2026-10-14、統括が Unity の Edit で計測)

Prefab (`Assets/KominkaHillside/Prefabs/WashPremium/`) の三角形数・材質・テクスチャと、音 (`Data/WashPremium/Audio/`、`scripts/art/wash-premium-audio.cjs` の合成音、全部 24 kHz モノラル) の割り当て (`KH_WashSelectionEditor.Build.cs` / `Water.cs`)。見た目の粗さ・UV の伸びは目視が要るので、この表は数値だけ (Edit 計測。Play・実機の見え方は未確認)。

| 分類 | 品 | 三角形 | 材質 (テクスチャ) | 使用音 (長さ) | 用途の頻度 |
|---|---|---|---|---|---|
| 泡 | FoamCannon 泡もこもこ銃 | 1,252 | Tools (BaseColor 2048) | Foam 2.0 s ループ + SpraySound WaterFlow | 高 (泡の主役) |
| 泡 | PawMitt 肉球ミトン | 1,012 | Tools | Brush 0.24 s | 中 |
| 水 | DuckWaterGun / FishWaterGun / BambooWaterGun | 1,356 / 1,348 / 1,364 | Tools | WaterJet 0.30 s | 高 |
| 水 | FlowerShower / RainShower | 1,388 / 764 | Tools | WaterFlow 2.0 s | 中 |
| 水 | WateringCan じょうろ | 976 | Tools | WaterFlow | 中 |
| 水 (0.4.0) | WaterPistol 水鉄砲 | 1,008 | Tools + Window (透明) | WaterJet | 高 |
| 水 (0.4.0) | Hose ホース | 1,856 | Tools + Addons (1024) | WaterJet | 高 |
| 仕上げ | Dryer ドライヤー | 1,348 | Tools | Air 2.0 s ループ | 高 |
| 仕上げ | PatternTowel 柄タオル | 2,240 | Tools | Brush | 中 |
| 仕上げ | LargeSponge / GroomingBrush | 620 / 1,344 | Tools | Brush | 低〜中 |
| 仕上げ | Cologne コロン | 524 | Tools | Cologne 0.35 s | 低 |
| 洗い場 | HinokiBath / StoneBath / TileBath (1.3 × 0.7 m) | 1,064 / 1,512 / 540 | Fixture (BaseColor 1024) | (なし。台の昇降は共通) | 高 (視界の中心) |
| 洗い場 (0.4.0) | PoolBath プール | 2,358 (4 メッシュ、水面は Water 材質) | Addons + Water | (なし) | 高 |
| 洗い場 | FloatingDuck | 1,000 | Tools | (なし) | 低 |
| 洗い場 | TelescopicSupport 台 | 300 | Fixture | (なし) | 常時 |
| 共通 | — | — | — | Appear 0.45 s (出現)、Click 0.07 s (ボタン) | 常時 |

- 合計 20 品 + 見本 PremiumWashPreview (19,652 tris、18 メッシュ。売り場の見本用)。
- テクスチャ: PremiumTools_BaseColor は Edit (PC) で 2048 × 2048 (上の「1024 (Android ASTC 6x6)」は Android の上書き値と思われる。Build Size/7 で実値を確かめる)。Fixture / Addons は 1024。
- 音は 8 クリップ全部がノイズ合成 (ノイズ + 包絡線) で、同じクリップを複数の品が共有する (WaterJet を 5 品、Brush を 4 品、WaterFlow を 3 品 + 全スプレーの SpraySound)。品ごとの音の違いが無く、いちばん「薄い」と感じやすい所。

### 優先順の案 (ユーザー承認待ち)

1. **音 (8 クリップ)**: 容量・負荷を変えずに体感が一番変わる。水系 3 クリップ (WaterJet / WaterFlow / Foam) と Air を先に、品ごとの差 (銃 3 本の音色差、タオルとブラシの差) を付ける。合成のまま質を上げる (倍音・複数層・44.1 kHz) か、録音・制作音源に置き換えるかは権利の記録 (source-rights) を決めてから。
2. **洗い場の台 3 種とプール**: 視界の中心で大きい (1.3 m)。1024 テクスチャで木目・石目が粗く見えるなら、まずここ。
3. **水鉄砲 3 本 + 泡銃 + ドライヤー**: 手に持つので近くで見る。形の粗さは目視が要る。
4. 残り (タオル・ミトン・スポンジ・ブラシ・コロン・じょうろ・シャワー・アヒル) は後回し。

## 完了条件 (範囲が決まってから詰める)

- [ ] 見直す品と内容の一覧を決め、ユーザーが承認した
- [ ] 取り込み直しの後、`WashPremium/21`・`44`、`Wash/12` PASS、アンカーの角度 1° 以内
- [ ] 音源の権利の記録を残した
- [ ] ビルドサイズの増分を記録した
- [ ] ユーザーの PCVR で、見直した品の見た目と音を確認した。Quest でも確認した

## 記録

2026-10-13: 起票 (ユーザー依頼「別で Issue を登録しておきたい」)。優先度 Low は AI の仮置き (不具合ではなく質の向上。範囲が未定)。実装は未着手。
2026-10-14: 統括 (Fable) が現状の一覧 (三角形数・材質・テクスチャ・音の割り当て) を Edit の計測で作り、優先順の案を書いた (上の節)。ユーザーの承認待ち。モデル・音の変更は未着手。
