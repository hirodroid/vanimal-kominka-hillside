using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-TRT-001 Phase 4b Step 7: **食べ終わりの絵と音。**
    /// 設計は [IMP-TRT-001](../../../docs/development/issues/IMP-TRT-001-treat-system.md)
    /// の設計 5「撫でエフェクトとは**テクスチャの作り方だけ共有する**」。
    ///
    /// 一文で: **犬が食べ終わった瞬間に、食べた場所へ品目ごとの絵を 1 回だけ撃つ。**
    ///
    /// ## なぜこれが要るのか (この Step の存在理由)
    ///
    /// おやつ 4 種は**性能差がゼロ**で、値段差の根拠は「絵の強さ」しかない。
    /// ここが無いと **「なぜ 5pt のジャーキーを買うのか」を Play で判定できない。**
    ///
    /// ## 同期を 1 本も増やさない
    ///
    /// 発火の時刻は `KD_TreatBox.dogEatMs + eatDuration` から**全クライアントが導出する**。
    /// 「今エフェクトを出せ」を配らないので、同期変数も RPC も 0 本
    /// (Phase 4b の 21 int から 1 本も増えていない)。
    ///
    /// ## どの絵かは**枠の `product`** から引く。プレイヤーの札からではない
    ///
    /// `KD_PlayerFxTag` (撫でエフェクトの札) から引くと、**同じ人が次のおやつを出した瞬間に
    /// 札が更新され、過去の食べ始めに別の絵が付く** (Issue「導出で済ませる案は 3 つとも
    /// 成立しない」の 2 行目)。枠の `product` は `outMs` の世代とセットで動くので、
    /// 食べ始めの時点で掴んでおけば後から変わらない。
    ///
    /// ## 撫でとは**発火の口が違う**
    ///
    /// <code>
    /// 撫で    持続   ps.Play() / ps.Stop() + RateOverTime を段階で動かす
    /// おやつ  単発   ps.Emit(n) を 1 回
    /// </code>
    ///
    /// **同じ口にまとめない。** まとめると、片方の「止め忘れ」がもう片方の
    /// 「出っぱなし」になる。共有するのはテクスチャの作り方 (`KD_ParticleBaker`) と
    /// **うるささの規律** (`maxParticles &lt;= 30` の機械検査) だけ。
    ///
    /// ## エッジ 1 回に閉じる — 状態で出さない
    ///
    /// **食べ終わってから枠が解放されるまでには一瞬の間がある** (枠の突き合わせは
    /// 0.25s おき)。その間 `EatFinishedFor` は true のままなので、
    /// 「食べ終わっている間ずっと出す」と書くと**同じ食事で何度も撃つ。**
    /// 犬ごとに `(食べ始め, 撃ったか)` を持ち、**立ち上がりで 1 回だけ**撃つ。
    /// </summary>
    /// <remarks>
    /// **SyncMode None・同期変数 0 本。** 見えているものは全員の手元で同じ入力
    /// (`dogEatMs` と枠の `product`) から作られるので、配る必要が無い。
    ///
    /// ParticleSystem は品目ごとに 4 本並べる (`Renderer.material` を実行時に触らない —
    /// `vrc-asset-bundle` / `vrc-udonsharp` §6。IMP-FET-009 Phase 1 と同じ答え)。
    /// **Simulation Space = World** なので、撃つ直前に位置を移してから撃てば
    /// 同フレームに複数回撃っても粒が混ざらない。
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_TreatFx : UdonSharpBehaviour
    {
        [Header("配線 (Tools/KominkaDogRun/Treat System が設定する)")]
        [Tooltip("食べ始めの正本。null だと何も出ない (機能ごと無効)")]
        public KD_TreatBox box;

        [Tooltip("枠 3 つ。**どの絵を出すかはここの product から引く**")]
        public KD_TreatSlot[] slots;

        [Tooltip("犬の名簿。頭数を数えるためだけに要る")]
        public KD_DogPack pack;

        [Tooltip("品目ごとの粒 (**添字は productKeys と揃える**)。Simulation Space = World")]
        public ParticleSystem[] fx;

        [Tooltip("恒久 ID (20..23)。**fx と同順・同数**であることを Verify が数える")]
        public string[] productKeys;

        [Tooltip("品目ごとの音源 (fx と同じノードに載せる — 移せば音の位置も付いてくる)")]
        public AudioSource[] fxAudio;

        [Tooltip("**4 種で共通の「食べた」音。** 犬の反応は全種同一という設計に合わせる")]
        public AudioClip eatClip;

        [Tooltip("品目ごとの追い音。**ジャーキーだけ非 null** — " +
            "設計表で音が書かれているのは最上位の 1 種だけで、そこが序列の印になる")]
        public AudioClip[] flourishClips;

        [Header("調整")]
        [Tooltip("1 回に撃つ粒の数。**maxParticles を超えて撃っても切り捨てられる**ので、" +
            "うるささの上限は粒側 (<= 30) が持つ")]
        public int burstCount = 10;

        /// <summary>突き合わせの間隔 (秒)。毎フレーム全犬を見ない (Udon VM)。
        /// 食べる時間 (2.6s 前後) に対して十分細かいので、エッジがずれても目に見えない。</summary>
        const float TickInterval = 0.05f;

        private float _nextTick;
        private int _dogCount;
        private int _eatMsDuration;

        // ---- 犬ごとの掛け金 (**ローカル。同期しない**) ----
        //
        // 食べ始めを見た時点で「どの絵を・どこで撃つか」を固めてしまう。
        // あとから枠が解放されても、掴んだ値はここに残る。

        /// <summary>この犬について今追いかけている食べ始め (0 = 追いかけていない)。</summary>
        private int[] _armedEatMs;
        /// <summary>そのとき枠に入っていた品目 (恒久 ID)。-1 = 引けなかった。</summary>
        private string[] _armedProduct;
        /// <summary>最後に見えた実体の位置。**撃つ場所。**</summary>
        private Vector3[] _armedPos;
        /// <summary>もう撃ったか。**エッジ 1 回に閉じる鍵。**</summary>
        private bool[] _armedFired;

        private void Start()
        {
            if (burstCount <= 0) burstCount = 10;

            _dogCount = 0;
            if (pack != null && pack.dogRoots != null) _dogCount = pack.dogRoots.Length;
            if (_dogCount <= 0)
                Debug.LogError("[KD_TreatFx] pack 未配線 - 食後エフェクトが出ない。"
                    + "Run Tools/KominkaDogRun/Treat System setup.");

            _armedEatMs = new int[_dogCount];
            _armedProduct = new string[_dogCount];
            _armedPos = new Vector3[_dogCount];
            _armedFired = new bool[_dogCount];

            _eatMsDuration = box != null ? (int)(box.eatDuration * 1000f) : 2600;
            if (_eatMsDuration <= 0) _eatMsDuration = 2600;

            // **粒を「常に再生中・出力 0」で待たせる。** 停止した ParticleSystem へ
            // Emit しても更新されず、粒が出たまま止まって見える
            if (fx != null)
                for (int i = 0; i < fx.Length; i++)
                    if (fx[i] != null && !fx[i].isPlaying) fx[i].Play();
        }

        private void Update()
        {
            if (box == null || _dogCount <= 0) return;
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + TickInterval;

            int now = Networking.GetServerTimeInMilliseconds();
            for (int d = 0; d < _dogCount; d++) StepDog(d, now);
        }

        /// <summary>犬 1 匹ぶんの立ち上がり検出。</summary>
        private void StepDog(int d, int now)
        {
            int eatMs = box.EatStartMsOf(d);

            // ---- 新しい食事が始まった ----
            if (eatMs != 0 && eatMs != _armedEatMs[d])
            {
                _armedEatMs[d] = eatMs;
                _armedProduct[d] = "";
                // **既定は「撃たない」。** 掴めたものだけを撃つ側へ倒す
                _armedFired[d] = true;

                KD_TreatSlot s = SlotOfDog(d);
                if (s != null)
                {
                    _armedProduct[d] = s.Product();
                    _armedPos[d] = s.BodyPosition();
                    // **入った時点で既に食べ終わっていたら撃たない** —
                    // Late Joiner や遅れて届いた同期値で、過去の食事の絵が突然出るのを防ぐ
                    if (now - eatMs < _eatMsDuration) _armedFired[d] = false;
                }
            }

            // ---- 食べが中断された (破壊的監査 v2 RC-6) ----
            // dogEatMs が 0 へ戻った (Claim の張り替え・マスター交代) のに
            // 予定時刻で無条件に撃つと「食べていないのに完食の絵と音」が出る。
            // アームを解除する — 次の食事は eatMs の変化で正しく再アームされる
            if (eatMs == 0 && _armedEatMs[d] != 0 && !_armedFired[d])
            {
                _armedFired[d] = true;
                return;
            }

            if (_armedEatMs[d] == 0 || _armedFired[d]) return;

            // 食べている間は場所を追いかける (枠が畳まれたら最後の位置がそのまま残る)
            KD_TreatSlot live = SlotOfDog(d);
            if (live != null) _armedPos[d] = live.BodyPosition();

            if (now - _armedEatMs[d] < _eatMsDuration) return;

            Fire(_armedProduct[d], _armedPos[d]);
            _armedFired[d] = true;
        }

        /// <summary>犬 d が今掴んでいる枠 (世代が一致していて、かつ出ているものだけ)。</summary>
        private KD_TreatSlot SlotOfDog(int d)
        {
            if (slots == null) return null;
            int i = box.ClaimedSlotOf(d);
            if (i < 0 || i >= slots.Length) return null;
            KD_TreatSlot s = slots[i];
            if (s == null) return null;
            if (s.State() != KD_TreatBox.SlotOut) return null;
            if (s.OutMs() != box.ClaimedGenOf(d)) return null;   // 枠の再利用を見抜く
            return s;
        }

        /// <summary>その品目の絵を <paramref name="pos"/> で 1 回だけ撃つ。
        ///
        /// **World 空間なので、移してから撃てば同フレームに複数回撃っても混ざらない。**</summary>
        private void Fire(string productId, Vector3 pos)
        {
            int idx = IndexOfProduct(productId);
            if (idx < 0) return;

            ParticleSystem ps = fx[idx];
            if (ps != null)
            {
                ps.transform.position = pos;
                if (!ps.isPlaying) ps.Play();
                ps.Emit(burstCount);
            }

            AudioSource a = (fxAudio != null && idx < fxAudio.Length) ? fxAudio[idx] : null;
            if (a == null) return;
            a.transform.position = pos;
            if (eatClip != null) a.PlayOneShot(eatClip);
            AudioClip extra = (flourishClips != null && idx < flourishClips.Length)
                ? flourishClips[idx] : null;
            if (extra != null) a.PlayOneShot(extra);
        }

        private int IndexOfProduct(string productId)
        {
            if (fx == null || productKeys == null) return -1;
            int n = fx.Length;
            if (productKeys.Length < n) n = productKeys.Length;
            for (int i = 0; i < n; i++)
                if (productKeys[i] == productId) return i;
            return -1;
        }
    }
}
