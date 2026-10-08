using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    public static class JurassicProjectSetup
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";

        // 编辑态烘焙入口（任务 #3 可用 -executeMethod JurassicWorld.Editor.JurassicProjectSetup.BakeScene 调用）。
        // 打开 Main.unity → 由 JurassicWorldSceneBuilder 建造全部内容 → 标脏并保存场景与资产。
        [MenuItem("Jurassic World/Scene/Bake Scene (Edit Mode)")]
        private static void BakeSceneMenu() => JurassicMenuGuard.Run(BakeScene, true);

        [MenuItem("Jurassic World/Scene/Bake Scene (Edit Mode)", true)]
        private static bool BakeSceneMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void BakeScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(MainScenePath))
            {
                Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(newScene, MainScenePath);
            }

            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            // Build() 在 Terrain 构建失败（如 NSk2 未导入）时会抛 InvalidOperationException；本方法【刻意不加 try/catch】——
            // 异常传出后下方的 MarkSceneDirty / SaveScene / SaveAssets / Refresh 全部跳过，
            // 磁盘上的上一次可用 Main.unity 与 JurassicTerrainData.asset 不被半成品场景覆盖。
            JurassicWorldSceneBuilder.Build();

            if (!EditorBuildSettings.scenes.Any(s => s.path == MainScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes
                    .Concat(new[] { new EditorBuildSettingsScene(MainScenePath, true) }).ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"JURASSIC_SCENE_BAKED: 已在编辑态烘焙 Main.unity（相机 / 方向光 / 天空 / 草地 / HUD / {JurassicWorldSceneBuilder.EnabledDinosaurCount} 只恐龙）。");
        }

        // 验证性重导入入口（实验用）：对 Assets/Resources/Dinosaurs 下所有 .fbx 强制重新导入，
        // 用于验证「恐龙 FBX 导入设置是否发生漂移」——若 .meta 未变、git diff 为空，则说明重导入是 no-op，
        // 排除「导入管线状态漂移导致脚部悬空」这一假设；配合 BakeScene 使用，把烘焙前的模型状态刷新到磁盘。
        [MenuItem("Jurassic World/Assets/Reimport Dinosaur Models")]
        private static void ReimportDinosaurModelsMenu() => JurassicMenuGuard.Run(ReimportDinosaurModels, false);

        [MenuItem("Jurassic World/Assets/Reimport Dinosaur Models", true)]
        private static bool ReimportDinosaurModelsMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void ReimportDinosaurModels()
        {
            const string root = "Assets/Resources/Dinosaurs";
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { root });
            Debug.Log($"[Reimport] 在 {root} 下发现 {guids.Length} 个 Model 资产，开始逐个强制重导入...");
            int index = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                index++;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"[Reimport] ({index}/{guids.Length}) ForceUpdate 重导入: {path}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"JURASSIC_REIMPORT_DONE: 共重导入 {index} 个模型资产；若 .meta 与 HEAD 零漂移，本步骤为 no-op。");
        }

        // 编辑态断言：不点 Play，打开烘焙后的 Main.unity，检查场景内已存在相机、方向光与「当前启用数量」
        // （JurassicWorldSceneBuilder.EnabledDinosaurCount）的挂 DinosaurAgent 的恐龙 GameObject。
        [MenuItem("Jurassic World/Scene/Validate Scene")]
        private static void ValidateProjectMenu() => JurassicMenuGuard.Run(ValidateProject, true);

        [MenuItem("Jurassic World/Scene/Validate Scene", true)]
        private static bool ValidateProjectMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void ValidateProject()
        {
            if (!File.Exists(MainScenePath))
                throw new FileNotFoundException("缺少已烘焙的主场景，请先执行 Jurassic World/Scene/Bake Scene (Edit Mode)。", MainScenePath);

            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            if (Object.FindObjectOfType<Camera>() == null)
                throw new InvalidDataException("场景缺少相机（Main Camera）。");

            bool hasDirectionalLight = Object.FindObjectsOfType<Light>().Any(light => light.type == LightType.Directional);
            if (!hasDirectionalLight)
                throw new InvalidDataException("场景缺少方向光（Sun）。");

            DinosaurAgent[] agents = Object.FindObjectsOfType<DinosaurAgent>();
            int expectedAgents = JurassicWorldSceneBuilder.EnabledDinosaurCount;
            if (agents.Length != expectedAgents)
                throw new InvalidDataException($"期望场景中有 {expectedAgents} 只挂 DinosaurAgent 的恐龙，实际找到 {agents.Length} 只。");

            if (agents.Any(agent => agent.GetComponent<DinosaurFbxLoader>() == null))
                throw new InvalidDataException("存在缺少 DinosaurFbxLoader 绑定器的恐龙。");

            if (!EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == MainScenePath))
                throw new InvalidDataException("Main scene is not enabled in build settings.");

            // ─── 天空验证（Panoramic 经纬度全景，无 cubemap 面缝） ───
            Material skybox = RenderSettings.skybox;
            if (skybox == null)
                throw new InvalidDataException("场景未配置天空盒材质（RenderSettings.skybox 为空）。");
            if (skybox.shader == null || skybox.shader.name != "Skybox/Panoramic")
                throw new InvalidDataException(
                    $"天空盒期望使用 Skybox/Panoramic 经纬度全景（当前 shader：{(skybox.shader != null ? skybox.shader.name : "null")}）；" +
                    "旧 NSk2 cubemap 天空（skybox.mat）在面边界处存在明显竖直接缝线，请重新执行 Bake Scene (Edit Mode)。");
            if (skybox.GetTexture("_MainTex") == null)
                throw new InvalidDataException(
                    "天空盒材质缺少 _MainTex 全景贴图（Assets/Materials/NatureSkyPanorama.tga 未就绪？请重新执行 Bake Scene (Edit Mode)）。");

            // ─── Terrain 环境验证 ───
            Terrain terrain = Object.FindObjectOfType<Terrain>();
            if (terrain == null)
                throw new InvalidDataException("场景缺少 Terrain。");
            if (terrain.terrainData == null)
                throw new InvalidDataException("Terrain 缺少 TerrainData 资产。");
            if (terrain.terrainData.terrainLayers.Length == 0)
                throw new InvalidDataException("Terrain 未配置任何 TerrainLayer（草地纹理）。");
            if (terrain.terrainData.size.x < 100f || terrain.terrainData.size.z < 100f)
                throw new InvalidDataException("Terrain 尺寸过小（期望至少 100m×100m）。");
            // 树数量断言与 ForestSeeder 回滚开关联动（不再硬编码 400）：启用森林时要求 >= 400
            // （中央 ~161 棵 + 外围森林 ~1500-2200 棵）；回滚态（森林段已被精确剥离）只要求中央段 >= 100。
            int minTrees = ForestSeeder.ForestEnabled ? 400 : 100;
            int treeCount = terrain.terrainData.treeInstanceCount;
            var foliageConversion = Object.FindObjectOfType<FoliageTerrainConversion>();
            if (foliageConversion != null) treeCount += foliageConversion.removed.Length;
            if (treeCount < minTrees)
                throw new InvalidDataException(
                    $"Terrain 树实例数不足：treeInstanceCount={treeCount} < 阈值 {minTrees}（ForestSeeder.ForestEnabled={ForestSeeder.ForestEnabled}），" +
                    "请检查 NSk2 导入与 ForestSeeder（Editor 日志搜 [ForestSeeder]）。");

            // 森林专属 prototype 区间校验 + 核心矩形零泄漏断言：合并到【单次 foreach】遍历完成，
            // 不用 LINQ Any（避免对 1500+ 元素的 treeInstances 做多次委托调度遍历）。
            // 区间基线引用 ForestSeeder.CentralTreePrototypeCount，与播种侧同源，不各自硬编码 4。
            int centralProtoCount = ForestSeeder.CentralTreePrototypeCount;
            TreeInstance[] treeInstances = terrain.terrainData.treeInstances;
            Vector3 terrainSize = terrain.terrainData.size;
            bool hasForestInstance = false;
            int coreLeakCount = 0;
            foreach (TreeInstance ti in treeInstances)
            {
                if (ti.prototypeIndex < centralProtoCount) continue;   // 中央段实例，不参与森林断言
                hasForestInstance = true;

                // TreeInstance.position 是地形归一化坐标（[0,1]³），换算回世界 XZ 后用 ForestSeeder 的同一份
                // 核心矩形定义判定（矩形常量与 IsInsideCoreRect 均为 internal，播种侧/校验侧同源）。
                Vector3 world = terrain.transform.TransformPoint(new Vector3(
                    ti.position.x * terrainSize.x, ti.position.y * terrainSize.y, ti.position.z * terrainSize.z));
                if (ForestSeeder.IsInsideCoreRect(new Vector2(world.x, world.z)))
                    coreLeakCount++;
            }

            // treePrototypes 超过中央段基线却无森林实例 → ForestSeeder 追加了 prototypes 但未写回实例，属异常状态。
            if (terrain.terrainData.treePrototypes.Length > centralProtoCount && !hasForestInstance)
                Debug.LogWarning($"[Validate] treePrototypes.Length={terrain.terrainData.treePrototypes.Length} > {centralProtoCount} " +
                    $"但无 prototypeIndex>={centralProtoCount} 的实例 — 森林追加段为空，请检查 ForestSeeder。");

            // 【不变量机器验证】中央活动区（相机钳制区 + 全部恐龙漫游包络）内必须零森林树：
            // 林缘 wobble 穿透之类的回归会在这里被直接抓到，故报错而非告警。
            if (coreLeakCount > 0)
                throw new InvalidDataException(
                    $"核心矩形 x∈[{ForestSeeder.CoreMinX},{ForestSeeder.CoreMaxX}], z∈[{ForestSeeder.CoreMinZ},{ForestSeeder.CoreMaxZ}] 内检测到 " +
                    $"{coreLeakCount} 棵森林树（prototypeIndex >= {centralProtoCount}），违反「中央活动区无森林树」不变量；" +
                    "请检查 ForestSeeder 硬隔离逻辑（必须用未经 wobble 扰动的原始距离先于 wobble 拒绝，且 dEff 钳到 >= ForestHardMargin）。");

            if (!terrain.drawInstanced)
                Debug.LogWarning("[Validate] Terrain.drawInstanced 未开启，GPU Instancing 未生效。");

            // ─── detail 渲染前提（散布模式 + 原型密度） ───
            // 「密度写对了却一个 detail 都画不出来」的历史根因是 TerrainData 的 Detail Scatter Mode：
            // CoverageMode 下 detail map 的值被解释为"覆盖比例"，实例数改由 DetailPrototype.density
            // 推导，与全项目的「每格实例数」语义（SeedDetails 基线 / PaintDetailLayers / 验收 approxInstances）
            // 冲突。故把它当不变量校验 —— 修复点见 JurassicWorldSceneBuilder.EnsureDetailInstanceCountMode。
            TerrainData detailData = terrain.terrainData;
            if (detailData.detailScatterMode != DetailScatterMode.InstanceCountMode)
                throw new InvalidDataException(
                    $"TerrainData.detailScatterMode={detailData.detailScatterMode}（应为 InstanceCountMode）：" +
                    "本项目 detail 层密度语义为「每格实例数」，CoverageMode 下会被重解释为覆盖度，" +
                    "导致 bush/grass/fern detail 静默不渲染；请重新执行 Bake Scene (Edit Mode) 让" +
                    "JurassicWorldSceneBuilder.EnsureDetailInstanceCountMode 归一化。");

            DetailPrototype[] detailProtos = detailData.detailPrototypes ?? new DetailPrototype[0];
            var zeroDensityProtos = new List<string>();
            foreach (DetailPrototype proto in detailProtos)
            {
                if (proto == null || proto.density > 0f)
                    continue;
                zeroDensityProtos.Add(proto.usePrototypeMesh && proto.prototype != null
                    ? proto.prototype.name
                    : "<texture:" + (proto.prototypeTexture != null ? proto.prototypeTexture.name : "null") + ">");
            }
            if (zeroDensityProtos.Count > 0)
                Debug.LogWarning($"[Validate] {zeroDensityProtos.Count} 个 DetailPrototype 的 density<=0" +
                                 $"（{string.Join(", ", zeroDensityProtos)}）：InstanceCountMode 下无影响，" +
                                 "但若 TerrainData 落入 CoverageMode 会直接导致 detail 不渲染（C# 构造原型须显式置 density）。");
            Debug.Log($"[Validate] detail 渲染前提：scatterMode={detailData.detailScatterMode}、" +
                      $"detailObjectDistance={terrain.detailObjectDistance:F0}m、detailObjectDensity={terrain.detailObjectDensity:F2}、" +
                      $"原型 {detailProtos.Length} 个（density<=0 的 {zeroDensityProtos.Count} 个）、drawInstanced={terrain.drawInstanced}、" +
                      $"wavingGrass strength={detailData.wavingGrassStrength:F3}/amount={detailData.wavingGrassAmount:F3}/speed={detailData.wavingGrassSpeed:F3}。");

            // ─── 四边界定义（WorldBounds 是唯一权威）：断言职责边界，防止概念再次耦合 ───
            // ① ⊇ ② 是「恐龙 Y 恒为 MovementHeight / 脚锁接地基准」这套硬编码合法的唯一前提。
            Rect flatBounds = WorldBounds.TerrainFlatXZ;
            Rect activityBounds = WorldBounds.DinosaurActivityXZ;
            Rect playerBounds = WorldBounds.PlayerExplorationXZ;
            if (activityBounds.xMin < flatBounds.xMin - 1e-3f || activityBounds.xMax > flatBounds.xMax + 1e-3f ||
                activityBounds.yMin < flatBounds.yMin - 1e-3f || activityBounds.yMax > flatBounds.yMax + 1e-3f)
                throw new InvalidDataException(
                    $"恐龙活动包络 x[{activityBounds.xMin},{activityBounds.xMax}] z[{activityBounds.yMin},{activityBounds.yMax}] " +
                    $"未包含于地形平坦区 x[{flatBounds.xMin},{flatBounds.xMax}] z[{flatBounds.yMin},{flatBounds.yMax}] 内：" +
                    "恐龙位置 Y 恒为 MovementHeight、DinosaurFootLock 接地基准只在 Start 采样一次，走出恒平区会脚悬空/陷地；" +
                    "请检查 DinosaurSpawnRandomizer.Spawn* 与 DinosaurAgent.WanderRadius（WorldBounds 由二者推导）。");

            float terrainHalf = WorldBounds.TerrainSize * 0.5f;
            if (playerBounds.xMin < -terrainHalf || playerBounds.xMax > terrainHalf ||
                playerBounds.yMin < -terrainHalf || playerBounds.yMax > terrainHalf)
                throw new InvalidDataException(
                    $"玩家探索矩形 x[{playerBounds.xMin},{playerBounds.xMax}] z[{playerBounds.yMin},{playerBounds.yMax}] " +
                    $"越出地形范围（±{terrainHalf:F0}m）：相机将采样到地形外的高度。");

            Debug.Log($"[Validate] 四边界：{WorldBounds.Describe()}");

            // 六条不规则中心线：几何、净空、交叉通行区与场景标记同时验收。
            float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
            if (MovementCorridor.RouteCount != 6)
                throw new InvalidDataException("Phase 1 必须恰有六条中心线。");

            Collider[] solidObstacles = Object.FindObjectsOfType<Collider>()
                .Where(c => c.enabled && !c.isTrigger && !(c is TerrainCollider) &&
                    c.GetComponentInParent<DinosaurAgent>() == null).ToArray();

            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            {
                string routeName = MovementCorridor.GetRouteName(route);
                if (!MovementCorridor.IsClosed(route))
                    throw new InvalidDataException($"路线「{routeName}」必须是连续闭合环线。");
                MovementCorridor.CorridorMetrics metrics = MovementCorridor.Measure(route);
                if (MovementCorridor.Length(route) < MovementCorridor.MinRouteLength)
                    throw new InvalidDataException($"路线「{routeName}」短于 {MovementCorridor.MinRouteLength}m。");
                Rect bounds = metrics.CenterlineBounds;
                if (bounds.xMin < activityBounds.xMin || bounds.xMax > activityBounds.xMax ||
                    bounds.yMin < activityBounds.yMin || bounds.yMax > activityBounds.yMax)
                    throw new InvalidDataException($"路线「{routeName}」超出恐龙活动区域。");
                if (bounds.xMin < ForestSeeder.CoreMinX || bounds.xMax > ForestSeeder.CoreMaxX ||
                    bounds.yMin < ForestSeeder.CoreMinZ || bounds.yMax > ForestSeeder.CoreMaxZ)
                    throw new InvalidDataException($"路线「{routeName}」超出森林核心区域。");
                if (metrics.SwingX < MovementCorridor.MinSwingMeters)
                    throw new InvalidDataException($"路线「{routeName}」横向摆幅不足。");
                if (MovementCorridor.IsClosed(route) && (metrics.SeamGap > 0.8f || metrics.SeamTurnDegrees > 15f))
                    throw new InvalidDataException($"路线「{routeName}」闭合接头不连续。");

                int count = MovementCorridor.SampleCount(route);
                for (int i = 0; i < count; i++)
                {
                    MovementCorridor.CorridorSample sample = MovementCorridor.GetSample(route, i);
                    if (TerrainTreeObstacleRegistry.IsBlocked(sample.Position, DinosaurAgent.TreeAvoidRadiusCap))
                        throw new InvalidDataException($"路线「{routeName}」弧长 {sample.Distance:F1}m 被树干阻挡。");
                    foreach (Collider obstacle in solidObstacles)
                    {
                        Bounds b = obstacle.bounds;
                        Vector3 nearest = b.ClosestPoint(new Vector3(sample.Position.x, b.center.y, sample.Position.y));
                        float dx = nearest.x - sample.Position.x;
                        float dz = nearest.z - sample.Position.y;
                        if (dx * dx + dz * dz < DinosaurAgent.TreeAvoidRadiusCap * DinosaurAgent.TreeAvoidRadiusCap)
                            throw new InvalidDataException($"路线「{routeName}」弧长 {sample.Distance:F1}m 被实体 {obstacle.name} 阻挡。");
                    }
                    int segmentCount = count - (MovementCorridor.IsClosed(route) ? 0 : 1);
                    for (int j = i + 2; i < segmentCount && i < segmentCount; j++)
                    {
                        if (MovementCorridor.IsClosed(route) && i == 0 && j == count - 1) continue;
                        Vector2 a = sample.Position;
                        Vector2 b = MovementCorridor.GetSample(route, (i + 1) % count).Position;
                        Vector2 c = MovementCorridor.GetSample(route, j).Position;
                        Vector2 d = MovementCorridor.GetSample(route, (j + 1) % count).Position;
                        float abC = Cross(b - a, c - a), abD = Cross(b - a, d - a);
                        float cdA = Cross(d - c, a - c), cdB = Cross(d - c, b - c);
                        if (abC * abD < -1e-6f && cdA * cdB < -1e-6f)
                            throw new InvalidDataException($"路线「{routeName}」中心线自交。");
                    }
                }

                foreach (TreeInstance tree in treeInstances)
                {
                    Vector3 world = terrain.transform.TransformPoint(new Vector3(
                        tree.position.x * terrainSize.x, tree.position.y * terrainSize.y, tree.position.z * terrainSize.z));
                    Vector2 treeXZ = new Vector2(world.x, world.z);
                    float limit = Mathf.Max(MovementCorridor.CenterlineTreeClearance,
                        DinosaurAgent.TreeAvoidRadiusCap + 0.7f * tree.widthScale + 0.3f);
                    if (MovementCorridor.MinDistanceToRoute(treeXZ, route) < limit - 0.01f)
                        throw new InvalidDataException($"路线「{routeName}」中心线附近有树 ({world.x:F1},{world.z:F1})。");
                }
            }

            if (MovementCorridorCrossings.ZoneCount < 2)
                throw new InvalidDataException("不规则路线缺少预期的交叉通行区。");

            RouteObstacleClearance.Validate(terrain);

            GameObject corridorRoot = GameObject.Find(MovementCorridorDebug.RootNodeName);
            if (corridorRoot == null || corridorRoot.GetComponent<MovementCorridorDebug>() == null)
                throw new InvalidDataException("场景缺少可开关的 MovementCorridorDebug 标记；请重新 Bake Scene。");
            Debug.Log($"[Validate] 六条不规则中心线合格；交叉区 {MovementCorridorCrossings.ZoneCount} 个，树木净空、接头与 F3 标记合格。 {MovementCorridor.Describe()}");
            // ─── Understory Pass 验证（几何安全不变量 + 视觉梯度 + 资产门控状态） ───
            int understoryEntityCount = 0;
            int bushLayerCount = 0;
            int fernLayerCount = 0;
            if (UnderstorySeeder.PassEnabled)
            {
                GameObject understoryRoot = GameObject.Find(UnderstorySeeder.RootNodeName);
                if (understoryRoot == null)
                    throw new InvalidDataException(
                        "Understory Pass 已启用但场景缺少 \"Understory Pass\" 根节点，请重新执行 Bake Scene (Edit Mode)。");

                // 实体不变量：全部 Renderer 中心到【恐龙活动包络】的距离 ≥ DinoSafetyBuffer（留 5cm bounds 容差）。
                // 恐龙漫步包络 ⊆ 活动包络，本条成立即保证中大型实体道具（rock / hero bush / log / stump）与恐龙零接触。
                // 注：落位门限更严（成员 9.0 = buffer + PlacementExtraMargin、簇心 12.5），此处断言的是
                // 「渲染包围盒中心的有效净距」，两者是不同层级的保证。
                Renderer[] understoryRenderers = understoryRoot.GetComponentsInChildren<Renderer>(true);
                understoryEntityCount = understoryRenderers.Length;
                foreach (Renderer renderer in understoryRenderers)
                {
                    Vector3 center = renderer.bounds.center;
                    float distToActivity = UnderstorySeeder.DistanceToDinosaurActivity(center.x, center.z);
                    if (distToActivity < UnderstorySeeder.DinoSafetyBuffer - 0.05f)
                        throw new InvalidDataException(
                            $"Understory 实体 \"{renderer.name}\" 位于安全缓冲内（距恐龙活动包络 {distToActivity:F2}m < {UnderstorySeeder.DinoSafetyBuffer}m）：" +
                            "违反「恐龙活动包络 + 缓冲外无中大型实体」不变量，请检查 UnderstorySeeder 簇心余量常量。");
                }

                // 低矮无碰撞植被（bush / fern detail）的抽样断言：
                //  (a) R0 开阔空地（活动包络内缩 ClearingInset）内密度必须恒 0；
                //  (b) R1/R2（空地外 → 玩家过渡带外沿）必须有非零格 —— 否则梯度静默 no-op（正是本次要避免的失效模式）。
                int[] bushLayers = UnderstorySeeder.FindBushDetailLayerIndices(terrain.terrainData);
                bushLayerCount = bushLayers.Length;
                int[] fernLayers = UnderstorySeeder.FindFernDetailLayerIndices(terrain.terrainData);
                fernLayerCount = fernLayers.Length;

                TerrainData td = terrain.terrainData;
                int detailRes = td.detailResolution;
                int stride = Mathf.Max(1, detailRes / 64); // 64×64 抽样网格（约 4096 采样点 / 层）
                float half = td.size.x / 2f;
                int gradientNonZero = 0;
                foreach (int layerIdx in bushLayers.Concat(fernLayers))
                {
                    string kind = bushLayers.Contains(layerIdx) ? "bush" : "fern";
                    int[,] layer = td.GetDetailLayer(0, 0, detailRes, detailRes, layerIdx);
                    for (int z = 0; z < detailRes; z += stride)
                    for (int x = 0; x < detailRes; x += stride)
                    {
                        float wx = (float)x / detailRes * td.size.x - half;
                        float wz = (float)z / detailRes * td.size.z - half;
                        float sd = UnderstorySeeder.SignedDistanceToDinosaurActivity(wx, wz);
                        if (sd <= -WorldBounds.ClearingInset + 0.05f && layer[x, z] != 0)
                            throw new InvalidDataException(
                                $"{kind} 层 idx={layerIdx} 在 R0 开阔空地内存在非零密度（格 ({x},{z}) 密度 {layer[x, z]}，到活动包络 {sd:F1}m）：" +
                                $"违反「空地（活动包络内缩 {WorldBounds.ClearingInset}m）内低矮植被恒 0」不变量，" +
                                "请检查 UnderstorySeeder.PaintDetailLayers 的门与终态硬钳制（及 fern 的 ApplyFernGate 钳制）。");
                        if (sd > -WorldBounds.ClearingInset && sd <= WorldBounds.PlayerTransitionOuter && layer[x, z] != 0)
                            gradientNonZero++;
                    }
                }

                // 梯度非空断言：R1/R2 必须有低矮植被，否则「从中央向外围连续的视觉梯度」这一交付物未生效。
                if ((bushLayers.Length > 0 || fernLayers.Length > 0) && gradientNonZero == 0)
                    throw new InvalidDataException(
                        $"R1/R2 梯度带（活动包络外缘 → 玩家过渡带外沿 {WorldBounds.PlayerTransitionOuter:F0}m）内 bush/fern 抽样密度全为 0：" +
                        "视觉梯度未生效（门被硬钳制压死？），请检查 WorldBounds.LowVegGate01 与 UnderstorySeeder 的门接线。");
                Debug.Log($"[Validate] 低矮植被梯度：R0 空地（内缩 {WorldBounds.ClearingInset:F0}m）内抽样密度恒 0；" +
                          $"R1/R2 抽样非零格 {gradientNonZero} 个（bush 层 {bushLayers.Length} / fern 层 {fernLayers.Length}）。");
            }
            else if (GameObject.Find(UnderstorySeeder.RootNodeName) != null)
            {
                Debug.LogWarning("[Validate] UnderstorySeeder 已禁用但场景中仍存在 \"Understory Pass\" 根：请重 Bake 完成回滚。");
            }

            // ─── 展示环可见性断言（近林带布局 + 道具落位） ───
            // 契约：实体道具必须位于平坦矩形 + DinoSafetyBuffer 之外，而该环带正是 ForestSeeder 的全密度林区
            // （道具会被树冠挡死）。故「展示位落在近林带 [buffer, ShowcaseBandOuter]、ForestSeeder 按同一份布局
            // 让开清空圆 + 视线通道、UnderstorySeeder 在这些位上落道具」三者任一失效，玩家就看不到实体道具。
            if (UnderstorySeeder.PassEnabled)
            {
                List<UnderstorySeeder.ShowcaseSpot> showcaseRing = UnderstorySeeder.BuildShowcaseRing();
                if (showcaseRing.Count == 0)
                    throw new InvalidDataException(
                        "Understory 展示环为空：近林带将没有任何可见道具（检查 ShowcaseBorderMargin 与平坦矩形/地形尺寸的关系）。");

                int showcaseRocks = 0, showcaseLogs = 0, showcaseStumps = 0;
                foreach (UnderstorySeeder.ShowcaseSpot spot in showcaseRing)
                {
                    if (spot.FlatDistance < UnderstorySeeder.DinoSafetyBuffer - 0.05f ||
                        spot.FlatDistance > UnderstorySeeder.ShowcaseBandOuterMeters + 0.01f)
                        throw new InvalidDataException(
                            $"Understory 展示位 ({spot.Center.x:F1},{spot.Center.y:F1}) 距平坦矩形 {spot.FlatDistance:F2}m，" +
                            $"越出近林带 [{UnderstorySeeder.DinoSafetyBuffer}m, {UnderstorySeeder.ShowcaseBandOuterMeters}m]：" +
                            "请检查 UnderstorySeeder.BuildShowcaseRing 的距离带与 ShowcaseBorderMargin 常量。");

                    if (spot.Kind == UnderstorySeeder.ShowcaseKind.Rock) showcaseRocks++;
                    else if (spot.Kind == UnderstorySeeder.ShowcaseKind.Log) showcaseLogs++;
                    else showcaseStumps++;
                }

                // 道具必须真的落在展示位上（Preview 按确定性前缀缩小规模，故只在生产规模下断言）
                if (UnderstorySeeder.PreviewScale == 1)
                {
                    GameObject rocksRoot = GameObject.Find(UnderstorySeeder.RootNodeName + "/" + UnderstorySeeder.RocksNodeName);
                    int rockCount = rocksRoot != null ? rocksRoot.transform.childCount : 0;

                    GameObject deadwoodRoot = GameObject.Find(UnderstorySeeder.RootNodeName + "/" + UnderstorySeeder.DeadwoodNodeName);
                    int logCount = 0, stumpCount = 0;
                    if (deadwoodRoot != null)
                    {
                        for (int i = 0; i < deadwoodRoot.transform.childCount; i++)
                        {
                            string childName = deadwoodRoot.transform.GetChild(i).name;
                            if (childName.StartsWith("Stump_", System.StringComparison.Ordinal)) stumpCount++;
                            else if (childName.StartsWith("FallenLog_", System.StringComparison.Ordinal)) logCount++;
                        }
                    }

                    if (UnderstoryAssetLocator.FindRockPrefabs().Length > 0 && rockCount < showcaseRocks)
                        throw new InvalidDataException(
                            $"Understory Pass/{UnderstorySeeder.RocksNodeName} 只有 {rockCount} 个实例 < 展示环岩石位 {showcaseRocks} 个：" +
                            "展示岩石簇未落在近林带（玩家看不到）；请重新 Bake 并检查 UnderstorySeeder.GenerateRockClusters。");
                    if (UnderstoryAssetLocator.FindDeadwoodPrefabs().Length > 0 &&
                        (logCount < showcaseLogs || stumpCount < showcaseStumps))
                        throw new InvalidDataException(
                            $"Understory Pass/{UnderstorySeeder.DeadwoodNodeName} 倒木 {logCount} / 树桩 {stumpCount} 少于展示位 " +
                            $"倒木 {showcaseLogs} / 树桩 {showcaseStumps}：展示倒木/树桩未落在近林带（玩家看不到）。");
                }

                Debug.Log($"[Validate] Understory 展示环：{showcaseRing.Count} 个近林带道具位" +
                          $"（岩石 {showcaseRocks} / 倒木 {showcaseLogs} / 树桩 {showcaseStumps}）。");
            }

            // detail 风力：蕨类走 Grass 渲染模式（会随风移动），风力未压低时表现为"整株左右平移"。
            // 与 detailScatterMode 同类 —— 都是"数据写对了但渲染表现错"的静默失效，故直接报错。
            if (!Mathf.Approximately(detailData.wavingGrassStrength, JurassicWorldSceneBuilder.DetailWindStrengthTarget) ||
                !Mathf.Approximately(detailData.wavingGrassAmount, JurassicWorldSceneBuilder.DetailWindAmountTarget))
                throw new InvalidDataException(
                    $"TerrainData detail 风力未压低（wavingGrass strength={detailData.wavingGrassStrength:F3}、" +
                    $"amount={detailData.wavingGrassAmount:F3}，应为 {JurassicWorldSceneBuilder.DetailWindStrengthTarget:F2}/" +
                    $"{JurassicWorldSceneBuilder.DetailWindAmountTarget:F2}）：Grass 渲染模式的 fern / bush 网格会整株左右平移；" +
                    "请重新执行 Bake Scene (Edit Mode) 让 JurassicWorldSceneBuilder.ApplyDetailWindSettings 生效。");

            // fern 原型渲染模式：必须是 VertexLit（Grass 走 Unity 内置 waving shader，位移是整株水平平移 =
            // "蕨类根部左右移动"）。该字段只烘进 TerrainData —— 只改代码不重 Bake 会静默沿用旧值，故当不变量校验。
            var wrongFernModes = new List<string>();
            foreach (DetailPrototype proto in detailProtos)
            {
                if (UnderstorySeeder.IsOwnedFernPrototype(proto) && proto.renderMode != UnderstorySeeder.FernRenderModeTarget)
                    wrongFernModes.Add($"{proto.prototype.name}={proto.renderMode}");
            }
            if (wrongFernModes.Count > 0)
                throw new InvalidDataException(
                    $"fern detail 原型渲染模式不是 {UnderstorySeeder.FernRenderModeTarget}：{string.Join(", ", wrongFernModes)}。" +
                    "Grass 模式下 Unity 内置 waving shader 会把整株（含根部）水平推走；" +
                    "请重新执行 Bake Scene (Edit Mode) 让 UnderstorySeeder 重写 fern 原型。");

            // 资产门控接口状态（信息级）：无合格资产时对应子系统 no-op，放入资产后重 Bake 自动生效。
            int fernGateCount = UnderstoryAssetLocator.FindFernPrefabs().Length;
            int deadwoodGateCount = UnderstoryAssetLocator.FindDeadwoodPrefabs().Length;
            int rockGateCount = UnderstoryAssetLocator.FindRockPrefabs().Length;
            if (fernGateCount == 0)
                Debug.Log($"[Validate] Understory fern 接口：no-op（{UnderstoryAssetLocator.FernFolder} 无合格 prefab）。");
            if (deadwoodGateCount == 0)
                Debug.Log($"[Validate] Understory deadwood 接口：no-op（{UnderstoryAssetLocator.DeadwoodFolder} 无合格 prefab）。");
            if (rockGateCount == 0 && !UnderstorySeeder.DebugProceduralRocksOn)
                Debug.Log($"[Validate] Understory 岩石接口：no-op（{UnderstoryAssetLocator.RockFolder} 无合格 prefab；cluster 分布仅用于植被邻域增强）。");
            if (UnderstorySeeder.DebugProceduralRocksOn)
                Debug.LogWarning("[Validate] UnderstorySeeder.DebugProceduralRocks=true：程序化 Sphere 岩石仅供分布验证，正式 Bake 禁止；确认后请恢复 false 并重 Bake。");

            // Preview 倍数必须复位：Preview 会按确定性前缀缩小实例数，若遗留 ≠ 1，当前场景即非生产规模。
            if (UnderstorySeeder.PreviewScale != 1)
                Debug.LogWarning($"[Validate] UnderstorySeeder.PreviewScale={UnderstorySeeder.PreviewScale} ≠ 1：" +
                                 "当前场景很可能来自 Preview Bake（实例数被缩小）。请置回 1 并重新执行 Bake Scene。");

            // ─── HQ 资产完整性断言（缺失 material / shader 属真实缺陷，直接报错） ───
            ValidateNoMissingMaterials();

            Debug.Log($"JURASSIC_MVP_VALIDATION_OK: 编辑态校验通过（相机 / 方向光 / 天空(Panoramic) / Terrain / 树 {treeCount} 棵（阈值 {minTrees}，ForestEnabled={ForestSeeder.ForestEnabled}）/ 核心区森林树泄漏 0 / {JurassicWorldSceneBuilder.EnabledDinosaurCount} 只挂 DinosaurAgent 的恐龙 / Understory{(UnderstorySeeder.PassEnabled ? $"实体 {understoryEntityCount} 个（距恐龙活动包络 {UnderstorySeeder.DinoSafetyBuffer}m 外）、梯度 bush {bushLayerCount} / fern {fernLayerCount} 层（R0 空地内抽样恒 0）" : "已禁用（回滚态）")} / fern 接口 {fernGateCount} prefab / deadwood 接口 {deadwoodGateCount} prefab / rock 接口 {rockGateCount} prefab）。");
        }

        /// <summary>
        /// 点到矩形**边界**的内缩量（米）：点在矩形内时 = 到最近一条边的距离，矩形外为负。
        /// 与 ForestSeeder.DistanceToCoreRect（矩形内部恒 0）语义恰好互补 —— 「端点不能贴场地边」
        /// 这类断言必须用本方法，用 DistanceToCoreRect 会恒判 0 而永远失败。
        /// </summary>
        private static float EdgeInset(Vector2 point, float minX, float maxX, float minZ, float maxZ)
        {
            return Mathf.Min(Mathf.Min(point.x - minX, maxX - point.x),
                             Mathf.Min(point.y - minZ, maxZ - point.y));
        }

        /// <summary>
        /// HQ 资产完整性断言：Understory Pass / Environment Props 子树与三个门控目录下的 prefab，
        /// 任何 Renderer 缺失 material、shader 为 null、shader 不受支持或为 Hidden/InternalErrorShader，
        /// 都属真实缺陷（Fern 走 TerrainData 的 DetailPrototype，Rock/Deadwood 走场景实体），直接抛异常。
        /// </summary>
        private static void ValidateNoMissingMaterials()
        {
            var offenders = new List<string>();
            int checkedCount = 0;

            foreach (string rootName in new[] { UnderstorySeeder.RootNodeName, "Environment Props" })
            {
                GameObject root = GameObject.Find(rootName);
                if (root != null)
                    CollectMissingMaterials(root, rootName, offenders, ref checkedCount);
            }

            foreach (string folder in new[]
                     {
                         UnderstoryAssetLocator.FernFolder,
                         UnderstoryAssetLocator.RockFolder,
                         UnderstoryAssetLocator.DeadwoodFolder
                     })
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    continue;
                foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                        CollectMissingMaterials(prefab, path, offenders, ref checkedCount);
                }
            }

            if (offenders.Count > 0)
                throw new InvalidDataException(
                    $"检测到 {offenders.Count} 个 Renderer 缺失/不支持 material 或 shader（共检查 {checkedCount} 个）：" +
                    string.Join("；", offenders) +
                    "。请执行 Jurassic World/Assets/Integrate PolyHaven Assets 后重 Bake。");

            Debug.Log($"[Validate] HQ 资产完整性：已检查 Renderer {checkedCount} 个，无 missing material / shader。");
        }

        private static void CollectMissingMaterials(GameObject root, string label, List<string> offenders, ref int checkedCount)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                checkedCount++;
                Material material = renderer.sharedMaterial;
                if (material == null)
                {
                    offenders.Add($"{label}:{renderer.name} 的 sharedMaterial 为 null");
                    continue;
                }
                Shader shader = material.shader;
                if (shader == null)
                    offenders.Add($"{label}:{renderer.name} 的材质 \"{material.name}\" shader 为 null");
                else if (!shader.isSupported)
                    offenders.Add($"{label}:{renderer.name} 的 shader \"{shader.name}\" 不受支持");
                else if (shader.name == "Hidden/InternalErrorShader")
                    offenders.Add($"{label}:{renderer.name} 的 shader 为 Hidden/InternalErrorShader（编译失败）");
            }
        }
    }
}
