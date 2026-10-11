# フリスビー HTML レビューの確認記録

2026-10-11 の [モデル改善案](../../../docs/plans/2026-10-11-frisbee-model-review.html) のブラウザ確認時に保存した画像と `qa.json`。モデル案・底面表示・犬種の切替、回答文の生成、幅390pxでの横溢れを確認した記録。Unity / VRChat 上での検証ではない。

画像・スクリプトのB/C案選択と「縁を少し柔らかく」は操作確認用の入力で、ユーザーの採用決定ではない。画像は検査時点のスナップショット。採用状況はリンク先の設計資料を参照する。

`check-browser.cjs` はプロジェクトルートから `node artifacts/frisbee-model-review/2026-10-11/check-browser.cjs` で実行した収集用スクリプト。Windows の既定の Chrome と WebSocket 対応 Node.js を使い、専用プロファイルで起動する。再実行は記録を上書きする。ブラウザプロファイルは Git 対象外。
