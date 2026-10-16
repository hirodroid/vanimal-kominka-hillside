---
id: IMP-UI-013
type: IMP
system: UI
status: review
priority: High
summary: 門前に「ばにまるトレーナー」専用の看板を 1 枚建てる (メインのサブスクリプション)。3 ワールドの特典、ひとやすみの入り江の宣伝 (写真・入り江へのポータル・URL 欄)、加入 / ギフトのボタン。13 言語
next: "ユーザーが実機で見る: 板の位置と向き (案内板の南西 4.1 m)、キャッチ・導入・応援文・価格の文、[料金・購入はこちら] が購入画面を開く・加入者には [ギフト]、[トレーナー特典を見る] のポップアップ、URL 欄のコピー (Quest / iOS も)、13 言語の収まり、ボタンの高さ。浜辺で犬と写した写真を 1 枚もらって差し替える。差し戻しがあれば反映"
updated: 2026-10-16
---

# IMP-UI-013 ばにまるトレーナーの看板 (入り江の宣伝を含む)

## 1. 依頼 (2026-10-16、原文)

- 「一休みの入り江はがっつり宣伝したいのだけど。別ボードをポップアップしてもいいから。」+ 入り江の告知文 (Discord 向け。§2 に写し)
- 「バニマルトレーナー用の看板を一つつくるか。これ、メインのサブスクリプションなので。」

統括 Fable が司令塔。起票 → Codex 相談 → 実装 (Opus) → Unity の構築・検査 (統括) → Codex 独立レビュー → コミット。前段 ([IMP-UI-012](IMP-UI-012-wash-store-board-game-wording.md) §6) で、トレーナーが 3 ワールド共通で入り江では扉を開く特典だと確認し、看板 A 4 枚目と古民家ドッグランの案内板へ 1 文ずつ追記した。本票はそれを「専用の看板 1 枚」に格上げする。

## 2. 入り江の告知文 (ユーザー提供 2026-10-16。看板の文言の元。原文は Discord 向け)

> 「ひとやすみの入り江」は、自分の犬とゆっくり過ごせる、夜のプライベートビーチです。焚き火のそばで休んだり、浜辺でボール遊びをしたり、テントで寝転んで星を眺めたり。犬を洗える場所も用意しました。
> 皆さんがインスタンスに入ると、ビーチへの扉が開きます。会員でないお友達も、一緒に入って、それぞれの犬と過ごせます。今のトレーナー特典に含まれるので、追加購入は不要です。
> ひとりでひと息つきたい時も、お友達とのんびりしたい時も。ぜひ、気軽に使ってもらえたら嬉しいです🐾
> メンテナンスの都合上、ワールドは非公開 (Private) のまま、URL からご利用いただく形にしています。URL の共有や、Friends+ などお好きな形式でのインスタンス作成は自由です。
> https://vrchat.com/home/world/wrld_266a7eef-4edd-479f-a76b-dd64e09b41af/info
> URL は Discord でもどなたでも確認できます。グループ掲載のリンクからぜひご参加ください！
> 定員は 6 名です。2 名用などのご要望は Discord のチケットへ。できる範囲で対応しますが、システム上、6 名を超える増員はできません。

## 3. 設計 (統括の推奨。Codex 相談で確認してから実装)

### 3.1 置き場所と作り

- **門前、古民家ドッグランの案内板 (`KH_GuideBoards/Kominka`) の隣**に、同じ作りの立て看板 `KH_GuideBoards/Trainer` を 1 枚 (木の板 `KH_RiverWood`、WorldSpace Canvas、`KD_NotoSerif SDF`)。案内板と同じ向き (門を背に読む)。位置は Guide/1 が案内板の `FindKominkaPose` と同じ型で物理探索 (案内板の東 / 西 1.9 m を起点、門・案内板・ポータル位置と重ならない、通路を塞がない)。一度置いた位置は人が動かしたものとして守る (他の板と同じ)。
- Canvas 1300 × 1300 (案内板は 1300 × 1100。写真と 3 段の特典が要るので縦を足す)。板の下端 0.80 m は案内板と同じ。
- ポップアップではなく**常設**にする (「がっつり宣伝」。門前で最初に目に入る)。ユーザー案の「別ボードをポップアップ」は、常設で収まらない時の代案。

### 3.2 面の中身 (上から)

1. **見出し**「ばにまるトレーナー」+ 副題「3 つのワールドで使える、月額の会員証」
2. **左: 入り江の写真** (RawImage、入り江 repo の `SourceArt/Cove/review/` の最新回 (r21) から夜の浜辺・焚き火が分かる 1 枚を選び、`Assets/KominkaHillside/Textures/Guide/KH_CoveBeach.png` へ 1024 幅以下でコピー。派生物として裏山側で管理。選定は統括が候補を見て決め、ユーザーが差し替えられる)
3. **右: 特典** (ワールド別の 3 行。各 1〜2 文):
   - 古民家の裏山 (ここ): 犬たちを並ばせて集合写真。フリスビーの取り方を変える。
   - 古民家ドッグラン: 集合写真。みんなで遊ぶ (かけっこ・2 匹の遊び) の開催。← 古民家の `experience.formation` / `experience.hosting_play` の読み。実装前に古民家の仕様で照合する
   - ひとやすみの入り江: トレーナーがいると入口の扉が開く。会員でない友達も一緒に入れる。
   - 芸 (バーン・ジャンプ) は提供前なので載せない (DES-UI-005 2026-10-13 決定)。
4. **下: 入り江の紹介** (告知文の要約、です/ます):
   「ひとやすみの入り江」は、自分の犬とゆっくり過ごせる、夜のプライベートビーチです。焚き火のそば、浜辺のボール遊び、テントで星見、犬の洗い場。トレーナーがインスタンスにいると扉が開き、会員でないお友達も一緒に入れます。追加購入は不要。定員は 6 名。ワールドは非公開 (URL から)。
5. **状態行**: 「加入中 (ここでも入り江でも使えます)」/「未加入」/「確認中…」(`KH_TrainerPass._Loaded` / `_OwnedBy(LocalPlayer)`。手元メニュー「あそぶ」と同じ判定)
6. **ボタン 3 つ** (Interact ではなく Canvas の Button。板の下段):
   - [加入する] = `KH_TrainerPass._OpenStore()` (`Store.OpenListing`、古民家と同じ Listing `prod_81a5ac65-…`)。加入中はラベル「ギフト」で同じ Listing を開く (購入画面側でギフトを選ぶ。古民家 IMP-PRG-012 と同じ入口、1 回押し。洗い場の 2 段の注意は「同じセットを二重に買う」危険のためで、サブスクの画面は自分の加入状態を見せるので付けない)
   - [入り江のポータルを出す] = 2 つ目の `VRCPortalMarker` (roomId `wrld_266a7eef-4edd-479f-a76b-dd64e09b41af`) を板の脇に 60 秒出す。`KH_PortalBoard` と同じローカル処理 (`KH_PortalBoard` を Button から呼ぶ形に小改修、または同じ型の `KH_CovePortal`)。**非公開ワールドへのポータルが他人にも通るか**は実機で確かめる (通らなければ URL 欄だけ残す)
   - URL 欄 (`KD_MenuFinishEditor.CopyField`、0.60 × 0.044 m、ポスターの URL 欄と同じ): `https://vrchat.com/home/world/wrld_266a7eef-4edd-479f-a76b-dd64e09b41af/info`。「URL は Discord でも」はグループポスター (塀) に任せ、ここには書かない。
7. **価格は載せない** (DES-UI-005 2026-10-03「看板に価格を載せないままで OK」を踏襲。購入画面が正)。「月額」だけ書く。

### 3.3 文言と配線

- キー `guide.trainer.*` (title / subtitle / perks.hillside / perks.kominka / perks.cove / cove.body / state.on / state.off / state.loading / join / gift / portal / url.label) を `UITranslations.json` に 13 言語で追加 (ja / en 統括、11 言語 Opus。看板の規則: ja は体言止め + 入り江の紹介だけ です/ます (案内板と同じ例外) → Codex に相談)。`UILocalizationBindings.json` に labels / interactions を追加。
- Guide/1 が建てる・Guide/2 が検査する (`StaticLabels` に追加、`markers.Length != 1` の前提を「古民家 1 + 入り江 1」に広げる、新しい板の位置規則、ボタンの配線、写真の import 設定)。Menu/11 → 22 で文言。
- 看板 A 4 枚目のトレーナーの段は当面そのまま (重複は次の文言見直しで「くわしくは隣の看板へ」に縮める候補)。

### 3.4 Codex 相談後の契約 (2026-10-16。gpt-6-astra、読み取りのみ。8 問の結論を反映)

- **2 面構成**: 常設面 (見出し・副題・大きな写真・特典 3 行・入り江の導入 2〜3 文・状態行・ボタン 3 つ) + **詳細ポップアップ** (遊びの列挙・定員・非公開と URL の説明・URL 欄・[とじる])。1 面に全文を詰めない (de / fr / pl / ru の折り返し)。写真は常設面で大きく。
- **操作は Canvas の Button** (onClick → backing Udon の `SendCustomEvent`。洗い場の商品板と同じ経路)。操作用 Canvas (`Actions` / `Details`) だけ VRCUiShape + GraphicRaycaster + BoxCollider を持ち、通常の板の Canvas には付けない (EnsureCanvas / Verify を分岐)。
- **入り江のポータルはシーン直下** `KH_CovePortal` (VRCPortalMarker、roomId `wrld_266a7eef-4edd-479f-a76b-dd64e09b41af`、保存時は非活性)。Guide/1 はマーカーを roomId で識別 (古民家 1・入り江 1)、「ちょうど 1 つ」の前提を置き換える。非公開ワールドへのポータルが他人にも通るかは実機 (通らなければ URL 欄だけへ切替)。
- **加入中は [ギフト] 1 回押し** + 注記「購入画面でギフトを選べます」(Listing を開くだけで購入確定ではない。自分用の延長もある画面なので注記で補う)。
- **価格は載せない**。副題は「3つのワールドで使える、期間制の会員証」(手元メニューの「自動更新なし・延長」と整合。「月 500 C から」は 12 か月換算 400 と食い違うので書かない)。
- **文体**: 見出し・特典は体言止め、入り江の導入・詳細は です/ます (案内板の例外と同じ)。「追加購入は不要です」「定員は6名です」も です/ます に揃える。
- **階層**: `KH_GuideBoards/Trainer/Canvas` (Header / Subtitle / Photo / Paper/Perks / Paper/Cove)、`Trainer/Actions` (Status / StoreButton / GiftNote / PortalButton / DetailsButton)、`Trainer/Details` (Paper / Body / UrlCaption / UrlField / CloseButton)。
- **キー** `guide.trainer.*`: title / subtitle / perks / cove.lead / cove.body / state.loading / state.off / state.on / state.unconfigured / join / gift / giftNote / portal / details.open / details.close / url.caption (16、13 言語)。静的な 9 つは bindings (labels)、動的な 7 つ (state.* / join / gift / giftNote) は実行時に `KH_TrainerBoard` が KD_Language から引く。
- **実行時** `KH_TrainerBoard` (ローカル、`_OpenStore` / `_TogglePortal` (60 秒) / `_OpenDetails` / `_CloseDetails`、tick で `KH_TrainerPass._Loaded / _OwnedBy` と言語を見て 準備中 / 確認中 / 未加入 / 加入中 を描く)。
- **分担**: Opus A = Editor (Guide/1 の板・位置探索・ポータル・配線・写真の Importer・Guide/2 の検査) + `KH_TrainerBoard.cs`、Opus B = 翻訳 16 キー × 13 言語 + bindings。写真の選定・Unity 操作・保存・コミットは統括。
- **検査の読み方**: Guide/2 の既存 FAIL 4 (アトラス .meta、別セッション) は項目で前後比較し、新規の失敗と分けて報告する。「全体 PASS」とは言わない。
- **写真**: `SourceArt/Cove/review/detail/r11/beach_night_r11.png` (Blender の描画、1400 × 1100。テント・焚き火・海が 1 枚に入る) を `Assets/KominkaHillside/Textures/Guide/KH_CoveBeach.png` へコピー済み (統括。候補 4 枚を見て選定: cave_night_exit は月と海、tent_hero は単体、stargazing は空だけ)。

## 4. 既定 (ユーザーが実機で直す前提で統括が決める)

| 項目 | 既定 | 代案 |
|---|---|---|
| 位置 | 案内板の隣 (物理探索の最初に通る側) | 看板 A の隣、広場側 |
| 写真 | r21 の夜の浜辺 1 枚 | ユーザー指定の画像 / 写真なし |
| 価格 | 載せない | 「月 500 C から」 |
| ポータル | 出す (60 秒) + URL 欄 | URL 欄だけ |
| ギフト | 加入中は [ギフト] 1 回押し | 洗い場と同じ 2 段 |

## 5. 範囲外

- 入り江側の看板・古民家側の看板 (別 repo)。
- トレーナーの価格・特典の変更。芸の案内 (提供後)。
- 手元メニューの変更。

## 6. 完了条件

- [ ] Guide/1 が `Trainer` 板を建て、Guide/2 が PASS (板・ボタン・ポータル 2 つ・URL 欄・写真・13 言語の収まり)。Menu/22 PASS。
- [ ] 状態行が加入 / 未加入 / 確認中で切り替わる (Edit の検査は `KH_TrainerPass` の値を差し替えて照合。実課金は実機)。
- [ ] Codex 独立レビュー反映、コミット (シーンは別セッションの差分の扱いに従う)。
- [ ] ユーザーが実機で見る: 位置と向き、写真、文、[加入する] が購入画面を開く、加入者には [ギフト]、入り江のポータルが他人にも通るか (通らなければ URL 欄だけに)、13 言語の収まり。

## 7. 記録

- 2026-10-16: 起票 (ユーザー依頼 2 件 + 告知文)。§3.1〜3.3 は統括の推奨、Codex 相談で §3.4 の契約に更新 (2 面構成・Button・ポータルはシーン直下・価格なし・ギフト 1 回押し + 注記)。Opus A / B を並列で起動。
- 2026-10-16 実装 (統括 Fable、Opus A = `KH_TrainerBoard.cs` + `KH_GuideBoardsEditor.Trainer.cs` / `.TrainerVerify.cs` + `KH_GuideBoardsEditor.cs` / `.Verify.cs` の変更 (Boards / StaticLabels / Ctx / ポータルを roomId で識別 / Guide/0 に Program Asset)、Opus B = `UITranslations.json` 16 キー × 13 言語 + `UILocalizationBindings.json` 9 件)。A の契約からの逸脱 (採用): 位置の探索は案内板の北東 2.3 / 2.7 m (写真ボード・塀のポスターの読者域に掛かるので棄却) → 南西 4.1 / 4.5 m (案内板のポータルの向こう); `KH_CovePortal` はシーン直下の非活性アンカー + 活性のマーカー (古民家と同じ型、SDK の VRCPortalMarker.prefab から InstantiatePrefab、roomId だけ書く); 語キーと ja の既定は `public const`; `Actions/Plate` (背景の帯) を追加; `CheckWords` は `guide.trainer.*` を除外 (入り江の名を引用するため)。B の逸脱 (採用): fr / ru / uk は隣の看板に合わせて親しい口調、pt-br の join は「Assinar」、ko / zh の短い札は「비치 / 海湾 / 海灣」の一般名 (正式名は特典・導入に残る)。
- 2026-10-16 検証 (Edit、ハーネスなし。Play 中でないことを先に確認): C# 2 回目で 0 (1 回目の CS0103 13 件は新規 partial の取り込み前の既知の罠、`scriptCompilationFailed` False・dll 更新で確定) → Menu/11 は「Trainer/Canvas/Header が無い」で停止 (配線表が板より先。**順序は Guide/1 → Menu/11**) → Guide/0 (`KH_TrainerBoard.asset`) → U# 0 → Guide/1 **BUILT** (Trainer (44.39, 1.50, 37.39) yaw 154、`KH_CovePortal` (42.83, 1.51, 36.59) yaw 334、写真の Importer を 1024 / ASTC に、ボタン 4 つの listener、tray mesh 964 頂点) → Guide/1 内 Verify: **新規 FAIL 0、新規 WARN 1** (Details/Body ja の fill 46 %、許容)、Trainer の OK 194 行。既存 FAIL 4 (アトラス .meta、別セッション) は不変 → Menu/11 (字形 5 追加: 達焚浜非式 → フォントアセットをコミットに含める) → Menu/22 PASS failures=0 → Guide/3 で撮影 (Temp/kh-guide-boards/shots/Trainer_*.png、統括が目視: 見出し・副題・写真・特典・導入・状態行「確認中…」・ボタン 3 つ。紙の下半分は空白 = Details の面) → Course/80 (keepOuts 15) → Course/81 **FAIL 13 は全部 別セッション** (VideoPage/UrlInput と 筆ペンの UI の既定フォント)、Trainer / Cove への指摘なし → Build Size/8 の MISSING 16 は別セッションのアイコン・Props (写真は対象外、Importer は Guide/1 の設定のまま) → 保存 (dirty 無し)。
- 2026-10-16 Codex 独立レビュー (gpt-6-astra、読み取りのみ。差分 + 3 ロール U / D / M、6 観点): 高 0 / 中 2 / 低 2 / 別件 1。**問題なし**: 実行時 (Udon で使えない API なし、`_` 前置、tick の増殖なし、ポータルの timeout 守り、状態・言語・購入後の更新、null 守り)、ビルダーの再実行 (名前・roomId で再利用、位置保持、Importer は差分時だけ、古民家の roomId・アトラス .meta を書かない)、既存検査の緩和は Trainer の操作 Canvas 本体だけ、状態 4 × ja/en は実行時と同じ `_StateFrom` / `_Render`、翻訳・配線 (16 × 13、改行、ボタン長)、§3.4 と実装の一致、来訪者視点。**指摘と対応** (全部反映):
  - 中: 写真の Importer を先に書くので後段の例外で設定が残る / Importer の適用を板の構築後 (字形・Configure・Bind の直前) へ移し、変更前の値を記録して catch で復元。U# の準備 (Program Asset が .cs を指す)・Importer の存在・プレハブの解決を書き込み前の検査へ。新規オブジェクトに Undo
  - 中: 古民家ポータルがプレハブでない時の `Object.Instantiate` (シーンの複製) の抜け道 / SDK の VRCPortalMarker.prefab を先に解決し (無ければ古民家マーカーのプレハブ元)、常に `InstantiatePrefab`。見つからなければ書き込み前に中止。Verify はプレハブ同一性を常に検査
  - 低: `Canvas/Photo` の RawImage が無いと写真の検査を飛ばす / RawImage の存在・有効・texture・alpha ≥ 0.999・activeInHierarchy を必須の FAIL に
  - 低: `cove.body` 末尾「URL はグループのページからも見られます」は原文 (Discord) と違う / 13 言語から 1 文を削除
  - 別件: Guide/2 の既存 FAIL 4 と Course/81 の FAIL 13 は項目で前後比較する (総数から引かない)
  - 反映後: C# 0 (scriptCompilationFailed False) → U# 0 → Guide/1 BUILT (Trainer の位置を保持 (44.39, 1.50, 37.39)) → Verify 新規 FAIL 0 / WARN 1 (既存 FAIL 4・WARN 3 は不変) → Menu/11 → Menu/22 PASS failures=0 → 保存 (dirty 無し)。→ review
- 残る実機確認: 位置と向き (案内板の南西 4.1 m、写真ボード・塀のポスターを避けた結果)、非公開ワールドへのポータルが他人に通るか、加入状態の切替と購入画面、Details の重なり (Quest)、13 言語の収まり (de/es/fr/pt-br のポータル札 16 字、[入り江について] は 2 行)、ボタンの高さ (中心 0.89 m)。A の指摘: 写真 (1400 × 1100、NPOT、mipmap) は Build Size/7 を流すと設定が変わり Guide/2 と食い違う可能性 → Build Size/7 を流す時にこのファイルの扱いを決める (今回の Build Size/8 では未指摘)。

## 8. 決定: 入り江へのポータルは置かない。URL のコピー欄を表の面へ (2026-10-16 ユーザー指示「ポータルはパブリックになってしまうため公開していないワールドはポータル開けないのでは。その場合は torinosuke Booth と同じく URL をコピペできるようにしたい」)

SDK の仕様 (2026-10-16 に確認): [VRC Portal Marker](https://creators.vrchat.com/worlds/components/vrc_portalmarker/) は World ID を指定すると「そのワールドの既存の Public インスタンスへ、無ければ新しいインスタンスへ」繋ぐ。非公開 (Private) ワールドはクライアント・サイトから Public インスタンスを作れない ([VRChat feedback: Private Worlds can have public instances via API](https://feedback.vrchat.com/bug-reports/p/private-worlds-can-have-public-instances-via-api) — API 経由だけの抜け穴として報告されている = 仕様上は不可)。インスタンス種別 (Invite / Friends+ / Group) を指定できる改良は [要望](https://feedback.vrchat.com/feature-requests/p/add-ability-specifying-world-instance-and-accessibility-to-vrcportalmarker) のまま (2023 に tracked、未実装)。公式の仕様 (Public インスタンスへ接続) と利用者の報告 (非公開ワールドの Public インスタンスはクライアントから作れない) は別の根拠で、公式に「非公開ワールドへのポータルは不可」と明記した文は無い。仕組み上 Public インスタンスに依存し、入り江は非公開のままにする方針 (§2) なので、**本用途ではポータルを採用しない**と判断 (実機で試して通る可能性を否定するものではない)。

変更: `KH_CovePortal` と [入り江のポータル] を外し (Guide/1 が削除、Guide/2 は残っていれば FAIL)、URL のコピー欄 (塀の torinosuke ポスターと同じ `CopyField`。URL が 76 字と長いので幅 0.94 × 0.044 m、1 行に収まる pt を 28 → 22 で自動選択) を詳細ポップアップから**表の面の操作帯 (2 段目)** へ移す。キー `guide.trainer.portal` を 13 言語から削除、bindings は `Trainer/Actions/UrlCaption`。詳細ポップアップは本文 + [とじる] だけ残す。

## 9. 決定: 文言と面の構成を「欲しくなる理由 → 納得 → ひと押し」に (2026-10-16 ユーザー指示「文言も見直しお願いします」+ 文案 (別 AI の提案を貼付))

採用した方針: 上にキャッチコピー、中央に入り江の写真と導入、下に特典・応援文・価格・購入への案内。ボタンは押した先が分かる名前。価格は載せる (§3.4 の「価格なし」を上書き。手元メニューと同じ数字)。

| キー | 新 ja | 置き場 |
|---|---|---|
| subtitle | 犬との時間を、もっと。 | 見出し下 |
| cove.lead | 自分の犬と、夜のプライベートビーチへ。焚き火のそばでくつろいだり、浜辺で遊んだり。「ひとやすみの入り江」は、トレーナーがいる間は扉が開き、会員でないお友達も一緒に入れます。 | 写真の横 |
| support (新) | ここでの時間を気に入ってもらえたら、ばにまるトレーナーになっていただけるとうれしいです。犬たちとの遊びや、過ごせる場所を増やしていく励みになります。 | 紙の下段 |
| price (新) | 1か月 500 Credits (3か月 1,350 / 6か月 2,550 / 12か月 4,800)。自動更新はありません。 | 応援文の下 |
| join | 料金・購入はこちら | 操作帯 |
| details.open | トレーナー特典を見る | 操作帯 |
| perks | 古民家の裏山: 犬たちを並ばせて集合写真。フリスビーの取り方を変える / 古民家ドッグラン: 集合写真。みんなで遊ぶ (かけっこなど) を開ける / ひとやすみの入り江: トレーナーがいる間は扉が開き、お友達も一緒に入れる | 詳細ポップアップ |
| cove.body | 「ひとやすみの入り江」は定員6名です。ワールドは非公開 (Private) のまま、URLから入ります。URLの共有や、Friends+ などお好きな形式でのインスタンス作成は自由です。 | 詳細ポップアップ |

文案からの差: 「古民家ドッグラン・古民家の裏山でも、犬たちとの集合写真など、もっと楽しめる特典をご用意しています」の段は、特典の中身 (3 行) を詳細ポップアップ「トレーナー特典を見る」へ移して表の面は応援文と価格にした (表の面の文字量を抑えるため)。写真: 「犬とここで過ごせる」が分かる 1 枚は入り江 repo の review 画像に無い (犬が写るのは入口前 `r14/pc/dogs.png` だけで UI 文字入り) → 当面は現在の描画のまま。**ユーザーに浜辺で犬と写した 1 枚を依頼**。
- 2026-10-16 §8・§9 の実装 (Opus A: ポータル削除・URL 欄を操作帯 2 段目 (940 × 44 mm、76 字が 1 行に収まる pt を 28 → 22 で自動選択)・面の再構成 (キャッチは太字 52 pt、紙に 導入 / 応援文 / 価格、詳細に 特典 3 行 + 注意)、Opus B: 17 キー × 13 言語 (`guide.trainer.portal` 削除、support / price 追加、6 キー書き直し。桁区切りは各言語の既存慣習: de/it/pt-br 1.350、es/fr/ru/uk/pl 1350、他 1,350)、bindings 10 件)。検証 (Edit): C# 0 / U# 0 → 保存 (別セッションの dirty 分を含む) → Guide/1 BUILT (位置保持。削除: KH_CovePortal、Actions/PortalButton、Details/UrlCaption・UrlField、Canvas/Paper/Perks。tray mesh 802 頂点。字形 1 追加 → フォントアセットを含める) → Verify 新規 FAIL 0 / WARN 1 (Details/Body ja fill 59 %) → Menu/11 → 22 PASS → Course/80 (keepOuts 15 → 14、入り江ポータルの分が消えた) → 81 (FAIL 13 は別セッションの UI フォントのまま、他 0) → 保存 → Guide/3 撮影 (統括が目視: 表の面の構成は §9 どおり。Edit の既定値で [加入する] と出るのは const の旧値で、実行時は KD_Language の「料金・購入はこちら」。const も合わせる)。
- 2026-10-16 Codex 独立レビュー 2 回目 (§8・§9 の差分、5 観点): 高 0 / 中 2 / 低 2。**問題なし**: 旧ノードの削除範囲と残存 FAIL、URL 欄の静的条件 (Quest / iOS のコピーは実機)、17 × 13 の欠落・重複なし、数字と桁区切り、応援文、ボタン名、§9 のキー・配置と bindings。**指摘と対応** (全部反映): 中: 価格「1か月 500 から」は 12 か月 4,800 (月 400) と矛盾 / 「1か月 500 Credits (…)」の言い切りに (13 言語)。中: 表の面の導入に「トレーナーがいる間」の条件が無く無料の紹介にも読める / 3 文目を「トレーナーがいる間は扉が開き、会員でないお友達も一緒に入れます」に (13 言語)。低: `KH_CovePortal` の削除が名前だけで階層を問わない / シーン直下で中身が入り江マーカーだけの物に限定、他は書き込み前に中止。低: §8 の根拠の書き方 (利用者報告から「仕様上不可」と断定) と URL 欄の幅 / 公式仕様と報告を分けて「本用途では採用しない」に、幅 0.94 m に訂正。
- 2026-10-16 2 回目の指摘の反映後 (A: `KH_CovePortal` の削除はシーン直下で中身が入り江マーカーだけの物に限定、他は書き込み前に中止。B: price 13 言語を言い切りに、cove.lead の 3 文目に「トレーナーがいる間は扉が開き」。const の join も「料金・購入はこちら」): C# / U# 0 → Guide/1 BUILT (位置保持、字形の追加なし) → Verify 新規 FAIL 0 / WARN 1 (Details/Body ja fill 59 %) → Menu/11 → 22 PASS → 保存 → eval で 13 言語の収まりを実測 (price: ja/ko/zh 30 pt 1 行、Latin 9 言語 28 pt 2 行、溢れ 0。cove.lead: 36 pt 4〜8 行、溢れ 0) → Guide/3 撮影 (`Temp/kh-guide-boards/shots/Trainer_read_1.8m.png`)。→ review
- 2026-10-16 札の折り返し (撮影で [料金・購入はこちら] が「こち / ら」に): StoreButton 420 幅 (Status / GiftNote 360)、Store / Details の札は折り返し禁止 + 自動縮小 (40 → 20 / 32 → 18)、Guide/2 に「ja / en は 1 行で枠内 (FAIL)、他 11 言語は推定 WARN」を追加。C# 0 → Guide/1 BUILT → Verify 新規 FAIL 0 (ja 360 / 400 mm、en 334 / 400 mm、1 行) → 保存 → Guide/3 撮影。
