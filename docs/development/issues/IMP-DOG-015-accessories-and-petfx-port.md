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

- [x] コンパイル 0 件で、かざり・撫でFX・ミニチュアのシーン配線を Verify で確認した (2026-09-24: C# 0 / U# 82 本・エラー 0。Menu/6f Verify Accessories (首輪干渉表を含む)、6h Verify Pet Fx、25 Verify Dog Previews (pass 1661 / fail 0)、20 (段階表 2・かざりの混在行と `_Fx{i}`)、21 (404/0)、22 全 PASS、Walking/5 209/0)
- [ ] 14 商品が 6 匹に装着でき、他人の画面にも見える (Manual 同期の復元を含む) — **実装済み・確認待ち**
- [ ] 購入・所持・試着プレビューが台帳 (IMP-PRG-001) と一致する。未所持でも試着一覧へ進め、犬の行から購入確認カードへ行ける — **実装済み・確認待ち**
- [ ] **首元 7 品 × 首輪で干渉を実測**し、対策 (a)(b) の採用値を記録した。解消できない品は一覧に残した — **実測済み・2026-09-24 案 C で決定: 5 件は (b) 適用、3 件は犬ごとに除外 (Verify 6f PASS)** (下の「首輪との干渉」。(a) の 9 件は採用して 6e が自動適用。8 件の取り下げはユーザー判断。PCVR の見え方は未確認)
- [ ] 撫でFX のポイント 6 種 + CE 同梱分の切替が他人にも配られる — **実装済み・確認待ち** (2 人は未確認)
- [ ] 60 秒ルールと「退室後もインスタンスに残る」を 2 人で確認した — **実装済み・確認待ち**
- [ ] PCVR でユーザーが見た目と着け心地 (首まわりの重なり) を確認した。**Quest / iOS の合格判定は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md)**

## 進捗

**2026-09-24 実装済み・Verify 全 PASS (Play 未実施)。** 実行順・Verify の数値・逸脱は [計画書「実装記録 / 段階 2」](../../plans/2026-09-23-menu-rebuild.md#段階-2-2026-09-24)、ファイルごとの差分と資産コピーは [kominka-dog-system「段階 2」](../imports/kominka-dog-system.md#2026-09-24-おやつかざり撫でfx呼ぶ-段階-2)。Codex D+M+U は実行中。コミットは未。

- 飾り: `Menu/6e Setup Accessories` / `6f Verify Accessories` (新規 `KD_AccessorySetupEditor.Hillside.cs`)。24 品 × 6 匹の見た目ノードを、古民家の実測値 `KD_AccessoryFits.Kominka.json` (古民家シーンの 144 ノードから抽出。Editor フォルダでビルドに入らない。コミット対象) どおり骨の下へ置く。`KH_Dogs/KD_AccessorySystem` (Manual、`_slotProduct` が正本)。セットは `KD_AccessorySetsEditor.Hillside.cs` が参照だけを配線する。同期・60 秒ルール・退室後の残留は古民家のまま。
- 撫でFX: `Menu/6g Setup Pet Fx` / `6h Verify Pet Fx` (新規 `KD_BundledPetFxEditor.Hillside.cs`)。同梱 5 種 × 2 層を各犬の `KD_PetHearts` と同じ位置へ置く。`KH_Dogs/KD_PetFxBox` (SyncMode None)、`catalog.consumers` に `_OnShopSelectionChanged`、`pouch.petFxBox`。
- かざりへの合流 (計画書 D-2 (a)(b)(c)): `.PetFx.cs` の `RedrawFxRows(visible)` を `.Acc.cs` の `RedrawAcc` の末尾から、カタログ階層のときだけ呼ぶ。群見出し「撫でエフェクト」(`FxHead0/1`)、確認カードは kind Fx、戻るは `_AccBack`。板の下端 (`HillsideAccFooter`) に撫でFX の結果か未受取の案内。旧 `RedrawFx()` は削除。CE セットの [ギフト] は `HillsideAccGiftEnabled = false` で出さない (裏山では未決定)。
- ミニチュア: 犬の体は古民家の焼き済みと 12 姿勢すべて一致した (最大ずれ 0.022 mm)。違いは首輪 (Band) の分だけなので、Menu/18 で**首輪込みに焼き直した** (開発側の決定。ユーザー確認待ち)。Verify 25 に「飾りのメッシュは 1 つを共有」の検査を足した。
- 文言の新規は 1 (`WordFxGroup`「撫でエフェクト」)。資産 (Models/Accessories +58 で計 70、Prefabs/PetFx 10、DogPreviews、KD_DogPreview シェーダ) は古民家と同じ GUID でコピー済みで、欠けは無い。

### 首輪との干渉 (2026-09-24 実測、Menu/6f)

むね (Chest) の 7 品 × 6 匹 = 42 組。頂点が首輪の帯 +3 mm の範囲か留め具に入るかで判定した。対策 (a) = 前へずらす (上限 30 mm)。(b) = 前 + 下へずらす (値は計算だけで、適用していない)。

| 結果 | 件数 | 品と犬 (採用値 / 必要な量) |
|---|---|---|
| 干渉なし | 25 | — |
| **(a) を採用して解消** (6e が自動で適用) | 9 | Corgi 肉球メダリオン 前 1 mm / Pome 宝石付き首飾り 9 mm・ひまわり首飾り 1 mm・肉球メダリオン 16 mm / Lab 蝶ネクタイ 19 mm・胸かぼちゃ 16 mm / Dachs 宝石付き首飾り 23 mm / Shiba 肉球メダリオン 3 mm / Samoyed 肉球メダリオン 6 mm |
| **未解消 → 案 C (2026-09-24)** | 8 → (b) 5 + 除外 3 | Lab: バンダナ ((b) でも不可)・宝石付き首飾り ((b) 前 8 + 下 16)・ひまわり首飾り ((b) 前 2 + 下 26)・花コサージュ ((b) 前 22 + 下 12)・肉球メダリオン ((b) 前 6 + 下 30) / Dachs: バンダナ (不可)・ひまわり首飾り (不可)・肉球メダリオン ((b) 前 0 + 下 28) |

Lab と Dachs は首輪が低く大きいことが原因。**2026-09-24 ユーザー決定 (案 C): 取り下げも首輪飾りへの作り替えもせず、当たる組み合わせだけ対処。** **2026-09-24 ユーザー決定 (案 C)**: 首元品はなくさず、首輪飾りへの作り替えもしない。当たる組み合わせだけ対処する。(b) 前 + 下 で解消する 5 件 (Lab: 宝石付き首飾り 前 8 + 下 16mm / ひまわり首飾り 前 2 + 下 26 / 花コサージュ 前 22 + 下 12 / 肉球メダリオン 前 6 + 下 30、Dachs: 肉球メダリオン 前 0 + 下 28) は Menu/6e が自動適用 (値は KD_AccessoryFits.Kominka.json の hillsideB*)。物理的に無理な 3 件 (Lab のバンダナ、Dachs のバンダナ、Dachs のひまわり首飾り) は除外表 KD_AccessorySetupEditor.HillsideCollarExcluded → KD_AccessoryBox.blockedDogs/blockedProducts に配線し、犬選択の行を暗くして「この犬には付けられません (首輪と重なる)」(WordAccNotForDog)、Owner も拒否。夏セットの Dachs はセットごと拒否せず ひまわり首飾りだけ飛ばす。既定品バンダナは「既定 = 何も付けない」で問題なし。Verify 6f: 5 件「解消 (b)」・3 件「除外」・解消しない品 0、25 / 20 / 22 / 21 / Walking/5 PASS、保存済み。試着・ポーズのミニチュアは首輪込みで確定 (ユーザー OK)。 リードの線と飾りの交差 (Dachs は留め具まで約 68 mm) も PCVR で見る。確認手順は [計画書の実機チェックリスト](../../plans/2026-09-23-menu-rebuild.md#3-段階-2-おやつかざり撫でfx呼ぶ2026-09-24-追加) の「かざり・撫でFX」。

## 関連

- [DES-UI-003](DES-UI-003-menu-scope-and-polish.md) 順番 1・4: 品揃えと撫でFX の採用、首輪干渉の対策候補 (a)(b)
- [IMP-DOG-004](review/IMP-DOG-004-leash-models-release.md): 首輪・リードの本制作 (干渉の相手側)
- [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md): 飾り 260pt / 撫でFX 120pt の価格表と CE の Product 参照
- [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md): かざりタブの頁と試着プレビュー
- [kominka-dog-system](../imports/kominka-dog-system.md): 移植記録の追記先
