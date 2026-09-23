---
id: BUG-DOG-003
type: BUG
system: DOG
status: wip
priority: High
---

# BUG-DOG-003 非マスターがフリスビーを投げても犬が追わない

## 報告 (2026-09-23)

ユーザーが立てたインスタンスにフレンドと 2 人で入り、散歩とフリスビーを確認した。ユーザーがマスター。2 人での散歩は「結構良かった」、基本的な体験は OK との評価。

フリスビーでは、投げる人によって次の違いがあった。

| 操作 | 観察結果 |
|------|----------|
| ユーザー (マスター) が投げる | 犬が取りに行き、キャッチする。フレンドにも追走・キャッチが見えた |
| フレンド (非マスター) が持つ | 犬は待機位置へ移動した |
| フレンドが投げる | 犬が取りに行かず、投球に反応しなかった |

確認端末、犬・ディスク等の実際の Owner、プリセット、投球条件、再現回数、ログは未記録。マスターかどうかによる観察上の差であり、原因がマスター判定にあるとはまだ確定していない。

## 再現手順 (報告に基づく)

1. プレイヤー A がインスタンスを立て、マスターとなる。プレイヤー B が参加する。
2. A がフリスビーを投げ、犬が取りに行き、B からもキャッチが見えることを確認する。
3. B がフリスビーを持ち、犬が待機位置へ行くことを確認する。
4. B が投げる。報告では犬が追走を開始しない。

期待する動作: 非マスターが投げても犬が反応し、追走・キャッチ・持ち帰りを両者から確認できる。

## 調査結果 (2026-09-23、Codex)

**投球通知の未達ではなく、通知を受け付けた直後に、同期が遅れた「まだ手に持っている」判定で追走を取り消す競合が最有力。** 手元の実機ログに、ローカルの発射ログを伴わない投球受付が 4 回あり、いずれも同じ秒に待機へ戻っている。現行コードの遷移と一致する。

調査対象はソース `2cde145`、生成済み UASM、既存の VRChat ログ。今回は読み取り調査と本 Issue の更新のみ。コード・シーンの変更、Unity 操作、Play、アップロード、2 人での再現試験は行っていない。原因箇所は強く絞れたが、取消理由を直接出すログが無く、**当該フレームの `IsHeld` 値と、報告されたプレイのビルド SHA は未確定**。修正済み・実機再検証済みとはしない。

### 1. 待機と投球は別経路

ソース位置は調査時点の行番号。リンクはリポジトリ内ファイルを指す。

| 段階 | 根拠 | 処理と意味 |
|------|------|------------|
| 持つ | [KH_Frisbee.cs](../../../Assets/KominkaHillside/Scripts/KH_Frisbee.cs) L104–110、L245–257 | `HolderPlayer` は `Pickup.IsHeld` とディスク Owner を読む。取得時の所有権要求はディスクのみ |
| 待機へ | [KH_FrisbeeCatch.cs](../../../Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs) L690–701、L792–799 | 犬 Owner が所持者を観測して `Idle → Ready`。投球通知を受け取らなくても待機位置へ行ける |
| 投げる | KH_Frisbee.cs L401–449、L185–206 | Use → Drop → 翌フレームの `_CheckThrow` → `_Launch`。捕獲制御の Owner なら `ReqCatch` を直接呼び、それ以外は `NetworkEventTarget.Owner` へ送信 |
| 受け付ける | KH_FrisbeeCatch.cs L529–560 | 犬と捕獲制御の両方の Owner であること、遊べる状態、位置・速度・時刻等を検査。通過すると投球を記憶し、`throw` ログを出して `StateChase` へ |
| 直後の取消 | KH_FrisbeeCatch.cs L703–708、L644–648、L603 | 接地中に `Pickup.IsHeld == true` なら即 `GiveUp()` → `Idle`。投球情報 `_haveLaunch` も消す |
| 再び待機へ | KH_FrisbeeCatch.cs L698 | まだ保持中に見えるため、次の Update で `Idle → Ready`。後から解放状態が届いても、L796 で `Idle` へ戻るだけで追走は復活しない |

この経路に `IsMaster` による投球制限はない。実装上の分岐は **投げ手と犬／捕獲制御の Owner が同じかどうか**。マスターの投球では手を離したことが同じ端末で既に分かる一方、他人の投球では「投球通知」と「Pickup の保持状態」の到着が別になる。

`ReqCatch` は `[NetworkCallable]`、引数 6 個、Owner 宛ての送信として定義されている。生成済み UASM にも `ReqCatch` の export と 6 引数の送信 extern が存在する (両キャッシュの更新時刻 2026-09-22 20:26:49)。ただし、このキャッシュとアップロード済みビルドが同一であることまでは証明していない。

### 2. 実機ログの証拠

ローカルの `%USERPROFILE%/AppData/LocalLow/VRChat/VRChat/output_log_2026-09-22_19-58-09.txt` を確認。次は **20:38:33–20:41:33、L17262–17855** の範囲。報告内容と一致するが、ユーザーによるセッション時刻の照合は未実施。フレンド側のログは未取得。氏名・ユーザー ID・インスタンス情報は転記しない。

| 時刻 | 行番号 | 観測 |
|------|--------|------|
| 20:38:47–48 | 17269–17277 | ローカル `launch v=7.03` → `throw` → `plan` → `takeoff` → `catch OK` |
| 20:39:01 | 17568–17569 | **ローカル launch 無し**、`throw speed=6.14` → 同じ秒に `ready grace: holder=1.5 m`。次の投球まで `plan` 無し |
| 20:39:24 | 17577–17579 | **ローカル launch 無し**、`throw speed=6.03` → 同じ秒に `ready grace: holder=6.2 m` → `ready go`。次の投球まで `plan` 無し |
| 20:39:47 | 17603–17605 | **ローカル launch 無し**、`throw speed=5.78` → 同じ秒に `ready grace: holder=6.7 m` → `ready go`。次の投球まで `plan` 無し |
| 20:40:12 | 17612–17614 | **ローカル launch 無し**、`throw speed=4.57` → 同じ秒に `ready grace: holder=8.0 m` → `ready go`。次の投球まで `plan` 無し |
| 20:40:45 | 17680–17693 | ローカル `launch` → `throw` → `plan` (その後、地上回収計画へ) |
| 20:41:10–12 | 17753–17766 | ローカル `launch` → `throw` → `plan` → `takeoff` → `catch OK` |

集計: ローカル発射 3 件、投球受付 7 件。差分の 4 件は上表の外部投球と解釈でき、全て待機へ戻る。該当範囲に `Ownership split`、Udon の `halted` は見つからない。これはログ範囲内の確認であり、全セッションのエラー無しを意味しない。

`throw` は `ReqCatch` の全受付ガードを通った後にのみ出る。したがって **この 4 件について、イベント未達・受付時の Owner 不一致・受付時の遊び禁止・古い発射時刻による拒否は主因から外れる**。さらに `ready grace` は Ready へ新しく入った後のログなので、追走が維持されず、保持者が再度見えていることが分かる。

### 3. 原因の確度と残る切り分け

**確認できた事実:** 受付済みの外部投球 4 件が計画ログを出さずに待機へ戻る。現行コードでは、投球受付後も保持状態を無条件に読んで即取消し、投球情報を破棄する。

**強い推定:** 投球イベントが解放状態の同期より先に犬 Owner へ届き、L708 が古い `IsHeld == true` を「投げた後の再取得」と誤認した。`ReqCatch → Chase → GiveUp → Idle → Ready` が、同じ秒の受付・待機ログを説明する。投球後に一度だけ通知する設計なので、保持状態が後から false になっても回復しない。

**未確定:** 各取消時の `IsHeld`、送信者 ID、Owner ID、取消理由は現行ログに無い。受付直後の本当の再取得、リード・集合等の割込み、終了通知による取消しを完全には排除していない。ただし 4 回連続の同じ並びと、ローカル投球では計画へ進む差から、保持同期の競合を最優先とする。

[公式 Network Events](https://creators.vrchat.com/worlds/udon/networking/events/) では Owner 宛ての配送、自己宛ての即時実行、イベントの順序保証の範囲を確認した (2026-09-23 閲覧)。イベント同士の順序保証を、Pickup の保持状態とイベント間の到着順保証に広げない。今回の Pickup の具体的な競合は、公式文書による断定ではなく上記コードと実機ログからの推定。

### 4. 対応案 (未実装)

**犬の所有権を投げ手へ移さず、犬 Owner 側に投球受付の短い保留を設ける方向を推奨。** 投球通知を受け取った時点で、まだ保持中に見えることを即取消理由にしない。

1. 受付時の発射位置・速度・時刻・曲率・投げ手・世代を保持し、解放状態がまだ見えない場合は期限付きで保留する。保留中は Ready への戻りで要求を消さない。
2. 犬 Owner が解放を観測した時点で、期限・Owner・遊べる状態・割込み・発射者を再確認し、元の発射時刻で追走を開始する。受信時刻へ時計を付け替えて予測を遅らせない。既に着地していれば地上回収／中止を判断する。
3. 保留中の終了・回収通知、持ち直し、別人の取得、次の投球、Owner 退出を区別して破棄する。解放を一度観測した後の新たな保持は、従来どおり手を優先して中止する。
4. 送信元と投球を対応付ける。`_gen` は投げ手端末ごとの非同期カウンタ (KH_Frisbee.cs L99) なので、交互投球では世代番号だけを全員共通の一意 ID とみなさない。送信者／投げ手と世代の組、または同等の識別方法を検討する。Owner の同期も遅れうるため、一時的な不一致を全て永久拒否する実装にはしない。

既存の [KD_DogFetchController.cs](../../../Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogFetchController.cs) に参考実装がある: `FetchRequestGrace = 1.5f` (L355)、`BallLooksThrown()` の保持判定 (L702–706)、Idle 中の期限確認と `TryStartFetchFromRequest()` 再試行 (L872–887)。ただしフリスビーは飛行予測の時間制約があるので、**1.5 秒をそのまま採用する決定ではない**。通信観測値と捕獲可能時間で保留上限を決める。

単に L708 を削除すると本当の持ち直しを無視する。`noticeDelay` を増やしても、その前に L708 が走るので対策にならない。また今回の同期待ちは、撫でた後の `readyGraceSeconds = 2` とは別の役割。

### 5. 修正時に足す観測と検証

診断ログは投球受付・保留解除・取消の各 1 回を基本にし、毎フレームは出さない。ローカル／ネットワーク経路、送信者と投げ手、犬／捕獲制御／ディスクの Owner、世代、発射からの経過、`IsHeld`、遷移元・先、拒否／取消理由を記録する。

| 確認 | 合格条件 |
|------|----------|
| 通知が解放より先 | 一時的に保持中でも要求を失わず、解放観測後に追走する |
| 解放が通知より先 | 通常どおり開始する。両順序を分けて検査する |
| 解放が来ない／期限切れ | 古い投球を後から実行せず、安全に保留を破棄する |
| 再取得・回収・集合・リード | 保留中／追走中とも新しい操作を優先し、手から奪わない |
| A → B → A の交互投球 | 投球と終了通知を混同せず、双方にキャッチ・持ち帰りが見える |
| 所有者退出・遅い通知 | 古い要求で再始動せず、安全に終了する |

既存の [ClientSim ハーネス](../../testing/kh-frisbee-harness.md) は単一クライアント・固定投球台で、保持者、他プレイヤー、Owner 移譲、通信同期を対象外としている。従来の 36 投等の結果は本不具合の否定材料にならない。順序違いの制御試験に加え、最終的に **犬 Owner と投げ手が別の 2 人での実機確認**を行う。

### 調査後の状態

**読み取り調査完了、修正・実機再検証待ち (`wip`)。** 次の実装対象は投球受付と保持同期の整合。ジャンプ計画・プリセット調整とは分けて扱う。

## 対応 (2026-09-23)

§4 の方針どおり、**犬 Owner 側に投球受付の短い保留を設けた**。所有権は動かさない。Unity のコンパイルは 0 件、Walking/5・Frisbee/5 の Verify は通っている。**2 人実機は未実施。**

ClientSim ハーネス `--mode=netorder` の結果は版ごとに分けて記録する。

| 版 | 結果 | run id |
|---|---|---|
| Codex 1 回目の是正前 | 8/8 | `20260923-115401-929dc4` |
| Codex 1 回目の是正後 | 10/10 | `20260923-121244-f1d40d` |
| Codex 2 回目の是正後 (最終版) | **10/10 PASS** | `20260923-122627-de9b6a` |

最終版の netorder は 5 ケース × 2 回。`debugHeldOverride` は全終了経路で 0 へ復元 (`overrideRestored`)、Console エラー 0、`sceneFileUnchanged` true。コンパイルはエラー 0 件、Verify は Walking 208/0、Frisbee 77/0。

**既定モード (36 投) の回帰確認**: 27/36 (run `20260923-122807-c9c769`)。落ちた 9 投 (`lob` のミス ×4、`far` の立ち取り／急旋回 ×3、`side` の急旋回 ×2) は**保留・拒否のログを一切通っていない** (`req hold` / `req reject` / `req cancel` が 1 行も出ていない) ので、今回の保留機構とは無関係。HEAD `2cde145` のコードだけで同じ投げを走らせた差分ラン (run `20260923-114552-6e8727`、18 投中 11 OK) でも同じ分類で再現する**既存の状態**。本 Issue の対象外とし、2cde145 の「立って取る計画の維持」とハーネス期待値 (`far` → `jump`) のずれとして**別 Issue 候補**に挙げる。

以下の行番号参照はすべて**旧** (調査時点、HEAD `2cde145`) の行番号。現在の行番号とは一致しない。

### 設計の要約

1. `ReqCatch` は受付ガードを通った後、**まだ保持中に見えるなら状態を一切触らずに保留する** (`req hold`)。`throw` ログと `StateChase` への遷移は受理したときだけ出る。
2. 犬 Owner の `Update` は毎フレーム、どの状態でも `ExpirePending()` (期限 → 両 Owner → 投げ手の在席 → `CanPlay` → **投げ手以外がディスクを取った** → 回収不能な終了通知) を先に見る。**受理は `IsEngaged` でないときだけ** `TryResolvePending()` が行う。期限判定と成立判定を分けたので、期限切れの保留が遅れて届いた解放で成立することはない (KD_DogFetchController 旧 L880-887 と同じ順序)。
2a. 「投げ手以外が取った」(`taken-by-other`) の判定には **`_pendingSeenThrower`** を使う。所持者／ディスク Owner が投げ手と一致したことを一度でも観測してから、初めて不一致を「他人に取られた」と扱う。観測前の不一致は**所有権の同期がまだ届いていないだけ**なので待つ (上限は `PendingHoldSeconds` の 1.0 s)。これが無いと、B が投げた通知が届いた時点で犬 Owner がまだディスク Owner を A と見ている、という普通の同期遅れで保留が消え、元の不具合が別の形で戻る。`HolderPlayer` 自体がディスク Owner を返すので、所持者と Owner は独立した証拠ではない (同じ同期が届いたかどうかを 2 通りに見ているだけ)。
2b. **解放済みの新しい投球は、接地中の進行中の追走に勝つ** (`req cancel reason=superseded` → `GiveUp()` → 翌フレームに保留が成立)。空中と口にくわえている間は従来どおり先に着地・完了させる。これが無いと、2 投目の通知は 1 投目の追走が終わるまで engaged で受理できず、期限切れで消える。
3. 受理は**元の `launchServerTime` のまま**。時計を付け替えないので、残り飛行だけが走査され、取れなければ `PlanGround` (地上回収) に自然に落ちる。保留から解決したときだけ `_stateEntered` を `noticeDelay` 分だけ戻し、0.1 秒の待ちを二重に払わない。
4. 保持判定の取消は「**この投球について一度解放を観測した後の保持**」だけに限定した (`_seenReleased && HeldNow()`)。投球前から残っていた古い `IsHeld == true` はもう取消理由にならない。これが §3 の推定に対する直接の修正。**`_seenReleased` は受理済みの投球だけのもの**で、書き込むのは受理時 (true) と `TransitionTo(StateIdle)` (false) の 2 箇所だけ。保留の格納・破棄はこのフラグに触らない — 追走中に次の投球通知が保留されただけで、進行中の追走が本物の持ち直しを検知できなくなるのを避けるため。
5. `IsHeld` / `HolderPlayer` の読み取りは全部 `HeldNow()` / `HolderNow()` を通す。`KH_Frisbee.IsHeldNow` / `HolderPlayer` と `KH_FrisbeeLauncher` は未変更なので、**投げ台は実値のまま保持中の投球を拒否する**。
6. `ReqFlightEnded` に送信者と**発生時刻**を足して 5 引数にした (オーバーロード禁止のため `KH_Frisbee.NotifyEnd` と同時に変更)。保留の照合を `_haveLaunch` ガードより前に置いたので、**保留中に届いた終了通知が捨てられなくなった**。保留を照合した後は `return` せず、そのまま live の照合にも落ちる。**投げ手本人の通知は (送信者, 世代) の組でどちらか一方にしか一致しないが、他人の `EndGrabbed` は「手がディスクに触れた」事実なので両方を正当に終わらせる** (`target=both`)。照合規則は**保留と live で同一**:
   - **その投球自身の投げ手から** (`sender == その投球の thrower`): 理由によらず**世代番号が一致すること**を要求する。世代は投げ手端末ごとのカウンタなので、その端末から来たときだけ意味がある。
   - **それ以外の送信者から**: `EndGrabbed` のみ受け付ける。判断材料は**送信者の認証と発生時刻**で、ディスクの Owner であることは要求しない。取得・回収の通知は所有権の同期より先に犬 Owner へ届きうるため、Owner 一致を要求すると永久に弾かれる。
   - **発生時刻 (`opTime`)**: `KH_Frisbee.NotifyEnd` が `Networking.GetServerTimeInSeconds()` を打刻して送る。他人からの通知は `opTime < その投球の発射時刻 - 1.0 s` なら無視する (その取得は**この投球より前**の出来事なので、これを終わらせる資格がない)。1.0 s は `ReqCatch` と同じ時計ずれの許容幅。`opTime` が NaN / 無限大なら無視する。投げ手本人からの通知は従来どおり世代一致だけで判断し、`opTime` は使わない。
   - ネット経路では `NetworkCalling.CallingPlayer` と `sender` を照合し、`CallingPlayer` が無効なら受け付けない (`ReqCatch` と同じ扱い)。
7. §5 の観測を `LogReq` 1 ヘルパーに集約した。1 イベント 1 行、毎フレームには出さない。**成立済みの投球が中止されるときは必ず理由が 1 行出る** (`held-regrab` / `claimed` / `owner-change` / `superseded` / `flight-ended:N`)。
8. 保留を待つ間、犬は**ディスクの予測位置を見る** (`_PositionAt` に保留した発射諸元を入れる。追走が直後に使うのと同じ予測)。投げた人を見つめ続けるのではない。

### 変更ファイル

| ファイル | 内容 |
|---|---|
| `Assets/KominkaHillside/Scripts/KH_FrisbeeCatch.cs` | 保留フィールド一式と `PendingHoldSeconds = 1.0f`、`debugHeldOverride` (ハーネス専用)、`HeldNow()` / `HolderNow()`、`LogReq` 系、`StorePending` / `ClearPending` / `ExpirePending` / `TryResolvePending` / `AcceptLaunch`、`ReqCatch` の分割、`ReqFlightEnded` の 4 引数化と保留照合、`Update` の保留処理と Ready の据え置き、`RetrievableReason` |
| `Assets/KominkaHillside/Scripts/KH_Frisbee.cs` | `NotifyEnd` が送信者 ID を付けて `ReqFlightEnded` を呼ぶ (直呼び・ネット送信の両方)。それ以外は未変更 |
| `Assets/KominkaHillside/Scripts/Editor/KH_DogWalkingEditor.cs` | Walking/5: 6 匹それぞれで `PendingHoldSeconds ∈ [0.5, 2]` と `debugHeldOverride == 0` (proxy + backing) |
| `Assets/KominkaHillside/Scripts/Editor/KH_FrisbeePrototypeEditor.cs` | Frisbee/5: 試作シーンの捕り手で同じ 2 件 |

`debugHeldOverride` は同期しない public int (0 = 実値 / 1 = 保持扱い / -1 = 解放扱い)。ClientSim ハーネスが到着順を決定的に再現するためだけのもので、Verify が**全シーンで 0 であることを検査する**。シンボルが無い・型が違う場合は 0 とみなさず FAIL にしてある。

### 決めたこと

- **保留中も撫では可**。`PetAllowed` は Idle / Ready / Drop のままで、保留は状態を変えないので撫でられる。ただし**保留の解決は撫で待機より優先**する (成立した投球は撫でより優先、という IMP-DOG-013 の既存方針と一貫)。
- **Ready の よしよし猶予 (`readyGraceSeconds = 2 s`) は保留中も進む**。`_heldSince` は Ready 入場で固定されるので、最大 1 秒が無言で消費される。実害は小さいと判断して許容した。実機で「保留の後に猶予がほぼ残らない」と感じたらここを見る。
- **保留中はディスクが `pickupable` のまま**。`ApplyState` は state で決めており保留は state を変えないため、Idle / Ready の true が続く。追走中 (Chase) も true なので新しい退行ではない。
- **ネット経路の判定は `NetworkCalling.InNetworkCall` / `CallingPlayer`**。`VRC.SDK3.UdonNetworkCalling.NetworkCalling` の両プロパティが Udon に公開されていることを SDK の extern テーブルで確認した (`VRC.Udon.VRCWrapperModules.dll` に `ExternVRCSDK3UdonNetworkCallingNetworkCalling` と `__get_InNetworkCall__SystemBoolean` / `__get_CallingPlayer__VRCSDKBaseVRCPlayerApi`)。`path=local|net` は経路そのもので決まり、ネット経路では `CallingPlayer` が無効または `playerId != thrower` なら `req reject reason=sender-mismatch` で弾く。**送信者照合は `ReqCatch` / `ReqFlightEnded` の両方で実装済み。**
  - 注意: `InNetworkCall` は**ネットワークイベントのハンドラ内から呼んだ直呼びにも引き継がれる**。今回の直呼び元 (`_Launch` は OnDrop / 後続フレーム、`_Recall` は Interact、`NotifyEnd` は `EndFlight` / `OnPickup` / `_Recall`) はいずれもネットワークイベントのハンドラではないので、`local` の判定は正しい。

### 観測ログ (1 イベント 1 行)

```
[KH_FrisbeeCatch] req accept   path=local|net thrower=7 gen=3 age=0.12 held=false ended=0 state=10 owner=1/1/7
[KH_FrisbeeCatch] req hold     path=local|net thrower=7 gen=3 age=0.12 held=true ended=0 state=10 owner=1/1/7
[KH_FrisbeeCatch] req resolved path=local|net thrower=7 gen=3 age=0.41 ageAtReceipt=0.12 heldFor=0.29 held=false ended=0 state=10 owner=1/1/7
[KH_FrisbeeCatch] req expired  path=local|net thrower=7 gen=3 age=1.13 ageAtReceipt=0.12 heldFor=1.00 held=true ended=2 state=10 owner=1/1/7
[KH_FrisbeeCatch] req cancel   reason=<下記> path=... held=... state=...
[KH_FrisbeeCatch] req reject   reason=not-owner|busy|implausible|claimed|cannot-play|age|sender-mismatch path=... held=... state=...
```

`req cancel` の理由は 2 種類ある。

- **保留 (まだ受理していない投球)**: `replaced` / `owner-change` / `thrower-left` / `claimed` / `cannot-play` / `taken-by-other` / `flight-ended:N`
- **成立済みの投球**: `held-regrab` / `owner-change` / `claimed` / `superseded` / `chase-timeout` / `retrieve-timeout` / `return-timeout` / `out-of-area` / `thrower-left` / `flight-ended:N`

受け取った飛行終了通知は 1 通につき 1 行出す (Codex M4)。`target` は照合の結果そのもの。

```
[KH_FrisbeeCatch] req end      reason=4 sender=7 gen=3 opAge=0.05 target=pending|live|both|dropped-gen|dropped-stale|dropped-reason|dropped-none
```

`owner=` は 犬 / 捕獲制御 / ディスク の Owner playerId。`thrower=-1` (`KH_Frisbee.cs` 旧 L198 で LocalPlayer が無効だった場合) はそのまま出る。既存の `throw v=` / `plan=` / `takeoff` / `catch OK` / `catch MISS` / `ready grace` / `ready go` / `[KH_Frisbee] launch` は文字列を変えていない。

### KD_DogFetchController の保留との違い

| 観点 | KD_DogFetchController (ボール) | KH_FrisbeeCatch (今回) |
|---|---|---|
| 保留する理由 | 要求イベントが投擲の証拠より先に届く | 投球通知が Pickup の保持同期より先に届く (同じ「別経路」問題) |
| 成立の証拠 | `BallLooksThrown()` = Pickup 非保持 **かつ** 直近にボールが動いた観測 (`VRCObjectSync` の transform 差分) | `HeldNow()` が false になったことだけ。飛行は発射諸元からの予測なので移動観測は要らない |
| 保留中の状態 | Idle のまま待つ | Ready (または進行中の系列) をそのまま維持。Ready では `UpdateReady` を飛ばし、立ってディスクの予測位置を見る (そのままだと旧 L796 で Idle へ落ちる) |
| 期限 | `FetchRequestGrace = 1.5 s`。期限判定を成立判定より先に置く | `PendingHoldSeconds = 1.0 s`。同じ順序。飛行が残っているうちに受理する必要があるので短い |
| 終了済み飛行の扱い | 概念なし (ボールに飛行終了通知が無い) | `ReqFlightEnded` を保留にも live にも同じ規則で照合。回収可能な理由なら `_flightEnded` を引き継いだまま受理して地上回収へ、`EndGrabbed` / `EndTimeout` なら保留を破棄 |
| 送信者照合 | 無し | `NetworkCalling.CallingPlayer` で照合し、終了通知も送信者 ID で照合する |

### 残る仮定

- **実際の通信到着順は 2 人実機で確認**。ClientSim ハーネスの `--mode=netorder` は `debugHeldOverride` で順序を人工的に作るだけで、本物の到着順・遅延・Owner 移譲は再現しない。
- **「保留中に別の人がディスクを取り、弱く放す」は 1 クライアントでは再現できない**。`taken-by-other` は所持者とディスク Owner の同期に依存する判定なので、2 人実機でしか確かめられない。
- **「追走中に相手が取得 → 即投球」は netorder の `steal-rethrow` で確認**。ただしこれは**同一ローカル投げ手の再投球**であり、別人の認証・所有権同期は含まない。そこは 2 人実機で確かめる。
- **他人からの `EndGrabbed` を Owner 照合なしで受け付ける**トレードオフ (上記 6) も 2 人実機で確認する。誤って追走が終わった場合、ログの `sender` に投げ手以外の ID が出る。
- `PendingHoldSeconds = 1.0 s` は初期値。実機ログの `heldFor` を見てから決める。
- Ready 猶予が保留中も進む件と、保留中もディスクが `pickupable` な件は許容と判断しただけで、実機で確認していない。


### Codex 指摘 / 対応 (2026-09-23)

| 指摘 | 内容 | 対応 |
|---|---|---|
| D1 | 保留中に投げ手以外がディスクを取ると、その人の弱い放しが「待っていた解放」に見える。他人の取得は `EndGrabbed` を出さない (`KH_Frisbee.OnPickup` は自分の端末で飛行中だったときしか通知しない) | 採用。`ExpirePending()` に所持者・ディスク Owner の照合を足し、投げ手以外なら `req cancel reason=taken-by-other` で破棄。**所持者／Owner の同期に依存するため 1 クライアントでは検証不能** |
| D2 | 解放済みの新しい投球が、接地中の進行中の追走に負けて期限切れになる。また `ReqFlightEnded` の保留分岐が無条件 `return` していて live の照合に届かない | 採用。接地中かつ口が空いていれば `req cancel reason=superseded` → `GiveUp()` で譲り、翌フレームに保留が成立。保留分岐の `return` を外して live にも落ちるようにした |
| D3 | `ReqFlightEnded` の `sender` が未認証。`ReqCatch` も `CallingPlayer` 無効を素通ししていた | 採用。両方でネット経路の `CallingPlayer` を照合し、無効なら受け付けない |
| D4 | 同じ投げ手からの世代不一致 `EndGrabbed` が、保留分岐のディスク Owner 経路で通っていた | 採用。**投げ手からの通知は理由によらず世代一致必須**に統一 (保留・live 共通) |
| D5 | 他人からの `EndGrabbed` に「ディスクの Owner であること」を要求すると、所有権の同期遅れで永久に弾かれる | 採用。Owner 要求を外し、**送信者の認証だけ**で受け付ける。残余リスクは上記 6 とログの `sender` で管理 |
| M3 | 成立済みの投球が中止される理由が出ないケースがあった | 採用。`owner-change` / `claimed` / `superseded` / `flight-ended:N` を 1 行ずつ追加 (毎フレームには出さない) |
| U1 | 保留中の犬が「投げた人」を見つめ続けるのは不自然 | 採用。保留した発射諸元から `_PositionAt` で**ディスクの予測位置**を向く |
| U2 | 期限切れ (`req expired`) のときの見え方 | コード変更なし。**実機ログの `heldFor` を見てから** `PendingHoldSeconds` を決める。1.0 s は初期値 |

**Codex 2 回目 (是正版のレビュー)。** D2 / D3 / U1 は妥当と判定。残り 4 件はすべて採用した。

| 指摘 | 内容 | 対応 |
|---|---|---|
| D1 退行 (高) | `taken-by-other` が、ごく普通の所有権同期遅れ (犬 Owner にはまだ前の所持者が見えている) でも発火し、保留を永久に壊す | 採用。`_pendingSeenThrower` を足し、**投げ手をディスク上で一度観測してからでないと** `taken-by-other` にしない。観測前の不一致は「同期中」として待つ |
| D4 残余 (中) | 古い他人の `EndGrabbed` が、新しい投球を取り消せてしまう | 採用。`ReqFlightEnded` に `opTime` (`NotifyEnd` での `GetServerTimeInSeconds()`) を足して 5 引数にし、**他人の通知は `opTime < 発射時刻 - 1.0 s` なら無視**。NaN / 無限大も無視 |
| M3 (中) | 黙って終わる `GiveUp()` がまだ残っていた (着地後の譲り・持ち直し・終了通知、回収の打ち切り、持ち帰りの打ち切り) | 採用。`CancelLaunch(reason)` に集約し、**すべての `GiveUp()` を経由させた**。唯一の例外は `_haveLaunch == false` が確定している UpdateChase 冒頭の 1 箇所 |
| M4 (中) | 受け取った終了通知が追跡できない | 採用。`req end` を 1 通 1 行で追加 (`reason` / `sender` / `gen` / `opAge` / `target`) |
| docs | 「1 つの通知は 2 つの組のどちらか一方にしか一致しない」は他人の `EndGrabbed` については誤り | 採用。コードのコメントと本文を修正 (`target=both` が正当に起きる) |

## 完了条件

- [ ] 2 人の実機で再現条件と原因を特定し、記録する
- [ ] 非マスターの取得 → 待機移動 → 投球 → 追走・キャッチ・持ち帰りが成立し、両者から確認できる
- [ ] マスターの投球も引き続き成立する
- [ ] 2 人が交互に持って投げても、所有権の切替後に反応しなくならない

## 関連

- [IMP-DOG-005 フリスビー飛行と基本ジャンプキャッチ](IMP-DOG-005-frisbee-jump-catch.md): 通常版・2 人での検証
- [IMP-DOG-013 フリスビーを持ち帰った犬をよしよしする](IMP-DOG-013-frisbee-pet-after-return.md): 直近の体験調整
- [IMP-DOG-012 フリスビーの 6 匹展開](IMP-DOG-012-frisbee-six-dogs.md): 今後の展開
