using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Read-only checks against the reorganized environment asset tree. Does not rebuild or save Main.
    public static class EnvironmentAssetOrganizationAcceptance
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string EnvironmentRoot = "Assets/Environment";
        private const string TreesRoot = EnvironmentRoot + "/Vegetation/Trees";
        private const string TerrainPrepared = EnvironmentRoot + "/Terrain/Prepared";
        private const string MainTerrainPath = TerrainPrepared + "/MainInteractionTerrain.asset";
        private const string BaseTerrainPath = TerrainPrepared + "/JurassicTerrainData.asset";
        private const string LogPath = "Logs/environment-asset-organization.txt";

        private static readonly string[] LegacyRoots =
        {
            "Assets/Vegetation", "Assets/VegetationDiversity", "Assets/VegetationInteraction",
            "Assets/ThirdParty/PolyHaven", "Assets/ThirdParty/VegetationDiversity",
            "Assets/NatureStarterKit2", "Assets/Terrain", "Assets/CreekDemo",
            "Assets/_TerrainAutoUpgrade", "Assets/Meshes", "Assets/Textures",
            "Assets/Materials/PolyHaven"
        };

        private static readonly string[] LegacyFiles =
        {
            "Assets/Navigation/MainInteractionTerrain.asset",
            "Assets/Navigation/MainCreekTerrain.asset"
        };

        public static void Verify()
        {
            try
            {
                RunChecks();
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, "FAIL: " + exception + Environment.NewLine);
                throw;
            }
        }

        private static void RunChecks()
        {
            foreach (string folder in LegacyRoots)
                Require(!Directory.Exists(folder), "Legacy folder recreated: " + folder);
            foreach (string file in LegacyFiles)
                Require(!File.Exists(file), "Legacy file recreated: " + file);

            Require(File.Exists(EnvironmentRoot + "/README.md"), "Missing environment README");
            Require(File.Exists(TreesRoot + "/README.md"), "Missing tree README");
            Require(Directory.Exists("Assets/ThirdParty/NatureStarterKit2/Nature"), "Nature Starter Kit 2 was not moved");
            Require(File.Exists("Assets/ThirdParty/NatureStarterKit2/Nature/tree01.prefab"), "NSK2 tree prefab missing");

            int trees = CountAssets(TreesRoot + "/Prepared/Prefabs", "*.prefab");
            int ferns = CountAssets(EnvironmentRoot + "/Vegetation/Fern02/Prepared/Prefabs", "*.prefab");
            int rocks = CountAssets(EnvironmentRoot + "/RocksAndDeadwood/RockMossSet01/Prepared/Prefabs", "*.prefab");
            int deadwood = CountAssets(EnvironmentRoot + "/RocksAndDeadwood/Deadwood/Prepared/Prefabs", "*.prefab");
            int autumn = CountAssets(EnvironmentRoot + "/RocksAndDeadwood/AutumnGroundedLog/Prepared/Prefabs", "*.prefab");
            int interaction = CountAssets(EnvironmentRoot + "/Vegetation/Interaction/Prepared", "*");
            int layers = CountAssets(TerrainPrepared + "/Layers", "*.terrainlayer");
            int creek = CountAssets(EnvironmentRoot + "/Creek/Prepared", "*");

            Require(trees == 11, "Expected 11 tree prefabs, found " + trees);
            Require(ferns == 4, "Expected 4 fern prefabs, found " + ferns);
            Require(rocks == 6, "Expected 6 rock prefabs, found " + rocks);
            Require(deadwood == 2, "Expected 2 deadwood prefabs, found " + deadwood);
            Require(autumn == 1, "Expected 1 autumn log prefab, found " + autumn);
            Require(interaction == 70, "Expected 70 interaction assets, found " + interaction);
            Require(layers == 4, "Expected 4 terrain layers, found " + layers);
            Require(creek == 4, "Expected 4 creek assets, found " + creek);

            Require(File.Exists(BaseTerrainPath), "Missing base terrain: " + BaseTerrainPath);
            Require(File.Exists(MainTerrainPath), "Missing main terrain: " + MainTerrainPath);
            Require(Directory.GetFiles(TerrainPrepared, "*.asset", SearchOption.TopDirectoryOnly).Length == 2,
                "Expected exactly two terrain assets in " + TerrainPrepared);
            foreach (string name in new[]
                     {
                         "CrestCreekWater.mat", "CreekRippleSpectrum.asset",
                         "JungleCreekBank.terrainlayer", "MainCreekTerrain.asset"
                     })
                Require(File.Exists(EnvironmentRoot + "/Creek/Prepared/" + name), "Missing creek asset: " + name);

            string natural = TerrainPrepared + "/GroundEcology/Natural";
            foreach (string group in new[] { "Moss", "DarkGrass", "Leaves", "Roots", "AutumnMoss" })
            {
                Require(File.Exists(natural + "/" + group + ".asset"), "Missing natural mesh: " + group);
                Require(File.Exists(natural + "/" + group + ".mat"), "Missing natural material: " + group);
            }
            Require(File.Exists(natural + "/ForestFloorAtlas.png"), "Missing natural atlas");

            string surface = TerrainPrepared + "/GroundEcology/Surface";
            foreach (string group in new[] { "Moss", "DarkGrass", "Leaves", "Roots" })
            {
                Require(File.Exists(surface + "/" + group + ".asset"), "Missing surface mesh: " + group);
                Require(File.Exists(surface + "/" + group + ".mat"), "Missing surface material: " + group);
            }
            Require(File.Exists(surface + "/RouteGrassTuft.asset"), "Missing route grass tuft mesh");
            Require(File.Exists(surface + "/RouteGrassTuft.mat"), "Missing route grass tuft material");

            CheckTerrain(BaseTerrainPath);
            CheckTerrain(MainTerrainPath);
            CheckMainScene();

            CheckSourcePaths();

            string summary = "PASS: old roots absent; trees=" + trees + ", ferns=" + ferns + ", rocks=" + rocks +
                             ", deadwood=" + deadwood + ", autumn=" + autumn + ", interaction=" + interaction +
                             ", terrainData=2, layers=" + layers + ", creek=" + creek +
                             "; Main references MainInteractionTerrain with resolved prototypes; " +
                             "no legacy paths in Editor/Scripts code or Editor JSON; Main not saved.";
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, summary + Environment.NewLine);
            Debug.Log("ENVIRONMENT_ASSET_ORGANIZATION_PASS");
        }

        private static void CheckTerrain(string path)
        {
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            Require(data != null, "Missing TerrainData: " + path);
            Require(data.terrainLayers != null && data.terrainLayers.Length > 0, path + " has no terrain layers");
            Require(data.terrainLayers.All(layer => layer != null), path + " has a null terrain layer");
            Require(data.treePrototypes.Length > 0 || data.detailPrototypes.Length > 0,
                path + " has no vegetation prototypes");

            foreach (TreePrototype prototype in data.treePrototypes)
            {
                Require(prototype != null && prototype.prefab != null, path + " has an empty tree prototype");
                string prefabPath = AssetDatabase.GetAssetPath(prototype.prefab);
                Require(IsEnvironmentAsset(prefabPath),
                    path + " tree prototype outside Environment/NSK2: " + prefabPath);
            }

            foreach (DetailPrototype prototype in data.detailPrototypes)
            {
                Require(prototype != null, path + " has a null detail prototype");
                if (prototype.usePrototypeMesh)
                {
                    Require(prototype.prototype != null, path + " has a mesh detail prototype without a prefab");
                    string prefabPath = AssetDatabase.GetAssetPath(prototype.prototype);
                    Require(IsEnvironmentAsset(prefabPath),
                        path + " detail prototype outside Environment/NSK2: " + prefabPath);
                }
                else
                {
                    Require(prototype.prototypeTexture != null, path + " has a texture detail prototype without a texture");
                }
            }
        }

        private static bool IsEnvironmentAsset(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   (path.StartsWith(EnvironmentRoot + "/", StringComparison.Ordinal) ||
                    path.StartsWith("Assets/ThirdParty/NatureStarterKit2/", StringComparison.Ordinal));
        }

        private static void CheckMainScene()
        {
            Require(File.Exists(MainScenePath), "Missing Main scene");
            var scene = EditorSceneManager.OpenScene(MainScenePath);

            Terrain terrain = Terrain.activeTerrain;
            Require(terrain != null, "Main has no active terrain");
            Require(AssetDatabase.GetAssetPath(terrain.terrainData) == MainTerrainPath,
                "Main terrain does not use " + MainTerrainPath);

            int missingScripts = 0;
            int missingPrefabs = 0;
            int missingMeshes = 0;
            int missingMaterials = 0;
            var examples = new List<string>();

            foreach (Transform transform in Object.FindObjectsOfType<Transform>(true))
            {
                GameObject go = transform.gameObject;
                if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.MissingAsset)
                {
                    missingPrefabs++;
                    AddExample(examples, "missing prefab: " + go.name);
                }

                foreach (Component component in go.GetComponents<Component>())
                {
                    if (component != null) continue;
                    missingScripts++;
                    AddExample(examples, "missing script: " + go.name);
                }

                var filter = go.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh == null && go.GetComponent<Renderer>() != null)
                {
                    missingMeshes++;
                    AddExample(examples, "missing mesh: " + go.name);
                }

                foreach (Renderer renderer in go.GetComponents<Renderer>())
                {
                    Material[] materials = renderer.sharedMaterials;
                    if (materials != null && materials.All(material => material != null &&
                            material.shader != null && material.shader.name != "Hidden/InternalErrorShader"))
                        continue;
                    missingMaterials++;
                    AddExample(examples, "missing material: " + go.name);
                }
            }

            Require(missingScripts == 0, "Main has " + missingScripts + " missing scripts; " + Join(examples));
            Require(missingPrefabs == 0, "Main has " + missingPrefabs + " missing prefabs; " + Join(examples));
            Require(missingMeshes == 0, "Main has " + missingMeshes + " empty meshes; " + Join(examples));
            Require(missingMaterials == 0, "Main has " + missingMaterials + " empty materials; " + Join(examples));
            Require(!scene.isDirty, "Verification unexpectedly changed Main");
        }

        private static void CheckSourcePaths()
        {
            IEnumerable<string> files = Directory.GetFiles("Assets/Editor", "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("Assets/Editor", "*.json", SearchOption.TopDirectoryOnly))
                .Where(path => !path.Replace('\\', '/').EndsWith(
                    "/EnvironmentAssetOrganizationAcceptance.cs", StringComparison.Ordinal));

            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                foreach (string legacy in LegacyRoots)
                    Require(!text.Contains(legacy), "Legacy path '" + legacy + "' remains in " + file);
                foreach (string legacy in LegacyFiles)
                    Require(!text.Contains(legacy), "Legacy path '" + legacy + "' remains in " + file);
            }
        }

        private static int CountAssets(string folder, string pattern)
        {
            Require(Directory.Exists(folder), "Missing folder: " + folder);
            return Directory.GetFiles(folder, pattern, SearchOption.AllDirectories)
                .Count(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
        }

        private static void AddExample(List<string> examples, string message)
        {
            if (examples.Count < 5) examples.Add(message);
        }

        private static string Join(List<string> examples)
        {
            return examples.Count == 0 ? "no examples" : string.Join("; ", examples);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
