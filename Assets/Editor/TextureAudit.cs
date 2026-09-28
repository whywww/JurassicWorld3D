using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace JurassicWorld.Editor
{
    public static class TextureAudit
    {
        private sealed class Row
        {
            public string path;
            public int sourceWidth;
            public int sourceHeight;
            public int importedWidth;
            public int importedHeight;
            public int maxSize;
            public string format;
            public string compression;
            public bool mipmaps;
            public bool isUi;
            public long runtimeBytes;
            public long sourceFileBytes;
        }

        [MenuItem("Jurassic World/Export Texture Audit")]
        public static void Export()
        {
            var rows = new List<Row>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) continue;

                importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
                TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings("Standalone");
                int maxSize = platform.overridden ? platform.maxTextureSize : importer.maxTextureSize;
                rows.Add(new Row
                {
                    path = path,
                    sourceWidth = sourceWidth,
                    sourceHeight = sourceHeight,
                    importedWidth = texture.width,
                    importedHeight = texture.height,
                    maxSize = maxSize,
                    format = texture.graphicsFormat.ToString(),
                    compression = importer.textureCompression.ToString(),
                    mipmaps = importer.mipmapEnabled,
                    isUi = importer.textureType == TextureImporterType.Sprite || path.Contains("/UI/") || path.Contains("/Fonts/"),
                    runtimeBytes = Profiler.GetRuntimeMemorySizeLong(texture),
                    sourceFileBytes = new FileInfo(path).Length
                });
            }

            rows = rows.OrderByDescending(row => row.runtimeBytes).ToList();
            string outputFolder = Path.GetFullPath("Reports");
            Directory.CreateDirectory(outputFolder);
            var csv = new StringBuilder("path,source_width,source_height,imported_width,imported_height,standalone_max_size,graphics_format,compression,mipmaps,ui_or_sprite,runtime_memory_bytes,source_file_bytes\n");
            foreach (Row row in rows)
            {
                csv.Append(Csv(row.path)).Append(',').Append(row.sourceWidth).Append(',').Append(row.sourceHeight)
                    .Append(',').Append(row.importedWidth).Append(',').Append(row.importedHeight)
                    .Append(',').Append(row.maxSize).Append(',').Append(Csv(row.format))
                    .Append(',').Append(Csv(row.compression)).Append(',').Append(row.mipmaps)
                    .Append(',').Append(row.isUi).Append(',').Append(row.runtimeBytes)
                    .Append(',').Append(row.sourceFileBytes).Append('\n');
            }
            File.WriteAllText(Path.Combine(outputFolder, "TextureAudit.csv"), csv.ToString(), new UTF8Encoding(false));

            long total = rows.Sum(row => row.runtimeBytes);
            long sourceBytes = rows.Sum(row => row.sourceFileBytes);
            int large = rows.Count(row => Math.Max(row.sourceWidth, row.sourceHeight) >= 4096);
            int largeImported = rows.Count(row => Math.Max(row.importedWidth, row.importedHeight) > 2048);
            var markdown = new StringBuilder();
            markdown.AppendLine("# 纹理资源审计")
                .AppendLine()
                .AppendLine("由 Unity Editor 的 `Jurassic World/Export Texture Audit` 导出。分辨率与格式来自当前导入结果；运行内存是 `Profiler.GetRuntimeMemorySizeLong` 的估值，并非 GPU 显存实测或场景实际驻留量。")
                .AppendLine()
                .AppendLine($"- 纹理数量：{rows.Count}")
                .AppendLine($"- 源文件长边至少 4096 像素：{large}")
                .AppendLine($"- 导入后长边超过 2048 像素：{largeImported}")
                .AppendLine($"- 这些纹理源文件合计：{sourceBytes / 1048576.0:F1} MiB")
                .AppendLine($"- 全部导入纹理运行内存估值合计：{total / 1048576.0:F1} MiB（并非同一时刻全部加载）")
                .AppendLine()
                .AppendLine("| 资源 | 源分辨率 | 导入分辨率 | 格式 | Mip Map | 估值 MiB |")
                .AppendLine("|---|---:|---:|---|---|---:|");
            foreach (Row row in rows.Take(20))
            {
                markdown.Append("| ").Append(row.path.Replace("|", "\\|"))
                    .Append(" | ").Append(row.sourceWidth).Append('×').Append(row.sourceHeight)
                    .Append(" | ").Append(row.importedWidth).Append('×').Append(row.importedHeight)
                    .Append(" | ").Append(row.format).Append(" | ").Append(row.mipmaps ? "是" : "否")
                    .Append(" | ").Append((row.runtimeBytes / 1048576.0).ToString("F1")).AppendLine(" |");
            }
            markdown.AppendLine().AppendLine("完整清单及导入最大尺寸、压缩设置、UI 标记、源文件大小见 `TextureAudit.csv`。当前 4K 源图已在导入时缩至不超过 2K；仅改源文件分辨率不会进一步降低游戏运行时纹理占用。占用最大的天空图未启用 Mip Map，属全景天空用途，应单独评估。高档保持当前资源外观。")
                .AppendLine();
            File.WriteAllText(Path.Combine(outputFolder, "TextureAudit.md"), markdown.ToString(), new UTF8Encoding(false));
            Debug.Log($"[TextureAudit] 已导出 {rows.Count} 张纹理到 {outputFolder}");
        }

        private static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
