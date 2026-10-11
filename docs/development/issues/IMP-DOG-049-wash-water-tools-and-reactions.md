---
id: IMP-DOG-049
type: IMP
system: DOG
status: review
priority: Medium
summary: ホースの連続放水・水鉄砲の単発射出・じょうろの傾け注ぎを整え、ホースと水鉄砲に犬の分かりやすい反応を組み合わせる
next: 実装・適用済み (段 A・段 B・じょうろの傾けて注ぐ)。ユーザーの PCVR で道具の持ち方・水鉄砲とホースの水・じょうろの傾け (持った瞬間に注がないか)・犬の反応を確認。2 人・Quest は未確認
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

段 A ではじょうろをトグルのまま残した (VR の手の傾きの判定・Desktop / スマホの自動の傾き・他の人への見せ方・ハーネスのじょうろのケースの作り直しが要るため)。**2026-10-11 に下の「じょうろの傾けて注ぐ (2026-10-11、未適用)」で実装した** (延期は解消。適用は未実施)。

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

## 段 B 実装 (2026-10-11、未適用)

犬の反応 (設計案 §3.3、Q10〜Q12 の推奨どおり)。**コードだけ。コンパイル・Codex 独立レビュー・Unity の操作は未実施** (統括が下の順で行う)。シーン・プレハブ・Animator Controller には書き込まない (値はコードの定数表、Jump の State は既存、顎は実行時に引く) ので、premium の後でも安全な設定メニューは要らない。

### 何をするか

| 道具 | 反応 | 作り |
|---|---|---|
| ホース (押している間、`pulseSeconds` = -1) | 水を追ってじゃれる 1.5 s | 顔を水の当たる方 (当たった点を洗う人の方へ 0.4 m 寄せた点 = 水の流れ) へ向ける (`_SetGaze`)、顎をパクパク (20°、3 Hz、LateUpdate で顎の骨へ加算)、前足を上げる (既存の足上げ = `_washPawUntil` → `WashPawPose` → CallPaw をそのまま使う) |
| 水鉄砲 (1 発、`pulseSeconds` > 0) | その場でぴょんと跳ねて振り向く | フリスビーの Jump の Bool + State (6 犬種の `AC_KD_Dog_<犬種>.controller` に既にある。Controller は変えない) をクリップの長さだけ立てる。Owner が体の位置を台の立ち位置 (`anchor`) から**真上にだけ**放物線で上げ下げする (クリップの空中の区間 = crouch 後の 2 × rise)。高さ = 立っている頭の高さ (`headAnchor` − 犬の根元) × 0.3、0.05〜0.2 m。振り向きは顔だけ (体の向きは standYaw のまま。2026-09-30 の「洗われ中は体を回さない」に合わせた読み。体も少し回す案は PCVR で判断) |
| じょうろ・無料シャワー | 付けない | `pulseSeconds` = 0 は印 0 |

- 反応の始めに短い声を 1 回 (犬の `barkSource` で `barkClip` を pitch 1.2・音量 0.5。ブルブルと同じ声の音源で、AudioSource は増やさない)。
- 跳ぶ動きはクリップの「着地」のキーで足が台に着く作りのまま。台を 0.9 m まで上げても立ち位置が台に乗って動くので、上下の動きだけが足される。

### 値の置き場 (1 か所)

`KH_DogWash.cs` の定数表 `WaterPlay*` (Q12 の仮値。Play で詰める)。Wash/12 が実際の値を 1 行に出す。

| 値 | 仮値 |
|---|---|
| 反応する部位 `WaterPlayParts` | 胸 1・首 2・前足 3/4・頭 7 (「頭・前足・胸のあたり」。首は頭と胸の間なので入れた) |
| ホース: 当たり続けてから 1 回目 `WaterPlayHoseFirst` | 0.5 s |
| ホース: 以後の間隔 `WaterPlayHoseSpacing` | 5 s (当たり続けている間) |
| ホース: 水が止まってから終わるまで / 当たりの切れ目 `WaterPlayHoseEnd` | 0.5 s |
| 水鉄砲: 同じ犬の間隔 `WaterPlayPistolSpacing` | 3 s (当たるたびに候補) |
| ホースの反応の長さ `WaterPlaySeconds` | 1.5 s |
| 跳ぶ長さ | Jump クリップの長さ (crouch + 2 rise + recover) を 1〜1.5 s に収める |
| 跳ぶ高さ `WaterPlayHopRatio` / `Min` / `Max` | 頭の高さ × 0.3、0.05〜0.2 m |
| 顎 `WaterPlayJawDegrees` / `Hz` | 20°、3 Hz |
| 顔を向ける点 `WaterPlayGazeOut` | 当たった点から洗う人の方へ 0.4 m |
| 声 `WaterPlayVoiceVolume` | 0.5 |

### 道具の区別 (洗いの効きの表は変えない、Q7)

道具 ID は増やさない。ストロークを受け取った側 (`KH_DogWash.ReceiveStroke`、全端末・自分の返り (echo) も) が、送り主が持っている水の道具を既存の `tools` と `_IsHeldBy` (遠隔ストロークの `HoldsTool` と同じ確かめ方) で探し、その道具の `pulseSeconds` (`KH_WashSelection` が出している品ごとに全端末で書く) の符号で 1 = ホース / 2 = 水鉄砲 / 0 = それ以外 とする。**通信の中身は増えない** (NetStroke の形もそのまま)。
- 候補 = 送り主が持っているシャワーの種類の道具すべて (無料シャワー・じょうろ = 印 0 も入れる。`WaterToolMode`)。**候補が 1 本**ならストロークはその道具からしか来ないので、その印。**2 本以上** (片手ずつ水の道具・持ち替えの直後で `_IsHeldBy` の猶予に前の道具が残る) なら**印 0 (反応しない)**。当初は「いま出ている道具 (`KH_WashTool._SprayActive`) がちょうど 1 本ならその印」としたが、Codex 再確認 (2026-10-11) で同期の遅れによる取り違え (別の場所へ撃った水鉄砲の直後に無料シャワーを当てると跳ねる) が残ると分かったので、2 本以上は一律に反応しないにした (`_SprayActive` は残すが判定には使わない)。使っていないホースを片手に持ったまま無料シャワーを当てても、ホースの反応にはならない。
- 自分のストロークは描いた時点 (`Stroke`、`_LastStrokePart` と同じ瞬間、部位が変わったらすぐ・同じ部位は 10 Hz まで) でも記録する。返り (echo) は往復の後なので、それを待つと下の足上げが先に部位を取っていた (Codex レビュー 4)。返りでもう一度記録するが、同じ値を更新するだけ。
- 記録 (`NoteWaterHit`): 全端末で「水が当たっている」(`_waterOnAt`、シャワーの種類のストロークなら道具・部位を問わない。ホースの終わりの判定) と、反応する部位へのホース・水鉄砲の当たり (ホースの連続の始まり・最後、水鉄砲の当たりの番号、部位、ワールド座標) を持つ。洗いの処理は一切変えない。

### 同期 (ブルブルと同じ形)

- `KH_DogWash` に同期変数 `waterPlay` (int 1 本) を足した: `(番号 << 6) | (部位 << 2) | 種類` (種類 1 = ホース、2 = 水鉄砲)。`_RequestWaterPlay(種類, 部位)` は `_RequestShake` と同じく **KH_DogWash の Owner (= 犬の Owner に追従)** だけが番号を 1 進めて送る。
- 決めるのは**犬の Owner** (`WaterPlayDecide`、台の上の処理の中、`_dogOwner && 台に着いている && arrived`)。ホースは反応する部位への連続の当たりが 0.5 s 以上で、前回から 5 s 以上。水鉄砲は新しい当たりの番号があれば、前回から 3 s 以上。どちらも**今の優先順の下**: 満足の合図・ブルブル (保留中も)・足上げ・反応中・足が動いている間は要求しない (そのときの当たりは捨てる。貯めない)。
- 間隔 (ホース 5 s・水鉄砲 3 s) は**受け取った反応からも全端末で数える** (Codex レビュー 1)。Owner が替わっても、新しい Owner がすぐに次を要求しない。
- 番号は送る側で 24 bit で一周するので、受け取る側は差を 24 bit で取り、(0, 2^23) を「新しい」とする (Codex レビュー 2)。
- 全端末 (Owner も次のフレーム) が番号の変化で反応を始める (`WaterPlayStand`)。**途中参加・台に新しく乗った犬は、台の上で最初に見た値 (状態を受け取った後) を基準にするだけで、再生しない**。台を離れて戻ったときも基準を取り直す。
- Owner の交代の最中 (KH_DogWash の Owner がまだ犬の Owner に追いついていない数秒) の要求は届かない (反応が 1 回抜ける。受け入れる)。

### 優先と止まる時 (Q11)

- 台の上の処理 (`UpdateWashStation` の「台の上」の枝) からだけ動く = **洗い場の台に留めて着いた犬だけ**。最後のフレームが台の上でなかったら (放す・リード・用事・川・Fetch / ディスク) 次の `WashErrandTick` で止める (Jump を下ろし、ホースの足上げを終える)。
- 満足の合図が始まると止める。ブルブルが始まると止める。反応中は足上げを始めない (Jump と CallReady が同時に立たない)。
- **ホース・水鉄砲の当たり (印 1 / 2) で前足へ移ったときは、普通の足上げを出さない** (`WashReact` の足上げの条件に「この部位の直近 0.35 s の当たりが水の道具のものでない」を足した。Codex レビュー 4)。足上げが先に `_washPawUntil` を立てると、Owner の水の判断が「足上げ中」で当たりを捨て、水鉄砲の前足への 1 発が足上げに化けていた。無料シャワー・じょうろ (印 0) の足上げは今までどおり。満足の合図・ブルブルとの優先は変えていない。濡れのブルブルは反応の終わりまで待つ (足上げと同じ扱い。水鉄砲の 1 発でストロークが終わってもブルブルは跳び終わってから)。
- 他の端末でまだ満足・ブルブル・足上げが見えていればその反応は捨てる (そのときだけ画面ごとに違いが出る)。

### Animator の経路

- 水鉄砲: `dogAnimator.SetBool("Jump", true)` (Any State → Jump、Bool が下りると Idle へ 0.2 s。フリスビー/2 が作ったもの)。跳んでいる間は毎フレーム立て直す (`KH_FrisbeeCatch` は待機中でも自分の状態の変わり目に Jump = false を書くため)。終わり・中断で false。フリスビーは `WashHeld` で台の上の犬には始まらないので、書き手が同時に 2 人になる場面は無い見込み。
- ホース: 既存の足上げの経路 (LateUpdate の `ApplyCompanionAnimation` → CallReady + CallPose 3)。顎は `KD_DogPetController.sourceBone` (頭の書き先 = 2 段構えなら Constraint のソース側、1 段ならその骨) の子 `Bone.006` を `Find` で 1 回だけ引き、LateUpdate (Animator の後・Constraint の前、vrc-character-rig §6) で犬の右軸まわりに加算する (+ = 顎が下 = 口が開く。フリスビーのクリップの jaw と同じ向き)。Animator が書かなかったフレームは前の基準から書く (撫での頭と同じ蓄積防止)。終わった後の 1 フレームで基準へ戻す。
- 体を動かすのは Owner だけ (`VRCObjectSync` が配る)。演出 (顔・顎・Jump の Bool・声) は全端末がローカルで出す。

### 負荷 (IMP-DOG-034 / 036)

- 台に乗っていない犬: `WashErrandTick` の int の比較 1 つと、LateUpdate の bool の確かめ 1 つだけ。
- 台の上の犬 (最大 4): 毎フレーム `_WaterPlay()` を 1 回。Owner は直近 1 s に当たりがある間だけ当たりの値を読む。
- `KH_DogWash`: シャワーの種類のストロークを受け取るたび (送り主ごとに最大 8 Hz) に float を 1 つ書く。反応する部位のときだけ道具を 1 周 (水の枠の道具だけ `_IsHeldBy` まで進む)。

### 変更したファイル

- U#: `Scripts/KH_DogWash.cs` (同期変数 `waterPlay`、定数表、当たりの記録 `NoteWaterHit` / `WaterToolMode`、読み出し、`_RequestWaterPlay`、ハーネス用 `_DebugWaterHit`)、`Imported/KominkaDogRun/Scripts/KD_DogWanderController.HillsideWash.cs` (反応の本体。既存の反応へは止める・待つの 4 行だけ)、`Imported/KominkaDogRun/Scripts/KD_DogWanderController.cs` (LateUpdate に顎の 1 行)、`Scripts/KH_WashTool.cs` (読むだけの `_SprayActive`、Codex レビュー 3。コンパイルで `KH_WashTool.asset` も作り直される)。新しい U# ファイル・Program Asset は無い。
- Editor: **新規** `Scripts/Editor/KH_DogWashSetupEditor.WaterPlay.cs` (Wash/12 の行)、`KH_DogWashSetupEditor.Verify.cs` (呼び出し 1 行)。
- Unity が作るもの: `Scripts/Editor/KH_DogWashSetupEditor.WaterPlay.cs.meta`。コンパイルで `KH_DogWash.asset` と `KD_DogWanderController.asset` が作り直される。**`KD_DogWanderController.asset` は別セッション (Treat 系) の未コミットの変更と同じ Program Asset** なので、stage は別セッションと分け方を決めてから (vrc-scene-edit-safety)。

### Unity で行う手順 (統括、この順で)

0. Codex 独立レビュー (段 B の同期の変更。完了の定義どおり)。
1. `bash scripts/unity-compile.sh --udon` — C# / U# のエラー 0 (`KH_DogWash` の同期変数が 1 つ増えたので Udon も)。
2. `Tools/KominkaHillside/Wash/12 - Verify Wash (All Dogs)` — FAIL 0 (段 A の基準 5361 行 → 5381 行の見込み: 表 1 + 同期変数 1 + 6 犬 × 3)。新しい行:
   - `PASS water play table (KH_DogWash.WaterPlay*, Q12 trial values): parts Chest,Neck,FrontL,FrontR,Head / hose first 0.5 s, every 5 s, ends 0.5 s after the water / pistol every 3 s / play 1.5 s, hop 1..1.5 s / hop height 0.3 x head, 0.05..0.2 m / jaw 20 deg at 3 Hz / gaze out 0.4 m / voice 0.5`
   - `PASS KH_DogWash.waterPlay is a synced int (serial << 6 | part << 2 | kind)`
   - 6 犬それぞれ: `PASS water play <番号> (<名前>): Jump Bool + state (KH_Jump_<犬種>) + Any State in / Idle out in Assets/KominkaHillside/Imported/KominkaDogRun/Animations/AC_KD_Dog_<犬種>.controller`
   - `PASS water play <番号> (<名前>): hop 0.0xx m (head 0.xxx m x 0.3) in the air 0.xx s after the crouch 0.xx s; play 1.xx s; Jump clip 1.xx s = crouch + 2 rise + recover 1.xx s (frisbeeCatch ...)` (Lab は crouch 0.25・air 0.70・play 1.35 s の見込み)
   - `PASS water play <番号> (<名前>): jaw = .../Bone.004/Bone.006 (head write bone .../Bone.004 / child Bone.006), editor write bone .../Bone.006, PhysBone roots on it 0`
   - **顎の行が FAIL のとき** (実行時の `sourceBone.Find("Bone.006")` が編集側の書き先と違う・見つからない): コードを緩めず、出た 2 つのパスを記録して戻す (犬ごとの顎の参照を Setup で配線する形へ変える)。Jump の行が FAIL なら Frisbee/2b で Controller を確かめる (この段では Controller を変えない)。
   - hop の行の高さは**保存されたシーンの姿勢** (地面の上) の頭の高さから出した目安。実行時は反応の始めに台の上で立った姿勢から取り直すので、この数字で調整しない。
3. シーンは変わらないので保存は不要 (Wash/12 は読むだけ)。44 / 21 は段 B で変わる所が無い (選択・道具・台を触っていない)。
4. この Issue の frontmatter (`next`) を変えたので `node scripts/check-issues.js --write` (統括)。

### ハーネス (回さない。回すかは相談で決める)

- 当てはまる既存の群: Course/100 の洗い (`KH_CourseWalkHarnessEditor.WashReact.cs` の W-paw / W-shake の隣) と、台に呼ぶ手順 (`WashCall.cs`)。既存のケースの道具は `_LocalContact` を直接呼び、道具を持つ人がいない (`_IsHeldBy` は Pickup の持ち主を見る。`debugHeldOverride` は効かない) ので印 0 になり、この段の変更では反応も足上げの抑えも出ず、**既存のケースの期待値は変わらない見込み**。
- **`_DebugWaterHit` で見られるのは反応の判断・同期・再生だけ。** 当たりの記録を直接書くので、`_LastStrokePart`・洗いの保持 (`_WashHold`)・`_washActive` (洗いのフレーム) を通らない。足上げとの取り合い (Codex レビュー 4) と、道具の区別 (`WaterToolMode` / `_SprayActive`) は試せない。それを見るケースは**本物のストロークの経路**が要る: 犬を台に呼んで着かせ (洗いのフレームが回る = `_washActive`)、水の枠に水鉄砲を出し、その `KH_WashTool` を実際に「持った」状態 (ClientSim の Pickup で持つ、またはハーネス用に `_IsHeldBy` が答える持ち主を作る口を足す) で、ノズルを前足へ向けて 1 発撃つ → `_wpHopCount` +1、`_washPawCount` は増えない。同じ形で無料シャワーを前足へ → 足上げ +1・反応 0。片手にホース (出さない) + 片手に無料シャワー (出す) → 反応 0。これらは道具を持たせる手順を作ってから足す。
- 判断・再生のケース (`_DebugWaterHit` で足りる。ClientSim は 1 人 = Owner の経路だけ): 犬を台に呼んで着かせ、`KH_DogWash._DebugWaterHit(1, 7)` を 8 Hz で 0.6 s → `_wpHoseCount` +1・`WashPawPose`・`_wpJaw` の回転、止めて 0.5 s で `_wpKind` 0、当て続けて 5 s 以内に 2 回目が出ない / 5 s 後に出る。`_DebugWaterHit(2, 3)` を 3 回 / 1 s → `_wpHopCount` +1 だけ・Animator の Jump・根元の y が anchor より上がり横は動かない・3 s 後の次の当たりでもう 1 回。対照: お尻 (部位 0) だけ → 0、台に着いていない犬 → 0、満足の合図の最中 → 0。非 Owner・途中参加 (再生しない) は 1 人では見られない (W-react-remote と同じく SKIP、2 人の実機で見る)。

### 残り・リスク

- **顎の骨**: 2 段構えの犬でソース側の顎が頭の書き先の直下に `Bone.006` という名前であるかは未確認 (Wash/12 が確かめる)。顎が開く向き (+ = 下) は **Wash/12 が犬ごとに静的に確かめる** (2026-10-11 追加、下の「じょうろの傾けて注ぐ」の「顎の向きの検査」。実行時の書き込み +20° を顎へ当て、顎の皮膚が下へ・鼻から離れる向きに動くかを測って元に戻す)。見た目 (口の開き方の自然さ) は Play で見る。Animator が顎を書かない State があっても蓄積しない作りにしたが、Play で「反応の後に口が開いたまま」でないかを見る。
- **跳ぶ高さの見え方**: 他の人には `VRCObjectSync` の補間を通るので、跳びが小さく・遅れて見えるかもしれない (2 人で確かめる)。クリップの空中の脚の畳みと上下の量の組み合わせは Play で見る。
- **声**: ホースを当て続けると 5 s ごとに短い声。うるさければ `WaterPlayVoiceVolume` を 0 にする。
- **足上げ**: 既存の足上げは静止した姿勢 (掻く動きではない)。「前足で水を掻く」に足りなければ、足上げを短く出し入れする案を Play の後に検討。
- **振り向き**: 体は回さず顔だけ。小さく分かりにくければ、跳んでいる間だけ体を少し (±15° 程度) 回して standYaw へ戻す案 (Owner の回転、同期は増えない)。
- 当たりの判定は道具の Raycast (直線) で、水鉄砲の粒は弧を描く。遠くでは見た目の当たりとずれる (無料シャワーと同じ性質)。
- 台の上の犬は毎フレーム判断する前提 (`WaterPlayStand` の基準取り直しと `WashErrandTick` の停止がフレームの連続を見る)。確かめた: リードなしは `TickBusy` が `_activityBusy` (台の枝が立てる) で、リード付きは `LeashLegsOk` が `_stationIdx >= 0` で、どちらも足だけのフレームにならない (IMP-DOG-036 の間引きを受けない)。
- 区別の印は送り主が持っている道具から受け取った側が引く。水の道具 (無料シャワー・じょうろを含む) を 1 本だけ持っているときはその道具で決める。**2 本以上持っているとき (片手ずつ・持ち替えの直後の猶予 1.5 s を含む) は反応しない** (2026-10-11 Codex 再確認: 同期の遅れで、別の場所へ撃った水鉄砲の直後に無料シャワーを当てると水鉄砲と取り違え得るため。両手持ちはまれなので、取り違えより「反応しない」を選んだ)。この場合も、違う種類の反応は出ない。

### ユーザーの確認 (PCVR、未確認)

- [ ] ホース: 台に留めた犬の頭・胸・前足へ 0.5 s 以上当てると、前足を上げ、口をパクパクさせ、顔を水の方へ向ける。手を離すと 0.5 s ほどで終わる。当て続けると 5 s に 1 回まで。お尻・しっぽ・後ろ足だけでは出ない。
- [ ] 水鉄砲: 頭・胸・前足に当てると、その場で小さく跳ねて当たった方へ顔を向ける。横へずれない・台からはみ出さない (台を一番高くしても)。3 s 以内の 2 発目では跳ばない。
- [ ] じょうろ・無料シャワーでは出ない。台に留めていない犬 (歩いている・リードで台の外) では出ない。
- [ ] 満足のお座り・ブルブル・足上げの最中には出ない。ホースを止めた後のブルブルは今までどおり出る。
- [ ] 口が下へ開く (頭の中へ閉じない)。反応の後に口が開いたまま・前足が上がったままにならない。
- [ ] 6 犬種で跳ぶ高さが体に合っている (ポメ・コーギーは小さく、ラブ・サモエドは大きめ)。
- [ ] 声の大きさ・回数がうるさくない。
- [ ] Desktop で同じ操作をしても同じ反応。
- [ ] 2 人: もう 1 人にも同じ反応が同じ時に見える (跳ぶ高さが極端に小さくない)。後から入った人に過去の反応が再生されない。

### 段 B の適用と、この作業のまとめ (2026-10-11、統括)

段 B: C# / U# コンパイル 0 → Codex 独立レビュー 2 回 (受信側の待ち時間、serial の周回、道具の特定、前足の命中の調停、文言。最後の 1 点「同期の遅れで 2 本持ちのとき取り違え得る」は、2 本以上持っているときは反応しない、で統括が直した) → Wash/12 **FAIL 0 (5,381 項目、段 A の 5,361 から 20 行増)**: 6 犬種とも Jump の配線・跳ぶ高さ (頭の高さ × 0.3)・顎の骨を確認 → 44 は 962 PASS / FAIL 0 のまま。シーンの保存は不要 (書く値が無い)。`KD_DogWanderController.asset` などのプログラムアセットは、別セッションの未コミットのおやつの変更と同じファイルなので stage していない (Unity が開くときにソースから作り直す)。

この作業の完了の定義 (上の「取り込みと実装の進め方」) は満たした: 3 品の取り込みと枠への追加 (`0677138`・`2bf922e`)、Codex の指摘の反映 (段 A 3 回・段 B 2 回)、44 / 21 / Wash/12 は基準より悪化なし、写真とリスティング画像の目視、コミット。**この Issue は done ではない**: じょうろの傾けて注ぐ (延期と記録済み)、下のユーザー確認、2 人・端末の確認が残る。

#### ユーザーの確認 (段 A の道具の動き、PCVR、未確認)

- [ ] 水鉄砲・ホースを右手・左手で持ち、ノズルが肘→手の向きか、握りの位置、ホースのたるみが腕や体に刺さって見えないか
- [ ] 水鉄砲: 1 回押すごとに水の玉が弧を描いて 1 発、0.4 秒ごと。ホース: 押している間だけ太い水流、離すと止まる
- [ ] 水の枠でじょうろ・水鉄砲・ホースを切り替え、持ったまま台 (檜・プール) を替えられる
- [ ] プール: 水面が薄く透けて足先が見えるか、泡・しぶきとの前後 (描き順) が崩れないか。主洗い場ではプールが斜面に埋まる (IMP-DOG-044 の判断待ち)
- [ ] 2 人: 相手の水鉄砲の 1 発が見える (連射はまとまって少なく見えることがある、許容)、同じ選択が見える。Quest / Android の負荷
- [ ] じょうろ (VR): 持っただけ・トリガーを押しただけでは出ない。立てて持つと出ず、注ぎ口の側へ 30° ほど傾けると細く出始め、60° まで傾けるほど太くなる。戻すと 24° あたりで止まる。境目でちらつかない
- [ ] じょうろ (VR): 持ったときの手の向きで、何もしなくても傾いて出てしまわないか (Grip の持ち方は BUG-DOG-010 の続きで未確認。出てしまうなら傾きの基準か持つ点を直す)。右手・左手
- [ ] じょうろ (Desktop / スマホ): 押している間、じょうろが持ち手を軸に 0.3 秒ほどで傾いて注ぎ、離すと戻って止まる。持ち手が手から外れて見えない
- [ ] じょうろ: 置く・リセットで戻すと注ぎが止まり、まっすぐに戻る。じょうろの水では犬がじゃれない・跳ねない (今までどおり)
- [ ] 2 人: 相手のじょうろの水が見える。相手が VR なら傾きも見える。相手が Desktop なら、じょうろはまっすぐのまま水だけが出る (受け入れた制限)

(犬の反応の確認項目は上の段 B の節)

## じょうろの傾けて注ぐ (2026-10-11、未適用)

設計案 §3.2 の「注ぎ口が下を向くほど注ぐ (傾けた角度で量が変わる)」「Desktop / スマホ = 押している間、じょうろが自動で傾いて注ぐ」「30° で出始め、60° で最大」を実装した。C# / U# コンパイル 0、Codex 独立レビュー済み (2026-10-11、統括: 90° を超える前傾で角度が折り返す件を `Atan2` に直し、落とす・戻す・持ち直し・角度での停止でも粒子量を戻すよう `RestModel` で `FlowReset` を呼ぶ)。シーンへの適用と検査の結果は下の「適用」。ハーネスは未実施。他の道具 (無料シャワー・ホース・水鉄砲・ボトル・スポンジ・タオル) の入力は変えていない。

### 何をするか

- 入力の種類を 1 つ足した: `KH_WashTool.PulseTilt` (= -2)。品ごとの `pulseDurations` で**じょうろ (品 6) だけ**がこれになる (0 = トグル、> 0 = 1 発、-1 = 押している間 (ホース) は変わらない)。
- **傾き** = じょうろが立った姿勢から注ぎ口の側へ倒れた角度 (度)。じょうろの注ぎ口 (ハス口) は立てた状態で既に水平から 31.5° 下を向いている (44 の `anchor WateringCan ... forward=(0.000, -0.523, 0.853)`) ので、注ぎ口そのものの向きでは測らない (測ると持っただけで出る)。注ぎ口の水平の向きが道具の座標でどれだけ下がったかで測る (横に倒しても増えない)。
- **VR (持っている本人)**: 手 (Pickup) の傾きから毎フレーム判定する。**押しても何も起きない** (トグルしない)。傾きは位置同期 (`VRCObjectSync`) でそのまま他の人にも見える。
- **Desktop / スマホ (持っている本人)**: Use を押している間、じょうろの模型 (枠の子 = 品のモデル。注ぎ口の `NozzleAnchor` と粒子 `UseMist` も一緒に動く) だけを持ち手 (`GripAnchor`) を軸に 0.3 s で 60° まで傾け (なめらかに出入り)、離すと戻す。Pickup の根元は動かさない。**他の人には傾きは見えない** (水だけ見える。受け入れた制限。持つ人が VR か を見て他の人も傾ける案は、必要なら後で)。
- **注ぐ / 止まる**: 30° 以上で注ぎ始め、24° 未満で止まる (ずれ幅 6° で、境目で手が揺れても同期の bit がちらつかない)。どちらの端末も同じ判定 (Desktop は表示中の角度)。
- **量**: 水の粒子の出る数 (`emission.rateOverTimeMultiplier`) を 30° で品の値の 0.35 倍、60° で 1 倍 (間は直線)。持っている本人は自分の傾きから、他の人は見えている傾きから (まっすぐに見える = Desktop の人なので Desktop の 60° の量)。止めたり品を替えたりするときに元の値へ戻す。**洗いの効きは注いでいる間ずっと無料シャワーと同じ** (Q7。ストロークの秒数を傾きで減らすと細い注ぎが無料シャワーより弱くなるので、量は見た目だけ)。
- **同期**: 注いでいる / いないだけを、今までの `KH_WashToolState._SetSpraying` (Owner が書く bool 1 本) で**変わった時だけ**送る。毎フレームの同期は増えない。新しい同期変数・ネットワークイベントは無い。
- **止まる時**: 置く (`OnDrop`)・リセットで戻す (`ReturnHome`)・品の切替や選択の解除 (`_SetSelected`、`KH_WashSelection.ConfigureSlot` が品を替える前に呼ぶ)・持ち直し (`OnPickup`) で、注ぎを止め、模型をまっすぐに (ゆっくり戻さず即座に) 戻し、粒子の量を元に戻す。手を離さずに Use を離したときだけゆっくり戻す。
- **犬の反応**: じょうろは今までどおり反応しない。`KH_DogWash.WaterToolMode` が `pulseSeconds < 0` をホースと見なしていたので (-2 もホースになる)、`== KH_WashTool.PulseHold` に直した。
- 散水の当たり判定のマスクなど (`SprayMask`、粒子の衝突・Trigger) は変えていない。

### 値 (仮。設計案の値 + こちらで決めた止まる角度。`KH_WashTool` の定数、44 が実際の値を出す)

| 値 | 仮値 |
|---|---|
| 注ぎ始め `TiltStartDegrees` | 30° (設計案) |
| 止まる `TiltStopDegrees` | 24° (ずれ幅。AI が決めた) |
| 量が最大 `TiltMaxDegrees` | 60° (設計案) |
| 30° での量 `TiltMinFlow` | 品の粒子の数の 0.35 倍 (AI が決めた) |
| Desktop の傾き `DesktopTiltDegrees` / `DesktopTiltSeconds` | 60° / 0.3 s |

### 変更したファイル

- U#: `Scripts/KH_WashTool.cs` (入力 `PulseTilt`、`TiltFrame` / `RemoteFlow` / `MeasuredTilt` / `ShowModelTilt` / `RestModel` / `SetFlow` / `FlowReset`、止まる経路 4 か所)、`Scripts/KH_DogWash.cs` (`WaterToolMode` の 1 行と注釈)、`Scripts/KH_WashSelection.cs` (注釈だけ)。新しい U# ファイル・Program Asset・同期変数は無い。
- Editor: `KH_WashSelectionEditor.cs` (`WateringCanId = 6`)、`.Category.cs` (品ごとの入力の表 `PulseSeconds(id)` を新設し、**48 / 49 (`ConfigureStation`) が毎回書く** (以前はシーンの値を残していた)。44 の `saved backing pulseDurations` をじょうろ = `PulseTilt` に変え (理由: トグルをやめたため。他の品の期待は変えず、全品 = 表 を足した)、論理の水の切替の後に `VerifyCanTilt` を呼ぶ)、`.Water.cs` (49 の水鉄砲・ホースも表から。44 の `VerifyCanTilt`)、`.Build.cs` (42 も表から)、`.Verify.cs` (NOT_TESTED の文言)、`KH_DogWashSetupEditor.WaterPlay.cs` (顎の向きの検査)。
- ハーネス: `Testing/KH_CourseWalkHarnessEditor.WashPremium.cs` のケース `P-selection-water-toggle` を **`P-selection-water-tilt`** に作り直した (群 `premium-input`。トグルを前提にした期待を傾けて注ぐ期待へ: 押しただけでは出ない → 0.5 s 後に傾いて (30° 以上) 注ぎ `spraying` → 離して 0.5 s 後に止まってまっすぐ → もう一度押して注ぐ → 置くと即座に止まってまっすぐ)。ClientSim は Desktop なので VR の手の傾きは通らない (44 が Edit mode で確かめる)。**回していない** (回すかは相談)。

### 44 の新しい行 (最初の洗い場、論理の水の切替がじょうろで終わった所)

- `PASS WateringCan on slot2 <洗い場>: tilt to pour (pulse -2 = KH_WashTool.PulseTilt), Grip, its own nozzle`
- `PASS WateringCan Use press does not toggle ... (after Use down: spray False, Use held True ...; after Use up: held False)` (Edit mode は VR でないので Desktop の経路。Edit mode で `Networking.LocalPlayer` が例外になる場合はこの行の代わりに log へ NOT_TESTED を 1 行出し、押す操作はハーネスの P-selection-water-tilt が見る)
- `PASS WateringCan tilt thresholds (...): pours from 30 deg, stops below 24 deg, full flow at 60 deg; flow 0.35 / 0.6x / 1.00 x the item's rate at 30 / 45 / 60 deg; Desktop / Touch leans the model to 60 deg in 0.3 s while Use is held`
- `PASS WateringCan upright measures 0.00 deg (its rose points -31.5 deg from the horizon at rest, ...)`
- `PASS WateringCan VR measure = the pickup's lean toward the spout ... (lean deg -> measured: 24 -> 24.00; 30 -> 30.00; 45 -> 45.00; 60 -> 60.00; 45 deg sideways -> 0.00)` (Pickup の根元を世界の軸まわりに回して実行時の `MeasuredTilt` を呼び、戻す)
- `PASS WateringCan Desktop lean 60 deg about the grip ...: spout turned 60.00 deg, pitch -31.5 -> -88.5 deg (前後), rose x.x cm lower, grip moved 0.00 mm` と `PASS WateringCan model upright again exactly after RestModel ...`
- 各洗い場: `PASS saved backing pulseDurations: can tilt -2 (was toggle 0), pistol shot 0.40 s, hose hold -1, every offered item = PulseSeconds <洗い場> (actual can -2)`
- 論理の `water swap -> WateringCan: slot2 Grip, pulse -2, range 1.5 m ...` (段 A の期待 `pulse 0` から変わる)。`can tilt <洗い場> spoutInSlot=... gripInSlot=...` を log に 1 行。
- 実行時の本体 (`MeasuredTilt` / `ShowModelTilt` / `RestModel` / `FlowFraction` / `OnPickupUseDown` / `OnPickupUseUp`) をプロキシで呼ぶ。回した Transform は finally で戻す。

### 顎の向きの検査 (Wash/12、段 B の残りの確認)

- `KH_DogWashSetupEditor.WaterPlay.cs` の顎の行の後に、犬ごとに 1 行足した (Wash/12 は 6 行増えて **5,381 → 5,387 行**の見込み)。
- 実行時 (`WaterPlayJawLate`) と同じ書き方で、実行時が書く顎の骨 (`petController.sourceBone.Find("Bone.006")`) を `+WaterPlayJawDegrees` (20°) 回す (`localRotation = AngleAxis(20, Inverse(parent.rotation) * dogRoot.right) * pose`)。2 段構えの犬では Constraint はこのメソッドの中では動かないので、皮膚側の顎 (`KD_DogRig.chin`) にも同じ世界の回転 `AngleAxis(20, dogRoot.right)` を当て、2 つの世界の差が 0.01° 未満で一致することを確かめる (同じ書き込みの再現の証拠)。
- 測る物: 顎の骨に 0.5 以上の重みがある頂点の重心 (`BakeMesh(mesh, true)` × `localToWorldMatrix`)。**`-dogRoot.up` の向きに 1 mm より多く下がり、鼻 (`KD_DogRig.nose` = Bone.007 の頂点の重心。顎では動かない) から離れること**。頂点が無ければ顎の子孫のいちばん遠い点で測る (行にそう出る)。
- 期待の行: `PASS water play <番号> (<名前>): jaw opens down at +20 deg (the runtime's write): <n> chin vertices move x.x mm down (-dogRoot.up), x.x mm forward; gap to the nose skin (<m> vertices) a -> b mm; written .../Bone.004/Bone.006 + skin .../Bone.006 (world deltas differ 0.0000 deg); restored exactly True, scene dirty False -> False`
- 骨は finally で元に戻し、成分ごとに完全一致を確かめる。シーンの dirty が変わったら FAIL (記録も保存もしない)。
- **FAIL のとき**: 「下がらない / 鼻へ寄る」なら顎の向きが逆。検査を緩めず、`WaterPlayJawLate` の符号 (または軸) を直す。測る物が無い・例外なら出た理由を記録して戻す。

### Unity で行う手順 (統括、この順で)

0. Codex 独立レビュー (この差分。入力の変更と、ハーネスの期待の作り直し)。
1. `bash scripts/unity-compile.sh --udon` — C# / U# のエラー 0 (`KH_WashTool`・`KH_DogWash`・`KH_WashSelection` の U# が変わった。同期変数は増えていない)。
2. (21 を回すなら先に `WashPremium/21b - Capture Integration Baseline`。シーンが保存済みの状態で)
3. `WashPremium/49 - Place 0.4.0 Items` (または 48。どちらも `ConfigureStation` が品ごとの入力を表から書き直す。**シーンが dirty だと止まる**) — `Temp/WashSelection/place-040.txt` に 4 洗い場とも `  pulse <洗い場> WateringCan 0 -> -2`、`created models=0`。保存はしない。
4. `WashPremium/44 - Verify Selection` — `PASS` (上の新しい行。行数は増える。FAIL 0)。
5. `Tools/KominkaHillside/Wash/12 - Verify Wash (All Dogs)` — FAIL 0、顎の向き 6 行 (読むだけ。シーンを変えない)。
6. シーンを保存 (stage は vrc-scene-edit-safety の手順で分ける)。保存後にもう一度 44 (保存された backing の `pulseDurations`)。差分の見込み: 4 洗い場の `KH_WashSelection.pulseDurations[6]` 0 → -2 (プロキシと backing)、水の枠の `KH_WashTool.pulseSeconds` は 44 が最後に設定した品の値。
7. 21 を回すなら: 基準との違いは「水の枠の `KH_WashTool.pulseSeconds` が 0 → -2 (44 の論理がじょうろで終わる)」だけの見込み。それ以外が出たら止める (コードは緩めない)。
8. この Issue の frontmatter (`next`) を変えたので `node scripts/check-issues.js --write` (統括)。
9. ハーネス: 回すかは相談 (vrc-review 相談モード)。回すなら群 `premium-input` の `P-selection-water-tilt` だけ。

### 残り・リスク

- **VR の持ち方**: じょうろは Grip で持つ (FBX の `GripAnchor`)。公式の Grip は VR で前へ 30° 傾けて持たせる (BUG-DOG-010 の調べ) ので、持った手の向きしだいで、何もしなくても 30° 近く傾いて出てしまうかもしれない。PCVR で確かめる。出てしまうなら、持つ点 (`GripAnchor` の向き) を直すか、傾きの基準を変える (値は定数 1 か所)。
- **Desktop の持ち方**: Desktop の Grip の向きで、じょうろが横向きに持たれていると、模型の傾き (じょうろ自身の注ぎ口の側へ) が画面では変な向きに見える。持ち方は今までと同じで、この変更では直していない。
- 他の人には Desktop の傾きが見えない (水だけ。受け入れた制限)。VR の人の傾きは位置同期の補間ぶん遅れて見える。
- 粒子の量は他の人の画面では見えている傾きから出すので、本人と少し違うことがある (見た目だけ)。
- 洗いの当たりはノズルからの直線 (無料シャワーと同じ)。注ぐ水は弧を描いて落ちるので、遠くでは見た目の当たりとずれる (じょうろの届く距離は 1.5 m のまま)。
- 2 人・Quest / Android は未確認。

### じょうろの傾けて注ぐ・顎の検査の適用 (2026-10-11、統括)

C# / U# コンパイル 0 → Codex 独立レビュー (90° を超える前傾での折り返し → `Atan2`、停止時の粒子量の戻し → `RestModel` で `FlowReset`、文言。統括が修正し 44 に 120° の確認を追加) → 21b → `WashPremium/49` (4 洗い場で `pulse WateringCan 0 -> -2`、作成 0) → 44 **969 PASS / FAIL 0** (傾きの測り方 24/30/45/60/120° → 24.00/30.00/45.00/60.00/120.00、横 45° → 0.00、Desktop の 60° の傾きで握りの移動 0.00 mm、押してもトグルしない) → 保存 → 21: 1 回目は「保全」で FAIL、差分 (`runtime-diff.txt`、21 に失敗時の差分の書き出しを追加) は展望のベンチの水の枠の `pulseSeconds 0 -> -2` の 1 行だけで、意図した変更と確認してから 21b で基準を取り直し **21 PASS** → Wash/12 **FAIL 0 (5,387)**: 6 犬種とも顎は +20° で下に開く (あご下の頂点が 4.6〜25.6 mm 下がり、鼻との間が 8.7→12.6 〜 49.1→74.5 mm に広がる) → 保存後の 44 も 969 PASS。シーンの差分は 4 洗い場の `pulseDurations[6]` と道具 1 つの `pulseSeconds` (と実行時用データ) だけ。

これで段 A・段 B・じょうろの傾けて注ぐまで、この Issue の実装はすべて入った。残りはユーザーの確認 (上の「ユーザーの確認」、じょうろは VR の Grip で持った瞬間に注がないか) と、2 人・Quest / Android。

### ClientSim ハーネス (2026-10-11、統括)

回す前に Codex に相談 (vrc-review 相談モード): 「回す。ただし 100p1 の `P-selection-water-tilt` と 100p3 の `P-free-shower-regression` の 2 ケースだけ。100p2 は 44 で判断と適用を確認済みで初回の最小範囲から外す、100f の `W-height-stand` は無料台へ戻すので minLift > 0 は確かめられない」。

- `Course/100p1` (premium-input): run `20261011-065712` **WASH_PASS** (1 ケース、sceneUnchanged true)。3 枠の UI → Owner の応答、じょうろの傾けて注ぐ (Desktop の傾け。VR の手の傾きは ClientSim では届かない)、泡の入れ替え、3 本同時。
- `Course/100p3` (premium-free): run `20261011-065847` **WASH_PASS** (1 ケース、sceneUnchanged true)。無料シャワーのトグル・Drop で止まる・リセット。
- 起動の 1 回目はどちらも `ExecuteMenuItem` の空応答で NOT_EXECUTED (vrc-synaptic-unity の既知の間欠)。Unity の状態を確かめて再試行した。

ハーネスで未確認のまま残すもの: 水鉄砲・ホースの入力 (1 発の共有を含む)、犬の水の反応 (ケース未作成。実際のストロークの経路を通すケースが要る)、台の最低の高さへの移動と犬の降台、100p2 の残りのケース。2 人・VR の手の傾きは ClientSim では確かめられない。
