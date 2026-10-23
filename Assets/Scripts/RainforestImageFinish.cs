using UnityEngine;

namespace JurassicWorld
{
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class RainforestImageFinish : MonoBehaviour
    {
        [SerializeField] private Shader shader;
        [SerializeField] private float exposure = 1.05f;
        [SerializeField] private float bloom = 0.045f;
        [SerializeField] private int tier = 1;
        private Material material;

        public void Configure(Shader finishShader, float fixedExposure, float bloomStrength, int qualityTier)
        {
            if (shader != finishShader) Release();
            shader = finishShader;
            exposure = fixedExposure;
            bloom = bloomStrength;
            tier = qualityTier;
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (shader == null || !shader.isSupported)
            {
                Graphics.Blit(source, destination);
                return;
            }
            if (material == null) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_Exposure", exposure);
            material.SetFloat("_BloomStrength", bloom);
            RenderTexture a = null, b = null;
            try
            {
                if (bloom > 0f)
                {
                    int divisor = tier == 2 ? 4 : 8;
                    int width = Mathf.Max(1, source.width / divisor), height = Mathf.Max(1, source.height / divisor);
                    a = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf);
                    b = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf);
                    a.filterMode = b.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(source, a, material, 0);
                    material.SetVector("_BlurDirection", new Vector4(1f / width, 0f, 0f, 0f));
                    Graphics.Blit(a, b, material, 1);
                    material.SetVector("_BlurDirection", new Vector4(0f, 1f / height, 0f, 0f));
                    Graphics.Blit(b, a, material, 1);
                }
                material.SetTexture("_BloomTex", a != null ? (Texture)a : Texture2D.blackTexture);
                Graphics.Blit(source, destination, material, 2);
            }
            finally
            {
                if (a != null) RenderTexture.ReleaseTemporary(a);
                if (b != null) RenderTexture.ReleaseTemporary(b);
            }
        }

        private void OnDisable() => Release();
        private void Release()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            material = null;
        }
    }
}
