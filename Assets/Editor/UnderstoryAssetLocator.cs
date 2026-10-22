using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// Understory 资产定位器：集中管理林下层植被（fern）/ 岩石 / 倒木的候选目录发现逻辑，
    /// 未来替换或补充资产只需把 prefab 放入对应目录，无需改代码 —— 是资产门控接口的唯一触点。
    ///
    /// 三个候选目录均属"资产门控"：目录不存在或目录内无合格 prefab 时返回空数组
    /// （不创建目录、不抛异常），对应子系统自动 no-op。
    /// 结果按资产名 Ordinal 升序（同名的按路径）排序：该顺序即实例化的随机流契约，跨 Bake 稳定。
    /// </summary>
    internal static class UnderstoryAssetLocator
    {
        // ---------------------------------------------------------------- 候选目录（门控接口唯一定义点）

        internal const string FernFolder = "Assets/Environment/Vegetation/Fern02/Prepared/Prefabs";
        internal const string RockFolder = "Assets/Environment/RocksAndDeadwood/RockMossSet01/Prepared/Prefabs";
        internal const string DeadwoodFolder = "Assets/Environment/RocksAndDeadwood/Deadwood/Prepared/Prefabs";

        // 名称关键词：包含任一 include 且不含任一 exclude 的 prefab 才被接受。
        // 排除词防止大树/灌木等高层植被被误归类为林下资产。
        private static readonly string[] FernKeywords = { "fern", "plant" };
        private static readonly string[] FernExcludeKeywords = { "tree", "bush", "shrub", "grass" };

        private static readonly string[] RockKeywords = { "rock", "boulder", "stone" };
        private static readonly string[] RockExcludeKeywords = { "tree", "bush", "fern", "grass" };

        private static readonly string[] DeadwoodKeywords = { "log", "stump", "deadwood", "snag" };
        private static readonly string[] DeadwoodExcludeKeywords = { "tree", "bush", "fern", "shrub" };

        // ---------------------------------------------------------------- 查询入口

        /// <summary>查找蕨类/林下植物 prefab。目录不存在或无合格资产时返回空数组（no-op 语义）。</summary>
        public static GameObject[] FindFernPrefabs()
        {
            return FindPrefabs(FernFolder, FernKeywords, FernExcludeKeywords, "fern/植物");
        }

        /// <summary>查找岩石 prefab。目录不存在或无合格资产时返回空数组（no-op 语义）。</summary>
        public static GameObject[] FindRockPrefabs()
        {
            return FindPrefabs(RockFolder, RockKeywords, RockExcludeKeywords, "岩石");
        }

        /// <summary>查找倒木/树桩 prefab。目录不存在或无合格资产时返回空数组（no-op 语义）。</summary>
        public static GameObject[] FindDeadwoodPrefabs()
        {
            return FindPrefabs(DeadwoodFolder, DeadwoodKeywords, DeadwoodExcludeKeywords, "倒木/树桩");
        }

        // ---------------------------------------------------------------- 私有辅助

        /// <summary>
        /// 在目录内搜索 GameObject（prefab/model），按名称关键词过滤、去重、确定性排序后返回。
        /// </summary>
        private static GameObject[] FindPrefabs(string folder, string[] includeKeywords, string[] excludeKeywords, string label)
        {
            if (!AssetDatabase.IsValidFolder(folder))
                return Array.Empty<GameObject>(); // 资产门控：目录未建立时静默 no-op（绝不创建目录）

            var entries = new List<KeyValuePair<string, GameObject>>();
            var seenPaths = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seenPaths.Add(path))
                    continue;

                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null)
                    continue;

                if (!ContainsAny(go.name, includeKeywords))
                    continue;
                if (ContainsAny(go.name, excludeKeywords))
                    continue;

                entries.Add(new KeyValuePair<string, GameObject>(path, go));
            }

            GameObject[] result = entries
                .OrderBy(e => e.Value.name, StringComparer.Ordinal)
                .ThenBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => e.Value)
                .ToArray();

            if (result.Length > 0)
            {
                string preview = string.Join(", ", result.Take(8).Select(p => p.name));
                if (result.Length > 8)
                    preview += ", ...";
                Debug.Log($"[UnderstoryLocator] {folder}: 找到 {result.Length} 个{label} prefab: {preview}");
            }

            return result;
        }

        /// <summary>名称是否包含任一关键词（不区分大小写）。</summary>
        private static bool ContainsAny(string name, string[] keywords)
        {
            if (string.IsNullOrEmpty(name) || keywords == null)
                return false;

            return keywords.Any(k => !string.IsNullOrEmpty(k) &&
                                     name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
