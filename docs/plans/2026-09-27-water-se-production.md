# 水音 SE の制作票 (IMP-DOG-020 段階 7)

2026-09-27。プロジェクト内にも購入アセットにも水音が無かったため、AI (Claude) が Node の標準機能だけで合成した。外部の音源・サンプル・音声素材・生成サービスは使っていない。

| 項目 | 内容 |
|---|---|
| 素材 | `Assets/KominkaHillside/Audio/Water/KH_WaterStep.wav` (足が入る「ぱしゃ」0.25 s)、`KH_WaterSplash.wav` (入水「ばしゃっ」0.5 s)、`KH_WaterPaddle.wav` (犬かき 1.2 s、0.4 s 周期 × 3 の継ぎ目なしのループ) |
| 作り方 | [`scripts/kh-gen-water-se.js`](../../scripts/kh-gen-water-se.js): 帯域を絞ったノイズの短い塊 (1 次フィルタ) + 上がっていく短いサイン (水滴) + 低い「どぼん」(下がっていくサイン)。乱数は固定のシードなので、流し直しても同じファイル |
| 形式 | mono 22,050 Hz 16 bit PCM。合計 86,126 byte (予算 200 KB)。Unity では Vorbis 品質 0.4・Load In Background (Android / iOS も同じ。`Course/87`) |
| 権利 | AI 制作 (合成)。第三者の素材を含まない。[共通制作手順](../../.claude/skills/vrc-asset-production/references/source-rights.md) の区分では「自作 (AI 制作)」 |
| 確認日 | 2026-09-27 (生成と容量のみ)。音の自然さ・音量・距離の減衰は未確認で、PCVR でユーザーが採否を決める |
| 使う場所 | `KD_DogWanderController.HillsideWater` が水遊び場で足音として鳴らす (`waterStepClip`)。大きい水音・犬かきは受ける欄が無く、取り込みだけ |
