using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.Udon;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-WLD-013 の Verify (Cart/2 の一部): KH_CartManager.Voice.cs の配線・既定値・調整値の範囲と、相手の選び方 (VoiceTargets) の Edit 検査。
    /// シーンは変えない。聞こえ方は証明しない (ClientSim では相手の端末の聞こえ方が出ない。複数人の実機で聴取する。設定値の検査だけを合格にしない)。</summary>
    public static partial class KH_CartSetupEditor
    {
        const string VoiceSourcePath = ProgramDir + "/KH_CartManager.cs";
        const float VoiceNearMargin = 2f;    // 隊列の最大長 (MaxCarts - 1) x gap への余裕 (横ずれ・カーブ・追いつきの _err)
        const float VoiceFarCap = 100f;      // 同じグループ内の可聴距離の上限 (far は各端末がその相手の声を聞く距離。グループ外の人の聞こえ方は変えない)

        static void VoiceCheck(V v, KH_CartManager manager, SerializedObject mso, UdonBehaviour mb)
        {
            // ---- VRChat の既定値 (戻す値) ----
            v.Req(KH_CartManager.VoiceDefaultNear == 0f && KH_CartManager.VoiceDefaultFar == 25f && KH_CartManager.VoiceDefaultGain == 15f && KH_CartManager.VoiceDefaultLowpass,
                "voice: restore defaults = VRChat near 0 / far 25 / gain 15 / lowpass on");

            // ---- 調整値 (proxy と backing) ----
            float near = VoiceFloat(v, mso, mb, "voiceNear", 25f);
            float far = VoiceFloat(v, mso, mb, "voiceFar", 40f);
            float gain = VoiceFloat(v, mso, mb, "voiceGain", 15f);
            var lp = mso.FindProperty("voiceLowpass");
            v.Req(lp != null && lp.propertyType == SerializedPropertyType.Boolean, "voice: manager.voiceLowpass bool field exists (contract)");
            // Codex 2026-10-12 (2): override が無いときは実行側がコードの既定 (true) を読むので、proxy もそれと一致すること (VoiceFloat と同じ)
            if (lp != null && mb != null && mb.publicVariables.TryGetVariableValue("voiceLowpass", out object blp))
                v.Req(blp is bool bb && bb == lp.boolValue, "voice: manager.voiceLowpass backing == proxy (" + lp.boolValue + ")");
            else if (lp != null) v.Req(lp.boolValue == true, "voice: manager.voiceLowpass proxy " + lp.boolValue + " == code default True (no backing override)");
            var gapProp = mso.FindProperty("gap");
            float gap = gapProp != null ? gapProp.floatValue : 3f;
            float column = (MaxCarts - 1) * gap;
            v.Req(near >= column + VoiceNearMargin, "voice: near " + near + " >= column (" + (MaxCarts - 1) + " x gap " + gap + " = " + column + ") + " + VoiceNearMargin);
            v.Req(far > near && far <= VoiceFarCap, "voice: near " + near + " < far " + far + " <= " + VoiceFarCap);
            v.Req(gain >= 0f && gain <= 24f, "voice: gain " + gain + " in 0..24 dB");

            // ---- 配線 (Reflection とソース) ----
            var t = typeof(KH_CartManager);
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            v.Req(t.GetMethod("VoiceSync", F) != null && t.GetMethod("VoiceRestore", F) != null && t.GetMethod("VoiceForget", F) != null, "voice: VoiceSync / VoiceRestore / VoiceForget exist (KH_CartManager.Voice.cs)");
            v.Req(t.GetMethod("OnPlayerJoined", F) != null, "voice: KH_CartManager overrides OnPlayerJoined (late joiner re-apply)");
            string src = File.Exists(VoiceSourcePath) ? File.ReadAllText(VoiceSourcePath) : "";
            var body = Regex.Match(src, @"private void ApplyCore\(\)(.*?)private bool CartActive", RegexOptions.Singleline);
            v.Req(body.Success && Regex.IsMatch(body.Groups[1].Value, @"^\s*VoiceSync\(\);", RegexOptions.Multiline),
                "voice: ApplyCore calls VoiceSync() (" + VoiceSourcePath + ")");

            // ---- 他の音声制御が無い (あれば戻す値の設計が要る) ----
            var others = UnityEngine.Object.FindObjectsOfType<UdonBehaviour>(true)
                .Where(u => u.programSource != null && u.programSource.name == "WorldAudioSettings").Select(u => u.name).ToArray();
            v.Req(others.Length == 0, "voice: no WorldAudioSettings UdonBehaviour in scene" + (others.Length > 0 ? " [" + string.Join(", ", others) + "]" : ""));

            // ---- 相手の選び方 (VoiceTargets、副作用なし) ----
            VoiceProbe(v, manager, t);
        }

        static float VoiceFloat(V v, SerializedObject so, UdonBehaviour backing, string field, float codeDefault)
        {
            var p = so.FindProperty(field);
            if (p == null || p.propertyType != SerializedPropertyType.Float) { v.Req(false, "voice: manager." + field + " float field exists (contract)"); return codeDefault; }
            // Setup は書かないので backing に override が無いのが通常 (heap の既定 = コードの既定)。あれば proxy と一致すること
            if (backing != null && backing.publicVariables.TryGetVariableValue(field, out object bv))
                v.Req(bv is float bf && Mathf.Abs(bf - p.floatValue) < 1e-4f, "voice: manager." + field + " backing == proxy (" + p.floatValue + ")");
            else v.Req(Mathf.Abs(p.floatValue - codeDefault) < 1e-4f, "voice: manager." + field + " proxy " + p.floatValue + " == code default " + codeDefault + " (no backing override)");
            return p.floatValue;
        }

        static void VoiceProbe(V v, KH_CartManager manager, Type t)
        {
            var m = t.GetMethod("VoiceTargets", BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(int), typeof(int), typeof(int[]), typeof(int[]), typeof(int[]), typeof(int[]), typeof(int[]) }, null);
            if (m == null) { v.Req(false, "voice: VoiceTargets(int,int,int[]x5) exists"); return; }
            const int Free = KH_CartManager.GroupFree, Run = KH_CartManager.GroupRunning, End = KH_CartManager.GroupEnding;
            const int Idle = KH_CartManager.CartIdle, Ride = KH_CartManager.CartRiding, Ending = KH_CartManager.CartEnding, Park = KH_CartManager.CartParked;

            // 基本の盤面: 自分 5 は cart0 / group0。group0 = {5, 7, 13(Ending ではなく Riding)}、group1 = {9}、cart3 は Parked (乗員 0 の約束だが壊れた値 11 を入れて除外を見る)、cart4 は乗員 0
            int[] rider = { 5, 7, 9, 11, 0, 13, 0, 0 };
            int[] state = { Ride, Ride, Ride, Park, Ride, Ride, Idle, Idle };
            int[] group = { 0, 0, 1, 0, 0, 0, -1, -1 };
            int[] gs = { Run, Run, Free, Free, Free, Free, Free, Free };
            Case("same group only (others, Parked, empty seat excluded)", 5, 0, rider, state, group, gs, new[] { 7, 13 });
            Case("alone in group 1 -> none", 9, 1, rider, state, group, gs, new int[0]);
            Case("self not riding (myGroup -1) -> none", 5, -1, rider, state, group, gs, new int[0]);
            Case("invalid local player (me 0) -> none", 0, 0, rider, state, group, gs, new int[0]);

            // グループ解散 (Free) なら誰もいない
            var gsFree = (int[])gs.Clone(); gsFree[0] = Free;
            Case("group dissolved (Free) -> none", 5, 0, rider, state, group, gsFree, new int[0]);

            // 到着中 (group Ending / carts Ending) は所属のまま
            var stEnd = (int[])state.Clone(); stEnd[0] = Ending; stEnd[1] = Ending; stEnd[5] = Ending;
            var gsEnd = (int[])gs.Clone(); gsEnd[0] = End;
            Case("arriving (Ending) keeps members", 5, 0, rider, stEnd, group, gsEnd, new[] { 7, 13 });

            // 7 が別グループへ移った・降りた (Idle に戻り rider 0)
            var grMove = (int[])group.Clone(); grMove[1] = 1;
            Case("7 moved to group 1 -> only 13", 5, 0, rider, state, grMove, gs, new[] { 13 });
            var rdLeft = (int[])rider.Clone(); var stLeft = (int[])state.Clone(); var grLeft = (int[])group.Clone();
            rdLeft[1] = 0; stLeft[1] = Idle; grLeft[1] = -1;
            Case("7 left (cart reset) -> only 13", 5, 0, rdLeft, stLeft, grLeft, gs, new[] { 13 });

            // 空いた枠に別の人 (21) が入った: 前の相手 (7) は残らない
            var rdReuse = (int[])rider.Clone(); rdReuse[1] = 21;
            Case("slot reused by 21 -> 21, 13 (7 not kept)", 5, 0, rdReuse, state, group, gs, new[] { 21, 13 });

            // 同じ人が 2 台に載っている過渡状態は 1 回だけ
            var rdDup = (int[])rider.Clone(); rdDup[5] = 7;
            Case("duplicate rider counted once", 5, 0, rdDup, state, group, gs, new[] { 7 });

            void Case(string name, int me, int myGroup, int[] r, int[] s, int[] g, int[] gst, int[] expect)
            {
                var outIds = new int[MaxCarts];
                int n = (int)m.Invoke(manager, new object[] { me, myGroup, r, s, g, gst, outIds });
                var got = outIds.Take(n).ToArray();
                bool ok = got.Length == expect.Length && got.OrderBy(x => x).SequenceEqual(expect.OrderBy(x => x));
                v.Req(ok, "voice probe: " + name + " -> [" + string.Join(",", got) + "] (expect [" + string.Join(",", expect) + "])");
            }
        }
    }
}
