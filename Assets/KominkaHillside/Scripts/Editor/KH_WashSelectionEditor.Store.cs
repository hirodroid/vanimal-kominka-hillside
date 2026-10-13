using System;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UdonSharpEditor;
using KDDefs = KominkaDogRun.Editor.KD_ProductDefinitions;
using KDLog = KominkaDogRun.Editor.KD_VerifyLog;
using KDItem = KominkaDogRun.KD_ShopItem;

namespace KominkaHillside.Editor
{
public static partial class KH_WashSelectionEditor
{
    // IMP-DOG-056 (2026-10-14): the wash sets as Credits products. 54 wires each station's KH_WashSelection to the four set items that Menu/4
    // Setup Shop builds (KH_Dogs/KD_ShopCatalog/KD_ShopItem_Wash_*, index = category) and to its own KH_WashStationReset (the idle check reads
    // its tools), and gives each board the complete set's Listing (C-5, no Product of its own), the 52-word table and a purchase button.
    // Resolves everything first: a missing / stale set item or station part writes nothing (FAIL). Unchanged values are not written again.
    // 44 checks all of it (VerifyStoreWiring per station from VerifyCategories, VerifyStoreLogic from VerifyCategoryLogic). Not saved.
    const string StoreLogPath = "Logs/kh-wash-store-wire.txt", BuyName = "Buy";
    // The free slot of the bottom row after 46 (Reset x -422..-162, Take x 49..399, y -324..-268): 180 wide leaves 15.5 px to each.
    // Written only when the button is created (a later hand adjustment stays; 44 checks it stays inside the board, clear of Reset / Take).
    public static readonly Vector2 BuyPos = new Vector2(-56.5f, -296f), BuySize = new Vector2(180f, 56f);
    const float BuyMinGap = 10f;

    [MenuItem(Menu + "54 - Wire Store")]
    public static void WireStore()
    {
        Guard();
        var scene = EditorSceneManager.GetActiveScene();
        var log = new StringBuilder();
        log.AppendLine("[WashPremium/54] Wire Store (IMP-DOG-056) scene=" + scene.path + " dirtyBefore=" + scene.isDirty);
        // ---- resolve (nothing is written before every target is known) ----
        string fail = null;
        var sels = UnityEngine.Object.FindObjectsOfType<KH_WashSelection>(true).Where(x => x.gameObject.scene == scene).OrderBy(x => x.transform.parent.name).ToArray();
        if (sels.Length != 4) fail = "expected four KH_WashSelection, found " + sels.Length;
        KDItem[] items = null;
        if (fail == null) fail = ResolveSetItems(scene, out items, log);
        WordsFile ws = null;
        if (fail == null) { try { ws = CheckedWords(); } catch (Exception e) { fail = e.Message; } }
        var resets = new KH_WashStationReset[sels.Length];
        var boards = new KH_WashSelectionBoard[sels.Length];
        for (int i = 0; fail == null && i < sels.Length; i++)
        {
            var sel = sels[i]; var st = sel.transform.parent; string name = st.name;
            var rs = st.GetComponentsInChildren<KH_WashStationReset>(true);
            if (rs.Length != 1) { fail = name + ": expected one KH_WashStationReset, found " + rs.Length; break; }
            var missing = sel.slots == null ? new string[] { "slots null" } : sel.slots.Where(t => t == null || rs[0].tools == null || Array.IndexOf(rs[0].tools, t) < 0).Select(t => t == null ? "null" : t.name).ToArray();
            if (missing.Length > 0) { fail = name + ": the reset board's tools lack the premium slots (" + string.Join(", ", missing) + "; 42 adds them)"; break; }
            resets[i] = rs[0];
            var b = st.GetComponentInChildren<KH_WashSelectionBoard>(true);
            if (b == null || b.selection != sel) { fail = name + ": no PremiumSelectionBoard wired to this selector"; break; }
            if (b.actionButton == null || b.resetLabel == null || b.status == null) { fail = name + ": board parts missing (42 / 46)"; break; }
            if (b.buyButton != null && !BuyWired(b, out string why)) { fail = name + ": the existing purchase button is wired wrong (" + why + "); fix or delete it, then run 54 again"; break; }
            if (b.buyButton == null && b.transform.Find(BuyName) != null) { fail = name + ": a '" + BuyName + "' child exists but the board does not reference it; delete it, then run 54 again"; break; }
            boards[i] = b;
        }
        if (fail != null)
        {
            log.AppendLine("FAIL " + fail);
            log.AppendLine("WIRE_STORE: FAIL (nothing written)");
            KDLog.Write(StoreLogPath, log);
            Debug.LogError("[WashPremium/54] FAIL " + fail);
            return;
        }
        // ---- write (only what differs) ----
        var words = ws.languages.SelectMany(x => x.words).ToArray();
        var codes = ws.languages.Select(x => x.code).ToArray();
        int selWritten = 0, boardWritten = 0, buttonsCreated = 0;
        for (int i = 0; i < sels.Length; i++)
        {
            var sel = sels[i]; var b = boards[i]; string name = sel.transform.parent.name;
            bool proxySame = sel.setItems != null && sel.setItems.SequenceEqual(items) && sel.reset == resets[i];
            if (!proxySame)
            {
                Undo.RecordObject(sel, "Wire Store");
                sel.setItems = (KDItem[])items.Clone(); sel.reset = resets[i];
            }
            if (!proxySame || !BackingSelectionWired(sel)) { Copy(sel); selWritten++; log.AppendLine("  " + name + ": setItems = [" + string.Join(", ", items.Select(x => x.name)) + "], reset = " + PathOf(resets[i].transform)); }
            else log.AppendLine("  " + name + ": setItems / reset unchanged");

            bool boardSame = b.words != null && b.words.SequenceEqual(words) && b.languageCodes != null && b.languageCodes.SequenceEqual(codes) && b.wordCount == WordCount
                && b.allListingId == KDDefs.WashAllListing && b.allCredits == KDDefs.WashAllCredits && b.buyButton != null && b.buyLabel != null;
            if (!boardSame)
            {
                Undo.RecordObject(b, "Wire Store");
                b.words = words; b.languageCodes = codes; b.wordCount = WordCount;
                b.allListingId = KDDefs.WashAllListing; b.allCredits = KDDefs.WashAllCredits;
                if (b.buyButton == null)
                {
                    TextMeshProUGUI label;
                    var btn = Button(b.transform, BuyName, BuyPos, BuySize, b, "_Buy", out label);
                    Undo.RegisterCreatedObjectUndo(btn.gameObject, "Wire Store");
                    label.text = ws.languages[0].words[46];
                    btn.gameObject.SetActive(false);   // the board shows it while the chosen item's set is None (KH_WashSelectionBoard.ShowStore)
                    b.buyButton = btn; b.buyLabel = label; buttonsCreated++;
                    log.AppendLine("  " + name + ": created " + PathOf(btn.transform) + " at " + BuyPos + " size " + BuySize + " -> _Buy (hidden)");
                }
                else if (b.buyLabel == null) b.buyLabel = b.buyButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }
            if (!boardSame || !BackingBoardWired(b)) { Copy(b); boardWritten++; log.AppendLine("  " + name + ": board words " + words.Length + " (13 x " + WordCount + "), allListingId " + b.allListingId + ", allCredits " + b.allCredits); }
            else log.AppendLine("  " + name + ": board unchanged");
        }
        if (selWritten + boardWritten > 0) EditorSceneManager.MarkSceneDirty(scene);
        // the listeners as written (target, SendCustomEvent, "_Buy", call state not Off, String mode; AddStringPersistentListener = RuntimeOnly)
        int badButtons = 0;
        foreach (var b in boards) { string why; if (BuyWired(b, out why)) log.AppendLine("  " + b.transform.parent.name + ": purchase listener " + b.buyButton.onClick.GetPersistentListenerState(0) + " / String / _Buy"); else { badButtons++; log.AppendLine("FAIL " + b.transform.parent.name + ": purchase listener " + why); } }
        if (badButtons > 0) { log.AppendLine("WIRE_STORE: FAIL (written, but " + badButtons + " purchase listener(s) wrong)"); KDLog.Write(StoreLogPath, log); Debug.LogError("[WashPremium/54] FAIL purchase listener"); return; }
        log.AppendLine("WIRE_STORE_OK selectors written=" + selWritten + " boards written=" + boardWritten + " buttons created=" + buttonsCreated + " (NOT saved: run 44 - Verify Selection, 43 - Apply Shared Font, then save)");
        KDLog.Write(StoreLogPath, log);
    }

    /// <summary>The four set items under KH_Dogs/KD_ShopCatalog by KD_ProductDefinitions.WashSetSceneNames, each matching its definition row
    /// (Listing, Product asset ID, credits). Returns the failure, or null.</summary>
    static string ResolveSetItems(Scene scene, out KDItem[] items, StringBuilder log)
    {
        items = null;
        if (KDDefs.WashSetKeys == null || KDDefs.WashSetKeys.Length != 4 || KDDefs.WashSetSceneNames == null || KDDefs.WashSetSceneNames.Length != 4) return "KD_ProductDefinitions.WashSetKeys / WashSetSceneNames must list the four sets";
        var roots = scene.GetRootGameObjects().Where(g => g.name == "KH_Dogs").ToArray();
        if (roots.Length != 1) return "expected one KH_Dogs root, found " + roots.Length;
        var catalog = roots[0].transform.Find("KD_ShopCatalog");
        if (catalog == null) return "KH_Dogs/KD_ShopCatalog missing (run Menu/4 Setup Shop)";
        var found = new KDItem[4];
        for (int c = 0; c < 4; c++)
        {
            var t = catalog.Find(KDDefs.WashSetSceneNames[c]);
            var item = t == null ? null : t.GetComponent<KDItem>();
            if (item == null) return "KH_Dogs/KD_ShopCatalog/" + KDDefs.WashSetSceneNames[c] + " missing (run Products/1 - Apply Metadata, then Menu/4 Setup Shop)";
            KDDefs.Row row;
            try { row = KDDefs.Get(KDDefs.WashSetKeys[c]); } catch (Exception e) { return "no definition row " + KDDefs.WashSetKeys[c] + ": " + e.Message; }
            if (item.ceListingId != row.ceListingId || item.ceProduct == null || item.ceProduct.ID != row.ceProductId || item.ceCredits != row.ceCredits)
                return item.name + " is stale against " + row.key + " (listing " + item.ceListingId + ", product " + (item.ceProduct == null ? "null" : item.ceProduct.ID) + ", credits " + item.ceCredits + "; run Products/1, then Menu/4)";
            found[c] = item;
            log.AppendLine("  set " + c + " " + row.key + " -> " + item.name + " listing " + row.ceListingId + " product " + row.ceProductId + " " + row.ceCredits + " C");
        }
        items = found;
        return null;
    }

    /// <summary>The purchase button calls _Buy on this board's backing UdonBehaviour, once.</summary>
    static bool BuyWired(KH_WashSelectionBoard b, out string why)
    {
        why = null;
        var btn = b.buyButton;
        if (btn == null) { why = "no buyButton"; return false; }
        if (btn.onClick.GetPersistentEventCount() != 1) { why = "persistent listeners " + btn.onClick.GetPersistentEventCount(); return false; }
        var back = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
        if (btn.onClick.GetPersistentTarget(0) != back) { why = "target " + btn.onClick.GetPersistentTarget(0); return false; }
        if (btn.onClick.GetPersistentMethodName(0) != "SendCustomEvent") { why = "method " + btn.onClick.GetPersistentMethodName(0); return false; }
        var so = new SerializedObject(btn);
        var arg = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument");
        if (arg == null || arg.stringValue != "_Buy") { why = "argument " + (arg == null ? "missing" : arg.stringValue); return false; }
        // Codex review 2026-10-14: a listener switched Off, or one that passes no string, never reaches _Buy (54 makes RuntimeOnly / String)
        var state = btn.onClick.GetPersistentListenerState(0);
        if (state == UnityEngine.Events.UnityEventCallState.Off) { why = "call state Off"; return false; }
        var mode = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Mode");
        if (mode == null || mode.intValue != (int)UnityEngine.Events.PersistentListenerMode.String) { why = "argument mode " + (mode == null ? "missing" : ((UnityEngine.Events.PersistentListenerMode)mode.intValue).ToString()) + " (want String)"; return false; }
        return true;
    }

    static bool SameRef(object v, UnityEngine.Object proxy)
    {
        var x = v as UnityEngine.Object;
        if (proxy == null) return x == null;
        if (x == proxy) return true;
        var u = proxy as UdonSharp.UdonSharpBehaviour;
        return u != null && x == UdonSharpEditorUtility.GetBackingUdonBehaviour(u);
    }
    static bool BackingSelectionWired(KH_WashSelection sel)
    {
        var back = UdonSharpEditorUtility.GetBackingUdonBehaviour(sel); object v;
        if (back == null || sel.setItems == null) return false;
        if (!back.publicVariables.TryGetVariableValue("setItems", out v) || !(v is Array a) || a.Length != sel.setItems.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!SameRef(a.GetValue(i), sel.setItems[i])) return false;
        return back.publicVariables.TryGetVariableValue("reset", out v) && SameRef(v, sel.reset);
    }
    static bool BackingBoardWired(KH_WashSelectionBoard b)
    {
        var back = UdonSharpEditorUtility.GetBackingUdonBehaviour(b); object v;
        if (back == null) return false;
        if (!back.publicVariables.TryGetVariableValue("allListingId", out v) || !(v is string s) || s != b.allListingId) return false;
        if (!back.publicVariables.TryGetVariableValue("allCredits", out v) || !(v is int n) || n != b.allCredits) return false;
        if (!back.publicVariables.TryGetVariableValue("wordCount", out v) || !(v is int w) || w != b.wordCount) return false;
        if (!back.publicVariables.TryGetVariableValue("words", out v) || !(v is string[] ws) || b.words == null || !ws.SequenceEqual(b.words)) return false;
        if (!back.publicVariables.TryGetVariableValue("buyButton", out v) || !SameRef(v, b.buyButton)) return false;
        return back.publicVariables.TryGetVariableValue("buyLabel", out v) && SameRef(v, b.buyLabel);
    }
    static Rect BoardRect(RectTransform rt) { var p = rt.anchoredPosition; var s = rt.sizeDelta; return new Rect(p.x - s.x * .5f, p.y - s.y * .5f, s.x, s.y); }
    static float Gap(Rect a, Rect b) { return Mathf.Max(Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax), Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax)); }

    // ---- 44, per station (from VerifyCategories) ----
    static void VerifyStoreWiring(KH_WashSelection sel, Action<bool, string> check, StringBuilder log)
    {
        string st = sel.transform.parent.name;
        bool wired = sel.setItems != null && sel.setItems.Length == 4;
        // FAIL, not WARN: unwired, the gate treats every set as None and refuses every item (no free opening by accident)
        check(wired, "IMP-DOG-056 setItems wired (4 sets) " + st + " (actual " + (sel.setItems == null ? "null" : sel.setItems.Length.ToString()) + "; run WashPremium/54)");
        if (wired)
        {
            for (int c = 0; c < 4; c++)
            {
                var item = sel.setItems[c]; KDDefs.Row row = null;
                try { row = KDDefs.Get(KDDefs.WashSetKeys[c]); } catch (Exception) { }
                bool ok = item != null && row != null && item.name == KDDefs.WashSetSceneNames[c] && item.ceListingId == row.ceListingId && item.ceProduct != null && item.ceProduct.ID == row.ceProductId && item.ceCredits == row.ceCredits;
                check(ok, "set " + c + " " + st + ": " + (item == null ? "null" : item.name + " listing " + item.ceListingId + " product " + (item.ceProduct == null ? "null" : item.ceProduct.ID) + " " + item.ceCredits + " C")
                    + " = " + (row == null ? "(no row " + KDDefs.WashSetKeys[c] + ")" : row.key + " " + row.ceListingId + " / " + row.ceProductId + " / " + row.ceCredits + " C"));
            }
        }
        // Codex review 2026-10-14: the idle check reads reset.tools, so it must be exactly this station's tools (free + premium): every
        // KH_WashTool under the station root, nothing else, no null, no duplicate.
        var actual = sel.transform.parent.GetComponentsInChildren<KH_WashTool>(true);
        var listed = sel.reset != null && sel.reset.tools != null ? sel.reset.tools : new KH_WashTool[0];
        var lacking = actual.Where(t => Array.IndexOf(listed, t) < 0).Select(t => t.name).ToArray();
        var extra = listed.Where(t => t == null || Array.IndexOf(actual, t) < 0).Select(t => t == null ? "null" : PathOf(t.transform)).ToArray();
        var dup = listed.Where(t => t != null).GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key.name).ToArray();
        check(sel.reset != null && sel.reset.transform.IsChildOf(sel.transform.parent) && sel.reset.tools != null && lacking.Length == 0 && extra.Length == 0 && dup.Length == 0 && sel.slots.All(t => Array.IndexOf(listed, t) >= 0),
            "reset = this station's KH_WashStationReset, its tools = every KH_WashTool of the station (" + actual.Length + ": " + actual.Count(t => Array.IndexOf(sel.slots, t) < 0) + " free + " + actual.Count(t => Array.IndexOf(sel.slots, t) >= 0) + " premium) " + st
            + " (" + (sel.reset == null ? "reset null" : PathOf(sel.reset.transform) + ", listed " + listed.Length) + (lacking.Length > 0 ? "; missing " + string.Join(", ", lacking) : "") + (extra.Length > 0 ? "; not of this station " + string.Join(", ", extra) : "") + (dup.Length > 0 ? "; twice " + string.Join(", ", dup) : "") + ")");
        check(BackingSelectionWired(sel), "saved backing setItems / reset " + st);
        check(sel.debugIdleSecondsOverride < 0f, "no idle debug override " + st + " (debugIdleSecondsOverride " + sel.debugIdleSecondsOverride + ")");
        var b = sel.transform.parent.GetComponentInChildren<KH_WashSelectionBoard>(true);
        if (b == null) { check(false, "board " + st); return; }
        check(b.allListingId == KDDefs.WashAllListing && b.allCredits == KDDefs.WashAllCredits, "complete set " + st + ": allListingId " + b.allListingId + " / " + b.allCredits + " C = " + KDDefs.WashAllListing + " / " + KDDefs.WashAllCredits + " C");
        string why;
        check(b.buyButton != null && b.buyLabel != null && b.buyButton.transform.parent == b.transform && b.buyLabel.transform.IsChildOf(b.buyButton.transform) && BuyWired(b, out why),
            "purchase button " + st + " calls _Buy on the board's backing (" + (b.buyButton == null ? "null" : PathOf(b.buyButton.transform)) + ")");
        check(BackingBoardWired(b), "saved backing allListingId / allCredits / words / buyButton / buyLabel " + st);
        if (b.buyButton != null && b.actionButton != null && b.resetLabel != null)
        {
            var root = (RectTransform)b.transform; var buy = BoardRect((RectTransform)b.buyButton.transform);
            var take = BoardRect((RectTransform)b.actionButton.transform); var reset = BoardRect((RectTransform)b.resetLabel.transform.parent);
            var inside = new Rect(-root.sizeDelta.x * .5f, -root.sizeDelta.y * .5f, root.sizeDelta.x, root.sizeDelta.y);
            check(inside.Contains(buy.min) && inside.Contains(buy.max) && Gap(buy, take) >= BuyMinGap && Gap(buy, reset) >= BuyMinGap,
                "purchase button inside the board, >= " + BuyMinGap + " px from Take / Reset " + st + " (rect " + buy + ", gaps " + Gap(buy, take).ToString("F1") + " / " + Gap(buy, reset).ToString("F1") + ")");
        }
    }

    // ---- 44, logic on one station (from VerifyCategoryLogic; its finally restores the selection, revision and the slots' held overrides) ----
    static void VerifyStoreLogic(KH_WashSelection sel, Action<bool, string> check, StringBuilder log)
    {
        const int Own = KDItem.InstanceOwned, None = KDItem.InstanceNone, Unk = KDItem.InstanceUnknown;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        string st = sel.transform.parent.name;
        var stationTools = (sel.reset != null && sel.reset.tools != null ? sel.reset.tools : sel.slots).Where(t => t != null).Distinct().ToArray();
        var touched = stationTools.Concat(sel.slots.Where(t => t != null)).Distinct().ToArray();
        var held0 = touched.Select(t => t.debugHeldOverride).ToArray();
        float idle0 = sel.debugIdleSecondsOverride; var reset0 = sel.reset; var items0 = sel.setItems;
        var fBusy = typeof(KH_WashSelection).GetField("_lastBusyAt", F); var fWarn = typeof(KH_WashSelection).GetField("_warnedUnwired", F);
        object busy0 = fBusy.GetValue(sel), warn0 = fWarn.GetValue(sel);
        var fDog = typeof(KH_WashStationCall).GetField("heldDog", F); int dog0 = sel.call != null ? (int)fDog.GetValue(sel.call) : -1;
        var fSeen = typeof(KH_WashTool).GetField("_lastHeldSeen", F); var seen0 = touched.Select(t => fSeen.GetValue(t)).ToArray();
        int f0 = sel.selectedFoam, w0 = sel.selectedWater, n0 = sel.selectedFinish, b0 = sel.selectedBath, r0 = sel.revision;
        Func<string> state = () => "foam=" + sel.selectedFoam + " water=" + sel.selectedWater + " finish=" + sel.selectedFinish + " bath=" + sel.selectedBath + " rev=" + sel.revision;
        string[] stateNames = { "Unknown", "Owned", "None" };
        try
        {
            foreach (var t in touched) t.debugHeldOverride = 0;
            // (b) the gate: Held -> Stale -> reset -> Unknown (Pending) -> not Owned (Locked) -> apply; a refusal changes nothing
            foreach (int s in new[] { Own, None, Unk })
            {
                string sn = stateNames[s];
                int expect = s == Own ? KH_WashSelection.ResultApplied : s == Unk ? KH_WashSelection.ResultPending : KH_WashSelection.ResultLocked;
                sel.selectedFoam = 0; sel.selectedWater = -1; sel.selectedFinish = -1; sel.selectedBath = -1; int rev = sel.revision;
                Func<int, int, int, bool> same = (water, bath, revision) => sel.selectedFoam == 0 && sel.selectedWater == water && sel.selectedFinish == -1 && sel.selectedBath == bath && sel.revision == revision;
                sel.slots[2].debugHeldOverride = 1;
                int ch = sel._Decide(6, rev, s);
                check(ch == KH_WashSelection.ResultHeld && same(-1, -1, rev), "gate " + sn + ": water held -> 1 Held before the set is asked, nothing changed (actual " + ch + ", " + state() + ")");
                sel.slots[2].debugHeldOverride = 0;
                int cs = sel._Decide(6, rev - 1, s);
                check(cs == KH_WashSelection.ResultStale && same(-1, -1, rev), "gate " + sn + ": stale revision -> 2 Stale, nothing changed (actual " + cs + ", " + state() + ")");
                int ct = sel._Decide(6, rev, s);
                check(ct == expect && same(s == Own ? 6 : -1, -1, rev), "gate " + sn + ": WateringCan -> " + expect + (s == Own ? " applied" : " refused, nothing changed") + " (actual " + ct + ", " + state() + ")");
                int cb = sel._Decide(13, rev, s);
                check(cb == expect && same(s == Own ? 6 : -1, s == Own ? 13 : -1, rev), "gate " + sn + ": platform Hinoki -> " + expect + " (actual " + cb + ", " + state() + ")");
                int cr = sel._Decide(-1, rev, s);
                check(cr == KH_WashSelection.ResultApplied && sel.selectedFoam == -1 && sel.selectedWater == -1 && sel.selectedBath == -1 && sel.revision == rev, "gate " + sn + ": reset -> 0 whatever the set (actual " + cr + ", " + state() + ")");
            }
            int odd = sel._Decide(6, sel.revision, 99);
            check(odd == KH_WashSelection.ResultLocked && sel.selectedWater == -1, "gate: an unexpected set state locks (actual " + odd + ")");
            sel.setItems = null;
            int unwired = sel._SetState(1);
            check(unwired == None, "unwired setItems -> _SetState None (never free by accident; actual " + unwired + ")");
            sel.setItems = items0;
            // (c) the end of a wash (_StationIdle): every tool of the station, the stand's dog, 60 s
            foreach (var t in stationTools) t.debugHeldOverride = -1;
            foreach (var t in sel.slots) t.debugHeldOverride = -1;
            if (sel.call != null) fDog.SetValue(sel.call, -1);
            sel.debugIdleSecondsOverride = KH_WashSelection.IdleGraceSeconds;
            check(sel._StationIdle(), "idle: nothing held, no dog, " + KH_WashSelection.IdleGraceSeconds + " s -> true " + st);
            sel.debugIdleSecondsOverride = KH_WashSelection.IdleGraceSeconds - .1f;
            check(!sel._StationIdle(), "idle: " + (KH_WashSelection.IdleGraceSeconds - .1f) + " s -> false (the grace not over) " + st);
            sel.debugIdleSecondsOverride = KH_WashSelection.IdleGraceSeconds;
            var free = stationTools.FirstOrDefault(t => Array.IndexOf(sel.slots, t) < 0);
            check(free != null, "the station's tools include a free tool " + st + " (" + stationTools.Length + " tools)");
            if (free != null) { free.debugHeldOverride = 1; check(!sel._StationIdle(), "idle: the free tool " + free.name + " held -> false " + st); free.debugHeldOverride = -1; }
            sel.slots[2].debugHeldOverride = 1; check(!sel._StationIdle(), "idle: a premium tool held -> false " + st); sel.slots[2].debugHeldOverride = -1;
            if (sel.call != null) { fDog.SetValue(sel.call, 0); check(!sel._StationIdle(), "idle: a dog on the stand -> false " + st); fDog.SetValue(sel.call, -1); }
            else log.AppendLine("idle " + st + ": no KH_WashStationCall wired (the dog branch is not reachable here)");
            sel.debugIdleSecondsOverride = -1f;
            sel.slots[0].debugHeldOverride = 1; sel._StationIdle(); sel.slots[0].debugHeldOverride = -1;
            check(!sel._StationIdle(), "idle (real clock): just seen busy -> false, the 60 s start again " + st);
            // Codex review 2026-10-14: the 60 s count from each tool's own last seen hold (KH_WashTool._LastHeldAt), not from the Owner's poll.
            // A tool held and put down between two polls (never seen busy by a poll) still holds the revert back.
            float now = Time.time, past = now - KH_WashSelection.IdleGraceSeconds - 1f;
            fBusy.SetValue(sel, past); foreach (var t in touched) fSeen.SetValue(t, past);
            check(sel._StationIdle(), "idle (real clock): every tool last held and the dog / wait start " + (KH_WashSelection.IdleGraceSeconds + 1f) + " s ago -> true " + st);
            var between = free != null ? free : sel.slots[2];
            fSeen.SetValue(between, now - 30f);
            check(!sel._StationIdle(), "idle (real clock): " + between.name + " last held 30 s ago, put down between polls (not held now, never seen busy by a poll) -> false " + st);
            fSeen.SetValue(between, past); fBusy.SetValue(sel, now - 30f);
            check(!sel._StationIdle(), "idle (real clock): the dog / the wait start 30 s ago -> false " + st);
            fBusy.SetValue(sel, past);
            sel.debugIdleSecondsOverride = KH_WashSelection.IdleGraceSeconds; sel.reset = null;
            check(sel._StationIdle(), "idle without a reset board: the premium slots only, none held -> true " + st);
            sel.slots[0].debugHeldOverride = 1; check(!sel._StationIdle(), "idle without a reset board: a premium slot held -> false " + st); sel.slots[0].debugHeldOverride = -1;
            sel.reset = reset0;
            var b = sel.transform.parent.GetComponentInChildren<KH_WashSelectionBoard>(true);
            if (b != null) VerifyStoreBoardText(b, check, log);
            log.AppendLine("store logic " + st + " (Edit mode, proxy; _Decide / _StationIdle shared with runtime): " + state());
        }
        finally
        {
            for (int i = 0; i < touched.Length; i++) { touched[i].debugHeldOverride = held0[i]; fSeen.SetValue(touched[i], seen0[i]); }
            sel.debugIdleSecondsOverride = idle0; sel.reset = reset0; sel.setItems = items0;
            fBusy.SetValue(sel, busy0); fWarn.SetValue(sel, warn0);
            if (sel.call != null) fDog.SetValue(sel.call, dog0);
            sel.selectedFoam = f0; sel.selectedWater = w0; sel.selectedFinish = n0; sel.selectedBath = b0; sel.revision = r0;
        }
    }

    /// <summary>The board's store line per state (ja contents; every language for overflow) and the purchase button's visibility, plus the
    /// two new result words in the status line. Restores the board's language, cached state, status and button.</summary>
    static void VerifyStoreBoardText(KH_WashSelectionBoard b, Action<bool, string> check, StringBuilder log)
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        var ty = typeof(KH_WashSelectionBoard);
        var fl = ty.GetField("_lang", F); var fs = ty.GetField("_storeState", F); var fc = ty.GetField("_storeCategory", F); var fch = ty.GetField("_chosen", F);
        var mr = ty.GetMethod("ResultWord", F);
        object l0 = fl.GetValue(b), s0 = fs.GetValue(b), c0 = fc.GetValue(b);
        bool buy0 = b.buyButton != null && b.buyButton.gameObject.activeSelf; string status0 = b.status != null ? b.status.text : null;
        var words = ReadWords(); int overflow = 0, wrong = 0;
        int[] states = { KDItem.InstanceOwned, KDItem.InstanceNone, KDItem.InstanceUnknown, KH_WashSelectionBoard.StoreContinuing };
        check((int)mr.Invoke(b, new object[] { KH_WashSelection.ResultLocked }) == 48 && (int)mr.Invoke(b, new object[] { KH_WashSelection.ResultPending }) == 49, "board result words: 4 Locked -> 48, 5 Pending -> 49");
        Action<TMP_Text, string> fits = (t, what) =>
        {
            if (t == null || !t.gameObject.activeInHierarchy) return;
            t.ForceMeshUpdate();
            if (t.isTextOverflowing && t.text.All(ch => char.IsControl(ch) || t.font.HasCharacter(ch, true))) { overflow++; log.AppendLine("OVERFLOW " + what + " " + t.name + " '" + t.text + "' rect " + t.rectTransform.sizeDelta); }
        };
        try
        {
            for (int l = 0; l < words.languages.Length; l++)
            {
                fl.SetValue(b, l); var w = words.languages[l].words;
                for (int tab = 0; tab < 5; tab++)
                {
                    ty.GetMethod("_Tab" + tab).Invoke(b, null);
                    int chosen = (int)fch.GetValue(b); int cat = b.groups != null && chosen >= 0 && chosen < b.groups.Length ? b.groups[chosen] : -1;
                    foreach (int s in states)
                    {
                        fs.SetValue(b, s); fc.SetValue(b, cat); b._Refresh();
                        bool buy = b.buyButton != null && b.buyButton.gameObject.activeSelf;
                        bool wantBuy = s == KDItem.InstanceNone || s == KH_WashSelectionBoard.StoreContinuing;
                        int credits = tab == 4 ? b.allCredits : b.selection != null && b.selection.setItems != null && cat >= 0 && cat < b.selection.setItems.Length && b.selection.setItems[cat] != null ? b.selection.setItems[cat].ceCredits : 0;
                        string head = s == KDItem.InstanceOwned ? w[47] : s == KDItem.InstanceNone ? (credits > 0 ? credits + " " + w[51] : "") : s == KDItem.InstanceUnknown ? w[49] : w[50];
                        bool ok = buy == wantBuy && b.notice.text.StartsWith(head) && (!buy || b.buyLabel.text == w[46]);
                        if (!ok) { wrong++; log.AppendLine("STORE LINE " + words.languages[l].code + " tab " + tab + " state " + s + ": notice '" + b.notice.text + "' buy " + buy + " (want '" + head + "…', buy " + wantBuy + ")"); }
                        if (l == 0 && tab == 0) log.AppendLine("store line ja tab 0 state " + s + ": '" + b.notice.text + "' buy " + buy);
                        fits(b.notice, words.languages[l].code + " state " + s);
                        if (buy) fits(b.buyLabel, words.languages[l].code);
                    }
                }
                foreach (int k in new[] { 48, 49, 50 }) { b.status.text = w[k]; fits(b.status, words.languages[l].code + " word " + k); }
            }
            check(wrong == 0, "board store line per state (Owned 47 / None price + 51 + 48 with the purchase button / Unknown 49 / continuing 50), 13 languages x 5 tabs (wrong " + wrong + ")");
            check(overflow == 0, "Editor overflow of the store words (fully baked labels only; fallback glyphs = device check) = " + overflow);
        }
        finally
        {
            fl.SetValue(b, l0); fs.SetValue(b, s0); fc.SetValue(b, c0); b._Tab0();
            if (b.buyButton != null && b.buyButton.gameObject.activeSelf != buy0) b.buyButton.gameObject.SetActive(buy0);
            if (b.status != null) b.status.text = status0;
        }
    }
}
}
