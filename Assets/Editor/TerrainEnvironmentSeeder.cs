using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// 地形环境播种器：heightmap 丘陵重建、4 层 TerrainLayer alphamap、owned detail 草层、岩石散布。
    ///
    /// 设计原则（增量添加 + 单开关回滚）：
    ///  - 通过 EnableTerrainEnvironmentPass 常量一键启停；关闭时 PopulateGround 执行 CleanupOwnedArtifacts 剥离；
    ///  - PrepareTerrain 为纯函数 heightmap 重建（无 RNG），幂等检测后才写入；
    ///  - 岩石使用独立 System.Random(GroundPassRandomSeed)，绝不干扰 ForestSeeder / SceneBuilder 随机流；
    ///  - owned detail prototype 以 grass01 纹理引用做所有权键，追加/移除时对非 owned 层做 snapshot/restore。
    /// </summary>
    internal static class TerrainEnvironmentSeeder
    {
        // ------------------------------------------------------------------ 可调常量块

        // --- 回滚开关 ---
        private const bool EnableTerrainEnvironmentPass = true;
        internal static bool PassEnabled => EnableTerrainEnvironmentPass;

        // --- 种子 ---
        private const int GroundPassRandomSeed = 271832;

        // --- Heightmap ---
        private const float HeightEqualTol = 2e-4f;
        // 平坦矩形归一化边界（internal：UnderstorySeeder 安全缓冲几何换算与校验同源引用）
        internal const float FlatMinX = 0.15f, FlatMaxX = 0.85f;
        internal const float FlatMinZ = 0.15f, FlatMaxZ = 0.95f;
        private const float TransitionWidth = 0.15f;
        private const float HillAmplitude = 0.2f;

        // --- Terrain Layer ---
        private const string TerrainLayerFolder = "Assets/Terrain/Layers";

        // --- Detail (owned grass) ---
        private static readonly Color OwnedHealthyColor = new Color(0.30f, 0.60f, 0.22f);
        private static readonly Color OwnedDryColor = new Color(0.55f, 0.60f, 0.25f);

        // --- 中央活动区泥土斑块（ground02 混入核心区绿草，纯噪声函数无 RNG）---
        // 斑块中心归一化后 ground02 占比 ≈ 24%（w1/(1+w1)，峰值 0.32）；仅核心区（t→0）生效，向过渡带随 (1-t) 衰减。
        private const float CoreMudPatchWeight = 0.32f;    // 斑块峰值权重
        private const float CoreMudPatchFreq = 11f;        // 噪声频率（200m / 11 ≈ 18m 特征尺度）
        private const float CoreMudPatchThreshold = 0.52f; // 噪声阈值：低于此值无斑块
        private const float CoreMudPatchSoftness = 0.18f;  // 阈值软过渡宽度（斑块边缘渐变）
        private const float CoreMudPatchOffsetX = 12.7f, CoreMudPatchOffsetZ = 7.9f; // 独立偏移，避开与 a/b 同源走向

        // --- Rocks ---
        private const string RockRootName = "Ground Pass Rocks";
        private const int RockCount = 24;
        private const int RockAttempts = 400;
        private const string RockMaterialPath = "Assets/Materials/RockNatural.mat";

        // --- Terrain size (must match SceneBuilder) ---
        // （internal：UnderstorySeeder 世界坐标换算同源引用）
        internal const float TerrainSize = 200f;

        // ------------------------------------------------------------------ Phase 1 入口

        /// <summary>
        /// 在 ForestSeeder.Seed() 之前调用：重建 heightmap 丘陵并 snap 已有树木贴地。
        /// 纯函数（无 RNG），幂等检测：heightmap 与现有一致时跳过写入。
        /// </summary>
        internal static void PrepareTerrain(TerrainData data)
        {
            if (!EnableTerrainEnvironmentPass || data == null) return;

            int res = data.heightmapResolution;

            // Build target heightmap (pure function, NO RNG)
            float[,] target = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);
                float nz = (float)z / (res - 1);
                float d = DistanceToFlatRect(nx, nz);
                float t = Smoothstep(Mathf.Clamp01(d / TransitionWidth));
                float n = 0.75f * Mathf.PerlinNoise(nx * 3f + 0.5f, nz * 3f + 0.5f)
                        + 0.25f * Mathf.PerlinNoise(nx * 7f + 1.3f, nz * 7f + 2.7f);
                target[z, x] = HillAmplitude * t * n; // always >= 0
            }

            // Idempotent guard - compare with current
            float[,] current = data.GetHeights(0, 0, res, res);
            int changedCount = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                if (Mathf.Abs(current[z, x] - target[z, x]) > HeightEqualTol)
                    changedCount++;
            }

            if (changedCount == 0)
            {
                Debug.Log("[GroundPass] heightmap 与现有一致，跳过写入");
                return;
            }

            // Apply
            data.SetHeights(0, 0, target);

            // Snap existing trees to new heightmap (ONLY mechanism, no manual Y calculation)
            int snapCount = 0;
            if (data.treeInstanceCount > 0)
            {
                TreeInstance[] existing = data.treeInstances;
                data.SetTreeInstances(existing, true); // native snapToHeightmap
                snapCount = existing.Length;
            }

            Debug.Log($"[GroundPass] heightmap 重建完成：{changedCount}/{res * res} 格变化，snap {snapCount} 棵树贴地。");
        }

        // ------------------------------------------------------------------ Phase 2 入口

        /// <summary>
        /// 在 ForestSeeder.Seed() 之后调用：绘制地面层、owned detail 草层、散布岩石。
        /// 当 EnableTerrainEnvironmentPass=false 时执行回滚清理。
        /// </summary>
        internal static void PopulateGround(TerrainData data, Transform terrainTransform)
        {
            if (data == null) return;

            if (!EnableTerrainEnvironmentPass)
            {
                CleanupOwnedArtifacts(data);
                return;
            }

            PaintGroundLayers(data);
            PaintOwnedDetails(data);
            ScatterRocks(data, terrainTransform);

            Debug.Log($"[GroundPass] PopulateGround 完成：4 layers, owned detail, {RockCount} rocks (seed={GroundPassRandomSeed}).");
        }

        // ------------------------------------------------------------------ 私有辅助：PaintGroundLayers

        private static void PaintGroundLayers(TerrainData data)
        {
            // Ensure folder exists
            if (!AssetDatabase.IsValidFolder(TerrainLayerFolder))
                AssetDatabase.CreateFolder("Assets/Terrain", "Layers");

            // Slot 0: load existing GrassLayer, do NOT modify its fields
            TerrainLayer slot0 = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainLayerFolder + "/GrassLayer.terrainlayer");
            if (slot0 == null)
            {
                Debug.LogWarning("[GroundPass] GrassLayer.terrainlayer 未找到，跳过 alphamap 绘制（EnsureGrassTerrainLayer 可能未先执行）。");
                return;
            }

            // Slots 1-3: load or create
            TerrainLayer slot1 = EnsureTerrainLayer("Ground02Layer", "ground02", new Vector2(6f, 6f), slot0);
            TerrainLayer slot2 = EnsureTerrainLayer("Ground03Layer", "ground03", new Vector2(5f, 5f), slot0);
            TerrainLayer slot3 = EnsureTerrainLayer("DryGrassLayer", "grass02", new Vector2(3f, 3f), slot0);

            TerrainLayer[] targetLayers = new[] { slot0, slot1, slot2, slot3 };

            // Only assign if reference sequence differs
            TerrainLayer[] currentLayers = data.terrainLayers;
            bool same = currentLayers.Length == targetLayers.Length;
            if (same)
                for (int i = 0; i < targetLayers.Length; i++)
                    if (currentLayers[i] != targetLayers[i]) { same = false; break; }

            // Ensure alphamapResolution is valid regardless of layer comparison result
            if (data.alphamapResolution <= 0)
                data.alphamapResolution = 256;

            if (!same)
            {
                data.terrainLayers = targetLayers;
            }

            // Single SetAlphamaps with pure-function weights
            int amW = data.alphamapWidth;
            int amH = data.alphamapHeight;
            float[,,] map = new float[amH, amW, 4];
            for (int y = 0; y < amH; y++)
            for (int x = 0; x < amW; x++)
            {
                float nx = (float)x / Mathf.Max(amW - 1, 1);
                float nz = (float)y / Mathf.Max(amH - 1, 1);
                float d = DistanceToFlatRect(nx, nz);
                float t = Smoothstep(Mathf.Clamp01(d / TransitionWidth));
                float a = Mathf.PerlinNoise(nx * 5f + 3.1f, nz * 5f + 4.7f);
                float b = Mathf.PerlinNoise(nx * 9f + 6.3f, nz * 9f + 8.9f);

                // 中央活动区泥土斑块：中频噪声阈值化生成不规则土斑，纯函数确定性；
                // 仅核心区可见（t=0 时最强），向过渡带按 (1-t) 衰减，与外围既有棕色混合无缝衔接。
                float p = Mathf.PerlinNoise(nx * CoreMudPatchFreq + CoreMudPatchOffsetX,
                                            nz * CoreMudPatchFreq + CoreMudPatchOffsetZ);
                float patch = Smoothstep(Mathf.Clamp01((p - CoreMudPatchThreshold) / CoreMudPatchSoftness));
                float mudPatch = patch * (1f - t) * CoreMudPatchWeight;

                float w0 = 1f - t;
                float w1 = t * (0.5f + 0.3f * a) + mudPatch;
                float w2 = t * (0.3f + 0.3f * b);
                float w3 = t * (0.2f + 0.2f * (1f - a));

                float sum = w0 + w1 + w2 + w3;
                if (sum > 0f) { w0 /= sum; w1 /= sum; w2 /= sum; w3 /= sum; }
                else { w0 = 1f; w1 = w2 = w3 = 0f; }

                map[y, x, 0] = w0;
                map[y, x, 1] = w1;
                map[y, x, 2] = w2;
                map[y, x, 3] = w3;
            }
            data.SetAlphamaps(0, 0, map);

            Debug.Log($"[GroundPass] TerrainLayers 4 层就绪，alphamap {amW}×{amH} 已写入。");
        }

        private static TerrainLayer EnsureTerrainLayer(string assetName, string textureToken, Vector2 tileSize, TerrainLayer fallbackSlot0)
        {
            string path = TerrainLayerFolder + "/" + assetName + ".terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer != null) return layer; // reuse existing, don't modify

            layer = new TerrainLayer();
            layer.tileSize = tileSize;
            layer.tileOffset = Vector2.zero;

            Texture2D tex = NatureStarterKitLocator.FindGroundTexture(textureToken);
            if (tex == null && fallbackSlot0 != null)
                tex = fallbackSlot0.diffuseTexture; // fallback to slot0's diffuse

            if (tex != null)
                layer.diffuseTexture = tex;

            AssetDatabase.CreateAsset(layer, path);
            EditorUtility.SetDirty(layer); // only on creation
            return layer;
        }

        // ------------------------------------------------------------------ 私有辅助：PaintOwnedDetails

        private static void PaintOwnedDetails(TerrainData data)
        {
            // Only set detail resolution if degenerate
            if (data.detailResolution <= 0)
            {
                data.SetDetailResolution(512, 8);
                Debug.LogWarning("[GroundPass] detailResolution<=0，已设置为 512/8。");
            }

            // Find grass01 texture as ownership key
            Texture2D grass01 = NatureStarterKitLocator.FindGroundTexture("grass01");
            if (grass01 == null)
            {
                Debug.LogWarning("[GroundPass] 未找到 grass01 纹理，跳过 owned detail 子职责。");
                return;
            }

            // Find owned prototype index by reference equality on prototype texture
            DetailPrototype[] protos = data.detailPrototypes ?? Array.Empty<DetailPrototype>();
            int ownedIdx = -1;
            for (int i = 0; i < protos.Length; i++)
            {
                if (protos[i].prototypeTexture == grass01)
                { ownedIdx = i; break; }
            }

            // If not found, append once with full snapshot/restore
            if (ownedIdx < 0)
            {
                int res = data.detailResolution;
                int existingCount = protos.Length;

                // Snapshot ALL existing layers BEFORE modifying detailPrototypes
                int[][] snapshotsData = new int[existingCount][];
                for (int i = 0; i < existingCount; i++)
                {
                    int[,] layerData = data.GetDetailLayer(0, 0, res, res, i);
                    // Flatten to 1D for storage
                    int[] flat = new int[res * res];
                    for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        flat[z * res + x] = layerData[x, z];
                    snapshotsData[i] = flat;
                }

                // Append new prototype
                DetailPrototype newProto = new DetailPrototype
                {
                    prototypeTexture = grass01,
                    renderMode = DetailRenderMode.GrassBillboard,
                    healthyColor = OwnedHealthyColor,
                    dryColor = OwnedDryColor,
                    minWidth = 0.5f,
                    maxWidth = 1.2f,
                    minHeight = 0.4f,
                    maxHeight = 1.0f,
                    noiseSpread = 0.2f,
                    usePrototypeMesh = false
                };

                DetailPrototype[] newProtos = new DetailPrototype[existingCount + 1];
                Array.Copy(protos, newProtos, existingCount);
                newProtos[existingCount] = newProto;
                data.detailPrototypes = newProtos; // This setter MAY clear detail data!

                // Guard: detailResolution may shift after prototype array assignment on some Unity versions
                int resAfter = data.detailResolution;
                if (resAfter != res)
                {
                    Debug.LogWarning($"[GroundPass] detailResolution 在 append 后由 {res} 变为 {resAfter}，按新值 restore。");
                    res = resAfter;
                }

                // Restore non-owned layers from snapshot
                for (int i = 0; i < existingCount; i++)
                {
                    int[,] restore = new int[res, res];
                    for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        restore[x, z] = snapshotsData[i][z * res + x];
                    data.SetDetailLayer(0, 0, i, restore);
                }

                ownedIdx = existingCount;
                Debug.Log($"[GroundPass] owned detail prototype 追加于 index={ownedIdx}。");
            }

            // Paint density (pure Perlin function, no RNG)
            int detailRes = data.detailResolution;
            int[,] density = new int[detailRes, detailRes];
            Vector2[] spawnXZ = { new Vector2(0, 11), new Vector2(10, 27), new Vector2(-10, 19), new Vector2(8, 15), new Vector2(-13, 35) };

            for (int z = 0; z < detailRes; z++)
            for (int x = 0; x < detailRes; x++)
            {
                float wx = (float)x / detailRes * TerrainSize - TerrainSize / 2f;
                float wz = (float)z / detailRes * TerrainSize - TerrainSize / 2f;

                float cluster = Mathf.PerlinNoise(wx * 0.08f + 2.2f, wz * 0.08f + 5.5f);
                int baseVal = Mathf.RoundToInt(cluster * 4f); // 0..4

                // Flat core check (normalized distance to flat rect)
                float nx = (wx + TerrainSize / 2f) / TerrainSize;
                float nz = (wz + TerrainSize / 2f) / TerrainSize;
                float d = DistanceToFlatRect(nx, nz);
                float t = Smoothstep(Mathf.Clamp01(d / TransitionWidth));

                int val;
                if (t < 0.02f) // flat core
                    val = Mathf.Clamp(baseVal, 1, 2);
                else
                    val = Mathf.Clamp(baseVal + 1, 0, 5);

                // Halve near spawn points
                foreach (Vector2 sp in spawnXZ)
                {
                    if (Vector2.Distance(sp, new Vector2(wx, wz)) < 20f)
                    { val = Mathf.Max(1, val / 2); break; }
                }

                density[x, z] = val;
            }

            // ONLY write owned layer index, never touch other layers
            data.SetDetailLayer(0, 0, ownedIdx, density);
            Debug.Log($"[GroundPass] owned detail (index={ownedIdx}) 密度写入完成，分辨率 {detailRes}×{detailRes}。");
        }

        // ------------------------------------------------------------------ 私有辅助：ScatterRocks

        private static void ScatterRocks(TerrainData data, Transform terrainTransform)
        {
            // Find or create dedicated root
            GameObject root = GameObject.Find(RockRootName);
            if (root == null)
                root = new GameObject(RockRootName);

            // Destroy only direct children of our root
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            var random = new System.Random(GroundPassRandomSeed);
            Material rockMat = AssetDatabase.LoadAssetAtPath<Material>(RockMaterialPath);

            int placed = 0;
            for (int attempt = 0; attempt < RockAttempts && placed < RockCount; attempt++)
            {
                float wx = (float)(random.NextDouble() * 190.0 - 95.0);
                float wz = (float)(random.NextDouble() * 190.0 - 95.0);

                // Normalized coordinates for terrain queries
                float nx = (wx + TerrainSize / 2f) / TerrainSize;
                float nz = (wz + TerrainSize / 2f) / TerrainSize;

                // Accept condition: distance to flat rect world boundary >= 3m
                float dNorm = DistanceToFlatRect(nx, nz);
                float dWorld = dNorm * TerrainSize; // convert normalized distance to world meters
                if (dWorld < 3f) continue;

                // Read FINAL heightmap for Y
                float y = terrainTransform.position.y + data.GetInterpolatedHeight(nx, nz) + 0.3f;

                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = "GroundRock_" + placed.ToString("D2");
                rock.transform.SetParent(root.transform);
                rock.transform.position = new Vector3(wx, y, wz);
                rock.transform.localScale = new Vector3(
                    2.5f + (float)random.NextDouble() * 2f,
                    0.8f + (float)random.NextDouble() * 0.6f,
                    2.0f + (float)random.NextDouble() * 1.5f);
                rock.transform.rotation = Quaternion.Euler(
                    (float)random.NextDouble() * 15f,
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 10f);

                if (rockMat != null)
                    rock.GetComponent<Renderer>().sharedMaterial = rockMat;

                Collider col = rock.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);

                GameObjectUtility.SetStaticEditorFlags(rock, StaticEditorFlags.BatchingStatic);
                placed++;
            }

            Debug.Log($"[GroundPass] 岩石散布完成：{placed}/{RockCount} 个（种子 {GroundPassRandomSeed}）。");
        }

        // ------------------------------------------------------------------ 私有辅助：CleanupOwnedArtifacts（回滚）

        private static void CleanupOwnedArtifacts(TerrainData data)
        {
            // 1. Destroy rock root and children
            GameObject root = GameObject.Find(RockRootName);
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);

            // 2. Remove owned detail prototype by reference key
            Texture2D grass01 = NatureStarterKitLocator.FindGroundTexture("grass01");
            if (grass01 == null || data == null)
            {
                Debug.Log("[GroundPass] 回滚清理完成（无 owned detail 需移除）。");
                return;
            }

            DetailPrototype[] protos = data.detailPrototypes ?? Array.Empty<DetailPrototype>();
            int ownedIdx = -1;
            for (int i = 0; i < protos.Length; i++)
            {
                if (protos[i].prototypeTexture == grass01)
                { ownedIdx = i; break; }
            }

            if (ownedIdx < 0)
            {
                Debug.Log("[GroundPass] 回滚清理完成（owned detail 不存在）。");
                return;
            }

            // Snapshot non-owned layers
            int res = data.detailResolution;
            int existingCount = protos.Length;
            int[][] snapshots = new int[existingCount][];
            for (int i = 0; i < existingCount; i++)
            {
                if (i == ownedIdx) continue; // skip owned layer
                int[,] layerData = data.GetDetailLayer(0, 0, res, res, i);
                int[] flat = new int[res * res];
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    flat[z * res + x] = layerData[x, z];
                snapshots[i] = flat;
            }

            // Assign array WITHOUT owned prototype
            DetailPrototype[] newProtos = new DetailPrototype[existingCount - 1];
            int idx = 0;
            for (int i = 0; i < existingCount; i++)
            {
                if (i == ownedIdx) continue;
                newProtos[idx++] = protos[i];
            }
            data.detailPrototypes = newProtos; // may clear data

            // Restore non-owned layers
            for (int i = 0; i < existingCount; i++)
            {
                if (i == ownedIdx || snapshots[i] == null) continue;
                int newIdx = i < ownedIdx ? i : i - 1;
                int[,] restore = new int[res, res];
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    restore[x, z] = snapshots[i][z * res + x];
                data.SetDetailLayer(0, 0, newIdx, restore);
            }

            Debug.Log($"[GroundPass] 回滚清理完成：rock root 已销毁，owned detail (index={ownedIdx}) 已移除。");
        }

        // ------------------------------------------------------------------ 私有辅助：数学工具

        private static float DistanceToFlatRect(float nx, float nz)
        {
            float dx = Mathf.Max(Mathf.Max(FlatMinX - nx, nx - FlatMaxX), 0f);
            float dz = Mathf.Max(Mathf.Max(FlatMinZ - nz, nz - FlatMaxZ), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float Smoothstep(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
