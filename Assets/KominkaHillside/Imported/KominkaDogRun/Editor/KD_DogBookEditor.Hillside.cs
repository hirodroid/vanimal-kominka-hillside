using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using KominkaHillside;
using KominkaHillside.Editor;

namespace KominkaDogRun.Editor
{
    /// <summary>
    /// 裏山改変 (DES-DOG-009 2026-10-08): 犬紹介・呼び名・お気に入りの板 (古民家 IMP-DOG-033) を裏山へ入れ、詳細ページに毛色の行を足す入口。
    /// 設計: docs/plans/2026-10-08-dog-book-and-fur-color.md
    ///
    /// 実行順 (前提 → 出力)。**シーンは保存しない**:
    ///   DogBook/0 Create Program Assets (KD_DogBook・KD_PlayerDogTag・KH_FurColor。新規の Asset はコンパイルを待ってから次へ)
    ///   → DogBook/4 Bake Portraits (Textures/DogBook/&lt;key&gt;.png。KD_TreatPouch.accPreviewDogs を撮る。古民家の本体をそのまま呼ぶ)
    ///   → DogBook/1 Setup (板と訪問タグ。初回だけ門前のおじいさんの左に置く。以後は人の位置を動かさない)
    ///   → DogBook/5 Build Fur Colors (KH_FurColor の配線。KH_FurColorEditor)
    ///   → DogBook/3 Verify (古民家の検査 + 置き場所 + 裏山の追加。Logs/dog-book-verify.txt)
    ///   DogBook/2 Layout Near Keeper は初期位置へ明示的に戻すときだけ (人が動かした位置を上書きする)。
    ///
    /// 古民家の入口 (Menu/Imported-Unused/Dog Book/0〜4) との違い:
    /// ①板 KD_DogBook と訪問タグ KD_PlayerDogTag は KH_Dogs 直下 (KD_Places / KD_Systems は裏山に無い)。板は古民家の Setup を先に置いた器へ組む
    ///   (古民家の Setup は既存の器の親・位置を触らない)。
    /// ②見出しは dogbook.title.hillside (「裏山の犬たち」)。古民家の dogbook.title (「庭の犬たち」) は残す。
    /// ③詳細ページの下段 ([閉じる] の左) に毛色の行: ◀ 色名 ▶ + 注記。呼び名の [保存]・お気に入りの段とは離す (個人設定と共有の毛色を同じボタンで確定しない)。
    /// ④保存キーは古民家と同じ KD_DogBook_v1 のまま (PlayerData はワールドごとなので値は古民家と共有されない見込み。キー名を変えるかはユーザーの決定待ち。設計書 §2)。
    /// </summary>
    public static partial class KD_DogBookEditor
    {
        const string HillsideMenu = "Tools/KominkaHillside/DogBook/";
        const string HillsideSetupLog = KD_Hillside.LogDir + "dog-book-hillside-setup.txt";
        public const string HillsideTitleKey = "dogbook.title.hillside";

        // ---- 初期位置 (初回の Setup と DogBook/2 だけが書く。最終位置はユーザーが現物で決める) ----
        // おじいさん KD_ShopKeeper (49.90, 42.80, 向き 235°) の左前 1.58 m、写真ボードの手前。写真ボード (中心) 1.66 m で、その読む場所 (表の前 0.3〜2.0 m・幅 2.32 m) の外、
        // 看板 A 3.7 m・スポーン 3.5 m・犬の自由行動の中心 (KH_Roam_*) 5.3 m。2026-10-08 に条件を満たす点を格子で探した中でおじいさんに最も近い点 (設計書 §3)。
        // スポーンからは写真ボードの左側に少し重なる (視線から 0.12 m)。ほかの候補と得失は設計書 §3
        public static readonly Vector2 HillsideBoardXZ = new Vector2(50.8f, 41.5f);
        /// <summary>地面から板の中心までの高さ (古民家の Layout Wall の 1.25 m と同じ。板の縦 0.71 m → 下端は地面 + 0.9 m)。</summary>
        public const float HillsideBoardHeight = 1.25f;
        /// <summary>表 (-Z 側) を向ける相手。無ければ <see cref="HillsideFallbackYaw"/>。</summary>
        const string FaceTowardName = "KH_Entrance";
        const float HillsideFallbackYaw = 66f;

        // ---- Verify の距離の基準 (m、水平) ----
        const float KeeperMin = 1.5f, KeeperMax = 3.0f, OtherBoardMin = 1.5f, SpawnMin = 3.0f, RoamMin = 3.3f;
        static readonly string[] NearbyBoards = { "KD_ShopKeeperMenu", "KH_PhotoBoard", "KD_WelcomeSpot", "KH_LeashRack", "KH_WashStation" };

        // ---- 毛色の行 (詳細ページ Detail/Visual の座標。板は 1050×710、[閉じる] は (350, -309) 275×62) ----
        const float FurRowY = -309f;
        static readonly Vector2 FurPrevPos = new Vector2(-485f, FurRowY), FurNextPos = new Vector2(-255f, FurRowY), FurArrowSize = new Vector2(64f, 58f);
        static readonly Vector2 FurValuePos = new Vector2(-370f, FurRowY), FurValueSize = new Vector2(160f, 58f);
        static readonly Vector2 FurNotePos = new Vector2(-15f, FurRowY), FurNoteSize = new Vector2(400f, 62f);
        const string FurPrevName = "FurPrev", FurNextName = "FurNext", FurValueName = "FurValue", FurNoteName = "FurNote";
        /// <summary>KD_DogBook.RefreshFur が引く文言 (翻訳表の 13 言語に置く)。色名は KH_FurColorEditor.ColorKeys。</summary>
        public static readonly string[] FurUiKeys = { "furcolor.label", "furcolor.master.only", "furcolor.none", KH_FurColor.DefaultKey };
        /// <summary>裏山で足した状態の文言 (KD_DogBook.StatusKey)。</summary>
        public static readonly string[] HillsideStateKeys = { "dogbook.state.walk", "dogbook.state.water", "dogbook.state.wash" };

        [MenuItem(HillsideMenu + "0 - Create Program Assets")]
        public static void HillsidePrograms()
        {
            KD_Hillside.Guard();
            Programs();   // KD_DogBook / KD_PlayerDogTag (古民家の本体。既にあれば作らない)
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(KH_FurColorEditor.ProgramPath) == null)
            {
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(KH_FurColorEditor.ScriptPath);
                if (asset.sourceCsScript == null) throw new InvalidOperationException(KH_FurColorEditor.ScriptPath);
                AssetDatabase.CreateAsset(asset, KH_FurColorEditor.ProgramPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[DogBook] created " + KH_FurColorEditor.ProgramPath + " — U# のコンパイルを待ってから DogBook/4・1・5 を流す");
            }
        }

        [MenuItem(HillsideMenu + "1 - Setup")]
        public static void HillsideSetup()
        {
            KD_Hillside.Guard();
            var log = new StringBuilder("[DogBook] Setup (裏山、DES-DOG-009)\n");
            foreach (var path in new[] { "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_DogBook.asset", "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/KD_PlayerDogTag.asset", KH_FurColorEditor.ProgramPath })
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path) == null) throw new InvalidOperationException("Program Asset missing: " + path + " (先に DogBook/0 → コンパイル)");
            var dogs = DogsRoot();
            int count;
            var host = KD_SceneObjects.Find(BoardName, out count);
            if (count > 1) throw new InvalidOperationException("Duplicate " + BoardName);
            if (host == null)
            {
                host = new GameObject(BoardName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(host, "Create " + BoardName);
                Undo.SetTransformParent(host.transform, dogs, "Place " + BoardName);
                PlaceNearKeeper(host.transform, log);
            }
            else
            {
                log.AppendLine("reused " + KD_SceneObjects.PathOf(host) + " at " + host.transform.position.ToString("F2") + " (位置・向きは動かさない)");
                if (host.transform.parent != dogs) log.AppendLine("WARN " + BoardName + " が " + KD_Hillside.DogsRootName + " 直下にない (Verify が FAIL にする)");
            }
            var tagGo = KD_SceneObjects.Find(TagName, out count);
            if (count > 1) throw new InvalidOperationException("Duplicate " + TagName);
            if (tagGo == null)
            {
                tagGo = new GameObject(TagName);
                Undo.RegisterCreatedObjectUndo(tagGo, "Create " + TagName);
                Undo.SetTransformParent(tagGo.transform, dogs, "Place " + TagName);
                log.AppendLine("created " + KD_Hillside.DogsRootName + "/" + TagName + " (VRCPlayerObject の雛形。古民家の Setup が中身を組む)");
            }

            Setup();   // 古民家の本体: 字形、索引 6 枚・詳細・説明の頁、訪問タグ、6 匹の dogBook の配線、文言表の Configure (Logs/dog-book-setup.txt)

            var book = host.GetComponent<KD_DogBook>();
            if (book == null) throw new InvalidOperationException("KD_DogBook was not built");
            BuildHillsideParts(book, log);
            EditorSceneManager.MarkSceneDirty(host.scene);
            Directory.CreateDirectory(Path.GetDirectoryName(HillsideSetupLog));
            File.WriteAllText(HillsideSetupLog, log.ToString());
            Debug.Log(log.ToString());
        }

        [MenuItem(HillsideMenu + "2 - Layout Near Keeper (初期位置へ戻す)")]
        public static void HillsideLayout()
        {
            KD_Hillside.Guard();
            var root = KD_SceneObjects.Find(BoardName);
            if (root == null) throw new InvalidOperationException("Setup first (DogBook/1)");
            var log = new StringBuilder();
            Undo.RecordObject(root.transform, "Place dog introduction board");
            PlaceNearKeeper(root.transform, log);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[DogBook] Layout: " + log);
        }

        [MenuItem(HillsideMenu + "3 - Verify")]
        public static void HillsideVerify() { Verify(); }

        [MenuItem(HillsideMenu + "4 - Bake Portraits")]
        public static void HillsidePortraits() { KD_Hillside.Guard(); Portraits(); }

        static Transform DogsRoot()
        {
            int count;
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName, out count);
            if (count != 1 || dogs == null || dogs.transform.parent != null) throw new InvalidOperationException(KD_Hillside.DogsRootName + " must exist once at the scene root");
            return dogs.transform;
        }

        static void PlaceNearKeeper(Transform board, StringBuilder log)
        {
            string how;
            float ground = GroundAt(HillsideBoardXZ.x, HillsideBoardXZ.y, out how);
            var pos = new Vector3(HillsideBoardXZ.x, ground + HillsideBoardHeight, HillsideBoardXZ.y);
            var spawn = KD_SceneObjects.Find(FaceTowardName);
            Quaternion rot;
            if (spawn != null)
            {
                // 表は -Z 側: LookRotation(板 - 読む人) (vrc-world-ui §2)
                Vector3 away = pos - spawn.transform.position; away.y = 0f;
                rot = away.sqrMagnitude > .01f ? Quaternion.LookRotation(away.normalized, Vector3.up) : Quaternion.Euler(0f, HillsideFallbackYaw, 0f);
            }
            else rot = Quaternion.Euler(0f, HillsideFallbackYaw, 0f);
            board.SetPositionAndRotation(pos, rot);
            board.localScale = Vector3.one * .001f;
            log.AppendLine("placed " + BoardName + " at " + pos.ToString("F2") + " yaw " + rot.eulerAngles.y.ToString("F1") + " (地面: " + how + "、表は "
                + (spawn != null ? FaceTowardName : "既定の向き") + " へ)。以後の Setup は動かさない");
        }

        /// <summary>(x, z) の地面。TerrainCollider を優先し、無ければ KH_Dogs の外の最も高い非トリガー面 (KD_ShopKeeperSetupEditor.GroundY と同じ規則)。</summary>
        static float GroundAt(float x, float z, out string how)
        {
            Physics.SyncTransforms();
            Transform dogs = null;
            try { dogs = DogsRoot(); } catch (InvalidOperationException) { }
            RaycastHit best = default(RaycastHit);
            bool have = false, terrain = false;
            foreach (var h in Physics.RaycastAll(new Vector3(x, 500f, z), Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || (dogs != null && h.collider.transform.IsChildOf(dogs))) continue;
                bool isTerrain = h.collider is TerrainCollider;
                if (!have || (isTerrain && !terrain) || (isTerrain == terrain && h.point.y > best.point.y)) { best = h; have = true; terrain = isTerrain; }
            }
            if (!have) { how = "WARN レイが何にも当たらない — y=1.60 (スポーンの地面) で仮置き"; return 1.6f; }
            how = KD_SceneObjects.PathOf(best.collider.gameObject) + (terrain ? " (Terrain)" : "") + " y=" + best.point.y.ToString("F2");
            return best.point.y;
        }

        /// <summary>古民家の Setup の後に足す裏山の部品。何度流しても同じ形になる (EnsureUiChild / Button が名前で引き直す)。</summary>
        static void BuildHillsideParts(KD_DogBook book, StringBuilder log)
        {
            catalog = KD_LocalizationEditor.Load();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (font == null) throw new InvalidOperationException("Shared UI font missing: " + KD_ShopSetupEditor.SharedFontPath);

            // ② 見出し
            int heading = Array.FindIndex(book.staticLabels, t => t != null && t.name == "Heading");
            if (heading < 0) throw new InvalidOperationException("Index heading label not found");
            book.staticKeys[heading] = HillsideTitleKey;
            book.staticLabels[heading].text = KD_LocalizationEditor.Text(catalog, "ja", HillsideTitleKey);
            log.AppendLine("heading key " + HillsideTitleKey + " (" + book.staticLabels[heading].text + ")");

            // ③ 毛色の行
            var visual = book.detail.transform.Find("Visual");
            if (visual == null) throw new InvalidOperationException("Detail/Visual missing");
            var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(book);
            var prev = Button(visual, FurPrevName, FurPrevPos, FurArrowSize, null, font, udon, "_FurPrev");
            var next = Button(visual, FurNextName, FurNextPos, FurArrowSize, null, font, udon, "_FurNext");
            Arrow(prev, "◀", font); Arrow(next, "▶", font);
            var value = Label(visual, FurValueName, FurValuePos, FurValueSize, null, font, 26);
            value.text = KD_LocalizationEditor.Text(catalog, "ja", KH_FurColor.DefaultKey);
            AutoSize(value, 16f, 26f, false);
            var note = Label(visual, FurNoteName, FurNotePos, FurNoteSize, null, font, 20);
            note.text = KD_LocalizationEditor.Text(catalog, "ja", "furcolor.label");
            note.alignment = TextAlignmentOptions.MidlineLeft;
            AutoSize(note, 14f, 20f, true);
            book.furValue = value; book.furNote = note;
            book.furEditParts = new[] { prev.targetGraphic.gameObject, prev.gameObject, next.targetGraphic.gameObject, next.gameObject };
            book.furParts = book.furEditParts.Concat(new[] { book.furValue.gameObject, book.furNote.gameObject }).ToArray();

            var systems = KD_ProductsEditor.SceneComponents<KH_FurColor>();
            book.furColor = systems.Length == 1 ? systems[0] : null;
            log.AppendLine(systems.Length == 1 ? "furColor wired: " + KD_SceneObjects.PathOf(systems[0].gameObject)
                : "WARN KH_FurColor が " + systems.Length + " 個 — 毛色の行は実行時に隠れる (DogBook/5 Build Fur Colors を流す)");
            KD_ProductsEditor.Copy(book);
            log.AppendLine("fur row: ◀ " + FurPrevPos + " / value " + FurValuePos + " / ▶ " + FurNextPos + " / note " + FurNotePos + " (呼び名の [保存]・お気に入りは y=-147、伝言は y=-228)");
        }

        static void Arrow(Button button, string glyph, TMP_FontAsset font)
        {
            var t = Label(button.targetGraphic.transform, "Label", Vector2.zero, FurArrowSize - new Vector2(8f, 4f), null, font, 30);
            t.text = glyph;   // 矢印は翻訳しない (◀ ▶ は共通フォントにある。KD_SleepPlacement と写真の頁送りと同じ字)
        }

        static void AutoSize(TextMeshProUGUI t, float min, float max, bool wrap)
        {
            t.enableAutoSizing = true; t.fontSizeMin = min; t.fontSizeMax = max; t.fontSize = max;
            t.enableWordWrapping = wrap; t.overflowMode = TextOverflowModes.Truncate;
        }

        // ================================================================ Verify (古民家の Verify の差し込み口)

        /// <summary>裏山の Setup で組んだ板か (KH_Dogs 直下、または見出しが裏山のキー)。違えば共通の Verify は古民家の検査だけをする
        /// (古民家の Setup の板 = KD_Places の下・見出し dogbook.title・毛色の行なし を裏山の検査で FAIL にしない)。</summary>
        static partial void ClassifyBoard(KD_DogBook b, ref bool hillside)
        {
            var p = b.transform.parent;
            bool underDogs = p != null && p.name == KD_Hillside.DogsRootName && p.parent == null;
            int heading = b.staticLabels == null ? -1 : Array.FindIndex(b.staticLabels, t => t != null && t.name == "Heading");
            bool title = heading >= 0 && b.staticKeys != null && heading < b.staticKeys.Length && b.staticKeys[heading] == HillsideTitleKey;
            hillside = underDogs || title;
        }

        static partial void VerifyPlacement(KD_VerifyLog v, KD_DogBook b)
        {
            v.Info("-- hillside board (DES-DOG-009): placement --");
            v.Check(b.transform.parent != null && b.transform.parent.name == KD_Hillside.DogsRootName && b.transform.parent.parent == null, "board directly under " + KD_Hillside.DogsRootName + " (裏山。古民家は KD_Places)");
            var p = b.transform.position;
            var keeper = KD_SceneObjects.Find("KD_ShopKeeper");
            float dk = keeper != null ? Flat(p, keeper.transform.position) : -1f;
            v.Check(keeper != null && dk >= KeeperMin && dk <= KeeperMax, "board near the keeper: " + dk.ToString("F2") + " m (" + KeeperMin + "〜" + KeeperMax + ")");
            foreach (var name in NearbyBoards)
            {
                var go = KD_SceneObjects.Find(name);
                if (go == null) { v.Note(name + " not in the scene (distance not checked)"); continue; }
                float d = Flat(p, go.transform.position);
                v.Check(d >= OtherBoardMin, "board apart from " + name + ": " + d.ToString("F2") + " m (>= " + OtherBoardMin + ")");
            }
            var photo = KD_SceneObjects.Find("KH_PhotoBoard");
            if (photo != null)
            {
                // 写真ボードを読む場所 (表 = -Z の前 0.3〜2.0 m、幅 = 板の背板) に、この板の幅 (1.05 m) のどこも入らない (KH_PhotoGalleryEditor の BoardReaders と同じ形)
                float half = KH_PhotoGalleryEditor.BoardCanvasMm.x * .001f * .5f + .04f;
                var inv = Quaternion.Inverse(Quaternion.Euler(0f, photo.transform.eulerAngles.y, 0f));
                bool inside = false;
                for (int k = -3; k <= 3; k++)
                {
                    var q = inv * (b.transform.TransformPoint(new Vector3(k * 175f, 0f, 0f)) - photo.transform.position);
                    if (Mathf.Abs(q.x) < half && q.z > -2.0f && q.z < -.3f) inside = true;
                }
                v.Check(!inside, "board outside the photo board's reading area (front 0.3〜2.0 m, half width " + half.ToString("F2") + " m)");
            }
            var guides = KD_SceneObjects.Find("KH_GuideBoards");
            if (guides != null)
                foreach (Transform g in guides.transform)
                {
                    float d = Flat(p, g.position);
                    v.Check(d >= OtherBoardMin, "board apart from guide board " + g.name + ": " + d.ToString("F2") + " m (>= " + OtherBoardMin + ")");
                }
            var spawn = KD_SceneObjects.Find(FaceTowardName);
            if (spawn != null)
            {
                float ds = Flat(p, spawn.transform.position);
                v.Check(ds >= SpawnMin, "board apart from the spawn " + FaceTowardName + ": " + ds.ToString("F2") + " m (>= " + SpawnMin + ")");
                Vector3 away = p - spawn.transform.position; away.y = 0f;
                Vector3 fwd = b.transform.forward; fwd.y = 0f;
                float angle = Vector3.Angle(fwd, away);
                v.Check(angle <= 60f, "front (-Z) readable from the spawn side: " + angle.ToString("F0") + "° (<= 60)");
            }
            else v.Note(FaceTowardName + " not found (facing not checked)");
            if (spawn != null)
                foreach (var name in new[] { "KH_PhotoBoard", "Asobikata" })
                {
                    var target = KD_SceneObjects.Find(name);
                    if (target == null) continue;
                    float clear = float.MaxValue;
                    for (int k = -3; k <= 3; k++) clear = Mathf.Min(clear, FlatSegment(b.transform.TransformPoint(new Vector3(k * 175f, 0f, 0f)), spawn.transform.position, target.transform.position));
                    v.Note("spawn → " + name + " sight line: board edge " + clear.ToString("F2") + " m from it (0 = the board hides its centre from the spawn)");
                }
            var dogs = KD_SceneObjects.Find(KD_Hillside.DogsRootName);
            if (dogs != null)
                foreach (Transform t in dogs.transform)
                    if (t.name.StartsWith("KH_Roam_"))
                    {
                        float d = Flat(p, t.position);
                        v.Check(d >= RoamMin, "board outside the free-roam circle " + t.name + ": " + d.ToString("F2") + " m (>= " + RoamMin + ")");
                    }
            string how;
            float ground = GroundAt(p.x, p.z, out how);
            v.Check(p.y - ground >= 1.0f && p.y - ground <= 1.6f, "board centre " + (p.y - ground).ToString("F2") + " m above the ground (1.0〜1.6, " + how + ")");
            v.Note("位置 " + p.ToString("F2") + " yaw " + b.transform.eulerAngles.y.ToString("F1") + " — 最終位置はユーザーが現物で決める (設計書 §3)");
        }

        static partial void VerifyHillside(KD_VerifyLog v, KD_DogBook b)
        {
            int heading = b.staticLabels == null ? -1 : Array.FindIndex(b.staticLabels, t => t != null && t.name == "Heading");
            v.Check(heading >= 0 && b.staticKeys[heading] == HillsideTitleKey, "heading uses " + HillsideTitleKey);
            v.Check(KD_DogBook.SaveKey == "KD_DogBook_v1", "PlayerData key KD_DogBook_v1 (キー名は古民家と同じ。値はワールドごと。変えるかはユーザー決定待ち)");
            var cat = KD_LocalizationEditor.Load();
            foreach (var key in new[] { HillsideTitleKey }.Concat(HillsideStateKeys).Concat(FurUiKeys))
                VerifyKeyEverywhere(v, cat, key);
            // 毛色の行
            var visual = b.detail != null ? b.detail.transform.Find("Visual") : null;
            v.Check(visual != null && b.furValue != null && b.furValue.transform.parent == visual && b.furNote != null && b.furNote.transform.parent == visual, "fur value / note on the detail page");
            v.Check(b.furEditParts != null && b.furEditParts.Length == 4 && b.furEditParts.All(x => x != null), "fur ◀ ▶ parts (visual + hit) x 2");
            v.Check(b.furParts != null && b.furEditParts != null && b.furEditParts.All(x => b.furParts.Contains(x)) && b.furParts.Contains(b.furValue.gameObject) && b.furParts.Contains(b.furNote.gameObject), "fur row parts list covers the row");
            var buttons = b.GetComponentsInChildren<Button>(true);
            v.Check(buttons.Count(x => x.onClick.GetPersistentEventCount() == 1 && PersistentArg(x) == "_FurPrev") == 1
                && buttons.Count(x => x.onClick.GetPersistentEventCount() == 1 && PersistentArg(x) == "_FurNext") == 1, "one ◀ (_FurPrev) and one ▶ (_FurNext)");
            var save = buttons.FirstOrDefault(x => x.onClick.GetPersistentEventCount() == 1 && PersistentArg(x) == "_SaveName");
            var favorite = buttons.FirstOrDefault(x => x.onClick.GetPersistentEventCount() == 1 && PersistentArg(x) == "_ToggleFavorite");
            if (save != null && favorite != null && b.furEditParts != null && b.furEditParts.Length == 4)
            {
                float fy = ((RectTransform)b.furEditParts[1].transform).anchoredPosition.y;
                float gap = Mathf.Min(Mathf.Abs(((RectTransform)save.transform).anchoredPosition.y - fy), Mathf.Abs(((RectTransform)favorite.transform).anchoredPosition.y - fy));
                v.Check(gap >= 120f, "fur row apart from [save name] / [favorite] rows: " + gap.ToString("F0") + " px (>= 120, 個人設定と共有の毛色を分ける)");
            }
            if (b.furNote != null && visual != null)
            {
                var r = (RectTransform)b.furNote.transform;
                var close = buttons.FirstOrDefault(x => x.onClick.GetPersistentEventCount() == 1 && PersistentArg(x) == "_Close" && x.transform.IsChildOf(b.detail.transform));
                if (close != null)
                {
                    float noteRight = r.anchoredPosition.x + r.sizeDelta.x * .5f;
                    var c = (RectTransform)close.transform;
                    float closeLeft = c.anchoredPosition.x - c.sizeDelta.x * .5f;
                    v.Check(noteRight < closeLeft, "fur note ends left of [close] (" + noteRight.ToString("F0") + " < " + closeLeft.ToString("F0") + ")");
                }
            }
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            v.Check(font != null && b.furValue != null && b.furValue.font == font && b.furNote != null && b.furNote.font == font, "fur labels use the shared UI font");
            v.Check(font != null && KD_LocalizationEditor.HasGlyph(font, '◀') && KD_LocalizationEditor.HasGlyph(font, '▶'), "◀ ▶ glyphs in the shared UI font");
            KH_FurColorEditor.VerifyInto(v, b, cat, font);
        }

        static void VerifyKeyEverywhere(KD_VerifyLog v, KD_LocalizationEditor.Catalog cat, string key)
        {
            var missing = cat.languages.Where(l => !l.texts.Any(t => t.key == key && !string.IsNullOrEmpty(t.text))).Select(l => l.code).ToArray();
            v.Check(missing.Length == 0, "text " + key + " in all " + cat.languages.Length + " languages" + (missing.Length == 0 ? "" : " (missing: " + string.Join(",", missing) + ")"));
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
        static float FlatSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0f;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
