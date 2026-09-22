# アバターペット連携の静的調査 — 犬っこギミック / ムチォ

調査日: 2026-09-21。依頼・残課題: [VER-DOG-001](../development/issues/VER-DOG-001-avatar-pet-contact-research.md)。

## 1. 結論

**来訪者のアバターを改変せず、購入時の構成のまま検知できることが条件。ムチォは既存Senderで対応候補、犬っこは通常時の継続的な検知の対象外とする。** 犬っこは食器表示中に限り既存Senderを利用できる構造だが、ペットの存在を安定して検知する用途には使えない。専用Senderの追加は来訪者に改変を求めるため、今回の対応案には含めない。いずれも保存済み構造からの判定であり、VRChatで取得成功・他人との位置一致を実測した意味ではない。

| 対象 | 判定 | 追跡対象・連携設定 | 残る条件 |
|---|---|---|---|
| ムチォ（Muchio） | **既存構成で対応候補（実機未確認）** | `<アバター>/muchio/PetWorld/PetMount/PetGroundRoot/PetMotionRoot/KissContacts` のSender。Root未指定＝同Transform、タグ `OnigiriPetKiss` | モデルの歩行上下動とは少しずれる。2人の位置差・設置・再表示を実機確認 |
| 犬っこギミック | **無改変での通常時検知は対象外** | 食器表示中の `Pet_Fed` / `Pet_Watered` は `<アバター>/dog_MA/1/2/inu_light_v3` をRootに持つ | 食器を出していない通常時は利用できない。`Pet_Run` はLocal Onlyで、他人のペットの最終位置・向きにも適さない |

ムチォでも、**全員の画面で同一座標に置かれたペットへ、共通の犬が正確に鼻先を合わせる**ところまで既存構成で保証はできない。各クライアントでConstraint・接地・PBを評価するため、まず観測差を測る。

「向き」は移動基準の回転と、芸・姿勢を含む胴体の回転を区別する。RootのQuaternionが取れることだけでは、見た目の鼻先方向と一致した証拠にならない。

## 2. 調査方法・対象版

- 外部プロジェクト: `C:/work/unity/vcc/PetSystemResearch`。対象フォルダのPrefab、参照先FBXのGUID、MAコンポーネント、AnimatorController、AnimationClip、メニューを読み取り調査。
- 犬っこ: `Assets/TORINOSUKE/いぬっこぎみっく/dog_MA.prefab`、`dog_MA kubiwa.prefab`、`kubiwaB.prefab`。参照される `Assets/TORINOSUKE/ギミック/追従GIMMICK2 out/` も確認。
- ムチォ: `Assets/Muchio/` 直下の10派生Prefabと `gimic/MuchioBase.prefab`、`gimic/muchio.prefab`、`gimic/MuchioWorldOrigin.prefab`。直下10体は共通基底を使用し、Contact/階層の差分はない。`pukao` には歩行スタイルの既定値差分がある。
- アバター側SDK: Avatars **3.10.4**。ワールド側SDK: Worlds **3.10.5**（各 `Packages/.../package.json`）。ワールドの `UdonSharpBehaviour.cs:476` に `OnContactEnter(ContactEnterInfo)` が存在。
- `scripts/unity-api.sh` で読んだ `Application.dataPath` は **古民家ヒルズのAssets**。PetSystemResearchのEditor内での展開・MAビルド結果・アニメーション評価・Playは実行していない。保存されていないシーン変更は対象外。
- アバター素材、ワールドのコード、シーン、Prefabは変更していない。OSCアプリの実行・解析も行っていない。

主要PrefabのSHA-256（版番号をファイルから確定できないため現物識別に使用）:

| ファイル | SHA-256 |
|---|---|
| `dog_MA.prefab` | `d250476716f940261ca0c1c6e2ae701e91d7331c50686043c66584c2f3239246` |
| `MuchioBase.prefab` | `87e3a908b52300fe13a293a7bdf1eb41c70a3c539c96e9997922962652457db1` |

以下の行番号はこの保存版のもの。PrefabのfileIDとGUIDを照合し、名前だけでRootを推定していない。

## 3. ワールドから読める情報

[公式Contacts仕様](https://creators.vrchat.com/common-components/contacts/#udon-access-in-worlds)を2026-09-21に確認。

- Receiverと同じGameObjectのUdonに `OnContactEnter` / `OnContactExit` が届く。
- Enterの `contactInfo.contactSender` は `ContactSenderProxy`。有効性を `isValid` で確かめてから、`position`・`rotation`・`scale`・`player`・`usage` を読む。
- `position` / `rotation` は **ContactのRoot Transformのワールド位置・回転**。形状のPosition/Rotationオフセット、接触点 `contactPoint`、プレイヤーの足元とは別。
- `player` はアバターContactを所有するプレイヤー。ワールドContactではnull。犬オブジェクトのネットワークOwnerと混同しない。
- タグは大文字小文字を区別する。ReceiverはAvatarを検知対象に設定する。

これは接触から得る参照であり、アバターHierarchy全体を列挙する機能ではない。入場した全ペットの位置を初めから無条件に取得する設計にはしない。検知範囲へ入ったものを扱う。

## 4. 犬っこギミック

### 4.1 移動・向き・設置

追跡パスはPrefab名を含む。組み込み先で改名した場合は先頭を読み替える。

```text
dog_MA
├─ kotei                         ← mateONでFreezeToWorld
│  └─ 追従GIMMICK2 out            ← 移動方向の選択用Controller
├─ 追従GIMMICK
│  ├─ constraint_follow1         ← FreezeToWorld既定ON
│  │  └─ constraint_follow2      ← 自分自身と目標点の重みで追従
│  └─ object(中に入れる)          ← XZ位置、接地Y、Aimの計算結果
└─ 1                             ← 上のobjectへParentConstraint
   └─ 2                          ← 持ち主へ向くAimConstraint
      └─ inu_light_v3            ← 表示モデルの移動ルート、scale 0.1
         └─ アーマチュア
            └─ Hips             ← 芸・姿勢で位置と回転が動く胴体
```

- `dog_MA.prefab:2833` の `1` は `object(中に入れる)` を参照するVRCParentConstraint。`:3524` の `2` は持ち主向きのVRCAimConstraint。したがって `1` や追従計算用Transformの向きでは、`2` で追加される回転を拾えない。
- `object(中に入れる)` はVRCAimConstraint＋VRCPositionConstraint 2個（XZとY）で駆動（`:6020` / `:6134` / `:6243`）。`VRCRaycast` の接地結果をYへ使う。PhysBoneは耳・尾などの付加挙動で、位置取得の必須条件ではない。
- `mateON.anim` は `kotei.FreezeToWorld=1` と `追従GIMMICK.GlobalWeight=1` を設定。`mateOFF.anim` は解除へ遷移する。`gimmick.controller` は `pet_kotei` / `Pet_Wait` を条件にこれらへ遷移する。
- 固定するのは制御側の `kotei`。**取得対象をkoteiへ切り替えず、常に表示側ルートを追う**。固定中も下流の追従・接地・芸は別に評価される。[Freeze To Worldの公式説明](https://creators.vrchat.com/common-components/constraints/#freeze-to-world)
- `anim motion/アーマチュア_kaiten.anim` は `アーマチュア` 自体の位置・回転を動かす。他の芸も `アーマチュア/Hips` を動かす。`inu_light_v3` は近接・接地基準に適するが、**芸中の実際の体の向きまで表すTransformではない**。

### 4.2 既存Contact Sender

| コンポーネントの場所（dog_MA以下） | Root Transform | タグ | Local Only / 半径 / Position | 有効状態・用途判断 |
|---|---|---|---|---|
| `追従GIMMICK/contact`（`:1001`） | `追従GIMMICK/constraint_follow1/constraint_follow2` | `Pet_Run` | ON / 0.02 / 0 | コンポーネントON。ローカル走行判定用。表示側の最終回転・接地Yを表さない |
| `esa position/esa_ire/conract`（`:2143`） | `1/2/inu_light_v3` | `Pet_Fed` | OFF / 0.3 / `(0,0,1.32)` | コンポーネントON、親 `esa_ire` が既定OFF。ごはん表示時だけ有効 |
| `water position/esa_ire (1)/conract`（`:4038`） | `1/2/inu_light_v3` | `Pet_Watered` | OFF / 0.3 / `(0,0,1.32)` | コンポーネントON、親が既定OFF。水表示時だけ有効 |

半径とPositionは保存されたローカル値。モデルRootのscale 0.1やアバターのスケールを考慮する。Senderは**3個**、Receiverは**4個**（Pet_Run、給餌、給水、Handによるnade）。首輪Prefabに追加Contactはない。

食器SenderのRoot参照 `2131312235359727665` は、FBXのルートTransform `-8679921383154817045` に対応し、PrefabInstanceの親は `1/2`、名前は `inu_light_v3`。**コンポーネントが食器の下にあるから食器の座標、という構成ではない**。ただし `gohan OFF.anim` / `mizu OFF.anim` がその親を無効化するため、常時存在の信号には使えない。

### 4.3 無改変での対応可否・収納・他人からの見え方

- 専用Senderを表示側に追加する構造上の余地はあるが、来訪者のアバター改変が必要になるため今回の解決策にはしない。ワールド側のReceiverだけでは、欠けているアバター側Senderを補えない。
- 既存の食器Senderは条件付きの検知に限られる。食器をしまったこととペット本体が消えたことを区別できず、通常時のペットの存在判定には採用しない。
- 今回確認したメニューはゲージ・エフェクト・ごはん・水。**犬本体の収納ON/OFFは、このPrefabのメニューと参照Animationには見つからなかった**。`Pet_Hide` というパラメータ名だけでは収納と判定できない。来訪者が独自に追加した収納の挙動は、購入時Prefabの調査からは保証できない。
- MAで `pet_kotei`、`Pet_Wait`、`Pet_Zoomies`、`global_motion`、`idou_global` 等は同期対象。`Pet_Run` 等にはローカル専用値もある。ローカルParameter Driverから `global_motion` / `idou_global` を書き、受信側の遷移条件に使う構成がある。**ローカル専用パラメータがあることだけで、ペット全体が本人にしか見えないとは判定しない**。
- Constraintの自己参照を使う追従や、固定を受け取る時刻の違いは位置差の要因。固定後に持ち主が離れるケース、途中参加、芸中の胴体方向を2人で検証する。

## 5. ムチォ（Muchio）

### 5.1 基準Transformと既存Sender

`MuchioBase.prefab` のルート名はMuchioBase、直下の `muchio.prefab` を導入した場合はmuchio。以下の `<ペット名>` はmuchio/mofuo等に置換する。

```text
<ペット名>
├─ PetOwnerSense                  ← 持ち主側Sender。追跡には使わない
├─ PetWorld                       ← 外部MuchioWorldOriginを参照
│  └─ PetMount                    ← 追従・掴み・だっこ先を切替
│     └─ PetGroundRoot            ← Raycastの接地Y
│        └─ PetMotionRoot
│           ├─ muchio            ← 見た目。歩行時に少し上下する
│           └─ KissContacts      ← 常時の既存Sender（表示中）
│              └─ MuchioFlagSender ← 状態フラグ用、既定OFF
└─ PetRayRig / PetTarget / PetCarryPoint など
```

| Sender（MuchioBase以下） | Root | タグ | Local Only / 半径 / 形状Position | 初期状態・用途 |
|---|---|---|---|---|
| `PetWorld/PetMount/PetGroundRoot/PetMotionRoot/KissContacts`（`:12101`） | 未指定＝自身 | **`OnigiriPetKiss`** | OFF / 0.107142866 / 0 | GameObject・コンポーネントともON。今回の推奨 |
| 上記の子 `MuchioFlagSender`（`:35`） | 未指定＝自身 | `OnigiriPetFlag` | OFF / 0.107142866 / 0 | GameObjectはOFF。MuchioFlagOn/Offで切替。存在判定に使わない |
| `PetOwnerSense`（`:10875`） | 未指定＝自身 | `MuchioOwnerSense` | OFF / 0.05 / 0 | ON。持ち主側の基準でありペット位置ではない |

Senderは**3個**、Receiverは**6個**。`OnigiriPetKiss` / `OnigiriPetFlag` を読むアバター側Receiverは既存の仲間検出用。ワールドにはReceiverだけを置けばよく、同名タグのSenderを犬に追加する必要はない（既存のキス等を意図せず発火させない）。

KissContactsのTransform自身のlocalPositionは **`(0,0.08571429,0)`**、localRotationはidentity。形状だけをずらした設定ではないため、proxyからこの点が得られる。アバターの足元ではなく、接地補正後のペット移動に追従する点である。

ただし `MuchioWalk.anim` は兄弟の `muchio` をローカルY=0〜0.014285714、`MuchioWalkSoft.anim` は0〜0.007142857で上下させる。KissContactsはこの上下動を共有しない。**近接反応の基準としては使えるが、メッシュの厳密な中心位置とは異なる**。実機で誤差を測り、既存Senderから得られる精度で成立するワールド側のリアクションを選ぶ。

### 5.2 動かし方・設置

- `PetWorld` はVRCParentConstraintで `gimic/MuchioWorldOrigin.prefab` のTransformを参照（`:11107`）。外部アセット参照の実クライアントでの評価は今回未検証。名前からワールド内の配置物と同一視しない。
- `PetMount` のVRCPositionConstraintは自分自身・PetTarget・PetGrabEnd・KissHold・PetCarryPointを重みで選択（`:10480`）。回転はVRCLookAtConstraint（`:10589`）。`PetGrab` にPhysBone（parameter `PetFollow`）があり、掴み点が移動先になる。
- `PetGroundRoot` はYだけをPetGroundHitへ合わせる（`:11459`）。VRCRaycastはPetRayRig/PetRayG（`:11045`）。Senderはこの補正の下にあるので、PetMountを直接読むより表示位置に近い。
- `PetFollowPark.anim` / `PetDropped.anim` はPositionConstraintの自分自身の重み20、その他の移動元を0にする。全体をFreezeToWorldで固定する単純な設置トグルではない。`PetFollowGate` は `MuchioOwnerNear` を使ってFree/Parkをブレンドするため、**持ち主が離れれば追従を再開し得る**。
- `PetKissFreeze` はKissHold/KissLookを一度更新し、`PetKissHold` でそれらをFreezeToWorldする。だっこ・掴み・PutDown・Droppedも別状態。置いたまま永続固定できる、と名前だけで断定しない。
- どの状態でも追跡点はKissContactsのまま。実際にその端末で描かれるペットに付いている点を読む。

### 5.3 収納・同期・改変

- `MuchioMenu.asset` の `PetEnabled` が `PetOn.anim` / `PetOff.anim` に接続。PetOffは **`PetWorld` 自体をOFF** にする。モデル・KissContacts・FlagSenderはすべて子孫なので一緒に消える。PetOwnerSense / PetRayRigもOFF。表示だけ消してSenderが残るカーブではない。
- MAの `PetEnabled` / `PetCarry` / `PetKiss` / `MuchioFlag` 等はLocal Only OFF。`MuchioOwnerNear` / `MuchioContactAlive` / `MuchioWalkStyle` 等はLocal Only ON。追従・接地は端末ごとの評価で、座標そのものを共通値として送っていると確認できたわけではない。
- 既存のKissContactsを読むだけならアバター改変不要。今回の連携には既定タグ `OnigiriPetKiss` を使い、来訪者へのSender追加や専用タグ設定を前提にしない。

## 6. 改変条件・素材の扱い

今回の用途はユーザー指定素材の構造調査と、来訪者のアバターを改変せず既存Senderを検知できるかの判断。モデルの複製・生成素材への入力・ワールドへの移植はしていない。以下は調査時に確認した利用条件の記録であり、Sender追加は今回の作業範囲外。

| 対象 | 調べた根拠（2026-09-21） | 今回の判断 |
|---|---|---|
| 犬っこ | [販売元](https://booth.pm/ja/items/8701767) が案内する [利用規約](https://drive.google.com/file/d/1PQ2Lpof-xSEp1qUDdMZYZTfHfOSuPXy-/view) | 指定Assets内に規約本文なし。リンク先本文を取得できず、**Sender追加の改変条件は未確認**。構造上の追加可否と許諾を分ける。実際のアバター改変前に購入同梱規約または配布元本文を確認する |
| ムチォ | [販売元の利用規約欄](https://booth.pm/ja/items/8657397) | 自分のアバターへの組み込み・改変・パーツ追加が認められている。再配布、第三者がDLできる共有、**ワールドへのアップロード**は禁止。ワールド側は自作Receiver/Udonだけを実装し、ムチォの素材を持ち込まない。アプリの解析・改造は禁止であり今回対象外 |

ムチォ内のKATのMITライセンスは、ムチォ本体の利用許諾に読み替えない。配布元の現行規約と購入時の同梱版に差がある場合は、追加作業前に確認する。

## 7. 次の最小試作

**まずムチォを無改変で検知する小さな試作を作る**。本番犬AIへ接続する前に、ワールド側Receiver＋Udon＋位置マーカー/方向矢印で実際に得られる情報を可視化する（今回は未実装）。

1. Receiverタグ `OnigiriPetKiss`、対象Avatar。半径1〜2m程度の限定領域、または犬周囲に配置。半径3m・各軸6mのContact上限を越す巨大Receiverで全ワールドを覆わない。
2. Enterでproxyと所有playerを登録。表示は `proxy.position` と `proxy.rotation`。`contactPoint`を追跡位置にしない。
3. 検知中だけ追跡する初回仕様。Exit、proxy無効化、プレイヤー退出・アバター変更で対象解除し、古い座標で犬を動かさない。`isValid` は「ペットが表示中」「現在も重なっている」そのものではない。イベント欠落・収納の実際の通知を実機で確認する。
4. 同じ人が複数ペットを持つ場合があるため、playerIdだけで1体に潰さない。同じSenderの重複登録を防ぎ、タグを恒久的な個体IDとして扱わない。
5. 犬っこは今回の通常時検知の対応対象に含めない。将来、配布元の既定構成に適切なSenderが追加された場合は、その版を改めて調査する。
6. 犬AIが近づく実装は、犬のOwnerが自端末で観測した対象を使い、犬の動きは既存の同期経路へ載せる。全端末から犬のTransformを書かない。ペット所有者と犬Ownerが別の場合の見た目のずれを採否判断に含める。

## 8. 実機チェックリスト（未実施）

以下の本試験は、アバターを改変しないムチォを対象とする。犬っこの食器表示中だけの検知は参考情報であり、対応完了の条件には含めない。

### 0. Editorで判定できない前提

- [ ] MAビルド後も想定Sender/Root/タグが残る。アバター・ワールドを対象版でアップロードし直している
- [ ] VRChatでEnterが届き、`usage=Avatar`、有効な `player` とproxyが得られる
- [ ] Contact無効設定、非表示アバター、Fallback/Impostor、距離によるカリング等で検知できない場合に、対象を残さず安全に解除できる（各条件を個別記録）
- [ ] アバター倍率を変えても取得点・球の実寸・方向軸を説明できる

### 1. 1人で確認

| 操作 | 確認すること | 結果 |
|---|---|---|
| Receiverに入る／出る | Enter/Exit、タグ、proxyの有効性、取得したplayerId | 未 |
| 平地・坂で移動、停止、旋回 | 矢印がペットへ追従。持ち主の足元・接触点へ張り付かない | 未 |
| だっこ・掴み・離す（ムチォ） | 接地補正のON/OFF後も見た目付近を追う。所有playerが掴んだ人へ誤変更されない | 未 |
| Park/PutDown→持ち主が離れる | どの条件で追従を再開するか、観測位置がその間も見た目に一致するか | 未 |
| 歩行上下動 | 既存Senderと実際の胴体の差を測り、近接リアクションに十分な精度か判断 | 未 |
| 収納→再表示 | 見た目が消えたら追跡解除。再表示で新たに検知 | 未 |
| アバター変更・再読込 | 古いproxyを利用しない。新しいアバターで再登録できる | 未 |

### 2. 2人以上で確認

| 操作 | 確認すること | 結果 |
|---|---|---|
| Aがペット所持、Bが犬Owner | Bでペットを検知しplayer=Aを取得。Bの矢印とA/B双方のペット表示を比較 | 未 |
| 固定→持ち主が離れる→Bが途中参加 | 固定位置・接地・向きの差。Late Joinerに固定時の位置が再現されるか | 未 |
| 2人／同一人の複数ペット | 取り違え、重複、片方退出で他方まで消える問題がない | 未 |
| ペット所持者退出・犬Owner退出 | 無効参照の解除、犬Owner移譲後の再検知、追跡終了 | 未 |

まずPCで成立を確かめる。Quest単体/iOSは対象アバターの対応版とContact・Constraintの実動作を別途確認し、PCの結果を転記しない。

| 症状 | 最初に確認する場所 |
|---|---|
| まったく検知しない | アップロード版、タグの綴り、Sender/親のActive、Local Only、Avatar表示とContact設定 |
| 持ち主の足元へ矢印が出る | MuchioOwnerSenseを誤使用していないか、Root参照と形状Positionを混同していないか |
| 犬っこが食器を出した時だけ検知される（参考） | 既存Senderの親が食器という既知の制限。通常時の対応対象にはしない |
| 位置は近いが向きが違う | 移動ルートと骨格の回転差、マーカー前方軸、アニメーション |
| 自分は合うが他人はずれる | ローカルConstraint/PB/接地、固定受信時刻、途中参加、アバター表示状態 |

## 9. 検証記録

保存済みの静的構造・参照・カーブの調査を完了。Unity上の動的評価、Udon試作のコンパイル、VRChat検知・追従・位置一致は未実施。文書検査・独立レビュー結果はIssueへ記録する。
