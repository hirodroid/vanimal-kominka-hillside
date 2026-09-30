using System;
using System.Linq;
using UnityEngine;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-028 洗われている犬の反応 (ブルブル・満足の合図・足上げ) — Course/100 (Wash.cs) に足すケース。
    ///
    /// 枠・手順の部品・観測は Wash.cs のもの (WsCase / WsDo / WsUntil / WsDrive / WsContact / WsPathHit / WsCover ...)。ケースの一覧への接続は
    /// Wash.cs の MakeWashCases で MakeWashBehaviorCases() の後、MakeWashSyncCases() の前に MakeWashReactCases() を呼ぶ
    /// (Sync のケースは乾きの値を変えるので最後のまま)。ここでは一覧へ自分でつながない。
    ///
    /// 読むもの: 犬 (KD_DogWanderController) の _washShakeCount / _washShakeKind / _washSatisfiedCount / _washPawCount / _washPawUntil、
    /// 撫での制御 (KD_DogPetController) の _shakeEnvelope (頭の先の合計の振幅) / _shakeBodyDeg (胴の根元へ書いた角度)、Animator の Sit。
    /// 濡れと泡はセル (_wet / _foam) を直接書いて作る場合がある (W-shake の対照)。清潔のエッジは Owner の cleanSerial を +1 して作る
    /// (IMP-DOG-026 の判定そのものは W-clean / W-reclean が見る)。
    ///
    /// 前提: KH_DogWash に _AverageWet() がある (濡れのきっかけが読む)。ClientSim は 1 人: 非 Owner の表示は対象外 (W-react-remote は SKIP)。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        static UdonBehaviour wrPet;
        static Animator wrAnim;

        static void MakeWashReactCases()
        {
            wrPet = WsRef(wsWander, "petController") as UdonBehaviour;
            wrAnim = WsRef(wsWander, "dogAnimator") as Animator;

            // ---- W-shake (濡れ + 最後のストロークから 0.8 s でブルブル、1.2 s で 0 へ。泡が多い間は出ない) ----
            var s = WsCase("W-shake", 100);
            WsAnchor skA = default; float skEnd = 0f, skStartAt = -1f, skMaxBody = 0f, skMaxEnv = 0f, skWet = 0f, skFoam = 0f, skShakeT0 = 0f, skBackAt = -1f;
            int skC0 = 0, skC1 = 0; float skNegFoam = 0f, skNegWet = 0f;
            s.Add(WsDo(() =>
            {
                WrRequire();
                WsExpect(WsArr<Transform>(wrPet, "shakeBones").Count(b => b != null) > 0, "the pet controller has shakeBones wired (Wash/11)");
                WsPlaceShiba(); WsPlayerNear(1.3f); wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsUntil(() => Time.time >= Get<float>(wsWander, "_washShakeWetNextAt") + 0.5f, 25, () => "the wet-shake spacing (washShakeWetSpacing) from an earlier case did not run out"));
            s.Add(WsWait(0.8));
            // 対照 (泡が多い): 泡 0.5・濡れ 0.8 をセルへ書き、短いストロークの後 2 s 何も起きない
            s.Add(WsDo(() => { WrSetCells(0.5f, 0.8f); skA = WsAnchorOf(WsPathHit(0.2f)); skC0 = WrCount("_washShakeCount"); }));
            s.Add(WsWait(0.7));   // EvaluateState (0.5 s) が平均を取り直す
            s.Add(WsDo(() => { skNegFoam = WsAvgFoam(); skNegWet = WrAvgWet(); WsExpect(skNegFoam > 0.15f, "foamy before the control stroke (avg foam " + skNegFoam.ToString("F3") + ")"); }));
            s.Add(WsDrive(0.3f, 1, u => WsAt(skA), WsSponge, -1f, null));
            s.Add(WsDo(() => { WsEnd(WsSponge); skEnd = Time.time; }));
            s.Add(now =>
            {
                if (WrCount("_washShakeCount") != skC0) throw new InvalidOperationException("shook while foamy (avg foam " + WsAvgFoam().ToString("F3") + " > 0.15): the rinse must stay the show");
                return Time.time - skEnd >= 2f;
            });
            // 本番: 泡なし、シャワー 2 s で濡らす
            s.Add(WsDo(() => wsWash.SendCustomEvent("_DebugClear")));
            s.Add(WsWait(0.8));
            // hip / chest only (u <= 0.7): the flank path's end can hit the right front leg and start a paw lift (run 20260930-062530)
            s.Add(WsDrive(2f, 1, u => WsPath(0.7f * u), WsShower, -1f, null));
            s.Add(WsDo(() => { WsEnd(WsShower); skEnd = Time.time; skC1 = WrCount("_washShakeCount"); }));
            s.Add(now =>
            {
                float t = Time.time - skEnd;
                if (skStartAt < 0f && WrCount("_washShakeCount") > skC1) { skStartAt = t; skShakeT0 = Time.time; skWet = WrAvgWet(); skFoam = WsAvgFoam(); }
                if (skStartAt >= 0f)
                {
                    skMaxBody = Mathf.Max(skMaxBody, Mathf.Abs(Get<float>(wrPet, "_shakeBodyDeg")));
                    skMaxEnv = Mathf.Max(skMaxEnv, Get<float>(wrPet, "_shakeEnvelope"));
                }
                if (skStartAt < 0f && t > 2f)
                    throw new InvalidOperationException("no shake 2 s after the shower ended (avg wet " + WrAvgWet().ToString("F3") + " vs the trigger washShakeWet " + Get<float>(wsWander, "washShakeWet") + ", avg foam " + WsAvgFoam().ToString("F3") + ", washActive " + Get<bool>(wsWander, "_washActive") + ", hold " + WsHold() + ")");
                return skStartAt >= 0f && Time.time - skShakeT0 >= 0.5f;
            });
            s.Add(now =>
            {
                float t = Time.time - skShakeT0;
                skMaxBody = Mathf.Max(skMaxBody, Mathf.Abs(Get<float>(wrPet, "_shakeBodyDeg")));
                bool back = Get<float>(wrPet, "_shakeEnvelope") == 0f && Get<float>(wrPet, "_shakeBodyDeg") == 0f;
                if (back) { skBackAt = t; return true; }
                if (t > 2.5f) throw new InvalidOperationException("the shake did not return to 0 within 2.5 s (envelope " + Get<float>(wrPet, "_shakeEnvelope").ToString("F2") + " deg)");
                return false;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(skStartAt >= 0.7f && skStartAt <= 1.1f, "the shake started 0.8 s after the last stroke (+" + skStartAt.ToString("F2") + " s)");
                WsExpect(Get<int>(wsWander, "_washShakeKind") == 1, "the wet trigger (kind " + Get<int>(wsWander, "_washShakeKind") + ")");
                WsExpect(WrCount("_washShakeCount") == skC1 + 1, "exactly one shake (" + skC1 + " -> " + WrCount("_washShakeCount") + ")");
                WsExpect(skMaxBody > 0.5f, "the body bone got an additive roll (max " + skMaxBody.ToString("F2") + " deg on shakeBones[0])");
                WsExpect(skBackAt >= 1.0f && skBackAt <= 1.6f, "back to 0 about 1.2 s after the start (" + skBackAt.ToString("F2") + " s)");
                WsNote("control: cells foam 0.5 / wet 0.8 (avg foam " + skNegFoam.ToString("F3") + ", wet " + skNegWet.ToString("F3") + ") + 0.3 s sponge -> no shake in 2 s. Shower 2 s along the flank -> shake at +" + skStartAt.ToString("F2")
                    + " s (avg wet " + skWet.ToString("F3") + ", foam " + skFoam.ToString("F3") + "), body roll max " + skMaxBody.ToString("F2") + " deg, envelope max " + skMaxEnv.ToString("F1") + " deg, back to 0 at " + skBackAt.ToString("F2") + " s");
            }));

            // ---- W-no-repeat (同じきっかけは 8 s あける。8 s 後は出る = 対照) ----
            s = WsCase("W-no-repeat", 110);
            float nrT1 = -1f, nrEnd2 = 0f, nrBlocked = 0f; int nrC = 0; float nrSecond = -1f;
            s.Add(WsDo(() => { WrRequire(); WsPlaceShiba(); WsPlayerNear(1.3f); wsWash.SendCustomEvent("_DebugClear"); }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsWait(0.8));
            // the wet-shake spacing (washShakeWetSpacing) left by an earlier case (W-shake / W-shake-remote) must run out first: wait for the dog's own next-at + 0.5 s
            s.Add(WsUntil(() => Time.time >= Get<float>(wsWander, "_washShakeWetNextAt") + 0.5f, 25,
                () => "the wet-shake spacing from an earlier case did not run out (next at " + Get<float>(wsWander, "_washShakeWetNextAt").ToString("F1") + ", now " + Time.time.ToString("F1") + ")"));
            // wet well above the trigger (cells written directly, foam 0; the runtime reads the 0.5 s average), then the stroke
            s.Add(WsDo(() => { WrSetCells(0f, 0.5f); nrC = WrCount("_washShakeCount"); }));
            s.Add(WsWait(0.7));
            s.Add(WsDrive(1.5f, 1, u => WsPath(0.7f * u), WsShower, -1f, null));
            s.Add(WsDo(() => WsEnd(WsShower)));
            s.Add(WsUntil(() => { if (WrCount("_washShakeCount") > nrC) { nrT1 = Time.time; return true; } return false; }, 2.5,
                () => "first shake did not come (avg wet " + WrAvgWet().ToString("F3") + ", foam " + WsAvgFoam().ToString("F3") + ")"));
            s.Add(WsWait(2.0));
            s.Add(WsDrive(1.5f, 1, u => WsPath(0.7f * u), WsShower, -1f, null));
            s.Add(WsDo(() => { WsEnd(WsShower); nrEnd2 = Time.time; nrBlocked = nrEnd2 - nrT1; nrC = WrCount("_washShakeCount"); }));
            s.Add(now =>
            {
                if (WrCount("_washShakeCount") != nrC) throw new InvalidOperationException("a second wet shake " + (Time.time - nrT1).ToString("F1") + " s after the first (washShakeWetSpacing " + Get<float>(wsWander, "washShakeWetSpacing") + " s)");
                return Time.time - nrEnd2 >= 2f;
            });
            s.Add(WsUntil(() => Time.time >= Get<float>(wsWander, "_washShakeWetNextAt") + 0.3f, 25, () => "waiting for the spacing (washShakeWetSpacing " + Get<float>(wsWander, "washShakeWetSpacing") + " s)"));
            s.Add(WsDo(() => WrSetCells(0f, 0.5f)));   // after the 20 s wait: back well above the trigger
            s.Add(WsWait(0.7));
            s.Add(WsDrive(1f, 1, u => WsPath(0.7f * u), WsShower, -1f, null));
            s.Add(WsDo(() => { WsEnd(WsShower); nrEnd2 = Time.time; }));
            s.Add(WsUntil(() => { if (WrCount("_washShakeCount") > nrC) { nrSecond = Time.time - nrT1; return true; } return false; }, 2.5,
                () => "control: no shake after the washShakeWetSpacing (avg wet " + WrAvgWet().ToString("F3") + ", foam " + WsAvgFoam().ToString("F3") + ")"));
            s.Add(WsDo(() => WsNote("wet shake 1; a second 1.5 s shower ending " + nrBlocked.ToString("F1") + " s after it gave none within 2 s (washShakeWetSpacing " + Get<float>(wsWander, "washShakeWetSpacing") + " s); after the spacing a 1 s shower gave the next shake at +" + nrSecond.ToString("F1") + " s from the first (control)")));

            // ---- W-satisfied (清潔のエッジで ブルブル → 座る 2.5 s + 尻尾 + 笑顔、1 回だけ。座っている間も洗える) ----
            s = WsCase("W-satisfied", 60);
            WsAnchor saA = default; float saEdge = 0f, saLast = -1f, saShake = -1f, saSitOn = -1f, saSitOff = -1f;
            int saS0 = 0, saNext = 0, saCount0 = 0, saShake0 = 0, saCells = 0; float[] saD = null, saF = null, saW = null; bool saSitAtEdge = false;
            s.Add(WsDo(() =>
            {
                WrRequire();
                if (wrAnim == null) throw new WsSkip("the Shiba's dogAnimator is not wired");
                WsPlaceShiba(); WsPlayerNear(1.3f); wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() => { saA = WsAnchorOf(WsPathHit(0.2f)); }));
            s.Add(WsDrive(0.4f, 1, u => WsAt(saA), WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsExpect(WsHold() && Get<bool>(wsWander, "_washActive"), "washed before the edge (hold " + WsHold() + ")");
                saS0 = Get<int>(wsWash, "cleanSerial"); saCount0 = WrCount("_washSatisfiedCount"); saShake0 = WrCount("_washShakeCount");
                saSitAtEdge = wrAnim.GetBool("Sit");
                saNext = WrSitSerial(saS0);
                wsWash.SetProgramVariable("cleanSerial", saNext);   // the Owner's clean edge (what EvaluateState does), a sitting one
                saEdge = Time.time; saLast = -1f;
            }));
            // 5 s のあいだ、腰を軽く洗い続けながら見る (座っている間もストロークがセルを変える)
            s.Add(now =>
            {
                float t = Time.time;
                if (saLast < 0f) saLast = t - Mathf.Max(Time.deltaTime, 1e-3f);
                float sec = Mathf.Min(t - saLast, 0.1f); saLast = t;
                if (sec > 1e-5f) { var h = WsAt(saA); WsContact(WsSponge, h.ci, h.p, sec, -1f); }
                float e = t - saEdge;
                if (saShake < 0f && WrCount("_washShakeCount") > saShake0) saShake = e;
                bool sit = wrAnim.GetBool("Sit");
                if (sit && saSitOn < 0f) { saSitOn = e; saD = (float[])Get<float[]>(wsWash, "_dirt").Clone(); saF = (float[])Get<float[]>(wsWash, "_foam").Clone(); saW = (float[])Get<float[]>(wsWash, "_wet").Clone(); }
                if (!sit && saSitOn >= 0f && saSitOff < 0f) { saSitOff = e; saCells = WsCellDiff(saD, saF, saW); }
                return e >= 5f;
            });
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(!saSitAtEdge, "not sitting before the edge");
                WsExpect(WrCount("_washSatisfiedCount") == saCount0 + 1, "the satisfied sign exactly once (" + saCount0 + " -> " + WrCount("_washSatisfiedCount") + ")");
                WsExpect(saShake >= 0f && saShake <= 0.3f && Get<int>(wsWander, "_washShakeKind") == 2, "the clean shake first (+" + saShake.ToString("F2") + " s, kind " + Get<int>(wsWander, "_washShakeKind") + ")");
                WsExpect(saSitOn >= 1.0f && saSitOn <= 1.6f, "Sit requested after the shake (+" + saSitOn.ToString("F2") + " s; Animator Sit)");
                WsExpect(saSitOff > 0f && saSitOff - saSitOn >= 2.1f && saSitOff - saSitOn <= 2.9f, "Sit released after 2.5 s (" + (saSitOff < 0f ? "never" : (saSitOff - saSitOn).ToString("F2") + " s") + ")");
                WsExpect(saCells > 0, "the strokes kept washing while sitting (" + saCells + " cells changed between sit on and off)");
                WsNote("cleanSerial " + saS0 + " -> " + saNext + " (a multiple of washSitEvery " + Get<int>(wsWander, "washSitEvery") + ") while stroked: shake +" + saShake.ToString("F2") + " s, Sit +" + saSitOn.ToString("F2") + " .. +" + saSitOff.ToString("F2") + " s, " + saCells + " cells changed by the sponge during the sit; satisfied count +1");
            }));

            // ---- W-satisfied-hold (Codex 4-1 / 4-3: 最後のストロークから 3 s で保持が切れても座りは 2.5 s 完走する。
            //      8 s 以内の次の清潔のエッジは、ブルブルは出さずに満足の合図 (座る) だけ出す) ----
            s = WsCase("W-satisfied-hold", 60);
            WsAnchor shA = default; float shEdge = 0f, shOn = -1f, shOff = -1f, shHoldEnd = -1f, shOn2 = -1f, shHappy = -1f;
            int shSat0 = 0, shShake0 = 0;
            s.Add(WsDo(() =>
            {
                WrRequire();
                if (wrAnim == null) throw new WsSkip("the Shiba's dogAnimator is not wired");
                WsPlaceShiba(); WsPlayerNear(1.3f); wsWash.SendCustomEvent("_DebugClear");
            }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsUntil(() => Time.time >= Get<float>(wsWander, "_washShakeCleanNextAt") + 0.5f, 10, () => "the clean-shake spacing from an earlier case did not run out"));
            s.Add(WsDo(() => { shA = WsAnchorOf(WsPathHit(0.2f)); }));
            s.Add(WsDrive(0.4f, 1, u => WsAt(shA), WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                shSat0 = WrCount("_washSatisfiedCount"); shShake0 = WrCount("_washShakeCount");
                wsWash.SetProgramVariable("cleanSerial", WrSitSerial(Get<int>(wsWash, "cleanSerial")));
                shEdge = Time.time;
            }));
            // no more strokes: the hold ends about 3 s after the sponge, the sit (+1.2 .. +3.7 s) must still finish
            s.Add(now =>
            {
                float e = Time.time - shEdge;
                bool sit = wrAnim.GetBool("Sit");
                if (sit && shOn < 0f) shOn = e;
                if (!sit && shOn >= 0f && shOff < 0f) shOff = e;
                if (shHoldEnd < 0f && !WsHold()) shHoldEnd = e;
                return e >= 5f;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(shHoldEnd > 0f && shHoldEnd < 3.5f, "the hold ended during the sequence (+" + shHoldEnd.ToString("F2") + " s)");
                WsExpect(shOn >= 1.0f && shOn <= 1.6f && shOff - shOn >= 2.1f && shOff - shOn <= 2.9f && shOff > shHoldEnd,
                    "the sit ran its 2.5 s past the hold (on +" + shOn.ToString("F2") + ", off " + (shOff < 0f ? "never" : "+" + shOff.ToString("F2")) + " s, hold ended +" + shHoldEnd.ToString("F2") + " s)");
                WsExpect(WrCount("_washShakeCount") == shShake0 + 1, "the clean shake once (" + shShake0 + " -> " + WrCount("_washShakeCount") + ")");
            }));
            // the next clean edge (+1: not a multiple of washSitEvery): the satisfied sign (tail / smile / voice) without sit or shake
            s.Add(WsDrive(0.4f, 1, u => WsAt(shA), WsSponge, -1f, null));
            s.Add(WsDo(() => { WsEnd(WsSponge); shShake0 = WrCount("_washShakeCount"); wsWash.SetProgramVariable("cleanSerial", Get<int>(wsWash, "cleanSerial") + 1); shEdge = Time.time; }));
            s.Add(now => { if (shOn2 < 0f && wrAnim.GetBool("Sit")) shOn2 = Time.time - shEdge; if (shHappy < 0f && Time.time < Get<float>(wsWander, "_washSatisfyUntil")) shHappy = Time.time - shEdge; return Time.time - shEdge >= 4f; });
            s.Add(WsDo(() =>
            {
                WsExpect(WrCount("_washSatisfiedCount") == shSat0 + 2, "the satisfied sign for both edges (" + shSat0 + " -> " + WrCount("_washSatisfiedCount") + ")");
                WsExpect(shHappy >= 0f && shHappy <= 0.3f, "the second edge started the satisfied window (+" + shHappy.ToString("F2") + " s)");
                WsExpect(shOn2 < 0f, "no sit on the non-sitting edge (Sit at +" + shOn2.ToString("F2") + " s)");
                WsExpect(WrCount("_washShakeCount") == shShake0, "no shake on the non-sitting edge");
                WsNote("edge 1 without further strokes: hold ended +" + shHoldEnd.ToString("F2") + " s, sit +" + shOn.ToString("F2") + " .. +" + shOff.ToString("F2") + " s, one shake; edge 2 (+1, not a multiple of washSitEvery): satisfied window from +" + shHappy.ToString("F2") + " s, no sit, no shake");
            }));

            // ---- W-satisfied-join (保持の無いところへ届いた cleanSerial = 途中参加の最初の受信: 何も出さない) ----
            s = WsCase("W-satisfied-join", 30);
            int sjCount0 = 0, sjShake0 = 0; float sjAt = 0f; bool sjSit = false;
            s.Add(WsDo(() => { WrRequire(); if (wrAnim == null) throw new WsSkip("the Shiba's dogAnimator is not wired"); WsPlayerNear(1.3f); }));
            s.Add(WsUntil(() => !WsHold(), wsHoldSeconds + 2, () => "the hold did not end"));
            // the previous case's satisfied sit (+1.2 .. +3.7 s, runs past the hold by design) and any shake must be over first
            s.Add(WsUntil(() => Time.time >= Get<float>(wsWander, "_washSatisfyUntil") && !wrAnim.GetBool("Sit") && Get<float>(wrPet, "_shakeEnvelope") == 0f, 6,
                () => "the previous satisfied sign / shake did not end"));
            s.Add(WsWait(0.3));
            // a joiner before its first receive: no state (_gotFirst false) for a few frames
            s.Add(WsDo(() => { sjCount0 = WrCount("_washSatisfiedCount"); sjShake0 = WrCount("_washShakeCount"); wsWash.SetProgramVariable("_gotFirst", false); }));
            s.Add(WsWait(0.2));
            s.Add(WsDo(() =>
            {
                // the first receive: the state, the serials and a fresh hold arrive together
                wsWash.SetProgramVariable("cleanSerial", Get<int>(wsWash, "cleanSerial") + 1);
                wsWash.SetProgramVariable("shakeSerial", Get<int>(wsWash, "shakeSerial") + 1);
                wsWash.SetProgramVariable("washMs", VRC.SDKBase.Networking.GetServerTimeInMilliseconds());
                wsWash.SetProgramVariable("washSeen", true);
                wsWash.SetProgramVariable("_gotFirst", true);
                sjAt = Time.time;
            }));
            s.Add(now => { sjSit |= wrAnim.GetBool("Sit"); return Time.time - sjAt >= 2.5f; });
            s.Add(WsDo(() =>
            {
                WsExpect(WrCount("_washSatisfiedCount") == sjCount0, "no satisfied sign on the first receive (" + sjCount0 + " -> " + WrCount("_washSatisfiedCount") + ")");
                WsExpect(WrCount("_washShakeCount") == sjShake0, "no shake from the first-received shakeSerial (" + sjShake0 + " -> " + WrCount("_washShakeCount") + ")");
                WsExpect(!sjSit, "no sit on the first receive");
                WsNote("_gotFirst false 0.2 s, then cleanSerial / shakeSerial +1 with the state and a fresh hold in one step: no satisfied sign, no shake, no Sit in 2.5 s");
            }));

            // ---- W-shake-remote (同期のブルブルの回数のエッジだけで揺れる = 非 Owner と同じ経路。_RequestShake を直接呼ぶ) ----
            s = WsCase("W-shake-remote", 30);
            WsAnchor srA = default; int srC0 = 0, srS0 = 0; float srAt = 0f, srStart = -1f, srBody = 0f;
            s.Add(WsDo(() => { WrRequire(); WsPlaceShiba(); WsPlayerNear(1.3f); wsWash.SendCustomEvent("_DebugClear"); }));
            s.Add(WsUntil(() => !WsHold(), wsHoldSeconds + 2, () => "the hold did not end"));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() => { srA = WsAnchorOf(WsPathHit(0.2f)); }));
            s.Add(WsDrive(0.3f, 1, u => WsAt(srA), WsSponge, -1f, null));   // dry sponge: the wet trigger cannot fire
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(Get<bool>(wsWander, "_washActive"), "washed before the request");
                srC0 = WrCount("_washShakeCount"); srS0 = Get<int>(wsWash, "shakeSerial");
                wsWash.SendCustomEvent("_RequestShake");
                srAt = Time.time;
            }));
            s.Add(now =>
            {
                float t = Time.time - srAt;
                if (srStart < 0f && WrCount("_washShakeCount") > srC0) srStart = t;
                if (srStart >= 0f) srBody = Mathf.Max(srBody, Mathf.Abs(Get<float>(wrPet, "_shakeBodyDeg")));
                if (srStart < 0f && t > 0.5f) throw new InvalidOperationException("no shake 0.5 s after _RequestShake (shakeSerial " + srS0 + " -> " + Get<int>(wsWash, "shakeSerial") + ")");
                return srStart >= 0f && t >= srStart + 0.5f;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(wsWash, "shakeSerial") == srS0 + 1, "shakeSerial +1 (" + srS0 + " -> " + Get<int>(wsWash, "shakeSerial") + ")");
                WsExpect(WrCount("_washShakeCount") == srC0 + 1 && Get<int>(wsWander, "_washShakeKind") == 1, "one shake from the counter's edge (kind " + Get<int>(wsWander, "_washShakeKind") + ")");
                WsExpect(srBody > 0.5f, "the body bone rolled (max " + srBody.ToString("F2") + " deg)");
                WsNote("_RequestShake with a dry dog: shakeSerial +1 -> shake at +" + srStart.ToString("F2") + " s through the counter edge (the non-Owner path), body roll max " + srBody.ToString("F2") + " deg");
            }));

            // ---- W-paw (前足を洗い始めると足上げの要求 1.5 s、6 s あける。Animator への接続は保留) ----
            s = WsCase("W-paw", 70);
            WsAnchor pwHip = default, pwLeg = default; int pwC0 = 0; float pwLen = 0f; bool pwHas = false;
            s.Add(WsDo(() =>
            {
                WrRequire();
                var legs = WsCover(0.05f).Where(a => wsColPart[a.ci] == 3 || wsColPart[a.ci] == 4).ToList();
                if (legs.Count == 0) throw new WsSkip("no front leg (part 3 / 4) collider on the Shiba");
                pwLeg = legs[0];
                WsPlaceShiba(); WsPlayerNear(1.3f); wsWash.SendCustomEvent("_DebugClear");
                pwHip = WsAnchorOf(WsPathHit(0.2f));
            }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsWait(0.8));
            s.Add(WsUntil(() => Time.time >= Get<float>(wsWander, "_washPawNextAt") + 0.3f, 25, () => "the paw spacing (washPawSpacing) from an earlier case did not run out"));
            s.Add(WsDrive(0.5f, 1, u => WsAt(pwHip), WsSponge, -1f, null));
            s.Add(WsDo(() => { pwC0 = WrCount("_washPawCount"); }));
            s.Add(WsDrive(0.5f, 1, u =>
            {
                if (!pwHas && WrCount("_washPawCount") > pwC0) { pwHas = true; pwLen = Get<float>(wsWander, "_washPawUntil") - Time.time; }
                return WsAt(pwLeg);
            }, WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(WrCount("_washPawCount") == pwC0 + 1, "one paw lift when the stroke moved onto the front leg (" + pwC0 + " -> " + WrCount("_washPawCount") + ", last part " + WsLastPart() + ")");
                WsExpect(pwHas && pwLen > 1.2f && pwLen <= 1.5f, "the request lasts 1.5 s (" + pwLen.ToString("F2") + " s left when first seen)");
            }));
            // 6 s 以内にもう一度 腰 → 前足: 出ない
            s.Add(WsDrive(0.4f, 1, u => WsAt(pwHip), WsSponge, -1f, null));
            s.Add(WsDrive(0.4f, 1, u => WsAt(pwLeg), WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(WrCount("_washPawCount") == pwC0 + 1, "no second paw lift within washPawSpacing (" + WrCount("_washPawCount") + ")");
                WsNote("hip 0.5 s -> front leg (" + wsCols[pwLeg.ci].name + ") 0.5 s: WashPawPose requested for " + pwLen.ToString("F2") + " s; hip -> leg again within washPawSpacing (" + Get<float>(wsWander, "washPawSpacing") + " s): none. NOT checked: the Animator pose on screen (CallReady + CallPose 3 = CallPaw, written by KD_DogWanderController.LateUpdate) -> Play");
            }));

            // ---- W-gaze (2026-09-30: 体を回さない。頭は washGazeIntervalMin..Max 秒に 1 回 washGazeSeconds だけ洗う人を見る。首かしげはその中だけ) ----
            s = WsCase("W-gaze", 60);
            WsAnchor gzA = default, gzB = default; float gzYaw0 = 0f, gzYawMax = 0f, gzT0 = 0f, gzLast = -1f, gzFirst = -1f, gzSwitch = 0f;
            int gzG0 = 0, gzTl0 = 0, gzTiltOut = 0, gzTiltSeen = 0; bool gzUseB = false;
            s.Add(WsDo(() =>
            {
                WrRequire();
                WsPlaceShiba(); WsPlayerNear(1.3f); wsWash.SendCustomEvent("_DebugClear");   // the washer 90 deg to the side: the old code turned the body here
            }));
            s.Add(WsUntil(() => !WsHold(), 6, () => "a hold left over from an earlier case did not end"));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                gzA = WsAnchorOf(WsPathHit(0.1f)); gzB = WsAnchorOf(WsPathHit(0.8f));
                if (wsColPart[gzA.ci] == wsColPart[gzB.ci]) throw new WsSkip("the two flank points are on the same part (" + wsColPart[gzA.ci] + "): no part change for the tilt");
                gzYaw0 = wsRoot.eulerAngles.y; gzG0 = WrCount("_washGazeCount"); gzTl0 = WrCount("_washTiltCount");
                gzT0 = Time.time; gzLast = -1f; gzSwitch = 0f;
            }));
            // 20 s of sponge, switching between the two parts every 0.25 s (a part change is always there for the tilt)
            s.Add(now =>
            {
                float t = Time.time;
                if (gzLast < 0f) gzLast = t - Mathf.Max(Time.deltaTime, 1e-3f);
                float sec = Mathf.Min(t - gzLast, 0.1f); gzLast = t;
                gzSwitch += sec; if (gzSwitch >= 0.25f) { gzSwitch = 0f; gzUseB = !gzUseB; }
                if (sec > 1e-5f) { var h = WsAt(gzUseB ? gzB : gzA); WsContact(WsSponge, h.ci, h.p, sec, -1f); }
                gzYawMax = Mathf.Max(gzYawMax, Mathf.Abs(Mathf.DeltaAngle(gzYaw0, wsRoot.eulerAngles.y)));
                if (gzFirst < 0f && WrCount("_washGazeCount") > gzG0) gzFirst = t - gzT0;
                int tilts = WrCount("_washTiltCount");
                if (tilts > gzTl0 + gzTiltSeen)
                {
                    gzTiltSeen = tilts - gzTl0;
                    if (!(Get<float>(wsWander, "_washTiltStart") < Get<float>(wsWander, "_washGazeUntil"))) gzTiltOut++;
                }
                return t - gzT0 >= 20f;
            });
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                float lo = Get<float>(wsWander, "washGazeIntervalMin"), hi = Get<float>(wsWander, "washGazeIntervalMax");
                int glances = WrCount("_washGazeCount") - gzG0;
                WsExpect(gzYawMax < 3f, "the body did not turn toward the washer 90 deg to the side (max yaw change " + gzYawMax.ToString("F1") + " deg)");
                WsExpect(gzFirst >= lo - 0.3f && gzFirst <= hi + 0.3f, "the first glance after one interval (+" + gzFirst.ToString("F1") + " s, interval " + lo + ".." + hi + " s)");
                WsExpect(glances >= 1 && glances <= 2, "one or two glances in 20 s (" + glances + ")");
                WsExpect(gzTiltSeen >= 1 && gzTiltOut == 0, "tilts only inside a glance (" + gzTiltSeen + " tilts, " + gzTiltOut + " outside)");
                WsNote("sponge 20 s switching parts " + wsColPart[gzA.ci] + "/" + wsColPart[gzB.ci] + " every 0.25 s, washer 90 deg to the side: yaw change max " + gzYawMax.ToString("F1") + " deg; glances " + glances + " (first +" + gzFirst.ToString("F1") + " s, " + Get<float>(wsWander, "washGazeSeconds") + " s each); tilts " + gzTiltSeen + " (outside a glance " + gzTiltOut + ")");
            }));

            // ---- W-react-remote ----
            s = WsCase("W-react-remote", 5);
            s.Add(WsDo(() => { throw new WsSkip("ClientSim has one player (always the Owner): a non-Owner seeing the satisfied sign from the synced cleanSerial, the wet shake from a received shakeSerial (W-shake-remote runs the same edge locally) and the paw from received strokes -> PCVR with 2 people"); }));
        }

        /// <summary>The next serial after s that is a multiple of washSitEvery (a sitting edge).</summary>
        static int WrSitSerial(int s)
        {
            int every = Mathf.Max(1, Get<int>(wsWander, "washSitEvery"));
            return (s / every + 1) * every;
        }

        static void WrRequire()
        {
            if (wrPet == null) throw new WsSkip("the Shiba's petController is not wired");
        }

        static int WrCount(string key) { return Get<int>(wsWander, key); }

        /// <summary>有効なセルの濡れの平均 (KH_DogWash._AverageWet と同じ式を、ヒープの _wet から)。</summary>
        static float WrAvgWet()
        {
            var w = Get<float[]>(wsWash, "_wet"); var valid = Get<bool[]>(wsWash, "cellValid");
            float sum = 0f; int n = 0;
            for (int c = 0; c < w.Length && c < valid.Length; c++) if (valid[c]) { sum += w[c]; n++; }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>有効なセルの泡と濡れを直接書く (Owner のセル。マスクは描き直さない: 反応のきっかけだけを作る)。</summary>
        static void WrSetCells(float foam, float wet)
        {
            var f = Get<float[]>(wsWash, "_foam"); var w = Get<float[]>(wsWash, "_wet"); var valid = Get<bool[]>(wsWash, "cellValid");
            for (int c = 0; c < f.Length && c < valid.Length; c++) if (valid[c]) { f[c] = foam; w[c] = wet; }
            wsWash.SetProgramVariable("_foam", f);
            wsWash.SetProgramVariable("_wet", w);
        }
    }
}
