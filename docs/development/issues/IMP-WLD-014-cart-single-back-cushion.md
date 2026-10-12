---
id: IMP-WLD-014
type: IMP
system: WLD
status: review
priority: Medium
summary: カートの背もたれのクッションが 2 つ並んでいて二人乗りに見える。一人用の 1 つにする (Blender 原本 scripts/art/osanpo-cart.py から再生成)
next: "再生成した FBX・BaseColor を取り込み済み (Body 2,876 / LOD1 1,088、Cart/2 Verify CART_OK)。ユーザーが背面と乗車時の見た目を確認"
updated: 2026-10-12
---

# IMP-WLD-014 カートの背もたれを一人用の 1 クッションに

2026-10-12 ユーザー依頼: 「背もたれにクッションが 2 つ付いていて二人乗り用みたいになっている。一人用に改善して」。

## 現状

- [本制作](../../plans/auto-walk-cart/production.md): 背クッションは 2 分割 (各 0.24 × 0.47 m、厚さ 70 mm)、三角形 360 (LOD0 Body の内訳)。原本は `scripts/art/osanpo-cart.py` (設定は先頭の定数)、再生成は Steam 版 Blender 5.2.2 の `--background` (約 4 秒) で FBX・BaseColor・Sign・証拠画像・検査 JSON を出す。
- 一人乗りのカート (DES-WLD-004) なので、見た目も 1 人掛けに揃える。

## 方針

- 背クッションを 1 枚にする。幅は 2 枚の外寸と同じ (約 0.50 m)、高さ 0.47 m、厚さ 70 mm、角の分割はそのまま (三角形は減る方向)。座布団・背板・UV の割当は変えない (テクスチャは同じスクリプトで再生成)。
- 予算 (LOD0 3,872 / LOD1 1,424) は超えない。LOD1 に背クッションの簡略形があれば同じく 1 枚に。
- 取り込み後: Cart/2 Verify、ユーザーが見た目を確認 (背面からと乗車時)。

## 完了条件

- [ ] 背もたれのクッションが 1 つで、二人掛けに見えない (ユーザー確認)
- [x] 三角形が予算内、`Temp/osanpo-cart-*/report.json` と production.md の数値を更新 (2026-10-12)
- [ ] Cart/2 Verify PASS

## 記録

2026-10-12 (統括): Unity に取り込み (コンパイルの refresh)。シーン内の Cart_0 の Body 2,876 / Body_LOD1 1,088 を確認、Cart/2 Verify CART_OK 2,321。証拠画像 front-quarter.png で 1 枚・中央・支柱に非接触を確認。ユーザーの見た目確認待ち。

2026-10-12: 起票。実装は Opus (統括 Fable)。
2026-10-12: 原本を変更して再生成 (Opus)。`scripts/art/osanpo-cart.py` の背クッションを中央 1 本の loft (`rrect(.50, .47, .045, …)` を (0, .25, 0) に、UV は x = 0 で折り返す) にし、左右の複製 (`body.mirror(bmk)`) は支柱だけに。島の寸法情報 `backcush_front` を w = .25 に。LOD1 も同じ関数なので同時に 1 枚。三角形 LOD0 3,872 → 3,692 (Body 3,056 → 2,876、背クッション 360 → 180)、LOD1 Body 1,136 → 1,088、LOD1 合計 1,424 → 1,376。背クッションの島が広がりアトラスが組み直された (中遠距離 ×1.629 → ×1.614、充填 77.4% → 77.7%) ので、FBX の全メッシュの UV と BaseColor が変わる。Empty 8 個・オブジェクト位置・寸法は前の FBX と一致。証拠画像 `docs/plans/evidence/2026-10-12-osanpo-cart/` (前斜め・上面・側面でクッション 1 枚、中央、背板の内側、支柱と重ならないことを確認)、検査 JSON `Temp/osanpo-cart-20261012/report.json`。Unity の操作・git はしていない。
