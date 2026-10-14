using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KominkaDogRun;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-UI-008 B: 犬モデルの作者 TORINOSUKEの家 (torinosukeShop) の紹介ポスターと、バニマル (VAnimal) のグループポスターを、
    /// 古民家の塀 (KH_World/KH_Places/KH_FarKominka/KH_KominkaWall/外壁) の外側の面へ並べて掛ける。
    ///
    /// 古民家ドッグランの現物 (2026-10-14 に読んだ):
    ///   - torinosuke: KD_AuthorPosterEditor (IMP-ENV-023)。購入プレハブ「ポスターボード」(Atelier_Fuyutsuki) に画像 torinosukenoie.png 0.354 × 0.50 m、
    ///     その下に URL のコピー欄 (KD_MenuFinishEditor.CopyField、https://torinosuke05.booth.pm/items、0.40 × 0.035 m、22pt、読み取り専用)。見出しなし。
    ///   - グループ: シーン直下の GroupPageOpener (MimyLab、購入物で Git 管理外) に Data/Panfret/poster.png (1587 × 2245) の Sprite、
    ///     画像 0.635 × 0.898 m、Interact (表示「Open Group」) で VRChat のグループページを開く (grp_45df2c16-…)。
    /// 裏山には購入プレハブも MimyLab も無い (コピーしない) ので、同じ寸法の画像を自作の木の枠 (案内板と同じ KH_RiverWood の板、Collider なし) と
    /// 読むだけの World Space Canvas の RawImage で見せ、URL 欄は同じ CopyField、グループのページは KH_GroupPoster (U#、ローカル) で開く。
    /// RawImage (UI/Default、unlit) なので、古民家が Android / iOS で踏んだ lilToon の白飛び (BUG-ENV-003 / ポスターボードの透明材質の差し替え) は起きない。
    ///
    /// 置き場所 (ユーザー依頼「古民家の壁面の、ポータルが出るほうと反対側」): 門 (KH_GateBlock) の枠で見て、案内板 Kominka のポータル (PortalAnchor) と
    /// 反対の側の塀の外面。初回だけ物理で探す: 門の中心から塀に沿って 0〜12 m (0.1 m 刻み) を、スポーン側 3 m から塀へレイを撃って面を見つけ、
    /// ポスター 2 枚の範囲が平らな塀に掛かるか (格子 0.1 m のレイ、±3 cm)、その前に固い Collider が無いか、ほかの看板・写真ボードの足元と読む場所に
    /// 掛からないかで判定する (下の WallRules)。写真ボード KH_PhotoBoard の手前 (門と写真ボードの間) と奥 (写真ボードの先) の 2 区間で
    /// 全規則を満たす最初の点をそれぞれログに出し、手前を優先して置く。どちらも無ければ、固い規則だけ満たす最初の点に置いて破った規則を WARN で出す
    /// (Guide/21 は FAIL にしない。ユーザーが Play で見て決める)。以後 x / z / yaw は人のもの (2026-10-14 ユーザー決定: 人が動かした位置は規則を破っても動かさず、Guide/20・21 は WARN。探し直すには KH_WallPosters を消す)。
    /// 2026-10-14 ユーザー「大きさをポスターに合わせて」: torinosuke の画像もグループと同じ 0.635 × 0.898 m、URL 欄 0.60 × 0.044 m (28pt)。
    ///
    /// Guide/20 Build Shop Intro And Poster: 保存済みの本番シーンを Temp/kh-guide-boards/before-wall-posters-*.unity へ写してから作る。
    /// Guide/21 Verify Shop Intro And Poster: シーンを変えない。Temp/kh-guide-boards/wall-posters-verify.json、ログ 1 行 WALL_POSTERS_OK / WALL_POSTERS_FAIL。</summary>
    public static partial class KH_GuideBoardsEditor
    {
        public const string WallPostersName = "KH_WallPosters";
        public const string TorinosukePosterName = "Torinosuke", GroupPosterName = "GroupPoster";
        public const string PosterTextureDir = "Assets/KominkaHillside/Textures/Posters/";
        public const string TorinosukeTexturePath = PosterTextureDir + "KH_TorinosukePoster.png";   // = 古民家 Assets/_Originals/torinosuke/torinosukenoie.png (1241 × 1754)
        public const string GroupTexturePath = PosterTextureDir + "KH_VAnimalGroupPoster.png";       // = 古民家 Assets/KominkaDogRun/Data/Panfret/poster.png (1587 × 2245)
        public const string TorinosukeUrl = "https://torinosuke05.booth.pm/items";                   // = 古民家 KD_AuthorPosterEditor.Url / クレジットの犬モデルの行
        public const string VAnimalGroupId = "grp_45df2c16-4e9f-4722-8025-d08cf4f1211b";             // = 古民家のシーンの GroupPageOpener._groupId
        public const string GroupPosterProgramPath = "Assets/KominkaHillside/Scripts/KH_GroupPoster";
        public const string GroupInteractPath = GroupPosterName + "/Interact", GroupInteractKey = "guide.wall.group.interact";
        const string WallObjectPath = "KH_World/KH_Places/KH_FarKominka/KH_KominkaWall", WallColliderName = "外壁";
        const string WallBuildReportPath = OutDir + "/wall-posters-build.json", WallVerifyReportPath = OutDir + "/wall-posters-verify.json";
        const int TorinosukeSrcW = 1241, TorinosukeSrcH = 1754, GroupSrcW = 1587, GroupSrcH = 2245;

        // ---------------------------------------------------------------- 寸法 (m)。画像は古民家と同じ大きさ
        // 2026-10-14 ユーザー「大きさをポスターに合わせて」: torinosuke もグループポスターと同じ高さ 0.898 m (縦横比はそのまま、0.635 x 0.898)。
        // 古民家は 0.354 x 0.50 (KD_AuthorPosterEditor)。URL 欄も画像の幅に合わせて広げた (1 px = 0.8 mm は古民家のまま)
        static readonly Vector2 TorImageM = new Vector2(0.898f * TorinosukeSrcW / TorinosukeSrcH, 0.898f);
        static readonly Vector2 GroupImageM = new Vector2(GroupSrcW * 0.0004f, GroupSrcH * 0.0004f);        // 0.635 x 0.898 (古民家: sprite 100 px/unit x 0.08 x 0.5)
        static readonly Vector2 UrlPx = new Vector2(750f, 55f);                                             // 1 px = 0.8 mm (古民家と同じ) -> 0.60 x 0.044 m, 28pt (古民家 500 x 44 / 22pt を画像に合わせて拡大)
        const float UrlScale = 0.0008f, UrlPoints = 28f, UrlGap = 0.0075f;
        const float FrameBorder = 0.03f, PlankDepth = 0.02f, PlankGap = 0.01f, PosterGap = 0.20f;
        /// <summary>画像の中心の高さ (読む場所の地面から)。塀が低ければ順に下げる。立って読む目の高さ 1.5〜1.6、座った VR 約 1.15 の間。</summary>
        static readonly float[] PosterCentreHeights = { 1.35f, 1.30f, 1.25f, 1.20f };
        const float PosterReaderNear = 0.3f, PosterReaderFar = 1.5f, PosterFrontZone = 0.3f, WallFlatTol = 0.03f;
        const float WallSearchMax = 12f, WallSearchStep = 0.1f, WallProbeOut = 3f, PosterGroundOut = 0.5f;
        const float GroupInteractProximity = 2.5f;

        static float UrlW { get { return UrlPx.x * UrlScale; } }
        static float UrlH { get { return UrlPx.y * UrlScale; } }
        static float TorFrameW { get { return Mathf.Max(TorImageM.x, UrlW) + 2f * FrameBorder; } }
        static float TorUrlBelow { get { return TorImageM.y * 0.5f + UrlGap + UrlH * 0.5f; } }       // 画像の中心 -> URL 欄の中心
        static float TorFrameTop { get { return TorImageM.y * 0.5f + FrameBorder; } }                // 画像の中心からの上端
        static float TorFrameBottom { get { return -(TorUrlBelow + UrlH * 0.5f + FrameBorder); } }  // 画像の中心からの下端 (負)
        static float GroupFrameW { get { return GroupImageM.x + 2f * FrameBorder; } }
        static float GroupFrameHalfH { get { return GroupImageM.y * 0.5f + FrameBorder; } }
        static float PostersW { get { return TorFrameW + PosterGap + GroupFrameW; } }
        static float PostersTop(float h) { return h + Mathf.Max(TorFrameTop, GroupFrameHalfH); }
        static float PostersBottom(float h) { return h + Mathf.Min(TorFrameBottom, -GroupFrameHalfH); }
        /// <summary>torinosuke の板の中心 x (根のローカル)。side = +1 なら根の +X (読む人の右) が門の側で、torinosuke を門寄りに置く。</summary>
        static float TorX(float side) { return side * (PostersW * 0.5f - TorFrameW * 0.5f); }
        static float GroupX(float side) { return -side * (PostersW * 0.5f - GroupFrameW * 0.5f); }

        class WallCtx
        {
            public Ctx c;                      // scene, pouch, language, font, catalog, wood, root (KH_GuideBoards), manager
            public Transform gate, spawn, kominka, portalAnchor, photo; public Vector3 gr, gf;
            public MeshCollider wall;
            public Texture2D torTex, groupTex;
            public Transform existing;
        }

        class WallPose
        {
            public Vector3 pos; public float yaw, h, along, outward;
            public string region;
            public List<string> hard = new List<string>(), soft = new List<string>();
            public string At() { return V(pos) + " yaw " + yaw.ToString("F1") + " (along " + along.ToString("+0.00;-0.00") + " m, out " + outward.ToString("F2") + " m, image centre " + h.ToString("F2") + " m)"; }
        }

        // ================================================================ context

        static WallCtx ResolveWall(bool forBuild)
        {
            var w = new WallCtx();
            var c = new Ctx { scene = EditorSceneManager.GetActiveScene() };
            w.c = c;
            var pouches = UnityEngine.Object.FindObjectsOfType<KD_TreatPouch>(true);
            if (pouches.Length != 1) throw new InvalidOperationException("exactly one KD_TreatPouch required (" + pouches.Length + "); nothing changed");
            c.pouch = pouches[0]; c.language = c.pouch.language;
            if (c.language == null) throw new InvalidOperationException("KD_TreatPouch.language not wired (Menu/11); nothing changed");
            c.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KD_ShopSetupEditor.SharedFontPath);
            if (c.font == null) throw new InvalidOperationException(KD_ShopSetupEditor.SharedFontPath + " missing; nothing changed");
            c.catalog = KD_LocalizationEditor.Load();
            c.wood = AssetDatabase.LoadAssetAtPath<Material>(WoodMatPath);
            if (c.wood == null) throw new InvalidOperationException(WoodMatPath + " missing; nothing changed");
            var managers = UnityEngine.Object.FindObjectsOfType<KH_LeashManager>(true);
            c.manager = managers.Length == 1 ? managers[0] : null;
            var guide = c.scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray();
            c.root = guide.Length == 1 ? guide[0].transform : null;
            if (c.root == null) throw new InvalidOperationException(RootName + " must exist once at the scene root (Guide/1): the posters are placed against its Kominka board and portal; nothing changed");
            w.gate = ScenePath(c.scene, "KH_World/KH_Places/KH_GateBlock");
            w.spawn = ScenePath(c.scene, "KH_World/KH_Places/KH_Entrance");
            w.kominka = c.root.Find(Kominka.name);
            w.portalAnchor = w.kominka != null ? w.kominka.Find(PortalAnchorName) : null;
            var photo = KD_SceneObjects.Find("KH_PhotoBoard");
            w.photo = photo != null ? photo.transform : null;
            if (w.gate == null || w.spawn == null) throw new InvalidOperationException("KH_GateBlock / KH_Entrance missing; nothing changed");
            if (w.kominka == null || w.portalAnchor == null) throw new InvalidOperationException(RootName + "/Kominka/PortalAnchor missing (Guide/1): the portal's side decides the posters' side; nothing changed");
            w.gr = Flat3(w.gate.right).normalized; w.gf = Flat3(w.gate.forward).normalized;
            if (Vector3.Dot(w.spawn.position - w.gate.position, w.gf) < 0f) w.gf = -w.gf;   // "out" = the spawn's side (as FindKominkaPose)
            var wallRoot = ScenePath(c.scene, WallObjectPath);
            var walls = wallRoot != null ? wallRoot.GetComponentsInChildren<MeshCollider>(true).Where(m => m.name == WallColliderName && m.enabled && !m.isTrigger).ToArray() : new MeshCollider[0];
            if (walls.Length != 1) throw new InvalidOperationException(walls.Length + " MeshCollider named " + WallColliderName + " under " + WallObjectPath + " (expected 1: the farmhouse wall); nothing changed");
            w.wall = walls[0];
            if (forBuild && AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(GroupPosterProgramPath + ".asset") == null)
                throw new InvalidOperationException(GroupPosterProgramPath + ".asset missing: run Guide/0, compile U# (unity-compile.sh --udon), then Guide/20; nothing changed");
            w.torTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TorinosukeTexturePath);
            w.groupTex = AssetDatabase.LoadAssetAtPath<Texture2D>(GroupTexturePath);
            if (w.torTex == null || w.groupTex == null) throw new InvalidOperationException("poster textures missing (" + TorinosukeTexturePath + " / " + GroupTexturePath + "; copied from Kominka Dog Run, IMP-UI-008); nothing changed");
            var roots = c.scene.GetRootGameObjects().Where(g => g.name == WallPostersName).ToArray();
            if (roots.Length > 1) throw new InvalidOperationException(roots.Length + " " + WallPostersName + " at the scene root (keep one); nothing changed");
            KD_SceneObjects.Find(WallPostersName, out int anywhere);
            if (anywhere != roots.Length) throw new InvalidOperationException(WallPostersName + " exists below another object (move it to the scene root); nothing changed");
            w.existing = roots.Length == 1 ? roots[0].transform : null;
            // texts and bindings this builder relies on
            foreach (var lang in c.catalog.languages)
            {
                var e = lang.texts.FirstOrDefault(x => x.key == GroupInteractKey);
                if (lang.code == c.catalog.fallbackLanguage && (e == null || string.IsNullOrWhiteSpace(e.text))) throw new InvalidOperationException(GroupInteractKey + " missing in " + KD_MenuFinishEditor.CatalogPath + "; nothing changed");
            }
            var manifest = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            if (!(manifest.interactions ?? new KD_LocalizationEditor.Binding[0]).Any(b => b.root == WallPostersName && b.path == GroupInteractPath && b.key == GroupInteractKey))
                throw new InvalidOperationException("interaction binding missing in " + KD_LocalizationEditor.BindingsPath + ": " + WallPostersName + "/" + GroupInteractPath + " = " + GroupInteractKey + "; nothing changed");
            return w;
        }

        // ================================================================ where: the wall face, the rules

        static float Along(WallCtx w, Vector3 p) { return Vector3.Dot(Flat3(p - w.gate.position), w.gr); }
        static float Outward(WallCtx w, Vector3 p) { return Vector3.Dot(Flat3(p - w.gate.position), w.gf); }
        /// <summary>+1 / -1: the side of the gate (along the wall) the portal is on.</summary>
        static float PortalAlongSign(WallCtx w) { return Along(w, w.portalAnchor.position) >= 0f ? 1f : -1f; }

        /// <summary>The posters' rect on the wall face is covered by the wall (every 0.1 m ray from 1 m in front hits it 1 m away, +-WallFlatTol).</summary>
        static bool WallCovers(WallCtx w, Vector3 pos, Quaternion rot, float h, out string why)
        {
            Vector3 right = rot * Vector3.right, fwd = rot * Vector3.forward;
            float x0 = -PostersW * 0.5f - 0.05f, x1 = PostersW * 0.5f + 0.05f, y0 = PostersBottom(h) - 0.03f, y1 = PostersTop(h) + 0.03f;
            int nx = Mathf.CeilToInt((x1 - x0) / 0.1f), ny = Mathf.CeilToInt((y1 - y0) / 0.1f);
            float worst = 0f;
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= ny; j++)
                {
                    float x = x0 + (x1 - x0) * i / nx, y = y0 + (y1 - y0) * j / ny;
                    var o = pos + right * x + Vector3.up * y - fwd;
                    if (!w.wall.Raycast(new Ray(o, fwd), out RaycastHit hit, 2f)) { why = "no wall at x " + x.ToString("+0.00;-0.00") + " y " + y.ToString("F2") + " (wall too low / ends / gate opening)"; return false; }
                    worst = Mathf.Max(worst, Mathf.Abs(hit.distance - 1f));
                    if (Mathf.Abs(hit.distance - 1f) > WallFlatTol) { why = "wall not flat at x " + x.ToString("+0.00;-0.00") + " y " + y.ToString("F2") + " (" + ((hit.distance - 1f) * 100f).ToString("+0.0;-0.0") + " cm)"; return false; }
                }
            why = "wall covers " + PostersW.ToString("F2") + " x " + (y1 - y0).ToString("F2") + " m, flat within " + (worst * 100f).ToString("F1") + " cm";
            return true;
        }

        /// <summary>One candidate along the wall: the wall face (a ray from the spawn side), then the highest image centre the wall covers.</summary>
        static WallPose WallPoseAt(WallCtx w, float along, out string why)
        {
            var basePt = w.gate.position + w.gr * along;
            float gy = Ground(basePt + w.gf * 1.0f);
            var origin = basePt + w.gf * WallProbeOut; origin.y = gy + PosterCentreHeights[0];
            if (!w.wall.Raycast(new Ray(origin, -w.gf), out RaycastHit hit, WallProbeOut + 3f)) { why = "no wall face"; return null; }
            var n = Flat3(hit.normal).normalized;
            if (Vector3.Dot(n, w.gf) < 0.8f) { why = "the face does not look toward the spawn side (normal " + n.ToString("F2") + ")"; return null; }
            var rot = Quaternion.LookRotation(-n, Vector3.up);
            var pos = hit.point; pos.y = Ground(hit.point + n * PosterGroundOut);
            string last = "";
            foreach (float h in PosterCentreHeights)
            {
                if (WallCovers(w, pos, rot, h, out string cover))
                {
                    why = cover;
                    return new WallPose { pos = pos, yaw = rot.eulerAngles.y, h = h, along = Along(w, pos), outward = Outward(w, pos) };
                }
                last = cover;
            }
            why = last;
            return null;
        }

        /// <summary>The other boards' footprints (from their own renderers, board-local x / z) and readers' areas (front = -Z, 0.3-2.0 m).</summary>
        class OtherBoard { public string name; public Transform t; public Rect foot, readers; }

        static List<OtherBoard> OtherBoards(Ctx c, Transform self)
        {
            var list = new List<OtherBoard>();
            var ts = new List<Transform>();
            if (c.root != null) foreach (var b in Boards) { var t = c.root.Find(b.name); if (t != null) ts.Add(t); }
            foreach (var name in new[] { DogBookStandName, "KH_PhotoBoard", ItemGuideName, WallPostersName })
            {
                var go = KD_SceneObjects.Find(name);
                if (go != null && (self == null || go.transform != self)) ts.Add(go.transform);
            }
            foreach (var t in ts)
            {
                if (t == self) continue;
                if (!RendererFootprint(t, out Rect foot)) continue;
                list.Add(new OtherBoard { name = t.name, t = t, foot = foot, readers = t.name == WallPostersName ? Rect.MinMaxRect(foot.xMin, -PosterReaderFar, foot.xMax, -PosterReaderNear) : Rect.MinMaxRect(foot.xMin, -2.0f, foot.xMax, -0.3f) });
            }
            return list;
        }

        /// <summary>x / z extent of a board's mesh renderers (not its Canvas graphics) in its own local space.</summary>
        static bool RendererFootprint(Transform t, out Rect r)
        {
            float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = float.PositiveInfinity, z1 = float.NegativeInfinity;
            // active renderers only: the Kominka board's PortalAnchor (the portal marker) is inactive in the saved scene and is not the board
            foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.enabled || mr.GetComponentInParent<Canvas>() != null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var l = t.InverseTransformPoint(mr.transform.TransformPoint(corner));
                    x0 = Mathf.Min(x0, l.x); x1 = Mathf.Max(x1, l.x); z0 = Mathf.Min(z0, l.z); z1 = Mathf.Max(z1, l.z);
                }
            }
            r = float.IsInfinity(x0) ? new Rect() : Rect.MinMaxRect(x0, z0, x1, z1);
            return !float.IsInfinity(x0);
        }

        static List<Vector3> SamplesAt(Vector3 pos, Quaternion rot, Rect r, float step)
        {
            var list = new List<Vector3>();
            int nx = Mathf.Max(1, Mathf.CeilToInt(r.width / step)), nz = Mathf.Max(1, Mathf.CeilToInt(r.height / step));
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                    list.Add(pos + rot * new Vector3(r.xMin + r.width * i / nx, 0f, r.yMin + r.height * j / nz));
            return list;
        }

        /// <summary>Solid colliders players bump into within a board-local x / z rect and a height band (triggers, the terrain, the
        /// farmhouse wall itself when allowWall, and anything under <paramref name="self"/> ignored).</summary>
        static List<string> SolidAt(Vector3 pos, Quaternion rot, Rect r, float bottom, float top, Transform self, Collider allow)
        {
            Physics.SyncTransforms();
            var centre = pos + rot * new Vector3(r.center.x, 0f, r.center.y); centre.y = (bottom + top) * 0.5f;
            return Physics.OverlapBox(centre, new Vector3(r.width * 0.5f, (top - bottom) * 0.5f, r.height * 0.5f), rot, ~0, QueryTriggerInteraction.Ignore)
                .Where(x => !(x is TerrainCollider) && x != allow && (self == null || !x.transform.IsChildOf(self)) && !Physics.GetIgnoreLayerCollision(x.gameObject.layer, 10))
                .Select(x => KD_SceneObjects.PathOf(x.gameObject)).Distinct().ToList();
        }

        /// <summary>hard = cannot hang there (wall, solid things, the portal's side, a board standing right in front);
        /// soft = the guide boards' customs (readers' areas, the spawn, the portal, the sight line). Notes = numbers for the report.</summary>
        static void WallRules(WallCtx w, WallPose p, Transform self, List<string> notes)
        {
            var rot = Quaternion.Euler(0f, p.yaw, 0f);
            p.hard.Clear(); p.soft.Clear();
            if (!WallCovers(w, p.pos, rot, p.h, out string cover)) p.hard.Add(cover);
            if (Mathf.Sign(p.along) == PortalAlongSign(w)) p.hard.Add("on the portal's side of the gate (along " + p.along.ToString("+0.00;-0.00") + ", portal " + Along(w, w.portalAnchor.position).ToString("+0.00;-0.00") + ")");
            var postersRect = Rect.MinMaxRect(-PostersW * 0.5f, -(PlankGap + PlankDepth + 0.04f), PostersW * 0.5f, -0.005f);
            var hv = SolidAt(p.pos, rot, postersRect, p.pos.y + PostersBottom(p.h), p.pos.y + PostersTop(p.h), self, w.wall);
            if (hv.Count > 0) p.hard.Add("solid collider in the posters' volume (" + string.Join(", ", hv.Take(3)) + ")");
            var readers = Rect.MinMaxRect(-PostersW * 0.5f, -PosterReaderFar, PostersW * 0.5f, -PosterReaderNear);
            float ry = Ground(p.pos + rot * new Vector3(0f, 0f, readers.center.y));
            var hr = SolidAt(p.pos, rot, readers, ry + 0.3f, ry + 1.9f, self, null);
            if (hr.Count > 0) p.hard.Add("solid collider where the readers stand (" + string.Join(", ", hr.Take(3)) + ")");
            var zone = Rect.MinMaxRect(-PostersW * 0.5f, -PosterFrontZone, PostersW * 0.5f, 0f);
            var zoneS = SamplesAt(p.pos, rot, zone, 0.1f);
            var readS = SamplesAt(p.pos, rot, readers, 0.2f);
            foreach (var o in OtherBoards(w.c, self))
            {
                if (zoneS.Any(q => RectGap(o.foot, o.t.InverseTransformPoint(q)) <= 0f)) p.hard.Add("board " + o.name + " stands right in front of the posters (within " + PosterFrontZone + " m)");
                if (readS.Any(q => RectGap(o.foot, o.t.InverseTransformPoint(q)) <= 0f)) p.soft.Add("board " + o.name + "'s frame stands in the posters' readers' area");
                if (readS.Any(q => RectGap(o.readers, o.t.InverseTransformPoint(q)) <= 0f)) p.soft.Add("readers' area shared with board " + o.name);
                // the sight line from a reader 1.2 m out (centre) to each poster
                var eye = p.pos + rot * new Vector3(0f, 0f, -1.2f);
                foreach (float x in new[] { TorX(TorSide(w, p.pos, rot)), GroupX(TorSide(w, p.pos, rot)) })
                {
                    var target = p.pos + rot * new Vector3(x, 0f, -0.05f);
                    bool blocked = false;
                    for (int i = 0; i <= 20 && !blocked; i++) blocked = RectGap(o.foot, o.t.InverseTransformPoint(Vector3.Lerp(eye, target, i / 20f))) <= 0f;
                    if (blocked) p.soft.Add("board " + o.name + " blocks the view from the readers to a poster");
                }
            }
            float spawnD = readS.Min(q => Flat(q - w.spawn.position));
            if (spawnD < ReaderSpawnClear) p.soft.Add("readers' area " + spawnD.ToString("F2") + " m from the spawn (< " + ReaderSpawnClear + ")");
            if (w.kominka != null)
            {
                float side = w.portalAnchor.localPosition.x >= 0f ? 1f : -1f;
                var pr = PortalRect(side);
                if (readS.Any(q => RectGap(pr, w.kominka.InverseTransformPoint(q)) <= 0f)) p.soft.Add("readers' area on the Kominka portal's space");
            }
            p.soft = p.soft.Distinct().ToList();
            if (notes == null) return;
            notes.Add("along the wall " + p.along.ToString("+0.00;-0.00") + " m from the gate's centre (portal side " + (PortalAlongSign(w) > 0 ? "+" : "-") + "), face " + p.outward.ToString("F2") + " m from the gate line, image centres " + p.h.ToString("F2") + " m above the readers' ground; " + cover);
            float bearing = Bearing(w.spawn.position, p.pos);
            float spawnYaw = w.spawn.eulerAngles.y;
            notes.Add("from the spawn: " + Flat(p.pos - w.spawn.position).ToString("F2") + " m at bearing " + bearing.ToString("F0") + " deg (" + Mathf.DeltaAngle(spawnYaw, bearing).ToString("+0;-0") + " deg from the spawn's facing " + spawnYaw.ToString("F0") + "; the gate is at " + Mathf.DeltaAngle(spawnYaw, Bearing(w.spawn.position, w.gate.position)).ToString("+0;-0") + ")");
            notes.Add("readers' area " + spawnD.ToString("F2") + " m from the spawn; the Kominka board " + Flat(p.pos - w.kominka.position).ToString("F2") + " m, its portal " + Flat(p.pos - w.portalAnchor.position).ToString("F2") + " m"
                + (w.photo != null ? ", KH_PhotoBoard " + Flat(p.pos - w.photo.position).ToString("F2") + " m (along " + Along(w, w.photo.position).ToString("+0.00;-0.00") + ")" : ""));
            if (w.c.manager != null && w.c.manager.route != null && w.c.manager.route.nodePositions != null && w.c.manager.route.nodePositions.Length > 1)
            {
                var nodes = w.c.manager.route.nodePositions; int n = nodes.Length, segs = w.c.manager.route.closedLoop ? n : n - 1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < segs; i++) best = Mathf.Min(best, SegDist(p.pos, nodes[i], nodes[(i + 1) % n]));
                notes.Add("the loop's centre line " + best.ToString("F2") + " m from the posters (seen from the walk)");
            }
        }

        /// <summary>+1 / -1: which side of the root (its +X / -X) faces the gate; torinosuke hangs on that side.</summary>
        static float TorSide(WallCtx w, Vector3 pos, Quaternion rot) { return Vector3.Dot(Flat3(w.gate.position - pos), rot * Vector3.right) >= 0f ? 1f : -1f; }

        /// <summary>The image centre height at a human-owned pose: the highest of PosterCentreHeights the wall covers; if none does (the user hung
        /// them partly off the wall), the height built last time when it is one of them, else the first. Guide/20 and Guide/21 use the same answer.</summary>
        static float KeptHeight(WallCtx w, Vector3 pos, Quaternion rot, Transform root)
        {
            foreach (float h in PosterCentreHeights) if (WallCovers(w, pos, rot, h, out _)) return h;
            var cv = root != null ? root.Find(TorinosukePosterName + "/" + CanvasName) : null;
            if (cv != null) foreach (float h in PosterCentreHeights) if (Mathf.Abs(cv.localPosition.y - h) < 0.005f) return h;
            return PosterCentreHeights[0];
        }

        static WallPose FindWallPose(WallCtx w, List<string> log)
        {
            if (w.existing != null)
            {
                // 2026-10-14 ユーザー決定 (IMP-UI-008「配置場所ちょっと移動した」): once it exists, x / z / yaw are the user's, as KH_DogBookStand.
                // Never searched again: rules broken there (hard ones too) are WARN lines. Delete KH_WallPosters to search again.
                var e = w.existing; float yaw = e.eulerAngles.y; var rot = Quaternion.Euler(0f, yaw, 0f);
                var kp = e.position; kp.y = Ground(kp + rot * Vector3.back * PosterGroundOut);   // only the height is re-seated
                var kept = new WallPose { pos = kp, yaw = yaw, h = KeptHeight(w, kp, rot, e), along = Along(w, kp), outward = Outward(w, kp), region = "kept" };
                WallRules(w, kept, e, null);
                var broken = kept.hard.Concat(kept.soft).ToList();
                log.Add("kept " + WallPostersName + " at " + kept.At() + " (human-owned)" + (broken.Count > 0 ? " WARN breaks " + string.Join("; ", broken) : " (all rules pass)"));
                return kept;
            }
            float sign = -PortalAlongSign(w);
            float photoAlong = w.photo != null ? Along(w, w.photo.position) * sign : float.PositiveInfinity;
            WallPose firstA = null, firstB = null, firstClear = null; int tried = 0, faces = 0;
            for (float d = 0f; d <= WallSearchMax + 1e-4f; d += WallSearchStep)
            {
                tried++;
                var p = WallPoseAt(w, sign * d, out string why);
                if (p == null) continue;
                faces++;
                p.region = d < photoAlong ? "between the gate and the photo board" : "beyond the photo board";
                WallRules(w, p, w.existing, null);
                if (p.hard.Count > 0) continue;
                if (firstClear == null) firstClear = p;
                if (p.soft.Count == 0)
                {
                    if (d < photoAlong) { if (firstA == null) firstA = p; }
                    else if (firstB == null) firstB = p;
                }
                if (firstA != null && firstB != null) break;
            }
            log.Add("searched " + tried + " points along the wall (" + faces + " with a flat wall face) on the side opposite the portal");
            log.Add("between the gate and the photo board: " + (firstA != null ? "ALL RULES PASS at " + firstA.At() : "no point passes every rule"));
            log.Add("beyond the photo board: " + (firstB != null ? "ALL RULES PASS at " + firstB.At() : "no point passes every rule"));
            var pick = firstA ?? firstB;
            if (pick != null) { log.Add("PLACED " + pick.region + ": " + pick.At()); return pick; }
            if (firstClear == null) throw new InvalidOperationException("no point on the wall opposite the portal can hold the posters (0-" + WallSearchMax + " m from the gate); nothing changed");
            log.Add("WARN no point passes every rule. PLACED at the first point the posters can hang (USER DECISION NEEDED: Play で見て、場所を変えるか規則の例外にするか): " + firstClear.At() + " breaks " + string.Join("; ", firstClear.soft));
            return firstClear;
        }

        // ================================================================ Guide/20

        [Serializable] class WallReport
        {
            public string utc, scene, status, backup;
            public List<string> log = new List<string>();
            public List<string> notes = new List<string>();
        }

        [MenuItem(Menu + "20 - Build Shop Intro And Poster")]
        public static void BuildWallPosters()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var r = new WallReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            EnsurePosterImporter(TorinosukeTexturePath, r.log);
            EnsurePosterImporter(GroupTexturePath, r.log);
            var w = ResolveWall(true);
            ProbeOtherBindings(new[] { WallPostersName, ItemGuideName });
            var pose = FindWallPose(w, r.log);
            Directory.CreateDirectory(OutDir);
            r.backup = OutDir + "/before-wall-posters-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            File.Copy(KH_CourseGroundEditor.ScenePath, r.backup, false);
            try
            {
                var root = w.existing;
                if (root == null)
                {
                    root = new GameObject(WallPostersName).transform;
                    SceneManagerMove(root.gameObject, w.c.scene);
                    r.log.Add("created " + WallPostersName + " at the scene root");
                }
                root.SetPositionAndRotation(pose.pos, Quaternion.Euler(0f, pose.yaw, 0f));
                root.localScale = Vector3.one; root.gameObject.layer = 0; root.gameObject.SetActive(true);
                BuildPosters(root, w, pose, r.log);
                var log = new StringBuilder();
                KD_ShopSetupEditor.AddGlyphs(WallGlyphText(w.c), log);
                BindIfComplete(w.c, log, new[] { WallPostersName, ItemGuideName });
                foreach (var line in log.ToString().Split('\n')) if (line.Trim().Length > 0 && !line.StartsWith("  AUTOSIZE")) r.log.Add(line.Trim());
                EditorSceneManager.MarkSceneDirty(w.c.scene);
                if (!EditorSceneManager.SaveScene(w.c.scene)) throw new IOException("wall posters scene save failed");
                AssetDatabase.SaveAssets();
            }
            catch
            {
                Debug.LogError("[KH Wall Posters] Build failed; the scene before this run is at " + r.backup + ". Reopen the scene without saving to discard partial changes.");
                r.status = "FAIL (exception, not saved)";
                WriteJson(WallBuildReportPath, r);
                throw;
            }
            r.status = "BUILT";
            WriteJson(WallBuildReportPath, r);
            var v = CheckWallPosters();
            WriteJson(WallVerifyReportPath, v);
            if (v.failed > 0)
            {
                Debug.LogError("[KH Wall Posters] WALL_POSTERS_FAIL after build (" + v.failed + " failed) -> " + WallVerifyReportPath);
                throw new InvalidOperationException("[KH Wall Posters] Build saved but " + v.failed + " check(s) failed:\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL")).Take(30)));
            }
            Debug.Log("[KH Wall Posters] BUILT and WALL_POSTERS_OK (" + v.lines.Count + " checks, " + v.warned + " WARN) -> " + WallBuildReportPath + " / " + WallVerifyReportPath);
        }

        /// <summary>Texture import for a poster: no mipmaps (Build Size/7 drops them on our own NPOT textures anyway), POT-rounded, compressed
        /// (BC7 on PC), PC 2048 / Android + iOS 1024 (Build Size/7's row for Textures/Posters/). Reimports only when something differs.</summary>
        static void EnsurePosterImporter(string path, List<string> log)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) throw new InvalidOperationException(path + " not imported (refresh first); nothing changed");
            bool dirty = false;
            void Set<T>(T now, T want, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(now, want)) { apply(want); dirty = true; } }
            Set(imp.textureType, TextureImporterType.Default, v => imp.textureType = v);
            Set(imp.sRGBTexture, true, v => imp.sRGBTexture = v);
            Set(imp.mipmapEnabled, false, v => imp.mipmapEnabled = v);
            Set(imp.npotScale, TextureImporterNPOTScale.ToNearest, v => imp.npotScale = v);
            Set(imp.wrapMode, TextureWrapMode.Clamp, v => imp.wrapMode = v);
            Set(imp.filterMode, FilterMode.Bilinear, v => imp.filterMode = v);
            Set(imp.maxTextureSize, 2048, v => imp.maxTextureSize = v);
            Set(imp.textureCompression, TextureImporterCompression.CompressedHQ, v => imp.textureCompression = v);
            Set(imp.isReadable, false, v => imp.isReadable = v);
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var s = imp.GetPlatformTextureSettings(platform);
                if (!s.overridden || s.maxTextureSize != 1024 || s.textureCompression == TextureImporterCompression.Uncompressed)
                {
                    s.overridden = true; s.maxTextureSize = 1024; s.textureCompression = TextureImporterCompression.Compressed; s.format = TextureImporterFormat.Automatic;
                    imp.SetPlatformTextureSettings(s); dirty = true;
                }
            }
            if (dirty) { imp.SaveAndReimport(); log.Add("texture import set: " + path + " (no mip, POT, CompressedHQ, PC 2048 / Android + iOS 1024)"); }
        }

        static void BuildPosters(Transform root, WallCtx w, WallPose pose, List<string> log)
        {
            var c = w.c;
            float side = TorSide(w, root.position, root.rotation), h = pose.h;
            foreach (Transform t in root)
                if (t.name != TorinosukePosterName && t.name != GroupPosterName) log.Add("WARN " + WallPostersName + "/" + t.name + " is not made by this builder: left as is");

            // ---- torinosuke: frame, image, URL field (古民家 KD_AuthorPosterEditor と同じ並び: 画像の下に URL 欄) ----
            var tor = PosterChild(root, TorinosukePosterName, new Vector3(TorX(side), 0f, 0f));
            var keepT = new HashSet<Transform>();
            var sT = Child(tor, StructureName, keepT); sT.localPosition = Vector3.zero; sT.localRotation = Quaternion.identity; sT.localScale = Vector3.one;
            float torCy = h + (TorFrameTop + TorFrameBottom) * 0.5f, torH = TorFrameTop - TorFrameBottom;
            Wood(sT, "Back", new Vector3(0f, torCy, -(PlankGap + PlankDepth * 0.5f)), Quaternion.identity, new Vector3(TorFrameW, torH, PlankDepth), c, keepT);
            var torCanvas = PosterCanvas(tor, CanvasName, TorImageM / Mm, Mm, new Vector3(0f, h, -(PlankGap + PlankDepth + 0.002f)), false, keepT);
            PosterImage(torCanvas, w.torTex, TorImageM / Mm, keepT);
            var urlCanvas = PosterCanvas(tor, "UrlCanvas", UrlPx, UrlScale, new Vector3(0f, h - TorUrlBelow, -(PlankGap + PlankDepth + 0.004f)), true, keepT);
            var field = KD_MenuFinishEditor.CopyField(urlCanvas, "Url", Vector2.zero, UrlPx, TorinosukeUrl, c.font, false);
            field.pointSize = UrlPoints;
            if (field.textComponent != null) { field.textComponent.font = c.font; EditorUtility.SetDirty(field.textComponent); }
            EditorUtility.SetDirty(field);
            foreach (var t in urlCanvas.GetComponentsInChildren<Transform>(true)) { keepT.Add(t); t.gameObject.layer = 0; }
            RemoveStalePoster(tor, keepT, log);

            // ---- group poster: frame, image, Interact (KH_GroupPoster) ----
            var grp = PosterChild(root, GroupPosterName, new Vector3(GroupX(side), 0f, 0f));
            var keepG = new HashSet<Transform>();
            var sG = Child(grp, StructureName, keepG); sG.localPosition = Vector3.zero; sG.localRotation = Quaternion.identity; sG.localScale = Vector3.one;
            Wood(sG, "Back", new Vector3(0f, h, -(PlankGap + PlankDepth * 0.5f)), Quaternion.identity, new Vector3(GroupFrameW, 2f * GroupFrameHalfH, PlankDepth), c, keepG);
            var grpCanvas = PosterCanvas(grp, CanvasName, GroupImageM / Mm, Mm, new Vector3(0f, h, -(PlankGap + PlankDepth + 0.002f)), false, keepG);
            PosterImage(grpCanvas, w.groupTex, GroupImageM / Mm, keepG);
            var it = Child(grp, InteractName, keepG);
            it.localPosition = Vector3.zero; it.localRotation = Quaternion.identity; it.localScale = Vector3.one;
            foreach (var col in it.GetComponents<Collider>()) if (!(col is BoxCollider)) UnityEngine.Object.DestroyImmediate(col);
            var box = it.GetComponent<BoxCollider>(); if (box == null) box = it.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true; box.center = new Vector3(0f, h, -(PlankGap + PlankDepth + 0.02f)); box.size = new Vector3(GroupFrameW, 2f * GroupFrameHalfH, 0.04f);
            EditorUtility.SetDirty(box);
            var gp = it.GetComponent<KH_GroupPoster>();
            if (gp == null) { gp = it.gameObject.AddUdonSharpComponent<KH_GroupPoster>(); log.Add("added KH_GroupPoster"); }
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(gp);
            if (backing == null) throw new InvalidOperationException("KH_GroupPoster backing missing (compile U# after Guide/0, then Guide/20 again; reopen the scene without saving)");
            if (backing.SyncMethod != VRC.SDKBase.Networking.SyncType.None) { backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None; EditorUtility.SetDirty(backing); }
            backing.proximity = GroupInteractProximity; EditorUtility.SetDirty(backing);
            gp.groupId = VAnimalGroupId;
            UdonSharpEditorUtility.CopyProxyToUdon(gp); EditorUtility.SetDirty(gp);
            RemoveStalePoster(grp, keepG, log);
            log.Add("torinosuke on the root's " + (side > 0f ? "+X" : "-X") + " side (toward the gate), group poster on the other; image centres " + h.ToString("F2") + " m; posters " + PostersW.ToString("F2") + " m wide");
        }

        static Transform PosterChild(Transform root, string name, Vector3 local)
        {
            var t = root.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(root, false); }
            t.localPosition = local; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            t.gameObject.layer = 0; t.gameObject.SetActive(true);
            return t;
        }

        /// <summary>A World Space Canvas at <paramref name="local"/>. input = the URL field's canvas (GraphicRaycaster + VRCUiShape + a trigger
        /// BoxCollider, Default layer: vrc-world-ui §2); otherwise read-only (none of them).</summary>
        static RectTransform PosterCanvas(Transform parent, string name, Vector2 size, float scale, Vector3 local, bool input, HashSet<Transform> keep)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null) { go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); }
            else go = t.gameObject;
            go.layer = 0;
            var canvas = go.GetComponent<Canvas>(); if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            if (input)
            {
                if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
                var bc = go.GetComponent<BoxCollider>(); if (bc == null) bc = go.AddComponent<BoxCollider>();
                bc.isTrigger = true; bc.center = Vector3.zero; bc.size = new Vector3(size.x, size.y, 10f);
                if (go.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null) go.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            }
            else
                foreach (var comp in go.GetComponents<Component>())
                    if (comp is GraphicRaycaster || comp is Collider || comp is CanvasScaler || comp is VRC.SDK3.Components.VRCUiShape) UnityEngine.Object.DestroyImmediate(comp);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.localPosition = local; rt.localRotation = Quaternion.identity; rt.localScale = Vector3.one * scale;
            EditorUtility.SetDirty(canvas);
            keep.Add(rt);
            return rt;
        }

        static void PosterImage(RectTransform canvas, Texture2D tex, Vector2 size, HashSet<Transform> keep)
        {
            var go = KD_ShopSetupEditor.EnsureUiChild(canvas, "Image", Vector2.zero, size);
            go.layer = 0;
            foreach (var g in go.GetComponents<Graphic>()) if (!(g is RawImage)) UnityEngine.Object.DestroyImmediate(g);
            var raw = go.GetComponent<RawImage>(); if (raw == null) raw = go.AddComponent<RawImage>();
            raw.texture = tex; raw.color = Color.white; raw.raycastTarget = false; raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            EditorUtility.SetDirty(raw);
            keep.Add(go.transform);
        }

        static void RemoveStalePoster(Transform poster, HashSet<Transform> keep, List<string> log)
        {
            foreach (var t in poster.GetComponentsInChildren<Transform>(true).Where(x => x != poster && !keep.Contains(x)).ToList())
            {
                if (t == null) continue;
                if (t.parent != poster && !keep.Contains(t.parent)) continue;   // goes with its parent
                log.Add("removed stale " + KD_SceneObjects.PathOf(t.gameObject));
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
        }

        static string WallGlyphText(Ctx c)
        {
            var s = new StringBuilder(TorinosukeUrl);
            foreach (var lang in c.catalog.languages)
                if (KD_LocalizationEditor.Bundled(lang)) s.Append(KD_LocalizationEditor.Text(c.catalog, lang.code, GroupInteractKey));
            return s.ToString();
        }

        // ================================================================ binding the whole manifest (shared with Guide/22)

        /// <summary>KD_LocalizationEditor.Bind resolves every row of the manifest. Before anything changes: every row whose root is not one of
        /// <paramref name="ours"/> must resolve (another builder's object missing = stop, nothing changed).</summary>
        static void ProbeOtherBindings(string[] ours)
        {
            var manifest = JsonUtility.FromJson<KD_LocalizationEditor.Manifest>(File.ReadAllText(KD_LocalizationEditor.BindingsPath));
            var unresolved = new List<string>();
            Action<string, string> probe = (r, p) =>
            {
                if (ours.Contains(r)) return;
                try { KD_LocalizationEditor.Resolve(new KD_LocalizationEditor.Binding { root = r, path = p }); }
                catch (Exception e) { unresolved.Add(r + "/" + p + " (" + e.Message + ")"); }
            };
            foreach (var b in manifest.labels) probe(b.root, b.path);
            foreach (var b in manifest.interactions ?? new KD_LocalizationEditor.Binding[0]) probe(b.root, b.path);
            foreach (var b in manifest.pickups ?? new KD_LocalizationEditor.Binding[0]) probe(b.root, b.path);
            foreach (var n in manifest.names ?? new KD_LocalizationEditor.Names[0]) probe(n.root, "");
            if (unresolved.Count > 0)
                throw new InvalidOperationException("the localization manifest names objects that are not in the scene (run the builder that owns them first): " + string.Join("; ", unresolved.Take(8)) + "; nothing changed");
        }

        /// <summary>Configure + Bind (as Guide/1) when every row resolves. While the other IMP-UI-008 board is not built yet (Guide/20 before
        /// Guide/22, or the reverse) the bind is deferred with a WARN: the next of the two binds everything. Verify reports the stale tables meanwhile.</summary>
        static bool BindIfComplete(Ctx c, StringBuilder log, string[] ours)
        {
            var missing = ours.Where(n => KD_SceneObjects.Find(n) == null).ToArray();
            if (missing.Length > 0)
            {
                log.AppendLine("WARN localization not bound yet: " + string.Join(", ", missing) + " not built (run " + (missing.Contains(ItemGuideName) ? "Guide/22" : "Guide/20") + " next; it binds everything)");
                return false;
            }
            KD_LocalizationEditor.Configure(c.language, c.catalog, log);
            KD_LocalizationEditor.Bind(c.language, c.catalog, log);
            UdonSharpEditorUtility.CopyProxyToUdon(c.language);
            EditorUtility.SetDirty(c.language);
            return true;
        }

        // ================================================================ Guide/21

        [MenuItem(Menu + "21 - Verify Shop Intro And Poster")]
        public static void VerifyWallPosters()
        {
            KH_CourseGroundEditor.Idle(KH_CourseGroundEditor.ScenePath);
            var v = CheckWallPosters();
            WriteJson(WallVerifyReportPath, v);
            if (v.failed > 0)
            {
                Debug.LogError("[KH Wall Posters] WALL_POSTERS_FAIL (" + v.failed + " failed, " + v.warned + " WARN) -> " + WallVerifyReportPath);
                throw new InvalidOperationException("[KH Wall Posters] Verify failed:\n" + string.Join("\n", v.lines.Where(l => l.StartsWith("FAIL")).Take(30)));
            }
            Debug.Log("[KH Wall Posters] WALL_POSTERS_OK (" + v.lines.Count + " checks, " + v.warned + " WARN) -> " + WallVerifyReportPath);
        }

        static VerifyReport CheckWallPosters()
        {
            var v = new VerifyReport { utc = DateTime.UtcNow.ToString("o"), scene = KH_CourseGroundEditor.ScenePath };
            v.limitations = "Static checks of the saved scene (IMP-UI-008 B): the wall face and the rules by physics, the textures and their import, the URL field, "
                + "the group poster's Interact (KH_GroupPoster, local), the bindings and the glyphs. Not judged here: whether the group page opens and the URL can be "
                + "copied in the VRChat client, readability from the spawn / the walk, how the posters look on Quest / phones (Play).";
            Action<bool, string> req = (ok, what) => { v.lines.Add((ok ? "OK   " : "FAIL ") + what); if (!ok) v.failed++; };
            Action<bool, string> warn = (ok, what) => { v.lines.Add((ok ? "OK   " : "WARN ") + what); if (!ok) v.warned++; };
            WallCtx w;
            try { w = ResolveWall(false); }
            catch (Exception e) { req(false, "resolve: " + e.Message); v.status = "WALL_POSTERS_FAIL"; return v; }
            var root = w.existing;
            req(root != null, WallPostersName + " once at the scene root");
            if (root == null) { v.status = "WALL_POSTERS_FAIL"; return v; }
            var e3 = root.eulerAngles;
            req(Mathf.Abs(Mathf.DeltaAngle(e3.x, 0f)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e3.z, 0f)) < 0.01f && root.localScale == Vector3.one, WallPostersName + " yaw only, scale 1");
            req(root.gameObject.activeInHierarchy && root.gameObject.layer == 0, WallPostersName + " active, Default layer");

            // ---- place: the wall, the side, the rules ----
            // 2026-10-14 ユーザー決定: the pose is human-owned (the user moved it). Every placement rule broken there, the hard ones too, is a WARN.
            float h = KeptHeight(w, root.position, root.rotation, root);
            bool covered = WallCovers(w, root.position, root.rotation, h, out string coverWhy);
            warn(covered, "the wall covers the posters at the current pose (" + coverWhy + ")");
            float side = TorSide(w, root.position, root.rotation);
            var tor = root.Find(TorinosukePosterName); var grp = root.Find(GroupPosterName);
            req(tor != null && grp != null, WallPostersName + "/" + TorinosukePosterName + " and /" + GroupPosterName);
            if (tor == null || grp == null) { v.status = "WALL_POSTERS_FAIL"; return v; }
            var canvasT = tor.Find(CanvasName);
            float builtH = canvasT != null ? canvasT.localPosition.y : -1f;
            req(Mathf.Abs(builtH - h) < 0.005f, "image centre " + builtH.ToString("F2") + " m = the height Guide/20 picks here (" + h.ToString("F2") + "; run Guide/20 after moving the posters)");
            var pose = new WallPose { pos = root.position, yaw = root.eulerAngles.y, h = h, along = Along(w, root.position), outward = Outward(w, root.position) };
            var notes = new List<string>();
            WallRules(w, pose, root, notes);
            v.notes.AddRange(notes);
            foreach (var s in pose.hard) warn(false, "placement rule at the user's pose (was a hard rule in the search; kept as the user placed it): " + s);
            foreach (var s in pose.soft) warn(false, "placement rule (USER DECISION: ユーザー依頼の壁面との兼ね合い): " + s);
            if (pose.hard.Count == 0 && pose.soft.Count == 0) req(true, "posters pass every placement rule at the user's pose");
            req(Mathf.Abs(root.position.y - Ground(root.position + root.rotation * Vector3.back * PosterGroundOut)) <= 0.02f,
                "root height = the ground " + PosterGroundOut + " m in front of the wall (moved by hand? run Guide/20, which keeps x / z / yaw)");
            req(Mathf.Abs(tor.localPosition.x - TorX(side)) < 0.001f && Mathf.Abs(grp.localPosition.x - GroupX(side)) < 0.001f, "torinosuke toward the gate, the group poster beside it (" + PosterGap + " m apart)");

            // ---- frames, images, textures ----
            foreach (var p in new[] { tor, grp })
            {
                var back = p.Find(StructureName + "/Back");
                var mr = back != null ? back.GetComponent<MeshRenderer>() : null;
                req(mr != null && mr.sharedMaterial == w.c.wood && back.GetComponent<Collider>() == null && (GameObjectUtility.GetStaticEditorFlags(back.gameObject) & StaticEditorFlags.BatchingStatic) != 0,
                    p.name + " frame: wood (" + WoodMatPath + "), no collider, Batching Static");
            }
            CheckPosterImage(tor, w.torTex, TorImageM, TorinosukeSrcW, TorinosukeSrcH, req);
            CheckPosterImage(grp, w.groupTex, GroupImageM, GroupSrcW, GroupSrcH, req);
            foreach (var path in new[] { TorinosukeTexturePath, GroupTexturePath })
            {
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                bool mobile = imp != null && new[] { "Android", "iPhone" }.All(pl => { var s = imp.GetPlatformTextureSettings(pl); return s.overridden && s.maxTextureSize == 1024 && s.textureCompression != TextureImporterCompression.Uncompressed; });
                req(imp != null && !imp.mipmapEnabled && imp.maxTextureSize == 2048 && imp.textureCompression != TextureImporterCompression.Uncompressed && imp.npotScale == TextureImporterNPOTScale.ToNearest && mobile,
                    path + " import: no mip, POT, compressed, PC 2048 / Android + iOS 1024");
            }

            // ---- URL field (古民家と同じ CopyField) ----
            var urlCanvas = tor.Find("UrlCanvas");
            req(urlCanvas != null && urlCanvas.GetComponent<Canvas>() != null && urlCanvas.GetComponent<GraphicRaycaster>() != null && urlCanvas.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null
                && urlCanvas.GetComponent<BoxCollider>() != null && urlCanvas.GetComponent<BoxCollider>().isTrigger && urlCanvas.gameObject.layer == 0, "URL canvas: GraphicRaycaster + VRCUiShape + trigger BoxCollider, Default layer");
            var field = urlCanvas != null ? urlCanvas.GetComponentsInChildren<TMP_InputField>(true).FirstOrDefault(f => f.name == "Url") : null;
            req(field != null && field.text == TorinosukeUrl && field.readOnly && field.onFocusSelectAll && !field.richText && field.lineType == TMP_InputField.LineType.SingleLine
                && field.textViewport != null && field.textViewport.GetComponent<RectMask2D>() != null, "URL field " + TorinosukeUrl + ": read-only, selects all, single line, masked");
            req(urlCanvas != null && Mathf.Abs(urlCanvas.localScale.x - UrlScale) < 1e-6f && ((RectTransform)urlCanvas).sizeDelta == UrlPx, "URL field " + (UrlW).ToString("F2") + " x " + (UrlH).ToString("F3") + " m (1 px = 0.8 mm, as Kominka)");
            string urlMissing = KD_ShopSetupEditor.MissingGlyphs(TorinosukeUrl);
            req(urlMissing.Length == 0, "URL glyphs in the shared font" + (urlMissing.Length > 0 ? " (missing " + urlMissing + ")" : ""));

            // ---- group poster Interact ----
            var it = grp.Find(InteractName);
            var gp = it != null ? it.GetComponent<KH_GroupPoster>() : null;
            req(gp != null && gp.groupId == VAnimalGroupId, GroupInteractPath + " has KH_GroupPoster with the VAnimal group id (proxy)");
            var bk = gp != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(gp) : null;
            req(bk != null && bk.SyncMethod == VRC.SDKBase.Networking.SyncType.None, "KH_GroupPoster sync None (local only)");
            object gid = null;
            req(bk != null && bk.publicVariables.TryGetVariableValue("groupId", out gid) && (gid as string) == VAnimalGroupId, "KH_GroupPoster.groupId (backing)");
            req(bk != null && bk.proximity >= 2f, "Interact reach " + (bk != null ? bk.proximity.ToString("F1") : "?") + " m (>= 2)");
            var box = it != null ? it.GetComponent<BoxCollider>() : null;
            req(box != null && box.isTrigger && it.gameObject.layer == 0 && box.size.x >= GroupImageM.x && box.size.y >= GroupImageM.y, "the Interact target is a trigger over the whole poster on Default");
            int ii = bk != null && w.c.language.interactTargets != null ? Array.IndexOf(w.c.language.interactTargets, bk) : -1;
            // Guide/20 before Guide/22: the bind waits for the item guide board (BindIfComplete), so the binding lines are WARN until then
            bool bindPending = KD_SceneObjects.Find(ItemGuideName) == null;
            Action<bool, string> bound = bindPending ? (ok, what) => warn(ok, what + " (bind deferred: run Guide/22, which binds everything)") : req;
            bound(ii >= 0 && w.c.language.interactKeys[ii] == GroupInteractKey, "the Interact text is bound to " + GroupInteractKey);
            var udons = root.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true);
            req(udons.Length == 1 && it != null && udons[0].transform == it, "one UdonBehaviour under " + WallPostersName + " (the group poster's Interact)");
            var solid = root.GetComponentsInChildren<Collider>(true).Where(x => !x.isTrigger).ToArray();
            req(solid.Length == 0, "no solid Collider under " + WallPostersName + " (players and dogs pass)");
            foreach (var lang in w.c.catalog.languages)
            {
                var e = lang.texts.FirstOrDefault(x => x.key == GroupInteractKey);
                warn(e != null && !string.IsNullOrWhiteSpace(e.text), GroupInteractKey + " has a " + lang.code + " text (AI translation, not proofread)");
            }
            try { KD_LocalizationEditor.ValidateScene(w.c.language, w.c.catalog); bound(true, "localization tables, bindings and glyphs match the catalogs (KD_LocalizationEditor.ValidateScene)"); }
            catch (Exception e) { bound(false, "localization: " + e.Message + " (Guide/20 / Guide/22 re-bind; the later of the two binds everything)"); }
            v.budget = "2 wood planks (" + WoodMatPath + ", Batching Static), 2 RawImages (" + TorinosukeTexturePath + " 1241x1754 -> POT, " + GroupTexturePath + " 1587x2245 -> POT; PC BC7 2048, Android / iOS 1024), "
                + "1 URL field (shared font), 1 UdonBehaviour (KH_GroupPoster, sync None), 2 trigger colliders; network objects 0";
            v.status = v.failed == 0 ? "WALL_POSTERS_OK" : "WALL_POSTERS_FAIL";
            return v;
        }

        static void CheckPosterImage(Transform poster, Texture2D tex, Vector2 sizeM, int srcW, int srcH, Action<bool, string> req)
        {
            var cv = poster.Find(CanvasName);
            var canvas = cv != null ? cv.GetComponent<Canvas>() : null;
            req(canvas != null && canvas.renderMode == RenderMode.WorldSpace && cv.GetComponent<GraphicRaycaster>() == null && cv.GetComponent<Collider>() == null && cv.GetComponent<VRC.SDK3.Components.VRCUiShape>() == null,
                poster.name + "/" + CanvasName + " read-only World Space Canvas");
            var raw = cv != null && cv.Find("Image") != null ? cv.Find("Image").GetComponent<RawImage>() : null;
            req(raw != null && raw.texture == tex && !raw.raycastTarget, poster.name + " image = " + AssetDatabase.GetAssetPath(tex));
            if (cv == null) return;
            var size = ((RectTransform)cv).sizeDelta * cv.localScale.x;
            req(Mathf.Abs(size.x - sizeM.x) < 0.002f && Mathf.Abs(size.y - sizeM.y) < 0.002f && Mathf.Abs(size.y / size.x - (float)srcH / srcW) < 0.01f,
                poster.name + " image " + size.x.ToString("F3") + " x " + size.y.ToString("F3") + " m, aspect of the source " + srcW + " x " + srcH + " (Kominka's size)");
        }
    }
}
