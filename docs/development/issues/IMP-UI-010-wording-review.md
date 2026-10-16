---
id: IMP-UI-010
type: IMP
system: UI
status: review
priority: High
summary: 看板・手元メニューの説明文・おじいさんの文言を棚卸しし、AI 臭・冗長・古い事実を直した (20 キー × 13 言語 + const)。おじいさんは「どの看板を見るか」の案内へ。カートの看板・洗い場の商品板は案だけ
next: "ユーザー: PCVR でおじいさんの 4 話題と挨拶・看板 A・古民家の案内板・ようこそ・あそぶタブの文を読む。カートの看板 (棚卸し §9) と洗い場の商品板 (§10) の案を反映するか決める。シーンの commit はパレード側の区切りで"
updated: 2026-10-15
---

# IMP-UI-010 看板・メニュー・おじいさんの文言の見直し

2026-10-15 ユーザー依頼。「看板の説明文やメニューの説明文で、AI 臭のある個所・冗長な説明・ゲームらしくない文章をリストアップし、現状と改善案にまとめ、多角的ロールでレビューして問題なければ文言を変える。おじいさんの説明も古くなっているので整備する。看板で説明がある場合は、おじいさんの説明は『どの看板を見ればいいか』で OK」。

## 対象と置き場

文言の正本は `Assets/KominkaHillside/Imported/KominkaDogRun/Data/UITranslations.json` (13 言語) と `Assets/KominkaHillside/Data/Environment/Course/CourseUITranslations.json` (カート・ワープ)。おじいさんの本文は加えて `KD_ShopKeeper.cs` の const (ja の写し。文言表と 1 字でも違うとビルドガードが止める)。

| 面 | キー | 焼き直し → 検査 |
|---|---|---|
| おじいさん (4 話題 + 挨拶 2 本) | `KD_ShopKeeper.*` | Menu/4d → 4e |
| 看板 A「あそびかた」(帯 2 行・フリスビー・ボール・ポイントとトレーナー)、地図、古民家ドッグランの案内板 | `guide.*` | Guide/1 → 2 |
| アイテム案内の立て看板 (洗い場 4 か所) | `guide.items.*` | Guide/22 → 23 |
| リード台の帯 | `walk.guide.*` | Guide/11 → 12 |
| 洗い場の帯 | `wash.guide.*` | Wash/11 → 12 |
| ようこそ・手のヒント・再読 | `welcome.*` `hint.*` | Menu/11 → 22 (11c) |
| 手元メニューの説明 (あそぶ・呼ぶ・おもちゃ・写真の？・犬紹介の？) | `activities.hillside.*` `call.*` `KD_TreatPouch.Word*Help` `photo.help.body` `dogbook.help.body` | Menu/11 → 22・23・DogBook/3 |
| 散歩の案内 (初回の帯) | `walkIntro.*` | Course/Walk Intro/2 → 3 |
| カートの乗り場・残ったカートの看板 | `cart.board.*` `cart.notice` `cart.station.pr` | Cart/1 → 2 (Course/27 で字形) |

棚卸し (現状 / 問題 / 改善案、文体の規則、対象外の一覧) は **[2026-10-15 文言の棚卸し](../../plans/2026-10-15-wording-review.md)** に置く。本票は決定・レビュー記録・検証結果だけを持つ。

## 方針

- **おじいさんは「どこを見るか」の案内役**。看板・帯が説明している操作 (手元メニューの開き方・リードのつなぎ方・洗い方・道) は本人が繰り返さず、看板を指す。看板が無い話題 (おやつ・呼ぶ) だけ本人が短く説明する。左ナビの 4 話題 (過ごし方 / さんぽ / おやつ / 呼ぶ) の構成は変えない (板の構造は `KD_ShopKeeperSetupEditor` が焼く。構成の変更は別 Issue)。
- **面ごとに口調を 1 つに揃える** (規則は棚卸し §1)。看板・帯は短い動詞と体言止め、メニューの説明は です/ます で 1 文 1 操作、おじいさんは既存の「〜だよ / 〜しな / 〜おくれ」。
- 手元メニューの開き方 (VR / Desktop / スマホ) を書く場所は、看板 A の帯 (`guide.menu`)・ようこその足元 (`welcome.ways` / `welcome.footer.*`)・手のヒント (`hint.hand.*`) の 3 つに限る。おじいさんの挨拶からは外す。
- 事実の古さは 2026-10-15 のコードで照合した (行き先 6 つ、おやつ初期 5 個、ポーズ名、「自由にする」「呼ぶを全員に許可: ON」の表記、トレーナーの特典 = 集合 + フリスビーの取り方、芸は提供前なので販売説明に載せない = DES-UI-005)。起票時に「みんなにも開放」と書いたのは誤認 (画面に出ない文言)。
- ユーザーが文面を承認した直後の面 (カートの看板 = DES-UI-007 の HTML B 案 2026-10-14、洗い場の商品板 = IMP-DOG-056 2026-10-14) と制作者の声 (`information.ai.body` / `information.vanimal.body`) は**案だけ**を出し、ユーザーの判断で反映する。
- 13 言語: ja / en は自作、他 11 言語は AI 訳・校正なし (既存方針。IMP-UI-005 の検査対象)。変えたキーに en の埋め戻しを残さない。
- ハーネスは回さない。文言の変更は Edit の Setup / Verify で決まる (字形・行幅・引用した札名の照合)。

## 完了条件

- [x] 棚卸しの表に、対象の全キーが現状 / 問題の種別 / 改善案つきで載っている (対象外はその理由つき) — 2026-10-15
- [x] 多角的ロール (初見の来場者・ゲーム UX ライター・日本語の校正・実装の制約・世界観とおじいさん) のレビューで、各案が OK / 直す / 却下 のどれかになり、却下と直しが反映されている — 2026-10-15 (棚卸し §12)
- [x] 採用した案が `UITranslations.json` の 13 言語と `KD_ShopKeeper.cs` の const に入り、C# 0 / U# 0、Menu/4e・Guide/2 (文言の検査)・Menu/22・Menu/23・Menu/11c・Photo/2 が PASS — 2026-10-15 (`CourseUITranslations.json` のカートは案だけ。Guide/23・Guide/12・Wash/12 は対象キーを変えなかったので流していない)
- [x] Codex の独立レビュー (差分) の指摘を反映した — 2026-10-15 (中 2・低 4 を反映、`18641ec`)
- [x] おじいさんの const 変更を [移植記録](../imports/kominka-dog-system.md) の裏山改変へ記録した (BUG-UI-001 の移植記録は 2026-10-03 に済んでいた) — 2026-10-15
- [ ] ユーザーの Play: 看板 A・リード台・洗い場・おじいさん・ようこそ・あそぶタブの文を実際に読んで違和感がない (PCVR。Desktop / スマホの折り返しは端末別に)
- [ ] ユーザーの判断: カートの看板 (棚卸し §9) と洗い場の商品板 (§10) の案を反映するか

## 関連

- [IMP-UI-002](IMP-UI-002-guide-board-capture-instructions.md) 看板の構成 (本票は文だけ)、[DES-UI-004](DES-UI-004-walk-destinations-and-boards.md)、[2026-10-02 看板設計](../../plans/2026-10-02-guide-boards.md)、[2026-10-03 現地の案内](../../plans/2026-10-03-onsite-guides.md)
- [BUG-UI-001](BUG-UI-001-shopkeeper-walk-info-destinations.md) おじいさんの行き先 (review)。本票で InfoWalk を作り替えるので、同票の残り (移植記録への追記) も本票で済ませる
- [IMP-UI-005](IMP-UI-005-complete-thirteen-language-ui.md) 13 言語の網羅。本票で変えたキーは ja / en 自作 + 11 言語 AI 訳
- [IMP-UI-008](IMP-UI-008-credits-shop-and-item-boards.md) アイテム案内の立て看板、[DES-UI-007](DES-UI-007-cart-join-guidance.md) カートの看板 (承認済み文言)、[DES-UI-005](DES-UI-005-trainer-and-paid-items-purchase-flow.md) 販売説明の範囲

## 記録

- 2026-10-15: 起票。棚卸しを作成 (`c4fc582`)。
- 2026-10-15 (続き): 5 ロールのレビュー (Opus 並列: 来場者 / UX ライター / 校正 / 実装 / 世界観) → 指摘の採否と採用文を [棚卸し §12](../../plans/2026-10-15-wording-review.md) に記録。起票時の事実の誤り 2 件を訂正 (呼ぶの権限トグルは画面上「呼ぶを全員に許可: ON」、かざりタブの群見出しは「撫でエフェクト」なので `guide.items.acc.line` は変えない)。**反映**: 20 キー × 13 言語 (`UITranslations.json`、ja / en 自作、11 言語 AI 訳・校正なし。翻訳は Opus 3 本) と `KD_ShopKeeper.cs` の const 6 本 + コメント。`CourseUITranslations.json` (カート) は案だけで未反映。
- 2026-10-15 検証 (Edit、ハーネスなし): C# 0 / U# 0 (25 s)。Menu/11 Setup Localization (字形 2 追加: 塀・顔 → 875) → Menu/4d PASS → Menu/4e PASS (本文 6 本の手動改行が幅 440 に収まる: 338〜390) → シーン保存 → Guide/1 BUILT → Guide/2 **GUIDE_BOARDS_FAIL 4 / WARN 4** (FAIL 4 件は全部アトラス `KH_PlayGuideCuts.png.meta` の取り込み設定 = 別セッションの未コミット変更 (mipmap off・圧縮変更)。文言・札名・収まりの検査は 284 ok、fit 73 件に溢れなし。WARN は従来の 3 件 + 古民家の案内板の本文が枠の 61% (CheckFit ≥ 70%。文を短くした結果、許容)) → Menu/22 PASS (failures=0) → Menu/23 PASS → Menu/23a OVERFLOW=2 (ja / en の隊形の見出し 1040×36 が 2 行。**以前からの溢れ**で本票の文は 1 行目に影響しない。trainerIntro は収まった) → Menu/11c Verify Welcome PASS (要求語・禁止語・欠字 0) → Photo/2 PHOTO_VERIFY_PASS (fails=0)。
- **シーンはこのコミットに含めない。** 焼き直し後の保存で、別セッション (パレード、IMP-LIV-001) の未保存だったオブジェクトも一緒にディスクへ出た (差分 5 万行)。本票のシーン側の変化は焼かれたラベルの文字だけで、Menu/11 → 4d → Guide/1 を流せば JSON から再現できる。シーンの commit はパレード側の区切りに合わせる。
- Codex 独立レビュー (gpt-6-astra、差分 + 文書。7 観点): 高 0 / 中 2 / 低 4 / 別件 2。**問題なし**: const と JSON ja の一致 (最長 13〜15 全角)、13 言語の `{0}`・タグ・改行数・記号、引用した札名 (13 言語)、ja / en と現状コードの整合、§12 の採用文 40 本 = JSON。**指摘と対応** (指摘 / 対応):
  - 中: pt-br の `guide.shop.body` / `guide.kominka.body` が「ワールドに入った」とも読める / pt-br に加え、同じ曖昧さ (uniste / dołączyłeś / 加入) の es・pl・zh-cn・zh-hk も「Trainer になった人」「購入した所」と明記する文へ直した (5 言語 × 2 キー = 10 値)。他 6 言語は加入の語 (gebucht / iscritto / inscrit / оформил / 가입) で問題なし
  - 中: 棚卸し §2.6 と Issue の方針に、訂正済みの旧トグル名「みんなにも開放」が残る / §2.6 を「起票時の誤認・採用文は §12」と明記、Issue の方針を現行名へ
  - 低: 規則「1 行 ≤ 24 字」を `guide.*` の本文が満たさない / 帯・キャプションの目安とし、本文は折り返し前提の例外と規則に明記 (文は変えない。Guide/2 の実測が正)
  - 低: 移植記録の「トグル名を変更」は差分と違う (変更前から同名) / 「維持したまま説明を整理」へ
  - 低: 対象数と実変更数の区別 / 「260 値を照合、236 値を変更、24 値は既存訳のまま」と記録
  - 低: 棚卸し §11 のシーン stage 方針が Issue と逆 / §11 に今回の例外 (stage しない) と理由を追記
  - 別件: 11 言語の `WordWalkTab` = "Walk" が未訳 (IMP-UI-005 の対象。変える時は本票の引用文も)。`KD_ShopKeeper.cs` の旧コメント「仕様は隠さず全部言う」→ 古民家由来の旧方針と明記した (本コミット)
  - 指摘の反映後 (10 値) に Menu/11 (字形の追加なし) → Menu/22 PASS (failures=0) → シーン保存 → Guide/2 を流し直した (文言・札名・収まり ok、fit 溢れ 0。FAIL 4 はアトラス .meta のまま)。const == JSON ja を Node でも再確認。
- 2026-10-16 ([IMP-UI-012](IMP-UI-012-wash-store-board-game-wording.md)): ユーザー指定で古民家ドッグランの案内板の本文 (ja) を です/ます に戻した (本票の規則「看板は です/ます を使わない」の例外。棚卸し §1 に明記)。洗い場の商品板 (棚卸し §10 の案だけだった面) も同 Issue で単語中心に書き直した。
