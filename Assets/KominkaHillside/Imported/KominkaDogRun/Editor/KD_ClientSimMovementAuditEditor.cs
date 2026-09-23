using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;

namespace KominkaDogRun.Editor
{
    public static class KD_ClientSimMovementAuditEditor
    {
        public static void Survey()
        {
            var scene = SceneManager.GetActiveScene();
            var data = new
            {
                scene = scene.path, play = EditorApplication.isPlaying, paused = EditorApplication.isPaused,
                timeScale = Time.timeScale, testMode = ClientSimRuntimeLoader.IsInUnityTest(),
                settings = ClientSimSettings.Instance,
                focusedWindow = EditorWindow.focusedWindow == null ? null : EditorWindow.focusedWindow.titleContent.text,
                applicationFocused = Application.isFocused, cursorLock = Cursor.lockState.ToString(),
                players = Object.FindObjectsOfType<ClientSimPlayer>(true).Select(p => new {
                    name = p.name, vr = p.IsUserVR, immobilized = p.locomotionData.GetImmobilized(),
                    walkSpeed = p.locomotionData.GetWalkSpeed(), position = p.transform.position.ToString()
                }).ToArray(),
#if ENABLE_INPUT_SYSTEM
                inputUpdateMode = UnityEngine.InputSystem.InputSystem.settings.updateMode.ToString(),
                devices = UnityEngine.InputSystem.InputSystem.devices.Select(d => new { name = d.name, enabled = d.enabled }).ToArray(),
                playerInputs = Object.FindObjectsOfType<UnityEngine.InputSystem.PlayerInput>(true).Select(p => new {
                    name = p.name, active = p.inputIsActive, enabled = p.enabled, scheme = p.currentControlScheme,
                    devices = p.devices.Select(d => d.name).ToArray(),
                    actions = p.actions == null ? null : p.actions.Select(a => new { name = a.name, enabled = a.enabled,
                        controls = a.controls.Select(c => c.path).ToArray() }).ToArray()
                }).ToArray(),
#endif
                inputManagers = Object.FindObjectsOfType<ClientSimInputManager>(true).Select(m => new {
                    name = m.name, enabled = m.enabled, inputNull = m.GetInput() == null,
                    movement = m.GetInput() == null ? null : m.GetInput().GetMovementAxes().ToString(),
                    look = m.GetInput() == null ? null : m.GetInput().GetLookAxes().ToString()
                }).ToArray(),
                inputLocks = Object.FindObjectsOfType<MonoBehaviour>(true).Where(m => m != null && m.GetType().Name == "ClientSimBaseInput")
                    .Select(m => new { name = m.name, enabled = m.enabled,
                        fields = m.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            .Where(f => f.FieldType == typeof(bool)).ToDictionary(f => f.Name, f => f.GetValue(m)) }).ToArray(),
                cameras = Resources.FindObjectsOfTypeAll<Camera>().Where(c => !EditorUtility.IsPersistent(c)).Select(c => new
                {
                    name = c.name, path = PathOf(c.transform), scene = c.gameObject.scene.path,
                    sceneValid = c.gameObject.scene.IsValid(), gameObjectHideFlags = c.gameObject.hideFlags.ToString(),
                    enabled = c.enabled, active = c.gameObject.activeInHierarchy, depth = c.depth,
                    target = c.targetTexture == null ? null : c.targetTexture.name, tag = c.tag,
                    position = c.transform.position.ToString(), hideFlags = c.hideFlags.ToString()
                }).ToArray(),
                listeners = Object.FindObjectsOfType<AudioListener>(true).Select(c => new { path = PathOf(c.transform), enabled = c.enabled }).ToArray(),
                sessions = new[] { "KD.CandyLiveHarness.Active", "KD.CandyRehearsal.Active" }.Select(k => new { key = k, active = SessionState.GetBool(k, false) }).ToArray()
            };
            Directory.CreateDirectory("Logs/live-add");
            File.WriteAllText("Logs/live-add/movement-audit.json", JsonConvert.SerializeObject(data, Formatting.Indented));
        }
        static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    }
}
