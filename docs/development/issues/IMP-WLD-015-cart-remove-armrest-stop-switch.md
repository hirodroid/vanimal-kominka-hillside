---
id: IMP-WLD-015
type: IMP
system: WLD
status: todo
priority: Medium
summary: カートの肘掛け (とって) の先にある停止スイッチ (StopL / StopR の操作面) がもう機能しておらず見た目も変。撤去する (停止は操作帯の「停止」だけに)
next: "起票のみ。実装担当は未定。Blender 原本 scripts/art/osanpo-cart.py から操作面を外して再生成 → Setup / Verify / Runtime / 文書から StopL / StopR を外す"
updated: 2026-10-12
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

- [ ] 全カート (走るカート 8 台 + 乗り場の見本 5 台) の肘掛け先に操作面が無い (ユーザー確認)
- [ ] `KH_CartStopButton` がコード・Program Asset・シーンから無くなっている
- [ ] 三角形が予算内で、`Temp/osanpo-cart-*/report.json` と production.md の数値を更新
- [ ] C# 0 / U# 0、Cart/1 Build → Cart/2 Verify PASS
- [ ] 操作帯の「停止」で今までどおり降りられる (ユーザー Play)

## 記録

2026-10-12: 起票 (Fable)。実装は未着手。
