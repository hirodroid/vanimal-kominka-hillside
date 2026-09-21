using UnityEngine;
using UnityEngine.AI;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace KominkaDogRun
{
    // Hillside-only adaptation. Existing Owner-only locomotion and animation remain the single writer.
    //
    // IMP-DOG-003 "free walk with a destination": the dog knows the trail and wants to go, the holder
    // sets the pace through the leash. Decisions run on the dog Owner; position reaches everyone through
    // VRCObjectSync. Poses that others must see (LookAt, LieDown) are derived on every client from
    // synced facts (dog pose, holder position/stillness) so Owner and non-Owner screens agree.
    public partial class KD_DogWanderController
    {
        public KominkaHillside.KH_LeashManager hillsideLeashes;
        // IMP-DOG-005: the disc catcher moves the root (Ready and engaged); Wander yields exactly as it does for Fetch.
        public KominkaHillside.KH_FrisbeeCatch frisbeeCatch;
        private bool FrisbeeActive { get { return frisbeeCatch != null && !frisbeeCatch.IsIdle; } }
        // Fetch and the disc catcher own the Animator (bools and playback speed) while they run. Wander's
        // own writes must stand back for exactly as long, or the two fight over Animator.speed (IMP-DOG-009).
        private bool AnimatorYielded() { return (fetchController != null && !fetchController.IsIdle) || FrisbeeActive; }
        // Read by sibling dogs of the same holder to stagger invitations. Meaningful on the dog Owner only.
        [HideInInspector] public int leashMode;
        [HideInInspector] public float leashInviteEndedAt = -1000f; // pack-wide quiet gap between invitations
        const int LeashWalk = 0, LeashWait = 1, LeashReturn = 2, LeashDetour = 3, LeashInvite = 4,
            LeashRest = 5, LeashFree = 6, LeashArrived = 7;

        private NavMeshPath _leashPath;
        private Vector3[] _leashCorners;
        private int _leashCorner;
        private float _nextLeashPath;
        private Vector3 _leashGoal;
        private bool _wasLeashed;
        private int _routeNext = -1;   // next trail node; -1 = not on the trail
        private int _routeDest = -1;
        private float _nextRouteEntry;
        private float _nextHolderSample;
        private Vector3 _holderSample;      // where the holder was when they last counted as moving
        private float _holderMovedAt;
        private bool _arrived;              // held until the destination changes or the holder leaves its zone
        private float _nextTeleportTry;
        private bool _holderOffTrail;
        private float _modeUntil;
        private float _sniffUntil;
        private Vector3 _detourPoint;
        private float _nextDetourRoll;
        private int _inviteCount;
        private float _nextInviteAt;
        private bool _inviteBarked;
        // IMP-DOG-009: how fast the holder is actually moving right now. Owner only, never synced; the 1 Hz
        // SampleHolder below is a different question (has the holder stood still long enough to rest/invite?)
        // and stays as it was. This one picks which of the four speed tiers the dog walks at.
        private float _holderSpeed;         // EMA of the holder's horizontal body speed (m/s)
        private Vector3 _holderPrevPos;
        private bool _holderPrevValid;
        private int _holderTier;            // 0 still / 1 walking / 2 running (hysteresis)
        private int _holderTracked = -1;    // playerId the history belongs to; a new holder starts from zero
        private bool _leashYielded;         // last frame handed the root/Animator to Fetch or the disc
        private bool _wasDogOwner;          // Owner edge: a client that just took over has no history

        // ---- IMP-DOG-009 の仮値 (どれも Play で決める。速さの段そのものは KH_LeashManager 側) ----
        const float HolderSpeedTau = 0.4f;       // 速さの平滑化の時定数 (s)
        const float HolderJumpMeters = 1f;       // 1 フレームでこれ以上飛んだら不連続 (テレポート・リスポーン)
        const float HolderRunEnter = 3f, HolderRunExit = 2.4f;    // 人の走り 4 m/s と歩き 2 m/s の間で切る
        const float HolderMoveEnter = 0.7f, HolderMoveExit = 0.4f; // 「歩き出した」/「止まった」
        const float LeadHoldGain = 2f;           // lead-hold: リード端までの余裕 1 m あたりの上乗せ (m/s / m)
        const float LeadHoldMax = 1.1f;          // lead-hold を続けるリード長の倍率の上限 (復帰の 1.25 倍の内側)
        const float TurnSlowDeg = 60f;           // 体がこれだけ向き違うなら歩きまで落とす (横滑り対策)
        const float TurnEaseDeg = 30f;           // 中間は走りで頭打ち

        public void _InvalidateLeashAnimation() { _leashAnimationDirty = true; }
        private bool _leashAnimationDirty;

        // Sound only, but only the dog Owner (the one deciding invitations) may ring it.
        [NetworkCallable(maxEventsPerSecond: 3)]
        public void LeashInviteBark()
        {
            if (dogRoot == null || NetworkCalling.CallingPlayer != Networking.GetOwner(dogRoot.gameObject)) return;
            Bark();
        }

        private bool UpdateHillsideLeash()
        {
            if (hillsideLeashes == null || dogRoot == null) return false;
            if (!hillsideLeashes._IsLeashed(dogIndex))
            {
                // Unleashing also gives the run clip back its normal playback speed: off the leash the dog
                // never exceeds runSpeed, and Fetch may take over before the next wander frame (Codex D1).
                if (_wasLeashed) { _wasLeashed = false; _speed = 0f; _nextLeashPath = 0f; _routeNext = -1; leashMode = LeashFree; ResetHolderSpeed(); ReleaseAnimatorSpeed(); }
                return false;
            }
            if (!_wasLeashed)
            {
                _leashAnimationDirty = true; _routeNext = -1; _routeDest = -1; _inviteCount = 0; _nextInviteAt = 0f;
                _holderMovedAt = Time.time; _nextHolderSample = 0f; _holderOffTrail = false; _arrived = false; leashMode = LeashWalk;
                ResetHolderSpeed(); _holderTracked = hillsideLeashes.walkers[dogIndex]; _leashYielded = false;
            }
            _wasLeashed = true;
            // Fetch cancels on its own owner tick. Never write the root concurrently with it.
            // The disc does the same (an airborne dog lands before the leash moves it; KH_FrisbeeCatch yields).
            // Both own the Animator too, so the hand-over gets cleaned up once, on the edge (Codex D2).
            if (AnimatorYielded())
            {
                if (!_leashYielded) { _leashYielded = true; YieldAnimator(); }
                return true;
            }
            if (_leashYielded) { _leashYielded = false; ResetHolderSpeed(); } // came back: the pace estimate is stale
            _wantSleep = false; _paused = false;
            _lastBusyTime = Time.time;
            VRCPlayerApi holder = VRCPlayerApi.GetPlayerById(hillsideLeashes.walkers[dogIndex]);
            if (!Utilities.IsValid(holder)) { if (_dogOwner) _speed = 0f; ApplyMoveAnim(); return true; }

            Vector3 handPos = hillsideLeashes._HandPosition(holder, hillsideLeashes.hands[dogIndex]);
            Vector3 holderPos = holder.GetPosition();
            // The pace comes from the body, not the hand: a hand waved beside a standing player is not walking.
            if (_dogOwner)
            {
                int holderId = hillsideLeashes.walkers[dogIndex];
                if (!_wasDogOwner || holderId != _holderTracked) { _holderTracked = holderId; ResetHolderSpeed(); }
                TrackHolderSpeed(holderPos);
            }
            _wasDogOwner = _dogOwner;
            Vector3 toHand = handPos - dogRoot.position; toHand.y = 0f;
            float handDist = toHand.magnitude;
            float leash = hillsideLeashes.leashLength;
            int destNode = hillsideLeashes._DestinationNode(dogIndex);
            KominkaHillside.KH_WalkRoute route = hillsideLeashes.route;
            if (destNode != _routeDest)
            {
                // A new destination is a fresh start: leave rest/invite state and the stillness clock behind.
                _routeDest = destNode; _routeNext = -1; _arrived = false;
                _holderMovedAt = Time.time; _holderSample = holderPos; _inviteCount = 0; _nextInviteAt = 0f;
                if (leashMode == LeashRest || leashMode == LeashInvite) leashMode = LeashWalk;
            }
            if (destNode >= 0)
            {
                float destDist = Horizontal(dogRoot.position, route._Position(destNode));
                if (destDist < route._Radius(destNode)) _arrived = true;
                else if (Horizontal(holderPos, route._Position(destNode)) > route._Radius(destNode) + 6f) _arrived = false;
            }
            else _arrived = false;

            SampleHolder(holderPos, destNode, route);
            float stillFor = Time.time - _holderMovedAt;
            float restAfter = (_arrived ? 20f : hillsideLeashes.inviteAfter + hillsideLeashes.inviteInterval * hillsideLeashes.inviteLimit) + dogIndex * 2f;
            bool settling = stillFor > restAfter && handDist < 2.2f;
            LeashPose(holder, toHand, handDist, settling);
            if (!_dogOwner) { ApplyMoveAnim(); return true; }

            // 1. Stretched too far: come back to the hand (run), teleport only after a real separation.
            if (leashMode == LeashReturn) { if (handDist < leash * 0.6f) { leashMode = LeashWalk; _routeNext = -1; } }
            else if (handDist > leash * 1.25f)
            {
                leashMode = LeashReturn;
                // PCVR のチェック用 (IMP-DOG-009)。走っている人の後ろで戻りが周期的に出るなら、
                // 先行を止めている側 (lead-hold の帯) が狭すぎる
                Debug.Log("[KH_Leash] dog" + dogIndex + " Return handDist=" + handDist.ToString("F1")
                    + " holderSpeed=" + _holderSpeed.ToString("F1"));
            }
            if (leashMode == LeashReturn)
            {
                Vector3 back = handPos - toHand / Mathf.Max(handDist, 0.001f) * 0.9f;
                if (handDist > hillsideLeashes.catchUpDistance && Time.time >= _nextTeleportTry)
                {
                    _nextTeleportTry = Time.time + 1f;
                    if (TeleportTo(back)) return true;
                }
                LeashGo(back, handDist > leash * 1.6f ? hillsideLeashes.sprintSpeed : hillsideLeashes.dashSpeed, 0.2f);
                return true;
            }

            // 2. Holder has stood still for long (or all invitations are spent): settle down beside them until they move.
            if (stillFor > restAfter || (_inviteCount >= hillsideLeashes.inviteLimit && stillFor > 1f))
            {
                leashMode = LeashRest;
                if (handDist > 1.8f) LeashGo(handPos - toHand / handDist * 1.0f, walkSpeed, 0.2f); else Coast();
                return true;
            }

            // 3. No destination, holder off the trail, or no reachable entry: keep them company around the hand.
            if (destNode < 0 || _holderOffTrail) { _routeNext = -1; FollowHand(handDist, leash, LeashFree); return true; }
            if (_arrived) { _routeNext = -1; FollowHand(handDist, leash, LeashArrived); return true; }
            if (_routeNext < 0 && Time.time >= _nextRouteEntry)
            {
                _routeNext = ChooseEntry(destNode, route);
                if (_routeNext < 0) _nextRouteEntry = Time.time + 3f; // nothing reachable: retry later, stay with the holder
            }
            if (_routeNext < 0) { FollowHand(handDist, leash, LeashFree); return true; }

            // 4. Advance along the routing table; arrival at the destination node ends the trip.
            Vector3 nodePos = route._Position(_routeNext);
            if (Horizontal(dogRoot.position, nodePos) < route._Radius(_routeNext) && _routeNext != destNode)
            {
                _routeNext = route._Next(_routeNext, destNode);
                if (_routeNext < 0) { FollowHand(handDist, leash, LeashFree); return true; }
                nodePos = route._Position(_routeNext);
            }
            if (Horizontal(dogRoot.position, nodePos) > 40f) { _routeNext = -1; FollowHand(handDist, leash, LeashFree); return true; }

            // 5. Invitation: holder stopped while the dog waits ahead -> come back, bark once, look, then lead again.
            if (leashMode == LeashInvite)
            {
                if (stillFor < 1f) leashMode = LeashWalk;
                else if (handDist > 1.2f && !_inviteBarked) LeashGo(handPos - toHand / handDist * 0.9f, runSpeed * 0.8f, 0.2f);
                else
                {
                    Coast(); FaceTowards(holderPos);
                    if (!_inviteBarked)
                    {
                        _inviteBarked = true; _modeUntil = Time.time + 2.5f;
                        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(LeashInviteBark));
                    }
                    if (Time.time >= _modeUntil)
                    {
                        _inviteCount++; leashInviteEndedAt = Time.time;
                        _nextInviteAt = Time.time + hillsideLeashes.inviteInterval * (1f + 0.3f * _inviteCount);
                        leashMode = LeashWalk;
                    }
                }
                if (leashMode == LeashInvite) return true;
            }
            else if (stillFor > hillsideLeashes.inviteAfter + dogIndex * 1.3f && handDist > leash * 0.55f && DogAhead(nodePos, toHand)
                && _inviteCount < hillsideLeashes.inviteLimit && Time.time >= _nextInviteAt && !SiblingInviting())
            {
                leashMode = LeashInvite; _inviteBarked = false;
                return true;
            }

            // 6. Detour: with slack on the leash, sniff a spot beside the trail, then carry on.
            if (leashMode == LeashDetour)
            {
                if (handDist > leash * 0.9f || Time.time > _modeUntil) leashMode = LeashWalk;
                else if (Horizontal(dogRoot.position, _detourPoint) > 0.35f && _sniffUntil <= 0f) { LeashGo(_detourPoint, walkSpeed, 0.1f); return true; }
                else
                {
                    if (_sniffUntil <= 0f) _sniffUntil = Time.time + 1.5f + Rand01((int)Time.time, 4211) * 1.5f;
                    Coast();
                    if (Time.time >= _sniffUntil) leashMode = LeashWalk; else return true;
                }
            }
            else if (leashMode == LeashWalk && handDist < leash * 0.5f && Time.time >= _nextDetourRoll)
            {
                _nextDetourRoll = Time.time + 4f;
                if (Rand01((int)Time.time, 4201) < hillsideLeashes.detourChance && PickDetour(nodePos, handPos, leash))
                { leashMode = LeashDetour; _modeUntil = Time.time + 8f; _sniffUntil = 0f; return true; }
            }

            // 7. Lead toward the next node; stop at the end of the leash when ahead of the holder.
            Vector3 nodeDir = nodePos - dogRoot.position; nodeDir.y = 0f;
            bool ahead = DogAhead(nodePos, toHand);
            float waitAt = leash * (0.78f + 0.04f * (dogIndex % 4));
            // Only a standing holder makes the dog stop and wait. While they walk or run the dog keeps
            // leading and trims its pace by the slack it has left (lead-hold) — stopping in front of a
            // running holder is what used to hand them the 1.25x leash end and start a return loop.
            bool leadHold = ahead && HolderMoving() && handDist >= leash * 0.45f && handDist <= leash * LeadHoldMax;
            if (ahead && handDist >= waitAt && !leadHold)
            {
                leashMode = LeashWait;
                Coast();
                if (_speed <= stopSpeedThreshold) FaceTowards(holderPos);
                return true;
            }
            leashMode = LeashWalk;
            float side = Mathf.Sin(Time.time * 0.2f + dogIndex * 1.3f) * 1.1f;
            Vector3 goal = nodePos + Vector3.Cross(Vector3.up, nodeDir.normalized) * side;
            float speed;
            if (!ahead && handDist > 1.2f)
                // Behind the holder: overtaking has to beat the pace they are actually keeping.
                speed = HolderRunning() ? hillsideLeashes.sprintSpeed
                    : (HolderMoving() ? hillsideLeashes.dashSpeed : runSpeed);
            else if (leadHold)
                // Match the holder and add just enough to keep the slack; never past the end of the leash.
                speed = Mathf.Clamp(_holderSpeed + LeadHoldGain * (waitAt - handDist), walkSpeed, hillsideLeashes.sprintSpeed);
            else if (handDist < leash * 0.45f && HolderMoving())
                speed = hillsideLeashes.dashSpeed; // plenty of slack and the holder is going: get out in front
            else speed = walkSpeed;
            LeashGo(goal, speed, 0.3f);
            return true;
        }

        // Every client: LookAt when stopped facing the holder, LieDown when settling. Both derive from
        // synced facts only, so nobody sees a pose the Owner did not choose.
        private void LeashPose(VRCPlayerApi holder, Vector3 toHand, float handDist, bool settling)
        {
            bool stopped = _speed <= stopSpeedThreshold;
            if (settling && stopped) { _wantLieDown = true; return; }
            Vector3 forward = dogRoot.forward; forward.y = 0f;
            if (!stopped || handDist < 0.8f || Vector3.Angle(forward, toHand) > 40f) return;
            _look = true;
            if (petController == null) return;
            petController._SetGaze(holder.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, 0f);
            petController._SetUrge(0.5f);
        }

        // 1 Hz: did the holder move, and are they still near the trail (with hysteresis)?
        private void SampleHolder(Vector3 holderPos, int destNode, KominkaHillside.KH_WalkRoute route)
        {
            if (Time.time < _nextHolderSample) return;
            bool first = _nextHolderSample <= 0f;
            _nextHolderSample = Time.time + 1f;
            // Displacement from where stillness began, so a slow stroll still counts as moving.
            if (first || Vector3.Distance(holderPos, _holderSample) > 0.6f)
            {
                _holderMovedAt = Time.time; _inviteCount = 0; _nextInviteAt = 0f; _holderSample = holderPos;
            }
            if (route == null) { _holderOffTrail = true; return; }
            float trailDist = route._DistanceToTrail(holderPos);
            bool nearDest = destNode >= 0 && Horizontal(holderPos, route._Position(destNode)) < route._Radius(destNode) + 2f;
            if (_holderOffTrail) { if (trailDist < hillsideLeashes.trailOnDistance || nearDest) _holderOffTrail = false; }
            else if (trailDist > hillsideLeashes.trailOffDistance && !nearDest) _holderOffTrail = true;
        }

        private void FollowHand(float handDist, float leash, int mode)
        {
            leashMode = mode;
            // Three tiers around the hand: sprint from outside the leash, run while it is taut, walk on slack.
            float speed = handDist > leash * 1.2f ? hillsideLeashes.sprintSpeed
                : (handDist > leash * 0.7f ? hillsideLeashes.dashSpeed : walkSpeed);
            LeashGo(hillsideLeashes._WalkingTarget(dogIndex), speed, 0.2f);
        }

        // ---- IMP-DOG-009: the holder's own pace ----

        private bool HolderRunning() { return _holderTier >= 2; }
        private bool HolderMoving() { return _holderTier >= 1; }

        private void ResetHolderSpeed()
        {
            _holderPrevValid = false; _holderSpeed = 0f; _holderTier = 0;
        }

        /// <summary>Owner only, every frame. Teleports, respawns and station warps arrive as one huge step,
        /// so those only move the reference point — letting them into the average would read as "running"
        /// for the next second and send every dog off at full speed.</summary>
        private void TrackHolderSpeed(Vector3 holderPos)
        {
            float dt = Time.deltaTime;
            if (dt <= 0.0001f) return;
            holderPos.y = 0f;
            if (!_holderPrevValid) { _holderPrevPos = holderPos; _holderPrevValid = true; return; }
            float delta = Vector3.Distance(holderPos, _holderPrevPos);
            _holderPrevPos = holderPos;
            if (delta > HolderJumpMeters) return;
            _holderSpeed = Mathf.Lerp(_holderSpeed, delta / dt, Mathf.Min(1f, dt / HolderSpeedTau));

            // Hysteresis both ways: a pace hovering on a threshold must not flip the dog's speed every frame.
            int tier = _holderTier;
            if (_holderSpeed >= HolderRunEnter) tier = 2;
            else if (tier == 2 && _holderSpeed < HolderRunExit) tier = _holderSpeed >= HolderMoveExit ? 1 : 0;
            if (tier < 2)
            {
                if (_holderSpeed >= HolderMoveEnter) { if (tier < 1) tier = 1; }
                else if (tier == 1 && _holderSpeed < HolderMoveExit) tier = 0;
            }
            if (tier != _holderTier)
            {
                Debug.Log("[KH_Leash] dog" + dogIndex + " holder tier " + _holderTier + "->" + tier
                    + " v=" + _holderSpeed.ToString("F1"));
                _holderTier = tier;
            }
        }

        /// <summary>Rising edge of the hand-over to Fetch / the disc (Codex D2). They own the root and the
        /// Animator from here, so leave nothing of ours for them to fight: our own pose bool, the playback
        /// speed we scaled, and the walk state (detour point, timers, pace history) that will be stale by
        /// the time the dog comes back.</summary>
        private void YieldAnimator()
        {
            // Pickup は Fetch がくわえポーズで自分で使う Bool。**こちらが立てた分だけ**下ろす
            if (_wasEatPose && dogAnimator != null) dogAnimator.SetBool("Pickup", false);
            _eatPose = false; _wasEatPose = false;
            ReleaseAnimatorSpeed();
            leashMode = LeashWalk;
            _sniffUntil = 0f; _modeUntil = 0f; _detourPoint = dogRoot.position;
            ResetHolderSpeed();
        }

        // Nearest reachable node that still leads to the destination. Path length, not straight distance,
        // so a dog below a switchback enters the lower leg instead of climbing across the slope.
        private int ChooseEntry(int destNode, KominkaHillside.KH_WalkRoute route)
        {
            if (route == null || route.count <= 0) return -1;
            if (_leashPath == null) _leashPath = new NavMeshPath();
            NavMeshHit from;
            if (!NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas)) return -1;
            int best = -1, nearest = -1; float bestScore = 1e9f, nearestLen = 1e9f;
            for (int i = 0; i < route.count; i++)
            {
                if (route._Cost(i, destNode) >= 1e5f) continue;
                Vector3 p = route._Position(i);
                if (Horizontal(dogRoot.position, p) > 30f) continue;
                NavMeshHit to;
                if (!NavMesh.SamplePosition(p, out to, 2f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath) || _leashPath.status != NavMeshPathStatus.PathComplete) continue;
                float len = PathLength(_leashPath);
                if (len < nearestLen) { nearestLen = len; nearest = i; }
                if (len > hillsideLeashes.entryMaxPath) continue;
                float score = len + route._Cost(i, destNode);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            _leashCorners = null; _nextLeashPath = 0f;
            return best >= 0 ? best : nearest;
        }

        private bool PickDetour(Vector3 nodePos, Vector3 handPos, float leash)
        {
            Vector3 dir = nodePos - dogRoot.position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return false;
            dir.Normalize();
            float side = Rand01((int)Time.time, 4231) < 0.5f ? -1f : 1f;
            Vector3 cand = dogRoot.position + dir * (0.5f + Rand01((int)Time.time, 4241) * 1.5f)
                + Vector3.Cross(Vector3.up, dir) * side * (1.5f + Rand01((int)Time.time, 4251) * 1.2f);
            if (Horizontal(cand, handPos) > leash * 0.8f) return false;
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(cand, out hit, 1f, NavMesh.AllAreas)) return false;
            if (_leashPath == null) _leashPath = new NavMeshPath();
            NavMeshHit from;
            if (!NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(from.position, hit.position, NavMesh.AllAreas, _leashPath) || _leashPath.status != NavMeshPathStatus.PathComplete) return false;
            if (PathLength(_leashPath) > 5f) return false;
            _detourPoint = hit.position; _leashCorners = null; _nextLeashPath = 0f;
            return true;
        }

        private bool SiblingInviting()
        {
            if (pack == null || pack.dogWander == null) return false;
            for (int i = 0; i < pack.dogWander.Length; i++)
            {
                if (i == dogIndex || pack.dogWander[i] == null) continue;
                if (hillsideLeashes.walkers[i] != hillsideLeashes.walkers[dogIndex]) continue;
                if (pack.dogWander[i].leashMode == LeashInvite) return true;
                if (Time.time - pack.dogWander[i].leashInviteEndedAt < hillsideLeashes.inviteInterval * 0.5f) return true; // quiet gap for the holder
            }
            return false;
        }

        // Recover only to a baked, reachable surface after a large separation/teleport.
        private bool TeleportTo(Vector3 goal)
        {
            if (_leashPath == null) _leashPath = new NavMeshPath();
            NavMeshHit from, to;
            if (!NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(goal, out to, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath) || _leashPath.status != NavMeshPathStatus.PathComplete) return false;
            VRCObjectSync sync = dogRoot.GetComponent<VRCObjectSync>();
            if (sync != null) sync.FlagDiscontinuity();
            dogRoot.position = to.position; // net: owner-only: _dogOwner checked by the caller
            _speed = 0f; _leashCorners = null; _nextLeashPath = 0f; _routeNext = -1;
            ApplyMoveAnim();
            return true;
        }

        // Walks toward goal along the baked NavMesh. The path is refreshed every 0.35 s or when the goal moves.
        private void LeashGo(Vector3 goal, float speed, float stop)
        {
            if (Time.time >= _nextLeashPath || Vector3.Distance(goal, _leashGoal) > 0.6f)
            {
                _nextLeashPath = Time.time + 0.35f; _leashGoal = goal;
                if (_leashPath == null) _leashPath = new NavMeshPath();
                NavMeshHit from, to;
                _leashCorners = null;
                if (NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(goal, out to, 2f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath)
                    && _leashPath.status != NavMeshPathStatus.PathInvalid)
                { _leashCorners = _leashPath.corners; _leashCorner = 1; }
            }
            if (_leashCorners == null || _leashCorners.Length < 2) { Coast(); return; }
            while (_leashCorner < _leashCorners.Length - 1 && Vector3.Distance(dogRoot.position, _leashCorners[_leashCorner]) < 0.35f) _leashCorner++;
            Vector3 target = _leashCorners[_leashCorner];
            Vector3 delta = target - dogRoot.position; delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.15f) { Coast(); return; }
            float clearance = WallClearance(dogRoot.position, delta / distance, distance);
            if (clearance < 0.05f) { Coast(); return; }
            // IMP-DOG-009: StepToward points the movement at the target instantly but turns the body only at
            // turnSpeedDegPerSec, so at 7 m/s the dog side-slips through a corner. Hold the pace back until
            // the body has come round. The 0.35 m corner advance and the shared decel/turn constants stay.
            Vector3 forward = dogRoot.forward; forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                float turn = Vector3.Angle(forward, delta / distance);
                if (turn > TurnSlowDeg) { if (speed > walkSpeed) speed = walkSpeed; }
                else if (turn > TurnEaseDeg && speed > hillsideLeashes.dashSpeed) speed = hillsideLeashes.dashSpeed;
            }
            StepToward(dogRoot.position + delta / distance * clearance, _leashCorner == _leashCorners.Length - 1 ? stop : 0.05f, speed);
        }

        // Gathering (KD_GroupActivities mode 1) on the hillside: follow a complete NavMesh path to the slot.
        // Returns false when no complete path exists so the caller walks straight (the agreed "pass through" fallback).
        private float _nextGatherPath;
        private bool _gatherPathOk;
        private bool GatherStep(Vector3 goal, float speed)
        {
            if (!_dogOwner) { ApplyMoveAnim(); return true; }
            if (Time.time >= _nextGatherPath || Vector3.Distance(goal, _leashGoal) > 0.6f)
            {
                _nextGatherPath = Time.time + 0.5f; _leashGoal = goal;
                if (_leashPath == null) _leashPath = new NavMeshPath();
                NavMeshHit from, to;
                _leashCorners = null; _gatherPathOk = false;
                if (NavMesh.SamplePosition(dogRoot.position, out from, 2f, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(goal, out to, 2f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _leashPath)
                    && _leashPath.status == NavMeshPathStatus.PathComplete)
                { _leashCorners = _leashPath.corners; _leashCorner = 1; _gatherPathOk = true; }
            }
            if (!_gatherPathOk || _leashCorners == null || _leashCorners.Length < 2) return false;
            while (_leashCorner < _leashCorners.Length - 1 && Vector3.Distance(dogRoot.position, _leashCorners[_leashCorner]) < 0.35f) _leashCorner++;
            bool last = _leashCorner == _leashCorners.Length - 1;
            StepToward(last ? goal : _leashCorners[_leashCorner], 0f, speed);
            return true;
        }

        private float PathLength(NavMeshPath path)
        {
            Vector3[] c = path.corners;
            float len = 0f;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        // The dog is between the holder and the next node (node and hand lie in opposite directions).
        private bool DogAhead(Vector3 nodePos, Vector3 toHand)
        {
            Vector3 nodeDir = nodePos - dogRoot.position; nodeDir.y = 0f;
            return Vector3.Dot(nodeDir, toHand) < 0f;
        }

        private float Horizontal(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
