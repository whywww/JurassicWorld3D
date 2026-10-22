# Flying Pteradactal

Source archive: `SourceArchives/Dinosaurs/Pteradactal/animated-flying-pteradactal-dinosaur-loop.zip`.
The original GLB (including skeleton and embedded textures) is retained under
`Assets/Dinosaurs/Pteradactal/Source`; converted FBX, extracted textures, material,
Legacy clip, prefab and baked airspace are under `Assets/Dinosaurs/Pteradactal/Prepared`.
No runtime glTF package is required.

## Regenerate

1. Run `blender --background --python Tools/Pteradactal/convert_model.py` from
   the project root. This extracts the GLB, exports the skinned FBX and textures,
   and packs glTF roughness/metallic channels for the Unity material.
2. In Unity, use **Jurassic World → Scene → Add or Update Flying Pteradactal**.
   Save any unrelated scene edits first. This updates only the flying dinosaur
   and the HUD loading count in Main; existing terrain and vegetation are preserved.
3. A full **Bake Scene** also adds the flyer after environment generation.

The flyer is independent of `DinosaurAgent`, ground navigation, foot locking,
summoning, and player interactions. Serialized flight settings are available on
the scene object's `PteradactalFlightController`. Its visual child plays the
Legacy flight loop while the parent handles movement. The initial wing span is
five meters. The source's double-sided wing membranes are preserved.

Default speed: 5 m/s. Smooth arc turns are limited to 45 degrees/s, with
15-degree visual banking. Horizontal bounds: ±75 m, including the model margin.
Flight height: 6–10 m **above local terrain**, not above the tallest tree.

The editor bakes `LowFlightAirspace.asset` from LOD0 scene meshes and Terrain
prototype instances, including leaf and branch geometry. A 0.5 m voxel grid and
an exact three-axis Euclidean distance transform provide conservative clearance;
voxel quantization error is subtracted before queries. The full animated model
is sampled at 60 Hz; its rotation-independent envelope plus 0.5 m margin, bobbing
and curve-sampling allowance must fit the entire route. The largest connected
low-air region supplies safe holding loops, avoiding isolated patches.

Roaming changes between validated loops through tangent-continuous Dubins
connections. Only complete connections that join another safe loop are accepted;
a failed request keeps flying the previous safe route. Front-pass attempts use
the player's horizontal forward direction, target 30–45 m ahead, and cross
laterally. After each pass the next attempt is scheduled in 25–40 s. Blocked
views retry every 5 s once the current connection finishes. A committed pass
finishes naturally instead of chasing the player's head rotation.

`speed`, `passInterval`, `passDistance`, `retryInterval`, and `randomSeed` are
serialized Inspector settings. Re-run the integration after editing vegetation
or terrain to regenerate the scene-specific airspace. In dense forest no safe
low front pass may fit; the flyer stays on its safe route until a clear view
becomes reachable. The camera is never steered by the flight controller.

## Acceptance

Run Unity with `-batchmode -projectPath <project> -executeMethod
JurassicWorld.Editor.PteradactalAcceptance.IntegrateAndVerify -logFile <log>`.
It integrates twice, reloads Main, checks preserved ground objects, clip
deformation and loop continuity, renders level-camera flybys at two open positions, and simulates five
minutes of flight at 50 Hz with player movement, sudden yaw changes and forest
occlusion. Analytic connection endpoints and tangent continuity are also checked. Results are written alongside this file. This
simulated test does not replace the Play Mode/build checks.

Add `-pteradactal-build` to also create the development build under
`Builds/PteradactalAcceptance`. Launch that build with `-pteradactal-smoke` to
run 300 game seconds at 10× speed; it writes `BuildSmoke.txt` here and exits.
Normal launches do not activate the diagnostic. An already-open, idle editor
can run the same acceptance/build by receiving `verify-and-build` in
`Logs/pteradactal-request.txt`; the request waits during Play, import, or unsaved
scene edits, and leaves the editor open on completion.
