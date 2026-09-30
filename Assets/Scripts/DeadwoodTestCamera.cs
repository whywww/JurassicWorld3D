using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace JurassicWorld
{
    public sealed class DeadwoodTestCamera : MonoBehaviour
    {
        private Terrain terrain;
        private Transform[] logs;

        private void Start()
        {
            terrain = Terrain.activeTerrain;
            logs = new Transform[3];
            for (int i = 0; i < logs.Length; i++)
                logs[i] = GameObject.Find("Test log " + (i + 1)).transform;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-deadwoodCaptureDir")
                {
                    StartCoroutine(Capture(args[i + 1]));
                    return;
                }
            Focus(0, 0);
        }

        private void Update()
        {
            for (int i = 0; i < 3; i++)
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) Focus(i, 0);
            float forward = Input.GetAxisRaw("Vertical");
            float sideways = Input.GetAxisRaw("Horizontal");
            transform.position += (transform.forward * forward + transform.right * sideways) * (4f * Time.deltaTime);
            if (Input.GetMouseButton(1))
                transform.Rotate(Vector3.up, Input.GetAxis("Mouse X") * 2f, Space.World);
        }

        private void Focus(int index, int view)
        {
            Vector3 p = logs[index].position;
            float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
            Vector3 offset = view == 0 ? new Vector3(0f, 1.7f, -5.4f)
                : view == 1 ? new Vector3(5.2f, 1.7f, 0f)
                : new Vector3(0f, 0.65f, -3.1f);
            transform.position = new Vector3(p.x, ground, p.z) + offset;
            transform.LookAt(new Vector3(p.x, ground + 0.3f, p.z));
        }

        private IEnumerator Capture(string directory)
        {
            Directory.CreateDirectory(directory);
            for (int i = 0; i < 3; i++)
            for (int view = 0; view < 3; view++)
            {
                Focus(i, view);
                yield return new WaitForEndOfFrame();
                string path = Path.Combine(directory, $"log-{i + 1}-view-{view + 1}.png");
                Camera camera = GetComponent<Camera>();
                var target = new RenderTexture(1280, 720, 24);
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                RenderTexture previous = RenderTexture.active;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Destroy(target);
                Destroy(image);
                yield return null;
            }
            Debug.Log("DEADWOOD_SCREENSHOTS_READY: " + directory);
            Application.Quit();
        }
    }
}
