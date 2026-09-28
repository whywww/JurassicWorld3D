# Vegetation Diversity Prototype

Open `Assets/Scenes/Main.unity`. The prototype is the root object **Vegetation Diversity Prototype (X -15..15, Z 60..90)** at the north edge of the initial camera view. Toggle that root in the Hierarchy to compare the existing forest with the new tree mix. The existing 15 Terrain trees in this 30 × 30 m region are unchanged; the root adds 11 trees and four understory plants.

Tree forms in the patch: four existing Nature Starter Kit 2 trees, three Fab broadleaf shapes, two Sketchfab palms, and one optimized Poly Haven island tree. Banana and Pachira add the broad-leaf understory. The original tree share is 15 / 26 = 57.7%.

The Sketchfab Banana Tree source is supplied as `Palm.fbx`. Its bark vertices are widened 1.7× in the prepared `BananaTree.fbx` so the trunk remains visible at the prototype's 3.1–3.3 m scene scale. Leaves, UVs, and bark material remain from the source. Regenerate it with `prepare_models.py banana <source Palm.fbx> <prepared BananaTree.fbx>` if needed.

Editor commands under **Jurassic World > Vegetation Diversity**:

- **Prepare Assets** refreshes the Built-in RP materials and prefabs from the prepared FBX files.
- **Build Prototype** replaces only this scene root. It does not write TerrainData or change ForestSeeder.
- **Validate Prototype** checks tree types, counts, old-tree share, route clearance, materials, and the hero LOD group.
- **Measure Render Cost** compares offscreen camera render CPU time with the root off and on; it does not save a scene change.

`Jurassic World > Bake Scene` rebuilds `Main.unity` from scratch and removes this standalone root. Run **Build Prototype** afterward to restore the same layout. The branch `codex/vegetation-diversity-prototype` starts from the unmodified project state and serves as the rollback checkpoint.

The current version is ready for the user's visual check in Unity. No visual acceptance screenshots were generated.
