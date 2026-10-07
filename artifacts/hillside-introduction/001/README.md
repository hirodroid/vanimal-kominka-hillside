# 古民家の裏山 説明用スライド

2026-10-04 作成。古民家ドッグラン紹介資料 `artifacts/kominka-introduction/004` の配色、1600×900の画面、文字サイズ、写真中心の構成、VAnimalのクレジットを継承。

- [PDF・全8ページ](output/KominkaHillside-introduction-8pages.pdf)
- [ブラウザー版・編集正本](KominkaHillside-slides.html)
- [ブラウザー版と画像一式](output/KominkaHillside-browser-slides-8pages.zip)

## 使い方

HTMLをブラウザーで開き、下部の「全画面表示」を押す。左右キーまたは画面下の矢印でページを送る。Escで一覧表示へ戻る。ZIPは展開してからHTMLを開く。

## 構成

表紙／リードで散歩／フリスビー・おもちゃ／川遊び／犬を洗う／景色と休憩／自動写真／できること一覧。

本文は `docs/core-concept.md`、`docs/game-design.md`、`docs/development/overview.md` の2026-10-04時点の記録に基づく。ライブは制作待ちのため掲載せず、前作の添い寝や常時描画の犬目線モニターも含めない。未確認の端末対応、性能、公開状況を保証する説明は入れない。

画像はプロジェクトに保存された制作画面。表紙は2026-09-20の柴犬と首輪の撮影、景観・設備は2026-10-04の記録を使用。犬の紹介サムネイルは前作の資料から引き継いだ。詳細な元ファイル対応は `qa/sources.json`。自動写真ページの画像は景観の紹介で、実際の自動撮影写真やギャラリー画面ではないことをページ内に記載。Unityへ新規撮影の接続を確認したが応答が得られず、既存の記録画像を使用した。

PDFはHTMLからChrome headlessで出力。全8ページをWindows.Data.Pdfで1600px幅に再描画し、文字の収まり、画像、ページ番号を目視確認。PowerPoint形式ではなく、HTMLが編集正本。`qa` は制作・検証用で配布ZIPに含めない。
