using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>
    /// 裏山改変 (IMP-WLD-011, 2026-10-04 ユーザー決定 案 A): 洗いの写真。docs/development/issues/IMP-WLD-011-wash-photos-kept.md。
    ///
    /// - 枠は「洗う」(HillsideGroupWash、6 枚)。散歩・撫で・遊びの写真では上書きされない
    /// - 節目は洗いの側 (KominkaHillside.KH_DogWash の photo 部分) が決めて、洗っている本人の端末だけがここを呼ぶ:
    ///   泡が広がった (WashShotFoam) / すすぎの途中 (WashShotRinse) / 洗い上がり (WashShotDone)。1 回の洗いで各 1 枚、最大 3 枚
    /// - 構図は撫での構図のうち、洗っている人 (この端末のプレイヤー) と犬が入るもの: 肩越し / 俯瞰 / 正面。
    ///   ShootPetShot → ShootFramed → ClearCameraPos (壁・地面の検査) をそのまま通る
    /// - 洗っている犬は撫でのバーストを止める (WashingDog。KD_PhotoDirector.Update の撫での積算と _BurstStep が読む)
    /// - 撮らないとき: 撮影 OFF・見る場所なし・読み込み前 (CapturePaused)、移動の暗転中、寝る場所の指定中、寝ている犬
    /// - すべてローカル (同期なし)
    ///
    /// 呼び方: KH_DogWash が washDog / washMilestone を書いてから _WashShoot() (引数なし。ハーネスも同じ口を使う)。
    /// 戻り値 true = 引き受けた (洗い上がりは washShotDoneDelay 秒後、カメラが前の撮影の 1 フレーム中なら 2 フレームおきに 3 回まで撮り直す)。
    /// </summary>
    public partial class KD_PhotoDirector
    {
        public const int WashShotNone = 0, WashShotFoam = 1, WashShotRinse = 2, WashShotDone = 3;

        [Header("裏山: 洗いの写真 (IMP-WLD-011。試作値。Photo/1 が書く)")]
        public bool washShotEnabled = true;
        [Tooltip("洗い上がり (清潔の合図 / ブルブル) から撮るまでの秒数。ブルブル (1.2 s) の途中を狙う")]
        public float washShotDoneDelay = 0.5f;
        // 2026-10-07 audit RC-9: 洗いだけでなく Shoot (全経路の共通の入口) もこれを見る。名前はシーン値を保つため変えない
        [Tooltip("移動の暗転 (KH_LeashManager.travelFade)。暗転中はどの写真も撮らない (Shoot が見る)。null なら見ない")]
        public KominkaHillside.KH_TravelFade washFade;
        [Tooltip("寝る場所の指定 (KD_TreatPouch.sleepPlacement)。指定中は撮らない。null なら見ない")]
        public KD_SleepPlacement washSleepPlacement;

        // ---- 依頼 (KH_DogWash が _WashShoot の直前に書く。ローカル) ----
        [HideInInspector] public int washDog = -1;
        [HideInInspector] public int washMilestone;

        int _washPendDog = -1;   // 撮る予定の犬 (-1 = なし)。1 枚ずつ
        int _washPendKind;
        int _washRetries;
        int _washVariant;        // 洗い上がりを撮るたびに 1 進め、次の洗いの構図をずらす
        // ---- ハーネスが読む ----
        int _washShots;
        int _washLastKind;
        int _washLastDog = -1;
        string _washLastReason = "";

        /// <summary>洗いの節目の 1 枚を頼む (KH_DogWash、洗っている本人の端末)。ローカル専用。</summary>
        public bool _WashShoot()
        {
            _washLastReason = "";
            int dog = washDog, kind = washMilestone;
            string why = WashBlocked();
            if (why.Length == 0 && (kind < WashShotFoam || kind > WashShotDone)) why = "unknown milestone " + kind;
            if (why.Length == 0 && (!ValidDog(dog) || SleepingDog(dog))) why = "dog " + dog + " invalid or asleep";
            if (why.Length == 0 && _washPendDog >= 0) why = "another wash photo is pending";
            if (why.Length > 0) { _washLastReason = why; return false; }
            _washPendDog = dog;
            _washPendKind = kind;
            _washRetries = 0;
            if (kind == WashShotDone && washShotDoneDelay > 0f) SendCustomEventDelayedSeconds(nameof(_WashShotStep), washShotDoneDelay);
            else _WashShotStep();
            return true;
        }

        /// <summary>洗いの 1 枚 (直接 / 遅延イベント)。撮る直前にもう一度「撮らない」条件を見る。</summary>
        public void _WashShotStep()
        {
            int dog = _washPendDog;
            if (dog < 0) return;
            string why = WashBlocked();
            if (why.Length == 0 && (!ValidDog(dog) || SleepingDog(dog))) why = "dog " + dog + " invalid or asleep";
            if (why.Length > 0) { _washPendDog = -1; _washLastReason = why; return; }
            int kind = _washPendKind;
            if (!ShootWash(dog, kind))
            {
                if (_washRetries < 3) { _washRetries++; SendCustomEventDelayedFrames(nameof(_WashShotStep), 2); return; }
                _washPendDog = -1;
                _washLastReason = "capture refused";
                return;
            }
            _washPendDog = -1;
            _washShots++;
            _washLastKind = kind;
            _washLastDog = dog;
            if (kind == WashShotDone) _washVariant++;
            MarkShot(dog);
        }

        /// <summary>撮らない理由 ("" = 撮ってよい)。</summary>
        string WashBlocked()
        {
            if (!washShotEnabled) return "wash photos off";
            if (!hillsideEventGroups) return "no wash category (prequel layout)";
            if (CapturePaused()) return "paused (OFF / no surfaces / loading)";
            if (_n <= 0) return "not started";
            if (washFade != null && washFade._Busy()) return "fade";
            if (washSleepPlacement != null && washSleepPlacement.Active) return "sleep placement";
            return "";
        }

        /// <summary>撫での構図から、洗っている人と犬が入るものを節目ごとに選ぶ (泡 = 肩越し、すすぎ = 俯瞰、洗い上がり = 正面。洗うたびに 1 つずらす)。
        /// 犬目線・寄り・ローアングル・真横・引きは使わない (人が入らない、真横は洗う人の背中から撮ることがある)。</summary>
        bool ShootWash(int dog, int kind)
        {
            int k = (kind - 1 + _washVariant) % 3;
            int shot = k == 0 ? ShotBack : (k == 1 ? ShotDrone : ShotFront);
            _hillsideGroupOverride = HillsideGroupWash;
            _logKind = kind == WashShotFoam ? "wash-foam" : (kind == WashShotRinse ? "wash-rinse" : "wash-done");
            _logDog = dog;
            bool ok = ShootPetShot(dog, shot);
            _hillsideGroupOverride = -1;
            return ok;
        }

        /// <summary>洗われている犬 (同期された洗いの保持、洗い場に呼ばれている、台から降りている途中)。撫でのバーストを止める。古民家の構成では常に false。</summary>
        bool WashingDog(int i)
        {
            if (!hillsideEventGroups || wanderControllers == null || i < 0 || i >= wanderControllers.Length) return false;
            KD_DogWanderController w = wanderControllers[i];
            return w != null && w.WashHeld;
        }

        /// <summary>撮影の記録 1 行 (Shoot から、1 枚につき 1 回)。</summary>
        void LogShot(int group, RenderTexture rt, bool captured)
        {
            int slot = -1;
            RenderTexture[] s = gallery != null ? gallery.slotTextures : null;
            if (s != null) for (int i = 0; i < s.Length; i++) if (s[i] == rt) { slot = i; break; }
            string g = group == HillsideGroupWalk ? "walk" : (group == HillsideGroupPet ? "pet" : (group == HillsideGroupPlay ? "play" : (group == HillsideGroupWash ? "wash" : "?")));
            // The kind's own category (walk-* / pet / wash-* / everything else = play). A different group means _hillsideGroupOverride was set
            // from outside (the photo harness's H6 / H9 send frisbee shots into the walk / pet slots): marked, so the log does not look stale
            string k = _logKind;
            int natural = k.StartsWith("walk") ? HillsideGroupWalk : (k == "pet" ? HillsideGroupPet : (k.StartsWith("wash") ? HillsideGroupWash : HillsideGroupPlay));
            Debug.Log("[KD_PhotoDirector] PHOTO " + (k.Length > 0 ? k : "other") + " group=" + g + " slot=" + slot + " dog=" + _logDog
                + (k.Length > 0 && natural != group ? " (group override)" : "") + (captured ? "" : " (capture refused)"));
        }
    }
}
