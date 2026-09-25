---
id: IMP-ENV-002
type: IMP
system: ENV
status: done
priority: Medium
summary: 空を古民家と同じキューブマップにし、手元メニューのヘッダーに OFF / ON のナイトモード (RBS 不使用、乗算オーバーレイ + 空の露出)
updated: 2026-09-22
---

# IMP-ENV-002 空を古民家と合わせ、手元メニューのナイトモード (OFF / 100%) を自前の暗転で作る

## 要望

2026-09-20 ユーザー要望: 「スカイボックスを古民家ドッグランの方と合わせたい。ナイトモードもメニューからできるようにしたい。ただ RBS は入れないので、自前のパーティクルライブの時に暗くなる方式でいい。あのメニューのナイトモード、0 / 100 でナイトモードできるようにしておきたい」。

2026-09-21: 設計 → Codex レビュー → 実装 → Codex レビューまで完了。Editor 検査 (Menu 25/25・Night 32/32) 済み。**2026-09-22 に PCVR で確認済み、Quest / iOS は未確認** ([計画書 §4](../../plans/2026-09-21-sky-and-night-mode.md) のチェックリスト)。

## 現状 (2026-09-20 調査)

| 項目 | 古民家ドッグラン | 裏山 |
|------|-----------------|------|
| 空 | `KD_Skybox.mat` = `Skybox/Cubemap`、`_Tex` は購入アセット Allsky `Day Sun High Clear` (2048² × 6 面。Android / iOS は 1024 へ Override、`.gitignore` で除外) | VCC テンプレートの `SKY_UdonLab.mat` のまま |
| ナイトモード | RBS SleepKit2 (`NightModeManager`、GrabPass なので Quest では描かれない) + 手元メニューの Slider 0〜100% | 無し (`KD_MenuSettings` の SleepKit 参照は `KH_WITH_SLEEPKIT` で切ってあり、既定では参照しない) |
| ライブ中の夜 | `KD_LiveNight` が画面いっぱいの乗算板 `KD_NightOverlay` (renderQueue 4999、GrabPass 無し) と空の `_Exposure` 1.0 → 0.15 / `_Tint` 青寄りを、同期時計から導いた暗さ 0〜1 で駆動 | `KD_LiveNight.cs` は移植済みだが、`KD_NightOverlay.shader` / `.mat`・全画面メッシュ・空は未移植で未配置 |

古民家は「空を差し替えず `_Exposure` で沈める」方式 (IMP-ENV-010、ビルドサイズ対策) なので、**空を揃えること自体がナイトモードの前提になる** (同じ材質を暗くするだけで済む)。

## A. 空を古民家と合わせる

- 古民家の `KD_Skybox.mat` と同じ設定 (`Skybox/Cubemap`、`_Tint` 0.5、`_Exposure` 1) の材質を `Assets/KominkaHillside/` 側に `KH_Skybox.mat` として持ち、`RenderSettings.skybox` を差し替える。
- キューブマップは購入アセットなので `Assets/Allsky/` を Git 管理外にして取り込む (古民家と同じ除外)。Android / iOS の `maxTextureSize: 1024` Override も同じにする。
- ライティング (環境光・太陽の向き・霧) は空を替えてから見て決める。古民家と同じにすることは要件にしない。秋の裏山の見え方 (`core-concept` のビジュアル方針) を崩さないことを優先する。
- 空を替えた後、古民家の眺め・散歩道・犬の視認性を PCVR で一度見る。

## B. ナイトモード (OFF / 100%) を手元メニューから

- **RBS SleepKit は入れない。** 古民家のライブの夜と同じ方式 (乗算オーバーレイ + 空の `_Exposure` / `_Tint`) をそのまま使う。GrabPass を使わないので Quest / iOS でも描ける。
- 手元メニュー (`KH_HandMenu`) のヘッダーに **OFF / ON の 2 値** (0% / 100%) のボタンを 1 つ置く。古民家の Slider のような中間値は持たない。ON 時の暗さは古民家のナイトモード 100% (明るさ 0.4、pow 2.5 で約 0.10 倍) と `KD_NightOverlay` の `_Tint` を参考に、手元メニューが読める範囲で決める。設計・値・チェックリストは [計画書](../../plans/2026-09-21-sky-and-night-mode.md)。
- **ローカル専用** (各自の目で選ぶ。同期しない)。ハンドラは `_` 始まり。
- 切替は即時ではなく短いフェード (古民家のライブと同じ 3 秒程度) にするか、実装時に決める。
- `KD_LiveNight` は暗さを同期時計の純関数で出しているので、ナイトモードは**別のローカル入力**として持ち、ライブの夜と **max で合成**する = 二重に乗算せず強い方を採る (ライブが終わってもナイトモードの人は夜のまま。ライブが重なっても掛け算で濃くならない。ライブの上限がナイトモードより暗い演目ではライブの暗さになる)。ライブ終了・中断で入力を 0 に戻すのはライブ側の契約。ライブ本体は [DES-LIV-001](DES-LIV-001-hillside-live.md) で設計中なので、ここでは合成の口だけ用意する。
- 古民家の「暗くなり切ったら木と塀の Renderer を消す」は古民家固有 (庭の演出) なので持ち込まない。
- `KD_MenuSettings` の SleepKit 依存 (`KH_WITH_SLEEPKIT`) は define で死んでいるコードなので今回は触らず、ライブ移植 ([DES-LIV-001](DES-LIV-001-hillside-live.md) の実装 Issue) で `KD_MenuSettings` ごと整理する。空と覆いを実行時に書くのは新規の `KH_NightMode` だけ (境界)。
- 保存 (PlayerData で次回も夜のまま) は要件にしない。必要なら別 Issue。

## 実装前に決めること

- ~~メニューのどこに置くか~~ → ヘッダーのボタン 1 つ (計画書 §1)。
- ON 時の暗さ・色味の数値と、手元メニュー・看板・犬が読める下限。
- 乗算板の renderQueue 4999 が裏山の描画 (LMArt の木・Terrain・遠景・TMP) と干渉しないか。

## 完了条件

- [x] 裏山の空が古民家と同じキューブマップ・同じ材質設定になり、購入アセットは Git 管理外のまま Android / iOS の圧縮 Override も揃っている (2026-09-21 Editor 検査)
- [x] 空を替えた後の眺め・道・犬の見え方を PCVR で確認し、環境光・太陽の扱いを記録した (2026-09-22 PCVR。環境光・反射は昼のまま = `game-design.md` §3)
- [x] 手元メニューに OFF / 100% のナイトモードがあり、自分だけが暗くなる (他の人に伝わらない) — **2 人での非伝播は未実施 (実装はローカル専用・同期なしで Editor 検査済み)**
- [x] ナイトモード ON で空も同じ材質の `_Exposure` / `_Tint` で沈み、太陽が月のように残る (2026-09-22 PCVR)
- [x] ON のまま手元メニュー・看板・犬が読める (2026-09-22 PCVR)。**Quest / iOS の実機は未実施 → 下の「完了」参照**
- [x] ライブの夜との合成の口 (max) があり、ライブ終了後にナイトモードの設定へ戻る (口 `liveDarkness` は実装済み。Play 8 で模擬確認。ライブ本体との実結合は [DES-LIV-001](DES-LIV-001-hillside-live.md))

**完了 (2026-09-22)**

ユーザーが PCVR で「確認済み」と判断した。空 (古民家と同じ Allsky キューブマップ) と手元メニューのヘッダーの OFF / ON ナイトモードを**採用**する。

- どう確かめたか: 2026-09-21 に設計 → Codex レビュー → 実装 → Codex レビューまで行い、Editor 検査 (Menu 25/25・Night 32/32) と値・チェックリストを [計画書](../../plans/2026-09-21-sky-and-night-mode.md) に記録。2026-09-22 にユーザーが PCVR で空の見え方とナイトモードの切替 (3 秒フェード、メニュー・看板・犬の可読性) を確認した。
- 想定と実際のずれ: 「RBS を入れずに古民家のライブの夜と同じ乗算オーバーレイで足りるか」が着手時の懸念だったが、**GrabPass 非依存の乗算板 + 空の露出で PCVR では意図どおり**で、乗算板の renderQueue 4999 と LMArt の木・Terrain・遠景・TMP の干渉も出なかった。
- **Quest 単体 / iOS は未確認。** GrabPass に依存しない方式なので描ける見込みだが、実機で暗さ・可読性・負荷を見るまで対応済みとしない ([開発全体像 §3](../overview.md) の「継続: Quest 単体・iOS」で扱う)。2 人での非伝播 (ローカル専用であること) も未確認。
- [x] 仕様の正本 (`game-design.md` §3) と `world-ui-spec.md` に空とナイトモードの扱いを反映した (2026-09-21)

## 関連

- 古民家: `KD_LiveNight.cs` / `KD_NightOverlay.shader` / `KD_Skybox.mat`、IMP-ENV-010 (空をビルドサイズから削る)、IMP-ENV-015 (SleepKit ナイトモードの手元入口)、BUG-ENV-002 (暗さの下限)
- [移植記録](../imports/kominka-dog-system.md): `KD_MenuSettings` の SleepKit 分離
- [DES-UI-001 手元メニュー](DES-UI-001-menu-and-leash-assist.md): 置き場所
- [DES-LIV-001 裏山のライブ](DES-LIV-001-hillside-live.md): ライブの夜との合成
- [core-concept](../../core-concept.md): ビジュアル方針 (秋の裏山・古民家の眺め)

## 完了記録

> 2026-09-25 の Issue 運用一本化で `archive/archive_summary.md` の行から移記。

- 完了日: 2026-09-22
- 備考: Editor 検査 (Menu 25/25・Night 32/32) + Codex レビュー 2 回の後、PCVR で確認済み。**Quest / iOS と 2 人での非伝播は未確認**。ライブの夜とは max 合成の口だけ用意 (DES-LIV-001)
