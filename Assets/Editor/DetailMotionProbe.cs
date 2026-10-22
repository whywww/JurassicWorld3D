using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// 临时诊断工具（验收后可删）：判定地形 detail（蕨类 Fern02）"整株左右移动 / 根部移动"的来源。
    ///
    /// 【为什么需要它】蕨类是 `DetailRenderMode.Grass` 的 mesh detail，出现"整株（含根部）滑动"时有两种可能：
    ///   (A) 风动画：Grass 模式 detail 随风移动（Unity 文档：VertexLit 不随风移动），位移若不含高度权重
    ///       就会把整株（含根部）一起推走；
    ///   (B) 实例重散布：InstanceCountMode 下 detail 实例每帧/patch 重建被重新随机摆位（与风力参数无关）。
    /// 二者修法完全不同，故用"同一帧内多组受控渲染 + 逐像素 diff"直接判定：
    ///   ① 同帧重复渲染两次 —— 不同 ⇒ 渲染本身不确定（= B）
    ///   ②/③ 手动改写 `_Time` —— 不同 ⇒ 与时间相关（= A）
    ///   ④ 关 wavingGrass / ⑤ 关 WindZone / ⑥ 切 VertexLit —— 定位是哪个开关在驱动
    ///   ⑦ 机位 +5cm 再回原位 —— 不同 ⇒ 与相机位置相关的重建随机（重散布）
    ///   ⑧ fern 层密度清零 —— 建立"蕨类像素掩码"，把上面各项的变化限定到蕨类像素上
    ///   ⑨ fern 材质 `_Cutoff=1` —— 若蕨类随之消失 ⇒ detail 绘制确实走本项目的 fern 材质 shader
    ///      （否则说明走的是 Unity 内置 detail shader，改自制 shader 无效）
    ///
    /// 【只读契约】只打开 Main.unity、只在内存里改设置/临时相机；**绝不** SaveScene / SaveAssets /
    /// SetDirty / Refresh。唯一磁盘写入是 Logs/acceptance/probe/*.png。
    /// </summary>
    public static class DetailMotionProbe
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string OutputRelativeFolder = "Logs/acceptance/probe";
        private const int Width = 1280;
        private const int Height = 720;

        // 机位：南侧蕨类带内（metrics `viewPlan.densestFern` 格 ≈ (14.25,-85.74)，单格密度 7），
        // 朝 -z 并下俯 14° —— 让蕨类带铺满画面中下部，便于逐像素 diff。
        private static readonly Vector3 Eye = new Vector3(14.25f, 2.2f, -80.5f);
        private const float Pitch = -14f;

        [MenuItem("Jurassic World/Acceptance/Probe Detail Motion", false, 7)]
        private static void MenuProbeMenu() => JurassicMenuGuard.Run(MenuProbe, true);

        [MenuItem("Jurassic World/Acceptance/Probe Detail Motion", true)]
        private static bool MenuProbeMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void MenuProbe() => Probe();

        public static void Probe()
        {
            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Terrain terrain = Object.FindObjectOfType<Terrain>();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogError("[Probe] 场景中没有 Terrain：中止。");
                return;
            }

            TerrainData data = terrain.terrainData;
            WindZone wind = Object.FindObjectOfType<WindZone>();
            Camera cam = CreateCamera(out RenderTexture rt);

            // 备份待改状态（全部只在内存里改，结尾恢复；全程不保存）
            float savedStrength = data.wavingGrassStrength;
            float savedAmount = data.wavingGrassAmount;
            float savedWindMain = wind != null ? wind.windMain : 0f;
            DetailPrototype[] protos = data.detailPrototypes ?? new DetailPrototype[0];
            int fernProto = IndexOfFernPrototype(protos);
            DetailRenderMode savedMode = fernProto >= 0 ? protos[fernProto].renderMode : DetailRenderMode.Grass;
            Material fernMaterial = fernProto >= 0 ? FernMaterialOf(protos[fernProto]) : null;
            float savedCutoff = fernMaterial != null && fernMaterial.HasProperty("_Cutoff")
                ? fernMaterial.GetFloat("_Cutoff")
                : -1f;
            int fernLayerCount = CountFernPrototypes(protos, fernProto);
            int res = data.detailResolution;
            var savedLayers = new int[fernLayerCount][,];
            for (int i = 0; i < fernLayerCount; i++)
                savedLayers[i] = data.GetDetailLayer(0, 0, res, res, fernProto + i);

            Debug.Log($"[Probe] 场景={scene.name} 机位={Eye} pitch={Pitch}；fern 原型 idx={fernProto}" +
                      $"（层 {fernProto}..{fernProto + fernLayerCount - 1}）、" +
                      $"wavingGrass strength={savedStrength:F3}/amount={savedAmount:F3}、" +
                      $"WindZone={(wind != null ? wind.windMain.ToString("F2", CultureInfo.InvariantCulture) : "无")}、" +
                      $"renderMode={savedMode}、detailObjectDistance={terrain.detailObjectDistance:F0}m、" +
                      $"fern 材质={(fernMaterial != null ? fernMaterial.name + "（shader=" + fernMaterial.shader.name + "，_Cutoff=" + savedCutoff.ToString("F2", CultureInfo.InvariantCulture) + "）" : "无")}");

            try
            {
                Color32[] v1 = Render(cam, rt, "probe_v1_baseline");
                Color32[] v2 = Render(cam, rt, null);
                Report("① 同帧重复渲染（确定性基线）", v1, v2);

                // ⑧ fern 层清零 → 建立"蕨类像素掩码"
                for (int i = 0; i < fernLayerCount; i++)
                    data.SetDetailLayer(0, 0, fernProto + i, new int[res, res]);
                Color32[] v8 = Render(cam, rt, "probe_v8_fernremoved");
                bool[] fernMask = BuildChangedMask(v1, v8, 2);
                Report("⑧ fern 层清零 vs 基线", v1, v8);
                Debug.Log($"[Probe] ⑧ 蕨类像素掩码 = {Count(fernMask)}/{fernMask.Length}（{100f * Count(fernMask) / fernMask.Length:F2}% 画面）");

                // 恢复 fern 层，确认回到基线（同时验证写入/恢复链路）
                for (int i = 0; i < fernLayerCount; i++)
                    data.SetDetailLayer(0, 0, fernProto + i, savedLayers[i]);
                Color32[] v1c = Render(cam, rt, null);
                Report("⑧b fern 层恢复后 vs 基线（应≈0）", v1, v1c);

                // (A) 时间相关性
                Shader.SetGlobalVector("_Time", new Vector4(5f, 5f, 10f, 15f));
                Color32[] v3 = Render(cam, rt, "probe_v3_time5");
                Report("② _Time=5.0s vs 基线（与时间相关？）", v1, v3);
                ReportMasked("②' 同上，仅统计蕨类像素", v1, v3, fernMask);
                Shader.SetGlobalVector("_Time", new Vector4(5.9f, 5.9f, 11.8f, 17.7f));
                Color32[] v4 = Render(cam, rt, "probe_v4_time5_9");
                Report("③ _Time=5.9s vs 5.0s（随时间摆动？）", v3, v4);
                ReportMasked("③' 同上，仅统计蕨类像素", v3, v4, fernMask);
                Shader.SetGlobalVector("_Time", new Vector4(Time.time, Time.time, Time.time * 2f, Time.time * 3f));

                // (A1) wavingGrass 风力参数
                data.wavingGrassStrength = 0f;
                data.wavingGrassAmount = 0f;
                Color32[] v5 = Render(cam, rt, "probe_v5_nowavinggrass");
                Report("④ wavingGrass=0 vs 基线", v1, v5);
                ReportMasked("④' 同上，仅统计蕨类像素", v1, v5, fernMask);
                data.wavingGrassStrength = savedStrength;
                data.wavingGrassAmount = savedAmount;

                // (A2) WindZone
                if (wind != null)
                {
                    wind.windMain = 0f;
                    Color32[] v6 = Render(cam, rt, "probe_v6_nowindzone");
                    Report("⑤ WindZone.main=0 vs 基线", v1, v6);
                    ReportMasked("⑤' 同上，仅统计蕨类像素", v1, v6, fernMask);
                    wind.windMain = savedWindMain;
                }

                // (A3) fern 渲染模式改 VertexLit（Unity 文档：不随风移动）
                if (fernProto >= 0)
                {
                    protos[fernProto].renderMode = DetailRenderMode.VertexLit;
                    Color32[] v7 = Render(cam, rt, "probe_v7_vertexlit");
                    Report("⑥ fern renderMode=VertexLit vs Grass", v1, v7);
                    ReportMasked("⑥' 同上，仅统计蕨类像素", v1, v7, fernMask);
                    protos[fernProto].renderMode = savedMode;
                }

                // (B) 机位抖动回原位：1 与 3 不同 ⇒ 与相机位置相关的重建随机（重散布）
                Vector3 savedPos = cam.transform.position;
                Color32[] j1 = Render(cam, rt, null);
                cam.transform.position = savedPos + new Vector3(0.05f, 0f, 0f);
                Render(cam, rt, null);
                cam.transform.position = savedPos;
                Color32[] j3 = Render(cam, rt, null);
                Report("⑦ 机位 +5cm 再回原位（重建确定性）", j1, j3);
                ReportMasked("⑦' 同上，仅统计蕨类像素", j1, j3, fernMask);

                // ⑨ fern 材质 _Cutoff=1：若蕨类整体消失 ⇒ detail 绘制走本项目 fern 材质 shader
                if (fernMaterial != null && savedCutoff >= 0f)
                {
                    fernMaterial.SetFloat("_Cutoff", 1f);
                    Color32[] v9 = Render(cam, rt, "probe_v9_cutoff1");
                    Report("⑨ fern 材质 _Cutoff=1 vs 基线", v1, v9);
                    ReportMasked("⑨' 同上，仅统计蕨类像素（若≈掩码全变 ⇒ 走自制 shader）", v1, v9, fernMask);
                    fernMaterial.SetFloat("_Cutoff", savedCutoff);
                }
            }
            finally
            {
                for (int i = 0; i < fernLayerCount; i++)
                    data.SetDetailLayer(0, 0, fernProto + i, savedLayers[i]);
                data.wavingGrassStrength = savedStrength;
                data.wavingGrassAmount = savedAmount;
                if (wind != null)
                    wind.windMain = savedWindMain;
                if (fernProto >= 0)
                    protos[fernProto].renderMode = savedMode;
                if (fernMaterial != null && savedCutoff >= 0f)
                    fernMaterial.SetFloat("_Cutoff", savedCutoff);

                Object.DestroyImmediate(cam.gameObject);
                rt.Release();
                Object.DestroyImmediate(rt);
                Debug.Log("[Probe] 已恢复全部内存改动（未保存任何资产/场景）。");
            }
        }

        private static int IndexOfFernPrototype(DetailPrototype[] protos)
        {
            for (int i = 0; i < protos.Length; i++)
            {
                DetailPrototype p = protos[i];
                if (p == null || !p.usePrototypeMesh || p.prototype == null)
                    continue;
                string path = AssetDatabase.GetAssetPath(p.prototype);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/Environment/Vegetation/Fern02"))
                    return i;
            }
            return -1;
        }

        private static int CountFernPrototypes(DetailPrototype[] protos, int first)
        {
            if (first < 0)
                return 0;
            int count = 0;
            for (int i = first; i < protos.Length; i++)
            {
                if (protos[i] == null || !protos[i].usePrototypeMesh || protos[i].prototype == null)
                    continue;
                string path = AssetDatabase.GetAssetPath(protos[i].prototype);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/Environment/Vegetation/Fern02"))
                    count++;
            }
            return count;
        }

        private static Material FernMaterialOf(DetailPrototype proto)
        {
            if (proto == null || proto.prototype == null)
                return null;
            Renderer renderer = proto.prototype.GetComponentInChildren<Renderer>(true);
            return renderer != null ? renderer.sharedMaterial : null;
        }

        private static Camera CreateCamera(out RenderTexture rt)
        {
            var go = new GameObject("DetailMotionProbeCamera");
            var cam = go.AddComponent<Camera>();
            cam.transform.position = Eye;
            cam.transform.rotation = Quaternion.Euler(Pitch, 180f, 0f); // 朝 -z
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 150f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.enabled = false; // 只手动 Render，不进渲染循环
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            return cam;
        }

        private static Color32[] Render(Camera cam, RenderTexture rt, string saveName)
        {
            cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            Color32[] pixels = tex.GetPixels32();

            if (!string.IsNullOrEmpty(saveName))
            {
                string folder = Path.Combine(Directory.GetCurrentDirectory(), OutputRelativeFolder);
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, saveName + ".png"), tex.EncodeToPNG());
            }

            Object.DestroyImmediate(tex);
            return pixels;
        }

        private static bool[] BuildChangedMask(Color32[] a, Color32[] b, int threshold)
        {
            var mask = new bool[a.Length];
            for (int i = 0; i < a.Length; i++)
                mask[i] = ChannelDiff(a[i], b[i]) > threshold;
            return mask;
        }

        private static int ChannelDiff(Color32 a, Color32 b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        }

        private static int Count(bool[] mask)
        {
            int n = 0;
            foreach (bool m in mask)
                if (m)
                    n++;
            return n;
        }

        private static void Report(string label, Color32[] a, Color32[] b)
        {
            long changed = 0, sum = 0;
            int max = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = ChannelDiff(a[i], b[i]);
                sum += d;
                if (d > 2)
                    changed++;
                if (d > max)
                    max = d;
            }
            Debug.Log($"[Probe] {label} → 变化像素 {changed}/{a.Length}（{100f * changed / a.Length:F3}%）、" +
                      $"平均差 {sum / (double)a.Length:F4}、最大差 {max}");
        }

        private static void ReportMasked(string label, Color32[] a, Color32[] b, bool[] mask)
        {
            long changed = 0, total = 0, sum = 0;
            int max = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (!mask[i])
                    continue;
                total++;
                int d = ChannelDiff(a[i], b[i]);
                sum += d;
                if (d > 2)
                    changed++;
                if (d > max)
                    max = d;
            }
            float percent = total > 0 ? 100f * changed / total : 0f;
            Debug.Log($"[Probe] {label} → 蕨类像素中变化 {changed}/{total}（{percent:F2}%）、" +
                      $"平均差 {(total > 0 ? sum / (double)total : 0f):F4}、最大差 {max}");
        }
    }
}
