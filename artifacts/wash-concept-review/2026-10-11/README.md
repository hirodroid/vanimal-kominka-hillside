# 新しいお手入れ道具の企画画像の確認 (2026-10-11)

[確認用HTMLを開く](index.html)。画像・スタイル・処理を埋め込んだ 1 ファイルで、インターネット接続は不要。

1. 左の一覧か「次の道具」で、ホース・水鉄砲・プールを 1 品ずつ見る。画像はクリックで拡大。
2. 「案 A / 案 B / 直したい / 保留」を選び、形・大きさ、素材・色、そのほかを書く。各品に「決まっていること (変えない)」を並べてある。
3. 「回答を保存（JSON）」でダウンロードし、チャットへ添付して引き継ぐ。「読みやすい一覧を保存」は Markdown。

画像は生成 AI の企画画像で、ゲーム内の見た目を約束しない。画像と生成記録の正本は `vrc-asset-lab/assets/wash-premium/design/2026-10-11/` (README・image-prompts.json・image-manifest.json)。再生成: `node scripts/art/build-wash-concept-review.cjs`。

関連: [水の道具 IMP-DOG-049](../../../docs/development/issues/IMP-DOG-049-wash-water-tools-and-reactions.md)、[プール IMP-DOG-044](../../../docs/development/issues/IMP-DOG-044-wash-pool-platform.md)、[設計案](../../../docs/plans/2026-10-11-wash-items-design.md) §3・§4。

確認記録 (2026-10-11): スクリプトの構文と、簡易的な DOM のまねで表示・選択・入力・次へ・ブラウザー保存を確認。実ブラウザーでの表示・クリック・ダウンロードは未確認。
