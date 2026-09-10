using UnityEngine;

namespace JurassicWorld
{
    public sealed class DinosaurFbxLoader : MonoBehaviour
    {
        public string ResourcePath { get; set; }
        public string DisplayName { get; set; }
        public float TargetLength { get; set; } = 8f;
        public float ModelYaw { get; set; }
        public string IdleAnimationHint { get; set; } = "idle";
        public string MoveAnimationHint { get; set; } = "run";
        public string PrimaryAlbedoPath { get; set; }
        public string SecondaryAlbedoPath { get; set; }
        public string PrimaryNormalPath { get; set; }
        public string SecondaryNormalPath { get; set; }
        public string OcclusionPath { get; set; }
        public string[] FootBoneNames { get; set; }
        public int FootLockJointDepth { get; set; } = 3;
        public bool ReleaseFootLockOnRise { get; set; }
        public bool EnableFootLock { get; set; } = true;
        public float FootContactHeight { get; set; } = -1f;
        public float FootRiseThreshold { get; set; } = -1f;
        public float FootLockDistanceLimit { get; set; } = -1f;
        public bool IsLoaded { get; private set; }
        private GameObject sourceModel;

        private void Start()
        {
            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Fail("未找到恐龙模型资源：" + ResourcePath);
                return;
            }

            var visualRoot = new GameObject(DisplayName + " Visual Root");
            visualRoot.transform.SetParent(transform, false);

            GameObject model = Instantiate(prefab, visualRoot.transform);
            sourceModel = model;
            model.name = DisplayName + " Source Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
            model.transform.localScale = Vector3.one;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            ApplyMaterials(renderers);
            foreach (Renderer renderer in renderers)
            {
                renderer.enabled = true;
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                    skinnedRenderer.updateWhenOffscreen = true;
            }

            if (!DinosaurModelFitter.Fit(visualRoot, transform, TargetLength, true))
            {
                Fail(DisplayName + " 无法计算模型尺寸。");
                return;
            }

            Bounds fittedBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                fittedBounds.Encapsulate(renderers[i].bounds);
            Debug.Log("DINO_VISIBLE " + DisplayName + " center=" + fittedBounds.center.ToString("F2")
                + " size=" + fittedBounds.size.ToString("F2"), this);

            Animation animation = model.GetComponentInChildren<Animation>(true);
            if (animation == null)
            {
                Fail(DisplayName + " 没有可播放的 Legacy 动画。请检查模型导入设置。");
                return;
            }

            animation.cullingType = AnimationCullingType.AlwaysAnimate;

            IsLoaded = true;
            DinosaurAgent agent = GetComponent<DinosaurAgent>();
            agent.BindAnimation(animation, IdleAnimationHint, MoveAnimationHint);
            if (EnableFootLock)
            {
                gameObject.AddComponent<DinosaurFootLock>().Bind(
                    visualRoot.transform, agent, FootBoneNames, FootLockJointDepth, ReleaseFootLockOnRise,
                    FootContactHeight, FootRiseThreshold, FootLockDistanceLimit);
            }
            HudController.ReportLoaded(DisplayName);
        }

        private void LateUpdate()
        {
            if (sourceModel == null)
                return;

            sourceModel.transform.localPosition = Vector3.zero;
            sourceModel.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
            sourceModel.transform.localScale = Vector3.one;
        }

        private void ApplyMaterials(Renderer[] renderers)
        {
            Shader shader = Shader.Find("Standard");
            Texture2D primaryAlbedo = LoadTexture(PrimaryAlbedoPath);
            Texture2D secondaryAlbedo = LoadTexture(SecondaryAlbedoPath);
            Texture2D primaryNormal = LoadTexture(PrimaryNormalPath);
            Texture2D secondaryNormal = LoadTexture(SecondaryNormalPath);
            Texture2D occlusion = LoadTexture(OcclusionPath);

            foreach (Renderer renderer in renderers)
            {
                Material[] sourceMaterials = renderer.sharedMaterials;
                var materials = new Material[sourceMaterials.Length];
                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    Material source = sourceMaterials[i];
                    bool useSecondary = secondaryAlbedo != null &&
                        ((source != null && source.name.IndexOf("other", System.StringComparison.OrdinalIgnoreCase) >= 0) || i > 0);
                    Texture2D albedo = useSecondary ? secondaryAlbedo : primaryAlbedo;
                    Texture2D normal = useSecondary && secondaryNormal != null ? secondaryNormal : primaryNormal;

                    Material material = new Material(shader != null ? shader : source.shader);
                    material.name = DisplayName + " " + (source == null ? "Material " + i : source.name);
                    material.color = Color.white;
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Glossiness", 0.28f);
                    if (albedo != null)
                        material.SetTexture("_MainTex", albedo);
                    if (normal != null)
                    {
                        material.SetTexture("_BumpMap", normal);
                        material.SetFloat("_BumpScale", 1f);
                        material.EnableKeyword("_NORMALMAP");
                    }
                    if (occlusion != null)
                    {
                        material.SetTexture("_OcclusionMap", occlusion);
                        material.SetFloat("_OcclusionStrength", 0.65f);
                    }
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static Texture2D LoadTexture(string resourcePath)
        {
            return string.IsNullOrEmpty(resourcePath) ? null : Resources.Load<Texture2D>(resourcePath);
        }

        private void Fail(string message)
        {
            Debug.LogError(message, this);
            HudController.ShowError(message);
        }
    }
}
