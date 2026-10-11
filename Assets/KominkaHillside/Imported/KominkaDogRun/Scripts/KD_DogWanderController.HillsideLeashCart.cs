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
            if (row > 0)
            {
                Vector3 fwd = cartManager._CartForward(holderId); fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f) anchor += fwd.normalized * (CartRowStep * row);
            }
            if (order != _cartOrderLogged)
            {
                Debug.Log("[KD_Leash] cart-follow dog=" + dogIndex + " slot=" + order + " holder=" + holderId);
                _cartOrderLogged = order;
            }
            // A sniff in progress ends here, through the single exit (Return's precedent: logged as its own reason)
            if (pausing) EndPause("cart");
            leashMode = LeashCart;
            // The ride carries the holder along the cart track, so the walk's trail progress is stale by the time they get off.
            // Forget it (branch 3's precedent) and let ChooseEntry pick a fresh entry on the first walking decision after the ride.
            // A stopover won before boarding is not cashed in at the stop either
            _routeNext = -1; _routeResume = -1;
            _stopoverPending = -1; _stopoverRolledFor = -1;
            // The height is the dog's own (SnapToGround); the anchor's y is not used. NavMesh is sampled around the dog's level
            anchor.y = dogRoot.position.y;
            float d = Horizontal(dogRoot.position, anchor);
            // walkSpeed (1.1) is slower than the cart (1.3): pace the holder's body (riding = the cart's speed) plus the lead-hold
            // gain on the gap, so the dog keeps its spot without falling back into the return. Far behind: sprint as FollowHand does
            float speed = d > leash * 1.2f ? hillsideLeashes.sprintSpeed
                : Mathf.Clamp(_holderSpeed + LeadHoldGain * d, walkSpeed, hillsideLeashes.dashSpeed);
            LeashGo(anchor, speed, 0.2f);
            return true;
        }

        /// <summary>Owner: the holder is no longer riding (or the anchor vanished). One log line on the edge; the walk itself needs
        /// nothing restored (the branches below set their own modes).</summary>
        private void CartFollowEnd()
        {
            if (_cartOrderLogged < 0) return;
            Debug.Log("[KD_Leash] cart-follow dog=" + dogIndex + " slot=-1 (off)");
            _cartOrderLogged = -1;
            if (leashMode == LeashCart) leashMode = LeashWalk;
        }
    }
}
