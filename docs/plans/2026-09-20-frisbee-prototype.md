# フリスビー飛行と基本ジャンプキャッチの試作 (記録と Play チェックリスト)

状態: 段階 2 (本シーン導入) まで実装し、**PCVR 1 人で「だいぶいい感じ」と評価** (2026-09-21)。項目別確認・2 人・Quest/iOS は未。作業票は [IMP-DOG-005](../development/issues/IMP-DOG-005-frisbee-jump-catch.md)、全体設計は [DES-DOG-002](../development/issues/DES-DOG-002-toy-behaviour.md)。ここは「順序と分担」と検証記録。何を作るかは Issue が持つ。

## 段階

| 段階 | 内容 | ゲート |
|---|---|---|
| 1 | **専用試作シーン** `Assets/Scenes/KH_FrisbeePrototype.unity` (平地・ラブ 1 匹・固定投球ボタン・回収ボタン・壁 1 枚) で 投球 → 追走 → ジャンプ → キャッチ → 着地 → 持ち帰り を通す。4 段階ポーズのクリップ生成と側面レンダ | Editor 検査 (Verify 35 項目) + Codex D/M/U → **ユーザーの Play 評価** |
| 2 | **本シーン導入** (散歩のラブ 1 匹 + ディスク 1 枚、照準モード、Wander / Fetch / リード / 集合 / 撫でとの排他、おもちゃメニューの「フリスビー」、広場の輪と場外の案内)。VR 確認は本シーンで | Editor 検査 + Codex → Build & Test → PCVR 1 人 → 2 人 (本シーン用チェックリスト) |
| 3 | **同じラブ1匹でバリエーション**。外からの走り込みを最初に、取り方・飛び方の候補を一通り試して採否を決める | [IMP-DOG-008](../development/issues/archive/IMP-DOG-008-frisbee-variations.md)。設計・分担・記録は [段階 3 の記録](2026-09-21-frisbee-variations.md)。通常版と採用候補の組み合わせをユーザーがVRで評価 |
| 4 | **1匹で通常版と採用バリエーションがOKになってから6匹展開**。犬種別の姿勢・口の位置を調整 | DES-DOG-002から別Issueを切る。複数ディスク・割当も具体化 |

2026-09-21 追加合意: 通常版の体験の仕上げはひとまずOK。次の制作は段階3とする。段階1〜2の項目別・2人確認はIMP-DOG-005に残し、Quest/iOSなどの公開向け検証も継続する。斜面への拡張は別途判断し、段階3〜4の前提にしない。

## 段階 1 の設計判断 (実装時に決めた値。Play で見直す)

- **飛行 = 予測**: `KH_Frisbee._PositionAt(p0, v0, t)` は閉形式 (水平は `e^(-drag t)` 減衰、鉛直は重力 + 水平速度²比例の揚力を積分)。ディスク Owner は毎フレームこの関数を踏んで kinematic で置く (VRCObjectSync が配る)。犬 Owner は同じ関数で予測するので、投げ手のクライアント上では一致が構造的に成立する。Verify で閉形式と微小ステップ積分の一致も確認 (最大誤差 3 mm / 3 秒)。既定値 (2026-09-21 改訂): gravity 9.81 / **drag 0.15 / lift 0.30** (揚力上限 0.95 g) / maxSpeed 11。固定投球 8 m/s・仰角 10° で約 1.9 s・13 m 飛び、降下は 1.4〜1.7 s に z 6〜8 m を通る (Node で閉形式を再計算)。初回 Play の drag 0.30 / lift 0.12 は 1.2 s・8 m で落ちて犬が一度も跳べなかった。飛行値・追走速度・犬の初期位置は **Build Scene が毎回書く (コードが正本)**、それ以外の調整値は Inspector が正本。
- **投球の事実の配り方**: `NetworkCallable` 引数付きイベント `ReqCatch(世代, p0, v0, サーバ時刻, 投げ手)` で犬 Owner へ (速度上限・距離・時刻の妥当性を検査)。着地・壁は `ReqFlightEnded(世代, 理由, 位置)` で世代一致のものだけ受理、掴み・回収 (EndGrabbed) は世代不問で受理 (手で取れる以上、止められて困らない)。犬 Owner は予測と観測 (VRCObjectSync の位置) が 3 m 以上 0.5 秒離れたら中断扱い (イベントが届かない場合の保険)。犬 Owner が途中で交代したら (前 Owner 退室) 新 Owner は計画を持たないので Idle へ戻す (継続は未実装)。ディスク Owner が交代して kinematic のまま誰も持っていなければ落とす。
- **候補時刻の選び方** (`Plan`, 0.1 s ごと): 現在 +0.2 s から 0.1 s 刻み 32 点。ディスク高さ − 着地地面高さ から「口の姿勢の高さ (mouthOffsetCatch.y = 0.774 m)」を引いた分を根元の上昇量 `rise` とし、**0.19〜0.75 m** の範囲でジャンプ。`tUp = √(2 rise / g)` で頂点キャッチ。下限 0.19 は「クリップの上昇 0.35 s を tUp に伸縮する倍率が 1.8 倍を超えない」境界 (Codex D2: 倍率を clamp すると頂点でキャッチ姿勢にならない。Build Scene がこの下限を強制し Verify が検査)。離地点 = キャッチ根元位置 − 進行方向 × takeoffSpeed × tUp、**ため開始点 = 離地点 − 進行方向 × takeoffSpeed × ため時間** (Codex U3: ため中も前進する)。**ジャンプ方向は「犬 → キャッチ点」の向き** (ディスクの進行方向ではない。正面から来れば正面で、後ろから来れば追う形で取る)。助走はキャッチ点までの距離に収まる長さに縮め、ほぼ真上に来るなら助走 0 の垂直ジャンプ。到着見積もりは旋回 + 加速 (12 m/s²) + 走り (4.5 m/s) で、一度選んだ候補は余裕 0.15 s 以内なら維持する (Play 2 回目: 距離/速度だけの見積もりでは次の再計画で同じ候補が不可能になり、低い候補へ落ち続けて跳べなかった)。レーンにレイが当たらず、着地点の真下に地面がある最初の候補。rise が 0.19 未満 (立ち姿勢の口 −0.30 m まで) は **走り抜けキャッチ** (ジャンプせず立ち姿勢の口を通す、Issue の「通常キャッチ」)。どれも無ければ着地予測点へ走って地上回収。
- **ジャンプ時の同期と姿勢**: `animSpeed = clipRise / tUp` を同期し、全クライアントで Animator `Jump` Bool + speed から同じ姿勢タイミングを導く。ため 0.25 s / 上昇 0.35 s / 下降 0.35 s / 回復 0.40 s (クリップ時間)。ため開始は「ため開始点に居て予定時刻に達した」ときだけ (**早出しは禁止**: 0.08 s 早いだけで 7 m/s のディスクは 0.5 m 手前。0.1 s 以上遅れたら次の候補へ再計画)。弧の時刻は予定時刻に固定し、クリップは開始フレームぶんだけ遅れる。空中の水平速度は離地時に「実位置からキャッチ根元へ tUp で届く速度」で解く (小さなずれをここで吸収。上限 chaseRunSpeed × 1.2 (初期実装は走速 × 1.5)、後退はしない)。離地後は再計画せず、掴まれても・再投球されても着地までは飛ぶ (再投球はランチャー側でも拒否)。
- **キャッチ判定**: 予定時刻 ±catchWindow (0.08 s) の窓の中で、口アンカーと予測位置の**縁までの距離** (中心距離 − 半径 0.12) ≤ catchTolerance (0.30 m) になった最初のフレームで成功。成功時だけディスクを口へ固定 (これが吸い込みの上限。固定は `PostLateUpdate` で Constraint 後の口に追従)。失敗は着地→ (まだ飛んでいれば再計画 / 落ちていれば地上回収)。ログ `[KH_FrisbeeCatch] catch OK/MISS mouth-predicted=… observed-predicted=…` に両方の距離を出す (後者が同期誤差。成否には使っていない = Codex U2、2 人検証で値を見る)。
- **低い投球**: ディスクが立ち姿勢の口より 0.30 m 以上低ければ空中を狙わず落ちてから拾う。その上〜rise 0.19 m 未満は走り抜けキャッチ。
- **姿勢クリップ**: `KH_Jump_Lab.anim` を `KH_FrisbeeJumpClipEditor` がラブの待機ポーズから生成 (古民家 `KD_CompanionMotionEditor` 方式の移植)。角度はこのモデルの制作値。ため・着地・回復は足裏基準で接地 (尾は除外)、空中キーは根元を待機高さのまま (上昇は制御側)。ためと着地の沈み込みは「前足を支点に胴を後ろ下がりに回して後足を接地」で作る。生成した 10 キーの側面・斜め図: [side](evidence/2026-09-20-frisbee-jump/jump-stages-side.png) / [quarter](evidence/2026-09-20-frisbee-jump/jump-stages-quarter.png)。Animator `AC_KD_Dog_Lab` に Bool `Jump` と State `Jump` (Any State → Jump、Jump → Run/Walk/Idle) を追加。
- **試作シーン**: ワールドシーンは触らない。Editor メニューは試作シーンを Additive で開いて単独保存し、閉じる。犬は移植元シーン `KH_DogPortSource` のラブをコピーし Wander/Fetch/Pet を外した個体 (VRCObjectSync のみ)。ディスクは円柱 φ0.24 (BoxCollider / Rigidbody / VRCPickup / VRCObjectSync)、layer Pickup。犬の子階層は layer 14。
- **持ったら位置取り** (ユーザー案 2026-09-21): ディスクが誰かの手にある間 (`VRC_Pickup.IsHeld` は同期値)、犬は Idle から `Ready` へ入り、持ち主の頭の向き 8 m 先 (`standoffDistance`) へ走って持ち主を向いて待つ。持ち主が向きを変えても 1.5 m 以上ずれるまで動かない。投げれば ReqCatch がそのまま追走へ切り替える。`standoffWhenHeld=false` で従来どおりその場から追う (ダッシュ&ジャンプ側のモード。並走の作り込みは未)。固定投球 (誰も持たない) のためには、犬を落下先の少し手前・横 (x 1.5, z 6.5、投げ台向き) に初期配置する。
- **追走速度**: 飛行中の追走だけ `chaseRunSpeed` 4.5 m/s (走りクリップは runSpeed 比で最大 2.2 倍速)。散歩用の 2.4 m/s では 7.5 m/s のディスクに追い越されて降下点に間に合わなかった。持ち帰り・地上回収は従来の走り/歩き。
- **照準モードで飛ばす** (ユーザー決定 2026-09-21): 物理的に投げる遊びではない (Quest/スマホでは投げ動作自体が難しく、投げの上手さを競いたいわけでもない)。ディスクを持つと持ち主にだけ **同じ `_PositionAt` で引いた軌道の線と着地マーカー** が出て、Use (トリガー) を押した瞬間にその軌道で飛ぶ。向きは VR = 持った手の向き、Desktop = 視線。仰角は −5〜40° に丸め、速度は固定 7.5 m/s (距離は仰角で調整。長押しチャージは廃止: 11 m/s は飛びすぎた)。`aimLaunch=false` で物理投げ / Desktop チャージへ戻せる (後日の「上級」候補)。**強さは向きと独立** (Play 4 回目の要望): VR は持った手側の右スティック上下 (`InputLookVertical`、VR では VRChat が使っていない軸)、Desktop はマウスホイールで 4〜11 m/s (既定 7.5) を変え、線と着地マーカーが伸び縮みする。**持っている間はディスクを 25% の半透明** (持ち主のクライアントだけ材質を差し替え) にして線を隠さない。スマホは右側バーチャルスティックが `InputLookVertical` に来る想定で未確認。来なければ端末別に「仰角だけ (強さ固定)」へ。
- **試作専用の入力**: `KH_FrisbeeStand` (Interact「Throw (fixed)」: 8 m/s・仰角 10°・+Z へ) と `KH_FrisbeeRecall` (Interact「Recall disc」)。犬がくわえている間・空中は拒否。手で持ったときの投げ方は上の照準モード。

## 実装記録 (段階 1、2026-09-20)

- 追加: `KH_Frisbee.cs` / `KH_FrisbeeCatch.cs` / `KH_FrisbeeLauncher.cs` (+ Program Asset)、`Scripts/Editor/KH_DogRig.cs` / `KH_FrisbeeJumpClipEditor.cs` / `KH_FrisbeePrototypeEditor.cs`、`Animations/KH_Jump_Lab.anim`、`Data/Frisbee/KH_Jump_Lab.json` (口の位置の事実)、`Data/Frisbee/*.mat`、`Scenes/KH_FrisbeePrototype.unity`。変更: `AC_KD_Dog_Lab.controller` (Jump 追加。移植資産の個別管理方針どおり直接書き換え)。
- メニュー: `Tools/KominkaHillside/Frisbee/` 1 Create Programs → 2 Build Jump Clip → 3 Render Stages → 4 Build Scene → 5 Verify (9 Inspect Rig は調査用)。
- Editor 検証: C# / U# (80 スクリプト) コンパイル成功、Console エラー 0、Verify **35 項目 失敗 0** (レビュー是正後) (Program Asset 3、Controller の Jump、クリップ長 1.35 s・非ループ・全バインド解決・ルートカーブ無し・接地キーの最下点、口の事実の一致、シーン配線・同期方式 Manual/None・backing heap の dogRoot / mouthOffsetCatch・NetworkIDs 残骸 0・閉形式と積分の一致)。
- クリップ生成で分かったこと: 世界軸回転をドライバ骨へ直接書くと表示側の節は 40〜75% しか回らない (Constraint のオフセット・二段骨格)。表示側の節の向きを測って残差を 3 回まで解く方式に変更。胴を起こす角度が脚の折りを打ち消すので、脚の角度は見た目より大きく指定する。尾を接地の基準に入れると「尾で立つ」姿勢になる (足裏基準に変更)。
- **Play 1 回目 (2026-09-21、ユーザー)**: 固定投球 3 回とも `plan=2` (地上回収) で跳ばず。原因は飛行が短い (1.16 s 着地) + 追走が遅い。上記の飛行値・追走速度・位置取り・初期配置を反映して再構築 (Verify 35/35)。**Play 2 回目 (2026-09-21)**: `plan=1 (rise 0.68)` が選ばれた直後に 3 → 2 へ落ちて跳ばず。計画のフラつき (加速・旋回を無視した到達判定) と、ディスク進行方向固定のジャンプ (犬の後ろにため開始点) が原因。ジャンプ方向・到達見積もり・候補維持を上記のとおり変更し、Node で固定投球を再計算: 犬 z 5.5〜8.5 のどこでも t=1.50 s・rise 0.60 の候補が成立。**Play 3 回目 (2026-09-21)**: 固定投球で 1 回成功。ジャンプの MISS は `mouth-predicted 0.74〜1.12` (同期誤差 0.07) → 踏み切りの早出し (±0.08 s 窓で最大 0.08 s 早く跳び、7 m/s のディスクは 0.5 m 手前) と向きの遅れ。早出し禁止・弧を予定時刻に固定・判定を縁基準 ±0.08 s 窓へ変更、MISS ログに dxz/dy/yawErr/inJumpState を追加。走り抜けは `0.307 MISS / 0.298 OK` で許容ぎりぎり → 縁基準で解消見込み。手投げは Desktop 短押し 4.5 m/s が低すぎ、長押し 11 m/s は飛びすぎ → 照準モードへ (上記)。**Play 4 回目 (2026-09-21、Desktop)**: 照準モードで `catch OK mouth-edge=0.255 at +0.08s` (窓の端で成立: まだ 1 フレーム前後遅い)。要望 2 件 (持っている間ディスクが線を隠す / 強さを向きと独立に) を反映。5 回目は本シーン・PCVR で実施 (下の Play 5 回目)。
- 未実施: 姿勢そのものの評価・2 人・Quest/iOS。以下のチェックリストへ。

#### 独立レビューと是正 (段階 1、Codex D/M/U、2026-09-20)

| 指摘 | 対応 |
|---|---|
| D2 高: アニメ速度の clamp で頂点 ≠ キャッチ姿勢 (rise 0.05 m では上昇 0.10 s に対しクリップ 0.19 s)。遅れ開始の位相合わせも効かない | clamp を撤去し rise の下限を 0.19 m (倍率 ≤ 1.8) に。下限未満は走り抜けキャッチへ分岐。ため開始は時刻窓 ±0.08 s のときだけ、外れたら再計画 |
| U3 中: ため中の前進が計画に無く、低いジャンプで空中速度が負になる | ため開始点 = 離地点 − 前進量。空中速度は後退禁止 |
| U1 高: 空中での再投球・掴みで着地を飛ばす | 空中状態では ReqCatch 拒否 (ランチャーも拒否)、掴みは着地後に処理 |
| D3 中: 別 Owner のディスクでは `IsFlying` が false で誤って地上回収へ | 判定を飛行終了イベントに統一。着地点未算出なら予測位置へ |
| D1 高: Owner 交代後に未設定の計画で UpdateJump が走る / ディスクが宙に残る | 計画を持たない新 Owner は Idle へ。ディスク側は交代時に kinematic を解除 (誰も持たない場合) |
| D5 中: 古い終了通知・無検証の投球値 | 投球世代を付与し不一致の終了を捨てる (掴み・回収は世代不問)。速度・距離・時刻の妥当性検査 |
| U5 中: 回収ボタンで犬が古い投球を追い続ける | 回収は `_Recall` → EndGrabbed を犬 Owner へ通知 |
| D6 中: 口元固定が Constraint 前の口を見る | 固定を `PostLateUpdate` へ (判定は Update のまま。1 フレームの遅れは Play で見る) |
| U4 中: 着地点の地面・レーンの検査不足 | 着地点の真下に地面 (±0.25 m) が無い候補を捨てる。レーンはため開始点から |
| M1 高: Verify が既に開いていたシーンを閉じる | 今回開いたシーンだけ閉じる (試作シーン・移植元シーンとも) |
| M3 中: クリップ再生成後の古い口位置が Verify を通る | JSON = proxy = backing の一致を検査に追加。minRootRise の下限も検査 |
| M4 低: JSON の小数点が OS 依存 | InvariantCulture で書く |
| M2 中: 本番と共有の `AC_KD_Dog_Lab.controller` を直接変更 | **見送り (意図的)**: Jump は本番へ持っていく状態で、Bool が立たない限り既存遷移に影響しない。移植資産は個別管理方針。本番投入時の統合対象として記録 |
| D4 中: SetOwner 直後の物理書き込みに取得確認が無い | **見送り**: 古民家 Fetch と同じ妥協。2 人検証の項目にし、必要なら取得待ちを入れる |
| U2 中: 成否が予測のみで、表示ディスクとの差を制限しない | **見送り**: 1 人試作では差 0。2 人検証で observed-predicted を記録してから設計 |
| M5 低: 6 匹に複製すると `frisbee.catcher` が Start 順で上書き | 試作は 1 対 1 (Verify)。展開時に担当裁定を設計 |

## 段階 2: 本シーン導入の計画 (2026-09-21 起案 → Codex P/M/D レビュー反映)

前提 (ユーザー決定): **散歩のラブにそのまま付ける** (別個体は用意しない。不変条件 1)。VR 確認は本シーンで行う。試作シーンは調整用に残す。**フリスビーは「おもちゃの一覧に増えた、動きの違う 1 種類」** (ユーザー 2026-09-21): 犬ごとに 1 枚、色はその犬の識別色。ボール系の見た目違い (まり・たい焼き) やディスクの柄違いは各おもちゃの中の話として後回し (DES-DOG-002)。段階 2 で配線するのはラブの 1 枚だけで、6 匹展開は「ディスクと Catch を 5 つ足す」だけになる構成にする。

### 置くもの

| もの | 場所・配線 | 所有者 |
|---|---|---|
| `KH_FrisbeeCatch_Lab` (Manual 同期、別 GameObject) | `KH_Dogs` 直下、`KD_FetchSystem_Lab` と同じ並び。`dogRoot` = `KD_Dog_Lab`、`dogAnimator` / `mouthAnchor` = Fetch と同じ参照、`frisbee` = 下のディスク、`hillsideLeashes` / `fetch` / `wander` / `activities` / `dogIndex`、pose facts = 試作と同じ JSON | 配線・pose facts・飛行値・追走速度は Build が毎回書く。`playArea` (広場中心・半径) はコード正本 (`KH_TerrainPrototypeConfig.Flats[PlazaFlat]`) |
| `KH_Frisbee_Lab` (Rigidbody / VRCPickup / VRCObjectSync / KH_Frisbee、layer Pickup、照準線・マーカー・半透明材質。色 = ラブの識別色 = ボールと同じ黄) | 広場のボール置き場の隣に `DiscHome(i)` (ボールと同じ規則、段階 2 は i = Lab のみ)。VRCObjectSync の Respawn 位置 = ここ | **位置・回転はディスクを新規作成したときだけ書く** (M4: 既存 Build の `initial` は `KH_Dogs` の有無なので流用しない。ボールは意図的に毎回だが、ディスクは人の配置を守る) |
| 「おもちゃ」ページ | **6 犬の行はそのまま**、各行に「ボール」「フリスビー」の 2 ボタン (タブは増やさない)。`KH_ToyRecall` は「犬番号 + おもちゃ種類」で門番と回収 (`discs[6]` / `discCatchers[6]`、ラブ以外は null = 「まだ」表示)。犬番号とおもちゃの対応は崩さない (M5) | Build が毎回 |
| 試作専用の台・回収ボタン | 置かない (手で持って照準モード) | — |

### 状態と「使用中」の定義 (M1)

- `IsIdle` = state == Idle。`IsEngaged` = state が Chase / Jump / JumpCarry / Land / LandCarry / Retrieve / Pickup / Return / Drop (**Ready は含まない**)。
- **Ready (持たれている間の位置取り)** は「他の遊びを奪わない」。開始条件 = `CanPlay()` (下表) が全て真。継続条件 = 毎フレーム同じ `CanPlay()`。偽になったら即 Idle (Ready は地上で止まっているだけなので、割り込む側が root を動かしてよい)。
- **投球 (ReqCatch)** も同じ `CanPlay()` を通る。Idle→Ready と ReqCatch の受付を 1 つの関数にする。
- Ready 中のボール投球: **Fetch が勝つ** (Fetch は Ready を見ないので始まる → 犬側が `!fetch.IsIdle` を見て Ready→Idle)。Ready 中のリード接続・集合も同様に成立する。

| `CanPlay()` の条件 | 正本 (M3) |
|---|---|
| リードされていない | `hillsideLeashes._IsLeashed(dogIndex)` |
| Fetch していない | `fetch.IsIdle` |
| 集合・かけっこの対象でない | `activities.Mode == 1 && activities._IsParticipant(dogIndex)`、`activities.Mode == 2` |
| おやつ対応中でない | `wander.IsTreatBusy` (**Fetch の通常投球は拒否しない項目。フリスビーは意図して拒否**: 食べている最中に位置取りへ走り出さない) |
| 休憩・寝ていない | `wander.IsResting`、`wander.IsAsleep` |
| 寝床・ライブ | この世界には配置していない (Build が null にする)。Verify で「未配線」を検査し、再導入時は競合検証を必須にする |
| 遊べる場所 | ディスクの発射点と Ready の目標が `playArea` (広場の円) の内側 (P2: 斜面・広場の縁・地面のない前方へは Ready も追走も出さない。計画の候補が地面検査で落ちるのは保険であって保証ではない) |

### 取り合いの規則

| 状況 | 規則 | 実装箇所 |
|---|---|---|
| フリスビー Engaged | Wander は動かない (Fetch 中と同じ扱い。休憩・寝床の凍結解除も同じ) | `KD_DogWanderController.Update` の Fetch ゲートの隣に `!frisbeeCatch.IsIdle` (Ready 中も Wander は root を書かない)。`HillsideLeash` の Fetch ゲートも同じ |
| Engaged 中に投げられたボール | Fetch は始まらない | `KD_DogFetchController.StartFetch` に `frisbeeCatch.IsEngaged` (**受付の唯一の口**) |
| Engaged 中のリード接続要求 | 拒否 | `KH_LeashManager.ReqAttach` の `fetchers[dog].IsIdle` の隣 |
| Engaged 中の集合対象 | 選ばれない | `KD_GroupActivities.Available` |
| **同期遅延で両立してしまったとき** (D2) | **受理済みのリード・集合参加が勝つ**。犬側は Owner tick で観測し、地上なら即 GiveUp (ディスクを放す)、空中なら「キャッチ・持ち帰りを中止予約」して着地後に Idle。着地までリード・集合側は root を動かさない (Wander が `!IsIdle` で待つ) | `KH_FrisbeeCatch.Update` (Fetch の `_IsLeashed → GiveUp` と同じ形) |
| 撫で (M2) | **Ready の静止中は許可、Engaged 中は抑制** (ジャンプ中に頭の骨へ追加回転を書かせない) | `KD_DogPetController` の Fetch 用ゲート (`dogState != Idle`) の隣に `frisbeeCatch.IsEngaged` |
| 共有 Animator の担当 (M2) | Idle 中はフリスビーが Animator を**書かない** (試作の `Coast()` を Idle から外す)。非 Idle へ入るとき自分の出力キャッシュを無効化して書き直す。Idle へ戻るとき Wander の出力を失効させる (Fetch と同じ `wander._InvalidateLeashAnimation()`) | `KH_FrisbeeCatch.ApplyState` |

### Owner の契約 (D1)

- dogRoot・`KD_FetchSystem_Lab`・`KH_FrisbeeCatch_Lab` は**同じ Owner** が前提 (既存の Fetch と同じ。犬側で SetOwner を呼ばない。分かれるのはマスター交代の一瞬で、収束する)。
- ディスクは `KH_FrisbeeCatch_Lab` の Owner へ ReqCatch を送り、受信側は dogRoot の Owner でなければ捨てる。不一致は Fetch の `WarnIfOwnerSplit` と同じ警告を出し、受付を止める (継続の実装はしない)。退出試験では 3 者の Owner を記録する。

### ディスクの回収と場外 (D3)

- メニュー回収: Owner 取得を待つ → 使用状態を再確認 (`IsHeldNow` / `catcher.IsEngaged`) → `KH_Frisbee._Recall()` (飛行終了 + 犬へ EndGrabbed) → 頭の向き 1.2 m 先の地面へ (ボールと同じ置き方)。飛行中でも回収できる (犬が投球を拒否した飛行中のディスクを戻す手段)。
- 場外: 飛行中に `RespawnHeightY` を下回ったら `EndFlight` (EndTimeout) → VRCObjectSync の Respawn が台へ戻す。犬は終了通知で Idle。

### 途中参加 (D4)

同期は state / throwerId / animSpeed のみ。合格条件: Ready・飛行・くわえ帰還・中断後は root と口元のディスク・Pickup 可否が復元される。**ジャンプ中に入った人のクリップ位相のずれは許容**し、着地後に通常表示へ戻ることを合格条件にする。

### 負荷 (D5) と端末 (D6)

- Ready の目標 (頭の向き 8 m 先) の更新を 10 Hz に間引く (移動の補間は毎フレーム)。本シーンで「未保持 / 保持+Ready / 投球」の増分を `vrc-performance` の手順で測る。
- 照準線 (LineRenderer 40 点 + 区間レイ) は保持者のクライアントだけ。半透明材質 (`Legacy Shaders/Transparent/Diffuse`) は Android/iOS ビルドで shader 参照を確認し、実機で両眼表示・地面との重なり・可読性を見る。PCVR 合格を Quest/iOS 合格に読み替えない。

### やらないこと (段階 2 の範囲外)

他 5 匹への展開・ディスク複数・犬の割当 (DES-DOG-002)、斜面でのジャンプの制作 (Issue 対象外。**安全な中止条件は上の playArea で今回入れる**)、ダッシュ (並走) とフリップ (別 Issue)、音・エフェクト、Owner 交代時の継続。

### Verify に足す項目 (M5)

6 匹維持・Catch はラブだけ・ディスク 1 対 1 (他 5 匹は未配線で正常) / Catch の backing が Manual、dogRoot・Animator・口参照が Fetch と一致 / pose facts JSON = proxy = backing、Jump とクリップ、minRootRise の下限 / Wander・Fetch・Manager・集合・回収・撫での追加参照 (保存・再読込後の backing) / メニュー 7 行の表示・イベント・対象、材質参照 / playArea が広場の平坦と一致 / NetworkID の欠損・重複 0 / 寝床・ライブが未配線。

### ゲート

Editor 検査 (`Walking/5 Verify` に上の項目) + Codex D/M → **NetworkID の失敗を解消** → Build & Test → PCVR 1 人 (本シーン用チェックリスト) → 2 人。Quest/iOS は残件として別途。

### 段階 2 の実装記録 (2026-09-21)

- 変更: `KH_FrisbeeCatch` (CanPlay / Claimed / IsEngaged、Ready の 10 Hz 目標更新と playArea への丸め、空中で譲る予約 → 着地後に GiveUp、Idle で Animator を書かない + 出入りでキャッシュ失効 + Wander の失効、撫での Suppress/Resume、Owner 不一致警告)、`KH_Frisbee` (Respawn 高さで飛行終了)、`KD_DogWanderController` / `.HillsideLeash` (フリスビー中ゲート = Fetch と同じ枝)、`KD_DogFetchController.StartFetch`、`KD_GroupActivities.Available`、`KH_LeashManager.ReqAttach`、`KH_ToyRecall` (おもちゃ種類: ボール / ディスク、ディスク回収は `_Recall` → 配置)、`KH_HandMenu` (各犬の行に「ボール」「フリスビー」の 2 ボタン、状態は短語で 2 種併記、ラブ以外は「まだ」)、`KH_HandMenuEditor` (2 ボタン、語彙を字形へ追加 = 2 字、ボタン数検査 46)、`KH_DogWalkingEditor` (`FrisbeeKeys = {Lab}`、`DiscHome(i)` = ボールの 2.2 m 後ろ、ディスクは新規作成時だけ配置、Catch と近隣の配線、playArea = 広場の円、Verify 12 項目追加)、`KH_FrisbeePrototypeEditor` (ディスク生成・Catch 設定を共有ヘルパへ抽出)。
- Editor 検証: C# / U# エラー 0、Console 0。**Walking Verify 134/134** (レビュー是正後) (フリスビー: Lab のみ配線・他 5 匹は意図して未配線、root/Animator/口の共有、近隣参照、Manual/None、pose facts = JSON、Animator の Jump、広場上、playArea、部品、backing、寝床/ライブ未配線)。**Menu Verify 22/22**。試作シーン Verify 35/35 (回帰)。
- NetworkID: 18:47 の Build 失敗を SDK の割当ロジックで再現したところ **エラー 0** (86 件、新規 2 = ディスクと Catch を保存済み、重複 0)。未保存だった別作業のオブジェクトが原因だった可能性が高い。次の Build & Test で確認。
- 未実施: 本シーンでの Play・PCVR・2 人。

#### 独立レビューと是正 (段階 2、Codex D/M、2026-09-21)

| 指摘 | 対応 |
|---|---|
| D1 高: playArea が受付位置にしか効かず、外向きの投球で犬が広場を出る | 犬が行く場所すべて (ため開始点・キャッチ根元・着地点・走り抜けの根元・地上回収先・追走の目標) を playArea (余白なし) で検査し、外なら候補を捨てる / 中止。受付だけ余白 2 m |
| D2 高: Idle 復帰で `_speed` と Animator の speed が残り、Ready→Idle 後も Run/Walk を立て直す | Idle 遷移で `_speed=0`、speed=1 を書き戻し、Idle では Animator を一切書かない (状態別更新の後に Idle を再確認) |
| D3 高: Fetch の受信 (`ApplyState`) が Idle のたびに `_ResumePetting` を呼び、Engaged 中の抑制を解く | Fetch の再開条件に `frisbeeCatch.IsEngaged` を追加 (Fetch 側 1 行) |
| D4 中: 譲る予約中も新規キャッチする | 予約中はキャッチ判定を通さない (くわえていた場合だけ着地後に放す) |
| D5 中: 場外 (EndTimeout) の後に Respawn 済みのディスクを拾いに行く | Grabbed と Timeout は回収対象外に統一 (`Retrievable()`) |
| M1 中: 「FrisbeeKeys に足すだけ」だが facts/クリップ/Jump は Lab 固定 | Build は `KH_FrisbeeJumpClipEditor.Breed` 以外のキーを明示的に拒否 (例外)。6 匹展開時は犬キーから facts/クリップ/Controller を解決する入口を作る (追加合意後の段階 4) |
| M2 中: Verify が backing の一部しか見ない、回収配列は非 null のみ、NetworkID は dangling のみ | backing で全 pose facts + playArea + 近隣参照 + dogIndex、Wander/Fetch の backing `frisbeeCatch`、回収/Manager/集合の配列が各犬の実物と一致、NetworkID の ID・GameObject 重複と同期物の未登録を検査 (134 項目) |

是正後: Walking Verify **134/134**、Menu Verify 22/22、C#/U# エラー 0、Console 0。

### 広場の見せ方 (2026-09-21、ユーザー要望 → Codex U/V/P 設計レビュー → 実装)

要望: 広場でしか遊べないことを分かるようにしたい、ただし景観を壊さない。検討 4 案 (照準で教える / 犬の位置取りで教える / 持っている間だけ薄い輪 / 地面そのもので表す) のうちユーザーは **1 + 3**。Codex は「進めてよい、ただし 4 点」→ 反映:

| 指摘 | 対応 |
|---|---|
| U/V 高: 軌道を縁で切ると「ディスクがそこで止まる」と誤解する | 線は実際の着地点まで描く。場外は **線とマーカーを灰色 + マーカーに × 印** |
| U 高: 輪が背後・遠くだと犬が来ない理由が分からない | 場外で持ったとき・場外へ照準したときだけ、マーカーの上 0.5 m に **「広場の中で遊ぼう」** (world-space TMP、持ち主向き) |
| P 高: 輪と受付条件がずれる (受付余白 2 m) | 受付余白を 0 にし、輪 = 犬が動ける円 = 受付の円 |
| V 中: 3 cm・alpha 0.3・水色は仮値、Quest 未検証 | 輪は淡い暖白 (alpha 0.3、幅 3 cm、地面 +3 cm)、照準は水色のまま。PCVR/Quest で見て確定 |

見送り: 取得直後だけ全周を見せて通常は弱める (V) — Play 後に判断。6 匹でも輪は各画面 1 つ (自分が持つディスクの分だけ)。実装: `KH_Frisbee` (輪の生成は持った時に 64 点を地面へレイ、材質差し替え、× 印、ヒント)、`BuildDisc` (材質 `KH_FrisbeeAimOut` / `KH_FrisbeeArea`、`Cross`、`AreaRing`、`Hint`)、字形は Menu/2 (289 字に含まれ追加なし)。試作シーンは playArea 無しなので出ない。Walking Verify 134/134、Menu 22/22、試作 35/35。

### Play 5 回目 (2026-09-21、本シーン・PCVR 1 人、ユーザー)

「だいぶいい感じ。輪も競技場のようには見えず大丈夫」。本シーンで散歩のラブがディスクを取れることと、広場の輪・場外の案内が景観の邪魔にならないことを確認。下のチェックリストの項目別結果 (割り込み・場外・回収・回帰) は個別には報告されていないため未チェックのまま。輪の幅・濃さ (3 cm / alpha 0.3) はこの評価で仮確定。

## Play チェックリスト (段階 1〜2。上から順に、必要人数順)

事前: Console にエラー無し / `KH_FrisbeePrototype.unity` を開く (ワールドシーンの未保存編集は先に保存するか捨てる) / Play 前に Verify が失敗 0 であること。

### §0 エディタで判定できない項目

- [ ] Jump クリップが Run からのクロスフェード (0.1 s) で破綻しない (足の滑り・体の跳ね)。**ここが NG なら姿勢の議論に入らない**
- [ ] `Networking.GetServerTimeInSeconds` 基準の踏み切りタイミングが ClientSim / 実機で合う (ログの `mouth-predicted`)
- [ ] VRCObjectSync + kinematic の飛行が非 Owner 画面で滑らかか (実機 2 人)
- [ ] `NetworkCallable` の `double` 引数が実機ビルドで通る (ClientSim では通っても実機で落ちる可能性。落ちたら float に変える)

### 1 人 (ClientSim / Desktop / PCVR)

- [ ] 「Throw (fixed)」を押す: ディスクが前へ滑空しゆっくり落ちる (約 2 秒・13 m)。極端に浮き上がらない・急落しない
- [ ] ディスクを手に持つと犬が 8 m 前へ走って自分を向いて待つ (`Ready`)。向きを変えるとついてくる
- [ ] 犬が 0.25 s ほど見てから走り出し、踏み切り点で **ため → 跳躍 → 口が届く → 着地の沈み込み** の順に見える
- [ ] Console `[KH_FrisbeeCatch] plan=1 …` と `catch OK mouth-predicted=0.0x` が出る。MISS なら `rise` と `mouth-predicted` を記録
- [ ] 空中で進路を変えない・遠くから吸い込まれない (成功時のディスク移動は 0.3 m 以下)
- [ ] 着地後に走りへ戻り、投げた人の 0.9 m 手前で止まって顔を向ける。3 秒以内に口から取れる (Pickup)。取らなければ足元へ置く
- [ ] 同じ固定投球を 5 回繰り返して成否が安定する (安定しなければ takeoffSpeed / catchTolerance / clipRise を Inspector で調整し値を記録)
- [ ] ディスクを持つと軌道の線と着地マーカーが出る (VR: 手の向き / Desktop: 視線)。仰角で距離が変わり、Use で線どおりに飛ぶ。線が出ない・ずれる場合は `aimSpeed` / 仰角の丸め
- [ ] 犬を狙って投げると跳んで取る。少し外して投げると走って取る (狙う/外すの手応え)
- [ ] 低い投球 (仰角 0°、4 m/s): 走り抜けキャッチ (`run catch OK`) か落ちてから拾うか。どちらになったかと見た目
- [ ] 高すぎ・遠すぎ (10 m/s、仰角 30°): 追いかけて落ちてから拾う。空中を狙わない
- [ ] 壁 (z=22) に当てる: ディスクが壁で落ち、犬は跳ばずに拾いに行く
- [ ] 飛行中に人が掴む: 犬が中止して Idle に戻る
- [ ] くわえて戻る途中に「Recall disc」を押す: 拒否される (ログ)。Idle 中に押すと台へ戻る
- [ ] ディスクを崖外相当 (y < −20) へ落とす手段が無いので、代わりに Recall で復帰できることを確認

### 本シーン (段階 2、1 人 → 2 人)

事前: `VRCDefaultWorldScene` を開く / Console エラー無し / Walking Verify 134・Menu Verify 22 が失敗 0 / Build & Test をし直した。

- [ ] 広場のボール置き場の 2.2 m 後ろにラブの黄色いディスクが 1 枚ある。手元メニュー「おもちゃ」に各犬の行「ボール」「フリスビー」があり、ラブ以外のフリスビーは「まだ」
- [ ] ディスクを持つとラブが 8 m 前へ走って自分を向く (Ready)。ほかの 5 匹は散歩・自由行動のまま
- [ ] 線を犬の少し先に置いて Use → ラブが跳んで取り、持ち帰って 0.9 m 手前で止まる。3 秒以内に口から取れる
- [ ] 取ったあと Idle に戻ったラブが普通に自由行動へ戻る (立ちっぱなし・走りっぱなしにならない = Animator の引き継ぎ)
- [ ] ラブが Ready で待っている間に撫でられる。追走・ジャンプ・持ち帰り中は撫でが効かない
- [ ] Ready 中にラブへリードをつなぐ → つながり、Ready が解ける。Ready 中にラブのボールを投げる → Fetch が始まり Ready が解ける
- [ ] 追走〜持ち帰り中にリードをつなごうとする → つながらない。集合を始める → ラブは選ばれない
- [ ] ラブをリードで連れている間にディスクを持つ → ラブは位置取りしない。投げても追わない (地面に落ちる)
- [ ] ディスクを持つと広場の縁に淡い輪が出る (持ち主だけ)。8 m 先の犬を見ていても気づくか、景観の邪魔にならないか
- [ ] 広場の外へ向けて照準する → 線とマーカーが灰色になり × 印、「広場の中で遊ぼう」が出る。線は着地点まで残る
- [ ] 広場の外 (坂・見晴らし) でディスクを持つ → 位置取りしない、ヒントが出る。広場の外へ投げる → 追わない
- [ ] メニュー「フリスビー」で回収: Idle なら目の前へ。飛行中に呼ぶ → 飛行が止まって目の前へ来て、犬は追わない
- [ ] ディスクを崖下へ落とす (Respawn 高さ) → 台へ戻り、犬は Idle
- [ ] 他 5 匹の散歩・集合・ボール Fetch がこれまでどおり (回帰)

### 2 人 (実機)

- [ ] 投げる人 ≠ 犬の Owner (マスター) で、犬が動き出す・跳ぶ・くわえる・戻るが両画面で一致する。`observed-predicted` の値を記録
- [ ] 非 Owner の画面で飛行が滑らかか、ジャンプの姿勢タイミングが遅れて見えないか
- [ ] 投げた人が退室: 犬が Idle に戻り、ディスクが宙に残らない
- [ ] 途中参加: 飛行中・くわえ中に入った人の画面で状態が復元される (口元にディスク)

### 症状 → 見るところ

| 症状 | 見るところ |
|---|---|
| 犬が動かない | Console `[KH_FrisbeeCatch] wiring incomplete` / `pose facts are zero` → 4 Build Scene からやり直し。`plan=0` のままなら候補が無い (高さ・到達)。`maxRootRise` / `runSpeed` |
| 跳ぶが口が合わない (MISS が続く) | `mouth-predicted` が大きい: 姿勢の口の位置 (JSON) と実再生の差 → クリップの catch キー・`clipRiseDuration`。`observed-predicted` が大きい: 同期遅延 (2 人のみ) |
| 空中で滑って見える | 踏み切り時の解いた水平速度が大きい (上限は `chaseRunSpeed × 1.2`。初期実装の `runSpeed × 1.5` は履歴)。`takeoffSpeed` を上げて事前に走らせる |
| 姿勢がおかしい | `Temp/KH_Frisbee/sheet-side.png` と比較。クリップ角度は `KH_FrisbeeJumpClipEditor.Keys()`。直したら 2 → 3 → 4 → 5 を通す |
| ディスクが浮いて残る | Owner の交代で `_Release` が届いていない。Recall で復帰。ログの `launch refused` |
| 投げても反応しない (Desktop) | `[KH_Frisbee] drop ignored (max v=…)` → `throwSpeedThreshold` |
