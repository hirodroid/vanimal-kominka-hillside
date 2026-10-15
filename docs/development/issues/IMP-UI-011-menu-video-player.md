---
id: IMP-UI-011
type: IMP
system: UI
status: review
priority: Medium
summary: VizVid (1.8.2、パッケージ導入済み・シーン未配置) の動画プレイヤーを手元メニューの副画面に組み込む。入口はヘッダの [写真] の隣の [動画]。モニターは置かない
next: "ユーザー: §3 の決定 1〜3 (推奨で実装済み。変えるなら Player/9 → 直して Player/1)。PCVR で [動画] → 面 → URL の実再生 (上下の向き・音・音量・消音・BGM が消えて停止で戻る)、板を閉じても音が続くこと。2 人で同期・途中参加 (B)。Quest の再生と負荷 (D)。iOS は見るだけ (E)"
updated: 2026-10-16
---

# IMP-UI-011 手元メニューに動画プレイヤー (VizVid) の面を組み込む

## 1. 依頼 (2026-10-15)

ユーザー: VizVid をプロジェクトに導入した。モニターを置く場所が無いので、メニューに組み込みたい。写真の横に「プレイヤー」ボタンを作るのがいちばんいいかなぁ。

統括 Fable が司令塔。起票 → 設計の推奨 → Codex 相談 → 実装 (Opus) → Edit 検査 → Codex 独立レビュー → コミット。

## 2. 起票時の現状 (読み取りだけ。Unity は未操作)

- **VizVid 1.8.2** (`idv.jlchntoz.vvmw`、依存 `vrcw-foundation 0.0.68`・`lazyswitch 0.10.2`) は `Packages/vpm-manifest.json` に入っている。`Packages/*/` は `.gitignore` なので、**manifest と packages-lock が唯一の記録** (未コミット。同じ diff に `net.ureishi.qvpen 3.3.15` も入っている)。`ProjectSettings.asset` の define に `LAZY_SWITCH` 等が足されている
- **シーンには VizVid のプレハブ・スクリプトが 1 つも無い** (GUID 照合 0 件)。既存の `Screen` / `ScreenControls` は撮影カメラ (IMP-WLD-008) のモニターで、VizVid ではない
- 手元メニュー `KD_TreatPouch`: 板は Canvas 1140×700 (scale 0.001 = 1.14 m × 0.70 m)、`VRCUiShape` + BoxCollider 済み。ヘッダ (Y 314、高さ 44) は `KD_HandMenuScrollEditor.LayoutFrame` が並べる: 見出し (-350, 幅 400。[写真] が無いときは -335 / 430)、[写真] (-65, 幅 120、`KH_PhotoGalleryEditor.HeaderButton*`)、残高 (文字 150 / 幅 232、**枠 `PointsButton` は幅 284** = 8〜292)、[設定] (370, 幅 120)、[閉じる] (500, 幅 110)
- 副画面の作り: `Page_Settings/PhotoPage` を `extraSettingsPages` に登録し、`_OpenExtraSettings(page, titleKey)` で開く (`KD_TreatPouch.Photo.cs`)。中身は別の U# (`KH_PhotoAlbum`) が描き、ビルダー `Tools/KominkaHillside/Photo/0〜5` が作る。写真の面の本文の帯は **Y 約 192〜-230** (上段の行 170、下段の行 -205。全面の覆いだけ -303 まで)。下段には撮影カメラの入口 ([撮影カメラ (PCのみ)] (-400, -205) 260×50)
- VizVid の画面: `Core.screenTargets` は **UI の RawImage を直接受ける** (`Core_Screen.cs` mode 4。AVPro のときは uvRect を上下反転)。`CoreEditor.cs` 1034・1113 行が RawImage を足すときの 7 本の並列配列の書き方 (`mode 4 / index -1 / "_MainTex" / "_IsAVProVideo" / 既定テクスチャ = rawImage.texture / ST = Vector4.zero`)。フィールドは `internal` なので SerializedObject で書く
- VizVid の操作 UI (`Default UI (Narrow)`) は独自の Canvas 1000×300 + UiShape + 多数の参照 + `LanguageReceiver` 前提。メニューの Canvas に入れ子にするより、**面のボタンから `FrontendHandler` の公開メソッドを呼ぶ**方が小さい (`_Play / _Pause / _Stop / _LocalSync / _GlobalSync`、URL は `FrontendHandler.PlayUrl(url, url, type)` (履歴・投入者・キューを通す。Core 直呼びはそれを迂回する)、状態は `FrontendHandler._AddListener(this)` で `_OnUIUpdate` / `_OnVolumeChange` / `_OnVideoError` を受ける)。URL 欄は `VRCUrlInputField` (Udon は文字列から VRCUrl を作れない)。`GetSuitablePlayerType` は 1 始まり (0 = 不適合)。`Locked` の既定は false で `PlayUrl` 側に検査は無い
- 音: 各バックエンド (`VideoPlayerHandler.primaryAudioSource`) の AudioSource が出口。`Core.Volume / Muted` はローカル値 (同期しない) で音量とピッチだけを書き、spatialBlend は触らない。設定面の音量は `base × gain` の台帳 (`Imported/` 側)。BGM はライブ・パレード中に `KD_BgmPlayer.LiveSilences()` で消す配線がある
- 仕様の前提: [game-design §4 音](../../game-design.md) は「BGM・環境音は同期しない」、§7 は「ラジオは今回持ち込まない (2026-09-23)」。**インスタンスで共有する動画プレイヤーはこの 2 行の例外になる**ので、§3-1 でユーザーに決めてもらう
- 端末 (公式 [Video Players](https://creators.vrchat.com/worlds/udon/video-players/)): URL の解決は **Android でも動く**。URL の読み込みは利用者ごとに全プレイヤー共通で **5 秒に 1 回**。許可リスト外のホストは利用者の「Allow Untrusted URLs」が要る。iOS の記述は無い

## 3. 設計の推奨 (統括 Fable、Codex 相談 2026-10-15 反映。ユーザー確認待ち。推奨 = 「はい」で進める既定)

| # | 決めること | 推奨 | 代案 |
|---|---|---|---|
| 1 | **同期と音** | **A: VizVid の素の挙動**。URL・再生位置はインスタンスで同期 (誰かが入れた動画を全員が自分のメニューで見られる)。音は **2D** (spatialBlend 0、`VRC_SpatialAudioSource` の spatialization OFF) で、再生中は面を閉じていても聞こえる。面に本人だけの音量スライダーと [消音] (`Core.Volume / Muted`。台帳には載せない)。再生中は BGM をライブと同じ掛け金で消し、停止・エラーで戻す。面を閉じてもデコードは続く (ActiveRegion は停止しない。Quest の負荷は実機で測る) | B: ローカル専用 (自分の面で自分だけが見る・聞く)。同期なしで軽いが「一緒に見る」ができない |
| 2 | **入口** | **ヘッダの [写真] の隣に [動画]** (ユーザー案。どのタブからも 1 回で開く)。両方あるとき: 見出し 中心 -390 / 幅 320 (-550〜-230)、[写真] -150 (-210〜-90)、[動画] -20 (-80〜40)、残高の枠 中心 175 / 幅 250 (50〜300)・文字 210、[設定] 370、[閉じる] 500 (隙間 20 / 10 / 10 / 10)。`LayoutFrame` は「無し / 写真だけ / 動画だけ / 両方」の 4 通りを 1 つの関数で並べる (動画だけのときは写真の席 -65 に座る。Photo/5 で写真を外しても崩れない) | 写真の面の下段 ([撮影カメラ] の隣) に [動画プレイヤー]。ヘッダは縮まないが 1 タップ増える |
| 3 | **ボタンの語** | **「動画」** (ja 2 字。en "Video"。120 px に 13 言語が収まる) | ユーザーの語「プレイヤー」(5 字、120 px・22 pt では窮屈。"Player" は英語圏で曖昧) |
| 4 | 面の構成 | 本文の帯 (Y 148〜-232) に収める: 左に 16:9 の画面 **640×360** (RawImage、`Core.screenTargets` mode 4)、右の列 (x 110〜550) に URL 欄 (`VRCUrlInputField`) + [入れる]、[再生] [一時停止] [停止] [再同期 (自分だけ)]、音量スライダー + [消音]、端末の注意 1 行。上段は状態の 1 行 (読み込み中 / エラー / 再生中・入れた人) と [もどる] (450, 170) 220×44 (写真の面と同じ)。描画の更新はリスナーのイベント + 面が開いている間だけ 0.5 s の自己再予約。URL は 5 秒に 1 回の制限に合わせて連打を抑える。`enableQueueList = false` (即時再生) | — |
| 5 | 端末 | Android も URL を解決する (公式)。面の注意は「許可リスト外のサイトは設定の Allow Untrusted URLs が要る」の 1 行。iOS は NOT_TESTED (保証しない) | — |
| 6 | 置き方 | VizVid の `VVMW (No Controls)` (Core + FrontendHandler (core 配線済み) + AVPro / Builtin / Image の各バックエンド + 音源) をビルダーが **シーンに展開 (UnpackPrefabInstance Completely)** してから配線する (プレハブ内 → 外の参照をオーバーライドで持たない。`vrc-udonsharp` §3)。再実行は削除 → 再生成で冪等 (NetworkIDs の残骸も検査)。`[Resolve]` はビルド時に foundation が埋めるが **`SingletonCoreControl` は Inspector の印で、Frontend の `core` は配線を Verify で見る**。バックエンドの映像取得用 Renderer は消さない | — |

決定: (未。ユーザーの回答待ち。推奨で実装済み 2026-10-16。変えるときは Player/9 で外してから直す)

## 4. 作業範囲

- **U#** `Assets/KominkaHillside/Scripts/KH_VideoPage.cs` (ローカル専用、`_` 始まり。`FrontendHandler` のリスナー、ボタン → `_Play` 等、URL 欄 → `PlayUrl`、音量 → `Core.Volume`、文言は `KH_PhotoAlbum` と同じく自分で描く) と `KD_TreatPouch.Video.cs` (partial: `videoPage`、`_VideoOpen` / `_VideoBack`。`Photo.cs` と同じ戻り方)、`KD_BgmPlayer` の掛け金に動画の条件 (裏山改変)
- **Editor** `Assets/KominkaHillside/Scripts/Editor/KH_VideoPlayerEditor*.cs`: `Tools/KominkaHillside/Player/0 Create Program Asset`、`1 Build` (VizVid の展開・Core の 7 配列・2D の音源・`Page_Settings/VideoPage`・ヘッダの [動画]・`LayoutFrame` の追従・BGM の配線)、`2 Verify` (読むだけ: 7 配列の長さ、RawImage が mode 4、Frontend.core、ヘッダ 4 通りの重なり無し、13 言語の収まり、音源 2D、NetworkIDs)、`9 Remove`。`KD_HandMenuScrollEditor.LayoutFrame` を 4 通りに。`KH_PhotoGalleryEditor` の Place / Verify の固定値 (`TitleWithButton*`) を「動画の有無」に追従
- **文言** `UITranslations.json` に `video.player.*` × 13 言語 (入口・面の題・ボタン・状態・注意)。Bind には登録しない (面が描く)
- **コミット**: `Packages/vpm-manifest.json` と `packages-lock.json` (VizVid + 依存の記録。QvPen も同じ diff に入っているので、その旨をコミットに書く) を本票で stage する。シーンは別セッションの未コミット分を含むので stage しない (Player/1 で再生成できる)

## 5. 完了条件

Edit で確定できるもの:
- [ ] ヘッダの [動画] からどのタブでも 1 回で面が開き、[もどる] で元のタブへ戻る。[写真] と両方あっても見出し・残高の枠と重ならない (4 通り、13 言語、Player/2・Photo/2)
- [ ] Core の 7 配列・RawImage mode 4・Frontend.core・音源 2D・BGM の配線が Player/2 で PASS。C# / U# 0。Codex 独立レビュー反映

実機でしか決まらないもの (NOT_TESTED のまま review へ):
- [ ] URL を入れると自分の面に動画が映り (上下の向きも)、再生・一時停止・停止・再同期が効く。面を閉じても音が続き、音量・消音が本人だけに効き、BGM が消えて停止で戻る (PCVR、A)
- [ ] 2 人で同じ動画が同期して映る。途中参加・入れた人の退出 (B)
- [ ] Quest で面が開き再生できる。負荷が許容 (D)。iOS は見るだけ (E、保証しない)。許可リスト外の URL でエラーの文言が出て遊びは続けられる

## 6. 記録

2026-10-15: 起票 (統括 Fable)。コードと VizVid のパッケージの読み取りだけ。Unity 未操作、実装なし。

2026-10-15 Codex 相談 (`codex exec -m gpt-6-astra`、読むだけ): §3 の推奨を直した 4 点 — (1) ヘッダの最初の案 (見出し 320 のまま中心 -350、残高 165) は見出しと [写真] が 20 px、[動画] と残高の枠 (284) が 17 px 重なる → 上の値へ。(2) 画面 880×495 は本文の帯 (約 450 px) に入らない → 640×360 + 右の列。(3) 「Quest は直リンクだけ」は誤り (Android も解決する) → 注意文を許可リストの 1 行に。(4) `SingletonCoreControl` はビルド時に埋まらない → Frontend.core は Verify で見る。ほかに採った点: `FrontendHandler.PlayUrl` を使う (Core 直呼びは履歴・キューを迂回)、`enableQueueList=false`、`_OnVolumeChange` も受ける、URL 5 秒制限の連打抑止、ActiveRegion では止まらない (面を閉じてもデコード継続)、Bundle 容量は配置後に測る。

2026-10-16 実装 (統括 Fable、実装 Opus 2 本 (ランタイム + 翻訳 / Editor ビルダー + ヘッダ)、Codex 独立レビュー D/M/X 高 0・中 5・低 1 反映)。

- **ランタイム**: `KH_VideoPage.cs` (新規 U#、ローカル専用、sync None。`FrontendHandler._AddListener` で `_OnUIUpdate / _OnVolumeChange / _OnVideoBeginLoad / _OnVideoError` を受け、`core.State` で状態の 1 行 (なし / 読み込み中 / 再生中 + 入れた人 (履歴の先頭から) / 一時停止 / エラー = RateLimited・許可リスト外・他)、URL は `VRCUrlInputField` → `frontend.PlayUrl(url, url, type)` (type 0 は 1 で投入 = VizVid の UI と同じ)、5 秒制限の連打抑止 (同じ URL は捨て、別の URL は「あと {0} 秒待って」)、Locked なら投入しない、音量 → `core.Volume`、消音 → `core.Muted`、面が開いている間だけ 0.5 s の見回り、板を開き直しても再開 (`SetOpen` から)、言語切替でヘッダの [動画] も描き直し)。`KD_TreatPouch.Video.cs` (partial: `videoPage / videoPageBehaviour`、`_VideoOpen / _VideoBack`。`KD_TreatPouch.cs` 本体は 2 行)。`KD_BgmPlayer.videoCore`: `IsPlaying && !IsStatic` で BGM を消す (静止画では消さない)。翻訳 `video.player.*` 22 キー × 13 言語 (back は既存 `gift.back` を再利用)。
- **Editor**: `KH_VideoPlayerEditor.cs / .Verify.cs` (Player/0 Program Asset、1 Build = `VVMW (No Controls)` を展開 (Unpack Completely) → `KH_VideoPlayer (VizVid)` (Core + FrontendHandler + AVPro / Builtin)、Core の 7 配列に面の RawImage (mode 4)、`enableQueueList=false`、音源 2D (spatialBlend 0・VRCSpatialAudioSource OFF)、`Page_Settings/VideoPage` (画面 640×360 (-230,-46)、右の列に URL 320×50 + [入れる]、[再生][一時停止][停止][再同期]、音量 220×40 + [消音]、注意 440×110 3 行 18→14pt、[もどる] (450,170))、ヘッダ `VideoButton` (-20)、配線と字形 (早 を追加)、保存前検査、2 Verify (178 項目: 7 配列・配線・ヘッダ 4 通り・帯・収まり・NetworkIDs)、9 Remove)。`KD_HandMenuScrollEditor.HeaderLayout(photo, video)` がヘッダの正本 (4 通り。両方: 見出し -390/320 自動縮小 24→18pt、[写真] -150、[動画] -20、残高 175/210 枠 250)。Photo の Place / Verify はこれに追従。
- **検証 (Edit、統括)**: C# 0 / U# 0 (172 scripts)。Player/0 → Player/1 BUILD_OK → Player/2 PASS fails=0 warns=0、Photo/2 PASS 0/0、Menu/21 317/0。シーンは保存済み (別セッションの未保存分も一緒に保存した)。**Play・実機は未確認** (ClientSim は動画を再生できない)。11 言語の収まりは VRChat のフォールバックフォントなので実機。
- **Codex 独立レビューの反映**: BGM の静止画除外 / 板を開き直したときの見回り再開 / URL 投入の Locked 検査 / 閉じたままの言語切替 / 注意文を「URL・再生・一時停止・停止はみんなに効く。音量と再同期は自分だけ。許可リスト外は Allow Untrusted URLs が要る」の 3 行に (「ON にすると映る」の断定をやめた)。見送り: State 3 (Ready) を一時停止と表示する意味差 (実害なし)。別件候補: `KD_TreatSetupEditor.PrunePanelChildren` が Photo/Video のボタンを keep しない (周回シーンでは RejectLegacyRebuild が先に止めるので今回の経路では起きない。KH_VideoPlayerEditor の冒頭に注意)。
- **コミット**: Issue・overview・コード・翻訳・フォント (字形 早)・`KH_VideoPage.asset`・`KD_BgmPlayer.asset`・`Packages/vpm-manifest.json` と `packages-lock.json` (VizVid 1.8.2 + 依存。同じ diff の QvPen 3.3.15 も含む)・`ProjectSettings.asset` (LAZY_SWITCH 等の define)。シーンと他の .asset は別セッションの未コミット分を含むので stage しない (Player/1 で再生成できる)。
