# Everyday accessories — 2026-09-12

Original geometry and palette authored for KominkaDogRun in Blender through the project's Synaptic API wrapper. No purchased model or third-party texture is embedded in these five accessories.

| Model | Triangles | Slot |
|---|---:|---|
| TopHat | 1804 | Head |
| Cap | 1844 | Head |
| SpiralGlasses | 2436 | Face |
| StarGlasses | 2158 | Face |
| Backpack | 1104 | Body |

Each FBX has one mesh, one material slot, UVMap, and no animation. `Shared` contains one opaque Standard material and two 256 px palette maps used by TopHat and StarGlasses. SpiralGlasses uses its own opaque Standard material with two 512 px maps: a white background with a black spiral in Base Color, and linear metallic/smoothness (alpha). Its flat printed lenses replace the original three-dimensional coils; no normal map is needed for the printed pattern. Backpack has a dedicated opaque Standard material and two 512 px maps for cloth, stitching, zipper teeth and the original paw patch. Its metallic map isolates the small buckles and zipper pull; fabric and leather remain nonmetallic. Cap uses one dedicated 512 px Base Color map for cloth, panel seams, stitching, eyelets and the original paw patch. Its opaque Standard material is nonmetallic with uniform smoothness 0.18; no metallic or normal map is needed.

Editable source and generation payload builder:
`C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/accessory-five-20260912/accessory-five.blend`
and `build-payloads.cjs` in the same directory.

The updated SpiralGlasses source is `C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/spiral-glasses-20260912/spiral-glasses.blend`, with `build.cjs` in the same directory. The research, structural simplification and verification are recorded in [the polish report](../../../../../../docs/plans/2026-09-12-spiral-glasses-polish.md). The source and textures are original; research photographs are not embedded.

Unity setup/validation: `Tools/KominkaDogRun/Accessories/Everyday - Import`, `Everyday - Build`, `Everyday - Verify Models`, and `Sets - Verify Integration`. Existing node transforms are preserved by Build. Play and platform checks are recorded in `docs/plans/2026-09-12-accessory-five.md` at the project root.

The updated StarGlasses source is `C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/accessory-polish-20260912/star/star-glasses.blend`, with `build.cjs` and verification images in the same directory. Its rounded star rims, bridge and endpieces form the front frame; the front curves around the muzzle. The existing palette and material are retained. See [the 18-item polish record](../../../../../../docs/plans/2026-09-12-accessory-polish-round.md). To regenerate, start from that directory's `before.blend`; do not rerun the builder over a populated final scene.

The updated Backpack source is `C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/accessory-polish-20260912/backpack/backpack.blend`, with `build.cjs` and UV-authored `backpack.obj` in the same directory. The builder generates original geometry and texture atlases; import/export through Synaptic Blender. Begin regeneration from that directory's `before.blend`. Keep the FBX root rotation and mesh coordinates consistent so existing world and preview transforms remain valid.

The updated Cap source is `C:/work/unity/vrc-world-agent/artifacts/KominkaDogRun/accessory-polish-20260912/cap/cap.blend`, with `build.cjs` and UV-authored `cap.obj` in the same directory. The fitted six-panel cap has an open bottom, lining, a thin curved visor and a small covered button. Fine surface details are painted into the atlas. Begin regeneration from `before.blend`, then import/export through Synaptic Blender; do not rerun the initial build payload over the final scene.
