---
id: BUG-WLD-001
type: BUG
system: WLD
status: review
priority: High
summary: カートに座った直後、足元が座面の高さに来て腰が浮く (PC・Quest とも)。座席の高さを一番下 (−30 cm) にしても足りない。腰 (お尻) が座面に乗る高さに自動で合わせる
next: "2026-10-13 ユーザー報告「前は高すぎ、今回は埋まった。中間に」→ 補正量を測った差の半分に (SeatAutoFraction 0.5、目標 座面 + 0.10 は据え置き)。C# 0 / U# 0、Cart/2 CART_OK。ユーザーの Play で中間の高さを確認し、ログ [KH_Cart] seat-auto の full / delta で割合を詰める"
updated: 2026-10-13
---

# BUG-WLD-001 カートの着座の高さを腰で合わせる

2026-10-12 ユーザー報告 (Quest / PC 共通): 「カートに座った時、最初、足元がカートの座面の高さになっている。座席を毎回一番下にしているが、それ以上下げられない。腰 (お尻) が椅子に乗る高さで合わせてほしい。座面でなく足に乗せる案もあるが、腰で」。「Issue 化して対応してほしい」。

## 現状の仕組み

- 座席は `VRCStation` (`seated = true`、`PlayerMobility = Immobilize`)。入る位置は `Anchor_Seat` = 座面上 (ローカル (0, 0.500, −0.260)、[本制作](../../plans/auto-walk-cart/production.md) の表)。
- 高さ・前後・向きの手動調整は同期の段数 (`cartSeatSteps` / `cartFwdSteps` / `cartYawSteps`、1 段 5 cm / 15°、−6..+6) で、全クライアントが `KH_CartSeat._ApplySeat` で `Anchor_Seat` を既定 + 段数に置く ([実装設計](../../plans/auto-walk-cart/implementation.md) §M1.1 3)。
- 報告どおりなら、実機では入る位置にアバターの足元 (root) が合っていて、腰は座面よりアバターの座高ぶん上に浮く。VRChat が seated の station で腰を合わせるかどうかは SDK の資料で確定できなかったので、**実測で合わせる**作りにする (どちらの挙動でも結果が同じになる)。

## 方針 (2026-10-12)

1. ローカルの乗員が着座 (`OnStationEntered`) してから 0.5 s 後に 1 回だけ、`VRCPlayerApi.GetBonePosition(HumanBodyBones.Hips)` の高さと、`Anchor_Seat` の**既定**位置 (手動段数を除いた座面) の高さを比べる。腰の骨の中心は尻の接地面より少し上なので、目標は「座面 + HipAboveSeat (約 0.10 m)」。
2. 差を 5 cm の段数に丸め、新しい同期配列 `cartSeatAuto[8]` (−20..+20 = ±1 m) に乗員だけが入れられる (`ReqSeatAuto`、`ReqSeat` と同じ照合)。全クライアントは `_ApplySeat(auto + 手動, yaw, fwd)` で置く。降車で 0。
3. 測るのは着座ごとに 1 回 (補正を掛けた後に測り直さない。ループ防止)。腰の骨が無いアバター (ゼロが返る) は補正なし。手動の ±30 cm は好みの調整として残す。
4. ログ `[KH_Cart] seat-auto cart= hips= feet= seat= delta= steps=` を残し、実機の報告と突き合わせる。

## 完了条件

- [ ] PC (Desktop / VR) と Quest で、座った直後に腰が座面に乗って見える (アバター 2 種類以上、背の高低)
- [ ] 他の人から見ても同じ高さ (同期)
- [ ] 手動の上下・前後・向きが従来どおり効き、降車で元に戻る
- [ ] Cart/2 Verify PASS (同期配列の長さを含む)

## 記録

2026-10-13: ユーザー報告「カートに座った時、今度は下がりすぎて埋まっていた。前は高すぎたので中間ぐらいに」。全量補正 (腰を座面 + 0.10 に乗せる) が下げすぎなので、`KH_CartSeat.SeatAutoFraction = 0.5` を足し、測った差の半分だけ補正する (0 = 補正なし = 以前の高さ、1 = 今回の全量。どのアバターでも「前と今回の真ん中」になる)。目標 HipAboveSeat 0.10 は据え置き。ログに full / fraction / delta を出す。C# 0 / U# 0、Cart/2 Verify CART_OK 2,088。Play 未確認 (ユーザー)。割合は Play の結果で 0.3〜0.7 に詰める。

2026-10-12: 起票。実装は Opus (統括 Fable)。

2026-10-12 (統括): C# 0 / U# 0、Cart/2 Verify CART_OK 2,321 (cartSeatAuto の長さ検査を含む)。Play 未確認。
2026-10-12: 実装 (未コンパイル・未 Play)。同期配列 `cartSeatAuto[8]` (`KH_CartManager.cs:69`)、ApplyCore が `_ApplySeat(手動, 向き, 前後, 自動)` (`KH_CartManager.cs:393` → `KH_CartSeat.cs:66`、高さ = 既定 + (手動 + 自動) × 5 cm)。測定は `KH_CartSeat.cs:223` (OnStationEntered のローカルで 0.5 s 後に予約) → `_MeasureHip` (`KH_CartSeat.cs:239`): 座っている・自分のカート・この着座で未測定のときだけ、段数 = round((Anchor_Seat の既定位置の world y + HipAboveSeat 0.10 (`:40`) − 腰 y) / 5 cm)、−20..+20。腰がゼロなら補正なしでログだけ。出入りを繰り返したときは最後の着座の予約だけが測る。要求は `_SendSeatAuto` (`KH_CartManager.Requests.cs:154`、Owner なら直接) → `ReqSeatAuto` (`:258`、CallingPlayer で照合) → `ReceiveSeatAuto` (`:286`、RiderCart の乗員だけ、絶対値で上書きして Commit)。降車・Parked・Maintain は `ResetCart` で 0 (`KH_CartManager.Groups.cs:211`、Idle の不整合判定 `:306`)。Cart/2 Verify の座席配列の長さに `cartSeatAuto` を追加 (`KH_CartSetupEditor.VerifyM11.cs:72`)。
