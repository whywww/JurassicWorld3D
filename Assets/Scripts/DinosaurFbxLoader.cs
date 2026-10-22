using UnityEngine;

namespace JurassicWorld
{
    // 运行期绑定器：模型网格、材质与缩放已在编辑态由 JurassicWorldSceneBuilder 烘焙进场景，
    // 这里只负责把已存在的模型绑定到 DinosaurAgent（Legacy 动画）、按需附加脚步锁定，并上报 HUD。
    // 绝不再 Instantiate / Fit / new Material（Fit 非幂等，二次调用会累积缩放导致尺寸翻倍）。
    public sealed class DinosaurFbxLoader : MonoBehaviour
    {
        [SerializeField] private GameObject modelRoot;
        [SerializeField] private string displayName = "恐龙";
        [SerializeField] private string idleAnimationHint = "idle";
        [SerializeField] private string moveAnimationHint = "run";
        [SerializeField] private bool enableFootLock = true;
        [SerializeField] private string[] footBoneNames;
        [SerializeField] private int footLockJointDepth = 3;
        [SerializeField] private bool releaseFootLockOnRise;
        [SerializeField] private float footContactHeight = -1f;
        [SerializeField] private float footRiseThreshold = -1f;
        [SerializeField] private float footLockDistanceLimit = -1f;

        public GameObject ModelRoot => modelRoot;
        public string DisplayName => displayName;
        public bool IsLoaded { get; private set; }

        private void Start()
        {
            if (modelRoot == null)
            {
                Fail(displayName + " 缺少已烘焙的模型引用（modelRoot 为空）。请在编辑态重新执行 Bake Scene。");
                return;
            }

            Animation animation = modelRoot.GetComponentInChildren<Animation>(true);
            if (animation == null)
            {
                Fail(displayName + " 没有可播放的 Legacy 动画。请检查模型导入设置。");
                return;
            }

            animation.cullingType = AnimationCullingType.AlwaysAnimate;

            foreach (Renderer renderer in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                    skinnedRenderer.updateWhenOffscreen = true;
            }

            IsLoaded = true;
            DinosaurAgent agent = GetComponent<DinosaurAgent>();
            agent.BindAnimation(animation, idleAnimationHint, moveAnimationHint);
            if (enableFootLock)
            {
                gameObject.AddComponent<DinosaurFootLock>().Bind(
                    modelRoot.transform, agent, footBoneNames, footLockJointDepth, releaseFootLockOnRise,
                    footContactHeight, footRiseThreshold, footLockDistanceLimit);
            }
        }

        private void Fail(string message)
        {
            Debug.LogError(message, this);
            HudController.ShowError(message);
        }
    }
}
