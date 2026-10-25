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
    public static class RainforestBackdropStageThreeChecks
    {
        private const string Key = "JurassicWorld.Backdrop.StageThreeCheck";
        private static int frames;
        private static readonly string[] ProtectedFiles =
        {
            "Assets/Scenes/Main.unity", RainforestAtmosphereStageOne.ProfilePath,
            "Assets/Environment/Terrain/Prepared/MainInteractionTerrain.asset",
            "Assets/Navigation/TRexInteractionNavMesh.asset"
        };

        static RainforestBackdropStageThreeChecks() => EditorApplication.playModeStateChanged += Changed;

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            RainforestBackdropStageThree.Validate();
            RainforestBackdropStageThree.ApplyConfigured();
            RainforestBackdropStageThree.ApplyConfigured();
            RainforestBackdropStageThree.Validate();
            if (AssetDatabase.GetDependencies("Assets/Environment/Backdrop/Prepared/RainforestBackdrop.prefab", true)
                .Contains(RainforestBackdropStageThree.Source))
                throw new InvalidOperationException("Backdrop prefab must not reference the high-detail source FBX.");
            SessionState.SetString(Key + ".Hashes", Hashes());
            SessionState.SetInt(Key + ".Tier", QualitySettings.GetQualityLevel());
            SessionState.SetBool(Key + ".Passed", false);
            SessionState.SetBool(Key, true);
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
                        throw new InvalidOperationException("Stage-three runtime check failed or protected files changed.");
                    QualitySettings.SetQualityLevel(SessionState.GetInt(Key + ".Tier", 1), true);
                    File.WriteAllText(RainforestBackdropStageThree.Output + "/Checks.txt",
                        "PASS: reopen references, independent LOD3 meshes, no high-detail FBX dependency, repeated configured application, " +
                        "600m backdrop / 150m foreground culling, no colliders, low/medium/high renders, " +
                        "eight headings and two boundary views, minimap/planar reflection exclusion, scene/profile/terrain/navmesh files unchanged by Play.\n" +
                        "Manual Play approval is pending. No fixed performance route or demonstration-machine test was run.\n");
                    Debug.Log("BACKDROP_STAGE_THREE_SMOKE_PASS: dependencies, culling, tiers, camera masks and protected files passed.");
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
                RainforestBackdropStageThree.Validate();
                RainforestBackdrop backdrop = Object.FindObjectOfType<RainforestBackdrop>();
                float[] distances = backdrop.viewCamera.layerCullDistances;
                for (int layer = 0; layer < 32; layer++)
                    if (distances[layer] != (layer == backdrop.backdropLayer ? 600f : 150f))
                        throw new InvalidOperationException("Unexpected draw distance on layer " + layer);
                int bit = 1 << backdrop.backdropLayer;
                foreach (Camera camera in Object.FindObjectsOfType<Camera>())
                    if (camera.name.Contains("Landscape") && (camera.cullingMask & bit) != 0)
                        throw new InvalidOperationException("Minimap includes backdrop.");
                foreach (Crest.OceanPlanarReflection reflection in Object.FindObjectsOfType<Crest.OceanPlanarReflection>())
                    if ((new SerializedObject(reflection).FindProperty("_reflectionLayers").intValue & bit) != 0)
                        throw new InvalidOperationException("Planar reflection includes backdrop.");
                RainforestAtmosphereController atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
                for (int tier = 0; tier < 3; tier++)
                {
                    QualitySettings.SetQualityLevel(tier, true);
                    atmosphere.ApplyQuality(tier);
                    RainforestBackdropStageThree.Capture(backdrop.viewCamera,
                        "Runtime-" + new[] { "Low", "Medium", "High" }[tier], tier == 1 ? 10 : 1);
                    if (Directory.Exists(RainforestBoundaryTransition.Output))
                        RainforestBoundaryTransition.Capture(backdrop.viewCamera,
                            "Runtime-" + new[] { "Low", "Medium", "High" }[tier]);
                }
                SessionState.SetBool(Key + ".Passed", true);
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { EditorApplication.isPlaying = false; }
        }
    }
}
