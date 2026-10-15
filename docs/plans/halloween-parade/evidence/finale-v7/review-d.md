# 007限定Dレビュー

実装 /root、独立レビュー /root/parade_choreography_consult、2026-10-15。006との差分（先頭専用Emission・街灯方向・撮影）を読み取りのみで確認。重大欠陥なし。

- LeaderPumpkinはCopySerialized後に専用値を設定しGUIDを保持。guide-journeyへの割当を後続処理が上書きしない。
- shaderの追加値default0と非先頭Pumpkin.matの値0を確認。他粒への波及なし。
- 前向き-ZとProp yaw0は整合。旧145–155秒反転は残らず、内部灯と殻も追従する。
- 撮影51は既存コピー経路を使いdirtyBefore/Afterともfalse。

軽微な提案: 非先頭6個の_SurfaceEmission==0を追加assertする余地。今回の実値と再適用コードに問題はなく、追加テストは行わず既存構造検査と実材質の確認を採用。曲の聴感と実機映像はレビュー範囲外。

## 内部灯追加後の限定再確認

同担当が再適用・GUID・後続処理だけ再確認し、重大欠陥なし。元Pumpkin+LanternCoreから毎回生成するためcoreは累積しない。SaveMeshとCopySerializedでGUID維持、colors32再設定あり。ApplyPumpkinsの後のFinishで先頭だけ専用mesh/materialへ戻る。Verify42の通常6+Leader1の合計は7。
軽微な検査提案: Verify47へ先頭mesh一致assertを追加する余地。今回は実際の割当を別読取で確認し、テストコード拡大はしない。内部灯の突出はコードレビューの合格範囲外で、統合画像で別確認する。先の1.25値は途中版、最終値は0.6。

## 容量ログの限定相談

同担当は抽出Editor.logにFailureの具体的理由を見つけられず、計測ツールがBuildAssetBundles戻り値を無視するため、ファイル容量と成功判定を分けるよう助言。再ビルドせずBuildReportを読む提案を採用した。最新raw出力のReportはSucceeded/errors0/warnings2だった。内側ログだけで失敗とも成功とも決めず、対象時刻・出力の一致で記録した。圧縮版の過去Reportへ一般化しない。
