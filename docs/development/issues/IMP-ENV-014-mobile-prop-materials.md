---
id: IMP-ENV-014
type: IMP
system: ENV
status: review
priority: High
summary: Android/iOS ビルドだけ、カート・洗い道具・小物の Unity Standard 材質を VRChat/Mobile/Standard Lite の複製へ差し替える (地形 Lite と同じビルドフック)。PC の見た目は変えない
next: "複製 7 件・Verify PASS・ビルドフック実装済み (2026-10-12)。ユーザーが Android ビルドし、ビルドログ Temp/kh-mobile-materials-build.txt の replaced / remainingTargets=0 と、Quest での見た目 (光りすぎなら ExpHighlights を 0 に) と fps を確認"
updated: 2026-10-12
---

# IMP-ENV-014 Quest/iOS だけ小物の材質を Mobile Standard Lite へ

2026-10-12 ユーザー決定「2, 3 でお願い」([VER-ENV-002](VER-ENV-002-scenery-performance.md) の候補 2)。乗車中は画面の大半がカートで、洗い場では道具と台が大きく映る。これらは Unity `Standard` のまま Quest に乗っている (Android のビルドフックは地形しか差し替えない。[描画側の監査](../../plans/2026-10-12-performance-inventory-scene.md) §2)。

## 対象 (有効な Renderer、2026-10-12 census)

| 材質 | Renderer | 使う所 | マップ |
|---|---|---|---|
| `KH_OsanpoCart_Body` / `_Sign` | 78 (乗用 8 + 見本 5、LOD0/1) | カート | BaseColor のみ。Metallic 0、Gloss 0.25、反射・ハイライト ON |
| `KH_WashTools` | 19 | 洗い道具 | BaseColor + MetallicGloss |
| `KH_PropFinish` | 15 | 棚・リード掛け・写真ボード・ボトル | BaseColor + Normal + MetallicGloss (2048、Android 1024) |
| `PremiumWash_Fixture` | 8 (うち 4 は支柱の SkinnedMesh) | 有料の台・器具 | Normal + Metallic |
| `PremiumWash_Tools` / `Addons` / `Window` / `Water` | 待機中 0 (選ぶと出る) | 有料道具 | 要確認 |

## 方針

- PC の見た目・材質は変えない。Android/iOS のビルド時のシーンのコピーだけ差し替える (`KH_MobileTerrainBuildEditor` と同じ `IProcessSceneWithReport`、対象は材質の GUID で判定)。
- 複製材質 `Assets/KominkaHillside/Materials/Mobile/<元の名前>_Mobile.mat` は Editor メニューで作る (冪等)。シェーダー `VRChat/Mobile/Standard Lite`。テクスチャ (BaseColor / Normal / MetallicGloss) は元のものを割り当て、カートのように BaseColor しか無いものは反射とハイライトを OFF、ノーマル・金属マップがあるものは残す (見た目を保つ)。色・タイリング・emission は元の値をコピー。
- スクリプトが Material を参照して実行時に差し替える箇所 (UdonBehaviour の public 変数) があれば、フックでその参照も複製へ書き換える (grep で洗い出す)。
- Editor の確認用に toggle プレビュー (Mobile/11 と同じ型。保存しない) と Verify (差し替え対象の数、複製のシェーダー・テクスチャ、フックのログ `Temp/kh-mobile-materials-build.txt`)。

## 完了条件

- [ ] Android ビルドのログで対象材質の Renderer が全部差し替わる (PC ビルド・Editor は元のまま)
- [ ] Editor のプレビュー画像 (Standard と Lite) を残し、Quest 実機でユーザーが見た目を許容
- [ ] Quest 実機の fps (乗車中・洗い場) をユーザーが読む ([VER-ENV-002](VER-ENV-002-scenery-performance.md) に記録)

## 記録

2026-10-12: 起票。実装は Opus (統括 Fable)。

2026-10-12: Editor コードを実装 (Opus、Unity 未実行・未コンパイル)。`Assets/KominkaHillside/Scripts/Editor/KH_MobilePropMaterialsEditor.cs` の 1 ファイル。
- メニュー: `Tools/KominkaHillside/Mobile/13 - Create Mobile Prop Materials` (複製を作る・同期する。冪等。ログ `Temp/kh-mobile-materials-create.txt`)、`14 - Verify Mobile Prop Materials` (読むだけ。`Temp/kh-mobile-materials-verify.txt` の末尾に `RESULT PASS/FAIL`)、`15 - Preview Mobile Prop Materials (toggle)` (開いているシーンの Renderer を元 ⇄ 複製で入れ替える。Undo 付きでシーンを dirty にする。**ON のまま保存しない**。OFF 後もプレハブの上書きが残りうるので、保存せずにシーンを開き直す。ログ `Temp/kh-mobile-materials-preview.txt`)。
- 対象: 固定 5 件 (Cart Body / Sign、KH_WashTools、KH_PropFinish、PremiumWash_Fixture) + `Models/WashPremium/PremiumWash_*.mat`。Unity `Standard` かつ Opaque だけ。`PremiumWash_Water` / `_Window` は Fade (`_Mode` 2) なので除外 (Lite は不透明だけ)。今の対象は 7 件。複製は `Assets/KominkaHillside/Materials/Mobile/<名前>_Mobile.mat`。
- 値の写し方 (Lite のソース `Packages/com.vrchat.base/Runtime/VRCSDK/Sample Assets/Shaders/Mobile/VRChat-Mobile-StandardLite.shader`): Lite は常に metallic = 金属マップ.r × `_Metallic`、smoothness = マップ.a × `_Glossiness` (surf 154-155 行)。Standard は金属マップがあると `_Metallic`/`_Glossiness` を使わず map.r と map.a × `_GlossMapScale` なので、マップありは `_Metallic` 1・`_Glossiness` = `_GlossMapScale`、無しは元の値。Lite は頂点色を掛ける (147-148 行、キーワードは 68 行) ので `DISABLE_VERTEX_COLORING` を ON (Standard は頂点色を使わない)。`[ToggleOff]` の 2 つは値とキーワード (`_SPECULARHIGHLIGHTS_OFF` / `_GLOSSYREFLECTIONS_OFF`、61-62 行) を両方書く。BaseColor だけの材質 (カート) は両方 OFF、ノーマル・金属マップがあるものは元のまま。`_EnableGeometricSpecularAA` は 0 (Standard に無い。安い側)。テクスチャ・タイリング・`_Color`・`_BumpScale`・Occlusion・emission (ON の時だけ)・GPU Instancing を写す。
- ビルドフック: `IProcessSceneWithReport`、`callbackOrder` 1100 (UdonSharp の `[PostProcessScene]` 0 と地形 1000 の後)。Android/iOS のビルド中かつ `KH_World` があるシーンだけ (地形フックと同じ条件)。全 Renderer (非アクティブ・SkinnedMesh を含む) の `sharedMaterials` を差し替え、UdonBehaviour の公開変数 (Material / Material[]) も差し替える。複製が無い・古い (元とテクスチャや値が食い違う) と `BuildFailedException` で止まり「13 を実行」と出る。ログ `Temp/kh-mobile-materials-build.txt` (`replaced=N remainingTargets=0 udonRemapped=N perMaterial=[...]`)。
- 実行時に Material を差すスクリプト: `public Material` は KH_DogWash.maskMaterial、KH_Frisbee の 4 つ、KH_LeashClip の 2 つ、KD_LiveNight.skyMaterial。どれも対象の材質ではない見込み (14 が件数を出す)。
- 統括の実行順: コンパイル → **13** → **14** (PASS を確認) → 任意で **15** を 2 回 (ON で見比べ・画像、OFF。保存せずシーンを開き直す) → Android ビルド → `Temp/kh-mobile-materials-build.txt` で `remainingTargets=0`。新しい `.mat` と `.meta`、`Materials/Mobile.meta` をコミットする。
- 注意: 固定 5 件のどれかが Standard / Opaque でなくなっても Android ビルドは止まる (意図した大声)。Lite の `_SpecularHighlights` は「Specular Lightprobe Hack」(プローブを光として鏡面を出す) なので、ハイライトを残した WashTools / PropFinish / Premium は Quest で PC より光って見えうる。15 で見比べ、強すぎれば `ExpHighlights` を全件 0 にする (1 行)。

2026-10-12 (統括): C# コンパイル 0。Mobile/13 を実行: 複製 7 件 (Cart Body / Sign は反射・ハイライト OFF、WashTools / PropFinish / PremiumWash_Fixture / Addons / Tools はノーマル・金属マップを維持、Water / Window は Fade なので対象外)。Mobile/14 PASS (対象スロット 210: Body 78 / Sign 26 / PropFinish 15 / WashTools 23 / Addons 20 / Fixture 13 / Tools 35、public Material の参照に対象なし)。Mobile/15 のプレビューはシーンを汚す (別セッションの未保存差分と同居) ので実行していない。見た目の確認は Quest の実ビルドでユーザーが行う (フックは Android/iOS ビルドだけで動く)。
- 2026-10-12 Codex レビュー反映: `Diff` (14 とビルドフックの古さ判定) が `ApplyMobile` の書く値を全部比べるよう、両者を 1 つの `SpecFor` (テクスチャ・ST・float・色・キーワード・Instancing・doubleSidedGI・rawRenderQueue・GI フラグ) から作る形に。プレビュー OFF と 14 の「シーンに複製が残っていないか」は、今の適格性に関係なく既存の複製すべてを元へ戻す・数える。

2026-10-12 (統括、レビュー後): Codex 指摘 (中: Diff の比較漏れ、低: プレビュー OFF の逆引き) を反映。`Material.rawRenderQueue` は Unity 2022.3 に無いので `m_CustomRenderQueue` を SerializedObject で読む形に統括が修正。C# 0、Mobile/14 PASS (再実行)。Mobile/15 プレビューは未実行 (シーンを汚すため)。
