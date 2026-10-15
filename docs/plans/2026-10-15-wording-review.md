# 看板・メニュー・おじいさんの文言の棚卸し (2026-10-15)

> Issue: [IMP-UI-010](../development/issues/IMP-UI-010-wording-review.md)。この文書は **現状 → 問題 → 改善案** の表と、面ごとの文体の規則を持つ。決定とレビューの記録は Issue 側。
> 現状の文は 2026-10-15 の `UITranslations.json` / `CourseUITranslations.json` (ja) から写した。事実関係は同日のコードで照合済み (§0)。
> 改行は `\n` で示す。改善案の `en` は自作、他 11 言語は反映時に AI 訳 (校正なし) を入れる。

## 0. 照合した事実 (案を書く前にコードで確かめたもの)

| 事実 | 出所 |
|---|---|
| おじいさんの左ナビは 4 本: 過ごし方 / さんぽ / おやつ / 呼ぶ。古民家の 8 本 (メニュー・なでかた・犬のこと・写真のこと・おかいもの) は裏山では作らない | `KD_ShopKeeperSetupEditor.Hillside.cs` NavEvents、`KD_ShopKeeper.cs` navImages |
| おじいさんの本文は 1 行 ≤ 16 全角 (本文 26pt / 有効幅 440px)。Menu/4e が実測。const と文言表 ja は一致必須 (ビルドガード) | `KD_ShopKeeper.cs` 文言の宣言、`KD_LocalizationBuildGuard.cs` |
| おじいさんは (49.9, 42.8) 向き 235°、看板 A「あそびかた」は (53.0, 44.5) 向き 55°。おじいさんは A の 11° 左前に立つ = A は彼のうしろ | [門前の配置 §6](2026-10-06-spawn-area-layout.md) |
| 支度ポイントは 200pt 一度きり。初期配布は 0。滞在 1 分で 1pt | `KD_ProgressLedger.HillsideKeeperPoints`、`KD_TreatPouch.WordPointsStaySub` |
| おやつは初期 5 個 (bone_biscuit、1pt × 5 個パック)。出すボタンは「出す」 | `KD_ProductDefinitions.cs` 86 行、`KD_TreatPouch.WordDispense` |
| 呼ぶ: 無料ポーズは ごろん / ふせ / おすわり / うずくまる (へそ天ほかはポイント)。戻すボタンは「自由にする」。権限のトグルは設定ホームの「呼ぶを全員に許可: ON / OFF」(`WordOpenOn` / `WordOpenOff`、オーナー、いなければ Master)。「みんなにも開放」(`WordSleepOpenToAll`) は画面に出ない (字形の宣言と Verify のメッセージだけ。**起票時の表は誤り。実装レビューで訂正**)。芸 (バーン / ジャンプ) はトレーナー特典で、提供前は販売説明に載せない | `call.pose0-4`、`KD_TreatPouch.WordSleepWake` / `WordOpenOn`、`KD_MenuFinishEditor.cs` 184 行、DES-UI-005 |
| さんぽの行き先 6 つ: 広場 / 古民家側の入口 / 奥の広場 / 川辺 / 山道から周回 / 川から周回。つなぐのは「左につなぐ」「右につなぐ」、外すのは「はずす」。片手 3 匹・両手 6 匹 | `KD_TreatPouch.WordWalk*`、`course.walk.*` |
| 手元メニューのタブ 7 つ: おやつ / おもちゃ / さんぽ / 呼ぶ / ライブ / かざり / あそぶ。なでた時の光は かざり タブの中の群「撫でエフェクト」(`WordFxGroup`。古民家の「パーティクル」タブは裏山に無い。**起票時の表は誤り。実装レビューで訂正**) | `KD_TreatPouch.cs` Tab*、`KD_TreatSetupEditor.Hillside.cs` 566 行 |
| トレーナーの特典 (裏山): 集合 + フリスビーの取り方の切り替え (`あそぶ`)。かけっこ・2 匹の遊び・犬目線カメラは裏山に無い | `KD_ActivitiesMenu.cs` 裏山改変、`KH_FrisbeeCatch.cs` |
| 看板 A の帯 `guide.menu` が手元メニューの開き方 (VR / Desktop / スマホ) の正本。看板 A の紙は フリスビー / ボール / ポイントとトレーナー の 3 枚 (設計書 §7 の「Credits のかざり」は JSON に無い = 設計書が古い) | `KH_GuideBoardsEditor.cs`、`guide.*` |
| 共有フォント (KD_NotoSerif SDF、48pt、2048²) の字形は 873。新しい字は各 Setup が AddGlyphs で足す (例: 「帯」は未収録) | `Assets/KominkaHillside/Font/KD_NotoSerif SDF.asset` |

## 1. 面ごとの文体の規則 (レビューの物差し)

| 面 | 口調 | 長さ | 書かない |
|---|---|---|---|
| 看板・帯 (`guide.*` `walk.guide.*` `wash.guide.*`) | 体言止め、または常体の言い切り。です/ます は使わない (既存の `guide.menu` / `guide.items.subtitle` / `wash.line` の です/ます は今回は据え置き)。ひらがな多め。手順は ①②③ か 1 2 3 | 帯・キャプション・箇条の 1 行は 24 字を目安 (本文 `guide.shop.body` `guide.kominka.body` `guide.frisbee.more` は折り返す前提で例外。表示の収まりは Guide/2 の実測が正)、紙 1 枚 ≤ 8 行 | 「〜することができます」とその言い換え (〜られます・〜てあげられます)、端末ごとの開き方の重複 (`guide.menu` だけ)、Interact の操作説明 (トリガー / クリック / タップ)、画面に見えているタブ名の列挙 |
| 手元メニューの説明 (`welcome.*` `*.help` `*Help` `*.hint`) | です/ます。1 文 1 操作。見出しは体言止め | 1 項目 2 行まで。？の本文は 5 段落まで | 「ご確認ください」「お試しください」「利用できません」「反映」「入手経路」「あらためて」、記号の見出し (— や →)、同じ語の重なり (集合させて集合写真) |
| おじいさん (`KD_ShopKeeper.*`) | 「〜だよ / 〜しな / 〜おくれ / 〜もんだよ」(既存)。一人称は「わし」、相手は呼ばない。短い文を改行で区切り、大事な行と締めの行は よ / しな で終える | 1 行 ≤ 16 全角 (半角 0.5)、1 話題 ≤ 18 行 (空行を除く) | 看板や帯にある操作の手順、端末の列挙、メニューの階層 (「設定 → …」)、価格表、来場者の向きで変わる方角 (左・右。固定の目印で言う: わしのうしろ・塀ぎわ・地図の看板) |
| カートの看板 (`cart.board.*`) | 「〜てね」(DES-UI-007 承認済み) | — | です/ます との混在 |
| 状態・結果の 1 行 (`*.state.*` `*.result.*` `course.travel.*`) | 体言止め、または「〜してから」「〜まで待って」 | 1 行 | 「ご確認ください」「お試しください」「利用できません」「応答」「結果を確認」 |
| 初回の帯 (`walkIntro.*`) | 「〜しよう / 〜してみて」(既存。ゲームの誘導) | 1 文 | 説明の追加 |

共通: 名詞は手元メニューの札の表記に合わせる (さんぽ・おやつ・かざり・呼ぶ・おもちゃ・あそぶ。「お散歩」「散歩」は固有名の「おさんぽカート」以外では「さんぽ」)。なでる (撫でる は ようこその行の題だけ = Menu/11c が要求する既存の語)。ポーズ (姿勢・動作は使わない)。「入る」はトレーナーの加入だけに使い、入室は「来たとき」。おじいさんの場所は「門のそば」。数字は半角、看板とおじいさんは「1分」、メニューの説明は「1 分」。「犬たち」は複数のとき。古民家の呼び方: 景色とおじいさんは「古民家」、ワールドをまたぐ話 (ポータル・トレーナー) は「古民家ドッグラン」、「前作」は `guide.kominka.body` だけ。

## 2. おじいさん (`KD_ShopKeeper.*`、Tier 1、6 キー)

方針: 看板・帯が説明する操作は本人が繰り返さず、**どの看板を見るか**を言う。看板が無い話題 (おやつ・呼ぶ) だけ短く説明する。

### 2.1 `WordGreeting` (2 回目以降の挨拶)

現状:
> よく来たね。\nここは古民家の裏山だよ。\n犬たちと、のんびり\n歩いていきな。\n\n支度は、手元メニューの\n「かざり」でできる。\nVRは右スティックを下に\n倒したままにすると出る。\nDesktopはTキー、\nスマホは画面にある\n「メニュー」を押しな。\n\n知りたいことがあれば、\n左から選んでおくれ。

問題: 端末ごとの開き方が看板 A の帯・ようこそ・手のヒントと重複 (4 か所目)。「支度は『かざり』」が挨拶の主文になっていて、裏山の遊びに触れない。看板を指していない。

改善案 (ja):
> よく来たね。\nここは古民家の裏山だよ。\n犬たちと、のんびり\n歩いていきな。\n\n遊び方は、わしのうしろの\n「あそびかた」の看板だ。\nさんぽはリード台、\n洗うのは洗い場、\n道は地図の看板を見な。\n\n聞きたいことがあれば、\n左から選んでおくれ。

en:
> Welcome. This is the hillside behind the old farmhouse. Take a slow walk with the dogs.\n\nHow to play is on the board behind me. Walks: the leash rack. Washing: the wash station. Trails: the map board.\n\nIf you want to ask something, pick it on the left.

### 2.2 `WordGreetingGift` (はじめての挨拶、{0} = 200)

現状:
> よく来たね。\nはじめてだね。\n散歩の支度に、これを\n持っていきな。\n{0}ptだ。一度きりだよ。\n\n手元メニューの「かざり」で\n犬の支度ができる。\nVRは右スティックを下に\n倒したままにすると出る。\nDesktopはTキー、\nスマホは画面にある\n「メニュー」を押しな。\n\n知りたいことがあれば、\n左から選んでおくれ。

問題: 2.1 と同じ重複。ポイントの使い道が「かざり」だけ (おやつ・呼ぶのポーズにも使える)。

改善案 (ja):
> よく来たね。\nはじめてだね。\n散歩の支度に、これを\n持っていきな。\n{0}ptだ。一度きりだよ。\n使い道は、手元メニューの\n「かざり」や「おやつ」だ。\n\n遊び方は、わしのうしろの\n「あそびかた」の看板だ。\nさんぽはリード台、\n洗うのは洗い場、\n道は地図の看板を見な。\n\n聞きたいことがあれば、\n左から選んでおくれ。

en:
> Welcome. First time here? Take these for getting ready: {0} pt, just this once. Spend them in Dress or Treats in your hand menu.\n\nHow to play is on the board behind me. Walks: the leash rack. Washing: the wash station. Trails: the map board.\n\nIf you want to ask something, pick it on the left.

### 2.3 `InfoWays` (過ごし方)

現状:
> ここでの過ごし方だよ。\n好きな順でいい。\n\n犬をつないで、さんぽに行く。\n犬をなでる。\nボールやディスクを投げる。\nおやつをあげる。\n\n山の上からは、ふもとの\n古民家が見えるよ。\nときどき振り返ってみな。\n\n「かざり」で犬におめかし、\nなでたときの光も選べる。\n「呼ぶ」で犬をそばに呼んで\n休ませることもできる。\n\nぜんぶやらなくていい。\nながめているだけでも\nいいもんだよ。

問題: 裏山の遊びに「洗う」が無い。「かざり」「呼ぶ」の段落はメニューの説明 (アイテム案内の看板と重複)。

改善案 (ja):
> ここでの過ごし方だよ。\n好きな順でいい。\n\n犬をつないで、さんぽ。\nなでる。\nボールやディスクを投げる。\nおやつをあげる。\n帰ったら、洗ってやる。\n\n山の上からは、ふもとの\n古民家が見えるよ。\nときどき振り返ってみな。\n\nぜんぶやらなくていい。\nながめているだけでも\nいいもんだよ。

en:
> Here is how to spend your time. Any order you like.\n\nLeash a dog and go for a walk. Pet them. Throw a ball or a disc. Give a treat. When you are back, give them a wash.\n\nFrom up on the hill you can see the old farmhouse below. Look back now and then.\n\nYou do not have to do it all. Just watching is fine, too.

### 2.4 `InfoWalk` (さんぽ)

現状:
> 手元メニューの「さんぽ」で\n犬をリードにつなげるよ。\n犬の行で「左につなぐ」か\n「右につなぐ」を押しな。\n近くにいる犬だけだ。\n片手に3匹、両手で6匹まで\nつなげる。\n\n行き先は6つから選べる。\n「広場」「奥の広場」「川辺」\nと、ぐるっと回る周回が2つ、\nそれに「古民家側の入口」だ。\nつないだ犬は、寄り道を\nしながら行き先へ歩く。\n\n外すときは「はずす」だ。\nつないだ本人だけが外せる。\n\nポイントがなくても、さんぽと\nボール遊びはいつでもできる。

問題: つなぎ方の手順がリード台の帯 (`walk.guide.*`) と重複。行き先の列挙が地図の看板と重複し、メニューの表記 (山道から周回 / 川から周回) とも違う。「ポイントがなくても」は看板 A の「だれでも無料」と重複。

改善案 (ja):
> つなぎ方は、リード台の\n帯に書いてあるよ。\n片手に3匹、両手で6匹だ。\n\n行き先は、手元メニューの\n「さんぽ」で選べる。\n道のことは、地図の看板を\n見な。\nつないだ犬は、寄り道を\nしながら歩くよ。\n\n外せるのは、つないだ\n本人だけだ。

en:
> How to leash a dog is on the strip above the leash rack. Three per hand, six with both.\n\nPick where to go in Walk in your hand menu. For the trails, see the map board. Leashed dogs wander a little on the way.\n\nOnly the person who leashed a dog can let it go.

### 2.5 `InfoTreats` (おやつ)

現状:
> 手元のメニューで「おやつ」を\n選び、「出す」を押しな。\nおやつが出るとメニューは\n閉じるから、犬にあげておくれ。\n\n手に持っていると犬が寄ってきて、\n手放すと、一番近くにいる犬が\n食べてくれるよ。\n一度に出せるのはひとつだけ。\nはじめに5個持たせてあるから、\nなくなったら「おやつ」で\nポイントと交換しな。

問題: 看板に無い話題なので本人が説明するのは正しい。「手に持っていると犬が寄ってきて、」はちょうど 16 全角で規約の上限 (超えてはいない。起票時の「超える」は誤り)。「閉じるから」の因果が冗長。

改善案 (ja):
> 手元メニューの「おやつ」で\n「出す」を押しな。\nおやつが出ると、メニューは\n閉じるよ。\n\n手に持つと犬が寄ってくる。\n放すと、いちばん近くの\n犬が食べるよ。\n出せるのは一度に一つだ。\n\nはじめに5個持たせてある。\nなくなったら「おやつ」で\nポイントと交換しな。

en:
> Open Treats in your hand menu and press Give. The menu closes once the treat is out.\n\nHold it and the dogs come over. Let go, and the nearest dog eats it. One treat at a time.\n\nYou start with 5. When they run out, trade points for more in Treats.

### 2.6 `InfoCall` (呼ぶ)

現状:
> 手元メニューの「呼ぶ」で\n犬を選び、ポーズを選んで、\n場所と向きを決める。\n犬が歩いてきて、そこで\n休んでくれるよ。\n自由にするなら、「呼ぶ」で\nその犬を選び、\n「自由にする」を押しな。\n\nリードでつないでいる犬は\n呼べない。先に外しな。\n\nごろん、ふせ、おすわり、\nうずくまるの4つは無料だ。\nほかの姿勢や動作は、\nポイントで増やせるよ。\n\n呼べるのは部屋のオーナー\n(いなければMaster) と、\n「呼ぶを全員に許可」が\nONのときの参加者だよ。

問題 (起票時の記述。**トグル名の指摘は誤認**: 画面の実物は「呼ぶを全員に許可: ON」で現状の文が正しい。実装レビューで判明 → §0・§12): 「ほかの姿勢や動作」の「動作」= 芸はトレーナー特典で提供前 (DES-UI-005: 載せない)。「戻すときは」の行き先が不明。最後の段落が設定画面の説明になっている。下の改善案は起票時のもので、**採用した文は §12** (トグル名「呼ぶを全員に許可」と「いなければMaster」を残した)。

改善案 (ja):
> 手元メニューの「呼ぶ」で\n犬とポーズを選び、\n場所と向きを決める。\n犬が歩いてきて、そこで\n休んでくれるよ。\n戻すときは、その犬の\n「自由にする」だ。\n\nつないでいる犬は呼べない。\n先に外しな。\n\nごろん、ふせ、おすわり、\nうずくまるは無料だ。\nほかの姿勢は、ポイントで\n増やせるよ。\n\n呼べるのは部屋のオーナーだ。\n「みんなにも開放」なら、\nだれでも呼べる。

en:
> Open Call in your hand menu, pick a dog and a pose, then choose the spot and the way it faces. The dog walks over and rests there. To let it go, press Let roam for that dog.\n\nLeashed dogs cannot be called. Unleash them first.\n\nSide sleep, Lie down, Sit and Curl up are free. Other poses can be added with points.\n\nThe instance owner can call. With Open to everyone on, anyone can.

### 2.7 裏山で使わない 5 話題 (`InfoHowTo` `InfoPetting` `InfoDogs` `InfoPhotos` `InfoShop`、対象外)

古民家の話題で、裏山のボタンは作らない (板に出ない)。内容は古民家の事実 (縁側の写真板・庭のライブ・ギフトコード・かけっこ) のままだが、表示されないので直さない。消すと古民家からの差分が広がるので残す (移植記録の方針)。

## 3. 看板 A「あそびかた」・地図・古民家ドッグランの案内板 (`guide.*`、Tier 1)

| キー | 現状 | 問題 | 改善案 (ja) | en |
|---|---|---|---|---|
| `guide.board.title` | あそびかた | — | 変えない | — |
| `guide.menu` | 手元メニュー　VR: 右スティックを下に長押し ／ Desktop: T キー ／ スマホ: 画面の［メニュー］ ／ リード台の札を押しても開きます | 開き方の正本。問題なし | 変えない | — |
| `guide.pointer` | お散歩はリード台で、洗うのは洗い場で、道は地図の看板で | 「お散歩」が札の表記 (さんぽ) と違う | さんぽはリード台、洗うのは洗い場、道は地図の看板で | Walks: the leash rack. Washing: the wash stations. Trails: the map board |
| `guide.frisbee.cap1`〜`cap4` | 「おもちゃ」で犬の［ディスク］ / 目の前にディスクが置かれる / 持つと線が出る。Use で投げる / その犬が跳んでキャッチ | 短い。問題なし | 変えない | — |
| `guide.frisbee.note1` `note2` | × が出る所へは、犬が行けません / ［まっすぐ］［曲げる］で飛び方を選ぶ。曲げると右へカーブ | 問題なし | 変えない | — |
| `guide.frisbee.more` | ディスクは犬ごとに1枚。広場にも置いてあります。\n強さ: VR は右スティック上下、Desktop はマウスホイール。\nリードでつないだ犬は取りに行きません。はずしてから。\n川ではディスクが浮き、犬が飛びこんで取ってきます。 | 4 行で情報は正しい。「置いてあります」「取ってきます」のです/ます が他のカットの体言止めと混ざる | ディスクは犬ごとに1枚。広場にもある。\n強さ: VR は右スティック上下、Desktop はマウスホイール。\nつないだ犬は取りに行かない。はずしてから。\n川に落ちると浮く。犬が飛びこんで取ってくる。 | One disc per dog. Some wait at the Plaza too.\nPower: VR right stick up/down, Desktop mouse wheel.\nLeashed dogs won't fetch. Unleash first.\nIn the river it floats, and the dog dives in for it. |
| `guide.ball.cap1` `cap2` `more` | 「おもちゃ」で犬の［ボール］ / 投げると、その犬が取ってくる / ボールかディスク、犬ごとに1つ出せます\nリードでつないだ犬は取りに行きません | `more` だけ です/ます | more: ボールかディスク、犬ごとに1つ\nつないだ犬は取りに行かない | One toy out per dog: ball or disc\nLeashed dogs won't fetch |
| `guide.shop.title` | ポイントとトレーナー | — | 変えない | — |
| `guide.shop.body` | お散歩・なでる・ボール・フリスビー・洗うは、だれでも無料。\n<ポイント>\n入口のおじいさんと話すと200pt (一度だけ)。ここにいると1分ごとに1pt。\n「かざり」「呼ぶ」「おやつ」で、飾り・なでた時の光・ポーズ・おやつと交換。\n<ばにまるトレーナー> (期間制・Credits)\n犬たちを集合させて、みんなで集合写真。古民家で入った人は、ここでもそのまま。\n入る: 手元メニュー「あそぶ」→［購入画面をひらく］\n使う: 「あそぶ」で隊形を選び［ここに集合］ | 「お散歩」の表記。「古民家で入った人は、ここでもそのまま」が意味を取りにくい。フリスビーの取り方 (トレーナー特典) が無い | さんぽ・なでる・ボール・フリスビー・洗うは、だれでも無料。\n<ポイント>\n入口のおじいさんと話すと200pt (一度だけ)。ここにいると1分ごとに1pt。\n「かざり」「呼ぶ」「おやつ」で、飾り・なでた時の光・ポーズ・おやつと交換。\n<ばにまるトレーナー> (期間制・Credits)\n犬たちを集合させて集合写真。フリスビーの取り方も切り替えられる。古民家ドッグランで入った人は、ここでも使える。\n入る: 手元メニュー「あそぶ」→［購入画面をひらく］\n使う: 「あそぶ」で隊形を選び［ここに集合］ | Walking, petting, balls, frisbee and washing are free for everyone.\n<Points>\nTalk to Grandpa at the gate for 200 pt (once). Earn 1 pt per minute here.\nTrade them in Dress, Call and Treats for accessories, petting effects, poses and treats.\n<VAnimal Trainer> (time-limited, Credits)\nGather the dogs for a group photo, and switch the Frisbee catch style. Joined at Kominka Dog Run? It works here too.\nJoin: hand menu Play → [Open the store]\nUse: in Play, pick a formation → [Gather here] |
| `guide.map.*` | 裏山の地図 / 1〜6 の凡例 / ● ○ ■ / ワープ: 手元メニュー「さんぽ」→［地図と移動］ ぐるっと一周: 1→2→3→4→5→6→1 (逆回りも) / いまここ / ↑北 | 問題なし | 変えない | — |
| `guide.kominka.body` | 門の向こうに見える古民家が「古民家ドッグラン」。\n同じ6匹の犬たちがいる、このワールドの前作です。\n<この看板を押すと、となりにポータルが出ます。>\nVR: 手を向けてトリガー ／ Desktop: クリック ／ スマホ: タップ\nもう一度押すとしまいます。1分たつと自動でしまいます。\nポイントと持ち物は、ワールドごとに別です。\nばにまるトレーナーは、どちらで入っても両方で使えます。 | Interact の操作説明 (トリガー / クリック / タップ) は VRChat の共通操作で看板に書く内容ではない。「もう一度押すとしまいます。1分たつと自動でしまいます」は 1 文にできる | 門の向こうに見える古民家が「古民家ドッグラン」。\n同じ6匹がいる、このワールドの前作です。\n<この看板を押すと、となりにポータルが出ます。>\nもう一度押すか、1分たつとしまいます。\nポイントと持ち物は、ワールドごとに別です。\nばにまるトレーナーは、どちらで入っても両方で使えます。 | The farmhouse beyond the gate is Kominka Dog Run, the earlier world with the same six dogs.\n<Press this board and a portal appears beside it.>\nPress again, or wait 1 minute, and it goes away.\nPoints and items are separate in each world.\nThe VAnimal Trainer works in both, wherever you joined. |
| `guide.kominka.interact` | ポータルを出す・しまう | — | 変えない | — |

## 4. アイテム案内の立て看板 (`guide.items.*`、Tier 1)

| キー | 現状 | 問題 | 改善案 (ja) | en |
|---|---|---|---|---|
| `title` / `subtitle` / `open` | 手元メニューでできること / 札を押すと、手元メニューがその画面でひらきます / ひらく ▶ | 問題なし | 変えない | — |
| `walk.line` | 犬をリードでつないで、行き先をえらぶ。地図と移動もここから | 問題なし | 変えない | — |
| `treat.line` | おやつを出して、手から犬にあげる | 問題なし | 変えない | — |
| `toy.line` | ボールやディスクを出して、投げて遊ぶ | 問題なし | 変えない | — |
| `call.line` | 犬を呼んで、好きな場所でポーズをとってもらう | 問題なし | 変えない | — |
| `acc.line` | かざり・撫でエフェクト・首輪とリードで、犬におめかし | (起票時: 「撫でエフェクト」は開発の語と判断) → **却下**。実装レビューで、裏山の かざり タブの中の群見出しが「撫でエフェクト」(`WordFxGroup`) と判明。看板の語がメニューの見出しと一致しているので変えない。変えるなら `WordFxGroup` と一緒に (別件) | 変えない | — |
| `trainer.name` / `trainer.line` | ばにまるトレーナー / 集合写真や、フリスビーの取り方の切り替え (Credits) | 問題なし | 変えない | — |
| `photo.name` / `photo.line` | 写真と撮影カメラ / 自動で撮れた写真を見る。撮影カメラは PC のみ | 問題なし | 変えない | — |
| `wash.name` / `wash.line` | 洗い場・おさんぽカート / 道具は洗い場の板で、カートは乗り場でえらびます | 問題なし | 変えない | — |

## 5. リード台の帯 (`walk.guide.*`) と洗い場の帯 (`wash.guide.*`、Tier 1)

どちらも ①〜④ + 一言の構成で、短い動詞・体言止め・ひらがな多めになっている (「③両手でわしゃわしゃ」「緑になったら、そのまま」)。規則 §1 に合っており、**変えない**。

| キー | 現状 | 判定 |
|---|---|---|
| `walk.guide.vr.step1`〜`sub4` | ①リードの先を取る / 持った手が、つなぐ手 / ②首輪に近づける / 緑になったら、そのまま / ③行き先をえらぶ / 「さんぽ」の右の列で / ④はずす / 自分の犬の首輪を押す | OK |
| `walk.guide.vr.help` / `flat.help` | 片手に3匹、両手で6匹まで。1匹ずつ、先を取ってつなぐ / 片手に3匹、両手で6匹まで。リードの先を首輪に近づけてもつなげます | OK (flat.help の「つなげます」は Desktop・スマホ向けの補足なので許容) |
| `walk.guide.flat.step1`〜`sub4` | ①犬のそばでメニュー / Tキー／画面のメニュー / ②「さんぽ」でつなぐ / 犬の行の左・右のボタン / ④はずす / ［はずす］か、首輪を押す | OK |
| `wash.guide.step1`〜`step4.sub` | ①泡をつける / すきな所に / ②ボトルをはなす / 両手をあける / ②スポンジを持つ / 棚から取る / ③両手でわしゃわしゃ / 泡をひろげる / ③スポンジでごしごし / Use を押したまま / ④たりなければ泡を足す / また①から | OK |
| `wash.guide.help` / `help.flat` | 泡だらけになったら、シャワーで流してタオルでふく / ボトルは Use、スポンジは Use を押したまま。最後はシャワーとタオル | OK |

## 6. ようこそ・手のヒント (`welcome.*` `hint.*`、Tier 1)

### 6.1 `welcome.body` (初回の板) と `welcome.reread.body` (設定 → インフォメーション → はじめに)

現状 (`welcome.body`):
> なにも持っていなくても — いつもの遊びは、そのまま\n・撫でる: 頭・ほっぺ・あごをなでると、犬が返事をします\n・ボールやディスクを投げる: 拾って投げると、持ってきます\n・一緒に歩く: この板の「さんぽ」から。犬をつないで、裏山を歩けます\n・そばで寝てもらう: この板の「呼ぶ」から。犬が来て、そばで寝ます\n\nこの板でできること — 過ごした時間がポイントになり、好きなものと交換できます\n・さんぽ・おやつ・かざり: 犬をつなぐ、手元に出す、犬につける。上のタブから\n・ポイントとおじいさん: 右上の「ポイント」に入手経路。門のそばのおじいさんに話しかけると、支度用のポイントがもらえます

問題: 見出しの「—」、「入手経路」「支度用」が硬い。「犬が返事をします」は意味が取りにくい (撫でると反応する)。裏山の遊び「洗う」が無い。「犬をつなぐ、手元に出す、犬につける」の並列が読みにくい。

改善案 (ja):
> なにも持っていなくても、いつもの遊びはそのまま\n・なでる: 頭・ほっぺ・あごをなでると、犬がよろこびます\n・投げる: ボールやディスクを投げると、犬が持ってきます\n・さんぽ: この板の「さんぽ」で犬をつないで、裏山を歩けます\n・そばで休んでもらう: この板の「呼ぶ」で、犬がそばに来て寝ます\n・洗う: さんぽのあとは、洗い場で洗ってあげられます\n\nこの板でできること\n・さんぽ・おやつ・かざり・呼ぶ・おもちゃ・あそぶ: 上のタブから\n・ポイント: ここで過ごすだけでたまります。右上の「ポイント」で確かめられます\n・門のそばのおじいさんに話しかけると、支度のポイントがもらえます

en:
> Nothing in hand? The usual play is all here\n- Pet: stroke the head, cheeks or chin and the dog is happy\n- Throw: throw a ball or a disc and the dog brings it back\n- Walk: leash dogs from Walk on this board and walk the hillside\n- Rest together: from Call on this board a dog comes and sleeps beside you\n- Wash: after a walk, wash the dogs at a wash station\n\nWhat this board does\n- Walk, Treats, Dress, Call, Toys, Play: the tabs above\n- Points: they add up just by being here. Check them at Points, top right\n- Talk to Grandpa by the gate for points to get ready

`welcome.reread.body` は同じ内容を `<b>` 見出しで段落にした版。改善案も同じ内容で揃える (見出し: いつもの遊びを、気軽に / なでる / 投げる / さんぽ / そばで休んでもらう / 洗う / メニューでできること / ポイントとおじいさん / メニューを手元に呼ぶには)。

### 6.2 その他

| キー | 現状 | 判定 |
|---|---|---|
| `welcome.title` / `later` / `menu` / `reread.title` / `ways` / `footer.*` / `language` | ようこそ、古民家の裏山へ / あとで / メニューを見る / はじめに / 手元に呼ぶには (端末ごと)… / 閉じたあとも、… / Language | OK (開き方の正本の 1 つ) |
| `hint.throw.*` / `hint.pet` / `hint.menu.*` / `hint.sleep` / `hint.hand.*` | 振って離すと投げられます / 頭・ほっぺ・あごをなでてみて\n両手だと大よろこび / 右スティックを下に長押しでメニュー\nさんぽ・おやつ・かざり・呼ぶ・おもちゃ・あそぶ・ライブ / … | OK。ゲームの誘導として短い。`hint.menu.*` の 7 タブの列挙は実物と一致 |

## 7. 手元メニューの説明文 (Tier 1 = 直す、Tier 2 = 案だけ)

### 7.1 Tier 1

| キー | 現状 | 問題 | 改善案 (ja) | en |
|---|---|---|---|---|
| `activities.hillside.formationHelp` | 隊形を選び、足元の印を見て「ここに集合」。印が出ない枠はそこに並べません。坂でも並びます。1 分で解散し、その場で自由行動に戻ります | 「印が出ない枠はそこに並べません」が読みにくい。「自由行動」は開発の語 | 隊形を選び、足元の印を見て「ここに集合」。印が出ない所には並べません。坂でも並びます。1 分で解散して、それぞれ遊びに戻ります | Pick a formation, check the marks at your feet, then press Gather here. No mark, no spot. Slopes are fine. They break up after 1 minute and go back to playing |
| `activities.hillside.trainerIntro` | トレーナーになると、犬たちを集合させて集合写真を撮れます。古民家で買った特典はここでも使えます。\n1 か月 500 / 3 か月 1,350 / 6 か月 2,550 / 12 か月 4,800 Credits。自動更新はなく、延長は残りの日数に足されます。 | フリスビーの取り方 (裏山の特典) が無い。「古民家で買った特典」→「古民家ドッグランで入った人」 | トレーナーになると、犬たちを集合させて集合写真を撮ったり、フリスビーの取り方を切り替えたりできます。古民家ドッグランで入った人は、ここでも使えます。\n1 か月 500 / 3 か月 1,350 / 6 か月 2,550 / 12 か月 4,800 Credits。自動更新はなく、延長は残りの日数に足されます。 | As a Trainer you can gather the dogs for a group photo and switch the Frisbee catch style. Joined at Kominka Dog Run? It works here too.\n1 month 500 / 3 months 1,350 / 6 months 2,550 / 12 months 4,800 Credits. No auto-renewal. Extensions add to the days you have left. |
| `activities.hillside.trainerActive` | 入室時に有効だったトレーナーの権利は、滞在中はそのまま使えます。集合は、その場のみんなで楽しめます。 | 「権利」が硬い | 入ったときに有効だったトレーナーは、滞在中そのまま使えます。集合は、その場のみんなで楽しめます。 | A Trainer pass that was active when you joined stays on while you are here. Everyone around can join the gathering. |
| `activities.hillside.unknown` | 結果を確認できませんでした。表示を見てもう一度どうぞ | システムの言い回し | 返事がありません。もう一度どうぞ | No answer. Try again |
| `KD_TreatPouch.WordToyHelp` | おもちゃは 1 つずつ、目の前の地面に置きます。ディスクは持つと軌道が見え、Use で飛びます | 「軌道」は開発の語 (看板 A は「線」) | おもちゃは犬ごとに 1 つ、目の前に置きます。ディスクは持つと線が出て、Use で投げられます | One toy per dog, placed in front of you. Hold a disc to see its path, then Use to throw |
| `KD_TreatPouch.WordWalkUnknown` | 結果を確認できませんでした。表示を見てもう一度どうぞ | 同上 | 返事がありません。もう一度どうぞ | No answer. Try again |
| `photo.help.body` | (7 段落。撮る場面の列挙 → 散歩の写真 → 24 枚の内訳 → 自分だけ → 持ち帰り方 → ON/OFF) | 情報は正しいが長い。「（シャッターはありません）」「犬の目線で見た景色や、自撮りのような写真」など補足が多い | §7.3 の短縮案 (5 段落) | §7.3 |

### 7.2 Tier 2 (案だけ。直す場合は別の判断)

| キー | 現状 | 問題 | 案 | 直さない理由 |
|---|---|---|---|---|
| `dogbook.help.body` | (6 文。呼び名の付け方・文字の制限・お気に入りの挙動) | 長いが内容は必要。「お気に入りにしても、その犬があなただけのものになるわけではありません」は注意書きとして自然 | 「呼び名で犬が変わることはありません。好きな名前で呼んでみてください。」の 2 文を「好きな名前で呼んでみてください。」に | DES-DOG-009 の確認待ち (板の位置・呼び名の保存) と一緒に見る |
| `information.trainer.body` / `information.vanimal.body` | 本文の 1 行目に題 (「ばにまるトレーナーについて」「VAnimalについて」) を繰り返す | ボタンに同じ題があるので重複の可能性 | 1 行目を外す | 読む面に見出しが無い構成なら 1 行目が見出しの役。Play で確認してから |
| `activities.storeNote` | トレーナーの購入は VRChat の画面で行います。入室時に有効なら、滞在中はそのまま使えます。 | 「行います」 | トレーナーは VRChat の購入画面で買います。入ったときに有効なら、滞在中そのまま使えます。 | 購入まわりは DES-UI-005 の範囲 |
| `call.pawHint` / `call.chinHint` | おすわり中、胸の前に手を差し出して少し待つとお手\n手を引くと戻ります。VR以外は近くで「お手」 | 2 行で密。許容 | — | 変えない |
| `call.lockedHint` / `purchaseHelp` | 基本の4姿勢は最初から使えます。追加動作は6犬種共通・各30pt / 一度の購入で6犬種共通。購入後に選んで使えます | 「追加動作」(芸と紛れる) | 追加のポーズは6犬種共通・各30pt | 価格表記は DES-UI-005 |
| `photo.settings.note` | (4 行。2K の注意) | 長いが設定の注意書き | — | 変えない |
| `video.pattern.note0`〜`5` ほか `video.*` | 投げて、追って戻る。じぶん → キャッチ待ち → 戻る犬に犬追従 | 「→」の連鎖で読みにくい | 文に直す | IMP-WLD-008 / 012 の撮り方の名前と種類がユーザー未確認。確定後に |
| `KD_TreatPouch.WordWalkSummaryMixed` | 行き先がまざっています (広場 {0} / 古民家側 {1}) | 行き先が 2 つだった頃の文。今は `course.walk.mixed` (行き先はそれぞれ) が先に出る | 削除候補 | 到達しない分岐のため動作に影響なし。IMP-UI-005 で未使用キーとして扱う |
| `course.travel.*` `wash.state.*` `activities.hillside.reject*` ほか状態の 1 行 | ワープを取りやめました: 犬に伝わりませんでした / 遠すぎます / … | 規則 §1 に合っている | — | 変えない |

### 7.3 `photo.help.body` の短縮案

現状 (要約): ①自動で撮る場面の列挙 ②散歩中の写真 ③24 枚の内訳と入れ替え ④自分だけ・退室で消える ⑤持ち帰り方 (PC・Quest / スマホ) ⑥自動撮影の ON/OFF。

改善案 (ja):
> 犬と遊んだり散歩したりすると、いい瞬間が自動で写真になります。ボールを持ってきたとき、なでているとき、犬どうしでじゃれたとき、フリスビーを跳んで取ったとき、集合写真のとき。散歩では、立ち寄った場所や見晴らしのいい場所でも撮ります。\n\n写真は 24 枚まで残ります (散歩 10・なでる 4・遊び 4・洗う 6)。いっぱいになると、同じ種類の古い写真から入れ替わります。新しい写真が左上で、◀ ▶ でページをめくれます。\n\n写真はあなたにだけ見えていて、ほかの人には別の写真が見えています。このワールドを出ると消えます。\n\n残したい写真は、PC・Quest では VRChat カメラを近づけると 1 枚が画面いっぱいに写るので、そのまま撮ります。スマホでは写真を大きく表示して、スクリーンショットで持ち帰れます。\n\n自動撮影は［設定］の［写真: ON / OFF］で切り替えられます。

en:
> As you play and walk with the dogs, good moments become photos on their own: when a dog brings a ball back, while you pet, when dogs play together, when a dog leaps for a frisbee, and at group photos. On walks, stops and viewpoints are captured too.\n\nUp to 24 photos are kept (walk 10, petting 4, play 4, wash 6). When full, the oldest of that kind is replaced. The newest is top left, and ◀ ▶ turns the pages.\n\nOnly you see your photos. Others see their own. They are gone when you leave this world.\n\nTo keep one: on PC and Quest, bring the VRChat camera close and the photo fills the frame, then shoot. On mobile, enlarge the photo and take a screenshot.\n\nTurn automatic photos on or off at Settings → Photos: ON / OFF.

## 8. 初回の帯 (`walkIntro.*`、Tier 1 = 変えない)

リードを取ってみよう / リードを、光った首輪へ近づけよう / つながった！ 犬について歩こう / メニューで行き先を変えられるよ / つながるのを待ってね / 近くに、いま散歩できる犬がいないよ / リードがないときは、さんぽメニューからつなげるよ / さんぽメニューをひらいてみよう / 近くの犬の「左につなぐ」か「右につなぐ」を押そう / 散歩できる犬に近づいてみよう。

判定: ゲームの誘導として揃っている。`walkIntro.note` (リードをつなぐまでの短い案内です。表示は自分だけに見えます。) も設定画面の説明として適切。**変えない**。

## 9. カートの看板 (`cart.board.*` `cart.notice` `cart.station.pr`、Tier 2 = 案だけ)

DES-UI-007 で 2026-10-14 にユーザーが HTML B 案の文面を承認済み。ここでは規則 §1 に照らした案だけを出し、**反映はユーザーの判断**。

| キー | 現状 | 問題 | 案 |
|---|---|---|---|
| `cart.notice` | 自動で移動するカートです。VR酔いを感じたら、停止して降りてください。 | 注意書きとして自然 | 変えない |
| `cart.station.pr` | 散歩のあとは、お手入れ道具で洗ってあげよう | 「散歩」→「さんぽ」。OK | さんぽのあとは、洗い場で洗ってあげよう |
| `cart.board.lead` / `lead2` | 一人ずつのカートで、一緒におさんぽできます。 / みんなで乗るときは、出発する人を一人決めてね。 | 「〜できます」と「〜てね」の混在 (板全体で です/ます と てね が交互) | 口調を「〜てね」に統一: 一人ずつのカートで、いっしょにおさんぽできるよ。 / みんなで乗るときは、出発する人を一人決めてね。 |
| `cart.board.avail.note` | 約30秒ごとに更新。空き状況は目安です。乗るときに、もう一度確認します。 | 「確認します」の主語が不明 | 約30秒ごとに更新。乗るときにもう一度調べます |
| `cart.board.join.note` | 一覧に出るのは各グループの先頭の人です。合流すると、相手の目的地へ進みます。 | OK | 変えない |
| `cart.board.row.closedNote` | 今は参加できません。一緒に走りたい場合は、先頭の人に受付を開けてもらってください。 | 「〜場合は〜してください」 | 今は受け付けていません。先頭の人に受付を開けてもらってね |
| `cart.board.row.endingNote` | 到着中のため参加できません。相手が次に出発するのを待ってください。 | 「〜のため〜できません」 | いま到着しているところ。次に出発するまで待ってね |
| `cart.board.empty.body` | 決めた一人は、下の「新しいグループで出発」へ。ほかの人は、その人の名前がここに出るまで待ってね。一人で乗るときも、下から出発できます。 | OK (長いが手順) | 変えない |
| `cart.board.start.note` | 一人で乗る方・最初の一人はこちら。別のグループで出発します。 | 「乗る方」の敬語 | 一人で乗る人・最初の一人はこちら。別のグループで出発します |
| `cart.board.start.destNote` | 犬のお散歩で選んだ目的地と、同じ行き先へ進みます。 | 「お散歩」 | 「さんぽ」で選んだ行き先へ進みます |
| `cart.board.result.*` | 新しいグループで出発します。… / グループに合流しました。… / いまは空いているカートがありません。空くまで待ってから、もう一度お試しください。 / このグループは受付を終了しました。… / この参加先を利用できません。一覧を確認して選び直してください。受付停止なら相手に受付を開けてもらってください。 | 「お試しください」「利用できません」「確認して」の連なり (特に `unavailable`) | full: いまは空いているカートがないよ。空いたらもう一度 / closed: このグループは受付を閉じたよ。先頭の人に開けてもらってね / unavailable: ここには合流できないよ。一覧から選び直してね |
| `cart.board.camera` / `camera.pc` / `rejoin.body` | 乗車中は、撮影カメラも見てみてね。 / PCでは、座席の操作パネルの「撮影モニター」から開けます。 / 一緒に走る相手を一人決め、… | OK | 変えない |

## 10. 対象外 (理由つき)

| 面 | 理由 |
|---|---|
| `information.ai.body` / `information.vanimal.body` の本文 | 制作者の声。文体の規則の対象外 (§7.2 の題の重複だけ指摘) |
| 洗い場の商品板 (`Data/WashPremium/words.json`: 「同じ種類の道具を置いてから選んでください」「すでに持っているセットがあります。自分用には買わないでください (ギフト用)」ほか) | IMP-DOG-056 で 2026-10-14 にユーザーが文面を決めた直後。案: 「同じ種類の道具を置いてから」「このセットは持っています。買うならギフト用に」。反映はユーザー判断 |
| `live.*` `KD_TreatPouch.WordLive*` パレードの文言 | 別セッション (IMP-LIV-001) が `UITranslations.json` に未コミットで作業中 |
| 商品名・ライブの曲名・`share.post` | 固有名 |
| おじいさんの古民家 5 話題 (§2.7) | 裏山では表示されない |
| `video.*` | §7.2。撮り方の名前がユーザー未確認 |

## 11. 反映の手順 (レビュー後)

1. `UITranslations.json` と `CourseUITranslations.json` を Node (UTF-8) で書き換える (ja / en 自作、11 言語 AI 訳)。`KD_ShopKeeper.cs` の const を文言表 ja と同じ文字列にする (`\n` の位置も)。
2. コンパイル (`bash scripts/unity-compile.sh --udon`) → Menu/4d → 4e、Guide/1 → 2、Guide/22 → 23、Menu/11 → 22 → 23 の順。字形の追加は各 Setup の AddGlyphs に任せ、FAIL なら Menu/2b を検討。
3. 変更したキーの一覧と Verify の結果を Issue に記録し、移植記録の裏山改変へ const の変更を追記 (BUG-UI-001 の残りも)。
4. `UITranslations.json` は別セッションの未コミット分 (`live.*`) を含むので、stage は HEAD + 本票のキーだけを合成した内容にする (`git hash-object -w` → `git update-index --cacheinfo`)。**シーンは今回 stage しない** (2026-10-15 の決定。焼き直し後の保存で別セッションの未保存オブジェクトも出たため。焼かれたラベルは Menu/11 → 4d → Guide/1 で JSON から再現できる)。シーンの部分 stage はできないので、次の担当も「他セッションの変更が混ざるなら含めない」で判断する。

## 12. 多角的ロールのレビュー結果と、採用した文 (2026-10-15)

レビューは Opus 5 本を並列 (初見の来場者 / ゲーム UX ライター / 日本語の校正 / 実装の制約 / 世界観とおじいさん)。全文はセッションの作業領域 (scratchpad、repo 外)。要点と採否:

| 指摘 (ロール) | 採否 | 反映 |
|---|---|---|
| `WordGreeting` は台帳の復元前なら はじめての人にも出る。「また来たね」の書き分けは不可 (世界観。コードで確認: Interact → WordGreeting → 付与できた時だけ Gift) | 採用 | 2.1 は中立のまま。場所の紹介を 2.2 にも入れ、2.2 は「はじめて見る顔だね」 |
| 看板 A はおじいさんの背中側 (方位差 6°、3.5 m)。「わしのうしろ」は正しい。リード台・洗い場は来場者の向きで左右が変わるので固定の目印で (世界観・来場者・実装) | 採用 | 「わしのうしろの看板に、遊び方が書いてあるよ」「洗うのは塀ぎわの洗い場」。「左」は使わない |
| 「帯」は来場者に通じず字形も未収録 (4 ロール一致) | 採用 | 「リード台に書いてあるよ」 |
| 呼ぶの権限トグルは画面上「呼ぶを全員に許可: ON」(`WordOpenOn`)。「みんなにも開放」は画面に出ない (実装) | 採用 (起票時の §0 を訂正) | InfoCall は「呼ぶを全員に許可」が ON なら、だれでも呼べる。en は Calling open to all |
| Public インスタンスにはオーナーがいないので「いなければ Master」を残す (UX・実装) | 採用 | 「呼べるのは部屋のオーナー (いなければMaster) だ」 |
| 「戻すときは」はどこへ戻すのか不明 (校正・UX) | 採用 | 「終わりにするなら、その犬の「自由にする」を押しな」 |
| 「ポーズ」と「姿勢」の混在 (UX・来場者) | 採用 | ポーズ に統一 |
| Gift の使い道に「呼ぶ」が抜け、「散歩」が表記規則に反する (校正・来場者) | 採用 | 「かざり」や「おやつ」「呼ぶ」で使いな。さんぽの支度 |
| InfoWays の「さんぽ。」だけ体言止め、「帰ったら、洗ってやる」はおじいさんが洗うとも読める (3 ロール) | 採用 | 「犬をつないで、さんぽに行く」「さんぽのあとは、洗い場で洗う」 |
| InfoWalk 「見な。」の孤立行、「片手に3匹…」は指した先と重複 (校正・UX) | 採用 | 行を組み直し、匹数は落とす。世界観の「急かさず、付き合ってやりな」を足す |
| InfoTreats 「いちばん近くの / 犬が」で名詞句が行をまたぐ (校正) | 採用 | 「放すと、いちばん近くの犬が / 食べてくれるよ」 |
| welcome の本文は Menu/11c が行の題 (撫でる / ボールやディスクを投げる / 一緒に歩く / そばで寝てもらう / この板でできること / さんぽ・おやつ・かざり / ポイントとおじいさん) を要求し、「ライブ」を禁止する (実装) | 採用 | 行の題を残して中身を直す。タブの列挙はしない。見出しは「なにも持っていなくても、すぐに遊べます」 |
| 「いつもの遊び」「この板」「支度のポイント」「入手経路」は初見に通じない (来場者・UX・世界観) | 採用 | 「ポイントがなくても、そのまま遊べます」「さんぽの支度にポイントをくれます」 |
| `acc.line` の「撫でエフェクト」は裏山の かざり タブの群見出しと一致している (実装) | 却下 (起票時の案を取り下げ) | 変えない |
| 「入る」が加入と入室の 2 義 (UX・来場者) | 採用 | 加入は「トレーナーになった人」、入室は「来たとき」。看板の「入る:」→「加入:」 |
| 「集合させて集合写真」の重なり (校正・UX) | 採用 | 「並ばせて集合写真」 |
| trainerIntro の en は 1040×84px・17pt で 3 行まで。案は 4 行 (実装) | 採用 | 2 行目を今の en の型で 1 行に |
| `unknown` の「返事がありません」は犬が返事しなかったように読める (3 ロール) | 採用 | 「うまくいったか分かりませんでした。もう一度どうぞ」 |
| `guide.kominka.body` は常体に。Interact の操作説明は消す (UX)。文が短くなり Guide/2 の CheckFit (fill ≥ 70%) が WARN になる (実装) | 採用 (WARN は許容) | 常体に書き直し |
| `guide.frisbee.more` 「川に落ちると浮く」は主語が犬に見える (UX) | 採用 | 「ディスクは川に浮く。犬が飛びこんで取ってくる」 |
| `photo.help.body` の案で「門の写真ボード」が消える (実装) | 採用 | 復活 |
| walk.guide.vr.sub2 「緑になったら、そのまま」→「そのまま待つ」(来場者) | 保留 | 帯は変えない方針。IMP-UI-002 の Play 確認で判断 |
| フリスビーの取り方を「走り込み・ひねりなど」と例示 (来場者) | 保留 | 取り方の名前は DES-UI-002 の確定待ち |
| en の personal menu (8 件) と hand menu の混在 (校正) | 別件 | 新しい文は hand menu (看板 A が正本)。残り 8 件は IMP-UI-005 |
| カートの看板・洗い場の商品板の案 (§9・§10) | 案のまま | ユーザー判断 |

### 採用した文 (ja / en。11 言語は AI 訳・校正なし。おじいさんの const も同じ ja)

- `KD_ShopKeeper.WordGreeting`
  - ja: "よく来たね。\nここは古民家の裏山だよ。\n犬たちと、のんびり\n歩いていきな。\n\nわしのうしろの看板に、\n遊び方が書いてあるよ。\n手元メニューの出し方もな。\nさんぽはリード台、\n洗うのは塀ぎわの洗い場、\n道は地図の看板を見な。\n\n聞きたいことがあれば、\n左から選んでおくれ。"
  - en: "Welcome. This is the hillside behind the old farmhouse. Take a slow walk with the dogs.\n\nThe board behind me tells you how to play, and how to open your hand menu. For walks, see the leash rack. For washing, the wash station by the wall. For the trails, the map board.\n\nIf you want to ask something, pick it on the left."
- `KD_ShopKeeper.WordGreetingGift`
  - ja: "よく来たね。\nはじめて見る顔だね。\nここは古民家の裏山だよ。\nさんぽの支度に、これを\n持っていきな。\n{0}ptだ。一度きりだよ。\n手元メニューの「かざり」や\n「おやつ」「呼ぶ」で使いな。\n\nわしのうしろの看板に、\n遊び方が書いてあるよ。\nさんぽはリード台、\n洗うのは塀ぎわの洗い場、\n道は地図の看板を見な。\n\n聞きたいことがあれば、\n左から選んでおくれ。"
  - en: "Welcome. A new face, I see. This is the hillside behind the old farmhouse. Take these to get ready for a walk: {0} pt, just this once. Spend them in Dress, Treats or Call in your hand menu.\n\nThe board behind me tells you how to play. For walks, see the leash rack. For washing, the wash station by the wall. For the trails, the map board.\n\nIf you want to ask something, pick it on the left."
- `KD_ShopKeeper.InfoWays`
  - ja: "ここでの過ごし方だよ。\n好きな順でいい。\n\n犬をつないで、さんぽに行く。\n犬をなでる。\nボールやディスクを投げる。\nおやつをあげる。\nさんぽのあとは、洗い場で洗う。\n\n山の上からは、ふもとの\n古民家が見えるよ。\nときどき振り返ってみな。\n\nぜんぶやらなくていい。\nながめているだけでも\nいいもんだよ。"
  - en: "Here is how to spend your time. Any order you like.\n\nLeash a dog and go for a walk. Pet a dog. Throw a ball or a disc. Give a treat. After the walk, wash them at the wash station.\n\nFrom up on the hill you can see the old farmhouse below. Look back now and then.\n\nYou do not have to do it all. Just watching is fine, too."
- `KD_ShopKeeper.InfoWalk`
  - ja: "つなぎ方は、リード台に\n書いてあるよ。\n\n行き先は、手元メニューの\n「さんぽ」で選べる。\n道は、地図の看板にあるよ。\nつないだ犬は、寄り道を\nしながら歩く。\n急かさず、付き合ってやりな。\n\n外せるのは、つないだ\n本人だけだよ。"
  - en: "How to leash a dog is shown at the leash rack.\n\nPick where to go in Walk in your hand menu. The trails are on the map board. Leashed dogs stop to sniff around on the way. Take your time with them.\n\nOnly the person who leashed a dog can unleash it."
- `KD_ShopKeeper.InfoTreats`
  - ja: "手元メニューの「おやつ」で\n「出す」を押しな。\nおやつが出ると、メニューは\n閉じるよ。\n\n手に持つと、犬が寄ってくる。\n放すと、いちばん近くの犬が\n食べてくれるよ。\n一度に出せるのはひとつだけ。\n\nはじめに5個持たせてある。\nなくなったら「おやつ」で\nポイントと交換しな。"
  - en: "Open Treats in your hand menu and press Give. The menu closes once the treat is out.\n\nHold it and the dogs come over. Let go, and the nearest dog eats it. One treat at a time.\n\nI have given you 5 to start with. When they run out, trade points for more in Treats."
- `KD_ShopKeeper.InfoCall`
  - ja: "手元メニューの「呼ぶ」で\n犬とポーズを選び、\n場所と向きを決める。\n犬が歩いてきて、そこで\n休んでくれるよ。\n終わりにするなら、その犬の\n「自由にする」を押しな。\n\nつないでいる犬は呼べない。\n先に外しな。\n\nごろん、ふせ、おすわり、\nうずくまるは無料だ。\nほかのポーズは、ポイントで\n増やせるよ。\n\n呼べるのは部屋のオーナー\n(いなければMaster) だ。\n「呼ぶを全員に許可」が\nONなら、だれでも呼べるよ。"
  - en: "Open Call in your hand menu, pick a dog and a pose, then choose the spot and the way it faces. The dog walks over and rests there. When you are done, press Let roam for that dog.\n\nLeashed dogs cannot be called. Unleash them first.\n\nSide sleep, Lie down, Sit and Curl up are free. Other poses can be added with points.\n\nThe instance owner (or the Master if there is none) can call. With Calling open to all on, anyone can."
- `guide.pointer`
  - ja: "さんぽはリード台で、洗うのは洗い場で、道は地図の看板で"
  - en: "Walks: see the leash racks. Washing: the wash stations. Trails: the map board"
- `guide.frisbee.more`
  - ja: "ディスクは犬ごとに1枚。広場にもある。\n強さ: VR は右スティック上下、Desktop はマウスホイール。\nつないだ犬は取りに行かない。はずしてから。\nディスクは川に浮く。犬が飛びこんで取ってくる。"
  - en: "One disc per dog. Some wait at the Plaza too.\nPower: VR right stick up/down, Desktop mouse wheel.\nLeashed dogs won't fetch. Unleash first.\nA disc floats on the river. The dog dives in for it."
- `guide.ball.more`
  - ja: "ボールかディスク、犬ごとに1つ\nつないだ犬は取りに行かない"
  - en: "One toy out per dog: ball or disc\nLeashed dogs won't fetch"
- `guide.shop.body`
  - ja: "さんぽ・なでる・ボール・フリスビー・洗うは、だれでも無料。\n<color=#B5432A>ポイント</color>\n門のそばのおじいさんと話すと200pt (一度だけ)。ここにいると1分ごとに1pt。\n「かざり」「呼ぶ」「おやつ」で、飾り・なでた時の光・ポーズ・おやつと交換。\n<color=#B5432A>ばにまるトレーナー</color> (期間制・Credits)\n犬たちを並ばせて集合写真。フリスビーの取り方も変えられる。古民家ドッグランでトレーナーになった人も、ここで使える。\n加入: 手元メニュー「あそぶ」→［購入画面をひらく］\n使う: 「あそぶ」で隊形を選び［ここに集合］"
  - en: "Walking, petting, balls, frisbee and washing are free for everyone.\n<color=#B5432A>Points</color>\nTalk to Grandpa by the gate for 200 pt (once). Earn 1 pt per minute here.\nTrade them in Dress, Call and Treats for accessories, petting effects, poses and treats.\n<color=#B5432A>VAnimal Trainer</color> (time-limited, Credits)\nLine the dogs up for a group photo, and switch the Frisbee catch style. A Trainer from Kominka Dog Run works here too.\nJoin: hand menu Play → [Open the store]\nUse: in Play, pick a formation → [Gather here]"
- `guide.kominka.body`
  - ja: "門の向こうに見える古民家が「古民家ドッグラン」。\n同じ6匹の犬たちが暮らす、このワールドの前作。\n<color=#B5432A>この看板を押すと、となりにポータルが出る。</color>\nもう一度押すか、1分たつと消える。\nポイントと持ち物は、ワールドごとに別。\nばにまるトレーナーは、どちらで加入しても両方で使える。"
  - en: "The farmhouse beyond the gate is Kominka Dog Run, the earlier world where the same six dogs live.\n<color=#B5432A>Press this board and a portal appears beside it.</color>\nPress again, or wait 1 minute, and it goes away.\nPoints and items are separate in each world.\nThe VAnimal Trainer works in both, wherever you joined."
- `welcome.body`
  - ja: "なにも持っていなくても、すぐに遊べます\n・撫でる: 頭・ほっぺ・あごをなでると、犬がよろこびます\n・ボールやディスクを投げる: 「おもちゃ」で出して投げると、犬が持ってきます\n・一緒に歩く: 「さんぽ」で犬をつないで、裏山を歩けます\n・そばで寝てもらう: 「呼ぶ」で犬が来て、そばで寝ます\n・洗う: さんぽのあとは、洗い場へ\n\nこの板でできること\n・さんぽ・おやつ・かざり: 上のタブから、犬をつないだり、おやつを出したり、かざりをつけたりできます\n・ポイントとおじいさん: ここで過ごすだけでポイントがたまり、右上の「ポイント」に出ます。門のそばのおじいさんに話しかけると、さんぽの支度にポイントをくれます"
  - en: "No points needed to start playing\n- Pet: stroke the head, cheeks or chin and the dog is happy\n- Throw a ball or disc: bring one out in Toys, throw it, and the dog brings it back\n- Walk together: leash dogs in Walk and walk the hillside\n- Nap together: in Call a dog comes and sleeps beside you\n- Wash: after a walk, head to a wash station\n\nWhat this board does\n- Walk, Treats, Dress: from the tabs above, leash dogs, bring out treats, dress a dog\n- Points and Grandpa: points add up just by being here and show at Points, top right. Talk to Grandpa by the gate and he gives you points to get ready for a walk"
- `welcome.reread.body`
  - ja: "<b>なにも持っていなくても、すぐに遊べます</b>\nポイントがなくても、そのまま遊べます。\n\n<b>撫でる</b>\n頭・ほっぺ・あごをなでると、犬がよろこびます。\n\n<b>ボールやディスクを投げる</b>\n「おもちゃ」で出して投げると、犬が持ってきます。\n\n<b>一緒に歩く</b>\n「さんぽ」で犬をつないで、裏山を歩けます。\n\n<b>そばで寝てもらう</b>\n「呼ぶ」で犬が来て、そばで寝ます。\n\n<b>洗う</b>\nさんぽのあとは、洗い場へ。\n\n<b>メニューでできること</b>\n上のタブから、犬をつないだり、おやつを出したり、かざりをつけたりできます。\nここで過ごすだけでポイントがたまり、好きなものと交換できます。\n\n<b>ポイントとおじいさん</b>\n右上の「ポイント」で、いまのポイントとためかたが分かります。\n門のそばのおじいさんに話しかけると、さんぽの支度にポイントをくれます。\n\n<b>メニューを手元に呼ぶには</b>\nVR：右スティックを下に長押し\nDesktop：T キー\nスマホ：画面の［メニュー］"
  - en: "<b>No points needed to start playing</b>\nYou can play right away without any points.\n\n<b>Pet a dog</b>\nStroke the head, cheeks or chin and the dog is happy.\n\n<b>Throw a ball or disc</b>\nBring one out in Toys and throw it. The dog brings it back.\n\n<b>Walk together</b>\nLeash dogs in Walk and walk the hillside.\n\n<b>Nap together</b>\nIn Call a dog comes and sleeps beside you.\n\n<b>Wash</b>\nAfter a walk, head to a wash station.\n\n<b>What the menu does</b>\nFrom the tabs above, leash dogs, bring out treats, or dress a dog.\nPoints add up just by being here, and you trade them for things you like.\n\n<b>Points and Grandpa</b>\nPoints, top right, shows how many you have and how to earn them.\nTalk to Grandpa by the gate and he gives you points to get ready for a walk.\n\n<b>Bring the menu to you</b>\nVR: Hold the right stick down\nDesktop: Press T\nPhone: Tap [Menu] on the screen"
- `activities.hillside.formationHelp`
  - ja: "隊形を選び、足元の印を見て「ここに集合」。印が出ない所には並べません。坂でも並びます。1 分で解散して、それぞれ遊びに戻ります"
  - en: "Pick a formation, check the marks at your feet, then press Gather here. No mark, no spot. Slopes are fine. They break up after 1 minute and go back to playing"
- `activities.hillside.trainerIntro`
  - ja: "トレーナーになると、犬たちを並ばせて集合写真を撮ったり、フリスビーの取り方を変えたりできます。古民家ドッグランでトレーナーになった人も、ここで使えます。\n1 か月 500 / 3 か月 1,350 / 6 か月 2,550 / 12 か月 4,800 Credits。自動更新はなく、延長は残りの日数に足されます。"
  - en: "As a Trainer you can line the dogs up for group photos and switch the Frisbee catch style. A Trainer from Kominka Dog Run works here too.\n500 Credits for 1 month / 1,350 for 3 / 2,550 for 6 / 4,800 for 12. No auto-renewal; extensions add to the days left."
- `activities.hillside.trainerActive`
  - ja: "このワールドに来たときにトレーナーなら、出るまでそのまま使えます。集合は、その場のみんなで楽しめます。"
  - en: "If you were a Trainer when you joined, it stays on until you leave. Everyone around can join the gathering."
- `activities.hillside.unknown`
  - ja: "うまくいったか分かりませんでした。もう一度どうぞ"
  - en: "Could not tell if that worked. Try again"
- `KD_TreatPouch.WordToyHelp`
  - ja: "おもちゃは犬ごとに 1 つ、目の前に出ます。ディスクは持つと線が出て、Use で投げられます"
  - en: "One toy per dog, placed in front of you. Hold a disc to see its path, then Use to throw"
- `KD_TreatPouch.WordWalkUnknown`
  - ja: "うまくいったか分かりませんでした。もう一度どうぞ"
  - en: "Could not tell if that worked. Try again"
- `photo.help.body`
  - ja: "犬と遊んだりさんぽしたりすると、いい瞬間が自動で写真になります。ボールを持ってきたとき、なでているとき、犬どうしでじゃれたとき、フリスビーを跳んで取ったとき、集合写真のとき。さんぽでは、立ち寄った場所や見晴らしのいい場所でも撮ります。\n\n写真は 24 枚まで残ります (さんぽ 10・なでる 4・遊び 4・洗う 6)。いっぱいになると、同じ種類の古い写真から入れ替わります。新しい写真が左上で、◀ ▶ でページをめくれます。\n\n写真はあなたにだけ見えていて、ほかの人には別の写真が見えています。このワールドを出ると消えます。\n\n残したい写真は、PC・Quest では、門の写真ボードかここの写真に VRChat カメラを近づけると 1 枚が画面いっぱいに写るので、そのまま撮ります。スマホでは写真を大きく表示して、スクリーンショットで持ち帰れます。\n\n自動撮影は［設定］の［写真: ON / OFF］で切り替えられます。"
  - en: "As you play and walk with the dogs, good moments become photos on their own: when a dog brings a ball back, while you pet, when dogs play together, when a dog leaps for a frisbee, and at group photos. On walks, stops and viewpoints are captured too.\n\nUp to 24 photos are kept (walk 10, petting 4, play 4, wash 6). When full, the oldest of that kind is replaced. The newest is top left, and ◀ ▶ turns the pages.\n\nOnly you see your photos. Others see their own. They are gone when you leave this world.\n\nTo keep one: on PC and Quest, bring the VRChat camera close to the photo board at the gate or a photo here, and it fills the frame, then shoot. On your phone, enlarge the photo and take a screenshot.\n\nTurn automatic photos on or off at Settings → Photos: ON / OFF."

## 記録

- 2026-10-15: 棚卸しを作成 (統括 Fable)。同日、5 ロールのレビューを反映し §12 に記録。反映と検証は Issue 側。
