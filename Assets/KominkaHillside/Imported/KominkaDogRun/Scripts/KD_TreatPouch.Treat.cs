using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>KD_TreatPouch の partial (Treat)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_TreatPouch
    {
        // ============================================================
        // 状態機械 — **遷移を決めるのは KD_TreatBox。ここは実行するだけ**
        // ============================================================

        private void Fire(int ev, int evSeq)
        {
            if (box == null) return;

            // **再入を潰す。** U# は既定で再帰できないので、副作用から Fire を
            // 呼び直すとローカル変数が壊れる。積んで下のループで回す
            if (_firing) { _queuedEv = ev; _queuedSeq = evSeq; return; }

            _firing = true;
            int e = ev;
            int s = evSeq;
            for (int guard = 0; guard < MaxChainedEvents; guard++)
            {
                int prev = _state;
                int packed = box.StepDispenseWith(_state, e, s, _curSeq);
                int next = box.DispenseNextOf(packed);
                bool consume = box.DispenseConsumesOf(packed);
                bool moved = next != _state;
                _state = next;

                if (moved)
                {
                    NoteReason(prev, e);
                    // **状態へ入る処理を先に済ませる。それから在庫を減らす。**
                    //
                    // 初版は `Consume()` → `EnterState()` の順だった。それだと
                    // **消費のあとに `_CommitOut()` という失敗しうる操作が残り**、
                    // 落ちると「在庫は 1 減っているのに枠は Out にならず犬が来られない」
                    // = 設計が名指しで塞いだ**「減ったのに出ていない」= プレイヤーの損**
                    // になる (2026-08-24 Codex 指摘。**コメントがこのバグを隠していた**)。
                    //
                    // > **在庫が減るのは、失敗しうる操作がすべて終わった後。**
                    EnterState(next);
                    // ★ **在庫を減らすのはここだけ。** 「減る辺が 1 本」だけでは
                    // 実行回数を保証できないので、**状態が実際に動いた呼び出し**に限る。
                    // **`EnterState` が失敗を積んでいたら減らさない** — 積まれている =
                    // 実体を出せていない、なので消費してはいけない
                    if (consume && _queuedEv < 0) Consume();
                }

                if (_queuedEv < 0) break;
                e = _queuedEv;
                s = _queuedSeq;
                _queuedEv = -1;
            }
            _queuedEv = -1;
            _firing = false;
            Redraw();
        }

        /// <summary>副作用から次のイベントを投げる (<see cref="Fire"/> の中でだけ呼ぶ)。</summary>
        private void Queue(int ev)
        {
            _queuedEv = ev;
            _queuedSeq = _curSeq;
        }

        /// <summary>**非同期の失敗も理由コードを持つ。** 押す前の判定 (Gate*) とは
        /// 射程が違う (往復のあとにしか分からない) が、**出し先は同じ 1 行**にそろえる。
        ///
        /// `EvTimeout` は文脈で意味が変わるので、**遷移前の状態**で読み分ける —
        /// 枠を待っている最中なら失敗、出している最中なら [しまう] の操作。</summary>
        private void NoteReason(int prevState, int ev)
        {
            if (ev == KD_TreatBox.EvOwnerFailed) { _reason = KD_TreatBox.GateOwnerFailed; return; }
            if (ev == KD_TreatBox.EvConsumeFailed) { _reason = KD_TreatBox.GateCommitFailed; return; }
            if (ev == KD_TreatBox.EvTimeout)
            {
                // Out からの Timeout = [しまう]。**失敗ではないので理由を残さない**
                if (prevState == KD_TreatBox.DispOut) _reason = KD_TreatBox.GateOk;
                else _reason = KD_TreatBox.GateOwnerFailed;
                return;
            }
            if (ev == KD_TreatBox.EvConsumeOk) _reason = KD_TreatBox.GateOk;
        }

        private void EnterState(int next)
        {
            _stateSince = Time.time;

            if (next == KD_TreatBox.DispIdle)
            {
                _slotIndex = -1;
                _slotGen = 0;
                return;
            }

            if (next == KD_TreatBox.DispOut)
            {
                // 出たおやつがUIのUSEを遮るため、成功時は配布操作へ戻す (BUG-TRT-001)。
                SetOpen(false);
                return;
            }

            if (next == KD_TreatBox.DispReserving)
            {
                _slotIndex = box.FreeSlotIndex();
                KD_TreatSlot slot = SlotAt(_slotIndex);
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (slot == null || !Utilities.IsValid(lp))
                {
                    Queue(KD_TreatBox.EvOwnerFailed);
                    return;
                }
                if (!Networking.IsOwner(slot.gameObject))
                    Networking.SetOwner(lp, slot.gameObject);          // net: setowner-ok: おやつの枠。犬ではないので Owner 固定の対象外
                if (slot.body != null && !Networking.IsOwner(slot.body.gameObject))
                    Networking.SetOwner(lp, slot.body.gameObject);     // net: setowner-ok: おやつの実体。同上
                // **移譲を待ってから書く。** 既に自分のものなら待つものが無い
                if (OwnsBoth(_slotIndex)) Queue(KD_TreatBox.EvOwnerGranted);
                return;
            }

            if (next == KD_TreatBox.DispReserved)
            {
                KD_TreatSlot slot = SlotAt(_slotIndex);
                if (slot == null)
                {
                    Queue(KD_TreatBox.EvOwnerFailed);
                    return;
                }
                if (!slot._TryReserve(_wantProduct))
                {
                    // **ReserveSettle 待ちの false は「負け」ではない** (破壊的監査 v2 RC-2)。
                    // 移譲成立の通知は同一フレームで届くため、非マスターの初回は
                    // ここで必ず settle 未経過になる。畳まずに少し待って再試行する
                    // (Reserved に留まる。固まっても Poll の stepTimeout が床)。
                    // settle 以外の false (枠を取られた) は従来どおり在庫を減らさず畳む
                    if (slot.ReserveSettling())
                    {
                        _reserveRetrySeq = _curSeq;
                        SendCustomEventDelayedSeconds(nameof(_RetryReserve), ReserveRetryDelay);
                        return;
                    }
                    Queue(KD_TreatBox.EvOwnerFailed);
                    return;
                }
                // 実体を手のひらへ吸着**するよう要求する**。**まだ在庫は減っていない**
                if (slot.body != null) slot.body._AttachToHand();
                // **ここで確認できるのは「要求を出した」ことまで** (2026-08-24 Codex 指摘で
                // 断定を弱めた)。吸着が成った / 見た目が付いた / body の Owner を保てている
                // までは見ていない — 次フレームの `_OnPlaced` は
                // **「吸着開始要求の次のフレーム」**であって「出たことの確認」ではない。
                //
                // **それでも在庫より先にここを通す意味はある** — 枠を Out にする
                // (`_CommitOut`) までを在庫より前に済ませられるので、
                // 「減ったのに犬が来られない」は塞げる
                _placedSeq = _curSeq;
                SendCustomEventDelayedFrames(nameof(_OnPlaced), 1);
                return;
            }

            if (next == KD_TreatBox.DispCommitting)
            {
                // **枠を Out にするのは在庫を減らす前。** ここが失敗しうる最後の操作で、
                // 落ちても**在庫は 1 も減らない** (`Fire` が失敗を見て消費を飛ばす)。
                //
                // 副作用として、消費に失敗した場合は「実体が一瞬出ているのに減っていない」
                // が起こりうる。**それがこの局面の緩い側** —
                // `shop-foundation §2`「緩い側の向きは局面ごとに違う」のとおり、
                // 買い物では「払わずに手に入る」が緩い側だが、
                // **消費では「出たけど減っていない」が緩い側**で、
                // 「減ったのに出ていない」がプレイヤーの損。**向きを間違えない。**
                KD_TreatSlot slot = SlotAt(_slotIndex);
                if (slot == null || !slot._CommitOut())
                {
                    // 予約した枠の Owner は自分なので本来起こらないが、
                    // 移譲を失っていれば起こる。**在庫を減らさずに畳む**
                    Debug.LogError("[KD_TreatPouch] _CommitOut に失敗 - 枠を返す (在庫は減らない)。slot "
                        + _slotIndex.ToString());
                    Queue(KD_TreatBox.EvConsumeFailed);
                    return;
                }
                _slotGen = slot.OutMs();
                return;
            }

            if (next == KD_TreatBox.DispRecalling) ReleaseSlot();
        }

        /// <summary>**在庫を 1 個減らす唯一の場所。**
        /// `scripts/check-consume-once.js` が「呼び出しは 1 箇所」を機械検査する。</summary>
        private void Consume()
        {
            bool ok = ledger != null && ledger._TryConsume(_wantProduct);
            Queue(ok ? KD_TreatBox.EvConsumeOk : KD_TreatBox.EvConsumeFailed);
        }

        /// <summary>**回収の口は本番と同じ 1 本** (<see cref="KD_TreatSlot._Release"/>)。
        /// テスト専用の経路を作らない — 作ると Step 6 で本番経路が初めて動くことになり、
        /// ここでの確認が証拠にならない。</summary>
        private void ReleaseSlot()
        {
            KD_TreatSlot slot = SlotAt(_slotIndex);
            if (slot == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp) && !Networking.IsOwner(slot.gameObject))
                Networking.SetOwner(lp, slot.gameObject);   // net: setowner-ok: おやつの枠。回収は Owner だけができる
            slot._Release();
        }

        // ============================================================
        // 問い合わせ
        // ============================================================

        /// <summary>出せるか / 出せない理由 (<c>KD_TreatBox.Gate*</c>)。
        ///
        /// **`Owned()` を混ぜない** — 在庫つき消耗品の `Owned()` は
        /// 「一度でも入手したか」なので、可否に使うと在庫 0 でも出せてしまう。</summary>
        private int GateFor(KD_ShopItem it)
        {
            if (box == null) return KD_TreatBox.GateLoading;
            bool loaded = ledger != null && ledger.Loaded();
            int stock = it == null ? 0 : it.Stock();
            return WithDogsBusy(box.DispenseGateWith(loaded, stock, IAlreadyOut(), box.FreeSlotCount()));
        }

        /// <summary>純関数の門番に「犬が全員 催しに拘束されている」を足す (破壊的監査 v4 FUNC-07/BUG-01)。
        /// 他人の都合なので自分側の理由 (在庫・1 人 1 個) より後 = <c>GateOk</c> のときだけ置き換える。
        /// ローカルの予測なので、ライブ中の「全員観客」のような決定的な場合を止めるのが役目 (<see cref="KD_TreatBox.AllDogsEngaged"/>)。</summary>
        private int WithDogsBusy(int gate)
        {
            if (gate == KD_TreatBox.GateOk && box != null && box.AllDogsEngaged()) return KD_TreatBox.GateDogsBusy;
            return gate;
        }

        /// <summary>品目に依らない理由だけを見る門番。
        /// **在庫は行が持つ**ので、ここでは在庫を 1 として渡して外す
        /// (別の条件式を書き起こさないための呼び方であって、別の判定ではない)。</summary>
        private int GlobalGate()
        {
            if (box == null) return KD_TreatBox.GateLoading;
            bool loaded = ledger != null && ledger.Loaded();
            return WithDogsBusy(box.DispenseGateWith(loaded, 1, IAlreadyOut(), box.FreeSlotCount()));
        }

        /// <summary>自分がもう 1 個出しているか (**1 人 1 個**)。
        /// 手続きの途中も「出している」側に数える — 二重に走らせない。</summary>
        private bool IAlreadyOut()
        {
            if (_state != KD_TreatBox.DispIdle) return true;
            if (box == null) return false;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return false;
            return box.PlayerHasSlot(lp.playerId);
        }

        /// <summary>掴んでいる枠がまだ自分のものか (**世代まで見る**)。</summary>
        private bool MineNow()
        {
            KD_TreatSlot slot = SlotAt(_slotIndex);
            if (slot == null) return false;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return false;
            if (slot.GiverId() != lp.playerId) return false;
            // 枠が再利用されたら別のおやつ。掴み続けない
            if (_state == KD_TreatBox.DispOut && _slotGen != 0 && slot.OutMs() != _slotGen)
                return false;
            return true;
        }

        /// <summary>枠と実体の**両方**を持っているか。片方だけでは書けない。</summary>
        private bool OwnsBoth(int slotIdx)
        {
            KD_TreatSlot slot = SlotAt(slotIdx);
            if (slot == null) return false;
            if (!Networking.IsOwner(slot.gameObject)) return false;
            if (slot.body == null) return true;
            return Networking.IsOwner(slot.body.gameObject);
        }

        private KD_TreatSlot SlotAt(int i)
        {
            if (box == null || box.slots == null) return null;
            if (i < 0 || i >= box.slots.Length) return null;
            return box.slots[i];
        }

        private KD_ShopItem ItemAt(int i)
        {
            if (items == null || i < 0 || i >= items.Length) return null;
            return items[i];
        }

    }
}
