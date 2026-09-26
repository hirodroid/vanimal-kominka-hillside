# 体験会 (2026-09-26) の記録と Issue への振り分け

2026-09-26 夜、グループインスタンスで裏山の体験会を開いた。この文書はユーザーの口頭報告と VRChat のログから起こした記録で、報告 → Issue の対応表を持つ。仕様の正本は [game-design](../game-design.md)、状態は [Issue サマリ](../development/issues/issue_summary.md)。**この記録でシーン・コードは変えていない。** 端末別・項目別の合否は付けない (不変条件 7)。

## 開催の事実 (ユーザー機のログ `output_log_2026-09-26_19-40-07.txt` から)

| 項目 | 値 |
|---|---|
| ワールド・インスタンス | ［TEST］ 古民家の裏山 (`wrld_a12bc96e`)、グループ (plus)、region jp |
| ビルド | `27e703f` 時点の内容 (広場中心の拠点と仮看板 3 基を撤去済み) を体験会前にアップロード |
| 本編 | 20:02 入室 〜 21:10 全員退出 (約 68 分) |
| 続き | 21:16 〜 21:33、3 人 (ユーザー + 2 人) |
| 同時最大 | **15 人** (20:41、ユーザー含む) |
| 延べ | 20 人 (ユーザー含む) |
| 端末 | ログには残らない。ユーザー報告: **スマホからも参加があり、スマホでも快適だった** (端末種・fps・人数の内訳は未記録) |
| ユーザー機 (PCVR) の fps | ログの統計ダンプ 3 区間で平均 59〜71 |
| ワールド側のエラー | KH_ / KD_ の例外なし。`NullReferenceException` 5 件はいずれも他プレイヤー入室直後の VRChat クライアント側 (難読化された Photon の受信処理) で、ワールドの Udon ではない |
| 遊びの記録 | フリスビーの投球 34 回、ボールの投球 4 回、リード接続あり (ユーザー機の視点のログのみ。他人の分は含まない) |

ログから取れない: 参加者の端末内訳、他人の fps、行き先の操作履歴 (KH_LeashManager は行き先変更をログに出さない)。

## 報告と振り分け

| 報告 (ユーザー) | 事実の補足 | 扱い |
|---|---|---|
| フリスビーが誰のか分からない。犬の顔をプリントしたい | ディスクは犬別 6 枚で識別は色だけ (`KH_FrisbeeDisc_{犬}.mat` の `_Color`)。顔・名前の表示は無い | [IMP-DOG-018](../development/issues/IMP-DOG-018-frisbee-dog-face.md) |
| iOS だとフリスビーの色が全部同じ | 材質は `VRChat/Mobile/Standard Lite` + `_Color`。持っている間だけ `Legacy Shaders/Transparent/Diffuse` の半透明材質に切り替え。iOS で色が落ちる原因は未特定 | [BUG-DOG-005](../development/issues/BUG-DOG-005-ios-disc-colors.md)。VER-WLD-001 の NG 項目として集約 |
| 行き先が犬ごとで決まっていて、プレイヤーごとのローカル選択になっていなかった | コード上は「自分の選択 (ローカル) を自分の接続犬に写す」設計 (`KH_LeashManager.destinations[]` は犬ごとに同期、`_myChoice` はローカル)。体験会でどう見えたかは要再現 | [BUG-DOG-004](../development/issues/BUG-DOG-004-destination-per-player.md) |
| ボールとフリスビーが一緒に置いてあり、同時に使うと犬がどっちに行くか分からない | 置き場は広場の同じ区画 (ディスクはボールの 2.2 m 奥)。同時使用時の裁定は「追走〜持ち帰り中は Fetch を拒む」規則だけで、場に出る表示は無い | [IMP-DOG-019](../development/issues/IMP-DOG-019-ball-and-disc-placement.md) |
| ポータルがスポーン地点の真後ろにあり、間違って入りそう | シーンにワールド側のポータル (`VRC_PortalMarker`) は無い。参加者が落としたポータルがスポーン (`KH_Entrance`) の後ろに置かれた | [IMP-ENV-003](../development/issues/IMP-ENV-003-spawn-and-dropped-portals.md) |
| リードの付け方が分からない。簡単な説明ボードが要る | 仮看板は撤去済みで、現地の説明は無い。手元メニューにも導入の説明は無い | [DES-UI-004](../development/issues/DES-UI-004-walk-destinations-and-boards.md) へ追記、重要度 High へ |
| 広場のリード置き場が遠い | 同日の決定で広場中心の拠点を撤去し、見晴らしの拠点 (広場中心から約 22 m) が広場を兼ねる | [DES-DOG-003](../development/issues/DES-DOG-003-plaza-leash-station.md) |
| 散歩コースが短い。周回コース・川が欲しい。今 2〜3 分のところを 20 分くらいのコースがあってもいい。ワールド自体を広くしてもいい。容量と FPS と合わせて考える | 現行の道は門前〜広場中心 113.9 m。2026-09-19 v4 で「円形の周回路は不要」と決めている。core-concept は「小さな空間でも散歩した感じ」 | [DES-ENV-002](../development/issues/DES-ENV-002-longer-course.md) |
| スマホでも快適だった。15〜16 人入った | 上の表。端末別の測定は無いので、Quest 単体・iOS の合格には読み替えない | [VER-WLD-001](../development/issues/VER-WLD-001-quest-ios-devices.md) へ所感として追記 |

## 次にやること

1. 再現が要る不具合を先に切り分ける: 行き先 (BUG-DOG-004) は 2 人で、iOS の色 (BUG-DOG-005) は iOS 実機で。
2. 説明ボードと拠点の位置 (DES-UI-004 / DES-DOG-003) は現地の配置案を作ってユーザー確認。
3. コース延長 (DES-ENV-002) は容量・負荷の見積りを先に出し、範囲をユーザーが決める。
