using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// 生产验收只读采集器 + 幂等比对器（HQ Environment Acceptance Pass）。
    ///
    /// 【只读契约（结构性，非注释承诺）】本类绝不调用：
    ///   AssetDatabase.SaveAssets / Refresh / ImportAsset、EditorSceneManager.SaveScene / MarkSceneDirty、
    ///   EditorUtility.SetDirty、PrefabUtility.SaveAsPrefabAsset、new GameObject、DestroyImmediate、
    ///   Object.Instantiate(TerrainData)。
    /// 唯一的磁盘写入目标是 &lt;project&gt;/Logs/acceptance/**（JSON / metrics / 日志）。
    /// 场景读取只用 EditorSceneManager.OpenScene(path, Single) + scene.GetRootGameObjects()（不跨场景、不跳过 inactive；
    /// 刻意不用 GameObject.Find —— 它会跨所有已加载场景命中，且忽略 inactive）。
    /// 开场前若活动场景 isDirty 则直接中止，绝不丢弃未保存改动。
    ///
    /// 【为什么需要它】项目里没有任何文件哈希 / 逐层 Σdensity / 生产场景实例计数的现成工具：
    /// PolyHavenPassPreview 只报告【克隆体 + 临时场景】上的数字（非破坏性契约），ValidateProject 是
    /// assert-and-throw 且不写文件。本类补齐生产 TerrainData / Main.unity 的真实度量与双 Bake 幂等判定。
    ///
    /// 【执行顺序】GUI 菜单 Jurassic World/Acceptance/1..6（编号即顺序）；同一批方法也可用
    ///   -executeMethod JurassicWorld.Editor.ProductionAcceptance.&lt;Method&gt; 调用。
    /// </summary>
    public static class ProductionAcceptance
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string TerrainDataPath = "Assets/Terrain/JurassicTerrainData.asset";
        private const string OutputRelativeFolder = "Logs/acceptance";

        /// <summary>生产常量期望值（门控用）。</summary>
        private const int ExpectedPreviewScale = 1;

        // ================================================================ 菜单入口

        [MenuItem("Jurassic World/Acceptance/1. Dump Production Constants", false, 1)]
        public static void MenuDumpConstants() => DumpProductionConstants();

        [MenuItem("Jurassic World/Acceptance/2. Capture Pre-Bake Baseline", false, 2)]
        public static void MenuCapturePrebake() => Capture("prebake");

        [MenuItem("Jurassic World/Acceptance/3. Capture Post-Bake #1", false, 3)]
        public static void MenuCapturePostbake1() => Capture("postbake1");

        [MenuItem("Jurassic World/Acceptance/4. Verify Bake #1 vs Pre-Bake", false, 4)]
        public static void MenuVerifyBake1() => VerifyBake1();

        [MenuItem("Jurassic World/Acceptance/5. Capture Post-Bake #2", false, 5)]
        public static void MenuCapturePostbake2() => Capture("postbake2");

        [MenuItem("Jurassic World/Acceptance/6. Compare #1 vs #2 (Idempotency)", false, 6)]
        public static void MenuCompareIdempotency() => CompareIdempotency();

        // ================================================================ 批处理入口（-executeMethod）

        public static void DumpProductionConstants() => RunGuarded(() =>
        {
            LogContract();
            ReportConstants();
            FileHash terrain = HashAsset(TerrainDataPath);
            FileHash scene = HashAsset(MainScenePath);
            Debug.Log($"[Acceptance] {TerrainDataPath}: sha256={terrain.sha256} size={terrain.sizeBytes} mtime={terrain.lastWriteUtc}");
            Debug.Log($"[Acceptance] {MainScenePath}: sha256={scene.sha256} size={scene.sizeBytes} mtime={scene.lastWriteUtc}");
            Debug.Log($"JURASSIC_ACCEPTANCE_CONSTANTS_OK: 常量与文件哈希已输出（只读，未修改任何资产）。");
        });

        public static void CapturePrebake() => Capture("prebake");
        public static void CapturePostbake1() => Capture("postbake1");
        public static void CapturePostbake2() => Capture("postbake2");
        public static void VerifyBake1() => VerifyBake1Core();
        public static void Compare() => CompareIdempotency();

        /// <summary>
        /// 采集当前生产状态到 Logs/acceptance/&lt;label&gt;.json + .metrics（只读）。
        /// </summary>
        public static void Capture(string label) => RunGuarded(() =>
        {
            LogContract();
            ReportConstants();

            string jsonPath = "", metricsPath = "";
            AcceptanceReport report = null;
            try
            {
                if (!TryOpenMainSceneReadOnly(out Scene scene, out string abortReason))
                {
                    Debug.LogError($"JURASSIC_ACCEPTANCE_FAIL: {abortReason}");
                    return;
                }

                report = Collect(scene, label);
                WriteReport(report, label, out jsonPath, out metricsPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"JURASSIC_ACCEPTANCE_FAIL: 采集 {label} 抛出异常：{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                return;
            }

            foreach (AcceptanceWarning warning in report.warnings)
                Debug.LogWarning($"[Acceptance] WARN {warning.code}: {warning.detail}");

            foreach (string line in BuildMetrics(report).Split('\n'))
                if (line.StartsWith("schema=", StringComparison.Ordinal))
                    continue;
                else
                    Debug.Log($"[Acceptance] {line}");

            Debug.Log($"JURASSIC_ACCEPTANCE_CAPTURED: {jsonPath} (label={label}, metrics={metricsPath})");
        });

        // ================================================================ 常量与契约

        private static void LogContract()
        {
            Debug.Log("[Acceptance] 只读契约：不调用 SaveAssets/SaveScene/MarkSceneDirty/SetDirty/ImportAsset/Refresh；" +
                      "仅写 Logs/acceptance/**；活动场景 isDirty 或处于 Play Mode 时直接中止。");
        }

        private static void ReportConstants()
        {
            ProductionConstants c = CollectConstants();
            Debug.Log($"[Acceptance] PreviewScale={c.previewScale} / DebugProceduralRocksOn={c.debugProceduralRocksOn} / " +
                      $"PassEnabled={c.passEnabled} / DinoSafetyBuffer={c.dinoSafetyBuffer} (" +
                      $"期望 PreviewScale={ExpectedPreviewScale}、DebugProceduralRocksOn=False、PassEnabled=True)");
        }

        private static ProductionConstants CollectConstants()
        {
            // 展示环布局（近林带可见性）：数量是确定性布局事实，入库便于跨 Bake 比对与门槛断言
            int showcaseTotal = 0, showcaseRocks = 0, showcaseLogs = 0, showcaseStumps = 0;
            if (UnderstorySeeder.PassEnabled)
            {
                foreach (UnderstorySeeder.ShowcaseSpot spot in UnderstorySeeder.BuildShowcaseRing())
                {
                    showcaseTotal++;
                    if (spot.Kind == UnderstorySeeder.ShowcaseKind.Rock) showcaseRocks++;
                    else if (spot.Kind == UnderstorySeeder.ShowcaseKind.Log) showcaseLogs++;
                    else showcaseStumps++;
                }
            }

            return new ProductionConstants
            {
                previewScale = UnderstorySeeder.PreviewScale,
                debugProceduralRocksOn = UnderstorySeeder.DebugProceduralRocksOn,
                passEnabled = UnderstorySeeder.PassEnabled,
                dinoSafetyBuffer = F(UnderstorySeeder.DinoSafetyBuffer),
                fernFolder = UnderstoryAssetLocator.FernFolder,
                rockFolder = UnderstoryAssetLocator.RockFolder,
                deadwoodFolder = UnderstoryAssetLocator.DeadwoodFolder,
                playerBounds = PlayerBox.Describe(),
                playerBoundsMin = PlayerBox.MinText,
                playerBoundsMax = PlayerBox.MaxText,
                worldBounds = WorldBounds.Describe(),
                showcaseSpotCount = showcaseTotal,
                showcaseRockCount = showcaseRocks,
                showcaseLogCount = showcaseLogs,
                showcaseStumpCount = showcaseStumps,
                showcaseClearRadius = F(UnderstorySeeder.ShowcaseClearRadius),
                showcaseLaneHalfWidth = F(UnderstorySeeder.ShowcaseLaneHalfWidth),
            };
        }

        /// <summary>
        /// 玩家可达矩形（唯一来源 = WorldBounds.PlayerExplorationBounds；不再反射读 SmoothExplorerCamera 的
        /// private const —— 那个写法只能表达正方盒，也无法表达 x[-78,78] z[-78,88] 的不对称范围）。
        /// </summary>
        private static readonly Rect PlayerExplorationRect = WorldBounds.PlayerExplorationXZ;

        private static class PlayerBox
        {
            internal static string Describe() =>
                $"x[{PlayerExplorationRect.xMin:F0},{PlayerExplorationRect.xMax:F0}] z[{PlayerExplorationRect.yMin:F0},{PlayerExplorationRect.yMax:F0}]";

            /// <summary>矩形左下角「x,z」（可被 AcceptanceShotCapture 解析）。</summary>
            internal static string MinText => $"{F(PlayerExplorationRect.xMin)},{F(PlayerExplorationRect.yMin)}";

            /// <summary>矩形右上角「x,z」。</summary>
            internal static string MaxText => $"{F(PlayerExplorationRect.xMax)},{F(PlayerExplorationRect.yMax)}";
        }

        // ================================================================ 场景安全门

        private static bool TryOpenMainSceneReadOnly(out Scene scene, out string abortReason)
        {
            scene = EditorSceneManager.GetActiveScene();
            abortReason = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                abortReason = "当前处于 Play Mode（或正在切换）：请先退出 Play Mode 再运行验收采集。";
                return false;
            }

            if (scene.IsValid() && scene.isDirty)
            {
                abortReason = $"活动场景 \"{scene.name}\" 有未保存改动：拒绝打开 Main.unity 以免丢弃你的工作。请先手动保存（或放弃）后重试。";
                return false;
            }

            if (!File.Exists(FullPath(MainScenePath)))
            {
                abortReason = $"缺少已烘焙的主场景 {MainScenePath}：请先执行 Jurassic World/Bake Scene (Edit Mode)。";
                return false;
            }

            if (!scene.IsValid() || !string.Equals(scene.path, MainScenePath, StringComparison.Ordinal))
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            return true;
        }

        // ================================================================ 采集

        private static AcceptanceReport Collect(Scene scene, string label)
        {
            var report = new AcceptanceReport
            {
                schema = "jw.acceptance/1",
                label = label,
                capturedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                projectPath = Directory.GetCurrentDirectory(),
                gitHead = TryGitHead(),
                constants = CollectConstants(),
                terrainData = HashAsset(TerrainDataPath),
                mainScene = HashAsset(MainScenePath),
            };

            var warnings = new List<AcceptanceWarning>();

            Terrain terrain = FindInScene<Terrain>(scene);
            if (terrain == null || terrain.terrainData == null)
            {
                warnings.Add(Warn("terrain-missing", "场景中找不到 Terrain 或其 terrainData 为 null；terrain/fern 相关字段为空。"));
                report.terrain = new TerrainSummary();
                report.fern = new FernReport { layers = Array.Empty<FernLayer>(), layersDigest = "" };
            }
            else
            {
                report.terrain = CollectTerrain(scene, terrain);
                report.fern = CollectFern(terrain.terrainData, terrain, warnings);
            }

            if (report.constants.previewScale != ExpectedPreviewScale)
                warnings.Add(Warn("preview-scale",
                    $"UnderstorySeeder.PreviewScale={report.constants.previewScale} ≠ 1：当前产物可能来自 Preview Bake（实例数被缩小）。"));
            if (report.constants.debugProceduralRocksOn)
                warnings.Add(Warn("debug-rocks", "UnderstorySeeder.DebugProceduralRocks=true：正式 Bake 禁止。"));
            if (!report.constants.passEnabled)
                warnings.Add(Warn("pass-disabled", "UnderstorySeeder 已禁用（回滚态）：Understory Pass 不应存在于生产场景。"));

            Transform understoryRoot = FindRoot(scene, UnderstorySeeder.RootNodeName);
            Transform propsRoot = FindRoot(scene, "Environment Props");

            report.understoryRocks = CollectEntityGroup("Understory Pass/Rocks",
                understoryRoot != null ? understoryRoot.Find("Rocks") : null, terrain, warnings);
            report.vegetation = CollectEntityGroup("Understory Pass/Vegetation",
                understoryRoot != null ? understoryRoot.Find("Vegetation") : null, terrain, warnings);
            report.deadwood = CollectEntityGroup("Understory Pass/Deadwood",
                understoryRoot != null ? understoryRoot.Find("Deadwood") : null, terrain, warnings);
            // GroundPass 岩石散布已退役（TerrainEnvironmentSeeder.cs:133/373：root 不再创建、无需清理，
            // 由 JurassicWorldSceneBuilder.ClearActiveScene 在每次 Bake 开头整根销毁）：
            // 这里刻意不产生 missing 告警，而是把「预烘焙 24 个 → 烘焙后 root 消失」作为退役证据记录下来。
            report.groundPassRocks = CollectEntityGroup("Ground Pass Rocks",
                FindRoot(scene, "Ground Pass Rocks"), terrain, warnings, warnWhenMissing: false);
            report.environmentPropsChildCount = propsRoot != null ? propsRoot.childCount : -1;
            report.centralRocks = CollectCentralRocks(propsRoot, terrain, warnings);
            report.gatePrefabs = CollectGatePrefabs(warnings);
            report.assets = CollectAssetInventory(warnings);
            report.missingMaterialCount = CountMissingMaterials(understoryRoot, propsRoot, warnings);
            report.fernMaterial = CollectFernMaterial(warnings);
            report.viewPlan = BuildViewPlan(report, scene);
            report.warnings = warnings.ToArray();
            return report;
        }

        private static TerrainSummary CollectTerrain(Scene scene, Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            var summary = new TerrainSummary
            {
                treeInstanceCount = data.treeInstanceCount,
                treePrototypeCount = data.treePrototypes != null ? data.treePrototypes.Length : 0,
                centralTreePrototypeCount = ForestSeeder.CentralTreePrototypeCount,
                detailPrototypesLength = data.detailPrototypes != null ? data.detailPrototypes.Length : 0,
                detailResolution = data.detailResolution,
                alphamapResolution = data.alphamapResolution,
                heightmapResolution = data.heightmapResolution,
                terrainLayersLength = data.terrainLayers != null ? data.terrainLayers.Length : 0,
                sizeX = F(data.size.x),
                sizeY = F(data.size.y),
                sizeZ = F(data.size.z),
                terrainPosition = Vec3(terrain.transform.position),
                drawInstanced = terrain.drawInstanced,
                treeDistance = F(terrain.treeDistance),
                treeBillboardDistance = F(terrain.treeBillboardDistance),
                treeMaximumFullLODCount = terrain.treeMaximumFullLODCount,
                treeCrossFadeLength = F(terrain.treeCrossFadeLength),
                detailObjectDistance = F(terrain.detailObjectDistance),
                detailObjectDensity = F(terrain.detailObjectDensity),
                wavingGrassStrength = F(data.wavingGrassStrength),
                wavingGrassAmount = F(data.wavingGrassAmount),
                wavingGrassSpeed = F(data.wavingGrassSpeed),
                basemapDistance = F(terrain.basemapDistance),
                heightmapPixelError = F(terrain.heightmapPixelError),
                coreRect = $"x∈[{ForestSeeder.CoreMinX},{ForestSeeder.CoreMaxX}] z∈[{ForestSeeder.CoreMinZ},{ForestSeeder.CoreMaxZ}]",
                channelAzimuths = string.Join("/", ForestSeeder.ChannelAzimuths.Select(F)),
                channelOrigin = Vec2(ForestSeeder.ChannelOrigin),
                channelHalfWidth = F(ForestSeeder.ChannelHalfWidth),
                channelSegmentLength = F(ForestSeeder.ChannelSegmentLength),
            };

            // 核心矩形零泄漏（与 ValidateProject 同一算法：单次 foreach，不做 LINQ 物化）
            int coreLeak = 0;
            Vector3 size = data.size;
            TreeInstance[] instances = data.treeInstances;
            foreach (TreeInstance ti in instances)
            {
                if (ti.prototypeIndex < ForestSeeder.CentralTreePrototypeCount) continue;
                Vector3 world = terrain.transform.TransformPoint(new Vector3(
                    ti.position.x * size.x, ti.position.y * size.y, ti.position.z * size.z));
                if (ForestSeeder.IsInsideCoreRect(new Vector2(world.x, world.z)))
                    coreLeak++;
            }
            summary.forestLeakCount = coreLeak;

            // 逐 detail 层的种类与 Σdensity（含 bush/grass，用于证明非 fern 层未被改动）
            var layers = new List<DetailLayerSummary>();
            int res = data.detailResolution;
            for (int i = 0; i < summary.detailPrototypesLength; i++)
            {
                DetailPrototype proto = data.detailPrototypes[i];
                var layer = new DetailLayerSummary
                {
                    prototypeIndex = i,
                    kind = ClassifyPrototype(proto),
                    sumDensity = SumDetailLayer(data, res, i),
                };
                layers.Add(layer);
            }
            summary.detailLayers = layers.ToArray();
            summary.detailLayersDigest = Digest(string.Join("|", layers.Select(
                l => $"{l.prototypeIndex}:{l.kind}:{l.sumDensity}")));
            return summary;
        }

        private static FernReport CollectFern(TerrainData data, Terrain terrain, List<AcceptanceWarning> warnings)
        {
            var report = new FernReport
            {
                detailPrototypesLength = data.detailPrototypes != null ? data.detailPrototypes.Length : 0,
                detailResolution = data.detailResolution,
                detailObjectDensity = F(terrain.detailObjectDensity),
                detailObjectDistance = F(terrain.detailObjectDistance),
                seederLastFernTotalTarget = UnderstorySeeder.LastFernTotalTarget,
                seederLastFernAssigned = UnderstorySeeder.LastFernAssigned,
                seederLastFernVariantCount = UnderstorySeeder.LastFernVariantCount,
            };

            var layers = new List<FernLayer>();
            int res = data.detailResolution;
            for (int i = 0; i < report.detailPrototypesLength; i++)
            {
                DetailPrototype proto = data.detailPrototypes[i];
                if (!IsFernPrototype(proto)) continue;

                var layer = new FernLayer
                {
                    prototypeIndex = i,
                    prefabName = proto.prototype != null ? proto.prototype.name : "<null>",
                    prefabAssetPath = proto.prototype != null ? AssetDatabase.GetAssetPath(proto.prototype) : "",
                    usePrototypeMesh = proto.usePrototypeMesh,
                    renderMode = proto.renderMode.ToString(),
                    minWidth = F(proto.minWidth),
                    maxWidth = F(proto.maxWidth),
                    minHeight = F(proto.minHeight),
                    maxHeight = F(proto.maxHeight),
                    noiseSpread = F(proto.noiseSpread),
                    healthyColor = Color3(proto.healthyColor),
                    dryColor = Color3(proto.dryColor),
                    sumDensity = SumDetailLayer(data, res, i),
                };
                layer.approxInstances = Mathf.RoundToInt(layer.sumDensity * terrain.detailObjectDensity);
                MeshFilter filter = proto.prototype != null ? proto.prototype.GetComponentInChildren<MeshFilter>() : null;
                layer.meshTriangles = filter != null && filter.sharedMesh != null ? filter.sharedMesh.triangles.Length / 3 : 0;
                layer.meshName = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "";
                Renderer renderer = proto.prototype != null ? proto.prototype.GetComponentInChildren<Renderer>() : null;
                layer.materialName = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "";
                layer.materialShader = renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                    ? renderer.sharedMaterial.shader.name
                    : "";
                layers.Add(layer);
            }

            report.layers = layers.ToArray();
            report.layerCount = layers.Count;
            report.totalSumDensity = layers.Sum(l => (long)l.sumDensity);
            report.layersDigest = Digest(string.Join("|", layers.Select(
                l => $"{l.prototypeIndex}:{l.prefabName}:{l.sumDensity}")));
            report.distinctLayerSumCount = layers.Select(l => l.sumDensity).Distinct().Count();
            report.conservationOk = report.seederLastFernTotalTarget > 0
                ? report.totalSumDensity == report.seederLastFernTotalTarget
                : true;

            if (report.layerCount == 0 && UnderstoryAssetLocator.FindFernPrefabs().Length > 0)
                warnings.Add(Warn("fern-layers-zero",
                    "Assets/Vegetation/Ferns 有合格 prefab 但 TerrainData 中 fern 层数为 0：Bake 未生效或 fern 门控被跳过。"));
            if (report.layerCount > 1 && report.distinctLayerSumCount != report.layerCount)
                warnings.Add(Warn("fern-layer-sums-equal",
                    "存在 Σdensity 完全相同的 fern 层：疑似回退成「每层写一份完整目标密度」。"));
            if (report.seederLastFernTotalTarget > 0 && !report.conservationOk)
                warnings.Add(Warn("fern-conservation",
                    $"fern 密度不守恒：Σ层={report.totalSumDensity} ≠ 播种器目标 {report.seederLastFernTotalTarget}。"));
            if (report.seederLastFernTotalTarget == 0 && report.layerCount > 0)
                Debug.Log("[Acceptance] 提示：UnderstorySeeder.LastFern* 为 0（多为脚本重编译导致域重载）；" +
                          "本报告以 TerrainData 实测 Σdensity 为准，不受影响。");
            return report;
        }

        private static EntityGroupReport CollectEntityGroup(string label, Transform root, Terrain terrain,
            List<AcceptanceWarning> warnings, bool warnWhenMissing = true)
        {
            var report = new EntityGroupReport { rootPath = label };
            if (root == null)
            {
                report.childCount = -1;
                report.countsDigest = Digest("missing");
                report.positionsDigest = Digest("missing");
                report.byPrefab = Array.Empty<EntityCount>();
                report.instances = Array.Empty<EntityInstance>();
                if (warnWhenMissing)
                    warnings?.Add(Warn($"group-missing:{label}", $"{label} 不存在（门控目录为空或 Pass 未启用时属预期）。"));
                return report;
            }

            int childCount = root.childCount;
            var byPrefab = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var instances = new List<EntityInstance>(childCount);

            for (int i = 0; i < childCount; i++)
            {
                Transform child = root.GetChild(i);
                GameObject go = child.gameObject;
                string prefabPath = PrefabAssetPathOf(go);
                string prefabName = !string.IsNullOrEmpty(prefabPath)
                    ? Path.GetFileNameWithoutExtension(prefabPath)
                    : $"<unprefabbed:{MeshSignatureOf(go)}>";
                if (!byPrefab.ContainsKey(prefabName)) byPrefab[prefabName] = 0;
                byPrefab[prefabName]++;

                Renderer renderer = PrimaryRenderer(go);
                LODGroup lod = go.GetComponent<LODGroup>();
                if (lod == null) lod = go.GetComponentInChildren<LODGroup>(true);
                float groundY = terrain != null ? GroundY(terrain, go.transform.position) : 0f;
                float sink = renderer != null ? groundY - renderer.bounds.min.y : 0f;

                instances.Add(new EntityInstance
                {
                    name = go.name,
                    group = prefabName,
                    prefabAssetPath = prefabPath ?? "",
                    position = Vec3(go.transform.position),
                    groundY = F(groundY),
                    sinkMeters = F(sink),
                    boundsSize = renderer != null ? Vec3(renderer.bounds.size) : "",
                    rotationEuler = Vec3(go.transform.eulerAngles),
                    scale = Vec3(go.transform.lossyScale),
                    triangles = renderer != null ? TriangleCount(renderer) : 0,
                    materialName = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "",
                    materialShader = renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                        ? renderer.sharedMaterial.shader.name
                        : "",
                    lodSummary = DescribeLodGroup(lod),
                    distanceToTravelBox = F(DistanceToTravelBoxXZ(go.transform.position.x, go.transform.position.z)),
                });
            }

            report.childCount = childCount;
            report.distinctPrefabCount = byPrefab.Count;
            report.byPrefab = byPrefab.Select(kv => new EntityCount { prefabName = kv.Key, count = kv.Value }).ToArray();
            report.instances = instances
                .OrderBy(inst => inst.name, StringComparer.Ordinal)
                .ToArray();
            report.countsDigest = Digest(string.Join("|", byPrefab.Select(kv => $"{kv.Key}={kv.Value}")));
            report.positionsDigest = Digest(string.Join("|", instances
                .OrderBy(inst => inst.name, StringComparer.Ordinal)
                .Select(inst => $"{inst.name}:{inst.position}")));

            EntityInstance nearest = instances
                .OrderBy(inst => ParseF(inst.distanceToTravelBox))
                .FirstOrDefault();
            if (nearest != null)
            {
                report.nearestToTravelBoxName = nearest.name;
                report.nearestToTravelBoxDistance = nearest.distanceToTravelBox;
            }
            return report;
        }

        private static CentralRockReport CollectCentralRocks(Transform propsRoot, Terrain terrain,
            List<AcceptanceWarning> warnings)
        {
            var report = new CentralRockReport { entries = Array.Empty<CentralRockEntry>() };
            if (propsRoot == null)
            {
                warnings.Add(Warn("props-missing", "\"Environment Props\" 不存在：中央 12 岩石与构图植物均缺失。"));
                report.xzDigest = Digest("missing");
                return report;
            }

            var entries = new List<CentralRockEntry>();
            for (int i = 0; i < propsRoot.childCount; i++)
            {
                Transform child = propsRoot.GetChild(i);
                if (!child.name.StartsWith("Rock_Large_", StringComparison.Ordinal))
                    continue;

                GameObject go = child.gameObject;
                string prefabPath = PrefabAssetPathOf(go);
                Renderer renderer = PrimaryRenderer(go);
                MeshFilter filter = go.GetComponentInChildren<MeshFilter>(true);
                string meshName = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "";

                var entry = new CentralRockEntry
                {
                    name = go.name,
                    sourcePrefabPath = prefabPath ?? "",
                    sourcePrefabName = !string.IsNullOrEmpty(prefabPath) ? Path.GetFileNameWithoutExtension(prefabPath) : "",
                    meshName = meshName,
                    isPrimitiveMesh = string.Equals(meshName, "Sphere", StringComparison.OrdinalIgnoreCase),
                    materialName = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "",
                    materialShader = renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                        ? renderer.sharedMaterial.shader.name
                        : "",
                    position = Vec3(go.transform.position),
                    rotationEuler = Vec3(go.transform.eulerAngles),
                    scale = Vec3(go.transform.lossyScale),
                    boundsSize = renderer != null ? Vec3(renderer.bounds.size) : "",
                    groundY = F(terrain != null ? GroundY(terrain, go.transform.position) : 0f),
                    sinkMeters = F(renderer != null && terrain != null ? GroundY(terrain, go.transform.position) - renderer.bounds.min.y : 0f),
                };

                JurassicWorldSceneBuilder.CentralRockFootprint footprint = JurassicWorldSceneBuilder.LastCentralRockFootprints
                    .FirstOrDefault(f => string.Equals(f.Name, go.name, StringComparison.Ordinal));
                if (!string.IsNullOrEmpty(footprint.Name))
                {
                    entry.targetFootprint = F(footprint.TargetFootprint);
                    entry.actualFootprint = F(footprint.ActualFootprint);
                    if (footprint.TargetFootprint > 0.001f)
                    {
                        float ratio = footprint.ActualFootprint / footprint.TargetFootprint;
                        entry.occupancyRatio = F(ratio);
                        if (ratio < 0.75f || ratio > 1.25f)
                            warnings.Add(Warn($"rock-footprint:{go.name}",
                                $"{go.name} footprint 比值 {ratio:F3} 超出 ±25%（target={footprint.TargetFootprint:F2}, actual={footprint.ActualFootprint:F2}）。"));
                    }
                }
                else
                {
                    warnings.Add(Warn($"rock-footprint-missing:{go.name}",
                        $"{go.name} 无 footprint 记录（LastCentralRockFootprints 为空，通常因脚本重编译导致域重载）：occupancyRatio 留空，请以目视 + boundsSize 判断。"));
                }
                entries.Add(entry);
            }

            report.entries = entries.OrderBy(e => e.name, StringComparer.Ordinal).ToArray();
            report.count = entries.Count;
            report.primitiveMeshCount = entries.Count(e => e.isPrimitiveMesh);
            report.occupancyRecordedCount = entries.Count(e => !string.IsNullOrEmpty(e.occupancyRatio));
            report.outOfBandFootprintCount = entries.Count(e =>
                !string.IsNullOrEmpty(e.occupancyRatio) &&
                (ParseF(e.occupancyRatio) < 0.75f || ParseF(e.occupancyRatio) > 1.25f));
            report.distinctSourcePrefabCount = entries
                .Where(e => !string.IsNullOrEmpty(e.sourcePrefabName))
                .Select(e => e.sourcePrefabName)
                .Distinct(StringComparer.Ordinal)
                .Count();
            // 布局指纹只取 name/x/z/rotY：换资产合法地改变 mesh/scale/材质，但 XZ 与朝向必须完全一致。
            report.xzDigest = Digest(string.Join("|", entries
                .OrderBy(e => e.name, StringComparer.Ordinal)
                .Select(e => $"{e.name}:{XZOf(e.position)}:{RotYOf(e.rotationEuler)}")));
            return report;
        }

        private static GatePrefabReport[] CollectGatePrefabs(List<AcceptanceWarning> warnings)
        {
            var result = new List<GatePrefabReport>();
            foreach (string folder in new[]
                     {
                         UnderstoryAssetLocator.FernFolder,
                         UnderstoryAssetLocator.RockFolder,
                         UnderstoryAssetLocator.DeadwoodFolder
                     })
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    warnings.Add(Warn($"gate-folder-missing:{folder}", $"{folder} 不存在：对应子系统 no-op。"));
                    continue;
                }

                foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;

                    LODGroup lod = prefab.GetComponent<LODGroup>();
                    if (lod == null) lod = prefab.GetComponentInChildren<LODGroup>(true);
                    Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                    var shaders = new SortedSet<string>(StringComparer.Ordinal);
                    int triangles = 0;
                    int missingMaterials = 0;
                    foreach (Renderer renderer in renderers)
                    {
                        if (renderer.sharedMaterial == null)
                        {
                            missingMaterials++;
                            continue;
                        }
                        shaders.Add(renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : "<null-shader>");
                        triangles += TriangleCount(renderer);
                    }

                    result.Add(new GatePrefabReport
                    {
                        assetPath = path,
                        folder = folder,
                        rendererCount = renderers.Length,
                        triangles = triangles,
                        materialShaders = string.Join("+", shaders),
                        missingMaterialCount = missingMaterials,
                        lodSummary = DescribeLodGroup(lod),
                        boundsSize = renderers.Length > 0 ? Vec3(UnionBounds(renderers).size) : "",
                    });

                    if (triangles == 0)
                        warnings.Add(Warn($"gate-prefab-no-mesh:{path}", $"{path} 三角面数为 0：mesh 未绑定或 prefab 结构异常。"));
                    if (missingMaterials > 0)
                        warnings.Add(Warn($"gate-prefab-missing-material:{path}", $"{path} 有 {missingMaterials} 个 Renderer 材质为 null。"));
                }
            }
            return result.OrderBy(p => p.assetPath, StringComparer.Ordinal).ToArray();
        }

        private static AssetInventory CollectAssetInventory(List<AcceptanceWarning> warnings)
        {
            string[] roots =
            {
                "Assets/Vegetation",
                "Assets/Materials/PolyHaven",
                "Assets/Textures/PolyHaven",
                "Assets/Meshes/PolyHaven",
            };

            var files = new List<AssetFile>();
            foreach (string root in roots)
                CollectFilesUnder(root, files);

            files = files.OrderBy(f => f.path, StringComparer.Ordinal).ToList();

            var duplicates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (AssetFile file in files)
            {
                if (file.path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (!duplicates.TryGetValue(file.sha256, out List<string> group))
                {
                    group = new List<string>();
                    duplicates[file.sha256] = group;
                }
                group.Add(file.path);
            }

            var duplicateGroups = duplicates
                .Where(kv => kv.Value.Count > 1)
                .Select(kv => string.Join(" + ", kv.Value))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            if (duplicateGroups.Length > 0)
                warnings.Add(Warn("duplicate-assets",
                    $"检测到 {duplicateGroups.Length} 组内容完全相同的资产（疑似重复创建）：{string.Join("；", duplicateGroups.Take(4))}"));

            var inventory = new AssetInventory
            {
                roots = roots,
                fileCount = files.Count,
                files = files.ToArray(),
                fernPrefabCount = CountPrefabs(UnderstoryAssetLocator.FernFolder),
                rockPrefabCount = CountPrefabs(UnderstoryAssetLocator.RockFolder),
                deadwoodPrefabCount = CountPrefabs(UnderstoryAssetLocator.DeadwoodFolder),
                duplicateContentGroups = duplicateGroups,
            };
            inventory.inventoryDigest = Digest(string.Join("|", files.Select(f => $"{f.path}={f.sha256}")));
            return inventory;
        }

        private static int CountMissingMaterials(Transform understoryRoot, Transform propsRoot,
            List<AcceptanceWarning> warnings)
        {
            int missing = 0;
            foreach (Transform root in new[] { understoryRoot, propsRoot })
            {
                if (root == null) continue;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.sharedMaterial == null)
                    {
                        missing++;
                        warnings.Add(Warn($"missing-material:{renderer.name}", $"{HierarchyPath(renderer.transform)} 的 sharedMaterial 为 null。"));
                        continue;
                    }
                    Shader shader = renderer.sharedMaterial.shader;
                    if (shader == null || !shader.isSupported || shader.name == "Hidden/InternalErrorShader")
                    {
                        missing++;
                        warnings.Add(Warn($"bad-shader:{renderer.name}",
                            $"{HierarchyPath(renderer.transform)} 的 shader={(shader == null ? "null" : shader.name)}（isSupported={shader != null && shader.isSupported}）。"));
                    }
                }
            }
            return missing;
        }

        private static FernMaterialProbe CollectFernMaterial(List<AcceptanceWarning> warnings)
        {
            var probe = new FernMaterialProbe();
            const string materialPath = "Assets/Materials/PolyHaven/Fern02_Cutout.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                probe.unavailableReason = $"{materialPath} 不存在";
                warnings.Add(Warn("fern-material-missing", $"{materialPath} 不存在：fern Cutout 材质缺失。"));
                return probe;
            }

            probe.available = true;
            probe.materialPath = materialPath;
            probe.shader = material.shader != null ? material.shader.name : "";
            probe.shaderSupported = material.shader != null && material.shader.isSupported;
            probe.cutoff = material.HasProperty("_Cutoff") ? F(material.GetFloat("_Cutoff")) : "";
            probe.cull = material.HasProperty("_Cull") ? material.GetFloat("_Cull").ToString(CultureInfo.InvariantCulture) : "";
            probe.doubleSided = probe.cull == "0";
            if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
            {
                probe.albedoTexturePath = AssetDatabase.GetAssetPath(material.GetTexture("_MainTex"));
                var importer = AssetImporter.GetAtPath(probe.albedoTexturePath) as TextureImporter;
                if (importer != null)
                {
                    probe.albedoMaxTextureSize = importer.maxTextureSize;
                    probe.albedoAlphaIsTransparency = importer.alphaIsTransparency;
                    probe.albedoSrgb = importer.sRGBTexture;
                }
            }
            if (!probe.shaderSupported)
                warnings.Add(Warn("fern-shader-unsupported", $"fern Cutout shader \"{probe.shader}\" 不受支持（渲染管线不兼容？）。"));
            return probe;
        }

        /// <summary>
        /// 观察点计划：把「最近的 fern / rock / deadwood / 中央岩石」换算成玩家可达盒内的站位与朝向，
        /// 供 Play Mode 目视与性能采样直接使用（不依赖 UnderstorySeeder 的私有簇数据结构，全部从场景实测推导）。
        /// </summary>
        private static ViewPlan BuildViewPlan(AcceptanceReport report, Scene scene)
        {
            var plan = new ViewPlan
            {
                travelBox = $"x[{PlayerExplorationRect.xMin:F0},{PlayerExplorationRect.xMax:F0}] " +
                            $"z[{PlayerExplorationRect.yMin:F0},{PlayerExplorationRect.yMax:F0}]" +
                            $"（WorldBounds.PlayerExplorationBounds；西/东/南 = 恐龙活动包络外扩 {WorldBounds.PlayerOutwardMargin:F0}m）",
                targets = Array.Empty<ViewTarget>(),
            };

            var targets = new List<ViewTarget>();

            // 最密 fern 格：4 层中取单格最大密度
            Terrain terrain = FindInScene<Terrain>(scene);
            if (terrain != null && terrain.terrainData != null && report.fern.layerCount > 0)
            {
                TerrainData data = terrain.terrainData;
                int res = data.detailResolution;
                float size = data.size.x;
                int bestLayer = -1, bestX = -1, bestZ = -1, bestValue = 0;
                foreach (FernLayer layer in report.fern.layers)
                {
                    int[,] cells = data.GetDetailLayer(0, 0, res, res, layer.prototypeIndex);
                    for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                    {
                        if (cells[x, z] <= bestValue) continue;
                        bestValue = cells[x, z];
                        bestLayer = layer.prototypeIndex;
                        bestX = x;
                        bestZ = z;
                    }
                }
                if (bestValue > 0)
                {
                    Vector3 world = terrain.transform.TransformPoint(new Vector3(
                        (bestX + 0.5f) / res * size, 0f, (bestZ + 0.5f) / res * size));
                    targets.Add(MakeViewTarget("densestFern", new Vector2(world.x, world.z),
                        $"层 {bestLayer} / 单格密度 {bestValue}"));
                }
            }

            targets.AddRange(NearestTargets("nearestUnderstoryRock", report.understoryRocks));
            targets.AddRange(NearestTargets("nearestDeadwood", report.deadwood));

            CentralRockEntry nearestRock = report.centralRocks.entries
                .OrderBy(e => DistanceToTravelBoxXZ(ParseX(e.position), ParseZ(e.position)))
                .FirstOrDefault();
            if (nearestRock != null)
                targets.Add(MakeViewTarget("nearestCentralRock",
                    new Vector2(ParseX(nearestRock.position), ParseZ(nearestRock.position)), nearestRock.name));

            plan.targets = targets.ToArray();
            return plan;
        }

        private static IEnumerable<ViewTarget> NearestTargets(string label, EntityGroupReport group)
        {
            if (group.instances == null || group.instances.Length == 0) yield break;
            EntityInstance nearest = group.instances
                .OrderBy(inst => DistanceToTravelBoxXZ(ParseX(inst.position), ParseZ(inst.position)))
                .First();
            yield return MakeViewTarget(label, new Vector2(ParseX(nearest.position), ParseZ(nearest.position)),
                $"{nearest.name}（{nearest.group}）");
        }

        /// <summary>把目标点钳进玩家可达矩形（不再假定 ±limit 正方盒）。</summary>
        private static Vector2 ClampToPlayerBox(Vector2 p) => new Vector2(
            Mathf.Clamp(p.x, PlayerExplorationRect.xMin, PlayerExplorationRect.xMax),
            Mathf.Clamp(p.y, PlayerExplorationRect.yMin, PlayerExplorationRect.yMax));

        private static ViewTarget MakeViewTarget(string label, Vector2 target, string detail)
        {
            var observer = ClampToPlayerBox(target);
            Vector2 delta = target - observer;
            return new ViewTarget
            {
                label = label,
                detail = detail,
                targetXZ = Vec2(target),
                observerXZ = Vec2(observer),
                bearingDeg = F(Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg),
                distanceMeters = F(delta.magnitude),
            };
        }

        // ================================================================ 报告写出

        private static void WriteReport(AcceptanceReport report, string label, out string jsonPath, out string metricsPath)
        {
            string folder = OutputFolder;
            Directory.CreateDirectory(folder);
            jsonPath = FullPath(OutputRelativeFolder + "/" + label + ".json");
            metricsPath = FullPath(OutputRelativeFolder + "/" + label + ".metrics");

            string json = JsonUtility.ToJson(report, true);
            string metrics = BuildMetrics(report);
            WriteAllTextAtomic(jsonPath, json);
            WriteAllTextAtomic(metricsPath, metrics);
        }

        /// <summary>扁平 key&lt;TAB&gt;value 投影：既是 Compare 的机器输入，也是人读的一屏摘要。</summary>
        private static string BuildMetrics(AcceptanceReport r)
        {
            var lines = new List<string> { "schema=jw.metrics/1" };
            void Add(string key, string value) => lines.Add(key + "\t" + value);

            Add("label", r.label);
            Add("capturedUtc", r.capturedUtc);
            Add("unityVersion", r.unityVersion);
            Add("gitHead", r.gitHead ?? "");

            Add("constants.previewScale", r.constants.previewScale.ToString(CultureInfo.InvariantCulture));
            Add("constants.debugProceduralRocksOn", r.constants.debugProceduralRocksOn.ToString());
            Add("constants.passEnabled", r.constants.passEnabled.ToString());
            Add("constants.dinoSafetyBuffer", r.constants.dinoSafetyBuffer);
            Add("constants.playerBounds", r.constants.playerBounds);
            Add("constants.playerBoundsMin", r.constants.playerBoundsMin);
            Add("constants.playerBoundsMax", r.constants.playerBoundsMax);
            Add("constants.worldBounds", r.constants.worldBounds);
            Add("constants.fernFolder", r.constants.fernFolder);
            Add("constants.rockFolder", r.constants.rockFolder);
            Add("constants.deadwoodFolder", r.constants.deadwoodFolder);
            Add("constants.showcaseSpotCount", r.constants.showcaseSpotCount.ToString(CultureInfo.InvariantCulture));
            Add("constants.showcaseRockCount", r.constants.showcaseRockCount.ToString(CultureInfo.InvariantCulture));
            Add("constants.showcaseLogCount", r.constants.showcaseLogCount.ToString(CultureInfo.InvariantCulture));
            Add("constants.showcaseStumpCount", r.constants.showcaseStumpCount.ToString(CultureInfo.InvariantCulture));
            Add("constants.showcaseClearRadius", r.constants.showcaseClearRadius);
            Add("constants.showcaseLaneHalfWidth", r.constants.showcaseLaneHalfWidth);

            Add("terrainData.path", r.terrainData.path);
            Add("terrainData.sha256", r.terrainData.sha256);
            Add("terrainData.sizeBytes", r.terrainData.sizeBytes.ToString(CultureInfo.InvariantCulture));
            Add("mainScene.path", r.mainScene.path);
            Add("mainScene.sha256", r.mainScene.sha256);
            Add("mainScene.sizeBytes", r.mainScene.sizeBytes.ToString(CultureInfo.InvariantCulture));

            foreach (DetailLayerSummary layer in r.terrain.detailLayers ?? Array.Empty<DetailLayerSummary>())
            {
                Add($"terrain.detailLayer.{layer.prototypeIndex}.kind", layer.kind);
                Add($"terrain.detailLayer.{layer.prototypeIndex}.sumDensity", layer.sumDensity.ToString(CultureInfo.InvariantCulture));
            }
            Add("terrain.detailLayersDigest", r.terrain.detailLayersDigest);
            Add("terrain.treeInstanceCount", r.terrain.treeInstanceCount.ToString(CultureInfo.InvariantCulture));
            Add("terrain.treePrototypeCount", r.terrain.treePrototypeCount.ToString(CultureInfo.InvariantCulture));
            Add("terrain.centralTreePrototypeCount", r.terrain.centralTreePrototypeCount.ToString(CultureInfo.InvariantCulture));
            Add("terrain.forestLeakCount", r.terrain.forestLeakCount.ToString(CultureInfo.InvariantCulture));
            Add("terrain.detailPrototypesLength", r.terrain.detailPrototypesLength.ToString(CultureInfo.InvariantCulture));
            Add("terrain.detailResolution", r.terrain.detailResolution.ToString(CultureInfo.InvariantCulture));
            Add("terrain.alphamapResolution", r.terrain.alphamapResolution.ToString(CultureInfo.InvariantCulture));
            Add("terrain.heightmapResolution", r.terrain.heightmapResolution.ToString(CultureInfo.InvariantCulture));
            Add("terrain.terrainLayersLength", r.terrain.terrainLayersLength.ToString(CultureInfo.InvariantCulture));
            Add("terrain.sizeX", r.terrain.sizeX);
            Add("terrain.sizeY", r.terrain.sizeY);
            Add("terrain.sizeZ", r.terrain.sizeZ);
            Add("terrain.terrainPosition", r.terrain.terrainPosition);
            Add("terrain.drawInstanced", r.terrain.drawInstanced.ToString());
            Add("terrain.treeDistance", r.terrain.treeDistance);
            Add("terrain.treeBillboardDistance", r.terrain.treeBillboardDistance);
            Add("terrain.treeMaximumFullLODCount", r.terrain.treeMaximumFullLODCount.ToString(CultureInfo.InvariantCulture));
            Add("terrain.treeCrossFadeLength", r.terrain.treeCrossFadeLength);
            Add("terrain.detailObjectDistance", r.terrain.detailObjectDistance);
            Add("terrain.detailObjectDensity", r.terrain.detailObjectDensity);
            Add("terrain.wavingGrassStrength", r.terrain.wavingGrassStrength);
            Add("terrain.wavingGrassAmount", r.terrain.wavingGrassAmount);
            Add("terrain.wavingGrassSpeed", r.terrain.wavingGrassSpeed);
            Add("terrain.basemapDistance", r.terrain.basemapDistance);
            Add("terrain.heightmapPixelError", r.terrain.heightmapPixelError);
            Add("terrain.coreRect", r.terrain.coreRect);
            Add("terrain.channelAzimuths", r.terrain.channelAzimuths);
            Add("terrain.channelOrigin", r.terrain.channelOrigin);
            Add("terrain.channelHalfWidth", r.terrain.channelHalfWidth);
            Add("terrain.channelSegmentLength", r.terrain.channelSegmentLength);

            Add("fern.layerCount", r.fern.layerCount.ToString(CultureInfo.InvariantCulture));
            Add("fern.totalSumDensity", r.fern.totalSumDensity.ToString(CultureInfo.InvariantCulture));
            Add("fern.layersDigest", r.fern.layersDigest);
            Add("fern.distinctLayerSumCount", r.fern.distinctLayerSumCount.ToString(CultureInfo.InvariantCulture));
            Add("fern.conservationOk", r.fern.conservationOk.ToString());
            Add("fern.seederLastFernTotalTarget", r.fern.seederLastFernTotalTarget.ToString(CultureInfo.InvariantCulture));
            Add("fern.seederLastFernAssigned", r.fern.seederLastFernAssigned.ToString(CultureInfo.InvariantCulture));
            Add("fern.detailObjectDensity", r.fern.detailObjectDensity);
            Add("fern.detailObjectDistance", r.fern.detailObjectDistance);
            foreach (FernLayer layer in r.fern.layers ?? Array.Empty<FernLayer>())
            {
                Add($"fern.layer.{layer.prototypeIndex}.prefab", layer.prefabName);
                Add($"fern.layer.{layer.prototypeIndex}.sumDensity", layer.sumDensity.ToString(CultureInfo.InvariantCulture));
                Add($"fern.layer.{layer.prototypeIndex}.approxInstances", layer.approxInstances.ToString(CultureInfo.InvariantCulture));
                Add($"fern.layer.{layer.prototypeIndex}.meshTriangles", layer.meshTriangles.ToString(CultureInfo.InvariantCulture));
                Add($"fern.layer.{layer.prototypeIndex}.materialShader", layer.materialShader);
            }

            AddGroup(lines, "understoryRocks", r.understoryRocks);
            AddGroup(lines, "vegetation", r.vegetation);
            AddGroup(lines, "deadwood", r.deadwood);
            AddGroup(lines, "groundPassRocks", r.groundPassRocks);
            Add("environmentProps.childCount", r.environmentPropsChildCount.ToString(CultureInfo.InvariantCulture));

            Add("centralRocks.count", r.centralRocks.count.ToString(CultureInfo.InvariantCulture));
            Add("centralRocks.primitiveMeshCount", r.centralRocks.primitiveMeshCount.ToString(CultureInfo.InvariantCulture));
            Add("centralRocks.outOfBandFootprintCount", r.centralRocks.outOfBandFootprintCount.ToString(CultureInfo.InvariantCulture));
            Add("centralRocks.occupancyRecordedCount", r.centralRocks.occupancyRecordedCount.ToString(CultureInfo.InvariantCulture));
            Add("centralRocks.distinctSourcePrefabCount", r.centralRocks.distinctSourcePrefabCount.ToString(CultureInfo.InvariantCulture));
            Add("centralRocks.xzDigest", r.centralRocks.xzDigest);
            foreach (CentralRockEntry entry in r.centralRocks.entries ?? Array.Empty<CentralRockEntry>())
            {
                Add($"centralRocks.entry.{entry.name}.source", string.IsNullOrEmpty(entry.sourcePrefabName) ? entry.meshName : entry.sourcePrefabName);
                Add($"centralRocks.entry.{entry.name}.occupancyRatio", entry.occupancyRatio ?? "");
            }

            foreach (GatePrefabReport prefab in r.gatePrefabs ?? Array.Empty<GatePrefabReport>())
            {
                string key = "gatePrefab." + Path.GetFileNameWithoutExtension(prefab.assetPath);
                Add(key + ".triangles", prefab.triangles.ToString(CultureInfo.InvariantCulture));
                Add(key + ".rendererCount", prefab.rendererCount.ToString(CultureInfo.InvariantCulture));
                Add(key + ".materialShaders", prefab.materialShaders);
                Add(key + ".missingMaterialCount", prefab.missingMaterialCount.ToString(CultureInfo.InvariantCulture));
                Add(key + ".lodSummary", prefab.lodSummary);
                Add(key + ".boundsSize", prefab.boundsSize);
            }

            Add("assets.fileCount", r.assets.fileCount.ToString(CultureInfo.InvariantCulture));
            Add("assets.inventoryDigest", r.assets.inventoryDigest);
            Add("assets.fernPrefabCount", r.assets.fernPrefabCount.ToString(CultureInfo.InvariantCulture));
            Add("assets.rockPrefabCount", r.assets.rockPrefabCount.ToString(CultureInfo.InvariantCulture));
            Add("assets.deadwoodPrefabCount", r.assets.deadwoodPrefabCount.ToString(CultureInfo.InvariantCulture));
            Add("assets.duplicateContentGroupCount", r.assets.duplicateContentGroups.Length.ToString(CultureInfo.InvariantCulture));
            Add("assets.duplicateContentGroups", string.Join(" || ", r.assets.duplicateContentGroups));
            foreach (AssetFile file in r.assets.files ?? Array.Empty<AssetFile>())
                Add("assets.file." + file.path, file.sha256);

            Add("missingMaterialCount", r.missingMaterialCount.ToString(CultureInfo.InvariantCulture));

            Add("fernMaterial.available", r.fernMaterial.available.ToString());
            Add("fernMaterial.shader", r.fernMaterial.shader ?? "");
            Add("fernMaterial.shaderSupported", r.fernMaterial.shaderSupported.ToString());
            Add("fernMaterial.cutoff", r.fernMaterial.cutoff ?? "");
            Add("fernMaterial.cull", r.fernMaterial.cull ?? "");
            Add("fernMaterial.doubleSided", r.fernMaterial.doubleSided.ToString());
            Add("fernMaterial.albedoTexturePath", r.fernMaterial.albedoTexturePath ?? "");
            Add("fernMaterial.albedoMaxTextureSize", r.fernMaterial.albedoMaxTextureSize.ToString(CultureInfo.InvariantCulture));
            Add("fernMaterial.albedoAlphaIsTransparency", r.fernMaterial.albedoAlphaIsTransparency.ToString());

            Add("viewPlan.travelBox", r.viewPlan.travelBox);
            foreach (ViewTarget target in r.viewPlan.targets ?? Array.Empty<ViewTarget>())
            {
                Add($"viewPlan.{target.label}.targetXZ", target.targetXZ);
                Add($"viewPlan.{target.label}.observerXZ", target.observerXZ);
                Add($"viewPlan.{target.label}.bearingDeg", target.bearingDeg);
                Add($"viewPlan.{target.label}.distanceMeters", target.distanceMeters);
                Add($"viewPlan.{target.label}.detail", target.detail);
            }

            Add("warning.count", r.warnings.Length.ToString(CultureInfo.InvariantCulture));
            foreach (AcceptanceWarning warning in r.warnings)
                Add("warning." + warning.code, warning.detail);

            // 确定性输出：键按 Ordinal 排序（除 schema 首行），值保持写定内容
            List<string> body = lines.Skip(1).OrderBy(l => l, StringComparer.Ordinal).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(lines[0]);
            foreach (string line in body) sb.AppendLine(line);
            return sb.ToString();
        }

        private static void AddGroup(List<string> lines, string prefix, EntityGroupReport group)
        {
            void Add(string key, string value) => lines.Add(key + "\t" + value);
            Add(prefix + ".rootPath", group.rootPath);
            Add(prefix + ".childCount", group.childCount.ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".distinctPrefabCount", group.distinctPrefabCount.ToString(CultureInfo.InvariantCulture));
            Add(prefix + ".countsDigest", group.countsDigest);
            Add(prefix + ".positionsDigest", group.positionsDigest);
            Add(prefix + ".nearestToTravelBoxName", group.nearestToTravelBoxName ?? "");
            Add(prefix + ".nearestToTravelBoxDistance", group.nearestToTravelBoxDistance ?? "");
            foreach (EntityCount count in group.byPrefab ?? Array.Empty<EntityCount>())
                Add($"{prefix}.byPrefab.{count.prefabName}", count.count.ToString(CultureInfo.InvariantCulture));
            // 逐实例明细只进 JSON（体量大且比较用不上 digest 以外的信息）；这里只留 sink 极值供快速判读
            if (group.instances != null && group.instances.Length > 0)
            {
                Add(prefix + ".sinkMetersMin", F(group.instances.Min(i => ParseF(i.sinkMeters))));
                Add(prefix + ".sinkMetersMax", F(group.instances.Max(i => ParseF(i.sinkMeters))));
                Add(prefix + ".trianglesTotal", group.instances.Sum(i => i.triangles).ToString(CultureInfo.InvariantCulture));
                int lodGroups = group.instances.Count(i => !string.IsNullOrEmpty(i.lodSummary) && i.lodSummary.StartsWith("LODGroup", StringComparison.Ordinal));
                Add(prefix + ".lodGroupCount", lodGroups.ToString(CultureInfo.InvariantCulture));
                Add(prefix + ".lodSummaryDigest", Digest(string.Join("|", group.instances
                    .Where(i => !string.IsNullOrEmpty(i.lodSummary))
                    .OrderBy(i => i.name, StringComparer.Ordinal)
                    .Select(i => i.name + ":" + i.lodSummary))));
            }
        }

        // ================================================================ Bake#1 结构门控

        private static void VerifyBake1Core() => RunGuarded(() =>
        {
            LogContract();
            string prePath = FullPath(OutputRelativeFolder + "/prebake.metrics");
            string postPath = FullPath(OutputRelativeFolder + "/postbake1.metrics");
            if (!File.Exists(prePath) || !File.Exists(postPath))
            {
                Debug.LogError($"JURASSIC_BAKE1_VERIFY_FAIL: 缺少 {prePath} 或 {postPath}；请先执行 Capture Pre-Bake Baseline 与 Capture Post-Bake #1。");
                return;
            }

            var pre = ReadMetrics(prePath);
            var post = ReadMetrics(postPath);
            var results = new List<GateResult>();

            void Gate(string id, bool ok, string detail) => results.Add(new GateResult(id, ok, false, detail));
            void SoftGate(string id, bool ok, string detail) => results.Add(new GateResult(id, ok, true, detail));
            string Get(Dictionary<string, string> dict, string key) => dict.TryGetValue(key, out string value) ? value : "<missing>";

            // B2/B6b/B7b 原本是「历史坏态 → 修复态」的**转变**断言（pre 侧硬编码 0 层 fern / 12 个 Sphere 占位）。
            // 但 prebake 基线如今采自**已修复**的状态（回滚态基线是 Logs/acceptance/backup_prebake/，9/21，早已不用），
            // 继续要求 pre==坏态 ⇒ 每次验收都必然误报 FAIL。故这三门改为「契约 + 幂等」：post 守住终态契约，
            // pre==post 守住「烘焙不改状态」。fern 层数与占位石头只断 post；hero 灌木另见 B6b 的常量说明。
            Gate("B2-fern-layers", I(post, "fern.layerCount") == 4,
                $"fern 层数 pre={Get(pre, "fern.layerCount")} → post={Get(post, "fern.layerCount")}（post 期望 4；pre 仅作参考）");
            Gate("B3-fern-distinct-sums", I(post, "fern.distinctLayerSumCount") == I(post, "fern.layerCount") && I(post, "fern.layerCount") > 0,
                $"逐层 Σdensity 互不相同层数={Get(post, "fern.distinctLayerSumCount")}/{Get(post, "fern.layerCount")}，总 Σdensity={Get(post, "fern.totalSumDensity")}，" +
                $"≈实例={Get(post, "fern.totalSumDensity")}×{Get(post, "fern.detailObjectDensity")}");
            Gate("B4-fern-detail-proto-count", I(post, "terrain.detailPrototypesLength") == 11,
                $"detailPrototypesLength={Get(post, "terrain.detailPrototypesLength")}（期望 11 = 7 原有 + 4 fern）");
            Gate("B5-rock-instances", I(post, "understoryRocks.childCount") > 0 && I(post, "understoryRocks.childCount") <= 225,
                $"rock 实例={Get(post, "understoryRocks.childCount")}，种类={Get(post, "understoryRocks.distinctPrefabCount")}（期望 1..225 且 ≤6 种）");
            Gate("B5b-rock-variants", I(post, "understoryRocks.distinctPrefabCount") == 6,
                $"rock prefab 种类={Get(post, "understoryRocks.distinctPrefabCount")}（期望 6）");

            int logs = SumPrefabCounts(post, "deadwood.byPrefab.", name => name.IndexOf("LogTrunk", StringComparison.OrdinalIgnoreCase) >= 0);
            int stumps = SumPrefabCounts(post, "deadwood.byPrefab.", name => name.IndexOf("Stump", StringComparison.OrdinalIgnoreCase) >= 0);
            Gate("B6-deadwood-counts", logs >= 6 && logs <= 8 && stumps >= 3 && stumps <= 5,
                $"deadwood log={logs}（期望 6..8）stump={stumps}（期望 3..5），总实例={Get(post, "deadwood.childCount")}");
            // 期望值 44 → 39：UnderstorySeeder 的 hero 灌木 cluster 化改造（9/23）把数量定格在 39，
            // 该值已由 VALIDATION.md「Understory Pass 实体…岩石 151 / hero 灌木 39 / deadwood 13」记为当前契约。
            Gate("B6b-vegetation-unchanged", I(post, "vegetation.childCount") == I(pre, "vegetation.childCount") && I(post, "vegetation.childCount") == 39,
                $"hero 灌木 pre={Get(pre, "vegetation.childCount")} → post={Get(post, "vegetation.childCount")}（pre==post 且 post==39）");
            Gate("B6c-ground-pass-rocks-retired", I(post, "groundPassRocks.childCount") == -1,
                $"已退役的 Ground Pass Rocks root：pre={Get(pre, "groundPassRocks.childCount")} 个实例 → post={Get(post, "groundPassRocks.childCount")}" +
                "（-1 = root 不存在，即退役生效；外围岩石由 Understory cluster 独占）");

            Gate("B7-central-rock-count", I(post, "centralRocks.count") == 12, $"centralRocks.count={Get(post, "centralRocks.count")}（期望 12）");
            Gate("B7b-central-rock-hq", I(post, "centralRocks.primitiveMeshCount") == 0,
                $"primitiveMeshCount pre={Get(pre, "centralRocks.primitiveMeshCount")} → post={Get(post, "centralRocks.primitiveMeshCount")}" +
                "（post 期望 0 = 中央 12 岩石全为 HQ prefab、无 Sphere 占位；Sphere→HQ 的历史转变早已完成，不再要求 pre==12）");
            Gate("B7c-central-rock-layout-frozen", Get(post, "centralRocks.xzDigest") == Get(pre, "centralRocks.xzDigest"),
                $"xzDigest pre={Get(pre, "centralRocks.xzDigest")} post={Get(post, "centralRocks.xzDigest")}（必须相同：只换资产不换布局）");
            SoftGate("B7d-central-rock-footprint",
                I(post, "centralRocks.outOfBandFootprintCount") == 0 && I(post, "centralRocks.occupancyRecordedCount") == 12,
                $"footprint 记录 {Get(post, "centralRocks.occupancyRecordedCount")}/12，超出 ±25% 的岩石数={Get(post, "centralRocks.outOfBandFootprintCount")}" +
                "（记录不足 12 通常因 Bake 与采集之间发生脚本重编译导致域重载，进程内 footprint 记录丢失）");

            Gate("B8-core-rect-leak", I(post, "terrain.forestLeakCount") == 0, $"核心矩形森林树泄漏={Get(post, "terrain.forestLeakCount")}（期望 0）");
            // 森林树数：展示环清空会主动让开一批树（可见性的必要代价），故只断言"不多于基线且减少量在预期内"，
            // 不再要求逐值不变；精确的可见性判据由 B15（最近实体道具距可达盒）承担。
            int treesPre = I(pre, "terrain.treeInstanceCount");
            int treesPost = I(post, "terrain.treeInstanceCount");
            Gate("B9-tree-count-clearance-aware", treesPost <= treesPre && treesPre - treesPost <= MaxShowcaseTreeReduction,
                $"treeInstanceCount pre={treesPre} → post={treesPost}（期望 0 ≤ 减少量 ≤ {MaxShowcaseTreeReduction}：展示环清空让树）");
            Gate("B10-no-missing-material", I(post, "missingMaterialCount") == 0, $"缺失/不受支持材质数={Get(post, "missingMaterialCount")}（期望 0）");
            Gate("B11-prefab-meshes", GatePrefabAllPositive(post, "triangles"),
                "门控 prefab 三角面全部 > 0：" + string.Join(", ", PrefabKeys(post, "triangles").Select(
                    k => $"{k.Replace("gatePrefab.", "").Replace(".triangles", "")}={Get(post, k)}")));
            Gate("B11b-prefab-materials", GatePrefabAllZero(post, "missingMaterialCount"),
                "门控 prefab missingMaterialCount 全部为 0：" + string.Join(", ", PrefabKeys(post, "missingMaterialCount").Select(
                    k => $"{k.Replace("gatePrefab.", "").Replace(".missingMaterialCount", "")}={Get(post, k)}")));
            Gate("B12-deadwood-lod", DeadwoodLodOk(post), $"DeadwoodLogTrunk02 LOD：{Get(post, "gatePrefab.DeadwoodLogTrunk02.lodSummary")}");
            Gate("B13-constants", I(post, "constants.previewScale") == 1 && Get(post, "constants.debugProceduralRocksOn") == "False" &&
                                  Get(post, "constants.passEnabled") == "True",
                $"previewScale={Get(post, "constants.previewScale")} debugRocks={Get(post, "constants.debugProceduralRocksOn")} passEnabled={Get(post, "constants.passEnabled")}");
            Gate("B14-assets-no-duplicates", I(post, "assets.duplicateContentGroupCount") == 0 && I(post, "assets.fernPrefabCount") == 4 &&
                                             I(post, "assets.rockPrefabCount") == 6 && I(post, "assets.deadwoodPrefabCount") == 2,
                $"资产清单 fileCount={Get(post, "assets.fileCount")}，重复内容组={Get(post, "assets.duplicateContentGroupCount")}，" +
                $"门控 prefab=fern {Get(post, "assets.fernPrefabCount")}/rock {Get(post, "assets.rockPrefabCount")}/deadwood {Get(post, "assets.deadwoodPrefabCount")}");

            // 展示环可见性：实体道具必须落在平坦矩形外 12.5–20m 的近林带（ForestSeeder 据同一布局让开森林树）
            Gate("B15-showcase-layout", I(post, "constants.showcaseSpotCount") >= MinShowcaseSpotCount &&
                                       I(post, "constants.showcaseRockCount") >= 5 &&
                                       I(post, "constants.showcaseLogCount") >= 2 && I(post, "constants.showcaseStumpCount") >= 2,
                $"展示环道具位={Get(post, "constants.showcaseSpotCount")}（岩石 {Get(post, "constants.showcaseRockCount")}/" +
                $"倒木 {Get(post, "constants.showcaseLogCount")}/树桩 {Get(post, "constants.showcaseStumpCount")}，" +
                $"期望 ≥{MinShowcaseSpotCount} 且各类 ≥5/2/2）");
            Gate("B15b-showcase-nearest", NearestWithin(Get(post, "understoryRocks.nearestToTravelBoxDistance"), ShowcaseMaxDistanceToTravelBox),
                $"最近林下岩石距可达盒={Get(post, "understoryRocks.nearestToTravelBoxDistance")}m（{Get(post, "understoryRocks.nearestToTravelBoxName")}，期望 ≤{ShowcaseMaxDistanceToTravelBox}m；" +
                $"修复前为 ~35.5m）");
            Gate("B15c-showcase-nearest-deadwood", NearestWithin(Get(post, "deadwood.nearestToTravelBoxDistance"), ShowcaseMaxDistanceToTravelBox),
                $"最近倒木/树桩距可达盒={Get(post, "deadwood.nearestToTravelBoxDistance")}m（{Get(post, "deadwood.nearestToTravelBoxName")}，期望 ≤{ShowcaseMaxDistanceToTravelBox}m；" +
                $"修复前为 ~34.1m）");
            // detail 风力：蕨类走 Grass 渲染模式，未压低时整株左右平移（渲染可见缺陷）
            Gate("B15d-detail-wind", WithinTolerance(Get(post, "terrain.wavingGrassStrength"), JurassicWorldSceneBuilder.DetailWindStrengthTarget) &&
                                    WithinTolerance(Get(post, "terrain.wavingGrassAmount"), JurassicWorldSceneBuilder.DetailWindAmountTarget),
                $"wavingGrass strength={Get(post, "terrain.wavingGrassStrength")}、amount={Get(post, "terrain.wavingGrassAmount")}、" +
                $"speed={Get(post, "terrain.wavingGrassSpeed")}（期望 {JurassicWorldSceneBuilder.DetailWindStrengthTarget:F2}/" +
                $"{JurassicWorldSceneBuilder.DetailWindAmountTarget:F2}，保留轻微摆动）");

            EmitGateResults("VERIFY", "JURASSIC_BAKE1_VERIFY", results);
        });

        // 展示环验收阈值：清空让树上限、最少道具位、最近道具距可达盒上限、风力容差
        private const int MaxShowcaseTreeReduction = 400;
        private const int MinShowcaseSpotCount = 9;
        private const float ShowcaseMaxDistanceToTravelBox = 20f;
        private const float WindValueTolerance = 0.01f;

        /// <summary>「最近实体道具距可达盒」判据：缺失/非法值直接失败（ParseF 失败会返回 0，必须显式排除）。</summary>
        private static bool NearestWithin(string value, float maxMeters)
        {
            if (string.IsNullOrEmpty(value) || value == "<missing>")
                return false;
            float parsed = ParseF(value);
            return parsed > 0f && parsed <= maxMeters;
        }

        private static bool WithinTolerance(string value, float expected)
        {
            if (string.IsNullOrEmpty(value) || value == "<missing>")
                return false;
            return Mathf.Abs(ParseF(value) - expected) <= WindValueTolerance;
        }

        // ================================================================ 幂等比对（#1 vs #2）

        private static void CompareIdempotency() => RunGuarded(() =>
        {
            LogContract();
            string aPath = FullPath(OutputRelativeFolder + "/postbake1.metrics");
            string bPath = FullPath(OutputRelativeFolder + "/postbake2.metrics");
            if (!File.Exists(aPath) || !File.Exists(bPath))
            {
                Debug.LogError($"JURASSIC_IDEMPOTENCY_FAIL: 缺少 {aPath} 或 {bPath}；请先完成两次 Full Bake 及对应采集。");
                return;
            }

            var a = ReadMetrics(aPath);
            var b = ReadMetrics(bPath);
            var results = new List<GateResult>();

            CompareExact("I1-terrain-hash", a, b, new[] { "terrainData.sha256" }, results);
            ComparePrefix("I2-fern", "fern.", a, b, results);
            ComparePrefix("I3-understory-rocks", "understoryRocks.", a, b, results);
            ComparePrefix("I4-deadwood", "deadwood.", a, b, results);
            ComparePrefix("I5-vegetation", "vegetation.", a, b, results);
            CompareExact("I6-central-rock-layout", a, b, new[] { "centralRocks.count", "centralRocks.xzDigest" }, results);
            ComparePrefix("I7-assets", "assets.", a, b, results);
            ComparePrefix("I8-terrain", "terrain.", a, b, results);
            CompareExact("I8b-environment-props", a, b, new[] { "environmentProps.childCount" }, results);
            ComparePrefix("I9-constants", "constants.", a, b, results);
            ComparePrefix("I10-fern-material", "fernMaterial.", a, b, results);

            // I6 追加：与 prebake 的三方一致（换资产不换布局的完整证据链）
            string prePath = FullPath(OutputRelativeFolder + "/prebake.metrics");
            if (File.Exists(prePath))
            {
                var pre = ReadMetrics(prePath);
                results.Add(new GateResult("I6b-central-rock-vs-prebake",
                    pre.TryGetValue("centralRocks.xzDigest", out string preDigest) &&
                    preDigest == GetValue(a, "centralRocks.xzDigest") && preDigest == GetValue(b, "centralRocks.xzDigest"),
                    false,
                    $"prebake={preDigest} postbake1={GetValue(a, "centralRocks.xzDigest")} postbake2={GetValue(b, "centralRocks.xzDigest")}"));
            }

            // 软门控：主场景字节一致性（内容计数一致时可能仅为序列化非确定性，必须解释而非静默放过）
            bool sceneSame = GetValue(a, "mainScene.sha256") == GetValue(b, "mainScene.sha256");
            results.Add(new GateResult("SOFT-main-scene-hash", sceneSame, true,
                $"Main.unity sha256 postbake1={GetValue(a, "mainScene.sha256")} postbake2={GetValue(b, "mainScene.sha256")}" +
                (sceneSame ? "" : "（不同：若 I2..I8 全 PASS，则属场景序列化非确定性，须在报告中解释并附 --- !u! 块计数对比）")));

            EmitGateResults("Idem", "JURASSIC_IDEMPOTENCY", results);
        });

        private static void CompareExact(string gateId, Dictionary<string, string> a, Dictionary<string, string> b,
            string[] keys, List<GateResult> results)
        {
            var diffs = new List<string>();
            foreach (string key in keys)
            {
                string va = GetValue(a, key);
                string vb = GetValue(b, key);
                if (va != vb) diffs.Add($"{key}: {va} ≠ {vb}");
            }
            results.Add(new GateResult(gateId, diffs.Count == 0, false,
                diffs.Count == 0 ? $"全部相同：{string.Join(", ", keys)}" : string.Join("；", diffs)));
        }

        private static void ComparePrefix(string gateId, string prefix, Dictionary<string, string> a,
            Dictionary<string, string> b, List<GateResult> results)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string key in a.Keys) if (key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
            foreach (string key in b.Keys) if (key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);

            var diffs = new List<string>();
            foreach (string key in keys)
            {
                bool inA = a.TryGetValue(key, out string va);
                bool inB = b.TryGetValue(key, out string vb);
                if (!inA || !inB) diffs.Add($"{key}: {(inA ? va : "<仅 #1 有>")} vs {(inB ? vb : "<仅 #2 有>")}");
                else if (va != vb) diffs.Add($"{key}: {va} ≠ {vb}");
            }

            string detail = diffs.Count == 0
                ? $"全部相同（{keys.Count} 个键）"
                : $"不同 {diffs.Count}/{keys.Count} 个键：" + string.Join("；", diffs.Take(8)) + (diffs.Count > 8 ? " …" : "");
            results.Add(new GateResult(gateId, diffs.Count == 0, false, detail));
        }

        private static void EmitGateResults(string tag, string sentinelPrefix, List<GateResult> results)
        {
            int failures = 0, warnings = 0;
            foreach (GateResult result in results)
            {
                string status = result.Ok ? "PASS" : (result.Soft ? "WARN" : "FAIL");
                if (!result.Ok && result.Soft) warnings++;
                if (!result.Ok && !result.Soft) failures++;
                string line = $"[{tag}] GATE {result.Id}: {status} — {result.Detail}";
                if (status == "FAIL") Debug.LogError(line);
                else if (status == "WARN") Debug.LogWarning(line);
                else Debug.Log(line);
            }

            if (failures == 0)
                Debug.Log($"{sentinelPrefix}_OK: 全部 {results.Count} 项门控通过" +
                          (warnings > 0 ? $"（另有 {warnings} 项软门控需在报告中解释）" : "") + "。");
            else
                Debug.LogError($"{sentinelPrefix}_FAIL（{failures} 项失败，详见上方 [{tag}] GATE FAIL 行）：请立即停止后续步骤，先出 problem→likely cause→smallest fix 报告。");
        }

        // ================================================================ 工具

        private static void RunGuarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Acceptance] 未捕获异常：{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }

        private static Dictionary<string, string> ReadMetrics(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrEmpty(raw)) continue;
                int tab = raw.IndexOf('\t');
                if (tab <= 0) continue;
                dict[raw.Substring(0, tab)] = raw.Substring(tab + 1);
            }
            return dict;
        }

        private static string GetValue(Dictionary<string, string> dict, string key) =>
            dict.TryGetValue(key, out string value) ? value : "<missing>";

        private static int I(Dictionary<string, string> dict, string key) =>
            int.TryParse(GetValue(dict, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : int.MinValue;

        private static int SumPrefabCounts(Dictionary<string, string> dict, string prefix, Func<string, bool> predicate)
        {
            int total = 0;
            foreach (KeyValuePair<string, string> kv in dict)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string name = kv.Key.Substring(prefix.Length);
                if (!predicate(name)) continue;
                if (int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) total += count;
            }
            return total;
        }

        private static IEnumerable<string> PrefabKeys(Dictionary<string, string> dict, string suffix) =>
            dict.Keys.Where(k => k.StartsWith("gatePrefab.", StringComparison.Ordinal) && k.EndsWith("." + suffix, StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal);

        private static bool GatePrefabAllPositive(Dictionary<string, string> dict, string suffix)
        {
            bool any = false;
            foreach (string key in PrefabKeys(dict, suffix))
            {
                any = true;
                if (I(dict, key) <= 0) return false;
            }
            return any;
        }

        private static bool GatePrefabAllZero(Dictionary<string, string> dict, string suffix)
        {
            bool any = false;
            foreach (string key in PrefabKeys(dict, suffix))
            {
                any = true;
                if (I(dict, key) != 0) return false;
            }
            return any;
        }

        /// <summary>deadwood LOD：期望 LODGroup 且 4 级，且各级 rend&gt;0、无 missing。</summary>
        private static bool DeadwoodLodOk(Dictionary<string, string> dict)
        {
            string summary = GetValue(dict, "gatePrefab.DeadwoodLogTrunk02.lodSummary");
            if (!summary.StartsWith("LODGroup 4 级", StringComparison.Ordinal)) return false;
            if (summary.Contains("rend=0")) return false;
            return !summary.Contains("missing=1") && !summary.Contains("missing=2") && !summary.Contains("missing=3");
        }

        private static string ClassifyPrototype(DetailPrototype proto)
        {
            if (proto == null) return "null";
            if (IsFernPrototype(proto)) return "fern";
            if (proto.usePrototypeMesh) return "bush";
            if (proto.prototypeTexture != null) return "grass";
            return "other";
        }

        /// <summary>与 UnderstorySeeder.IsOwnedFernProto 同源的只读签名判定（不改 Seeder，也不依赖其私有方法）。</summary>
        private static bool IsFernPrototype(DetailPrototype proto)
        {
            if (proto == null || !proto.usePrototypeMesh || proto.prototype == null) return false;
            string path = AssetDatabase.GetAssetPath(proto.prototype);
            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith(UnderstoryAssetLocator.FernFolder + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static int SumDetailLayer(TerrainData data, int res, int layerIndex)
        {
            int[,] cells = data.GetDetailLayer(0, 0, res, res, layerIndex);
            long sum = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                sum += cells[x, z];
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (string.Equals(root.name, name, StringComparison.Ordinal))
                    return root.transform;
            return null;
        }

        private static string PrefabAssetPathOf(GameObject go)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source == null) return null;
            string path = AssetDatabase.GetAssetPath(source);
            return string.IsNullOrEmpty(path) ? null : path;
        }

        private static string MeshSignatureOf(GameObject go)
        {
            MeshFilter filter = go.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null) return "no-mesh";
            Renderer renderer = go.GetComponentInChildren<Renderer>(true);
            string material = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "no-mat";
            return filter.sharedMesh.name + "+" + material;
        }

        private static Renderer PrimaryRenderer(GameObject go)
        {
            LODGroup lod = go.GetComponent<LODGroup>();
            if (lod == null) lod = go.GetComponentInChildren<LODGroup>(true);
            if (lod != null)
            {
                LOD[] lods = lod.GetLODs();
                if (lods.Length > 0 && lods[0].renderers != null)
                    foreach (Renderer renderer in lods[0].renderers)
                        if (renderer != null) return renderer;
            }
            return go.GetComponentInChildren<Renderer>(true);
        }

        private static string DescribeLodGroup(LODGroup lod)
        {
            if (lod == null) return "";
            LOD[] lods = lod.GetLODs();
            var parts = new List<string>();
            for (int i = 0; i < lods.Length; i++)
            {
                int rendererCount = 0, triangles = 0, missing = 0;
                var materials = new SortedSet<string>(StringComparer.Ordinal);
                if (lods[i].renderers != null)
                {
                    foreach (Renderer renderer in lods[i].renderers)
                    {
                        if (renderer == null)
                        {
                            missing++;
                            continue;
                        }
                        rendererCount++;
                        triangles += TriangleCount(renderer);
                        materials.Add(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "<null>");
                    }
                }
                parts.Add($"LOD{i}[rend={rendererCount} tri={triangles} mat={string.Join("+", materials)} missing={missing}]");
            }
            return $"LODGroup {lods.Length} 级 anim={lod.animateCrossFading} fade={lod.fadeMode} " + string.Join(" ", parts);
        }

        private static int TriangleCount(Renderer renderer)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return 0;
            return filter.sharedMesh.triangles.Length / 3;
        }

        private static Bounds UnionBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static float GroundY(Terrain terrain, Vector3 worldPosition)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            float nx = Mathf.Clamp01((worldPosition.x - origin.x) / data.size.x);
            float nz = Mathf.Clamp01((worldPosition.z - origin.z) / data.size.z);
            return origin.y + data.GetInterpolatedHeight(nx, nz);
        }

        /// <summary>到玩家可达矩形的最短距离（米，矩形内为 0）。</summary>
        private static float DistanceToTravelBoxXZ(float x, float z)
        {
            float dx = Mathf.Max(Mathf.Max(PlayerExplorationRect.xMin - x, x - PlayerExplorationRect.xMax), 0f);
            float dz = Mathf.Max(Mathf.Max(PlayerExplorationRect.yMin - z, z - PlayerExplorationRect.yMax), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float ParseF(string value) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f;

        private static float ParseX(string position) => ParseComponent(position, 0);
        private static float ParseZ(string position) => ParseComponent(position, 2);

        private static float ParseComponent(string vector, int index)
        {
            if (string.IsNullOrEmpty(vector)) return 0f;
            string[] parts = vector.Split(',');
            if (index >= parts.Length) return 0f;
            return ParseF(parts[index]);
        }

        private static string XZOf(string position)
        {
            string[] parts = (position ?? "").Split(',');
            return parts.Length >= 3 ? $"{parts[0]},{parts[2]}" : position ?? "";
        }

        private static string RotYOf(string euler)
        {
            string[] parts = (euler ?? "").Split(',');
            return parts.Length >= 3 ? parts[1] : euler ?? "";
        }

        private static int CountPrefabs(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return 0;
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) count++;
            }
            return count;
        }

        private static void CollectFilesUnder(string assetFolder, List<AssetFile> into)
        {
            string full = FullPath(assetFolder);
            if (!Directory.Exists(full)) return;
            foreach (string file in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
            {
                string relative = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                into.Add(new AssetFile
                {
                    path = relative,
                    sha256 = Sha256Hex(File.ReadAllBytes(file)),
                    sizeBytes = new FileInfo(file).Length,
                });
            }
        }

        private static FileHash HashAsset(string assetPath)
        {
            var hash = new FileHash { path = assetPath };
            string full = FullPath(assetPath);
            if (!File.Exists(full))
            {
                hash.sha256 = "<missing>";
                return hash;
            }
            var info = new FileInfo(full);
            hash.sizeBytes = info.Length;
            hash.lastWriteUtc = info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
            hash.sha256 = Sha256Hex(File.ReadAllBytes(full));
            hash.guid = AssetDatabase.AssetPathToGUID(assetPath);
            return hash;
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var sb = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static string Digest(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text ?? string.Empty)).Substring(0, 32);

        private static string FullPath(string assetRelativePath)
        {
            if (assetRelativePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                assetRelativePath.StartsWith("Assets\\", StringComparison.Ordinal))
                return Path.GetFullPath(Path.Combine(Application.dataPath,
                    assetRelativePath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
                assetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string OutputFolder => FullPath(OutputRelativeFolder);

        private static void WriteAllTextAtomic(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static string TryGitHead()
        {
            try
            {
                string headFile = FullPath(".git/HEAD");
                if (!File.Exists(headFile)) return "";
                string head = File.ReadAllText(headFile).Trim();
                if (!head.StartsWith("ref:", StringComparison.Ordinal)) return head;
                string refPath = FullPath(".git/" + head.Substring(4).Trim());
                return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : head;
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static AcceptanceWarning Warn(string code, string detail) => new AcceptanceWarning { code = code, detail = detail };

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Vec3(Vector3 v) => $"{F(v.x)},{F(v.y)},{F(v.z)}";
        private static string Vec2(Vector2 v) => $"{F(v.x)},{F(v.y)}";
        private static string Color3(Color c) => $"{F(c.r)},{F(c.g)},{F(c.b)}";

        // ================================================================ DTO（JsonUtility 兼容：public 字段 + [Serializable]）

        [Serializable]
        public sealed class AcceptanceReport
        {
            public string schema;
            public string label;
            public string capturedUtc;
            public string unityVersion;
            public string projectPath;
            public string gitHead;
            public ProductionConstants constants;
            public FileHash terrainData;
            public FileHash mainScene;
            public TerrainSummary terrain;
            public FernReport fern;
            public EntityGroupReport understoryRocks;
            public EntityGroupReport vegetation;
            public EntityGroupReport deadwood;
            public EntityGroupReport groundPassRocks;
            public int environmentPropsChildCount;
            public CentralRockReport centralRocks;
            public GatePrefabReport[] gatePrefabs;
            public AssetInventory assets;
            public int missingMaterialCount;
            public FernMaterialProbe fernMaterial;
            public ViewPlan viewPlan;
            public AcceptanceWarning[] warnings;
        }

        [Serializable]
        public sealed class ProductionConstants
        {
            public int previewScale;
            public bool debugProceduralRocksOn;
            public bool passEnabled;
            public string dinoSafetyBuffer;
            public string fernFolder;
            public string rockFolder;
            public string deadwoodFolder;
            public string playerBounds;
            public string playerBoundsMin;
            public string playerBoundsMax;
            public string worldBounds;
            public int showcaseSpotCount;
            public int showcaseRockCount;
            public int showcaseLogCount;
            public int showcaseStumpCount;
            public string showcaseClearRadius;
            public string showcaseLaneHalfWidth;
        }

        [Serializable]
        public sealed class FileHash
        {
            public string path;
            public string sha256;
            public long sizeBytes;
            public string lastWriteUtc;
            public string guid;
        }

        [Serializable]
        public sealed class DetailLayerSummary
        {
            public int prototypeIndex;
            public string kind;
            public int sumDensity;
        }

        [Serializable]
        public sealed class TerrainSummary
        {
            public int treeInstanceCount;
            public int treePrototypeCount;
            public int centralTreePrototypeCount;
            public int forestLeakCount;
            public int detailPrototypesLength;
            public int detailResolution;
            public int alphamapResolution;
            public int heightmapResolution;
            public int terrainLayersLength;
            public string sizeX;
            public string sizeY;
            public string sizeZ;
            public string terrainPosition;
            public bool drawInstanced;
            public string treeDistance;
            public string treeBillboardDistance;
            public int treeMaximumFullLODCount;
            public string treeCrossFadeLength;
            public string detailObjectDistance;
            public string detailObjectDensity;
            public string wavingGrassStrength;
            public string wavingGrassAmount;
            public string wavingGrassSpeed;
            public string basemapDistance;
            public string heightmapPixelError;
            public string coreRect;
            public string channelAzimuths;
            public string channelOrigin;
            public string channelHalfWidth;
            public string channelSegmentLength;
            public DetailLayerSummary[] detailLayers;
            public string detailLayersDigest;
        }

        [Serializable]
        public sealed class FernLayer
        {
            public int prototypeIndex;
            public string prefabName;
            public string prefabAssetPath;
            public bool usePrototypeMesh;
            public string renderMode;
            public string minWidth;
            public string maxWidth;
            public string minHeight;
            public string maxHeight;
            public string noiseSpread;
            public string healthyColor;
            public string dryColor;
            public int sumDensity;
            public int approxInstances;
            public int meshTriangles;
            public string meshName;
            public string materialName;
            public string materialShader;
        }

        [Serializable]
        public sealed class FernReport
        {
            public int layerCount;
            public long totalSumDensity;
            public string layersDigest;
            public int distinctLayerSumCount;
            public bool conservationOk;
            public long seederLastFernTotalTarget;
            public long seederLastFernAssigned;
            public int seederLastFernVariantCount;
            public int detailPrototypesLength;
            public int detailResolution;
            public string detailObjectDensity;
            public string detailObjectDistance;
            public FernLayer[] layers;
        }

        [Serializable]
        public sealed class EntityCount
        {
            public string prefabName;
            public int count;
        }

        [Serializable]
        public sealed class EntityInstance
        {
            public string name;
            public string group;
            public string prefabAssetPath;
            public string position;
            public string groundY;
            public string sinkMeters;
            public string boundsSize;
            public string rotationEuler;
            public string scale;
            public int triangles;
            public string materialName;
            public string materialShader;
            public string lodSummary;
            public string distanceToTravelBox;
        }

        [Serializable]
        public sealed class EntityGroupReport
        {
            public string rootPath;
            public int childCount;
            public int distinctPrefabCount;
            public EntityCount[] byPrefab;
            public EntityInstance[] instances;
            public string countsDigest;
            public string positionsDigest;
            public string nearestToTravelBoxName;
            public string nearestToTravelBoxDistance;
        }

        [Serializable]
        public sealed class CentralRockEntry
        {
            public string name;
            public string sourcePrefabName;
            public string sourcePrefabPath;
            public string meshName;
            public bool isPrimitiveMesh;
            public string materialName;
            public string materialShader;
            public string position;
            public string rotationEuler;
            public string scale;
            public string boundsSize;
            public string groundY;
            public string sinkMeters;
            public string targetFootprint;
            public string actualFootprint;
            public string occupancyRatio;
        }

        [Serializable]
        public sealed class CentralRockReport
        {
            public int count;
            public int primitiveMeshCount;
            public int outOfBandFootprintCount;
            public int occupancyRecordedCount;
            public int distinctSourcePrefabCount;
            public string xzDigest;
            public CentralRockEntry[] entries;
        }

        [Serializable]
        public sealed class GatePrefabReport
        {
            public string assetPath;
            public string folder;
            public int rendererCount;
            public int triangles;
            public string materialShaders;
            public int missingMaterialCount;
            public string lodSummary;
            public string boundsSize;
        }

        [Serializable]
        public sealed class AssetFile
        {
            public string path;
            public string sha256;
            public long sizeBytes;
        }

        [Serializable]
        public sealed class AssetInventory
        {
            public string[] roots;
            public int fileCount;
            public string inventoryDigest;
            public AssetFile[] files;
            public int fernPrefabCount;
            public int rockPrefabCount;
            public int deadwoodPrefabCount;
            public string[] duplicateContentGroups;
        }

        [Serializable]
        public sealed class FernMaterialProbe
        {
            public bool available;
            public string unavailableReason;
            public string materialPath;
            public string shader;
            public bool shaderSupported;
            public string cutoff;
            public string cull;
            public bool doubleSided;
            public string albedoTexturePath;
            public int albedoMaxTextureSize;
            public bool albedoAlphaIsTransparency;
            public bool albedoSrgb;
        }

        [Serializable]
        public sealed class ViewTarget
        {
            public string label;
            public string detail;
            public string targetXZ;
            public string observerXZ;
            public string bearingDeg;
            public string distanceMeters;
        }

        [Serializable]
        public sealed class ViewPlan
        {
            public string travelBox;
            public ViewTarget[] targets;
        }

        [Serializable]
        public sealed class AcceptanceWarning
        {
            public string code;
            public string detail;
        }

        private sealed class GateResult
        {
            internal GateResult(string id, bool ok, bool soft, string detail)
            {
                Id = id;
                Ok = ok;
                Soft = soft;
                Detail = detail;
            }

            internal string Id { get; }
            internal bool Ok { get; }
            internal bool Soft { get; }
            internal string Detail { get; }
        }
    }
}
