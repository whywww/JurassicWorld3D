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

        [MenuItem("Jurassic World/Create MVP Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Jurassic World MVP scene created.");
        }

        // 编辑态烘焙入口（任务 #3 可用 -executeMethod JurassicWorld.Editor.JurassicProjectSetup.BakeScene 调用）。
        // 打开 Main.unity → 由 JurassicWorldSceneBuilder 建造全部内容 → 标脏并保存场景与资产。
        [MenuItem("Jurassic World/Bake Scene (Edit Mode)")]
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
        [MenuItem("Jurassic World/Reimport Dinosaur Models")]
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
        [MenuItem("Jurassic World/Validate MVP")]
        public static void ValidateProject()
        {
            if (!File.Exists(MainScenePath))
                throw new FileNotFoundException("缺少已烘焙的主场景，请先执行 Jurassic World/Bake Scene (Edit Mode)。", MainScenePath);

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

            // ─── Dense Jungle Phase 1 → 1.1：恐龙移动通道**网络**（MovementCorridor）几何不变量 ───
            // 通道网络是后续「密集丛林 + 遮挡节奏」的唯一地基：几何一旦漂移（越出活动包络 / 掉进森林 /
            // 退化成直线 / 变宽成草坪 / 支路不再分叉汇合），后面每个阶段都会在错误的底子上叠加，
            // 故此处直接报错。Phase 1.1 起 ①②③③b④⑤⑦ 对**每条路线**分别断言，
            // 再叠加网络级 ⑥（分叉/汇合）、⑧（在册支路的角色几何）、⑨（标注根）。
            // Phase 1.1d 起在册 = 主干 + 前景（中景/林缘停用、数据保留，见 MovementCorridor 注释）。
            if (MovementCorridor.RouteCount != MovementCorridor.MinRouteCount)
                throw new InvalidDataException(
                    $"Phase 1 必须恰有 {MovementCorridor.MinRouteCount} 条通道，当前 {MovementCorridor.RouteCount} 条。");

            // ③b 的输入：场内最宽一对恐龙互斥半径之和（所有路线面对同一对恐龙，提到循环外算一次）。
            float widestPairRadius = 0f;
            for (int i = 0; i < agents.Length; i++)
            {
                for (int j = i + 1; j < agents.Length; j++)
                    widestPairRadius = Mathf.Max(widestPairRadius, agents[i].CollisionRadius + agents[j].CollisionRadius);
            }

            const float CorridorEndInset = 10f;

            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            {
                string routeName = MovementCorridor.GetRouteName(route);
                MovementCorridor.CorridorMetrics corridorMetrics = MovementCorridor.Measure(route);
                Rect corridorOuter = corridorMetrics.OuterBounds;

                // ① 该路线（AABB ⊇ 全部边界点）⊆ 恐龙活动包络：恐龙沿路线走到哪儿都合法。
                if (corridorOuter.xMin < activityBounds.xMin - 1e-3f || corridorOuter.xMax > activityBounds.xMax + 1e-3f ||
                    corridorOuter.yMin < activityBounds.yMin - 1e-3f || corridorOuter.yMax > activityBounds.yMax + 1e-3f)
                    throw new InvalidDataException(
                        $"路线「{routeName}」x[{corridorOuter.xMin:F1},{corridorOuter.xMax:F1}] z[{corridorOuter.yMin:F1},{corridorOuter.yMax:F1}] " +
                        $"越出恐龙活动包络 x[{activityBounds.xMin:F0},{activityBounds.xMax:F0}] z[{activityBounds.yMin:F0},{activityBounds.yMax:F0}]：" +
                        "恐龙会走到未被植被安全边界覆盖的区域（外侧可能起伏/密林）。请回改该路线的控制点。");

                // ② 该路线 ⊆ ForestSeeder 核心矩形 ⇒ 路线内零外围森林树（可行走路线不被整棵森林树堵死）。
                float coreMinX = ForestSeeder.CoreMinX, coreMaxX = ForestSeeder.CoreMaxX;
                float coreMinZ = ForestSeeder.CoreMinZ, coreMaxZ = ForestSeeder.CoreMaxZ;
                if (corridorOuter.xMin < coreMinX || corridorOuter.xMax > coreMaxX ||
                    corridorOuter.yMin < coreMinZ || corridorOuter.yMax > coreMaxZ)
                    throw new InvalidDataException(
                        $"路线「{routeName}」x[{corridorOuter.xMin:F1},{corridorOuter.xMax:F1}] z[{corridorOuter.yMin:F1},{corridorOuter.yMax:F1}] " +
                        $"越出森林核心矩形 x[{coreMinX:F0},{coreMaxX:F0}] z[{coreMinZ:F0},{coreMaxZ:F0}]：" +
                        "路线内会出现外围森林树（prototypeIndex ≥ 中央段基线），恐龙路线会被高密度树群堵死。请回改该路线的控制点。");

                // ③ 全宽区间：「约开放区宽度 1/3 的狭窄通道」。过窄夹死霸王龙，过宽退回大草坪。
                if (corridorMetrics.MinFullWidthSeen < MovementCorridor.MinFullWidth - 1e-3f ||
                    corridorMetrics.MaxFullWidthSeen > MovementCorridor.MaxFullWidth + 1e-3f)
                    throw new InvalidDataException(
                        $"路线「{routeName}」全宽 {corridorMetrics.MinFullWidthSeen:F1}–{corridorMetrics.MaxFullWidthSeen:F1}m " +
                        $"超出允许区间 [{MovementCorridor.MinFullWidth}, {MovementCorridor.MaxFullWidth}]m：" +
                        "过窄会让霸王龙（treeAvoidRadius 2.5m）在两侧有树时被夹死，过宽则做不出遮挡。请回改该路线半宽数组。");

                // ③b 车道容量：每只恐龙 Awake 时分到一条固定车道（DinosaurAgent.AssignLane），两条最外侧
                // 车道的横向间距 = LaneBiasScale ×（该路线全宽 − 2×LaneEdgeMargin）。该间距必须 ≥ 场内最宽
                // 一对恐龙的互斥半径之和，否则「分道对会」在最窄处也过不去，会车让行逻辑长期顶死
                // （用户报告的「撞上后一动不动」）。两只恐龙时等价于「任意一对可对会」；多于两只时为
                // 最外侧车道的必要条件，其余拥堵由 HandlePeerBlocked 的先行权/让路兜底。
                float minUsableWidth = corridorMetrics.MinFullWidthSeen - 2f * DinosaurAgent.LaneEdgeMargin;
                float minLaneSeparation = DinosaurAgent.LaneBiasScale * minUsableWidth;
                if (agents.Length > 1 && minLaneSeparation < widestPairRadius)
                    throw new InvalidDataException(
                        $"路线「{routeName}」最窄处全宽 {corridorMetrics.MinFullWidthSeen:F1}m 时两条最外车道仅相距 " +
                        $"{minLaneSeparation:F1}m < 最宽一对恐龙互斥半径之和 {widestPairRadius:F1}m：" +
                        $"（= {DinosaurAgent.LaneBiasScale} ×（{corridorMetrics.MinFullWidthSeen:F1} − 2×{DinosaurAgent.LaneEdgeMargin}））" +
                        "最窄段两只恐龙仍会挤进彼此互斥圆，会车让行逻辑会长期顶死。请加宽该路线半宽数组或调小车道常量。");

                // ④ 形状：必须是「轻微 S」而非直线、且左右不对称（禁镜像）。
                if (corridorMetrics.SwingX < MovementCorridor.MinSwingMeters)
                    throw new InvalidDataException(
                        $"路线「{routeName}」横向摆幅 {corridorMetrics.SwingX:F1}m < {MovementCorridor.MinSwingMeters}m：" +
                        "路线退化成近似直线，恐龙朝向不再随路径变化。请加大该路线控制点的横向起伏。");
                if (corridorMetrics.MaxAsymmetry < MovementCorridor.MinAsymmetryMeters)
                    throw new InvalidDataException(
                        $"路线「{routeName}」最大左右半宽差 {corridorMetrics.MaxAsymmetry:F2}m < {MovementCorridor.MinAsymmetryMeters}m：" +
                        "左右边界完全对称，失去「局部宽窄不一 + 不对称」的走道观感。请错开该路线的左右半宽数组。");

                // ⑤ 不贯穿场地：两端必须收在草地内部（距核心矩形边界留余量），否则等于开一条贯穿全场的视线通道。
                // 注意不能用 ForestSeeder.DistanceToCoreRect —— 矩形【内部】恒返回 0，此处要的是「到最近边的内缩量」。
                Vector2 routeStart = MovementCorridor.GetSample(route, 0).Position;
                Vector2 routeEnd = MovementCorridor.GetSample(route, MovementCorridor.SampleCount(route) - 1).Position;
                float startInset = EdgeInset(routeStart, coreMinX, coreMaxX, coreMinZ, coreMaxZ);
                float endInset = EdgeInset(routeEnd, coreMinX, coreMaxX, coreMinZ, coreMaxZ);
                if (startInset < CorridorEndInset || endInset < CorridorEndInset)
                    throw new InvalidDataException(
                        $"路线「{routeName}」端点距核心矩形边界仅 {startInset:F1}m / {endInset:F1}m（要求 ≥ {CorridorEndInset}m）：" +
                        "路线贯穿到场地边缘会形成一眼看穿的裸带。请把首尾控制点往草地内部收。");

                // ⑦【硬门】轨迹净空带内零树 —— 用户要求「轨迹上不要有障碍物」（Phase 1.1c）。
                // 侵入 = 树中心在路线内（未钳制投影、弧长 ∈ [0, Length]，端帽之外不计）且
                // |横向| < 所在侧半宽 + MovementCorridor.CorridorTreeClearVerge（1.8m）——
                // 即最外车道（LaneBiasScale×(half−1)≈4.7m）+ treeAvoidRadius 2.5 + 树干0.9 的
                // 滑移/阻挡半径覆盖不到的范围。SceneBuilder.SeedTrees.NudgeOutOfCorridors 已在播种时
                // 把侵入者沿法线横推出带外（只横移、不改随机流），此处复核；任何残留都判失败。
                // 必须用未钳制投影：ProjectToCorridor 会把弧长钳进 [0, Length]，端帽之外、恰好与路线中线
                // 共线的树会得到一个虚假的「横向贴近」值（历史 bug：端点外 25m 的树被计入）。
                int blockingTreeCount = 0;
                string blockingTreeList = "";
                foreach (TreeInstance ti in treeInstances)
                {
                    Vector3 world = terrain.transform.TransformPoint(new Vector3(
                        ti.position.x * terrainSize.x, ti.position.y * terrainSize.y, ti.position.z * terrainSize.z));
                    MovementCorridor.ProjectUnclamped(route, new Vector2(world.x, world.z), out float arc, out float lateral);
                    if (arc < 0f || arc > MovementCorridor.Length(route))
                        continue;   // 在路线两端之外：不属于该路线段
                    MovementCorridor.GetSampleAt(route, arc, out _, out _, out float halfLeft, out float halfRight);
                    float limit = (lateral >= 0f ? halfLeft : halfRight) + MovementCorridor.CorridorTreeClearVerge;
                    if (Mathf.Abs(lateral) < limit)
                    {
                        blockingTreeCount++;
                        if (blockingTreeCount <= 8)
                            blockingTreeList += $" ({world.x:F1},{world.z:F1}) 横向 {lateral:F1}m / 限 {limit:F1}m";
                    }
                }
                if (blockingTreeCount > 0)
                    throw new InvalidDataException(
                        $"路线「{routeName}」轨迹净空带内有 {blockingTreeCount} 棵树：{blockingTreeList}" +
                        $"……（净空 = 所在侧半宽 + {MovementCorridor.CorridorTreeClearVerge}m）。" +
                        "恐龙会贴树滑行/卡死。请重新执行 Bake Scene（SeedTrees 会自动把侵入者横推出去），" +
                        "若重 Bake 后仍报错则是 ForestSeeder 等后续 pass 引入的树，需回改其分布。");

                // 与运行期同一棵树注册表逐点复核三条可能车道；中心净空断言无法覆盖异常大的树干缩放。
                float maxTreeAvoidRadius = agents.Max(agent => Mathf.Min(agent.CollisionRadius, DinosaurAgent.TreeAvoidRadiusCap));
                for (int sample = 0; sample < MovementCorridor.SampleCount(route); sample++)
                {
                    float arc = MovementCorridor.GetSample(route, sample).Distance;
                    foreach (float bias in new[] { -DinosaurAgent.LaneBiasScale, 0f, DinosaurAgent.LaneBiasScale })
                    {
                        Vector2 lanePoint = MovementCorridor.PointAt(route, arc, DinosaurAgent.LaneLateral(route, arc, bias));
                        if (TerrainTreeObstacleRegistry.IsBlocked(lanePoint, maxTreeAvoidRadius))
                            throw new InvalidDataException(
                                $"路线「{routeName}」弧长 {arc:F1}m 的车道点 ({lanePoint.x:F1},{lanePoint.y:F1}) 被树干阻挡。");
                    }
                }
            }

            // 两条路线沿途独立。验收最小中心线间距，保证即使恐龙随机选线、
            // 在最靠内侧的车道上迎面经过，也不会侵入双方互斥圆。
            float minimumRouteSeparation = float.MaxValue;
            for (int i = 0; i < MovementCorridor.SampleCount(MovementCorridor.MainRoute); i++)
                minimumRouteSeparation = Mathf.Min(minimumRouteSeparation, MovementCorridor.MinDistanceToRoute(
                    MovementCorridor.GetSample(MovementCorridor.MainRoute, i).Position,
                    MovementCorridor.ForegroundRoute));
            const float MaximumLaneReach = 5.5f;
            if (minimumRouteSeparation - 2f * MaximumLaneReach < widestPairRadius)
                throw new InvalidDataException(
                    $"两条路线最近中心线仅相距 {minimumRouteSeparation:F1}m，内侧车道可能相撞：" +
                    $"要求中心线间距 ≥ {widestPairRadius + 2f * MaximumLaneReach:F1}m。");

            // ⑨ 场景 debug 标注根存在（可开关可视化交付物，每条路线一组线段）。
            GameObject corridorRoot = GameObject.Find(MovementCorridorDebug.RootNodeName);
            if (corridorRoot == null)
                throw new InvalidDataException(
                    $"场景缺少 \"{MovementCorridorDebug.RootNodeName}\" 根节点（通道 debug 标注）：" +
                    "请重新执行 Bake Scene (Edit Mode)。");
            MovementCorridorDebug corridorDebug = corridorRoot.GetComponent<MovementCorridorDebug>();
            if (corridorDebug == null)
                throw new InvalidDataException(
                    $"\"{MovementCorridorDebug.RootNodeName}\" 缺少 MovementCorridorDebug 组件，请重新执行 Bake Scene (Edit Mode)。");

            Debug.Log($"[Validate] {MovementCorridor.Describe()}");
            Debug.Log($"[Validate] 双通道断言：{MovementCorridor.RouteCount} 条路线全部 ⊆ 活动包络 ✓ / ⊆ 森林核心矩形 ✓ / " +
                      $"全宽 ∈ [{MovementCorridor.MinFullWidth},{MovementCorridor.MaxFullWidth}]m ✓ / 分道对会容量 ✓ / " +
                      $"摆幅 ≥ {MovementCorridor.MinSwingMeters}m ✓ / 端点内缩 ≥ {CorridorEndInset}m ✓ / " +
                      $"两线最小间距 {minimumRouteSeparation:F1}m ✓ / " +
                      $"轨迹净空带（半宽+{MovementCorridor.CorridorTreeClearVerge}m）0 棵树 ✓；" +
                      $"标注 \"{MovementCorridorDebug.RootNodeName}\" showMarkers={corridorDebug.ShowMarkers}（运行期 F3 可切换）。");

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
                    "。请执行 Jurassic World/Integrate PolyHaven Assets 后重 Bake。");

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
