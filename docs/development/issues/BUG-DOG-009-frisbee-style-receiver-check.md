---
id: BUG-DOG-009
type: BUG
system: DOG
status: review
priority: High
summary: "`KH_FrisbeeCatch.ReqVariant` (NetworkCallable) が送り手を確かめず、トレーナーでない人がネットワークイベントを送れば 6 匹の取り方を変えられた。受け手 (犬の Owner) で集合と同じ規則 (`KD_GroupActivities` → `KH_TrainerPass._OwnedBy`) を確かめるようにした (未コンパイル・未実行)"
next: "実機 2 人: トレーナーでない Owner の端末へ、トレーナーの押下が届いて切り替わるか、トレーナーでない人の偽装要求が実通信で弾かれるか (B60・B63)。ClientSim の模擬は済み"
updated: 2026-10-03
---

# BUG-DOG-009 フリスビーの取り方の要求を受け手がトレーナーか確かめていない

`docs/plans/2026-10-03-frisbee-settings.md` の F1 で見つかった (設計案自体は未承認。ここでは受け手の確認だけを足す。トレーナー特典の範囲・メニュー・周回は変えない)。

## 原因

[KH_FrisbeeCatch.cs](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs) の `ReqVariant(int v)` は `[NetworkCallable]` で、Owner か・番号の範囲・退役番号しか見ていなかった。トレーナーかどうかはメニュー ([KD_ActivitiesMenu.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_ActivitiesMenu.cs) の `Trainer()`) だけが見ていた。改造クライアントから `SendCustomNetworkEvent(Owner, "ReqVariant", 3)` を送れば、トレーナーでなくても全員の犬の取り方を変えられた (予約の取り消しも同じ)。

集合は受け手 ([KD_GroupActivities.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_GroupActivities.cs) の `Handle`) で `OwnsFormation(caller)` = `hillsidePass._OwnedBy(caller)` を確かめている。`caller` はネットワーク経由なら `NetworkCalling.CallingPlayer`、Owner 自身の要求なら `Networking.LocalPlayer`。開始 (op 1) に Owner / Master の例外は無い (Master が通れるのは「おわる」だけ)。

## 修正 (2026-10-03)

- `KD_GroupActivities._OwnsFormationFor(VRCPlayerApi)` を足した。中身は集合の受け手と同じ `OwnsFormation` (KH_TrainerPass があればそれ、無ければ旧 `formationItem`)。読み取りだけ。
- `KH_FrisbeeCatch.ReqVariant` で、Owner の確認の直後に `VariantCallerAllowed()` を通す。
  - 送り手: `NetworkCalling.InNetworkCall` ならその `CallingPlayer`、直接呼び出しなら `Networking.LocalPlayer` (`ReqCatch` と同じ見分け方。`_RequestVariant` は UI と Interact からしか呼ばれず、ネットワークの処理の中からは呼ばれない)。
  - `activities._OwnsFormationFor(caller)` が偽なら、ログ `[KH_FrisbeeCatch] variant request refused: player N is not a trainer (or the pass is not loaded)` を出して捨てる。送り手が無効ならログを出して捨てる。
  - 権利の読み込みがまだのとき: 自分 (`isLocal`) で `_loaded` が偽なら `_OwnedBy` は偽 (集合と同じく断る)。他人はストアの問い合わせで決まる (集合と同じ)。
  - `activities` が null のときだけ通す。試作シーン (`KH_FrisbeePrototype`) には集合も KH_TrainerPass も無く、切替キューブとフリスビーハーネスがそこで使う。本番シーンでは Walking/5 Verify が 6 匹すべての `catcher.activities == KD_GroupActivities` を確かめている (`KH_DogWalkingEditor` の "catcher neighbours")。
- 新しいシリアライズ参照は足していない。既存の `KH_FrisbeeCatch.activities` → `KD_GroupActivities.hillsidePass` を辿るので、シーンの値は変わらず、ビルダー・Verify の変更も要らない。

### 修正後の動き

- トレーナーでない人が偽って送った `ReqVariant`: 各犬の Owner がそれぞれ断る。`variant` / `pendingVariant` は変わらず、同期も送らない。返事は無い。
- トレーナーが押したとき、受け手 (犬の Owner) がトレーナーでなくても通る: 確かめるのは受け手ではなく送り手 (`CallingPlayer`) の権利だから。
- Owner でもトレーナーでなければ、自分の端末の直接呼び出しも断られる (メニューも押させない)。

## 確かめたこと / まだのこと

- 確かめた: 集合の判定の読み取り、`_RequestVariant` の呼び出し元 (メニューと試作の `KH_FrisbeeLauncher` だけ)、試作シーンに `KD_GroupActivities` が無いこと、本番シーンに切替キューブが無いこと、名前の衝突 (partial を含めて無し)。
- **まだ: コンパイルも実行もしていない** (Unity との接続が切れていた)。
- ClientSim は 1 クライアントなので、別人の `CallingPlayer` を偽った経路はハーネスでは作れない。直接呼び出しの経路 (Owner = 自分) だけ確かめられる。

## 完了条件

- `bash scripts/unity-compile.sh --udon` が通る。Walking/5 Verify の "catcher neighbours" が 6 匹とも PASS。
- ClientSim (本番シーン、模擬): 自分がトレーナーでない状態で犬の `_RequestVariant(3)` → 拒否のログ、`variant` 不変。`KH_TrainerPass` の `_owners` / `_ownerCount` に自分を入れて同じ呼び出し → `variant` が 3 (Idle のとき) か `pendingVariant` が 3。
- 試作シーンの切替キューブ (フリスビーハーネスの EnsureVariant) が今どおり番号を切り替える。
- 実機 2 人: トレーナーでない Owner の端末へ、トレーナーの押下が届いて切り替わる。

## 2026-10-03 (`8faf572`) 本番シーンへ

- コンパイル完了 (C# / U# エラー 0)。受け手 (犬の Owner) が KD_GroupActivities → KH_TrainerPass._OwnedBy で送り手を確かめる VariantCallerAllowed が、本番の 6 匹とも activities 配線済みで効く (Course/123 が確認)。さらに DES-UI-002 の実装で、世界では ReqVariant(5) をトレーナーからでも拒否する。
- ClientSim: Course/126 11/11 (TT3 / TT4 のトレーナー判定は KH_TrainerPass の所有者表のヒープ模擬。ClientSim の Store は所有を返さない前提)。Walking/5 の失敗 22 件は旧コース前提の既存のもの。
- 残り (ユーザー): 2 人の実通信で、トレーナーでない人の送信が弾かれること。B60・B63。

検証は ClientSim 1 人 (疑似を含む)。実通信・PCVR・2 人・端末の合格ではない。
