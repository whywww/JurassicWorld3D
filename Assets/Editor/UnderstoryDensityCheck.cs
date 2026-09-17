using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// Understory 密度分布抽查（诊断工具，等价于「抽查 GetDetailLayer」的自动化版）：
    /// 读取 JurassicTerrainData 的 bush（6 层平均）与 grass01 层，打印 ASCII 密度热图 +
    /// 到平坦矩形距离的分带统计，用于视觉验收前确认分区数据形态。
    ///
    /// 典型期望（默认常量）：dFlat &lt; 7.5 恒 0；[7.5,19.5) 爬升（ramp 12m）；
    /// [19.5,45) 团块带（bush 中高值 + 通道/泥斑减半）；地形边缘密林墙抬高；
    /// 北带 z∈[90,100] 因空间不足（地形边 100）覆盖薄属已知限制。
    ///
    /// 菜单：Jurassic World/Check Understory Density；
    /// 批处理：-executeMethod JurassicWorld.Editor.UnderstoryDensityCheck.Run
    /// </summary>
    public static class UnderstoryDensityCheck
    {
        private const string TerrainDataPath = "Assets/Terrain/JurassicTerrainData.asset";
        private const int MapSize = 48;           // ASCII 图 48×48（每字符 ≈4.2m）

        [MenuItem("Jurassic World/Check Understory Density")]
        public static void Run()
        {
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null)
            {
                Debug.LogError($"[DensityCheck] 找不到 {TerrainDataPath}，请先 Bake Scene。");
                return;
            }

            int res = data.detailResolution;
            int[] bushLayers = UnderstorySeeder.FindBushDetailLayerIndices(data);
            if (bushLayers.Length == 0)
            {
                Debug.LogError("[DensityCheck] 未识别到 bush detail 层（NSk2 未导入？）。");
                return;
            }

            // bush 6 层 → 逐格平均场
            var bushAvg = new float[res, res];
            foreach (int layerIdx in bushLayers)
            {
                int[,] layer = data.GetDetailLayer(0, 0, res, res, layerIdx);
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    bushAvg[x, z] += layer[x, z];
            }
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                bushAvg[x, z] /= bushLayers.Length;

            // grass01 层（与 UnderstorySeeder 同源签名：prototypeTexture == grass01 纹理）
            float[,] grass = null;
            Texture2D grass01 = NatureStarterKitLocator.FindGroundTexture("grass01");
            if (grass01 != null)
            {
                DetailPrototype[] protos = data.detailPrototypes;
                for (int i = 0; i < protos.Length; i++)
                {
                    if (protos[i] == null || protos[i].prototypeTexture != grass01)
                        continue;
                    int[,] gl = data.GetDetailLayer(0, 0, res, res, i);
                    grass = new float[res, res];
                    for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        grass[x, z] = gl[x, z];
                    break;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[DensityCheck] res={res}，bush 层 {bushLayers.Length} 个（下图 = 逐格平均），grass01 {(grass != null ? "已识别" : "未识别")}。");
            sb.AppendLine("行 = z 从 +100(北) 到 -100(南)；列 = x 从 -100(西) 到 +100(东)；每字符 ≈4.2m。");
            sb.AppendLine("bush 平均密度： '.'=0  ':'<0.5  '+'<1.5  '*'<3  '#'>=3");
            AppendMap(sb, bushAvg, res, v => v <= 0f ? '.' : v < 0.5f ? ':' : v < 1.5f ? '+' : v < 3f ? '*' : '#');
            if (grass != null)
            {
                sb.AppendLine("grass01 密度： '.'=0  ':'<1  '+'<2  '*'<3  '#'>=3");
                AppendMap(sb, grass, res, v => v <= 0f ? '.' : v < 1f ? ':' : v < 2f ? '+' : v < 3f ? '*' : '#');
            }

            // 分带统计（到平坦矩形距离；同时统计 bush 与 grass）
            sb.AppendLine("分带统计（到平坦矩形 [-70,70]×[-70,90] 的距离）：");
            float[] bounds = { 0f, 7.5f, 19.5f, 45f, 150f };
            var dFlat = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
                dFlat[x, z] = UnderstorySeeder.DistanceToFlatRectWorld(
                    (float)x / res * 200f - 100f, (float)z / res * 200f - 100f);
            for (int b = 0; b < bounds.Length - 1; b++)
            {
                long count = 0, nonZero = 0;
                double sumBush = 0, sumGrass = 0;
                float maxBush = 0;
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float d = dFlat[x, z];
                    if (d < bounds[b] || d >= bounds[b + 1])
                        continue;
                    count++;
                    sumBush += bushAvg[x, z];
                    if (grass != null) sumGrass += grass[x, z];
                    if (bushAvg[x, z] > 0.5f) nonZero++;
                    if (bushAvg[x, z] > maxBush) maxBush = bushAvg[x, z];
                }
                double avgBush = count > 0 ? sumBush / count : 0;
                double avgGrass = grass != null && count > 0 ? sumGrass / count : 0;
                sb.AppendLine($"  dFlat ∈ [{bounds[b],5},{bounds[b + 1],5}) : bush 平均 {avgBush:F2}（非零格 {100.0 * nonZero / Math.Max(1, count):F0}%，max {maxBush:F1}） / grass 平均 {avgGrass:F2}  [{count} 格]");
            }

            // 关键点抽查
            sb.AppendLine("关键点抽查：");
            AppendPoint(sb, bushAvg, grass, res, 0f, 0f, "活动区中心(0,0)          ");
            AppendPoint(sb, bushAvg, grass, res, 60f, 20f, "东缘(60,20) 向外观测点   ");
            AppendPoint(sb, bushAvg, grass, res, 90f, 20f, "东缘外 30m(90,20)       ");
            AppendPoint(sb, bushAvg, grass, res, 0f, -89f, "南缘外(0,-89)           ");
            AppendPoint(sb, bushAvg, grass, res, 0f, 97f, "北带(0,97) 空间不足     ");

            Debug.Log(sb.ToString());
        }

        private static void AppendMap(StringBuilder sb, float[,] field, int res, Func<float, char> map)
        {
            for (int row = 0; row < MapSize; row++)
            {
                var line = new StringBuilder(MapSize);
                for (int col = 0; col < MapSize; col++)
                {
                    int x = Mathf.Clamp(Mathf.RoundToInt((col + 0.5f) / MapSize * res), 0, res - 1);
                    int z = Mathf.Clamp(Mathf.RoundToInt((1f - (row + 0.5f) / MapSize) * res), 0, res - 1);
                    line.Append(map(field[x, z]));
                }
                sb.AppendLine(line.ToString());
            }
        }

        private static void AppendPoint(StringBuilder sb, float[,] bush, float[,] grass, int res, float wx, float wz, string label)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((wx + 100f) / 200f * res), 0, res - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((wz + 100f) / 200f * res), 0, res - 1);
            string g = grass != null ? $" grass={grass[x, z]:F0}" : "";
            sb.AppendLine($"  {label} bush={bush[x, z]:F2}{g}");
        }
    }
}
