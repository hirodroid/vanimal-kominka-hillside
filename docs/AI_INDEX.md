# KominkaHillside -- AI Document Index

> AI エージェント用ナビゲーション入口。
> 編集・実装の前に必ずこのファイルから読み始めること。

## Reading Rules

1. **TIER 0 は常に最初に読む** -- CLAUDE.md (root) + このファイル
2. **タスクに関連する TIER 1 だけ読む** -- 全部読まない
3. **TIER 2 はドメインが関わるときだけ参照**
4. **体験の意図・見た目の判断は core-concept.md、仕様判断は game-design.md に立ち返る**

---

## Document Tree

### TIER 0: AI Gate (必読)

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

見た目の制作・アセット選定・統合確認では `vrc-visual-direction` と `core-concept.md` の「ビジュアル方針」を先に読む。
意図と仕様が食い違ったらコアコンセプトに照らして矛盾を解消し、仕様やパレットを別資料で二重管理しない。

UI制作・改修・レビューは `vrc-udonsharp` §5 と `.claude/skills/vrc-udonsharp/reference/ui/00_START_HERE.md` (プロジェクトルート基準) から必要な章を選び、`world-ui-spec.md` の対象範囲を確認する。

| File | Domain |
|------|--------|
| `development/glossary.md` | 用語集 (ユビキタス言語) |
| `development/design/design_index.md` | 詳細設計の入口 |
| `development/issues/issue_summary.md` | 課題の入口 (状態ごとに 4 分割) |
| `development/git-workflow.md` | Git 運用ルール |
| `development/imports/kominka-autumn-assets.md` | 古民家ドッグランからの秋色資産 (草・葉・遠景の材質と TerrainData) の移植記録。Layer の使い分けもここ |
| `development/imports/kominka-dog-system.md` | 古民家ドッグランからの犬ギミック移植記録 (移植時に作成。書式は古民家側 `C:/work/unity/vcc/KominkaDogRun/docs/development/dog-system-porting.md`) |
| `reference/world/*.png` | 空間構成のイメージ図 (雰囲気の参考。地形図・確定レイアウトではない。犬の頭数・犬種の根拠にもしない) |
| `plans/` | 実装計画書・実機チェックリスト |
| `../.claude/skills/vrc-*/` | 共有 Skills (VRChat World 開発一般の知識・手順) |

---

## 不変条件 (Invariants)

1. **古民家ドッグランの続編である。** 主役は同じ 6 匹 (コーギー / ポメラニアン / ラブラドール / ダックスフンド / 柴犬 / サモエド)。犬種・頭数を増減しない
2. **山から古民家が見える。** 展望・休憩場所から古民家の外観が識別できることを崩す変更をしない。古民家は遠景専用で内部は作らない
3. **犬ギミックは古民家から一式コピーして個別管理する。** 最初から共通ライブラリ化・VPM 化を進めず、同じ修正が繰り返される部分が見えたら共通化を検討する。移植元のタグ・SHA・範囲・差分を `development/imports/kominka-dog-system.md` に残す
4. **片手 3 匹・合計 6 匹のリード接続・範囲内行動は初期範囲に含む。** 追加相談で、自分の接続犬へ目的地を指定し、通過点をたどる自由な散歩を採用方針とした。先行・寄り道・待機・誘い・犬同士の遊びを試作し、前方への固定配置を目標にしない。仕様とスコープは `game-design.md` §4・7 が正本
5. **PC に加え Quest 単体・iOS 対応を目指す。** 「Terrain だから軽い」と判断しない。密度で豪華にせず、眺め・道・犬・古民家とのつながりで作る
6. **散歩道から Terrain の切断面・空白を見せない。** 歩ける範囲の外に景観用の余白を置く
7. **画面は同期しない。** 既存の同期を移植の土台にする。リードの接続先・左右は専用ManagerのOwnerが裁定する初回実装を導入した。複数人・退出・途中参加は未検証。設計と残課題は DES-DOG-001、検証状態は IMP-DOG-002 を参照
8. **企画の原本 (`reference/VAnimal_KominkaHillside_Concept.md`) と購入アセットのフォルダは編集しない。** 秋色などの派生物は `Assets/KominkaHillside/` 側にコピーして管理する

---

## 現在地と次の作業

**2026-09-20 PCVR試作確認**: ユーザーがPlayはひとまず良好、アップロード後PCVRで非常に面白かったと報告。同じ6匹とリード散歩の体験を確認できたプロトタイプとして保存する。各操作・6匹・複数人・Quest単体・iOSの検証が全て済んだ意味ではない。

後から受領した散歩資料を正とする。Terrain v4は「門→登り道→見晴らし→上部の遊び場」の試作。最終コース長・形状・休憩場所数・端処理は未確定。実物6匹を導入しダミー非表示。地形Editor検査53/53、散歩Editor検査74/74の範囲と実機の結果を区別する。

- 現在の品質・改善順: [開発全体像](development/overview.md) §3。
- 最新の到達点 (`f25de23`): 目的地と通過点による自由な散歩 (IMP-DOG-003 手順1〜2)、ボールの広場配置 (BUG-DOG-001 完了)、首輪・リードの本制作 (IMP-DOG-004) を PCVR 1 人で確認し好評。[散歩の記録](plans/2026-09-20-free-walk.md)、[制作票](plans/2026-09-20-leash-asset-production.md)。
- 次の主軸: **DES-UI-001** 古民家型メニュー本体 (目的地選択で仮看板を置き換え、接続補助、集合、ボール集め)。並行して **BUG-DOG-002** 持ち手の向きなどの UX 調整、**DES-WLD-002** 音の設計。順序は [開発全体像 §3](development/overview.md)。
- 次の実装: **BUG-DOG-001** ボールを広場へ、**IMP-DOG-003** 目的地と通過点による自由な散歩・誘い・犬同士の遊びと首輪ずれ。目的地指定に必要な **DES-DOG-001 / DES-UI-001** の契約は試作に先行。並行して **IMP-DOG-001/002・VER-ENV-001** の同期・端末・地形検証。
- その後: **DES-UI-001** 古民家型メニュー・接続補助・集合、**DES-DOG-002** 挙動の違うおもちゃ、**DES-LIV-001** 専用ライブ・既存購入者の利用。**IMP-ENV-001**の景観増量は優先度を下げる。往復導線は **DES-WLD-001**。詳細は [PCVR具体報告](plans/2026-09-20-pcvr-feedback.md)。
- 状態と実行キュー: [Issueサマリ](development/issues/issue_summary.md)。移植の出所は [移植記録](development/imports/kominka-dog-system.md)、ユーザー報告と項目別手順は [散歩検証記録](plans/2026-09-20-leash-walking.md)。

Build は KH_World を作り直す。手修正は設定へ写してから再生成する。数値は Assets/KominkaHillside/Scripts/Editor/KH_TerrainPrototypeConfig.cs が正本。手置きは KH_World 外へ。
Unity 操作前は scripts/unity-api.sh 経由で接続と Application.dataPath を確認する。
