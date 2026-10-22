using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // Read-only checks against saved assets. Does not rebuild or save Main.
    public static class DinosaurAssetOrganizationAcceptance
    {
        public static void VerifyGenerators()
        {
            // Exercise the real generators twice, then restore the exact saved
            // prepared bytes so this check cannot change the project's visuals.
            var before = Directory.GetFiles("Assets/Dinosaurs", "*", SearchOption.AllDirectories)
                .Where(p => p.Replace('\\', '/').Contains("/Prepared/"))
                .ToDictionary(p => p, File.ReadAllBytes);
            try
            {
                for (int iteration = 0; iteration < 2; iteration++)
                {
                    BaurusuchusSceneIntegration.EnsureAssets();
                    SauropeltaSceneIntegration.EnsureAssets();
                    PachycephalosaurusSceneIntegration.EnsureAssets();
                    StegosaurusSceneIntegration.EnsureAssets();
                    PteradactalSceneIntegration.EnsureAssets();
                    ProtoceratopsSceneIntegration.EnsureAssets();
                    AssetDatabase.SaveAssets();
                }
                var after = new HashSet<string>(Directory.GetFiles("Assets/Dinosaurs", "*", SearchOption.AllDirectories)
                    .Where(p => p.Replace('\\', '/').Contains("/Prepared/")));
                Require(after.SetEquals(before.Keys), "Generators created unexpected prepared files");
            }
            finally
            {
                foreach (var pair in before) File.WriteAllBytes(pair.Key, pair.Value);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            Verify();
            File.AppendAllText("Logs/dinosaur-asset-organization.txt",
                "PASS: all six species adaptation generators ran twice; prepared file set unchanged; original prepared bytes restored.\n");
            Debug.Log("DINOSAUR_ASSET_GENERATORS_PASS");
        }

        public static void VerifyAndBuild()
        {
            Verify();
            PteradactalAcceptance.BuildStandalone();
        }

        public static void Verify()
        {
            string[] legacyFolders = {
                "Assets/Resources/Dinosaurs", "Assets/FerociousIndustries",
                "Assets/Materials/Dinosaurs", "Assets/UI/DinosaurPortraits",
                "Assets/StreamingAssets/Dinosaur"
            };
            foreach (string folder in legacyFolders)
                Require(!Directory.Exists(folder), "Legacy folder recreated: " + folder);

            MethodInfo create = typeof(JurassicWorldSceneBuilder).GetMethod("CreateTemplates",
                BindingFlags.Static | BindingFlags.NonPublic);
            int templates = 0;
            foreach (object template in (IEnumerable)create.Invoke(null, null))
            {
                Type type = template.GetType();
                string name = (string)type.GetField("objectName").GetValue(template);
                string path = (string)type.GetField("assetPath").GetValue(template);
                Require(path.StartsWith("Assets/Dinosaurs/"), name + " uses a noncanonical model path");
                Require(AssetDatabase.LoadAssetAtPath<GameObject>(path) != null, "Missing model: " + path);
                foreach (string field in new[] { "primaryAlbedo", "secondaryAlbedo", "primaryNormal", "secondaryNormal", "occlusion" })
                {
                    string texture = (string)type.GetField(field).GetValue(template);
                    if (!string.IsNullOrEmpty(texture))
                        Require(AssetDatabase.LoadAssetAtPath<Texture2D>(texture) != null, "Missing texture: " + texture);
                }
                templates++;
            }
            Require(templates == 9, "Expected all nine ground species, including disabled ones");
            Require(AssetDatabase.LoadAssetAtPath<GameObject>(PteradactalSceneIntegration.PrefabPath) != null,
                "Missing prepared flying model");

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Dinosaurs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Prepared/")) CheckReferences(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            }

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var agents = Object.FindObjectsOfType<DinosaurAgent>();
            Require(agents.Length == JurassicWorldSceneBuilder.EnabledDinosaurCount, "Main ground species count changed");
            foreach (DinosaurAgent agent in agents)
            {
                var loader = agent.GetComponent<DinosaurFbxLoader>();
                Require(loader != null && loader.ModelRoot != null, agent.name + " missing model binding");
                CheckReferences(loader.ModelRoot);
                var animation = loader.ModelRoot.GetComponentInChildren<Animation>(true);
                Require(animation != null && animation.GetClipCount() > 0, agent.name + " missing Legacy clips");
                string portrait = JurassicWorldSceneBuilder.GetDinosaurPreparedFolder(agent.name) + "/" + agent.name + ".png";
                if (File.Exists(portrait))
                {
                    var serialized = new SerializedObject(agent);
                    Require(serialized.FindProperty("minimapPortrait").objectReferenceValue ==
                        AssetDatabase.LoadAssetAtPath<Sprite>(portrait), agent.name + " portrait reference changed");
                }
            }
            var flyers = Object.FindObjectsOfType<PteradactalFlightController>();
            Require(flyers.Length == 1, "Expected one flyer");
            CheckReferences(flyers[0].gameObject);
            Require(!scene.isDirty, "Verification unexpectedly changed Main");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/dinosaur-asset-organization.txt",
                $"PASS: 10 species; {agents.Length} enabled ground animals and 1 flyer; model, texture, prefab, animation, material and portrait references resolved; Main not saved.\n");
            Debug.Log("DINOSAUR_ASSET_ORGANIZATION_PASS");
        }

        private static void CheckReferences(GameObject root)
        {
            Require(root != null, "Missing prefab root");
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                Require(component != null, root.name + " has a missing script");
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference)
                        Require(property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0,
                            root.name + ": unresolved reference " + property.propertyPath);
            }
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Require(renderer.sharedMesh != null && renderer.bones.All(b => b != null), root.name + " missing mesh/bones");
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                Require(renderer.sharedMaterials.All(m => m != null && m.shader != null &&
                    m.shader.name != "Hidden/InternalErrorShader"), root.name + " missing material/shader");
            foreach (Animation animation in root.GetComponentsInChildren<Animation>(true))
                foreach (AnimationState state in animation)
                    Require(state.clip != null, root.name + " missing animation clip");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
