using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Canonical preferred sites survive Remove Sample and complete scene Bake.
    internal static class RainforestTreeSizing
    {
        private const string Path = "Assets/Editor/RainforestSamplePlanting.json";
        [Serializable] internal sealed class Tree
        {
            public string path;
            public Vector2 position;
            public float yaw, variation;
        }
        [Serializable] private sealed class Planting { public Tree[] trees; public string layout; }
        internal static float BaseHeight(string path)
        {
            switch (System.IO.Path.GetFileNameWithoutExtension(path))
            {
                case "tree01": return 10f;
                case "tree02": return 9f;
                case "tree03": return 8f;
                case "tree04": case "Pachira_B": return 6f;
                default: throw new InvalidOperationException("Unexpected sample tree: " + path);
            }
        }
        internal static Tree[] Load() => File.Exists(Path)
            ? JsonUtility.FromJson<Planting>(File.ReadAllText(Path)).trees : Array.Empty<Tree>();
        internal static void CaptureExisting()
        {
            var stored = File.Exists(Path) ? JsonUtility.FromJson<Planting>(File.ReadAllText(Path)) : null;
            if (stored != null && !string.IsNullOrEmpty(stored.layout)) return;
            var root = GameObject.Find(RainforestSample.RootName);
            if (root == null) return;
            if (stored != null)
            {
                stored.layout = JsonUtility.ToJson(root.GetComponent<RainforestSampleLayout>());
                File.WriteAllText(Path, JsonUtility.ToJson(stored, true) + "\n");
                AssetDatabase.ImportAsset(Path); return;
            }
            var trees = root.GetComponentsInChildren<SceneTreeObstacle>().Select(t =>
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                BaseHeight(path);
                return new Tree { path = path, position = new Vector2(t.transform.position.x, t.transform.position.z),
                    yaw = t.transform.eulerAngles.y,
                    variation = Mathf.Lerp(.82f, 1.14f, Mathf.InverseLerp(15f, 21f, CentralForestDensification.BoundsOf(t.gameObject).size.y)) };
            }).ToArray();
            if (trees.Length == 0) return;
            File.WriteAllText(Path, JsonUtility.ToJson(new Planting { trees = trees,
                layout = JsonUtility.ToJson(root.GetComponent<RainforestSampleLayout>()) }, true) + "\n");
            AssetDatabase.ImportAsset(Path);
        }
        internal static bool RestoreLayout(RainforestSampleLayout layout)
        {
            if (!File.Exists(Path)) return false;
            var stored = JsonUtility.FromJson<Planting>(File.ReadAllText(Path));
            if (string.IsNullOrEmpty(stored.layout)) return false;
            JsonUtility.FromJsonOverwrite(stored.layout, layout); return true;
        }
        internal static void WriteReport()
        {
            var root = GameObject.Find(RainforestSample.RootName);
            var original = Load();
            var report = new StringBuilder("# 样板树木尺寸协调\n\n树种基准与外围一致，等比例缩放 0.82～1.14 倍。原 15～21 米范围已移除。\n\n|树|树种|实际高度 m|基准 m|原位置与树种保留|\n|---|---|---:|---:|---|\n");
            int preserved = 0;
            foreach (var marker in root.GetComponentsInChildren<SceneTreeObstacle>())
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(marker.gameObject);
                float height = CentralForestDensification.BoundsOf(marker.gameObject).size.y, basis = BaseHeight(path);
                if (height < basis * .82f - .01f || height > basis * 1.14f + .01f)
                    throw new InvalidOperationException("Sample tree size outside species range: " + marker.name);
                bool retained = original.Any(t => t.path == path && Vector2.Distance(t.position,
                    new Vector2(marker.transform.position.x, marker.transform.position.z)) < .001f);
                if (retained) preserved++;
                report.AppendLine($"|{marker.name}|{System.IO.Path.GetFileNameWithoutExtension(path)}|{height:F2}|{basis:F1}|{retained}|");
            }
            report.AppendLine($"\n原种植记录 {original.Length} 棵；保留位置与树种 {preserved} 棵。冲突候选继续经过溪流、路线、出生、硬枝及互动空间检查。\n\n本次保留既有样板分区及其面积记录，不按当前更细的动画分块重新缩小林隙或增种。种植记录同时保存原有林隙、连接通道及密度统计；未扩大排除区以提高密度比例。\n\nBuild and Verify 同时检查两次生成一致、撤销、Bake 接入和外围对象及 Terrain 数据不变。固定入口/林内/侧面机位对照：size-before-0..2.png、size-after-0..2.png。运动与全场景检查结果见 TreeSizingVerification.md，不能仅凭构建通过推断普通 Play 全部通过。\n");
            File.WriteAllText(RainforestSample.Output + "/TreeSizing.md", report.ToString());
        }
        internal static void CaptureViews(string prefix)
        {
            if (prefix == "size-before" && File.Exists($"{RainforestSample.Output}/{prefix}-2.png")) return;
            var source = Camera.main;
            if (source == null) throw new InvalidOperationException("Main camera missing for tree size comparison.");
            var holder = new GameObject("Tree size comparison camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = holder.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create(); camera.targetTexture = target;
                var eyes = new[] { new Vector3(0f, 1.7f, -13f), new Vector3(-6f, 1.7f, 14f), new Vector3(29f, 1.7f, 22f) };
                var aims = new[] { new Vector3(-5f, 4f, 18f), new Vector3(9f, 4f, 30f), new Vector3(0f, 4f, 20f) };
                for (int i = 0; i < eyes.Length; i++)
                {
                    camera.transform.SetPositionAndRotation(eyes[i], Quaternion.LookRotation(aims[i] - eyes[i]));
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                    File.WriteAllBytes($"{RainforestSample.Output}/{prefix}-{i}.png", pixels.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = previous; Object.DestroyImmediate(holder); target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
