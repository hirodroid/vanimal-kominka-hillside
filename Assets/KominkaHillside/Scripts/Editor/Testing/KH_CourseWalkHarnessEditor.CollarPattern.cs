using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-050 首輪とリードのおそろいセットの ClientSim ハーネス — Course/129 (群 collar-pattern、2 ケース)。
    /// 枠は周回コースの歩行ハーネスと同じ (Begin / Course/41 Stop / 締切 / PlayerData の退避と復旧 / シーンは保存しない /
    /// 結果は Logs/kh-course-walk/&lt;runId&gt;/report.json、ケースの記録は collar-pattern.json)。PlayerData は空から (初めて来た人、0 pt。Course/126 と同じ準備)。
    /// 既存の Course/83b (首輪の「外す」Interact、KH_CourseWalkHarnessEditor.Collar.cs、群 collar) とは別物。
    ///
    ///   collar-restore-delayed-35s     前提: おじいさんの 200 pt (本物の Interact) → 手元メニューで しま を買う (_Collar1 → _ConfirmYes。買うとリードの柄に選ばれ Sel.collar に保存)。
    ///                                  模擬 (遅い復元): 台帳のヒープ _loaded=false、カタログの _resolved / _loaded / collar の _selected / _userChose を戻し、
    ///                                  KH_CollarLeash の _restoreSeen / _local / _published と自分の KH_CollarTag._selected を空へ戻す → カタログの _ResolveRestored。
    ///                                  35 s 待つ間: カタログ未解決・リードは何も採らない (_restoreSeen false、_local / 札は空)。
    ///                                  台帳 _loaded=true → 3 s 以内に カタログが解決し、KH_CollarLeash が しま を採り (_restoreSeen / _local)、札が しま。
    ///                                  手元メニュー (かざり) の しま の行の副文が「リードに使用中」(collar.onLeash) = 三者一致。
    ///                                  犬 0 を繋ぐ → 縄・スナップ・持ち手の _MainTex が しま のテクスチャ。
    ///                                  (ClientSim では PlayerData の復元自体は遅らせられないので、Course/96 の L2 と同じく台帳のヒープで遅らせる。
    ///                                  旧コードの 30 s の打ち切りは Start からの連鎖だったので、ここでは「35 s 後の解決を _Tick が拾う」ことを見る)
    ///   collar-select-basic-roundtrip  前提: しま を持っている (無ければ上と同じ手順で買う)。犬 0 を繋ぐ。手元メニューで しま の面 (_Collar1) の「自分のリード」(_Acc0) →
    ///                                  縄・スナップ・持ち手が しま・札が しま・Sel.collar が しま。戻る (_AccBack) → 基本の面 (_Collar0) → _Acc0 →
    ///                                  カタログの選択・KH_CollarLeash._local・札・Sel.collar が collar.basic、3 つの描画先の _MainTex が基本 (KH_Leash_BaseColor)、_applied[0] = 0。
    /// ハーネス対象外 (2 人 / PCVR / 実機): 他人のリードの柄 (札の受信と OwnedBy)、遠いと LineRenderer になって柄が消える往復、犬の首輪の 60 秒と Owner の検証、
    /// 途中参加、本物の再入室 (VRChat のサーバー保存と OnPlayerRestored の遅れ)、見た目。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        [MenuItem("Tools/KominkaHillside/Course/129 - Walk Harness Collar Pattern (IMP-DOG-050)")]
        public static void CollarPatternHarness()
        {
            Begin(false);
            report.collarPatternOnly = true;
            report.deadline = EditorApplication.timeSinceStartup + 360;
            report.limitations = "Single local ClientSim; the local player is instance master and every Owner. Menu operations go through the real Udon events (_Tab5, _Collar*, _Acc0, _AccBack, _ConfirmYes, _WalkLeft0/_WalkOff0) "
                + "and the keeper grant is the real Interact. The delayed PlayerData restore is SIMULATED by heap writes (ledger _loaded, catalog _resolved/_loaded/_selected/_userChose, KH_CollarLeash and KH_CollarTag back to empty). "
                + "Not covered: another player's leash pattern, the far LineRenderer round trip, dog collars (60 s / Owner checks), late join, real re-entry, devices.";
            try { TrPrepareStorage(); }
            catch (Exception e) { End("BLOCKED", "PlayerData preparation failed (nothing tested): " + e.Message); return; }
            Save();
        }

        [Serializable] class CollarPatternCase { public string name, status = "PENDING"; public bool simulated; public List<string> notes = new List<string>(); }
        [Serializable] class CollarPatternReport { public string runId; public List<CollarPatternCase> cases = new List<CollarPatternCase>(); public List<string> notCovered = new List<string>(); }

        const string CpStripe = "collar.stripe", CpBasic = "collar.basic", CpCategory = "collar";
        static List<List<Func<double, bool>>> cpScripts;
        static List<double> cpLimits;
        static CollarPatternReport cpReport;
        static int cpStep;
        static double cpStepAt, cpCaseAt;
        static bool cpFailed, cpGrantNeeded, cpBuyNeeded;
        static float cpMinuteSeconds = 60f;
        static UdonBehaviour cpCatalog, cpLedger, cpKeeper, cpLeash, cpPattern;
        static string[] cpOnLeashTexts, cpOwnedTexts;

        // ============================================================
        // 組み立て
        // ============================================================

        static void MakeCollarPatternCases()
        {
            cpScripts = new List<List<Func<double, bool>>>(); cpLimits = new List<double>();
            cpStep = 0; cpCaseAt = -1; cpFailed = false;
            cpReport = new CollarPatternReport { runId = report.runId };
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true);
            cpCatalog = CpOne(all, "KD_ShopCatalog"); cpLedger = CpOne(all, "KD_ProgressLedger"); cpKeeper = CpOne(all, "KD_ShopKeeper");
            cpLeash = CpOne(all, "KH_CollarLeash"); cpPattern = CpOne(all, "KH_CollarPattern");
            cpMinuteSeconds = Get<float>(cpLedger, "minuteSeconds");
            var cat = KominkaDogRun.Editor.KD_LocalizationEditor.Load();
            cpOnLeashTexts = CpTexts(cat, "collar.onLeash"); cpOwnedTexts = CpTexts(cat, "accessory.owned");
            cpReport.notCovered.Add("Another player's leash pattern (KH_CollarTag received + KD_ShopItem.OwnedBy on the holder) -> 2 players");
            cpReport.notCovered.Add("A remote rope beyond ropeStraightDistance is drawn by the LineRenderer without the pattern, and comes back near -> 2 players (open question in IMP-DOG-050)");
            cpReport.notCovered.Add("Dog collars: the Owner's range / 60 s / ownership checks, late join, the changer leaving -> 2 players");
            cpReport.notCovered.Add("Real re-entry: VRChat server persistence and the real OnPlayerRestored delay (the delay here is simulated on the heap) -> PCVR");
            MakeCollarRestoreDelayedCase();
            MakeCollarBasicRoundtripCase();
            CpWrite();
        }

        static void MakeCollarRestoreDelayedCase()
        {
            var s = CpCase("collar-restore-delayed-35s", true, 120);
            CpAddSetupSteps(s);
            CpAddOwnStripeSteps(s);
            s.Add(CpDo(() =>
            {
                CpExpect(CpSelected() == CpStripe && CpSavedSelection() == CpStripe, "precondition: stripe selected and saved (catalog " + CpSelected() + " / Sel.collar " + CpSavedSelection() + ")");
                // 模擬: 遅い復元。台帳を未復元へ、カタログを未解決へ、リードと札を入室直後へ戻す
                cpLedger.SetProgramVariable("_loaded", false);
                int c = CpCategoryIndex();
                var selected = Get<string[]>(cpCatalog, "_selected"); selected[c] = ""; cpCatalog.SetProgramVariable("_selected", selected);
                var chose = Get<bool[]>(cpCatalog, "_userChose"); chose[c] = false; cpCatalog.SetProgramVariable("_userChose", chose);
                cpCatalog.SetProgramVariable("_resolved", false); cpCatalog.SetProgramVariable("_loaded", false); cpCatalog.SetProgramVariable("_retryScheduled", false);
                cpLeash.SetProgramVariable("_restoreSeen", false); cpLeash.SetProgramVariable("_local", ""); cpLeash.SetProgramVariable("_published", "");
                var tag = CpLocalTag(); CpExpect(tag != null, "own KH_CollarTag (PlayerObject) found");
                tag.SetProgramVariable("_selected", "");
                cpCatalog.SendCustomEvent("_ResolveRestored");   // ledger not loaded -> the catalog keeps retrying every 0.5 s
                CpNote("simulated slow restore: ledger _loaded=false; catalog _resolved/_loaded=false, collar _selected='' / _userChose=false; KH_CollarLeash _restoreSeen=false, _local/_published=''; own tag _selected=''");
            }));
            s.Add(CpWait(35));
            s.Add(CpDo(() =>
            {
                CpExpect(!Get<bool>(cpCatalog, "_resolved"), "catalog still unresolved after 35 s");
                CpExpect(!Get<bool>(cpLeash, "_restoreSeen") && Get<string>(cpLeash, "_local") == "" && CpTagSelected() == "", "nothing adopted while unresolved (restoreSeen " + Get<bool>(cpLeash, "_restoreSeen") + ", local '" + Get<string>(cpLeash, "_local") + "', tag '" + CpTagSelected() + "')");
                cpLedger.SetProgramVariable("_loaded", true);
                CpNote("35 s with the ledger unrestored: catalog unresolved, KH_CollarLeash adopted nothing; then ledger _loaded=true");
            }));
            s.Add(CpUntil(() => Get<bool>(cpCatalog, "_resolved") && Get<bool>(cpLeash, "_restoreSeen") && Get<string>(cpLeash, "_local") == CpStripe && CpTagSelected() == CpStripe, 3,
                () => "after the late resolve: catalog resolved " + Get<bool>(cpCatalog, "_resolved") + " / selected " + CpSelected() + ", leash restoreSeen " + Get<bool>(cpLeash, "_restoreSeen") + " local '" + Get<string>(cpLeash, "_local") + "', tag '" + CpTagSelected() + "'"));
            s.Add(CpDo(() => { CpMenu(); if (Get<int>(pouch, "_collarPage") >= 0) pouch.SendCustomEvent("_AccBack"); }));
            s.Add(CpWait(1.2));   // the open panel redraws every 0.5 s
            s.Add(CpDo(() =>
            {
                string sub = CpRowSub(1);
                CpExpect(cpOnLeashTexts.Contains(sub), "menu: the stripe row says 'on my leash' (" + sub + ")");
                CpExpect(cpOwnedTexts.Contains(CpRowSub(0)), "menu: the basic row says 'owned', not 'on my leash' (" + CpRowSub(0) + ")");
                CpNote("late resolve adopted by _Tick: catalog = KH_CollarLeash._local = own tag = stripe; menu stripe row '" + sub + "'");
            }));
            CpAddAttachSteps(s);
            s.Add(CpUntil(() => CpLeashApplied(0) == 1 && CpRopeTextures(0).All(t => t == CpTexture(1)), 3,
                () => "dog 0 rope / snap / handle not stripe (applied " + CpLeashApplied(0) + ", textures " + string.Join(",", CpRopeTextures(0).Select(t => t == null ? "null" : t.name)) + ")"));
            s.Add(CpDo(() => { CpNote("dog 0 attached: rope / snap / handle _MainTex = " + CpTexture(1).name); pouch.SendCustomEvent("_WalkOff0"); }));
            s.Add(CpUntil(() => Get<int[]>(manager, "walkers")[0] < 0, 5, () => "detach dog 0"));
        }

        static void MakeCollarBasicRoundtripCase()
        {
            var s = CpCase("collar-select-basic-roundtrip", false, 90);
            CpAddSetupSteps(s);
            CpAddOwnStripeSteps(s);
            CpAddAttachSteps(s);
            // しま を選ぶ (手元メニュー: しま の面 → 自分のリード)
            s.Add(CpDo(() =>
            {
                CpMenu(); if (Get<int>(pouch, "_collarPage") >= 0) pouch.SendCustomEvent("_AccBack");
                pouch.SendCustomEvent("_Collar1");
                CpExpect(Get<int>(pouch, "_collarPage") == 1, "stripe page open (_collarPage " + Get<int>(pouch, "_collarPage") + ")");
                pouch.SendCustomEvent("_Acc0");
            }));
            s.Add(CpUntil(() => CpSelected() == CpStripe && Get<string>(cpLeash, "_local") == CpStripe && CpTagSelected() == CpStripe && CpLeashApplied(0) == 1
                    && CpRopeTextures(0).All(t => t == CpTexture(1)) && CpSavedSelection() == CpStripe, 3,
                () => "stripe: catalog " + CpSelected() + ", local " + Get<string>(cpLeash, "_local") + ", tag " + CpTagSelected() + ", applied " + CpLeashApplied(0) + ", saved " + CpSavedSelection()));
            s.Add(CpDo(() =>
            {
                CpNote("stripe selected from the menu: catalog / leash / tag / Sel.collar stripe; rope / snap / handle = " + CpTexture(1).name);
                pouch.SendCustomEvent("_AccBack");
                CpExpect(Get<int>(pouch, "_collarPage") < 0, "back to the list");
                pouch.SendCustomEvent("_Collar0");
                CpExpect(Get<int>(pouch, "_collarPage") == 0, "basic page open (_collarPage " + Get<int>(pouch, "_collarPage") + ")");
                pouch.SendCustomEvent("_Acc0");
            }));
            s.Add(CpUntil(() => CpSelected() == CpBasic && Get<string>(cpLeash, "_local") == CpBasic && CpTagSelected() == CpBasic && CpLeashApplied(0) == 0
                    && CpRopeTextures(0).All(t => t == CpTexture(0)) && CpSavedSelection() == CpBasic, 3,
                () => "basic: catalog " + CpSelected() + ", local " + Get<string>(cpLeash, "_local") + ", tag " + CpTagSelected() + ", applied " + CpLeashApplied(0)
                    + ", textures " + string.Join(",", CpRopeTextures(0).Select(t => t == null ? "null" : t.name)) + ", saved " + CpSavedSelection()));
            s.Add(CpDo(() =>
            {
                CpNote("basic selected: catalog / leash / tag / Sel.collar collar.basic; rope / snap / handle _MainTex = " + CpTexture(0).name + " (written explicitly), applied[0] = 0");
                pouch.SendCustomEvent("_AccBack");
                pouch.SendCustomEvent("_WalkOff0");
            }));
            s.Add(CpUntil(() => Get<int[]>(manager, "walkers")[0] < 0, 5, () => "detach dog 0"));
        }

        // ---- 前提の部品 ----
        static void CpAddSetupSteps(List<Func<double, bool>> s)
        {
            s.Add(CpUntil(() => Get<bool>(cpLedger, "_loaded"), 25, () => "ledger not restored (_loaded)"));
            s.Add(CpUntil(() => Get<bool>(cpCatalog, "_resolved") && Get<bool>(cpLeash, "_restoreSeen"), 5, () => "catalog not resolved / KH_CollarLeash did not adopt the restore"));
            s.Add(CpDo(() => { for (int i = 0; i < 6; i++) if (Get<int[]>(manager, "walkers")[i] >= 0) pouch.SendCustomEvent("_WalkOff" + i); }));
            s.Add(CpUntil(() => Get<int[]>(manager, "walkers").All(w => w < 0), 5, () => "detach before setup"));
            s.Add(CpDo(() => { cpLedger.SetProgramVariable("minuteSeconds", 1000000f); CpToGate(); }));   // 模擬: 滞在の加算を止める (残高を固定)
        }

        /// <summary>前提: しま を持っている。まだなら おじいさんの 200 pt (本物の Interact) → 手元メニューで買う (_Collar1 → カード → _ConfirmYes)。</summary>
        static void CpAddOwnStripeSteps(List<Func<double, bool>> s)
        {
            s.Add(CpDo(() => { cpBuyNeeded = !CpOwned(CpStripe); cpGrantNeeded = cpBuyNeeded && Get<int>(cpLedger, "_earned") < 200; if (cpGrantNeeded) CpToKeeper(); }));
            s.Add(now => !cpGrantNeeded || now - cpStepAt >= 1);
            s.Add(CpDo(() => { if (cpGrantNeeded) cpKeeper.Interact(); }));
            s.Add(CpUntil(() => !cpGrantNeeded || Get<int>(cpLedger, "_earned") >= 200, 3, () => "keeper 200 pt (earned " + Get<int>(cpLedger, "_earned") + ")"));
            s.Add(CpDo(() =>
            {
                if (cpGrantNeeded) { cpKeeper.SendCustomEvent("_Close"); CpToGate(); }
                if (!cpBuyNeeded) return;
                CpMenu(); if (Get<int>(pouch, "_collarPage") >= 0) pouch.SendCustomEvent("_AccBack");
                pouch.SendCustomEvent("_Collar1");
                CpExpect(Get<int>(pouch, "_confirmKind") == KominkaDogRun.KD_TreatPouch.ConfirmCollar && CpGoActive("confirmYes"), "purchase card (kind ConfirmCollar) with [購入]");
                pouch.SendCustomEvent("_ConfirmYes");
            }));
            s.Add(CpUntil(() => CpOwned(CpStripe), 3, () => "stripe not owned after the purchase"));
            s.Add(CpDo(() => { if (cpBuyNeeded) CpNote("precondition: keeper grant + bought " + CpStripe + " through the menu (30 pt)"); }));
        }

        static void CpAddAttachSteps(List<Func<double, bool>> s)
        {
            s.Add(CpDo(() => { holder = nodes[0]; CpToGate(); }));
            s.Add(CpWait(.5));
            s.Add(CpDo(() => { PlaceDog(0, Networking.LocalPlayer.GetRotation()); pouch.SendCustomEvent("_WalkLeft0"); }));   // same as Course/83b KAttach
            s.Add(CpUntil(() => Get<int[]>(manager, "walkers")[0] == Networking.LocalPlayer.playerId, 5, () => "attach dog 0 (_WalkLeft0)"));
        }

        // ============================================================
        // 実行
        // ============================================================

        static void CollarPatternTick(double now)
        {
            if (cpScripts == null || caseIndex < 0 || caseIndex >= cpScripts.Count) throw new InvalidOperationException("Collar pattern script lost (domain reload?)");
            if (cpStep == 0 && cpCaseAt != Current.startedAt) { cpCaseAt = Current.startedAt; cpStepAt = now; }
            try
            {
                if (now - Current.startedAt > cpLimits[caseIndex]) throw new TimeoutException("Case deadline (" + cpLimits[caseIndex] + " s)");
                var steps = cpScripts[caseIndex];
                while (cpStep < steps.Count)
                {
                    if (!steps[cpStep](now)) return;
                    cpStep++; cpStepAt = now;
                }
                Current.status = "PASS"; Current.endedAt = now; Current.reason = string.Join(" | ", cpReport.cases[caseIndex].notes);
                cpReport.cases[caseIndex].status = "PASS";
            }
            catch (Exception e)
            {
                cpFailed = true;
                Current.status = "FAIL"; Current.endedAt = now; Current.reason = "step " + cpStep + ": " + e.GetType().Name + ": " + e.Message;
                cpReport.cases[caseIndex].status = "FAIL";
                cpReport.cases[caseIndex].notes.Add("FAIL at step " + cpStep + ": " + e.Message);
                cpReport.cases[caseIndex].notes.Add("at: " + TvWhere(e));
                CpRecover();
            }
            cpStep = 0;
            CpWrite(); Save();
            if (caseIndex + 1 < report.cases.Count) { Next(); return; }
            try { cpLedger.SetProgramVariable("minuteSeconds", cpMinuteSeconds); } catch (Exception) { }
            End(cpFailed ? "COLLAR_PATTERN_FAIL" : "COLLAR_PATTERN_PASS", cpFailed ? "One or more cases failed; see collar-pattern.json"
                : "Local ClientSim collar & leash set cases passed (delayed restore adopted, select stripe -> basic round trip). Not other players, the far LineRenderer, dog collars, late join, real re-entry or devices.");
        }

        static void CpRecover()
        {
            try { cpLedger.SetProgramVariable("_loaded", true); } catch (Exception) { }
            try { if (Get<int>(pouch, "_confirmKind") != 0) pouch.SendCustomEvent("_ConfirmNo"); } catch (Exception) { }
            try { if (Get<int>(pouch, "_collarPage") >= 0) pouch.SendCustomEvent("_AccBack"); } catch (Exception) { }
            try { cpKeeper.SendCustomEvent("_Close"); } catch (Exception) { }
            try { pouch.SendCustomEvent("_WalkOff0"); } catch (Exception) { }
        }

        static void CpWrite()
        {
            if (report == null || cpReport == null) return;
            Directory.CreateDirectory(Dir + "/" + report.runId);
            File.WriteAllText(Dir + "/" + report.runId + "/collar-pattern.json", JsonUtility.ToJson(cpReport, true));
        }

        // ---- 手順の部品 ----
        static List<Func<double, bool>> CpCase(string name, bool simulated, double limit)
        {
            report.cases.Add(new Case { name = name });
            cpReport.cases.Add(new CollarPatternCase { name = name, simulated = simulated });
            cpLimits.Add(limit);
            var s = new List<Func<double, bool>>(); cpScripts.Add(s); return s;
        }
        static Func<double, bool> CpDo(Action a) { return now => { a(); return true; }; }
        static Func<double, bool> CpWait(double seconds) { return now => now - cpStepAt >= seconds; }
        static Func<double, bool> CpUntil(Func<bool> condition, double limit, Func<string> what)
        { return now => { if (condition()) return true; if (now - cpStepAt > limit) throw new TimeoutException(what() + " (" + limit + " s)"); return false; }; }
        static void CpExpect(bool ok, string what) { if (!ok) throw new InvalidOperationException("Expected: " + what); }
        static void CpNote(string text) { cpReport.cases[caseIndex].notes.Add(text); }

        // ---- 読み取り・操作 ----
        static UdonBehaviour CpOne(UdonBehaviour[] all, string program)
        {
            var found = all.Where(u => u.programSource != null && u.programSource.name == program).ToArray();
            if (found.Length != 1) throw new InvalidOperationException(program + " in scene: " + found.Length + " (expected 1; run Collar/1 for the KH_Collar* objects)");
            return found[0];
        }
        static string[] CpTexts(KominkaDogRun.Editor.KD_LocalizationEditor.Catalog c, string key)
        {
            return c.languages.Select(l => l.texts.FirstOrDefault(x => x.key == key)).Where(x => x != null && !string.IsNullOrEmpty(x.text)).Select(x => x.text).ToArray();
        }
        static int CpCategoryIndex()
        {
            int c = Array.IndexOf(Get<string[]>(cpCatalog, "categoryKeys"), CpCategory);
            if (c < 0) throw new InvalidOperationException("catalog has no category collar");
            return c;
        }
        static string CpSelected() { var sel = Get<string[]>(cpCatalog, "_selected"); int c = CpCategoryIndex(); return sel != null && c < sel.Length ? sel[c] ?? "" : ""; }
        /// <summary>PlayerData の Sel.collar (カタログが選択のたびに書く保存値)。無ければ ""。</summary>
        static string CpSavedSelection()
        {
            string key = Get<string>(cpLedger, "storageNamespace") + ".Sel." + CpCategory, value;
            return VRC.SDK3.Persistence.PlayerData.TryGetString(Networking.LocalPlayer, key, out value) ? value ?? "" : "";
        }
        static bool CpOwned(string key)
        {
            var products = ((Array)Get<object>(cpLedger, "products")).Cast<object>().Select(x => x as UdonBehaviour).ToArray();
            for (int i = 0; i < products.Length; i++)
                if (products[i] != null && Get<string>(products[i], "productKey") == key) return Get<bool[]>(cpLedger, "_owned")[i] && Get<bool[]>(cpLedger, "_ownValid")[i];
            throw new InvalidOperationException("Product not in the ledger: " + key);
        }
        static UdonBehaviour CpLocalTag()
        {
            var objs = Networking.GetPlayerObjects(Networking.LocalPlayer);
            if (objs == null) return null;
            foreach (var go in objs)
                if (go != null)
                    foreach (var u in go.GetComponents<UdonBehaviour>())
                        if (u.programSource != null && u.programSource.name == "KH_CollarTag") return u;
            return null;
        }
        static string CpTagSelected() { var t = CpLocalTag(); return t == null ? "<no tag>" : Get<string>(t, "_selected") ?? ""; }
        static int CpLeashApplied(int dog) { var a = Get<int[]>(cpLeash, "_applied"); return a != null && dog < a.Length ? a[dog] : -1; }
        static Texture CpTexture(int option) { var t = Get<Texture2D[]>(cpPattern, "textures"); return t != null && option < t.Length ? t[option] : null; }
        /// <summary>犬 dog の縄・スナップ・持ち手の slot 0 の MaterialPropertyBlock の _MainTex (未設定は null)。</summary>
        static Texture[] CpRopeTextures(int dog)
        {
            var result = new List<Texture>();
            foreach (var field in new[] { "ropeRenderers", "snapRenderers", "handleRenderers" })
            {
                var list = Get<Renderer[]>(cpLeash, field);
                var r = list != null && dog < list.Length ? list[dog] : null;
                if (r == null) { result.Add(null); continue; }
                var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block, 0);
                result.Add(block.GetTexture("_MainTex"));
            }
            return result.ToArray();
        }
        static string CpRowSub(int row)
        {
            object o; if (!pouch.TryGetProgramVariable("collarSubLabels", out o) || !(o is Array a) || row >= a.Length) return "";
            var t = a.GetValue(row) as TMP_Text; return t == null ? "" : t.text ?? "";
        }
        static bool CpGoActive(string field) { object o; return pouch.TryGetProgramVariable(field, out o) && o is GameObject g && g != null && g.activeSelf; }
        /// <summary>手元メニューを開いて かざり (_Tab5) へ。</summary>
        static void CpMenu()
        {
            object o; var root = pouch.TryGetProgramVariable("panelRoot", out o) ? o as GameObject : null;
            if (root == null || !root.activeSelf) pouch.SendCustomEvent("_Toggle");
            if (Get<bool>(pouch, "_welcome")) pouch.SendCustomEvent("_WelcomeMenu");
            pouch.SendCustomEvent("_Tab5");
        }
        static void CpToGate()
        {
            var face = nodes[1] - nodes[0]; face.y = 0;
            Networking.LocalPlayer.TeleportTo(nodes[0], Quaternion.LookRotation(face));
        }
        static void CpToKeeper()
        {
            var t = cpKeeper.transform; var fwd = t.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            UnityEngine.AI.NavMeshHit hit;
            var want = t.position + fwd.normalized * 1.6f;
            var p = UnityEngine.AI.NavMesh.SamplePosition(want, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas) ? hit.position : want;
            var face = t.position - p; face.y = 0;
            Networking.LocalPlayer.TeleportTo(p, face.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(face) : Networking.LocalPlayer.GetRotation());
        }
    }
}
