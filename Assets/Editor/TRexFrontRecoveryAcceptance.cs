using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Controlled corridors isolate exit selection from forest planting and authored body size.
    [InitializeOnLoad]
    public static class TRexFrontRecoveryAcceptance
    {
        private const string Key = "JurassicWorld.FrontRecoveryAcceptance";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly int[] Rates = { 30, 60, 144 };
        private static DinosaurAgent rex;
        private static DinosaurInteractionSettings settings;
        private static DinosaurInteractionProfile profile;
        private static DinosaurCoreBody body;
        private static NavMeshDataInstance mesh;
        private static NavMeshData data;
        private static readonly Vector3 Origin = new Vector3(300f, 0f, 300f);
        private static int test, rate, frames, lastFrame;
        private static float began;
        private static bool sawTurn, moved;
        private static readonly List<string> Results = new List<string>();
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static object Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static TRexFrontRecoveryAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify T-Rex Interaction Recovery")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Run recovery fixtures from a saved scene outside Play.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }
        private static void Begin()
        {
            try
            {
                var terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(10f, 1f, 10f) };
                Terrain.CreateTerrainGameObject(terrainData).transform.position = new Vector3(1000f, 0f, 1000f);
                TerrainTreeObstacleRegistry.Invalidate();
                var root = new GameObject("Recovery fixture rex");
                rex = root.AddComponent<DinosaurAgent>(); rex.enabled = false;
                var visual = new GameObject("Recovery fixture visual"); visual.transform.SetParent(root.transform, false);
                var animation = visual.AddComponent<Animation>();
                Set("legacyAnimation", animation);
                Set("viewCamera", new GameObject("Recovery fixture player").AddComponent<Camera>());
                Set("curiosityPath", new NavMeshPath());
                settings = new GameObject("Recovery fixture settings").AddComponent<DinosaurInteractionSettings>();
                settings.navigationAgentTypeId = NavMesh.GetSettingsByIndex(0).agentTypeID;
                Set("interactionSettings", settings);
                body = new GameObject("Recovery fixture body").AddComponent<DinosaurCoreBody>();
                body.bands = new[] { new DinosaurCoreBody.Band { bounds = new Bounds(Vector3.up * .25f, new Vector3(.2f, .5f, .2f)) } };
                var envelope = new GameObject("Recovery fixture animation envelope").AddComponent<DinosaurCoreBody>();
                envelope.bands = body.bands; Set("frontEnvelope", envelope);
                profile = ScriptableObject.CreateInstance<DinosaurInteractionProfile>();
                var motions = new List<DinosaurInteractionProfile.Motion>();
                foreach (var role in new[] { InteractionMotionRole.LookForward, InteractionMotionRole.Walk, InteractionMotionRole.FollowWalk,
                    InteractionMotionRole.TurnLeft, InteractionMotionRole.TurnRight, InteractionMotionRole.Turn180Look })
                {
                    var clip = new AnimationClip { legacy = true, name = "RecoveryFixture_" + role };
                    clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0f, .2f, 0f));
                    float yaw = role == InteractionMotionRole.TurnLeft ? -90f : role == InteractionMotionRole.TurnRight ? 90f
                        : role == InteractionMotionRole.Turn180Look ? 180f : 0f;
                    var motion = new DinosaurInteractionProfile.Motion { role = role, sourceName = clip.name, clip = clip,
                        bodyBands = body.bands, footFrames = new[] {
                            new DinosaurInteractionProfile.FootFrame { time = 0f, leftContact = true, rightContact = true },
                            new DinosaurInteractionProfile.FootFrame { time = .2f, yaw = yaw, rootOffset = new Vector2(0f, .2f), leftContact = true, rightContact = true } } };
                    motions.Add(motion);
                    animation.AddClip(clip, motion.StateName);
                }
                profile.motions = motions.ToArray(); Set("interactionProfile", profile);
                Set("moveClip", profile.Find(InteractionMotionRole.Walk).StateName);
                Set("idleClip", profile.Find(InteractionMotionRole.LookForward).StateName);
                var build = NavMesh.GetSettingsByID(settings.navigationAgentTypeId);
                data = NavMeshBuilder.BuildNavMeshData(build, new List<NavMeshBuildSource> {
                    new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(80f, .2f, 80f),
                        transform = Matrix4x4.TRS(Origin - Vector3.up * .1f, Quaternion.identity, Vector3.one) } },
                    new Bounds(Origin, new Vector3(100f, 20f, 100f)), Vector3.zero, Quaternion.identity);
                Require(data != null, "Fixture navigation bake failed."); mesh = NavMesh.AddNavMeshData(data);
                typeof(DinosaurInteractionSettings).GetField("navigationInstance", Flags).SetValue(settings, mesh);
                Set("frontOriginalBody", body); Set("interactionBody", body);
                Set("frontActive", true); Set("frontAction", null); Set("frontFollowDuration", 1000f);
                Set("lostSightGrace", 1000f);
                rate = test = 0; Results.Clear(); StartCase(); EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void StartCase()
        {
            Time.captureDeltaTime = 1f / Rates[rate];
            Call("ClearFrontRecovery"); Call("ClearSteppingTurn"); Call("CancelFrontWait");
            Set("frontExitPending", false); Set("frontStopPending", false); Set("frontFollowElapsed", 0f);
            Set("frontTimingFollow", false);
            var stage = typeof(DinosaurAgent).GetField("frontStage", Flags);
            stage.SetValue(rex, Enum.Parse(stage.FieldType, test < 3 ? "Follow" : "ReturnWalk"));
            Set("interactionBody", body); Set("frontOriginalBody", body);
            rex.transform.SetPositionAndRotation(Origin, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            // Half-plane bound rejects every walking heading except the chosen exit.
            int direction = test % 3;
            Rect area = direction == 0 ? new Rect(299.6f, 280f, .8f, 20.35f)
                : direction == 1 ? new Rect(280f, 299.6f, 20.35f, .8f) : new Rect(299.65f, 299.6f, 20f, .8f);
            typeof(DinosaurInteractionSettings).GetField("activityBoundsOverride", Flags).SetValue(settings, area);
            settings.solidObstacles = new[] { new Bounds(Origin + Vector3.forward * .2f, new Vector3(.6f, 2f, .6f)) };
            Vector3 goal = Origin + (direction == 0 ? Vector3.back : direction == 1 ? Vector3.left : Vector3.right) * 8f;
            Set("lastSeenPlayer", goal);
            var trail = (List<Vector3>)Get("frontTrail"); trail.Clear(); trail.Add(goal); Set("frontReturnIndex", 0);
            Set("frontWaitBegan", Time.time - settings.spaceFailureTimeout - .1f);
            Set("frontWaiting", true); Set("frontWaitReason", "Solid obstacle"); Set("frontBlockReason", "Solid obstacle");
            Call("TickFrontSpaceWait");
            Require((bool)Get("frontRecoveryActive"), "Static wait did not activate recovery.");
            Require(rex.IsSteppingTurn, "No animated escape selected: " + direction);
            var selected = (DinosaurInteractionProfile.Motion)Get("stepTurnMotion");
            var expected = direction == 0 ? InteractionMotionRole.Turn180Look : direction == 1 ? InteractionMotionRole.TurnLeft : InteractionMotionRole.TurnRight;
            Require(selected.role == expected, "Wrong escape: expected " + expected + " got " + selected.role);
            Require((float)Get("stepTurnEnd") == selected.clip.length, "Escape truncated the authored turn.");
            // A static-blocked turn still starts; peer bodies remain hard blockers.
            Require(settings.PoseBlockReason(new Vector2(300f, 300f), Vector2.up, body) != null, "Fixture start is not blocked.");
            sawTurn = moved = false; frames = 0; began = Time.time; lastFrame = -1;
        }
        private static void Tick()
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                Vector3 before = rex.transform.position;
                bool turning = rex.IsSteppingTurn;
                if (turning) sawTurn = true;
                Call("TickFrontRecovery");
                if (!turning)
                {
                    float travel = Vector3.Distance(before, rex.transform.position);
                    Require(travel <= rex.WalkSpeed * Time.deltaTime + .001f, "Recovery teleported.");
                    moved |= travel > .0001f;
                }
                frames++;
                if (!(bool)Get("frontRecoveryActive"))
                {
                    Require(sawTurn && moved, "Recovery did not turn and walk.");
                    Require(rex.FrontInteractionStage.StartsWith(test < 3 ? "Follow" : "ReturnWalk"), "Recovery changed interaction stage.");
                    Require((bool)Call("FreeRecoveryRestPose", rex.transform.position, rex.transform.rotation), "Recovery ended in unsafe space.");
                    Results.Add($"PASS {Rates[rate]} FPS case {test}: direction={test % 3}, frames={frames}, stage={rex.FrontInteractionStage}");
                    if (++test < 6) { StartCase(); return; }
                    VerifyWaitAndPeer();
                    test = 0;
                    if (++rate < Rates.Length) { StartCase(); return; }
                    Finish(null); return;
                }
                Require(Time.time - began < 20f, $"Recovery did not finish: {rex.FrontInteractionStage}, position={rex.transform.position:F4}, rotation={rex.transform.eulerAngles}, target={Get("frontRecoveryTarget")}, blocked={rex.FrontBlockingReason}, last={rex.LastMovementBlock}, boundary={Call("FreeRecoveryBoundary", rex.transform.position, rex.transform.rotation)}, nav={Call("FreeRecoveryNavDistance", rex.transform.position)}");
            }
            catch (Exception error) { Finish(error); }
        }
        private static void VerifyWaitAndPeer()
        {
            Call("ClearFrontRecovery"); Call("ClearSteppingTurn");
            rex.transform.SetPositionAndRotation(Origin, Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            Set("interactionBody", body);
            settings.solidObstacles = new[] { new Bounds(Origin, Vector3.one * 100f) };
            Call("BeginFrontRecovery");
            Require((bool)Get("frontRecoveryActive") && !rex.IsSteppingTurn && !(bool)Get("frontRecoveryWalking"), "Sealed space triggered a useless turn.");
            Require((float)Get("frontRecoveryRetryAt") >= Time.time + 1.9f, "No-exit retry is not delayed.");
            Call("TickFrontRecovery"); Require(rex.transform.position == Origin, "Waiting moved without an exit.");
            settings.solidObstacles = Array.Empty<Bounds>();
            typeof(DinosaurInteractionSettings).GetField("activityBoundsOverride", Flags).SetValue(settings, new Rect(260f, 260f, 80f, 80f));
            Set("frontRecoveryRetryAt", 0f); Call("TickFrontRecovery");
            Require((bool)Get("frontRecoveryWalking"), "Opening an exit did not resume recovery.");
            var peer = new GameObject("Recovery peer fixture").AddComponent<DinosaurAgent>(); peer.enabled = false;
            try
            {
                peer.transform.position = rex.transform.position;
                typeof(DinosaurAgent).GetField("interactionBody", Flags).SetValue(peer, body);
                Set("peers", new[] { peer });
                Require(!(bool)Call("FrontAnimationPoseSafe", Origin, Origin, rex.transform.rotation, rex.transform.rotation, body), "Turn ignored a dinosaur body.");
                var use = Enum.Parse(typeof(DinosaurAgent).GetNestedType("TurnUse", BindingFlags.NonPublic), "FrontRecovery");
                var turn = profile.Find(InteractionMotionRole.TurnRight);
                Call("BeginSteppingTurn", turn, turn.clip.length, use);
                Call("TickFrontRecovery");
                Require(!rex.IsSteppingTurn && (bool)Get("frontRecoveryActive") && rex.transform.position == Origin,
                    "Blocked recovery turn advanced through a peer.");
                Require(!rex.ApplyFootRootCorrection(Vector3.right * .01f), "Recovery foot correction ignored a peer.");
            }
            finally { Set("peers", Array.Empty<DinosaurAgent>()); Object.DestroyImmediate(peer.gameObject); }
            Call("ClearFrontRecovery"); Call("ClearSteppingTurn");
            VerifyRecoveryWalking();
            Results.Add($"PASS {Rates[rate]} FPS sealed wait, delayed retry, opened exit and peer gate");
        }
        private static void VerifyRecoveryWalking()
        {
            rex.transform.SetPositionAndRotation(Origin, Quaternion.identity);
            Set("interactionBody", body);
            settings.solidObstacles = new[] { new Bounds(Origin, Vector3.one),
                new Bounds(Origin + Vector3.forward * 3.5f, Vector3.one) };
            Call("CaptureFrontRecoveryPose");
            Require((bool)Call("FreeRecoveryStep", Origin, Origin + Vector3.forward * 2f, Quaternion.identity, Quaternion.identity, true), "Recovery could not leave original rock.");
            Require(!(bool)Call("FreeRecoveryStep", Origin + Vector3.forward * 2f, Origin, Quaternion.identity, Quaternion.identity, false), "Recovery re-entered an exited rock.");
            Require(!(bool)Call("FreeRecoveryStep", Origin + Vector3.forward * 2f, Origin + Vector3.forward * 4f, Quaternion.identity, Quaternion.identity, false), "Recovery entered a new rock.");
            settings.solidObstacles = Array.Empty<Bounds>();
            var treeObject = new GameObject("Recovery tree fixture");
            try
            {
                treeObject.transform.position = Origin;
                var tree = treeObject.AddComponent<SceneTreeObstacle>();
                tree.SetRadius(.5f);
                tree.trunkSections = new[] { new TreeTrunkSection { radius = .5f, bottom = -100f, top = 100f } };
                TerrainTreeObstacleRegistry.Invalidate(); Call("CaptureFrontRecoveryPose");
                Require((bool)Call("FreeRecoveryStep", Origin, Origin + Vector3.forward * 2f, Quaternion.identity, Quaternion.identity, true), "Recovery could not leave original tree.");
                Require(!(bool)Call("FreeRecoveryStep", Origin + Vector3.forward * 2f, Origin, Quaternion.identity, Quaternion.identity, false), "Recovery re-entered an exited tree.");
            }
            finally { Object.DestroyImmediate(treeObject); TerrainTreeObstacleRegistry.Invalidate(); }
            typeof(DinosaurInteractionSettings).GetField("activityBoundsOverride", Flags).SetValue(settings, new Rect(299f, 299f, 2f, 2f));
            rex.transform.position = Origin + Vector3.right * 1.2f; Call("CaptureFrontRecoveryPose");
            Require((bool)Call("FreeRecoveryStep", rex.transform.position, Origin, Quaternion.identity, Quaternion.identity, false), "Boundary recovery rejected inward travel.");
            Require(!(bool)Call("FreeRecoveryStep", rex.transform.position, rex.transform.position + Vector3.right, Quaternion.identity, Quaternion.identity, false), "Boundary recovery moved farther out.");
            typeof(DinosaurInteractionSettings).GetField("activityBoundsOverride", Flags).SetValue(settings, new Rect(240f, 240f, 120f, 120f));
            var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = NavMesh.AllAreas };
            Require(NavMesh.SamplePosition(Origin + Vector3.right * 45f, out var edge, 20f, filter), "Fixture has no navigation edge.");
            rex.transform.position = new Vector3(edge.position.x + 1f, 0f, Origin.z); Call("CaptureFrontRecoveryPose");
            Require((bool)Call("FreeRecoveryStep", rex.transform.position, new Vector3(edge.position.x - 2f, 0f, Origin.z), Quaternion.identity, Quaternion.identity, false), "Off-mesh recovery rejected inward travel.");
            Require(!(bool)Call("FreeRecoveryStep", rex.transform.position, rex.transform.position + Vector3.right, Quaternion.identity, Quaternion.identity, false), "Recovery moved farther off navigation.");
            // Existing follow timeout still ends an interaction while its escape is waiting.
            var stage = typeof(DinosaurAgent).GetField("frontStage", Flags);
            stage.SetValue(rex, Enum.Parse(stage.FieldType, "Follow"));
            Set("frontRecoveryActive", true); Set("frontRecoveryWalking", false);
            Set("frontRecoveryRetryAt", Time.time + 10f);
            Set("frontTimingFollow", true); Set("frontFollowElapsed", 999.999f);
            Set("frontExitPending", false);
            Call("TickFrontInteraction");
            Require(!(bool)Get("frontRecoveryActive") && rex.FrontInteractionStage.StartsWith("Ending"), "Recovery suppressed the follow timeout.");
            Set("frontTimingFollow", false); Set("frontFollowElapsed", 0f);
            Results.Add($"PASS {Rates[rate]} FPS original overlap exit, no re-entry/new obstacle, tree identity, boundary and navigation recovery");
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Key, false); Time.captureDeltaTime = 0f;
            if (mesh.valid) mesh.Remove();
            if (data != null) Object.DestroyImmediate(data);
            string report = string.Join("\n", Results) + "\n" + (error == null ? "PASS interaction recovery fixtures" : error.ToString());
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/trex-front-recovery-result.txt", report);
            if (error != null) Debug.LogException(error); else Debug.Log(report);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1); else EditorApplication.isPlaying = false;
        }
    }
}
