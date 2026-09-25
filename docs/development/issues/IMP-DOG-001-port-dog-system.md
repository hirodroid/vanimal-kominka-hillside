---
id: IMP-DOG-001
type: IMP
system: DOG
status: wip
priority: High
summary: 古民家ドッグランの犬ギミックを移植する (タグ `export/kominka-hillside-v1` + 移植記録)
---

# IMP-DOG-001 古民家ドッグランの犬ギミックを移植する

企画原本 §7。動作確認済みの実装を一式コピーし、以後このワールドで個別管理する。
初めから共通化を進めず、両ワールドで同じ修正が繰り返される部分が見えたら検討する。後から受領した散歩企画資料と game-design.md を優先する。

移植方針の正本は古民家側 `C:/work/unity/vcc/KominkaDogRun/docs/development/dog-system-porting.md`。

## 手順

当初はDES-ENV-001確定後の着手としていたが、2026-09-20の散歩実装指示により移植を前倒しした。地形の境界・端末負荷の項目別合格は未了のまま扱う。固定タグを作成してコピー済み。詳細は [移植記録](../imports/kominka-dog-system.md)。

1. 古民家側で移植元コミットを決め、タグ `export/kominka-hillside-v1` を打つ (未コミット変更を含めない。タグは後から動かさない)
2. 移植範囲を棚卸しする: スクリプト / Prefab / Animator / データ / マテリアル / 設定 / `.meta` と参照先。Progression・ショップなど依存機能は「持ち込む / 省く」を決める
3. Git 外の依存 (購入アセット) を列挙し、名前・版・入手元・必要な設定を記録する。このプロジェクトへ導入したら `.gitignore` に 2 行ずつ追記
4. コピーして `Assets/KominkaHillside/` 配下に置く。命名は `KH_` へ寄せるか `KD_` のままかを決めて記録 (参照切れを増やさない方を選ぶ)
5. 移植記録 `docs/development/imports/kominka-dog-system.md` を古民家側の書式で作成し、古民家側の「移植先の一覧」にリンクを追加する
6. コンパイルエラー 0 を確認し、6 匹 + 撫で・採用するおもちゃを Play で確認する (ユーザー)。山に合わない機能 (庭固有の配置・縁側の添い寝位置など) はここで調整・削除

屋外で伏せる・一緒に休む動作は未確定で、添い寝を必須としない。リードの新規実装は [DES-DOG-001](DES-DOG-001-leash-walking.md) の設計後に別の実装 Issue で扱う。移植記録にはコピー日・既知の不具合や注意事項・裏山側変更も残す。

## 決めること (この Issue で)

- 残す機能・省く機能 (屋外休憩の採否・おもちゃの配置、トレーナー「集まる」の権限、写真・ギャラリー、Progression / ショップ)
- 犬の行動範囲を山に合わせる方法 (広場 + 散歩道)

## 完了条件

- [x] 古民家側にタグがあり、移植記録に SHA (完全長) が書かれている
- [ ] 6 匹が置かれ、撫で・採用するおもちゃが動く (2 人で同期確認: ユーザー)
- [x] 省いた機能と理由が移植記録にある
- [ ] `game-design.md` §4 の屋外休憩・既存の飾り・トレーナー機能など移植対象の未決定を更新した

## 参照

- 企画原本 §3, §7, §8: `../../reference/VAnimal_KominkaHillside_Concept.md`
- OurUsualPark の移植 (前例。撫で判定・ゾーン別リアクションのみ): `C:/work/unity/vcc/VAnimal_OurUsualPark/docs/development/dog-system-porting.md`
- `vrc-networking` / `vrc-scene-edit-safety` (NetworkIDs の残骸)
