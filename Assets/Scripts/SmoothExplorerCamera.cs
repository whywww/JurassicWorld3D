using UnityEngine;

namespace JurassicWorld
{
    public sealed class SmoothExplorerCamera : MonoBehaviour
    {
        private const float MoveSpeed = 6f;
        private const float Acceleration = 6f;
        private const float TurnSpeed = 60f;
        private const float CameraFollowSpeed = 3.5f;
        private const float LookSensitivity = 65f;
        private Vector3 velocity;
        private Vector3 previousMousePosition;
        private float travelYaw;
        private float yaw;
        private float pitch = 4f;

        private void Start()
        {
            yaw = transform.eulerAngles.y;
            travelYaw = yaw;
        }

        private void Update()
        {
            float turn = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float forward = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);

            if (!Mathf.Approximately(turn, 0f))
            {
                travelYaw += turn * TurnSpeed * Time.deltaTime;
                forward = 1f;
            }

            Vector3 travelDirection = Quaternion.Euler(0f, travelYaw, 0f) * Vector3.forward;
            Vector3 desired = travelDirection * forward * MoveSpeed;
            velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-Acceleration * Time.deltaTime));

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
                yaw += mouseDelta.x * LookSensitivity * 0.01f;
                travelYaw = yaw;
                pitch -= mouseDelta.y * LookSensitivity * 0.01f;
                pitch = Mathf.Clamp(pitch, -18f, 35f);
            }
            else
            {
                yaw = Mathf.LerpAngle(yaw, travelYaw, 1f - Mathf.Exp(-CameraFollowSpeed * Time.deltaTime));
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(pitch, yaw, 0f), 1f - Mathf.Exp(-10f * Time.deltaTime));
        }
    }
}
