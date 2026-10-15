# 005 開発者レビュー

2026-10-15。実装 /root、独立D /root/parade_choreography_consult。担当は読取のみ、追加レビューなし。

購入原本の不変更、再構築時の差替維持、Body/Head/Wing bindpose、UIとbackingからのResume除去、Live再表示を確認。初回の黒いかぼちゃ、破壊前入力検査、既存mesh GPU更新を指摘。

対応: Float32 colorsをColor32へ変更。通常MeshRendererでは正常だった同一mesh/materialがPSでは黒くなる比較を保存し、変更後はPSでも橙色・軸・蝋燭が表示された。GPU instancing無効と明示vertex streamsだけでは直らなかったため、UV不一致説は原因として採用しない。旧画像はdiagnostics-float-color、修正後はpumpkin-probe-*。

PreflightをPrepare前へ置き、元mesh/UV/材質/関節/bind条件/7PS/UI配列を検査。CopySerialized後UploadMeshData(false)で再更新。2Hat、14Skin、7PS、resume proxy/backing/object無し、NetworkID null無しを保存後検査。

限定再レビュー結果: 新しい重大な欠陥なし。PreflightとApplyのPS抽出集合条件に小差がある点は将来の再構築保守候補（今回の適用件数は一致）。完全rollbackではない。連続動作・VR・全編はこの構造/標本画像判定の外。
