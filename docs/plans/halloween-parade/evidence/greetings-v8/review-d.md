# 008 Dレビュー

担当 /root/parade_choreography_consult、実装 /root。対象はPlaza partial、Playback接続、WashSelectionのlocal gateとEditor配線。読取独立レビュー。同期・所有権を壊す重大欠陥なし。

- Renderer.enabled/model.activeを復元せず、forceRenderingOffだけを戻すため、上演中の選択状態を巻き戻さない。
- 管理Pickupは現在slotから復元。同期選択再適用にもlocal gateが効く。
- 無効化後Updateは既存enabled guard、OnDisableで復元。
- UI例外/ResetButton不足の検査をBindの変更前へ移動。
- held工具もpickupable=falseにする契約。Dropは呼ばずIsHeldで描画を維持する設計だが、保持中の実操作、drop後非表示、使用中台、通信での選択変更は未検証。
- 限定NightLifecycleへの非表示/復元確認追加を相談し妥当。管理Pickupが現在slotと整合、他3stationのpickupable/selection gate不変も検査する。

実施結果: 初回は検査が主洗い場をKH_WashStations配下と誤認して開始前INVALID_SETUP。主洗い場はKH_WashStation、ほかRiver/Upperを明示し、独立相談後同一局所試験を再実行。PASS（plaza-lifecycle.json）。非表示/Disable復元/再開/Cancel復元、63renderer・11pickup・4UI collider・現在slot整合・他3station不変、scene/source/PlayerData/settings復旧を確認。通常220秒終端・held/busy/通信はこの試験に含まない。
