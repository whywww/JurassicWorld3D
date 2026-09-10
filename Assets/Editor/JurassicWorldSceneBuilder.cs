using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JurassicWorld.Editor
{
    // 编辑态共享建造器：把原 JurassicWorldBootstrap 在运行时凭代码创建的全部内容（灯光、草地、相机、
    // HUD、5 只恐龙）迁移为可在【不点 Play】的编辑态调用的静态建造方法。所有硬编码参数严格对照
    // Bootstrap 原值迁移（草地岩石固定随机种子 271828、Agent 漫游种子仍由 DinosaurAgent 内部持有）。
    //
    // 关键约定：
    //  - 恐龙模型用 PrefabUtility.InstantiatePrefab 实例化为子物体，DinosaurModelFitter.Fit 只在烘焙时
    //    计算一次并写入 transform（运行期绑定器不再调用 Fit）。
    //  - 材质烘焙为 Assets/Materials/Dinosaurs/*.mat 资产并赋给 Renderer（替代运行时 new Material）。
    //  - 各组件的可配置参数通过 [SerializeField] 私有字段写入，字段名与运行期脚本严格一致（见 SetField）。
    public static class JurassicWorldSceneBuilder
    {
        private const int GroundRandomSeed = 271828;
        private const string MaterialFolder = "Assets/Materials";
        private const string DinosaurMaterialFolder = "Assets/Materials/Dinosaurs";

        // 建造入口：清空当前场景后重建全部内容（幂等，避免与已烘焙对象重复）。
        public static void Build()
        {
            ClearActiveScene();
            Font uiFont = LoadBuiltinFont();
            Camera camera = BuildCamera();
            BuildLighting();
            BuildGround();
            BuildHud(uiFont);
            BuildDinosaurs(camera, uiFont);
        }

        // ---------------------------------------------------------------- 场景清理

        private static void ClearActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;
            foreach (GameObject root in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(root);
        }

        // ---------------------------------------------------------------- 相机

        private static Camera BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();

            camera.transform.position = new Vector3(0f, 3f, -16f);
            camera.transform.rotation = Quaternion.Euler(2f, 0f, 0f);
            camera.fieldOfView = 65f;
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 150f;
            camera.clearFlags = CameraClearFlags.Skybox;

            SmoothExplorerCamera explorer = cameraObject.AddComponent<SmoothExplorerCamera>();
            SetField(explorer, "moveSpeed", 6f);
            SetField(explorer, "acceleration", 6f);
            SetField(explorer, "turnSpeed", 60f);
            SetField(explorer, "cameraFollowSpeed", 3.5f);
            SetField(explorer, "lookSensitivity", 65f);
            SetField(explorer, "initialPitch", 4f);
            EditorUtility.SetDirty(explorer);
            return camera;
        }

        // ---------------------------------------------------------------- 灯光 / 天空

        private static void BuildLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.66f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.42f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.20f, 0.12f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.58f, 0.70f, 0.66f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 105f;

            Material skybox = EnsureSkyboxMaterial();
            if (skybox != null)
                RenderSettings.skybox = skybox;

            var sunObject = new GameObject("Sun");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.91f, 0.74f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sunObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
        }

        private static Material EnsureSkyboxMaterial()
        {
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader == null)
                return null;

            EnsureFolder(MaterialFolder);
            const string path = MaterialFolder + "/JurassicSkybox.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(skyShader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = skyShader;
            }

            material.SetColor("_SkyTint", new Color(0.40f, 0.58f, 0.76f));
            material.SetColor("_GroundColor", new Color(0.20f, 0.26f, 0.16f));
            material.SetFloat("_AtmosphereThickness", 0.75f);
            material.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- 草地 + 岩石

        private static void BuildGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Grassland";
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            ground.GetComponent<Renderer>().sharedMaterial =
                EnsureColorMaterial(MaterialFolder + "/Grassland.mat", new Color(0.19f, 0.39f, 0.12f), 0.05f);

            Material rockMaterial = EnsureColorMaterial(MaterialFolder + "/Rock.mat", new Color(0.28f, 0.31f, 0.22f), 0f);
            var random = new System.Random(GroundRandomSeed);
            for (int i = 0; i < 26; i++)
            {
                float x = (float)(random.NextDouble() * 88.0 - 44.0);
                float z = (float)(random.NextDouble() * 88.0 - 44.0);
                if (new Vector2(x, z).magnitude < 10f)
                    continue;

                GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = "Rock";
                rock.transform.position = new Vector3(x, 0.25f, z);
                rock.transform.localScale = new Vector3(1.4f, 0.5f, 1.1f) * (float)(0.6 + random.NextDouble());
                rock.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                rock.GetComponent<Renderer>().sharedMaterial = rockMaterial;
                Collider collider = rock.GetComponent<Collider>();
                if (collider != null)
                    UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        // ---------------------------------------------------------------- HUD

        private static void BuildHud(Font font)
        {
            var root = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            Text help = MakeText(root.transform, "Help", TextAnchor.LowerLeft, 24, font);
            RectTransform helpRect = help.rectTransform;
            helpRect.anchorMin = new Vector2(0f, 0f);
            helpRect.anchorMax = new Vector2(0f, 0f);
            helpRect.pivot = new Vector2(0f, 0f);
            helpRect.anchoredPosition = new Vector2(34f, 30f);
            helpRect.sizeDelta = new Vector2(820f, 100f);
            help.text = "W/S：前进/后退    A/D：转向并前进    按住鼠标右键：自由观察\n空格键：召唤画面中的恐龙";

            Text status = MakeText(root.transform, "Status", TextAnchor.UpperRight, 24, font);
            RectTransform statusRect = status.rectTransform;
            statusRect.anchorMin = new Vector2(1f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(1f, 1f);
            statusRect.anchoredPosition = new Vector2(-34f, -30f);
            statusRect.sizeDelta = new Vector2(980f, 90f);
            status.text = "正在加载 5 种恐龙模型…";

            Text error = MakeText(root.transform, "Error", TextAnchor.UpperCenter, 27, font);
            error.color = new Color(1f, 0.45f, 0.35f);
            RectTransform errorRect = error.rectTransform;
            errorRect.anchorMin = new Vector2(0.5f, 1f);
            errorRect.anchorMax = new Vector2(0.5f, 1f);
            errorRect.pivot = new Vector2(0.5f, 1f);
            errorRect.anchoredPosition = new Vector2(0f, -30f);
            errorRect.sizeDelta = new Vector2(1300f, 100f);

            HudController hud = root.AddComponent<HudController>();
            SetField(hud, "errorText", error);
            SetField(hud, "statusText", status);
            SetField(hud, "helpText", help);
            SetField(hud, "expectedDinosaurCount", 5);
            EditorUtility.SetDirty(hud);
        }

        private static Text MakeText(Transform parent, string name, TextAnchor alignment, int size, Font font)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Font LoadBuiltinFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                Debug.LogError("[SceneBuilder] 无法加载内置字体（LegacyRuntime.ttf / Arial.ttf），HUD 与信息卡文字可能缺失。");
            return font;
        }

        // ---------------------------------------------------------------- 恐龙

        private static void BuildDinosaurs(Camera camera, Font font)
        {
            foreach (DinosaurTemplate template in CreateTemplates())
                BuildDinosaur(template, camera, font);
        }

        private static void BuildDinosaur(DinosaurTemplate t, Camera camera, Font font)
        {
            var root = new GameObject(t.objectName);
            root.transform.position = t.position;

            DinosaurAgent agent = root.AddComponent<DinosaurAgent>();
            SetField(agent, "viewCamera", camera);
            SetField(agent, "displayName", t.displayName);
            SetField(agent, "movementHeight", t.position.y);
            SetField(agent, "viewFocusHeight", t.focusHeight);
            SetField(agent, "visualYaw", t.yaw);
            SetField(agent, "walkSpeed", t.walkSpeed);
            SetField(agent, "moveAnimationGroundSpeed", t.walkSpeed);
            SetField(agent, "approachSpeed", Mathf.Max(t.walkSpeed * 1.35f, t.walkSpeed + 0.5f));
            EditorUtility.SetDirty(agent);

            var visualRoot = new GameObject(t.displayName + " Visual Root");
            visualRoot.transform.SetParent(root.transform, false);

            GameObject model = InstantiateModel(t, visualRoot.transform);
            if (model != null)
                ApplyBakedMaterials(model, t);

            // Fit 只在烘焙时算一次并把缩放/位置写入 transform（非幂等，运行期绑定器不再调用）。
            if (model != null && t.targetLength > 0f &&
                !DinosaurModelFitter.Fit(visualRoot, root.transform, t.targetLength, t.alignBottom))
            {
                Debug.LogError("[SceneBuilder] " + t.displayName + " 无法计算模型尺寸（Fit 失败）。");
            }

            // 动画姿势感知接地：Fit 用绑定姿势 AABB 对齐地面，运行期播放动画后脚会悬空；
            // 这里在烘焙时采样 idle 姿势，用形变后网格最低点把 visualRoot 绝对对齐到地面 y。
            if (model != null && t.alignBottom && t.enableFootLock)
                GroundFeetByAnimation(visualRoot, model, root.transform, t);

            DinosaurFbxLoader loader = root.AddComponent<DinosaurFbxLoader>();
            SetField(loader, "modelRoot", visualRoot);
            SetField(loader, "displayName", t.displayName);
            SetField(loader, "idleAnimationHint", t.idleHint);
            SetField(loader, "moveAnimationHint", t.moveHint);
            SetField(loader, "enableFootLock", t.enableFootLock);
            SetField(loader, "footBoneNames", t.footBoneNames);
            SetField(loader, "footLockJointDepth", t.footLockJointDepth);
            SetField(loader, "releaseFootLockOnRise", t.releaseFootLockOnRise);
            SetField(loader, "footContactHeight", t.footContactHeight);
            SetField(loader, "footRiseThreshold", t.footRiseThreshold);
            SetField(loader, "footLockDistanceLimit", t.footLockDistanceLimit);
            EditorUtility.SetDirty(loader);

            BuildInfoCard(root.transform, camera, agent, t.description, t.cardPosition, font);
        }

        // 动画姿势感知接地校正（仅烘焙期执行一次）：采样 Legacy 动画姿势，用形变后网格最低点
        // 把 visualRoot 绝对对齐到地面 y，只加位移、绝不重复 Fit、绝不改缩放。
        private static void GroundFeetByAnimation(GameObject visualRoot, GameObject model, Transform rootAnchor, DinosaurTemplate t)
        {
            // 1) 取动画组件（Legacy Animation）。
            Animation animation = model.GetComponentInChildren<Animation>(true);
            if (animation == null)
            {
                Debug.LogWarning("[GroundFeet] " + t.displayName + " 缺少 Legacy Animation，跳过接地校正。");
                return;
            }

            // 2) 解析接地片段（镜像 DinosaurAgent.FindClip：OrdinalIgnoreCase 匹配、排除 _RM 根运动）。
            //    运行期 BindAnimation 会同时解析并播放 idleClip 与 moveClip（CrossFade 混合），故这里不短路：
            //    idle 与 move 都采样并取全局最低点，避免行走切到 move 且其最低点更低时脚/身体穿入草地。
            AnimationClip idleClip = ResolveGroundClip(animation, new[] { t.idleHint, "idle", "stand", "wait" });
            if (idleClip == null)
                idleClip = ResolveFirstClip(animation); // idle 缺失时用第一个片段兜底
            AnimationClip moveClip = ResolveGroundClip(animation, new[] { t.moveHint, "run", "walk", "fly", "animation" });

            // 组装待采样片段列表：idleClip 优先，moveClip 若与 idleClip 不同则加入（去重）。
            var clips = new List<AnimationClip>();
            if (idleClip != null)
                clips.Add(idleClip);
            if (moveClip != null && moveClip != idleClip)
                clips.Add(moveClip);
            if (clips.Count == 0)
            {
                Debug.LogWarning("[GroundFeet] " + t.displayName + " 未解析到可用动画片段，保留 Fit 结果，跳过接地校正。");
                return;
            }

            // 3) 地面高度基准（地面恐龙为 0）。
            float groundY = rootAnchor.position.y;

            // 资源缓存：整个流程只取一次 Renderer[]/SkinnedMeshRenderer[] 与一个临时 Mesh，循环内复用，减少每帧分配。
            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            SkinnedMeshRenderer[] smrs = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Mesh tempMesh = new Mesh();
            try
            {
                // 4) 绑定姿势自校准：进入 AnimationMode 前测两个基准，判定 bake 通道空间换算是否可信。
                float bindBoundsMinY = MeasureLowestWorldYByBounds(renderers);
                float bindBakeMinY = MeasureLowestWorldYByBake(smrs, tempMesh);
                float modelHeight = 0.001f;
                if (renderers.Length > 0)
                {
                    Bounds bindBounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bindBounds.Encapsulate(renderers[i].bounds);
                    modelHeight = Mathf.Max(0.001f, bindBounds.size.y);
                }
                bool bakeChannelValid = Mathf.Abs(bindBoundsMinY - bindBakeMinY) < 0.05f * modelHeight;
                if (!bakeChannelValid)
                    Debug.LogWarning($"[GroundFeet] {t.displayName}: bake 通道与 bounds 通道绑定姿势基准不一致" +
                        $"（bindBoundsMinY={bindBoundsMinY:F3} bindBakeMinY={bindBakeMinY:F3} modelHeight={modelHeight:F3}），bake 兜底不可用。");

                // 5) 多帧采样：对列表中每个片段整段采样，lowest 取所有片段所有帧的全局最小值。
                //    AnimationMode 全局态守卫：仅当此前不在动画模式时才由本方法开启/关闭，避免打断用户动画预览。
                float lowestBoundsY = float.MaxValue;
                float lowestBakeY = float.MaxValue;
                bool ownsAnimationMode = !AnimationMode.InAnimationMode();
                if (ownsAnimationMode)
                    AnimationMode.StartAnimationMode();
                try
                {
                    const int SampleCount = 24;
                    foreach (AnimationClip clip in clips)
                    {
                        // R8：clip.length<=0 时只采 time=0 一帧，避免除零/无意义循环；分母非零。
                        int steps = clip.length > 0f ? SampleCount : 0;
                        for (int i = 0; i <= steps; i++)
                        {
                            float time = steps > 0 ? clip.length * (i / (float)steps) : 0f;
                            AnimationMode.BeginSampling();
                            // 以 animation.gameObject 为绑定根，与运行期 Legacy 播放保持一致（用 model 可能静默得到绑定姿势）。
                            AnimationMode.SampleAnimationClip(animation.gameObject, clip, time);
                            AnimationMode.EndSampling(); // EndSampling 后姿态才落到 Transform
                            lowestBoundsY = Mathf.Min(lowestBoundsY, MeasureLowestWorldYByBounds(renderers));
                            if (bakeChannelValid)
                                lowestBakeY = Mathf.Min(lowestBakeY, MeasureLowestWorldYByBake(smrs, tempMesh));
                        }
                    }
                }
                finally
                {
                    if (ownsAnimationMode && AnimationMode.InAnimationMode())
                        AnimationMode.StopAnimationMode(); // 复位姿态，绝不把采样姿势烘进场景
                }

                // 6) 测量通道新鲜度判定：若 bounds 通道采样后仍等于绑定姿势基准，说明 Renderer.bounds 未刷新(stale)。
                float lowestY;
                string chosenChannel;
                bool boundsStale = lowestBoundsY != float.MaxValue && Mathf.Abs(lowestBoundsY - bindBoundsMinY) < 1e-4f;
                if (boundsStale)
                {
                    lowestY = lowestBakeY;
                    chosenChannel = "bake";
                }
                else
                {
                    lowestY = lowestBoundsY;
                    chosenChannel = "bounds";
                }
                if (lowestY == float.MaxValue)
                {
                    Debug.LogError("[GroundFeet] " + t.displayName + " bounds 与 bake 通道均无有效最低点，接地静默失效，保留 Fit 结果不做位移。");
                    return;
                }

                // 7) 绝对对齐：只加位移，绝不重复调用 Fit、绝不改缩放。
                //    dy 安全上限：若整段动画网格最低点误命中尾尖/颈部（长尾的阿根廷龙、霸王龙风险最高），
                //    会把整只模型抬高一个不受约束的量、脚反而更悬空，故超限则放弃位移。
                float dy = groundY - lowestY;
                float limit = Mathf.Max(modelHeight * 0.2f, 0.3f);
                bool dyClamped = Mathf.Abs(dy) > limit;
                if (dyClamped)
                {
                    Debug.LogWarning($"[GroundFeet] {t.displayName}: dy={dy:F3} 超出安全上限 {limit:F3}，动画最低点可能非足部，保留 Fit 结果不位移。");
                    return;
                }
                visualRoot.transform.position += Vector3.up * dy;

                // 8) 日志（验证证据）：含实际采样片段名、双基准、动画最低点、dy、所选通道、是否触发钳制。
                var clipNames = new List<string>();
                foreach (AnimationClip clip in clips)
                    clipNames.Add(clip.name);
                Debug.Log($"[GroundFeet] {t.displayName}: clips=[{string.Join(",", clipNames.ToArray())}] " +
                    $"bindBoundsMinY={bindBoundsMinY:F3} bindBakeMinY={bindBakeMinY:F3} animLowestY={lowestY:F3} " +
                    $"dy={dy:F3} channel={chosenChannel} dyClamped={dyClamped}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempMesh); // 统一释放一次，避免 Mesh 泄漏
            }
        }

        // 镜像 DinosaurAgent.FindClip 的匹配规则，但返回 AnimationClip：OrdinalIgnoreCase 匹配 hint，
        // 排除名字含 _RM 的根运动片段，优先返回非 _RM 匹配，其次 _RM 匹配；无匹配返回 null（不兜底 first）。
        private static AnimationClip ResolveGroundClip(Animation animation, string[] hints)
        {
            AnimationClip rootMotionMatch = null;
            foreach (AnimationState state in animation)
            {
                AnimationClip clip = state.clip;
                if (clip == null)
                    continue;
                foreach (string hint in hints)
                {
                    if (!string.IsNullOrEmpty(hint) && state.name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool isRootMotion = state.name.IndexOf("_RM", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isRootMotion)
                            return clip;
                        if (rootMotionMatch == null)
                            rootMotionMatch = clip;
                    }
                }
            }
            return rootMotionMatch;
        }

        // 兜底：取枚举到的第一个非空片段。
        private static AnimationClip ResolveFirstClip(Animation animation)
        {
            foreach (AnimationState state in animation)
                if (state.clip != null)
                    return state.clip;
            return null;
        }

        // bounds 通道：与 DinosaurModelFitter.Fit 完全相同的封装方式，返回合并包围盒最低世界 Y。
        // 接收调用方缓存的 Renderer[]，避免每帧 GetComponentsInChildren 分配。
        private static float MeasureLowestWorldYByBounds(Renderer[] rs)
        {
            if (rs == null || rs.Length == 0)
                return float.MaxValue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
                b.Encapsulate(rs[i].bounds);
            return b.min.y;
        }

        // bake 通道：对每个 SkinnedMeshRenderer 烘焙形变后网格，取包围盒 8 个角点经 localToWorldMatrix
        // 变换到世界，跨所有 smr 返回最低世界 Y。复用调用方缓存的 smr[] 与 tempMesh，避免每帧分配。
        private static float MeasureLowestWorldYByBake(SkinnedMeshRenderer[] smrs, Mesh tempMesh)
        {
            if (smrs == null || smrs.Length == 0)
                return float.MaxValue;

            float lowest = float.MaxValue;
            foreach (SkinnedMeshRenderer smr in smrs)
            {
                if (smr == null || smr.sharedMesh == null)
                    continue;
                // useScale=false：缩放仅由下方 localToWorldMatrix 施加一次，避免 S² 双重缩放。
                smr.BakeMesh(tempMesh, false);
                Bounds mb = tempMesh.bounds;
                Vector3 c = mb.center;
                Vector3 e = mb.extents;
                Matrix4x4 m = smr.transform.localToWorldMatrix;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = c + Vector3.Scale(e, new Vector3(sx, sy, sz));
                    Vector3 world = m.MultiplyPoint3x4(corner);
                    if (world.y < lowest)
                        lowest = world.y;
                }
            }
            return lowest;
        }

        private static GameObject InstantiateModel(DinosaurTemplate t, Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>(t.resourcePath);
            if (prefab == null)
            {
                Debug.LogError("[SceneBuilder] 未找到恐龙 FBX 资源：Resources/" + t.resourcePath +
                    "（若为并行导入任务尚未完成，请先导入素材再烘焙）。");
                return null;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            model.name = t.displayName + " Source Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, t.yaw, 0f);
            model.transform.localScale = Vector3.one;
            return model;
        }

        private static void ApplyBakedMaterials(GameObject model, DinosaurTemplate t)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Texture2D primaryAlbedo = LoadTexture(t.primaryAlbedo);
            Texture2D secondaryAlbedo = LoadTexture(t.secondaryAlbedo);
            Texture2D primaryNormal = LoadTexture(t.primaryNormal);
            Texture2D secondaryNormal = LoadTexture(t.secondaryNormal);
            Texture2D occlusion = LoadTexture(t.occlusion);

            Material primaryMaterial = EnsureDinosaurMaterial(t.displayName + " Primary",
                primaryAlbedo, primaryNormal, occlusion);
            Material secondaryMaterial = secondaryAlbedo != null
                ? EnsureDinosaurMaterial(t.displayName + " Secondary",
                    secondaryAlbedo, secondaryNormal != null ? secondaryNormal : primaryNormal, occlusion)
                : null;

            foreach (Renderer renderer in renderers)
            {
                Material[] sourceMaterials = renderer.sharedMaterials;
                var materials = new Material[sourceMaterials.Length];
                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    Material source = sourceMaterials[i];
                    bool useSecondary = secondaryMaterial != null &&
                        ((source != null && source.name.IndexOf("other", System.StringComparison.OrdinalIgnoreCase) >= 0) || i > 0);
                    materials[i] = useSecondary ? secondaryMaterial : primaryMaterial;
                }
                renderer.sharedMaterials = materials;
                renderer.enabled = true;
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                    skinnedRenderer.updateWhenOffscreen = true;
                EditorUtility.SetDirty(renderer);
            }
        }

        private static Material EnsureDinosaurMaterial(string assetName, Texture albedo, Texture normal, Texture occlusion)
        {
            EnsureFolder(DinosaurMaterialFolder);
            Material material = LoadOrCreateStandardMaterial(DinosaurMaterialFolder + "/" + assetName + ".mat");
            material.color = Color.white;
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.28f);

            material.SetTexture("_MainTex", albedo);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
            }

            if (occlusion != null)
            {
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetFloat("_OcclusionStrength", 0.65f);
            }
            else
            {
                material.SetTexture("_OcclusionMap", null);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureColorMaterial(string path, Color color, float smoothness)
        {
            EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            Material material = LoadOrCreateStandardMaterial(path);
            material.color = color;
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateStandardMaterial(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }

        private static Texture2D LoadTexture(string resourcePath)
        {
            return string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<Texture2D>(resourcePath);
        }

        // ---------------------------------------------------------------- 信息卡

        private static void BuildInfoCard(Transform dinosaur, Camera camera, DinosaurAgent agent,
            string description, Vector3 localPosition, Font font)
        {
            var root = new GameObject("Dinosaur Info Card", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root.transform.SetParent(dinosaur, false);
            root.transform.localPosition = localPosition;
            root.transform.localScale = Vector3.one * 0.012f;

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420f, 245f);

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            RectTransform bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(0.035f, 0.075f, 0.055f, 0.92f);

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(background.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 18f);
            textRect.offsetMax = new Vector2(-24f, -18f);
            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = 24;
            text.color = new Color(0.93f, 0.96f, 0.84f);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = description;

            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;

            DinosaurInfoCard card = root.AddComponent<DinosaurInfoCard>();
            SetField(card, "viewCamera", camera);
            SetField(card, "agent", agent);
            SetField(card, "canvasGroup", canvasGroup);
            SetField(card, "descriptionText", text);
            EditorUtility.SetDirty(card);
        }

        // ---------------------------------------------------------------- 反射写字段（字段名契约）

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Debug.LogError("[SceneBuilder] 字段契约不一致：" + target.GetType().Name + " 缺少字段 '" + fieldName + "'。");
                return;
            }
            field.SetValue(target, value);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ---------------------------------------------------------------- 恐龙参数模板（严格对照 Bootstrap 原值）

        private sealed class DinosaurTemplate
        {
            public string objectName;
            public string displayName;
            public string resourcePath;
            public Vector3 position;
            public float targetLength;
            public float yaw;
            public float focusHeight;
            public float walkSpeed;
            public string description;
            public Vector3 cardPosition;
            public string idleHint = "idle";
            public string moveHint = "run";
            public bool alignBottom = true;
            public bool enableFootLock = true;
            public string primaryAlbedo;
            public string secondaryAlbedo;
            public string primaryNormal;
            public string secondaryNormal;
            public string occlusion;
            public string[] footBoneNames;
            public int footLockJointDepth = 3;
            public bool releaseFootLockOnRise;
            public float footContactHeight = -1f;
            public float footRiseThreshold = -1f;
            public float footLockDistanceLimit = -1f;
        }

        private static List<DinosaurTemplate> CreateTemplates()
        {
            return new List<DinosaurTemplate>
            {
                // 霸王龙（原为 GLB，现改 FBX；体长按任务表迁移为 ~12，idle/move hint 沿用 idle/run）。
                new DinosaurTemplate
                {
                    objectName = "Tyrannosaurus Rex",
                    displayName = "霸王龙",
                    resourcePath = "Dinosaurs/TRex/source/T-Rex",
                    position = new Vector3(0f, 0f, 11f),
                    targetLength = 12f,
                    yaw = 0f,
                    focusHeight = 2.8f,
                    walkSpeed = 5.2f,
                    description = "霸王龙  Tyrannosaurus Rex\n\n生存年代：晚白垩世\n食性：肉食性\n体型：约 12–13 米\n\n强壮的顶级掠食者，拥有惊人的咬合力。",
                    cardPosition = new Vector3(4.5f, 5.8f, 0f),
                    idleHint = "idle",
                    moveHint = "run",
                    alignBottom = true,
                    enableFootLock = true,
                    primaryAlbedo = "Dinosaurs/TRex/textures/trexDiff",
                    primaryNormal = "Dinosaurs/TRex/textures/trexNormal",
                },

                // 风神翼龙（原为 GLB 翼龙，现改 FBX；飞行单位，EnableFootLock=false、alignBottom=false、y 抬高）。
                new DinosaurTemplate
                {
                    objectName = "Quetzalcoatlus",
                    displayName = "风神翼龙",
                    resourcePath = "Dinosaurs/Quetzalcoatlus/source/Quetzalcoatlus",
                    position = new Vector3(10f, 11f, 27f),
                    targetLength = 8f,
                    yaw = 180f,
                    focusHeight = 1.2f,
                    walkSpeed = 3.8f,
                    description = "风神翼龙  Quetzalcoatlus\n\n生存年代：晚白垩世\n食性：肉食性\n活动方式：飞行\n\n白垩纪晚期最大的飞行动物之一，翼展可达 10 米以上，以鱼类和小型动物为食。",
                    cardPosition = new Vector3(2.8f, 2.2f, 0f),
                    idleHint = "idle",
                    moveHint = "fly",
                    alignBottom = false,
                    enableFootLock = false,
                    primaryAlbedo = "Dinosaurs/Quetzalcoatlus/textures/b48",
                },

                // 胜王龙
                new DinosaurTemplate
                {
                    objectName = "Rajasaurus",
                    displayName = "胜王龙",
                    resourcePath = "Dinosaurs/Rajasaurus/source/Rajasaurus",
                    position = new Vector3(-10f, 0f, 19f),
                    targetLength = 9f,
                    yaw = 180f,
                    focusHeight = 2.4f,
                    walkSpeed = 1.6f,
                    description = "胜王龙  Rajasaurus\n\n生存年代：晚白垩世\n食性：肉食性\n分布：印度\n\n头骨短而厚，是印度次大陆特有的阿贝力龙类。",
                    cardPosition = new Vector3(3.8f, 5.0f, 0f),
                    idleHint = "idle",
                    moveHint = "walk",
                    primaryAlbedo = "Dinosaurs/Rajasaurus/textures/AlbedoMap",
                    occlusion = "Dinosaurs/Rajasaurus/textures/AmbientOcclusionMap",
                    footBoneNames = new[] { "Bone.L.007", "Bone.R.007" },
                    footLockJointDepth = 3,
                },

                // 伶盗龙
                new DinosaurTemplate
                {
                    objectName = "Velociraptor",
                    displayName = "伶盗龙",
                    resourcePath = "Dinosaurs/Velociraptor/Raptor/Raptor_Animated_FBX",
                    position = new Vector3(8f, 0f, 15f),
                    targetLength = 2.4f,
                    yaw = 180f,
                    focusHeight = 1.3f,
                    walkSpeed = 5.5f,
                    description = "伶盗龙  Velociraptor\n\n生存年代：晚白垩世\n食性：肉食性\n体型：约 2 米\n\n敏捷的小型猎手，后足具有醒目的镰刀状趾爪。",
                    cardPosition = new Vector3(2.3f, 2.8f, 0f),
                    idleHint = "idle",
                    moveHint = "run",
                    primaryAlbedo = "Dinosaurs/Velociraptor/Raptor/Textures/Raptor_PatternA_Albedo",
                    secondaryAlbedo = "Dinosaurs/Velociraptor/Raptor/Textures/Other_Albedo2 1K",
                    primaryNormal = "Dinosaurs/Velociraptor/Raptor/Textures/Raptor_Normal",
                    secondaryNormal = "Dinosaurs/Velociraptor/Raptor/Textures/Other_Normal 1K",
                    occlusion = "Dinosaurs/Velociraptor/Raptor/Textures/Raptor_AO",
                },

                // 阿根廷龙
                new DinosaurTemplate
                {
                    objectName = "Argentinosaurus",
                    displayName = "阿根廷龙",
                    resourcePath = "Dinosaurs/Argentinosaurus/source/Argentinosaurus",
                    position = new Vector3(-13f, 0f, 35f),
                    targetLength = 27f,
                    yaw = 180f,
                    focusHeight = 5.5f,
                    walkSpeed = 0.7f,
                    description = "阿根廷龙  Argentinosaurus\n\n生存年代：晚白垩世\n食性：植食性\n体型：约 30–35 米\n\n已知最大的陆生动物之一，拥有极长的颈部和尾部。",
                    cardPosition = new Vector3(8f, 10f, 0f),
                    idleHint = "idle",
                    moveHint = "walk",
                    primaryAlbedo = "Dinosaurs/Argentinosaurus/textures/AlbedoMap",
                    occlusion = "Dinosaurs/Argentinosaurus/textures/AmbientOcclutionMap",
                    footBoneNames = new[] { "Bone.l.002", "Bone.r.002", "Bone.l.006", "Bone.r.006" },
                    footLockJointDepth = 2,
                    releaseFootLockOnRise = true,
                    enableFootLock = true,
                    footContactHeight = 0.08f,
                    footRiseThreshold = 0.0005f,
                    footLockDistanceLimit = 0.75f,
                },
            };
        }
    }
}
