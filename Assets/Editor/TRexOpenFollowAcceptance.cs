using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // An isolated Play fixture: Main's actor, animation and production navigation
    // calls, with an empty flat navigation surface. No production bypass or forced Follow.
    [InitializeOnLoad]
    public static class TRexOpenFollowAcceptance
    {
        private const string Key = "JurassicWorld.OpenFollow";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly int[] Rates = { 30, 60, 144 };
        private static DinosaurAgent rex;
        private static Camera camera;
        private static NavMeshData navData;
        private static int scenario, lastFrame, originalVsync, originalFrameRate;
        private static float began, followAt, firstMoveAt, error;
        private static Vector3 followOrigin;
        private static double deadline;
        private static readonly StringBuilder Report = new StringBuilder();
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static object Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        static TRexOpenFollowAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Restore", false))
                { SessionState.SetBool(Key + ".Restore", false); EditorSceneManager.OpenScene("Assets/Scenes/Main.unity"); }
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify Unobstructed Roar to Follow")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the scene before acceptance.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".Restore", true); EditorApplication.isPlaying = true;
        }
        private static void Begin()
        {
            try
            {
                PrepareWorld(out camera);
                Report.Clear(); Report.AppendLine("# Unobstructed side Roar to Follow\n\nMain actor/profile in an isolated empty Play navigation fixture. Production state machine, body/head checks and foot solver; no forced stage or minimum Follow time.\n");
                scenario = 0; lastFrame = -1; Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception e) { Finish(e); }
        }
        internal static DinosaurAgent PrepareWorld(out Camera targetCamera)
        {
                rex = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex"); camera = Camera.main;
                foreach (var agent in Object.FindObjectsOfType<DinosaurAgent>()) agent.enabled = false;
                foreach (var marker in Object.FindObjectsOfType<SceneTreeObstacle>()) marker.enabled = false;
                foreach (var terrain in Terrain.activeTerrains)
                {
                    // Clone the asset in Play; never modify or save original TerrainData.
                    var data = Object.Instantiate(terrain.terrainData); data.treeInstances = Array.Empty<TreeInstance>(); terrain.terrainData = data;
                }
                foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
                    if (behaviour is SmoothExplorerCamera || behaviour is EmotivPlayerInput || behaviour is MovementCorridorDebug
                        || behaviour.GetType().Namespace == "Crest") behaviour.enabled = false;
                TerrainTreeObstacleRegistry.Invalidate();
                var settings = Object.FindObjectOfType<DinosaurInteractionSettings>(); settings.solidObstacles = Array.Empty<Bounds>();
                NavMesh.RemoveAllNavMeshData();
                var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(new Vector3(0, -.1f, 0), Quaternion.identity, Vector3.one), size = new Vector3(120, .2f, 120), area = 0 } };
                navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(settings.navigationAgentTypeId), sources,
                    new Bounds(Vector3.zero, new Vector3(130, 20, 130)), Vector3.zero, Quaternion.identity);
                if (navData == null) throw new InvalidOperationException("Fixture navigation bake failed.");
                typeof(DinosaurInteractionSettings).GetField("navigationInstance", Flags).SetValue(settings, NavMesh.AddNavMeshData(navData));
                camera.enabled = false; originalVsync = QualitySettings.vSyncCount; originalFrameRate = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            targetCamera = camera;
            return rex;
        }
        private static void Prepare()
        {
            if (rex.IsFrontInteractionActive) Call("FinishFrontInteraction");
            Call("ClearSteppingTurn"); Set("peers", new[] { rex }); Set("frontTestControlled", true); Set("frontTestWaiting", false);
            rex.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animation = rex.GetComponentInChildren<Animation>(); animation.Stop();
            string idle = (string)typeof(DinosaurAgent).GetField("idleClip", Flags).GetValue(rex);
            animation.Play(idle); animation[idle].time = 0f; animation.Sample();
            rex.GetComponentInChildren<DinosaurFootLock>()?.ResetSupportAnchors();
            Vector3 direction = Quaternion.Euler(0, scenario % 2 == 0 ? -35f : 35f, 0) * Vector3.forward;
            camera.transform.position = direction * 24f + Vector3.up * 1.7f;
            Time.captureDeltaTime = 1f / Rates[scenario / 2];
            Call("BeginInteractionSequence", true, scenario % 2 == 0); rex.enabled = true;
            began = Time.time; followAt = firstMoveAt = -1f; error = 0f; deadline = EditorApplication.timeSinceStartup + 120;
        }
        private static void Tick()
        {
            try
            {
                if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
                if (EditorApplication.timeSinceStartup > deadline) throw new InvalidOperationException("Open Follow fixture timed out.");
                var feet = rex.GetComponentInChildren<DinosaurFootLock>();
                if (feet != null) error = Mathf.Max(error, feet.LeftPlanted ? feet.LeftPlantError : 0, feet.RightPlanted ? feet.RightPlantError : 0);
                if (rex.FrontInteractionStage.StartsWith("Follow") && followAt < 0f) { followAt = Time.time; followOrigin = rex.transform.position; }
                if (rex.FrontInteractionStage.Contains("Ending") || Time.time - began > 25f)
                    throw new InvalidOperationException("Unobstructed Roar failed to follow: " + rex.FrontInteractionStage + " / " + rex.LastFrontSpaceFailure);
                if (followAt < 0f) return;
                float moved = Vector3.Distance(followOrigin, rex.transform.position);
                if (moved > .15f && firstMoveAt < 0f) firstMoveAt = Time.time;
                if (firstMoveAt < 0f && Time.time - followAt < 8f || firstMoveAt >= 0f && Time.time - firstMoveAt < 2f) return;
                if (moved < 1f || error > .25f) throw new InvalidOperationException($"Follow failed movement/support: {moved:F3}m / {error:F3}m; {rex.FrontExitStatus}, {rex.FrontExitFailure}, checks={rex.FrontExitReviewCount}, queries={rex.FrontExitPathQueries}, block={rex.FrontBlockingReason}.");
                Report.AppendLine($"- PASS {Rates[scenario/2]} FPS, {(scenario%2==0 ? "left" : "right")} Roar: Follow began at {followAt-began:F3}s; moved {moved:F3}m over two seconds; maximum support error {error:F4}m.");
                if (++scenario < 6) Prepare(); else Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0;
            QualitySettings.vSyncCount = originalVsync; Application.targetFrameRate = originalFrameRate;
            string result = error == null ? "PASS OPEN_ROAR_FOLLOW" : "FAILED " + error;
            File.WriteAllText(RainforestSample.Output + "/OpenRoarFollow.md", result + "\n\n" + Report);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
