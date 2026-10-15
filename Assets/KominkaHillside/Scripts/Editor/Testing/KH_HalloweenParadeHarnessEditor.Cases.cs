using System;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    public static partial class KH_HalloweenParadeHarnessEditor
    {
        static T Get<T>(UdonBehaviour u, string symbol)
        {
            if (u == null || !u.TryGetProgramVariable(symbol, out object value) || !(value is T))
                throw new InvalidOperationException("Unreadable VM heap: " + (u != null ? u.name : "null") + "." + symbol);
            return (T)value;
        }
        // Public getter/method executes in the backing Udon VM. No typed UdonSharp proxy invocation.
        static T Vm<T>(UdonBehaviour u, string entry)
        {
            var program = u.programSource.SerializedProgramAsset.RetrieveProgram();
            Setup(program != null && program.EntryPoints.TryGetAddressFromSymbol(entry, out _), "Missing exported VM entry " + entry);
            var returns = program.SymbolTable.GetSymbols().Where(s => s.EndsWith("_" + entry + "__ret", StringComparison.Ordinal)).ToArray();
            Setup(returns.Length == 1, "Ambiguous VM return for " + entry + ": " + string.Join(",", returns));
            u.SendCustomEvent(entry);
            return Get<T>(u, returns[0]);
        }
        static void Resolve()
        {
            var all = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true);
            var matches = all.Where(u => u.programSource != null && u.programSource.name == "KH_HalloweenParade").ToArray();
            Setup(matches.Length == 1, "Expected one backing KH_HalloweenParade, found " + matches.Length);
            manager = matches[0]; Setup(manager.enabled && Networking.IsOwner(manager.gameObject), "Manager not enabled/local authority");
            dogs = Get<Array>(manager, "dogs").Cast<object>().Select(o => o as UdonBehaviour).ToArray();
            Setup(dogs.Length == 6 && dogs.All(d => d != null && d.enabled), "Expected six enabled adapter backings");
            wanders = dogs.Select(d => Get<UdonBehaviour>(d, "wander")).ToArray();
            washes = dogs.Select(d => Get<UdonBehaviour>(d, "wash")).ToArray();
            pets = wanders.Select(w => Get<UdonBehaviour>(w, "petController")).ToArray();
            Setup(Get<int>(manager, "phase") == 0 && Get<bool>(manager, "developmentEnabled")==report.originalDevelopment, "Expected idle VM with original preview gate");
            Setup(Mathf.Abs(Get<float>(manager, "preparationSeconds") - 15f) < .01f && Mathf.Abs(Get<float>(manager, "recoverySeconds") - 30f) < .01f,
                "Harness requires production preparation 15s / recovery 30s; it never shortens these clocks");
            Setup(Vm<bool>(manager, "ConfigurationReady"), "VM ConfigurationReady rejected setup");
            for (int i = 0; i < 6; i++)
            {
                Setup(Get<int>(dogs[i], "dogIndex") == i && Vm<bool>(dogs[i], "_ReadyForRequest"), "Dog " + i + " not ready");
                Setup(!Get<bool>(pets[i], "_suppressed"), "Dog " + i + " initially has another pet suppression; idle-case precondition missing");
            }
            Note("Resolved one manager and six dogs; real getter/return VM symbols and private pet heap are readable. No ownership changed.");
            Setup(Vm<bool>(manager, "_LocalCanStart"), "Paid ClientSim fixture must own the dedicated CE product; no free hosting bypass");
        }
        static int Phase() { return Get<int>(manager, "phase"); }
        static void BeginCase(bool failure)
        {
            Setup(Phase() == 0, "Case must start Idle");
            int previous = Get<int>(manager, "requestId");
            manager.SendCustomEvent("_RequestStart");
            int id = Get<int>(manager, "requestId");
            Setup(id != previous && Phase() == 1, "Real _RequestStart was not accepted; result=" + Get<int>(manager, "resultCode"));
            Setup(Get<int>(manager, "userCount") == 0, "Idle-dog harness does not simulate active users or auto-approve consent");
            caseAt = Time.time; lastPhase = -1;
            if (failure) report.failureRequest = id; else report.normalRequest = id;
            Note((failure ? "F1 missing ACK" : "N1 normal/cancel") + " request=" + id);
        }
        static void ObservePhase()
        {
            int phase = Phase(); if (phase == lastPhase) return; lastPhase = phase;
            Note("phase=" + phase + " request=" + Get<int>(manager, "requestId") + " prepared=" + Get<int>(manager, "preparedMask")
                + " ended=" + Get<int>(manager, "endedMask") + " placed=" + Get<int>(manager, "placedMask") + " restored=" + Get<int>(manager, "restoredMask"));
        }
        static void Advance()
        {
            if(report.nightCheck){AdvanceNight();return;}
            if(report.danceCapture){AdvanceDance();return;}
            if (stage == 0)
            {
                if (Time.time - playAt > 20f) throw new InvalidOperationException("Local player/VM startup not ready within 20s");
                if (Time.time - playAt < 3f || !Utilities.IsValid(Networking.LocalPlayer)) return;
                Resolve(); BeginCase(false); ToStage(1); return;
            }
            ObservePhase();
            int phase = Phase();
            Need(Get<int>(manager, "requestId") == (stage <= 3 ? report.normalRequest : report.failureRequest), "Unexpected request replacement");
            if (stage <= 3 && Time.time - caseAt > 15f) throw new Exception("Normal prepare/cancel/recovery exceeded 15s");
            if (stage >= 4 && Time.time - caseAt > 55f) throw new Exception("Missing-ACK case exceeded 55s including terminal stability observation");
            if (stage == 1)
            {
                Need(phase != 0 && phase != 6 && phase != 7, "Normal preparation failed; result=" + Get<int>(manager, "resultCode"));
                if (phase != 5) return;
                Need(Get<int>(manager, "endedMask") == 63 && Get<int>(manager, "placedMask") == 63, "Playing without all actual end/place ACKs");
                for (int i = 0; i < 6; i++) Need(Get<int>(dogs[i], "endedRequest") == report.normalRequest && Get<int>(dogs[i], "placedRequest") == report.normalRequest,
                    "Dog receipt does not match normal request " + i);
                if (Vm<float>(manager, "get_Elapsed") < .25f) return;
                for (int i = 0; i < 6; i++) Need(Get<bool>(pets[i], "_suppressed"), "Playing did not suppress petting on dog " + i);
                manager.SendCustomEvent("_Cancel"); Need(Phase() == 6, "VM Cancel did not enter Restore");
                Note("All six actual ACK/receipts; Playing clock >=0.25s; real VM Cancel entered Restore"); ToStage(2); return;
            }
            if (stage == 2)
            {
                if (phase != 0) { Need(phase == 6, "Unexpected normal recovery phase " + phase); return; }
                Need(Get<int>(manager, "restoredMask") == 63 && Get<int>(manager, "resultCode") == 8 && Get<bool>(manager, "resultAfterCommit"), "Normal cancellation recovery evidence incomplete");
                ToStage(3); return;
            }
            if (stage == 3)
            {
                if (Time.time - stageAt < .3f) return;
                for (int i = 0; i < 6; i++) Need(!Vm<bool>(dogs[i], "get_BlocksNewUses") && !Get<bool>(pets[i], "_suppressed"), "Normal dog gate/petting did not resume " + i);
                report.normalPassed = true; Note("N1 PASS in " + (Time.time - caseAt).ToString("F2") + "s");
                BeginCase(true); ToStage(4); return;
            }
            if (stage == 4)
            {
                Setup(phase == 1 || phase == 2, "INVALID_SETUP: all-reserve-ACK / pre-EndUses injection window was not observed");
                if (phase != 2 || Get<int>(manager, "preparedMask") != 63) return;
                Setup(Get<int>(manager, "endedMask") == 0, "Cannot inject after any end ACK");
                faultOriginalEnabled = dogs[FaultDog].enabled; dogs[FaultDog].enabled = false; faultDisabled = true;
                report.faultWindowCaptured = true;
                Note("Injection: prepared=63 ended=0 phase=Reserve; disabled adapter backing only, dog=5. No synchronized fields/ACKs changed."); ToStage(5); return;
            }
            if (stage == 5)
            {
                Setup(phase == 2 || phase == 3 || phase == 6, "Fault injection invalidated request before intended end-wait");
                if (Get<int>(manager, "endedMask") != HealthyMask)
                {
                    Setup(phase != 6 && Time.time - stageAt < 10f, "Intended five-end / one-missing-ACK condition never formed"); return;
                }
                for (int i = 0; i < 5; i++) Need(Get<int>(dogs[i], "endedRequest") == report.failureRequest, "Healthy dog missing end receipt");
                Need(Get<int>(dogs[5], "endedRequest") != report.failureRequest && Get<int>(manager, "placedMask") == 0, "Disabled dog ended or placement began");
                Note("Five actual end ACKs, sixth absent, no placement. Waiting unchanged 15s preparation timeout."); ToStage(6); return;
            }
            if (stage == 6)
            {
                Need(phase == 3 || phase == 6, "Missing ACK must prevent show start");
                if (phase != 6) return;
                Need(!Get<bool>(manager, "showStarted"), "Failure case entered show"); ToStage(7); return;
            }
            if (stage == 7)
            {
                Need(phase == 6 || phase == 7, "Unexpected failure recovery phase");
                if (!report.petBeforePoseRecovered && Time.time - stageAt >= 1.5f)
                {
                    for (int i = 0; i < 5; i++) Need(!Get<bool>(wanders[i], "_paradePose") && !Get<bool>(wanders[i], "_paradePetSuppressed") && !Get<bool>(pets[i], "_suppressed"),
                        "D2 pet suppression leaked before first parade pose, dog=" + i);
                    report.petBeforePoseRecovered = true; Note("D2: five dogs never acquired parade pose; actual pet _suppressed and parade marker are false in Restore.");
                }
                if (phase != 7) return;
                report.failedMask = Get<int>(manager, "recoveryFailedMask");
                Need(report.failedMask == 32 && Get<int>(manager, "restoredMask") == HealthyMask && Get<int>(manager, "resultCode") == 11
                    && Get<bool>(manager, "resultAfterCommit") && !Vm<bool>(manager, "get_BlocksNewUses"), "RecoveryFailed did not isolate only the missing dog");
                terminalCounters = Counters();
                dogs[FaultDog].enabled = faultOriginalEnabled; faultDisabled = false;
                Note("RecoveryFailed mask=32; five restored, whole-world gate open. Disabled adapter re-enabled; observe 3s for stale cleanup/warp."); ToStage(8); return;
            }
            if (stage == 8)
            {
                Need(phase == 7 && terminalCounters == Counters(), "Terminal state replayed cleanup/placement receipts or counters");
                if (Time.time - stageAt < 3f) return;
                for (int i = 0; i < 6; i++)
                {
                    Need(Vm<bool>(dogs[i], "get_BlocksNewUses") == (i == FaultDog) && Vm<bool>(dogs[i], "get_Isolated") == (i == FaultDog), "VM per-dog admission mismatch " + i);
                    if (i != FaultDog) Need(!Get<bool>(pets[i], "_suppressed"), "Healthy pet suppression returned after terminal state");
                }
                manager.SendCustomEvent("_RequestStart"); Need(Phase() == 7 && Get<int>(manager, "requestId") == report.failureRequest, "Failed instance accepted a new parade");
                report.failurePassed = true; Note("F1 PASS: VM getters permit five healthy dogs / reject isolated dog; cleanup counters and receipts fixed across adapter re-enable; no new parade accepted.");
                End("PASS", "N1 normal/cancel and F1 missing-end-ACK recovery passed within bounded local VM scope");
            }
        }
        static string Counters()
        {
            return string.Join("|", Enumerable.Range(0, 6).Select(i => Get<int>(dogs[i], "cleanCount") + "," + Get<int>(dogs[i], "warpCount") + ","
                + Get<int>(dogs[i], "endedRequest") + "," + Get<int>(dogs[i], "placedRequest") + "," + Get<int>(washes[i], "paradeCleanRequest")));
        }
    }
}
