---
id: IMP-UI-001
type: IMP
system: UI
status: todo
priority: High
---

# IMP-UI-001 メニューを表駆動にして 7 タブ + 設定面 + ヘッダーを作る

2026-09-23 起票。[DES-UI-003](DES-UI-003-menu-scope-and-polish.md) で **タブ構成は案 A**、ヘッダーは [残高] [設定] [閉じる]、ナイトモードは設定面へ移す、距離フェードは PC のみで採用、と決まった。現行の `Assets/KominkaHillside/Scripts/Editor/KH_HandMenuEditor.cs` は **タブ数固定 3・ページごとの座標直書き・ExpectedEvent の手動対応表**で、タブを足すたびに同じ修正を繰り返す構造になっている。先に表駆動へ直し、その上に頁と設定面を作る。参考は古民家の `KominkaDogRun: Assets/KominkaDogRun/Editor/KD_HandMenuScrollEditor.cs` (Widths / Order)。

実装順は **本Issueの表駆動化 → [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) → [IMP-DOG-014](IMP-DOG-014-treats-port.md) / [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) / [IMP-DOG-016](IMP-DOG-016-call-pose-port-and-slope.md)**。各タブの中身 (Box・Slot・台帳の層) はそれぞれの Issue が持ち、本Issueは **UI 頁と配線**だけを担当する。

## 範囲

- `KH_HandMenuEditor` の表駆動化。Tabs / Widths / Order の表を持ち、タブ数固定 3 (`for (i<3)` と配列長 3)・座標直書き・ExpectedEvent の手動表を解消する。`Charset()` へのグリフ追加も表から導く。
- 案 A の 7 タブを組む: さんぽ / おやつ / かざり / 呼ぶ / おもちゃ / あそぶ / ライブ。既存 3 タブ (さんぽ / あそぶ / おもちゃ) の中身は変えない。**ライブは [DES-LIV-001](DES-LIV-001-hillside-live.md) の採用まで非表示または空の頁**にする。初回タブ = さんぽ (現行維持)。
- ヘッダーを [残高] [設定] [閉じる] にする。開発用 [Dev] の扱いは [DES-UI-002](DES-UI-002-frisbee-settings.md) の撤去時期に従う。**初回付与を受け取っていない間だけ**、かざり・おやつの購入面に「入口のおじいさんから支度ポイントを受け取れます」を出す (受取済みなら出さない)。残高の値は IMP-PRG-001 が持ち、本Issueは表示枠を用意する。
- 設定面 (タブではなく上段の別面) を新設する。ナイトモードをヘッダーから移設し、古民家と同じスライダー (夜の明るさ 0〜100%、PlayerData に保存「自分だけに反映・次回も復元」) に作り替える (現行 OFF/ON は暫定。`KH_NightMode` の強度を連続値にする) / 音量 (音の中身は [DES-WLD-002](DES-WLD-002-sound.md)) / 言語 (日本語のみ provisional、切替は保留) / 距離フェード / 呼ぶを全員に許可 / ライブ開催許可 (DES-LIV-001 後) / クレジット / 案内。
- 距離フェードは `Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DistanceFadeSettings.cs` をそのまま配線する。4 プリセット + 4 入力欄 + [確定して保存]、PlayerData の 1 文字列で保存、本人のみ・同期なし。**Android / iOS は入口ボタンだけ閉じ、文言「距離フェード (PCのみ)」は全端末で出す** (性能理由ではなくシェーダのフォールバック懸念)。
- 状態表示の規律を `KH_HandMenu` の Tint / Pending / Note へ対応付ける: 未復元 (よみこみ中・残高非表示) / 実行可能 (点灯 + 枠) / 資源不足 (「あと N pt」+ ボタンを隠す) / 権限・占有 (暗くする + 理由の文言) / 処理待ち (文字 + 当該ボタン無効) / 失敗 (4 秒の文 + 再操作可)。**隠すと暗くするの使い分けは意図的**なので崩さない (`KominkaDogRun: docs/development/design/world-ui-spec.md` の状態表)。
- 全売り場で共通の購入確認カード 1 枚 (古民家の `KD_TreatPouch.confirmRoot` 相当) を用意する。7 状態 (読込中 / 上限 / 使用中 / 購入済み / スペシャル / 残高不足 / 買う) の文言枠まで。
- **ヘッダーの段構成**: 7 タブ + [残高][設定][閉じる] は 1 段に入らない (モックで判明。1140px では**タブ幅約 96px**になり、現行の金の下線 150×6px が入らない)。**タブ帯を 2 段目に分ける案を基本**に、下線幅・タブ幅を表駆動の値にする。
- `4 Verify Menu` を 7 タブ + 設定面の件数へ更新し、TMP グリフを追加する (不足すると Build が例外。手順は `vrc-tmp-font`)。
- DES-UI-003 のメニューモック (案 A、通常 / 使用不可 / 処理待ち) と項目配置を突き合わせる。食い違いはモック側を正とするか本Issueで直すかを記録する。

## やらないこと

各機能のロジック (おやつ・かざり・撫でFX・呼ぶ・台帳・おじいさん)、ライブの中身と演目、音源そのもの、言語の切替実装、Quest/iOS の合格判定。

## 完了条件

- [ ] 表駆動化の後、既存 3 タブが `4 Verify Menu` で従来と同数合格する (退行なし)
- [ ] 7 タブ + 設定面がビルドされ、Verify が件数 (タブ・ボタン・ExpectedEvent) を検査する
- [ ] 距離フェードが PC で反映され、保存して再入室で復元する。Android / iOS は入口が押せず文言だけ見える (T-011)
- [ ] ナイトモードが設定面のスライダーで連続的に効き、保存値が再入室で復元される (ヘッダーからの移設で退行しない)
- [ ] 6 状態の見せ方が規律どおりで、モック (DES-UI-003) と照合して差分を記録した
- [ ] 残高枠・未受取の案内が IMP-PRG-001 の配線後に正しく出る
- [ ] PCVR でユーザーがタブの開閉・可読性を確認した。**Quest / iOS の合格判定は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md)**

## 関連

- [DES-UI-003](DES-UI-003-menu-scope-and-polish.md): 案 A のタブ構成・設定面の項目・状態表示の規律を決めた大元
- [DES-UI-001](archive/DES-UI-001-menu-and-leash-assist.md): 現行 3 タブの実装と PCVR 確認 (done。退行させない基準)
- [world-ui-spec](../../world-ui-spec.md): 画面・入力・状態の正本。T-011 (距離フェード) はここ
- [DES-LIV-001](DES-LIV-001-hillside-live.md) / [DES-WLD-002](DES-WLD-002-sound.md): ライブタブと音量の中身
- [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md): 残高・未受取の案内の値を供給する
