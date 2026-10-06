# 3D 侏罗纪世界 MVP

Unity 版本：`2022.3.62f3c1`。场景包含霸王龙、胜王龙、伶盗龙、阿根廷龙和翼龙：四种地面恐龙在草地中活动，翼龙在空中巡游，所有物种都支持视野内召唤和独立信息卡。

## 启动

1. 使用 Unity Hub 或 `C:\Program Files\Unity 2022.3.62f3c1\Editor\Unity.exe` 打开本目录。
2. 等待 Package Manager 完成 glTFast 依赖恢复和脚本编译。
3. 打开 `Assets/Scenes/Main.unity`，点击 Play。

首次恢复依赖需要能够访问 GitHub。若 Package Manager 失败，请确认 `Packages/manifest.json` 中的 glTFast `v6.4.0` 地址可访问后执行 Resolve。

## 操作

- `W/S` 或上/下方向键：按当前行进方向前进/后退，不改变视角。
- `A/D` 或左/右方向键：持续向左/右转向并前进，相机平滑跟随。
- 按住鼠标右键并移动鼠标：缓慢转动视角。
- `Space`：召唤当前画面内的恐龙。
- 右上角指南针显示视线方向与角度（+Z 为北）；小地图固定北向上，显示整个 landscape，黄色箭头标记玩家位置与行进方向。地图在地形就绪后生成，地形变化或场景重载时刷新。
- 左上角“画质”按钮或 `F10`：选择低、中、高档。首次启动默认中档，选择会自动保存；高档保留原 Ultra 效果。
- 画质菜单中的“测量 15 秒”：在同一场景、分辨率和视角下分别测量三档，结果显示在左上角，并追加到 `Application.persistentDataPath/quality-benchmark.csv`。纹理资源清单见 `Reports/TextureAudit.md`。

恐龙会在静止与随机巡游之间切换。召唤后，视野中的恐龙会转向相机并沿固定路线穿过观察者，到达相机后方后恢复自由活动。信息卡会在召唤对象进入画面中央时淡入，离开主要视野后隐藏。

## 环境系统

### 资产依赖

- **Nature Starter Kit 2**（免费 Asset Store 包）是本项目的环境资产来源，包含树木 Prefab、草地 Details、天空盒材质。首次克隆仓库后**必须**从 Unity Asset Store 手动导入到 `Assets/NatureStarterKit2/`，否则 Bake 会自动跳过植被播种，仅生成光秃 Terrain。
- 导入完成后，`Assets/Editor/NatureStarterKitLocator.cs` 会自动定位所需 Prefab 与材质；若路径变更需在 Locator 中更新常量。

### Terrain 架构

- 场景使用 **200m × 200m** 的 Unity Terrain（替代原 Plane），资产持久化到 `Assets/Terrain/JurassicTerrain.asset`，Bake 幂等。
- **核心活动区**（中央约 100m × 100m，即恐龙与相机的活动范围）heightmap 固定为 0，视觉与物理上均完全平坦。
- **外围区域**允许轻微起伏（≤ 2m），用于遮挡地图边界并营造远景层次。
- 地表铺设单一 `TerrainLayer`（草地 Albedo + Normal），Tiling 约 4m，避免大面积纹理拉伸。

### 植被三通道

| 通道 | 用途 | 渲染方式 |
|------|------|----------|
| Terrain Trees (`TreePrototype`) | 中大型乔木、外围疏林 | Terrain 内置树渲染器，支持 LOD 与 Billboard |
| Terrain Details (`DetailPrototype`) | 草地、小灌木、地表覆盖 | GPU Instancing（`terrain.drawInstanced = true`） |
| 普通 Prefab | 大型岩石、构图植物、地标 | 常规 MeshRenderer，手工摆放 |

- 首次 Bake 时 `JurassicWorldSceneBuilder` 会**自动播种**（Seeded Random，可复现），仅在 `TerrainData.treeInstanceCount == 0 && detailPrototype 全部为空` 时触发。
- 播种后用户可通过 Unity **Terrain Tools > Paint Trees / Paint Details** 手动微调；再次 Bake **不会覆盖**已有实例，只重建恐龙、相机、光照等动态内容。
- 强制重播种：手动清空 TerrainData 的 Trees/Details 后再次 Bake。

### 运行期依赖（Y=0 硬编码）

以下运行期脚本假定恐龙与相机所在的核心活动区 Terrain heightmap = 0（World Y = 0）：

- `DinosaurAgent.MoveTowards` — 每帧将根节点 Y 强制钉在 `MovementHeight`（=0）。
- `SmoothExplorerCamera.Update` — 相机位置 Clamp ±47m 且 `y = 3`。
- `DinosaurFootLock.Start` — 一次性采样脚底 Y 作为地面基准。

因此**禁止**在核心活动区引入地形起伏。若后续需要真正的地形高度支持，必须同步改造这三个脚本，改为 `Terrain.SampleHeight()` 每帧或每 N 米重采样，具体迁移方案见架构备忘。

## 验收清单

- Console 没有 C# 编译错误或模型加载错误。
- 五种恐龙材质、蒙皮正常；地面恐龙有循环动作，翼龙持续播放飞行动画。
- 相机移动、转向跟随和右键视角控制平滑，且不会越过草地边界。
- 空格键只触发一次召唤；当前视野中的恐龙穿过视角后自动恢复随机活动。
- 信息卡只在召唤后且霸王龙位于画面中央、距离合适时出现。

可在菜单执行 `Jurassic World > Validate MVP`，或用批处理方法 `JurassicWorld.Editor.JurassicProjectSetup.ValidateProject` 检查模型、动画和场景。

## 当前边界

- 仅保证 Unity Editor 演示，不生成独立程序。
- 使用标准单相机和旧版 Input API；未接入 BCI、裸眼 3D 厂商 SDK、NavMesh 或多恐龙选择。
- GLB 或 FBX 加载失败会在 Console 和屏幕顶部明确报错，不使用占位模型冒充成功结果。

## 资产署名

`Rampaging T-Rex.glb` 来自 Sketchfab 的 [Animated Tyrannosaurus Rex Dinosaur Running Loop](https://sketchfab.com/3d-models/animated-tyrannosaurus-rex-dinosaur-running-loop-38007d947ae74dea83988cb0b08ee053)，按原页面标注的 CC BY 许可使用。发布或展示时请保留作者署名及原始资源链接。

新增模型来自用户提供的 `animated-rajasaurus-free.zip`、`pbr-velociraptor-animated.zip`、`animated-flying-pteradactal-dinosaur-loop.zip` 和 `animated-argentinosaurus-free.zip`。发布前请按各下载页许可补全作者署名和原始链接。
