> 2026-10-12 の静的監査 (Opus サブエージェント、英語のまま収録。統括 Fable が実体数の census を Edit モードで取り、同期方式 28/19 を現物で確認)。[VER-ENV-002](../development/issues/VER-ENV-002-scenery-performance.md) の 2026-10-12 節から参照する。**計測ではない。** 対となる Udon 側は [2026-10-12-performance-inventory-udon.md](2026-10-12-performance-inventory-udon.md)。census の生データは `Temp/kh-census-*.tsv` (git 管理外。取り直しは eval で `FindObjectsOfType<T>(true)` を根ごとに数える)。

# Non-Udon scene audit: Quest 3 constant ~20 fps (2026-10-12)

Scope: what's drawn, shaders, world-space canvases, audio, physics and network sync, particles, scene settings. Read-only; no Unity, no Play.
Sources: `Temp/kh-census-*.tsv` (edit-mode census of the open scene; authoritative for counts), scene snapshots `scene_1003/1006/wt.unity` in the scratchpad (text parse; misses components inside prefab instances), code under `Assets/KominkaHillside/Scripts`, material and texture `.meta` files.
Labels: **[F]** = verified fact from files or code. **[E]** = estimate or inference. **[K]** = general Unity/VRChat knowledge, with a confidence level.

Spawn point (the scene descriptor's `spawns[0]` = `KH_World/KH_Places/KH_Entrance`) is at **(47.6, 1.6, 40.1)** [F]. Distances below are horizontal distances from there.

How exact the distances are: each census path is placed at its nearest transform that appears in the scene text. Objects inside prefab instances (the dogs' bodies, the kominka house interior) therefore inherit a parent's position, so dog and house distances are approximate. Canvas, wash, cart and river distances are exact, because those are plain scene transforms.

---

## 0. Headline

- The non-Udon growth near spawn since 10-06 is **small** [F]. Within 30 m of spawn the new items are: StationCart_0 plus the Station_0 board (6 renderers / 8 material pairs, 17 m away), the KH_WashStation premium board and fixtures (6 small renderers plus 1 canvas, 8 m), and the dog book (3 canvases, 8 m). Altogether that is roughly +15 to 40 draw calls [E]. Going from 40 fps to 20 fps means about +25 ms per frame. Draw calls on this scale cannot cost that.
- None of the 48 new AudioSources play at idle, and none of the 36 new ParticleSystems emit at idle [F]. All 47 wash rigidbodies are kinematic, and none of the colliders are non-convex [F]. No new light, reflection probe, render-texture camera or fog was added [F].
- The non-Udon items whose per-frame cost grows with their count are: **VRCObjectSync + VRCPickup (40 → 68)**, **enabled GraphicRaycasters (14 active; does VRChat poll them every frame? not verifiable here)**, and 35 active world-space canvases (draw calls only while in view; no idle rebuilds, verified).
- **The prime candidate for a constant cost at spawn is the Udon side** (UdonBehaviour 234 → 366, covered by the `udon-audit` agent). The A/B groups in §8b that switch off whole GameObject roots remove Udon and non-Udon cost together. They are labeled **mixed**.
- **Run this first, no build needed:** at spawn, look straight up at the sky. If it stays at ~20 fps, the cost is CPU (scripts, sync), not rendering. Better still, run the OVR Metrics Tool overlay and read GPU % and CPU %: GPU under ~80 % at 20 fps means the frame is CPU-bound.

---

## 1. World-space canvases (27 → 78; 35 active)

Growth by root, 10-06 → now (text parse) [F]: KH_Cart 0 → 37, KH_Dogs 12 → 19 (dog book stand 7), KH_WashStation 2 → 3 and KH_WashStations 6 → 9 (PremiumSelectionBoard ×4), KH_WalkIntro 0 → 2, KH_VideoSystem 1 → 2 (PatternPanel). Total +51 = 78.

### Active canvases (census), sorted by distance from spawn [F]

| dist | Canvas | graphics / TMP | GraphicRaycaster | VRCUiShape | new since 10-06 |
|---|---|---|---|---|---|
| 4 m | KH_GuideBoards/Kominka | 4 / 2 | – | – | no |
| 4 m | KH_Dogs/KH_LeashRack/WalkGuide | 30 / 21 | – | – | no (10-04→06) |
| 5 m | KH_PhotoBoard/Canvas | 12 / 2 | – | – | no |
| 7 m | KH_GuideBoards/Asobikata | 72 / 34 | – | – | no |
| 8 m | KH_WashStation/PremiumSelectionBoard | 41 / 19 | yes | yes | **yes** |
| 8 m | KH_WashStation/Board | 32 / 20 | yes | yes | no |
| 8 m | KH_WashStation/Guide | 17 / 12 | – | – | no |
| 8 m | KD_DogBook/Index/Visual + Index/Input + Stand/Canvas | 30/16 + 8/0 + 2/1 | Input: yes | Input: yes | **yes** |
| 11 m | KH_GuideBoards/Map | 19 / 12 | – | – | no |
| 17 m | KH_Cart/Stations/Station_0/Board | 32 / 21 | yes | yes | **yes** |
| 63 m* | KH_Cart/Carts/Cart_0..7/…/SignCaption ×8 | 2 / 1 each | – | – | **yes** |
| 77–97 m | Station_Viewpoint / Plaza WalkGuide, WashStations/Hillside ×3, Station_1 | 30–41 each | Hillside boards, Station_1 | yes | partly |
| 510–590 m | River / Upper wash ×6, Station_2/3/4 | 17–41 each | boards: yes | yes | partly |

\* The SignCaption canvases sit on the ride carts, which are parked at `KH_Cart/Parking` = (0, −50, 0), underground.

- **Within 17 m of spawn: 12 active canvases, 299 graphics, 160 TMP texts** [F]. Of these, 5 canvases (113 graphics, ~57 TMP) are new since 10-06.
- Inactive and costing nothing: 24 per-cart canvases (Panel/Board 32/21, Controls/Board 24/12, TabCanvas 2/1 per cart), KD_TreatPouchPanel (810/448), video monitor, WalkIntro ×2, the sleep and hint overlays [F].
- Active VRCUiShape (14) and active GraphicRaycaster correspond 1:1: 5 station boards, 8 wash boards and selection boards, and DogBook Index/Input [F, from kh-census-udon.tsv].

### Periodic UI writes while idle (do they force canvas rebuilds?)

| Script | Period while idle | What it writes | Rebuilds while idle? |
|---|---|---|---|
| `KH_CartStationPanel._Refresh` (KH_CartStationPanel.cs:50) | 1 s, only panels within 30 m (`KH_CartManager.cs:429`, `PanelNear=30` at :592). Station_0 is 17 m away, so it refreshes at spawn | title/notice/pr/start `.text`, joinLabels; `SetActive` and `interactable` only when changed | **No** [F]: same-string writes are dropped |
| `KH_WashSelectionBoard._Tick` (KH_WashSelectionBoard.cs:45–74) | **0.25 s on all 4 boards, no distance gate** | `status.text` only when busy or changed; `actionButton.interactable` every tick | **No** [F] |
| `KH_WashStationBoard.Update` (:255) | only within `showMeters`; writes guarded by `!= _shown` and per-dog change checks | | **No** [F] |
| `KD_DogBook.Update` → `Refresh()` (KD_DogBook.cs:158–165, 252–274) | **0.5 s, no distance gate** | ~20 `.text`, 6 `.color`, `portrait.texture`, 3 `interactable` | **No** [F] |
| `KH_CartControls`, `KH_CartSeat`, `KH_FurColor`, `KH_WalkIntro` | event-driven / Start only (WalkIntro moves its hint transforms in PostLateUpdate only when not Quiet; its canvases are inactive) | | No |

Why "no" [F]:
- `Library/PackageCache/com.unity.textmeshpro@3.0.6/Scripts/Runtime/TMP_Text.cs:125`: the `text` setter returns early when the new string equals the current one.
- UGUI 1.0.0: `Graphic.color` (Graphic.cs:156, `SetPropertyUtility.SetColor`), `RawImage.texture` (RawImage.cs:90, `if (m_Texture == value) return`) and `Selectable.interactable` (Selectable.cs:365, `SetStruct`) all early-return on equal values.
- [K, medium-high] The VRChat client bundles the same major TMP and UGUI versions.
- So these ticks cost Udon time (string building and extern calls, handed to udon-audit), not Canvas rebuilds.

### Draw calls and culling

- [K, high] Each world-space canvas batches separately: it gets its own batch set per material, texture and depth interleave. A board with a background, images and TMP text typically costs 3–8 batches. The 12 canvases near spawn come to roughly **35–80 UI draw calls when all are in view**, of which **~15–35 are new** [E].
- [K, high] Canvas geometry is frustum-culled like any renderer, so off-screen canvases don't draw. `Canvas.SendWillRenderCanvases` rebuilds only dirty canvases, and none are dirty while idle (above).
- [K, low; hypothesis] GraphicRaycaster polling: in stock Unity, `EventSystem.RaycastAll` asks every enabled raycaster each frame. VRChat's input module is believed to raycast the VRCUiShape collider first and only then query that canvas. This cannot be verified from the repo, so it is an A/B group (§8b), not a cost claim. There are 14 enabled raycasters, 6 of them within 17 m of spawn.

---

## 2. Unity `Standard` shader: 239 active renderer-material pairs (170 renderers)

**Caveat on the "71 → 239" comparison:** the 10-04 inventory counted the scene text, which can't see inside prefab instances. The census sees 44 Standard renderers in KH_World (the kominka house prefab: 障子, 木目, 柱, 瓦 and so on) that existed before 10-03 and were invisible to that count. The real growth is in the second table below.

Per material (active+enabled renderers; tris = sum of mesh triangles over LOD levels; multi-material renderers count once per material) [F]:

| Material | renderers | tris | roots | maps / settings |
|---|---|---|---|---|
| KH_OsanpoCart_Body | 78 | 68,848 | KH_Cart (8 ride + 5 station carts × LOD0/1 × body+2 axles) | `_MainTex` only (KH_OsanpoCart_BaseColor.png 1024², Android ASTC 6×6); Metallic 0, Gloss 0.25, **GlossyReflections 1, SpecularHighlights 1**, no emission, instancing on |
| KH_OsanpoCart_Sign | 26 | (2nd submesh of the body) | KH_Cart | `_MainTex` KH_CartSign.png 512×256, ASTC 6×6; same settings as Body |
| KH_WashTools | 19 | 16,832 | WashStation 7, WashStations 12 | `_MainTex` (1024 px source, Android 512) + `_METALLICGLOSSMAP` (256²); Gloss 0.18; reflections and highlights on |
| KH_PropFinish | 15 | 19,288 | Dogs 6, WashStations 6, WashStation 2, PhotoBoard 1 | **`_NORMALMAP` + `_METALLICGLOSSMAP`**, BaseColor/Normal/Metal at 2048 px, Android 1024 ASTC 6×6; Gloss 0.22; reflections and highlights on |
| PremiumWash_Fixture | 8 | 1,200 | WashStations 6, WashStation 2 (incl. 4 skinned TelescopicSupport_Moving) | **normal + metallic maps** at 1024 (Android 1024 ASTC 6×6; iPhone not overridden); Gloss 0.5; reflections and highlights on |
| PremiumWash_Tools / Addons / Window / Water | 0 active at idle | – | premium slot items are inactive until selected | Standard |
| KH_World house materials (障子 23, 木目 21, 引き戸取っ手 12, 御影石 9, 柱 3 7, 瓦 3, 畳 3, …) | 44 | ~21k | KH_World | old (pre-10-03) |
| M_KD_Ball_* | 6 | 4,608 | KH_Dogs | old |

Growth by period [F]:

| Period | Standard additions |
|---|---|
| 10-03 → 10-04 | PropFinish 15 + early wash tools |
| 10-06 → now | **Cart: 78 renderers / 104 pairs**, premium fixtures 8 (+ premium tools, inactive at idle), more wash tools |

Near spawn: KH_WashStation has 11 Standard renderers at 8 m (WashTools ×7, PropFinish ×2, Fixture ×2). StationCart_0 is at 17 m and shows LOD1 (2 renderers, 3 draw calls).

**Android build hook** [F]: the only IProcessSceneWithReport hooks are:
- `KH_MobileTerrainBuildEditor`: swaps the Terrain material only.
- `KD_AndroidToonMaterialsEditor`: keyed on root `====KominkaDogRun====`, which is **absent** from this scene, so it does nothing here.

`KH_CourseMenuBuildGuard`, `KH_CourseTravelBuildGuard` and `KD_LocalizationBuildGuard` are only checks (IVRCSDKBuildRequestedCallback). **No hook swaps the prop, cart or wash materials. They ship to Quest as Unity Standard.**

**BUG-ENV-001** (docs/development/issues/BUG-ENV-001-mirror-default-materials.md:64): it only changed values on 40 builder-made `VRChat/Mobile/Standard Lite` materials (metallic 1 → 0, gloss → 0.3, reflections and specular off). It did not touch Standard materials and swapped no shaders. It has no performance effect.

**Quest cost ranking** [K, medium-high; qualitative]:
- Standard (full PBR BRDF; with normal map + metallic map: 3 texture samples plus a tangent-space normal; GlossyReflections samples the skybox reflection cubemap via `unity_SpecCube0` because there's no probe; GGX specular) costs clearly more fragment ALU than Standard Lite.
- VRChat/Mobile/Standard Lite (simplified BRDF; optional normal and metallic maps; reflections and specular can be switched off) is cheaper still.
- Standard Lite with reflections and specular off is about the cost of Mobile Diffuse.
- In practice the difference scales with how much of the screen these materials cover: large when standing at a wash station or riding a cart, small at spawn. Neither shader adds draw calls by itself. Standard's ForwardAdd pass never runs, because the only light is Baked.

---

## 3. Carts

- 13 LODGroups under KH_Cart (8 `Carts/Cart_n/Model` + 5 `Stations/StationCart_n`). Heights 0.12 / 0.03, size 1.2 m [F].
  - LOD0 = Body 3,056 + 2 axles × 408 = **3,872 tris, 4 draw calls** (the body has 2 submeshes, Body and Sign).
  - LOD1 = Body_LOD1 1,136 + 2 × 144 = **1,424 tris, 4 draw calls**.
  - Switch and cull distances [E, Quest vertical FOV ≈ 96°]: LOD0 → LOD1 at ≈4.5 m and culled at ≈18 m, both ×lodBias. If the client's Quest lodBias is 2 (as in the template; see §7), that becomes 9 m and 36 m.
- **Ride carts (8):** their renderers are enabled in the saved scene, but `KH_CartManager.Start()` (KH_CartManager.cs:129–136) calls `ParkCart(c)` for every cart that isn't shown. That runs `KH_CartSeat._SetVisible(false)` (KH_CartSeat.cs:80–87, `renderers[i].enabled = false`) and moves the cart to Parking (0, −50, 0) (KH_CartManager.Motion.cs:355–362). **At runtime all 48 ride-cart renderers are off while idle** [F]. The census shows them enabled only because it was taken in edit mode.
  - Loose end: `renderers` comes from `GetComponentsInChildren<Renderer>` (KH_CartSetupEditor.cs:299/356), so the **8 SignCaption canvases (2 graphics each) stay active** at Parking, underground. The cost is negligible (culled, or depth-rejected under the terrain), but they aren't hidden.
- **Station carts (5):** static decorations, always enabled, and `static=0` (no static batching, not occludees). Only StationCart_0 (17 m) is within LOD1 range at spawn; the others are 97–590 m away and LOD-culled.
- **Textures:** KH_OsanpoCart_BaseColor.png is 1024² (192 KB PNG; Android override max 2048, ASTC 6×6, so it ships at 1024). KH_CartSign.png is 512×256, ASTC 6×6. Mipmaps on. Memory is fine, and these are not an fps factor.

---

## 4. AudioSources (70 → 118)

Growth 10-06 → now [F, text diff by path]:
- **KH_WashStations +27 / KH_WashStation +9:**
  - 28 `PremiumTools/Premium_*_Slot0..6/SpraySound`: loop=1, poa=0; clips WaterFlow, Foam, Brush, Air, Cologne; max 5 m, volume 0.25–0.35.
  - 4 `PremiumSelectionBoard/Click`: one-shot.
  - 4 `TakeoutHome/AppearSound`: one-shot.
- **KH_Dogs +12:** `KH_Wash_<dog>/PremiumFinish/BrushAudio` and `FragranceAudio` (one-shots, loop=0).

None of these are breathing or treat sounds. The `KD_BreathSource` ×6 and the `ContactFoam` / `ContactFur` / `Scrub` loops ×18 (volume 0 in the scene) were already there on 10-06.

Which play at idle [F]:
- The wash spray loops are stopped at Start: `KH_WashTool.Start()` sets `_sprayShown=true; ApplySpray(false)` (KH_WashTool.cs:137–139), which calls `sprayAudio.Stop()` (:417). They only `Play()` while spraying. The showers' `Nozzle` loops use the same class.
- `KH_WashFinishFx` plays its sound only when it receives a finish event (KH_WashFinishFx.cs:57).
- The dog wash contact loops are stopped when silent (`KH_DogWash.cs` Fade/SilenceAll, lines 3841–3857).
- Breathing stops when the dog isn't panting (`KD_DogWanderController.cs:1990–1995`).

**Voices playing at idle:** the 20 `KH_RiverAmbience/River_00..19` sources (poa=1, loop=1, spatial, max 48 m, volume 0.8; already there on 10-03) plus `KD_Bgm` (2D) = **21**, plus up to 6 breath loops after running. **Same as 10-03/10-06. None of the +48 add a playing voice.**

The river emitters are **348–643 m from spawn** with max distance 48 m, so they are inaudible at spawn.
- [K, medium] Unity keeps them as real voices as long as the real-voice limit isn't exceeded: 21 voices is under the limit, and Unity virtualizes by priority and audibility only when over it. That means the mixer and spatializer may still process 20 silent spatial voices. This was already true at 10-03 (~40 fps), so it isn't a regression, but it's a cheap thing to fix.
- [K, low] Each AudioSource also carries a `VRCSpatialAudioSource` (118, matching 1:1). Whether the client's spatializer component does per-frame work on sources that aren't playing is unknown. A/B group G5 tests it.

**ProjectSettings/AudioManager.asset** (uncommitted: VirtualVoiceCount 64 → 32, RealVoiceCount 32 → 24) [K, **high ~90 %**]: world uploads are scene AssetBundles and don't carry ProjectSettings. The VRChat client's own AudioManager settings govern voice counts. This change affects only the local Editor and ClientSim, not Quest. Proposal: revert it (`git checkout -- ProjectSettings/AudioManager.asset`) so it doesn't look like a fix. Not done (read-only).

---

## 5. Physics and network sync

VRCObjectSync (component identified by fileID −1783056023 in the SDK DLL) and VRCPickup (fileID 1804438810), by root [F]:

| root | 10-03 | 10-06 | now |
|---|---|---|---|
| KH_WashStation | 6 | 7 | **14** |
| KH_WashStations | 9 | 12 | **33** |
| KH_Dogs (9 in text + 6 in prefab instances, plus the 6 leash clips) | 21 | 21 | 21 |
| **total (census)** | ~36 | ~40 | **68** (+ 68 VRCPickup) |

- **Wash: 47 rigidbodies, all kinematic, gravity off, no interpolation**, colliders Box (38) or Capsule (9). They rest in slots [F].
- **Dogs: 15 non-kinematic** (6 balls with Sphere colliders, 6 frisbees and 3 treats with Box), gravity on, no interpolation. They sleep once at rest (Unity default sleep threshold). Plus 6 kinematic leash clips. Unchanged since 10-03 [F].
- **Non-convex MeshColliders: 0** (no NONCVX rows in the census) [F].
- [K, medium] VRCObjectSync runs per-frame work in the client on every client: the owner checks for movement and send timing; non-owners interpolate and handle discontinuities. When alone, you own everything. The cost per instance is small but adds up with count: 68 is moderate, and it is the only non-Udon per-frame item that grew substantially (+28 since 10-06). VRCPickup adds proximity and highlight work near the hands; 14 of the pickups are 8 m from spawn.
- Udon sync on the same objects (handed to udon-audit) [F]:
  - `KH_WashToolState` ×47 is Manual.
  - `KH_WashTool` declares `NoVariableSync` (KH_WashTool.cs:43), but in the scene **28 instances have `_syncMethod: 2` (Continuous; these are the new premium tools) and 19 have `1` (None)**.
  - On a late join, all the Manual and Continuous behaviours serialize to the joiner. That's a one-time spike, not a constant cost.

---

## 6. ParticleSystems (141 → 177, +36)

New since 10-06 [F]:
- 20 `UseMist`, on the premium hose, pistol, watering can and so on, inside prefab instances: loop=1, rate 16–110, max 24–48. **activeInHierarchy = 0 at idle** (census).
- 12 `KH_Wash_<dog>/PremiumFinish/BrushSheen` and `Fragrance`: poa=0, loop=0, max 64; triggered with `Emit()` (KH_WashFinishFx.cs:56).
- 4 `TakeoutHome/Appear`: poa=0, loop=0, max 64.

**None emit or play at idle** [F].

Other active systems are old:
- Pet FX (114, poa=0).
- Wash `HitSplash` / `FoamPuff`.
- The 5 wash shower `Spray` systems (poa=0, rate 140, max 160): only `Play()` while spraying (KH_WashTool.cs:416).
- The only poa=1 systems (KD_TreatFx nodes ×4, KH_WaterSpot Splash and Ripple) have **rateOverTime 0**: they're "playing" but empty, which costs almost nothing.

---

## 7. Scene-level settings

- **RenderSettings `m_IndirectSpecularColor`** went from (0.045, 0.150, 0.451) to **black** (uncommitted) [F]. Unity recomputes and caches this skybox-derived value when lighting updates. It's a re-save artifact with no performance effect.
  - [K, medium-high] It feeds `unity_IndirectSpecColor`, which Unity's Standard BRDF reads **only when `_GLOSSYREFLECTIONS_OFF`**. The cart, wash and PropFinish materials all have reflections on, so they're unaffected.
  - Materials with reflections off (for example the 40 BUG-ENV-001 Standard Lite materials, if that shader uses the same fallback) would lose a faint sky tint. That's a visual note, not perf.
- **Other RenderSettings** [F]: fog off; ambient Trilight (mode 1); default reflection = skybox at 128 px; LightingDataAsset present.
- **No Reflection Probe, no realtime light** (1 directional, Baked, no shadows), **no extra RT camera** (Main + 2 disabled) [F, census].
- **Occlusion culling data** is `Assets/Scenes/KH_RiverLoopCourse/OcclusionCullingData.asset`, last committed **2026-09-27** (bc24ee2) [F]. Static flags on active renderers [F]:

  | Static (in the bake as occluders / occludees) | Non-static |
  |---|---|
  | KH_World 128, KH_CourseScenery 145, KH_CourseExtension 27, KH_AutumnGroves 30, KH_GuideBoards 18 | **KH_RoadsideAutumn 318, KH_RiverVerge 111, KH_Cart 78, KH_WashStations 48, KH_WashStation 19**, most of KH_Dogs |

  The non-static ones get no static batching. [K] They can still be culled behind baked static occluders through dynamic occlusion (`allowOcclusionWhenDynamic`, default on), but they aren't part of the PVS. The 10-04 scenery is the bulk of this. It's view-dependent, so it matters most when looking along the river, and it is the likely mechanism behind the unmeasured 10-04 "20–40 unstable".
- **QualitySettings** (`ProjectSettings/QualitySettings.asset`, unchanged since the initial commit 51e3fd2) [F]. The template's "VRC Mobile" level has:
  - lodBias **2**, maximumLODLevel 0
  - globalTextureMipmapLimit 0, anisotropicTextures 2 (ForceEnable)
  - shadows 0, softParticles 0, realtimeReflectionProbes 0
  - particleRaycastBudget 1024, skinWeights 4, AA 2

  [K, high] Like the AudioManager, these don't ship with the world; the client applies its own. [K, medium] The VRChat template is meant to mirror the client's levels, so **Quest likely uses lodBias 2**. That would double every LOD distance in the 10-04 inventory (river grass and billboard trees culled at ~350–420 m instead of ~200 m), and the cart LODs too.
- **WorldQualitySettings** (VRChat sample, `Packages/com.vrchat.worlds/Samples/VRCQualitySettings/Scripts/WorldQualitySettings.cs`): sets only the shadowmask mode, realtime reflection probes and shadow distances, at Start. No LOD or texture control, and no per-frame cost [F].
- **Scene descriptor:** 1 spawn, spawnRadius 0.2, RespawnHeightY −10 [F].

---

## 8. Conclusions

### (a) Ranked non-Udon suspects for a constant ~20 fps at spawn

| # | Suspect | Evidence | Confidence it explains a large part of 40 → 20 |
|---|---|---|---|
| 0 | **Udon (234 → 366 behaviours), not my scope** | everything below adds up small; the user reports the drop with no interaction, from the moment of entry | – (prime candidate; see udon-audit) |
| 1 | **Pre-existing render floor from 10-04 scenery plus the house and dog fur** (never measured) | 10-04 user report "20–40" was never isolated (VER-ENV-002); 406 Foliage Lite alpha-clip pairs (878k tris over LODs); 429 scenery renderers non-static; occlusion bake from 09-27; probable lodBias 2 on Quest | medium as the floor. It isn't new since 10-06, but 10-12 may be "the old floor + a new constant", which turns "20–40" into "always 20" |
| 2 | **VRCObjectSync + VRCPickup 40 → 68** (wash 19 → 47) | §5; the only per-frame non-Udon item that grew a lot; you own them all when alone | medium-low (likely ~1 ms or less in total [E]) |
| 3 | **Enabled GraphicRaycasters + VRCUiShape (14; 6 within 17 m)** | §1; polling behaviour unverified | low (hypothesis) |
| 4 | **World-space canvas draw calls near spawn** (12 canvases, 299 graphics; +5 new) | §1; idle rebuilds ≈ 0 (verified) | low (+15–35 draw calls [E]) |
| 5 | **Standard materials on carts and wash items** | §2; GPU cost tracks screen coverage; at spawn: 11 renderers at 8 m + 1 cart at LOD1 | low at spawn; medium at a wash station or on a cart |
| 6 | **20 river loops always playing 350–640 m away** | §4; old (10-02) | low (not a regression; easy fix) |
| 7 | Particles, new AudioSources, LODGroups (+13), SignCaption canvases | §3, §4, §6: none active or playing at idle | very low |

### (b) A/B groups for a Quest measurement probe (rebuild the KH_PerfProbe pattern with Mobile/20)

Toggle **components** where possible (`Canvas.enabled`, `GraphicRaycaster.enabled`, `Renderer.enabled`, `AudioSource.enabled`) so the Udon state stays intact. Groups marked *mixed* switch off GameObjects that also carry Udon.

| Group | What to disable | Separates |
|---|---|---|
| G0 Look-up test (no probe) | at spawn, look at the sky, then at the ground; with OVR Metrics, read GPU % and CPU % | CPU vs GPU |
| G1 CanvasesNear | `Canvas.enabled=false` on the 12 active canvases within 17 m (§1 table) | UI draw calls |
| G1b CanvasesAll | `Canvas.enabled=false` on all 35 active world canvases | UI total |
| G2 Raycasters | `GraphicRaycaster.enabled=false` + the VRCUiShape BoxColliders on the 14 boards (5 `KH_Cart/Stations/Station_n/Board`, 4 `…/Board` and 4 `…/PremiumSelectionBoard` under KH_WashStation(s), `KD_DogBook/Index/Input`) | raycaster polling hypothesis |
| G3 CartRender | `Renderer.enabled=false` on `KH_Cart/Stations/StationCart_0..4/**` + SignCaption canvases | cart draw calls and Standard |
| G3m CartAll (*mixed*) | GameObject `KH_Cart` | cart Udon (54) + render + 8 VRCStation |
| G4 WashStandard | `Renderer.enabled=false` on Standard-material renderers under `KH_WashStation`, `KH_WashStations` (KH_WashTools / KH_PropFinish / PremiumWash_*) | Standard shader at the wash station |
| G4m WashTools (*mixed*) | GameObjects `KH_WashStation/PremiumTools`, `KH_WashStations/*/PremiumTools`, plus tool objects `KH_Wash{Towel,Sponge,Shower,Bottle}_*` (47 ObjectSync/Pickup/RB + KH_WashTool/State Udon) | ObjectSync/Pickup + wash Udon |
| G4M WashAll (*mixed*) | GameObjects `KH_WashStation`, `KH_WashStations` | everything wash |
| G5 AudioIdle | `AudioSource.enabled=false` on the 48 new non-playing sources (wash `SpraySound`/`Click`/`AppearSound`, dog `PremiumFinish/*Audio`) | per-component spatializer overhead (hypothesis) |
| G6 RiverAudio | GameObject `KH_RiverAmbience` (no Udon) | 20 playing spatial voices |
| G7 Scenery1004 | `KH_RoadsideAutumn`, `KH_RiverVerge` (render only, no Udon); split into grass / boards / other as in VER-ENV-002 | the 10-04 render floor |
| G8 Dogs / DogUdon | the existing VER-ENV-002 groups | fur two-pass + dog Udon |
| G9 ShaderSwap (optional) | at runtime set `sharedMaterial` of the cart, wash and PropFinish renderers to pre-made `…_Mobile.mat` (VRChat/Mobile/Standard Lite) copies | Standard vs Standard Lite with the same draw calls |

Read at spawn facing a fixed direction, solo, 6 dogs idle, right after entry and again after 15 min (heat). Change one group at a time. Coordinate with udon-audit so that Udon-only groups exist too (for example, disabling `KH_CartManager` alone).

### (c) Fix proposals (proposals, not decisions)

1. **Mobile material variants via the existing build-hook pattern.** Add an IProcessSceneWithReport (same shape as `KD_AndroidToonMaterialsEditor` / `KH_MobileTerrainBuildEditor`; Android and iOS only; touches only the build's scene copy). It would swap `KH_OsanpoCart_Body/Sign`, `KH_WashTools`, `KH_PropFinish`, `PremiumWash_Fixture/Tools/Addons/Window/Water` to `VRChat/Mobile/Standard Lite` copies.
   - Keep the normal and metallic maps where they carry the look (PropFinish, Fixture).
   - Turn reflections and specular off on the cart (it has no maps beyond albedo).
   - PC keeps Standard. Compare against the capture views before adopting.
2. **Hide the cart SignCaption canvases with the renderers** in `KH_CartSeat._SetVisible` (`Canvas.enabled`).
3. **Distance-gate far boards.** Disable `Canvas` + `GraphicRaycaster` on station boards and wash boards beyond ~40 m (KH_CartManager already measures panel distance; KH_WashStationBoard has `showMeters`). Also gate `KH_WashSelectionBoard._Tick` and `KD_DogBook.Refresh` by distance (Udon side).
4. **Canvas consolidation near spawn.** Dog book Index/Visual + Index/Input + Stand/Canvas could be one canvas. Static boards (Asobikata 72/34, Map, Kominka, WalkGuide 30/21) could be baked to a texture quad (one draw call each, no TMP).
5. **River ambience.** Enable only the 3–4 emitters nearest the player (1 Hz Udon), or switch them off beyond ~80 m. That saves up to 20 silent spatial voices.
6. **10-04 scenery and occlusion.** Mark the fixed roadside and river scenery (and the station carts) static (batching + occludee), then rebake occlusion. The current bake (09-27) predates the scenery, carts and wash. Or shorten their LOD cull heights, keeping in mind that Quest probably uses lodBias 2.
7. **AudioManager.** Revert the uncommitted voice-count change; it has no effect in VRChat.
8. **ObjectSync.** Count it into the budget. If G4m shows a measurable cost, consider whether every premium slot item needs its own VRCObjectSync while resting (for example, spawn on take), or keep it.
