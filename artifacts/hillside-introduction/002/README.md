# 古民家の裏山 説明用スライド

2026-10-04作成、全8ページ。提供された実プレイ写真6枚を使用。
2026-10-09に旧版・一時ファイルを整理。スライドの内容と完成版PDFは変更していません。

- [完成版PDF](output/KominkaHillside-introduction-8pages.pdf)
- [ブラウザー版・編集正本](KominkaHillside-slides.html)
- [配布用ZIP](output/KominkaHillside-browser-slides-8pages.zip)
- [写真・素材の出典](sources.json)

## 表示と編集

ZIPは展開してからHTMLを開きます。HTMLと同じ階層にassetsフォルダーを置いてください。
「全画面表示」で発表モード、左右キーでページ送り、Escで一覧表示に戻ります。

編集するのはHTMLです。文言・配置・CSS・ページ送り処理を1ファイルに保持しています。
画像はassets内の同梱ファイルを使うため、個人の写真フォルダーや旧版、Unityへの接続は不要です。
sources.json内の撮影元パスは出典の記録であり、表示・編集時には参照しません。

## 再出力

PDFはChromeでHTMLを開き、印刷からPDF保存します。背景のグラフィックを有効、ヘッダーとフッターを無効にしてください。
HTMLの印刷CSSは1600×900の8ページを指定しています。出力後はページ数と文字・写真の収まりを確認します。

配布用ZIPは、この002フォルダーを作業ディレクトリとして次のPowerShellで再生成できます。

    Compress-Archive -LiteralPath 'KominkaHillside-slides.html','assets' -DestinationPath 'output/KominkaHillside-browser-slides-8pages.zip' -Force

## 整理方針

完成版・編集正本・使用画像・出典を保持します。旧版001、PDFの確認用PNG、一時作成スクリプト、未使用画像を削除しました。
旧版の内容はGit履歴で確認できます。生成し直す確認画像やブラウザーキャッシュはqaまたは.cacheに置き、コミット対象から外します。
