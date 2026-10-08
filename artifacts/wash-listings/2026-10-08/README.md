# 洗い場追加アイテムのリスティング画像

2026-10-08。古民家ドッグランの artifacts/product-thumbnails/2026-09-15/README.md の実制作仕様を確認し、**1024 × 1024 PNG・価格なし・犬モデルなし**で作成。現行PrefabをUnityで撮影した画像です。

| セット | 画像 | モデル数 |
|---|---|---:|
| 泡 | [01-foam.png](01-foam.png) | 3 |
| 水 | [02-water.png](02-water.png) | 6 |
| 仕上げ | [03-finish.png](03-finish.png) | 4 |
| 洗い場 | [04-station.png](04-station.png) | 4 |
| 全部入り | [05-complete.png](05-complete.png) | 17 |

[一括ZIP](wash-listings-1024.zip) / [寸法・SHA256](manifest.json)。Unity内の17品写真と5セット画像は Assets/KominkaHillside/Data/WashPremium/Photos/。実行時は最大512、Android ASTC 6×6。リスティング原本は1024を維持。

## 制作・使用条件

- Unityメニュー Tools/KominkaHillside/WashPremium/41 - Photograph Listings がvisual Prefabを隔離プレビューシーンで撮影。画像用に形状や素材を描き替えていない。
- 生成元は KH_WashSelectionEditor.cs、セット所属は Data/WashPremium/catalog.json。
- 現在は購入なしで使う選択UI。販売ページ・商品ID・価格表示・購入判定は未接続。登録素材の用意であり、販売開始済みではない。
- 色付き泡、泡の形選択、虹しぶき、毛のボリューム変化、散歩中の持続演出は後工程。画像説明で実装済みと扱わない。
- [選択画面](evidence/board.png) / [洗い場への配置](evidence/station.png) はUnity Editor撮影。VRChat実機・Questの見え方の証明ではない。

フォント：[Noto Serif CJK](https://github.com/notofonts/noto-cjk) / SIL Open Font License。原本とライセンスは Assets/KominkaHillside/Font/Editor/WashPremium/。使用文字だけStatic SDFへ焼き、実行時の元フォント参照は外している。
