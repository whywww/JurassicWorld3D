using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JurassicWorld.Editor
{
    // 编辑态共享建造器：把原 JurassicWorldBootstrap 在运行时凭代码创建的全部内容（灯光、草地、相机、
    // HUD、若干只恐龙，数量由 EnabledDinosaurCount 依据 CreateTemplates 的 enabled 开关派生）迁移为可在【不点 Play】的编辑态调用的静态建造方法。所有硬编码参数严格对照
    // Bootstrap 原值迁移（草地岩石固定随机种子 271828、Agent 漫游种子仍由 DinosaurAgent 内部持有）。
    //
    // 关键约定：
    //  - 恐龙模型用 PrefabUtility.InstantiatePrefab 实例化为子物体，DinosaurModelFitter.Fit 只在烘焙时
    //    计算一次并写入 transform（运行期绑定器不再调用 Fit）。
    //  - 材质烘焙为 Assets/Materials/Dinosaurs/*.mat 资产并赋给 Renderer（替代运行时 new Material）。
    //  - 各组件的可配置参数通过 [SerializeField] 私有字段写入，字段名与运行期脚本严格一致（见 SetField）。
    public static class JurassicWorldSceneBuilder
    {
        private const int GroundRandomSeed = MovementCorridor.LayoutSeed;
        private const string MaterialFolder = "Assets/Materials";
        private const string SkyPanoramaPath = "Assets/Materials/NatureSkyPanorama.tga";
        private const string DinosaurMaterialFolder = "Assets/Materials/Dinosaurs";
        private const string TerrainFolder = "Assets/Terrain";
        private const string TerrainDataPath = "Assets/Terrain/JurassicTerrainData.asset";
        private const string TerrainLayerFolder = "Assets/Terrain/Layers";
        private const float TerrainSize = 200f;
        private const float TerrainHeight = 10f;
        private const int HeightmapRes = 257;
        private const int DetailRes = 512;
        private const int DetailResPerPatch = 8;

        // 人眼视高（米）：相机 Y 与 SmoothExplorerCamera.eyeHeight 的唯一来源。
        private const float HumanEyeHeight = 1.7f;

        // 当前启用（会被烘焙进场景）的恐龙数量 —— HUD 期望计数与 Validate 断言的唯一来源。
        public static int EnabledDinosaurCount => CreateTemplates().Count(t => t.enabled);

        // 建造入口：清空当前场景后重建全部内容（幂等，避免与已烘焙对象重复）。
        public static void Build()
        {
            FoliageInteractionPreparation.RestoreCurrent();
            ClearActiveScene();
            DeleteLegacyEnvironmentAssets();
            // HQ 资产铸造（幂等）：确保 Assets/Vegetation/{Ferns,Rocks,Deadwood} 下的 project-owned
            // material / prefab 就绪，使 UnderstorySeeder 的三个资产门控在本次 Bake 生效。
            // 必须早于 BuildTerrain（后者内部会跑 UnderstorySeeder.Seed）；资产已存在时为纯读操作。
            PolyHavenAssetIntegration.EnsureAssets();
            Font uiFont = LoadBuiltinFont();
            Camera camera = BuildCamera();
            BuildLighting();
            BuildWind();
            // Terrain 构建失败（如 NSk2 未导入）时立即抛出：异常会一路传出 JurassicProjectSetup.BakeScene
            // （该方法无 try/catch 吞异常），MarkSceneDirty / SaveScene / SaveAssets 因此不会执行，
            // 上一次可用的 Main.unity 与 JurassicTerrainData.asset 不被半成品场景覆盖。
            // 注：ClearActiveScene 只销毁【内存中】的场景对象，未保存前不影响已落盘的场景文件。
            if (!BuildTerrain())
                throw new InvalidOperationException("[SceneBuilder] Terrain 构建失败，中止 Bake 以避免覆盖上一次可用场景。");
            BuildScatterPrefabs();
            BuildHud(uiFont);
            BuildDinosaurs(camera, uiFont);
            // Phase 2: five authored, overlapping vegetation islands in the central clearing.
            VegetationIslandSeeder.Seed();
            // The reviewed Phase 2 biome pass is regenerated after a full scene Bake.
            Phase2VegetationSeeder.Seed();
            GroundEcologyPass.SeedSurface(UnityEngine.Object.FindObjectOfType<Terrain>());
            CentralForestDensification.SeedConfiguredStage();
            LocalForestRepair.SeedConfigured();
            FoliageInteractionPreparation.SeedConfigured();
            // Dense Jungle Phase 1：可开关的通道 debug 标注（纯场景线段，不动 TerrainData / 植被 Pass）。
            BuildMovementCorridorDebug();
        }

        // ---------------------------------------------------------------- 通道 debug 标注（Phase 1）

        /// <summary>
        /// 创建 MovementCorridorDebug 根节点（Dense Jungle Phase 1 的通道标注）。
        /// 标注几何全部由运行期按 MovementCorridor 程序化生成，本方法只负责「放一个挂了组件的根」，
        /// 并把显示开关按烘焙态契约写入（showMarkers 字段名是跨脚本契约，禁止改名）。
        /// 阶段结束后：把 MovementCorridorDebug.ShowMarkersDefault 置 false 重 Bake 即整体关闭。
        /// </summary>
        private static void BuildMovementCorridorDebug()
        {
            var root = new GameObject(MovementCorridorDebug.RootNodeName);
            var corridorDebug = root.AddComponent<MovementCorridorDebug>();
            SetField(corridorDebug, "showMarkers", MovementCorridorDebug.ShowMarkersDefault);
        }

        // ---------------------------------------------------------------- 场景清理

        private static void ClearActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;
            foreach (GameObject root in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(root);
        }

        // ---------------------------------------------------------------- 相机

        private static Camera BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();

            camera.transform.position = new Vector3(0f, HumanEyeHeight, -16f);
            camera.transform.rotation = Quaternion.Euler(2f, 0f, 0f);
            camera.fieldOfView = 65f;
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 150f;
            camera.clearFlags = CameraClearFlags.Skybox;

            EmotivPlayerInput emotivInput = cameraObject.AddComponent<EmotivPlayerInput>();
            SmoothExplorerCamera explorer = cameraObject.AddComponent<SmoothExplorerCamera>();
            SetField(explorer, "emotivInput", emotivInput);
            // 视高由 3m 降到 1.7m 后近地视觉流速加快，同步降速以保持原有观感与人的尺度感。
            SetField(explorer, "moveSpeed", 4f);
            SetField(explorer, "acceleration", 6f);
            SetField(explorer, "turnSpeed", 60f);
            SetField(explorer, "cameraFollowSpeed", 3.5f);
            SetField(explorer, "lookSensitivity", 65f);
            SetField(explorer, "initialPitch", 4f);
            SetField(explorer, "eyeHeight", HumanEyeHeight);
            EditorUtility.SetDirty(explorer);
            return camera;
        }

        // ---------------------------------------------------------------- 灯光 / 天空 / 风

        private static void BuildLighting()
        {
            Material skybox = ResolveSkyboxMaterial();
            if (skybox != null)
                RenderSettings.skybox = skybox;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 128;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.65f, 0.75f, 0.82f);
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 180f;

            var sunObject = new GameObject("Sun");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sunObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        // 天空材质解析：优先用 NSk2 天空贴图的【经纬度全景】（Skybox/Panoramic）版本。
        // 旧 NSk2 cubemap 天空（skybox.mat，2015 年旧式自动生成的立方体贴图，seamlessCubemap=0）
        // 在面边界处内容不连续，画面中会出现明显竖直接缝线（云被切断），故不再使用。
        private static Material ResolveSkyboxMaterial()
        {
            Texture2D skyPanorama = EnsureSkyPanoramaTexture();

            EnsureFolder(MaterialFolder);
            const string path = MaterialFolder + "/NaturalSkybox.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(skyPanorama != null ? "Skybox/Panoramic" : "Skybox/Procedural");
            if (shader == null) return null;
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            if (skyPanorama != null)
            {
                // Panoramic 在球面上连续采样单张 2D 贴图，不存在立方体面边界，竖直接缝线从机制上不可能再出现。
                material.SetTexture("_MainTex", skyPanorama);
                material.SetFloat("_Mapping", 1f);    // 1 = Latitude Longitude Layout（贴图按经纬度展开）
                material.SetFloat("_ImageType", 0f);  // 0 = 360 度全景
                material.SetFloat("_MirrorOnBack", 0f);
                material.SetFloat("_Layout", 0f);
                material.SetFloat("_Rotation", 0f);
                material.SetFloat("_Exposure", 1f);
                // _Tint 0.5 为天空盒中性默认（与旧 cubemap 材质一致），亮度观感不变。
                material.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                // KeywordEnum 关键字需显式关闭，保证运行在经纬度采样分支。
                material.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            }
            else
            {
                // NSk2 天空贴图缺失（如资源包未导入）时的程序化天空回退。
                Debug.LogWarning("[SceneBuilder] 天空全景贴图不可用，回退程序化天空（Skybox/Procedural），观感将与 NSk2 天空不同。");
                material.SetColor("_SkyTint", new Color(0.45f, 0.65f, 0.88f));
                material.SetColor("_GroundColor", new Color(0.25f, 0.30f, 0.20f));
                material.SetFloat("_AtmosphereThickness", 1.0f);
                material.SetFloat("_Exposure", 1.3f);
                material.SetFloat("_SunDisk", 2f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        // 确保项目自有的 2D 全景天空贴图就绪：
        // 从 NSk2 源 sky.tga 复制一份到 Assets/Materials（CopyAsset 连导入设置一起复制，
        // 副本仍是旧 cubemap 导入），再幂等强制为 2D 全景导入设置（textureShape 回 2D + 关闭 mip）；
        // 不修改 NSk2 原资产，其演示场景不受影响。
        private static Texture2D EnsureSkyPanoramaTexture()
        {
            if (!System.IO.File.Exists(SkyPanoramaPath))
            {
                string sourcePath = NatureStarterKitLocator.FindSkyTexturePath();
                if (sourcePath == null)
                    return null;

                EnsureFolder(MaterialFolder);
                if (!AssetDatabase.CopyAsset(sourcePath, SkyPanoramaPath))
                {
                    Debug.LogWarning($"[SceneBuilder] 天空全景贴图复制失败：{sourcePath} → {SkyPanoramaPath}，回退程序化天空。");
                    return null;
                }
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(SkyPanoramaPath);
            if (importer == null)
            {
                Debug.LogWarning($"[SceneBuilder] 无法读取天空全景贴图导入器：{SkyPanoramaPath}，回退程序化天空。");
                return null;
            }

            bool needsFixup =
                importer.textureType != TextureImporterType.Default ||
                importer.textureShape != TextureImporterShape.Texture2D ||
                importer.wrapMode != TextureWrapMode.Clamp ||
                importer.anisoLevel != 1 ||
                importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.mipmapEnabled ||
                importer.maxTextureSize != 2048 ||
                !importer.sRGBTexture;
            if (needsFixup)
            {
                // textureShape 必须显式回 2D：CopyAsset 会连同源资产的导入设置一起复制，
                // 源是旧 cubemap 导入（textureShape=Cube），否则副本仍是立方体贴图，
                // LoadAssetAtPath<Texture2D> 取不到而静默回退程序化天空。
                // 关闭 mip：经纬度环绕列（atan2 经度突变）在隐式导数采样下会命中极深层 mip，
                // 渲染为一条全图平均色条带（蓝天上呈亮光束 / 白云上呈切断暗线）；全景天空无需 mip。
                // clamp（经度边界不混入对侧像素）、aniso 1（避免球面掠射角过滤伪影）、
                // 不压缩（平滑渐变用 DXT 会出色带）、2048 保证缩采样质量。
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.anisoLevel = 1;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 2048;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(SkyPanoramaPath);
        }

        private static void BuildWind()
        {
            var windObject = new GameObject("Wind Zone");
            WindZone wind = windObject.AddComponent<WindZone>();
            wind.mode = WindZoneMode.Directional;
            wind.windMain = 0.35f;
            wind.windTurbulence = 0.15f;
            wind.windPulseMagnitude = 0.4f;
            wind.windPulseFrequency = 0.08f;
        }

        private static void DeleteLegacyEnvironmentAssets()
        {
            AssetDatabase.DeleteAsset("Assets/Materials/Grassland.mat");
            AssetDatabase.DeleteAsset("Assets/Materials/Rock.mat");
            AssetDatabase.DeleteAsset("Assets/Materials/JurassicSkybox.mat");
        }

        // ---------------------------------------------------------------- Terrain

        // 返回值：Terrain 是否构建成功。false = 前置依赖缺失（Nature Starter Kit 2 未导入），
        // 调用方 Build() 据此抛异常中止整个 Bake，避免把缺地形/缺森林的场景保存落盘。
        private static bool BuildTerrain()
        {
            if (!NatureStarterKitLocator.IsImported)
            {
                Debug.LogError("[SceneBuilder] 未检测到 Nature Starter Kit 2，请先从 Asset Store 导入后再执行 Bake。");
                return false;
            }

            EnsureFolder(TerrainFolder);
            EnsureFolder(TerrainLayerFolder);

            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            bool firstTimeSeed = false;
            if (data == null)
            {
                data = new TerrainData();
                data.heightmapResolution = HeightmapRes;
                data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
                AssetDatabase.CreateAsset(data, TerrainDataPath);
                firstTimeSeed = true;
            }
            else
            {
                firstTimeSeed = (data.treeInstanceCount == 0);
            }

            if (firstTimeSeed)
            {
                data.SetHeights(0, 0, GenerateHeightmap());
            }

            // detail 层语义归一化必须早于任何 detail 数据写回（SeedDetails / GroundPass / Understory）
            EnsureDetailInstanceCountMode(data);

            // 环境 Pass 阶段 1：确定性 heightmap 重建 + 原生 snap 贴地（heightmap 为所有后续放置的基础数据）
            TerrainEnvironmentSeeder.PrepareTerrain(data);

            GameObject terrainGO = Terrain.CreateTerrainGameObject(data);
            terrainGO.name = "Terrain";
            terrainGO.transform.position = new Vector3(-TerrainSize / 2f, 0f, -TerrainSize / 2f);

            // 关闭 Terrain 树碰撞：项目运行时为纯 transform 位移（DinosaurAgent / SmoothExplorerCamera 均无 Rigidbody），
            // 静态树 Collider 对它们零阻挡效果，反而是纯 PhysX 开销；碰撞语义由运行时 TerrainTreeObstacleRegistry
            // 行为级注册表统一承担（覆盖中央 161 棵 + 外围森林全部实例）。
            // 注：Unity 2022.3 的 TerrainCollider 未暴露 enableTreeColliders 公共 C# 属性（仅 Inspector 序列化的 m_EnableTreeColliders），
            // 故通过 SerializedObject 写入；未来 Unity 版本若提供公共属性可简化为直接赋值。
            TerrainCollider terrainCollider = terrainGO.GetComponent<TerrainCollider>();
            if (terrainCollider != null)
            {
                var colliderSO = new SerializedObject(terrainCollider);
                SerializedProperty enableTreeCollidersProp = colliderSO.FindProperty("m_EnableTreeColliders");
                if (enableTreeCollidersProp != null)
                {
                    enableTreeCollidersProp.boolValue = false;
                    colliderSO.ApplyModifiedPropertiesWithoutUndo();
                    // SerializedObject 直写字段不会自动标脏，漏掉 SetDirty 会让本次修改在 SaveScene/SaveAssets 时丢失。
                    EditorUtility.SetDirty(terrainCollider);
                }
                else
                {
                    Debug.LogWarning("[SceneBuilder] TerrainCollider 未找到序列化字段 m_EnableTreeColliders，跳过关闭树碰撞（Unity 版本差异）。");
                }
            }

            Terrain terrain = terrainGO.GetComponent<Terrain>();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 5;
            terrain.basemapDistance = 200f;
            // 渲染参数与外围森林配套：farClip=150，treeDistance=160 留裸边零 pop-in；
            // billboard 60 起跳，fullLODCount=130 为峰值真实性能旋钮（130 × ~3,700 tris × 2 阴影 pass ≈ 96 万 tris）。
            // 性能不达标时的调参次序：treeMaximumFullLODCount 130→100→80 → treeBillboardDistance 60→70/80 → ForestSeeder 密度减档。
            terrain.treeDistance = 160f;
            terrain.treeBillboardDistance = 60f;
            terrain.treeCrossFadeLength = 5f;
            terrain.treeMaximumFullLODCount = 130;
            terrain.detailObjectDistance = 80f;
            terrain.detailObjectDensity = 0.6f;

            // detail 风力（Grass 渲染模式的 fern / bush 网格 + 草 billboard 共用）：压到"轻微摆动"
            ApplyDetailWindSettings(data);
            terrain.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            EnsureGrassTerrainLayer(data);

            if (firstTimeSeed)
            {
                SeedTrees(data);
                SeedDetails(data);
            }

            // 外围森林播种：每次 Bake 无条件执行，ForestSeeder 自清除并重建自己区间 [baseCount, ...) 的实例（幂等）。
            // 不触碰 firstTimeSeed 门、SeedTrees、SeedDetails、BuildScatterPrefabs、spawnXZ。
            ForestSeeder.Seed(data);

            // 轨迹净空（Phase 1.1c，必须在 ForestSeeder 之后）：把落在 MovementCorridor 净空带内的
            // 地形树横推出去 ——「轨迹上不要有障碍物」。放 SeedTrees 里无效（firstTimeSeed 门重焙不跑）；
            // ForestSeeder 对中央段按值原样写回，已推开的树不会被打回。幂等：已净空时零写入。
            NudgeTreesOutOfCorridors(terrain, data);

            // 环境 Pass 阶段 2：4 层 alphamap + owned detail + 专属岩石群（读最终 heightmap）
            TerrainEnvironmentSeeder.PopulateGround(data, terrainGO.transform);

            // Understory / Forest Detail Pass：林下层植被密度分区 + hero 灌木 + 资产门控接口
            // （必须在 GroundPass 之后：grass01 层由其先行重绘，本 Pass 在其基础上叠加分区密度；
            //   禁用时本 Pass 执行回滚：bush 层恢复 SeedDetails 基线、grass01 由 GroundPass 恢复）
            UnderstorySeeder.Seed(data, terrainGO.transform);

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] Terrain 构建完成（200m×200m，drawInstanced=true）。");
            return true;
        }

        /// <summary>
        /// 把 TerrainData 的 detail 散布模式归一化为 InstanceCountMode（detail map 每格 = 实例数）。
        ///
        /// 为什么必须显式归一化：Unity 2021.2+ 的 TerrainData 带 Detail Scatter Mode，
        /// `new TerrainData()` 取 CoverageMode —— 该模式下 map 值被解释为"覆盖比例"，
        /// 实例数改由 DetailPrototype.density（相对原型尺寸）推导。本项目所有 DetailPrototype
        /// 都由 `new DetailPrototype{...}` 构造，语义是"每格实例数"（SeedDetails 随机 2-6 基线、
        /// PaintDetailLayers 逐层写 0-8、验收侧 approxInstances = Σdensity × detailObjectDensity），
        /// 模式不匹配时会出现"GetDetailLayer 读回密度非 0，但一个 detail 都不画"的静默失效。
        ///
        /// 幂等：已是目标模式时只打印日志，不写任何数据；必须在任何 detail 写回之前调用。
        /// </summary>
        private static void EnsureDetailInstanceCountMode(TerrainData data)
        {
            DetailScatterMode before = data.detailScatterMode;
            if (before != DetailScatterMode.InstanceCountMode)
                data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

            Debug.Log($"[SceneBuilder] detailScatterMode: {before} → {data.detailScatterMode}" +
                      "（detail 层语义 = 每格实例数，与播种/回滚/验收口径同源）。");
        }

        // detail 风力目标值（绝对值，保证幂等；Unity 默认草风力是按"草叶"尺度调的）
        private const float DetailWindStrength = 0.20f;
        private const float DetailWindAmount = 0.30f;

        /// <summary>detail 风力目标值的对外只读视图（供 JurassicProjectSetup.ValidateProject 断言）。</summary>
        internal static float DetailWindStrengthTarget => DetailWindStrength;
        internal static float DetailWindAmountTarget => DetailWindAmount;

        /// <summary>
        /// 压低地形 detail 的风动振幅，保留轻微摆动。
        ///
        /// 症状：Understory 蕨类（Fern02，`DetailRenderMode.Grass`）在场景里"整株左右平移滑动"。
        /// Grass 模式会随风移动（Unity 文档：VertexLit 模式不随风移动），而 Terrain Settings 的
        /// *Wind Settings for Grass* 默认值是按草叶尺度调的 —— 套在 0.3-0.5m 高的蕨类网格上，
        /// 整株被推着来回滑移而非叶尖摆动。已排除枢轴嫌疑：Fern02_A 网格 y ∈ [-0.029, 0.258]，
        /// 枢轴距根部仅 2.9cm；也排除缩放/位置抖动（无脚本逐帧改动实例）。
        ///
        /// 处理：只把 wavingGrassStrength / wavingGrassAmount 写成固定目标值（绝对值 → 幂等，
        /// 重复 Bake 不会连乘衰减），保留 Grass 渲染模式与 WindZone（树冠摆动不变）。
        /// 若仍嫌大：再降这两个常量；最后一档才是把 fern 原型改 `DetailRenderMode.VertexLit`（完全静止）。
        /// </summary>
        private static void ApplyDetailWindSettings(TerrainData data)
        {
            float strengthBefore = data.wavingGrassStrength;
            float amountBefore = data.wavingGrassAmount;
            bool changed = !Mathf.Approximately(strengthBefore, DetailWindStrength)
                        || !Mathf.Approximately(amountBefore, DetailWindAmount);

            if (changed)
            {
                data.wavingGrassStrength = DetailWindStrength;
                data.wavingGrassAmount = DetailWindAmount;
            }

            Debug.Log($"[SceneBuilder] detail 风力（wavingGrass）: strength {strengthBefore:F3} → {data.wavingGrassStrength:F3}、" +
                      $"amount {amountBefore:F3} → {data.wavingGrassAmount:F3}（speed {data.wavingGrassSpeed:F3} 不动）、" +
                      $"WindZone 不动" + (changed ? "。" : "（与现有一致，跳过写入）。"));
        }

        private static float[,] GenerateHeightmap()
        {
            float[,] heights = new float[HeightmapRes, HeightmapRes];
            const float flatMinX = 0.15f, flatMaxX = 0.85f;
            const float flatMinZ = 0.15f, flatMaxZ = 0.95f;
            const float transitionWidth = 0.08f;

            for (int z = 0; z < HeightmapRes; z++)
            for (int x = 0; x < HeightmapRes; x++)
            {
                float nx = (float)x / (HeightmapRes - 1);
                float nz = (float)z / (HeightmapRes - 1);

                float distToFlat = DistanceToFlatRegion(nx, nz, flatMinX, flatMaxX, flatMinZ, flatMaxZ);
                float blend = Mathf.Clamp01(distToFlat / transitionWidth);

                float noise = Mathf.PerlinNoise(nx * 6f + 0.5f, nz * 6f + 0.5f) * 0.15f
                            + Mathf.PerlinNoise(nx * 12f + 1.3f, nz * 12f + 2.7f) * 0.05f;
                noise = Mathf.Max(0f, noise);

                heights[z, x] = noise * blend * blend;
            }
            return heights;
        }

        private static float DistanceToFlatRegion(float nx, float nz,
            float minX, float maxX, float minZ, float maxZ)
        {
            float dx = Mathf.Max(Mathf.Max(minX - nx, nx - maxX), 0f);
            float dz = Mathf.Max(Mathf.Max(minZ - nz, nz - maxZ), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static TerrainLayer EnsureGrassTerrainLayer(TerrainData data)
        {
            const string layerPath = TerrainLayerFolder + "/GrassLayer.terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }

            // 优先搜索 "ground" 关键词（NSk2 的 ground01.tga 是地面纹理），"grass" 可能命中草叶贴图
            Texture2D grassTex = NatureStarterKitLocator.FindGroundTexture("ground");
            if (grassTex == null)
                grassTex = NatureStarterKitLocator.FindGroundTexture("grass");

            if (grassTex == null)
            {
                Debug.LogWarning("[SceneBuilder] 未找到 NSk2 地面纹理，使用程序化绿色纹理作为 fallback。");
                EnsureFolder(MaterialFolder);
                string fallbackPath = MaterialFolder + "/GrassFallbackTexture.png";
                grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>(fallbackPath);
                if (grassTex == null)
                {
                    grassTex = new Texture2D(4, 4);
                    Color[] pixels = new Color[16];
                    for (int i = 0; i < 16; i++)
                        pixels[i] = new Color(0.19f, 0.39f, 0.12f);
                    grassTex.SetPixels(pixels);
                    grassTex.Apply();
                    System.IO.File.WriteAllBytes(
                        System.IO.Path.Combine(Application.dataPath, "..", fallbackPath),
                        grassTex.EncodeToPNG());
                    AssetDatabase.ImportAsset(fallbackPath);
                    grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>(fallbackPath);
                }
            }

            if (grassTex != null)
            {
                layer.diffuseTexture = grassTex;
                layer.tileSize = new Vector2(4f, 4f);
                layer.tileOffset = Vector2.zero;
            }
            Debug.Log($"[SceneBuilder] TerrainLayer 使用纹理: {(grassTex != null ? grassTex.name : "NULL")}");

            EditorUtility.SetDirty(layer);

            // 设置 terrainLayers 前确保 alphamapResolution 有效
            if (data.alphamapResolution <= 0)
                data.alphamapResolution = 256;

            data.terrainLayers = new[] { layer };

            // 关键修复：显式初始化 alphamap 权重为全 1（单图层铺满整个地形）
            int amWidth = data.alphamapWidth;
            int amHeight = data.alphamapHeight;
            float[,,] alphamap = new float[amHeight, amWidth, 1];
            for (int y = 0; y < amHeight; y++)
                for (int x = 0; x < amWidth; x++)
                    alphamap[y, x, 0] = 1f;
            data.SetAlphamaps(0, 0, alphamap);
            Debug.Log($"[SceneBuilder] Alphamap 初始化完成: {amWidth}x{amHeight}, 单图层权重=1.0");

            return layer;
        }

        // ---------------------------------------------------------------- Terrain 播种

        private static void SeedTrees(TerrainData data)
        {
            GameObject[] treePrefabs = NatureStarterKitLocator.FindTreePrefabs();
            if (treePrefabs.Length == 0)
            {
                Debug.LogWarning("[SceneBuilder] 未找到 NSK2 树 Prefab，跳过 Paint Trees。");
                return;
            }

            data.treePrototypes = Array.ConvertAll(treePrefabs, p => new TreePrototype
            {
                prefab = p,
                bendFactor = 0.1f
            });

            var random = new System.Random(GroundRandomSeed);
            var instances = new List<TreeInstance>();

            // NOTE: spawnXZ 列出全部恐龙出生点（含 enabled=false 的），同时用于树木/草丛/岩石的排除区
            // 以及 BuildScatterPrefabs 中环绕各出生点的灌木构图。此数组的【长度与内容】决定了
            // GroundRandomSeed 随机流的消费次数 —— 一旦改动，全部岩石与灌木会被确定性重排。
            // 因此禁用恐龙时【故意保留】其出生点，以保证重烘焙后散布物逐字节一致、diff 最小。
            Vector2[] spawnXZ = { new Vector2(0, 11), new Vector2(10, 27), new Vector2(-10, 19), new Vector2(8, 15), new Vector2(-13, 35) };
            Vector2 cameraSpawn = new Vector2(0, -16);

            int targetCount = 180;
            for (int i = 0; i < targetCount; i++)
            {
                float wx = (float)(random.NextDouble() * 180.0 - 90.0);
                float wz = (float)(random.NextDouble() * 180.0 - 90.0);

                bool excluded = false;
                foreach (Vector2 sp in spawnXZ)
                    if (Vector2.Distance(sp, new Vector2(wx, wz)) < 15f) { excluded = true; break; }
                if (!excluded && Vector2.Distance(cameraSpawn, new Vector2(wx, wz)) < 10f)
                    excluded = true;
                if (excluded) continue;

                float nx = (wx + TerrainSize / 2f) / TerrainSize;
                float nz = (wz + TerrainSize / 2f) / TerrainSize;
                float hy = data.GetInterpolatedHeight(nx, nz) / data.size.y;

                instances.Add(new TreeInstance
                {
                    position = new Vector3(nx, hy, nz),
                    widthScale = 0.8f + (float)random.NextDouble() * 0.5f,
                    heightScale = 0.8f + (float)random.NextDouble() * 0.5f,
                    rotation = (float)random.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                    prototypeIndex = random.Next(treePrefabs.Length)
                });
            }

            data.SetTreeInstances(instances.ToArray(), false);
            Debug.Log($"[SceneBuilder] Paint Trees: 播种 {instances.Count} 棵树（{treePrefabs.Length} 种 TreePrototype）。");
        }

        /// <summary>
        /// 轨迹净空 pass（Phase 1.1c，幂等）：把**落在任一路线净空带内**的地形树沿法线横推到带外，
        /// 由 BakeScene 在 ForestSeeder.Seed 之后无条件调用 —— SeedTrees 受 firstTimeSeed 幂等门
        /// 控制、重 Bake 不会重跑，放循环里等于没生效；放 ForestSeeder 之后则首焙/重焙都覆盖，
        /// 且 ForestSeeder 对中央段「按值原样写回」⇒ 已推开的树不会被它下一次比对打回。
        /// 只横移、不增删、不消费任何随机流 ⇒ 其余散布物逐字节一致；返回被推开的树数（0 = 已净空）。
        /// 净空按两条闭合中心线与树干实际缩放计算。
        /// </summary>
        internal static int NudgeTreesOutOfCorridors(Terrain terrain, TerrainData data)
        {
            TreeInstance[] instances = data.treeInstances;
            if (instances == null || instances.Length == 0)
                return 0;

            Vector3 terrainPos = terrain.transform.position;
            Vector3 size = data.size;
            bool changed = false;
            int movedCount = 0;

            for (int i = 0; i < instances.Length; i++)
            {
                TreeInstance ti = instances[i];
                float origX = ti.position.x;
                float origZ = ti.position.z;

                // 树的归一化坐标 → 世界 XZ（镜像 ValidateProject ⑦ 的 TransformPoint 换算）。
                Vector3 world = terrain.transform.TransformPoint(new Vector3(
                    ti.position.x * size.x, ti.position.y * size.y, ti.position.z * size.z));
                float wx = world.x;
                float wz = world.z;
                NudgeOutOfCorridors(ref wx, ref wz, i, ti.widthScale);
                if (Mathf.Approximately(wx, world.x) && Mathf.Approximately(wz, world.z))
                    continue;

                // 世界 XZ → 归一化坐标；Y 重新按高度图采样（与 SeedTrees 同源换算）。
                Vector3 local = terrain.transform.InverseTransformPoint(new Vector3(wx, 0f, wz));
                float nnx = local.x / size.x;
                float nnz = local.z / size.z;
                ti.position = new Vector3(nnx, data.GetInterpolatedHeight(nnx, nnz) / size.y, nnz);
                instances[i] = ti;
                changed = true;
                movedCount++;
            }

            if (changed)
            {
                data.SetTreeInstances(instances, false);
                Debug.Log($"[SceneBuilder] 轨迹净空：把 {movedCount} 棵树推出中心线安全距离。");
            }
            else
            {
                Debug.Log("[SceneBuilder] 轨迹净空：无需移动（全部地形树已在净空带外）。");
            }
            return movedCount;
        }

        /// <summary>
        /// 把树沿闭合中心线法线移到安全距离外；必要时迭代避开另一条路线。
        /// </summary>
        private static void NudgeOutOfCorridors(ref float wx, ref float wz, int treeIndex, float treeScale)
        {
            float limit = Mathf.Max(MovementCorridor.CenterlineTreeClearance,
                DinosaurAgent.TreeAvoidRadiusCap + 0.7f * treeScale + 0.3f);
            NudgeOutOfCorridorsWithClearance(ref wx, ref wz, treeIndex, limit);
        }

        internal static int NudgeSceneTreesOutOfCorridors(Terrain terrain)
        {
            int moved = 0;
            foreach (SceneTreeObstacle tree in UnityEngine.Object.FindObjectsOfType<SceneTreeObstacle>())
            {
                Vector3 position = tree.transform.position;
                float x = position.x, z = position.z;
                NudgeOutOfCorridorsWithClearance(ref x, ref z, moved,
                    Mathf.Max(MovementCorridor.CenterlineTreeClearance, DinosaurAgent.TreeAvoidRadiusCap + tree.Radius + .3f));
                if (Mathf.Approximately(x, position.x) && Mathf.Approximately(z, position.z)) continue;
                float heightDelta = terrain.SampleHeight(new Vector3(x, 0f, z)) - terrain.SampleHeight(position);
                tree.transform.position = new Vector3(x, position.y + heightDelta, z);
                EditorUtility.SetDirty(tree.transform);
                moved++;
            }
            TerrainTreeObstacleRegistry.Invalidate();
            Debug.Log($"[SceneBuilder] 场景树净空：移动 {moved} 棵挡路树。");
            return moved;
        }

        private static void NudgeOutOfCorridorsWithClearance(ref float wx, ref float wz, int treeIndex, float limit)
        {
            var point = new Vector2(wx, wz);
            for (int pass = 0; pass < 16; pass++)
            {
                bool moved = false;
                for (int route = 0; route < MovementCorridor.RouteCount; route++)
                {
                    MovementCorridor.ProjectToCorridor(route, point, out float arc, out float lateral);
                    MovementCorridor.GetSampleAt(route, arc, out Vector2 center, out Vector2 tangent);
                    if (Vector2.Distance(point, center) >= limit)
                        continue;

                    Vector2 normal = new Vector2(-tangent.y, tangent.x);
                    Vector2 left = center + normal * (limit + 2f);
                    Vector2 right = center - normal * (limit + 2f);
                    float leftScore = float.MaxValue, rightScore = float.MaxValue;
                    for (int other = 0; other < MovementCorridor.RouteCount; other++)
                    {
                        leftScore = Mathf.Min(leftScore, MovementCorridor.MinDistanceToRoute(left, other));
                        rightScore = Mathf.Min(rightScore, MovementCorridor.MinDistanceToRoute(right, other));
                    }
                    point = leftScore > rightScore + 0.01f ? left :
                        rightScore > leftScore + 0.01f ? right :
                        ((treeIndex & 1) == 0 ? left : right);
                    moved = true;
                }
                if (!moved)
                    break;
            }
            wx = point.x;
            wz = point.y;
        }

        // ---------------------------------------------------------------- 共享基线设施（Understory 回滚同源复刻）

        // 恐龙出生点（XZ）：树/草/岩石排除区与环绕构图的契约数据。长度与内容决定 GroundRandomSeed 随机流
        // 消费次数——禁止改动（含顺序）。internal 供 UnderstorySeeder 回滚时复用同一份定义。
        internal static readonly Vector2[] DinosaurSpawnPoints =
        {
            new Vector2(0, 11), new Vector2(10, 27), new Vector2(-10, 19), new Vector2(8, 15), new Vector2(-13, 35)
        };

        // SeedDetails 的 bush 基线随机流（GroundRandomSeed + 1）。UnderstorySeeder 禁用回滚时按同一构造重放
        // 该流（逐层升序、每层一次 BuildBushBaselineDensity），逐格恢复 SeedDetails 基线。
        internal static System.Random CreateBushBaselineRandom()
        {
            return new System.Random(GroundRandomSeed + 1);
        }

        // 复刻 SeedDetails 的逐格基线算法：每格 random.Next(2,6)，出生点 20m 内减半（Max(1, v/2)）。
        // 严禁在 SeedDetails 内再内联复制该算法——播种与回滚必须共用同一实现，否则回滚精度不成立。
        internal static int[,] BuildBushBaselineDensity(System.Random random, int res, Vector2[] spawnXZ)
        {
            int[,] density = new int[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                density[x, z] = random.Next(2, 6);

                float wx = (float)x / res * TerrainSize - TerrainSize / 2f;
                float wz = (float)z / res * TerrainSize - TerrainSize / 2f;
                foreach (Vector2 sp in spawnXZ)
                {
                    if (Vector2.Distance(sp, new Vector2(wx, wz)) < 20f)
                    {
                        density[x, z] = Mathf.Max(1, density[x, z] / 2);
                        break;
                    }
                }
            }
            return density;
        }

        private static void SeedDetails(TerrainData data)
        {
            GameObject[] detailMeshes = NatureStarterKitLocator.FindDetailMeshes();
            if (detailMeshes.Length == 0)
            {
                Debug.LogWarning("[SceneBuilder] 未找到 NSK2 草/蕨类 Mesh，跳过 Paint Details。");
                return;
            }

            data.SetDetailResolution(DetailRes, DetailResPerPatch);

            foreach (GameObject mesh in detailMeshes)
            {
                Renderer[] renderers = mesh.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    if (r.sharedMaterial != null && !r.sharedMaterial.enableInstancing)
                    {
                        r.sharedMaterial.enableInstancing = true;
                        EditorUtility.SetDirty(r.sharedMaterial);
                    }
                }
            }

            data.detailPrototypes = Array.ConvertAll(detailMeshes, m => new DetailPrototype
            {
                usePrototypeMesh = true,
                prototype = m,
                renderMode = DetailRenderMode.Grass,
                healthyColor = new Color(0.35f, 0.65f, 0.25f),
                dryColor = new Color(0.55f, 0.60f, 0.25f),
                minWidth = 0.6f,
                maxWidth = 1.4f,
                minHeight = 0.6f,
                maxHeight = 1.4f,
                noiseSpread = 0.15f,
                density = 1, // CoverageMode 下的实例数倍率基准（InstanceCountMode 下无副作用；写整数字面量以兼容 int/float 声明）
            });

            // bush 基线算法与 spawn 点已抽为共享 helper（BuildBushBaselineDensity / DinosaurSpawnPoints）：
            // UnderstorySeeder 回滚时按同一 RNG 流重放该实现，保证逐格一致——禁止再内联复制。
            var random = CreateBushBaselineRandom();
            int res = data.detailResolution;

            for (int layerIdx = 0; layerIdx < data.detailPrototypes.Length; layerIdx++)
            {
                data.SetDetailLayer(0, 0, layerIdx, BuildBushBaselineDensity(random, res, DinosaurSpawnPoints));
            }

            Debug.Log($"[SceneBuilder] Paint Details: {data.detailPrototypes.Length} 层 Detail Mesh，分辨率 {res}×{res}。");
        }

        // ---------------------------------------------------------------- 环境散布

        private static void BuildScatterPrefabs()
        {
            var scatterRoot = new GameObject("Environment Props");
            var random = new System.Random(GroundRandomSeed + 2);

            GameObject[] bushPrefabs = NatureStarterKitLocator.FindDetailMeshes()
                .Where(p => p.name.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            // 中央 12 个大岩石：资产门控换成 HQ 岩石 prefab（无 prefab 时退回原程序化 Sphere）。
            // 兜底材质只在需要时创建：有 HQ prefab 时完全跳过，不再每 Bake 无谓改写 RockNatural.mat。
            GameObject[] rockPrefabs = UnderstoryAssetLocator.FindRockPrefabs();
            Material fallbackRockMat = rockPrefabs.Length == 0 ? EnsureFallbackRockMaterial() : null;

            Terrain scatterTerrain = UnityEngine.Object.FindObjectOfType<Terrain>();
            ScatterCentralRocks(scatterRoot, scatterTerrainData: scatterTerrain != null ? scatterTerrain.terrainData : null,
                scatterTerrainTransform: scatterTerrain != null ? scatterTerrain.transform : null,
                random, fallbackRockMat, rockPrefabs);

            if (bushPrefabs.Length > 0)
            {
                for (int i = 0; i < ScatterSpawnXZ.Length; i++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                        float dist = 12f + (float)random.NextDouble() * 6f;
                        float bx = ScatterSpawnXZ[i].x + Mathf.Cos(angle) * dist;
                        float bz = ScatterSpawnXZ[i].y + Mathf.Sin(angle) * dist;

                        GameObject bushPrefab = bushPrefabs[random.Next(bushPrefabs.Length)];
                        GameObject bush = (GameObject)PrefabUtility.InstantiatePrefab(bushPrefab, scatterRoot.transform);
                        bush.name = "FeaturePlant_" + i + "_" + j;
                        bush.transform.position = new Vector3(bx, 0f, bz);
                        bush.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                        float scale = 1.2f + (float)random.NextDouble() * 0.8f;
                        bush.transform.localScale = Vector3.one * scale;

                        foreach (Collider col in bush.GetComponentsInChildren<Collider>(true))
                            UnityEngine.Object.DestroyImmediate(col);

                        GameObjectUtility.SetStaticEditorFlags(bush, StaticEditorFlags.BatchingStatic);
                    }
                }
            }

            Debug.Log($"[SceneBuilder] Environment Props: {scatterRoot.transform.childCount} 个大型岩石/构图植物已放置。");
        }

        // ---------------------------------------------------------------- 中央岩石（资产门控 + 空间占位语义）

        /// <summary>构图用出生点（岩石避让 + 灌木环绕中心）。内容决定 GroundRandomSeed+2 随机流的消费次数，勿改。</summary>
        private static readonly Vector2[] ScatterSpawnXZ =
        {
            new Vector2(0, 11), new Vector2(10, 27), new Vector2(-10, 19), new Vector2(8, 15), new Vector2(-13, 35)
        };

        /// <summary>HQ 岩石贴地后的固定 sink（米，常量）：不引入随机性，故不影响 placement RNG。</summary>
        private const float CentralRockSink = 0.05f;

        /// <summary>单个中央岩石的 footprint 记录（供 PolyHavenPassPreview 报告，验证替换前后占位一致）。</summary>
        internal struct CentralRockFootprint
        {
            public string Name;
            public float TargetFootprint; // 原程序化 Sphere 的世界水平占位直径
            public float ActualFootprint; // HQ 替换后的世界水平 footprint
        }

        /// <summary>最近一次 ScatterCentralRocks 的 footprint 记录（进程内传递；Preview 报告直接读取）。</summary>
        internal static readonly List<CentralRockFootprint> LastCentralRockFootprints = new List<CentralRockFootprint>();

        /// <summary>
        /// 中央 12 个大岩石放置（"Environment Props" 下的 Rock_Large_*）。
        ///
        /// 【只换资产、不换布局】：root 名、Rock_Large_* 命名、位置、循环结构、
        /// 以及 RNG 消费次数与顺序（x,z → 3 次 scale → 3 次 rotation）全部与替换前一致；
        /// prefab 选择用索引取模，不额外消费 RNG。因此灌木构图与中央活动区布局逐字节不变。
        ///
        /// 【空间占位语义保持】：不做 `localScale = randomScale` 的粗暴替换。
        /// HQ prefab 用 mesh bounds 求原生 XZ footprint，再算确定性 uniform normalization scale：
        ///     finalScale = targetFootprint(原 Sphere 世界水平直径，已含原随机缩放) / sourceFootprint
        /// 只允许 uniform scale（随机因子已折进 targetFootprint，不重复相乘）；随后按 mesh bounds.min.y
        /// 做 ground anchoring 使岩石底部贴地。这样 DinosaurSpawnRandomizer 读取 Environment Props 的
        /// renderer bounds 作开局避让圆时，尺寸语义与原 Sphere 实质一致。
        ///
        /// 提取为 internal 方法是为了让 PolyHavenPassPreview 能在临时场景里复用【同一条生产代码路径】。
        /// </summary>
        internal static void ScatterCentralRocks(GameObject parent, TerrainData scatterTerrainData,
            Transform scatterTerrainTransform, System.Random random, Material fallbackMaterial, GameObject[] rockPrefabs)
        {
            bool usePrefabs = rockPrefabs != null && rockPrefabs.Length > 0 && scatterTerrainData != null;

            int prefabCount = usePrefabs ? rockPrefabs.Length : 0;
            var sourceFootprint = new float[prefabCount];
            var sourceMinY = new float[prefabCount];
            for (int i = 0; i < prefabCount; i++)
            {
                MeshFilter filter = rockPrefabs[i].GetComponentInChildren<MeshFilter>();
                Bounds meshBounds = filter != null && filter.sharedMesh != null
                    ? filter.sharedMesh.bounds
                    : new Bounds(Vector3.zero, Vector3.one);
                sourceFootprint[i] = Mathf.Max(0.001f, Mathf.Max(meshBounds.size.x, meshBounds.size.z));
                sourceMinY[i] = meshBounds.min.y;
            }

            LastCentralRockFootprints.Clear();
            for (int i = 0; i < 12; i++)
            {
                float x = (float)(random.NextDouble() * 160.0 - 80.0);
                float z = (float)(random.NextDouble() * 160.0 - 80.0);

                bool tooClose = false;
                foreach (Vector2 sp in ScatterSpawnXZ)
                    if (Vector2.Distance(sp, new Vector2(x, z)) < 6f) { tooClose = true; break; }
                if (tooClose) continue;

                // 3 次 scale + 3 次 rotation：消费次数与顺序与替换前完全一致。
                double rawScaleX = random.NextDouble();
                double rawScaleY = random.NextDouble();
                double rawScaleZ = random.NextDouble();
                float rotX = (float)random.NextDouble() * 15f;
                float rotY = (float)random.NextDouble() * 360f;
                float rotZ = (float)random.NextDouble() * 10f;

                // 原程序化 Sphere 的 localScale（primitive mesh 直径为 1，故 world 直径 == localScale）。
                var originalSphereScale = new Vector3(
                    2.5f + 2f * (float)rawScaleX,
                    0.8f + 0.6f * (float)rawScaleY,
                    2.0f + 1.5f * (float)rawScaleZ);
                float targetFootprint = Mathf.Max(originalSphereScale.x, originalSphereScale.z);

                if (usePrefabs)
                {
                    int index = i % rockPrefabs.Length; // 索引取模：不额外消费 RNG
                    GameObject rock = (GameObject)PrefabUtility.InstantiatePrefab(rockPrefabs[index], parent.transform);
                    rock.name = "Rock_Large_" + i.ToString("D2");

                    float finalScale = targetFootprint / sourceFootprint[index];
                    rock.transform.localScale = Vector3.one * finalScale;
                    rock.transform.rotation = Quaternion.Euler(rotX, rotY, rotZ);

                    float nx = (x + scatterTerrainData.size.x / 2f) / scatterTerrainData.size.x;
                    float nz = (z + scatterTerrainData.size.z / 2f) / scatterTerrainData.size.z;
                    float groundY = scatterTerrainTransform.position.y + scatterTerrainData.GetInterpolatedHeight(nx, nz);
                    rock.transform.position = new Vector3(
                        x, groundY - sourceMinY[index] * finalScale - CentralRockSink, z);

                    foreach (Collider col in rock.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(col);
                    GameObjectUtility.SetStaticEditorFlags(rock, StaticEditorFlags.BatchingStatic);

                    Renderer renderer = rock.GetComponentInChildren<Renderer>();
                    Vector3 size = renderer != null ? renderer.bounds.size : Vector3.zero;
                    LastCentralRockFootprints.Add(new CentralRockFootprint
                    {
                        Name = rock.name,
                        TargetFootprint = targetFootprint,
                        ActualFootprint = Mathf.Max(size.x, size.z)
                    });
                }
                else
                {
                    // 兜底：无 HQ 岩石 prefab 时保持替换前的程序化 Sphere 行为不变。
                    var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    rock.name = "Rock_Large_" + i.ToString("D2");
                    rock.transform.SetParent(parent.transform);
                    rock.transform.position = new Vector3(x, 0.3f, z);
                    rock.transform.localScale = originalSphereScale;
                    rock.transform.rotation = Quaternion.Euler(rotX, rotY, rotZ);

                    if (fallbackMaterial != null)
                        rock.GetComponent<Renderer>().sharedMaterial = fallbackMaterial;

                    Collider col = rock.GetComponent<Collider>();
                    if (col != null)
                        UnityEngine.Object.DestroyImmediate(col);

                    GameObjectUtility.SetStaticEditorFlags(rock, StaticEditorFlags.BatchingStatic);
                }
            }

            if (!usePrefabs)
                Debug.LogWarning("[SceneBuilder] 未找到 HQ 岩石 prefab（Assets/Vegetation/Rocks），" +
                                 "Environment Props 退回程序化 Sphere 占位。");
        }

        /// <summary>
        /// Preview 专用入口：用与生产完全相同的种子重放中央 12 岩石的放置。
        /// 岩石循环是 BuildScatterPrefabs 中 GroundRandomSeed+2 随机流的【首个】消费者，
        /// 故独立新建同种子 Random 即得到与生产逐项一致的前缀序列（不改变生产随机契约）。
        /// </summary>
        internal static void ScatterCentralRocksForPreview(GameObject parent, TerrainData data,
            Transform terrainTransform, GameObject[] rockPrefabs)
        {
            ScatterCentralRocks(parent, data, terrainTransform,
                new System.Random(GroundRandomSeed + 2), null, rockPrefabs);
        }

        /// <summary>
        /// 兜底岩石材质：仅在无 HQ 岩石 prefab 时使用，保持替换前对 RockNatural.mat 的原有配置。        /// 有 HQ prefab 时完全跳过 → 不再每 Bake 无谓改写该材质资产。
        /// </summary>
        private static Material EnsureFallbackRockMaterial()
        {
            if (!NatureStarterKitLocator.IsImported)
                return null;
            Texture2D groundTex = NatureStarterKitLocator.FindGroundTexture("ground");
            if (groundTex == null)
                return null;

            EnsureFolder(MaterialFolder);
            Material rockMat = LoadOrCreateStandardMaterial(MaterialFolder + "/RockNatural.mat");
            rockMat.mainTexture = groundTex;
            rockMat.mainTextureScale = new Vector2(2f, 2f);
            rockMat.SetFloat("_Glossiness", 0.02f);
            EditorUtility.SetDirty(rockMat);
            return rockMat;
        }

        // ---------------------------------------------------------------- HUD

        private static void BuildHud(Font font)
        {
            int expectedCount = EnabledDinosaurCount;
            var root = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            Text help = MakeText(root.transform, "Help", TextAnchor.LowerLeft, 24, font);
            RectTransform helpRect = help.rectTransform;
            helpRect.anchorMin = new Vector2(0f, 0f);
            helpRect.anchorMax = new Vector2(0f, 0f);
            helpRect.pivot = new Vector2(0f, 0f);
            helpRect.anchoredPosition = new Vector2(34f, 30f);
            helpRect.sizeDelta = new Vector2(820f, 100f);
            help.text = "W/S：前进/后退    A/D：转向并前进    按住鼠标右键：自由观察\n空格键：召唤画面中的恐龙";

            Text status = MakeText(root.transform, "Status", TextAnchor.UpperRight, 24, font);
            RectTransform statusRect = status.rectTransform;
            statusRect.anchorMin = new Vector2(1f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(1f, 1f);
            statusRect.anchoredPosition = new Vector2(-268f, -30f);
            statusRect.sizeDelta = new Vector2(980f, 90f);
            status.text = $"正在加载 {expectedCount} 种恐龙模型…";

            Text error = MakeText(root.transform, "Error", TextAnchor.UpperCenter, 27, font);
            error.color = new Color(1f, 0.45f, 0.35f);
            RectTransform errorRect = error.rectTransform;
            errorRect.anchorMin = new Vector2(0.5f, 1f);
            errorRect.anchorMax = new Vector2(0.5f, 1f);
            errorRect.pivot = new Vector2(0.5f, 1f);
            errorRect.anchoredPosition = new Vector2(0f, -30f);
            errorRect.sizeDelta = new Vector2(1300f, 100f);

            HudController hud = root.AddComponent<HudController>();
            SetField(hud, "errorText", error);
            SetField(hud, "statusText", status);
            SetField(hud, "helpText", help);
            SetField(hud, "expectedDinosaurCount", expectedCount);
            root.AddComponent<WorldNavigationHud>().BuildUi(font);
            BuildInfoPanel(root.transform, font);
            EditorUtility.SetDirty(hud);
        }

        private static Text MakeText(Transform parent, string name, TextAnchor alignment, int size, Font font)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Font LoadBuiltinFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                Debug.LogError("[SceneBuilder] 无法加载内置字体（LegacyRuntime.ttf / Arial.ttf），HUD 与信息卡文字可能缺失。");
            return font;
        }

        // ---------------------------------------------------------------- 恐龙

        private static void BuildDinosaurs(Camera camera, Font font)
        {
            StegosaurusSceneIntegration.EnsureAssets();
            foreach (DinosaurTemplate template in CreateTemplates())
            {
                if (!template.enabled)
                {
                    Debug.Log($"[SceneBuilder] 跳过已禁用恐龙：{template.displayName}（{template.objectName}）");
                    continue;
                }

                BuildDinosaur(template, camera, font);
            }
        }

        private static DinosaurAgent BuildDinosaur(DinosaurTemplate t, Camera camera, Font font)
        {
            var root = new GameObject(t.objectName);
            root.transform.position = t.position;

            DinosaurAgent agent = root.AddComponent<DinosaurAgent>();
            SetField(agent, "viewCamera", camera);
            SetField(agent, "displayName", t.displayName);
            SetField(agent, "minimapPortrait", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/DinosaurPortraits/" + t.objectName + ".png"));
            SetField(agent, "movementHeight", t.position.y);
            SetField(agent, "viewFocusHeight", t.focusHeight);
            SetField(agent, "visualYaw", t.yaw);
            SetField(agent, "walkSpeed", t.walkSpeed);
            SetField(agent, "moveAnimationGroundSpeed", t.walkSpeed);
            SetField(agent, "approachSpeed", Mathf.Max(t.walkSpeed * 1.35f, t.walkSpeed + 0.5f));
            SetField(agent, "description", t.description);
            // 行为级碰撞契约：collisionRadius 只影响恐龙互斥（IsPeerBlocked 圆域查询）；
            // 树干避让由 DinosaurAgent 侧 Min(collisionRadius, DinoTreeAvoidRadiusCap=2.5f) 钳制，
            // 大体型恐龙（如阿根廷龙 collisionRadius=8）躯干可视觉探入树冠但树干附近仍可通行。
            // 字段缺失时 SetField 已有优雅报错兜底（不崩）。
            SetField(agent, "collisionRadius", t.collisionRadius);
            // 行为级碰撞契约：另一代理在 DinosaurAgent.cs 中添加同名 [SerializeField] private bool groundObstacleAware = true;
            // 字段名必须逐字符一致（反射写入，编译期无依赖）；飞行单位写 false，不被地面树干阻挡。
            SetField(agent, "groundObstacleAware", t.groundObstacleAware);
            EditorUtility.SetDirty(agent);

            var visualRoot = new GameObject(t.displayName + " Visual Root");
            visualRoot.transform.SetParent(root.transform, false);

            GameObject model = InstantiateModel(t, visualRoot.transform);
            if (model != null && !t.preserveSourceMaterials)
                ApplyBakedMaterials(model, t);

            // Fit 只在烘焙时算一次并把缩放/位置写入 transform（非幂等，运行期绑定器不再调用）。
            if (model != null && t.targetLength > 0f &&
                !DinosaurModelFitter.Fit(visualRoot, root.transform, t.targetLength, t.alignBottom))
            {
                Debug.LogError("[SceneBuilder] " + t.displayName + " 无法计算模型尺寸（Fit 失败）。");
            }

            // 动画姿势感知接地 + 步幅校准：Fit 用绑定姿势 AABB 对齐地面，运行期播放动画后脚会悬空；
            // 这里在烘焙时按「脚部接触几何」把 visualRoot 绝对对齐到地面 y，并顺带用 move clip 估算动画原生地速。
            if (model != null && t.alignBottom && t.enableFootLock)
                GroundFeetByAnimation(visualRoot, model, root.transform, agent, t);

            DinosaurFbxLoader loader = root.AddComponent<DinosaurFbxLoader>();
            SetField(loader, "modelRoot", visualRoot);
            SetField(loader, "displayName", t.displayName);
            SetField(loader, "idleAnimationHint", t.idleHint);
            SetField(loader, "moveAnimationHint", t.moveHint);
            SetField(loader, "enableFootLock", t.enableFootLock);
            SetField(loader, "footBoneNames", t.footBoneNames);
            SetField(loader, "footLockJointDepth", t.footLockJointDepth);
            SetField(loader, "releaseFootLockOnRise", t.releaseFootLockOnRise);
            SetField(loader, "footContactHeight", t.footContactHeight);
            SetField(loader, "footRiseThreshold", t.footRiseThreshold);
            SetField(loader, "footLockDistanceLimit", t.footLockDistanceLimit);
            EditorUtility.SetDirty(loader);
            return agent;
        }

        // ---------------------------------------------------------------- 烘焙期脚部接地对齐 + 步幅/地速校准

        // 一次动画采样同时得出两组结论（复用同一批 BakeMesh，避免二次开销）：
        //   (a) 每个 clip 的「脚部接触最低点」→ 把 visualRoot 绝对对齐到地面 y（修「脚悬空」）；
        //   (b) move clip 的「原生地速」= 2 × 单步行程 ÷ 周期 → 写入 agent.moveAnimationGroundSpeed（修「滑步」）。
        //
        // 接触点度量通道优先级（自上而下退化）：
        //   footVertex —— BakeMesh 形变后顶点里「蒙皮权重归属脚骨及其子骨」的那批（真正的脚底/趾尖几何，最准）；
        //   footBone   —— 脚骨原点最低点（关节原点普遍高于接触面，仅作次级通道）；
        //   meshBounds —— 整网格最低点（旧行为：最低点常是尾尖/下颌，会把模型整体抬高 → 只在脚骨全找不到时兜底 + 告警）。
        // 只加位移、绝不重复 Fit、绝不改缩放（Fit 非幂等；运行期严禁再改缩放）。
        private static void GroundFeetByAnimation(GameObject visualRoot, GameObject model, Transform rootAnchor,
            DinosaurAgent agent, DinosaurTemplate t)
        {
            // 1) 取动画组件（Legacy Animation）。
            Animation animation = model.GetComponentInChildren<Animation>(true);
            if (animation == null)
            {
                Debug.LogWarning("[GroundFeet] " + t.displayName + " 缺少 Legacy Animation，跳过接地校正。");
                return;
            }

            // 2) 解析接地片段（镜像 DinosaurAgent.FindClip：OrdinalIgnoreCase 匹配、排除 _RM 根运动）。
            //    idleClip 就是运行期 Idle 状态实际播放的片段 → 作为接地对齐基准（不再取多 clip 全局最低点）；
            //    moveClip 用于约束行走支撑脚不穿地，并作为原生地速估算对象。
            AnimationClip idleClip = ResolveGroundClip(animation, new[] { t.idleHint, "idle", "stand", "wait" });
            if (idleClip == null)
                idleClip = ResolveFirstClip(animation); // idle 缺失时用第一个片段兜底
            AnimationClip moveClip = ResolveGroundClip(animation, new[] { t.moveHint, "run", "walk", "fly", "animation" });

            // 组装待采样片段：idleClip 为基准，moveClip 若与 idleClip 不同则加入（去重）。
            var probes = new List<ClipFootProbe>();
            if (idleClip != null)
                probes.Add(new ClipFootProbe { clip = idleClip, role = "idle" });
            if (moveClip != null && moveClip != idleClip)
                probes.Add(new ClipFootProbe { clip = moveClip, role = "move" });
            if (probes.Count == 0)
            {
                Debug.LogWarning("[GroundFeet] " + t.displayName + " 未解析到可用动画片段，保留 Fit 结果，跳过接地校正。");
                return;
            }

            // 3) 地面高度基准（地面恐龙为 0）与模型高度（各种容差/钳制的比例基准）。
            float groundY = rootAnchor.position.y;
            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            float modelHeight = MeasureModelHeight(renderers);

            // 4) 脚骨发现：直接复用运行期 DinosaurFootLock.DiscoverFeet（显式名 → 命名启发式 → 几何兜底），
            //    保证「烘焙接地基准」与「运行期落脚锁定骨骼」永远是同一批，两处不会各说各话。
            var feet = new List<Transform>();
            Bounds modelBounds;
            DinosaurFootLock.DiscoverFeet(visualRoot.transform, t.footBoneNames, feet, out modelBounds);
            if (feet.Count == 0)
            {
                Debug.LogWarning($"[GroundFeet] {t.displayName}: 脚骨发现失败（显式名 [{DescribeNames(t.footBoneNames)}] 与命名/几何启发式均无结果），" +
                    "退回整网格最低点接地（旧行为，最低点可能是尾尖/下颌而非脚）。");
                GroundByWholeMeshLowest(visualRoot, animation, probes, renderers, groundY, modelHeight, t);
                return;
            }

            // 5) 资源缓存：只取一次 SkinnedMeshRenderer[]，绑定姿势下一次性算出「脚区顶点索引」，
            //    采样时只遍历这批索引（而非整网格），tempMesh / vertexBuffer 循环内复用。
            SkinnedMeshRenderer[] smrs = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            List<FootVertexRegion> regions = CollectFootVertexRegions(smrs, feet);
            if (regions.Count == 0)
                Debug.LogWarning($"[GroundFeet] {t.displayName}: 取不到脚区蒙皮顶点（网格不可读或无蒙皮权重），" +
                    "接地基准退化为脚骨原点最低点（关节原点高于接触面，脚掌可能略微陷入地面）。");
            Transform bodyReference = ResolveBodyReference(animation, modelBounds);
            Mesh tempMesh = new Mesh();
            var vertexBuffer = new List<Vector3>();
            float bindFootVertexY;
            float bindFootBoneY;
            try
            {
                // 绑定姿势基准（纯诊断用）：Fit 之后脚区几何离地面还有多远。
                bindFootVertexY = MeasureFootVertexLowest(regions, tempMesh, vertexBuffer);
                bindFootBoneY = MeasureFootBoneLowest(feet);

                // 6) 逐 clip 多帧采样：每帧同时记录脚区接触最低点与脚相对骨盆的水平轨迹（后者用于步幅估算）。
                //    AnimationMode 全局态守卫：仅当此前不在动画模式时才由本方法开启/关闭，避免打断用户动画预览。
                bool ownsAnimationMode = !AnimationMode.InAnimationMode();
                if (ownsAnimationMode)
                    AnimationMode.StartAnimationMode();
                try
                {
                    foreach (ClipFootProbe probe in probes)
                    {
                        ProbeClipFeet(animation, probe, feet, regions, bodyReference, tempMesh, vertexBuffer, t.displayName);
                        Debug.Log($"[GroundFeet] {t.displayName} clip '{probe.clip.name}'({probe.role}): " +
                            $"footVertexLowest={FormatMeasured(probe.footVertexLowest)} footBoneLowest={FormatMeasured(probe.footBoneLowest)} " +
                            $"stepLength={probe.stepLength:F3} clipLength={probe.clip.length:F2}s cycleSpeed={FormatMeasured(probe.nativeGroundSpeed)}");
                        // 步态测速可信度告警（仅 move clip）：跨脚速度离散度过大时给出显式线索（CSV 同目录取证）。
                        if (probe.role == "move" && probe.stanceGroundSpeed > 0f)
                        {
                            if (probe.stanceSpeedSpread > 0.25f)
                                Debug.LogWarning($"[GroundFeet] {t.displayName} clip '{probe.clip.name}': 各脚支撑速度离散度 {(probe.stanceSpeedSpread * 100f):F0}%（左右不对称或支撑识别不稳），" +
                                    "测速可信度下降，请结合 footprobe CSV 复核。");
                        }
                    }
                }
                finally
                {
                    if (ownsAnimationMode && AnimationMode.InAnimationMode())
                        AnimationMode.StopAnimationMode(); // 复位姿态，绝不把采样姿势烘进场景
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempMesh); // 统一释放一次，避免 Mesh 泄漏
            }

            // 7) 接地对齐：基准 = idle clip 的脚部接触最低点，绝对对齐到 groundY（只加位移）。
            ClipFootProbe idleProbe = probes[0];
            ClipFootProbe moveProbe = probes.Count > 1 ? probes[1] : null;
            float idleLowest = idleProbe.ContactLowest;
            if (idleLowest == float.MaxValue)
            {
                Debug.LogError("[GroundFeet] " + t.displayName + " 脚部接触点无有效测量值，接地静默失效，保留 Fit 结果不做位移。");
                return;
            }

            // move clip 只作为「支撑脚穿地不超容差」的下限约束：行走片段个别帧脚下探属动画常态，
            // 若直接取两 clip 全局最低点会把整只模型抬高，反而让占比更高的 idle 姿态悬空。
            float dy = groundY - idleLowest;
            float penetrationTolerance = Mathf.Max(0.03f, modelHeight * 0.02f);
            float walkContactY = float.NaN;
            if (moveProbe != null && moveProbe.ContactLowest != float.MaxValue)
            {
                dy = Mathf.Max(dy, groundY - moveProbe.ContactLowest - penetrationTolerance);
                walkContactY = moveProbe.ContactLowest + dy; // 对齐后行走支撑脚的世界高度，应 ≈ 0
            }

            // dy 安全钳制：脚部测量若意外命中非足部几何，位移会失控 → 超限则放弃位移。
            float limit = Mathf.Max(modelHeight * 0.2f, 0.3f);
            if (Mathf.Abs(dy) > limit)
            {
                Debug.LogWarning($"[GroundFeet] {t.displayName}: dy={dy:F3} 超出安全上限 {limit:F3}，脚部测量可疑，保留 Fit 结果不位移。");
                return;
            }
            visualRoot.transform.position += Vector3.up * dy;

            // 8) 脚锁休眠防护：运行期 plant 条件为「最低脚骨 clearance <= releaseHeight」，
            // 若模板唤醒阈值低于行走支撑相最低脚骨离地（footBoneLowest + dy），脚锁在整个行走期间
            // 都不会触发（滑移无人兜底）。显式告警，杜绝未来体长/模板变化后的静默休眠。
            if (t.enableFootLock && moveProbe != null && moveProbe.footBoneLowest != float.MaxValue)
            {
                float walkBoneClearance = moveProbe.footBoneLowest + dy;
                float lockReleaseHeight = t.footContactHeight > 0f ? t.footContactHeight : Mathf.Max(0.04f, modelHeight * 0.04f);
                if (lockReleaseHeight < walkBoneClearance)
                    Debug.LogWarning($"[GroundFeet] {t.displayName}: 脚锁唤醒阈值 releaseHeight={lockReleaseHeight:F3}（" +
                        (t.footContactHeight > 0f ? $"footContactHeight={t.footContactHeight:F3}" : "默认 4% 体高") +
                        $"）低于行走支撑相最低脚骨离地 {walkBoneClearance:F3}，脚锁行走时不会触发（滑移无人兜底）。" +
                        $"建议 footContactHeight≈{walkBoneClearance + 0.03f:F2}。");
            }

            // 9) 步幅/地速校准：把 move clip 的估算原生地速写入 agent.moveAnimationGroundSpeed（修滑步次因）；
            //    同一时机顺带写入「最优循环窗口」（修接缝相位跳的小弹跳，见 FindBestLoopWindow）。
            float nativeSpeed = -1f;
            if (t.syncMoveAnimationSpeed && moveProbe != null)
            {
                nativeSpeed = ApplyNativeGroundSpeed(agent, t, moveProbe, modelHeight);
                ApplyLoopWindow(agent, t, moveProbe);
            }

            // 10) 汇总日志（验证证据）：脚骨数、所选通道、绑定姿势双基准、每 clip 接触最低点、dy、行走支撑脚高度、步幅/原生地速。
            Debug.Log($"[GroundFeet] {t.displayName}: clips=[{DescribeClips(probes)}] footBones={feet.Count} channel={idleProbe.Channel} " +
                $"modelHeight={modelHeight:F3} bindFootVertexY={FormatMeasured(bindFootVertexY)} bindFootBoneY={FormatMeasured(bindFootBoneY)} " +
                $"animLowestY={idleLowest:F3} moveLowestY={FormatMeasured(moveProbe != null ? moveProbe.ContactLowest : float.MaxValue)} " +
                $"dy={dy:F3} walkContactY={FormatMeasured(walkContactY)} " +
                $"stepLength={(moveProbe != null ? moveProbe.stepLength.ToString("F3") : "n/a")} " +
                $"stanceSpeed={(moveProbe != null && moveProbe.stanceGroundSpeed > 0f ? moveProbe.stanceGroundSpeed.ToString("F3") : "n/a")} " +
                $"duty={(moveProbe != null && moveProbe.stanceDuty > 0f ? moveProbe.stanceDuty.ToString("F3") : "n/a")} " +
                $"spread={(moveProbe != null && moveProbe.stanceSpeedSpread >= 0f ? (moveProbe.stanceSpeedSpread * 100f).ToString("F1") + "%" : "n/a")} " +
                $"moveAnimationGroundSpeed={(nativeSpeed > 0f ? nativeSpeed.ToString("F3") : t.walkSpeed.ToString("F3") + "(unchanged)")} " +
                $"walkSpeed={(nativeSpeed > 0f && t.syncWalkSpeedToNative ? nativeSpeed.ToString("F3") + "(synced)" : t.walkSpeed.ToString("F3"))}");
        }

        // ---------------------------------------------------------------- 接地 / 步幅校准辅助

        // 单个 clip 的采样结论（接地最低点双通道 + 步幅估算）。
        private sealed class ClipFootProbe
        {
            public AnimationClip clip;
            public string role;                               // "idle"（对齐基准）/ "move"（穿地约束 + 地速估算）
            public float footVertexLowest = float.MaxValue;   // 脚区蒙皮顶点最低世界 Y（最准通道）
            public float footBoneLowest = float.MaxValue;     // 脚骨原点最低世界 Y（次级通道）
            public float stepLength;                          // 单步行程（米）：脚相对骨盆水平轨迹主轴的 peak-to-peak 中位数
            public float nativeGroundSpeed = -1f;             // 诊断：周期公式 2s/T（假设 clip=单周期+占空比 50%，对多周期 clip 不成立，仅对照）
            public float stanceGroundSpeed = -1f;             // 支撑相测速主估计：各脚贴地低带内相邻帧速度中位数（跨脚再取中位数，米/秒）
            public float stanceDuty = -1f;                    // 诊断：低带帧占比中位数（不参与估算）
            public float stanceSpeedSpread = -1f;             // 诊断：跨脚速度离散度（平均绝对偏差÷中位数；测速可信度）
            public float loopWindowStart = -1f;               // 运行期循环窗口起点（秒）：见 FindBestLoopWindow；-1 = 不启用（整段循环）
            public float loopWindowEnd = -1f;                 // 运行期循环窗口终点（秒）
            public float loopWindowSeamBefore = -1f;          // 诊断：全 clip 循环的接缝姿势差（米，Σ 各脚骨首尾水平差）
            public float loopWindowSeamAfter = -1f;           // 诊断：最优窗口循环的接缝姿势差（米）

            // 实际采用的接触点通道名（写日志用）。
            public string Channel => footVertexLowest != float.MaxValue ? "footVertex" : "footBone";
            // 实际采用的接触最低点：优先脚区顶点，退化到脚骨原点。
            public float ContactLowest => footVertexLowest != float.MaxValue ? footVertexLowest : footBoneLowest;
        }

        // 一个 SkinnedMeshRenderer 上归属「脚区」（脚骨本身及其子骨：趾骨/爪骨）的顶点索引集合。
        private sealed class FootVertexRegion
        {
            public SkinnedMeshRenderer Renderer;
            public int[] Indices;
        }

        // 轨迹采样密度：≥60fps 等效（步态测速需要足够帧分辨率，32 帧会把支撑段/低掠摆动混同）。
        // 顶点最低点通道（BakeMesh 昂贵）按 FootVertexSampleCount 抽采，保持既有接地基准口径；
        // 脚骨最低点逐帧测量（Transform 便宜）。
        private const float FootSampleRate = 60f;
        private const int FootVertexSampleCount = 32;

        // 对单个 clip 整段采样：每帧同时更新「脚区接触最低点」与「脚相对骨盆的水平轨迹」。
        // AnimationMode 的开关由调用方负责（本方法只在已开启的动画模式里采样）。
        private static void ProbeClipFeet(Animation animation, ClipFootProbe probe, List<Transform> feet,
            List<FootVertexRegion> regions, Transform bodyReference, Mesh tempMesh, List<Vector3> vertexBuffer,
            string displayName)
        {
            // clip.length<=0 时只采 time=0 一帧，避免除零/无意义循环。
            // 采样密度：60fps 等效（下限 32 保住旧最低点测量口径，上限 4096 防超长 clip）。
            int steps = probe.clip.length > 0f
                ? Mathf.Clamp(Mathf.CeilToInt(probe.clip.length * FootSampleRate), FootVertexSampleCount, 4096)
                : 0;
            int vertexStep = Mathf.Max(1, steps / FootVertexSampleCount);
            var tracks = new List<Vector2>[feet.Count];
            var heights = new List<float>[feet.Count];   // 与 tracks 平行的脚骨世界 Y 轨迹（支撑相识别用）
            for (int i = 0; i < tracks.Length; i++)
            {
                tracks[i] = new List<Vector2>(steps + 1);
                heights[i] = new List<float>(steps + 1);
            }

            for (int i = 0; i <= steps; i++)
            {
                float time = steps > 0 ? probe.clip.length * (i / (float)steps) : 0f;
                AnimationMode.BeginSampling();
                // 以 animation.gameObject 为绑定根，与运行期 Legacy 播放保持一致（用 model 可能静默得到绑定姿势）。
                AnimationMode.SampleAnimationClip(animation.gameObject, probe.clip, time);
                AnimationMode.EndSampling(); // EndSampling 后姿态才落到 Transform

                float boneLowest = MeasureFootBoneLowest(feet);
                if (boneLowest < probe.footBoneLowest)
                    probe.footBoneLowest = boneLowest;
                // 顶点通道按旧密度（~32 帧）抽采：BakeMesh 昂贵，且保持既有接地基准口径不变。
                if (i % vertexStep == 0)
                {
                    float vertexLowest = MeasureFootVertexLowest(regions, tempMesh, vertexBuffer);
                    if (vertexLowest < probe.footVertexLowest)
                        probe.footVertexLowest = vertexLowest;
                }

                // 步幅轨迹：脚相对「身体参考骨」的水平偏移。在身体参考系里量，
                // 原地循环与带根位移的片段得到的步幅结论一致。
                Vector3 reference = bodyReference.position;
                for (int f = 0; f < feet.Count; f++)
                {
                    if (feet[f] == null)
                        continue;
                    Vector3 offset = feet[f].position - reference;
                    tracks[f].Add(new Vector2(offset.x, offset.z));
                    heights[f].Add(feet[f].position.y);
                }
            }

            probe.stepLength = EstimateStepLength(tracks);
            // 诊断：周期公式 2s/T（假设 clip=单周期+占空比 50%；多周期 clip 上不成立，仅对照）。
            if (probe.clip.length > 0f && probe.stepLength > 0f)
                probe.nativeGroundSpeed = 2f * probe.stepLength / probe.clip.length;

            // 支撑相测速（主估计）：脚在贴地低带内相对骨盆匀速后滑，低带帧对速度中位数 = 原生地速。
            float deltaTime = steps > 0 ? probe.clip.length / steps : 0f;
            if (deltaTime > 0f)
            {
                var lowBands = new List<bool[]>(tracks.Length);
                probe.stanceGroundSpeed = MeasureStanceSpeed(tracks, heights, deltaTime, lowBands,
                    out probe.stanceDuty, out probe.stanceSpeedSpread);
                DumpFootTrackCsv(displayName, probe, tracks, heights, lowBands, deltaTime);
            }

            // 循环窗口搜索（仅 move clip）：多周期 clip 的总长与内部步态周期不整除时，接缝处相位跳变会
            // 造成每循环一次的「脚位瞬移」小弹跳（实测接缝差 0.2~0.5m）；在 clip 内找「首尾姿势差最小」
            // 的窗口（实测可压到 0.01~0.1m），运行期只在该窗口内循环。无显著改善时保持整段循环。
            if (probe.role == "move")
                FindBestLoopWindow(tracks, probe.clip.length, steps, out probe.loopWindowStart, out probe.loopWindowEnd,
                    out probe.loopWindowSeamBefore, out probe.loopWindowSeamAfter);
        }

        // 一次性取证：把每只脚逐帧的相对轨迹与贴地低带标记写 Logs/footprobe_<恐龙>_<clip>.csv，
        // 供离线复核支撑识别/测速（重烘焙出现问题时不依赖猜测）。任何 IO 异常只告警不中断烘焙。
        private static void DumpFootTrackCsv(string displayName, ClipFootProbe probe, List<Vector2>[] tracks,
            List<float>[] heights, List<bool[]> lowBands, float deltaTime)
        {
            try
            {
                string folder = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
                System.IO.Directory.CreateDirectory(folder);
                string file = System.IO.Path.Combine(folder,
                    "footprobe_" + SanitizeFilePart(displayName) + "_" + SanitizeFilePart(probe.clip.name) + ".csv");
                var builder = new System.Text.StringBuilder();
                // 数值一律 InvariantCulture：CSV 必须用点号小数点，避免中文环境逗号小数破坏列结构。
                System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
                builder.AppendLine("foot,frame,time,relX,relZ,y,inStance");
                for (int f = 0; f < tracks.Length; f++)
                {
                    if (tracks[f] == null || heights[f] == null || heights[f].Count != tracks[f].Count)
                        continue;
                    bool[] lowBand = lowBands != null && f < lowBands.Count ? lowBands[f] : null;
                    for (int i = 0; i < tracks[f].Count; i++)
                    {
                        bool inStance = lowBand != null && i < lowBand.Length && lowBand[i];
                        builder.Append(f).Append(',')
                            .Append(i).Append(',')
                            .Append((i * deltaTime).ToString("F4", culture)).Append(',')
                            .Append(tracks[f][i].x.ToString("F5", culture)).Append(',')
                            .Append(tracks[f][i].y.ToString("F5", culture)).Append(',')
                            .Append(heights[f][i].ToString("F5", culture)).Append(',')
                            .Append(inStance ? '1' : '0').Append('\n');
                    }
                }
                System.IO.File.WriteAllText(file, builder.ToString());
                Debug.Log($"[GroundFeet] 脚轨迹取证 CSV 已写出：{file}");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[GroundFeet] 脚轨迹取证 CSV 写出失败：" + exception.Message);
            }
        }

        // 取证文件名清洗：去掉路径非法字符与竖线/空格（如伶盗龙 clip 名含 '|'）。
        private static string SanitizeFilePart(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "unnamed";
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
                builder.Append(System.Array.IndexOf(invalid, c) >= 0 || c == '|' || c == ' ' ? '_' : c);
            return builder.ToString();
        }

        // 脚区顶点最低世界 Y：BakeMesh 取形变后顶点（useScale=false，缩放只由 localToWorldMatrix 施加一次，避免 S²），
        // 只遍历预先筛好的脚区索引。无脚区（网格不可读等）时返回 float.MaxValue，由调用方退化到脚骨通道。
        private static float MeasureFootVertexLowest(List<FootVertexRegion> regions, Mesh tempMesh, List<Vector3> vertexBuffer)
        {
            if (regions == null || regions.Count == 0)
                return float.MaxValue;

            float lowest = float.MaxValue;
            foreach (FootVertexRegion region in regions)
            {
                if (region.Renderer == null)
                    continue;
                region.Renderer.BakeMesh(tempMesh, false);
                vertexBuffer.Clear();
                tempMesh.GetVertices(vertexBuffer); // GetVertices(List) 复用缓存，避免逐帧分配大数组
                Matrix4x4 m = region.Renderer.transform.localToWorldMatrix;
                for (int i = 0; i < region.Indices.Length; i++)
                {
                    int index = region.Indices[i];
                    if (index < 0 || index >= vertexBuffer.Count)
                        continue;
                    float y = m.MultiplyPoint3x4(vertexBuffer[index]).y;
                    if (y < lowest)
                        lowest = y;
                }
            }
            return lowest;
        }

        // 脚骨原点最低世界 Y（次级通道：关节原点普遍高于接触面，会把模型放得偏低，但远优于整网格 AABB）。
        private static float MeasureFootBoneLowest(List<Transform> feet)
        {
            float lowest = float.MaxValue;
            foreach (Transform foot in feet)
            {
                if (foot != null && foot.position.y < lowest)
                    lowest = foot.position.y;
            }
            return lowest;
        }

        // 模型高度（绑定姿势包围盒 Y 尺寸），作为各种容差/钳制的比例基准。
        private static float MeasureModelHeight(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                return 0.001f;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return Mathf.Max(0.001f, bounds.size.y);
        }

        // 绑定姿势下一次性筛出「脚区顶点索引」：蒙皮权重命中脚骨或其子骨（趾骨/爪骨）即归属脚区。
        // 相比「整网格最低点」，这一步把尾尖/下颌/垂下的前肢完全排除在对齐基准之外（正是旧实现把模型抬高的根因）。
        private static List<FootVertexRegion> CollectFootVertexRegions(SkinnedMeshRenderer[] smrs, List<Transform> feet)
        {
            var regions = new List<FootVertexRegion>();
            if (smrs == null || feet == null || feet.Count == 0)
                return regions;

            foreach (SkinnedMeshRenderer smr in smrs)
            {
                if (smr == null || smr.sharedMesh == null)
                    continue;
                Transform[] bones = smr.bones;
                if (bones == null || bones.Length == 0)
                    continue;

                var footRegionBone = new bool[bones.Length];
                bool anyFootBone = false;
                for (int i = 0; i < bones.Length; i++)
                {
                    footRegionBone[i] = IsFootRegionBone(bones[i], feet, smr.transform);
                    if (footRegionBone[i])
                        anyFootBone = true;
                }
                if (!anyFootBone)
                    continue;

                BoneWeight[] weights;
                try
                {
                    weights = smr.sharedMesh.boneWeights;
                }
                catch (Exception exception)
                {
                    // 非可读网格在部分 Unity 版本下会拒绝访问蒙皮权重 → 退回脚骨原点通道，不中断烘焙。
                    Debug.LogWarning($"[GroundFeet] 读取 {smr.name} 蒙皮权重失败（{exception.GetType().Name}），该网格不参与脚区顶点度量。");
                    continue;
                }
                if (weights == null || weights.Length == 0)
                    continue;

                var indices = new List<int>();
                for (int v = 0; v < weights.Length; v++)
                {
                    BoneWeight weight = weights[v];
                    if ((weight.weight0 > 0f && footRegionBone[weight.boneIndex0]) ||
                        (weight.weight1 > 0f && footRegionBone[weight.boneIndex1]) ||
                        (weight.weight2 > 0f && footRegionBone[weight.boneIndex2]) ||
                        (weight.weight3 > 0f && footRegionBone[weight.boneIndex3]))
                        indices.Add(v);
                }
                if (indices.Count > 0)
                    regions.Add(new FootVertexRegion { Renderer = smr, Indices = indices.ToArray() });
            }
            return regions;
        }

        // bone 是否为脚骨本身或脚骨后代（沿父链上溯，最多 12 层，遇到所属 Renderer 根即止）。
        private static bool IsFootRegionBone(Transform bone, List<Transform> feet, Transform stopAt)
        {
            Transform current = bone;
            for (int depth = 0; current != null && depth < 12; depth++)
            {
                if (feet.Contains(current))
                    return true;
                if (current == stopAt)
                    return false;
                current = current.parent;
            }
            return false;
        }

        // 单步行程估算：把每只脚相对骨盆的水平轨迹投影到其协方差主轴（PCA，与模型朝向约定无关），
        // 取 peak-to-peak 幅度；跨脚取中位数抗离群（如摆动相张开的趾骨）。
        // 依据：一个步态周期里脚相对骨盆从「身前 +s/2」走到「身后 -s/2」，故 peak-to-peak = 单步行程 s。
        private static float EstimateStepLength(List<Vector2>[] tracks)
        {
            var ranges = new List<float>();
            foreach (List<Vector2> samples in tracks)
            {
                if (samples == null || samples.Count < 4)
                    continue;

                Vector2 mean = Vector2.zero;
                foreach (Vector2 sample in samples)
                    mean += sample;
                mean /= samples.Count;

                float cxx = 0f, czz = 0f, cxz = 0f;
                foreach (Vector2 sample in samples)
                {
                    float dx = sample.x - mean.x;
                    float dz = sample.y - mean.y;
                    cxx += dx * dx;
                    czz += dz * dz;
                    cxz += dx * dz;
                }
                // 2×2 协方差矩阵主轴：无位移时 atan2(0,0)=0，后续 peak-to-peak 自然近 0，由调用方做有效性判定。
                float theta = 0.5f * Mathf.Atan2(2f * cxz, cxx - czz);
                var axis = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));

                float min = float.MaxValue;
                float max = float.MinValue;
                foreach (Vector2 sample in samples)
                {
                    float projection = Vector2.Dot(sample - mean, axis);
                    if (projection < min)
                        min = projection;
                    if (projection > max)
                        max = projection;
                }
                if (max > min)
                    ranges.Add(max - min);
            }

            if (ranges.Count == 0)
                return 0f;
            ranges.Sort();
            return ranges[ranges.Count / 2];
        }

        // 支撑相测速（主估计，经 CSV 实证的方法）：一个步态周期里脚只在支撑相贴地，
        // 此时脚相对骨盆的水平速度恰等于身体地速 v（跑步机性质）——与占空比无关，也与 clip 内含几个周期无关。
        // 每只脚取自身 Y 轨迹的贴地低带（最低 12% 幅度或 3cm 取大者，覆盖趾/爪骨贴地段），
        // 对「相邻两帧都落带」的帧对记 |Δ相对XZ| ÷ Δt 并取该脚中位数（抗摆动低掠帧污染）；
        // 跨全部脚再取中位数（抗左右不对称/离群脚）。
        // CSV 实证（footprobe_霸王龙_walk_slow_loop.csv）：14 只脚 × 3 档带宽（12/15/20%）全部收敛
        // 在 2.26~2.38 m/s，对带宽不敏感 → 稳健真值。
        // 【纠错记录】曾短暂替换为「整 clip 主轴切弧弦速法」：该法假设 clip 恰含一个步态周期闭环，
        // 而 walk slow loop 实含 ~2 个周期（周期≈2.76s），弧识别失效（duty 0.82、弦速 1.90，错误）；
        // 单周期闭环恒等式（v×duty×T ≡ 弦长）随之失效，一并废弃。
        // lowBands：输出每脚低带帧标记（CSV 取证）。
        // 返回跨脚速度中位数；有效脚数 <2 返回 -1，由调用方保留模板值并告警。
        // 阈值修正（伶盗龙滑步修复）：双腿单位恰有 2 只脚（伶盗龙），两只都有效即可输出实测地速；
        // 旧阈值 <4 使 2 脚单位恒失败（日志 stanceSpeed=n/a）→ 实测同步永不生效。≥4 脚单位（霸王龙 14 脚）不受影响。
        private static float MeasureStanceSpeed(List<Vector2>[] tracks, List<float>[] heights, float deltaTime,
            List<bool[]> lowBands, out float duty, out float spread)
        {
            duty = -1f;
            spread = -1f;
            lowBands.Clear();
            var speeds = new List<float>();
            var duties = new List<float>();
            for (int f = 0; f < tracks.Length; f++)
            {
                List<Vector2> samples = tracks[f];
                List<float> ys = heights[f];
                if (samples == null || ys == null || samples.Count < 8 || ys.Count != samples.Count)
                {
                    lowBands.Add(null);
                    continue;
                }

                float min = float.MaxValue;
                float max = float.MinValue;
                foreach (float y in ys)
                {
                    if (y < min)
                        min = y;
                    if (y > max)
                        max = y;
                }
                float bandY = min + Mathf.Max((max - min) * 0.12f, 0.03f);

                var band = new bool[ys.Count];
                int bandFrames = 0;
                var footSpeeds = new List<float>();
                for (int i = 0; i < ys.Count; i++)
                {
                    if (ys[i] <= bandY)
                    {
                        band[i] = true;
                        bandFrames++;
                    }
                }
                for (int i = 0; i < ys.Count - 1; i++)
                {
                    if (band[i] && band[i + 1])
                        footSpeeds.Add((samples[i + 1] - samples[i]).magnitude / deltaTime);
                }
                lowBands.Add(band);
                duties.Add(bandFrames / (float)ys.Count);
                if (footSpeeds.Count < 4)
                    continue;
                footSpeeds.Sort();
                speeds.Add(footSpeeds[footSpeeds.Count / 2]);
            }

            if (duties.Count > 0)
            {
                duties.Sort();
                duty = duties[duties.Count / 2];
            }
            // 至少 2 只有效脚：双腿单位（伶盗龙，恰好 2 只脚）据此获得实测地速；单脚无跨脚交叉校验，仍判失败。
            // ≥4 脚的单位（霸王龙）14>2 提前满足，路径与旧阈值 <4 完全一致。
            if (speeds.Count < 2)
                return -1f;
            speeds.Sort();
            float median = speeds[speeds.Count / 2];
            // 跨脚离散度（平均绝对偏差 ÷ 中位数）：识别可信度诊断，超阈值时调用方告警。
            float deviation = 0f;
            foreach (float speed in speeds)
                deviation += Mathf.Abs(speed - median);
            spread = deviation / speeds.Count / median;
            return median;
        }

        // 循环窗口搜索：找「首尾姿势差最小」的时间窗 [start, start+length]（帧），使运行期在该窗口内
        // 循环播放时接缝最小。背景：clip 含约 2 个步态周期但总长与周期不整除时（如 5.5s vs 2×2.76s），
        // 全 clip 循环每圈都会「快进/急刹」几十毫秒的相位 → 脚位瞬移（实测 0.2~0.5m）→ 观感小弹跳；
        // 在窗口内循环可把接缝差压到 0.01~0.1m。仅当最优窗口显著优于整段循环（改善 ≥ 40%）时才启用。
        private static void FindBestLoopWindow(List<Vector2>[] tracks, float clipLength, int steps,
            out float windowStart, out float windowEnd, out float seamBefore, out float seamAfter)
        {
            windowStart = -1f;
            windowEnd = -1f;
            seamBefore = -1f;
            seamAfter = -1f;
            if (tracks == null || tracks.Length == 0 || clipLength <= 0f || steps < 16)
                return;

            // 现状接缝 = 整段循环的首尾差（第 steps 帧即 clip.length 时刻的采样）。
            float natural = 0f;
            for (int f = 0; f < tracks.Length; f++)
            {
                List<Vector2> track = tracks[f];
                if (track != null && track.Count > steps)
                    natural += (track[steps] - track[0]).magnitude;
            }

            // 周期帧数范围：clip 步数的 45%~55%（覆盖「含约 2 个步态周期」的走路片段）。
            int minLength = Mathf.Max(8, Mathf.RoundToInt(steps * 0.45f));
            int maxLength = Mathf.Min(steps - 8, Mathf.RoundToInt(steps * 0.55f));
            float bestScore = float.MaxValue;
            int bestStart = -1;
            int bestLength = -1;
            for (int length = minLength; length <= maxLength; length++)
            {
                for (int start = 0; start + length <= steps; start++)
                {
                    float score = 0f;
                    for (int f = 0; f < tracks.Length; f++)
                    {
                        List<Vector2> track = tracks[f];
                        if (track == null || track.Count <= start + length)
                            continue;
                        score += (track[start + length] - track[start]).magnitude;
                    }
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestStart = start;
                        bestLength = length;
                    }
                }
            }

            // 启用条件：找到有效窗口 + 显著优于整段循环 + 窗口不是整段本身。
            if (bestStart < 0 || bestLength <= 0 || bestStart + bestLength >= steps || bestScore >= natural * 0.6f)
                return;
            windowStart = clipLength * bestStart / steps;
            windowEnd = clipLength * (bestStart + bestLength) / steps;
            seamBefore = natural;
            seamAfter = bestScore;
        }

        // 身体参考骨（骨盆/根）：步幅必须在身体参考系里量，否则原地循环与带根位移的片段结论不一致。
        // 候选：名字含 pelvis/hips/hip/cog/root 的骨骼，取最靠近体轴（包围盒中心 XZ）的一根，
        // 避免选到腿根（如 jt_Hip_L）；全未命中时退回动画根 Transform（原地动画下同样成立）。
        private static Transform ResolveBodyReference(Animation animation, Bounds modelBounds)
        {
            var bones = new List<Transform>();
            foreach (SkinnedMeshRenderer smr in animation.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Transform bone in smr.bones)
                {
                    if (bone != null && !bones.Contains(bone))
                        bones.Add(bone);
                }
            }

            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (Transform bone in bones)
            {
                string lower = bone.name.ToLowerInvariant();
                if (!lower.Contains("pelvis") && !lower.Contains("hips") && !lower.Contains("hip") &&
                    !lower.Contains("cog") && !lower.Contains("root"))
                    continue;
                float distance = new Vector2(bone.position.x - modelBounds.center.x, bone.position.z - modelBounds.center.z).magnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = bone;
                }
            }
            return best != null ? best : animation.transform;
        }

        // 把 move clip 的估算原生地速写入 agent.moveAnimationGroundSpeed（序列化字段）：
        // 运行期 PlayMovementClip 用 playbackSpeed = groundSpeed / moveAnimationGroundSpeed 播放，
        // 于是「动画脚速」自动等于「根节点位移速度」→ 不再滑步。
        // 选值：支撑相测速（贴地低带速度中位数；CSV 实证 14 脚 × 3 档带宽收敛于 2.26~2.38，稳健）。
        // 【纠错记录】周期公式 2s/T（1.417）假设「clip=单周期+占空比 50%」，而 walk slow loop 实含
        // ~2 个周期（周期≈2.76s），该值偏低 38%；曾短暂采用的「整 clip 主轴切弧弦速法」同样因多周期
        // 而失效（duty 0.82、弦速 1.90）。现仅以低带速度中位数为选值（对周期数与占空比均不敏感），
        // 失败时保留模板值并告警。
        // syncWalkSpeedToNative 开启时把实测地速同步写入 walkSpeed / approachSpeed（播放 1.0× 原生、零滑移）。
        private static float ApplyNativeGroundSpeed(DinosaurAgent agent, DinosaurTemplate t, ClipFootProbe moveProbe, float modelHeight)
        {
            // 选值：支撑相低带速度中位数（duty-free、周期数无关）；周期公式与弦速法均已证伪，不再参与选值。
            float native = moveProbe.stanceGroundSpeed;
            bool unusable = agent == null || native <= 0.05f || native > 25f || moveProbe.stepLength < modelHeight * 0.02f;
            if (unusable)
            {
                Debug.LogWarning($"[GroundFeet] {t.displayName}: 原生地速估算失败（clip='{moveProbe.clip.name}' " +
                    $"stepLength={moveProbe.stepLength:F3} nativeSpeed={native:F3}），保留模板值 moveAnimationGroundSpeed={t.walkSpeed:F2}" +
                    "（脚速与根位移不匹配仍会滑步，需人工核定该常量，慢步循环参考 2~3 m/s）。");
                return -1f;
            }

            SetField(agent, "moveAnimationGroundSpeed", native);
            // 播放 1.0× 原生（可选）：walkSpeed 同步为实测地速，运行期 playbackSpeed ≡ 1.0，
            // 滑移率 = walkSpeed × (1 − 真值/估速) 严格为零；模板 walkSpeed 仅在估算失败时兜底。
            if (t.syncWalkSpeedToNative)
            {
                SetField(agent, "walkSpeed", native);
                SetField(agent, "approachSpeed", Mathf.Max(native * 1.35f, native + 0.5f));
            }
            EditorUtility.SetDirty(agent);
            float effectiveWalkSpeed = t.syncWalkSpeedToNative ? native : t.walkSpeed;
            float playback = effectiveWalkSpeed / native;
            if (playback > 4f)
                Debug.LogWarning($"[GroundFeet] {t.displayName}: playbackSpeed={playback:F2}× 偏高（walkSpeed={effectiveWalkSpeed:F2} ÷ 原生地速={native:F2}），" +
                    "步频会明显加快；若观感过快，下调模板 walkSpeed 即可（步幅已自动同步，walkSpeed 不再需要与体长同比例）。");
            return native;
        }

        // 把「最优循环窗口」写入 agent（见 FindBestLoopWindow）：运行期在该窗口内循环播放 move clip，
        // 消除 clip 接缝的相位跳（多周期 clip 与总长不整除时每循环一次的「脚位瞬移」小弹跳）。
        // 无有效窗口时显式写入 -1（保持整段循环），避免残留上次烘焙的旧值。
        private static void ApplyLoopWindow(DinosaurAgent agent, DinosaurTemplate t, ClipFootProbe moveProbe)
        {
            if (agent == null || moveProbe == null)
                return;

            bool valid = moveProbe.loopWindowEnd > moveProbe.loopWindowStart &&
                moveProbe.loopWindowEnd < moveProbe.clip.length - 1e-4f;
            SetField(agent, "moveClipLoopStart", valid ? moveProbe.loopWindowStart : -1f);
            SetField(agent, "moveClipLoopEnd", valid ? moveProbe.loopWindowEnd : -1f);
            EditorUtility.SetDirty(agent);
            if (valid)
                Debug.Log($"[GroundFeet] {t.displayName}: move clip 循环窗口 = [{moveProbe.loopWindowStart:F3}s, {moveProbe.loopWindowEnd:F3}s]" +
                    $"（clip 全长 {moveProbe.clip.length:F3}s；接缝姿势差 {moveProbe.loopWindowSeamBefore:F3}m → {moveProbe.loopWindowSeamAfter:F3}m）" +
                    "，运行期将只在该窗口内循环。");
        }

        // 兜底通道：脚骨完全找不到时退回「整网格最低点」对齐（旧行为）。
        // 该通道的最低点常是尾尖/下颌而非脚，会把整只模型抬高 → 仅作最后手段并显式告警。
        private static void GroundByWholeMeshLowest(GameObject visualRoot, Animation animation, List<ClipFootProbe> probes,
            Renderer[] renderers, float groundY, float modelHeight, DinosaurTemplate t)
        {
            SkinnedMeshRenderer[] smrs = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Mesh tempMesh = new Mesh();
            try
            {
                // 绑定姿势自校准：bounds 与 bake 两通道基准差距过大时，bake 兜底不可信。
                float bindBoundsMinY = MeasureLowestWorldYByBounds(renderers);
                float bindBakeMinY = MeasureLowestWorldYByBake(smrs, tempMesh);
                bool bakeChannelValid = Mathf.Abs(bindBoundsMinY - bindBakeMinY) < 0.05f * modelHeight;

                float lowestBoundsY = float.MaxValue;
                float lowestBakeY = float.MaxValue;
                bool ownsAnimationMode = !AnimationMode.InAnimationMode();
                if (ownsAnimationMode)
                    AnimationMode.StartAnimationMode();
                try
                {
                    foreach (ClipFootProbe probe in probes)
                    {
                        int steps = probe.clip.length > 0f ? FootVertexSampleCount : 0;
                        for (int i = 0; i <= steps; i++)
                        {
                            float time = steps > 0 ? probe.clip.length * (i / (float)steps) : 0f;
                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(animation.gameObject, probe.clip, time);
                            AnimationMode.EndSampling();
                            lowestBoundsY = Mathf.Min(lowestBoundsY, MeasureLowestWorldYByBounds(renderers));
                            if (bakeChannelValid)
                                lowestBakeY = Mathf.Min(lowestBakeY, MeasureLowestWorldYByBake(smrs, tempMesh));
                        }
                    }
                }
                finally
                {
                    if (ownsAnimationMode && AnimationMode.InAnimationMode())
                        AnimationMode.StopAnimationMode();
                }

                // Renderer.bounds 在 AnimationMode 采样后常不刷新（stale）：与绑定姿势基准逐位相等时改用 bake 通道。
                bool boundsStale = lowestBoundsY != float.MaxValue && Mathf.Abs(lowestBoundsY - bindBoundsMinY) < 1e-4f;
                float lowestY = boundsStale ? lowestBakeY : lowestBoundsY;
                string channel = boundsStale ? "bake" : "bounds";
                if (lowestY == float.MaxValue)
                {
                    Debug.LogError("[GroundFeet] " + t.displayName + " 兜底通道 bounds/bake 均无有效最低点，保留 Fit 结果不做位移。");
                    return;
                }

                float dy = groundY - lowestY;
                float limit = Mathf.Max(modelHeight * 0.2f, 0.3f);
                if (Mathf.Abs(dy) > limit)
                {
                    Debug.LogWarning($"[GroundFeet] {t.displayName}: 兜底通道 dy={dy:F3} 超出安全上限 {limit:F3}，保留 Fit 结果不位移。");
                    return;
                }
                visualRoot.transform.position += Vector3.up * dy;
                Debug.Log($"[GroundFeet] {t.displayName}: clips=[{DescribeClips(probes)}] footBones=0 channel=meshBounds({channel}) " +
                    $"bindBoundsMinY={bindBoundsMinY:F3} bindBakeMinY={bindBakeMinY:F3} animLowestY={lowestY:F3} dy={dy:F3}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempMesh);
            }
        }

        // 日志辅助：无测量值（MaxValue/NaN）统一打 n/a，避免把哨兵值当成坐标读。
        private static string FormatMeasured(float value)
        {
            return float.IsNaN(value) || value >= float.MaxValue || value <= float.MinValue ? "n/a" : value.ToString("F3");
        }

        private static string DescribeClips(List<ClipFootProbe> probes)
        {
            var names = new List<string>(probes.Count);
            foreach (ClipFootProbe probe in probes)
                names.Add(probe.clip.name);
            return string.Join(",", names.ToArray());
        }

        private static string DescribeNames(string[] names)
        {
            return names == null || names.Length == 0 ? "未配置" : string.Join(",", names);
        }

        // 镜像 DinosaurAgent.FindClip 的匹配规则，但返回 AnimationClip：OrdinalIgnoreCase 匹配 hint，
        // 排除名字含 _RM 的根运动片段，优先返回非 _RM 匹配，其次 _RM 匹配；无匹配返回 null（不兜底 first）。
        private static AnimationClip ResolveGroundClip(Animation animation, string[] hints)
        {
            AnimationClip rootMotionMatch = null;
            foreach (AnimationState state in animation)
            {
                AnimationClip clip = state.clip;
                if (clip == null)
                    continue;
                foreach (string hint in hints)
                {
                    if (!string.IsNullOrEmpty(hint) && state.name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool isRootMotion = state.name.IndexOf("_RM", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isRootMotion)
                            return clip;
                        if (rootMotionMatch == null)
                            rootMotionMatch = clip;
                    }
                }
            }
            return rootMotionMatch;
        }

        // 兜底：取枚举到的第一个非空片段。
        private static AnimationClip ResolveFirstClip(Animation animation)
        {
            foreach (AnimationState state in animation)
                if (state.clip != null)
                    return state.clip;
            return null;
        }

        // bounds 通道：与 DinosaurModelFitter.Fit 完全相同的封装方式，返回合并包围盒最低世界 Y。
        // 接收调用方缓存的 Renderer[]，避免每帧 GetComponentsInChildren 分配。
        private static float MeasureLowestWorldYByBounds(Renderer[] rs)
        {
            if (rs == null || rs.Length == 0)
                return float.MaxValue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
                b.Encapsulate(rs[i].bounds);
            return b.min.y;
        }

        // bake 通道：对每个 SkinnedMeshRenderer 烘焙形变后网格，取包围盒 8 个角点经 localToWorldMatrix
        // 变换到世界，跨所有 smr 返回最低世界 Y。复用调用方缓存的 smr[] 与 tempMesh，避免每帧分配。
        private static float MeasureLowestWorldYByBake(SkinnedMeshRenderer[] smrs, Mesh tempMesh)
        {
            if (smrs == null || smrs.Length == 0)
                return float.MaxValue;

            float lowest = float.MaxValue;
            foreach (SkinnedMeshRenderer smr in smrs)
            {
                if (smr == null || smr.sharedMesh == null)
                    continue;
                // useScale=false：缩放仅由下方 localToWorldMatrix 施加一次，避免 S² 双重缩放。
                smr.BakeMesh(tempMesh, false);
                Bounds mb = tempMesh.bounds;
                Vector3 c = mb.center;
                Vector3 e = mb.extents;
                Matrix4x4 m = smr.transform.localToWorldMatrix;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = c + Vector3.Scale(e, new Vector3(sx, sy, sz));
                    Vector3 world = m.MultiplyPoint3x4(corner);
                    if (world.y < lowest)
                        lowest = world.y;
                }
            }
            return lowest;
        }

        private static GameObject InstantiateModel(DinosaurTemplate t, Transform parent)
        {
            GameObject prefab = string.IsNullOrEmpty(t.assetPath)
                ? Resources.Load<GameObject>(t.resourcePath)
                : AssetDatabase.LoadAssetAtPath<GameObject>(t.assetPath);
            if (prefab == null)
            {
                Debug.LogError("[SceneBuilder] 未找到恐龙模型资源：" + (string.IsNullOrEmpty(t.assetPath) ? "Resources/" + t.resourcePath : t.assetPath) +
                    "（若为并行导入任务尚未完成，请先导入素材再烘焙）。");
                return null;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            model.name = t.displayName + " Source Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, t.yaw, 0f);
            model.transform.localScale = Vector3.one;
            return model;
        }

        internal static DinosaurAgent AddStegosaurusToCurrentScene()
        {
            Camera camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Main camera required to add Stegosaurus.");
            DinosaurTemplate template = CreateTemplates().Single(t => t.objectName == "Stegosaurus");
            MovementCorridor.GetSampleAt(2, MovementCorridor.Length(2) * .35f, out Vector2 point, out Vector2 tangent);
            template.position = new Vector3(point.x, 0f, point.y);
            return BuildDinosaur(template, camera, LoadBuiltinFont());
        }

        private static void ApplyBakedMaterials(GameObject model, DinosaurTemplate t)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Texture2D primaryAlbedo = LoadTexture(t.primaryAlbedo);
            Texture2D secondaryAlbedo = LoadTexture(t.secondaryAlbedo);
            Texture2D primaryNormal = LoadTexture(t.primaryNormal);
            Texture2D secondaryNormal = LoadTexture(t.secondaryNormal);
            Texture2D occlusion = LoadTexture(t.occlusion);

            Material primaryMaterial = EnsureDinosaurMaterial(t.displayName + " Primary",
                primaryAlbedo, primaryNormal, occlusion);
            Material secondaryMaterial = secondaryAlbedo != null
                ? EnsureDinosaurMaterial(t.displayName + " Secondary",
                    secondaryAlbedo, secondaryNormal != null ? secondaryNormal : primaryNormal, occlusion)
                : null;

            foreach (Renderer renderer in renderers)
            {
                Material[] sourceMaterials = renderer.sharedMaterials;
                var materials = new Material[sourceMaterials.Length];
                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    Material source = sourceMaterials[i];
                    bool useSecondary = secondaryMaterial != null &&
                        ((source != null && source.name.IndexOf("other", System.StringComparison.OrdinalIgnoreCase) >= 0) || i > 0);
                    materials[i] = useSecondary ? secondaryMaterial : primaryMaterial;
                }
                renderer.sharedMaterials = materials;
                renderer.enabled = true;
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                    skinnedRenderer.updateWhenOffscreen = true;
                EditorUtility.SetDirty(renderer);
            }
        }

        private static Material EnsureDinosaurMaterial(string assetName, Texture albedo, Texture normal, Texture occlusion)
        {
            EnsureFolder(DinosaurMaterialFolder);
            Material material = LoadOrCreateStandardMaterial(DinosaurMaterialFolder + "/" + assetName + ".mat");
            material.color = Color.white;
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.28f);

            material.SetTexture("_MainTex", albedo);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
            }

            if (occlusion != null)
            {
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetFloat("_OcclusionStrength", 0.65f);
            }
            else
            {
                material.SetTexture("_OcclusionMap", null);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateStandardMaterial(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }

        private static Texture2D LoadTexture(string resourcePath)
        {
            return string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<Texture2D>(resourcePath);
        }

        // ---------------------------------------------------------------- 信息卡（屏幕空间 UI 面板）

        private static Sprite EnsureRoundedCardSprite()
        {
            const string path = "Assets/UI/RoundedCardBg.png";
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            EnsureFolder("Assets/UI");
            const int size = 64;
            const int radius = 14;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color white = Color.white;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                white.a = alpha;
                tex.SetPixel(x, y, white);
            }
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100;
            importer.spriteBorder = new Vector4(16, 16, 16, 16);
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void BuildInfoPanel(Transform hudParent, Font font)
        {
            const float CardWidth = 520f;
            const float CardHeight = 360f;
            const float MarginLeft = 36f;
            const float MarginTop = 36f;
            const int TitleFontSize = 32;
            const int BodyFontSize = 24;
            const float PaddingH = 28f;
            const float PaddingTop = 24f;
            const float PaddingBottom = 20f;
            const float TitleHeight = 48f;
            const float TitleBodyGap = 8f;

            Sprite roundedSprite = EnsureRoundedCardSprite();

            var cardRoot = new GameObject("InfoCard", typeof(RectTransform), typeof(CanvasGroup));
            cardRoot.transform.SetParent(hudParent, false);
            RectTransform cardRect = cardRoot.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0f, 1f);
            cardRect.anchorMax = new Vector2(0f, 1f);
            cardRect.pivot = new Vector2(0f, 1f);
            cardRect.anchoredPosition = new Vector2(MarginLeft, -MarginTop);
            cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);

            CanvasGroup cg = cardRoot.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(cardRoot.transform, false);
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = bg.GetComponent<Image>();
            bgImage.sprite = roundedSprite;
            bgImage.type = Image.Type.Sliced;
            bgImage.color = new Color(0.05f, 0.05f, 0.07f, 0.78f);

            var titleObj = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleObj.transform.SetParent(bg.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(PaddingH, -PaddingTop);
            titleRect.sizeDelta = new Vector2(-PaddingH * 2f, TitleHeight);
            Text title = titleObj.GetComponent<Text>();
            title.font = font;
            title.fontSize = TitleFontSize;
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(0.97f, 0.87f, 0.55f);
            title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.text = "";

            var bodyObj = new GameObject("Body", typeof(RectTransform), typeof(Text));
            bodyObj.transform.SetParent(bg.transform, false);
            RectTransform bodyRect = bodyObj.GetComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(PaddingH, PaddingBottom);
            bodyRect.offsetMax = new Vector2(-PaddingH, -(PaddingTop + TitleHeight + TitleBodyGap));
            Text body = bodyObj.GetComponent<Text>();
            body.font = font;
            body.fontSize = BodyFontSize;
            body.color = new Color(0.92f, 0.93f, 0.90f);
            body.alignment = TextAnchor.UpperLeft;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            body.lineSpacing = 1.2f;
            body.text = "";

            DinosaurInfoCard card = cardRoot.AddComponent<DinosaurInfoCard>();
            SetField(card, "canvasGroup", cg);
            SetField(card, "titleText", title);
            SetField(card, "bodyText", body);
            EditorUtility.SetDirty(card);
        }

        // ---------------------------------------------------------------- 反射写字段（字段名契约）

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Debug.LogError("[SceneBuilder] 字段契约不一致：" + target.GetType().Name + " 缺少字段 '" + fieldName + "'。");
                return;
            }
            field.SetValue(target, value);
        }

        // internal：PolyHavenAssetIntegration 复用同一实现，避免各自复制一份递归建目录逻辑。
        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ---------------------------------------------------------------- 恐龙参数模板（严格对照 Bootstrap 原值）

        private sealed class DinosaurTemplate
        {
            public string objectName;
            public string displayName;
            public string resourcePath;
            public string assetPath;
            public bool preserveSourceMaterials;
            public Vector3 position;
            public float targetLength;
            public float yaw;
            public float focusHeight;
            public float walkSpeed;
            public string description;
            public string idleHint = "idle";
            public string moveHint = "run";
            public bool alignBottom = true;
            public bool enableFootLock = true;
            public string primaryAlbedo;
            public string secondaryAlbedo;
            public string primaryNormal;
            public string secondaryNormal;
            public string occlusion;
            public string[] footBoneNames;
            public int footLockJointDepth = 3;
            public bool releaseFootLockOnRise;
            public float footContactHeight = -1f;
            public float footRiseThreshold = -1f;
            public float footLockDistanceLimit = -1f;
            // 是否在烘焙期用 move clip 估算「动画原生地速」并覆盖 agent.moveAnimationGroundSpeed（步幅/速度同步，治滑步）。
            // 默认 false：该覆盖会改变动画播放倍速（playbackSpeed = walkSpeed ÷ 原生地速），
            // 只对已确认存在滑步的单位逐个开启，避免波及观感已正常的其它恐龙。
            public bool syncMoveAnimationSpeed;
            // 是否把实测原生地速同步写入 agent.walkSpeed / agent.approachSpeed（移动速度=原生地速，播放 1.0×）：
            // 开启后步态观感即动画原生步频、行走零滑移（运行期 playbackSpeed = walkSpeed ÷ 原生地速 ≡ 1.0）；
            // 模板 walkSpeed 仅在估算失败时兜底。仅在 syncMoveAnimationSpeed 同开时生效。
            public bool syncWalkSpeedToNative;
            public bool enabled = true;   // false = 烘焙期直接跳过，不生成任何场景对象（翻回 true + 重烘焙即可复原）
            // 行为级碰撞半径（米）：写入 DinosaurAgent.collisionRadius 私有字段。
            // collisionRadius 只影响恐龙互斥（IsPeerBlocked 圆域查询）；树干避让由 DinosaurAgent 侧
            // Min(collisionRadius, DinoTreeAvoidRadiusCap=2.5f) 钳制，视觉树冠不参与碰撞，仅树干级近似。
            public float collisionRadius = 1.5f;
            // 是否感知地面障碍物（树干级行为碰撞）：写入 DinosaurAgent.groundObstacleAware 私有字段。
            // 飞行单位应设 false —— 它们在高空航线飞行，不该被地面树干阻挡或触发绕行。
            // 【跨代理字段契约】DinosaurAgent.cs 侧为 [SerializeField] private bool groundObstacleAware = true;
            // 字段名必须逐字符一致，否则 SetField 只打「字段契约不一致」错误日志并跳过写入（不崩，但静默失效）。
            public bool groundObstacleAware = true;
        }

        private static List<DinosaurTemplate> CreateTemplates()
        {
            return new List<DinosaurTemplate>
            {
                // 霸王龙（原为 GLB，现改 FBX；体长按任务表迁移为 ~12，idle/move hint 沿用 idle/run）。
                new DinosaurTemplate
                {
                    objectName = "Tyrannosaurus Rex",
                    displayName = "霸王龙",
                    resourcePath = "Dinosaurs/TRex/source/T-Rex",
                    position = new Vector3(0f, 0f, 11f),
                    targetLength = 15f,
                    yaw = 0f,
                    focusHeight = 3.5f,
                    walkSpeed = 1.5f,   // 仅作估算失败兜底：syncWalkSpeedToNative=true 时烘焙会把实测原生地速（≈2.3 m/s）写入 walkSpeed /
                                        // approachSpeed，运行期播放 =1.0× 原生步态、零滑移（playbackSpeed = walkSpeed ÷ 实测地速 ≡ 1.0）。
                                        // 此后想调步频只改本值 + 重烘焙即可（测速准确前提下始终零滑移，仅观感变化）。
                    description = "霸王龙  Tyrannosaurus Rex\n\n生存年代：晚白垩世\n食性：肉食性\n体型：约 15 米\n\n强壮的顶级掠食者，拥有惊人的咬合力。",
                    idleHint = "idle",
                    moveHint = "run",
                    alignBottom = true,
                    enableFootLock = true,
                    primaryAlbedo = "Dinosaurs/TRex/textures/trexDiff",
                    primaryNormal = "Dinosaurs/TRex/textures/trexNormal",
                    // 脚骨显式配置（T-Rex.fbx 骨骼名，腿部层级 Hip→Thigh→Knee→Ankle→Foot→Toe*→Claw*）：
                    // 只写 jt_Foot_L/R 不够——跖/踝关节原点离地还有数十厘米，运行期 FootLock 的
                    // clearance = 脚骨Y - 地面Y 会永远大于 releaseHeight(≈4% 体高) → 永不落脚 → 滑步。
                    // 把趾骨/爪骨一并列入，让「最低脚骨」真正落在接触面附近（烘焙期接地度量也复用这批骨骼）。
                    footBoneNames = new[]
                    {
                        "jt_Foot_L", "jt_Foot_R",
                        "jt_ToeInner_L", "jt_ToeMiddle_L", "jt_ToeOutter_L",
                        "jt_ToeInner_R", "jt_ToeMiddle_R", "jt_ToeOutter_R",
                        "jt_ClawInner_L", "jt_ClawMiddle_L", "jt_ClawOutter_L",
                        "jt_ClawInner_R", "jt_ClawMiddle_R", "jt_ClawOutter_R",
                    },
                    // 霸王龙 move clip "walk slow loop" 实含 ~2 个步态周期（周期≈2.76s），原生地速实测 ≈2.30 m/s
                    // （CSV 实证：14 只脚贴地低带速度中位数 × 3 档带宽全部收敛于 2.26~2.38）。
                    // 旧周期公式 2s/T=1.417 与曾短暂采用的主轴切弧弦速法=1.90 均已证伪（多周期假设失效）。
                    // 现由烘焙期「贴地低带速度中位数法」估原生地速并写回 moveAnimationGroundSpeed（失败保留模板值 + 告警）。
                    syncMoveAnimationSpeed = true,
                    // 播放 1.0× 原生（用户取向：速度慢一点没关系，零滑步最高优先）：walkSpeed 同步为实测原生地速（≈2.3 m/s），
                    // 滑移率 = walkSpeed × (1 − 真值/估速)，测速准确时严格为零；此后调 walkSpeed 仍零滑移，仅改变步态观感。
                    syncWalkSpeedToNative = true,
                    // 【脚锁唤醒·治残余滑步】行走支撑相最低脚骨离地实测 ≈0.235m（footBoneLowest 0.577 + dy -0.342），
                    // 高于脚锁默认唤醒阈值 releaseHeight≈0.231m（体高 5.763 × 4%）→ 运行期 plant 条件永不满足，
                    // 脚锁整个行走期间休眠、残余滑移无人兜底。footContactHeight=0.27 留 +3.5cm 余量让脚锁在支撑相接管；
                    // releaseFootLockOnRise + 1mm 阈值保证起脚瞬间释放（否则会在 0.27m 高度硬释放产生竖向上跳）。
                    // footLockDistanceLimit 保持 -1：有限距离触发是「锚点→动画位」瞬间回弹（回弹量≈限值），
                    // 与 3.9m 步幅及转向扫掠不匹配；起脚释放已覆盖正常节奏。
                    releaseFootLockOnRise = true,
                    footContactHeight = 0.27f,
                    footRiseThreshold = 0.001f,
                    collisionRadius = 5f,   // 体长 15m 的顶级掠食者：行为级碰撞半径取 5m
                },

                // 胜王龙
                new DinosaurTemplate
                {
                    enabled = false,   // 用户要求暂时禁用（不显示）
                    objectName = "Rajasaurus",
                    displayName = "胜王龙",
                    resourcePath = "Dinosaurs/Rajasaurus/source/Rajasaurus",
                    position = new Vector3(-10f, 0f, 19f),
                    targetLength = 9f,
                    yaw = 180f,
                    focusHeight = 2.4f,
                    walkSpeed = 1.6f,
                    description = "胜王龙  Rajasaurus\n\n生存年代：晚白垩世\n食性：肉食性\n分布：印度\n\n头骨短而厚，是印度次大陆特有的阿贝力龙类。",
                    idleHint = "idle",
                    moveHint = "walk",
                    primaryAlbedo = "Dinosaurs/Rajasaurus/textures/AlbedoMap",
                    occlusion = "Dinosaurs/Rajasaurus/textures/AmbientOcclusionMap",
                    footBoneNames = new[] { "Bone.L.007", "Bone.R.007" },
                    footLockJointDepth = 3,
                    collisionRadius = 3f,   // 体长 9m 中型兽脚类
                },

                // 伶盗龙
                new DinosaurTemplate
                {
                    objectName = "Velociraptor",
                    displayName = "伶盗龙",
                    resourcePath = "Dinosaurs/Velociraptor/Raptor/Raptor_Animated_FBX",
                    position = new Vector3(8f, 0f, 15f),
                    targetLength = 2.7f,
                    yaw = 180f,
                    focusHeight = 1.3f,
                    // 目标行进速度（用户选定「折中 ≈3.0 m/s」档）：烘焙期实测原生地速（≈1.6 m/s）写入
                    // moveAnimationGroundSpeed 后，运行期 playbackSpeed = 3.0 ÷ ≈1.6 ≈ 1.8×——步频约 1.8 倍、
                    // 脚速与根位移严格匹配（零滑移）。日后想回原生步态只改本值 ≈1.6 + 重烘焙即可。
                    // 【纠错】旧值 6.2f（= 5.5 × 2.7/2.4 按体长比例外推）从未与动画匹配：未开启实测同步时
                    // playback 恒 1.0×，身体 6.2 m/s / 脚摆速实测 ≈1.63 m/s → 滑移率 ≈74%（「大幅滑步」根因）。
                    walkSpeed = 3.0f,
                    description = "伶盗龙  Velociraptor\n\n生存年代：晚白垩世\n食性：肉食性\n体型：约 2.7 米\n\n敏捷的小型猎手，后足具有醒目的镰刀状趾爪。",
                    idleHint = "idle",
                    moveHint = "run",
                    primaryAlbedo = "Dinosaurs/Velociraptor/Raptor/Textures/Raptor_PatternA_Albedo",
                    secondaryAlbedo = "Dinosaurs/Velociraptor/Raptor/Textures/Other_Albedo2 1K",
                    primaryNormal = "Dinosaurs/Velociraptor/Raptor/Textures/Raptor_Normal",
                    secondaryNormal = "Dinosaurs/Velociraptor/Raptor/Textures/Other_Normal 1K",
                    occlusion = "Dinosaurs/Velociraptor/Raptor/Textures/Raptor_AO",
                    // 烘焙期实测 move clip（Raptor_Run1_Anim，'run' hint 匹配）原生地速并写入 agent.moveAnimationGroundSpeed，
                    // 同时搜索「最优循环窗口」（修多周期 clip 接缝相位跳的小弹跳，同霸王龙链路）。
                    // 实测证据（CSV 复算）：左脚 ≈1.249 / 右脚 ≈1.634 m/s（每脚 22 个贴地帧对，离散度 ≈12%＜25% 阈值）。
                    // 【历史障碍】MeasureStanceSpeed 原「有效脚数 <4 即失败」的阈值使 2 脚单位恒返回 -1
                    // （烘焙日志 stanceSpeed=n/a）——阈值已放宽为 <2，本开关才能得到结果。
                    syncMoveAnimationSpeed = true,
                    // 刻意【不】与实测地速同步（与霸王龙的 syncWalkSpeedToNative=true 不同）：
                    // 保留 walkSpeed=3.0 的行进速度，playback ≈1.8×；脚速仍严格匹配位移、零滑移。
                    syncWalkSpeedToNative = false,
                    // 【脚锁唤醒】行走支撑相最低脚骨离地实测 ≈0.087m（footBoneLowest 0.176 + dy -0.089），
                    // 高于脚锁默认唤醒阈值 0.056（体高 1.397 × 4%）→ 行走期间脚锁全程休眠、残余滑移无人兜底
                    // （烘焙日志已告警并建议 ≈0.12）。footContactHeight=0.12 留 +3.3cm 余量；
                    // releaseFootLockOnRise + 1mm 阈值保证起脚瞬间释放（防在 0.12m 高度硬释放产生竖向上跳）。
                    releaseFootLockOnRise = true,
                    footContactHeight = 0.12f,
                    footRiseThreshold = 0.001f,
                    collisionRadius = 1.5f,   // 体长 2.7m 小型猎手：与 DinosaurTemplate 默认值一致
                },

                new DinosaurTemplate
                {
                    objectName = "Stegosaurus",
                    enabled = false,   // 用户要求暂时禁用，保留配置以便恢复
                    displayName = "剑龙",
                    assetPath = StegosaurusSceneIntegration.PrefabPath,
                    preserveSourceMaterials = true,
                    position = new Vector3(18f, 0f, 24f),
                    targetLength = 9f,
                    yaw = 0f,
                    focusHeight = 2.5f,
                    walkSpeed = 1.15f,
                    description = "剑龙  Stegosaurus\n\n植食性恐龙，拥有背部骨板和带刺的尾巴。\n场景体长：约 9 米。",
                    idleHint = "Idle",
                    moveHint = "Walk",
                    footBoneNames = new[] { "Foot.L", "Foot.R", "BackFoot.L", "BackFoot.R", "Toes.L", "Toes.R", "BackToes.L", "BackToes.R" },
                    footLockJointDepth = 2,
                    releaseFootLockOnRise = true,
                    footRiseThreshold = .001f,
                    syncMoveAnimationSpeed = true,
                    syncWalkSpeedToNative = true,
                    collisionRadius = 3f,
                },

                // 阿根廷龙
                new DinosaurTemplate
                {
                    enabled = false,   // 用户要求暂时禁用（不显示）
                    objectName = "Argentinosaurus",
                    displayName = "阿根廷龙",
                    resourcePath = "Dinosaurs/Argentinosaurus/source/Argentinosaurus",
                    position = new Vector3(-13f, 0f, 35f),
                    targetLength = 27f,
                    yaw = 180f,
                    focusHeight = 5.5f,
                    walkSpeed = 0.7f,
                    description = "阿根廷龙  Argentinosaurus\n\n生存年代：晚白垩世\n食性：植食性\n体型：约 30–35 米\n\n已知最大的陆生动物之一，拥有极长的颈部和尾部。",
                    idleHint = "idle",
                    moveHint = "walk",
                    primaryAlbedo = "Dinosaurs/Argentinosaurus/textures/AlbedoMap",
                    occlusion = "Dinosaurs/Argentinosaurus/textures/AmbientOcclutionMap",
                    footBoneNames = new[] { "Bone.l.002", "Bone.r.002", "Bone.l.006", "Bone.r.006" },
                    footLockJointDepth = 2,
                    releaseFootLockOnRise = true,
                    enableFootLock = true,
                    footContactHeight = 0.08f,
                    footRiseThreshold = 0.0005f,
                    footLockDistanceLimit = 0.75f,
                    // 体长 27m 巨型蜥脚类：行为级碰撞半径取 8m。
                    // 【核心矩形余量提示，仅注释不改数值】出生点 (-13,35) + 漫游半径 45m 的包络西界 x=-58、
                    // 北界 z=80，距 ForestSeeder 的 CoreMinX=-60 / CoreMaxZ=82 仅约 2m 余量；把 enabled 翻回 true
                    // 之前需评估外扩 CoreMinX/CoreMaxZ 或调小本 collisionRadius（详见 ForestSeeder 常量区注释）。
                    collisionRadius = 8f,
                },
            };
        }
    }
}
