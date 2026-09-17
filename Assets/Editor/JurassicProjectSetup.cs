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

            // ─── Understory Pass 验证（几何安全不变量 + bush 密度硬钳制 + 资产门控状态） ───
            int understoryEntityCount = 0;
            int bushLayerCount = 0;
            if (UnderstorySeeder.PassEnabled)
            {
                GameObject understoryRoot = GameObject.Find(UnderstorySeeder.RootNodeName);
                if (understoryRoot == null)
                    throw new InvalidDataException(
                        "Understory Pass 已启用但场景缺少 \"Understory Pass\" 根节点，请重新执行 Bake Scene (Edit Mode)。");

                // 实体不变量：全部 Renderer 中心到平坦矩形的距离 ≥ DinoSafetyBuffer（留 5cm bounds 容差）。
                // 恐龙漫步包络 ⊆ 平坦矩形，本条成立即保证中大型实体道具（rock / hero bush / log / stump）与恐龙零接触。
                Renderer[] understoryRenderers = understoryRoot.GetComponentsInChildren<Renderer>(true);
                understoryEntityCount = understoryRenderers.Length;
                foreach (Renderer renderer in understoryRenderers)
                {
                    Vector3 center = renderer.bounds.center;
                    float distToFlat = UnderstorySeeder.DistanceToFlatRectWorld(center.x, center.z);
                    if (distToFlat < UnderstorySeeder.DinoSafetyBuffer - 0.05f)
                        throw new InvalidDataException(
                            $"Understory 实体 \"{renderer.name}\" 位于安全缓冲内（距平坦矩形 {distToFlat:F2}m < {UnderstorySeeder.DinoSafetyBuffer}m）：" +
                            "违反「恐龙活动区 + 缓冲外无中大型实体」不变量，请检查 UnderstorySeeder 簇心余量常量。");
                }

                // bush 层抽样：平坦矩形 + buffer − 0.2m 内密度必须恒 0（PaintDetailLayers 终态硬钳制的机器验证）。
                int[] bushLayers = UnderstorySeeder.FindBushDetailLayerIndices(terrain.terrainData);
                bushLayerCount = bushLayers.Length;
                if (bushLayers.Length > 0)
                {
                    TerrainData td = terrain.terrainData;
                    int res = td.detailResolution;
                    int stride = Mathf.Max(1, res / 64); // 64×64 抽样网格（约 4096 采样点 / 层）
                    float half = td.size.x / 2f;
                    foreach (int layerIdx in bushLayers)
                    {
                        int[,] layer = td.GetDetailLayer(0, 0, res, res, layerIdx);
                        for (int z = 0; z < res; z += stride)
                        for (int x = 0; x < res; x += stride)
                        {
                            float wx = (float)x / res * td.size.x - half;
                            float wz = (float)z / res * td.size.z - half;
                            if (UnderstorySeeder.DistanceToFlatRectWorld(wx, wz) < UnderstorySeeder.DinoSafetyBuffer - 0.2f &&
                                layer[x, z] != 0)
                                throw new InvalidDataException(
                                    $"bush 层 idx={layerIdx} 在安全缓冲内存在非零密度（格 ({x},{z}) 密度 {layer[x, z]}）：" +
                                    "违反「buffer 内 bush 密度恒 0」不变量，请检查 UnderstorySeeder.PaintDetailLayers 的终态硬钳制。");
                        }
                    }
                }
            }
            else if (GameObject.Find(UnderstorySeeder.RootNodeName) != null)
            {
                Debug.LogWarning("[Validate] UnderstorySeeder 已禁用但场景中仍存在 \"Understory Pass\" 根：请重 Bake 完成回滚。");
            }

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

            Debug.Log($"JURASSIC_MVP_VALIDATION_OK: 编辑态校验通过（相机 / 方向光 / 天空(Panoramic) / Terrain / 树 {treeCount} 棵（阈值 {minTrees}，ForestEnabled={ForestSeeder.ForestEnabled}）/ 核心区森林树泄漏 0 / {JurassicWorldSceneBuilder.EnabledDinosaurCount} 只挂 DinosaurAgent 的恐龙 / Understory{(UnderstorySeeder.PassEnabled ? $"实体 {understoryEntityCount} 个（buffer {UnderstorySeeder.DinoSafetyBuffer}m 外，bush 层 {bushLayerCount} 个抽样密度 0）" : "已禁用（回滚态）")} / fern 接口 {fernGateCount} prefab / deadwood 接口 {deadwoodGateCount} prefab / rock 接口 {rockGateCount} prefab）。");
        }
    }
}
