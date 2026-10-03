# ボールの柄 (DES-DOG-002) — 全員共通 1 つ・おもちゃタブで選んで買う

2026-10-03 作成。Issue: [DES-DOG-002](../development/issues/DES-DOG-002-toy-behaviour.md)。質問と推奨: [着手前に答えが要る質問](2026-10-03-open-questions.md) の DES-DOG-002 の節。上段を分け合う相手: [フリスビーの設定](2026-10-03-frisbee-settings.md) §8。

決定: 2026-10-03 (ユーザー回答「推奨で」) 柄は 6 匹のボール全部に効く全員共通の 1 つ (古民家からコピー済みの `KD_ToyBox`、同期 string 1 本)。選ぶ・買うのはおもちゃタブの上段、買うのは共通の確認カード。棚は置かない。価格は古民家と同じ (2026-09-23 決定、game-design §4: 基本 0 / テニス 10 / ドーナツ 20 / 骨 30 / 布 40 / 手毬 50 pt)。ぬいぐるみ 3 点 (CE) とディスクの柄・動きの違う 3 種類目は初期公開に入れない。

状態: コードと文書を書いた段階 (未コンパイル・Unity 未実行・未コミット)。Unity のコンパイル・メニュー・ハーネス・Play は統括が行う。

## 1. 古民家の作り (読取) と裏山での使い方

- 古民家: ボール (SphereCollider + VRCPickup + VRCObjectSync) は 1 犬 1 個のまま、子の `KD_ToyMesh_{key}` を 1 つだけ `SetActive` する。当たり・物理・Fetch は共通。選んだ値は `KD_ShopCatalog` のプレイヤーごとのカーソル (PlayerData に保存) で、`KD_ToyBox` がカタログの通知 (`_OnShopSelectionChanged`) を受けて Owner を取り、同期 string に書く。見た目は同期値からだけ作る (途中参加の人の保存値が既にいる人のボールを書き換えない)。
- 裏山: 6 犬種のボールのプレハブ (`Imported/KominkaDogRun/Prefabs/KD_Ball_*.prefab`) には `KD_ToyMesh_*` の子 (基本ボール・テニス・ドーナツ・骨・布・手毬・ぬいぐるみ 3 点・手毬 Codex 案) と犬色のマテリアル (柴・サモエドを含む) が既にある。初期表示は基本ボールだけ。商品の行 (`KD_ShopItem_Toy_*`) もカタログにある。足りなかったのはシーンの `KD_ToyBox` と選ぶ画面だけ。

## 2. 同期と受け手の検査

| 項目 | 値 |
|---|---|
| 同期 | `KH_Dogs/KD_ToyBox` (専用 GameObject、Manual)。`[UdonSynced] string _syncedProduct` 1 本 (古民家と同じ。増やしていない) |
| 書く人 | `KD_ToyBox` の Owner だけ。選んだ人は `SetOwner` してから書く (移譲を待つ間は自分の画面だけ先に出し、5 秒で畳む。古民家のまま) |
| 途中参加 | `OnDeserialization` で同期値を受け取る。未設定 ("") ならシーンの初期状態 (基本ボール) のまま |
| 受け手の検査 (裏山で追加) | 見た目を作る前に全クライアントが同じ規則で確かめ、通らなければ無料の基本ボール (`productKeys[0]`) を出す。① 表に無いキー・壊れた文字列は添字にしない (`IndexOfProduct` が −1 → 基本ボール) ② 無料品はそのまま ③ CE は扱わない ④ 有料品は今の Owner が `KD_ShopItem.OwnedBy` (台帳の PlayerData の `Own.*`。他人の分も読める) を満たすときだけ |
| Owner の PlayerData がまだ届いていない・後から変わる | 基本ボールにしておき、`OnPlayerRestored` (その Owner の分)・2 秒ごとの見直し (最大 15 回)・`OnPlayerDataUpdated` (今の Owner の分で、有料の柄を保留している間だけ。見直しの回数を使い切った後でも出る) で出し直す (独立レビュー 2026-10-03 中 2) |
| Owner が替わった | **全員が** `OnOwnershipTransferred` と `OnPlayerLeft` (1 秒後) で今の Owner に対して確かめ直す。新 Owner が書き直さなくても、全員が同じ入力 (同期値と今の Owner) から同じ見た目にそろう (独立レビュー 2026-10-03 中 1)。選んだ人が Owner を取る間の一瞬、他の人には基本ボールが見えることがある |
| Owner の退室 | 自動で移った新 Owner は、その柄を持っていなければ基本ボールへ書き直す (受け手もすでに基本ボールを出しているので値をそろえるだけ)。自分の台帳がまだ読めていなければ 2 秒ごとに待つ |
| 帯域 | 選んだときだけ (文字列 1 本) |

確かめる相手を「書いた人」ではなく「今の Owner」にしたのは、同期値を書けるのが Owner だけだから。書いた人の id を別に同期すると変数が 2 本になる (決定は 1 本)。

残る穴: PlayerData を書き換えた改造クライアントは自分の所有を偽れる (台帳全体の前提で、柄に限らない)。

## 3. 画面 (おもちゃタブの上段の右半分)

```
投げ方 [まっすぐ][曲げる]   ボールの柄 [◀][ テニスボール  10 pt ][▶]
────────────────────────────────────────────────────
ボール・ディスクを手元へ (犬 6 行、今のまま)
```

| 部品 | 中心 x | 幅 | x の範囲 | 押したとき |
|---|---:|---:|---|---|
| ボールの柄 (ラベル) | 75 | 130 | 10..140 | — (22pt、自動縮小 14 まで) |
| [◀] | 172 | 52 | 146..198 | `_PatternPrev`: 見ている柄を 1 つ戻す (世界は変えない) |
| [名前 状態] | 334 | 260 | 204..464 | `_PatternAct` (下表) |
| [▶] | 496 | 52 | 470..522 | `_PatternNext` |

縦は y 164 ± 24 (140..188)。左半分 (投げ方、-545..-15) と 25 px あく。右端 522 は板の内寸 550 の内側。

中央のボタン (1 行。名前と、金色の状態):

| 見ている柄 | 状態の文字 | 地の色 | 押すと |
|---|---|---|---|
| 世界に出ている柄 (誰も選んでいない間は基本ボール) | 使用中 | 灰 | 「使用中」と出すだけ |
| 持っている (基本ボールは最初から) | えらぶ | 青 (equip) | カタログの `_RequestSelect` → `KD_ToyBox` が全員のボールを変える。「6 匹のボールを「〇〇」にしました」 |
| 持っていない・買える | 10 pt など | 緑 | 共通の確認カード (題「〇〇を 10 pt で購入しますか？」、中身「6 匹のボールが全部この柄になります。みんなにも見えます」、残り)。[購入] で `_RequestBuy` (払う → 解放 → 選ぶ) |
| 持っていない・足りない | 10 pt など | 灰 | 同じカード。[購入] は出ない (注記「あと N pt」) |
| 台帳の読み込み中 | 読み込み中 | 灰 | 同じカード (読み込み中) |

- 見ている柄 (カーソル) はこの板だけのローカルの値。最初は世界に出ている柄。
- カードは他の操作 (タブ切替・設定・板を閉じる・別のカード) で閉じられたら次の描画で捨てる。
- pt が動くのは `_PatternConfirmBuy` の 1 か所だけ (おやつの [買う] と同じ 2 段階)。
- 文言 3 キー × 13 言語 (コースの表): `course.toy.pattern.label` ボールの柄 / `course.toy.pattern.body` 6 匹のボールが全部この柄になります。みんなにも見えます / `course.toy.pattern.changed` 6 匹のボールを「{0}」にしました。品名・「使用中」・「えらぶ」・「購入」は既存のキー。

## 4. 変えたもの

| ファイル | 中身 |
|---|---|
| `Imported/.../Scripts/KD_ToyBox.cs` | 受け手の検査 (`ValidatedProduct` / `ApplySynced`)、`productItems`、見直し (`OnPlayerRestored` / `_Revalidate`)、新 Owner の書き直し (`_ReconcileAsNewOwner`)、移譲待ちの打ち切りで基本ボールへ戻す修正、メニューが読む `ItemOfProduct` / `ProductCount` / `ProductAt`。同期変数は 1 本のまま |
| `Imported/.../Scripts/KD_TreatPouch.ToyTop.cs` | 右半分の行 (`_PatternPrev` / `_PatternNext` / `_PatternAct` / `_PatternConfirmBuy` / `_PatternConfirmNo`、確認カードの種別 `ConfirmToyPattern = 17`) |
| `Imported/.../Scripts/KD_TreatPouch.Confirm.cs` | [購入] / [やめる] の振り分けに 1 行ずつ |
| `Imported/.../Editor/KD_ShopSetupEditor.Hillside.cs` | `Menu/4 Setup Shop` を流し直しても `KD_ToyBox` をカタログの通知先から消さない |
| `Scripts/Editor/KH_ToyTopRowEditor.Patterns.cs` (新規) | `Course/124 - Build Ball Patterns (toy box and toy tab row)` と `Course/125 - Verify Ball Patterns` |
| `Scripts/Editor/Testing/KH_CourseWalkHarnessEditor.ToyRow.cs` | `Course/126b` (BP0〜BP6) |
| `Data/Environment/Course/CourseUITranslations.json` | 上の 3 キー × 13 言語 |

ボールのプレハブ・Fetch・物理は触っていない (見た目の子を `SetActive` するだけ。ボールに UdonBehaviour を足さない)。拾うときの文言 (`pickups`) も配線しない (今の文言のまま)。

## 5. 回すもの (統括)

1. コンパイル (`bash scripts/unity-compile.sh --udon`)。新しい U# スクリプトは無い (`KD_ToyBox.asset` は既にある。Manual)。
2. [フリスビーの設定](2026-10-03-frisbee-settings.md) §8.3 の Course/122 を先に (上段を分け合う。Course/124 は 122 が無いと止まる)
3. `Course/124 - Build Ball Patterns (toy box and toy tab row)` (保存まで行う)
4. `Menu/19b Assign Network IDs` → シーンを保存 (新しい同期オブジェクト)
5. `Course/27 - Add Course UI Glyphs`
6. `Course/125 - Verify Ball Patterns` → `BALL_PATTERNS_OK`。あわせて `Menu/4a Verify Shop`・`Menu/21 Verify Scroll Layout`・`Walking/5 Verify` (同期オブジェクトの NetworkID)
7. ハーネス (BP0〜BP8。BP7 = Owner を柄を持たない人形へ、BP8 = 所有データの遅着、どちらも模擬): `bash scripts/kh-harness-run.sh walk "Course/126b - Harness Toy Tab ball patterns (DES-DOG-002)"` → `TOY_ROW_PASS` (126a と続けるなら `Course/126` で両方)

## 6. ハーネスでは見ないもの

- 2 人: 他人の画面で柄が変わること、途中参加の人が同じ柄を見ること (Owner の PlayerData が後から届いて出し直すまでの間は基本ボール)、Owner の退室と新 Owner の書き直し、2 人がほぼ同時に違う柄を選んだときの最後の 1 つへのそろい方。
- PCVR / 実機: 6 犬種で柄ごとの大きさ・くわえた向き・犬色の見え方 (古民家で許容した骨の向きを含む)、ボタンの押し心地、端末のフォント (ja / en 以外は端末の字形)。
- BP5 (偽の同期値) はヒープへの直接書き込みの模擬で、実通信の受信ではない。

## 7. 仕様の正本へ (統括が反映する文案)

- game-design §4「おもちゃの柄違い」の行: 「6 匹のボール全部に効く全員共通の柄 1 つ。手元メニューのおもちゃタブの上段で選び、持っていない柄は共通の確認カードで買う (棚は置かない)。価格は古民家と同じ。ぬいぐるみ 3 点 (CE)・ディスクの柄・動きの違う 3 種類目は初期公開に入れない (2026-10-03 決定)。」
- world-ui-spec §5 の `toy.selection` の行: 「共有 1 本 (`KH_Dogs/KD_ToyBox` の同期 string)。書くのは Owner、受け手は今の Owner の所有 (台帳の PlayerData) を確かめ、通らなければ基本ボール。途中参加は同期値 + Owner の PlayerData の到着で出し直す (2026-10-03 決定、DES-DOG-002)。」
