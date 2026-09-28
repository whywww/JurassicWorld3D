using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// Environment Spatial Rebalance Pass 的「Bake + 影响对账 + 幂等复验」机器验证入口（可重复执行）。
    ///
    /// 【为什么需要它】本 Pass 的交付物包含一组**可证伪的预测**（见计划 §四「预计影响区域」）：
    ///  - heightmap / 树实例 / grass 层 / 中大型实体位置 —— 预测 **0 变化**；
    ///  - bush 层 —— 预测仅在 sd &lt; WorldBounds.NearForestOuter 处变化；
    ///  - fern 层 —— 预测仅在内侧带（含既有林缘簇外溢）变化；
    ///  - R0 空地 —— 前后都必须是 0（禁止域不变）。
    /// 单纯「跑一次 Bake 没报错」无法验证这些断言，故本工具在【同一进程内】先快照、再 Bake、再快照、逐项对账。
    ///
    /// 【幂等复验】连续两次 Bake 后比对 JurassicTerrainData.asset 的 MD5（Main.unity 的 fileID 重排属正常，
    /// 不作为判据 —— 与 ProductionAcceptance 的判定口径一致）。
    ///
    /// 【与其它工具的分工】本工具会**修改**场景与 TerrainData（等价于菜单 Bake Scene）；
    /// 只读采集请用 ProductionAcceptance / UnderstoryDensityCheck。
    ///
    /// 菜单：Jurassic World/Environment Spatial Rebalance/Bake + 影响对账
    /// 批处理：-executeMethod JurassicWorld.Editor.SpatialRebalanceBakeVerify.Run
    /// </summary>
    internal static class SpatialRebalanceBakeVerify
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string TerrainDataPath = "Assets/Terrain/JurassicTerrainData.asset";
        private const string OutputPath = "Logs/spatial_rebalance/accounting.txt";
        private const float SdTolerance = 0.05f;

        [MenuItem("Jurassic World/Environment Spatial Rebalance/Bake + 影响对账", false, 40)]
        public static void MenuRun() => Run();

        public static void Run()
        {
            var report = new StringBuilder();
            var failures = new List<string>();
            var sb = report;

            sb.AppendLine("[SpatialRebalance] ========== Bake + 影响对账开始 ==========");
            sb.AppendLine($"[SpatialRebalance] 四边界：{WorldBounds.Describe()}");

            // ---- 1. 快照（Bake 前）----
            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Snapshot before = Capture(scene, "Bake 前");
            if (before == null)
            {
                Fail(failures, "Bake 前无法采集快照（Terrain / TerrainData 缺失？）。");
                Finish(report, failures);
                return;
            }

            // ---- 2. Bake #1 ----
            JurassicProjectSetup.BakeScene();
            scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Snapshot after = Capture(scene, "Bake 后");
            if (after == null)
            {
                Fail(failures, "Bake 后无法采集快照。");
                Finish(report, failures);
                return;
            }

            // ---- 3. 逐项对账 ----
            Compare(sb, failures, before, after);

            // ---- 4. 幂等复验（Bake #2）----
            string hash1 = after.terrainDataMd5;
            JurassicProjectSetup.BakeScene();
            string hash2 = Md5File(TerrainDataPath);
            sb.AppendLine();
            sb.AppendLine($"[SpatialRebalance] 幂等复验：Bake#1 后 TerrainData MD5 = {hash1}");
            sb.AppendLine($"[SpatialRebalance] 幂等复验：Bake#2 后 TerrainData MD5 = {hash2}");
            if (!string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase))
                Fail(failures, "连续两次 Bake 的 TerrainData 字节哈希不一致（幂等性破坏）。");
            else
                sb.AppendLine("[SpatialRebalance] 幂等复验：PASS（两次 Bake 字节级一致）。");

            // ---- 5. 编辑态断言（ValidateProject）----
            try
            {
                JurassicProjectSetup.ValidateProject();
                sb.AppendLine("[SpatialRebalance] ValidateProject：PASS。");
            }
            catch (Exception exception)
            {
                Fail(failures, "ValidateProject 抛异常：" + exception.Message);
            }

            Finish(report, failures);
        }

        // ================================================================ 快照

        private sealed class Snapshot
        {
            public int heightRes;
            public float[,] heights;
            public int treeCount;
            public long treeDigest;
            public string[] layerKind;      // 每层类型：bush / fern / grass / other
            public List<int[,]> layers = new List<int[,]>();
            public int detailRes;
            public int entityCount;
            public long entityDigest;
            public string terrainDataMd5;
        }

        private static Snapshot Capture(Scene scene, string label)
        {
            Terrain terrain = FindTerrain(scene);
            if (terrain == null || terrain.terrainData == null)
                return null;

            TerrainData data = terrain.terrainData;
            var snap = new Snapshot
            {
                heightRes = data.heightmapResolution,
                detailRes = data.detailResolution,
                treeCount = data.treeInstanceCount,
                terrainDataMd5 = Md5File(TerrainDataPath),
            };
            snap.heights = data.GetHeights(0, 0, snap.heightRes, snap.heightRes);

            long treeHash = 17;
            foreach (TreeInstance instance in data.treeInstances)
            {
                treeHash = Mix(treeHash, instance.prototypeIndex);
                treeHash = Mix(treeHash, instance.position.x * 1000f);
                treeHash = Mix(treeHash, instance.position.z * 1000f);
                treeHash = Mix(treeHash, instance.widthScale);
                treeHash = Mix(treeHash, instance.heightScale);
                treeHash = Mix(treeHash, instance.rotation);
            }
            snap.treeDigest = treeHash;

            // detail 层：按签名分类（bush / fern / grass / other），逐层取整数值
            DetailPrototype[] protos = data.detailPrototypes ?? Array.Empty<DetailPrototype>();
            var bushLayers = new HashSet<int>(UnderstorySeeder.FindBushDetailLayerIndices(data));
            var fernLayers = new HashSet<int>(UnderstorySeeder.FindFernDetailLayerIndices(data));
            Texture2D grass01 = NatureStarterKitLocator.FindGroundTexture("grass01");
            snap.layerKind = new string[protos.Length];
            for (int i = 0; i < protos.Length; i++)
            {
                if (bushLayers.Contains(i)) snap.layerKind[i] = "bush";
                else if (fernLayers.Contains(i)) snap.layerKind[i] = "fern";
                else if (grass01 != null && protos[i] != null && protos[i].prototypeTexture == grass01) snap.layerKind[i] = "grass";
                else snap.layerKind[i] = "other";
                snap.layers.Add(data.GetDetailLayer(0, 0, snap.detailRes, snap.detailRes, i));
            }

            // 实体位置摘要（Understory Pass 根下全部 Transform：名称 + 位置，量化到 1e-4 米）
            long entityHash = 17;
            int entityCount = 0;
            GameObject root = FindRoot(scene, UnderstorySeeder.RootNodeName);
            if (root != null)
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    entityCount++;
                    entityHash = Mix(entityHash, transform.name.GetHashCode());
                    entityHash = Mix(entityHash, transform.position.x * 10000f);
                    entityHash = Mix(entityHash, transform.position.z * 10000f);
                }
            }
            snap.entityCount = entityCount;
            snap.entityDigest = entityHash;

            Debug.Log($"[SpatialRebalance] 快照（{label}）：heightmap {snap.heightRes}²、树 {snap.treeCount} 棵、detail {snap.detailRes}²×{protos.Length} 层、" +
                      $"实体 {snap.entityCount} 个、TerrainData {snap.terrainDataMd5.Substring(0, 8)}。");
            return snap;
        }

        // ================================================================ 对账

        private static void Compare(StringBuilder sb, List<string> failures, Snapshot before, Snapshot after)
        {
            sb.AppendLine();
            sb.AppendLine("[SpatialRebalance] ---------- 影响对账（预测 vs 实测） ----------");

            // (1) heightmap：预测 0 变化
            int heightChanged = 0;
            float heightMaxDelta = 0f;
            for (int z = 0; z < before.heightRes; z++)
            for (int x = 0; x < before.heightRes; x++)
            {
                float delta = Mathf.Abs(before.heights[z, x] - after.heights[z, x]);
                if (delta > 0f)
                    heightChanged++;
                if (delta > heightMaxDelta)
                    heightMaxDelta = delta;
            }
            Predict(sb, failures, "heightmap 逐格不变", heightChanged == 0,
                $"变化 {heightChanged} 格，最大 |Δ| = {heightMaxDelta:E2}");

            // (2) 树实例：预测 0 变化
            Predict(sb, failures, "树实例（数量 + 位置摘要）不变",
                before.treeCount == after.treeCount && before.treeDigest == after.treeDigest,
                $"树 {before.treeCount} → {after.treeCount}，摘要 {(before.treeDigest == after.treeDigest ? "一致" : "不一致")}");

            // (3) 实体位置：预测 0 变化（数值阈值与 RNG 消费序均未变）
            Predict(sb, failures, "Understory 实体（数量 + 位置摘要）不变",
                before.entityCount == after.entityCount && before.entityDigest == after.entityDigest,
                $"实体 {before.entityCount} → {after.entityCount}，摘要 {(before.entityDigest == after.entityDigest ? "一致" : "不一致")}");

            // (4) 逐层对账
            long bushChanged = 0, fernChanged = 0, grassChanged = 0, otherChanged = 0;
            float bushChangedMaxSd = float.MinValue, fernChangedMaxSd = float.MinValue;
            int clearingViolations = 0;
            long bushNonZeroBefore = 0, bushNonZeroAfter = 0, fernNonZeroBefore = 0, fernNonZeroAfter = 0;

            for (int i = 0; i < after.layers.Count; i++)
            {
                string kind = after.layerKind[i];
                int[,] b = before.layers[i];
                int[,] a = after.layers[i];
                for (int z = 0; z < after.detailRes; z++)
                for (int x = 0; x < after.detailRes; x++)
                {
                    float wx = (float)x / after.detailRes * WorldBounds.TerrainSize - WorldBounds.TerrainSize * 0.5f;
                    float wz = (float)z / after.detailRes * WorldBounds.TerrainSize - WorldBounds.TerrainSize * 0.5f;
                    float sd = WorldBounds.SignedDistanceToDinosaurActivity(wx, wz);

                    if (kind == "bush")
                    {
                        if (b[x, z] != 0) bushNonZeroBefore++;
                        if (a[x, z] != 0) bushNonZeroAfter++;
                    }
                    else if (kind == "fern")
                    {
                        if (b[x, z] != 0) fernNonZeroBefore++;
                        if (a[x, z] != 0) fernNonZeroAfter++;
                    }

                    // R0 空地：前后都必须为 0（低矮植被的唯一禁止域）
                    if (sd <= -WorldBounds.ClearingInset + SdTolerance &&
                        (kind == "bush" || kind == "fern") && a[x, z] != 0)
                        clearingViolations++;

                    if (b[x, z] == a[x, z])
                        continue;

                    switch (kind)
                    {
                        case "bush":
                            bushChanged++;
                            if (sd > bushChangedMaxSd) bushChangedMaxSd = sd;
                            break;
                        case "fern":
                            fernChanged++;
                            if (sd > fernChangedMaxSd) fernChangedMaxSd = sd;
                            break;
                        case "grass":
                            grassChanged++;
                            break;
                        default:
                            otherChanged++;
                            break;
                    }
                }
            }

            Predict(sb, failures, "grass 层逐格不变（无符号距离保留）",
                grassChanged == 0, $"变化 {grassChanged} 格");
            // 上界断言：变化不得越出梯度带（幂等重跑时变化为 0 格，属正确结果——梯度已生效）。
            Predict(sb, failures, "bush 层变化不越出 sd < NearForestOuter",
                bushChanged == 0 || bushChangedMaxSd < WorldBounds.NearForestOuter + SdTolerance,
                $"变化 {bushChanged} 格，变化格最大 sd = {SdText(bushChangedMaxSd)}（上限 {WorldBounds.NearForestOuter:F1}m）");
            Predict(sb, failures, "fern 层变化局限在内侧带（含既有林缘簇外溢）",
                fernChanged == 0 || fernChangedMaxSd < WorldBounds.PlayerTransitionOuter + WorldBounds.NearForestOuter * 0.5f,
                $"变化 {fernChanged} 格，变化格最大 sd = {SdText(fernChangedMaxSd)}");
            Predict(sb, failures, "fern/bush 在 R0 空地内恒 0",
                clearingViolations == 0, $"空地内非零格 {clearingViolations} 个");
            Predict(sb, failures, "其余 detail 层不变",
                otherChanged == 0, $"变化 {otherChanged} 格");

            sb.AppendLine($"[SpatialRebalance] 非零格统计：bush {bushNonZeroBefore} → {bushNonZeroAfter}（{Signed(bushNonZeroAfter - bushNonZeroBefore)}），" +
                          $"fern {fernNonZeroBefore} → {fernNonZeroAfter}（{Signed(fernNonZeroAfter - fernNonZeroBefore)}）。");

            // (5) 视觉梯度确实落在 R1/R2（逐环「绝对」非零格，跨「首次转入 / 幂等重跑」都成立）
            long[] rings = AppendRingStats(sb, before, after);
            Predict(sb, failures, "梯度带 R1（活动包络外缘）与 R2（玩家过渡带）均有低矮植被",
                rings[1] > 0 && rings[2] > 0, $"R1 {rings[1]} 格 / R2 {rings[2]} 格（非零）");
        }

        private static string SdText(float sd) => sd == float.MinValue ? "N/A（本次无变化格）" : sd.ToString("F2") + "m";
        private static string Signed(long value) => value >= 0 ? "+" + value : value.ToString();

        /// <summary>逐环（以到活动包络的带符号距离分环）统计 bush/fern 非零格，返回各环「之后」的非零格数。</summary>
        private static long[] AppendRingStats(StringBuilder sb, Snapshot before, Snapshot after)
        {
            float[] bounds =
            {
                -150f, -WorldBounds.ClearingInset, 0f, WorldBounds.PlayerTransitionOuter,
                WorldBounds.NearForestOuter, 45f, 150f
            };
            string[] names = { "R0 空地", "R1 外缘", "R2 过渡带", "R3 林缘内", "R4 密林", "R5 远林" };
            var afterCounts = new long[bounds.Length - 1];
            sb.AppendLine("[SpatialRebalance] 逐环非零格（bush+fern 合计，前 → 后）：");
            for (int ring = 0; ring < bounds.Length - 1; ring++)
            {
                long beforeCount = 0, afterCount = 0;
                for (int i = 0; i < after.layers.Count; i++)
                {
                    string kind = after.layerKind[i];
                    if (kind != "bush" && kind != "fern") continue;
                    int[,] b = before.layers[i];
                    int[,] a = after.layers[i];
                    for (int z = 0; z < after.detailRes; z++)
                    for (int x = 0; x < after.detailRes; x++)
                    {
                        float wx = (float)x / after.detailRes * WorldBounds.TerrainSize - WorldBounds.TerrainSize * 0.5f;
                        float wz = (float)z / after.detailRes * WorldBounds.TerrainSize - WorldBounds.TerrainSize * 0.5f;
                        float sd = WorldBounds.SignedDistanceToDinosaurActivity(wx, wz);
                        if (sd < bounds[ring] || sd >= bounds[ring + 1]) continue;
                        if (b[x, z] != 0) beforeCount++;
                        if (a[x, z] != 0) afterCount++;
                    }
                }
                afterCounts[ring] = afterCount;
                sb.AppendLine($"  {names[ring],-10} sd ∈ [{bounds[ring],6:F1},{bounds[ring + 1],6:F1}) : {beforeCount,7} → {afterCount,7}");
            }
            return afterCounts;
        }

        // ================================================================ 辅助

        private static void Predict(StringBuilder sb, List<string> failures, string name, bool ok, string detail)
        {
            sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {name} —— {detail}");
            if (!ok)
                failures.Add($"{name}（{detail}）");
        }

        private static void Fail(List<string> failures, string message)
        {
            failures.Add(message);
            Debug.LogError("[SpatialRebalance] " + message);
        }

        private static void Finish(StringBuilder report, List<string> failures)
        {
            report.AppendLine();
            if (failures.Count == 0)
                report.AppendLine("JURASSIC_SPATIAL_REBALANCE_OK: Bake + 影响对账 + 幂等复验全部通过。");
            else
            {
                report.AppendLine($"JURASSIC_SPATIAL_REBALANCE_FAILED: {failures.Count} 项不通过：");
                foreach (string failure in failures)
                    report.AppendLine("  - " + failure);
            }

            string text = report.ToString();
            Debug.Log(text);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath) ?? "Logs");
                File.WriteAllText(OutputPath, text, Encoding.UTF8);
                Debug.Log($"[SpatialRebalance] 对账报告已写入 {OutputPath}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SpatialRebalance] 写入 {OutputPath} 失败：{exception.Message}");
            }
        }

        private static Terrain FindTerrain(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Terrain terrain = root.GetComponentInChildren<Terrain>(true);
                if (terrain != null)
                    return terrain;
            }
            return null;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name)
                    return root;
            return null;
        }

        private static long Mix(long hash, float value) =>
            hash * 31 + BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        private static long Mix(long hash, int value) => hash * 31 + value;

        private static string Md5File(string path)
        {
            if (!File.Exists(path))
                return "<missing>";
            using (MD5 md5 = MD5.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
