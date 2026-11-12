# Runtime performance: three quality tiers

Current acceptance uses visible, non-Development Windows D3D11 Players at 1920×1080. There is no mandatory 60 FPS gate. The earlier Editor-only 60 FPS experiment is historical; see `LowQualityResults.md` and `Results.md` for its dated results, not current defaults.

## Current defaults

| Budget | Low | Medium | High |
|---|---:|---:|---:|
| Internal world render scale (normal gameplay) | 1.00 | 1.00 | 1.00 |
| Manifest soft plants retained (baseline 1757) | 879 | 1230 | 1582 |
| Shadow distance | 0 m | 30 m | 60 m |
| Shadow cascades | off | single | 2 |
| Plant contact radius | off | 8 m | 15 m |
| LOD bias | 0.5 | 0.75 | 1.0 |

All tiers share simplified foliage materials, per-instance color handling and static creek water. Crest simulation, planar reflection, mist particles and shafts are disabled. Final gameplay uses native 3D resolution in every tier. The optional benchmark scale uses a world-only RenderTexture and a native-resolution UI; resize and tier switches recreate or release that target. The measured 75%/85% candidates had mixed benefits and were not selected. F10 selects tiers and preserves the saved preference.

Soft-plant selection is deterministic, nested and whole-plant based. It only affects validated, collision-free manifest owners; hard tree blockers and dinosaur populations remain unchanged. Terrain detail density/distance retain their previous per-tier factors. `terrain-details.csv` records layer map units and density, not actual visible grass instance counts. Do not describe manifest retention as retention of all vegetation.

Native Renderer GPU instancing and shared materials remain enabled. The experimental explicit `DrawMeshInstanced` implementation was rejected because its depth differed from native rendering; it is not shipped. Dinosaur animation/update cadence is unchanged: measured feet work was small. Collision broadphase and stable geometry/tree-query caches address the measured AI spike without skipping navigation, contact or collision logic.

## Reproduce

Run from the project root with the target project's Editor closed. Run Unity sequentially and stop other heavy tasks. Keep the Player visible and the desktop unlocked. The runner marks minimized, locked or interrupted Player samples invalid. Never use `-nographics` for these measurements.

```powershell
# Default build is non-Development. Add -Development for diagnostics only.
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Build
# Short diagnostic: native 1080p world rendering, not formal evidence.
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Player -PlayerPath Builds/AllTierClean/JurassicWorld.exe -Quality Medium -RenderScale 1 -Label short-medium -Visible -Warmup 3 -Seconds 10 -Repeats 1
# Full retained baseline/native/final matrix: about two hours.
.\Tools\RuntimePerformance\Run-AllTierMatrix.ps1 -Prefix formal-example
python Tools/RuntimePerformance/compare_all_tiers.py --prefix formal-example --output Tools/RuntimePerformance/AllTierComparison.md
```

`-jwPerfQuality Low|Medium|High` defaults to Medium. `-jwRenderScale` overrides the normal native scale only during benchmark execution; the runner defaults to 1 to separate native rendering gains from reduced resolution. `-jwPerfDevelopment` opts into diagnostic builds. `-PlayerPath` chooses a retained executable. `-VegetationReduction -1` uses the tier defaults; 0 explicitly preserves the manifest pool. The obsolete staged `-LowStage` / `-jwLowStage` switches and custom instancing flags have been removed. Rendering always uses the retained optimized profile.

The matrix uses forest, creek, an occupied dinosaur close-up and the roaming route, each with 15 seconds warmup, 60 seconds sampling and three repeats. It interleaves retained baseline and optimized captures by tier. Native rendering is the final configuration in every tier; scaled Low/Medium captures are rejected candidates, retained for comparison. The formal report validates completion, visible Player rendering, build type, actual resolution, scale, hardware, sample duration, fixed poses, seed, hard tree/animal counts and close-up submission coverage. Missing counters are unavailable, not zero.

Frame times use monotonic time between automatic camera renders. Output formatting and screenshots occur outside timed intervals. CPU counters can include waiting and cannot be added to GPU time. The CSV, per-repeat median/P95/P99, draw counters, GC, plant counts, poses and PNGs remain in `Logs/RuntimePerformance/<label>`. Dynamic animal trajectories can differ even with a fixed seed; inspect repeat consistency. FPS in the comparison is frame count divided by total measured frame time, not the reciprocal of the median.

## Cleanup scope

The performance-only acceptance classes, opt-in Player smoke fixture, depth-capture test shader, old Batch-stage shaders, terrain probe, contact-math harness and Python test modules have been removed. The old fixed `dinosaur` benchmark view is removed; use `dinosaur-close`. Shared production shaders, baked creek/plant assets, geometry caches, profiling markers and runtime quality controls remain.

Editor menus now offer Diagnostic (3/10/1) and Capture (15/60/3) for Low/Medium/High. They all use native resolution and the current tier plant budget. The old Low stage menus and Budgets runner mode are retired. Unrelated navigation, brain input and dinosaur behavior fixtures remain outside this cleanup scope.

Prior test results in the reports describe validation completed before cleanup; their removed commands are historical. Current sampling/report generation remain supported. Original optimized, baseline and measured Players are preserved. The cleaned source builds a separate non-Development Player at `Builds/AllTierClean/JurassicWorld.exe`; short post-cleanup captures are diagnostic checks, not a new formal performance claim.

Final Development diagnostics: [AllTierDiagnostic.md](AllTierDiagnostic.md).

Formal results and limitations: [AllTierResults.md](AllTierResults.md), [AllTierComparison.md](AllTierComparison.md). The exact measured optimized package is retained at `Builds/AllTierMeasured`; the rebuilt shipped package at `Builds/AllTierOptimized` changes only the default resolution choice and its smoke fixture. Timed captures explicitly set render scale, so the selected native captures match the shipped defaults.

`-StartTier Medium|High` resumes the remaining matrix tiers under the same prefix after completed captures are retained and the failed directory/log are separately archived. A previous INVALID marker must never be silently cleared. The runner requests display/system wakefulness while its own visible Player runs and restores that request afterward; it does not change lock-screen policy.

Cleanup details: [Cleanup.md](Cleanup.md). A new build from the runner uses `Builds/Performance`; pass that path with `-OptimizedPlayer` when comparing that build instead of the delivered cleaned package.
