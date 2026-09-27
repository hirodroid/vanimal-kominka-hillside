using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (Toy)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_TreatPouch
    {
        // ============================================================
        // おもちゃタブ (IMP-FET-011) — **待機中のボールを目の前へ呼ぶ**
        //
        // 旧 `KD_BallResetButton` を置き換える。あちらは `VRCObjectSync.Respawn()` で
        // **シーン開始位置**へ戻していたが、それだと「呼ぶ」の意味が「取りに行く」へ戻る。
        //
        // **「詰み」を解く 2 行 (`SetKinematic(false)` / `velocity = 0`) はそのまま持ってくる** —
        // 破壊的監査 RC-3 が名指しした「位置だけ戻しても詰みは解けない」。犬の Return 中に
        // 切断・Owner 交代が起きて kinematic のまま取り残されたボールは、ここが最後の砦になる。
        // ============================================================

        /// <summary>押せる。</summary>
        public const int ToyOk = 0;
        /// <summary>誰かが持っている。**手から奪わない。**</summary>
        public const int ToyHeld = 1;
        /// <summary>犬が追っている / くわえて帰っている (`dogState != Idle`)。</summary>
        public const int ToyDogBusy = 2;
        /// <summary>連打の抑制中。</summary>
        public const int ToyCooldown = 3;
        /// <summary>配線が足りない。**無言にしない。**</summary>
        public const int ToyUnwired = 4;
        /// <summary>その犬が誰かのそばで寝ている (IMP-DOG-019 Phase 5)。
        /// **`fetch.IsIdle` は真のままなので、これを足さないと
        /// 「呼んでも犬が来ないボタン」になる** (Issue §6 危険 4)。</summary>
        public const int ToySleeping = 5;

        /// <summary>置くときの浮かせ量 (m)。地面と重なった状態で置かない。</summary>
        const float RecallLift = 0.05f;

        // 裏山改変: [ボール] は KH_ToyRecall (坂の地面へ置く・ディスクと 1 本の要求) へ。古民家の Recall (KD_BallController 直叩き) は呼ばない (復帰条件なし)
        public void _Toy0() { HillsideToy(0, KominkaHillside.KH_ToyRecall.KindBall); }
        public void _Toy1() { HillsideToy(1, KominkaHillside.KH_ToyRecall.KindBall); }
        public void _Toy2() { HillsideToy(2, KominkaHillside.KH_ToyRecall.KindBall); }
        public void _Toy3() { HillsideToy(3, KominkaHillside.KH_ToyRecall.KindBall); }
        // IMP-DOG-020: 6 匹化 (柴 / サモエド)。行 = 犬 1 匹なので口も 1 匹 1 つ
        public void _Toy4() { HillsideToy(4, KominkaHillside.KH_ToyRecall.KindBall); }
        public void _Toy5() { HillsideToy(5, KominkaHillside.KH_ToyRecall.KindBall); }
        // 裏山改変: 行の 2 つ目のボタン [ディスク] (IMP-DOG-005。KH_ToyRecall.KindDisc)
        public void _Disc0() { HillsideToy(0, KominkaHillside.KH_ToyRecall.KindDisc); }
        public void _Disc1() { HillsideToy(1, KominkaHillside.KH_ToyRecall.KindDisc); }
        public void _Disc2() { HillsideToy(2, KominkaHillside.KH_ToyRecall.KindDisc); }
        public void _Disc3() { HillsideToy(3, KominkaHillside.KH_ToyRecall.KindDisc); }
        public void _Disc4() { HillsideToy(4, KominkaHillside.KH_ToyRecall.KindDisc); }
        public void _Disc5() { HillsideToy(5, KominkaHillside.KH_ToyRecall.KindDisc); }

        /// <summary>**「待機中」は導出する。新しい状態は 1 つも作らない。**
        ///
        /// <code>
        /// 待機中 ≡ !ball.IsHeldNow      (VRC_Pickup.IsHeld は同期される)
        ///        ∧ fetch.IsIdle        (dogState は [UdonSynced] で全員に届いている)
        ///        ∧ !sleeping           (IMP-DOG-019 Phase 5。枠の同期値から導出)
        /// </code>
        ///
        /// `IsIdle` で見るので、旧リセットが素通ししていた **ChaseStart / Chase / Pickup**
        /// まで自動的に除かれる — **犬の追跡を横から折る経路が構造的に消える。**
        ///
        /// ⚠ **`∧ !sleeping` は `IsIdle` では代用できない** (Issue §6 危険 4) —
        /// 寝ている犬の `dogState` は Idle のままなので、足さないと
        /// **押しても犬が来ないボタン**が出る。</summary>
        private int ToyGate(int row)
        {
            KD_BallController ball = BallAt(row);
            if (ball == null) return ToyUnwired;
            if (_ballSyncs == null || row >= _ballSyncs.Length || _ballSyncs[row] == null)
                return ToyUnwired;   // ObjectSync が無いと置いても他人へ伝わらない
            KD_DogFetchController fetch = FetchAt(row);
            if (fetch == null) return ToyUnwired;

            if (ball.IsHeldNow) return ToyHeld;
            if (fetch.activities != null && fetch.activities.RaceBall == row) return ToyDogBusy;
            // **「誰かが持っている」の次・「犬が遊んでいる」の前。**
            // ボールの事情 (手の中にある) は犬の事情より先に読ませ、犬の事情の中では
            // **具体的な相手が分かるほう**を先に出す (`犬が遊んでいる` は、
            // 寝ている犬では `IsIdle` が真なのでそもそも成立しない)
            if (sleepBoard != null && sleepBoard.IsSleeping(row)) return ToySleeping;
            if (!fetch.IsIdle) return ToyDogBusy;
            if (_recallAt != null && row < _recallAt.Length
                && Time.time - _recallAt[row] < recallCooldown) return ToyCooldown;
            return ToyOk;
        }

        /// <summary>**呼ぶ = 自分の目の前の地面へ置く** (2026-09-03 ユーザー決定)。</summary>
        private int _pendingRecallRow = -1;
        private int _requestToyRow = -1;
        private float _requestAt;
        private float _recallWrittenAt = -1f;
        private int _pendingSleepRow = -1;
        private float _sleepRequestAt;
        private void Recall(int row)
        {
            if (_pendingRecallRow >= 0) return;
            int gate = ToyGate(row);
            if (gate != ToyOk) { Feedback("ui.extra.043", false); _requestToyRow = row; Redraw(); return; }
            var ball = BallAt(row); var lp = Networking.LocalPlayer;
            if (ball == null || !Utilities.IsValid(lp)) return;
            _requestToyRow = row; _pendingRecallRow = row; _requestAt = Time.time; _recallWrittenAt = -1f;
            Feedback("ui.extra.039", true);
            if (!Networking.IsOwner(ball.gameObject))
                Networking.SetOwner(lp, ball.gameObject); // net: setowner-ok: ball only; position waits for ownership
            Redraw();
        }
        private void CompleteRecall(int row)
        {
            var ball = BallAt(row); var lp = Networking.LocalPlayer;
            if (ball == null || !Utilities.IsValid(lp) || !Networking.IsOwner(ball.gameObject)) return;
            var go = ball.gameObject;
            // **位置を書くより先に kinematic を解く。** 順序が逆だと、
            // 空中に固定されたまま目の前へ移動するだけになって拾えない
            VRCObjectSync sync = _ballSyncs[row];
            if (sync != null) sync.SetKinematic(false);

            // 向きは頭のヨーだけ。真下 / 真上を向いていると水平成分が消えるので身体で代替する
            VRCPlayerApi.TrackingData head =
                lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 fwd = head.rotation * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f)
            {
                fwd = lp.GetRotation() * Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            }
            fwd = fwd.normalized;

            // **高さは足元から積む。絶対ワールド Y で書かない** —
            // 床が原点にあるワールドでしか意味を持たない判定を書くと、
            // 例外もログも出ないまま黙って無効になる (Avatar Idle Arena `IMP-UI-009` 一般則②)。
            // **レイも撃たない** — 庭は平坦で足元基準で足りるし、撃つと
            // 「板やトリガーを床と読む」を自前で背負うことになる
            float radius = go.transform.localScale.x * 0.5f;   // 契約: localScale.x == 直径
            go.transform.position = lp.GetPosition()
                + fwd * (recallDistance * _scale)
                + Vector3.up * (radius + RecallLift);

            // 残存速度で置いた場所から転がり出さない (旧リセットと同じ処置)
            Rigidbody rb = (_ballBodies != null && row < _ballBodies.Length) ? _ballBodies[row] : null;
            if (rb != null && !rb.isKinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (_recallAt != null && row < _recallAt.Length) _recallAt[row] = Time.time;
            _recallWrittenAt = Time.time;
            Redraw();
        }

        /// <summary>ボールの部品を 1 回だけ引く。**毎フレーム GetComponent しない。**
        ///
        /// 参照の向きは「シーン側 → プレハブインスタンスの子」だけを使う
        /// (逆向きは実機ビルドで null に化ける — `vrc-udonsharp` §3)。</summary>
        private void PollSharedRequests()
        {
            // 裏山改変: さんぽの要求とおもちゃの結果もここで毎フレーム解決する (Redraw は板が閉じていると何もしないので、その外で回す)
            PollWalkPending();
            PollTravel();   // IMP-WLD-001: 地点ワープの結果とボタンの秒数 (KD_TreatPouch.Travel.cs)
            PollHillsideToy();
            if (_pendingRecallRow >= 0)
            {
                int row = _pendingRecallRow;
                var ball = BallAt(row);
                bool owner = ball != null && Networking.IsOwner(ball.gameObject);
                if (_recallWrittenAt < 0f && owner)
                {
                    int gate = ToyGate(row);
                    if (gate == ToyOk) CompleteRecall(row);
                    else { _pendingRecallRow = -1; Feedback("ui.extra.043", false); _feedbackTab = TabToy; }
                }
                else if (_recallWrittenAt >= 0f && (!owner || Time.time - _recallWrittenAt >= 1f))
                {
                    _pendingRecallRow = -1;
                    Feedback(owner ? "ui.extra.041" : "ui.extra.042", owner); _feedbackTab = TabToy;
                }
                else if (Time.time - _requestAt >= 4f)
                {
                    _pendingRecallRow = -1; Feedback("ui.extra.040", false); _feedbackTab = TabToy;
                }
            }
            if (_pendingSleepRow >= 0)
            {
                var slot = sleepBoard == null ? null : sleepBoard.SlotOf(_pendingSleepRow);
                if (slot != null) slot._KeepSelectionAlive();
                var lp = Networking.LocalPlayer;
                int who = slot == null ? -1 : slot.LiveSleeperId;
                bool success = Utilities.IsValid(lp) && who == lp.playerId;
                if (success || who >= 0 || Time.time - _sleepRequestAt >= 4f)
                {
                    _pendingSleepRow = -1;
                    Feedback(success ? "ui.extra.041" : who >= 0 ? "ui.extra.042" : "ui.extra.040", success);
                    _feedbackTab = TabSleep;
                }
            }
        }

        // ============================================================
        // 裏山改変: おもちゃタブ = KH_ToyRecall のボール / ディスク (KH_HandMenu の Toy / DrawToys を移した)
        //
        // 可否 (_GateToy) も結果 (_LastResult) も KH_ToyRecall が正本。板は読むだけで、行には [ボール][ディスク] の 2 つ。
        // 押せない行は隠さず暗くし、理由は行の 2 行目に出す (古民家のおもちゃタブと同じ作法)。
        // ============================================================

        /// <summary>結果を待っている行と種類 (-1 = 待っていない)。KH_ToyRecall の最後の結果がこの行・種類のときだけ表示する。</summary>
        private int _hsToyRow = -1;
        private int _hsToyKind;

        private void HillsideToy(int row, int kind)
        {
            if (recall == null) return;
            // 裏山改変 (IMP-DOG-016): 呼ばれて待機中の犬は おもちゃ (ボール / ディスク) の候補から外す (古民家の !sleeping)。
            // KH_ToyRecall の可否は Fetch の IsIdle を見るが、寝ている犬の dogState は Idle のままなので弾けない (Issue §6 危険 4)
            if (sleepBoard != null && sleepBoard.IsSleeping(row))
            {
                HillsideNote(TabToy, "KD_TreatPouch.WordSleepingSomeone", "");
                Redraw();
                return;
            }
            int gate = recall._GateToy(row, kind);
            if (gate != KominkaHillside.KH_ToyRecall.Ok)
            {
                if (gate == KominkaHillside.KH_ToyRecall.InWater) WaterAsk(row);   // IMP-DOG-020
                string key = gate == KominkaHillside.KH_ToyRecall.Unwired && kind == KominkaHillside.KH_ToyRecall.KindDisc
                    ? "KD_TreatPouch.WordToyGateNoDisc" : HillsideToyGateKey(gate);
                HillsideNote(TabToy, key, "");
                Redraw();
                return;
            }
            _hsToyRow = row; _hsToyKind = kind;
            recall._RecallToy(row, kind);
            HillsideNote(TabToy, "KD_TreatPouch.WordToyCalling", "");
            Redraw();
        }

        /// <summary>KH_ToyRecall の結果を 1 回だけ下端へ出す (0 置いた / 1 呼べなかった / 2 時間切れ)。
        /// 別の行・別の種類の結果は拾わない (連打・行の取り違えの床)。</summary>
        private void PollHillsideToy()
        {
            if (_hsToyRow < 0 || recall == null) return;
            int result = recall._LastResult();
            if (result < 0 || recall._LastRow() != _hsToyRow || recall._LastKind() != _hsToyKind) return;
            _hsToyRow = -1;
            HillsideNote(TabToy, result == 0 ? "KD_TreatPouch.WordToyPlaced" : result == 1 ? "KD_TreatPouch.WordToyFailed" : "KD_TreatPouch.WordToyTimeout", "");
            Redraw();
        }

        private string HillsideToyGateKey(int gate)
        {
            if (gate == KominkaHillside.KH_ToyRecall.InWater) return "course.water.busyToy";
            if (gate == KominkaHillside.KH_ToyRecall.Held) return "KD_TreatPouch.WordToyGateHeld";
            if (gate == KominkaHillside.KH_ToyRecall.DogBusy) return "KD_TreatPouch.WordToyGateBusy";
            if (gate == KominkaHillside.KH_ToyRecall.Cooldown) return "KD_TreatPouch.WordToyGateCooldown";
            if (gate == KominkaHillside.KH_ToyRecall.Pending) return "KD_TreatPouch.WordToyGatePending";
            return "KD_TreatPouch.WordToyGateUnavailable";
        }

        private string HillsideToyShort(int gate)
        {
            if (gate == KominkaHillside.KH_ToyRecall.Ok) return L("KD_TreatPouch.WordToyStateOk");
            if (gate == KominkaHillside.KH_ToyRecall.InWater) return L("course.water.state");
            if (gate == KominkaHillside.KH_ToyRecall.Held) return L("KD_TreatPouch.WordToyStateHeld");
            if (gate == KominkaHillside.KH_ToyRecall.DogBusy) return L("KD_TreatPouch.WordToyStateBusy");
            if (gate == KominkaHillside.KH_ToyRecall.Pending) return L("KD_TreatPouch.WordToyStatePending");
            if (gate == KominkaHillside.KH_ToyRecall.Cooldown) return L("KD_TreatPouch.WordToyStateCooldown");
            return L("KD_TreatPouch.WordToyStateNone");
        }

        /// <summary>おもちゃタブ (Redraw の TabToy 分岐から。上の「裏山改変:」の一部)。行 = 犬、[ボール] は toyButton*、[ディスク] は disc*。</summary>
        private void RedrawHillsideToy()
        {
            int n = toyLabels == null ? 0 : toyLabels.Length;
            for (int i = 0; i < n; i++)
            {
                int ballGate = recall == null ? KominkaHillside.KH_ToyRecall.Unwired : recall._GateToy(i, KominkaHillside.KH_ToyRecall.KindBall);
                int discGate = recall == null ? KominkaHillside.KH_ToyRecall.Unwired : recall._GateToy(i, KominkaHillside.KH_ToyRecall.KindDisc);
                bool ballOn = ballGate == KominkaHillside.KH_ToyRecall.Ok;
                bool discOn = discGate == KominkaHillside.KH_ToyRecall.Ok;
                // 裏山改変 (IMP-DOG-016): 呼ばれて待機中の犬は押せない (ボタンは暗く、理由は「〇〇さんのそばにいる」)
                bool sleeping = sleepBoard != null && sleepBoard.IsSleeping(i);
                if (sleeping) { ballOn = false; discOn = false; }

                if (toyLabels[i] != null) toyLabels[i].text = HillsideDogName(i);
                if (toyReasonLabels != null && i < toyReasonLabels.Length && toyReasonLabels[i] != null)
                    toyReasonLabels[i].text = sleeping ? SleeperText(i, ToyNameBudget)
                        : L("KD_TreatPouch.WordToyBall") + ": " + HillsideToyShort(ballGate) + " / " + L("KD_TreatPouch.WordToyDisc") + ": "
                        + (discGate == KominkaHillside.KH_ToyRecall.Unwired ? L("KD_TreatPouch.WordToyStateNotYet") : HillsideToyShort(discGate));

                // **ボタンは隠さない。** 押せないことは interactable と色で表す (古民家のおもちゃタブと同じ)
                GameObject ball = (toyButtons != null && i < toyButtons.Length) ? toyButtons[i] : null;
                if (ball != null && !ball.activeSelf) ball.SetActive(true);
                if (toyButtonComponents != null && i < toyButtonComponents.Length && toyButtonComponents[i] != null)
                    toyButtonComponents[i].interactable = ballOn;
                if (toyButtonImages != null && i < toyButtonImages.Length && toyButtonImages[i] != null)
                    toyButtonImages[i].color = ballOn ? recallOnColor : recallOffColor;

                GameObject disc = (discButtons != null && i < discButtons.Length) ? discButtons[i] : null;
                if (disc != null && !disc.activeSelf) disc.SetActive(true);
                if (discButtonComponents != null && i < discButtonComponents.Length && discButtonComponents[i] != null)
                    discButtonComponents[i].interactable = discOn;
                if (discButtonImages != null && i < discButtonImages.Length && discButtonImages[i] != null)
                    discButtonImages[i].color = discOn ? recallOnColor : recallOffColor;
                if (discButtonLabels != null && i < discButtonLabels.Length && discButtonLabels[i] != null)
                    discButtonLabels[i].text = L("KD_TreatPouch.WordToyDisc");
            }
            HillsideFooter(HillsideNoteOr(TabToy, L("KD_TreatPouch.WordToyHelp")));
        }

        private void CacheBalls()
        {
            int n = balls == null ? 0 : balls.Length;
            _ballSyncs = new VRCObjectSync[n];
            _ballBodies = new Rigidbody[n];
            _recallAt = new float[n];
            for (int i = 0; i < n; i++)
            {
                _recallAt[i] = -1000f;
                KD_BallController b = balls[i];
                if (b == null) continue;
                _ballSyncs[i] = b.GetComponent<VRCObjectSync>();
                _ballBodies[i] = b.GetComponent<Rigidbody>();
                // **欠けていても押した瞬間に無反応になるだけなので、起動時に名指しする**
                // (破壊的監査 RC-1 と同じ判断 — 「復帰手段が無いこと」に誰も気付けない)
                if (_ballSyncs[i] == null)
                    Debug.LogError("[KD_TreatPouch] " + b.gameObject.name
                        + " に VRCObjectSync が無い - 呼んでも他人の画面では動かない。");
            }
        }

        private KD_BallController BallAt(int i)
        {
            if (balls == null || i < 0 || i >= balls.Length) return null;
            return balls[i];
        }

        private KD_DogFetchController FetchAt(int i)
        {
            if (toyFetchers == null || i < 0 || i >= toyFetchers.Length) return null;
            return toyFetchers[i];
        }

        private string ToyNameAt(int i)
        {
            if (toyNameKeys == null || i < 0 || i >= toyNameKeys.Length) return "-";
            string s = toyNameKeys[i];
            return (s == null || s.Length == 0) ? "-" : L(s);
        }

        /// <summary>押せない理由の文言。**行ごとに変わる理由 (寝ている相手の名前) を
        /// 出すので row を受け取る。**</summary>
        private string ToyReasonText(int row, int gate)
        {
            if (gate == ToyHeld) return L("KD_TreatPouch.WordHeldByOther");
            // **枠は 180px @18pt しかない** ので名前は 5 字で切る (`ToyNameBudget`)。
            // 溢れると隣の列へ描き出す (Overflow なので黙って重なる — vrc-tmp-font §4)
            if (gate == ToySleeping) return SleeperText(row, ToyNameBudget);
            if (gate == ToyDogBusy) return L("KD_TreatPouch.WordDogBusy");
            if (gate == ToyUnwired) return L("KD_ShopShelf.WordLoading");
            return "";   // Ok と Cooldown は理由を出さない (押せる状態と地続きなので)
        }

    }
}
