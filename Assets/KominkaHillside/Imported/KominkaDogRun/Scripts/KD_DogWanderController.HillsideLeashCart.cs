using UnityEngine;

namespace KominkaDogRun
{
    // DES-WLD-004 M2 (おさんぽカート): a leashed dog whose holder rides a cart walks at a fixed spot in front of that cart instead
    // of following the hand, and starts no sniffing (detour / stopover / rest / invitation) while the holder rides.
    // Decisions stay on the dog Owner (UpdateHillsideLeashFull, HillsideLeash.cs); the Owner is often not the rider, so the cart
    // API answers from the cart manager's synced state for any playerId. Getting off needs no special exit: the next decision
    // finds the holder not riding and the normal walk resumes (the route entry is re-chosen, see CartFollow).
    // cartManager null = exactly the old behaviour (every check below is gated on it).
    public partial class KD_DogWanderController
    {
        [Tooltip("DES-WLD-004 M2: おさんぽカート。持ち主が乗車中は犬をカート前方の定位置へ歩かせ、くん活を止める。" +
            "Cart/1 Build が配線する。null = 従来どおり")]
        public KominkaHillside.KH_CartManager cartManager;

        const int LeashCart = 11;              // leashMode: walking at the cart's dog anchor (Owner's bookkeeping; 9/10 = treat)
        const float CartRowStep = 0.8f;        // m: a 4th-6th dog of one holder walks one row further ahead (the API has 3 slots)
        private bool _cartRiding;              // every client, set once per decision: the holder rides a cart (LeashPose reads it)
        private int _cartOrderLogged = -1;     // Owner: the order last logged as cart-following, -1 = not cart-following
        private int _cartStateLogged = -1;     // Owner: 1 = following a moving cart, 0 = standing by a waiting cart (log edge)
        // ---- 2026-10-12 Play fix (stop-go beside the cart): the continuous follow. Desk check in the M2 report ----
        const float CartGain = 1.2f;           // 1/s: extra speed per metre behind the spot (along the cart's forward)
        const float CartMinRatio = 0.35f;      // ahead of the spot: never slower than this x cartSpeed (keeps walking, gets caught up)
        const float CartLead = 0.8f;           // m: the steering target is this far ahead (never reached, so the dog never "arrives")
        const float CartAlign = 0.3f;          // m: within this of the spot, steer along the cart's forward (no weaving)
        const float CartArrive = 0.2f;         // m: stand-by distance at a waiting cart
        const float CartPathFar = 2f;          // m: farther than this behind the spot, walk the NavMesh path
        const float CartStillSpeed = 0.05f;    // m/s: _CartSpeed at or below this = the cart is waiting
        // ---- 2026-10-12 Play fix 2 ("a fixed spot has no walk feel"): the target wanders around the anchor (Owner, local time) ----
        // offset in the cart frame: lateral = amp * (0.6 sin(2pi t/7.3 + 0.6k) + 0.4 sin(2pi t/5.3 + 1.02k + 1.3)), amp 0.6 (0.25 single file)
        //                           forward = 0.9 + 0.3 * (0.6 sin(2pi t/8.6 + 2.1k) + 0.4 sin(2pi t/6.1 + 2.1k + 2.2))  in [+0.6, +1.2]
        //                           (BUG-DOG-014, 2026-10-14: was 0.35 + 0.65 * (...) in [-0.3, +1.0], see the keep line below)
        // k = the dog's order on the holder (slot): neighbours sway sideways nearly together (0.6 rad apart) and take turns ahead
        // (2.1 rad = a third of a turn apart), which is what keeps them from closing in on each other
        const float CartSwayLat = 0.6f, CartSwayLatNarrow = 0.25f;
        const float CartSwayT1 = 7.3f, CartSwayT2 = 5.3f, CartSwayT3 = 8.6f, CartSwayT4 = 6.1f;   // s, all within 5-9
        const float CartSwayLatPhase = 0.6f, CartSwayFwdPhase = 2.1f;   // rad per slot
        const float CartFwdMid = 0.9f, CartFwdAmp = 0.3f;
        const float Tau = 6.2831853f;
        const float CartSpacing = 0.55f;       // m: goals of the holder's dogs are kept at least about this far apart
        // ---- BUG-DOG-014 (2026-10-14, user report "the dog drops back into the cart, quite often"): the keep line ----
        // The anchor (_DogAnchor, DogAhead 0.9) is only 0.30 m past the front bumper (cart body z -0.60..+0.60, x +-0.41). Measured
        // (BakeMesh, root-local z, 2026-10-14): the body reaches ~0.5 m behind the root (Lab torso ~0.5 / tail tip 0.81, Samoyed 0.46,
        // Corgi 0.49, Shiba / Dachshund 0.33, Pomeranian 0.19). On a non-Owner's screen (the rider usually is not the dog's Owner)
        // VRCObjectSync shows the dog late while the cart is drawn from server time (the desk check assumed 0.23 s, not measured):
        // ~0.3 m further back at 1.3 m/s.
        // So the root must stay at least 0.60 + 0.5 + 0.3 = 1.4 m ahead of the cart origin (1.71 with the Lab's tail). The old window
        // (-0.3..+1.0 around the anchor) plus the dawdle (speed x0.6 -> the dog settles ~0.7 m behind its goal) put the root down to
        // 0.34 m (desk check, issue): inside the body. Now the goals stay at or past CartKeepLine, and a root behind the line is
        // never slowed (the dawdle and CartMinRatio give way) and catches up at CartKeepGain (corners: the outer side of a turn needs
        // more than cartSpeed). Lead decision 2026-10-14: the line is 0.8 past the anchor (z 1.7; the sway window still starts at
        // 0.6, so its lower part is held at the line) and the waiting cart's stand-by spot is on the line too.
        // Desk check: root >= 1.51 m (straights, 4 m-radius corners, stops / restarts) at 90 / 30 fps, 1-6 dogs.
        // The goals start at the keep line when the cart starts (2+ dogs: the spacing clamp below); CartBlendTime fades in the sway
        const float CartKeepLine = 0.8f;       // m past the anchor: the goals never fall behind it, a root behind it is not slowed
        const float CartKeepGain = 4f;         // 1/s: extra speed per metre a root is behind the keep line (dashSpeed caps it)
        const int CartRelax = 4;               // relaxation rounds of the spacing (desk check: 2 left pairs at 0.3-0.4 m, 4 none under 0.5)
        private Vector3[] _cartGoals, _cartGoalsNext, _cartBases;   // scratch for the spacing (holder's dogs in order), reused
        const float CartNarrowWidth = 0.7f;    // m: slot 0-1 anchors closer than this = the runtime halved them (single file)
        const float CartBlendTime = 2f;        // s: the wander fades in after the cart starts (BUG-DOG-014: the keep line holds from the start)
        // "almost stopping": every 15-40 s, for 1.5-2.5 s, speed x0.6 and sway x1.5 at the peak (a sine bump, so no jerk). Never a stop,
        // never a sniff pose (LeashPose reads _cartRiding)
        const float CartHesGapMin = 15f, CartHesGapMax = 40f, CartHesMin = 1.5f, CartHesMax = 2.5f;
        const float CartHesSpeed = 0.6f, CartHesSway = 0.5f;
        private float _cartBlend;              // 0..1 wander fade-in, from _cartMoveSince (CartWander)
        private float _cartMoveSince;          // when the cart was last seen starting to move (or the follow began moving)
        const float CartWanderEvery = 0.25f;   // s: the wander (and its spacing) is recomputed at 4 Hz, the decisions reuse it
        private float _cartWanderNext;         // 0 = recompute now
        private Vector3 _cartWanderOff;
        private float _cartHesNext, _cartHesStart, _cartHesUntil;   // 0 = not scheduled / not in one
        private float _cartHesFactor = 1f;     // speed factor of this decision

        /// <summary>Every client, once per leash decision. Synced facts only (the cart manager's rider table), so every screen agrees
        /// up to sync latency.</summary>
        private bool CartHolderRiding(int holderId)
        {
            return cartManager != null && holderId > 0 && cartManager._IsPlayerRiding(holderId);
        }

        /// <summary>This dog's order among the holder's leashed dogs, by dog number (walkers is synced, so every client gets the same
        /// order; both hands count). 0 = first.</summary>
        private int CartOrder(int holderId)
        {
            int[] w = hillsideLeashes.walkers;
            int n = w == null ? 0 : w.Length;
            int order = 0;
            for (int i = 0; i < n && i < dogIndex; i++) if (w[i] == holderId) order++;
            return order;
        }

        /// <summary>Owner only, while the holder rides (after the travel ticket, the return and the wash hold, before rest).
        /// false = no cart anchor this decision (the API returned zero): the caller walks as usual.</summary>
        private bool CartFollow(int holderId, float leash)
        {
            int order = CartOrder(holderId);
            int slot = order % 3;
            int row = order / 3;
            Vector3 anchor = cartManager._DogAnchor(holderId, slot);
            if (anchor.sqrMagnitude < 0.0001f) { CartFollowEnd(); return false; }
            Vector3 fwd = cartManager._CartForward(holderId); fwd.y = 0f;
            bool hasFwd = fwd.sqrMagnitude > 0.0001f;
            if (hasFwd) fwd = fwd.normalized;
            if (row > 0 && hasFwd) anchor += fwd * (CartRowStep * row);
            float cartSpeed = cartManager._CartSpeed(holderId);
            bool moving = cartSpeed > CartStillSpeed && hasFwd;
            int state = moving ? 1 : 0;
            if (order != _cartOrderLogged || state != _cartStateLogged)
            {
                Debug.Log("[KD_Leash] cart-follow dog=" + dogIndex + " slot=" + order + " holder=" + holderId
                    + (moving ? " state=move" : " state=wait"));
                if (moving && _cartStateLogged != 1) { _cartMoveSince = Time.time; _cartWanderNext = 0f; }   // starts: fade the wander in
                _cartOrderLogged = order; _cartStateLogged = state;
            }
            // A sniff in progress ends here, through the single exit (Return's precedent: logged as its own reason)
            if (pausing) EndPause("cart");
            leashMode = LeashCart;
            // The ride carries the holder along the cart track, so the walk's trail progress is stale by the time they get off.
            // Forget it (branch 3's precedent) and let ChooseEntry pick a fresh entry on the first walking decision after the ride.
            // A stopover won before boarding is not cashed in at the stop either
            _routeNext = -1; _routeResume = -1;
            _stopoverPending = -1; _stopoverRolledFor = -1;
            // The height is the dog's own (SnapToGround inside the step); the anchor's y is never used
            Vector3 pos = dogRoot.position;
            anchor.y = pos.y;
            Vector3 toAnchor = anchor - pos;
            float d = toAnchor.magnitude;
            if (!moving)
            {
                // The cart is waiting (start grace, traffic hold, arrived): the stand-by - walk onto the stand spot and stand there.
                // StepToward coasts to a stop inside CartArrive. No sniff / LieDown (LeashPose and settling read _cartRiding)
                _cartBlend = 0f; _cartHesUntil = 0f; _cartHesFactor = 1f; _cartWanderNext = 0f;   // the wander stops; it fades in again on the next start
                // BUG-DOG-014 (lead decision 2026-10-14, plan A): stand on the keep line, not on the bare anchor (a big dog standing
                // there had its hindquarters in the front bumper, and every restart started from inside it). _DogAnchor is unchanged
                Vector3 stand = anchor + fwd * CartKeepLine;   // fwd ~ zero without a heading: the bare anchor, as before
                float ds = (stand - pos).magnitude;
                if (ds > CartPathFar) LeashGo(stand, walkSpeed, CartArrive);
                else CartStep(stand, CartArrive, walkSpeed);
                return true;
            }
            // Moving cart (2026-10-12 Play fix: the dog used to arrive at the anchor, stop, and start again once it had moved on,
            // so it was seen beside the cart, stop-go). Now it never "arrives": a continuous speed law on the gap along the
            // cart's forward direction, and a steering target always CartLead ahead of the dog.
            //   dAlong = (anchor - dog) . fwd   (> 0: the dog is behind its spot)
            //   v = cartSpeed + CartGain * dAlong, clamped to [CartMinRatio * cartSpeed, dashSpeed]
            // At the spot v = cartSpeed; ahead of it the dog slows but keeps walking and lets the cart catch up.
            // The goal is the anchor plus the slow wander (fix 2); everything below converges on the goal, as before on the anchor
            if (Time.time >= _cartWanderNext) { _cartWanderOff = CartWander(holderId, order, fwd); _cartWanderNext = Time.time + CartWanderEvery; }
            Vector3 goal = anchor + _cartWanderOff;
            goal.y = pos.y;
            // BUG-DOG-014 (Codex review): the spacing clamp in CartWander does not cover a single dog, the fade-in after a start
            // (_cartBlend 0: the goal on the bare anchor) or a turn (the 0.25 s old offset is a world vector). Clamp the goal to the
            // keep line once more against this decision's anchor and heading (a no-op whenever CartWander's clamp already holds)
            float goalAhead = Vector3.Dot(goal - anchor, fwd);
            if (goalAhead < CartKeepLine) goal += fwd * (CartKeepLine - goalAhead);
            toAnchor = goal - pos;
            d = toAnchor.magnitude;
            float dAlong = Vector3.Dot(toAnchor, fwd);
            Vector3 lateral = toAnchor - fwd * dAlong;
            float v = Mathf.Clamp(cartSpeed + CartGain * dAlong, CartMinRatio * cartSpeed, hillsideLeashes.dashSpeed) * _cartHesFactor;
            // BUG-DOG-014: the keep line (CartKeepLine past the anchor, along the cart). Floor = cartSpeed + CartKeepGain x (how far
            // the root is behind it): behind the line at least the cart's speed plus a catch-up, whatever the dawdle, the goal or the
            // slow-down ahead of it say; ahead of it the floor drops below cartSpeed and fades out, so the dawdle still slows the dog
            // and finds a resting point just ahead of the line (no on/off switch at the line: a switched floor made the speed jump
            // between decisions, desk check). Arithmetic on this decision's values only
            float behind = CartKeepLine - Vector3.Dot(pos - anchor, fwd);
            v = Mathf.Max(v, Mathf.Min(cartSpeed + CartKeepGain * behind, hillsideLeashes.dashSpeed));
            if (d > CartPathFar && dAlong > 0f)
            {
                // Far behind or off to the side: the NavMesh path to the spot (around obstacles). Return / the teleport above
                // still take over past 1.25x leash from the hand
                LeashGo(goal + fwd * CartLead, v, 0.05f);
                return true;
            }
            // Near the spot (or ahead of it): steer straight. Behind: toward anchor + fwd * CartLead (pure pursuit, the lateral
            // error closes as the dog walks). Within CartAlign of the spot or ahead of it: the target is CartLead ahead of the dog
            // along the cart's forward plus the lateral error only, so the body lines up with the cart instead of weaving
            float along = (d <= CartAlign || dAlong < 0f) ? 0f : dAlong;
            CartStep(pos + fwd * (along + CartLead) + lateral, 0.05f, v);
            return true;
        }

        /// <summary>Owner, moving cart: the wander offset (cart frame, world vector) for this decision, and the "almost stopping"
        /// speed factor (_cartHesFactor). Constants and formulas above.</summary>
        private Vector3 CartWander(int holderId, int order, Vector3 fwd)
        {
            float now = Time.time;
            if (_cartHesNext <= 0f) _cartHesNext = now + CartHesGapMin + Rand01((int)(now * 10f), 4301) * (CartHesGapMax - CartHesGapMin);
            if (_cartHesUntil <= 0f && now >= _cartHesNext)
            {
                _cartHesStart = now;
                _cartHesUntil = now + CartHesMin + Rand01((int)(now * 10f), 4311) * (CartHesMax - CartHesMin);
                Debug.Log("[KD_Leash] cart-follow dog=" + dogIndex + " dawdle " + (_cartHesUntil - now).ToString("F1") + "s");
            }
            float h = 0f;
            if (_cartHesUntil > 0f)
            {
                if (now >= _cartHesUntil)
                {
                    _cartHesUntil = 0f;
                    _cartHesNext = now + CartHesGapMin + Rand01((int)(now * 10f), 4321) * (CartHesGapMax - CartHesGapMin);
                }
                else h = Mathf.Sin(Mathf.PI * (now - _cartHesStart) / (_cartHesUntil - _cartHesStart));
            }
            _cartHesFactor = 1f - (1f - CartHesSpeed) * h;
            // single file: the runtime halves the slot spacing then, so the sway narrows too
            Vector3 a0 = cartManager._DogAnchor(holderId, 0), a1 = cartManager._DogAnchor(holderId, 1), a2 = cartManager._DogAnchor(holderId, 2);
            bool narrow = Horizontal(a0, a1) < CartNarrowWidth;
            float amp = narrow ? CartSwayLatNarrow : CartSwayLat;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);   // the runtime's right (+ = right of travel)
            _cartBlend = Mathf.Clamp01((now - _cartMoveSince) / CartBlendTime);
            // Keep clear of the holder's other dogs. Not their positions (targets never mix in other dogs' local positions,
            // DES-NET-001): their goals, from the same formula, the same anchors and this clock (their dawdle is theirs, so it is
            // left out for them). The slot phases alone could not do it - the middle slot is only 0.45 m from each side, and with
            // independent sway a pair was under 0.5 m about a quarter of the time (desk check). So every dog relaxes the whole set
            // the same way (CartRelax rounds): a pair closer than CartSpacing moves apart by half the shortfall each, along a fixed
            // axis - sideways by slot (0 left, 2 middle, 1 right) for different slots, forward by row for the same slot - so two
            // dogs never swap sides and the goals move continuously. Desk check (3 and 6 dogs, 15 min): every pair >= 0.50 m.
            int[] w = hillsideLeashes.walkers;
            int n = w == null ? 0 : w.Length;
            if (_cartGoals == null || _cartGoals.Length < n)
            { _cartGoals = new Vector3[n]; _cartGoalsNext = new Vector3[n]; _cartBases = new Vector3[n]; }
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (w[i] != holderId) continue;
                Vector3 bk = CartSlotBase(count, a0, a1, a2, fwd); bk.y = 0f;
                Vector3 sk = CartSway(count, now, amp, i == dogIndex ? 1f + CartHesSway * h : 1f, right, fwd) * _cartBlend;
                _cartBases[count] = bk;
                _cartGoals[count] = bk + sk;
                count++;
            }
            if (order >= count) return Vector3.zero;   // walkers changed under us (cannot happen within one decision): no wander
            for (int r = 0; count > 1 && r < CartRelax; r++)
            {
                for (int k = 0; k < count; k++)
                {
                    Vector3 m = _cartGoals[k];
                    float px = 0f, pf = 0f;
                    for (int q = 0; q < count; q++)
                    {
                        if (q == k) continue;
                        Vector3 dv = m - _cartGoals[q];
                        float dx = Vector3.Dot(dv, right), df = Vector3.Dot(dv, fwd);
                        if (k % 3 != q % 3)
                        {
                            float sg = CartLatRank(k % 3) > CartLatRank(q % 3) ? 1f : -1f;
                            float need = Mathf.Sqrt(Mathf.Max(0f, CartSpacing * CartSpacing - df * df));
                            float have = dx * sg;
                            if (have < need) px += sg * (need - have) * 0.5f;
                        }
                        else
                        {
                            float sg = k / 3 > q / 3 ? 1f : -1f;
                            float need = Mathf.Sqrt(Mathf.Max(0f, CartSpacing * CartSpacing - dx * dx));
                            float have = df * sg;
                            if (have < need) pf += sg * (need - have) * 0.5f;
                        }
                    }
                    Vector3 g = m + right * px + fwd * pf;
                    // never behind its own keep line (BUG-DOG-014: CartKeepLine past its anchor), whatever the push did
                    float fo = Vector3.Dot(g - _cartBases[k], fwd);
                    if (fo < CartKeepLine) g += fwd * (CartKeepLine - fo);
                    _cartGoalsNext[k] = g;
                }
                Vector3[] tmp = _cartGoals; _cartGoals = _cartGoalsNext; _cartGoalsNext = tmp;
            }
            Vector3 off = _cartGoals[order] - _cartBases[order];
            off.y = 0f;
            return off;
        }

        /// <summary>Sideways rank of a slot as the runtime places it: 0 left (-), 2 middle, 1 right (+).</summary>
        private float CartLatRank(int slot) { return slot == 0 ? -1f : (slot == 1 ? 1f : 0f); }

        /// <summary>The anchor of the holder's dog number `k` (0-based order): slot k % 3, one row of CartRowStep further ahead per 3.</summary>
        private Vector3 CartSlotBase(int k, Vector3 a0, Vector3 a1, Vector3 a2, Vector3 fwd)
        {
            int slot = k % 3;
            Vector3 a = slot == 0 ? a0 : (slot == 1 ? a1 : a2);
            return a + fwd * (CartRowStep * (k / 3));
        }

        /// <summary>The slow wander of dog number `k` (world vector, cart frame). latScale: the dawdle widens the sway.</summary>
        private Vector3 CartSway(int k, float now, float amp, float latScale, Vector3 right, Vector3 fwd)
        {
            float sL = 0.6f * Mathf.Sin(Tau * now / CartSwayT1 + k * CartSwayLatPhase)
                + 0.4f * Mathf.Sin(Tau * now / CartSwayT2 + k * CartSwayLatPhase * 1.7f + 1.3f);
            float sF = 0.6f * Mathf.Sin(Tau * now / CartSwayT3 + k * CartSwayFwdPhase)
                + 0.4f * Mathf.Sin(Tau * now / CartSwayT4 + k * CartSwayFwdPhase + 2.2f);
            return right * (amp * sL * latScale) + fwd * (CartFwdMid + CartFwdAmp * sF);
        }

        /// <summary>A straight step along the cart track. The track is the walk trail (bridges included), which the river band rule
        /// would refuse as a straight move, so it is stepped as a NavMesh move (_navStep, recorded for the legs replay).</summary>
        private void CartStep(Vector3 target, float stop, float speed)
        {
            _navStep = true;
            StepToward(target, stop, speed);
            _navStep = false;
        }

        /// <summary>Owner: the holder is no longer riding (or the anchor vanished). One log line on the edge; the walk itself needs
        /// nothing restored (the branches below set their own modes).</summary>
        private void CartFollowEnd()
        {
            if (_cartOrderLogged < 0) return;
            Debug.Log("[KD_Leash] cart-follow dog=" + dogIndex + " slot=-1 (off)");
            _cartOrderLogged = -1; _cartStateLogged = -1;
            _cartBlend = 0f; _cartHesNext = 0f; _cartHesUntil = 0f; _cartHesFactor = 1f; _cartWanderNext = 0f; _cartWanderOff = Vector3.zero;
            if (leashMode == LeashCart) leashMode = LeashWalk;
        }
    }
}
