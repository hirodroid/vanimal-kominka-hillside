using System;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-DOG-054 芸「バーン」「ジャンプ」の Runtime の配線 (1-B)。契約: docs/development/issues/IMP-DOG-054-tricks-bang-jump.md §4.4。
    ///
    /// Tricks/3 は**新しいフィールドだけ**を書く: 6 枠の trainerPass・tricksEnabled、KD_CallPose の trainerPass・trickZoneCenters・trickZoneRadii、
    /// 呼ぶタブのポーズ面のボタン 2 つ (KD_ShopSetupEditor.EnsureButton で冪等)。Menu/6j Setup Call Poses は流さない (ゴースト・ミニチュアまで作り直す広い Setup)。
    /// 6j は面の既存の子を消さず、KD_CallPose の他のフィールドも触らないので、6j を後で流してもボタンと配列は残る (6j 側には作成を足さない)。
    /// ゾーンは犬の根のローカル: プレビューシーンで座り姿勢 (KD_Sit_&lt;犬&gt;) にした複製の頭の骨 Bone.004 から、
    /// バーン = 頭 + 根の前方 (水平) × KD_CallPose.TrickBangForward、ジャンプ = 頭 + 上 × TrickJumpUp、半径 TrickZoneRadius (調整値は KD_CallPose の定数 1 か所)。
    /// tricksEnabled (提供前のスイッチ §2.10) は既存値を保持する: 初めての配線 (6 枠とも trainerPass が null) のときだけ ON。切り替えは Tricks/3b・3c。
    /// 値はすべて proxy へ書いて CopyProxyToUdon。**シーンは保存しない** (統括が保存する)。検査は Tricks/4 (Temp/kh-tricks/verify.txt)。</summary>
    public static class KH_TrickSetupEditor
    {
        public const string OutDir = "Temp/kh-tricks/";
        public const string HeadBone = "Bone.004";
        public const string BangButtonName = "Bang", JumpButtonName = "Jump";
        /// <summary>ポーズ面 (CallPosePanel、1050 × 460) の 3 段目。既存の 2 段 (y -150: Stretch / Apply / Place、y -210: Back / Paw / Release) は動かさない。
        /// 面の下端 (-230) から少し出るが、親の Page_Sleep (700 高) の内側で、ほかのタブの下段ボタン (y -285) と同じ帯。</summary>
        public static readonly Vector2 BangPosition = new Vector2(-155f, -270f), JumpPosition = new Vector2(155f, -270f);
        public static readonly Vector2 ButtonSize = new Vector2(290f, 52f);
        const float ZoneTolerance = .001f;

        struct Zone
        {
            public Vector3 headLocal, bangLocal, jumpLocal;
            public Vector3 headWorld, bangWorld, jumpWorld, forward;
            public int boneCount;
        }

        // ================================================================ 3 Setup

        [MenuItem("Tools/KominkaHillside/Tricks/3 Setup Call Tricks")]
        public static void Setup()
        {
            KD_Hillside.Guard();
            var log = new StringBuilder("[KH_TrickSetup] Setup " + DateTime.Now.ToString("s") + "\n");
            var c = FindCallPose();
            var pass = FindTrainerPass();
            var slots = Slots(c);

            // ---- 6 枠: trainerPass + tricksEnabled (既存値を保持。初めての配線だけ ON) ----
            bool firstWire = slots.All(s => s.trainerPass == null);
            bool enabled = firstWire || slots[0].tricksEnabled;
            if (!firstWire && slots.Any(s => s.tricksEnabled != enabled))
                log.AppendLine("WARN tricksEnabled が枠ごとに違っていた → 枠 0 の値 " + OnOff(enabled) + " に揃えた");
            log.AppendLine("tricksEnabled = " + OnOff(enabled) + (firstWire ? " (初めての配線なので ON)" : " (既存値を保持)"));
            foreach (var s in slots)
            {
                s.trainerPass = pass; s.tricksEnabled = enabled;
                EditorUtility.SetDirty(s); UdonSharpEditorUtility.CopyProxyToUdon(s);
            }

            // ---- ゾーン 12 個 (犬の根のローカル) ----
            var zones = ComputeZones();
            var centers = new Vector3[12];
            var radii = new float[12];
            for (int d = 0; d < 6; d++)
            {
                centers[d * 2] = zones[d].bangLocal; centers[d * 2 + 1] = zones[d].jumpLocal;
                radii[d * 2] = radii[d * 2 + 1] = KD_CallPose.TrickZoneRadius;
            }
            AppendZoneTable(log, zones);

            // ---- ボタン 2 つ ----
            var pouch = c.pouch;
            if (pouch == null || c.panel == null) throw new InvalidOperationException("KD_CallPose.pouch / panel missing (run Menu/6j Setup Call Poses first)");
            var font = pouch.sleepLabels != null && pouch.sleepLabels.Length > 0 && pouch.sleepLabels[0] != null ? pouch.sleepLabels[0].font : null;
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (udon == null) throw new InvalidOperationException("KD_CallPose backing UdonBehaviour missing");
            Button MakeButton(string name, Vector2 pos, string key, string fallback, string evt)
            {
                var b = KD_ShopSetupEditor.EnsureButton(c.panel.transform, name, pos, ButtonSize, KD_UiSpriteEditor.ActionNeutral, font,
                    KD_Hillside.Text(key, fallback, log), udon, evt, log).GetComponent<Button>();
                // 6j の Button() と同じ見た目 (20pt、ラベルの枠)
                var label = b.GetComponentInChildren<TextMeshProUGUI>(true); label.fontSize = 20; label.rectTransform.sizeDelta = ButtonSize - new Vector2(12, 4);
                b.navigation = new Navigation { mode = Navigation.Mode.None };
                return b;
            }
            c.bangButton = MakeButton(BangButtonName, BangPosition, "call.trick.bang", "バーン", "_Bang");
            c.jumpButton = MakeButton(JumpButtonName, JumpPosition, "call.trick.jump", "ジャンプ", "_Jump");
            log.AppendLine("buttons: CallPosePanel/" + BangButtonName + " " + BangPosition + " → _Bang、CallPosePanel/" + JumpButtonName + " " + JumpPosition + " → _Jump (" + ButtonSize + ")");

            // ---- KD_CallPose ----
            c.trainerPass = pass;
            c.trickZoneCenters = centers;
            c.trickZoneRadii = radii;
            EditorUtility.SetDirty(c); UdonSharpEditorUtility.CopyProxyToUdon(c);

            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            log.AppendLine("trainerPass = " + PathOf(pass.transform) + " → KD_CallPose + 6 枠");
            log.AppendLine("**シーンは保存していない。** 次: Tools/KominkaHillside/Tricks/4 Verify Call Tricks");
            KD_VerifyLog.Write(OutDir + "setup.txt", log);
        }

        [MenuItem("Tools/KominkaHillside/Tricks/3b Set Tricks Enabled ON")]
        public static void EnableOn() { SetEnabled(true); }

        [MenuItem("Tools/KominkaHillside/Tricks/3c Set Tricks Enabled OFF (before the initial release build)")]
        public static void EnableOff() { SetEnabled(false); }

        static void SetEnabled(bool value)
        {
            KD_Hillside.Guard();
            var c = FindCallPose();
            foreach (var s in Slots(c))
            {
                s.tricksEnabled = value;
                EditorUtility.SetDirty(s); UdonSharpEditorUtility.CopyProxyToUdon(s);
            }
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            KD_VerifyLog.Write(OutDir + "enabled.txt", "[KH_TrickSetup] tricksEnabled = " + OnOff(value) + " (6 枠)。シーンは保存していない。Tricks/4 で確かめる\n");
        }

        // ================================================================ 4 Verify

        [MenuItem("Tools/KominkaHillside/Tricks/4 Verify Call Tricks")]
        public static void Verify()
        {
            if (EditorApplication.isPlaying || !KD_Hillside.InScene()) throw new InvalidOperationException("Open " + KD_Hillside.ScenePath + " in Edit mode");
            var v = new KD_VerifyLog("[KH_TrickSetup] Verify " + DateTime.Now.ToString("s"));
            try { VerifyInto(v); }
            catch (Exception e) { v.Check(false, "例外: " + e.GetType().Name + ": " + e.Message); }
            v.Finish(OutDir + "verify.txt");
            Debug.Log(v.Fail == 0 ? "TRICKS_OK " + v.Pass : "TRICKS_NG " + v.Fail);
        }

        static void VerifyInto(KD_VerifyLog v)
        {
            var go = KD_SceneObjects.Find("KD_CallPose", out int count);
            if (!v.Check(count == 1 && go != null && go.GetComponent<KD_CallPose>() != null, "KD_CallPose が 1 つ (" + count + ")")) return;
            var c = go.GetComponent<KD_CallPose>();
            var dogsRoot = KD_SceneObjects.Find(KD_Hillside.DogsRootName);
            var passes = dogsRoot == null ? new KH_TrainerPass[0] : dogsRoot.GetComponentsInChildren<KH_TrainerPass>(true);
            if (!v.Check(passes.Length == 1, "KH_Dogs の下の KH_TrainerPass が 1 つ (" + passes.Length + ")")) return;
            var pass = passes[0];
            var passBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(pass);
            var cBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(c);
            if (!v.Check(c.board != null && c.board.slots != null && c.board.slots.Length == 6, "KD_SleepBoard の枠 6")) return;

            // ---- 配線 (proxy と backing) ----
            v.Check(c.trainerPass == pass && BackingRef(cBacking, "trainerPass") == passBacking, "KD_CallPose.trainerPass = KH_TrainerPass (proxy / backing)");
            v.Check(pass._Configured(), "KH_TrainerPass に product と listingId がある (未所持の押下で購入画面をひらく)");
            bool? enabled = null; bool same = true;
            for (int d = 0; d < 6; d++)
            {
                var s = c.board.SlotOf(d);
                if (!v.Check(s != null, "枠 " + d + " がある")) continue;
                var sb = UdonSharpEditorUtility.GetBackingUdonBehaviour(s);
                v.Check(s.trainerPass == pass && BackingRef(sb, "trainerPass") == passBacking, "枠 " + d + " trainerPass (proxy / backing。Bang / Jump の受信側の権利)");
                bool backingEnabled;
                bool hasValue = sb.publicVariables.TryGetVariableValue("tricksEnabled", out backingEnabled);
                // bool の既定 (false) と同じ値は heap に override が無いことがある (vrc-udonsharp §4) ので、無ければ false と読む
                v.Check((hasValue ? backingEnabled : false) == s.tricksEnabled, "枠 " + d + " tricksEnabled proxy = backing (" + OnOff(s.tricksEnabled) + ")");
                if (enabled == null) enabled = s.tricksEnabled; else if (enabled.Value != s.tricksEnabled) same = false;
                var program = sb.programSource.SerializedProgramAsset.RetrieveProgram();
                v.Check(program.SymbolTable.GetSymbols().Contains("trickKind") && !program.SymbolTable.GetSymbols().Contains("hasStretch"), "枠 " + d + " コンパイル済み: 同期 trickKind あり・hasStretch なし");
                v.Check(program.EntryPoints.GetExportedSymbols().Contains("_Bang") && program.EntryPoints.GetExportedSymbols().Contains("_Jump"), "枠 " + d + " コンパイル済みの入口 _Bang / _Jump");
                var def = KD_DogDefs.All[d]; var source = KD_SceneObjects.Find(def.DogName);
                v.Check(source != null && s.DogRoot() == source.transform, "枠 " + d + " DogRoot = " + def.DogName + " (ゾーンを計算した根と同じ)");
            }
            v.Check(same, "tricksEnabled が 6 枠で一致");
            v.Info("-- tricksEnabled (提供前のスイッチ §2.10) = " + (enabled == null ? "?" : OnOff(enabled.Value)) + (same ? "" : " (枠ごとに違う)"));
            v.Note("OFF のとき (静的確認): HandleRequest が op 10 / 11 を拒否、_TrickStatus = 8 でボタン非表示・合図とガイド無し、伸び (op 9) は従来どおり使える");
            var uiProgram = cBacking.programSource.SerializedProgramAsset.RetrieveProgram();
            var exported = uiProgram.EntryPoints.GetExportedSymbols().ToArray();
            foreach (var entry in new[] { "_Bang", "_Jump", "_TrickGuideDog" })
                v.Check(exported.Contains(entry), "KD_CallPose コンパイル済みの入口 " + entry);
            // 引数付きのメソッドは U# が名前を飾って出すことがあるので、末尾一致も受ける
            foreach (var entry in new[] { "_TrickStatus", "_TrickZoneCenter", "_TrickZoneRadius", "_TrickWaitSeconds", "_TrickLastAccepted" })
                v.Check(exported.Any(s => s == entry || s.EndsWith("_" + entry)), "KD_CallPose コンパイル済みの入口 " + entry + " (引数付き)");

            // ---- 受信側の行 (ソースの静的確認) ----
            string slotSource = File.ReadAllText("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_SleepSlot.cs");
            v.Check(slotSource.Contains("(op == Bang || op == Jump) && tricksEnabled && poseId == 2 && arrived && CanStretch")
                && slotSource.Contains("trainerPass != null && trainerPass._OwnedBy(player))"), "KD_SleepSlot.HandleRequest: Bang / Jump は提供中・おすわり・到着・間隔・送り手のトレーナー所持");
            v.Check(slotSource.Contains("op == Stretch && CanStretch && ActionOwnedBy(StretchYawn, player))"), "KD_SleepSlot.HandleRequest: 伸びは従来の権利 (tricksEnabled に依らない)");

            // ---- ゾーン ----
            bool arrays = c.trickZoneCenters != null && c.trickZoneCenters.Length == 12 && c.trickZoneRadii != null && c.trickZoneRadii.Length == 12;
            if (v.Check(arrays, "trickZoneCenters / trickZoneRadii が 12"))
            {
                Vector3[] savedCenters; float[] savedRadii;
                v.Check(cBacking.publicVariables.TryGetVariableValue("trickZoneCenters", out savedCenters) && savedCenters != null && savedCenters.SequenceEqual(c.trickZoneCenters), "trickZoneCenters proxy = backing");
                v.Check(cBacking.publicVariables.TryGetVariableValue("trickZoneRadii", out savedRadii) && savedRadii != null && savedRadii.SequenceEqual(c.trickZoneRadii), "trickZoneRadii proxy = backing");
                var zones = ComputeZones();
                AppendZoneTable(v.Text, zones);
                for (int d = 0; d < 6; d++)
                {
                    string key = KD_DogDefs.All[d].key;
                    Vector3 bang = c.trickZoneCenters[d * 2], jump = c.trickZoneCenters[d * 2 + 1];
                    v.Check(Finite(bang) && Finite(jump) && !float.IsNaN(c.trickZoneRadii[d * 2]) && !float.IsNaN(c.trickZoneRadii[d * 2 + 1]), key + " ゾーンが有限");
                    v.Check(Mathf.Abs(c.trickZoneRadii[d * 2] - KD_CallPose.TrickZoneRadius) < 1e-5f && Mathf.Abs(c.trickZoneRadii[d * 2 + 1] - KD_CallPose.TrickZoneRadius) < 1e-5f,
                        key + " 半径 = " + KD_CallPose.TrickZoneRadius + " (定数と一致)");
                    v.Check(Vector3.Distance(bang, zones[d].bangLocal) < ZoneTolerance && Vector3.Distance(jump, zones[d].jumpLocal) < ZoneTolerance,
                        key + " 中心が座り姿勢の " + HeadBone + " からの計算と一致 (許容 " + ZoneTolerance + ")");
                    v.Check(Vector3.Dot(zones[d].bangWorld - zones[d].headWorld, zones[d].forward) > KD_CallPose.TrickBangForward - .01f
                        && Mathf.Abs(zones[d].bangWorld.y - zones[d].headWorld.y) < .01f, key + " バーンの中心は頭より前 (鼻の向き)・頭の高さ");
                    v.Check(zones[d].jumpWorld.y - zones[d].headWorld.y > KD_CallPose.TrickJumpUp - .01f, key + " ジャンプの中心は頭より上");
                }
            }

            // ---- ボタン ----
            CheckButton(v, c, cBacking, c.bangButton, "bangButton", BangButtonName, "_Bang", BangPosition);
            CheckButton(v, c, cBacking, c.jumpButton, "jumpButton", JumpButtonName, "_Jump", JumpPosition);
        }

        static void CheckButton(KD_VerifyLog v, KD_CallPose c, VRC.Udon.UdonBehaviour cBacking, Button b, string field, string name, string evt, Vector2 pos)
        {
            if (!v.Check(b != null && c.panel != null && b.transform.parent == c.panel.transform && b.name == name, field + " = CallPosePanel/" + name)) return;
            v.Check(BackingRef(cBacking, field) == b, field + " proxy = backing");
            bool wired = b.onClick.GetPersistentEventCount() == 1 && b.onClick.GetPersistentTarget(0) == cBacking && b.onClick.GetPersistentMethodName(0) == "SendCustomEvent";
            var arg = new SerializedObject(b).FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument");
            v.Check(wired && arg != null && arg.stringValue == evt, name + " の押下 = KD_CallPose." + evt);
            v.Check(b.GetComponentInChildren<TextMeshProUGUI>(true) != null, name + " にラベル");
            // 配置: 親 (Page_Sleep) の内側で、面のほかのボタンと重ならない (既存のボタンの位置は動かしていない)
            var rect = (RectTransform)b.transform; var panel = (RectTransform)c.panel.transform;
            v.Check(Vector2.Distance(rect.anchoredPosition, pos) < .5f && Vector2.Distance(rect.sizeDelta, ButtonSize) < .5f, name + " の位置 " + pos + " / 大きさ " + ButtonSize);
            Vector2 min = panel.anchoredPosition + rect.anchoredPosition - rect.sizeDelta / 2, max = panel.anchoredPosition + rect.anchoredPosition + rect.sizeDelta / 2;
            v.Check(min.x >= -KD_TreatSetupEditor.PanelWidth / 2 && max.x <= KD_TreatSetupEditor.PanelWidth / 2 && min.y >= -KD_TreatSetupEditor.PanelHeight / 2 && max.y <= KD_TreatSetupEditor.PanelHeight / 2,
                name + " が手元メニューの板 (" + KD_TreatSetupEditor.PanelWidth + " × " + KD_TreatSetupEditor.PanelHeight + ") の内側 (" + min + "〜" + max + ")");
            var mine = new Rect(rect.anchoredPosition - rect.sizeDelta / 2, rect.sizeDelta);
            var overlaps = c.panel.GetComponentsInChildren<Button>(true).Where(o => o != b && o.transform.parent == c.panel.transform)
                .Where(o => { var r = (RectTransform)o.transform; return new Rect(r.anchoredPosition - r.sizeDelta / 2, r.sizeDelta).Overlaps(mine); }).Select(o => o.name).ToArray();
            var hint = c.status != null ? c.status.rectTransform : null;
            bool hintOverlap = hint != null && new Rect(hint.anchoredPosition - hint.sizeDelta / 2, hint.sizeDelta).Overlaps(mine);
            v.Check(overlaps.Length == 0 && !hintOverlap, name + " が面のほかのボタン・状態行と重ならない" + (overlaps.Length > 0 ? " (" + string.Join(", ", overlaps) + ")" : "") + (hintOverlap ? " (Hint)" : ""));
        }

        // ================================================================ 共通

        static KD_CallPose FindCallPose()
        {
            var go = KD_SceneObjects.Find("KD_CallPose", out int count);
            var c = go == null ? null : go.GetComponent<KD_CallPose>();
            if (count != 1 || c == null) throw new InvalidOperationException("KD_CallPose x" + count + " (run Menu/6j Setup Call Poses first)");
            if (c.board == null) throw new InvalidOperationException("KD_CallPose.board missing (run Menu/6j)");
            return c;
        }

        static KH_TrainerPass FindTrainerPass()
        {
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName);
            var passes = dogs == null ? new KH_TrainerPass[0] : dogs.GetComponentsInChildren<KH_TrainerPass>(true);
            if (passes.Length != 1) throw new InvalidOperationException("KH_TrainerPass under " + KD_Hillside.DogsRootName + ": " + passes.Length + " (run Walking/3 Build)");
            return passes[0];
        }

        static KD_SleepSlot[] Slots(KD_CallPose c)
        {
            var slots = new KD_SleepSlot[6];
            for (int d = 0; d < 6; d++)
            {
                slots[d] = c.board.SlotOf(d);
                if (slots[d] == null) throw new InvalidOperationException("KD_SleepSlot " + d + " missing (run Menu/6 Setup Sleep)");
            }
            if (slots.Distinct().Count() != 6) throw new InvalidOperationException("KD_SleepBoard.slots are not six distinct slots");
            return slots;
        }

        /// <summary>6 犬種の座り姿勢の頭とゾーン。KD_CallPoseEditor.Setup の pawTargets と同じ手順 (シーンの犬を複製 → 原点・回転なし → KD_Sit を標本 → Constraint 適用 → 骨の位置を根のローカルへ)。</summary>
        static Zone[] ComputeZones()
        {
            var zones = new Zone[6];
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                for (int d = 0; d < 6; d++)
                {
                    var def = KD_DogDefs.All[d];
                    var source = KD_SceneObjects.Find(def.DogName);
                    if (source == null) throw new InvalidOperationException("Dog " + def.DogName + " missing");
                    var clip = KD_CallPoseEditor.PoseClip(def, 2);
                    var clone = UnityEngine.Object.Instantiate(source);
                    SceneManager.MoveGameObjectToScene(clone, scene);
                    try
                    {
                        clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.SetActive(true);
                        foreach (var part in clone.GetComponentsInChildren<Transform>(true))
                            if (part != clone.transform && KD_CallPoseEditor.IsWalkingGear(part.name)) part.gameObject.SetActive(false);
                        KD_DogPreviewEditor.SamplePose(clone, new KD_ClipRef(clip.name, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clip)), clip.length));
                        var bones = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(t => t != null && t.name == HeadBone).Distinct().ToArray();
                        if (bones.Length == 0) throw new InvalidOperationException("Head bone " + HeadBone + " missing " + def.key);
                        Vector3 head = bones[0].position;
                        Vector3 forward = clone.transform.forward; forward.y = 0f;
                        if (forward.sqrMagnitude < 1e-6f) throw new InvalidOperationException("Dog root forward is vertical " + def.key);
                        forward.Normalize();
                        Vector3 bang = head + forward * KD_CallPose.TrickBangForward, jump = head + Vector3.up * KD_CallPose.TrickJumpUp;
                        zones[d] = new Zone
                        {
                            headWorld = head, bangWorld = bang, jumpWorld = jump, forward = forward, boneCount = bones.Length,
                            headLocal = clone.transform.InverseTransformPoint(head),
                            bangLocal = clone.transform.InverseTransformPoint(bang),
                            jumpLocal = clone.transform.InverseTransformPoint(jump),
                        };
                    }
                    finally { UnityEngine.Object.DestroyImmediate(clone); }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            return zones;
        }

        /// <summary>到達性の判断材料: 犬ごとの頭の高さ (座り、地面から) とゾーンの中心の高さ。</summary>
        static void AppendZoneTable(StringBuilder log, Zone[] zones)
        {
            log.AppendLine("-- zones (座り姿勢、" + HeadBone + "。高さは根の原点から m。local = 犬の根のローカル) --");
            log.AppendLine("犬 | 頭の高さ | バーンの高さ | ジャンプの高さ | 頭 local | バーン local | ジャンプ local | 骨の数");
            for (int d = 0; d < zones.Length; d++)
            {
                var z = zones[d];
                log.AppendLine(KD_DogDefs.All[d].key + " | " + z.headWorld.y.ToString("F3") + " | " + z.bangWorld.y.ToString("F3") + " | " + z.jumpWorld.y.ToString("F3")
                    + " | " + z.headLocal.ToString("F3") + " | " + z.bangLocal.ToString("F3") + " | " + z.jumpLocal.ToString("F3") + " | " + z.boneCount);
            }
            log.AppendLine("調整値 (KD_CallPose): 速さ " + KD_CallPose.TrickSpeedMin + " m/s、バーンの角度 " + KD_CallPose.TrickBangAngle + "°、前 " + KD_CallPose.TrickBangForward
                + " m、ジャンプの上 " + KD_CallPose.TrickJumpUp + " m、半径 " + KD_CallPose.TrickZoneRadius + " m");
        }

        static UnityEngine.Object BackingRef(VRC.Udon.UdonBehaviour backing, string symbol)
        {
            object raw;
            return backing != null && backing.publicVariables.TryGetVariableValue(symbol, out raw) ? raw as UnityEngine.Object : null;
        }

        static bool Finite(Vector3 p)
        {
            return !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
        }

        static string OnOff(bool value) { return value ? "ON" : "OFF"; }

        static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    }
}
