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
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    // Synthetic fixtures exercise real Update, NavMesh paths and body gates without
    // changing the production scene, imported models or their animation profiles.
    [InitializeOnLoad]
    public static class DinosaurGroundTrafficAcceptance
    {
        private const string Key = "JurassicWorld.GroundTrafficAcceptance";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string ReportPath = "Logs/ground-traffic-acceptance.txt";
        private static readonly string[] Cases = { "head-on", "crossing", "overtaking", "standing blocker", "initial circle overlap",
            "three-way chain", "narrow passing bay", "solid beside retreat", "blocked turn", "no exit then obstacle removed", "rex safety", "initial body overlap", "resume original goals", "retreat to wider section" };
        private static readonly List<DinosaurAgent> Agents = new List<DinosaurAgent>();
        private static readonly StringBuilder Report = new StringBuilder();
        private static DinosaurInteractionSettings settings;
        private static Vector3[] starts, previous;
        private static float[] travelled;
        private static float[,] previousPenetration;
        private static Vector3[] goals;
        private static bool[] goalReached;
        private static int scenario, frame;
        private static int traceSecond;
        private static float began;
        private static bool obstacleRemoved;
        private static object Call(DinosaurAgent a, string method, params object[] args)
            => typeof(DinosaurAgent).GetMethods(Private).Single(m => m.Name == method
                && m.GetParameters().Length == args.Length && m.GetParameters().All(p => !p.ParameterType.IsByRef)).Invoke(a, args);
        private static void Set(DinosaurAgent a, string field, object value)
            => typeof(DinosaurAgent).GetField(field, Private).SetValue(a, value);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        static DinosaurGroundTrafficAcceptance()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Begin;
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    SessionState.SetBool(Key, false);
                    if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(Key + ".Failed", false) ? 1 : 0);
                    else
                    {
                        string path = SessionState.GetString(Key + ".Scene", "");
                        if (!string.IsNullOrEmpty(path)) EditorSceneManager.OpenScene(path);
                    }
                }
            };
        }
        [MenuItem("Jurassic World/Acceptance/Verify Ground Priority and Clearance")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save current scene edits before running traffic acceptance.");
            SessionState.SetString(Key + ".Scene", SceneManager.GetActiveScene().path);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".Failed", false);
            EditorApplication.isPlaying = true;
        }
        private static void Begin()
        {
            try
            {
                Time.captureDeltaTime = 1f / 30f;
                Report.Clear(); scenario = 0;
                VerifyOrder();
                Prepare();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static DinosaurAgent Make(string name, DinosaurAgent.DinosaurSpecies species, Vector3 position)
        {
            var root = new GameObject(name); root.SetActive(false);
            root.transform.position = position;
            var body = root.AddComponent<DinosaurCoreBody>();
            body.bands = new[] { new DinosaurCoreBody.Band { layer = 0, bounds = new Bounds(new Vector3(0f, .4f, 0f), new Vector3(.6f, .8f, 1f)) } };
            var agent = root.AddComponent<DinosaurAgent>();
            Set(agent, "species", species); Set(agent, "roamingMode", DinosaurAgent.RoamingMode.Free);
            Set(agent, "interactionBody", body); Set(agent, "interactionSettings", settings);
            Set(agent, "collisionRadius", .75f); Set(agent, "walkSpeed", 2f);
            Set(agent, "displayName", name); Set(agent, "playerInteractionEnabled", false);
            root.SetActive(true);
            Agents.Add(agent);
            return agent;
        }
        private static void VerifyOrder()
        {
            var expected = new[] { DinosaurAgent.DinosaurSpecies.Tyrannosaurus, DinosaurAgent.DinosaurSpecies.Argentinosaurus,
                DinosaurAgent.DinosaurSpecies.Stegosaurus, DinosaurAgent.DinosaurSpecies.Rajasaurus, DinosaurAgent.DinosaurSpecies.Sauropelta,
                DinosaurAgent.DinosaurSpecies.Pachycephalosaurus, DinosaurAgent.DinosaurSpecies.Baurusuchus, DinosaurAgent.DinosaurSpecies.Velociraptor };
            for (int i = 0; i < expected.Length; i++) Make("rank-" + i, expected[i], new Vector3(i * 4f, 0f, 0f));
            for (int i = 0; i < Agents.Count; i++)
                for (int j = 0; j < Agents.Count; j++)
                    if (i != j) Require((bool)Call(Agents[i], "HasRightOfWay", Agents[j]) == (i < j), "Species total order failed.");
            var high = Agents[0]; var low = Agents[7];
            typeof(DinosaurAgent).GetProperty("CurrentState").SetValue(low, DinosaurAgent.DinosaurState.Approaching);
            Require((bool)Call(low, "HasRightOfWay", high), "Summon did not outrank ordinary rex.");
            typeof(DinosaurAgent).GetProperty("CurrentState").SetValue(high, DinosaurAgent.DinosaurState.Approaching);
            Require((bool)Call(high, "HasRightOfWay", low), "Two summoned dinosaurs ignored species order.");
            typeof(DinosaurAgent).GetProperty("CurrentState").SetValue(high, DinosaurAgent.DinosaurState.Idle);
            Call(low, "RegisterGroundConflict", high);
            typeof(DinosaurAgent).GetProperty("CurrentState").SetValue(low, DinosaurAgent.DinosaurState.Idle);
            Require((bool)Call(low, "HasRightOfWay", high), "Encounter priority changed mid-conflict.");
            Call(low, "FinishGroundTraffic"); Call(high, "FinishGroundTraffic");
            Set(low, "frontActive", true);
            var stage = typeof(DinosaurAgent).GetField("frontStage", Private);
            stage.SetValue(low, Enum.Parse(stage.FieldType, "Follow"));
            Require((bool)Call(low, "HasRightOfWay", high), "Player follow did not receive interaction priority.");
            stage.SetValue(low, Enum.Parse(stage.FieldType, "ReturnWalk"));
            Require(!(bool)Call(low, "HasRightOfWay", high), "Return retained interaction priority.");
            Set(low, "frontActive", false);
            Set(Agents[1], "species", DinosaurAgent.DinosaurSpecies.Tyrannosaurus);
            Require((bool)Call(high, "HasRightOfWay", Agents[1]) != (bool)Call(Agents[1], "HasRightOfWay", high), "Same species tie was ambiguous.");
            Set(low, "groundObstacleAware", false);
            Require(!(bool)Call(high, "IsGroundTrafficPeer", low), "Flying agent entered ground scheduling.");
            Require(DinosaurAgent.GroundSpeciesForName("Stegosaurus") == DinosaurAgent.DinosaurSpecies.Stegosaurus, "Legacy species mapping failed.");
            Report.AppendLine("PASS species order, summon/follow priority, return downgrade, frozen encounter tier, same-species tie and flight exclusion.");
            ClearAgents();
        }
        private static void ClearAgents()
        {
            foreach (var agent in Agents) if (agent != null) UnityEngine.Object.DestroyImmediate(agent.gameObject);
            Agents.Clear();
        }
        private static void Prepare()
        {
            ClearAgents();
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings.gameObject);
            var configuration = new GameObject("Traffic fixture navigation"); configuration.SetActive(false);
            settings = configuration.AddComponent<DinosaurInteractionSettings>(); settings.allowOffRouteInteraction = false;
            var build = NavMesh.GetSettingsByIndex(0); build.agentRadius = .2f; build.agentHeight = 1f;
            var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(100f, .2f, 100f), transform = Matrix4x4.TRS(new Vector3(0f, -.1f, 0f), Quaternion.identity, Vector3.one) } };
            settings.navigationAgentTypeId = build.agentTypeID;
            settings.navigationData = NavMeshBuilder.BuildNavMeshData(build, sources, new Bounds(Vector3.zero, new Vector3(110f, 10f, 110f)), Vector3.zero, Quaternion.identity);
            Require(settings.navigationData != null, "Fixture navigation build failed.");
            configuration.SetActive(true);
            var high = Make("High", DinosaurAgent.DinosaurSpecies.Sauropelta, Vector3.zero);
            var low = Make("Low", DinosaurAgent.DinosaurSpecies.Velociraptor, new Vector3(0f, 0f, 3f));
            Vector3 aGoal = new Vector3(0f, 0f, 15f), bGoal = new Vector3(0f, 0f, -15f);
            if (scenario == 1) { low.transform.position = new Vector3(-3f, 0f, 3f); bGoal = new Vector3(15f, 0f, 3f); }
            if (scenario == 2) { bGoal = new Vector3(0f, 0f, 15f); Set(low, "walkSpeed", 1f); }
            if (scenario == 3) bGoal = low.transform.position;
            if (scenario == 4) low.transform.position = new Vector3(0f, 0f, 1.2f);
            if (scenario == 11) low.transform.position = new Vector3(0f, 0f, .4f);
            if (scenario == 5) Make("Middle", DinosaurAgent.DinosaurSpecies.Pachycephalosaurus, new Vector3(1.2f, 0f, 1.2f));
            if (scenario == 6) settings.solidObstacles = new[] { new Bounds(new Vector3(-2.5f, .5f, 0f), new Vector3(1f, 1f, 8f)), new Bounds(new Vector3(2.5f, .5f, 0f), new Vector3(1f, 1f, 8f)) };
            if (scenario == 13) settings.solidObstacles = new[] { new Bounds(new Vector3(-2.5f, .5f, 0f), new Vector3(1f, 1f, 20f)), new Bounds(new Vector3(2.5f, .5f, 0f), new Vector3(1f, 1f, 20f)) };
            if (scenario == 7) settings.solidObstacles = new[] { new Bounds(new Vector3(0f, .5f, 6f), new Vector3(2f, 1f, 1f)) };
            if (scenario == 8) { low.transform.position = new Vector3(0f, 0f, 1.8f); settings.solidObstacles = new[] { new Bounds(new Vector3(.65f, .5f, 1.8f), new Vector3(.25f, 1f, 1.2f)) }; }
            if (scenario == 9)
            {
                low.transform.position = new Vector3(0f, 0f, 1.2f);
                settings.solidObstacles = new[] { new Bounds(new Vector3(-1.1f, .5f, .5f), new Vector3(.2f, 1f, 4f)),
                    new Bounds(new Vector3(1.1f, .5f, .5f), new Vector3(.2f, 1f, 4f)),
                    new Bounds(new Vector3(0f, .5f, -1.5f), new Vector3(2.4f, 1f, .2f)), new Bounds(new Vector3(0f, .5f, 2.5f), new Vector3(2.4f, 1f, .2f)) };
            }
            if (scenario == 10) { Set(high, "species", DinosaurAgent.DinosaurSpecies.Tyrannosaurus); low.transform.position = new Vector3(0f, 0f, 10f); aGoal = Vector3.zero; }
            foreach (var agent in Agents) Call(agent, "CachePeers");
            starts = new Vector3[Agents.Count]; previous = new Vector3[Agents.Count]; travelled = new float[Agents.Count];
            previousPenetration = new float[Agents.Count, Agents.Count];
            goals = new Vector3[Agents.Count]; goalReached = new bool[Agents.Count];
            for (int i = 0; i < Agents.Count; i++)
            {
                var agent = Agents[i]; starts[i] = previous[i] = agent.transform.position;
                Vector3 goal = i == 0 ? aGoal : i == 1 ? bGoal : new Vector3(-12f, 0f, 0f);
                goals[i] = goal;
                Set(agent, "freeGoal", goal);
                agent.transform.rotation = Quaternion.LookRotation((goal - agent.transform.position).sqrMagnitude > .001f ? goal - agent.transform.position : Vector3.forward);
                if (Vector3.Distance(goal, agent.transform.position) > .01f)
                {
                    Require((bool)Call(agent, "FreeNavigationReady"), "Fixture navigation unavailable.");
                    Call(agent, "PlanCuriosityPath", goal, true, true);
                    typeof(DinosaurAgent).GetProperty("CurrentState").SetValue(agent, DinosaurAgent.DinosaurState.RandomWalk);
                }
                else Set(agent, "stateTimer", 10000f);
            }
            for (int i = 0; i < Agents.Count; i++) for (int j = i + 1; j < Agents.Count; j++)
                previousPenetration[i, j] = (float)Call(Agents[i], "PeerPenetration", Agents[i].transform.position,
                    Agents[i].transform.rotation, Agents[i].GetComponent<DinosaurCoreBody>(), Agents[j]);
            began = Time.time; frame = -1; obstacleRemoved = false;
            traceSecond = -1;
        }
        private static void Tick()
        {
            if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                float elapsed = Time.time - began;
                if (scenario == 12 && (int)elapsed / 5 != traceSecond)
                {
                    traceSecond = (int)elapsed / 5;
                    for (int i = 0; i < Agents.Count; i++) Report.AppendLine($"TRACE {elapsed:F1}s {Agents[i].name}: pos={Agents[i].transform.position:F2} state={Agents[i].CurrentState} traffic={Agents[i].GroundTrafficStatus} reached={goalReached[i]}.");
                }
                for (int i = 0; i < Agents.Count; i++)
                {
                    travelled[i] += Vector3.Distance(previous[i], Agents[i].transform.position);
                    previous[i] = Agents[i].transform.position;
                    if (Vector3.Distance(previous[i], goals[i]) < .3f) goalReached[i] = true;
                    for (int j = i + 1; j < Agents.Count; j++)
                    {
                        float depth = (float)Call(Agents[i], "PeerPenetration", Agents[i].transform.position,
                            Agents[i].transform.rotation, Agents[i].GetComponent<DinosaurCoreBody>(), Agents[j]);
                        Require(depth <= previousPenetration[i, j] + .0001f, "New or deepening body intersection in " + Cases[scenario]);
                        previousPenetration[i, j] = depth;
                    }
                }
                if (scenario == 9 && !obstacleRemoved && elapsed >= 4f)
                {
                    Require(travelled[0] < .1f && travelled[1] < .1f, "No-exit fixture moved through a solid.");
                    settings.solidObstacles = Array.Empty<Bounds>(); obstacleRemoved = true;
                }
                bool cleared = scenario >= 12 ? goalReached.All(reached => reached)
                    : scenario == 10 ? Vector3.Distance(Agents[0].transform.position, Agents[1].transform.position) >= 20f
                    : scenario == 9 ? obstacleRemoved && travelled[1] >= 1f
                    : travelled[0] >= 3f && (scenario == 3 || travelled[1] >= 3f)
                        && (Agents.Count < 3 || travelled[2] >= 2f)
                        && Vector3.Distance(Agents[0].transform.position, Agents[1].transform.position) > 2.5f;
                if (scenario == 9 && elapsed > 7f) Require(cleared, "Did not resume within 3s of obstacle removal.");
                if (!cleared && elapsed < 30f) return;
                Require(cleared, "30s traffic timeout: " + Cases[scenario] + " / " + Agents[0].GroundTrafficStatus + " / " + Agents[1].GroundTrafficStatus);
                Report.AppendLine($"PASS {Cases[scenario]} in {elapsed:F2}s; travel {travelled[0]:F2}/{travelled[1]:F2}m.");
                if (++scenario < Cases.Length) Prepare(); else Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            SessionState.SetBool(Key + ".Failed", error != null);
            Report.AppendLine(error == null ? "ALL PASS" : "FAILED " + error);
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, Report.ToString());
            if (error != null) Debug.LogException(error); else Debug.Log(Report.ToString());
            EditorApplication.isPlaying = false;
        }
    }
}
