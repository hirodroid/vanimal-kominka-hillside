using UnityEngine;

namespace KominkaDogRun
{
    public partial class KD_CandyDogPerformance
    {
        // Opt-in on copied revision 002 only; serialized 001 keeps its original movement.
        public bool useChoreography;
        public Transform choreographySpace;
        public float[] motionTimes = new float[0];
        public Vector3[] motionPositions = new Vector3[0];
        public float[] motionYaws = new float[0];
        public int[] motionPoses = new int[0];
        public int[] motionPartners = new int[0];
        public float[] motionSpeedLimits = new float[0];
        public Transform[] dogLightLayers = new Transform[0];
        public int[] dogLightDogs = new int[0];
        public Vector3[] dogLightOrigins = new Vector3[0];
        public Vector3[] dogLightBaselines = new Vector3[0];
        public float motionLookAhead = .18f;
        public float motionErrorLimit = 1.25f;
        public float motionErrorGrace = 2f;
        [HideInInspector] public int dogLightUpdatedFrame = -1;
        public float maxMotionError;
        private int _motionCursor;
        private float _motionBadSince = -1f;

        private bool ChoreographyValid()
        {
            if (choreographySpace == null || motionTimes == null || motionTimes.Length < 2) return false;
            int n = motionTimes.Length * 6;
            if (motionPositions == null || motionPositions.Length != n || motionYaws == null || motionYaws.Length != n
                || motionPoses == null || motionPoses.Length != n || motionPartners == null || motionPartners.Length != n
                || motionSpeedLimits == null || motionSpeedLimits.Length != n) return false;
            if (dogLightLayers == null || dogLightDogs == null || dogLightOrigins == null || dogLightBaselines == null
                || dogLightLayers.Length != dogLightDogs.Length || dogLightLayers.Length != dogLightOrigins.Length
                || dogLightLayers.Length != dogLightBaselines.Length) return false;
            _motionBadSince = -1f; maxMotionError = 0f;
            return true; // Full numeric, path, material and child-curve checks are Editor gates.
        }

        private void UpdateChoreography(float t)
        {
            int last = motionTimes.Length - 1;
            while (_motionCursor < last - 1 && motionTimes[_motionCursor + 1] <= t) _motionCursor++;
            int k = _motionCursor;
            float span = motionTimes[k + 1] - motionTimes[k];
            float u = Mathf.Clamp01((t - motionTimes[k]) / span);
            int ahead = k;
            float aheadTime = Mathf.Min(t + motionLookAhead, duration);
            while (ahead < last - 1 && motionTimes[ahead + 1] <= aheadTime) ahead++;
            float au = Mathf.Clamp01((aheadTime - motionTimes[ahead]) / (motionTimes[ahead + 1] - motionTimes[ahead]));
            float worst = 0f;
            for (int d = 0; d < 6; d++)
            {
                int a = k * 6 + d, b = (k + 1) * 6 + d;
                Vector3 expected = choreographySpace.TransformPoint(Vector3.Lerp(motionPositions[a], motionPositions[b], u));
                Vector3 target = choreographySpace.TransformPoint(Vector3.Lerp(motionPositions[ahead * 6 + d], motionPositions[(ahead + 1) * 6 + d], au));
                // The ordinary ground motor must remain settled until launch;
                // looking ahead into the airborne curve would start it early.
                if (useMount && d == mountRider && t < mountStart && aheadTime >= mountStart)
                    target = choreographySpace.TransformPoint(mountTakeoff);
                Vector3 error = expected - dogs[d].dogRoot.position;
                if (!(useMount && d == mountRider && t >= mountStart && t <= mountLand)) error.y = 0f;
                float distance = error.magnitude;
                if (distance > worst) worst = distance;
                dogs[d].candyMotionEnabled = true;
                dogs[d].candyMotionGathering = phase == 1;
                dogs[d].candyMotionExpected = expected;
                dogs[d].candyMotionTarget = target;
                dogs[d].candyMotionFacing = choreographySpace.TransformDirection(
                    Quaternion.Euler(0f, Mathf.LerpAngle(motionYaws[a], motionYaws[b], u), 0f) * Vector3.forward);
                dogs[d].candyMotionSpeedLimit = motionSpeedLimits[a];
                dogs[d].candyMotionPose = motionPoses[a];
                int partner = motionPartners[a];
                dogs[d].candyMotionPartner = partner >= 0 && partner < 6 ? dogs[partner].dogRoot : null;
                dogs[d].candyMotionError = distance;
                dogs[d].candyPerformanceSit = motionPoses[a] == 2;
            }
            // Gathering starts away from the stage; only score tracking once the song runs.
            if (phase != 2) return;
            if (worst > maxMotionError) maxMotionError = worst;
            if (worst > motionErrorLimit)
            {
                if (_motionBadSince < 0f) _motionBadSince = Time.time;
                if (Time.time - _motionBadSince > motionErrorGrace)
                {
                    if (previewSceneOnly) Fail("Choreography path blocked or locomotion is too far behind; no position snap was applied.");
                    // Remote ObjectSync positions lag the authored clock. A local observation
                    // must never stop that viewer's live or reset another owner's dogs.
                    else failureReason = "Observed choreography lag; playback continues on the shared clock.";
                }
            }
            else _motionBadSince = -1f;
        }

        private void LateUpdate()
        {
            if (!_reserved || !useChoreography || choreographySpace == null || dogs == null) return;
            // Timeline still owns actor transforms. Only unanimated PS child-layer offsets change.
            // World-simulated particles already emitted remain behind; local motifs follow the dog.
            for (int i = 0; i < dogLightLayers.Length; i++)
            {
                int d = dogLightDogs[i];
                if (dogLightLayers[i] == null || d < 0 || d >= 6 || dogs[d] == null || dogs[d].dogRoot == null) continue;
                Vector3 worldDelta = dogs[d].dogRoot.position - choreographySpace.TransformPoint(dogLightOrigins[i]);
                Transform parent = dogLightLayers[i].parent;
                if (parent != null)
                    dogLightLayers[i].localPosition = dogLightBaselines[i] + parent.InverseTransformVector(worldDelta);
            }
            dogLightUpdatedFrame = Time.frameCount;
        }

        private void RestoreDogLightOffsets()
        {
            if (!useChoreography || dogLightLayers == null || dogLightBaselines == null) return;
            for (int i = 0; i < dogLightLayers.Length && i < dogLightBaselines.Length; i++)
                if (dogLightLayers[i] != null) dogLightLayers[i].localPosition = dogLightBaselines[i];
        }
    }
}
