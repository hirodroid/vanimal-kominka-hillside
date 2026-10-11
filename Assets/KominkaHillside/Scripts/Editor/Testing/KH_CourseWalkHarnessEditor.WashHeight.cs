using System;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-ENV-011 洗い場の高さ (台と自分の床) — Course/100 (Wash.cs) に足すケース。
    ///
    /// 枠・手順の部品は Wash.cs (WsCase / WsDo / WsWait / WsUntil / WsExpect / WsNote / WsRef / WsPlaceShiba)、立つ・離れるは
    /// Commerce.cs (CmGround / CmTeleport)。ケースの一覧への接続は Wash.cs の MakeWashCases の末尾 (WsWrite() の前) で
    /// MakeWashHeightCases() を呼ぶ。ここでは一覧へ自分でつながない。対象は主洗い場 (KH_WashStation)。
    ///
    /// W-height-floor: 自分の床。棚の東の床の上に立ち _Up ×3 → 0.3 m へ上がり、プレイヤーの足も床の上面へ (上げ終えた時の TeleportTo は 0 か 1 回)。
    ///   9 m 離れると 0 へ戻り、Collider が切れる。
    /// W-height-stand: 台。ボードの犬のボタン (柴) → 留まる、台 ▲ ×2 → liftRoot が 0.2 m 上がる、台 ▼ → 0.1 m、おわり → 解放して 0 へ。
    ///   始めに主洗い場の台の品を無料の台へ戻す (IMP-DOG-044: プール等を選んでいると台は最低の高さ minLift より下がらない)。元には戻さない。
    ///   ボードのハンドラ (_Dog / _LiftUp / _LiftDown / _End) が Call の入口を呼ぶことも、これで見る。犬のルートの追従は別のケース (IMP-DOG-035)。
    /// W-height-board-me: ボードの 自分 ▲ ×2 → 床の目標 0.2 m、自分 ▼ ×2 → 0。
    ///
    /// 対象外: VRChat の実際のプレイヤー (ClientSim の CharacterController と同じとは限らない) が動く床に乗るか、他の人の見え方、
    /// Interact の光線が床の層 (Water) で止まるか、ボタンを光線で押すこと → Play / PCVR。</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        const string WhStationName = "KH_WashStation";
        static Transform whStation, whFloorRoot, whFloor, whLift;
        static UdonBehaviour whPf, whCall, whBoard;

        static void MakeWashHeightCases()
        {
            // ---- W-height-floor ----
            var s = WsCase("W-height-floor", 40);
            float y0 = 0f, top = 0f; int rescues0 = 0;
            s.Add(WsDo(() =>
            {
                WhBind();
                WhFloorZero();
                var p = CmGround(whFloorRoot.TransformPoint(new Vector3(1.6f, 0f, 0f)), 1.5f);   // on the east floor box, beside the duckboard
                CmTeleport(p, whStation.position - p);
            }));
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                y0 = Networking.LocalPlayer.GetPosition().y;
                rescues0 = Get<int>(whPf, "_rescues");
                for (int i = 0; i < 3; i++) whPf.SendCustomEvent("_Up");
                WsExpect(Get<int>(whPf, "_steps") == 3, "_Up x3 -> 3 steps (" + Get<int>(whPf, "_steps") + ")");
            }));
            s.Add(WsUntil(() => Mathf.Abs(Get<float>(whPf, "_height") - 0.3f) < 1e-3f, 4, () => "the floor did not reach 0.3 m in 4 s (" + Get<float>(whPf, "_height").ToString("F3") + ")"));
            s.Add(WsWait(0.8));
            s.Add(WsDo(() =>
            {
                top = whFloorRoot.position.y + Get<float>(whPf, "baseY") + 0.3f;
                float y1 = Networking.LocalPlayer.GetPosition().y;
                var cols = WsArr<Collider>(whPf, "floorColliders");
                WsExpect(cols.Length >= 1 && Array.TrueForAll(cols, c => c != null && c.enabled), "the floor collider(s) are on above 0 (" + cols.Length + ")");
                WsExpect(Mathf.Abs(whFloor.localPosition.y - (Get<float>(whPf, "baseY") + 0.3f)) < 1e-3f, "the floor boxes moved to base + 0.3 (" + whFloor.localPosition.y.ToString("F3") + ")");
                WsExpect(Mathf.Abs(y1 - top) < 0.08f && y1 > y0 + 0.1f, "the player stands on the raised floor (feet " + y0.ToString("F3") + " -> " + y1.ToString("F3") + ", top " + top.ToString("F3") + ")");
                int rescues = Get<int>(whPf, "_rescues") - rescues0;
                WsExpect(rescues == 0 || rescues == 1, "at most one TeleportTo for the whole rise, at its end (" + rescues + ")");
                WsNote("floor 0 -> 0.3 m at 0.5 m/s: feet " + y0.ToString("F3") + " -> " + y1.ToString("F3") + " (top " + top.ToString("F3") + "); TeleportTo at the end of the rise: " + rescues
                    + (rescues == 0 ? " (the controller rode the floor by itself)" : " (the controller did not ride it: one TeleportTo onto the top when the floor reached 0.3)"));
                // away: 9 m from the station
                var away = CmGround(whStation.position + whStation.rotation * new Vector3(0f, 0f, -9.5f), 3f);
                CmTeleport(away, whStation.position - away);
            }));
            s.Add(WsUntil(() => Get<int>(whPf, "_steps") == 0 && Get<float>(whPf, "_height") <= 0f, 5, () => "the floor did not go back to 0 after leaving (steps " + Get<int>(whPf, "_steps") + ", height " + Get<float>(whPf, "_height").ToString("F3") + ")"));
            s.Add(WsDo(() =>
            {
                var cols = WsArr<Collider>(whPf, "floorColliders");
                WsExpect(Array.TrueForAll(cols, c => c != null && !c.enabled), "the floor colliders are off at 0");
                WsNote("more than 8 m away -> back to 0, colliders off");
            }));

            // ---- W-height-stand ----
            s = WsCase("W-height-stand", 80);
            int dog = 4;
            s.Add(WsDo(() =>
            {
                WhBind();
                if (wsRoot != null) { dog = wsDog; WsPlaceShiba(); }
                var p = CmGround(whFloorRoot.TransformPoint(new Vector3(1.6f, 0f, 0f)), 1.5f);
                CmTeleport(p, whStation.position - p);
                whBoard.SendCustomEvent("_End");
                WhFreePlatform();   // IMP-DOG-044: the stand's actual height below is the synced lift only with no premium platform's minLift
            }));
            s.Add(WsWait(0.5));
            // the dog must be free: an earlier case (call group C-fetch-busy) may leave it in a Fetch or held at a station.
            // Wait up to 30 s for Idle and no station holding it (releasing any that does); then force the Fetch to Idle.
            UdonBehaviour fetch = null; bool forced = false; double freeFrom = 0;
            s.Add(WsDo(() =>
            {
                var wander = WhWander(dog);
                fetch = wander != null ? WsRef(wander, "fetchController") as UdonBehaviour : null;
                freeFrom = UnityEditor.EditorApplication.timeSinceStartup;
            }));
            s.Add(now =>
            {
                foreach (var c in WhCalls()) if (Get<int>(c, "heldDog") == dog) c.SendCustomEvent("_Release");
                bool idle = fetch == null || Get<int>(fetch, "dogState") == 0;
                bool free = idle && !Array.Exists(WhCalls(), c => Get<int>(c, "heldDog") == dog);
                if (free) return true;
                if (now - freeFrom < 30.0) return false;
                if (!idle && fetch != null && Networking.IsOwner(fetch.gameObject))
                {
                    fetch.SetProgramVariable("dogState", 0);   // the Owner's own synced value (ClientSim: the only player)
                    fetch.RequestSerialization();
                    forced = true;
                }
                return true;
            });
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                WsExpect(fetch == null || Get<int>(fetch, "dogState") == 0, "dog " + dog + " Idle before the call (dogState " + (fetch != null ? Get<int>(fetch, "dogState").ToString() : "-") + ")");
                if (forced) WsNote("dog " + dog + " was still busy after 30 s (an earlier case's Fetch): dogState forced to Idle");
                whBoard.SendCustomEvent("_Dog" + dog);
            }));
            s.Add(WsUntil(() => Get<int>(whCall, "heldDog") == dog, 8, () => "board _Dog" + dog + ": the Call did not hold dog " + dog + " (heldDog " + Get<int>(whCall, "heldDog") + ", refused reason " + Get<int>(whCall, "refusedReason") + ")"));
            // presses 0.25 s apart: the Call drops a lift step within liftInterval (0.2 s) of the last one
            s.Add(WsDo(() => whBoard.SendCustomEvent("_LiftUp")));
            s.Add(WsWait(0.25));
            s.Add(WsDo(() => whBoard.SendCustomEvent("_LiftUp")));
            s.Add(WsUntil(() => Mathf.Abs(Get<float>(whCall, "lift") - 0.2f) < 1e-3f && Mathf.Abs(whLift.localPosition.y - 0.2f) < 0.005f, 5,
                () => "board _LiftUp x2: lift " + Get<float>(whCall, "lift").ToString("F3") + ", liftRoot local y " + whLift.localPosition.y.ToString("F3") + " (want 0.2)"));
            s.Add(WsDo(() => { WsNote("board _Dog" + dog + " -> held; _LiftUp x2 (0.25 s apart) -> lift 0.2, liftRoot y " + whLift.localPosition.y.ToString("F3")); }));
            s.Add(WsWait(0.25));
            s.Add(WsDo(() => whBoard.SendCustomEvent("_LiftDown")));
            s.Add(WsUntil(() => Mathf.Abs(Get<float>(whCall, "lift") - 0.1f) < 1e-3f && Mathf.Abs(whLift.localPosition.y - 0.1f) < 0.005f, 5,
                () => "board _LiftDown: lift " + Get<float>(whCall, "lift").ToString("F3") + ", liftRoot y " + whLift.localPosition.y.ToString("F3") + " (want 0.1)"));
            s.Add(WsDo(() => whBoard.SendCustomEvent("_End")));
            s.Add(WsUntil(() => Get<int>(whCall, "heldDog") < 0 && whLift.localPosition.y < 0.005f, 6,
                () => "board _End: heldDog " + Get<int>(whCall, "heldDog") + ", liftRoot y " + whLift.localPosition.y.ToString("F3") + " (want released, 0)"));
            s.Add(WsDo(() => WsNote("board _LiftDown -> 0.1; _End -> released, the stand back to 0")));

            // ---- W-height-board-me ----
            s = WsCase("W-height-board-me", 15);
            s.Add(WsDo(() =>
            {
                WhBind(); WhFloorZero();
                var p = CmGround(whFloorRoot.TransformPoint(new Vector3(1.6f, 0f, 0f)), 1.5f);
                CmTeleport(p, whStation.position - p);
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                whBoard.SendCustomEvent("_MeUp"); whBoard.SendCustomEvent("_MeUp");
                WsExpect(Get<int>(whPf, "_steps") == 2, "board _MeUp x2 -> my floor 2 steps (" + Get<int>(whPf, "_steps") + ")");
                whBoard.SendCustomEvent("_MeDown"); whBoard.SendCustomEvent("_MeDown");
                WsExpect(Get<int>(whPf, "_steps") == 0, "board _MeDown x2 -> 0 steps (" + Get<int>(whPf, "_steps") + ")");
                whBoard.SendCustomEvent("_MeDown");
                WsExpect(Get<int>(whPf, "_steps") == 0, "below 0 stays 0");
                WsNote("board _MeUp / _MeDown reach KH_WashPersonalFloor._Adjust (2 up, 2 down, clamped at 0)");
            }));
            s.Add(WsUntil(() => Get<float>(whPf, "_height") <= 0f, 3, () => "my floor did not settle at 0"));
        }

        static void WhBind()
        {
            var go = GameObject.Find(WhStationName);
            if (go == null || go.transform.parent != null) throw new InvalidOperationException(WhStationName + " not found at the scene root (run Wash/11)");
            whStation = go.transform;
            whFloorRoot = whStation.Find("PersonalFloor");
            whPf = whFloorRoot != null ? whFloorRoot.GetComponent<UdonBehaviour>() : null;
            if (whPf == null) throw new InvalidOperationException(WhStationName + "/PersonalFloor has no UdonBehaviour (IMP-ENV-011: run Wash/10b and 11)");
            whFloor = WsRef(whPf, "floor") as Transform;
            if (whFloor == null) throw new InvalidOperationException("PersonalFloor.floor is not wired");
            var c = whStation.Find("Call");
            whCall = c != null ? c.GetComponent<UdonBehaviour>() : null;
            if (whCall == null) throw new InvalidOperationException(WhStationName + "/Call has no UdonBehaviour (KH_WashStationCall)");
            whLift = WsRef(whCall, "liftRoot") as Transform;
            if (whLift == null || whLift != whStation.Find("Lift")) throw new InvalidOperationException("Call.liftRoot is not " + WhStationName + "/Lift");
            var b = whStation.Find("Board");
            whBoard = b != null ? b.GetComponent<UdonBehaviour>() : null;
            if (whBoard == null) throw new InvalidOperationException(WhStationName + "/Board has no UdonBehaviour (KH_WashStationBoard)");
        }

        static UdonBehaviour[] WhCalls() { return WssPrograms("KH_WashStationCall").ToArray(); }

        static UdonBehaviour WhWander(int dog)
        {
            foreach (var u in WssPrograms("KD_DogWanderController")) if (Get<int>(u, "dogIndex") == dog) return u;
            return null;
        }

        /// <summary>IMP-DOG-044 (2026-10-11): the main station's premium platform back to the free table (selectedBath -1, applied
        /// locally as WashPremium's PremiumClear does; the harness is the only player and the Owner), so KH_WashStationCall.minLift is 0
        /// and liftRoot follows the synced lift alone. A scene without the premium selection: nothing to do.</summary>
        static void WhFreePlatform()
        {
            var t = whStation.Find("PremiumTools");
            var sel = t != null ? t.GetComponent<UdonBehaviour>() : null;
            if (sel == null || sel.programSource == null || sel.programSource.name != "KH_WashSelection") { WsNote(WhStationName + ": no premium selection (minLift stays 0)"); return; }
            int bath = Get<int>(sel, "selectedBath");
            if (bath >= 0)
            {
                sel.SetProgramVariable("selectedBath", -1);
                sel.SetProgramVariable("revision", Get<int>(sel, "revision") + 1);
                HwInvoke(sel, "_Apply", "animate", false);
                WsNote(WhStationName + ": premium platform " + bath + " -> the free table for this case");
            }
            WsExpect(Get<float>(whCall, "minLift") == 0f, WhStationName + ": Call.minLift 0 with the free table (" + Get<float>(whCall, "minLift").ToString("F3") + ")");
        }

        /// <summary>My floor back to 0 at once (Down x 12; the height eases down at 0.5 m/s by itself).</summary>
        static void WhFloorZero() { for (int i = 0; i < 12; i++) whPf.SendCustomEvent("_Down"); }

        // ============================================================
        // IMP-DOG-044 (2026-10-11): the main station's minimum stand height with the pool — group height-minlift (menu 100f2, also in 100f)
        // ============================================================

        /// <summary>WashPremium/50 fitted the main station's pool at this height on 2026-10-11 (IMP-DOG-044「主洗い場の斜面: 適用」). The case
        /// reads the scene's bathMinLifts and expects it here; a re-fit (terrain changed) moves both.</summary>
        const float WhPoolMinLiftFitted = 0.223f;

        /// <summary>The stand's height above its base as KH_WashStationCall writes it (liftRoot local y - _liftBase.y).</summary>
        static float WhLiftY() { return whLift.localPosition.y - Get<Vector3>(whCall, "_liftBase").y; }

        /// <summary>W-height-minlift: the pool on the main station (UI) -> the empty stand eases to _LiftRest() = minLift; the called Shiba
        /// stands at the raised anchor; lift 0.1 / 0.2 (below minLift) leaves the stand there, 0.3 raises it; おわり -> the stand rides back
        /// down to minLift (not 0) with the dog on the anchor, then the dog eases to the ground and walks out (never below the terrain, no
        /// snap, no timeout); the board's reset (platform free) -> the stand back to 0. The selection is put back as it was at the start.
        /// Board handlers (_LiftUp / _End) and the premium board (Take / Reset) are the product entries; the call is ReqCall (WashCall.cs).</summary>
        static void MakeWashMinLiftCases()
        {
            WcBind();
            var s = WsCase("W-height-minlift", 170);
            UdonBehaviour sel = null; int[] orig = null; int to0 = 0, rideN = 0;
            float rest = 0f, fitted = 0f, t0 = 0f, emptyAt = -1f, anchorAboveGround = 0f, keepOut = 0f, limit = 0f;
            float liftMin = 0f, restAt = -1f, ownAt = -1f, groundAt = -1f, outAt = -1f, belowMin = 0f, maxGap = 0f, maxExcess = -1f, prevY = 0f, prevT = 0f, freeAt = -1f;
            s.Add(WsDo(() =>
            {
                wsFailDump = WcDump;
                WhBind();
                if (wcStation == null || whCall != wcStation) throw new InvalidOperationException("station 0's KH_WashStationCall is not " + WhStationName + "/Call");
                var t = whStation.Find("PremiumTools");
                sel = t != null ? t.GetComponent<UdonBehaviour>() : null;
                if (sel == null || sel.programSource == null || sel.programSource.name != "KH_WashSelection") throw new WsSkip(WhStationName + ": no premium selection (IMP-DOG-046)");
                object lifts; if (!sel.TryGetProgramVariable("bathMinLifts", out lifts) || !(lifts is float[] la) || la.Length <= PPool) throw new WsSkip(WhStationName + ": bathMinLifts not written (WashPremium/50 not applied)");
                WsExpect(WsRef(sel, "call") as UdonBehaviour == whCall, "the selection's call is " + WhStationName + "/Call (WashPremium/50 wiring)");
                orig = new[] { Get<int>(sel, "selectedFoam"), Get<int>(sel, "selectedWater"), Get<int>(sel, "selectedFinish"), Get<int>(sel, "selectedBath") };
                WsNote("selection at the start: foam " + orig[0] + " water " + orig[1] + " finish " + orig[2] + " bath " + orig[3] + " (put back at the end)");
            }));
            WcSetup(s);   // the Shiba free and by the gate, no station holds it, me beside the stand
            s.Add(WsDo(PremiumBind));   // nothing held, every category and the platform free
            s.Add(WsUntil(() => WhLiftY() < 0.005f, 3, () => "the empty stand did not come down to 0 on the free table (" + WhLiftY().ToString("F3") + ")"));
            s.Add(WsUntil(PremiumNear, 3, () => "tester within the selector's 6 m"));
            s.Add(WsDo(() => PremiumTake(PPool)));
            s.Add(PremiumAck("pool"));
            s.Add(WsDo(() =>
            {
                WsExpect(PremiumCode() == KH_WashSelection.ResultApplied && PremiumSel("selectedBath") == PPool && PremiumSel("_shownBath") == PPool && PremiumModel(PPool).activeSelf, "pool via the board, local owner replied success (" + PremiumState() + ")");
                fitted = Get<float[]>(sel, "bathMinLifts")[PPool];
                WsExpect(Mathf.Abs(fitted - WhPoolMinLiftFitted) <= 0.005f, "bathMinLifts[pool] at the main station = the 2026-10-11 fit " + WhPoolMinLiftFitted + " +- 0.005 (" + fitted.ToString("F4") + ")");
                WsExpect(Mathf.Abs(Get<float>(whCall, "minLift") - fitted) < 1e-4f, "_Apply wrote Call.minLift (" + Get<float>(whCall, "minLift").ToString("F4") + ")");
                HwInvoke(whCall, "_LiftRest"); rest = HwRet<float>(whCall, "_LiftRest");
                WsExpect(Mathf.Abs(rest - fitted) < 1e-4f, "_LiftRest() = minLift (" + rest.ToString("F4") + ")");
                WsExpect(Get<int>(whCall, "heldDog") < 0 && Get<float>(whCall, "lift") == 0f, "no dog, synced lift 0");
                t0 = Time.time;
            }));
            s.Add(WsUntil(() => { if (Mathf.Abs(WhLiftY() - rest) < 0.005f) { emptyAt = Time.time - t0; return true; } return false; }, 3,
                () => "the empty stand did not ease to _LiftRest " + rest.ToString("F3") + " (liftRoot " + WhLiftY().ToString("F3") + ", _liftNow " + Get<float>(whCall, "_liftNow").ToString("F3") + ")"));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<float>(whCall, "lift") == 0f, "the synced lift stays 0 while the stand rests at minLift");
                WsNote("pool selected: minLift " + fitted.ToString("F4") + " (fit " + WhPoolMinLiftFitted + "), the empty stand eased 0 -> " + WhLiftY().ToString("F3") + " m in " + emptyAt.ToString("F2") + " s (liftSpeed " + Get<float>(whCall, "liftSpeed") + " m/s), lift 0");
                WcPlayerAtStation();
            }));
            WcCallAndArrive(s, wsDog, 60);
            s.Add(WsWait(1.0));
            s.Add(WsDo(() =>
            {
                float dy = wsRoot.position.y - wcAnchor.position.y;
                anchorAboveGround = wcAnchor.position.y - KH_DogWashSetupEditor.StationGround(whStation, wcAnchor.position);
                WsExpect(Mathf.Abs(WhLiftY() - rest) < 0.005f && Get<float>(whCall, "lift") == 0f, "with the dog the stand stays at minLift (" + WhLiftY().ToString("F3") + ", lift " + Get<float>(whCall, "lift") + ")");
                WsExpect(Mathf.Abs(dy) < 0.03f && Flat(wsRoot.position - wcAnchor.position) < 0.05f, "the Shiba stands on the raised anchor (root - anchor y " + dy.ToString("F3") + ", flat " + Flat(wsRoot.position - wcAnchor.position).ToString("F3") + ")");
                WsNote("called: root - anchor y " + dy.ToString("F3") + " m, anchor " + anchorAboveGround.ToString("F3") + " m above the terrain under it");
                whBoard.SendCustomEvent("_LiftUp");
            }));
            s.Add(WsWait(0.25));
            s.Add(WsDo(() => whBoard.SendCustomEvent("_LiftUp")));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                WsExpect(Mathf.Abs(Get<float>(whCall, "lift") - 0.2f) < 1e-3f && Mathf.Abs(WhLiftY() - rest) < 0.005f, "lift 0.2 (below minLift) leaves the stand at minLift (lift " + Get<float>(whCall, "lift").ToString("F2") + ", stand " + WhLiftY().ToString("F3") + ")");
                whBoard.SendCustomEvent("_LiftUp");
            }));
            s.Add(WsUntil(() => Mathf.Abs(Get<float>(whCall, "lift") - 0.3f) < 1e-3f && Mathf.Abs(WhLiftY() - 0.3f) < 0.005f, 3,
                () => "_LiftUp x3: lift " + Get<float>(whCall, "lift").ToString("F2") + ", stand " + WhLiftY().ToString("F3") + " (want 0.3)"));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                float dy = wsRoot.position.y - wcAnchor.position.y;
                WsExpect(Mathf.Abs(dy) < 0.03f, "the Shiba rose with the stand to 0.3 (root - anchor y " + dy.ToString("F3") + ")");
                to0 = WrCount("_leaveTimeouts"); keepOut = WcKeepOut(); limit = WcLeaveLimit(0.3f, true);
                liftMin = Get<float>(whCall, "_liftNow"); prevY = wsRoot.position.y; prevT = Time.time; belowMin = float.MaxValue;
                whBoard.SendCustomEvent("_End");
                t0 = Time.time;
            }));
            s.Add(WsUntil(() => Get<int>(whCall, "heldDog") < 0, 2, () => "board _End did not release (heldDog " + Get<int>(whCall, "heldDog") + ")"));
            s.Add(now =>
            {
                float t = Time.time - t0, ln = Get<float>(whCall, "_liftNow");
                var p = wsRoot.position;
                liftMin = Mathf.Min(liftMin, ln);
                if (restAt < 0f && Mathf.Abs(ln - rest) < 0.003f) restAt = t;
                bool lowering = Get<bool>(wsWander, "_leaveLowering"), own = Get<bool>(wsWander, "_leaveOwnEase");
                if (lowering && !own) { rideN++; maxGap = Mathf.Max(maxGap, Mathf.Abs(p.y - wcAnchor.position.y)); }   // riding: the root is the anchor
                if (own && ownAt < 0f) ownAt = t;
                float ground = KH_DogWashSetupEditor.StationGround(whStation, p);
                belowMin = Mathf.Min(belowMin, p.y - ground);
                if (p.y - ground < -0.03f) throw new InvalidOperationException("the Shiba's root went " + (ground - p.y).ToString("F3") + " m below the terrain at +" + t.ToString("F2") + " s (lowering " + lowering + ", own ease " + own + ")");
                // a tick can span several frames: the drop is judged against liftSpeed x the game time it took (C-release round 3)
                float tick = Mathf.Max(0f, Time.time - prevT);
                maxExcess = Mathf.Max(maxExcess, (prevY - p.y) - Mathf.Max(0.05f, Get<float>(whCall, "liftSpeed")) * tick - 0.02f);
                prevY = p.y; prevT = Time.time;
                if (groundAt < 0f && !lowering && p.y - ground < 0.05f) groundAt = t;
                if (outAt < 0f && Flat(p - wcAnchor.position) >= keepOut) { outAt = t; return true; }
                if (t > limit) throw new TimeoutException("the Shiba was still inside the keep-out " + limit.ToString("F1") + " s after おわり (" + Flat(p - wcAnchor.position).ToString("F2") + " / " + keepOut + " m, lowering " + lowering + ", own ease " + own + ", stand " + ln.ToString("F3") + ", root above terrain " + (p.y - ground).ToString("F3") + ")");
                return false;
            });
            s.Add(WsDo(() =>
            {
                float lowerT = (0.3f - rest) / Mathf.Max(0.05f, Get<float>(whCall, "liftSpeed"));
                WsExpect(Get<float>(whCall, "lift") == 0f && !Get<bool>(wsWander, "_stationLocked"), "released: lift 0, the dog side unlocked");
                WsExpect(liftMin >= rest - 0.002f, "the stand never went below minLift on the way down (min " + liftMin.ToString("F3") + ", rest " + rest.ToString("F3") + ")");
                WsExpect(restAt >= 0f && restAt <= lowerT + 0.5f, "the stand reached minLift " + (restAt < 0f ? "never" : "+" + restAt.ToString("F2") + " s") + " (0.3 -> " + rest.ToString("F3") + " = " + lowerT.ToString("F2") + " s)");
                WsExpect(rideN > 0, "the ride down with the stand was observed (0 samples with _leaveLowering && !_leaveOwnEase: the gap check would be vacuous)");
                WsExpect(maxGap < 0.03f, "the root stayed on the anchor while the stand came down (largest gap " + maxGap.ToString("F3") + " m over " + rideN + " samples)");
                WsExpect(ownAt >= 0f, "from minLift the dog eased down by itself (_leaveOwnEase seen)");
                WsExpect(groundAt >= 0f && groundAt <= outAt, "the dog reached the ground before leaving the keep-out (" + (groundAt < 0f ? "never" : "+" + groundAt.ToString("F2") + " s") + ")");
                WsExpect(maxExcess <= 0f, "no snap: largest drop beyond liftSpeed x tick + 0.02 = " + maxExcess.ToString("F3") + " m");
                WsExpect(WrCount("_leaveTimeouts") == to0, "the walk out did not time out (" + to0 + " -> " + WrCount("_leaveTimeouts") + ")");
                WsNote("おわり from 0.3: stand back to minLift " + rest.ToString("F3") + " at +" + restAt.ToString("F2") + " s (min " + liftMin.ToString("F3") + "), root on the anchor while riding (gap " + maxGap.ToString("F3") + " m, " + rideN + " samples), own ease from +" + ownAt.ToString("F2") + " s, on the ground +" + groundAt.ToString("F2") + " s, out of the " + keepOut + " m keep-out +" + outAt.ToString("F2") + " s (limit " + limit.ToString("F1") + "), lowest root - terrain " + belowMin.ToString("F3") + " m, drop excess " + maxExcess.ToString("F3"));
            }));
            s.Add(WsWait(0.5));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<int>(whCall, "heldDog") < 0 && Mathf.Abs(WhLiftY() - rest) < 0.005f, "the empty stand stays at minLift with the pool (" + WhLiftY().ToString("F3") + ")");
                Networking.LocalPlayer.TeleportTo(whStation.TransformPoint(new Vector3(.8f, .1f, -.8f)), whStation.rotation);   // PremiumBind's spot (within the selector's 6 m)
            }));
            s.Add(WsUntil(PremiumNear, 3, () => "tester within the selector's 6 m"));
            s.Add(WsWait(PremiumGap));
            s.Add(WsDo(PremiumReset));
            s.Add(PremiumAck("reset"));
            s.Add(WsDo(() =>
            {
                WsExpect(PremiumCode() == KH_WashSelection.ResultApplied && PremiumSel("selectedBath") == -1 && Get<float>(whCall, "minLift") == 0f, "board reset: the platform free and minLift 0 (" + PremiumState() + ", minLift " + Get<float>(whCall, "minLift") + ")");
                t0 = Time.time;
            }));
            s.Add(WsUntil(() => { if (WhLiftY() < 0.005f) { freeAt = Time.time - t0; return true; } return false; }, 3, () => "the stand did not come back to 0 after the reset (" + WhLiftY().ToString("F3") + ")"));
            s.Add(WsDo(() =>
            {
                WsNote("board reset -> free table, minLift 0, the stand " + rest.ToString("F3") + " -> 0 in " + freeAt.ToString("F2") + " s");
                string[] keys = { "selectedFoam", "selectedWater", "selectedFinish", "selectedBath" };
                for (int k = 0; k < 4; k++) premiumSelection.SetProgramVariable(keys[k], orig[k]);
                premiumSelection.SetProgramVariable("revision", PremiumSel("revision") + 1);
                HwInvoke(premiumSelection, "_Apply", "animate", false);
                WsExpect(PremiumSel("selectedBath") == orig[3] && PremiumSel("_shownBath") == orig[3], "the selection put back as at the start (" + PremiumState() + ")");
                PremiumRecover();
            }));
        }
    }
}
