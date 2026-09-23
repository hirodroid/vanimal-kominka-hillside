---
id: IMP-DOG-016
type: IMP
system: DOG
status: todo
priority: Medium
---

# IMP-DOG-016 呼ぶ・ポーズを移植し、リード除外と斜面判定を足す

2026-09-23 起票。[DES-UI-003](DES-UI-003-menu-scope-and-polish.md) で **呼ぶ・ポーズは既存の種類・操作を保ち、リードを外して使う**と決まった。古民家の場所決めには **接地・傾斜・法線・NavMesh の判定が一切なく、`ValidPlacement` は NaN / Inf チェックだけ** (`KominkaDogRun: Assets/KominkaDogRun/Scripts/KD_SleepSlot.cs:277`)。平地の庭では成立していたが、裏山は斜面なのでここに判定を足す。

実装順は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md) → [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md) → **本Issue** ([IMP-DOG-014](IMP-DOG-014-treats-port.md) / [IMP-DOG-015](IMP-DOG-015-accessories-and-petfx-port.md) と並行可)。**接続競合と斜面の契約は [DES-DOG-001](DES-DOG-001-leash-walking.md)**、UI 頁は IMP-UI-001 が持つ。

## 範囲

- `KD_SleepSlot` / `KD_SleepBoard` / `KD_SleepPlacement` / `KD_CallPose` と `KD_DogWanderController.Sleep` を裏山へ配線する。
- 継承するもの: 犬 → ポーズ → 場所 の流れ、5 ポーズ (ごろん / ふせ / おすわり / うずくまる / へそ天) + あご乗せ・お手・到着後の伸び+あくび、予約枠 (3 秒更新・15 秒途絶で解放・先着 1 人)、終了 4 条件 (自由にする / 権限者の取り上げ / 呼んだ人の退室 / Ownership 移譲)、寝ている犬を おもちゃ・おやつ・Fetch の候補から外す扱い。
- **4 動作 (へそ天 / あご乗せ / お手 / 伸び+あくび) 各 30pt の課金ゲート**を `ActionOwnedBy` で継承する。価格表は [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md)。
- 「呼ぶを全員に許可」を設定面へ置く (初期 ON、権限者 = ワールド作者・不在時 Master、権限者以外は操作不可)。初期 ON は 2026-09-23 のユーザー決定 (古民家の最近の変更に合わせる)。権限者は継承。
- **変更 1 — リード除外**: 自分・他人を問わずリード接続中の犬を対象外にする。行を暗色にし、理由を文言で出す (「リード中。さんぽで外してから」)。逆に、**呼ばれて待機中の犬はさんぽタブで接続不可**にする (暗色 + 理由)。
- **変更 2 — 場所の検証**: マーカーを真下へレイキャストして地面へ接地させ、**地面法線 ≥ 0.9 (約 25°、仮値) のみ有効**とする。無効時はマーカーを赤くして確定不可にし、色だけでなく文言「この姿勢には平らな場所が必要です」も出す。集合の判定より厳しくする理由は、ふせ・ごろん・へそ天が体を地面に密着させるため。数値は実測して確定する。
- **変更 3 — 犬の移動**: 古民家は水平距離の StepToward + Y の MoveTowards で接地スナップをしない。裏山では散歩と同じ地形追従に合わせる ([DES-DOG-001](DES-DOG-001-leash-walking.md) の「隊形と斜面」と同じ扱い)。
- 呼ぶタブの頁 (犬 6 → ポーズ 5 → 場所) と端末別の配置操作 (VR は手の前 + 板の ◀▶、Desktop は距離ホイール + Q/E、Touch は [ここへ呼ぶ]) は [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md)。
- UI 頁は古民家の `KD_TreatPouch` の partial `.Sleep` + `KD_CallPose` のポーズ面をそのまま使い、ビルダーは [計画書](../../plans/2026-09-23-menu-rebuild.md) 段階 0 で移植済みのものに配線を足す (新しい頁を自作しない)。ポーズ面のミニチュアは `KD_CallPoseEditor` を段階 2 で焼く。

## やらないこと

「離れたら解除」の復活 (`releaseDistance` / `arriveTimeout` は古民家でも定義のみで参照ゼロ。挙動を変えない)、接続したままポーズを取り終了時に散歩を再開する案 (保留を継続)、新しいポーズの追加、写真・ライブとの連動、Quest / iOS の合格判定。

## 完了条件

- [ ] 犬 → ポーズ → 場所 の流れが 6 匹すべてで平地で成立する (予約・配置・到着・終了)
- [ ] リード接続中の犬が暗色 + 理由で除外され、外せば呼べる。呼ばれて待機中の犬はさんぽで接続できない (T-010)
- [ ] 斜面で赤マーカーと文言が出て確定できず、平地では成立する。**採用した法線値を記録した**
- [ ] 4 動作が所持で解放され、未所持では価格が出る
- [ ] 「呼ぶを全員に許可」を OFF にすると権限者以外が呼べない。寝ている犬は OFF にしても起きない
- [ ] 2 人で同じ犬を選んで予約の取り合いになり、先着が保たれる
- [ ] PCVR でユーザーが斜面の見え方と配置操作を確認した。**Quest / iOS の合格判定は [VER-WLD-001](VER-WLD-001-quest-ios-devices.md)**

## 関連

- [DES-UI-003](DES-UI-003-menu-scope-and-polish.md) 順番 3: 継承する範囲と変更 1〜3、保留にした 2 件
- [DES-DOG-001](DES-DOG-001-leash-walking.md): 接続競合・隊形と斜面の契約。集合の配置判定と数値を揃える
- [IMP-PRG-001](IMP-PRG-001-ledger-shopkeeper-points.md): 追加動作 4 種 各 30pt の価格表と所持判定
- [IMP-UI-001](IMP-UI-001-menu-table-driven-and-settings.md): 呼ぶタブの頁と端末別の配置操作
- [world-ui-spec](../../world-ui-spec.md) T-010: リード中の除外と斜面・平地の検証条件
