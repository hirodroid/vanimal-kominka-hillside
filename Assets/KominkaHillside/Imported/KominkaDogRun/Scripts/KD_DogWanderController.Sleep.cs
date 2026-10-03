using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_DogWanderController の partial (Sleep)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_DogWanderController
    {
        // ================================================================ そばで寝る (IMP-DOG-019 Phase 2)

        /// <summary>
        /// **呼んだ人の横へ行って寝る。** 寝るモード中なら true (呼び出し元が早期 return する)。
        ///
        /// ## 入力は枠の同期値だけ — 犬側の同期変数は 1 本も増えない
        ///
        /// <code>
        /// sleepBoard.LiveSleeperOf(dogIndex)   誰が呼んでいるか (-1 = 誰も)
        /// sleepBoard.SlotOf(dogIndex).CalledMs いつ呼んだか (呼び直しの検出)
        /// </code>
        ///
        /// **`sleeperId` を直接読まない** — 退室した人の id が同期値に残るため。
        /// `LiveSleeperOf` は実在確認込みなので、**全クライアントが同じ結論**を出す
        /// (`treatBox.LiveSlotOf(dogIndex)` とまったく同じ形)。
        ///
        /// ## 3 段: 並ぶ → 向く → 寝る
        ///
        /// 1. **並ぶ** — <see cref="SleepSpot"/> (Fetch の `ReturnSpot` の移植) が返す
        ///    扇のスロットへ歩く。**目標は同期値と静的な体格だけから導く**ので、
        ///    全クライアントで同じ絶対座標になる
        /// 2. **向く** — 人と平行 (DES-DOG-003 §3.2 の変更点 2)。±どちら向きかは
        ///    <see cref="SleepFaceDir"/> が**腹と顔が人を向く側**に決める (実測根拠はそちら)。
        ///    **向き切ってから**寝姿勢へ入る (BUG-DOG-001 — 静止ポーズ中に回すと足が擦る)
        /// 3. **寝る** — 到着したら<b>凍結</b>し、人が動いてもスロットを取り直さない
        ///    (Return の `insideStop` と同型。§3.2 の変更点 3)
        ///
        /// ## 諦める床はここには無い (正本を 2 つにしない)
        ///
        /// 「一定時間そばへ着けなかったら Idle へ戻す」は**枠側 (Phase 3b) が持つ** —
        /// 呼んだ人のクライアントが `arriveTimeout` 内に着かなければ枠を解放する。
        /// 犬はただ `LiveSleeperOf` に従うだけで、解放されれば次のフレームから Idle へ戻る。
        ///
        /// ## Owner も非 Owner も同じ式で判定する (IMP-NET-005)
        ///
        /// ルート Transform を書くのは `StepToward` / `FaceTowards` / `Coast` の中の
        /// Owner ゲートの下だけ。**判断はここも含めて全員が回す** — 到着 (凍結) の判定は
        /// 「同期された実位置」と「観測した実速度」から作ってあるので、
        /// 非 Owner も Owner と同じタイミングで同じ結論に達する。
        /// </summary>
        bool UpdateSleep()
        {
            if (sleepBoard == null || dogIndex < 0) return false;

            // **占有者は毎フレーム引き直す** (おやつの LiveSlotOf と同じ)。
            // 枠が解放された / 呼んだ人が退室した瞬間に -1 になり、そこが凍結の出口
            int sleeper = sleepBoard.LiveSleeperOf(dogIndex);
            if (sleeper < 0)
            {
                if (_placedSleepActive && _dogOwner)
                    dogRoot.position = GroundAfterPlacedSleep(dogRoot.position); // net: owner-only: _dogOwner、指定寝床解除後は接地へ戻す
                _placedSleepActive = false;
                _sleepFrozen = false;   // **凍結を解くのは枠が空いたときだけ** (+ 下の呼び直し)
                _sleeperId = -1;
                _sleepCalledMs = 0;
                return false;
            }

            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(sleeper);
            if (!Utilities.IsValid(p))
            {
                // LiveSleeperOf が実在確認込みなので基本ここへは来ない。伝播の谷間の保険
                _sleepFrozen = false;
                return false;
            }

            KD_SleepSlot slot = sleepBoard.SlotOf(dogIndex);
            int calledMs = slot != null ? slot.CalledMs : 0;

            // **呼び直し / 占有者の交代で凍結を解く。**
            // 人が別の部屋へ移ってからもう一度呼ぶ、が「もう一度そばへ来て」の意味になる —
            // 解かないと**押しても犬が動かないボタン**になる (危険 4 と同じ形)。
            // 判定は同期値 (playerId / calledMs) なので全クライアントで同時に起きる
            if (sleeper != _sleeperId || calledMs != _sleepCalledMs)
            {
                if (_placedSleepActive && _dogOwner)
                    dogRoot.position = GroundAfterPlacedSleep(dogRoot.position); // net: owner-only: _dogOwner、呼び直しの水平接近は接地から始める
                _sleeperId = sleeper;
                _sleepCalledMs = calledMs;
                _sleepFrozen = false;
            }

            _sleepCalled = true;

            // **休憩 FSM は畳む** (Fetch 割り込みと同じ処置)。畳まないと寝ている間じゅう
            // 休憩が凍結され、起きた直後に古いタイマーのまま再開して即伏せる。
            // EndRest 経由なのでクールダウンと借金の清算はどの出口とも同じ
            if (_restPhase != RestPhaseNone) EndRest();

            Vector3 playerPos = p.GetPosition();

            // ---- 凍結前: 扇のスロットへ歩く ----
            if (slot != null && slot.HasSpot)
            {
                _placedSleepActive = true;
                return UpdatePlacedSleep(slot);
            }
            if (_placedSleepActive && _dogOwner)
                dogRoot.position = GroundAfterPlacedSleep(dogRoot.position); // net: owner-only: _dogOwner、通常の呼び出しへ戻す
            _placedSleepActive = false;
            if (!_sleepFrozen)
            {
                Vector3 spot = SleepSpot(p, playerPos);
                Vector3 dogPos = dogRoot.position;
                Vector3 to = spot - dogPos;
                to.y = 0f;
                float dist = to.magnitude;

                // **到着 = スロットの近くで足が止まった。** ここから先はスロットを取り直さない。
                // 非 Owner も同じ式で判定できる (実位置は VRCObjectSync が配り、
                // _speed は ObserveSpeed がその実位置の差分で埋めている)
                if (dist <= arriveRadius && _speed <= stopSpeedThreshold)
                {
                    _sleepFrozen = true;
                }
                else if (SleepSettled(calledMs))
                {
                    // 遅れて入ってきた人の合流路 (SleepSettleGraceMs の注記)。
                    // **同期値だけで書いてある**ので、この経路も全クライアントで一致する
                    _sleepFrozen = true;
                }
                else
                {
                    // 添い寝へ向かう間は Fetch と同じく壁を抜けて目標へ進む (BUG-DOG-004)。
                    // WallClearance で手前へ寄せると、寝室の壁や縁側で止まってしまう。
                    // 接地・加減速・Owner だけの位置更新は既存の StepToward に任せる。
                    SleepStep(spot, dist > runDistance ? runSpeed : walkSpeed);   // 裏山改変: 散歩と同じ地形追従 (SleepStep)

                    // **呼ばれて来ていることが読めるように、呼んだ人を見ながら寄る**
                    // (おやつが「おやつそのものを見つめる」でやっているのと同じ。設計原則 4)。
                    // **寝てからは差し込まない** — そこは Phase 4 (寝たままの撫で) の領分
                    if (petController != null)
                    {
                        VRCPlayerApi.TrackingData head = p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                        petController._SetGaze(head.position, 0f);
                        petController._SetUrge(0.6f);
                    }
                    return true;
                }
            }

            // ---- 凍結後: その場で人の方を向き直り、寝る ----
            //
            // **人が歩き回ってもここから動かない。** 追い続けると「ずるずる付いてくる犬」に
            // なり、寝るどころではなくなる (DES-DOG-003 §3.2 の変更点 3)
            Coast();

            // **向き切ってから寝姿勢へ入る。** 寝てから回すと BUG-DOG-001 の
            // 「静止ポーズのまま足が地面を擦る」が出る (水を飲む前の向き合わせと同じ作り)
            //
            // **見るのは `_wasSleep` (Animator へ実際に書いた値)。** 一度寝たら向きは
            // 二度と測り直さない — 測り直すと、他の犬が呼ばれて扇が組み替わったときに
            // **寝ている犬が 180° 回り出す** (UpdateRest が `_wasLieDown` を見ているのと同じ理由)
            bool facing = true;
            if (!_wasSleep && _speed <= stopSpeedThreshold)
            {
                Vector3 face = SleepFaceDir(p);
                if (face.sqrMagnitude > 0.0001f)
                {
                    facing = Vector3.Angle(dogRoot.forward, face) <= SleepFaceToleranceDeg;
                    if (!facing) FaceTowards(dogRoot.position + face);
                }
            }

            // **到着していないうちは Sleep を立てない** (Phase 1 の申し送り) —
            // 移動中に立てると「歩いたまま寝る」絵になる。Animator へ書くのは LateUpdate の 1 箇所
            if (_speed <= stopSpeedThreshold && facing) _wantSleep = true;
            return true;
        }

        /// <summary>指定寝床: 水平接近後、寝姿勢で同期された位置・向きへ収まる。</summary>
        bool UpdatePlacedSleep(KD_SleepSlot slot)
        {
            Vector3 target = slot.Spot;
            Vector3 delta = target - dogRoot.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (!_sleepFrozen && distance > Mathf.Max(0.02f, placedSleepLiftRadius))
            {
                SleepStep(target, distance > runDistance ? runSpeed : walkSpeed);   // 裏山改変: 散歩と同じ地形追従 (SleepStep)
                return true;
            }
            _sleepFrozen = true;
            _speed = 0f; _moveDir = Vector3.zero; _stopHoldUntil = 0f;
            ApplyMoveAnim();
            Vector3 face = Quaternion.Euler(0f, slot.SpotYaw, 0f) * Vector3.forward;
            bool facing = Vector3.Angle(dogRoot.forward, face) <= SleepFaceToleranceDeg;
            // IMP-DOG-036 S1/S2 fix 3: the settle below writes the root itself (no recorded mover), so until it has arrived the dog
            // decides every frame (TickBusy) and the root moves every frame as before; arrived = only the final yaw is rewritten
            if (!facing) { _placedSettling = true; FaceTowards(dogRoot.position + face); return true; }
            _wantSleep = true;
            _placedSettling = false;
            if (_dogOwner)
            {
                dogRoot.rotation = Quaternion.Euler(0f, slot.SpotYaw, 0f); // net: owner-only: _dogOwner、指定向きへ最後の許容角を合わせる
                // IMP-DOG-032: 古民家どおり、置いた高さへ浮く (接地スナップしない)。IMP-DOG-016 変更 3 の SnapToGround は外した
                // (手の高さに置ける = 膝・胸の上、斜面の上でも置いた点へ収まる)。近づくまでの地形追従 (SleepStep) は残す
                Vector3 lifted = Vector3.MoveTowards(dogRoot.position, target, Mathf.Max(.05f, placedSleepLiftSpeed) * Dt());
                dogRoot.position = lifted; // net: owner-only: _dogOwner、上昇中は接地スナップしない
                // fix 3: still settling while the root has not reached the slot (MoveTowards returns the target exactly once within one
                // step, so an arrived dog reads 0 here). IMP-DOG-032: measured in 3D - the lift may be vertical only (a dog already
                // under / over the spot), which a horizontal check would call arrived. Busy until then (TickBusy)
                _placedSettling = (target - lifted).sqrMagnitude > 0.000001f;
            }
            return true;
        }

        private bool _placedSettling;   // IMP-DOG-036 S1/S2 fix 3: the placed-sleep settle is still moving the root (TickBusy)

        /// <summary>裏山改変: IMP-DOG-032。場所を決めて呼ばれ、置いた点へ収まる途中 / 収まって寝ている (近づく歩きは含まない)。
        /// KD_DogPack の押し出しはこの犬を押さない (押し出しは接地スナップするので、膝・胸の上や斜面の上に置いた犬が地面へ落ちる)。
        /// 洗い場の台の StationLocked と同じ扱い: この犬自身の押し出しは捨て、相手の取り分は変えない。
        /// 書き手は Owner だけ (KD_DogPack も Owner だけが書く) なので、Owner の局所値で足りる。
        /// 解除の縁 (LiveSleeperOf &lt; 0 / 呼び直し / 場所なしへ) では UpdateSleep が GroundAfterPlacedSleep で接地へ戻してから下ろす。</summary>
        public bool PlacedSleepHeld { get { return _placedSleepActive && _sleepFrozen; } } // net: callable-ok: read-only

        /// <summary>裏山改変: IMP-DOG-016 変更 3。呼ばれた犬の足。**散歩の集合と同じ地形追従**
        /// (KD_DogWanderController.HillsideLeash の GatherStep = NavMesh の完全な経路を角ごとに StepToward + 接地スナップ)。
        /// 完全な経路が無いときは古民家と同じ直進 (StepToward、壁を抜けて目標へ = BUG-DOG-004 / DES-DOG-001 の「到達不能時は直進」)。
        /// 裏山でない (hillsideLeashes 未配線) ときは古民家どおり StepToward だけ。非 Owner は GatherStep / StepToward の中で脚だけ選ぶ。</summary>
        void SleepStep(Vector3 target, float speed)
        {
            if (hillsideLeashes != null && GatherStep(target, speed)) return;
            StepToward(target, 0f, speed);
        }

        /// <summary>従来の扇呼びのみ、遅れて入った人の停止判定を時刻で補う。</summary>
        bool SleepSettled(int calledMs)
        {
            if (calledMs == 0) return false;               // まだ一度も呼ばれていない枠
            if (_speed > stopSpeedThreshold) return false; // 歩いている = まだ着いていない
            return (Networking.GetServerTimeInMilliseconds() - calledMs) >= SleepSettleGraceMs;
        }

        /// <summary>**寝るときに向く向き** (world。水平) = **自分のスロットから呼んだ人へ向かう向き**。
        ///
        /// 着いたときの向き (人を見ている) のまま横になる。**人と平行には寝かせない。**
        ///
        /// ## 経緯 (2026-09-04 Play)
        ///
        /// 初版は DES-DOG-003 §3.2 の「人と平行」を採り、寝姿勢が左右非対称 (右脇腹が下・
        /// 腹と顔が犬の左) なので**扇の左右で ± を切り替えて腹を人へ向ける**作りだった。
        /// Play で 4 匹とも背中を向け、符号を反転したら**ダックスだけ**逆のまま
        /// (犬種ごとにリグの X の向きが揃っていない — family D)。
        /// ユーザー判断: 「顔をユーザーの方に向けたまま寝れば、回転だけしなければ大丈夫」。
        ///
        /// → **人を向いて着地したその向きで寝る。** 左右の判定そのものが消えるので、
        /// リグの X の符号に依存しない。顔が人側に来るのはどの犬種でも同じ。
        ///
        /// 入力はスロットの方位 (<see cref="SleepSpot"/> と同じ導出 = 同期値と静的な体格) だけなので
        /// 全クライアントで同じ向きになる。人の実位置へ `FaceTowards` すると人が動くたびに回るので、
        /// **スロットの方位の逆**で固定する (凍結の規律)。フルトラで水平成分が消えたときの
        /// world forward へのフォールバックも `SleepSpot` と同じ (Issue §6 危険 3)。</summary>
        Vector3 SleepFaceDir(VRCPlayerApi p)
        {
            Vector3 fwd = p.GetRotation() * Vector3.forward;
            fwd.y = 0f;
            float fl = fwd.magnitude;
            Vector3 dir = fl > 0.01f ? fwd / fl : Vector3.forward;
            float bearing = Mathf.Atan2(dir.z, dir.x) + SleepFanOffset();
            // スロット → 人 = スロットの方位の逆
            return -new Vector3(Mathf.Cos(bearing), 0f, Mathf.Sin(bearing));
        }

        /// <summary>
        /// **扇の中での自分のオフセット (rad)。** 正 = 人の左側、負 = 人の右側。
        ///
        /// <see cref="SleepSpot"/> (並ぶ位置) と <see cref="SleepFaceDir"/> (向き) の
        /// **両方がここを見る** — 別々に導くと、位置と向きが別の根拠で回る。
        ///
        /// 集合は「今この人に呼ばれている犬」(`LiveSleeperOf` が同じ人) を `dogIndex` 順。
        /// 必要角は隣り合う 2 匹の体格から `2·asin((r隣 + r自) × sleepSpread / 2R)`
        /// (`KD_DogFetchController.ReturnSpot` の移植)。
        /// **入力は同期値と静的な体格だけ** — ローカル値を 1 つも混ぜない
        /// (`scripts/check-sync-anchors.js` の SHARED_FUNCS が本体を機械検査する)。
        /// </summary>
        float SleepFanOffset()
        {
            if (sleepBoard == null || _packRadius == null
                || dogIndex < 0 || dogIndex >= _packRadius.Length) return 0f;

            int sleeper = sleepBoard.LiveSleeperOf(dogIndex);
            if (sleeper < 0) return 0f;

            float twoR = 2f * Mathf.Max(sleepStopDistance, 0.05f);
            float total = 0f; // 扇の全幅 (rad)
            float mine = 0f;  // 扇の端から自分のスロットまで (rad)
            int prev = -1;    // 直前に詰めた犬 (必要角は隣り合う 2 匹の体格から)
            int n = _packRadius.Length;
            for (int j = 0; j < n; j++)
            {
                // **activeInHierarchy は見ない** (ReturnSpot と同じ理由 — ローカル状態は
                // クライアント間で割れうる)。LiveSleeperOf は同期値 + 実在確認なので割れない
                KD_SleepSlot otherSlot = sleepBoard.SlotOf(j);
                bool joining = j == dogIndex || (sleepBoard.LiveSleeperOf(j) == sleeper
                    && (otherSlot == null || !otherSlot.HasSpot));
                if (!joining) continue;
                if (prev >= 0)
                    total += 2f * Mathf.Asin(Mathf.Min(1f,
                        (_packRadius[prev] + _packRadius[j]) * sleepSpread / twoR));
                if (j == dogIndex) mine = total;
                prev = j;
            }
            return mine - total * 0.5f; // 扇全体を forward の左右対称に置く
        }

        /// <summary>
        /// **寝床のスロット。** <see cref="KD_DogFetchController.ReturnSpot"/> の移植で、
        /// 「プレイヤーを中心とする円周上の扇スロット」— 「点」ではなく「扇」。
        ///
        /// 帰還の扇から変えたのは 2 点だけ (DES-DOG-003 §3.2):
        ///
        ///   - **集合の作り方**: 「今この人へ帰っている犬」ではなく
        ///     **「今この人に呼ばれて寝る犬」** (`LiveSleeperOf(j)` が同じ人)。
        ///     どちらも同期値由来なので、全クライアントが同じ集合を作る
        ///   - **半径**: `sleepStopDistance + 体の半径`。大きい犬ほど外側に立つ
        ///     (水場の `DrinkSpot` ・催促の `nudgeStandoff + radius` と同じ作り)。
        ///     **実測 (Phase 1) で四肢が腹側 (= 人の側) へ投げ出される**と分かっており、
        ///     その広がりはポメ 約 0.11m ・ラブはその倍程度 = **体格に比例する**ので、
        ///     余白も体格に比例させる。扇の角度は共通の `sleepStopDistance` で計算するため、
        ///     外側へ出た犬のあいだの隙間はむしろ広がる = 重ならない
        ///
        /// **入力は同期値 (占有者) と静的な体格 (`_packRadius`) と
        /// プレイヤーの forward だけ** — ローカル値を 1 つも混ぜない
        /// (`scripts/check-sync-anchors.js` の SHARED_FUNCS が本体を機械検査する)。
        /// </summary>
        Vector3 SleepSpot(VRCPlayerApi p, Vector3 playerPos)
        {
            // 方位の基準 = プレイヤーの forward (水平)。ReturnSpot と同じ取り方・同じ保険
            Vector3 fwd = p.GetRotation() * Vector3.forward;
            fwd.y = 0f;
            float fl = fwd.magnitude;
            Vector3 dir = fl > 0.01f ? fwd / fl : Vector3.forward;
            // 扇の中の自分の位置。**向き (SleepFaceDir) と同じ導出を使う**
            float bearing = Mathf.Atan2(dir.z, dir.x) + SleepFanOffset();

            return playerPos + new Vector3(Mathf.Cos(bearing), 0f, Mathf.Sin(bearing)) * sleepStopDistance;
        }

    }
}
