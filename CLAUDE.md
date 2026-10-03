# KominkaHillside -- CLAUDE.md

VRChat ワールド「古民家の裏山」。古民家ドッグラン (`C:/work/unity/vcc/KominkaDogRun`) の続編。
資料の入口と不変条件は [docs/AI_INDEX.md](docs/AI_INDEX.md)、一般の知識と手順は共有 Skill (`.claude/skills/vrc-*/`)。ここには毎回要ることだけを書く。

- **シーン**: 作業対象は `Assets/Scenes/VRCDefaultWorldScene.unity` (川沿い周回版)。更新は `Tools/KominkaHillside/Course/` を使う。旧 Terrain Prototype の再生成、Walking/3・4・4b、Menu/7・12・13・17 は周回構成を失うので実行しない ([切替記録](docs/plans/des-env-002/production.md))。
- **置き場所と名前**: 自作物は `Assets/KominkaHillside/`、スクリプトは `KH_`、古民家から移植したものは `KD_` のまま `Imported/KominkaDogRun/`。メニューは `Tools/KominkaHillside/` 配下。購入アセットのフォルダは編集せず、派生物を `Assets/KominkaHillside/` へコピーする。
- **Unity 操作**: `scripts/unity-api.sh` 経由。コンパイル確認は `bash scripts/unity-compile.sh [--udon]`。
- **Play (ハーネス・計測)**: 起動は `bash scripts/kh-harness-run.sh` からだけ。回す前に相談し、全体はユーザーが回すと指示した時だけ ([説明書](docs/testing/kh-frisbee-harness.md))。
- **文書**: ファイルを移動したら `node scripts/check-doc-links.js`。Issue は `node scripts/check-issues.js` (`--write` で一覧を生成、`--next <prefix>` で採番)。
- **Git**: `main` へ直コミット。stage は自分が触ったパスだけ (別セッションが同じ作業ツリーにいる)。購入アセットはコミットしない。`.meta` は必ずコミットする。
