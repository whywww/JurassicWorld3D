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

1. 安装 Unity Hub，并安装项目指定的 Unity **2022.3.62f3c1**。本机已有编辑器时，可在 Unity Hub 中定位对应版本的编辑器。请先匹配版本，避免直接升级项目。
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

所有 Emotiv 设置统一从项目根目录的 `emotiv.local.json` 读取，不再通过 Unity Inspector 修改。首次配置时，将 `emotiv.example.json` 复制为 `emotiv.local.json`，按下面字段填写；已有本地配置时直接编辑即可。

| JSON 字段 | 用途／示例默认值 |
|---|---|
| `clientId` | Cortex 应用的 Client ID |
| `clientSecret` | 同一应用的 Client Secret |
| `appName` | 应用名称；`JurassicWorld` |
| `profileName` | 已训练 profile 的准确名称；示例为 `Guest001` |
| `threshold` | power 接受阈值；`0.3`，范围 0–1 |
| `commandTimeout` | command 超时秒数；`1.0`，最小 `0.1` |

`emotiv.local.json` 已加入 `.gitignore`，不会被普通 `git add` / commit 收录；不要强制添加该文件。场景不保存上述字段。修改 JSON 后重新进入 Play，或重启 Windows Player，即可加载最新配置。文件缺失、JSON 无效、必填字符串为空或数值超出范围时，脑控不连接，WASD 仍可用。

`Smooth Explorer Camera` 的 **Emotiv Input** 引用已关联本相机的组件。若要关闭脑控，取消勾选 Emotiv Player Input 组件即可。

Editor 从项目根目录读取 `emotiv.local.json`。Windows Player 从 exe 同目录读取该文件；构建后需要自行复制本地配置，Unity 不会把项目根目录的配置打包进构建。分享构建时不附带该文件。重新 **Bake Scene** 不影响本地配置。

### 5. 启动并确认连接

保持耳机在线及 Launcher 登录，点击 **Play**，在 Launcher 接受首次访问请求，再查看 Unity Console 中的 `[EMOTIV]` 日志：

1. `Connecting to wss://localhost:6868`：开始连接；`Cortex state` 显示当前连接／授权阶段。
2. `Authorized; searching for headset / Virtual Brainwear`：授权完成；无设备时每 2 秒继续查询。
3. `Headset found` → `Session created; loading profile`：找到设备并建立 session。
4. `Profile loaded`：加载了指定 profile，开始订阅 `com/sys/dev`。
5. `Mental Command stream subscribed; control ready`：收到 `com` 订阅成功通知，可开始测试方向控制。

项目默认选择搜索结果中的第一个设备；测试时只保留目标耳机或目标 Virtual Brainwear。独立程序使用构建中保存的同一配置，并在运行前登录 Launcher；脑控参数目前通过 Editor 配置，没有独立程序内的配置界面。

修复验证（2026-10-09）：修正缓冲模式下订阅回执未触发导致输入始终为零的问题，改为监听 `DataStreamProcess.SubscribedOK`。临时 Unity 项目中 46 项回归检查通过，覆盖真实 SDK 回执及 command 事件到玩家输入的链路、两种缓冲模式、neutral、断流和 session 丢失；项目运行时代码编译通过。实际设备／模拟器到场景相机移动仍需重新 Play 验证。

### Virtual Brainwear 单个 push 测试

1. 在 Launcher 中创建 simulated device，保持设备 powered on / connected，打开模拟数据控制面板。
2. 在 EmotivBCI 中创建例如 `SimGuest` 的 profile，完成并保存所需训练，确认 push 已训练且激活。仅验证 push 时不要求同时训练 pull / left / right；完整四方向测试再补齐另外三个动作。参见 [官方 BCI 说明](https://emotiv.gitbook.io/cortex-api/bci)。
3. **停止 Unity Play，断开 EmotivBCI 的设备连接并完全退出 EmotivBCI，保留 Launcher。** 检查其他 Cortex 示例、BCI 工具或独立游戏实例也没有占用该设备的 profile。
4. 在 `emotiv.local.json` 中设置 `"profileName": "SimGuest"`，确认对应 Cortex 凭据已填写，再重新进入 Play。
5. 等待 `Profile loaded: SimGuest` 及 `Mental Command stream subscribed; control ready`。在 Launcher 的模拟控制中选择 push，将 power 设置为高于 Threshold（例如 0.5），并让模拟器持续输出；返回 Unity Game 窗口，不按任何方向键，观察相机前进。
6. 用 `com data: push, power: ...` 日志确认收到的实际指令。改为 neutral 或 power 低于 0.3 后应平滑停止；同方向按键优先，不会与脑控叠加加速。

如果 Console 显示 `the profile SimGuest is loaded by other apps`，表示已连接 Cortex / 建立 session，但 Unity 未成功加载 profile，当前代码因此还未订阅 `com`。`getCurrentProfile.loadedByThisApp=false` 指明 profile 来自其他应用；[官方 setupProfile 文档](https://emotiv.gitbook.io/cortex-api/bci/setupprofile) 明确禁止卸载另一应用加载的 profile。请先由占用者释放或退出，再重新 Play，不要把其他应用加载的 profile 当作 Unity 已成功加载。

session 创建时的 `SubscribeData: No Stream to subscribe` 是插件在本项目尚未添加订阅列表前的提示；随后应该在 profile 加载成功后订阅 `com/sys/dev`。`handleWarning: 142 / Headset discovering complete` 只表示设备扫描完成，不能作为脑控可用的标志，见 [官方 Warning Objects](https://emotiv.gitbook.io/cortex-api/warning-objects)。

### 6. 方向控制规则

| Mental Command | 对应键盘 | 行为 |
|---|---|---|
| `push` | W / ↑ | 按当前行进方向前进 |
| `pull` | S / ↓ | 后退 |
| `left` | A / ← | 左转并前进 |
| `right` | D / → | 右转并前进 |

power **达到** Threshold 才接受方向，移动和转向速度按限制在 `[0,1]` 的 power 缩放。主场景满速为 4 m/s、转向 60°/s，例如 power=0.5 的转弯目标为前进 2 m/s、转向 30°/s，移动仍经过原有平滑加减速。

按住任意 WASD 或方向键时，**键盘完全优先**，即使同时按相反键使输入抵消也不会退回脑控；松开后恢复当前有效脑控输入。鼠标右键视角操作保留。neutral、未知 action、低 power、断流达到 Command Timeout 或 session 丢失时清除脑控输入，玩家按原逻辑平滑减速；碰撞、地图边界和地形高度约束仍然生效。

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

验证记录（2026-10-09）：Unity 2022.3.62f3c1 编译及 51 项输入／场景／每帧碰撞与边界／重复初始化检查通过。真实 Headset、Virtual Brainwear、Launcher 应用授权和端到端脑控连接尚未验证，需要填写自己的应用凭据及训练 profile 后完成。

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
