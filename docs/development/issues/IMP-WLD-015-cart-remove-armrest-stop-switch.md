---
id: IMP-WLD-015
type: IMP
system: WLD
status: review
priority: Medium
summary: カートの肘掛け (とって) の先にある停止スイッチ (StopL / StopR の操作面) がもう機能しておらず見た目も変。撤去した (停止は操作帯の「停止」だけ)。メッシュ・コード・シーン・文書から外し、Verify CART_OK
next: "実装・取り込み済み (2026-10-13、Body 2,576 / LOD1 936、Cart/2 Verify CART_OK 2,088)。残りはユーザーの確認: 全カートの肘掛け先に操作面が無いこと (PCVR の見た目) と、操作帯の「停止」で降りられること (Play)"
updated: 2026-10-13
---

# IMP-WLD-015 カートの肘掛け先の停止スイッチを撤去する

2026-10-12 ユーザー依頼: 「カートのとっての先にある停止スイッチ、もう機能していないし、なんか変だからなくして」。

「とって」= 肘掛け。カートに取っ手は無い ([asset-brief](../../plans/auto-walk-cart/asset-brief.md): 屋根・ハンドル無しの四輪式) ので、対象は肘掛け前端の停止操作面 (くさび形の台座 + 凸ボタン) で確定。

## 現状

- **メッシュ**: Blender 原本 `scripts/art/osanpo-cart.py` の `STOP` (肘掛け前端のくさび、tag `stop_panel` / `stop_button`、UV 島 `stop_base` / `stop_cap` / `button_side` / `button_top`)。[production.md](../../plans/auto-walk-cart/production.md) の内訳で「停止操作面 2 個 300 (台座 184、ボタン 116)」(LOD0 Body)。LOD1 にも同じ関数で入っている。
- **Unity 側**: `Cart/1 Build` が各カートの子 `StopL` / `StopR` (BoxCollider + `KH_CartStopButton`、kind はどちらも 0 = 停止 → `manager._Leave()`) を `Anchor_StopL` / `Anchor_StopR` の位置に作る。`KH_CartSeat.stopButton / acceptButton / stopL / stopR` に配線、`_SetRider` で乗員にだけ Interact を出す。`Cart/2 Verify` は「KH_CartStopButton が 2 × 8 台」と位置・Collider・参照を検査する。
- **機能していない**: ユーザー報告 (実機)。撤去が決まっているので原因は追わない。
- **代替は既にある**: 操作帯 `KH_CartControls` の「停止」(BtnStop = 8 → `_PressStop` → `manager._Leave()`)。停止の経路は操作帯だけで足りる。
- 受付切替 (kind 1) は M1.1 で操作帯に移っており、物理ボタンには置いていない ([implementation.md](../../plans/auto-walk-cart/implementation.md) §KH_CartStopButton)。つまり `KH_CartStopButton` は撤去すると丸ごと不要。

## 方針

1. **Blender 原本**: `osanpo-cart.py` から停止操作面 (台座 + ボタン) を LOD0 / LOD1 とも外し、Steam 版 Blender 5.2.2 の `--background` で FBX・BaseColor・証拠画像・検査 JSON を再生成 (IMP-WLD-014 と同じ手順)。島が減るのでアトラスは組み直され、全メッシュの UV と BaseColor が変わる (前例どおり)。production.md の三角形の内訳を更新。
2. **アンカー `Anchor_StopL` / `Anchor_StopR` は残す**。ボタンの位置だけでなく、M3 カート上メニュー (`Anchor_StopR` + 0.55 m)、乗り場パネル (道側の肘掛け)、Verify / VerifyM3 の位置検査、`osanpo-cart.py` 末尾の FBX assert が参照している。改名 (例: Anchor_ArmrestL/R) は任意で、既定は据え置き。
3. **Editor**: `KH_CartSetupEditor` は `StopL` / `StopR` を作らず、既にあれば消す (Build は冪等なので、シーンに残っている 16 個を片付ける手順が要る)。`ProgramNames` から `KH_CartStopButton` を外す。`Verify` は「2 × 8 台ある」→「1 つも無い」に変え、StopL / StopR の位置・Collider・`stopButton` / `acceptButton` / `stopL` / `stopR` の RefCheck を落とす。
4. **Runtime**: `KH_CartStopButton.cs` と Program Asset を削除 (`.meta` の削除もコミット)。`KH_CartSeat` の `stopButton / acceptButton / stopL / stopR` と `_SetRider` / `_RefreshText` の該当行を外す。翻訳キー `cart.stop` は操作帯のラベルが使うので残す。
5. **文書**: implementation.md (ファイル表の `KH_CartStopButton`、§KH_CartStopButton、M1.1 の 6)、production.md の内訳。
6. 乗り場の見本カート (静的) も同じ FBX なので一緒に消える。

## 完了条件

- [ ] 全カート (走るカート 8 台 + 乗り場の見本 5 台) の肘掛け先に操作面が無い (ユーザー確認。Blender の証拠画像 `docs/plans/evidence/2026-10-13-osanpo-cart/armrest-close.png` / `front-quarter.png` では無い)
- [x] `KH_CartStopButton` がコード・Program Asset・シーンから無くなっている (2026-10-13。保存したシーンに Program Asset の GUID `2f7e3f29…`・`.cs` の GUID・`StopL` / `StopR` の名前が 0 件、`KH_Cart` 配下の programSource が null の UdonBehaviour 0)
- [x] 三角形が予算内で、`Temp/osanpo-cart-*/report.json` と production.md の数値を更新 (2026-10-13。LOD0 3,692 → 3,392 / Body 2,876 → 2,576、LOD1 1,376 → 1,224 / Body 1,088 → 936。シーンの Cart_0 の Body 2,576 / Body_LOD1 936 と一致)
- [x] C# 0 / U# 0、Cart/1 Build → Cart/2 Verify PASS (2026-10-13。Build が Cart_0〜7 の StopL / StopR 16 個を除去、Verify CART_OK pass 2,088 / ng 0。新しい検査 3 つ: Program Asset が無い / KH_Cart 配下に programSource null の UdonBehaviour が無い / 各カートに StopL・StopR が無い)
- [ ] 操作帯の「停止」で今までどおり降りられる (ユーザー Play)

## 記録

2026-10-12: 起票 (Fable)。実装は未着手。

2026-10-13 (統括 Fable、実装 Opus 2 本): 方針 1〜6 のとおり実装。**Blender**: `osanpo-cart.py` の `build()` から台座の loft・ボタンの lathe を外し (LOD0 / LOD1 とも)、島 4 つ・塗り 4 関数・色 `stop` を削除。`STOP` 定数と `R / Tst / ctop / ntop / ftop` の計算は `Anchor_StopL / R` のために残し、8 アンカーの位置・向きは前版の report.json と文字列で完全一致 (Anchor_StopR (0.285, 0.775, −0.025) / 前方向 (−0.132, 0.942, −0.309))。島が減ってアトラスは詰め直し (midFarScale 1.614 → 1.77、nearScale 1.25 → 1.2、充填 77.7% → 79.3%。座布団の前縁 15.3 → 14.7 px/cm、足置き 12.5 → 12.0 で目標 10〜16 の中)。FBX の全メッシュの UV と BaseColor が変わるので両方を一緒に取り込んだ。証拠画像 `docs/plans/evidence/2026-10-13-osanpo-cart/` (肘掛け前端は丸い木口だけ、背クッション 1 枚は維持)、検査 JSON `Temp/osanpo-cart-20261013/report.json`。**Unity**: `KH_CartStopButton.cs / .asset` と `.meta` を削除、`KH_CartSeat` の `stopL / stopR / stopButton / acceptButton` と `_SetRider` / `_RefreshText` の該当行を削除 (`_SetRider(mine, master, accepting)` の引数は呼び出し側に合わせて残す)、`KH_CartSetupEditor` は `ProgramNames` 5 本・`StopColliderSize` 削除・作成ブロックを「残っている StopL / StopR を消す」に置換、Verify は上の 3 検査に反転。**文書**: implementation.md 9 か所、DES-WLD-004 3 か所、game-design §4 1 か所、usage-flow.html 2 か所、performance-inventory 1 か所、production.md (内訳・テクセル表・アンカー表・主要色・履歴)。asset-brief.md / design-v1.html は制作当時の仕様書なので残した (現状は production.md が正本)。検証: C# 0 / U# 0 (Unity 再起動後にも `nochange` / U# 0)、Cart/1 Build BUILT (Build のログ `removed Cart_0/StopL` / `StopR`、NetworkIDs の死骸 16 件は保存前に外れた)、保存、Cart/2 Verify CART_OK 2,088。シーンの保存には Build が作り直す Model・パネルの再シリアライズを含む。未確認: ユーザーの見た目 (PCVR、近距離の密度が少し下がった座布団と足置きの前縁を含む) と操作帯の「停止」での降車 (Play)。 Codex 独立レビュー (gpt-6-astra、観点 6 つ): 修正必須なし、全観点「問題なし」。別件候補: 残骸の汎用監査 (missing script・想定外 Collider の検査)、`KH_CartControls.cs:23-25` のコメント「10 ボタン」が実際の 11 と不一致 (今回以前から)。
