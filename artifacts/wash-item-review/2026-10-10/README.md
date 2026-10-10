# 洗浄アイテムの採否・改善チェック

[確認用HTMLを開く](index.html)。ファイル単体で使えるように写真・スタイル・処理を埋め込んでいる。インターネット接続不要。

1. 一覧または前後ボタンで1点ずつ確認する。
2. 「いる／いらない／保留」と改善点、持ったときの違和感を記入する。初期状態は全品未確認。
3. 「回答を保存（JSON）」で回答ファイルをダウンロードし、チャットへ添付して引き継ぐ。「読みやすい一覧を保存」はMarkdown形式。
4. 再開時は同じブラウザーの保存内容を使うか、「回答を読み込む」で保存済みJSONを選ぶ。

ブラウザー保存は補助。file URLやブラウザー設定、ストレージ容量によって利用できない場合は画面に案内を出す。別ブラウザー・別端末へ引き継ぐ場合はJSONを使用する。回答はUnity・Issueへ自動反映されない。

対象は既存の選択可能な17品と、未制作のプール候補。写真は2026-10-08撮影のUnity単体モデル。握った状態の確認画像ではない。支柱や無料道具はこの追加アイテムの採否一覧には含めない。

再生成：`node scripts/art/build-wash-item-review.cjs`。画像番号と名称の対応は `KH_WashSelectionEditor.cs` のNames配列に対応。

関連：[種類別の選択 IMP-DOG-046](../../../docs/development/issues/IMP-DOG-046-wash-tools-by-category.md)、[持ち方 BUG-DOG-010](../../../docs/development/issues/BUG-DOG-010-wash-tool-grip.md)。

## 回答受領（2026-10-10）

[ユーザー回答JSON](vanimal-wash-review-2026-10-10T07-47-47-617Z.json)を原文のまま保存。18点すべて回答済み（keep 10点＝既存9品＋プール、remove 8点）。[採否と改善点の整理](../../../docs/development/issues/DES-DOG-008-wash-paid-tools-and-effects.md)へ反映した。実装は[IMP-DOG-047](../../../docs/development/issues/IMP-DOG-047-wash-item-review-changes.md)等で未着手。HTMLへは「回答を読み込む」でこのJSONを選べば再表示できる。

## 確認記録（2026-10-10）

- JavaScript構文、18点・未確認の初期値、17写真と元画像のバイト一致を確認。
- DOMを模擬したJavaScript実行で、個別入力・前後移動・ローカル保存・JSON書出し/読込み・Markdown書出し・絞り込み・プール表示を確認。不正JSONで既存入力を保持し、保存不可時の警告を保持することも確認。
- 独立レビュー：wash_html_review（静的・読み取りのみ）。読込成功文が保存失敗警告を消す指摘を修正し、失敗するストレージで再検査した。
- 接続可能なブラウザーがないため、実ブラウザーの表示・クリック・ファイルダウンロードは未確認。DOM模擬検査を実画面の合格に読み替えない。
- Unity・モデル・既存シーンは変更していない。
