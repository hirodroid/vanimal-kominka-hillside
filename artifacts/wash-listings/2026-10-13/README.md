# 洗い場追加アイテム — 現行Listing画像

2026-10-13、現在のUnity visual Prefabから再撮影。**1024×1024 PNG、価格なし、犬モデルなし**。公開・販売登録は行っていない。

| セット | 画像 | 内容 |
|---|---|---|
| 泡 | [01-foam.png](01-foam.png) | 泡銃・ミトン |
| 水 | [02-water.png](02-water.png) | じょうろ・水鉄砲・ホース |
| 仕上げ | [03-finish.png](03-finish.png) | ドライヤー・タオル |
| 洗い場 | [04-station.png](04-station.png) | 檜台・プール |
| 全部入り | [05-complete.png](05-complete.png) | 上記9品 |

[5枚のZIP](wash-listings-1024.zip) / [撮影・ハッシュ記録](manifest.json) / [6ロールのモデルレビューと対応](model-review.md)

## 今回の更新

- 提供対象は `KH_WashSelectionEditor.Category.cs` の `Order={0,9,6,17,18,7,8,13,19}`。旧17品の `catalog.json` は撮影対象の正本ではない。
- 現行Prefab・材質を使用し、泡銃の修正版を含めて撮り直した。画像用のモデル改造や生成画像による補正はしていない。
- 全部入りは5列から3×3へ変更し、モデルの最大表示寸法を約1.68倍にした。水の説明を画像の読む順「じょうろ・水鉄砲・ホース」へ合わせた。
- `Assets/KominkaHillside/Data/WashPremium/Photos/` の提供中9品の単体画像と5セット画像も再生成。セットは同じPNG、実行時インポートは最大512・Android ASTC 6×6。
- 撮影メニューは `Tools/KominkaHillside/WashPremium/41 - Photograph Listings`。出力先をこの日付へ変更した。過去の撮影フォルダは保全している。

## 検証と範囲

- C#コンパイル：エラー0。撮影ログの現在時刻と出力先を確認。
- 5枚とも1024角、単体9枚は512角。5セットは実行用画像とSHA256一致。
- 5セット・単体9品をU/X/V各担当が目視。変更後の全部入りはU/Vが再確認し、9品・重なりなし・欠けなし。水の説明順はUが再確認。撮影コード差分はMが確認。
- シーン、Prefab、共有フォント、過去の画像・記録など開始時に取得した76ファイルはSHA256不変。撮影後もEdit mode・シーンdirty=false。
- 一時フォントのUnderlineグリフ不足の警告が出た。画像には下線を使用せず、本文の全文字は撮影前チェックを通過、出力画像も目視確認した。共有フォントは変更していない。
- 最新のワールド内使用写真・PCVR左右の保持・Quest/iOSの見え方は今回未検証。物撮りは実寸比を表さず、実機の使い心地や性能の合格証拠ではない。

制作仕様の履歴は[10月8日の記録](../2026-10-08/README.md)、関連Issueは[IMP-DOG-043](../../../docs/development/issues/IMP-DOG-043-wash-premium-selection.md)。
