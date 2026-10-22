using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Isolated navigation contracts: leave the user's loaded scene and its data intact.
    public static class TRexFreeRoamPathAcceptance
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Result = "Logs/free-roam-path-result.txt";
        private static DinosaurAgent rex;
        private static DinosaurInteractionSettings settings;
        private static readonly Vector3 Origin = new Vector3(300f, 0f, 300f);
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static object Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        [MenuItem("Jurassic World/Acceptance/Verify T-Rex Free Roam Path Connections")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run path fixtures outside Play.");
            if (Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Scene original = SceneManager.GetActiveScene();
            Scene fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(fixture);
            var report = new List<string>();
            Exception failure = null;
            NavMeshData data = null;
            NavMeshDataInstance instance = default;
            try
            {
                var root = new GameObject("Free roam path fixture"); root.SetActive(false);
                rex = root.AddComponent<DinosaurAgent>(); rex.enabled = false;
                var animation = root.AddComponent<Animation>();
                var profile = AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>("Assets/Navigation/TRexCuriosity.asset");
                Require(profile != null, "Missing production T-Rex motion profile.");
                foreach (var motion in profile.motions)
                    if (motion.clip != null) animation.AddClip(motion.clip, motion.StateName);
                Set("legacyAnimation", animation); Set("interactionProfile", profile);
                Set("idleClip", profile.Find(InteractionMotionRole.LookForward).StateName);
                Set("moveClip", profile.Find(InteractionMotionRole.Walk).StateName);
                Set("random", new System.Random(813)); Set("movementHeight", 0f); Set("visualYaw", 0f);
                Set("startupRoamingMode", DinosaurAgent.RoamingMode.Free);
                Set("species", DinosaurAgent.DinosaurSpecies.Tyrannosaurus);
                Set("traceFreeRoamTurns", true); Set("curiosityPath", new NavMeshPath());
                var body = root.AddComponent<DinosaurCoreBody>();
                body.bands = new[] { new DinosaurCoreBody.Band { bounds = new Bounds(Vector3.up * .25f, new Vector3(.2f, .5f, .2f)) } };
                Set("interactionBody", body);
                var settingsRoot = new GameObject("Free roam path settings");
                settings = settingsRoot.AddComponent<DinosaurInteractionSettings>();
                settings.navigationAgentTypeId = NavMesh.GetSettingsByIndex(0).agentTypeID;
                typeof(DinosaurInteractionSettings).GetField("activityBoundsOverride", Flags).SetValue(settings, new Rect(250f, 250f, 100f, 100f));
                Set("interactionSettings", settings);
                data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(settings.navigationAgentTypeId),
                    new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                        size = new Vector3(40f, .2f, 40f), transform = Matrix4x4.TRS(Origin - Vector3.up * .1f, Quaternion.identity, Vector3.one) } },
                    new Bounds(Origin, new Vector3(50f, 10f, 50f)), Vector3.zero, Quaternion.identity);
                Require(data != null, "Fixture navigation did not bake.");
                instance = NavMesh.AddNavMeshData(data);
                typeof(DinosaurInteractionSettings).GetField("navigationInstance", Flags).SetValue(settings, instance);

                // Inject a projected origin to exercise the exact old 8 cm / 12 cm gap.
                foreach (var direction in new[] { Vector3.right, Vector3.left, Vector3.back })
                    foreach (float distance in new[] { .081f, .1f, .119f, .12f })
                    {
                        Reset(); Vector3 first = Origin + direction * distance;
                        Path(first, Origin + Vector3.forward * 5f);
                        Require(!(bool)Call("PathFinished"), "Fixture no longer reproduces the old arrival-tolerance gap.");
                        Require(Mathf.Abs(Vector3.SignedAngle(Vector3.forward, first - Origin, Vector3.up)) >= 40f,
                            "Fixture would not have requested a large turn.");
                        Call("NormalizeCuriosityStart", first);
                        Require((int)Get("curiosityCorner") == 1, "Safe projected start was not skipped: " + direction + " / " + distance);
                        Call("MoveCuriosity", rex.WalkSpeed);
                        Require(!rex.IsSteppingTurn, "Projected start still caused a turn.");
                    }
                report.Add("PASS 12 injected left/right/behind origins at 8.1–12 cm: old turn trigger reproduced; safe next point selected");

                // Also reproduce the gap using a real NavMesh projection, not only
                // injected corners. The allowed unsnapped root is 10 cm off an edge.
                var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
                Require(NavMesh.SamplePosition(Origin + Vector3.right * 30f, out var edge, 20f, filter), "Missing fixture navigation edge.");
                foreach (float facing in new[] { 0f, 180f })
                {
                    Reset();
                    Vector3 outside = new Vector3(edge.position.x + .1f, 0f, Origin.z);
                    rex.transform.SetPositionAndRotation(outside, Quaternion.Euler(0f, facing, 0f));
                    Vector3 target = new Vector3(edge.position.x - 1f, 0f, Origin.z + (facing == 0f ? 5f : -5f));
                    Require(NavMesh.SamplePosition(outside, out var projected, 1f, filter)
                        && Vector2.Distance(new Vector2(outside.x, outside.z), new Vector2(projected.position.x, projected.position.z)) > .08f,
                        "Real projection did not reproduce the old tolerance gap.");
                    Require((bool)Call("PlanCuriosityPath", target, true, false), "Projected-edge path did not plan.");
                    Require((int)Get("curiosityCorner") == 1, "Real projected edge start was not skipped.");
                    Call("MoveCuriosity", rex.WalkSpeed);
                    Require(!rex.IsSteppingTurn, "Real projected edge start still triggered a large turn.");
                }
                report.Add("PASS real baked NavMesh left/right 10 cm projection gap: full planner selected the forward connector");

                Reset(); Path(Origin + Vector3.right * .1f, Origin + Vector3.forward * 5f);
                Call("NormalizeCuriosityStart", Origin); // Not the projected origin.
                Require((int)Get("curiosityCorner") == 0, "A real bend was mistaken for a projected origin.");
                Reset(); Path(Origin + Vector3.right * .13f, Origin + Vector3.forward * 5f);
                Call("NormalizeCuriosityStart", Origin + Vector3.right * .13f);
                Require((int)Get("curiosityCorner") == 0, "Navigation tolerance expanded past 12 cm.");

                Reset(); Path(Origin + Vector3.right * .1f, Origin + Vector3.forward * 5f);
                settings.solidObstacles = new[] { new Bounds(Origin + Vector3.forward * 2f, new Vector3(1f, 2f, 1f)) };
                Call("NormalizeCuriosityStart", Origin + Vector3.right * .1f);
                Require((int)Get("curiosityCorner") == 0, "Shortcut crossed a solid obstacle.");
                Call("MoveCuriosity", rex.WalkSpeed);
                Require(!rex.IsSteppingTurn, "Blocked nearby bend triggered a large turn.");

                Reset(); Path(Origin + Vector3.right * .1f, Origin + Vector3.forward * 30f);
                Call("NormalizeCuriosityStart", Origin + Vector3.right * .1f);
                Require((int)Get("curiosityCorner") == 0, "Shortcut left the NavMesh.");

                Reset(); Path(Origin + Vector3.right * .1f, Origin + Vector3.forward * 5f);
                var peerRoot = new GameObject("Path fixture peer");
                var peer = peerRoot.AddComponent<DinosaurAgent>(); peer.enabled = false;
                peer.transform.position = Origin + Vector3.forward * 2.5f;
                typeof(DinosaurAgent).GetField("collisionRadius", Flags).SetValue(peer, .2f);
                typeof(DinosaurAgent).GetField("movementHeight", Flags).SetValue(peer, 0f);
                Set("collisionRadius", .2f); Set("peers", new[] { peer });
                Call("NormalizeCuriosityStart", Origin + Vector3.right * .1f);
                Require((int)Get("curiosityCorner") == 0, "Shortcut crossed a peer between its endpoints.");
                Set("peers", Array.Empty<DinosaurAgent>());
                report.Add("PASS real bend, >12 cm, solid, off-mesh and intervening peer remain blocked; no large turn toward blocked near point");

                Reset(); Set("freeGoal", Origin + Vector3.forward * 8f);
                Require((bool)Call("PlanCuriosityPath", Get("freeGoal"), true, false), "Real navigation planning failed.");
                Require((int)Get("curiosityCorner") == 1, "Real path retained its redundant origin.");
                Set("freeGoal", Origin + Vector3.right * 8f);
                CompleteTurn(90f);
                Require((int)Get("freeTurnsWithoutWalk") == 1 && (bool)Get("hasCuriosityPath"), "90 degree turn did not rejoin navigation.");
                Require((float)Get("freeRoamWalkTravel") == 0f, "Turn motion counted as ordinary walking.");
                Require(Mathf.Abs(Mathf.DeltaAngle(90f, rex.transform.eulerAngles.y)) < 1f, "Production right turn lost its heading.");

                Reset(); Set("freeGoal", Origin + Vector3.back * 12f);
                CompleteTurn(180f); CompleteTurn(90f);
                Require((int)Get("freeTurnsWithoutWalk") == 2, "Two turns for 180 degrees were not allowed.");
                Require(Mathf.Abs(Mathf.DeltaAngle(180f, rex.transform.eulerAngles.y)) < 2f, "Two production turns did not reach 180 degrees.");
                var use = Enum.Parse(typeof(DinosaurAgent).GetNestedType("TurnUse", BindingFlags.NonPublic), "Wander");
                Require(!(bool)Call("TryBeginSteppingTurn", 90f, use) && !rex.IsSteppingTurn,
                    "Third turn without walking was accepted.");
                Require(!(bool)Get("hasCuriosityPath") && rex.CurrentState == DinosaurAgent.DinosaurState.Idle
                    && (float)Get("stateTimer") == 2f && !(bool)Get("freeRetryGoal"), "Loop guard did not clear path and wait for a new goal.");

                Reset(); Set("freeTurnsWithoutWalk", 2); Call("RecordFreeRoamWalk", .49f);
                Require((int)Get("freeTurnsWithoutWalk") == 2, "Sub-threshold walking reset loop protection.");
                Call("RecordFreeRoamWalk", .011f);
                Require((int)Get("freeTurnsWithoutWalk") == 0 && (bool)Call("FreeRoamTurnAllowed"), "0.5 m of walking did not reset protection.");
                Set("freeTurnsWithoutWalk", 2); Set("freeRecoveryActive", true);
                Require((bool)Call("FreeRoamTurnAllowed"), "Ordinary loop guard blocked recovery.");
                Set("freeRecoveryActive", false); Set("frontActive", true);
                Require((bool)Call("FreeRoamTurnAllowed"), "Ordinary loop guard blocked interaction.");
                Set("frontActive", false); Set("startupRoamingMode", DinosaurAgent.RoamingMode.Route);
                Require((bool)Call("FreeRoamTurnAllowed"), "Ordinary loop guard blocked route mode.");
                report.Add("PASS production 90/180 degree completion and replanning, third-turn stop, 0.5 m progress reset, recovery/interaction/route exclusions");
                report.Add("PASS free roam path contracts (controlled projection gap reproduced; live reported scene cause still requires trace)");
            }
            catch (Exception error) { failure = error; report.Add("FAIL " + error); Debug.LogException(error); }
            finally
            {
                if (instance.valid) instance.Remove();
                if (data != null) Object.DestroyImmediate(data);
                SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(fixture, true);
                Directory.CreateDirectory("Logs"); File.WriteAllText(Result, string.Join("\n", report));
            }
            Debug.Log(string.Join("\n", report));
            if (failure != null) throw new InvalidOperationException("Free roam path acceptance failed; see " + Result, failure);
        }

        private static void Reset()
        {
            Call("ClearSteppingTurn"); Call("FreeIdle", 0f);
            rex.transform.SetPositionAndRotation(Origin, Quaternion.identity);
            Set("frontActive", false); Set("freeRecoveryActive", false);
            Set("freeRoamWalkTravel", 0f); Set("curiosityBlockedTime", 0f);
            settings.solidObstacles = Array.Empty<Bounds>();
        }
        private static void Path(Vector3 first, Vector3 next)
        {
            Set("curiosityCorners", new[] { first, next }); Set("curiosityCorner", 0); Set("hasCuriosityPath", true);
        }
        private static void CompleteTurn(float angle)
        {
            var use = Enum.Parse(typeof(DinosaurAgent).GetNestedType("TurnUse", BindingFlags.NonPublic), "Wander");
            Require((bool)Call("TryBeginSteppingTurn", angle, use), "Production turn did not start.");
            // Existing timing acceptance covers per-frame motion; exercise the completion
            // branch here with its real final pose, blend and navigation reconnection.
            Set("stepTurnTime", Get("stepTurnEnd"));
            Require((bool)Call("TickSteppingTurn") && !rex.IsSteppingTurn, "Production turn did not complete.");
        }
    }
}
