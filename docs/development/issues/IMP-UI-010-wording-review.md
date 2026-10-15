---
id: IMP-UI-010
type: IMP
system: UI
status: wip
priority: High
summary: 看板・手元メニューの説明文・おじいさんの文言を棚卸しし、AI 臭・冗長・ゲームらしくない文と古い事実を直す。おじいさんは「どの看板を見るか」の案内へ
next: "棚卸し (plans/2026-10-15-wording-review.md) を多角的ロールでレビュー → 問題なしの案を 13 言語へ反映 → Unity の焼き直しと Verify → Codex 独立レビュー → ユーザーの Play"
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
- 事実の古さは 2026-10-15 のコードで照合した (行き先 6 つ、おやつ初期 5 個、ポーズ名、「自由にする」「みんなにも開放」の表記、トレーナーの特典 = 集合 + フリスビーの取り方、芸は提供前なので販売説明に載せない = DES-UI-005)。
- ユーザーが文面を承認した直後の面 (カートの看板 = DES-UI-007 の HTML B 案 2026-10-14、洗い場の商品板 = IMP-DOG-056 2026-10-14) と制作者の声 (`information.ai.body` / `information.vanimal.body`) は**案だけ**を出し、ユーザーの判断で反映する。
- 13 言語: ja / en は自作、他 11 言語は AI 訳・校正なし (既存方針。IMP-UI-005 の検査対象)。変えたキーに en の埋め戻しを残さない。
- ハーネスは回さない。文言の変更は Edit の Setup / Verify で決まる (字形・行幅・引用した札名の照合)。

## 完了条件

- [ ] 棚卸しの表に、対象の全キーが現状 / 問題の種別 / 改善案つきで載っている (対象外はその理由つき)
- [ ] 多角的ロール (初見の来場者・ゲーム UX ライター・日本語の校正・実装の制約・世界観とおじいさん) のレビューで、各案が OK / 直す / 却下 のどれかになり、却下と直しが反映されている
- [ ] 採用した案が `UITranslations.json` / `CourseUITranslations.json` の 13 言語と `KD_ShopKeeper.cs` の const に入り、C# 0 / U# 0、Menu/4e・Guide/2・Guide/23・Guide/12・Wash/12・Menu/22・Menu/23 (該当分) が PASS
- [ ] Codex の独立レビュー (差分) の指摘を反映した
- [ ] おじいさんの const 変更を [移植記録](../imports/kominka-dog-system.md) の裏山改変へ記録した (BUG-UI-001 の未了分も含む)
- [ ] ユーザーの Play: 看板 A・リード台・洗い場・おじいさん・ようこそ・あそぶタブの文を実際に読んで違和感がない (PCVR。Desktop / スマホの折り返しは端末別に)

## 関連

- [IMP-UI-002](IMP-UI-002-guide-board-capture-instructions.md) 看板の構成 (本票は文だけ)、[DES-UI-004](DES-UI-004-walk-destinations-and-boards.md)、[2026-10-02 看板設計](../../plans/2026-10-02-guide-boards.md)、[2026-10-03 現地の案内](../../plans/2026-10-03-onsite-guides.md)
- [BUG-UI-001](BUG-UI-001-shopkeeper-walk-info-destinations.md) おじいさんの行き先 (review)。本票で InfoWalk を作り替えるので、同票の残り (移植記録への追記) も本票で済ませる
- [IMP-UI-005](IMP-UI-005-complete-thirteen-language-ui.md) 13 言語の網羅。本票で変えたキーは ja / en 自作 + 11 言語 AI 訳
- [IMP-UI-008](IMP-UI-008-credits-shop-and-item-boards.md) アイテム案内の立て看板、[DES-UI-007](DES-UI-007-cart-join-guidance.md) カートの看板 (承認済み文言)、[DES-UI-005](DES-UI-005-trainer-and-paid-items-purchase-flow.md) 販売説明の範囲

## 記録

- 2026-10-15: 起票。棚卸しを作成。
