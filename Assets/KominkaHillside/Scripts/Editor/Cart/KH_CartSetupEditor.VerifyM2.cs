using System;
using System.Linq;
using System.Reflection;
using KominkaDogRun;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>M2 の Verify (Cart/2) と Probe (Cart/3): 道幅の分類・待避点・交通の同期配列・調整値・掲示画像・犬とカメラの cartManager、交通判定 6 ケース。シーンは変えない。</summary>
    public static partial class KH_CartSetupEditor
    {
        static void M2ManagerCheck(V v, KH_CartManager manager, SerializedObject mso, UdonBehaviour mb, KH_WalkRoute route, KH_LeashManager leash)
        {
            Tuning tuning;
            try { tuning = LoadTuning(); } catch (Exception ex) { v.Req(false, "tuning.json: " + ex.Message); return; }
            var track = BuildTrack(route);
            // ---- 道幅の分類 (保存値と、Verify 時点の NavMesh での再計算) ----
            var cls = mso.FindProperty("trackClass");
            int m = track.positions.Length;
            v.Req(cls != null && cls.isArray && cls.arraySize == m, "manager.trackClass length = " + m + " segments (actual " + (cls != null && cls.isArray ? cls.arraySize : -1) + ")");
            var from = ReadFloats(mso, "narrowFrom"); var to = ReadFloats(mso, "narrowTo"); var wb = ReadFloats(mso, "waitBefore"); var wa = ReadFloats(mso, "waitAfter");
            bool pairs = from != null && to != null && wb != null && wa != null && from.Length == to.Length && wb.Length == from.Length && wa.Length == from.Length;
            v.Req(pairs, "narrowFrom / narrowTo / waitBefore / waitAfter have the same length (" + (from != null ? from.Length : -1) + ")");
            if (cls != null && cls.isArray && cls.arraySize == m && pairs)
            {
                var c = new int[m]; for (int i = 0; i < m; i++) c[i] = cls.GetArrayElementAtIndex(i).intValue;
                var want = ClassifyTrack(track, tuning.gap);
                v.Req(c.SequenceEqual(want.cls), "trackClass matches the NavMesh width classification now (Cart/1 after NavMesh/route changes)");
                // 橋 (linkAllowsDetour = false) は class 2 で、2 本以上の narrow 区間に入る
                int bridgeZones = 0;
                for (int z = 0; z < from.Length; z++)
                {
                    bool hasBridge = false;
                    for (int i = 0; i < m && !hasBridge; i++) if (!track.detour[i]) { float mid = (track.distances[i] + (i + 1 < m ? track.distances[i + 1] : track.length)) * .5f; hasBridge = InZone(track, from[z], to[z], mid); }
                    if (hasBridge) bridgeZones++;
                }
                v.Req(Enumerable.Range(0, m).Where(i => !track.detour[i]).All(i => c[i] == 2), "every no-detour (bridge) segment is class 2");
                v.Req(bridgeZones >= 2, "narrow zones include the 2 bridges (zones with a bridge: " + bridgeZones + " of " + from.Length + ")");
                float back = WaitExtra + tuning.gap * ConvoyCars;
                for (int z = 0; z < from.Length; z++)
                {
                    foreach (var (w, edge, sign, what) in new[] { (wb[z], from[z], -1, "waitBefore (+1)"), (wa[z], to[z], +1, "waitAfter (-1)") })
                    {
                        if (w < 0f) { v.Req(false, "zone " + z + " " + what + " found (-1)"); continue; }
                        int seg = TrackSegment(track, w);
                        bool insideAny = Enumerable.Range(0, from.Length).Any(k => InZone(track, from[k], to[k], w));
                        float dist = sign < 0 ? TrackWrap(track, edge - w) : TrackWrap(track, w - edge);
                        v.Req(c[seg] <= 1 && !insideAny && dist >= back - .01f, "zone " + z + " " + what + " at " + w.ToString("F1") + ": class " + c[seg] + ", outside every zone, " + dist.ToString("F1") + " m from the edge (>= " + back.ToString("F1") + ")");
                    }
                }
            }
            // ---- 交通の同期配列 (宣言時に長さ 8) ----
            foreach (var name in new[] { "groupLane", "groupHoldDist", "groupHoldSinceMs" })
            {
                var p = mso.FindProperty(name);
                v.Req(p != null && p.isArray && p.arraySize == MaxGroups, "manager." + name + " length " + MaxGroups + " (proxy " + (p != null && p.isArray ? p.arraySize : -1) + ")");
                if (mb != null && mb.publicVariables.TryGetVariableValue(name, out object bv)) v.Req(bv is Array a && a.Length == MaxGroups, "manager." + name + " length " + MaxGroups + " (backing)");
            }
            // ---- 調整値 (proxy と backing) ----
            foreach (var f in TuningFields)
            {
                var p = mso.FindProperty(f); float want = TuningValue(tuning, f);
                if (p == null || p.propertyType != SerializedPropertyType.Float) { v.Req(false, "manager." + f + " exists (tuning)"); continue; }
                v.Req(Mathf.Abs(p.floatValue - want) < 1e-4f, "manager." + f + " = tuning " + want + " (proxy " + p.floatValue + ")");
                if (mb != null && mb.publicVariables.TryGetVariableValue(f, out object bv)) v.Req(bv is float bf && Mathf.Abs(bf - want) < 1e-4f, "manager." + f + " = tuning " + want + " (backing)");
            }
            // ---- 犬 6 匹と撮影カメラの cartManager ----
            int dogs = 0, dogOk = 0, dogNoField = 0;
            foreach (var d in UnityEngine.Object.FindObjectsOfType<KD_DogWanderController>(true))
            {
                dogs++;
                var so = new SerializedObject(d); var p = so.FindProperty("cartManager");
                if (p == null) { dogNoField++; continue; }
                var db = UdonSharpEditorUtility.GetBackingUdonBehaviour(d);
                bool backingOk = db == null || !db.publicVariables.TryGetVariableValue("cartManager", out object bv) || ReferenceEquals(bv, UdonSharpEditorUtility.GetBackingUdonBehaviour(manager));
                if (p.objectReferenceValue == manager && backingOk) dogOk++;
            }
            if (dogNoField > 0) v.Pend("KD_DogWanderController.cartManager not found on " + dogNoField + " dogs (Dog M2 not merged)");
            else v.Req(dogs == 6 && dogOk == 6, "6 dogs have cartManager wired (dogs " + dogs + ", wired " + dogOk + ")");
            var videos = UnityEngine.Object.FindObjectsOfType<KH_VideoCamera>(true);
            if (videos.Length == 1)
            {
                var vso = new SerializedObject(videos[0]);
                if (vso.FindProperty("cartManager") == null) v.Pend("KH_VideoCamera.cartManager not found (Camera M2 not merged)");
                else RefCheck(v, vso, UdonSharpEditorUtility.GetBackingUdonBehaviour(videos[0]), "cartManager", manager, "video");
            }
        }

        static void SignImageCheck(V v)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(SignOutPath);
            v.Req(tex != null && tex.width == SignWidth && tex.height == SignHeight, SignOutPath + " is " + SignWidth + "x" + SignHeight + " (actual " + (tex != null ? tex.width + "x" + tex.height : "missing") + ")");
            var ti = AssetImporter.GetAtPath(SignOutPath) as TextureImporter;
            if (ti != null)
            {
                var ps = ti.GetPlatformTextureSettings("Android");
                v.Req(ti.sRGBTexture && !ti.isReadable && ps.overridden && ps.format == TextureImporterFormat.ASTC_6x6, SignOutPath + " import sRGB, Read/Write off, Android ASTC 6x6");
            }
        }

        // ============================================================
        // Probe: 交通判定 6 ケース (KH_CartManager.TrafficDecide / TrafficHoldDistance、Reflection)
        // ============================================================

        static void ProbeTraffic(P pr, KH_CartManager mgr)
        {
            var t = mgr.GetType(); const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var decide = t.GetMethod("TrafficDecide", F, null, new[] { typeof(float), typeof(int), typeof(int), typeof(int), typeof(float), typeof(int), typeof(int), typeof(int), typeof(float), typeof(float) }, null);
            var hold = t.GetMethod("TrafficHoldDistance", F, null, new[] { typeof(float), typeof(int), typeof(int) }, null);
            if (decide == null) { pr.Skip("KH_CartManager.TrafficDecide(10 args) not found: traffic cases skipped"); return; }
            var so = new SerializedObject(mgr);
            var from = ReadFloats(so, "narrowFrom"); var to = ReadFloats(so, "narrowTo"); var wb = ReadFloats(so, "waitBefore"); var wa = ReadFloats(so, "waitAfter");
            var lenP = so.FindProperty("trackLength"); float L = lenP != null ? lenP.floatValue : 0f;
            var gapP = so.FindProperty("gap"); float gap = gapP != null ? gapP.floatValue : 3f;
            if (from == null || from.Length == 0 || L <= 0f) { pr.Skip("no narrow zone saved (run Cart/1): traffic cases skipped"); return; }
            var cache = t.GetField("_routeCached", F); if (cache != null && cache.FieldType == typeof(bool)) cache.SetValue(mgr, false);
            float zf = from[0], zt = to[0];
            float W(float d) { d %= L; return d < 0f ? d + L : d; }
            int D(float myLead, int myDir, int myCount, int myGroup, float oLead, int oDir, int oCount, int oGroup) { return (int)decide.Invoke(mgr, new object[] { W(myLead), myDir, myCount, myGroup, W(oLead), oDir, oCount, oGroup, zf, zt }); }
            pr.r.traffic.Add("zone 0 = " + zf.ToString("F1") + "-" + zt.ToString("F1") + " m, gap " + gap);
            try
            {
                float mid = W(zf + ZoneLengthRaw(zf, zt, L) * .5f);
                // 1 片方が区間内、もう片方 (逆向き) が接近中 → 接近側が待つ (1)
                int c1 = D(zf - 10f, +1, 1, 0, mid, -1, 1, 1); pr.Req(c1 == 1, "traffic 1: other inside, me approaching -> wait (got " + c1 + ")");
                // 2 両方接近中 10 m 対 20 m → 遠い方 (20 m) が待つ
                int c2far = D(zf - 20f, +1, 1, 0, zt + 10f, -1, 1, 1), c2near = D(zt + 10f, -1, 1, 1, zf - 20f, +1, 1, 0);
                pr.Req(c2far == 1 && c2near == 0, "traffic 2: both approaching 20 m vs 10 m -> farther waits (far " + c2far + ", near " + c2near + ")");
                // 3 同距離 → group 番号が大きい方が待つ
                int c3big = D(zf - 15f, +1, 1, 5, zt + 15f, -1, 1, 2), c3small = D(zt + 15f, -1, 1, 2, zf - 15f, +1, 1, 5);
                pr.Req(c3big == 1 && c3small == 0, "traffic 3: same distance -> larger group number waits (g5 " + c3big + ", g2 " + c3small + ")");
                // 4 相手 (順方向に出て行く) の最後尾が区間を出て 2 m 超 → 進む (0)。最後尾 = lead - dir*(count-1)*gap
                float tailOut = zt + 2.5f; int c4 = D(zt + 10f, -1, 1, 0, tailOut + (3 - 1) * gap, +1, 3, 1);
                pr.Req(c4 == 0, "traffic 4: other's tail 2.5 m past the zone -> go (got " + c4 + ")");
                // 5 同じ向き → 0
                int c5 = D(zf - 10f, +1, 1, 0, mid, +1, 1, 1); pr.Req(c5 == 0, "traffic 5: same direction -> go (got " + c5 + ")");
                // 6 自分が区間内 → 2
                int c6 = D(mid, +1, 1, 0, zt + 5f, -1, 1, 1); pr.Req(c6 == 2, "traffic 6: me inside -> go through (got " + c6 + ")");
                pr.r.traffic.Add("cases: " + c1 + " / " + c2far + "," + c2near + " / " + c3big + "," + c3small + " / " + c4 + " / " + c5 + " / " + c6);
                if (hold != null)
                {
                    float h1 = (float)hold.Invoke(mgr, new object[] { W(wb[0] - 30f), +1, 0 }), h2 = (float)hold.Invoke(mgr, new object[] { W(wa[0] + 30f), -1, 0 }), h3 = (float)hold.Invoke(mgr, new object[] { 0f, +1, from.Length });
                    pr.Req(Mathf.Abs(h1 - wb[0]) < 1e-3f && Mathf.Abs(h2 - wa[0]) < 1e-3f && h3 == -1f, "TrafficHoldDistance: +1 -> waitBefore " + wb[0].ToString("F1") + " (" + h1.ToString("F1") + "), -1 -> waitAfter " + wa[0].ToString("F1") + " (" + h2.ToString("F1") + "), bad zone -> -1 (" + h3 + ")");
                    float past = W(wb[0] + 1f), h4 = (float)hold.Invoke(mgr, new object[] { past, +1, 0 });
                    pr.Req(Mathf.Abs(h4 - past) < 1e-3f, "TrafficHoldDistance: lead already past the wait point -> stop where it is (" + h4.ToString("F1") + ")");
                }
                else pr.Skip("KH_CartManager.TrafficHoldDistance(float,int,int) not found");
            }
            catch (TargetInvocationException ex) { pr.Req(false, "traffic function threw in Edit mode: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }
        }

        static float ZoneLengthRaw(float from, float to, float L) { float l = to - from; return l < 0f ? l + L : l; }
    }
}
