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
    //
    // ---- Operations and their results (design §1-2; IMP-DOG-026). The mask (FragBrush) and the cells (ApplyCell)
    // use the same formulas; only the weight differs: mask w = the shader's soft capsule falloff x clumps x part gate
    // (foam removal uses the wider wOut), cells w = 1 for every cell centre within (r + cellReach) of the capsule, x
    // cellGain. s = Dwell(sec) x w. sat = clamp 0..1. D, F, W = dirt, foam, wet before; D', F', W' after.
    //
    //  op                | delta (dD, dF, dW) per s                          | result
    //  sponge (foam)     | (0, spongeFoamRate, spongeWetRate)                | F' = sat(F + dF (1 - ease F))  ease = _FoamGrowEase
    //                    |                                                   | D' = D (foam never changes dirt; foam also grows on a clean dog)
    //                    |                                                   | W' = sat(W + dW)
    //  shower (rinse)    | (showerDirtRate, showerFoamRate, showerWetRate)   | F' = sat(F + dF)
    //                    |                                                   | Dn = sat(D + dD (1 + F rinseFoamBoost))   (water alone removes dirt; foam is faster)
    //                    |                                                   | g  = foamRinseGate x smoothstep(0.03, 0.2, F)
    //                    |                                                   | D' = sat(lerp(Dn, min(D F' / max(F, 1e-4), D), g))
    //                    |                                                   | W' = sat(W + dW)
    //  towel (dry)       | (towelDirtRate, towelFoamRate, towelWetRate)      | as the shower with g = 0: D' = sat(D + dD (1 + F boost))
    //  dry (Decay, 2 s)  | fd = washing ? 0 : 2 / foamDrySeconds             | F' = max(0, F - fd), W' = max(0, W - wd), D' = D
    //                    | wd = 2 / wetDrySeconds                            | washing = _WashHold() || a stroke seen here within holdSeconds
    //  get dirty         | (IMP-DOG-029, not yet)                            |
    //  reset (Owner)     | _DebugSetDirt(v): D = v on valid cells, F = W = 0 | epoch++ -> every client repaints once (Restore)
    //                    | _DebugClear(): D = F = W = 0                      | cleanArmed = (v >= dirtyDirtThreshold), arm set cleared
    //  repaint (Restore) | mask = cells (bilinear) with D x (0.5 + noise)    | only: first receive, epoch change, lost RT, long gap, dried out
    //  sync (Owner)      | D 2 bit, F 3 bit, W 3 bit per cell                | decoded on receive into the floats (a coarse copy)
    //
    // Clean edge (Owner, every 0.5 s, n = valid cells, avg over the valid cells):
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
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KH_DogWash : UdonSharpBehaviour
    {
        public const int ToolSponge = 0;
        public const int ToolShower = 1;
        public const int ToolTowel = 2;
        public const int PartCount = 9;
        const int CellCount = 256;
        const int AllowSlots = 16;   // _AllowPart / _PartFoamLimit length in the shaders
        const int BoneSlots = 64;    // _BoneToPart length in the shaders
        const int PassBrush = 0;
        const int PassRestore = 1;
        const int PassDecay = 2;
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
        [Tooltip("Emit で使う。Simulation Space = World")]
        public ParticleSystem hitSplash;
        public ParticleSystem foamPuff;
        public AudioSource contactFur;
        public AudioSource contactFoam;
        public AudioSource scrub;

        [Header("Prototype values (Play で決め直す)")]
        [Range(0f, 1f)] public float initialDirt = 0.6f;
        public float spongeRadius = 0.09f;
        public float showerRadius = 0.07f;
        public float towelRadius = 0.10f;
        public float spongeFoamRate = 0.9f;
        public float spongeWetRate = 0.2f;
        public float showerFoamRate = -1.6f;
        public float showerDirtRate = -0.5f;
        [Tooltip("使っていない (マスクのシェーダーに上限が無いので、セルも同じ式にした 2026-09-30)")]
        public float showerDirtRateMax = -1.6f;
        public float showerWetRate = 1.2f;
        public float towelWetRate = -1.2f;
        public float towelFoamRate = -0.4f;
        public float towelDirtRate = -0.15f;
        [Tooltip("すすぎで汚れを落とす速さ = 基本 × (1 + 泡 × この値)。マスクとセルの両方に使う")]
        public float rinseFoamBoost = 2.2f;
        public float foamRinseGate = 1f;
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

        // ---- Synced (Owner of this object writes; the Owner follows the dog root) ----
        // Per cell: dirt 2 bit | foam 3 bit << 2 | wet 3 bit << 5 (design §1-4).
        [UdonSynced] private byte[] cells = new byte[CellCount];
        [UdonSynced] private int epoch;              // 0 = never initialised; bumped by every whole-dog reset
        // Last accepted stroke, server milliseconds. Compared by int subtraction (wraps correctly, negative server
        // times are fine: the same convention as KD_AccessoryBox / KH_LeashManager.Travel). washSeen says whether
        // there was a stroke at all, so 0 is not a magic "never".
        [UdonSynced] private int washMs;
        [UdonSynced] private bool washSeen;
        [UdonSynced] private int cleanSerial;
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

        private int _lastStrokePart = -1;
        private float _lastStrokeTime = -100f;
        private int _lastWasherId = -1;

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
        private float _releaseTime = -1000f;
        private bool[] _relValid = new bool[SenderSlots];
        private int[] _relSeq = new int[SenderSlots];
        private int _acceptSlot = -1;     // AcceptSender's slot of the last accepted stroke
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

        // Contact history per tool (mesh space, converted at the frame of contact).
        private bool[] _hValid = new bool[3];
        private int[] _hCollider = new int[3];
        private Vector3[] _hMesh = new Vector3[3];
        private float[] _hTime = new float[3];

        // Outgoing segments: one open slot per tool, closed into a small queue, sent at most 8 Hz.
        private bool[] _segOpen = new bool[3];
        private int[] _segPart = new int[3];
        private Vector3[] _segFrom = new Vector3[3];
        private Vector3[] _segTo = new Vector3[3];
        private float[] _segSec = new float[3];
        private float[] _segRad = new float[3];
        private float[] _segPath = new float[3];
        private float[] _segStart = new float[3];
        private float[] _segLast = new float[3];
        private bool[] _segCarry = new bool[3];
        private int[] _qTool = new int[QueueMax];
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
        private float _splashAcc;
        private float _puffAcc;
        private int _foamNearPart = -1;
        private float _foamNearAt = -100f;
        private float _foamNearValue;

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

            if (meshToWorldScale <= 0f) meshToWorldScale = 1f;
            for (int i = 0; i < SenderSlots; i++) { _sndId[i] = -1; _sndLast[i] = -1e9f; }
            EnsureArrays();
            BuildIndex();

            // Runtime copies only (vrc-udonsharp §1: Renderer.material is the one way to duplicate). The shell's
            // copy is taken once here and re-pointed at the newest mask after every Blit.
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
                    cleanArmed = true;
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
            _nextEval = now;
            _nextDecay = now + 2f;
            _nextRtCheck = now + 0.5f;
            _nextOwnerCheck = now + 1f;
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
            int[] fill = new int[PartCount];
            if (_cellsOk)
            {
                for (int c = 0; c < CellCount; c++)
                {
                    int p = cellPart[c];
                    if (!cellValid[c] || p < 0 || p >= PartCount) continue;
                    _partCells[_partCellStart[p] + fill[p]] = c;
                    fill[p]++;
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
            for (int c = 0; c < CellCount; c++)
            {
                _foam[c] = 0f;
                _wet[c] = 0f;
                _dirt[c] = 0f;
                if (!_cellsOk || !cellValid[c]) continue;
                _dirt[c] = Mathf.Clamp01(initialDirt * PartDirtFactor(cellPart[c]));
            }
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

        // Preferred entry (KH_WashTool): this frame's contact point only. It is converted to mesh space now, and the
        // capsule runs from this tool's previous mesh point (same collider, within SegmentGap) to it. Two tools of the
        // same kind in one player's two hands share this history (a limitation of the prototype).
        public void _LocalContact(int tool, int colliderIndex, Vector3 worldPoint, float seconds, float radiusMeters)
        {
            if (!_ready || tool < ToolSponge || tool > ToolTowel || !ColliderUsable(colliderIndex)) return;
            Vector3 m = partBones[colliderIndex].InverseTransformPoint(worldPoint);
            m = boneToMesh[colliderIndex].MultiplyPoint3x4(m);
            float now = Time.time;
            Vector3 from = (_hValid[tool] && _hCollider[tool] == colliderIndex && now - _hTime[tool] <= SegmentGap) ? _hMesh[tool] : m;
            _hValid[tool] = true;
            _hCollider[tool] = colliderIndex;
            _hMesh[tool] = m;
            _hTime[tool] = now;
            Stroke(tool, colliderIndex, from, m, worldPoint, seconds, radiusMeters);
        }

        // The tool lost contact / Use was released: end this tool's capsule and its outgoing segment now, so the next
        // contact does not join two points across the gap.
        public void _EndContact(int tool)
        {
            if (tool < ToolSponge || tool > ToolTowel) return;
            _hValid[tool] = false;
            if (_ready) CloseSegment(tool, false);
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
            if (!_ready || tool < ToolSponge || tool > ToolTowel || !ColliderUsable(colliderIndex)) return;
            Transform bone = partBones[colliderIndex];
            Matrix4x4 mat = boneToMesh[colliderIndex];
            Vector3 a = mat.MultiplyPoint3x4(bone.InverseTransformPoint(worldFrom));
            Vector3 b = mat.MultiplyPoint3x4(bone.InverseTransformPoint(worldTo));
            _hValid[tool] = true;
            _hCollider[tool] = colliderIndex;
            _hMesh[tool] = b;
            _hTime[tool] = Time.time;
            Stroke(tool, colliderIndex, a, b, worldTo, seconds, radiusMeters);
        }

        void Stroke(int tool, int colliderIndex, Vector3 a, Vector3 b, Vector3 worldTo, float seconds, float radiusMeters)
        {
            if (!(seconds > 0f)) return;
            seconds = Mathf.Min(seconds, 0.25f);   // a long hitch must not become one huge dab
            float rad = radiusMeters > 0f ? radiusMeters : ToolRadius(tool);
            rad = Mathf.Clamp(rad, 0.02f, MaxToolRadius(tool));
            int part = partOfCollider[colliderIndex];

            QueueOrDraw(tool, part, a, b, rad, seconds);
            AddToSegment(tool, part, a, b, rad, seconds);

            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp)) _lastWasherId = lp.playerId;
            _localStrokeAt = Time.time;
            _lastStrokePart = part;
            _lastStrokeTime = Time.time;
            _lastActivity = Time.time;
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
            if (tool < ToolSponge || tool > ToolTowel || part < 0 || part >= PartCount) return;
            if (from < 0 || from > PackMax || to < 0 || to > PackMax) return;
            if (amount < 0 || (amount >> 20) != 0) return;
            int ms = amount & 0xFFF;
            int rmm = (amount >> 12) & 0xFF;
            if (ms <= 0 || ms > MaxAmountMs || rmm < 20 || rmm > Mathf.RoundToInt(MaxToolRadius(tool) * 1000f)) return;
            if (seq < 0 || strokeEpoch < 0) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(caller)) return;

            // Epoch first, before the sender's sequence and budget are touched: a stale stroke must not use up the
            // budget of the fresh ones behind it. The epoch only identifies the version: a stroke is taken when it
            // matches this client's epoch, or is 0 (the sender held no state yet: a joiner). Anything else is
            // dropped, also a stroke of a reset that has not reached this client yet (a short visual gap only; the
            // cells come with the reset).
            if (strokeEpoch != 0 && strokeEpoch != epoch) return;

            if (dogRoot != null && Vector3.Distance(caller.GetPosition(), dogRoot.position) > maxWasherDistance) return;
            // The own echo comes from this client's own _LocalContact (only the local holder calls it): trusted.
            if (!caller.isLocal && !HoldsTool(caller.playerId, tool)) return;
            float sec = ms * 0.001f;
            if (!AcceptSender(caller.playerId, seq, sec)) return;
            int slot = _acceptSlot;

            float rad = rmm * 0.001f;
            Vector3 a = Unpack(part, from);
            Vector3 b = Unpack(part, to);
            _lastWasherId = caller.playerId;

            if (!caller.isLocal)
            {
                QueueOrDraw(tool, part, a, b, rad, sec);
                _lastStrokePart = part;
                _lastStrokeTime = Time.time;
                _lastActivity = Time.time;
                ContactEffects(tool, part, MeshToWorld(part, b), b, sec);
                ShowShell(true);
            }

            Record(caller.playerId, seq, epoch, tool, part, a, b, rad, sec);
            // Only the Owner integrates (a non-Owner's own sums would be overwritten by an older receive anyway; it
            // keeps the record instead and re-integrates when it becomes the Owner).
            if (!Networking.IsOwner(gameObject)) return;
            ApplyToCells(tool, part, a, b, rad, sec);
            SetApplied(caller.playerId, seq);
            _syncDue = true;
            // A stroke from before _ReleaseHold (accepted seq at the release, or in flight within ReleaseGrace)
            // changes the cells but does not start the hold again.
            bool old = _released && (Time.time - _releaseTime < ReleaseGrace
                || (slot >= 0 && _relValid[slot] && !SeqNewer(seq, _relSeq[slot])));
            if (!old)
            {
                washMs = Networking.GetServerTimeInMilliseconds();   // net: owner-only
                washSeen = true;                                      // net: owner-only
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
                ApplyToCells(_rcTool[i], _rcPart[i], _rcA[i], _rcB[i], _rcRad[i], _rcSec[i]);
                SetApplied(_rcSender[i], _rcSeq[i]);
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
            _sndLast[slot] = now;
            _acceptSlot = slot;
            return true;
        }

        // The player of the last stroke seen here (own or received); -1 before any or after that player left.
        public int _WasherPlayerId() { return _lastWasherId; }

        // Seconds (Time.time) since that player's last stroke seen here: the own strokes as they are drawn, the others'
        // as their NetStroke is accepted. 1e9 when there is no record (never, or evicted from the sender table).
        public float _StrokeAgeOf(int playerId)
        {
            float now = Time.time;
            float age = 1e9f;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp) && lp.playerId == playerId && _localStrokeAt > -1e8f) age = now - _localStrokeAt;
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
            VRCPlayerApi p = _lastWasherId >= 0 ? VRCPlayerApi.GetPlayerById(_lastWasherId) : null;
            if (Utilities.IsValid(p)) return p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            return dogRoot != null ? dogRoot.position : transform.position;
        }

        public int _CleanSerial() { return cleanSerial; }

        // Part of the last stroke seen here (own or received); -1 before any.
        public int _LastStrokePart() { return _lastStrokePart; }

        // Harness read-outs (last evaluation, every 0.5 s).
        public float _AverageDirt() { return _avgDirt; }
        public float _AverageFoam() { return _avgFoam; }

        // Prototype / harness: Owner only. A whole-dog reset bumps the epoch, so everyone repaints once.
        public void _DebugSetDirt(float value)
        {
            if (!_ready || !Networking.IsOwner(gameObject)) return;
            value = Mathf.Clamp01(value);
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
            BumpEpoch();
        }

        // The whole-dog reset: Owner only (a non-Owner call does nothing); everyone repaints once via the epoch.
        public void _DebugClear()
        {
            if (!_ready || !Networking.IsOwner(gameObject)) return;
            for (int c = 0; c < CellCount; c++) { _dirt[c] = 0f; _foam[c] = 0f; _wet[c] = 0f; }
            cleanArmed = false;
            ClearArm();
            ClearBits(quietBits);
            BumpEpoch();
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
            _pdCount = 0;
            _qCount = 0;
            for (int t = 0; t < 3; t++) { _segOpen[t] = false; _segCarry[t] = false; _hValid[t] = false; }
        }

        // ================= Update =================

        void Update()
        {
            if (!_ready) return;
            float now = Time.time;

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
                if (_replayPending)
                {
                    _replayPending = false;
                    if (restored) { _replaying = true; _rpIndex = 0; _rpLeft = -1f; }
                    else StopReplay();
                }
            }
            ReplayStep();
            DrainPending();

            UpdateSend(now);
            UpdateAudio(now);

            if (now >= _nextDecay && CanBlit())
            {
                _nextDecay = now + 2f;
                Decay(2f);
            }
            if (now >= _nextEval)
            {
                _nextEval = now + 0.5f;
                EvaluateState();
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
            if (!Networking.IsOwner(gameObject)) return;
            EnsureArrays();
            Reintegrate();
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
            for (int c = 0; c < CellCount; c++)
            {
                int d = Mathf.Clamp(Mathf.RoundToInt(_dirt[c] * 3f), 0, 3);
                int f = Mathf.Clamp(Mathf.RoundToInt(_foam[c] * 7f), 0, 7);
                int w = Mathf.Clamp(Mathf.RoundToInt(_wet[c] * 7f), 0, 7);
                cells[c] = (byte)(d | (f << 2) | (w << 5));
            }
        }

        public override void OnDeserialization()
        {
            EnsureArrays();
            if (cells == null || cells.Length != CellCount) return;
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
            int k = _pdCount - 1;
            if (k >= 0 && _pdTool[k] == tool && _pdPart[k] == part) { MergePending(k, b, rad, sec); return; }
            if (_pdCount >= PendMax)
            {
                for (int i = PendMax - 1; i >= 0; i--)
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
            SetAllow(part);
            VRCGraphics.Blit(_cur, _next, _maskMat, PassBrush);
            Swap();
        }

        void SetAllow(int part)
        {
            if (part == _allowFor) return;
            _allowFor = part;
            int mask = partNeighbors != null && part >= 0 && part < partNeighbors.Length ? partNeighbors[part] : 0;
            for (int i = 0; i < AllowSlots; i++) _allow[i] = (i == part || (i < PartCount && ((mask >> i) & 1) != 0)) ? 1f : 0f;
            _maskMat.SetFloatArray(_idAllowPart, _allow);
        }

        // Whole mask from the cells (first receive, reset, lost texture, dried out). Restore writes every texel, so
        // the undefined content of a fresh RenderTexture does not matter. Counted in the Blit budget.
        bool Restore()
        {
            _restorePending = false;
            _blitCount++;
            if (_maskMat == null || _cur == null || _next == null || cellTex == null) return false;
            for (int c = 0; c < CellCount; c++) _cellColors[c] = new Color(_dirt[c], _foam[c], _wet[c], 1f);
            cellTex.SetPixels(_cellColors);
            cellTex.Apply(false);
            _maskMat.SetTexture(_idCellTex, cellTex);
            VRCGraphics.Blit(_cur, _next, _maskMat, PassRestore);
            Swap();
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
            for (int i = 0; i < _qCount; i++)
                AddLog(_qTool[i], _qPart[i], Unpack(_qPart[i], _qFrom[i]), Unpack(_qPart[i], _qTo[i]), _qRmm[i] * 0.001f, _qMs[i] * 0.001f, -1);
            for (int t = 0; t < 3; t++)
                if (_segOpen[t]) AddLog(t, _segPart[t], _segFrom[t], _segTo[t], _segRad[t], _segSec[t], -1);
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
            float now = Time.time;
            float drain = Mathf.Max(foamDrySeconds, wetDrySeconds);
            if (!_anyFoamOrWet && now - _lastActivity > drain) return;
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
            bool changed = false;
            for (int c = 0; c < CellCount; c++)
            {
                if (_foam[c] > 0f || _wet[c] > 0f) changed = true;
                _foam[c] = Mathf.Max(0f, _foam[c] - fd);
                _wet[c] = Mathf.Max(0f, _wet[c] - wd);
            }
            if (changed && Networking.IsOwner(gameObject)) _syncDue = true;
        }

        void Swap()
        {
            RenderTexture t = _cur;
            _cur = _next;
            _next = t;
            if (_shellMat != null) _shellMat.SetTexture(_idMask, _cur);
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
            return spongeRadius;
        }

        // What a stroke of each kind may claim (the shower widens with distance up to 0.2 m, KH_WashTool).
        float MaxToolRadius(int tool)
        {
            if (tool == ToolShower) return 0.2f;
            return Mathf.Clamp(ToolRadius(tool) * 1.1f, 0.02f, 0.25f);
        }

        Vector4 ToolDelta(int tool, float sec)
        {
            if (tool == ToolShower) return new Vector4(showerDirtRate * sec, showerFoamRate * sec, showerWetRate * sec, 0f);
            if (tool == ToolTowel) return new Vector4(towelDirtRate * sec, towelFoamRate * sec, towelWetRate * sec, 0f);
            return new Vector4(0f, spongeFoamRate * sec, spongeWetRate * sec, 0f);
        }

        // The cell version of the Brush pass. Cells are coarser than the band, so every cell within the brush plus
        // cellReach takes the brush's full weight (cellGain tunes it against the mask in Play); the amount is the
        // same Dwell() as the mask's and the reaction is the shader's (ApplyCell).
        void ApplyToCells(int tool, int part, Vector3 a, Vector3 b, float radMeters, float sec)
        {
            if (!_cellsOk) return;
            float rMesh = radMeters / meshToWorldScale;
            float reach = (radMeters + cellReach) / meshToWorldScale;
            float reach2 = reach * reach;
            Vector4 delta = ToolDelta(tool, Dwell(sec, a, b, rMesh) * cellGain);
            float gateParam = tool == ToolShower ? Mathf.Clamp01(foamRinseGate) : 0f;
            int mask = partNeighbors != null && part < partNeighbors.Length ? partNeighbors[part] : 0;
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
                    if ((p - (a + ab * t)).sqrMagnitude > reach2) continue;
                    ApplyCell(c, delta, gateParam);
                }
            }
        }

        // Same reaction as KH_WashMask.shader FragBrush (w = 1): foam growth eases near full, dirt removal is boosted
        // by foam, and while rinsing the dirt under foam follows the foam's ratio (the gate).
        void ApplyCell(int c, Vector4 delta, float gateParam)
        {
            float dirt = _dirt[c];
            float foam = _foam[c];
            float dF = delta.y;
            float ease = dF > 0f ? (1f - _foamGrowEase * foam) : 1f;
            float foamNew = Mathf.Clamp01(foam + dF * ease);
            float dD = delta.x;
            float boost = dD < 0f ? (1f + foam * rinseFoamBoost) : 1f;
            float dirtNormal = Mathf.Clamp01(dirt + dD * boost);
            float rinsing = (delta.x < 0f && delta.y < 0f) ? 1f : 0f;
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

        // Every 0.5 s: averages over the valid cells, the shell switch, and (Owner) the clean edge with hysteresis.
        void EvaluateState()
        {
            if (_dirt == null) return;
            float sd = 0f;
            float sf = 0f;
            int n = 0;
            bool dirty = false;
            bool foamWet = false;
            for (int c = 0; c < CellCount; c++)
            {
                if (_cellsOk && !cellValid[c]) continue;
                float d = _dirt[c];
                float f = _foam[c];
                float w = _wet[c];
                sd += d;
                sf += f;
                n++;
                if (f > 0f || w > 0f) foamWet = true;
                if (d > 0.002f) dirty = true;   // dirt never dries; a rinse leaves tiny floats behind
            }
            _avgDirt = n > 0 ? sd / n : 0f;
            _avgFoam = n > 0 ? sf / n : 0f;
            _anyFoamOrWet = foamWet;
            if (foamWet) _lastActivity = Time.time;
            _cellsEmpty = !dirty && !foamWet;

            // The shell goes off only when the cells are all zero AND the mask has had a full drying time since the
            // last stroke / foam / wet (its band can outlast the coarse cells). The mask is then set to the cells
            // (all zero), so a residue cannot come back on the next touch.
            float drain = Mathf.Max(foamDrySeconds, wetDrySeconds);
            bool off = _cellsEmpty && Time.time - _lastActivity > drain + 2f;
            if (off && _shellOn) { _restorePending = true; ShowShell(false); }
            else if (!off && !_shellOn) ShowShell(true);

            // The clean edge (formulas in the header): arm on the average dirt or on enough foamed cells anywhere
            // (legs only is fine); fire when the averages are low AND the foamed set itself has been rinsed.
            if (!Networking.IsOwner(gameObject) || epoch == 0) return;
            // Quiet cells (still foamy at the last edge) leave the quiet set once rinsed / dried below the clean
            // threshold; until then they are the rest of the same wash and do not count toward a new arm.
            int fresh = 0;
            bool quietChanged = false;
            for (int c = 0; c < CellCount; c++)
            {
                if (_cellsOk && !cellValid[c]) continue;
                int bit = 1 << (c & 31);
                int w = c >> 5;
                bool quiet = (quietBits[w] & bit) != 0;
                if (quiet && _foam[c] < cleanFoamThreshold) { quietBits[w] &= ~bit; quiet = false; quietChanged = true; }
                if (!quiet && _foam[c] >= dirtyFoamThreshold) fresh++;
            }
            if (quietChanged) _syncDue = true;
            if (!cleanArmed)
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
            if (MarkArm()) _syncDue = true;
            float setAvg = ArmAverage();   // U#: no out parameters on own methods; the count is in _armCount
            if (_avgDirt < cleanDirtThreshold && _avgFoam < cleanFoamThreshold && (_armCount == 0 || setAvg < cleanFoamThreshold))
            {
                cleanArmed = false;         // net: owner-only
                ClearArm();
                // net: owner-only (quietBits): what is still foamy now is left over from this wash.
                for (int c = 0; c < CellCount; c++)
                    if ((!_cellsOk || cellValid[c]) && _foam[c] >= dirtyFoamThreshold) quietBits[c >> 5] |= 1 << (c & 31);
                cleanSerial++;
                _syncDue = true;
                _nextSync = Time.time;      // the edge is what the dog waits for
            }
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

        // Average foam over the arm set (0 when empty); the set's size goes to _armCount.
        private int _armCount;
        float ArmAverage()
        {
            int count = 0;
            float sum = 0f;
            for (int c = 0; c < CellCount; c++)
            {
                if ((armBits[c >> 5] & (1 << (c & 31))) == 0) continue;
                count++;
                sum += _foam[c];
            }
            _armCount = count;
            return count > 0 ? sum / count : 0f;
        }

        // ================= Outgoing =================

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

        // Into the send queue. A full queue does not drop: the newest queued segment of the same tool and part takes
        // this one (its end moves here and the times add up); only when there is none are the two oldest entries of
        // the same kind merged, and only as the last resort the oldest is dropped (it is already drawn locally).
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
                for (int i = QueueMax - 1; i >= 0; i--)
                {
                    if (_qTool[i] != t || _qPart[i] != part) continue;
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
            int k = _qCount;
            _qTool[k] = t;
            _qPart[k] = part;
            _qFrom[k] = Pack(part, _segFrom[t]);
            _qTo[k] = packedTo;
            _qMs[k] = ms;
            _qRmm[k] = rmm;
            _qCount++;
        }

        // Frees one queue slot by merging the first adjacent pair of the same tool and part.
        bool MergeOldestPair()
        {
            for (int i = 0; i + 1 < _qCount; i++)
            {
                if (_qTool[i] != _qTool[i + 1] || _qPart[i] != _qPart[i + 1]) continue;
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
            _qTool[to] = _qTool[from];
            _qPart[to] = _qPart[from];
            _qFrom[to] = _qFrom[from];
            _qTo[to] = _qTo[from];
            _qMs[to] = _qMs[from];
            _qRmm[to] = _qRmm[from];
        }

        void UpdateSend(float now)
        {
            for (int t = 0; t < 3; t++)
            {
                if (!_segOpen[t]) continue;
                if (now - _segLast[t] > SegmentGap) CloseSegment(t, false);
                else if (now - _segStart[t] > SegmentMaxAge) CloseSegment(t, true);
            }
            if (now < _nextSend) return;
            if (_qCount == 0)
            {
                int oldest = -1;
                for (int t = 0; t < 3; t++)
                {
                    if (!_segOpen[t] || now - _segStart[t] < SendInterval) continue;
                    if (oldest < 0 || _segStart[t] < _segStart[oldest]) oldest = t;
                }
                if (oldest >= 0) CloseSegment(oldest, true);
            }
            if (_qCount == 0) return;
            int amount = _qMs[0] | (_qRmm[0] << 12);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetStroke), _qTool[0], _qPart[0], _qFrom[0], _qTo[0], amount, _seq, epoch);
            if (!_gotFirst) LogSent(_qTool[0], _qPart[0], _qFrom[0], _qTo[0], _qMs[0], _qRmm[0], _seq);
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

        // Foam of the nearest cell of the part (cached 0.1 s: this runs every frame while washing).
        float FoamNear(int part, Vector3 meshPoint)
        {
            float now = Time.time;
            if (part == _foamNearPart && now - _foamNearAt < 0.1f) return _foamNearValue;
            _foamNearPart = part;
            _foamNearAt = now;
            _foamNearValue = 0f;
            if (!_cellsOk) return 0f;
            float bestD = -1f;
            int end = _partCellStart[part + 1];
            for (int k = _partCellStart[part]; k < end; k++)
            {
                int c = _partCells[k];
                float d = (cellCenter[c] - meshPoint).sqrMagnitude;
                if (bestD < 0f || d < bestD) { bestD = d; _foamNearValue = _foam[c]; }
            }
            return _foamNearValue;
        }

        // Physical cause only (splash, foam bits, contact loops), at the stroke itself - own and received alike.
        void ContactEffects(int tool, int part, Vector3 world, Vector3 meshPoint, float sec)
        {
            float now = Time.time;
            float foamHere = FoamNear(part, meshPoint);
            if (tool == ToolShower)
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
            if (tool == ToolSponge || (tool == ToolShower && foamHere > 0.2f))
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

        void UpdateAudio(float now)
        {
            float step = audioFadeSpeed * Time.deltaTime;
            bool shower = now < _showerUntil;
            Fade(contactFur, shower ? _furTarget : 0f, step);
            Fade(contactFoam, shower ? _foamTarget : 0f, step);
            Fade(scrub, now < _scrubUntil ? scrubVolume : 0f, step);
        }

        void Fade(AudioSource s, float target, float step)
        {
            if (s == null) return;
            float v = Mathf.MoveTowards(s.volume, target, step);
            s.volume = v;
            if (v > 0.001f) { if (!s.isPlaying) s.Play(); }
            else if (s.isPlaying) s.Stop();
        }

        void SilenceAll()
        {
            if (contactFur != null) { contactFur.volume = 0f; contactFur.Stop(); }
            if (contactFoam != null) { contactFoam.volume = 0f; contactFoam.Stop(); }
            if (scrub != null) { scrub.volume = 0f; scrub.Stop(); }
        }
    }
}
