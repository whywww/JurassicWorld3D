using UnityEngine;

namespace JurassicWorld
{
    [CreateAssetMenu(menuName = "Jurassic World/Rainforest Atmosphere")]
    public sealed class RainforestAtmosphereProfile : ScriptableObject
    {
        public Material skyMaterial;
        public Material cloudMaterial;
        public Shader finishingShader;
        [Range(0f, 360f)] public float skyRotation;
        [Range(0.1f, 3f)] public float skyExposure = 0.8f;
        public Color sunColor = new Color(1f, 0.96f, 0.89f);
        [Range(0f, 3f)] public float sunIntensity = 1.15f;
        public Vector3 sunEuler;
        [Range(0f, 2f)] public float ambientIntensity = 0.8f;
        [Range(0f, 0.25f)] public float skyFillStrength = 0.1f;
        [HideInInspector] public float[] skyFillCoefficients = new float[0];
        [HideInInspector] public float[] skyFillBaseProbe = new float[0];
        [HideInInspector] public bool vegetationLightingEnabled;
        [Range(0f, 1f)] public float shadowStrength = 0.8f;
        [Range(0.1f, 3f)] public float exposure = 1.05f;
        [Range(0f, 0.2f)] public float bloom = 0.045f;
        [Range(0f, 1f)] public float cloudOpacity = 0.12f;
        public Vector2 cloudVelocity = new Vector2(0.0006f, 0.0002f);

        [Header("Stage 2 - local atmosphere")]
        public bool localAtmosphereEnabled;
        public Color distanceFogColor = new Color(0.64f, 0.73f, 0.76f);
        public float distanceFogStart = 42f;
        public float distanceFogEnd = 155f;
        [Range(0f, 0.3f)] public float mistOpacity = 0.065f;
        [Range(0f, 0.15f)] public float shaftOpacity = 0.018f;

    }
}
