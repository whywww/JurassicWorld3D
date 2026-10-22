using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static partial class DinosaurGaitAcceptance
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        internal const string Output = "Reports/ThreeAnimalGaits";
        [Serializable] private sealed class BaselineAnimal
        {
            public string species; public float walkSpeed, groundSpeed, visualY, loopStart, loopEnd; public bool footLock;
        }
        [Serializable] private sealed class BaselineData { public BaselineAnimal[] animals; }
        private static BaselineData baseline;
        private static readonly StringBuilder Report = new StringBuilder();
        private static void Set(DinosaurAgent agent, string field, object value)
        { typeof(DinosaurAgent).GetField(field, Flags).SetValue(agent, value); }
        [MenuItem("Jurassic World/Acceptance/Repair and Verify Three Animal Gaits")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            Report.Clear(); Report.AppendLine("# Three animal gait acceptance\n\nDeterministic scene-scale animation sampling; independent sole anchors measure drift. Videos use the same tracking camera.\n");
            Report.AppendLine("|Animal|Version|FPS|Case|Max stance drift m|Max reach error m|Max release jump m|Landings|\n|---|---|---:|---|---:|---:|---:|---:|");
            try
            {
                DinosaurGaitCalibration.Require(!EditorApplication.isPlayingOrWillChangePlaymode
                    && !EditorSceneManager.GetActiveScene().isDirty, "Stop Play and save scene edits first.");
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                string baselinePath = Output + "/Baseline.json";
                if (File.Exists(baselinePath)) baseline = JsonUtility.FromJson<BaselineData>(File.ReadAllText(baselinePath));
                else
                {
                    DinosaurGaitCalibration.Require(!Object.FindObjectsOfType<DinosaurAgent>().Any(a =>
                        DinosaurGaitCalibration.Species.Contains(a.name) && a.GaitProfile != null),
                        "Restore an uncalibrated scene before creating a new before-repair baseline.");
                    baseline = new BaselineData { animals = Object.FindObjectsOfType<DinosaurAgent>()
                        .Where(a => DinosaurGaitCalibration.Species.Contains(a.name)).Select(a => new BaselineAnimal {
                            species = a.name, walkSpeed = a.WalkSpeed, groundSpeed = a.MoveAnimationGroundSpeed,
                            visualY = a.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.localPosition.y,
                            loopStart = new SerializedObject(a).FindProperty("moveClipLoopStart").floatValue,
                            loopEnd = new SerializedObject(a).FindProperty("moveClipLoopEnd").floatValue,
                            footLock = new SerializedObject(a.GetComponent<DinosaurFbxLoader>()).FindProperty("enableFootLock").boolValue
                        }).ToArray() };
                    File.WriteAllText(baselinePath, JsonUtility.ToJson(baseline, true));
                }
                foreach (string species in DinosaurGaitCalibration.Species)
                {
                    var original = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == species);
                    var probe = Object.Instantiate(original.gameObject); probe.name = species;
                    DinosaurGaitProfile profile;
                    try { profile = DinosaurGaitCalibration.Bake(probe.GetComponent<DinosaurAgent>()); }
                    finally { Object.DestroyImmediate(probe); }
                    try
                    {
                        Simulate(original, profile, false, 60, "straight", true);
                        foreach (int rate in new[] { 30, 60, 144 })
                            foreach (string scenario in new[] { "straight", "turn", "turn-in-place", "stop-start", "blocked", "short-step", "reverse" })
                                Simulate(original, profile, true, rate, scenario, rate == 60 && scenario == "straight");
                    }
                    finally { Object.DestroyImmediate(profile); }
                }
                // Apply only after every targeted animation and leg passes.
                foreach (string species in DinosaurGaitCalibration.Species)
                {
                    var agent = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == species);
                    DinosaurGaitCalibration.Apply(agent);
                    float height = agent.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.localPosition.y;
                    float speed = agent.WalkSpeed;
                    DinosaurGaitCalibration.Apply(agent);
                    DinosaurGaitCalibration.Require(Mathf.Abs(height - agent.GetComponent<DinosaurFbxLoader>().ModelRoot.transform.localPosition.y) < .00001f
                        && Mathf.Abs(speed - agent.WalkSpeed) < .00001f, "Calibration is not idempotent: " + species);
                }
                Report.AppendLine("\nPASS repeated calibration: model height and ground speed unchanged on the second bake.\n");
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                File.WriteAllText(Output + "/Acceptance.md", Report + "\nPASS: scene calibration saved.\n");
                Debug.Log("PASS THREE_ANIMAL_GAITS");
            }
            catch (Exception error)
            {
                File.WriteAllText(Output + "/Acceptance.md", Report + "\nFAILED: " + error);
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }
        private static void Simulate(DinosaurAgent original, DinosaurGaitProfile profile, bool repaired, int rate, string scenario, bool record, string outputDirectory = null)
        {
            string output = outputDirectory ?? Output;
            var root = Object.Instantiate(original.gameObject); root.name = original.name;
            Camera camera = null; RenderTexture target = null; Texture2D frame = null;
            GameObject floor = null, lightObject = null;
            Material floorMaterial = null; Texture2D checker = null;
            MediaEncoder encoder = null;
            try
            {
                var agent = root.GetComponent<DinosaurAgent>(); agent.enabled = false;
                Set(agent, "gaitProfile", repaired ? profile : null);
                Set(agent, "gaitMoving", true);
                Set(agent, "<CurrentState>k__BackingField", DinosaurAgent.DinosaurState.RandomWalk);
                var animation = root.GetComponentInChildren<Animation>(true);
                animation.Stop(); animation.cullingType = AnimationCullingType.AlwaysAnimate;
                animation.AddClip(profile.clip, profile.clip.name);
                animation.Play(profile.clip.name);
                var state = animation[profile.clip.name]; state.enabled = true; state.weight = 1f; state.speed = 0f;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var savedBaseline = baseline.animals.Single(a => a.species == original.name);
                var visual = root.GetComponent<DinosaurFbxLoader>().ModelRoot.transform;
                Vector3 visualPosition = visual.localPosition;
                visualPosition.y = repaired
                    ? visualPosition.y + profile.heightCorrection - (original.GaitProfile != null ? original.GaitProfile.heightCorrection : 0f)
                    : savedBaseline.visualY;
                visual.localPosition = visualPosition;
                var feet = profile.feet.Select(f => animation.transform.Find(f.path)).ToArray();
                var hips = profile.feet.Select(f => animation.transform.Find(f.hipPath)).ToArray();
                var knees = profile.feet.Select(f => animation.transform.Find(f.kneePath)).ToArray();
                var legLengths = new Vector2[feet.Length];
                var solver = root.GetComponent<DinosaurFootLock>() ?? root.AddComponent<DinosaurFootLock>();
                solver.enabled = false;
                solver.Bind(root.GetComponent<DinosaurFbxLoader>().ModelRoot.transform, agent,
                    DinosaurGaitCalibration.FootNames(original.name), original.name == "Baurusuchus" ? 2 : 3,
                    original.name == "Pachycephalosaurus", .19f, .001f);
                bool oldLock = savedBaseline.footLock;
                float speed = repaired ? DinosaurGaitCalibration.WalkingSpeed(original.name, profile.groundSpeed) : savedBaseline.walkSpeed;
                float playback = speed / (repaired ? profile.groundSpeed : savedBaseline.groundSpeed);
                float loopStart = repaired ? profile.loopStart : savedBaseline.loopStart;
                float loopEnd = repaired ? profile.loopEnd : savedBaseline.loopEnd;
                if (loopStart < 0f || loopEnd <= loopStart) { loopStart = 0f; loopEnd = profile.clip.length; }
                float duration = scenario == "straight" ? 3f * (loopEnd - loopStart) / playback : 2f * (loopEnd - loopStart) + 2f;
                float dt = 1f / rate, clipTime = loopStart;
                var anchors = new Vector3[feet.Length]; var previous = new Vector3[feet.Length];
                var contact = new bool[feet.Length]; var havePrevious = new bool[feet.Length];
                int landings = 0; float drift = 0f, reach = 0f, jump = 0f;
                if (record)
                {
                    foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
                    floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.layer = 30;
                    floor.transform.position = new Vector3(0f, -.02f, 0f); floor.transform.localScale = Vector3.one * 30f;
                    checker = new Texture2D(2, 2); checker.SetPixels(new[] {
                        new Color(.22f, .25f, .28f), new Color(.30f, .33f, .36f),
                        new Color(.30f, .33f, .36f), new Color(.22f, .25f, .28f) });
                    checker.filterMode = FilterMode.Point; checker.wrapMode = TextureWrapMode.Repeat; checker.Apply();
                    floorMaterial = new Material(Shader.Find("Standard")); floorMaterial.mainTexture = checker;
                    floorMaterial.mainTextureScale = Vector2.one * 300f;
                    floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                    lightObject = new GameObject("Gait preview light"); var light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional; light.intensity = 1.1f; light.cullingMask = 1 << 30;
                    light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                    camera = new GameObject("Gait preview camera").AddComponent<Camera>();
                    camera.enabled = false; camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.18f, .22f, .27f); camera.fieldOfView = 38f;
                    target = new RenderTexture(640, 360, 24); camera.targetTexture = target;
                    frame = new Texture2D(640, 360, TextureFormat.RGBA32, false);
                    encoder = new MediaEncoder(output + "/" + original.name + (repaired ? "-after" : "-before") + ".mp4",
                        new VideoTrackAttributes { width = 640, height = 360, frameRate = new MediaRational(30), includeAlpha = false, bitRateMode = VideoBitrateMode.High });
                }
                for (int tick = 0; tick < Mathf.CeilToInt(duration * rate); tick++)
                {
                    float elapsed = tick * dt;
                    bool stopped = (scenario == "stop-start" || scenario == "blocked") && elapsed > duration * .35f && elapsed < duration * .55f;
                    float fraction = scenario == "short-step" && tick % 12 == 0 ? .12f : 1f;
                    float direction = scenario == "reverse" ? -1f : 1f;
                    float travel = stopped ? 0f : speed * dt * fraction * direction;
                    if (scenario == "turn" || scenario == "turn-in-place")
                        root.transform.rotation *= Quaternion.Euler(0f, profile.turnSpeed * dt, 0f);
                    if (scenario == "turn-in-place") travel = 0f;
                    root.transform.position += root.transform.rotation * Quaternion.Euler(0f, agent.VisualYaw, 0f) * Vector3.forward * travel;
                    if (!stopped) clipTime += dt * playback * fraction * direction;
                    clipTime = loopStart + Mathf.Repeat(clipTime - loopStart, loopEnd - loopStart);
                    state.time = clipTime;
                    animation.Sample();
                    for (int f = 0; f < feet.Length; f++) legLengths[f] = new Vector2(
                        Vector3.Distance(hips[f].position, knees[f].position), Vector3.Distance(knees[f].position, feet[f].position));
                    if (repaired) solver.UpdateGaitFeet(dt);
                    else if (oldLock) typeof(DinosaurFootLock).GetMethod("LateUpdate", Flags).Invoke(solver, null);
                    for (int f = 0; f < feet.Length; f++)
                    {
                        Vector3 sole = feet[f].TransformPoint(profile.feet[f].soleOffset);
                        if (repaired)
                        {
                            DinosaurGaitCalibration.Require(sole.y >= -.02f, $"Sole below ground: {original.name}/{scenario} {sole.y:F4}");
                            var solvedLengths = new Vector2(Vector3.Distance(hips[f].position, knees[f].position),
                                Vector3.Distance(knees[f].position, feet[f].position));
                            DinosaurGaitCalibration.Require(Vector2.Distance(legLengths[f], solvedLengths) < .0001f,
                                "IK stretched a leg segment: " + original.name);
                        }
                        bool planted = profile.Contact(f, clipTime) >= .5f;
                        if (planted && !contact[f]) { anchors[f] = sole; landings++; }
                        if (planted && contact[f])
                            drift = Mathf.Max(drift, Vector2.Distance(new Vector2(sole.x, sole.z), new Vector2(anchors[f].x, anchors[f].z)));
                        if (!planted && contact[f] && havePrevious[f]) jump = Mathf.Max(jump, Vector3.Distance(sole, previous[f]));
                        contact[f] = planted; previous[f] = sole; havePrevious[f] = true;
                    }
                    reach = Mathf.Max(reach, solver.GaitMaximumReachError);
                    if (encoder != null && tick % (rate / 30) == 0)
                    {
                        camera.transform.position = root.transform.position + new Vector3(4.8f, 1.9f, 4.8f);
                        camera.transform.LookAt(root.transform.position + Vector3.up * .8f);
                        camera.Render(); var active = RenderTexture.active; RenderTexture.active = target;
                        frame.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); frame.Apply(); RenderTexture.active = active;
                        encoder.AddFrame(frame);
                        if (tick == rate) File.WriteAllBytes(output + "/" + original.name + (repaired ? "-after" : "-before") + ".png", frame.EncodeToPNG());
                    }
                }
                Report.AppendLine($"|{original.name}|{(repaired ? "after" : "before")}|{rate}|{scenario}|{drift:F5}|{reach:F5}|{jump:F5}|{landings}|");
                File.WriteAllText(output + "/Acceptance.md", Report.ToString());
                Debug.Log($"GAIT_RESULT {original.name} repaired={repaired} fps={rate} {scenario} drift={drift:F5} reach={reach:F5} jump={jump:F5} landings={landings}");
                if (repaired)
                {
                    DinosaurGaitCalibration.Require(landings >= feet.Length * 2, "Insufficient stance coverage: " + original.name);
                    DinosaurGaitCalibration.Require(jump <= .08f, $"Abrupt foot release: {original.name}/{scenario} {jump:F4}m");
                    DinosaurGaitCalibration.Require(drift <= .02f && reach <= .02f,
                        $"Stance error >2cm: {original.name}/{scenario} drift={drift:F4} reach={reach:F4}");
                }
            }
            finally
            {
                encoder?.Dispose();
                if (camera != null) Object.DestroyImmediate(camera.gameObject);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (frame != null) Object.DestroyImmediate(frame);
                if (floor != null) Object.DestroyImmediate(floor);
                if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
                if (checker != null) Object.DestroyImmediate(checker);
                if (lightObject != null) Object.DestroyImmediate(lightObject);
                Object.DestroyImmediate(root);
            }
        }
    }
}
