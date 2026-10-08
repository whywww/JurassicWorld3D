# 3D 侏罗纪世界

基于 Unity Built-in Render Pipeline 的恐龙森林探索项目。Unity 版本为 **`2022.3.62f3c1`**（以 `ProjectSettings/ProjectVersion.txt` 为准）。

场景包含森林、香蕉林、草地与蕨类、苔藓岩石、枯木，以及从东侧延伸至北侧的 Crest 小溪。恐龙沿固定路线活动，玩家可在林地中步行观察。

## 安装与启动

### 从 GitHub 克隆到本地

先安装 [Git](https://git-scm.com/downloads) 和 [Git LFS](https://git-lfs.com/)。本仓库的模型、贴图及部分 Terrain 数据由 Git LFS 管理，必须下载实际大文件后再打开 Unity。安装方法可参考 [GitHub 官方说明](https://docs.github.com/en/repositories/working-with-files/managing-large-files/installing-git-large-file-storage)。

在 PowerShell 或 Git Bash 中进入希望存放项目的父目录，执行：

```shell
git --version
git lfs version
git lfs install
git clone https://github.com/whywww/JurassicWorld3D.git
cd JurassicWorld3D
git lfs pull
git lfs ls-files
```

`git lfs install` 初始化本机的 LFS 支持；`git lfs pull` 下载当前版本的大文件并填充工作目录（见 [Git LFS 命令说明](https://github.com/git-lfs/git-lfs/blob/main/docs/man/git-lfs-pull.adoc)）。即使克隆时已经自动下载过，仍可执行该命令检查并补齐。`git lfs ls-files` 列出的条目中，`*` 表示工作目录中已有实际文件，`-` 表示仍是指针文件。若提示 `git: 'lfs' is not a git command`，安装 Git LFS 后重新打开终端。

已有本地仓库时，在项目根目录执行：

```shell
git status
git lfs install
git pull --ff-only
git lfs pull
```

更新前保存并提交或暂存需要保留的本地修改。`git pull --ff-only` 更新当前分支的代码，`git lfs pull` 补齐大文件。下载失败时检查 GitHub 访问、账号的仓库权限及 LFS 错误信息，成功后再打开 Unity。

### 在 Unity 中打开

1. 安装 Unity Hub，并安装项目指定的 Unity **2022.3.62f3c1**。本机已有编辑器时，可直接使用 `C:\Program Files\Unity 2022.3.62f3c1\Editor\Unity.exe`。请先匹配版本，避免直接升级项目。
2. 在 Unity Hub 中选择 **Add / 添加项目**，选择仓库根目录（包含 `Assets`、`Packages` 和 `ProjectSettings`）。
3. 打开项目，等待 Package Manager 恢复 `Packages/manifest.json` 中的依赖、Unity 导入资源并完成脚本编译。首次恢复 Unity 包需要访问对应包源。
4. 打开 `Assets/Scenes/Main.unity`，或选择 **Jurassic World > Open Main Scene**。
5. 确认 Console 没有编译错误和丢失资源错误，点击 **Play**，在 Game 窗口中操作。

**普通启动直接使用保存好的 Main 场景，无需重新 Bake。** `Bake Scene (Edit Mode)` 会重建场景内容，用于主动维护或重建；执行前保存需要保留的场景改动。

### 资源完整性

- `Assets/NatureStarterKit2/`、`Assets/Crest/`、`Assets/FerociousIndustries/` 及现有准备后的模型、材质均已有 Git 跟踪内容。完整检出后无需无条件重新导入 Nature Starter Kit 2。
- 若 Console 提示模型、材质或脚本丢失，先确认仓库文件完整，再按下方资产清单恢复对应目录。Asset Store 资源缺失时，从原商品页通过自己的账号获取并导入，保留项目引用所需路径。
- 原始 ZIP 被 `.gitignore` 排除，不能假定克隆后包含全部下载压缩包。正常运行使用已导入资源；只有重新制作资产时才需要对应原始文件及准备工具。不要用 Bake 代替补齐缺失资源。
- Package Manager 恢复失败时，检查 Unity 包源网络和 Console 报错，再重新解析依赖；不要添加已移除的 glTFast Git 地址。

### 构建与启动 Windows 程序

1. 在 Unity Hub 中为该编辑器安装 Windows 构建支持；使用 IL2CPP 时还需对应的 **Windows Build Support (IL2CPP)** 及工具链。
2. 在编辑模式下选择 **Jurassic World > Scene > Build Windows Player**，构建保存的 `Assets/Scenes/Main.unity`。
3. 构建成功后运行 `Builds/JurassicWorld/JurassicWorld.exe`。

分发或移动程序时保留整个 `Builds/JurassicWorld/` 目录，包括生成的数据目录、DLL 和其他运行文件，不能只复制 EXE。独立程序需要单独验证画面、输入和性能。

## 操作

- `W/S` 或上/下方向键：按当前行进方向前进/后退。
- `A/D` 或左/右方向键：持续向左/右转向并前进，相机平滑跟随。
- 按住鼠标右键并移动鼠标：转动视角。
- 指南针显示视线方向与角度（+Z 为北）；小地图固定北向上，黄色箭头标记玩家位置与行进方向。
- 左上角“画质”按钮或 `F10`：选择低、中、高档，选择会自动保存，首次启动默认中档。
- 画质菜单中的“测量 15 秒”：比较三档表现，结果追加到 `Application.persistentDataPath/quality-benchmark.csv`。纹理审计见 [Reports/TextureAudit.md](Reports/TextureAudit.md)。

当前 `DinosaurAgent` 的召唤开关关闭，`Space` 不触发召唤；旧召唤流程与信息卡代码仍保留，不作为当前启动验收要求。

## 3D 资产资源清单

以下按当前项目文件与来源记录整理。中文名称用于项目识别，英文名称保留物种名或原始资产名；不明确树种的模型使用通用名称。状态区分“使用中”“暂禁用”“归档”。来源页无法核实或缺少记录的信息标注为“待补充”，不将相似商品视作已确认来源。

### 恐龙与翼龙

| 中文名 | 英文名／原始资源名 | 来源／作者 | 许可 | 项目路径 | 状态 |
|---|---|---|---|---|---|
| 霸王龙 | Tyrannosaurus Rex / T-Rex | SketchFab [T-REX](https://skfb.ly/oM76u)    | CC BY | `Assets/Resources/Dinosaurs/TRex/` | 使用中 |
| 伶盗龙 / 迅猛龙 | Velociraptor | SketchFab [PBR Velociraptor (Animated)](https://skfb.ly/oHoUE) | CC BY | `Assets/Resources/Dinosaurs/Velociraptor/` | 使用中 |
| 剑龙（绿色） | Stegosaurus | [Unity Asset Store](https://assetstore.unity.com/packages/3d/characters/animals/pbr-animated-dinosaurs-256019)，Ferocious Industries | Standard Unity Asset Store EULA | 源资源 `Assets/FerociousIndustries/PBRDinosaurs/PBRStegasaurus/`；适配资源 `Assets/Dinosaurs/Stegosaurus/` | 暂禁用 |
| 胜王龙 | Rajasaurus | SketchFab [Animated Rajasaurus Free](https://skfb.ly/pFMxT) | 待补充 | `Assets/Resources/Dinosaurs/Rajasaurus/` | 暂禁用 |
| 阿根廷龙 | Argentinosaurus | SketchFab [Animated Argentinosaurus Free](https://skfb.ly/pEYQ6) | 待补充 | `Assets/Resources/Dinosaurs/Argentinosaurus/` | 暂禁用 |


### 植被

下表 Poly Haven 资源使用 [CC0](https://polyhaven.com/license)。准备后的树木 Prefab 位于 `Assets/VegetationDiversity/Prefabs/`，原始来源及下载记录位于 `Assets/ThirdParty/VegetationDiversity/`。

| 中文名 | 英文原名 | 来源／作者 | 许可 | 项目路径 | 状态 |
|---|---|---|---|---|---|
| 自然入门套件：树木、草地、灌木与天空盒 | Nature Starter Kit 2 | [Unity Asset Store](https://assetstore.unity.com/packages/3d/environments/nature-starter-kit-2-52977) | 待补充 | `Assets/NatureStarterKit2/` | 使用中，基础森林和地表资源 |
| 桌面树木包（树 1、5、10） | Desktop Trees Pack 60 (PC/Console) | [Fab](https://www.fab.com/listings/43b47da3-b552-4b6f-9deb-d638c26d3021)，Tree Master | [Fab Standard License](https://www.fab.com/eula) | `Assets/VegetationDiversity/Prefabs/FabTree_1.prefab`、`FabTree_5.prefab`、`FabTree_10.prefab` | 使用中 |
| 香蕉树 | Banana Tree | [Sketchfab](https://sketchfab.com/3d-models/banana-tree-5cb46e64d7fc40978c3d6798017eced1)，Garecra | CC BY | `Assets/VegetationDiversity/Prefabs/BananaTree.prefab` | 使用中，东南侧香蕉林及热带植被 |
| 岛屿树 01 | Island Tree 01 | [Poly Haven](https://polyhaven.com/a/island_tree_01)，Rico Cilliers、Rob Tuytel | CC0 | `Assets/VegetationDiversity/Prefabs/IslandTree01.prefab` | 使用中，中央森林 |
| 岛屿树 02 | Island Tree 02 | [Poly Haven](https://polyhaven.com/a/island_tree_02)，Rico Cilliers、Rob Tuytel | CC0 | `Assets/VegetationDiversity/Prefabs/IslandTree02.prefab` | 使用中，景观树与林地 |
| 岛屿树 03 | Island Tree 03 | [Poly Haven](https://polyhaven.com/a/island_tree_03)，Rico Cilliers、Rob Tuytel | CC0 | `Assets/VegetationDiversity/Prefabs/IslandTree03.prefab` | 使用中，中央森林 |
| 小树 02 | Tree Small 02 | [Poly Haven](https://polyhaven.com/a/tree_small_02)，Rico Cilliers | CC0 | `Assets/VegetationDiversity/Prefabs/TreeSmall02.prefab` | 使用中，中央森林 |
| 蓝花楹 | Jacaranda Tree | [Poly Haven](https://polyhaven.com/a/jacaranda_tree)，Rico Cilliers | CC0 | `Assets/VegetationDiversity/Prefabs/JacarandaTree.prefab` | 使用中，中央森林 |
| 冷杉 01 | Fir Tree 01 | [Poly Haven](https://polyhaven.com/a/fir_tree_01)，Rico Cilliers、Rob Tuytel | CC0 | `Assets/VegetationDiversity/Prefabs/FirTree01.prefab` | 使用中，采用 A 变体 |
| 发财树／瓜栗 | Pachira Aquatica 01 | [Poly Haven](https://polyhaven.com/a/pachira_aquatica_01)，Rico Cilliers、Rob Tuytel | CC0 | `Assets/VegetationDiversity/Prefabs/Pachira_B.prefab` | 使用中，林下 B 形态 |
| 蕨类 02 | Fern 02 | [Poly Haven](https://polyhaven.com/a/fern_02)，Rico Cilliers、Rob Tuytel | CC0 | 源资源 `Assets/ThirdParty/PolyHaven/Fern02/`；准备资源 `Assets/Vegetation/Ferns/` | 使用中，四种蕨丛形态 |

### 岩石与枯木

| 中文名 | 英文原名 | 来源／作者 | 许可 | 项目路径 | 状态 |
|---|---|---|---|---|---|
| 苔藓岩石组 01 | Rock Moss Set 01 | [Poly Haven](https://polyhaven.com/a/rock_moss_set_01) | CC0 | 源资源 `Assets/ThirdParty/PolyHaven/RockMossSet01/`；准备资源 `Assets/Vegetation/Rocks/` | 使用中，六种岩石形态 |
| 枯树干／倒木 02 | Dead Tree Trunk 02 | [Poly Haven](https://polyhaven.com/a/dead_tree_trunk_02)，Jenelle van Heerden、Rico Cilliers | CC0 | 源资源 `Assets/ThirdParty/PolyHaven/DeadTreeTrunk02/`；准备资源 `Assets/Vegetation/Deadwood/`；秋林适配 `Assets/Terrain/DeadVegetation/` | 使用中，包含秋林贴地倒木 |
| 树桩 01 | Tree Stump 01 | [Poly Haven](https://polyhaven.com/a/tree_stump_01) | CC0 | 源资源 `Assets/ThirdParty/PolyHaven/TreeStump01/`；准备资源 `Assets/Vegetation/Deadwood/DeadwoodStump01.prefab` | 使用中 |

### 小溪与地形

| 中文名 | 英文名 | 来源／作者 | 许可 | 项目路径 | 状态 |
|---|---|---|---|---|---|
| Crest 水体系统 | Crest Ocean System | [Wave Harmonic 官方仓库](https://github.com/wave-harmonic/crest) | MIT | `Assets/Crest/` | 使用中，溪流水面渲染 |
| 东侧至北侧小溪、溪岸与涟漪配置 | East-to-North Creek / Creek Bank / Ripple Spectrum | 项目自建，基于 Crest 和 Unity Terrain；见 `MainCreekIntegration` | 自建配置 | `Assets/CreekDemo/MainCreekTerrain.asset`、`CrestCreekWater.mat`、`CreekRippleSpectrum.asset`、`JungleCreekBank.terrainlayer` | 使用中 |
| 侏罗纪地形与地表生态 | Jurassic Terrain / Ground Ecology | 项目自建地形、布局与派生配置；基础纹理引用 Nature Starter Kit 2 和现有环境资源 | 自建配置 | `Assets/Terrain/JurassicTerrainData.asset`、`Assets/Terrain/Layers/`、`Assets/Terrain/GroundEcology/` | 使用中；主场景使用小溪整合后的地形副本 |

### 来源记录与署名

详细植被来源、作者、原始压缩包哈希和下载记录见 [Assets/VegetationDiversity/SOURCES.md](Assets/VegetationDiversity/SOURCES.md)。五种新增森林树的来源目录包含 `download-manifest.json`；模型统计见 `Tools/VegetationDiversity/five-tree-model-report.json`。

Sketchfab 页面本次未能读取，已知链接与 CC BY 信息沿用项目来源记录。署名保留 **Palm Trees by YadroGames, licensed under CC BY via Sketchfab** 和 **Banana Tree by Garecra, licensed under CC BY via Sketchfab** 及各自链接。香蕉树原页面记录其纹理来自 Quixel Bridge，纹理再分发条件仍待核实。其余标注“待补充”的资源需补齐原始下载页、作者及许可后再完成对外发布署名。

## 环境与路线维护

- 场景采用 **200m × 200m** Unity Terrain，主场景使用 `MainCreekTerrain.asset`；基础地形保留在 `JurassicTerrainData.asset`。地表包含草地、落叶、苔藓、根系及溪岸等多层配置。
- 植被通过 Terrain Trees、Terrain Details 和普通 Prefab 三种方式组织；准备后的模型包含 LOD，交互用网格保留在 `Assets/VegetationInteraction/`。
- 中央森林当前为 Stage One，使用五种新增树种；西北侧为秋林及过渡带，东南侧保留香蕉林。Stage Two 尚未执行。布局与维护详情见 [Assets/VegetationDiversity/README.md](Assets/VegetationDiversity/README.md)。
- 恐龙路线由 `MovementCorridor` 等脚本约束，并结合树干、岩石、枯木及恐龙之间的避障检查。当前召唤和路口切换关闭，沿固定路线观察。
- 相机依据 `WorldBounds` 限制可探索范围，通过地形采样保持眼高，并支持树干碰撞滑移。地面恐龙仍使用固定移动高度，脚锁接地基准在启动时采样；修改恐龙路线所在区域的高度前，需要同步检查移动和脚锁逻辑。

## 验收与编辑器菜单

启动后检查：Console 无 C# 编译错误或丢失资源错误；霸王龙、伶盗龙和剑龙的材质、蒙皮及待机／移动动画正常；相机控制平滑，地图与指南针正常；森林、地表和小溪可见，恐龙沿路线活动。

在编辑模式执行 **Jurassic World > Scene > Validate Scene**，或通过批处理入口 `JurassicWorld.Editor.JurassicProjectSetup.ValidateProject` 检查保存的主场景。此验证会打开 Main 场景，运行前处理未保存的场景改动；编辑态检查不能代替 Play 或 Windows 程序验收。

`Jurassic World` 顶层保留 **Open Main Scene**，其余工具分组如下：

- **Scene**：Bake Scene (Edit Mode)、Validate Scene、Build Windows Player 及剑龙整合。
- **Environment**：森林、植被、小溪、地表生态、恐龙路线和导航维护。
- **Assets**：准备植被资源、集成 Poly Haven 资源和重导入恐龙模型。
- **Diagnostics**：性能比较、密度检查、纹理审计和隔离倒木测试。
- **Acceptance**：生产烘焙对账、截图和动态细节验收。

打开或重建场景的菜单在 Play Mode 中禁用。旧植被原型、五树比较等工具已经退役，历史报告与备份仍保留。

## 当前边界

以 Unity Editor 演示为主要验证目标，Windows 程序通过现有菜单构建并另行验收。项目使用标准单相机和旧版 Input API，未接入 BCI、裸眼 3D 厂商 SDK、NavMesh 或多恐龙选择。当前运行模型采用已导入的 FBX／Prefab；模型缺失应检查 Console 和资源引用，不以占位模型替代。
