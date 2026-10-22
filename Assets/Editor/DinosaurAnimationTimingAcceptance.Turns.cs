using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static partial class DinosaurAnimationTimingAcceptance
    {
        private sealed class TurnCase
        {
            internal string use;
            internal float angle;
            internal bool repeat, block;
            internal float blockAt = .35f;
        }
        private static readonly List<TurnCase> TurnCases = MakeTurnCases();
        private static Transform turnCog;
        private static Vector3 turnFacing;
        private static Quaternion turnRotation;
        private static Vector3 turnOrigin;
        private static float turnEnd, blockedElapsed;
        private static int blockedFrames;
        private static DinosaurAgent turnBlocker;
        private static Vector3 blockerOrigin;
        private static bool injectedBlock;
        private static int TurnRate => Rates[scenario / TurnCases.Count];
        private static TurnCase CurrentTurnCase => TurnCases[scenario % TurnCases.Count];

        private static List<TurnCase> MakeTurnCases()
        {
            var result = new List<TurnCase>();
            foreach (string use in new[] { "Return", "Route", "Wander", "FrontFollow", "FrontReturn" })
                foreach (float angle in new[] { -90f, 90f, -60f, 60f })
                    result.Add(new TurnCase { use = use, angle = angle });
            foreach (float angle in new[] { -90f, -90f, 90f, 90f, -90f })
                result.Add(new TurnCase { use = "Return", angle = angle, repeat = true });
            result.Add(new TurnCase { use = "FrontReturn", angle = -90f, block = true });
            result.Add(new TurnCase { use = "FrontReturn", angle = 90f, block = true });
            result.Add(new TurnCase { use = "FrontReturn", angle = -90f, block = true, blockAt = .03f });
            result.Add(new TurnCase { use = "FrontReturn", angle = 90f, block = true, blockAt = .03f });
            return result;
        }

        [MenuItem("Jurassic World/Acceptance/Verify T-Rex Turn Synchronization")]
        public static void RunTurns()
        {
            SessionState.SetBool(Key + ".Turns", true);
            Run();
        }

        public static void RepairTurnsAndRun()
        {
            TRexTurnCalibration.Repair90DegreeTurns();
            RunTurns();
        }

        private static void AuditTurnCurves(DinosaurInteractionProfile profile, Animation animation)
        {
            foreach (var role in new[] { InteractionMotionRole.TurnLeft, InteractionMotionRole.TurnRight })
            {
                var item = profile.Find(role);
                Require(Mathf.Abs(item.turnPlaybackSpeed - 1f) < .0001f, "Turn must use authored playback speed.");
                var source = animation[item.sourceName].clip;
                var bindings = AnimationUtility.GetCurveBindings(source);
                Require(bindings.Length == AnimationUtility.GetCurveBindings(item.clip).Length, "Turn curve bindings differ.");
                foreach (var binding in bindings)
                {
                    var a = AnimationUtility.GetEditorCurve(source, binding);
                    var b = AnimationUtility.GetEditorCurve(item.clip, binding);
                    Require(b != null && a.keys.SequenceEqual(b.keys), "Turn curve differs from sampled source: " + binding.path + "/" + binding.propertyName);
                }
                var end = item.footFrames[item.footFrames.Length - 1];
                Require(Mathf.Abs(end.yaw + end.authoredYaw - item.turnDegrees) < .1f, "Turn does not finish at its authored heading.");
            }
        }

        private static void PrepareTurnScenario()
        {
            var test = CurrentTurnCase;
            var animation = rex.GetComponentInChildren<Animation>();
            var profile = (DinosaurInteractionProfile)Get("interactionProfile");
            if (scenario == 0)
            {
                AuditTurnCurves(profile, animation);
                AuditTurnCurves(AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>("Assets/Navigation/TRexCuriosity.asset"), animation);
                Report.Clear(); Report.AppendLine("# T-Rex turn synchronization\n\nOriginal source curves verified against playback copies. Simulated FPS; production movement checks and foot solver.\n");
                Report.AppendLine("|FPS|Use|Angle|Repeat|Blocked frames|Expected s|Active s|Foot error m|Heading error deg|Result|\n|---|---|---:|---|---:|---:|---:|---:|---:|---|");
                turnCog = TRexTurnCalibration.Bone(animation.transform, "jt_Cog_C");
                camera.fieldOfView = 40f;
                turnBlocker = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().First(a => a != rex);
                blockerOrigin = turnBlocker.transform.position;
            }
            turnBlocker.transform.position = blockerOrigin;
            bool continuePose = test.repeat && scenario % TurnCases.Count > 20;
            if (!continuePose)
            {
                if ((bool)Get("frontActive")) Call("FinishFrontInteraction");
                Call("ClearSteppingTurn");
                Vector3 p = layout.clearingPositions[0]; p.y = rex.MovementHeight;
                rex.transform.SetPositionAndRotation(p, layout.clearingRotations[0]);
                animation.Stop(); animation.Play((string)Get("idleClip"));
                animation[(string)Get("idleClip")].time = 0f; animation.Sample();
                feet.ResetSupportAnchors();
                Set("activeClip", (string)Get("idleClip"));
                Call("BeginInteractionSequence", false, false);
                Set("frontAction", null); Set("frontWaiting", false);
            }
            Set("peers", new[] { rex });
            Set("frontActive", test.use.StartsWith("Front", StringComparison.Ordinal));
            Set("frontStopPending", false); Set("frontExitPending", false); Set("frontNearPending", false);
            Set("routeRejoining", false); Set("routeJoinRecovering", false);
            motion = profile.Find(test.angle < 0 ? InteractionMotionRole.TurnLeft : InteractionMotionRole.TurnRight);
            Time.timeScale = 1f; Time.captureDeltaTime = 1f / TurnRate;
            var use = Enum.Parse(typeof(DinosaurAgent).GetNestedType("TurnUse", BindingFlags.NonPublic), test.use);
            var previousState = string.IsNullOrEmpty((string)Get("activeClip")) ? null : animation[(string)Get("activeClip")];
            bool sameState = previousState == animation[motion.StateName];
            turnFacing = Vector3.ProjectOnPlane(turnCog.forward, Vector3.up).normalized;
            turnRotation = rex.transform.rotation; turnOrigin = rex.transform.position;
            Require((bool)Call("TryBeginSteppingTurn", test.angle, use), "Turn did not start: " + test.use + " " + test.angle);
            Require((AnimationState)Get("stepTurnState") != (AnimationState)Get("stepTurnOutgoing"), "Incoming turn aliases outgoing state.");
            if (continuePose && (scenario % TurnCases.Count == 21 || scenario % TurnCases.Count == 23))
                Require(sameState, "Repeated direction did not exercise the shared-state regression.");
            turnEnd = (float)Get("stepTurnEnd"); expected = turnEnd;
            ticks = blockedFrames = 0; elapsed = error = blockedElapsed = 0f; injectedBlock = false;
            previousRoot = rex.transform.position; previousTravel = rex.AnimationRootTravel;
            deadline = EditorApplication.timeSinceStartup + 180;
            Vector3 forward = turnRotation * Quaternion.Euler(0f, rex.VisualYaw, 0f) * Vector3.forward;
            camera.transform.position = rex.transform.position + Quaternion.Euler(0f, 35f, 0f) * forward * 21f + Vector3.up * 3f;
            camera.transform.LookAt(rex.transform.position + Vector3.up * 2.5f);
            if (TurnRate == 60 && test.repeat)
            {
                if (encoder == null)
                    encoder = new MediaEncoder(RainforestSample.Output + "/turn-sync-repeat.mp4",
                        new VideoTrackAttributes { width = 640, height = 360, frameRate = new MediaRational(60), includeAlpha = false, bitRateMode = VideoBitrateMode.High });
            }
        }

        private static void TickTurnScenario()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Turn synchronization timeout.");
                var test = CurrentTurnCase;
                var animation = rex.GetComponentInChildren<Animation>();
                float before = rex.StepTurnClipTime;
                float beforeWeight = ((AnimationState)Get("stepTurnState")).weight;
                // Introduce a peer after stepping starts, then remove it after a short wait.
                if (test.block && !injectedBlock && before >= test.blockAt)
                {
                    // The root origin can fall between authored body bands depending on the
                    // turn direction, so place the peer inside the current performance envelope.
                    var probe = (DinosaurCoreBody)Get("frontProbeBody");
                    Vector3 peerPoint = rex.transform.position;
                    if (probe != null && probe.bands.Length > 0)
                    {
                        var band = probe.bands.OrderByDescending(b => b.bounds.size.x * b.bounds.size.z).First();
                        Vector3 forward = (Vector3)Call("GetVisualForward"); forward.y = 0f; forward.Normalize();
                        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
                        peerPoint += right * band.bounds.center.x + forward * band.bounds.center.z;
                    }
                    turnBlocker.transform.position = peerPoint;
                    Set("peers", new[] { rex, turnBlocker }); injectedBlock = true;
                }
                if (blockedFrames >= Mathf.CeilToInt(.1f * TurnRate))
                { turnBlocker.transform.position = blockerOrigin; Set("peers", new[] { rex }); }
                bool done;
                if (rex.FrontActionWaitingForSpace)
                {
                    Call("TickFrontSpaceWait"); done = false;
                }
                else done = (bool)Call("TickSteppingTurn");
                ticks++; elapsed += Time.deltaTime;
                float time = rex.StepTurnClipTime;
                if (!done && Mathf.Abs(time - before) < .00001f)
                {
                    blockedFrames++; blockedElapsed += Time.deltaTime;
                    Require(Vector3.Distance(previousRoot, rex.transform.position) < .001f, "Blocked root advanced.");
                    Require(Mathf.Abs(((AnimationState)Get("stepTurnState")).time - before) < .00001f, "Blocked turn animation advanced.");
                    Require(Mathf.Abs(((AnimationState)Get("stepTurnState")).weight - beforeWeight) < .00001f, "Blocked turn blend advanced.");
                }
                else if (!done)
                {
                    var state = (AnimationState)Get("stepTurnState");
                    Require(state.enabled && Mathf.Abs(state.time - time) < .00001f, "Turn pose clock differs from movement clock.");
                    if ((float)Get("stepTurnBlendTime") >= (float)Get("stepTurnBlendDuration"))
                    {
                        Require(state.weight > .999f, "Incoming turn was faded out or disabled.");
                        var pose = DinosaurAgent.SampleFootFrame(motion, time);
                        float visualYaw = Vector3.SignedAngle(turnFacing, Vector3.ProjectOnPlane(turnCog.forward, Vector3.up), Vector3.up);
                        Require(Mathf.Abs(Mathf.DeltaAngle(visualYaw, pose.yaw + pose.authoredYaw)) < 1f, "Visual pose did not turn with the sampled root.");
                    }
                    var sample = DinosaurAgent.SampleFootFrame(motion, time);
                    var rotation = Quaternion.Euler(0f, sample.yaw, 0f) * turnRotation;
                    Require(Quaternion.Angle(rex.transform.rotation, rotation) < .02f, "Root heading differs from sampled pose.");
                }
                typeof(DinosaurFootLock).GetMethod("LateUpdate", Flags).Invoke(feet, null);
                float allowed = rex.AnimationRootTravel - previousTravel
                    + Mathf.Min(2.4f, Mathf.Max(rex.WalkSpeed, rex.ApproachSpeed) * .8f) * Mathf.Min(Time.deltaTime, .05f) + .001f;
                Require(Vector3.Distance(previousRoot, rex.transform.position) <= allowed, "Root exceeded sampled travel and foot correction budget.");
                previousRoot = rex.transform.position; previousTravel = rex.AnimationRootTravel;
                float footError = Mathf.Max(feet.LeftPlanted ? feet.LeftPlantError : 0f, feet.RightPlanted ? feet.RightPlantError : 0f);
                if (footError > .2f && footError > error)
                    Debug.Log($"TURN_SYNC_SUPPORT case={scenario} time={time:F4} error={footError:F4} correction={feet.MaximumLegCorrection:F3} planted={feet.LeftPlanted}/{feet.RightPlanted} weight={((AnimationState)Get("stepTurnState")).weight:F3} leftYaw={feet.LeftYawError:F2} rightYaw={feet.RightYawError:F2}");
                error = Mathf.Max(error, footError);
                if (encoder != null)
                {
                    camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); frame.Apply(); RenderTexture.active = old; encoder.AddFrame(frame);
                }
                if (!done) return;
                float heading = Vector3.SignedAngle(turnFacing, Vector3.ProjectOnPlane(turnCog.forward, Vector3.up), Vector3.up);
                var end = DinosaurAgent.SampleFootFrame(motion, turnEnd);
                float headingError = Mathf.Abs(Mathf.DeltaAngle(heading, end.yaw + end.authoredYaw));
                Require(headingError < 1f, "Visual turn heading mismatch: " + headingError);
                Require(Mathf.Abs(elapsed - blockedElapsed - expected) <= 2f / TurnRate + .0001f, "Turn did not run at original speed.");
                Require(error <= .25f, "Planted foot error exceeds 0.25m: " + error);
                Require(Vector3.Distance(turnOrigin, rex.transform.position) > .1f, "Turn stayed on a fixed root pivot.");
                Require(!test.block || blockedFrames > 0, "Blocked resume was not exercised.");
                Report.AppendLine($"|{TurnRate}|{test.use}|{test.angle}|{test.repeat}|{blockedFrames}|{expected:F4}|{elapsed-blockedElapsed:F4}|{error:F4}|{headingError:F3}|PASS|");
                Debug.Log($"TURN_SYNC fps={TurnRate} use={test.use} angle={test.angle} repeat={test.repeat} blocked={blockedFrames} support={error:F4} headingError={headingError:F3}");
                if (!test.repeat || scenario % TurnCases.Count == 24) { encoder?.Dispose(); encoder = null; }
                File.WriteAllText(RainforestSample.Output + "/TurnSynchronization.md", "RUNNING\n\n" + Report);
                if (++scenario < Rates.Length * TurnCases.Count) PrepareTurnScenario(); else Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
    }
}
