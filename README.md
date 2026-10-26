# 3D 侏罗纪世界

基于 Unity Built-in Render Pipeline 的恐龙森林探索项目。Unity 版本为 **`2022.3.62f3c1`**（以 `ProjectSettings/ProjectVersion.txt` 为准）。

玩家以步行视角探索一座侏罗纪森林，观察多种恐龙在林间、溪流与植被中自由漫游和飞行。

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

1. 安装 Unity Hub，并安装项目指定的 Unity **2022.3.62f3c1**（中国版）。本机已有编辑器时，可在 Unity Hub 中定位对应版本的编辑器。请先匹配版本，避免直接升级项目。
2. 在 Unity Hub 中选择 **Add / 添加项目**，选择仓库根目录（包含 `Assets`、`Packages` 和 `ProjectSettings`）。
3. 打开项目，等待 Package Manager 恢复 `Packages/manifest.json` 中的依赖、Unity 导入资源并完成脚本编译。首次恢复需要联网访问项目配置的 Unity 中国版包源。
4. 打开 `Assets/Scenes/Main.unity`，或选择 **Jurassic World > Open Main Scene**。
5. 确认 Console 没有编译错误和丢失资源错误，点击 **Play**，在 Game 窗口中操作。

**普通启动直接使用保存好的 Main 场景，无需重新 Bake。** `Bake Scene (Edit Mode)` 会重建场景内容，用于主动维护或重建；执行前保存需要保留的场景改动。

### 构建与启动 Windows 程序

1. 在 Unity Hub 中为该编辑器安装 **Windows Build Support**。项目使用 Mono 脚本后端，无需 IL2CPP 工具链。
2. 退出 Play Mode 并保存场景改动，然后选择 **Jurassic World > Scene > Build Windows Player**，构建 `Assets/Scenes/Main.unity`。
3. 构建成功后运行 `Builds/JurassicWorld/JurassicWorld.exe`。

分发或移动程序时保留整个 `Builds/JurassicWorld/` 目录，包括生成的数据目录、DLL 和其他运行文件，不能只复制 EXE。独立程序需要单独验证画面、输入和性能。

## 操作

- `W/S` 或上/下方向键：按当前行进方向前进/后退。
- `A/D` 或左/右方向键：持续向左/右转向并前进，相机平滑跟随。
- 按住鼠标右键并移动鼠标：转动视角。
- 指南针显示视线方向与角度（+Z 为北）；小地图固定北向上，黄色箭头标记玩家位置与行进方向。
- 左上角“画质”按钮或 `F10`：选择低、中、高档，选择会自动保存，首次启动默认中档。
- `L`：显示／隐藏右下角 FPS（实际帧率），默认隐藏。

<details>
<summary>Emotiv EPOC X 脑机接口的配置与连接</summary>

## Emotiv Headset：配置与连接

本项目采用官方 [unity-plugin 的 Option 1：Cortex Service](https://github.com/Emotiv/unity-plugin#option-1-emotiv-cortex-service-desktop-only)，通过 `wss://localhost:6868` 连接本机服务。当前接入组件支持 Windows Editor / Windows Player；**不要定义 `USE_EMBEDDED_LIB`**，无需 Embedded Cortex Library 或 UniWebView。插件及桌面依赖已包含在项目中。

### 1. 在 Launcher 中连接耳机

安装并启动 [EMOTIV Launcher](https://www.emotiv.com/products/emotiv-launcher)，登录自己的 EmotivID。按耳机型号完成无线适配器或蓝牙连接，确认 Launcher 中设备在线。佩戴耳机，并在设备质量界面检查电极接触质量；调整传感器，确认信号适合训练和检测。运行 Unity 时保持 Launcher 和 Cortex Service 运行，并使用训练 profile 所属的同一个账号。

### 2. 准备 Cortex 应用凭据及授权

通过 [EMOTIV Developer](https://www.emotiv.com/pages/developer) 注册 Cortex 应用；在账号的 Cortex 应用管理页面获取 **Client ID** 和 **Client Secret**。这两个字段属于开发者应用，不能用 EmotivID 的登录账号和密码替代。详细流程见 [Cortex Getting Started](https://emotiv.gitbook.io/cortex-api)。

首次启动本项目的脑控连接时，插件会请求应用访问权限；在 Launcher 中接受对应应用的访问请求，随后插件自动执行授权。账号或应用权限、许可不足时，以 Launcher 提示和 Console 中的 Cortex 错误为准。参见 [官方 API 流程](https://emotiv.gitbook.io/cortex-api/overview-of-api-flow)。

### 3. 准备 Mental Command profile

连接耳机本身不会产生可用的方向指令。打开 **EmotivBCI**，在同一账号下选择或创建训练 profile，先训练 **neutral**，再训练 **push、pull、left、right**；确认四个 action 已训练并激活，保存 profile。在 EmotivBCI 中先测试动作能被识别并保存 profile，然后断开 EmotivBCI 的设备连接、释放 profile；测试时可完全退出 EmotivBCI，再运行 Unity。仅退出训练模式或切换窗口可能仍占用 profile。保持 EMOTIV Launcher / Cortex Service 运行。本项目只加载 profile 和接收数据，不提供训练界面。

记录 profile 的准确名称（包括大小写）。`Guest001` 只是项目默认名称，**不表示项目附带已经训练好的 profile**。拼错名称时，插件可能创建新的空 profile，导致只有 neutral 或没有有效方向。训练方法见 [训练第一个 command](https://emotiv.gitbook.io/emotivbci/mental-commands/training-your-first-command) 和 [训练 profiles](https://emotiv.gitbook.io/emotivbci/training-profiles/your-training-profiles)。

### 4. 本地 JSON 配置

所有 Emotiv 设置统一从项目根目录的 `emotiv.local.json` 读取。首次配置时，将 `emotiv.example.json` 复制为 `emotiv.local.json`，按下面字段填写；已有本地配置时直接编辑即可。

| JSON 字段 | 用途／示例默认值 |
|---|---|
| `clientId` | Cortex 应用的 Client ID |
| `clientSecret` | 同一应用的 Client Secret |
| `appName` | 应用名称；`JurassicWorld` |
| `profileName` | 已训练 profile 的准确名称；示例为 `Guest001` |
| `threshold` | power 接受阈值；`0.3`，范围 0–1 |
| `commandTimeout` | command 超时秒数；`1.0`，最小 `0.1` |

`emotiv.local.json` 已加入 `.gitignore`，不会被普通 `git add` / commit 收录；不要强制添加该文件。场景不保存上述字段。修改 JSON 后重新进入 Play，或重启 Windows Player，即可加载最新配置。文件缺失、JSON 无效、必填字符串为空或数值超出范围时，脑控不连接，WASD 仍可用。

`Smooth Explorer Camera` 的 **Emotiv Input** 引用已关联本相机的组件。若要关闭脑控，取消勾选 Unity Inspector 的 Emotiv Player Input 组件即可。

### 5. 启动并确认连接

保持耳机在线及 Launcher 登录，点击 **Play**，在 Launcher 接受首次访问请求，再查看 Unity Console 中的 `[EMOTIV]` 日志：

1. `Connecting to wss://localhost:6868`：开始连接；`Cortex state` 显示当前连接／授权阶段。
2. `Authorized; searching for headset / Virtual Brainwear`：授权完成；无设备时每 2 秒继续查询。
3. `Headset found` → `Session created; loading profile`：找到设备并建立 session。
4. `Profile loaded`：加载了指定 profile，开始订阅 `com/sys/dev`。
5. `Mental Command stream subscribed; control ready`：收到 `com` 订阅成功通知，可开始测试方向控制。

项目默认选择搜索结果中的第一个设备；测试时只保留目标耳机或目标 Virtual Brainwear。独立程序使用构建中保存的同一配置，并在运行前登录 Launcher；脑控参数目前通过 Editor 配置，没有独立程序内的配置界面。

### Virtual Brainwear 虚拟控制测试

1. 在 Launcher 中创建 simulated device，保持设备 powered on / connected，打开模拟数据控制面板。
2. 在 EmotivBCI 中创建例如 `SimGuest` 的 profile，不需要完成训练。参见 [官方 BCI 说明](https://emotiv.gitbook.io/cortex-api/bci)。
3. 断开 EmotivBCI 的设备连接并完全退出 EmotivBCI，保留 Launcher。检查其他 Cortex 示例、BCI 工具或独立游戏实例也没有占用该设备的 profile。
4. 在 `emotiv.local.json` 中设置 `"profileName": "SimGuest"`，确认对应 Cortex 凭据已填写，再重新进入 Play。
5. 等待 `Profile loaded: SimGuest` 及 `Mental Command stream subscribed; control ready`。在 Launcher 的模拟控制中选择 push/pull/left/right，将 power 设置为高于 Threshold，并让模拟器持续输出；返回 Unity Game 窗口，不按任何方向键，观察相机前进。
6. 用 `com data: push, power: ...` 日志确认收到的实际指令。改为 neutral 或 power 低于 0.3 后应平滑切换为正常速度的 50% 自动前进；同方向按键优先，不会与脑控叠加加速。

### 6. 方向控制规则

| Mental Command | 对应键盘 | 行为 |
|---|---|---|
| `push` | W / ↑ | 按当前行进方向前进 |
| `pull` | S / ↓ | 后退 |
| `left` | A / ← | 左转并前进 |
| `right` | D / → | 右转并前进 |

power **达到** Threshold 才接受方向，移动和转向速度按限制在 `[0,1]` 的 power 缩放。主场景满速为 4 m/s、转向 60°/s，例如 power=0.5 的转弯目标为前进 2 m/s、转向 30°/s，移动仍经过原有平滑加减速。

连接、授权、session、profile 和 `com` 订阅均正常时，若没有有效方向指令（未训练、Push 被禁用、neutral、低于阈值、空指令、未知指令、无效 power 或指令超时），自动沿当前行进方向以正常速度的 **50%** 前进。该目标速度随场景 `Move Speed` 缩放，保留平滑加减速；收到有效方向指令后恢复上述映射。未连接、断连、流停止或组件禁用时输出零。此行为仅适用于 Emotiv，MindWave 保持原有逻辑。

按住任意 WASD 或方向键时，**键盘完全优先**，即使同时按相反键使输入抵消也不会退回脑控；松开后恢复当前有效脑控输入。鼠标右键视角操作保留。

### 常见问题

| 现象 | 检查步骤 |
|---|---|
| 配置缺失提示 | 在非 Play 状态填写四个文本字段；重新进入 Play |
| 一直 Connecting / Service_connecting | 确认 Launcher 已启动、Cortex Service 正常、本机端口 6868 可用；检查插件的连接／TLS 错误；参考 [Cortex 连接说明](https://emotiv.gitbook.io/cortex-api/connecting-to-the-cortex-api) |
| Login_notYet / Authorize_failed | 登录 Launcher，确认应用凭据成对正确，接受访问请求，查看 Cortex 的许可或权限错误 |
| Authorized 后没有 Headset found | 确认设备在线及适配器／蓝牙连接；先在 Launcher 确认设备，再重新 Play |
| profile is loaded by other apps | 保存训练，断开或完全退出 EmotivBCI 及其他占用应用；保留 Launcher，然后重新 Play |
| 加载 profile 失败／只有 neutral | 核对账号和准确 profile 名称、设备兼容性、四个 action 是否已训练并激活；不要误用自动创建的空 profile |
| 持续收到 push 但未显示 control ready | 更新到修复后的 EmotivPlayerInput，停止并重新 Play；使用缓冲模式无关的 Cortex 订阅回执检测就绪 |
| 有 command 但不移动 | 查看插件 `com data` 日志中的 action 和 power；确认 power 达到 Threshold、没有按方向键、组件启用且引用正确 |
| 断连后没有恢复 | 输入会自动清除；确认耳机和 Launcher 恢复在线，必要时停止 Play 后重新进入，重新完成授权／session 流程 |

开发验证入口：**Jurassic World > Tests > Emotiv Input Acceptance**，检查四方向映射、阈值、速度缩放、键盘优先、超时和场景组件引用。该检查不代替真实耳机验证；完整验收需填写凭据后，用真实 Headset 或 Virtual Brainwear 走完上述连接日志并实际测试操作。
</details>

## 3D 资产资源清单

### 恐龙与翼龙

| 中文名 | 英文名 | 来源／作者 | 许可 | 项目路径 | 状态 |
|---|---|---|---|---|---|
| 霸王龙 | Tyrannosaurus Rex | [SketchFab](https://skfb.ly/oM76u)    | CC BY | `Assets/Dinosaurs/TRex/`（`Source` 原始，`Prepared` 适配） | 使用中 |
| 伶盗龙 / 迅猛龙 | Velociraptor | [SketchFab](https://skfb.ly/oHoUE) | CC BY | `Assets/Dinosaurs/Velociraptor/`（`Source/Sketchfab` 原始，`Prepared` 适配；`Source/FerociousIndustries` UAS 原包） | 使用中 |
| 厚头龙（红色） | Pachycephalosaurus | [Unity Asset Store](https://assetstore.unity.com/packages/3d/characters/animals/pbr-animated-dinosaurs-256019), Ferocious Industries | Standard Unity Asset Store EULA | `Assets/Dinosaurs/Pachycephalosaurus/` （`Source/FerociousIndustries` 原始，`Prepared` 适配） | 使用中 |
| 包鲁鳄 | Baurusuchus | Fab [Animated Baurusuchus](https://www.fab.com/listings/69ac72dc-187f-4db7-ad99-c524023113b6) | [Fab Standard License](https://www.fab.com/eula) | `Assets/Dinosaurs/Baurusuchus/` （`Source` 原始，`Prepared` 适配） | 使用中 |
| 蜥结龙 | Sauropelta | Fab [Animated Sauropelta](https://www.fab.com/listings/de1b8487-8dbd-4105-80be-79bd64a3905e) | [Fab Standard License](https://www.fab.com/eula) | `Assets/Dinosaurs/Sauropelta/`（`Source` 原始，`Prepared` 适配） | 使用中 |
| 原角龙 | Protoceratops | [SketchFab](https://skfb.ly/6xDZt) | [SketchFab Standard License](https://sketchfab.com/licenses) | `Assets/Dinosaurs/Protoceratops/`（`Source` 原始，`Prepared` 适配） | 使用中；2 米 |
| 翼龙 | Pteradactal | [SketchFab](https://skfb.ly/o9nTY) | CC BY | `Assets/Dinosaurs/Pteradactal/`（`Source` 原始，`Prepared` 适配） | 使用中；5 米翼展，6–10 米低空飞行 |
| 剑龙（绿色） | Stegosaurus | [Unity Asset Store](https://assetstore.unity.com/packages/3d/characters/animals/pbr-animated-dinosaurs-256019)，Ferocious Industries | Standard Unity Asset Store EULA | `Assets/Dinosaurs/Stegosaurus/` （`Source/FerociousIndustries` 原始，`Prepared` 适配） | 暂禁用 |
| 胜王龙 | Rajasaurus | [SketchFab](https://skfb.ly/pFMxT) | CC BY-NC | `Assets/Dinosaurs/Rajasaurus/`（`Source` 原始，`Prepared` 适配） | 暂禁用 |
| 阿根廷龙 | Argentinosaurus | [SketchFab](https://skfb.ly/pEYQ6) | CC BY | `Assets/Dinosaurs/Argentinosaurus/`（`Source` 原始，`Prepared` 适配） | 暂禁用 |

### 植被

环境资产统一位于 `Assets/Environment/`，按 `<类别>/<资产集>/{Source,Prepared}` 组织：`Source/` 保留厂商原始包，`Prepared/` 放项目使用的 prefab、材质、网格与贴图。完整索引见 [Assets/Environment/README.md](Assets/Environment/README.md)。

| 中文名 | 英文原名 | 来源 | 许可 | 项目路径 | 状态 |
|---|---|---|---|---|---|
| 自然入门套件：树木、草地、灌木与天空盒 | Nature Starter Kit 2 | [Unity Asset Store](https://assetstore.unity.com/packages/3d/environments/nature-starter-kit-2-52977) | Standard Unity Asset Store EULA | `Assets/ThirdParty/NatureStarterKit2/` | 使用中，基础森林和地表资源 |
| 桌面树木包（树 1、5、10） | Desktop Trees Pack 60 (PC/Console) | [Fab](https://www.fab.com/listings/43b47da3-b552-4b6f-9deb-d638c26d3021) | [Fab Standard License](https://www.fab.com/eula) | `Assets/Environment/Vegetation/Trees/Prepared/Prefabs/FabTree_1.prefab`、`FabTree_5.prefab`、`FabTree_10.prefab` | 使用中 |
| 香蕉树 | Banana Tree | [Sketchfab](https://sketchfab.com/3d-models/banana-tree-5cb46e64d7fc40978c3d6798017eced1) | CC BY | `Assets/Environment/Vegetation/Trees/Prepared/Prefabs/BananaTree.prefab` | 使用中，东南侧香蕉林及热带植被 |
| 岛屿树 01 | Island Tree 01 | [Poly Haven](https://polyhaven.com/a/island_tree_01) | CC0 | `Assets/Environment/Vegetation/Trees/Prepared/Prefabs/IslandTree01.prefab` | 使用中，中央森林 |
| 岛屿树 02 | Island Tree 02 | [Poly Haven](https://polyhaven.com/a/island_tree_02) | CC0 | `Assets/Environment/Vegetation/Trees/Prepared/Prefabs/IslandTree02.prefab` | 使用中，景观树与林地 |
| 岛屿树 03 | Island Tree 03 | [Poly Haven](https://polyhaven.com/a/island_tree_03) | CC0 | `Assets/Environment/Vegetation/Trees/Prepared/Prefabs/IslandTree03.prefab` | 使用中，中央森林 |
| 小树 02 | Tree Small 02 | [Poly Haven](https://polyhaven.com/a/tree_small_02) | CC0 | `Assets/Environment/Vegetation/Trees/Prepared/Prefabs/TreeSmall02.prefab` | 使用中，中央森林 |
| 蓝花楹 | Jacaranda Tree | [Poly Haven](https://polyhaven.com/a/jacaranda_tree) | CC0 | `Assets/Environment/Vegetation/Trees/Prepared/Prefabs/JacarandaTree.prefab` | 使用中，中央森林 |
| 冷杉 01 | Fir Tree 01 | [Poly Haven](https://polyhaven.com/a/fir_tree_01) | CC0 | `Assets/Environment/Vegetation/Trees/Prepared/Prefabs/FirTree01.prefab` | 使用中，采用 A 变体 |
| 发财树／瓜栗 | Pachira Aquatica 01 | [Poly Haven](https://polyhaven.com/a/pachira_aquatica_01) | CC0 | `Assets/Environment/Vegetation/Trees/Prepared/Prefabs/Pachira_B.prefab` | 使用中，林下 B 形态 |
| 蕨类 02 | Fern 02 | [Poly Haven](https://polyhaven.com/a/fern_02) | CC0 | 源资源 `Assets/Environment/Vegetation/Fern02/Source/`；准备资源 `Assets/Environment/Vegetation/Fern02/Prepared/Prefabs/` | 使用中，四种蕨丛形态 |

### 岩石与枯木

| 中文名 | 英文原名 | 来源／作者 | 许可 | 项目路径 | 状态 |
|---|---|---|---|---|---|
| 苔藓岩石组 01 | Rock Moss Set 01 | [Poly Haven](https://polyhaven.com/a/rock_moss_set_01) | CC0 | 源资源 `Assets/Environment/RocksAndDeadwood/RockMossSet01/Source/`；准备资源 `Assets/Environment/RocksAndDeadwood/RockMossSet01/Prepared/Prefabs/` | 使用中，六种岩石形态 |
| 枯树干／倒木 02 | Dead Tree Trunk 02 | [Poly Haven](https://polyhaven.com/a/dead_tree_trunk_02) | CC0 | 源资源 `Assets/Environment/RocksAndDeadwood/Deadwood/Source/DeadTreeTrunk02/`；准备资源 `Assets/Environment/RocksAndDeadwood/Deadwood/Prepared/Prefabs/`；秋林适配 `Assets/Environment/RocksAndDeadwood/AutumnGroundedLog/Prepared/Prefabs/` | 使用中，包含秋林贴地倒木 |
| 树桩 01 | Tree Stump 01 | [Poly Haven](https://polyhaven.com/a/tree_stump_01) | CC0 | 源资源 `Assets/Environment/RocksAndDeadwood/Deadwood/Source/TreeStump01/`；准备资源 `Assets/Environment/RocksAndDeadwood/Deadwood/Prepared/Prefabs/DeadwoodStump01.prefab` | 使用中 |

### 小溪与地形

| 中文名 | 英文名 | 来源／作者 | 许可 | 项目路径 | 状态 |
|---|---|---|---|---|---|
| Crest 水体系统 | Crest Ocean System | [Wave Harmonic 官方仓库](https://github.com/wave-harmonic/crest) | MIT | `Assets/Crest/` | 使用中，溪流水面渲染 |
| 东侧至北侧小溪、溪岸与涟漪配置 | East-to-North Creek / Creek Bank / Ripple Spectrum | 项目自建，基于 Crest 和 Unity Terrain；见 `MainCreekIntegration` | 自建配置 | `Assets/Environment/Creek/Prepared/`（`MainCreekTerrain.asset`、`CrestCreekWater.mat`、`CreekRippleSpectrum.asset`、`JungleCreekBank.terrainlayer`） | 使用中；`MainCreekTerrain.asset` 为历史生成物，当前场景不引用 |
| 侏罗纪地形与地表生态 | Jurassic Terrain / Ground Ecology | 项目自建地形、布局与派生配置；基础纹理引用 Nature Starter Kit 2 和现有环境资源 | 自建配置 | `Assets/Environment/Terrain/Prepared/`（`JurassicTerrainData.asset`、`MainInteractionTerrain.asset`、`Layers/`、`GroundEcology/`） | 使用中；主场景实际使用 `MainInteractionTerrain.asset` |

## 环境与路线维护

- 场景为 **200m × 200m** Unity Terrain。主场景引用 `MainInteractionTerrain.asset`（交互烘焙生成）；`JurassicTerrainData.asset` 是基础地形，`MainCreekTerrain.asset` 为历史生成物、当前未引用。地表由草地、落叶、苔藓、根系和溪岸等多层组成。
- 植被通过 Terrain Trees、Terrain Details 和普通 Prefab 组织；模型含 LOD，交互用网格位于 `Assets/Environment/Vegetation/Interaction/`。
- 当前启用的地面恐龙均为自由漫游，翼龙独立飞行；`MovementCorridor` 保留用于植被净空、调试标注和路线验收。移动会避让树干、岩石和其他恐龙，召唤与路口切换关闭。
- 相机由 `WorldBounds` 限制探索范围，并采样地形保持眼高；玩家可穿过树木，树木只参与恐龙避障。地面恐龙使用固定移动高度，脚锁以该高度为接地基准；改动活动区地形高度时需同步检查移动和脚锁。

## 验收与菜单

启动后检查：Console 无 C# 编译错误或丢失资源错误；霸王龙、伶盗龙、厚头龙、蜥结龙、包鲁鳄、原角龙的材质、蒙皮及待机／移动动画正常，翼龙飞行动作正常；相机控制平滑，地图与指南针正常；森林、地表和小溪可见；地面恐龙均自由漫游，小型恐龙靠近霸王龙时主动撤离。

在编辑模式执行 **Jurassic World > Scene > Validate Scene**，或通过批处理入口 `JurassicWorld.Editor.JurassicProjectSetup.ValidateProject` 检查保存的主场景。此验证会打开 Main 场景，运行前处理未保存的场景改动；编辑态检查不能代替 Play 或 Windows 程序验收。

`Jurassic World` 顶层保留 **Open Main Scene**，其余工具分组如下：

- **Scene**：Bake Scene (Edit Mode)、Validate Scene、Build Windows Player 及剑龙整合。
- **Environment**：森林、植被、小溪、地表生态、恐龙路线和导航维护。
- **Assets**：准备植被资源、集成 Poly Haven 资源和重导入恐龙模型。
- **Diagnostics**：性能比较、密度检查、纹理审计。
- **Acceptance**：生产烘焙对账、截图和动态细节验收。

打开或重建场景的菜单在 Play Mode 中禁用。`Prepare Biome Vegetation` 与 `Prepare Five Forest Tree Species` 仍作为资源准备入口保留。

## 避让规则

- 地面通行顺序：**霸王龙＞(阿根廷龙＞剑龙＞胜王龙)＞蜥结龙＞厚头龙＞包鲁鳄＞伶盗龙＞原角龙**。召唤和玩家交互优先，双方均在交互时按物种排序；返程恢复普通顺序，会车期间锁定先行权。
- 低优先级者主动侧移或后撤，窄路先退到宽处；先行者等待 **1.2 秒**后可绕行。解围后恢复原目标，受阻时持续重试，保留导航与防穿透检查。
- 小恐龙与持有先行权的霸王龙水平距离小于 **15 米**时撤离，达到 **20 米**解除避让，闲站时同样生效。飞行单位不参与地面会车。
- `F4` 调试窗可查看排名、交互优先状态、阻挡者和会车持续时间。

## 霸王龙互动动画流程

霸王龙只把硬树干、样板区硬枝和大型岩石当作移动与视线障碍；柔性叶片、草、蕨类、灌木、小石、低矮倒木和树桩不阻挡。大型岩石按 `Environment Props/Rock_Large_*` 与 `Understory Pass/Rocks/Rock_*_Hero` 识别。

| 阶段 | 玩家方向与角度范围 | 动画顺序 | 播放方式／条件 |
|---|---|---|---|
| **1. 发现与观察** | 正前方：偏角 **≤20°** | `idle investigate` → `idle investigae out` → `Big roar step` | 玩家在 **32 米内**持续可见 **0.3 秒**后触发；各播一次，约 **9.77 秒** |
|  | 左／右侧：对应侧偏角 **>20°且≤50°** | `look left/right` → `roar left/right` | 玩家在 **32 米内**持续可见 **0.3 秒**后触发；按发现侧播放，各一次，约 **8.2 秒** |
| **2. 跟随** | 从正前方发现：初始偏角 **≤20°** | `walk head low` 循环 | 发现动作结束后开始；**>10 米**继续跟随，**≤10 米**停步；退到 **>15 米**且目标可见、路径可行时恢复；累计最多 **30 秒** |
|  | 从左／右侧发现：初始偏角 **>20°且≤50°** | `walk head low` 循环 | 吼叫结束后 **0.4 秒**接管，按支撑脚平滑接步；转向最多 **2 秒**，大角度交给导航转向，不固定插入 90° 转身。受阻时冻结进度，退出时清理 |
| **3. 停步互动／结束** | 所有方向 | `ROAR` → `look forward 2` → 随机 `bite close left/right` → `look forward 2` | **≤10 米**请求停步；连续失去视线 **3 秒**、跟随满 **30 秒**或掉头空间不足时结束。完整动作约 **10.5～11 秒**；两段张望各取前 **2 秒** |
| **4. 掉头与返回** | 所有方向 | `turn 180 right` → `walk slow loop` → 恢复自由漫游 | 本轮结束或无法恢复跟随时掉头。掉头约 **2.7 秒**、转 **170°**；沿记录路径原路返回，最多 **3 秒**，提前到起点即结束。转身不检查静态障碍，只有其它恐龙能中断；转后前 **2 秒**不检查障碍和 NavMesh，之后恢复正常碰撞 |

**保持区间**：停步后 **10～15 米**不会自动恢复；需退到 **15 米外**且目标可见、路径可行。`ROAR` 或咬击可播完当前动作后恢复，两段张望可平滑中断；确定结束时先完成收尾动作再返回。

**受阻与重试**：阶段 2 连续受阻 **2 秒**结束本轮，其它恐龙让行不计入；空间不足时每 **0.5 秒**搜索 **4／8／12／16 米**的互动点，结束阶段跳过不安全动作和大角度掉头。返回优先走记录路径，暂无安全出口时每 **2 秒**重试，找到可行路线后再转身；恢复漫游后 **10 秒**内不再发现玩家。跟随计时包含停步与绕行，返回只计实际行走。


## 当前边界

项目以 **Windows 平台 + Unity Editor** 演示为主要验证目标，Windows 独立程序通过现有菜单构建并另行验收；未验证 macOS、Linux、移动端或主机。

- **玩法**：发布入口为主场景 `Assets/Scenes/Main.unity`，单场景、单人森林观察演示。包含键鼠漫游、HUD／小地图／指南针、画质档位、恐龙自由漫游与避让、霸王龙互动。`TRexFrontInteractionTest` 仅为开发调试场景，不属于发布内容。
- **输入**：使用标准单相机和键鼠。Emotiv 脑控为可选输入，仅在 Windows 下通过本机蓝牙 Cortex 服务连接，需要 Launcher、应用凭据和已训练的 Mental Command profile；不提供训练或程序内配置界面，配置失败时键鼠仍可用。
- **导航与场景**：恐龙导航使用烘焙好的 NavMesh 数据加项目内避让／通行规则，不在运行期重新烘焙；场景内容由编辑态 Bake 菜单维护，不做运行期程序化生成。可探索范围为固定的 200m × 200m Terrain。
- **恐龙**：主场景当前启用霸王龙、伶盗龙、厚头龙、蜥结龙、包鲁鳄、原角龙与翼龙。召唤关闭。
- **资产**：运行模型使用已导入的 FBX／Prefab 和准备后的资源；模型缺失应检查 Console 和资源引用，不以占位模型替代。
