# KominkaHillside -- AI Document Index

> AI エージェント用ナビゲーション入口。
> 共通指示はCLAUDE.md。対象が分かる作業は必要な資料・節だけ読む。

## Reading Rules

1. **CLAUDE.mdを共通入口にする**。この索引は仕様判断・作業選択・資料探索時に読む。対象Issueが既知なら個票と参照先の該当節へ直行し、仕様・機能変更時は下の不変条件を確認する
2. **タスクに関連する TIER 1 だけ読む** -- 全部読まない
3. **TIER 2 はドメインが関わるときだけ参照**
4. **体験の意図・見た目の判断は core-concept.md、仕様判断は game-design.md に立ち返る**

---

## Document Tree

### TIER 0: AI Gate (作業入口)

| File | Role |
|------|------|
| `../CLAUDE.md` | プロジェクト規約・命名規則・検証ワークフロー |
| `AI_INDEX.md` | このファイル。Tier 分類 + Reading Rules + 不変条件 |

### TIER 1: Authoritative (Single Source of Truth)

| File | Domain | Role |
|------|--------|------|
| `reference/VAnimal_KominkaHillside_Walking_Addendum.md` | 企画 (優先資料) | 後から受領した 11 項目。ユーザー指定により旧資料・以前の会話・試作より優先。未確定事項は固定しない |
| `reference/VAnimal_KominkaHillside_Concept.md` | 企画 (旧原本) | 旧企画の履歴。AI は編集しない。食い違う場合は後から受領した散歩企画資料を優先 |
| `core-concept.md` | 判断基準・ビジュアル | 原本から導いた体験の核・判断基準とビジュアル方針 (色・形・素材・光・UI・例外) の正本 |
| `game-design.md` | 仕様 | コアコンセプトから導く仕様・スコープの正本 |
| `world-ui-spec.md` | UI仕様 | ビジュアル方針から具体化するUIトークン・画面・状態/入力契約・検証記録。既存の同等資料があればそちらを正本にする |
| `development/overview.md` | 開発全体 | マイルストーンの進捗・完成条件 |

### TIER 2: Reference

見た目の制作・アセット選定・統合確認では `vrc-asset-production` の references/visual-direction.md と `core-concept.md` の「ビジュアル方針」を先に読む。
意図と仕様が食い違ったらコアコンセプトに照らして矛盾を解消し、仕様やパレットを別資料で二重管理しない。

UI制作・改修・レビューは `vrc-world-ui` と `.claude/skills/vrc-world-ui/reference/ui/00_START_HERE.md` (プロジェクトルート基準) から必要な章を選び、`world-ui-spec.md` の対象範囲を確認する。

| File | Domain |
|------|--------|
| `development/glossary.md` | 用語集 (ユビキタス言語) |
| `development/issues/issue_summary.md` | 課題の一覧 (frontmatter から `check-issues.js --write` で生成。運用は同じ場所の README.md) |
| `development/git-workflow.md` | Git 運用ルール |
| `development/imports/kominka-autumn-assets.md` | 古民家ドッグランからの秋色資産 (草・葉・遠景の材質と TerrainData) の移植記録。Layer の使い分けもここ |
| `development/imports/kominka-dog-system.md` | 古民家ドッグランからの犬ギミック移植記録 (移植時に作成。書式は古民家側 `C:/work/unity/vcc/KominkaDogRun/docs/development/dog-system-porting.md`) |
| `reference/world/*.png` | 空間構成のイメージ図 (雰囲気の参考。地形図・確定レイアウトではない。犬の頭数・犬種の根拠にもしない) |
| `plans/` | 実装計画書・実機チェックリスト |
| `../.claude/skills/vrc-*/` | 共有 Skills (VRChat World 開発一般の知識・手順) |

---

## 素材の確認記録

新規・変更素材は [共通制作手順](../.claude/skills/vrc-asset-production/references/source-rights.md) の型を既存記録に統合する。購入・移植素材は `development/imports/` の該当記録、自作は `plans/` の該当制作票を使う（例: [リード制作票](plans/2026-09-20-leash-asset-production.md)）。そこから参照元・条件の根拠・確認日・生成履歴・採用前結果へたどれるようにする。別台帳は増やさず、記録がない新素材は制作票に節を作る。既存記録の技術検証・自作表記を権利確認済みとは読み替えない。

## 不変条件 (Invariants)

1. **古民家ドッグランの続編である。** 主役は同じ 6 匹 (コーギー / ポメラニアン / ラブラドール / ダックスフンド / 柴犬 / サモエド)。犬種・頭数を増減しない
2. **山から古民家が見える。** 展望・休憩場所から古民家の外観が識別できることを崩す変更をしない。古民家は遠景専用で内部は作らない
3. **犬ギミックは古民家から一式コピーして個別管理する。** 最初から共通ライブラリ化・VPM 化を進めず、同じ修正が繰り返される部分が見えたら共通化を検討する。移植元のタグ・SHA・範囲・差分を `development/imports/kominka-dog-system.md` に残す
4. **片手 3 匹・合計 6 匹のリード接続・範囲内行動は初期範囲に含む。** 追加相談で、自分の接続犬へ目的地を指定し、通過点をたどる自由な散歩を採用方針とした。先行・寄り道・待機・誘い・犬同士の遊びを試作し、前方への固定配置を目標にしない。仕様とスコープは `game-design.md` §4・7 が正本
5. **PC に加え Quest 単体・iOS 対応を目指す。** 「Terrain だから軽い」と判断しない。密度で豪華にせず、眺め・道・犬・古民家とのつながりで作る
6. **散歩道から Terrain の切断面・空白を見せない。** 歩ける範囲の外に景観用の余白を置く
7. **画面は同期しない。PCVR の総合評価を、項目別・端末別の合格や性能数値に読み替えない。** 既存の同期を移植の土台にし、リードの接続先・左右は専用 Manager の Owner が裁定する。ユーザーの PCVR 評価は多くが総合評価 (例: 2026-09-22 の 6 人散歩は項目別の合否が未記録) なので、完了条件の各項目・Quest 単体・iOS ([VER-WLD-001](development/issues/VER-WLD-001-quest-ios-devices.md))・ClientSim や Editor の結果とは分けて記録する。この但し書きの正本はここで、他の文書は「(不変条件 7)」で参照する。同期の設計と残課題は DES-DOG-001
8. **企画の原本 (`reference/VAnimal_KominkaHillside_Concept.md`) と購入アセットのフォルダは編集しない。** 秋色などの派生物は `Assets/KominkaHillside/` 側にコピーして管理する

---

## 対象ごとの参照先

**現在地と次の作業の本文は [開発全体像](development/overview.md) §3 だけが持つ。** 実行キューと状態は [Issueサマリ](development/issues/issue_summary.md)。下表は対象ごとの参照先の案内で、進捗・状態・検査件数はここへ書かない (個票・記録を更新する)。過去の PASS を現在版の合格へ読み替えない。

| 対象 | 記録・未確認条件の参照先 |
|---|---|
| 散歩・首輪・リード | [自由な散歩](plans/2026-09-20-free-walk.md)、[散歩検証](plans/2026-09-20-leash-walking.md)、[制作票](plans/2026-09-20-leash-asset-production.md) |
| 手元メニュー・集合・拠点 | [旧メニューの実装とチェックリスト](plans/2026-09-20-menu-gather-stations.md)、[古民家の器への作り替え計画](plans/2026-09-23-menu-rebuild.md)、[IMP-UI-001](development/issues/IMP-UI-001-menu-table-driven-and-settings.md) / [IMP-PRG-001](development/issues/IMP-PRG-001-ledger-shopkeeper-points.md) / [IMP-DOG-014](development/issues/IMP-DOG-014-treats-port.md) / [015](development/issues/IMP-DOG-015-accessories-and-petfx-port.md) / [016](development/issues/IMP-DOG-016-call-pose-port-and-slope.md) |
| フリスビー | [本シーン導入](plans/2026-09-20-frisbee-prototype.md)、[動きの改修経緯](plans/2026-09-21-frisbee-variations.md)、[6 匹展開](plans/2026-09-24-frisbee-six-dogs.md)、[ハーネスの手順・最新記録](testing/kh-frisbee-harness.md)、[BUG-DOG-003 非マスターの投球](development/issues/BUG-DOG-003-frisbee-non-master-throw.md) |
| 空・ナイトモード | [実装とチェックリスト](plans/2026-09-21-sky-and-night-mode.md) |
| 容量 (ビルドサイズ) | [ビルドサイズ実測](plans/2026-09-24-build-size-audit.md)、計測メニュー `Tools/KominkaHillside/Build Size/`、手順と罠は `vrc-performance` のビルドサイズチェック。素材を足したら `7 - Apply` → `2 - Bundle Size Android` |
| Quest単体・iOS | [端末検証](development/issues/VER-WLD-001-quest-ios-devices.md) |
| 体験会 (複数人・スマホ参加) | [2026-09-26 の記録と振り分け](plans/2026-09-26-group-playtest.md)。端末別の合格には読み替えない (不変条件 7) |
| 地形・境界・負荷 | [IMP-ENV-001](development/issues/IMP-ENV-001-hillside-polish.md)、[完了した地形判断](development/issues/issue_summary.md) |
| 犬ギミックの出所 | [移植記録](development/imports/kominka-dog-system.md) |

Build は KH_World を作り直す。手修正は設定へ写してから再生成する。数値は Assets/KominkaHillside/Scripts/Editor/KH_TerrainPrototypeConfig.cs が正本。手置きは KH_World 外へ。
Unity 操作前は scripts/unity-api.sh 経由で接続と Application.dataPath を確認する。
