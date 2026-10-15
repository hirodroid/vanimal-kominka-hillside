# ZFight scan: boards, posters, photo board, bottles (2026-10-15T11:42:25.1113648Z)

**BOARDS_ZFIGHT_OK 0**

- guarantee: the readers' (front) side only. UI is Cull Off, so a UI quad vs an opposite-facing mesh face can flicker when seen from behind that face; those pairs are listed under 'Back side' and are outside this check. Depth: Near 0.01 since BUG-WLD-002; Quest's depth bits unconfirmed, so any parallel overlap within 30 mm counts
- scene: Assets/Scenes/VRCDefaultWorldScene.unity (dirty False -> False)
- thresholds: |dot(nA,nB)| > 0.999; gap <= 30 mm (coplanar <= 3 mm), measured along nA at the overlap polygon's vertices; per triangle pair: overlap >= 0.09999999 cm2 and mean width >= 0.09999999 mm (float slivers dropped); counted per group when its total area >= 1 cm2; clusters linked within 10 cm
- rule: counted = same-facing (or Cull Off material) groups with at least one MeshRenderer side and >= 1 cm2; UI vs UI skipped (ZWrite Off); smaller groups listed; required targets missing / duplicated / unreadable or a cut-short run = INCOMPLETE
- per group: KH_GuideBoards 0; KH_ItemGuideBoards 0; KH_WallPosters 0; KH_PhotoBoard 0; KH_DogBookStand 0; bottles 0
- parts: 64 renderers, 354 UI graphics, 4 bottles, 8950 triangles, 40806 candidates, 1.2 s
- UI vs UI groups skipped: 781
- target KH_GuideBoards: 114 parts
- target KH_ItemGuideBoards: 164 parts
- target KH_WallPosters: 6 parts
- target KH_PhotoBoard: 65 parts
- target KH_Dogs/KH_DogBookStand: 65 parts
- target 4 KH_WashTool bottles: 4 parts

## Counted (same-facing, at least one mesh, >= 1 cm2)

None.

## Below 1 cm2 per group (listed, not counted)

None.

## Back side (UI vs an opposite-facing mesh face: seen only from behind that face; outside the guarantee, not counted)

| # | group | tier | facing | relation | A | B | area cm2 | gap max / mean mm | centre | notes |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/Canvas/Image [RawImage] (0, (none)) | 156.55 | 20.00 / 20.00 | 50.43, 2.70, 38.72 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/Canvas/Image [RawImage] (0, (none)) | 39.70 | 20.00 / 20.00 | 50.51, 3.04, 38.75 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/Canvas/Image [RawImage] (0, (none)) | 2656.52 | 20.00 / 20.00 | 50.65, 2.58, 38.81 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/Canvas/Image [RawImage] (0, (none)) | 2656.53 | 20.00 / 20.00 | 50.79, 2.82, 38.86 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/Canvas/Image [RawImage] (0, (none)) | 77.17 | 20.00 / 20.00 | 51.01, 2.62, 38.95 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/Canvas/Image [RawImage] (0, (none)) | 39.68 | 20.00 / 20.00 | 50.94, 2.36, 38.92 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/Canvas/Image [RawImage] (0, (none)) | 79.34 | 20.00 / 20.00 | 51.01, 2.85, 38.95 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/UrlCanvas/Url [Image] (0, (none)) | 132.00 | 20.00 / 20.00 | 50.63, 2.22, 38.80 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/UrlCanvas/Url [Image] (0, (none)) | 132.00 | 20.00 / 20.00 | 50.82, 2.23, 38.87 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/UrlCanvas/Url/TextArea/Text [TextMeshProUGUI] (0, (none)) | 99.35 | 20.00 / 20.00 | 50.64, 2.22, 38.80 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/Torinosuke/Structure/Back (0, KH_RiverWood) | KH_WallPosters/Torinosuke/UrlCanvas/Url/TextArea/Text [TextMeshProUGUI] (0, (none)) | 99.35 | 20.00 / 20.00 | 50.81, 2.23, 38.87 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/GroupPoster/Structure/Back (0, KH_RiverWood) | KH_WallPosters/GroupPoster/Canvas/Image [RawImage] (0, (none)) | 2850.24 | 20.00 / 20.00 | 51.46, 2.55, 39.12 | text/UI (likely intended offset) |
| 0 | KH_WallPosters | near | opposite | renderers | KH_WallPosters/GroupPoster/Structure/Back (0, KH_RiverWood) | KH_WallPosters/GroupPoster/Canvas/Image [RawImage] (0, (none)) | 2850.23 | 20.00 / 20.00 | 51.66, 2.85, 39.20 | text/UI (likely intended offset) |
