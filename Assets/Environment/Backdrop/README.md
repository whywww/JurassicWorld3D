# 雨林远景 — 阶段三

状态：阶段三已实施。阶段四溪岸湿润材质现已实施并等待手动查收，说明见 Assets/Environment/Creek/Prepared/Wetness/README.md。

远景在本目录独立管理，不归入 Atmosphere 的天空、雾和光束资源。

## 资源与场景

- Source/CoastalCliff01：用户下载的 FBX 和 4K 贴图，保持源文件。
- Prepared/Meshes：独立导出的 LOD3 岩壁、现有四类树种的静态网格副本、外围视觉地面。
- Prepared/Materials、Textures：远景专用雾化材质和 1K 岩壁漫反射副本。
- Prepared/RainforestBackdrop.prefab：无碰撞、无投影的远景组合。
- Shaders：独立空气透视着色器，避免远景被主场景距离雾完全抹掉。

主场景根节点 Rainforest Backdrop 的轻量方案包含 192 棵近侧真树、192 株林下植被、96 个烘焙树群代理、12 段错落岩壁以及外围非通行视觉地面。
树冠复用现有四类树种的几何与贴图，不复制交互、动画、碰撞或导航组件。
当前树木密度、树皮材质、森林布局和阶段二雾/光束保持原配置。

## 手动查收

本轮边界修正以玩家与恐龙活动范围的并集为保护区，树干从区外 3 米开始分布。
3–20 米布置 128 棵、20–45 米 64 棵真树，按角度分段错开，增强近侧遮挡。
192 株灌木及蕨类集中在近侧，水平包围范围不进入活动保护区。
远侧 96 个树群代理分为三个错位深度层，每个只有两个三角形，使用四张 2048×2048、各含 16 方向的树群图集。
图像由现有树木在编辑器中渲染，着色器按视角插值选择图像；没有运行时烘焙、拍摄或逐物体 Update。
图集采用透明裁剪及深度写入，仍有像素覆盖成本，不能仅以三角形少判断性能。
林下植被按八个空间分区和共享材质在编辑态合批；保留 192 个原始布置节点，关闭其独立渲染，由 Boundary Static Batches 绘制。
真树保持逐棵剔除及共享材质实例化，避免整片树木合并后扩大不可见几何的提交范围。
外围地面采样地形边沿高度，向外缓降；额外起伏仅为断续的小土包，最高 0.65 米，禁止整圈连续抬高形成土墙。树根使用同一高度函数。
地面深浅及土色变化由编辑态顶点色生成，仅外围地面材质启用，不增加纹理采样或运行时脚本。
Prepared/LightForest 保存图集、代理网格、材质及本轮独立的过渡地面，不改旧版加工地面。
Reports/Backdrop/LightForest 保存本轮场景与远景 Prefab 备份、分布数据、16 处边界前后截图及性能对照。
Reports/Backdrop/BoundaryTransition 保留上一轮 384 棵真树方案的旧记录，不作为本轮验收结果。
Reports/Backdrop/GreenWallFix 保存绿色土墙修正的独立场景、Prefab、被覆盖地面/合批网格/材质备份，以及用户截图位置和 16 处边界的对照图。
仅回退土墙修正时，先备份后续人工改动，再恢复该目录 Main.Before.unity、Backdrop.Before.prefab，并按 Resources 下的原相对路径恢复网格和材质，保留项目原有 meta。
只回退本轮修改时，同时恢复 LightForest 报告目录的 Main.Before.unity 和 Backdrop.Before.prefab 到原路径，保留各自 meta；先备份后续人工修改。

打开 Assets/Scenes/Main.unity 直接 Play，无需 Bake。先以中档检查：

1. 中央空地和溪边完整转动 360°，观察林间树冠和低对比岩壁是否连续自然。
2. 接近东、北及西侧通行边界，检查岩壁悬空、外围地面露缝。
3. 抬头、低头、左右转动，检查岩壁是否过高、过亮、像围墙或有重复轮廓。
4. 行走和 F10 切换画质时，检查突然消失、裁剪及主体轮廓变化。
5. 检查小地图、溪流水面和已有恐龙行为。

本轮尤其检查四边及四角的疏密过渡、近处树材质是否协调、树根贴地、地形边缘是否露缝，以及树林是否形成整齐的墙。
沿边界移动并斜向观察，额外检查代理侧边、视角切换、颜色接缝及重复树群；抬头确认天空与远山窗口仍存在。

## 距离与成本

RainforestBackdrop 进入 Play 时将主相机远裁剪设为 600 米，Backdrop 图层允许该距离；其他前景图层保持原 150 米对象剔除距离。
现有 Terrain 的树木和草地绘制参数仍由原画质控制器管理，不修改 TerrainData。
远景排除小地图和 Crest 平面反射，材质支持 GPU 实例化，不更新风动或实时阴影。

源岩壁 LOD0 为 461824 个三角形，LOD3 为 57726 个三角形。本阶段只把 LOD3 独立网格用于远景 Prefab，避免携带整份 FBX 的高精度层级。
原 4K 贴图保留，渲染副本导入尺寸为 1024；远景不加载法线和粗糙度贴图。
全部岩壁共享网格和材质，使用不同方位、比例和高度减少重复。

增量应用菜单：Jurassic World > Environment > Backdrop > Apply Stage 3 - Forest and Ridges。
首次应用轻量方案前，使用同目录 Bake Lightweight Forest Atlases 烘焙四套图集；独立边界批处理入口会自动完成此步骤。
此菜单重建外围树冠与过渡地面，保留已有岩壁变换及材质；人工微调树冠后请先备份。全场构建末尾可从已配置的 Prefab 恢复远景；不要用整场 Bake 应用本阶段。

## 报告与回退

Reports/Backdrop/StageThree 保存实施前场景、原相机参数、八方向和两处边界的编辑态前后截图、运行态截图与 Checks.txt。
Logs/backdrop-stage3.log 和 Logs/backdrop-stage3-smoke.log 保存 Unity 应用与基础检查记录。
短暂 Play 检查不替代用户手动查收，不包含固定路线性能验收或演示机测试。
LightForest/Performance.Before.json 与 Performance.After.json 记录相同 16 个静态视点、中档、1280×720 的短暂诊断。
为隔离本轮变化，性能相机只绘制 Backdrop，临时关闭其他相机、反射和相机脚本；CPU 探针额外调用一次 Camera.Render。
Profiler 计数含编辑器帧工作；visibleMeshTriangles 是保守视锥网格预算，不是 GPU 实测计数。
GPU 无有效样本时标记为不可用；不把零值解释为零开销，也不据此承诺完整场景帧率。

临时隐藏远景可在编辑态禁用 Rainforest Backdrop 根节点。
完全回退时先备份后续人工改动，再用 Main.Before.unity 的内容恢复主场景，保留 Main.unity.meta；原地形、导航和材质无需恢复。
原相机远裁剪为 150 米，每层剔除参数记录于 Camera.Before.json。

## 来源与许可

Coastal Cliff 01：https://polyhaven.com/a/coastal_cliff_01
作者：Rico Cilliers（Cleanup）、Rob Tuytel（Photography, Processing）。
许可证：CC0，https://polyhaven.com/license 。无强制署名，仍保留来源。
外围树冠沿用项目已有树木的来源与许可记录。

## 下一阶段

阶段四湿润材质可复用已有 RockMossSet01、溪岸地形层和植被贴图，无需新下载。
等待用户完成阶段三手动查收并明确同意后，才实施阶段四。
