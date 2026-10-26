# Runtime performance

The benchmark is opt-in (`-jwPerf`). Normal startup does not create a measurement camera, render target or profiler recorders. The three quality options, F10 and the saved graphics preference are preserved. Measurements select Medium without writing PlayerPrefs.

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

Defaults: automatic 1920×1080 camera target, Medium, seed 73021, 15 seconds of warmup then 60 seconds of samples per view, three repeats. Views are a forest clearing, authored creek viewpoint, a fixed dinosaur-area viewpoint and the authored forest corridor. The dinosaur-area camera uses a retained baseline's initial actor location, not the next run's randomized actor location. The animals continue simulating and can leave that viewpoint; this does not guarantee a continuously occupied close-up. The route repeats the same path over 60 seconds. Output includes actual camera transforms, hardware, build mode, CSV, median/P95 frame times, CPU counters, GPU timing when available, draw/batch/triangle/SetPass counters, script markers and PNGs. The screenshots contain the world camera output; overlay HUD acceptance is separate.

The harness observes automatic camera rendering; it never invokes `Camera.Render()`. It requires main-camera render callbacks and excludes simulation ticks that did not draw that camera. On this Windows device both a hidden Player window and `-batchmode` skipped world rendering: Player sampling needs a visible normal window. Never interpret simulation-only timings or black captures as rendered FPS. File formatting, CSV output, screenshot readback and logs occur outside timed intervals. Frame time uses a monotonic clock, avoiding Unity's delta-time clamp. CPU counters can include waiting. Unsupported counters are `unavailable`, never inferred. Render counters in the Editor can include other cameras and auxiliary passes, and are diagnostic rather than the primary acceptance metric. VSync and target frame rate are temporarily disabled and restored with camera/input state during cleanup. The harness exits the test process when finished.

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

Stage-one target: each representative route run has median frame time ≤33.3 ms and P95 ≤40 ms. Stretch target: median ≤16.7 ms. Report failures and remaining bottlenecks rather than converting draw-count reductions into an FPS claim.
