# Final validation

- Runtime and editor C# checks passed.
- Unity scene/animation/material acceptance passed; see `Acceptance.md`.
- Main integration repeated twice and reloaded: exactly one flyer, all four
  existing ground dinosaurs and unrelated scene roots preserved.
- Animated wingspan: 5.00 m. Flight clip: 10.6917 s. Last-frame/start seam:
  0.0114 m maximum vertex difference, sampled before loop wrap.
- Five-minute deterministic flight simulation passed: ~2107 m travelled,
  |X/Z| <= 57.59 m, height 28.48–38.48 m, lowest sampled animated geometry
  26.94 m against a 20.42 m canopy top.
- Player-view capture inspected: flyer visible in open sky from the saved
  camera position when looking up. Model close-up also inspected.
- Windows development build succeeded. Its opt-in runtime smoke passed
  300 game seconds at 10× time scale: 2098.02 m, |X/Z| <= 57.53 m,
  animation continuously playing with 27 observed loop crossings.
- The runtime log separately reports an existing concurrent Sauropelta
  integration issue: `[SpawnRandomizer] 蜥结龙 没有安全的自由漫游出生点。`
  It did not prevent the flyer smoke from passing. Ground-dinosaur logic was
  left to the separate chat working on that integration.

Build: `Builds/PteradactalAcceptance/JurassicWorld.exe`

Logs: `Logs/pteradactal-build.log` and `Logs/pteradactal-player.log`.
