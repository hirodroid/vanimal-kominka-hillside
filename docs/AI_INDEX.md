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

## 素材の確認記録

新規・変更素材は [共通制作手順](../.claude/skills/vrc-asset-production/references/source-rights.md) の型を既存記録に統合する。購入・移植素材は `development/imports/` の該当記録、自作は `plans/` の該当制作票を使う（例: [リード制作票](plans/2026-09-20-leash-asset-production.md)）。そこから参照元・条件の根拠・確認日・生成履歴・採用前結果へたどれるようにする。別台帳は増やさず、記録がない新素材は制作票に節を作る。既存記録の技術検証・自作表記を権利確認済みとは読み替えない。

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
- 最新の着手: **DES-UI-001 / DES-DOG-001** の合意を実装 (手元メニュー 3 タブ、リード拠点、集合、ボール個別回収、共通購入権)。Editor 検査済み、Play/PCVR 待ち。[記録とチェックリスト](plans/2026-09-20-menu-gather-stations.md)。
- 並行: **BUG-DOG-002** 持ち手の向き、**DES-WLD-002** 音の設計。順序は [開発全体像 §3](development/overview.md)。
- **IMP-DOG-005 フリスビー**: 本シーンの散歩のラブが取れる (広場限定、照準モード、位置取り、メニュー「おもちゃ」の「フリスビー」)。PCVR 1 人で好評。項目別・2 人・他犬・Quest/iOS は未。調整用の試作シーン `Assets/Scenes/KH_FrisbeePrototype.unity` と `Tools/KominkaHillside/Frisbee/` は残す。[記録とチェックリスト](plans/2026-09-20-frisbee-prototype.md)。
- **フリスビーの次の制作 (2026-09-21 合意 → 着手)**: 通常版の体験はひとまずOK。[IMP-DOG-008](development/issues/IMP-DOG-008-frisbee-variations.md) でラブ1匹の走り込み 3 候補・ひねり・横曲がりを、手元メニューのヘッダの開発用切替 (6 プリセット) で比べる。**PCVR 1 回目は「全然取らない」→ 先回り型キャッチ (投げた瞬間に点を決め 10 m/s で先回り → 真下で微調整 → 真上に跳ぶ) へ作り替え、PCVR 2 回目「かなり良かった」(跳んで取った 29 回 / MISS 3 回)。3 回目に向けて 待機中の向き直り (真の原因 = 停止中の旋回 30°/s)・ブレ・並走の追い抜きを修正、ClientSim ハーネス (`scripts/kh-frisbee-harness.cjs`) を導入し、回帰と「ひねりが取れない」真の原因 (購入犬の胴体がルートに遅れて追従する ParentConstraint) を修正、PCVR 3 回目「機械的」(瞬間移動・急旋回・ポンと跳ぶ) を受けて、回り込み経路・追いながら跳ぶ (正面も可)・なだらかな加減速・機械っぽさの数値判定へ作り替え。ClientSim 4 回目「ひねり以外はだいぶいい」→ ひねりが跳ばない原因 (横の口オフセットで助走の内側に居て、ため開始点が後ろへ滑る) を修正、**ハーネス 30/30 PASS**、Verify 試作 61 / Walking 161 / Menu 27。Build & Test と PCVR 5 回目待ち**。設計・分担・チェックリストは [記録](plans/2026-09-21-frisbee-variations.md)。通常版と採用バリエーションが1匹でOKになってから6匹展開。005の未確認項目は継続する。
- **IMP-ENV-002 空とナイトモード**: 空を古民家と同じ Allsky キューブマップに、手元メニューのヘッダーに OFF/ON のナイトモード (自前の乗算オーバーレイ、RBS 不使用)。Editor 検査済み、Play 待ち。[記録とチェックリスト](plans/2026-09-21-sky-and-night-mode.md)。
- 状態と実行キュー: [Issueサマリ](development/issues/issue_summary.md)。移植の出所は [移植記録](development/imports/kominka-dog-system.md)、ユーザー報告と項目別手順は [散歩検証記録](plans/2026-09-20-leash-walking.md)。

Build は KH_World を作り直す。手修正は設定へ写してから再生成する。数値は Assets/KominkaHillside/Scripts/Editor/KH_TerrainPrototypeConfig.cs が正本。手置きは KH_World 外へ。
Unity 操作前は scripts/unity-api.sh 経由で接続と Application.dataPath を確認する。
