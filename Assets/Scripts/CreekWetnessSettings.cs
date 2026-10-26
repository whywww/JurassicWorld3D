using UnityEngine;

namespace JurassicWorld
{
    [CreateAssetMenu(menuName = "Jurassic World/Creek Wet Surfaces")]
    public sealed class CreekWetnessSettings : ScriptableObject
    {
        public bool enabled = true;
        [Range(0f, 1f)] public float rockWetness = 0.8f;
        [Range(0f, 1f)] public float leafWetness = 0.45f;
        [Range(0f, 1f)] public float mudSmoothness = 0.4f;
    }
}
