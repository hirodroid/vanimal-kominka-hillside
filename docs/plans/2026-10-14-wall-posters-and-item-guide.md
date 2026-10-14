# 塀のポスター 2 枚とアイテム案内の看板 — 設計と制作票 (IMP-UI-008)

[IMP-UI-008](../development/issues/IMP-UI-008-credits-shop-and-item-boards.md) の B (torinosukeShop の紹介とバニマルのグループポスター) と C (アイテムの説明と手元メニューを開く看板) の作りと、素材の出所・利用条件の記録。クレジット (A) は Issue 本文に書く。Unity での反映・配置の数値の確定は統括のメニュー実行後 (2026-10-14 時点で未実行)。

## 1. 参照元 (古民家ドッグラン、2026-10-14 に読んだ)

| 対象 | 古民家の現物 (読んだ場所) |
|---|---|
| torinosuke の紹介 | `Assets/KominkaDogRun/Editor/KD_AuthorPosterEditor.cs` (古民家 `docs/development/issues/IMP-ENV-023-dog-model-author-poster.md`)。縁側ギャラリーのクレジット掲示板の真上。購入プレハブ「ポスターボード」(Atelier_Fuyutsuki) に画像 `Assets/_Originals/torinosuke/torinosukenoie.png` (1241 × 1754) を 0.354 × 0.50 m、その下 7.5 mm に URL のコピー欄 `https://torinosuke05.booth.pm/items` (0.40 × 0.035 m、22pt、読み取り専用・選ぶと全選択)。見出しなし |
| グループポスター | シーン直下の `GroupPageOpener` (MimyLab の配布プレハブ、Git 管理外)。Sprite `Assets/KominkaDogRun/Data/Panfret/poster.png` (1587 × 2245) を 0.635 × 0.898 m。Interact (表示「Open Group」、距離 2 m) で `Store.OpenGroupPage("grp_45df2c16-4e9f-4722-8025-d08cf4f1211b")` |
| 札で手元メニューを開く板 | `Assets/KominkaDogRun/Editor/KD_PlayGuideEditor.cs` (古民家 `docs/development/design/play-guide-corner.md`、IMP-PRG-013 / 014)。おじいさんの右の白壁、犬紹介板「庭の犬たち」の右に写真の板「犬とのあそびかた」0.94 × 1.232 m。副題「気になる写真を押してみよう」、札 6 枚 (写真・名前・説明 2 行・区分の札。おもちゃ / 呼ぶ / ライブ / かざり / パーティクル / トレーナー)、下端に公式サイト `https://vanimal.jp/` のコピー欄。札の Button が `KD_TreatPouch._OpenFromBoard*` を呼び、閉じていれば目の前に開き、開いていれば呼び戻してそのタブにする (自分で開けた印は立てない、ようこそ中は先にようこそ) |

依頼文の「犬たちのお気に入り」は、古民家の文書・シーンに同名の物が無い。お気に入りを選ぶ犬紹介板「庭の犬たち」の隣にある上の写真の板と読んだ (ユーザー確認の候補)。

## 2. 裏山の作り

### 2-1. 塀のポスター `KH_WallPosters` (Guide/20 Build・Guide/21 Verify)

- 置き場: 古民家の塀 `KH_World/KH_Places/KH_FarKominka/KH_KominkaWall/外壁` (MeshCollider) の外面。門 `KH_GateBlock` の枠で見て、案内板 Kominka のポータル (`PortalAnchor`、門の西) と**反対の東側**。
- 探し方 (初回だけ): 門の中心から塀に沿って東へ 0〜12 m を 0.1 m 刻み。スポーン側 3 m から塀へレイを撃って面を見つけ、ポスター 2 枚の範囲 (幅 1.35 m・高さ約 1.0 m) を 0.1 m の格子のレイで「塀が後ろにあり、±3 cm で平ら」と確かめる。画像の中心は読む場所の地面から 1.35 m (塀が低ければ 1.20 m まで下げる)。
- 規則 (固い = 置けない): 塀が覆う / ポータルの側でない / ポスターの体積と読む場所 (前 0.3〜1.5 m) に固い Collider が無い / ほかの看板がポスターの前 0.3 m に立っていない。規則 (やわらかい = WARN、ユーザー判断): 読む場所に看板の足元・ほかの看板の読む場所が重ならない、スポーンから 1.5 m 以上、Kominka のポータルの空間に掛からない、読む人からポスターへの視線を看板が遮らない。
- 2 区間: 写真ボード `KH_PhotoBoard` (門の東 約 4.8 m、塀から 約 1.4 m 前) の手前と奥。全規則を満たす最初の点を区間ごとにログへ出し、手前を優先。オフラインの見積もり (門の開口の半幅 2.25 m、写真ボードの足元 東 3.4〜6.2 m) では、手前は幅 0.9 m しか空かずポスター (1.35 m) が入らないか写真ボードの足元に読む場所が掛かるので、**奥 (門から東へ 約 7 m) が選ばれる見込み**。塀がそこで平らに続くかは Guide/20 の物理で決まる。
- 作り: 古民家と同じ寸法の画像を、案内板と同じ木の板 (KH_RiverWood、Collider なし、Batching Static) に、読むだけの World Space Canvas の RawImage で掛ける。torinosuke を門寄り、グループをその隣 (間 0.20 m)。URL 欄は古民家と同じ `KD_MenuFinishEditor.CopyField` (専用の小さな Canvas に GraphicRaycaster + VRCUiShape + trigger BoxCollider)。グループは Interact の trigger + `KH_GroupPoster` (U#、`BehaviourSyncMode.None`、`Store.OpenGroupPage`)。Interact の文言は `guide.wall.group.interact` (13 言語)。
- 古民家との違い: 購入プレハブ (透明な枠) の代わりに木の板。lilToon ではなく UI の unlit なので、古民家が Android / iOS で直した白飛び (BUG-ENV-003・ポスターボードの透明材質) の差し替えは要らない。

### 2-2. アイテム案内の看板 `KH_ItemGuideBoard` (Guide/22 Build・Guide/23 Verify)

- 作り: 案内板と同じ屋根つきの立て看板 (`BuildStructure` / `EnsureCanvas`、canvas 1300 × 1180 mm、下端 0.80 m)。見出し「手元メニューでできること」、和紙の紙に副題「札を押すと、手元メニューがその画面でひらきます」、札 8 枚 (2 列 × 4 段、各 580 × 200 mm: 名前 46〜30 mm・説明 2 行 34〜24 mm・「ひらく ▶」)。押せる札は暗い板 + 明るい字 + 角丸、押せない札は和紙の平らな面 (vrc-world-ui「角丸は押せる面だけ」)。
- 押せる面: 描く Canvas とは別の透明な入力の Canvas `Input` (GraphicRaycaster + VRCUiShape + trigger BoxCollider、Default)。札の Button は `KD_TreatPouch` の backing UdonBehaviour へ `SendCustomEvent` (古民家と同じ。新しい Udon は無い)。KH_GuideBoards の下には置かない (Guide/2 の「読むだけ」の検査に掛かる)。

| 札 (タブの並び) | 名前の文言キー | 押した先 (`KD_TreatPouch.Board.cs`) |
|---|---|---|
| さんぽ | `KD_TreatPouch.WordWalkTab` | `_OpenFromBoardWalk` → さんぽタブ |
| おやつ | `KD_TreatPouch.WordTabTreat` | `_OpenFromBoardTreat` → おやつタブ |
| かざり | `KD_TreatPouch.WordTabAcc` | `_OpenFromBoardAccessory` → かざりタブ (飾り・撫でエフェクト・首輪とリード) |
| 呼ぶ | `KD_TreatPouch.WordTabSleep` | `_OpenFromBoardCall` → 呼ぶタブ |
| おもちゃ | `KD_TreatPouch.WordTabToy` | `_OpenFromBoardToy` → おもちゃタブ |
| ばにまるトレーナー | `guide.items.trainer.name` | `_OpenFromBoardPlay` → あそぶタブ |
| 写真と撮影カメラ | `guide.items.photo.name` | `_OpenFromBoardPhoto` → ヘッダの [写真] と同じ面 (撮影カメラは PC のみ) |
| 洗い場・おさんぽカート | `guide.items.wash.name` | 押せない (手元メニューに入口が無い。道具は洗い場の板、カートは乗り場) |

- 入れないもの: ライブ (裏山は準備中、DES-LIV-001)、古民家の CE 商品 (今回の公開版から外した、IMP-PRG-002)、札の写真と区分の札・公式サイトの URL 欄 (ユーザー判断の候補。写真は撮影が要る)。
- 探し方 (初回だけ): 地図の看板 `KH_GuideBoards/Map` (39.20, 46.60) にいちばん近い周回の道の線分 (`KH_WalkRoute.nodePositions`、2026-10-14 時点の節点 1 → 2) へ地図を映した反対側。地図の真向かいから道に沿って 0 → ±3 m、道の中心線から 2.4〜4.0 m、表を道へ向け yaw ±0 / ±10°。判定は DogBookStand と同じ (Guide/2 の Placement + 看板どうし + 写真ボード + 犬紹介の看板 + 塀のポスター)。オフラインの見積もり: 真向かい (45.4, 51.8) は犬の待機の輪 (KH_Roam_Dachs (44, 48)) から 3.3 m + 足元が取れず落ち、全規則を満たすのは道に沿って 1〜2.5 m 先の (43.2〜44.5, 52.1〜53.0) 付近、**地図から 約 7〜8 m**。「地図の隣」とは言いにくい距離なので、Guide/23 は地図から 8 m を超えると WARN を出す。
- 手元メニューの口 (`KD_TreatPouch.Board.cs`、裏山改変): 古民家の `_OpenFromBoard*` / `OpenFromBoard` を裏山のタブと写真の面へ移した。閉じていれば目の前に開き、開いていれば呼び戻してその面にする。自分で開けた印は立てない。寝床の位置指定中は何もしない。ようこそ表示中に押したら、ようこそを目の前へ呼び戻し、[メニューを見る] (`_WelcomeMenu`) で札の面へ続ける。ローカルのみ、同期・保存なし。

### 2-3. 文言 (13 言語)

新しいキー 15 個 × 13 言語を `Assets/KominkaHillside/Imported/KominkaDogRun/Data/UITranslations.json` へ足した (`guide.wall.group.interact`、`guide.items.*` 14 個)。日本語は自作、他の 12 言語は既存の Guide 板の調子に合わせた AI 訳で校正なし。束縛は `UILocalizationBindings.json` (ラベル 25 = root `KH_ItemGuideBoard`、Interact 1 = root `KH_WallPosters`)。日本語・英語の字は既存の文言にある字だけ (共有フォントへ足す字 0 の見込み。Guide/20・22 が `AddGlyphs` で念のため焼く)。

## 3. 素材の記録 (source-rights の型)

| 素材 | 成果物 | 参照元・出所 | 利用条件の根拠と確認日 | 使い方 | 採用前確認 |
|---|---|---|---|---|---|
| torinosuke のポスター画像 | `Assets/KominkaHillside/Textures/Posters/KH_TorinosukePoster.png` (1241 × 1754、SHA-256 `7bf7f78a…46b5`) | 古民家 `Assets/_Originals/torinosuke/torinosukenoie.png` と同一 (バイト一致)。犬モデルの作者「TORINOSUKEの家」からユーザーがもらったポスター (古民家 IMP-ENV-023、2026-10-04 ユーザー配置) | **掲示条件は未確認** (古民家 IMP-ENV-023「作者からの掲示条件は不明のまま」)。2026-10-14 時点で裏山でも調べられる記録は無い。古民家では Git 管理外 (`Assets/_Originals/` は .gitignore) | 加工なし (縦横比どおり 0.354 × 0.50 m で表示)。取り込みは PC 2048 / Android・iOS 1024、圧縮 | 未。**コミットしてよいか (古民家は Git に入れていない) と掲示条件はユーザー判断**。判断まで「確認済み」と扱わない |
| VAnimal のグループポスター | `Assets/KominkaHillside/Textures/Posters/KH_VAnimalGroupPoster.png` (1587 × 2245、SHA-256 `e6033a61…317b`) | 古民家 `Assets/KominkaDogRun/Data/Panfret/poster.png` と同一 (バイト一致)。古民家の ui-map §10「画像と meta は自作コンテンツとして管理する」(2026-09-07 ユーザー配置) | 自作 (VAnimal の告知物)。生成履歴は古民家の記録に無い (同じフォルダの explain-onboarding.png は imagegen 編集と記録あり。poster.png の作り方は不明)。アバターの人物と犬 (TORINOSUKE のモデル) が写る | 加工なし (0.635 × 0.898 m)。取り込みは同上 | 古民家で公開済みの同じ画像。生成履歴が不明な点は記録に残す |
| グループページを開く処理 | `Assets/KominkaHillside/Scripts/KH_GroupPoster.cs` | 古民家は MimyLab GroupPageOpener (配布物、Git 管理外)。**コードは写していない** (SDK の `Store.OpenGroupPage` を 1 回呼ぶだけの自作) | SDK の公開 API (VRC.Udon.VRCWrapperModules に `__OpenGroupPage__SystemString__SystemVoid`、2026-10-14 確認) | — | 実クライアントでページが開くかは Play |
| 板の枠 | 既存の `KH_RiverWood.mat` | 案内板と同じ | 既存の記録のまま | — | — |

古民家の購入プレハブ (Atelier_Fuyutsuki のポスターボード) と MimyLab は裏山へ入れていない。

## 4. メニューと順番 (統括)

文言と束縛を足したので、**新しい 2 つの根ができるまで、束縛の全行を解決するほかの Build (Guide/1・Menu/11・DogBook/2 など) は「objects not in the scene」で何も変えずに止まる**。Guide/20 と Guide/22 は相手がまだ無いとき束縛を後回しにし (WARN)、後に流した方が全部を束縛する。

1. compile (`unity-compile.sh`) → `Guide/0 - Create Board Programs` (KH_GroupPoster の Program Asset) → compile `--udon`
2. `Guide/20 - Build Shop Intro And Poster` (テクスチャの取り込み設定 → 塀の探索 → 作る → 保存 → Guide/21 相当。束縛は後回し。Interact の文言の束縛と ValidateScene は、相手の看板が無い間だけ WARN「bind deferred」になり FAIL にしない)
3. `Guide/22 - Build Item Guide Board` (地図の向かいの探索 → 作る → 束縛 → 保存 → Guide/23 相当)
4. `Guide/21 - Verify Shop Intro And Poster` (2 の後の束縛で通る) → `Menu/11` → `Menu/22` → `Guide/2` → `Build Size/7 - Apply Mobile Caps` → `Build Size/8` → 保存

## 5. Edit で証明すること / Play に残すこと

| Edit (Guide/21・23、Menu/22) | Play (ユーザー) |
|---|---|
| 塀が後ろにあり平ら・ポータルと反対側・固い物が無い・看板の規則 (WARN の一覧) | スポーン・道から見つけられるか、読めるか (1〜3 m、PCVR / Desktop / Quest / スマホ) |
| 画像の寸法と縦横比、取り込み設定、URL 欄の設定と字形 | URL 欄を選んでコピーできるか |
| KH_GroupPoster の同期 None・group id (proxy / backing)・Interact の文言の束縛 | Interact でグループページが開くか (ClientSim では開かない) |
| 札 7 枚の Button → `_OpenFromBoard*` の配線、`_OpenFromBoard*` の存在、押せない札に当たりが無い | 札を押すとメニューがその面で目の前に開くか、開いている板が呼び戻されるか、ようこそ中の動き |
| 文言 13 言語の有無・ja / en の枠の収まり (他 11 言語は見積もり WARN)・束縛・ValidateScene | 文字の大きさ・読みやすさ、11 言語の実機の字形 |
| 置き場所の規則・地図からの距離・道の反対側 | 通行・犬の通り道・看板の前の溜まり |

## 6. 既知の注意 (2026-10-14)

- 地点ワープの着地点を置かない円 (Course/80・81) は `KH_GuideBoards` の直下の板だけを数える。犬紹介の看板・写真ボードと同じく、新しい 2 つは入らない (Course/81 は FAIL しない)。着地点が看板・ポスターの前に落ちるかは未確認で、入れるなら Course/80 の拡張は別作業。
- アイテム案内の探索 (195 点) で全部が固い規則 (体積・高低差 1.2 m 超など) に掛かると、Guide/22 は作りかけの根を消して止まるが、シーンは dirty のまま残る (次の Idle が拒む)。シーンを保存せずに開き直し、`ItemAlongOffsets` / `ItemRoadDistances` を広げて再実行する。
- `KH_GroupPoster` の UdonBehaviour は保存時に NetworkID を 1 つ受ける (KH_PortalBoard と同じ)。Scene Layout 系の件数の変化はこれ。
