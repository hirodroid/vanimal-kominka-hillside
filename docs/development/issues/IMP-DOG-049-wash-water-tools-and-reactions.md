---
id: IMP-DOG-049
type: IMP
system: DOG
status: wip
priority: Medium
summary: ホースの連続放水・水鉄砲の単発射出・じょうろの傾け注ぎを整え、ホースと水鉄砲に犬の分かりやすい反応を組み合わせる
next: 段 A (水鉄砲・ホース・プールの取り込みと道具の動き) のコードを書いた (2026-10-11、C# / U# コンパイル 0 (統括)、Codex 独立レビューの指摘 4 件を反映、未適用)。次は統括が「段 A 実装」の順で Unity へ適用 (コンパイル → 12 → 43 → 41 → 21b → 49 → 44 → 21 → Wash/12 → 保存 → 45)。じょうろの傾け注ぎは段 A から外した (下の記録)。段 B (犬の反応) は未着手
updated: 2026-10-11
---

# IMP-DOG-049 水道具の体験と犬の反応

## 依頼

2026-10-10 ユーザーの[最終9品方針](DES-DOG-008-wash-paid-tools-and-effects.md)に基づく。標準シャワーと体験が重なる角型シャワーを外し、水の道具はじょうろ・ホース・水鉄砲にする。

| 道具 | 決まった方向 | 未確定 |
|---|---|---|
| じょうろ（既存） | 傾けて注ぐ変わり種 | 傾き判定、注水量、Desktop/Touchで相当する操作 |
| ホース（新規） | 連続して勢いよく水を出す | 意匠、ホース本体の表現、操作、射程・流量 |
| 水鉄砲（新規） | 一発ずつ水を飛ばす | 意匠、射出表現、操作、射程・発射間隔 |

新しい水鉄砲は、除外したアヒル・魚・竹モデルの再採用を意味しない。ホース・水鉄砲の形状は未指定で、VAnimalのブランド方針とモバイル予算に沿って設計する。

## 犬の反応

ホースと水鉄砲には、**犬が水にじゃれる・跳ねるなど、大きく分かりやすい反応**を組み合わせる方針。以下は実装前に詰める項目で、既定値・動作はまだ決めない。

- 動きの種類、道具による違い、犬ごとの体格への適合。
- 水や狙いとの接触条件、反応可能な犬の状態、頻度、連続放水時の繰り返し方、反応終了条件。
- 洗浄台からのはみ出し、既存の洗浄保持・移動・散歩などとの競合。
- 複数人・途中参加・Owner交代での反応共有と、音・水しぶきの端末負荷。

じょうろに同じ反応を付けるとは今回決めていない。無料の標準道具とDesktop用の標準スポンジは維持する。

## 関連と完了条件

種類別同時利用は[IMP-DOG-046](IMP-DOG-046-wash-tools-by-category.md)、構成・画像の更新は[IMP-DOG-047](IMP-DOG-047-wash-item-review-changes.md)、プールは[IMP-DOG-044](IMP-DOG-044-wash-pool-platform.md)。

- 連続放水・単発射出・傾け注ぎが、それぞれ操作と見た目で区別できる。
- 合意した反応条件で犬が大きく分かりやすく反応し、終了・抑制条件を守る。
- 左右の保持、Desktop/Touch操作、洗浄との両立、2人同期、対象端末の見た目と負荷を確認し、方法と結果を記録する。

今回は方針の記録と起票のみ。モデル・Unity・アニメーションは未着手。

2026-10-11: 設計案を [docs/plans/2026-10-11-wash-items-design.md](../../plans/2026-10-11-wash-items-design.md) §3 に作成 (推奨はホース=連続でじゃれる、水鉄砲=単発でその場で跳ねる、台に留めた犬だけ、ブルブルと同じ同期の形)。ユーザーの判断待ち: Q7 洗う効きを無料シャワーと同じにするか、Q8 意匠の進め方、Q9 ホース本体の見せ方、Q10 反応の種類、Q11 反応する犬の範囲、Q12 頻度の仮値。

決定: 2026-10-11 (ユーザー「推奨でOK」) [設計案](../../plans/2026-10-11-wash-items-design.md) の Q7〜Q12 を推奨どおり採用。

2026-10-11: 企画画像 (各 A / B の 2 案) を作った。画像はラボ `vrc-asset-lab/assets/wash-premium/design/2026-10-11/`、確認用ページは [artifacts/wash-concept-review/2026-10-11](../../../artifacts/wash-concept-review/2026-10-11/README.md)。ユーザーが案を選んでから Blender で制作する。

2026-10-11 (ユーザー回答 [JSON](../../../artifacts/wash-concept-review/2026-10-11/vanimal-wash-concept-2026-10-11T01-43-01-752Z.json)): 水鉄砲は案 A。ホースは案 A を元に「色は B、持ち手と先端は A」→ リールの形を 2 通りにした 2 回目の企画画像で確認中 ([round2](../../../artifacts/wash-concept-review/2026-10-11/round2/index.html))。
決定: 2026-10-11 (ユーザー) 水鉄砲の意匠は企画画像の案 A (横に水の窓、短い丸いノズル)。ホースのノズルは案 A の形。

2026-10-11 (ユーザー 2 回目の回答 [JSON](../../../artifacts/wash-concept-review/2026-10-11/round2/vanimal-wash-concept-r2-2026-10-11T02-02-00-932Z.json)): ホースはリールも棚とのつながりも作らない。「ホースの途中と先端でいいな。リール不要。ちょっと頑張るところじゃないか。ホース感だけでていればいい。自由に持って動くからつながっているとちょっと大変すぎるかも」。ノズルは案 A (アイボリーの胴・深緑の握り) のまま、短いホースが手元から垂れる形。1 回目の「色は B」はリールの色のことで、リールが無くなったので関係しない。線 (LineRenderer) の描画と毎フレームの更新は不要になった (設計案 §3.2・§3.6 に注記)。ホースの長さ (30 cm の S 字 / 50 cm のひと巻き) をラボの `design/2026-10-11/hose-concept-r3.png` で確認中。
決定: 2026-10-11 (ユーザー) Q9 を取り下げ、ホースはノズル + 短いホースだけ (リール・つながり無し)。
水鉄砲 (案 A) は 2026-10-11 にラボで 0.4.0 として Blender 制作に着手。

決定: 2026-10-11 (ユーザー「ホースは１だね」) ホースは企画画像 3 回目の 1: 案 A のノズルに約 30 cm の短いホースが S 字に垂れる形 (ラボ `design/2026-10-11/hose-concept-r3.png`)。リール・つながり無し。ラボで 0.4.0 に追加して制作する。

2026-10-11: ラボ 0.4.0 にホースを追加 (`04a798e`)。Hose 2560 tri、Hose_GunHold は水鉄砲・泡銃と同じ Gun の向き。水鉄砲は `67907d8`。どちらも Blender の検査だけで、Unity 取り込み・手持ち・実機は未確認。

## 取り込みと実装の進め方 (2026-10-11、ユーザー「完了まで適宜レビューを受けながらすすめてください」)

段 A (取り込みと道具の動き、IMP-DOG-044 のプールを含む) → 段 B (犬の反応)。この作業の完了の定義:

- 3 品 (水鉄砲・ホース・プール) をワールドへ取り込み、水の枠と台の枠へ追加し、C# / U# コンパイル 0。
- Codex 独立レビューの指摘を反映 (段 A のコード、段 B の同期の変更の両方)。
- シーンへ適用し、44 (基準 707 PASS) と Wash/12 (基準 FAIL 0) を基準と比べて悪化なし。YAML ドキュメント単位の差分が意図どおり。
- 写真・リスティング画像を撮り直し、目視で確かめる。
- すべてコミットし、IMP-DOG-049 / 044 を review にして、ユーザー確認の一覧を書く。

PCVR・2 人・Quest / Android の確認は「未確認」として残す (この作業の合格に含めない)。ハーネスは相談で決めた群だけ。

## 段 A 実装 (2026-10-11、未適用)

コードと記録。**C# / U# コンパイル 0 (2026-10-11、統括)。** Codex 独立レビュー (2 回) の指摘は下の「Codex レビューの反映」で直した。1 回目の反映後もコンパイル 0 (統括)。**2 回目の反映 (発射番号の直し) の後に再コンパイルが要る。**Unity の操作・保存・Play は未実施。プール (IMP-DOG-044) も同じ変更・同じ手順。ラボの引き渡しは `vrc-asset-lab/assets/wash-premium/handoff.md` の「0.4.0 追加」、素材は `Assets/KominkaHillside/Models/WashPremium/` (統括がコピー済み、`origin-0.4.0.json`)。

### 品の番号と枠 (IMP-DOG-047 の「足す所」1〜5 に沿う)

- 番号は末尾に足した: **17 = WaterPistol、18 = Hose、19 = PoolBath**。既存の 0〜16 とハーネスの定数は変えていない。
- `KH_WashSelectionEditor.cs`: `Names` に 3 品、`Groups` = 水 / 水 / 台、`Slots[17] = Slots[18] = 2` (じょうろと同じ水の枠。13〜16 は台なので -1 で埋めた)。
- `Category.cs`: `Order = {0, 9, 6, 17, 18, 7, 8, 13, 19}` (未制作の 3 品を一覧に出す。Q4 の隠しはこれで外れる)、`ItemWords[17..19] = 43, 44, 45`、`WordCount = 46`、`BathDepth(19) = .036`。
- 文言 (`Data/WashPremium/words.json`、13 言語、**AI 作成・校正なし**): 末尾に名前 3 語 (水鉄砲 / ホース / プール ほか)。セット説明 16 (水) に「ホースは押している間、水鉄砲はひと押しで一発」、18 (洗い場) に「檜の台かプール」を足した。43 で焼く新しい字は 20 字 (`Шプホ住发喉射張或池発發管續续동안풀호`、node で集計)。
- 写真 41 のキャプション (`ListingSubtitles`) を 9 品に合わせ、出力先を `artifacts/wash-listings/2026-10-11-040/` に変えた (6 品の `2026-10-11/` は IMP-DOG-047 の記録として残す)。ボードの `Photos/Set0〜4.png` と `Item17〜19.png` は 41 が書く。

### 取り込み (新メニュー `WashPremium/12 - Import 0.4.0 Visuals`、`KH_WashPremiumEditor.Addons.cs`)

- 10 は 0.3.0 の 18 品のまま (再実行しない)。12 は 3 FBX と `PremiumAddons_{BaseColor,Normal,MetallicSmoothness}` だけ。画像・モデルの取り込み設定は 10 と同じ (Normal は NormalMap、sRGB は BaseColor だけ、MS の α は入力から、Trilinear・Aniso 4、Android 1024 ASTC 6x6。Addons は 1024)。
- 材質は子の名前で割り当てる (`materialImportMode = None` なので FBX の材質名は Renderer から読めない。ラボの build-v04.json で各子 1 材質を確認): `*_Mesh` (水鉄砲の胴・ホースのノズル) → 既存 `PremiumWash_Tools`、`Hose_Tube`・`PoolBath_Floor/Wall/Rim` → 新 `PremiumWash_Addons` (Standard 不透明、Tools / Fixture と同じ作り)、`WaterPistol_Window` → 新 `PremiumWash_Window`、`PoolBath_Water` → 新 `PremiumWash_Water`。知らない子・サブメッシュ 2 以上は例外で止める。
- 透明 2 材質: Standard **Fade** (面全体が薄くなる。映り込みは切る `_GLOSSYREFLECTIONS_OFF`)、なめらかさ .55 / .5、色は水 (0.62, 0.80, 0.84, α .38)・窓 (0.74, 0.88, 0.90, α .45) (仮。AI が選んだ値)、影なし、**renderQueue 2990**。
  - 描き順の根拠: 洗いの粒子の材質はすべて 3000 (`WashMist`、`KH_WashParticle_Foam / Spray / Splash`)、犬の濡れ・泡の殻は `KH_WashShell.shader` の Transparent+10 = 3010。水面・窓を先に描き、粒子、殻の順になる (水越しの足先の泡は殻が上に描かれる)。実際の見え方は **Play / 実機で確かめる** (Inspector で Mode を触ると 3000 に戻る。21 が 2990 ちょうどを、44 が「粒子 3000・殻 3010 より前」の順を検査)。
- Prefab は 10 と同じ形 (`Prefabs/WashPremium/<名前>.prefab` = 空の親 + FBX)。受領書 `Temp/WashPremium/import-040.txt`: Prefab ごとの bounds・三角形数・子と材質・全アンカーの Unity 側の位置と軸、水鉄砲・ホースの「GunHold +X と NozzleAnchor 前方の角度」「GunHold +Z とモデル上方向の角度」(どちらか 1° を超えたら受領書を書いてから例外)、プールの支柱の取り付け深さ。

### シーンへの配置 (新メニュー `WashPremium/49 - Place 0.4.0 Items`、`KH_WashSelectionEditor.Water.cs`)

- 開始時に dirty なら止まる。4 洗い場に冪等 (2 回目は作らずに値を書き直す)。dirty にするが**保存しない**。受領書 `Temp/WashSelection/place-040.txt`。
- 順番: 品ごとの配列を 20 に伸ばす (既存の値は残す) → 3 品を置く → **48 の本体 (`ConfigureStation` + `ConfigureBoard`) を同じ実行で回す** → 非表示の展示 `KH_WashPremiumPreview_Inactive` に 3 品を足す (21 が `Order.Length` = 9 を数える) → NetworkIDs の掃除 (48 と同じ)。
  - `Order` に 3 品を載せたので、**49 を回す前の 48 は「offered item has no model」で止まる** (意図どおり)。49 の後は 48 も冪等に回る。
- 置き方:
  - 水鉄砲: 枠 2 の下、握りで立つ (最低点を枠の高さへ。42 と同じ)。
  - ホース: 高さ 0.485 m (ホースが持つ点の 0.335 m 下まで垂れる) で、トレー (奥行き 0.42 m、ミトンとタオルの間の横幅 0.23 m) に平らには収まらない。**トレーの手前の縁に掛ける**: 胴はトレーの上、握り・継ぎ手・ホースは手前の縁の外に垂らす (`HoseRestLocal = (0, -0.074, -0.14)`、枠の中、回転なし)。FBX の頂点を node で読んで決めた。
  - プール: `Lift/WashTable` の原点 (檜と同じ置き方)。層 17 の長方形の当たり判定はそのまま。犬ごとの大きさ (IMP-ENV-013) は掛けない (ラボの既定の逃げ道: プールは 1.3 × 0.7 のまま)。
- トレーの予測 (当たり判定の箱 = `TightBounds`、4 洗い場で同じ):
  - 水鉄砲 x -1.107..-1.033、z -0.127..0.059 → 間隔 ミトン 9.1 cm・泡銃 10.1 cm・タオル 8.3 cm・ドライヤー 20.7 cm。
  - ホース x -1.104..-1.036、z -0.286..-0.067 (手前の縁 -0.18 を越えて垂れる)、y 0.411..0.896 → 間隔 ミトン 9.4 cm・泡銃 10.4 cm・タオル 8.6 cm・ドライヤー 21.0 cm。トレーの高さ (0.77..0.80) にある頂点のいちばん奥が z -0.191 (縁から 1.1 cm 手前)、トレーの箱の中の頂点 0。
  - 水の品でいちばん狭いのは今までどおりじょうろ (ミトン 5.3 cm、タオル 4.5 cm)。棚の左端との間隔 2.0 cm (タオル) も変わらない。

### 持ち方 (Gun) と道具の動き

- `FoamCannonHold` を `GunHolds(sel)` に広げた (`Refine.cs`、`GunItems = {0, 17, 18}`)。泡銃は今までどおり子 `KH_FoamCannonGunHold` と枠 0 の Gun。水鉄砲・ホースは **FBX の `*_GunHold` をそのまま** `gunHolds[17/18]` に入れる (ラボの軸 +X = 噴射、+Z = 上 は泡銃の持つ点と同じ。12 と 44 が角度を測る)。水の枠はじょうろ (Grip) と共有なので Pickup には直接書かず、実行時の `_Apply` が出ている品に合わせて Gun / Grip を切り替える (IMP-DOG-046 の設計どおり)。何も出ていない水の枠は Grip に戻す (48 / 49、44 の後片付け `RestHolds`)。PCVR で手の位置を変えたくなったら、泡銃と同じく子を作って `gunHolds` を向ける。
- メニュー 47 は `47 - Apply Gun Holds` に改名 (中身は 3 品分。48 / 49 も同じ関数を呼ぶので通常は不要)。
- 入力 (**U# の実行時コードを最小で変更**、`KH_WashTool.cs` / `KH_WashToolState.cs`): `pulseSeconds` の意味を 3 通りにした。トグルと押し続けは従来の `state._SetSpraying` (`spraying`)、1 発は下の発射番号。
  - 0 = 押すたびに ON / OFF (無料シャワー・じょうろ。変更なし)。
  - \> 0 = **1 回押すと 1 発**: `pulseSeconds` の間だけ出る。出ている間の押しは無視 (以前は延長されて連射の帯になった) → 間隔 = 0.4 s (`WaterPistolShot`)。他の人へは `spraying` ではなく**発射番号**で配る (下の「Codex レビューの反映」2)。
  - `KH_WashTool.PulseHold` (-1) = **押している間だけ出る** (ホース。設計案 §3.2。2026-09-30 の「押し続けは疲れるのでトグル」は無料シャワーの決定で、ホースは承認済みの設計案に従う)。Use を離す・落とす・置き場へ戻すで止まる。
- 届く距離: `sprayRange` は枠ごとの値だったので、水の枠で 3 品が共有できるよう**品ごとの `KH_WashSelection.sprayRanges[]`** を足した (U#、同期しない設定値。`_Ready` が長さを確かめ、`ConfigureSlot` が枠へ書く)。じょうろ 1.5 m、ホース 2.5 m、水鉄砲 4 m (設計案の仮値)、ドライヤー 0.8 m、他 1.5 m (従来の枠の値)。宣言は null (vrc-udonsharp §4)。
- 粒子 (各ノズルの子 `UseMist`、49 が毎回書く。値は `Water.cs` の定数で仮値):
  - ホース: 連続、毎秒 110、寿命 0.4 s、速さ 6〜7 m/s、大きさ 0.02〜0.034 m (無料シャワーより太い)、重力 .6、上限 48 (生きている数 ≈ 44、設計案 §3.6 の 48 以下)、Stretch。
  - 水鉄砲: 1 回 10 粒のバースト (設計案の 8〜12)、寿命 0.55 s、速さ 8〜8.5 m/s、重力 .5 (約 4.5 m で弧を描く)、Billboard (玉)、上限 24 (寿命 0.55 s > 間隔 0.4 s なので 2 発分が重なっても足りる。`PistolMaxParticles`)。
  - 両方: 無料シャワーの材質 `KH_WashParticle_Spray` (3000)、Collision は World・Default + Environment (`SprayMask`。Wash/12 の「floor 22 を含まない」を満たす)・当たると消える、犬の部位 Collider の Trigger で消える (無料シャワーと同じ)。音は `WaterJet`。
  - 洗う効きは従来どおり道具の Raycast (Q7: 無料シャワーと同じ効き)。粒子は見た目だけ。

### じょうろの傾け注ぎ (段 A から外した)

この Issue の完了条件「連続放水・単発射出・傾け注ぎが区別できる」に入っているが、段 A の完了の定義 (上の 5 項目) には無い。VR の手の傾きの判定、Desktop / スマホの自動の傾き、他の人への見せ方 (`KH_WashToolState` の配り方) と、ハーネスのじょうろのケース (`P-selection-water-toggle` はトグルを前提) の作り直しが要り、小さな変更ではない。**段 A では入れず、じょうろはトグルのまま。** 段 B の後に別の段で扱う (この Issue を done にする前に必要)。

### 検査の追加 (緩めた所は無い。21 は基準ファイルが無いと失敗のまま、21b で取る)

- 44 (`Verify.cs` / `Category.cs` / `Water.cs`): 品の配列の長さ 20 と `sprayRanges`、`bathDepths` (檜 .038・プール .036)、保存された backing の `gunHolds` (Gun の 3 品だけ非 null)・`sprayRanges`・`pulseDurations` (じょうろ 0 / 水鉄砲 0.4 / ホース -1)、各洗い場で水鉄砲・ホースの持つ点の角度 (`hold WaterPistol <洗い場> ... aimErr / upErr`、各 0.5° 未満、ノズルが持つ点より 0.12 m 以上前)、水の枠は何も出ていない間 Grip、置き方 (ホースの位置・水鉄砲の接地・プールの原点)、プールと檜の**支柱の取り付け深さの実測** (天板の Collider の下面 − `SupportMount`、1 mm 以内)、粒子 (ノズルの下・ループ / バースト・World 衝突のマスク・Trigger・材質 3000・上限)、音、透明材質の Queue (水面・窓 < 粒子 3000 < 殻 3010)。トレー: 全品の足跡がトレーの内側 (ホースだけ「横と奥は内側、手前は縁を越える」)、どの品の頂点もトレーの箱に入らない、ホースの縁との間隔を `hang` 行に出す。単品の反映ごとに枠の持ち方・粒子・入力・距離。論理: 水の枠を 水鉄砲 → ホース → じょうろ と替えて Gun / Gun / Grip とノズル・入力・距離が付いて回ること、台をプールにすると檜が隠れて支柱の深さが .036 になること。
- 21 (`KH_WashPremiumEditor.cs`): 0.4.0 の 3 Prefab の子ごとの材質・三角形 8,414 (2508 + 2560 + 3346)・Addons 画像の取り込み・透明材質 (Queue 2990・ZWrite 0・Fade)、展示の数 = `Order.Length` (9)。実行時の保全 (各洗い場の `KH_WashStationCall`・全 `KH_WashTool`・天板まわりの Collider の JSON) は従来どおり基準と比べ、**基準ファイルが無ければ例外で失敗** (PASS にしない)。基準は新メニュー `21b - Capture Integration Baseline` が保存済みのシーンから取る (`Temp/WashPremium/runtime-before.txt` と globalID にした `runtime-before-stable.txt`)。
- Wash/12: 変更なし (水鉄砲・ホースの粒子は `SprayMask` を持ち、選ばれていない枠の `spray` は空。泡銃の枠 0 の数え方も変わらない)。
- ハーネス `Testing/KH_CourseWalkHarnessEditor.WashPremium.cs`: `PremiumBind` の `Order` の期待を 9 品に、定数 `PWaterPistol / PHose / PPool` を足しただけ (ケースは変えていない。ホースの押し続け・水鉄砲の 1 発は未カバー。回すかは相談)。

### 変更したファイル

- U#: `Scripts/KH_WashTool.cs` (入力 3 通り・他の人の 1 発 `_OnShot`)、`Scripts/KH_WashToolState.cs` (発射番号 `shotSerial` / `shotServerMs`、`_Shot`)、`Scripts/KH_WashSelection.cs` (`sprayRanges`)、`Scripts/KH_WashSelectionBoard.cs` (`wordCount` の既定 46。シーンの値は 49 / 48 が書く)。新しい U# ファイル・Program Asset は無い。
- Editor: `KH_WashPremiumEditor.cs` (partial 化、21、21b)、**新規** `KH_WashPremiumEditor.Addons.cs` (12、21 の 0.4.0 部分)、`KH_WashSelectionEditor.cs` (カタログ・キャプション・出力先)、`.Category.cs` (Order・語・深さ・距離・48 の本体・44)、`.Refine.cs` (`GunHolds`・47)、`.Verify.cs` (44・45)、**新規** `.Water.cs` (49・44 の 0.4.0 部分)、`.Build.cs` (42 も 3 品を置く)、`Testing/KH_CourseWalkHarnessEditor.WashPremium.cs`。
- データ: `Data/WashPremium/words.json`。
- Unity が作るもの (コミット時に stage): 新しい 2 つの Editor `.cs` の `.meta`、3 FBX と Addons 3 画像の `.meta`、`PremiumWash_Addons / Window / Water.mat`、`Prefabs/WashPremium/WaterPistol / Hose / PoolBath.prefab`、`Photos/Item17〜19.png` と更新された `Set0〜4.png`・`Item*.png`、`artifacts/wash-listings/2026-10-11-040/`。

### Unity で行う手順 (統括、この順で)

0. Codex 独立レビュー (段 A のコード) — 済 (2026-10-11、指摘 4 件を反映)。
1. `bash scripts/unity-compile.sh --udon` — C# / U# のエラー 0 (この回の修正後に再コンパイルが要る。`KH_WashToolState` の同期変数が 3 つ増えたので Udon も)。
2. `Tools/KominkaHillside/WashPremium/12 - Import 0.4.0 Visuals` — `Temp/WashPremium/import-040.txt` の先頭が `PASS`。期待: `WaterPistol ... tris=2508` / `Hose ... tris=2560` / `PoolBath ... tris=3346`、`gun WaterPistol angle(GunHold +X, NozzleAnchor forward)=0.000deg angle(GunHold +Z, model up)=0.000deg nozzleInHold=(0.152, 0.000, 0.000)` 前後 (ホースは x 0.173)、`pool mount depth ... 0.0360`、`material PremiumWash_Water queue=2990`。
3. `WashPremium/43 - Prepare Care Font` — **シーンが保存済みの状態で、49 より先に** (43 は保存する)。`Temp/WashSelection/font.txt` の `missing=0`。新しい字 20。
4. `WashPremium/41 - Photograph Listings` — **49 より先に** (49 の `ConfigureBoard` が `Photos/Item17〜19.png` を参照し、44 が「載せた品は写真あり」を検査するため)。シーンは変えない。出力 `artifacts/wash-listings/2026-10-11-040/01-foam〜05-complete.png` と `Data/WashPremium/Photos/`。画像は統括・ユーザーが見る (全部入りは 9 品・5 列、水は 3 品、洗い場は 2 品)。
4b. `WashPremium/21b - Capture Integration Baseline` — **シーンが保存済みの状態で、49 の直前に**。`Temp/WashPremium/baseline.txt` に `stations=4 snapshot lines=…`。21 が 49 の後にこの基準と比べる。
5. `WashPremium/49 - Place 0.4.0 Items` — `Temp/WashSelection/place-040.txt`。期待: `selectors=4 created models=12 ... offered=FoamCannon,PawMitt,WateringCan,WaterPistol,Hose,Dryer,PatternTowel,HinokiBath,PoolBath`、各洗い場で `created .../Premium_<洗い場>_Slot2/WaterPistol`・`.../Slot2/Hose`・`.../Lift/WashTable/PoolBath`、`Hose ... at (0.000, -0.074, -0.140) pulse=-1 range=2.5`、`WaterPistol ... pulse=0.4 range=4`、`collider <洗い場> WaterPistol / Hose size ...` (新規なので旧値 0 → 新値)、`gallery + KH_WashPremiumPreview_Inactive/...` 3 行、`NetworkIDs before / after` (型付きの WARN が出たら止める)。
6. `WashPremium/44 - Verify Selection` — `Temp/WashSelection/verify.txt` が `PASS` (基準 707 より行が増える。FAIL 0)。主な新しい行:
   - `PASS four stations own distinct pickups (28) and visuals (36 / 36: FoamCannon, PawMitt, WateringCan, WaterPistol, Hose, Dryer, PatternTowel, HinokiBath, PoolBath)`、`PASS 13 languages / 46 words`、`PASS all 20 visual prefabs kept in Assets`
   - 各洗い場: `PASS itemCategories <洗い場> = [0,-1,-1,-1,-1,-1,1,2,2,0,-1,-1,-1,3,-1,-1,-1,1,1,3]`、`PASS itemSlots ... = [0,-1,-1,-1,-1,-1,2,4,3,1,-1,-1,-1,-1,-1,-1,-1,2,2,-1]`、`PASS bathDepths ... (hinoki 0.038, pool 0.036)`、`PASS sprayRanges ... (can 1.5 / pistol 4.0 / hose 2.5 m)`
   - `footprint <洗い場> WaterPistol x -1.107..-1.033 z -0.127..0.059`、`footprint ... Hose x -1.104..-1.036 z -0.286..-0.067`、`PASS tray gap WaterPistol / PawMitt ... 9.1 cm` ほか、`PASS footprint Hose hung over the tray's front lip`、`PASS no Hose vertex inside the tray box (0 / …)`、`hang <洗い場> Hose: nearest vertex at the tray's height z -0.1914 (front edge -0.180, clearance 1.1 cm)`
   - `PASS saved backing gunHolds: FoamCannon KH_FoamCannonGunHold, WaterPistol WaterPistol_GunHold, Hose Hose_GunHold, others Grip`、`PASS saved backing pulseDurations: can toggle 0, pistol shot 0.40 s, hose hold -1`、`PASS slot2 (water: can Grip / pistol, hose Gun) rests as Grip while nothing is out`
   - `hold WaterPistol <洗い場> sprayInHold=(1.000, 0.000, 0.000) modelUpInHold=(0.000, 0.000, 1.000) aimErr=0.00deg upErr=0.00deg ...`、同じく Hose。`PASS mount depth HinokiBath ... measured 0.0380 m`、`PASS mount depth PoolBath ... measured 0.0360 m`
   - `PASS WaterPistol spray ...: one burst of 10 ...`、`PASS Hose spray ...: loop 110/s ...`、`PASS transparent queues ...: PremiumWash_Water 2990 / PremiumWash_Window 2990 < WashMist 3000 / KH_WashParticle_Spray 3000 < wet shell 3010`
   - 単品: `PASS hold WaterPistol Gun WaterPistol_GunHold on slot2`、`PASS slot2 spray / input / reach for Hose (pulse -1, range 2.5 m)`。論理: `PASS water swap -> WaterPistol: slot2 Gun, pulse 0.4, range 4 m ...`・`-> Hose: slot2 Gun, pulse -1, range 2.5 m`・`-> WateringCan: slot2 Grip, pulse 0, range 1.5 m`、`PASS held water: platform -> PoolBath shown, hinoki hidden, support mount 0.036`
   - 既存の行 (泡銃の `hold FoamCannon ... aimErr=0.00deg`、Q1〜Q3、`rendered locale overflow=0 missing=0`、`every word's glyphs baked`) がそのまま PASS。
7. `WashPremium/21 - Verify Integration` — `Temp/WashPremium/verify.txt` に `PASS 3 0.4.0 models / 8414 tris / ...` と `PASS 18 models / 19604 tris / ... existing runtime preservation (Temp/WashPremium/runtime-before*.txt) / gallery 9 offered items`。**許してよい差分は無い見込み**: 49 は基準に入らない物 (新しいモデル・粒子・プール) を足すだけで、既存の枠の `KH_WashTool` には 44 の後で以前と同じ値 (枠 2 = じょうろのノズル・距離 1.5 など) が残る。`Stable runtime preservation failed` が出たら `runtime-before-stable.txt` と新しい snapshot を行ごとに比べ、違いが「既存の枠の道具の値が 44 で最後に設定した品のまま」だけなら記録して判断する (コードは緩めない)。基準が無ければ `No runtime baseline` で止まる → 21b からやり直し。
8. `Tools/KominkaHillside/Wash/12 - Verify Wash (All Dogs)` — 基準どおり FAIL 0。
9. シーンを保存 (stage は vrc-scene-edit-safety の手順で分ける)。保存後にもう一度 44。`typelessNull` が 0 でなければ 49 をもう一度 → 44 → 保存。差分の見込み: 4 洗い場で 3 品のプレハブインスタンス 12 個とノズルの下の粒子 8 個、展示の 3 個、`KH_WashSelection` の配列 (20 品・`sprayRanges`)、ボードの値 (語 46・order・写真)、枠 2 の Pickup (Grip のまま)・当たり判定。
10. `WashPremium/45 - Capture Selection` — `artifacts/wash-listings/2026-10-11-040/evidence/board.png`・`station.png` (泡銃・ホース・ドライヤー・プールを出した状態。ホースがトレーの縁に掛かって見えること)。シーンは元に戻る。

### 残り・リスク

- **未確認 (Play / 実機)**: 水面・窓と泡・しぶきの粒子の描き順、ホースを持ったときに垂れた管が腕・体に当たって見えるか、Gun の持ち方 (PCVR 右手・左手、Desktop)、ホースの押し続けと水鉄砲の 1 発の手触り、粒子の量・射程・音、Quest / Android の負荷 (透明 1 枚 × 4 か所と Addons 画像 1 組)。
- 水鉄砲の 1 発は発射番号で配る (下の反映 2・5)。**受け入れた制限**: Manual 同期は続けて撃った発射を 1 回の更新にまとめることがあり、他の人には撃った数より少ないバーストに見える (番号が進めば 1 回だけ出す)。他の人にとって 1 発は見た目だけ (洗いは持っている人の Raycast で、KH_DogWash が別に送る) なので、これでよい。所有権が届く前の発射は最大 3 発まで数えて、届いたときにまとめて書く (時刻は最後の発射の押した時刻。古ければ相手が出さない)。2 人で確かめる。
- 洗いの Raycast は直線、粒子は弧を描く。遠く (水鉄砲 3 m 以上・ホース 2 m 以上) では当たった場所と見た目がずれる (無料シャワーと同じ性質)。
- ホースの掛け方は見た目の判断 (ユーザー確認)。当たり判定の箱がトレーの手前に縦長に垂れる (y 0.41〜0.90) ので、トレーへ手を伸ばしたときに掴みやすい・掴みにくいは Play で見る。コースの離隔検査 (Wash/12) は Pickup の当たり判定を除くので影響しない見込み。
- 文言 16 が長くなった (de / it / es / fr / pt-br が 134〜142 字)。44 の `rendered locale overflow` が FAIL なら短くする。全部入りのタブは 9 品で 2 ページ (「次の写真」ボタンが初めて出る)。44 の描画検査は 1 ページ目だけ。
- `KH_WashSelection._Ready` が `sprayRanges` を要求するので、コンパイル後〜49 の前のシーンでは選択が動かない (その間に Play しない)。
- 段 B (犬の反応) と傾け注ぎは未着手。`issue_summary.md` は frontmatter を変えたので統括が `node scripts/check-issues.js --write` を回す。

### Codex レビューの反映 (2026-10-11、コードのみ。1 回目の反映後はコンパイル 0 (統括)、2 回目 (5) の後は再コンパイルが要る)

1. (Medium) 水鉄砲の粒子の上限: 10 粒 × 寿命 0.55 s に対して間隔 0.4 s、上限 12 では続けて撃つと 2 発目が欠ける (`Stop()` は飛んでいる粒を消さない)。上限を `PistolMaxParticles = 2 × 10 + 4 = 24` にし、44 が「上限 ≥ 2 発分」を検査して値を出す。
2. (Medium) 他の人に 1 発が届かない: 1 発を `spraying` の ON → OFF で配っていたので、(a) 状態の所有権が 0.4 s 以内に来ないと、待っている希望が OFF に上書きされて ON が送られない、(b) OFF → ON がまとまると相手の表示が出たままになり次のバーストが始まらない。**1 発の道具 (`pulseSeconds > 0`) だけ**、`KH_WashToolState` に同期変数 `shotSerial` (Owner が 1 発ごとに +1) と `shotServerMs` (撃ったサーバー時刻) を足した。持っている人は `_Shot()` を呼ぶ (Owner ならすぐ書く。違えば所有権を頼み、届いたときにその発射が 1 秒以内なら書く。頼むのは 3 秒に 1 回まで、`_SetSpraying` と同じ形)。他の人は `OnDeserialization` で番号が変わったら、撃った時刻が 1.5 秒以内 (`ShotFreshMs`) のときだけ `KH_WashTool._OnShot()` で 1 回分出す (pulseSeconds の間 `Want()` が真。前の表示中ならバーストを出し直す)。(途中参加の扱い・時刻・品の照合は 5 で直した。) トグル・押し続けは `spraying` のまま変えていない。ローカル専用のメソッドは `_` 始まり (vrc-networking §1)、同期変数は Owner だけが書く。
3. (Medium) 21 の基準が無いときの `SKIP` → 全体 PASS は検査を緩めていた。基準が無ければ例外で失敗に戻し、基準を取る `21b - Capture Integration Baseline` を足した (手順 4b)。
4. (Low) 文書: 「緩めた所は無い」を 3 に合わせ、Queue の検査の分担 (21 = 2990 ちょうど、44 = 順) を正し、コンパイル済みを記録。words.json 16 のホースは「引き金を押している間」と分かるように直した (en `while you hold the trigger`、ru / uk `пока нажат курок` / `доки натиснуто курок`、de `solange der Abzug gedrückt ist`、es / pt-br `el gatillo` / `o gatilho`、fr `tant qu'on tient la détente`、it `il grilletto`、pl `dopóki trzymasz spust`。ja / ko / zh は元から「押している間」「按住时」)。43 で焼く新しい字は 20 のまま。
5. (2 回目の再レビュー、Medium × 3) 発射番号の直し (`KH_WashToolState.cs` / `KH_WashTool.cs`):
   - 途中参加: 最初に受け取る状態 (参加時の状態) は番号を記録するだけで、**決して再生しない** (`_received`)。時刻の新しさだけでは、参加直後に受け取った直前の発射を再生していた。代わりに、その場にいて一度も受け取っていなかった人は最初の 1 発を見ない (見た目だけなので受け入れる)。
   - 品の照合: 同期変数 `shotItem` (撃った品の番号、Owner が書く) を足した。`KH_WashTool._OnShot(item)` は、その枠が今も 1 発の品 (`pulseSeconds > 0`) を出していて、`spraying` (トグル・押し続け) が OFF で、`item` が枠の出している品と同じときだけ出す。水の枠を水鉄砲からホース・じょうろに替えた後に遅れて届いた通知で、ホース・じょうろが 0.4 s 出ることは無くなった。
   - 所有権が届く前の発射: 押した瞬間のサーバー時刻を記録し (`_Shot(item)` の中で `GetServerTimeInMilliseconds`)、発射の数を数える (最大 3)。所有権が届いたら `shotSerial += 数`、`shotServerMs` = 最後の発射の押した時刻、`shotItem` を書く。以前の「1 秒より古い発射は捨てる」はやめ、新しさは受け取る側が押した時刻で判断する (1.5 s、`ShotFreshMs`)。

2026-10-11 (統括): Codex 最終確認の残り 1 点 (選択解除・品の切替の後も 1 発の表示タイマーが残り、別モードで噴射を再開し得る) を直した: `_SetSelected(false)` でタイマーを消し、`Want()` は `pulseSeconds > 0` の間だけタイマーを見る。C# / U# コンパイル 0。
