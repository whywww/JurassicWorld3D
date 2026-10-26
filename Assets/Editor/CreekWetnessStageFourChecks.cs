using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class CreekWetnessStageFourChecks
    {
        private const string Key = "JurassicWorld.CreekWetness.Check";
        private static int frames;
        private static readonly string[] ProtectedFiles =
        {
            "Assets/Scenes/Main.unity",
            "Assets/Environment/Terrain/Prepared/MainInteractionTerrain.asset",
            "Assets/Navigation/TRexInteractionNavMesh.asset",
            "Assets/Environment/Creek/Prepared/CrestCreekWater.mat",
            "Assets/Environment/Creek/Prepared/JungleCreekBank.terrainlayer",
            "Assets/Environment/RocksAndDeadwood/RockMossSet01/Prepared/Materials/RockMossSet01.mat",
            "Assets/Environment/Vegetation/Fern02/Prepared/Materials/Fern02_Cutout.mat"
        };
        static CreekWetnessStageFourChecks() => EditorApplication.playModeStateChanged += Changed;

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            CreekWetnessStageFour.Validate();
            var state = Object.FindObjectOfType<CreekWetSurfaceState>();
            if (state.bindings.Length != 74 || !File.Exists(state.originalWeightsBackup))
                throw new InvalidOperationException("Wet surfaces or rollback snapshot incomplete.");
            // All wet bindings must point back to dry originals, including after a second application.
            if (state.bindings.Any(b => b.originals.Any(m => AssetDatabase.GetAssetPath(m).StartsWith(CreekWetnessStageFour.Folder))))
                throw new InvalidOperationException("Wet variants were recursively cloned instead of preserving originals.");
            SessionState.SetString(Key + ".Hashes", Hashes());
            SessionState.SetInt(Key + ".Tier", QualitySettings.GetQualityLevel());
            SessionState.SetBool(Key + ".Passed", false);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static string Hashes() => string.Join(";", ProtectedFiles.Select(CreekWetnessStageFour.HashFile));

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
                        throw new InvalidOperationException("Play check failed or protected source files drifted.");
                    QualitySettings.SetQualityLevel(SessionState.GetInt(Key + ".Tier", 1), true);
                    File.WriteAllText(CreekWetnessStageFour.Output + "/Checks.txt",
                        "PASS: shaders, 56 rocks/18 ferns, dry original bindings after repeat application, creek-only mask, local bank blend, two static cubemaps; Low/Medium/High Play renders; " +
                        "scene/terrain/navmesh/water/original bank/original rock/original fern files unchanged by Play.\n" +
                        "Application also verified terrain height/detail/tree fingerprints and navigation unchanged, with zero paint change outside the wet mask.\n" +
                        "User manual Play approval remains pending. No fixed performance route or demonstration-machine check was run.\n");
                    Debug.Log("CREEK_STAGE_FOUR_SMOKE_PASS: tiers, materials, baked reflections and protected files passed.");
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
                CreekWetnessStageFour.Validate();
                Transform[] points = GameObject.Find("Main East to North Creek").GetComponentsInChildren<Transform>()
                    .Where(t => t.name.StartsWith("Flow Point ", StringComparison.Ordinal)).ToArray();
                var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
                for (int tier = 0; tier < 3; tier++)
                {
                    QualitySettings.SetQualityLevel(tier, true);
                    atmosphere.ApplyQuality(tier);
                    CreekWetnessStageFour.Capture(points, "Runtime-" + new[] { "Low", "Medium", "High" }[tier]);
                }
                SessionState.SetBool(Key + ".Passed", true);
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { EditorApplication.isPlaying = false; }
        }
    }
}
