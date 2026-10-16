using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexMainIntegrationSmoke
    {
        private const string Key = "JurassicWorld.TRexMainIntegration";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly float[] Angles = { -45f, 45f, 0f };
        private static readonly StringBuilder Report = new StringBuilder();
        private static DinosaurAgent rex;
        private static Camera camera;
        private static DinosaurInteractionSettings settings;
        private static int index, frame, stage;
        private static float began, detectionBegan, cooldownEnds;
        private static float peerWaitBegan;
        private static Vector3 peerWaitPosition;
        private static Vector3 direction;
        private static bool sawIntro, sawWalk, sawEnding, sawTurn, sawReturn;
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Private).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Private).SetValue(rex, value);
        private static object Call(string name) => typeof(DinosaurAgent).GetMethod(name, Private).Invoke(rex, null);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        static TRexMainIntegrationSmoke()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                    EditorApplication.delayCall += Begin;
            };
        }

        [MenuItem("Jurassic World/Tests/Verify Main T-Rex Integration")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the current scene first.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            rex = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var serialized = new SerializedObject(rex);
            Require(serialized.FindProperty("interactionProfile").objectReferenceValue ==
                AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(TRexInteractionBake.SideTestProfilePath), "Main uses the old profile.");
            Require(rex.ViewCamera != null && serialized.FindProperty("roamingMode").enumValueIndex == (int)DinosaurAgent.RoamingMode.Free
                && rex.GetComponent<DinosaurFbxLoader>() != null, "Main references are incomplete.");
            Require(UnityEngine.Object.FindObjectOfType<TRexFrontTestController>() == null, "Main contains test controls.");
            // Runtime-only: avoid hardware input and unsupported headless ocean rendering.
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (behaviour is EmotivPlayerInput || behaviour is SmoothExplorerCamera || behaviour.GetType().Namespace == "Crest")
                    behaviour.enabled = false;
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void Begin()
        {
            try
            {
                rex = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
                camera = rex.ViewCamera; settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                Require(!rex.IsFrontTestControlled && (bool)Get("interactionReady") && settings.NavigationReady, "Main did not initialize normally.");
                Report.Clear(); index = frame = 0; Time.captureDeltaTime = 1f / 60f;
                Prepare(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Prepare()
        {
            rex.enabled = false;
            var body = (DinosaurCoreBody)Get("interactionBody");
            bool found = false;
            // Find a real forest fixture without clearing trees, props or navigation.
            foreach (var point in new[] { new Vector3(-22f, 0f, 8f), new Vector3(23f, 0f, 18f), new Vector3(0f, 0f, 49f), rex.transform.position })
            {
                if (!NavMesh.SamplePosition(point, out var hit, 5f, new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 })) continue;
                for (int yaw = 0; yaw < 360; yaw += 15)
                {
                    Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    if (settings.PoseBlocked(new Vector2(hit.position.x, hit.position.z), new Vector2(forward.x, forward.z), body)) continue;
                    rex.transform.SetPositionAndRotation(hit.position, Quaternion.Euler(0f, yaw - rex.VisualYaw, 0f));
                    direction = Quaternion.Euler(0f, Angles[index], 0f) * forward;
                    camera.transform.position = rex.transform.position + direction * 31.9f + Vector3.up * 1.7f;
                    Physics.SyncTransforms();
                    if (!(bool)Call("CanSeePlayer")) continue;
                    found = true; break;
                }
                if (found) break;
            }
            Require(found, "No unobstructed Main fixture for angle " + Angles[index]);
            rex.GetComponent<DinosaurFootLock>().ResetSupportAnchors();
            Set("visibleTime", 0f); stage = 0; began = Time.time;
            cooldownEnds = Time.time + rex.InteractionCooldownRemaining;
            detectionBegan = Mathf.Max(Time.time, cooldownEnds);
            sawIntro = sawWalk = sawEnding = sawTurn = sawReturn = false;
            peerWaitBegan = -1f;
            // The natural detection gate must reject both out-of-range and hidden players.
            camera.transform.position += direction * .2f;
            Require(!(bool)Call("CanSeePlayer"), "Detected a player beyond 32m.");
            camera.transform.position -= direction * .2f;
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.position = Vector3.Lerp(rex.transform.position + Vector3.up * rex.ViewFocusHeight, camera.transform.position, .5f);
            blocker.transform.localScale = Vector3.one * 3f; Physics.SyncTransforms();
            Require((bool)Call("CanSeePlayer"), "An unregistered soft/decorative collider blocked player detection.");
            var originalRocks = settings.solidObstacles;
            settings.solidObstacles = originalRocks.Concat(new[] { blocker.GetComponent<Renderer>().bounds }).ToArray();
            bool occluded = !(bool)Call("CanSeePlayer");
            settings.solidObstacles = originalRocks;
            UnityEngine.Object.DestroyImmediate(blocker); Physics.SyncTransforms();
            Require(occluded, "Main detection ignored an occluder.");
        }

        private static void Tick()
        {
            try
            {
                if (frame == Time.frameCount) return;
                frame = Time.frameCount;
                Require(Time.time - began < 180f, "Main sequence timed out: " + rex.FrontInteractionStage + "/" + rex.CurrentAnimationSourceName);
                if (stage == 0)
                {
                    // Keep the fixture still while exercising the real hold/cooldown/visibility gate.
                    Call("UpdatePlayerInteraction");
                    if (!rex.IsFrontInteractionActive) return;
                    Require(Time.time >= detectionBegan + .3f - Time.deltaTime - .002f, "Detection bypassed hold or cooldown.");
                    Require(!rex.IsFrontTestControlled && rex.FrontTestInteractionCount == 0, "Main used a test trigger.");
                    Require(rex.IsSideTestSequence == (index < 2), "Live bearing selected the wrong sequence or retained stale side state.");
                    if (index < 2) Require((bool)Get("sideFromLeft") == (index == 0), "Wrong side selected.");
                    rex.enabled = true; stage = 1;
                }
                string action = rex.CurrentAnimationSourceName;
                sawIntro |= action == (index == 0 ? "look left" : index == 1 ? "look right" : "idle investigate");
                sawWalk |= action == "walk head low";
                sawEnding |= action == "look forward 2";
                sawTurn |= action == "turn 180 right";
                sawReturn |= rex.FrontInteractionStage == "ReturnWalk";
                if (rex.FrontActionWaitingForSpace && (string)Get("frontWaitReason") == "Peer")
                {
                    if (peerWaitBegan < 0f) { peerWaitBegan = Time.time; peerWaitPosition = rex.transform.position; }
                    if (Time.time - peerWaitBegan >= 1f)
                    {
                        Require(Vector3.Distance(peerWaitPosition, rex.transform.position) < .05f, "Main moved through a peer while yielding.");
                        // Release the runtime fixture after proving yielding. Random route traffic
                        // can otherwise keep this same forest bottleneck occupied indefinitely.
                        int peerIndex = 0;
                        foreach (var peer in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Where(a => a != rex))
                        { peer.enabled = false; peer.transform.position = new Vector3(200f + 20f * peerIndex++, peer.MovementHeight, 200f); }
                        Report.AppendLine("PASS Main peer yielding: held position for 1s before traffic fixture was released.");
                        peerWaitBegan = -1f;
                    }
                }
                else peerWaitBegan = -1f;
                if (rex.FrontInteractionStage == "Follow" || rex.FrontInteractionStage == "SideStartRun")
                    camera.transform.position = rex.transform.position + direction * 24f + Vector3.up * 1.7f;
                if (rex.FrontInteractionStage == "Ending" || rex.FrontInteractionStage == "TurnBack")
                    camera.transform.position = new Vector3(200f, 2f, 200f);
                if (!sawReturn || rex.IsFrontInteractionActive) return;
                Require(sawIntro && sawWalk && sawEnding && sawTurn, "Main skipped part of the tested sequence.");
                Require(rex.InteractionCooldownRemaining > 9.9f && rex.IsFreeRoaming, "Main did not return to free roaming with cooldown.");
                Report.AppendLine("PASS Main natural " + Angles[index] + "deg: detection/occlusion/hold, full sequence, return and cooldown; no test controller.");
                if (++index == Angles.Length) Finish(null); else Prepare();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (error != null) { Report.AppendLine(error.ToString()); Debug.LogException(error); }
            else { Report.AppendLine("PASS Main integration, including side-to-front state reset."); Debug.Log(Report.ToString()); }
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/trex-main-integration-result.txt", Report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
