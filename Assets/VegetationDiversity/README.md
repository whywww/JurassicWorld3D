# Vegetation Diversity: Phase 2 Full Layout

Open `Assets/Scenes/Main.unity`. The full layout is the root object **Phase 2 Biome Vegetation**. It adds 30 vegetation groups, 60 trees, and 180 fern/bush instances across the playable area. Autumn Fab trees are placed west of x=-20, tropical coconut palms and broadleaf trees east of x=20, and green Nature Starter Kit trees form the central transition. ForestSeeder and TerrainData tree seeding remain unchanged.

The scene includes the previously reviewed 15-group layout plus 15 added groups to fill more of the player exploration area. The saved first-stage scene is `Tools/VegetationDiversity/Checkpoints/Main_Phase2_Stage1Review.unity`; the pre-Phase-2 scene is `Tools/VegetationDiversity/Checkpoints/Main_BeforePhase2.unity`.

The Sketchfab Banana Tree source is supplied as `Palm.fbx`. Its bark vertices are widened 1.7× in the prepared `BananaTree.fbx` so the trunk remains visible at the prototype's 3.1–3.3 m scene scale. Leaves, UVs, and bark material remain from the source. Regenerate it with `prepare_models.py banana <source Palm.fbx> <prepared BananaTree.fbx>` if needed.

Editor commands under **Jurassic World > Vegetation Diversity**:

- **Prepare Assets** refreshes the Built-in RP materials and prefabs from the prepared FBX files.
- **Build Phase 2 Full Layout** replaces this scene root idempotently and re-registers scene-tree trunk avoidance.
- **Validate Phase 2 Full Layout** checks 30 groups, 60 tree trunks, biome placement, all six route loops and turns, existing island saplings and central rocks, runtime obstacle registration, and palm LODs.
- **Measure Phase 2 Render Cost** compares offscreen 1280×720 CPU render submission with the root off and on, and reports mesh/texture memory. It does not measure GPU frame time.

`Jurassic World > Bake Scene` regenerates the full layout after terrain, rocks, and central islands are rebuilt. The bake hook does not add trees to TerrainData or alter ForestSeeder.

The visual review rejected orange alpha fringes and broad banana-like leaves in the downloaded Sketchfab palm textures. The scene uses project-generated pinnate coconut palms (`Assets/VegetationDiversity/GeneratedPalms`) with opaque green leaflets, connected midribs, visible tapered trunks, and three geometry LODs. The downloaded Sketchfab files remain archived as source assets but are no longer used by the scene.

The final 1280×720 offscreen comparison with all repeated Fab trees on three LODs reported 3.15 ms baseline vs 4.74 ms with the vegetation root active (+1.59 ms CPU render submission), 339,529 unique mesh LOD triangles, 53.4 MiB mesh memory, and 38.0 MiB texture memory. GPU frame time was not measured in batch mode; inspect it on the target machine in Unity's Game view.
