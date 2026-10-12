using System;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-ENV-013 洗い場の台の大きさ (犬ごと) — Course/100 の群 stand-size (メニュー 100f3。群 height にも含む)。
    ///
    /// W-stand-size: 主洗い場。台の品を無料の台へ戻し (プールは大きさを保つので)、空の台が idle の大きさにあること → ポメ (いちばん小さい台)
    /// を呼ぶ (ReqCall) → 受け付けた時点 (着く前) から台が standSize[ポメ] へ standSpeed (1.5 m/s) で寄る (縮む向きだけ、高さ・厚みは不変)
    /// → 着いた後もその大きさ → おわり (ReqRelease) → 台が idle へ同じ速さで戻る (広がる向きだけ)。時間は Time.time で測る。
    /// 判定は同期の切り替わり (heldDog が立った / 下りた) を見た時刻から (Codex レビュー 2026-10-12): 動き始めが 1 フレーム以内、
    /// 寄せ終わるまで |変化| / 1.5 m/s、呼んだ時は犬が着く前に途中の大きさが見え、着く前に寄せ終わる (「着いてから縮む」を落とす)。
    /// 部品は Wash.cs (WsCase / WsDo / WsUntil ...)、WashCall.cs (WcBind / WcSetup / WcDump)、WashCallAvail.cs (WcBringNear)、
    /// WashHeight.cs (WhBind / WhFreePlatform)。台の品は元に戻さない (W-height-stand と同じ)。
    ///
    /// 対象外: 足が台からはみ出さないこと (Edit の Wash/23 が 6 匹 × 4 か所で計算)、見た目 (檜の木目・縁)、2 人で同じ大きさに見えること、
    /// 途中参加の即時合わせ (OnDeserialization: ClientSim は 1 人) → Play / PCVR。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        const int SsDog = 1;   // ポメ: the smallest stand, the largest change from idle

        static Transform ssTable;
        static Vector3 ssBaseSize, ssBaseScale, ssBasePos;

        /// <summary>The stand's current (length, width, offset) from standTable's transform, as KH_WashStationCall._StandPose writes it.</summary>
        static Vector3 SsNow()
        {
            var s = ssTable.localScale; var p = ssTable.localPosition;
            return new Vector3(s.x / ssBaseScale.x * ssBaseSize.x, s.z / ssBaseScale.z * ssBaseSize.z, p.x - ssBasePos.x);
        }
        static float SsYDev() { return Mathf.Max(Mathf.Abs(ssTable.localScale.y - ssBaseScale.y), Mathf.Abs(ssTable.localPosition.y - ssBasePos.y)); }
        static string SsText(Vector3 v) { return v.x.ToString("F3") + " x " + v.y.ToString("F3") + " (offset " + v.z.ToString("+0.000;-0.000") + ")"; }

        static void MakeWashStandSizeCases()
        {
            WcBind();
            var s = WsCase("W-stand-size", 150);
            Vector3 idle = Vector3.zero, want = Vector3.zero; float speed = 1.5f;
            // One ease (shrink after the call, grow after the release), all times Time.time from the request (t0):
            //   edgeAt = the synced edge seen (heldDog == the Pome / heldDog < 0), moveAt = the first frame the size changed, landAt = on the target.
            //   Codex review 2026-10-12: judged from the edge, not from moveAt (a "starts only on arrival" stand would pass a duration measured
            //   from its own start): the start comes within a frame of the edge, the whole ease takes |change| / speed from the edge, and for the
            //   call a size between idle and the Pome's is seen while the dog has not arrived (and the ease lands before it arrives).
            float t0 = 0f, edgeAt = -1f, moveAt = -1f, landAt = -1f, prevDist = 0f, startDist = 0f, backStep = 0f, yDev = 0f, landFlat = 0f;
            int samples = 0; bool midBeforeArrival = false, arrivedAtLand = false;
            // the target is read when the step runs (want / idle are filled by the first step at run time)
            Func<Func<Vector3>, Func<bool>, Func<double, bool>> ease = (targetOf, edge) => now =>
            {
                Vector3 cur = SsNow(), target = targetOf();
                float dist = Vector3.Distance(cur, target), t = Time.time - t0;
                bool arrived = Get<bool>(whCall, "arrived");
                float flat = Flat(roots[SsDog].position - wcAnchor.position);
                samples++; yDev = Mathf.Max(yDev, SsYDev());
                if (edgeAt < 0f && edge()) edgeAt = t;
                if (moveAt < 0f && Mathf.Abs(dist - prevDist) > 1e-4f) moveAt = t;
                if (dist > 1e-3f && dist < startDist - 1e-3f && !arrived && flat > 0.35f) midBeforeArrival = true;
                backStep = Mathf.Max(backStep, dist - prevDist);   // > 0 = moved away from the target
                prevDist = dist;
                if (dist < 1e-4f) { landAt = t; arrivedAtLand = arrived; landFlat = flat; return true; }
                if (t > 8f) throw new TimeoutException("the stand did not reach " + SsText(target) + " in 8 s (now " + SsText(cur) + ", heldDog " + Get<int>(whCall, "heldDog") + ", standKeep " + Get<bool>(whCall, "standKeep") + ", edge at " + edgeAt.ToString("F2") + ")");
                return false;
            };
            Action<Vector3> startEase = target => { t0 = Time.time; edgeAt = -1f; moveAt = -1f; landAt = -1f; prevDist = startDist = Vector3.Distance(SsNow(), target); backStep = 0f; yDev = 0f; samples = 0; midBeforeArrival = false; arrivedAtLand = false; };
            Action<string, Vector3, Vector3, bool> judge = (what, from, to, beforeArrival) =>
            {
                float expected = Vector3.Distance(from, to) / Mathf.Max(0.05f, speed), took = landAt - edgeAt, delay = moveAt - edgeAt;
                float frame = wsMaxDt + 0.02f, slack = 0.1f + 2f * wsMaxDt;
                WsExpect(edgeAt >= 0f && moveAt >= 0f, what + ": the synced edge (+" + edgeAt.ToString("F2") + " s) and a change of size (+" + moveAt.ToString("F2") + " s) were both seen");
                WsExpect(delay >= -1e-4f && delay <= frame, what + ": the stand started within a frame of the edge (" + (delay * 1000f).ToString("F0") + " ms, <= " + (frame * 1000f).ToString("F0") + ")");
                WsExpect(took >= expected - slack && took <= expected + slack,
                    what + ": from the edge to " + SsText(to) + " in " + took.ToString("F2") + " s (|change| / " + speed + " m/s = " + expected.ToString("F2") + " s, +-" + slack.ToString("F2") + "; eased, not snapped)");
                WsExpect(backStep <= 1e-4f, what + ": every frame closer to the target (largest step away " + backStep.ToString("F5") + " m over " + samples + " samples)");
                WsExpect(yDev < 1e-5f, what + ": the table's height and thickness unchanged (largest y change " + yDev.ToString("F6") + ")");
                if (beforeArrival)
                {
                    WsExpect(midBeforeArrival, what + ": a size between " + SsText(from) + " and " + SsText(to) + " was seen while the dog was still walking (not arrived, > 0.35 m from the anchor)");
                    WsExpect(!arrivedAtLand && landFlat > 0.35f, what + ": the stand landed before the dog arrived (arrived " + arrivedAtLand + ", " + landFlat.ToString("F2") + " m from the anchor; Q3: the accepted call, not the arrival)");
                }
                WsNote(what + ": " + SsText(from) + " -> " + SsText(to) + ", edge +" + edgeAt.ToString("F2") + " s after the request, started " + (delay * 1000f).ToString("F0") + " ms after it, landed " + took.ToString("F2") + " s after it (expected " + expected.ToString("F2") + ")"
                    + (beforeArrival ? ", dog " + landFlat.ToString("F2") + " m away at the landing" : "") + ", " + samples + " samples");
            };

            s.Add(WsDo(() =>
            {
                wsFailDump = WcDump;
                WhBind();
                if (wcStation == null || whCall != wcStation) throw new InvalidOperationException("station 0's KH_WashStationCall is not " + WhStationName + "/Call");
                ssTable = WsRef(whCall, "standTable") as Transform;
                var sizes = WsRef(whCall, "standSize") as Vector3[];
                if (ssTable == null || sizes == null || sizes.Length <= SsDog) throw new WsSkip("the stand sizes are not applied (Wash/22: standTable / standSize)");
                ssBaseSize = Get<Vector3>(whCall, "standBaseSize"); ssBaseScale = Get<Vector3>(whCall, "standBaseScale"); ssBasePos = Get<Vector3>(whCall, "standBasePos");
                idle = Get<Vector3>(whCall, "standIdle"); want = sizes[SsDog]; speed = Get<float>(whCall, "standSpeed");
                WsExpect(ssBaseSize.x > 0f && ssBaseSize.z > 0f && ssBaseScale.x > 0f && ssBaseScale.z > 0f, "standBaseSize / standBaseScale wired (" + ssBaseSize + " / " + ssBaseScale + ")");
                WsExpect(want.x < idle.x && want.y <= idle.y, "the Pome's stand " + SsText(want) + " is smaller than idle " + SsText(idle));
                WhFreePlatform();   // the pool keeps 1.3 x 0.7: the case needs a platform that scales
                WsExpect(!Get<bool>(whCall, "standKeep"), "standKeep false with the free table");
            }));
            WcSetup(s);   // the Shiba free by the gate, no station holds a dog, me beside the stand
            s.Add(WsDo(() => { if (Get<int[]>(manager, "walkers")[SsDog] >= 0) pouch.SendCustomEvent("_WalkOff" + SsDog); }));
            s.Add(WsUntil(() => Vector3.Distance(SsNow(), idle) < 1e-4f, 4, () => "the empty stand is not at idle " + SsText(idle) + " (now " + SsText(SsNow()) + ")"));
            s.Add(WsDo(() =>
            {
                WsExpect(SsYDev() < 1e-5f, "the empty stand's height and thickness are the scene's (y change " + SsYDev().ToString("F6") + ")");
                WcBringNear(SsDog);   // IMP-DOG-037: within callRange
                startEase(want);
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqCall", SsDog);
            }));
            s.Add(ease(() => want, () => Get<int>(whCall, "heldDog") == SsDog));
            s.Add(WsDo(() =>
            {
                judge("called the Pome (shrink)", idle, want, true);
                WsExpect(Get<int>(whCall, "heldDog") == SsDog, "the Pome held (heldDog " + Get<int>(whCall, "heldDog") + ")");
            }));
            s.Add(WsUntil(() => Get<bool>(whCall, "arrived") && Flat(roots[SsDog].position - wcAnchor.position) < 0.35f, 60,
                () => "the Pome did not arrive (" + Flat(roots[SsDog].position - wcAnchor.position).ToString("F1") + " m from the anchor)"));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                WsExpect(Vector3.Distance(SsNow(), want) < 1e-4f, "on the stand the size stays the Pome's (" + SsText(SsNow()) + ")");
                WsNote("the Pome on the stand: root " + Flat(roots[SsDog].position - wcAnchor.position).ToString("F3") + " m from the anchor, stand " + SsText(SsNow()));
                startEase(idle);
                wcStation.SendCustomNetworkEvent(NetworkEventTarget.Owner, "ReqRelease");
            }));
            s.Add(ease(() => idle, () => Get<int>(whCall, "heldDog") < 0));
            s.Add(WsDo(() =>
            {
                judge("released (grow back to idle)", want, idle, false);
                WsExpect(Get<int>(whCall, "heldDog") < 0, "released (heldDog " + Get<int>(whCall, "heldDog") + ")");
            }));
        }
    }
}
