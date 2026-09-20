# KominkaHillside -- CLAUDE.md

## プロジェクト概要

VRChat ワールド **「古民家の裏山」 (KominkaHillside)** — VAnimal シリーズ。
古民家ドッグラン (`C:/work/unity/vcc/KominkaDogRun`) の続編。**同じ 6 匹と秋の裏山を散歩し、遠くの古民家を眺める。**

> **AI ドキュメントナビゲーション:** `docs/AI_INDEX.md` を参照
> 企画は後から受領した `docs/reference/VAnimal_KominkaHillside_Walking_Addendum.md` を優先する (ユーザー指定)。旧 `VAnimal_KominkaHillside_Concept.md` は履歴として保存し、編集しない。
> 判断基準・ビジュアル方針は `docs/core-concept.md`、仕様・スコープは `docs/game-design.md`。

**優先する体験 (並列。優劣なし)**: 山から古民家が見える / 犬と無理なく遊べる / 小さな空間でも散歩した感じがする。豪華さは草木の密度ではなく眺め・道・犬・つながりで作る。
**PC に加え Quest 単体・iOS 対応を目指す** (実機で判断)。犬ギミックは古民家の確認済み実装をコピーして個別管理。同じ修正が繰り返される部分が見えたら共通化を検討する。散歩は片手最大 3 匹・合計最大 6 匹のリード接続を含む。

---

## ディレクトリ構成

```
Assets/
├── Scenes/                 # シーンファイル
├── KominkaHillside/         # 自作コンテンツ (すべてここに置く)
│       ├── Scripts/        # UdonSharp スクリプト
│       ├── Prefabs/        # プレハブ
│       ├── Materials/      # マテリアル
│       └── Data/           # データ
├── UdonSharp/              # UdonSharp ユーティリティ (自動生成)
└── SerializedUdonPrograms/ # Udon コンパイル済み (自動生成)

Packages/
├── com.vrchat.worlds                        # VRChat Worlds SDK
└── com.local.vrc-world-tools               # 共有診断ツール (file: 参照)
```

**作業対象シーンは `Assets/Scenes/VRCDefaultWorldScene.unity`** (VCC テンプレートのまま。改名するときはここも直す)。

購入アセット (Git 管理外): `Assets/LMArt-StylizedEnvironmentKit/` (古民家と同じ広葉樹・遠景)。秋色などの派生物は `Assets/KominkaHillside/` 側へコピーして管理する。

---

## 技術スタック

| 項目 | 値 |
|------|-----|
| Unity | 2022.3.22f1 LTS |
| VRChat SDK | Worlds SDK 3.10.5 |
| スクリプティング | UdonSharp (C#) |
| UI | World Space Canvas + TextMeshPro |
| カラースペース | Linear |

---

## 命名規則

| 対象 | 規則 | 例 |
|------|------|-----|
| システムスクリプト | `KH_{ComponentName}` | `KH_GameManager` |
| Editor スクリプト | `KH_{Name}Editor` | `KH_SetupEditor` |
| プレハブ | `KH_{Name}` | `KH_GameBoard` |
| feature ブランチ | `feature/kh-{shortName}` | `feature/kh-time-attack` |

**UI ハンドラなどローカル専用の public メソッドは `_` 始まりにする**
(理由は `vrc-networking` スキル。付けないとネットワーク経由で誰でも呼べる)。

---

## メニュー項目の配置

- このワールド固有の `[MenuItem]` は `Tools/KominkaHillside/` 配下に統一
- トップレベルに直接配置しない
- 共有診断ツールは `Tools/VRC World Tools/` 配下 (パッケージ側で固定)

---

## 共有ナレッジ (vrc-world-agent)

VRChat ワールド開発**一般**の知識・作業手順は `.claude/skills/vrc-*/` にある
(共有リポジトリへの skill ごとのジャンクション)。
**このファイルにはこのワールド固有のことだけを書く。**

| Skill | 読むべき場面 |
|-------|-------------|
| `vrc-project-registry` | 「これは前に別のワールドでやった」を辿るとき |
| `vrc-new-world-setup` | 環境の導入・再セットアップ (稀。忘れたとき用) |
| `vrc-visual-direction` | コアコンセプト直後のビジュアル方針の仮決め・代表シーン検証、モデル・小物・UI制作前、アセット選定・統合レビュー |
| `vrc-asset-production` | 3D モデル・小物の制作仕様、書き出し・取り込み・検収 |
| `vrc-synaptic-unity` | Unity エディタを操作するとき (最初に読む)。eval か Editor スクリプトかの判断もここ |
| `vrc-synaptic-blender` | Blender を操作するとき (モデル・シェイプキー・リグの修正、FBX 書き出し) |
| `vrc-verification` | 検証・コンパイル確認・Play の引き継ぎ |
| `vrc-scene-edit-safety` | シーン/プレハブを一括変更する前 |
| `vrc-udonsharp` | UdonSharp実装、UIの設計・制作・改修・レビュー (§5のUI資料)、アセットを実行時に切り替える設計 |
| `vrc-networking` | 同期・ネットワークイベントを扱うとき |
| `vrc-tmp-font` | 日本語 UI の文言追加・フォント作業 |
| `vrc-physbone-colliders` | 揺れる部位に当たり判定を載せるとき |
| `vrc-animator-reactions` | Animator でリアクションを出す / 手の接触・撫でを判定するとき |
| `vrc-performance` | 「重い」「fps が出ない」を判断するとき |
| `vrc-codex-review` | 節目 (Issue 完了・設計書の大幅更新・コミット前) に第三者レビューを頼むとき |
| `vrc-destructive-audit` | 公開前の総点検・「バグが無いか一通り見て」と頼まれたとき |

**一般化できる知見を得たら、プロジェクト側ではなく共有リポジトリの Skill を直す**
(ジャンクションなのでその場で編集できる)。

---

## 検証ワークフロー

**AI の検証はコンパイルエラー 0 件 + シーン構築の確認まで。Play はユーザーが行う。**

1. ファイル編集
2. `bash scripts/unity-api.sh execute '{"tool":"unity_force_refresh_assets","params":{"importMode":"force"}}'`
3. `unity_analyze_console_logs` を**フィルタ無し**で呼んでエラーを確認
4. シーン構築の結果は検証メニュー / grep / GUID 参照数で確かめる
5. Play で確認すべき項目をチェックリストにしてユーザーへ引き継ぐ

詳細は `vrc-verification` スキル。API 呼び出しは**必ず `bash scripts/unity-api.sh` 経由**。

---

## ドキュメント検査

```bash
node scripts/check-doc-links.js     # リンク切れ (ファイルを移動したら必ず通す)
node scripts/check-issues.js        # Issue の frontmatter / 置き場所 / サマリの整合
node scripts/check-issues.js --next # 次に使える Issue ID (採番は必ずこれで)
```

---

## Git 運用

- **購入アセットはコミットしない** (`.gitignore` で除外)
- `.meta` ファイルは必ずコミットする (Unity のアセット参照に必要)
- `Library/`, `Logs/`, `UserSettings/` は除外
- `.claude/skills/vrc-*/` は共有リポジトリへのジャンクションなので除外
