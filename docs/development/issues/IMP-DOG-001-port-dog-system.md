---
id: IMP-DOG-001
type: IMP
system: DOG
status: todo
priority: High
---

# IMP-DOG-001 古民家ドッグランの犬ギミックを移植する

企画原本 §7。動作確認済みの実装を一式コピーし、以後このワールドで個別管理する。
共通ライブラリ化・VPM 化・自動同期はしない (不変条件 3)。

移植方針の正本は古民家側 `C:/work/unity/vcc/KominkaDogRun/docs/development/dog-system-porting.md`。

## 手順

手順 1〜3 は VER-ENV-001 (試作) より先に済ませる (試作用の犬 1 匹もこの記録範囲から持ち込むため)。

1. 古民家側で移植元コミットを決め、タグ `export/kominka-hillside-v1` を打つ (未コミット変更を含めない。タグは後から動かさない)
2. 移植範囲を棚卸しする: スクリプト / Prefab / Animator / データ / マテリアル / 設定 / `.meta` と参照先。Progression・ショップなど依存機能は「持ち込む / 省く」を決める
3. Git 外の依存 (購入アセット) を列挙し、名前・版・入手元・必要な設定を記録する。このプロジェクトへ導入したら `.gitignore` に 2 行ずつ追記
4. コピーして `Assets/KominkaHillside/` 配下に置く。命名は `KH_` へ寄せるか `KD_` のままかを決めて記録 (参照切れを増やさない方を選ぶ)
5. 移植記録 `docs/development/imports/kominka-dog-system.md` を古民家側の書式で作成し、古民家側の「移植先の一覧」にリンクを追加する
6. コンパイルエラー 0 を確認し、6 匹 + 撫で・ボール・添い寝を Play で確認する (ユーザー)。山に合わない機能 (庭固有の配置・縁側の添い寝位置など) はここで調整・削除

## 決めること (この Issue で)

- 残す機能・省く機能 (添い寝・おもちゃの配置、トレーナー「集まる」の権限、写真・ギャラリー、Progression / ショップ)
- 犬の行動範囲を山に合わせる方法 (広場 + 散歩道)

## 完了条件

- [ ] 古民家側にタグがあり、移植記録に SHA (完全長) が書かれている
- [ ] 6 匹が置かれ、撫で・ボール・添い寝が古民家と同じ操作で動く (2 人で同期確認: ユーザー)
- [ ] 省いた機能と理由が移植記録にある
- [ ] `game-design.md` §4 の「ふれあい」「トレーナー機能」の未決定を更新した

## 参照

- 企画原本 §3, §7, §8: `../../reference/VAnimal_KominkaHillside_Concept.md`
- OurUsualPark の移植 (前例。撫で判定・ゾーン別リアクションのみ): `C:/work/unity/vcc/VAnimal_OurUsualPark/docs/development/dog-system-porting.md`
- `vrc-networking` / `vrc-scene-edit-safety` (NetworkIDs の残骸)
