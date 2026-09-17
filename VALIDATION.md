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

1. **地形平坦性**：在恐龙活动区（中央 ±47m）行走一圈，目视确认地面完全平坦，恐龙脚部无穿地、无悬空，落脚锁定表现正常。
2. **植被渲染**：确认树木与草地颜色自然，**非粉色/紫色**（粉色为 Unity shader 编译失败标志，提示 Nature Starter Kit 2 shader 与当前渲染管线不兼容）。
3. **Wind Zone 生效**：静置观察树冠约 5 秒，确认有轻微自然摆动，而非完全静止（验证 `WindZone` 组件已挂载并影响 Tree Creator shader）。
4. **Skybox 自然无接缝**：抬头观察天空，确认显示渐变蓝天/云层（**非默认纯灰色或纯黑色**），且**画面中不存在竖直接缝线、云不被直线切断**（验证 `Assets/Materials/NaturalSkybox.mat`（Skybox/Panoramic 经纬度全景 + `NatureSkyPanorama.tga`）已绑定到 `RenderSettings.skybox`；旧 NSk2 cubemap 天空 `skybox.mat` 存在面缝伪影，已不再使用）。
5. **外围起伏**：将相机推至地图边缘（±90m 附近），确认外围地形有轻微起伏但恐龙不会越界，边缘被远景树木自然遮挡。
6. **Bake 幂等性**：连续执行两次 `Jurassic World > Bake Scene`，确认第二次运行后树木与草地实例数量**不变**（不重复播种），恐龙位置、相机、光照正常重建。第二次的 Editor 日志应出现 `[ForestSeeder] 结果与现有实例一致，跳过写回`（固定种子下逐字段比对命中，跳过 `treePrototypes` 赋值与 `SetTreeInstances`，避免重写 ~3MB 二进制资产并重烘树 billboard）。
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

### 像素探针（临时工具，验证后可删除 `Assets/Editor/SkyProbeCapture.cs`）

- 命令：`Unity.exe -batchmode -quit -projectPath <proj> -executeMethod JurassicWorld.Editor.SkyProbeCapture.Capture`（或菜单 Jurassic World > Capture Sky Probe）。
- 探针以默认机位环绕 8 个方位各渲染一帧（`Logs/sky_probe_yaw*.png`），统计天空带逐列亮度均值的最大相邻差（已剔除树木暗列），输出 `SKY_SEAM_PROBE worstYaw/maxVerticalStep` 日志；<5 视为干净，>15 视为明显接缝。
- 最差方位附带环绕列 ±6 列亮度剖面（区分"平均色条带"与"真实内容台阶"）。
- 探针会临时关闭天空贴图 mip 重渲染同一方位做 A/B（`NOMIP_AB` 日志 + `Logs/sky_probe_nomip.png`），验证后自动恢复导入设置；若 mip=off 后台阶显著下降，说明残留属经纬度环绕列 mip 采样伪影（Bake 已强制关闭 mip）。

## Understory Pass 验证（林下层植被 / 岩石 / 资产门控接口）

### 背景与所有权

- `UnderstorySeeder`（`Assets/Editor/UnderstorySeeder.cs`，种子链第 5 段 `271833`）在 `TerrainEnvironmentSeeder.PopulateGround` 之后执行：重绘现有 7 个 detail 层密度（bush01-06 mesh 层 + grass01 billboard 层，不增删原型）+ 重建单一根节点 `Understory Pass`（子节点 Rocks / Vegetation / Deadwood；每次 Bake 先整根 `DestroyImmediate` 再重建，绝不叠加）。
- **安全缓冲不变量**：恐龙漫步包络 ⊆ 平坦矩形（x∈[-70,70]、z∈[-70,90] 世界坐标）；所有中大型实体道具（rock / hero bush / log / stump）位于平坦矩形 + `DinoSafetyBuffer`（默认 7.5m）之外；buffer 内 bush detail 密度恒 0（终态硬钳制）。碰撞规避不依赖运行时避让（未改 `DinosaurSpawnRandomizer`），而是靠上述几何不变量。
- **资产门控**：`Assets/Vegetation/Ferns` / `Rocks` / `Deadwood` 目录不存在或为空时，对应子系统 no-op（仅日志提示目录），其余子系统不受影响；放入合格 prefab 后重 Bake 自动生效（岩石簇分布逻辑无论有无资产都按固定种子确定性执行，仅供植被邻域增强与未来实例化）。

### 自动断言（`JurassicProjectSetup.ValidateProject`）

- 启用时（`UnderstorySeeder.PassEnabled == true`）场景必须存在 `Understory Pass` 根节点，否则抛 `InvalidDataException`。
- **实体缓冲不变量**：`Understory Pass` 下全部 Renderer 中心到平坦矩形的距离 ≥ `DinoSafetyBuffer − 0.05m`（bounds 容差），违反即抛异常。
- **bush 层密度硬钳制抽样**：`FindBushDetailLayerIndices` 识别出的 bush 层在「平坦矩形 + buffer − 0.2m」内按 64×64 抽样网格逐格断言密度为 0，违反即抛异常。
- 禁用时（回滚态）若 `Understory Pass` 根仍存在 → 告警提示重 Bake 完成回滚。
- `DebugProceduralRocks == true` 时告警「仅限分布验证，正式 Bake 禁止」。
- fern / deadwood / rock 接口状态（prefab 数量）并入 `JURASSIC_MVP_VALIDATION_OK` 行与信息日志。

### 人工 Play Mode 检查（玩家视高 1.7m，禁用鸟瞰 / 自由飞行观察）

1. **中央草地**：活动区内草地连续低矮（密度 2-4，出生点 20m 内减半），bush 密度恒 0；确认无裸露突兀的空白斑块。
2. **林缘重点带**：从活动区向外走，确认草层渐趋斑驳（带缝隙），bush01-06 以团簇（cluster）形态重点分布，簇间留有空隙、非均匀撒点。
3. **林内底层**：进入森林，确认草密度降低、bush 以低频斑块补底层（未来 Fern 的过渡覆盖）。
4. **泥斑与通道**：泥土斑块区植被明显减少；沿 35°/160°/285° 视觉通道方向确认可辨识的裸带。
5. **英雄灌木**：确认约 36 个放大灌木（1.0-2.2 倍）分布于岩石簇 / 树干基座 / 林缘附近，无悬浮、无穿模。
6. **重复图案检查**：确认无明显规则网格或重复噪声图案（hash dither + 与 ForestSeeder 不同频段的 cluster 噪声）。

### 幂等与回滚演练

1. **双 Bake 幂等**：连续执行两次 `Jurassic World > Bake Scene (Edit Mode)`；第二次日志应全部为 `与现有一致跳过`（bush/grass/fern 层 diff=0）；`git status` 确认 `JurassicTerrainData.asset` 无二进制 diff；`Understory Pass` 根下实例数不变。
2. **回滚演练**：将 `UnderstorySeeder.EnableUnderstoryPass` 置 `false` → 重 Bake → `Understory Pass` 根消失；bush 层逐格回到 SeedDetails 基线（日志 `bush 层恢复 N 个`，抽查 `GetDetailLayer` 与基线 helper 输出一致；基线算法与 spawn 点由 `JurassicWorldSceneBuilder.BuildBushBaselineDensity / DinosaurSpawnPoints` 同源复刻）；grass01 层由 GroundPass 同 Bake 先行重绘恢复。再置回 `true` 重 Bake，产物与「从未禁用」逐格一致（纯函数恢复，无永久覆盖）。

### DebugProceduralRocks 分布验证（可选，正式 Bake 禁止）

1. 临时将 `UnderstorySeeder.DebugProceduralRocks` 置 `true` → 重 Bake → 日志出现 `[UnderstorySeeder] [DEBUG ONLY] 程序化 Sphere 岩石 N 个已生成`。
2. 以 Play Mode 目检岩石簇分布（hero 环带 10 个扇区 + 林缘 / 坡脚加权簇；确认全部位于活动区 + 缓冲外、沉地自然、无悬浮）。
3. 确认后恢复 `false` 重 Bake；`ValidateProject` 会对此开关持续告警。

### 失败排查

- 若抛出 `Understory Pass 已启用但场景缺少 "Understory Pass" 根节点`：说明 Bake 未完成或场景被手动改动，重新 Bake。
- 若抛出 `Understory 实体 ... 位于安全缓冲内`：检查 `UnderstorySeeder` 的 `ClusterCenterMinDistance`（= buffer + 1.5 + 3.5）与 `DinoSafetyBuffer` 常量是否被调小，或簇成员散布半径是否越界。
- 若抛出 `bush 层 idx=... 在安全缓冲内存在非零密度`：检查 `PaintDetailLayers` 的终态硬钳制（`dFlat < DinoSafetyBuffer → v=0`）是否被移除或改动。
- 若日志出现 `未识别到 bush detail 层` / `未识别到 grass01 层`：检查 NSk2 是否导入（`NatureStarterKitLocator`）与 GroundPass 是否启用；bush 层签名 = NSk2 detail mesh 名称含 "bush"，grass01 层签名 = `prototypeTexture == grass01` 纹理。
- 若 fern / deadwood 接口未生效：确认 prefab 已放入对应目录（`Assets/Vegetation/...`）且名称含关键词（fern/plant；log/stump/deadwood/snag），且不命中排除词（tree/bush 等）；prefab 命名影响排序契约，跨 Bake 改名会改变实例化随机流。
