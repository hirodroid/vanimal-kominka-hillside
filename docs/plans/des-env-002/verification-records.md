# 周回コースの検証記録（整理版）

2026-09-27。中間JSONをプランから退避し、結論・条件は制作記録と本ファイルに集約した。詳細ログはローカルの `Logs/des-env-002-plan-archive-20260927` に保存（Git対象外）。下記のSHA256は退避した原ファイルの同一性確認用。失敗も履歴として残し、過去の結果を現在の実機合格とは扱わない。

現行の見た目・容量は[秋の改修](autumn-polish.md)、手順と全周検査は[制作記録](implementation.md)。配置図で使う `proposal-data.json` と旧17点の互換検査に必要な `original-route.json` は残した。

## autumn-benchmark

原ファイル: `autumn-benchmark.json` / 24,595 bytes / SHA256 `27a5a024d83458f6ab223d3a879888e22d3077a39e0b9c748ce2d9f9a1777246`

```text
runId: 20260927-011341
startedUtc: 2026-09-27T01:13:41.1338080Z
endedUtc: 2026-09-27T01:16:12.6743473Z
status: MEASURED
startedEditorTime: 91200.9926008
restored: true
cameraOcclusionRestored: true
restoredCap: -1
restoredVsync: 0
limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
```

## autumn-capacity

原ファイル: `autumn-capacity.json` / 1,209 bytes / SHA256 `7ad6d356ec92254df48c101a797a33661d743328e7dbc7d649ab2dc4bd26334f`

```text
android.startedUtc: 2026-09-27T01:07:26.4289885Z
android.endedUtc: 2026-09-27T01:09:20.4552158Z
android.status: MEASURED
android.error:
android.downloadBytes: 47010969
android.uncompressedBytes: 124848992
android.limitations: Saved-scene AssetBundle proxy, not SDK upload size or resident/device memory. Compare with the same build target and compression. Does not certify shader appearance, VR stereo, or runtime performance.
ios.startedUtc: 2026-09-27T01:10:23.4318185Z
ios.endedUtc: 2026-09-27T01:11:57.1151963Z
ios.status: MEASURED
ios.error:
ios.downloadBytes: 46151169
ios.uncompressedBytes: 121080448
ios.limitations: Saved-scene AssetBundle proxy, not SDK upload size or resident/device memory. Compare with the same build target and compression. Does not certify shader appearance, VR stereo, or runtime performance.
```

## autumn-night

原ファイル: `autumn-night.json` / 2,553 bytes / SHA256 `746c5d133ad829830c308e4c76991d8d2404c39ddd5f599004c886f837059468`

```text
utc: 2026-09-27T01:16:27.1971570Z
status: NIGHT_EDITOR_RENDER_OK
errors: 0
skyMaterialPreserved: true
limitations: Edit-mode mono offscreen render using cloned materials and the existing night recipe. Does not exercise Udon fade, saved preferences, stereo, VRChat mirror/photo cameras or mobile devices.
```

## autumn-verified

原ファイル: `autumn-verified.json` / 1,136 bytes / SHA256 `8e782f273052abd0cfed304e01b2bcf5cac720b289d4e1e27ed00f24df54e46b`

```text
utc: 2026-09-27T01:16:34.7535267Z
limitation: Editor daytime mono images and saved-data checks; no device or multiplayer performance claim.
```

## course-font-verification

原ファイル: `course-font-verification.json` / 481 bytes / SHA256 `804ab3906752789570119f928e91cc74e7b139d23a3a681e966587ac5e4554fd`

```text
utc: 2026-09-26T17:52:06.745Z
status: EXISTING_FONT_PRESERVED
limitations: Serialized glyph metrics, character mappings and atlas pixels only; does not verify device text rendering.
```

## far-kominka-cleanup

原ファイル: `far-kominka-cleanup.json` / 4,507 bytes / SHA256 `350de2550fb7dd161034208902785eb0d57f48f874c18ec152fff5ffc78d4f6b`

```text
before.utc: 2026-09-26T18:46:32.3973646Z
before.status: SCRIPT_REMNANTS
after.utc: 2026-09-26T18:47:45.3508032Z
after.status: DECORATIVE_BUILDING_ONLY
initialFailure.runId: 20260926-184400
initialFailure.status: FAIL
initialFailure.utc: 2026-09-26T18:44:00.6933390Z
initialFailure.endedUtc: 2026-09-26T18:44:25.9301239Z
initialFailure.settingsRestored: true
initialFailure.limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. Bridge cases and gate wrap are local segments, not a full lap or user walking-time/device acceptance. Random dog pauses remain enabled.
```

## full-course-bench-measurements

原ファイル: `full-course-bench-measurements.json` / 1,645 bytes / SHA256 `53cb9e53670f4bee266fbeaaec41b36e70f30cebc26e5622eefb723fb517295e`

```text
詳細な結果と失敗原因は制作記録の該当段階を参照。
```

## full-course-ground-scenery

原ファイル: `full-course-ground-scenery.json` / 41,660 bytes / SHA256 `425f50c1d14c80dda8bcd5ac445bccad31adf931c0cd1283821685cbd171195a`

```text
status: GROUND_STRUCTURE_OK
utc: 2026-09-26T18:07:56.3741452Z
footSamples: 14499
protectedSamples: 534
marginSamples: 4608
errors: 0
loopLengthMetres: 2406.89111328125
limitations: Saved geometry only. Full navigation/dog route, culling, visual finishing, walking time and device/16-player acceptance remain incomplete.
```

## full-course-ground

原ファイル: `full-course-ground.json` / 54,086 bytes / SHA256 `98b8ec0f0e66c7fcaccafcd92d3040603a0f6032adb9a95a7ce36e1931eda578`

```text
status: GROUND_STRUCTURE_OK
utc: 2026-09-26T17:28:51.7583475Z
footSamples: 14499
protectedSamples: 534
marginSamples: 4608
errors: 0
loopLengthMetres: 2406.89111328125
limitations: Saved geometry only. Full navigation/dog route, culling, visual finishing, walking time and device/16-player acceptance remain incomplete.
```

## full-course-landscape-measurements

原ファイル: `full-course-landscape-measurements.json` / 1,566 bytes / SHA256 `5e0208e0b3a19d106d400878be3d77e377ccec477630e2fd8b3eb636afee0dad`

```text
詳細な結果と失敗原因は制作記録の該当段階を参照。
```

## full-course-landscape-polish

原ファイル: `full-course-landscape-polish.json` / 108,809 bytes / SHA256 `c0fa61468491794fc242ee91ab07cc3cb332b5a82024621e6f766b740a2a9c8f`

```text
before.utc: 2026-09-26T20:19:17.2411167Z
before.restored: true
after.utc: 2026-09-26T20:22:34.2774248Z
after.restored: true
occlusion.status: OCCLUSION_DATA_OK
occlusion.utc: 2026-09-26T20:25:25.3728564Z
occlusion.errors: 0
bake.status: SAVED
bake.utc: 2026-09-26T20:25:22.1461468Z
comparison.status: SAMPLED_IMAGES_MATCH
comparison.utc: 2026-09-26T20:25:26.3011394Z
comparison.pairs: 95
comparison.limitations: Sparse Edit-mode mono images, 384x216, FOV 60, near 0.3/far 2200. Route nodes every ten steps in both directions plus three key views and two bridge eye heights. OFF/ON/OFF rendered in the same Editor callback. Pixel differences over 2/255 require inspection; identical images do not prove every viewpoint, stereo, mirror or device. Does not measure speed.
survey.utc: 2026-09-26T20:25:33.3286938Z
survey.limitations: Edit-mode daytime mono survey, 512x288, eye height 1.62m, FOV60, near0.3/far2200. Every twenty route nodes in four directions. Sparse images require visual inspection; no aesthetic or stereo/device PASS is inferred.
```

## full-course-measurements

原ファイル: `full-course-measurements.json` / 2,794 bytes / SHA256 `a23e22595360d3eee78a5bc343578f36405be6fe549575c2277943e506d7fc97`

```text
詳細な結果と失敗原因は制作記録の該当段階を参照。
```

## full-course-menu

原ファイル: `full-course-menu.json` / 6,812 bytes / SHA256 `dfaecc7e821eeb30d85579bfee89ab870d23c5ed66802a3f6c3f15f54a1bdbb6`

```text
utc: 2026-09-26T17:48:31.6533833Z
status: COURSE_MENU_DATA_OK
errors: 0
limitations: Editor structure, localization and saved Udon values only. VR/touch scrolling, remote request acknowledgement and device font fallback are not tested.
```

## full-course-midway-return

原ファイル: `full-course-midway-return.json` / 33,816 bytes / SHA256 `16da8d0251d986a79af7e6b963a0614b36ab46c93039daf41833c3757908f653`

```text
runId: 20260926-212223
status: MIDWAY_RETURN_OBSERVED
utc: 2026-09-26T21:22:23.3265343Z
endedUtc: 2026-09-26T21:31:11.6956899Z
settingsRestored: true
playerDataRestored: true
limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. lapChoice=0 is segment coverage only; lapChoice=2/3 is a continuous synthetic-holder lap. Neither measures user walking time or device performance. Random dog pauses remain enabled.
```

## full-course-navigation-scenery

原ファイル: `full-course-navigation-scenery.json` / 621 bytes / SHA256 `a853027d1f1f8fcf49738437e5af2d14084e652b036b614f172efc0214f5e5e8`

```text
status: NAV_GEOMETRY_OK
utc: 2026-09-26T18:06:06.9254182Z
errors: 0
routeSamples: 562
bedSamples: 117
limitations: Static navigation geometry only. Dog route graph/loop intention, leashes, animation, runtime cost and devices remain unverified.
```

## full-course-navigation

原ファイル: `full-course-navigation.json` / 695 bytes / SHA256 `0d21746dae9c29f6d4190ce919e3dada7b85dd16e8be4c5453bf2391c302a330`

```text
status: NAV_GEOMETRY_OK
utc: 2026-09-26T17:28:52.2475175Z
errors: 0
routeSamples: 562
bedSamples: 117
limitations: Static navigation geometry only. Dog route graph/loop intention, leashes, animation, runtime cost and devices remain unverified.
```

## full-course-night

原ファイル: `full-course-night.json` / 2,171 bytes / SHA256 `bda1f8f04a2c0bdb1abaa807939b761c18069c54e1d0d3cbd3b258db4db6a12f`

```text
utc: 2026-09-26T18:20:04.2075809Z
status: NIGHT_EDITOR_RENDER_OK
errors: 0
skyMaterialPreserved: true
limitations: Edit-mode mono offscreen render using cloned materials and the existing night recipe. Does not exercise Udon fade, saved preferences, stereo, VRChat mirror/photo cameras or mobile devices.
```

## full-course-occlusion-measurements

原ファイル: `full-course-occlusion-measurements.json` / 2,152 bytes / SHA256 `8750411bda50b4c56b0a546582f224f4e7e998882a71821beac45e03c4d74db1`

```text
limitations: Saved-scene proxy bundle, not an SDK upload or measured device memory. Delta includes occlusion scene configuration and PVS; no performance acceptance.
```

## full-course-occlusion

原ファイル: `full-course-occlusion.json` / 70,874 bytes / SHA256 `59798489b8d447b5cddb3946258fd62b25ee616fd5c3e373b5e5d92db8eadc62`

```text
structure.status: OCCLUSION_DATA_OK
structure.utc: 2026-09-26T18:34:35.9399268Z
structure.errors: 0
images.status: SAMPLED_IMAGES_MATCH
images.utc: 2026-09-26T18:31:54.8480085Z
images.pairs: 95
images.limitations: Sparse Edit-mode mono images, 384x216, FOV 60, near 0.3/far 2200. Route nodes every ten steps in both directions plus three key views and two bridge eye heights. OFF/ON/OFF rendered in the same Editor callback. Pixel differences over 2/255 require inspection; identical images do not prove every viewpoint, stereo, mirror or device. Does not measure speed.
raw.runId: 20260926-183210
raw.startedUtc: 2026-09-26T18:32:10.4547626Z
raw.endedUtc: 2026-09-26T18:34:23.4026722Z
raw.status: MEASURED
raw.startedEditorTime: 67110.3146369
raw.restored: true
raw.cameraOcclusionRestored: true
raw.restoredCap: -1
raw.restoredVsync: 0
raw.limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
postRun.scenery.status: SCENERY_STRUCTURE_OK
postRun.scenery.utc: 2026-09-26T18:34:45.4336473Z
postRun.scenery.errors: 0
postRun.scenery.limitations: Saved Edit-mode structure and static navigation only. Screen-relative LOD is camera-driven; no hill occlusion bake, platform performance or visual/device acceptance is inferred.
limitations: Three fixed Editor ClientSim views and sparse low-resolution mono Edit images. Not worst-case cost, culling CPU timing, device FPS/memory, stereo/mirror/photo acceptance or 16-player/six-leashed-dog performance.
```

## full-course-polish-measurements

原ファイル: `full-course-polish-measurements.json` / 1,627 bytes / SHA256 `36d2fc0cc8deeacbdbccfed46980f455c9b414f5cd198142fbbb2daad12083f6`

```text
詳細な結果と失敗原因は制作記録の該当段階を参照。
```

## full-course-route-measurements

原ファイル: `full-course-route-measurements.json` / 3,007 bytes / SHA256 `cffb1ce2024e08d72be67d9d4f194dc1403ae68a16ce63768ecb5b46394bd5c7`

```text
詳細な結果と失敗原因は制作記録の該当段階を参照。
```

## full-course-route-scenery

原ファイル: `full-course-route-scenery.json` / 730 bytes / SHA256 `02f25ace581c2157fae660224086af288c0dfda2b396997ded363dcc4f755d90`

```text
status: ROUTE_DATA_OK
utc: 2026-09-26T18:07:47.6282232Z
nodes: 436
links: 436
errors: 0
shortestPairs: 1744
limitations: Editor execution of route methods and serialized Udon values. Actual Udon VM dog walking, UI selection, Owner handover and device performance require separate verification.
```

## full-course-route

原ファイル: `full-course-route.json` / 678 bytes / SHA256 `d5554c9ad1e282f724d7ec1207804835adafd1d88a3857d1190cdba427b78e37`

```text
status: ROUTE_DATA_OK
utc: 2026-09-26T17:48:47.5974104Z
nodes: 436
links: 436
errors: 0
shortestPairs: 1744
limitations: Editor execution of route methods and serialized Udon values. Actual Udon VM dog walking, UI selection, Owner handover and device performance require separate verification.
```

## full-course-scenery-before-mobile-cap

原ファイル: `full-course-scenery-before-mobile-cap.json` / 2,318 bytes / SHA256 `efa845173a8335ac2953122a46ba884ab7434bf08beed675f30ac205ac9fa67d`

```text
limitations: Bundle proxy only. Added near/far tree materials and procedural meshes, refreshed NavMesh; not an isolated shader or tree-only size delta. No platform performance or resident-memory acceptance.
```

## full-course-scenery-benchmark

原ファイル: `full-course-scenery-benchmark.json` / 23,339 bytes / SHA256 `4dd24b438bba70b17b8c7cc87b425cc20b25b7ae22336dabfc990250be39ad15`

```text
raw.runId: 20260926-180850
raw.startedUtc: 2026-09-26T18:08:50.0776286Z
raw.endedUtc: 2026-09-26T18:11:02.9510681Z
raw.status: MEASURED
raw.startedEditorTime: 65709.9362958
raw.restored: true
raw.restoredCap: -1
raw.restoredVsync: 0
raw.limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
postRun.verification.status: SCENERY_STRUCTURE_OK
postRun.verification.utc: 2026-09-26T18:11:27.0026354Z
postRun.verification.errors: 0
postRun.verification.limitations: Saved Edit-mode structure and static navigation only. Screen-relative LOD is camera-driven; no hill occlusion bake, platform performance or visual/device acceptance is inferred.
limitations: Native LOD ON/OFF scenery contribution at three fixed Editor views; not forced-LOD0 comparison, hidden-zone occlusion savings, six leashed dogs, 16 avatars or device performance.
```

## full-course-scenery-measurements

原ファイル: `full-course-scenery-measurements.json` / 2,769 bytes / SHA256 `63da157969d04e68d625a93ad5a6da17ed06242e085861e2a91a334772b63af8`

```text
limitations: Bundle proxy only. Added near/far tree materials and procedural meshes, refreshed NavMesh; not an isolated shader or tree-only size delta. No platform performance or resident-memory acceptance.
```

## full-course-scenery-polish

原ファイル: `full-course-scenery-polish.json` / 104,517 bytes / SHA256 `867bf8fe0968c3d9cf787ea67f496b436ef9fa15cceadf25c4a93450c89254aa`

```text
placementSurvey.utc: 2026-09-26T19:24:41.4814744Z
applied.utc: 2026-09-26T19:31:23.3949923Z
navigation.status: NAV_GEOMETRY_OK
navigation.utc: 2026-09-26T19:33:05.7542576Z
navigation.errors: 0
navigation.routeSamples: 562
navigation.bedSamples: 117
navigation.limitations: Static navigation geometry only. Dog route graph/loop intention, leashes, animation, runtime cost and devices remain unverified.
occlusion.status: OCCLUSION_DATA_OK
occlusion.utc: 2026-09-26T19:32:49.5555341Z
occlusion.errors: 0
bake.status: SAVED
bake.utc: 2026-09-26T19:32:47.3005585Z
renderComparison.status: SAMPLED_IMAGES_MATCH
renderComparison.utc: 2026-09-26T19:33:06.2966754Z
renderComparison.pairs: 95
renderComparison.limitations: Sparse Edit-mode mono images, 384x216, FOV 60, near 0.3/far 2200. Route nodes every ten steps in both directions plus three key views and two bridge eye heights. OFF/ON/OFF rendered in the same Editor callback. Pixel differences over 2/255 require inspection; identical images do not prove every viewpoint, stereo, mirror or device. Does not measure speed.
survey.utc: 2026-09-26T19:33:08.0257187Z
survey.limitations: Edit-mode daytime mono survey, 512x288, eye height 1.62m, FOV60, near0.3/far2200. Every twenty route nodes in four directions. Sparse images require visual inspection; no aesthetic or stereo/device PASS is inferred.
```

## full-course-scenery

原ファイル: `full-course-scenery.json` / 786 bytes / SHA256 `90282c0f729db87b8adb4f7016cd4fb957c697be7c8901499edac4fa922506e1`

```text
status: SCENERY_STRUCTURE_OK
utc: 2026-09-26T18:07:47.1096225Z
errors: 0
limitations: Saved Edit-mode structure and static navigation only. Screen-relative LOD is camera-driven; no hill occlusion bake, platform performance or visual/device acceptance is inferred.
```

## full-course-sdk-build

原ファイル: `full-course-sdk-build.json` / 2,637 bytes / SHA256 `8083736abded0eebef523492ee6d080ed4a780771bb98845d6e7c50863fb0b3e`

```text
runId: 20260926-213450
status: LOCAL_SDK_BUILD_VERIFIED
sourceRestored: 38732F64F8E84DC57723CC7185A686C1F9795D28D6FAB89A3F66A6168CB864A9
```

## full-course-shoulder-final-performance

原ファイル: `full-course-shoulder-final-performance.json` / 22,497 bytes / SHA256 `6f4f53ea68f515cc48dfe5f3396c60465a8efc5b097022c0d8c903236f722c96`

```text
report.runId: 20260926-205639
report.startedUtc: 2026-09-26T20:56:39.6263358Z
report.endedUtc: 2026-09-26T20:58:55.5873628Z
report.status: MEASURED
report.startedEditorTime: 75779.485677
report.restored: true
report.cameraOcclusionRestored: true
report.restoredCap: -1
report.restoredVsync: 0
report.limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
```

## full-course-shoulder-final

原ファイル: `full-course-shoulder-final.json` / 151,273 bytes / SHA256 `99583ac0975af1d7682123a4327557b477db488e82bd8cc93be11891fd03e514`

```text
applied.utc: 2026-09-26T20:50:03.2866416Z
applied.status: APPLIED_REQUIRES_NAV_PVS_VISUAL_CHECK
applied.samples: 1320980
applied.changedSamples: 139673
applied.protectedSamples: 66564
saved.status: SAVED_HEIGHTS_AND_TREES_OK
saved.maxError: 0.001529694
saved.maxProtectedError: 0.001527786
ground.status: GROUND_STRUCTURE_OK
ground.utc: 2026-09-26T20:51:04.5109225Z
ground.footSamples: 14499
ground.protectedSamples: 534
ground.marginSamples: 4608
ground.errors: 0
ground.loopLengthMetres: 2406.89111328125
ground.limitations: Saved geometry only. Full navigation/dog route, culling, visual finishing, walking time and device/16-player acceptance remain incomplete.
navigation.status: NAV_GEOMETRY_OK
navigation.utc: 2026-09-26T20:51:11.5736237Z
navigation.errors: 0
navigation.routeSamples: 562
navigation.bedSamples: 117
navigation.limitations: Static navigation geometry only. Dog route graph/loop intention, leashes, animation, runtime cost and devices remain unverified.
occlusion.status: OCCLUSION_DATA_OK
occlusion.utc: 2026-09-26T20:51:55.3467139Z
occlusion.errors: 0
bake.status: SAVED
bake.utc: 2026-09-26T20:51:52.1230083Z
images.status: SAMPLED_IMAGES_MATCH
images.utc: 2026-09-26T20:51:56.2739839Z
images.pairs: 95
images.limitations: Sparse Edit-mode mono images, 384x216, FOV 60, near 0.3/far 2200. Route nodes every ten steps in both directions plus three key views and two bridge eye heights. OFF/ON/OFF rendered in the same Editor callback. Pixel differences over 2/255 require inspection; identical images do not prove every viewpoint, stereo, mirror or device. Does not measure speed.
survey.utc: 2026-09-26T20:51:58.9088010Z
survey.limitations: Edit-mode daytime mono survey, 512x288, eye height 1.62m, FOV60, near0.3/far2200. Every twenty route nodes in four directions. Sparse images require visual inspection; no aesthetic or stereo/device PASS is inferred.
```

## full-course-shoulder-measurements

原ファイル: `full-course-shoulder-measurements.json` / 1,567 bytes / SHA256 `79ce368618d10fbe8c6b71e9164fda1bc706d8984f3f9adefc96b6cc73f327f3`

```text
詳細な結果と失敗原因は制作記録の該当段階を参照。
```

## full-course-shoulder-performance

原ファイル: `full-course-shoulder-performance.json` / 22,591 bytes / SHA256 `520021252ca6443320e2f6c3351e8f2f9e1e44ffcf3f914fc7fac56f15ab9cc4`

```text
report.runId: 20260926-203653
report.startedUtc: 2026-09-26T20:36:53.1010485Z
report.endedUtc: 2026-09-26T20:39:07.7151987Z
report.status: MEASURED
report.startedEditorTime: 74592.96039410001
report.restored: true
report.cameraOcclusionRestored: true
report.restoredCap: -1
report.restoredVsync: 0
report.limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
```

## full-course-shoulder-polish

原ファイル: `full-course-shoulder-polish.json` / 151,028 bytes / SHA256 `d87864b0bcdd992368726c9d1e823bae2dff9c82d523ebdfa9874d288ec37a17`

```text
applied.utc: 2026-09-26T20:33:12.7465918Z
applied.status: APPLIED_REQUIRES_NAV_PVS_VISUAL_CHECK
applied.samples: 1320980
applied.changedSamples: 143431
applied.protectedSamples: 145310
saved.status: SAVED_HEIGHTS_AND_TREES_OK
saved.maxError: 0.001529694
saved.maxProtectedError: 0.001527786
ground.status: GROUND_STRUCTURE_OK
ground.utc: 2026-09-26T20:34:50.2924691Z
ground.footSamples: 14499
ground.protectedSamples: 534
ground.marginSamples: 4608
ground.errors: 0
ground.loopLengthMetres: 2406.89111328125
ground.limitations: Saved geometry only. Full navigation/dog route, culling, visual finishing, walking time and device/16-player acceptance remain incomplete.
navigation.status: NAV_GEOMETRY_OK
navigation.utc: 2026-09-26T20:36:06.9560240Z
navigation.errors: 0
navigation.routeSamples: 562
navigation.bedSamples: 117
navigation.limitations: Static navigation geometry only. Dog route graph/loop intention, leashes, animation, runtime cost and devices remain unverified.
occlusion.status: OCCLUSION_DATA_OK
occlusion.utc: 2026-09-26T20:36:36.8699037Z
occlusion.errors: 0
bake.status: SAVED
bake.utc: 2026-09-26T20:36:33.6623054Z
images.status: SAMPLED_IMAGES_MATCH
images.utc: 2026-09-26T20:36:37.7385794Z
images.pairs: 95
images.limitations: Sparse Edit-mode mono images, 384x216, FOV 60, near 0.3/far 2200. Route nodes every ten steps in both directions plus three key views and two bridge eye heights. OFF/ON/OFF rendered in the same Editor callback. Pixel differences over 2/255 require inspection; identical images do not prove every viewpoint, stereo, mirror or device. Does not measure speed.
survey.utc: 2026-09-26T20:36:40.0787067Z
survey.limitations: Edit-mode daytime mono survey, 512x288, eye height 1.62m, FOV60, near0.3/far2200. Every twenty route nodes in four directions. Sparse images require visual inspection; no aesthetic or stereo/device PASS is inferred.
```

## full-course-walk-bench-diagnosis

原ファイル: `full-course-walk-bench-diagnosis.json` / 340,911 bytes / SHA256 `0fa231c83228310a70a7a7d30d21a5d60430135c0fde8ae61441f22aa31a214c`

```text
runId: 20260926-194637
status: FAIL
utc: 2026-09-26T19:46:37.2007447Z
endedUtc: 2026-09-26T19:47:54.2997736Z
settingsRestored: true
playerDataRestored: true
limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. lapChoice=0 is segment coverage only; lapChoice=2/3 is a continuous synthetic-holder lap. Neither measures user walking time or device performance. Random dog pauses remain enabled.
```

## full-course-walk-bench-fixed

原ファイル: `full-course-walk-bench-fixed.json` / 222,423 bytes / SHA256 `067de13a5ddbd26457b35b934199ba5443acd43add9bf2aa7b39c2db6619082c`

```text
navigation.status: NAV_GEOMETRY_OK
navigation.utc: 2026-09-26T19:51:34.5890290Z
navigation.errors: 0
navigation.routeSamples: 562
navigation.bedSamples: 117
navigation.limitations: Static navigation geometry only. Dog route graph/loop intention, leashes, animation, runtime cost and devices remain unverified.
staticBench.excludedFootprintSamples: 30
after.runId: 20260926-195140
after.status: SEGMENTS_OBSERVED
after.utc: 2026-09-26T19:51:40.2729755Z
after.endedUtc: 2026-09-26T19:52:43.7224136Z
after.settingsRestored: true
after.playerDataRestored: true
after.limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. lapChoice=0 is segment coverage only; lapChoice=2/3 is a continuous synthetic-holder lap. Neither measures user walking time or device performance. Random dog pauses remain enabled.
```

## full-course-walk-bridges

原ファイル: `full-course-walk-bridges.json` / 649,041 bytes / SHA256 `af837e4a3bca47e40f6d4e8571e84a188ceafa84650b36203442ed62dad1bb31`

```text
status: BRIDGE_SEGMENTS_OBSERVED
raw.runId: 20260926-184959
raw.status: FAIL
raw.utc: 2026-09-26T18:49:59.8709037Z
raw.endedUtc: 2026-09-26T18:54:32.2101909Z
raw.settingsRestored: true
raw.playerDataRestored: true
raw.limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. Bridge cases and gate wrap are local segments, not a full lap or user walking-time/device acceptance. Random dog pauses remain enabled.
```

## full-course-walk-gate

原ファイル: `full-course-walk-gate.json` / 1,127,098 bytes / SHA256 `bc62771c9c90536d4d75cf80a33d4b4cc377caad0410751a2442e6ad1f1ac75b`

```text
status: SEGMENTS_OBSERVED
before.runId: 20260926-185647
before.status: FAIL
before.utc: 2026-09-26T18:56:47.3918438Z
before.endedUtc: 2026-09-26T18:58:50.4907313Z
before.settingsRestored: true
before.playerDataRestored: true
before.limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. Bridge cases and gate wrap are local segments, not a full lap or user walking-time/device acceptance. Random dog pauses remain enabled.
navReplay.limitation: Recorded half-second positions approximate the transition; this is saved-NavMesh replay, not exact runtime branch instrumentation.
after.runId: 20260926-190510
after.status: SEGMENTS_OBSERVED
after.utc: 2026-09-26T19:05:10.5997626Z
after.endedUtc: 2026-09-26T19:08:58.7565464Z
after.settingsRestored: true
after.playerDataRestored: true
after.limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. Bridge cases and gate wrap are local segments, not a full lap or user walking-time/device acceptance. Random dog pauses remain enabled.
checks.csharpErrors: 0
checks.udonErrors: 0
limitation: Only bounded single-local-client Udon segments. Not a full lap, real walking time, multiplayer/Owner handover, aesthetics or device acceptance.
```

## full-course-walk-lap-first

原ファイル: `full-course-walk-lap-first.json` / 339,215 bytes / SHA256 `8461fd9f12c9e5175d3b49391d927c3421a43f460e9006b06e25d68fa203664b`

```text
runId: 20260926-193828
status: FAIL
utc: 2026-09-26T19:38:28.0729618Z
endedUtc: 2026-09-26T19:39:45.1962971Z
settingsRestored: true
playerDataRestored: true
limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. lapChoice=0 is segment coverage only; lapChoice=2/3 is a continuous synthetic-holder lap. Neither measures user walking time or device performance. Random dog pauses remain enabled.
```

## full-course-walk-lap-forward-final

原ファイル: `full-course-walk-lap-forward-final.json` / 56,857 bytes / SHA256 `fcb0b78995231fd6105e12051f16d0de6ab68f2492a316eb97c4a3f82978b900`

```text
runId: 20260926-214203
status: LAP_OBSERVED
utc: 2026-09-26T21:42:03.6473299Z
endedUtc: 2026-09-26T22:03:22.7447162Z
settingsRestored: true
playerDataRestored: true
limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. lapChoice=0 is segment coverage only; lapChoice=2/3 is a continuous synthetic-holder lap. Neither measures user walking time or device performance. Random dog pauses remain enabled.
sourceReport.traceSamples: 17090
```

## full-course-walk-lap-forward

原ファイル: `full-course-walk-lap-forward.json` / 7,883,207 bytes / SHA256 `b35d86d87703e45f2ed27696145c5e5f2e82e12c3de66ba5833f0fa544fe3bc7`

```text
runId: 20260926-195630
status: LAP_OBSERVED
utc: 2026-09-26T19:56:30.4346913Z
endedUtc: 2026-09-26T20:17:27.9141965Z
settingsRestored: true
playerDataRestored: true
limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. lapChoice=0 is segment coverage only; lapChoice=2/3 is a continuous synthetic-holder lap. Neither measures user walking time or device performance. Random dog pauses remain enabled.
```

## full-course-walk-lap-reverse-final

原ファイル: `full-course-walk-lap-reverse-final.json` / 55,854 bytes / SHA256 `be6fd0c48e40d0d9629c4d5d39b649c69ab32bbd57b825bc7a0e50a938958767`

```text
runId: 20260926-205950
status: LAP_OBSERVED
utc: 2026-09-26T20:59:50.4654354Z
endedUtc: 2026-09-26T21:20:40.4187629Z
settingsRestored: true
playerDataRestored: true
limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. lapChoice=0 is segment coverage only; lapChoice=2/3 is a continuous synthetic-holder lap. Neither measures user walking time or device performance. Random dog pauses remain enabled.
sourceReport.traceSamples: 17001
```

## full-course-walk-measurements

原ファイル: `full-course-walk-measurements.json` / 2,123 bytes / SHA256 `a60d9ee51aca3aa1d0cf837c8af936fb1257d854cf0b9f79878d7c9acc818a8b`

```text
limitations: Saved-scene AssetBundle proxy. Not SDK upload size, device memory or runtime/16-player performance.
```

## full-course-walk-segments

原ファイル: `full-course-walk-segments.json` / 1,328,928 bytes / SHA256 `6d28fa6cbdbcf86a4b033e3a5e1aa4147e2350210b851edacf6c313f2e1150c7`

```text
runId: 20260926-190952
status: SEGMENTS_OBSERVED
utc: 2026-09-26T19:09:52.1567938Z
endedUtc: 2026-09-26T19:17:26.3194926Z
settingsRestored: true
playerDataRestored: true
limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. Bridge cases and gate wrap are local segments, not a full lap or user walking-time/device acceptance. Random dog pauses remain enabled.
```

## full-course-walk-smoke

原ファイル: `full-course-walk-smoke.json` / 15,106 bytes / SHA256 `e1ea2ea2ceab1c4f12ad9b99d4b1bedceed2964293aa9ebde088157c494d3475`

```text
runId: 20260926-184852
status: SEGMENTS_OBSERVED
utc: 2026-09-26T18:48:52.4071426Z
endedUtc: 2026-09-26T18:49:35.5869277Z
settingsRestored: true
playerDataRestored: true
limitations: Single local ClientSim. Menu Udon events exercise attachment and destination request paths; no physical UI click, pickup or real network/Owner handover. Initial actors are placed by the harness. Holder follows saved route by short TeleportTo steps at 2 m/s; dog transforms are never assigned during observation. Bridge cases and gate wrap are local segments, not a full lap or user walking-time/device acceptance. Random dog pauses remain enabled.
```

## measurements-v1

原ファイル: `measurements-v1.json` / 32,785 bytes / SHA256 `402aed5f78d144c105afd8ba655162eba1838dff9134eb094b99bbdd7e1eae25`

```text
baselineEditor.runId: 20260926-160135
baselineEditor.startedUtc: 2026-09-26T16:01:35.4954391Z
baselineEditor.endedUtc: 2026-09-26T16:02:55.4515660Z
baselineEditor.status: MEASURED
baselineEditor.startedEditorTime: 58075.3543528
baselineEditor.restored: true
baselineEditor.restoredCap: -1
baselineEditor.restoredVsync: 0
baselineEditor.limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
representativeSceneryContribution.runId: 20260926-163101
representativeSceneryContribution.startedUtc: 2026-09-26T16:31:01.3576497Z
representativeSceneryContribution.endedUtc: 2026-09-26T16:33:15.2381829Z
representativeSceneryContribution.status: MEASURED
representativeSceneryContribution.startedEditorTime: 59841.2168763
representativeSceneryContribution.restored: true
representativeSceneryContribution.restoredCap: -1
representativeSceneryContribution.restoredVsync: 0
representativeSceneryContribution.limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
baselineBundle.Android.downloadBytes: 44828470
baselineBundle.Android.uncompressedBytes: 115378432
baselineBundle.iOS.downloadBytes: 43806128
baselineBundle.iOS.uncompressedBytes: 110775456
baselineBundle.limitations: Saved-scene proxy AssetBundles; neither SDK upload nor resident/device memory.
```

## measurements

原ファイル: `measurements.json` / 52,743 bytes / SHA256 `3b05ab9716811be4ad0a1666bcb651e1e08ee03090bb9cb44637afb53c7da570`

```text
baselineEditor.runId: 20260926-160135
baselineEditor.startedUtc: 2026-09-26T16:01:35.4954391Z
baselineEditor.endedUtc: 2026-09-26T16:02:55.4515660Z
baselineEditor.status: MEASURED
baselineEditor.startedEditorTime: 58075.3543528
baselineEditor.restored: true
baselineEditor.restoredCap: -1
baselineEditor.restoredVsync: 0
baselineEditor.limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
baselineBundle.Android.downloadBytes: 44828470
baselineBundle.Android.uncompressedBytes: 115378432
baselineBundle.iOS.downloadBytes: 43806128
baselineBundle.iOS.uncompressedBytes: 110775456
baselineBundle.limitations: Saved-scene proxy AssetBundles; neither SDK upload nor resident/device memory.
representativeSceneryContributionV1.runId: 20260926-163101
representativeSceneryContributionV1.startedUtc: 2026-09-26T16:31:01.3576497Z
representativeSceneryContributionV1.endedUtc: 2026-09-26T16:33:15.2381829Z
representativeSceneryContributionV1.status: MEASURED
representativeSceneryContributionV1.startedEditorTime: 59841.2168763
representativeSceneryContributionV1.restored: true
representativeSceneryContributionV1.restoredCap: -1
representativeSceneryContributionV1.restoredVsync: 0
representativeSceneryContributionV1.limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
representativeSceneryContribution.runId: 20260926-165748
representativeSceneryContribution.startedUtc: 2026-09-26T16:57:48.9177009Z
representativeSceneryContribution.endedUtc: 2026-09-26T17:00:02.0590204Z
representativeSceneryContribution.status: MEASURED
representativeSceneryContribution.startedEditorTime: 61448.7768377
representativeSceneryContribution.restored: true
representativeSceneryContribution.restoredCap: -1
representativeSceneryContribution.restoredVsync: 0
representativeSceneryContribution.limitations: Editor Desktop ClientSim; one local player, existing six dogs in their normal scene state; not six leashed dogs or 16 real avatars. Observed frame intervals include Editor work; UnityStats may include Editor views. Allocated/reserved memory is the entire Unity Editor process, not player memory. No GPU/CPU split or real human walking pace is measured. Compare only identical conditions; no platform PASS is inferred.
```

## proposal-data-v1

原ファイル: `proposal-data-v1.json` / 4,144 bytes / SHA256 `d5b0b071aa76ac3ff87ba140ca66076aa8c248a0ff1c6ae6e4263d86d5203c0e`

```text
status: 周回構成採用・代表区間制作中・全周未実装・寸法/性能予算未確定
observed.routeNodes: 17
observed.routeLinks: 16
```
