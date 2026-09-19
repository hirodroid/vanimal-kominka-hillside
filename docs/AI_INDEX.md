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
| `reference/VAnimal_KominkaHillside_Concept.md` | 企画 (原本) | ユーザー作成の企画・引き継ぎ資料 (2026-09-19 合意)。**AI は編集しない。** 導出文書と食い違ったらこちらを優先し、下 2 枚を直す。**例外: 原本より後のユーザー決定** — `game-design.md` に日付付きで追補されたものが原本より優先 (原本は書き換えないため) |
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
3. **犬ギミックは古民家から一式コピーして個別管理する。** 共通ライブラリ化・VPM 化・自動同期をしない。移植元のタグ・SHA・範囲・差分を `development/imports/kominka-dog-system.md` に残す
4. **新しい大規模ギミック・専用散歩 AI は初期範囲に入れない。** スコープは `game-design.md` §7 が正本
5. **スマホ対応を目指す。** 「Terrain だから軽い」と判断しない。密度で豪華にせず、眺め・道・犬・古民家とのつながりで作る
6. **散歩道から Terrain の切断面・空白を見せない。** 歩ける範囲の外に景観用の余白を置く
7. **画面は同期しない。** 同期は古民家の設計 (犬の位置 = Owner 権威、ボール、状態) を引き継ぎ、新しい同期を設計しない
8. **企画の原本 (`reference/VAnimal_KominkaHillside_Concept.md`) と購入アセットのフォルダは編集しない。** 秋色などの派生物は `Assets/KominkaHillside/` 側にコピーして管理する

---

## 最初に着手すべきこと

<!-- 現在地と着手順をここに書く。セッションのたびに更新する。 -->

**現在地 (2026-09-19 夜)**: Terrain 試作 (VER-ENV-001) **v2** (門から出発する配置。ユーザーの手作業を把握して再パラメータ化) を生成し、Editor 検証 50/50・View Shots で一次判定済み。**次はユーザーの Play** ([計画 §10](plans/2026-09-19-terrain-prototype.md))。**Build は手作業を消す**: 手で直したい値は設定へ写してから Build する (手作業の参照コミット `13a7e31`)。舞台は `Assets/Scenes/VRCDefaultWorldScene.unity` の `KH_World` 配下 (Build で作り直す。手で置くものはその外)。数値の正本は `Assets/KominkaHillside/Scripts/Editor/KH_TerrainPrototypeConfig.cs`。Unity 2022.3.22f1 / SDK 3.10.5 / Synaptic AI Pro / LMArt / モサンゴ屋 導入済み。

着手順 (Issue は `development/issues/issue_summary.md`):

1. **DES-ENV-001 (着手済み)** まず Terrain。切り替え基準は Issue に記録済み (2026-09-19)
2. **VER-ENV-001** 試作 (**生成済み。Play 待ち**): Terrain + 秋の広葉樹 + 古民家の外観 + 犬サイズのダミーで、眺め・歩行・端の見え方・実機負荷を確認する。`core-concept.md` §3 の代表シーン検証を兼ねる。作り直しは `bash scripts/kh-terrain-loop.sh B`
3. **DES-ENV-001 (確定)** 試作の実測で方式を決め、`game-design.md` §4 を更新する
4. **IMP-DOG-001** 地形方式が決まってから、古民家側にタグ `export/kominka-hillside-v1` を打ち、6 匹 + 撫で・ボール・添い寝を移植する (移植記録を作成)。**タグはこの時点で打つ** (試作用の先行コピーはしない: 2026-09-19 ユーザー決定)
5. **VER-ENV-001 の犬項目** (遊び・追従・行動範囲) を移植後に確認する
6. 試作結果を踏まえて `game-design.md` §4 の未決定事項 (広さ・配置・行動範囲・性能目標) を埋める

作業前の確認: `bash scripts/unity-api.sh health` が通り、eval の `Application.dataPath` がこのプロジェクトを指すこと (`vrc-synaptic-unity`)。
