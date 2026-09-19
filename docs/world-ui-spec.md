# KominkaHillside -- ワールド固有UI設定

配置先：`docs/world-ui-spec.md`。共通資料1.0.0の09章を基にしたワールド固有の設定。Unityへ自動インポートするスキーマではない。
全体のビジュアル方針は `core-concept.md` が正本。この資料は参照先・更新日とUIへの具体化を持ち、全体パレットを二重管理しない。既存のUI仕様が別名ならそちらを正本とし、この資料は案内にする。
新規・全面改修では全セクションを確認。S変更は該当箇所と決定記録だけ更新する。不要は理由付き `not_applicable`、不明は `unknown`、仮決定は `provisional`。

## 1. 識別と環境

| 項目 | 値 |
|---|---|
| world / UI scope | KominkaHillside (古民家の裏山) / 古民家ドッグランの既存 UI を移植して使う。新規 UI は増やさない方針 (provisional) |
| この設定の版・最終更新日 | 0.1 / 2026-09-19 (立ち上げ時の識別情報のみ。UI 制作開始時に埋める) |
| 共通リファレンスの版・パス | 1.0.0 / `.claude/skills/vrc-udonsharp/reference/ui/00_START_HERE.md` (プロジェクトルート基準) |
| 対象シーン・Prefab・ゲーム処理 | `Assets/Scenes/VRCDefaultWorldScene.unity` / 移植する犬ギミックの UI (IMP-DOG-001 で棚卸し) |
| Unity / Worlds SDK / TMP / ClientSim | 2022.3.22f1 / 3.10.4 / 3.0.6 / SDK 同梱 |
| ビルドターゲット | Windows 確定。Android / iOS はスマホ対応を目指す (実機で判断。`game-design.md` §1) |
| 対象機器・入力 | PCVR / Desktop / Android Touch / iOS Touch / その他 |
| 対応言語 | 日本語 (古民家と同じ。provisional) |
| 主目的・主要操作・戻る活動 | unknown |
| UIの影響範囲 | 個人 / 共有 / 混在、項目別に定義 |
| 検証の到達点 | documentedのみ等。根拠に応じて記入 |

## 2. ビジュアル方針との接続

| 必要情報 | 参照ファイル・節・版 | UIへの解釈 | 状態 |
|---|---|---|---|
| コアコンセプト | `core-concept.md` §1〜2 (2026-09-19) | 犬が主役。UI は古民家のトークンを継承し、案内は最小 | provisional |
| 主役と背景、避ける遮蔽 | unknown | unknown | missing |
| パレットと明暗 | `core-concept.md` §3 役割別パレット (2026-09-19) | 暗い面 + 白文字を継承。状態は色以外でも区別 | provisional |
| 形状・角・輪郭 | unknown | unknown | missing |
| 素材・模様・デフォルメ | unknown | unknown | missing |
| 昼夜・照明・演出時の背景 | unknown | unknown | missing |
| UI文字とアイコンの方針 | unknown | unknown | missing |

検出規則：参照が存在しない→missing。項目はあるが具体的なUI役割に変換できない→missing。複数資料が矛盾→conflict。仮の解釈で試作→provisional。**ファイル名が埋まっただけではreadyにしない。**

不足への対応：`止める範囲 / 進める試作 / 決定者または必要証拠 / 次に見直す工程` を記載。

## 3. 役割トークン

| トークン | 値 | 単位・色空間・対象条件 | 出典／決定理由 | 状態 |
|---|---|---|---|---|
| surface.panel | unknown | sRGB色＋alpha、背景条件 | unknown | unknown |
| surface.raised | unknown | sRGB色＋alpha | unknown | unknown |
| text.primary / secondary | unknown | sRGB色、適用面 | unknown | unknown |
| action.primary / onPrimary | unknown | 色を対で定義 | unknown | unknown |
| selection.surface / marker | unknown | 色＋形・ラベル | unknown | unknown |
| focus.ring | unknown | 色、実装、厚みの単位 | unknown | unknown |
| disabled.surface / text | unknown | 理由は可読性を保つ | unknown | unknown |
| status.success / warning / error | unknown | 色＋文言・アイコン | unknown | unknown |
| font.heading / body / numeric | unknown | font asset、weight、fallback | unknown | unknown |
| type.heading / body / label / auxiliary | unknown | TMP単位、実効scale、実距離、測った字高 | unknown | unknown |
| spacing.inset / item / group | unknown | Canvas設計単位、実効scale | unknown | unknown |
| shape.radius / border | unknown | Sprite borderとRect、厚み・最小寸法 | unknown | unknown |
| motion.transition / reduced | unknown | 秒、入力方式、軽減時の振る舞い | unknown | unknown |

トークンの循環参照、未定義の参照、色の組合せ不足を確認する。値はテーマの正本を1つ決める。Inspector直修正と生成設定のどちらが正か曖昧にしない。

## 4. 画面と情報構造

| screen ID | 目的・利用場面 | 常時／必要時／詳細 | パターン | 主要操作 | 入る／戻る／閉じる | 保持する文脈 |
|---|---|---|---|---|---|---|
| unknown | unknown | unknown | unknown | unknown | unknown | unknown |

初回利用者への手がかり：unknown  
継続利用者への近道・復元：unknown  
会話・犬・戦闘を見る空間：unknown  
この画面に入れない情報と理由：unknown

## 5. 操作と状態契約

| action ID | 対象ID | 表示範囲 | 変更する状態 | 実行者・書込者 | 実行可能条件 | 消費・結果 | 確認／取消 |
|---|---|---|---|---|---|---|---|
| unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown |

| 状態軸 | このUIで使う値 | 読込・初期状態 | 根拠になるデータ |
|---|---|---|---|
| interaction | normal / focus / pressed等 | unknown | 入力 |
| selection | 選択中のtab / item / target | unknown | ローカルUI状態 |
| domain | owned / equipped / running等 | unknown | ゲーム・CE・共有状態 |
| availability | available / reason | unknown | 条件判定 |
| request | idle / pending / succeeded / rejected / unknown | unknown | 要求結果 |

代表状態の表示仕様：

| 状態・組合せ | 文言 | 色以外の手がかり | 操作可否 | 出口・回復 |
|---|---|---|---|---|
| 状態未取得 | unknown | unknown | unknown | unknown |
| 実行可能 | unknown | unknown | unknown | unknown |
| 選択中＋実行可能 | unknown | unknown | unknown | unknown |
| 資源・権限不足 | unknown | unknown | unknown | unknown |
| 処理中 | unknown | unknown | unknown | unknown |
| 完了 | unknown | unknown | unknown | unknown |
| 失敗・結果不明 | unknown | unknown | unknown | unknown |
| 対象消失・権限失効 | unknown | unknown | unknown | unknown |

進捗を使う場合：`意味 / 現在値 / 分母 / 単位 / 値の更新元 / 不明時 / 0% / 100% / 操作可能性 / 実装手段` を記録。不要ならnot_applicable。

## 6. 個人・共有・永続化

| 状態キー | 個人／共有 | 表示対象 | 管理者 | 同期手段 | 保存手段・寿命 | 初期復元 | 版・移行 |
|---|---|---|---|---|---|---|---|
| unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown |

共有操作があれば記入：

- 採用順序・競合方式：unknown
- 同時要求、重複要求、観測revision不一致：unknown
- 操作権とObject Ownerの関係：unknown
- 開始者・owner・担当者の退出／中断復帰：unknown
- 途中参加者への復元元：unknown
- 待機・取消・終了・復旧：unknown
- 個人の消音／演出軽減と共有進行の関係：unknown

CEがあれば記入：

- listing/product/用途の対応：unknown
- 権利読込、確定、失効、数量：unknown
- UI非表示中の受信Behaviour：unknown
- 公式購入画面へ移って戻る時の状態：unknown
- 無料ポイントとCEの表示・処理の区別：unknown

## 7. 端末・空間・入力

| モード | UI配置 | 開く | 決定 | 戻る | 閉じる | 説明を見る | 特殊条件 |
|---|---|---|---|---|---|---|---|
| PCVR | unknown | unknown | unknown | unknown | unknown | unknown | 左右、片手、座位、Pickup、アバターサイズ |
| Desktop | unknown | unknown | unknown | unknown | unknown | unknown | 移動・既存ショートカットとの競合 |
| Android Touch | unknown | unknown | unknown | unknown | unknown | unknown | 通常表示／Focus View、外部入力、縦横 |
| iOS Touch | unknown | unknown | unknown | unknown | unknown | unknown | 読込ビルド種別、縦横 |

計測記録：`端末名 / 解像度・向き / HMD設定 / 入力 / Canvas寸法 / 実効scale / 面までの距離 / 字形実寸 / target寸法・間隔 / 視角 / 背景 / 結果`。
Focus Viewの設定：unknown。通常入口：unknown。再配置・見失い復帰：unknown。

## 8. Unity部品と更新

| 部品・Prefab | 責務 | 寸法管理者 | Raycast | 更新契機 | 常駐／非表示 | ワールドoverride |
|---|---|---|---|---|---|---|
| unknown | unknown | unknown | unknown | unknown | unknown | unknown |

| UnityEvent | 対象Behaviour | メソッド／イベント名 | action ID | 実行確認 |
|---|---|---|---|---|
| unknown | unknown | unknown | unknown | NOT_TESTED |

| API・機能 | Unity出典 | VRChat許可 | Udon露出確認の版 | コンパイル | 実クライアント |
|---|---|---|---|---|---|
| unknown | unknown | unknown | unknown | NOT_TESTED | NOT_TESTED |

更新方針：値が変わった時／必要な周期／常時の理由：unknown  
CPU・GPU・メモリの予算と比較条件：unknown（初期値を根拠なく設定しない）  
同期対象のスキーマ・Network IDの保全：unknown

## 9. レビューと仮設定の記録

| decision ID | 項目・判断 | 状態 | 適用ルール | 理由・反例 | 影響 | 解消に必要な証拠 | 見直す工程 |
|---|---|---|---|---|---|---|---|
| D-001 | unknown | provisional | unknown | unknown | unknown | unknown | 試作後等 |

| test ID | 条件・操作 | 期待結果 | 実測・観察 | 判定 | 証拠・版 |
|---|---|---|---|---|---|
| T-001 | unknown | unknown | 未実施 | NOT_TESTED | unknown |

判定はPASS / FAIL / NOT_TESTED / N_A（理由必須）。実機未所持はNOT_TESTED。S変更の場合、既存証拠を再利用する範囲と、今回更新した証拠を短く書く。

## 10. 設定の充足判定

- 対象画面について主目的と主要操作が書けている。
- 使用する役割トークンが具体値または追跡可能な参照で解決できる。
- 各数値に単位・適用する端末/距離/scale・根拠種別がある。
- 操作に対象・権限・状態・結果・復帰が対応する。
- 対応すると記載した全入力で入口と出口がある。
- 共有/CE/保存のunknownは、実処理接続前に解消する。
- provisionalとNOT_TESTEDをready/PASSに取り違えていない。

この判定は文章による契約。エージェントへ渡す時に自動検査コードが存在すると誤認させない。必要なら既存のワールド検証ツールへ未定義トークン・接続欠落等の検査を追加する。
