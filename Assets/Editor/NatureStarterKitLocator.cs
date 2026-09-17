using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// Nature Starter Kit 2 资产定位器。
    /// 集中管理 NSk2 资产路径发现逻辑，未来替换资产包只需修改此文件。
    /// 所有查询在 NSk2 未导入（Root == null）时优雅返回空结果，不抛异常。
    /// </summary>
    internal static class NatureStarterKitLocator
    {
        // NSk2 根目录候选名（不同版本/导入方式可能路径略有差异）
        private static readonly string[] RootCandidates =
        {
            "Assets/NatureStarterKit2",
            "Assets/Nature Starter Kit 2",
            "Assets/Nature Starter Kit",
            "Assets/NatureStarterKit"
        };

        // 树木关键词（用于 FindTreePrefabs 名称过滤）
        private static readonly string[] TreeKeywords = { "tree" };

        // 树木排除词：这些通常属于草/灌木/细节，不应归类为大树的 TreePrototype
        private static readonly string[] TreeExcludeKeywords =
        {
            "bush", "shrub", "grass", "fern", "plant", "detail"
        };

        // 细节（草/蕨/灌木）关键词（用于 FindDetailMeshes 名称过滤）
        private static readonly string[] DetailKeywords =
        {
            "grass", "fern", "plant", "bush", "shrub", "detail"
        };

        // 细节排除词：避免大树被误归类为 DetailPrototype
        private static readonly string[] DetailExcludeKeywords = { "tree" };

        // 地面纹理次选关键词（prefer 未命中时使用）
        private static readonly string[] GroundKeywords = { "ground", "grass", "terrain" };

        // 地面纹理排除词：法线/凹凸/天空/云层贴图不适合作 diffuse
        private static readonly string[] GroundExcludeKeywords =
        {
            "normal", "bump", "sky", "cloud"
        };

        /// <summary>
        /// 找到的 NSk2 根路径；null 表示未导入。
        /// </summary>
        public static string Root { get; } = FindRoot();

        /// <summary>
        /// NSk2 是否已导入到项目中。
        /// </summary>
        public static bool IsImported => Root != null;

        // ---------------------------------------------------------------- 根目录发现

        /// <summary>
        /// 遍历候选路径，返回第一个在 AssetDatabase 中有效的文件夹。
        /// </summary>
        private static string FindRoot()
        {
            foreach (string candidate in RootCandidates)
            {
                if (AssetDatabase.IsValidFolder(candidate))
                {
                    Debug.Log($"[NSk2Locator] 定位到 NSk2 根目录: {candidate}");
                    return candidate;
                }
            }

            Debug.LogWarning("[NSk2Locator] 未找到 Nature Starter Kit 2 根目录，自然环境相关功能将被跳过。");
            return null;
        }

        // ---------------------------------------------------------------- 树木 Prefab

        /// <summary>
        /// 查找树木 Prefab（用于 Terrain TreePrototype）。
        /// 按名称过滤包含 "tree" 关键词的 Prefab，并排除草/灌木等细节资产。
        /// </summary>
        public static GameObject[] FindTreePrefabs()
        {
            if (Root == null)
                return Array.Empty<GameObject>();

            GameObject[] trees = FindGameObjects(
                    includeKeywords: TreeKeywords,
                    excludeKeywords: TreeExcludeKeywords)
                .ToArray();

            LogFound(trees.Length, "树木 Prefab", trees.Select(o => o.name));
            return trees;
        }

        // ---------------------------------------------------------------- 细节 Mesh / Prefab

        /// <summary>
        /// 查找草/蕨类/小型灌木的 Mesh 或 Prefab（用于 Terrain DetailPrototype）。
        /// 按名称过滤包含草/蕨/灌木等关键词，并排除名称含 "tree" 的大树。
        /// </summary>
        public static GameObject[] FindDetailMeshes()
        {
            if (Root == null)
                return Array.Empty<GameObject>();

            GameObject[] details = FindGameObjects(
                    includeKeywords: DetailKeywords,
                    excludeKeywords: DetailExcludeKeywords)
                .ToArray();

            LogFound(details.Length, "细节 Mesh/Prefab", details.Select(o => o.name));
            return details;
        }

        // ---------------------------------------------------------------- 地面纹理

        /// <summary>
        /// 查找地面纹理（用于 TerrainLayer 的 diffuseTexture）。
        /// 优先匹配 prefer 参数指定的名称，否则取第一张 ground/grass/terrain texture。
        /// </summary>
        public static Texture2D FindGroundTexture(string prefer = "grass")
        {
            if (Root == null)
                return null;

            Texture2D[] textures = FindAssetsByType<Texture2D>("t:Texture2D")
                .Where(t => t != null && !ContainsAny(t.name, GroundExcludeKeywords))
                .ToArray();

            if (textures.Length == 0)
            {
                Debug.LogWarning("[NSk2Locator] 未找到可用的地面纹理。");
                return null;
            }

            // 1) 优先命中 prefer 关键词
            Texture2D preferred = textures.FirstOrDefault(t =>
                !string.IsNullOrEmpty(prefer) &&
                t.name.IndexOf(prefer, StringComparison.OrdinalIgnoreCase) >= 0);

            // 2) 其次命中 ground/grass/terrain 关键词
            Texture2D ground = preferred ?? textures.FirstOrDefault(t => ContainsAny(t.name, GroundKeywords));

            // 3) 最后兜底取第一张
            Texture2D result = ground ?? textures[0];

            LogFound(1, "地面纹理", new[] { result.name });
            return result;
        }

        // ---------------------------------------------------------------- 天空全景贴图

        /// <summary>
        /// 查找天空全景贴图的资产路径（NSk2 的 sky 贴图）。
        /// 返回路径而非 Texture：该资产可能仍以旧式 cubemap 导入（LoadAssetAtPath&lt;Texture2D&gt; 取不到），
        /// 调用方需先按路径修正导入设置为 2D 全景图再加载。
        /// </summary>
        public static string FindSkyTexturePath()
        {
            if (Root == null)
                return null;

            // 1) 已知布局优先：NSk2 的 Textures/sky.tga（用基类 Texture 探测，不限定导入类型）
            string direct = Root + "/Textures/sky.tga";
            if (AssetDatabase.LoadAssetAtPath<Texture>(direct) != null)
            {
                LogFound(1, "天空贴图", new[] { "Textures/sky.tga" });
                return direct;
            }

            // 2) 兜底：在 Root 下按文件名搜索（优先 .tga，避免命中同名 .psd 源文件）
            string fallback = null;
            foreach (string guid in AssetDatabase.FindAssets("sky", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;
                if (!System.IO.Path.GetFileNameWithoutExtension(path)
                        .Equals("sky", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (AssetDatabase.LoadAssetAtPath<Texture>(path) == null)
                    continue;

                if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    LogFound(1, "天空贴图", new[] { System.IO.Path.GetFileName(path) });
                    return path;
                }

                if (fallback == null)
                    fallback = path;
            }

            if (fallback != null)
            {
                LogFound(1, "天空贴图", new[] { System.IO.Path.GetFileName(fallback) });
                return fallback;
            }

            Debug.LogWarning("[NSk2Locator] 未找到天空全景贴图（Textures/sky.tga 或名为 sky 的纹理资产）。");
            return null;
        }

        // ---------------------------------------------------------------- 私有辅助

        /// <summary>
        /// 在 Root 目录下按 AssetDatabase 查询串搜索资产并加载，去重后返回。
        /// </summary>
        private static IEnumerable<T> FindAssetsByType<T>(string searchFilter) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets(searchFilter, new[] { Root });
            var seen = new HashSet<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seen.Add(path))
                    continue;

                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                    yield return asset;
            }
        }

        /// <summary>
        /// 搜索 GameObject（Prefab），按名称包含任一 include 关键词、且不含任一 exclude 关键词过滤，去重。
        /// </summary>
        private static IEnumerable<GameObject> FindGameObjects(string[] includeKeywords, string[] excludeKeywords)
        {
            var seen = new HashSet<GameObject>();
            foreach (GameObject go in FindAssetsByType<GameObject>("t:GameObject"))
            {
                if (go == null || !seen.Add(go))
                    continue;

                if (!ContainsAny(go.name, includeKeywords))
                    continue;
                if (ContainsAny(go.name, excludeKeywords))
                    continue;

                yield return go;
            }
        }

        /// <summary>
        /// 名称是否包含任一关键词（不区分大小写）。
        /// </summary>
        private static bool ContainsAny(string name, string[] keywords)
        {
            if (string.IsNullOrEmpty(name) || keywords == null)
                return false;

            return keywords.Any(k => !string.IsNullOrEmpty(k) &&
                                     name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// 统一的成功日志输出，便于调试资产发现结果。
        /// </summary>
        private static void LogFound(int count, string type, IEnumerable<string> names)
        {
            List<string> list = names.ToList();
            string preview = string.Join(", ", list.Take(8));
            if (list.Count > 8)
                preview += ", ...";
            Debug.Log($"[NSk2Locator] 找到 {count} 个 {type}: {preview}");
        }
    }
}
