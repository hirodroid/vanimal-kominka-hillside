# 空を古民家と合わせ、手元メニューにナイトモード (OFF / ON) を置く (設計・記録・Play チェックリスト)

状態: 実装済み・Editor 検査済み (§3)。Codex レビュー: 設計 P/M/D (§5)、実装 D/M/U (§6)。**2026-09-22 に PCVR で確認済み (ユーザー)、Quest / iOS は未確認** (§4)。作業票は [IMP-ENV-002](../development/issues/IMP-ENV-002-skybox-and-night-mode.md)。ここは「順序と分担」と、実装時に決めた値の記録。何を作るかは Issue が持つ。

## 1. 決めたこと (Issue の「実装前に決めること」への答え)

| 項目 | 決定 | 理由 |
|---|---|---|
| 空の材質 | `Assets/KominkaHillside/Materials/KH_Skybox.mat` を新設。`Skybox/Cubemap`、`_Tex` = Allsky `Day Sun High Clear` (キューブマップ 2048²×6)、`_Tint` (0.5,0.5,0.5,0.5)、`_Exposure` 1、`_Rotation` 180 (古民家 `KD_Skybox.mat` と同値) | 古民家と同じ見え方。**Setup が毎回書く値** = shader / `_Tex` / 昼の `_Exposure` `_Tint` (`KH_NightMode` の定数が正本)。**新規作成時にだけ書く値** = `_Rotation` (太陽の方角。裏山の地形に合わせて Inspector で変えてよい。`.mat` はアセットなので KH_World の再生成で消えない) |
| 購入アセット | `Assets/Allsky/` を `.gitignore` へ (古民家と同じ 2 行)。ユーザーが置いた原本フォルダをそのまま参照し、コピーしない | 購入アセットはコミットしない (CLAUDE.md)。キューブマップの GUID は古民家と同じ `34019144…` なので `.mat` の参照はクローン先でも一致する |
| モバイル圧縮 | キューブマップの TextureImporter に Android / iPhone `overridden=true, maxTextureSize 1024` を Setup が書く (PC は 2048 のまま)。Verify は `overridden` と size の両方を見る | 古民家 IMP-ENV-010 と同じ。空はビルドサイズの最大の 1 枚。1024 は解像度の上限で、圧縮形式は既定のまま |
| ライティング | 触らない (環境光 Trilight・Directional Light・反射はそのまま)。実行時に空を差し替えても `DynamicGI.UpdateEnvironment` は呼ばない | ナイトモードは全画面乗算なので環境光に依存しない。反射は昼のまま (夜に更新しない = 毎フレームの負荷を持ち込まない)。昼の見え方の調整は空を替えた後にユーザーが判断 (Issue A) |
| ナイトモードの方式 | 古民家のライブの夜と同じ **画面いっぱいの乗算板** (renderQueue 4999、GrabPass 無し) + **空の `_Exposure` / `_Tint`** | RBS を入れない (ユーザー決定)。GrabPass 無しなので Quest / iOS でも描ける見込み (実機は §4 §0) |
| 覆いシェーダー | 古民家 `KD_NightOverlay.shader` を **`Assets/KominkaHillside/Shaders/KH_NightOverlay.shader`** としてコピーし、1 点だけ足す: `_VRChatMirrorMode != 0` (ミラー用カメラ) のときは描かない | ミラーの中で 1 回、主視点でミラー面にもう 1 回で **2 回暗くなる** (Codex D-1)。ミラー描画で抑制すれば、鏡の中も周囲と同じ 1 回になる。写真カメラは「写真 = 1 回 (夜として正しい)、手持ちカメラのプレビュー画面 = 主視点でもう 1 回」で、プレビューだけ濃く見える (既知。古民家と同じ)。派生物なので `Imported/` ではなく裏山側に置く |
| 値 | ON = 暗さ 1.0。**覆い単独の乗算係数**は `_Tint` (0.22, 0.26, 0.42) — `KH_NightMode.OverlayTint*` の定数が正本で Setup が材質へ毎回書き、Verify が見る (Codex M-2)。空は `_Exposure` 1 → 0.15、`_Tint` (0.5,0.5,0.5) → (0.30,0.34,0.50) で、**空は覆いと二重に沈む** (青で約 0.06 倍) | 古民家 `KD_LiveNight` の定数をそのまま。覆いの 0.22 は古民家のナイトモード 100% (0.10 倍) より明るい = 手元メニューが読める側に倒す。読めなければ `onDarkness` の既定 (コード定数) を下げる |
| 2 値 | OFF / ON のみ。Slider は置かない | ユーザー決定 (0 / 100) |
| フェード | 3 秒 (古民家 `NightInSeconds` と同じ)。`fadeSeconds <= 0` は即時。途中で押し直したらその位置から反転 | 即時切替は目に痛い |
| 値の所有者 | `onDarkness` / `fadeSeconds` は **コードが正本** (`KH_NightMode` の `public const Default*`)。Setup が毎回 public フィールドへ書く。Play 中の Inspector 調整は試行用で、確定値は定数へ戻す | フリスビーの飛行値と同じ方式。`KH_NightMode` は KH_World の外 (シーン直下) に置くが、値の保持を配置に頼らない (Codex M-4) |
| 同期・保存 | ローカル専用 (`BehaviourSyncMode.None`、ハンドラは `_` 始まり)。同期変数 0 本。PlayerData 保存は無し。退室・再入室で OFF | 各自の目で選ぶもの。保存は要件外 (Issue) |
| ライブとの合成 | `KH_NightMode.liveDarkness` (public float 0〜1) を将来のライブ側が書く。表示暗さ = **max(手動フェード × onDarkness, liveDarkness)** = 「二重に乗算せず、強い方を採る」。ライブ終了・中断・入力元の無効化で **`liveDarkness = 0` に戻すのはライブ側の契約** | ナイトモード ON の人にライブが重なっても掛け算にならない。ライブの上限が `onDarkness` を超える演目ではライブの暗さになる (Issue の文言をこれに合わせた。Codex D-2)。ライブ本体は DES-LIV-001 |
| 材質の書き先 | 覆いは `Renderer.material` の複製。空は **描画しない専用 Renderer (`SkyHolder`, enabled=false) の `.material` で複製し、`Start` で `RenderSettings.skybox` へ差す** (setter は Udon Wrapper に `__set_skybox__UnityEngineMaterial` として公開されていることを dll の文字列で確認済み) | 共有アセット `KH_Skybox.mat` を実行時に書かない (Editor Play の途中で抜けても `.mat` に差分が残らない)。古民家は共有アセットへ直接書く方式だが、裏山では複製にする。**空と覆いの実行時の書き手は `KH_NightMode` だけ** (境界) |
| メニューの置き場所 | **ヘッダー** (タイトルと [閉じる] の間) に「ナイトモード OFF」/「ナイトモード ON」の **ボタン 1 つ**。押すとラベルと色 (ON = `toggleOn`) が変わる | 今回の要件はボタン 1 つ。4 つ目のタブ「せってい」は音量など 2 つ目の個人設定が来たときに作る (Codex P-1。将来機能を今の根拠にしない)。どのタブでも見えるので迷わない |
| 古民家固有の演出 | 木と塀の Renderer を消す処理は持ち込まない | 庭の演出。裏山では木が消えると眺めが壊れる |
| `KD_LiveNight` / `KD_MenuSettings` | 使わない。`KD_LiveNight.cs` は `KD_LiveSystem` の同期時計に密結合なので新規 `KH_NightMode` を書く。`KD_MenuSettings` の `KH_WITH_SLEEPKIT` 分岐は define で死んでいるので今回は触らず、**ライブ移植 (DES-LIV-001) の実装 Issue で整理する** (Issue の文言をこれに合わせた。Codex M-5) | 移植記録に「ナイトモードは KH_NightMode が担う」と 1 行足す |

## 2. 構成

```
Assets/KominkaHillside/
├── Materials/KH_Skybox.mat                       空 (RenderSettings.skybox)
├── Materials/KH_NightOverlay.mat                  覆い (KH_NightOverlay shader、queue 4999)
├── Materials/KH_FullscreenQuad.asset              画面いっぱいの板メッシュ (bounds 200000)
├── Shaders/KH_NightOverlay.shader                 古民家 KD_NightOverlay + ミラー抑制
├── Scripts/KH_NightMode.cs (+ .asset)             ナイトモード本体 (U#, None)
└── Scripts/Editor/KH_NightModeEditor.cs           Setup / Verify

シーン直下 (KH_World の外 — Terrain Build が KH_World を作り直すため)
KH_NightMode                 KH_NightMode (U#)
├── Overlay                  MeshFilter (KH_FullscreenQuad) + MeshRenderer (KH_NightOverlay.mat)。enabled=false で開始
└── SkyHolder                MeshRenderer (KH_Skybox.mat、enabled=false)。空の材質の複製元
```

### KH_NightMode (U#)

```csharp
public Renderer overlayRenderer;   // Overlay
public Renderer skyHolder;         // SkyHolder (描画しない。material で複製)
public float onDarkness = DefaultOnDarkness;   // ON のときの暗さ 0〜1 (Setup が定数から毎回書く)
public float fadeSeconds = DefaultFadeSeconds; // <= 0 は即時
public float liveDarkness;         // 将来のライブが書く 0〜1 (ライブ側が 0 へ戻す契約)
public bool IsOn { get; }          // メニュー表示用 (目標状態)
public float Darkness { get; }     // max(clamp01(_phase) * clamp01(onDarkness), clamp01(liveDarkness))
public void _Toggle();             // ローカル
public const float DaySkyExposure = 1f, NightSkyExposure = 0.15f;   // KD_LiveNight と同値
public const float DayTintR/G/B = 0.5f, NightTintR = 0.30f, NightTintG = 0.34f, NightTintB = 0.50f, SkyTintA = 0.5f;
public const int OverlayQueue = 4999;
```

- `Start`: 未配線は `Debug.LogError("[KH_NightMode] …")` で止まる (何もしない)。`_overlayMat = overlayRenderer.material`、`_overlayMat.renderQueue = OverlayQueue`、`_skyMat = skyHolder.material`、`RenderSettings.skybox = _skyMat`、`overlayRenderer.enabled = false`、`Apply(Darkness)` (通常 0。`liveDarkness` が先に書かれていればその値)。
- `Update`: `_phase` を目標 (ON=1 / OFF=0) へ `Time.deltaTime / fadeSeconds` で寄せる。`Darkness` が前回と変わったときだけ `Apply` (静止中は早期 return)。覆いは `_Darkness` を書き、暗さ ≈ 0 で `enabled=false`。空は `_Exposure` と `_Tint` を lerp。メニューを閉じてもフェードは続く (Update は `KH_NightMode` 側)。

### KH_HandMenu の変更

- `public KH_NightMode night; public Image nightImage; public TextMeshProUGUI nightLabel;`
- `_Night()` → `night._Toggle()`; `Draw()`。`Draw()` の共通部で `DrawNight()`: 未配線なら disabled 色 + 「ナイトモード」、それ以外は ON なら `toggleOn` + 「ナイトモード: ON」、OFF なら `actionNeutral` + 「ナイトモード: OFF」(コロン付き = 今の状態の表示。Codex U-2)。
- Build: ヘッダーに `Night` ボタン (x 290, w 250, h 44)。`night` は `Night/1 Setup` が作った `KH_NightMode` を `FindObjectOfType` で配線 (無ければ null のまま = disabled 表示)。`ExpectedEvent` に `Night → _Night`。Verify: ボタン 46 → **47** (パネル 46 + ランチャー 1)、`night` 配線、`nightImage` / `nightLabel` の backing 参照。
- 文字集合: **runtime のリテラルは `Charset()` が自動で拾うが、Editor 側の定数は `Charset()` に `Append` を足さないと入らない** (Codex M-2)。ボタン文言の正本は runtime (`KH_HandMenu.cs`) のリテラル。Build が置く初期文言 `WordNight` は同じ文字列の写し (毎 Draw で上書きされる。幅検査のために最長の OFF 表記)。文言を変えるときは両方直す。

### Editor メニュー (`Tools/KominkaHillside/Night/`)

| # | 名前 | すること |
|---|---|---|
| 1 | Setup Sky And Night | `.gitignore` は手で (Setup は触らない)。`KH_Skybox.mat` (無ければ作成し `_Rotation` 180。毎回: shader / `_Tex` / 昼の `_Exposure` `_Tint`) → `RenderSettings.skybox` → キューブマップの Android / iPhone Override 1024 (`SetPlatformTextureSettings` + 再インポート) → `KH_NightOverlay.shader` の存在確認 → `KH_NightOverlay.mat` (queue 4999) → `KH_FullscreenQuad.asset` → シーン直下に `KH_NightMode` / Overlay / SkyHolder → U# 配線 + `onDarkness` `fadeSeconds` を定数から (`CopyProxyToUdon`、SyncMethod None を実物で是正) → 保存 |
| 2 | Verify Sky And Night | 空: `RenderSettings.skybox` のパス・shader・`_Tex` GUID・`_Exposure` 1・`_Tint` 昼・Override (`overridden` && 1024) ×2 / 覆い: shader 名・queue・メッシュ名と bounds・enabled=false / SkyHolder: 材質 = 空・enabled=false / U#: backing の `overlayRenderer` `skyHolder` 参照、`onDarkness` `fadeSeconds` = 定数、SyncMethod None、`liveDarkness` 0 / `KH_HandMenu.night` 配線 / `KH_NightMode` がシーン直下 / NetworkIDs の `fileID: 0` 残骸 0 |

順序: `Night/1 Setup` (U# 新規なので初回は Program Asset 作成 → refresh → 再実行) → `Menu/2 Ensure Glyphs` → `Menu/3 Build Menu` (night を配線) → `Menu/4 Verify` → `Night/2 Verify` → `game-design.md` / `world-ui-spec.md` / 移植記録の更新 → Codex (D/M/U) → Play。

### 描画順と暗くなる対象 (Codex D-3)

| 対象 | queue | ナイトモード ON で |
|---|---|---|
| Terrain・木・地面・犬・アバター | ≤ 3000 | 暗くなる |
| 手元メニュー (UI)・看板の TMP | 4000 (Transparent+1000) | 暗くなる (覆い 0.22〜0.42 倍) |
| 空 | Background | 暗くなる + 露出でさらに沈む |
| フリスビーの軌道線・着地マーカー | 要確認 (Play 5) | 3000 前後なら暗くなる |
| ライブの粒 (将来) | 5000 | **暗くならない (意図)** |

## 3. 実装記録 (2026-09-21)

- 追加: `Scripts/KH_NightMode.cs` (+ Program Asset)、`Scripts/Editor/KH_NightModeEditor.cs`、`Shaders/KH_NightOverlay.shader` (古民家 `KD_NightOverlay` + `_VRChatMirrorMode` で discard)、`Materials/KH_Skybox.mat` / `KH_NightOverlay.mat` / `KH_FullscreenQuad.asset`。変更: `KH_HandMenu.cs` (`night` / `nightImage` / `nightLabel`、`_Night`、`DrawNight`)、`KH_HandMenuEditor.cs` (ヘッダーの `Night` ボタン、`ExpectedEvent`、Verify 47 ボタン + night 配線)、`.gitignore` (`/Assets/Allsky/` 2 行)、シーン (`RenderSettings.skybox`、シーン直下 `KH_NightMode`)。
- メニュー: `Tools/KominkaHillside/Night/` 1 Setup Sky And Night (初回は Program Asset を作って止まる → 再実行) → 2 Verify Sky And Night。Setup 後に `Menu/2 Ensure Glyphs` → `Menu/3 Build Menu` → `Menu/4 Verify Menu`。
- Editor 検証: C# / U# (81 スクリプト) コンパイル成功、Console エラー 0、シェーダーエラー 0。`Menu/4 Verify` **25 項目 失敗 0**、`Night/2 Verify` **32 項目 失敗 0** (実装レビュー §6 の反映後。反映前は 24 / 23)。保存済みファイルの独立確認: シーンの `m_SkyboxMaterial` = `KH_Skybox.mat` の GUID 1 件、`KH_NightMode` 1 件、`_Night` の onClick 1 件、NetworkIDs の `fileID: 0` 0 件、`.png.meta` の Android / iPhone `overridden: 1` + 1024、Standalone は 2048 のまま。`KH_Skybox.mat` は `_Tex` GUID `34019144…`・`_Exposure` 1・`_Rotation` 180・`_Tint` 0.5。Ensure Glyphs は「追加なし (290 字)」= 新語の字形は既存。
- `KH_NightOverlay.mat` の `m_CustomRenderQueue` は −1 (shader の `Transparent+1999` = 4999 がそのまま効く。Verify は材質の `renderQueue` 4999 を見る。実行時は複製にも 4999 を書く)。
- レビュー反映で足した検査: root / Overlay / SkyHolder の GameObject が active・backing Udon と proxy が enabled、Program Asset の `sourceCsScript` と backing の `programSource`、Quad の頂点 (xy ±1) と bounds (xyz)、SkyHolder の MeshFilter、Transform の identity、影・プローブ off、覆いの `_Tint` = 定数、backing 側の `onDarkness` / `fadeSeconds` / `liveDarkness`、メニュー backing の `night` (相手の backing UdonBehaviour として保持される) / `nightImage` / `nightLabel`。
- 未実施: Play・PCVR・Quest/iOS (§4)。

## 4. Play チェックリスト (ユーザー)

### §0 エディタで判定できない項目

- 覆いが **主視点で 1 回だけ** 掛かる (Quest の両目 / VRChat の写真カメラ / ミラーが将来置かれたとき鏡の中が周囲と同じ暗さ)。ClientSim には VRChat のカメラ・ミラーが無い
- Quest / iOS で暗くなり、ON のまま手元メニューが読めて OFF に戻せる (GrabPass 非依存の根拠はシェーダーだけ。実機の目視が証拠)
- 空の太陽が「月」として残る見え方、`_Rotation` 180 の太陽の方角が裏山の眺めに合うか
- 覆いの負荷 (全画面 1 枚の乗算)。同じ視点・犬の状態で OFF / フェード中 / ON の fps を比べる

### 1 人で (PCVR / Desktop)

1. 入った直後: 空が古民家と同じ (青空・太陽)。地面・木・古民家の色味に違和感が無いか (環境光は変えていない)
2. 手元メニューのヘッダー「ナイトモード: OFF」を押す: 3 秒で暗くなる。ボタンが金色、ラベルが「ナイトモード: ON」。初見で「OFF」が今の状態だと読めるか。押した直後にメニューを閉じてもフェードが最後まで進む
3. ON のまま: 手元メニューの文字・看板・犬が読める / 見える。空が暗くなり太陽が月に見える
3b. **ON → メニューを閉じる → 木陰を歩く (犬の顔・道の縁・遠くの古民家が判別できるか) → メニューを開き直す → OFF に戻せる** (Codex U-1。厳しければ `DefaultOnDarkness` を下げる)
4. もう一度押す: 3 秒で戻る。空も戻る。フェード途中で押し直すとその位置から反転する
5. ON のまま Terrain の端・遠景・木・フリスビーの軌道線を見る: 上の表と違う層が無いか
6. ON のまま VRChat の写真カメラ: **保存した写真**は夜として見られる (犬の表情が分かる)。手持ちカメラのプレビュー画面だけ濃い (既知) が、構図と犬の表情は判断できるか (Codex U-3)
7. Desktop (T キーでメニュー) でも同じ
8. Play 中に `KH_NightMode` の Inspector で `liveDarkness` を 0 → 1 → 0 (ライブの模擬): OFF の人は 1 で暗くなり 0 で昼へ、ON の人は 1 でも変わらず 0 で ON のまま
9. Play を ON の途中で止めた後、`KH_Skybox.mat` に差分が出ていない (`git status`)

### 2 人で

10. A が ON、B は OFF: B の画面は変わらない (ローカル専用)。A が ON のまま退室しても B は変わらない
11. A が ON のまま退室・再入室: OFF に戻っている (保存しない仕様)。B が ON のところへ C が入室: C は OFF

### Quest / iOS

12. ON で暗くなる。フレームレートが落ちない。ON のままメニューが読めて OFF に戻せる (3b を Quest / スマホでも)

| 症状 | 見るところ |
|---|---|
| 押しても暗くならない | Console の `[KH_NightMode]` エラー (未配線)。`Night/2 Verify` |
| 空だけ暗くならない | `RenderSettings.skybox` が複製ではなく元の `.mat` のまま (`Start` が走っていない) |
| メニューが暗すぎて読めない | `KH_NightMode.DefaultOnDarkness` を 0.8 などへ → `Night/1 Setup`。覆いの `_Tint` も候補 |
| 一部だけ暗い / 二重に暗い | 覆いの bounds (Verify) / 別のカメラの Culling Mask / ミラー用カメラで `_VRChatMirrorMode` が効いていない |
| Quest で暗くならない | シェーダーが Android で落ちている: ビルドログの shader error |

## 5. Codex レビュー 1 回目 (設計、P/M/D、2026-09-21) の指摘 / 対応

| 指摘 | 対応 |
|---|---|
| P-1 中: 4 つ目のタブの根拠が弱い。ヘッダーのトグル 1 個が小さい | **採用**: ヘッダーのボタン 1 つに変更。タブは 2 つ目の個人設定が来たとき |
| P-2 中: ライブ合成と `game-design.md` 反映が手順・チェックリストに無い | **採用**: Play 8 (Inspector で `liveDarkness` を模擬)、順序に正本更新を追加 |
| M-1 中: ボタン数 47 は誤り (タブ案なら 48) | ヘッダー案なので **47** (パネル 46 + ランチャー 1) に確定 |
| M-2 中: Editor 定数は Charset に自動で入らない | **採用**: 文言は runtime リテラルに置く。設計書に書き分けを明記 |
| M-3 中: `Draw()` の 4 分岐が抜けている | ヘッダー案で不要。`DrawNight()` は共通部で呼ぶ |
| M-4 高: Inspector 正本の値が再生成される階層にある | **採用**: シーン直下に置き、`onDarkness` / `fadeSeconds` はコード定数が正本で Setup が毎回書く。空の昼の値も Setup が毎回書き、`_Rotation` だけ新規時 |
| M-5 中: SleepKit 分岐の整理時期が Issue と逆 | **採用**: Issue を「ライブ移植の実装 Issue で整理」に修正。書き手の境界を明記 |
| D-1 高: ミラーで 2 回暗くなる | **採用**: シェーダーを裏山側にコピーし `_VRChatMirrorMode != 0` で描かない。写真プレビューの濃さは既知として記録 |
| D-2 中: max 合成は「さらに暗くしない」と矛盾しうる | **採用**: 仕様を「強い方を採る」に言い換え、Issue も修正。`liveDarkness = 0` に戻す契約を明記 |
| D-3 中: 0.22 は覆い単独の係数 | **採用**: 文言修正。描画順の表を追加 |
| D-4 中: `RenderSettings.skybox` setter の公開未確認 | Wrapper dll の文字列で **公開を確認**。反射・環境光は昼のまま (更新しない) と明記 |
| D-5 中: Verify・端末検証が正常系に偏る | **採用**: `fadeSeconds <= 0` 即時、途中反転、メニューを閉じてもフェード継続、途中入室 OFF、Quest で OFF に戻せる、fps 比較を追加 |

## 6. Codex レビュー 2 回目 (実装、D/M/U、2026-09-21) の指摘 / 対応

高は無し。指摘と対応:

| 指摘 | 対応 |
|---|---|
| D-1 中: root / Overlay の GameObject が非アクティブでも Verify が通る | **採用**: Setup が root と子を `SetActive(true)`、backing Udon と proxy の `enabled` を是正。Verify に active・enabled を追加 |
| M-1 中: 設計書が約束した backing 側の検査 (メニューの `night` / `nightImage` / `nightLabel`、Night の数値 3 つ) が無い。Transform・影/プローブ・Quad の中身・SkyHolder の MeshFilter・Program Asset も未検査。`_Darkness` が負でも合格 | **採用**: すべて Verify に追加 (§3)。最初の実装で `night` の backing 比較が proxy と一致せず FAIL → backing heap の U# 参照は相手の backing UdonBehaviour で保持されるため、`GetBackingUdonBehaviour(menu.night)` と比較して PASS。`_Darkness` は絶対値比較 |
| M-2 低: 覆いの `_Tint` の所有者が曖昧 | **採用**: `KH_NightMode.OverlayTint*` の定数を正本にし、Setup が毎回書き Verify が見る |
| M-3 低: Start の `Apply(0)` 表記、`WordNight` の写し | **採用**: 設計書を実装に合わせ、写しであることと両方直す旨を明記 |
| M-4 低: §6 が無いのに参照している | **採用**: この節 |
| U-1 中 (受入確認): 夜のまま木陰を歩いて戻れるかは未判定 | **採用**: チェックリスト 3b を端末ごとに追加 |
| U-2 低: 「ナイトモード OFF」が状態か操作か曖昧 | **採用**: 「ナイトモード: OFF / ON」(コロン付き) に変更。金色が ON。初見の読みはチェックリスト 2 で確認 |
| U-3 低: 写真プレビューだけ濃いと撮影を諦めるかも | **採用 (確認項目として)**: チェックリスト 6 を保存写真の比較と構図判断に強化。描画方式は変えない |
| D 問題なし: U# の API (getter 越し読み、MoveTowards、.material、skybox setter、renderQueue)、Start 順序、`_applied` 初期値、フェード境界、シェーダーの両目・ミラー、Importer、NetworkIDs、ローカル専用 | — |

## 7. オート／マニュアル (IMP-ENV-005、2026-10-02 実装・Editor 検査済み・ClientSim と Play は未)

§1〜6 は IMP-ENV-002 の初期仕様 (OFF/ON・保存なし・ヘッダーのボタン) の記録。現在の入口は設定面のスライダー (IMP-UI-001) で、そこへ [オート][マニュアル] を足した。作業票は [IMP-ENV-005](../development/issues/IMP-ENV-005-auto-night-mode.md) (推奨案 R をユーザーが採用、2026-10-02)。描画 (覆い + 空) と書き手 (`KH_NightMode` だけ) は変えていない。

| 項目 | 決定 |
|---|---|
| オート | インスタンス共通の時計。開始時刻 (サーバー ms) = 開始を書いた時刻 (最初の参加者の入室の約 2 s 後。後継の Owner が始めた場合はその時刻) から、昼 10 分 (0) → 夕暮れ 30 分で smoothstep 0 → 0.8 → 夜で止める (循環しない)。値は仮値で、`KH_AutoNight.Default*` が正本 (Night/1 が毎回書き、Night/2 が見る) |
| 同期 | 新規 `KH_AutoNight` (`KH_NightMode/AutoNight`、Manual、同期変数は `_epochMs` 1 本)。Owner は既定のまま (SetOwner を呼ばない。Night/2 がソースを静的に検査)。Owner が値 0 のときだけ 1 回書く。0 は「未開始」と「未受信」の両方なので、書けるのは 0 を信用できるときだけ (関門): (a) 自分の入室時に Owner で他に誰もいない (最初の参加者)、(b) この器の状態を受信した (`OnDeserialization`)、(c) `Networking.IsNetworkSettled` (SDK 3.10.5 で Udon に公開)、(d) 関門が 120 s 閉じたまま (最後の手段)。入室の 2 s 後・Owner を受け取った 5 s 後・5 s ごとの再試行のどれも同じ関門を通る。未受信の新 Owner は待ち、前の Owner の値が届けばそれを保つ。以後は書き換えないので、入室・モード切替で巻き戻らず、Owner 交代・退出でも状態が届く限り巻き戻らない (届かないまま 120 s たった場合だけ新しく始まる)。暗さ・進み・誰がオートかは同期しない (各自が `f(サーバー時刻 − _epochMs)` を計算) |
| 途中参加 | 値を受け取るまで昼 (0)。受け取ったら 3 s のフェードで合流 (オート中に 1 段で 0.05 を超えて変わったらフェード) |
| マニュアル | 既存のスライダー。自分だけ。共通の時計に触らない |
| モード | 個人単位。初回 (保存なし) はオート。オート中のスライダーは共通の値を表示し Fill を淡色にする。触るとマニュアルへ切り替わりその値になる。[マニュアル] は自分の保存値 (初回は OFF) へ戻る。[オート] は時計を変えずに共通の値へ戻る |
| 保存 | モード = PlayerData `KH_NightMode.mode` (0 オート / 1 マニュアル、無ければオート)。マニュアル値 = 既存の `KH_NightMode.dark` のまま。オートの値では書かない。復元前の操作はキーごとに優先 (モードだけ先に押してもマニュアル値は上書きしない) |
| フェード | 表示値を 3 s (`fadeSeconds`) で寄せる: モード切替・OFF⇄ON・復元・途中参加の受信。マニュアルで ON のままのスライダー操作は従来どおり即時 |
| ライブ | 表示 = max(オート or マニュアル, `liveDarkness`)。強い方を採る契約は同じ。ライブが終わると 0 ではなくオート／マニュアルの値へ戻る。オートの夜 (0.8) がライブの上限より暗いとライブの暗転は見えない (ライブ中だけオートを止める案は、夜にいた人をライブ開始で明るくするので採らない) |
| 毎フレーム | `KH_AutoNight` は Update を持たない (1 s ごとの遅延イベントで再計算し、値が変わったときだけ `autoDarkness` を書く)。`KH_NightMode.Update` は従来どおり毎フレームの比較数回で、材質を書くのはフェード中と、夕暮れの間の 1 s に 1 回だけ |
| 犬 | 変えない (game-design §4「夜の散歩」) |
| 確認 (2026-10-02) | コンパイル OK。Night/1 (2 回)・Menu/15・Menu/19b を実行し、Night/2 46/46、Menu/15a 41 PASS / 0 FAIL、Menu/22 9363 PASS、シーン保存。独立レビューの指摘 (未受信の新 Owner が時計を書き直す) を関門で修正 (再コンパイル待ち)。ClientSim Course/83c: 未実行。Play・実機: 未 |
| 既知の限界 | サーバーの ms は 2^32 ms (約 49.7 日) で一周する。差は約 24.8 日まで正しく、それを超えると夜として扱うが、約 49.7 日続いたインスタンスでは再び昼に読める |

設定ホームのナイトモードの行: 題字 (-330) / スライダー (-60) / 値 (150) / [オート] (290) / [マニュアル] (455)、幅 150・高さ 44。選択中は `pouch.equipOnColor`、他方は `tabOffColor`。注記は同じ位置に 2 行 (`NightModeAutoNote` = `settings.night.autoNote` / `NightModeNote` = `settings.night.note`) を置き、モードで片方だけ出す。文言キー 3 つ (`settings.night.auto` / `manual` / `autoNote`) を 13 言語で追加し、束縛 3 件を `UILocalizationBindings.json` に足した。

Editor: `Night/1 Setup` が `KH_AutoNight` の Program Asset (無ければ作って止まる) → `AutoNight` の子と値を作る。`Menu/15 Setup Night Mode` (`KD_MenuFinishEditor.BuildNightMode`。Menu/11 も同じ本体を通る) が行・ボタン・注記と配線を作る。検査は `Night/2` (AutoNight の同期方式・値・NetworkID・同期変数 1 本・SetOwner 無し)、`Menu/15a` (ボタンの配線・束縛・オートの見た目で構築)、Verify 22 (群の並び)。ClientSim の部分確認は Course/83c ([ハーネス](../testing/kh-frisbee-harness.md))。

### Play チェックリスト (IMP-ENV-005)

- 1 人: 設定を開くと [オート] が選択中、注記がオートの文。スライダーは淡色で数字は 0%。昼のまま 10 分 → 30 分で暗くなり 0.8 で止まる (Inspector で `KH_AutoNight` の `daySeconds` / `duskSeconds` を縮めて早回ししてよい)。スライダーを触るとマニュアルへ切り替わる。[オート] で 3 s かけて共通の暗さへ戻る。夜のまま道・犬・メニューが読めるか
- 再入室: マニュアルで退室 → 再入室でマニュアルと値が戻る。オートで退室 → 再入室でオート (保存したマニュアル値は残っている)
- 2 人: A・B ともオートで同じ暗さ。A がマニュアルにしても B は変わらない。夜になってから C が入る → C は昼から 3 s で同じ暗さへ。最初に入った人 (Owner) が退室しても暗さが巻き戻らない (入ったばかりの人が Owner を受け取る順も試す)
- ライブ: オートの夜にライブを始める・終える → ライブ中は強い方、終了後はオートの値へ戻る (0 にならない)
- Quest 単体 / iOS: 上の 1 人の項目と、夜のメニューの読みやすさ・fps を分けて記録

### パレードの灯り006（2026-10-15）

ユーザー指定で開演中を薄暮化。KH_NightModeの表示値はmax(オート/マニュアルの表示値, liveDarkness, paradeDarkness)。パレードPlaybackだけが専用paradeDarknessを書き、共有Elapsedで開始4秒・終端5秒、最大0.72へフェードする。停止/Disableでは専用入力のみ0に戻す。手動値やPlayerData、Autoの時計、通常live入力を変更しない。通常Emissionも暗くする覆い4999の後に、パレード専用の深度判定付き発光を5000で描画。限定VM結果とビジュアル証拠は[統合制作票](halloween-parade/production.md)。この結果を上記Auto全項目や端末の合格へ読み替えない。
