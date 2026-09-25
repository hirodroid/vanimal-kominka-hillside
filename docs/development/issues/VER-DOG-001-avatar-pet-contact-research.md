---
id: VER-DOG-001
type: VER
system: DOG
status: hold
priority: Medium
summary: 来訪者の無改変が条件。ムチォは既存Senderで対応候補、犬っこは通常時の対象外。静的調査まで完了、IMP-DOG-011 と共に保留
next: "再検討: IMP-DOG-011 の保留解除時。静的調査結果は [調査結果](../../plans/2026-09-21-avatar-pet-contact-research.md) に残す"
---

# VER-DOG-001 アバターペットの位置・向き・持ち主をワールドから検知できるか調査する

## 目的

古民家ヒルズの犬が、来訪者のアバターに組み込まれたペットへリアクションするための接続方法を調べる。ユーザー依頼: 2026-09-21。

**来訪者のアバターを改変せず、購入時の既定構成で検知できることが条件。専用Senderの追加を来訪者に求める方式は対象外。**

## 調査対象

- `C:/work/unity/vcc/PetSystemResearch/Assets/TORINOSUKE/いぬっこぎみっく`
- `C:/work/unity/vcc/PetSystemResearch/Assets/Muchio`

購入Prefab・コンポーネント・Animation/Animator・利用条件を読み取り調査する。アバターおよびワールドの実装変更はこの調査に含めない。

## 確認事項

- [x] 基準Transformと駆動方式を特定し、見た目とのずれを記録する（動的な一致は実機確認）
- [x] 設置・追従・収納の制御と他プレイヤー側での再現条件を調べる
- [x] 既存SenderのRoot・タグ・Local Only・有効状態とアニメーション制御を調べる
- [x] 既定構成のままでの対応可否・制約を判定し、調査した改変条件を参考記録する
- [x] 実機用の位置・向き・持ち主・参照寿命チェックリストを残す
- [ ] VRChat実機で位置取得・追従・収納・アバター変更・複数人を確認する（後続試作が必要）

## 根拠

[VRChat Contacts公式仕様](https://creators.vrchat.com/common-components/contacts/#udon-access-in-worlds): Contact proxyから取得する位置と向きはContactのRoot Transform。形状のPositionオフセットとは異なる。持ち主はproxyのplayerを使う。

## 状態

後続の実装は [IMP-DOG-011 自由行動中にムチォの匂いを嗅ぐ](IMP-DOG-011-muchio-greeting.md) で管理する。本Issueは静的調査の根拠と実機での検知成立の確認を担当する。

**静的調査済み。実装・実機検証は未着手。2026-09-22 に IMP-DOG-011 と共に保留 ([保留の記録](#保留の記録))。** 詳細は [調査結果・実機チェックリスト](../../plans/2026-09-21-avatar-pet-contact-research.md)。

| 対象 | 判定 | 主な根拠 |
|---|---|---|
| ムチォ | 既存構成で対応候補（実機未確認） | KissContactsの `OnigiriPetKiss` Senderがペット側の接地補正後Transformにあり、収納時に親PetWorldごとOFFになる |
| 犬っこギミック | 無改変での通常時検知は対象外 | 既存Pet_Fed/Pet_Wateredは本体Rootを参照するが食器表示中のみ。Pet_RunはLocal Onlyで他人のペットの追跡に適さない。Sender追加は今回の条件を満たさない |

ムチォの既存Senderは歩行の小さな上下動を追わず、犬っこのモデルルートは骨格だけが回転する芸を追わない。取得座標と見た目の中心・鼻先が一致するという断定はしない。

次はワールド側のReceiverと位置マーカーだけで、無改変のムチォを検知する最小試作。犬AIへの接続は位置・向き・持ち主・収納時の解除を実測してから行う。犬っこの専用Sender追加は行わず、食器表示中の条件付き検知を通常対応とは数えない。今回、購入アセットやシーンの変更はしていない。

## 文書検証・独立レビュー（2026-09-21）

- `node scripts/check-issues.js`: 23件、all consistent。
- `node scripts/check-doc-links.js`: 52ファイル、all links OK。
- 独立担当 `pet_research_review` がD（開発者）・M（保守）・P（範囲）の観点で一次Prefab、収納カーブ、公式仕様、改変条件と結果文書を照合。必須修正なし。途中指摘のIssueチェック・結果リンクは更新済み。
- 実装変更なしのためUnity/Udonコンパイルは対象外。レビューも静的確認であり、Unity動的評価・実機検証を代替しない。
- 同日追記: ユーザーの「来訪者の既定構成で検知」という条件を明記し、判定と後続試作の対象を修正。この追記はセルフ確認。

## 保留の記録

> 2026-09-25 の Issue 運用一本化で `hold/hold_summary.md` の行から移記。

- 保留の理由: 2026-09-22。静的調査は完了。実機検証は IMP-DOG-011 の試作と一体なので同時に保留
- 再検討のトリガー: IMP-DOG-011 の保留解除時。静的調査結果は [調査結果](../../plans/2026-09-21-avatar-pet-contact-research.md) に残す
