using System.IO;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-TRT-001 おやつ: 3 - Verify (シーン配線 / 手元メニューの検査)。本体は KD_TreatSetupEditor.cs。</summary>
    public static partial class KD_TreatSetupEditor
    {
        // 裏山改変: 古民家のおやつ一式の Verify (枠・棚・台帳を数える)。裏山の Menu/20 は VerifyHillside (このファイルの末尾)
        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Treat System/3 - Verify Treat System (Kominka)")]
        public static void Verify()
        {
            var v = new KD_VerifyLog("[KD_TreatSetup] Verify");
            var log = v.Text;

            foreach (var (path, name) in new[] {
                (ControllerProgramPath, "KD_TreatController"), (BoxProgramPath, "KD_TreatBox"),
                (SlotProgramPath, "KD_TreatSlot") })
            {
                var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                v.Check(pa != null && pa.sourceCsScript != null, "program asset ok: " + name);
            }

            int found;

            // ---- Phase 1 の進行の正本が**跡形も無い** (Step 6 で畳んだ) ----
            //
            // 残っていると進行の正本が 2 つになり、`_SetPickupable(!EatingNow)` が
            // 枠の「giver 以外は掴めない」を毎フレーム打ち消す (Step 4 の記録)。
            // **シーンとアセットの両方を数える** — スクリプトだけ消してオブジェクトが
            // 残ると「スクリプト欠損」として黙って居座る
            KD_SceneObjects.Find(LegacySystemName, out int legacySysCount);
            v.Check(legacySysCount == 0,
                LegacySystemName + " がシーンに無い (actual " + legacySysCount
                + ") — Phase 1 の進行の正本は Step 6 で畳んだ");
            v.Check(
                AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(LegacySystemProgramPath) == null,
                LegacySystemProgramPath + " が無い (プログラムアセットごと畳んだ)");
            v.Check(
                AssetDatabase.LoadAssetAtPath<MonoScript>(
                    "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatSystem.cs") == null,
                "KD_TreatSystem.cs が無い (コードからも 0 件)");

            // ---- 旧名が残っていない (改名が済んでいる証拠) ----
            foreach (var legacy in new[] { KD_DogDefs.LegacyTreatName, KD_DogDefs.LegacyTreatHomeName })
            {
                KD_SceneObjects.Find(legacy, out int legacyCount);
                v.Check(legacyCount == 0,
                    "旧名 '" + legacy + "' が残っていない (actual " + legacyCount
                    + ") — 残っていたら Scene Layout/2 - Apply");
            }

            // ---- 枠がちょうど 3 つ (実体 / 状態 / 定位置がそれぞれ 3 つ・添字 0..2) ----
            var bodyGos = new GameObject[SlotCount];
            var parkGos = new GameObject[SlotCount];
            var slotGos = new GameObject[SlotCount];
            var bodies = new KD_TreatController[SlotCount];
            var slotBs = new KD_TreatSlot[SlotCount];
            var dogTs = KD_SceneObjects.AllDogTransforms();
            int pickupLayer = LayerMask.NameToLayer("Pickup");

            for (int i = 0; i < SlotCount; i++)
            {
                string bodyName = KD_DogDefs.TreatNameOf(i);
                string homeName = KD_DogDefs.TreatHomeNameOf(i);
                string slotName = KD_DogDefs.TreatSlotNameOf(i);

                bodyGos[i] = KD_SceneObjects.Find(bodyName, out found);
                v.Check(found == 1 && bodyGos[i] != null,
                    bodyName + " がシーン内に 1 個 (actual " + found + ")");
                parkGos[i] = KD_SceneObjects.Find(homeName, out found);
                v.Check(found == 1 && parkGos[i] != null,
                    homeName + " がシーン内に 1 個 (actual " + found + ")");
                slotGos[i] = KD_SceneObjects.Find(slotName, out found);
                v.Check(found == 1 && slotGos[i] != null,
                    slotName + " がシーン内に 1 個 (actual " + found + ")");

                if (bodyGos[i] != null) bodies[i] = bodyGos[i].GetUdonSharpComponent<KD_TreatController>();
                if (slotGos[i] != null) slotBs[i] = slotGos[i].GetUdonSharpComponent<KD_TreatSlot>();
            }
            // **4 枠目を作っていない** (表を増やしたのに Verify を増やし忘れる、の逆側)
            KD_SceneObjects.Find(KD_DogDefs.TreatNameOf(SlotCount), out int overflowCount);
            v.Check(overflowCount == 0,
                KD_DogDefs.TreatNameOf(SlotCount) + " は存在しない (枠はちょうど " + SlotCount + " つ)");

            // ---- 実体の構成 (枠ごと) ----
            for (int i = 0; i < SlotCount; i++)
            {
                var treatGo = bodyGos[i];
                string tag = KD_DogDefs.TreatNameOf(i) + ": ";
                if (treatGo == null) continue;

                // **VRCObjectSync の Respawn 先はシーン開始時の world 位置**。
                // 動く親の下に置くと Respawn の意味が黙って変わる
                string treatOffender;
                v.Check(KD_SceneObjects.HasStaticAncestry(treatGo, out treatOffender),
                    tag + "先祖が動かないコンテナだけ (VRCObjectSync の Respawn 先を守る)");
                v.Check(!PrefabUtility.IsPartOfPrefabInstance(treatGo)
                        && !KD_SceneObjects.IsUnderAnyDog(treatGo.transform, dogTs),
                    tag + "犬 (購入プレハブ) の中にない");

                v.Check(treatGo.GetComponent<Rigidbody>() != null
                    && !treatGo.GetComponent<Rigidbody>().isKinematic,
                    tag + "non-kinematic Rigidbody");
                v.Check(treatGo.GetComponent<VRCObjectSync>() != null,
                    tag + "VRCObjectSync (位置同期の正本)");
                var pickup = treatGo.GetComponent<VRCPickup>();
                v.Check(pickup != null && pickup.pickupable,
                    tag + "VRCPickup (pickupable)");
                v.Check(treatGo.layer == (pickupLayer >= 0 ? pickupLayer : 13),
                    tag + "layer == Pickup (写真の許可リストに入っている層)");
                v.Check(bodies[i] != null, tag + "KD_TreatController が付いている");

                // **root は見た目を持たない** (4 種のメッシュと重なって 5 個目の絵が出る)
                v.Check(treatGo.GetComponent<MeshRenderer>() == null,
                    tag + "root に MeshRenderer が無い (見た目は 4 種のメッシュだけ)");

                // Respawn の整合: **自分の**定位置の上に居ること。
                // 定位置だけ動かして実体を置き忘れると「回収のたび旧位置へ戻る」
                if (parkGos[i] != null && bodies[i] != null)
                {
                    float d = Vector3.Distance(treatGo.transform.position, parkGos[i].transform.position);
                    v.Check(d <= bodies[i].homeRadius,
                        tag + "自分の定位置の上にある (距離 " + d.ToString("F2") + " m <= homeRadius "
                        + bodies[i].homeRadius.ToString("F2") + ") — Respawn 先の整合");
                }

                // ---- 4 種のメッシュが**その品のモデル**であること ----
                //
                // **名前だけ合っていても意味が無い** (Step 3b の罠 2 — 貼るものを間違えると
                // 4 種が全部まっ白な同じ形になり、値段差の根拠が消える)
                int n = KD_ShopSetupEditor.TreatProductCount;
                v.Check(bodies[i] != null && bodies[i].meshNodes != null
                    && bodies[i].meshNodes.Length == n
                    && bodies[i].productKeys != null && bodies[i].productKeys.Length == n,
                    tag + "meshNodes / productIds が " + n + " 本");
                for (int p = 0; p < n && bodies[i] != null && bodies[i].meshNodes != null
                        && bodies[i].meshNodes.Length == n; p++)
                {
                    string nodeName = KD_ShopSetupEditor.TreatMeshNodeAt(p);
                    var node = bodies[i].meshNodes[p];
                    if (!v.Check(node != null && node.name == nodeName,
                            tag + "meshNodes[" + p + "] == " + nodeName)) continue;
                    v.Check(bodies[i].productKeys[p] == KD_ShopSetupEditor.TreatProductKeyAt(p),
                        tag + "productIds[" + p + "] == " + KD_ShopSetupEditor.TreatProductKeyAt(p)
                        + " (" + KD_ShopSetupEditor.TreatLabelAt(p) + ")");

                    var mf = node.GetComponent<MeshFilter>();
                    Mesh want; Matrix4x4 ignored; string meshProblem;
                    bool resolved = KD_ModelNodeUtil.ResolveMeshFromFbx(
                        KD_ShopSetupEditor.TreatFbxPathAt(p), out want, out ignored, out meshProblem);
                    v.Check(resolved && mf != null && mf.sharedMesh == want,
                        tag + nodeName + " のメッシュが " + KD_ShopSetupEditor.TreatFbxPathAt(p)
                        + " 由来 " + (resolved ? "" : "(" + meshProblem + ")"));

                    var mr = node.GetComponent<MeshRenderer>();
                    var wantMat = AssetDatabase.LoadAssetAtPath<Material>(
                        KD_ShopSetupEditor.TreatMaterialPathAt(p));
                    v.Check(mr != null && wantMat != null && mr.sharedMaterial == wantMat,
                        tag + nodeName + " のマテリアルが " + KD_ShopSetupEditor.TreatMaterialPathAt(p));
                    // **`_MainTex` が null だと 4 種がまっ白な同じ形になる** (Step 3b の罠 2)
                    v.Check(wantMat != null && wantMat.HasProperty("_MainTex")
                        && wantMat.GetTexture("_MainTex") != null,
                        tag + nodeName + " のマテリアルに _MainTex がある");
                }
            }

            // ---- 定位置は「目印」であって見た目ではない (共有の皿は廃止された) ----
            for (int i = 0; i < SlotCount; i++)
            {
                if (parkGos[i] == null) continue;
                int visible = 0;
                foreach (var r in parkGos[i].GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) visible++;
                v.Check(visible == 0,
                    KD_DogDefs.TreatHomeNameOf(i) + " は見た目を持たない (enabled な Renderer "
                    + visible + " 個) — 共有の皿は廃止された");
            }

            // ---- 枠の状態 (Manual・専用 GameObject・配線) ----
            var slotPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(SlotProgramPath);
            v.Check(slotPa != null && slotPa.behaviourSyncMode == BehaviourSyncMode.Manual,
                "KD_TreatSlot の behaviourSyncMode == Manual (giverId / product / outMs)");
            for (int i = 0; i < SlotCount; i++)
            {
                string tag = KD_DogDefs.TreatSlotNameOf(i) + ": ";
                if (slotGos[i] == null) continue;
                v.Check(slotGos[i].GetComponents<VRC.Udon.UdonBehaviour>().Length == 1,
                    tag + "UdonBehaviour は 1 個 (Manual なので同居不可)");
                v.Check(slotGos[i].GetComponent<VRCObjectSync>() == null,
                    tag + "VRCObjectSync が無い (Manual 同期と同居させない)");
                if (!v.Check(slotBs[i] != null, tag + "KD_TreatSlot が付いている")) continue;
                v.Check(slotBs[i].slotIndex == i, tag + "slotIndex == " + i);
                v.Check(slotBs[i].body != null && slotBs[i].body == bodies[i],
                    tag + "body == " + KD_DogDefs.TreatNameOf(i));
                v.Check(slotBs[i].reserveTimeout > 1f && slotBs[i].abandonTimeout > 1f,
                    tag + "調整値が 0 でない (reserveTimeout / abandonTimeout)");
                // **持っている間の上限が「置いてからの猶予」より長いこと** (IMP-TRT-003)。
                // 逆だと上限が先に効いて、**持っていても持っていなくても同じ**になる
                // = 2 本立てにした意味が消える (手に持ったまま 60 秒で消える旧挙動へ戻る)
                v.Check(slotBs[i].holdAbandonTimeout > slotBs[i].abandonTimeout,
                    tag + "holdAbandonTimeout (" + slotBs[i].holdAbandonTimeout.ToString("F0")
                    + "s) > abandonTimeout (" + slotBs[i].abandonTimeout.ToString("F0")
                    + "s) — **手に持っている間だけ延ばす**");
                // **実体 → 枠の逆向きも張れていること** (片方向だと見た目が枠に従わない)
                v.Check(bodies[i] != null && bodies[i].slot == slotBs[i]
                    && bodies[i].slotIndex == i,
                    KD_DogDefs.TreatNameOf(i) + ": slot == " + KD_DogDefs.TreatSlotNameOf(i)
                    + " / slotIndex == " + i);
                v.Check(bodies[i] != null && parkGos[i] != null
                    && bodies[i].parking == parkGos[i].transform,
                    KD_DogDefs.TreatNameOf(i) + ": parking == " + KD_DogDefs.TreatHomeNameOf(i));
            }

            // ---- Phase 4b: ルールの正本と、その純関数の自己テスト ----
            var boxGo = KD_SceneObjects.Find(BoxName, out int boxCount);
            var box = (boxCount == 1 && boxGo != null) ? boxGo.GetUdonSharpComponent<KD_TreatBox>() : null;
            v.Check(boxCount == 1, BoxName + " がシーン内に 1 個 (actual " + boxCount + ")");
            if (boxGo != null)
            {
                // **Manual は専用 GameObject** — 同一 GameObject で同期方式を混在できない
                v.Check(boxGo.GetComponents<VRC.Udon.UdonBehaviour>().Length == 1,
                    BoxName + " の UdonBehaviour は 1 個 (Manual なので同居不可)");
            }
            var boxPa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(BoxProgramPath);
            v.Check(
                boxPa != null && boxPa.behaviourSyncMode == BehaviourSyncMode.Manual,
                "KD_TreatBox の behaviourSyncMode == Manual (枠と Claim の正本)");
            if (boxGo != null)
                v.Check(boxGo.GetComponent<VRCObjectSync>() == null,
                    BoxName + " に VRCObjectSync が無い (Manual 同期と同居させない)");

            if (box != null)
            {
                // 枠 3 つが**順番どおり**に入っている
                bool slotsOk = box.slots != null && box.slots.Length == SlotCount;
                for (int i = 0; slotsOk && i < SlotCount; i++)
                    if (box.slots[i] == null || box.slots[i] != slotBs[i]) slotsOk = false;
                v.Check(slotsOk,
                    BoxName + ".slots が " + SlotCount + " 本で添字どおり");
                for (int i = 0; i < SlotCount; i++)
                    v.Check(slotBs[i] != null && slotBs[i].box == box,
                        KD_DogDefs.TreatSlotNameOf(i) + ".box == " + BoxName);

                // **同期配列の長さは Start が名簿から作る** ので、エディタでは
                // 「入力の名簿が犬の数とそろっているか」までしか言えない。
                // 言えないことを言った気にならないよう、検査する対象をここに明記しておく
                // (`dogSlot` / `dogSlotOutMs` / `dogEatMs` はこの長さで確保される)
                int dogs = KD_DogDefs.All.Length;
                v.Check(box.pack != null, BoxName + ".pack 配線");
                v.Check(box.pack != null && box.pack.dogRoots != null
                    && box.pack.dogRoots.Length == dogs,
                    BoxName + ".pack の名簿が犬 " + dogs + " 匹 (actual "
                    + (box.pack == null || box.pack.dogRoots == null ? -1 : box.pack.dogRoots.Length)
                    + ") — dogSlot / dogSlotOutMs / dogEatMs はこの長さで確保される");
                v.Check(box.eatDuration > 0.01f, BoxName + ".eatDuration > 0");
                v.Check(box.thanksCooldown > 0.01f, BoxName + ".thanksCooldown > 0");
            }

            // ---- 犬側の配線と、「返事が濁らない」条件 ----
            //
            // **Phase 1 の `assignCooldown > treatThanksDuration` の置き換え。**
            // あちらは「世界に 1 個のおやつ」を前提に、食べ終わってから次の犬を呼ぶまでを
            // 全体で 1 本のクールダウンで空けていた。枠が 3 つになると
            // **同時に 3 人が出せるので、全体を止める形の保証はそもそも成り立たない**
            // (成り立たせると「他人が出したせいで自分のおやつに犬が来ない」になる)。
            //
            // 代わりに 2 つで守る:
            //   ① **1 人 1 枠 × 枠は 1 匹まで** (門番の GateAlreadyOut と Claim の単射) —
            //      **同じ人のところへ 2 匹目が走ってくることが構造的に起きない。**
            //      Phase 1 の検査が本当に防いでいたのはこれで、自己テストの不変条件 1 が数える
            //   ② **犬ごとの返事の保持時間** (`thanksCooldown`) — 食べ終わった犬は
            //      返事が終わるまで次の枠を受け持たない。ここがその機械検査
            foreach (var def in KD_DogDefs.All)
            {
                string tag = def.key + ": ";
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out int wCount);
                var wander = (wCount == 1 && wanderGo != null)
                    ? wanderGo.GetUdonSharpComponent<KD_DogWanderController>() : null;
                if (!v.Check(wander != null, tag + def.WanderSystemName + " が取れる")) continue;
                v.Check(wander.treatBox != null && wander.treatBox == box,
                    tag + "wander.treatBox == " + BoxName + " (犬はここから (枠, outMs) を読む)");
                if (box != null)
                    v.Check(box.thanksCooldown > wander.treatThanksDuration,
                        tag + "thanksCooldown (" + box.thanksCooldown.ToString("F1")
                        + ") > treatThanksDuration (" + wander.treatThanksDuration.ToString("F1")
                        + ") — 返事の途中で次のおやつへ走り出さない");
                // 食べる間合いが体で潰れない (犬がおやつへ届く前に体が支えて永久に届かない、を防ぐ床)
                v.Check(wander.treatArriveRadius > 0.01f,
                    tag + "treatArriveRadius > 0");
            }

            // ---- Phase 4b Step 5: 手元メニュー ----
            VerifyPouch(box, slotBs, v);

            // ---- NetworkIDs (**VRCObjectSync を 2 つ増やしたので必ず見る**) ----
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (desc != null && desc.NetworkIDCollection != null)
            {
                int broken = 0;
                foreach (var pair in desc.NetworkIDCollection)
                    if (pair == null || pair.gameObject == null) broken++;
                v.Check(broken == 0, "NetworkIDs broken refs == 0 (actual " + broken
                    + ", total " + desc.NetworkIDCollection.Count + ")");
            }

            if (box == null)
            {
                log.AppendLine("  SKIP 純関数の自己テスト (KD_TreatBox が無い。先に 2 - Setup)");
                v.AddFail();
            }
            else
            {
                v.AddFail(SelfTestGate(box, log));
                v.AddFail(SelfTestGive(box, log));
                v.AddFail(SelfTestDispense(box, log));
                v.AddFail(SelfTestClaims(box, log));
            }

            // ---- 食後エフェクトと効果音 (Step 7) ----
            log.AppendLine("-- 食後エフェクト --");
            v.AddFail(KD_TreatFxSetupEditor.Verify(box, log));

            v.Finish(TreatLogFile);
        }

        // ================================================================ 手元メニューの検査
        //
        // ここで落とすのは**「無言で失敗する」形**に絞ってある:
        //   同期変数が生えた / 板が開いたまま保存された / ポインタが効かない layer /
        //   onClick が二重登録 (1 回押して 2 個出る) / 理由の出し先が無い / 字が焼けていない
        // どれも Play で「押しても何も起きない」に見えて、原因が読めない類。

        static void VerifyPouch(KD_TreatBox box, KD_TreatSlot[] slotBs, KD_VerifyLog v)
        {
            var log = v.Text;

            var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(PouchProgramPath);
            v.Check(pa != null && pa.sourceCsScript != null,
                "program asset ok: KD_TreatPouch");
            v.Check(pa != null && pa.behaviourSyncMode == BehaviourSyncMode.None,
                "KD_TreatPouch の behaviourSyncMode == None (**画面は同期しない**)");

            // **同期変数 0 本。** 1 本でも生えると「他人のポーチが見える」側へ倒れうる
            int synced = 0;
            foreach (var f in typeof(KD_TreatPouch).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic))
            {
                foreach (var a in f.GetCustomAttributes(true))
                    if (a.GetType().Name == "UdonSyncedAttribute") synced++;
            }
            v.Check(synced == 0,
                "KD_TreatPouch の [UdonSynced] が 0 本 (actual " + synced + ")");

            var go = KD_SceneObjects.Find(PouchName, out int pouchCount);
            if (!v.Check(pouchCount == 1 && go != null,
                    PouchName + " がシーン内に 1 個 (actual " + pouchCount + ")"))
                return;

            var pouch = go.GetUdonSharpComponent<KD_TreatPouch>();
            if (!v.Check(pouch != null, PouchName + " に KD_TreatPouch が付いている"))
                return;

            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            v.Check(
                ub != null && ub.SyncMethod == VRC.SDKBase.Networking.SyncType.None,
                PouchName + " の backing SyncMethod == None");
            v.Check(go.layer == 0,
                PouchName + " の layer が Default (actual " + go.layer + ")");
            // ★ **当たりを持たないこと** (2026-09-01 に実物ごと廃止)。
            // 触る先が無いので Interact は要らず、**体に追従する当たりは物理で
            // プレイヤーを押しのける** (2026-08-24 の Play で実測。動いた瞬間に横へ飛ぶ)。
            v.Check(go.GetComponent<Collider>() == null,
                PouchName + " に Collider が無い "
                + "(**体に追従する当たりはプレイヤーを吹っ飛ばす**。"
                + "開くのは VR = 手のひらの向き / Desktop = キー)");
            // **実物 (革のポーチ) が残っていないこと。** 板が出る / 消えるだけが手元メニュー
            v.Check(go.transform.Find(PouchBodyName) == null
                    && go.GetComponent<MeshRenderer>() == null,
                PouchName + " に見た目が無い (" + PouchBodyName
                + " は廃止。閉じている間は世界に何も出さない)");
            // ---- VR の開き方 (2026-09-03 に右スティック長押しへ。IMP-TRT-002) ----
            //
            // 旧導線 (手のひらの向き) の検査 2 本 — axisDebug が false か /
            // showDot > hideDot か — は**判定ごと消えたのでここからも落とした。**
            // 残すと「決めていない軸」の検査だけが生き残る。
            //
            // ここで落とすのは**「倒しても開かない」「倒していないのに開く」**の 2 方向。
            v.Check(pouch.openStickSign == 1 || pouch.openStickSign == -1,
                PouchName + ".openStickSign が ±1 (actual " + pouch.openStickSign
                + ") — **0 だとどちらへ倒しても開かない**");
            v.Check(
                pouch.openStickThreshold > 0.05f && pouch.openStickThreshold <= 1f,
                PouchName + ".openStickThreshold が 0.05〜1.0 (actual "
                + pouch.openStickThreshold.ToString("F2")
                + ") — **0 だと倒していなくても常に開く**");
            v.Check(pouch.openHoldSeconds > 0.05f,
                PouchName + ".openHoldSeconds > 0.05 (actual "
                + pouch.openHoldSeconds.ToString("F2")
                + ") — **0 だと旋回のたびに板が出る**");
            // 据え置きの距離。0 だと板が顔の中に生えて読めないまま操作もできない
            v.Check(pouch.panelDistance > 0.2f,
                PouchName + ".panelDistance > 0.2 (actual "
                + pouch.panelDistance.ToString("F2") + ")");

            v.Check(go.GetComponent<VRCObjectSync>() == null,
                PouchName + " に VRCObjectSync が無い (追従はローカル。位置を配らない)");

            // ---- 配線 (未配線は Play で最も気づきにくい壊れ方) ----
            v.Check(pouch.box != null && pouch.box == box,
                PouchName + ".box == " + BoxName + " (門番と遷移表の正本)");
            v.Check(pouch.ledger != null,
                PouchName + ".ledger 配線 (**在庫を減らす唯一の口**)");

            int n = KD_ShopSetupEditor.TreatProductCount;
            bool itemsOk = pouch.items != null && pouch.items.Length == n;
            for (int i = 0; itemsOk && i < n; i++)
                if (pouch.items[i] == null
                    || pouch.items[i].productKey != KD_ShopSetupEditor.TreatProductKeyAt(i))
                    itemsOk = false;
            v.Check(itemsOk,
                PouchName + ".items が " + n + " 本で productKey が棚と同順 "
                + "(**値段・在庫・上限の正本を 2 つにしない**)");

            // ---- 板 ----
            var panel = pouch.panelRoot;
            if (!v.Check(panel != null, PouchName + ".panelRoot 配線")) return;

            // **既定で閉じている。** 開いたまま保存すると入室した瞬間から板が浮くうえ、
            // 撮影の許可レイヤーに active な Canvas が増える (Photo System/3 が数えている)
            v.Check(!panel.activeSelf,
                PouchPanelName + " が既定で非アクティブ (開いたまま保存しない)");

            var canvas = panel.GetComponent<Canvas>();
            v.Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace,
                PouchPanelName + " が WorldSpace Canvas");
            v.Check(
                panel.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null,
                PouchPanelName + " に VRCUiShape がある (**無いと VRChat でボタンが押せない**)");
            v.Check(
                panel.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null,
                PouchPanelName + " に GraphicRaycaster がある");
            v.Check(KD_HandMenuCollisionEditor.Validate(panel, log),
                PouchPanelName + " uses nonblocking world UI input");

            // ---- IMP-TRT-006: Touch の起動ボタン ----
            //
            // **Touch は今までこの板に到達できなかった** (Tab しか導線が無かった)。
            // 起動ボタンの配線が欠けると、スマホでは 3 タブ全部が再び到達不能になる
            var launcher = pouch.touchLauncher;
            v.Check(launcher != null, PouchName + ".touchLauncher 配線 (Touch の唯一の導線)");
            if (launcher != null)
            {
                // BUG-ENV-004: VRCUiShape can add a collider after upload.
                foreach (var child in launcher.GetComponentsInChildren<Transform>(true))
                    v.Check(child.gameObject.layer == KD_HandMenuCollisionEditor.InputLayer,
                        PouchLauncherName + "/" + child.name + " uses Walkthrough (actual layer="
                        + child.gameObject.layer + ")");
                v.Check(launcher.transform.parent == pouch.transform,
                    PouchLauncherName + " が " + PouchName + " の子 (板と同じ置き方)");
                v.Check(!launcher.activeSelf,
                    PouchLauncherName + " が既定で非アクティブ (出すのは実行時の Touch 判定だけ。"
                    + "有効のままだと VR / Desktop にも出るうえ ClientSim の当たりが全画面になる)");
                var lc = launcher.GetComponent<Canvas>();
                v.Check(lc != null && lc.renderMode == RenderMode.ScreenSpaceOverlay,
                    PouchLauncherName + " が Screen-Space Overlay Canvas");
                v.Check(launcher.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null,
                    PouchLauncherName + " に GraphicRaycaster がある");
                v.Check(
                    launcher.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null,
                    PouchLauncherName + " に VRCUiShape がある");
                var lbt = launcher.transform.Find(LauncherButtonName);
                v.Check(pouch.touchLauncherButton != null && pouch.touchLauncherButton == lbt,
                    "Touch launcher orientation target is wired");
                var lb = lbt != null ? lbt.GetComponent<UnityEngine.UI.Button>() : null;
                var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
                UnityEngine.Object lTarget;
                string lEv = OnClickEventName(lb, out lTarget);
                v.Check(lb != null && lEv == "_Toggle" && lTarget == pouchUdon,
                    LauncherButtonName + " の onClick が SendCustomEvent(\"_Toggle\") を "
                    + PouchName + " へ (actual " + (lEv ?? "none") + ")");
                v.Check(lb != null && lb.onClick.GetPersistentEventCount() == 1,
                    LauncherButtonName + " の onClick が 1 個 (二重登録で 1 タップ 2 回開閉にならない)");
                // **押せない状態でも配線だけは一致してしまう** (Codex レビュー 2026-09-06) — 有効状態も数える
                v.Check(lb != null && lb.enabled && lb.interactable && lb.gameObject.activeSelf,
                    LauncherButtonName + " が enabled / interactable / activeSelf (押せる状態)");
                v.Check(
                    lb != null && lb.onClick.GetPersistentEventCount() == 1
                    && lb.onClick.GetPersistentListenerState(0) != UnityEngine.Events.UnityEventCallState.Off,
                    LauncherButtonName + " の onClick が Off でない (CallState)");
                v.Check(lc != null && lc.enabled,
                    PouchLauncherName + " の Canvas が enabled");
                var lgr = launcher.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                v.Check(lgr != null && lgr.enabled,
                    PouchLauncherName + " の GraphicRaycaster が enabled");
                var lsc = launcher.GetComponent<UnityEngine.UI.CanvasScaler>();
                v.Check(
                    lsc != null && lsc.uiScaleMode == UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize,
                    PouchLauncherName + " の CanvasScaler が ScaleWithScreenSize (縦横どちらでも同じ見かけ)");
            }
            v.Check(!pouch.debugForceTouch,
                PouchName + ".debugForceTouch が false (開発用の上書き。true のまま公開すると VR / Desktop にも起動ボタンが出る)");

            v.Check(pouch.balanceLabel != null, PouchName + ".balanceLabel 配線");
            // **理由コードがあっても出す先が無ければ無言になる。** 完了条件の中心
            v.Check(pouch.statusLabel != null,
                PouchName + ".statusLabel 配線 (**理由の出し先。無いと無言で失敗する**)");
            v.Check(
                pouch.rowLabels != null && pouch.rowLabels.Length == n,
                PouchName + ".rowLabels が " + n + " 本");
            v.Check(
                pouch.rowButtons != null && pouch.rowButtons.Length == n,
                PouchName + ".rowButtons が " + n + " 本");
            v.Check(pouch.putAwayButton != null,
                PouchName + ".putAwayButton 配線 (**犬抜きで往復が閉じる口**)");
            // **閉じる道が板の中に無いと、VR は長押しを覚えるまで閉じられない**
            v.Check(pouch.closeButton != null,
                PouchName + ".closeButton 配線 (板の右上の [" + KD_TreatPouch.WordClose
                + "]。**[しまう] と違い枠には触らない**)");

            // ---- onClick が二重登録されていない ----
            //
            // 追記していくと同じイベントが 2 回飛び、**1 回押したら 2 個出る**。
            // 在庫は遷移表と世代が守るが、**押し心地の側は守られない**ので数える
            for (int i = 0; i < n; i++)
            {
                var btn = (pouch.rowButtons != null && i < pouch.rowButtons.Length)
                    ? pouch.rowButtons[i] : null;
                if (btn == null) continue;
                var b = btn.GetComponent<UnityEngine.UI.Button>();
                v.Check(
                    b != null && b.onClick.GetPersistentEventCount() == 1,
                    "Row" + i + " の onClick がちょうど 1 個 (actual "
                    + (b == null ? -1 : b.onClick.GetPersistentEventCount()) + ")");
            }
            if (pouch.putAwayButton != null)
            {
                var b = pouch.putAwayButton.GetComponent<UnityEngine.UI.Button>();
                v.Check(
                    b != null && b.onClick.GetPersistentEventCount() == 1,
                    "PutAwayButton の onClick がちょうど 1 個 (actual "
                    + (b == null ? -1 : b.onClick.GetPersistentEventCount()) + ")");
            }
            if (pouch.closeButton != null)
            {
                var b = pouch.closeButton.GetComponent<UnityEngine.UI.Button>();
                v.Check(
                    b != null && b.onClick.GetPersistentEventCount() == 1,
                    "CloseButton の onClick がちょうど 1 個 (actual "
                    + (b == null ? -1 : b.onClick.GetPersistentEventCount()) + ")");
            }

            VerifyHandMenu(pouch, panel, v);

            // ---- 枠 → ポーチ (移譲の知らせ先) ----
            for (int i = 0; i < SlotCount; i++)
                v.Check(slotBs[i] != null && slotBs[i].pouch == pouch,
                    KD_DogDefs.TreatSlotNameOf(i) + ".pouch == " + PouchName
                    + " (移譲の知らせ先)");

            // ---- 文言がフォントに入っている ----
            //
            // **エディタでは Dynamic 焼きのせいで見えてしまう**ので、目視では分からない。
            // 実機で初めて豆腐になるのを防げるのはこの検査だけ (vrc-tmp-font §3)
            //
            // **焼けているかを数える前に、焼き込みそのものが生きているかを見る**
            // (Codex レビュー 2 回目 2026-09-04)。`atlasPopulationMode` が Dynamic だと
            // **エディタでは足りない字がその場で焼かれてしまう**ので `HasCharacter` は
            // 常に true = この検査ごと無意味になる。しかもワールドは独立 Bundle なので
            // **実機では焼き足せず豆腐になる** (vrc-tmp-font §3 / `vrc-asset-bundle`)。
            // 容量測定 (`ShopSetup` §フォント) が Dynamic へ倒して戻す手順を持つぶん、
            // **戻し忘れが起こりうる** — その事故を捕まえるのはここ
            var sharedFont = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                KD_ShopSetupEditor.SharedFontPath);
            v.Check(sharedFont != null,
                "共有フォントが在る (" + KD_ShopSetupEditor.SharedFontPath + ")");
            v.Check(
                sharedFont != null
                && sharedFont.atlasPopulationMode == TMPro.AtlasPopulationMode.Static,
                "共有フォントのアトラスが Static (実際 "
                + (sharedFont == null ? "フォント無し"
                    : sharedFont.atlasPopulationMode.ToString())
                + ") — **Dynamic だと下の欠け 0 字が嘘になり、実機でだけ豆腐になる**");

            string missing = KD_ShopSetupEditor.MissingGlyphs(PouchWords());
            v.Check(missing.Length == 0,
                "手元メニューの文言がフォントに入っている (欠け " + missing.Length + " 字"
                + (missing.Length == 0 ? "" : ": " + missing) + ")");
        }

        /// <summary>手元メニュー (IMP-TRT-004 / FET-011 / PET-007) の検査。
        ///
        /// **見た目が壊れるだけで例外もログも出ないもの**を機械で捕まえるのがここの仕事:
        /// 板が縦に伸びる / `Sliced` が `Filled` や `Simple` へ戻る /
        /// タブの口が足りない / 押せない行の見せ方が配線されていない。</summary>
        static void VerifyHandMenu(KD_TreatPouch pouch, GameObject panel, KD_VerifyLog v)
        {
            var log = v.Text;
            int tabs = KD_TreatPouch.TabCount;

            // ---- 板の寸法。**縦は 520px を超えない** (hand-menu §4) ----
            //
            // **却下の基準は書いた時点で終わりではなく、以後ずっと自分にも掛かる**
            // (Avatar Idle Arena `IMP-UI-009` 一般則①)。あちらは「2m を超える」で
            // 案を落としたのに、その板自身が増床 5 回で 2.13m になっていた。
            // **入れておかないと、タブを 4 つ目にしたくなった日に静かに破られる**
            var prect = panel.GetComponent<RectTransform>();
            v.Check(prect != null && prect.sizeDelta.y <= PanelHeightLimit,
                PouchPanelName + " の縦が " + PanelHeightLimit + "px 以下 (actual "
                + (prect == null ? -1f : prect.sizeDelta.y)
                + ") — **行が増えたら縦ではなく 2 列グリッドで受ける**");
            v.Check(prect != null && Mathf.Approximately(prect.sizeDelta.x, PanelWidth),
                PouchPanelName + " の横が " + PanelWidth + "px");

            // ---- タブ ----
            //
            // **横にも上限を置く** (IMP-INF-006 F-12)。縦 520px だけを見張っていたので、
            // タブは 3 → 4 → 5 → 6 枚と増えるたびに 1 枚の幅を 200 → 180 → 140 → 120px と
            // 詰めて 1 段に収めてきた。**次の 1 枚で 1 段に入らなくなる** — そのとき
            // 「2 段にする」か「板を横へ広げる」を無自覚に選ぶことになるので、
            // ここで 1 段に収まることを機械検査して**選択を意識的にさせる**
            // (一般則①: 書いた却下は以後ずっと自分にも掛かる)。
            v.Check(tabs * TabStep <= PanelWidth,
                "タブ " + tabs + " 枚が板の横 " + PanelWidth + "px に 1 段で収まる (actual "
                + (tabs * TabStep) + "px) — **増やすなら「使う」以外を入れていないか**"
                + " (hand-menu §9) を先に確かめる");

            v.Check(pouch.tabPages != null && pouch.tabPages.Length == tabs,
                PouchName + ".tabPages が " + tabs + " 枚");
            v.Check(pouch.tabImages != null && pouch.tabImages.Length == tabs,
                PouchName + ".tabImages が " + tabs + " 本 (選択中の色を書く先)");
            v.Check(pouch.tabUnderlines != null && pouch.tabUnderlines.Length == tabs,
                PouchName + ".tabUnderlines が " + tabs + " 本 "
                + "(**色だけだと明度差が弱い環境で読めない**)");

            // **既定は左端の「かざり」だけ開いている。** 実行時のStartと合わせる。
            if (pouch.tabPages != null)
            {
                for (int i = 0; i < pouch.tabPages.Length; i++)
                {
                    var page = pouch.tabPages[i];
                    if (page == null) { v.Check(false, "tabPages[" + i + "] 未配線"); continue; }
                    bool want = i == KD_TreatPouch.TabAcc;
                    v.Check(page.activeSelf == want,
                        page.name + " の既定が " + (want ? "アクティブ" : "非アクティブ")
                        + " (**既定タブはかざり**)");
                }
            }

            // ---- タブは角のまま。**丸みと枠は押せるものだけ** (一般則③) ----
            //
            // 逆に「意匠が揃っていない」と誤検出しないよう、**タブは Sliced でないこと**を要求する
            for (int i = 0; i < tabs; i++)
            {
                var img = (pouch.tabImages != null && i < pouch.tabImages.Length)
                    ? pouch.tabImages[i] : null;
                if (img == null) continue;
                v.Check(img.type != UnityEngine.UI.Image.Type.Sliced,
                    "Tab" + i + " が角のまま (タブは押せる面ではなく見出し。"
                    + "丸みを広げると板の中で操作対象と表示物が混ざる)");
            }

            // ---- 色スウォッチは円スプライト + Simple (IMP-TRT-005) ----
            //
            // 角丸スプライトの流用や `Sliced` へ戻ると「玉」が角丸四角に化ける。
            // **見た目が壊れるだけで例外もログも出ない**ので機械で見張る
            var wantCircle = KD_UiSpriteEditor.LoadCircle();
            v.Check(wantCircle != null,
                "円スプライトが焼かれている (" + KD_UiSpriteEditor.CircleSpritePath + ")");
            foreach (var img in panel.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img.name != "Swatch") continue;
                v.Check(
                    img.sprite == wantCircle && img.type == UnityEngine.UI.Image.Type.Simple,
                    img.transform.parent.name + "/Swatch が円スプライト + Simple (実際 "
                    + (img.sprite == null ? "sprite無し" : img.sprite.name)
                    + " / " + img.type + ")");
            }

            // ---- 押せるものは全部 Sliced ----
            //
            // **`Filled` は 9-slice のボーダーを無視して角が破綻する** (「角がびよーん」)。
            // `Simple` も同じ。**見た目が壊れるだけで例外もログも出ない**ので機械で見張る
            int sliced = 0, notSliced = 0;
            foreach (var b in panel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (b.name.StartsWith("Tab")) continue;          // タブは対象外 (上で逆を検査済み)
                if (b.name == ConfirmPanelName) continue;         // P13 の暗幕: 押せる面ではなく「後ろを押せなくする」面。角丸も枠も当てない
                if (b.name == "Card" && b.transform.parent != null && b.transform.parent.name == ConfirmPanelName) continue;   // カードのクリック止め (何もしない Button)
                var img = b.GetComponent<UnityEngine.UI.Image>();
                if (img == null) continue;
                if (img.type == UnityEngine.UI.Image.Type.Sliced) sliced++;
                else { notSliced++; log.AppendLine("  FAIL " + b.name + " が Sliced でない (" + img.type + ")"); }
            }
            v.Check(notSliced == 0,
                "板の全ボタン背景が Sliced (" + sliced + " 枚 / 違反 " + notSliced + ")");

            // ---- おもちゃタブ (IMP-FET-011) ----
            int dogs = KD_DogDefs.All.Length;
            v.Check(pouch.balls != null && pouch.balls.Length == dogs,
                PouchName + ".balls が " + dogs + " 本 (犬 1 匹 = 1 行)");
            v.Check(pouch.toyFetchers != null && pouch.toyFetchers.Length == dogs,
                PouchName + ".toyFetchers が " + dogs + " 本 "
                + "(**「待機中」は !IsHeldNow ∧ IsIdle の AND から導出する**)");
            if (pouch.balls != null)
                for (int i = 0; i < pouch.balls.Length; i++)
                    v.Check(pouch.balls[i] != null, "balls[" + i + "] 配線");
            if (pouch.toyFetchers != null)
                for (int i = 0; i < pouch.toyFetchers.Length; i++)
                    v.Check(pouch.toyFetchers[i] != null, "toyFetchers[" + i + "] 配線");

            // **押せない行を「暗くする」ための配線。** 欠けると押せないことが見た目に出ない
            v.Check(
                pouch.toyButtonComponents != null && pouch.toyButtonComponents.Length == dogs,
                PouchName + ".toyButtonComponents が " + dogs + " 本 "
                + "(**押せないときに interactable を落とす先**)");
            v.Check(
                pouch.toyButtonImages != null && pouch.toyButtonImages.Length == dogs,
                PouchName + ".toyButtonImages が " + dogs + " 本 (消灯色を書く先)");
            v.Check(
                pouch.toyReasonLabels != null && pouch.toyReasonLabels.Length == dogs,
                PouchName + ".toyReasonLabels が " + dogs + " 本 "
                + "(**理由の出し先。無いと「押せないだけ」で理由が読めない**)");
            v.Check(pouch.toyNames != null && pouch.toyNames.Length == dogs,
                PouchName + ".toyNames が " + dogs + " 本");

            // **消灯色と点灯色が別物であること。** 同じだと「押せない」が見た目に出ない
            v.Check(pouch.recallOnColor != pouch.recallOffColor,
                PouchName + " の [" + KD_TreatPouch.WordRecall + "] 点灯色と消灯色が違う");
            v.Check(pouch.tabOnColor != pouch.tabOffColor,
                PouchName + " のタブ点灯色と消灯色が違う");

            v.Check(pouch.recallDistance > 0.3f,
                PouchName + ".recallDistance > 0.3 (actual "
                + pouch.recallDistance.ToString("F2")
                + ") — 0 だと呼んだおもちゃが足の中に生えて掴めない");
            v.Check(pouch.recallCooldown > 0.05f,
                PouchName + ".recallCooldown > 0.05 (actual "
                + pouch.recallCooldown.ToString("F2") + ") — 0 だと連打が素通りする");

            // ---- なでタブ (IMP-PET-007) ----
            int fxN = KD_ShopSetupEditor.PetFxProductCount;
            v.Check(pouch.catalog != null,
                PouchName + ".catalog 配線 (撫でエフェクトを切り替える唯一の口)");
            v.Check(pouch.petFxBox != null,
                PouchName + ".petFxBox 配線 (「使用中」の判定)");
            v.Check(pouch.fxItems != null && pouch.fxItems.Length == fxN,
                PouchName + ".fxItems が " + fxN + " 本 (**棚と同じ KD_ShopItem を指す**)");
            v.Check(pouch.fxLabels != null && pouch.fxLabels.Length == fxN,
                PouchName + ".fxLabels が " + fxN + " 本");
            v.Check(pouch.fxButtons != null && pouch.fxButtons.Length == fxN,
                PouchName + ".fxButtons が " + fxN + " 本");
            v.Check(
                pouch.fxButtonLabels != null && pouch.fxButtonLabels.Length == fxN,
                PouchName + ".fxButtonLabels が " + fxN + " 本 "
                + "(**[つける] と [買う] を切り替える先**)");
            if (pouch.fxItems != null)
                for (int i = 0; i < pouch.fxItems.Length; i++)
                    v.Check(pouch.fxItems[i] != null, "fxItems[" + i + "] 配線");

            // ---- 2 段階購入の確認帯 ----
            //
            // **触った瞬間に pt が減る形になっていないこと**が、ここでいちばん守りたいこと。
            // 帯が無い / 既定で開いている / [購入] が未配線 のいずれも
            // 「押したら即決済」か「買えない」のどちらかへ倒れる
            // P13: 確認は板の手前の共有カード 1 枚。各売り場の参照はそのカードを指す
            v.Check(pouch.confirmRoot != null && pouch.confirmTitleLabel != null && pouch.confirmBodyLabel != null
                && pouch.confirmNoteLabel != null && pouch.confirmYes != null && pouch.confirmNo != null
                && pouch.confirmYesLabel != null && pouch.confirmNoLabel != null,
                PouchName + ".confirm* 配線 (**購入の確認カード。無いと即決済になる**)");
            v.Check(pouch.confirmRoot != null && pouch.fxConfirmRoot == pouch.confirmRoot
                && pouch.liveConfirmRoot == pouch.confirmRoot && pouch.accConfirmRoot == pouch.confirmRoot,
                "確認の口が共有カード 1 本 (fx / live / acc が同じ confirmRoot を指す)");
            if (pouch.confirmRoot != null)
            {
                var yesBtn = pouch.confirmYes != null ? pouch.confirmYes.GetComponent<UnityEngine.UI.Button>() : null;
                var noBtn = pouch.confirmNo != null ? pouch.confirmNo.GetComponent<UnityEngine.UI.Button>() : null;
                UnityEngine.Object yesTarget, noTarget;
                v.Check(OnClickEventName(yesBtn, out yesTarget) == "_ConfirmYes" && yesTarget == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch),
                    "確認カードの [購入] が _ConfirmYes へ配線");
                v.Check(OnClickEventName(noBtn, out noTarget) == "_ConfirmNo" && noTarget == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch),
                    "確認カードの [やめる] が _ConfirmNo へ配線");
                var dimBtn = pouch.confirmRoot.GetComponent<UnityEngine.UI.Button>();
                var dimImg = pouch.confirmRoot.GetComponent<UnityEngine.UI.Image>();
                UnityEngine.Object dimTarget;
            {
                var cardT = pouch.confirmRoot == null ? null : pouch.confirmRoot.transform.Find("Card");
                var cardBtn = cardT == null ? null : cardT.GetComponent<UnityEngine.UI.Button>();
                v.Check(cardBtn != null && cardBtn.onClick.GetPersistentEventCount() == 0 && cardBtn.transition == UnityEngine.UI.Selectable.Transition.None,
                    "確認カードの余白を押しても暗幕の [やめる] へ抜けない (Card に何もしない Button)");
            }
                v.Check(dimImg != null && dimImg.raycastTarget && OnClickEventName(dimBtn, out dimTarget) == "_ConfirmNo",
                    "暗幕が押下を受け、押すと [やめる] (後ろの行を押せない)");
                v.Check(pouch.confirmRoot.transform.parent == pouch.panelRoot.transform
                    && pouch.confirmRoot.transform.GetSiblingIndex() == pouch.panelRoot.transform.childCount - 1,
                    "確認カードが板直下の最後の子 (最前面)");
            }
            v.Check(pouch.fxConfirmRoot != null,
                PouchName + ".fxConfirmRoot 配線 (共有カード)");
            v.Check(pouch.fxConfirmLabel != null,
                PouchName + ".fxConfirmLabel 配線 (値段の出し先)");
            v.Check(pouch.fxConfirmYes != null,
                PouchName + ".fxConfirmYes 配線 ([" + KD_ShopShelf.WordBuy + "])");
            v.Check(pouch.fxConfirmNo != null,
                PouchName + ".fxConfirmNo 配線 ([" + KD_ShopShelf.WordCancel + "])");
            if (pouch.fxConfirmRoot != null)
                v.Check(!pouch.fxConfirmRoot.activeSelf,
                    "確認カードが既定で非アクティブ (開いたまま保存しない)");

            // **確認の型は 1 つ。** 板の中に確認の口が 2 つ生えていないこと
            // (旧 `KD_ShopMenu` は IMP-PET-007 でクラスごと削除した)
            v.Check(
                panel.GetComponentsInChildren<KD_ShopShelf>(true).Length == 0
                && panel.GetComponentsInChildren<KD_ShopItem>(true).Length == 0,
                "板の中に棚も商品も居ない (確認の口は共有カード 1 本だけ)");

            // ---- ライブタブ (IMP-PRG-004 Phase 4 / IMP-PRG-005 で 6 曲) ----
            //
            // 欠けると ①[開催] が暗いまま (live 未配線) ②値段だけ出て買えない (liveItems 未配線)
            // ③別の曲が開催される (liveItems の添字と liveId のずれ) の 3 通り
            {
                var liveShows = KD_DogDefs.LiveShows;
                int liveN = liveShows.Length;
                v.Check(liveN <= 9, "LiveShows が 9 本以下 (板の口 _Live0.._Live8。actual " + liveN + ")");
                int catalogLive = 0;
                if (pouch.catalog != null && pouch.catalog.items != null)
                    foreach (var it in pouch.catalog.items)
                        if (it != null && it.categoryKey == KD_ShopSetupEditor.CategoryLive) catalogLive++;
                v.Check(catalogLive >= 1, "カタログに Live の商品が " + catalogLive + " 本 (1 本以上。Shop Setup が作る)");
                v.Check(pouch.live != null, PouchName + ".live 配線 (**開催・残り秒・待機の正本**)");
                v.Check(pouch.liveItems != null && pouch.liveItems.Length == liveN,
                    PouchName + ".liveItems が " + liveN + " 本 (行 = 作品。添字 = liveId)");
                v.Check(pouch.liveTitles != null && pouch.liveTitles.Length == liveN,
                    PouchName + ".liveTitles が " + liveN + " 本 (行の曲名)");
                if (pouch.liveItems != null && pouch.live != null)
                    v.Check(pouch.live.stageDirectors != null
                        && pouch.live.stageDirectors.Length == pouch.liveItems.Length,
                        "liveItems の本数 == KD_LiveSystem.stageDirectors の本数 (添字 = liveId がずれない)");
                v.Check(pouch.liveLabels != null && pouch.liveLabels.Length == liveN,
                    PouchName + ".liveLabels が " + liveN + " 本");
                v.Check(pouch.liveSubLabels != null && pouch.liveSubLabels.Length == liveN,
                    PouchName + ".liveSubLabels が " + liveN + " 本 ([Pack] 札と値段の出し先)");
                v.Check(pouch.liveButtons != null && pouch.liveButtons.Length == liveN
                    && pouch.liveButtonComponents != null && pouch.liveButtonComponents.Length == liveN
                    && pouch.liveButtonImages != null && pouch.liveButtonImages.Length == liveN
                    && pouch.liveButtonLabels != null && pouch.liveButtonLabels.Length == liveN,
                    PouchName + ".liveButton* が " + liveN + " 本ずつ");
                v.Check(pouch.liveGiftButtons != null && pouch.liveGiftButtons.Length == liveN
                    && pouch.liveGiftLabels != null && pouch.liveGiftLabels.Length == liveN,
                    PouchName + ".liveGift* が " + liveN + " 本ずつ (IMP-PRG-012)");

                // **行ごとに中身まで見る** — 長さだけでは「別の曲の商品」「隣の行の _Live{j}」が通る
                var liveUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
                for (int i = 0; i < liveN; i++)
                {
                    string tag = "ライブ行 " + i + " (" + liveShows[i].slug + ") ";
                    var it = ArrayAt(pouch.liveItems, i);
                    v.Check(it != null && it.categoryKey == KD_ShopSetupEditor.CategoryLive
                        && it.productKey == liveShows[i].productKey,
                        tag + "の liveItems.productKey == productKey " + liveShows[i].productKey
                        + " (actual " + (it == null ? "null" : it.productKey.ToString()) + ")");
                    string title = ArrayAt(pouch.liveTitles, i);
                    v.Check(title == liveShows[i].title,
                        tag + "の liveTitles == \"" + liveShows[i].title + "\" (actual \"" + (title ?? "null") + "\")");
                    var lbl = ArrayAt(pouch.liveLabels, i);
                    var sub = ArrayAt(pouch.liveSubLabels, i);
                    var btnCo = ArrayAt(pouch.liveButtonComponents, i);
                    var btnLbl = ArrayAt(pouch.liveButtonLabels, i);
                    v.Check(lbl != null && sub != null && btnCo != null && btnLbl != null,
                        tag + "の 4 本が非 null (曲名" + (lbl != null ? "○" : "×") + " / 2 行目" + (sub != null ? "○" : "×")
                        + " / Button" + (btnCo != null ? "○" : "×") + " / ボタン字" + (btnLbl != null ? "○" : "×") + ")");
                    // 押し先の名前と宛先
                    string want = "_Live" + i.ToString();
                    UnityEngine.Object tgt;
                    string got = OnClickEventName(btnCo, out tgt);
                    v.Check(got == want && liveUdon != null && tgt == (UnityEngine.Object)liveUdon,
                        tag + "の onClick が SendCustomEvent(\"" + want + "\") で " + PouchName + " 宛 (実際 \""
                        + (got == null ? "無し" : got) + "\" / " + (tgt == null ? "宛先無し" : tgt.name) + ")");
                    // 口が実在する (SendCustomEvent は無い名前でも例外を出さない)
                    v.Check(typeof(KD_TreatPouch).GetMethod(want,
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) != null,
                        "KD_TreatPouch に " + want + "() がある");
                    // [ギフト] (IMP-PRG-012): 既定で非表示、onClick は _LiveGift{i}
                    var giftGo = ArrayAt(pouch.liveGiftButtons, i);
                    var giftBtn = giftGo == null ? null : giftGo.GetComponent<UnityEngine.UI.Button>();
                    string wantGift = "_LiveGift" + i.ToString();
                    UnityEngine.Object giftTgt;
                    string gotGift = OnClickEventName(giftBtn, out giftTgt);
                    v.Check(giftGo != null && !giftGo.activeSelf && gotGift == wantGift && giftTgt == (UnityEngine.Object)liveUdon
                        && ArrayAt(pouch.liveGiftLabels, i) != null
                        && typeof(KD_TreatPouch).GetMethod(wantGift, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) != null,
                        tag + "の [ギフト] が既定非表示で onClick が SendCustomEvent(\"" + wantGift + "\") (実際 \"" + (gotGift == null ? "無し" : gotGift) + "\")");
                    // 最長題の溢れ (Overflow なので黙って隣へ描かれる — `vrc-tmp-font` §4)
                    if (lbl != null && lbl.font != null)
                    {
                        float need = PreferredUiWidth(lbl, liveShows[i].title);
                        float have = lbl.rectTransform.sizeDelta.x;
                        v.Check(need <= have,
                            tag + "の曲名が枠に収まる (要 " + need.ToString("F0") + "px / 枠 " + have.ToString("F0") + "px)");
                    }
                }
                v.Check(pouch.liveStatusLabel != null, PouchName + ".liveStatusLabel 配線 (残り秒の出し先)");
                // P7 / P8 (2026-09-16): 一覧の行 = 商品 (安い → 高い、CE は最後)、詳細の面の配線
                var liveProducts = KD_DogDefs.LiveProducts;
                v.Check(pouch.liveProducts != null && pouch.liveProducts.Length == liveProducts.Length,
                    PouchName + ".liveProducts が " + liveProducts.Length + " 本 (一覧の行 = 商品。P7)");
                if (pouch.liveProducts != null)
                {
                    string order = ""; bool ordered = true;
                    for (int i = 0; i < pouch.liveProducts.Length; i++)
                    {
                        var it = pouch.liveProducts[i];
                        v.Check(it != null && i < liveProducts.Length && it.productKey == liveProducts[i].productKey
                            && System.Array.IndexOf(pouch.liveItems ?? new KD_ShopItem[0], it) >= 0,
                            "ライブ商品行 " + i + " が " + (i < liveProducts.Length ? liveProducts[i].productKey : "?") + " で、作品の表 (liveItems) に居る");
                        if (it == null) continue;
                        order += (i > 0 ? " > " : "") + (it.IsCE() ? it.ceCredits + "C" : it.price + "pt");
                        if (i > 0 && pouch.liveProducts[i - 1] != null)
                        {
                            var prev = pouch.liveProducts[i - 1];
                            if (prev.IsCE() && !it.IsCE()) ordered = false;
                            else if (prev.IsCE() == it.IsCE() && (it.IsCE() ? prev.ceCredits > it.ceCredits : prev.price > it.price)) ordered = false;
                        }
                    }
                    v.Check(ordered, "ライブ商品の並びが 安い → 高い、CE は最後 (P9): " + order);
                }
                v.Check(pouch.liveDetailRoot != null && pouch.liveDetailTitle != null && pouch.liveDetailMeta != null && pouch.liveDetailMood != null
                    && pouch.liveDetailScene != null && pouch.liveDetailSongs != null && pouch.liveDetailFx != null && pouch.liveDetailAcc != null
                    && pouch.liveDetailNote != null && pouch.liveDetailPosterLabel != null && pouch.liveDetailSongsRow != null
                    && pouch.liveDetailFxRow != null && pouch.liveDetailAccRow != null,
                    PouchName + ".liveDetail* の面と文字が配線 (P8。無いと [くわしく] が空の面を出す)");
                v.Check(pouch.liveDetailRoot != null && !pouch.liveDetailRoot.activeSelf, "LiveDetail は既定で非アクティブ (一覧から開く)");
                // 参考画像 (2026-09-17): 7 商品すべて (Textures/LiveThumbs)。欠けは色面で動くが、公開前は揃える
                v.Check(pouch.liveDetailPosterImage != null && pouch.liveDetailPosterImage.preserveAspect, PouchName + ".liveDetailPosterImage 配線 (preserveAspect)");
                v.Check(pouch.liveThumbs != null && pouch.liveProducts != null && pouch.liveThumbs.Length == pouch.liveProducts.Length,
                    PouchName + ".liveThumbs が liveProducts と同じ本数");
                if (pouch.liveThumbs != null && pouch.liveProducts != null)
                    for (int i = 0; i < pouch.liveThumbs.Length && i < pouch.liveProducts.Length; i++)
                        v.Check(pouch.liveThumbs[i] != null, "参考画像 " + (pouch.liveProducts[i] == null ? "?" : pouch.liveProducts[i].productKey) + " (Textures/LiveThumbs/<productKey>.png)");
                v.Check(pouch.liveDetailBack != null && pouch.liveDetailAction != null && pouch.liveDetailActionButton != null
                    && pouch.liveDetailActionImage != null && pouch.liveDetailActionLabel != null
                    && !pouch.liveDetailBack.activeSelf && !pouch.liveDetailAction.activeSelf,
                    PouchName + ".liveDetailBack / Action 配線 (既定で非アクティブ)");
                {
                    var udonTarget = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
                    UnityEngine.Object tgt;
                    var backBtn = pouch.liveDetailBack == null ? null : pouch.liveDetailBack.GetComponent<UnityEngine.UI.Button>();
                    v.Check(backBtn != null && OnClickEventName(backBtn, out tgt) == "_LiveBack" && tgt == udonTarget, "[もどる] が _LiveBack へ配線");
                    v.Check(pouch.liveDetailActionButton != null && OnClickEventName(pouch.liveDetailActionButton, out tgt) == "_LiveDetailAction" && tgt == udonTarget, "[買う / 開催] が _LiveDetailAction へ配線");
                    var fxBtn = pouch.liveDetailFxRow == null ? null : pouch.liveDetailFxRow.GetComponentInChildren<UnityEngine.UI.Button>(true);
                    var accBtn = pouch.liveDetailAccRow == null ? null : pouch.liveDetailAccRow.GetComponentInChildren<UnityEngine.UI.Button>(true);
                    v.Check(fxBtn != null && OnClickEventName(fxBtn, out tgt) == "_LiveSeeFx" && tgt == udonTarget, "[パーティクルで見る] が _LiveSeeFx へ配線");
                    v.Check(accBtn != null && OnClickEventName(accBtn, out tgt) == "_LiveSeeAcc" && tgt == udonTarget, "[かざりで見る] が _LiveSeeAcc へ配線");
                }
                // P10: インフォメーション帯 (既定で非アクティブ、_EventBand へ配線、ヘッダとタブの間)
                v.Check(pouch.eventBandRoot != null && pouch.eventBandLabel != null && !pouch.eventBandRoot.activeSelf,
                    PouchName + ".eventBand* 配線 (既定で非アクティブ。販売中だけ実行時に出す)");
                if (pouch.eventBandRoot != null)
                {
                    UnityEngine.Object bandTgt;
                    var bandBtn = pouch.eventBandRoot.GetComponent<UnityEngine.UI.Button>();
                    v.Check(bandBtn != null && OnClickEventName(bandBtn, out bandTgt) == "_EventBand" && bandTgt == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch),
                        "インフォメーション帯が _EventBand へ配線");
                    var bandRect = (RectTransform)pouch.eventBandRoot.transform;
                    var tab0 = pouch.tabImages != null && pouch.tabImages.Length > 0 ? (RectTransform)pouch.tabImages[0].transform : null;
                    var title = pouch.sectionTitleLabel == null ? null : pouch.sectionTitleLabel.rectTransform;
                    v.Check(tab0 != null && title != null
                        && bandRect.anchoredPosition.y - bandRect.sizeDelta.y / 2f >= tab0.anchoredPosition.y + tab0.sizeDelta.y / 2f - 0.5f
                        && bandRect.anchoredPosition.y + bandRect.sizeDelta.y / 2f <= title.anchoredPosition.y - title.sizeDelta.y / 2f + 0.5f,
                        "インフォメーション帯がヘッダとタブの間に収まる (帯 " + bandRect.anchoredPosition.y + " / タブ上端 " + (tab0 == null ? "?" : (tab0.anchoredPosition.y + tab0.sizeDelta.y / 2f).ToString()) + ")");
                    if (pouch.eventBandLabel != null && pouch.eventBandLabel.font != null)
                    {
                        var bandCatalog = KD_LocalizationEditor.Load();
                        foreach (var lang in new[] { "ja", "en" }) foreach (var key in new[] { "event.halloween.text", "event.halloween.owned" })
                        {
                            string text = KD_LocalizationEditor.Text(bandCatalog, lang, key);
                            float need = PreferredUiWidth(pouch.eventBandLabel, text);
                            v.Check(need <= pouch.eventBandLabel.rectTransform.sizeDelta.x, "帯の文 " + lang + " " + key + " が収まる (要 " + need.ToString("F0") + "px)");
                        }
                    }
                }
                // 商品名が行の枠に収まる (一覧の行 = 商品名。P7)
                if (pouch.liveProducts != null && pouch.liveLabels != null && pouch.liveLabels.Length > 0 && pouch.liveLabels[0] != null && pouch.liveLabels[0].font != null)
                {
                    var catalogForNames = KD_LocalizationEditor.Load();
                    foreach (var it in pouch.liveProducts)
                    {
                        if (it == null) continue;
                        foreach (var lang in new[] { "ja", "en" })
                        {
                            string name = KD_LocalizationEditor.Text(catalogForNames, lang, it.displayNameKey);
                            float need = PreferredUiWidth(pouch.liveLabels[0], name);
                            float have = pouch.liveLabels[0].rectTransform.sizeDelta.x;
                            v.Check(need <= have, "ライブ商品名 " + lang + " \"" + name + "\" が行の枠に収まる (要 " + need.ToString("F0") + "px / 枠 " + have.ToString("F0") + "px)");
                            // IMP-PRG-012: 所有済みの買い切り CE は右上に [ギフト] が出て名前の幅が 340 に詰まる (RedrawLive)。Point 商品には出ない
                            if (it.IsCE())
                                v.Check(need <= 340f, "ライブ商品名 " + lang + " \"" + name + "\" が [ギフト] 表示時の枠 340px に収まる (要 " + need.ToString("F0") + "px)");
                        }
                    }
                    // かざりの商品一覧でも CE セットの行は [ギフト] で名前の幅が 300 に詰まる (RedrawAcc)
                    if (pouch.accItems != null && pouch.accNameKeys != null)
                        for (int a = 0; a < pouch.accItems.Length && a < pouch.accNameKeys.Length; a++)
                        {
                            var it = pouch.accItems[a];
                            if (it == null || !it.IsCE() || string.IsNullOrEmpty(pouch.accNameKeys[a])) continue;
                            // セットの中身 (単品) は商品一覧に並ばず [ギフト] も出ない (セット単位で贈る) ので測らない
                            if (pouch.accessories != null && pouch.accessories.setMembers != null && pouch.accProductIds != null && a < pouch.accProductIds.Length
                                && System.Array.IndexOf(pouch.accessories.setMembers, pouch.accProductIds[a]) >= 0) continue;
                            foreach (var lang in new[] { "ja", "en" })
                            {
                                string name = KD_LocalizationEditor.Text(catalogForNames, lang, pouch.accNameKeys[a]);
                                float need = PreferredUiWidth(pouch.liveLabels[0], name);
                                v.Check(need <= 300f, "かざりの CE セット名 " + lang + " \"" + name + "\" が [ギフト] 表示時の枠 300px に収まる (要 " + need.ToString("F0") + "px)");
                            }
                        }
                }
                v.Check(pouch.tabPages != null && pouch.tabPages.Length > KD_TreatPouch.TabLive
                    && pouch.tabPages[KD_TreatPouch.TabLive] != null
                    && pouch.tabPages[KD_TreatPouch.TabLive].transform.Find("AvatarDarkButton") == null,
                    "ライブタブに AvatarDarkButton が無い (2026-09-06 廃止)");
                v.Check(pouch.liveConfirmRoot != null && pouch.liveConfirmLabel != null
                    && pouch.liveConfirmYes != null && pouch.liveConfirmNo != null,
                    PouchName + ".liveConfirm* 配線 (共有カード。無いと即決済になる)");
                // 確認カードの題 (商品名 + 価格) が 1〜2 行に収まる — 商品ごとに実測 (KD_TreatPouch._ShowItemConfirm の式と同じ形)
                if (pouch.confirmTitleLabel != null && pouch.confirmTitleLabel.font != null)
                {
                    float have = pouch.confirmTitleLabel.rectTransform.sizeDelta.x * 2f;   // 折り返し 2 行まで
                    var catalog = KD_LocalizationEditor.Load();
                    string template = KD_LocalizationEditor.Text(catalog, "ja", "confirm.buyTitle");
                    foreach (var p in KD_DogDefs.LiveProducts)
                    {
                        string worst = template.Replace("{0}", p.displayName).Replace("{1}", p.price.ToString() + " pt");
                        float need = PreferredUiWidth(pouch.confirmTitleLabel, worst);
                        v.Check(need <= have,
                            "確認カードの題 \"" + worst + "\" が 2 行に収まる (要 " + need.ToString("F0") + "px / 枠 " + have.ToString("F0") + "px)");
                    }
                }
            }

            // ---- ねるタブ (IMP-DOG-019 Phase 5) ----
            //
            // **配線が欠けたときの壊れ方が 2 つある**ので両方数える:
            //   ① ねるタブが 4 行とも暗いまま (呼べない)
            //   ② おもちゃタブの `∧ !sleeping` が効かず、**寝ている犬に [呼ぶ] が出る**
            //      (押しても犬は来ない = Issue §6 危険 4 そのもの)
            var sleepGo = KD_SceneObjects.Find(KD_DogDefs.SleepBoardName, out int sleepCount);
            var wantBoard = (sleepCount == 1 && sleepGo != null)
                ? sleepGo.GetUdonSharpComponent<KD_SleepBoard>() : null;
            v.Check(pouch.sleepBoard != null && pouch.sleepBoard == wantBoard,
                PouchName + ".sleepBoard == " + KD_DogDefs.SleepBoardName
                + " (**欠けると寝ている犬に [" + KD_TreatPouch.WordRecall + "] が出る**)");
            v.Check(pouch.sleepLabels != null && pouch.sleepLabels.Length == dogs,
                PouchName + ".sleepLabels が " + dogs + " 本 (犬 1 匹 = 1 行)");
            v.Check(
                pouch.sleepStatusLabels != null && pouch.sleepStatusLabels.Length == dogs,
                PouchName + ".sleepStatusLabels が " + dogs + " 本 "
                + "(**`〇〇さんと寝ている` の出し先。無いと占有が外から読めない**)");
            v.Check(pouch.sleepButtons != null && pouch.sleepButtons.Length == dogs,
                PouchName + ".sleepButtons が " + dogs + " 本");
            v.Check(
                pouch.sleepButtonComponents != null && pouch.sleepButtonComponents.Length == dogs,
                PouchName + ".sleepButtonComponents が " + dogs + " 本 "
                + "(押せないときに interactable を落とす先)");
            v.Check(
                pouch.sleepButtonImages != null && pouch.sleepButtonImages.Length == dogs,
                PouchName + ".sleepButtonImages が " + dogs + " 本 (消灯色を書く先)");
            v.Check(
                pouch.sleepButtonLabels != null && pouch.sleepButtonLabels.Length == dogs,
                PouchName + ".sleepButtonLabels が " + dogs + " 本 "
                + "(**[" + KD_TreatPouch.WordSleepCall + "] と ["
                + KD_TreatPouch.WordSleepWake + "] を切り替える先**)");
            // **長さだけでは通ってしまう** (Codex レビュー 2 回目 2026-09-04)。
            // 配列が 4 本あっても要素が null なら、その行は実行時に何も更新されず
            // **[一緒に寝る] の文言も消灯も出ないまま押せてしまう**。
            // 行 1 本ぶんの実物と、押し先の名前 (`_Sleep{i}`) まで犬ごとに数える
            var sleepUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            for (int i = 0; i < dogs; i++)
            {
                var lbl = ArrayAt(pouch.sleepLabels, i);
                var st = ArrayAt(pouch.sleepStatusLabels, i);
                var btnGo = ArrayAt(pouch.sleepButtons, i);
                var btnCo = ArrayAt(pouch.sleepButtonComponents, i);
                var btnImg = ArrayAt(pouch.sleepButtonImages, i);
                var btnLbl = ArrayAt(pouch.sleepButtonLabels, i);
                v.Check(
                    lbl != null && st != null && btnGo != null && btnCo != null
                    && btnImg != null && btnLbl != null,
                    "ねる行 " + i + " の 6 本が非 null (ラベル" + (lbl != null ? "○" : "×")
                    + " / 状態" + (st != null ? "○" : "×")
                    + " / 行" + (btnGo != null ? "○" : "×")
                    + " / Button" + (btnCo != null ? "○" : "×")
                    + " / Image" + (btnImg != null ? "○" : "×")
                    + " / ボタン字" + (btnLbl != null ? "○" : "×") + ")");

                // **押し先の名前まで見る。** 隣の行の `_Sleep{j}` を張ってしまうと
                // 「別の犬が寝る」になり、見た目には配線済みに見える
                string want = "_Sleep" + i.ToString();
                UnityEngine.Object tgt;
                string got = OnClickEventName(btnCo, out tgt);
                v.Check(
                    got == want && sleepUdon != null && tgt == (UnityEngine.Object)sleepUdon,
                    "ねる行 " + i + " の onClick が SendCustomEvent(\"" + want
                    + "\") で " + PouchName + " 宛 (実際 \"" + (got == null ? "無し" : got)
                    + "\" / " + (tgt == null ? "宛先無し" : tgt.name) + ")");
            }

            v.Check(pouch.openToAllButton != null,
                PouchName + ".openToAllButton 配線 ([" + KD_TreatPouch.WordSleepOpenToAll + "])");
            v.Check(pouch.openToAllImage != null,
                PouchName + ".openToAllImage 配線 (**点灯 = 開放中。無いと今の状態が読めない**)");
            v.Check(pouch.sleepNoteLabel != null,
                PouchName + ".sleepNoteLabel 配線 (**押せない理由。"
                + "4 行が暗いだけだと「壊れている」と読める**)");
            v.Check(pouch.openOnColor != pouch.openOffColor,
                PouchName + " の開放トグルの点灯色と消灯色が違う");
            // P5 (2026-09-16): 設定ホームの [呼ぶを全員に許可]。隠さず、権限が無い人には押せない (interactable)
            if (pouch.openToAllButton != null)
            {
                v.Check(pouch.settingsHome != null && pouch.openToAllButton.transform.parent == pouch.settingsHome.transform,
                    "呼ぶの開放スイッチが設定ホームに居る (呼ぶタブには無い。P5)");
                v.Check(pouch.openToAllButtonComponent != null && pouch.openToAllButtonComponent.gameObject == pouch.openToAllButton,
                    PouchName + ".openToAllButtonComponent 配線 (権限が無い人には押せない)");
                UnityEngine.Object callTarget;
                v.Check(OnClickEventName(pouch.openToAllButtonComponent, out callTarget) == "_OpenToAll" && callTarget == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch),
                    "[呼ぶを全員に許可] が _OpenToAll へ配線");
                v.Check(pouch.tabPages != null && pouch.tabPages.Length > KD_TreatPouch.TabSleep
                    && pouch.tabPages[KD_TreatPouch.TabSleep].transform.Find("OpenToAllButton") == null,
                    "呼ぶタブの旧 OpenToAllButton が無い");
            }

            // **タブバーが板の内側に収まっている。** 4 枚目を足した日に静かに破られる —
            // 溢れても `Overflow` なので黙って板の外へ描かれる (例外もログも出ない)
            v.Check(TabStep * tabs <= PanelWidth,
                "タブ " + tabs + " 枚が板の横幅に収まる (" + (TabStep * tabs) + "px <= "
                + PanelWidth + "px) — **枚数を足すなら幅ではなく間隔を詰める**");

            // ---- onClick の二重登録 (タブ / おもちゃ / なで / ねる) ----
            foreach (var b in panel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (b.name == "Card" && b.transform.parent != null && b.transform.parent.name == ConfirmPanelName) continue;   // 確認カードのクリック止め (onClick 0 個が正しい)
                int c = b.onClick.GetPersistentEventCount();
                if (c == 1) continue;
                v.Check(false,
                    b.name + " の onClick が " + c + " 個 (1 個であること — "
                    + "追記されると 1 回押して 2 回動く)");
            }
        }

        // 非アクティブで未 Awake の UGUI は isOrthographic=false のまま。
        // TMP 3.0.6 はこの場合、字幅を 0.1 倍で計算する。UI の単位へ戻す。
        // margin は倍率の外で加算される。シーンの有効状態や TMP の設定は変えない。
        static float PreferredUiWidth(TMPro.TMP_Text label, string text)
        {
            float width = label.GetPreferredValues(text).x;
            if (label is TMPro.TextMeshProUGUI && !label.isOrthographic)
            {
                float margin = Mathf.Max(0f, label.margin.x) + Mathf.Max(0f, label.margin.z);
                width = (width - margin) / 0.1f + margin;
            }
            return width;
        }

        // ================================================================ 裏山 (段階 0-b) の検査
        //
        // 計画書「Verify の追加項目」の 0-b 行を全部持つ。古民家の検査のうち、裏山の値へ書き換えたもの
        // (既定タブ = さんぽ / Page_Acc の Content = AccRow 16 + FxRow 11 / 行数) と、対象外として skip を
        // 明示するもの (ライブ行のリフレクション検査・棚・寝台・ミニチュア) を分ける。黙って落とさない。
        // 参照の null は段階表 (D-1) で「意図的な未導入 (PASS + 理由)」と「欠落 (FAIL)」に分ける。

        const string HillsideVerifyLog = KD_Hillside.LogDir + "verify-pouch.txt";

        [MenuItem("Tools/KominkaHillside/Menu/20 Verify Pouch")]
        public static void VerifyHillside()
        {
            var v = new KD_VerifyLog("[KD_TreatSetup] Verify (裏山 段階 " + KD_Hillside.Stage + ")");
            var log = v.Text;
            if (!v.Check(!EditorApplication.isPlaying && KD_Hillside.InScene(), KD_Hillside.ScenePath + " を Edit モードで開いている"))
            { v.Finish(HillsideVerifyLog); return; }

            // ---- Program Asset と同期 (全段階: 同期変数 0 本・SyncMode None) ----
            var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(PouchProgramPath);
            v.Check(pa != null && pa.sourceCsScript != null, "program asset ok: KD_TreatPouch");
            v.Check(pa != null && pa.behaviourSyncMode == BehaviourSyncMode.None, "KD_TreatPouch の behaviourSyncMode == None (画面は同期しない)");
            int synced = 0;
            foreach (var f in typeof(KD_TreatPouch).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                foreach (var a in f.GetCustomAttributes(true))
                    if (a.GetType().Name == "UdonSyncedAttribute") synced++;
            v.Check(synced == 0, "同期変数 0 本: KD_TreatPouch の [UdonSynced] (actual " + synced + ")");

            // ---- 旧実装の不在 (KH_HandMenu コンポーネント 0 個・旧 Panel なし)。型は退役で消えるので名前で数える ----
            {
                int legacy = 0;
                var scene = EditorSceneManager.GetActiveScene();
                foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                {
                    if (mb == null || mb.gameObject.scene != scene) continue;
                    if (mb.GetType().Name == KD_Hillside.LegacyHandMenuName) legacy++;
                    var udon = mb as VRC.Udon.UdonBehaviour;
                    if (udon != null && udon.programSource != null && udon.programSource.name == KD_Hillside.LegacyHandMenuName) legacy++;
                }
                v.Check(legacy == 0, "KH_HandMenu コンポーネント 0 個 (proxy + backing。actual " + legacy + ")");
                KD_SceneObjects.Find(KD_Hillside.LegacyHandMenuName, out int legacyObjects);
                v.Check(legacyObjects == 0, "旧 KH_Dogs/KH_HandMenu (Panel / Launcher / Markers) が無い (actual " + legacyObjects + ")");
            }

            var go = KD_SceneObjects.Find(PouchName, out int pouchCount);
            if (!v.Check(pouchCount == 1 && go != null, PouchName + " がシーン内に 1 個 (actual " + pouchCount + ")")) { v.Finish(HillsideVerifyLog); return; }
            var pouch = go.GetUdonSharpComponent<KD_TreatPouch>();
            if (!v.Check(pouch != null, PouchName + " に KD_TreatPouch が付いている")) { v.Finish(HillsideVerifyLog); return; }
            var pouchUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            v.Check(go.transform.parent != null && go.transform.parent.name == KD_Hillside.DogsRootName && go.transform.parent.parent == null,
                PouchName + " が " + KD_Hillside.DogsRootName + " 直下 (\"====KominkaDogRun====\" を作らない)");
            KD_SceneObjects.Find(KD_DogDefs.WorldRootName, out int kominkaRoots);
            v.Check(kominkaRoots == 0, "\"" + KD_DogDefs.WorldRootName + "\" がシーンに無い (actual " + kominkaRoots + ")");
            v.Check(pouchUdon != null && pouchUdon.SyncMethod == VRC.SDKBase.Networking.SyncType.None, PouchName + " の backing SyncMethod == None");
            v.Check(go.layer == 0, PouchName + " の layer が Default (actual " + go.layer + ")");
            v.Check(go.GetComponent<Collider>() == null && go.GetComponent<MeshRenderer>() == null && go.transform.Find(PouchBodyName) == null,
                PouchName + " に Collider / 見た目 / " + PouchBodyName + " が無い (体に追従する当たりはプレイヤーを押しのける)");
            v.Check(go.GetComponent<VRCObjectSync>() == null, PouchName + " に VRCObjectSync が無い");
            // 開閉は古民家値 (D-6。KH_HandMenu の 0.6 は持ち込まない)
            v.Check(pouch.openStickSign == -1 && Mathf.Approximately(pouch.openStickThreshold, 0.8f) && Mathf.Approximately(pouch.openHoldSeconds, 0.5f),
                "開閉 = 右スティック下 (sign -1)・しきい値 0.8・0.5 秒 (actual " + pouch.openStickSign + " / " + pouch.openStickThreshold.ToString("F2") + " / " + pouch.openHoldSeconds.ToString("F2") + ")");
            v.Check(Mathf.Approximately(pouch.panelDistance, 0.95f), PouchName + ".panelDistance == 0.95 (actual " + pouch.panelDistance.ToString("F2") + ")");
            v.Check(!pouch.debugForceTouch, PouchName + ".debugForceTouch が false");

            // ---- 段階表 (D-1)。段階 0 で null が正の参照は PASS + 理由、必須は FAIL ----
            v.Check(pouch.hillsideStage == KD_Hillside.Stage, PouchName + ".hillsideStage == " + KD_Hillside.Stage + " (0 = 段階 0-b / 1 = IMP-PRG-001 / 2 = IMP-DOG-014〜016。actual " + pouch.hillsideStage + ")");
            int stage = pouch.hillsideStage;
            void StageRef(bool present, string name, int requiredFrom, string when)
            {
                if (stage >= requiredFrom) v.Check(present, name + " 配線 (段階 " + requiredFrom + " から必須: " + when + ")");
                else v.Check(true, name + (present ? " 配線済み (先行)" : " は意図的な未導入 (段階 " + stage + " では null が正。" + when + " で必須)"));
            }
            StageRef(pouch.manager != null, PouchName + ".manager (KH_LeashManager)", 0, "段階 0-b");
            StageRef(pouch.recall != null, PouchName + ".recall (KH_ToyRecall)", 0, "段階 0-b");
            StageRef(pouch.activitiesMenu != null, PouchName + ".activitiesMenu (KD_ActivitiesMenu)", 0, "段階 0-b。Menu/9 → Menu/7");
            StageRef(pouch.ledger != null, PouchName + ".ledger (KD_ProgressLedger)", 1, "IMP-PRG-001");
            StageRef(pouch.catalog != null, PouchName + ".catalog (KD_ShopCatalog)", 1, "IMP-PRG-001");
            StageRef(pouch.box != null, PouchName + ".box (KD_TreatBox)", 2, "IMP-DOG-014");
            // 裏山改変 (段階 1): おやつの商品 4 行は台帳と一緒に配線する (出す口の box は段階 2)。撫でFX の商品は無い分 null 可 (WARN)
            StageRef(pouch.items != null && pouch.items.Length == KD_ShopSetupEditor.TreatProductCount && System.Array.TrueForAll(pouch.items, x => x != null),
                PouchName + ".items (おやつの商品 " + KD_ShopSetupEditor.TreatProductCount + " 行)", 1, "IMP-PRG-001");
            if (stage >= 1)
            {
                int fxSet = pouch.fxItems == null ? 0 : System.Array.FindAll(pouch.fxItems, x => x != null).Length;
                v.Check(pouch.fxItems != null && pouch.fxItems.Length == KD_ShopSetupEditor.PetFxProductCount,
                    PouchName + ".fxItems が " + KD_ShopSetupEditor.PetFxProductCount + " 枠 (配線 " + fxSet + "。null の枠は WARN 扱い: 描画・購入は段階 2 IMP-DOG-015)");
                if (fxSet < KD_ShopSetupEditor.PetFxProductCount) log.AppendLine("  WARN fxItems の null " + (KD_ShopSetupEditor.PetFxProductCount - fxSet) + " 枠 (Menu/4 Setup Shop の撫でFX 行)");
            }
            StageRef(pouch.accessories != null, PouchName + ".accessories (KD_AccessoryBox)", 2, "IMP-DOG-015");
            StageRef(pouch.petFxBox != null, PouchName + ".petFxBox (KD_PetFxBox)", 2, "IMP-DOG-015");
            StageRef(pouch.sleepBoard != null, PouchName + ".sleepBoard (KD_SleepBoard)", 2, "IMP-DOG-016");
            StageRef(pouch.sleepPlacement != null, PouchName + ".sleepPlacement (KD_SleepPlacement)", 2, "IMP-DOG-016");
            StageRef(pouch.callPose != null, PouchName + ".callPose (KD_CallPose)", 2, "IMP-DOG-016");
            // ライブは段階の番号を持たない (DES-LIV-001: 「あれば検査」)。シーンに KD_LiveSystem が無ければ null、あればそれを指す
            var sceneLive = HillsideSceneLive(out int sceneLiveCount);
            if (sceneLiveCount == 0)
                v.Check(pouch.live == null, PouchName + ".live は null (シーンに " + KD_DogDefs.LiveSystemName + " が無い = ライブは準備中)");
            else
                v.Check(sceneLiveCount == 1 && sceneLive != null && pouch.live == sceneLive,
                    PouchName + ".live == シーンの " + KD_DogDefs.LiveSystemName + " (actual " + sceneLiveCount + " 個 / 配線 " + (pouch.live != null) + "。Menu/26a → Menu/7)");
            {
                // U# 参照は backing に相手の backing UdonBehaviour として入る (vrc-udonsharp §4)
                object wired;
                v.Check(pouch.manager != null && pouchUdon.publicVariables.TryGetVariableValue("manager", out wired)
                    && wired as Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch.manager), "backing Udon が manager を持つ");
                v.Check(pouch.recall != null && pouchUdon.publicVariables.TryGetVariableValue("recall", out wired)
                    && wired as Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch.recall), "backing Udon が recall を持つ");
                var dogs = go.transform.parent;
                v.Check(pouch.manager != null && pouch.manager.transform.parent == dogs && pouch.manager.name == KD_Hillside.LeashesName, "manager は " + KD_Hillside.DogsRootName + "/" + KD_Hillside.LeashesName);
                v.Check(pouch.recall != null && pouch.recall.transform.parent == dogs && pouch.recall.name == KD_Hillside.ToyRecallName, "recall は " + KD_Hillside.DogsRootName + "/" + KD_Hillside.ToyRecallName);
                if (stage >= 1)
                {
                    v.Check(pouch.ledger != null && pouch.ledger.transform.parent == dogs && pouch.ledger.name == KD_Hillside.ProgressSystemName,
                        "ledger は " + KD_Hillside.DogsRootName + "/" + KD_Hillside.ProgressSystemName);
                    v.Check(pouch.ledger != null && pouch.ledger.keeperPoints == KD_Hillside.KeeperPoints,
                        "おじいさんの支度ポイント: ledger.keeperPoints == " + KD_Hillside.KeeperPoints + " (actual " + (pouch.ledger == null ? -1 : pouch.ledger.keeperPoints) + ")");
                    v.Check(pouch.catalog != null && pouch.catalog.transform.parent == dogs && pouch.catalog.name == KD_Hillside.ShopCatalogName,
                        "catalog は " + KD_Hillside.DogsRootName + "/" + KD_Hillside.ShopCatalogName);
                    v.Check(pouch.ledger != null && pouchUdon.publicVariables.TryGetVariableValue("ledger", out wired)
                        && wired as Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch.ledger), "backing Udon が ledger を持つ");
                    v.Check(pouch.catalog != null && pouchUdon.publicVariables.TryGetVariableValue("catalog", out wired)
                        && wired as Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch.catalog), "backing Udon が catalog を持つ");
                    // おじいさんは pouch から参照しない (KD_ShopKeeper 側が ledger を持つ)。名前で 1 個だけ数える
                    var keeperT = dogs.Find("KD_ShopKeeper");
                    int keepers = 0;
                    foreach (var mb in Resources.FindObjectsOfTypeAll<KD_ShopKeeper>()) if (mb != null && mb.gameObject.scene == go.scene) keepers++;
                    v.Check(keeperT != null && keeperT.GetComponent<KD_ShopKeeper>() != null && keepers == 1,
                        "おじいさん " + KD_Hillside.DogsRootName + "/KD_ShopKeeper が 1 個 (シーンの KD_ShopKeeper " + keepers + " 個。Menu/4d Setup Shop Keeper)");
                    v.Check(pouch.uiSuccessClip != null && pouch.uiRejectClip != null && pouch.uiAudio != null,
                        "購入の効果音 (uiSuccessClip / uiRejectClip / uiAudio) 配線 (裏山は棚ではなく KD_ShopSetupEditor.HillsideBuyClip / HillsideRejectClip)");
                }
                if (stage >= 2)
                {
                    // 段階 2 (IMP-DOG-014): おやつの箱。枠・実体・自己テストの詳細は Menu/6d Verify Treats
                    v.Check(pouch.box != null && pouch.box.transform.parent == dogs && pouch.box.name == BoxName,
                        "box は " + KD_Hillside.DogsRootName + "/" + BoxName);
                    v.Check(pouch.box != null && pouchUdon.publicVariables.TryGetVariableValue("box", out wired)
                        && wired as Object == UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch.box), "backing Udon が box を持つ");
                    v.Check(pouch.box != null && pouch.box.slots != null && pouch.box.slots.Length == KD_DogDefs.TreatSlotCount
                        && System.Array.TrueForAll(pouch.box.slots, s => s != null && s.pouch == pouch),
                        "box.slots が " + KD_DogDefs.TreatSlotCount + " 本・各枠の pouch == " + PouchName + " (移譲の知らせ先)");
                    // 段階 2 (IMP-DOG-015): かざりの台帳と撫でFX の置き場 (台帳・一覧の中身は Menu/6f、撫でFX は Menu/6h)
                    v.Check(pouch.accessories != null && pouch.accessories.transform.parent == dogs && pouch.accessories.name == KD_DogDefs.AccessoryBoxName,
                        "accessories は " + KD_Hillside.DogsRootName + "/" + KD_DogDefs.AccessoryBoxName);
                    v.Check(pouch.petFxBox != null && pouch.petFxBox.transform.parent == dogs && pouch.petFxBox.name == KD_DogDefs.PetFxBoxName,
                        "petFxBox は " + KD_Hillside.DogsRootName + "/" + KD_DogDefs.PetFxBoxName);
                    // 段階 2 (IMP-DOG-016): 呼ぶの参照がシーンの実物を指す (中身の検査は Menu/6b Verify Sleep System が持つ。正本を 2 つにしない)
                    var wantSleepBoard = dogs.Find(KD_DogDefs.SleepBoardName)?.GetComponent<KD_SleepBoard>();
                    var wantPlacement = dogs.Find(KD_SleepPlacementEditor.SystemName)?.GetComponent<KD_SleepPlacement>();
                    var wantCallPose = dogs.Find("KD_CallPose")?.GetComponent<KD_CallPose>();
                    v.Check(wantSleepBoard != null && pouch.sleepBoard == wantSleepBoard, PouchName + ".sleepBoard == " + KD_Hillside.DogsRootName + "/" + KD_DogDefs.SleepBoardName);
                    v.Check(wantPlacement != null && pouch.sleepPlacement == wantPlacement && wantPlacement.board == wantSleepBoard,
                        PouchName + ".sleepPlacement == " + KD_Hillside.DogsRootName + "/" + KD_SleepPlacementEditor.SystemName + " (board も同じ台帳)");
                    v.Check(wantCallPose != null && pouch.callPose == wantCallPose && wantCallPose.board == wantSleepBoard && wantCallPose.placement == wantPlacement,
                        PouchName + ".callPose == " + KD_Hillside.DogsRootName + "/KD_CallPose (board / placement も同じ)");
                    v.Check(wantSleepBoard != null && wantSleepBoard.hillsideLeashes == pouch.manager, "KD_SleepBoard.hillsideLeashes == pouch.manager (リード接続中・集合中は呼べない)");
                    v.Check(pouch.manager != null && pouch.manager.sleepBoard == wantSleepBoard, "KH_LeashManager.sleepBoard == KD_SleepBoard (呼ばれて待機中の犬はリードをつなげない。Menu/6 / Walking/3)");
                }
            }

            // ---- 板 ----
            var panel = pouch.panelRoot;
            if (!v.Check(panel != null, PouchName + ".panelRoot 配線")) { v.Finish(HillsideVerifyLog); return; }
            v.Check(!panel.activeSelf, PouchPanelName + " が既定で非アクティブ");
            var prect = (RectTransform)panel.transform;
            v.Check(prect.sizeDelta == new Vector2(PanelWidth, PanelHeight), PouchPanelName + " 1140x700");
            var canvas = panel.GetComponent<Canvas>();
            v.Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace && panel.GetComponent<VRCUiShape>() != null
                && panel.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null, PouchPanelName + " が WorldSpace Canvas + VRCUiShape + GraphicRaycaster");
            v.Check(KD_HandMenuCollisionEditor.Validate(panel, log), PouchPanelName + " の当たりがプレイヤーを塞がない (layer 17)");
            var launcher = pouch.touchLauncher;
            v.Check(launcher != null && !launcher.activeSelf && launcher.transform.parent == go.transform, "Touch ランチャーが pouch の子で既定非アクティブ");
            if (launcher != null)
            {
                var lb = launcher.transform.Find(LauncherButtonName)?.GetComponent<UnityEngine.UI.Button>();
                Object lt; v.Check(OnClickEventName(lb, out lt) == "_Toggle" && lt == pouchUdon, LauncherButtonName + " -> _Toggle");
                var lc = launcher.GetComponent<Canvas>();
                v.Check(lc != null && lc.renderMode == RenderMode.ScreenSpaceOverlay && lc.sortingOrder == 100, "Touch ランチャーは Overlay / sortingOrder 100");
            }
            v.Check(pouch.statusLabel != null && pouch.closeButton != null && pouch.sectionTitleLabel != null, "statusLabel / closeButton / sectionTitleLabel 配線");

            // ---- 段階 1: ヘッダの残高・ポイントの面・ギフトコードの器が無い・ようこその面 ----
            if (stage >= 1)
            {
                var balance = pouch.balanceLabel;
                v.Check(balance != null && balance.gameObject.activeSelf && balance.transform.parent == panel.transform && !balance.raycastTarget,
                    "ヘッダの残高 (Balance) が表示・板直下・押下を塞がない (0-b の非表示を解除)");
                var entry = panel.transform.Find(KD_GiftCodeSetupEditor.PointsButtonName);
                Object et = null;
                v.Check(entry != null && pouch.hillsidePointsButton == entry.gameObject && entry.gameObject.activeSelf
                    && OnClickEventName(entry.GetComponent<UnityEngine.UI.Button>(), out et) == "_HillsidePoints" && et == pouchUdon
                    && typeof(KD_TreatPouch).GetMethod("_HillsidePoints") != null, "残高の入口 " + KD_GiftCodeSetupEditor.PointsButtonName + " -> _HillsidePoints (1 本・pouch 宛・口が実在)");
                if (entry != null && balance != null)
                {
                    var er = (RectTransform)entry; var br = balance.rectTransform;
                    v.Check((er.anchoredPosition - br.anchoredPosition).magnitude < .5f && er.sizeDelta.x >= br.sizeDelta.x && er.sizeDelta.y >= br.sizeDelta.y
                        && entry.GetSiblingIndex() < balance.transform.GetSiblingIndex(), "入口は残高ラベルを覆い、ラベルが上に描かれる");
                }
                var page = pouch.hillsidePointsPage;
                v.Check(page != null && page.name == KD_Hillside.PointsPageName && pouch.settingsPage != null && page.transform.parent == pouch.settingsPage.transform
                    && !page.activeSelf && pouch.extraSettingsPages != null && System.Array.IndexOf(pouch.extraSettingsPages, page) >= 0,
                    "ポイントの面 Page_Settings/" + KD_Hillside.PointsPageName + " (既定非表示・extraSettingsPages に登録)");
                v.Check(pouch.hillsidePointsHeading != null && AllSet(pouch.hillsidePointsRows, 2) && AllSet(pouch.hillsidePointsSubs, 2) && pouch.hillsidePointsBackLabel != null
                    && page != null && System.Array.TrueForAll(pouch.hillsidePointsRows, r => r.transform.parent == page.transform),
                    "ポイントの面: 見出し + 入手経路 2 行 (おじいさんの支度ポイント / 滞在) + 補足 2 行");
                var back = page == null ? null : page.transform.Find("Back");
                Object bt = null;
                v.Check(back != null && OnClickEventName(back.GetComponent<UnityEngine.UI.Button>(), out bt) == "_HillsidePointsBack" && bt == pouchUdon
                    && typeof(KD_TreatPouch).GetMethod("_HillsidePointsBack") != null, "ポイントの面 [もどる] -> _HillsidePointsBack");
                if (page != null)
                {
                    int extra = 0;
                    foreach (Transform c in page.transform) if (c.name != "Heading" && c.name != "Row0" && c.name != "Row1" && c.name != "Sub0" && c.name != "Sub1" && c.name != "Back") { extra++; log.AppendLine("  FAIL ポイントの面に想定外の子: " + c.name); }
                    v.Check(extra == 0, "ポイントの面の子は 見出し / 2 行 / 補足 2 / [もどる] だけ (想定外 " + extra + ")");
                }
                // ギフトコードの器が無い (計画書 D-5: 裏山では未決定なので作らない)
                int giftPanels = 0;
                foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                    if (mb != null && mb.gameObject.scene == go.scene && mb.GetType().Name == "KD_GiftCodePanel") giftPanels++;
                KD_SceneObjects.Find(KD_GiftCodeSetupEditor.SystemName, out int giftObjects);
                v.Check(giftPanels == 0 && giftObjects == 0, "ギフトコードの器が無い: KD_GiftCodePanel コンポーネント " + giftPanels + " 個 / オブジェクト " + giftObjects + " 個");
                v.Check(pouch.settingsPage == null || pouch.settingsPage.transform.Find(KD_GiftCodeSetupEditor.PageName) == null, "ギフトコードの面 (" + KD_GiftCodeSetupEditor.PageName + ") が無い");
                // 裏山改変 (司令塔 2026-09-24、段階 3): 距離フェードの入力欄とクレジットの URL 欄 (Page_Settings 配下) は正規の入力欄。ギフトコード用の入力欄が Page_Settings の外に無いことだけを見る
                int strayInputs = 0;
                foreach (var f in panel.GetComponentsInChildren<TMPro.TMP_InputField>(true)) { bool inSettings = false; for (var t = f.transform; t != null && t != panel.transform; t = t.parent) if (t.name == "Page_Settings") { inSettings = true; break; } if (!inSettings) strayInputs++; }
                v.Check(strayInputs == 0, "Page_Settings の外に入力欄 (TMP_InputField) が無い (コード入力の器を置かない。actual " + strayInputs + ")");
                // ようこその面 (最小版)。細部は Menu/11c Verify Welcome
                v.Check(pouch.welcomePage != null && pouch.welcomePage.name == KD_WelcomeGuideEditor.PageName && pouch.welcomePage.transform.parent == panel.transform && !pouch.welcomePage.activeSelf,
                    "ようこその面 " + KD_WelcomeGuideEditor.PageName + " が板直下・既定非表示 (詳細は Menu/11c Verify Welcome)");
                v.Check(pouch.hints != null && pouch.welcomeSpot != null, "ようこその配線 (hints / welcomeSpot。Menu/11b Build Welcome)");
                // 未受取の案内の口が実在 (段階 1 は準備中の行のサブ文、段階 2 は行 0 の上の 1 行)
                v.Check(typeof(KD_TreatPouch).GetMethod("HillsideGrantLineVisible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null,
                    "未受取の案内: KD_TreatPouch.HillsideGrantLineVisible() がある (ledger 復元後 && !BonusReceived(keeper))");
            }
            else log.AppendLine("  SKIP 残高・ポイントの面・ようこそ (段階 0: 段階 1 IMP-PRG-001 で必須)");

            // ---- タブ (幅の和・並び・既定タブ) ----
            int tabs = KD_TreatPouch.TabCount;
            v.Check(pouch.tabPages != null && pouch.tabPages.Length == tabs && pouch.tabImages != null && pouch.tabImages.Length == tabs
                && pouch.tabUnderlines != null && pouch.tabUnderlines.Length == tabs, "タブ " + tabs + " 枚 (pages / images / underlines)");
            v.Check(KD_TreatPouch.TabWalk == 2, "TabWalk == 2 (古民家の PetFx の枠をさんぽへ。D-2)");
            float sum = KD_HandMenuScrollEditor.WidthSum();
            v.Check(Mathf.Approximately(sum, 1064f) && Mathf.Approximately(sum + KD_HandMenuScrollEditor.TabGap * (tabs - 1), KD_HandMenuScrollEditor.TabRowWidth),
                "タブ幅の和 1064、間隔 6×6 を加えて 1100 (actual " + sum + ")");
            v.Check(string.Join(",", KD_HandMenuScrollEditor.Order) == "2,0,5,3,1,6,4", "表示順 {2,0,5,3,1,6,4} = さんぽ / おやつ / かざり / 呼ぶ / おもちゃ / あそぶ / ライブ");
            if (pouch.tabPages != null && pouch.tabPages.Length == tabs)
            {
                v.Check(pouch.tabPages[KD_TreatPouch.TabWalk] != null && pouch.tabPages[KD_TreatPouch.TabWalk].name == "Page_Walk", "tabPages[2] == Page_Walk");
                for (int i = 0; i < tabs; i++)
                {
                    bool want = i == KD_TreatPouch.TabWalk;
                    v.Check(pouch.tabPages[i] != null && pouch.tabPages[i].activeSelf == want
                        && pouch.tabUnderlines[i] != null && pouch.tabUnderlines[i].activeSelf == want,
                        "既定タブ = さんぽ: " + (pouch.tabPages[i] == null ? "tab" + i : pouch.tabPages[i].name) + (want ? " だけアクティブ" : " は非アクティブ"));
                }
            }
            // 実行時の既定タブ (_tab) は private。ソースのフィールド初期値と Start を読む (契約: _tab = TabWalk)
            {
                string src = File.Exists(PouchScriptPath) ? File.ReadAllText(PouchScriptPath) : "";
                v.Check(src.Contains("_tab = TabWalk"), "既定タブ: KD_TreatPouch.cs が _tab = TabWalk を持つ (フィールド初期値 / Start)");
            }

            // ---- さんぽの行 / 行き先 ----
            var walkScroll = pouch.tabScrolls != null && pouch.tabScrolls.Length > KD_TreatPouch.TabWalk ? pouch.tabScrolls[KD_TreatPouch.TabWalk] : null;
            var walkContent = walkScroll == null ? null : walkScroll.content;
            // 裏山改変 (Course/25・DES-UI-006): コースの本番シーンは行き先 6 つ (行 12)。行き先 3 つ目以降の押し先と 2 列の配置は Course/26・121 が見る
            int walkRows = pouch.walkDestinationIds != null && pouch.walkDestinationIds.Length > 0 ? WalkDogRows + pouch.walkDestinationIds.Length : WalkRowCount;
            v.Check(walkContent != null && walkContent.childCount == walkRows, "さんぽの行が " + walkRows + " 行 (犬 6 + 行き先 " + (walkRows - WalkDogRows) + ") で Scroll の Content に居る");
            for (int i = 0; i < walkRows && walkContent != null; i++)
            {
                var row = walkContent.Find(WalkRowPrefix + i);
                if (!v.Check(row != null, WalkRowPrefix + i + " がある")) continue;
                if (i < WalkDogRows)
                {
                    foreach (string b in new[] { "Left", "Right", "Off" })
                    {
                        var btn = row.Find(b)?.GetComponent<UnityEngine.UI.Button>();
                        string want = "_Walk" + b + i; Object t;
                        v.Check(btn != null && btn.onClick.GetPersistentEventCount() == 1 && OnClickEventName(btn, out t) == want && t == pouchUdon
                            && typeof(KD_TreatPouch).GetMethod(want) != null, "さんぽ " + KD_DogDefs.All[i].displayName + " [" + b + "] -> " + want + " (1 本・pouch 宛・口が実在)");
                    }
                }
                else if (i < WalkDogRows + 2)
                {
                    string want = i == WalkDogRows ? "_WalkPlaza" : "_WalkGate";
                    var btn = row.Find("Here")?.GetComponent<UnityEngine.UI.Button>(); Object t;
                    v.Check(btn != null && btn.onClick.GetPersistentEventCount() == 1 && OnClickEventName(btn, out t) == want && t == pouchUdon
                        && typeof(KD_TreatPouch).GetMethod(want) != null, "さんぽの行き先 " + WalkRowPrefix + i + " [ここへ] -> " + want);
                }
            }
            v.Check(pouch.walkLabels != null && pouch.walkLabels.Length == walkRows && pouch.walkStateLabels != null && pouch.walkStateLabels.Length == walkRows
                && System.Array.TrueForAll(pouch.walkLabels, x => x != null) && System.Array.TrueForAll(pouch.walkStateLabels, x => x != null), "walkLabels / walkStateLabels が " + walkRows + " 本とも配線");
            v.Check(AllSet(pouch.walkLeftButtons, WalkDogRows) && AllSet(pouch.walkRightButtons, WalkDogRows) && AllSet(pouch.walkOffButtons, WalkDogRows)
                && AllSet(pouch.walkLeftImages, WalkDogRows) && AllSet(pouch.walkRightImages, WalkDogRows) && AllSet(pouch.walkOffImages, WalkDogRows),
                "walkLeft/Right/Off の Button と Image が 6 本ずつ配線");
            v.Check(AllSet(pouch.walkHereButtons, walkRows - WalkDogRows) && AllSet(pouch.walkHereImages, walkRows - WalkDogRows), "walkHereButtons / walkHereImages が " + (walkRows - WalkDogRows) + " 本ずつ配線");

            // ---- おもちゃの行 ([ボール] [ディスク] → KH_ToyRecall) ----
            int dogsN = KD_DogDefs.All.Length;
            var toyScroll = pouch.tabScrolls != null && pouch.tabScrolls.Length > KD_TreatPouch.TabToy ? pouch.tabScrolls[KD_TreatPouch.TabToy] : null;
            for (int i = 0; i < dogsN && toyScroll != null; i++)
            {
                var row = toyScroll.content.Find("ToyRow" + i);
                if (!v.Check(row != null, "ToyRow" + i + " が Scroll の Content に居る")) continue;
                foreach (var (b, want) in new[] { ("Button", "_Toy" + i), (DiscButtonName, "_Disc" + i) })
                {
                    var btn = row.Find(b)?.GetComponent<UnityEngine.UI.Button>(); Object t;
                    v.Check(btn != null && btn.onClick.GetPersistentEventCount() == 1 && OnClickEventName(btn, out t) == want && t == pouchUdon
                        && typeof(KD_TreatPouch).GetMethod(want) != null, "おもちゃ " + KD_DogDefs.All[i].displayName + " [" + b + "] -> " + want);
                }
            }
            v.Check(AllSet(pouch.toyButtons, dogsN) && AllSet(pouch.toyButtonComponents, dogsN) && AllSet(pouch.toyButtonImages, dogsN)
                && AllSet(pouch.toyLabels, dogsN) && AllSet(pouch.toyReasonLabels, dogsN), "toyButtons / Components / Images / Labels / ReasonLabels が 6 本ずつ ([ボール])");
            v.Check(AllSet(pouch.discButtons, dogsN) && AllSet(pouch.discButtonComponents, dogsN) && AllSet(pouch.discButtonImages, dogsN) && AllSet(pouch.discButtonLabels, dogsN),
                "discButtons / Components / Images / Labels が 6 本ずつ ([ディスク])");
            v.Check(pouch.recall != null, "おもちゃの叩く先 recall (KH_ToyRecall) が非 null");
            log.AppendLine("  NOTE KD_BallController / KD_DogFetchController (balls / toyFetchers) は裏山では使わない (KH_ToyRecall が正本。検査しない)");

            // ---- かざりの混在行 (AccRow0..15, FxHead0..1, FxRow0..10 の順) ----
            var accScroll = pouch.tabScrolls != null && pouch.tabScrolls.Length > KD_TreatPouch.TabAcc ? pouch.tabScrolls[KD_TreatPouch.TabAcc] : null;
            if (accScroll != null)
            {
                var want = new System.Collections.Generic.List<string>();
                // 裏山改変 (IMP-DOG-015): 期待値は Scroll の行の表 (AccRow16 → FxHead2 → FxRow11) から作る
                foreach (var (prefix, rowCount) in KD_HandMenuScrollEditor.RowsFor(KD_TreatPouch.TabAcc)) for (int i = 0; i < rowCount; i++) want.Add(prefix + i);
                var got = new System.Collections.Generic.List<string>();
                foreach (Transform row in accScroll.content) got.Add(row.name);
                v.Check(System.Linq.Enumerable.SequenceEqual(want, got), "かざりの Content の子順が AccRow0..15, FxHead0..1, FxRow0..10 (actual " + got.Count + " 行)");
            }
            else v.Check(false, "かざりの Scroll がある");
            v.Check(AllSet(pouch.fxButtons, HillsideFxRowCount) && AllSet(pouch.fxLabels, HillsideFxRowCount), "fxButtons / fxLabels が 11 本 (かざりの Content の FxRow)");
            if (stage >= 2)
            {
                // 裏山改変 (IMP-DOG-015 / D-2 (e)): 撫でFX の行の [つける/買う] は pouch の _Fx{i} を 1 本だけ呼ぶ
                for (int i = 0; i < HillsideFxRowCount; i++)
                {
                    var btn = pouch.fxButtons != null && i < pouch.fxButtons.Length && pouch.fxButtons[i] != null ? pouch.fxButtons[i].GetComponent<UnityEngine.UI.Button>() : null;
                    Object t = null;
                    v.Check(btn != null && btn.onClick.GetPersistentEventCount() == 1 && OnClickEventName(btn, out t) == "_Fx" + i && t == pouchUdon
                        && typeof(KD_TreatPouch).GetMethod("_Fx" + i) != null, "撫でFX FxRow" + i + " [つける/買う] -> _Fx" + i);
                }
                v.Check(pouch.hillsideFxHeadRows != null && pouch.hillsideFxHeadRows.Length == 2 && pouch.hillsideFxHeadRows[0] != null && pouch.hillsideFxHeadRows[1] != null
                    && pouch.hillsideFxHeadLabel != null && pouch.hillsideFxHeadLabel.transform.parent == pouch.hillsideFxHeadRows[0].transform,
                    "撫でFX の群見出し (FxHead0 見出し / FxHead1 空き) が配線済み");
            }
            else log.AppendLine("  NOTE FxRow の onClick (_Fx{i}) の検査は段階 2 (IMP-DOG-015) から (段階 " + stage + ")");

            // ---- 準備中の器 (おやつ 4 / かざり 16 / 呼ぶ 6 / ライブ 9。行 0 に準備中を描く先がある) ----
            v.Check(AllSet(pouch.rowLabels, KD_ShopSetupEditor.TreatProductCount) && AllSet(pouch.rowButtons, KD_ShopSetupEditor.TreatProductCount), "おやつの器 Row0..3 (行 0 = 準備中の出し先)");
            if (stage >= 2)
            {
                // 段階 2 (IMP-DOG-014): 行の [買う] (Row{i}/Buy -> _TreatBuy{i}) と確認カード kind Treat
                int tn = KD_ShopSetupEditor.TreatProductCount;
                v.Check(AllSet(pouch.treatBuyButtons, tn) && AllSet(pouch.treatBuyImages, tn) && AllSet(pouch.treatBuyLabels, tn),
                    "[買う] の配線 treatBuyButtons / treatBuyImages / treatBuyLabels が " + tn + " 本");
                for (int i = 0; i < tn && pouch.treatBuyButtons != null && i < pouch.treatBuyButtons.Length; i++)
                {
                    var buyGo = pouch.treatBuyButtons[i];
                    var btn = buyGo == null ? null : buyGo.GetComponent<UnityEngine.UI.Button>();
                    string want = TreatBuyEventOf(i);
                    Object t = null;
                    v.Check(btn != null && buyGo.name == TreatBuyButtonName && pouch.rowButtons != null && i < pouch.rowButtons.Length && pouch.rowButtons[i] != null
                        && buyGo.transform.parent == pouch.rowButtons[i].transform.parent
                        && btn.onClick.GetPersistentEventCount() == 1 && OnClickEventName(btn, out t) == want && t == pouchUdon
                        && typeof(KD_TreatPouch).GetMethod(want) != null,
                        "おやつ Row" + i + " [買う] -> " + want + " (出すと同じ行・1 本・pouch 宛・口が実在)");
                }
                string confirmSrc = File.ReadAllText("Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_TreatPouch.Confirm.cs");
                v.Check(typeof(KD_TreatPouch).GetMethod("_TreatConfirmBuy") != null && typeof(KD_TreatPouch).GetMethod("_TreatConfirmNo") != null
                    && confirmSrc.Contains("kind == ConfirmTreat) { _TreatConfirmBuy();") && confirmSrc.Contains("kind == ConfirmTreat) { _TreatConfirmNo();"),
                    "確認カード kind Treat: _ConfirmYes / _ConfirmNo が _TreatConfirmBuy / _TreatConfirmNo へ振り分ける");
            }
            v.Check(AllSet(pouch.accRows, KD_AccessorySetsEditor.MenuCount) && AllSet(pouch.accRowLabels, KD_AccessorySetsEditor.MenuCount), "かざりの器 AccRow0..15");
            v.Check(AllSet(pouch.sleepLabels, dogsN) && AllSet(pouch.sleepButtons, dogsN), "呼ぶの器 SleepRow0..5");
            v.Check(pouch.liveLabels != null && pouch.liveLabels.Length == KD_DogDefs.LiveShows.Length && pouch.liveLabels[0] != null
                && pouch.liveSubLabels != null && pouch.liveSubLabels.Length > 0 && pouch.liveSubLabels[0] != null,
                "ライブの器 LiveRow0.." + (KD_DogDefs.LiveShows.Length - 1) + " (KD_DogDefs.LiveShows の本数。live が無い間は行 0 = 準備中 1 行の出し先)");
            // 裏山改変 (DES-LIV-001): KD_LiveSystem があればライブ行の中身まで検査する (無ければ準備中 1 行だけなので SKIP)
            if (pouch.live != null) HillsideVerifyLive(v, pouch, log);
            else log.AppendLine("  SKIP ライブ行の onClick リフレクション検査 (_Live{i} / _LiveGift{i})・liveItems / liveProducts / 詳細の面 (live が null = 準備中 1 行だけ。Menu/26a Setup Live → Menu/7 で入る)");
            HillsideCheckNoExtraLiveRows(v, pouch);
            log.AppendLine("  SKIP 棚 (KD_ShopShelf) の配線・棚の効果音・棚の商品の Collider (裏山では対象外: 棚は置かない。商品は menuOnly で KH_Dogs/KD_ShopCatalog の子。購入はメニュー)");
            if (stage >= 2)
            {
                // 段階 2 (IMP-DOG-016): 参照は上の段階表の検査。IMP-DOG-032 で場所決めの斜面判定は外した (古民家どおり)
                log.AppendLine("  NOTE 場所決め: 古民家どおり手 / 頭の前にそのまま置く (斜面判定・接地なし、IMP-DOG-032)。"
                    + "詳細 (赤い印の残り・押し出しの除外・ポーズ面・追加動作) は Menu/6b Verify Sleep System");
                // 段階 2 (IMP-DOG-015 / D-7): 試着のミニチュア (Menu/18 Build Dog Previews)。詳しい検査は Menu/25 Verify Dog Previews
                int prodN = pouch.accessories != null && pouch.accessories.productIds != null ? pouch.accessories.productIds.Length : 0;
                v.Check(AllSet(pouch.accPreviewDogs, dogsN) && pouch.accPreviewItems != null && pouch.accPreviewItems.Length == prodN * dogsN && prodN > 0
                    && System.Array.TrueForAll(pouch.accPreviewItems, x => x != null),
                    "試着ミニチュア accPreviewDogs " + dogsN + " / accPreviewItems " + (prodN * dogsN) + " (Menu/18 Build Dog Previews。詳細は Menu/25)");
                // 裏山改変 (IMP-DOG-015 案 C、2026-09-24 ユーザー決定): 首輪と物理的に重なる組の除外が台帳に配線され、手元メニューに犬の行を暗くする条件がある
                {
                    var exLog = new StringBuilder();
                    bool exOk = KD_AccessorySetupEditor.HillsideExcludedArrays(out int[] exDogs, out int[] exProducts, exLog);
                    var accBox = pouch.accessories;
                    v.Check(exOk && accBox != null && accBox.blockedDogs != null && accBox.blockedProducts != null
                        && System.Linq.Enumerable.SequenceEqual(accBox.blockedDogs, exDogs) && System.Linq.Enumerable.SequenceEqual(accBox.blockedProducts, exProducts),
                        "かざりの除外 " + exDogs.Length + " 組 (首輪と重なる 犬 × 品。KD_AccessorySetupEditor.HillsideCollarExcluded) が台帳 blockedDogs / blockedProducts に配線 (Menu/6e)");
                    v.Check(typeof(KD_TreatPouch).GetMethod("AccDogBlocked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null
                        && typeof(KD_AccessoryBox).GetMethod("Blocked") != null,
                        "除外の組は犬選択の行が暗くなり理由を出す (KD_TreatPouch.AccDogBlocked)・台帳の Owner が拒否する (KD_AccessoryBox.Blocked)");
                }
                log.AppendLine("  NOTE おやつの枠 (KD_TreatSlot / KD_TreatController / 実体 / 自己テスト / 食後エフェクト) は Menu/6d Verify Treats が数える");
            }
            else
            {
                log.AppendLine("  SKIP 寝台・ポーズ面 (KD_SleepBoard / KD_SleepPlacement / KD_CallPose) (裏山では対象外: 段階 2 IMP-DOG-016 まで)");
                log.AppendLine("  SKIP ミニチュア (DogPreview / accPreviewDogs) (段階 2 IMP-DOG-015 から。段階 " + stage + ")");
                log.AppendLine("  SKIP おやつの枠 (KD_TreatSlot / KD_TreatController / 自己テスト) (裏山では対象外: 段階 2 IMP-DOG-014 まで)");
            }

            // ---- 拠点板 3 枚 → pouch._Toggle ----
            {
                var openers = Object.FindObjectsOfType<global::KominkaHillside.KH_MenuOpener>(true);
                v.Check(openers.Length == KD_Hillside.OpenerStations.Length, "拠点板 (KH_MenuOpener) が " + KD_Hillside.OpenerStations.Length + " 枚 (actual " + openers.Length + ")");
                foreach (string st in KD_Hillside.OpenerStations)
                {
                    var plate = go.transform.parent.Find(st + "/" + KD_Hillside.OpenerPlateName);
                    var opener = plate == null ? null : plate.GetComponent<global::KominkaHillside.KH_MenuOpener>();
                    object wired;
                    v.Check(opener != null && opener.pouch == pouch
                        && UdonSharpEditorUtility.GetBackingUdonBehaviour(opener).publicVariables.TryGetVariableValue("pouch", out wired)
                        && wired as Object == pouchUdon, "拠点板 " + st + "/" + KD_Hillside.OpenerPlateName + " が pouch を指す (proxy + backing)");
                }
            }

            // ---- ナイトモード (設定面の Slider → KH_NightMode) ----
            {
                var settingsGo = KD_SceneObjects.Find("KD_MenuSettings", out int settingsCount);
                var settings = settingsCount == 1 && settingsGo != null ? settingsGo.GetComponent<KD_MenuSettings>() : null;
                if (v.Check(settings != null, "KD_MenuSettings が 1 個 (Menu/11 Setup Localization が作る)"))
                {
                    var nights = Object.FindObjectsOfType<global::KominkaHillside.KH_NightMode>(true);
                    v.Check(nights.Length == 1 && settings.hillsideNight == nights[0], "ナイトモード: settings.hillsideNight がシーンの KH_NightMode (" + nights.Length + " 個)");
                    Object t = null; string ev = null;
                    if (settings.nightMode != null && settings.nightMode.onValueChanged.GetPersistentEventCount() == 1)
                    {
                        var calls = new SerializedObject(settings.nightMode).FindProperty("m_OnValueChanged.m_PersistentCalls.m_Calls");
                        t = calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue;
                        ev = calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
                    }
                    v.Check(settings.nightMode != null && pouch.settingsHome != null && settings.nightMode.transform.parent == pouch.settingsHome.transform
                        && settings.nightMode.minValue == 0f && settings.nightMode.maxValue == 1f && ev == "_NightMode"
                        && t == UdonSharpEditorUtility.GetBackingUdonBehaviour(settings), "ナイトモード: 設定ホームの Slider (0〜1 = 0〜100%) -> KD_MenuSettings._NightMode");
                    v.Check(pouch.settingsHome != null && pouch.settingsHome.transform.Find("RadioSettingsButton") == null
                        && pouch.settingsPage != null && pouch.settingsPage.transform.Find("RadioSettings") == null, "ラジオ設定の入口と頁が無い (裏山: ラジオ無し)");
                }
            }

            // ---- あそぶ (集合のみ) ----
            {
                int pov = 0, photo = 0;
                var scene = EditorSceneManager.GetActiveScene();
                foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                {
                    if (mb == null || mb.gameObject.scene != scene) continue;
                    string n = mb.GetType().Name;
                    if (n == "KD_DogPovCamera") pov++;
                    if (n == "KD_PhotoDirector") photo++;
                }
                v.Check(pov == 0 && photo == 0, "あそぶは集合のみ: 犬目線カメラ (KD_DogPovCamera " + pov + ")・写真 (KD_PhotoDirector " + photo + ") がシーンに無い");
                v.Check(pouch.activitiesMenu != null, "あそぶ: pouch.activitiesMenu 配線 (面の中身・集合のみの詳細は Menu/23 Verify Activities が数える)");
            }

            // ---- 確認カード (共有 1 枚) / 押せるものの意匠 / onClick 1 本 ----
            v.Check(pouch.confirmRoot != null && pouch.confirmRoot.transform.parent == panel.transform
                && pouch.confirmRoot.transform.GetSiblingIndex() == panel.transform.childCount - 1 && !pouch.confirmRoot.activeSelf,
                "確認カードは共有 1 枚・板直下の最後の子・既定で閉じている");
            {
                int notSliced = 0, badClick = 0;
                foreach (var b in panel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    bool cardStop = b.name == "Card" && b.transform.parent != null && b.transform.parent.name == ConfirmPanelName;
                    if (!cardStop && b.onClick.GetPersistentEventCount() != 1) { badClick++; log.AppendLine("  FAIL " + b.name + " の onClick が " + b.onClick.GetPersistentEventCount() + " 本"); }
                    if (b.name.StartsWith("Tab") || b.name == ConfirmPanelName || cardStop) continue;
                    var img = b.GetComponent<UnityEngine.UI.Image>();
                    if (img != null && img.type != UnityEngine.UI.Image.Type.Sliced) { notSliced++; log.AppendLine("  FAIL " + b.name + " が Sliced でない"); }
                }
                v.Check(badClick == 0, "板の全ボタンの onClick が 1 本 (二重登録なし。違反 " + badClick + ")");
                v.Check(notSliced == 0, "押せるものは全部 Sliced (違反 " + notSliced + ")");
                var circle = KD_UiSpriteEditor.LoadCircle();
                int badSwatch = 0;
                foreach (var img in panel.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                    if (img.name == "Swatch" && (img.sprite != circle || img.type != UnityEngine.UI.Image.Type.Simple)) badSwatch++;
                v.Check(circle != null && badSwatch == 0, "色の丸は円スプライト + Simple (違反 " + badSwatch + ")");
            }

            // ---- 欠字 0 (Static) ----
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            v.Check(font != null && font.atlasPopulationMode == TMPro.AtlasPopulationMode.Static, "共有フォントが Static (Dynamic だと欠字 0 が嘘になる)");
            v.Check(font != null && Mathf.RoundToInt(font.faceInfo.pointSize) == HillsidePointSize, "共有フォントが " + HillsidePointSize + " pt (actual " + (font == null ? 0 : font.faceInfo.pointSize) + ")");
            string missing = KD_ShopSetupEditor.MissingGlyphs(PouchWords() + HillsideWords());
            v.Check(missing.Length == 0, "欠字 0: PouchWords + HillsideWords (欠け " + missing.Length + " 字" + (missing.Length == 0 ? "" : ": " + missing) + ")");

            // ---- NetworkIDs ----
            var desc = Object.FindObjectOfType<VRC.SDK3.Components.VRCSceneDescriptor>();
            if (desc != null && desc.NetworkIDCollection != null)
            {
                int broken = 0;
                foreach (var pair in desc.NetworkIDCollection) if (pair == null || pair.gameObject == null) broken++;
                v.Check(broken == 0, "NetworkIDs broken refs == 0 (actual " + broken + ", total " + desc.NetworkIDCollection.Count + ")");
            }
            v.Finish(HillsideVerifyLog);
        }

        /// <summary>配列が <paramref name="n"/> 本で、要素が全部非 null。</summary>
        static bool AllSet<T>(T[] a, int n) where T : Object
        {
            if (a == null || a.Length != n) return false;
            foreach (var x in a) if (x == null) return false;
            return true;
        }
    }
}
