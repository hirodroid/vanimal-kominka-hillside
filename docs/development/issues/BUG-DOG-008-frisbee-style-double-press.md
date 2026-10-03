---
id: BUG-DOG-008
type: BUG
system: DOG
status: review
priority: Medium
summary: あそぶタブの「フリスビーの取り方」を返事が届く前に押し直すと、2 つ先へ進まず同じ番号を頼み直していた。`f7cb379` で `_frisbeeAsked = next; _frisbeeAskedUntil = ...;` が直前の行のコメントへ入り込み、実行されていなかった。行を戻した (未コンパイル・未実行)
next: "2 人以上の実機 (または非 Owner の端末) で、トレーナーが「フリスビーの取り方」を 1.5 秒以内に 2 回押して 0 → 3 → 4 と進むか (A0-19・B60)。Owner の端末では修正前でも進む"
updated: 2026-10-03
---

# BUG-DOG-008 フリスビーの取り方の連打で 2 つ進まない

`docs/plans/2026-10-03-frisbee-settings.md` の F2 で見つかった (設計案自体は未承認。ここでは不具合だけを直す)。

## 原因

[KD_ActivitiesMenu.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ActivitiesMenu.cs) の `FrisbeeSwitch()`。`f7cb379` (2026-09-26、v1 / v2 の退役) で次の 2 行が 1 行に繋がり、後半がコメントの中に入った。

```csharp
int next = KominkaHillside.KH_FrisbeeCatch.NextVariant(FrisbeeCurrent(first));   // 引退した v1 / v2 を飛ばす (2026-09-26)            _frisbeeAsked = next; _frisbeeAskedUntil = Time.time + 1.5f;
```

`f7cb379` より前は `_frisbeeAsked = next; _frisbeeAskedUntil = Time.time + 1.5f;` が独立した行だった (`git show f7cb379`)。`_frisbeeAsked` は初期値 -1 のまま一度も書かれないので、`FrisbeeCurrent` は常に同期値 (`PendingVariant` か `Variant`) を返していた。非 Owner の端末で、Owner の返事 (同期) が届く前に押し直すと、古い値から数えて同じ番号を頼み直す (例: 0 で 2 回押しても 3 を 2 回頼むだけ)。

## 修正 (2026-10-03)

行を元の独立した行に戻した。ほかは変えていない (周回 0→3→4→5、1.5 秒、表示は同期値のまま)。

今のコードでの動き (読み取りで確認):

- 1 回目: `FrisbeeCurrent` = 同期値 0 → `NextVariant(0)` = 3。`_frisbeeAsked = 3`、1.5 秒後まで有効。
- 1.5 秒以内の 2 回目: `Variant` も `PendingVariant` もまだ 3 でなければ `FrisbeeCurrent` は 3 を返す → 4 を頼む。返事 (Variant か PendingVariant が頼んだ値) が届くか 1.5 秒経てば、同期値へ戻る。
- Owner の端末では `ReqVariant` が同じフレームで値を書くので、`c.Variant == _frisbeeAsked` で即座に同期値へ戻る (結果は同じ)。
- 受け手が断った (トレーナーでない、BUG-DOG-009) ときは 1.5 秒で同期値へ戻る。

## 同じ事故の検索 (2026-10-03)

追跡されている `.cs` 全 367 本で、コードのある行の `//` コメントの後ろに 6 個以上の空白と文が続く行を探した。

- コードが飲み込まれていたのは上の 1 件だけ。
- コメントだけがずれていた 1 件: [KH_ToyRecall.cs](../../../Assets/KominkaHillside/Scripts/KH_ToyRecall.cs) の `RiverBankShiftZ` の行に、`RiverSlopeTie` のコメント (`a farther candidate must beat the nearer one by this much`) が付いていた (`b04ef39`)。動作は変わらないが同じ種類の編集事故なので、コメントを `RiverSlopeTie` へ戻した。
- そのほかの該当 (`KD_PhotoSetupEditor` の層の表、`KD_PhotoDirector` の定数の表、Editor のコメント内の表) は桁揃えで、事故ではない。

## 確かめたこと / まだのこと

- 確かめた: `git show f7cb379` で元の形、`FrisbeeCurrent` と呼び出し元の読み取り、上の検索。
- **まだ: コンパイルも実行もしていない** (Unity との接続が切れていた)。
- ハーネス: ClientSim は 1 クライアントで Owner = 自分なので、同期の遅れが起きず連打の分岐を通らない。決定的に確かめるなら、メニューの `FrisbeeSwitch` を直接 2 回呼ぶ間に同期値を変えない形 (Owner でない状態) が要り、今の群に自然な置き場所は無い。

## 完了条件

- `bash scripts/unity-compile.sh --udon` が通る。
- 2 人以上の実機 (または非 Owner の端末) で、トレーナーが「フリスビーの取り方」を 1.5 秒以内に 2 回押すと 2 つ進む (0→3→4)。

## 2026-10-03 (`8faf572`) 本番シーンへ

- コンパイル完了 (C# / U# エラー 0)。メニュー側は巡回を NextMenuVariant にしたうえで、連打の分岐 (F2) は本修正のまま。Menu/23 通過、ClientSim の Course/126 11/11 (取り方の権限 TT3 / TT4 を含む)。
- ClientSim は 1 クライアントで Owner = 自分なので、同期の遅れが起きず連打の分岐は通らない。完了条件の「非 Owner 端末で 2 回押して 2 つ進む」は未実施。
- 残り (ユーザー): A0-19 と、本命の B60 (Owner ではない側のトレーナー)。

検証は ClientSim 1 人 (疑似を含む)。実通信・PCVR・2 人・端末の合格ではない。
