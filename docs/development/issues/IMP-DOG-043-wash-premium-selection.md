---
id: IMP-DOG-043
type: IMP
system: DOG
status: wip
priority: Medium
summary: 洗い場ごとに写真から追加道具と台を選択。購入判定なしの先行導入とリスティング画像5枚
next: "Unity導入・局所4ケース確認済み。PCVR/Androidと2人同期を確認後、購入画面・商品IDを別工程で接続"
updated: 2026-10-09
---

# IMP-DOG-043 洗い場の写真選択と追加道具

[企画 DES-DOG-008](DES-DOG-008-wash-paid-tools-and-effects.md)。2026-10-08ユーザー依頼：まず購入なしで使える写真選択UI、出現・使用時の演出と音、従来サイズのリスティング画像を一通り用意する。購入画面は後工程。道具変更は**洗い場ごとに独立**と回答・実装。

## 今回の実装

- 4洗い場のボード横に写真選択パネル。泡・水・仕上げ・洗い場・全部入りの5タブ、17品の写真、セット説明、取り出す／外観に適用／基本に戻す。13言語（既存KD_Language設定に追従）。
- 各洗い場にManualの選択状態を1個ずつ持つ。他の洗い場へは伝播しない。タブ・写真の閲覧は個人設定、確定した道具・台・アヒルはその洗い場の全員へ共有。
- 1洗い場に追加道具は1品。7種類の固定Pickupルートへ13モデルを割り当て、位置同期や洗浄効果IDを付け替えない。元の無料道具は併置。
- 泡銃は連続泡、水鉄砲3品は0.22秒、シャワー2品・じょうろはON/OFF。ドライヤーは濡れだけを減らす専用channel、ブラシ・コロンは洗浄セル／履歴へ入らない仕上げ演出。
- 元の洗浄接触音に加え、泡・水・送風・ブラシ・コロン・出現・ボタンの音を配置。音源は scripts/art/wash-premium-audio.cjs に生成根拠を保存した24kHzモノラルの自作合成音。粒子は各最大64。
- 台3品は既存の高さ調整に追従し、共通支柱との接点を補正。アヒルは台の装飾。旧Wash/11は導入済み時に変更前停止する。旧収集・件数・reset検査は追加道具を認識する。

[画像5枚・ZIP・撮影仕様](../../../artifacts/wash-listings/2026-10-08/README.md)。1024×1024 PNG、価格なし、犬なし。Unity実行用写真22枚は最大512、Android ASTC6×6。

制作原本：vrc-asset-lab commit b63dc851dc1696a6fa6bb3d545b7d1218d5e2822（wash-premium 0.3.0）。

## 配線・操作契約

KH_WashSelectionは各stationのPremiumTools、KH_WashSelectionBoardはPremiumSelectionBoard。選択Ownerが要求者／距離6m／revision／保持中を確認。Pickupの返却は現在のPickup Ownerのみ。初回同期は保持中でも復元し出現演出を再生しない。その後の保持競合はモデル切替を保留する。要求結果はOwner確認付きイベントで要求者へ通知し、アヒルは明示ON/OFF要求。

同期：selector Manual／premium KH_WashTool NoVariableSync + backing Continuous（ObjectSyncと同居）／子KH_WashToolState Manual／KH_WashFinishFx Manual／UI None。NoVariableSyncとbacking Noneは同義ではない。

メニュー40でProgram Asset作成 → Udonコンパイル → 43で字形作成 → 41で画像 → 42で初回構築 → 46で配置/配線の調整 → 44で検証 → 45でEditor撮影。42は既存PremiumToolsを削除再生成しない。

## 確認記録

- C#/Udonコンパイル：エラー0。
- 保存再読込後：4selectors、28固定Pickup、68モデル、6仕上げreceiver、22写真、13言語×42語、各ボタンイベントと通信設定を確認。
- 17選択のapply、ノズル／グリップ参照、13言語×5タブの描画で文字欠け0・はみ出し0。ドライヤーdeltaは実行コード共有本文をC#側で検査し、泡/汚れ0・濡れ負値。
- ClientSim 20261008-103823：写真UI→Owner要求・応答、水鉄砲pulse停止、同slotのシャワーへ変更、Use解放継続、Drop停止 PASS（1case）。
- ClientSim 20261008-104240：保持競合で旧モデル維持／初回復元の模擬、ブラシ・コロンのray接触で洗浄セル/履歴不変 PASS（2cases）。同runのfree-showerはON/OFF後Resetのみ失敗。前caseが検査プレイヤーを犬のそばへ移動したまま、6m範囲外でResetした経路を独立相談で特定（失敗時の実距離は未記録）。
- ClientSim 20261008-104738：検査プレイヤー位置を洗い場に戻し、追加品を出す正の対照からReset→成功応答→解除、既存無料シャワーのON/OFF・Drop停止 PASS（1case）。
- 全run終了時sceneUnchanged=true。Playは限定ケースだけ。Pickup/Useはイベントを模擬し、物理的な手の握りは未検証。
- 初回Playではbacking Noneがネットイベントを拒否する問題を検出・修正。起動時に既知のSynaptic eval CS0433でNOT_EXECUTEDとなった試行は合格に含めていない。
- 独立レビュー：wash_selection_consult（読み取りのみ）。保持中の初回復元、旧Builder保護、finishイベント順序、同時要求の応答を修正。追加レビューの再帰起動なし。

## 配置の保存

2026-10-08: ユーザーが変更した KH_WashStations/Hillside の向き（Y=221.453°）を保存。既存station rootの位置・向きは人の調整を維持する。選択状態は4洗い場で独立している。

## 実機確認待ち

### 2026-10-09 メディアフィックス版の一時非表示

ユーザー依頼で、本番シーンの4洗い場（KH_WashStation / Hillside / Upper / River）の `PremiumTools`・`PremiumSelectionBoard`・`SelectionPosts` と、6匹の `PremiumFinish` を非アクティブ化。削除や配線変更はせず、計18オブジェクトの Active を戻せば再表示できる。台3種とアヒルは元から未選択・非アクティブのまま。無料台の共通支柱は残す。

Editorで追加Pickup 28枠・追加モデル68個の非表示、無料道具19個と4洗い場の通常ボード／台の有効状態を確認。今回の一時Editor処理はC#コンパイルエラー0。Play・実機・ビルド／アップロードは未実施。以下の追加品の実機確認は、再表示後に行う。

同日、非表示版をコミット `1b85684`・注釈付きタグ `promotion-fix-2026-10-09` として保存後、ユーザー依頼で18オブジェクトの Active を再び有効化。現在の作業シーンは追加品を選択できる状態へ復元済み。タグは非表示版を保持する。Editorで18か所と無料道具19個の有効状態、保存差分が18個の Active 変更のみであることを確認。復元後のPlay・実機確認は未実施。

0. PCVR/QuestでUIレーザー、写真・字形、実際の握り・噴出口、粒子・音・負荷を確認（Editor画像/ClientSimでは確定しない）。
1. 1人：4洗い場それぞれで写真選択→左トレーから持つ→使う→置く。別の洗い場の道具・台が変わらない。台3品と高さ0/中/最大の支柱接点。
2. 2人：保持中の途中参加、持った直後のコロン、同時選択、受理直後のPickup、保持者退出時の音停止、Owner交代。
3. 購入画面・商品ID・所有権判定は次工程。色/形を選ぶ泡、虹しぶき、毛のボリューム変化、散歩中の持続エフェクトは企画の残件で、今回の実装済み範囲に含めない。
