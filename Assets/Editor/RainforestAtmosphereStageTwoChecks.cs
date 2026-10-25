using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class RainforestAtmosphereStageTwoChecks
    {
        private const string Key = "JurassicWorld.Atmosphere.StageTwoSmoke";
        private const string Output = "Reports/Atmosphere/StageTwo";
        private static int frames;
        private static readonly string[] ProtectedFiles =
        {
            "Assets/Scenes/Main.unity", RainforestAtmosphereStageOne.ProfilePath,
            "Assets/Environment/Terrain/Prepared/MainInteractionTerrain.asset",
            "Assets/Navigation/TRexInteractionNavMesh.asset"
        };

        static RainforestAtmosphereStageTwoChecks() => EditorApplication.playModeStateChanged += Changed;

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            RainforestAtmosphereStageTwo.Validate();
            int systems = Object.FindObjectsOfType<ParticleSystem>().Length;
            RainforestAtmosphereStageTwo.ApplyConfigured();
            if (systems != Object.FindObjectsOfType<ParticleSystem>().Length ||
                Object.FindObjectsOfType<RainforestAtmosphereEffects>().Length != 1)
                throw new InvalidOperationException("Repeated stage-two application duplicated effects.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetString(Key + ".Hashes", Hashes());
            SessionState.SetInt(Key + ".Tier", QualitySettings.GetQualityLevel());
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".Passed", false);
            EditorApplication.isPlaying = true;
        }

        private static string Hashes()
        {
            using (SHA256 hash = SHA256.Create())
                return string.Join(";", ProtectedFiles.Select(path =>
                {
                    using (FileStream file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file));
                }));
        }

        private static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                frames = 0;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Key, false);
                try
                {
                    if (!SessionState.GetBool(Key + ".Passed", false) || Hashes() != SessionState.GetString(Key + ".Hashes", ""))
                        throw new InvalidOperationException("Runtime smoke failed or protected files changed.");
                    QualitySettings.SetQualityLevel(SessionState.GetInt(Key + ".Tier", 1), true);
                    File.WriteAllText(Output + "/Checks.txt", "PASS: shaders and depth references; one effects root after repeat application; low disables local effects; medium/high render; mist intersection/inside and shaft-side captures; minimap/planar-reflection exclusion; scene/profile/terrain/navmesh files unchanged by Play.\nManual visual approval remains pending. No performance route was run.\n");
                    Debug.Log("ATMOSPHERE_STAGE_TWO_SMOKE_PASS: tiers, camera masks, effect lifecycle and protected-file hashes passed.");
                    EditorApplication.Exit(0);
                }
                catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || ++frames < 16) return;
            EditorApplication.update -= Tick;
            try
            {
                var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
                RainforestAtmosphereEffects effects = atmosphere.localEffects;
                RainforestAtmosphereStageTwo.Validate();
                if (effects == null || effects.mist.Any(p => p == null) || effects.shafts.Any(p => p == null))
                    throw new InvalidOperationException("Missing stage-two runtime references.");
                int layer = LayerMask.NameToLayer("Atmosphere");
                foreach (Crest.OceanPlanarReflection reflection in Object.FindObjectsOfType<Crest.OceanPlanarReflection>())
                    if ((new SerializedObject(reflection).FindProperty("_reflectionLayers").intValue & (1 << layer)) != 0)
                        throw new InvalidOperationException("Planar reflection includes local effects.");
                foreach (Camera other in Object.FindObjectsOfType<Camera>())
                    if (other.name.Contains("Landscape") && (other.cullingMask & (1 << layer)) != 0)
                        throw new InvalidOperationException("Minimap includes local effects.");
                Vector3 creek = effects.mist[0].transform.position;
                for (int tier = 0; tier < 3; tier++)
                {
                    QualitySettings.SetQualityLevel(tier, true);
                    atmosphere.ApplyQuality(tier);
                    if (effects.mist.Any(p => p.gameObject.activeSelf != (tier > 0)))
                        throw new InvalidOperationException("Mist quality activation mismatch.");
                    foreach (ParticleSystem system in effects.mist)
                        if (system.gameObject.activeSelf) system.Simulate(20f, true, true, true);
                    Capture(effects.viewCamera, creek + new Vector3(4f, 1.1f, -4f), creek + new Vector3(0f, 0.5f, 3f), "Runtime-" + new[] { "Low", "Medium", "High" }[tier]);
                }
                Capture(effects.viewCamera, creek + Vector3.up * 0.6f, creek + Vector3.forward * 4f + Vector3.up * 0.6f, "Inside-Mist");
                Vector3 shaft = effects.shafts[0].bounds.center;
                Capture(effects.viewCamera, shaft + new Vector3(-4f, -1f, 2f), shaft, "Shaft-Side");
                SessionState.SetBool(Key + ".Passed", true);
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { EditorApplication.isPlaying = false; }
        }

        private static void Capture(Camera camera, Vector3 eye, Vector3 target, string name)
        {
            RenderTexture previous = camera.targetTexture, active = RenderTexture.active;
            var texture = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
                atmosphere.cloudRenderer.transform.position = eye;
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                texture.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(pixels);
            }
        }
    }
}
