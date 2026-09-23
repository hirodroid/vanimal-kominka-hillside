---
id: IMP-DOG-015
type: IMP
system: DOG
status: todo
priority: Medium
---

# IMP-DOG-015 飾りと撫でパーティクルを移植し、首輪との干渉を調整する

2026-09-23 起票。[DES-UI-003](DES-UI-003-menu-scope-and-polish.md) で **飾りの品揃え・見た目・選び方・着け方はそのまま持ち込む**、**撫でパーティクルも 6 種 + CE 同梱分をそのまま採用**し、置き場所は かざりタブ内の小見出し、と決まった。古民家には首輪が無いため、**首元 (Chest スロット) を使う 7 品と裏山の首輪・リードの干渉は前例がない**。実測と調整が本Issueの主題のひとつ。

実装順は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) → [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) → **本Issue** ([IMP-DOG-014](IMP-DOG-014-treats-port.md) / [IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md) と並行可)。首輪・リードの本体は [IMP-DOG-004](review/IMP-DOG-004-leash-models-release.md) が持つ。

## 範囲

- `KD_AccessoryBox` / `KD_PetFxBox` / `KD_PlayerFxTag` をシーンへ配線する。古民家の Editor (`KominkaDogRun: Assets/KominkaDogRun/Editor/KD_AccessorySetupEditor.cs` / `KD_AccessorySetsEditor.cs` と撫でFX の setup) は **`KD_` 名のまま `Assets/KominkaHillside/Imported/KominkaDogRun/Editor/` へコピー**し、裏山の差分は partial に隔離する。**裏山で新規に書く Editor だけ `KH_{Name}Editor`** (2026-09-23 決定。[計画書](../../plans/2026-09-23-menu-rebuild.md) D-3)。
- 14 商品 (単独 9 品 + セット 5 件) のモデル・マテリアルを移植する。ポイント品は IMP-PRG-001 の価格表 (計 260pt)、**CE 品 (王様 / 夏 / キャンディ / パンプキン) は Product 参照で所持判定**する。移植記録は [kominka-dog-system](../imports/kominka-dog-system.md)、権利確認は [素材の確認記録](../../AI_INDEX.md#素材の確認記録) の型へ統合する。
- 撫でFX 11 種 (ハート / 肉球 / キラキラ / 花びら / 桜ハート / 金の肉球 = 0〜40pt、キャンディ / かぼちゃ / 提灯 / 流れ星 / 紙吹雪 = CE 同梱) を移植する。個人装備・同期変数 0 本で、選択だけ `KD_PlayerFxTag._Publish` が他人へ配る方式を継承する。
- 装着機構を継承する: 5 スロット固定 (Head / Face / Chest / Body / Tail)、ボーン割当 (Chest = 首ボーン Bone.003)、品目×犬の 1 次元配列、Manual 同期の `_slotProduct` が正本で見た目は同期変数から作る、Owner が未所持品を拒否、**最後の変更者は即・他人は 60 秒待つ**、**着せた人が退室してもそのインスタンスには残る** (古民家 2026-09-16 決定、`KominkaDogRun: docs/development/design/accessories.md` §7.1)。
- **首元 7 品 (バンダナ / 蝶ネクタイ / 宝石付き首飾り / ひまわり首飾り / 花コサージュ / 肉球メダリオン / 胸かぼちゃ) と首輪・リードの干渉を実測**する。対策候補 (a) 首輪を Chest 品の少し上に置き、Chest 品の既存「前へ 15cm」オフセットを再調整する (b) 首輪装着中だけ Chest 品を少し前・下へずらす。**解消できない品は一覧に記録して相談する** (取り下げの判断はユーザー)。
- かざりタブの頁 (14 行 → セット / 個別 → 犬選択 + 試着プレビュー + 撫でエフェクトの小見出し) は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md)。本Issueは Box が返す所持・装着状態をその頁へ渡すところまで。
- **装着の永続は入れない** (新しいインスタンスは装着なしから始まる)。古民家⇄裏山の往復で何が残るかは [DES-WLD-001](DES-WLD-001-round-trip.md)。
- UI 頁は古民家の `KD_TreatPouch` の partial `.Acc` + `.PetFx` を使うが、**「そのまま」ではなく「Acc へ合流する裏山改変」**を行う ([計画書](../../plans/2026-09-23-menu-rebuild.md) D-2)。現物は `Redraw.cs:58,61` で TabPetFx → `RedrawFx()`、TabAcc → `RedrawAcc()` の**排他分岐**で、FX の購入確認更新も `RedrawFx` の中にある。本Issueの改変は (a) `RedrawFx` を「行の描画部 `RedrawFxRows(parent)`」と「タブ部」に分け、`RedrawAcc` の**カタログ階層 (`_accSetMenu == 0` かつ `_accProduct` 未選択) のときだけ** FxRows を描く (セット中身・装着方法・犬選択の階層では非表示) (b) 確認カードの kind Fx はそのまま (c) 戻るは Acc の `_AccBack` を使い FX 専用の戻るを作らない (e) Verify に混在行の順序と FX 行の onClick `_Fx{i}` を足す。**(d) Page_Acc Content への `FxRow{0..10}` 生成 (ビルダー) は段階 0-b で IMP-UI-001 が行う。**
- ミニチュアは `KD_DogPreviewEditor` を段階 2 で焼く。

## やらないこと

新しい飾りの制作・裏山オリジナルの品揃え、装着状態の PlayerData 保存、ギフト (CE セットの [ギフト] は裏山では未決定)、首輪モデル自体の作り直し (IMP-DOG-004)、Quest / iOS の合格判定。

## 完了条件

- [ ] 14 商品が 6 匹に装着でき、他人の画面にも見える (Manual 同期の復元を含む)
- [ ] 購入・所持・試着プレビューが台帳 (IMP-PRG-001) と一致する。未所持でも試着一覧へ進め、犬の行から購入確認カードへ行ける
- [ ] **首元 7 品 × 首輪で干渉を実測**し、対策 (a)(b) の採用値を記録した。解消できない品は一覧に残した
- [ ] 撫でFX のポイント 6 種 + CE 同梱分の切替が他人にも配られる
- [ ] 60 秒ルールと「退室後もインスタンスに残る」を 2 人で確認した
- [ ] PCVR でユーザーが見た目と着け心地 (首まわりの重なり) を確認した。**Quest / iOS の合格判定は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md)**

## 関連

- [DES-UI-003](DES-UI-003-menu-scope-and-polish.md) 順番 1・4: 品揃えと撫でFX の採用、首輪干渉の対策候補 (a)(b)
- [IMP-DOG-004](review/IMP-DOG-004-leash-models-release.md): 首輪・リードの本制作 (干渉の相手側)
- [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md): 飾り 260pt / 撫でFX 120pt の価格表と CE の Product 参照
- [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md): かざりタブの頁と試着プレビュー
- [kominka-dog-system](../imports/kominka-dog-system.md): 移植記録の追記先
