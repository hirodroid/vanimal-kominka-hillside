# ZFight scan: premium wash tools (2026-10-13T04:44:36.4542081Z)

- thresholds: |dot| > 0.999; coplanar <= 0.5 mm, near <= 3 mm (tighter than the farmhouse 3 / 20 mm: held at ~0.3-0.6 m, a gap above ~0.1 mm cannot fight even in a 16-bit buffer, so 3 mm is the outer bound the task gave); overlap > 0.09999999 cm2 per triangle pair; clusters linked within 2 cm; ranked by area x (near 0.25)
- coordinates: prefab-root space, metres
- transparency: A transparent (Fade, renderQueue > 2500) face coplanar with an opaque face is drawn after it with ZTest LEqual: with ZWrite Off it does not fight for the depth buffer, but per pixel it passes or fails the test against the opaque depth, so it shows as patchy show-through that crawls as the tool moves (looks like z-fighting). Two opaque coplanar faces are classic z-fighting. Two transparent coplanar faces flicker by sort order.

| prefab | tris | renderers | coplanar groups / cm2 | near groups / cm2 | transparent-vs-opaque groups / cm2 | back-to-back groups / cm2 |
|---|---|---|---|---|---|---|
| WaterPistol | 2508 | 2 | 0 / 0.00 | 3 / 24.15 | 2 / 15.87 | 5 / 26.00 |
| DuckWaterGun | 1356 | 1 | 0 / 0.00 | 0 / 0.00 | 0 / 0.00 | 2 / 1.85 |
| FishWaterGun | 1348 | 1 | 0 / 0.00 | 0 / 0.00 | 0 / 0.00 | 2 / 3.49 |
| BambooWaterGun | 1364 | 2 | 0 / 0.00 | 0 / 0.00 | 0 / 0.00 | 1 / 2.66 |
| FoamCannon | 1616 | 1 | 1 / 6.97 | 0 / 0.00 | 0 / 0.00 | 3 / 35.21 |

## WaterPistol

| renderer | sub | material | shader | queue | ZWrite | Cull Off | transparent | tris |
|---|---|---|---|---|---|---|---|---|
| WaterPistol/WaterPistol/WaterPistol_Mesh | 0 | PremiumWash_Tools | Standard | 2000 | 1 | False | False | 2252 |
| WaterPistol/WaterPistol/WaterPistol_Window | 0 | PremiumWash_Window | Standard | 2990 | 0 | False | True | 256 |

| rank | tier | facing | relation | A (sub, material) | B (sub, material) | area cm2 | local centre | gap max / mean mm | notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | near | same | regions | WaterPistol/WaterPistol/WaterPistol_Mesh (0, PremiumWash_Tools) | WaterPistol/WaterPistol/WaterPistol_Mesh (0, PremiumWash_Tools) | 8.28 | 0.000, 0.089, 0.096 | 3.00 / 3.00 |  |
| 2 | near | same | renderers | WaterPistol/WaterPistol/WaterPistol_Mesh (0, PremiumWash_Tools) | WaterPistol/WaterPistol/WaterPistol_Window (0, PremiumWash_Window) | 7.94 | 0.034, 0.089, 0.002 | 1.20 / 1.05 | transparent vs opaque |
| 3 | near | same | renderers | WaterPistol/WaterPistol/WaterPistol_Mesh (0, PremiumWash_Tools) | WaterPistol/WaterPistol/WaterPistol_Window (0, PremiumWash_Window) | 7.93 | -0.034, 0.089, 0.002 | 1.19 / 1.05 | transparent vs opaque |

- back-to-back (opposite normals, culled): 5 groups, 26.00 cm2
- cell 0.007 m, prefab extent 0.07, 0.20, 0.19 m, candidates 2398, accepted 176

## DuckWaterGun

| renderer | sub | material | shader | queue | ZWrite | Cull Off | transparent | tris |
|---|---|---|---|---|---|---|---|---|
| DuckWaterGun/DuckWaterGun/DuckWaterGun_Mesh | 0 | PremiumWash_Tools | Standard | 2000 | 1 | False | False | 1356 |

No same-facing coplanar / near overlap above the threshold.

- back-to-back (opposite normals, culled): 2 groups, 1.85 cm2
- cell 0.009 m, prefab extent 0.09, 0.24, 0.25 m, candidates 798, accepted 8

## FishWaterGun

| renderer | sub | material | shader | queue | ZWrite | Cull Off | transparent | tris |
|---|---|---|---|---|---|---|---|---|
| FishWaterGun/FishWaterGun/FishWaterGun_Mesh | 0 | PremiumWash_Tools | Standard | 2000 | 1 | False | False | 1348 |

No same-facing coplanar / near overlap above the threshold.

- back-to-back (opposite normals, culled): 2 groups, 3.49 cm2
- cell 0.009 m, prefab extent 0.08, 0.22, 0.25 m, candidates 864, accepted 20

## BambooWaterGun

| renderer | sub | material | shader | queue | ZWrite | Cull Off | transparent | tris |
|---|---|---|---|---|---|---|---|---|
| BambooWaterGun/BambooWaterGun/BambooWaterGun_Mesh | 0 | PremiumWash_Tools | Standard | 2000 | 1 | False | False | 1196 |
| BambooWaterGun/BambooWaterGun/BambooWaterGun_Pump | 0 | PremiumWash_Tools | Standard | 2000 | 1 | False | False | 168 |

No same-facing coplanar / near overlap above the threshold.

- back-to-back (opposite normals, culled): 1 groups, 2.66 cm2
- cell 0.009 m, prefab extent 0.07, 0.06, 0.35 m, candidates 2031, accepted 8

## FoamCannon

| renderer | sub | material | shader | queue | ZWrite | Cull Off | transparent | tris |
|---|---|---|---|---|---|---|---|---|
| FoamCannon/FoamCannon/FoamCannon_Mesh | 0 | PremiumWash_Tools | Standard | 2000 | 1 | False | False | 1616 |

| rank | tier | facing | relation | A (sub, material) | B (sub, material) | area cm2 | local centre | gap max / mean mm | notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | coplanar | same | regions | FoamCannon/FoamCannon/FoamCannon_Mesh (0, PremiumWash_Tools) | FoamCannon/FoamCannon/FoamCannon_Mesh (0, PremiumWash_Tools) | 6.97 | 0.000, 0.105, -0.050 | 0.00 / 0.00 |  |

- back-to-back (opposite normals, culled): 3 groups, 35.21 cm2
- cell 0.008 m, prefab extent 0.08, 0.22, 0.20 m, candidates 2949, accepted 57
