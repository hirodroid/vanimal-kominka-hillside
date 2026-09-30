using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-030 洗浄の 6 匹への展開 — Course/100 (Wash.cs) に足すケース。
    ///
    /// 枠・手順の部品は Wash.cs のもの (WsCase / WsDo / WsWait / WsUntil / WsDrive / WsExpect / WsNote / WsRef / WsMask / WsBandCheck)。
    /// ケースの一覧への接続は Wash.cs の MakeWashCases の末尾 (MakeWashDirtCases の後、WsWrite の前) で MakeWashDogsCases() を呼ぶ。
    /// ここでは一覧へ自分でつながない。順は wired → others → foam (W-dogs-foam は Wash.cs の束縛 (wsWash / wsRoot / wsCols …) を一時的にダックスへ
    /// 付け替え、最後に保持が自分で切れるのを待ってから柴へ戻す)。W-dogs-others は最初に全部の犬の保持と洗浄の枝が閉じるのを待つ
    /// (run 20260930-113707: 直前のダックスの保持 holdSeconds 3 s が残っていて FAIL)。
    ///
    /// - W-dogs-wired: 6 匹の KD_DogWanderController.wash が配線され、それぞれ自分の殻・部位 Collider (9 部位、犬の下、Trigger、ほかの犬と共有しない)・
    ///   自分のマスク・有効なセル (> 60) を持ち、実行時の初期化が済んでいる。道具 (柴の wash.tools) の washes が 6 匹分
    /// - W-dogs-foam: 柴以外の 1 匹 (ダックス) の横腹をスポンジで 2.5 s。ダックスのマスクに泡の帯 (G +0.1) が出て、ほかの 5 匹のマスクの泡とセルの泡は増えない
    ///   (乾燥で減るのは可)、ほかの犬は洗われ中にならない
    /// - W-dogs-others: 柴を洗っている間、ほかの 5 匹は wash が配線されていても洗われ中にならず (_washActive / _WashHold が false、泡が増えない)、
    ///   洗い終えて離れると 60 s 以内にそれぞれ 0.3 m 以上歩く (既存の W-others の期待を「配線されていても洗われていない犬は既存のまま」へ直したもの)
    ///
    /// 対象外 (Wash.cs と同じ): 実通信・2 人・道具の Pickup と光線 (入力は _LocalContact へ直接)・見た目・端末。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        const string WxFoamDog = "KD_Dog_Dachs", WxHomeDog = "KD_Dog_Shiba";
        /// <summary>ほかの犬の泡の増え方の許容 (マスク G / セル)。乾燥 (減る) は許す。</summary>
        const float WxOtherFoamTolerance = 0.02f;

        static void MakeWashDogsCases()
        {
            // ---- W-dogs-wired ----
            var s = WsCase("W-dogs-wired", 20);
            s.Add(WsDo(() =>
            {
                WxBind(WxIndex(WxHomeDog));
                var owner = new Dictionary<Collider, int>();
                var masks = new HashSet<Texture>();
                var lines = new List<string>();
                for (int i = 0; i < wander.Length; i++)
                {
                    string who = "dog " + i + " (" + roots[i].name + ")";
                    var u = WsRef(wander[i], "wash") as UdonBehaviour;
                    WsExpect(u != null, who + ": KD_DogWanderController.wash is wired");
                    WsExpect(Get<int>(u, "dogIndex") == Get<int>(wander[i], "dogIndex"), who + ": wash.dogIndex " + Get<int>(u, "dogIndex") + " = the dog's index");
                    WsExpect(Get<Transform>(u, "dogRoot") == roots[i], who + ": wash.dogRoot = the dog root");
                    WsExpect(Get<bool>(u, "_ready"), who + ": the wash finished its runtime set-up (_ready)");
                    object sh; var shell = u.TryGetProgramVariable("shellRenderer", out sh) ? sh as Renderer : null;
                    WsExpect(shell != null && shell.transform.IsChildOf(roots[i]), who + ": shell renderer wired, under the dog");
                    var cols = WsArr<Collider>(u, "partColliders");
                    var parts = WsArr<int>(u, "partOfCollider");
                    WsExpect(cols.Length > 0 && cols.All(c => c != null) && parts.Length == cols.Length, who + ": " + cols.Length + " part colliders, none null");
                    foreach (var c in cols)
                    {
                        WsExpect(c.transform.IsChildOf(roots[i]) && c.isTrigger, who + ": " + c.name + " is a trigger under the dog");
                        int prev;
                        WsExpect(!owner.TryGetValue(c, out prev), who + ": " + c.name + " is not wired to dog " + (owner.TryGetValue(c, out prev) ? prev : -1) + " too");
                        owner[c] = i;
                    }
                    var partSet = new HashSet<int>(parts);
                    WsExpect(Enumerable.Range(0, KH_DogWash.PartCount).All(partSet.Contains), who + ": every part 0.." + (KH_DogWash.PartCount - 1) + " has a collider (" + string.Join(",", partSet.OrderBy(x => x)) + ")");
                    int valid = Get<bool[]>(u, "cellValid").Count(x => x);
                    WsExpect(valid > 60, who + ": valid cells " + valid + " > 60");
                    var ma = Get<RenderTexture>(u, "maskA"); var mb = Get<RenderTexture>(u, "maskB");
                    WsExpect(ma != null && mb != null && ma != mb && masks.Add(ma) && masks.Add(mb), who + ": its own two masks (not another dog's)");
                    lines.Add(roots[i].name.Replace("KD_Dog_", "") + " " + cols.Length + " colliders / " + valid + " cells / mts " + Get<float>(u, "meshToWorldScale").ToString("F3"));
                }
                var tools = WsArr<UdonBehaviour>(wsWash, "tools");
                WsExpect(tools.Length > 0, "the Shiba's wash has tools");
                foreach (var t in tools)
                {
                    if (t == null) continue;
                    var ws = WsArr<UdonBehaviour>(t, "washes");
                    WsExpect(ws.Length == wander.Length && ws.All(x => x != null), t.name + ".washes has all " + wander.Length + " dogs (" + ws.Length + ")");
                }
                WsNote("6 dogs wired: " + string.Join("; ", lines) + "; " + owner.Count + " part colliders, none shared; " + tools.Length + " tools with washes x" + wander.Length);
            }));

            // ---- W-dogs-others (柴を洗っている間、配線済みのほかの 5 匹は既存のまま) ----
            s = WsCase("W-dogs-others", 100);
            int[] otDogs = null; float[] otPath = null; Vector3[] otPrev = null;
            var otFoam = new Dictionary<int, float[]>();
            // Wait until no dog is still held from an earlier case (the hold lasts holdSeconds after the last stroke;
            // run 20260930-113707 started this case ~2 s after W-dogs-foam and caught the Dachs still held)
            s.Add(WsDo(() => WxBind(WxIndex(WxHomeDog))));
            s.Add(WxUntilReleased(() => wsHoldSeconds + 0.5, "before W-dogs-others"));
            s.Add(WsDo(() =>
            {
                WxBind(WxIndex(WxHomeDog));
                otDogs = Enumerable.Range(0, wander.Length).Where(i => i != wsDog).ToArray();
                otPath = new float[wander.Length]; otPrev = new Vector3[wander.Length];
                otFoam.Clear();
                foreach (int o in otDogs)
                {
                    WsExpect(WsRef(wander[o], "wash") as UdonBehaviour != null, "dog " + o + " (" + roots[o].name + ") wash is wired (IMP-DOG-030)");
                    otFoam[o] = (float[])Get<float[]>(WxWash(o), "_foam").Clone();
                }
                WsPlaceShiba(); WsPlayerNear(1.3f); WsSetDirt(0.6f);
                foreach (int o in otDogs) otPrev[o] = roots[o].position;
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDrive(2f, 1, u =>
            {
                foreach (int o in otDogs)
                {
                    if (Get<bool>(wander[o], "_washActive")) throw new InvalidOperationException("dog " + o + " (" + roots[o].name + ") entered the wash branch while the Shiba was washed");
                    if (WxHold(WxWash(o))) throw new InvalidOperationException("dog " + o + " (" + roots[o].name + ") _WashHold() true while only the Shiba was washed");
                }
                return WsPath(0.4f + 0.2f * u);
            }, WsSponge, -1f, null));
            s.Add(WsDo(() =>
            {
                WsEnd(WsSponge);
                WsExpect(Get<bool>(wsWander, "_washActive") || WsHold(), "control: the Shiba itself was held");
                foreach (int o in otDogs)
                {
                    var f1 = Get<float[]>(WxWash(o), "_foam"); var f0 = otFoam[o]; float up = 0f;
                    for (int c = 0; c < f1.Length && c < f0.Length; c++) up = Mathf.Max(up, f1[c] - f0[c]);
                    WsExpect(up < 1e-4f, roots[o].name + ": foam cells did not grow while the Shiba was washed (max +" + up.ToString("F4") + ")");
                }
                WsPlayerFar(12f);
            }));
            s.Add(WsUntil(() =>
            {
                foreach (int o in otDogs)
                {
                    var p = roots[o].position; otPath[o] += Flat(p - otPrev[o]); otPrev[o] = p;
                    if (Get<bool>(wander[o], "_washActive")) throw new InvalidOperationException("dog " + o + " wash branch active");
                }
                return otDogs.All(o => otPath[o] >= 0.3f);
            }, 60, () => "not every other dog moved 0.3 m in 60 s: " + string.Join(", ", otDogs.Select(o => o + ":" + otPath[o].ToString("F2") + "m rest" + Get<int>(wander[o], "_restPhase")))));
            s.Add(WsDo(() => WsNote("other dogs " + string.Join(",", otDogs) + ": wash wired, never held / in the wash branch and no foam while the Shiba was washed, each walked >= 0.3 m ("
                + string.Join(", ", otDogs.Select(o => otPath[o].ToString("F1"))) + " m)")));

            // ---- W-dogs-foam (ダックスを洗う。ほかの犬は変わらない) ----
            s = WsCase("W-dogs-foam", 60);
            var foT = new WsTrace(); Color[] fo0 = null; float foLen = 0f; int foDog = -1;
            var foOtherMask = new Dictionary<int, Color[]>(); var foOtherFoam = new Dictionary<int, float[]>();
            s.Add(WsDo(() =>
            {
                foDog = WxIndex(WxFoamDog);
                WxBind(foDog);
                WsPlaceShiba();   // places the bound dog (Wash.cs names it after the Shiba)
                WsPlayerNear(1.3f); WsSetDirt(0.6f);
                foOtherMask.Clear(); foOtherFoam.Clear();
                for (int i = 0; i < wander.Length; i++)
                {
                    if (i == foDog) continue;
                    var u = WxWash(i);
                    foOtherMask[i] = WxMaskOf(u);
                    foOtherFoam[i] = (float[])Get<float[]>(u, "_foam").Clone();
                }
            }));
            s.Add(WsWait(1.2));
            s.Add(WsDo(() => { fo0 = WsMask(); Vector3 a, b; foLen = WsPathEnds(out a, out b); }));
            s.Add(WsDrive(2.5f, 1, u =>
            {
                foreach (var i in foOtherMask.Keys) if (Get<bool>(wander[i], "_washActive")) throw new InvalidOperationException("dog " + i + " (" + roots[i].name + ") entered the wash branch while the Dachs was washed");
                return WsPath(u);
            }, WsSponge, -1f, foT));
            s.Add(WsDo(() => WsEnd(WsSponge)));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                var fo1 = WsMask();
                int total = foT.calls + foT.misses;
                WsExpect(foT.calls >= 10 && foT.misses * 5 <= total, "the flank ray hit the Dachs's part colliders on most frames (" + foT.calls + " hits, " + foT.misses + " misses along " + foLen.ToString("F3") + " m)");
                var seen = new HashSet<int>(foT.part);
                WsExpect(seen.Contains(0) || seen.Contains(1), "the path touched the hip or the chest (parts " + foT.Parts() + ")");
                string band = WsBandCheck(foT, fo0, fo1, 1, wsSpongeR, 5, d => d > 0.1f, "G +0.1");
                float af = WsAvgFoam();
                WsExpect(af > 0.001f, "the Dachs's _AverageFoam() grew (" + af.ToString("F3") + ")");
                var others = new List<string>();
                foreach (var kv in foOtherMask)
                {
                    int i = kv.Key; var u = WxWash(i);
                    float up = WxMaxIncrease(kv.Value, WxMaskOf(u), 1);
                    WsExpect(up < WxOtherFoamTolerance, roots[i].name + ": mask foam (G) did not grow while the Dachs was washed (max +" + up.ToString("F3") + ")");
                    var f1 = Get<float[]>(u, "_foam"); var f0 = foOtherFoam[i]; float cup = 0f;
                    for (int c = 0; c < f1.Length && c < f0.Length; c++) cup = Mathf.Max(cup, f1[c] - f0[c]);
                    WsExpect(cup < 1e-4f, roots[i].name + ": foam cells did not grow (max +" + cup.ToString("F4") + ")");
                    WsExpect(!Get<bool>(wander[i], "_washActive") && !WxHold(u), roots[i].name + ": not being washed (_washActive / _WashHold false)");
                    others.Add(roots[i].name.Replace("KD_Dog_", "") + " +" + up.ToString("F3"));
                }
                WsNote("Dachs (dog " + foDog + ", mts " + wsMts.ToString("F3") + ", " + wsCols.Length + " colliders): sponge 2.5 s along the flank (world " + foLen.ToString("F3") + " m, mesh " + foT.LengthM().ToString("F3") + " m, parts " + foT.Parts()
                    + ", " + foT.calls + " calls / " + foT.misses + " misses): band " + band + ", avg foam " + af.ToString("F3") + "; other dogs' mask foam max increase: " + string.Join(", ", others) + " (< " + WxOtherFoamTolerance + "), cells unchanged, none washed");
                wsWash.SendCustomEvent("_DebugClear");
            }));
            // The Dachs's hold must end by itself holdSeconds after the last stroke (not a stuck hold), then rebind to the Shiba
            s.Add(WxUntilReleased(() => wsHoldSeconds + 0.5, "after the Dachs's last stroke"));
            s.Add(WsDo(() => WxBind(WxIndex(WxHomeDog))));
        }

        // ============================================================
        // 束縛の付け替え (Wash.cs の WsBind の後半と同じ。記号表と入口は同じプログラムなので WsResolve のキャッシュをそのまま使う)
        // ============================================================

        /// <summary>Extra time allowed after the hold ends for the wash branch to close (the satisfied reaction can still run).</summary>
        const double WxBranchGrace = 10.0;

        /// <summary>全部の犬の _WashHold() が holdLimit 秒以内に false になり、そのあと _washActive (洗浄の枝) が WxBranchGrace 秒以内に閉じるまで待つ。
        /// どの犬がいつ離れたかを記録する (保持が自分で切れること = 実行時の不具合でないことの証拠)。時間切れは失敗。</summary>
        static Func<double, bool> WxUntilReleased(Func<double> holdLimit, string label)
        {
            double t0 = -1, holdAt = -1; var heldAtStart = new List<string>(); var released = new Dictionary<int, double>();
            return now =>
            {
                if (t0 < 0)
                {
                    t0 = now; holdAt = -1; heldAtStart.Clear(); released.Clear();
                    for (int i = 0; i < wander.Length; i++)
                        if (WxHold(WxWash(i)) || Get<bool>(wander[i], "_washActive")) heldAtStart.Add(roots[i].name.Replace("KD_Dog_", "") + (WxHold(WxWash(i)) ? " hold" : "") + (Get<bool>(wander[i], "_washActive") ? " branch" : ""));
                }
                bool anyHold = false, anyBranch = false;
                for (int i = 0; i < wander.Length; i++)
                {
                    bool h = WxHold(WxWash(i)), br = Get<bool>(wander[i], "_washActive");
                    anyHold |= h; anyBranch |= br;
                    if (!h && !br && !released.ContainsKey(i)) released[i] = now - t0;
                }
                if (!anyHold && holdAt < 0) holdAt = now;
                if (!anyHold && !anyBranch)
                {
                    WsNote("released " + label + ": held at start [" + string.Join(", ", heldAtStart) + "], all holds off after " + (holdAt - t0).ToString("F2") + " s, wash branches closed after " + (now - t0).ToString("F2")
                        + " s (limit hold " + holdLimit().ToString("F1") + " s + branch " + WxBranchGrace + " s)" + (heldAtStart.Count > 0 ? "; per dog " + string.Join(", ", released.Where(kv => kv.Value > 0).Select(kv => roots[kv.Key].name.Replace("KD_Dog_", "") + " " + kv.Value.ToString("F2") + " s")) : ""));
                    t0 = -1;
                    return true;
                }
                if (anyHold && now - t0 > holdLimit())
                    throw new TimeoutException("a hold did not end within " + holdLimit().ToString("F1") + " s " + label + " (stuck hold?): " + string.Join(", ", Enumerable.Range(0, wander.Length).Where(i => WxHold(WxWash(i))).Select(i => roots[i].name)));
                if (!anyHold && now - holdAt > WxBranchGrace)
                    throw new TimeoutException("the wash branch stayed open " + WxBranchGrace + " s after every hold ended " + label + ": " + string.Join(", ", Enumerable.Range(0, wander.Length).Where(i => Get<bool>(wander[i], "_washActive")).Select(i => roots[i].name)));
                return false;
            };
        }

        static int WxIndex(string dogName)
        {
            int i = Array.FindIndex(roots, r => r != null && r.name == dogName);
            if (i < 0) throw new InvalidOperationException(dogName + " not among the dog roots");
            return i;
        }

        static UdonBehaviour WxWash(int dog)
        {
            var u = WsRef(wander[dog], "wash") as UdonBehaviour;
            if (u == null) throw new InvalidOperationException(roots[dog].name + ": KD_DogWanderController.wash is not wired (run Wash/11)");
            return u;
        }

        /// <summary>Wash.cs の部品 (WsPath / WsContact / WsMask / WsLocal …) が見る犬を dog へ付け替える。</summary>
        static void WxBind(int dog)
        {
            var u = WxWash(dog);
            if (u == wsWash && wsDog == dog) return;
            wsWash = u; wsDog = dog; wsWander = wander[dog]; wsRoot = roots[dog];
            WsExpect(Get<int>(wsWash, "dogIndex") == Get<int>(wsWander, "dogIndex"), roots[dog].name + ": wash.dogIndex = the dog's index");
            WsExpect(Get<Transform>(wsWash, "dogRoot") == wsRoot, roots[dog].name + ": wash.dogRoot = the dog root");
            wsCols = WsArr<Collider>(wsWash, "partColliders"); wsBones = WsArr<Transform>(wsWash, "partBones");
            wsColPart = WsArr<int>(wsWash, "partOfCollider"); wsB2M = WsArr<Matrix4x4>(wsWash, "boneToMesh");
            wsNeighbors = WsArr<int>(wsWash, "partNeighbors"); wsBoneToPart = WsArr<float>(wsWash, "boneToPart");
            if (wsCols.Length == 0 || wsBones.Length < wsCols.Length || wsColPart.Length < wsCols.Length || wsB2M.Length < wsCols.Length) throw new InvalidOperationException(roots[dog].name + ": wash collider arrays are not wired");
            wsMts = Get<float>(wsWash, "meshToWorldScale"); if (!(wsMts > 0f)) wsMts = 1f;
            wsSpongeR = Get<float>(wsWash, "spongeRadius"); wsShowerR = Get<float>(wsWash, "showerRadius"); wsHoldSeconds = Get<float>(wsWash, "holdSeconds");
            object sh; wsShell = wsWash.TryGetProgramVariable("shellRenderer", out sh) ? sh as Renderer : null;
            if (wsShell == null) throw new InvalidOperationException(roots[dog].name + ": wash.shellRenderer is not wired");
            if (WsTorso().Count == 0) throw new InvalidOperationException(roots[dog].name + ": no torso (part 0 / 1) collider");
            wsLayer = WsLoadLayer();
            WsLoadPosMap(Get<Texture>(wsWash, "posMap"));
            wsProbed = false;   // the first contact on this dog checks again that _LocalContact reached it
            wsPosHist.Clear();
            wsReport.resolved.Add("bound to " + roots[dog].name + " (dog " + dog + ", mts " + wsMts.ToString("F3") + ", " + wsCols.Length + " colliders, " + wsTexCount + " texels)");
        }

        /// <summary>ある犬の今のマスク (ヒープの _cur、無ければ maskA)。</summary>
        static Color[] WxMaskOf(UdonBehaviour u)
        {
            object o; var cur = u.TryGetProgramVariable("_cur", out o) ? o as RenderTexture : null;
            if (cur == null) cur = Get<RenderTexture>(u, "maskA");
            if (cur == null) throw new InvalidOperationException(u.name + ": no mask");
            return WsRead(cur, TextureFormat.RGBAHalf);
        }

        static float WxMaxIncrease(Color[] a, Color[] b, int ch)
        {
            float m = 0f;
            for (int i = 0; i < a.Length && i < b.Length; i++) m = Mathf.Max(m, WsCh(b[i], ch) - WsCh(a[i], ch));
            return m;
        }

        /// <summary>ある犬の _WashHold() (WsHold は束縛中の犬だけ)。入口と戻り値の記号は同じプログラムなので Wash.cs のキャッシュを使う。</summary>
        static bool WxHold(UdonBehaviour u)
        {
            u.SendCustomEvent(WsEntry("_WashHold"));
            object o;
            if (!u.TryGetProgramVariable(WsRetSym("_WashHold"), out o) || !(o is bool)) throw new InvalidOperationException(u.name + ": return of _WashHold unreadable (" + o + ")");
            return (bool)o;
        }
    }
}
