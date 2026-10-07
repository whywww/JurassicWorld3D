# MVP 验证记录

## 自动检查

- 目标编辑器：Unity 2022.3.62f3c1
- 检查项：C# 编译、主场景、两份 GLB、三份 FBX、动画片段、Renderer 与运行时 Animation 组件
- 合格标志：Editor 日志包含 `JURASSIC_MVP_VALIDATION_OK`

## 人工 Play Mode 检查

1. 检查 W/S 前进后退时视角不变，A/D 转向前进时相机平滑跟随，以及右键拖动和场地边界。
2. 观察地面恐龙至少一次 Idle → RandomWalk → Idle，并确认翼龙保持飞行高度。
3. 让任意恐龙保持在画面内并按空格，确认其朝相机移动、穿过视角并到达相机后方。
4. 确认信息卡在画面中央淡入，恐龙跑出主要视野后淡出，召唤随后自动结束。
5. 确认恐龙结束召唤后平滑转向新的随机目标，继续 Idle / RandomWalk 循环。
6. 查看 Console，确认没有模型、材质、蒙皮或动画错误。

## 失败边界

- 自动检查可证明模型、动画、渲染器和贴图绑定存在；最终尺寸、动画观感和信息卡位置仍需通过 Play Mode 观察确认。
- 当前环境未接 BCI 和裸眼 3D 输出，因此不能对这两部分形成验证结论。

## Terrain 环境验证

### 自动断言（`JurassicProjectSetup.ValidateProject`）

- 场景存在 `Terrain` 组件且 `terrainData != null`，否则抛 `InvalidDataException`。
- `terrainData.terrainLayers.Length > 0`，确认已配置草地纹理层。
- `terrainData.size.x ≥ 100` 且 `size.z ≥ 100`，确认地图尺寸不低于 100m×100m（实际设计 200m×200m）。
- **树数量阈值（与回滚开关联动，不再是 warn-only）**：`ForestSeeder.ForestEnabled == true` 时要求 `treeInstanceCount ≥ 400`（中央 ~161 棵 + 外围森林 ~1500–2200 棵）；回滚态（`EnableForest = false`，森林段已被精确剥离）时要求 `≥ 100`。不足即抛 `InvalidDataException`，报错文本包含实际值、阈值与 `ForestEnabled` 状态。
- **森林追加段非空**：`treePrototypes.Length > ForestSeeder.CentralTreePrototypeCount`（同源常量，不再硬编码 `4`）时必须存在 `prototypeIndex ≥` 该基线的实例，否则告警（追加了原型却未写回实例属异常状态）。遍历为单次 `foreach`，不使用 LINQ 全量物化。
- **核心区森林树零泄漏（不变量机器验证）**：核心矩形 `x ∈ [-60, 60]`、`z ∈ [-50, 82]`（直接引用 `ForestSeeder.CoreMinX / CoreMaxX / CoreMinZ / CoreMaxZ`，播种侧与校验侧同源）内 `prototypeIndex ≥ CentralTreePrototypeCount` 的实例数**必须为 0**，违反即抛 `InvalidDataException`。该断言用于抓住林缘 `wobble` 穿透之类的回归。
- **Bake 失败即中止，不覆盖场景**：`JurassicWorldSceneBuilder.BuildTerrain()` 返回 `false`（Nature Starter Kit 2 未导入等前置依赖缺失）时，`Build()` 抛 `InvalidOperationException`，`BakeScene` 后续的 `MarkSceneDirty / SaveScene / SaveAssets / Refresh` 全部跳过 —— 上一次可用的 `Main.unity` 与 `JurassicTerrainData.asset` 不会被半成品场景覆盖。
- `terrain.drawInstanced == false` 时告警：提示 Details 的 GPU Instancing 未生效。

### 人工 Play Mode 检查

1. **地形平坦性**：在中央平坦活动区行走一圈（玩家可达范围见 ③ `WorldBounds.PlayerExplorationBounds`，
   西/东/南已外扩到平坦区外 8m，故相机 Y = `Terrain.SampleHeight + eyeHeight`、不再硬编码 1.7m；
   平坦区内地面恒平），目视确认恐龙脚部无穿地、无悬空，落脚锁定表现正常。
2. **植被渲染**：确认树木与草地颜色自然，**非粉色/紫色**（粉色为 Unity shader 编译失败标志，提示 Nature Starter Kit 2 shader 与当前渲染管线不兼容）。
3. **Wind Zone 生效**：静置观察树冠约 5 秒，确认有轻微自然摆动，而非完全静止（验证 `WindZone` 组件已挂载并影响 Tree Creator shader）。
4. **Skybox 自然无接缝**：抬头观察天空，确认显示渐变蓝天/云层（**非默认纯灰色或纯黑色**），且**画面中不存在竖直接缝线、云不被直线切断**（验证 `Assets/Materials/NaturalSkybox.mat`（Skybox/Panoramic 经纬度全景 + `NatureSkyPanorama.tga`）已绑定到 `RenderSettings.skybox`；旧 NSk2 cubemap 天空 `skybox.mat` 存在面缝伪影，已不再使用）。
5. **外围起伏**：将相机推至地图边缘（±90m 附近），确认外围地形有轻微起伏但恐龙不会越界，边缘被远景树木自然遮挡。
6. **Bake 幂等性**：连续执行两次 `Jurassic World > Scene > Bake Scene`，确认第二次运行后树木与草地实例数量**不变**（不重复播种），恐龙位置、相机、光照正常重建。第二次的 Editor 日志应出现 `[ForestSeeder] 结果与现有实例一致，跳过写回`（固定种子下逐字段比对命中，跳过 `treePrototypes` 赋值与 `SetTreeInstances`，避免重写 ~3MB 二进制资产并重烘树 billboard）。
7. **森林密度量化对比**：`[ForestSeeder] 森林播种完成` 日志含 `attemptsUsed` / 接受率 / 是否触顶 `forestCapacity` / 物种直方图 / 径向密度直方图（按到核心矩形的原始距离分环带 `[5,10) [10,15) [15,20) [20,25) [25+)`）。执行「遮挡达标即止」密度迭代时，以该直方图做改参前后的逐带对比，`[5,10)` 带棵数同时是硬隔离自检指标（应显著小于外带且核心区泄漏为 0）。

### 失败排查

- 若树数量断言失败且 Console 伴随 `[ForestSeeder] 未找到 NSk2 树 Prefab`：从 Asset Store 导入 Nature Starter Kit 2 至 `Assets/NatureStarterKit2/`，然后清空 TerrainData 树数据后重新 Bake。
- 若 Console 出现 `[Validate] Terrain.drawInstanced 未开启`：在 Inspector 中选中 Terrain > Settings > Instancing，勾选 `Draw Instanced`。
- 若抛出 `Terrain 树实例数不足`：先确认 `Assets/NatureStarterKit2/` 已导入，再看 Editor 日志中的 `[ForestSeeder]` 行——若为 `未找到 NSk2 树 Prefab` 则是资源定位失败；若为 `原型数组结构与预期不符，已中止且不写回`，说明 `treePrototypes` 被外部改动或上次 Bake 使用了不同数量的森林 prefab，此时 ForestSeeder 会拒绝落盘以保护中央段（可清空 TerrainData 树数据后重 Bake）。
- 若抛出 `核心矩形内检测到 N 棵森林树`：属硬隔离回归，检查 `ForestSeeder.Seed` 步骤 (b1)/(b2) 是否仍为「先用未扰动的原始距离 `d < ForestHardMargin` 拒绝、再把 `dEff` 钳到 `≥ ForestHardMargin`」。
- 若 Bake 报 `[SceneBuilder] Terrain 构建失败，中止 Bake`：属预期的失败隔离行为，导入 Nature Starter Kit 2 后重新 Bake；磁盘上的上一次可用场景未被覆盖。
- 若植被呈现粉色：确认项目仍为 **Built-in Render Pipeline**（Nature Starter Kit 2 的 Tree Creator shader 不兼容 URP/HDRP），或在 Graphics Settings 中手动指定 `Hidden/Nature/Tree Soft Occlusion Leaves` 等兼容 shader。

## 天空验证（Panoramic 全景，修复旧 cubemap 面缝）

### 背景

旧 NSk2 `skybox.mat`（`Skybox/Cubemap` + 2015 年自动生成的 cubemap，`seamlessCubemap: 0`、`aniso: 16`）面边界内容不连续，画面中出现竖直亮暗分界线、云被直线切断。修复：Bake 构建 `Assets/Materials/NaturalSkybox.mat`（`Skybox/Panoramic` 经纬度全景），贴图取 NSk2 `sky.tga` 的 2D 副本 `Assets/Materials/NatureSkyPanorama.tga`（强制 2D / Clamp / aniso 1 / 不压缩 / 关闭 mip 导入）；NSk2 原资产（`sky.tga` / `skybox.mat` / `Demo.unity`）未被修改。

### 自动断言（`JurassicProjectSetup.ValidateProject`）

- `RenderSettings.skybox` 非空，否则抛 `InvalidDataException`。
- 天空盒 shader 必须为 `Skybox/Panoramic`（全景单张贴图连续采样，无 cubemap 面边界）；违反即抛异常并提示重新 Bake。
- 天空材质 `_MainTex` 非空（对应 `NatureSkyPanorama.tga` 已就绪）。

### 像素探针（历史验证记录）

天空接缝修复曾通过八个方位的渲染和禁用 mip 的 A/B 探针验证。一次性 `SkyProbeCapture` 工具及菜单已移除；已有报告和图像保留。当前天空配置继续由 `JurassicProjectSetup.ValidateProject` 校验。

## Understory Pass 验证（林下层植被 / 岩石 / 资产门控接口）

### 背景与所有权

- `UnderstorySeeder`（`Assets/Editor/UnderstorySeeder.cs`，种子链第 5 段 `271833`）在 `TerrainEnvironmentSeeder.PopulateGround` 之后执行：重绘现有 7 个 detail 层密度（bush01-06 mesh 层 + grass01 billboard 层，不增删原型）+ 重建单一根节点 `Understory Pass`（子节点 Rocks / Vegetation / Deadwood；每次 Bake 先整根 `DestroyImmediate` 再重建，绝不叠加）。
- **安全缓冲不变量**：恐龙漫步包络 ⊆ 平坦矩形（x∈[-70,70]、z∈[-70,90] 世界坐标）；所有中大型实体道具（rock / hero bush / log / stump）位于平坦矩形 + `DinoSafetyBuffer`（默认 7.5m）之外；buffer 内 bush detail 密度恒 0（终态硬钳制）。碰撞规避不依赖运行时避让（未改 `DinosaurSpawnRandomizer`），而是靠上述几何不变量。
  > ⚠️ **本节描述已被下方「Environment Spatial Rebalance Pass」取代**：安全边界改为以 **DinosaurActivityBounds** 为准，
  > bush/fern 不再在整条 buffer 内硬钳制（只清空 R0 空地）。保留原文作为当时状态记录。
- **资产门控**：`Assets/Vegetation/Ferns` / `Rocks` / `Deadwood` 目录不存在或为空时，对应子系统 no-op（仅日志提示目录），其余子系统不受影响；放入合格 prefab 后重 Bake 自动生效（岩石簇分布逻辑无论有无资产都按固定种子确定性执行，仅供植被邻域增强与未来实例化）。

### 自动断言（`JurassicProjectSetup.ValidateProject`）

- 启用时（`UnderstorySeeder.PassEnabled == true`）场景必须存在 `Understory Pass` 根节点，否则抛 `InvalidDataException`。
- **实体缓冲不变量**：`Understory Pass` 下全部 Renderer 中心到**恐龙活动包络**的距离 ≥ `DinoSafetyBuffer − 0.05m`（bounds 容差），违反即抛异常（落位门限更严：成员 9.0 / 簇心 12.5，**数值未变**，基准由平坦矩形改为活动包络）。
- **低矮植被梯度断言**：bush/fern 层在 **R0 空地**（活动包络内缩 `WorldBounds.ClearingInset`）内按 64×64 抽样网格逐格断言密度为 0，**且 R1/R2 抽样非零格数必须 > 0**（防「梯度静默 no-op」），违反即抛异常。
  > 旧断言「bush 层在『平坦矩形 + buffer − 0.2m』内恒 0」已被取代（本 Pass 故意让低矮植被进入该带）。
- 禁用时（回滚态）若 `Understory Pass` 根仍存在 → 告警提示重 Bake 完成回滚。
- `DebugProceduralRocks == true` 时告警「仅限分布验证，正式 Bake 禁止」。
- **HQ 资产完整性**：`Understory Pass` / `Environment Props` 子树与三个门控目录下的 prefab，任何 Renderer 的 material 为 null、shader 为 null / 不受支持 / 为 `Hidden/InternalErrorShader` 都抛 `InvalidDataException`（`ValidateNoMissingMaterials`）。
- `UnderstorySeeder.PreviewScale != 1` 时告警：提示当前场景很可能来自 Preview Bake，需置回 1 重 Bake。
- fern / deadwood / rock 接口状态（prefab 数量）并入 `JURASSIC_MVP_VALIDATION_OK` 行与信息日志。

> **Fern 的验证口径**：Fern **不属于** `Understory Pass` GameObject 子树，它存在于 **TerrainData 的 `DetailPrototype` / density layers**。
> `Understory Pass` 子树只统计 **Rock / Bush / Deadwood** 等 GameObject 数量；Fern 用 **detail prototype 数 + 各层 Σdensity** 验证。
> 详见下节「HQ Asset Pass」。

### 人工 Play Mode 检查（玩家视高 1.7m，禁用鸟瞰 / 自由飞行观察）

1. **中央草地**：活动区内草地连续低矮（密度 2-4，出生点 20m 内减半），bush 密度恒 0；确认无裸露突兀的空白斑块。
2. **林缘重点带**：从活动区向外走，确认草层渐趋斑驳（带缝隙），bush01-06 以团簇（cluster）形态重点分布，簇间留有空隙、非均匀撒点。
3. **林内底层**：进入森林，确认草密度降低、bush 以低频斑块补底层（未来 Fern 的过渡覆盖）。
4. **泥斑与通道**：泥土斑块区植被明显减少；沿 35°/160°/285° 视觉通道方向确认可辨识的裸带。
5. **英雄灌木**：确认约 36 个放大灌木（1.0-2.2 倍）分布于岩石簇 / 树干基座 / 林缘附近，无悬浮、无穿模。
6. **重复图案检查**：确认无明显规则网格或重复噪声图案（hash dither + 与 ForestSeeder 不同频段的 cluster 噪声）。

### 幂等与回滚演练

1. **双 Bake 幂等（hash 判据）**：连续执行两次 `Jurassic World > Scene > Bake Scene (Edit Mode)`。
   - 第 1 次 Full Bake **合法地**修改 `JurassicTerrainData.asset`（正式写入 Fern detail data）——记录该文件的**文件 hash**；
   - 第 2 次 Full Bake 后再算 hash，**两次 hash 必须完全相同**；
   - 同时断言：Fern 各层 Σdensity 一致；Rock / Deadwood 实例数一致；`Environment Props` 下 `Rock_Large_*` 仍为 12；不新增重复 prefab / material / texture；
   - 第二次日志应全部为 `与现有一致跳过`（bush/grass/fern 层 diff=0）。
   > ❌ **不得**用「Full Bake 后 TerrainData 对 HQ 集成前 Git baseline 无 diff」作幂等条件 —— 首次正式接入 Fern 本就应该改变 TerrainData。
2. **回滚演练**：将 `UnderstorySeeder.EnableUnderstoryPass` 置 `false` → 重 Bake → `Understory Pass` 根消失；bush 层逐格回到 SeedDetails 基线（日志 `bush 层恢复 N 个`，抽查 `GetDetailLayer` 与基线 helper 输出一致；基线算法与 spawn 点由 `JurassicWorldSceneBuilder.BuildBushBaselineDensity / DinosaurSpawnPoints` 同源复刻）；grass01 层由 GroundPass 同 Bake 先行重绘恢复。再置回 `true` 重 Bake，产物与「从未禁用」逐格一致（纯函数恢复，无永久覆盖）。

### DebugProceduralRocks 分布验证（可选，正式 Bake 禁止）

1. 临时将 `UnderstorySeeder.DebugProceduralRocks` 置 `true` → 重 Bake → 日志出现 `[UnderstorySeeder] [DEBUG ONLY] 程序化 Sphere 岩石 N 个已生成`。
2. 以 Play Mode 目检岩石簇分布（**展示环 7 个岩石位**紧贴平坦矩形外 12.5–20m + 林缘 / 坡脚加权的其余簇；确认全部位于活动区 + 缓冲外、沉地自然、无悬浮）。
3. 确认后恢复 `false` 重 Bake；`ValidateProject` 会对此开关持续告警。

## HQ Asset Pass（Poly Haven 资产接入）

### 资产与所有权

- **`Assets/ThirdParty/**` 严格只读**：不修改源文件，**也不修改任何 `.meta`**。需要不同导入设置的贴图一律复制到项目自有路径后再配置该 copy。
- 源资产：`Assets/ThirdParty/PolyHaven/{Fern02,RockMossSet01,DeadTreeTrunk02,TreeStump01}/`（模型 + diff / nor_gl / rough）。
- 项目自有产物（由 `PolyHavenAssetIntegration.EnsureAssets` 幂等铸造）：
  - 贴图 `Assets/Textures/PolyHaven/`：`Fern02_Normal.exr`（唯一需要复制的源文件：源 nor_gl importer 是 Default+sRGB，不能直接当 Normal Map；copy 的 importer 设为 **Normal Map + sRGB off**）、`Fern02_AlbedoCutout.png`、4 张 `*_Smoothness.png`。全部 **maxTextureSize = 2048**（HQ 纹理不无条件以 4K 进 runtime；源 4K 保持不动）。
  - 材质 `Assets/Materials/PolyHaven/`：`Fern02_Cutout.mat`（项目自有双面 Cutout shader）、`RockMossSet01.mat` / `DeadTreeTrunk02.mat` / `TreeStump01.mat`（Standard 不透明）。
  - prefab `Assets/Vegetation/{Ferns,Rocks,Deadwood}/`：4 Fern + 6 Rock + 1 Log（含 4 级 LODGroup）+ 1 Stump。**默认全部无 Collider**。
- **贴图通道约定**：Diffuse→Albedo；**Roughness→Smoothness 必须取 `1 − Roughness`** 并烘焙进 `_MetallicGlossMap.a`（**不得把 Roughness 直接当 Smoothness**）；Normal 用 **Normal GL**（Unity Built-in 即 OpenGL Y-up 约定，可直接用）；本批资产**无 AO 贴图**，故不设 `_OcclusionMap`。
- **像素读取**：统一走 Editor GPU readback（`Blit → RenderTexture → ReadPixels`），不使用 `ImageConversion.LoadImage`，**不临时翻转源 importer 的 `isReadable`**；源 importer 全程不被改写。
- **Fern Cutout**：`Fern02_AlbedoCutout.png` 的 **A 通道 = 真实 alpha mask**（mask 位于 A 通道还是 RGB 灰度由像素探查确定并打印）；**绝不用 diffuse 亮度参与裁切**（深色叶片实测占比 > 0，用亮度会误裁）。`_Cutoff` 是 material 属性（默认 0.35），**不在 shader 内硬编码**。
- **Fern 四变体共享总密度**：`BuildFernField()` 的空间分布 / cluster center / mud suppression / channel avoidance / seed 全部不变；`fernField[x,z]` 定义为该 cell 的 **Fern 总密度**，再按逐 unit `Hash01`（独立固定 salt）确定性拆分给各 variant 层，保证每 cell `densityA+…+densityD == target`。**新增 Fern prefab 不会线性抬高总植被密度**；不额外消费 `System.Random`。
- **中央 12 个 `Rock_Large_*`**：只换资产、不换布局 —— 保留 `Environment Props` root 名、`Rock_Large_*` 命名、原位置、原 RNG 消费**次数与顺序**；prefab 选择用索引取模不额外消费 RNG。HQ 岩石按 `mesh.bounds` 计算**确定性 uniform normalization scale** 使 XZ footprint ≈ 原 Sphere 目标直径（`finalScale = targetFootprint / sourceFootprint`，随机因子已折进 target，不重复相乘），并按 `bounds.min.y` 做 ground anchoring + 固定 sink。这样 `DinosaurSpawnRandomizer` 读取 `Environment Props` bounds 作开局避让圆时尺寸语义不变。
- **GroundPass 岩石散布已退役**：`TerrainEnvironmentSeeder.ScatterRocks` 及其常量 / 调用 / 回滚分支整体移除，`Ground Pass Rocks` root 不再创建；外围岩石由 `UnderstorySeeder` 的资产门控 cluster 分布独占。`DinosaurSpawnRandomizer.CollectPropCircles` 对缺失 root 静默跳过，**未修改任何恐龙逻辑**。

### 非破坏性 Preview（先跑这个，通过后再 Full Bake）

```bash
Unity.exe -batchmode -quit -projectPath <proj> \
  -executeMethod JurassicWorld.Editor.PolyHavenPassPreview.Run \
  -logFile Logs/hq_preview.log
```
或菜单 `Jurassic World > Diagnostics > Preview HQ Asset Pass`。

- **非破坏性契约**：克隆 `JurassicTerrainData`（`Object.Instantiate`）+ **Single 临时场景**，全部派生物落在克隆体与临时场景上；
  结束 / 异常 / 取消都销毁临时对象与克隆体；**全程不调用 `AssetDatabase.SaveAssets` / `SaveScene`**；
  **Full Bake 是唯一被允许修改生产 TerrainData 的路径**。
  必须用 Single：`UnderstorySeeder.Seed` 内 `GameObject.Find("Understory Pass")` 会跨所有已加载场景命中，
  若 `Main.unity` 同时在场景会销毁生产 root。
- **缩小规模不改契约**：只用 `UnderstorySeeder.PreviewScale`（editor-only，默认 1；Preview 用 4）按**确定性前缀**缩小岩石簇 / fern 簇 / deadwood 数量；
  不改任何分布常量、不改任何随机流。`PreviewScale` 在 `finally` 中复位为 1。
- **安全门**：活动场景 `isDirty` 时直接中止（避免丢弃未保存的内存改动）。
- 报告内容（`[HQPreview]` 前缀 + `JURASSIC_POLYHAVEN_PREVIEW_OK` / `_FAIL` 结束标记）：
  Fern 层数 / 逐层 Σdensity / **密度守恒断言** / 变体混合；Rock 与 Deadwood 各变体实例数；
  材质类型（material + shader + `isSupported`）；**实际 import 纹理分辨率**；mesh 三角面数；
  各 prefab 实测真实世界 bounds（尺度审计）；中央 12 岩石的 **target footprint / 实际 footprint / ratio**（超 ±25% 告警）；
  Fern Cutout 的 mask 来源与透明像素占比（≈0 → 有矩形背景；> 0.92 → 叶缘缺失）；
  **missing material / shader 计数**（非 0 即 FAIL）。
- **Preview 后必须确认**：`git status` 中 `Assets/ThirdParty/PolyHaven/**` **零 diff**（含 `.meta`），
  且 `Assets/Scenes/Main.unity` 与 `Assets/Terrain/JurassicTerrainData.asset` **零 diff**。
- **Fern 数量异常时**：只报告并停止，**不自动修改** `FernClusterCount` / `FernPerClusterMax`，等确认后再进 Full Bake。

### HQ Asset Pass 失败排查

- 若 Fern / Rock / Deadwood 实例数为 0：确认 `Assets/Vegetation/{Ferns,Rocks,Deadwood}` 下存在 prefab，
  且名称含关键词（fern/plant；rock/boulder/stone；log/stump/deadwood/snag）**且不命中排除词**。
  ⚠️ **排除词含 `tree`** —— 故 log / stump prefab 命名为 `DeadwoodLogTrunk02` / `DeadwoodStump01`，
  直接用源名 `DeadTreeTrunk02` / `TreeStump01` 会被**静默过滤**。
- 若报告 FAIL `fern 层数为 0`：检查 `PolyHavenAssetIntegration.EnsureAssets` 日志是否报源资产缺失。
- 若报告 FAIL `缺失 material / shader`：执行 `Jurassic World > Assets > Integrate PolyHaven Assets` 后重试；
  Standard 材质在代码中创建时必须显式 `EnableKeyword("_NORMALMAP")` 与 `("_METALLICGLOSSMAP")`，否则法线 / 光滑度不生效。
- 若报告 FAIL `Fern 透明像素占比` 越界：检查 `Fern02_AlbedoCutout.png` 的 mask 来源探查结论，或调 `Fern02_Cutout.mat` 的 `_Cutoff`。
- 若报告 FAIL `fern 密度不守恒` 或 `存在 Σdensity 完全相同的 fern 层`：说明回退成了「每层写一份完整目标密度」，检查 `ApplyFernGate` 的变体拆分。

### 失败排查

- 若抛出 `Understory Pass 已启用但场景缺少 "Understory Pass" 根节点`：说明 Bake 未完成或场景被手动改动，重新 Bake。
- 若抛出 `Understory 实体 ... 位于安全缓冲内`：检查 `UnderstorySeeder` 的 `ClusterCenterMinDistance`（= buffer + 1.5 + 3.5）与 `DinoSafetyBuffer` 常量是否被调小，或簇成员散布半径是否越界。
- 若抛出 `bush 层 idx=... 在安全缓冲内存在非零密度`：检查 `PaintDetailLayers` 的终态硬钳制（`dFlat < DinoSafetyBuffer → v=0`）是否被移除或改动。
- 若日志出现 `未识别到 bush detail 层` / `未识别到 grass01 层`：检查 NSk2 是否导入（`NatureStarterKitLocator`）与 GroundPass 是否启用；bush 层签名 = NSk2 detail mesh 名称含 "bush"，grass01 层签名 = `prototypeTexture == grass01` 纹理。
- 若 fern / deadwood 接口未生效：确认 prefab 已放入对应目录（`Assets/Vegetation/...`）且名称含关键词（fern/plant；log/stump/deadwood/snag），且不命中排除词（**tree**/bush 等）——⚠️ 排除词含 `tree`，用源名 `TreeStump01` / `DeadTreeTrunk02` 会被静默过滤，故 deadwood prefab 命名为 `DeadwoodLogTrunk02` / `DeadwoodStump01`；prefab 命名影响排序契约，跨 Bake 改名会改变实例化随机流。

## Detail 渲染前提与地表贴图（2026-09-22 HQ Full Bake 视觉检查三项修复）

### 背景（症状 → 根因，三项都不是「资产未加载」）

1. **detail 全域未绘制**（fern「白茫茫」/ 看不到岩石簇与倒木的真实原因之一）：
   detail 层 Σdensity 非 0（bush 55k–135k、grass 464,951、fern 179,669），但 `Logs/acceptance/shots/spawn_ref.png`
   （与 Game View 同机位同 FOV）里平坦区草地按公式密度≈3–4/格却**看不到任何草与灌木** ⇒ 不是 fern 特有问题，
   而是**整个 terrain detail 通道不渲染**。根因：`new TerrainData()`（Unity 2021.2+）默认
   **Detail Scatter Mode = CoverageMode** —— 该模式下 detail map 的值被解释为「覆盖比例」，实例数改由
   `DetailPrototype.density`（相对原型尺寸）推导；而本项目所有 DetailPrototype 都由 C# `new DetailPrototype{...}`
   构造、从未设 density，且全项目语义（`SeedDetails` 随机 2-6 基线 / `PaintDetailLayers` / 验收
   `approxInstances = Σdensity × detailObjectDensity`）都是 **InstanceCountMode（每格实例数）**。
   模式不匹配即出现「GetDetailLayer 读回密度非 0，却一个实例都不画」的静默失效（旧校验只查 Σdensity，故全绿）。
2. **「白茫茫」是地表本身**：平坦矩形外 30m 过渡带后 `w0 = 1-t = 0`（林地无 Grass 层权重），而第 4 层
   `DryGrassLayer` 的 diffuse 曾解析到 `Assets/NatureStarterKit2/Textures/grass02.tga` —— 实测是
   **211/211/211 的灰度渐变斜坡图（不是草贴图）**。NSk2 的 `grass01/grass02/bush01/bark*` 都是给
   「灰度贴图 + 顶点色 / healthy-dry 着色」的网格与 billboard 用的，**不可当地表 diffuse**（只有
   `ground01/02/03` 是有色的）。fern 带正好落在这个区域，观感上就像「蕨类资产没加载」。
3. **实体道具看不到 = 可达范围几何 + 树冠遮挡**：165 岩石 / 13 倒木树桩的实例与高度都正确，但一律位于平坦矩形
   （`x∈[-70,70] z∈[-70,90]`）+ `DinoSafetyBuffer` 7.5m 之外（≥77.5），而相机被 `TravelLimit` 钳制；
   metrics 实测最近岩石距可达盒 35.5m、最近倒木 34.1m、最密 fern 格 38.7m，且那片是 `ForestSeeder`
   核心矩形之外 60→100m 深的林墙：eyeball 高度 1.7m 下，树线只在地面缝隙透出贴地的蕨类/草，
   1-5m 高的岩石 / 倒木 / 树桩被树冠与树干挡死（**修复前**取证：
   `Logs/acceptance/shots_before_showcase/zone5_rock_observer.png`、`rock_8m.png`）。**已修复**，见下节。

### 修复点

- `JurassicWorldSceneBuilder.EnsureDetailInstanceCountMode(data)`：`BuildTerrain` 内、**任何 detail 写回之前**
  把 `detailScatterMode` 归一化为 `InstanceCountMode`（幂等；日志打印旧值 → 新值）。
- 三处自建 DetailPrototype 补 `density = 1`（CoverageMode 下的实例数倍率基准）：`SeedDetails`（bush 网格）、
  `PaintOwnedDetails`（grass01 billboard）、`ApplyFernGate`（fern 网格）。
- `TerrainEnvironmentSeeder`：`DryGrassLayer` 的 diffuse token `grass02 → ground02`（浅棕土壤）；`EnsureTerrainLayer`
  对**已存在**的 layer 增加 diffuse 校验修复（历史产物指向灰度图时重指向并 `SetDirty`，逐项比对保证幂等）。
  alphamap 权重公式**不变**（中央草地区仍为纯 ground01 绿）。
- `SmoothExplorerCamera.TravelLimit 47 → 65`：玩家可走到林缘，使 fern 带与实体道具落在可达边界外约 12.5m。
  **上界必须 < 70**（平坦矩形边缘；矩形内 heightmap 恒 0 是 `eyeHeight = 1.7m` 硬编码成立的前提）。
- `PolyHavenAssetIntegration`：fern 材质不再 `EnableKeyword("_NORMALMAP"/"_METALLICGLOSSMAP")` —— 本项目
  surface shader 由 `surf` 内直接采样 `_BumpMap`/`_MetallicGlossMap`、未声明这两个 keyword，置上只会落进
  `m_InvalidKeywords`（检查器无意义告警）。Standard 材质（`ConfigureOpaque`）的调用保留。

### 自动断言（`JurassicProjectSetup.ValidateProject`）

- `terrainData.detailScatterMode == DetailScatterMode.InstanceCountMode`，否则抛 `InvalidDataException`（整套密度语义失效）；
- 一行信息日志：`scatterMode / detailObjectDistance / detailObjectDensity / 原型数 / density<=0 原型数 / drawInstanced`。

### 人工 Play Mode 检查（视高 1.7m）

1. 中央草地仍是连续低矮绿草，但**草 billboard 与 bush detail 应真正可见**（修复前完全不显示）；
2. 走到可达边界（`|x|` 或 `|z|` ≈ 65）朝林缘看：应出现**绿色蕨类带**（约 12.5m 外）、**moss 岩石簇 / 倒木 / 树桩**，
   林地地表为**棕色**而非白茫茫。

### 失败排查

- 若 detail 仍完全不渲染：先看 rebake 日志 `[SceneBuilder] detailScatterMode: <旧值> → …` 确认归一化是否发生；
  若已是 InstanceCountMode 仍不渲染，则在 Terrain Settings 取消勾选 **Draw Instanced** 观察是否恢复
  （代码强制 `drawInstanced = true`，`ValidateProject` 对其有告警）。
- 若 bush/grass 正常出现而**仅 fern 异常**（白 / 黑 / 不可见）：fern 是本项目唯一使用自制 surface shader 的
  detail 材质，问题在其 instanced detail 绘制路径 → 改为 instancing-ready 的 vertex/fragment Cutout shader
  （`Cull Off` + alphatest + `#pragma multi_compile_instancing`）。
- 若林地地表仍偏白：确认 `Assets/Terrain/Layers/DryGrassLayer.terrainlayer` 的 diffuse 已重指向 `ground02.tga`
  （Bake 日志 `[GroundPass] …/DryGrassLayer.terrainlayer: diffuse 修正为 ground02`）。

## 林下层可见性 + 蕨类风动（2026-09-22 后续修复）

### 背景（症状 → 根因）

1. **moss 岩石簇 / 倒木 / 树桩 依然看不到**（蕨类带与棕色林地已正常）：实体道具虽正确落位，但全部在
   平坦矩形 + 7.5m 之外 ≥77.5 的全密度林区里，树冠与树干把 1-5m 高的道具挡死 —— 只有贴地的蕨类/草
   能从树线底部缝隙透出来。**根因是"位置对但不可见"，不是资产未加载。**
2. **蕨类"整株左右平移滑动"**（不是叶片摆动，也不是枢轴偏移）：
   - 蕨类 detail 原型是 `usePrototypeMesh = true` + `DetailRenderMode.Grass`。**Grass 模式走 Unity 内置
     waving-grass detail shader（材质上的自制 shader 在该路径下完全不参与渲染）**，其风位移是
     **整株水平平移**，位移量级与顶点高度无关；
   - 2026-09-22 用 `DetailMotionProbe`（同帧受控渲染 + 逐像素 diff）实测确认：
     · 同帧重复渲染 / 机位抖动回原位：0 像素差异 → **不是**实例每帧重散布；
     · `DetailPrototype.renderMode`（内存改动）与材质 `_Cutoff=1` 都不改变画面 → 该路径既不走原型材质，
       也不会被内存里的 renderMode 改动生效（需重 Bake）；
     · 把 `TerrainData.wavingGrassStrength/Amount` 置 0 → **蕨类像素 71.25% 发生变化**（画面 6.8%）；
       把 `WindZone.main` 置 0 → 蕨类像素只变 2.12%（画面 23.1%，主要是树冠与草地）⇒ **驱动蕨类位移的是
       `wavingGrass*`，不是 WindZone**；
     · 按连通蕨类块统计：**底部接触边界像素**受风影响比例 81.2%、**顶部边界** 79.4% ⇒ 连根部一起被推走
       （不是"叶尖摆动"），这也解释了为什么 0.3m 高的蕨类被推 0.2m 量级时观感极其突出。
   - 已排除枢轴嫌疑：`Assets/Meshes/PolyHaven/Fern02_A.asset` 网格 y ∈ [-0.029, 0.258]，枢轴距根部仅 2.9cm；
     也排除逐帧脚本抖动（工程内无 `_Time`/wind 顶点动画脚本）。
   - 结论：**在 detail 渲染路径里无法实现"根部固定 + 叶尖轻摆"**（位移是整株平移，且不受我们控制）——
     只能让蕨类脱离内置 waving shader。

### 修复点

- `UnderstorySeeder`：新增 **展示环**（`BuildShowcaseRing` / `InShowcaseClearance`，纯函数、不读 TerrainData）：
  10 个岩石簇心按 360° 扇区落在平坦矩形外 **12.5–20m** 的近林带，另加 3 倒木 + 2 树桩位（错开扇区、共用
  min-gap 9m）。地理约束：距平坦矩形 ≥ `ClusterCenterMinDistance`(12.5m)、且距地形边界 ≥ `WallInset-2`(10m)
  —— 北侧（平坦矩形 z 上界 90 → 地形边 100 只有 10m）几何上放不下，故该方向没有展示位。
  实测布局：**11 个位**（岩石 7 / 倒木 2 / 树桩 2），覆盖 E/SE/S/SW/W/NE。
- `ForestSeeder`：拒绝采样链中新增 **展示环清空**（`UnderstorySeeder.InShowcaseClearance`，纯几何判定、不消费
  随机数）：每个展示位让出 **清空圆 7m + 指向平坦矩形的视线通道（半宽 3.5m、向矩形内再延伸 8m）**，
  即从树线到道具的整条视线被打通。与视觉通道共用 `borderDist >= WallInset` 守卫 → **边界密林墙与地平线遮挡
  语义不受影响**；清空随 `UnderstorySeeder.PassEnabled` 联动，禁用该 Pass 时森林恢复为无缺口基线。
- `UnderstorySeeder`：hero 岩石簇改用展示环簇心；倒木/树桩各划出 3/2 个落展示位（展示位的倒木横置朝向草地，
  长轴垂直于视线）；其余簇保持原有加权分布并新增"近林带优先"权重；小石缩放下限 `0.35 → 0.55`（原下限在
  15m 外几乎不可见）。
- **蕨类脱离内置 waving shader（本次修复核心）**：
  · `UnderstorySeeder` 的 fern 原型 `renderMode`：`Grass → VertexLit`（Unity 文档：VertexLit 不随风移动）。
    该值只烘进 TerrainData → 必须重 Bake 才生效；
  · ⚠️ **踩坑记录**：`ApplyFernGate` 原先只在"没有自有 fern 原型"时才构造原型（`alreadyNormalized` 分支直接沿用
    既有原型）⇒ 只改代码重 Bake **不会**改写 `renderMode`，修复会静默不生效（第一次尝试即栽在这里）。
    现已改为**规格比对**（`FernPrototypeMatchesSpec`：prototype/renderMode/density 任一不符即整块重建），
    Bake 日志会打印 `fern 原型块重建（规格不符，已按目标 renderMode/密度重写）于 index=7 … renderMode=VertexLit`；
  · `ValidateProject` 新增不变量：所有自有 fern 原型的 `renderMode` 必须 == `UnderstorySeeder.FernRenderModeTarget`，
    否则抛异常（防"改了代码没重 Bake"再次静默复发）；
  · **结论（实测）**：VertexLit 模式下蕨类**完全静止**（没有任何摆动）—— 这是"根部绝对不动"的保证。
    `Assets/Shaders/FoliageCutoutDoubleSided.shader` 在该路径下**不参与渲染**（把材质 `_Cutoff` 置 1 画面毫无变化
    即为证），所以"叶尖轻摆"无法通过改这个 shader 实现；shader 已还原为原样，只在文件头注明该现状与后续出路。
    想要"根部固定 + 叶尖轻摆"必须让蕨类**脱离 terrain detail**（自研分块 `DrawMeshInstanced` + 自己的顶点风摆，
    根部 y=0 位移恒为 0），属独立改造，本次不做。
- `JurassicWorldSceneBuilder.ApplyDetailWindSettings`：保留 `TerrainData.wavingGrassStrength/Amount = 0.20/0.30`
  （绝对值 → 幂等）。蕨类已不吃这套风力，此值现在只影响 **草 billboard 与 bush detail** 的摆动幅度
  （比 Unity 默认 0.5/0.5 更收敛）；若日后觉得草地太"静"，可恢复默认而不再影响蕨类。

### 自动断言（`JurassicProjectSetup.ValidateProject`）

- 展示环非空、每位距平坦矩形落在 `[DinoSafetyBuffer, ShowcaseBandOuter]` 距离带内；
- 生产规模（`PreviewScale == 1`）且门控有资产时：`Understory Pass/Rocks` 实例数 ≥ 展示环岩石位数、
  `Deadwood` 下 `FallenLog_*` / `Stump_*` 实例数 ≥ 各自展示位数（否则抛异常）；
- `wavingGrassStrength/Amount` 必须等于目标值，否则抛异常（与 `detailScatterMode` 同类：数据对但渲染表现错的静默失效）。

### 验收指标（`ProductionAcceptance`）

- `constants.showcaseSpotCount/showcaseRockCount/showcaseLogCount/showcaseStumpCount`（布局事实，入 metrics 便于比对）；
- `terrain.wavingGrassStrength/Amount/Speed`；
- 新增门槛：`B15-showcase-layout`（≥9 位且各类 ≥5/2/2）、`B15b/B15c-showcase-nearest`
  （最近岩石 / 倒木距可达盒 **≤20m**，修复前为 35.5m / 34.1m）、`B15d-detail-wind`（风力等于目标值）；
- `B9-tree-count-unchanged` 改为 `B9-tree-count-clearance-aware`：森林树数只允许**减少**且减少量 ≤ 400
  （展示环清空让树的必要代价，不再要求逐值不变）。

### 取证机位（`AcceptanceShotCapture`）

新增 `showcase_NN_<kind>_*` 组：机位 = 平坦矩形上距该展示位最近的点钳制进 `TravelLimit`（即玩家贴着边界站的
那一步），朝道具位看 —— 这组图就是"玩家能不能看见岩石/倒木/树桩"的直接判据。

### 本次修复的机器验证结果（2026-09-22，批处理 `-executeMethod`）

- **Bake ×2 幂等**：第 2 次日志全为 `与现有一致跳过` / `结果与现有实例一致，跳过写回`；`JurassicTerrainData.asset`
  两次 sha256 完全一致（`10fb5cd9…`，字节级 `cmp` 通过）。
- **`ValidateProject` 全绿**：`JURASSIC_MVP_VALIDATION_OK … 树 1985 棵（阈值 400）/ 核心区森林树泄漏 0 /
  Understory实体 227 个（buffer 7.5m 外，bush 层 6 个抽样密度 0）/ fern 接口 4 / deadwood 接口 2 / rock 接口 6`；
  新增断言 `[Validate] Understory 展示环：11 个近林带道具位（岩石 7 / 倒木 2 / 树桩 2）` 与
  `wavingGrass strength=0.200/amount=0.300/speed=0.500` 均通过。
- **指标**（`Capture Post-Bake #1`）：`understoryRocks.nearestToTravelBoxDistance=16.16`（修复前 35.51）、
  `deadwood.nearestToTravelBoxDistance=15.30`（修复前 34.14）、`vegetation.nearest=16.63`。
  展示环 11 位全部有实例落位：7 个 `Rock_NN_Hero` 与展示位坐标误差 0.00m，倒木 `FallenLog_00/01`、树桩 `Stump_00/01` 同。
- **森林代价**：森林树 1974 → 1824（−150，清空排除候选 353 个）。`treeNear` 影响场未变 —— BFS 网格为 1.57m/格，
  每格有多棵树，局部移除不改变源集合 → bush/fern 密度场逐格不变（日志 `与现有一致跳过`）。**这是预期行为，不是漏算。**
- **取证组图**：`Logs/acceptance/shots/showcase_*.png`（11 张）——`showcase_04_rock.png` 可见 moss 岩石、
  `showcase_07_log.png` 可见倒木与右侧 moss 岩石。修复前的同机位图保存在 `Logs/acceptance/shots_before_showcase/`。
- **目视副作用**：清空通道打通树线底部视线，局部会露出地平线/天空亮带（该现象修复前已存在，通道使其略放大）；
  若观感不接受，收紧 `ShowcaseClearRadius` 7→5m 或 `ShowcaseLaneHalfWidth` 3.5→2.5m 即可。

### 人工 Play Mode 检查（视高 1.7m）

1. 走到可达边界（`|x|` 或 `|z|` ≈ 65）朝林缘逐个方位看：应能看到 **moss 岩石簇（hero 3.5–4.9m 宽）、倒木、树桩**，
   位置约在 12.5–20m 外、**正对树线缺口**（缺口宽约 7–11m，观感是"林缘小径/空地"）；
2. 绿色蕨类带仍在、林地地表仍是棕色（不被本次改动静默破坏）；
3. 蕨类**完全静止**（VertexLit 模式下根部与叶尖都不动）——用"站立不动反复确认根部有没有横移"作为判据；
   若想要"根部固定 + 叶尖轻摆"，见上一节结论：需走自研实例化渲染，属独立改造（当前不做）；
4. 中央草地与 bush 仍有风动（受 `wavingGrass 0.20/0.30` + WindZone 驱动）；
5. 3 条视觉通道（35°/160°/285°）与边界密林墙的地平线遮挡未受影响。

### 蕨类静止修复的实测数据（2026-09-22，`DetailMotionProbe` + 批处理）

- 修复前（Grass）：`wavingGrass` 置 0 → **蕨类像素 71.25% 变化**；蕨类连通块的底部接触边界 / 顶部边界
  受风影响比例 **81.2% / 79.4%**（= 整株平移）；最优对齐位移无意义（覆盖面对不上）。
- 修复后（VertexLit）：
  · `wavingGrass` 置 0 → 蕨类像素只剩 **6.24%** 变化（均值 2.59，且集中在根部——是蕨类镂空缝隙里透出的
    *草/灌木* 在摆，非蕨类本身）；
  · 蕨类覆盖掩码在"风开 / 风关"两帧之间的**最优对齐位移 = (0,0)，重合 99.77%** ⇒ 蕨类覆盖面不再平移；
  · 连通块整体变化率 **76.8% → 6.4%**；底部/顶部边界变化率 81.2%/79.4% → 22.3%/14.7%（且底 > 顶 = 背景草在摆）；
  · `Bake ×2` 幂等：TerrainData sha256 `68fe8a79…` 两次字节级一致；第 2 次不再重建 fern 原型；
  · `ValidateProject` 全绿（含新增 fern renderMode 断言）。

### 失败排查

- 若岩石/倒木仍看不到：先看 Bake 日志 `[UnderstorySeeder] 展示环：…` 与
  `[ForestSeeder] … 展示环清空=N 处（排除候选 M 个）`。清空排除数为 0 ⇒ 布局与 `borderDist` 守卫冲突；
  有排除但物体会被树挡 ⇒ 缺口的视线通道没打通（检查 `ShowcaseLaneHalfWidth` / `ShowcaseLaneInwardExtension`）。
- 若某方向完全没有展示位：北侧是几何限制（缓冲 12.5m > 可用 10m）；东/西若也空，检查
  `ShowcaseBorderMargin` 是否把整条环带排除了。
- 若蕨类根部仍在整株平移（重 Bake 后仍如此）：按顺序查
  (a) `DetailMotionProbe` 的 ④ 项：把 `wavingGrass` 置 0 后蕨类像素是否还变——变了说明 renderMode 没生效
  （fern 原型必须重 Bake 成 `VertexLit`，日志 `fern 原型块追加于 index=…` 之后可查 `ProductionAcceptance`
  报告里的 `fern.layer.N.renderMode`）；
  (b) 若已 VertexLit 但位移仍在，说明 VertexLit 仍走内置 detail shader（自制 shader 未参与），此时唯一
  "根部固定"的保证是把 `TerrainData.wavingGrassStrength/Amount` 直接置 **0**（蕨类完全静止，草地/树不受影响），
  同时把 `ValidateProject` 与 `ProductionAcceptance` 里的风力目标值改成 0；
  (c) 若想同时保留摆动，则只能让蕨类脱离 terrain detail（自研实例化绘制 + 自己的 shader），属独立改造。
- 若蕨类带出现"缺叶/实心片"（VertexLit 下镂空失效）：说明该路径没走 `Fern02_Cutout.mat`；回退到
  (b) 的 `wavingGrass=0` + Grass 模式（保留原有的内置 cutout 表现，只是完全静止）。

---

## Environment Spatial Rebalance Pass（四边界拆分 + 中央梯度重构，2026-09-23）

### 背景（症状 → 根因）

- **症状**：中央活动区（140×160m）视觉上是一块空草坪 —— bush 密度在**整个**平坦矩形内被硬钳制为 0，fern 同样为 0，
  中大型道具（rock / hero bush / log / stump）全部被推到矩形外 9–12.5m；玩家可达范围（`TravelLimit=65` 方形）
  卡在平坦区里，看得到却走不到任何层次。
- **根因**：一个「平坦矩形」同时承担了三个互不相干的职责（地形是否平坦 / 恐龙能走到哪 / **所有**植被的安全边界），
  于是低矮无碰撞植被（本该允许进入活动区外缘）与中大型实体道具（必须与恐龙隔离）用了同一条线。

### 修复点（四边界拆分，`Assets/Scripts/WorldBounds.cs` 为唯一权威）

| 边界 | 定义 | 数值 |
| --- | --- | --- |
| ① TerrainFlatBounds | 只描述哪些地形保持平坦 | `x[-70,70] z[-70,90]`（**未变**，heightmap 恒 0） |
| ② DinosaurActivityBounds | 恐龙可能移动 / 被召唤经过的 gameplay envelope（出生矩形 ⊕ `DinosaurAgent.WanderRadius` 的 AABB，真实包络的**保守超集**） | `x[-70,70] z[-70,90]`（由恐龙常量推导；与①数值巧合相同） |
| ③ PlayerExplorationBounds | 玩家允许探索的范围 | `x[-78,78] z[-78,88]`（西/东/南 = ② 外扩 8m；北侧止于北林线 88） |
| ④ VisualVegetationZones | Grass/Fern/Bush/Rock/Deadwood 的视觉分布分带 | R0 空地（②内缩 50m）→ R1 稀疏低矮植被 → R2 玩家过渡带（≤8m）→ R3 林缘（≤19.5m）→ R4 密林 |

- **中大型道具**（rock / hero bush / log / stump）仍以 `DinoSafetyBuffer`(7.5) 为界（成员落位门限 9.0、簇心 12.5，
  **数值逐字未变**），只是判定基准从①换成② —— 以后改①不再牵动植被安全。
- **低矮无碰撞植被**（bush / fern detail）改由 `WorldBounds.LowVegGate01(sd)` 控制：R0 空地恒 0，从②外缘起 0.35（稀疏）
  单调加密到 19.5m 处 1.0（**该点与旧 ramp 饱和点对齐 ⇒ `sd ≥ 19.5` 区域逐格不变**）。
  既有 fern 簇流与其 7.5m **采样判据**原样保留（R3/R4 森林 fern）→ 既有簇位置不重掷；同时 fern 的**场值硬钳制**
  从「`< 7.5m` → 0」改为「R0 空地内 → 0」，于是紧贴边界的既有林缘簇会外溢约 4m（实测 fern 变化格最大 sd = 9.69m，
  25559 格）—— 这正是「低矮植被允许进入活动包络外缘 + 玩家过渡带」的预期效果。新增 `+4` 独立簇流专铺 R1/R2，两流互不干扰。
- **玩家端**：`SmoothExplorerCamera` 的 XZ 钳制改读③（矩形，仍是凸集，保留「起点/终点在界内 ⇒ 线段全程在界内」论证）；
  相机 Y 改为 `Terrain.SampleHeight(x,z) + eyeHeight`（取代硬编码 1.7m）—— 西/东/南已越出平坦区，外缘地面高 ≈0.35m。
- **召唤**：`DinosaurAgent.Summon` 的 `approachDestination` 的 XZ 钳进②（**唯一一处恐龙侧行为改动**）。不钳制时
  玩家站到新范围外缘召唤会把恐龙拖到外围缓坡上，而它 Y 恒为 `MovementHeight` → 明显陷地（最坏 ≈1.7m）；
  钳制后玩家站在②之外时恐龙走到②边缘即停。漫步逻辑 / `WanderRadius` / 出生矩形**均未改动**。
- 回滚开关、种子链、RNG 消费序、逐层幂等写回等既有契约**均未改动**。

### 自动断言（`JurassicProjectSetup.ValidateProject`）

- **② ⊆ ①**（恐龙 Y 恒为 `MovementHeight`、`DinosaurFootLock` 接地基准只在 Start 采样一次 ⇒ 走出恒平区会陷地）。
- **③ ⊆ 地形范围**（±100m），否则相机将采样到地形外的高度。
- `Understory Pass` 全部 Renderer 中心到**②** 的距离 ≥ `DinoSafetyBuffer − 0.05m`。
- **低矮植被梯度**：R0 空地内（`sd ≤ -ClearingInset + 0.05`）bush/fern 层 64×64 抽样恒 0；且 R1/R2 抽样非零格数 > 0。
- 日志含四边界摘要（`[Validate] 四边界：…`，与 Bake 日志 / 验收报告共用 `WorldBounds.Describe()` 的同一份措辞）。

### 机器验证（`SpatialRebalanceBakeVerify`）

菜单 `Jurassic World/Acceptance/Spatial Rebalance/Bake + 影响对账`，或批处理
`-executeMethod JurassicWorld.Editor.SpatialRebalanceBakeVerify.Run`。它在**同一进程内**执行
「快照 → Bake#1 → 快照 → 逐项对账 → Bake#2 幂等 → `ValidateProject`」，报告写入
`Logs/spatial_rebalance/accounting.txt`，合格标志 `JURASSIC_SPATIAL_REBALANCE_OK`。

对账口径：

| 产物 | 预测 |
| --- | --- |
| heightmap / treeInstances / grass 层 / `Understory Pass` 实体位置 | **0 变化** |
| bush 层 | 仅在 `sd < 19.5m` 处变化 |
| fern 层 | 变化局限在内侧带（含既有林缘簇外溢） |
| R0 空地 | 前后恒 0 |
| `JurassicTerrainData.asset`（连续两次 Bake） | 字节级一致（`Main.unity` 的 fileID 重排不作判据） |

### 本次实测结果（2026-09-23，批处理 `-executeMethod JurassicWorld.Editor.SpatialRebalanceBakeVerify.Run`）

报告 `Logs/spatial_rebalance/accounting.txt`，日志 `Logs/spatial_rebalance_verify.log`。

| 对账项 | 预测 | 实测 |
| --- | --- | --- |
| heightmap | 0 变化 | **0 格**（最大 \|Δ\| = 0） |
| 树实例 | 0 变化 | 1985 → 1985，位置摘要一致 |
| `Understory Pass` 实体 | 0 变化 | 239 → 239，位置摘要一致（岩石 151 / hero 灌木 39 / deadwood 13） |
| grass 层 | 0 变化 | **0 格** |
| bush 层 | 仅 `sd < 19.5m` 处变化 | 635087 格变化，变化格最大 sd = **19.45m** |
| fern 层 | 局限在内侧带 | 25559 格变化，最大 sd = **9.69m** |
| R0 空地 | 前后恒 0 | 非零格 0 |
| 两次 Bake | 字节级一致 | TerrainData MD5 `5EF98FB1…` 两次完全相同 |
| `ValidateProject` | 全绿 | PASS（`JURASSIC_MVP_VALIDATION_OK`，树 1985 / 实体 227+ / 梯度 bush 6 / fern 4 层） |

逐环非零格（bush + fern 合计，前 → 后）：

```
R0 空地  sd ∈ [-150, -50) :       0 →       0
R1 外缘  sd ∈ [ -50,   0) :       0 →  359315     ← 中央空洞由这里填上（活动包络外缘稀疏低矮植被）
R2 过渡带 sd ∈ [   0,   8) :    2118 →  184055     ← 玩家可走的过渡带
R3 林缘内 sd ∈ [   8,  19.5):  221417 →  298396     ← 林缘加密
R4 密林  sd ∈ [ 19.5,  45) :  283053 →  283053     ← 逐格未动
R5 远林  sd ∈ [  45, 150) :       0 →       0
```

即：改动恰好落在 R1–R3 的梯度带上，**密林（R4）与外圈逐格零改动**；fern 内侧带 160/160 簇（独立流 salt `271837`），
fern 总目标密度 Σ=210982、各 variant Σ 守恒 OK；第 2 次 Bake 全部层 `与现有一致跳过`。

### 回滚演练与状态不动点（2026-09-23）

1. **回滚**：`UnderstorySeeder.EnableUnderstoryPass` 临时置 `false` → Bake：日志
   `[UnderstorySeeder] 回滚完成：根节点不存在；fern 原型剥离 4 个（index=7,8,9,10）；bush 层恢复 6 个`；
   场景内 `Understory Pass` 引用数 **0**；TerrainData → `86C1B551…`。**回滚契约成立**（含本次新填入 R1/R2 的 bush 密度被基线重放覆盖）。
2. **再启用**：置回 `true` → Bake：bush 6 层 + fern 4 层重新写回，fern 总目标密度 Σ=210982、逐层
   Σdensity=[52742,52848,52513,52879] 与首次 Bake **逐项相同**；实体清单（岩石 151 / hero 灌木 39 / deadwood 13）一致。
3. **状态不动点**：以当前产物为起点再跑一轮「Bake → 对账 → Bake」：**全部层 0 格变化**、逐环非零格前后完全相同、
   两次 Bake 的 TerrainData 字节哈希相同 → 当前状态是确定性不动点（幂等成立）。

> ⚠️ **已知差异（记录，未修）**：首次 Bake（旧态 → 新态）的 TerrainData 哈希 `5EF98FB1…` 与「回滚后再启用」的
> `A7201861…` 不同（同尺寸，中段约 53853 字节差异），尽管两者的逐层 Σdensity / 非零格 / 环带分布 / 实体清单
> **完全一致**。两次进入路径的唯一代码差异是 fern `DetailPrototype` 走的两条既有分支：首次为「规格相符 → 沿用既有对象」，
> 回滚后为「无自有原型 → `BuildFernPrototype` 重新构造」（本次未改动这两条分支）。故判定为**进入路径带来的序列化差异，
> 不是内容漂移**；`A7201861…` 是稳定不动点。若要恢复字节级「往返零漂移」，应让 `FernPrototypeMatchesSpec`
> 覆盖 `BuildFernPrototype` 设置的全部字段（当前只比对 usePrototypeMesh / prototype / renderMode / density），
> 并让 reuse 路径也以同一构造点写回。

### 人工 Play Mode 检查（视高 = 地形高 + 1.7m）

1. **中央空地**：出生点 (0,-16) 附近仍为空草坪，bush/fern 不侵入 R0。
2. **梯度**：朝任一方向走，应依次见到「草 → 稀疏低矮植被 → fern/bush → 林缘 fern + moss 岩石 → 密林 + 倒木」。
3. **相机贴地**：走到西/东/南边缘（`|x|≈78` 或 `z≈-78`，已在平坦区外 8m），确认相机不悬空、不钻地。
4. **边界手感**：北侧（z≈88）被林线挡住；其余方向由树干阻挡，不应出现"凭空撞墙"。
5. **召唤**：站在范围边缘朝外按空格，恐龙应走到②边缘停下，而不是陷进外围坡里。
6. **恐龙不变**：漫步范围、脚步锁定、朝向、信息卡行为与本次改动前一致。

### 已知副作用（记录，不修）

- 新增的低矮 bush/fern 是 **detail 层**，不参与实体断言，也不进 `DinosaurSpawnRandomizer.CollectPropCircles`
  ⇒ 恐龙会「穿过」它们（与 grass 同性质，符合「低矮无碰撞植被」定位）。
- 玩家仍可穿过 R3 的无 Collider 岩石 / 倒木（决策：保持现状，仅记录）。
- `ForestSeeder` 的核心矩形 `x[-60,60] z[-50,82]` 与②**不是**同一概念（林缘另有 5m 硬隔离带 + 18m 密度坡宽）；
  本 Pass 不合并它们，只在 `WorldBounds` 与各类注释里注明各自职责。

### 失败排查

- 若 `ValidateProject` 抛「R0 开阔空地内存在非零密度」：门或终态硬钳制被改动 —— 查
  `WorldBounds.LowVegGate01`、`UnderstorySeeder.PaintDetailLayers` 的 bush 终态钳制、`ApplyFernGate` 的 fern 钳制。
- 若抛「恐龙活动包络未包含于地形平坦区」：`DinosaurSpawnRandomizer.Spawn*` 或 `DinosaurAgent.WanderRadius`
  被调大 —— 二者经 `WorldBounds.DinosaurActivityXZ` 联动到全部植被安全边界，必须先扩①或缩回。
- 若抛「R1/R2 梯度带抽样密度全为 0」：梯度未生效（门被压死 / 层识别失败），查 Bake 日志
  `[UnderstorySeeder] 密度场…` 与 `[WorldBounds]` 摘要。
- 若相机在边缘悬空/钻地：`SmoothExplorerCamera` 是否拿到 Terrain（日志会警告「场景无 Terrain」）。

## 恐龙不规则路线验证

- 六条路线由 `MovementCorridor.LayoutSeed`（与地表生成共用的固定种子）生成：四条大小不同的不规则环线、两条去回程分开的斜向闭合环线。周期曲线平滑连接，交叉处保持各自路线，没有端点反向。
- 只刷新路线时使用菜单 `Jurassic World > Environment > Routes > Rebuild Dinosaur Routes`。它会按已启用恐龙的实际行走动画、模型长宽与路线朝向扫掠，加 1 米余量，只移开树干、低处木质枝干及岩石；允许树叶、细枝和灌木轻微交叠，单次移位限制 12 米，再重绘地表土路；保留恐龙数量、速度及地图主体布局。完整 Bake 也使用同一份路径几何。
- 批处理验收：`Unity.exe -batchmode -projectPath <project> -executeMethod JurassicWorld.Editor.DinosaurRouteAcceptance.Run -logFile Logs/routes-all-acceptance.log`，不要加 `-quit`，测试完成后会自动退出。
- 验收覆盖 32 个种子的可复现性、边界、自交、环线接缝，以及当前地图的模型净空；Play Mode 分三组覆盖全部六条路线，共模拟 1200 秒，检查多圈闭合行走、贴线、瞬移、持续阻塞和同伴间距。成功日志为 `DINOSAUR_ROUTE_GEOMETRY_OK` 和 `DINOSAUR_ROUTE_MOVEMENT_OK`；地图预览输出到 `Logs/routes-map.png`。
- 密林修复记录：`Logs/forest-restore.log`。仅从原始版本恢复匹配的障碍物位置，不整体回滚资产。1972 棵地形树保留数量，1942 棵保留原位置；局部避让移动 30 棵地形树、30 个场景障碍物，最大 11.25 米。木质与岩石使用实际子网格，排除叶片材质和高处树冠；关联景观组随岩石移动，但不把叶片和灌木算入岩石包围范围。

- 本次通过：`Logs/forest-acceptance.log`（32 种子、六条路线共 1200 秒），`Logs/forest-refresh-preview.log`（重复刷新位置及数量、恐龙速度、地形高度和溪流河岸稳定）。预览：`Logs/routes-map.png`、`Logs/routes-forest-0.png`、`Logs/routes-forest-1.png`。

## 中心密林加密第一阶段（修正前检查点）

- 已保存 Main，中心 X[-60,60] Z[-50,82] 共 399 棵乔木；加密组 283 棵，秋叶树 100 棵（25.1%）。按扣除必要路线净空后的 7084m² 可种植面积，密度达到外围 13 个样方中位值的 65.1%；完整数据见 `Tools/VegetationDiversity/CentralForest/DensityReport.md`。
- `Jurassic World > Environment > Central Forest` 提供第一阶段构建、确认后追加第二阶段、验证和撤销；Play 开销比较位于 `Jurassic World > Diagnostics > Central Forest`。`Assets/Editor/CentralForestStage.json` 固定为阶段 1；第二阶段没有运行。用户自行 Editor Play 视觉确认，不生成截图。
- `Logs/central-forest-stage1.log`：落地、材料、LOD 引用、溪流避让及全部木质障碍的动画身体扫掠 + 1m 净空通过。`Logs/central-forest-repeat.log`：重复构建两次，位置、数量及 Terrain 树不变。
- `Logs/central-forest-ownership.log`：在内存中撤销加密并恢复原五树预览，再通过持久化阶段的 Bake 钩子重建，树木及 580 个林下实例的位置完全一致；原有对象和地形树保留，阶段仍为 1。测试不保存场景。
- `Logs/central-forest-routes-graphics.log`：32 个路线种子几何检查通过；全部六条路线累计模拟 1200 秒，多圈行走、同伴净空、无卡住或路线偏离通过。无图形模式不适用于现有地图渲染，因此 Play 验收使用正常图形设备。
- `Tools/VegetationDiversity/CentralForest/PlayPerformance.md`：实际 Play 三个相同视点，1280×720，CPU 渲染提交中位 11.74 → 15.05ms；GPU 计时不可用，不将这些结果解释成发布版 FPS。
- 相对于 `Tools/VegetationDiversity/Checkpoints/Main_BeforeCentralForest.unity`，1890 个原有序列化对象全部保留；仅五树预览的根对象/Transform（转为不激活备份）和 SceneRoots 列表发生变化，其余原有对象字节不变。溪流 TerrainData SHA256 保持 `B2688C1C0C35A04323FDC6A193580415E2636C74719AD7EFD269B7FE3FE836D0`。

## 第一阶段修正：西北秋叶林与路线林冠（修正前检查点）

- Main 已保存，阶段仍为 1。西北主体区占全地形投影面积 25.11%（2m网格），18m渐变边缘及14m稀疏过渡；这是区域面积指标，不再对秋叶树设置25%的数量配额。主体区秋叶树395棵，过渡区48棵，远离过渡区0棵。报告：`Tools/VegetationDiversity/CentralForest/NorthwestRegion.md`。
- 355个 Terrain 树实例及18棵原场景树等量转换，完整原字段/原索引及原父节点、局部变换保存在 `ForestRegionBackup` 中。原场景树作为不激活备份保留。中心共399棵（65.1%外围参考密度），秋叶104棵，仅统计、不设比例配额；新增林下实例558个。
- 树群优先采样路线安全包络外侧1–5m；另有10棵高冠树覆盖路线观察位置。18个固定观察段的树冠投影覆盖由1/18增至14/18，两侧12m内都有树干的段数由9/18增至12/18。4个林冠空缺及6个两侧树干空缺保留必要净空，详见 `RouteEdges.md` 与 `RouteEdgesBeforeRevision.md`；这些是LOD0几何估计，不是叶片像素覆盖，实际视觉由用户自行Editor Play确认。
- `Logs/northwest-route-canopy.log`：全部树落地、溪流避让、材质及LOD引用、0.5m高度层动画身体扫掠+1m净空通过。`Logs/northwest-route-regression.log`：高处硬枝允许、身体高度硬枝拒绝、软叶片允许、路线树干拒绝；精确恢复原Terrain树数组，地形高度、溪流/地表alphamap、外围prototype数组不变；撤销及持久化Bake钩子确定性重建通过，未启用阶段2，测试不保存场景。
- `Logs/northwest-route-play.log`：正常图形设备下，32种子几何检查及全部六条路线1200秒运动验收通过，无卡住、路线偏离或同伴重叠；无截图。
- `Logs/northwest-route-repeat.log`：连续重复构建两次通过，树木数量、位置及剩余 Terrain 树数组完全一致，没有重复植被或累积替换。
- `Logs/northwest-route-performance.log` 与 `PlayPerformance.md`：相同三视点、1280×720、实际Play，CPU渲染提交中位6.14 → 11.28ms（+5.14ms）。基线在内存副本中恢复区域原树及预览，未修改保存的场景/地形；GPU计时不可用，不解释为发布版FPS。
- 修改前检查点为 `Tools/VegetationDiversity/Checkpoints/Main_BeforeNorthwestRouteRevision.unity` 和 `MainCreekTerrain_BeforeNorthwestRouteRevision.asset`；恢复修正前状态需要同时恢复这两个文件。最终Main SHA256为 `DB50BAA043BC5DD3926D0127B224FEB815275340420B84F95E86100CCF9C009C`；修改后地形树资产为 `4BD0A66E3F4D6CDC948AEE07EB965C6E9DC8D5A05DF2393CB5293AE04A842FA8`。原有1890个基础场景序列化对象全部保留，32个变化均属于预览备份、区域替换原树的父关系及相关父节点/根列表，其余原有对象字节一致。

## 第一阶段再次修正：20%秋叶林与核心身体净空

- Main 已保存，阶段1、布局版本4，未执行阶段2、无截图。新西北主体面积20.02%（2m地面网格），18m渐变及14m稀疏过渡。新主体内修正前337棵秋叶树，保留169棵；多余秋叶等量换成绿树，过渡区45棵，远处0棵。原Terrain树313个及原场景树16棵等量转换；完整原实例、原父节点及局部变换可恢复。
- 中心399棵保持不变；按原7084m²标定口径为外围参考密度65.1%。新核心净空使可种植面积扩大到10608m²，同样399棵按新口径是43.5%，不满足新口径60%～70%；达到新口径65%需额外198棵。本次按已确认“不增加中心总棵数”执行，两种口径均明确记录，未以总量变化凑指标。
- 明确映射当前地面恐龙的躯干/髋部蒙皮骨骼，按核心骨骼总权重≥0.5裁切表面并采样实际行走动画16帧及初始姿态，生成0.5m高度层。编辑器和运行时共享序列化核心包络与树干截面：零额外留距，仍补偿路线采样和转弯扫掠；头尾、四肢、枝叶不扩大树禁区。运行时圆形查询只做空间筛选，实际阻挡由方向性核心包络判定。岩石/倒木保留全身体+1m，出生及恐龙同伴规则保留。
- 路线植树优先安全包络外0～2m，再延伸到5m。18个观察段两侧均有12m内树干；两侧树干距离中位8.15→3.4m，树冠投影14/18→17/18。最近树干距核心包络保守估计0.33～2.43m，中位0.655m，逐段列于RouteEdges.md。冠层是界框/外接圆几何估计，实际视觉需自行Editor Play确认。
- 新增路线点缀373个，草0.15～0.5m，蕨/灌木0.4～0.8m；无Collider、无SceneTreeObstacle，不触发树干绕行。另有697个森林林下实例。既有草纹理和双面裁切Shader复用，新增轻量交叉草簇网格，不下载资源，不改Terrain detail数据。
- `Logs/core-forest-final-build.log`：落地、溪流、材质、LOD引用、核心树干与全身体岩石净空通过。`Logs/core-forest-final-regression.log`：核心内部拒绝树干、边界外无额外留距、枝叶允许、路线点缀无碰撞通过；精确恢复原Terrain树数组，地形高度/溪流图层/外围原型不变；撤销及保存阶段的Bake钩子重建位置一致；连续重复构建两次无重复或再次减半。检查均在内存中进行，未保存测试场景。
- `Logs/core-forest-play.log`：正常图形设备、32种子几何检查和六条路线1200秒运动验收通过，逐帧核心身体与树干无相交，无卡住、瞬移、偏离路线或恐龙同伴重叠。原截图文件时间保持2026-10-06 11:54:17。
- 本次前检查点：`Tools/VegetationDiversity/Checkpoints/Main_BeforeCoreRouteRevision.unity` 与 `MainCreekTerrain_BeforeCoreRouteRevision.asset`，精确恢复本次前状态须同时恢复两者。Remove菜单恢复首次加密前的原植被，保留半数基准供后续重建，不再次按当前169棵减半。
- `Logs/core-forest-performance.log`：同一三视点、1280×720正常图形Play，CPU渲染提交合并中位7.64→13.13ms（+5.50ms，+72.0%）。基线为首次加密前植被的内存恢复，与本次前旧版第一阶段的绝对值不可直接当作同次对比；旧版报告另存PlayPerformanceBeforeCoreRevision.md。GPU计时不可用，不解释为发布版FPS。
- 性能及运动测试后资产哈希不变：Main `DE90B52F290C969506EA83DD40EDABF48B3800E8AB3F38219C8FC49F4DD81058`，TerrainData `2F9F4AB83ADBBDB66CB405BA2C5BD0F7D23F4DD292D6DBBABABD277F190CA18C`。相对首次加密前检查点1890个原有序列化对象全部保留；113个变化包含76个树干截面注册数据、区域替换原树/预览的父关系、相关父节点/根列表及恐龙核心包络组件引用。未改未涉及对象的世界位置、尺寸和方向；注册数据变化为本次碰撞规则所需。
