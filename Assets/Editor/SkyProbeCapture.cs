using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// 天空接缝像素探针（临时验证工具，验证完成后可删除此文件）：
    /// 1) 以默认机位为起点环绕 8 个方位各渲染一帧，统计天空带逐列亮度均值的最大相邻差（已剔除树木暗列）；
    /// 2) 对最差方位输出环绕列 ±6 列的亮度剖面，区分"平均色条带"与"真实内容台阶"；
    /// 3) 临时关闭天空贴图 mip 后重渲染同一方位做 A/B（验证经度环绕列 mip 采样伪影假说），随后恢复导入设置。
    /// 菜单 Jurassic World > Capture Sky Probe，或
    /// -executeMethod JurassicWorld.Editor.SkyProbeCapture.Capture 调用。
    /// 输出 Logs/sky_probe_yaw*.png、Logs/sky_probe_nomip.png 与 [SkyProbe] 统计日志。
    /// </summary>
    public static class SkyProbeCapture
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const int Width = 640;
        private const int Height = 360;
        private const int YawCount = 8;

        // 只统计画面 4%–47% 高度的天空带（避开顶部边缘噪声与树线以下的植被）。
        private const float SkyBandTopRatio = 0.04f;
        private const float SkyBandBottomRatio = 0.47f;
        private const float DarkPixelLuma = 120f;

        [MenuItem("Jurassic World/Capture Sky Probe")]
        public static void Capture()
        {
            if (!File.Exists(MainScenePath))
            {
                Debug.LogError("[SkyProbe] 缺少已烘焙的主场景，请先执行 Jurassic World/Bake Scene (Edit Mode)。");
                return;
            }

            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Camera camera = Object.FindObjectOfType<Camera>();
            if (camera == null)
            {
                Debug.LogError("[SkyProbe] 场景内未找到相机。");
                return;
            }

            Transform camT = camera.transform;
            Quaternion originalRotation = camT.rotation;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var rt = new RenderTexture(Width, Height, 24);
            Directory.CreateDirectory("Logs");
            try
            {
                float baseYaw = originalRotation.eulerAngles.y;
                float pitch = originalRotation.eulerAngles.x;

                float bestStep = -1f;
                int bestYawDeg = 0;
                int bestX = 0;
                for (int i = 0; i < YawCount; i++)
                {
                    int yawDeg = i * (360 / YawCount);
                    camT.rotation = Quaternion.Euler(pitch, baseYaw + yawDeg, 0f);
                    Texture2D frame = RenderFrame(camera, rt);
                    File.WriteAllBytes($"Logs/sky_probe_yaw{yawDeg:D3}.png", frame.EncodeToPNG());

                    float step;
                    int stepX;
                    MeasureSkyBandStep(frame, out step, out stepX);
                    Debug.Log($"[SkyProbe] yaw+{yawDeg:F0}° maxVerticalStep={step:F2} @x={stepX}/{Width}");
                    if (step > bestStep)
                    {
                        bestStep = step;
                        bestYawDeg = yawDeg;
                        bestX = stepX;
                    }
                    Object.DestroyImmediate(frame);
                }

                Debug.Log($"[SkyProbe] SKY_SEAM_PROBE worstYaw=+{bestYawDeg}° maxVerticalStep={bestStep:F2} @x={bestX}/{Width}" +
                          "（8 方位天空带逐列亮度均值最大相邻差，已剔除树木暗列；<5 视为干净，>15 视为明显接缝）");

                // 最差方位的环绕列剖面 + mip 开关 A/B。
                camT.rotation = Quaternion.Euler(pitch, baseYaw + bestYawDeg, 0f);
                Texture2D worst = RenderFrame(camera, rt);
                LogColumnProfile(worst, bestX);

                Texture skyTex = RenderSettings.skybox != null ? RenderSettings.skybox.GetTexture("_MainTex") : null;
                string texPath = skyTex != null ? AssetDatabase.GetAssetPath(skyTex) : null;
                if (!string.IsNullOrEmpty(texPath))
                {
                    var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                    if (importer != null)
                    {
                        Debug.Log($"[SkyProbe] 天空贴图 {texPath}：shape={importer.textureShape} mip={importer.mipmapEnabled} " +
                                  $"aniso={importer.anisoLevel} wrap={importer.wrapMode} filter={importer.filterMode}");
                        if (importer.mipmapEnabled)
                        {
                            importer.mipmapEnabled = false;
                            importer.SaveAndReimport();

                            Texture2D noMip = RenderFrame(camera, rt);
                            File.WriteAllBytes("Logs/sky_probe_nomip.png", noMip.EncodeToPNG());
                            float step2;
                            int stepX2;
                            MeasureSkyBandStep(noMip, out step2, out stepX2);
                            Debug.Log($"[SkyProbe] NOMIP_AB mip=off maxVerticalStep={step2:F2} @x={stepX2}/{Width}" +
                                      $"（mip=on 时 {bestStep:F2}；若显著下降则残留属经度环绕列 mip 采样伪影）");
                            LogColumnProfile(noMip, stepX2);
                            Object.DestroyImmediate(noMip);

                            // 恢复导入设置保持资产状态不变；永久状态由 Bake 统一强制（关闭 mip）。
                            importer.mipmapEnabled = true;
                            importer.SaveAndReimport();
                        }
                    }
                }
                Object.DestroyImmediate(worst);
            }
            finally
            {
                camT.rotation = originalRotation;
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        private static Texture2D RenderFrame(Camera camera, RenderTexture rt)
        {
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previousTarget;

            RenderTexture.active = rt;
            var frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            frame.Apply();
            return frame;
        }

        // 天空带逐列亮度均值；返回最大相邻列差与位置。
        // 相邻列任一侧树木暗像素占比过高（>15%）视为枝叶边缘，不参与候选（防近景树误报）。
        private static void MeasureSkyBandStep(Texture2D frame, out float maxStep, out int maxStepX)
        {
            float[] columnMeans;
            float[] darkFractions;
            ComputeColumnStats(frame, out columnMeans, out darkFractions);

            maxStep = 0f;
            maxStepX = 0;
            for (int x = 4; x < Width - 4; x++)
            {
                if (darkFractions[x - 1] > 0.15f || darkFractions[x] > 0.15f || darkFractions[x + 1] > 0.15f)
                    continue;
                float step = Mathf.Abs(columnMeans[x] - columnMeans[x - 1]);
                if (step > maxStep)
                {
                    maxStep = step;
                    maxStepX = x;
                }
            }
        }

        // 环绕列 ±6 列的亮度剖面：mip 平均色条带 = 单列凹陷后回升；真实内容台阶 = 单侧不回。
        private static void LogColumnProfile(Texture2D frame, int centerX)
        {
            float[] columnMeans;
            float[] darkFractions;
            ComputeColumnStats(frame, out columnMeans, out darkFractions);

            var text = new StringBuilder($"[SkyProbe] profile x={centerX}: ");
            int from = Mathf.Max(0, centerX - 6);
            int to = Mathf.Min(Width - 1, centerX + 6);
            for (int x = from; x <= to; x++)
                text.Append($"{x}:{columnMeans[x]:F1} ");
            Debug.Log(text.ToString());
        }

        private static void ComputeColumnStats(Texture2D frame, out float[] columnMeans, out float[] darkFractions)
        {
            int yTop = Mathf.RoundToInt(Height * SkyBandTopRatio);
            int yBottom = Mathf.RoundToInt(Height * SkyBandBottomRatio);
            int rows = yBottom - yTop;
            Color32[] pixels = frame.GetPixels32();
            columnMeans = new float[Width];
            darkFractions = new float[Width];
            for (int x = 0; x < Width; x++)
            {
                float sum = 0f;
                int dark = 0;
                for (int y = yTop; y < yBottom; y++)
                {
                    // 纹理坐标 y=0 在画面底部，逐行从下往上取该列的天空带。
                    Color32 c = pixels[y * Width + x];
                    float luma = c.r * 0.30f + c.g * 0.59f + c.b * 0.11f;
                    sum += luma;
                    if (luma < DarkPixelLuma)
                        dark++;
                }
                columnMeans[x] = sum / rows;
                darkFractions[x] = (float)dark / rows;
            }
        }
    }
}
