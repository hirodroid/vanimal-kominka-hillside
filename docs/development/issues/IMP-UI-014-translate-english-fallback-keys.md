---
id: IMP-UI-014
type: IMP
system: UI
status: review
priority: High
summary: 手元メニュー・案内・インフォメーションで英語のまま残る 148 キーを 11 言語 (ru / uk / ko / de / zh-cn / es / fr / pt-br / it / pl / zh-hk) に訳す (IMP-UI-005 の具体の 1 回目。「Walk」タブの既訳 7 キーの英字参照も置き換える)
next: "ユーザーが (1) ko / zh の welcome.title を既訳の呼び名のままにするか Latin の Kominka Hillside に揃えるかを決める (§6)、(2) 実機または Play で 1〜2 言語に切り替え、散歩・おもちゃ・あそぶ・設定・はじめに・インフォメーション・門前の案内の収まりを見る (it の Passeggiata、ru / uk のタブ札を優先)。差し戻しがあれば反映して done"
updated: 2026-10-16
---

# IMP-UI-014 英語のまま残る 148 キーを 11 言語に訳す

## 1. 依頼 (2026-10-16、原文)

ユーザー: 「Fableが司令塔となって対応お願いします。Issue登録してからお願いします。多言語対応お願いします。」

統括 Fable が司令塔。起票 → 対象の抽出 (統括、Node) → 翻訳 (Opus 4 本、言語別に 3 言語ずつ) → 併合と機械検査 (統括) → Codex 独立レビュー → Unity の焼き直し・検査 (統括。Menu/11 → 22) → コミット。

[IMP-UI-005](IMP-UI-005-complete-thirteen-language-ui.md) (todo、2026-10-08 起票) が「13 言語の未訳と翻訳表外の UI を解消する」の親票。本票はその完了条件 1 (未訳の一覧) と 2 の前半 (散歩 43 キーを含む通常文言の 13 言語化) を今日の分として実施する。完了条件 2 の後半 (欠落・英語埋め戻しを検知する検査の常設) と 3・4 (端末別の確認・運用) は本票では扱わず、IMP-UI-005 に残す。

## 2. 対象 (読み取りだけ。2026-10-16 時点の `UITranslations.json` 996 キー × 13 言語)

正本は `Assets/KominkaHillside/Imported/KominkaDogRun/Data/UITranslations.json`。`glyphs` は ja / en が `self` (共有フォント `KD_NotoSerif SDF` に焼く)、他 11 言語は `client` (VRChat クライアントのフォールバックで描く。字形の追加なし)。焼き直しは Menu/11 Setup Localization、検査は Menu/22 Verify Localization (client 言語の収まりは Editor で測れず `CLIENT_UNMEASURED` として実機確認へ回る)。

Node で「11 言語のうち 1 つ以上で en と同文」のキーを抽出 (en と同文 = 英語のまま) → 220 キー。うち固有名・記号など**正当な同文 85 キーを除外**し、翻訳対象 **135 キー**。加えて、Walk タブの訳が決まると既訳の中で英字の「Walk」がタブ名として残る **7 キー** (`welcome.body` / `welcome.reread.body` / `KD_ShopKeeper.InfoWalk` / `guide.map.note` / `walk.guide.sub3` / `walk.guide.flat.step2` / `film.granted.note`) をその言語の訳語に置き換える。翻訳担当の報告で見つかった同種の **xref 5 キー** (`guide.frisbee.cap1` / `guide.ball.cap1` / `guide.frisbee.more` / `guide.map.legend` / `walk.guide.flat.sub4` の英字のボタン名 Disc / Ball / Plaza / Farmhouse-side gate / Unleash) と、当初除外していた `activities.hillside.frisbeeVariant.0` "Normal" (ru / uk / ko / zh / it では訳が要る) を足して、内訳は **135 + walkref 7 + xref 5 + Normal 1 = 148 キー × 11 言語 = 1628 セル (対象数)**。このうち、その言語で同形の語になるセル (es の `WordWalkPlaza` "Plaza"、de の `WordToyBall` "Ball" と `WordWalkPlaza` "Plaza"、de / fr / es / pt-br の `frisbeeVariant.0` "Normal"、およびそれらを参照する xref) は同文のまま残るので、実変更数は対象数より少ない (§6 に記録)。

| 群 | キー数 | 例 (en) | 表示 |
|---|---|---|---|
| 散歩タブ (`KD_TreatPouch.WordWalk*`、`KD_ShopKeeper.WordWalk`) | 43 | Walk / Leash left / On your left hand / Mixed destinations (plaza {0} / farmhouse side {1}) | タブ名・ボタン・犬の行の状態・要約・結果行 |
| おもちゃタブ (`WordToy*`) | 19 | Ball / Disc / ready / Someone is holding it | ボタン・状態・結果行 |
| 集合とフリスビー (`activities.hillside.*`。`frisbeeVariant.0` "Normal" は当初除外したが ru / uk / ko / zh / it では訳語が要るので対象に戻した) | 32 | Gathering started / Frisbee catch style / Run-in: side | あそぶタブの結果行・選択肢 |
| おやつ・呼ぶ・かざり・ポイントの結果行 (`WordTreatBusy*` / `WordSleep*` / `WordPoints*` / `WordGrantLine` / `WordAccNotForDog` / `WordFxGroup` / `WordTreatBuyCancelled` / `WordPreparing` / `WordLive*`) | 20 | Grandpa's welcome points / The dogs are gathering. Try after it ends | 結果行・見出し |
| おじいさんのボタン (`KD_ShopKeeper.WordTreats` / `WordCall`) | 2 | Treats / Call | 門のおじいさんの話題ボタン (タブ名と同じ語にする) |
| はじめに (`welcome.title` / `welcome.footer.*`)・メニューの呼び方 (`hint.menu.*`) | 7 | Welcome to the Kominka Hillside / Hold the right stick down… | 初回の案内・ヒント |
| インフォメーション本文 (`information.vanimal.body` / `information.trainer.body`) | 2 | About VAnimal / About VAnimal Trainer | 設定 → インフォメーションのスクロール本文 |
| ライブ「紅葉が星になる」の説明 (`live.detail.hillside_dusk.meta` / `mood` / `scene`) | 3 | The plaza, from dusk into night | ライブの詳細 (題名は訳さない) |
| 設定の見出し (`settings.group.*` / `settings.volume.scope`) | 4 | What you see and hear / Read & Write | 設定ホームの群見出し |
| クレジットの分類 (`credit.environment.kit` / `credit.sky` / `credit.keeper`) | 3 | Environment\nLMArt… | 1 行目だけ訳す。アセット名は Latin のまま |
| 散歩の案内 (`walk.guide.flat.sub2`) | 1 | Leash left / right | 散歩案内の補助文 |
| Walk の既訳参照 (walkref 7 キー) | 7 | 「Walk」 → その言語の WordWalkTab | 既訳の中の英字のタブ名だけ (前置詞・引用符はその言語で自然に) |
| ボタン名の既訳参照 (xref 5 キー `guide.frisbee.cap1` / `guide.ball.cap1` / `guide.frisbee.more` / `guide.map.legend` / `walk.guide.flat.sub4`) | 5 | [Disc] / [Ball] / Plaza / Farmhouse-side gate / Unleash → その言語の WordToyDisc / WordToyBall / WordWalkPlaza / WordWalkGate / WordWalkOff | 既訳の中の英字のボタン名だけ (格変化・語間スペースはその言語で自然に) |

**除外 (正当な同文、85 キー) と理由**:
- ライブの題名 (`KD_TreatPouch.liveTitleKeys.*`、`KD_ShopItem_Live_*.displayNameKey.0`、`live.detail.*.jp`): 作品名。ワールド名を Latin に揃えたユーザー決定 (2026-10-16、IMP-UI-012 追補 2) に従い訳さない (ja だけ日本語題がある)。
- 固有名 (`guide.kominka.title` "Kominka Dog Run"、`guide.trainer.title` / `guide.items.trainer.name` "VAnimal Trainer"、`radio.about.title`): 既訳の本文でも "VAnimal Trainer" は Latin。
- 記号・数値・単位 (`ON` / `OFF` / `?` / `2K 2560×1440` / `{0}: {1}` / `s` / `↑N` / `Language / 言語`)。
- その言語で同形の語 (犬種名 Corgi / Husky / Labrador / Dachshund / Sheltie、Bandana / Donut / Tricolor / Halloween / Frisbee / Video / Volume / Menu / Reset / Auto / Manual / Standard / Trainer / Bang / Drone / Codex / Credits (de) / Information (de) / Details (de) / Photos (fr) / Points (fr) / Distance (m) (fr))。
- 裏山に無い機能 (`ui.extra.028` ラジオ設定)。

## 3. 翻訳の規則 (Opus への指示。ja / en は変えない)

1. ja を原文、en を補助に読む。既存の各言語の訳語を再利用する (タブ名 おやつ / かざり / 呼ぶ / おもちゃ / あそぶ / ライブ = `WordTabTreat` / `WordTabAcc` / `WordTabSleep` / `WordTabToy` / `WordTabPlay` / `WordLiveVolume`、設定 = `WordSettings`、インフォメーション = `information.title`、はじめに = `welcome.reread.title`)。`hint.menu.*` のタブ列挙と `welcome.footer.*` の「設定 → インフォメーション → はじめに」は、その言語の既訳をそのまま並べる。
2. `KD_ShopKeeper.WordTreats` / `WordCall` / `WordWalk` は同じ言語の `WordTabTreat` / `WordTabSleep` / `WordWalkTab` と一字一句同じにする。
3. `{0}` `{1}` は残す (Menu/11 の Load が Placeholder mismatch で止まる)。`\n` の数は en と同じ。`<color>` などのタグは保つ。
4. 文体はその言語の既訳に合わせる (ko は丁寧体、他は既訳のとおり)。固有名 ("VAnimal Trainer"、"Kominka Dog Run"、"A Cove to Unwind"、アセット名) は Latin のまま。
5. 1 行の札 (タブ名・ボタン・設定の群見出し・おじいさんのボタン) は en 以下の長さを目安にする (client 言語の収まりは Editor で測れない)。
6. 題名・固有名 (§2 の除外) は訳さない。

## 4. 完了条件

- [x] `UITranslations.json`: 148 キー × 11 言語が埋まり、対象外のセルは HEAD と同一 (Node で照合)。各言語の `hint.menu.*` にその言語のタブ名 7 語が含まれ、`KD_ShopKeeper.Word*` 3 語がタブ名と一致。Placeholder と `\n` の数が en と一致。
- [x] Codex 独立レビュー (U / X / M。訳の二重の意味・長さ・既訳との用語の一致・記号・固有名) を反映。
- [x] Menu/11 Setup Localization (字形の追加なし = client 言語は焼かない) → 保存 → Menu/22 Verify Localization PASS failures=0。`CLIENT_UNMEASURED` の件数を記録し、実機確認へ渡す。
- [ ] ユーザーが実機 (または Play) で 1〜2 言語を切り替え、散歩・おもちゃ・あそぶ・設定・はじめに・インフォメーションの収まりを見る。

## 5. 範囲外

- 未訳を検知する検査の常設と、新機能の追加時の運用 (IMP-UI-005 の完了条件 2 後半・4)。
- 端末別 (Quest / iOS) の字形確認 (IMP-UI-005 の完了条件 3、VER-WLD-001)。
- `words.json` (洗い場) / `ParadeWords.json` / `CourseUITranslations.json` は 13 言語が揃っている (IMP-UI-012 ほか)。本票は `UITranslations.json` だけ。
- 母語話者の校正 (AI 訳・校正なし。従来の 11 言語と同じ扱い)。

## 6. 記録

- 2026-10-16: 起票。対象の抽出 (Node、統括)。
- 2026-10-16 翻訳 (Opus 4 本、言語別: ru/uk/pl、ko/zh-cn/zh-hk、de/fr/it、es/pt-br。各自が patch JSON を書き、統括が Node で併合): 対象 148 キー × 11 言語 = 1628 セル、**実変更 1618 セル** (同形の語で同文のまま: de 5 = Ball / Plaza / Normal とその xref、es 3 = Plaza / Normal と frisbee.more、fr 1 = Normal、pt-br 1 = Normal)。併合の機械検査 ERRORS 0 ({n}・\n・タグが en と一致、hint.menu.* にタブ 7 語、KD_ShopKeeper.Word* 3 語 = タブ名、対象外セル = HEAD、JSON の整形は原本と同一)。
  - さんぽタブの訳 (既訳の `walkIntro.destination` に合わせた): ru Прогулка / uk Прогулянка / pl Spacer / ko 산책 / zh 散步 / de Gassi / fr Promenade / it Passeggiata / es Paseo / pt-br Passeio。
  - 広場: de は「Plaza」のまま (「Platz」は伏せの号令と重なるため)。fr Place / it Piazza (既訳のハロウィンのライブ説明に合わせた。Codex 指摘)。es Plaza (同形)。
  - **ユーザー判断待ち**: ko / zh-cn / zh-hk の `welcome.title` は既訳の呼び名「옛 고택의 뒷산 / 老宅的后山 / 老宅的後山」(おじいさんの台詞・地図・犬図鑑の題と同じ) にした。他 8 言語とインフォメーション本文は Latin の "Kominka Hillside"。ワールド名を Latin に揃える決定 (IMP-UI-012 追補 2) に合わせるなら 3 セルを "Kominka Hillside에 오신 것을 환영해요 / 欢迎来到 Kominka Hillside / 歡迎來到 Kominka Hillside" に差し替える。
- 2026-10-16 Codex 独立レビュー (gpt-6-astra、読み取りのみ。U / X / M、7 観点): 高 0 / 中 6 / 低 3 / 別件 3。**問題なし**: 記号・書式・数の表記、拒否理由の区別、walkref / xref の変更範囲、固有名の保持、ja / en と対象外セルの不変。**反映** (全部): es / pt-br の `WordWalkGate`「Entrada de la casa」は玄関と読める → 「Entrada junto a la casa / Entrada junto à casa」(凡例も) / ko `WordToyStateHeld`「사용 중」→「들고 있음」/ pl `live.detail.hillside_dusk.scene`「Na szczycie」(頂上) → 「W kulminacji utworu」/ pl「w Spacer」→「w zakładce Spacer」、ru / uk「в «Прогулка»」→「во вкладке «Прогулка»」「у вкладці «Прогулянка»」(welcome.body / reread.body) / fr / it の Plaza → Place / Piazza (18 セル) / Issue §2 の群別件数と 148 の内訳 (135 + walkref 7 + xref 5 + Normal 1) を訂正。**実機確認へ**: it「Passeggiata」(11 字) のタブ札。**別件候補 (既訳側の揺れ、本票では直さない)**: 集合の語 (ko 모이기 / 집합、de Sammeln / Versammeln)、uk の フリスビー 3 表記 (фризбі / фрисбі / фрісбі)、ko / zh の Trainer (Latin) と 트레이너 / 训练员 の併存、uk `WordSleepOwnerOnly` の «Налаштуваннях» と `WordSettings` «Параметри»、zh-hk の書き言葉と `CourseUITranslations.json` の広東語口語の混在。
- 2026-10-16 Unity (Edit、ハーネスなし。C# の変更なし): シーンは別セッションの未保存 (dirty) 状態だったので先に保存 (14:38 → 15:50 の保存は相手の作業中の状態をディスクへ写しただけ) → Menu/11 Setup Localization (setup.txt 15:50:41。**font atlas 収録 888 + 追加 0**、ko の clientGlyphs 584 → 597 は端末側) → 保存 → Menu/22 Verify Localization **RESULT PASS failures=0** (PASS 13308、fixed bundled-language glyphs missing=0、INCOMPLETE_LANGUAGES なし、CLIENT_UNMEASURED 3971 = 11 言語の札の収まりは実機確認、AUTOSIZE_SHRINK 216 / FIT_WARNINGS 10 は ja / en の従来分)。フォントアセットに差分なし。**シーンは stage しない** (IMP-UI-010 / 012 と同じ: 別セッションのトレーナー看板ほかの未コミット分が同じファイルに乗っている。本票のシーン側の変化は KD_Language の 11 言語の配列だけで、Menu/11 で JSON から再現できる)。→ review
- 2026-10-16 変えた文言が載る他の面の Edit 検査 (コミット `59ab1d8` 後、シーン dirty 無しで実行。報告ファイルの更新時刻で実行を確認): Guide/2 Verify Guide Boards = **FAIL 4 / WARN 3** (FAIL 4 は `KH_PlayGuideCuts.png.meta` のアトラス取り込み設定 = 別セッションの未コミット、10-15 から不変。WARN 3 も従来分: Asobikata の地形 0.60 m、Paper_Shop の余白 141 mm、`guide.cut.throw.label` の es の概算溢れ。本票で変えたキーの新しい FAIL / WARN なし) / Course/81 Verify Travel Spots = **FAIL 13** (全部 別セッションの筆ペン・動画の UI フォント。labelFit に FAIL なし = 13 言語の `WordWalkGate` / `WordWalkPlaza` の新しい訳は地図の札に収まる概算) / Cart/2 Verify Carts = **CART_OK ng=0 pass=3652**。いずれも従来の基準値と同じで、本票由来の新しい失敗はない。
