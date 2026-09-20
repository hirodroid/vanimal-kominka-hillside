using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;

namespace KominkaDogRun
{
    /// <summary>One player's visit. Nicknames never enter this shared object.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class KD_PlayerDogTag : UdonSharpBehaviour
    {
        // A visit mark younger than this is "the visit in progress": arrival re-sends are ignored
        // and every observer treats it as complete without knowing when it started watching.
        public const int VisitWindowMs = 30000;
        public KD_DogPack pack;
        [UdonSynced] private int _favoriteDog = -1;
        [UdonSynced] private bool _greeted;
        [UdonSynced] private int _greetedDog = -1;
        [UdonSynced] private int _greetedMs;
        // IMP-DOG-035: latest casual drop-by per dog (server ms, 0 = never). Switching favorites keeps every dog's history.
        [UdonSynced] private int[] _visitedMs = new int[6];
        public int FavoriteDog { get { return _favoriteDog; } }
        public bool Greeted { get { return _greeted; } }
        public int GreetedDog { get { return _greetedDog; } }
        public int GreetedMs { get { return _greetedMs; } }
        // net: callable-ok: read-only accessor of a synchronized mark
        public int VisitedMs(int dog) { return _visitedMs != null && dog >= 0 && dog < _visitedMs.Length ? _visitedMs[dog] : 0; }
        public void _Publish(int dog)
        {
            if (!Networking.IsOwner(gameObject) || dog < -1 || dog >= 6 || dog == _favoriteDog) return;
            _favoriteDog = dog;
            // Changing favorites never creates a second welcome in the same visit.
            RequestSerialization();
        }
        [NetworkCallable(maxEventsPerSecond: 2)]
        // net: callable-ok: dog-owner arrival request, validated against sender, target, state and distance
        public void MarkGreeted(int dog)
        {
            if (_greeted || !ArrivalIsValid(dog)) return;
            _greeted = true; _greetedDog = dog; _greetedMs = Networking.GetServerTimeInMilliseconds();
            RequestSerialization();
        }
        [NetworkCallable(maxEventsPerSecond: 2)]
        // net: callable-ok: dog-owner drop-by arrival, validated against sender, target and distance; idempotent per visit
        public void MarkVisited(int dog)
        {
            if (!ArrivalIsValid(dog) || _visitedMs == null || dog >= _visitedMs.Length) return;
            int now = Networking.GetServerTimeInMilliseconds();
            int last = _visitedMs[dog];
            if (last != 0 && now - last < VisitWindowMs) return;
            _visitedMs[dog] = now == 0 ? 1 : now;
            RequestSerialization();
        }
        bool ArrivalIsValid(int dog)
        {
            if (!Networking.IsOwner(gameObject) || pack == null
                || pack.dogRoots == null || pack.dogFetch == null || dog < 0
                || dog >= pack.dogRoots.Length || dog >= pack.dogFetch.Length) return false;
            var sender = NetworkCalling.CallingPlayer;
            var root = pack.dogRoots[dog];
            var lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(sender) || root == null || !Utilities.IsValid(lp)
                || sender != Networking.GetOwner(root.gameObject)) return false;
            // The authenticated dog owner reports arrival. Do not re-read its current action:
            // a pet or Fetch can already have replaced the greeting while this event was in flight.
            Vector3 delta = root.position - lp.GetPosition();
            return delta.sqrMagnitude <= 9f;
        }
    }
}
