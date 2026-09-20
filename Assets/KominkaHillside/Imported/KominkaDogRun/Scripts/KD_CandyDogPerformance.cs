using UdonSharp;
using UnityEngine;
using UnityEngine.Playables;
using VRC.SDKBase;

namespace KominkaDogRun
{
    /// <summary>Local dog/outfit renderer. Production reads the authenticated Live ledger;
    /// copied-scene auditions retain their explicit preview-only entry.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public partial class KD_CandyDogPerformance : UdonSharpBehaviour
    {
        public bool previewSceneOnly;
        public bool autoStartPreview = true;
        public bool autoBeginPerformance = true;
        public PlayableDirector director;
        public KD_DogWanderController[] dogs;
        public Transform[] slots;
        public KD_AccessoryBox accessoryBox;
        public KD_GroupActivities activities;
        public GameObject[] costumeNodes;
        public float[] cueTimes = new float[0];
        public int[] cueMasks = new int[0];
        public float[] cueYaw = new float[0];
        // Reserved for validated breed-specific steps; yaw/gaze accents are currently used.
        public float[] cueSteps = new float[0];
        public float sitTime = 115.2f;
        public float duration = 124.8f;
        public float gatherTimeout = 45f;
        public float tailSeconds = 3f;
        public float startDelay = 2f;
        public int phase; // 0 idle, 1 preparing, 2 playing, 3 completed, -1 cancelled/failed
        public string failureReason = "";
        public Vector3[] originalPositions = new Vector3[6];
        public Quaternion[] originalRotations = new Quaternion[6];
        private float _startedAt;
        private float _readySince = -1f;
        private float _playStartedAt;
        private bool _autoAttempted;
        private bool _reserved;
        private bool _started;
        private float _lastTimelineTime;
        private float _lastProgressAt;

        // net: callable-ok: read-only preparation state
        public bool AllReady
        {
            get
            {
                if (!_reserved || dogs == null || dogs.Length != 6) return false;
                for (int i = 0; i < 6; i++)
                    if (dogs[i] == null || !dogs[i].CandyReady || !dogs[i].fetchController.IsIdle) return false;
                return true;
            }
        }

        private void Start() { _started = true; }

        public void _Prepare()
        {
            // The Editor builder verifies playableAsset; that getter is not exposed to Udon.
            if (!previewSceneOnly || _reserved || director == null) return;
            if (!Utilities.IsValid(Networking.LocalPlayer)) return;
            failureReason = "";
            if (dogs == null || slots == null || dogs.Length != 6 || slots.Length != 6
                || costumeNodes == null || costumeNodes.Length != 18 || accessoryBox == null)
            { Fail("Preview needs six dogs, six slots and eighteen fitted accessory nodes."); return; }
            if (cueTimes == null || cueMasks == null || cueYaw == null || cueTimes.Length == 0
                || cueTimes.Length != cueMasks.Length || cueTimes.Length != cueYaw.Length)
            { Fail("Body cue arrays are incomplete."); return; }
            if (useChoreography && !ChoreographyValid())
            { Fail("Choreography or dog-light bindings are incomplete."); return; }
            if (!MountValid()) { Fail("Mounting requires a fully validated copied-scene setup."); return; }
            for (int i = 0; i < 18; i++)
                if (costumeNodes[i] == null) { Fail("Missing fitted accessory node."); return; }
            for (int i = 0; i < 6; i++)
            {
                if (dogs[i] == null || slots[i] == null || dogs[i].dogRoot == null
                    || dogs[i].fetchController == null || !dogs[i].enabled || !dogs[i].gameObject.activeInHierarchy
                    || !dogs[i].dogRoot.gameObject.activeInHierarchy
                    || !Networking.IsOwner(dogs[i].dogRoot.gameObject)
                    || !Networking.IsOwner(dogs[i].fetchController.gameObject))
                { Fail("Six active locally owned dogs are required by this ClientSim preview."); return; }
                if (!dogs[i].CandyMountAvailable)
                { Fail("The previous landing and standing recovery must finish before another preview starts."); return; }
                for (int j = 0; j < i; j++)
                    if (dogs[j] == dogs[i]) { Fail("Duplicate dog in preview roster."); return; }
                KD_SleepSlot sleep = dogs[i].sleepBoard == null ? null : dogs[i].sleepBoard.SlotOf(dogs[i].dogIndex);
                if (sleep != null && !Networking.IsOwner(sleep.gameObject))
                { Fail("A companion slot has a different owner; preparation did not start."); return; }
            }
            director.Stop();
            director.time = 0;
            _reserved = true;
            SetCostumeGlow(0);
            phase = 1;
            _startedAt = Time.time;
            _readySince = -1f;
            if (activities != null && activities.Busy) activities._Request(0, 0, 0);
            for (int i = 0; i < 6; i++)
            {
                originalPositions[i] = dogs[i].dogRoot.position;
                originalRotations[i] = dogs[i].dogRoot.rotation;
                dogs[i].candyPerformanceSlot = slots[i];
                dogs[i].candyPerformanceActive = true;
                dogs[i].candyPerformanceYaw = 0f;
                dogs[i].candyPerformanceEnergy = 0f;
                // A cue on the very first note must already be prepared before audio starts.
                for (int c = 0; c < cueTimes.Length && cueTimes[c] <= .001f; c++)
                    if ((cueMasks[c] & (1 << i)) != 0)
                    {
                        dogs[i].candyPerformanceYaw = Mathf.Clamp(cueYaw[c], -18f, 18f);
                        dogs[i].candyPerformanceEnergy = 1f;
                    }
                dogs[i].candyPerformanceSit = false;
                dogs[i].candyMotionEnabled = false;
                dogs[i].fetchController._EndForCandyPerformance();
                KD_SleepSlot sleep = dogs[i].sleepBoard == null ? null : dogs[i].sleepBoard.SlotOf(dogs[i].dogIndex);
                if (sleep != null) sleep._ReleaseForCandyPerformance();
                dogs[i]._BeginCandyPerformance();
            }
            // Gather through the same motor, tolerance, facing and pose used at t=0.
            // A separate slot-arrival path caused a tiny corrective walk and turn on the first note.
            if (useChoreography)
            {
                _motionCursor = 0;
                UpdateBodyCues(0f);
                UpdateChoreography(0f);
            }
            ConfigureMount();
            accessoryBox._SetCandyPerformanceOverlay(true);
            for (int i = 0; i < 18; i++) costumeNodes[i].SetActive(true);
        }

        public void _BeginPerformance()
        {
            if (phase != 1 || !AllReady || director == null || !director.gameObject.activeInHierarchy) return;
            // In externally driven mode the Editor harness has checked Playing.
            // PlayableDirector.state is not exposed to Udon in this SDK.
            if (autoBeginPerformance)
            {
                director.time = 0;
                director.Play();
            }
            _playStartedAt = Time.time - (float)director.time;
            _lastTimelineTime = (float)director.time;
            _lastProgressAt = Time.time;
            _motionCursor = 0;
            for (int i = 0; i < 6; i++) dogs[i].candyMotionGathering = false;
            phase = 2;
        }

        private void Update()
        {
            if (!_started) return;
            if (!previewSceneOnly) { UpdateLivePerformance(); return; }
            if (autoStartPreview && !_autoAttempted && Time.timeSinceLevelLoad >= startDelay
                && Utilities.IsValid(Networking.LocalPlayer))
            { _autoAttempted = true; _Prepare(); }
            if (!_reserved) return;
            if (phase == 2 && Time.time - _playStartedAt >= duration + tailSeconds)
            { Finish(); return; }
            // Existing LiveSystem disables its stage at the musical ending, before
            // the three-second lighting/outfit restoration tail has elapsed.
            if (phase == 2 && director != null && !director.gameObject.activeInHierarchy
                && Time.time - _playStartedAt >= duration - .1f) return;
            // LiveSystem keeps its stage inactive until NightIn is complete.
            // Preparation runs from this separate active coordinator.
            if (director == null || (phase == 2 && !director.gameObject.activeInHierarchy))
            { Fail("The preview director became unavailable."); return; }
            for (int i = 0; i < 6; i++)
            {
                if (dogs[i] == null || slots[i] == null || dogs[i].dogRoot == null
                    || dogs[i].fetchController == null || !dogs[i].enabled || !dogs[i].gameObject.activeInHierarchy
                    || !dogs[i].dogRoot.gameObject.activeInHierarchy
                    || !Networking.IsOwner(dogs[i].dogRoot.gameObject)
                    || !Networking.IsOwner(dogs[i].fetchController.gameObject))
                { Fail("An audition dog became unavailable or changed owner."); return; }
            }
            if (phase == 1)
            {
                if (Time.time - _startedAt > gatherTimeout) { Fail("Six-dog preparation timed out."); return; }
                if (AllReady)
                {
                    if (_readySince < 0f) _readySince = Time.time;
                    if (autoBeginPerformance && Time.time - _readySince >= .75f) _BeginPerformance();
                }
                else _readySince = -1f;
                return;
            }
            if (phase != 2) return;
            if (Time.time - _playStartedAt > duration + tailSeconds + 10f)
            { Fail("Preview playback watchdog expired."); return; }
            float t = (float)director.time;
            if (t >= duration + tailSeconds || Time.time - _playStartedAt >= duration + tailSeconds)
            { Finish(); return; }
            if (t > _lastTimelineTime + .0001f) _lastProgressAt = Time.time;
            if (Time.time - _lastProgressAt > 1f && Mathf.Max(t, _lastTimelineTime) < duration - .1f)
            { Fail("Timeline stopped before its expected ending."); return; }
            t = Mathf.Max(t, _lastTimelineTime);
            _lastTimelineTime = t;
            UpdateCostumeGlow(t);
            if (!UpdateMountClock(t)) return;
            UpdateBodyCues(t);
            if (useChoreography) UpdateChoreography(t);
        }

        public void _Cancel()
        {
            if (!previewSceneOnly) { if (liveSystem != null) liveSystem._StopLive(); return; }
            Fail("Preview cancelled.");
        }
        private void Fail(string reason) { failureReason = reason; Cleanup(); phase = -1; }
        private void Finish() { Cleanup(); phase = 3; }
        private void Cleanup()
        {
            SetCostumeGlow(0);
            if (!_reserved) return;
            _reserved = false;
            if (previewSceneOnly && director != null) director.Stop();
            RestoreDogLightOffsets();
            if (costumeNodes != null)
                for (int i = 0; i < costumeNodes.Length; i++) if (costumeNodes[i] != null) costumeNodes[i].SetActive(false);
            if (accessoryBox != null) accessoryBox._SetCandyPerformanceOverlay(false);
            KD_DogWanderController landing = null;
            if (dogs != null) for (int i = 0; i < dogs.Length; i++)
                if (dogs[i] != null && dogs[i].CandyMountBusy) landing = dogs[i];
            if (dogs != null)
                for (int i = 0; i < dogs.Length; i++) if (dogs[i] != null)
                {
                    // Hold the other actors while the pair lands, including when
                    // this coordinator is being disabled and cannot update again.
                    if (landing != null) { dogs[i].candyRecoveryRider = landing; dogs[i].candyExitHoldUntil = Time.time + 3f; }
                    dogs[i]._EndCandyPerformance();
                }
            // Do not restore old reservations, equipment or old root positions. Normal AI resumes here.
        }
        private void OnDisable() { if (_reserved) Fail("Preview coordinator disabled."); }
    }
}
