using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace JurassicWorld.Editor
{
    [InitializeOnLoad]
    public static class TRexFrontTestScene
    {
        private const string Folder = "Assets/Scenes/TRexFrontTestAssets";
        private static string lastStatus;
        static TRexFrontTestScene() => EditorApplication.update += PollRequest;
        private static void PollRequest()
        {
            const string path = "Logs/trex-front-test-request.txt";
            if (!File.Exists(path)) return;
            string status = EditorApplication.isPlayingOrWillChangePlaymode ? "WAITING_FOR_PLAY_TO_STOP"
                : EditorApplication.isCompiling || EditorApplication.isUpdating ? "WAITING_FOR_IMPORT"
                : SceneManager.GetActiveScene().isDirty ? "WAITING_FOR_SCENE_SAVE" : "STARTING";
            if (status != lastStatus) { File.WriteAllText("Logs/trex-front-test-status.txt", status); lastStatus = status; }
            if (status != "STARTING") return;
            string request = File.ReadAllText(path).Trim(); File.Delete(path);
            try
            {
                if (request == "blocked-test") TRexBlockedInteractionSmoke.Run();
                else if (request == "interaction-test") TRexFrontTestSmoke.Run();
                else if (request == "side-test") TRexSideTestSmoke.Run();
                else if (request == "reservation-test") TRexReservationSmoke.Run();
                else if (request == "open") Open();
                else { Create(); if (request == "test") TRexFrontTestSmoke.Run(); }
            }
            catch (Exception error) { Debug.LogException(error); File.WriteAllText("Logs/trex-front-test-status.txt", error.ToString()); }
        }
        [MenuItem("Jurassic World/Tests/Create Front Interaction Scene")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before creating the test scene.");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before creating the test scene.");
            var main = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var source = UnityEngine.Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var originalSettings = UnityEngine.Object.FindObjectOfType<DinosaurInteractionSettings>();
            float navigationRadius = originalSettings.navigationRadius;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var clone = UnityEngine.Object.Instantiate(source.gameObject);
            SceneManager.MoveGameObjectToScene(clone, scene);
            clone.name = "Tyrannosaurus Rex";
            EditorSceneManager.CloseScene(main, true);
            SceneManager.SetActiveScene(scene);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Scenes", "TRexFrontTestAssets");

            var rex = clone.GetComponent<DinosaurAgent>();
            clone.transform.SetPositionAndRotation(new Vector3(0f, rex.MovementHeight, 0f), Quaternion.Euler(0f, -rex.VisualYaw, 0f));
            var cameraObject = new GameObject("Player"); cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.46f, .58f, .68f);
            camera.nearClipPlane = .05f; camera.farClipPlane = 250f; camera.fieldOfView = 65f;
            cameraObject.AddComponent<AudioListener>();
            var controller = cameraObject.AddComponent<SmoothExplorerCamera>();
            var cameraSerialized = new SerializedObject(controller);
            cameraSerialized.FindProperty("moveSpeed").floatValue = 3f;
            cameraSerialized.FindProperty("initialPitch").floatValue = 4f;
            cameraSerialized.ApplyModifiedPropertiesWithoutUndo();
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 1.7f, 40f), Quaternion.Euler(4f, 180f, 0f));
            var rexSerialized = new SerializedObject(rex);
            rexSerialized.FindProperty("interactionProfile").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<DinosaurInteractionProfile>(TRexInteractionBake.SideTestProfilePath) ?? TRexInteractionBake.BuildSideTestProfile(rex);
            rexSerialized.FindProperty("viewCamera").objectReferenceValue = camera;
            rexSerialized.FindProperty("roamingMode").enumValueIndex = (int)DinosaurAgent.RoamingMode.Free;
            rexSerialized.ApplyModifiedPropertiesWithoutUndo();

            var terrainData = new TerrainData { heightmapResolution = 129, size = new Vector3(400f, 10f, 400f) };
            terrainData = SaveAsset(terrainData, Folder + "/FlatTerrain.asset");
            var ground = Terrain.CreateTerrainGameObject(terrainData); ground.name = "Flat Test Ground";
            ground.transform.position = new Vector3(-200f, 0f, -200f);
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.1f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.6f, .65f, .7f); RenderSettings.fog = false; RenderSettings.skybox = null;
            var yellow = SaveAsset(new Material(Shader.Find("Standard")) { color = new Color(1f, .78f, .04f) }, Folder + "/TriggerYellow.mat");
            var layout = new GameObject("Approach Layout").transform;
            Mark("Trigger Zone 30-32m", new Vector3(0f, .015f, 31f), new Vector3(4f, .03f, 2f), yellow, layout);
            for (int z = 34; z <= 38; z += 2)
                Mark("Approach marker " + z, new Vector3(0f, .015f, z), new Vector3(.18f, .025f, 1f), yellow, layout);

            var settings = new GameObject("Dinosaur Interaction Settings").AddComponent<DinosaurInteractionSettings>();
            settings.allowOffRouteInteraction = true; settings.navigationRadius = navigationRadius;
            var build = NavMesh.GetSettingsByIndex(0); build.agentRadius = navigationRadius; build.agentHeight = 6f;
            build.agentSlope = 10f; build.agentClimb = .1f; build.overrideVoxelSize = true; build.voxelSize = .25f;
            var data = NavMeshBuilder.BuildNavMeshData(build, new System.Collections.Generic.List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Terrain, sourceObject = terrainData,
                    transform = ground.transform.localToWorldMatrix, area = 0 }
            }, new Bounds(new Vector3(0f, 2f, 10f), new Vector3(300f, 12f, 300f)), Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("Test navigation bake failed.");
            data.name = "TRex Front Test Navigation";
            settings.navigationData = SaveAsset(data, Folder + "/TestNavMesh.asset"); settings.navigationAgentTypeId = build.agentTypeID;
            var test = new GameObject("Front Test Controls").AddComponent<TRexFrontTestController>();
            test.dinosaur = rex; test.playerCamera = camera;
            test.approachLayout = layout;
            EditorSceneManager.SaveScene(scene, TRexFrontTestController.ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("TREX_FRONT_TEST_SCENE_CREATED: fixed rex (0,0,0), player (0,1.7,40), one-shot zone x +/-2 z30..32; independent navigation.");
        }
        private static T SaveAsset<T>(T asset, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(asset, path); return asset; }
            EditorUtility.CopySerialized(asset, existing); UnityEngine.Object.DestroyImmediate(asset); EditorUtility.SetDirty(existing);
            return existing;
        }
        private static void Mark(string name, Vector3 position, Vector3 size, Material material, Transform parent)
        {
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube); mark.name = name;
            mark.transform.SetParent(parent, false);
            mark.transform.position = position; mark.transform.localScale = size;
            UnityEngine.Object.DestroyImmediate(mark.GetComponent<Collider>());
            mark.GetComponent<Renderer>().sharedMaterial = material;
        }
        public static void Open() => EditorSceneManager.OpenScene(TRexFrontTestController.ScenePath);
    }
}
