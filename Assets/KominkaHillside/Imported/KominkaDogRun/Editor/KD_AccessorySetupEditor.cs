using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using static KominkaDogRun.Editor.KD_ModelNodeUtil;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// DES-DOG-004 Accessories: 飾りの 3D アセットと、**犬へ着けるための実測**を担う。
    ///
    /// **測る (1・2・3) と作る (0・4) の 2 種類がある。**
    /// 4 - Setup がシーンへ装着ノードを作る。比率は先に画で見て詰める段取り
    /// ([引き継ぎ §1](../../../docs/plans/2026-09-07-accessories-blender-handoff.md))。
    ///
    /// ---- 参照先は BlenderOptimized が正本 (2026-09-07) ----
    ///
    /// 初期の Tripo 版 (`Models/Accessories/&lt;ID&gt;/&lt;ID&gt;.fbx`) は
    /// **Blender 制作の軽量版へ置き換わった** (4 点 47,920 → 10,320 tris / 1 品 1 マテリアル)。
    /// このツールも `BlenderOptimized/&lt;ID&gt;/` を見る。**旧 Tripo 版のフォルダはまだ消していない**
    /// ので、実測を旧側で取って新側の検証と取り違えないこと (引き継ぎ §7 の 1 つ目)。
    ///
    /// **マテリアルを作る `2 - Apply` は廃止した。** Tripo 版は埋め込みマテリアルが真っ白で
    /// `AddRemap` が要ったが、Blender 版は**外部 `.mat` が正本**として既に配線済みで、
    /// 作り直すと Metallic/Smoothness の調整を戻してしまう (引き継ぎ §8)。
    ///
    /// ---- 実測の前提 (仮定を持ち込まない) ----
    ///
    /// - 装着先の骨名は <see cref="KD_DogRig"/> が**実測で解決する**。
    ///   資料 (`vrc-asset-production` ACC-002) の `Socket_HeadTop` 等は**提案名で、実在しない**
    /// - 「上」「前」は犬ごとに測る。`アーマチュア` に world euler (270,0,0) の軸変換が入っており、
    ///   骨のローカル軸は素直ではない (BUG-DOG-003 で 1 度読み違えている)
    /// - 頭の寸法は**骨だけでなく皮 (SkinnedMesh の頭ウェイト頂点) から測る**。
    ///   骨の位置は頭蓋の表面ではないので、骨だけで置くと毛に埋まるか浮く
    ///
    /// 実行順:
    ///   1 - Survey Accessory Assets   (飾り 4 点の実測。書き込み無し)
    ///   2 - Survey Dog Head Fit       (6 犬種の頭を実測し、王冠の Transform 案を出す。書き込み無し)
    ///   3 - Verify Accessory Assets   (数えるだけ)
    ///   4 - Setup Accessory Nodes     (犬へ装着ノードを作る。**作るだけ**)
    ///   5 - Replace Accessory Placement (既存ノードを表の比率で置き直す。**手の調整が消える**)
    /// </summary>
    public static class KD_AccessorySetupEditor
    {
        const string AccessoriesRoot = "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/BlenderOptimized";

        /// <summary>装着枠。**DES-DOG-004 論点 1 の 5 部位に各 1 つ**。
        /// 同じ枠のものは同時に着けられない (王冠と麦わら帽子はどちらか 1 つ)。</summary>
        public enum Slot { Head, Face, Chest, Body, Tail }

        /// <summary>三層。**現時点では案** — 商品としては未確定で、値段も解放条件もここでは決めていない
        /// (引き継ぎ §7)。商品の正本は progression-ce.md と KD_ShopSetupEditor。</summary>
        public enum Tier { Free, Point, CE }

        struct AccessoryDef
        {
            public string key;
            public string displayName;
            public Slot slot;
            public Tier tierPlan;

            /// <summary>世界での幅 / headScale。**装着枠ではなく品目ごとに要る** —
            /// 王冠と麦わら帽子は同じ頭枠だが、帽子はつばが頭より広くないと帽子に見えず、
            /// 王冠と同じ 1.4 だと頭に埋まる (2026-09-07 実測)。</summary>
            public float widthRatio;
            /// <summary>基準からの上下 / headScale。頭は「頭頂」、胸と尻尾は「骨」が基準。</summary>
            public float upRatio;
            /// <summary>基準からの前後 / headScale。</summary>
            public float fwdRatio;
            /// <summary>コーギーで画を見て詰めたか。false = まだ当て推量。</summary>
            public bool measured;
            public bool theme;
            public bool everyday;

            public string Folder { get { return AccessoriesRoot + "/" + key; } }
            public string FbxPath { get { return everyday ? KD_AccessoryEverydayEditor.ModelPath(key) : theme ? "Assets/KominkaHillside/Imported/KominkaDogRun/Models/Accessories/ThemeSets/" + key + "/KD_" + key + "_Set.fbx" : Folder + "/KD_" + key + "_Optimized.fbx"; } }
            public string MaterialPath { get { return everyday ? KD_AccessoryEverydayEditor.MaterialFor(key) : theme ? KD_AccessorySetProductionEditor.MaterialFor(key) : Folder + "/M_KD_AccOpt_" + key + ".mat"; } }
        }

        /// <summary>Blender 制作の軽量版 4 点 (2026-09-07)。
        /// **無料枠の普通のバンダナはまだ無い** (DES-DOG-004 論点 4 の Free 層は空)。
        ///
        /// 比率は **2026-09-07 にコーギーで「置く → 4 方向レンダー → 直す」を回して決めた実測寄りの値**。
        /// 他の 5 犬種はこの値がそのまま入るだけで、まだ見ていない (fit_status = untested)。</summary>
        static readonly AccessoryDef[] Accessories =
        {
            // 頭頂から少し沈めて後ろへ。耳の間に収まる
            new AccessoryDef { key = "Crown",      displayName = "王冠",         slot = Slot.Head,  tierPlan = Tier.CE,
                widthRatio = 1.40f, upRatio = -0.12f, fwdRatio = -0.15f, measured = true },
            // **王冠より一回り大きい。** つばが頭より広くないと帽子に見えず、1.4 だと頭に埋まる
            new AccessoryDef { key = "StrawHat",   displayName = "麦わら帽子",   slot = Slot.Head,  tierPlan = Tier.Point,
                widthRatio = 2.05f, upRatio = 0.10f, fwdRatio = -0.10f, measured = true },
            // **前へ 2.10 (15cm) 出さないと胸毛に沈む** — 首ボーン (Bone.003) は胸毛のずっと奥。
            // 下げ幅 -1.05 も要る (それ未満だと顎の下に付いて口の一部に見える)
            new AccessoryDef { key = "BowTie",     displayName = "蝶ネクタイ",   slot = Slot.Chest, tierPlan = Tier.Point,
                widthRatio = 1.30f, upRatio = -1.05f, fwdRatio = 2.10f, measured = true },
            // 尻尾の毛から出すのに 1.05 上げる
            new AccessoryDef { key = "TailRibbon", displayName = "尻尾用リボン", slot = Slot.Tail,  tierPlan = Tier.Point,
                widthRatio = 1.20f, upRatio = 1.05f, fwdRatio = -0.20f, measured = true },
            new AccessoryDef { key = "Bandana", displayName = "バンダナ", slot = Slot.Chest, theme = true,
                widthRatio = 1.9f, upRatio = -1.05f, fwdRatio = 2.1f },
            new AccessoryDef { key = "RoyalNecklace", displayName = "宝石付き首飾り", slot = Slot.Chest, theme = true,
                widthRatio = 1.9f, upRatio = -1.05f, fwdRatio = 2.1f },
            new AccessoryDef { key = "RoyalTail", displayName = "王室風尻尾飾り", slot = Slot.Tail, theme = true,
                widthRatio = 1.2f, upRatio = 1.05f, fwdRatio = -0.2f },
            new AccessoryDef { key = "Sunglasses", displayName = "サングラス", slot = Slot.Face, theme = true,
                widthRatio = 2.3f, upRatio = 0.32f, fwdRatio = 1.05f },
            new AccessoryDef { key = "SunflowerNecklace", displayName = "ひまわり首飾り", slot = Slot.Chest, theme = true,
                widthRatio = 1.9f, upRatio = -1.05f, fwdRatio = 2.1f },
            new AccessoryDef { key = "MarineCap", displayName = "マリン帽", slot = Slot.Head, theme = true,
                widthRatio = 1.6f, upRatio = 0f, fwdRatio = -0.15f },
            new AccessoryDef { key = "FlowerCrown", displayName = "花冠", slot = Slot.Head, theme = true,
                widthRatio = 1.65f, upRatio = -0.1f, fwdRatio = -0.15f },
            new AccessoryDef { key = "FlowerCorsage", displayName = "花コサージュ", slot = Slot.Chest, theme = true,
                widthRatio = 1.3f, upRatio = -1.05f, fwdRatio = 2.1f },
            new AccessoryDef { key = "FlowerTail", displayName = "花付き尻尾飾り", slot = Slot.Tail, theme = true,
                widthRatio = 1.2f, upRatio = 1.05f, fwdRatio = -0.2f },
            new AccessoryDef { key = "TopHat", displayName = "シルクハット", slot = Slot.Head, tierPlan = Tier.Point, everyday = true },
            new AccessoryDef { key = "Cap", displayName = "キャップ", slot = Slot.Head, tierPlan = Tier.Point, everyday = true },
            new AccessoryDef { key = "SpiralGlasses", displayName = "ぐるぐる眼鏡", slot = Slot.Face, tierPlan = Tier.Point, everyday = true },
            new AccessoryDef { key = "StarGlasses", displayName = "星型パーティー眼鏡", slot = Slot.Face, tierPlan = Tier.Point, everyday = true },
            new AccessoryDef { key = "Backpack", displayName = "リュック", slot = Slot.Body, tierPlan = Tier.Point, everyday = true },
        };

        /// <summary>シーン上の飾りオブジェクト名 (`KD_Acc_{key}_{dog}`) から装着枠を引く。
        /// 品目表 (<see cref="Accessories"/>) が正本 — 検査側に枠の写しを持たない (IMP-DOG-029 の新品目で `Unknown accessory` に落ちた)。</summary>
        internal static Slot SlotOfObject(string objectName)
        {
            foreach (var acc in Accessories)
                if (objectName.StartsWith("KD_Acc_" + acc.key + "_")) return acc.slot;
            throw new System.InvalidOperationException("Unknown accessory " + objectName);
        }

        /// <summary>**フォントへ焼く字。** 品目名と装着枠名はこの表が正本なので、
        /// 書き写さずにここから連結する (`KD_TreatSetupEditor.PouchWords` が呼ぶ)。
        /// 品目を足したらフォントの焼き直しが要る — 忘れると実機で豆腐になる。</summary>
        public static string MenuWords()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Accessories.Length; i++)
            {
                sb.Append(Accessories[i].displayName);
                sb.Append(SlotWord(Accessories[i].slot));
            }
            // 枠は 5 つとも焼く (品目がまだ無い枠も、後から足したときに困らないように)
            var slots = (Slot[])System.Enum.GetValues(typeof(Slot));
            for (int i = 0; i < slots.Length; i++) sb.Append(SlotWord(slots[i]));
            return sb.ToString();
        }

        /// <summary>頭の皮としてどのくらいのウェイトから数えるか。
        /// 0.5 = その頂点が頭ボーンに支配されているもの (混合ぶんは首・胴側へ渡す)。</summary>
        const float HeadWeightThreshold = 0.5f;

        const string BoxScriptPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_AccessoryBox.cs";
        const string BoxProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_AccessoryBox.asset";

        // =====================================================================
        // 0 - Create Program Asset
        //
        // **Setup と別メニューなのは、作った直後の同一実行内で
        // `AddUdonSharpComponent` すると属性を解決できず、シーンの backing
        // UdonBehaviour が既定の Continuous で焼き付くから** (`vrc-udonsharp` §2)。
        // 作る → コンパイル完了を待つ → Setup、の順で回す
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Accessories/0 - Create Accessory Program Asset")]
        public static void CreateProgramAsset()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_AccessorySetup] Create Program Asset");

            if (AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(BoxProgramPath) != null)
            {
                log.AppendLine("既にある: " + BoxProgramPath);
                Finish("Logs/des-dog-004-program.txt", log);
                return;
            }

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(BoxScriptPath);
            if (script == null)
            {
                log.AppendLine("FAIL script not found: " + BoxScriptPath);
                Finish("Logs/des-dog-004-program.txt", log);
                return;
            }

            var asset = ScriptableObject.CreateInstance<UdonSharp.UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, BoxProgramPath);
            AssetDatabase.SaveAssets();
            log.AppendLine("created: " + BoxProgramPath);
            log.AppendLine("**コンパイル完了を待ってから Setup を回すこと。**");
            Finish("Logs/des-dog-004-program.txt", log);
        }

        // =====================================================================
        // 1 - Survey Accessory Assets (書き込み無し)
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Accessories/1 - Survey Accessory Assets")]
        public static void SurveyAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_AccessorySetup] Survey Accessory Assets (書き込み無し)");
            log.AppendLine("参照: " + AccessoriesRoot + " (**Blender 軽量版が正本**)");
            log.AppendLine();

            int fail = 0;
            int triTotal = 0;
            int slotTotal = 0;

            for (int i = 0; i < Accessories.Length; i++)
            {
                var acc = Accessories[i];
                log.AppendLine("== " + acc.displayName + " (" + acc.key + ") / 枠 " + acc.slot
                    + " / 区分案 " + acc.tierPlan);
                log.AppendLine("   fbx: " + acc.FbxPath);

                Mesh mesh;
                Matrix4x4 modelMatrix;
                string problem;
                if (!ResolveMeshFromFbx(acc.FbxPath, out mesh, out modelMatrix, out problem))
                {
                    log.AppendLine("   FAIL " + problem);
                    fail++;
                    log.AppendLine();
                    continue;
                }

                int tris = mesh.triangles.Length / 3;
                triTotal += tris;
                log.AppendLine(string.Format("   mesh '{0}': verts {1} / tris {2} / submesh {3}",
                    mesh.name, mesh.vertexCount, tris, mesh.subMeshCount));

                // **装着で効くのは model 空間 (FBX をシーンへ置いた向き) の寸法。**
                // mesh.bounds のままだと FBX 内の軸変換ぶん読み違える
                Vector3 mc, ms;
                MeasureMeshUnder(mesh, modelMatrix, out mc, out ms);
                log.AppendLine("   model 空間 AABB: center " + V(mc) + " / size " + V(ms) + "  (m)");
                log.AppendLine("   → 幅 " + (ms.x * 100f).ToString("F1") + "cm / 高さ "
                    + (ms.y * 100f).ToString("F1") + "cm / 奥行 " + (ms.z * 100f).ToString("F1") + "cm");
                log.AppendLine("   装着基準からの底面オフセット: Y min " + (mc.y - ms.y * 0.5f).ToString("F4") + " m");
                log.AppendLine("   model matrix: rot " + V(modelMatrix.rotation.eulerAngles)
                    + " / scale " + V(modelMatrix.lossyScale));

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(acc.FbxPath);
                var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
                var ourMat = AssetDatabase.LoadAssetAtPath<Material>(acc.MaterialPath);
                if (ourMat == null) { log.AppendLine("   FAIL material not found: " + acc.MaterialPath); fail++; }

                for (int r = 0; r < renderers.Length; r++)
                {
                    var mats = renderers[r].sharedMaterials;
                    slotTotal += mats.Length;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        string tn = "-";
                        if (mats[m] != null && mats[m].HasProperty("_MainTex"))
                        {
                            var t = mats[m].GetTexture("_MainTex");
                            tn = t == null ? "**_MainTex null**" : t.name;
                        }
                        log.AppendLine("   renderer[" + r + "] mat[" + m + "]: "
                            + (mats[m] == null ? "**null**" : mats[m].name + " / _MainTex " + tn));
                        if (mats[m] != ourMat) { log.AppendLine("        FAIL 外部 mat を指していない"); fail++; }
                    }
                }
                log.AppendLine();
            }

            log.AppendLine("== 合計 " + triTotal + " tris / マテリアル slot " + slotTotal);
            log.AppendLine("   最大構成 6 匹 x (頭 1 + 胸 1 + 尻尾 1) は 2 - Survey Dog Head Fit の表を参照");
            log.AppendLine();
            log.AppendLine(fail == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + fail + ")");
            Finish("Logs/des-dog-004-assets.txt", log);
        }

        // =====================================================================
        // 2 - Survey Dog Head Fit (書き込み無し)
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Accessories/2 - Survey Dog Head Fit")]
        public static void SurveyHeadFit()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_AccessorySetup] Survey Dog Head Fit (書き込み無し)");
            log.AppendLine("目的: 6 犬種の頭を実測し、王冠の接続先と Transform 案を出す (ACC-003 の適合データ)");
            log.AppendLine("**骨名は KD_DogRig が実測で解決する。資料の Socket_HeadTop 等は実在しない**");
            log.AppendLine();

            // ---- 飾り側の寸法 (王冠) ----
            var crown = Accessories[0];
            Mesh crownMesh; Matrix4x4 crownMatrix; string crownProblem;
            Vector3 crownCenter = Vector3.zero, crownSize = Vector3.one;
            bool crownOk = ResolveMeshFromFbx(crown.FbxPath, out crownMesh, out crownMatrix, out crownProblem);
            if (crownOk)
            {
                MeasureMeshUnder(crownMesh, crownMatrix, out crownCenter, out crownSize);
                log.AppendLine("== 王冠 (model 空間)");
                log.AppendLine("   幅 " + (crownSize.x * 100f).ToString("F1") + "cm / 高さ "
                    + (crownSize.y * 100f).ToString("F1") + "cm / 奥行 " + (crownSize.z * 100f).ToString("F1") + "cm");
                log.AppendLine("   底面 Y = " + (crownCenter.y - crownSize.y * 0.5f).ToString("F4") + " m");
            }
            else
            {
                log.AppendLine("== 王冠 FAIL " + crownProblem);
            }
            log.AppendLine();

            var table = new StringBuilder();
            table.AppendLine("| breed | 接続先 (実測) | headScale | 耳間 | 頭幅 (皮) | 頭頂 | localPosition | localScale | fit_status |");
            table.AppendLine("|---|---|---:|---:|---:|---:|---|---:|---|");

            int fail = 0;

            foreach (var def in KD_DogDefs.All)
            {
                log.AppendLine("== " + def.displayName + " (" + def.key + ")");

                var dog = KD_SceneObjects.Find(def.DogName);
                if (dog == null)
                {
                    log.AppendLine("   FAIL シーンに " + def.DogName + " が無い");
                    fail++;
                    log.AppendLine();
                    continue;
                }

                var rig = KD_DogRig.Resolve(dog);
                if (!rig.Ok)
                {
                    for (int p = 0; p < rig.problems.Count; p++) log.AppendLine("   FAIL " + rig.problems[p]);
                    fail++;
                    log.AppendLine();
                    continue;
                }

                var head = rig.head;
                log.AppendLine("   接続先: " + PathUnder(dog.transform, head));
                log.AppendLine("   骨格: " + (rig.TwoTier ? "2 段 (メッシュ側 " + rig.meshSkelRoot.name + ")" : "1 段 (アーマチュアのみ)"));
                log.AppendLine("   headScale " + rig.headScale.ToString("F4") + " m / bodyScale "
                    + rig.bodyScale.ToString("F4") + " m / 体格比 " + rig.BodyRatio.ToString("F3"));

                // ---- 「上」「前」を実測する (骨のローカル軸を信じない) ----
                // 前 = 頭 -> 鼻 を犬の水平面へ落としたもの / 上 = 犬ルートの up
                Vector3 upW = dog.transform.up;
                Vector3 noseDir = rig.nose.position - head.position;
                Vector3 fwdW = Vector3.ProjectOnPlane(noseDir, upW);
                if (fwdW.sqrMagnitude < 1e-8f)
                {
                    log.AppendLine("   FAIL 頭->鼻 が真上を向いている (前方向を決められない)");
                    fail++;
                    log.AppendLine();
                    continue;
                }
                fwdW = fwdW.normalized;
                Vector3 rightW = Vector3.Cross(upW, fwdW).normalized;

                log.AppendLine("   実測した頭の向き (world): up " + V(upW) + " / fwd " + V(fwdW));
                log.AppendLine("   頭ボーンの local 軸を犬ローカルで見ると: "
                    + "x " + V(dog.transform.InverseTransformDirection(head.right))
                    + " y " + V(dog.transform.InverseTransformDirection(head.up))
                    + " z " + V(dog.transform.InverseTransformDirection(head.forward)));

                // ---- 耳 ----
                Vector3 earL = rig.earL.position, earR = rig.earR.position;
                Vector3 earMid = (earL + earR) * 0.5f;
                float earSpan = Vector3.Distance(earL, earR);
                log.AppendLine("   耳間 " + (earSpan * 100f).ToString("F1") + "cm / 耳の中点は頭ボーンから "
                    + (Vector3.Dot(earMid - head.position, upW) * 100f).ToString("F1") + "cm 上");

                // ---- 皮 (頭ウェイトの頂点) から頭蓋の実寸を測る ----
                var skull = MeasureSkull(dog, head, rig.earL, rig.earR, rig.headScale, upW, fwdW, rightW);
                float headTopUp = 0f, headWidth = 0f;
                if (skull.ok)
                {
                    headWidth = skull.width;
                    headTopUp = skull.topUp;
                    log.AppendLine("   皮 " + skull.count + " 頂点 (頭ウェイト >= " + HeadWeightThreshold
                        + " / 正中線 " + skull.midlineCount + " / 頭蓋の外として幅から除いた " + skull.earRejected + ")");
                    log.AppendLine("   頭頂は頭ボーンから " + (skull.topUp * 100f).ToString("F1")
                        + "cm 上 / 前後 " + (skull.topFwd * 100f).ToString("F1") + "cm");
                    log.AppendLine("   冠線の頭幅 " + (headWidth * 100f).ToString("F1")
                        + "cm (頭頂から " + (SkullBandRatio * rig.headScale * 100f).ToString("F1") + "cm 下までの帯)");
                    log.AppendLine("   [比較] 耳間 " + (earSpan * 100f).ToString("F1")
                        + "cm — **スケールの根拠には使わない** (立ち耳と垂れ耳で意味が変わる)");
                }
                else
                {
                    log.AppendLine("   FAIL 頭ウェイトの頂点が取れない (SkinnedMeshRenderer か bindposes を確認)");
                    fail++;
                }

                // ---- 王冠の Transform 案 ----
                string posStr = "-", scaleStr = "-";
                if (skull.ok && crownOk)
                {
                    // 置く点 = 正中線・頭頂・頭頂の前後位置
                    // (王冠の原点は底面中央なので、頭頂へそのまま乗る)
                    Vector3 spotW = head.position
                        + upW * headTopUp
                        + fwdW * skull.topFwd;

                    // 大きさ: **BASELINE。実測ではない。**
                    // 皮からの頭幅は 6 犬種で 5.8〜17.8cm とばらけて信用できなかった
                    // (頭ボーンが持つ皮の範囲が犬種でまるで違う) ので、根拠には使わない。
                    // 代わりに **headScale (鼻までの距離) 比**で置く — 既存のアンカーが全部この作法で、
                    // 犬種をまたいで単調に効くことが確認できている唯一の頭の寸法。
                    // **見て詰める前提の初期値** (ACC-003 の fit_status は untested のまま)
                    float targetWidth = crown.widthRatio * rig.headScale;
                    float scale = crownSize.x > 1e-6f ? targetWidth / crownSize.x : 1f;

                    Vector3 localPos = head.InverseTransformPoint(spotW);
                    Quaternion localRot = Quaternion.Inverse(head.rotation) * Quaternion.LookRotation(fwdW, upW);
                    // 犬ルートのスケールが 1 でない犬種があるので、親のスケールを割り戻す
                    Vector3 hs = head.lossyScale;
                    float localScale = scale / Mathf.Max(1e-6f, (hs.x + hs.y + hs.z) / 3f);

                    posStr = V(localPos);
                    scaleStr = localScale.ToString("F4");

                    log.AppendLine("   → 王冠案 (親 = 頭ボーン):");
                    log.AppendLine("      localPosition " + posStr);
                    log.AppendLine("      localRotation " + V(localRot.eulerAngles));
                    log.AppendLine("      localScale    " + scaleStr + " (均一)");
                    log.AppendLine("      → 実寸 幅 " + (crownSize.x * scale * 100f).ToString("F1")
                        + "cm / 高さ " + (crownSize.y * scale * 100f).ToString("F1") + "cm");

                    table.AppendLine("| " + def.key + " | " + head.name + " | "
                        + rig.headScale.ToString("F4") + " | " + (earSpan * 100f).ToString("F1") + "cm | "
                        + (headWidth * 100f).ToString("F1") + "cm | " + (headTopUp * 100f).ToString("F1") + "cm | "
                        + posStr + " | " + scaleStr + " | untested |");
                }
                log.AppendLine();
            }

            log.AppendLine("== 王冠の犬種別適合データ (ACC-003)");
            log.AppendLine("asset_id Crown / slot_id head_top / 回転は LookRotation(前, 上) を頭ボーン相対にしたもの");
            log.AppendLine();
            log.Append(table.ToString());
            log.AppendLine();
            log.AppendLine("**fit_status は全犬種 untested。** 耳・目・毛との干渉、頭を下げた姿勢、");
            log.AppendLine("走る・尻尾を振る等での見え方は未確認 (ACC-006 の姿勢一覧)。Play はユーザーへ引き継ぐ。");
            log.AppendLine();
            log.AppendLine(fail == 0 ? "RESULT: PASS (6 犬種すべて測れた)" : "RESULT: FAIL (" + fail + ")");
            Finish("Logs/des-dog-004-headfit.txt", log);
        }

        // =====================================================================
        // 4 - Setup Accessory Nodes
        //
        // **作るだけ。** 既存の親も位置も触らない (CLAUDE.md の役割分担)。
        // 置き場所の正本は KD_DogDefs.LayoutTable (KD_AccessorySystem を 1 行足してある)。
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Accessories/4 - Setup Accessory Nodes")]
        public static void SetupNodes()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_AccessorySetup] Setup Accessory Nodes");
            log.AppendLine("**作るだけ。既存ノードの位置は触らない** — 置き直しは 5 - Replace Accessory Placement");
            log.AppendLine();

            var defs = KD_DogDefs.All;
            int dogCount = defs.Length;
            int prodCount = Accessories.Length;
            int made = 0, kept = 0;
            bool ok = true;

            // ---- 台帳 ----
            var boxGo = KD_SceneObjects.Find(KD_DogDefs.AccessoryBoxName);
            if (boxGo == null)
            {
                var parent = KD_SceneObjects.Find("KD_Systems");
                if (parent == null)
                {
                    log.AppendLine("FAIL KD_Systems がシーンに無い");
                    Finish("Logs/des-dog-004-setup.txt", log);
                    return;
                }
                boxGo = new GameObject(KD_DogDefs.AccessoryBoxName);
                boxGo.transform.SetParent(parent.transform, false);
                log.AppendLine("created " + KD_DogDefs.AccessoryBoxName);
            }

            var box = UdonSharpEditor.UdonSharpComponentExtensions
                .GetUdonSharpComponent<KominkaDogRun.KD_AccessoryBox>(boxGo);
            if (box == null)
            {
                box = UdonSharpEditor.UdonSharpComponentExtensions
                    .AddUdonSharpComponent<KominkaDogRun.KD_AccessoryBox>(boxGo);
                log.AppendLine("added KD_AccessoryBox");
            }

            // ---- 犬 x 品目のノード ----
            var nodes = new GameObject[prodCount * dogCount];
            var productIds = new int[prodCount];
            var productSlots = new int[prodCount];

            for (int p = 0; p < prodCount; p++)
            {
                var acc = Accessories[p];
                productIds[p] = p;                      // 恒久 ID = 表の並び (増やすときは末尾へ足す)
                productSlots[p] = (int)acc.slot;


                Mesh mesh; Matrix4x4 mm; string problem;
                if (!ResolveMeshFromFbx(acc.FbxPath, out mesh, out mm, out problem))
                {
                    log.AppendLine("FAIL " + acc.key + " " + problem);
                    ok = false;
                    continue;
                }
                Vector3 mc, ms;
                MeasureMeshUnder(mesh, mm, out mc, out ms);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(acc.FbxPath);

                for (int d = 0; d < dogCount; d++)
                {
                    var dog = KD_SceneObjects.Find(defs[d].DogName);
                    if (dog == null) { log.AppendLine("FAIL " + defs[d].DogName + " が無い"); ok = false; continue; }

                    var rig = KD_DogRig.Resolve(dog);
                    if (!rig.Ok) { log.AppendLine("FAIL rig " + defs[d].key); ok = false; continue; }

                    Transform anchor = AnchorFor(rig, acc.slot);
                    if (anchor == null)
                    {
                        log.AppendLine("FAIL " + defs[d].key + " の " + acc.slot + " 用ボーンが無い");
                        ok = false;
                        continue;
                    }

                    string nodeName = "KD_Acc_" + acc.key + "_" + defs[d].key;
                    int existingCount;
                    var existingGo = KD_SceneObjects.Find(nodeName, out existingCount);
                    if (existingCount > 1) { log.AppendLine("FAIL duplicate " + nodeName); Finish("Logs/des-dog-004-setup.txt", log); return; }
                    var existing = existingGo == null ? null : existingGo.transform;
                    if (existing != null && existing.parent != anchor)
                    {
                        log.AppendLine("FAIL parent " + nodeName + " (run VR Interaction/2 - Repair)");
                        // 台帳配列を部分的な null で上書きしない。Repair後に再実行できる。
                        Finish("Logs/des-dog-004-setup.txt", log);
                        return;
                    }
                    GameObject holder;
                    if (existing != null)
                    {
                        // **既にあるものの位置は触らない** (CLAUDE.md の役割分担 = Setup は作るだけ)。
                        // 最後は人が手で詰める前提なので、ここで計算値を上書きすると
                        // **その調整が次の Setup で黙って消える**。表の比率を変えて置き直したいときは
                        // `5 - Replace Accessory Placement` を明示的に呼ぶ
                        holder = existing.gameObject;
                        kept++;
                    }
                    else
                    {
                        // **入れ物を 1 枚かませる。** この FBX は Blender の Z-up -> Unity の Y-up 変換を
                        // **プレハブのルート自身の Transform** に持っている (メッシュも子ではなくルート)。
                        // ルートの rotation を直接書くとその変換ごと消えて 90 度倒れる (2026-09-07 実測)
                        holder = new GameObject(nodeName);
                        holder.transform.SetParent(anchor, false);
                        var art = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        art.transform.SetParent(holder.transform, false);
                        PlaceHolder(holder.transform, dog, rig, anchor, acc, ms);
                        made++;
                    }

                    // **既定は非アクティブ。** 何も着けていない状態がシーンの初期状態で、
                    // 台帳の既定 (Unset) と一致していないと Late Joiner の見た目がずれる
                    if (holder.activeSelf) holder.SetActive(false);
                    nodes[p * dogCount + d] = holder;
                }
                log.AppendLine((acc.measured ? "PASS  " : "BASE  ") + acc.displayName
                    + " (" + acc.key + ") 枠 " + acc.slot
                    + " / 幅 " + acc.widthRatio.ToString("F2") + " x headScale"
                    + (acc.measured ? " (コーギーで確認済み)" : " (**未確認**)"));
            }

            box.productIds = productIds;
            box.productSlots = productSlots;
            box.nodes = nodes;
            box.dogCount = dogCount;
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(box);

            // **Manual を実物で保証する。** 属性だけでは backing UdonBehaviour が
            // Continuous で焼き付くことがある (`vrc-udonsharp` §2)
            var udon = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(box);
            if (udon != null && udon.SyncMethod != VRC.SDKBase.Networking.SyncType.Manual)
            {
                udon.SyncMethod = VRC.SDKBase.Networking.SyncType.Manual;
                EditorUtility.SetDirty(udon);
                log.AppendLine("fixed sync = Manual");
            }
            EditorUtility.SetDirty(boxGo);

            // ---- 手元メニューへ配線 ----
            var pouchGo = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName);
            if (pouchGo == null)
            {
                log.AppendLine("FAIL " + KD_DogDefs.TreatPouchName + " が無い");
                ok = false;
            }
            else
            {
                var pouch = UdonSharpEditor.UdonSharpComponentExtensions
                    .GetUdonSharpComponent<KominkaDogRun.KD_TreatPouch>(pouchGo);
                if (pouch == null) { log.AppendLine("FAIL KD_TreatPouch が無い"); ok = false; }
                else
                {
                    var names = new string[prodCount];
                    var slotNames = new string[prodCount];
                    for (int p = 0; p < prodCount; p++)
                    {
                        names[p] = Accessories[p].displayName;
                        slotNames[p] = SlotWord(Accessories[p].slot);
                    }
                    pouch.accessories = box;
                    pouch.accProductIds = productIds;
                    pouch.accNames = names;
                    pouch.accSlotNames = slotNames;
                    KD_AccessorySetsEditor.Wire(box, pouch, log);
                    UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(pouch);
                    EditorUtility.SetDirty(pouchGo);
                    log.AppendLine("wired KD_TreatPouch.accessories / accProductIds / accNames / accSlotNames");
                    log.AppendLine("wired accessory product entitlements and three sets");
                }
            }

            // ---- 実体の無くなった NetworkID を掃除する ----
            //
            // **出どころは飾りではなく QvPen** (2026-09-07 に特定)。シーンを保存すると
            // `PenManagerButtonGenerator` が「ボタンを 12 個生成しました」と出してボタンを作り直し、
            // **前のボタンを指していた NetworkID entry が 12 件宙ぶらりんになる。**
            // Treat Verify の「NetworkIDs broken refs == 0」がこれで落ちる
            // (IMP-DOG-005 で 4 件壊したのと同じ検査)。飾りのノードは UdonBehaviour を持たないので
            // NetworkID を持たず、増やしてはいない (台帳 1 件だけが正当な増分)。
            //
            // ⚠ **ここでの掃除は取りこぼす。** DestroyImmediate 直後の参照は同じ呼び出しの中では
            // まだ null と比べられず (Unity の擬似 null)、保存より前に走るこの掃除では捕まらない。
            // **確実に消すには保存の後にもう一度回す** — 下のログが 0 件でも Verify が落ちたら、
            // 保存後に同じ掃除をもう一度かける
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (desc != null && desc.NetworkIDCollection != null)
            {
                int removed = 0;
                for (int i = desc.NetworkIDCollection.Count - 1; i >= 0; i--)
                {
                    var pair = desc.NetworkIDCollection[i];
                    if (pair != null && pair.gameObject != null) continue;
                    if (pair != null && pair.SerializedTypeNames != null && pair.SerializedTypeNames.Count > 0)
                    {
                        log.AppendLine("WARN NetworkID " + pair.ID + " has types but no object; preserved for investigation");
                        continue;
                    }
                    desc.NetworkIDCollection.RemoveAt(i);
                    removed++;
                }
                if (removed > 0)
                {
                    EditorUtility.SetDirty(desc);
                    log.AppendLine("NetworkIDs: 実体の無い entry を " + removed + " 件消した (残り "
                        + desc.NetworkIDCollection.Count + " 件)");
                }
                else
                {
                    log.AppendLine("NetworkIDs: 破損 0 件 (" + desc.NetworkIDCollection.Count + " 件)");
                }
            }

            log.AppendLine();
            log.AppendLine("ノード: 新規 " + made + " 個 / 既存のまま " + kept + " 個 (位置は触っていない)");
            log.AppendLine(ok ? "RESULT: PASS — シーンを保存すること" : "RESULT: FAIL");
            log.AppendLine("既存ノードの手調整は保持。新規配置の見た目は Sets - Render Fits で犬種ごとに確認する。");
            Finish("Logs/des-dog-004-setup.txt", log);
        }

        // =====================================================================
        // 5 - Replace Accessory Placement
        //
        // **既存ノードの Transform を表の比率で計算し直す。明示的に呼んだときだけ。**
        // 4 - Setup を「作るだけ」に戻したので、比率を変えて置き直したいときはこちらを使う。
        //
        // ⚠ **人が手で詰めた位置はこれで消える。** 最後の微調整を手でやる運用なので、
        // 走らせる前に「表の値で置き直してよいか」を確かめること。
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Accessories/5 - Replace Accessory Placement")]
        public static void ReplacePlacement()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_AccessorySetup] Replace Accessory Placement");
            log.AppendLine("**既存ノードの位置を表の比率で計算し直す。手で詰めた位置は消える**");
            log.AppendLine();

            var defs = KD_DogDefs.All;
            int moved = 0, missing = 0;

            for (int p = 0; p < Accessories.Length; p++)
            {
                var acc = Accessories[p];
                Mesh mesh; Matrix4x4 mm; string problem;
                if (!ResolveMeshFromFbx(acc.FbxPath, out mesh, out mm, out problem))
                {
                    log.AppendLine("FAIL " + acc.key + " " + problem);
                    continue;
                }
                Vector3 mc, ms;
                MeasureMeshUnder(mesh, mm, out mc, out ms);

                for (int d = 0; d < defs.Length; d++)
                {
                    var dog = KD_SceneObjects.Find(defs[d].DogName);
                    if (dog == null) { missing++; continue; }
                    var rig = KD_DogRig.Resolve(dog);
                    if (!rig.Ok) { missing++; continue; }
                    var anchor = AnchorFor(rig, acc.slot);
                    if (anchor == null) { missing++; continue; }

                    var holder = anchor.Find("KD_Acc_" + acc.key + "_" + defs[d].key);
                    if (holder == null) { missing++; continue; }

                    PlaceHolder(holder, dog, rig, anchor, acc, ms);
                    EditorUtility.SetDirty(holder);
                    moved++;
                }
                log.AppendLine("  " + acc.displayName + " (" + acc.key + ") 幅 "
                    + acc.widthRatio.ToString("F2") + " / 上下 " + acc.upRatio.ToString("F2")
                    + " / 前後 " + acc.fwdRatio.ToString("F2"));
            }

            log.AppendLine();
            log.AppendLine("置き直した " + moved + " 個 / 見つからなかった " + missing + " 個");
            log.AppendLine(missing == 0 ? "RESULT: PASS — シーンを保存すること"
                : "RESULT: FAIL (先に 4 - Setup でノードを作る)");
            Finish("Logs/des-dog-004-replace.txt", log);
        }

        /// <summary>装着枠ごとの接続先ボーン。**すべて KD_DogRig の実測から引く** —
        /// 資料の Socket_* は実在しない。</summary>
        internal static Transform AnchorFor(KD_DogRig rig, Slot slot)
        {
            if (slot == Slot.Head || slot == Slot.Face) return rig.head;
            // 胸・首元 = 頭ボーンの親 (Bone.003 = 首)。顎の下・胸の上部中央はここに付く
            if (slot == Slot.Chest) return rig.head != null ? rig.head.parent : null;
            if (slot == Slot.Body) return rig.Bone("Bone.001", null); // Back surface follows this bone through idle/sit/sleep; Bone.002 lifts the bag away in sit.
            if (slot == Slot.Tail)
            {
                // 2 段の Bone.018 は尻尾を振る骨の手前。PB の直前の表示骨が動く付け根。
                if (rig.tailPhysBone == null) return null;
                var root = rig.tailPhysBone.rootTransform != null ? rig.tailPhysBone.rootTransform : rig.tailPhysBone.transform;
                return root.parent;
            }
            return null;
        }

        static string SlotWord(Slot slot)
        {
            if (slot == Slot.Head) return "あたま";
            if (slot == Slot.Face) return "かお";
            if (slot == Slot.Chest) return "むね";
            if (slot == Slot.Body) return "からだ";
            return "しっぽ";
        }

        /// <summary>入れ物を置く。**向きは犬ごとに実測する** (骨のローカル軸は素直ではない)。</summary>
        static void PlaceHolder(Transform holder, GameObject dog, KD_DogRig rig,
            Transform anchor, AccessoryDef acc, Vector3 modelSize)
        {
            if (acc.everyday) { KD_AccessoryEverydayEditor.PlaceNew(holder, dog, rig, acc.key, modelSize); return; }
            if (acc.theme && KD_AccessorySetsEditor.PlaceNew(holder, dog, rig, acc.key, modelSize)) return;
            Vector3 upW = dog.transform.up;
            Vector3 fwdW = Vector3.ProjectOnPlane(rig.nose.position - rig.head.position, upW);
            if (fwdW.sqrMagnitude < 1e-8f) fwdW = dog.transform.forward;
            fwdW = fwdW.normalized;

            float baseUp = 0f;
            if (acc.slot == Slot.Head)
            {
                // 頭だけは皮から頭頂を測る (骨は頭蓋の奥にあるので骨基準だと埋まる)
                Vector3 rightW = Vector3.Cross(upW, fwdW).normalized;
                var skull = MeasureSkull(dog, rig.head, rig.earL, rig.earR, rig.headScale, upW, fwdW, rightW);
                if (skull.ok) baseUp = skull.topUp;
            }

            Vector3 spotW = anchor.position
                + upW * (baseUp + acc.upRatio * rig.headScale)
                + fwdW * (acc.fwdRatio * rig.headScale);

            float scale = modelSize.x > 1e-6f ? (acc.widthRatio * rig.headScale) / modelSize.x : 1f;
            Vector3 ls = anchor.lossyScale;
            float localScale = scale / Mathf.Max(1e-6f, (ls.x + ls.y + ls.z) / 3f);

            holder.localPosition = anchor.InverseTransformPoint(spotW);
            holder.localRotation = Quaternion.Inverse(anchor.rotation) * Quaternion.LookRotation(fwdW, upW);
            holder.localScale = Vector3.one * localScale;
        }

        // =====================================================================
        // 3 - Verify (数えるだけ)
        // =====================================================================

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Accessories/3 - Verify Accessory Assets")]
        public static void Verify()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_AccessorySetup] Verify (数えるだけ)");
            log.AppendLine();

            int fail = 0;

            for (int i = 0; i < Accessories.Length; i++)
            {
                var acc = Accessories[i];
                var headline = acc.displayName + " (" + acc.key + ")";

                // 100: FBX がある
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(acc.FbxPath);
                if (model == null) { log.AppendLine("FAIL 100 " + headline + " fbx 無し " + acc.FbxPath); fail++; continue; }

                // 101: 外部マテリアルがある
                var mat = AssetDatabase.LoadAssetAtPath<Material>(acc.MaterialPath);
                if (mat == null) { log.AppendLine("FAIL 101 " + headline + " material 無し " + acc.MaterialPath); fail++; }

                // 102: レンダラー 1 個 / submesh 1 / 外部マテリアル 1 スロット
                //      (Blender 版の契約 = 1 Mesh / 1 Renderer / 1 Submesh / 1 Material)
                var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != 1)
                { log.AppendLine("FAIL 102 " + headline + " MeshRenderer が " + renderers.Length + " 個 (1 個が契約)"); fail++; }

                int slots = 0, notOurs = 0;
                for (int r = 0; r < renderers.Length; r++)
                {
                    var mats = renderers[r].sharedMaterials;
                    slots += mats.Length;
                    for (int m = 0; m < mats.Length; m++) if (mats[m] != mat) notOurs++;
                }
                if (slots != 1) { log.AppendLine("FAIL 102 " + headline + " マテリアル slot " + slots + " (1 が契約)"); fail++; }
                if (notOurs > 0) { log.AppendLine("FAIL 103 " + headline + " 外部 mat 以外を指す slot " + notOurs + " 件"); fail++; }

                // 104: アーマチュアを持ち込んでいない (剛体追従で着ける前提 — ACC-001)
                var skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (skinned.Length != 0)
                { log.AppendLine("FAIL 104 " + headline + " SkinnedMeshRenderer " + skinned.Length + " 個 (剛体追従の契約に反する)"); fail++; }

                var mf = model.GetComponentInChildren<MeshFilter>(true);
                int tris = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.triangles.Length / 3 : -1;
                int sub = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.subMeshCount : -1;
                if (sub != 1) { log.AppendLine("FAIL 105 " + headline + " submesh " + sub + " (1 が契約)"); fail++; }

                if (mat != null && renderers.Length == 1 && slots == 1 && notOurs == 0 && skinned.Length == 0 && sub == 1)
                    log.AppendLine("PASS     " + headline + " 枠 " + acc.slot + " / " + tris + " tris / 1 mat");
            }

            log.AppendLine();
            log.AppendLine("== 装着枠の内訳 (同じ枠は排他。複数あってよい — ACC-005)");
            var slotValues = (Slot[])System.Enum.GetValues(typeof(Slot));
            for (int s = 0; s < slotValues.Length; s++)
            {
                int n = 0;
                for (int i = 0; i < Accessories.Length; i++) if (Accessories[i].slot == slotValues[s]) n++;
                log.AppendLine("   " + slotValues[s] + ": " + n + " 品");
            }

            log.AppendLine();
            log.AppendLine(fail == 0 ? "RESULT: PASS (FAIL 0)" : "RESULT: FAIL (" + fail + ")");
            Finish("Logs/des-dog-004-verify.txt", log);
        }

        // =====================================================================

        /// <summary>頭頂と冠線の頭幅。<see cref="MeasureSkull"/> の戻り。</summary>
        struct SkullFit
        {
            public bool ok;
            public int count;
            public int earRejected;
            public int midlineCount;
            /// <summary>実際に使った正中線の帯幅 (広げた結果)</summary>
            public float midlineUsed;
            /// <summary>頭頂の高さ (頭ボーンからの上方向成分)</summary>
            public float topUp;
            /// <summary>頭頂の前後位置 (頭ボーンからの前方向成分)</summary>
            public float topFwd;
            /// <summary>冠が乗る高さ帯での頭幅</summary>
            public float width;
        }

        /// <summary>冠線の頭幅を測る帯の深さ (headScale 比)。頭頂からこのぶん下までを見る。</summary>
        const float SkullBandRatio = 0.5f;

        /// <summary>正中線とみなす左右の幅 (headScale 比)。**頭頂はここでしか測らない** —
        /// 耳は左右に張り出しているので、正中線だけ見れば立ち耳でも耳を拾わない。</summary>
        const float MidlineRatio = 0.15f;

        /// <summary>頭蓋とみなす頭ボーンからの距離 (headScale 比)。
        /// 幅の測定でこれを超えるものを落とす — 立ち耳の先端は頭蓋よりずっと遠い。</summary>
        const float SkullRadiusRatio = 1.2f;

        /// <summary>頭ボーンに支配されている皮の頂点から、**頭頂**と**冠線の頭幅**を測る。
        ///
        /// 骨の位置は頭蓋の表面ではないので、**皮から測らないと毛に埋まるか浮く**。
        ///
        /// **耳の外し方は 2 度失敗している** (2026-09-07 実測):
        ///   1. 素通し → 立ち耳の柴・サモエドで頭幅が 20cm 超になる (耳の付け根が頭ボーン持ち)
        ///   2. 「耳ボーンに近い頂点を落とす」→ **頭頂そのものが消える**。
        ///      耳ボーンは頭蓋の天辺付近にあり、頭ボーンは頭蓋の奥にあるので、
        ///      欲しい頂点ほど耳ボーンに近い
        /// → 距離ではなく**位置**で外す。頭頂は正中線でだけ測り (<see cref="MidlineRatio"/>)、
        ///   幅は頭ボーンからの距離で頭蓋に限る (<see cref="SkullRadiusRatio"/>)。
        ///
        /// 姿勢はいまシーンに出ている状態 (= 立ち) をそのまま使う。</summary>
        static SkullFit MeasureSkull(GameObject dog, Transform head, Transform earL, Transform earR,
            float headScale, Vector3 upW, Vector3 fwdW, Vector3 rightW)
        {
            var fit = new SkullFit();
            var ups = new System.Collections.Generic.List<float>();
            var rights = new System.Collections.Generic.List<float>();
            var fwds = new System.Collections.Generic.List<float>();
            var dists = new System.Collections.Generic.List<float>();

            var renderers = dog.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var smr = renderers[r];
                var mesh = smr.sharedMesh;
                if (mesh == null || smr.bones == null) continue;

                int headIndex = -1;
                for (int b = 0; b < smr.bones.Length; b++)
                    if (smr.bones[b] == head) { headIndex = b; break; }
                if (headIndex < 0) continue;

                var bindposes = mesh.bindposes;
                if (bindposes == null || headIndex >= bindposes.Length) continue;

                var weights = mesh.boneWeights;
                var verts = mesh.vertices;
                if (weights == null || weights.Length != verts.Length) continue;

                // 支配骨 1 本だけで置く近似 (頭ウェイトが閾値を超える頂点しか見ないので十分)
                Matrix4x4 skin = head.localToWorldMatrix * bindposes[headIndex];

                for (int v = 0; v < verts.Length; v++)
                {
                    if (HeadWeight(weights[v], headIndex) < HeadWeightThreshold) continue;

                    Vector3 world = skin.MultiplyPoint3x4(verts[v]);
                    Vector3 d = world - head.position;
                    rights.Add(Vector3.Dot(d, rightW));
                    ups.Add(Vector3.Dot(d, upW));
                    fwds.Add(Vector3.Dot(d, fwdW));
                    dists.Add(d.magnitude);
                }
            }

            fit.count = ups.Count;
            if (fit.count == 0) return fit;

            // ---- 頭頂: 正中線でだけ測る (耳は左右に張り出しているので入らない) ----
            // **帯は広げながら探す。** 犬種によって頭ボーンが持つ皮の範囲がまるで違い
            // (実測 2026-09-07: 柴 823 頂点 / ラブ 100 頂点 / サモエドは正中線に 0 頂点)、
            // 固定幅だと測れない犬種が出る
            float midline = MidlineRatio * headScale;
            int top = -1;
            for (int pass = 0; pass < 4 && top < 0; pass++, midline *= 2f)
            {
                for (int i = 0; i < ups.Count; i++)
                {
                    if (Mathf.Abs(rights[i]) > midline) continue;
                    if (top < 0 || ups[i] > ups[top]) top = i;
                }
            }
            if (top < 0) return fit;   // 皮そのものが取れていない
            fit.midlineUsed = midline;
            fit.topUp = ups[top];
            fit.topFwd = fwds[top];
            fit.midlineCount = 0;
            for (int i = 0; i < ups.Count; i++) if (Mathf.Abs(rights[i]) <= midline) fit.midlineCount++;

            // ---- 冠線の頭幅: 頭頂直下の帯 x 頭蓋の半径内 ----
            float floor = fit.topUp - SkullBandRatio * headScale;
            float radius = SkullRadiusRatio * headScale;
            float maxAbs = 0f;
            for (int i = 0; i < ups.Count; i++)
            {
                if (ups[i] < floor) continue;
                if (dists[i] > radius) { fit.earRejected++; continue; }
                float a = Mathf.Abs(rights[i]);
                if (a > maxAbs) maxAbs = a;
            }
            fit.width = maxAbs * 2f;
            fit.ok = true;   // **幅が取れなくても失敗にしない** — 位置は測れているので進める
            return fit;
        }

        static float HeadWeight(BoneWeight w, int index)
        {
            float t = 0f;
            if (w.boneIndex0 == index) t += w.weight0;
            if (w.boneIndex1 == index) t += w.weight1;
            if (w.boneIndex2 == index) t += w.weight2;
            if (w.boneIndex3 == index) t += w.weight3;
            return t;
        }

        /// <summary>犬ルートからの相対パス (接続先を人が追えるように)。</summary>
        static string PathUnder(Transform root, Transform t)
        {
            string s = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        static void Finish(string file, StringBuilder log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, log.ToString(), new UTF8Encoding(false));
            Debug.Log(log.ToString());
        }
    }
}
