using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;
using VRC.Udon;
using UIButton = UnityEngine.UI.Button;
using UIGraphic = UnityEngine.UI.Graphic;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// TEMPORARY probe (2026-09-26). Delete after the live-tab purchase bug is understood.
    ///
    /// Symptom: Live tab -> "紅葉が星になる" (live.hillside_dusk, 30 pt) -> detail -> [買う] opens the confirm card,
    /// but the card's [購入] does nothing (card stays open), while other 30 pt items buy fine through the same card.
    ///
    /// What it does: enters Play on the main scene (same guards as KH_FrisbeeHarnessEditor), passes ClientSim's
    /// Accept if shown, waits for the ledger, opens the hand menu with the same public events the UI uses, then
    ///   (a) _Tab4  (b) _Live{row of live.hillside_dusk}  (c) _LiveDetailAction  (d) _ConfirmYes (direct event)
    /// and afterwards the click-like path: _LiveBack -> _Live{row} -> _LiveDetailAction -> raycast evidence ->
    /// ConfirmPanel/Card/Yes Button.onClick.Invoke(). State after every step goes to Logs/kh-live-buy-probe/latest.json.
    ///
    /// Rules (same as the frisbee harness): never saves the scene; opens nothing but the already-open main scene;
    /// always leaves Play; values are read from the runtime UdonBehaviour heap (not the U# proxy).
    /// ClientSim PlayerData (ClientSimStorage/PlayerData/*_VRCDefaultWorldScene.json) is backed up before Play and
    /// put back after, so a purchase made by the probe does not stay in the user's ClientSim save.
    /// </summary>
    [InitializeOnLoad]
    public static class KH_LiveBuyProbeEditor
    {
        const string MenuRun = "Tools/KominkaHillside/Testing/Live Buy Probe (ClientSim)";
        const string MenuStop = "Tools/KominkaHillside/Testing/Live Buy Probe Stop";
        const string Base = "Logs/kh-live-buy-probe";
        const string Journal = Base + "/journal.json";
        const string Latest = Base + "/latest.json";
        const string BackupDir = Base + "/playerdata-backup";
        const string ScenePath = "Assets/Scenes/VRCDefaultWorldScene.unity";
        const string SceneName = "VRCDefaultWorldScene";
        const string PlayerDataDir = "ClientSimStorage/PlayerData";
        const string ProductKey = "live.hillside_dusk";
        const string ProductObjectName = "KD_ShopItem_Live_HillsideDusk";
        const string PouchObjectName = "KD_TreatPouch";
        const string Key = "KH.LiveBuyProbe.Run";
        const string PhaseKey = Key + ".phase";      // starting / playing / exiting
        const string StartKey = Key + ".startedAt";
        const double StepWait = 0.6;
        const double Budget = 150.0;
        const int ConfirmLive = 2;
        const int TabLive = 4;
        const int MaxPoints = 1000000;

        [Serializable]
        public class Snap
        {
            public string step;
            public double t;
            public int frame;
            // KD_TreatPouch
            public string tab, livePage, liveProduct, liveConfirmRow, liveGiftRow, confirmKind, confirmGift;
            public string settingsOpen, welcome, feedbackText, feedbackTab, feedbackActive;
            public string panelOpen, confirmRootActive, confirmYesActive, confirmYesLabel, confirmTitleLabel, confirmNoteLabel;
            public string liveDetailActionActive, liveDetailActionInteractable;
            public string pouchEnabled, pouchUdonFlags;
            // KD_ShopItem (live.hillside_dusk)
            public string itemDbgBought, itemDbgRejected;
            public string itemOwnedMirror, itemCanBuyMirror, itemMirrorNote;
            // KD_ProgressLedger
            public string ledgerLoaded, ledgerCatalogReady, earned, spent, earnedValid, spentValid, points;
            public string ledgerIndex, ownedAtIndex, ownValidAtIndex, ledgerDbgBuyCount, ledgerDbgRejectCount;
            // KD_ShopCatalog
            public string catalogResolved, catalogLoaded, catalogIndex, categoryIndex, selectionValidAtCategory;
            // Console
            public int boughtLinesSoFar;
            public List<string> newLogs = new List<string>();
        }

        [Serializable]
        public class ClickCheck
        {
            public string label;
            public string yesPath, expectedYesPath, yesMatchesConfirmYesField;
            public bool yesActiveInHierarchy, buttonEnabled, buttonInteractable, buttonIsInteractable;
            public string targetGraphic, targetGraphicRaycastTarget, imageRaycastTarget;
            public List<string> canvasGroups = new List<string>();
            public List<string> canvases = new List<string>();
            public List<string> uiShapeLike = new List<string>();
            public string worldCorners, worldCenter;
            public string playerCamera, canvasWorldCamera, eventCamera, screenPoint;
            public bool onScreen;
            public string eventSystem;
            public List<string> eventSystemHits = new List<string>();   // top first
            public List<string> graphicHits = new List<string>();       // manual hit test in the root canvas, top first
            public List<string> physicsHits = new List<string>();       // camera -> Yes centre, nearest first
            public string topEventSystemHit, topIsYesOrChild;
            public List<string> onClickCalls = new List<string>();
        }

        [Serializable]
        public class Report
        {
            public string runId, status = "RUNNING", failure;
            public string started, ended, unity, scene, sceneHashBefore, sceneHashAfter;
            public bool sceneDirtyAfterPlay, sceneFileUnchanged;
            public double deadline;
            public string acceptPressed = "not tried";
            public string pouch, ledger, catalog, live, hillsideStage, item, itemFields;
            public int liveRow = -1;
            public string liveRowEvent;
            public string ledgerWait;
            public string howToRead =
                "Compare steps 'c-card' (before) and 'd-direct' (after _ConfirmYes). " +
                "NOT REACHED = liveConfirmRow and feedbackText unchanged and no dbg counter moved -> _ConfirmYes/_LiveBuy never ran (or confirmKind != 2 so it went to another seller). " +
                "REFUSED = liveConfirmRow went to -1 and feedbackText = KD_ShopShelf.WordCantBuy (itemDbgRejected / ledgerDbgRejectCount +1 when _Buy ran) -> purchase logic; see itemMirrorNote / catalog* / ledger* for the failing condition. " +
                "BOUGHT = boughtLinesSoFar +1 and feedbackText = KD_ShopShelf.WordBought. " +
                "Then compare 'e-card' and 'e-click' (Button.onClick.Invoke) the same way; clickCheck says whether a real pointer at the Yes centre would land on Yes.";
            public string verdictDirect = "NOT_RUN", verdictClick = "NOT_RUN";
            public string resetBetween = "not needed";
            public int boughtLines;
            public List<Snap> steps = new List<Snap>();
            public List<ClickCheck> clickChecks = new List<ClickCheck>();
            public List<string> logs = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> trace = new List<string>();
            public List<string> playerData = new List<string>();
        }

        static Report report;
        static IEnumerator routine;
        static int lastFrame = -1;
        static bool ending;
        static double t0;
        static int logMark;
        static UdonBehaviour pouch, ledger, catalog, item;

        static double Now { get { return EditorApplication.timeSinceStartup; } }
        static string Phase { get { return SessionState.GetString(PhaseKey, ""); } set { SessionState.SetString(PhaseKey, value); } }
        static bool Active { get { return !string.IsNullOrEmpty(SessionState.GetString(Key, "")); } }

        static KH_LiveBuyProbeEditor()
        {
            EditorApplication.playModeStateChanged += Mode;
            EditorApplication.update += Tick;
            if (Active)
            {
                try { if (File.Exists(Journal)) report = JsonUtility.FromJson<Report>(File.ReadAllText(Journal)); }
                catch { report = null; }
                Configure();
            }
        }

        static void Configure()
        {
            Application.logMessageReceived -= Log;
            Application.logMessageReceived += Log;
            ClientSimRuntimeLoader.BeginUnityTesting(new ClientSimSettings
            {
                enableClientSim = true,
                hideMenuOnLaunch = true,
                displayLogs = false,
                customLocalPlayerName = "KH Live Buy Probe",
                localPlayerIsMaster = true,
                initializationDelay = 0f,
                setTargetFrameRate = true,
                targetFrameRate = 60,
                spawnPlayer = true,
            });
        }

        // ------------------------------------------------------------------ menu
        [MenuItem(MenuRun)]
        public static void Run()
        {
            try { Begin(); }
            catch (Exception e)
            {
                WriteBlocked(e.Message);
                Debug.LogError("[KH Live Buy Probe] BLOCKED " + e.Message);
            }
        }

        [MenuItem(MenuStop)]
        public static void Stop()
        {
            if (!Active)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Journal))
                {
                    File.Delete(Journal);
                    Debug.Log("[KH Live Buy Probe] removed a stale journal (" + Journal + "); latest.json is kept");
                    return;
                }
                Debug.Log("[KH Live Buy Probe] no run in progress");
                return;
            }
            if (report != null) { report.status = "BLOCKED"; Why("Explicit stop"); }
            if (EditorApplication.isPlaying) StopInternal();
            else Finalise();
        }

        static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Probe needs an idle Edit mode (playing / compiling / building).");
            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Compile errors are present; fix them first.");
            if (Active || File.Exists(Journal))
                throw new InvalidOperationException("A previous probe run did not finish. Run '" + MenuStop + "' first.");
            if (SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Exactly one scene must be open (" + ScenePath + ").");
            var active = SceneManager.GetActiveScene();
            if (active.path != ScenePath)
                throw new InvalidOperationException("Open " + ScenePath + " first (active: " + active.path + "). The probe does not open scenes.");
            if (active.isDirty)
                throw new InvalidOperationException("The scene has unsaved changes. Save or discard them yourself first.");

            Directory.CreateDirectory(Base);
            report = new Report
            {
                runId = DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                started = DateTime.Now.ToString("o"),
                unity = Application.unityVersion,
                scene = ScenePath,
                sceneHashBefore = HashFile(ScenePath),
                deadline = Now + Budget,
            };
            BackupPlayerData();
            SessionState.SetString(Key, report.runId);
            SessionState.SetString(StartKey, Now.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Phase = "starting";
            ending = false;
            Save();
            try
            {
                Configure();
                EditorApplication.EnterPlaymode();
            }
            catch
            {
                ClearSession();
                if (File.Exists(Journal)) File.Delete(Journal);
                report = null;
                throw;
            }
        }

        static void Mode(PlayModeStateChange state)
        {
            if (!Active || report == null) return;
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (report.status == "RUNNING")
                {
                    report.status = "BLOCKED";
                    Why("Play ended before the probe finished (external Stop, recompile, or Unity).");
                }
                Save();
            }
        }

        static void Tick()
        {
            if (!Active) return;
            string phase = Phase;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (phase == "playing" || phase == "exiting") { Finalise(); return; }
                if (phase == "starting")
                {
                    double started;
                    if (!double.TryParse(SessionState.GetString(StartKey, "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out started)) started = 0;
                    if (Now > started + 60)
                    {
                        if (report != null) { report.status = "BLOCKED"; Why("Play did not start within 60 s"); }
                        Finalise();
                    }
                }
                return;
            }
            if (!EditorApplication.isPlaying || report == null) return;
            if (phase == "starting")
            {
                Phase = "playing";
                routine = Probe();
                lastFrame = -1;
                t0 = Now;
            }
            if (ending || Phase != "playing") return;
            if (Now > report.deadline || EditorApplication.isPaused)
            {
                report.status = "BLOCKED";
                Why(EditorApplication.isPaused ? "Editor paused (Error Pause?)" : "Watchdog " + Budget + " s");
                StopInternal();
                return;
            }
            if (routine != null && Time.frameCount != lastFrame)
            {
                lastFrame = Time.frameCount;
                try
                {
                    if (!routine.MoveNext())
                    {
                        if (report.status == "RUNNING") report.status = "DONE";
                        StopInternal();
                    }
                }
                catch (Exception e)
                {
                    report.status = "ERROR";
                    report.failure = e.ToString();
                    StopInternal();
                }
            }
        }

        static void StopInternal()
        {
            routine = null;
            if (ending) return;
            ending = true;
            Phase = "exiting";
            Save();
            if (EditorApplication.isPlaying)
                foreach (var u in Resources.FindObjectsOfTypeAll<UdonBehaviour>())
                    if (u != null && u.gameObject.scene.IsValid() && u.enabled) u.enabled = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            else Finalise();
        }

        static void Finalise()
        {
            try
            {
                if (report == null && File.Exists(Journal))
                    report = JsonUtility.FromJson<Report>(File.ReadAllText(Journal));
                ClientSimRuntimeLoader.EndUnityTesting();
                Application.logMessageReceived -= Log;
                if (report != null)
                {
                    if (report.status == "RUNNING") { report.status = "BLOCKED"; Why("Run ended without finishing"); }
                    var active = SceneManager.GetActiveScene();
                    report.sceneDirtyAfterPlay = active.isDirty;
                    // Play marks the scene dirty (NetworkIDs). Re-open from disk without saving; checked clean in Begin.
                    if (File.Exists(ScenePath) && (active.path != ScenePath || active.isDirty))
                        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    report.sceneHashAfter = File.Exists(ScenePath) ? HashFile(ScenePath) : "MISSING";
                    report.sceneFileUnchanged = report.sceneHashAfter == report.sceneHashBefore;
                    RestorePlayerData();
                    report.ended = DateTime.Now.ToString("o");
                    string json = JsonUtility.ToJson(report, true);
                    Directory.CreateDirectory(Base);
                    File.WriteAllText(Latest, json);
                    File.WriteAllText(Base + "/" + report.runId + ".json", json);
                    Debug.Log("[KH Live Buy Probe] " + report.status + " direct=" + report.verdictDirect + " click=" + report.verdictClick + " -> " + Latest);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[KH Live Buy Probe] finalise failed: " + e);
                try
                {
                    if (report != null)
                    {
                        report.failure += "\nFinalise: " + e;
                        File.WriteAllText(Latest, JsonUtility.ToJson(report, true));
                    }
                }
                catch { }
            }
            finally
            {
                if (File.Exists(Journal)) { try { File.Delete(Journal); } catch { } }
                ClearSession();
                report = null; routine = null; ending = false;
                pouch = ledger = catalog = item = null;
            }
        }

        static void ClearSession()
        {
            SessionState.EraseString(Key);
            SessionState.EraseString(PhaseKey);
            SessionState.EraseString(StartKey);
        }

        // ------------------------------------------------------------------ probe body
        static IEnumerator Probe()
        {
            double until = Now + 30;
            while (Networking.LocalPlayer == null || LocalPlayer() == null)
            {
                if (Now > until) throw new TimeoutException("ClientSim local player did not appear within 30 s");
                yield return null;
            }
            report.trace.Add("local player: " + Networking.LocalPlayer.displayName);
            var w = Wait(1.0); while (w.MoveNext()) yield return null;
            PressAccept();
            w = Wait(0.5); while (w.MoveNext()) yield return null;

            pouch = FindPouch();
            if (pouch == null) throw new InvalidOperationException("No runtime UdonBehaviour for KD_TreatPouch (needs confirmRoot + liveProducts symbols).");
            ledger = UB(pouch, "ledger");
            catalog = UB(pouch, "catalog");
            report.pouch = Path(pouch.transform);
            report.ledger = ledger == null ? "null" : Path(ledger.transform);
            report.catalog = catalog == null ? "null" : Path(catalog.transform);
            report.live = V(pouch, "live");
            report.hillsideStage = V(pouch, "hillsideStage");

            // The product and its row in liveProducts (the row index is what _Live{n} takes on the list page).
            var products = Arr(pouch, "liveProducts");
            if (products != null)
                for (int i = 0; i < products.Length; i++)
                {
                    var u = products.GetValue(i) as UdonBehaviour;
                    if (u != null && S(u, "productKey") == ProductKey) { item = u; report.liveRow = i; break; }
                }
            if (item == null)
            {
                foreach (var u in Resources.FindObjectsOfTypeAll<UdonBehaviour>())
                    if (u != null && u.gameObject.scene.IsValid() && u.gameObject.name == ProductObjectName) { item = u; break; }
                report.trace.Add("product not found in pouch.liveProducts (length " + (products == null ? "null" : products.Length.ToString()) + "); by name: " + (item != null));
            }
            if (item == null) { report.status = "BLOCKED"; Why("Product " + ProductKey + " not found"); yield break; }
            report.item = Path(item.transform);
            report.itemFields = "productKey=" + V(item, "productKey") + " categoryKey=" + V(item, "categoryKey") + " price=" + V(item, "price")
                + " consumable=" + V(item, "consumable") + " packCount=" + V(item, "packCount") + " ceListingId='" + V(item, "ceListingId") + "'"
                + " menuOnly=" + V(item, "menuOnly") + " item.ledger=" + V(item, "ledger")
                + " item.ledger==pouch.ledger=" + (ledger != null && ReferenceEquals(Get(item, "ledger"), ledger))
                + " ledger.storageNamespace=" + V(ledger, "storageNamespace");
            if (report.liveRow < 0 || report.liveRow > 8)
            {
                report.status = "BLOCKED";
                Why("live.hillside_dusk is not on rows 0..8 of liveProducts (row " + report.liveRow + "); _Live{n} cannot reach it");
                yield break;
            }
            report.liveRowEvent = "_Live" + report.liveRow;

            // Wait for the ledger (and the catalog) like a player would (max 30 s).
            until = Now + 30;
            while (!(ledger != null && B(ledger, "_loaded") && (catalog == null || B(catalog, "_resolved"))) && Now < until) yield return null;
            report.ledgerWait = "ledger._loaded=" + V(ledger, "_loaded") + " catalog._resolved=" + V(catalog, "_resolved") + " after " + (Now - t0).ToString("F1") + " s";
            Take("0-ready");

            // Open the hand menu with the same event the Touch launcher / stick uses.
            pouch.SendCustomEvent("_Toggle");
            w = Wait(StepWait); while (w.MoveNext()) yield return null;
            Take("open(_Toggle)");

            // (a) Live tab
            pouch.SendCustomEvent("_Tab4");
            w = Wait(StepWait); while (w.MoveNext()) yield return null;
            Take("a-tab(_Tab4)");

            // (b) the product's row -> detail page
            pouch.SendCustomEvent(report.liveRowEvent);
            w = Wait(StepWait); while (w.MoveNext()) yield return null;
            Take("b-row(" + report.liveRowEvent + ")");

            // (c) [買う] on the detail page -> confirm card
            pouch.SendCustomEvent("_LiveDetailAction");
            w = Wait(StepWait); while (w.MoveNext()) yield return null;
            var cardDirect = Take("c-card(_LiveDetailAction)");
            report.clickChecks.Add(CheckClick("before-d"));
            Save();

            // (d) the card's [購入], as a direct event
            int spentBefore = I(ledger, "_spent", 0);
            pouch.SendCustomEvent("_ConfirmYes");
            w = Wait(StepWait); while (w.MoveNext()) yield return null;
            var afterDirect = Take("d-direct(_ConfirmYes)");
            report.verdictDirect = Judge(cardDirect, afterDirect);

            // If (d) bought it, make it unowned again in memory only so the click path meets the same state.
            if (afterDirect.boughtLinesSoFar > cardDirect.boughtLinesSoFar || afterDirect.ownedAtIndex == "True")
            {
                int li = IndexIn(ledger, "products", item);
                var owned = Arr(ledger, "_owned") as bool[];
                if (li >= 0 && owned != null && li < owned.Length)
                {
                    owned[li] = false;
                    ledger.SetProgramVariable("_owned", owned);
                    ledger.SetProgramVariable("_spent", spentBefore);
                    report.resetBetween = "ledger._owned[" + li + "]=false and _spent=" + spentBefore + " (memory only; PlayerData file is restored after Play)";
                }
                else report.resetBetween = "wanted but could not (ledger index " + li + ")";
            }

            // (e) click-like path from a fresh detail page
            pouch.SendCustomEvent("_LiveBack");
            w = Wait(StepWait); while (w.MoveNext()) yield return null;
            pouch.SendCustomEvent(report.liveRowEvent);
            w = Wait(StepWait); while (w.MoveNext()) yield return null;
            pouch.SendCustomEvent("_LiveDetailAction");
            w = Wait(StepWait); while (w.MoveNext()) yield return null;
            var cardClick = Take("e-card(_LiveBack,_Live,_LiveDetailAction)");
            var check = CheckClick("before-click");
            report.clickChecks.Add(check);
            var yesGo = Get(pouch, "confirmYes") as GameObject;
            var button = yesGo != null ? yesGo.GetComponent<UIButton>() : null;
            if (button == null)
            {
                report.verdictClick = "SKIPPED: no Button on confirmYes";
            }
            else if (!button.gameObject.activeInHierarchy || !button.IsInteractable())
            {
                report.verdictClick = "SKIPPED: Yes not clickable (activeInHierarchy=" + button.gameObject.activeInHierarchy + " IsInteractable=" + button.IsInteractable() + ") - a real click cannot happen";
                Take("e-click(skipped)");
            }
            else
            {
                button.onClick.Invoke();
                w = Wait(StepWait); while (w.MoveNext()) yield return null;
                var afterClick = Take("e-click(Button.onClick.Invoke)");
                report.verdictClick = Judge(cardClick, afterClick) + " | topRaycastIsYes=" + check.topIsYesOrChild;
            }
            Save();
        }

        static string Judge(Snap before, Snap after)
        {
            var notes = new List<string>();
            if (before.confirmRootActive != "True") notes.Add("card was not open before the press (confirmRootActive=" + before.confirmRootActive + ")");
            if (before.confirmKind != ConfirmLive.ToString()) notes.Add("confirmKind=" + before.confirmKind + " (not ConfirmLive=2): _ConfirmYes dispatches to another seller");
            if (before.confirmYesActive != "True") notes.Add("Yes was hidden before the press (confirmYesActive=" + before.confirmYesActive + ")");
            bool bought = after.boughtLinesSoFar > before.boughtLinesSoFar;
            bool rowCleared = before.liveConfirmRow != "-1" && after.liveConfirmRow == "-1";
            bool fbChanged = after.feedbackText != before.feedbackText;
            bool rejected = after.itemDbgRejected != before.itemDbgRejected || after.ledgerDbgRejectCount != before.ledgerDbgRejectCount;
            string head;
            if (bought) head = "BOUGHT";
            else if (!rowCleared && !fbChanged && !rejected) head = "NOT_REACHED (_LiveBuy did not run)";
            else if (after.feedbackText == "KD_ShopShelf.WordCantBuy" || rejected)
                head = "REFUSED_BY_LOGIC (" + (rejected ? "item._Buy/ledger refused" : "refused before _Buy: catalog._RequestBuy or LiveSaleClosed/null") + ")";
            else head = "REACHED_UNCLEAR (liveConfirmRow " + before.liveConfirmRow + "->" + after.liveConfirmRow + ", feedback '" + before.feedbackText + "'->'" + after.feedbackText + "')";
            notes.Insert(0, head);
            notes.Add("cardAfter=" + after.confirmRootActive + " kindAfter=" + after.confirmKind);
            if (!bought)
                notes.Add("mirror: points=" + before.points + " owned=" + before.ownedAtIndex + " ownValid=" + before.ownValidAtIndex
                    + " ledgerIndex=" + before.ledgerIndex + " catalogIndex=" + before.catalogIndex + " catalogResolved=" + before.catalogResolved
                    + " selectionValid=" + before.selectionValidAtCategory + " canBuyMirror=" + before.itemCanBuyMirror + " (" + before.itemMirrorNote + ")");
            return string.Join(" | ", notes.ToArray());
        }

        // ------------------------------------------------------------------ state
        static Snap Take(string step)
        {
            var s = new Snap { step = step, t = Math.Round(Now - t0, 2), frame = Time.frameCount };
            s.tab = V(pouch, "_tab");
            s.livePage = V(pouch, "_livePage");
            s.liveProduct = V(pouch, "_liveProduct");
            s.liveConfirmRow = V(pouch, "_liveConfirmRow");
            s.liveGiftRow = V(pouch, "_liveGiftRow");
            s.confirmKind = V(pouch, "_confirmKind");
            s.confirmGift = V(pouch, "_confirmGift");
            s.settingsOpen = V(pouch, "_settingsOpen");
            s.welcome = V(pouch, "_welcome");
            s.feedbackText = V(pouch, "_feedbackText");
            s.feedbackTab = V(pouch, "_feedbackTab");
            object fu = Get(pouch, "_feedbackUntil");
            s.feedbackActive = fu is float ? (Time.time < (float)fu).ToString() : "?";
            s.panelOpen = ActiveSelf(Get(pouch, "panelRoot") as GameObject);
            s.confirmRootActive = ActiveSelf(Get(pouch, "confirmRoot") as GameObject);
            s.confirmYesActive = ActiveSelf(Get(pouch, "confirmYes") as GameObject);
            s.confirmYesLabel = Txt(Get(pouch, "confirmYesLabel"));
            s.confirmTitleLabel = Txt(Get(pouch, "confirmTitleLabel"));
            s.confirmNoteLabel = Txt(Get(pouch, "confirmNoteLabel"));
            s.liveDetailActionActive = ActiveSelf(Get(pouch, "liveDetailAction") as GameObject);
            var ab = Get(pouch, "liveDetailActionButton") as UIButton;
            s.liveDetailActionInteractable = ab == null ? "null" : ab.interactable.ToString();
            s.pouchEnabled = pouch.enabled.ToString();
            s.pouchUdonFlags = UdonFlags(pouch);

            s.itemDbgBought = V(item, "_dbgBought");
            s.itemDbgRejected = V(item, "_dbgRejected");

            s.ledgerLoaded = V(ledger, "_loaded");
            s.ledgerCatalogReady = V(ledger, "_catalogReady");
            s.earned = V(ledger, "_earned");
            s.spent = V(ledger, "_spent");
            s.earnedValid = V(ledger, "_earnedValid");
            s.spentValid = V(ledger, "_spentValid");
            int earned = I(ledger, "_earned", 0), spent = I(ledger, "_spent", 0);
            int points = Math.Max(0, Mathf.Clamp(earned, 0, MaxPoints) - Mathf.Clamp(spent, 0, MaxPoints));
            s.points = points.ToString();
            int li = IndexIn(ledger, "products", item);
            s.ledgerIndex = li.ToString();
            s.ownedAtIndex = ElemStr(ledger, "_owned", li);
            s.ownValidAtIndex = ElemStr(ledger, "_ownValid", li);
            s.ledgerDbgBuyCount = V(ledger, "_dbgBuyCount");
            s.ledgerDbgRejectCount = V(ledger, "_dbgRejectCount");

            s.catalogResolved = V(catalog, "_resolved");
            s.catalogLoaded = V(catalog, "_loaded");
            s.catalogIndex = IndexIn(catalog, "items", item).ToString();
            int ci = -1;
            var cats = Arr(catalog, "categoryKeys");
            string cat = S(item, "categoryKey");
            if (cats != null) for (int i = 0; i < cats.Length; i++) if ((cats.GetValue(i) as string) == cat) { ci = i; break; }
            s.categoryIndex = ci + " (" + cat + ")";
            s.selectionValidAtCategory = ElemStr(catalog, "_selectionValid", ci);

            // Mirror of KD_ShopItem.Owned() / CanBuy() for a plain unlock item (not CE / free / stocked).
            // Registered() is only partly mirrored (ledger index + item.ledger); IsConfigured/namespace are not.
            bool loaded = B(ledger, "_loaded");
            bool ce = !string.IsNullOrEmpty(S(item, "ceListingId"));
            int price = I(item, "price", -1);
            bool consumable = B(item, "consumable");
            int pack = I(item, "packCount", 0);
            bool ownedB = s.ownedAtIndex == "True", ownValidB = s.ownValidAtIndex == "True";
            bool itemLedgerOk = ledger != null && ReferenceEquals(Get(item, "ledger"), ledger);
            s.itemOwnedMirror = (itemLedgerOk && li >= 0 && loaded && ownValidB && ownedB).ToString();
            bool canAfford = loaded && B(ledger, "_earnedValid") && B(ledger, "_spentValid") && price > 0 && points >= price && spent <= MaxPoints - price;
            s.itemCanBuyMirror = (itemLedgerOk && li >= 0 && canAfford && ownValidB && !ownedB && !consumable && !ce && pack == 0).ToString();
            var why = new List<string>();
            if (!itemLedgerOk) why.Add("item.ledger != pouch.ledger");
            if (li < 0) why.Add("not in ledger.products");
            if (!loaded) why.Add("ledger not loaded");
            if (!canAfford) why.Add("cannot afford (points " + points + " price " + price + ")");
            if (!ownValidB) why.Add("ownValid false");
            if (ownedB) why.Add("already owned");
            if (ce) why.Add("CE item");
            if (consumable || pack > 0) why.Add("consumable/stocked");
            s.itemMirrorNote = why.Count == 0 ? "ok" : string.Join(", ", why.ToArray());

            s.boughtLinesSoFar = report.boughtLines;
            for (int i = logMark; i < report.logs.Count; i++) s.newLogs.Add(report.logs[i]);
            logMark = report.logs.Count;
            report.steps.Add(s);
            Save();
            return s;
        }

        static ClickCheck CheckClick(string label)
        {
            var c = new ClickCheck { label = label };
            var yesField = Get(pouch, "confirmYes") as GameObject;
            var root = Get(pouch, "confirmRoot") as GameObject;
            Transform expected = root != null ? root.transform.Find("Card/Yes") : null;
            c.expectedYesPath = expected == null ? "null (ConfirmPanel/Card/Yes not found under confirmRoot)" : Path(expected);
            GameObject yes = yesField != null ? yesField : (expected != null ? expected.gameObject : null);
            c.yesMatchesConfirmYesField = (expected != null && yesField != null && expected.gameObject == yesField).ToString();
            if (yes == null) { c.yesPath = "null"; return c; }
            c.yesPath = Path(yes.transform);
            c.yesActiveInHierarchy = yes.activeInHierarchy;
            var button = yes.GetComponent<UIButton>();
            if (button != null)
            {
                c.buttonEnabled = button.enabled;
                c.buttonInteractable = button.interactable;
                c.buttonIsInteractable = button.IsInteractable();
                c.targetGraphic = button.targetGraphic == null ? "null" : Path(button.targetGraphic.transform);
                c.targetGraphicRaycastTarget = button.targetGraphic == null ? "null" : button.targetGraphic.raycastTarget.ToString();
                int n = button.onClick.GetPersistentEventCount();
                var so = new SerializedObject(button);
                for (int i = 0; i < n; i++)
                {
                    var target = button.onClick.GetPersistentTarget(i);
                    string arg = "";
                    var p = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[" + i + "].m_Arguments.m_StringArgument");
                    if (p != null) arg = p.stringValue;
                    var st = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[" + i + "].m_CallState");
                    var targetComp = target as Component;
                    string targetText = target == null ? "null" : target.GetType().Name + "@" + (targetComp != null ? Path(targetComp.transform) : target.name);
                    string stateText = st == null ? "?" : st.intValue.ToString() + " (0=Off 1=EditorAndRuntime 2=RuntimeOnly)";
                    c.onClickCalls.Add(targetText + " ." + button.onClick.GetPersistentMethodName(i) + "('" + arg + "') callState=" + stateText
                        + " sameAsPouch=" + (target != null && ReferenceEquals(target, pouch)));
                }
                c.onClickCalls.Add("runtime (non-persistent) listeners are not listed");
            }
            var img = yes.GetComponent<UIGraphic>();
            c.imageRaycastTarget = img == null ? "no Graphic on Yes" : img.GetType().Name + " raycastTarget=" + img.raycastTarget + " enabled=" + img.enabled;
            for (Transform t = yes.transform; t != null; t = t.parent)
            {
                foreach (var g in t.GetComponents<CanvasGroup>())
                    c.canvasGroups.Add(Path(t) + " alpha=" + g.alpha + " interactable=" + g.interactable + " blocksRaycasts=" + g.blocksRaycasts + " ignoreParent=" + g.ignoreParentGroups + " enabled=" + g.enabled);
                var cv = t.GetComponent<Canvas>();
                if (cv != null)
                {
                    var gr = t.GetComponent("GraphicRaycaster") as Behaviour;
                    c.canvases.Add(Path(t) + " mode=" + cv.renderMode + " override=" + cv.overrideSorting + " order=" + cv.sortingOrder + " layer=" + cv.sortingLayerName
                        + " worldCamera=" + (cv.worldCamera == null ? "null" : cv.worldCamera.name) + " GraphicRaycaster=" + (gr == null ? "none" : gr.enabled.ToString()));
                }
                foreach (var comp in t.GetComponents<Component>())
                    if (comp != null && (comp.GetType().Name.Contains("UiShape") || comp.GetType().Name.Contains("UIShape")))
                    {
                        var col = t.GetComponent<Collider>();
                        c.uiShapeLike.Add(Path(t) + " " + comp.GetType().Name + " layer=" + LayerMask.LayerToName(t.gameObject.layer) + " collider=" + (col == null ? "none" : col.GetType().Name + " enabled=" + col.enabled + " bounds=" + col.bounds));
                    }
            }

            var rt = yes.transform as RectTransform;
            if (rt == null) return c;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            c.worldCorners = string.Join(" ", corners.Select(v => v.ToString("F3")).ToArray());
            Vector3 centre = (corners[0] + corners[2]) * 0.5f;
            c.worldCenter = centre.ToString("F3");

            Camera playerCam = null;
            var lp = LocalPlayer();
            try { if (lp != null && lp.GetCameraProvider() != null) playerCam = lp.GetCameraProvider().GetCamera(); } catch (Exception e) { c.playerCamera = "error " + e.Message; }
            if (c.playerCamera == null) c.playerCamera = playerCam == null ? "null" : playerCam.name + " pos=" + playerCam.transform.position.ToString("F2") + " fwd=" + playerCam.transform.forward.ToString("F2");
            var parentCanvas = yes.GetComponentInParent<Canvas>();
            Canvas rootCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
            c.canvasWorldCamera = rootCanvas == null ? "no canvas" : rootCanvas.worldCamera == null ? "null" : rootCanvas.worldCamera.name;
            Camera evCam = rootCanvas != null && rootCanvas.worldCamera != null ? rootCanvas.worldCamera : (playerCam != null ? playerCam : Camera.main);
            c.eventCamera = evCam == null ? "null" : evCam.name;
            if (evCam == null) return c;
            Vector3 sp = evCam.WorldToScreenPoint(centre);
            c.screenPoint = sp.ToString("F1") + " pixelRect=" + evCam.pixelRect;
            c.onScreen = sp.z > 0 && evCam.pixelRect.Contains(new Vector2(sp.x, sp.y));
            Vector2 pt = new Vector2(sp.x, sp.y);

            // EventSystem.RaycastAll (what uGUI would deliver to), top first.
            var es = EventSystem.current != null ? EventSystem.current : UnityEngine.Object.FindObjectOfType<EventSystem>();
            c.eventSystem = es == null ? "none" : Path(es.transform) + " module=" + (es.currentInputModule == null ? "null" : es.currentInputModule.GetType().Name);
            if (es != null)
            {
                try
                {
                    var hits = new List<RaycastResult>();
                    es.RaycastAll(new PointerEventData(es) { position = pt }, hits);
                    foreach (var h in hits.Take(20))
                        c.eventSystemHits.Add(Path(h.gameObject == null ? null : h.gameObject.transform) + " module=" + (h.module == null ? "null" : h.module.GetType().Name)
                            + " depth=" + h.depth + " sortingOrder=" + h.sortingOrder + " dist=" + h.distance.ToString("F3"));
                    if (hits.Count > 0 && hits[0].gameObject != null)
                    {
                        c.topEventSystemHit = Path(hits[0].gameObject.transform);
                        c.topIsYesOrChild = hits[0].gameObject.transform.IsChildOf(yes.transform).ToString();
                    }
                    else { c.topEventSystemHit = "none"; c.topIsYesOrChild = "no hit"; }
                }
                catch (Exception e) { c.eventSystemHits.Add("RaycastAll error: " + e.Message); }
            }

            // Manual hit test over every raycast-target Graphic of the root canvas (independent of EventSystem).
            if (rootCanvas != null)
            {
                var gs = new List<KeyValuePair<long, string>>();
                foreach (var g in rootCanvas.GetComponentsInChildren<UIGraphic>(false))
                {
                    if (g == null || !g.raycastTarget || !g.isActiveAndEnabled) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, pt, evCam)) continue;
                    var gc = g.canvas;
                    long key = (long)(gc == null ? 0 : gc.sortingOrder) * 100000L + g.depth;
                    gs.Add(new KeyValuePair<long, string>(key, Path(g.transform) + " " + g.GetType().Name + " depth=" + g.depth
                        + " canvasOrder=" + (gc == null ? "?" : gc.sortingOrder.ToString()) + " isYesOrChild=" + g.transform.IsChildOf(yes.transform)));
                }
                foreach (var kv in gs.OrderByDescending(k => k.Key).Take(25)) c.graphicHits.Add(kv.Value);
                if (gs.Count == 0) c.graphicHits.Add("no raycast-target Graphic contains the point");
            }

            // Physics: camera -> Yes centre (ClientSim's desktop pointer hits the UiShape collider first).
            Vector3 origin = evCam.transform.position;
            Vector3 dir = centre - origin;
            float dist = dir.magnitude;
            if (dist > 0.0001f)
            {
                var ph = Physics.RaycastAll(origin, dir / dist, dist + 1f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Take(15);
                foreach (var h in ph)
                    c.physicsHits.Add(Path(h.collider.transform) + " " + h.collider.GetType().Name + " trigger=" + h.collider.isTrigger + " layer=" + LayerMask.LayerToName(h.collider.gameObject.layer) + " dist=" + h.distance.ToString("F3") + " (Yes at " + dist.ToString("F3") + ")");
                if (c.physicsHits.Count == 0) c.physicsHits.Add("no collider between the camera and the Yes centre");
            }
            Save();
            return c;
        }

        // ------------------------------------------------------------------ helpers
        static UdonBehaviour FindPouch()
        {
            UdonBehaviour byName = null, bySymbols = null;
            foreach (var u in Resources.FindObjectsOfTypeAll<UdonBehaviour>())
            {
                if (u == null || !u.gameObject.scene.IsValid()) continue;
                object a, b;
                bool fits = u.TryGetProgramVariable("confirmRoot", out a) && u.TryGetProgramVariable("liveProducts", out b);
                if (!fits) continue;
                if (u.gameObject.name == PouchObjectName) byName = u;
                else if (bySymbols == null) bySymbols = u;
            }
            if (byName == null && bySymbols != null) report.trace.Add("pouch found by symbols on " + Path(bySymbols.transform));
            return byName ?? bySymbols;
        }

        static void PressAccept()
        {
            int n = 0;
            foreach (var b in Resources.FindObjectsOfTypeAll<UIButton>())
                if (b != null && b.gameObject.scene.IsValid() && b.gameObject.name == "AcceptButton" && b.gameObject.activeInHierarchy)
                {
                    b.onClick.Invoke();
                    n++;
                }
            report.acceptPressed = n == 0 ? "no active AcceptButton (already accepted / menu hidden)" : "invoked " + n;
        }

        static IEnumerator Wait(double seconds)
        {
            double until = Now + seconds;
            while (Now < until) yield return null;
        }

        static ClientSimPlayer LocalPlayer()
        {
            return Resources.FindObjectsOfTypeAll<ClientSimPlayer>().FirstOrDefault(p => p.Player != null && p.Player.isLocal);
        }

        static object Get(UdonBehaviour u, string sym)
        {
            object v;
            if (u == null || !u.TryGetProgramVariable(sym, out v)) return null;
            return v;
        }

        /// <summary>"NO_BEHAVIOUR" / "MISSING" (symbol not in the program) / "null" / value.</summary>
        static string V(UdonBehaviour u, string sym)
        {
            if (u == null) return "NO_BEHAVIOUR";
            object v;
            if (!u.TryGetProgramVariable(sym, out v)) return "MISSING";
            return Fmt(v);
        }

        static string Fmt(object v)
        {
            if (v == null) return "null";
            var uo = v as UnityEngine.Object;
            if (uo != null) return uo.name + " (" + uo.GetType().Name + ")";
            if (v is UnityEngine.Object) return "destroyed";
            var arr = v as Array;
            if (arr != null && !(v is string)) return v.GetType().Name + "[" + arr.Length + "]";
            return v.ToString();
        }

        static bool B(UdonBehaviour u, string sym) { object v = Get(u, sym); return v is bool && (bool)v; }
        static int I(UdonBehaviour u, string sym, int fallback) { object v = Get(u, sym); return v is int ? (int)v : fallback; }
        static string S(UdonBehaviour u, string sym) { return Get(u, sym) as string; }
        static Array Arr(UdonBehaviour u, string sym) { return Get(u, sym) as Array; }
        static UdonBehaviour UB(UdonBehaviour u, string sym) { var v = Get(u, sym); return v as UdonBehaviour ?? (v is Component ? ((Component)v).GetComponent<UdonBehaviour>() : null); }

        static int IndexIn(UdonBehaviour owner, string sym, UdonBehaviour target)
        {
            var arr = Arr(owner, sym);
            if (arr == null || target == null) return -1;
            for (int i = 0; i < arr.Length; i++)
            {
                var el = arr.GetValue(i) as UnityEngine.Object;
                if (el != null && el == target) return i;
                var comp = el as Component;
                if (comp != null && comp.gameObject == target.gameObject && comp.GetComponent<UdonBehaviour>() == target) return i;
            }
            return -1;
        }

        static string ElemStr(UdonBehaviour owner, string sym, int i)
        {
            if (owner == null) return "NO_BEHAVIOUR";
            object v;
            if (!owner.TryGetProgramVariable(sym, out v)) return "MISSING";
            var arr = v as Array;
            if (arr == null) return "null array";
            if (i < 0 || i >= arr.Length) return "index " + i + " out of [0," + arr.Length + ")";
            var e = arr.GetValue(i);
            return e == null ? "null" : e.ToString();
        }

        static string ActiveSelf(GameObject go) { return go == null ? "null" : go.activeSelf.ToString(); }

        static string Txt(object o)
        {
            var t = o as TMPro.TMP_Text;
            if (t == null) return o == null ? "null" : "not TMP_Text";
            return t.text;
        }

        /// <summary>Any bool fields on UdonBehaviour whose name hints at a halted program (reflection; names differ by SDK).</summary>
        static string UdonFlags(UdonBehaviour u)
        {
            try
            {
                var parts = new List<string>();
                foreach (var f in typeof(UdonBehaviour).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (f.FieldType != typeof(bool)) continue;
                    string n = f.Name.ToLowerInvariant();
                    if (n.Contains("error") || n.Contains("halt") || n.Contains("initialized") || n.Contains("ready"))
                        parts.Add(f.Name + "=" + f.GetValue(u));
                }
                return parts.Count == 0 ? "none found" : string.Join(" ", parts.ToArray());
            }
            catch (Exception e) { return "reflection error " + e.Message; }
        }

        static string Path(Transform t)
        {
            if (t == null) return "null";
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        static void Why(string reason) { if (report != null && string.IsNullOrEmpty(report.failure)) report.failure = reason; }

        static void Log(string message, string stack, LogType type)
        {
            if (report == null || message == null) return;
            if (message.StartsWith("[KD_Commerce] bought")) report.boughtLines++;
            bool mine = message.StartsWith("[KD_") || message.StartsWith("[KH_") || message.Contains("Udon");
            if (mine && report.logs.Count < 400)
                report.logs.Add((Now - t0).ToString("F2") + " " + type + " " + (message.Length > 400 ? message.Substring(0, 400) : message));
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && report.errors.Count < 50)
                report.errors.Add(message + "\n" + (stack == null ? "" : stack.Length > 600 ? stack.Substring(0, 600) : stack));
        }

        static void Save()
        {
            if (report == null) return;
            try
            {
                Directory.CreateDirectory(Base);
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Latest, json);
                File.WriteAllText(Journal, json);
            }
            catch (Exception e) { Debug.LogWarning("[KH Live Buy Probe] save failed: " + e.Message); }
        }

        static void WriteBlocked(string why)
        {
            try
            {
                Directory.CreateDirectory(Base);
                var r = new Report { runId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-blocked", status = "BLOCKED", failure = why, started = DateTime.Now.ToString("o"), ended = DateTime.Now.ToString("o"), unity = Application.unityVersion };
                File.WriteAllText(Latest, JsonUtility.ToJson(r, true));
            }
            catch { }
        }

        // ClientSim persists PlayerData per scene; keep the user's save as it was before the probe.
        static void BackupPlayerData()
        {
            if (Directory.Exists(BackupDir)) Directory.Delete(BackupDir, true);
            Directory.CreateDirectory(BackupDir);
            if (!Directory.Exists(PlayerDataDir)) { report.playerData.Add("no " + PlayerDataDir + " before Play"); return; }
            foreach (var f in Directory.GetFiles(PlayerDataDir, "*_" + SceneName + ".json"))
            {
                File.Copy(f, BackupDir + "/" + System.IO.Path.GetFileName(f), true);
                report.playerData.Add("backed up " + f);
            }
        }

        static void RestorePlayerData()
        {
            if (report == null) return;
            if (!Directory.Exists(BackupDir)) { report.playerData.Add("no backup dir; nothing restored"); return; }
            var saved = new HashSet<string>(Directory.GetFiles(BackupDir, "*.json").Select(f => System.IO.Path.GetFileName(f)));
            if (Directory.Exists(PlayerDataDir))
                foreach (var f in Directory.GetFiles(PlayerDataDir, "*_" + SceneName + ".json"))
                    if (!saved.Contains(System.IO.Path.GetFileName(f))) { File.Delete(f); report.playerData.Add("deleted (created during Play) " + f); }
            foreach (var name in saved)
            {
                Directory.CreateDirectory(PlayerDataDir);
                File.Copy(BackupDir + "/" + name, PlayerDataDir + "/" + name, true);
                report.playerData.Add("restored " + PlayerDataDir + "/" + name);
            }
        }

        static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
    }
}
