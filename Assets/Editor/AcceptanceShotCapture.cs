using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    /// <summary>
    /// 眼高视点验收渲染器（临时工具，验收后可删）。
    ///
    /// 【存在理由】玩家相机被 WorldBounds.PlayerExplorationBounds 钳制，且视高 = 地形高度 + eyeHeight
    /// （x∈[-78,78]、z∈[-78,88]；西/东/南三侧已越出地形平坦区 8m，故机位必须按地形高度取 Y）；
    /// 而 UnderstorySeeder 把 fern / rock / deadwood 的中大型道具放在恐龙活动包络 + DinoSafetyBuffer 之外、
    /// 低矮 fern/bush 则按视觉梯度允许进入活动包络外缘与玩家过渡带。修复前实体道具（rock/log/stump）全在
    /// 可达区外的密林里、被树冠与树干挡死，Play Mode 完全看不到；现已改为「近林带展示环 + 林缘清空缺口」
    /// （见 UnderstorySeeder.BuildShowcaseRing / ForestSeeder 的展示环清空），最近的展示道具约在可达范围外
    /// 4.5–12.5m 且位于树线缺口正中。本工具用【与玩家相机相同参数】（y = 地形高 + eyeHeight / FOV / near / far）
    /// 的临时相机渲染静帧，其中 showcase_* 组即"玩家贴边界正对缺口"的直接判据，
    /// zone8/zone9 组为本次空间重平衡（R0 空地 → R1/R2 → R3 林缘梯度 + 相机贴地）的直接判据。
    ///
    /// 【只读契约】主场景 Main.unity 全程不被修改：临时相机与灯光引用放在一个 additive 空场景里，
    /// 渲染所需的 RenderSettings（skybox / ambient / fog）在临时场景内重建，渲染在 RenderTexture 上进行，
    /// 结束后销毁临时对象并关闭临时场景。全过程不调用 SaveScene / SaveAssets / SetDirty / ImportAsset；
    /// 唯一磁盘写入是 Logs/acceptance/shots/*.png。
    ///
    /// 【依赖顺序】本工具读取 Logs/acceptance/&lt;label&gt;.metrics（由 ProductionAcceptance 采集）
    /// 里的 viewPlan 机位；请先执行菜单 3（Capture Post-Bake #1），再执行本菜单。
    /// </summary>
    public static class AcceptanceShotCapture
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string OutputRelativeFolder = "Logs/acceptance/shots";
        private const string MetricsLabel = "postbake1";
        private const int ShotWidth = 1920;
        private const int ShotHeight = 1080;
        private const float EyeHeightFallback = 1.7f;

        private static readonly float[] DeadwoodLodDistances = { 5f, 15f, 30f, 45f };

        [MenuItem("Jurassic World/Acceptance/7. Render Eye-Height Acceptance Shots", false, 7)]
        private static void MenuCaptureMenu() => JurassicMenuGuard.Run(MenuCapture, true);

        [MenuItem("Jurassic World/Acceptance/7. Render Eye-Height Acceptance Shots", true)]
        private static bool MenuCaptureMenuEnabled() => JurassicMenuGuard.CanRun;

        public static void MenuCapture() => Capture();

        /// <summary>批处理入口：-executeMethod JurassicWorld.Editor.AcceptanceShotCapture.Capture</summary>
        public static void Capture()
        {
            try
            {
                CaptureCore();
            }
            catch (Exception e)
            {
                Debug.LogError($"JURASSIC_ACCEPTANCE_SHOTS_FAIL: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }

        private static void CaptureCore()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("JURASSIC_ACCEPTANCE_SHOTS_FAIL: 当前处于 Play Mode；请先退出 Play Mode（本工具在编辑态渲染）。");
                return;
            }

            Scene mainScene = EditorSceneManager.GetActiveScene();
            if (mainScene.IsValid() && mainScene.isDirty)
            {
                Debug.LogError($"JURASSIC_ACCEPTANCE_SHOTS_FAIL: 活动场景 \"{mainScene.name}\" 有未保存改动，拒绝打开 Main.unity 以免丢弃你的工作。");
                return;
            }
            if (!File.Exists(FullPath(MainScenePath)))
            {
                Debug.LogError($"JURASSIC_ACCEPTANCE_SHOTS_FAIL: 缺少 {MainScenePath}。");
                return;
            }
            if (!mainScene.IsValid() || !string.Equals(mainScene.path, MainScenePath, StringComparison.Ordinal))
                mainScene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            string metricsPath = FullPath("Logs/acceptance/" + MetricsLabel + ".metrics");
            if (!File.Exists(metricsPath))
            {
                Debug.LogError($"JURASSIC_ACCEPTANCE_SHOTS_FAIL: 缺少 {metricsPath}；请先执行菜单 3（Capture Post-Bake #1）后再渲染。");
                return;
            }
            Dictionary<string, string> metrics = ReadMetrics(metricsPath);

            Camera playerCamera = FindPlayerCamera(mainScene);
            Terrain terrain = FindInScene<Terrain>(mainScene);
            float eyeHeight = playerCamera != null ? playerCamera.transform.position.y : EyeHeightFallback;
            float fov = playerCamera != null ? playerCamera.fieldOfView : 65f;
            float near = playerCamera != null ? playerCamera.nearClipPlane : 0.15f;
            float far = playerCamera != null ? playerCamera.farClipPlane : 150f;
            Debug.Log($"[Shots] 模板相机：eyeHeight={eyeHeight:F2} fov={fov:F1} near={near:F2} far={far:F2}；" +
                      $"分辨率 {ShotWidth}×{ShotHeight}；HUD（Screen Space - Overlay Canvas）不会进入相机渲染，故静帧不含 HUD。");

            string folder = FullPath(OutputRelativeFolder);
            Directory.CreateDirectory(folder);

            Scene previousActive = mainScene;
            Scene tempScene = default;
            Camera tempCamera = null;
            RenderTexture target = null;
            var failures = new List<string>();
            int success = 0;

            try
            {
                tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                CopyRenderSettingsInto(tempScene);
                EditorSceneManager.SetActiveScene(tempScene);

                var cameraObject = new GameObject("AcceptanceTempCamera");
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                tempCamera = cameraObject.AddComponent<Camera>();
                tempCamera.fieldOfView = fov;
                tempCamera.nearClipPlane = near;
                tempCamera.farClipPlane = far;
                tempCamera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                tempCamera.backgroundColor = RenderSettings.skybox != null ? Color.black : new Color(0.45f, 0.55f, 0.65f);
                tempCamera.allowHDR = false;
                tempCamera.allowMSAA = true;
                tempCamera.enabled = false;

                target = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
                target.antiAliasing = 4;
                target.Create();
                tempCamera.targetTexture = target;

                var shots = BuildShotPlan(metrics, mainScene, terrain, eyeHeight);

                // 首个静帧 = 玩家出生点参考帧：用于与真实 Game View 对拍，校验临时场景的渲染设置正确
                ShotPlan? first = null;
                foreach (ShotPlan candidate in shots)
                    if (candidate.Label == "spawn_ref")
                    {
                        first = candidate;
                        break;
                    }
                if (!first.HasValue || !RenderOne(tempCamera, target, folder, first.Value, failures))
                {
                    Debug.LogError("JURASSIC_ACCEPTANCE_SHOTS_FAIL: 出生点参考帧渲染失败 —— 渲染设置可能不可用，请改用回退方案" +
                                   "（玩家可达边界 Game View 截图 + postbake1.metrics 的 sink/footprint/材质数值诊断）。");
                    return;
                }
                success++;

                foreach (ShotPlan shot in shots)
                {
                    if (shot.Label == "spawn_ref") continue;
                    if (RenderOne(tempCamera, target, folder, shot, failures)) success++;
                }
            }
            finally
            {
                if (tempCamera != null)
                {
                    tempCamera.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(tempCamera.gameObject);
                }
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
                if (previousActive.IsValid())
                    EditorSceneManager.SetActiveScene(previousActive);
                if (tempScene.IsValid())
                    EditorSceneManager.CloseScene(tempScene, true);
            }

            if (failures.Count > 0)
            {
                Debug.LogError($"JURASSIC_ACCEPTANCE_SHOTS_FAIL: 成功 {success} 张，失败 {failures.Count} 张：" +
                               string.Join("；", failures.Take(8)));
                return;
            }
            Debug.Log($"JURASSIC_ACCEPTANCE_SHOTS_OK: {success} 张静帧 → {folder}（眼高 {eyeHeight:F2}m，FOV {fov:F1}，{ShotWidth}×{ShotHeight}）。" +
                      "请先人工确认 spawn_ref.png 与真实 Game View 构图一致，再采信近距离静帧结论。");
        }

        // ================================================================ 机位计划

        private struct ShotPlan
        {
            internal string Label;
            internal string Group;
            internal Vector3 Eye;
            internal Vector3 LookAt;
            internal string Note;
        }

        /// <summary>
        /// 玩家可达矩形（不再假定为 ±limit 正方盒）：来源 = metrics 的 constants.playerBoundsMin/Max
        /// （由 ProductionAcceptance 从 WorldBounds 采集），缺失时回退 WorldBounds 常量本身。
        /// </summary>
        private struct PlayerBox
        {
            internal float MinX, MaxX, MinZ, MaxZ;

            internal Vector2 Clamp(Vector2 p) => new Vector2(
                Mathf.Clamp(p.x, MinX, MaxX), Mathf.Clamp(p.y, MinZ, MaxZ));

            internal float DistanceTo(float x, float z)
            {
                float dx = Mathf.Max(Mathf.Max(MinX - x, x - MaxX), 0f);
                float dz = Mathf.Max(Mathf.Max(MinZ - z, z - MaxZ), 0f);
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            internal string Describe() => $"x[{MinX:F0},{MaxX:F0}] z[{MinZ:F0},{MaxZ:F0}]";

            internal static PlayerBox FromMetrics(Dictionary<string, string> metrics)
            {
                Rect fallbackRect = WorldBounds.PlayerExplorationXZ;
                var box = new PlayerBox
                {
                    MinX = fallbackRect.xMin,
                    MaxX = fallbackRect.xMax,
                    MinZ = fallbackRect.yMin,
                    MaxZ = fallbackRect.yMax,
                };
                if (TryParseXZ(Get(metrics, "constants.playerBoundsMin"), out Vector2 min) &&
                    TryParseXZ(Get(metrics, "constants.playerBoundsMax"), out Vector2 max))
                {
                    box.MinX = min.x; box.MinZ = min.y;
                    box.MaxX = max.x; box.MaxZ = max.y;
                }
                return box;
            }
        }

        private static List<ShotPlan> BuildShotPlan(Dictionary<string, string> metrics, Scene mainScene,
            Terrain terrain, float eyeHeight)
        {
            var shots = new List<ShotPlan>();
            PlayerBox box = PlayerBox.FromMetrics(metrics);
            Debug.Log($"[Shots] 玩家可达矩形 {box.Describe()}（不再假定 ±limit 正方盒）。");

            // 0) 出生点参考帧（与 BuildCamera 一致：位置 (0, eye, -16)，朝 +Z）
            var spawn = new Vector3(0f, eyeHeight, -16f);
            shots.Add(new ShotPlan
            {
                Label = "spawn_ref",
                Group = "spawn",
                Eye = spawn,
                LookAt = spawn + Vector3.forward * 10f,
                Note = "玩家出生点参考帧：必须与 Play Mode Game View 构图一致",
            });

            // 1) fern：最密格近旁（朝向外侧，与玩家在林缘的视向一致）
            AddTargetSeries(shots, metrics, "viewPlan.densestFern", "fern", "最密 fern 格",
                new[] { 5f, 30f }, box, eyeHeight);

            // 2) rock：最近外围岩石簇
            AddTargetSeries(shots, metrics, "viewPlan.nearestUnderstoryRock", "rock", "最近 rock 簇",
                new[] { 8f }, box, eyeHeight);

            // 3) deadwood：最近 stump / fallen log，多距离留证 LOD0→LOD3
            ShotPlan? stump = NearestNamedInstance(mainScene, "Stump_", box, eyeHeight);
            ShotPlan? log = NearestNamedInstance(mainScene, "FallenLog_", box, eyeHeight);
            if (stump.HasValue) AddDistanceSeries(shots, stump.Value, "deadwood_stump", DeadwoodLodDistances);
            if (log.HasValue) AddDistanceSeries(shots, log.Value, "deadwood_log", DeadwoodLodDistances);

            // 4) central：最近中央大岩石
            AddTargetSeries(shots, metrics, "viewPlan.nearestCentralRock", "central", "最近中央岩石",
                new[] { 8f }, box, eyeHeight);

            // 5) 可达分区（与 Play Mode 目视同一构图）
            shots.Add(Zone("zone1_central_east", new Vector2(0f, 0f), 90f, eyeHeight, "中央开阔区朝 +X（东）"));
            shots.Add(Zone("zone2_edge_east", new Vector2(45f, 0f), 90f, eyeHeight, "东侧林缘过渡"));
            shots.Add(Zone("zone3_forest_east", new Vector2(47f, 20f), 90f, eyeHeight, "东侧密林内部（自边界观察）"));
            shots.Add(Zone("zone5_rock_observer", ObserverOf(metrics, "viewPlan.nearestUnderstoryRock", box, eyeHeight),
                BearingOf(metrics, "viewPlan.nearestUnderstoryRock", 0f), eyeHeight, "最近 rock 簇的玩家可达站位"));
            shots.Add(Zone("zone6_deadwood_observer", ObserverOf(metrics, "viewPlan.nearestDeadwood", box, eyeHeight),
                BearingOf(metrics, "viewPlan.nearestDeadwood", 0f), eyeHeight, "最近 deadwood 的玩家可达站位"));

            // 5b) 本次新增：R0 空地 → R1 稀疏低矮植被（站空地东缘朝东看梯度起始）
            shots.Add(Zone("zone8_clearing_edge_east",
                new Vector2(WorldBounds.DinosaurActivityXZ.xMax - WorldBounds.ClearingInset + 4f, 0f),
                90f, eyeHeight,
                $"R0 开阔空地东缘（活动包络内缩 {WorldBounds.ClearingInset:F0}m）朝东：空地 → R1 稀疏低矮植被"));
            // 5c) 本次新增：站玩家探索范围西侧外缘（已在平坦区外 8m）朝东，验证相机贴地 + 梯度
            shots.Add(Zone("zone9_player_bound_west", new Vector2(box.MinX + 2f, 0f), 90f, eyeHeight,
                $"玩家探索范围西缘（x={box.MinX:F0}，平坦区外 {WorldBounds.PlayerOutwardMargin:F0}m）朝东：" +
                "R1 → R2 玩家过渡带 → R3 林缘；此机位地形高 ≠ 0，是「相机贴地」的直接判据"));

            // 6) 视觉通道：沿 35°/160°/285° 三条通道，站在可达边界最深处顺向观察
            for (int i = 0; i < ForestSeeder.ChannelAzimuths.Length; i++)
            {
                float azimuth = ForestSeeder.ChannelAzimuths[i];
                Vector2 dir = ChannelDir(azimuth);
                Vector2 observer = BoundaryPointAlongRay(ForestSeeder.ChannelOrigin, dir, box);
                shots.Add(Zone($"zone7_channel_{Mathf.RoundToInt(azimuth)}", observer, azimuth, eyeHeight,
                    $"视觉通道 {azimuth}°（方位角自正北 +z 顺时针）"));
                // 通道边缘：站同一点、朝垂直方向看，观察植被斑驳过渡（zone4 泥斑/草地交界的可达替代视角）
                if (i == 0)
                {
                    shots.Add(Zone("zone4_channel_edge", observer, azimuth + 90f, eyeHeight,
                        "视觉通道边缘：植被由密转疏的过渡带"));
                }
            }

            // 7) 展示环：站在可达边界最近点正对每个近林带道具位（"能不能看见岩石/倒木/树桩"的直接判据）
            if (UnderstorySeeder.PassEnabled)
                AddShowcaseShots(shots, box, eyeHeight);

            // 8) 眼高贴合地形：玩家范围已越出地形平坦区（西/东/南外扩 8m），固定 y = eyeHeight 会让外缘机位
            //    悬空 / 入地，判据失真。统一改成 y = 地形高度 + eyeHeight —— 与 SmoothExplorerCamera 运行期行为一致
            //    （spawn_ref 在平坦区内，地形高 0，故与 BuildCamera 的构图仍逐字一致）。
            for (int i = 0; i < shots.Count; i++)
            {
                ShotPlan shot = shots[i];
                shot.Eye = new Vector3(shot.Eye.x, GroundY(terrain, shot.Eye) + eyeHeight, shot.Eye.z);
                shot.LookAt = new Vector3(shot.LookAt.x, GroundY(terrain, shot.LookAt) + eyeHeight, shot.LookAt.z);
                shots[i] = shot;
            }

            return shots;
        }

        /// <summary>地形高度（米，世界 Y）——与 SmoothExplorerCamera.SampleGroundHeight 同一算法，保证机位与运行期一致。</summary>
        private static float GroundY(Terrain terrain, Vector3 worldPosition)
        {
            if (terrain == null)
                return 0f;
            return terrain.SampleHeight(worldPosition);
        }

        private static void AddTargetSeries(List<ShotPlan> shots, Dictionary<string, string> metrics, string keyPrefix,
            string group, string note, float[] distances, PlayerBox box, float eyeHeight)
        {
            if (!TryParseXZ(Get(metrics, keyPrefix + ".targetXZ"), out Vector2 target))
            {
                Debug.LogWarning($"[Shots] metrics 缺少 {keyPrefix}.targetXZ：跳过 {group} 组静帧。");
                return;
            }
            Vector2 towardCenter = -target.normalized;
            if (towardCenter.sqrMagnitude < 0.0001f) towardCenter = Vector2.down;
            foreach (float distance in distances)
            {
                // 观察点必须落在玩家真正可达的范围内（矩形，非正方盒）
                Vector2 eye = box.Clamp(target + towardCenter * distance);
                shots.Add(new ShotPlan
                {
                    Label = $"{group}_{Mathf.RoundToInt(distance)}m",
                    Group = group,
                    Eye = new Vector3(eye.x, eyeHeight, eye.y),
                    LookAt = new Vector3(target.x, eyeHeight, target.y),
                    Note = $"{note}，视距 {distance:F0}m（{Get(metrics, keyPrefix + ".detail")}）",
                });
            }
        }

        private static void AddDistanceSeries(List<ShotPlan> shots, ShotPlan template, string group, float[] distances)
        {
            Vector2 target = new Vector2(template.LookAt.x, template.LookAt.z);
            foreach (float distance in distances)
            {
                Vector2 towardCenter = -target.normalized;
                if (towardCenter.sqrMagnitude < 0.0001f) towardCenter = Vector2.down;
                Vector2 eye = target + towardCenter * distance;
                shots.Add(new ShotPlan
                {
                    Label = $"{group}_{Mathf.RoundToInt(distance)}m",
                    Group = group,
                    Eye = new Vector3(eye.x, template.Eye.y, eye.y),
                    LookAt = new Vector3(target.x, template.Eye.y, target.y),
                    Note = $"{template.Note}，视距 {distance:F0}m（LOD 留证）",
                });
            }
        }

        /// <summary>
        /// 展示环取证机位：站在可达范围内**距该道具最近的点**正对每个近林带道具位（与人工 Play Mode 验收同一构图）。
        /// 玩家探索范围已扩到平坦区外 8m，故「贴边界站的那一步」比修复前更靠近道具（视距更短）——
        /// 这正是本次空间重平衡的直接可验结果。
        /// </summary>
        private static void AddShowcaseShots(List<ShotPlan> shots, PlayerBox box, float eyeHeight)
        {
            int index = 0;
            foreach (UnderstorySeeder.ShowcaseSpot spot in UnderstorySeeder.BuildShowcaseRing())
            {
                // 玩家可达范围内距道具最近的点 = 把道具位置钳进可达矩形（道具恒在矩形外）
                Vector2 eye = box.Clamp(spot.Center);
                float distance = Vector2.Distance(eye, spot.Center);
                shots.Add(new ShotPlan
                {
                    Label = $"showcase_{index:D2}_{spot.Kind.ToString().ToLowerInvariant()}",
                    Group = "showcase",
                    Eye = new Vector3(eye.x, eyeHeight, eye.y),
                    LookAt = new Vector3(spot.Center.x, eyeHeight, spot.Center.y),
                    Note = $"近林带展示位 #{index}（{spot.Kind}，距活动包络 {spot.FlatDistance:F1}m）：站可达范围最近点正对，" +
                           $"视线 {distance:F1}m",
                });
                index++;
            }
        }

        private static ShotPlan? NearestNamedInstance(Scene scene, string namePrefix, PlayerBox box, float eyeHeight)        {
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!transform.name.StartsWith(namePrefix, StringComparison.Ordinal)) continue;
                    Vector3 position = transform.position;
                    float distance = box.DistanceTo(position.x, position.z);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = transform;
                }
            }
            if (best == null)
            {
                Debug.LogWarning($"[Shots] 场景中找不到 \"{namePrefix}*\" 实例：跳过该组静帧。");
                return null;
            }
            return new ShotPlan
            {
                Label = namePrefix.TrimEnd('_'),
                Group = namePrefix.TrimEnd('_'),
                Eye = new Vector3(best.position.x, eyeHeight, best.position.z),
                LookAt = new Vector3(best.position.x, eyeHeight, best.position.z),
                Note = $"最近 \"{namePrefix}*\" 实例 {best.name}",
            };
        }

        private static ShotPlan Zone(string label, Vector2 xz, float bearingDeg, float eyeHeight, string note)
        {
            Vector2 dir = ChannelDir(bearingDeg);
            var eye = new Vector3(xz.x, eyeHeight, xz.y);
            return new ShotPlan
            {
                Label = label,
                Group = "zone",
                Eye = eye,
                LookAt = eye + new Vector3(dir.x, 0f, dir.y) * 10f,
                Note = note,
            };
        }

        private static Vector2 ObserverOf(Dictionary<string, string> metrics, string keyPrefix, PlayerBox box, float eyeHeight)
        {
            if (!TryParseXZ(Get(metrics, keyPrefix + ".observerXZ"), out Vector2 observer))
                return Vector2.zero;
            return box.Clamp(observer);
        }

        private static float BearingOf(Dictionary<string, string> metrics, string keyPrefix, float fallback)
        {
            return float.TryParse(Get(metrics, keyPrefix + ".bearingDeg"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float bearing) ? bearing : fallback;
        }

        /// <summary>方位角 → 方向（与项目约定一致：0° = 正北 +z，顺时针转向 +x）。</summary>
        private static Vector2 ChannelDir(float azimuthDeg)
        {
            float rad = azimuthDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)).normalized;
        }

        /// <summary>沿射线从 origin 走到可达矩形边界的点。</summary>
        private static Vector2 BoundaryPointAlongRay(Vector2 origin, Vector2 dir, PlayerBox box)
        {
            float tMax = float.MaxValue;
            if (Mathf.Abs(dir.x) > 0.0001f)
                tMax = Mathf.Min(tMax, ((dir.x > 0f ? box.MaxX : box.MinX) - origin.x) / dir.x);
            if (Mathf.Abs(dir.y) > 0.0001f)
                tMax = Mathf.Min(tMax, ((dir.y > 0f ? box.MaxZ : box.MinZ) - origin.y) / dir.y);
            if (tMax == float.MaxValue || tMax < 0f) tMax = 0f;
            return box.Clamp(origin + dir * tMax);
        }

        // ================================================================ 渲染

        private static bool RenderOne(Camera camera, RenderTexture target, string folder, ShotPlan shot,
            List<string> failures)
        {
            try
            {
                Vector3 eye = new Vector3(shot.Eye.x, shot.Eye.y, shot.Eye.z);
                Vector3 lookAt = shot.LookAt;
                Vector3 forward = lookAt - eye;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                camera.transform.position = eye;
                camera.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, ShotWidth, ShotHeight), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;

                string path = Path.Combine(folder, shot.Label + ".png");
                byte[] png = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);
                if (png == null || png.Length == 0)
                {
                    failures.Add($"{shot.Label}(编码为空)");
                    return false;
                }
                File.WriteAllBytes(path, png);

                float bearing = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                float distance = forward.magnitude;
                Debug.Log($"[Shots] {shot.Label}: eye=({eye.x:F2},{eye.y:F2},{eye.z:F2}) bearing={bearing:F1}° " +
                          $"目标距离={distance:F1}m → {path}｜{shot.Note}");
                return true;
            }
            catch (Exception e)
            {
                failures.Add($"{shot.Label}({e.GetType().Name})");
                Debug.LogWarning($"[Shots] {shot.Label} 渲染失败：{e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 把主场景的 RenderSettings 复刻进临时场景：临时场景是空的，若不复刻会渲染出无天空/无环境光的画面。
        /// 只写临时场景的设置，主场景的 lighting 设置不被修改（RenderSettings 是 per-scene 的）。
        /// </summary>
        private static void CopyRenderSettingsInto(Scene tempScene)
        {
            // 注意：必须先读取（此前 Main.unity 是 active），再切换到临时场景后写入。
            Material skybox = RenderSettings.skybox;
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientSky = RenderSettings.ambientSkyColor;
            Color ambientEquator = RenderSettings.ambientEquatorColor;
            Color ambientGround = RenderSettings.ambientGroundColor;
            Color ambientLight = RenderSettings.ambientLight;
            float ambientIntensity = RenderSettings.ambientIntensity;
            bool fog = RenderSettings.fog;
            FogMode fogMode = RenderSettings.fogMode;
            Color fogColor = RenderSettings.fogColor;
            float fogDensity = RenderSettings.fogDensity;
            float fogStart = RenderSettings.fogStartDistance;
            float fogEnd = RenderSettings.fogEndDistance;
            Light sun = RenderSettings.sun;
            DefaultReflectionMode reflectionMode = RenderSettings.defaultReflectionMode;
            Texture customReflection = RenderSettings.customReflectionTexture;

            RenderSettings.skybox = skybox;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.defaultReflectionMode = reflectionMode;
            if (customReflection != null) RenderSettings.customReflectionTexture = customReflection;
            if (sun != null) RenderSettings.sun = sun;
            DynamicGI.UpdateEnvironment();

            Debug.Log($"[Shots] 临时场景 RenderSettings：skybox={(skybox != null ? skybox.name : "<none>")} " +
                      $"ambientMode={ambientMode} ambientIntensity={ambientIntensity:F2} fog={fog} sun={(sun != null ? sun.name : "<none>")}");
        }

        // ================================================================ 工具

        private static Camera FindPlayerCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Camera camera = root.GetComponentInChildren<Camera>(true);
                if (camera != null && camera.gameObject.CompareTag("MainCamera")) return camera;
            }
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Camera camera = root.GetComponentInChildren<Camera>(true);
                if (camera != null) return camera;
            }
            return null;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        private static Dictionary<string, string> ReadMetrics(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrEmpty(raw)) continue;
                int tab = raw.IndexOf('\t');
                if (tab <= 0) continue;
                dict[raw.Substring(0, tab)] = raw.Substring(tab + 1);
            }
            return dict;
        }

        private static string Get(Dictionary<string, string> dict, string key) =>
            dict.TryGetValue(key, out string value) ? value : "";

        private static bool TryParseXZ(string value, out Vector2 result)
        {
            result = Vector2.zero;
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split(',');
            if (parts.Length < 2) return false;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            result = new Vector2(x, y);
            return true;
        }

        private static string FullPath(string assetRelativePath)
        {
            if (assetRelativePath.StartsWith("Assets/", StringComparison.Ordinal))
                return Path.GetFullPath(Path.Combine(Application.dataPath,
                    assetRelativePath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)));
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
                assetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
