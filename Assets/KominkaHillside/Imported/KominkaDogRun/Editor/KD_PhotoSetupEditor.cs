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
    /// <summary>
    /// IMP-CAM-001 / IMP-GAL-001: 撮影 + ギャラリーのセットアップと検証。
    ///
    /// 実行順 (別実行にする — Program Asset 直後の CopyProxyToUdon は outdated error):
    ///   1 - Create Program Assets → force refresh
    ///   2 - Setup Photo System
    ///   3 - Verify Photo System
    ///
    /// 前提: Fetch System (KD_FetchSystem) と Pet System (KD_PetSystem) が構築済み。
    ///
    /// ギャラリーの看板は共通背景とTMP文字。文言はUITranslationsと固定キー接続表が正本。
    /// </summary>
    public static partial class KD_PhotoSetupEditor
    {
        const string ScriptsDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Scripts/";
        static readonly string[] ScriptNames =
            { "KD_PhotoCapture", "KD_PhotoDirector", "KD_GalleryController",
              "KD_PhotoBoard", "KD_PhotoViewer", "KD_PhotoFrame", "KD_PhotoSettings", "KD_PhotoHelp" };

        const string TexturesDir = "Assets/KominkaHillside/Imported/KominkaDogRun/Textures";
        const string ShaderName = "KominkaDogRun/KD_PhotoPrint";

        // ================================================================ BUG-CAM-003 (案 B)
        // 「板をタップ → 目の前に大きく出す → 端末のスクリーンショット」の器。
        // iOS では吸着 (KD_PhotoPrint の全画面化) が真っ黒になり、実機 3 回の切り分けで
        // シェーダー側からは直せないと確定した。PC の吸着は残し、モバイルはこちらで持ち帰る
        const string ViewerName = "KD_PhotoViewer";
        const string ViewerShaderName = "KominkaDogRun/KD_PhotoViewer";
        const string ViewerBackMatPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PhotoViewerBack.mat";
        const string ViewerPhotoMatPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PhotoViewerPhoto.mat";
        const float ViewerDistance = 0.8f;
        // 頭から 0.8m・縦 0.78m = Desktop (縦 FOV 60°) の視界の約 85%。
        // スマホの横長画面 (19.5:9) でも横は 1.39m < 視界幅 2.0m で収まる
        const float ViewerPhotoHeight = 0.78f;
        const float ViewerPhotoWidth = ViewerPhotoHeight * 16f / 9f;
        // 背景は視界を必ず覆う大きさ (PCVR の縦 FOV 100° でも 0.8m 先で縦 1.9m)
        const float ViewerBackWidth = 4f;
        const float ViewerBackHeight = 3f;
        // 描画物は UI(5): 自分のワールド撮影カメラの許可リストに無い = 撮った写真に写り込まない。
        // 当たり判定は Default の Trigger (Interact の光線が確実に当たる + 体が押されない)
        const int UiLayer = 5;
        const float BoardProximity = 2f;
        const int ViewerBackQueue = 4000;   // Overlay
        const int ViewerPhotoQueue = 4001;  // 背景の後に描く (ZTest Always なので順序が全て)
        // 2026-08-11 廃止: 持ち帰り台 (PhotoPrint) と板の Interact (KD_GalleryBoard)。
        // 板から直接持ち帰れる (IMP-GAL-002) ため不要 — Setup が旧構成を掃除する
        const string LegacyPrintMatPath = "Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_PhotoPrint.mat";
        const string LegacyBoardProgramPath = ScriptsDir + "KD_GalleryBoard.asset";
        const int SlotCount = 24;    // 6 段 × 4 列 (IMP-GAL-003 で 20 → 24)
        // **RT 総メモリの実測 (2026-08-14)**: 深度付き 1 枚 = **10.55 MiB**
        //   色 R8G8B8A8_SRGB 3.52 + 深度 D32_SFloat_S8_UInt 7.03。**20 枚で約 211 MiB**。
        // かつて「約 73MB」と書いていたのは**色だけを数えた誤り** (実際は約 3 倍)。
        //
        // IMP-GAL-003: 深度が要るのは「カメラが描いている一瞬」だけで、
        // **撮り終えたスロットは表示用テクスチャにすぎない** = 19/20 枚で死に荷重だった。
        // → **撮影用 RT を 1 枚 (深度付き) に分離し、スロットは深度なし**にする。
        //   24 枚でも 24 × 3.52 + 10.55 = **約 95 MiB** (旧 20 枚 211 MiB の半分以下)。
        //   解像度は落とさない (ユーザー判断。960x540 化は約 142 MiB でこれより重い)。
        const int SlotWidth = 1280;
        const int SlotHeight = 720;
        const int SlotDepthBits = 0;    // スロットは表示専用 — 深度を持たせない
        // 撮影用の 1 枚だけが深度を持つ。**要求 24 に対し実際は 32 が返る** —
        // Unity が D32_SFloat_S8_UInt を確保するため (2026-08-14 実測)。
        // 等値で見ると毎回 retune し続けるので下限で見ること
        const int CaptureDepthBits = 24;
        // IMP-GAL-004: **4 → 6。これで「1 列 = 1 カテゴリ」になる。**
        //
        // スロットの割り当ては `idx = group + k * slotGroupCount` (KD_GalleryController)
        // なので、**`BoardsPerRow == SlotGroupCount` のとき `col = idx % BoardsPerRow` が
        // 恒等に `group` と一致する**。`KD_GalleryController` は 1 行も変えていない。
        //   g=0 → 板 0/6/12/18 → col 0,0,0,0
        //   g=5 → 板 5/11/17/23 → col 5,5,5,5
        //
        // かつて 4 列にして**斜めに散らしていた**のは IMP-CAM-003 の
        // 「壁のどこを見ても絵が揃わない / 数枚しか撮っていない段階でも板が一箇所に固まらない」
        // ため。**列にラベルを貼るならその前提を捨てるしかない** (2026-08-16 ユーザー要望) —
        // 代わりに「空の列 = まだ撮れていない出来事」が案内になる。
        //
        // 間隔 (BoardSpacingX/Y) は変えていないので、**BUG-GAL-001 の
        // 「隣接板の同時吸着が幾何的に起こりえない」は維持される** (Verify が実配置から検査する)
        const int BoardsPerRow = 6;
        // カテゴリ数。スロットはカテゴリごとに間引いて割り当てられ、
        // **上書きは同じカテゴリの中だけ**で回る (ギャラリーが同じ絵で埋まらない)。24 を割り切ること。
        // **BoardsPerRow と同じ値でなければ「列 = カテゴリ」が崩れる** (Verify が見る)
        // 正本は KD_PhotoDirector.GroupCount (IMP-INF-001 / D — ここに 6 を書き写さない)
        const int SlotGroupCount = KD_PhotoDirector.GroupCount;
        // IMP-CAM-005 (2026-09-12 ユーザー決定): **列 → カテゴリの表。** 自分が写る出来事
        // (もってきた / 撫で) に 2 列ずつ、犬目線＋かけっこ と 庭ごと＋じゃれ合い に 1 列ずつ。
        // スロット i のカテゴリは ColumnGroups[i % BoardsPerRow] で、Setup が
        // KD_GalleryController.slotGroups へ配る (器の側は表を持たない)。
        // 列ラベルはカテゴリごとに 1 枚で、2 列ぶんの幅にまたがる。
        public static readonly int[] ColumnGroups = { 0, 0, 1, 1, 2, 3 };
        /// <summary>カテゴリ g が占める列の先頭と本数。</summary>
        static void GroupColumns(int g, out int first, out int span)
        {
            first = -1; span = 0;
            for (int c = 0; c < ColumnGroups.Length; c++)
                if (ColumnGroups[c] == g) { if (first < 0) first = c; span++; }
        }
        /// <summary>カテゴリ g のラベルが何列ぶんの幅か。</summary>
        public static int LabelSpan(int g) { GroupColumns(g, out _, out int span); return span; }

        /// <summary>
        /// スロットのカテゴリ名。**IMP-CAM-002 で「構図」から「出来事」へ切り直した**
        /// (2026-08-16 ユーザー決定)。並びは KD_PhotoDirector の Group* 定数と同じ。
        ///
        /// IMP-CAM-003 は「寄り / 目線 / 俯瞰 …」= 構図で切っていた。同じ絵で埋まらない
        /// という理由は正しかったが、**列に「この列は何が撮られるか」を書けない** —
        /// 「俯瞰」の列にはあらゆる出来事の俯瞰が混ざるため。
        ///
        /// **ここが列ラベルの正本。** 物理レイアウトと板のラベルは IMP-GAL-004 が
        /// これを読んで作る (文字列を向こうに書き写さない — 複製は黙ってズレる)。
        /// </summary>
        /// <remarks>
        /// **表示する言葉と、コードの中の出来事名は別。**
        /// ここは**壁に貼る文字**で、犬の側から見た言い方にしてある (2026-08-16 ユーザー指定 —
        /// 「受け渡し」は業務っぽい)。コード側は Fetch の状態名に合わせて
        /// `GroupHandoff` / 「受け渡し」「Drop」のまま — 仕組みを指す語なので変えない。
        ///
        /// 長い訳文はTMPの自動サイズ調整で収め、Verify Gallery Textで全言語を検査する。
        /// </remarks>
        public static string[] SlotGroupNames
        {
            get { var c=KD_LocalizationEditor.Load();var result=new string[SlotGroupCount];for(int i=0;i<SlotGroupCount;i++)result[i]=KD_LocalizationEditor.Text(c,"ja","gallery.label."+i);return result; }
        }
        const int BoardRows = SlotCount / BoardsPerRow;

        // ---- 板の大きさと間隔 (IMP-GAL-004 で縮めた) ----
        //
        // **6 列にしたら縁側の壁に入らなかった。** 2026-08-16 にシーンで実測:
        // ギャラリーを掛けている壁で家具に遮られない帯は **z 0.93..2.87 = 1.94m** しかなく、
        // 旧寸法 (板 0.64 / 間隔 0.72) の 6 列は **2.12m** で 18cm 溢れていた。
        // 端の 2 列が タンス と ふすま の裏に 1/3 ほど隠れる状態だった。
        //
        // **空き帯は「コライダー」ではなく「レンダラー」で測ること。**
        // 最初はコライダーで測って 1.98m と出し、それに合わせた寸法 (板 0.58 / 間隔 0.65)
        // では**まだ タンス に 1.1cm 食い込んでいた** (ユーザーが見て気付いた)。
        // タンス はコライダーとレンダラーで範囲が違い、当たり判定側の検査をすり抜ける。
        // **見えているかどうかの話なので、見えている形 (Renderer.bounds) が正しい。**
        //
        // **間隔を詰めるだけでは直らない** — 吸着の判定余白 (AttachAimMargin 0.012m) を
        // 保つには実寸で `間隔 - 板幅 > 0.024m` が要るので、板を細くしないと
        // BUG-GAL-001 の「隣接板の同時吸着が起こりえない」が崩れる。
        // → **板ごと 9% 小さくした** (ユーザー判断 2026-08-16)。
        //
        // **16:9 は必ず保つ** — 撮影 RT が 1280x720 なので、比を崩すと写真が歪む。
        // BoardHeight を BoardWidth から導出しているのはそのため (手で両方書かない)。
        //
        // **不変条件: `BoardSpacingX - BoardWidth > 0.048` (実寸で 間隔 - 板幅 > 0.024)。**
        // 吸着の判定余白 (AttachAimMargin) を隣接板と重ねないための下限で、
        // ここを割ると BUG-GAL-001 の「同時吸着が起こりえない」が崩れる。
        // 幅を詰めるときは**板と間隔を一緒に**下げて、この差 0.07 を保つこと。
        //
        // 実寸 (ギャラリー配置スケール 0.5 込み):
        //   板 0.28 × 0.158m / 間隔 横 0.315・縦 0.193m / 隙間 横も縦も 0.035m
        //   全体 横 1.855m × 縦 0.735m
        //   帯 (z 0.93..2.87) に対する余り: ふすま側 6.6cm / タンス側 2.0cm
        //   (ギャラリー自体が帯の中心から 2cm ずれて掛かっているので左右非対称。
        //    掛け位置は手調整なので Setup では動かさない)
        const float BoardWidth = 0.56f;
        const float BoardHeight = BoardWidth * 9f / 16f; // 撮影 RT 1280x720 と同じ比
        const float BoardSpacingX = 0.63f;
        // 縦も詰めて**横と隙間をぴったりそろえる** (どちらも 0.035m)。
        // 吸着の余白は縦横とも 0.011m 残る — **従来の最小 0.006m より広い**
        const float BoardSpacingY = 0.385f;

        // ---- IMP-GAL-004: 列ラベル ----
        // 各列の上に 1 枚ずつ「この列は何が撮られるか」を置く。**文言の正本は SlotGroupNames**。
        // 板と同じ平面に置くが、**吸着シェーダは使わない** (KD_PhotoPrint ではなく Unlit) —
        // ラベルを持ち帰る意味は無いし、判定対象が増えると BUG-GAL-001 の
        // 「交点が 1 点」の議論に他人が混ざる
        const float LabelHeight = 0.16f;      // 板の縦のおよそ半分。実寸 (スケール 0.5) で 0.08m
        const float LabelGap = 0.06f;         // 最上段の板の上端からの隙間
        // 最上段の板の上端 = (BoardRows-1)*BoardSpacingY + BoardHeight/2
        const float LabelLocalY = (BoardRows - 1) * BoardSpacingY + BoardHeight * 0.5f
            + LabelGap + LabelHeight * 0.5f;
        // 既存の看板の寸法を保持する。描画はTMPへ移行済み。
        const float CaptionWidth = 0.9f;
        const float CaptionHeight = CaptionWidth * 3f / 8f;  // 既存看板の縦横比を保持
        const float CaptionGap = 0.06f;
        // Caption は**列ラベルのさらに上**。ラベルを足したぶん持ち上がる
        // (段数と同じく BoardRows からの導出値なので、Setup が毎回そろえる)
        static readonly Vector3 CaptionLocalPos = new Vector3(0f,
            LabelLocalY + LabelHeight * 0.5f + CaptionGap + CaptionHeight * 0.5f, 0f);

        /// <summary>列 col の板の X (ギャラリーローカル)。板もラベルもこれを使う —
        /// 別々に計算すると「ラベルだけ半列ずれる」が起きる。</summary>
        static float ColumnX(int col)
        {
            return (col - (BoardsPerRow - 1) * 0.5f) * BoardSpacingX;
        }

        // IMP-GAL-002: 板から直接持ち帰るための吸着パラメータ。
        // 実寸 (ギャラリー配置スケール 0.5 込み) は 板 0.28 × 0.158m /
        // 間隔 横 0.315m・縦 0.193m = 板の隙間は **縦横とも 0.035m**。
        // **IMP-GAL-004 で 6 列化して板を細くしたぶん、余白の根拠は
        // 「縦の隙間の半分」から「隙間の半分」(縦横同値) になった** — 0.035/2 = 0.0175m。
        //
        // BUG-GAL-001: エイム判定を角度 (cos 15°) から
        // **「視線が板の面のどこに当たるか」**へ替えた。角度判定は近づくほど
        // 許容が縮み、板にくっつけると 1cm のずれで外れていた。
        // 余白は**狭い方の隙間の半分 (横 0.0175m) より小さく**取る — これで隣接板の
        // 判定領域が重ならず、交点は 1 点なので**同時吸着が幾何的に起こりえない**。
        // 実機で調整が要る場合はここを直して Setup を再実行する (Setup が正本)。
        // 重なりが無いことは Verify がシーンの実配置から機械検査する。
        const float BoardAttachDistance = 0.35f;
        const float AttachAimMargin = 0.012f; // 板の縁から 12mm (隙間半分 17.5mm より内側)

        // ================================================================ 撮影カメラの cullingMask
        // BUG-CAM-001 (2026-08-14 実機): メニュー UI と個人用ミラーが写真に写り込んだ。
        //
        // **原因は 2 つ。**
        // 1. **除外リスト方式だった** — `~((1<<3)|(1<<10)|...)` と書いていたので、
        //    列挙し忘れたレイヤーは**全部写る**。パッケージが後から UI を置くと必ず漏れる
        // 2. **レイヤー番号を間違えていた** — 旧コメントは「InternalUI(3) / PostProcessing(19)」
        //    だが、このプロジェクトの実際は **3 = Item / 19 = InternalUI**
        //    (VRChat SDK が設定する番号。2026-08-14 に TagManager から実測)
        //
        // 実測で写り込んでいたもの:
        //   - **layer 2 Ignore Raycast** — QvPen の UI が **44 個 active**
        //     (Clear / ClearAll / Respawn ボタン)。これが「メニュー」
        //   - **layer 4 Water** — `LocalMirrorSetSwitch/.../Mirror` が active。
        //     これが「パーソナルミラー」(古民家アセット付属の個人用ミラー)。
        //     **ミラー側のレイヤーは触らない** — VRChat のミラーは描画の仕組みが
        //     レイヤーに依存するため、動かすと反射が壊れる。カメラ側で外す
        //
        // → **許可リスト方式へ変える。** 「写したいものを挙げる」形なら、
        //   知らないレイヤーに何が置かれても写り込まない。
        static readonly int[] PhotoLayers =
        {
            0,  // Default          — 古民家・犬・ギャラリーの板 (本体)
            8,  // Interactive      — ワールドの操作対象
            9,  // Player           — **他のプレイヤー** (受け渡しの構図に要る)
            11, // Environment      — ワールドの地形
            13, // Pickup           — **ボール** (Fetch の写真に要る)
            14, // PickupNoEnvironment
            17, // Walkthrough      — すり抜ける地形
            18, // MirrorReflection — **自分のアバター**。PlayerLocal(10) だと
                //                    自分が被写体のとき顔が欠ける (KanjiFind IMP-SC-003 実測)
        };
        // 意図的に外しているもの (番号は 2026-08-14 に実測):
        //   1 TransparentFX / 2 Ignore Raycast (**QvPen の UI**) / 3 Item /
        //   4 Water (**個人用ミラー**。このワールドに水面は無い — 足すときは要再検討) /
        //   5 UI / 6,7 reserved / 10 PlayerLocal (顔が欠ける) / 12 UiMenu /
        //   15,16 Stereo / 19 InternalUI / 20 HardwareObjects / 21+ reserved
        static int PhotoCullingMask
        {
            get
            {
                int m = 0;
                foreach (var l in PhotoLayers) m |= 1 << l;
                return m;
            }
        }

        static string DescribeMask(int mask)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                string n = LayerMask.LayerToName(i);
                sb.Append(i).Append(':').Append(string.IsNullOrEmpty(n) ? "?" : n);
            }
            return sb.ToString();
        }

        // BUG-CAM-001: 写真に写ることを**許容した** UI の置き場 (購入アセットの非 CC0 群)。
        // ここに居るのは VizVid の操作パネルで、Default / Pickup にあるため
        // レイヤーでは外せない。2026-08-14 ユーザー判断で「そのまま」。
        // ここを名指しにしてあるので、**別の UI が増えたときは Verify が FAIL する**
        const string AcceptedUiRoot = "==非CC0アセット用==";

        const string SystemName = KD_DogDefs.PhotoSystemName;   // IMP-INF-003: 正本は KD_DogDefs
        // IMP-CAM-002: 犬は KD_DogDefs.All 全部。頭アンカーの名前は全犬共通
        // (KD_PetSystem Setup が同じ名前で作る)
        const string HeadAnchorName = "KD_PetAnchor_Head";

        const string CaptureRTPath = TexturesDir + "/KD_PhotoCaptureRT.renderTexture";

        static string SlotRTPath(int i) => $"{TexturesDir}/KD_PhotoSlot{i}.renderTexture";
        static string SlotMatPath(int i) => $"Assets/KominkaHillside/Imported/KominkaDogRun/Materials/M_KD_GallerySlot{i}.mat";

        /// <summary>3 - Verify の記録先。</summary>
        const string VerifyLogFile = "Logs/imp-cam-001-verify.txt";

        // ================================================================ 1: Program assets

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo System/1 - Create Program Assets")]
        public static void CreateProgramAssets()
        {
            var log = new StringBuilder();
            log.AppendLine("[KD_PhotoSetup] Create program assets");
            bool ok = true;
            foreach (var name in ScriptNames)
            {
                string scriptPath = ScriptsDir + name + ".cs";
                string programPath = ScriptsDir + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath) != null)
                {
                    log.AppendLine("already exists: " + programPath);
                    continue;
                }
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (script == null)
                {
                    log.AppendLine("FAIL script not found: " + scriptPath);
                    ok = false;
                    continue;
                }
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                AssetDatabase.CreateAsset(asset, programPath);
                log.AppendLine("created: " + programPath);
            }
            AssetDatabase.SaveAssets();
            log.AppendLine(ok ? "RESULT: PASS (force refresh してから Setup を実行する)" : "RESULT: FAIL");
            KD_VerifyLog.Write("Logs/imp-cam-001-program.txt", log);
        }

        // ================================================================ BUG-CAM-003
        // 吸着プローブ。**実機でしか出ない黒画面を 1 回のアップロードで切り分ける**ための
        // 一時的な目印で、製品ビルドでは必ず OFF。
        //
        // 実機でしか再現しない不具合は、1 回ビルドして上げるたびに仮説が 1 つしか
        // 潰せないのが一番高くつく。**帯を 4 本出して 1 回で全部答えさせる。**
        //
        // **ON のままの検査は Photo System の Verify ではなく Release Check に置く。**
        // 切り分け中は ON が正常な状態なので、Verify に混ぜると常時 FAIL になって
        // 検査全体への信頼を毀損する (KD_ReleaseCheckEditor の設計判断と同じ)
        const string DebugProbeProp = "_DebugProbe";

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo System/4 - Debug Snap Probe ON")]
        public static void DebugProbeOn() { SetDebugProbe(true); }

        [MenuItem("Tools/KominkaHillside/Menu/Imported-Unused/Photo System/5 - Debug Snap Probe OFF")]
        public static void DebugProbeOff() { SetDebugProbe(false); }

        static void SetDebugProbe(bool on)
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(SlotMatPath(i));
                if (mat == null || !mat.HasProperty(DebugProbeProp)) continue;
                mat.SetFloat(DebugProbeProp, on ? 1f : 0f);
                EditorUtility.SetDirty(mat);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[KD_PhotoSetup] debug probe {(on ? "ON" : "OFF")} -> {n}/{SlotCount} materials"
                + (on ? " — **公開前に OFF へ戻すこと** (Release Check/1 が検査する)" : ""));
        }

        /// <summary>吸着プローブが ON のスロット材質の数 (KD_ReleaseCheckEditor が呼ぶ)。
        /// スロット数と材質のパスはこのファイルが正本なので、数える側もここに置く。</summary>
        internal static int CountDebugProbeOn()
        {
            int probeOn = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(SlotMatPath(i));
                if (mat != null && mat.HasProperty(DebugProbeProp)
                    && mat.GetFloat(DebugProbeProp) > 0.5f) probeOn++;
            }
            return probeOn;
        }

        // ================================================================ helpers

        static bool TryCopy(UdonSharpBehaviour proxy, StringBuilder log)
        {
            try
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                EditorUtility.SetDirty(proxy);
                return true;
            }
            catch (System.Exception e)
            {
                log.AppendLine("CopyProxyToUdon FAILED (" + proxy.GetType().Name + "): " + e.Message);
                return false;
            }
        }

        static Transform FindDescendant(GameObject root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
