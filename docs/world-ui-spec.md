# KominkaHillside -- ワールド固有UI設定

配置先：`docs/world-ui-spec.md`。共通資料1.0.0の09章を基にしたワールド固有の設定。Unityへ自動インポートするスキーマではない。
全体のビジュアル方針は `core-concept.md` が正本。この資料は参照先・更新日とUIへの具体化を持ち、全体パレットを二重管理しない。既存のUI仕様が別名ならそちらを正本とし、この資料は案内にする。
新規・全面改修では全セクションを確認。S変更は該当箇所と決定記録だけ更新する。不要は理由付き `not_applicable`、不明は `unknown`、仮決定は `provisional`。

## 1. 識別と環境

| 項目 | 値 |
|---|---|
| world / UI scope | KominkaHillside (古民家の裏山) / 古民家ドッグランと基本構成を揃え、中身を裏山向けにする。しゃがめない人向けの接続/解除補助を追加する (2026-09-20ユーザー方針) |
| この設定の版・最終更新日 | 0.6 / 2026-09-21 (ヘッダーのナイトモード OFF/ON を追加。0.5: 集合の1分制限/空席/斜面、補助操作、個別ボール回収、共通購入権を合意) |
| 共通リファレンスの版・パス | 1.0.0 / `.claude/skills/vrc-udonsharp/reference/ui/00_START_HERE.md` (プロジェクトルート基準) |
| 対象シーン・Prefab・ゲーム処理 | `Assets/Scenes/VRCDefaultWorldScene.unity` / 移植する犬ギミックの UI (IMP-DOG-001 で棚卸し) |
| Unity / Worlds SDK / TMP / ClientSim | 2022.3.22f1 / 3.10.5 / 3.0.6 / SDK 同梱 |
| ビルドターゲット | Windows / Android / iOSを対象とする目標。Quest単体・iOSを含む実機確認は未了 (`game-design.md` §1、config/platform-support.json) |
| 対象機器・入力 | PCVR / Desktop / Quest単体VR / iOS Touch。Android Touchは追加の確認候補 |
| 対応言語 | 日本語 (古民家と同じ。provisional) |
| 主目的・主要操作・戻る活動 | 犬との遊びと散歩を補助する。犬と左右を選んで接続/解除し、自分の接続犬に目的地を伝えて散歩へ戻る。既存メニューの構成を調査後、集合・ライブ等の対象機能を具体化する |
| UIの影響範囲 | 個人 / 共有 / 混在、項目別に定義 |
| 検証の到達点 | 合意要件と画面候補・操作契約の一部を記録。モック・実装・実機確認は未実施。画面・入力・権限は [DES-UI-001](development/issues/archive/DES-UI-001-menu-and-leash-assist.md)、有料ライブは [DES-LIV-001](development/issues/DES-LIV-001-hillside-live.md) で具体化 |

補助UIの合意: 個人メニューに犬一覧と「左につなぐ／右につなぐ／外す」を置き、散歩先・座位でも使える入口を用意する。接続は近距離 (4mを試作基準)、自己解除は距離不問。固定ボードは案内の候補。寸法・端末別入力は試作する。物理接続と同じ上限・他人の接続・集合/Fetchの裁定を使い、処理待ちや拒否も表示する。適用資料はXR-01 / XR-04。

2026-09-20追加合意: 「あそぶ」の集合はトレーナー特典として古民家と同じ隊形選択を持つ。自他の接続犬を除外し、終了後は参加犬が集合場所周辺で自由行動する。各目的地のリード取り出し口は各自ローカルで利用し、解除後の道具は解除地点の最寄り利用可能拠点へ回収する。仕様は [game-design §4](game-design.md)、調査根拠と残件は [DES-UI-001](development/issues/archive/DES-UI-001-menu-and-leash-assist.md)。全タブ構成・距離計算など回収の実装詳細・権利確認の実装は未確定。NOT_TESTED。

## 2. ビジュアル方針との接続

| 必要情報 | 参照ファイル・節・版 | UIへの解釈 | 状態 |
|---|---|---|---|
| コアコンセプト | `core-concept.md` §1〜2 (2026-09-19) | 犬が主役。UI は古民家のトークンを継承し、案内は最小 | provisional |
| 主役と背景、避ける遮蔽 | unknown | unknown | missing |
| パレットと明暗 | `core-concept.md` §3 役割別パレット (2026-09-19) | 暗い面 + 白文字を継承。状態は色以外でも区別 | provisional |
| 形状・角・輪郭 | unknown | unknown | missing |
| 素材・模様・デフォルメ | unknown | unknown | missing |
| 昼夜・照明・演出時の背景 | unknown | unknown | missing |
| UI文字とアイコンの方針 | unknown | unknown | missing |

検出規則：参照が存在しない→missing。項目はあるが具体的なUI役割に変換できない→missing。複数資料が矛盾→conflict。仮の解釈で試作→provisional。**ファイル名が埋まっただけではreadyにしない。**

不足への対応：`止める範囲 / 進める試作 / 決定者または必要証拠 / 次に見直す工程` を記載。

## 3. 役割トークン

| トークン | 値 | 単位・色空間・対象条件 | 出典／決定理由 | 状態 |
|---|---|---|---|---|
| surface.panel | unknown | sRGB色＋alpha、背景条件 | unknown | unknown |
| surface.raised | unknown | sRGB色＋alpha | unknown | unknown |
| text.primary / secondary | unknown | sRGB色、適用面 | unknown | unknown |
| action.primary / onPrimary | unknown | 色を対で定義 | unknown | unknown |
| selection.surface / marker | unknown | 色＋形・ラベル | unknown | unknown |
| focus.ring | unknown | 色、実装、厚みの単位 | unknown | unknown |
| disabled.surface / text | unknown | 理由は可読性を保つ | unknown | unknown |
| status.success / warning / error | unknown | 色＋文言・アイコン | unknown | unknown |
| font.heading / body / numeric | unknown | font asset、weight、fallback | unknown | unknown |
| type.heading / body / label / auxiliary | unknown | TMP単位、実効scale、実距離、測った字高 | unknown | unknown |
| spacing.inset / item / group | unknown | Canvas設計単位、実効scale | unknown | unknown |
| shape.radius / border | unknown | Sprite borderとRect、厚み・最小寸法 | unknown | unknown |
| motion.transition / reduced | unknown | 秒、入力方式、軽減時の振る舞い | unknown | unknown |

トークンの循環参照、未定義の参照、色の組合せ不足を確認する。値はテーマの正本を1つ決める。Inspector直修正と生成設定のどちらが正か曖昧にしない。

## 4. 画面と情報構造

以下は画面候補 (provisional)。集合の用途・利用権・対象・終了後の挙動と拠点回収は合意済みだが、タブ追加・全体の並び・具体操作は未合意。古民家の7タブを全機能の採用範囲とは扱わない。

| screen ID | 目的・利用場面 | 常時／必要時／詳細 | パターン | 主要操作 | 入る／戻る／閉じる | 保持する文脈 |
|---|---|---|---|---|---|---|
| WALK | 接続する犬と行き先を決めて散歩へ戻る | 必要時 | 「さんぽ」タブ案。犬6匹の一覧 | 犬/左右選択、接続/解除、行き先2択 | 古民家型メニューから入る。共通の閉じる。接続後は連続操作のため表示維持案 | 犬の選択はローカル、接続・目的地の現在値は共有状態から表示 |
| GATHER | 犬を並べて写真・ライブを楽しむ | 必要時→詳細 | 「あそぶ」内のトレーナー集合→隊形選択 | 隊形・場所・開始・古民家同様の途中終了。開催から1分 | 開始成功時は犬を見るため閉じる案。失敗は同画面。パネルを閉じても開催は継続 | 開催対象・開催者・隊形・残り時間。1〜5匹は空席保持。斜面配置対応 |
| TOYS | ボールを使える場所へ戻す | 必要時 | 古民家のおもちゃタブを候補 | 1個ずつ手元へ。一括リセットは置かない | メニュー共通の戻る/閉じる | 使用中・回収可能なボール |
| NIGHT | 自分の画面だけ夜にする (IMP-ENV-002、2026-09-21 実装) | 常時 (ヘッダー) | タブではなくヘッダーのボタン 1 つ「ナイトモード: OFF / ON」(今の状態を表示、ON は金色)。中間値なし | 押すたびに反転、3 秒フェード | どのタブでも見える。閉じてもフェードは続く | ON/OFF (ローカル。保存しない)。設定が 2 つ目以上になったら「せってい」タブへ移す ([計画書](plans/2026-09-21-sky-and-night-mode.md) §1) |

リード拠点は画面と別の常設入口。1拠点1操作対象からローカル道具を取り出す。物理の取り出しとメニューからの接続補助をどちらも残す。端末別の受け取り方は§7の残件。

初回利用者への手がかり：unknown  
継続利用者への近道・復元：unknown  
会話・犬・戦闘を見る空間：unknown  
この画面に入れない情報と理由：unknown

## 5. 操作と状態契約

目的地選択の要件 (2026-09-20): メニューから広場/古民家側の散歩入口を指定し、自分の接続犬の目的地を変更する。画面操作は個人、犬の目的地と動きは共有。ワールド全犬への一括変更にはしない。初期値・追加接続・再接続・要求中の接続変更はDES-DOG-001、対象なし・現在値の混在・要求結果の表示はDES-UI-001で具体化する。仕様の正本は [game-design §4](game-design.md)。以下のunknownは詳細未設計であり、要件が未合意という意味ではない。適用観点はSOC-01〜04、未実装・NOT_TESTED。

| action ID | 対象ID | 表示範囲 | 変更する状態 | 実行者・書込者 | 実行可能条件 | 消費・結果 | 確認／取消 |
|---|---|---|---|---|---|---|---|
| walk.attach / detach | 犬ID・左右 | 個人UI / 結果は共有 | 接続先・左右、解除後の自由行動の中心 | 利用者が要求、既存LeashManager Ownerが裁定 | 接続は近距離・左右各3匹・未接続・集合/Fetch中でないこと。自己解除は距離不問。受理時に再判定 | 接続成立/拒否。道具の回収先は最寄り拠点 | 応答不明時は確定状態を再取得。取消の可否は未設計 |
| walk.destination | 自分の接続犬 | 個人UI / 結果は共有 | 犬ごとの目的地と本人の選択 | 利用者が要求、共有状態はLeashManager Ownerが裁定 | 対象なしは理由表示。要求中の接続変更はDES-DOG-001で具体化 | 全対象同一/混在/対象なしを区別。追加犬は本人が選んだ行き先を継承 | 要求の採否・結果対応は未設計 |
| gather.start / end | 集合対象犬・隊形・開催 | 個人UI / 結果は共有 | 集合と終了後の自由行動の中心 | 開始者はトレーナー。途中終了は古民家と同じ開催者/管理者。書込者の統合は未設計 | 自他の接続犬・開始時Fetch中の犬を除外。開催中は別集合不可。ライブ連携は別途具体化。権利の読込/未所持/取得失敗を区別 | 歩いて集合、到達不能時は障害物すり抜け。開催から60秒で周辺自由行動へ | 到着前の中断・応答不明の具体処理はDES-DOG-001 |
| leash.take / return | 拠点ID・ローカル道具 | ローカル | 手持ち/回収先 | 各利用者のローカル処理 | 他人が使用中でも取り出せる。同一人の連打・手持ち数は未設計 | 解除後に最寄り利用可能拠点へ回収。共有接続をローカルだけで成立させない | 接続成功/未接続Drop時の回収は未設計 |

| 状態軸 | このUIで使う値 | 読込・初期状態 | 根拠になるデータ |
|---|---|---|---|
| interaction | normal / focus / pressed等 | unknown | 入力 |
| selection | 選択中のtab / item / target | unknown | ローカルUI状態 |
| domain | owned / equipped / running等 | unknown | ゲーム・CE・共有状態 |
| availability | available / reason | unknown | 条件判定 |
| request | idle / pending / succeeded / rejected / unknown | unknown | 要求結果 |

代表状態の表示仕様：

| 状態・組合せ | 文言 | 色以外の手がかり | 操作可否 | 出口・回復 |
|---|---|---|---|---|
| 状態未取得 | unknown | unknown | unknown | unknown |
| 実行可能 | unknown | unknown | unknown | unknown |
| 選択中＋実行可能 | unknown | unknown | unknown | unknown |
| 資源・権限不足 | unknown | unknown | unknown | unknown |
| 処理中 | unknown | unknown | unknown | unknown |
| 完了 | unknown | unknown | unknown | unknown |
| 失敗・結果不明 | unknown | unknown | unknown | unknown |
| 対象消失・権限失効 | unknown | unknown | unknown | unknown |

進捗を使う場合：`意味 / 現在値 / 分母 / 単位 / 値の更新元 / 不明時 / 0% / 100% / 操作可能性 / 実装手段` を記録。不要ならnot_applicable。

## 6. 個人・共有・永続化

| 状態キー | 個人／共有 | 表示対象 | 管理者 | 同期手段 | 保存手段・寿命 | 初期復元 | 版・移行 |
|---|---|---|---|---|---|---|---|
| unknown | unknown | unknown | unknown | unknown | unknown | unknown | unknown |
| night (ナイトモード ON/OFF) | 個人 | 本人の画面 (全画面乗算 + 空の露出) | `KH_NightMode` (ローカル) | なし (同期変数 0 本) | 保存しない。退室で消える | 常に OFF で開始 | 2026-09-21 初版。ライブの夜とは max 合成 |

共有操作があれば記入：

- 採用順序・競合方式：unknown
- 同時要求、重複要求、観測revision不一致：unknown
- 操作権とObject Ownerの関係：unknown
- 開始者・owner・担当者の退出／中断復帰：unknown
- 途中参加者への復元元：unknown
- 待機・取消・終了・復旧：unknown
- 個人の消音／演出軽減と共有進行の関係：unknown

### トレーナーの共通購入権 (2026-09-20)

両ワールドで同じProduct/Listingを使い、どちらでも購入して両方で利用することを合意。利用権を確認するProductと、購入画面を開くListingは役割が異なり、互いに同じIDという意味ではない。

| 対象 | 古民家の現行値・出典 |
|---|---|
| Product | `prod_8c044b21-35f2-4ebb-80f9-e4f685f7bf9f` |
| Listing | `prod_81a5ac65-bd3d-40ad-8edb-2fc7d13b1cee` |
| 設定の参照元 | `C:/work/unity/vcc/KominkaDogRun/Assets/KominkaDogRun/Editor/KD_ProductDefinitions.cs` のTrainerProduct/TrainerListing。`experience.formation` と `experience.hosting_play` は同じ商品を参照 |
| Product実体 | 同プロジェクトの `Assets/KominkaDogRun/Data/CE/KD_Product_Trainer.asset` のIDと一致 |

公式確認 (2026-09-20): [World Stores](https://creators.vrchat.com/economy/store/world-store/) は同一Listingの複数ワールドでの特典提供を認める。[CE Udon Documentation](https://creators.vrchat.com/economy/sdk/udon-documentation/) はProduct IDでの照合、Listingを開く操作、シーンでのUdonProduct参照、購入情報読込後の所有確認を定義する。この仕様と既存IDを根拠に共通利用方式を採用する。裏山での配線・クライアント検証は未実施。

- 裏山の常駐Udonから同じProductを参照し、購入入口は同じListingを開く。権利は購入情報の読込完了後に判定し、読込中/未所持/取得失敗を分ける。開始要求の受理側でも実送信者の権利を再判定する。
- 購入・期限切れ通知とUI再表示で状態を更新する。古民家の入室時権利保持の運用と整合させ、滞在中の期限切れ処理は既存コード調査で具体化する。パネル非表示中も通知を受け取れる構成にする。
- 購入画面を開いた/閉じたことを購入成功としない。新商品の発行・管理画面の販売設定変更は今回行っていない。
- 検証: 古民家購入済み→裏山入室、裏山購入→古民家入室、未所持、読込遅延、期限切れを確認。ID設定の一致だけで双方向利用の実機合格にしない。実課金を伴う購入はユーザー確認で行う。

## 7. 端末・空間・入力

移植候補は古民家のVR右スティック下長押し・DesktopのT・Touchのメニューボタン。閉じるは共通ボタンを置く。VR/Touchの再操作は手元へ再配置、DesktopのTは開閉という既存差も確認する。これは古民家の調査結果であり、裏山での動作確認ではない。片手で右スティックを使えない場合の入口、拠点から道具を受け取る操作、Desktop/Touchで左右を指定する操作はモックで具体化する。下表のunknownはその未確定値を示す。

取り出し操作の試作案 (provisional): VRは拠点の1つの道具を使える手で持ち、持った手を接続先にする。Desktop/Touchは拠点Interactで目の前にローカル道具を出し、「左/右」を選んで通常のPickup操作で持つ。自動で手に持たせられると仮定しない。手首・しゃがみ操作が難しい場合はメニューから犬と左右を選んで直接接続する。同じ拠点を他人が使用していても各自が使える。右スティックを操作できない片手利用向けには拠点のメニュー入口を試作し、散歩先で再度開く導線も確認する。これらは新たなユーザー判断待ちにせず、実機の到達性・誤操作を開発側で比較する。

| モード | UI配置 | 開く | 決定 | 戻る | 閉じる | 説明を見る | 特殊条件 |
|---|---|---|---|---|---|---|---|
| PCVR | unknown | unknown | unknown | unknown | unknown | unknown | 左右、片手、座位、Pickup、アバターサイズ |
| Desktop | unknown | unknown | unknown | unknown | unknown | unknown | 移動・既存ショートカットとの競合 |
| Android Touch | unknown | unknown | unknown | unknown | unknown | unknown | 通常表示／Focus View、外部入力、縦横 |
| iOS Touch | unknown | unknown | unknown | unknown | unknown | unknown | 読込ビルド種別、縦横 |

計測記録：`端末名 / 解像度・向き / HMD設定 / 入力 / Canvas寸法 / 実効scale / 面までの距離 / 字形実寸 / target寸法・間隔 / 視角 / 背景 / 結果`。
Focus Viewの設定：unknown。通常入口：unknown。再配置・見失い復帰：unknown。

## 8. Unity部品と更新

| 部品・Prefab | 責務 | 寸法管理者 | Raycast | 更新契機 | 常駐／非表示 | ワールドoverride |
|---|---|---|---|---|---|---|
| unknown | unknown | unknown | unknown | unknown | unknown | unknown |

| UnityEvent | 対象Behaviour | メソッド／イベント名 | action ID | 実行確認 |
|---|---|---|---|---|
| unknown | unknown | unknown | unknown | NOT_TESTED |

| API・機能 | Unity出典 | VRChat許可 | Udon露出確認の版 | コンパイル | 実クライアント |
|---|---|---|---|---|---|
| unknown | unknown | unknown | unknown | NOT_TESTED | NOT_TESTED |

更新方針：値が変わった時／必要な周期／常時の理由：unknown  
CPU・GPU・メモリの予算と比較条件：unknown（初期値を根拠なく設定しない）  
同期対象のスキーマ・Network IDの保全：unknown

## 9. レビューと仮設定の記録

| decision ID | 項目・判断 | 状態 | 適用ルール | 理由・反例 | 影響 | 解消に必要な証拠 | 見直す工程 |
|---|---|---|---|---|---|---|---|
| D-001 | 集合はトレーナー特典、隊形選択、自他の接続犬除外、終了後の周辺滞在 | agreed / 未実装 | SOC-01〜04 | ユーザー合意。仕様の正本はgame-design §4 | GATHER | 権利・同期・終了の実機確認 | 契約具体化→実装 |
| D-002 | リードは各拠点1入口からローカル持ち出し、解除後は最寄り利用可能拠点へ | agreed / 未実装 | SOC-01 / XR-01 | ユーザー合意。取り合いと地面への残置をなくす | 拠点・接続補助 | 2人同時利用、回収、端末別操作 | モック→実装 |
| D-003 | WALK/GATHER/TOYSの画面候補 | provisional | IA-01 / IA-03 / IA-06 | 古民家の操作感を土台に用途を分ける | メニュー全体 | 採用タブ・主要操作の合意、操作モック | 次の設計相談 |
| D-004 | 集合1分/空席保持/斜面可/到達不能時すり抜け、追加犬の目的地継承、接続補助、個別ボール回収、同一商品利用 | agreed / 未実装 | SOC-01〜04 / XR-01 | Q1〜Q5等へのユーザー回答。仕様はgame-design §4 | 犬・UI・CE | 下記T-004〜006ほか | 試作→実機確認 |

| test ID | 条件・操作 | 期待結果 | 実測・観察 | 判定 | 証拠・版 |
|---|---|---|---|---|---|
| T-001 | トレーナー/非トレーナーで集合を開く。自他の接続犬を含めて開始 | 特典の可否と対象外理由が分かり、接続犬を動かさない | 未実施 | NOT_TESTED | game-design §6 条件11 |
| T-002 | 集合→写真/ライブ→終了→自由行動。別クライアントが途中参加 | 終了後も周辺に留まり、同じ自由行動の中心を復元する | 未実施 | NOT_TESTED | game-design §6 条件11 |
| T-003 | 2人が同じ拠点から取り出し、山上と道の途中で解除 | 他人の道具に干渉せず、解除地点に残さず最寄り拠点へ回収 | 未実施 | NOT_TESTED | game-design §6 条件12 |
| T-004 | 斜面へ1〜5匹で集合。途中に障害物を置く。60秒経過・途中終了 | 空席を残し地表へ並ぶ。到達不能時はすり抜け、終了後まで救済移動を続けない。接続/新規Fetch不可 | 未実施 | NOT_TESTED | game-design §4 |
| T-005 | 帰り道で犬を追加接続、遠くの自己接続犬を解除、利用中/待機中のボールを呼ぶ | 追加犬は選んだ行き先へ。自己解除は距離不問。空いたボールだけ個別に手元の地表へ | 未実施 | NOT_TESTED | game-design §4 |
| T-006 | 古民家→裏山、裏山→古民家で同じ商品の購入権を確認 | 両側で認識し二重購入を求めない。読込中を未所持扱いしない | 未実施 | NOT_TESTED | 本書§6 |

判定はPASS / FAIL / NOT_TESTED / N_A（理由必須）。実機未所持はNOT_TESTED。S変更の場合、既存証拠を再利用する範囲と、今回更新した証拠を短く書く。

## 10. 設定の充足判定

- 対象画面について主目的と主要操作が書けている。
- 使用する役割トークンが具体値または追跡可能な参照で解決できる。
- 各数値に単位・適用する端末/距離/scale・根拠種別がある。
- 操作に対象・権限・状態・結果・復帰が対応する。
- 対応すると記載した全入力で入口と出口がある。
- 共有/CE/保存のunknownは、実処理接続前に解消する。
- provisionalとNOT_TESTEDをready/PASSに取り違えていない。

この判定は文章による契約。エージェントへ渡す時に自動検査コードが存在すると誤認させない。必要なら既存のワールド検証ツールへ未定義トークン・接続欠落等の検査を追加する。
