using UnityEngine;

namespace JurassicWorld
{
    /// <summary>Authored creek mist and sun shafts. No forest or navigation queries at runtime.</summary>
    public sealed class RainforestAtmosphereEffects : MonoBehaviour
    {
        public RainforestAtmosphereProfile profile;
        public Camera viewCamera;
        public ParticleSystem[] mist = new ParticleSystem[0];
        public Renderer[] shafts = new Renderer[0];
        private int tier = 1;
        private DepthTextureMode previousDepth;
        private bool addedDepth;
        private MaterialPropertyBlock properties;

        private void OnEnable()
        {
            if (!Application.isPlaying || profile == null || !profile.localAtmosphereEnabled) return;
            if (viewCamera != null)
            {
                previousDepth = viewCamera.depthTextureMode;
                addedDepth = (previousDepth & DepthTextureMode.Depth) == 0;
                viewCamera.depthTextureMode |= DepthTextureMode.Depth;
            }
            ApplyQuality(QualitySettings.GetQualityLevel());
        }

        public void ApplyQuality(int quality)
        {
            if (profile == null || !profile.localAtmosphereEnabled) return;
            tier = Mathf.Clamp(quality, 0, 2);
            // Only apply on configuration/quality changes; the minimap temporarily disables global fog.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = profile.distanceFogColor;
            RenderSettings.fogStartDistance = profile.distanceFogStart;
            RenderSettings.fogEndDistance = profile.distanceFogEnd;
            foreach (ParticleSystem system in mist)
            {
                if (system == null) continue;
                var main = system.main;
                main.maxParticles = tier == 2 ? 12 : 6;
                main.startColor = new Color(0.74f, 0.80f, 0.78f, profile.mistOpacity);
                var emission = system.emission;
                emission.rateOverTime = tier == 2 ? 0.48f : 0.24f;
                system.gameObject.SetActive(tier > 0);
                if (Application.isPlaying && tier > 0 && !system.isPlaying) system.Play();
            }
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetColor("_Tint", new Color(1f, 0.96f, 0.84f, profile.shaftOpacity));
            foreach (Renderer shaft in shafts)
                if (shaft != null) shaft.SetPropertyBlock(properties);
            UpdateVisibility();
        }

        private void LateUpdate() => UpdateVisibility();

        private void UpdateVisibility()
        {
            if (viewCamera == null) return;
            Vector3 position = viewCamera.transform.position;
            foreach (ParticleSystem system in mist)
            {
                if (system == null || !system.gameObject.activeSelf) continue;
                float distance = (system.transform.position - position).sqrMagnitude;
                if (distance > 75f * 75f && system.isPlaying) system.Pause();
                else if (distance < 68f * 68f && system.isPaused) system.Play();
            }
            for (int i = 0; i < shafts.Length; i++)
                if (shafts[i] != null)
                    shafts[i].enabled = tier > 0 && (tier == 2 || i < 2) &&
                        (shafts[i].bounds.center - position).sqrMagnitude < 65f * 65f;
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;
            if (addedDepth && viewCamera != null && viewCamera.depthTextureMode == (previousDepth | DepthTextureMode.Depth))
                viewCamera.depthTextureMode = previousDepth;
            foreach (ParticleSystem system in mist)
                if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (Renderer shaft in shafts)
                if (shaft != null) shaft.enabled = false;
        }
    }
}
