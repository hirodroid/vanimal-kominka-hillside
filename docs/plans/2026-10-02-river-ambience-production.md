# 川のせせらぎ音の制作票・素材記録 (IMP-ENV-006)

2026-10-02。Issue は [IMP-ENV-006](../development/issues/IMP-ENV-006-river-ambience.md)。素材記録の型は [source-rights-record](../../.claude/skills/vrc-asset-production/references/source-rights-record.md)、手順は [source-rights](../../.claude/skills/vrc-asset-production/references/source-rights.md)。
状態: **Unity 配置・構成検査済み (2026-10-07、§9)。** 公開保留は2026-10-03の回答で解除済み (§1)。音の聞こえ・ループの継ぎ目・端末は未確認。

## 1. 素材記録

```text
素材名 / 成果物パス・版 (hash):
  川のせせらぎ音「小川の水音 middle」
  Assets/KominkaHillside/Audio/Ambience/River/Firefly_audio_小川の水音_middle.wav
  1,536,770 bytes、SHA256 c440dbd630e1b884e29c936b980ae46b405a5b2941a408d3af1d4c9e0029e84a (2026-10-02 に照合済み。Issue 記載値と一致)
  同じ場所の big / little (各 1,536,770 bytes) は不採用の候補。取り込み・配置しない。
参照区分 / 選んだ参照元・入力素材:
  AI 生成物 (ユーザー提供)。参照元・入力素材: 不明 (ユーザーに確認。下記)。プロンプトで特定作品・実在の録音を指定したかも未確認。
予定用途:
  VRChat ワールド「古民家の裏山」の本番シーンへそのまま組み込み (ループ再生の環境音)。加工は取り込み設定 (mono 化・Vorbis 圧縮) のみ。
  商用か非商用か・公開先は公開前に確認 (本ワールドは基本無料。課金のある別機能とは音源を分けている)。
利用条件の根拠・該当条件 / 確認日 / クレジット:
  根拠: (1) ユーザー回答: (2026-10-02)「制作元は Adobe Firefly の生成物」、(2026-10-03)「Fireflyの無料プランだが、商用利用もOKなので問題なし。2026/10/2 小川のせせらぎ」。
  (2) 公式規約の調査 (エージェント、2026-10-03): [service-conditions の Adobe Firefly 節](../../.claude/skills/vrc-asset-production/references/service-conditions.md#adobe-firefly-サウンド生成--generate-sound-effectsadobe-製モデル)。Adobe 製モデルの効果音生成の出力は、機能紹介ページが「can be used to create royalty free sound effects for commercial use, provided you adhere to Adobe guidelines and the audio produced is developed using the commercially released version of Adobe Firefly」と述べ、Generative AI Product Specific Terms (2026-04-23 版) §1 は「The Input and Output are your Content」。クレジット表示の義務は規約に見当たらない。Content Credentials・透かしを除去・改変しないことが条件 (§3.1)。
  この調査で確認できなかったこと: 無料プランの商用利用を直接述べた公式文 (FAQ・ヘルプは 403)、2026-10-02 の生成時にベータ表示だったか (2025-07 のブログは「Generate Sound Effects (beta)」、2026-10-03 の機能紹介ページに「beta」の語なし)、「commercially released version」の意味。規約 3 本には、無料プラン・ベータの出力の商用利用を禁じる文は見当たらない。
  参考 (今回の根拠にはしない): 古民家ドッグランの吠え声・呼吸音の記録 (ユーザー確認、2026-08-17。古民家 docs/development/issues/archive/IMP-FET-005-ball-pickup-anticipation.md、docs/development/release-checklist.md §4) は、根拠がユーザー確認のみで規約の URL・版は記録されていない。そのため今回の根拠の前例にはならず、同じ条件と読み替えない (source-rights §1・§2)。
  クレジット表示: 不要 (規約に義務なし。2026-10-03 調査)。ただし Content Credentials の扱いは下の「公開保留の判断」。
生成ツール・機能・プラン / 生成日 / 条件の確認記録:
  ツール: Adobe Firefly。プラン: 無料プラン。生成日: 2026-10-02 (いずれもユーザー回答 2026-10-03)。機能: ファイル名 (Firefly_audio_) からは効果音生成と推定されるが、ユーザーは機能名を述べていない。ベータ表示だったか: 不明。
  条件の確認記録: [service-conditions の Adobe Firefly 節](../../.claude/skills/vrc-asset-production/references/service-conditions.md#adobe-firefly-サウンド生成--generate-sound-effectsadobe-製モデル) (2026-10-03)。
主要な指示・生成設定:
  プロンプト: 「小川のせせらぎ」(ユーザー回答 2026-10-03。ファイル名の「小川の水音」とは別の語)。その他の生成設定・生成画面の記録: 未確認 (「big / middle / little」は名称で設定の記録ではない)。
  ファイルに XMP チャンク (684 byte) あり (生成ツールのメタデータの可能性。内容は未解読)。
加工内容 / 元素材:
  元ファイルは変更しない。取り込み設定のみ (mono・Normalize 切・Vorbis 品質 0.7・Decompress On Load)。ループ継ぎ目の修正版は作っていない (§4)。
採用前確認 (担当・日付・対象版・方法・範囲・結果):
  聴取: 未実施 (AI は聴けない。ユーザーの Play で採否)。類似確認: 実在の特定作品の録音・音声を狙った形跡の有無はプロンプトが分かってから。
  数値の検査: 2026-10-02 Node (AI)。長さ 8.000 s・48 kHz・16 bit stereo・peak -11.4 dBFS・RMS -29.2 dBFS・L/R 相関 0.958・継ぎ目の段差 (§4)。
状態: **公開保留は解除 (2026-10-03)**。採用はユーザー決定済み (2026-10-02)、規約の根拠は記録済み (2026-10-03)、参照音声なし・Adobe 製モデルの効果音生成 (ユーザー回答 2026-10-03)。残るのは採用前の聴取での類似確認と、無料プランの商用利用を直接述べた公式文の未確認 (下の判断)。
```

### ユーザー回答 (2026-10-03) と、残る確認

回答済み: 1. ツール = Adobe Firefly、無料プラン、生成日 2026-10-02。2. 商用利用はユーザーの回答で OK。3. プロンプト = 「小川のせせらぎ」。
エージェントの規約調査は完了 (2026-10-03、[service-conditions の Adobe Firefly 節](../../.claude/skills/vrc-asset-production/references/service-conditions.md#adobe-firefly-サウンド生成--generate-sound-effectsadobe-製モデル))。

**残り (ユーザーへの質問。各 1 行で足りる)** — 2026-10-03 回答: 1 = 参照無し、2 = Adobe モデルの効果音生成 (ベータ表示の有無は未回答)。3・4 は未回答:
1. 参照音声・自分の声を入力したか (入力した場合、その音声の権利の確認が別に要る。規約 §2)。
2. 使った機能は「サウンド生成 (Generate Sound Effects)」で、Adobe 製モデルか (他社モデルを選んでいないか)。生成画面にベータ表示があったか (覚えていれば)。
3. big・middle・little は同じ生成のバリエーションか。
4. 聴取後: 気になる既存音源との類似の気づき (採用前確認)。
任意: 無料プランの商用利用の確認として、Firefly の FAQ・ヘルプ (エージェントは 403 で開けない) の該当文をユーザーがブラウザで確認して貼る。

元の質問 (2026-10-02):

1. Firefly のどの機能・どのプラン (無料/有料) で生成したか、生成日。
2. 生成時の規約で、この用途 (VRChat ワールドへの組み込み、公開。商用の可否) が許されるか。クレジット表示の要否。確認できる根拠 (規約のページ・契約画面・領収) の場所。
3. プロンプトと生成設定 (残っていれば)。参照音源・入力音声を与えたか。
4. big / middle / little が同じ生成のバリエーションか (採用は middle)。
5. 気になる既存音源 (実在の録音) との類似に気づいていないか (聴取後)。

**公開保留の判断の更新 (2026-10-03、ユーザー回答後): 解除する。** ユーザー回答「参照無し、Adobe モデルの効果音生成」で、残る妨げ (a) 参照音声の入力なし、(b) Adobe 製モデル (他社モデルではない) が確定した。規約の根拠 (URL・版・該当条件・確認日) は §1 に記録済み。残る未確認は「無料プランの商用利用を直接述べた公式文」と「生成時のベータ表示の有無」で、公式の製品ページと生成 AI 製品別規約に無料プランの除外も商用禁止も見当たらないことを根拠に、ユーザー了承のうえのリスクとして扱う (FAQ の該当文をユーザーがブラウザで確認して貼れば閉じる)。聴取での類似確認は採用前の通常手順として残す。

(以下は解除前の判断の記録) **公開保留の判断 (source-rights §2・§5、2026-10-03): まだ解除しない。** 規約の根拠 (URL・版・該当条件・確認日) は記録でき、Adobe 製モデルの効果音生成の出力が商用利用可・クレジット義務なし・出力は自分の Content であることは公式ページで確認できた。残る妨げは、(a) 参照音声を入力したか (入力していれば規約 §2 の別確認)、(b) 生成機能が Adobe 製モデルの効果音生成か (他社モデルなら別規約)、(c) 無料プラン・ベータ表示の扱い (規約に制限は見当たらないが直接述べた公式文は未取得で、「commercially released version」の意味も不明)。(a)(b) の回答が問題なければ解除してよい見込み。(c) は公式文が取れない限り「制限の記載は見当たらない」止まりで、残るリスクを受け入れるかはユーザーの判断 (ユーザーの「進めてよい」は権利者の許諾の代わりにならない: source-rights §5)。Content Credentials は規約上、除去・改変が禁止。この WAV を取り込み (mono 化・Vorbis 圧縮) で加工してビルドに入れることが問題になるかは、規約から判断できず未確認 ([source-rights](../../.claude/skills/vrc-asset-production/references/source-rights.md) §8)。

## 2. 音源の実測 (Node、2026-10-02)

| 項目 | 値 |
|---|---|
| 形式 | PCM 16 bit stereo 48 kHz、384,000 フレーム = 8.000 s |
| 音量 | peak -11.4 / -12.1 dBFS (L / R)、RMS -29.2 / -29.1 dBFS。0.5 秒ごとの RMS は -28.3〜-30.5 dBFS で一様 (うねり・欠けなし) |
| ステレオ | L/R 相関 0.958、side/mid -16.7 dB。ほぼ mono 相当で、3D 音源にステレオは不要 |
| 帯域 (全体比) | 300 Hz〜3 kHz に大半 (-5 / -3 dB)、3〜6 kHz -11 dB、6〜16 kHz -14〜-17 dB、100 Hz 未満 -26 dB。DC は 0 に近い |

## 3. 取り込み設定と容量

`Course/110` が書き、`Course/111` が検査する。全プラットフォーム (既定・Standalone・Android・iOS) で同じ。

| 設定 | 値 | 理由 |
|---|---|---|
| Force To Mono | 入 | 3D 音源。ステレオ差が小さい。メモリ・容量が半分 |
| Normalize | **切** (`m_Normalize`) | mono 化の Normalize は peak を 0 dBFS へ持ち上げる (+11.4 dB)。切って元の音量のまま使い、音量は AudioSource 側で決める |
| 圧縮 | Vorbis 品質 0.7 | 水の広帯域ノイズは低ビットレートで「ざらつく」ため BGM (0.5 / 0.7) より下げない。8 s・mono なので容量の差はわずか |
| Load Type | Decompress On Load、Preload Audio Data 入、Load In Background 切 | 入室時から鳴らす。クリップは 1 つで全音源が共有する。再生中にデコードの CPU がかからない。同時に鳴るのは最大 5 本 |
| Sample Rate | 維持 (48 kHz) | 6 kHz 以上にも -14 dB 前後あり、落とすと水の「きらめき」が減る。容量が小さいので削らない |

| 項目 | 見込み (実測前) |
|---|---|
| 元 WAV | 1.54 MB (版管理・Assets 内。ビルドには入らない) |
| ビルドに入る容量 (Android / iOS / PC とも同じ設定) | **約 50〜120 KB** (Vorbis mono 8 s で 50〜120 kbps と見た目安。品質値からの換算なので実測ではない) |
| 実行時メモリ | **0.77 MB** (mono 16 bit 48 kHz 8 s を 1 回だけ。音源 20 個で共有) |

容量は [ビルドサイズ実測](2026-09-24-build-size-audit.md) と同じ方法 (`Tools/KominkaHillside/Build Size/` の `7 - Apply` → `2 - Bundle Size Android`) で取り込み前後を比べて確定する。見込みの数値を実測として記録しない。

## 4. ループの継ぎ目 (数値上の判断)

聴いていない。末尾 → 先頭の段差:

| 指標 | L | R |
|---|---|---|
| 継ぎ目の段差 (最後 → 最初のサンプル差) | 0.0497 | 0.0577 |
| ファイル全体のサンプル間差の中央値 / 99 % / 99.9 % | 0.0085 / 0.0357 / 0.0543 | 0.0087 / 0.0361 / 0.0540 |
| 継ぎ目の段差の順位 | 99.9 パーセンタイル | 99.9 パーセンタイル |
| 直線外挿との誤差 (継ぎ目 / ファイル全体の 99 %) | 0.049 / 0.042 | 0.057 / 0.043 |
| 最後 / 最初の 5 ms の RMS | -30.8 / -28.1 dBFS | -30.8 / -27.7 dBFS |
| 最後 / 最初の 100 ms の RMS | -28.9 / -29.8 dBFS | -28.7 / -29.6 dBFS |

判断: 継ぎ目の段差は、この音の中で 1000 ステップに 1 回出る大きさ (約 -26 dBFS の 1 ステップ) で、全体の RMS の約 1.4 倍。
帯域の広いノイズの中ではふつうの揺れの範囲で、単独のクリックとして目立つ見込みは低い。ただし**先頭 3 ms に 0.1 の山** (ファイル内の他と同程度) があり、末尾にフェードもないため、「全く無い」とは言えない。
加えて 3D 音源に通すと高域は距離で減り、隣の音源は別ピッチで鳴るので継ぎ目は重ならない。**耳で確認し、気になったら次の修正を用意する:**

- 修正案: 等電力クロスフェード 0.25〜0.5 s。末尾の X 秒と先頭の X 秒を `cos` / `sin` の重みで混ぜ、長さを (8 − X) s に縮めた派生 WAV を隣へ置く
  (例 `Firefly_audio_小川の水音_middle_Loop.wav`。元は変えない)。ノイズは無相関なので電力和でうねらない。派生を採用するときはこの票へハッシュと作り方を追記し、`KH_RiverAmbienceEditor.ClipPath` / `ClipSha256` / `ClipSamples` を直す。
  今回は継ぎ目が目立つと判断できる数値ではないので**生成していない**。

## 5. 配置 (Course/110 が作る)

- ルート: 本番シーンのルート `KH_RiverAmbience` (AudioSource + `VRCSpatialAudioSource`、20 個の子 `River_00`〜`River_19`)。Udon なし・同期なし。これを消して `Course/110` を流せば作り直せる。
- 位置は `KH_CourseGroundEditor.RiverAt` (川の中心線と水面の高さ) から計算 (手書きの座標ではない)。川の z 135〜550 を、水平の弧長 544 m で端を含めて等分 (20 本、間隔 27〜29 m)。y は水面 +0.5 m。川の形・水面を変えたら `Course/110` を流し直す (`Course/111` が差を検出する)。
- 範囲の根拠 (散歩道は `KH_CourseGroundEditor.Route`): 散歩道が川から 40 m 以内にいるのは z 約 550 → 150 の区間 (橋 2 本を含む)。z 150 より川下 (道が西へ離れる、川から 60 m 以上) と、z 550 より川上 (道は川から 40 m 以上離れて外周を回る) は聞こえなくてよいので置かない。
- 距離: `VRCSpatialAudioSource` Gain 0 / Near 4 m / Far 48 m / Volumetric Radius 0 (吠え声・呼吸音と同じ作法)。Near 内は減衰なし。
- 本数の理由: 同時に聞こえるのは散歩道のどこでも 3〜5 本 (Far 48 m の範囲)。近似モデルで、川の中心から 6 m の岸辺を川に沿って歩いたときの合計の山谷は、間隔 40 m (14 本) で約 9 dB、28 m (20 本) で約 6 dB、20 m (28 本) で約 4 dB。川から 12〜20 m では 28 m 間隔で 4 dB 前後。本数を増やすほど山谷は減るが、同時に聞こえる本数 (CPU) と置く数が増える。28 m・Far 48 m は山谷 4〜6 dB・同時最大 5 本の折り合い。
- 川から遠い所の静けさ: 散歩道で川から 50 m 以上離れた全点 (約 1,770 m) で聞こえる音源は 0 本 (近似モデル。`Course/111` が検査)。
- 重なり: 近似モデル (Near までは 1、その先は Near / 距離、Far 手前で 0。VRChat の実曲線ではない) で、散歩道の川沿い 293 点の合計は、音源 1 本を Near で聞いたときに対して -16.0〜-6.5 dB。**音源が重なっても 1 本の最大より大きくならない** (`Course/111` は +2 dB 超で失敗)。
- ループ開始のずらし: 全音源が PlayOnAwake で同時に始まる。Udon を使わず、隣り合う音源のピッチを変える (1.000 / 1.025 / 0.975 / 1.0125 / 0.9875 の繰り返し。隣との差 1.25〜5 %)。広帯域ノイズは 1 % のピッチ差で数十 ms 以内に位相が無相関になり、足し算が電力和になる (+3 dB の山が出ない)。ループ長も音源ごとに 7.8〜8.2 s で揃わず、8 s の繰り返しが一斉に見えない。
  音程の差 (±2.5 % = ±43 セント) は水音では気にならない見込みだが、耳で確認する。気になる場合は Udon で `AudioSource.time` をずらす案 (§7 案 D と同じ小さな Udon) に替える。

| 音源 | x | y | z | ピッチ |
|---|---|---|---|---|
| River_00 | 382.7 | 3.8 | 135.0 | 1 |
| River_01 | 404.0 | 4.3 | 154.0 | 1.025 |
| River_02 | 417.3 | 4.9 | 179.0 | 0.975 |
| River_03 | 416.3 | 6.0 | 207.5 | 1.0125 |
| River_04 | 430.4 | 7.1 | 231.3 | 0.9875 |
| River_05 | 452.8 | 7.9 | 249.3 | 1 |
| River_06 | 474.6 | 8.8 | 267.8 | 1.025 |
| River_07 | 484.7 | 10.0 | 293.0 | 0.975 |
| River_08 | 469.3 | 11.2 | 317.0 | 1.0125 |
| River_09 | 448.6 | 12.2 | 336.8 | 0.9875 |
| River_10 | 427.0 | 13.1 | 355.8 | 1 |
| River_11 | 403.3 | 13.8 | 371.3 | 1.025 |
| River_12 | 379.2 | 14.6 | 387.3 | 0.975 |
| River_13 | 360.4 | 15.5 | 408.3 | 1.0125 |
| River_14 | 370.9 | 16.8 | 433.3 | 0.9875 |
| River_15 | 392.4 | 17.8 | 452.3 | 1 |
| River_16 | 412.9 | 18.8 | 472.0 | 1.025 |
| River_17 | 418.5 | 20.6 | 499.5 | 0.975 |
| River_18 | 433.6 | 22.5 | 523.3 | 1.0125 |
| River_19 | 439.9 | 25.7 | 550.0 | 0.9875 |

上の座標は Node での試算 (ビルダーの `Plan()` と同じ式)。正本はビルダーと保存後のシーン。

## 6. 音量

- AudioSource の基準音量 0.8 (`KH_RiverAmbienceEditor.BaseVolume`)。ファイルが RMS -29 dBFS と小さいため、音源の近く (Near 4 m 内) で約 -31 dBFS RMS、散歩道 (川から 8〜16 m) で約 -37〜-45 dBFS RMS。BGM の既定 (約 -38 dBFS RMS) と同じ桁で、ボイス・犬の声の下に入る見込み。**耳で決める値**で、変えるときは定数を直して `Course/110` を流し直す (Inspector で直すと再実行で戻る)。
- 同一作法: 設計値 (音量・Near / Far・ピッチ) は Setup が毎回書く ([vrc-scene-edit-safety](../../.claude/skills/vrc-scene-edit-safety/SKILL.md))。

## 7. 音量操作・ライブ中の扱い (決定待ち)

調べた事実: BGM の「設定 → 音量」は `KD_TreatPouch` が `KD_BgmPlayer` 1 つ (AudioSource 1 つ) に配線する。音量頁は BGM の行・犬の声の行・戻るの 4 操作で満杯 (`KH_BgmEditor.CheckLayout`)。`KD_BgmPlayer` は `KD_LiveSystem.IsNightWanted` でライブ中に 1 s で 0 へ下げる。
`KD_LiveSystem` は本番シーンにまだ無い (ライブ配線計画 2026-09-26 §2.2)。環境音は [DES-WLD-002](../development/issues/DES-WLD-002-sound.md) が「ローカルで足りる想定」とする。

**今回の実装 (案 A、最小): 固定音量。** 設定メニューへ行を足さず、Udon なし。BGM の ON/OFF・音量にも連動しない。
ライブ中も鳴り続けるが、聞こえるのは川から 50 m 以内だけ。ライブの舞台が川の近くでなければ影響しない (舞台の位置は未定)。

| 案 | 内容 | コスト |
|---|---|---|
| A (採用) | 固定音量 | なし。BGM を切った人も川は聞こえる (音量を下げる手段が無い) |
| B | BGM の音量に連動 (BGM のスライダー・ON/OFF が川にも効く) | 小さい Udon 1 本 (`KD_BgmPlayer` から値を読んで 20 本へ書く) と配線。BGM だけ切りたい人の操作と衝突する (環境音だけ残せない)。文言の変更 (「BGM」→「BGM と環境音」) と 13 言語の収まり検査が要る |
| C | 設定 → 音量に「環境音」の行を足す | `KD_TreatPouch` の欄と Udon 追加、音量頁のレイアウト作り直し (今は 4 操作で満杯)、13 言語の文言、`KH_BgmEditor.CheckLayout` の期待値変更。別担当が音量メニューを触っているので衝突しやすい。体験会 (Quest / iOS) の操作性確認も増える |
| D | ライブ中だけ川を下げる (行は足さない) | 小さい Udon 1 本 (`KD_LiveSystem.IsNightWanted` を見て 20 本の `volume` を 1 s でフェード、`KD_BgmPlayer` と同じ作法)。**ライブを配置してから**。ステージが川から 50 m 以上なら不要 |

推奨: まず A のまま Play の耳で確認する。BGM と川の兼ね合いに不満が出たら B か C。ライブを置いた後に舞台が川に近ければ D (B・C のどちらとも両立する)。

## 8. 実行順とメニュー

1. コンパイル (C# のみ。Udon の追加なし)。
2. `Tools/KominkaHillside/Course/110 - Build River Ambience` (Edit モード・シーン未変更・本番シーンが開いていること)。取り込み設定 → 20 個 → 保存 → 検査まで自動。
3. シーンを開き直して `Tools/KominkaHillside/Course/111 - Verify River Ambience` (`Temp/imp-env-006/river-ambience-verify.json`、ログ `RIVER_AMBIENCE_OK` / `RIVER_AMBIENCE_FAIL`)。
4. 容量は [ビルドサイズ実測](2026-09-24-build-size-audit.md) の手順で取り込み前後を比べる。Play: 川沿いを歩き、距離・つながり・重なり・川から離れた静けさ・ボイスと犬の声との音量バランス・ループの継ぎ目を耳で確認する。

`Temp/imp-env-006/before-river-ambience.unity` が実行前のシーンの控え。更新手順は [周回版の運用記録](des-env-002/production.md) の表に 1 行 (川のせせらぎ音) を足した。

## 9. 全音源の空間音設定見直し (2026-10-07)

ユーザー依頼で、本番シーンの非アクティブを含む AudioSource 70個を点検。川20個と犬の声12個には VRCSpatialAudioSource があり、残り38個には無かった。川音の不足コンポーネントが原因とは確認されていない。Unity側の Spatialize がオフの音源、犬4匹の吠えの Unity距離1.5〜18m / VRC距離2〜5mの不一致も確認した。

`KH_AudioSpatialEditor` の `Audio/1 - Apply Spatial Settings` → `Audio/2 - Verify Spatial Settings` → シーン保存で修正。各生成元のEditorも同じ設定処理を呼ぶ。既存VRCのGain/Near/Far、クリップ・音量・ピッチ・ループ・自動再生・Udon配線は維持。新規VRCはGain 0、2DはSpatialize/EnableSpatializationオフ、3Dはオン、Dopplerは0。購入アセットは未編集。

| 音源 | 個数 | 減衰と設定の所有者 |
|---|---:|---|
| 川 | 20 | 既存VRC Near4 / Far48m、音量0.8を維持。Unity側の距離とSpatializeを整合 |
| 犬の吠え / 呼吸 | 6 / 6 | 既存VRCの2〜5m / 1〜3.5mを保持。Unity側も同じ距離 |
| 水遊び | 6 | 既存Custom (1〜20m、20mで0) のキーを完全保持し、VRCでAudioSource曲線を使用 |
| 洗う音 / シャワー | 18 / 5 | 0.5〜8m。8mで0となるCustomに変更し、VRCで同じ曲線を使用 |
| おやつ演出 | 4 | 1〜12m。12mで0となるCustomに変更し、VRCで同じ曲線を使用 |
| 店主 | 1 | 既存Linear 1.5〜12mを維持、VRCで同じ曲線を使用 |
| BGM・ライブ・ポーチUI・合流通知 | 4 | 既存2D再生を維持、VRC設定を明示 |

Logarithmicから変更した27音源は、近距離まで1、その先を `near/d × (far-d)/(far-near)`、far以遠を0とする線形補間のCustom。UnityのLogarithmicはmaxDistanceでも無音にならないため、範囲外へ残る音を除く。中距離も小さくなる変更で、洗い4mは旧Logarithmicの約53%、おやつ6mは約55%が式の目安 (実装は離散キー補間)。聴感の採用確認は未実施。

検証 (root実装、別プロセスによる独立相談・レビュー):

- C#コンパイルの新規エラー0、新規Editor型のロード確認済み。Udonコード/配線変更なし。
- 全体構成検査: total=70 / checked=70 / unmanaged=0 / errors=0。再適用は追加0個、保存したシーンのSHA256が1回目と一致。
- 川の既存Course/111検査: 20音源・経路1217点・川沿い検査対象293点・川から離れた887点を検査し、範囲内の音源は最大5個。配置・取り込み設定はOK。距離モデルであり実際の聴取結果ではない。
- 保存前後の独立YAML検査: AudioSourceの許可項目、VRC38個の追加とGameObjectのコンポーネント参照だけを変更。水6音源のCustom曲線、音声参照・再生条件・基準音量は保持。Unity保存時の環境反射色の自動更新は今回の差分から除外。
- 再現資料: `Temp/audio-audit/verify.txt`、`saved-diff.json`、`sources.tsv`、`before.unity`、`Temp/imp-env-006/river-ambience-verify.json` (Tempは非追跡)。

Play/アップロードは未実施。実機では、川沿いの音のつながり・48m外の静けさ・8秒ループの継ぎ目、洗い場8m/水遊び20m/店主・おやつ12mの減衰、犬の声5m/呼吸3.5m、BGM/UI/ライブの2D維持を確認する。洗い場は近距離でのASMRの聞こえと会話の両立も確認する。PCVR・Quest単体・iOSの聴感は別々に記録し、構成検査を聴感合格と扱わない。川の固定音量・BGMやライブに連動しない方針は変更していない。

最終確認 (2026-10-07 15:24 JST): ユーザーのリロード後、Unity APIの応答が復旧。本番シーン・Editモード・dirty=Falseを確認し、後片付け後のC#コンパイル検査はエラー0。一時監査Editorの型がアセンブリから消えたことも確認。再読み込み後の全音源検査は70/70・エラー0、川音のCourse/111は20音源・最大5個・エラー0。保存YAML検査もPASS (VRC38個追加、水6曲線保持、非音響変更なし)。再読み込み待ちの留保は解消した。実機での聴感とPlayは上記のとおり未確認。全体git diff --checkの警告はUnity生成YAMLの空欄末尾空白 (開始前からのUdon資産差分を含む) で、今回のEditor/文書の限定diff --checkと文書リンク検査は通過。
