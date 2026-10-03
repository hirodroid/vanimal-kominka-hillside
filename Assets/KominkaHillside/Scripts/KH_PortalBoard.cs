using UdonSharp;
using UnityEngine;

namespace KominkaHillside
{
    // IMP-WLD-006: the 古民家ドッグラン board at the gate. Interact shows the world portal beside the board for this player only;
    // Interact again (or showSeconds later) hides it. Local only: no synced variables, no ownership, no network events — a world
    // portal sends only the person who walks into it, and each player decides for themselves (docs/plans/2026-10-02-guide-boards.md §12).
    // Nothing runs while the portal is hidden: no Update; the timeout is one delayed event per Interact, checked against the time
    // the portal was last shown (an older pending event finds a newer show time and does nothing).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KH_PortalBoard : UdonSharpBehaviour
    {
        [Tooltip("The parent of the VRCPortalMarker (KH_GuideBoards/Kominka/PortalAnchor). Inactive in the saved scene.")]
        public GameObject portal;
        [Tooltip("Seconds until the shown portal hides itself (0 = stays until the next Interact).")]
        public float showSeconds = 60f;
        private float _shownAt = -1f;

        private void Start()
        {
            // the saved scene already has it hidden; this keeps late joiners and a mis-saved scene hidden too
            if (portal != null && portal.activeSelf) portal.SetActive(false);
        }

        public override void Interact()
        {
            if (portal == null) return;
            if (portal.activeSelf) { portal.SetActive(false); _shownAt = -1f; return; }
            portal.SetActive(true);
            _shownAt = Time.time;
            if (showSeconds > 0f) SendCustomEventDelayedSeconds(nameof(_HideAfterTimeout), showSeconds);
        }

        // local only (the `_` prefix keeps it off the network: vrc-networking §1)
        public void _HideAfterTimeout()
        {
            if (portal == null || !portal.activeSelf || _shownAt < 0f) return;
            if (Time.time - _shownAt < showSeconds - 0.05f) return;   // shown again after this event was queued
            portal.SetActive(false);
            _shownAt = -1f;
        }
    }
}
