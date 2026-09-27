using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-DOG-008: 犬の名簿 (roster) と、犬同士が重ならないための押し出し。
    ///
    /// **同期変数ゼロ。** 犬の位置は Fetch / Wander が全クライアントで同じ入力から
    /// 同じ結論を出して動かしているので、その結果を見て押し出すこの層も
    /// 全クライアントで同じ結論になる (設計書 §2「同期しないもの」)。
    /// DES-DOG-002 の「犬をまたぐ調停を新設しない」とは矛盾しない —
    /// ここでやるのは Ownership でも注意の割り当てでもなく、**ローカルな操舵だけ**。
    ///
    /// ## 名簿としての役割 (IMP-DOG-009 と共有する土台)
    ///
    /// <see cref="dogRoots"/> / <see cref="dogRadius"/> は「犬が他の犬を知る」唯一の窓口。
    /// KD_DogFetchController は帰還の着地点 (扇) を決めるためにこれを読む。
    /// じゃれ合い (IMP-DOG-009) も同じ名簿に乗り、<see cref="dogFetch"/> を足した。
    ///
    /// **名簿に置いてよいのは「その犬が何者か」だけで、「その犬が今どうしているか」ではない。**
    /// <see cref="dogFetch"/> は前者 (どの Behaviour がその犬の Fetch か) であって、
    /// 状態そのものは持っていない。読み手は必要なときに `IsIdle` を引く —
    /// しかもその値は同期変数由来なので、全クライアントで一致する。
    /// ここへ「今の状態」をキャッシュし始めると、犬をまたぐ調停が生えて
    /// DES-DOG-002 の決定 (調停を新設しない) を崩すことになる。
    ///
    /// ## 押し出しの規則 — **動いている犬が避ける**
    ///
    /// 「先客がいたらちょっとずれる」を、状態ではなく**実測した移動量**で表す:
    ///
    /// - 自分の脚で動いている犬 (帰還中・徘徊中・追跡中) は**全力で譲る**
    /// - 止まっている犬 (くわえ / 置き / 撫でられ中 / 待機) は
    ///   <see cref="plantedYieldScale"/> 倍しか動かない = **場所を譲らない**
    /// - 同格なら**体の大きい犬が残る** (ラブが来たらポメが退く)
    ///
    /// これで「先客優先」が主・「体格」が従になる。状態を見ないので、
    /// 犬に新しい状態が増えても (Sit / じゃれ合い) この層は書き換え不要。
    ///
    /// **止まっている犬をほとんど動かさないのは見た目の要請でもある** —
    /// 置きのポーズ (頭を下げてボールを放す) の最中に根元が滑ると、
    /// IMP-FET-004 で潰した「足が動かないのに移動する」が再発する。
    ///
    /// ## 自己増幅を起こさない作り
    ///
    /// 「動いているか」は前フレームの **押し出し後**の位置と比べて測る。
    /// つまりここに出る差分は**犬自身の Update が動かした分だけ**で、
    /// 自分の押し出しが「動いている」と読まれて更に押し出す循環が構造的に起きない。
    ///
    /// 実行は LateUpdate — Fetch / Wander の Update が犬を動かし切った後に効かせる
    /// (Udon の Behaviour 間実行順は保証されないため、Update 同士では順序が定まらない)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_DogPack : UdonSharpBehaviour
    {
        [Header("名簿 (Tools/KominkaDogRun/Dog Pack が設定する)")]
        [Tooltip("犬のルート Transform。KD_DogDefs.All と同じ並び")]
        public Transform[] dogRoots;
        [Tooltip("犬ごとの体の半径 (m)。KD_DogRig.bodyScale (体幹長) の半分。" +
            "犬定義には書かない — 体格は実測できるので導出する (IMP-DOG-005 の方針)")]
        public float[] dogRadius;
        [Tooltip("犬ごとの Fetch コントローラ (dogRoots と同じ並び)。IMP-DOG-009 が " +
            "「相手は今 Fetch 中か」を引くために使う。**この層自身は使わない**")]
        public KD_DogFetchController[] dogFetch;
        [Tooltip("犬ごとの徘徊コントローラ (dogRoots と同じ並び)。IMP-DOG-009 が " +
            "「相手はプレイヤーが近くて止まっていないか」を引くために使う。この層自身は InFormation (隊形中の押し出し除外) だけを読む")]
        public KD_DogWanderController[] dogWander;

        [Header("間合い")]
        [Tooltip("保ちたい間隔 = (半径 + 半径) × これ。1.0 = 体幹長の半分どうしが触れる距離。" +
            "押し出しは食い込みの深さに比例するので、**軽く触れるくらいは自然に許される**")]
        public float spacing = DefaultSpacing;
        [Tooltip("押し出しの最大速度 (m/s)。歩き 1.1 と同程度にすると、横へずれる分が歩みの一部に見える。" +
            "大きくすると弾かれて見える (完了条件「機械的に見えない」)")]
        public float separationSpeed = DefaultSeparationSpeed;
        [Tooltip("止まっている犬が譲る割合。小さいほど「先客は動かない」。" +
            "0 にはしない — 2 匹とも止まったまま重なった場合に永久に解けなくなる")]
        public float plantedYieldScale = DefaultPlantedYieldScale;
        [Tooltip("この速度 (m/s) を超えて自分の脚で動いていたら「移動中」とみなす")]
        public float movingThreshold = DefaultMovingThreshold;
        [Tooltip("1 フレームでこれ以上動いていたらワープ (リスポーン等) とみなし、速度の測定をやり直す")]
        public float teleportThreshold = DefaultTeleportThreshold;

        // 既定値の正本 (IMP-INF-001 / B)。宣言の初期値と Start の 0 フォールバックが
        // 同じ数字をここから読む — 片方だけ直すと黙ってずれる形を構造的に消す
        const float DefaultSpacing = 1.0f;
        const float DefaultSeparationSpeed = 1.2f;
        const float DefaultPlantedYieldScale = 0.12f;
        const float DefaultMovingThreshold = 0.15f;
        const float DefaultTeleportThreshold = 1.5f;

        [Header("接地 (KD_DogFetchController と同じ値にする)")]
        public LayerMask groundLayerMask = (1 << 0) | (1 << 11);
        public float groundRayOffset = 0.5f;
        public float groundRayDistance = 4f;

        int _count;
        Vector3[] _pos;
        Vector3[] _push;
        Vector3[] _prevPos;     // 前フレームの **押し出し後** の位置
        bool[] _prevValid;
        float[] _ownSpeed;      // 犬自身の Update が動かした速度 (押し出し分を含まない)

        void Start()
        {
            if (dogRoots == null || dogRadius == null || dogRoots.Length != dogRadius.Length)
            {
                Debug.LogError("[KD_DogPack] roster invalid. Run Tools/KominkaDogRun/Dog Pack setup.");
                enabled = false;
                return;
            }
            _count = dogRoots.Length;
            _pos = new Vector3[_count];
            _push = new Vector3[_count];
            _prevPos = new Vector3[_count];
            _prevValid = new bool[_count];
            _ownSpeed = new float[_count];

            // 既存シーンへ後から足したフィールドが 0 のまま入る事故を無言で通さない
            // (KD_DogFetchController.Start と同じ検査)。spacing 0 は間合いが消えて
            // 押し出しが一切効かず、separationSpeed 0 は同じく無効化される
            if (spacing <= 0.01f || separationSpeed <= 0.01f || movingThreshold <= 0f)
            {
                Debug.LogWarning("[KD_DogPack] tuning value is 0 - falling back to defaults.");
                if (spacing <= 0.01f) spacing = DefaultSpacing;
                if (separationSpeed <= 0.01f) separationSpeed = DefaultSeparationSpeed;
                if (movingThreshold <= 0f) movingThreshold = DefaultMovingThreshold;
            }
            if (plantedYieldScale <= 0.001f) plantedYieldScale = DefaultPlantedYieldScale;
            if (teleportThreshold <= 0.01f) teleportThreshold = DefaultTeleportThreshold;

            for (int i = 0; i < _count; i++)
            {
                if (dogRadius[i] > 0.001f) continue;
                Debug.LogWarning("[KD_DogPack] dogRadius[" + i + "] が 0 - この犬は押し出しの対象外になる");
            }

            // IMP-DOG-009: この層は使わないが、欠けていると じゃれ合いが黙って止まる。
            // 読み手 (KD_DogWanderController) も自分で検査するが、原因はここで名指しする
            if (dogFetch == null || dogFetch.Length != _count
                || dogWander == null || dogWander.Length != _count)
            {
                Debug.LogWarning("[KD_DogPack] dogFetch / dogWander が名簿と揃っていない - "
                    + "じゃれ合い (IMP-DOG-009) が無効になる。Run Tools/KominkaDogRun/Dog Pack setup.");
            }
        }

        void LateUpdate()
        {
            if (_count < 2) return;
            float dt = Time.deltaTime;
            if (dt <= 0.0001f) return;

            // ---- 1) 位置と「自分の脚で動いた量」を採る ----
            for (int i = 0; i < _count; i++)
            {
                _push[i] = Vector3.zero;
                _ownSpeed[i] = 0f;
                Transform t = dogRoots[i];
                if (t == null || !t.gameObject.activeInHierarchy)
                {
                    _prevValid[i] = false;
                    continue;
                }
                Vector3 p = t.position;
                _pos[i] = p;
                Vector3 moved = p - _prevPos[i];
                moved.y = 0f;
                float m = moved.magnitude;
                // 前フレームの押し出しは _prevPos に含まれているため、ここに残るのは
                // 犬自身の Update が動かした分だけ (押し出しの自己増幅が起きない)
                if (_prevValid[i] && m <= teleportThreshold) _ownSpeed[i] = m / dt;
            }

            // ---- 2) 総当たりで押し出しを積む (犬は数匹。O(n^2) で足りる) ----
            for (int i = 0; i < _count; i++)
            {
                if (!_prevValid[i] && (dogRoots[i] == null || !dogRoots[i].gameObject.activeInHierarchy)) continue;
                if (dogRadius[i] <= 0.001f) continue;
                for (int j = i + 1; j < _count; j++)
                {
                    Transform tj = dogRoots[j];
                    if (tj == null || !tj.gameObject.activeInHierarchy) continue;
                    if (dogRadius[j] <= 0.001f) continue;
                    // 両方が隊形の枠に着いている (集合 / Live の観客) ペアは押さない — 隊形は体を密着させるので、
                    // 押し出しと枠へ戻る歩きが往復する。片方だけなら通りすがりの犬として従来どおり押す (2026-09-13)
                    if (dogWander != null && dogWander.Length == _count && dogWander[i] != null && dogWander[j] != null
                        && dogWander[i].InFormation && dogWander[j].InFormation) continue;

                    Vector3 d = _pos[i] - _pos[j];
                    d.y = 0f;
                    float dist = d.magnitude;
                    float gap = (dogRadius[i] + dogRadius[j]) * spacing;
                    if (gap <= 0.001f || dist >= gap) continue;

                    Vector3 dir;
                    if (dist > 0.001f)
                    {
                        dir = d / dist;
                    }
                    else
                    {
                        // 完全に重なった退化ケース。番号から決めた向きへ割る
                        // (全クライアントで同じ値になること だけが要件)
                        float ang = (i * 97 + j * 31) * 0.618f;
                        dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    }

                    // 食い込みの深さに比例させる = 触れかけは弱く、深く重なったら強く。
                    // 「急に弾かれるのではなく遅れて避ける」(設計書 §4 の考え方)
                    float depth = (gap - dist) / gap;
                    float yi = _ownSpeed[i] > movingThreshold ? 1f : plantedYieldScale;
                    float yj = _ownSpeed[j] > movingThreshold ? 1f : plantedYieldScale;
                    // 抵抗 = 体格 / 譲りやすさ。止まっている犬と大きい犬ほど動かない
                    float ri = dogRadius[i] / yi;
                    float rj = dogRadius[j] / yj;
                    float sum = ri + rj;
                    if (sum <= 0.0001f) continue;

                    float step = separationSpeed * depth * dt;
                    _push[i] += dir * (step * (rj / sum) * yi);
                    _push[j] -= dir * (step * (ri / sum) * yj);
                }
            }

            // ---- 3) 適用 (押し出した先で接地し直す) ----
            // 合計してから頭打ちにする (Codex 指摘) — 相手ごとの押し出しを足し込むので、
            // 3 匹に挟まれると 1 匹ぶんの上限を超えて横滑りしうる。
            // separationSpeed は「押し出しの最大速度」であってペアごとの値ではない
            //
            // **IMP-NET-005: 書くのはその犬の Owner だけ** (DES-NET-002 不変条件 1)。
            // ここは `dogRoot` ではなく `dogRoots[i]` を受けた一時変数経由で書いているため、
            // 「犬のルートを書く箇所」の棚卸しから**最も落ちやすい 1 件**だった。
            //
            // 計算 (1〜2) と測定 (`_prevPos` / `_ownSpeed`) は**全員が回したまま**にする —
            // 止めると非 Owner 側で `_ownSpeed` が腐り、Owner になった瞬間に
            // 「止まっている犬」と誤判定して先客が譲ってしまう。
            //
            // **⚠ 「結論が割れない」わけではない** (破壊的監査 v2 FUNC-10 が訂正)。
            // 測定の入力が違う — マスターの `_prevPos` は「押し出し後の自分の書き込み」、
            // 非 Owner のそれは「VRCObjectSync が配った受信位置」なので、
            // **同じ `_ownSpeed` という名前で別の物理量を測っている。**
            // 位置を書くのがマスターだけなので実害はマスター交代の 1 フレームに
            // 限られるが、この非対称を知らずに「全員同じ結論のはず」を前提に
            // 観測の間引き等を入れると割れが表に出る。変更時はここを思い出すこと
            float maxStep = separationSpeed * dt;
            for (int i = 0; i < _count; i++)
            {
                Transform t = dogRoots[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (_push[i].sqrMagnitude > maxStep * maxStep)
                    _push[i] = _push[i].normalized * maxStep;
                if (_push[i].sqrMagnitude > 1e-10f && Networking.IsOwner(t.gameObject))
                {
                    bool floating = dogWander != null && i < dogWander.Length && dogWander[i] != null && dogWander[i]._WaterFloating();
                    // IMP-DOG-020: afloat, keep the height (no snap to the riverbed) and push at most 0.1 m per frame
                    if (floating && _push[i].sqrMagnitude > 0.01f) _push[i] = _push[i].normalized * 0.1f;
                    // IMP-DOG-020 step 7: a dog on its feet (wading or on land) is not pushed where its own step would be refused
                    // (the wading depth limit, the sink guard, the band outside the spot: KD_DogWanderController._WaterPushBlocked).
                    // afloat too: never out of the spot into the band (_WaterPushBlocked decides for both)
                    bool refused = dogWander != null && i < dogWander.Length && dogWander[i] != null
                        && dogWander[i]._WaterPushBlocked(_pos[i], _pos[i] + _push[i]);
                    if (!refused) t.position = floating ? _pos[i] + _push[i] : SnapToGround(_pos[i] + _push[i]); // net: owner-only: IsOwner(dogRoots[i])
                }
                _prevPos[i] = t.position;
                _prevValid[i] = true;
            }
        }

        /// <summary>KD_DogFetchController.SnapToGround と同じ (穴の上では高さを変えない)。</summary>
        Vector3 SnapToGround(Vector3 pos)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * groundRayOffset, Vector3.down, out hit,
                    groundRayDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
            return pos;
        }
    }
}
