using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_DogWanderController の partial (Rest)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_DogWanderController
    {
        // ================================================================ IMP-DOG-014

        /// <summary>走った後の休憩。休んでいたら true (呼び出し元が早期 return する)。
        ///
        /// ## 疲労値を作らない
        ///
        /// 却下表の「体調」と同型になるので、**数値は持たず時間的なリズムとして持つ**。
        /// 入る条件は はぁはぁ (IMP-DOG-015) と**同じ `_breathRunAccum` を共有する** —
        /// 別々に測ると「息は上がっているのに休まない」「休んでいるのに息が上がっていない」が
        /// 出る。共有しているから **「伏せて、はぁはぁ言って、落ち着く」が自然に一続きになる。**
        ///
        /// ## 4 匹が一斉に伏せないこと (危険 3)
        ///
        /// 長さを <see cref="restDurationMin"/>〜<see cref="restDurationMax"/> の幅で採り、
        /// **`Rand01` (= `seedSalt` が混ざる) で犬ごとに変える。**
        /// 先行実装 `com.virtualcampers.wildlife-area` が
        /// `restDurationMin/Max` + 個体ごとの種で同じ問題を解いている。
        ///
        /// ## 置物にならないこと (危険 2)
        ///
        /// Fetch・おやつ・催促には**この関数へ来る前に**負ける (割り込み順)。
        /// 撫では主状態 (`dogState`) を変えないので**伏せたままでも成立する**。
        /// 接近で起きるかは <see cref="restWakesOnApproach"/> で切り替える。</summary>
        bool UpdateRest()
        {
            // ---- 休んでいない: 入るかどうかだけ決める ----
            if (_restPhase == RestPhaseNone)
            {
                if (Time.time < _restReadyAt) return false;
                if (_breathRunAccum < restTriggerRunTime) return false;
                // **今まさに人の相手をしているなら休まない** (_paused は割り込み 2 の材料)
                if (restWakesOnApproach && _paused) return false;
                // **じゃれ合いの最中は休憩へ入らない** (破壊的監査 v2 RC-7)。
                // restTriggerRunTime (4s) ≪ チェイス実走 (18〜28s) なので、
                // 入場を許すとほぼ毎回のチェイスが走り始め数秒で崩壊する正帰還になる
                // (IMP-DOG-012 が伸ばした 2.4 倍を IMP-DOG-014 が黙って打ち消していた)。
                // **借金 (_breathRunAccum) は清算せず持ち越す** — 遊び切ってから
                // 水を飲みに行く、が意図した絵 (トリアージ視点 2 反論 4)。
                // 休憩 (1.8) はじゃれ合い (3) より先に判定されるので前フレームの結論を読む
                if (_playingPrev) return false;
                // 裏山改変 (2026-10-02 ユーザー要望「台の上には休憩し始めたりもしない」): 洗い場の立入りの円の中では休憩に入らない
                // (伏せるのはその場なので、場所の候補はこの 1 点。円の外へ出る歩きは UpdateStationLeave、HillsideStationLeave.cs)
                if (KeepOutRestBlocked()) return false;

                _restCount++;
                _restStillSince = -1f;
                // 水場が使えるなら**まず飲みに行く**。無い / 遠すぎるならその場で伏せる —
                // **水場が無くてもサイクルは成立する** (Phase 1 の挙動へ落ちるだけ)
                if (WaterUsable())
                {
                    _restPhase = RestPhaseToWater;
                    _waterStartTime = Time.time;
                }
                else
                {
                    EnterLieDown();
                }
            }

            // ---- 人が来たらどの段階でもやめる (置物にならない) ----
            if (restWakesOnApproach && _paused) { EndRest(); return false; }

            // ---- ① 水場へ歩いていく ----
            if (_restPhase == RestPhaseToWater)
            {
                // **届かない目標を永久に追わせない** (Fetch の returnTimeout と同じ保険。
                // 破壊的監査 RC-2 で「Chase にあって Return に無かった」と指摘された形)
                if (Time.time - _waterStartTime > waterApproachTimeout || !WaterUsable())
                {
                    EnterLieDown();
                }
                else
                {
                    Vector3 spot = DrinkSpot();
                    Vector3 to = spot - dogRoot.position; // sync: local-ok: 到着判定のみ — 目標は DrinkSpot の共有導出 (水場の絶対座標 + dogIndex)
                    to.y = 0f;
                    if (to.magnitude <= drinkArriveRadius)
                    {
                        _drinkUntil = Time.time + drinkDuration;
                        _restPhase = RestPhaseDrink;
                    }
                    else
                    {
                        // **stopDist は 0。** ここへ `arriveRadius` (0.25m) を渡していたせいで
                        // 犬は目標の 0.25m 手前で止まり、**水場から器 1 個ぶんずれて飲んでいた**
                        // (2026-08-23 ポメで発覚)。おやつが「到着判定より少し奥まで詰める」と
                        // 書いているのと同じで、**止まる点と到着判定は別物**
                        StepToward(spot, 0f, walkSpeed);
                        // **見つめながら寄る。** おやつが「持ち主の顔ではなくおやつそのものを
                        // 見つめる (食い意地が読める)」でやっているのと同じ —
                        // これが無いと「水場へ歩いていく」ではなく「たまたまそこへ移動した」に見える
                        if (petController != null)
                        {
                            petController._SetGaze(DrinkGaze(), 0f);
                            petController._SetUrge(0.8f);
                        }
                        return true;
                    }
                }
            }

            // ---- ② 飲む ----
            if (_restPhase == RestPhaseDrink)
            {
                // **飲むクリップは無い。** おやつが「食べるクリップが無い → くわえポーズ
                // (頭を下げる) を流用」で成立したのと同じ手を使う (IMP-TRT-001 の前例)。
                // `_eatPose` は ApplyMoveAnim が Animator の `Pickup` へ落とす
                Coast();

                // **まず器の方を向く。向き切ってからポーズを立てる。**
                // 着いた瞬間にポーズを立てると FaceTowards が止まり、
                // **器の横を向いたまま飲む**ことになる (2026-08-23 ユーザー
                // 「ずれたんだったら水の岩の方を向く」)。扇で立ち位置が横へ寄るぶん、
                // 着いてから向きを変える時間が要る
                bool facing = true;
                if (WaterUsable() && _speed <= stopSpeedThreshold)
                {
                    Vector3 want = DrinkGaze() - dogRoot.position; // sync: local-ok: 向きの判定のみ — 位置の目標には使わない
                    want.y = 0f;
                    if (want.sqrMagnitude > 0.0001f)
                        facing = Vector3.Angle(dogRoot.forward, want) <= DrinkFaceToleranceDeg;
                    if (!facing) FaceTowards(DrinkGaze());
                }
                // 向き切ってから頭を下げる。**以降は回さない** (BUG-DOG-001)
                if (_speed <= stopSpeedThreshold && facing) _eatPose = true;

                // **注視は差し込まない** — ポーズが頭を下げているところへ注視の追加回転を
                // 重ねると喧嘩する (設計書 §4「書き手は 1 つ」)。**尻尾だけ振る** —
                // ここもおやつの食べる枝と同じ
                if (petController != null) petController._SetUrge(1f);

                if (Time.time >= _drinkUntil) EnterRestMove();
                else return true;
            }

            // ---- ②b 飲み終わったら**少し離れてから**伏せる ----
            // **水場で寝転がらせない** (2026-08-23 ユーザー)。水場は縁側の前に据えてあり、
            // そこは「通る場所・眺める場所」であって寝る場所ではない。
            // 犬も水を飲んだらふつう少し移動してから落ち着く
            if (_restPhase == RestPhaseMove)
            {
                Vector3 toRest = _restSpot - dogRoot.position; // sync: local-ok: 到着判定のみ — 目標は RestSpot の共有導出
                toRest.y = 0f;
                if (toRest.magnitude > drinkArriveRadius
                    && Time.time - _restMoveStart < waterApproachTimeout)
                {
                    StepToward(_restSpot, 0f, walkSpeed);
                    return true;
                }
                EnterLieDown();
            }

            // ---- ③ 伏せて休む ----
            if (Time.time >= _restUntil) { EndRest(); return false; }
            // 裏山改変 (2026-10-02): 押されるなどで洗い場の円の中へ入ったら伏せ続けない (次の選び直しで円の外へ歩く)
            if (KeepOutRestBlocked()) { EndRest(); return false; }

            // 休憩中の足: 減速し切るまでは普通に歩いて止まる
            // (**位置を自分で書かない** — Coast が加減速・接地・Animator を全部持っている)
            Coast();

            // **足が止まってから伏せる。** 走ったまま伏せると「急に畳まれた」ように見える
            // (IMP-FET-004 で潰した「アニメだけ先に切り替わる」と同じ形)
            if (_speed > stopSpeedThreshold) _restStillSince = -1f;
            else if (_restStillSince < 0f) _restStillSince = Time.time;

            // **伏せる前に向きを決める。** 伏せてから回すと BUG-DOG-001 の
            // 「静止ポーズのまま足が地面を擦る」が出るので、**回すのは settle の間だけ**
            // **見るのは _wasLieDown (Animator へ実際に書いた値)。**
            // _wantLieDown は Update 冒頭で毎フレーム false に落とされるので、
            // ここで見ると**常に「まだ伏せていない」になり、伏せた後も回り続ける**
            // (2026-08-23 ユーザー「伏せた後に回ってた」)
            if (!_wasLieDown && _speed <= stopSpeedThreshold) FaceTowards(_restFaceTarget);

            if (_restStillSince >= 0f && Time.time - _restStillSince >= restSettleTime)
                _wantLieDown = true;

            return true;
        }

        /// <summary>飲み終わったあと、**落ち着く場所へ少し移動する段**へ入る。
        ///
        /// 行き先は「水場から離れる向き」に <see cref="restMoveDistance"/> 進んだ点で、
        /// 向きは休憩の回数を種にした乱数で毎回変える (**厳密でなくてよい** — 2026-08-23 ユーザー)。
        /// 徘徊の中心へ少し寄せてあるので、庭の外や縁側の上へは出ていきにくい。</summary>
        void EnterRestMove()
        {
            _restPhase = RestPhaseMove;
            _restMoveStart = Time.time;
            _restStillSince = -1f;

            Vector3 from = WaterUsable() ? waterPlace.position : dogRoot.position; // sync: local-ok: 水場が無いときの原点のみ
            // 水場から離れる向き + 毎回変わる散らし。**徘徊の中心側へ寄せる**ので
            // 「水場のまわりをうろつく」ではなく「庭へ戻って落ち着く」に見える
            Vector3 inward = areaCenter != null ? areaCenter.position - from : Vector3.forward;
            inward.y = 0f;
            if (inward.sqrMagnitude < 0.0001f) inward = Vector3.forward;
            float spread = (Rand01(_restCount, RandStreamRestMoveAngle) - 0.5f) * RestMoveSpreadRad;
            float baseAng = Mathf.Atan2(inward.z, inward.x) + spread;
            _restSpot = from + new Vector3(Mathf.Cos(baseAng), 0f, Mathf.Sin(baseAng)) * restMoveDistance;
        }

        /// <summary>伏せの段へ入る (長さをここで決める)。</summary>
        void EnterLieDown()
        {
            _restPhase = RestPhaseLie;
            _restStillSince = -1f;
            _restUntil = Time.time + Mathf.Lerp(restDurationMin, restDurationMax,
                Rand01(_restCount, RandStreamRestLen));
            _restFaceTarget = PickRestFaceTarget();
        }

        /// <summary>座ったときにどちらを向くか。
        ///
        /// **「飲んだ場所」ではなく「面白い方」を向く** (2026-08-23 ユーザー)。
        /// 候補は他の犬 (遊んでいる犬を優先) とプレイヤーで、
        /// **厳密でなくてよい / 毎回変わってよい**ので休憩の回数を種にして選ぶ。
        /// 見つからなければ徘徊の中心 (庭の方) を向く — 縁側や壁を向いて座らない。</summary>
        Vector3 PickRestFaceTarget()
        {
            // 遊んでいる犬が居ればそちら。**庭で何かが起きている方を向く**のがいちばん生きて見える
            if (_packRoots != null && _packWander != null)
            {
                for (int i = 0; i < _packRoots.Length; i++)
                {
                    if (i == dogIndex || _packRoots[i] == null || _packWander[i] == null) continue;
                    if (_packWander[i].IsPlaying)
                        return _packRoots[i].position; // sync: local-ok: 視線の向き先のみ — 位置の目標には使わない
                }
            }

            int count = VRCPlayerApi.GetPlayerCount();
            if (count > _players.Length) count = _players.Length;
            VRCPlayerApi.GetPlayers(_players);
            // 候補の中から休憩の回数で選ぶ (毎回変わる)
            if (count > 0)
            {
                VRCPlayerApi p = _players[(int)(Rand01(_restCount, RandStreamRestFace) * count) % count];
                if (Utilities.IsValid(p)) return p.GetPosition();
            }

            // 誰も居なければ庭の方を向く (縁側や壁を向いて座らない)
            if (_packRoots != null)
                for (int i = 0; i < _packRoots.Length; i++)
                    if (i != dogIndex && _packRoots[i] != null)
                        return _packRoots[i].position; // sync: local-ok: 視線の向き先のみ

            return areaCenter != null ? areaCenter.position : dogRoot.position + dogRoot.forward;
        }

        /// <summary>休憩を終える。**出口を 1 つにする** — どの段階から抜けても
        /// クールダウンと「走った借金」の清算が必ず同じになる。</summary>
        void EndRest()
        {
            _restPhase = RestPhaseNone;
            _restUntil = -1f;
            _restReadyAt = Time.time + restCooldown;
            _breathRunAccum = 0f; // 休んだので「走った借金」は返した
        }

        /// <summary>水場のどこを見るか。**足元ではなく水面**。
        ///
        /// モデルのピボットは台の底にあるので、`waterPlace.position` をそのまま見つめると
        /// 犬が地面を睨む。おやつは「おやつそのもの」を見つめれば済んだが
        /// (皿の上に乗っている)、水場は器に高さがあるぶんの持ち上げが要る。</summary>
        Vector3 DrinkGaze()
        {
            return waterPlace.position + Vector3.up * drinkGazeRise;
        }

        /// <summary>水場へ寄れるか。無い / 遠すぎるなら false (その場で伏せる)。</summary>
        bool WaterUsable()
        {
            if (waterPlace == null) return false;
            Vector3 d = waterPlace.position - dogRoot.position; // sync: local-ok: 遠すぎるかの判定のみ — 目標そのものは水場の絶対座標
            d.y = 0f;
            return d.magnitude <= waterMaxDistance;
        }

        /// <summary>水場のどこに立って飲むか。
        ///
        /// **犬ごとに水場の別の側へ立つ。** 完了条件「複数の犬が同時に来ても破綻しない
        /// (場所の取り合いにならない)」への答えで、帰還の扇 (IMP-NET-004) と同じ考え方 —
        /// **同期値だけから決める**ので全クライアントで同じ絶対座標になる
        /// (水場はシーンに据えた固定物、`dogIndex` は名簿の静的な番号)。
        ///
        /// 体の半径を足すのは、大きい犬ほど外側に立たせるため
        /// (`KD_DogPack` の押し出しに任せると、飲んでいる最中に押し合う)。</summary>
        Vector3 DrinkSpot()
        {
            Vector3 c = waterPlace.position;
            // **正面は「庭の側」。** 器は実測 直径 0.30m・高さ 0.15m しかないので
            // 4 方向から囲むのは無理で、囲むと「並んで飲んでいる」ように見える
            // (2026-08-23 ユーザー)。帰還の扇 (IMP-NET-004) と同じ形にする —
            // **正面を基準に左右へ狭く開く**
            Vector3 front = areaCenter != null ? areaCenter.position - c : Vector3.forward;
            front.y = 0f;
            if (front.sqrMagnitude < 0.0001f) front = Vector3.forward;
            float baseAng = Mathf.Atan2(front.z, front.x);
            int slots = (_packRoots != null && _packRoots.Length > 0) ? _packRoots.Length : 4;
            // dogIndex を -1.5 / -0.5 / +0.5 / +1.5 のように中央対称へ写す
            float slot = dogIndex - (slots - 1) * 0.5f;
            float ang = baseAng + slot * DrinkFanStepRad;
            return c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (drinkStandoff + _selfRadius);
        }

    }
}
