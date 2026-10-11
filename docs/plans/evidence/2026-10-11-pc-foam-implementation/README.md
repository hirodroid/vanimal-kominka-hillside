# PC立体泡・実装候補の検証記録

2026-10-11 / Unity 2022.3.22f1 / Worlds SDK 3.10.5 / Windows Editor D3D11。仕様と採否は [詳細設計 §14](../../2026-10-11-pc-shampoo-foam-design.md#14-実装候補と確認記録2026-10-11)。本フォルダの画像・ローカル検査はPCVR実機・性能・全ゲート合格の証拠ではない。

| ファイル | 対象と限界 |
|---|---|
| report.json | 中心skin、球展開、5支点マスク、薄膜3pass、保存・単体PC bundle後の描画 |
| body-audit.json / generation.json | 6犬種の入力形状監査と生成数・部位・半径 |
| verify.txt | 本番6匹の参照・Bounds・設定・生成入力の鮮度 |
| runtime-checks.txt | 実C#メソッドのEditプローブ。停止中PSに直接Emitして0粒だった診断も残す。fixtureでSimulate後の実測でありUdon動作の代用ではない |
| build-checks.txt | PC/Android/iOS指定でコピーを処理。改名root・誤mesh・stale scaleの拒否を含む。実build callbackの証拠は別 |
| lifecycle-report.json / lifecycle-wash.json | 専用ハーネス経由の局所Udon Play。run 20261011-082239、1ケースPASS。テスト側からPS.Play/Simulateを呼ばず実Emitを確認 |
| scene-diff.json | Apply前の保存シーンとのYAML要素比較。NetworkIDs一致。無関係なTMP fontSize自動変更1件はコミット対象から除外 |
| platform-compile.txt | Android/iOSではPC泡の変数0、Windowsでは12。最後にEditor用コンパイルを復元 |
| bundle-*.json / build-report-*.txt / build-callbacks.txt | 本番sceneのWindows/Android/iOSローカルbundle。今回run・実callback target・6匹・mobile依存除外・最終report成功を照合。mobileのaddedGroupBytes=0は計測省略であり、容量0の実測ではない |
| source-hashes.json | 検査後の入力ファイルSHA256。共有作業ツリーの元sceneに含まれるTMP自動変更はindexから除外しているため、その1件だけ保存sceneとコミットは異なる |
| editor-program-restore.txt / editor-program-disk.json | cross-target検査後のEditor生成物の保存復元。後者は再コンパイルせずディスク上のPC fieldDefinitionsを独立確認 |
| capture.txt / PNG | 固定照明の非保存Editプレビュー。主洗い場の照明・PCVR・水との重なり・実機採否は未確認 |

画像の `new-flat` は法線高さ0.85／半径に対する埋込み0.03の候補。薄膜はBASE/FOAM0/FOAM1。`Shiba-old-full-quarter.png` は比較基準、`Shiba-new-band-quarter.png` は試験帯。最終候補の元出力は `Temp/PCFoamPreview/20261011082057376/`。

Play起動ではSynapticの動的C#コンパイラがSystem.Text.Encoding/Convertの重複参照で最初の呼出を拒否した（Play未開始）。`Temp/foam-research/unity-eval-compat.*` でbase64復号式だけを同じC#文字列リテラルへ変換し、既存 `scripts/unity-api.sh` へ委譲した。専用ハーネスの起動gate・ledger・cleanupは維持。共有スクリプト自体は変更していない。

生成meshのBakeMeshはuseScale=trueで検証。Editの同一update内で複数の姿勢を試す描画プローブに限りforceMatrixRecalculationPerRenderを使用。これは実行コードの設定ではない。単体bundle作成が一時テクスチャをunloadしないよう、プローブ所有物にHideAndDontSaveを設定した。

派生meshは購入犬の元mesh/UV/weightsを入力とするワールド内資産。元GUIDとhashは生成manifestに記録。新しい外部画像・音源を使っていない。購入資産の再配布許諾をこの検証で取得した意味ではない。

Bundle検査では、初回のmanifest非nullだけの判定を採用していない。最終版は各BuildAssetBundles呼出し直後の `Library/LastBuild.buildreport` を読み、Succeeded・errors0・要求platform・固有出力path・report更新時刻を照合する。今回callbackの処理targetと6匹処理、mobileの依存除去完了も必要。Windowsでは生成bundleを読み、期待scene pathを検査する（シーンはロードしない）。nativeログのFailure表記と最終BuildReportの成功は区別し、内部ログの理由は未特定としている。summary日時のKind=UnspecifiedをUTC変換して9時間ずらした検査側の失敗も、成功実績には含めていない。

3target検査後、Editor再コンパイルだけではProgramAssetのディスクがmobile用のfieldDefinitionsを保持することが判明。SDKはCompileSyncで生成物をdirtyにするが保存までは行わないため、共有の復元helperでプロジェクト配下の生成ProgramAssetと参照SerializedProgramだけをSaveAssetIfDirtyした。既存dirtyを持つ状態での次回cross-target検査は開始前に拒否する。復元後のシーンhash/dirtyも確認する。修正は独立レビュー済み。描画・mobile除外の3target検査を繰り返さず、復元helperのC#/Udon成功とディスク内容でこの修正を検査した。ソースhashはこの復元修正後の版。
