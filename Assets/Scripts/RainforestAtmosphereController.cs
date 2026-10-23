using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld
{
    /// <summary>Owns only the authored atmosphere; never searches or rebuilds the forest.</summary>
    public sealed class RainforestAtmosphereController : MonoBehaviour
    {
        public RainforestAtmosphereProfile profile;
        public Light sun;
        public Camera viewCamera;
        public Renderer cloudRenderer;
        public RainforestImageFinish imageFinish;
        private Material skyInstance, cloudInstance, previousSky;
        private Material previousCloud;

        private void Awake()
        {
            if (!Application.isPlaying) return;
            if (profile == null || profile.skyMaterial == null) return;
            previousSky = RenderSettings.skybox;
            skyInstance = new Material(profile.skyMaterial) { hideFlags = HideFlags.DontSave };
            skyInstance.SetFloat("_Rotation", profile.skyRotation);
            skyInstance.SetFloat("_Exposure", profile.skyExposure);
            RenderSettings.skybox = skyInstance;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = profile.ambientIntensity;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(profile.sunEuler);
                sun.color = profile.sunColor;
                sun.intensity = profile.sunIntensity;
                sun.shadowStrength = profile.shadowStrength;
            }
            if (cloudRenderer != null && profile.cloudMaterial != null)
            {
                previousCloud = cloudRenderer.sharedMaterial;
                cloudInstance = new Material(profile.cloudMaterial) { hideFlags = HideFlags.DontSave };
                cloudRenderer.sharedMaterial = cloudInstance;
            }
            ApplyQuality(QualitySettings.GetQualityLevel());
        }

        public void ApplyQuality(int tier)
        {
            if (profile == null) return;
            if (cloudInstance != null)
            {
                cloudInstance.SetFloat("_Opacity", profile.cloudOpacity * (tier == 0 ? 0.5f : 1f));
                cloudInstance.SetVector("_Velocity", new Vector4(profile.cloudVelocity.x, profile.cloudVelocity.y, 0f, 0f));
                cloudInstance.SetFloat("_Detail", tier == 2 ? 1f : 0f);
            }
            if (imageFinish != null)
                imageFinish.Configure(profile.finishingShader, profile.exposure,
                    tier == 0 ? 0f : profile.bloom * (tier == 2 ? 1.3f : 1f), tier);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (cloudRenderer != null && viewCamera != null)
                cloudRenderer.transform.position = viewCamera.transform.position;
        }

        private void OnDestroy()
        {
            if (RenderSettings.skybox == skyInstance) RenderSettings.skybox = previousSky;
            if (cloudRenderer != null && cloudRenderer.sharedMaterial == cloudInstance)
                cloudRenderer.sharedMaterial = previousCloud;
            if (skyInstance != null) Destroy(skyInstance);
            if (cloudInstance != null) Destroy(cloudInstance);
        }
    }
}
