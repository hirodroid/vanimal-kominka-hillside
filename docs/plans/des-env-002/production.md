# 周回版の本番採用とプラン整理

2026-09-27。ユーザーが全体を了承し、細部は今後直す方針で本番採用・整理・コミットを依頼した。

## 普段使うシーン

- 本番：`Assets/Scenes/VRCDefaultWorldScene.unity`。承認済みの周回版をコピーし、既存のファイル名・GUID・公開先IDを維持。
- 旧本番：`Assets/Scenes/Archive/KH_HillsideBeforeRiverLoop.unity`。旧17点の道の比較・復元用。通常の編集/ビルド対象ではない。
- 二重編集を避けるため、作業用 `KH_RiverLoopCourse.unity` は本番採用後に除去。承認時のシーンそのものは `Logs/kh-course-promotion/accepted-course.unity` に退避。
- `KH_RiverLoopCourse/OcclusionCullingData.asset` は本番から参照する遮蔽データなので保持。CourseのTerrain/NavMesh/材質も同じGUIDを引き継ぐ。

旧本番のシーンは保存時のファイルそのものを保持する。犬のスクリプト・フォントなど共通資産は共有されるので、Archiveだけでプロジェクト全体が過去版へ戻るわけではない。完全な復元にはGitの対象版を使う。

## 更新と検査

UnityをEditモード・保存済みにして `Tools/KominkaHillside/Course/` を使う。

| 対象 | 更新 | 確認 |
|---|---|---|
| 地形 | 15（高さ/ペイント等を再生成するため、手調整前に適用範囲を確認） | 14 |
| 犬の通行 | 30（所有NavMeshを更新） | 20 |
| 経路 | 現在の436点を維持。21は旧17点からの初回構築専用。変更時は更新手順を別途作る | 22 |
| 目的地 | 現在の12行/6目的地を維持。25は初回追加専用 | 26 |
| 地点ワープ (IMP-WLD-001) | 地点 = 80、地図・暗転・副画面 = 84、合流の受付箱・通知板・[人] = 85。経路や地点を変えたら 80 → 84 → 27 | 81 |
| 表示名フォント (IMP-WLD-001 段階 4) | 86 (`KH_DisplayName SDF` と Latin fallback を焼き直す。既存アセットは中身だけ入れ替えて GUID を保ち、17 ラベルへ配線。文字集合を変えたら先に `node scripts/kh-gen-name-charset.js`) | 81 (欠字 0・Static・EditorRef なし・Alpha8・ビルドに入る TTF の参照 0・配線) |
| 遮蔽 | 52で再生成し、完了後36で保存 | 37、必要に応じ38の表示比較 |
| 秋の橋・地面 | Inspectorで調整。68の初回適用は繰り返さない | 69 |
| 水遊び場 (IMP-DOG-020) | 76 (水遊び場のデータ、踏み分け道 (Terrain 層 2 の塗り)、目印の石と仮看板、犬ごとの揺れの配線)。次の後は、76 → 77 をもう一度実行する: 15 (地形)、30 (NavMesh)、経路・通過点「川辺」の変更、川岸の石や障害物の変更 (76 はこれらから採る区間・入水点・出水点・復帰点を決める)、仮看板の寸法や色の変更。仮看板の文言だけを変えるときは、訳のファイルの `course.water.sign` を直して 27 → 77 (77 が 13 言語の収まりを検査する) | 77 |
| 水音 (IMP-DOG-020 段階 7) | 87 (WAV の取り込み設定と犬ごとの AudioSource の配線。音を作り直したら先に `node scripts/kh-gen-water-se.js`。76 で水遊び場を作り直した後も 87) | 87 (自前の検査。`Temp/imp-dog-020/water-audio.json`) |
| 川のせせらぎ音 (IMP-ENV-006) | 110 (WAV の取り込み設定と、川の中心線に沿ったルート `KH_RiverAmbience` の AudioSource 20 本。川の形・水面 (15 / `KH_CourseGroundEditor.River`) を変えたら 110 をもう一度。音量・Near / Far は `KH_RiverAmbienceEditor` の定数が正本。[制作票](../2026-10-02-river-ambience-production.md)) | 111 (`RIVER_AMBIENCE_OK`。自前の検査。`Temp/imp-env-006/river-ambience-verify.json`) |
| フリスビーと川 (IMP-DOG-020 段階 7) | 89 (犬 6 匹とディスク 6 枚へ川のデータと水遊び場を配線) | 89 (`FRISBEE_RIVER_OK`。帯の手前で止まる・岸の点・水遊び場の中は止めない) |
| 犬を洗う (IMP-DOG-024〜030、IMP-ENV-007 / 008) | メニューは `Tools/KominkaHillside/Wash/` (Course ではない)。初回だけ 10 と 10b (Program Asset) → U# のコンパイル。11 (6 匹の殻・部位 Collider・KH_Wash_*・主洗い場と道中の洗い場 3 か所・道具・音の取り込み。棚と洗い場の位置 (x / z / yaw) は保つ。y は 2026-10-13 からコードの規則 = 足元の最高地面 + 0.01 m、BUG-ENV-002) → 20 (泥の地点)。位置マップを 2 で焼き直したら 11 をもう一度。シェーダーの既定値を変えたら 17 → 11 | 12 (`Temp/KH_DogWashSetupVerify.json`)、21 (泥の地点)。見た目は 18 で犬を選び 13 → 15 / 16 → 14 (保存しない)。ClientSim は `Course/100` |
| 洗浄道具の制作モデル (2026-10-01) | Wash/31で全4洗い場へ適用・保存。Wash/11再構築時も適用 | Wash/32 + Wash/12。寸法・材質・検証は [取込記録](../../development/imports/wash-tools.md) |

旧 `Terrain Prototype/Build Stage A・B`、`Walking/3 Build・4 Bake Navigation・4b Build Walk Route`、`Menu/7 Build Menu・12 Rebuild Panel・13 Apply Scroll Layout・17 Refresh Menus` と Imported-UnusedのTreat System再構築は、周回版を旧道・旧メニューで上書きするため拒否する。通常の局所編集・検査は可能。全メニューの再生成が必要になったときは、周回の行き先を保持する対応を先に行う。

## 切替の確認

切替前はEditモード・dirty=false。本番内容と既存meta、承認済みコース、BuildSettingsを `Logs/kh-course-promotion` に保存してから適用した。独立した72の検査で、本番の内容が承認済みコースと完全一致、旧本番バックアップが変更前と一致、本番meta不変、BuildSettingsが本番1件、閉路436点、旧再生成ガードを確認。

本番で地形14・Nav20・経路22・メニュー26・遮蔽37を再検査しエラー0。PVSは1,315,952 bytes。C#・Udonコンパイルともエラー0、最終ガード修正後のC#もエラー0。Unityから列挙した本番の自作依存776件すべてについて、アセット本体とmetaがGit管理対象に揃うことを確認した。今回のシーン切替で地形や犬の動作データは変更していないため、全周ハーネスの再実行は対象外。アップロードは実施していない。

独立レビュー `/root/proposal_review` は本番/旧バックアップ/metaの一致と切替参照を確認。指摘されたMenu/13の8行並べ直し、Imported-Unusedの事後拒否は、それぞれ入口で拒否するよう修正。メニュー21を通常更新とした案内も初回専用へ訂正した。

## プランを整理した範囲

JSONは50個から2個に整理。残すのは、HTMLの配置図データ `proposal-data.json` と、旧17点の互換性検査に使う `original-route.json`。48個（約12.36 MiB）の中間・測定ログは `Logs/des-env-002-plan-archive-20260927` に退避し、内容のハッシュ一致を確認してからプラン側を除去した。

結果・条件・失敗履歴は[制作記録](implementation.md)、[秋の改修](autumn-polish.md)、[検証記録の整理版](verification-records.md)に残した。原ログはGitへ入れない。画像・制作スクリプト・生成に必要な設定・素材の出自は保持。

Quest 3/iPhone 15 Proの端末性能・16人時・実通信の確認は引き続き未完。本番の作業対象として採用したことと、全端末の公開品質を保証したことは区別する。
