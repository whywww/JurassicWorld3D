using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicWorld.Editor
{
    // Return-phase contract: waiting searches without turning, return turns play through
    // static scenery, and the first two seconds after a turn walk without static checks.
    [InitializeOnLoad]
    public static class TRexReturnWaitAcceptance
    {
        private const string Key = "JurassicWorld.ReturnWaitAcceptance";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float Rate = 60f;
        private const string Result = "Logs/trex-return-wait-result.txt";
        private const int Scenarios = 6;
        private static DinosaurAgent rex;
        private static DinosaurInteractionSettings settings;
        private static DinosaurFootLock feet;
        private static DinosaurAgent peer;
        private static DinosaurCoreBody originalBody;
        private static Camera camera;
        private static object frontStageReturn;
        private static int scenario, phase, lastFrame, originalVsync, originalRate;
        private static float began, driveBegan;
        private static Vector3 anchor, driveAnchor;
        private static Quaternion anchorRotation;
        private static double deadline;
        private static readonly StringBuilder Report = new StringBuilder();

        private static object Get(string field) => typeof(DinosaurAgent).GetField(field, Flags).GetValue(rex);
        private static void Set(string field, object value) => typeof(DinosaurAgent).GetField(field, Flags).SetValue(rex, value);
        private static object Call(string method, params object[] args) => typeof(DinosaurAgent).GetMethod(method, Flags).Invoke(rex, args);
        private static bool Flag(string field) => (bool)Get(field);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        static TRexReturnWaitAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Restore", false))
                { SessionState.SetBool(Key + ".Restore", false); EditorSceneManager.OpenScene("Assets/Scenes/Main.unity"); }
            };
        }

        [MenuItem("Jurassic World/Acceptance/Verify T-Rex Return Wait Search")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before return-wait acceptance.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            SessionState.SetBool(Key + ".Restore", true);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void Begin()
        {
            try
            {
                originalVsync = QualitySettings.vSyncCount; originalRate = Application.targetFrameRate;
                rex = TRexOpenFollowAcceptance.PrepareWorld(out camera);
                settings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
                originalBody = (DinosaurCoreBody)Get("interactionBody");
                // The fixture drives the stage directly; give the production finish a body to restore.
                Set("frontOriginalBody", originalBody);
                feet = rex.GetComponentInChildren<DinosaurFootLock>();
                if (feet != null) feet.enabled = false;
                frontStageReturn = Enum.Parse(typeof(DinosaurAgent).GetNestedType("FrontStage", BindingFlags.NonPublic), "ReturnWalk");
                Set("frontReturnWalkTime", 30f);
                Set("frontTestControlled", true); Set("frontTestWaiting", false);
                CreatePeer();
                Time.captureDeltaTime = 1f / Rate;
                Report.Clear();
                Report.AppendLine("# T-Rex return wait and guaranteed walk\n\nMain actor/profile in an isolated Play navigation fixture. Search, turn gates and the two-second opening walk are exercised through production fields and methods.\n");
                scenario = 0; lastFrame = -1; PrepareScenario();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void CreatePeer()
        {
            var root = new GameObject("Return wait peer");
            peer = root.AddComponent<DinosaurAgent>();
            peer.enabled = false;
            var body = root.AddComponent<DinosaurCoreBody>();
            body.bands = new[] { new DinosaurCoreBody.Band { bounds = new Bounds(Vector3.up * 2f, new Vector3(6f, 6f, 6f)) } };
            typeof(DinosaurAgent).GetField("coreBody", Flags).SetValue(peer, body);
            SetField(peer, "collisionRadius", .2f);
            SetField(peer, "movementHeight", rex.MovementHeight);
            root.SetActive(false);
        }

        private static void SetField(DinosaurAgent agent, string field, object value)
            => typeof(DinosaurAgent).GetField(field, Flags).SetValue(agent, value);

        private static void ResetReturn(Quaternion rotation)
        {
            if (rex.IsFrontInteractionActive) Call("FinishFrontInteraction");
            Call("ClearSteppingTurn");
            Call("CancelFrontWait");
            if (peer != null) peer.gameObject.SetActive(false);
            Set("peers", new[] { rex });
            settings.solidObstacles = Array.Empty<Bounds>();
            TerrainTreeObstacleRegistry.Invalidate();
            Set("frontActive", true);
            Set("frontStage", frontStageReturn);
            Set("frontWaiting", false); Set("frontWaitPlaying", false); Set("frontWaitHoldingPose", false); Set("frontWaitClip", null);
            Set("frontWaitReason", null); Set("frontBlockReason", null);
            Set("frontBlocked", 0f); Set("frontBlockingPeer", null); Set("frontPeerBlockedTime", 0f);
            Set("frontReturnBlockedAt", -1f);
            Set("frontReturnBlendUntil", 0f); Set("frontReturnFreeWalkUntil", 0f);
            Set("frontReturnAligning", false);
            Set("frontReturnWalked", 0f); Set("frontReturnHasHistory", true); Set("frontReturnIndex", 0);
            Set("frontReturnTurnMotion", null); Set("frontReturnTurnIndex", -1);
            Set("frontWaitReplans", 0);
            Set("hasCuriosityPath", false);
            Set("curiosityPath", new NavMeshPath());
            Set("curiosityCorners", Array.Empty<Vector3>()); Set("curiosityCorner", 0);
            var trail = (List<Vector3>)Get("frontTrail"); trail.Clear(); trail.Add(Vector3.zero);
            rex.transform.SetPositionAndRotation(Vector3.zero, rotation);
            var animation = rex.GetComponentInChildren<Animation>(); animation.Stop();
            var idle = (string)Get("idleClip"); animation.Play(idle); animation[idle].time = 0f; animation.Sample();
            feet?.ResetSupportAnchors();
        }

        private static void SetPath(Vector3 target)
        {
            Set("curiosityCorners", new[] { target }); Set("curiosityCorner", 0); Set("hasCuriosityPath", true);
        }

        private static void SetTrail(Vector3 target)
        {
            var trail = (List<Vector3>)Get("frontTrail"); trail.Clear(); trail.Add(target);
            Set("frontReturnIndex", 0); Set("frontReturnHasHistory", true);
        }

        private static void PrepareScenario()
        {
            phase = 0; began = Time.time; deadline = EditorApplication.timeSinceStartup + 180;
            switch (scenario)
            {
                case 0:
                    ResetReturn(Quaternion.identity);
                    SetTrail(Vector3.left * 4f);
                    break;
                case 1:
                    ResetReturn(Quaternion.identity);
                    SetPath(Vector3.forward * 4f);
                    Set("frontReturnFreeWalkUntil", Time.time + 2f);
                    settings.solidObstacles = new[] { new Bounds(Vector3.forward * 1.2f + Vector3.up * 2f, new Vector3(3f, 4f, 3f)) };
                    driveAnchor = rex.transform.position; driveBegan = Time.time;
                    break;
                case 2:
                    ResetReturn(Quaternion.identity);
                    SetPath(Vector3.left * 4f);
                    Set("frontReturnFreeWalkUntil", Time.time + 2f);
                    driveAnchor = rex.transform.position; driveBegan = Time.time;
                    break;
                case 3:
                    ResetReturn(Quaternion.identity);
                    SetPath(Vector3.forward * 4f);
                    Set("frontReturnFreeWalkUntil", Time.time - 1f);
                    settings.solidObstacles = new[] { new Bounds(Vector3.forward * .8f + Vector3.up * 2f, new Vector3(3f, 4f, 3f)) };
                    anchor = rex.transform.position; anchorRotation = rex.transform.rotation;
                    break;
                case 4:
                    ResetReturn(Quaternion.identity);
                    SetPath(Vector3.left * 4f);
                    break;
                case 5:
                    ResetReturn(Quaternion.identity);
                    Set("curiosityCorners", new[] { Vector3.back * .1f, Vector3.forward * 3f });
                    Set("curiosityCorner", 0); Set("hasCuriosityPath", true);
                    break;
            }
        }

        private static void Tick()
        {
            try
            {
                if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
                Require(EditorApplication.timeSinceStartup < deadline, "Return-wait fixture wall-clock timeout in case " + scenario + ".");
                if (scenario == 0) DriveWaitAndTurn();
                else if (scenario == 1) DriveFreeWalkThroughStatic();
                else if (scenario == 2) DriveFreeWalkCorner();
                else if (scenario == 3) DriveExpiredWalk();
                else if (scenario == 4) DrivePeerTurn();
                else DriveNearCorner();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Pass(string note)
        {
            Report.AppendLine($"- PASS case {scenario}: {note}");
            File.WriteAllText(Result, "RUNNING\n\n" + Report);
            if (++scenario < Scenarios) PrepareScenario();
            else Finish(null);
        }

        private static void DriveWaitAndTurn()
        {
            if (phase == 0)
            {
                anchor = rex.transform.position; anchorRotation = rex.transform.rotation;
                Require((bool)Call("FrontWaitCanResume"), "Return wait could not plan a route: " + rex.FrontBlockingReason);
                Require(!rex.IsSteppingTurn, "Waiting started the turn instead of only searching.");
                Require(Vector3.Distance(anchor, rex.transform.position) < .0001f, "Waiting moved the root.");
                Require(Quaternion.Angle(anchorRotation, rex.transform.rotation) < .0001f, "Waiting changed the heading.");
                Require(Get("frontReturnTurnMotion") != null, "Waiting did not prepare the turn action.");
                // A static obstacle across the authored sweep must not stop the performance.
                settings.solidObstacles = new[] { new Bounds(Vector3.forward * 1.2f + Vector3.up * 2f, new Vector3(4f, 5f, 4f)) };
                bool moving = (bool)Call("MoveFrontPath", rex.WalkSpeed, true, false);
                Require(!moving && rex.IsSteppingTurn, "The prepared return turn did not start.");
                Require(Equals(Get("stepTurnUse"), Enum.Parse(typeof(DinosaurAgent).GetNestedType("TurnUse", BindingFlags.NonPublic), "FrontReturn")),
                    "Return walk started the wrong turn type.");
                phase = 1;
                return;
            }
            if (phase == 1)
            {
                bool done = (bool)Call("TickSteppingTurn");
                Require(!Flag("frontWaiting"), "Static scenery interrupted a return turn.");
                if (!done) return;
                Require(!rex.IsSteppingTurn, "Return turn did not clear its state.");
                Require(rex.ReturnFreeWalkActive, "Completed return turn did not open the guaranteed walk.");
                float remaining = (float)Get("frontReturnFreeWalkUntil") - Time.time;
                Require(remaining > 1.7f && remaining <= 2.05f, "Guaranteed walk window is not two seconds: " + remaining.ToString("F2"));
                Require(!Flag("hasCuriosityPath"), "Return turn kept its old route instead of reconnecting.");
                // No turn-count brake: a second turn is accepted without any walking between them.
                Set("frontReturnFreeWalkUntil", Time.time - 1f);
                Vector3 forward = (Vector3)Call("GetVisualForward");
                SetPath(rex.transform.position - forward * 4f);
                bool straight = (bool)Call("MoveFrontPath", rex.WalkSpeed, true, false);
                Require(!straight && rex.IsSteppingTurn, "A second return turn without walking was refused.");
                Call("ClearSteppingTurn");
                settings.solidObstacles = Array.Empty<Bounds>();
                Pass("wait prepared the turn without moving; the static sweep played through; the window is 2 s and another turn is not braked");
            }
        }

        private static void DriveFreeWalkThroughStatic()
        {
            Require(rex.ReturnFreeWalkActive, "Guaranteed walk expired before reaching the obstacle.");
            bool moving = (bool)Call("MoveFrontPath", rex.WalkSpeed, true, false);
            Require(moving, "Guaranteed walk stopped at static scenery: " + rex.FrontBlockingReason);
            Require(!rex.IsSteppingTurn, "Guaranteed walk started a turn.");
            float distance = Vector3.Distance(driveAnchor, rex.transform.position);
            if (distance < 1.3f)
            {
                Require(Time.time - driveBegan < 1.85f, "Guaranteed walk did not cover ground: " + distance.ToString("F3"));
                return;
            }
            Require(distance > 1.3f, "Guaranteed walk did not cross the static obstacle.");
            Pass($"walked {distance:F2} m through a solid obstacle during the two-second window");
        }

        private static void DriveFreeWalkCorner()
        {
            Require(rex.ReturnFreeWalkActive, "Guaranteed walk expired before the corner check.");
            bool moving = (bool)Call("MoveFrontPath", rex.WalkSpeed, true, false);
            Require(!rex.IsSteppingTurn, "A large corner started a turn during the guaranteed walk.");
            if (Time.time - driveBegan < .6f) return;
            float distance = Vector3.Distance(driveAnchor, rex.transform.position);
            Require(distance > .15f, "Steering only did not move the actor into the corner: " + distance.ToString("F3"));
            Pass($"turned smoothly toward a 90-degree corner and walked {distance:F2} m without an authored turn");
        }

        private static void DriveExpiredWalk()
        {
            Require(!rex.ReturnFreeWalkActive, "Guaranteed walk did not expire.");
            bool moving = (bool)Call("MoveFrontPath", rex.WalkSpeed, true, false);
            Require(!moving, "Expired window still walked through the static obstacle.");
            Require(Flag("frontWaiting"), "Expired window did not enter the normal collision wait.");
            Require(Vector3.Distance(anchor, rex.transform.position) < .0001f, "Expired window moved through the obstacle.");
            Require(Quaternion.Angle(anchorRotation, rex.transform.rotation) < 2f, "Expired window rotated through the obstacle.");
            Call("CancelFrontWait");
            settings.solidObstacles = Array.Empty<Bounds>();
            Pass("after two seconds the same obstacle again stops the walk and starts the normal wait");
        }

        private static void DrivePeerTurn()
        {
            if (phase == 0)
            {
                bool moving = (bool)Call("MoveFrontPath", rex.WalkSpeed, true, false);
                Require(!moving && rex.IsSteppingTurn, "Return turn did not start for the peer check.");
                peer.gameObject.SetActive(true);
                peer.transform.SetPositionAndRotation(rex.transform.position, Quaternion.identity);
                Set("peers", new[] { rex, peer });
                Call("TickSteppingTurn");
                Require(rex.IsSteppingTurn, "Peer collision cleared the return turn.");
                Require(Flag("frontWaiting"), "Peer collision did not hold the return turn.");
                Require(!Flag("frontPeerClearing"), "Return turn yielded instead of holding its pose.");
                phase = 1; deadline = EditorApplication.timeSinceStartup + 60;
                return;
            }
            peer.transform.position = new Vector3(500f, rex.MovementHeight, 500f);
            Set("frontRetryAt", 0f);
            Call("TickFrontSpaceWait");
            Require(!Flag("frontWaiting"), "Return turn did not resume after the peer left.");
            bool done = (bool)Call("TickSteppingTurn");
            if (!done) return;
            Require(!rex.IsSteppingTurn, "Resumed return turn did not complete.");
            peer.gameObject.SetActive(false);
            Set("peers", new[] { rex });
            Pass("a peer froze the return turn and the turn completed after it moved away");
        }

        private static void DriveNearCorner()
        {
            Require((bool)Call("FrontReturnSkipNearCorner"), "A safe near corner was not skipped.");
            Require((int)Get("curiosityCorner") == 1, "Skipping the near corner did not advance the target.");
            Require(!rex.IsSteppingTurn, "Skipping a near corner started a turn.");
            Set("curiosityCorners", new[] { Vector3.back * .1f, Vector3.forward * 3f });
            Set("curiosityCorner", 0);
            settings.solidObstacles = new[] { new Bounds(Vector3.forward * 1.5f + Vector3.up * 2f, new Vector3(4f, 4f, 3f)) };
            Require(!(bool)Call("FrontReturnSkipNearCorner"), "An unchecked near-corner shortcut was accepted.");
            Require((int)Get("curiosityCorner") == 0, "A blocked near corner was skipped.");
            Require(!rex.IsSteppingTurn, "A blocked near corner started a turn instead of waiting.");
            Require(!string.IsNullOrEmpty(rex.FrontBlockingReason), "A blocked near corner has no reason.");
            settings.solidObstacles = Array.Empty<Bounds>();
            Pass("near corner skipped only across a checked connector; the blocked case keeps waiting without turning");
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Key, false);
            Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = originalVsync; Application.targetFrameRate = originalRate;
            if (feet != null) feet.enabled = true;
            if (settings != null) settings.solidObstacles = Array.Empty<Bounds>();
            if (peer != null) UnityEngine.Object.Destroy(peer.gameObject);
            string result = error == null ? "PASS RETURN_WAIT" : "FAILED " + error;
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Result, result + "\n\n" + Report);
            if (error == null) Debug.Log(result); else Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
