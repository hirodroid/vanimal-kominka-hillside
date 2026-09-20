using UdonSharp;
using UnityEngine;

namespace KominkaDogRun
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class KD_PhotoHelp : UdonSharpBehaviour
    {
        public GameObject panel;
        public GameObject settingsPanel;
        public override void Interact() { _Toggle(); }
        public void _Toggle()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (panel != null) panel.SetActive(!panel.activeSelf);
        }
        public void _Close() { if (panel != null) panel.SetActive(false); }
        public void _ToggleSettings()
        {
            if (panel != null) panel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(!settingsPanel.activeSelf);
        }
        public void _CloseSettings() { if (settingsPanel != null) settingsPanel.SetActive(false); }
    }
}
