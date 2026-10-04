---
id: IMP-WLD-010
type: IMP
system: WLD
status: review
priority: Medium
summary: 手元メニューの写真の面で、いちばん新しい写真の左上に出る「新しい」の印が邪魔なので外す
next: "実装と検査は済み (Photo/2 PASS)。ユーザーが、写真の面を開いても・開いている間に撮れても札が出ないことを確認 (A0-48)"
updated: 2026-10-04
---

# IMP-WLD-010 メニューの写真の「新しい」の印を外す

## 依頼

2026-10-04 ユーザー: メニューの写真ギャラリーの「新しい」のタグのようなものは邪魔だから不要。

## 現状

写真の面を閉じている間に撮れた写真があると、次に開いたとき 1 ページ目の左上 (いちばん新しい写真) に「新しい」の札 (100×30) が写真へ重なって出る。面を開いている間に撮れたときも出る。

## 作業範囲

- [KH_PhotoAlbum.cs](../../../Assets/KominkaHillside/Scripts/KH_PhotoAlbum.cs): `menuNewMark`・`menuNewLabel` と、印のためだけの状態 `_unseen`・`_markShown` を外す (ほかでは使っていない)
- [KH_PhotoGalleryEditor.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_PhotoGalleryEditor.cs): `NewMark` を作らず、既にシーンにある `NewMark` を消す (656 行付近)。必須の文言の一覧から `photo.new` を外す (230 行)
- Verify ([KH_PhotoGalleryEditor.Verify.cs](../../../Assets/KominkaHillside/Scripts/Editor/KH_PhotoGalleryEditor.Verify.cs) 223・276 行) と写真ハーネス ([KH_PhotoHarnessEditor.cs](../../../Assets/KominkaHillside/Scripts/Editor/Testing/KH_PhotoHarnessEditor.cs) 740・753 行) の「印が出る」検査を外す
- 翻訳の `photo.new` (13 言語) は使わなくなる。キーを消すか残すかは実装時に決める (残しても表示には出ない)
- 新しい順に左上から並ぶ動きは変えない

## 完了条件

- [ ] 写真の面を開いても、開いている間に撮れても、印が出ない (ユーザー)
- [ ] Photo/1 → Photo/2 PASS。U# コンパイル エラー 0

## 記録

2026-10-04: 起票。コードの読み取りだけ (Unity は未接続、実装なし)。

2026-10-04 実装 (`2886603`): `KH_PhotoAlbum` から `menuNewMark`・`menuNewLabel`・`_unseen`・`_markShown` を外し、ビルダーがシーンの `PhotoPage/NewMark` を消す (Photo/1 のログ「removed PhotoPage/NewMark」)。Verify に「NewMark が無い」を追加。翻訳の `photo.new` は 13 言語ともキーを残した (表示には出ない)。Photo/2 PASS (失敗 0・警告 0)。
