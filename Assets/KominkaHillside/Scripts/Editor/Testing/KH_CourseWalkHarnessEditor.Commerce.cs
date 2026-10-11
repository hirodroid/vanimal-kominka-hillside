using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.SDK3.ClientSim;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-PRG-001 (台帳) / IMP-DOG-014 (おやつ) の ClientSim ハーネスの共通部 — Course/96・96b・97。
    ///
    /// 枠は周回コースの歩行ハーネスと同じ (KH_CourseWalkHarnessEditor.cs の Begin / Stop (Course/41) / 全体の締切 /
    /// PlayerData の退避と復旧 (Course/46) / シーンは保存しない / 結果は Logs/kh-course-walk/&lt;runId&gt;/report.json)。
    /// ケースごとの記録は同じ場所の commerce.json (notes・模擬の中身・対象外の一覧)。
    ///
    /// **PlayerData:** Begin が ClientSimStorage/PlayerData/*_VRCDefaultWorldScene.json を退避した後、
    /// 96・97 はその場のファイルを消して「初めて来た人」で Play に入る (退避は Finish が必ず戻す)。
    /// 96b は 96 の最後に写した PlayerData を置いて Play に入る (= ClientSim での再入室)。
    ///
    /// 操作は本物の入口 (手元メニューの _Tab* / _Row* / _TreatBuy* / _ConfirmYes / _ConfirmNo / _PutAway / _Fx* / _Walk* / _Toy* / _Disc*、
    /// おじいさんの Interact と _Close、おやつの実体の Udon イベント _onPickupUseDown / _onPickupUseUp) で行い、観測は Udon のヒープ・
    /// PlayerData (ClientSim の保存先) から読む。模擬 (ヒープへの書き込み) は各ケースの notes に書く。
    /// ケースが落ちても片付け (CmRecover) をして次のケースへ進む (82c と同じ)。最後の状態は *_PASS / *_FAIL。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        const int CmSetLedger = 0, CmSetLedgerReentry = 1, CmSetTreat = 2, CmSetTreatCall = 3;   // 3: IMP-DOG-006 Course/97c (TreatCall.cs)
        const int CmSetToyHover = 4;   // IMP-DOG-040 Course/97d (ToyHover.cs)
        const int CmSetHandFeed = 5;   // IMP-DOG-041 Course/97e: one bounded hand-offer cycle
        const string CmLastLedgerPath = Dir + "/ledger-last.json";
        const string CmBiscuit = "treat.bone_biscuit", CmPawCookie = "treat.paw_cookie", CmJerky = "treat.jerky", CmFxPawCookie = "petfx.paw_cookie";

        [Serializable] public class CmCaseResult
        {
            public string name, status = "PENDING";
            public bool simulated;
            public List<string> notes = new List<string>();
        }
        [Serializable] class CmReport
        {
            public string runId, harness, source;
            public List<CmCaseResult> cases = new List<CmCaseResult>();
            public List<string> notCovered = new List<string>();
            public List<string> logs = new List<string>();
        }
        [Serializable] class CmLedgerLast { public string runId, status, file, name; public int localId; }

        static List<List<Func<double, bool>>> cmScripts;
        static List<double> cmLimits;
        static CmReport cmReport;
        static int cmStep;
        static double cmStepAt, cmCaseAt;
        static bool cmFailed, cmLoadedForced;
        static float cmMinuteSeconds = 60f;
        static UdonBehaviour cmLedger, cmKeeper, cmCatalog, cmBox, cmRecall;
        static UdonBehaviour[] cmSlots, cmProducts;
        static List<VRCPlayerApi> cmPuppets = new List<VRCPlayerApi>();

        // ============================================================
        // 入口
        // ============================================================

        static void BeginCommerce(int set)
        {
            string source = null;
            if (set == CmSetLedgerReentry)
            {
                // Play に入る前に確かめる (無ければ何もしない)
                if (!File.Exists(CmLastLedgerPath)) throw new InvalidOperationException("No " + CmLastLedgerPath + ": run Course/96 first");
                var last = JsonUtility.FromJson<CmLedgerLast>(File.ReadAllText(CmLastLedgerPath));
                if (last == null || string.IsNullOrEmpty(last.file) || !File.Exists(last.file)) throw new InvalidOperationException("The 96 PlayerData snapshot is missing: " + (last == null ? "?" : last.file));
                if (string.IsNullOrEmpty(last.name) || !last.name.EndsWith("_VRCDefaultWorldScene.json", StringComparison.Ordinal) || Path.GetFileName(last.name) != last.name)
                    throw new InvalidOperationException("Unsafe snapshot name: " + last.name);
                source = last.file;
            }
            Begin(false);
            report.commerceOnly = true;
            report.commerceSet = set;
            report.commerceSource = source;
            report.deadline = EditorApplication.timeSinceStartup + (set == CmSetHandFeed ? 120 : set == CmSetLedger ? 1200 : set == CmSetLedgerReentry ? 600 : set == CmSetTreatCall ? 1500 : set == CmSetToyHover ? 1000 : 1800);
            report.limitations = "Single local ClientSim; the local player is instance master and every Owner. Menu and NPC operations go through the real Udon events "
                + "(no physical UI click, ray or proximity). PlayerData is ClientSim's local file: 'saved' = written to ClientSimStorage, 're-entry' (96b) = a new Play reading that file; "
                + "not VRChat's server persistence or OnPlayerRestored timing. Heap writes (stay-accrual freeze, restore delay, slots held by puppets) are SIMULATED and listed in each case's notes. "
                + "Not covered (PCVR / devices): real hand grab and VR Use routing, petting, gathering (trainer CE), real disc/ball throws, CE Store ownership, several real clients racing for the 3 treat slots.";
            try { CmPrepareStorage(set, source); }
            catch (Exception e) { End("BLOCKED", "PlayerData preparation failed (nothing tested): " + e.Message); return; }
            Save();
        }

        /// <summary>Begin が退避を済ませた後で、この場の PlayerData を「初めて」(96・97) か「96 の続き」(96b) にする。
        /// 退避 (manifest) が揃っていることを確かめてから消す。戻すのは Finish (RestorePlayerData)。</summary>
        static void CmPrepareStorage(int set, string source)
        {
            string storage = Path.GetFullPath("ClientSimStorage/PlayerData");
            Directory.CreateDirectory(storage);
            var manifest = File.ReadAllLines(report.playerDataBackup + "/manifest.txt");
            foreach (var name in manifest)
                if (!File.Exists(report.playerDataBackup + "/" + name) || Hash(report.playerDataBackup + "/" + name) != Hash(Path.Combine(storage, name)))
                    throw new IOException("Backup of " + name + " is not identical; nothing deleted");
            foreach (var path in Directory.GetFiles(storage, "*_VRCDefaultWorldScene.json"))
            {
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), storage, StringComparison.OrdinalIgnoreCase)) throw new IOException("PlayerData path outside storage");
                File.Delete(path);
            }
            if (set == CmSetLedgerReentry)
            {
                var last = JsonUtility.FromJson<CmLedgerLast>(File.ReadAllText(CmLastLedgerPath));
                File.Copy(source, Path.Combine(storage, last.name), true);
            }
        }

        // ============================================================
        // 組み立て (MakeCases から)
        // ============================================================

        static void MakeCommerceCases()
        {
            Application.logMessageReceived -= CmLog; Application.logMessageReceived += CmLog;
            cmScripts = new List<List<Func<double, bool>>>(); cmLimits = new List<double>();
            cmStep = 0; cmCaseAt = -1; cmFailed = false; cmLoadedForced = false; cmPuppets.Clear();
            cmReport = new CmReport { runId = report.runId, harness = report.commerceSet == CmSetLedger ? "ledger (Course/96)" : report.commerceSet == CmSetLedgerReentry ? "ledger re-entry (Course/96b)" : report.commerceSet == CmSetTreatCall ? "treat call on the leash (Course/97c)" : report.commerceSet == CmSetToyHover ? "toy recall hover (Course/97d)" : "treats (Course/97)", source = report.commerceSource };
            if (report.commerceSet == CmSetHandFeed) cmReport.harness = "hand feeding (Course/97e, HF1 only)";
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true);
            cmLedger = CmOne(all, "KD_ProgressLedger");
            cmKeeper = CmOne(all, "KD_ShopKeeper");
            cmCatalog = CmOne(all, "KD_ShopCatalog");
            cmBox = CmOne(all, "KD_TreatBox");
            cmRecall = CmOne(all, "KH_ToyRecall");
            cmSlots = CmRefs(cmBox, "slots");
            cmProducts = CmRefs(cmLedger, "products");
            if (cmSlots.Length != 3 || cmSlots.Any(x => x == null)) throw new InvalidOperationException("KD_TreatBox.slots must be 3 wired slots (run Menu/6c)");
            if (cmProducts.Length == 0) throw new InvalidOperationException("Ledger has no products (run Menu setup for the catalog)");
            cmMinuteSeconds = Get<float>(cmLedger, "minuteSeconds");
            if (report.commerceSet == CmSetLedger) MakeLedgerCases();
            else if (report.commerceSet == CmSetLedgerReentry) MakeLedgerReentryCases();
            else if (report.commerceSet == CmSetTreatCall) MakeTreatCallCases();
            else if (report.commerceSet == CmSetToyHover) MakeToyHoverCases();
            else if (report.commerceSet == CmSetHandFeed) MakeHandFeedCases();
            else MakeTreatCases();
            CmWrite();
        }

        static List<Func<double, bool>> CmCase(string name, bool simulated, double limit = 240)
        {
            report.cases.Add(new Case { name = name });
            cmReport.cases.Add(new CmCaseResult { name = name, simulated = simulated });
            cmLimits.Add(limit);
            var s = new List<Func<double, bool>>(); cmScripts.Add(s); return s;
        }

        // ============================================================
        // 実行
        // ============================================================

        static void CommerceTick(double now)
        {
            if (cmScripts == null || caseIndex < 0 || caseIndex >= cmScripts.Count) throw new InvalidOperationException("Commerce script lost (domain reload?)");
            if (cmStep == 0 && cmCaseAt != Current.startedAt) { cmCaseAt = Current.startedAt; cmStepAt = now; }
            try
            {
                if (now - Current.startedAt > cmLimits[caseIndex]) throw new TimeoutException("Case deadline (" + cmLimits[caseIndex] + " s)");
                var steps = cmScripts[caseIndex];
                while (cmStep < steps.Count)
                {
                    if (!steps[cmStep](now)) return;
                    cmStep++; cmStepAt = now;
                }
                Current.status = "PASS"; Current.endedAt = now; Current.reason = string.Join(" | ", cmReport.cases[caseIndex].notes);
                cmReport.cases[caseIndex].status = "PASS";
            }
            catch (Exception e)
            {
                cmFailed = true;
                Current.status = "FAIL"; Current.endedAt = now; Current.reason = "step " + cmStep + ": " + e.GetType().Name + ": " + e.Message;
                cmReport.cases[caseIndex].status = "FAIL";
                cmReport.cases[caseIndex].notes.Add("FAIL at step " + cmStep + ": " + e.Message);
                cmReport.cases[caseIndex].notes.Add("at: " + TvWhere(e));
                CmRecover();
            }
            cmStep = 0;
            CmWrite(); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); return; }
            string head = report.commerceSet == CmSetLedger ? "LEDGER" : report.commerceSet == CmSetLedgerReentry ? "LEDGER_REENTRY" : report.commerceSet == CmSetTreatCall ? "TREAT_CALL" : report.commerceSet == CmSetToyHover ? "TOY_HOVER" : "TREAT";
            if (report.commerceSet == CmSetHandFeed) head = "HAND_FEED";
            End(head + (cmFailed ? "_FAIL" : "_PASS"), cmFailed ? "One or more cases failed; see commerce.json" : "Local ClientSim commerce cases passed. Not network, server persistence, device or PCVR acceptance.");
        }

        /// <summary>落ちたケースの後始末 (どれも失敗してよい。次のケースが前の状態を引きずらないため)。</summary>
        static void CmRecover()
        {
            HfRestoreTracking();
            try { if (cmLoadedForced) { cmLedger.SetProgramVariable("_loaded", true); cmLoadedForced = false; } } catch (Exception) { }
            try { if (CmState() == KominkaDogRun.KD_TreatBox.DispOut) pouch.SendCustomEvent("_PutAway"); } catch (Exception) { }
            try { CmDetachAll(); } catch (Exception) { }
            try { CmClearPuppetLeashes(); } catch (Exception) { }   // IMP-DOG-006: simulated leashes held by puppets (TreatCall.cs)
            try { TcClearTicket(); } catch (Exception) { }
            try { CmReleasePuppetSlots(); CmRemovePuppets(); } catch (Exception) { }
            try { cmKeeper.SendCustomEvent("_Close"); } catch (Exception) { }
            try { if (Get<int>(pouch, "_confirmKind") != 0) pouch.SendCustomEvent("_ConfirmNo"); } catch (Exception) { }
        }

        static void CmLog(string condition, string stack, LogType type)
        {
            if (report == null || !report.commerceOnly || cmReport == null || !EditorApplication.isPlaying || cmReport.logs.Count >= 200) return;
            if (condition.StartsWith("[KD_Commerce]") || condition.StartsWith("[KD_Treat") || condition.StartsWith("[KD_Shop")) cmReport.logs.Add(condition);
        }

        static void CmWrite()
        {
            if (report == null || cmReport == null) return;
            Directory.CreateDirectory(Dir + "/" + report.runId);
            File.WriteAllText(Dir + "/" + report.runId + "/commerce.json", JsonUtility.ToJson(cmReport, true));
        }

        // ---- 手順の部品 (TvDo 等と同じ形。時刻は cmStepAt から) ----
        static Func<double, bool> CmDo(Action a) { return now => { a(); return true; }; }
        static Func<double, bool> CmWait(double seconds) { return now => now - cmStepAt >= seconds; }
        static Func<double, bool> CmUntil(Func<bool> condition, double limit, Func<string> what)
        { return now => { if (condition()) return true; if (now - cmStepAt > limit) throw new TimeoutException(what() + " (" + limit + " s)"); return false; }; }
        static void CmExpect(bool ok, string what) { if (!ok) throw new InvalidOperationException("Expected: " + what); }
        static void CmNote(string text) { cmReport.cases[caseIndex].notes.Add(text); }

        // ============================================================
        // 読み取り
        // ============================================================

        static UdonBehaviour CmOne(UdonBehaviour[] all, string program)
        {
            var found = all.Where(u => u.programSource != null && u.programSource.name == program).ToArray();
            if (found.Length != 1) throw new InvalidOperationException(program + " in scene: " + found.Length + " (expected 1)");
            return found[0];
        }

        /// <summary>U# の Behaviour 配列 (ヒープでは Component[] 等) を UdonBehaviour[] で読む。</summary>
        static UdonBehaviour[] CmRefs(UdonBehaviour u, string key)
        {
            object o;
            if (u == null || !u.TryGetProgramVariable(key, out o) || !(o is Array a)) throw new InvalidOperationException("Missing runtime array " + key);
            return a.Cast<object>().Select(x => x as UdonBehaviour).ToArray();
        }

        static string CmStr(UdonBehaviour u, string key) { object o; return u != null && u.TryGetProgramVariable(key, out o) ? o as string : null; }
        static GameObject CmGo(UdonBehaviour u, string key) { object o; return u != null && u.TryGetProgramVariable(key, out o) ? o as GameObject : null; }
        static string CmText(UdonBehaviour u, string key)
        {
            object o; if (u == null || !u.TryGetProgramVariable(key, out o) || !(o is Component c) || c == null) return "";
            var p = c.GetType().GetProperty("text"); return p == null ? "" : (string)p.GetValue(c) ?? "";
        }

        static int CmMe() { return Networking.LocalPlayer.playerId; }
        static string CmNs() { return CmStr(cmLedger, "storageNamespace"); }
        static string CmKey(string kind, string key) { return CmNs() + "." + kind + (string.IsNullOrEmpty(key) ? "" : "." + key); }
        static int CmEarned() { return Get<int>(cmLedger, "_earned"); }
        static int CmSpent() { return Get<int>(cmLedger, "_spent"); }
        static int CmPoints() { return Math.Max(0, CmEarned() - CmSpent()); }
        static int CmGranted() { return Get<int>(cmLedger, "_grantedMinutes"); }
        static bool CmLoaded() { return Get<bool>(cmLedger, "_loaded"); }
        static int CmIdx(string productKey)
        {
            for (int i = 0; i < cmProducts.Length; i++) if (cmProducts[i] != null && CmStr(cmProducts[i], "productKey") == productKey) return i;
            throw new InvalidOperationException("Product not in the ledger: " + productKey);
        }
        static int CmStockB(string k) { return Get<int[]>(cmLedger, "_stockBought")[CmIdx(k)]; }
        static int CmStockE(string k) { return Get<int[]>(cmLedger, "_stockEaten")[CmIdx(k)]; }
        static int CmStock(string k) { return Math.Max(0, CmStockB(k) - CmStockE(k)); }
        static bool CmSeeded(string k) { return Get<bool[]>(cmLedger, "_stockSeeded")[CmIdx(k)]; }
        static bool CmOwned(string k) { int i = CmIdx(k); return Get<bool[]>(cmLedger, "_owned")[i] && Get<bool[]>(cmLedger, "_ownValid")[i]; }

        /// <summary>滞在の加算を止める / 戻す (模擬: 台帳のヒープ minuteSeconds)。止めている間の分は戻したときにまとめて付く (台帳の _Tick の仕様どおり)。</summary>
        static void CmFreezeStay(bool on) { cmLedger.SetProgramVariable("minuteSeconds", on ? 1000000f : cmMinuteSeconds); }

        // ---- PlayerData: ClientSim の API (メモリ上) と、保存先のファイル (= 保存された値) ----
        static bool PdHas(string key) { return VRC.SDK3.Persistence.PlayerData.HasKey(Networking.LocalPlayer, key); }
        static bool PdBool(string key) { bool v; return VRC.SDK3.Persistence.PlayerData.TryGetBool(Networking.LocalPlayer, key, out v) && v; }
        static string CmStorageFile() { return Path.Combine(Path.GetFullPath("ClientSimStorage/PlayerData"), "PlayerData_" + CmMe() + "_" + SceneManager.GetActiveScene().name + ".json"); }

        /// <summary>ClientSim の PlayerData ファイルを key → 値の文字列へ (bool は true/false、int は数字、string は中身)。</summary>
        static Dictionary<string, string> CmFileValues(string path)
        {
            var d = new Dictionary<string, string>();
            if (!File.Exists(path)) return d;
            string text = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(text, "\"Key\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*,\\s*\"Value\"\\s*:\\s*\\{\\s*\"type\"\\s*:\\s*\"(\\w+)\"\\s*,\\s*\"value\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|[^,\\s}]+)"))
                d[m.Groups[1].Value] = m.Groups[3].Value.Trim('"');
            return d;
        }
        static Dictionary<string, string> CmFile() { return CmFileValues(CmStorageFile()); }
        static int CmFileInt(Dictionary<string, string> d, string key) { string v; int n; return d.TryGetValue(key, out v) && int.TryParse(v, out n) ? n : -1; }
        static bool CmFileTrue(Dictionary<string, string> d, string key) { string v; return d.TryGetValue(key, out v) && v == "true"; }

        /// <summary>ファイルの Earned / Spent がヒープと一致 (LateUpdate で書かれるので Until で待つ)。</summary>
        static bool CmFileTotalsMatch()
        {
            var f = CmFile();
            return CmFileIntOr0(f, CmKey("Earned", "")) == CmEarned() && CmFileIntOr0(f, CmKey("Spent", "")) == CmSpent();
        }
        /// <summary>キーが無ければ 0 (台帳は未保存の合計を 0 として読む)。</summary>
        static int CmFileIntOr0(Dictionary<string, string> d, string key) { return d.ContainsKey(key) ? CmFileInt(d, key) : 0; }
        static string CmFileSummary()
        {
            var f = CmFile();
            return "file Earned=" + CmFileInt(f, CmKey("Earned", "")) + " Spent=" + CmFileInt(f, CmKey("Spent", "")) + " / heap earned=" + CmEarned() + " spent=" + CmSpent();
        }

        // ============================================================
        // 場所・手元メニュー・犬
        // ============================================================

        static Vector3 CmGround(Vector3 p, float radius = 3f)
        {
            UnityEngine.AI.NavMeshHit hit;
            if (!UnityEngine.AI.NavMesh.SamplePosition(p, out hit, radius, UnityEngine.AI.NavMesh.AllAreas)) throw new InvalidOperationException("No NavMesh near " + p);
            return hit.position;
        }
        static void CmTeleport(Vector3 p, Vector3 face)
        {
            face.y = 0;
            Networking.LocalPlayer.TeleportTo(p, face.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(face) : Networking.LocalPlayer.GetRotation());
        }
        /// <summary>門の道 (周回のノード 0) に立つ。平らで川から遠い (ディスクの取り出しの門が通る)。</summary>
        static void CmToGate() { CmTeleport(nodes[0], nodes[1] - nodes[0]); }
        /// <summary>おじいさんの前 1.6 m (Interact の距離・4 m で閉じる範囲の内)。</summary>
        static void CmToKeeper()
        {
            var t = cmKeeper.transform; var fwd = t.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            var p = CmGround(t.position + fwd.normalized * 1.6f);
            CmTeleport(p, t.position - p);
        }

        static bool CmPanelOpen() { var g = CmGo(pouch, "panelRoot"); return g != null && g.activeSelf; }
        /// <summary>手元メニューを開いて (ようこそが出ていれば通常の面へ) タブを選ぶ。本物の _Toggle / _WelcomeMenu / _Tab*。</summary>
        static void CmMenu(string tabEvent)
        {
            if (!CmPanelOpen()) pouch.SendCustomEvent("_Toggle");
            if (Get<bool>(pouch, "_welcome")) pouch.SendCustomEvent("_WelcomeMenu");
            if (tabEvent != null) pouch.SendCustomEvent(tabEvent);
        }
        static int CmConfirmKind() { return Get<int>(pouch, "_confirmKind"); }
        static bool CmConfirmYesShown() { var g = CmGo(pouch, "confirmYes"); return g != null && g.activeSelf; }
        static string CmFeedback() { return CmStr(pouch, "_feedbackText") ?? ""; }

        /// <summary>おやつの行 (pouch.items の添字) を商品キーから引く。</summary>
        static int CmTreatRow(string productKey)
        {
            var items = CmRefs(pouch, "items");
            for (int i = 0; i < items.Length; i++) if (items[i] != null && CmStr(items[i], "productKey") == productKey) return i;
            throw new InvalidOperationException("Treat row not found: " + productKey);
        }
        static int CmState() { return Get<int>(pouch, "_state"); }
        static int CmReason() { return Get<int>(pouch, "_reason"); }
        static int CmGiver(int slot) { return Get<int>(cmSlots[slot], "giverId"); }
        static int CmOutMs(int slot) { return Get<int>(cmSlots[slot], "outMs"); }
        static UdonBehaviour CmBody(int slot) { return Get<UdonBehaviour>(cmSlots[slot], "body"); }

        static bool CmMine(int dog) { return Get<int[]>(manager, "walkers")[dog] == CmMe(); }
        static bool CmNoneMine() { return Get<int[]>(manager, "walkers").All(w => w != CmMe()); }
        static void CmDetachAll() { for (int i = 0; i < 6; i++) if (CmMine(i)) pouch.SendCustomEvent("_WalkOff" + i); }
        /// <summary>犬を足元へ置いてから手元メニューでつなぐ (置くのは接続の直前だけ = 他の歩行ハーネスと同じ注入)。</summary>
        static void CmAttach(int dog, int hand)
        {
            holder = Networking.LocalPlayer.GetPosition();
            PlaceDog(dog, Networking.LocalPlayer.GetRotation());
            pouch.SendCustomEvent((hand == 0 ? "_WalkLeft" : "_WalkRight") + dog);
        }

        // ---- 人形 (Udon の無い遠隔プレイヤー。T-009 の「枠を他人が使っている」の相手) ----
        static void CmSpawnPuppets(int n) { for (int i = 0; i < n; i++) ClientSimMain.SpawnRemotePlayer("KH_TreatNeighbor" + i); }
        static bool CmCollectPuppets(int n)
        {
            var all = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]; VRCPlayerApi.GetPlayers(all);
            cmPuppets = all.Where(p => p != null && !p.isLocal).ToList();
            return cmPuppets.Count >= n;
        }
        static void CmReleasePuppetSlots()
        {
            if (cmSlots == null || cmPuppets.Count == 0) return;
            for (int i = 0; i < cmSlots.Length; i++) if (cmPuppets.Any(p => Utilities.IsValid(p) && p.playerId == CmGiver(i))) cmSlots[i].SendCustomEvent("_Release");
        }
        static void CmRemovePuppets()
        {
            foreach (var p in cmPuppets) if (Utilities.IsValid(p)) ClientSimMain.RemovePlayer(p);
            cmPuppets.Clear();
        }
    }
}
