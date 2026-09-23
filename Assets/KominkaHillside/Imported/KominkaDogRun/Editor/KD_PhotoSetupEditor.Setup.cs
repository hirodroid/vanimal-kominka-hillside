using System.IO;
using System.Linq;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace KominkaDogRun.Editor
{
    /// <summary>IMP-CAM-001 撮影: 2 - Setup (カメラ・ギャラリー・ビューアの構築)。本体は KD_PhotoSetupEditor.cs。</summary>
    public static partial class KD_PhotoSetupEditor
    {
        // ================================================================ 2: Setup

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo System/2 - Setup Photo System")]
        public static void Setup()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_PhotoSetup] Setup");

            // IMP-CAM-002: 撮影は **1 個のまま全犬へ配線する**。
            // Director を犬ごとに複製しないのは、クールダウン・スロット・公平性が
            // 犬の数だけ分裂して「アルバム全体をどう配分するか」を誰も持たなくなるため。
            // **配列の並びは KD_DogDefs.All = KD_DogPack の名簿と同じ** —
            // 通知側 (fetch / wander) が自分の dogIndex をそのまま渡してくるので、
            // ここで別の番号体系を作ると黙ってズレる
            var defs = KD_DogDefs.All;
            int dogN = defs.Length;
            var dogRoots = new Transform[dogN];
            var headAnchors = new Transform[dogN];
            var fetches = new KD_DogFetchController[dogN];
            var pets = new KD_DogPetController[dogN];
            var wanders = new KD_DogWanderController[dogN];
            GameObject dog = null; // 仮置き位置の基準 (Primary = [0])

            for (int i = 0; i < dogN; i++)
            {
                var def = defs[i];
                log.AppendLine("-- [" + i + "] " + def.displayName + " (" + def.key + ")");

                int dogCount, fetchCount, petCount, wanderCount;
                var dogGo = KD_SceneObjects.Find(def.DogName, out dogCount);
                var fetchGo = KD_SceneObjects.Find(def.FetchSystemName, out fetchCount);
                var petGo = KD_SceneObjects.Find(def.PetSystemName, out petCount);
                var wanderGo = KD_SceneObjects.Find(def.WanderSystemName, out wanderCount);
                if (dogCount != 1 || fetchCount != 1)
                {
                    log.AppendLine($"   FAIL {def.DogName}={dogCount} / {def.FetchSystemName}={fetchCount}" +
                        " (それぞれ 1 個が前提。Fetch System を先に構築する)");
                    KD_VerifyLog.Write("Logs/imp-cam-001-setup.txt", log);
                    return;
                }
                var f = fetchGo.GetUdonSharpComponent<KD_DogFetchController>();
                if (f == null)
                {
                    log.AppendLine("   FAIL KD_DogFetchController が無い: " + def.FetchSystemName);
                    KD_VerifyLog.Write("Logs/imp-cam-001-setup.txt", log);
                    return;
                }
                dogRoots[i] = dogGo.transform;
                fetches[i] = f;
                headAnchors[i] = FindDescendant(dogGo, HeadAnchorName);
                pets[i] = petGo == null ? null : petGo.GetUdonSharpComponent<KD_DogPetController>();
                // 徘徊はこの Setup より**後**に作られることがある (Wander System Setup)。
                // 見つかった分だけ入れておく — 配線の正本は向こうで、ここは
                // **長さをそろえて Director を動かせる状態にする**のが目的
                // (長さが違うと Director は Start で自分を止める)
                wanders[i] = wanderGo == null ? null : wanderGo.GetUdonSharpComponent<KD_DogWanderController>();
                if (dog == null) dog = dogGo;

                if (pets[i] == null)
                    log.AppendLine("   WARN " + def.PetSystemName + " not found - この犬の撫での一枚は無効");
                if (headAnchors[i] == null)
                    log.AppendLine("   WARN " + HeadAnchorName + " not found - 構図はルート近似 (体格の逆算も効かない)");
                if (wanders[i] == null)
                    log.AppendLine("   WARN " + def.WanderSystemName +
                        " not found - 徘徊の走りと じゃれ合いは撮れない (Wander System Setup を後で実行する)");
            }

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                log.AppendLine("FAIL shader not found: " + ShaderName);
                KD_VerifyLog.Write("Logs/imp-cam-001-setup.txt", log);
                return;
            }

            if (!AssetDatabase.IsValidFolder(TexturesDir))
                AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Textures");
            if (!AssetDatabase.IsValidFolder("Assets/KominkaHillside/Imported/KominkaDogRun/Materials"))
                AssetDatabase.CreateFolder("Assets/KominkaHillside/Imported/KominkaDogRun", "Materials");

            // ---- 撮影用 RT (深度付き 1 枚) ----
            var captureRT = AssetDatabase.LoadAssetAtPath<RenderTexture>(CaptureRTPath);
            if (captureRT == null)
            {
                captureRT = new RenderTexture(SlotWidth, SlotHeight, CaptureDepthBits);
                captureRT.useMipMap = false;
                AssetDatabase.CreateAsset(captureRT, CaptureRTPath);
                log.AppendLine("created " + CaptureRTPath + " (depth " + CaptureDepthBits + ")");
            }
            else if (!RTMatches(captureRT, CaptureDepthBits, true))
            {
                RetuneRT(captureRT, CaptureDepthBits);
                log.AppendLine($"retuned {CaptureRTPath} -> {SlotWidth}x{SlotHeight} depth {CaptureDepthBits}");
            }

            // ---- スロット RT + マテリアル ----
            var slots = new RenderTexture[SlotCount];
            var slotMats = new Material[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                slots[i] = AssetDatabase.LoadAssetAtPath<RenderTexture>(SlotRTPath(i));
                if (slots[i] == null)
                {
                    var rt = new RenderTexture(SlotWidth, SlotHeight, SlotDepthBits);
                    rt.useMipMap = false;
                    AssetDatabase.CreateAsset(rt, SlotRTPath(i));
                    slots[i] = rt;
                    log.AppendLine("created " + SlotRTPath(i) + " (depth " + SlotDepthBits + ")");
                }
                else if (!RTMatches(slots[i], SlotDepthBits, false))
                {
                    // IMP-GAL-003: 既存 20 枚から深度を外す。**作り直さない** —
                    // マテリアルが GUID で参照しているので、削除して作り直すと参照が切れる
                    RetuneRT(slots[i], SlotDepthBits);
                    log.AppendLine($"retuned {SlotRTPath(i)} -> {SlotWidth}x{SlotHeight} " +
                        $"depth {SlotDepthBits} (in-place。GUID 維持)");
                }
                // IMP-GAL-002: 板も吸着シェーダーへ (Unlit から差し替え)。
                // エイム判定込みなので狙った 1 枚だけが全画面化する
                slotMats[i] = EnsureMaterial(SlotMatPath(i), shader, slots[i], log);
                SetAttachParams(slotMats[i], BoardAttachDistance, AttachAimMargin);
            }

            // 旧構成の掃除 (2026-08-11 台廃止): 台のマテリアルと板 Interact の Program Asset
            if (AssetDatabase.LoadAssetAtPath<Material>(LegacyPrintMatPath) != null)
            {
                AssetDatabase.DeleteAsset(LegacyPrintMatPath);
                log.AppendLine("deleted legacy " + LegacyPrintMatPath);
            }
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(LegacyBoardProgramPath) != null)
            {
                AssetDatabase.DeleteAsset(LegacyBoardProgramPath);
                log.AppendLine("deleted legacy " + LegacyBoardProgramPath);
            }

            // ---- シーン構築 ----
            int found;
            var sysGo = KD_SceneObjects.Find(SystemName, out found);
            if (found > 1)
            {
                log.AppendLine($"FAIL '{SystemName}' がシーン内に {found} 個ある。1 個に整理してから再実行");
                KD_VerifyLog.Write("Logs/imp-cam-001-setup.txt", log);
                return;
            }
            if (sysGo == null)
            {
                sysGo = new GameObject(SystemName);
                KD_SceneObjects.PlaceIfNew(sysGo, KD_DogDefs.PlacesPath);   // IMP-INF-003: 据えた場
                sysGo.transform.position = dog.transform.position;
                log.AppendLine("created " + SystemName);
            }

            // カメラ (撮影の瞬間だけ有効化される)
            var camT = sysGo.transform.Find("PhotoCamera");
            Camera cam;
            if (camT == null)
            {
                var camGo = new GameObject("PhotoCamera");
                camGo.transform.SetParent(sysGo.transform, false);
                cam = camGo.AddComponent<Camera>();
                // 初期値は新規作成時だけ入れる (再実行で FOV・mask 等の手調整を潰さない)
                cam.fieldOfView = 50f;
                cam.nearClipPlane = 0.01f; // IMP-ENV-021: 犬に寄っても切れない (視点と同じ 0.01)
                cam.farClipPlane = 60f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                log.AppendLine("created PhotoCamera");
            }
            else
            {
                cam = camT.GetComponent<Camera>();
                log.AppendLine("reused PhotoCamera (settings untouched)");
            }
            cam.enabled = false; // 有効のまま放置はしない (常時レンダリングになる)
            // IMP-GAL-003: 描き先は常に撮影用 RT。スロットへ直接描かせない
            // (スロットは深度を持たないため、直接描くと深度テストが働かない)
            cam.targetTexture = captureRT;
            // BUG-CAM-001: cullingMask は**毎回上書きする**。
            // 「手調整を潰さない」ために新規作成時だけ入れていたが、
            // これは好みの値ではなく**写ってはいけないものを決める値**なので Setup が正本。
            // (実機で UI が写り込んだとき、Setup 再実行で直らない形になっていた)
            cam.cullingMask = PhotoCullingMask;
            log.AppendLine("cullingMask = 0x" + PhotoCullingMask.ToString("X8") + " (" +
                DescribeMask(PhotoCullingMask) + ")");

            // ---- BUG-CAM-003 (案 B): 目の前に大きく出すビューア ----
            // 頭に追従する物なので位置に意味は無い。KD_PhotoSystem の子に置く
            var viewerShader = Shader.Find(ViewerShaderName);
            if (viewerShader == null)
            {
                log.AppendLine("FAIL shader not found: " + ViewerShaderName);
                KD_VerifyLog.Write("Logs/imp-cam-001-setup.txt", log);
                return;
            }
            var backMat = EnsureMaterial(ViewerBackMatPath, viewerShader, null, log);
            backMat.SetColor("_Color", Color.black);
            EditorUtility.SetDirty(backMat);
            var photoMat = EnsureMaterial(ViewerPhotoMatPath, viewerShader, null, log);
            photoMat.SetColor("_Color", Color.white);
            // **描画順を距離ソートに頼らない。** 両方 ZTest Always / ZWrite Off なので、
            // 後に描いた方が勝つ。背景 (4000 = Overlay) の後に写真 (4001) を描く
            backMat.renderQueue = ViewerBackQueue;
            photoMat.renderQueue = ViewerPhotoQueue;
            EditorUtility.SetDirty(backMat);
            EditorUtility.SetDirty(photoMat);

            var viewerT = sysGo.transform.Find(ViewerName);
            if (viewerT == null)
            {
                var viewerGo = new GameObject(ViewerName);
                viewerGo.transform.SetParent(sysGo.transform, false);
                viewerT = viewerGo.transform;
                log.AppendLine("created " + ViewerName);
            }
            var viewerBox = viewerT.GetComponent<BoxCollider>();
            if (viewerBox == null) viewerBox = viewerT.gameObject.AddComponent<BoxCollider>();
            viewerBox.size = new Vector3(ViewerBackWidth, ViewerBackHeight, 0.05f);
            viewerBox.center = Vector3.zero;
            viewerBox.isTrigger = true;     // 頭に貼り付くので、体を押さない
            viewerBox.enabled = false;      // 開いている間だけ KD_PhotoViewer が有効にする

            var panelT = viewerT.Find("Panel");
            if (panelT == null)
            {
                var panelGo = new GameObject("Panel");
                panelGo.transform.SetParent(viewerT, false);
                panelT = panelGo.transform;
                log.AppendLine("created Panel");
            }
            Transform ViewerQuad(string name, float w, float h, float z, Material m)
            {
                var q = panelT.Find(name);
                if (q == null)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    go.name = name;
                    go.transform.SetParent(panelT, false);
                    Object.DestroyImmediate(go.GetComponent<Collider>());  // 当たり判定は root の Trigger だけ
                    q = go.transform;
                    log.AppendLine("created " + name);
                }
                q.localPosition = new Vector3(0f, 0f, z);
                q.localRotation = Quaternion.identity;
                q.localScale = new Vector3(w, h, 1f);
                q.gameObject.layer = UiLayer;
                q.GetComponent<MeshRenderer>().sharedMaterial = m;
                return q;
            }
            // 背景をわずかに奥へ。ZTest Always なので描画順は Queue 内の距離順に頼る
            ViewerQuad("Backdrop", ViewerBackWidth, ViewerBackHeight, 0.02f, backMat);
            var photoQuadT = ViewerQuad("Photo", ViewerPhotoWidth, ViewerPhotoHeight, 0f, photoMat);
            panelT.gameObject.SetActive(false);  // 開いたまま保存しない (写真にも写らないが、規律として)

            var viewer = EnsureUsc<KD_PhotoViewer>(viewerT.gameObject, log);
            viewer.slotTextures = slots;
            viewer.photoRenderer = photoQuadT.GetComponent<MeshRenderer>();
            viewer.panelRoot = panelT.gameObject;
            viewer.closeCollider = viewerBox;
            viewer.distance = ViewerDistance;
            // IMP-TRT-006: 開いている間は手元メニューの起動ボタン (Screen-Space) を隠してもらう —
            // スマホの持ち帰りは端末スクショなので、Overlay が写る。名前で引く (パスでは引かない)
            var pouchGo = KD_SceneObjects.Find(KD_DogDefs.TreatPouchName);
            viewer.pouch = pouchGo != null ? pouchGo.GetUdonSharpComponent<KD_TreatPouch>() : null;
            if (viewer.pouch == null)
                log.AppendLine("  WARN " + KD_DogDefs.TreatPouchName + " が見つからない — viewer.pouch 未配線 "
                    + "(Treat System/2 を先に流してから再実行。無いとスマホの持ち帰りに [メニュー] が写る)");
            var viewerUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(viewer);
            if (viewerUdon != null)
            {
                viewerUdon.interactText = KD_PhotoViewer.InteractWord;
                viewerUdon.proximity = BoardProximity;
                EditorUtility.SetDirty(viewerUdon);
            }
            log.AppendLine($"viewer: distance {ViewerDistance} / photo {ViewerPhotoWidth:F2}x{ViewerPhotoHeight:F2} " +
                $"/ backdrop {ViewerBackWidth}x{ViewerBackHeight} / renderers on layer {UiLayer} (UI)");

            // ギャラリー一式 (置き場所は仮 — Play 後に Gallery ごと縁側へ動かす。再実行で保持)
            var galleryT = sysGo.transform.Find("Gallery");
            if (galleryT == null)
            {
                var galleryGo = new GameObject("Gallery");
                galleryT = galleryGo.transform;
                galleryT.SetParent(sysGo.transform, false);
                Vector3 dogFwd = dog.transform.forward;
                galleryT.position = dog.transform.position - dogFwd * 2.5f + Vector3.up * 1.3f;
                galleryT.rotation = Quaternion.LookRotation(-dogFwd, Vector3.up);
                log.AppendLine($"created Gallery at {galleryT.position} (PLACEHOLDER - 縁側へ手で動かす)");
            }
            else
            {
                log.AppendLine("reused Gallery (position untouched)");
            }

            var boards = new GameObject[SlotCount];
            var boardScripts = new KD_PhotoBoard[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                var bT = galleryT.Find("Board" + i);
                if (bT == null)
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    b.name = "Board" + i;
                    b.transform.SetParent(galleryT, false);
                    log.AppendLine("created Board" + i);
                    bT = b.transform;
                }

                // **IMP-GAL-004: 配置は毎回上書きする (Caption の高さと同じ扱い)。**
                // ここは手調整の対象ではなく BoardsPerRow / 間隔からの**導出値**で、
                // 正本はこのファイル。生成時にしか書いていなかったので、
                // **4 → 6 列にしても既存シーンの板が 1 枚も動かず**、
                // 「列 = カテゴリ」が成り立たないまま Verify だけ落ちる状態になっていた。
                // ギャラリーごとの位置 (galleryT) は手調整なのでそのまま
                int row = i / BoardsPerRow;
                int col = i % BoardsPerRow;
                Vector3 wantPos = new Vector3(ColumnX(col), row * BoardSpacingY, 0f);
                Vector3 wantScale = new Vector3(BoardWidth, BoardHeight, 1f);
                if ((bT.localPosition - wantPos).sqrMagnitude > 1e-8f)
                {
                    log.AppendLine($"moved Board{i} {bT.localPosition} -> {wantPos} (row {row} / col {col})");
                    bT.localPosition = wantPos;
                }
                bT.localScale = wantScale;
                bT.localRotation = Quaternion.identity;
                boards[i] = bT.gameObject;
                boards[i].GetComponent<MeshRenderer>().sharedMaterial = slotMats[i];

                // BUG-CAM-003 (案 B): 板の Interact を**復活**させる (2026-08-11 に一度廃止)。
                // 用途は当時と違う — 持ち帰り台の切替ではなく「目の前に大きく出す」。
                // 当たり判定は Quad の MeshCollider ではなく薄い BoxCollider に統一する
                // (旧構成を剥がした板は Collider を持っていないので、どちらにせよ足す)
                var mc = boards[i].GetComponent<MeshCollider>();
                if (mc != null) Object.DestroyImmediate(mc);
                var box = boards[i].GetComponent<BoxCollider>();
                if (box == null)
                {
                    box = boards[i].AddComponent<BoxCollider>();
                    log.AppendLine("added BoxCollider to Board" + i);
                }
                box.size = new Vector3(1f, 1f, 0.02f);
                box.center = Vector3.zero;
                box.isTrigger = false;

                var pb = EnsureUsc<KD_PhotoBoard>(boards[i], log);
                pb.viewer = viewer;
                pb.slotIndex = i;
                boardScripts[i] = pb;
                var pbUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(pb);
                if (pbUdon != null)
                {
                    // 既定文言 "Use" のまま放置しない (KD_ShopKeeperSetupEditor と同じ検査型)
                    pbUdon.interactText = KD_PhotoBoard.InteractWord;
                    pbUdon.proximity = BoardProximity;
                    EditorUtility.SetDirty(pbUdon);
                }
            }

            // ---- IMP-GAL-004: 列ラベル ----
            // 「この列は何が撮られるか」を各列の上に 1 枚ずつ。**文言の正本は SlotGroupNames**。
            // **未撮影の列はラベルだけが見える** — 板は撮るまで非表示なので、
            // 空の列がそのまま「まだ撮れていない出来事」の案内になる (遊び方を文章で説明しない)
            for (int g = 0; g < SlotGroupCount; g++)
            {
                var lT = galleryT.Find("Label" + g);
                if (lT == null)
                {
                    var lb = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    lb.name = "Label" + g;
                    lb.transform.SetParent(galleryT, false);
                    // Caption と同じ扱い — 触れる物ではないので当たり判定を持たせない
                    Object.DestroyImmediate(lb.GetComponent<Collider>());
                    lT = lb.transform;
                    log.AppendLine("created Label" + g + " (" + SlotGroupNames[g] + ")");
                }
                // 位置は板と同じ ColumnX を通す (別計算にすると「ラベルだけ半列ずれる」が起きる)。
                // IMP-CAM-005: 2 列のカテゴリはその 2 列の中央に、2 列ぶんの幅で置く
                GroupColumns(g, out int first, out int span);
                lT.localPosition = new Vector3((ColumnX(first) + ColumnX(first + span - 1)) * 0.5f, LabelLocalY, 0f);
                lT.localScale = new Vector3(BoardWidth + (span - 1) * BoardSpacingX, LabelHeight, 1f);
                lT.localRotation = Quaternion.identity;
            }
            // 列数を減らした場合に古いラベルが浮いたまま残らないようにする
            for (int g = SlotGroupCount; g < SlotGroupCount + 4; g++)
            {
                var stale = galleryT.Find("Label" + g);
                if (stale == null) continue;
                Object.DestroyImmediate(stale.gameObject);
                log.AppendLine("removed stale Label" + g + " (列が減った)");
            }
            log.AppendLine($"labels: {string.Join(" / ", SlotGroupNames)} " +
                $"(列 {BoardsPerRow} × 段 {BoardRows} / y={LabelLocalY:F2})");

            // 持ち帰り台 (PhotoPrint) は廃止 — 残っていれば Caption ごと消して作り直す
            var printRootT = galleryT.Find("PhotoPrint");
            if (printRootT != null)
            {
                Object.DestroyImmediate(printRootT.gameObject);
                log.AppendLine("removed legacy PhotoPrint (持ち帰り台の廃止 — 2026-08-11)");
            }

            // Caption はギャラリー最上段の上 (説明が先に目に入る位置)
            var captionT = galleryT.Find("Caption");
            if (captionT == null)
            {
                var caption = GameObject.CreatePrimitive(PrimitiveType.Quad);
                caption.name = "Caption";
                caption.transform.SetParent(galleryT, false);
                caption.transform.localPosition = CaptionLocalPos;
                caption.transform.localScale = new Vector3(0.9f, CaptionHeight, 1f);
                Object.DestroyImmediate(caption.GetComponent<Collider>());
                captionT = caption.transform;
                log.AppendLine($"created Caption at top {CaptionLocalPos}");
            }
            else if (!Mathf.Approximately(captionT.localPosition.y, CaptionLocalPos.y))
            {
                // IMP-GAL-003: 段数が変わったら追随させる。高さは BoardRows からの導出値
                // (正本はこのファイル) なので Setup が持つ。横位置の手調整は保持する
                var cp = captionT.localPosition;
                log.AppendLine($"moved Caption y {cp.y:F2} -> {CaptionLocalPos.y:F2} (段数 {BoardRows} へ追随)");
                cp.y = CaptionLocalPos.y;
                captionT.localPosition = cp;
            }
            // 高さは PNG の比からの導出値 (BUG-CAM-003 で 2 行 → 3 行)。毎回そろえる
            captionT.localScale = new Vector3(CaptionWidth, CaptionHeight, 1f);
            BuildGalleryText(galleryT, AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(GalleryFontPath), KD_LocalizationEditor.Load(), log);
            var menuPouch=KD_SceneObjects.Find(KD_DogDefs.TreatPouchName)?.GetComponent<KD_TreatPouch>();
            // Menu Finish owns migration and localization binding. Photo Setup only follows
            // caption geometry after the share panel has already been installed.
            if(menuPouch!=null && menuPouch.language!=null && galleryT.Find("ShareCanvas")!=null)
            {
                KD_MenuFinishEditor.BuildPhotoShare(menuPouch,menuPouch.language,menuPouch.sectionTitleLabel.font,log);
                UdonSharpEditorUtility.CopyProxyToUdon(menuPouch.language);
            }

            // ---- U# 配線 ----
            var capture = EnsureUsc<KD_PhotoCapture>(sysGo, log);
            var gallery = EnsureUsc<KD_GalleryController>(sysGo, log);
            var director = EnsureUsc<KD_PhotoDirector>(sysGo, log);

            capture.photoCamera = cam;
            capture.captureTexture = captureRT;

            gallery.slotTextures = slots;
            gallery.boards = boards;
            gallery.slotGroupCount = SlotGroupCount;
            // IMP-CAM-005: スロット → カテゴリ (列表から導出。板 i は列 i % BoardsPerRow)
            gallery.slotGroups = new int[SlotCount];
            for (int i = 0; i < SlotCount; i++) gallery.slotGroups[i] = ColumnGroups[i % BoardsPerRow];

            director.photoCapture = capture;
            director.photoCamera = cam;
            director.gallery = gallery;
            // IMP-CAM-002: 犬は 5 本の配列で持つ (並びは KD_DogDefs.All)
            director.dogRoots = dogRoots;
            director.dogHeadAnchors = headAnchors;
            director.fetchControllers = fetches;
            director.petControllers = pets;
            director.wanderControllers = wanders;
            // IMP-CAM-003: カメラを遮る地形だけを見る。**プレイヤーと犬は入れない** —
            // 入れると被写体自身に遮られたと判定してカメラが寄り切ってしまう。
            // 古民家・地面は Default(0) 実測 (2026-08-14)。Environment(11) は保険
            director.cameraBlockMask = (1 << 0) | (1 << 11);

            // **逆配線は全犬。** かつて Primary だけだったので、
            // ポメ・ラブ・ダックスは受け渡しでも走っても 1 枚も撮られなかった (IMP-CAM-002)
            for (int i = 0; i < dogN; i++) fetches[i].photoDirector = director;

            bool copyOk = true;
            copyOk &= TryCopy(capture, log);
            copyOk &= TryCopy(gallery, log);
            copyOk &= TryCopy(director, log);
            copyOk &= TryCopy(viewer, log);
            for (int i = 0; i < SlotCount; i++) copyOk &= TryCopy(boardScripts[i], log);
            for (int i = 0; i < dogN; i++) copyOk &= TryCopy(fetches[i], log);
            log.AppendLine("wired dogs=" + dogN + " (fetch から director への逆配線も全犬)");
            log.AppendLine("slot groups (出来事) = " + string.Join(" / ", SlotGroupNames) +
                " — 列ラベルの正本 (IMP-GAL-004 はここを読む)");

            KD_BedroomOnboardingEditor.RewirePhoto(director, viewer, slots);
            if (KD_SceneObjects.Find("KD_PhotoFrame") != null) KD_PhotoFrameEditor.Setup();
            if (KD_SceneObjects.Find("KD_PhotoSettings") != null) KD_PhotoInformationEditor.Setup();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.AppendLine(copyOk
                ? "SETUP RESULT: PASS (scene not saved yet)"
                : "SETUP RESULT: FAIL (CopyProxyToUdon 失敗あり。force refresh 後に再実行)");
            KD_VerifyLog.Write("Logs/imp-cam-001-setup.txt", log);
        }

    }
}
