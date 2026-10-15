# 灯り006 独立Dレビュー

2026-10-15、担当 /root/parade_choreography_consult。rootが実装、担当は読取のみ。追加レビューの起動なし。

- 夜の専有入力とmax合成、共有Elapsedのフェードを事前相談。保存する手動night APIを呼ばない。
- 破壊前の入力検査不足、早すぎる選択marker、PS材質cloneのパス不安定を修正。新しいHat/Bat accentもPreflightへ追加した。
- Disableの最初の試験は失敗。SDKのOnDisable dispatch、program export、参照先を調査。2回目はDisable直後の0から、無効のままUpdateで再開したことを観測した。内部キューが原因とは断定しない。
- Updateのenabled/activeInHierarchy関門を追加。同じ限定ケースでDisable後0を保持、reenableで現在Elapsedへ復帰、Cancelで0へ。night-lifecycle.jsonのPASSを確認。
- かぼちゃ内部球はmeshのみ共有しColliderを持ち込まず、殻の深度で遮蔽。方向curveは本体だけに設定し子が一緒に回転する。Bat accentは元のbones/rootBone/boundsを継承。現在構造の再構築/参照を壊す重大欠陥はなし。

通常220秒終端、ネットワーク途中参加、VR描画の合格には読み替えない。最終の形状収まりはE画像確認へ。
