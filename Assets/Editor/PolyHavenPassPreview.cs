using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// HQ Asset Pass 的【完全非破坏性】Preview Bake + 统计报告。
    ///
    /// 非破坏性契约（硬性）：
    ///  - 绝不写生产 Assets/Environment/Terrain/Prepared/JurassicTerrainData.asset，也绝不让 Main.unity 变脏；
    ///  - TerrainData 用 Object.Instantiate 克隆，派生物（岩石/灌木/fern 密度）全部落在克隆体上；
    ///  - 临时 Single 场景承载全部 GameObject；结束/异常/取消都销毁临时对象与克隆体；
    ///  - 全程不调用 AssetDatabase.SaveAssets / EditorSceneManager.SaveScene；
    ///  - Full Bake 是唯一被允许修改生产 TerrainData 的路径。
    ///
    /// 为什么必须 Single 场景：UnderstorySeeder.Seed 内部用 GameObject.Find("Understory Pass") 定位并
    /// DestroyImmediate 该 root，而 GameObject.Find 会跨【所有已加载场景】命中。若 Main.unity 同时在
    /// 场，就会销毁生产 root —— 因此 Preview 必须在【唯一已加载的临时场景】里运行。
    ///
    /// 缩小规模不改契约：只用 UnderstorySeeder.PreviewScale（确定性前缀），
    /// 不改任何分布常量、不改任何随机流；Full Bake 恒为 PreviewScale = 1。
    ///
    /// 菜单：Jurassic World/Diagnostics/Preview HQ Asset Pass
    /// 批处理：-executeMethod JurassicWorld.Editor.PolyHavenPassPreview.Run
    /// </summary>
    public static class PolyHavenPassPreview
    {
        private const string TerrainDataPath = "Assets/Environment/Terrain/Prepared/JurassicTerrainData.asset";

        /// <summary>Preview 缩小倍数（仅影响实例数量；正式 Bake 恒为 1）。</summary>
        private const int PreviewScaleFactor = 4;

        /// <summary>
        /// 生产 detailObjectDensity（渲染期实例概率乘数，见 JurassicWorldSceneBuilder：0.6）。
        /// 优先从生产场景的 Terrain 读取，批处理下取不到时用此兜底值并在报告中标注。
        /// </summary>
        private const float DefaultDetailObjectDensity = 0.6f;

        /// <summary>中央岩石 footprint 偏差告警阈值（±25%）。</summary>
        private const float FootprintToleranceRatio = 0.25f;

        /// <summary>Fern Cutout 透明像素占比的合理区间：低于下限说明几乎没裁切（会出现矩形背景），高于上限说明叶缘缺失过多。</summary>
        private const float CutoutTransparentMin = 0.10f;
        private const float CutoutTransparentMax = 0.92f;

        [MenuItem("Jurassic World/Diagnostics/Preview HQ Asset Pass")]
        private static void RunMenuMenu() => JurassicMenuGuard.Run(RunMenu, true);

        [MenuItem("Jurassic World/Diagnostics/Preview HQ Asset Pass", true)]
        private static bool RunMenuMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void RunMenu()
        {
            Run();
        }

        public static void Run()
        {
            int failures = 0;
            try
            {
                failures = Execute();
            }
            catch (Exception e)
            {
                Debug.LogError($"[HQPreview] Preview 异常中止（生产场景与 TerrainData 未被修改）：{e}");
                failures = Mathf.Max(1, failures);
            }
            finally
            {
                // 无论成功 / 失败 / 异常，PreviewScale 必须复位，避免影响后续正式 Bake。
                UnderstorySeeder.PreviewScale = 1;
            }

            Debug.Log(failures == 0
                ? "JURASSIC_POLYHAVEN_PREVIEW_OK"
                : $"JURASSIC_POLYHAVEN_PREVIEW_FAIL（{failures} 项检查未通过，详见上方 [HQPreview] 行）");
        }

        // ================================================================ 主体

        private static int Execute()
        {
            // (1) 资产铸造（幂等；这一步会正常创建 project-owned 资产，是本次接入的目标产物）
            PolyHavenAssetIntegration.EnsureAssets();

            TerrainData production = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (production == null)
            {
                Debug.LogError($"[HQPreview] 找不到 {TerrainDataPath}，无法 Preview。");
                return 1;
            }

            // (2) 安全门：当前场景若有未保存改动，Preview 会切换场景 → 中止，绝不丢弃内存改动。
            Scene previousScene = EditorSceneManager.GetActiveScene();
            string previousScenePath = previousScene.IsValid() ? previousScene.path : null;
            if (previousScene.IsValid() && previousScene.isDirty)
            {
                Debug.LogError("[HQPreview] 当前场景存在未保存改动：为避免丢弃内存改动，Preview 已中止。" +
                               "请先保存或还原改动后重试。");
                return 1;
            }

            float detailObjectDensity = ResolveProductionDetailObjectDensity(out bool densityFromScene);

            TerrainData clone = null;
            Scene tempScene = default;
            int failures;
            try
            {
                // (3) 克隆生产 TerrainData（只读源；后续所有写入只落在克隆体上）
                clone = UnityEngine.Object.Instantiate(production);

                // (4) 临时 Single 空场景 + 临时 Terrain
                tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject terrainObject = Terrain.CreateTerrainGameObject(clone);
                terrainObject.name = "HQ Preview Terrain";
                Terrain terrain = terrainObject.GetComponent<Terrain>();

                // (5) 用生产代码路径播种（只作用于克隆体与临时场景）
                UnderstorySeeder.PreviewScale = PreviewScaleFactor;
                UnderstorySeeder.Seed(clone, terrain.transform);

                // (6) 中央 12 岩石：复用生产放置路径（同名 root / 同名实例 / 同随机流）
                var propsRoot = new GameObject("Environment Props");
                JurassicWorldSceneBuilder.ScatterCentralRocksForPreview(
                    propsRoot, clone, terrain.transform, UnderstoryAssetLocator.FindRockPrefabs());

                // (7) 采集 + 报告
                failures = Report(clone, detailObjectDensity, densityFromScene);
            }
            finally
            {
                UnderstorySeeder.PreviewScale = 1;
                if (tempScene.IsValid())
                    EditorSceneManager.CloseScene(tempScene, true);
                if (clone != null)
                    UnityEngine.Object.DestroyImmediate(clone);
                if (!string.IsNullOrEmpty(previousScenePath))
                    EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }

            return failures;
        }

        /// <summary>生产 detailObjectDensity：优先读生产场景 Terrain，批处理下退回默认值。</summary>
        private static float ResolveProductionDetailObjectDensity(out bool fromScene)
        {
            foreach (Terrain terrain in UnityEngine.Object.FindObjectsOfType<Terrain>())
            {
                if (terrain.terrainData != null && terrain.terrainData.name.StartsWith("JurassicTerrainData", StringComparison.Ordinal))
                {
                    fromScene = true;
                    return terrain.detailObjectDensity;
                }
            }
            fromScene = false;
            return DefaultDetailObjectDensity;
        }

        // ================================================================ 报告

        private static int Report(TerrainData clone, float detailObjectDensity, bool densityFromScene)
        {
            int failures = 0;
            var sb = new StringBuilder();
            sb.AppendLine("[HQPreview] ==================== HQ Asset Pass Preview 报告 ====================");
            sb.AppendLine($"[HQPreview] PreviewScale={PreviewScaleFactor}（仅按确定性前缀缩小实例数；" +
                          "分布常量与随机流未改动）。生产 TerrainData / Main.unity 全程只读" +
                          "（未 SaveAssets / 未 SaveScene / 未 SetDirty）。");
            sb.AppendLine($"[HQPreview] detailObjectDensity={detailObjectDensity:0.##}" +
                          $"（{(densityFromScene ? "取自生产场景 Terrain" : "批处理兜底值")}）——" +
                          "Fern 的「有效实例数」= Σdensity × 该乘数（渲染期概率）。");

            failures += ReportFern(sb, clone, detailObjectDensity);
            failures += ReportFernCutout(sb);
            failures += ReportSceneEntities(sb);
            failures += ReportCentralRocks(sb);
            failures += ReportGatePrefabAssets(sb);
            failures += ReportMissingMaterials(sb);

            Debug.Log(sb.ToString());
            return failures;
        }

        // ---------------------------------------------------------------- Fern（TerrainData 侧）

        private static int ReportFern(StringBuilder sb, TerrainData clone, float detailObjectDensity)
        {
            int failures = 0;
            sb.AppendLine("[HQPreview] ---- Fern（位于 TerrainData 的 DetailPrototype / density layers；" +
                          "Fern【不属于】Understory Pass GameObject 子树）----");

            int res = clone.detailResolution;
            DetailPrototype[] protos = clone.detailPrototypes ?? Array.Empty<DetailPrototype>();
            int layerCount = 0;
            long total = 0;
            var sums = new List<long>();

            for (int i = 0; i < protos.Length; i++)
            {
                if (!IsFernPrototype(protos[i]))
                    continue;

                int[,] layer = clone.GetDetailLayer(0, 0, res, res, i);
                long sum = 0;
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    sum += layer[x, z];

                layerCount++;
                total += sum;
                sums.Add(sum);

                GameObject prefab = protos[i].prototype;
                sb.AppendLine($"[HQPreview]   layer {i}  {prefab.name}  Σdensity={sum}  " +
                              $"≈{sum * detailObjectDensity:0} 实例  " +
                              $"protoMesh={protos[i].usePrototypeMesh}  renderMode={protos[i].renderMode}");
                sb.AppendLine($"[HQPreview]      {DescribeInstanceSource(prefab)}");
            }

            sb.AppendLine($"[HQPreview]   fern 层数={layerCount}（期望 4）; 逐层 Σdensity=[{string.Join(",", sums)}]; " +
                          $"四层合计={total}");

            if (layerCount == 0)
            {
                failures++;
                sb.AppendLine("[HQPreview]   FAIL: fern 层数为 0 —— 资产门控未生效（检查 Assets/Environment/Vegetation/Fern02/Prepared/Prefabs）。");
            }
            else
            {
                long target = UnderstorySeeder.LastFernTotalTarget;
                long assigned = UnderstorySeeder.LastFernAssigned;
                sb.AppendLine($"[HQPreview]   fern 密度守恒：总目标 Σ={target}，各 variant 合计 Σ={assigned}，" +
                              $"variant 数={UnderstorySeeder.LastFernVariantCount}（期望 {layerCount}）");
                if (target != assigned || UnderstorySeeder.LastFernVariantCount != layerCount)
                {
                    failures++;
                    sb.AppendLine("[HQPreview]   FAIL: fern 密度不守恒（每个 cell 应满足 " +
                                  "densityA+…+densityD == target）。");
                }

                // 若四层是同一份完整密度的拷贝（旧 ×4 行为），各层 Σdensity 会几乎完全相同。
                if (sums.Count > 1 && sums.Distinct().Count() != sums.Count)
                {
                    failures++;
                    sb.AppendLine("[HQPreview]   FAIL: 存在 Σdensity 完全相同的 fern 层 —— " +
                                  "疑似回退为「每层写一份完整目标密度」（总密度被 ×N）。");
                }
            }

            if (layerCount > 0 && total > 0)
            {
                float perCell = (float)total / (res * (long)res);
                sb.AppendLine($"[HQPreview]   fern 平均密度={perCell:0.0000}/cell（detailResolution={res}，" +
                              $"即每格 ≈{200f / res:0.00}m）。若该值明显偏离设计意图，请据此评估 " +
                              "FernClusterCount / FernPerClusterMax（本次【不擅自修改】这两个分布常量）。");
            }

            return failures;
        }

        // ---------------------------------------------------------------- Fern Cutout 质量

        private static int ReportFernCutout(StringBuilder sb)
        {
            int failures = 0;
            sb.AppendLine("[HQPreview] ---- Fern Alpha Cutout 检查 ----");

            if (PolyHavenAssetIntegration.ProbeFernAlphaMask(out PolyHavenAssetIntegration.MaskProbe probe))
            {
                sb.AppendLine($"[HQPreview]   源 alpha mask 探查：A∈[{probe.AlphaMin:F3},{probe.AlphaMax:F3}]，" +
                              $"RGB∈[{probe.RgbMin:F3},{probe.RgbMax:F3}]，采样 {probe.Samples} 点 → 判定 mask 位于 " +
                              (probe.AlphaMax - probe.AlphaMin > 0.02f ? "A 通道" : "RGB 灰度"));
            }
            else
            {
                sb.AppendLine("[HQPreview]   WARN: 无法探查源 alpha mask。");
            }

            Material fernMat = AssetDatabase.LoadAssetAtPath<Material>(
                PolyHavenAssetIntegration.FernMaterialPath);
            float cutoff = fernMat != null ? fernMat.GetFloat("_Cutoff") : 0.35f;

            if (PolyHavenAssetIntegration.AnalyzeCutoutMask(cutoff,
                    out float aMin, out float aMax, out float transparent, out float darkOpaque, out int samples))
            {
                sb.AppendLine($"[HQPreview]   生成 AlbedoCutout：A∈[{aMin:F3},{aMax:F3}]，采样 {samples} 点；" +
                              $"_Cutoff={cutoff:0.##}（material 属性，非 shader 硬编码）；" +
                              $"透明像素占比={transparent:P2}; 不透明但亮度<0.25（深色叶片）占比={darkOpaque:P2}");
                sb.AppendLine("[HQPreview]   → 深色叶片占比 > 0 即为「必须用真实 alpha mask、不得用 diffuse 亮度裁切」的证据");

                if (transparent < CutoutTransparentMin)
                {
                    failures++;
                    sb.AppendLine($"[HQPreview]   FAIL: 透明像素占比 {transparent:P2} < {CutoutTransparentMin:P0} —— " +
                                  "几乎未发生裁切，叶片会出现黑色矩形背景。");
                }
                else if (transparent > CutoutTransparentMax)
                {
                    failures++;
                    sb.AppendLine($"[HQPreview]   FAIL: 透明像素占比 {transparent:P2} > {CutoutTransparentMax:P0} —— " +
                                  "叶缘可能大面积缺失。");
                }
                else
                {
                    sb.AppendLine("[HQPreview]   OK: 透明占比落在合理区间，无黑色矩形背景 / 无大面积叶缘缺失迹象。");
                }
            }
            else
            {
                failures++;
                sb.AppendLine("[HQPreview]   FAIL: 找不到生成的 Fern02_AlbedoCutout.png。");
            }

            return failures;
        }

        // ---------------------------------------------------------------- Rock / Deadwood（场景 GameObject 侧）

        private static int ReportSceneEntities(StringBuilder sb)
        {
            int failures = 0;
            sb.AppendLine("[HQPreview] ---- Rock / Deadwood（Understory Pass 子树下的 GameObject）----");

            GameObject root = GameObject.Find(UnderstorySeeder.RootNodeName);
            if (root == null)
            {
                failures++;
                sb.AppendLine("[HQPreview]   FAIL: 临时场景中找不到 \"Understory Pass\" 根节点。");
                return failures;
            }

            failures += ReportChildGroup(sb, root.transform, "Rocks");
            failures += ReportChildGroup(sb, root.transform, "Deadwood");
            return failures;
        }

        private static int ReportChildGroup(StringBuilder sb, Transform understoryRoot, string childName)
        {
            int failures = 0;
            Transform parent = understoryRoot.Find(childName);
            if (parent == null)
            {
                failures++;
                sb.AppendLine($"[HQPreview]   FAIL: 缺少子节点 {childName}。");
                return failures;
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var samples = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            for (int i = 0; i < parent.childCount; i++)
            {
                GameObject child = parent.GetChild(i).gameObject;
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child);
                string key = source != null ? source.name : child.name;
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                if (source != null)
                    samples[key] = source;
            }

            sb.AppendLine($"[HQPreview]   {childName}: 总实例 {parent.childCount} 个 / {counts.Count} 种 prefab");
            foreach (KeyValuePair<string, int> entry in counts.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"[HQPreview]     {entry.Key} × {entry.Value}");
                if (samples.TryGetValue(entry.Key, out GameObject prefab))
                    sb.AppendLine($"[HQPreview]        {DescribeInstanceSource(prefab)}");
            }

            if (parent.childCount == 0)
            {
                failures++;
                sb.AppendLine($"[HQPreview]   FAIL: {childName} 实例数为 0 —— 资产门控未生效" +
                              "（deadwood 需注意 prefab 名不得含 exclude 关键词 \"tree\"）。");
            }

            return failures;
        }

        // ---------------------------------------------------------------- 中央 12 岩石 footprint

        private static int ReportCentralRocks(StringBuilder sb)
        {
            sb.AppendLine("[HQPreview] ---- 中央 12 个 Rock_Large_*（Environment Props；只换资产不换布局）----");
            List<JurassicWorldSceneBuilder.CentralRockFootprint> records =
                JurassicWorldSceneBuilder.LastCentralRockFootprints;

            if (records.Count == 0)
            {
                sb.AppendLine("[HQPreview]   WARN: 无 footprint 记录（可能退回了程序化 Sphere 占位）。");
                return 0;
            }

            int outOfBand = 0;
            foreach (JurassicWorldSceneBuilder.CentralRockFootprint record in records)
            {
                float ratio = record.TargetFootprint > 0.0001f
                    ? record.ActualFootprint / record.TargetFootprint
                    : 0f;
                bool bad = Mathf.Abs(ratio - 1f) > FootprintToleranceRatio;
                if (bad)
                    outOfBand++;
                sb.AppendLine($"[HQPreview]   {record.Name}: 原目标 footprint={record.TargetFootprint:0.00}m，" +
                              $"HQ 实际 footprint={record.ActualFootprint:0.00}m，ratio={ratio:0.000}" +
                              (bad ? "  ← 超出 ±25%" : ""));
            }

            sb.AppendLine($"[HQPreview]   共 {records.Count} 个（期望 12），footprint 超差 {outOfBand} 个" +
                          "（WARN 级：DinosaurSpawnRandomizer 的避让圆由该 bounds 推导，超差会轻微改变开局避让行为）。");
            return 0; // footprint 超差只告警，不计入 FAIL
        }

        // ---------------------------------------------------------------- 门控目录 prefab 资产统计

        private static int ReportGatePrefabAssets(StringBuilder sb)
        {
            int failures = 0;
            sb.AppendLine("[HQPreview] ---- 门控目录 prefab 资产统计（材质 / 纹理分辨率 / 三角面数 / 实测 bounds）----");

            var folders = new (string Path, string Label)[]
            {
                (UnderstoryAssetLocator.FernFolder, "Fern"),
                (UnderstoryAssetLocator.RockFolder, "Rock"),
                (UnderstoryAssetLocator.DeadwoodFolder, "Deadwood")
            };

            foreach ((string path, string label) in folders)
            {
                if (!AssetDatabase.IsValidFolder(path))
                {
                    failures++;
                    sb.AppendLine($"[HQPreview]   FAIL: {label} 门控目录不存在：{path}");
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { path });
                var prefabs = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToArray();

                sb.AppendLine($"[HQPreview]   {label}（{path}）：{prefabs.Length} 个 prefab");
                foreach (string prefabPath in prefabs)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null)
                        continue;
                    sb.AppendLine($"[HQPreview]     {prefab.name}  {DescribeInstanceSource(prefab)}");
                }

                if (prefabs.Length == 0)
                {
                    failures++;
                    sb.AppendLine($"[HQPreview]   FAIL: {label} 目录下无 prefab（门控将 no-op）。");
                }
            }

            return failures;
        }

        // ---------------------------------------------------------------- missing material / shader

        private static int ReportMissingMaterials(StringBuilder sb)
        {
            int failures = 0;
            sb.AppendLine("[HQPreview] ---- missing material / shader 扫描 ----");

            var offenders = new List<string>();
            int checkedCount = 0;

            GameObject root = GameObject.Find(UnderstorySeeder.RootNodeName);
            if (root != null)
                CheckRenderers(root, offenders, ref checkedCount);

            GameObject props = GameObject.Find("Environment Props");
            if (props != null)
                CheckRenderers(props, offenders, ref checkedCount);

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
                    if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        continue;
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                        CheckRenderers(prefab, offenders, ref checkedCount);
                }
            }

            sb.AppendLine($"[HQPreview]   已检查 Renderer {checkedCount} 个；缺失/不支持 material 或 shader 的 {offenders.Count} 个");
            foreach (string offender in offenders)
                sb.AppendLine($"[HQPreview]     FAIL: {offender}");

            failures += offenders.Count;
            if (offenders.Count == 0)
                sb.AppendLine("[HQPreview]   OK: 无 missing material / shader。");
            return failures;
        }

        private static void CheckRenderers(GameObject root, List<string> offenders, ref int checkedCount)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                checkedCount++;
                Material material = renderer.sharedMaterial;
                if (material == null)
                {
                    offenders.Add($"{HierarchyPath(renderer)}: sharedMaterial == null");
                    continue;
                }
                Shader shader = material.shader;
                if (shader == null)
                    offenders.Add($"{HierarchyPath(renderer)}: material \"{material.name}\" 的 shader == null");
                else if (!shader.isSupported)
                    offenders.Add($"{HierarchyPath(renderer)}: shader \"{shader.name}\" isSupported == false");
                else if (shader.name == "Hidden/InternalErrorShader")
                    offenders.Add($"{HierarchyPath(renderer)}: shader 为 Hidden/InternalErrorShader（编译失败）");
            }
        }

        // ================================================================ 描述辅助

        /// <summary>单行描述一个 prefab/实例的：材质、shader、纹理分辨率、三角面数、实测真实世界 bounds。</summary>
        private static string DescribeInstanceSource(GameObject source)
        {
            LODGroup lodGroup = source.GetComponent<LODGroup>();
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                var parts = new List<string>();
                foreach (LOD lod in lods)
                    parts.Add($"{lod.renderers.Length}×{TriangleCount(lod.renderers)}tris");
                Renderer first = lods.Length > 0 && lods[0].renderers.Length > 0 ? lods[0].renderers[0] : null;
                return $"LODGroup {lods.Length} 级 [{string.Join(", ", parts)}]; " +
                       $"transition=[{string.Join("/", lods.Select(l => l.screenRelativeTransitionHeight.ToString("0.###")))}]; " +
                       MaterialInfo(first) + " " + BoundsInfo(source);
            }

            Renderer renderer = source.GetComponentInChildren<Renderer>(true);
            return $"tris={TriangleCount(renderer)}; {MaterialInfo(renderer)} {BoundsInfo(source)}";
        }

        private static int TriangleCount(Renderer[] renderers)
        {
            int total = 0;
            foreach (Renderer renderer in renderers)
            {
                var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                if (filter != null && filter.sharedMesh != null)
                    total += filter.sharedMesh.triangles.Length / 3;
            }
            return total;
        }

        private static int TriangleCount(Renderer renderer)
        {
            if (renderer == null)
                return 0;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null ? filter.sharedMesh.triangles.Length / 3 : 0;
        }

        private static string MaterialInfo(Renderer renderer)
        {
            if (renderer == null)
                return "material=<none>";
            Material material = renderer.sharedMaterial;
            if (material == null)
                return "material=<MISSING>";

            return $"material={material.name}(shader={material.shader.name}, " +
                   $"mainTex={TextureInfo(material, "_MainTex")}, normal={TextureInfo(material, "_BumpMap")}, " +
                   $"smooth={TextureInfo(material, "_MetallicGlossMap")})";
        }

        private static string TextureInfo(Material material, string property)
        {
            Texture texture = material.GetTexture(property);
            if (texture == null)
                return "null";
            return $"{texture.width}×{texture.height}/{texture.graphicsFormat}";
        }

        /// <summary>实测真实世界 bounds（用于尺度审计：确认未被随意放大）。</summary>
        private static string BoundsInfo(GameObject source)
        {
            Renderer renderer = source.GetComponentInChildren<Renderer>(true);
            MeshFilter filter = source.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null)
                return "bounds=<none>";
            Bounds b = filter.sharedMesh.bounds;
            return $"bounds={b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00}m";
        }

        private static bool IsFernPrototype(DetailPrototype prototype)
        {
            if (prototype == null || !prototype.usePrototypeMesh || prototype.prototype == null)
                return false;
            string path = AssetDatabase.GetAssetPath(prototype.prototype);
            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith(UnderstoryAssetLocator.FernFolder + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string HierarchyPath(Component component)
        {
            var chain = new List<string>();
            Transform current = component.transform;
            while (current != null)
            {
                chain.Add(current.name);
                current = current.parent;
            }
            chain.Reverse();
            return string.Join("/", chain);
        }
    }
}
