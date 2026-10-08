using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KominkaDogRun;
using KominkaDogRun.Editor;

namespace KominkaHillside.Editor
{
    /// <summary>
    /// DES-DOG-009 (2026-10-08): 犬の毛色 (インスタンスのマスターが選ぶ・全員共通・犬ごと) の配線。設計 docs/plans/2026-10-08-dog-book-and-fur-color.md §4〜6。
    ///
    /// `Tools/KominkaHillside/DogBook/5 - Build Fur Colors` (前提: DogBook/0 で KH_FurColor の Program Asset、DogBook/1 で板)。シーンは保存しない。
    ///
    /// 1. 犬 6 匹 (KD_Dog_&lt;key&gt;) の Renderer から毛の材質スロット (lilToon の毛シェーダー: Uzumore 082bc8… / lts_fur_two 54bc8b…) を集め、
    ///    距離フェードの Uzumore スロットの _MainTex (= その犬の毛のベースカラー) と同じテクスチャのスロットだけを対象にする。
    ///    目・口・その他 (me / kuti / sonota) と、飾り KD_Acc_ / KD_CandyCostume_ / 裏山の部品 KH_ の下は対象外。
    /// 2. 犬ごとの色の一覧 = [もとの色 (今の _MainTex)] + 下の表 <see cref="Table"/> で見つかったもの + 持ち込み置き場
    ///    <see cref="DropInDir"/>/&lt;key&gt;/&lt;色&gt;.png (色 = <see cref="ColorKeys"/> の末尾。black.png → furcolor.black)。
    ///    **見つからない行は WARN で一覧に出すだけ** (テクスチャの有無に依存しない)。同じテクスチャ・同じ色名は先の方を残す。
    /// 3. 各テクスチャの平均色を 16×16 へ縮めて測り、ログに出す (色名は推定。ユーザーが目視で直す)。
    /// 購入アセット (Assets/TORINOSUKE) は**読むだけ** (インポート設定も触らない)。持ち込みのテクスチャは Assets/KominkaHillside/ 側へコピーして置く。
    /// </summary>
    public static class KH_FurColorEditor
    {
        public const string SystemName = "KH_FurColor";
        public const string ScriptPath = "Assets/KominkaHillside/Scripts/KH_FurColor.cs";
        public const string ProgramPath = "Assets/KominkaHillside/Scripts/KH_FurColor.asset";
        public const string PackRoot = "Assets/TORINOSUKE/犬ギミック/";
        /// <summary>持ち込みの置き場 (無ければ何もしない)。&lt;key&gt; は KD_DogDefs の key (Corgi / Pome / Lab / Dachs / Shiba / Samoyed)。</summary>
        public const string DropInDir = "Assets/KominkaHillside/Textures/FurColors";
        const string Menu = "Tools/KominkaHillside/DogBook/";
        const string BuildLog = KD_Hillside.LogDir + "fur-color-build.txt";
        /// <summary>毛のシェーダー (アセットの GUID)。Uzumore = 距離フェード付きの lilToon Fur 2 pass (lilcontainer)、lts_fur_two = lilToon 本体。</summary>
        public static readonly string[] FurShaderGuids = { "082bc8ee39ecb30459ed0dde7a7f341d", "54bc8b41278802d4a81b27fe402994e2" };
        public const string UzumoreShaderGuid = "082bc8ee39ecb30459ed0dde7a7f341d";
        /// <summary>色名の翻訳キー (翻訳表の 13 言語に置く)。持ち込みのファイル名はこの末尾 (black / white / …)。</summary>
        public static readonly string[] ColorKeys = { "furcolor.black", "furcolor.white", "furcolor.cream", "furcolor.brown", "furcolor.red", "furcolor.lightred", "furcolor.tricolor", "furcolor.choco" };

        /// <summary>犬種 → 購入パックのフォルダ (材質を探す範囲)。</summary>
        static readonly Dictionary<string, string> BreedFolder = new Dictionary<string, string>
        {
            { "Corgi", "コーギー" }, { "Pome", "もちぽめ" }, { "Lab", "ラブラドール" }, { "Dachs", "ダックスフンド" }, { "Shiba", "柴犬_remake" }, { "Samoyed", "サモエド" },
        };

        /// <summary>候補の 1 行。material = 購入パックの材質名 (拡張子なし。その _MainTex を使う)、texture = 犬種フォルダからの相対パス (どちらか)。</summary>
        public struct Candidate
        {
            public string dog, material, texture, nameKey, note;
            public Candidate(string dog, string material, string texture, string nameKey, string note)
            { this.dog = dog; this.material = material; this.texture = texture; this.nameKey = nameKey; this.note = note; }
        }

        /// <summary>
        /// 候補の表。出典は古民家 DES-DOG-012「購入パックに入っている毛色」の材質名 (2026-09-22) と、この repo の残りのテクスチャ (2026-10-08 の平均色で推定)。
        /// **色名はすべて推定** (古民家の資料の言葉か平均色。実物の見た目はユーザーが確かめる)。この repo に無い材質は WARN で出るだけ。
        /// </summary>
        public static readonly Candidate[] Table =
        {
            new Candidate("Corgi", "ke 1", null, "furcolor.tricolor", "DES-DOG-012 ke 1 (色は未確認。推定)"),
            new Candidate("Corgi", "ke 2", null, "furcolor.black", "DES-DOG-012 ke 2 (色は未確認。推定)"),
            new Candidate("Corgi", "ke 3", null, "furcolor.cream", "DES-DOG-012 ke 3 (色は未確認。推定)"),
            new Candidate("Corgi", "ke 4", null, "furcolor.brown", "DES-DOG-012 ke 4 (色は未確認。推定)"),
            new Candidate("Pome", "ke 2", null, "furcolor.black", "DES-DOG-012 ke 2 黒"),
            new Candidate("Pome", "ke 3", null, "furcolor.cream", "DES-DOG-012 ke 3 クリーム"),
            new Candidate("Pome", "ke 4", null, "furcolor.brown", "DES-DOG-012 ke 4 茶"),
            new Candidate("Lab", "ke 2", null, "furcolor.white", "DES-DOG-012 ke 2 白"),
            new Candidate("Lab", "ke 3", null, "furcolor.black", "DES-DOG-012 ke 3 黒"),
            new Candidate("Dachs", "ke 2", null, "furcolor.red", "DES-DOG-012 ke 2 明るいレッド"),
            new Candidate("Dachs", "ke 3", null, "furcolor.choco", "DES-DOG-012 ke 3 チョコ"),
            // この repo にある: ke.mat → shiba/マテリアル_Base_color.png (平均 #bca485、今の ke 1 = Base_color 2 #cc927a より淡い)
            new Candidate("Shiba", "ke", null, "furcolor.lightred", "DES-DOG-012 ke 淡い赤 / repo にあり (平均色で推定)"),
            new Candidate("Shiba", "ke 2", null, "furcolor.black", "DES-DOG-012 ke 2 黒"),
            new Candidate("Shiba", "ke 3", null, "furcolor.white", "DES-DOG-012 ke 3 白"),
            // この repo にある: サモエド毛.mat → samoedo/DefaultMaterial.001_Base_Color 2.png (平均 #312b2b、今の毛 2 = Base_Color 1 の白黒反転の配置)。
            // 古民家の資料は「毛 2 黒」だが、この repo では 毛 2 が今の白、毛 (2 なし) が黒
            new Candidate("Samoyed", "サモエド毛", null, "furcolor.black", "DES-DOG-012 毛 2 黒 / repo では サモエド毛.mat が黒 (平均色で推定)"),
        };

        class Target { public Renderer renderer; public int slot; public Texture2D texture; public Material material; }

        [MenuItem(Menu + "5 - Build Fur Colors")]
        public static void Build()
        {
            KD_Hillside.Guard();
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProgramPath) == null) throw new InvalidOperationException("Program Asset missing: " + ProgramPath + " (先に DogBook/0 → コンパイル)");
            var log = new StringBuilder("[KH_FurColor] Build (DES-DOG-009)\n");
            var catalog = KD_LocalizationEditor.Load();

            var renderers = new List<Renderer>(); var slots = new List<int>(); var dogsOf = new List<int>();
            var textures = new List<Texture2D>(); var keys = new List<string>(); var start = new int[KH_FurColor.DogCount]; var counts = new int[KH_FurColor.DogCount];
            var defs = KD_DogDefs.All;
            if (defs.Length != KH_FurColor.DogCount) throw new InvalidOperationException("KD_DogDefs.All must have " + KH_FurColor.DogCount + " dogs");
            int warn = 0;
            for (int d = 0; d < defs.Length; d++)
            {
                var def = defs[d];
                List<Target> targets; Texture2D baseTex; List<string> skipped;
                Collect(def, out targets, out baseTex, out skipped);
                log.AppendLine("== " + d + " " + def.key + ": fur base " + Describe(baseTex));
                foreach (var t in targets) { renderers.Add(t.renderer); slots.Add(t.slot); dogsOf.Add(d); log.AppendLine("   target " + KD_SceneObjects.PathOf(t.renderer.gameObject) + "[" + t.slot + "] " + t.material.name); }
                foreach (var s in skipped) log.AppendLine("   skip   " + s);

                start[d] = textures.Count;
                textures.Add(baseTex); keys.Add(KH_FurColor.DefaultKey);
                foreach (var option in Options(def.key, log, ref warn))
                {
                    if (option.Key == null) continue;
                    var tex = option.Key; var key = option.Value;
                    int from = start[d];
                    bool sameTex = false, sameKey = false;
                    for (int i = from; i < textures.Count; i++) { if (textures[i] == tex) sameTex = true; if (keys[i] == key) sameKey = true; }
                    if (sameTex) { log.AppendLine("   WARN option " + key + " " + Describe(tex) + " is already in the list (same texture) — skipped"); warn++; continue; }
                    if (sameKey) { log.AppendLine("   WARN option " + key + " " + Describe(tex) + " duplicates a color name — skipped (rename the file / fix the table)"); warn++; continue; }
                    textures.Add(tex); keys.Add(key);
                    log.AppendLine("   option " + (textures.Count - from - 1) + " " + key + " (" + KD_LocalizationEditor.Text(catalog, "ja", key) + ") " + Describe(tex));
                }
                counts[d] = textures.Count - start[d];
                if (counts[d] <= 1) log.AppendLine("   NOTE no other colors for " + def.key + " (行は「ほかの毛色がありません」になる)");
            }

            var system = EnsureSystem(log);
            system.targetRenderers = renderers.ToArray(); system.targetSlots = slots.ToArray(); system.targetDogs = dogsOf.ToArray();
            system.textures = textures.ToArray(); system.nameKeys = keys.ToArray(); system.textureStart = start; system.textureCount = counts;
            KD_ProductsEditor.Copy(system);
            var books = KD_ProductsEditor.SceneComponents<KD_DogBook>();
            if (books.Length == 1) { books[0].furColor = system; KD_ProductsEditor.Copy(books[0]); log.AppendLine("KD_DogBook.furColor wired"); }
            else { log.AppendLine("WARN KD_DogBook " + books.Length + " 個 — 板は未配線 (DogBook/1 Setup の後にもう一度流す)"); warn++; }
            EditorSceneManager.MarkSceneDirty(system.gameObject.scene);
            log.AppendLine("RESULT targets=" + renderers.Count + " textures=" + textures.Count + " (dogs: " + string.Join(" ", defs.Select((x, i) => x.key + "=" + counts[i])) + ") warn=" + warn);
            Directory.CreateDirectory(Path.GetDirectoryName(BuildLog));
            File.WriteAllText(BuildLog, log.ToString());
            Debug.Log(log.ToString());
        }

        static KH_FurColor EnsureSystem(StringBuilder log)
        {
            int count;
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out count);
            if (count != 1 || dogs == null) throw new InvalidOperationException(KD_Hillside.DogsRootName + " must exist once");
            var go = KD_SceneObjects.Find(SystemName, out count);
            if (count > 1) throw new InvalidOperationException("Duplicate " + SystemName);
            if (go == null)
            {
                go = new GameObject(SystemName);
                Undo.RegisterCreatedObjectUndo(go, "Create " + SystemName);
                Undo.SetTransformParent(go.transform, dogs.transform, "Place " + SystemName);
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + SystemName);
            }
            var system = go.GetComponent<KH_FurColor>();
            if (system == null) system = go.AddUdonSharpComponent<KH_FurColor>();
            // vrc-udonsharp §2: AddUdonSharpComponent right after the Program Asset was created can bake Continuous — fix it every run
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(system);
            if (udon != null && udon.SyncMethod != VRC.SDKBase.Networking.SyncType.Manual) { udon.SyncMethod = VRC.SDKBase.Networking.SyncType.Manual; EditorUtility.SetDirty(udon); log.AppendLine("sync method set to Manual"); }
            return system;
        }

        /// <summary>犬 1 匹の対象スロット。基準 = Uzumore (距離フェード) スロットの _MainTex。それと同じテクスチャの毛シェーダーのスロットだけ。</summary>
        static void Collect(KD_DogDef def, out List<Target> targets, out Texture2D baseTex, out List<string> skipped)
        {
            int count;
            var dog = KD_SceneObjects.Find(def.DogName, out count);
            if (count != 1 || dog == null) throw new InvalidOperationException("Dog missing/duplicate: " + def.DogName);
            var fur = new List<Target>(); skipped = new List<string>();
            foreach (var r in dog.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                string part = ExcludedPart(r.transform, dog.transform);
                if (part != null) { skipped.Add(KD_SceneObjects.PathOf(r.gameObject) + " (" + part + ")"); continue; }
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (!IsFurShader(m.shader)) { skipped.Add(KD_SceneObjects.PathOf(r.gameObject) + "[" + i + "] " + m.name + " (not a fur shader)"); continue; }
                    fur.Add(new Target { renderer = r, slot = i, material = m, texture = m.GetTexture("_MainTex") as Texture2D });
                }
            }
            var bases = fur.Where(t => ShaderGuid(t.material.shader) == UzumoreShaderGuid).Select(t => t.texture).Distinct().ToArray();
            if (bases.Length != 1 || bases[0] == null) throw new InvalidOperationException(def.key + ": expected one distance-fade (Uzumore) fur texture, found " + bases.Length + " (Menu/14 の距離フェードの前提が崩れている?)");
            baseTex = bases[0];
            var b = baseTex;
            targets = fur.Where(t => t.texture == b).ToList();
            foreach (var t in fur.Where(t => t.texture != b))
                skipped.Add(KD_SceneObjects.PathOf(t.renderer.gameObject) + "[" + t.slot + "] " + t.material.name + " (fur shader, other texture " + Describe(t.texture) + ")");
        }

        static string ExcludedPart(Transform t, Transform dog)
        {
            for (var p = t; p != null && p != dog; p = p.parent)
                if (p.name.StartsWith("KD_Acc_") || p.name.StartsWith("KD_CandyCostume_") || p.name.StartsWith("KH_")) return "accessory / hillside part " + p.name;
            return null;
        }

        static bool IsFurShader(Shader shader) { return shader != null && FurShaderGuids.Contains(ShaderGuid(shader)); }

        static string ShaderGuid(Shader shader)
        {
            string guid; long local;
            return shader != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out guid, out local) ? guid : "";
        }

        /// <summary>表の行 (この犬の分) と持ち込み置き場を、見つかった順に (テクスチャ, 色名キー) で返す。見つからない表の行は WARN。</summary>
        static IEnumerable<KeyValuePair<Texture2D, string>> Options(string dogKey, StringBuilder log, ref int warn)
        {
            var result = new List<KeyValuePair<Texture2D, string>>();
            string folder = PackRoot + BreedFolder[dogKey];
            foreach (var c in Table.Where(x => x.dog == dogKey))
            {
                Texture2D tex = null; string from = null;
                if (!string.IsNullOrEmpty(c.material))
                {
                    var mat = AssetDatabase.FindAssets("t:Material", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                        .Where(p => Path.GetFileNameWithoutExtension(p) == c.material).Select(AssetDatabase.LoadAssetAtPath<Material>).FirstOrDefault();
                    if (mat != null) { tex = mat.GetTexture("_MainTex") as Texture2D; from = AssetDatabase.GetAssetPath(mat); }
                }
                if (tex == null && !string.IsNullOrEmpty(c.texture)) { tex = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + c.texture); from = folder + "/" + c.texture; }
                if (tex == null) { log.AppendLine("   WARN missing " + c.nameKey + " — " + (c.material != null ? "material \"" + c.material + "\"" : "texture " + c.texture) + " not in " + folder + " (" + c.note + ")"); warn++; continue; }
                log.AppendLine("   found  " + c.nameKey + " from " + from + " (" + c.note + ")");
                result.Add(new KeyValuePair<Texture2D, string>(tex, c.nameKey));
            }
            string drop = DropInDir + "/" + dogKey;
            if (AssetDatabase.IsValidFolder(drop))
                foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { drop }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal))
                {
                    string key = "furcolor." + Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    if (!ColorKeys.Contains(key)) { log.AppendLine("   WARN drop-in " + path + " — unknown color name (use one of: " + string.Join(", ", ColorKeys.Select(k => k.Substring(9))) + ")"); warn++; continue; }
                    log.AppendLine("   found  " + key + " from drop-in " + path);
                    result.Add(new KeyValuePair<Texture2D, string>(AssetDatabase.LoadAssetAtPath<Texture2D>(path), key));
                }
            return result;
        }

        /// <summary>テクスチャのパス・大きさ・平均色 (16×16 へ縮めて測る推定。読み取り不可のテクスチャでも測れる)。</summary>
        public static string Describe(Texture2D tex)
        {
            if (tex == null) return "(null)";
            return AssetDatabase.GetAssetPath(tex) + " " + tex.width + "x" + tex.height + " avg #" + ColorUtility.ToHtmlStringRGB(Average(tex));
        }

        static Color Average(Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            var read = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
                read.Apply();
                var px = read.GetPixels();
                Color sum = Color.clear;
                foreach (var c in px) sum += c;
                return sum / px.Length;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(read);
            }
        }

        /// <summary>DogBook/3 Verify (KD_DogBookEditor.VerifyHillside) から呼ばれる。配線・スロット・既定テクスチャ・翻訳キー・字形・権限の形を数える。直さない。</summary>
        public static void VerifyInto(KD_VerifyLog v, KD_DogBook book, KD_LocalizationEditor.Catalog catalog, TMP_FontAsset font)
        {
            v.Info("-- fur color (KH_FurColor, DES-DOG-009) --");
            var systems = KD_ProductsEditor.SceneComponents<KH_FurColor>();
            if (!v.Check(systems.Length == 1, "one " + SystemName + " (" + systems.Length + ")")) return;
            var s = systems[0];
            v.Check(book.furColor == s, "KD_DogBook.furColor wired");
            v.Check(s.transform.parent != null && s.transform.parent.name == KD_Hillside.DogsRootName && s.transform.parent.parent == null, SystemName + " directly under " + KD_Hillside.DogsRootName);
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(s);
            v.Check(udon != null && udon.SyncMethod == VRC.SDKBase.Networking.SyncType.Manual, "sync method Manual (backing UdonBehaviour)");
            v.Check(s.gameObject.activeInHierarchy && s.enabled, SystemName + " active (an inactive UdonBehaviour is not synced)");
            v.Check(s.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null, "no VRCObjectSync on " + SystemName + " (Manual only)");
            string src = File.Exists(ScriptPath) ? File.ReadAllText(ScriptPath) : "";
            v.Check(src.Contains("OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) { return false; }") && !src.Contains("SetOwner("), "nobody can take the Owner over (OnOwnershipRequest false, no SetOwner) — source check");
            v.Check(src.Contains("Networking.IsOwner(gameObject)") && !src.Contains(".isMaster"), "writes are gated by Ownership, not isMaster — source check");

            int n = s.targetRenderers == null ? -1 : s.targetRenderers.Length;
            bool shapes = n > 0 && s.targetSlots != null && s.targetSlots.Length == n && s.targetDogs != null && s.targetDogs.Length == n
                && s.textureStart != null && s.textureStart.Length == KH_FurColor.DogCount && s.textureCount != null && s.textureCount.Length == KH_FurColor.DogCount
                && s.textures != null && s.nameKeys != null && s.nameKeys.Length == s.textures.Length;
            if (!v.Check(shapes, "array shapes (targets " + n + ", 6 blocks, textures = name keys)")) return;
            var defs = KD_DogDefs.All;
            var fade = KD_ProductsEditor.SceneComponents<KD_DistanceFadeSettings>().FirstOrDefault();
            for (int d = 0; d < KH_FurColor.DogCount && d < defs.Length; d++)
            {
                var dog = KD_SceneObjects.Find(defs[d].DogName);
                int from = s.textureStart[d], count = s.textureCount[d];
                bool block = count >= 1 && from >= 0 && from + count <= s.textures.Length && (d == 0 ? from == 0 : from == s.textureStart[d - 1] + s.textureCount[d - 1]);
                if (!v.Check(block, defs[d].key + ": texture block [" + from + ", " + (from + count) + ") contiguous")) continue;
                v.Check(s.nameKeys[from] == KH_FurColor.DefaultKey && s.textures[from] != null, defs[d].key + ": first entry is the original color (" + KH_FurColor.DefaultKey + ")");
                var mine = Enumerable.Range(0, n).Where(i => s.targetDogs[i] == d).ToArray();
                v.Check(mine.Length >= 1, defs[d].key + ": " + mine.Length + " fur target slot(s)");
                foreach (int i in mine)
                {
                    var r = s.targetRenderers[i]; int slot = s.targetSlots[i];
                    var mats = r != null ? r.sharedMaterials : new Material[0];
                    var m = slot >= 0 && slot < mats.Length ? mats[slot] : null;
                    v.Check(r != null && dog != null && r.transform.IsChildOf(dog.transform) && ExcludedPart(r.transform, dog.transform) == null, defs[d].key + " target " + i + " is a body renderer of " + defs[d].DogName);
                    v.Check(m != null && IsFurShader(m.shader), defs[d].key + " target " + i + " slot " + slot + " is a fur material (" + (m != null ? m.name : "null") + ")");
                    v.Check(m != null && m.GetTexture("_MainTex") == s.textures[from], defs[d].key + " target " + i + " original _MainTex = the recorded original (" + (m != null ? Describe(m.GetTexture("_MainTex") as Texture2D) : "-") + ")");
                }
                if (fade != null && fade.targetRenderers != null && fade.targetSlots != null)
                {
                    bool covered = true;
                    for (int k = 0; k < fade.targetRenderers.Length && k < fade.targetSlots.Length; k++)
                        if (fade.targetRenderers[k] != null && dog != null && fade.targetRenderers[k].transform.IsChildOf(dog.transform))
                            covered &= mine.Any(i => s.targetRenderers[i] == fade.targetRenderers[k] && s.targetSlots[i] == fade.targetSlots[k]);
                    v.Check(covered, defs[d].key + ": the distance-fade slot is a fur target too (both write the same per-slot block)");
                }
                for (int k = from + 1; k < from + count; k++)
                {
                    v.Check(s.textures[k] != null && s.textures[k] != s.textures[from], defs[d].key + " option " + (k - from) + " " + s.nameKeys[k] + " is a different texture");
                    v.Check(ColorKeys.Contains(s.nameKeys[k]), defs[d].key + " option " + (k - from) + " key " + s.nameKeys[k] + " is a known color name");
                }
                v.Note(defs[d].key + ": " + count + " color(s) — " + string.Join(" / ", Enumerable.Range(from, count).Select(k => KD_LocalizationEditor.Text(catalog, "ja", s.nameKeys[k]))));
            }
            foreach (var key in ColorKeys.Concat(new[] { KH_FurColor.DefaultKey }).Distinct())
            {
                var missing = catalog.languages.Where(l => !l.texts.Any(t => t.key == key && !string.IsNullOrEmpty(t.text))).Select(l => l.code).ToArray();
                v.Check(missing.Length == 0, "text " + key + " in all languages" + (missing.Length == 0 ? "" : " (missing: " + string.Join(",", missing) + ")"));
            }
            foreach (var lang in catalog.languages.Where(KD_LocalizationEditor.Bundled))
                foreach (var e in lang.texts.Where(t => t.key.StartsWith("furcolor.")))
                    v.Check(font != null && e.text.Where(KD_LocalizationEditor.Drawn).All(c => KD_LocalizationEditor.HasGlyph(font, c)), "glyphs " + lang.code + " " + e.key);
        }
    }
}
