# 环境资产

本目录统一存放森林、岩石与枯木、小溪和地形相关的环境资产，沿用 `Assets/Dinosaurs/<物种>/{Source,Prepared}` 的组织方式。

## 目录规则

- `<类别>/<资产集>/Source/`：厂商原始包，只读。保留原 FBX、贴图、材质和目录名，不手工修改。
- `<类别>/<资产集>/Prepared/`：项目实际使用的 prefab、材质、网格与贴图，由资源准备菜单或编辑态 Bake 生成。
- 移动或重命名资产时连同 `.meta` 一起移动，以保持 GUID 引用；不要在 Unity 打开项目时移动资产。
- 场景、prefab 和 TerrainData 通过 GUID 引用资产，目录整理本身不需要修改场景文本。

## 资产组

| 类别 | 资产组 | Source | Prepared | 主要使用方 |
|---|---|---|---|---|
| 植被 | `Vegetation/Trees` | `FabDesktopTrees60/`、`PolyHaven/` 下 7 个树包、`SketchfabBananaTree/` | 11 个 prefab、28 个材质、17 个 FBX（含 LOD）；另有 [README](Vegetation/Trees/README.md) | Central Forest、Phase 2、Rainforest、Northwest Autumn 等种植流程 |
| 植被 | `Vegetation/Fern02` | Poly Haven Fern 02 原始贴图 | 4 个 prefab，以及网格、材质、贴图 | `UnderstorySeeder`、`MainCreekIntegration`、`CentralForestDensification` |
| 植被 | `Vegetation/Interaction` | 无（项目生成的交互网格） | 34 个 mesh、36 个 material | `FoliageInteractionPreparation`、Main 场景枝叶互动 |
| 岩石与枯木 | `RocksAndDeadwood/RockMossSet01` | Poly Haven Rock Moss Set 01 原始贴图 | 6 个 prefab，以及网格、材质、贴图 | `UnderstorySeeder`、`CentralForestDensification`、`MainCreekIntegration` |
| 岩石与枯木 | `RocksAndDeadwood/Deadwood` | `DeadTreeTrunk02/`、`TreeStump01/` 原始包 | 2 个 prefab，以及网格、材质、贴图 | `UnderstorySeeder`、`CentralForestDensification`、`DeadVegetationPass` |
| 岩石与枯木 | `RocksAndDeadwood/AutumnGroundedLog` | 从倒木派生的秋林版本 | 1 个贴地倒木 prefab、LOD 网格、`AutumnMoss` | `DeadVegetationPass`、`NorthwestAutumnForest` |
| 小溪 | `Creek` | 无（项目配置） | `CrestCreekWater.mat`、`CreekRippleSpectrum.asset`、`JungleCreekBank.terrainlayer`、`MainCreekTerrain.asset` | `MainCreekIntegration` |
| 地形 | `Terrain` | 无（项目生成） | 2 个 TerrainData、4 个 terrain layer、`GroundEcology/` | Bake、Main 场景 |

## 第三方与中间件

- `Assets/ThirdParty/NatureStarterKit2/`：Nature Starter Kit 2 原包，保留内部目录结构，直接引用；不复制进 `Environment`。
- `Assets/Crest/`：Crest 水体系统，保持原位，不参与本次环境资产整理。

## 生成物与重建

- `Terrain/Prepared/GroundEcology/Surface/`：`GroundEcologyPass` 的生成输出，包含 Moss、DarkGrass、Leaves、Roots 四组网格/材质及 `RouteGrassTuft`。其中 `Moss.mat` 也被 `DeadVegetationPass` 引用。
- `Terrain/Prepared/GroundEcology/Natural/`：`NaturalGroundDecoration` 的生成输出，主场景实际引用这组网格/材质和 `ForestFloorAtlas.png`。
- `RocksAndDeadwood/AutumnGroundedLog/`：`AutumnGroundedLogAssetBuilder` 与 `DeadVegetationPass` 的生成物。
- `Terrain/Prepared/MainInteractionTerrain.asset`：`TRexInteractionBake` 生成，主场景实际使用。
- `Terrain/Prepared/JurassicTerrainData.asset`：基础地形。
- `Creek/Prepared/MainCreekTerrain.asset`：`MainCreekIntegration` 的历史生成物，当前场景不引用，保留用于对照。
- `Terrain/Prepared/GroundEcology/LocalForestRepairBaseline.asset`：`LocalForestRepair` 的基准数据。

重建时按现有流程执行：**Jurassic World > Assets > Integrate PolyHaven Assets**（幂等）→ **Prepare Biome Vegetation** / **Prepare Five Forest Tree Species** → 各植被种植与区域流程 → **Jurassic World > Scene > Bake Scene**。仅验证目录移动时不要重跑 Bake，避免覆盖 Main 和 TerrainData。

## LFS 与验收

- Git LFS 跟踪 `Terrain/Prepared/JurassicTerrainData.asset`、`Terrain/Prepared/MainInteractionTerrain.asset` 及 `ThirdParty/NatureStarterKit2/` 内的地形与部分贴图。`MainCreekTerrain.asset` 与 `LocalForestRepairBaseline.asset` 仍是普通 Git 文件。
- 只读验收入口：`JurassicWorld.Editor.EnvironmentAssetOrganizationAcceptance.Verify`，结果写入 `Logs/environment-asset-organization.txt`。批处理方式见根 [README](../../README.md)。
- 验收检查旧目录未回潮、资产数量、Main 的丢失引用、地形原型与 layer，以及代码/JSON 中是否残留旧路径。

## 维护注意

- 不要手工覆盖 `Prepared/` 中由菜单生成的文件；改动导入逻辑后重新运行对应准备菜单。
- 新增资产组时沿用 `Source/` 与 `Prepared/` 分层，并同步 Editor 路径常量与验收数量。
- `GroundEcology/Surface/` 与 `Natural/` 中的同名材质不是重复副本，用途不同，不能互相替换。
