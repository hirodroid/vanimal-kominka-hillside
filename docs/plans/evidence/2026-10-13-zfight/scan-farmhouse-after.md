# ZFight scan: farmhouse (2026-10-13T05:21:35.4320981Z)

- scene `Assets/Scenes/VRCDefaultWorldScene.unity` dirty True -> True, root `KH_World/KH_Places/KH_FarKominka`, spawn eye (47.64, 3.23, 40.08)
- renderers: main 43, extra (bounds meet the farmhouse) 23, inactive skipped 24, LOD>0 skipped 0
- triangles 12439, oversize 0, candidate pairs 52646, accepted 11444, 0.9 s, truncated False
- thresholds: |dot(nA,nB)| > 0.999; coplanar: max gap <= 3 mm, near: <= 20 mm (gap measured along nA at the overlap polygon's vertices); overlap > 1 cm2 per triangle pair
- score = area_cm2 / max(distance_m, 1)^2 x (near tier 0.25) x (text/UI 0.1); back-to-back (opposite normals, both materials culled) are excluded from the ranking
- flags: wallcornerN = within 2 m of the Nth top corner of 外壁 (1 = nearest the spawn); steps = within 2 m of a step-like renderer or one of the pair is step-like; gate = within 2 m of 門枠

## Reference targets

| name | path | point | dist m |
|---|---|---|---|
| wallcorner1 | KH_FarKominka/KH_KominkaWall/外壁 | 41.62, 3.64, 35.18 | 7.8 |
| wallcorner2 | KH_FarKominka/KH_KominkaWall/外壁 | 66.93, 3.64, 44.93 | 19.9 |
| wallcorner3 | KH_FarKominka/KH_KominkaWall/外壁 | 48.10, 3.64, 18.39 | 21.7 |
| wallcorner4 | KH_FarKominka/KH_KominkaWall/外壁 | 73.42, 3.64, 28.09 | 28.4 |
| step1- | KH_FarKominka/KH_KominkaBuilding/床/玄関床 | 51.47, 1.72, 32.23 | 8.9 |
| step2- | KH_FarKominka/KH_KominkaBuilding/縁側柱 | 58.03, 4.14, 30.34 | 14.3 |
| step3- | KH_FarKominka/KH_KominkaBuilding/縁側床 | 57.98, 1.88, 30.32 | 14.3 |
| step4- | KH_FarKominka/KH_KominkaBuilding/床の間/台座 | 57.27, 2.07, 26.97 | 16.3 |
| gate | KH_FarKominka/KH_KominkaWall/門枠 | 48.73, 1.58, 37.84 | 2.5 |

## Overlap groups (ranked, top 80 of 253)

| rank | tier | flags | renderer A (sub) | renderer B (sub) | material A / B | relation | area cm2 | centre xyz | dist m | gap max / mean mm | notes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | near |  | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/壁 (2) | 柱 3 / 障子 | submeshes | 108812.3 | 52.59, 2.81, 28.20 | 12.9 | 20.0 / 13.0 |  |
| 2 | near | gate | KH_FarKominka/KH_KominkaWall/門枠 (1) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 柱 3 / 柱 3 | regions | 1562.9 | 48.73, 3.93, 37.82 | 2.6 | 4.1 / 4.1 |  |
| 3 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (2) | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | 障子 / 柱 3 | renderers | 12755.0 | 53.06, 3.62, 32.00 | 9.7 | 14.6 / 10.4 |  |
| 4 | near |  | KH_PhotoBoard/Structure/FinishedVisual (0) | KH_PhotoBoard/Structure/FinishedVisual (0) | KH_PropFinish / KH_PropFinish | regions | 1966.0 | 52.44, 3.00, 41.23 | 4.9 | 17.0 / 15.9 |  |
| 5 | near | gate | KH_FarKominka/KH_KominkaBuilding/床/石畳み/床.001 (8) (0) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 御影石 / 柱 3 | renderers | 801.9 | 47.63, 1.53, 37.40 | 3.2 | 4.3 / 4.2 |  |
| 6 | near | gate | KH_FarKominka/KH_KominkaBuilding/床/石畳み/床.001 (8) (0) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 御影石 / 柱 3 | renderers | 794.8 | 49.84, 1.53, 38.25 | 3.3 | 4.3 / 4.2 |  |
| 7 | near | gate | KH_FarKominka/KH_KominkaBuilding/床/石畳み/床.001 (8) (0) | KH_FarKominka/KH_KominkaWall/門.002 (0) | 御影石 / 木目 | renderers | 598.9 | 48.34, 1.59, 37.68 | 3.0 | 4.1 / 4.1 |  |
| 8 | near | gate | KH_FarKominka/KH_KominkaBuilding/床/石畳み/床.001 (8) (0) | KH_FarKominka/KH_KominkaWall/門.001 (0) | 御影石 / 木目 | renderers | 598.9 | 49.14, 1.59, 37.98 | 3.1 | 4.1 / 4.1 |  |
| 9 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (1) | 障子 / 木目 | submeshes | 8247.4 | 56.33, 2.90, 32.71 | 11.4 | 8.0 / 6.9 |  |
| 10 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (1) | 障子 / 木目 | submeshes | 8247.4 | 56.62, 2.90, 31.87 | 12.2 | 8.0 / 6.9 |  |
| 11 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (1) | 障子 / 木目 | submeshes | 8247.4 | 56.98, 2.90, 30.93 | 13.1 | 8.0 / 6.9 |  |
| 12 | near | gate | KH_FarKominka/KH_KominkaWall/門.001 (0) | KH_FarKominka/KH_KominkaWall/門.002 (0) | 木目 / 木目 | renderers | 305.7 | 48.75, 2.63, 37.83 | 2.6 | 4.1 / 4.1 |  |
| 13 | near | steps | KH_FarKominka/KH_KominkaBuilding/書院 (2) | KH_FarKominka/KH_KominkaBuilding/縁側床 (0) | 柱 3 / 板 | renderers | 10937.0 | 58.11, 1.90, 28.81 | 15.4 | 20.0 / 20.0 |  |
| 14 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (1) | 障子 / 木目 | submeshes | 8247.4 | 57.33, 2.90, 30.13 | 13.9 | 8.0 / 6.9 |  |
| 15 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (1) | 柱 3 / 障子 | submeshes | 1555.5 | 56.82, 3.77, 31.41 | 12.6 | 2.5 / 2.5 |  |
| 16 | near | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (1) | 瓦 / 外壁 | submeshes | 299.5 | 47.34, 3.50, 37.31 | 2.8 | 8.2 / 6.1 |  |
| 17 | near | gate | KH_FarKominka/KH_KominkaWall/門.002 (0) | KH_FarKominka/KH_KominkaWall/門.002 (0) | 木目 / 木目 | regions | 247.9 | 48.61, 2.42, 37.78 | 2.6 | 19.1 / 17.0 |  |
| 18 | coplanar |  | KH_PhotoBoard/Structure/FinishedVisual (0) | KH_PhotoBoard/Structure/FinishedVisual (0) | KH_PropFinish / KH_PropFinish | regions | 219.0 | 52.44, 2.97, 41.22 | 4.9 | 2.0 / 1.2 |  |
| 19 | near |  | KH_FarKominka/KH_KominkaBuilding/壁 (2) | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | 障子 / 柱 3 | renderers | 7537.4 | 53.88, 3.62, 26.49 | 15.0 | 12.1 / 10.0 |  |
| 20 | near | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (1) | 瓦 / 外壁 | submeshes | 308.3 | 50.13, 3.50, 38.37 | 3.0 | 12.3 / 6.3 |  |
| 21 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (1) | 障子 / 木目 | submeshes | 2933.3 | 49.86, 2.91, 30.83 | 9.5 | 10.1 / 7.7 |  |
| 22 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.012 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.012 (1) | 障子 / 木目 | submeshes | 2989.7 | 49.45, 2.91, 30.63 | 9.6 | 16.2 / 7.9 |  |
| 23 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.010 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.010 (1) | 障子 / 木目 | submeshes | 2933.2 | 48.98, 2.90, 30.45 | 9.7 | 10.1 / 7.7 |  |
| 24 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.011 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.011 (1) | 障子 / 木目 | submeshes | 2933.3 | 48.56, 2.91, 30.33 | 9.8 | 10.1 / 7.7 |  |
| 25 | coplanar |  | KH_FarKominka/KH_KominkaWall/外壁 (1) | KH_FarKominka/KH_KominkaWall/外壁 (1) | 外壁 / 外壁 | regions | 452.7 | 41.85, 1.55, 35.12 | 7.8 | 0.0 / 0.0 |  |
| 26 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/壁 (0) | 柱 3 / 柱 3 | regions | 2914.8 | 51.94, 3.04, 30.84 | 10.2 | 14.5 / 10.1 |  |
| 27 | near |  | KH_FarKominka/KH_KominkaBuilding/屋根 (1) | KH_FarKominka/KH_KominkaBuilding/屋根 (2) | 柱 3 / 障子 | submeshes | 4924.3 | 49.15, 5.30, 26.86 | 13.5 | 15.0 / 12.8 |  |
| 28 | near |  | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (2) | 柱 3 / 障子 | submeshes | 4647.8 | 52.66, 3.64, 27.88 | 13.2 | 15.6 / 11.6 |  |
| 29 | near | steps | KH_FarKominka/KH_KominkaBuilding/縁側柱 (0) | KH_FarKominka/KH_KominkaBuilding/縁側柱 (1) | 木目 / 柱 3 | submeshes | 5185.0 | 58.49, 3.85, 30.51 | 14.5 | 19.6 / 10.2 |  |
| 30 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (1) | 柱 3 / 障子 | submeshes | 3942.3 | 56.81, 3.78, 31.41 | 12.6 | 12.5 / 8.2 |  |
| 31 | coplanar |  | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (2) | 柱 3 / 障子 | submeshes | 1335.9 | 50.55, 3.63, 24.77 | 15.6 | 0.3 / 0.3 |  |
| 32 | near |  | KH_FarKominka/KH_KominkaBuilding/障子/障子.007 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.007 (1) | 障子 / 木目 | submeshes | 2752.8 | 47.91, 2.91, 28.64 | 11.5 | 8.0 / 6.8 |  |
| 33 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/書院 (0) | KH_FarKominka/KH_KominkaBuilding/書院 (2) | 壁 / 柱 3 | submeshes | 1244.0 | 58.32, 3.46, 28.89 | 15.5 | 2.5 / 2.0 |  |
| 34 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/床/畳.002 (0) | 柱 3 / 畳 | renderers | 2515.4 | 49.27, 1.90, 29.17 | 11.1 | 15.0 / 14.4 |  |
| 35 | near |  | KH_FarKominka/KH_KominkaBuilding/障子/障子.008 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.008 (1) | 障子 / 木目 | submeshes | 2752.7 | 48.02, 2.92, 28.25 | 11.8 | 8.0 / 6.8 |  |
| 36 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (1) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (1) | 木目 / 木目 | regions | 607.2 | 56.33, 2.91, 32.71 | 11.4 | 2.0 / 2.0 |  |
| 37 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (1) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (1) | 木目 / 木目 | regions | 607.2 | 56.62, 2.90, 31.87 | 12.2 | 2.0 / 2.0 |  |
| 38 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (2) | 柱 3 / 障子 | submeshes | 917.1 | 51.21, 3.64, 32.93 | 8.0 | 14.6 / 12.8 |  |
| 39 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (1) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (1) | 木目 / 木目 | regions | 607.2 | 56.98, 2.90, 30.94 | 13.1 | 2.0 / 2.0 |  |
| 40 | near |  | KH_FarKominka/KH_KominkaBuilding/障子/障子.005 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.005 (1) | 障子 / 木目 | submeshes | 2752.8 | 48.93, 2.91, 25.99 | 14.2 | 8.0 / 6.8 |  |
| 41 | near |  | KH_FarKominka/KH_KominkaBuilding/障子/障子.006 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.006 (1) | 障子 / 木目 | submeshes | 2752.7 | 49.04, 2.92, 25.62 | 14.5 | 8.0 / 6.8 |  |
| 42 | near | steps | KH_FarKominka/KH_KominkaBuilding/屋根 (1) | KH_FarKominka/KH_KominkaBuilding/屋根 (2) | 柱 3 / 障子 | submeshes | 2237.2 | 57.58, 5.31, 31.76 | 13.1 | 14.2 / 14.2 |  |
| 43 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.002 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.002 (1) | 障子 / 木目 | submeshes | 2829.6 | 58.09, 2.91, 29.53 | 14.9 | 8.0 / 6.8 |  |
| 44 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (1) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (1) | 木目 / 木目 | regions | 607.2 | 57.33, 2.91, 30.13 | 13.9 | 2.0 / 2.0 |  |
| 45 | near | steps | KH_FarKominka/KH_KominkaBuilding/書院 (2) | KH_FarKominka/KH_KominkaBuilding/書院 (2) | 柱 3 / 柱 3 | regions | 2939.0 | 58.31, 3.46, 28.93 | 15.4 | 17.0 / 14.0 |  |
| 46 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/書院障子/障子.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書院障子/障子.001 (1) | 障子 / 木目 | submeshes | 2829.5 | 58.19, 2.91, 29.17 | 15.2 | 8.0 / 6.8 |  |
| 47 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/書院障子/障子.004 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書院障子/障子.004 (1) | 障子 / 木目 | submeshes | 2829.6 | 58.37, 2.91, 28.69 | 15.7 | 8.0 / 6.8 |  |
| 48 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (2) | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | 障子 / 柱 3 | renderers | 2939.0 | 58.12, 3.63, 28.04 | 16.0 | 11.5 / 10.0 |  |
| 49 | coplanar |  | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (2) | 柱 3 / 障子 | submeshes | 649.2 | 52.35, 3.63, 25.46 | 15.4 | 0.3 / 0.3 |  |
| 50 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.003 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.003 (1) | 障子 / 木目 | submeshes | 2829.6 | 58.57, 2.91, 28.27 | 16.1 | 8.0 / 6.8 |  |
| 51 | near | wallcorner1 | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (1) | 瓦 / 外壁 | submeshes | 598.3 | 41.71, 3.50, 35.17 | 7.7 | 8.2 / 6.1 |  |
| 52 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | 柱 3 / 柱 3 | renderers | 850.3 | 51.84, 3.62, 31.73 | 9.4 | 12.0 / 9.5 |  |
| 53 | coplanar | gate | KH_GuideBoards/Kominka/Structure/RoofFront (0) | KH_GuideBoards/Kominka/Structure/Ridge (0) | KH_RiverWood / KH_RiverWood | renderers | 7.6 | 47.27, 3.78, 38.44 | 1.8 | 0.0 / 0.0 |  |
| 54 | coplanar | gate | KH_GuideBoards/Kominka/Structure/Ridge (0) | KH_GuideBoards/Kominka/Structure/RoofBack (0) | KH_RiverWood / KH_RiverWood | renderers | 7.6 | 49.17, 3.78, 39.34 | 1.8 | 0.0 / 0.0 |  |
| 55 | coplanar |  | KH_FarKominka/KH_KominkaWall/外壁 (1) | KH_FarKominka/KH_KominkaWall/外壁 (1) | 外壁 / 外壁 | regions | 920.2 | 66.81, 1.55, 44.75 | 19.8 | 0.0 / 0.0 |  |
| 56 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸.001 (0) | 柱 3 / 木目 | renderers | 598.2 | 50.69, 2.43, 32.67 | 8.1 | 16.1 / 14.0 |  |
| 57 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (1) | 柱 3 / 木目 | renderers | 826.4 | 50.05, 2.85, 30.91 | 9.5 | 12.2 / 8.7 |  |
| 58 | near | steps | KH_FarKominka/KH_KominkaBuilding/屋根 (1) | KH_FarKominka/KH_KominkaBuilding/屋根 (2) | 柱 3 / 障子 | submeshes | 2257.3 | 58.61, 5.37, 29.08 | 15.7 | 10.7 / 10.7 |  |
| 59 | near | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (0) | 瓦 / 瓦 | regions | 84.1 | 50.13, 3.58, 38.37 | 3.0 | 8.2 / 5.3 |  |
| 60 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/壁 (1) | 柱 3 / 障子 | submeshes | 590.8 | 51.26, 1.60, 33.00 | 8.1 | 15.2 / 12.9 |  |
| 61 | coplanar | gate | KH_GuideBoards/Kominka/Structure/RoofFront (0) | KH_GuideBoards/Kominka/Structure/Ridge (0) | KH_RiverWood / KH_RiverWood | renderers | 6.9 | 49.16, 3.78, 39.36 | 1.8 | 0.0 / 0.0 |  |
| 62 | coplanar | gate | KH_GuideBoards/Kominka/Structure/Ridge (0) | KH_GuideBoards/Kominka/Structure/RoofBack (0) | KH_RiverWood / KH_RiverWood | renderers | 6.9 | 47.28, 3.78, 38.42 | 1.8 | 0.0 / 0.0 |  |
| 63 | near |  | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (2) | 柱 3 / 障子 | renderers | 1513.9 | 52.03, 3.88, 27.45 | 13.4 | 20.0 / 16.4 |  |
| 64 | near |  | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.011 (1) | 柱 3 / 木目 | renderers | 805.1 | 48.37, 2.85, 30.26 | 9.9 | 11.7 / 9.1 |  |
| 65 | near | gate | KH_FarKominka/KH_KominkaWall/門.001 (0) | KH_FarKominka/KH_KominkaWall/門.001 (0) | 木目 / 木目 | regions | 68.4 | 49.15, 1.84, 37.98 | 2.9 | 15.5 / 13.5 |  |
| 66 | coplanar |  | KH_FarKominka/KH_KominkaBuilding/障子/障子.007 (1) | KH_FarKominka/KH_KominkaBuilding/障子/障子.007 (1) | 木目 / 木目 | regions | 256.8 | 47.91, 2.91, 28.64 | 11.5 | 2.0 / 2.0 |  |
| 67 | near | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (0) | 瓦 / 瓦 | regions | 61.2 | 47.34, 3.58, 37.30 | 2.8 | 8.2 / 5.3 |  |
| 68 | near | gate | KH_FarKominka/KH_KominkaWall/門.002 (0) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 木目 / 柱 3 | renderers | 50.1 | 48.72, 3.73, 37.82 | 2.6 | 17.6 / 17.6 |  |
| 69 | coplanar |  | KH_FarKominka/KH_KominkaBuilding/障子/障子.008 (1) | KH_FarKominka/KH_KominkaBuilding/障子/障子.008 (1) | 木目 / 木目 | regions | 256.8 | 48.02, 2.91, 28.25 | 11.8 | 2.0 / 2.0 |  |
| 70 | near |  | KH_FarKominka/KH_KominkaBuilding/屋根 (1) | KH_FarKominka/KH_KominkaBuilding/屋根 (1) | 柱 3 / 柱 3 | regions | 525.4 | 51.41, 4.87, 32.52 | 8.6 | 18.0 / 8.2 |  |
| 71 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (1) | 柱 3 / 木目 | renderers | 217.2 | 56.35, 3.59, 32.73 | 11.4 | 1.4 / 1.4 |  |
| 72 | near |  | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/床/畳.001 (0) | 柱 3 / 畳 | renderers | 1278.3 | 49.77, 1.90, 26.37 | 13.9 | 14.1 / 14.1 |  |
| 73 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/床/畳 (0) | 柱 3 / 畳 | renderers | 681.6 | 54.60, 1.90, 32.30 | 10.5 | 15.0 / 14.9 |  |
| 74 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (1) | 柱 3 / 木目 | renderers | 212.9 | 56.60, 3.59, 31.85 | 12.2 | 1.4 / 1.4 |  |
| 75 | near |  | KH_FarKominka/KH_KominkaBuilding/屋根 (0) | KH_FarKominka/KH_KominkaBuilding/屋根 (0) | 柱 3 / 柱 3 | regions | 556.9 | 47.53, 4.06, 30.23 | 9.9 | 18.4 / 11.5 |  |
| 76 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (2) | 柱 3 / 障子 | submeshes | 887.2 | 56.77, 3.96, 31.53 | 12.5 | 10.0 / 10.0 |  |
| 77 | near | steps | KH_FarKominka/KH_KominkaBuilding/書院 (0) | KH_FarKominka/KH_KominkaBuilding/書院 (2) | 壁 / 柱 3 | submeshes | 1255.3 | 58.32, 3.49, 28.90 | 15.5 | 10.0 / 8.4 |  |
| 78 | near |  | KH_Dogs/KH_LeashRack/FinishedVisual (0) | KH_Dogs/KH_LeashRack/FinishedVisual (0) | KH_PropFinish / KH_PropFinish | regions | 82.4 | 45.00, 2.79, 43.04 | 4.0 | 10.0 / 7.0 |  |
| 79 | near |  | KH_FarKominka/KH_KominkaBuilding/屋根 (0) | KH_FarKominka/KH_KominkaBuilding/屋根 (0) | 柱 3 / 柱 3 | regions | 321.0 | 49.83, 4.02, 32.54 | 7.9 | 19.8 / 14.8 |  |
| 80 | coplanar |  | KH_FarKominka/KH_KominkaBuilding/障子/障子.005 (1) | KH_FarKominka/KH_KominkaBuilding/障子/障子.005 (1) | 木目 / 木目 | regions | 256.8 | 48.93, 2.91, 26.00 | 14.2 | 2.0 / 2.0 |  |

## Flagged groups (178)

| rank | tier | flags | renderer A (sub) | renderer B (sub) | material A / B | relation | area cm2 | centre xyz | dist m | gap max / mean mm | notes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | near | gate | KH_FarKominka/KH_KominkaWall/門枠 (1) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 柱 3 / 柱 3 | regions | 1562.9 | 48.73, 3.93, 37.82 | 2.6 | 4.1 / 4.1 |  |
| 3 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (2) | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | 障子 / 柱 3 | renderers | 12755.0 | 53.06, 3.62, 32.00 | 9.7 | 14.6 / 10.4 |  |
| 5 | near | gate | KH_FarKominka/KH_KominkaBuilding/床/石畳み/床.001 (8) (0) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 御影石 / 柱 3 | renderers | 801.9 | 47.63, 1.53, 37.40 | 3.2 | 4.3 / 4.2 |  |
| 6 | near | gate | KH_FarKominka/KH_KominkaBuilding/床/石畳み/床.001 (8) (0) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 御影石 / 柱 3 | renderers | 794.8 | 49.84, 1.53, 38.25 | 3.3 | 4.3 / 4.2 |  |
| 7 | near | gate | KH_FarKominka/KH_KominkaBuilding/床/石畳み/床.001 (8) (0) | KH_FarKominka/KH_KominkaWall/門.002 (0) | 御影石 / 木目 | renderers | 598.9 | 48.34, 1.59, 37.68 | 3.0 | 4.1 / 4.1 |  |
| 8 | near | gate | KH_FarKominka/KH_KominkaBuilding/床/石畳み/床.001 (8) (0) | KH_FarKominka/KH_KominkaWall/門.001 (0) | 御影石 / 木目 | renderers | 598.9 | 49.14, 1.59, 37.98 | 3.1 | 4.1 / 4.1 |  |
| 9 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (1) | 障子 / 木目 | submeshes | 8247.4 | 56.33, 2.90, 32.71 | 11.4 | 8.0 / 6.9 |  |
| 10 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (1) | 障子 / 木目 | submeshes | 8247.4 | 56.62, 2.90, 31.87 | 12.2 | 8.0 / 6.9 |  |
| 11 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (1) | 障子 / 木目 | submeshes | 8247.4 | 56.98, 2.90, 30.93 | 13.1 | 8.0 / 6.9 |  |
| 12 | near | gate | KH_FarKominka/KH_KominkaWall/門.001 (0) | KH_FarKominka/KH_KominkaWall/門.002 (0) | 木目 / 木目 | renderers | 305.7 | 48.75, 2.63, 37.83 | 2.6 | 4.1 / 4.1 |  |
| 13 | near | steps | KH_FarKominka/KH_KominkaBuilding/書院 (2) | KH_FarKominka/KH_KominkaBuilding/縁側床 (0) | 柱 3 / 板 | renderers | 10937.0 | 58.11, 1.90, 28.81 | 15.4 | 20.0 / 20.0 |  |
| 14 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (1) | 障子 / 木目 | submeshes | 8247.4 | 57.33, 2.90, 30.13 | 13.9 | 8.0 / 6.9 |  |
| 15 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (1) | 柱 3 / 障子 | submeshes | 1555.5 | 56.82, 3.77, 31.41 | 12.6 | 2.5 / 2.5 |  |
| 16 | near | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (1) | 瓦 / 外壁 | submeshes | 299.5 | 47.34, 3.50, 37.31 | 2.8 | 8.2 / 6.1 |  |
| 17 | near | gate | KH_FarKominka/KH_KominkaWall/門.002 (0) | KH_FarKominka/KH_KominkaWall/門.002 (0) | 木目 / 木目 | regions | 247.9 | 48.61, 2.42, 37.78 | 2.6 | 19.1 / 17.0 |  |
| 20 | near | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (1) | 瓦 / 外壁 | submeshes | 308.3 | 50.13, 3.50, 38.37 | 3.0 | 12.3 / 6.3 |  |
| 21 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (1) | 障子 / 木目 | submeshes | 2933.3 | 49.86, 2.91, 30.83 | 9.5 | 10.1 / 7.7 |  |
| 22 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.012 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.012 (1) | 障子 / 木目 | submeshes | 2989.7 | 49.45, 2.91, 30.63 | 9.6 | 16.2 / 7.9 |  |
| 23 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.010 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.010 (1) | 障子 / 木目 | submeshes | 2933.2 | 48.98, 2.90, 30.45 | 9.7 | 10.1 / 7.7 |  |
| 24 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.011 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.011 (1) | 障子 / 木目 | submeshes | 2933.3 | 48.56, 2.91, 30.33 | 9.8 | 10.1 / 7.7 |  |
| 26 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/壁 (0) | 柱 3 / 柱 3 | regions | 2914.8 | 51.94, 3.04, 30.84 | 10.2 | 14.5 / 10.1 |  |
| 29 | near | steps | KH_FarKominka/KH_KominkaBuilding/縁側柱 (0) | KH_FarKominka/KH_KominkaBuilding/縁側柱 (1) | 木目 / 柱 3 | submeshes | 5185.0 | 58.49, 3.85, 30.51 | 14.5 | 19.6 / 10.2 |  |
| 30 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (1) | 柱 3 / 障子 | submeshes | 3942.3 | 56.81, 3.78, 31.41 | 12.6 | 12.5 / 8.2 |  |
| 33 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/書院 (0) | KH_FarKominka/KH_KominkaBuilding/書院 (2) | 壁 / 柱 3 | submeshes | 1244.0 | 58.32, 3.46, 28.89 | 15.5 | 2.5 / 2.0 |  |
| 34 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/床/畳.002 (0) | 柱 3 / 畳 | renderers | 2515.4 | 49.27, 1.90, 29.17 | 11.1 | 15.0 / 14.4 |  |
| 36 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (1) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (1) | 木目 / 木目 | regions | 607.2 | 56.33, 2.91, 32.71 | 11.4 | 2.0 / 2.0 |  |
| 37 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (1) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (1) | 木目 / 木目 | regions | 607.2 | 56.62, 2.90, 31.87 | 12.2 | 2.0 / 2.0 |  |
| 38 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (2) | 柱 3 / 障子 | submeshes | 917.1 | 51.21, 3.64, 32.93 | 8.0 | 14.6 / 12.8 |  |
| 39 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (1) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (1) | 木目 / 木目 | regions | 607.2 | 56.98, 2.90, 30.94 | 13.1 | 2.0 / 2.0 |  |
| 42 | near | steps | KH_FarKominka/KH_KominkaBuilding/屋根 (1) | KH_FarKominka/KH_KominkaBuilding/屋根 (2) | 柱 3 / 障子 | submeshes | 2237.2 | 57.58, 5.31, 31.76 | 13.1 | 14.2 / 14.2 |  |
| 43 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.002 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.002 (1) | 障子 / 木目 | submeshes | 2829.6 | 58.09, 2.91, 29.53 | 14.9 | 8.0 / 6.8 |  |
| 44 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (1) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (1) | 木目 / 木目 | regions | 607.2 | 57.33, 2.91, 30.13 | 13.9 | 2.0 / 2.0 |  |
| 45 | near | steps | KH_FarKominka/KH_KominkaBuilding/書院 (2) | KH_FarKominka/KH_KominkaBuilding/書院 (2) | 柱 3 / 柱 3 | regions | 2939.0 | 58.31, 3.46, 28.93 | 15.4 | 17.0 / 14.0 |  |
| 46 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/書院障子/障子.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書院障子/障子.001 (1) | 障子 / 木目 | submeshes | 2829.5 | 58.19, 2.91, 29.17 | 15.2 | 8.0 / 6.8 |  |
| 47 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/書院障子/障子.004 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書院障子/障子.004 (1) | 障子 / 木目 | submeshes | 2829.6 | 58.37, 2.91, 28.69 | 15.7 | 8.0 / 6.8 |  |
| 48 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (2) | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | 障子 / 柱 3 | renderers | 2939.0 | 58.12, 3.63, 28.04 | 16.0 | 11.5 / 10.0 |  |
| 50 | near | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.003 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.003 (1) | 障子 / 木目 | submeshes | 2829.6 | 58.57, 2.91, 28.27 | 16.1 | 8.0 / 6.8 |  |
| 51 | near | wallcorner1 | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (1) | 瓦 / 外壁 | submeshes | 598.3 | 41.71, 3.50, 35.17 | 7.7 | 8.2 / 6.1 |  |
| 52 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | 柱 3 / 柱 3 | renderers | 850.3 | 51.84, 3.62, 31.73 | 9.4 | 12.0 / 9.5 |  |
| 53 | coplanar | gate | KH_GuideBoards/Kominka/Structure/RoofFront (0) | KH_GuideBoards/Kominka/Structure/Ridge (0) | KH_RiverWood / KH_RiverWood | renderers | 7.6 | 47.27, 3.78, 38.44 | 1.8 | 0.0 / 0.0 |  |
| 54 | coplanar | gate | KH_GuideBoards/Kominka/Structure/Ridge (0) | KH_GuideBoards/Kominka/Structure/RoofBack (0) | KH_RiverWood / KH_RiverWood | renderers | 7.6 | 49.17, 3.78, 39.34 | 1.8 | 0.0 / 0.0 |  |
| 56 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸.001 (0) | 柱 3 / 木目 | renderers | 598.2 | 50.69, 2.43, 32.67 | 8.1 | 16.1 / 14.0 |  |
| 57 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (1) | 柱 3 / 木目 | renderers | 826.4 | 50.05, 2.85, 30.91 | 9.5 | 12.2 / 8.7 |  |
| 58 | near | steps | KH_FarKominka/KH_KominkaBuilding/屋根 (1) | KH_FarKominka/KH_KominkaBuilding/屋根 (2) | 柱 3 / 障子 | submeshes | 2257.3 | 58.61, 5.37, 29.08 | 15.7 | 10.7 / 10.7 |  |
| 59 | near | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (0) | 瓦 / 瓦 | regions | 84.1 | 50.13, 3.58, 38.37 | 3.0 | 8.2 / 5.3 |  |
| 60 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/壁 (1) | 柱 3 / 障子 | submeshes | 590.8 | 51.26, 1.60, 33.00 | 8.1 | 15.2 / 12.9 |  |
| 61 | coplanar | gate | KH_GuideBoards/Kominka/Structure/RoofFront (0) | KH_GuideBoards/Kominka/Structure/Ridge (0) | KH_RiverWood / KH_RiverWood | renderers | 6.9 | 49.16, 3.78, 39.36 | 1.8 | 0.0 / 0.0 |  |
| 62 | coplanar | gate | KH_GuideBoards/Kominka/Structure/Ridge (0) | KH_GuideBoards/Kominka/Structure/RoofBack (0) | KH_RiverWood / KH_RiverWood | renderers | 6.9 | 47.28, 3.78, 38.42 | 1.8 | 0.0 / 0.0 |  |
| 65 | near | gate | KH_FarKominka/KH_KominkaWall/門.001 (0) | KH_FarKominka/KH_KominkaWall/門.001 (0) | 木目 / 木目 | regions | 68.4 | 49.15, 1.84, 37.98 | 2.9 | 15.5 / 13.5 |  |
| 67 | near | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/外壁 (0) | 瓦 / 瓦 | regions | 61.2 | 47.34, 3.58, 37.30 | 2.8 | 8.2 / 5.3 |  |
| 68 | near | gate | KH_FarKominka/KH_KominkaWall/門.002 (0) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 木目 / 柱 3 | renderers | 50.1 | 48.72, 3.73, 37.82 | 2.6 | 17.6 / 17.6 |  |
| 71 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (1) | 柱 3 / 木目 | renderers | 217.2 | 56.35, 3.59, 32.73 | 11.4 | 1.4 / 1.4 |  |
| 73 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁 (0) | KH_FarKominka/KH_KominkaBuilding/床/畳 (0) | 柱 3 / 畳 | renderers | 681.6 | 54.60, 1.90, 32.30 | 10.5 | 15.0 / 14.9 |  |
| 74 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (1) | 柱 3 / 木目 | renderers | 212.9 | 56.60, 3.59, 31.85 | 12.2 | 1.4 / 1.4 |  |
| 76 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/壁.001 (2) | 柱 3 / 障子 | submeshes | 887.2 | 56.77, 3.96, 31.53 | 12.5 | 10.0 / 10.0 |  |
| 77 | near | steps | KH_FarKominka/KH_KominkaBuilding/書院 (0) | KH_FarKominka/KH_KominkaBuilding/書院 (2) | 壁 / 柱 3 | submeshes | 1255.3 | 58.32, 3.49, 28.90 | 15.5 | 10.0 / 8.4 |  |
| 81 | near | steps | KH_FarKominka/KH_KominkaBuilding/書院 (1) | KH_FarKominka/KH_KominkaBuilding/書院 (2) | 障子 / 柱 3 | submeshes | 1195.0 | 58.27, 2.88, 28.96 | 15.4 | 20.0 / 13.9 |  |
| 82 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (1) | 柱 3 / 木目 | renderers | 212.9 | 56.96, 3.59, 30.92 | 13.1 | 1.6 / 1.6 |  |
| 85 | coplanar | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.002 (1) | KH_FarKominka/KH_KominkaBuilding/障子/障子.002 (1) | 木目 / 木目 | regions | 263.9 | 58.09, 2.91, 29.53 | 14.9 | 2.0 / 2.0 |  |
| 86 | near | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (0) | KH_FarKominka/KH_KominkaBuilding/書院 (2) | 柱 3 / 柱 3 | renderers | 1097.6 | 57.86, 3.66, 28.73 | 15.3 | 5.6 / 5.6 |  |

## Terrain contact (upward faces of KH_FarKominka vs Terrain; gap = face - terrain)

| part | terrain | up area cm2 | coplanar (<=3 mm) cm2 | near (<=20 mm or crossing) cm2 | buried cm2 | gap min..max mm | centre |
|---|---|---|---|---|---|---|---|
| KH_FarKominka/KH_KominkaBuilding/床/玄関床 | KH_Terrain | 40881 | 0 | 0 | 0 | 187..215 | 0.00, 0.00, 0.00 |
| KH_FarKominka/KH_KominkaBuilding/縁側床 | KH_Terrain | 96600 | 0 | 0 | 0 | 332..431 | 0.00, 0.00, 0.00 |
| KH_FarKominka/KH_KominkaBuilding/縁側柱 | KH_Terrain | 26050 | 0 | 0 | 0 | 2173..2670 | 0.00, 0.00, 0.00 |

## Back-to-back (opposite normals, both culled; not z-fighting unless a material is Cull Off): 209 groups, 270493 cm2. Top 30:

| rank | tier | flags | renderer A (sub) | renderer B (sub) | material A / B | relation | area cm2 | centre xyz | gap max / mean mm | notes |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | near (opp) | gate | KH_FarKominka/KH_KominkaWall/外壁 (1) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 外壁 / 柱 3 | renderers | 7768.2 | 47.34, 2.56, 37.30 | 3.5 / 3.5 |  |
| 0 | near (opp) | gate | KH_FarKominka/KH_KominkaWall/外壁 (1) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 外壁 / 柱 3 | renderers | 7768.2 | 50.13, 2.55, 38.39 | 7.3 / 7.3 |  |
| 0 | coplanar (opp) |  | KH_PhotoBoard/Structure/FinishedVisual (0) | KH_PhotoBoard/Structure/FinishedVisual (0) | KH_PropFinish / KH_PropFinish | regions | 2918.4 | 52.43, 3.67, 41.20 | 2.0 / 1.5 |  |
| 0 | near (opp) | gate | KH_FarKominka/KH_KominkaWall/門.001 (0) | KH_FarKominka/KH_KominkaWall/門.002 (0) | 木目 / 木目 | renderers | 1536.8 | 48.74, 2.63, 37.83 | 6.8 / 6.8 |  |
| 0 | near (opp) | gate | KH_FarKominka/KH_KominkaWall/門.002 (0) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 木目 / 柱 3 | renderers | 1536.8 | 47.79, 2.64, 37.46 | 18.1 / 18.1 |  |
| 0 | near (opp) | gate | KH_GuideBoards/Kominka/Structure/Back (0) | KH_GuideBoards/Kominka/Structure/PostR (0) | KH_RiverWood / KH_RiverWood | renderers | 472.0 | 47.60, 2.81, 38.59 | 10.0 / 10.0 |  |
| 0 | near (opp) | gate | KH_GuideBoards/Kominka/Structure/Back (0) | KH_GuideBoards/Kominka/Structure/PostL (0) | KH_RiverWood / KH_RiverWood | renderers | 472.0 | 48.84, 2.81, 39.19 | 10.0 / 10.0 |  |
| 0 | near (opp) |  | KH_Dogs/KH_LeashRack/WalkGuide/FinishedVisual (0) | KH_Dogs/KH_LeashRack/WalkGuide/FinishedVisual (0) | KH_PropFinish / KH_PropFinish | same-region | 2252.2 | 45.00, 3.34, 43.05 | 18.0 / 18.0 |  |
| 0 | near (opp) |  | KH_PhotoBoard/Structure/FinishedVisual (0) | KH_PhotoBoard/Structure/FinishedVisual (0) | KH_PropFinish / KH_PropFinish | regions | 2954.4 | 52.44, 2.90, 41.22 | 10.0 / 9.4 |  |
| 0 | near (opp) | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 瓦 / 柱 3 | renderers | 779.0 | 47.35, 3.48, 37.28 | 11.7 / 8.9 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (0) | 障子 / 障子 | regions | 11984.1 | 56.34, 2.95, 32.69 | 8.0 / 8.0 |  |
| 0 | near (opp) | gate | KH_FarKominka/KH_KominkaWall/外壁 (0) | KH_FarKominka/KH_KominkaWall/門枠 (1) | 瓦 / 柱 3 | renderers | 830.0 | 50.13, 3.49, 38.37 | 19.6 / 14.7 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (0) | 障子 / 障子 | regions | 11984.1 | 56.62, 2.89, 31.86 | 8.0 / 8.0 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (0) | 障子 / 障子 | regions | 11984.1 | 56.98, 2.89, 30.95 | 8.0 / 8.0 |  |
| 0 | near (opp) |  | KH_FarKominka/KH_KominkaBuilding/屋根 (0) | KH_FarKominka/KH_KominkaBuilding/屋根 (0) | 柱 3 / 柱 3 | regions | 7908.4 | 49.10, 4.27, 29.49 | 19.5 / 8.4 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子.003 (1) | 障子 / 木目 | submeshes | 8249.3 | 56.33, 2.90, 32.71 | 16.0 / 14.9 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (0) | 障子 / 障子 | regions | 11984.1 | 57.33, 2.90, 30.13 | 8.0 / 8.0 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/屋根 (0) | KH_FarKominka/KH_KominkaBuilding/屋根 (0) | 柱 3 / 柱 3 | regions | 12630.4 | 55.83, 4.28, 28.39 | 20.0 / 9.4 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.002 (1) | 障子 / 木目 | submeshes | 8249.3 | 56.62, 2.90, 31.87 | 16.0 / 14.9 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/壁 (1) | KH_FarKominka/KH_KominkaBuilding/床/石畳み/床.001 (0) | 障子 / 御影石 | renderers | 3515.9 | 51.21, 1.56, 32.97 | 9.3 / 9.3 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子/引き戸障子.001 (1) | 障子 / 木目 | submeshes | 8249.3 | 56.98, 2.90, 30.94 | 16.0 / 14.9 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/壁.001 (1) | KH_FarKominka/KH_KominkaBuilding/壁.001 (1) | 障子 / 障子 | regions | 7577.0 | 56.79, 3.78, 31.45 | 10.0 / 10.0 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸障子 (1) | 障子 / 木目 | submeshes | 8249.3 | 57.33, 2.90, 30.13 | 16.0 / 14.9 |  |
| 0 | coplanar (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/引き戸 (0) | KH_FarKominka/KH_KominkaBuilding/障子/引き戸.001 (0) | 木目 / 木目 | renderers | 663.2 | 51.21, 2.68, 32.93 | 2.0 / 2.0 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/書院タンス (1) | KH_FarKominka/KH_KominkaBuilding/書院タンス (1) | 障子 / 障子 | regions | 8331.2 | 57.86, 2.18, 28.72 | 10.0 / 10.0 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (0) | 障子 / 障子 | same-region | 2943.1 | 49.86, 2.93, 30.83 | 8.0 / 8.0 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (0) | KH_FarKominka/KH_KominkaBuilding/障子/障子.009 (1) | 障子 / 木目 | submeshes | 2937.2 | 49.86, 2.91, 30.83 | 18.1 / 15.7 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.012 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.012 (0) | 障子 / 障子 | same-region | 2943.1 | 49.45, 2.93, 30.64 | 8.0 / 8.0 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.012 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.012 (1) | 障子 / 木目 | submeshes | 2937.1 | 49.45, 2.91, 30.63 | 18.1 / 15.7 |  |
| 0 | near (opp) | steps | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.010 (0) | KH_FarKominka/KH_KominkaBuilding/障子/書斎障子/障子.010 (0) | 障子 / 障子 | same-region | 2943.1 | 48.99, 2.94, 30.45 | 8.0 / 8.0 |  |

## Notes

