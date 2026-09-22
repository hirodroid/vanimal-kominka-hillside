---
id: IMP-DOG-013
type: IMP
system: DOG
status: wip
priority: Medium
---

# IMP-DOG-013 フリスビーを持ち帰った犬をよしよしする

## 要望

2026-09-22 ユーザー要望: 「フリスビーを持って帰ってきた時によしよししたい。今は反応しないし、受け取ったらすぐ走っていってしまう」。

## 何が起きていたか

**2 つの別々の理由で、持ち帰りの直後だけ犬に触れなかった。**

1. **くわえて待っている間は撫でが抑制されていた。** 撫では「engaged (Idle と Ready 以外)」の間まるごと止めていた ([IMP-DOG-005](IMP-DOG-005-frisbee-jump-catch.md) 段階 2 の規則 — 跳んでいる最中に頭の骨へ 2 人目の書き手を入れないため)。ところが Drop (目の前まで持ち帰って、くわえたまま 3 秒待つ) も engaged なので、**いちばん撫でたい瞬間が抑制の中に入っていた**。犬は止まっているのに無反応だった。
2. **ディスクを受け取った瞬間に 8 m 先へ走った。** 口から取った = 誰かが持っている = Ready で、Ready の最初の仕事が「持ち手の正面 8 m へ走って構える」。受け取った直後に必ず走り出していた。

## 設計 (2026-09-22 実装)

### 1. くわえて待っている間は撫でられる

- 撫でを再開してよい状態を `IsEngaged` の裏返しではなく **`KH_FrisbeeCatch.PetAllowed`** という 1 本の規則にした: **Idle / Ready / Drop** (止まっていて、次に動く予定が無い状態)。走る・跳ぶ・拾う・運ぶ状態では従来どおり抑制する。
- `KD_DogFetchController` の再開判定も同じ `PetAllowed` を読む (以前は `IsEngaged`)。**2 つのコントローラが同じ述語で再開する**ので、どちらか片方だけ抑制が解ける事故が起きない。
- **撫でている間は Drop のタイマーを止める。** 撫での最中にディスクを足元へ落とさない。手が離れたら止まっていたところから再開する。
- くわえたディスクの表示は変えない。撫では LateUpdate で頭の骨に加算回転を書き、ディスクは `PostLateUpdate` で口のアンカーから置き直すので、**ディスクは撫でられた頭に付いてくる** (取り合いにならない)。

### 2. Ready の頭に「よしよしの猶予」を置く

- 新しいコード所有の値 `readyGraceSeconds = 2` / `readyGraceDistance = 2.5`。Build (`ConfigureCatcher`) が書く。
  (初版は 4 秒。**PCVR 2026-09-22 で「待ちは 2 秒でいい」** → 2 秒へ。撫でている間は秒数に関係なく待ち続けるので、体験は短くならない)
- Ready に入った時刻を記録し、**持ち手が 2.5 m 以内にいて、かつ 2 秒以内**の間は、待機位置へ走らずその場に留まって持ち手のほうを向く。
- **撫でられている間は、猶予が過ぎていても走り出さない。** 判定は撫でコントローラの `IsPetting` (自分の手) と、Fetch 側の同期された撫で状態 `PetActive` (他の人の手) の両方を見る。犬の Owner と撫でている人が別でも成立する。
- 持ち手が 2.5 m より離れる / 2 秒経って誰も撫でていない → **従来どおり** 8 m 先 (プリセットごとの幾何) へ走って構える。猶予は Ready 1 回につき 1 度だけで、走り出した後に戻ってくることはない。
- **猶予の間に投げても今までどおり。** 投げは `ReqCatch` が Ready を上書きするので、猶予は一切見ない。
- `IsEngaged` の意味は変えない (Ready は今までどおり engaged ではない)。走り込みプリセット v0〜3 の幾何も猶予の後は完全に従来どおり。

### ログ (Owner、Ready 1 回につき 1 行ずつ)

```
[KH_FrisbeeCatch] ready grace: holder=1.4 m petting=false
[KH_FrisbeeCatch] ready go (reason=distance)      # または reason=time
[KH_FrisbeeCatch] drop wait paused: being petted  # Drop の待ちを止めたとき 1 回
```

## 実装 (2026-09-22、Editor 検査済み・Play 未)

- `Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs`: `readyGraceSeconds` / `readyGraceDistance`、`PetAllowed` / `PetAllowedIn(int)`、`BeingPetted()`、`ReadyGrace()`、Drop のタイマー控除。
- `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogFetchController.Owner.cs`: 撫で再開を `!frisbeeCatch.PetAllowed` に。
- `Assets/KominkaHillside/Scripts/Editor/KH_FrisbeePrototypeEditor.cs` / `KH_DogWalkingEditor.cs`: Build が猶予の 2 値を書き、Verify が範囲・proxy/backing の一致・撫で再開の規則 (状態ごと) を機械検査する。
- 試作 Verify 75 項目 / 散歩 Verify 206 項目 / メニュー Verify 27 項目、いずれも失敗 0。コンパイルエラー 0、コンソール Error 0。
- ClientSim ハーネス 36 投は回帰確認 (持ち帰り・Drop が従来どおり完了するか)。**試作シーンには持ち手がいないので Ready・猶予・撫では 1 度も通らない** (ハーネスの `notCovered` に明記)。

## PCVR 1 回目 (2026-09-22、ユーザー) と round 5 の修正

**「撫でてから離れて投げるのはめちゃくちゃ気持ちいい」** — 流れ自体は確定。直したのは 2 点。

- **待ちは 2 秒でいい** → `readyGraceSeconds` 4 → 2。撫でている間は秒数に関係なく待ち続けるので、体験は短くならない。
- **よしよしの流れが跳躍を潰していた。** 撫でた直後は犬が投げ手の 0.8-1.1 m 横にいるので、そこから跳躍帯へ入るのは幾何的に間に合わず、15 投のうち 5 投が地上回収に落ちていた (`plan=2 mode=hook dist=10-11 slack=0.00`)。後ろから走って追いついて立ち取り帯でさらう「追い抜き」は v3 専用だったので、**全プリセットへ開いた** (順番は両方の跳びの後、立ち止まる計画の前)。速さ・跳ぶ距離・見積もりの較正と合わせて [フリスビーのバリエーション §6 回目](../../plans/2026-09-21-frisbee-variations.md) に記録。

## Play チェックリスト (ユーザー担当・PCVR)

準備: PCVR で入り、フリスビーを投げて犬に持ち帰らせる。Console を `[KH_FrisbeeCatch]` で絞ると `ready grace` / `ready go` / `drop wait paused` が読める。

- [ ] 持ち帰って目の前で待っている犬を**撫でられる** (♡・表情・尻尾が出る)
- [ ] 撫でている間はディスクを落とさない (手を離すと、止まっていた残り時間で落とす)
- [ ] 口からディスクを受け取っても、**2 秒 or 2.5 m 離れるまではその場で待つ** (すぐ走り出さない)
- [ ] 受け取った後に撫で続けている間は走り出さない (2 秒を過ぎても待つ)
- [ ] 持ち手が離れたら、いつもどおり 8 m 先へ走って構える (`ready go (reason=distance)`)
- [ ] 猶予の最中に投げれば、今までどおり即座に追う
- [ ] ボール Fetch との排他は従来どおり (ボールを投げれば Fetch が勝ち、その間は撫でられない)
- [ ] リード接続・集合中はフリスビーが始まらない / 途中で取られたら犬はそちらへ行く (従来どおり)
- [ ] 待っている間の撫でで、くわえたディスクが口から外れたり暴れたりしない

| 症状 | 見るところ |
|---|---|
| 持ち帰りの後も撫でに反応しない | 状態 (`Drop` に入っているか)。Fetch 側が Idle か (ボールを持っていると Fetch が撫でを取る)。撫でコントローラの配線 (`pet`) |
| 撫でている最中にディスクを落とす | `drop wait paused` が出ているか。出ないなら `IsPetting` / `PetActive` が立っていない (他人の撫では同期経路) |
| 受け取ってもまだすぐ走る | `ready grace:` の `holder=` (2.5 m より遠い位置で取っていないか)。`ready go (reason=)` が `distance` なら距離、`time` なら 2 秒切れ |
| いつまでも走り出さない | 誰かが撫で続けている (`ready go` が出ない)。撫でをやめれば出る |
| 撫でている最中に跳んで頭が暴れる | 抑制が漏れている → Verify の `PetAllowed` 項目。Chase/Jump で撫でが生きていれば配線か規則の破れ |

## 完了条件

- [ ] 持ち帰って待っている犬を撫でられ、撫でている間はディスクを落とさないことを PCVR で確認した
- [ ] ディスクを受け取った直後に走り出さず、離れる / 時間切れで従来どおり構えに行くことを確認した
- [ ] 猶予中の投げ・ボール Fetch・リード・集合との排他が従来どおりであることを確認した
- [ ] 複数人 (投げ手 ≠ 犬 Owner、撫でる人 ≠ 投げ手) で同じ挙動になることを確認した
- [ ] Quest 単体・iOS の実機確認は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md) に集約する

## 関連

- [IMP-DOG-005 フリスビーの跳んで取る](IMP-DOG-005-frisbee-jump-catch.md): 状態機械・Ready・Drop・撫での抑制規則の出どころ
- [フリスビー試作の計画](../../plans/2026-09-20-frisbee-prototype.md): 「持ったら位置取り」と段階 2「取り合いの規則」の撫で (M2)
- [フリスビーのバリエーション](../../plans/2026-09-21-frisbee-variations.md): 走り込みプリセットの Ready 幾何 (猶予の後は従来どおり)
- [DES-UI-002 フリスビーの設定](DES-UI-002-frisbee-settings.md): 待つ場所・猶予を設定項目にするかはここで判断する
