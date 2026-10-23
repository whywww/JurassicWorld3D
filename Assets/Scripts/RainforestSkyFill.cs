using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicWorld
{
    /// <summary>One authored sky diffuse contribution; independent of quality and image exposure.</summary>
    [ExecuteAlways, DefaultExecutionOrder(1000)]
    public sealed class RainforestSkyFill : MonoBehaviour
    {
        public RainforestAtmosphereProfile profile;
        private AmbientMode previousMode;
        private SphericalHarmonicsL2 previousProbe;
        private bool applied;

        private void OnEnable()
        {
            previousMode = RenderSettings.ambientMode;
            previousProbe = RenderSettings.ambientProbe;
            Apply();
        }

        public void Apply()
        {
            if (profile == null || profile.skyFillCoefficients == null || profile.skyFillCoefficients.Length != 27 ||
                profile.skyFillBaseProbe == null || profile.skyFillBaseProbe.Length != 27) return;
            var probe = new SphericalHarmonicsL2();
            float strength = Mathf.Clamp(profile.skyFillStrength, 0f, 0.25f);
            for (int channel = 0; channel < 3; channel++) for (int i = 0; i < 9; i++)
                probe[channel, i] = profile.skyFillBaseProbe[channel * 9 + i] + strength * profile.skyFillCoefficients[channel * 9 + i];
            RenderSettings.ambientMode = strength > 0f ? AmbientMode.Custom : AmbientMode.Skybox;
            RenderSettings.ambientProbe = probe;
            applied = true;
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) Apply();
        }

        private void OnDisable()
        {
            if (!applied) return;
            RenderSettings.ambientMode = previousMode;
            RenderSettings.ambientProbe = previousProbe;
            applied = false;
        }
    }
}
