using UnityEngine;

namespace JurassicWorld
{
    public sealed class SmoothExplorerCamera : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float acceleration = 6f;
        [SerializeField] private float turnSpeed = 60f;
        [SerializeField] private float cameraFollowSpeed = 3.5f;
        [SerializeField] private float lookSensitivity = 65f;
        [SerializeField] private float initialPitch = 4f;
        private Vector3 velocity;
        private Vector3 previousMousePosition;
        private float travelYaw;
        private float yaw;
        private float pitch;

        private void Start()
        {
            yaw = transform.eulerAngles.y;
            travelYaw = yaw;
            pitch = initialPitch;
        }

        private void Update()
        {
            float turn = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float forward = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);

            if (!Mathf.Approximately(turn, 0f))
            {
                travelYaw += turn * turnSpeed * Time.deltaTime;
                forward = 1f;
            }

            Vector3 travelDirection = Quaternion.Euler(0f, travelYaw, 0f) * Vector3.forward;
            Vector3 desired = travelDirection * forward * moveSpeed;
            velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-acceleration * Time.deltaTime));

            Vector3 next = transform.position + velocity * Time.deltaTime;
            next.x = Mathf.Clamp(next.x, -47f, 47f);
            next.z = Mathf.Clamp(next.z, -47f, 47f);
            next.y = 3f;
            transform.position = next;

            if (Input.GetMouseButtonDown(1))
                previousMousePosition = Input.mousePosition;

            if (Input.GetMouseButton(1))
            {
                Vector3 mouseDelta = Input.mousePosition - previousMousePosition;
                previousMousePosition = Input.mousePosition;
                yaw += mouseDelta.x * lookSensitivity * 0.01f;
                travelYaw = yaw;
                pitch -= mouseDelta.y * lookSensitivity * 0.01f;
                pitch = Mathf.Clamp(pitch, -18f, 35f);
            }
            else
            {
                yaw = Mathf.LerpAngle(yaw, travelYaw, 1f - Mathf.Exp(-cameraFollowSpeed * Time.deltaTime));
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(pitch, yaw, 0f), 1f - Mathf.Exp(-10f * Time.deltaTime));
        }
    }
}
