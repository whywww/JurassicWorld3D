# Runtime performance

The benchmark is opt-in (`-jwPerf`). Normal startup does not create a measurement camera, render target or profiler recorders. The three quality options, F10 and the saved graphics preference are preserved. `-Quality Low|Medium|High` (Unity argument `-jwPerfQuality`) selects the measurement tier without writing PlayerPrefs; the default remains Medium.

## Low quality: preserve vegetation first

Normal Low now uses the Materials stage: contact bending/pose sampling and planar reflection stop; a baked creek strip replaces Crest simulation/rendering, and shared plant material variants simplify lighting while retaining cutout silhouettes, authored colors and ordinary tree wind. Tree variants support a separate instanced tint property. Low also tests material-oriented opaque sorting. Medium/High restore original materials, property blocks, rendering flags, Crest component state, contact history and camera sorting. Existing terrain/LOD tiers, culling distances, fog and animal population remain unchanged.

`-LowStage Baseline|Contact|Reflection|Water|Batching|Materials` controls cumulative diagnostic stages. Baseline reproduces prior Low contact/reflection behavior; Contact stops contact work; Reflection additionally stops planar reflection; Water substitutes the creek strip; Batching switches tree variants and sorting; Materials additionally simplifies leaf/bark/ground lighting. The default is Materials, with **zero plant reduction**. Stages and experimental reduction do not persist to PlayerPrefs.

`-VegetationReduction 0|10|20|30` is an optional Materials-stage experiment. The baked ownership list contains 1,757 complete soft plants, excludes tree obstacles/colliders/navigation obstacles and removes overlapping ownership. The fixed order prioritizes plants outside the dinosaur activity bounds, then plants further from the authored route. The nested 10/20/30% sets hide 175/351/527 whole plants via rendering flags; they do not remove objects or alter navigation, distance culling or fog. Restoration preserves each renderer's original flag. Stale ownership entries invalidate a benchmark instead of silently claiming the requested reduction. Preliminary reductions did not materially improve the heavy creek view, so normal Low keeps the full population.

Use an **unlocked desktop** and visible Editor for the 1080p Editor target. `-Mode Editor -Visible` maximizes a fixed-size 1920x1080 Game View, closes Scene Views, turns off deep profiling/profiler recording and renders directly to the Game View. Its displayed UI scale does not reduce the camera's native rendering resolution. Normal Editor and Player measurements use seed 73021 for both Unity random state and dinosaur spawning; normal gameplay keeps its existing spawn-seed policy. The default views include `dinosaur-close`: a camera following the largest ground dinosaur's position/orientation, with main-camera submission required in at least 95% of sample frames. The old fixed `dinosaur` view remains available. Submission verifies presence, not freedom from vegetation occlusion; inspect the PNG. Dynamic animal paths differ between runs, so near-view differences cannot be attributed to a single optimization.

When this project is already open, use `Jurassic World > Performance > Low` menus to run diagnostics/acceptance in the existing Editor instead of starting a competing process. Acceptance menus use 15/60/3; diagnostic menus use 3/10/1. The menus restore prior quality and maximized-window state, and preserve saved quality preferences. Before rebuilding assets, stop Play: `Prepare Creek Assets` reuses the authored river curve, and `Prepare Soft Plant List` rebuilds ownership without saving Main or changing its vegetation.

```powershell
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Editor -Quality Low -LowStage Baseline -Label low-before -Visible
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Editor -Quality Low -LowStage Contact -Label low-contact -Visible -Warmup 3 -Seconds 10 -Repeats 1
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Editor -Quality Low -LowStage Reflection -Label low-reflection -Visible -Warmup 3 -Seconds 10 -Repeats 1
# Formal combined acceptance: 15 seconds warmup, 60 seconds per sample, three repeats.
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Editor -Quality Low -LowStage Materials -Label low-after -Visible
# Optional, only after the no-reduction stages fail:
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Editor -Quality Low -LowStage Materials -VegetationReduction 10 -Label low-thin10 -Visible -Warmup 3 -Seconds 10 -Repeats 1
python Tools\RuntimePerformance\compare_results.py --before Logs\RuntimePerformance\low-before --after Logs\RuntimePerformance\low-after --output Logs\RuntimePerformance\low-60fps.md --views forest,creek,dinosaur-close,route --strict-editor-60
python Tools\RuntimePerformance\test_compare_results.py
```

Strict acceptance requires completed, visible, maximized 1080p Low Editor captures, seed 73021, full sampling, matching hardware/settings and median **and P95 <=16.7ms in every repeat**. Short diagnostics, interrupted/partial rendering, quality/resolution changes and batchmode cannot pass. Successful captures write `complete.txt`. Read population, reduction, stage, dinosaur submission diagnostics and screenshots alongside FPS. Lock-screen captures are invalid even if a graphics callback happened. CPU/GPU counters can lag the wall-clock sample and cannot be added together; unavailable frame-clock markers are not reported as zero work.

`-Mode NavigationLow` verifies the existing Main navigation HUD in Low, including scene reload, reflection/contact state and unchanged plant/tree/dinosaur population. It does not install a HUD or save the scene. The existing `Navigation` mode still installs the fixture and belongs in a disposable test project.

The completed stage diagnostics and formal validation status are in [LowQualityResults.md](LowQualityResults.md). Neither a reduction in draw counters nor a short median-only result proves stable 60 FPS.

Measured outcomes, screenshots, failed budgets and regression limits are in [Results.md](E:/Unity/JurassicWorld/Tools/RuntimePerformance/Results.md).

## Reproduce

Close the Editor for the target project, or use an independent copy under `Builds/RuntimePerformanceProject` containing `Assets`, `Packages`, and `ProjectSettings`. Do not put a test project in `Temp`: Unity owns and clears that directory. Copying `Library` speeds imports but is optional. Run graphics tests sequentially; competing Unity processes in Play invalidate comparisons. Do not use `-nographics`.

From PowerShell in the repository:

```powershell
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Audit
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Editor -Label before-editor
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Build
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Player -Label before-player -Visible
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode FrameDebugger -Label inspection -Visible
```

Use `-ProjectRoot` to select the independent copy, `-UnityEditorPath` if IDE references have not been generated, and `-PlayerPath` to select a retained baseline executable. Before/after runs must use the same executable build type and sample configuration. The default builder creates a **Development Player without Deep Profiling**, so CPU diagnostics are available; its FPS is not a claim about a release build.

Defaults: automatic 1920×1080 camera target, Medium, seed 73021, 15 seconds of warmup then 60 seconds of samples per view, three repeats. Views are a forest clearing, authored creek viewpoint, a following dinosaur close-up and the authored forest corridor. The optional legacy `dinosaur` view uses a fixed location and does not guarantee a continuously occupied close-up. The route repeats the same path over 60 seconds. Output includes actual camera transforms, hardware, build mode, CSV, median/P95 frame times, CPU counters, GPU timing when available, draw/batch/triangle/SetPass counters, script markers and PNGs. Visible Editor screenshots include overlay HUD; HUD behavior has separate functional acceptance.

The harness observes automatic camera rendering; it never invokes `Camera.Render()`. It requires main-camera render callbacks and excludes simulation ticks that did not draw that camera. On this Windows device both a hidden Player window and `-batchmode` skipped world rendering: Player sampling needs a visible normal window. Never interpret simulation-only timings or black captures as rendered FPS. File formatting, CSV output, screenshot readback and logs occur outside timed intervals. Frame time uses a monotonic clock, avoiding Unity's delta-time clamp. CPU counters can include waiting. Unsupported counters are `unavailable`, never inferred. Render counters in the Editor can include other cameras and auxiliary passes, and are diagnostic rather than the primary acceptance metric. VSync and target frame rate are temporarily disabled and restored with camera/input state during cleanup. CLI captures exit their test process when finished; interactive menu captures stop Play and keep the existing Editor open.

Short isolation example (diagnostic only, not formal acceptance):

```powershell
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Editor -Label isolation -Warmup 3 -Seconds 10 -Repeats 1 -Views forest,dinosaur -Cases normal,no-visibility,no-shadows,no-finish,no-interaction
```

To compare the depth paths, set `DinosaurVisibility.reuseMainCameraDepth` to false for a retained test variant. Runtime normally reuses a depth texture already requested by the atmosphere. A camera without a depth texture uses the original probe path; explicit synchronous `CaptureNow` also retains that path. Runtime has at most one GPU readback in flight. A readback error falls back to a capped synchronous path.

## Changes and boundaries

- Visibility reuses target/renderer caches, collections, command buffers and snapshot arrays. Main depth sampling avoids a second forest render, while renderer callbacks retain the camera's actual LOD choice. Selection thresholds and information card scoring are unchanged.
- Near foliage uses the same contact geometry and spring rules. Medium visual interactions are limited to plants whose world bounds are within 55 m (Low 35 m, High 85 m); existing active fields can settle through a 10 m margin. Trunk geometry, collisions, routes and AI remain independent of this visual budget. Tests without an assigned budget camera retain unlimited simulation.
- Contact motion coefficients are prepared once per plant/contact; untouched field nodes skip spring integration. Slow frames bound spring substeps to avoid multiplying simulation cost (the authored 30/60/144 FPS steps are unchanged, and higher spring frequencies retain finer steps). Displacement uniforms are only resubmitted when values change. Settled plants restore their original property block, allowing instancing again when the original block was empty.
- The explicit Materials maintenance action enables instancing only on supported project material assets referenced by Main or its restoration bindings. It does not merge materials, combine deforming meshes or regenerate the forest. Review `instancing-materials.md` and compare samples before keeping asset changes.
- HUD formatting updates when displayed coordinates/headings change; marker collections are reused. AI and foot locking receive profiling markers; their update cadence is preserved.
- Small plants explicitly marked `CenterGapGroundPlant` receive a runtime cull LOD if they are at most 1.5 m tall and 4 m wide, with no authored LOD in their hierarchy. Medium culls these distant plants at roughly 54 m for a 60° view; Low uses its lower LOD bias, and High retains distant coverage. Near density, large plants and existing LODs are preserved.
- Medium shadow distance is 50 m (previously 65 m), and its LOD bias is 0.85 (previously 1). With that bias, the new small-ground cull is about 46 m at 60° FOV. The shadow cascade count, near plant density, render resolution and High preset are unchanged.

## Regression and acceptance

```powershell
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Budgets -Label regression
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Visibility -Label regression
.\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode Navigation -Label regression
```

Budget acceptance checks repeated Low/Medium/High switching, saved preference preservation and near/far foliage transitions. Visibility acceptance compares probe and main depth for unobstructed silhouettes, actual LOD, opaque occlusion, transparent/cutout leaves, terrain, dinosaur mutual occlusion and the skinned flyer, then checks async cleanup. The graphical fixture may explicitly render a camera; those renders are outside benchmark timings.

Run the remaining suites sequentially in the independent test copy after performance sampling:

```powershell
$testProject = Join-Path $PWD 'Builds\RuntimePerformanceProject'
foreach ($suite in 'Animation','Follow','Return','Traffic','Emotiv','MindWave','Foliage') {
    .\Tools\RuntimePerformance\Run-Benchmark.ps1 -Mode $suite -ProjectRoot $testProject -Label regression
}
.\Tools\RuntimePerformance\Check-ContactMath.ps1
```

Navigation installs its HUD in the test scene. The full foliage maintenance acceptance rebuilds prepared assets and saves the test scene; run it last and retain/restore the measured project state before reusing that copy for performance comparisons. Neither should run against unsaved user scene edits. Keyboard and brain-input fixtures use synthetic data; physical headset/authorization acceptance remains separate.

The historical Medium target was median ≤33.3 ms and P95 ≤40 ms. Low acceptance uses the stricter budget above: median and P95 ≤16.7 ms in every repeat. Report failures and remaining bottlenecks rather than converting draw-count reductions into an FPS claim.
