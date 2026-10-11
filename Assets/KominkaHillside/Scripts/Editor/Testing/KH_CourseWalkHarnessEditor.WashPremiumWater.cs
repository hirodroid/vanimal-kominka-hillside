using System;
using System.Linq;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-049 (2026-10-11): the water pistol (one shot) and the hose (hold to spray) inputs — Course/100 group premium-water
    /// (menu 100p4, also part of 100p). Wiring and helpers are WashPremium.cs (PremiumBind / PremiumTake / PremiumAck ...).
    ///
    /// The holder's own events (_onPickup / _onPickupUseDown / _onPickupUseUp / _onDrop) are sent to the water slot's KH_WashTool, the way
    /// P-selection-water-tilt and W-tool-toggle do (ClientSim is one Desktop player and the Owner of the tool and its State). Read: the tool's
    /// _sprayOn / _sprayShown / _pulseUntil / _shotShownUntil, the State's spraying / shotSerial / shotItem, the slot's spray ParticleSystem.
    ///
    /// Not covered here: the other players' burst (KH_WashToolState.OnDeserialization -> _OnShot is called directly as the receiver would),
    /// the late joiner's first state (record only), shots merged by Manual sync, the real trigger / VR hand -> two clients / PCVR.</summary>
    public static partial class KH_CourseWalkHarnessEditor
    {
        static void PremiumWaterCases()
        {
            // ---- P-pistol-shot: one press = one burst (shotSerial +1, shotItem 17, spraying never set), a press during the shot is ignored,
            //      the next press after pulseSeconds fires again, drop leaves nothing, a received shot plays only while the slot shows that item ----
            var s = WsCase("P-pistol-shot", 45);
            UdonBehaviour st = null; ParticleSystem ps = null;
            int serial0 = 0, peak = 0; float burst = 0f, pressAt = 0f, pulseUntil1 = 0f, shotLen = -1f, pulse = 0f; bool sprayedBit = false;
            Func<string> what = () => "(_sprayOn " + Get<bool>(premiumWater, "_sprayOn") + ", shown " + Get<bool>(premiumWater, "_sprayShown") + ", spraying " + Get<bool>(st, "spraying")
                + ", shotSerial " + Get<int>(st, "shotSerial") + ", shotItem " + Get<int>(st, "shotItem") + ", emitting " + (ps != null && ps.isEmitting) + ", particles " + (ps != null ? ps.particleCount : -1) + ")";
            s.Add(WsDo(PremiumBind));
            s.Add(WsUntil(PremiumNear, 3, () => "tester within the selector's 6 m"));
            s.Add(WsDo(() => PremiumTake(PWaterPistol)));
            s.Add(PremiumAck("water pistol"));
            s.Add(WsDo(() =>
            {
                WsExpect(PremiumCode() == KH_WashSelection.ResultApplied && PremiumSel("selectedWater") == PWaterPistol && PremiumShown()[1] == PWaterPistol && PremiumPickupable(PWaterPistol), "water pistol via UI, local owner replied success (" + PremiumState() + ")");
                WsExpect(PremiumSlotTool(PWaterPistol) == premiumWater, "the pistol is on the water slot (the can's slot)");
                pulse = Get<float>(premiumWater, "pulseSeconds");
                WsExpect(pulse > 0f, "water slot fires one shot per press for the pistol (pulseSeconds " + pulse + ")");
                WsExpect(WsRef(premiumWater, "nozzle") as Transform == (WsRef(premiumSelection, "nozzleAnchors") as Array).GetValue(PWaterPistol) as Transform, "water slot configured with the pistol's nozzle");
                st = WsRef(premiumWater, "state") as UdonBehaviour; ps = WsRef(premiumWater, "spray") as ParticleSystem;
                WsExpect(st != null && Networking.IsOwner(st.gameObject), "the water slot's State is owned by the local player (a shot is written at once)");
                WsExpect(ps != null, "the water slot has the pistol's spray (UseMist)");
                var em = ps.emission; var bs = new ParticleSystem.Burst[em.burstCount]; em.GetBursts(bs); burst = bs.Sum(b => b.count.constant);
                WsNote("pistol: pulseSeconds " + pulse + ", spray loop " + ps.main.loop + " duration " + ps.main.duration.ToString("F2") + " s, bursts " + bs.Length + " = " + burst + " particles, rate " + em.rateOverTimeMultiplier + "/s, lifetime " + ps.main.startLifetimeMultiplier.ToString("F2") + " s, max " + ps.main.maxParticles);
                WsExpect(!Get<bool>(premiumWater, "_sprayShown") && !ps.isEmitting, "nothing sprays before the press " + what());
                premiumWater.SendCustomEvent("_onPickup");
                serial0 = Get<int>(st, "shotSerial");
                premiumWater.SendCustomEvent("_onPickupUseDown");
                pressAt = Time.time; pulseUntil1 = Get<float>(premiumWater, "_pulseUntil");
                WsExpect(Get<bool>(premiumWater, "_sprayOn") && Get<bool>(premiumWater, "_sprayShown"), "Use down: the shot is on " + what());
                WsExpect(Get<int>(st, "shotSerial") == serial0 + 1 && Get<int>(st, "shotItem") == PWaterPistol, "Use down: shotSerial +1 (" + serial0 + " -> " + Get<int>(st, "shotSerial") + ") with shotItem " + PWaterPistol + " " + what());
                WsExpect(!Get<bool>(st, "spraying"), "a shot does not set the spraying bit " + what());
            }));
            // 0.2 s: past toggleGuardSeconds (0.15), inside the shot (pulseSeconds 0.4): the second press is ignored
            s.Add(now => { peak = Mathf.Max(peak, ps.particleCount); sprayedBit |= Get<bool>(st, "spraying"); return Time.time - pressAt >= 0.2f; });
            s.Add(WsDo(() =>
            {
                premiumWater.SendCustomEvent("_onPickupUseDown");
                WsExpect(Get<int>(st, "shotSerial") == serial0 + 1 && Get<float>(premiumWater, "_pulseUntil") == pulseUntil1, "a press " + (Time.time - pressAt).ToString("F2") + " s into the shot is ignored (serial " + Get<int>(st, "shotSerial") + ", pulseUntil unchanged) " + what());
            }));
            s.Add(now =>
            {
                peak = Mathf.Max(peak, ps.particleCount); sprayedBit |= Get<bool>(st, "spraying");
                if (!Get<bool>(premiumWater, "_sprayOn")) { shotLen = Time.time - pressAt; return true; }
                if (Time.time - pressAt > 1.5f) throw new TimeoutException("the shot did not end 1.5 s after the press " + what());
                return false;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(shotLen >= pulse - 0.02f && shotLen <= pulse + 0.15f, "the shot lasts pulseSeconds (" + shotLen.ToString("F3") + " s vs " + pulse + ")");
                WsExpect(!sprayedBit && !Get<bool>(st, "spraying"), "the spraying bit stayed false through the shot");
                WsExpect(peak > 0 && (burst <= 0f || peak <= burst + 0.5f), "one burst: peak " + peak + " live particles (one burst = " + burst + ")");
                WsExpect(!Get<bool>(premiumWater, "_sprayShown") && !ps.isEmitting, "after the shot nothing emits " + what());
            }));
            s.Add(WsWait(0.05));
            s.Add(WsDo(() =>
            {
                premiumWater.SendCustomEvent("_onPickupUseDown");
                WsExpect(Get<int>(st, "shotSerial") == serial0 + 2 && Get<bool>(premiumWater, "_sprayOn"), "a press after the shot fires again (serial " + serial0 + " -> " + Get<int>(st, "shotSerial") + ", " + (Time.time - pressAt).ToString("F2") + " s after the first) " + what());
            }));
            s.Add(WsWait(0.1));
            s.Add(WsDo(() =>
            {
                premiumWater.SendCustomEvent("_onDrop");
                WsExpect(!Get<bool>(premiumWater, "_sprayOn") && !Get<bool>(premiumWater, "_sprayShown") && !Get<bool>(st, "spraying") && !ps.isEmitting, "drop during the second shot stops it at once " + what());
            }));
            s.Add(WsWait(0.8));   // past the burst's lifetime (0.55 s)
            s.Add(WsDo(() =>
            {
                WsExpect(!Get<bool>(premiumWater, "_sprayShown") && !ps.isEmitting && ps.particleCount == 0 && !Get<bool>(st, "spraying") && Get<int>(st, "shotSerial") == serial0 + 2, "nothing lingers 0.8 s after the drop " + what());
                // a received shot (the receiver's call; KH_WashToolState.OnDeserialization makes it) plays while the slot shows that item ...
                HwInvoke(premiumWater, "_OnShot", "item", PWaterPistol);
                WsExpect(Get<float>(premiumWater, "_shotShownUntil") > Time.time && Get<bool>(premiumWater, "_sprayShown"), "control: _OnShot(17) on the pistol slot (not held) shows one burst " + what());
            }));
            s.Add(WsWait(0.6));
            s.Add(WsDo(() =>
            {
                WsExpect(!Get<bool>(premiumWater, "_sprayShown") && !ps.isEmitting, "the received burst ends after pulseSeconds " + what());
                float until = Get<float>(premiumWater, "_shotShownUntil");
                HwInvoke(premiumWater, "_OnShot", "item", PHose);
                WsExpect(Get<float>(premiumWater, "_shotShownUntil") == until && !Get<bool>(premiumWater, "_sprayShown"), "_OnShot(18) (another item) on the pistol slot does nothing " + what());
            }));
            s.Add(WsWait(PremiumGap));
            s.Add(WsDo(() => PremiumTake(PHose)));
            s.Add(PremiumAck("hose after the pistol"));
            float staleUntil = 0f;
            s.Add(WsDo(() =>
            {
                WsExpect(PremiumCode() == KH_WashSelection.ResultApplied && PremiumSel("selectedWater") == PHose && Get<float>(premiumWater, "pulseSeconds") == KH_WashTool.PulseHold, "the water slot switched to the hose (" + PremiumState() + ", pulseSeconds " + Get<float>(premiumWater, "pulseSeconds") + ")");
                ps = WsRef(premiumWater, "spray") as ParticleSystem;
                staleUntil = Get<float>(premiumWater, "_shotShownUntil");
                HwInvoke(premiumWater, "_OnShot", "item", PWaterPistol);
                WsExpect(Get<float>(premiumWater, "_shotShownUntil") == staleUntil && !Get<bool>(premiumWater, "_sprayShown"), "a stale _OnShot(17) after the switch to the hose does nothing " + what());
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(!Get<bool>(premiumWater, "_sprayShown") && (ps == null || !ps.isEmitting) && !Get<bool>(st, "spraying"), "the hose stays off 0.3 s after the stale shot " + what());
                WsNote("press -> shot " + shotLen.ToString("F3") + " s (pulseSeconds " + pulse + "), shotSerial " + serial0 + " -> +1 with shotItem 17, spraying never true, peak " + peak + " particles (burst " + burst + "); press at +0.2 s ignored; press after the shot -> +2; drop stops at once, 0 particles 0.8 s later; received _OnShot(17) plays on the pistol slot, _OnShot(18) does not; after the switch to the hose a stale _OnShot(17) does nothing. Simulated pickup / Use / drop events; the other players' receive is the direct _OnShot call");
                PremiumClear(); PremiumRecover();
            }));

            // ---- P-hose-hold: on while Use is held, off on Use up and on drop ----
            s = WsCase("P-hose-hold", 30);
            UdonBehaviour hs = null; ParticleSystem hp = null; int hSerial0 = 0, hParticles = 0; float hDownAt = 0f, hOffAt = 0f; bool hDropped = false;
            Func<string> hWhat = () => "(_sprayOn " + Get<bool>(premiumWater, "_sprayOn") + ", shown " + Get<bool>(premiumWater, "_sprayShown") + ", spraying " + Get<bool>(hs, "spraying") + ", emitting " + (hp != null && hp.isEmitting) + ", particles " + (hp != null ? hp.particleCount : -1) + ")";
            s.Add(WsDo(PremiumBind));
            s.Add(WsUntil(PremiumNear, 3, () => "tester within the selector's 6 m"));
            s.Add(WsDo(() => PremiumTake(PHose)));
            s.Add(PremiumAck("hose"));
            s.Add(WsDo(() =>
            {
                WsExpect(PremiumCode() == KH_WashSelection.ResultApplied && PremiumSel("selectedWater") == PHose && PremiumShown()[1] == PHose && PremiumPickupable(PHose), "hose via UI, local owner replied success (" + PremiumState() + ")");
                WsExpect(Get<float>(premiumWater, "pulseSeconds") == KH_WashTool.PulseHold, "water slot sprays while Use is held for the hose (pulseSeconds " + Get<float>(premiumWater, "pulseSeconds") + ")");
                WsExpect(WsRef(premiumWater, "nozzle") as Transform == (WsRef(premiumSelection, "nozzleAnchors") as Array).GetValue(PHose) as Transform, "water slot configured with the hose's nozzle");
                hs = WsRef(premiumWater, "state") as UdonBehaviour; hp = WsRef(premiumWater, "spray") as ParticleSystem;
                WsExpect(hs != null && Networking.IsOwner(hs.gameObject) && hp != null, "the State is owned locally and the hose's spray is wired");
                hSerial0 = Get<int>(hs, "shotSerial");
                premiumWater.SendCustomEvent("_onPickup");
                WsExpect(!Get<bool>(premiumWater, "_sprayOn") && !Get<bool>(hs, "spraying"), "picked up: off " + hWhat());
                premiumWater.SendCustomEvent("_onPickupUseDown");
                hDownAt = Time.time;
                WsExpect(Get<bool>(premiumWater, "_sprayOn") && Get<bool>(hs, "spraying") && Get<bool>(premiumWater, "_sprayShown"), "Use down: spraying at once " + hWhat());
            }));
            s.Add(now =>
            {
                if (!Get<bool>(premiumWater, "_sprayOn") || !Get<bool>(hs, "spraying")) throw new InvalidOperationException("the hose stopped by itself " + (Time.time - hDownAt).ToString("F2") + " s into the hold " + hWhat());
                hParticles = Mathf.Max(hParticles, hp.particleCount);
                return Time.time - hDownAt >= 0.6f;
            });
            s.Add(WsDo(() =>
            {
                WsExpect(hp.isEmitting && hParticles > 0, "still emitting at +" + (Time.time - hDownAt).ToString("F2") + " s (peak " + hParticles + " particles) " + hWhat());
                WsExpect(Get<int>(hs, "shotSerial") == hSerial0, "the hose does not use the shot serial (" + hSerial0 + " -> " + Get<int>(hs, "shotSerial") + ")");
                premiumWater.SendCustomEvent("_onPickupUseUp");
                hOffAt = Time.time;
                WsExpect(!Get<bool>(premiumWater, "_sprayOn") && !Get<bool>(hs, "spraying") && !Get<bool>(premiumWater, "_sprayShown") && !hp.isEmitting, "Use up: off in the same frame " + hWhat());
            }));
            s.Add(WsWait(0.1));   // a frame or two later it has not come back
            s.Add(WsDo(() =>
            {
                WsExpect(!Get<bool>(premiumWater, "_sprayOn") && !Get<bool>(hs, "spraying") && !hp.isEmitting, "still off " + (Time.time - hOffAt).ToString("F2") + " s after Use up " + hWhat());
                premiumWater.SendCustomEvent("_onPickupUseDown");
                WsExpect(Get<bool>(premiumWater, "_sprayOn") && Get<bool>(hs, "spraying"), "Use down again: on " + hWhat());
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(Get<bool>(premiumWater, "_sprayOn") && Get<bool>(hs, "spraying"), "held 0.3 s: still on " + hWhat());
                premiumWater.SendCustomEvent("_onDrop"); hDropped = true;
                WsExpect(!Get<bool>(premiumWater, "_sprayOn") && !Get<bool>(hs, "spraying") && !Get<bool>(premiumWater, "_sprayShown") && !hp.isEmitting, "drop while held: off at once " + hWhat());
            }));
            s.Add(WsWait(0.3));
            s.Add(WsDo(() =>
            {
                WsExpect(hDropped && !Get<bool>(hs, "spraying") && !Get<bool>(premiumWater, "_sprayShown") && !hp.isEmitting, "0.3 s after the drop: off " + hWhat());
                WsNote("hose: Use down -> spraying at once, held 0.6 s without a stop (peak " + hParticles + " particles), Use up -> off in the same frame and still off 0.1 s later, shotSerial unchanged; Use down again -> on, drop while held -> off at once. Simulated pickup / Use / drop events");
                PremiumClear(); PremiumRecover();
            }));
        }
    }
}
