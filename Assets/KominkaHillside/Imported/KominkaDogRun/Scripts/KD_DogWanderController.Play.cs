using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>KD_DogWanderController の partial (Play)。IMP-INF-019 C6-α で分割 (2026-09-13)。
    /// Start/Update/LateUpdate・同期変数・public フィールドは主ファイル。ここは主にメソッド (セクション固有の private フィールド・const は同居する)。分割はメンバー境界での機械的な移動で挙動不変。共有処理の所在はメソッド名で grep する (ファイル名は目安)。</summary>
    public partial class KD_DogWanderController
    {
        // ================================================================ じゃれ合い (IMP-DOG-009)

        /// <summary>
        /// じゃれ合いの予定表を引き、自分が今フレーム遊ぶなら足を動かして true を返す。
        ///
        /// **全クライアント・両方の犬が同じ結論を出すことが唯一の要件。**
        /// 入力はサーバー時刻と名簿だけで、状態も同期変数も持たない。
        /// 1 サイクルに遊ぶのは **1 組だけ** — 3 匹以上でも「別々の相手を選んで破綻する」が
        /// 構造的に起こらない (完了条件 3 を実装ではなく設計で満たす)。
        /// </summary>
        bool UpdatePlay(double serverTime)
        {
            if (_packRoots == null)
            {
                if (debugInstrument) _dbgReason = DbgNoPack;
                return false; // 名簿が無い / 壊れている = 1 匹版
            }
            int n = _packRoots.Length;
            if (n < 2)
            {
                if (debugInstrument) _dbgReason = DbgAlone;
                return false;
            }

            // ---- いつ (共有サイクル。seedSalt を混ぜない — PlayRand の注意書き) ----
            int cyc = (int)(serverTime / playCyclePeriod);
            float phase = (float)(serverTime - (double)cyc * playCyclePeriod);
            if (phase < 0f)
            {
                cyc -= 1;
                phase += playCyclePeriod;
            }

            EnsurePlaySchedule(cyc, n);
            float coreLen = _playCoreLength;
            float total = _playWindowTotal;
            float start = _playWindowStart;
            if (debugInstrument)
            {
                _dbgCycle = cyc;
                _dbgPhase = phase;
                _dbgWinStart = start;
                _dbgWinCore = coreLen;
                _dbgWinTotal = total;
            }
            if (phase < start || phase >= start + total)
            {
                if (debugInstrument)
                {
                    _dbgReason = DbgWindowClosed;
                    _dbgType = 0;
                    _dbgRole = 0;
                }
                return false;
            }

            // ---- 型を先に決める。対面は頭数によらず 2 匹固定なので参加人数に効く ----
            // (3 点の重心は自己安定しないので対面を多頭化しない — 設計書 §8)
            bool isFace = _playIsFace;

            // ---- 何匹参加するか (IMP-DOG-012) ----
            //
            // **旧実装は「全ペアから 1 組」だった。** それだと 1 匹が選ばれる確率が
            // ちょうど 2/n になり、**頭数を増やすほど個々の犬が遊ばなくなる**。
            // ここを「各犬が自分で参加を導出する集合」へ変えたのがこの Issue の本体。
            // **参加率が n に依存しなくなる** = 多頭化が体験を薄めるのではなく濃くする。
            int want = _playPartySize;

            // ---- 自分が参加するか = 抽選値の順位で決める ----
            //
            // **`seedSalt` は混ぜない。index を「引数」として渡す** —
            // 種を分けると 2 匹が別の予定表を見る (PlayRand の注意書き)。
            // 同じ数列の別の項を読む形なので、**全員が全員の抽選値を再現でき**、
            // 参加上限 want を全員が同じように守れる。
            // `seedSalt` ではなく `dogIndex` なのは、名簿に seedSalt が無い =
            // 他犬の値を読めないため。**制約が正しい方向へ効いている。**
            // The cycle's rolls and both rank lookups are prepared by EnsurePlaySchedule.

            int myRank = PlayRankOf(dogIndex, n);
            if (debugInstrument)
            {
                _dbgPartySize = want;
                _dbgPairB = myRank;
                _dbgPairCycle = cyc;
            }
            if (myRank >= want)
            {
                if (debugInstrument)
                {
                    _dbgReason = DbgNotMyTurn;
                    _dbgType = 0;
                    _dbgRole = 0;
                }
                return false; // 今回は参加しない
            }

            float t = phase - start;

            // ---- 途中参加 (ユーザー要望「途中で参加して最大 4 匹」) ----
            // **順位 0/1 は頭からそろう** (でないと窓の頭に誰も居ない回ができる)。
            // 3 匹目以降は本体の前半のどこかで入ってくる
            float myJoin = PlayJoinTime(cyc, myRank, coreLen);
            if (t < myJoin)
            {
                if (debugInstrument)
                {
                    _dbgReason = DbgNotJoinedYet;
                    _dbgType = 0;
                    _dbgRole = 0;
                }
                return false; // まだ入る時刻でない = それまでは徘徊している
            }

            // ---- 離れる (窓の最後。やめ方も演技にする) ----
            if (t >= coreLen)
            {
                int away = PlayFleeDogAt(cyc, coreLen, want, n, coreLen - 0.001f);
                if (away == dogIndex || away < 0) away = PlayDogOfRank(myRank == 0 ? 1 : 0, n);
                if (away < 0) return false;
                if (debugInstrument)
                {
                    _dbgReason = DbgPlaying;
                    _dbgType = 3;
                    _dbgRole = 0;
                    _dbgPairA = away;
                }
                PlayBreakOff(cyc, start, coreLen, want, n, isFace, away);
                return true;
            }

            // ---- 対面 (2 匹固定) ----
            if (isFace)
            {
                int other = PlayDogOfRank(myRank == 0 ? 1 : 0, n);
                if (other < 0) return false;
                int blocked = PlayBlockReason(other);
                if (blocked != 0)
                {
                    if (debugInstrument) _dbgReason = blocked;
                    return false;
                }
                if (debugInstrument)
                {
                    _dbgReason = DbgPlaying;
                    _dbgType = 2;
                    _dbgRole = 0;
                    _dbgPairA = other;
                }
                _playing = true; // IMP-CAM-002: 撮影が拾う「じゃれ合っている」
                PlayFace(cyc, start, other, (_selfRadius + _packRadius[other]) * playContactSpacing);
                return true;
            }

            // ---- 追いかけっこ: 逃げ役 1 匹 + 追う役 n-1 ----
            int seg = (int)(t / roleSwapPeriod);
            int flee = PlayFleeDogAt(cyc, coreLen, want, n, seg * roleSwapPeriod);
            if (flee < 0) return false;
            if (debugInstrument) _dbgPairA = flee;

            if (dogIndex == flee)
            {
                // **逃げ役は追う相手が 1 匹でも「使える」ことを確かめる。**
                // 全員が止まっているのに 1 匹だけ全力で走っていると、
                // 追いかけっこではなく「変な犬」になる
                bool anyChaser = false;
                for (int r = 0; r < want && !anyChaser; r++)
                {
                    int d = PlayDogOfRank(r, n);
                    if (d < 0 || d == dogIndex) continue;
                    if (PlayJoinTime(cyc, r, coreLen) > t) continue;
                    if (PlayBlockReason(d) == 0) anyChaser = true;
                }
                if (!anyChaser)
                {
                    if (debugInstrument) _dbgReason = DbgOtherPaused;
                    return false;
                }
                if (debugInstrument)
                {
                    _dbgReason = DbgPlaying;
                    _dbgType = 1;
                    _dbgRole = 2;
                }
                _playing = true;
                PlayFlee(cyc, t);
                return true;
            }

            // 追う役: 逃げ役が使えなければ降りる。
            // **逃げ役を「使える犬」から選び直さない** — 可用性はローカル観測 (RC-5) なので、
            // 選定に混ぜるとクライアントごとに違う犬が逃げ役になり、絵そのものが割れる。
            // 降りるだけなら「その犬が遊ばない」で済み、次の区間 (roleSwapPeriod) で
            // 別の逃げ役が引かれて**自然に復帰する**
            int fb = PlayBlockReason(flee);
            if (fb != 0)
            {
                if (debugInstrument) _dbgReason = fb;
                return false;
            }
            if (debugInstrument)
            {
                _dbgReason = DbgPlaying;
                _dbgType = 1;
                _dbgRole = 1;
            }
            _playing = true;
            // 追う: **逃げ役の共有ホップ先 P への回り込み点**へ走る (IMP-NET-002 / B9)。
            // 旧実装は逃げ役の実位置を目標にしており、相手のずれ δ がそのまま自分の
            // 経路に入った (DES-NET-001 §4-2 = 等級 D)。回り込み点は P・前ホップ・順位
            // だけから導くので全クライアントで一致し、「先読みして角を取る」絵になる (§4-4)。
            // 追う役同士は順位の扇 (ChaseFanRad) で散らす — 全員が同じ P を見るため
            int chaseHop = (int)(t / _packFleeHop[flee]);
            Vector3 aim = ChaseInterceptByHop(cyc, chaseHop, flee, dogIndex, n);
            // 相手の実位置は接触の間合いの減速 (と、追い付いたときの視線) にだけ使う
            Vector3 fleePos = _packRoots[flee].position; // sync: local-ok: 減速と視線のみ — 目標は ChaseInterceptByHop の共有導出 (DES-NET-001 §4-4)
            float chaseSpeed = ContactSpeedCap(fleePos, (_selfRadius + _packRadius[flee]) * playContactSpacing, runSpeed);
            if (chaseSpeed <= 0f)
            {
                // 追い付いた。相手が次のホップへ走り出すまで、止まって目で追う
                Coast();
                FaceTowards(fleePos);
                return true;
            }
            StepToward(aim, 0f, chaseSpeed);
            return true;
        }

        /// <summary>抽選値の順位 (0 = 最小)。同値は index の小さい方を先にして全順序にする。</summary>
        int PlayRankOf(int j, int n)
        {
            return _playRankByDog[j];
        }

        /// <summary>順位から犬番号を引く (PlayRankOf の逆)。見つからなければ -1。</summary>
        int PlayDogOfRank(int rank, int n)
        {
            return rank >= 0 && rank < n ? _playDogByRank[rank] : -1;
        }

        /// <summary>その順位の犬が窓に入ってくる時刻 (窓頭からの秒)。
        /// **順位 0/1 は 0 = 頭からそろう。** 引数が順位なので全犬が同じ値を出す。</summary>
        float PlayJoinTime(int cyc, int rank, float coreLen)
        {
            if (rank < 2) return 0f;
            float span = coreLen * Mathf.Clamp01(playJoinWindow);
            return PlayRand(cyc, PlayStreamJoinBase + rank) * span;
        }

        /// <summary>ある時刻の逃げ役 (犬番号)。**区間の開始時刻で判定する** —
        /// 区間の途中で誰かが参加しても役が入れ替わらないようにするため
        /// (入れ替わると走っている最中に目標が飛ぶ)。</summary>
        int PlayFleeDogAt(int cyc, float coreLen, int want, int n, float segStart)
        {
            int joined = 0;
            for (int r = 0; r < want; r++)
                if (PlayJoinTime(cyc, r, coreLen) <= segStart) joined++;
            if (joined < 2) joined = 2; // 順位 0/1 は必ず居る

            int seg = (int)(segStart / roleSwapPeriod);
            int slot = (int)(PlayRand(cyc, PlayStreamFleeSlotBase + seg) * joined);
            if (slot >= joined) slot = joined - 1;
            return PlayDogOfRank(slot, n);
        }

        /// <summary>その犬とじゃれ合えない理由 (0 = 使える)。
        /// **Fetch は同期変数から、接近は相手の判定結果をそのまま読む** —
        /// 自分で測り直すとヒステリシスの帯で結論が割れる (設計書 §8)。</summary>
        int PlayBlockReason(int idx)
        {
            Transform tr = _packRoots[idx];
            if (tr == null || !tr.gameObject.activeInHierarchy) return DbgOtherGone;
            // 2026-10-07 audit RC-5: ホームどうしが遠い・川の対岸なら組まない (入力は同期されたホームと静的な川のデータだけ = 全端末で同じ。距離は対称なので両方の犬が同時に降りる)
            if (PlayHomesApart(idx)) return DbgOtherFar;
            KD_DogFetchController f = _packFetch[idx];
            if (f == null || !f.IsIdle) return DbgOtherFetch;
            // 2026-10-07 audit RC-5: 水の中 (同期の waterPose) の相手は追わない
            if (f.WaterPose != 0) return DbgOtherBusy;
            KD_DogWanderController w = _packWander[idx];
            if (w == null || w.IsPausedForPlayer) return DbgOtherPaused;
            // 2026-10-07 audit RC-5: リード中 (切符はリード中に限る) と洗い場の保持は IsPausedForPlayer に立たないので同期値から読む
            // (WashHeldSynced: ローカルの台の降下中は含めない。IsPausedForPlayer と同じく、伝播の遅れの間だけ端末差が出うる)
            if (w.WashHeldSynced) return DbgOtherBusy;
            if (hillsideLeashes != null && hillsideLeashes._IsLeashed(idx)) return DbgOtherBusy;
            return 0;
        }

        // 2026-10-07 audit RC-5: じゃれ合い・あいさつの距離の上限 (水平 m)。2.4 m/s で約 10 s = 本体の最短 18 s の半分強で着く。0 以下は既定へ戻す
        [SerializeField] private float playMaxHomeDistance = 25f;
        public const int DbgOtherFar = 20;   // 2026-10-07 audit RC-5: 相手のホームが遠い / 川の対岸
        public const int DbgOtherBusy = 21;  // 2026-10-07 audit RC-5: 相手がリード中・洗い場・水の中

        float PlayMaxDistance() { return playMaxHomeDistance > 0f ? playMaxHomeDistance : 25f; }

        /// <summary>2026-10-07 audit RC-5: 自分のホームと相手のホームが上限より遠い、または川の対岸。
        /// ホームは KH_LeashManager の同期値 releasedHomes から全端末で同じ位置へ置かれる Transform。</summary>
        bool PlayHomesApart(int idx)
        {
            if (areaCenter == null || _packAreaCenter == null || idx >= _packAreaCenter.Length || _packAreaCenter[idx] == null) return false;
            Vector3 mine = areaCenter.position, other = _packAreaCenter[idx].position;
            float lim = PlayMaxDistance();
            if (Horizontal(mine, other) > lim) return true;
            return riverData != null && riverData._OtherBank(mine, other);
        }

    }
}
