using UnityEngine;

namespace JurassicWorld
{
    /// <summary>Extends the background view without extending foreground geometry draw distances.</summary>
    public sealed class RainforestBackdrop : MonoBehaviour
    {
        public Camera viewCamera;
        public float foregroundDistance = 150f;
        public float backdropDistance = 600f;
        public int backdropLayer;

        private void OnEnable()
        {
            if (Application.isPlaying) ApplyCamera();
        }

        public void ApplyCamera()
        {
            if (viewCamera == null) return;
            float[] distances = viewCamera.layerCullDistances;
            for (int layer = 0; layer < distances.Length; layer++)
                distances[layer] = layer == backdropLayer ? backdropDistance :
                    distances[layer] > 0f ? Mathf.Min(distances[layer], foregroundDistance) : foregroundDistance;
            viewCamera.layerCullDistances = distances;
            viewCamera.farClipPlane = backdropDistance;
            viewCamera.cullingMask |= 1 << backdropLayer;
        }
    }
}
