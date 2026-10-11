# 自動散歩カート HTML レビューの確認記録

2026-10-11 の [デザイン案](../../docs/plans/auto-walk-cart/design-v1.html) のブラウザ確認時に保存した画像と `checks.json`。3案・3配色・5方向、寸法表示、隊列表示、回答文の生成・保存、幅390pxでの横溢れを確認した記録。Unity / VRChat 上での検証ではない。

画像やスクリプト内の「肘掛けを細く」は入力欄の動作確認用テキストで、ユーザーの要望・採用決定ではない。A案は推奨段階。採用状況の正本は [Issue](../../docs/development/issues/DES-WLD-004-auto-walk-cart.md)。

`check.cjs` はプロジェクトルートから `node artifacts/cart-design-review/check.cjs` で実行した収集用スクリプト。Windows の既定の Chrome と WebSocket 対応 Node.js を使用し、専用プロファイルとポート9347を使う。JSONの各値は観測値であり、終了コードだけで全項目の合否を判定しない。再実行は記録を上書きする。

ブラウザプロファイルとダミー回答のダウンロードファイルは Git 対象外。
