using UnityEngine;
using UnityEngine.SceneManagement;

namespace JurassicWorld
{
    [DefaultExecutionOrder(-1200)]
    public sealed class TRexFrontTestController : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/TRexFrontInteractionTest.unity";
        public DinosaurAgent dinosaur;
        public Camera playerCamera;
        public enum ApproachSide { Front, Left, Right }
        [Tooltip("Relative to the dinosaur's initial visual heading. Applied on entering Play.")]
        public ApproachSide initialSide = ApproachSide.Front;
        [Range(20f, 90f)] public float sideAngle = 45f;
        public Transform approachLayout;
        public ApproachSide ActiveSide { get; private set; }
        public float ActiveAngle { get; private set; }
        public bool Triggered => dinosaur != null && dinosaur.FrontTestInteractionCount > 0;
        public bool IsInsideTrigger
        {
            get
            {
                if (playerCamera == null || approachLayout == null) return false;
                Vector3 local = approachLayout.InverseTransformPoint(playerCamera.transform.position);
                return Mathf.Abs(local.x) <= 2f && local.z >= 30f && local.z <= 32f;
            }
        }

        private void Awake()
        {
            ActiveSide = initialSide;
            ActiveAngle = initialSide == ApproachSide.Front ? 0f : Mathf.Clamp(sideAngle, 20f, 90f);
            float signedAngle = initialSide == ApproachSide.Left ? -ActiveAngle : ActiveAngle;
            Quaternion heading = dinosaur.transform.rotation * Quaternion.Euler(0f, dinosaur.VisualYaw, 0f);
            Vector3 forward = heading * Vector3.forward;
            forward.y = 0f;
            Quaternion layoutRotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, signedAngle, 0f);
            Vector3 origin = dinosaur.transform.position;
            origin.y = 0f;
            approachLayout.SetPositionAndRotation(origin, layoutRotation);
            playerCamera.transform.SetPositionAndRotation(approachLayout.TransformPoint(new Vector3(0f, 1.7f, 40f)),
                layoutRotation * Quaternion.Euler(4f, 180f, 0f));
            dinosaur.ArmFrontInteractionTest();
            // The independent flat test uses its own baked area; Main keeps its existing activity bounds.
            FindObjectOfType<DinosaurInteractionSettings>().activityBoundsOverride = new Rect(-150f, -140f, 300f, 300f);
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R)) { Reload(); return; }
        }
        public void Reload()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(gameObject.scene.name);
#endif
        }
    }
}
