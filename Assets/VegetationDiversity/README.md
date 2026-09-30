# Vegetation Diversity: Phase 2 Full Layout

Open `Assets/Scenes/Main.unity`. The full layout is the root object **Phase 2 Biome Vegetation**. It adds 30 vegetation groups, one banana grove, 60 trees, and 180 fern/bush instances across the playable area. Autumn Fab trees are placed west of x=-20, the 12 banana trees form a grove in the southeast clearing (x=50–70, z=-65 to -35), other broadleaf trees remain east of x=20, and green Nature Starter Kit trees form the central transition. ForestSeeder and TerrainData tree seeding remain unchanged.

The scene includes the previously reviewed 15-group layout plus 15 added groups to fill more of the player exploration area. The saved first-stage scene is `Tools/VegetationDiversity/Checkpoints/Main_Phase2_Stage1Review.unity`; the pre-Phase-2 scene is `Tools/VegetationDiversity/Checkpoints/Main_BeforePhase2.unity`.

The Sketchfab Banana Tree source is supplied as `Palm.fbx`. Its bark vertices are widened 1.7× in the prepared `BananaTree.fbx` so the trunk remains visible at the scene's 3.2–3.6 m scale. Leaves, UVs, and bark material remain from the source. Regenerate it with `prepare_models.py banana <source Palm.fbx> <prepared BananaTree.fbx>` if needed.

Editor commands under **Jurassic World > Vegetation Diversity**:

- **Prepare Assets** refreshes the Built-in RP materials and prefabs from the prepared FBX files.
- **Build Phase 2 Full Layout** replaces this scene root idempotently and re-registers scene-tree trunk avoidance.
- **Validate Phase 2 Full Layout** checks 30 groups plus the banana grove, 60 tree trunks, 12 clustered banana trees outside the creek, biome placement, all six route loops and turns, existing island saplings and central rocks, runtime obstacle registration, and Fab tree LODs.
- **Measure Phase 2 Render Cost** compares offscreen 1280×720 CPU render submission with the root off and on, and reports mesh/texture memory. It does not measure GPU frame time.

`Jurassic World > Bake Scene` regenerates the full layout after terrain, rocks, and central islands are rebuilt. The bake hook does not add trees to TerrainData or alter ForestSeeder.

The scene uses the prepared Sketchfab Banana Tree prefab in the 12 former coconut-palm slots, at 3.2 m and 3.6 m. They are grouped in a compact southeast grove; placement fails rather than spreading a tree outside the clearing or into the creek. The downloaded Sketchfab palm source files remain archived but are not used by the scene.

The previous coconut-palm layout measured 3.15 ms baseline vs 4.74 ms with the vegetation root active (+1.59 ms CPU render submission) at 1280×720. Re-run **Measure Phase 2 Render Cost** after this banana-tree replacement; GPU frame time is not measured in batch mode.
