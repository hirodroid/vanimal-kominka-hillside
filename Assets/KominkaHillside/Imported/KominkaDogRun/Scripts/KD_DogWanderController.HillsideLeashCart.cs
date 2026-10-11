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
                // The cart is waiting (start grace, traffic hold, arrived): the old stand-by - walk onto the anchor and stand there.
                // StepToward coasts to a stop inside CartArrive. No sniff / LieDown (LeashPose and settling read _cartRiding)
                if (d > CartPathFar) LeashGo(anchor, walkSpeed, CartArrive);
                else CartStep(anchor, CartArrive, walkSpeed);
                return true;
            }
            // Moving cart (2026-10-12 Play fix: the dog used to arrive at the anchor, stop, and start again once it had moved on,
            // so it was seen beside the cart, stop-go). Now it never "arrives": a continuous speed law on the gap along the
            // cart's forward direction, and a steering target always CartLead ahead of the dog.
            //   dAlong = (anchor - dog) . fwd   (> 0: the dog is behind its spot)
            //   v = cartSpeed + CartGain * dAlong, clamped to [CartMinRatio * cartSpeed, dashSpeed]
            // At the spot v = cartSpeed; ahead of it the dog slows but keeps walking and lets the cart catch up.
            float dAlong = Vector3.Dot(toAnchor, fwd);
            Vector3 lateral = toAnchor - fwd * dAlong;
            float v = Mathf.Clamp(cartSpeed + CartGain * dAlong, CartMinRatio * cartSpeed, hillsideLeashes.dashSpeed);
            if (d > CartPathFar && dAlong > 0f)
            {
                // Far behind or off to the side: the NavMesh path to the spot (around obstacles). Return / the teleport above
                // still take over past 1.25x leash from the hand
                LeashGo(anchor + fwd * CartLead, v, 0.05f);
                return true;
            }
            // Near the spot (or ahead of it): steer straight. Behind: toward anchor + fwd * CartLead (pure pursuit, the lateral
            // error closes as the dog walks). Within CartAlign of the spot or ahead of it: the target is CartLead ahead of the dog
            // along the cart's forward plus the lateral error only, so the body lines up with the cart instead of weaving
            float along = (d <= CartAlign || dAlong < 0f) ? 0f : dAlong;
            CartStep(pos + fwd * (along + CartLead) + lateral, 0.05f, v);
            return true;
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
            if (leashMode == LeashCart) leashMode = LeashWalk;
        }
    }
}
