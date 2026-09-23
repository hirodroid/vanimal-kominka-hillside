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
    /// <summary>IMP-CAM-001 撮影: 3 - Verify。本体は KD_PhotoSetupEditor.cs。</summary>
    public static partial class KD_PhotoSetupEditor
    {
        // ================================================================ 3: Verify

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo System/3 - Verify Photo System")]
        public static void Verify()
        {
            var v = new KD_VerifyLog("[KD_PhotoSetup] Verify");
            var log = v.Text;

            foreach (var name in ScriptNames)
            {
                var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ScriptsDir + name + ".asset");
                v.Check(pa != null && pa.sourceCsScript != null, "program asset ok: " + name);
            }
            v.Check(Shader.Find(ShaderName) != null, "shader exists: " + ShaderName);

            // IMP-GAL-003: 深度を持つのは撮影用の 1 枚だけ。スロットは色だけ。
            // ここが崩れると RT 総量が一気に 2 倍以上へ戻る (20 枚 211 MiB の教訓)
            var capRT = AssetDatabase.LoadAssetAtPath<RenderTexture>(CaptureRTPath);
            v.Check(capRT != null && capRT.width == SlotWidth && capRT.height == SlotHeight,
                $"capture RT exists ({SlotWidth}x{SlotHeight})");
            v.Check(capRT != null && capRT.depth >= CaptureDepthBits,
                $"capture RT has depth (>= {CaptureDepthBits} bits, actual {(capRT == null ? -1 : capRT.depth)})");
            int slotsWithDepth = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(SlotRTPath(i));
                v.Check(rt != null && rt.width == SlotWidth && rt.height == SlotHeight,
                    $"slot RT {i} exists ({SlotWidth}x{SlotHeight})");
                if (rt != null && rt.depth != SlotDepthBits) slotsWithDepth++;
            }
            v.Check(slotsWithDepth == 0,
                $"slot RTs carry no depth buffer (depth-bearing slots {slotsWithDepth}/{SlotCount})");
            // 見積もりを記録に残す (Profiler 実測との突き合わせ用。実測が正本)
            float colorMiB = SlotWidth * SlotHeight * 4f / (1024f * 1024f);
            float depthMiB = SlotWidth * SlotHeight * 8f / (1024f * 1024f);
            log.AppendLine($"  note RT budget (計算値): slots {SlotCount} x {colorMiB:F2} + capture " +
                $"{(colorMiB + depthMiB):F2} = {SlotCount * colorMiB + colorMiB + depthMiB:F1} MiB " +
                "— **Profiler で実測すること** (計算だけで判断しない)");
            var bedroomBudgetRoot = KD_SceneObjects.Find("KD_BedroomPhoto");
            var bedroomBudget = bedroomBudgetRoot != null ? bedroomBudgetRoot.GetComponent<KD_GalleryController>() : null;
            int bedroomSlots = bedroomBudget != null && bedroomBudget.slotTextures != null ? bedroomBudget.slotTextures.Length : 0;
            if (bedroomSlots > 0) log.AppendLine($"  note bedroom RT budget: +{bedroomSlots * colorMiB:F2} MiB / combined {(SlotCount + bedroomSlots + 1) * colorMiB + depthMiB:F2} MiB (estimate)");

            // BUG-CAM-003: 吸着プローブの ON/OFF は **ここでは合否にしない** (切り分け中は ON が
            // 正常)。公開前の検査は Release Check/1 が持つ。ここは状態を記録するだけ
            int probeOn = CountDebugProbeOn();
            log.AppendLine("  note debug probe: " + (probeOn == 0 ? "OFF" : "**ON** (" + probeOn + "/"
                + SlotCount + " materials — 公開前に Photo System/5 で戻す。Release Check/1 が FAIL にする)"));

            int found;
            var sysGo = KD_SceneObjects.Find(SystemName, out found);
            v.Check(found == 1, $"{SystemName} exists exactly once in the scene (actual {found})");
            if (sysGo == null)
            {
                v.Finish(VerifyLogFile);
                return;
            }
            v.Check(!UnityEditor.PrefabUtility.IsPartOfPrefabInstance(sysGo), SystemName + " is a plain scene object");

            var camT = sysGo.transform.Find("PhotoCamera");
            var cam = camT == null ? null : camT.GetComponent<Camera>();
            v.Check(cam != null, "PhotoCamera exists");
            if (cam != null)
            {
                v.Check(!cam.enabled, "PhotoCamera disabled by default");
                v.Check(cam.targetTexture != null, "PhotoCamera has targetTexture");
                // IMP-ENV-021: 犬に寄っても切れない。視点 (Reference Camera) と同じ Near 0.01、Far は庭の範囲 60
                v.Check(cam.nearClipPlane <= 0.01f + 1e-4f && Mathf.Approximately(cam.farClipPlane, 60f),
                    "PhotoCamera near 0.01 / far 60 (actual " + cam.nearClipPlane + " / " + cam.farClipPlane + ")");

                // BUG-CAM-001: 許可リストと完全一致していること。
                // **「特定の 1 枚を外し忘れた」を検出できる形にする** —
                // 個別の bit チェックだけだと、除外リスト方式の漏れを素通りしていた
                v.Check(cam.cullingMask == PhotoCullingMask,
                    "cullingMask == 許可リスト (期待 0x" + PhotoCullingMask.ToString("X8") +
                    " / 実際 0x" + cam.cullingMask.ToString("X8") + ")");
                if (cam.cullingMask != PhotoCullingMask)
                {
                    int extra = cam.cullingMask & ~PhotoCullingMask;
                    int missing = PhotoCullingMask & ~cam.cullingMask;
                    if (extra != 0) log.AppendLine("       余分に写る: " + DescribeMask(extra));
                    if (missing != 0) log.AppendLine("       写らない: " + DescribeMask(missing));
                }
                v.Check((cam.cullingMask & (1 << 10)) == 0, "cullingMask excludes PlayerLocal(10) (顔が欠ける)");
                v.Check((cam.cullingMask & (1 << 18)) != 0, "cullingMask includes MirrorReflection(18) (自分が写る)");
                v.Check((cam.cullingMask & (1 << 2)) == 0,
                    "cullingMask excludes Ignore Raycast(2) — **QvPen の UI が居る**");
                v.Check((cam.cullingMask & (1 << 4)) == 0,
                    "cullingMask excludes Water(4) — **個人用ミラーが居る**");
                v.Check((cam.cullingMask & ((1 << 5) | (1 << 12) | (1 << 19) | (1 << 20))) == 0,
                    "cullingMask excludes UI(5) / UiMenu(12) / InternalUI(19) / HardwareObjects(20)");
                v.Check((cam.cullingMask & (1 << 13)) != 0, "cullingMask includes Pickup(13) (ボールが写る)");
                v.Check((cam.cullingMask & (1 << 9)) != 0, "cullingMask includes Player(9) (他の人が写る)");

                // **写真に写るレイヤーへ、想定外の UI が居ないこと。**
                // 実機で写り込んだのは全部 Canvas だった (BUG-CAM-001)。
                // レイヤーで外せるものは PhotoLayers で外したが、**ワールド本体と同じ
                // レイヤーに置かれた UI は外せない**ので、ここで名指しで管理する。
                //
                // 既知で**許容済み**のものは通し、知らないものが増えたら FAIL にする。
                // 「全部 0 件」にすると許容済みの分で永久 FAIL になり、
                // 検査そのものが無視されるようになるため
                int unexpectedUi = 0, acceptedUi = 0;
                var uiNames = new StringBuilder();
                // IMP-INF-003: 自作の許容判定を **root の名前ではなく所属**で見る。
                // 階層化で KD_PhotoSystem がルートでなくなり、root.name が
                // "====KominkaDogRun====" になった瞬間にクレジット板が「想定外」に化けた
                var photoT = sysGo.transform;
                foreach (var cv in Object.FindObjectsOfType<Canvas>(true))
                {
                    if ((PhotoCullingMask & (1 << cv.gameObject.layer)) == 0) continue;
                    if (!cv.gameObject.activeInHierarchy || !cv.enabled) continue;
                    // Photo help pointer surfaces are transparent; their visible siblings are on UI.
                    // Verify actual alpha instead of exempting every canvas below the help object.
                    if (cv.GetComponentInParent<KD_PhotoHelp>() != null || cv.GetComponentInParent<KD_DogBook>() != null)
                    {
                        bool invisible = true;
                        foreach (var graphic in cv.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                            if (graphic.color.a > 0f) invisible = false;
                        if (invisible) continue;
                    }
                    string rootName = cv.transform.root.name;
                    // 自作のクレジット板は「写ってよい UI」(掲示物として置いてある)
                    if (photoT != null && cv.transform.IsChildOf(photoT)) continue;
                    // VizVid (動画プレイヤー) の操作パネル。**2026-08-14 ユーザー判断で
                    // 「そのまま」** — 家の方を向いた構図では写る。庭側の構図なら入らない
                    if (rootName == AcceptedUiRoot) { acceptedUi++; continue; }
                    unexpectedUi++;
                    if (unexpectedUi <= 6)
                        uiNames.Append("\n       - layer ").Append(cv.gameObject.layer)
                               .Append(" : ").Append(cv.gameObject.name)
                               .Append("  (root ").Append(rootName).Append(')');
                }
                v.Check(unexpectedUi == 0,
                    "写真に写るレイヤー上に**想定外の** active Canvas が無い " +
                    $"(想定外 {unexpectedUi} / 許容済み {acceptedUi})" + uiNames);
                log.AppendLine($"  note 許容済みの写り込み UI {acceptedUi} 個 ('{AcceptedUiRoot}' 配下 = " +
                    "VizVid の操作パネル。2026-08-14 ユーザー判断で「そのまま」)");
            }

            var capture = sysGo.GetUdonSharpComponent<KD_PhotoCapture>();
            var gallery = sysGo.GetUdonSharpComponent<KD_GalleryController>();
            var director = sysGo.GetUdonSharpComponent<KD_PhotoDirector>();
            v.Check(capture != null && gallery != null && director != null,
                "KD_PhotoCapture / KD_GalleryController / KD_PhotoDirector on " + SystemName);

            if (capture != null)
            {
                v.Check(capture.photoCamera == cam, "capture.photoCamera wired");
                v.Check(capture.captureTexture == capRT, "capture.captureTexture wired to capture RT");
                v.Check(cam == null || cam.targetTexture == capRT,
                    "PhotoCamera targets the capture RT (スロットへ直接描かない)");
            }
            if (gallery != null)
            {
                v.Check(gallery.slotTextures != null && gallery.slotTextures.Length == SlotCount,
                    "gallery.slotTextures length == " + SlotCount);
                v.Check(gallery.boards != null && gallery.boards.Length == SlotCount,
                    "gallery.boards length == " + SlotCount);
                // IMP-CAM-003: カテゴリが均等に割り切れること。割り切れないと
                // 一部カテゴリだけ枠が 1 枚多く、上書きの寿命が偏る
                v.Check(gallery.slotGroupCount == SlotGroupCount,
                    "gallery.slotGroupCount == " + SlotGroupCount);
                // ---- IMP-GAL-004 / IMP-CAM-005: 列 = カテゴリ (列表) ----
                //
                // KD_GalleryController は slotGroups (スロット → カテゴリ) で回るので、
                // **表と器の配列が一致しているときだけ列ラベルが本当になる**。
                // 片方だけ触ると「庭とあそび」の列に受け渡しの写真が出る、という
                // Play で気づきにくい壊れ方をする
                v.Check(ColumnGroups.Length == BoardsPerRow,
                    $"ColumnGroups.Length == BoardsPerRow ({ColumnGroups.Length} / {BoardsPerRow})");
                bool everyGroupHasColumn = true;
                for (int g = 0; g < SlotGroupCount; g++) if (LabelSpan(g) < 1) everyGroupHasColumn = false;
                v.Check(everyGroupHasColumn && ColumnGroups.All(g => g >= 0 && g < SlotGroupCount),
                    "ColumnGroups が全カテゴリを 1 列以上に割り当て、範囲内");
                bool slotGroupsMatch = gallery.slotGroups != null && gallery.slotGroups.Length == SlotCount;
                if (slotGroupsMatch)
                    for (int i = 0; i < SlotCount; i++) if (gallery.slotGroups[i] != ColumnGroups[i % BoardsPerRow]) slotGroupsMatch = false;
                v.Check(slotGroupsMatch, "gallery.slotGroups == ColumnGroups[i % BoardsPerRow] (列 = カテゴリ)");
                {
                    var counts = new int[SlotGroupCount];
                    for (int i = 0; i < SlotCount; i++) counts[ColumnGroups[i % BoardsPerRow]]++;
                    v.Check(counts.SequenceEqual(new[] { 8, 8, 4, 4 }),
                        "カテゴリ枚数 = 8 / 8 / 4 / 4 (actual " + string.Join("/", counts) + ") — IMP-CAM-005 ユーザー決定");
                }
                v.Check(SlotCount % BoardsPerRow == 0,
                    $"板が {BoardsPerRow} 列で割り切れる ({SlotCount} / {BoardsPerRow} = {BoardRows} 段)");

                // 2026-08-11 台廃止: PhotoPrint が残っていないこと + Caption が最上段の上に居ること
                var galleryT = sysGo.transform.Find("Gallery");
                v.Check(galleryT != null && galleryT.Find("PhotoPrint") == null,
                    "legacy PhotoPrint (持ち帰り台) removed");
                var captionT = galleryT == null ? null : galleryT.Find("Caption");
                v.Check(captionT != null, "Caption exists under Gallery");
                if (captionT != null)
                {
                    v.Check(captionT.localPosition.y > (BoardRows - 1) * BoardSpacingY + 0.17f,
                        $"Caption is above top row (y={captionT.localPosition.y:F2})");
                    v.Check(captionT.GetComponent<Collider>() == null, "Caption has no collider");
                    v.Check(Mathf.Abs(captionT.localScale.y - CaptionHeight) < 1e-4f,
                        $"Caption の高さ == {CaptionHeight:F4} (3 行ぶん)");
                    // IMP-GAL-004: 列ラベルを足したぶん Caption は上がる。重なっていないこと
                    v.Check(captionT.localPosition.y > LabelLocalY + LabelHeight * 0.5f,
                        $"Caption は列ラベルより上 (caption y={captionT.localPosition.y:F2} / " +
                        $"label 上端 {(LabelLocalY + LabelHeight * 0.5f):F2})");
                }

                // ---- IMP-GAL-004: 列ラベル ----
                for (int g = 0; g < SlotGroupCount; g++)
                {
                    string ltag = $"label {g} ({SlotGroupNames[g]}): ";
                    var lT = galleryT == null ? null : galleryT.Find("Label" + g);
                    v.Check(lT != null, ltag + "Label" + g + " exists under Gallery");
                    if (lT == null) continue;

                    v.Check(lT.GetComponent<Collider>() == null, ltag + "collider を持たない");
                    // **カテゴリ g の板と同じ列に居ること。** ここがずれると
                    // 「じゃれ合い」の看板の下に受け渡しの写真が並ぶ。
                    // カテゴリ g の列 (先頭〜末尾) の中央に居ること。最下段の板 = 列番号そのもの
                    GroupColumns(g, out int firstCol, out int span);
                    var firstBoard = gallery.boards != null && firstCol >= 0 && firstCol < gallery.boards.Length
                        ? gallery.boards[firstCol] : null;
                    var lastBoard = gallery.boards != null && firstCol >= 0 && firstCol + span - 1 < gallery.boards.Length
                        ? gallery.boards[firstCol + span - 1] : null;
                    float wantX = firstBoard == null || lastBoard == null ? 0f
                        : (firstBoard.transform.localPosition.x + lastBoard.transform.localPosition.x) * 0.5f;
                    v.Check(firstBoard != null && lastBoard != null && Mathf.Abs(lT.localPosition.x - wantX) < 0.001f,
                        ltag + $"カテゴリ {g} の列 {firstCol}..{firstCol + span - 1} の中央 (label x={lT.localPosition.x:F3} / 期待 {wantX:F3})");
                    v.Check(Mathf.Abs(lT.localScale.x - (BoardWidth + (span - 1) * BoardSpacingX)) < 0.001f,
                        ltag + $"{span} 列ぶんの幅 (scale x={lT.localScale.x:F3})");
                    v.Check(Mathf.Abs(lT.localPosition.y - LabelLocalY) < 0.001f,
                        ltag + $"最上段の上 (y={lT.localPosition.y:F2} / 期待 {LabelLocalY:F2})");

                    v.Check(lT.Find("Text")?.GetComponent<TMPro.TextMeshPro>() != null, ltag + "TMP text exists");
                }
                // 列を減らしたときの取り残し
                v.Check(galleryT == null || galleryT.Find("Label" + SlotGroupCount) == null,
                    $"Label{SlotGroupCount} が残っていない (列を減らしたときの取り残し)");

                if (gallery.boards != null)
                {
                    for (int i = 0; i < gallery.boards.Length; i++)
                    {
                        var b = gallery.boards[i];
                        if (b == null) { v.Check(false, $"board {i} wired"); continue; }
                        // BUG-CAM-003 (案 B): 板はタップできる — BoxCollider + KD_PhotoBoard
                        v.Check(b.GetComponent<BoxCollider>() != null && !b.GetComponent<BoxCollider>().isTrigger,
                            $"board {i} has a solid BoxCollider (タップ用)");
                        var pb = b.GetUdonSharpComponent<KD_PhotoBoard>();
                        v.Check(pb != null && pb.viewer != null && pb.slotIndex == i,
                            $"board {i} KD_PhotoBoard wired (viewer set / slotIndex == {i})");
                        var pbUdon = pb == null ? null : UdonSharpEditorUtility.GetBackingUdonBehaviour(pb);
                        v.Check(pbUdon != null && pbUdon.interactText == KD_PhotoBoard.InteractWord,
                            $"board {i} interactText == \"{KD_PhotoBoard.InteractWord}\" (既定の \"Use\" のまま放置しない)");
                        var mat = b.GetComponent<MeshRenderer>().sharedMaterial;
                        v.Check(mat != null && mat.mainTexture == AssetDatabase.LoadAssetAtPath<RenderTexture>(SlotRTPath(i)),
                            $"board {i} material shows slot RT {i}");
                        // IMP-GAL-002: 板は吸着シェーダー + 短距離 + エイム判定
                        v.Check(mat != null && mat.shader != null && mat.shader.name == ShaderName,
                            $"board {i} material uses " + ShaderName);
                        v.Check(mat != null && mat.HasProperty("_AttachDistance") &&
                            Mathf.Approximately(mat.GetFloat("_AttachDistance"), BoardAttachDistance),
                            $"board {i} _AttachDistance == {BoardAttachDistance}");
                        v.Check(mat != null && mat.HasProperty("_AttachMargin") &&
                            Mathf.Approximately(mat.GetFloat("_AttachMargin"), AttachAimMargin),
                            $"board {i} _AttachMargin == {AttachAimMargin}");

                        // IMP-GAL-004: **板が「自分の列」に居ること。**
                        // 配置は導出値なので Setup が毎回そろえるが、そろえ忘れると
                        // 「定数は 6 列なのにシーンは 4 列のまま」= ラベルと中身がずれる。
                        // かつて配置を生成時にしか書いていなかったので、これは実際に起きた
                        int wantRow = i / BoardsPerRow;
                        int wantCol = i % BoardsPerRow;
                        Vector3 wantLocal = new Vector3(ColumnX(wantCol), wantRow * BoardSpacingY, 0f);
                        v.Check((b.transform.localPosition - wantLocal).sqrMagnitude < 1e-6f,
                            $"board {i} は row {wantRow} / col {wantCol} = {wantLocal} " +
                            $"(actual {b.transform.localPosition})");
                    }

                    // ---- BUG-CAM-003 (案 B): ビューア ----
                    var viewerT = sysGo == null ? null : sysGo.transform.Find(ViewerName);
                    v.Check(viewerT != null, ViewerName + " exists under " + SystemName);
                    if (viewerT != null)
                    {
                        var vw = viewerT.GetUdonSharpComponent<KD_PhotoViewer>();
                        v.Check(vw != null, ViewerName + " has KD_PhotoViewer");
                        var vbox = viewerT.GetComponent<BoxCollider>();
                        v.Check(vbox != null && vbox.isTrigger, ViewerName + " collider is a Trigger (体を押さない)");
                        var panelT = viewerT.Find("Panel");
                        v.Check(panelT != null && !panelT.gameObject.activeSelf,
                            "viewer Panel は既定で非アクティブ (開いたまま保存しない)");
                        if (vw != null)
                        {
                            int expectedViewerSlots = SlotCount + (director != null && director.bedroom != null ? 4 : 0);
                            v.Check(vw.slotTextures != null && vw.slotTextures.Length == expectedViewerSlots,
                                "viewer.slotTextures length == " + expectedViewerSlots);
                            v.Check(vw.photoRenderer != null && vw.panelRoot == (panelT == null ? null : panelT.gameObject)
                                && vw.closeCollider == vbox, "viewer photoRenderer / panelRoot / closeCollider wired");
                            var vUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(vw);
                            v.Check(vUdon != null && vUdon.interactText == KD_PhotoViewer.InteractWord,
                                $"viewer interactText == \"{KD_PhotoViewer.InteractWord}\"");
                            // IMP-TRT-006: 未配線だと Touch の持ち帰りスクショに [メニュー] が写る
                            v.Check(vw.pouch != null, "viewer.pouch wired (開いている間は手元メニューの起動ボタンを隠す)");
                        }
                        if (panelT != null)
                        {
                            int offLayer = 0, wrongShader = 0;
                            var frame = KD_SceneObjects.Find("KD_PhotoFrame")?.GetComponent<KD_PhotoFrame>();
                            foreach (var r in panelT.GetComponentsInChildren<MeshRenderer>(true))
                            {
                                if (r.gameObject.layer != UiLayer) offLayer++;
                                string expectedShader = frame != null && r == frame.viewerOverlay
                                    ? "KominkaDogRun/KD_PhotoFrameViewer" : ViewerShaderName;
                                if (r.sharedMaterial == null || r.sharedMaterial.shader == null
                                    || r.sharedMaterial.shader.name != expectedShader) wrongShader++;
                                v.Check(r.GetComponent<Collider>() == null, "viewer " + r.name + " has no collider");
                            }
                            v.Check(offLayer == 0,
                                $"viewer renderers on layer {UiLayer} (UI = 撮影カメラに写らない。外れ {offLayer})");
                            v.Check(wrongShader == 0, $"viewer renderers use {ViewerShaderName} (外れ {wrongShader})");
                            var bq = panelT.Find("Backdrop");
                            var pqr = panelT.Find("Photo");
                            var bm = bq == null ? null : bq.GetComponent<MeshRenderer>().sharedMaterial;
                            var pm = pqr == null ? null : pqr.GetComponent<MeshRenderer>().sharedMaterial;
                            v.Check(bm != null && pm != null && pm.renderQueue > bm.renderQueue,
                                $"viewer Photo の renderQueue > Backdrop (ZTest Always なので順序が全て。" +
                                $"photo {(pm == null ? -1 : pm.renderQueue)} / back {(bm == null ? -1 : bm.renderQueue)})");
                            var pq = panelT.Find("Photo");
                            v.Check(pq != null && Mathf.Abs(pq.localScale.x / Mathf.Max(pq.localScale.y, 1e-6f) - 16f / 9f) < 0.01f,
                                "viewer Photo は 16:9 (スロット RT と同じ比)");
                        }
                    }

                    // BUG-GAL-001: 判定領域 (板の矩形 + 余白) が隣接板と重ならないことを
                    // シーンの実配置から検査する。**視線と平面の交点は 1 点**なので、
                    // 領域が重なっていなければ同時吸着は起こりえない — 距離に依存しない。
                    // 旧・円錐判定の「窓が空か」検査を置き換えるもの (こちらは網羅かつ厳密)。
                    int overlaps = 0, nonCoplanar = 0;
                    float tightest = float.MaxValue;
                    for (int i = 0; i < gallery.boards.Length; i++)
                    {
                        var bi = gallery.boards[i] == null ? null : gallery.boards[i].transform;
                        if (bi == null) continue;
                        float hwI = Mathf.Abs(bi.lossyScale.x) * 0.5f + AttachAimMargin;
                        float hhI = Mathf.Abs(bi.lossyScale.y) * 0.5f + AttachAimMargin;
                        for (int j = i + 1; j < gallery.boards.Length; j++)
                        {
                            var bj = gallery.boards[j] == null ? null : gallery.boards[j].transform;
                            if (bj == null) continue;
                            float hwJ = Mathf.Abs(bj.lossyScale.x) * 0.5f + AttachAimMargin;
                            float hhJ = Mathf.Abs(bj.lossyScale.y) * 0.5f + AttachAimMargin;
                            Vector3 d = bj.position - bi.position;
                            // この検査は「同一平面 + 面内の向きも揃っている」ことを前提に
                            // 軸への射影で重なりを見ている。法線が同じでも**面内で回転**して
                            // いると射影が分離軸にならないので、前提から外れるものは
                            // 別に数えて可視化する (Codex 指摘)
                            if (Mathf.Abs(Vector3.Dot(d, bi.forward)) > 0.001f ||
                                Vector3.Angle(bi.forward, bj.forward) > 0.5f ||
                                Vector3.Angle(bi.right, bj.right) > 0.5f) { nonCoplanar++; continue; }
                            float gapX = Mathf.Abs(Vector3.Dot(d, bi.right)) - (hwI + hwJ);
                            float gapY = Mathf.Abs(Vector3.Dot(d, bi.up)) - (hhI + hhJ);
                            float gap = Mathf.Max(gapX, gapY); // どちらかの軸で離れていれば重ならない
                            if (gap < 0f) overlaps++;
                            tightest = Mathf.Min(tightest, gap);
                        }
                    }
                    v.Check(overlaps == 0,
                        $"aim rects free of adjacent-board overlap " +
                        $"(margin {AttachAimMargin}m, tightest gap {tightest:F4}m, overlaps {overlaps})");
                    v.Check(nonCoplanar == 0,
                        "all boards coplanar and axis-aligned " +
                        $"(交点が 1 点である前提 + 重なり検査の前提。out-of-assumption pairs {nonCoplanar})");
                }
            }
            if (director != null)
            {
                v.Check(director.photoCapture == capture && director.photoCamera == cam &&
                    director.gallery == gallery, "director core wiring");
                // IMP-CAM-003: 頭アンカーはフレーミングの体格測定に使うので必須。
                // 遮蔽レイのマスクにプレイヤー(9)/PlayerLocal(10) が入っていないこと
                int mask = director.cameraBlockMask;
                v.Check((mask & (1 << 0)) != 0, "cameraBlockMask includes Default(0)");
                v.Check((mask & ((1 << 9) | (1 << 10))) == 0,
                    "cameraBlockMask excludes Player(9)/PlayerLocal(10) (被写体で遮蔽判定させない)");

                // ---- IMP-CAM-002: 4 匹ぶんの配線 ----
                //
                // **かつてここは「Primary (コーギー) を指していること」を検査していた。**
                // つまり検査そのものが単一犬を固定していて、ポメ・ラブ・ダックスが
                // 1 枚も撮られない状態を「PASS」と言い続けていた。
                // 今は**全犬ぶん**を見る。5 本の配列は長さがそろっていなければならず
                // (ずれていると Director は Start で自分を止める)、
                // **同じ添字が同じ犬を指していなければならない** —
                // 多頭で最も起きやすい壊れ方は「fetch は犬 A・pet は犬 B」で、
                // 個別の null チェックだけでは素通りしてしまう
                var defs = KD_DogDefs.All;
                int dogN = defs.Length;
                v.Check(director.dogRoots != null && director.dogRoots.Length == dogN,
                    $"director.dogRoots.Length == {dogN} (actual " +
                    (director.dogRoots == null ? "null" : director.dogRoots.Length.ToString()) + ")");
                v.Check(director.dogHeadAnchors != null && director.dogHeadAnchors.Length == dogN,
                    $"director.dogHeadAnchors.Length == {dogN}");
                v.Check(director.fetchControllers != null && director.fetchControllers.Length == dogN,
                    $"director.fetchControllers.Length == {dogN}");
                v.Check(director.petControllers != null && director.petControllers.Length == dogN,
                    $"director.petControllers.Length == {dogN}");
                // 徘徊は Wander System Setup が入れる。**長さは Photo Setup がそろえる**ので
                // ここが違えば「Photo Setup を流していない」か「犬定義を足して流し直していない」
                v.Check(director.wanderControllers != null && director.wanderControllers.Length == dogN,
                    $"director.wanderControllers.Length == {dogN} (Wander System Setup が中身を入れる)");

                if (director.dogRoots != null && director.dogRoots.Length == dogN
                    && director.dogHeadAnchors != null && director.dogHeadAnchors.Length == dogN
                    && director.fetchControllers != null && director.fetchControllers.Length == dogN
                    && director.petControllers != null && director.petControllers.Length == dogN)
                {
                    for (int i = 0; i < dogN; i++)
                    {
                        var ddef = defs[i];
                        string tag = ddef.key + ": ";
                        int dc, fc, pc;
                        var ddog = KD_SceneObjects.Find(ddef.DogName, out dc);
                        var dfetchGo = KD_SceneObjects.Find(ddef.FetchSystemName, out fc);
                        var dpetGo = KD_SceneObjects.Find(ddef.PetSystemName, out pc);
                        var dfetch = dfetchGo == null ? null : dfetchGo.GetUdonSharpComponent<KD_DogFetchController>();
                        var dpet = dpetGo == null ? null : dpetGo.GetUdonSharpComponent<KD_DogPetController>();

                        v.Check(ddog != null && director.dogRoots[i] == ddog.transform,
                            tag + $"dogRoots[{i}] == {ddef.DogName}");
                        v.Check(director.fetchControllers[i] != null && director.fetchControllers[i] == dfetch,
                            tag + $"fetchControllers[{i}] == {ddef.FetchSystemName}");
                        v.Check(director.petControllers[i] != null && director.petControllers[i] == dpet,
                            tag + $"petControllers[{i}] == {ddef.PetSystemName}");
                        v.Check(ddog != null && director.dogHeadAnchors[i] != null &&
                            director.dogHeadAnchors[i].IsChildOf(ddog.transform),
                            tag + $"dogHeadAnchors[{i}] が {ddef.DogName} の配下 (体格の逆算に必須)");
                        // **逆配線が全犬あること。** ここが Primary だけだったのが
                        // 「4 匹のうち 3 匹が写らない」の直接の原因だった
                        v.Check(dfetch != null && dfetch.photoDirector == director,
                            tag + "fetch.photoDirector wired back");
                        // **番号が名簿と同じであること。** 通知側は自分の dogIndex を
                        // そのまま渡すので、ここがずれると「ポメの受け渡しでラブを撮る」になる。
                        // dogIndex を配るのは Dog Pack Setup なので、赤いときはそれを流す
                        v.Check(dfetch != null && dfetch.dogIndex == i,
                            tag + $"fetch.dogIndex == {i} (actual {(dfetch == null ? -99 : dfetch.dogIndex)})" +
                            " — 撮影の配列の添字と同じでなければならない (Dog Pack Setup が配る)");
                        // 徘徊は無くても撮影自体は動く (受け渡し・撫では撮れる) ので WARN 相当。
                        // ただし居るなら同じ犬を指していること
                        var dw = director.wanderControllers == null || i >= director.wanderControllers.Length
                            ? null : director.wanderControllers[i];
                        if (dw == null)
                            log.AppendLine("  note " + tag +
                                "wanderControllers が空 — 徘徊の走りと じゃれ合いが撮れない (Wander System Setup を実行する)");
                        else
                        {
                            v.Check(ddog != null && dw.dogRoot == ddog.transform,
                                tag + $"wanderControllers[{i}] が {ddef.DogName} を指している");
                            v.Check(dw.dogIndex == i,
                                tag + $"wander.dogIndex == {i} (actual {dw.dogIndex})");
                            v.Check(dw.photoDirector == director, tag + "wander.photoDirector wired back");
                        }
                    }
                }

                // **proxy ではなく backing UdonBehaviour を直接見る** (vrc-verification: 独立チャネル)。
                // CopyProxyToUdon が漏れると proxy 側だけ正しく見え、実行時は 5 本とも null =
                // Director が Start で自分を止める = **撮影がまるごと消える**。
                // 起動ログには出るが、シーンを触った人が気づくのは Play のあとになる
                var dirUbs = sysGo.GetComponents<VRC.Udon.UdonBehaviour>();
                VRC.Udon.UdonBehaviour dirUb = null;
                foreach (var ub in dirUbs)
                    if (ub.programSource != null && ub.programSource.name == "KD_PhotoDirector") dirUb = ub;
                v.Check(dirUb != null, "backing UdonBehaviour (KD_PhotoDirector) がある");
                if (dirUb != null && dirUb.publicVariables != null)
                {
                    foreach (var vn in new[] { "dogRoots", "dogHeadAnchors", "fetchControllers",
                                               "petControllers", "wanderControllers" })
                    {
                        object raw;
                        bool got = dirUb.publicVariables.TryGetVariableValue(vn, out raw);
                        var arr = raw as System.Array;
                        v.Check(got && arr != null && arr.Length == dogN,
                            $"backing {vn}.Length == {dogN} (actual " +
                            (!got ? "missing" : (arr == null ? "null" : arr.Length.ToString())) +
                            ") — 漏れていると Director は Start で自分を止める");
                    }
                }

                // カテゴリの正本 (IMP-GAL-004 が列ラベルに使う) が枠数と一致していること
                v.Check(SlotGroupNames.Length == SlotGroupCount,
                    $"SlotGroupNames.Length == SlotGroupCount ({SlotGroupNames.Length} / {SlotGroupCount})");
                // IMP-INF-001 / D: SlotGroupNames の並びは KD_PhotoDirector.Group* の番号に
                // 対応している (コメントでの約束)。番号の並びが動いたらここで捕まえる
                // IMP-CAM-005: 犬目線＋かけっこ = 2 / 庭ごと＋じゃれ合い = 3 に畳んである
                v.Check(KD_PhotoDirector.GroupHandoff == 0 && KD_PhotoDirector.GroupPet == 1
                    && KD_PhotoDirector.GroupDogEye == 2 && KD_PhotoDirector.GroupChase == 2
                    && KD_PhotoDirector.GroupPlay == 3
                    && KD_PhotoDirector.GroupWide == KD_PhotoDirector.GroupCount - 1,
                    "KD_PhotoDirector.Group* が SlotGroupNames の並び (0..3) と一致");
                log.AppendLine("  note スロットのカテゴリ (出来事) = " + string.Join(" / ", SlotGroupNames));
            }

            var desc = Object.FindObjectOfType<VRCSceneDescriptor>();
            v.Check(desc != null, "VRCSceneDescriptor exists");
            if (desc != null && desc.NetworkIDCollection != null)
            {
                int broken = 0;
                foreach (var pair in desc.NetworkIDCollection)
                    if (pair == null || pair.gameObject == null) broken++;
                v.Check(broken == 0, $"NetworkIDs broken refs == 0 (actual {broken})");
            }

            v.Finish(VerifyLogFile);
        }
    }
}
