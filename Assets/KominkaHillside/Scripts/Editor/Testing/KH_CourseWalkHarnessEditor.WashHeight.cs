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
                WsExpect(cols.Length == 4 && Array.TrueForAll(cols, c => c != null && c.enabled), "the 4 floor colliders are on above 0");
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

        /// <summary>My floor back to 0 at once (Down x 12; the height eases down at 0.5 m/s by itself).</summary>
        static void WhFloorZero() { for (int i = 0; i < 12; i++) whPf.SendCustomEvent("_Down"); }
    }
}
