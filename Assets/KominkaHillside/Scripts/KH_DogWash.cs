using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside
{
    // IMP-DOG-025 (DES-DOG-006): one dog's wash state - the mask it is drawn with, the cells that are the truth,
    // and the strokes that change both. Contract: docs/plans/2026-09-30-dog-wash-prototype.md §2-§4 / §6.
    //
    // Two layers, kept apart on purpose:
    //   mask  = two RenderTextures (maskA / maskB, R dirt / G foam / B wet) swapped on every Blit. Every client
    //           draws every stroke into its own mask, so the band follows the tool without waiting for sync.
    //   cells = 16 x 16 UV cells x (dirt, foam, wet), the synced truth (1 byte each). The Owner of this object
    //           integrates the strokes into them; the clean test and the late-join / recovery repaint read them.
    // The mask is NEVER repainted from the cells on a normal receive (design §1-4: the washed band would come back).
    // Repaint (Restore pass) only on a joiner's first receive, an epoch change (reset), a lost RenderTexture, and
    // when the dog has dried out completely (the mask is then set to the all-zero cells before the shell goes off).
    //
    // Amount: every stroke is a capsule (a -> b, radius r) with a contact time `sec`. A point on a moving brush is
    // only under it for part of that time, so the amount given to every point inside the capsule is
    //     dwell = sec * 2r / (|b - a| + 2r)          (Dwell(); r and |b - a| in the same units)
    // A still brush (|b - a| = 0) gets the full time; a brush moving at speed v gives each point ~2r / v however the
    // path is cut into capsules. The local per-frame draw, the received 8 Hz segment and the cells all use Dwell(),
    // so the sender and the receivers wash at the same speed.
    //
    // Strokes: the holder's tool calls _LocalContact every frame (the point is converted to mesh space at once and
    // the history is kept in mesh space, so a moving leg does not bend the capsule); the segments go out at most
    // 8 Hz as NetStroke to All. The sender skips drawing its own echo but the Owner integrates the echo like any
    // other stroke, so there is one integration path for the truth. `amount` carries the contact time.
    //
    // Ownership: this object follows the dog root's Owner (checked every 2 s, one request per 6 s, only after the dog
    // root has been ours for two checks, so a transfer in flight or two clients briefly both owning the root do not
    // fight). Strokes never take ownership, so two people can wash the same dog.
    // The wash hold (synced washMs / washSeen, and since IMP-DOG-037 washBy = the playerId of the stroke that set it) is what
    // the gates and the wash station's call rule read; all three are written together by the Owner.
    //
    // ---- Operations and their results (design §1-2; IMP-DOG-026). The mask (FragBrush) and the cells (ApplyCell)
    // use the same formulas; only the weight differs: mask w = the shader's soft capsule falloff x clumps x part gate
    // (foam removal uses the wider wOut, whose outer edge is rinseReach x the noisy radius for the shower and 1.2 x for
    // the towel), cells w = 1 for every cell centre within (r + cellReach) of the capsule, x cellGain (the shower: the
    // foam part of the delta, with the dirt gate, also out to (r x rinseReach + cellReach), as wOut does on the mask).
    // s = Dwell(sec) x w. sat = clamp 0..1. D, F, W = dirt, foam, wet before; D', F', W' after.
    // Part gate: the sponge and the towel paint the hit part and its partNeighbors only (no foam through to the other
    // leg); the SHOWER has no part gate (2026-10-02, IMP-DOG-025: shoulder texels nearest to the neck / hip collider
    // were never rinsed by water aimed at the shoulder).
    //
    //  op                | delta (dD, dF, dW) per s                          | result
    //  sponge (foam)     | (0, spongeFoamRate, spongeWetRate)                | F' = sat(F + dF (1 - ease F))  ease = _FoamGrowEase
    //                    |                                                   | D' = D (foam never changes dirt; foam also grows on a clean dog)
    //                    |                                                   | W' = sat(W + dW)
    //  shower (rinse)    | (showerDirtRate, showerFoamRate, showerWetRate)   | F' = sat(F + dF)       every part (no part gate), dF out to rinseReach
    //                    |                                                   | Dn = sat(D + dD (1 + F rinseFoamBoost))   (water alone removes dirt; foam is faster)
    //                    |                                                   | g  = foamRinseGate x smoothstep(0.03, 0.2, F)
    //                    |                                                   | D' = sat(lerp(Dn, min(D F' / max(F, 1e-4), D), g))
    //                    |                                                   | W' = sat(W + dW)
    //  runoff (after a   | every runoffInterval until runoffSeconds after    | n  = rinsed neighbours of 8 (W >= 0.5 && F < 0.1; outside the islands do not count)
    //   shower stroke,   |   the last shower stroke; a = runoffRate x dt     | F' = max(0, F - a n / 8)            (only where F > 0; W' = W)
    //   every client)    |   (dt <= 0.25 s); mask: neighbours +-runoffStep   | D' = min(D, D F' / max(F, 1e-4))
    //                    |   texels; cells (Owner): the 8 grid neighbours,    | leftover foam next to a rinsed area flows away by itself (no new
    //                    |   a x runoffCellGain (1); a cell counts as rinsed |   synced variable / event: the Owner's cells are the truth)
    //                    |   only if a SHOWER stroke rinsed it (no chain:    | mask: an eroded wet texel counts, so the mask's front moves up to
    //                    |   one ring around the showered cells)            |   runoffStep texels per pass through wet texels
    //                    | also after a Restore that repainted (a joiner in  | a new Owner seeds the showered cells from the rinsed cells it holds
    //                    |   a window) and on a new Owner                    |   and continues the window
    //  towel (dry)       | (towelDirtRate, towelFoamRate, towelWetRate)      | as the shower with g = 0: D' = sat(D + dD (1 + F boost))
    //  dry (Decay, dt)   | fd = washing ? 0 : dt / foamDrySeconds            | F' = max(0, F - fd), W' = max(0, W - wd), D' = D
    //   (4 s / 2 s)      | wd = dt / wetDrySeconds                           | washing = _WashHold() || a stroke seen here within holdSeconds
    //  get dirty (Owner, | walk: legs +walkLegDirt per walkDirtMeters of    | D' = min(1, D + inc) per valid cell of the part (a full cell stays 1)
    //   every 2 s)       |   leashed / Fetch / disc movement                 | river: D' = D > floor ? max(floor, D - drop) : D   (floor = riverDirtFloor)
    //                    | zone: legs +zoneLegDirt, hip + chest +zoneBodyDirt| W' = min(1, W + riverWetPerSecond x water time) (legs; + hip / chest when swimming)
    //                    |   per zoneDirtSeconds inside a dirt zone          | none while washed (_WashHold), within dirtGraceSeconds of the last clean edge,
    //                    | river (WaterPose != 0): legs -riverLegDrop per    |   or within dirtStationRadius of a wash station (washStation or washStations[]; the river still rinses there)
    //                    |   riverDropSeconds, + wet                         | mask: the part's average change, as a whole-part Brush (see "Dirt on the mask")
    //                    |                                                   | a river drop alone never fires the clean edge (riverBlocksEdge until a wash stroke)
    //  reset (Owner)     | _DebugSetDirt(v): D = v on valid cells, F = W = 0 | epoch++ -> every client repaints once (Restore)
    //                    | _DebugClear(): D = F = W = 0                      | cleanArmed = (v >= dirtyDirtThreshold), arm set cleared
    //  repaint (Restore) | mask = cells (bilinear) with D x (0.5 + noise)    | only: first receive, epoch change, lost RT, long gap, dried out
    //  sync (Owner)      | D 2 bit, F 3 bit, W 3 bit per cell                | decoded on receive into the floats (a coarse copy)
    //
    // Clean edge (Owner, every 0.5 s while the cells change, else every 4 s (IMP-DOG-034 B1a), n = valid cells, avg over the valid cells):
    //   arm   : !armed && (avgD >= dirtyDirtThreshold || #(F >= dirtyFoamThreshold) >= max(1, ceil(armFoamCellFraction n)))
    //           -> the arm set = the cells with F >= dirtyFoamThreshold (cells reaching it later while armed join the set)
    //   clean :  armed && avgD < cleanDirtThreshold && avgF < cleanFoamThreshold && (set empty || avg F over the set < cleanFoamThreshold)
    //           -> cleanSerial++, disarm, set cleared
    //           -> quiet set = the cells still >= dirtyFoamThreshold at the edge; a quiet cell does not count toward the
    //              next arm until its foam is < cleanFoamThreshold (left-over foam of the same wash is not a new wash)
    // Foaming only the legs (>= the fraction) and rinsing them gives the edge; after it, re-arming needs that many
    // fresh cells at 0.3 again, so wobbling around 0.1 cannot repeat it (hysteresis 0.3 / 0.1, a count, the quiet set).
    //
    // Epoch: identifies the version only (equal or not, never ordered; FreshEpoch is random). A stroke is taken when
    // its epoch equals this client's or is 0 ("the sender holds no state yet": a joiner before its first receive),
    // checked before any budget is spent; anything else is dropped (a reset in flight costs a short visual gap).
    // Cells: only the Owner integrates, and syncs per sender the newest seq it holds (appliedIds / appliedSeqs). Every
    // client records the accepted strokes; a new Owner re-integrates the recorded ones newer than appliedSeqs.
    // Repaint keeps the own band: a joiner's own segments are logged; after its first repaint it draws the ones the
    // received cells do not hold (seq newer than appliedSeqs[own]) plus the unsent ones, in the Blit budget.
    // _ReleaseHold ends the hold explicitly (gather / recall / warp); only a stroke after it holds again.
    //
    // ---- Dirt on the mask (IMP-DOG-029). A normal receive never repaints (the washed band would come back), so a dirt
    // increase the Owner makes in the cells has to be drawn into every client's mask as well. It goes out as a NetStroke
    // with tool = ToolDirt (3): one event per 2 s tick at most, only when something changed, sent only by the Owner of
    // this object and accepted only from it. Arguments: part = 0 (unused), from = the dirt change of the legs 3..6, to =
    // of the parts 0, 1, 2, 8 (7 bit each, offset 64, unit DirtUnit, -63..63), amount = the wet change of the legs
    // (bits 0..9) and of hip + chest (bits 10..19), unit 0.001. Every client (the Owner at once, the others on receipt)
    // draws each part's change as ONE Brush pass: a capsule of zero length at the part's centre, large enough to cover
    // the whole part (_partReach), _AllowPart = that part only, _Delta = (dD, 0, dW). The Brush pass ADDS dD to every
    // texel of the part (x the shader's clump noise = the mud pattern) and leaves foam alone, so a washed band stays
    // lighter than its surroundings by exactly what it was washed: nothing is overwritten. The alternative (repainting
    // the part from the cells with Restore) was not taken: Restore writes every texel from the 16 x 16 cells, i.e. it
    // would replace the band with the coarse cell value (and it has no part gate at all).
    // The amount drawn is the part's AVERAGE cell change (quantised, the remainder carried to the next tick), so a
    // saturated part (cells at 1) draws nothing more. The mask clamps at 0..1 itself.
    // Which events a state holds (Codex review 3, 1-1): the synced dirtSeq is the newest dirt event the cells contain
    // (one sequence per object, continued by each Owner). Every client logs the accepted events; a Restore makes the mask
    // equal to the cells (= up to dirtSeq) and draws again only the logged events newer than dirtSeq; a received event is
    // drawn only when newer than what the mask holds. A new Owner integrates the logged events its cells lack.
    //
    // ---- Hand wash (IMP-DOG-039, docs/plans/2026-10-03-hand-wash.md; §13 overrides §3-§5). Two more kinds on the same
    // NetStroke (no new synced variable, no new event, the same wire format):
    //   bottle (4) : a KH_WashTool. One dose = _LocalContact(4, .., bottleSquirtSeconds) + _EndContact(4): a dot of foam only
    //                (bottleFoamRate, no wet). Received like every tool (HoldsTool).
    //   hand   (5) : _HandContact from KH_HandWash (the local VR player's free hands at a station's dog). Foam only (handFoamRate),
    //                and only where foam already is - the LATHER GATE, applied by whoever integrates it from what it holds:
    //                mask : _LatherGate in the Brush pass, gate = smoothstep(handFoamMin, +0.1, max foam of the texel and its
    //                       4 neighbours at handSpreadStep texels within handSpreadReach in 3D)
    //                cells: one gate per stroke from the max foam (before the update) of the cells it reaches (ApplyToCells)
    //                touchedFoam (that max >= handFoamMin) is what makes it a wash: an untouched hand stroke moves only the
    //                order (AcceptSender / SetApplied) - no hold, no washer, no effects, no edge, no dirt-count reset.
    //                Received: every client checks VR, the capsule length (handMaxSegment) and the station (washCalls);
    //                the Owner alone checks the sender's hands / head (each client sees another avatar differently).
    // Channels: the sender's history, open segments and send queue are per channel (sponge, shower, towel, bottle, left hand,
    // right hand); everything that is drawn, logged, recorded or sent carries the kind (ChannelTool). Receivers do not know
    // left from right, so hand draws are never merged while waiting.
    //
    // ---- Photos (IMP-WLD-011): the wash milestones for the local washer's own album (KH_DogWash.Photo.cs; local, nothing synced). Hooks
    // only at the existing change points: Stroke (own counted strokes), EvaluateState (every client), OnDeserialization (non-Owner) and
    // _RequestShake (Owner). The idle Update path is not touched.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public partial class KH_DogWash : UdonSharpBehaviour
    {
        public const int ToolSponge = 0;
        public const int ToolShower = 1;
        public const int ToolTowel = 2;
        public const int ToolDirt = 3;       // NetStroke only: the Owner's dirt change (IMP-DOG-029), never a held tool
        public const int ToolBottle = 4;     // IMP-DOG-039: the shampoo bottle (a KH_WashTool): one dose of foam, a dot
        public const int ToolDryer = 6;      // premium air: reduces wetness only
        public const int ToolHand = 5;       // IMP-DOG-039: the bare hands (KH_HandWash -> _HandContact), never a held tool
        public const int PartCount = 9;
        // IMP-DOG-039 (hand-wash design §13.4): the sender's local strokes run on channels, not kinds. Contact history, open
        // segments and the send queue are per channel; the draws, the logs, the records and the wire carry the kind.
        //   channel 0 sponge, 1 shower, 2 towel, 3 bottle, 4 left hand, 5 right hand  ->  kind 0, 1, 2, 4, 5, 5 (ChannelTool)
        // ToolDirt (3) is a kind, not a channel: channel 3 is the bottle.
        const int ChannelCount = 7;
        const int ChDryer = 6;
        const int ChBottle = 3;
        const int ChHandL = 4;               // + hand (0 = left, 1 = right)
        const int FoamKeyEffects = ChannelCount;   // FoamNear's cache slot for ContactEffects / received strokes
        const int OwnFoamMax = 16;           // the own-foam ring (§3.4 / §13.5)
        const int CellCount = 256;
        const int AllowSlots = 16;   // _AllowPart / _PartFoamLimit length in the shaders
        const int BoneSlots = 64;    // _BoneToPart length in the shaders
        const int PassBrush = 0;
        const int PassRestore = 1;
        const int PassDecay = 2;
        const int PassRunoff = 3;
        const int AllowAllKey = -4;          // _allowFor: every part allowed (the shower). -2 = nothing set yet, -3 = SetAllowOnly
        const float TowelFoamReach = 1.2f;   // wOut's outer edge for the towel (and the dirt draw): the value before rinseReach
        const float MaxRunoffStep = 0.25f;   // one runoff pass covers at most this much time (a hitch does not wipe a patch)
        const int PackMax = (1 << 30) - 1;   // 3 axes x 10 bit
        const int MaxAmountMs = 2000;
        const int QueueMax = 10;
        const int PendMax = 8;
        const int SenderSlots = 8;
        const int ArmWords = 8;              // 256 bits
        const int LogMax = 48;               // 32 sent + QueueMax queued + 3 open
        const int LogSentMax = 32;
        const int RecMax = 48;               // ~3 s of two washers at 8 Hz
        const int AppliedSlots = 8;
        const float MaxBlitSeconds = 0.25f;  // one Blit takes at most this much contact time; the rest waits

        [Header("Wiring (KH_DogWashSetupEditor)")]
        public int dogIndex = -1;
        public Transform dogRoot;
        public SkinnedMeshRenderer shellRenderer;
        public RenderTexture maskA;
        public RenderTexture maskB;
        public Material maskMaterial;
        [Tooltip("任意。描画しない (enabled=false) Renderer に maskMaterial を付けて配線すると、その .material (複製) を Blit に使い、アセットへ実行時の値を書かない。null なら maskMaterial へ直接書く")]
        public Renderer maskMaterialHolder;
        public Texture posMap;
        [Tooltip("16x16、読み書き可。実行時に Restore の前にセルの値を書く。画素 (x, y) = セル y*16+x (y は UV の v、下から)")]
        public Texture2D cellTex;
        public float meshToWorldScale = 1f;
        [Tooltip("全部の洗う道具。受け取ったストロークの送り主が、その種類の道具を持っているかを確かめる。null / 長さ 0 なら確かめない")]
        public KH_WashTool[] tools;

        // Scene-reference / data arrays: no program defaults (vrc-udonsharp §4). The builder allocates them.
        public Transform[] partBones;
        public int[] partOfCollider;
        public Collider[] partColliders;
        public Matrix4x4[] boneToMesh;
        [Tooltip("骨の番号 → 部位 ID (64)。マスクと殻の _BoneToPart へ Start で渡す (マテリアルの配列はアセットに保存されない)")]
        public float[] boneToPart;
        [Tooltip("部位ごとの隣接のビットマスク (長さ 9)。bit j = 部位 j へもブラシが効く。自分の部位は常に許可")]
        public int[] partNeighbors;
        public bool[] cellValid;
        public Vector3[] cellCenter;
        public int[] cellPart;
        [Tooltip("殻の _PartFoamLimit (16)。null なら頭 (7) 0.35、ほかは 1")]
        public float[] partFoamLimit;

        [Header("Effects (null is fine)")]
        [Tooltip("個人音量: 接触の音 3 本は「目標の音量 × この犬の localVoiceGain」を書く (KD_TreatPouch.ApplyDogVoiceSettings が gain と mute を決める)。null なら gain 1")]
        public KominkaDogRun.KD_DogWanderController voiceDog;
        [Tooltip("Emit で使う。Simulation Space = World")]
        public ParticleSystem hitSplash;
        public ParticleSystem foamPuff;
        public AudioSource contactFur;
        public AudioSource contactFoam;
        public AudioSource scrub;

        [Header("Prototype values (Play で決め直す)")]
        [Range(0f, 1f)] public float initialDirt = 0f;   // 決定: 2026-10-01 (IMP-DOG-034 C2-2)。犬はきれいな状態で始まる
        // Wash/11 writes KH_DogWashSetupEditor.WashTuning (spongeRadius, the sponge / shower rates, rinseReach, runoff*)
        public float spongeRadius = 0.11f;
        public float showerRadius = 0.07f;
        public float towelRadius = 0.10f;
        public float spongeFoamRate = 1.6f;
        public float spongeWetRate = 0.2f;
        public float showerFoamRate = -4f;
        public float showerDirtRate = -1f;
        [Tooltip("使っていない (マスクのシェーダーに上限が無いので、セルも同じ式にした 2026-09-30)")]
        public float showerDirtRateMax = -1.6f;
        public float showerWetRate = 1.2f;
        public float towelWetRate = -1.2f;
        public float towelFoamRate = -0.4f;
        public float towelDirtRate = -0.15f;
        [Tooltip("すすぎで汚れを落とす速さ = 基本 × (1 + 泡 × この値)。マスクとセルの両方に使う")]
        public float rinseFoamBoost = 2.2f;
        public float foamRinseGate = 1f;
        [Tooltip("シャワーが泡を消す範囲の外側の縁 = 縁のノイズ込みの半径 × この値 (マスクの _RinseReach)。セルも泡はブラシの半径 × この値 + cellReach まで。1.2 = 以前の固定値")]
        public float rinseReach = 1.7f;
        public float brushSoft = 0.35f;
        public float edgeNoise = 0.3f;
        [Tooltip("セルへの積算: ブラシの半径にこの距離 (m) を足した範囲のセルが変わる (セルは帯より粗い)")]
        public float cellReach = 0.05f;
        public float cellGain = 1f;
        public float cleanDirtThreshold = 0.08f;
        public float cleanFoamThreshold = 0.1f;
        public float dirtyDirtThreshold = 0.2f;
        public float dirtyFoamThreshold = 0.3f;
        [Tooltip("泡 dirtyFoamThreshold 以上のセルが、有効セルのこの割合以上になったら再洗浄の合図を構える (足だけでもよい)")]
        [Range(0f, 1f)] public float armFoamCellFraction = 0.15f;
        public float holdSeconds = 3f;
        public float foamDrySeconds = 120f;
        public float wetDrySeconds = 90f;
        public float syncInterval = 0.75f;
        [Tooltip("送る位置を詰める箱の半分の大きさ (m、部位の中心から)")]
        public float packHalfMeters = 0.5f;
        [Tooltip("送り主がこれより遠いストロークは受けない (m)")]
        public float maxWasherDistance = 8f;
        public int maxBlitsPerFrame = 4;
        public float contactVolume = 0.8f;
        public float scrubVolume = 0.7f;
        public float audioFadeSpeed = 6f;
        public float splashPerSecond = 60f;
        public float foamPuffPerSecond = 20f;

        [Header("Hand wash (IMP-DOG-039, docs/plans/2026-10-03-hand-wash.md。Wash/11 が WashTuning を書く)")]
        [Tooltip("ボトル 1 回分の泡の半径 (m)")]
        public float bottleRadius = 0.10f;
        [Tooltip("ボトルの泡 / s (1 回 = bottleSquirtSeconds 0.25 s)。濡れは足さない")]
        public float bottleFoamRate = 2.8f;
        [Tooltip("素手の泡立ての半径 (m)")]
        public float handRadius = 0.10f;
        [Tooltip("素手の泡 / s (泡立ての門を通った所だけ)")]
        public float handFoamRate = 1.2f;
        [Tooltip("泡立ての門: この泡より下の所には素手で泡が増えない (マスクとセル)。門 = smoothstep(min, min + 0.1, 泡)")]
        public float handFoamMin = 0.12f;
        [Tooltip("マスクの門で隣を見る距離 (テクセル)")]
        public float handSpreadStep = 2f;
        [Tooltip("マスクの門で数える隣の 3D の上限 (m)。これより遠い隣 (UV の島の境の向こう) は数えない")]
        public float handSpreadReach = 0.015f;
        [Tooltip("送り手: 近いセルの泡がこれ以上なら泡立てを送る (自分の最近の泡の輪でも可)")]
        public float handStartFoam = 0.1f;
        [Tooltip("送り手: 手のワールドの速さがこれ未満 (置いた手) は泡立てない (m/s)")]
        public float handMinSpeed = 0.05f;
        [Tooltip("送り手: 手のワールドの速さがこれ超 (追跡の飛び) は泡立てない (m/s)")]
        public float handMaxSpeed = 3f;
        [Tooltip("Owner の受信検査: カプセルの両端が送り主のどちらかの手からこの距離以内 (m)")]
        public float handVerifyRadius = 0.5f;
        [Tooltip("Owner の受信検査: カプセルの両端が送り主の頭からこの距離以内 (m)")]
        public float handReachMeters = 2f;
        [Tooltip("送り手: 自分のボトル・スポンジの泡をこの秒数だけ覚え、受け取ったセルの代わりに泡立ての判定に使う")]
        public float ownFoamSeconds = 3f;
        [Tooltip("全員の受信検査: 素手のカプセルの長さの上限 (m)。送り手はこの手前で区間を切る")]
        public float handMaxSegment = 0.35f;
        [Tooltip("全洗い場 (Wash/11 が配線)。素手のストロークはこの犬がどれかに留められ着いている時だけ受ける。null / 空なら他人の素手は受けない (自分の手は描く)")]
        public KH_WashStationCall[] washCalls;
        [Tooltip("読み取り専用 (ハーネス): この端末で送った泡立てのストローク数")]
        public int handStrokesIssued;
        [Tooltip("読み取り専用 (ハーネス、Owner): 受けたが泡に触れていなかった素手のストローク数 (保持・演出を動かさない)")]
        public int handStrokesInert;
        [Tooltip("読み取り専用 (ハーネス): 素手の検査で落とした受信ストローク数")]
        public int handStrokesRejected;

        [Header("Runoff (2026-10-02: 流した所の隣に残った泡は自然に流れる。Wash/11 が WashTuning を書く)")]
        [Tooltip("最後のシャワーのストロークからこの秒数、流れる処理を続ける。0 以下で無効")]
        public float runoffSeconds = 1.5f;
        [Tooltip("流れる処理の間隔 (s)。1 回 = マスクの Blit 1 回 (Blit の予算に数える) + Owner のセル")]
        public float runoffInterval = 0.1f;
        [Tooltip("8 近傍すべてが流された時に 1 秒で減る泡。0 以下で無効")]
        public float runoffRate = 3f;
        [Tooltip("マスクで近傍を見る距離 (テクセル、整数に丸める)")]
        public float runoffStep = 2f;
        [Tooltip("セルの流れる量 = マスクの量 × この値。セルはシャワーで流したセルの隣 1 周だけが減る (連鎖しない) ので、1 = 隣のセルがマスクと同じ速さ。大きくすると真実がマスクより先に清潔になる")]
        public float runoffCellGain = 1f;

        [Header("Getting dirty (IMP-DOG-029, wired by Wash/20 KH_DirtZoneEditor; prototype values)")]
        [Tooltip("犬の Fetch (IsIdle / WaterPose)。null なら Fetch 中の移動と川を見ない")]
        public KominkaDogRun.KD_DogFetchController dirtFetcher;
        [Tooltip("リード (walkers[dogIndex] >= 0 = 接続中)。null ならリードの移動を見ない")]
        public KH_LeashManager dirtLeashes;
        [Tooltip("ディスク (IsEngaged)。null ならディスクの移動を見ない")]
        public KH_FrisbeeCatch dirtFrisbee;
        [Tooltip("洗い場 (KH_WashStation)。ここから dirtStationRadius 以内では汚れない。null なら判定しない")]
        public Transform washStation;
        [Tooltip("道中の洗い場も含む全洗い場 (IMP-ENV-008、Wash/11 が書く)。どれかから dirtStationRadius 以内では汚れない。null / 空なら washStation だけ")]
        public Transform[] washStations;
        // Scene-reference arrays: no program defaults (vrc-udonsharp §4). Wash/20 allocates them.
        [Tooltip("泥の地点。球 = 中心から lossyScale.x / 2 以内、箱 (dirtZoneBox[i]) = Transform のローカルの ±0.5 の立方体の中")]
        public Transform[] dirtZones;
        public bool[] dirtZoneBox;
        public bool dirtEnabled = true;
        [Tooltip("リード接続中・Fetch 中・ディスク中にこの距離 (m) を進むと、足が walkLegDirt 汚れる")]
        public float walkDirtMeters = 2700f;   // Wash/11 writes KH_DogWashSetupEditor.DirtTuning
        public float walkLegDirt = 0.5f;
        [Tooltip("泥の地点にこの秒数いると、足が zoneLegDirt、腰と胸が zoneBodyDirt 汚れる")]
        public float zoneDirtSeconds = 100f;
        public float zoneLegDirt = 0.5f;
        public float zoneBodyDirt = 0.2f;
        [Tooltip("水の中 (WaterPose != 0) にこの秒数いると、足の汚れが riverLegDrop 落ちる。riverDirtFloor より下へは落とさない (川だけで清潔にしない)")]
        public float riverDropSeconds = 50f;
        public float riverLegDrop = 0.3f;
        public float riverDirtFloor = 0.15f;
        [Tooltip("水の中で足 (泳ぐと腰と胸も) が 1 秒あたりに濡れる量")]
        public float riverWetPerSecond = 0.1f;
        [Tooltip("清潔の合図 (cleanSerial) からこの秒数は汚れない")]
        public float dirtGraceSeconds = 300f;
        public float dirtStationRadius = 6f;
        [Tooltip("汚れの増減をセルへ入れて配る間隔 (s)。増減が無い回は送らない")]
        public float dirtApplyInterval = 20f;   // 決定: 2026-10-01 (IMP-DOG-034 C2-1)。散歩中は数値だけ積み、20 s ごとにセル・マスクへ
        [Tooltip("部位全体を覆うブラシの半径 = 部位の中心から最も遠いセルまで × この値 (セルの中心より外の毛も覆う)")]
        public float dirtReachScale = 1.6f;
        [Tooltip("これより速い位置の変化はワープとして距離に数えない (m/s)")]
        public float dirtMaxSpeed = 12f;
        [Tooltip("汚れは足元から (決定 2026-09-30 PCVR): 地面 (最も低いセル) からの高さ (m) で増え方の重みを落とす。この高さまで 1")]
        public float dirtHeightFull = 0.08f;
        [Tooltip("ここまでに 1 → dirtMidWeight")]
        public float dirtHeightMid = 0.25f;
        [Tooltip("ここまでに dirtMidWeight → dirtTopWeight (その上は dirtTopWeight)")]
        public float dirtHeightTop = 0.45f;
        public float dirtMidWeight = 0.35f;
        public float dirtTopWeight = 0.05f;

        // ---- Synced (Owner of this object writes; the Owner follows the dog root) ----
        // Per cell: dirt 2 bit | foam 3 bit << 2 | wet 3 bit << 5 (design §1-4).
        [UdonSynced] private byte[] cells = new byte[CellCount];
        [UdonSynced] private int epoch;              // 0 = never initialised; bumped by every whole-dog reset
        // Last accepted stroke, server milliseconds. Compared by int subtraction (wraps correctly, negative server
        // times are fine: the same convention as KD_AccessoryBox / KH_LeashManager.Travel). washSeen says whether
        // there was a stroke at all, so 0 is not a magic "never".
        [UdonSynced] private int washMs;
        [UdonSynced] private bool washSeen;
        // IMP-DOG-037 (review of 8faf572): the playerId of the stroke that set washMs (-1 = none). Written by the Owner in the same
        // place as washMs, so every client - a late joiner and a new Owner too - knows who holds the dog with the same synced
        // hold. Read by KH_WashStationCall._Availability ("being washed by someone else") through _WashHolderId. 4 bytes.
        // (_lastWasherId stays the local "last stroke seen here", for the gaze; it is not synced and is not restored.)
        [UdonSynced] private int washBy = -1;
        [UdonSynced] private int cleanSerial;
        // Server milliseconds of the last clean edge (cleanSerial++): the dirt grace (IMP-DOG-029) carries over an Owner
        // change. Meaningful only when cleanSerial > 0.
        [UdonSynced] private int cleanMs;
        // The Owner's shake (IMP-DOG-028): _RequestShake bumps it and syncs; the others play the shake on the edge.
        [UdonSynced] private int shakeSerial;
        // IMP-DOG-049 段 B: the Owner's water play (the hose / the water pistol, design 2026-10-11 §3.3). The shakeSerial pattern in
        // one packed int: (serial << 6) | (part << 2) | kind - kind 1 = hose play, 2 = pistol hop, part = the hit part (0..8, the side
        // and the gaze). _RequestWaterPlay bumps the serial and syncs; every client starts the reaction on the edge (the dog's
        // WaterPlayStand); a joiner's first receive is only the baseline (never replayed). The wash-effect table is not touched (Q7).
        [UdonSynced] private int waterPlay;
        // IMP-DOG-029 (Codex review 3, 1-1): the newest dirt event (NetStroke tool 3) whose change these cells hold. One
        // sequence for the object, continued by every Owner (the next event is dirtSeq + 1), so a receiver can tell which
        // dirt events a received / repainted state already contains.
        [UdonSynced] private int dirtSeq;
        // (review 3, 1-2) a river drop changed the dirt since the last accepted wash stroke: the clean edge waits for a
        // stroke. Synced so a new Owner keeps holding the edge back (the 2-bit dirt rounds the floor 0.15 down to 0).
        [UdonSynced] private bool riverBlocksEdge;
        [UdonSynced] private bool cleanArmed;        // became dirty since the last "clean"; one edge per wash
        // The arm set (bit c of armBits[c >> 5]): the cells that were foamed while armed; "clean" waits for them.
        // Synced so a new Owner keeps judging the same place (32 bytes).
        [UdonSynced] private int[] armBits = new int[ArmWords];
        // Cells that were still foamy (>= dirtyFoamThreshold) when the last clean edge fired. They do not count toward
        // the next arm until they have dropped below cleanFoamThreshold (the rest of the same wash is not a new wash).
        [UdonSynced] private int[] quietBits = new int[ArmWords];
        // Which strokes the cells hold: per sender (playerId, 0 = free slot), the newest sequence number integrated.
        // A new Owner re-integrates its recorded strokes that are newer than this (the cells it received are older
        // than the strokes it has seen), and a joiner draws on top of its first repaint only its own segments newer
        // than this. 8 x 2 ints.
        [UdonSynced] private int[] appliedIds = new int[AppliedSlots];
        [UdonSynced] private int[] appliedSeqs = new int[AppliedSlots];

        // ---- Local ----
        private bool _ready;
        private Material _shellMat;
        private Material _maskMat;
        private RenderTexture _cur;
        private RenderTexture _next;
        private float[] _dirt;
        private float[] _foam;
        private float[] _wet;
        private Color[] _cellColors;
        private int[] _partCellStart;   // PartCount + 1
        private int[] _partCells;
        private Vector3[] _partCenter;
        private Matrix4x4[] _meshToBone;
        private Vector3[] _colliderMeshCenter;
        private bool _cellsOk;
        private float[] _allow = new float[AllowSlots];
        private int _allowFor = -2;
        private bool _latherOn;          // IMP-DOG-039: _LatherGate as last written (z = 1); written only when it changes
        private Vector4 _latherVec;
        private float _foamGrowEase = 0.7f;   // read from the mask material in Start (the shader's value is the truth)

        private bool _gotFirst;          // this client holds a state (seeded it as Owner, or received it)
        private int _appliedEpoch;
        private bool _restorePending;
        private bool _syncDue;
        private float _nextSync;
        private float _nextOwnerCheck;
        private float _nextEval;
        private float _nextDecay;
        private float _nextRtCheck;
        private float _lastRealtime;
        private bool _anyFoamOrWet;
        private bool _cellsEmpty;
        private float _lastActivity = -1000f;  // last stroke, or last time the cells had foam / wet
        private bool _shellOn = true;
        private float _avgDirt;
        private float _avgFoam;
        private float _avgWet;

        // ---- IMP-DOG-034 B1: lower-rate monitoring (user policy 2026-09-30: lightness over realtime; washing needs little) ----
        // B1a: EvaluateState loops the cells only when they may have changed since the last one. Every writer of _dirt /
        // _foam / _wet sets _cellsDirty (InitialCells, ApplyToCells, _DebugSetDirt / _DebugClear, OnDeserialization, Decay,
        // ApplyDirt, AddPartCells, RunoffCells, _DebugCellsChanged); a replaced array (the harness's SetProgramVariable) counts as well.
        // A missed writer degrades to "late" (SafeEvalInterval), never "never". The time-only part (shell off) runs on the
        // 0.5 s schedule without the loop (EvaluateTime).
        private bool _cellsDirty = true;
        private float _nextSafeEval;
        private float[] _evDirt;
        private float[] _evFoam;
        private float[] _evWet;
        private int[] _validCells;          // the cells EvaluateState counts (cellValid, or all 256 when the tables are missing)
        // B1a / B1c / B1d / B1e: this dog's phase (0..1) on every fixed-period grid, so six dogs do not share a frame.
        private float _phase;
        // B1e: an idle Update returns after a cheap check and does its periodic work on a IdleInterval grid. A stroke keeps
        // it per-frame until _busyUntil; a network event / restore / sync request wakes it for the next frame (Wake).
        private float _idleNext;
        private float _busyUntil = -1f;
        // B1c: the last stroke / foam / wet (the mask may hold foam or wet until a full drying time after it). Unlike
        // _lastActivity, a dirt-only change does not extend it, so a walking dog whose dirt grows does not keep Decay
        // (256 cells + Blit) running. _lastActivity keeps its meaning for the shell switch.
        private float _lastWetActivity = -1000f;
        // B1d: the main station is also in washStations (checked against the array reference, so a replaced array is re-checked)
        private Transform[] _stationsRef;
        private bool _mainInList;
        private float _dirtNextSample;
        const float EvalInterval = 0.5f;
        const float SafeEvalInterval = 4f;
        const float DecayInterval = 4f;     // B1c: was 2 s with Decay(2f); Decay(elapsed) keeps the same rate
        const float DecayWashInterval = 2f; // B1c: while washing (and the grid the 4 s runs sit on)
        private float _lastDecayAt;
        const float IdleInterval = 0.2f;    // B1e: 5 Hz
        const float BusyHold = 1f;          // B1e: per-frame this long after a stroke (segments close in SegmentGap, audio fades)

        private int _lastStrokePart = -1;
        private float _lastStrokeTime = -100f;
        private int _lastWasherId = -1;

        // ---- IMP-DOG-049 段 B: water play (design 2026-10-11 §3.3, Q10-Q12). THE one table (trial values until Play, Q12);
        // KD_DogWanderController.HillsideWash.cs reads it, Wash/12 prints it ("water play table"). ----
        /// <summary>Parts whose hits start a reaction (bit = part id): 1 Chest, 2 Neck, 3 FrontL, 4 FrontR, 7 Head
        /// ("頭・前足・胸のあたり"; the neck sits between head and chest).</summary>
        public const int WaterPlayParts = (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4) | (1 << 7);
        public const float WaterPlayHoseFirst = 0.5f;     // hose: continuous hits this long before the first play
        public const float WaterPlayHoseSpacing = 5f;     // hose: then at most one play per this while the hits continue
        public const float WaterPlayHoseEnd = 0.5f;       // hose: a gap this long breaks the run; the play ends this long after the water stops
        public const float WaterPlayPistolSpacing = 3f;   // pistol: every hit is a candidate, the same dog at most once per this
        public const float WaterPlaySeconds = 1.5f;       // one hose play (paw + snapping)
        public const float WaterPlayHopMinSeconds = 1f;   // pistol hop = the dog's Jump clip length, clamped to 1..1.5 s
        public const float WaterPlayHopMaxSeconds = 1.5f;
        public const float WaterPlayHopRatio = 0.3f;      // hop height = this x the standing head height (headAnchor above the root)
        public const float WaterPlayHopMin = 0.05f;       // m
        public const float WaterPlayHopMax = 0.2f;        // m
        public const float WaterPlayJawDegrees = 20f;     // hose: jaw snap (パクパク) peak, chin down
        public const float WaterPlayJawHz = 3f;
        public const float WaterPlayGazeOut = 0.4f;       // the head looks at the hit point moved this far toward the washer (the stream)
        public const float WaterPlayVoiceVolume = 0.5f;   // one short bark (barkSource, pitch 1.2) at the start; no new AudioSource
        // Every client, from the accepted strokes (ReceiveStroke; the own echo too). Hits of a hold-mode tool (the hose) or a shot
        // tool (the pistol) on a WaterPlayParts part only; any shower-kind stroke keeps "water on" for the hose play's end.
        private float _waterOnAt = -1000f;     // last accepted shower-kind stroke on this dog (any tool, any part)
        private float _hoseHitAt = -1000f;     // last hose hit on a reacting part
        private float _hoseRunStart = -1000f;  // first hit of the current hose run (gaps < WaterPlayHoseEnd)
        private float _pistolHitAt = -1000f;
        private int _pistolHitSeq;             // +1 per pistol hit (several per shot: one per received segment)
        private float _waterHitAt = -1000f;    // last hose or pistol hit (either)
        private int _waterHitPart = -1;
        private Vector3 _waterHitWorld;
        private float _ownWaterNoteAt = -1000f;   // the own strokes' water-hit record, at most 10 Hz per part (Stroke)
        private int _ownWaterNotePart = -1;

        // Ownership follow (FollowDogOwner): consecutive checks the dog root was ours while this was not, and the
        // time of the last request.
        private int _dogOwnerStreak;
        private float _ownerAskAt = -100f;

        // Joiner's log: the own segments sent before the first receive (seq = the number they went out with). At the
        // first receive it becomes the replay list: the logged segments the received cells do not hold (seq newer
        // than appliedSeqs[own]) plus the queued and open segments (not sent yet), drawn after the Restore within the
        // normal Blit budget, over as many frames as it takes (_rpIndex / _rpLeft).
        private int _lgCount;
        private int[] _lgTool = new int[LogMax];
        private int[] _lgPart = new int[LogMax];
        private int[] _lgSeq = new int[LogMax];
        private Vector3[] _lgA = new Vector3[LogMax];
        private Vector3[] _lgB = new Vector3[LogMax];
        private float[] _lgRad = new float[LogMax];
        private float[] _lgSec = new float[LogMax];
        private bool _replayPending;     // the list is built; start after the next Restore
        private bool _replaying;
        private int _rpIndex;
        private float _rpLeft = -1f;

        // Every client: the accepted current strokes of the last seconds (sender, seq, epoch, capsule). Only the Owner
        // integrates strokes into the cells; a client that becomes the Owner re-integrates from here what the cells it
        // received do not hold yet (appliedSeqs).
        private int _rcCount;
        private int[] _rcSender = new int[RecMax];
        private int[] _rcSeq = new int[RecMax];
        private int[] _rcEpoch = new int[RecMax];
        private int[] _rcTool = new int[RecMax];
        private int[] _rcPart = new int[RecMax];
        private Vector3[] _rcA = new Vector3[RecMax];
        private Vector3[] _rcB = new Vector3[RecMax];
        private float[] _rcRad = new float[RecMax];
        private float[] _rcSec = new float[RecMax];
        private float[] _apAt = new float[AppliedSlots];   // Owner-local: last use of each applied slot (eviction)

        // _ReleaseHold: the hold ends now; strokes up to this point (per sender slot: the accepted seq, plus those
        // arriving within ReleaseGrace, i.e. in flight) do not start it again.
        private int _releaseMs;
        private bool _released;
        private bool _dryFinished;       // Decay has made its final clearing pass since the last foam / wet / stroke
        private float _releaseTime = -1000f;
        private bool[] _relValid = new bool[SenderSlots];
        private int[] _relSeq = new int[SenderSlots];
        private int _acceptSlot = -1;     // AcceptSender's slot of the last accepted stroke
        private float _acceptPrevLast;    // that slot's _sndLast before AcceptSender moved it (an inert hand stroke puts it back)
        const float ReleaseGrace = 0.6f;

        // Harness read-outs: Restore passes run / replay entries drawn / strokes re-integrated at a handover.
        private int _restoreCount;
        private int _replayCount;
        private int _reintCount;

        // Blit budget (per frame, every pass counts) and the draws that did not fit, in order, never mixed across
        // tool / part.
        private int _blitFrame = -1;
        private int _blitCount;
        private int _pdCount;
        private int[] _pdTool = new int[PendMax];
        private int[] _pdPart = new int[PendMax];
        private Vector3[] _pdA = new Vector3[PendMax];
        private Vector3[] _pdB = new Vector3[PendMax];
        private float[] _pdRad = new float[PendMax];
        private float[] _pdSec = new float[PendMax];

        // Runoff (2026-10-02): active from an accepted shower stroke until runoffSeconds after the last one. _runoffLast is
        // the time the last pass covered up to (its elapsed is the next pass's amount); _rinsed is the cells' snapshot.
        private bool _runoffOn;
        private float _runoffUntil;
        private float _runoffNext;
        private float _runoffLast;
        private bool[] _rinsed;
        // Owner-local (round 2): the cells a SHOWER stroke left rinsed (wet >= 0.5, foam < 0.1 after ApplyToCells, inner reach).
        // Only these count as rinsed neighbours in RunoffCells: a cell eroded by the runoff never becomes one, so the erosion
        // reaches one ring of cells around the showered ones and does not chain across the grid (the mask's front moves
        // <= runoffStep texels per pass). Cleared for a cell a sponge stroke foams, and for all on a reset (DropInFlight).
        private bool[] _cellShowered;
        // Harness read-outs: mask passes run / cell passes run (Owner)
        private int _runoffPasses;
        private int _runoffCellPasses;

        // Contact history per channel (mesh space, converted at the frame of contact). IMP-DOG-039: channels, not kinds.
        private bool[] _hValid = new bool[ChannelCount];
        private int[] _hCollider = new int[ChannelCount];
        private Vector3[] _hMesh = new Vector3[ChannelCount];
        private float[] _hTime = new float[ChannelCount];
        // IMP-DOG-039 §13.5: per hand, the last world contact point and its time (the hand's world speed)
        private bool[] _hwValid = new bool[2];
        private Vector3[] _hwPos = new Vector3[2];
        private float[] _hwTime = new float[2];
        // IMP-DOG-039 §13.5: the own-foam ring - the local player's own bottle / sponge foam (part, mesh point, radius in mesh
        // units, Time.time). Never fed or extended by hand strokes; expires after ownFoamSeconds; the own shower clears entries.
        private int[] _ofPart = new int[OwnFoamMax];
        private Vector3[] _ofPos = new Vector3[OwnFoamMax];
        private float[] _ofRad = new float[OwnFoamMax];
        private float[] _ofAt = new float[OwnFoamMax];
        private int _ofNext;

        // Outgoing segments: one open slot per channel, closed into a small queue, sent at most 8 Hz.
        private bool[] _segOpen = new bool[ChannelCount];
        private int[] _segPart = new int[ChannelCount];
        private Vector3[] _segFrom = new Vector3[ChannelCount];
        private Vector3[] _segTo = new Vector3[ChannelCount];
        private float[] _segSec = new float[ChannelCount];
        private float[] _segRad = new float[ChannelCount];
        private float[] _segPath = new float[ChannelCount];
        private float[] _segStart = new float[ChannelCount];
        private float[] _segLast = new float[ChannelCount];
        private bool[] _segCarry = new bool[ChannelCount];
        private int[] _qCh = new int[QueueMax];     // the channel (ChannelTool gives the kind when it is sent / logged)
        private int[] _qPart = new int[QueueMax];
        private int[] _qFrom = new int[QueueMax];
        private int[] _qTo = new int[QueueMax];
        private int[] _qMs = new int[QueueMax];
        private int[] _qRmm = new int[QueueMax];
        private int _qCount;
        private int _seq;
        private float _nextSend;
        const float SendInterval = 0.125f;   // 8 Hz, matches maxEventsPerSecond below
        const float SegmentGap = 0.25f;      // no contact this long = the segment ends (the tool also ends it)
        const float SegmentMaxAge = 0.5f;

        // Incoming: per sender, the last sequence number and a contact-time budget (two hands = two tools at once).
        private int[] _sndId = new int[SenderSlots];
        private int[] _sndSeq = new int[SenderSlots];
        private float[] _sndTokens = new float[SenderSlots];
        private float[] _sndAt = new float[SenderSlots];
        private float[] _sndLast = new float[SenderSlots];   // Time.time of the sender's last accepted stroke
        private float _localStrokeAt = -1e9f;                 // this client's own last stroke (drawn at once)
        const float BudgetRate = 2.2f;       // contact seconds per second a sender may give (2 tools + margin)
        const float BudgetCap = 3f;

        // Contact effects
        private float _furTarget;
        private float _foamTarget;
        private float _showerUntil;
        private float _scrubUntil;
        private float _foamOnlyUntil;    // IMP-DOG-039: the foam loop alone (bottle dose, lathering hand), never mixed with the shower's
        private float _splashAcc;
        private float _puffAcc;
        // FoamNear's 0.1 s cache, per key (IMP-DOG-039 §13.5: the two hands touching one part at two places must not share it):
        // keys 0..ChannelCount-1 = the local channels, FoamKeyEffects = the effects / received strokes.
        private int[] _fnPart = new int[ChannelCount + 1];
        private float[] _fnAt = new float[ChannelCount + 1];
        private float[] _fnValue = new float[ChannelCount + 1];

        // ---- Getting dirty (IMP-DOG-029) ----
        const float DirtUnit = 0.002f;        // one step of a sent dirt change (7 bit signed: +-0.126 per event)
        const int DirtQMax = 63;
        const float DirtWetUnit = 0.001f;     // one step of a sent wet change (10 bit)
        const int DirtWetQMax = 1023;
        // IMP-DOG-034 B1d: 4 Hz -> 1 Hz. Every accumulation is by elapsed time / distance, so the dirt per second is unchanged
        // (a curved walk is measured by a slightly shorter chord: negligible at walkDirtMeters 900 m).
        const float DirtSampleInterval = 1f;
        private float[] _partReach;           // mesh units: the part's centre to its farthest cell centre
        // Height above the ground (m) of each cell centre, and its dirt weight (DirtHeightWeight). The ground is the lowest
        // valid cell centre; the bind-pose mesh is Z-up (position map / cellCenter z; KH_DogWashBake AABB z from 0).
        private float[] _cellHeight;
        private float[] _cellW;
        private float _groundZ;               // mesh units
        // Owner: what happened since the last apply (metres counted, seconds in a zone / in the water / swimming)
        private float _dirtSampleAt = -1f;
        private float _dirtNextApply;
        private float _dirtIvUsed;   // the dirtApplyInterval the current deadline was computed with
        private Vector3 _dirtPrevPos;
        private bool _dirtPrevValid;
        private float _walkAcc;
        private float _zoneAcc;
        private float _waterAcc;
        private float _swimAcc;
        // Owner: the part's average change not sent yet (quantisation remainder; a saturated part gains nothing)
        private float[] _dirtCarry = new float[PartCount];
        private float _wetCarryLeg;
        private float _wetCarryBody;
        private bool _dirtOutPending;
        private int _dirtOutFrom;
        private int _dirtOutTo;
        private int _dirtOutAmount;
        private int _dirtOutSeq;
        // Every client: the dirt events received lately (ring, oldest out). After a Restore the ones newer than the
        // cells' dirtSeq are drawn again; a new Owner integrates them into its cells (review 3, 1-1).
        const int DirtLogMax = 8;
        private int _dlCount;
        private int[] _dlSeq = new int[DirtLogMax];
        private int[] _dlEpoch = new int[DirtLogMax];
        private int[] _dlFrom = new int[DirtLogMax];
        private int[] _dlTo = new int[DirtLogMax];
        private int[] _dlAmount = new int[DirtLogMax];
        private int _dirtMaskSeq;        // the mask holds the dirt events up to this (the cells' dirtSeq at the last Restore)
        private bool _dirtLastValid;
        private int _dirtLastSeq;        // the newest accepted dirt event
        // Owner tracking for the dirt check: the current Owner and, for a short time after a change, the previous one
        // (its events still in flight are accepted, so the masks match the new Owner's re-integrated cells).
        private int _ownerId = -1;
        private int _prevOwnerId = -1;
        private float _prevOwnerUntil = -1f;
        const float PrevOwnerGrace = 5f;
        // 2026-10-07 audit RC-7 (b): the cells' dirtSeq right after this client took over (OnOwnershipTransferred, after
        // ReintegrateDirt). A previous Owner's event newer than this, arriving within PrevOwnerGrace, goes into the cells.
        private int _dirtTakeoverSeq;
        // 2026-10-07 audit RC-7 (a): another player's tool strokes that arrived before this client saw that player hold a tool of
        // that kind (a pick-up / hand-over: the stroke can beat both the Pickup's holder and the ownership). Kept up to
        // KH_WashTool.HoldGrace and taken, in arrival order, as soon as HoldsTool says yes; older or overflowing ones are dropped.
        const int DeferMax = 8;
        private int _dfCount;
        private int[] _dfSender = new int[DeferMax];
        private int[] _dfTool = new int[DeferMax];
        private int[] _dfPart = new int[DeferMax];
        private int[] _dfFrom = new int[DeferMax];
        private int[] _dfTo = new int[DeferMax];
        private int[] _dfMs = new int[DeferMax];
        private int[] _dfRmm = new int[DeferMax];
        private int[] _dfSeq = new int[DeferMax];
        private int[] _dfEpoch = new int[DeferMax];
        private float[] _dfAt = new float[DeferMax];
        private int _strokesDeferred, _strokesDeferRetried;   // harness read-outs (deferred / re-run from the queue; a re-run may still be refused)
        // 2026-10-07 audit RC-7 (b), Codex review A-2: at a takeover the new Owner's numbering jumps ahead of what the cells held, so
        // its first events do not share a number with a previous Owner's event still in flight (PrevOwnerGrace; a few at most) -
        // a third client keeps the first of two equal numbers and would have mixed them up. SeqNewer is a window, so a gap is free.
        // Limit (Codex review 2): a second transfer before the first new Owner's seq reached the next one starts from the same
        // base and can reuse the gap; collision-free numbering would need the sender / Owner generation in the event id (Tier 3).
        const int TakeoverSeqGap = 16;
        const int DirtDrawsPerFrame = 2;  // whole-part Brushes per dog per frame (six dogs at once stay bounded)
        // Every client: dirt / wet changes waiting to be drawn (the Owner's own, or received), one Brush per part
        private float[] _dirtDrawPend = new float[PartCount];
        private float[] _wetDrawPend = new float[PartCount];
        private bool _anyDirtDraw;
        // Harness read-outs: totals counted (m / s / s), apply ticks, events sent, Brush draws, the last block reason
        // (0 none, 1 disabled, 2 washed, 3 grace, 4 station, 5 water)
        private float _dirtWalkTotal;
        private float _dirtZoneTotal;
        private float _dirtWaterTotal;
        private int _dirtTicks;
        private int _dirtEvents;
        private int _dirtDraws;
        private int _dirtBlock;

        // Shader property IDs
        private int _idMask;
        private int _idPosMap;
        private int _idBoneToPart;
        private int _idPartFoamLimit;
        private int _idBrushA;
        private int _idBrushB;
        private int _idBrushRadius;
        private int _idBrushSoft;
        private int _idDelta;
        private int _idRinseFoamBoost;
        private int _idFoamRinseGate;
        private int _idAllowPart;
        private int _idEdgeNoise;
        private int _idCellTex;
        private int _idDecay;
        private int _idFoamGrowEase;
        private int _idDirtHeightFade;
        private int _idDirtHeightWeight;
        private int _idRinseReach;
        private int _idRunoffStep;
        private int _idRunoffAmount;
        private int _idLatherGate;

        void Start()
        {
            _idMask = VRCShader.PropertyToID("_Mask");
            _idPosMap = VRCShader.PropertyToID("_PosMap");
            _idBoneToPart = VRCShader.PropertyToID("_BoneToPart");
            _idPartFoamLimit = VRCShader.PropertyToID("_PartFoamLimit");
            _idBrushA = VRCShader.PropertyToID("_BrushA");
            _idBrushB = VRCShader.PropertyToID("_BrushB");
            _idBrushRadius = VRCShader.PropertyToID("_BrushRadius");
            _idBrushSoft = VRCShader.PropertyToID("_BrushSoft");
            _idDelta = VRCShader.PropertyToID("_Delta");
            _idRinseFoamBoost = VRCShader.PropertyToID("_RinseFoamBoost");
            _idFoamRinseGate = VRCShader.PropertyToID("_FoamRinseGate");
            _idAllowPart = VRCShader.PropertyToID("_AllowPart");
            _idEdgeNoise = VRCShader.PropertyToID("_EdgeNoise");
            _idCellTex = VRCShader.PropertyToID("_CellTex");
            _idDecay = VRCShader.PropertyToID("_Decay");
            _idFoamGrowEase = VRCShader.PropertyToID("_FoamGrowEase");
            _idDirtHeightFade = VRCShader.PropertyToID("_DirtHeightFade");
            _idDirtHeightWeight = VRCShader.PropertyToID("_DirtHeightWeight");
            _idRinseReach = VRCShader.PropertyToID("_RinseReach");
            _idRunoffStep = VRCShader.PropertyToID("_RunoffStep");
            _idRunoffAmount = VRCShader.PropertyToID("_RunoffAmount");
            _idLatherGate = VRCShader.PropertyToID("_LatherGate");

            if (meshToWorldScale <= 0f) meshToWorldScale = 1f;
            for (int i = 0; i < SenderSlots; i++) { _sndId[i] = -1; _sndLast[i] = -1e9f; }
            for (int i = 0; i < OwnFoamMax; i++) _ofAt[i] = -1e9f;      // IMP-DOG-039: an empty ring (Time.time starts near 0)
            for (int i = 0; i <= ChannelCount; i++) { _fnPart[i] = -1; _fnAt[i] = -100f; }
            EnsureArrays();
            BuildIndex();

            // Runtime copies only (vrc-udonsharp §1: Renderer.material is the one way to duplicate). The shell's
            // copy is taken once here and re-pointed at the newest mask after every Blit.
#if !UNITY_ANDROID && !UNITY_IOS
            PCFoamPrepare();
#endif
            if (shellRenderer != null) _shellMat = shellRenderer.material;
            _maskMat = maskMaterialHolder != null ? maskMaterialHolder.material : maskMaterial;

            // Arrays on a material are not saved with the .mat asset, so both shaders get theirs here.
            float[] b2p = new float[BoneSlots];
            if (boneToPart != null) for (int i = 0; i < BoneSlots && i < boneToPart.Length; i++) b2p[i] = boneToPart[i];
            float[] limit = new float[AllowSlots];
            for (int i = 0; i < AllowSlots; i++) limit[i] = i == 7 ? 0.35f : 1f;
            if (partFoamLimit != null) for (int i = 0; i < AllowSlots && i < partFoamLimit.Length; i++) limit[i] = partFoamLimit[i];
            if (_maskMat != null)
            {
                if (posMap != null) _maskMat.SetTexture(_idPosMap, posMap);
                if (cellTex != null) _maskMat.SetTexture(_idCellTex, cellTex);
                _maskMat.SetFloatArray(_idBoneToPart, b2p);
                _maskMat.SetFloatArray(_idAllowPart, _allow);
                _maskMat.SetVector(_idLatherGate, Vector4.zero);   // IMP-DOG-039: off until a hand stroke (whatever the asset holds)
                _latherOn = false;
                _foamGrowEase = Mathf.Clamp(_maskMat.GetFloat(_idFoamGrowEase), 0f, 0.95f);
            }
            if (_shellMat != null)
            {
                if (posMap != null) _shellMat.SetTexture(_idPosMap, posMap);
                _shellMat.SetFloatArray(_idBoneToPart, b2p);
                _shellMat.SetFloatArray(_idPartFoamLimit, limit);
            }

            _cur = maskA;
            _next = maskB;
#if !UNITY_ANDROID && !UNITY_IOS
            PCFoamInitialize(limit);
#endif

            if (!_gotFirst)
            {
                // The Owner seeds the truth once per instance; everyone else shows the same seed until the first
                // receive replaces it (that receive repaints).
                InitialCells();
                if (Networking.IsOwner(gameObject) && epoch == 0)
                {
                    // A fresh epoch, not 1: if this Owner is a joiner whose Owner left before anything arrived, the
                    // others hold another epoch and repaint to this seed instead of silently mixing two states.
                    epoch = FreshEpoch();
                    _appliedEpoch = epoch;
                    // IMP-DOG-034 C2-2: a dog that starts clean must not fire the clean edge (and its dirt grace) on the
                    // first evaluation; EvaluateState arms it once it is dirty / foamed enough.
                    cleanArmed = initialDirt > 0f;
                    ClearArm();
                    _gotFirst = true;   // the seed IS the state: a later receive (after losing the ownership) must not repaint
                    // Start's RequestSerialization does not reach late joiners (vrc-networking §5): send later.
                    _syncDue = true;
                    _nextSync = Time.time + 2f;
                }
            }

            EnsureCreated();
            _shellOn = shellRenderer != null && shellRenderer.enabled;
            _ready = true;
            _restorePending = true;   // first thing in the first Update, inside the Blit budget
            float now = Time.time;
            // IMP-DOG-034 B1a / B1c / B1d / B1e: one phase per dog (six dogs = 1/6 of each period apart).
            _phase = dogIndex >= 0 ? (dogIndex % 6) / 6f : 0f;
            _nextEval = now + _phase * EvalInterval;
            _nextSafeEval = NextGrid(now, SafeEvalInterval, _phase + 0.5f);
            _nextDecay = NextGrid(now + 1f, DecayWashInterval, _phase);
            _lastDecayAt = now;
            _idleNext = now;
            _dirtNextSample = now + _phase * DirtSampleInterval;
            _nextRtCheck = now + 0.5f;
            _nextOwnerCheck = now + 1f;
            // Staggered per dog (review 3, 8): six dogs dirtied at once do not all apply, send and Blit in the same frame.
            // IMP-DOG-034 C2-1: the six dogs apply 1/6 of the interval apart; the first apply comes within one interval, so no
            // apply ever covers more than dirtApplyInterval (one event carries at most 0.126 per part: 20 s of mud = 0.1,
            // 20 s of river = 0.12 with riverDropSeconds 50).
            _dirtIvUsed = Mathf.Max(0.5f, dirtApplyInterval);
            _dirtNextApply = now + _dirtIvUsed * ((dogIndex >= 0 ? dogIndex % 6 : 0) + 1) / 6f;
            VRCPlayerApi owner0 = Networking.GetOwner(gameObject);
            if (Utilities.IsValid(owner0)) _ownerId = owner0.playerId;
            _lastRealtime = Time.realtimeSinceStartup;
            SilenceAll();
        }

        void EnsureSyncedArrays()
        {
            if (armBits == null || armBits.Length != ArmWords) armBits = new int[ArmWords];
            if (quietBits == null || quietBits.Length != ArmWords) quietBits = new int[ArmWords];
            if (appliedIds == null || appliedIds.Length != AppliedSlots) appliedIds = new int[AppliedSlots];
            if (appliedSeqs == null || appliedSeqs.Length != AppliedSlots) appliedSeqs = new int[AppliedSlots];
        }

        // Somewhere in the upper half of the 30-bit space, never 0 (0 = "no state"). The epoch only identifies a
        // version (equal or not); it is never compared for order.
        int FreshEpoch()
        {
            return UnityEngine.Random.Range(0x10000000, 0x3FFFFFFF);
        }

        void EnsureArrays()
        {
            EnsureSyncedArrays();
            if (_cellShowered == null || _cellShowered.Length != CellCount) _cellShowered = new bool[CellCount];
            if (_dirt != null) return;
            _dirt = new float[CellCount];
            _foam = new float[CellCount];
            _wet = new float[CellCount];
            _cellColors = new Color[CellCount];
        }

        // Per-part cell lists, so a stroke loops over its own and the neighbouring parts only (Udon is slow).
        void BuildIndex()
        {
            _cellsOk = cellValid != null && cellCenter != null && cellPart != null
                && cellValid.Length >= CellCount && cellCenter.Length >= CellCount && cellPart.Length >= CellCount;
            _partCellStart = new int[PartCount + 1];
            _partCenter = new Vector3[PartCount];
            int[] counts = new int[PartCount];
            if (_cellsOk)
            {
                for (int c = 0; c < CellCount; c++)
                {
                    int p = cellPart[c];
                    if (!cellValid[c] || p < 0 || p >= PartCount) continue;
                    counts[p]++;
                    _partCenter[p] += cellCenter[c];
                }
            }
            int total = 0;
            for (int p = 0; p < PartCount; p++)
            {
                _partCellStart[p] = total;
                total += counts[p];
                if (counts[p] > 0) _partCenter[p] = _partCenter[p] / counts[p];
            }
            _partCellStart[PartCount] = total;
            _partCells = new int[total];
            // IMP-DOG-034 B1a: exactly the cells EvaluateState / Decay counted before (`_cellsOk && !cellValid[c]` skipped).
            // _partCells is not the same set (it also drops a valid cell whose part is out of range).
            int nv = 0;
            for (int c = 0; c < CellCount; c++) if (!_cellsOk || cellValid[c]) nv++;
            _validCells = new int[nv];
            nv = 0;
            for (int c = 0; c < CellCount; c++) if (!_cellsOk || cellValid[c]) { _validCells[nv] = c; nv++; }
            int[] fill = new int[PartCount];
            _partReach = new float[PartCount];
            ComputeCellHeights();
            if (_cellsOk)
            {
                for (int c = 0; c < CellCount; c++)
                {
                    int p = cellPart[c];
                    if (!cellValid[c] || p < 0 || p >= PartCount) continue;
                    _partCells[_partCellStart[p] + fill[p]] = c;
                    fill[p]++;
                    _partReach[p] = Mathf.Max(_partReach[p], (cellCenter[c] - _partCenter[p]).magnitude);
                }
            }

            // Mesh -> bone for received strokes (effects need a world point), and each collider's centre in mesh
            // space so a received point is mapped through the bone nearest to it (a leg has several bones).
            int n = partColliders != null ? partColliders.Length : 0;
            _meshToBone = new Matrix4x4[n];
            _colliderMeshCenter = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                if (!ColliderUsable(i)) continue;
                _meshToBone[i] = boneToMesh[i].inverse;
                Vector3 local = partBones[i].InverseTransformPoint(partColliders[i].bounds.center);
                _colliderMeshCenter[i] = boneToMesh[i].MultiplyPoint3x4(local);
            }
        }

        bool ColliderUsable(int i)
        {
            return partColliders != null && partBones != null && partOfCollider != null && boneToMesh != null
                && i >= 0 && i < partColliders.Length && i < partBones.Length && i < partOfCollider.Length && i < boneToMesh.Length
                && partColliders[i] != null && partBones[i] != null && partOfCollider[i] >= 0 && partOfCollider[i] < PartCount;
        }

        // Prototype seed: legs dirtiest, then the body (belly), little on the head (design §1-9 order).
        void InitialCells()
        {
            _cellsDirty = true;   // IMP-DOG-034 B1a
            for (int c = 0; c < CellCount; c++)
            {
                _foam[c] = 0f;
                _wet[c] = 0f;
                _dirt[c] = 0f;
                if (!_cellsOk || !cellValid[c]) continue;
                // From the feet up (decision 2026-09-30): the height weight, not the part table
                _dirt[c] = Mathf.Clamp01(initialDirt * (_cellW != null ? _cellW[c] : PartDirtFactor(cellPart[c])));
            }
        }

        // The cell centres' height above the lowest valid one (m) and their dirt weight.
        void ComputeCellHeights()
        {
            _cellHeight = new float[CellCount];
            _cellW = new float[CellCount];
            if (!_cellsOk) { for (int c = 0; c < CellCount; c++) _cellW[c] = 1f; return; }
            float minZ = 1e9f;
            for (int c = 0; c < CellCount; c++) if (cellValid[c] && cellCenter[c].z < minZ) minZ = cellCenter[c].z;
            if (minZ > 1e8f) minZ = 0f;
            _groundZ = minZ;
            for (int c = 0; c < CellCount; c++)
            {
                _cellHeight[c] = (cellCenter[c].z - minZ) * meshToWorldScale;
                _cellW[c] = DirtHeightWeight(_cellHeight[c]);
            }
        }

        // 1 up to dirtHeightFull, 1 -> dirtMidWeight up to dirtHeightMid, -> dirtTopWeight up to dirtHeightTop, then flat.
        // The mask shader uses the same curve (_DirtHeightFade / _DirtHeightWeight, see DrawDirt).
        float DirtHeightWeight(float h)
        {
            float full = Mathf.Max(0f, dirtHeightFull);
            float mid = Mathf.Max(full + 0.001f, dirtHeightMid);
            float top = Mathf.Max(mid + 0.001f, dirtHeightTop);
            if (h <= full) return 1f;
            if (h <= mid) return Mathf.Lerp(1f, dirtMidWeight, (h - full) / (mid - full));
            return Mathf.Lerp(dirtMidWeight, dirtTopWeight, Mathf.Clamp01((h - mid) / (top - mid)));
        }

        float PartDirtFactor(int part)
        {
            if (part >= 3 && part <= 6) return 1f;    // legs
            if (part == 0 || part == 1) return 0.6f;  // body incl. the belly
            if (part == 8) return 0.4f;               // tail
            if (part == 2) return 0.3f;               // neck
            return 0.15f;                             // head
        }

        // ================= Entry points =================

        // The kinds a wash stroke may carry (NetStroke, logs, records). ToolDirt (3) is not one: it has its own path.
        bool WashKind(int tool)
        {
            return tool == ToolSponge || tool == ToolShower || tool == ToolTowel || tool == ToolBottle || tool == ToolHand || tool == ToolDryer;
        }

        // A held tool's channel (0, 1, 2 -> the same; the bottle -> 3); -1 for anything else (the hands have a channel per
        // hand, ChHandL + hand, and never come through the tool entries).
        int ToolChannel(int tool)
        {
            if (tool == ToolSponge || tool == ToolShower || tool == ToolTowel) return tool;
            if (tool == ToolBottle) return ChBottle;
            if (tool == ToolDryer) return ChDryer;
            return -1;
        }

        // channel 0, 1, 2, 3, 4, 5 -> kind 0, 1, 2, 4, 5, 5 (design §13.4).
        int ChannelTool(int ch)
        {
            if (ch < ChBottle) return ch;
            if (ch == ChBottle) return ToolBottle;
            if (ch == ChDryer) return ToolDryer;
            return ToolHand;
        }

        // Preferred entry (KH_WashTool): this frame's contact point only. It is converted to mesh space now, and the
        // capsule runs from this tool's previous mesh point (same collider, within SegmentGap) to it. Two tools of the
        // same kind in one player's two hands share this history (a limitation of the prototype; the bare hands do not,
        // they have a channel each).
        public void _LocalContact(int tool, int colliderIndex, Vector3 worldPoint, float seconds, float radiusMeters)
        {
            int ch = ToolChannel(tool);
            if (!_ready || ch < 0 || !ColliderUsable(colliderIndex)) return;
            Vector3 m = partBones[colliderIndex].InverseTransformPoint(worldPoint);
            m = boneToMesh[colliderIndex].MultiplyPoint3x4(m);
            float now = Time.time;
            Vector3 from = (_hValid[ch] && _hCollider[ch] == colliderIndex && now - _hTime[ch] <= SegmentGap) ? _hMesh[ch] : m;
            _hValid[ch] = true;
            _hCollider[ch] = colliderIndex;
            _hMesh[ch] = m;
            _hTime[ch] = now;
            Stroke(tool, ch, colliderIndex, from, m, worldPoint, seconds, radiusMeters, true);
        }

        // The tool lost contact / Use was released: end this tool's capsule and its outgoing segment now, so the next
        // contact does not join two points across the gap.
        public void _EndContact(int tool)
        {
            int ch = ToolChannel(tool);
            if (ch < 0) return;
            _hValid[ch] = false;
            if (_ready) CloseSegment(ch, false);
        }

        // IMP-DOG-039 (design §3.3 / §13.5): one bare hand's contact this sample (KH_HandWash, the local VR player's free hand;
        // hand 0 = left, 1 = right). The point goes to mesh space at once and the hand's own channel (ChHandL + hand) keeps the
        // history, so the two hands never share a capsule. A lather stroke is issued only when
        //   - the hand moves: the TRACKED hand's world speed (handWorld) since this hand's previous sample (within SegmentGap)
        //     is within handMinSpeed..handMaxSpeed (a hand held still in the world on a moving leg is not rubbing; a tracking jump
        //     is not either). worldPoint (the coat point under the hand) is only where it paints, and
        //   - foam is there as far as this client knows: the nearest cell's foam >= max(handStartFoam, handFoamMin) (this
        //     channel's FoamNear cache; the max makes the sender's cell test never looser than the Owner's gate, Codex review
        //     of the implementation, 4), or the own-foam ring (the own bottle / sponge foam of the last ownFoamSeconds, before the
        //     cells arrive).
        // Otherwise only the history moves and nothing is drawn or sent. True = a lather stroke was issued (the caller's haptic).
        // The mask (_LatherGate) and the Owner's cells gate the foam again by what they hold, so this judgement only decides
        // whether to send and to give feedback; a wrong or forged one cannot grow foam where there is none.
        // A stroke allowed by the ring alone (the cells this client holds do not show foam yet) is drawn, sent and gives the light
        // effects, but does not count as washing here: no _lastWasherId, _localStrokeAt (_StrokeAgeOf) or last-stroke time.
        // Whether it was a wash is the Owner's call (touchedFoam -> the synced hold).
        public bool _HandContact(int hand, int colliderIndex, Vector3 worldPoint, Vector3 handWorld, float seconds)
        {
            if (!_ready || hand < 0 || hand > 1 || !ColliderUsable(colliderIndex)) return false;
            int ch = ChHandL + hand;
            float now = Time.time;
            Vector3 m = boneToMesh[colliderIndex].MultiplyPoint3x4(partBones[colliderIndex].InverseTransformPoint(worldPoint));
            Vector3 from = (_hValid[ch] && _hCollider[ch] == colliderIndex && now - _hTime[ch] <= SegmentGap) ? _hMesh[ch] : m;
            _hValid[ch] = true;
            _hCollider[ch] = colliderIndex;
            _hMesh[ch] = m;
            _hTime[ch] = now;
            float dt = now - _hwTime[hand];
            bool moving = false;
            if (_hwValid[hand] && dt <= SegmentGap && dt > 1e-4f)
            {
                float speed = (handWorld - _hwPos[hand]).magnitude / dt;
                moving = speed >= handMinSpeed && speed <= handMaxSpeed;
            }
            _hwValid[hand] = true;
            _hwPos[hand] = handWorld;
            _hwTime[hand] = now;
            if (!moving) return false;
            int part = partOfCollider[colliderIndex];
            bool cellsFoam = FoamNear(ch, part, m) >= Mathf.Max(handStartFoam, handFoamMin);
            if (!cellsFoam && !OwnFoamNear(part, m)) return false;
            // One step longer than a receiver accepts (a hitch): start over here instead of drawing / sending the long capsule
            if ((m - from).magnitude * meshToWorldScale > handMaxSegment * 0.9f)
            {
                from = m;
                CloseSegment(ch, false);
            }
            Stroke(ToolHand, ch, colliderIndex, from, m, worldPoint, seconds, -1f, cellsFoam);
            handStrokesIssued++;
            return true;
        }

        // IMP-DOG-039: the hand stopped touching (KH_HandWash calls it once when the contact closes): end its capsule and its
        // outgoing segment now. The other hand's channel is untouched.
        public void _EndHand(int hand)
        {
            if (hand < 0 || hand > 1) return;
            int ch = ChHandL + hand;
            _hValid[ch] = false;
            _hwValid[hand] = false;
            if (_ready) CloseSegment(ch, false);
        }

        // The own-foam ring (§13.5): a recent own bottle / sponge stroke (same part or a neighbour of it) within its radius plus
        // the hand's radius of the mesh point.
        bool OwnFoamNear(int part, Vector3 meshPoint)
        {
            float now = Time.time;
            float handR = handRadius / meshToWorldScale;
            int nb = partNeighbors != null && part >= 0 && part < partNeighbors.Length ? partNeighbors[part] : 0;
            for (int i = 0; i < OwnFoamMax; i++)
            {
                if (now - _ofAt[i] > ownFoamSeconds) continue;
                int q = _ofPart[i];
                if (q != part && (q < 0 || q >= PartCount || ((nb >> q) & 1) == 0)) continue;
                float r = _ofRad[i] + handR;
                if ((_ofPos[i] - meshPoint).sqrMagnitude <= r * r) return true;
            }
            return false;
        }

        // The own bottle / sponge stroke ends at meshPoint: refresh an entry it falls into (same part, within half its radius),
        // else take the oldest slot. Hand strokes never come here.
        void AddOwnFoam(int part, Vector3 meshPoint, float radMesh)
        {
            float now = Time.time;
            for (int i = 0; i < OwnFoamMax; i++)
            {
                if (now - _ofAt[i] > ownFoamSeconds || _ofPart[i] != part) continue;
                float h = 0.5f * _ofRad[i];
                if ((_ofPos[i] - meshPoint).sqrMagnitude > h * h) continue;
                _ofPos[i] = meshPoint;
                _ofRad[i] = Mathf.Max(_ofRad[i], radMesh);
                _ofAt[i] = now;
                return;
            }
            int k = _ofNext;
            _ofNext = (_ofNext + 1) % OwnFoamMax;
            _ofPart[k] = part;
            _ofPos[k] = meshPoint;
            _ofRad[k] = radMesh;
            _ofAt[k] = now;
        }

        // The own shower reaches meshPoint: the ring entries it rinses are gone (another player's shower is caught up by the
        // ownFoamSeconds expiry and the synced cells). No part test: the shower has no part gate.
        void ClearOwnFoam(Vector3 meshPoint, float radMesh)
        {
            float reach = radMesh * Mathf.Max(1f, rinseReach);
            for (int i = 0; i < OwnFoamMax; i++)
            {
                if (_ofAt[i] < -1e8f) continue;
                float r = reach + _ofRad[i];
                if ((_ofPos[i] - meshPoint).sqrMagnitude <= r * r) _ofAt[i] = -1e9f;
            }
        }

        // Contract entry (§6). Both ends are converted with the CURRENT bone pose, so a worldFrom from an earlier
        // frame lands off the surface when the part moved; _LocalContact avoids that and is what KH_WashTool uses.
        public void _LocalStroke(int tool, int colliderIndex, Vector3 worldFrom, Vector3 worldTo, float seconds)
        {
            _LocalStrokeR(tool, colliderIndex, worldFrom, worldTo, seconds, -1f);
        }

        // Same as _LocalStroke with an explicit radius in metres. radius <= 0 = the tool's default.
        public void _LocalStrokeR(int tool, int colliderIndex, Vector3 worldFrom, Vector3 worldTo, float seconds, float radiusMeters)
        {
            int ch = ToolChannel(tool);
            if (!_ready || ch < 0 || !ColliderUsable(colliderIndex)) return;
            Transform bone = partBones[colliderIndex];
            Matrix4x4 mat = boneToMesh[colliderIndex];
            Vector3 a = mat.MultiplyPoint3x4(bone.InverseTransformPoint(worldFrom));
            Vector3 b = mat.MultiplyPoint3x4(bone.InverseTransformPoint(worldTo));
            _hValid[ch] = true;
            _hCollider[ch] = colliderIndex;
            _hMesh[ch] = b;
            _hTime[ch] = Time.time;
            Stroke(tool, ch, colliderIndex, a, b, worldTo, seconds, radiusMeters, true);
        }

        // tool = the kind (drawn, effects); ch = the sender's channel (the outgoing segment). counts = this stroke is washing as
        // far as this client knows (every tool stroke; a hand stroke only when the cells it holds show foam, _HandContact): only
        // then do the washer (_lastWasherId), _localStrokeAt (_StrokeAgeOf) and the last-stroke time move. The draw, the send,
        // the effects, the shell and _busyUntil (B1e: Update per-frame for the open segment and the audio fade) always happen.
        void Stroke(int tool, int ch, int colliderIndex, Vector3 a, Vector3 b, Vector3 worldTo, float seconds, float radiusMeters, bool counts)
        {
            if (!(seconds > 0f)) return;
            seconds = Mathf.Min(seconds, 0.25f);   // a long hitch must not become one huge dab
            float rad = radiusMeters > 0f ? radiusMeters : ToolRadius(tool);
            rad = Mathf.Clamp(rad, 0.02f, MaxToolRadius(tool));
            int part = partOfCollider[colliderIndex];

            QueueOrDraw(tool, part, a, b, rad, seconds);
            AddToSegment(ch, part, a, b, rad, seconds);
            // IMP-DOG-039 §13.5: the own-foam ring - fed by the own bottle and sponge only, cleared by the own shower
            if (tool == ToolBottle || tool == ToolSponge) AddOwnFoam(part, b, rad / meshToWorldScale);
            else if (tool == ToolShower) ClearOwnFoam(b, rad / meshToWorldScale);

            if (counts)
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp)) _lastWasherId = lp.playerId;
                // IMP-DOG-049 段 B (Codex review 4): the own water hits are recorded with the own stroke, the same moment as
                // _lastStrokePart below (the echo comes a round trip later, after the dog's paw lift would already have taken the part).
                // At most 10 Hz per part (the source lookup loops the tools); the echo records again, which only refreshes the same values.
                if (tool == ToolShower && Utilities.IsValid(lp) && (Time.time - _ownWaterNoteAt >= 0.1f || part != _ownWaterNotePart))
                {
                    _ownWaterNoteAt = Time.time;
                    _ownWaterNotePart = part;   // a part change is recorded at once (the paw lift reads the part this frame)
                    NoteWaterHit(lp.playerId, part, b);
                }
                _localStrokeAt = Time.time;
                _lastStrokePart = part;
                _lastStrokeTime = Time.time;
                PhotoStroke(tool, seconds);   // IMP-WLD-011: this client washes this dog (the wash session, the rinse milestone)
            }
            _lastActivity = Time.time;
            _lastWetActivity = _lastActivity;          // IMP-DOG-034 B1c
            _busyUntil = _lastActivity + BusyHold;     // IMP-DOG-034 B1e: per-frame while washing (segments, audio)
            if (tool == ToolShower) MarkRunoff();
            ContactEffects(tool, part, worldTo, b, seconds);
            ShowShell(true);
        }

        // net: network-callable; every argument is checked (anyone may send): ranges, the sender's sequence and
        // contact-time budget, distance, and (when `tools` is wired) that the sender holds a tool of that kind. The
        // sender's own echo is not drawn again, but it is integrated into the cells like everyone else's.
        [NetworkCallable(maxEventsPerSecond: 8)]
        public void NetStroke(int tool, int part, int from, int to, int amount, int seq, int strokeEpoch)
        {
            if (!_ready) return;
            if (tool == ToolDirt) { ReceiveDirt(part, from, to, amount, seq, strokeEpoch); return; }
            if (!WashKind(tool) || part < 0 || part >= PartCount) return;
            if (from < 0 || from > PackMax || to < 0 || to > PackMax) return;
            if (amount < 0 || (amount >> 20) != 0) return;
            int ms = amount & 0xFFF;
            int rmm = (amount >> 12) & 0xFF;
            if (ms <= 0 || ms > MaxAmountMs || rmm < 20 || rmm > Mathf.RoundToInt(MaxToolRadius(tool) * 1000f)) return;
            if (seq < 0 || strokeEpoch < 0) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;
            // 2026-10-07 audit RC-7 (a): deferred strokes first (AcceptSender takes a sender's seqs in order only)
            DrainDeferred();
            ReceiveStroke(caller, tool, part, from, to, ms, rmm, seq, strokeEpoch, false);
        }

        // 2026-10-07 audit RC-7 (a): the body of NetStroke after the argument checks, also run for a deferred stroke (queued).
        void ReceiveStroke(VRCPlayerApi caller, int tool, int part, int from, int to, int ms, int rmm, int seq, int strokeEpoch, bool queued)
        {
            // Epoch first, before the sender's sequence and budget are touched: a stale stroke must not use up the
            // budget of the fresh ones behind it. The epoch only identifies the version: a stroke is taken when it
            // matches this client's epoch, or is 0 (the sender held no state yet: a joiner). Anything else is
            // dropped, also a stroke of a reset that has not reached this client yet (a short visual gap only; the
            // cells come with the reset).
            if (strokeEpoch != 0 && strokeEpoch != epoch) return;

            if (dogRoot != null && Vector3.Distance(caller.GetPosition(), dogRoot.position) > maxWasherDistance) return;
            bool hand = tool == ToolHand;
            Vector3 a = Unpack(part, from);
            Vector3 b = Unpack(part, to);
            // The own echo comes from this client's own _LocalContact / _HandContact (only the local player calls them): trusted.
            // IMP-DOG-039 §13.1: a bare-hand stroke has no tool to hold; every client checks what is the same everywhere (the
            // sender is in VR, the capsule is short, this dog is held and arrived at an active station: synced). The bottle and
            // the other tools keep the hold check (HoldsTool: a KH_WashTool with tool == ToolBottle).
            if (!caller.isLocal)
            {
                // 2026-10-07 audit RC-7 (a), Codex review A-1: a sender with a stroke still deferred keeps its order - every later
                // stroke of that sender (any tool, the bare hand too) waits behind it, or AcceptSender's per-sender seq would take
                // the later one and then refuse the deferred one as old (the sender's _seq is shared by both hands / all tools)
                if (!queued && SenderDeferred(caller.playerId)) { DeferStroke(caller.playerId, tool, part, from, to, ms, rmm, seq, strokeEpoch); return; }
                if (hand)
                {
                    if (!HandSyncedOk(caller.IsUserInVR(), (b - a).magnitude * meshToWorldScale)) { handStrokesRejected++; return; }
                }
                else if (!HoldsTool(caller.playerId, tool))
                {
                    // 2026-10-07 audit RC-7 (a): not dropped at once - kept until the holder / Owner change arrives (FUNC-03/BUG-07)
                    if (!queued) DeferStroke(caller.playerId, tool, part, from, to, ms, rmm, seq, strokeEpoch);
                    return;
                }
            }
            float sec = ms * 0.001f;
            if (!AcceptSender(caller.playerId, seq, sec)) return;
            int slot = _acceptSlot;
            bool owner = Networking.IsOwner(gameObject);
            // IMP-DOG-039 §13.1: the hands' / head's distance differs per client (a remote player's tracking is this client's
            // view of the avatar), so only the Owner judges it (plus the dog's own station hold). Order and budget above are
            // already spent on every client alike. A stroke the Owner drops was drawn elsewhere: a finite, visual-only extra
            // of foam where foam already shows (the mask gate); cells, hold and the clean edge are not touched.
            if (hand && owner && !caller.isLocal && !HandOwnerOk(caller, part, a, b))
            {
                handStrokesRejected++;
                if (slot >= 0) _sndLast[slot] = _acceptPrevLast;   // no wash seen from it (_StrokeAgeOf)
                return;
            }

            float rad = rmm * 0.001f;
            // IMP-DOG-039 §13.2 + Codex review of the implementation (1, 4): what a hand stroke may move here.
            //   touched = it counts as WASHING on this client: the washer (_lastWasherId), the sender's stroke age (_sndLast ->
            //             _StrokeAgeOf: the station's auto-release, the leash stop, the reactions) and (Owner) the hold.
            //             Owner: its cells before the update touch foam (the value ApplyToCells gates with), after its own checks.
            //             Non-Owner: only the OWN echo, when the cells it holds show foam. Another player's hand stroke is never
            //             washing on a non-Owner: this client cannot check the hands / head, so it only draws it and plays the
            //             effects; whether it was a wash reaches everyone through the Owner's synced hold (washMs / washBy, which
            //             _StrokeAgeOf also reads).
            //   fx      = the local visuals (effects, shell, last-stroke part / time): the Owner as touched, a non-Owner when the
            //             nearest received cell shows foam.
            bool touched = true;
            bool fx = true;
            if (hand)
            {
                if (owner) touched = HandSourceFoam(part, a, b, rad) >= handFoamMin;
                else touched = caller.isLocal && FoamNear(FoamKeyEffects, part, b) >= handFoamMin;
                fx = owner ? touched : FoamNear(FoamKeyEffects, part, b) >= handFoamMin;
                if (!touched && slot >= 0) _sndLast[slot] = _acceptPrevLast;
            }
            if (touched) _lastWasherId = caller.playerId;
            if (tool == ToolShower) NoteWaterHit(caller.playerId, part, b);   // IMP-DOG-049 段 B: every client, the own echo too
            if (touched || fx) _busyUntil = Time.time + BusyHold;   // IMP-DOG-034 B1e: an accepted stroke brings Update back to per-frame
            // Every client (the own echo too: the Owner's cells take the shower at the echo, so their runoff follows it)
            if (tool == ToolShower) MarkRunoff();

            if (!caller.isLocal)
            {
                // The mask is drawn either way: for a hand its gate (_LatherGate) decides from what this mask holds.
                QueueOrDraw(tool, part, a, b, rad, sec);
                if (fx)
                {
                    _lastStrokePart = part;
                    _lastStrokeTime = Time.time;
                    _lastActivity = Time.time;
                    _lastWetActivity = _lastActivity;   // IMP-DOG-034 B1c
                    ContactEffects(tool, part, MeshToWorld(part, b), b, sec);
                    ShowShell(true);
                }
            }

            // Codex review of the implementation (2): a non-Owner does not record another player's hand stroke (it could not run
            // the Owner's checks, and the Owner may have refused it), so Reintegrate never applies one. Consequence: at an Owner
            // change, up to one sync interval of other players' lather that the old Owner accepted but had not synced yet is
            // missing from the new Owner's cells (the masks keep it; it is foam on foam). The own hand strokes are recorded (the
            // local input is trusted, as for the tools).
            if (!hand || owner || caller.isLocal) Record(caller.playerId, seq, epoch, tool, part, a, b, rad, sec);
            // Only the Owner integrates (a non-Owner's own sums would be overwritten by an older receive anyway; it
            // keeps the record instead and re-integrates when it becomes the Owner).
            if (!owner) return;
            ApplyToCells(tool, part, a, b, rad, sec);
            SetApplied(caller.playerId, seq);
            if (!touched)
            {
                // §13.2: the order is recorded (SetApplied, AcceptSender); nothing else - no hold, no washer, no edge, no reset of
                // the dirt counts, no sync of its own (appliedSeqs go out with the next one).
                handStrokesInert++;
                return;
            }
            riverBlocksEdge = false;   // net: owner-only. Washed by a tool: the clean edge may fire again (IMP-DOG-029)
            // (review 3, 2-1) a wash started: what was counted before it is not applied after it.
            _walkAcc = 0f;
            _zoneAcc = 0f;
            _syncDue = true;
            // A stroke from before _ReleaseHold (accepted seq at the release, or in flight within ReleaseGrace)
            // changes the cells but does not start the hold again.
            bool old = _released && (Time.time - _releaseTime < ReleaseGrace
                || (slot >= 0 && _relValid[slot] && !SeqNewer(seq, _relSeq[slot])));
            if (!old)
            {
                washMs = Networking.GetServerTimeInMilliseconds();   // net: owner-only
                washSeen = true;                                      // net: owner-only
                washBy = caller.playerId;                             // net: owner-only. IMP-DOG-037: who holds it, with the hold
            }
        }

        // seq a is after b (30-bit wrap, the same rule as AcceptSender).
        bool SeqNewer(int a, int b)
        {
            int diff = (a - b) & 0x3FFFFFFF;
            return diff != 0 && diff < 0x20000000;
        }

        // Every client: keep the accepted stroke for a later re-integration (ring, the oldest goes).
        void Record(int sender, int seq, int ep, int tool, int part, Vector3 a, Vector3 b, float rad, float sec)
        {
            if (_rcCount >= RecMax)
            {
                for (int i = 1; i < RecMax; i++) CopyRecord(i, i - 1);
                _rcCount = RecMax - 1;
            }
            int k = _rcCount;
            _rcSender[k] = sender;
            _rcSeq[k] = seq;
            _rcEpoch[k] = ep;
            _rcTool[k] = tool;
            _rcPart[k] = part;
            _rcA[k] = a;
            _rcB[k] = b;
            _rcRad[k] = rad;
            _rcSec[k] = sec;
            _rcCount++;
        }

        void CopyRecord(int from, int to)
        {
            _rcSender[to] = _rcSender[from];
            _rcSeq[to] = _rcSeq[from];
            _rcEpoch[to] = _rcEpoch[from];
            _rcTool[to] = _rcTool[from];
            _rcPart[to] = _rcPart[from];
            _rcA[to] = _rcA[from];
            _rcB[to] = _rcB[from];
            _rcRad[to] = _rcRad[from];
            _rcSec[to] = _rcSec[from];
        }

        // The newest sequence number the cells hold for this sender; found = false when the sender is not listed.
        int AppliedSlotOf(int sender)
        {
            for (int i = 0; i < AppliedSlots; i++) if (appliedIds[i] == sender && sender != 0) return i;
            return -1;
        }

        // net: owner-only (appliedIds / appliedSeqs). A new sender takes a free slot, else the least recently used.
        void SetApplied(int sender, int seq)
        {
            int k = AppliedSlotOf(sender);
            if (k < 0)
            {
                k = 0;
                for (int i = 0; i < AppliedSlots; i++)
                {
                    if (appliedIds[i] == 0) { k = i; break; }
                    if (_apAt[i] < _apAt[k]) k = i;
                }
                appliedIds[k] = sender;
                appliedSeqs[k] = seq;
            }
            else if (SeqNewer(seq, appliedSeqs[k])) appliedSeqs[k] = seq;
            _apAt[k] = Time.time;
        }

        // A stroke the cells do not hold yet: the sender is not listed, or its seq is newer than the listed one.
        bool NotApplied(int sender, int seq)
        {
            int k = AppliedSlotOf(sender);
            return k < 0 || SeqNewer(seq, appliedSeqs[k]);
        }

        // The new Owner: integrate the recorded strokes of this epoch the received cells do not hold, in order.
        void Reintegrate()
        {
            for (int i = 0; i < _rcCount; i++)
            {
                if (_rcEpoch[i] != epoch || !NotApplied(_rcSender[i], _rcSeq[i])) continue;
                // IMP-DOG-039 §13.2: a hand stroke that touches no foam in these cells (now, in this order) only moves the order.
                // A recorded hand stroke of another player (recorded only while this client was the Owner and had checked it)
                // also passes the every-client checks again on the current state (HandRecordOk); one that fails only moves the order.
                bool touched = false;
                if (_rcTool[i] != ToolHand || HandRecordOk(_rcSender[i], _rcA[i], _rcB[i]))
                    touched = ApplyToCells(_rcTool[i], _rcPart[i], _rcA[i], _rcB[i], _rcRad[i], _rcSec[i]);
                SetApplied(_rcSender[i], _rcSeq[i]);
                if (touched) riverBlocksEdge = false;
                _reintCount++;
            }
        }

        // Explicit end of the hold (gather, recall, warp: the behaviour side calls it on the request). Owner: the hold
        // is expired now and synced; the strokes up to now (per sender: the accepted seq, and those arriving within
        // ReleaseGrace) still wash the cells but do not start it again; a newer stroke does. Non-Owner: the same,
        // locally (_WashHold() is false until the Owner syncs a stroke made after the release).
        public void _ReleaseHold()
        {
            if (!_ready) return;
            _released = true;
            _releaseTime = Time.time;
            _releaseMs = Networking.GetServerTimeInMilliseconds();
            for (int i = 0; i < SenderSlots; i++) { _relValid[i] = _sndId[i] >= 0; _relSeq[i] = _sndSeq[i]; }
            if (Networking.IsOwner(gameObject) && washSeen)
            {
                washMs = _releaseMs - Mathf.RoundToInt(holdSeconds * 1000f) - 1000;   // net: owner-only
                _syncDue = true;
                _nextSync = Time.time;
                Wake();   // IMP-DOG-034 B1e: the sync goes out next frame, not on the next idle tick
            }
        }

        // Tools are wired: the sender must actually hold (VRC_Pickup.currentPlayer) at least one tool of this kind, or
        // have held it / become its Owner within a short grace (the pickup state and the stroke travel separately;
        // KH_WashTool._IsHeldBy). Being the tool's Owner alone is not enough (the Owner stays after the drop).
        bool HoldsTool(int playerId, int tool)
        {
            if (tools == null || tools.Length == 0) return true;
            for (int i = 0; i < tools.Length; i++)
            {
                KH_WashTool t = tools[i];
                if (t != null && t.tool == tool && t._IsHeldBy(playerId)) return true;
            }
            return false;
        }

        // ---- IMP-DOG-039 §13.1: bare-hand strokes from others ----
        // Every client (the same answer everywhere: synced or sent values only): the sender is in VR, the capsule is no longer
        // than handMaxSegment, and this dog is held and arrived at an active station (washCalls; none wired = never).
        bool HandSyncedOk(bool senderVr, float segMeters)
        {
            if (!senderVr || !(segMeters <= handMaxSegment)) return false;
            return HeldAtStation();
        }

        bool HeldAtStation()
        {
            if (washCalls == null || dogIndex < 0) return false;
            for (int i = 0; i < washCalls.Length; i++)
            {
                KH_WashStationCall c = washCalls[i];
                if (c != null && c.gameObject.activeInHierarchy && c._HeldDog() == dogIndex && c._Arrived()) return true;
            }
            return false;
        }

        // Reintegrate (a new Owner): a recorded hand stroke is applied only when its sender is this client (trusted local input)
        // or, for another player, the every-client checks hold on the current state: the sender is still here and in VR, the
        // capsule is short, the dog is held and arrived at an active station, and the dog's own station hold.
        bool HandRecordOk(int sender, Vector3 meshA, Vector3 meshB)
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp) && lp.playerId == sender) return true;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(sender);
            if (!Utilities.IsValid(p)) return false;
            return HandSyncedOk(p.IsUserInVR(), (meshB - meshA).magnitude * meshToWorldScale) && DogStationHold();
        }

        // Owner only: the dog's own station hold as well (KD_DogWanderController.StationLocked: on the stand of the station
        // holding it now), so a stale heldDog alone does not pass. voiceDog is this dog's controller (the builder wires it);
        // null = refused (Codex review of the implementation, 3: an unwired dog takes no hand strokes from others).
        bool DogStationHold()
        {
            return voiceDog != null && voiceDog.StationLocked;
        }

        // Owner only: both capsule ends within handVerifyRadius of one of the sender's hands (each end may be near either hand)
        // and within handReachMeters of the sender's head, plus the dog's station hold.
        bool HandOwnerOk(VRCPlayerApi caller, int part, Vector3 meshA, Vector3 meshB)
        {
            Vector3 wa = MeshToWorld(part, meshA);
            Vector3 wb = MeshToWorld(part, meshB);
            Vector3 hl = caller.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            Vector3 hr = caller.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
            Vector3 head = caller.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            return HandNear(hl, hr, head, wa, wb) && DogStationHold();
        }

        bool HandNear(Vector3 handL, Vector3 handR, Vector3 head, Vector3 worldA, Vector3 worldB)
        {
            float v2 = handVerifyRadius * handVerifyRadius;
            float h2 = handReachMeters * handReachMeters;
            if (!((worldA - handL).sqrMagnitude <= v2 || (worldA - handR).sqrMagnitude <= v2)) return false;
            if (!((worldB - handL).sqrMagnitude <= v2 || (worldB - handR).sqrMagnitude <= v2)) return false;
            return (worldA - head).sqrMagnitude <= h2 && (worldB - head).sqrMagnitude <= h2;
        }

        /// <summary>Harness only (Course/100g H-hand-reject; ClientSim has no other CallingPlayer): the full check a bare-hand
        /// stroke from another player gets on the Owner - the every-client part (VR, segment length, held and arrived at an active
        /// station) and the Owner's part (both ends near a hand and the head, the dog's station hold) - for the given values.
        /// Pure: reads the arguments and the station state, changes nothing (no counters). Local.</summary>
        public bool _DebugHandPlausible(bool senderVr, Vector3 handL, Vector3 handR, Vector3 head, int part, Vector3 worldA, Vector3 worldB)
        {
            if (!_ready || part < 0 || part >= PartCount) return false;
            if (!HandSyncedOk(senderVr, (worldB - worldA).magnitude)) return false;
            return HandNear(handL, handR, head, worldA, worldB) && DogStationHold();
        }

        // 2026-10-07 audit RC-7 (a): keep a stroke whose sender is not yet seen holding the tool (full: the oldest goes).
        void DeferStroke(int sender, int tool, int part, int from, int to, int ms, int rmm, int seq, int strokeEpoch)
        {
            if (_dfCount >= DeferMax)
            {
                for (int i = 1; i < DeferMax; i++) CopyDeferred(i, i - 1);
                _dfCount = DeferMax - 1;
            }
            int k = _dfCount;
            _dfSender[k] = sender; _dfTool[k] = tool; _dfPart[k] = part; _dfFrom[k] = from; _dfTo[k] = to;
            _dfMs[k] = ms; _dfRmm[k] = rmm; _dfSeq[k] = seq; _dfEpoch[k] = strokeEpoch; _dfAt[k] = Time.time;
            _dfCount++;
            _strokesDeferred++;
        }

        void CopyDeferred(int from, int to)
        {
            _dfSender[to] = _dfSender[from]; _dfTool[to] = _dfTool[from]; _dfPart[to] = _dfPart[from]; _dfFrom[to] = _dfFrom[from];
            _dfTo[to] = _dfTo[from]; _dfMs[to] = _dfMs[from]; _dfRmm[to] = _dfRmm[from]; _dfSeq[to] = _dfSeq[from];
            _dfEpoch[to] = _dfEpoch[from]; _dfAt[to] = _dfAt[from];
        }

        // 2026-10-07 audit RC-7 (a): a deferred stroke whose sender now holds that kind of tool (a bare-hand stroke: at once) runs
        // the full receive (epoch, distance, hand check, sequence and budget again); one older than KH_WashTool.HoldGrace, or whose
        // sender left, goes. Per sender the order is kept (Codex review A-1): while an earlier stroke of the same sender is still
        // kept, the later ones of that sender wait behind it. Every client (NetStroke before a new stroke, Update while any is
        // kept), so every mask and the Owner's cells take it. The receive may still refuse it (seq / budget / epoch).
        void DrainDeferred()
        {
            if (_dfCount == 0) return;
            float now = Time.time;
            int n = _dfCount;
            int keep = 0;
            for (int i = 0; i < n; i++)
            {
                bool gone = now - _dfAt[i] > KH_WashTool.HoldGrace;
                VRCPlayerApi p = null;
                if (!gone) p = VRCPlayerApi.GetPlayerById(_dfSender[i]);
                if (!gone && !Utilities.IsValid(p)) gone = true;
                bool blocked = false;   // an earlier stroke of the same sender is still kept: this one waits behind it
                if (!gone) for (int j = 0; j < keep; j++) if (_dfSender[j] == _dfSender[i]) { blocked = true; break; }
                if (!gone && !blocked && (_dfTool[i] == ToolHand || HoldsTool(_dfSender[i], _dfTool[i])))
                {
                    _strokesDeferRetried++;
                    ReceiveStroke(p, _dfTool[i], _dfPart[i], _dfFrom[i], _dfTo[i], _dfMs[i], _dfRmm[i], _dfSeq[i], _dfEpoch[i], true);
                    gone = true;
                }
                if (gone) continue;
                if (keep != i) CopyDeferred(i, keep);
                keep++;
            }
            _dfCount = keep;
        }

        // 2026-10-07 audit RC-7 (a): does `sender` have a stroke kept in the deferred queue?
        bool SenderDeferred(int sender)
        {
            for (int i = 0; i < _dfCount; i++) if (_dfSender[i] == sender) return true;
            return false;
        }

        // Per sender: seq must move forward (a replay or a reorder is dropped), and the contact time is limited to
        // BudgetRate seconds per second (the attribute rate limit bounds the call count, not what each call claims).
        bool AcceptSender(int playerId, int seq, float sec)
        {
            float now = Time.time;
            int slot = -1;
            int oldest = 0;
            for (int i = 0; i < SenderSlots; i++)
            {
                if (_sndId[i] == playerId) { slot = i; break; }
                if (_sndAt[i] < _sndAt[oldest]) oldest = i;
            }
            if (slot < 0)
            {
                slot = oldest;
                _sndId[slot] = playerId;
                _sndSeq[slot] = seq - 1;
                _sndTokens[slot] = BudgetCap;
                _sndAt[slot] = now;
                _sndLast[slot] = -1e9f;
                _relValid[slot] = false;
            }
            int diff = (seq - _sndSeq[slot]) & 0x3FFFFFFF;
            if (diff == 0 || diff >= 0x20000000) return false;
            float tokens = Mathf.Min(BudgetCap, _sndTokens[slot] + (now - _sndAt[slot]) * BudgetRate);
            _sndAt[slot] = now;
            if (tokens < sec) { _sndTokens[slot] = tokens; return false; }
            _sndTokens[slot] = tokens - sec;
            _sndSeq[slot] = seq;
            _acceptPrevLast = _sndLast[slot];
            _sndLast[slot] = now;
            _acceptSlot = slot;
            return true;
        }

        // The player of the last stroke seen here (own or received); -1 before any or after that player left.
        // IMP-DOG-039 (Codex review of the implementation, 1): a non-Owner no longer takes another player's hand stroke as a
        // washer (it cannot check it); that washing reaches it through the Owner's synced hold. So when the synced holder
        // (washBy) washed clearly more recently than the local washer (synced age + SyncedWasherMargin < the local one's age),
        // the holder is the washer. With the tools both name the same strokes (the margin keeps two washers from flickering).
        public int _WasherPlayerId()
        {
            if (!washSeen || washBy < 0 || washBy == _lastWasherId) return _lastWasherId;
            float synced = SyncedAgeOf(washBy);
            if (synced > 1e8f) return _lastWasherId;
            if (_lastWasherId < 0 || synced + SyncedWasherMargin < _StrokeAgeOf(_lastWasherId)) return washBy;
            return _lastWasherId;
        }
        const float SyncedWasherMargin = 1f;

        // IMP-DOG-039: seconds since the stroke that set the synced hold, when playerId is its holder (washBy); 1e9 otherwise,
        // or when that hold was ended by _ReleaseHold. Authoritative: washMs / washSeen / washBy are written only by the Owner
        // of this object, and only for a stroke that passed every check there (a hand stroke: the Owner's hand / head / station
        // checks and touchedFoam). Server milliseconds, int subtraction (the _WashHold convention); a small negative age (the
        // Owner's clock a moment ahead) counts as 0.
        float SyncedAgeOf(int playerId)
        {
            if (!washSeen || washBy != playerId || playerId < 0) return 1e9f;
            if (_released && washMs - _releaseMs <= 0) return 1e9f;
            int ageMs = Networking.GetServerTimeInMilliseconds() - washMs;
            if (ageMs < -1000) return 1e9f;
            return Mathf.Max(0, ageMs) * 0.001f;
        }

        /// <summary>IMP-DOG-037: the player whose stroke set the current wash hold (synced washBy; -1 when none). Pair it with
        /// _WashHold(): both come from the same synced state, so every client and every Owner give the same answer.</summary>
        public int _WashHolderId() { return washSeen ? washBy : -1; }

        // Seconds (Time.time) since that player's last stroke seen here: the own strokes as they are drawn, the others'
        // as their NetStroke is accepted. 1e9 when there is no record (never, or evicted from the sender table).
        // IMP-DOG-039 (Codex review of the implementation, 1): which values count -
        //   local  : _localStrokeAt (own strokes that are washing as far as this client knows) and _sndLast (strokes accepted
        //            here; another player's hand stroke counts only on the Owner, after its checks and touchedFoam - a
        //            non-Owner restores _sndLast for it),
        //   synced : the Owner's hold (SyncedAgeOf: washMs when washBy == playerId), the authoritative record of a wash.
        // The smaller wins, so a non-Owner (e.g. the wash station's Owner deciding the auto-release) still sees another player's
        // real hand washing, one sync later, and never an unverified one.
        public float _StrokeAgeOf(int playerId)
        {
            float now = Time.time;
            float age = SyncedAgeOf(playerId);
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp) && lp.playerId == playerId && _localStrokeAt > -1e8f) age = Mathf.Min(age, now - _localStrokeAt);
            for (int i = 0; i < SenderSlots; i++)
            {
                if (_sndId[i] != playerId || _sndLast[i] < -1e8f) continue;
                age = Mathf.Min(age, now - _sndLast[i]);
            }
            return age;
        }

        // Harness only (ClientSim has one player, so CallingPlayer is always local): the per-sender check with a
        // made-up player id. Local-only (`_`), changes only the budget table.
        public bool _DebugAcceptSender(int playerId, int seq, float sec)
        {
            return AcceptSender(playerId, seq, sec);
        }

        // Washed within holdSeconds. No stroke yet = false (not "server time < 0"). The age is an int subtraction
        // of server milliseconds, so the wrap and negative server times are both fine; a small negative age (the
        // Owner's clock read a moment ahead) still counts as held.
        public bool _WashHold()
        {
            if (!washSeen) return false;
            if (_released && washMs - _releaseMs <= 0) return false;   // no stroke after _ReleaseHold yet
            int age = Networking.GetServerTimeInMilliseconds() - washMs;
            return age > -1000 && age < Mathf.RoundToInt(holdSeconds * 1000f);
        }

        // The last washer's head (the dog looks there); the dog's own position when nobody is known.
        public Vector3 _WasherPosition()
        {
            int id = _WasherPlayerId();   // IMP-DOG-039: the synced holder when it washed clearly later than the local washer
            VRCPlayerApi p = id >= 0 ? VRCPlayerApi.GetPlayerById(id) : null;
            if (Utilities.IsValid(p)) return p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            return dogRoot != null ? dogRoot.position : transform.position;
        }

        public int _CleanSerial() { return cleanSerial; }

        // Part of the last stroke seen here (own or received); -1 before any.
        public int _LastStrokePart() { return _lastStrokePart; }

        // Harness read-outs (last evaluation: within 0.5 s of a cell change, IMP-DOG-034 B1a).
        public float _AverageDirt() { return _avgDirt; }
        public float _AverageFoam() { return _avgFoam; }
        public float _AverageWet() { return _avgWet; }

        // This client holds a state: its first receive arrived, or it seeded / took the state as the Owner.
        public bool _HasState() { return _gotFirst; }

        // IMP-DOG-028: the Owner's shake. Owner only (a non-Owner call does nothing); synced at once, the others see the
        // edge of _ShakeSerial(). Never repaints anything.
        public void _RequestShake()
        {
            if (!_ready || !Networking.IsOwner(gameObject)) return;
            shakeSerial = (shakeSerial + 1) & 0x3FFFFFFF;   // net: owner-only
            _syncDue = true;
            _nextSync = Mathf.Min(_nextSync, Time.time);
            Wake();   // IMP-DOG-034 B1e
            _PhotoEdges();   // IMP-WLD-011: the Owner's shake edge (the others see it in OnDeserialization)
        }

        public int _ShakeSerial() { return shakeSerial; }

        // ================= IMP-DOG-049 段 B: water play (design 2026-10-11 §3.3) =================
        // Which water tool hit: no tool ID is added (Q7: the wash effect stays the free shower's). The receiver looks at the tool
        // the sender holds - the same KH_WashTool list and _IsHeldBy that HoldsTool already checks for every remote stroke - and
        // reads its input mode (pulseSeconds, written per shown item by KH_WashSelection on every client): PulseHold (-1) = the hose,
        // > 0 one shot = the pistol, 0 = the free shower, PulseTilt (-2) = the watering can (tilt to pour, 2026-10-11): those two no
        // reaction. Nothing new goes over the wire.

        /// <summary>ReceiveStroke (every client, accepted shower-kind strokes, the own echo too): record a hit.</summary>
        void NoteWaterHit(int playerId, int part, Vector3 meshPoint)
        {
            float now = Time.time;
            _waterOnAt = now;
            if (part < 0 || part >= PartCount || ((WaterPlayParts >> part) & 1) == 0) return;
            int mode = WaterToolMode(playerId);
            if (mode != 0) RecordWaterHit(mode, part, MeshToWorld(part, meshPoint));
        }

        void RecordWaterHit(int mode, int part, Vector3 world)
        {
            float now = Time.time;
            if (mode == 1)
            {
                if (now - _hoseHitAt > WaterPlayHoseEnd) _hoseRunStart = now;   // a new run
                _hoseHitAt = now;
            }
            else
            {
                _pistolHitAt = now;
                _pistolHitSeq = (_pistolHitSeq + 1) & 0x3FFFFFFF;
            }
            _waterHitAt = now;
            _waterHitPart = part;
            _waterHitWorld = world;
        }

        /// <summary>The source of a shower-kind stroke from playerId: 1 = the hose (hold), 2 = the pistol (shot), 0 = anything else or
        /// unknown. Candidates = every shower-kind tool the player holds (the free shower (pulseSeconds 0) and the watering can
        /// (KH_WashTool.PulseTilt) included = mode 0). One candidate: the stroke can only come from it (it passed HoldsTool / is the own input), its mode. Several (a
        /// tool in each hand, or one just dropped within _IsHeldBy's grace): the one that is spraying now (KH_WashTool._SprayActive:
        /// the hold / toggle spraying, or a shot within its freshness) decides; none or more than one active = ambiguous = 0 (no
        /// reaction, Codex review 3: an unused hose in one hand must not turn the free shower's water into hose play).</summary>
        int WaterToolMode(int playerId)
        {
            if (tools == null) return 0;
            int held = 0, heldMode = 0, active = 0, activeMode = 0;
            for (int i = 0; i < tools.Length; i++)
            {
                KH_WashTool t = tools[i];
                if (t == null || t.tool != ToolShower || !t._IsHeldBy(playerId)) continue;
                // only the hose's hold is mode 1: the can's tilt input (PulseTilt, also < 0) stays 0 = no reaction (2026-10-11)
                int m = t.pulseSeconds == KH_WashTool.PulseHold ? 1 : (t.pulseSeconds > 0f ? 2 : 0);
                held++;
                heldMode = m;
                if (t._SprayActive()) { active++; activeMode = m; }
            }
            // 2026-10-11 Codex: with two water tools held, the synced spraying / shot state can lag the stroke and point at the wrong
            // tool (a pistol shot elsewhere + the free shower on the dog). Holding two is rare, so no reaction at all in that case.
            return held == 1 ? heldMode : 0;
        }

        /// <summary>Seconds since the last accepted shower-kind stroke on this dog (any tool, any part): "the water is on".</summary>
        public float _WaterOnAge() { return Time.time - _waterOnAt; }
        /// <summary>Seconds since the last hose or pistol hit on a reacting part (1e9-like when none).</summary>
        public float _WaterHitAge() { return Time.time - _waterHitAt; }
        /// <summary>The current hose run on the reacting parts: seconds from its first hit to its last, or -1 when the last hose hit
        /// is older than WaterPlayHoseEnd (the run is broken).</summary>
        public float _HoseRunSeconds() { return Time.time - _hoseHitAt <= WaterPlayHoseEnd ? _hoseHitAt - _hoseRunStart : -1f; }
        public int _PistolHitSeq() { return _pistolHitSeq; }
        public float _PistolHitAge() { return Time.time - _pistolHitAt; }
        public int _WaterHitPart() { return _waterHitPart; }
        public Vector3 _WaterHitWorld() { return _waterHitWorld; }

        /// <summary>The world centre of a part (the cells' centre through the nearest collider of the part); the dog root when the
        /// tables are missing. For the gaze of a reaction whose hit this client did not see.</summary>
        public Vector3 _PartWorld(int part)
        {
            if (!_ready || _partCenter == null || part < 0 || part >= PartCount || part >= _partCenter.Length)
                return dogRoot != null ? dogRoot.position : transform.position;
            return MeshToWorld(part, _partCenter[part]);
        }

        /// <summary>The synced water play: (serial << 6) | (part << 2) | kind. Read by the dog every frame on a wash stand.</summary>
        public int _WaterPlay() { return waterPlay; }

        /// <summary>IMP-DOG-049 段 B: the Owner's water play (kind 1 hose play, 2 pistol hop; part = the hit part). Owner only (a
        /// non-Owner call does nothing, like _RequestShake); synced at once. The dog's Owner decides (WaterPlayDecide); this object
        /// follows the dog root's Owner, so during a hand-over window the request may be dropped (one reaction lost).</summary>
        public void _RequestWaterPlay(int kind, int part)
        {
            if (!_ready || !Networking.IsOwner(gameObject) || kind < 1 || kind > 2) return;
            if (part < 0 || part >= PartCount) part = 0;
            int serial = ((waterPlay >> 6) + 1) & 0xFFFFFF;          // 24 bits: the packed value stays positive
            waterPlay = (serial << 6) | (part << 2) | kind;          // net: owner-only
            _syncDue = true;
            _nextSync = Mathf.Min(_nextSync, Time.time);
            Wake();   // IMP-DOG-034 B1e
        }

        /// <summary>Harness only: a water-tool hit as ReceiveStroke records it (mode 1 hose / 2 pistol) on a part, at the part's
        /// centre, without a held tool (ClientSim cannot hold the premium slot). Local; nothing in the world calls it.</summary>
        public void _DebugWaterHit(int mode, int part)
        {
            if (!_ready || mode < 1 || mode > 2 || part < 0 || part >= PartCount || _partCenter == null) return;
            _waterOnAt = Time.time;
            if (((WaterPlayParts >> part) & 1) != 0) RecordWaterHit(mode, part, MeshToWorld(part, _partCenter[part]));
        }

        // Prototype / harness: Owner only. A whole-dog reset bumps the epoch, so everyone repaints once.
        public void _DebugSetDirt(float value)
        {
            if (!_ready || !Networking.IsOwner(gameObject)) return;
            value = Mathf.Clamp01(value);
            _cellsDirty = true;   // IMP-DOG-034 B1a (BumpEpoch evaluates at once as well)
            for (int c = 0; c < CellCount; c++)
            {
                bool v = _cellsOk && cellValid[c];
                _dirt[c] = v ? value : 0f;
                _foam[c] = 0f;
                _wet[c] = 0f;
            }
            cleanArmed = value >= dirtyDirtThreshold;
            ClearArm();
            ClearBits(quietBits);
            riverBlocksEdge = false;
            BumpEpoch();
        }

        // The whole-dog reset: Owner only (a non-Owner call does nothing); everyone repaints once via the epoch.
        public void _DebugClear()
        {
            if (!_ready || !Networking.IsOwner(gameObject)) return;
            _cellsDirty = true;   // IMP-DOG-034 B1a
            for (int c = 0; c < CellCount; c++) { _dirt[c] = 0f; _foam[c] = 0f; _wet[c] = 0f; }
            cleanArmed = false;
            ClearArm();
            ClearBits(quietBits);
            riverBlocksEdge = false;
            BumpEpoch();
        }

        // IMP-DOG-034 B1a, harness only: the cells were written from outside in place (the same array, e.g. WrSetCells), which
        // the array-reference check cannot see. Evaluates on the next 0.5 s tick instead of within SafeEvalInterval. Local.
        public void _DebugCellsChanged()
        {
            _cellsDirty = true;
            Wake();
        }

        // IMP-DOG-034 B1a: a writer set the flag, or an array was replaced from outside (the harness's SetProgramVariable).
        bool CellsMayHaveChanged()
        {
            return _cellsDirty || _dirt != _evDirt || _foam != _evFoam || _wet != _evWet;
        }

        // IMP-DOG-034 B1e: run the next Update fully (an event that wants its periodic work soon: sync, evaluation).
        void Wake()
        {
            _idleNext = 0f;
        }

        // IMP-DOG-034 B1: the first time after `now` on the grid `period` shifted by phase x period (phase 0..1, wraps).
        float NextGrid(float now, float period, float phase)
        {
            return now + period - Mathf.Repeat(now - phase * period, period);
        }

        void BumpEpoch()
        {
            epoch = (epoch + 1) & 0x3FFFFFFF;
            if (epoch == 0) epoch = 1;
            _appliedEpoch = epoch;
            _gotFirst = true;
            DropInFlight();
            StopReplay();             // the Owner's own reset: nothing from before it is drawn again
            _restorePending = true;
            EvaluateState();
            _syncDue = true;
            _nextSync = Time.time;
        }

        // A reset makes every stroke made before it meaningless: drop what is waiting to be drawn or sent, and the
        // contact history, so nothing from before is re-applied after the reset.
        void DropInFlight()
        {
#if !UNITY_ANDROID && !UNITY_IOS
            PCFoamReset();
#endif
            _pdCount = 0;
            _qCount = 0;
            for (int t = 0; t < ChannelCount; t++) { _segOpen[t] = false; _segCarry[t] = false; _hValid[t] = false; }
            // IMP-DOG-039: the hands' speed history and the own-foam ring belong to the old state
            _hwValid[0] = false;
            _hwValid[1] = false;
            for (int i = 0; i < OwnFoamMax; i++) _ofAt[i] = -1e9f;
            // Dirt (IMP-DOG-029): the waiting draws, the unsent event and the Owner's carry / counts belong to the old state.
            DropDirtDraws();
            _dlCount = 0;
            _dirtOutPending = false;
            for (int p = 0; p < PartCount; p++) _dirtCarry[p] = 0f;
            _wetCarryLeg = 0f;
            _wetCarryBody = 0f;
            _walkAcc = 0f;
            _zoneAcc = 0f;
            _waterAcc = 0f;
            _swimAcc = 0f;
            _runoffOn = false;   // the repaint replaces the mask; nothing rinsed before the reset runs off after it
            if (_cellShowered != null) for (int c = 0; c < _cellShowered.Length; c++) _cellShowered[c] = false;
        }

        // ================= Update =================

        void Update()
        {
            if (!_ready) return;
            float now = Time.time;

            // IMP-DOG-034 B1e: idle (no stroke within BusyHold, nothing waiting to be drawn / replayed / repainted / sent, no
            // sound fading) = only the field checks below every frame; the periodic work runs on this dog's IdleInterval grid.
            // A stroke sets _busyUntil; a received dirt draw / repaint sets its own flag here; other events call Wake().
            // An open segment always comes with a stroke within SegmentGap (< BusyHold), so _segOpen needs no check.
            if (now < _idleNext && now >= _busyUntil && _pdCount == 0 && _qCount == 0 && !_anyDirtDraw && !_restorePending
                && !_replaying && !_replayPending && !_dirtOutPending && !_audioOn && !_syncDue   // _syncDue: a send waiting for its time goes out on time
                && !_runoffOn   // the runoff runs on its own runoffInterval schedule (it outlasts BusyHold)
                && _dfCount == 0) return;   // 2026-10-07 audit RC-7 (a): deferred strokes are re-asked every frame
            if (now >= _idleNext) _idleNext = NextGrid(now, IdleInterval, _phase);

            // Lost RenderTexture (device reset, app resume) or a long real-time gap: repaint from the cells.
            float real = Time.realtimeSinceStartup;
            bool gap = real - _lastRealtime > 5f;
            _lastRealtime = real;
            if (now >= _nextRtCheck || gap)
            {
                _nextRtCheck = now + 0.5f;
                if (EnsureCreated() || gap) _restorePending = true;
            }
            if (_restorePending && CanBlit())
            {
                bool restored = Restore();
                // Round 2: a joiner (or any repaint) during someone's runoff window: the repainted mask holds the cells as they
                // were sent, so the same erosion runs once more from there (one window). Only when the cells hold foam next to a
                // rinsed cell; nothing here sets _restorePending again, so this cannot loop.
                if (restored && CellsHaveRunoff()) MarkRunoff();
                if (_replayPending)
                {
                    _replayPending = false;
                    if (restored) { _replaying = true; _rpIndex = 0; _rpLeft = -1f; }
                    else StopReplay();
                }
            }
            DrainDeferred();   // 2026-10-07 audit RC-7 (a): before the draws, so a stroke taken now is drawn this frame
            ReplayStep();
            DrainPending();
            DrainDirt();
            RunoffTick(now);   // after the draws: it takes only the Blit budget they leave

            // Getting dirty (IMP-DOG-029): the Owner samples the dog once a second (IMP-DOG-034 B1d) and applies every
            // dirtApplyInterval.
            if (Networking.IsOwner(gameObject)) DirtTick(now);
            else _dirtPrevValid = false;

            UpdateSend(now);
            UpdateAudio(now);

            if (now >= _nextDecay && CanBlit())
            {
                // IMP-DOG-034 B1c: checked on a 2 s grid (this dog's phase); runs every 4 s, or every 2 s while a stroke is
                // within holdSeconds + 2 s (the washer is looking at it). The step is the time since the last run, so the
                // drying rate is the same as the old 2 s / Decay(2f).
                _nextDecay = NextGrid(now, DecayWashInterval, _phase);
                float every = now - _lastStrokeTime < holdSeconds + 2f ? DecayWashInterval : DecayInterval;
                if (now - _lastDecayAt >= every - 0.25f)
                {
                    // Capped at two periods: a longer gap (Blit budget full, a hitch) dries only that much, as the old fixed Decay(2f)
                    // per tick also dropped the time it did not run.
                    float dt = Mathf.Clamp(now - _lastDecayAt, 0f, 2f * DecayInterval);
                    _lastDecayAt = now;
                    Decay(dt);
                }
            }
            if (now >= _nextEval)
            {
                // IMP-DOG-034 B1a: the cell loop only when the cells may have changed (or the arrays were replaced, or the
                // SafeEvalInterval fallback is due); otherwise only the time-dependent shell switch.
                _nextEval = NextGrid(now, EvalInterval, _phase);
                if (CellsMayHaveChanged() || now >= _nextSafeEval) EvaluateState();
                else EvaluateTime(now);
            }

            if (now >= _nextOwnerCheck)
            {
                _nextOwnerCheck = now + 2f;
                FollowDogOwner();
            }
            if (_syncDue && now >= _nextSync && Networking.IsOwner(gameObject))
            {
                _syncDue = false;
                _nextSync = now + syncInterval;
                RequestSerialization();
            }
        }

        // Only the client that owns the dog root asks, only after it has owned the root for two checks in a row (a
        // transfer in flight, or two clients briefly both believing they own it, does not start a tug of war), and at
        // most once per 6 s (repeated SetOwner calls in a short time fight and roll back, vrc-networking §4). When the
        // Owner leaves, VRChat hands this object to someone at once (OnOwnershipTransferred); this only corrects it
        // toward the dog's Owner afterwards.
        void FollowDogOwner()
        {
            if (dogRoot == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (!Networking.IsOwner(dogRoot.gameObject) || Networking.IsOwner(gameObject)) { _dogOwnerStreak = 0; return; }
            _dogOwnerStreak++;
            if (_dogOwnerStreak < 2) return;
            float now = Time.time;
            if (now - _ownerAskAt < 6f) return;
            _ownerAskAt = now;
            Networking.SetOwner(lp, gameObject); // net: setowner-ok: wash state only, never the dog; follows the dog root's Owner (DES-DOG-006 §1-4)
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            int id = player.playerId;
            if (_lastWasherId == id) _lastWasherId = -1;
            for (int i = 0; i < SenderSlots; i++) if (_sndId[i] == id) { _sndId[i] = -1; _sndAt[i] = -1000f; _sndLast[i] = -1e9f; _relValid[i] = false; }
            // Ownership of this and of the dog root may both move now: look again soon.
            if (_ready) _nextOwnerCheck = Mathf.Min(_nextOwnerCheck, Time.time + 1f);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // The new Owner continues from the cells it last received, plus the recorded strokes of this epoch those
            // cells do not hold yet (appliedSeqs came with them), so strokes accepted between that receive and the
            // transfer are not lost. washMs is server time, the same clock for every Owner, so the hold carries over.
            // Its own mask is kept: a repaint here would wipe the fine band it is looking at for a coarse copy of the
            // same state, and the others see only a normal receive (no repaint) from it.
            // Dirt events (review 3, 1-1): the previous Owner's events in flight are still accepted for a short time.
            if (Utilities.IsValid(player) && player.playerId != _ownerId)
            {
                _prevOwnerId = _ownerId;
                _prevOwnerUntil = Time.time + PrevOwnerGrace;
                _ownerId = player.playerId;
            }
            Wake();   // IMP-DOG-034 B1e: the Owner's periodic work (dirt, sync) from the next frame
            if (!Networking.IsOwner(gameObject)) return;
            EnsureArrays();
            Reintegrate();
            ReintegrateDirt();
            _dirtTakeoverSeq = dirtSeq;   // 2026-10-07 audit RC-7 (b): what the cells hold at the takeover
            dirtSeq = (dirtSeq + TakeoverSeqGap) & 0x3FFFFFFF;   // net: owner-only. Codex review A-2: our numbers start past the previous Owner's in-flight ones
            SeedShoweredFromCells();   // round 2: the runoff's showered cells, and a window in progress continues here
            for (int i = 0; i < AppliedSlots; i++) _apAt[i] = Time.time;
            if (!_gotFirst)
            {
                // Became the Owner before anything arrived (a joiner whose Owner left at once): the seed and the own
                // strokes on it (re-integrated just now, epoch 0) are all this client has. A fresh epoch makes
                // everyone else repaint to it, so no client mixes this seed into another state; the own band stays.
                if (epoch == 0) epoch = FreshEpoch();
                _appliedEpoch = epoch;
                StopReplay();
            }
            else if (epoch == 0) { epoch = FreshEpoch(); _appliedEpoch = epoch; }
            _gotFirst = true;
            _syncDue = true;
            _nextSync = Time.time + 1f;
            if (_ready) EvaluateState();
        }

        public override void OnPreSerialization()
        {
            if (_dirt == null) return;
            if (cells == null || cells.Length != CellCount) cells = new byte[CellCount];
            for (int c = 0; c < CellCount; c++) cells[c] = (byte)CellByte(c);
        }

        // One cell as it is sent (OnPreSerialization) and as IMP-DOG-034 B1b compares it with the last sent byte.
        int CellByte(int c)
        {
            int d = Mathf.Clamp(Mathf.RoundToInt(_dirt[c] * 3f), 0, 3);
            return d | (Q7(_foam[c]) << 2) | (Q7(_wet[c]) << 5);
        }

        int Q7(float x)
        {
            return Mathf.Clamp(Mathf.RoundToInt(x * 7f), 0, 7);
        }

        public override void OnDeserialization()
        {
            EnsureArrays();
            if (cells == null || cells.Length != CellCount) return;
            _cellsDirty = true;   // IMP-DOG-034 B1a: the received cells replace the floats
            Wake();               // IMP-DOG-034 B1e
            for (int c = 0; c < CellCount; c++)
            {
                int v = cells[c];
                _dirt[c] = (v & 3) / 3f;
                _foam[c] = ((v >> 2) & 7) / 7f;
                _wet[c] = ((v >> 5) & 7) / 7f;
            }
            EnsureSyncedArrays();
            // Repaint only on a joiner's first receive and after a reset; a normal receive (and a former Owner's
            // first receive: it already holds the state) leaves the mask alone.
            bool first = !_gotFirst;
            bool reset = epoch != _appliedEpoch;
            bool repaint = first || reset;
            if (reset && !first)
            {
                // Everything made before the reset is meaningless. A joiner's first receive keeps its unsent
                // segments instead: they go out with the epoch of the time they are sent, and 0-epoch ones count.
                DropInFlight();
                StopReplay();
            }
            if (repaint)
            {
                _restorePending = true;
                if (first)
                {
                    // The own draws so far are all in the segments (sent = logged, queued, open). The Restore wipes
                    // them; the replay list puts back only what the received cells do not hold. The waiting draws
                    // are part of those segments, so they are dropped here (drawing them too would double them).
                    _pdCount = 0;
                    BuildReplay();
                }
                else _pdCount = 0;
            }
            _gotFirst = true;
            _appliedEpoch = epoch;
            if (_ready && repaint) EvaluateState();
            else if (_ready) _PhotoEdges();   // IMP-WLD-011: the clean / shake edges as they arrive (EvaluateState already calls it)
            // Before Start: Start sees _gotFirst, keeps these values and repaints from them (the log is still empty).
        }

        // ================= Mask =================

        bool CanBlit()
        {
            int f = Time.frameCount;
            if (f != _blitFrame) { _blitFrame = f; _blitCount = 0; }
            return _blitCount < maxBlitsPerFrame;
        }

        // Draw now when the budget allows and nothing is waiting (order is kept). Otherwise wait: merged into the
        // newest waiting draw only when it is the same tool and part (never turns foam into a rinse), else a new
        // entry. When all PendMax entries are taken the newest same-tool-and-part entry takes it; only a full queue
        // of other kinds drops the oldest (the budget drains 4 per frame, so this needs > 8 distinct kinds at once).
        void QueueOrDraw(int tool, int part, Vector3 a, Vector3 b, float rad, float sec)
        {
            DrainPending();
            // While a repaint is due the draw waits: the Restore would wipe it (it runs first in the next Update).
            if (_pdCount == 0 && !_restorePending && !_replaying && !_replayPending && CanBlit()) { DrawBrush(tool, part, a, b, rad, sec); return; }
            // IMP-DOG-039 §13.4: a hand's draws are never merged. A received hand stroke has no left / right, so two of the same
            // part may be the two hands far apart; merging would draw one capsule across the body between them. The bottle's
            // doses are dots (KH_WashTool closes each one) and are not merged into a line either.
            bool canMerge = tool != ToolHand && tool != ToolBottle;
            int k = _pdCount - 1;
            if (canMerge && k >= 0 && _pdTool[k] == tool && _pdPart[k] == part) { MergePending(k, b, rad, sec); return; }
            if (_pdCount >= PendMax)
            {
                for (int i = PendMax - 1; i >= 0 && canMerge; i--)
                {
                    if (_pdTool[i] == tool && _pdPart[i] == part) { MergePending(i, b, rad, sec); return; }
                }
                for (int i = 1; i < PendMax; i++) CopyPending(i, i - 1);
                _pdCount = PendMax - 1;
            }
            k = _pdCount;
            _pdTool[k] = tool;
            _pdPart[k] = part;
            _pdA[k] = a;
            _pdB[k] = b;
            _pdRad[k] = rad;
            _pdSec[k] = sec;
            _pdCount++;
        }

        void MergePending(int k, Vector3 b, float rad, float sec)
        {
            _pdB[k] = b;                     // the capsule grows; Dwell() thins the amount over the longer path
            _pdSec[k] += sec;
            _pdRad[k] = Mathf.Max(_pdRad[k], rad);
        }

        void CopyPending(int from, int to)
        {
            _pdTool[to] = _pdTool[from];
            _pdPart[to] = _pdPart[from];
            _pdA[to] = _pdA[from];
            _pdB[to] = _pdB[from];
            _pdRad[to] = _pdRad[from];
            _pdSec[to] = _pdSec[from];
        }

        // One Blit takes at most MaxBlitSeconds of an entry; the rest stays at the head for the next Blit / frame.
        void DrainPending()
        {
            if (_restorePending || _replaying || _replayPending) return;
            while (_pdCount > 0 && CanBlit())
            {
                float s = Mathf.Min(_pdSec[0], MaxBlitSeconds);
                DrawBrush(_pdTool[0], _pdPart[0], _pdA[0], _pdB[0], _pdRad[0], s);
                _pdSec[0] -= s;
                if (_pdSec[0] > 0.0005f) continue;
                for (int i = 1; i < _pdCount; i++) CopyPending(i, i - 1);
                _pdCount--;
            }
        }

        // sec * 2r / (|b - a| + 2r): see the header. radMesh and the points in mesh units.
        float Dwell(float sec, Vector3 a, Vector3 b, float radMesh)
        {
            float twoR = 2f * Mathf.Max(radMesh, 1e-6f);
            return sec * twoR / ((b - a).magnitude + twoR);
        }

        void DrawBrush(int tool, int part, Vector3 a, Vector3 b, float radMeters, float sec)
        {
            _blitCount++;
            if (_maskMat == null || _cur == null || _next == null) return;
            float r = radMeters / meshToWorldScale;
            _maskMat.SetVector(_idBrushA, new Vector4(a.x, a.y, a.z, 1f));
            _maskMat.SetVector(_idBrushB, new Vector4(b.x, b.y, b.z, 1f));
            _maskMat.SetFloat(_idBrushRadius, r);
            _maskMat.SetFloat(_idBrushSoft, brushSoft);
            _maskMat.SetVector(_idDelta, ToolDelta(tool, Dwell(sec, a, b, r)));
            bool shower = tool == ToolShower;
            _maskMat.SetFloat(_idRinseFoamBoost, rinseFoamBoost);   // the shader only boosts a removal
            _maskMat.SetFloat(_idFoamRinseGate, shower ? foamRinseGate : 0f);
            _maskMat.SetFloat(_idEdgeNoise, edgeNoise);
            _maskMat.SetFloat(_idRinseReach, shower ? Mathf.Max(1f, rinseReach) : TowelFoamReach);
            // The shower has no part gate (2026-10-02): water aimed at the shoulder hits the neck / hip collider there, and
            // the shoulder texels of the leg are not that part's neighbours. The sponge / towel keep the neighbour gate.
            SetAllow(shower ? AllowAllKey : part);
            SetLather(tool == ToolHand);
            VRCGraphics.Blit(_cur, _next, _maskMat, PassBrush);
            Swap();
#if !UNITY_ANDROID && !UNITY_IOS
            PCFoamTouch(tool);
#endif
        }

        // IMP-DOG-039 §3.4: the Brush pass's lather gate - on for the bare hand only (x = handFoamMin, y = handSpreadStep texels,
        // z = 1, w = handSpreadReach in mesh units), z = 0 for every other Brush (the tools, the dirt draw: unchanged output).
        // Written only when it changes (like _allowFor).
        void SetLather(bool on)
        {
            if (!on)
            {
                if (!_latherOn) return;
                _latherOn = false;
                _maskMat.SetVector(_idLatherGate, Vector4.zero);
                return;
            }
            Vector4 v = new Vector4(handFoamMin, Mathf.Max(1f, (float)Mathf.RoundToInt(handSpreadStep)), 1f,
                Mathf.Max(0f, handSpreadReach) / Mathf.Max(1e-4f, meshToWorldScale));
            if (_latherOn && v.x == _latherVec.x && v.y == _latherVec.y && v.w == _latherVec.w) return;
            _latherOn = true;
            _latherVec = v;
            _maskMat.SetVector(_idLatherGate, v);
        }

        // key = a part (that part + its partNeighbors) or AllowAllKey (every part). Cached per key (_allowFor).
        void SetAllow(int key)
        {
            if (key == _allowFor) return;
            _allowFor = key;
            if (key == AllowAllKey)
            {
                for (int i = 0; i < AllowSlots; i++) _allow[i] = 1f;
            }
            else
            {
                int mask = partNeighbors != null && key >= 0 && key < partNeighbors.Length ? partNeighbors[key] : 0;
                for (int i = 0; i < AllowSlots; i++) _allow[i] = (i == key || (i < PartCount && ((mask >> i) & 1) != 0)) ? 1f : 0f;
            }
            _maskMat.SetFloatArray(_idAllowPart, _allow);
        }

        // Whole mask from the cells (first receive, reset, lost texture, dried out). Restore writes every texel, so
        // the undefined content of a fresh RenderTexture does not matter. Counted in the Blit budget.
        bool Restore()
        {
            _restorePending = false;
            // Dirt (review 3, 1-1): the mask becomes the cells, which hold the dirt events up to dirtSeq. The waiting draws are
            // dropped and the logged events newer than dirtSeq (received but not in these cells yet) are queued again.
            DropDirtDraws();
            _dirtMaskSeq = dirtSeq;
            for (int i = 0; i < _dlCount; i++)
                if (_dlEpoch[i] == epoch && SeqNewer(_dlSeq[i], dirtSeq)) QueueDirtFields(_dlFrom[i], _dlTo[i], _dlAmount[i] & 1023, (_dlAmount[i] >> 10) & 1023);
            _blitCount++;
            if (_maskMat == null || _cur == null || _next == null || cellTex == null) return false;
#if !UNITY_ANDROID && !UNITY_IOS
            bool pcHasFoam = false;
#endif
            for (int c = 0; c < CellCount; c++)
            {
                _cellColors[c] = new Color(_dirt[c], _foam[c], _wet[c], 1f);
#if !UNITY_ANDROID && !UNITY_IOS
                if (_foam[c] > 0f) pcHasFoam = true;
#endif
            }
            cellTex.SetPixels(_cellColors);
            cellTex.Apply(false);
            _maskMat.SetTexture(_idCellTex, cellTex);
            VRCGraphics.Blit(_cur, _next, _maskMat, PassRestore);
            Swap();
#if !UNITY_ANDROID && !UNITY_IOS
            PCFoamRestored(pcHasFoam);
#endif
            _restoreCount++;
            if (_replaying) { _rpIndex = 0; _rpLeft = -1f; }   // the partly replayed band was just wiped
            return true;
        }

        // Joiner, before the first receive: a segment it has just sent (UpdateSend). Only the newest LogSentMax are kept.
        void LogSent(int tool, int part, int fromPacked, int toPacked, int ms, int rmm, int seq)
        {
            if (_lgCount >= LogSentMax)
            {
                for (int i = 1; i < _lgCount; i++) CopyLog(i, i - 1);
                _lgCount--;
            }
            AddLog(tool, part, Unpack(part, fromPacked), Unpack(part, toPacked), rmm * 0.001f, ms * 0.001f, seq);
        }

        void AddLog(int tool, int part, Vector3 a, Vector3 b, float rad, float sec, int seq)
        {
            if (_lgCount >= LogMax || !(sec > 0f)) return;
            int k = _lgCount;
            _lgTool[k] = tool;
            _lgPart[k] = part;
            _lgSeq[k] = seq;
            _lgA[k] = a;
            _lgB[k] = b;
            _lgRad[k] = rad;
            _lgSec[k] = sec;
            _lgCount++;
        }

        void CopyLog(int from, int to)
        {
            _lgTool[to] = _lgTool[from];
            _lgPart[to] = _lgPart[from];
            _lgSeq[to] = _lgSeq[from];
            _lgA[to] = _lgA[from];
            _lgB[to] = _lgB[from];
            _lgRad[to] = _lgRad[from];
            _lgSec[to] = _lgSec[from];
        }

        // First receive: the log becomes the replay list. Sent segments the received cells already hold (seq not
        // newer than appliedSeqs[own]) are removed; the queued and open segments (not sent, so certainly not held) are
        // appended. Their draws were all made before this moment; later draws wait in the pending queue.
        void BuildReplay()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            int own = Utilities.IsValid(lp) ? lp.playerId : -1;
            int n = 0;
            for (int i = 0; i < _lgCount; i++)
            {
                if (!NotApplied(own, _lgSeq[i])) continue;
                if (n != i) CopyLog(i, n);
                n++;
            }
            _lgCount = n;
            // IMP-DOG-039: the queue and the open segments are per channel; the log holds the kind (ChannelTool, once, here)
            for (int i = 0; i < _qCount; i++)
                AddLog(ChannelTool(_qCh[i]), _qPart[i], Unpack(_qPart[i], _qFrom[i]), Unpack(_qPart[i], _qTo[i]), _qRmm[i] * 0.001f, _qMs[i] * 0.001f, -1);
            for (int t = 0; t < ChannelCount; t++)
                if (_segOpen[t]) AddLog(ChannelTool(t), _segPart[t], _segFrom[t], _segTo[t], _segRad[t], _segSec[t], -1);
            _replayPending = _lgCount > 0;
            _replaying = false;
        }

        // After the Restore: the replay list in order, inside the normal per-frame Blit budget, over as many frames as
        // it takes (new draws wait in the pending queue meanwhile). A Restore during it starts it over.
        void ReplayStep()
        {
            if (!_replaying) return;
            while (_rpIndex < _lgCount && CanBlit())
            {
                if (_rpLeft < 0f) _rpLeft = _lgSec[_rpIndex];
                float s = Mathf.Min(_rpLeft, MaxBlitSeconds);
                DrawBrush(_lgTool[_rpIndex], _lgPart[_rpIndex], _lgA[_rpIndex], _lgB[_rpIndex], _lgRad[_rpIndex], s);
                _rpLeft -= s;
                if (_rpLeft > 0.0005f) continue;
                _replayCount++;
                _rpIndex++;
                _rpLeft = -1f;
            }
            if (_rpIndex >= _lgCount) StopReplay();
        }

        void StopReplay()
        {
            _replaying = false;
            _replayPending = false;
            _lgCount = 0;
            _rpIndex = 0;
            _rpLeft = -1f;
        }

        // Drying keeps running until the mask can hold nothing more: while the cells have foam / wet, and for a full
        // drying time after the last stroke or the last foam / wet the cells saw (the mask's band can be stronger
        // than the coarse cells report). Counted in the Blit budget.
        void Decay(float dt)
        {
            // IMP-DOG-034 B1c: _anyFoamOrWet must describe the cells as they are now (the skips below depend on it). The
            // evaluation was due within 0.5 s anyway; running it here clears the flag, so it is not repeated.
            if (CellsMayHaveChanged()) EvaluateState();
            float now = Time.time;
            float drain = Mathf.Max(foamDrySeconds, wetDrySeconds);
            // IMP-DOG-034 B1c: _lastWetActivity (not _lastActivity): a dirt-only change adds no foam / wet to the mask.
            if (!_anyFoamOrWet && now - _lastWetActivity > drain)
            {
                // Drying stops here. The mask's band can be stronger than the coarse cells, and the drying time may
                // have been shortened meanwhile (the rate and the window no longer match), so the last pass takes all
                // foam and wet off the mask once: the cells are 0, nothing can be left on it.
                if (!_dryFinished && _maskMat != null && _cur != null && _next != null)
                {
                    _dryFinished = true;
                    _blitCount++;
                    _maskMat.SetVector(_idDecay, new Vector4(0f, 1f, 1f, 0f));
                    VRCGraphics.Blit(_cur, _next, _maskMat, PassDecay);
                    Swap();
#if !UNITY_ANDROID && !UNITY_IOS
                    PCFoamRestored(false);
#endif
                }
                return;
            }
            _dryFinished = false;
            // Foam does not dry while the dog is being washed. _WashHold() alone lags on a non-Owner by the sync (the
            // washer's own foam would melt at the start of the wash), so a stroke seen here counts as well. The same
            // fd / wd go to the mask and the cells, and both clamp at 0 (never negative, however long it runs).
            bool washing = _WashHold() || (now - _lastStrokeTime < holdSeconds && _lastStrokeTime > _releaseTime + ReleaseGrace);
            float fd = washing ? 0f : dt / Mathf.Max(1f, foamDrySeconds);
            float wd = dt / Mathf.Max(1f, wetDrySeconds);
            if (_maskMat != null && _cur != null && _next != null)
            {
                _blitCount++;
                _maskMat.SetVector(_idDecay, new Vector4(0f, fd, wd, 0f));
                VRCGraphics.Blit(_cur, _next, _maskMat, PassDecay);
                Swap();
            }
            // IMP-DOG-034 B1c: no foam / wet in the cells = nothing in them can dry (only the mask's band, Blit above).
            if (!_anyFoamOrWet || _validCells == null) return;
            // Only the counted cells (_validCells; the others are never written above 0 at run time). A zero cell stays 0, so
            // it is skipped (Max(0, 0 - fd) = 0 as before).
            // IMP-DOG-034 B1b: the Owner syncs only when a sent value (3 bit foam / wet) changed; each other writer of the
            // cells sets _syncDue itself, so "changed since the last send" is the same as "changed in some write".
            bool owner = Networking.IsOwner(gameObject);
            bool changed = false;
            bool qChanged = false;
            int nv = _validCells.Length;
            for (int k = 0; k < nv; k++)
            {
                int c = _validCells[k];
                float f0 = _foam[c];
                float w0 = _wet[c];
                if (!(f0 > 0f) && !(w0 > 0f)) continue;
                changed = true;
                float f1 = Mathf.Max(0f, f0 - fd);
                float w1 = Mathf.Max(0f, w0 - wd);
                _foam[c] = f1;
                _wet[c] = w1;
                if (owner && !qChanged && (Q7(f1) != Q7(f0) || Q7(w1) != Q7(w0))) qChanged = true;
            }
            if (changed) _cellsDirty = true;   // IMP-DOG-034 B1a
            if (qChanged) _syncDue = true;
        }

        // ---- Runoff (2026-10-02, IMP-DOG-025 user request: "the shower washes the foam away quickly"; no aiming at every
        // small leftover). Leftover foam next to a rinsed area flows away by itself: an accepted shower stroke (own or
        // received, on every client) starts it; the mask's Runoff pass (KH_WashMask pass 3) runs every runoffInterval until
        // runoffSeconds after the last shower stroke, and the Owner erodes its cells the same way. The cells are the synced
        // truth: a late joiner's Restore holds the Owner's erosion up to the snapshot it received, and the Restore starts one
        // window of its own so the rest flows away on its mask too. No synced variable and no event is added.

        void MarkRunoff()
        {
            if (!(runoffSeconds > 0f) || !(runoffRate > 0f)) return;
            float now = Time.time;
            if (!_runoffOn)
            {
                _runoffOn = true;
                _runoffLast = now;
                _runoffNext = now + Mathf.Max(0.02f, runoffInterval);
            }
            _runoffUntil = now + runoffSeconds;
        }

        void RunoffTick(float now)
        {
            if (!_runoffOn || now < _runoffNext) return;
            if (!(runoffSeconds > 0f) || !(runoffRate > 0f)) { _runoffOn = false; return; }
            // A repaint / replay first (it would wipe the pass, and a band being put back must not be eroded half-drawn), and
            // only inside the Blit budget. The time waits meanwhile, up to MaxRunoffStep.
            if (_restorePending || _replaying || _replayPending || !CanBlit()) return;
            float dt = Mathf.Clamp(now - _runoffLast, 0f, MaxRunoffStep);
            _runoffLast = now;
            _runoffNext = now + Mathf.Max(0.02f, runoffInterval);
            if (now >= _runoffUntil) _runoffOn = false;   // the last pass
            float amount = runoffRate * dt;
            if (!(amount > 0f)) return;
            DrawRunoff(amount);
            // The cells (Owner) take runoffCellGain x the amount. Round 2: the cells erode only one ring around the showered
            // cells (no chain), i.e. about one cell (~16 texels) per window, while the mask's front advances up to runoffStep
            // texels per pass (~30 per window) through wet texels. Gain 1 keeps the ring cell's rate equal to the mask's, so the
            // truth neither runs ahead of what the mask shows (a clean edge with foam in view) nor lags it much.
            if (Networking.IsOwner(gameObject)) RunoffCells(amount * Mathf.Max(0f, runoffCellGain));
        }

        // The cells hold foam somewhere and a rinsed cell (wet >= 0.5, foam < 0.1) somewhere: a runoff window could change something.
        bool CellsHaveRunoff()
        {
            if (_dirt == null || _validCells == null) return false;
            bool rinsed = false;
            bool foam = false;
            for (int k = 0; k < _validCells.Length; k++)
            {
                int c = _validCells[k];
                if (_wet[c] >= 0.5f && _foam[c] < 0.1f) rinsed = true;
                if (_foam[c] > 0f) foam = true;
                if (rinsed && foam) return true;
            }
            return false;
        }

        // Round 2, a new Owner: it did not see which cells the shower rinsed, so it takes the cells that are rinsed now (an
        // eroded cell with wet >= 0.5 counts once here: at most one extra ring, once per handover), and continues a window
        // that may be in progress at the handover (one window; nothing to erode = nothing changes).
        void SeedShoweredFromCells()
        {
            EnsureArrays();
            if (_validCells == null) return;
            for (int c = 0; c < CellCount; c++) _cellShowered[c] = false;
            for (int k = 0; k < _validCells.Length; k++)
            {
                int c = _validCells[k];
                if (_wet[c] >= 0.5f && _foam[c] < 0.1f) _cellShowered[c] = true;
            }
            if (CellsHaveRunoff()) MarkRunoff();
        }

        // The mask: foam with rinsed neighbours (+-runoffStep texels, 8 directions) loses amount x n / 8. Counted in the Blit budget.
        void DrawRunoff(float amount)
        {
            _blitCount++;
            _runoffPasses++;
            if (_maskMat == null || _cur == null || _next == null) return;
            _maskMat.SetFloat(_idRunoffStep, Mathf.Max(1f, (float)Mathf.RoundToInt(runoffStep)));
            _maskMat.SetFloat(_idRunoffAmount, amount);
            VRCGraphics.Blit(_cur, _next, _maskMat, PassRunoff);
            Swap();
        }

        // The cells (Owner): the same rule on the 16 x 16 grid (cell y * 16 + x, the cellTex layout), the 8 grid neighbours.
        // A neighbour counts when it is valid, was rinsed by a shower stroke (_cellShowered) and is still rinsed (W >= 0.5 &&
        // F < 0.1) BEFORE this pass (_rinsed is the snapshot), so the result does not depend on the loop order and an eroded
        // cell never passes the erosion on. Dirt follows the foam's ratio (never increases).
        void RunoffCells(float amount)
        {
            if (!_cellsOk || _validCells == null || !(amount > 0f)) return;
            _runoffCellPasses++;
            // Only the valid cells are ever set (validity does not change at run time), so the others stay false.
            if (_rinsed == null || _rinsed.Length != CellCount) _rinsed = new bool[CellCount];
            EnsureArrays();   // _cellShowered
            int nv = _validCells.Length;
            bool any = false;
            for (int k = 0; k < nv; k++)
            {
                int c = _validCells[k];
                // round 2: only a cell a shower stroke rinsed counts (an eroded cell does not: no chain across the grid)
                bool r = _cellShowered[c] && _wet[c] >= 0.5f && _foam[c] < 0.1f;
                _rinsed[c] = r;
                if (r) any = true;
            }
            if (!any) return;
            float per = amount / 8f;
            bool changed = false;
            bool qChanged = false;
            for (int k = 0; k < nv; k++)
            {
                int c = _validCells[k];
                float f0 = _foam[c];
                if (!(f0 > 0f)) continue;
                int x = c & 15;
                int y = c >> 4;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy > 15) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if ((dx == 0 && dy == 0) || xx < 0 || xx > 15) continue;
                        if (_rinsed[(yy << 4) | xx]) n++;
                    }
                }
                if (n == 0) continue;
                float f1 = Mathf.Max(0f, f0 - per * n);
                float d0 = _dirt[c];
                float d1 = Mathf.Min(d0, d0 * f1 / Mathf.Max(f0, 1e-4f));
                _foam[c] = f1;
                _dirt[c] = d1;
                changed = true;
                // IMP-DOG-034 B1b: sync only when a sent value (3 bit foam, 2 bit dirt) changed
                if (!qChanged && (Q7(f1) != Q7(f0) || Mathf.RoundToInt(d1 * 3f) != Mathf.RoundToInt(d0 * 3f))) qChanged = true;
            }
            if (changed) _cellsDirty = true;   // IMP-DOG-034 B1a
            if (qChanged) _syncDue = true;
        }

        void Swap()
        {
            RenderTexture t = _cur;
            _cur = _next;
            _next = t;
            if (_shellMat != null) _shellMat.SetTexture(_idMask, _cur);
#if !UNITY_ANDROID && !UNITY_IOS
            PCFoamBind();
#endif
        }

        bool EnsureCreated()
        {
            bool made = false;
            if (maskA != null && !maskA.IsCreated()) { maskA.Create(); made = true; }
            if (maskB != null && !maskB.IsCreated()) { maskB.Create(); made = true; }
            return made;
        }

        void ShowShell(bool on)
        {
            _shellOn = on;
            if (shellRenderer != null && shellRenderer.enabled != on) shellRenderer.enabled = on;
        }

        // ================= Cells =================

        float ToolRadius(int tool)
        {
            if (tool == ToolShower) return showerRadius;
            if (tool == ToolTowel) return towelRadius;
            if (tool == ToolDryer) return .10f;
            if (tool == ToolBottle) return bottleRadius;   // IMP-DOG-039
            if (tool == ToolHand) return handRadius;
            return spongeRadius;
        }

        // What a stroke of each kind may claim (the shower widens with distance up to 0.2 m, KH_WashTool). The sponge / towel
        // limit follows their radius (spongeRadius 0.11 -> 0.121 m = 121 mm in NetStroke's check; the sender clamps to it too).
        float MaxToolRadius(int tool)
        {
            if (tool == ToolShower || tool == ToolDryer) return 0.2f;
            return Mathf.Clamp(ToolRadius(tool) * 1.1f, 0.02f, 0.25f);
        }

        Vector4 ToolDelta(int tool, float sec)
        {
            if (tool == ToolDryer) return new Vector4(0f, 0f, -0.32f * sec, 0f);
            if (tool == ToolShower) return new Vector4(showerDirtRate * sec, showerFoamRate * sec, showerWetRate * sec, 0f);
            if (tool == ToolTowel) return new Vector4(towelDirtRate * sec, towelFoamRate * sec, towelWetRate * sec, 0f);
            // IMP-DOG-039: shampoo and lathering add foam only (no water, no dirt change); the hand's foam is gated by the
            // foam already there (the mask's _LatherGate, the cells' gate in ApplyToCells)
            if (tool == ToolBottle) return new Vector4(0f, bottleFoamRate * sec, 0f, 0f);
            if (tool == ToolHand) return new Vector4(0f, handFoamRate * sec, 0f, 0f);
            return new Vector4(0f, spongeFoamRate * sec, spongeWetRate * sec, 0f);
        }

        // The cell version of the Brush pass. Cells are coarser than the band, so every cell within the brush plus
        // cellReach takes the brush's full weight (cellGain tunes it against the mask in Play); the amount is the
        // same Dwell() as the mask's and the reaction is the shader's (ApplyCell).
        // Shower (2026-10-02): every part (no part gate, as on the mask), and the foam part of the delta reaches out to
        // (r x rinseReach + cellReach) like wOut on the mask, so the truth never keeps foam the mask has lost. In that outer
        // ring only the foam changes (and the dirt under it through the gate, as the mask's ring does); dirt and wet keep the
        // (r + cellReach) reach of w.
        // IMP-DOG-039 §13.3, the bare hand: ONE gate value per stroke, from the foam BEFORE the update of the cells this stroke
        // reaches (the same set as below: the hit part and its neighbours, within r + cellReach of the capsule) -
        // src = their maximum, gate = smoothstep(handFoamMin, handFoamMin + 0.1, src), the same gate on every one of them (no
        // order dependence, no neighbour search). Returns false (and changes nothing) when it touched no foam
        // (src < handFoamMin, §13.2); true for every other stroke.
        bool ApplyToCells(int tool, int part, Vector3 a, Vector3 b, float radMeters, float sec)
        {
            if (!_cellsOk) return tool != ToolHand;   // no cells: a hand touches no foam (HandSourceFoam is 0 as well)
            float handGate = 1f;
            if (tool == ToolHand)
            {
                float src = HandSourceFoam(part, a, b, radMeters);
                if (!(src >= handFoamMin)) return false;
                handGate = SmoothStep01(handFoamMin, handFoamMin + 0.1f, src);
            }
            EnsureArrays();   // _cellShowered
            _cellsDirty = true;   // IMP-DOG-034 B1a (the callers set _syncDue)
            bool shower = tool == ToolShower;
            float rMesh = radMeters / meshToWorldScale;
            float reach = (radMeters + cellReach) / meshToWorldScale;
            float reach2 = reach * reach;
            float outer = shower ? Mathf.Max(reach, (radMeters * Mathf.Max(1f, rinseReach) + cellReach) / meshToWorldScale) : reach;
            float outer2 = outer * outer;
            Vector4 delta = ToolDelta(tool, Dwell(sec, a, b, rMesh) * cellGain * handGate);
            Vector4 foamOnly = new Vector4(0f, delta.y, 0f, 0f);
            float gateParam = shower ? Mathf.Clamp01(foamRinseGate) : 0f;
            bool rinsing = delta.x < 0f && delta.y < 0f;   // the shader's `rinsing` reads the uniform _Delta, not the weighted one
            int mask = shower ? -1 : (partNeighbors != null && part < partNeighbors.Length ? partNeighbors[part] : 0);   // -1 = every part
            Vector3 ab = b - a;
            float l2 = ab.sqrMagnitude;
            for (int q = 0; q < PartCount; q++)
            {
                if (q != part && ((mask >> q) & 1) == 0) continue;
                int end = _partCellStart[q + 1];
                for (int k = _partCellStart[q]; k < end; k++)
                {
                    int c = _partCells[k];
                    Vector3 p = cellCenter[c];
                    float t = l2 > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2) : 0f;
                    float d2 = (p - (a + ab * t)).sqrMagnitude;
                    if (d2 > outer2) continue;
                    if (d2 <= reach2)
                    {
                        ApplyCell(c, delta, gateParam, rinsing);
                        // round 2: which cells the shower rinsed (RunoffCells counts only these); a sponge's foam undoes it
                        if (shower) { if (_wet[c] >= 0.5f && _foam[c] < 0.1f) _cellShowered[c] = true; }
                        else if (delta.y > 0f) _cellShowered[c] = false;
                    }
                    else ApplyCell(c, foamOnly, gateParam, rinsing);
                }
            }
            return true;
        }

        // IMP-DOG-039 §13.3: the largest foam among the cells a hand stroke reaches (the hit part and its partNeighbors, cell
        // centre within radMeters + cellReach of the capsule a -> b), as they are now. Read only.
        float HandSourceFoam(int part, Vector3 a, Vector3 b, float radMeters)
        {
            if (!_cellsOk || part < 0 || part >= PartCount) return 0f;
            float reach = (radMeters + cellReach) / meshToWorldScale;
            float reach2 = reach * reach;
            int mask = partNeighbors != null && part < partNeighbors.Length ? partNeighbors[part] : 0;
            Vector3 ab = b - a;
            float l2 = ab.sqrMagnitude;
            float src = 0f;
            for (int q = 0; q < PartCount; q++)
            {
                if (q != part && ((mask >> q) & 1) == 0) continue;
                int end = _partCellStart[q + 1];
                for (int k = _partCellStart[q]; k < end; k++)
                {
                    int c = _partCells[k];
                    if (!(_foam[c] > src)) continue;
                    Vector3 p = cellCenter[c];
                    float t = l2 > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2) : 0f;
                    if ((p - (a + ab * t)).sqrMagnitude <= reach2) src = _foam[c];
                }
            }
            return src;
        }

        // Same reaction as KH_WashMask.shader FragBrush (w = 1): foam growth eases near full, dirt removal is boosted
        // by foam, and while rinsing the dirt under foam follows the foam's ratio (the gate).
        void ApplyCell(int c, Vector4 delta, float gateParam, bool rinsingTool)
        {
            float dirt = _dirt[c];
            float foam = _foam[c];
            float dF = delta.y;
            float ease = dF > 0f ? (1f - _foamGrowEase * foam) : 1f;
            float foamNew = Mathf.Clamp01(foam + dF * ease);
            float dD = delta.x;
            float boost = dD < 0f ? (1f + foam * rinseFoamBoost) : 1f;
            float dirtNormal = Mathf.Clamp01(dirt + dD * boost);
            float rinsing = rinsingTool ? 1f : 0f;
            float gate = gateParam * rinsing * SmoothStep01(0.03f, 0.2f, foam);
            float dirtGated = dirt * (foamNew / Mathf.Max(foam, 1e-4f));
            float dirtNew = Mathf.Clamp01(Mathf.Lerp(dirtNormal, Mathf.Min(dirtGated, dirt), gate));
            _dirt[c] = dirtNew;
            _foam[c] = foamNew;
            _wet[c] = Mathf.Clamp01(_wet[c] + delta.z);
        }

        // HLSL smoothstep.
        float SmoothStep01(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // When the cells may have changed (IMP-DOG-034 B1a; at least every SafeEvalInterval): averages over the valid
        // cells, the shell switch, and (Owner) the clean edge with hysteresis. One pass over _validCells does the averages,
        // the quiet set, the fresh count and, while armed, MarkArm + ArmAverage (IMP-DOG-034 B1a; same results: every
        // loop already skipped the cells outside _validCells, and arm bits are only ever set on those cells).
        // IMP-WLD-011: every evaluation ends with the photo milestones (the averages and, on the Owner, the clean edge just updated).
        void EvaluateState()
        {
            EvaluateStateCells();
            _PhotoEdges();
        }

        void EvaluateStateCells()
        {
            if (_dirt == null || _validCells == null) return;
            float now = Time.time;
            // IMP-DOG-034 B1a: what this evaluation has seen
            _cellsDirty = false;
            _evDirt = _dirt;
            _evFoam = _foam;
            _evWet = _wet;
            _nextSafeEval = NextGrid(now, SafeEvalInterval, _phase + 0.5f);
            bool edge = Networking.IsOwner(gameObject) && epoch != 0;
            bool armed = edge && cleanArmed;
            float sd = 0f;
            float sf = 0f;
            float sw = 0f;
            int n = _validCells.Length;
            bool dirty = false;
            bool foamWet = false;
            // Owner only: quiet cells (still foamy at the last edge) leave the quiet set once rinsed / dried below the
            // clean threshold; until then they are the rest of the same wash and do not count toward a new arm.
            int fresh = 0;
            bool quietChanged = false;
            bool armAdded = false;   // MarkArm
            int armCount = 0;        // ArmAverage
            float armSum = 0f;
            for (int k = 0; k < n; k++)
            {
                int c = _validCells[k];
                float d = _dirt[c];
                float f = _foam[c];
                float w = _wet[c];
                sd += d;
                sf += f;
                sw += w;
                if (f > 0f || w > 0f) foamWet = true;
                if (d > 0.002f) dirty = true;   // dirt never dries; a rinse leaves tiny floats behind
                if (!edge) continue;
                int bit = 1 << (c & 31);
                int wi = c >> 5;
                bool quiet = (quietBits[wi] & bit) != 0;
                if (quiet && f < cleanFoamThreshold) { quietBits[wi] &= ~bit; quiet = false; quietChanged = true; }
                if (!quiet && f >= dirtyFoamThreshold) fresh++;
                if (!armed) continue;
                int ab = armBits[wi];
                if ((ab & bit) == 0 && f >= dirtyFoamThreshold) { ab |= bit; armBits[wi] = ab; armAdded = true; }
                if ((ab & bit) != 0) { armCount++; armSum += f; }
            }
            _avgDirt = n > 0 ? sd / n : 0f;
            _avgFoam = n > 0 ? sf / n : 0f;
            _avgWet = n > 0 ? sw / n : 0f;
            _anyFoamOrWet = foamWet;
            _cellsEmpty = !dirty && !foamWet;
#if !UNITY_ANDROID && !UNITY_IOS
            PCFoamEvaluate(sf);
#endif
            EvaluateTime(now);

            // The clean edge (formulas in the header): arm on the average dirt or on enough foamed cells anywhere
            // (legs only is fine); fire when the averages are low AND the foamed set itself has been rinsed.
            if (!edge) return;
            if (quietChanged) _syncDue = true;
            if (!armed)
            {
                int need = Mathf.Max(1, Mathf.CeilToInt(Mathf.Clamp01(armFoamCellFraction) * n));
                if (_avgDirt >= dirtyDirtThreshold || (n > 0 && fresh >= need))
                {
                    cleanArmed = true;      // net: owner-only
                    ClearArm();
                    MarkArm();
                    _syncDue = true;
                }
                return;
            }
            if (armAdded) _syncDue = true;
            _armCount = armCount;
            float setAvg = armCount > 0 ? armSum / armCount : 0f;
            // IMP-DOG-029: a river drop alone does not make the dog "clean" (design §8 default); a wash stroke after it does.
            if (_avgDirt < cleanDirtThreshold && _avgFoam < cleanFoamThreshold && (_armCount == 0 || setAvg < cleanFoamThreshold) && !riverBlocksEdge)
            {
                cleanArmed = false;         // net: owner-only
                cleanMs = Networking.GetServerTimeInMilliseconds();   // net: owner-only (the dirt grace starts here)
                // (review 3, 2-1) nothing counted before the clean edge is applied after it (the water time would set
                // riverBlocksEdge again).
                _walkAcc = 0f;
                _zoneAcc = 0f;
                _waterAcc = 0f;
                _swimAcc = 0f;
                ClearArm();
                // net: owner-only (quietBits): what is still foamy now is left over from this wash.
                for (int c = 0; c < CellCount; c++)
                    if ((!_cellsOk || cellValid[c]) && _foam[c] >= dirtyFoamThreshold) quietBits[c >> 5] |= 1 << (c & 31);
                cleanSerial++;
                _syncDue = true;
                _nextSync = Time.time;      // the edge is what the dog waits for
            }
        }

        // IMP-DOG-034 B1a: the part of the evaluation that depends on the time only (EvaluateState's last results +
        // _lastActivity), every 0.5 s without the cell loop. Unchanged cells = unchanged _anyFoamOrWet / _cellsEmpty.
        void EvaluateTime(float now)
        {
#if !UNITY_ANDROID && !UNITY_IOS
            PCFoamTime(now);
#endif
            if (_anyFoamOrWet) { _lastActivity = now; _lastWetActivity = now; }
            // The shell goes off only when the cells are all zero AND the mask has had a full drying time since the
            // last stroke / foam / wet (its band can outlast the coarse cells). The mask is then set to the cells
            // (all zero), so a residue cannot come back on the next touch.
            float drain = Mathf.Max(foamDrySeconds, wetDrySeconds);
            bool off = _cellsEmpty && now - _lastActivity > drain + 2f;
            if (off && _shellOn) { _restorePending = true; ShowShell(false); }
            else if (!off && !_shellOn) ShowShell(true);
        }

        // net: owner-only (armBits). Adds every valid cell with foam >= dirtyFoamThreshold; true when one was new.
        bool MarkArm()
        {
            bool added = false;
            for (int c = 0; c < CellCount; c++)
            {
                if ((_cellsOk && !cellValid[c]) || _foam[c] < dirtyFoamThreshold) continue;
                int bit = 1 << (c & 31);
                int w = c >> 5;
                if ((armBits[w] & bit) != 0) continue;
                armBits[w] |= bit;
                added = true;
            }
            return added;
        }

        void ClearArm()
        {
            if (armBits == null || armBits.Length != ArmWords) armBits = new int[ArmWords];
            for (int i = 0; i < ArmWords; i++) armBits[i] = 0;
        }

        void ClearBits(int[] bits)
        {
            if (bits == null) return;
            for (int i = 0; i < bits.Length; i++) bits[i] = 0;
        }

        // The arm set's size at the last evaluation (its average foam is computed in EvaluateState's pass, IMP-DOG-034 B1a).
        private int _armCount;

        // ================= Getting dirty (IMP-DOG-029) =================
        // Owner only: sample the dog once a second (IMP-DOG-034 B1d), add up what happened, and every dirtApplyInterval put it into the
        // cells (the synced truth) and send the part averages as one ToolDirt NetStroke (see "Dirt on the mask" in the
        // header). Nothing changes = nothing is synced or sent. Dirt is never a penalty: nothing depends on it except the
        // look and the clean edge's arming (a dirty dog can be washed again).

        void DirtTick(float now)
        {
            if (!_cellsOk || dogRoot == null || _partCells == null) return;
            // IMP-DOG-034 B1d: once a second on this dog's phase (the six dogs do not sample in the same frame)
            if (now >= _dirtNextSample)
            {
                _dirtNextSample = NextGrid(now, DirtSampleInterval, _phase);
                DirtSample(now);
            }
            float iv = Mathf.Max(0.5f, dirtApplyInterval);
            if (iv != _dirtIvUsed)   // the interval was changed (harness): do not wait out the old one
            {
                if (iv < _dirtIvUsed && _dirtNextApply > now + iv) _dirtNextApply = now + iv;
                _dirtIvUsed = iv;
            }
            if (now >= _dirtNextApply)
            {
                _dirtNextApply = now + iv;
                ApplyDirt();
            }
        }

        void DirtSample(float now)
        {
            float dt = _dirtSampleAt < 0f ? 0f : now - _dirtSampleAt;
            _dirtSampleAt = now;
            Vector3 p = dogRoot.position;
            float step = 0f;
            if (_dirtPrevValid) { Vector3 d = p - _dirtPrevPos; d.y = 0f; step = d.magnitude; }
            _dirtPrevPos = p;
            _dirtPrevValid = true;
            // a hitch or a gap: not counted (IMP-DOG-034 B1d: 2.5 sample intervals; was 1 s at 0.25 s sampling)
            if (dt > DirtSampleInterval * 2.5f) { dt = 0f; step = 0f; }
            if (step > dt * Mathf.Max(1f, dirtMaxSpeed) + 0.5f) step = 0f;       // a warp (travel, recall, placement)
            if (!(dt > 0f)) return;
            if (!dirtEnabled) { _dirtBlock = 1; return; }
            int pose = dirtFetcher != null ? dirtFetcher.WaterPose : 0;
            if (pose != 0)
            {
                // The river only rinses and wets, so it runs near the station and in the grace too.
                _dirtBlock = 5;
                _waterAcc += dt;
                if (pose == 2) _swimAcc += dt;
                _dirtWaterTotal += dt;
                return;
            }
            // (review 3, 2-1) a block also drops what was counted before it in this period: it is never applied after a wash.
            if (_WashHold()) { _dirtBlock = 2; _walkAcc = 0f; _zoneAcc = 0f; return; }
            if (InDirtGrace()) { _dirtBlock = 3; _walkAcc = 0f; _zoneAcc = 0f; return; }   // a washed dog stays clean for a while (IMP-DOG-024)
            if (NearStation(p)) { _dirtBlock = 4; _walkAcc = 0f; _zoneAcc = 0f; return; }  // a few steps from the wash place do not undo the wash
            _dirtBlock = 0;
            if (step > 0f && DirtMoving()) { _walkAcc += step; _dirtWalkTotal += step; }
            if (InDirtZone(p)) { _zoneAcc += dt; _dirtZoneTotal += dt; }
        }

        bool InDirtGrace()
        {
            if (cleanSerial <= 0) return false;
            int age = Networking.GetServerTimeInMilliseconds() - cleanMs;
            return age > -1000 && age < Mathf.RoundToInt(Mathf.Max(0f, dirtGraceSeconds) * 1000f);
        }

        bool NearStation(Vector3 p)
        {
            if (!(dirtStationRadius > 0f)) return false;
            // IMP-DOG-034 B1d: Wash/11 lists the main station in washStations as well; it is checked once (in the list).
            // Decided per array reference, so a replaced array (harness) is looked at again.
            if (washStations != _stationsRef)
            {
                _stationsRef = washStations;
                _mainInList = false;
                if (washStations != null && washStation != null)
                    for (int i = 0; i < washStations.Length; i++) if (washStations[i] == washStation) { _mainInList = true; break; }
            }
            if (!_mainInList && NearOne(washStation, p)) return true;
            if (washStations == null) return false;
            for (int i = 0; i < washStations.Length; i++) if (NearOne(washStations[i], p)) return true;   // IMP-ENV-008: the stations along the course
            return false;
        }

        bool NearOne(Transform station, Vector3 p)
        {
            if (station == null) return false;
            Vector3 d = p - station.position;
            d.y = 0f;
            return d.magnitude <= dirtStationRadius;
        }

        // Leashed, or in a Fetch, or in a disc play: the dog is out walking / running with someone.
        bool DirtMoving()
        {
            if (dirtLeashes != null && dogIndex >= 0)
            {
                int[] w = dirtLeashes.walkers;
                if (w != null && dogIndex < w.Length && w[dogIndex] >= 0) return true;
            }
            if (dirtFetcher != null && !dirtFetcher.IsIdle) return true;
            if (dirtFrisbee != null && dirtFrisbee.IsEngaged) return true;
            return false;
        }

        // The dog's root inside a dirt zone: a sphere (radius lossyScale.x / 2) or, with dirtZoneBox[i], the zone's
        // local unit cube (its scale is the box size). Distances only, no trigger.
        bool InDirtZone(Vector3 p)
        {
            if (dirtZones == null) return false;
            for (int i = 0; i < dirtZones.Length; i++)
            {
                Transform z = dirtZones[i];
                if (z == null) continue;
                bool box = dirtZoneBox != null && i < dirtZoneBox.Length && dirtZoneBox[i];
                if (box)
                {
                    Vector3 l = z.InverseTransformPoint(p);
                    if (Mathf.Abs(l.x) <= 0.5f && Mathf.Abs(l.y) <= 0.5f && Mathf.Abs(l.z) <= 0.5f) return true;
                }
                else if ((p - z.position).magnitude <= Mathf.Abs(z.lossyScale.x) * 0.5f) return true;
            }
            return false;
        }

        // Owner: the counted walk / zone / water time into the cells. Legs (3..6) first, hip and chest (0, 1) from the
        // zone only (the cell table has no up / down, so the belly is the whole torso, thinly). A cell never goes above 1
        // (a dirty cell does not grow further), and the river never takes a cell below riverDirtFloor.
        void ApplyDirt()
        {
            // (review 3, 2-1) the conditions again at the apply: a hold / grace / station now drops the counted dirt.
            if (dogRoot != null && (_WashHold() || InDirtGrace() || NearStation(dogRoot.position))) { _walkAcc = 0f; _zoneAcc = 0f; }
            float zoneK = _zoneAcc / Mathf.Max(0.1f, zoneDirtSeconds);
            float legInc = _walkAcc / Mathf.Max(1f, walkDirtMeters) * walkLegDirt + zoneK * zoneLegDirt;
            float bodyInc = zoneK * zoneBodyDirt;
            float legDrop = _waterAcc / Mathf.Max(0.1f, riverDropSeconds) * riverLegDrop;
            float legWet = _waterAcc * riverWetPerSecond;
            float bodyWet = _swimAcc * riverWetPerSecond;
            _walkAcc = 0f;
            _zoneAcc = 0f;
            _waterAcc = 0f;
            _swimAcc = 0f;
            _dirtTicks++;
            bool changed = false;
            bool riverBefore = riverBlocksEdge;   // IMP-DOG-034 B1b
            int changedParts = 0;                 // IMP-DOG-034 B1b: bit p = part p's cells changed
            if (legInc > 0f || bodyInc > 0f || legDrop > 0f || legWet > 0f || bodyWet > 0f)
            {
                float floor = Mathf.Clamp01(riverDirtFloor);
                float wetLeg = 0f;
                float wetBody = 0f;
                for (int p = 0; p < PartCount; p++)
                {
                    bool leg = p >= 3 && p <= 6;
                    bool body = p == 0 || p == 1;
                    float inc = leg ? legInc : (body ? bodyInc : 0f);
                    float drop = leg ? legDrop : 0f;
                    float wa = leg ? legWet : (body ? bodyWet : 0f);
                    if (!(inc > 0f) && !(drop > 0f) && !(wa > 0f)) continue;
                    int s = _partCellStart[p];
                    int e = _partCellStart[p + 1];
                    if (e <= s) continue;
                    float sd = 0f;
                    float sw = 0f;
                    float neg = 0f;
                    bool rose = false;
                    for (int k = s; k < e; k++)
                    {
                        int c = _partCells[k];
                        float d0 = _dirt[c];
                        // From the feet up: the increase is weighted by the cell's height (the river drop is not)
                        float d1 = Mathf.Min(1f, d0 + Mathf.Max(0f, inc) * _cellW[c]);
                        if (d1 > d0) rose = true;
                        float d = d1;
                        if (drop > 0f && d > floor) d = Mathf.Max(floor, d - drop);
                        neg += d - d1;
                        _dirt[c] = d;
                        sd += d - d0;
                        float w0 = _wet[c];
                        float w = Mathf.Min(1f, w0 + Mathf.Max(0f, wa));
                        _wet[c] = w;
                        sw += w - w0;
                        if (d != d0 || w != w0) changedParts |= 1 << p;   // IMP-DOG-034 B1a / B1b (per cell, not the sums)
                    }
                    if (sd == 0f && sw == 0f) continue;
                    changed = true;
                    if (drop > 0f) riverBlocksEdge = true;   // net: owner-only (synced with the cells below)
                    int n = e - s;
                    // What the mask is told: the UNWEIGHTED increase (the Brush weights it by each texel's height with the
                    // same curve; the mask saturates at 1 like the cells), plus the average drop. A fully saturated part adds 0.
                    float told = (rose ? Mathf.Max(0f, inc) : 0f) + neg / n;
                    _dirtCarry[p] = Mathf.Clamp(_dirtCarry[p] + told, -0.5f, 0.5f);
                    if (leg) wetLeg += sw / n * 0.25f;
                    else wetBody += sw / n * 0.5f;
                }
                _wetCarryLeg = Mathf.Min(1f, _wetCarryLeg + wetLeg);
                _wetCarryBody = Mathf.Min(1f, _wetCarryBody + wetBody);
            }
            if (changed) _lastActivity = Time.time;
            if (changedParts != 0) _cellsDirty = true;   // IMP-DOG-034 B1a
            // IMP-DOG-034 B1b: was _syncDue on every change. The cells go out quantised (dirt 2 bit, wet 3 bit) and a walk adds
            // ~0.0015 per tick, so most ticks would resend the same bytes. Sync when riverBlocksEdge changed, when the event
            // below moves dirtSeq (BuildDirtEvent sets _syncDue), or when a changed cell's byte differs from the last sent one.
            if (riverBlocksEdge != riverBefore) _syncDue = true;
            BuildDirtEvent();
            if (changedParts != 0 && !_syncDue && PartsDifferFromSent(changedParts)) _syncDue = true;
        }

        // IMP-DOG-034 B1b: a cell of the marked parts whose byte (as OnPreSerialization would write it) differs from the
        // last serialized one (`cells`; on a new Owner the last received one, which OnOwnershipTransferred resends anyway).
        bool PartsDifferFromSent(int parts)
        {
            if (cells == null || cells.Length != CellCount) return true;
            for (int p = 0; p < PartCount; p++)
            {
                if (((parts >> p) & 1) == 0) continue;
                int e = _partCellStart[p + 1];
                for (int k = _partCellStart[p]; k < e; k++)
                {
                    int c = _partCells[k];
                    if (CellByte(c) != (int)cells[c]) return true;
                }
            }
            return false;
        }

        // Owner: quantise the carried part averages into one event; draw the same quantised change here at once.
        void BuildDirtEvent()
        {
            if (_dirtOutPending) return;   // the last one has not gone out yet: the carry waits for the next tick
            int from = 0;
            int to = 0;
            bool any = false;
            for (int k = 0; k < 4; k++)
            {
                int qa = DirtQuant(3 + k);
                int qb = DirtQuant(k < 3 ? k : 8);
                if (qa != 0 || qb != 0) any = true;
                from |= (qa + 64) << (7 * k);
                to |= (qb + 64) << (7 * k);
            }
            int wl = Mathf.Clamp(Mathf.FloorToInt(_wetCarryLeg / DirtWetUnit), 0, DirtWetQMax);
            int wb = Mathf.Clamp(Mathf.FloorToInt(_wetCarryBody / DirtWetUnit), 0, DirtWetQMax);
            if (wl > 0 || wb > 0) any = true;
            if (!any) return;
            _wetCarryLeg -= wl * DirtWetUnit;
            _wetCarryBody -= wb * DirtWetUnit;
            QueueDirtFields(from, to, wl, wb);
            // net: owner-only (dirtSeq): these cells now hold this event; the next sync says so (review 3, 1-1).
            dirtSeq = (dirtSeq + 1) & 0x3FFFFFFF;
            _dirtMaskSeq = dirtSeq;
            _dirtOutSeq = dirtSeq;
            _dirtOutFrom = from;
            _dirtOutTo = to;
            _dirtOutAmount = wl | (wb << 10);
            _dirtOutPending = true;
            _syncDue = true;
        }

        int DirtQuant(int p)
        {
            int q = Mathf.Clamp(Mathf.RoundToInt(_dirtCarry[p] / DirtUnit), -DirtQMax, DirtQMax);
            _dirtCarry[p] -= q * DirtUnit;
            return q;
        }

        // Non-Owner: the Owner's dirt change (NetStroke tool 3). Everything is checked (anyone may send): the packing,
        // the epoch, that the sender is this object's Owner (or was, within PrevOwnerGrace of a change), and that the
        // object's dirt sequence moves forward. Review 3, 1-1: every accepted event is logged; it is drawn only when the
        // mask does not hold it yet (seq newer than _dirtMaskSeq = the cells' dirtSeq at the last Restore). An event that
        // arrives before this client's first receive is only logged: the first Restore draws the ones its cells lack.
        void ReceiveDirt(int part, int from, int to, int amount, int seq, int strokeEpoch)
        {
            if (part != 0 || from < 0 || from > PackMax || to < 0 || to > PackMax) return;
            if ((from >> 28) != 0 || (to >> 28) != 0) return;
            if (amount < 0 || (amount >> 20) != 0 || seq < 0) return;
            if (strokeEpoch == 0) return;
            if (_gotFirst && strokeEpoch != epoch) return;
            for (int k = 0; k < 4; k++)
            {
                if (((from >> (7 * k)) & 127) == 0 || ((to >> (7 * k)) & 127) == 0) return;   // -64 is never sent
            }
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller) || caller.isLocal) return;   // the Owner drew its own change when it made it
            bool owner = Networking.IsOwner(caller, gameObject)
                || (caller.playerId == _prevOwnerId && Time.time < _prevOwnerUntil);   // in flight across an Owner change
            if (!owner) return;
            if (_dirtLastValid && !SeqNewer(seq, _dirtLastSeq)) return;
            _dirtLastValid = true;
            _dirtLastSeq = seq;
            LogDirt(seq, strokeEpoch, from, to, amount);
            if (!_gotFirst) return;
            // 2026-10-07 audit RC-7 (b): this client is the new Owner and the event is the previous Owner's, in flight across the
            // change (PrevOwnerGrace). OnOwnershipTransferred re-integrated only what had arrived by then, so this one went to the
            // log and the mask but never into the cells / dirtSeq (codex-03/BUG-02). Taken when newer than what the cells held at
            // the takeover (_dirtTakeoverSeq), not than dirtSeq: our own numbering starts TakeoverSeqGap past it (OnOwnershipTransferred),
            // so a previous Owner's number in flight lies between the two and never equals one of ours. dirtSeq only moves up here
            // (max with what was received); a third client that took our event first still refuses the previous Owner's older
            // number - its cells converge with the next cell sync (the mask is not repainted by it: RC-7's ordering, Tier 3).
            bool takeover = Networking.IsOwner(gameObject) && _cellsOk && _partCells != null && SeqNewer(seq, _dirtTakeoverSeq);
            if (takeover || SeqNewer(seq, _dirtMaskSeq)) QueueDirtFields(from, to, amount & 1023, (amount >> 10) & 1023);
            if (takeover)
            {
                ApplyDirtEvent(from, to, amount);
                if (SeqNewer(seq, dirtSeq)) dirtSeq = seq;   // net: owner-only
                if (SeqNewer(seq, _dirtMaskSeq)) _dirtMaskSeq = seq;
                _syncDue = true;
                Wake();
            }
        }

        void LogDirt(int seq, int ep, int from, int to, int amount)
        {
            if (_dlCount >= DirtLogMax)
            {
                for (int i = 1; i < DirtLogMax; i++)
                {
                    _dlSeq[i - 1] = _dlSeq[i]; _dlEpoch[i - 1] = _dlEpoch[i]; _dlFrom[i - 1] = _dlFrom[i];
                    _dlTo[i - 1] = _dlTo[i]; _dlAmount[i - 1] = _dlAmount[i];
                }
                _dlCount = DirtLogMax - 1;
            }
            int k = _dlCount;
            _dlSeq[k] = seq; _dlEpoch[k] = ep; _dlFrom[k] = from; _dlTo[k] = to; _dlAmount[k] = amount;
            _dlCount++;
        }

        // A new Owner (review 3, 1-1): the logged events of this epoch its received cells do not hold (seq newer than
        // dirtSeq) go into the cells as the part averages they carry (uniform over the part, clamped 0..1; the floor of a
        // river drop is kept), in order, and dirtSeq moves to the newest. Its own mask already shows them.
        void ReintegrateDirt()
        {
            if (!_cellsOk || _partCells == null) return;
            for (int i = 0; i < _dlCount; i++)
            {
                if (_dlEpoch[i] != epoch || !SeqNewer(_dlSeq[i], dirtSeq)) continue;
                ApplyDirtEvent(_dlFrom[i], _dlTo[i], _dlAmount[i]);   // 2026-10-07 audit RC-7 (b): shared with ReceiveDirt
                dirtSeq = _dlSeq[i];   // net: owner-only
                _syncDue = true;
            }
            _dirtMaskSeq = dirtSeq;
        }

        // One dirt event into the cells: the part averages it carries (uniform over the part). 2026-10-07 audit RC-7 (b): moved out
        // of ReintegrateDirt unchanged, so the takeover path in ReceiveDirt applies an event the same way.
        void ApplyDirtEvent(int from, int to, int amount)
        {
            float wl = (amount & 1023) * DirtWetUnit;
            float wb = ((amount >> 10) & 1023) * DirtWetUnit;
            for (int k = 0; k < 4; k++)
            {
                int pa = 3 + k;
                int pb = k < 3 ? k : 8;
                AddPartCells(pa, (((from >> (7 * k)) & 127) - 64) * DirtUnit, wl);
                AddPartCells(pb, (((to >> (7 * k)) & 127) - 64) * DirtUnit, pb <= 1 ? wb : 0f);
            }
        }

        void AddPartCells(int part, float dD, float dW)
        {
            if (dD == 0f && dW == 0f) return;
            _cellsDirty = true;   // IMP-DOG-034 B1a (ReintegrateDirt sets _syncDue)
            float floor = Mathf.Clamp01(riverDirtFloor);
            int e = _partCellStart[part + 1];
            for (int k = _partCellStart[part]; k < e; k++)
            {
                int c = _partCells[k];
                float d = _dirt[c];
                if (dD > 0f) d = Mathf.Min(1f, d + dD * _cellW[c]);   // the event carries the unweighted increase
                else if (dD < 0f && d > floor) d = Mathf.Max(floor, d + dD);
                _dirt[c] = d;
                _wet[c] = Mathf.Clamp01(_wet[c] + dW);
            }
        }

        void QueueDirtFields(int from, int to, int wetLegQ, int wetBodyQ)
        {
            float wl = wetLegQ * DirtWetUnit;
            float wb = wetBodyQ * DirtWetUnit;
            for (int k = 0; k < 4; k++)
            {
                int pa = 3 + k;
                int pb = k < 3 ? k : 8;
                float da = (((from >> (7 * k)) & 127) - 64) * DirtUnit;
                float db = (((to >> (7 * k)) & 127) - 64) * DirtUnit;
                QueueDirtDraw(pa, da, wl);
                QueueDirtDraw(pb, db, pb <= 1 ? wb : 0f);
            }
        }

        void QueueDirtDraw(int part, float dD, float dW)
        {
            if (dD == 0f && dW == 0f) return;
            _dirtDrawPend[part] += dD;
            _wetDrawPend[part] += dW;
            _anyDirtDraw = true;
            // The shell must not switch off (and repaint from cells that do not have this yet) before the sync arrives.
            _lastActivity = Time.time;
            if (dW != 0f) _lastWetActivity = _lastActivity;   // IMP-DOG-034 B1c: wet on the mask has to dry (Decay)
            ShowShell(true);
        }

        // One whole-part Brush per waiting part, inside the Blit budget, after any repaint / replay.
        void DrainDirt()
        {
            if (!_anyDirtDraw || _restorePending || _replaying || _replayPending) return;
            bool left = false;
            int drawn = 0;
            for (int p = 0; p < PartCount; p++)
            {
                float dD = _dirtDrawPend[p];
                float dW = _wetDrawPend[p];
                if (dD == 0f && dW == 0f) continue;
                if (drawn >= DirtDrawsPerFrame || !CanBlit()) { left = true; break; }
                drawn++;
                _dirtDrawPend[p] = 0f;
                _wetDrawPend[p] = 0f;
                DrawDirt(p, dD, dW);
            }
            _anyDirtDraw = left;
        }

        void DropDirtDraws()
        {
            for (int p = 0; p < PartCount; p++) { _dirtDrawPend[p] = 0f; _wetDrawPend[p] = 0f; }
            _anyDirtDraw = false;
        }

        // The Brush pass as an additive whole-part change: a zero-length capsule at the part's centre whose radius covers
        // the part even at the rim noise's smallest (r (1 - edgeNoise)) with a hard edge, only this part allowed. Inside,
        // w = the clump noise (mean ~1, fixed in mesh space: repeated dirt builds the same mud pattern). Foam is not
        // touched (dF = 0); the rinse boost and gate are off (a river drop is plain).
        void DrawDirt(int part, float dD, float dW)
        {
            _blitCount++;
            _dirtDraws++;
            if (_maskMat == null || _cur == null || _next == null || _partReach == null || _partCenter == null) return;
            float en = Mathf.Clamp(edgeNoise, 0f, 0.9f);
            float minR = 0.05f / Mathf.Max(1e-4f, meshToWorldScale);
            float r = Mathf.Max(_partReach[part], minR) * Mathf.Max(1f, dirtReachScale) / ((1f - en) * 0.99f);
            Vector3 c = _partCenter[part];
            Vector4 c4 = new Vector4(c.x, c.y, c.z, 1f);
            _maskMat.SetVector(_idBrushA, c4);
            _maskMat.SetVector(_idBrushB, c4);
            _maskMat.SetFloat(_idBrushRadius, r);
            _maskMat.SetFloat(_idBrushSoft, 0.01f);
            _maskMat.SetVector(_idDelta, new Vector4(dD, 0f, dW, 0f));
            _maskMat.SetFloat(_idRinseFoamBoost, 0f);
            _maskMat.SetFloat(_idFoamRinseGate, 0f);
            _maskMat.SetFloat(_idEdgeNoise, en);
            _maskMat.SetFloat(_idRinseReach, TowelFoamReach);   // no foam change here; keeps the shader's cheap reject tight
            // From the feet up (mask shader, same curve as DirtHeightWeight): only a dirt INCREASE (_Delta.x > 0, never a
            // tool) is weighted by the texel's height = (position map z - ground) in mesh units. z = enabled.
            float inv = 1f / Mathf.Max(1e-4f, meshToWorldScale);
            float full = Mathf.Max(0f, dirtHeightFull);
            float mid = Mathf.Max(full + 0.001f, dirtHeightMid);
            float top = Mathf.Max(mid + 0.001f, dirtHeightTop);
            _maskMat.SetVector(_idDirtHeightFade, new Vector4(_groundZ, _groundZ + full * inv, _groundZ + mid * inv, _groundZ + top * inv));
            _maskMat.SetVector(_idDirtHeightWeight, new Vector4(dirtMidWeight, dirtTopWeight, 1f, 0f));
            SetAllowOnly(part);
            SetLather(false);   // IMP-DOG-039: the dirt draw is a Brush pass too; a hand's gate must not hold it back
            VRCGraphics.Blit(_cur, _next, _maskMat, PassBrush);
            Swap();
        }

        // _AllowPart = this part only (no neighbours: the leg's dirt must not spill onto the chest). _allowFor = -3 so the
        // next tool stroke writes its own set again.
        void SetAllowOnly(int part)
        {
            for (int i = 0; i < AllowSlots; i++) _allow[i] = i == part ? 1f : 0f;
            _maskMat.SetFloatArray(_idAllowPart, _allow);
            _allowFor = -3;
        }

        // ================= Outgoing =================

        // tool = the sender's CHANNEL here (IMP-DOG-039: every index below is a channel; the kind is ChannelTool(channel)).
        void AddToSegment(int tool, int part, Vector3 a, Vector3 b, float rad, float sec)
        {
            float now = Time.time;
            if (_segOpen[tool] && (_segPart[tool] != part || now - _segLast[tool] > SegmentGap)) CloseSegment(tool, false);
            // A curve would be flattened into one straight capsule: cut the segment when the path it covers is
            // clearly longer than its chord (the next one continues from here).
            if (_segOpen[tool])
            {
                float chord = (b - _segFrom[tool]).magnitude;
                float slack = 2f * rad / meshToWorldScale;
                if (_segPath[tool] + (b - a).magnitude > chord * 1.3f + slack) CloseSegment(tool, true);
                // IMP-DOG-039 §13.1: a hand capsule longer than handMaxSegment is dropped by every receiver: cut before it
                // (90 %, for the 10-bit packing), the next one continues from here.
                else if (tool >= ChHandL && tool < ChDryer && chord * meshToWorldScale > handMaxSegment * 0.9f) CloseSegment(tool, true);
            }
            if (!_segOpen[tool])
            {
                // A segment cut by the timer or the curve test continues from where the last one ended, so the
                // receivers' capsules join up.
                bool carry = _segCarry[tool] && _segPart[tool] == part && now - _segLast[tool] <= SegmentGap;
                _segOpen[tool] = true;
                _segFrom[tool] = carry ? _segTo[tool] : a;
                _segPart[tool] = part;
                _segSec[tool] = 0f;
                _segRad[tool] = rad;
                _segPath[tool] = carry ? (a - _segTo[tool]).magnitude : 0f;
                _segStart[tool] = now;
            }
            _segCarry[tool] = false;
            _segPath[tool] += (b - a).magnitude;
            _segTo[tool] = b;
            _segSec[tool] += sec;
            _segRad[tool] = Mathf.Max(_segRad[tool], rad);
            _segLast[tool] = now;
        }

        // Into the send queue. A full queue does not drop: the newest queued segment of the same channel and part takes
        // this one (its end moves here and the times add up); only when there is none are the two oldest entries of
        // the same channel merged, and only as the last resort the oldest is dropped (it is already drawn locally).
        // IMP-DOG-039: t is the channel. A hand channel is never merged (the merged capsule could pass handMaxSegment and be
        // dropped by every receiver), nor the bottle's (its doses are dots); see the full-queue rule for them below.
        void CloseSegment(int t, bool carry)
        {
            if (!_segOpen[t]) return;
            _segOpen[t] = false;
            _segCarry[t] = carry;
            int ms = Mathf.Clamp(Mathf.RoundToInt(_segSec[t] * 1000f), 1, MaxAmountMs);
            int rmm = Mathf.Clamp(Mathf.RoundToInt(_segRad[t] * 1000f), 20, 250);
            int part = _segPart[t];
            int packedTo = Pack(part, _segTo[t]);
            if (_qCount >= QueueMax)
            {
                if (t >= ChBottle)
                {
                    // IMP-DOG-039 (Codex review of the implementation, 6): a bottle / hand entry never evicts a tool 0-2 entry (a
                    // shower segment lost here is rinsed on this mask but never in the Owner's cells). It takes the place of the
                    // oldest hand entry, else the oldest bottle entry; with neither, the new entry is dropped (drawn locally).
                    int victim = -1;
                    for (int i = 0; i < _qCount && victim < 0; i++) if (_qCh[i] >= ChHandL) victim = i;
                    for (int i = 0; i < _qCount && victim < 0; i++) if (_qCh[i] == ChBottle) victim = i;
                    if (victim < 0) return;
                    for (int i = victim + 1; i < _qCount; i++) CopyQueue(i, i - 1);
                    _qCount--;
                }
                else
                {
                    for (int i = QueueMax - 1; i >= 0; i--)
                    {
                        if (_qCh[i] != t || _qPart[i] != part) continue;
                        _qTo[i] = packedTo;
                        _qMs[i] = Mathf.Min(MaxAmountMs, _qMs[i] + ms);
                        _qRmm[i] = Mathf.Max(_qRmm[i], rmm);
                        return;
                    }
                    if (!MergeOldestPair())
                    {
                        for (int i = 1; i < QueueMax; i++) CopyQueue(i, i - 1);
                        _qCount = QueueMax - 1;
                    }
                }
            }
            int k = _qCount;
            _qCh[k] = t;
            _qPart[k] = part;
            _qFrom[k] = Pack(part, _segFrom[t]);
            _qTo[k] = packedTo;
            _qMs[k] = ms;
            _qRmm[k] = rmm;
            _qCount++;
        }

        // Frees one queue slot by merging the first adjacent pair of the same channel and part (never the bottle's or a hand's).
        bool MergeOldestPair()
        {
            for (int i = 0; i + 1 < _qCount; i++)
            {
                if (_qCh[i] >= ChBottle || _qCh[i] != _qCh[i + 1] || _qPart[i] != _qPart[i + 1]) continue;
                _qTo[i] = _qTo[i + 1];
                _qMs[i] = Mathf.Min(MaxAmountMs, _qMs[i] + _qMs[i + 1]);
                _qRmm[i] = Mathf.Max(_qRmm[i], _qRmm[i + 1]);
                for (int j = i + 2; j < _qCount; j++) CopyQueue(j, j - 1);
                _qCount--;
                return true;
            }
            return false;
        }

        void CopyQueue(int from, int to)
        {
            _qCh[to] = _qCh[from];
            _qPart[to] = _qPart[from];
            _qFrom[to] = _qFrom[from];
            _qTo[to] = _qTo[from];
            _qMs[to] = _qMs[from];
            _qRmm[to] = _qRmm[from];
        }

        void UpdateSend(float now)
        {
            for (int t = 0; t < ChannelCount; t++)   // IMP-DOG-039: channels
            {
                if (!_segOpen[t]) continue;
                if (now - _segLast[t] > SegmentGap) CloseSegment(t, false);
                else if (now - _segStart[t] > SegmentMaxAge) CloseSegment(t, true);
            }
            if (now < _nextSend) return;
            // The Owner's dirt change shares the 8 Hz slot with the strokes (one event, then the next slot).
            if (_dirtOutPending)
            {
                _dirtOutPending = false;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStroke), ToolDirt, 0, _dirtOutFrom, _dirtOutTo, _dirtOutAmount, _dirtOutSeq, epoch);
                _dirtEvents++;
                _nextSend = now + SendInterval;
                return;
            }
            if (_qCount == 0)
            {
                int oldest = -1;
                for (int t = 0; t < ChannelCount; t++)
                {
                    if (!_segOpen[t] || now - _segStart[t] < SendInterval) continue;
                    if (oldest < 0 || _segStart[t] < _segStart[oldest]) oldest = t;
                }
                if (oldest >= 0) CloseSegment(oldest, true);
            }
            if (_qCount == 0) return;
            int amount = _qMs[0] | (_qRmm[0] << 12);
            int kind = ChannelTool(_qCh[0]);   // IMP-DOG-039: the wire and the log carry the kind (left / right are not sent)
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStroke), kind, _qPart[0], _qFrom[0], _qTo[0], amount, _seq, epoch);
            if (!_gotFirst) LogSent(kind, _qPart[0], _qFrom[0], _qTo[0], _qMs[0], _qRmm[0], _seq);
            _seq = (_seq + 1) & 0x3FFFFFFF;
            for (int i = 1; i < _qCount; i++) CopyQueue(i, i - 1);
            _qCount--;
            _nextSend = now + SendInterval;
        }

        // Mesh point -> 3 x 10 bit, relative to the part's centre, inside a +-packHalfMeters box (design §1-4).
        int Pack(int part, Vector3 v)
        {
            Vector3 c = _partCenter[part];
            float inv = meshToWorldScale / Mathf.Max(0.01f, packHalfMeters);
            int x = Quant((v.x - c.x) * inv);
            int y = Quant((v.y - c.y) * inv);
            int z = Quant((v.z - c.z) * inv);
            return x | (y << 10) | (z << 20);
        }

        int Quant(float f)
        {
            return Mathf.Clamp(Mathf.RoundToInt(f * 511.5f + 511.5f), 0, 1023);
        }

        Vector3 Unpack(int part, int p)
        {
            Vector3 c = _partCenter[part];
            float half = Mathf.Max(0.01f, packHalfMeters) / meshToWorldScale;
            float x = ((p & 1023) - 511.5f) / 511.5f * half;
            float y = (((p >> 10) & 1023) - 511.5f) / 511.5f * half;
            float z = (((p >> 20) & 1023) - 511.5f) / 511.5f * half;
            return new Vector3(c.x + x, c.y + y, c.z + z);
        }

        // ================= Effects =================

        // A received stroke is in mesh space; the effects need a world point. Through the part's bone whose
        // collider centre is nearest in mesh space (a leg has several bones).
        Vector3 MeshToWorld(int part, Vector3 meshPoint)
        {
            int best = -1;
            float bestD = 0f;
            int n = _meshToBone != null ? _meshToBone.Length : 0;
            for (int i = 0; i < n; i++)
            {
                if (!ColliderUsable(i) || partOfCollider[i] != part) continue;
                float d = (_colliderMeshCenter[i] - meshPoint).sqrMagnitude;
                if (best < 0 || d < bestD) { best = i; bestD = d; }
            }
            if (best < 0) return dogRoot != null ? dogRoot.position : transform.position;
            return partBones[best].TransformPoint(_meshToBone[best].MultiplyPoint3x4(meshPoint));
        }

        // Foam of the nearest cell of the part (cached 0.1 s per key: this runs every frame while washing). key = a local
        // channel (the hands judge with their own) or FoamKeyEffects (the effects and the received strokes).
        float FoamNear(int key, int part, Vector3 meshPoint)
        {
            float now = Time.time;
            if (part == _fnPart[key] && now - _fnAt[key] < 0.1f) return _fnValue[key];
            _fnPart[key] = part;
            _fnAt[key] = now;
            float value = 0f;
            if (_cellsOk && part >= 0 && part < PartCount)
            {
                float bestD = -1f;
                int end = _partCellStart[part + 1];
                for (int k = _partCellStart[part]; k < end; k++)
                {
                    int c = _partCells[k];
                    float d = (cellCenter[c] - meshPoint).sqrMagnitude;
                    if (bestD < 0f || d < bestD) { bestD = d; value = _foam[c]; }
                }
            }
            _fnValue[key] = value;
            return value;
        }

        // Physical cause only (splash, foam bits, contact loops), at the stroke itself - own and received alike.
        void ContactEffects(int tool, int part, Vector3 world, Vector3 meshPoint, float sec)
        {
            if (tool == ToolDryer) return; // air sound/particles live at the nozzle, not water/contact scrub
            float now = Time.time;
            float foamHere = FoamNear(FoamKeyEffects, part, meshPoint);
#if !UNITY_ANDROID && !UNITY_IOS
            PCFoamContact(tool, part, world, sec, foamHere);
#endif
            if (tool == ToolBottle || tool == ToolHand)
            {
                // IMP-DOG-039 §2.5: the foam loop alone (its own deadline, not the shower's fur / foam mix): the bottle for one
                // dose, a lathering hand while it keeps lathering. Puffs below.
                _foamOnlyUntil = now + (tool == ToolBottle ? Mathf.Max(0.25f, sec) : 0.2f);
                if (contactFoam != null) contactFoam.transform.position = world;
            }
            else if (tool == ToolShower)
            {
                _splashAcc += sec * splashPerSecond;
                int n = Mathf.FloorToInt(_splashAcc);
                if (n > 0 && hitSplash != null)
                {
                    _splashAcc -= n;
                    hitSplash.transform.position = world;
                    hitSplash.Emit(n);
                }
                _furTarget = contactVolume * (1f - foamHere);
                _foamTarget = contactVolume * foamHere;
                _showerUntil = now + 0.2f;
                if (contactFur != null) contactFur.transform.position = world;
                if (contactFoam != null) contactFoam.transform.position = world;
            }
            else
            {
                _scrubUntil = now + 0.2f;
                if (scrub != null) scrub.transform.position = world;
            }
            if (tool == ToolBottle)
            {
                if (foamPuff != null)
                {
                    foamPuff.transform.position = world;
                    foamPuff.Emit(6);   // one dose = a small puff (§2.5)
                }
            }
            else if (tool == ToolSponge || tool == ToolHand || (tool == ToolShower && foamHere > 0.2f))
            {
                _puffAcc += sec * foamPuffPerSecond;
                int n = Mathf.FloorToInt(_puffAcc);
                if (n > 0 && foamPuff != null)
                {
                    _puffAcc -= n;
                    foamPuff.transform.position = world;
                    foamPuff.Emit(n);
                }
            }
            if (_splashAcc > 1000f) _splashAcc = 0f;
            if (_puffAcc > 1000f) _puffAcc = 0f;
        }

        // The fade runs on this behaviour's own levels (_furVol / _foamVol / _scrubVol); what reaches the source is the level x
        // the dog's personal voice gain (KD_DogWanderController.localVoiceGain, written by KD_TreatPouch.ApplyDogVoiceSettings;
        // the ledger bakes base 0 for these sources, so this write is what plays; mute stays the ledger's).
        private float _furVol;
        private float _foamVol;
        private float _scrubVol;

        void UpdateAudio(float now)
        {
            // IMP-DOG-034 B1e: silent and no contact = every level is 0 and stays 0 (the return below, without the reads)
            if (!_audioOn && now >= _showerUntil && now >= _scrubUntil && now >= _foamOnlyUntil) return;
            float step = audioFadeSpeed * Time.deltaTime;
            bool shower = now < _showerUntil;
            _furVol = Mathf.MoveTowards(_furVol, shower ? _furTarget : 0f, step);
            // IMP-DOG-039: the shower's mix while it runs; otherwise the bottle / hand foam loop at contactVolume
            _foamVol = Mathf.MoveTowards(_foamVol, shower ? _foamTarget : (now < _foamOnlyUntil ? contactVolume : 0f), step);
            _scrubVol = Mathf.MoveTowards(_scrubVol, now < _scrubUntil ? scrubVolume : 0f, step);
            if (_furVol <= 0f && _foamVol <= 0f && _scrubVol <= 0f && !_audioOn) return;   // silent: no cross-behaviour read
            float gain = voiceDog != null ? Mathf.Clamp01(voiceDog.localVoiceGain) : 1f;
            Fade(contactFur, _furVol, gain);
            Fade(contactFoam, _foamVol, gain);
            Fade(scrub, _scrubVol, gain);
            _audioOn = _furVol > 0f || _foamVol > 0f || _scrubVol > 0f;
        }
        private bool _audioOn = true;   // one pass after Start / the last sound so the sources end at 0 and stopped

        void Fade(AudioSource s, float level, float gain)
        {
            if (s == null) return;
            s.volume = level * gain;
            if (level > 0.001f) { if (!s.isPlaying) s.Play(); }
            else if (s.isPlaying) s.Stop();
        }

        void SilenceAll()
        {
            _furVol = 0f;
            _foamVol = 0f;
            _scrubVol = 0f;
            if (contactFur != null) { contactFur.volume = 0f; contactFur.Stop(); }
            if (contactFoam != null) { contactFoam.volume = 0f; contactFoam.Stop(); }
            if (scrub != null) { scrub.volume = 0f; scrub.Stop(); }
        }
    }
}
