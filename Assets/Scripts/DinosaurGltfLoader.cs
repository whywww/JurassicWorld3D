using System;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace JurassicWorld
{
    public sealed class DinosaurGltfLoader : MonoBehaviour
    {
        public const string RelativeModelPath = "Dinosaur/Rampaging T-Rex.glb";
        public string ModelPath { get; set; } = RelativeModelPath;
        public string DisplayName { get; set; } = "霸王龙";
        public Vector3 ModelScale { get; set; } = Vector3.one;
        public float TargetLength { get; set; }
        public bool AlignBottom { get; set; } = true;
        public float ModelYaw { get; set; }
        public string IdleAnimationHint { get; set; } = "idle";
        public string MoveAnimationHint { get; set; } = "run";
        public bool IsLoaded { get; private set; }
        public string Error { get; private set; }

        private GltfAsset asset;

        private async void Start()
        {
            await LoadModel();
        }

        public async Task<bool> LoadModel()
        {
            string path = Path.Combine(Application.streamingAssetsPath, ModelPath);
            if (!File.Exists(path))
            {
                Fail("未找到" + DisplayName + "模型：" + path);
                return false;
            }

            try
            {
                var modelRoot = new GameObject(DisplayName + " Model");
                modelRoot.transform.SetParent(transform, false);
                modelRoot.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
                modelRoot.transform.localScale = ModelScale;
                asset = modelRoot.AddComponent<GltfAsset>();
                asset.LoadOnStartup = false;
                bool success = await asset.Load(new Uri(path).AbsoluteUri);
                if (!success)
                {
                    Fail("glTFast 无法加载" + DisplayName + "模型。请检查 Console。 ");
                    return false;
                }

                if (TargetLength > 0f && !DinosaurModelFitter.Fit(modelRoot, transform, TargetLength, AlignBottom))
                {
                    Fail(DisplayName + " 无法计算模型尺寸。");
                    return false;
                }

                IsLoaded = true;
                DinosaurAgent agent = GetComponent<DinosaurAgent>();
                agent.BindAnimation(GetComponentInChildren<Animation>(true), IdleAnimationHint, MoveAnimationHint);
                if (AlignBottom)
                    gameObject.AddComponent<DinosaurFootLock>().Bind(modelRoot.transform, agent);
                HudController.ReportLoaded(DisplayName);
                return true;
            }
            catch (Exception exception)
            {
                Fail(DisplayName + "模型加载异常：" + exception.Message);
                return false;
            }
        }

        private void Fail(string message)
        {
            Error = message;
            Debug.LogError(message, this);
            HudController.ShowError(message);
        }
    }
}
