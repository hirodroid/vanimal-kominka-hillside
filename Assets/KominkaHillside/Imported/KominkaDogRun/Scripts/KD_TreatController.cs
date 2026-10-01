using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Components;

namespace KominkaDogRun
{
    /// <summary>
    /// IMP-TRT-001: おやつの実体 (`KD_Treat_{i}` に載る)。**枠 1 つぶんの見た目と物理。**
    ///
    /// 一文で: **状態は 1 つも持たない。枠 (<see cref="KD_TreatSlot"/>) の同期状態から、
    /// 各クライアントが自分で見た目・掴めるか・物理を導出する。**
    ///
    /// <code>
    /// Free / Returning  メッシュ全部 false / pickupable false / isKinematic true / parking に居る
    /// Reserved / Out    product のメッシュだけ true
    /// pickupable        **giverId == 自分のときだけ true**  ← 横取り防止の主役
    /// </code>
    ///
    /// ## なぜ `pickupable` が主役なのか
    ///
    /// `OnOwnershipRequest` で拒否するのは**二重の保険**にとどめる。
    /// Pickup 由来の移譲まで止まるかは**クライアント側の挙動なので実測できない**からで、
    /// 実測できないものに主たる保証を置かない。一方 `pickupable` は
    /// **同期値 `giverId` から各クライアントが自分で導く**ローカルプロパティなので、
    /// 「他人のおやつには手が届かない」が全員の画面で同じ理屈で成立する。
    ///
    /// ## 位置は VRCObjectSync が運ぶ
    ///
    /// 着地したおやつの位置 = 全クライアント共通の絶対座標で、犬はそこへ歩いて止まる —
    /// **食べる場所そのものが収束点**になる (BUG-NET-001 の教訓: ローカル導出の目標を作らない)。
    ///
    /// 同じ GameObject に VRCObjectSync (Continuous 固定) が居るため NoVariableSync
    /// (Manual を載せると SDK ビルド検証でエラー — vrc-networking §6)。
    /// **だから枠の状態は兄弟オブジェクト (`KD_TreatSlot_{i}`) が持つ。**
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class KD_TreatController : UdonSharpBehaviour
    {
        [Header("枠 (Tools/KominkaDogRun/Treat System が設定する)")]
        [Tooltip("枠番号 (0..2)")]
        public int slotIndex;

        [Tooltip("この実体が従う枠の状態。**null だと見た目も掴めるかも作られない** " +
            "(進行の正本は枠しかない — Phase 1 の KD_TreatSystem は Step 6 で畳んだ)")]
        public KD_TreatSlot slot;

        [Tooltip("定位置 (KD_TreatHome_{i})。**Respawn 先はシーン開始時の位置**なので、" +
            "これは「戻ったか」を測るための目印であって Respawn 先そのものではない")]
        public Transform parking;

        [Header("見た目の器 (Setup が入れる)")]
        [Tooltip("品目ごとの恒久 ID。**meshNodes と添字で対応する**")]
        public string[] productKeys;

        [Tooltip("品目ごとの兄弟メッシュ。productKeys[i] に対応する 1 個だけを active にする")]
        public GameObject[] meshNodes;

        [Header("調整")]
        [Tooltip("m。定位置からこれ以内なら「戻った」。Respawn の再試行を打ち切る判定")]
        public float homeRadius = DefaultHomeRadius;

        const float DefaultHomeRadius = 0.25f;

        private Rigidbody _rb;
        private VRC_Pickup _pickup;
        private VRCObjectSync _sync;

        // 適用済みの枠の値 (変化したときだけ書く。Udon の extern を毎フレーム払わない)
        private int _appliedGiver = -2;
        private string _appliedProduct = "";
        private int _appliedState = -2;

        // 手のひらへの吸着 (出した本人のローカルだけ)
        private bool _attaching;
        /// <summary>吸着が始まった後に Use の**押し下がり**を見たか。
        /// **`[出す]` を押したトリガーの離しを捨てるための順序の記録** (IMP-TRT-003)。</summary>
        private bool _armed;

        /// <summary>今このおやつが誰かに持たれているか (`VRC_Pickup.IsHeld` は同期される)。
        ///
        /// **持ち主が誰かは返さない。** Phase 1 の `HolderPlayer` / `RestingNow` は
        /// Step 6 で落とした — 前者は Owner 由来なので「出した人」とずれ
        /// (正本は枠の `giverId`)、後者は**非 Owner では velocity が実勢を表さない**。
        /// 静止しているかは <see cref="KD_TreatBox"/> がマスターの画面で
        /// 位置の差分から見る (読むのが 1 人なら割れない)。</summary>
        public bool IsHeldNow { get { return _pickup != null && _pickup.IsHeld; } }

        /// <summary>定位置へ戻っているか。**枠の Respawn 再試行の打ち切り条件。**
        /// 位置は状態の正本ではない — ここは健全性の確認にだけ使う (設計の注記)。</summary>
        // net: callable-ok: 読み取りのみ
        public bool AtHome()
        {
            if (parking == null) return true;   // 目印が無ければ判定しない (再試行を止める)
            if (_pickup != null && _pickup.IsHeld) return false;
            return (transform.position - parking.position).sqrMagnitude <= homeRadius * homeRadius;
        }

        /// <summary>いま手にあるか (吸着している / 握られている)。
        /// **枠の放置タイムアウトが読む** — 手にある間は「置きっぱなし」ではない
        /// (<see cref="KD_TreatSlot"/> の `abandonTimeout` / `holdAbandonTimeout`)。
        ///
        /// `IsHeld` は同期されるので誰の画面でも読めるが、
        /// **`_attaching` は出した本人のローカル**にしか無い。読むのは枠の Owner
        /// (= 通常は出した本人) なので、そこでは両方そろっている。</summary>
        // net: callable-ok: 読み取りのみ
        public bool InHandNow()
        {
            if (_attaching) return true;
            return _pickup != null && _pickup.IsHeld;
        }

        /// <summary>出した瞬間、手のひらへ吸着させる (**出した本人のローカルだけ**)。
        ///
        /// 掴めば <see cref="OnPickup"/> が吸着を解く。
        ///
        /// ## 時間では落とさない (2026-09-03 ユーザー決定。IMP-TRT-003)
        ///
        /// 旧実装は握られないまま 3 秒で落としていた。あれは「手に貼りついたまま
        /// 追従し続けると掴み直せない」ための保険だったが、**結果として
        /// 「あげる」タイミングを時計が決めていた。**
        /// いまは <see cref="InputUse"/> (トリガーを引いて離す) で放す —
        /// **「時間が経ったので落ちた」ではなく「自分が放したのであげた」。**</summary>
        public void _AttachToHand()
        {
            _attaching = true;
            // **武装はここで落とす。** [出す] を押した押し下がりは吸着より前なので、
            // その離しで放してしまわないため (順序で塞ぐ — KD_TreatBox.GiveStepWith)
            _armed = false;
        }

        private void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            _sync = GetComponent<VRCObjectSync>();

            if (homeRadius <= 0.01f) homeRadius = DefaultHomeRadius;

            if (_pickup == null || _sync == null)
                Debug.LogError("[KD_TreatController] VRC_Pickup / VRCObjectSync が無い - "
                    + "おやつは機能しない。Run Tools/KominkaDogRun/Treat System setup.");

            // **最初の 1 回は必ず適用する。** 枠が Free のまま誰も触らない限り
            // 変化が来ないので、シーンの初期状態 (メッシュが全部 on 等) が残る
            if (slot != null) ApplySlot(true);
        }

        private void Update()
        {
            if (slot == null) return;
            if (_armed && slot.pouch != null && slot.pouch.TreatInputBlocked()) _armed = false;
            ApplySlot(false);
            StepAttach();
        }

        /// <summary>枠の同期状態から見た目・掴めるか・物理を作る。
        /// **各クライアントが自分で導く** — 誰も配らない。</summary>
        private void ApplySlot(bool force)
        {
            int state = slot.State();
            // IMP-DOG-034 C1-misc: a not-live slot (Free / Returning) whose state did not change needs no product / giver read
            // or string compare. Unchanged behaviour, no latency: when !live every output below (meshes all off, pickupable
            // false, ClearAttach, kinematic) depends only on `live`, and a change to Reserved/Out changes `state`, which
            // still applies on the same frame. Live slots compare all three every frame as before.
            if (!force && state == _appliedState && state != KD_TreatBox.SlotReserved && state != KD_TreatBox.SlotOut) return;
            string prod = slot.Product();
            int giver = slot.GiverId();
            if (!force && state == _appliedState && prod == _appliedProduct && giver == _appliedGiver) return;
            _appliedState = state;
            _appliedProduct = prod;
            _appliedGiver = giver;

            bool live = state == KD_TreatBox.SlotReserved || state == KD_TreatBox.SlotOut;

            // ---- メッシュ: 生きている枠だけ、その品を 1 個 ----
            int want = live ? IndexOfProduct(prod) : -1;
            if (meshNodes != null)
            {
                for (int i = 0; i < meshNodes.Length; i++)
                {
                    GameObject go = meshNodes[i];
                    if (go == null) continue;
                    bool on = i == want;
                    if (go.activeSelf != on) go.SetActive(on);
                }
            }

            // ---- 掴めるか: **giverId == 自分のときだけ** ----
            VRCPlayerApi lp = Networking.LocalPlayer;
            bool mine = live && Utilities.IsValid(lp) && giver == lp.playerId;
            if (_pickup != null)
            {
                if (_pickup.pickupable != mine) _pickup.pickupable = mine;
                // 枠が畳まれたのに誰かが握ったままなら手放させる
                // (**pickupable を false にしても、既に握っている手からは落ちない**)
                if (!live && _pickup.IsHeld && Networking.IsOwner(gameObject)) _pickup.Drop();
            }

            if (!live) ClearAttach();

            // ---- 物理: **Owner だけが触る** ----
            //
            // 設計は「Free / Returning は isKinematic = true」と書いているが、
            // **非 Owner では書かない**方へ倒した (意図的な逸脱)。
            // 非 Owner の Rigidbody は VRCObjectSync が毎フレーム位置を書いていて、
            // そこへ isKinematic = false を入れるとローカル重力と綱引きになる。
            // 非 Owner 側は ObjectSync が位置を運ぶので、そもそも書く必要が無い。
            if (_rb != null && Networking.IsOwner(gameObject))
            {
                bool kin = !live;
                if (_rb.isKinematic != kin)
                {
                    if (!kin)
                    {
                        _rb.isKinematic = false;
                    }
                    else
                    {
                        _rb.velocity = Vector3.zero;
                        _rb.angularVelocity = Vector3.zero;
                        _rb.isKinematic = true;
                    }
                }
            }
        }

        /// <summary>吸着を 1 歩進める。**Owner のローカルだけ**が動かす。
        /// **時間では切らない** — 放すのは <see cref="InputUse"/> か、掴む操作か、枠の畳み。</summary>
        private void StepAttach()
        {
            if (!_attaching) return;
            if (!Networking.IsOwner(gameObject)) { ClearAttach(); return; }
            if (_pickup != null && _pickup.IsHeld) { ClearAttach(); return; }

            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { ClearAttach(); return; }

            VRCPlayerApi.TrackingData hand =
                lp.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
            Vector3 pos = hand.position;
            Quaternion rot = hand.rotation;
            if (pos.sqrMagnitude < 0.0001f)
            {
                pos = lp.GetBonePosition(HumanBodyBones.RightHand);
                if (pos.sqrMagnitude < 0.0001f) { ClearAttach(); return; }
            }

            // 手のひらの少し先。**握れる位置に出す**のが目的で、見栄えではない
            transform.position = pos + rot * new Vector3(0f, 0f, 0.06f);
            transform.rotation = rot;
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>吸着の記録を落とすだけ (物理には触らない)。
        /// **掴まれた / 枠が畳まれた / Owner を失った**ときはこちら —
        /// 物理の面倒は Pickup と <see cref="ApplySlot"/> が見る。</summary>
        private void ClearAttach()
        {
            _attaching = false;
            _armed = false;
        }

        /// <summary>**あげる = 手から離して落とす。**
        ///
        /// **初速は与えない。** 投げにすると狙いが要る遊びになり、「あげる」でなくなる —
        /// 手のひらの位置からそのまま落ちるので、犬の食べる点の導出は今までと同じ
        /// (着地したおやつの位置 = 全員共通の絶対座標)。</summary>
        private void ReleaseFromHand()
        {
            if (!Networking.IsOwner(gameObject)) return;
            ClearAttach();
            if (_pickup != null && _pickup.IsHeld) _pickup.Drop();
            // 吸着中は StepAttach が位置を書いていたので、落とすには物理へ返すだけでよい。
            // 枠が生きている間は ApplySlot が既に非 kinematic にしているが、
            // 取りこぼしていても落ちるように念のため戻す
            if (_rb != null && _rb.isKinematic && Networking.IsOwner(gameObject))
                _rb.isKinematic = false;
        }

        /// <summary>**あげる — トリガーを引いて離した瞬間に手から放す** (IMP-TRT-003)。
        ///
        /// 判定は <see cref="KD_TreatBox.GiveStepWith"/> (純関数。自己テストが全 16 通り)。
        /// ここは結果を実行するだけで、条件式を書き起こさない。
        ///
        /// ⚠ **入力イベントは 3 つの実体すべてに飛ぶ。** 反応してよいのは
        /// 吸着している 1 個だけで、それは純関数の第 1 条件が担っている。</summary>
        public override void InputUse(bool value, VRC.Udon.Common.UdonInputEventArgs args)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (lp.IsUserInVR())
            {
                bool left = _pickup != null && _pickup.IsHeld && _pickup.currentHand == VRC_Pickup.PickupHand.Left;
                if ((args.handType == VRC.Udon.Common.HandType.LEFT) != left) return;
            }
            HandleGiveUse(value);
        }

        public override void OnPickupUseDown() { HandleGiveUse(true); }
        public override void OnPickupUseUp() { HandleGiveUse(false); }
        public override void OnDrop() { ClearAttach(); }

        private void HandleGiveUse(bool value)
        {
            if (slot == null || slot.box == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || slot.GiverId() != lp.playerId || !Networking.IsOwner(gameObject)) return;
            bool panelOpen = slot.pouch != null && slot.pouch.TreatInputBlocked();
            if (panelOpen) { _armed = false; return; }
            int step = slot.box.GiveStepWith(InHandNow(), _armed, false, value);
            if (step == KD_TreatBox.GiveArm) { _armed = true; return; }
            if (step == KD_TreatBox.GiveDisarm) { _armed = false; return; }
            if (step == KD_TreatBox.GiveRelease) ReleaseFromHand();
        }

        /// <summary>器を持っている品目か。</summary>
        // net: callable-ok: 読み取りのみ
        public int IndexOfProduct(string wantProduct)
        {
            if (productKeys == null) return -1;
            for (int i = 0; i < productKeys.Length; i++)
                if (productKeys[i] == wantProduct) return i;
            return -1;
        }

        public override void OnPickup()
        {
            // OnPickup は掴んだ本人のクライアントでだけ発火するローカルイベント。
            // **掴む導線は Phase 1 のまま残っている** — 掴んで手放しても「あげる」になる
            ClearAttach();
        }

        /// <summary>**二重の保険。** 主たる横取り防止は `pickupable` (上の注記)。
        ///
        /// 枠が誰かのものである間は、その人以外への移譲要求を断る。
        /// **giver が既に居なければ通す** — 通さないと退室後の片付けまで塞ぐ。</summary>
        public override bool OnOwnershipRequest(VRCPlayerApi requester, VRCPlayerApi newOwner)
        {
            if (slot == null) return true;
            int giver = slot.GiverId();
            if (giver < 0) return true;                                  // Free
            if (newOwner != null && newOwner.playerId == giver) return true;
            VRCPlayerApi g = VRCPlayerApi.GetPlayerById(giver);
            if (!Utilities.IsValid(g)) return true;                      // 出した人はもう居ない
            return false;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // 物理の主導権が移ったので、その画面での導出をやり直す
            if (slot != null) ApplySlot(true);
        }

        /// <summary>
        /// 定位置へ戻す (回収)。**戻り値 = 実際に戻せたか。**
        ///
        /// ## 自分で Owner かを確かめる (2026-08-24 Codex 指摘で追加)
        ///
        /// 旧コメントは「**呼ぶ側が Ownership を取ってから呼ぶ**」と書いていたが、
        /// 呼び出し側は `SetOwner()` の**直後**に呼んでいて、移譲は非同期なので
        /// **1 回目は非 Owner のまま `Respawn` / `Drop` / 速度書き換えを実行していた。**
        /// つまり**コメントが実装の保証していないことを書いていた。**
        ///
        /// → **ここで `IsOwner` を見て、false なら何もせず false を返す。**
        /// 呼び出し側は戻り値で「まだ戻せていない」を知り、`Returning` を維持して再試行する。
        /// **「呼ぶ側が取ってから」を規約で守るのではなく、取れていないことを検出する。**
        ///
        /// **ワープには `FlagDiscontinuity()` を使う** (2026-08-24 にノード登録で実測)。
        /// 付けないと補間が働いて、部屋の反対側から定位置まで滑ってくる。
        /// </summary>
        public bool _RespawnHome()
        {
            if (_sync == null) return false;
            // **非 Owner が書いても効かない。** 効かないまま「戻した」と報告するのが
            // いちばん悪い (呼び出し側が Returning を畳んでしまい、枠が塞がったままになる)
            if (!Networking.IsOwner(gameObject)) return false;

            // **`pickupable = false` は既に握られている物を落とさない** (Step 4 の実測)。
            // 握られたまま回収すると手にくっついて残る
            if (_pickup != null && _pickup.IsHeld) _pickup.Drop();
            ClearAttach();
            _sync.Respawn();
            _sync.FlagDiscontinuity();
            // 残存速度で定位置から転がり出さない (KD_BallResetButton と同じ処置)
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            return true;
        }
    }
}
