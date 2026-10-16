using System;
using System.Collections.Generic;
using System.Linq;
using KominkaDogRun.Editor;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace KominkaHillside.Editor
{
    /// <summary>IMP-UI-013 (docs/development/issues/IMP-UI-013-trainer-board.md): the ばにまるトレーナー board KH_GuideBoards/Trainer beside the
    /// 古民家ドッグラン board in front of the gate, built by Guide/1 and checked by Guide/2 (.TrainerVerify.cs).
    ///
    /// The board is the same wooden standing board as Kominka (Structure: tray Back / posts / ridge / roof, KH_RiverWood), canvas 1300 x 1300 mm:
    ///   Trainer/Canvas  = the read-only face: HeaderPlate (Header + Subtitle), Paper (Perks right of the photo, Cove under it), Photo (RawImage
    ///                     KH_CoveBeach.png, 14:11) over the paper's top-left;
    ///   Trainer/Actions = an operation canvas (GraphicRaycaster + VRCUiShape + trigger BoxCollider, Default layer) along the bottom strip:
    ///                     Plate, Status, GiftNote, StoreButton, PortalButton, DetailsButton; KH_TrainerBoard (U#, local) sits on it;
    ///   Trainer/Details = the popup (an operation canvas over the paper, inactive in the saved scene): Paper, Body, UrlCaption, UrlField
    ///                     (KD_MenuFinishEditor.CopyField, read-only), CloseButton. It covers the face's paper only (never the Actions strip).
    /// All three canvases lie in the tray's front plane (BUG-ENV-005: every drawn graphic inside a hole); Details draws over the face by its
    /// canvas sortingOrder.
    /// The cove portal: scene-root KH_CovePortal (an anchor, inactive in the saved scene) holding the cove's VRCPortalMarker (roomId
    /// CoveRoomId) as an active child, the same structure as Kominka/PortalAnchor + its marker. The marker is created on the first build from
    /// the same prefab as the Kominka portal (else a copy of it) and only its roomId is written; the Kominka marker is never touched here.
    /// Pose: searched like FindKominkaPose from the Kominka board's pose (the one this run keeps or puts it at), sideways along its line, same
    /// yaw; first the side away from the Kominka portal, then beyond the Kominka portal. TrainerRules (Guide/1 and Guide/2) keep it clear of
    /// the gate line, the Kominka board, both portals, the other boards / posters and their readers, the fixtures, physics, and steep ground.
    /// An existing board is kept where it is when it passes every rule there (as Kominka).</summary>
    public static partial class KH_GuideBoardsEditor
    {
        public const string TrainerProgramPath = "Assets/KominkaHillside/Scripts/KH_TrainerBoard";
        public const string CovePortalName = "KH_CovePortal", CoveMarkerName = "CoveVRCPortalMarker";
        public const string CoveRoomId = "wrld_266a7eef-4edd-479f-a76b-dd64e09b41af";
        public const string CoveUrl = "https://vrchat.com/home/world/" + CoveRoomId + "/info";
        public const string CovePhotoPath = "Assets/KominkaHillside/Textures/Guide/KH_CoveBeach.png";
        /// <summary>The SDK portal prefab (the Kominka portal is an instance of it); the cove marker is always an instance of it.</summary>
        public const string SdkPortalPrefabPath = "Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCPortalMarker.prefab";
        const string ActionsName = "Actions", DetailsName = "Details";
        const string StoreButtonName = "StoreButton", PortalButtonName = "PortalButton", DetailsButtonName = "DetailsButton", CloseButtonName = "CloseButton";
        const string StoreEvent = "_OpenStore", PortalEvent = "_TogglePortal", DetailsOpenEvent = "_OpenDetails", DetailsCloseEvent = "_CloseDetails";

        /// <summary>The dynamic words KH_TrainerBoard writes (UITranslations.json; Guide/1 requires them in every language and adds their ja / en glyphs).</summary>
        static readonly string[] TrainerWordKeys =
        {
            KH_TrainerBoard.KeyLoading, KH_TrainerBoard.KeyOff, KH_TrainerBoard.KeyOn, KH_TrainerBoard.KeyUnconfigured,
            KH_TrainerBoard.KeyJoin, KH_TrainerBoard.KeyGift, KH_TrainerBoard.KeyGiftNote,
        };
        static readonly string[] TrainerJaFallbacks =
        {
            KH_TrainerBoard.JaLoading, KH_TrainerBoard.JaOff, KH_TrainerBoard.JaOn, KH_TrainerBoard.JaUnconfigured,
            KH_TrainerBoard.JaJoin, KH_TrainerBoard.JaGift, KH_TrainerBoard.JaGiftNote,
        };

        // ---------------------------------------------------------------- face layout (canvas 1300 x 1300 mm, centre origin; paper children in paper-local mm)
        static readonly Vector2 TrHeaderPlatePos = new Vector2(0f, 540f), TrHeaderPlateSize = new Vector2(1240f, 200f);
        static readonly Vector2 TrHeaderPos = new Vector2(0f, 585f), TrHeaderSize = new Vector2(1200f, 104f);
        static readonly Vector2 TrSubtitlePos = new Vector2(0f, 482f), TrSubtitleSize = new Vector2(1200f, 64f);
        static readonly Vector2 TrPaperPos = new Vector2(0f, -25f), TrPaperSize = new Vector2(1240f, 890f);
        /// <summary>The photo (canvas-local): 580 x 456 = 14:11 like the 1400 x 1100 source, on the paper's top-left.</summary>
        static readonly Vector2 TrPhotoPos = new Vector2(-315f, 177f), TrPhotoSize = new Vector2(580f, 456f);
        static readonly Vector2 TrPerksPos = new Vector2(300f, 202f), TrPerksSize = new Vector2(610f, 456f);   // paper-local, right of the photo
        static readonly Vector2 TrCovePos = new Vector2(0f, -238f), TrCoveSize = new Vector2(1210f, 384f);     // paper-local, under the photo
        const float TrHeaderMax = 80f, TrHeaderMin = 52f, TrSubtitleMax = 40f, TrSubtitleMin = 24f;
        const float TrPerksMax = 34f, TrPerksMin = 20f, TrCoveMax = 36f, TrCoveMin = 22f, TrLineSpacing = 10f;
        const float TrPhotoAspect = 1400f / 1100f;

        // ---------------------------------------------------------------- Actions strip (its own canvas, 1240 x 150 mm, centre 565 mm under the face's centre)
        const float TrActionsY = -565f;
        static readonly Vector2 TrActionsSize = new Vector2(1240f, 150f);
        static readonly Vector2 TrStatusPos = new Vector2(-375f, 32f), TrStatusSize = new Vector2(450f, 60f);
        static readonly Vector2 TrGiftNotePos = new Vector2(-375f, -36f), TrGiftNoteSize = new Vector2(450f, 52f);
        static readonly Vector2 TrStorePos = new Vector2(-10f, 0f), TrStoreSize = new Vector2(250f, 120f);
        static readonly Vector2 TrPortalPos = new Vector2(275f, 0f), TrPortalSize = new Vector2(300f, 120f);
        static readonly Vector2 TrDetailsBtnPos = new Vector2(520f, 0f), TrDetailsBtnSize = new Vector2(170f, 120f);
        const float TrStatusMax = 34f, TrStatusMin = 18f, TrGiftNoteMax = 26f, TrGiftNoteMin = 16f;
        const float TrStoreMax = 40f, TrStoreMin = 20f, TrButtonMax = 32f, TrButtonMin = 18f;

        // ---------------------------------------------------------------- Details popup (its own canvas over the face's paper, 1240 x 890 mm)
        static readonly Vector2 TrBodyPos = new Vector2(0f, 140f), TrBodySize = new Vector2(1160f, 560f);
        static readonly Vector2 TrUrlCaptionPos = new Vector2(0f, -180f), TrUrlCaptionSize = new Vector2(1160f, 50f);
        static readonly Vector2 TrUrlPos = new Vector2(0f, -250f), TrUrlSize = new Vector2(1160f, 64f);
        static readonly Vector2 TrClosePos = new Vector2(0f, -365f), TrCloseSize = new Vector2(320f, 100f);
        const float TrBodyMax = 34f, TrBodyMin = 20f, TrUrlCaptionMax = 30f, TrUrlCaptionMin = 18f, TrUrlPoints = 22f, TrCloseMax = 36f, TrCloseMin = 20f;
        const int TrActionsSorting = 1, TrDetailsSorting = 2;
        const float ButtonCornerMultiplier = 2f;   // KD_UiRound's corners on the mm canvas (the cut callouts use 4 for ~7 mm)
        static readonly Color PortalBlue = new Color(0.22f, 0.42f, 0.62f);

        // ---------------------------------------------------------------- placement
        /// <summary>Sideways start points from the Kominka board's centre along its own x (metres). The footprint is 2.1 m long (roof), so two
        /// boards need > 2.1 m between centres; the Kominka portal's rect spans 0.95 .. 2.55 m on its side. Away side first, then beyond the portal.</summary>
        static readonly float[] TrainerAwayOffsets = { 2.3f, 2.7f }, TrainerPortalSideOffsets = { 4.1f, 4.5f };
        const float TrainerMaxOut = 1.0f, TrainerBoardGap = 0.15f, TrainerPortalGap = 0.10f, TrainerPortalsGap = 0.30f, TrainerPortalStep = 0.6f;

        // ================================================================ context (called from Resolve)

        static void ResolveTrainer(Ctx c, bool forBuild, List<VRC.SDK3.Components.VRCPortalMarker> cove)
        {
            var passes = UnityEngine.Object.FindObjectsOfType<KH_TrainerPass>(true);
            if (passes.Length != 1) throw new InvalidOperationException(passes.Length + " KH_TrainerPass in the scene (expected KH_Dogs/KH_TrainerPass once); nothing changed");
            c.trainerPass = passes[0];
            c.covePhoto = AssetDatabase.LoadAssetAtPath<Texture2D>(CovePhotoPath);
            if (forBuild && c.covePhoto == null) throw new InvalidOperationException(CovePhotoPath + " not imported (refresh first); nothing changed");
            if (cove.Count > 1) throw new InvalidOperationException(cove.Count + " VRCPortalMarker with the cove's world id " + CoveRoomId + " (keep one); nothing changed");
            c.covePortal = cove.Count == 1 ? cove[0] : null;
            if (c.covePortal != null)
            {
                var go = c.covePortal.gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                    throw new InvalidOperationException("the cove portal marker is inside another prefab instance (" + KD_SceneObjects.PathOf(go) + "): it cannot be re-parented; nothing changed");
            }
            // Codex review 2026-10-16 中2: the cove marker's prefab, resolved before anything is written: the SDK prefab by path, else the
            // Kominka marker's prefab source. No scene copy.
            c.portalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SdkPortalPrefabPath);
            if (c.portalPrefab == null && c.portal != null && PrefabUtility.IsOutermostPrefabInstanceRoot(c.portal.gameObject))
                c.portalPrefab = PrefabUtility.GetCorrespondingObjectFromSource(c.portal.gameObject);
            if (c.portalPrefab != null && c.portalPrefab.GetComponent<VRC.SDK3.Components.VRCPortalMarker>() == null) c.portalPrefab = null;
            if (forBuild && c.portalPrefab == null)
                throw new InvalidOperationException("the portal prefab (" + SdkPortalPrefabPath + ", or the Kominka marker's prefab source) with a VRCPortalMarker on its root was not found; nothing changed");
            if (forBuild)
            {
                // Codex review 2026-10-16 中1: what the build would trip over later is checked before anything is written
                var oldActions = c.root != null ? c.root.Find(Trainer.name + "/" + ActionsName) : null;
                var oldTb = oldActions != null ? oldActions.GetComponent<KH_TrainerBoard>() : null;
                if (oldTb != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(oldTb) == null)
                    throw new InvalidOperationException("KH_TrainerBoard on " + Trainer.name + "/" + ActionsName + " has no backing UdonBehaviour (compile U#, then Guide/1 again); nothing changed");
                var prog = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(TrainerProgramPath + ".asset");
                if (prog != null && (prog.sourceCsScript == null || prog.sourceCsScript.GetClass() != typeof(KH_TrainerBoard)))
                    throw new InvalidOperationException(TrainerProgramPath + ".asset does not point at KH_TrainerBoard.cs (compile U#, or recreate it with Guide/0); nothing changed");
                if (!(AssetImporter.GetAtPath(CovePhotoPath) is TextureImporter))
                    throw new InvalidOperationException(CovePhotoPath + " has no TextureImporter (refresh first); nothing changed");
            }
            var anchors = c.scene.GetRootGameObjects().Where(g => g.name == CovePortalName).ToArray();
            if (anchors.Length > 1) throw new InvalidOperationException(anchors.Length + " " + CovePortalName + " at the scene root (keep one); nothing changed");
            KD_SceneObjects.Find(CovePortalName, out int anywhere);
            if (anywhere != anchors.Length) throw new InvalidOperationException(CovePortalName + " exists below another object (it belongs at the scene root); nothing changed");
            c.coveAnchor = anchors.Length == 1 ? anchors[0].transform : null;
            if (forBuild && AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(TrainerProgramPath + ".asset") == null)
                throw new InvalidOperationException(TrainerProgramPath + ".asset missing: run Guide/0, compile U# (unity-compile.sh --udon), then Guide/1; nothing changed");
            var missing = new List<string>();
            foreach (var lang in c.catalog.languages)
                foreach (var k in TrainerWordKeys)
                {
                    var e = lang.texts.FirstOrDefault(x => x.key == k);
                    if (e == null || string.IsNullOrWhiteSpace(e.text)) missing.Add(lang.code + ":" + k);
                }
            if (missing.Count > 0) throw new InvalidOperationException("trainer board words missing in " + KD_MenuFinishEditor.CatalogPath + ": " + string.Join(", ", missing.Take(12)) + "; nothing changed");
        }

        /// <summary>The ja / en glyphs the trainer board draws besides its bound labels (the dynamic words, their ja fallbacks, the URL).</summary>
        static void TrainerGlyphs(Ctx c, System.Text.StringBuilder s)
        {
            s.Append(CoveUrl);
            foreach (var w in TrainerJaFallbacks) s.Append(w);
            foreach (var lang in c.catalog.languages)
            {
                if (!KD_LocalizationEditor.Bundled(lang)) continue;
                foreach (var k in TrainerWordKeys) s.Append(Visible(KD_LocalizationEditor.Text(c.catalog, lang.code, k)));
            }
        }

        // ================================================================ pose

        /// <summary>Where the Kominka board stands after this run: the searched pose, else where it is. y = its seat on the terrain.</summary>
        static void PlannedKominka(Ctx c, Vector3? kominkaPose, out Vector3 kp, out float kyaw)
        {
            if (kominkaPose.HasValue) { kp = new Vector3(kominkaPose.Value.x, 0f, kominkaPose.Value.z); kyaw = kominkaPose.Value.y; }
            else
            {
                var k = c.root != null ? c.root.Find(Kominka.name) : null;
                if (k == null) throw new InvalidOperationException("the Trainer board is placed beside the Kominka board, which is missing; nothing changed");
                kp = k.position; kp.y = 0f; kyaw = k.eulerAngles.y;
            }
            var rot = Quaternion.Euler(0f, kyaw, 0f);
            float gy = float.NegativeInfinity;
            foreach (var sp in SeatSamples(Kominka)) gy = Mathf.Max(gy, Ground(kp + rot * sp));
            kp.y = gy;
        }

        /// <summary>+1 / -1: the cove portal's side of the Trainer board (board-local x): the side away from the Kominka board.</summary>
        static float TrainerPortalSide(Vector3 pos, float yaw, Vector3 kominkaPos)
        {
            float d = Vector3.Dot(Flat3(pos - kominkaPos), Quaternion.Euler(0f, yaw, 0f) * Vector3.right);
            return d >= 0f ? 1f : -1f;
        }

        static string Compass(Vector3 dir)
        {
            float b = Mathf.Repeat(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 360f);
            string[] n = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return n[Mathf.RoundToInt(b / 45f) % 8];
        }

        /// <summary>Null = keep the Trainer board where it is (it exists and passes every rule there). Otherwise Vector3(x, yaw, z). Nothing passes:
        /// an exception before any change.</summary>
        static Vector3? FindTrainerPose(Ctx c, Vector3? kominkaPose, out string how)
        {
            Vector3 kp; float kyaw;
            PlannedKominka(c, kominkaPose, out kp, out kyaw);
            var a = FindAnchors(c);
            var log = new List<string>();
            var existing = c.root != null ? c.root.Find(Trainer.name) : null;
            if (existing != null)
            {
                var broken = TrainerRules(c, a, existing.position, existing.eulerAngles.y, kp, kyaw, existing, true);
                if (broken.Count == 0) { how = "kept at " + V(existing.position) + " yaw " + existing.eulerAngles.y.ToString("F1") + " (clear, all rules pass)"; return null; }
                log.Add("current " + V(existing.position) + ": " + string.Join("; ", broken));
            }
            var krot = Quaternion.Euler(0f, kyaw, 0f);
            float ks = PortalSide(c, kp, krot);
            var tries = TrainerAwayOffsets.Select(d => -ks * d).Concat(TrainerPortalSideOffsets.Select(d => ks * d)).ToArray();
            foreach (var off in tries)
            {
                var dir = krot * Vector3.right * Mathf.Sign(off);
                string name = Mathf.Abs(off).ToString("F1") + " m " + Compass(dir) + " of the Kominka board (" + (Mathf.Sign(off) == Mathf.Sign(ks) ? "beyond its portal" : "away from its portal") + ")";
                var start = kp + krot * new Vector3(off, 0f, 0f); start.y = 0f;
                string why;
                var got = TryTrainer(c, a, start, kyaw, kp, kyaw, existing, out why);
                if (got.HasValue) { how = name + ": " + why + (log.Count > 0 ? " | rejected before: " + string.Join(" | ", log) : ""); return got; }
                log.Add(name + " " + V(start) + ": " + why);
            }
            throw new InvalidOperationException("Trainer board: no start point passes: " + string.Join(" | ", log) + "; nothing changed");
        }

        /// <summary>From a start point, step out along the front normal (0.1 m up to TrainerMaxOut) to the first point where the board volume and
        /// the cove portal's volume are clear of solid colliders, then judge every rule there. Vector3(x, yaw, z) or null (why says why).</summary>
        static Vector3? TryTrainer(Ctx c, Anch a, Vector3 start, float yaw, Vector3 kp, float kyaw, Transform self, out string why)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 front = rot * Vector3.back;
            var tried = new List<string>();
            for (float d = 0f; d <= TrainerMaxOut + 1e-4f; d += KomStep)
            {
                var pos = start + front * d; pos.y = 0f;
                float gy = float.NegativeInfinity;
                foreach (var sp in SeatSamples(Trainer)) gy = Mathf.Max(gy, Ground(pos + rot * sp));
                pos.y = gy;
                float side = TrainerPortalSide(pos, yaw, kp);
                var hb = SolidIn(c, pos, rot, Footprint(Trainer), gy + 0.05f, gy + 2.8f);
                float pg = Ground(pos + rot * new Vector3(side * PortalSideX, 0f, BackZ));
                var hp = SolidIn(c, pos, rot, PortalRect(side), pg + 0.05f, pg + 2.1f);
                if (hb.Count > 0 || hp.Count > 0)
                {
                    if (tried.Count < 3) tried.Add(d.ToString("F1") + " m blocked by " + string.Join(", ", hb.Concat(hp).Distinct().Take(2)));
                    continue;
                }
                var broken = TrainerRules(c, a, pos, yaw, kp, kyaw, self, true);
                if (broken.Count > 0) { why = "first clear point " + d.ToString("F1") + " m out " + V(pos) + " breaks " + string.Join("; ", broken); return null; }
                why = "stepped " + d.ToString("F1") + " m out from " + V(start) + " to " + V(pos) + " yaw " + yaw.ToString("F1") + "; cove portal at "
                    + V(pos + rot * new Vector3(side * PortalSideX, 0f, BackZ)) + (tried.Count > 0 ? " (" + string.Join("; ", tried) + ")" : "");
                return new Vector3(pos.x, yaw, pos.z);
            }
            why = "no clear point within " + TrainerMaxOut + " m (" + string.Join("; ", tried) + ")";
            return null;
        }

        /// <summary>Every placement rule of the Trainer board and its cove portal (Guide/1 decides with it, Guide/2 judges with it; one function so
        /// they cannot disagree). <paramref name="kp"/> / <paramref name="kyaw"/> = the Kominka board's pose; <paramref name="self"/> = the existing
        /// Trainer board (skipped among the other boards) or null.</summary>
        static List<string> TrainerRules(Ctx c, Anch a, Vector3 pos, float yaw, Vector3 kp, float kyaw, Transform self, bool physics)
        {
            var bad = new List<string>();
            var rot = Quaternion.Euler(0f, yaw, 0f); var krot = Quaternion.Euler(0f, kyaw, 0f);
            var foot = RectPoly(pos, rot, Footprint(Trainer));
            var readers = RectPoly(pos, rot, ReaderArea(Trainer));
            var komFoot = RectPoly(kp, krot, Footprint(Kominka));
            var komReaders = RectPoly(kp, krot, ReaderArea(Kominka));
            float ks = PortalSide(c, kp, krot), ts = TrainerPortalSide(pos, yaw, kp);
            var komPortal = RectPoly(kp, krot, PortalRect(ks));
            var coveRect = RectPoly(pos, rot, PortalRect(ts));
            var portal = pos + rot * new Vector3(ts * PortalSideX, 0f, BackZ);
            Func<float, string> f2 = x => x.ToString("F2");

            // ---- the Kominka board and the two portals ----
            float g;
            if ((g = PolyDist(foot, komFoot)) < TrainerBoardGap) bad.Add("footprint " + f2(g) + " m from the Kominka board's (< " + TrainerBoardGap + ")");
            if ((g = PolyDist(foot, komPortal)) < TrainerPortalGap) bad.Add("footprint " + f2(g) + " m from where the Kominka portal appears (< " + TrainerPortalGap + ")");
            if ((g = PolyDist(coveRect, komFoot)) < TrainerPortalGap) bad.Add("cove portal " + f2(g) + " m from the Kominka board (< " + TrainerPortalGap + ")");
            if ((g = PolyDist(coveRect, komPortal)) < TrainerPortalsGap) bad.Add("cove portal " + f2(g) + " m from the Kominka portal (< " + TrainerPortalsGap + ")");
            if (PolyDist(readers, komPortal) <= 0f) bad.Add("the Kominka portal appears where this board's readers stand");
            if (PolyDist(coveRect, komReaders) <= 0f) bad.Add("the cove portal appears where the Kominka board's readers stand");
            if (PolyDist(readers, komFoot) <= 0f || PolyDist(foot, komReaders) <= 0f) bad.Add("overlaps the Kominka board's readers' area");

            // ---- gate, spawn, entrance ----
            if (a.gate != null)
            {
                Vector3 gf3 = a.gate.forward; gf3.y = 0f; gf3.Normalize();
                Vector2 o = XZ(a.gate.position), gf = XZ(gf3);
                float side = foot.Min(q => Vector2.Dot(q - o, gf)), pside = coveRect.Min(q => Vector2.Dot(q - o, gf));
                if (side < GateSideMinKominka) bad.Add("only " + f2(side) + " m in front of the gate line (< " + GateSideMinKominka + ")");
                if (pside < 0f) bad.Add("the cove portal reaches behind the gate line (" + f2(pside) + " m)");
            }
            if (a.spawn != null)
            {
                float ds = PolyDist(new[] { XZ(a.spawn.position) }, foot), dp = Flat(portal - a.spawn.position), dc = Flat(pos - a.spawn.position);
                if (ds < KominkaSpawnClear) bad.Add("board " + f2(ds) + " m from the spawn (< " + KominkaSpawnClear + ")");
                if (dp < PortalSpawnClear) bad.Add("cove portal " + f2(dp) + " m from the spawn (< " + PortalSpawnClear + ")");
                if (dc > EntranceRadius) bad.Add("centre " + dc.ToString("F1") + " m from the spawn (> " + EntranceRadius + ")");
                var pv = PortalInTurnAround(a, portal);
                if (pv != null) bad.Add("cove portal in the turn-around view to " + pv);
            }
            // ---- dogs, loop, wash station ----
            foreach (var r in a.roams) if (r != null && (g = PolyDist(new[] { XZ(r.position) }, foot)) < RoamClear) { bad.Add("board " + f2(g) + " m from the dog roam centre " + r.name + " (< " + RoamClear + ")"); break; }
            if (a.route != null && a.route.Length > 1)
            {
                int n = a.route.Length, segs = a.closed ? n : n - 1; float rf = float.PositiveInfinity, rp = float.PositiveInfinity;
                for (int i = 0; i < segs; i++)
                {
                    rf = Mathf.Min(rf, PolyDist(new[] { XZ(a.route[i]), XZ(a.route[(i + 1) % n]) }, foot));
                    rp = Mathf.Min(rp, SegDist(portal, a.route[i], a.route[(i + 1) % n]));
                }
                if (rf < RouteClear) bad.Add("board " + f2(rf) + " m from the loop line (< " + RouteClear + ")");
                if (rp < 2.5f) bad.Add("cove portal " + f2(rp) + " m from the loop line (< 2.5)");
            }
            var readerPts = SamplesAt(pos, rot, ReaderArea(Trainer), 0.25f);
            if (a.washFloor != null && a.washMain != null)
            {
                var sq = RectPoly(a.washFloor.position, Quaternion.Euler(0f, a.washMain.rotation.eulerAngles.y, 0f), Rect.MinMaxRect(-WashFloorHalf, -WashFloorHalf, WashFloorHalf, WashFloorHalf));
                if (PolyDist(foot, sq) < WashFloorClear) bad.Add("board on / near the wash floor");
                if (OutsideSquare(portal, a.washFloor.position, a.washMain.rotation, WashFloorHalf) < 1.0f) bad.Add("cove portal on / near the wash floor");
                var inv = Quaternion.Inverse(Quaternion.Euler(0f, a.washMain.eulerAngles.y, 0f));
                bool work = readerPts.Concat(new[] { portal }).Any(q => { var l = inv * (q - a.washMain.position); return Mathf.Abs(l.x) < WorkHalfX + 1f && l.z > WorkMinZ - 1f && l.z < WorkMaxZ + 1f; });
                if (work) bad.Add("readers / cove portal within 1 m of the wash station's working area");
                float centre = Flat(pos + rot * new Vector3(0f, 0f, ReaderArea(Trainer).center.y) - a.washMain.position);
                if (centre < 4f) bad.Add("readers " + centre.ToString("F1") + " m from the wash station (< 4)");
            }
            foreach (var t in new[] { a.rack, a.keeper, a.welcome }.Concat(a.treats))
                if (t != null && Mathf.Min(PolyDist(new[] { XZ(t.position) }, foot), Flat(portal - t.position)) < FixtureClear) bad.Add("within " + FixtureClear + " m of " + t.name);
            var photo = ScenePath(c.scene, "KH_PhotoBoard");
            if (photo != null && Flat(portal - photo.position) < PhotoPortalClear) bad.Add("cove portal " + f2(Flat(portal - photo.position)) + " m from KH_PhotoBoard (< " + PhotoPortalClear + ")");

            // ---- boards A / B (no colliders: physics does not see them), then every other board, poster and stand by its renderers ----
            var footPts = SamplesAt(pos, rot, Footprint(Trainer), 0.1f);
            var portalPts = SamplesAt(pos, rot, PortalRect(ts), 0.1f);
            foreach (var other in new[] { Asob, MapBoard })
            {
                var ot = c.root != null ? c.root.Find(other.name) : null;
                if (ot == null) continue;
                var of = Footprint(other);
                float best = footPts.Concat(portalPts).Concat(readerPts).Min(q => Mathf.Max(0f, RectGap(of, ot.InverseTransformPoint(q))));
                if (best < 1.5f) bad.Add("board / cove portal / readers " + f2(best) + " m from board " + other.name + " (< 1.5)");
            }
            var invSelf = Quaternion.Inverse(rot);
            foreach (var o in OtherBoards(c, self))
            {
                if (o.t == self || (c.root != null && o.t.parent == c.root && Boards.Any(x => x.name == o.t.name))) continue;
                float gap = footPts.Min(q => RectGap(o.foot, o.t.InverseTransformPoint(q)));
                if (gap < StandBoardGap) bad.Add("footprint " + f2(gap) + " m from " + o.name + "'s (< " + StandBoardGap + ")");
                if (footPts.Any(q => RectGap(o.readers, o.t.InverseTransformPoint(q)) <= 0f)) bad.Add("stands in " + o.name + "'s readers' area");
                if (portalPts.Any(q => RectGap(o.foot, o.t.InverseTransformPoint(q)) <= 0f || RectGap(o.readers, o.t.InverseTransformPoint(q)) <= 0f)) bad.Add("the cove portal appears on " + o.name + " or where its readers stand");
                if (Samples(o.t, o.foot, 0.2f).Any(q => RectGap(ReaderArea(Trainer), invSelf * (q - pos)) <= 0f)) bad.Add(o.name + " stands in this board's readers' area");
            }

            // ---- physics and ground ----
            if (physics)
            {
                float gy = pos.y;
                var hb = SolidIn(c, pos, rot, Footprint(Trainer), gy + 0.05f, gy + 2.8f);
                float pg = Ground(portal);
                var hp = SolidIn(c, pos, rot, PortalRect(ts), pg + 0.05f, pg + 2.1f);
                var rr = ReaderArea(Trainer);
                float ry = Ground(pos + rot * new Vector3(0f, 0f, rr.center.y));
                var hr = SolidIn(c, pos, rot, rr, ry + 0.3f, ry + 1.9f);
                if (hb.Count > 0) bad.Add("solid collider in the board's volume (" + string.Join(", ", hb.Take(3)) + ")");
                if (hp.Count > 0) bad.Add("solid collider where the cove portal appears (" + string.Join(", ", hp.Take(3)) + ")");
                if (hr.Count > 0) bad.Add("solid collider where the readers stand (" + string.Join(", ", hr.Take(3)) + ")");
                var heights = footPts.Concat(readerPts).Select(q => Ground(q)).ToList();
                float range = heights.Max() - heights.Min();
                if (range > SlopeFail) bad.Add("terrain range over the board + readers' area " + f2(range) + " m (> " + SlopeFail + ")");
                if (Mathf.Abs(pg - gy) > TrainerPortalStep) bad.Add("the cove portal's ground is " + (pg - gy).ToString("+0.00;-0.00") + " m from the board's (> " + TrainerPortalStep + ")");
            }
            return bad;
        }

        // ================================================================ build (called from Build for b == Trainer)

        /// <summary>The face, the Actions strip, the Details popup, KH_TrainerBoard and its wiring, the cove portal. Returns the extra canvases
        /// (Actions, Details) for the tray's holes.</summary>
        static List<RectTransform> BuildTrainer(Transform board, RectTransform canvas, Ctx c, HashSet<Transform> keep, BuildReport r)
        {
            // ---- the face ----
            Img(canvas, "HeaderPlate", TrHeaderPlatePos, TrHeaderPlateSize, HeaderPlateColor, null, keep);
            Txt(canvas, "Header", TrHeaderPos, TrHeaderSize, TrHeaderMax, TrHeaderMin, TextAlignmentOptions.Center, SignInk, false, c, keep);
            Txt(canvas, "Subtitle", TrSubtitlePos, TrSubtitleSize, TrSubtitleMax, TrSubtitleMin, TextAlignmentOptions.Center, SignInk, false, c, keep);
            var paper = Img(canvas, "Paper", TrPaperPos, TrPaperSize, Washi, null, keep);
            var perks = Txt(paper.transform, "Perks", TrPerksPos, TrPerksSize, TrPerksMax, TrPerksMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            perks.lineSpacing = TrLineSpacing; EditorUtility.SetDirty(perks);
            var cove = Txt(paper.transform, "Cove", TrCovePos, TrCoveSize, TrCoveMax, TrCoveMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            cove.lineSpacing = TrLineSpacing; EditorUtility.SetDirty(cove);
            var photoGo = KD_ShopSetupEditor.EnsureUiChild(canvas, "Photo", TrPhotoPos, TrPhotoSize);
            photoGo.layer = 0;
            foreach (var gr in photoGo.GetComponents<Graphic>()) if (!(gr is RawImage)) UnityEngine.Object.DestroyImmediate(gr);
            var raw = photoGo.GetComponent<RawImage>(); if (raw == null) raw = photoGo.AddComponent<RawImage>();
            raw.texture = c.covePhoto; raw.color = Color.white; raw.raycastTarget = false; raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            EditorUtility.SetDirty(raw);
            photoGo.transform.SetSiblingIndex(paper.transform.GetSiblingIndex() + 1);   // drawn over the paper
            keep.Add(photoGo.transform);

            // ---- the Actions strip (an operation canvas) ----
            var actions = PosterCanvas(board, ActionsName, TrActionsSize, Mm, new Vector3(0f, CanvasCenterY(Trainer) + TrActionsY * Mm, TrayFront), true, keep);
            SetSorting(actions, TrActionsSorting);
            Img(actions, "Plate", Vector2.zero, TrActionsSize, HeaderPlateColor, null, keep);
            var status = Txt(actions, "Status", TrStatusPos, TrStatusSize, TrStatusMax, TrStatusMin, TextAlignmentOptions.Left, SignInk, true, c, keep);
            var giftNote = Txt(actions, "GiftNote", TrGiftNotePos, TrGiftNoteSize, TrGiftNoteMax, TrGiftNoteMin, TextAlignmentOptions.Left, SignInk, true, c, keep);
            TextMeshProUGUI storeLabel, portalLabel, detailsLabel, closeLabel;
            var storeBtn = TrainerButton(actions, StoreButtonName, TrStorePos, TrStoreSize, Vermilion, Washi, TrStoreMax, TrStoreMin, c, keep, out storeLabel);
            TrainerButton(actions, PortalButtonName, TrPortalPos, TrPortalSize, PortalBlue, Washi, TrButtonMax, TrButtonMin, c, keep, out portalLabel);
            TrainerButton(actions, DetailsButtonName, TrDetailsBtnPos, TrDetailsBtnSize, StripWashi, Sumi, TrButtonMax, TrButtonMin, c, keep, out detailsLabel);
            // the saved scene shows the "checking" state; KH_TrainerBoard re-renders on Start
            status.text = KH_TrainerBoard.JaLoading; storeLabel.text = KH_TrainerBoard.JaJoin; giftNote.text = KH_TrainerBoard.JaGiftNote;
            EditorUtility.SetDirty(status); EditorUtility.SetDirty(storeLabel); EditorUtility.SetDirty(giftNote);
            if (giftNote.gameObject.activeSelf) giftNote.gameObject.SetActive(false);

            // ---- the Details popup (an operation canvas over the face's paper) ----
            var details = PosterCanvas(board, DetailsName, TrPaperSize, Mm, new Vector3(0f, CanvasCenterY(Trainer) + TrPaperPos.y * Mm, TrayFront), true, keep);
            SetSorting(details, TrDetailsSorting);
            Img(details, "Paper", Vector2.zero, TrPaperSize, Washi, null, keep);
            var body = Txt(details, "Body", TrBodyPos, TrBodySize, TrBodyMax, TrBodyMin, TextAlignmentOptions.TopLeft, Sumi, true, c, keep);
            body.lineSpacing = TrLineSpacing; EditorUtility.SetDirty(body);
            Txt(details, "UrlCaption", TrUrlCaptionPos, TrUrlCaptionSize, TrUrlCaptionMax, TrUrlCaptionMin, TextAlignmentOptions.Left, Sumi, false, c, keep);
            var field = KD_MenuFinishEditor.CopyField(details, "UrlField", TrUrlPos, TrUrlSize, CoveUrl, c.font, false);
            field.pointSize = TrUrlPoints;
            if (field.textComponent != null) { field.textComponent.font = c.font; EditorUtility.SetDirty(field.textComponent); }
            EditorUtility.SetDirty(field);
            foreach (var t in field.GetComponentsInChildren<Transform>(true)) { keep.Add(t); t.gameObject.layer = 0; }
            TrainerButton(details, CloseButtonName, TrClosePos, TrCloseSize, HeaderPlateColor, SignInk, TrCloseMax, TrCloseMin, c, keep, out closeLabel);
            if (details.gameObject.activeSelf) details.gameObject.SetActive(false);   // KH_TrainerBoard opens it per player

            // ---- the runtime and its wiring ----
            var anchor = EnsureCovePortal(c, board, r);
            var tb = actions.GetComponent<KH_TrainerBoard>();
            if (tb == null) { tb = actions.gameObject.AddUdonSharpComponent<KH_TrainerBoard>(); r.log.Add("added KH_TrainerBoard on " + Trainer.name + "/" + ActionsName); }
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(tb);
            if (backing == null) throw new InvalidOperationException("KH_TrainerBoard backing missing (compile U# after Guide/0, then Guide/1 again; reopen the scene without saving)");
            if (backing.SyncMethod != VRC.SDKBase.Networking.SyncType.None) { backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None; EditorUtility.SetDirty(backing); }
            tb.pass = c.trainerPass; tb.language = c.language; tb.portal = anchor.gameObject; tb.details = details.gameObject;
            tb.status = status; tb.storeLabel = storeLabel; tb.giftNote = giftNote; tb.storeButton = storeBtn; tb.showSeconds = PortalShowSeconds;
            UdonSharpEditorUtility.CopyProxyToUdon(tb); EditorUtility.SetDirty(tb);
            WireClick(storeBtn, backing, StoreEvent, r.log);
            WireClick(actions.Find(PortalButtonName).GetComponent<Button>(), backing, PortalEvent, r.log);
            WireClick(actions.Find(DetailsButtonName).GetComponent<Button>(), backing, DetailsOpenEvent, r.log);
            WireClick(details.Find(CloseButtonName).GetComponent<Button>(), backing, DetailsCloseEvent, r.log);
            return new List<RectTransform> { actions, details };
        }

        static void SetSorting(RectTransform rt, int order)
        {
            var cv = rt.GetComponent<Canvas>();
            if (cv.sortingOrder != order) { cv.sortingOrder = order; EditorUtility.SetDirty(cv); }
        }

        /// <summary>A rounded button (KD_UiRound, Sliced) with a centred label; raycast target on the button face only.</summary>
        static Button TrainerButton(Transform parent, string name, Vector2 pos, Vector2 size, Color face, Color ink, float max, float min, Ctx c, HashSet<Transform> keep, out TextMeshProUGUI label)
        {
            var img = Img(parent, name, pos, size, face, c.round, keep);
            img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = ButtonCornerMultiplier; img.raycastTarget = true;
            EditorUtility.SetDirty(img);
            var btn = img.GetComponent<Button>(); if (btn == null) btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img; btn.transition = Selectable.Transition.ColorTint; btn.interactable = true;
            KD_UiSpriteEditor.ApplyButtonFeedback(btn);
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            EditorUtility.SetDirty(btn);
            label = Txt(img.transform, "Label", Vector2.zero, size - new Vector2(20f, 10f), max, min, TextAlignmentOptions.Center, ink, true, c, keep);
            return btn;
        }

        /// <summary>Exactly one persistent listener: target.SendCustomEvent(evt), String mode, not Off (the wash board's BuyWired, generalised).</summary>
        static bool ClickWired(Button btn, VRC.Udon.UdonBehaviour target, string evt, out string why)
        {
            why = null;
            if (btn == null) { why = "no Button"; return false; }
            if (target == null) { why = "no backing UdonBehaviour"; return false; }
            if (btn.onClick.GetPersistentEventCount() != 1) { why = "persistent listeners " + btn.onClick.GetPersistentEventCount(); return false; }
            if (btn.onClick.GetPersistentTarget(0) != target) { why = "target " + btn.onClick.GetPersistentTarget(0); return false; }
            if (btn.onClick.GetPersistentMethodName(0) != "SendCustomEvent") { why = "method " + btn.onClick.GetPersistentMethodName(0); return false; }
            var so = new SerializedObject(btn);
            var arg = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument");
            if (arg == null || arg.stringValue != evt) { why = "argument " + (arg == null ? "missing" : arg.stringValue); return false; }
            if (btn.onClick.GetPersistentListenerState(0) == UnityEngine.Events.UnityEventCallState.Off) { why = "call state Off"; return false; }
            var mode = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Mode");
            if (mode == null || mode.intValue != (int)UnityEngine.Events.PersistentListenerMode.String) { why = "argument mode " + (mode == null ? "missing" : ((UnityEngine.Events.PersistentListenerMode)mode.intValue).ToString()); return false; }
            return true;
        }

        static void WireClick(Button btn, VRC.Udon.UdonBehaviour target, string evt, List<string> log)
        {
            string why;
            if (ClickWired(btn, target, evt, out why)) return;
            for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(btn.onClick, i);
            UnityEventTools.AddStringPersistentListener(btn.onClick, target.SendCustomEvent, evt);
            EditorUtility.SetDirty(btn);
            log.Add("wired " + KD_SceneObjects.PathOf(btn.gameObject) + " -> " + evt + " (was: " + why + ")");
        }

        // ================================================================ the cove portal

        /// <summary>KH_CovePortal at the scene root beside the Trainer board (inactive in the saved scene), with the cove's VRCPortalMarker as its
        /// active child at local zero. The marker is created once from the Kominka portal's prefab (else a copy of the Kominka marker); only its
        /// roomId is written, and only on that first creation. The Kominka marker itself is never changed.</summary>
        static Transform EnsureCovePortal(Ctx c, Transform board, BuildReport r)
        {
            var anchor = c.coveAnchor;
            if (anchor == null)
            {
                anchor = new GameObject(CovePortalName).transform;
                SceneManagerMove(anchor.gameObject, c.scene);
                Undo.RegisterCreatedObjectUndo(anchor.gameObject, "Create " + CovePortalName);
                r.log.Add("created " + CovePortalName + " at the scene root");
                c.coveAnchor = anchor;
            }
            var kom = board.parent != null ? board.parent.Find(Kominka.name) : null;   // placed earlier in this run (Boards order); c.root is null on a first build
            if (kom == null) throw new InvalidOperationException("the Kominka board is missing beside the Trainer board; reopen the scene without saving");
            float side = TrainerPortalSide(board.position, board.eulerAngles.y, kom.position);
            var spot = board.TransformPoint(new Vector3(side * PortalSideX, 0f, BackZ));
            var want = new Vector3(spot.x, Ground(spot), spot.z);
            var wantRot = Quaternion.Euler(0f, board.eulerAngles.y + PortalYaw, 0f);
            if ((anchor.position - want).sqrMagnitude > 1e-8f || Quaternion.Angle(anchor.rotation, wantRot) > 0.01f)
            {
                r.log.Add(CovePortalName + " placed at " + V(want) + " yaw " + wantRot.eulerAngles.y.ToString("F1") + " (was " + V(anchor.position) + ")");
                anchor.SetPositionAndRotation(want, wantRot);
            }
            if (anchor.localScale != Vector3.one) anchor.localScale = Vector3.one;
            anchor.gameObject.layer = 0;

            var marker = c.covePortal;
            if (marker == null)
            {
                // Codex review 2026-10-16 中2: always an instance of the portal prefab resolved in ResolveTrainer (never a copy of a scene object)
                var go = c.portalPrefab != null ? PrefabUtility.InstantiatePrefab(c.portalPrefab, c.scene) as GameObject : null;
                if (go == null) throw new InvalidOperationException("could not instantiate the portal prefab " + (c.portalPrefab != null ? AssetDatabase.GetAssetPath(c.portalPrefab) : SdkPortalPrefabPath) + "; reopen the scene without saving");
                Undo.RegisterCreatedObjectUndo(go, "Create " + CoveMarkerName);
                r.log.Add("created the cove portal from the prefab " + AssetDatabase.GetAssetPath(c.portalPrefab));
                go.name = CoveMarkerName;
                go.transform.SetParent(anchor, false);
                marker = go.GetComponent<VRC.SDK3.Components.VRCPortalMarker>();
                if (marker == null) throw new InvalidOperationException("the new cove portal has no VRCPortalMarker; reopen the scene without saving");
                var so = new SerializedObject(marker);
                var room = so.FindProperty("roomId");
                if (room == null) throw new InvalidOperationException("VRCPortalMarker.roomId not found (SDK changed?); reopen the scene without saving");
                room.stringValue = CoveRoomId;
                so.ApplyModifiedProperties();
                c.covePortal = marker;
                r.log.Add("cove portal roomId = " + CoveRoomId);
            }
            var mt = marker.transform;
            if (mt.parent != anchor)
            {
                r.log.Add("moved the cove portal " + KD_SceneObjects.PathOf(mt.gameObject) + " under " + CovePortalName);
                mt.SetParent(anchor, false);
            }
            if (mt.localPosition != Vector3.zero || mt.localRotation != Quaternion.identity || mt.localScale != Vector3.one)
            { mt.localPosition = Vector3.zero; mt.localRotation = Quaternion.identity; mt.localScale = Vector3.one; }
            if (!mt.gameObject.activeSelf) mt.gameObject.SetActive(true);
            if (anchor.gameObject.activeSelf) anchor.gameObject.SetActive(false);   // hidden for everyone at load; KH_TrainerBoard shows it per player
            foreach (Transform t in anchor) if (t != mt) r.log.Add("WARN " + CovePortalName + "/" + t.name + " is not made by this builder: left as is");
            return anchor;
        }

        // ================================================================ the photo's import

        /// <summary>What differs from the photo's import: sRGB, opaque (no alpha: DXT1 on PC), mipmaps (seen from 1-3 m), POT-scaled, no
        /// Read/Write, max 1024, PC default compression (DXT1), Android / iOS ASTC 6x6 at 1024.</summary>
        static List<string> CovePhotoImportProblems(TextureImporter imp)
        {
            var p = new List<string>();
            if (imp == null) { p.Add("not imported"); return p; }
            if (imp.textureType != TextureImporterType.Default) p.Add("type " + imp.textureType);
            if (!imp.sRGBTexture) p.Add("not sRGB");
            if (imp.alphaSource != TextureImporterAlphaSource.None) p.Add("alpha source " + imp.alphaSource);
            if (!imp.mipmapEnabled) p.Add("no mipmaps");
            if (imp.npotScale != TextureImporterNPOTScale.ToNearest) p.Add("NPOT " + imp.npotScale);
            if (imp.isReadable) p.Add("Read/Write on");
            if (imp.maxTextureSize != 1024) p.Add("max " + imp.maxTextureSize);
            if (imp.textureCompression != TextureImporterCompression.Compressed) p.Add("compression " + imp.textureCompression);
            var pc = imp.GetPlatformTextureSettings("Standalone");
            if (pc.overridden && !(pc.format == TextureImporterFormat.DXT1 || pc.format == TextureImporterFormat.Automatic)) p.Add("Standalone override " + pc.format);
            if (pc.overridden && pc.maxTextureSize > 1024) p.Add("Standalone max " + pc.maxTextureSize);
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var s = imp.GetPlatformTextureSettings(platform);
                if (!s.overridden || s.maxTextureSize != 1024 || s.format != TextureImporterFormat.ASTC_6x6) p.Add(platform + " " + (s.overridden ? s.format + " " + s.maxTextureSize : "not overridden"));
            }
            return p;
        }

        /// <summary>The photo importer's fields EnsureCovePhotoImporter writes, as they were before (Codex review 2026-10-16 中1: put back if the build fails).</summary>
        class CovePhotoImport
        {
            public TextureImporterType type; public bool srgb, mip, readable; public TextureImporterAlphaSource alpha; public TextureImporterNPOTScale npot;
            public int max; public TextureImporterCompression compression;
            public TextureImporterPlatformSettings pc, android, iphone;
        }

        /// <summary>Sets the photo's import when it differs; returns the settings before (null = nothing changed).</summary>
        static CovePhotoImport EnsureCovePhotoImporter(List<string> log)
        {
            var imp = AssetImporter.GetAtPath(CovePhotoPath) as TextureImporter;
            if (imp == null) throw new InvalidOperationException(CovePhotoPath + " not imported (refresh first)");
            var was = CovePhotoImportProblems(imp);
            if (was.Count == 0) return null;
            var before = new CovePhotoImport
            {
                type = imp.textureType, srgb = imp.sRGBTexture, mip = imp.mipmapEnabled, readable = imp.isReadable, alpha = imp.alphaSource, npot = imp.npotScale,
                max = imp.maxTextureSize, compression = imp.textureCompression,
                pc = imp.GetPlatformTextureSettings("Standalone"), android = imp.GetPlatformTextureSettings("Android"), iphone = imp.GetPlatformTextureSettings("iPhone"),
            };
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.alphaSource = TextureImporterAlphaSource.None;
            imp.mipmapEnabled = true; imp.npotScale = TextureImporterNPOTScale.ToNearest; imp.isReadable = false;
            imp.maxTextureSize = 1024; imp.textureCompression = TextureImporterCompression.Compressed;
            var pc = imp.GetPlatformTextureSettings("Standalone");
            if (pc.overridden && !(pc.format == TextureImporterFormat.DXT1 || pc.format == TextureImporterFormat.Automatic) || pc.overridden && pc.maxTextureSize > 1024)
            { pc.overridden = false; imp.SetPlatformTextureSettings(pc); }
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var s = imp.GetPlatformTextureSettings(platform);
                s.overridden = true; s.maxTextureSize = 1024; s.format = TextureImporterFormat.ASTC_6x6;
                imp.SetPlatformTextureSettings(s);
            }
            imp.SaveAndReimport();
            log.Add("texture import set: " + CovePhotoPath + " (was: " + string.Join(", ", was) + ")");
            return before;
        }

        /// <summary>Puts back the photo's import as it was before this run (the build failed after EnsureCovePhotoImporter changed it).</summary>
        static void RestoreCovePhotoImporter(CovePhotoImport b, List<string> log)
        {
            try
            {
                var imp = AssetImporter.GetAtPath(CovePhotoPath) as TextureImporter;
                if (imp == null) return;
                imp.textureType = b.type; imp.sRGBTexture = b.srgb; imp.mipmapEnabled = b.mip; imp.isReadable = b.readable; imp.alphaSource = b.alpha; imp.npotScale = b.npot;
                imp.maxTextureSize = b.max; imp.textureCompression = b.compression;
                imp.SetPlatformTextureSettings(b.pc); imp.SetPlatformTextureSettings(b.android); imp.SetPlatformTextureSettings(b.iphone);
                imp.SaveAndReimport();
                log.Add("texture import of " + CovePhotoPath + " put back (the build failed)");
            }
            catch (Exception e) { Debug.LogError("[KH Guide Boards] could not put back the import of " + CovePhotoPath + ": " + e.Message); }
        }

        // ================================================================ helpers shared with Guide/2

        /// <summary>The board's canvases in the tray's front plane (the face; for Trainer also Actions and Details).</summary>
        static List<RectTransform> BoardCanvases(Transform board, Board b)
        {
            var list = new List<RectTransform>();
            var cv = board.Find(CanvasName) as RectTransform; if (cv != null) list.Add(cv);
            if (b == Trainer)
                foreach (var n in new[] { ActionsName, DetailsName }) { var x = board.Find(n) as RectTransform; if (x != null) list.Add(x); }
            return list;
        }

        /// <summary>The Trainer's operation canvases present in the scene (they alone may carry GraphicRaycaster / VRCUiShape / a trigger).</summary>
        static HashSet<Transform> TrainerOperationCanvases(Ctx c)
        {
            var set = new HashSet<Transform>();
            var t = c.root != null ? c.root.Find(Trainer.name) : null;
            if (t == null) return set;
            foreach (var n in new[] { ActionsName, DetailsName }) { var x = t.Find(n); if (x != null) set.Add(x); }
            return set;
        }
    }
}
