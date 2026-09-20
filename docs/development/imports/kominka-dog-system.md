# 古民家ドッグランからの犬ギミック移植記録

- コピー元リポジトリ: `C:/work/unity/vcc/KominkaDogRun`
- コピー元タグ: `export/kominka-hillside-v1` (annotated、移動しない)
- コピー元コミットSHA（完全長）: `503dde8ff702e8d9f4dd73b4a6473ede7dcfd638`
- 移植日: 2026-09-20
- 範囲: 同じ6犬種のモデル、Animator、Pet/Fetch/Wander/StopAnchor/DogPack、各犬のボール。型依存を保つためDebug以外の既存runtimeスクリプト・Program Assetもコピー。元シーンの必要部分1133ブロックを抽出。ファイル594件と元データSHA-256は [一覧](kominka-dog-files.json)。`.meta` のGUIDを維持。
- 配置: 自作資産を `Assets/KominkaHillside/Imported/KominkaDogRun/`、購入資産を元と同じ `Assets/TORINOSUKE/`。新しい散歩機能は `Assets/KominkaHillside/Scripts/KH_Leash*.cs`。既存クラスは`KD_`とnamespaceを維持し、参照切れを増やさない。
- 管理方針: 以後はこのワールドで個別管理する。コピー元の変更は自動同期しない。

## Git外の依存

| 対象 | 版・出所・必要条件 |
|---|---|
| TORINOSUKEの6犬種とモーション | ユーザー所有・古民家の導入済みファイルから必要なGUIDだけをコピー。販売版番号は取得できず、ファイル一覧とハッシュで識別。元のPrefab/骨/アニメーション契約を維持。Git除外済み |
| lilToon | 2.3.4、既存VPM導入。犬導入前に最適化された設定では毛のマスク・陰影が無効だったため、公式同梱メニュー `Assets/lilToon/[Shader] Refresh shaders` で更新。元の犬材質は編集していない |
| UzumoreShader | `jp.sigmal00.uzumore-shader` 1.0.20、古民家の同名Packagesからコピー。lilToon >=1.9.0依存。VPM manifestに記録。欠落すると6匹の毛がピンクになる |
| SDK | 移植先Worlds/Base 3.10.5。Avatar DescriptorなどWorldsで解決しないコンポーネントは、移植したシーン内だけ除去。購入Prefabは変更しない |

タグだけで購入物・パッケージを復元できるとは扱わない。一覧の`missing`には組み込み参照と省略したAvatar関連GUID等が含まれるため、その数を参照整合の合否には使わない。Editor検査では実際のシーンのmissing script・材質・必須配線を確認する。

## 移植時の差分

- 元の作業ツリーにあった未コミットのシーン・Program Asset・音量設定は取り込まない。trackedファイルは `git show <SHA>:<path>` から採取した。
- `KH_DogPortSource.unity` は移植用の中間シーン。本番は `Assets/Scenes/VRCDefaultWorldScene.unity`。犬を`KH_Dogs`に置き、Terrain再生成の`KH_World`の外で保持する。
- 室内、睡眠部屋、ショップ、進行・保存、ライブ、写真、トレーナーの各サービスはシーンへ配置しない。散歩に必要な機能を先に検証するため。型依存のコードや元モデル内の非表示の飾りは残るが、選択UI・保存・解放を実装済みとは扱わない。
- `KD_MenuSettings` のSleepKit参照を`KH_WITH_SLEEPKIT`で分離。既定では参照しない。夜・部屋用サービスは未配置。
- WanderにHillsideLeash partialを追加。犬のOwnerだけがNavMesh経路と既存の加減速・地面追従を使って動かす。位置同期は元のVRCObjectSyncを継続。
- Fetchは接続中の開始を拒否。接続状態が遅れて届いた競合は既存GiveUpを通してボールを解放する。Idleに戻る際に全クライアントでWanderのアニメ出力を失効し、次の更新で再適用。
- 門前の犬・ボール配置、自由行動の中心、地面レイヤー、首輪・先端・経路を裏山用へ設定。犬の子階層はPickupNoEnvironmentとし、地面レイが自分のColliderを拾わないようにする。
- 元の移動・触れ合い・ボール処理の全挙動が裏山でも成立するかは、ユーザーのPlay・2人試験で確認する。

## 再現と確認

`scripts/kh-dog-port.js` は固定SHAから初回コピーする補助ツール。`--copy`は初回のみ。改変済みの移植先と違う内容があれば上書きを拒否する。`--stage-only`は中間シーンのみ再生成する。パッケージは別途上表どおり導入する。

Unityメニュー `Tools/KominkaHillside/Walking/` の順序は、1 Create Programs → コンパイル完了確認 → 3 Build → 4 Bake Navigation → 5 Verify。Buildは既存犬の配置・調整済み首輪の位置を保持する。8 Refit Collarsは犬種別の初期値へ意図的に戻すメニュー。

コピー元はユーザーの既存ワールドの動作実績を土台とするが、この固定SHAの全機能を再試験した記録は今回取得していない。移植先の証拠・未確認条件は [散歩実装記録](../../plans/2026-09-20-leash-walking.md) を正とする。

## 後日の修正取り込み

| 日付 | 原因・修正元のコミット／Issue | このワールドへの適用内容・見送り理由 | 確認結果 |
|---|---|---|---|
| 2026-09-20 | IMP-DOG-002 | リードとFetchの競合・アニメ出力キャッシュは裏山固有の追加処理。古民家へ自動逆移植しない | 静的レビュー済み。ユーザーPlay/PCVR総合評価は良好、競合の項目別結果は未取得 |
