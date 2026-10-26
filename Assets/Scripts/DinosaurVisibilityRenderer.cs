using UnityEngine;

namespace JurassicWorld
{
    // The camera's own culling selects the active LOD, including animated LOD groups.
    // Bounds and Renderer.enabled alone would incorrectly count every LOD mesh.
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class DinosaurVisibilityRenderer : MonoBehaviour
    {
        internal static Camera CaptureCamera;
        internal static int CaptureSerial;
        internal int LastCapture;
        internal Camera LastCamera;
        internal int LastFrame = -1;

        private void OnWillRenderObject()
        {
            if (CaptureCamera != null && Camera.current == CaptureCamera)
                LastCapture = CaptureSerial;
            else
            {
                LastCamera = Camera.current;
                LastFrame = Time.frameCount;
            }
        }
    }
}
