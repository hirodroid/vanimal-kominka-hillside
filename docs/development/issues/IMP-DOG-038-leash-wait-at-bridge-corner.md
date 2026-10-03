---
id: IMP-DOG-038
type: IMP
system: DOG
status: review
priority: Medium
summary: 上流の橋の入口の急な角 (ノード 240→241、約 106°) で、リードの犬が角の先で Wait に入り、飼い主が角を回り込む間にリードの距離が Return の閾値 3.75 m を越えて手元へ走って戻る
next: "Play (A0-27): 上流の橋の角でリードの犬が走って戻らず、少し歩み寄って待つか。下流の橋の角 (316→317) と逆回りも見る。閾値 (75°・0.4 m) は幾何から置いた値で未調整"
updated: 2026-10-03
---

# IMP-DOG-038 橋の角でリードの犬が待ち、閾値を越えて手元へ戻る

## 見つけた経緯

2026-10-03、[IMP-WLD-004](IMP-WLD-004-warp-all-five-points.md) の ClientSim ハーネス `Course/83d` に入れた標本 (0.2 s ごとの犬と飼い主の位置・距離 r・飼い主の向きで見た前後 fwd / 左右 lat・leashMode m・_routeNext n・速さ v) で見つけた。**ワープの問題ではない**: ワープ無しで同じ位置から歩いても再現する。

場所は上流の橋の入口。周回の正方向で、ノード 238 (上流の橋の着地点) → 239 → 240 (400, 490) で南へ下り、240 → 241 (406, 490) で東の橋の床へ約 106° 曲がる。ハーネスの飼い主は 1.2 m/s でノードを結ぶ線をそのまま歩く。

run 20261002-233009 の抜粋:

- **ワープ無し、2 ノード手前から (`T13-bridge-control`)**: Return 0。ただし角で `m1 n241 v0.00` (Wait) に入り、飼い主が角を回る間に r が 3.34 → 3.60 → 3.73 まで伸びた (閾値まで 2 cm)。飼い主が角を過ぎると n242 へ進んで r は 3.5 前後に戻った。
- **ワープ無し、着地点のノードで繋いで 1.5 s 後に歩き出す (`T13-bridge-control-landing`)**: 9.9 s で Return。`t9.1 d(404.1,491.3) r3.58 m1 n241 v0.00` → `t9.7 r3.75 m1` → `t9.9 r3.84 m2` (Return)。犬は角を斜めに先回りして橋の上 (404, 491) で止まり、飼い主は (400.3, 491.1) から角へ下っている。
- **ワープ後 (`T13-dogs-1-L1`、上流の橋へワープして同じ道を歩く)**: 10.2 s で Return。`t8.9 r3.56 m1 n241` → `t9.7 r3.76 m1 n241 v0.00` → Return (handDist 3.8)。着地点で繋いだ場合と同じ形。6 匹の回でも複数の犬が同じ位置で handDist 3.8。

## 原因の見立て (コードの読み、未確定)

`KD_DogWanderController.HillsideLeash.cs` の 7 (Lead):

- 犬は角のノード 240 に近づいた時点で次の 241 へ進み、角を斜めに切って先回りする。
- 飼い主より前 (`DogAhead`) で `handDist >= waitAt` (リード長 × 0.78〜0.90)、かつ lead-hold の帯 (`LeadHoldMax`) を外れると `LeashWait` に入って止まる。
- 飼い主は角の手前でまだ南へ歩いているので、止まった犬から見ると飼い主は横から後ろへ回り込む形になる。r は伸び続け、1.25 倍 (3.75 m) を越えると Return (走って手元へ戻る)。
- 飼い主が角を過ぎれば犬は前に戻るので、越えるかどうかは数 cm の差で決まる (2 ノード手前からだと 3.73 m で止まった)。

## 直し方の候補

1. 飼い主が角のノードを過ぎた (または飼い主の進む向きで見て犬が横〜後ろになった) ら、Wait せずに次のノード側へ回り込んで歩き続ける
2. 飼い主の向きで見て後ろになるノード (n241 が飼い主の後ろ側) では Wait に入らない
3. 急な角 (隣のリンクとの角度が大きいノード) の近くだけ、Return の閾値を上げる、または Wait の距離を短くする

どれも周回・合流・ワープ後の追従に効くので、`Course/83d` (上流の橋の T13)、歩行の区間確認 (`scripts/kh-harness-run.sh segment` の橋のケース)、`Course/82` / `83` で回帰を見る。

## 確かめること

- [ ] Play (PCVR / Desktop) で、上流の橋の入口の角を普通に歩いたとき、リードの犬が角の先で止まり、走って戻るのが見えるか (見えるなら直す。見えないなら閾値の境目だけの話として保留も可)
- [ ] 下流の橋の角 (ノード 316 → 317 付近) でも同じことが起きるか (逆回りも)
- [x] 直した後、`Course/83d` の `KNOWN_BRIDGE_CORNER_RETURN (IMP-DOG-038)` の例外をハーネスから外し、T13 と control 2 本で Return 0 を確かめる (2026-10-03、下の記録)

## ハーネスでの扱い (2026-10-03)

`Course/83d` は Return を T1 と同じく FAIL にする。例外はこの Issue の形だけ: (a) 直前の標本で犬が角のノード (spot.bridgeUp のノード + 3 = 241) で Wait (m1)・速さ 0、(b) 飼い主が橋の区域 + 4 m、(c) 着いてから 5 s 以上。その場合は `KNOWN_BRIDGE_CORNER_RETURN (IMP-DOG-038)` として notes に残し、合否に数えない (`KH_CourseWalkHarnessEditor.Travel.cs` の `TvKnownBridgeCornerReturn`)。比較の `T13-bridge-control` と `T13-bridge-control-landing` は証拠として残す。

## 関連

- [IMP-WLD-004](IMP-WLD-004-warp-all-five-points.md): 見つけた場所 (ワープ先の追加と Course/83d)
- [地図と移動の設計 冒頭の追記](../../plans/2026-09-27-map-and-travel.md)

## 2026-10-03 (`8faf572`) 注記

ClientSim の Course/83d は 5/5 で通過し、この橋の角の既知の戻りは本 Issue として注記した (ワープの問題ではない。[IMP-WLD-004](IMP-WLD-004-warp-all-five-points.md) の記録からリンク)。直し方は未選択のまま、status は todo。Play での確認項目は A0-27。

## 2026-10-03 直し方の選択と実装

**選んだもの: 案 1 と 2 の合成 (案 3 は採らない)。** 「前にいる」の判定を飼い主の**体の進む向き**でも見て、角の先で横〜後ろになった犬は lead-hold で先へ出ず、張りすぎる前に歩み寄る。

コードで確かめた原因 (`KD_DogWanderController.HillsideLeash.cs`、修正前の行):

- `DogAhead` (1458-1462) は「次のノードと手が犬を挟んで反対側か」だけを見る。飼い主の向きも進む向きも使わない。角を斜めに切って n241 の手前にいる犬は、飼い主が南へ歩いていても「前」になる。
- 7 (Lead) の `leadHold` (617) は `ahead && HolderMoving()` で 0.45〜1.1 × リード長 (3.0〜3.3 m) の間、犬をノードの方へ歩かせ続ける (速さは walkSpeed が下限)。角では飼い主から離れる向きなので、犬は 3.3 m まで出る。
- 3.3 m を越えると leadHold が外れて `LeashWait` (618-623) で `Coast()` (止まる)。飼い主は犬の横〜後ろへ歩くので r は伸び続け、`handDist > leash * 1.25f` (414) で Return。
- 飼い主の向き (facing) はハーネスの標本 (fwd / lat) だけが使っており、犬の判断には入っていない。ハーネスの飼い主の向きが道に遅れて付くことは原因ではない。体の速度は `TrackHolderSpeed` が速さ (スカラー) だけを平滑化していた。

案 1 そのまま (Wait をやめて `_routeNext` へ歩き続ける) は採らない: この位置の次のノードは飼い主から離れる側 (n241 → n242) で、r をさらに伸ばす。案 3 は Return の閾値という全体の安全柵を場所ごとに緩めるので採らない。

変更 (Owner の 7 (Lead) と Owner の `TrackHolderSpeed` だけ。同期変数・判断の間引き (IMP-DOG-036) は変えない):

- `KD_DogWanderController.HillsideLeash.cs:61` `_holderVel` (体の水平速度の平滑値、ベクトル) を追加。`:881-890` `TrackHolderSpeed` で `_holderSpeed` と同じ時定数・同じ不連続の除外で更新、`ResetHolderSpeed` (`:869`) で 0 に戻す (持ち手の交代・Owner の交代・Fetch からの復帰で既にここを通る)。
- `:77-79` 定数 `AsideCos = 0.26` (75°)・`AsideSlack = 0.4 m`。
- `:621-645` 7 (Lead): `aside` = `ahead && HolderMoving()` かつ「飼い主の進む向きと、飼い主から犬への向きの角度が 75° を越える」。`aside` の間は `leadHold` を掛けない。`ahead && handDist >= waitAt` のとき、`aside` で `handDist > waitAt + 0.4` なら `LeashWalk` で手と犬を結ぶ線上の waitAt の点へ `LeashGo` (速さ = 飼い主の速さ + 2 × 超過分、walkSpeed〜dashSpeed)。それ以外は従来どおり `LeashWait`。飼い主が角を回って犬の方へ向けば `aside` が外れ、従来の lead-hold / Wait に戻る。
- 幾何の確認 (run 20261002-233009 の標本): Return 直前の `h(400.3,491.1) d(404.1,491.3)` で飼い主が南へ 1.2 m/s のとき、角度は約 93° で aside。waitAt (2.34〜2.70 m) + 0.4 = 最大 3.1 m で歩み寄るので、Return (3.75 m) まで 0.65 m 以上残る。

変えないもの: Return の閾値 1.25 倍とその処理、`DogAhead` の定義 (招き 5. も同じ関数を使う)、まっすぐな道での lead-hold (犬が進む向きから 75° 以内なら従来と同じ)、非 Owner の経路 (7 は `if (!_dogOwner) return` の後)、同期変数。

ハーネス (`KH_CourseWalkHarnessEditor.Travel.cs:328-334, 405-406`): `KnownBridgeCornerReturnAllowed = false` を追加。`TvKnownBridgeCornerReturn` の検出は残し、一致した Return は `KNOWN_BRIDGE_CORNER_RETURN (IMP-DOG-038, now FAIL)` として FAIL の理由に入る。修正を戻して比べるときだけ true にする。

統括が回すもの (コンパイル確認の後):

1. `bash scripts/unity-compile.sh --udon`
2. `Tools/KominkaHillside/Course/83d` (上流の橋の T13、`T13-bridge-control` / `T13-bridge-control-landing` を含む): Return 0、`KNOWN_BRIDGE_CORNER_RETURN` 0。control の標本で r の最大が 3.4 m 前後に収まり、角で m0 (歩み寄り) → m1 が出ることを見る
3. `bash scripts/kh-harness-run.sh segment bridge-490-choice-2-dogs-6` と `segment bridge-490-choice-3-dogs-6` (上流の橋、正逆)、`segment bridge-190-choice-2-dogs-6` (下流の橋の角、確かめること 2 つ目の代わり)
4. `Course/82` と `Course/83` (ワープ後の追従の回帰)

Play (A0-27) では、角の先で犬が止まって走って戻らず、少し歩み寄って待つかを見る。

## 2026-10-03 ハーネスの結果 (修正後、U# コンパイル OK)

- `Course/83d` run 20261003-021001: 5/5 PASS、全ケース Return 0・`KNOWN_BRIDGE_CORNER_RETURN` 0 (例外は FAIL 扱いに戻した状態)。角でのリード距離の最大は `T13-bridge-control` 3.12 m、`T13-bridge-control-landing` 2.93 m (修正前は 3.84 m で Return)、`T13-dogs-1-L1` 2.93 m、`T13-dogs-6-L3R3` 3.17 m。標本では角の先で m0 (歩み寄り) → m1 (待ち) → n242 へ進む。
- `segment bridge-490-choice-2-dogs-6` run 20261003-021738: SEGMENTS_OBSERVED、6 匹とも橋を渡り forward 5 / reverse 0、最大の 1 歩 0.27 m、sceneUnchanged。
- 回さなかったもの: bridge-490 の逆方向・bridge-190・Course/82・83 (変更は 7 (Lead) の「飼い主の進む向きから 75° 以上外れた時」だけに掛かる条件で、直線と緩い曲がりは従来の式のまま。公開前の回帰で拾う)。Codex の独立レビューは省略 (Owner 専用の経路 32 行、同期変数・権限の変更なし。vrc-review の省略条件)。
- ClientSim 1 人の結果で、Play・2 人・実機の合格ではない。
