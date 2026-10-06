using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class DinosaurRouteAcceptance
    {
        private const string RunningKey = "JurassicWorld.DinosaurRouteAcceptance.Running";
        private const string PreviewKey = "JurassicWorld.DinosaurRouteAcceptance.Preview";
        private const string ImagesKey = "JurassicWorld.DinosaurRouteAcceptance.Images";
        private static DinosaurAgent[] agents;
        private static Vector3[] previous;
        private static float[] travelled;
        private static float[] lastProgress;
        private static float startTime;
        private static double deadline;
        private static int scenario = -1;
        private static readonly int[,] RoutePairs = { { 0, 4 }, { 1, 2 }, { 3, 5 } };

        static DinosaurRouteAcceptance()
        {
            if (SessionState.GetBool(RunningKey, false))
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public static void VerifyGeometry()
        {
            MethodInfo build = typeof(MovementCorridor).GetMethod("BuildRoutes", BindingFlags.NonPublic | BindingFlags.Static);
            for (int seed = MovementCorridor.LayoutSeed; seed < MovementCorridor.LayoutSeed + 32; seed++)
            {
                var first = (Array)build.Invoke(null, new object[] { seed });
                var second = (Array)build.Invoke(null, new object[] { seed });
                for (int route = 0; route < first.Length; route++)
                {
                    object data = first.GetValue(route);
                    var type = data.GetType();
                    var samples = (MovementCorridor.CorridorSample[])type.GetField("Samples").GetValue(data);
                    var repeat = (MovementCorridor.CorridorSample[])type.GetField("Samples").GetValue(second.GetValue(route));
                    bool closed = (bool)type.GetField("Closed").GetValue(data);
                    Require(closed, "Every route must be a continuous loop.");
                    float length = (float)type.GetField("Length").GetValue(data);
                    Require(length >= MovementCorridor.MinRouteLength, "Route too short.");
                    for (int i = 0; i < samples.Length; i++)
                    {
                        Vector2 p = samples[i].Position;
                        Require(p == repeat[i].Position && samples[i].Distance == repeat[i].Distance, "Seed is not deterministic.");
                        Require(p.x >= -55f && p.x <= 55f && p.y >= -45f && p.y <= 77f, "Route outside safe core.");
                        Require(Mathf.Abs(samples[i].Tangent.magnitude - 1f) < .001f, "Invalid tangent.");
                        if (i > 0) Require(samples[i].Distance > samples[i - 1].Distance, "Degenerate segment.");
                        int segments = samples.Length - (closed ? 0 : 1);
                        for (int j = i + 2; j < segments && i < segments; j++)
                        {
                            if (closed && i == 0 && j == segments - 1) continue;
                            Vector2 a = p, b = samples[(i + 1) % samples.Length].Position;
                            Vector2 c = samples[j].Position, d = samples[(j + 1) % samples.Length].Position;
                            Require(!(Cross(b - a, c - a) * Cross(b - a, d - a) < -1e-6f
                                && Cross(d - c, a - c) * Cross(d - c, b - c) < -1e-6f), "Route self-intersection.");
                        }
                    }
                    if (closed)
                        Require(Vector2.Distance(samples[0].Position, samples[samples.Length - 1].Position) < .8f
                            && Vector2.Angle(samples[0].Tangent, samples[samples.Length - 1].Tangent) < 15f, "Loop seam discontinuity.");
                }
            }

            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            TerrainTreeObstacleRegistry.Invalidate();
            RouteObstacleClearance.Validate(Terrain.activeTerrain);
            for (int route = 0; route < MovementCorridor.RouteCount; route++)
            {
                int spawnSamples = 0;
                for (int i = 0; i < MovementCorridor.SampleCount(route); i++)
                {
                    Vector2 p = MovementCorridor.GetSample(route, i).Position;
                    foreach (var agent in UnityEngine.Object.FindObjectsOfType<DinosaurAgent>())
                    {
                        var core = agent.GetComponent<DinosaurCoreBody>(); if (core == null) continue;
                        Require(!TerrainTreeObstacleRegistry.IsCoreBlocked(p, MovementCorridor.GetSample(route, i).Tangent, core), "Core body intersects trunk on route " + route + " for " + agent.name);
                    }
                    if (!MovementCorridorCrossings.IsInside(route, p)) spawnSamples++;
                }
                Debug.Log($"ROUTE_GEOMETRY route={route} length={MovementCorridor.Length(route):F1} closed={MovementCorridor.IsClosed(route)} spawnSamples={spawnSamples}");
                float length = MovementCorridor.Length(route);
                Require(Vector2.Distance(MovementCorridor.PointAt(route, length), MovementCorridor.PointAt(route, 0f)) < .001f, "Loop position discontinuity.");
                MovementCorridor.GetSampleAt(route, -.01f, out _, out Vector2 beforeSeam);
                MovementCorridor.GetSampleAt(route, .01f, out _, out Vector2 afterSeam);
                Require(Vector2.Angle(beforeSeam, afterSeam) < 2f, "Loop heading discontinuity.");
            }
            Debug.Log("DINOSAUR_ROUTE_GEOMETRY_OK: 32 seeds; all loops, determinism, bounds, self-intersection, seams, full model clearance.");
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        public static void VerifyRefresh()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            TerrainData data = Terrain.activeTerrain.terrainData;
            float[,] heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            float[,,] layers = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            int count = data.treeInstanceCount;
            TreeInstance[] beforeTrees = data.treeInstances;
            var dinosaurs = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>();
            var dinosaurIds = new GlobalObjectId[dinosaurs.Length];
            var walkSpeeds = new float[dinosaurs.Length];
            var approachSpeeds = new float[dinosaurs.Length];
            for (int i = 0; i < dinosaurs.Length; i++)
            {
                dinosaurIds[i] = GlobalObjectId.GetGlobalObjectIdSlow(dinosaurs[i]);
                walkSpeeds[i] = dinosaurs[i].WalkSpeed; approachSpeeds[i] = dinosaurs[i].ApproachSpeed;
            }
            var transforms = UnityEngine.Object.FindObjectsOfType<Transform>();
            var positions = new Vector3[transforms.Length];
            var identifiers = new GlobalObjectId[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                positions[i] = transforms[i].position;
                identifiers[i] = GlobalObjectId.GetGlobalObjectIdSlow(transforms[i]);
            }
            GroundEcologyPass.RebuildRoutes();
            GroundEcologyPass.RebuildRoutes();
            Require(count == data.treeInstanceCount, "Refresh changed tree count.");
            TreeInstance[] afterTrees = data.treeInstances;
            for (int i = 0; i < afterTrees.Length; i++)
                Require(afterTrees[i].position == beforeTrees[i].position && afterTrees[i].prototypeIndex == beforeTrees[i].prototypeIndex
                    && afterTrees[i].widthScale == beforeTrees[i].widthScale && afterTrees[i].heightScale == beforeTrees[i].heightScale
                    && afterTrees[i].rotation == beforeTrees[i].rotation && afterTrees[i].color.Equals(beforeTrees[i].color), "Refresh changed terrain tree " + i);
            Require(UnityEngine.Object.FindObjectsOfType<Transform>().Length == transforms.Length, "Refresh changed scene object count.");
            Require(UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Length == dinosaurs.Length, "Refresh changed dinosaur count.");
            for (int i = 0; i < dinosaurIds.Length; i++)
            {
                var dinosaur = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(dinosaurIds[i]) as DinosaurAgent;
                Require(dinosaur != null && dinosaur.WalkSpeed == walkSpeeds[i] && dinosaur.ApproachSpeed == approachSpeeds[i], "Refresh changed dinosaur speeds.");
            }
            for (int i = 0; i < transforms.Length; i++)
            {
                var current = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(identifiers[i]) as Transform;
                Require(current != null && current.position == positions[i], "Repeated refresh changed object " + identifiers[i]);
            }
            float[,] after = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            for (int y = 0; y < after.GetLength(0); y++) for (int x = 0; x < after.GetLength(1); x++)
                Require(after[y, x] == heights[y, x], "Refresh changed terrain height.");
            float[,,] alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            for (int y = 0; y < alpha.GetLength(0); y++) for (int x = 0; x < alpha.GetLength(1); x++)
                for (int layer = 4; layer < alpha.GetLength(2); layer++)
                    Require(Mathf.Abs(alpha[y, x, layer] - layers[y, x, layer]) < .00001f, "Refresh changed creek bank.");
            Debug.Log("ROUTE_REFRESH_OK: repeated placement, counts, terrain heights and creek banks stable.");
        }

        public static void VerifyRefreshAndPreview()
        {
            try { VerifyRefresh(); CapturePreview(); }
            catch (Exception error) { Finish(error); }
        }

        // Batch entry: omit -quit so the asynchronous Play Mode verification can finish.
        public static void Run() => StartMovementCheck(true);

        public static void RunWithoutImages() => StartMovementCheck(false);

        private static void StartMovementCheck(bool images)
        {
            try
            {
                VerifyGeometry();
                SessionState.SetBool(PreviewKey, false);
                SessionState.SetBool(ImagesKey, images);
                SessionState.SetBool(RunningKey, true);
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        public static void CapturePreview()
        {
            SessionState.SetBool(PreviewKey, true);
            SessionState.SetBool(RunningKey, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            agents = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>();
            Require(agents.Length == 2, "Route verification expects the two enabled dinosaurs.");
            previous = new Vector3[agents.Length];
            travelled = new float[agents.Length];
            lastProgress = new float[agents.Length];
            startTime = Time.time;
            deadline = EditorApplication.timeSinceStartup + 150;
            for (int i = 0; i < agents.Length; i++)
            {
                previous[i] = agents[i].transform.position;
                lastProgress[i] = Time.time;
            }
            Time.timeScale = 20f;
            EditorApplication.update += VerifyMovement;
        }

        private static void BeginScenario(int index)
        {
            scenario = index;
            typeof(MovementCorridorCrossings).GetMethod("ResetOwners", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            for (int i = 0; i < agents.Length; i++)
            {
                int route = RoutePairs[index, i];
                bool placed = false;
                for (int sample = 0; sample < MovementCorridor.SampleCount(route); sample++)
                {
                    Vector2 point = MovementCorridor.GetSample(route, sample).Position;
                    if (MovementCorridorCrossings.IsInside(route, point, RoutePairs[index, 1 - i])) continue;
                    if (i > 0 && Vector2.Distance(point, new Vector2(previous[0].x, previous[0].z))
                        < agents[0].CollisionRadius + agents[i].CollisionRadius + 1f) continue;
                    agents[i].ApplySpawnPlacement(point, route);
                    agents[i].SendMessage("BeginRandomWalk");
                    previous[i] = agents[i].transform.position;
                    travelled[i] = 0f;
                    lastProgress[i] = Time.time;
                    placed = true;
                    break;
                }
                Require(placed, "No safe scenario placement for route " + route);
            }
            startTime = Time.time;
            deadline = EditorApplication.timeSinceStartup + 150;
            Debug.Log($"ROUTE_SCENARIO {index}: routes={RoutePairs[index, 0]},{RoutePairs[index, 1]}");
        }

        private static void VerifyMovement()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Movement verification timeout.");
                WorldNavigationHud nav = UnityEngine.Object.FindObjectOfType<WorldNavigationHud>();
                if (SessionState.GetBool(PreviewKey, false))
                {
                    if (nav == null || !nav.MapReady) return;
                    CaptureMap(nav);
                    CaptureForestViews();
                    Debug.Log("DINOSAUR_ROUTE_PREVIEW_OK");
                    Finish(null);
                    return;
                }
                if (scenario < 0)
                {
                    if (Time.time - startTime < 3f) return;
                    BeginScenario(0);
                    return;
                }
                if (SessionState.GetBool(ImagesKey, true) && nav != null && nav.MapReady && !File.Exists("Logs/routes-map.png"))
                    CaptureMap(nav);
                for (int i = 0; i < agents.Length; i++)
                {
                    Vector3 position = agents[i].transform.position;
                    var core = agents[i].GetComponent<DinosaurCoreBody>();
                    if (core != null)
                    {
                        Vector3 forward = agents[i].transform.rotation * Quaternion.Euler(0f, agents[i].VisualYaw, 0f) * Vector3.forward;
                        Require(!TerrainTreeObstacleRegistry.IsCoreBlocked(new Vector2(position.x,position.z),new Vector2(forward.x,forward.z).normalized,core), "Core body intersects a trunk during movement: " + agents[i].DisplayName);
                    }
                    float step = Vector3.Distance(previous[i], position);
                    Require(step < 5f, "Agent teleported: " + agents[i].DisplayName);
                    travelled[i] += step;
                    if (step > .01f) lastProgress[i] = Time.time;
                    Require(Time.time - lastProgress[i] < 120f, "Agent stuck: " + agents[i].DisplayName);
                    Require(MovementCorridor.MinDistanceToRoute(new Vector2(position.x, position.z), agents[i].RouteIndex) < .02f, "Agent left route.");
                    previous[i] = position;
                }
                Require(Vector3.Distance(agents[0].transform.position, agents[1].transform.position)
                    >= agents[0].CollisionRadius + agents[1].CollisionRadius - .01f, "Dinosaurs overlap.");
                if (Time.time - startTime < 400f) return;
                for (int i = 0; i < agents.Length; i++)
                {
                    Debug.Log($"ROUTE_MOVEMENT {agents[i].DisplayName} route={agents[i].RouteIndex} travelled={travelled[i]:F1}m");
                    Require(travelled[i] > MovementCorridor.Length(agents[i].RouteIndex) * 2f, "Agent did not complete route.");
                }
                if (scenario < RoutePairs.GetLength(0) - 1) { BeginScenario(scenario + 1); return; }
                Debug.Log("DINOSAUR_ROUTE_MOVEMENT_OK: all six routes; 1200 simulated seconds; full routes, continuous movement, peer clearance, no stuck agents.");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= VerifyMovement;
            Time.timeScale = 1f;
            if (error != null) Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        private static void CaptureMap(WorldNavigationHud nav)
        {
            var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
            RenderTexture old = RenderTexture.active;
            RenderTexture.active = nav.MapCamera.targetTexture;
            image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            image.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes("Logs/routes-map.png", image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
        }

        private static void CaptureForestViews()
        {
            Camera camera = Camera.main;
            Require(camera != null, "Missing forest preview camera.");
            DinosaurAgent dinosaur = agents[0].CollisionRadius > agents[1].CollisionRadius ? agents[0] : agents[1];
            RenderTexture originalTarget = camera.targetTexture, originalActive = RenderTexture.active;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var markers = UnityEngine.Object.FindObjectsOfType<MovementCorridorDebug>();
            var visibility = new bool[markers.Length];
            for (int i = 0; i < markers.Length; i++) { visibility[i] = markers[i].ShowMarkers; markers[i].SetShowMarkers(false); }
            try
            {
                camera.targetTexture = target;
                for (int route = 0; route < 2; route++)
                {
                    MovementCorridor.GetSampleAt(route, MovementCorridor.Length(route) * .24f, out Vector2 point, out Vector2 tangent);
                    dinosaur.ApplySpawnPlacement(point, route);
                    Vector3 center = dinosaur.transform.position;
                    Vector3 forward = new Vector3(tangent.x, 0f, tangent.y), right = new Vector3(tangent.y, 0f, -tangent.x);
                    Vector3 eye = center + right * 13f - forward * 4f;
                    eye.y = Terrain.activeTerrain.SampleHeight(eye) + Terrain.activeTerrain.transform.position.y + 1.7f;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(center + Vector3.up * 2.3f - eye));
                    camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                    File.WriteAllBytes($"Logs/routes-forest-{route}.png", image.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = originalTarget; RenderTexture.active = originalActive;
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                for (int i = 0; i < markers.Length; i++) markers[i].SetShowMarkers(visibility[i]);
                target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image);
            }
        }
    }
}
