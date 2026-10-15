using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    /// <summary>KD_DogFetchController の partial (PetSync)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_DogFetchController
    {
        // ---- IMP-NET-001: 撫での同期 ----

        /// <summary>ローカルの撫で判定が変化した (KD_DogPetController がエッジでだけ呼ぶ)。
        /// **ここでは送らない** — 送信の判断は `ReconcilePetSync` に一本化する。</summary>
        public void _SetLocalPetState(bool active, int zone, bool twoHands)
        {
            _localPetActive = active;
            _localPetZone = zone;
            _localPetTwoHands = twoHands;
        }

        /// <summary>ローカルの撫で状態を同期変数へ突き合わせる (Idle 中のみ・毎フレーム呼ばれる)。
        ///
        /// **`RequestSerialization` は値が変わったときだけ** — 撫でている間じゅう送らない
        /// (完了条件「撫で中の送信が Zone 変化時のみ」)。
        ///
        /// 競合は**先着優先** (不変条件 4): すでに誰かが撫でていたら Ownership を取りに行かない。
        /// 「A が触れているのに犬が B へすり寄る」を起こさないため。
        ///
        /// 出しっぱなし防止は二重にしてある — ここ (Owner が畳む) と、
        /// 表示側が毎フレーム `Utilities.IsValid` を見て導出すること。
        /// **後者があるので、この畳み込みが届かなくても ♡ が出続けることはない**
        /// (`vrc-networking` §6「ON にした人が居なくなる」)。</summary>
        void ReconcilePetSync()
        {
            // 誰も撫でていない (最頻)。Udon の extern 呼び出しをここで打ち切る。
            // **`_petRequestSent` も見る** — 要求を出したまま撫でをやめた人が
            // 解除を送れずに終わる経路を残さないため
            if (!_localPetActive && petState < 0 && !_petRequestSent) return;

            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            int myId = lp.playerId;
            bool isOwner = _dogOwner;

            // 撫でていた人がもう居ない / 犬から離れた → Owner が畳む。
            // **IMP-NET-005: 距離でも畳む。** 要求イベントは取りこぼしうるし、
            // 送り主が取れない以上「解除だけ届かなかった人」を他の手段で救えない
            if (isOwner && petState >= 0)
            {
                VRCPlayerApi a = VRCPlayerApi.GetPlayerById(attentionPlayerId);
                bool gone = !Utilities.IsValid(a);
                if (!gone)
                {
                    Vector3 away = a.GetPosition() - dogRoot.position;
                    away.y = 0f;
                    gone = away.sqrMagnitude > PetAttributeRadius * PetAttributeRadius;
                }
                if (gone)
                {
                    _petEndPending = false;
                    petState = -1;
                    attentionPlayerId = -1;
                    RequestSerialization();
                    return;
                }

                // ---- End の確認窓 (破壊的監査 v2 RC-10 = 幽霊撫で) ----
                // ReqPetEnd を受けたが手がまだ犬の傍にある = 真偽不明のまま保留した分を、
                // keep-alive (ReqPetBegin が 1.5s ごとに届き続ける) が窓の間に
                // 届いたかどうかで決める。撫でを続けている本物なら誤って畳まない
                if (_petEndPending && Time.time - _petEndPendingTime > PetEndConfirmWindow)
                {
                    _petEndPending = false;
                    // 自分 (Owner) の撫では毎フレーム直接整合するので窓の対象外
                    if (_petKeepAliveTime < _petEndPendingTime
                        && attentionPlayerId == _petEndPendingFor && attentionPlayerId != myId)
                    {
                        petState = -1;
                        attentionPlayerId = -1;
                        RequestSerialization();
                        return;
                    }
                }
            }

            if (_localPetActive)
            {
                KD_DogWanderController paradeWander = MyWander();
                if (paradeWander != null && paradeWander.ParadeReserved
                    && (paradeWander.paradeDog.Committed || paradeWander.paradeDog.Isolated || petState < 0 || attentionPlayerId != myId)) return;
                // 先着優先: 他人が撫でている間は割り込まない (不変条件 4)。
                // **自分のローカルな撫でリアクションは止めない** — 自分の画面では
                // 自分の手へ返事が返る (これは IMP-PET-005 で確認済みの既存挙動)
                if (petState >= 0 && attentionPlayerId != myId) return;

                if (!isOwner)
                {
                    // **IMP-NET-005: Ownership を取りに行かない** (不変条件 3)。
                    // **成立が返ってくるまで、ゆっくり再要求する** (Codex レビュー 2026-08-22)。
                    // 1 回だけ送る形だと、Fetch 中・他人が撫でている最中・マスター交代の谷間で
                    // 弾かれたとき、条件が解けても**その撫では二度と同期されない**。
                    //
                    // **成立した後も 1.5s 間隔で送り続けるのは意図的 (keep-alive。
                    // 破壊的監査 v2 RC-10)。** Owner は ReqPetEnd の真偽を手の位置でしか
                    // 確かめられず、手が犬の傍に残ったままだと「本当に終わった」を
                    // 判別できない。**撫でている間じゅう Begin が届き続けること**が
                    // 「まだ撫でている」の証拠になり、End の後にそれが途切れることが
                    // 「終わった」の証拠になる (Owner 側の _petEndPending 参照)。
                    // 帯域は 1 イベント / 1.5s で、旧規律 (毎フレーム送らない) は保たれている
                    if (!_petRequestSent || Time.time - _petRequestTime > PetRequestRetryInterval)
                    {
                        _petRequestSent = true;
                        _petRequestTime = Time.time;
                        SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqPetBegin");
                    }
                    return;
                }

                // **IMP-NET-005: ゾーンと両手は送らない** (petState は 0 / -1 の 2 値)。
                // 送り主が取れない以上 Owner はゾーンを知りようがなく、
                // 8 通りのイベントを生やすより「他人の画面のポーズが既定になる」ほうが安い。
                // **撫でている本人の画面は完全ローカルのままなので何も変わらない** (不変条件 7)。
                // 精度を戻すなら Owner が GetBonePosition から自分で導出する (Phase B)
                if (petState != 0 || attentionPlayerId != myId)
                {
                    petState = 0;
                    attentionPlayerId = myId;
                    RequestSerialization(); // 成立したときだけ
                }
            }
            else
            {
                // 自分の撫でが終わった
                if (_petRequestSent)
                {
                    _petRequestSent = false;
                    if (!isOwner)
                    {
                        _petEndSentTime = Time.time;
                        SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqPetEnd");
                    }
                }
                // **End は 1 回きりにしない** (破壊的監査 v2 RC-10 = 幽霊撫で)。
                // イベントの取りこぼし・Owner 交代の谷間で End が消えると、
                // 犬の傍に居る限り ♡ が出しっぱなし + 他人の撫でが先着ブロックされ続ける。
                // 同期がまだ自分を撫で主として指している間は、Begin と同じ間隔で送り直す
                if (!isOwner && petState >= 0 && attentionPlayerId == myId
                    && Time.time - _petEndSentTime > PetRequestRetryInterval)
                {
                    _petEndSentTime = Time.time;
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqPetEnd");
                }
                if (isOwner && petState >= 0 && attentionPlayerId == myId)
                {
                    petState = -1;
                    attentionPlayerId = -1;
                    RequestSerialization();
                }
            }
        }

        /// <summary>Owner への撫で開始要求 (IMP-NET-005 A5)。
        ///
        /// **送り主の playerId は Udon では取れない**ので、Owner が
        /// <see cref="NearestPlayerId"/> で解決する — 撫では手が犬に触れる距離でしか
        /// 成立しないので、<see cref="PetAttributeRadius"/> 内の最寄り = 撫でた人でよい。
        /// 先着優先 (不変条件 4) はここで判定する。</summary>
        // net: callable-ok: Owner 権威への遷移要求 (DES-NET-002 不変条件 3)。Owner / dogState / 先着 / **手が犬に触れていること** を自分で検査する
        public void ReqPetBegin()
        {
            KD_DogWanderController paradeWander = MyWander();
            if (paradeWander != null && paradeWander.ParadeReserved && (paradeWander.paradeDog.Committed || paradeWander.paradeDog.Isolated || petState < 0)) return;
            if (!IsDogOwner()) return;
            if (dogState != StateIdle) return;   // Fetch 中の撫では無視 (設計書 §1)
            if (CandyBusy()) return;             // Candy 演目・着地・退場中も無視 (破壊的監査 v4 FUNC-02/BUG-01。通常ライブの観客は撫でられる)
            if (petState >= 0)
            {
                // 先着優先。**成立中の Begin は keep-alive として記録する**
                // (破壊的監査 v2 RC-10) — ReqPetEnd の確認窓が
                // 「まだ撫でている本物」と「幽霊」をこれで見分ける
                _petKeepAliveTime = Time.time;
                return;
            }
            // **「最寄りの人」だけでは認証にならない** (Codex レビュー 2026-08-22) —
            // 遠くの誰かがイベントを送るだけで、無関係な人を撫で主にできてしまう。
            // Owner が自分で観測できる事実 = **その人の手が犬に触れる位置にあること**で認証する。
            // これで攻撃者にできるのは「放っておいても成立していた状態を早める」ことだけになる
            int id = NearestPlayerWithHandOnDog();
            if (id < 0) return;
            _petEndPending = false; // 新しい撫での成立。古い End の保留は無効
            petState = 0;
            attentionPlayerId = id;
            RequestSerialization();
        }

        /// <summary>Owner への撫で終了通知 (IMP-NET-005 A5)。
        ///
        /// **これは「終わったかもしれない」という合図でしかない。**
        /// 送り主が取れないので、第三者が送っただけで他人の撫でを消せてはいけない
        /// (Codex レビュー 2026-08-22)。だから Owner は
        /// **今の相手の手がもう犬から離れていることを自分で確かめてから**畳む。
        /// 取りこぼしても <see cref="ReconcilePetSync"/> の毎フレームの判定が拾うので、
        /// このイベントは「反応を早める」ためだけのもの。</summary>
        // net: callable-ok: 終了の合図 (DES-NET-002 不変条件 3)。**手が離れたことを Owner が自分で確かめてから畳む**
        public void ReqPetEnd()
        {
            if (!IsDogOwner()) return;
            if (petState < 0) return;
            VRCPlayerApi a = VRCPlayerApi.GetPlayerById(attentionPlayerId);
            if (Utilities.IsValid(a) && HandIsOnDog(a))
            {
                // まだ手が犬の傍にある — **ただちに誤報とは断定しない** (破壊的監査 v2
                // RC-10 = 幽霊撫で)。手を膝に置いても 1.2m 内なら旧実装は永久に拒否し、
                // ♡ が出しっぱなし + 他人の撫でを先着ブロックし続けた。
                // 保留にして、keep-alive (ReqPetBegin) が確認窓の間に途切れたら畳む
                // (判定は ReconcilePetSync)。第三者の悪意ある End では、本物の
                // keep-alive が窓内に届くので畳まれない — 旧ガードの意図は保たれる
                if (!_petEndPending)
                {
                    _petEndPending = true;
                    _petEndPendingTime = Time.time;
                    _petEndPendingFor = attentionPlayerId;
                }
                return;
            }
            _petEndPending = false;
            petState = -1;
            attentionPlayerId = -1;
            RequestSerialization();
        }

        /// <summary>手が犬に触れている人のうち、犬に最も近い人の playerId。居なければ -1。
        ///
        /// **要求イベントの認証はここが担う。** 送り主の playerId は Udon では取れないので、
        /// 「誰が言ったか」ではなく「**その状態が実際に成立しているか**」で判断する。</summary>
        int NearestPlayerWithHandOnDog()
        {
            int count = VRCPlayerApi.GetPlayerCount();
            if (count > _players.Length) count = _players.Length;
            VRCPlayerApi.GetPlayers(_players);
            Vector3 dogPos = dogRoot.position;
            float best = PetAttributeRadius * PetAttributeRadius;
            int bestId = -1;
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi p = _players[i];
                if (!Utilities.IsValid(p)) continue;
                Vector3 d = p.GetPosition() - dogPos;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq >= best) continue;
                if (!HandIsOnDog(p)) continue;
                best = sq; bestId = p.playerId;
            }
            return bestId;
        }

        /// <summary>そのプレイヤーの手が犬に触れる位置にあるか。
        ///
        /// **リモートプレイヤーの手ボーンは VRChat が同期している**ので、
        /// Owner は帯域ゼロで「本当に触っているか」を確かめられる
        /// (`IMP-NET-001` が「手の座標は送らず相手のアバターの IK から読む」を
        /// 表示側で採ったのと同じ発想を、**認証側でも使う**)。
        ///
        /// 判定は dogRoot からの距離で、ゾーンは見ない — ここで欲しいのは
        /// 「撫でが成立しうる位置に手があるか」であって、どこを撫でているかではない。</summary>
        bool HandIsOnDog(VRCPlayerApi p)
        {
            Vector3 dogPos = dogRoot.position;
            float r2 = PetHandRadius * PetHandRadius;
            Vector3 l = p.GetBonePosition(HumanBodyBones.LeftHand);
            if (l != Vector3.zero && (l - dogPos).sqrMagnitude < r2) return true;
            Vector3 rr = p.GetBonePosition(HumanBodyBones.RightHand);
            if (rr != Vector3.zero && (rr - dogPos).sqrMagnitude < r2) return true;
            return false;
        }

        /// <summary>**IMP-NET-005: ここへ来るのはマスター交代のときだけになった。**
        ///
        /// 旧方式は投げ・撫でのたびに Ownership が移り、その完了通知でようやく
        /// 状態を書いていた。Owner 固定 (犬側で `SetOwner` を呼ばない) にしたので、
        /// 移譲が起きるのは **Owner が退出して VRChat が自動で移したとき**だけ。
        /// やることは「引き継いだ状態の後始末」に一本化される。</summary>
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            if (!player.isLocal) return;

            // 自分が Owner になった。時間遷移の基準を取り直す — Update の経過時間判定なので
            // これだけで現在状態のタイマーが引き継がれる (`SendCustomEventDelayedSeconds` の
            // タイマーは移譲で消えるが、こちらは使っていない — vrc-networking §3)
            _stateEnteredTime = Time.time;

            // 旧 Owner の退室などで途中から引き継いだ場合: 対象が居なければ復帰
            if (dogState != StateIdle && !Utilities.IsValid(VRCPlayerApi.GetPlayerById(attentionPlayerId)))
                GiveUp();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // 対象が抜けたら Owner が -1 へ戻す (設計書 §2 — 忘れると犬が居ない人を追い続ける)。
            // Update 内の Utilities.IsValid ガードは保険として残す (退室イベント前の 1 フレーム対策)
            if (!IsDogOwner()) return;
            if (player == null) return;
            if (player.playerId == attentionPlayerId && dogState != StateIdle)
            {
                GiveUp();
                return;
            }
            // IMP-NET-001: 撫でていた人が抜けた → 畳む (♡ と SE の出しっぱなし防止)。
            // Idle 中は上の分岐に入らないため、ここで別に見る
            if (player.playerId == attentionPlayerId && dogState == StateIdle && petState >= 0)
            {
                petState = -1;
                attentionPlayerId = -1;
                RequestSerialization();
            }
        }

        /// <summary>ボールが掴まれた (掴んだ本人のクライアントでだけ呼ばれる)。</summary>
        public void _OnBallHeldLocal()
        {
            // Fetch 中に取り上げられたら諦める。dogState != Idle 中は pickupable=false に
            // しているので通常は起きない (発生し得るのは false が届く前の一瞬だけ)
            if (!IsDogOwner()) return;
            if (dogState != StateIdle) GiveUp();
        }

    }
}
