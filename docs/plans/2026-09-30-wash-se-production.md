# 洗浄 SE (仮) の制作票 (IMP-DOG-025 用、仕上げは IMP-DOG-028)

2026-09-30。犬を洗う試作 (IMP-DOG-025) で「操作と音の対応」を確かめるための **仮の音**。AI (Claude) が Node の標準機能だけで合成した。外部の音源・サンプル・音声素材・生成サービスは使っていない。仕上げの音は IMP-DOG-028 で作り直す。音の構成は [犬洗い設計 §1-8](2026-09-30-dog-wash-design.md) に従う。

## 作り方

- スクリプト: [`scripts/kh-gen-wash-se.js`](../../scripts/kh-gen-wash-se.js) (水音の [`kh-gen-water-se.js`](../../scripts/kh-gen-water-se.js) と同じ置き場)
- 実行: `node scripts/kh-gen-wash-se.js` (`Assets/KominkaHillside/Audio/Wash/` へ書き、読み直して計測値を表示する)
- 乱数: 固定シード (噴射 101 / 毛 202 / 泡 303 / 泡立て 404 / タオル 505 / ブルブル 606)。流し直しても同じファイル
- 合成: 帯域を絞ったノイズ (1 次フィルタ) + 減衰する正弦・短い帯域ノイズの粒のランダム配置 + ループ長に整数周期を収めた揺らぎ。仕上げに 6.5-7 kHz の低域通過で高域を抑える
- ループの継ぎ目: ノイズは末尾 0.2 s を先頭へ等パワーで交差。粒は循環バッファへ足して端で切れない。フィルタは循環で 2 周流して状態を連続させた
- 形式: mono 44,100 Hz 16 bit PCM

## ファイルと計測値 (書いた WAV を読み直した値)

| ファイル | 長さ | 内容 | ピーク | RMS | 継ぎ目の段差 | 8 kHz 超 (対全体 RMS) | サイズ |
|---|---|---|---|---|---|---|---|
| `KH_Wash_Spray_Loop.wav` | 4.00 s ループ | 噴射「シャー」 (1.5-6.5 kHz) | -6.1 dBFS | -20.0 dBFS | 0.069 | -23 dB | 352,844 B |
| `KH_Wash_ContactFur_Loop.wav` | 4.00 s ループ | 毛に当たる「パタパタ・ザー」 | -3.2 dBFS | -20.3 dBFS | 0.047 | -32 dB | 352,844 B |
| `KH_Wash_ContactFoam_Loop.wav` | 4.00 s ループ | 泡に当たる「シュワー・プシュー」 | -3.2 dBFS | -20.6 dBFS | 0.019 | -27 dB | 352,844 B |
| `KH_Wash_Scrub_Loop.wav` | 3.00 s ループ | 泡立て「クシュクシュ」 (3 Hz の強弱) | -3.2 dBFS | -20.2 dBFS | 0.051 | -30 dB | 264,644 B |
| `KH_Wash_Towel_Loop.wav` | 3.00 s ループ | タオル「サッサッ」 (約 2.7 Hz) | -3.7 dBFS | -20.0 dBFS | 0.023 | -36 dB | 264,644 B |
| `KH_Wash_Shake.wav` | 1.20 s 単発 | ブルブル (12 Hz の強弱と水滴) | -3.2 dBFS | -21.6 dBFS | 0 (両端フェード) | -30 dB | 105,884 B |

合計 1,693,704 B (予算 2 MB 以内)。継ぎ目の段差は末尾と先頭の標本の差 (満度 1.0)。ノイズ系の音は隣り合う標本の差が最大 0.15-0.33 あり、継ぎ目の段差はそれより小さい (クリックにならない見込み)。ピークは全て -3 dBFS 以下 (最大 -3.2)。

## 権利の確認

| 項目 | 内容 |
|---|---|
| 区分 | 自作 (AI 制作・合成)。[共通制作手順](../../.claude/skills/vrc-asset-production/references/source-rights.md) の区分に従う |
| 第三者の素材 | 含まない (外部のダウンロード・サンプル・録音・生成サービスは不使用) |
| 確認日 | 2026-09-30 (生成と計測のみ) |

## Unity への取り込みの案 (統括とビルダーが行う。AI サブエージェントは Unity を操作しない)

- 圧縮: Vorbis 品質 0.4 前後 (水音と同じ)、Load In Background。ループ 4 本は Load Type = Compressed In Memory、ブルブルは Decompress On Load でもよい (短い)。Force To Mono は既にモノ。Android / iOS も同設定 (`Course/87` の水音に合わせる)
- ループ 5 本は AudioSource の Loop、ブルブルは `PlayOneShot`。WAV のループ点は先頭と末尾で、Unity 側でトリムしない
- 3D: Spatial Blend 1、Logarithmic Rolloff、Min Distance 0.5-1 m、Max Distance 8-10 m 程度から。当たった位置へ置いたソースで鳴らす (設計 §1-8)。音量は個人音量の台帳へ登録する

## 未確認

- 実際に聞いた評価 (音の自然さ・操作との対応・音量バランス・ループのつなぎ目のクリック・耳に痛い成分) は未確認。AI は波形の数値だけを見ており、ユーザーが PCVR または Editor で聞いて採否を決める
- 対象端末 (Quest・iOS) での鳴り方と、Unity の圧縮後の音質・サイズは未確認
