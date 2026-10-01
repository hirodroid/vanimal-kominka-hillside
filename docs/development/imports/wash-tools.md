# 洗浄道具 0.1.0 の取り込み・配置

2026-10-01、ユーザーの「取込＆配置まで」の追加依頼により実施。09-30のUnity非接続指定は今回の取り込みについて解除された。ラボで作成した5モデルの採用を確認し、本番 `Assets/Scenes/VRCDefaultWorldScene.unity` に保存。

原本は [ラボ制作票](../../../../../vrc-asset-lab/assets/wash-tools/asset.md) と [引き渡し](../../../../../vrc-asset-lab/assets/wash-tools/handoff.md)。一般的な道具の構造から自作し、外部画像・購入モデル・先行Animal Washの素材は流用していない。原本revisionは0.1.0。コピーした7ファイルのSHA-256、原本のコミット、ワールド固有調整は `Assets/KominkaHillside/Models/WashTools/asset-origin.json` に固定する。

## 配置と調整

既存の主洗い場と道中3か所の仮モデルを差し替えた。新しい洗浄ロジックや洗い場は増やしていない。

| 洗い場 | 位置 (world x/y/z、m) | 道具 |
|---|---|---|
| `KH_WashStation` | 42.5000 / 2.0706 / 43.0000 | スポンジ・シャワー・タオル各2 |
| `KH_WashStations/River` | 508.3267 / 10.1384 / 286.0372 | 各1 |
| `KH_WashStations/Upper` | 224.0210 / 39.9988 / 599.5621 | 各1 |
| `KH_WashStations/Hillside` | 66.4234 / 19.0014 / 121.2640 | 各1 |

各地点に木製棚・すのこを1つずつ。計15道具＋8設備＝23 Renderer、表示三角形25,040、共通Material1つ。描画回数・実機FPSを意味する数値ではない。元のプリミティブRendererは無効にし、必要な既存Collider・参照先のGameObjectは維持した。

元モデルはラボに保持し、ワールド内のVisual Transformで次を調整する。

- 棚：既存の幅1.2 m・奥行0.45 m・天板高0.8 mへ合わせる。操作側は−Zなので、FBX原本から棚だけY180°回転。既存の棚・脚のCollider、看板・RESET札を保持。
- すのこ：既存の0.6×0.9 m・歩行面高0.03 mへ合わせる。ラボの1.3×0.9×0.1 mのまま拡張しない。既存の通路・犬の地面判定・NavMeshの構成を保つため。草が重なる箇所は既存Terrainの配置として残り、草・地面の編集は今回対象外。
- タオル・スポンジ：既存のBoxColliderへサイズと中心を合わせ、道具の棚上位置・戻り位置・接触点を維持。
- シャワー：0.75倍にし、モデルのNozzleAnchorを既存Nozzleに位置・方向とも合わせる。既存の放水はそのまま噴射面から出る。CapsuleColliderは新しい形へフィットし、半径は0.028 mから約0.0356 mへ拡大（高さ・中心もモデルBoundsへ合わせる）。拾う判定の調整であり、洗浄の接触半径・射程は変更していない。

ステーション原点・回転、道具ルート・Home・Nozzle・State・Udon backing・VRCObjectSync・Pickup・犬一覧・リセット対象の参照を保持。自動戻し0、シャワーのUseトグルを保持。ホース・蛇口・犬を台へ呼ぶ機能は今回追加していない。

## 材質・取り込み設定

`Models/WashTools/` にFBX5つ、BaseColor/Roughness PNGをコピー。Unity用 `WashTools_MetallicSmoothness.png` は元Roughnessを非色データとして読み、RGB=0、A=1−Roughnessで256²へ変換。共通 `KH_WashTools.mat` はStandard/Opaque/非金属、GPU Instancingを有効化。Read/Writeは最終的にOFF。

PCはBaseColor1024²、MetallicSmoothness256²。Android/iOSはBaseColor512²、MetallicSmoothness256²、ASTC 6×6を明示。原RoughnessはMaterialから未参照。モデルのAnimation/Camera/Light/自動Collider/自動Material取り込みをOFF、固定物2点はUV1生成をON。現行描画はLight Probesを使用し、新たなベイクはしていない。

## 更新手順

既存の `KH_DogWashSetupEditor` に `Art.cs` を追加。通常のWash/11の道具・棚生成からもアートを適用し、次の再構築で仮モデルへ戻らないようにした。メニューは `Tools/KominkaHillside/Wash/`。

| メニュー | 内容 |
|---|---|
| 30 - Survey Imported Tools | FBXの軸・アンカー・既存配置を読み取り |
| 31 - Import and Apply Tool Art | Import設定・共有Material・アート適用、バックアップと参照照合、保存 |
| 32 - Verify Imported Tool Art | 数・材質・寸法・ノズル・コライダー・圧縮設定を独立検査 |
| 33 - Capture Imported Tools | Edit中の実シーンの全景・近景。Playに入らない |

31は本番シーン・Edit/Idle・保存済みを要求。未保存変更があれば保存を自動で巻き取らず停止する。バックアップは `Temp/WashToolsImport/before-*.unity`。適用2回でモデル数23のまま増殖なし。全体のWash/11は今回実行していない（既存の犬などを再構築する必要がないため）。

## 検証

- C#コンパイル完了、エラー0。ランタイムC#/Udonコード変更なし。
- 31で配置・保存後、シーンをディスクから開き直して32とWash/12を実行。アート検査失敗0、既存洗浄検査3,887項目PASS。
- アートは23モデル、共通材質、ノズル位置と前方一致、道具の棚への埋まりなし。テクスチャRead/Write OFF、Android/iOS Overrideあり。
- 保存YAMLを変更前バックアップと文書ID単位で比較し、既存の変更は洗い場階層内のみ、既存ドキュメント削除0。犬・地形・手動配置・性能設定へ変更を加えていない。
- Mobile Caps Verify：対象8テクスチャ、missing 0。今回の新素材は局所的に上限設定し、全資産へのApplyは実行していない。
- Unity Editレンダーで全景・近景を確認。棚の前後を修正して再確認。画像は [全景](../../plans/evidence/2026-10-01-wash-tools/station.png) / [道具の近景](../../plans/evidence/2026-10-01-wash-tools/tools.png)。証拠は同じディレクトリの `art-verify.txt`・`scene-diff.json`・`wash-verify.json`。
- Android代理バンドル：圧縮50.28 MB / 非圧縮129.51 MB、計測134秒。通信待ちはタイムアウトしたが、Unity側の完了ログと生成ファイルで完了を確認。SDKのアップロードサイズや実機性能を保証する計測ではない。

独立レビュー担当は、実装に参加していない別エージェント `wash_asset_review`。指摘は (1) シャワーCollider変更の記録・検査不足 → 意図したフィットとして上記に記録し値の検査を追加、(2) 棚の前後が逆 → Y180°回転で是正。追加レビューの再帰起動なし。

## 実機で残る確認

今回Play・VRChat実機は未実施。各地点で道具の掴みやすさ、シャワーの噴射と手の向き、タオル/スポンジ接触、棚へ戻すRESET、実照明・Quest/iOSの質感と負荷をユーザーが確認する。編集時の参照・描画検査を操作の合格に読み替えない。既存の待ち条件は [IMP-ENV-007](../issues/IMP-ENV-007-main-wash-station.md)・[IMP-ENV-008](../issues/IMP-ENV-008-wash-stations-along-course.md) で継続。
