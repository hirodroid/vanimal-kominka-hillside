---
id: IMP-ENV-013
type: IMP
system: ENV
status: review
priority: Medium
summary: 洗い場の台を、呼んだ犬の足の大きさに合わせて縮める (足 × 1.5・余白 5 cm・最小 0.30 × 0.22 m、犬がいない時はラブの大きさ、プールを選んでいる間は 1.3 × 0.7 のまま)
next: Wash/22・23・12・WashPremium/21 は PASS・保存済み (10-12)。Codex レビューの反映分を統括が再コンパイル → Wash/23 → W-stand-size (Course/100f3)。その後ユーザーの PCVR (ポメ + 檜、A0-63〜65・B86)
updated: 2026-10-12
---

# IMP-ENV-013 洗い場の台を犬の大きさに合わせる

## ユーザー報告

2026-10-11:「洗う機能の台が犬によって大きさが変わらない」。

## いまの状態 (2026-10-11 に読んだ範囲)

- 台は全洗い場 (主 + 道中 3) で 1 種類。2026-10-01 に犬が棚と平行に立てるよう 1.3 × 0.7 m に広げた ([IMP-ENV-011](IMP-ENV-011-wash-height-adjust.md) の記録)。犬を呼んで留める・高さを変える処理 (`KH_WashStationCall`) は `liftRoot` の高さだけを動かし、平面の大きさは変えない (ランタイムに `localScale` を書く所が無い)。
- 犬別に台の大きさを変える仕組みは**入江側に既存の実装がある**が、裏山へは移していない。ラボの記録 (`vrc-asset-lab/assets/wash-premium/validation/unity/README.md`「入江の犬別台サイズ機構の裏山への適用」) と [DES-DOG-008](DES-DOG-008-wash-paid-tools-and-effects.md) 2026-10-08 の「犬別台サイズの裏山移植は未実施」で、残課題として挙がっているだけで Issue は無かった。入江の実装 (寸法、切り替えの契機) は未確認。
- 有料の天板 (檜・石・タイル等) は 1.3 × 0.7 m 前提で作ってある。中央支柱は天板の実中心・実高さへ追従するが拡縮しない。天板を犬別に変えるときの UV 密度は未検証 (ラボ `handoff.md`、`support-design.md`「犬別サイズと高さを分離する」)。

## 対応範囲

- 犬 6 匹それぞれの台の平面サイズを決め、留めた犬に合わせて台 (見た目と Collider) を切り替える。犬がいない時の大きさも決める。
- 高さ調整 ([IMP-ENV-011](IMP-ENV-011-wash-height-adjust.md)) とは別の軸で扱う (台の上下と大きさを独立に)。
- 写真選択で選べる有料の天板 ([IMP-DOG-043](IMP-DOG-043-wash-premium-selection.md))、中央支柱、今後のプール ([IMP-DOG-044](IMP-DOG-044-wash-pool-platform.md)) が、大きさを変えても破綻しないこと。

## 設計時に決めること

- 犬別の寸法 (入江の値を流用するか)。切り替え方 (拡縮 / 大きさ違いのメッシュ数種)。拡縮なら天板のテクスチャの密度と縁・木口の潰れ、メッシュ数種なら有料天板の作り足しの量。
- 切り替える時点 (呼んだ時 / 着いた時) と同期 (台の高さと同じく洗い場ごとに全員へ)。
- 棚・道具・おやつの置き場・自分の床との距離 (いまは 1.3 × 0.7 m を前提に棚との間 0.625 m、おやつの置き場と 1.5 m を取っている)。

## 完了条件

- 6 匹それぞれを台に呼ぶと、台が犬に合った大きさになり、犬の足が台からはみ出さない・台が大きすぎて見えない。犬が下りたら決めた既定の大きさへ戻る。
- 台の高さを変えても、大きさを変えても、犬・天板・支柱・Collider の位置関係と洗浄操作が崩れない。有料の天板すべてで見た目 (テクスチャの密度、縁) が崩れない。
- 洗い場の既存の検査が通る。2 人で、台の大きさが全員に同じに見えることを確かめる (ユーザー)。

関連: [洗い場へ犬を呼ぶ・高さを調整する](../../plans/2026-10-01-wash-call-and-height.md)、[主洗い場 IMP-ENV-007](IMP-ENV-007-main-wash-station.md)、[道中の洗い場 IMP-ENV-008](IMP-ENV-008-wash-stations-along-course.md)、[6 匹の洗い IMP-DOG-030](IMP-DOG-030-wash-six-dogs.md) (体格差の調整)。

起票のみ。Unity・モデルは未変更。

## 入江 (A_Cove_to_Unwind) の犬別台サイズの仕組み (2026-10-11 調査)

入江 = A_Cove_to_Unwind (根拠: 2026-10-11 にユーザーが `C:\work\unity\vcc\A_Cove_to_Unwind` を入江として読むことを許可。入江の `Imported/KominkaHillside/Scripts/KH_WashStationCall.cs:8` は「Cove adapter」、入江の Issue `docs/development/issues/IMP-DOG-011-wash-platform-size.md` が同じ依頼「洗い台を犬の大きさに合わせる」で `docs/plans/cove-r25-wash-platform-notes.md` を設計として引いている)。

読んだのは入江の次の 4 ファイルと、値の出典としてシーンの該当行だけ (入江側は編集していない)。以下、入江のパスは `A_Cove_to_Unwind/` からの相対で書く。

- `Assets/A_Cove_to_Unwind/Imported/KominkaHillside/Scripts/KH_WashStationCall.cs` (以下 入江 Call)
- `Assets/A_Cove_to_Unwind/Editor/AC_CoveR25WashPlatformEditor.cs` (以下 入江 Editor)
- `docs/plans/cove-r25-wash-platform-notes.md` (以下 入江 notes)
- `docs/development/issues/IMP-DOG-011-wash-platform-size.md` (以下 入江 011)
- `Assets/A_Cove_to_Unwind/Scenes/AC_Cove_PC.unity:2357316-2357358` (書き込まれた値)。Android シーンも同じ値 (`AC_Cove_Android.unity:605425-605462`)

### 寸法 (犬種別、m)

規則 (入江 Editor:33-38、計算は :198-200、notes:39-44): 4 本の足 (各脚の最後の `KH_WashPart_{FrontL,FrontR,HindL,HindR}_N` カプセル、半径込み、入江 Editor:158-184) が作る長方形について、長さ・幅とも `max(足 × 1.5, 足 + 2 × 0.05, 下限)`。下限は長さ 0.30・幅 0.22。上限は元の 1.3 × 0.7。台は足の中心が台の中心に来るよう、犬の前後方向 (Lift のローカル X) へずらす。高さと厚み (0.03 m) は変えない。

下表は裏山の犬 index の順 (0 コーギー、1 ポメ、2 ラブ、3 ダックス、4 柴、5 サモエド。裏山の [wash-verify.json](../../plans/evidence/2026-10-01-wash-tools/wash-verify.json) の `dogIndex` 行と同じ)。入江のユニットの並びは `slot × 6 + 犬種` で、犬種 index は値の照合 (notes の表との一致) から上と同じ順だと判断した。値はシーンに書かれた Apply の結果 (正本)。notes の表 (:46-54) は Apply 前に YAML から見積もった値で、コーギーが 0.45 と 0.4436 のように少し違う。

| index | 犬種 | 足 長さ × 幅 (notes:28-33) | 台 長さ x (Lift X) | 台 幅 y (Lift Z) | 中心のずれ z (Lift X) | 元の面積比 (notes:48-53) |
|---|---|---|---|---|---|---|
| 0 | コーギー | 0.297 × 0.203 | 0.4436 | 0.3035 | +0.0331 | 15% |
| 1 | ポメ | 0.167 × 0.099 | 0.30 (下限) | 0.22 (下限) | −0.0056 | 7% |
| 2 | ラブ | 0.515 × 0.249 | 0.7718 | 0.3706 | +0.0153 | 32% |
| 3 | ダックス | 0.295 × 0.127 | 0.4425 | 0.2276 | +0.0087 | 11% |
| 4 | 柴 | 0.358 × 0.213 | 0.5377 | 0.3188 | +0.0294 | 19% |
| 5 | サモエド | 0.501 × 0.202 | 0.7517 | 0.3025 | +0.0178 | 25% |
| — | 犬がいない時 (idle) | — | 0.7718 | 0.3706 | 0 | 32% (最大犬種 = ラブと同じ、入江 Editor:204) |

`AC_Cove_PC.unity:2357318-2357323` (6 行 × 6 スロットの繰り返し)、idle は `:2357354`。基準は `coveStandBaseSize` 1.3 × 0.7、`coveStandBaseScale` (1,1,1)、`coveStandBasePos` (0, −0.0163, −1.2)、速さ 1.5 m/s (`:2357355-2357358`)。

### 切り替える時点

- 同期済みの `heldDog` が変わった時 (受け付けた時・解放した時・Owner が替わった時)。着いた時ではない。変わると目標の大きさを取り直し、1.5 m/s で寄せる。犬が歩いて来る間に台はもう縮んでいる (入江 Call:59-72、notes:66-70)。
- 入江には「着いた」の同期 (`arrived`) が無い (入江 Call:21-25 の同期変数に無い。着いたかは各端末で犬と Anchor の距離 0.22 m で見る、:81)。
- 解放: `RequestRelease` は `releasing = true`・`lift = 0` だけを立て (入江 Call:124)、台が下りきってから Owner が `Release()` で `heldDog = -1` にする (:146、:128)。そのため台は下りきるまで犬の大きさのままで、下りてから idle に戻る。
- 入る時 (Start) は目標の大きさへ即座に合わせる (途中から入った人に縮む動きを見せない、入江 Call:52、:60、notes:67)。
- 設定が無い (台が null・大きさが 0) 時は元の 1.3 × 0.7 のまま (入江 Call:53、notes:70)。

### 同期

- 同期変数は増やしていない。各端末が同期済みの `heldDog` から大きさを出す。途中から入った人は OnDeserialization で `heldDog` を受け取る (入江 Call:26-29、notes:64、入江 011:24)。
- `heldDog` を書くのは洗い場の Owner だけ (`RequestCall` の `IsOwner` 確認 :113、書き込み :119-120、`Release` :128、呼んだ人が抜けた時 :129、Owner の自動解放 :149)。同期方式は Manual のまま (入江 Editor:322 で検査)。

### 他の要素との関係

- **高さ**: 拡縮するのは `Lift/WashTable` の transform の X・Z だけ。Y と Anchor は変わらない。昇降 (`Lift` の y、0〜0.9 m) とは別の軸 (入江 Call:68-70、notes:12、:64。Verify が「高さが変わらない」を検査、入江 Editor:334-336)。
- **Collider**: `WashTable` 自身の BoxCollider (1.3 × 0.03 × 0.7、layer 17 Walkthrough) が transform と一緒に拡縮する。BoxCollider の API は呼ばない。layer 17 なので人も犬の地面レイも通り抜け、Pickup と落とした道具だけが当たる (notes:14、:64)。Apply の前提として、固い BoxCollider が 1 つだけ・ほかの Collider なし・**WashTable の下に Behaviour なし** (拡縮で動いてしまうため) を検査する (入江 Editor:96-100)。
- **犬の立つ点 (Anchor)**: `Lift/Anchor` は WashTable の兄弟で動かない。台の方を足の中心のずれ (z 列) だけ前後へずらす (notes:17、:43、入江 Editor:200)。Apply は「Anchor が台の中心の真上」を前提として確かめ (入江 Editor:270-271)、Verify は 36 ユニット全部について、足が縁から 5 cm 以上内側・足の中心が台の中心から 5 mm 以内・台が元より大きくない、を模擬計算で確かめる (入江 Editor:216-231、:324-333)。
- **照明**: WashTable 以下が static 0・ライトマップなし・Bakery の保存先に無い・Light Probe 照明であることを Apply / Verify の前提にしている (入江 Editor:129-151、notes:58-64)。再ベイク不要とした理由。
- **有料の天板・UV 密度・中央支柱**: **入江には有料の天板も中央支柱も無い。** WashTable の子は `Slat_0..4` (非表示) と `ImportedPlatform` だけに決め打ちで (入江 Editor:44、:101-102)、見える 1 枚の木の台 (`WashPlatform.fbx`) を X・Z に非一様に引き伸ばしている。木目と縁の面取りは比例して細かくなり、厚みは 3 cm のまま。この見た目は受け入れた妥協として書かれているだけで、確かめてはいない (notes:72)。UV 密度・縁の潰れ・支柱は入江では扱っていない。
- **見え方の注意**: Edit モードのシーンは 1.3 × 0.7 のまま。新しい大きさは Play / VRChat でだけ見える (notes:72、入江 011:42)。

### 入江で未確認のもの

- 入江 011 は status review。C# / U# コンパイル 0、PC / Android で Apply → Verify PASS → 保存 → reload → Verify PASS まで (入江 011:38-40)。notes:5 の「not compiled or run」は Apply 前の記述で古い。
- 実行時に縮む見た目 (木目)、VR で洗う人の体が台にめり込まないか、小さい台に置いた道具が落ちやすくなること、Quest での見た目は未確認 (入江 011:41、notes:105-117、入江 Editor:341 の `notTested`)。
- 足は Edit モードの姿勢で測っていて、洗い中の姿勢の違いは 5 cm 以上の余白で吸収する前提 (notes:116)。
- サモエド・ラブの台の幅 (0.30 / 0.37 m) が毛や体より細く、心許なくないかは未確認。足ではなく体の幅で決めるなら規則と定数が変わる (notes:109、:117、入江 011:8)。

## 裏山への移し方 (案)

### 裏山のいまの形 (入江との違い)

- 入江の `KH_WashStationCall` は裏山版の写しではなく、洗い場 1 か所・36 ユニット用に書き直した別物 (入江 Call:8-25)。裏山版 (`Assets/KominkaHillside/Scripts/KH_WashStationCall.cs`) には台の大きさの処理が無い。裏山で同期しているのは `heldDog` (:82)、`lift` (:84)、`arrived` (:85)。台の高さだけを各端末で寄せている (:499-510)。
- 台の組み立ては入江とほぼ同じ形: `Lift/WashTable` (BoxCollider 1.3 × 0.03 × 0.7、拡縮 1、`KH_DogWashSetupEditor.Station.cs:52`、:1409-1417)、その下に非表示の `Slat_0..4` (:1418-1426、`KH_DogWashSetupEditor.Art.cs:228`) と `ImportedPlatform` (Art.cs:225-227)、`Lift/Anchor` は WashTable の兄弟で台の上面中央 (Station.cs:1431-1433)。シーンでは 4 か所とも `WashTable`・`ImportedPlatform`・`Lift`・`SupportGroundRoot` が static 0 (2026-10-11 にシーンの YAML で、この名前の GameObject だけを確認。ImportedPlatform の子の Mesh と外装プレハブの中は未確認)。ライトマップ index と Bakery の保存先への登録も**未確認**。
- 違う点:
  - 洗い場が 4 か所、犬は 6 匹 (index = 犬種、`KH_WashStationCall.cs:47`)。入江は 1 か所・36 ユニット。
  - 裏山の犬は Anchor へ歩いて来て、そこで止まる (`KD_DogWanderController.HillsideWash.cs:284-296`)。入江のように吸着はしない。台の Collider は犬の地面レイに映らない (StationVerify.cs:184 の説明) ので、台の大きさは犬の動きに影響しない。
  - 裏山の `Release()` は `heldDog = -1` と `lift = 0` を同時に書く (:422-429)。犬は下がる台に乗って下りる (`KD_DogWanderController.HillsideStationLeave.cs:13`)。入江の書き方を移すと、下りている途中で台が idle (最大の大きさ) へ広がる。広がるだけなので足がはみ出ることはない。
  - 別の犬を呼ぶと、前の犬が歩いて去る間に台が新しい犬の大きさへ縮む (Accept :401-413)。
- **有料の台**: 檜・石・タイルの外装と浮かぶアヒル (models 13〜16) は **WashTable の子** として置かれている (`KH_WashSelectionEditor.Build.cs:36`、アヒルはローカル (0.38, 0.05, −0.2))。入江の方式 (WashTable の transform を拡縮) をそのまま使うと、外装も一緒に非一様に伸び縮みする (例: ポメで X 0.23 倍・Z 0.31 倍)。無料の台の見た目は `freeTableRenderers` (WashTable 以下のすべての Renderer、Build.cs:31) で、有料の台を選ぶと隠す (`KH_WashSelection.cs:97`)。なお石・タイル・アヒルは IMP-DOG-047 で外す予定 ([IMP-DOG-047](IMP-DOG-047-wash-item-review-changes.md):18)。残る有料の台は檜と、今後のプール (IMP-DOG-044) の 2 つ。
- **中央支柱**: `KH_WashSupportVisual` は Station の直下にあり、Lift の子でも拡縮の子でもない。毎フレーム、台の Collider の下面中心 (拡縮とずれを含む) を X・Z で追い、自分は拡縮しない (`KH_WashSupportVisual.cs:17-27`)。`KH_WashPremiumEditor.cs:179-182` が、台を拡縮 (0.5, 1, 0.6) してずらしても支柱が付いて行くことを検査済み。ラボの引き渡しにも「Lift や犬別 X/Z スケールの子にしない」とある (`vrc-asset-lab/assets/wash-premium/handoff.md:43`)。そのため支柱はほぼ手を入れずに使える。取り付けの深さは台の種類ごと (`KH_WashSelection.cs:98`)。

### 移すときに必要な変更

1. **`KH_WashStationCall.cs` (裏山)**: 同期しないフィールドを足す。`standTable`、`standSize` (Vector3[6]、犬 index 順。null で宣言し、Editor が確保して配線する)、`standIdle`、`standBaseScale` / `BasePos` / `BaseSize`、`standSpeed`。`StandStep` と `_StandSize()` は入江 Call:52-73 と同じ計算で足す。同期変数は増やさない。
   - **罠**: 裏山の `Update` は、高さが目標に着くと `if (_liftNow == want) return;` で抜ける (:507)。`StandStep` はこれより前 (または `liftRoot` の判定の前) で呼ぶ。そうしないと、台が止まっている間は大きさが変わらない。
   - Q3 で「着いた時」を選ぶ場合は、`heldDog >= 0 && arrived` を目標の条件にする (:85、:405)。
   - Q2 で「有料の台は 1.3 × 0.7 固定」を選ぶ場合は、`KH_WashSelection` の参照を足して `selectedBath >= 0` の間は元の大きさを目標にする。外装 (Build.cs:36) は WashTable から Lift の直下へ付け替える (付け替えないと、寄せている間に伸び縮みが見える)。
   - U# の Program Asset を再コンパイルし、`unity-compile.sh --udon` で確かめる。
2. **ビルダー**: Wash/11 は、写真選択が入った後は自分で止まる (`KH_DogWashSetupEditor.cs:226`「use a targeted station update instead」)。そのため Wash/11 は変えず、入江 Editor と同じ形の**狙いを絞った Apply / Verify の 2 メニューを新しく作る** (`Tools/KominkaHillside/Wash/` 配下)。
   - 入江 Editor から外すもの: 洗い場 1 か所の前提 (:87)、36 ユニット・6 スロットの union とばらつき検査 (:188-196)、`AC_*` の Ready・シーン判定 (:58-64、:264)。代わりに 4 か所を順に回す。
   - 足は裏山の 6 匹の `KH_WashPart_{FrontL,FrontR,HindL,HindR}_N` で測り直す (部位の名前 `KH_DogWashSetupEditor.cs:92`、:139、作り方 `KH_DogWashSetupEditor.Dog.cs:486`)。入江の値は入江の犬の姿勢と縮尺で測ったものなので、そのまま写さない。
   - `standYaw` が Lift の X 軸に沿うかと、その向き (±1) は洗い場ごとに求める (入江 Editor:103-105)。z 列 (中心のずれ) の符号は洗い場ごとに変わりうるので、値は洗い場ごとに書く。
   - WashTable の子の決め打ち (入江 Editor:44、:101-102) と「Behaviour なし」(:100) は、裏山では有料の台の外装 (プレハブ) が子にいるため、そのままでは通らない。Q2 の答えに合わせて直す (拡縮するなら外装を許す一覧に入れる。固定なら付け替えた後の形を検査する)。
   - 照明の前提 (static 0・ライトマップなし・Bakery に無い・Light Probe 照明、入江 Editor:129-151) は、裏山では未確認なので Apply の前提として必ず入れる。
3. **既存の検査への影響**:
   - Wash/12 は Edit モードで「Collider の大きさ × lossyScale = 1.3 × 0.7」とスラット 5 本を見る (`KH_DogWashSetupEditor.StationVerify.cs:186-187`、:198-199)。Wash/32 は「台の見た目 = TableSize」を見る (Art.cs:250)。Edit モードの拡縮は 1 のままなので、どちらも影響しない。
   - ClientSim ハーネスの洗い場の群 (`KH_CourseWalkHarnessEditor.Wash*.cs`) は `liftRoot` だけを読み、台の大きさは読まない (2026-10-11 grep)。
   - **WashPremium/21 Verify は壊れる**: `KH_WashPremiumEditor.cs:97` が `KH_WashStationCall` を丸ごと JSON にして、保存した基準 (`Temp/WashPremium/runtime-before-stable.txt`、:188) と比べるため、フィールドを足すと一致しなくなる (`Stable()` は instanceID を置き換えるだけ、:223-224)。基準の取り直しが要る。
   - 新しい Verify は入江と同じ項目にする: 6 匹 × 4 か所で、足が縁から 5 cm 以上内側・中心のずれ 5 mm 以内・元より大きくない・高さが変わらない・backing の値。
4. **Play での確認 (ユーザー)**: ポメ・ダックス・ラブを呼び、台の大きさ・寄せる速さ・高さを上げて横から洗う時の体のめり込み・檜の台の木目と縁 (特にポメ)・小さい台に置いた道具。2 人で同じ大きさに見えること。ハーネスを回すかは実装後に相談して決める。

### 危険と注意

- **有料の台の伸び縮み**: 檜の縁と木口が細く潰れる。ラボは「全体を圧縮すると後縁・木口が潰れる」として禁止し、「犬別サイズは入江の実装を参照し、サイズ別の UV 密度は別途検証」としている (`vrc-asset-lab/assets/wash-premium/handoff.md:35`)。拡縮を選ぶなら、ポメ (X 0.23 倍) の檜を見て決める必要がある。
- **プール (IMP-DOG-044)**: §4 の案は「1.3 × 0.7 の 1 種で作り、縁を角・辺・底の部品に分けておく」([設計案 §4.2](../../plans/2026-10-11-wash-items-design.md))。WashTable の子に置いて拡縮すると、縁の厚さと帆布の模様が伸びる。縁を保ちたいなら、部品の位置を大きさから計算して置き直す処理 (角は動かすだけ、辺は長さ方向だけ伸ばす) が別に要る。透ける水面 1 枚は拡縮で問題ない見込み。
- 台が小さくなると、台に置いた道具が落ちやすい (入江 notes:111)。リセットで戻る。
- 洗い場 4 か所 × 犬 6 匹。同じ犬を 2 か所が一時的に持つことがある (`heldMs` の新しい方が勝つ、:89-96) が、大きさは各洗い場が自分の `heldDog` から出すので矛盾は出ない。
- Edit モードの証拠写真は 1.3 × 0.7 のまま写る (入江 notes:72 と同じ)。
- 気づいた点 (未確認): `freeTableRenderers` には非表示にしたスラット 5 本の Renderer も入っている (シーンの各洗い場で 6 本、`VRCDefaultWorldScene.unity:363791-363797` 等)。`KH_WashSelection._Apply` は有料の台を選んでいない時にこれらすべてを `enabled = true` にする (`KH_WashSelection.cs:97`)。そのため Wash/32 で隠したスラット (Art.cs:178-182、:228) が表示に戻り、台の見た目と重なっている可能性がある。拡縮すればスラットも一緒に縮むので、台の大きさの問題とは別に確かめる。

### ユーザーに決めてもらうこと

- **Q1 寸法の規則**: 入江と同じ「足の範囲 × 1.5、各辺 5 cm 以上、下限 0.30 × 0.22 m」(推奨) / 体の幅 (毛) まで覆う規則 / 他の値。**推奨: 入江と同じ。** 2 つのワールドで見え方が揃い、定数は 1 か所で変えられる。サモエド・ラブの幅 0.30 / 0.37 m は入江でも未確認なので、Play で細すぎたら幅の下限だけ上げる。
- **Q2 有料の台 (檜・今後のプール) も犬に合わせて拡縮するか**: (a) 無料の台と一緒に拡縮 / (b) 有料の台を選んでいる間は 1.3 × 0.7 のまま / (c) 縁を保つ部品の置き直し、または大きさ違いのモデル。**推奨: (a) で作り、ポメ + 檜を Play で見てもらう。** 仕組みが 1 つで済み、支柱は対応済みで、再ベイクも要らない。崩れて見えたら (b) へ切り替える (外装の付け替えと目標の条件を足すだけ)。(c) はラボのモデル作業が増えるので、プールで縁の厚さが気になった時だけにする。
- **Q3 切り替える時点**: 呼んで受け付けた時 (入江と同じ) / 着いた時 (裏山にある `arrived` を使う)。**推奨: 受け付けた時。** 犬が乗る前に大きさが決まり、途中から入った人も同じ同期値から同じ大きさになる。着いた時にすると、犬が乗ってから台が縮むのが見える。
- **Q4 犬がいない時の大きさ**: 最大の犬種 = ラブの大きさ (入江と同じ、約 0.77 × 0.37 m) / いまの 1.3 × 0.7 m。**推奨: 入江と同じ。** 空の台が大きすぎて見えない。どの犬でも「呼ぶと縮む・放すと広がる」の向きになるので、裏山の解放 (台が下りる途中で idle に戻る) でも足がはみ出さない。

2026-10-11 調査の追記のみ。コード・Unity・入江プロジェクトは変更していない。

決定: 2026-10-11 (ユーザー「推奨」) 上の Q1〜Q4 を推奨どおり: Q1 入江と同じ決め方 (足の矩形 × 1.5、各辺 5 cm 以上、最小 0.30 × 0.22 m)、Q2 有料の台 (檜・プール) も一緒に拡縮し、ポメ + 檜を Play で見る (崩れて見えたら有料の台を選んでいる間は 1.3 × 0.7 m のまま)、Q3 呼び出しを受けた時に切り替える、Q4 犬がいないときは最大の犬の大きさ。実装は IMP-DOG-046 / 047 (有料の台を作り替える) の後に着手する。

2026-10-11 (統括、IMP-DOG-044 の制作から): プールは犬ごとの大きさに拡縮しない。ラボ 0.4.0 の計算では、ポメの倍率 (X 0.23 / Z 0.31) で丸い縁が幅 1.0〜1.4 cm の板につぶれ、縦板も 1.5 cm 幅になる。Q2 の決定の予備 (「崩れて見えたら有料の台を選んでいる間は 1.3 × 0.7 m のまま」) に従い、**プールを選んでいる間は 1.3 × 0.7 m のまま**にする (ラボ `handoff.md` 0.4.0 節)。小さい犬用が要るなら拡縮ではなく、別の大きさのプールを生成する (`config.json` の `addons_v04.pool`)。実装時は拡縮の対象からプールを外すこと。

## 記録

2026-10-12 (実装担当、コードと文書のみ。Unity の起動・メニュー・Play・保存はしていない): 設計は [設計書 §8](../../plans/2026-10-01-wash-call-and-height.md#8-2026-10-12-犬別の台の大きさ-imp-env-013)。

- `KH_WashStationCall.cs`: 同期しないフィールド `standTable` / `standSize` (Vector3[6]、null 宣言) / `standIdle` / `standBaseSize` / `standBasePos` / `standBaseScale` / `standSpeed` (1.5) と `[NonSerialized] standKeep`、`_StandSize()` (純粋)・`_StandPose(s)`・`StandStep()`。`StandStep` は `Update` の高さの早期 return より前。Start と最初の OnDeserialization で即座に合わせ、プールを選んだ瞬間も即座に元の大きさへ。同期変数は増やしていない
- `KH_WashSelection.cs`: `bathKeepsSize` (bool[]、プールだけ true、`_Ready` に入れない) と `_KeepsSize(bath)`。`_Apply` が `minLift` と同じ所で `call.standKeep` を書く
- 新しいメニュー `Wash/22 - Apply Stand Sizes` / `Wash/23 - Verify Stand Sizes` (`KH_DogWashSetupEditor.StandSize.cs`)。Wash/11 は変えていない
- `KH_WashPremiumEditor.cs`: WashPremium/21 の保全検査で、`KH_WashStationCall` の新しい 7 フィールドを比べる前に両側から外す (`StripStand`。基準の取り直しは不要)
- ClientSim: `W-stand-size` (群 `stand-size`、`Course/100f3`、群 `height` にも入る。`KH_CourseWalkHarnessEditor.WashStandSize.cs`)。回していない
- 規則で入江から変えた点: 幅に「足の中心の左右のずれ × 2」を足す (台は左右へ動かないので、両側の 5 cm を保つため)。idle は 6 匹の台をすべて含む長方形 (最大の犬 + ずれぶん。広がる向きしか無いことを保証)
- 検証: scratchpad の一時 csproj (Assembly-CSharp / -Editor の全ソース + ディスク上の新しいファイル、Unity が作った DLL を参照) で実行側・Editor 側ともエラー 0。U# のコンパイル、Wash/22・23、既存の Wash/12・32・WashPremium/21、Play は未実施
- 未確認: 裏山の足の実測値 (Wash/22 の報告から設計書 §8.2 へ写す)、Play の見た目 (檜 + ポメ、ラブ・サモエドの幅、縮む動き)、洗い中の姿勢、台に置いた道具、別の犬へ替えた時に前の犬が去る間の大きさ、2 人・途中参加、Quest
- 実機の確認: [Play チェックリスト](../../testing/play-checklist-2026-10-02.md) A0-63〜A0-65、B86

2026-10-12 (統括の Unity 検査): C# / U# コンパイル 0、Wash/22 4 stations、Wash/23 VERIFY_OK 265、Wash/12 PASS、WashPremium/21 PASS、保存済み。Wash/32 の FAIL 32 件は全部が有料の道具の見た目 (別セッションの領域) で無関係。実測値 (4 か所同じ、犬の前 = Lift +X): コーギー 0.4630 × 0.3243 (+0.0341)、ポメ 0.3002 × 0.2216 (−0.0253)、ラブ 0.7745 × 0.3822 (+0.0153)、ダックス 0.4435 × 0.2345 (+0.0087)、柴 0.5433 × 0.3266 (+0.0308)、サモエド 0.7612 × 0.3195 (+0.0158)、idle = ラブと同じ。表は設計書 §8.2。Codex 独立レビュー (高なし) の中 2・低 1 を反映 (実装担当、コードと文書のみ): プールへ切り替えた時はモデルを表示する前に台を即時に元の大きさへ (`KH_WashStationCall._StandSnapNow`、`KH_WashSelection._Apply` から)、`W-stand-size` の判定を同期の切り替わりの時刻から (開始 1 フレーム以内・着く前に途中の大きさ・着く前に寄せ終わる)、値の表と「寄せている途中の大きさは端末ごとに同時とは限らない」を設計書へ。再コンパイル → Wash/23 → 100f3 は統括
2026-10-12 (統括): 修正後の Unity 検査 C#/U# 0、Wash/23 VERIFY_OK 265。ClientSim Course/100f3 W-stand-size **PASS** (run 20261012-025039、受理から 1 フレーム以内に縮み始め・1.5 m/s・着く前に寄せ終わり・おわりで idle へ)。コミット d86a3a5。残りは実機 (A0-63〜65、B86)。
