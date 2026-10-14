using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// 裏山改変 (IMP-DOG-015): 撫でエフェクト 11 種の消費側 (KH_Dogs/KD_PetFxBox) を作る入口 (Menu/6g) と検査 (Menu/6h)。
    ///
    /// 古民家では KD_PetFxBox を Shop Setup (棚と同じ Setup) が作り、同梱 5 種 (キャンディ / かぼちゃ / 提灯 / 流れ星 / 紙吹雪) の粒は
    /// Pet System/9 (レシピ適用) → 共有 prefab 化 (KD_BundledPetFxPrefabs) で各犬の KD_PetSystem_* に置いていた。
    /// 裏山は犬の移植時に 6 種 (♡ + Point 5 種) の粒だけが来ていて、同梱 5 種は fxVariants が null のまま。
    /// ここでは**共有 prefab (Prefabs/PetFx、古民家と同 GUID) を各犬の KD_PetHearts と同じ位置に置く** (古民家の配置と同じ規則:
    /// 古民家の 60 個もすべて KD_PetHearts と同じローカル位置・回転・倍率)。レシピは prefab が正本なので書き換えない。
    ///
    /// **個人装備・同期変数 0 本。** 選択はカタログのカーソル (PlayerData) が正本で、他人へは自分の KD_PlayerFxTag._Publish が配る
    /// (PlayerObject は入室時に複製されるので、札は実行時に探す — KD_PetFxBox.localTag は配線しない)。
    /// </summary>
    public static partial class KD_BundledPetFxEditor
    {
        const string HillsideSetupLog = KD_Hillside.LogDir + "setup-petfx.txt";
        const string HillsideVerifyLog = KD_Hillside.LogDir + "verify-petfx.txt";
        const string PetFxBoxProgramPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_PetFxBox.asset";
        const string CategoryPetFx = "petfx";
        const string DefaultFx = "petfx.heart";
        /// <summary>同梱 5 種 (fxProductKeys の 6..10)。prefab は Prefabs/PetFx/KD_PetFx_{Key}.prefab と {Key}2.prefab (2 層目)。</summary>
        static readonly string[] BundledKeys = { "Candy", "Pumpkin", "Lantern", "Meteor", "Confetti" };
        const int BundledFirst = 6;
        const string HeartsName = "KD_PetHearts";

        static string BundledPrefabPath(int b, int layer) { return KD_BundledPetFxPrefabs.Folder + "/KD_PetFx_" + BundledKeys[b] + (layer == 0 ? "" : "2") + ".prefab"; }

        // ================================================================ Menu/6g Setup Pet Fx

        [MenuItem("Tools/KominkaHillside/Menu/6g Setup Pet Fx")]
        public static void SetupHillside()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_BundledPetFx] 6g Setup Pet Fx (裏山 IMP-DOG-015)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene())
            { log.AppendLine("FAIL " + KD_Hillside.ScenePath + " を Edit モードで開くこと"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out int dogsCount);
            if (dogsGo == null || dogsCount != 1) { log.AppendLine("FAIL " + KD_Hillside.DogsRootName + " が 1 個ない"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            var dogs = dogsGo.transform;
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(PetFxBoxProgramPath) == null)
            { log.AppendLine("FAIL " + PetFxBoxProgramPath + " が無い"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            var catalogT = dogs.Find(KD_Hillside.ShopCatalogName);
            var catalog = catalogT == null ? null : catalogT.GetComponent<KD_ShopCatalog>();
            if (catalog == null) { log.AppendLine("FAIL KH_Dogs/" + KD_Hillside.ShopCatalogName + " が無い。先に Menu/4 Setup Shop"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }

            // ---- 先に全部を検査してから書く (途中で半端な配列を焼かない) ----
            KD_PetHeartsSetupEditor.GetShopEntries(out var keys, out _, out _, out _, out _);
            var prefabs = new GameObject[BundledKeys.Length, 2];
            bool ok = keys.Length == BundledFirst + BundledKeys.Length;
            for (int b = 0; b < BundledKeys.Length; b++)
                for (int layer = 0; layer < 2; layer++)
                {
                    prefabs[b, layer] = AssetDatabase.LoadAssetAtPath<GameObject>(BundledPrefabPath(b, layer));
                    if (prefabs[b, layer] == null || prefabs[b, layer].GetComponent<ParticleSystem>() == null)
                    { log.AppendLine("FAIL 同梱の粒の prefab が無い: " + BundledPrefabPath(b, layer)); ok = false; }
                }
            var defs = KD_DogDefs.All;
            var pets = new KD_DogPetController[defs.Length];
            for (int d = 0; d < defs.Length; d++)
            {
                var t = dogs.Find(defs[d].PetSystemName);
                pets[d] = t == null ? null : t.GetComponent<KD_DogPetController>();
                if (pets[d] == null) { log.AppendLine("FAIL KH_Dogs/" + defs[d].PetSystemName + " に KD_DogPetController が無い"); ok = false; continue; }
                var hearts = t.Find(HeartsName);
                if (hearts == null) { log.AppendLine("FAIL " + defs[d].PetSystemName + "/" + HeartsName + " が無い (粒の置き場の基準)"); ok = false; }
                if (pets[d].fxProductKeys == null || !pets[d].fxProductKeys.SequenceEqual(keys)
                    || pets[d].fxVariants == null || pets[d].fxVariants.Length != keys.Length
                    || pets[d].fxVariants2 == null || pets[d].fxVariants2.Length != keys.Length
                    || pets[d].fxAccentRateMultipliers == null || pets[d].fxAccentRateMultipliers.Length != keys.Length)
                { log.AppendLine("FAIL " + defs[d].PetSystemName + " の fxProductKeys / fxVariants / fxVariants2 / fxAccentRateMultipliers が " + keys.Length + " 種の表と合わない"); ok = false; }
                else for (int i = 0; i < BundledFirst; i++)
                        if (pets[d].fxVariants[i] == null) { log.AppendLine("FAIL " + defs[d].PetSystemName + " の " + keys[i] + " の粒が無い (移植済みのはず)"); ok = false; }
            }
            if (!ok) { log.AppendLine("SETUP RESULT: FAIL (シーンは触っていない)"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }

            // ---- 同梱 5 種の粒を各犬へ (KD_PetHearts と同じ位置。既存は位置を触らない) ----
            int made = 0, kept = 0;
            for (int d = 0; d < defs.Length; d++)
            {
                var pet = pets[d];
                var root = pet.transform;
                var hearts = root.Find(HeartsName);
                for (int b = 0; b < BundledKeys.Length; b++)
                    for (int layer = 0; layer < 2; layer++)
                    {
                        string name = prefabs[b, layer].name;
                        var t = root.Find(name);
                        if (t != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) != BundledPrefabPath(b, layer))
                        { log.AppendLine("FAIL " + root.name + "/" + name + " が共有 prefab のインスタンスでない"); ok = false; continue; }
                        if (t == null)
                        {
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[b, layer], root.gameObject.scene);
                            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
                            go.name = name;
                            go.transform.SetParent(root, false);
                            go.transform.localPosition = hearts.localPosition;
                            go.transform.localRotation = hearts.localRotation;
                            go.transform.localScale = hearts.localScale;
                            go.layer = hearts.gameObject.layer;
                            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                            t = go.transform;
                            made++;
                        }
                        else kept++;
                        var ps = t.GetComponent<ParticleSystem>();
                        if (ps != null) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
                        if (layer == 0) pet.fxVariants[BundledFirst + b] = ps; else pet.fxVariants2[BundledFirst + b] = ps;
                    }
            }
            log.AppendLine("   同梱の粒: 新規 " + made + " / 既存 " + kept + " (6 匹 × 5 種 × 2 層。位置は各犬の " + HeartsName + " と同じ)");

            // ---- 消費側 (SyncMode None・同期変数 0 本) ----
            var boxT = dogs.Find(KD_DogDefs.PetFxBoxName);
            KD_SceneObjects.Find(KD_DogDefs.PetFxBoxName, out int boxCount);
            if (boxCount > 1 || (boxCount == 1 && boxT == null))
            { log.AppendLine("FAIL " + KD_DogDefs.PetFxBoxName + " が " + boxCount + " 個 / KH_Dogs 直下に無い"); log.AppendLine("SETUP RESULT: FAIL"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            GameObject boxGo;
            if (boxT == null)
            {
                boxGo = new GameObject(KD_DogDefs.PetFxBoxName);
                Undo.RegisterCreatedObjectUndo(boxGo, "Create " + KD_DogDefs.PetFxBoxName);
                boxGo.transform.SetParent(dogs, false);
                log.AppendLine("   created " + KD_Hillside.DogsRootName + "/" + KD_DogDefs.PetFxBoxName);
            }
            else boxGo = boxT.gameObject;
            var box = boxGo.GetUdonSharpComponent<KD_PetFxBox>();
            if (box == null)
            {
                try { box = boxGo.AddUdonSharpComponent<KD_PetFxBox>(); log.AppendLine("   added KD_PetFxBox"); }
                catch (Exception e) { log.AppendLine("FAIL KD_PetFxBox: " + e.Message + " — force refresh してから再実行"); KD_VerifyLog.Write(HillsideSetupLog, log); return; }
            }
            KD_ShopSetupEditor.SetSync(box, VRC.SDKBase.Networking.SyncType.None, log);
            box.catalog = catalog;
            box.categoryKey = CategoryPetFx;
            box.defaultProductKey = DefaultFx;
            box.petControllers = pets;
            box.localTag = null;   // 実行時に自分の PlayerObject から探す (シーンの雛形を配線すると他人の札を握る)
            foreach (var pet in pets)
            {
                pet.petFxBox = box;
                try { UdonSharpEditorUtility.CopyProxyToUdon(pet); EditorUtility.SetDirty(pet); }
                catch (Exception e) { log.AppendLine("FAIL CopyProxyToUdon(" + pet.name + "): " + e.Message); ok = false; }
            }

            // ---- カタログの通知先へ (おもちゃの KD_ToyBox は裏山に無い。既存の通知先は保つ) ----
            var consumers = (catalog.consumers ?? new UdonSharpBehaviour[0]).ToList();
            var events = (catalog.consumerEvents ?? new string[0]).ToList();
            while (events.Count < consumers.Count) events.Add("");
            if (events.Count > consumers.Count) events.RemoveRange(consumers.Count, events.Count - consumers.Count);
            int at = consumers.IndexOf(box);
            if (at < 0) { consumers.Add(box); events.Add("_OnShopSelectionChanged"); }
            else events[at] = "_OnShopSelectionChanged";
            catalog.consumers = consumers.ToArray();
            catalog.consumerEvents = events.ToArray();
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(box);
                UdonSharpEditorUtility.CopyProxyToUdon(catalog);
            }
            catch (Exception e) { log.AppendLine("FAIL CopyProxyToUdon: " + e.Message); ok = false; }
            EditorUtility.SetDirty(box); EditorUtility.SetDirty(catalog);
            log.AppendLine("   KD_PetFxBox: catalog / categoryKey=petfx / default=" + DefaultFx + " / petControllers " + pets.Length + " / catalog.consumers " + consumers.Count);

            // ---- 手元メニュー (段階 2 の Menu/7 も同じ配線をする。ここでも入れておくと 18 より前に Verify できる) ----
            var pouchT = dogs.Find(KD_DogDefs.TreatPouchName);
            var pouch = pouchT == null ? null : pouchT.GetComponent<KD_TreatPouch>();
            if (pouch != null)
            {
                pouch.petFxBox = box;
                // 裏山改変 IMP-PRG-002 (2026-10-14): 撫でFX の行の商品 (fxItems、keys と同じ 11 枠)。書き手の Menu/7 は周回版で拒否されるのでここでも書く。
                // 公開しない同梱 FX (キャンディ / かぼちゃ / 提灯 / 流れ星 / 紙吹雪) は null = 行ごと隠れる (KD_TreatPouch.RedrawFxRows の既存の扱い)。
                // 粒 (fxVariants) と fxProductKeys は 11 種のまま (添字を動かさない)
                var fxItems = new KD_ShopItem[keys.Length];
                int fxWithheld = 0;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (KD_ProductDefinitions.IsWithheldKey(keys[i])) { fxWithheld++; continue; }
                    fxItems[i] = catalog.items == null ? null : catalog.items.FirstOrDefault(x => x != null && x.productKey == keys[i]);
                    if (fxItems[i] == null) { log.AppendLine("FAIL 撫でFX の商品 " + keys[i] + " がカタログに無い。先に Menu/4 Setup Shop"); ok = false; }
                }
                pouch.fxItems = fxItems;
                log.AppendLine("   pouch.fxItems: " + (keys.Length - fxWithheld) + " 件 + 公開しない " + fxWithheld + " 枠は null (IMP-PRG-002。行は隠れる)");
                try { UdonSharpEditorUtility.CopyProxyToUdon(pouch); } catch (Exception e) { log.AppendLine("FAIL CopyProxyToUdon(pouch): " + e.Message); ok = false; }
                EditorUtility.SetDirty(pouch);
            }

            var tagGo = KD_SceneObjects.Find(KD_DogDefs.PlayerFxTagName, out int tagCount);
            if (tagGo == null || tagCount != 1 || tagGo.GetComponent<KD_PlayerFxTag>() == null)
            { log.AppendLine("FAIL " + KD_DogDefs.PlayerFxTagName + " が 1 個ない (" + tagCount + ")。段階 1 の Menu/4 Setup Shop が作る"); ok = false; }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine("SETUP RESULT: " + (ok ? "PASS" : "FAIL"));
            log.AppendLine("**シーンは保存していない。** 次: 7 Build Menu (pouch.petFxBox) → 6h Verify Pet Fx");
            KD_VerifyLog.Write(HillsideSetupLog, log);
        }

        /// <summary>段階 2 の Menu/7 (SetupHillside) が呼ぶ: KH_Dogs/KD_PetFxBox を探して返す (無ければ FAIL を書いて null)。</summary>
        internal static KD_PetFxBox HillsideFindBox(Transform dogs, StringBuilder log)
        {
            var t = dogs.Find(KD_DogDefs.PetFxBoxName);
            KD_SceneObjects.Find(KD_DogDefs.PetFxBoxName, out int count);
            var box = t == null ? null : t.GetComponent<KD_PetFxBox>();
            if (box == null || count != 1) log.AppendLine("FAIL KH_Dogs/" + KD_DogDefs.PetFxBoxName + " に KD_PetFxBox が 1 個ない (" + count + ")。先に Menu/6g Setup Pet Fx");
            return count == 1 ? box : null;
        }

        // ================================================================ Menu/6h Verify Pet Fx

        [MenuItem("Tools/KominkaHillside/Menu/6h Verify Pet Fx")]
        public static void VerifyHillside()
        {
            var v = new KD_VerifyLog("[KD_BundledPetFx] 6h Verify Pet Fx (裏山 IMP-DOG-015)");
            if (EditorApplication.isPlaying || !KD_Hillside.InScene()) { v.Check(false, KD_Hillside.ScenePath + " を Edit モードで開いている"); v.Finish(HillsideVerifyLog); return; }
            var dogsGo = KD_SceneObjects.Find(KD_Hillside.DogsRootName);
            if (dogsGo == null) { v.Check(false, "KH_Dogs がある"); v.Finish(HillsideVerifyLog); return; }
            var dogs = dogsGo.transform;
            KD_PetHeartsSetupEditor.GetShopEntries(out var keys, out _, out _, out _, out _);
            v.Check(keys.Length == 11, "撫でFX 11 種 (♡ / 肉球 / キラキラ / 花びら / 桜ハート / 金の肉球 + 同梱 5)");

            // ---- 消費側 ----
            var boxGo = KD_SceneObjects.Find(KD_DogDefs.PetFxBoxName, out int boxCount);
            var box = boxGo == null ? null : boxGo.GetComponent<KD_PetFxBox>();
            v.Check(boxCount == 1 && box != null && boxGo.transform.parent == dogs, "KH_Dogs/" + KD_DogDefs.PetFxBoxName + " に KD_PetFxBox が 1 個 (" + boxCount + ")");
            if (box == null) { v.Finish(HillsideVerifyLog); return; }
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(box);
            v.Check(backing != null && backing.SyncMethod == VRC.SDKBase.Networking.SyncType.None, "KD_PetFxBox の SyncMethod None (個人装備)");
            int synced = typeof(KD_PetFxBox).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Count(f => f.GetCustomAttributes(typeof(UdonSyncedAttribute), true).Length > 0);
            v.Check(synced == 0, "KD_PetFxBox の同期変数 0 本 (" + synced + ")");
            var catalogT = dogs.Find(KD_Hillside.ShopCatalogName);
            var catalog = catalogT == null ? null : catalogT.GetComponent<KD_ShopCatalog>();
            v.Check(catalog != null && box.catalog == catalog && box.categoryKey == CategoryPetFx && box.defaultProductKey == DefaultFx,
                "catalog = KH_Dogs/KD_ShopCatalog / categoryKey petfx / 既定 " + DefaultFx);
            v.Check(box.localTag == null, "localTag は配線しない (実行時に自分の PlayerObject から探す)");
            int at = catalog == null || catalog.consumers == null ? -1 : Array.IndexOf(catalog.consumers, box);
            v.Check(at >= 0 && catalog.consumerEvents != null && at < catalog.consumerEvents.Length && catalog.consumerEvents[at] == "_OnShopSelectionChanged"
                && catalog.consumers.Length == catalog.consumerEvents.Length, "カタログの通知先に KD_PetFxBox._OnShopSelectionChanged (consumers と events が同数)");

            // ---- 各犬の粒 ----
            var defs = KD_DogDefs.All;
            v.Check(box.petControllers != null && box.petControllers.Length == defs.Length, "petControllers " + defs.Length + " 匹");
            for (int d = 0; d < defs.Length; d++)
            {
                var t = dogs.Find(defs[d].PetSystemName);
                var pet = t == null ? null : t.GetComponent<KD_DogPetController>();
                if (!v.Check(pet != null, defs[d].PetSystemName + " に KD_DogPetController")) continue;
                v.Check(box.petControllers != null && d < box.petControllers.Length && box.petControllers[d] == pet && pet.petFxBox == box, defs[d].key + ": 配り先と petFxBox の往復 (撫で主に合わせて差し替える口)");
                v.Check(pet.fxProductKeys != null && pet.fxProductKeys.SequenceEqual(keys), defs[d].key + ": fxProductKeys が 11 種の表どおり");
                bool all = pet.fxVariants != null && pet.fxVariants.Length == keys.Length && pet.fxVariants.All(x => x != null && x.transform.parent == t);
                v.Check(all, defs[d].key + ": fxVariants 11 種すべてに粒 (KD_PetSystem の子)");
                var hearts = t.Find(HeartsName);
                bool bundled = hearts != null && pet.fxVariants2 != null && pet.fxVariants2.Length == keys.Length;
                for (int b = 0; bundled && b < BundledKeys.Length; b++)
                    for (int layer = 0; layer < 2; layer++)
                    {
                        var ps = layer == 0 ? pet.fxVariants[BundledFirst + b] : pet.fxVariants2[BundledFirst + b];
                        bundled &= ps != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ps.gameObject) == BundledPrefabPath(b, layer)
                            && (ps.transform.localPosition - hearts.localPosition).sqrMagnitude < 1e-10f && Quaternion.Angle(ps.transform.localRotation, hearts.localRotation) < .01f
                            && ps.GetComponent<ParticleSystemRenderer>() != null && ps.GetComponent<ParticleSystemRenderer>().sharedMaterial != null
                            && !ps.main.playOnAwake;
                    }
                v.Check(bundled, defs[d].key + ": 同梱 5 種 × 2 層が共有 prefab (Prefabs/PetFx) のインスタンスで " + HeartsName + " と同じ位置、材質あり、playOnAwake なし");
                var pb = UdonSharpEditorUtility.GetBackingUdonBehaviour(pet);
                object raw = null;
                v.Check(pb != null && pb.publicVariables.TryGetVariableValue("fxVariants", out raw) && raw is Object[] refs && refs.Length == keys.Length
                    && Enumerable.Range(0, keys.Length).All(i => refs[i] == pet.fxVariants[i]), defs[d].key + ": backing の fxVariants が proxy と一致");
            }

            // ---- 他人へ配る札 ----
            var tagGo = KD_SceneObjects.Find(KD_DogDefs.PlayerFxTagName, out int tagCount);
            var tag = tagGo == null ? null : tagGo.GetComponent<KD_PlayerFxTag>();
            v.Check(tagCount == 1 && tag != null && tagGo.GetComponent<VRC.SDK3.Components.VRCPlayerObject>() != null, "KD_PlayerFxTag (VRCPlayerObject) が 1 個");
            int tagSynced = typeof(KD_PlayerFxTag).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Count(f => f.GetCustomAttributes(typeof(UdonSyncedAttribute), true).Length > 0);
            v.Check(tag != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(tag).SyncMethod == VRC.SDKBase.Networking.SyncType.Manual && tagSynced == 1,
                "札は Manual・同期変数 1 本 (選択した品目キーだけ)");

            // ---- 商品と手元メニュー ----
            var pouchT = dogs.Find(KD_DogDefs.TreatPouchName);
            var pouch = pouchT == null ? null : pouchT.GetComponent<KD_TreatPouch>();
            if (pouch == null) { v.Check(false, "KH_Dogs/" + KD_DogDefs.TreatPouchName + " がある"); v.Finish(HillsideVerifyLog); return; }
            bool stage2 = pouch.hillsideStage >= 2;
            if (stage2) v.Check(pouch.petFxBox == box, "pouch.petFxBox = KD_PetFxBox (段階 2 から必須)");
            else v.Note("pouch.petFxBox " + (pouch.petFxBox == box ? "配線済み" : "未配線") + " (段階 " + pouch.hillsideStage + "。段階 2 の Menu/7 で必須)");
            // 裏山改変 IMP-PRG-002 (2026-10-14): 公開しない同梱 FX の枠は null (行ごと隠れる)、ほかは表の順
            v.Check(pouch.fxItems != null && pouch.fxItems.Length == keys.Length && Enumerable.Range(0, keys.Length).All(i => KD_ProductDefinitions.IsWithheldKey(keys[i])
                    ? pouch.fxItems[i] == null : pouch.fxItems[i] != null && pouch.fxItems[i].productKey == keys[i]),
                "pouch.fxItems 11 枠が表の順 (" + string.Join(", ", keys) + ")。公開しない " + keys.Count(KD_ProductDefinitions.IsWithheldKey) + " 枠は null (IMP-PRG-002)");
            for (int i = BundledFirst; catalog != null && catalog.items != null && i < keys.Length; i++)
            {
                // 同梱の判定はカタログの行で見る (手元メニューの枠は公開しない分が null)
                var item = catalog.items.FirstOrDefault(x => x != null && x.productKey == keys[i]);
                if (item == null) { v.Check(false, keys[i] + ": カタログに行がある"); continue; }
                string parentKey = i == 6 ? "live.candy_step_set" : i == 7 ? "live.pumpkin_parade_set" : "live.kominka_pack";
                var parent = catalog.items.FirstOrDefault(x => x != null && x.productKey == parentKey);
                bool withheld = KD_ProductDefinitions.IsWithheldKey(keys[i]);
                v.Check(parent != null && item.IsCE() && item.price == 0 && item.ceProduct == parent.ceProduct && item.ceListingId == parent.ceListingId && item.ceCredits == parent.ceCredits
                    && item.menuOnly && item.GetComponent<Collider>() == null && (!withheld || (item.CeWithheld() && parent.CeWithheld())),
                    keys[i] + ": 同梱 (" + parentKey + ") と所有・購入先・Credits を共有、Point 価格なし、メニューだけで買う"
                    + (withheld ? "。IMP-PRG-002 で公開しない (Product なし = 所有が付かず購入画面も開かない)" : ""));
            }
            // FxRow (かざりの Content) と onClick
            var backingPouch = UdonSharpEditorUtility.GetBackingUdonBehaviour(pouch);
            bool rows = pouch.fxButtons != null && pouch.fxButtons.Length == keys.Length && pouch.fxLabels != null && pouch.fxLabels.Length == keys.Length;
            for (int i = 0; rows && i < keys.Length; i++)
            {
                var btn = pouch.fxButtons[i] == null ? null : pouch.fxButtons[i].GetComponent<UnityEngine.UI.Button>();
                var calls = btn == null ? null : new SerializedObject(btn).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                rows &= calls != null && calls.arraySize == 1 && btn.onClick.GetPersistentTarget(0) == backingPouch
                    && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == "_Fx" + i
                    && typeof(KD_TreatPouch).GetMethod("_Fx" + i) != null
                    && pouch.fxLabels[i] != null && pouch.fxLabels[i].transform.parent != null && pouch.fxLabels[i].transform.parent.name == "FxRow" + i;
            }
            v.Check(rows, "FxRow0..10 の [つける/買う] が pouch の _Fx{i} を 1 本だけ呼ぶ");
            var accScroll = pouch.tabScrolls != null && pouch.tabScrolls.Length > KD_TreatPouch.TabAcc ? pouch.tabScrolls[KD_TreatPouch.TabAcc] : null;
            v.Check(accScroll != null && pouch.fxLabels != null && pouch.fxLabels.All(l => l != null && l.transform.parent.parent == accScroll.content),
                "撫でFX の行は かざりの Content の中 (合流。専用のタブは無い)");
            v.Check(pouch.fxConfirmRoot != null && pouch.fxConfirmRoot == pouch.confirmRoot && !pouch.fxConfirmRoot.transform.IsChildOf(accScroll != null ? accScroll.transform : pouch.transform),
                "撫でFX の確認は共有カード 1 枚 (kind Fx)、スクロールの外");
            v.Check(typeof(KD_TreatPouch).GetMethod("RedrawFxRows", BindingFlags.Instance | BindingFlags.NonPublic) != null
                && typeof(KD_TreatPouch).GetMethod("_FxBuy") != null && typeof(KD_TreatPouch).GetMethod("_FxCancel") != null,
                "描画の口 RedrawFxRows (かざりのカタログ階層だけ) と _FxBuy / _FxCancel がある");
            v.Note("PCVR / 2 人でしか分からないこと: 自分の撫でに選んだ粒が出る、他人が撫でたとき他人の選んだ粒が見える (KD_PlayerFxTag)、再入室で選択が戻る (PlayerData)");
            v.Finish(HillsideVerifyLog);
        }
    }
}
