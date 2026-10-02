---
id: IMP-WLD-003
type: IMP
system: WLD
status: review
priority: Medium
summary: 専用BGM「木漏れ日の帰り道」003を導入し、古民家と同じ設定メニューで個人のON/OFF・音量調整を可能にする
next: "残り: ON/OFF ボタンを残すか・基準音量 0.35 と圧縮品質の決定 (A131・A132)、別プレイヤーへ影響しないこと (2 人)、Android/iOS の容量の実測と Quest/iOS の操作性。PCVR 1 人 (A130・A133・A134) は 10-02 ユーザー総合判断で確認済み。WAV は版管理へ収録済み"
updated: 2026-10-02
decided: 2026-09-29
---

# IMP-WLD-003 専用BGMの導入と設定メニューのON/OFF・音量調整

2026-09-29 ユーザー依頼: 制作したBGMを裏山へ組み込み、古民家ドッグランと同じようにメニューの設定からBGMをON/OFFし、音量を変えられるところまでをセットで実装するIssueを作る。今回はIssue登録のみで、音源取り込み・シーン配線・公開は未実施。

## 対象音源

- 採用対象: **KH_Komorebi 003「木漏れ日の帰り道 — きみと、この場所で」**。216秒（3分36秒）のシームレスループ。好評だった002を発展させた003を使い、001/002と取り違えない。
- 制作元: `C:/work/unity/vrc-music-agent/songs/KH_Komorebi/versions/003/`
- 取り込む完成音源: `output/KH_Komorebi_003_Loop.wav`。48kHz・24bit・stereo、62,208,044 bytes。`output/master.wav` は未正規化・未折り返しの工程用なので使わない。
- SHA256: `fca0b63df11744823972d0a5477db11fea4dd6a5dcd7bd0f8f8fc1fc1a201774`
- [音源の検証記録](../../../../../vrc-music-agent/songs/KH_Komorebi/versions/003/review.md)・[素材と研究の記録](../../../../../vrc-music-agent/songs/KH_Komorebi/versions/003/musical-basis.md)。元WAVとREAPERプロジェクトはMusic Agent側に保持する。

## 実装範囲

1. 完成WAVを本プロジェクトの自作コンテンツ側へ取り込み、本番シーン `Assets/Scenes/VRCDefaultWorldScene.unity` の常時BGMとしてループ再生する。古民家同様の2D BGMを基準とし、犬や会話を邪魔しない作者側の基準音量を設定する。
2. 既存メニューの **設定 → 音量** にBGMの行を組み込み、古民家と同じON/OFFボタン・音量スライダー・現在値表示で操作できるようにする。新しい音設定画面を別に増やさない。犬の声やライブ音量の項目とは独立させる。
3. 設定は本人の聞こえ方だけに反映し、他人の音量・ON/OFFを変えない。音量0で無音、OFFでも選んだ音量を保持し、ONへ戻すとその値が効く。メニューの開閉でも値を維持する。
4. 古民家と同じライブ連携を含める。ライブの演出開始から終了までBGMを滑らかに抑え、終了後は現在の個人設定に従う。元からOFF、ライブ中にOFF/音量変更、途中入室、中止の場合も、勝手にONや以前の音量へ戻さない。
5. 初期値・再入室時の保存/復元は古民家の実装を照合して合わせる。コード上の初期値はON・50%だが、シーン配線・永続化の実態を確認して実装記録へ残す。新しい保存仕様をこのIssueで勝手に追加しない。
6. PC・Android/Quest・iOSの取り込み設定を決め、容量・再生メモリと音質を確認する。曲の長さとステレオを保つことを基準に、Vorbis圧縮・Streamingを候補として評価する。

## 既存実装との関係

音の方針は [DES-WLD-002](DES-WLD-002-sound.md)、設定画面の器は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) / [world-ui-spec](../../world-ui-spec.md) を参照。古民家と裏山の両方に `KD_BgmPlayer` と `KD_TreatPouch` のBGM設定コードがある。裏山の音量頁は犬の声を中心に構築した経緯があるため、存在するコードだけで配線済みと判断しない。

- 裏山の確認対象: `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_BgmPlayer.cs`、`KD_TreatPouch.cs`、`KD_TreatPouch.Settings.cs`、設定頁のEditor生成処理、`KD_LiveSystem`。
- 古民家の比較元: `C:/work/unity/vcc/KominkaDogRun/Assets/KominkaDogRun/Scripts/` の同名実装と実シーン。古民家の寝室・ラジオなど、裏山の対象外機能は追加しない。
- UIの変更は既存の配置・文言・ローカライズ方式に揃え、Setup再実行でも保持される配線にする。

## モバイル容量の扱い

平均192〜256kbpsなら216秒で約5.2〜6.9MBというのは**計算上の目安**であり、Unityの圧縮品質値への直接対応や実測結果ではない。元WAVの62.2MBをそのままダウンロード増分と数えない。

[既存の容量記録](../../plans/2026-09-24-build-size-audit.md) と同じ方法で、取り込み前後のAndroid/iOS圧縮バンドルを比較する。過去の約46MBという値を現在のビルド結果に流用しない。非圧縮バンドル容量と実行時メモリも分ける。音質削減を先に行わず、余韻・ループを保てる設定を選ぶ。

## 完了条件

- [ ] 指定した003の完成音源が本番シーンへ配線され、参照・ループ範囲・再生設定を保存後に確認できる。
- [ ] 設定 → 音量にBGMのON/OFF・音量スライダー・値表示があり、古民家同様に利用できる。犬の声・ライブの設定や他のメニューを壊していない。
- [ ] 0%・中間・100%、OFF→ON、OFF中の音量変更、メニュー開閉で表示と実出力が一致する。別プレイヤーへ影響しない。
- [x] ライブ開始/終了/中止、ライブ中の設定変更・途中入室でBGMとライブ音が競合せず、本人の現在設定を尊重する。
- [ ] 古民家との初期値・再入室時の挙動の対応が記録され、未確認を保存済みと扱わない。
- [ ] コンパイル、Setup/Verify、保存後の参照・UIイベント・Udon値の整合を確認する。
- [ ] Android/iOSの圧縮後サイズとワールド容量の増分を実測し、再生時のメモリ/負荷を容量と区別して記録する。
- [ ] PC/VR・Quest/Android・iOSで操作性、会話や犬との音量バランス、圧縮後の余韻、複数周回の継ぎ目を確認する。実機待ちはreviewとし、完了にしない。

## 実装記録 (2026-09-29、コードと Editor ビルダーまで。Unity 未実行)

### 着手前の事実 (コードとシーン YAML から確定)

- 裏山: `KD_BgmPlayer` と `KD_TreatPouch` の BGM 設定コード (`_BgmChanged` / `_ToggleBgm` / `ApplyBgmSettings`) は古民家と**同一** (差分なし) だが**未配線**。本番シーンに `KD_BgmPlayer` は 0 個、`KD_TreatPouch` の `bgmPlayer` / `bgmSlider` / `bgmValueLabel` / `bgmToggleLabel` / `bgmToggleImage` はすべて `{fileID: 0}`。音量頁の構築 (`KD_DogVoiceSettingsEditor.Hillside.cs`) が BGM の行を毎回消して参照を null にし、Menu/16a・Verify 22 が「BGM が無いこと」を検査していた。
- 古民家 (比較元): `KD_Bgm` (2D・loop・PlayOnAwake、`baseVolume` 0.35、`live` 配線済み)。取り込みは Vorbis・Streaming、品質 PC 1.0 / Android・iOS 0.5。音量頁は IMP-ENV-011 で「外・寝室 × BGM・犬の声・ライブ」の 6 スライダーにし、**BGM / 犬の声の ON/OFF ボタンは画面から外して 0% で無音に統一**している (シーンの `bgmToggleLabel` / `bgmToggleImage` は null、`BgmToggle` は無い)。
- 保存・復元: 古民家も裏山も BGM 設定を PlayerData に保存しない。初期値は `_bgmEnabled = true` / `_bgmVolume = 0.5` (コード)、スライダーの焼き値も 0.5。**再入室すると ON・50% に戻る** (古民家と同じ。新しい保存仕様は足していない)。
- ライブ連携: `KD_BgmPlayer.Update` が `KD_LiveSystem.IsNightWanted` (夜のフェードインの頭〜タイムラインの終わり、同期時計の純関数) で掛け金を 1 秒かけて 0 へ動かし、個人設定は書き換えない。途中入室は `Start` で最初から 0、中止は `startMs` を終端へ送るので同じ式で戻る。古民家と同じコードをそのまま使う。

### 実装

- 音源: `Assets/KominkaHillside/Audio/BGM/KH_Komorebi_003_Loop.wav` (SHA256 一致を確認してコピー。.meta は Unity が作る)。
- `Tools/KominkaHillside/Course/98 - Build BGM` / `99 - Verify BGM` (`Scripts/Editor/KH_BgmEditor.cs`): SHA256 照合 → 取り込み設定 (Vorbis・Streaming・preload なし・48 kHz 維持・stereo、品質 PC 0.7 / Android・iOS 0.5) → `KH_Dogs/KD_Bgm` (AudioSource 2D・loop・PlayOnAwake + `KD_BgmPlayer` SyncMode None、`live` = `KD_LiveSystem`、`baseVolume` 0.35) → 音量頁の BGM の行と文言の束縛 → 保存 → 検査。検査は取り込み設定・クリップ (stereo・48 kHz・10,368,000 フレーム)・配線 (proxy と backing の両方が、音量頁の BGM の行の実体と `KH_Dogs/KD_Bgm` の `KD_BgmPlayer` に同一か。スライダーと [ON/OFF] のイベントも宛先の UdonBehaviour の実体とイベント名 `_BgmChanged` / `_ToggleBgm` で比べる)・頁の配置と重なり・13 言語の注記の収まり・**掛け算と設定保持の単体検査** (別シーンで 0/50/100%、OFF→ON、OFF 中の変更、掛け金 0 の間の変更、掛け金の前から OFF なら後も OFF、掛け金 0 の間に ON。掛け金 `_duck` は反射で直接書く) を見る。結果は `Temp/imp-wld-003/bgm-build.json` / `bgm-verify.json`。
- **未検証 (ClientSim / 実機)**: 単体検査は `KD_BgmPlayer.Start` / `Update` と `KD_LiveSystem.IsNightWanted` を通らないので、ライブ開始・終了・中止での上下、途中入室、1 秒フェード、実際の出音と他プレイヤーへの影響は確かめていない。json の `notCovered` に同じ内容を出す。上の「ライブ連携」はコードを読んで分かったことで、検査で確かめた結果ではない。
- 音量頁 (`KD_DogVoiceSettingsEditor.Hillside.cs`、Menu/7・11・16 の共通本体): 「外」の列に BGM の行 (題字 BGM・値・スライダー・右に [ON/OFF]) を犬の声の上に置き (88 間隔、古民家と同じ)、注記を 2 行 (既存「自分だけに反映・0% で消音」+ 古民家の `KD_TreatPouch.WordBgmNote`「ライブ中は自動で止まります」、13 言語は文言表に既存) にした。`KD_Bgm` が居れば `pouch.bgmPlayer` を配線する (古民家と同じく両方向)。Menu/16a・Verify 22 の検査を「BGM の行がある」に改めた。束縛は `Data/UILocalizationBindings.json` に `Page_Settings/VolumeSettings/BgmNote` を 1 件追加。
- 新しい翻訳キーは足していない。Udon のコードは変更なし。

### 未確認・判断待ち

- [ON/OFF] ボタンは本 Issue の実装範囲 2・3 に合わせて付けた。**古民家の現行画面にはこのボタンが無い** (上記)。古民家に揃えるならボタンを外し 0% を OFF とする (コードは両対応)。
- `baseVolume` 0.35 は古民家の値を仮に使った。003 は peak -3.0 dBFS / RMS -23.1 dBFS で、古民家の曲 (mp3、取り込みで normalize あり) との聞こえの差は実機で決める。品質 0.7 / 0.5 も容量実測の前の候補。

## 今回の範囲外

2026-10-02 未コミット整理: 完成WAV 62,208,044 bytesのSHA256が上記003版と一致し、既存のAudioImporter設定とフォルダmetaを併せて版管理へ収録する。音源の改変・シーン再構築・試聴の代行は行っていない。聴感と実機の未確認条件は継続する。

楽曲の追加改作、環境音の新規制作、場所別の曲切替、ラジオ、ライブ曲の差し替え、公開操作。環境音などの残る音設計はDES-WLD-002で扱う。

2026-10-02 ユーザー確認: PCVR 1 人の項目は「基本全部見たから OK」(総合判断。項目別の結果の記録なし。[play-checklist-2026-10-02](../../testing/play-checklist-2026-10-02.md) の A 節)。残り: ON/OFF ボタンの採否・基準音量・圧縮品質の決定 (総合判断では決めない)、2 人、Android/iOS の容量と端末。
