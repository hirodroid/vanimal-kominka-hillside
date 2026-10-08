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
    /// 2. 犬ごとの色の一覧 = 下の表 <see cref="Table"/> の行の順 (2026-10-08 ユーザー決定: 名前と順は入江 A_Cove_to_Unwind の毛色の一覧に合わせる)。
    ///    先頭 (index 0) は今の色 (今の _MainTex) で、名前も入江のもの (コーギーなら「茶白」)。ほかの行は持ち込み置き場 <see cref="DropInDir"/>/&lt;key&gt;/&lt;色&gt;.png
    ///    (古民家のパックのテクスチャのコピー) か、この repo の購入パックの材質。**見つからない行は WARN** (Verify は表の全行が配線されていることを求める)。
    ///    置き場にあって表に無いファイルも WARN (配線しない)。続けて色名の ja / en の字形を共通フォントへ足し、KD_Language の表を作り直す (Configure)。
    /// 3. 各テクスチャの平均色を 16×16 へ縮めて測り、ログと Verify の NOTE に出す (色名は入江のもの。見た目と合うかはユーザーが Play で見る)。
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

        /// <summary>犬種 → 購入パックのフォルダ (材質を探す範囲)。</summary>
        static readonly Dictionary<string, string> BreedFolder = new Dictionary<string, string>
        {
            { "Corgi", "コーギー" }, { "Pome", "もちぽめ" }, { "Lab", "ラブラドール" }, { "Dachs", "ダックスフンド" }, { "Shiba", "柴犬_remake" }, { "Samoyed", "サモエド" },
        };

        /// <summary>候補の 1 行。material = 購入パックの材質名 (拡張子なし。その _MainTex を使う)、texture = <see cref="DropInDir"/> からの相対パス。
        /// どちらも null の行は「今の色」(その犬の距離フェードのスロットの _MainTex) で、犬ごとの先頭 (index 0) に 1 行だけ置く。</summary>
        public struct Candidate
        {
            public string dog, material, texture, nameKey, note;
            public Candidate(string dog, string material, string texture, string nameKey, string note)
            { this.dog = dog; this.material = material; this.texture = texture; this.nameKey = nameKey; this.note = note; }
            public bool Original { get { return string.IsNullOrEmpty(material) && string.IsNullOrEmpty(texture); } }
        }

        /// <summary>
        /// 色の表 (2026-10-08 ユーザー決定: 色名は入江と合わせる / 足りない色は古民家のパックから持ってくる)。犬ごとの行の順 = 板の ◀ ▶ の順 (index 0 = 今の色)。
        /// 名前と順は入江の毛色の一覧 (catalog) のとおり。テクスチャは古民家の購入パックの ke 2〜4 などを統括が FurColors/&lt;犬&gt;/&lt;色&gt;.png へコピーしたもの
        /// (購入テクスチャの派生物。加工なし・インポート設定はパックのまま)。柴 1 とサモエド 1 だけはこの repo のパックの材質をそのまま使う。
        /// </summary>
        public static readonly Candidate[] Table =
        {
            new Candidate("Corgi", null, null, "furcolor.tanWhite", "今の色 (コーギー/マテリアル_Base_color.png)"),
            new Candidate("Corgi", null, "Corgi/lightWhite.png", "furcolor.lightWhite", "古民家 ke 1"),
            new Candidate("Corgi", null, "Corgi/darkWhite.png", "furcolor.darkWhite", "古民家 ke 2"),
            new Candidate("Corgi", null, "Corgi/tricolor.png", "furcolor.tricolor", "古民家 ke 3"),
            new Candidate("Corgi", null, "Corgi/mostWhite.png", "furcolor.mostWhite", "古民家 ke 4"),
            new Candidate("Pome", null, null, "furcolor.white", "今の色"),
            new Candidate("Pome", null, "Pome/black.png", "furcolor.black", "古民家 ke 2"),
            new Candidate("Pome", null, "Pome/cream.png", "furcolor.cream", "古民家 ke 3"),
            new Candidate("Pome", null, "Pome/brown.png", "furcolor.brown", "古民家 ke 4"),
            new Candidate("Lab", null, null, "furcolor.brown", "今の色 (入江は今のイエローを「茶」と呼ぶ)"),
            new Candidate("Lab", null, "Lab/white.png", "furcolor.white", "古民家 ke 2"),
            new Candidate("Lab", null, "Lab/black.png", "furcolor.black", "古民家 ke 3"),
            new Candidate("Dachs", null, null, "furcolor.brown", "今の色"),
            new Candidate("Dachs", null, "Dachs/light.png", "furcolor.light", "古民家 ke 2"),
            new Candidate("Dachs", null, "Dachs/blackTan.png", "furcolor.blackTan", "古民家 ke 3"),
            new Candidate("Shiba", null, null, "furcolor.red", "今の色 (マテリアル_Base_color 2.png)"),
            new Candidate("Shiba", "ke", null, "furcolor.light", "この repo のパック 柴犬_remake/material/ke.mat (マテリアル_Base_color.png)"),
            new Candidate("Shiba", null, "Shiba/dark.png", "furcolor.dark", "古民家 ke 2"),
            new Candidate("Shiba", null, "Shiba/white.png", "furcolor.white", "古民家 ke 3"),
            new Candidate("Samoyed", null, null, "furcolor.white", "今の色"),
            new Candidate("Samoyed", "サモエド毛", null, "furcolor.black", "この repo のパック サモエド/mat/サモエド毛.mat (DefaultMaterial.001_Base_Color 2.png)"),
        };

        /// <summary>色名の翻訳キー (翻訳表の 13 言語に置く)。<see cref="Table"/> が使うものだけ (入江 A_Cove_to_Unwind の coat.* と同じ名前・同じ訳)。</summary>
        public static readonly string[] ColorKeys = Table.Select(c => c.nameKey).Distinct().ToArray();

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
                var rows = RowsOf(def.key);
                if (rows.Length == 0 || !rows[0].Original) throw new InvalidOperationException(def.key + ": the table must start with the original color row (material = texture = null)");
                textures.Add(baseTex); keys.Add(rows[0].nameKey);
                log.AppendLine("   option 0 " + rows[0].nameKey + " (" + KD_LocalizationEditor.Text(catalog, "ja", rows[0].nameKey) + ") = the current texture");
                for (int r = 1; r < rows.Length; r++)
                {
                    string from;
                    var tex = Resolve(rows[r], out from);
                    var key = rows[r].nameKey;
                    if (tex == null) { log.AppendLine("   WARN missing " + key + " — " + from + " (" + rows[r].note + ")"); warn++; continue; }
                    bool sameTex = false, sameKey = false;
                    for (int i = start[d]; i < textures.Count; i++) { if (textures[i] == tex) sameTex = true; if (keys[i] == key) sameKey = true; }
                    if (sameTex) { log.AppendLine("   WARN option " + key + " " + Describe(tex) + " is already in the list (same texture) — skipped"); warn++; continue; }
                    if (sameKey) { log.AppendLine("   WARN option " + key + " " + Describe(tex) + " duplicates a color name — skipped (fix the table)"); warn++; continue; }
                    textures.Add(tex); keys.Add(key);
                    log.AppendLine("   option " + (textures.Count - start[d] - 1) + " " + key + " (" + KD_LocalizationEditor.Text(catalog, "ja", key) + ") from " + from + " " + Describe(tex) + " (" + rows[r].note + ")");
                }
                counts[d] = textures.Count - start[d];
                foreach (var stray in UnlistedDropIns(def.key)) { log.AppendLine("   WARN drop-in " + stray + " is not in the table (not wired)"); warn++; }
                if (counts[d] <= 1) log.AppendLine("   NOTE no other colors for " + def.key + " (行は「ほかの毛色がありません」になる)");
            }
            // 色名の字形 (ja / en は共通フォントに焼く) と KD_Language の表 (新しいキーを実行時に引けるように)。DogBook/1 Setup と同じ 2 つ
            var language = KD_ProductsEditor.SceneComponents<KD_Language>();
            if (language.Length != 1) throw new InvalidOperationException("KD_Language must exist once (" + language.Length + ")");
            KD_ShopSetupEditor.AddGlyphs(string.Concat(catalog.languages.Where(KD_LocalizationEditor.Bundled).SelectMany(l => l.texts.Where(t => t.key.StartsWith("furcolor.")).Select(t => t.text))), log);
            KD_LocalizationEditor.Configure(language[0], catalog, log);
            KD_ProductsEditor.Copy(language[0]);

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

        /// <summary>表のうち、この犬の行 (並び = 板の順)。</summary>
        public static Candidate[] RowsOf(string dogKey) { return Table.Where(x => x.dog == dogKey).ToArray(); }

        /// <summary>表の 1 行のテクスチャ (今の色の行は null)。material = 購入パックの犬種フォルダの材質の _MainTex、texture = <see cref="DropInDir"/> からの相対パス。
        /// from に探した場所を返す (見つからなくても)。</summary>
        public static Texture2D Resolve(Candidate c, out string from)
        {
            from = null;
            if (c.Original) return null;
            if (!string.IsNullOrEmpty(c.material))
            {
                string folder = PackRoot + BreedFolder[c.dog];
                from = "material \"" + c.material + "\" in " + folder;
                var mat = AssetDatabase.FindAssets("t:Material", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => Path.GetFileNameWithoutExtension(p) == c.material).Select(AssetDatabase.LoadAssetAtPath<Material>).FirstOrDefault();
                if (mat == null) return null;
                from = AssetDatabase.GetAssetPath(mat);
                return mat.GetTexture("_MainTex") as Texture2D;
            }
            from = DropInDir + "/" + c.texture;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(from);
        }

        /// <summary>持ち込み置き場 &lt;DropInDir&gt;/&lt;犬&gt;/ にあって表に無いテクスチャ (配線しない。WARN で知らせる)。</summary>
        static IEnumerable<string> UnlistedDropIns(string dogKey)
        {
            string drop = DropInDir + "/" + dogKey;
            if (!AssetDatabase.IsValidFolder(drop)) return new string[0];
            var listed = new HashSet<string>(RowsOf(dogKey).Where(c => !string.IsNullOrEmpty(c.texture)).Select(c => DropInDir + "/" + c.texture));
            return AssetDatabase.FindAssets("t:Texture2D", new[] { drop }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !listed.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
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
                var rows = RowsOf(defs[d].key);
                v.Check(count == rows.Length, defs[d].key + ": " + count + " colors = the table's " + rows.Length + " rows (every row wired, no WARN)");
                v.Check(rows.Length > 0 && rows[0].Original && s.nameKeys[from] == rows[0].nameKey && s.textures[from] != null, defs[d].key + ": first entry is the current color named " + (rows.Length > 0 ? rows[0].nameKey : "-"));
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
                    int idx = k - from;
                    v.Check(s.textures[k] != null && s.textures[k] != s.textures[from], defs[d].key + " option " + idx + " " + s.nameKeys[k] + " is a different texture");
                    string where = "-";
                    var want = idx < rows.Length ? Resolve(rows[idx], out where) : null;
                    v.Check(idx < rows.Length && s.nameKeys[k] == rows[idx].nameKey && want != null && s.textures[k] == want,
                        defs[d].key + " option " + idx + " = table row " + idx + " (" + (idx < rows.Length ? rows[idx].nameKey : "none") + " from " + where + ")");
                    v.Note(defs[d].key + " option " + idx + " " + KD_LocalizationEditor.Text(catalog, "ja", s.nameKeys[k]) + ": " + Describe(s.textures[k]));
                }
                v.Note(defs[d].key + ": " + count + " color(s) — " + string.Join(" / ", Enumerable.Range(from, count).Select(k => KD_LocalizationEditor.Text(catalog, "ja", s.nameKeys[k]))));
            }
            v.Check(s.textures.Length == Table.Length && Table.Length == 21, "21 colors in all (" + s.textures.Length + " wired, table " + Table.Length + "; Corgi 5 / Pome 4 / Lab 3 / Dachs 3 / Shiba 4 / Samoyed 2)");
            v.Check(ColorKeys.Length == 13, "13 color names (" + ColorKeys.Length + ", the Cove's coat names)");
            // 翻訳表の furcolor.* = 行の文言 (KD_DogBookEditor.FurUiKeys) + 色名だけ (使わない色名・もとの色 furcolor.default を残さない)
            var expected = new HashSet<string>(ColorKeys.Concat(KD_DogBookEditor.FurUiKeys));
            var stray = catalog.languages.SelectMany(l => l.texts).Select(t => t.key).Where(k => k.StartsWith("furcolor.") && !expected.Contains(k)).Distinct().ToArray();
            v.Check(stray.Length == 0, "no unused furcolor.* keys in the catalog" + (stray.Length == 0 ? "" : " (" + string.Join(", ", stray) + ")"));
            var lang0 = KD_ProductsEditor.SceneComponents<KD_Language>().FirstOrDefault();
            v.Check(lang0 != null && lang0.keys != null && ColorKeys.All(k => lang0.keys.Contains(k)), "KD_Language has the color names (DogBook/5 or 1 ran Configure)");
            foreach (var key in ColorKeys)
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
