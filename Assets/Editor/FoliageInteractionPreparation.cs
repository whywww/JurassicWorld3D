using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class FoliageInteractionPreparation
    {
        internal const string RootName = "Dinosaur Foliage Interaction";
        internal const string EnabledPath = "Assets/Editor/FoliageInteractionEnabled.json";
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string AssetsRoot = "Assets/VegetationInteraction";
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<Material, Material> Materials = new Dictionary<Material, Material>();
        private static readonly Dictionary<Mesh, Bounds> FlexibleBounds = new Dictionary<Mesh, Bounds>();

        [MenuItem("Jurassic World/Environment/Foliage Interaction/Prepare and Enable")]
        private static void PrepareMenu() => JurassicMenuGuard.Run(PrepareMain, true);
        [MenuItem("Jurassic World/Environment/Foliage Interaction/Restore Original Trees")]
        private static void RestoreMenu() => JurassicMenuGuard.Run(RestoreMain, true);
        [MenuItem("Jurassic World/Environment/Foliage Interaction/Prepare and Enable", true)]
        [MenuItem("Jurassic World/Environment/Foliage Interaction/Restore Original Trees", true)]
        private static bool MenuEnabled() => JurassicMenuGuard.CanRun;

        public static void PrepareMain()
        {
            EditorSceneManager.OpenScene(ScenePath);
            PrepareCurrent();
            File.WriteAllText(EnabledPath, "{\"enabled\":true}\n");
            Save();
        }
        public static void RestoreMain()
        {
            EditorSceneManager.OpenScene(ScenePath);
            RestoreCurrent();
            if (File.Exists(EnabledPath)) File.Delete(EnabledPath);
            Save();
        }
        public static void RebuildMain()
        {
            EditorSceneManager.OpenScene(ScenePath);
            RestoreCurrent();
            PrepareCurrent();
            Save();
        }
        public static void ValidateMain()
        {
            EditorSceneManager.OpenScene(ScenePath);
            ValidateCurrent();
            Debug.Log("FOLIAGE_SCENE_VALID: tree ledger, mesh streams and material references are intact.");
        }
        internal static void SeedConfigured() { if (File.Exists(EnabledPath)) PrepareCurrent(); }
        private static void Save()
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        public static void PrepareCurrent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Prepare foliage in edit mode.");
            var existing = Object.FindObjectOfType<FoliageTerrainConversion>();
            if (existing != null) { ValidateCurrent(); return; }
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) throw new InvalidOperationException("Foliage preparation requires Terrain.");
            Directory.CreateDirectory(AssetsRoot + "/Meshes"); Directory.CreateDirectory(AssetsRoot + "/Materials");
            AssetDatabase.Refresh(); Meshes.Clear(); Materials.Clear(); FlexibleBounds.Clear();
            var root = new GameObject(RootName);
            root.AddComponent<FoliageInteractionSystem>();
            FoliageTerrainConversion ledger = root.AddComponent<FoliageTerrainConversion>();
            ledger.terrain = terrain;
            TreeInstance[] source = terrain.terrainData.treeInstances;
            ledger.originalTreeCount = source.Length;
            var removed = new List<FoliageTerrainConversion.RemovedTree>();
            var retained = new List<TreeInstance>();
            var addedSources = new List<DinosaurFoliageInteractor>();
            bool terrainCommitted = false;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (DinosaurAgent agent in Object.FindObjectsOfType<DinosaurAgent>())
                {
                    if (!new SerializedObject(agent).FindProperty("groundObstacleAware").boolValue) continue;
                    DinosaurFoliageInteractor interactor = BakeInteractor(agent);
                    addedSources.Add(interactor);
                }
                if (addedSources.Count == 0) throw new InvalidOperationException("No ground dinosaur contact profile.");
                TreePrototype[] prototypes = terrain.terrainData.treePrototypes;
                var profiles = Object.FindObjectOfType<ForestTrunkProfiles>();
                for (int i = 0; i < source.Length; i++)
                {
                    TreeInstance tree = source[i];
                    if (tree.widthScale <= 0f || tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length)
                    { retained.Add(tree); continue; }
                    GameObject prefab = prototypes[tree.prototypeIndex].prefab;
                    Vector3 position = terrain.transform.position + Vector3.Scale(tree.position, terrain.terrainData.size);
                    Quaternion rotation = Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f);
                    Vector3 scale = Vector3.Scale(prefab.transform.localScale, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                    Bounds bounds = PrefabBounds(prefab, position, rotation, scale);
                    if (!CanReach(bounds, addedSources)) { retained.Add(tree); continue; }
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    instance.name = prefab.name + "_ContactTree_" + i;
                    instance.transform.SetPositionAndRotation(position, rotation); instance.transform.localScale = scale;
                    // Behaviour obstacle is copied exactly from TerrainTreeObstacleRegistry, without rebaking geometry.
                    foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                    SceneTreeObstacle obstacle = instance.GetComponent<SceneTreeObstacle>() ?? instance.AddComponent<SceneTreeObstacle>();
                    obstacle.SetRadius(.7f * tree.widthScale);
                    obstacle.trunkSections = CopySections(profiles, tree, position);
                    var foliage = PrepareTree(instance, obstacle);
                    foliage.convertedTerrainTree = true; foliage.instanceColor = tree.color; foliage.instanceLightmapColor = tree.lightmapColor;
                    ApplyTreeTint(foliage);
                    removed.Add(new FoliageTerrainConversion.RemovedTree { originalIndex = i, instance = tree, prototype = prefab });
                }
                // Authored tree markers exclude ground detail, bushes, logs, and rocks by construction.
                foreach (SceneTreeObstacle tree in Object.FindObjectsOfType<SceneTreeObstacle>())
                {
                    if (tree.transform.IsChildOf(root.transform)) continue;
                    Bounds bounds = FlexibleTreeBounds(tree.gameObject);
                    if (CanReach(bounds, addedSources)) PrepareTree(tree.gameObject, tree);
                }
                ledger.removed = removed.ToArray();
                ledger.retainedSignature = TreeSignature(retained.ToArray(), prototypes);
                // Commit the Terrain array only after all profiles, material variants and meshes succeeded.
                if (removed.Count > 0)
                {
                    terrain.terrainData.SetTreeInstances(retained.ToArray(), false);
                    terrainCommitted = true;
                    EditorUtility.SetDirty(terrain.terrainData);
                }
                TerrainTreeObstacleRegistry.Invalidate();
                ValidateCurrent();
                Debug.Log($"FOLIAGE_PREPARED: {addedSources.Count} dinosaurs, {removed.Count} converted Terrain trees, " +
                    $"{Object.FindObjectsOfType<InteractiveFoliage>().Length} interactive trees; layout and trunk sections preserved.");
            }
            catch
            {
                if (terrainCommitted) terrain.terrainData.SetTreeInstances(source, false);
                foreach (InteractiveFoliage foliage in Object.FindObjectsOfType<InteractiveFoliage>())
                    if (!foliage.transform.IsChildOf(root.transform)) RestoreRenderer(foliage);
                foreach (var interactor in addedSources) if (interactor != null) Object.DestroyImmediate(interactor);
                Object.DestroyImmediate(root); TerrainTreeObstacleRegistry.Invalidate(); throw;
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }

        public static void RestoreCurrent()
        {
            var ledger = Object.FindObjectOfType<FoliageTerrainConversion>();
            if (ledger == null) return;
            TerrainData data = ledger.terrain.terrainData;
            TreeInstance[] retained = data.treeInstances;
            if (TreeSignature(retained, data.treePrototypes) != ledger.retainedSignature)
                throw new InvalidOperationException("Terrain trees/prototypes changed after conversion. Restore before editing the vegetation layout; no trees were overwritten.");
            var restored = new TreeInstance[ledger.originalTreeCount];
            var removed = ledger.removed.ToDictionary(entry => entry.originalIndex);
            int next = 0;
            for (int i = 0; i < restored.Length; i++) restored[i] = removed.TryGetValue(i, out var entry) ? entry.instance : retained[next++];
            bool hasConvertedTrees = ledger.removed.Length > 0;
            foreach (InteractiveFoliage tree in Object.FindObjectsOfType<InteractiveFoliage>())
                if (!tree.convertedTerrainTree) RestoreRenderer(tree);
            foreach (var interactor in Object.FindObjectsOfType<DinosaurFoliageInteractor>()) Object.DestroyImmediate(interactor);
            Object.DestroyImmediate(ledger.gameObject);
            if (hasConvertedTrees) { data.SetTreeInstances(restored, false); EditorUtility.SetDirty(data); }
            TerrainTreeObstacleRegistry.Invalidate();
            Debug.Log("FOLIAGE_RESTORED: original Terrain tree order, transforms, colours and authored renderers restored.");
        }

        private static void RestoreRenderer(InteractiveFoliage tree)
        {
            foreach (var original in tree.originals)
            {
                if (original.renderer == null) continue;
                original.renderer.sharedMaterials = original.materials;
                MeshFilter filter = original.renderer.GetComponent<MeshFilter>();
                if (filter != null) filter.sharedMesh = original.mesh;
                ((MeshRenderer)original.renderer).additionalVertexStreams = original.additionalStreams;
                GameObjectUtility.SetStaticEditorFlags(original.renderer.gameObject, (StaticEditorFlags)original.staticFlags);
                original.renderer.SetPropertyBlock(null);
            }
            Object.DestroyImmediate(tree);
        }

        private static bool CanReach(Bounds crown, List<DinosaurFoliageInteractor> sources)
        {
            foreach (var source in sources)
            {
                Bounds body = source.animationEnvelope;
                float padding = FoliageInteractionSystem.MaximumDisplacement;
                float bottom = source.transform.position.y + body.min.y, top = source.transform.position.y + body.max.y;
                if (crown.max.y < bottom - padding || crown.min.y > top + padding) continue;
                float radius = new Vector2(body.center.x, body.center.z).magnitude + new Vector2(body.extents.x, body.extents.z).magnitude;
                float crownRadius = new Vector2(crown.extents.x, crown.extents.z).magnitude;
                for (int route = 0; route < MovementCorridor.RouteCount; route++)
                    if (MovementCorridor.MinDistanceToRoute(new Vector2(crown.center.x, crown.center.z), route) <= radius + crownRadius + padding + .5f) return true;
            }
            return false;
        }

        private static TreeTrunkSection[] CopySections(ForestTrunkProfiles profiles, TreeInstance tree, Vector3 world)
        {
            if (profiles == null || tree.prototypeIndex >= profiles.prototypes.Length) return Array.Empty<TreeTrunkSection>();
            TreeTrunkSection[] source = profiles.prototypes[tree.prototypeIndex].sections;
            var result = new TreeTrunkSection[source.Length];
            float cos = Mathf.Cos(tree.rotation), sin = Mathf.Sin(tree.rotation);
            for (int i = 0; i < source.Length; i++)
            {
                var section = source[i]; Vector2 p = section.center * tree.widthScale;
                section.center = new Vector2(p.x * cos + p.y * sin, -p.x * sin + p.y * cos);
                section.bottom = world.y + section.bottom * tree.heightScale;
                section.top = world.y + section.top * tree.heightScale;
                section.radius *= tree.widthScale; result[i] = section;
            }
            return result;
        }

        private static Bounds PrefabBounds(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, scale) * prefab.transform.worldToLocalMatrix;
            Bounds result = new Bounds(); bool first = true;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                {
                    Mesh mesh = PrepareMesh(filter.sharedMesh, filter.GetComponent<Renderer>().sharedMaterials,
                        prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                    IncludeBounds(ref result, ref first, FlexibleBounds[mesh], matrix * filter.transform.localToWorldMatrix);
                }
            return result;
        }
        private static Bounds FlexibleTreeBounds(GameObject tree)
        {
            Bounds result = new Bounds(); bool first = true;
            foreach (MeshFilter filter in tree.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                {
                    Mesh mesh = PrepareMesh(filter.sharedMesh, filter.GetComponent<Renderer>().sharedMaterials,
                        tree.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                    IncludeBounds(ref result, ref first, FlexibleBounds[mesh], filter.transform.localToWorldMatrix);
                }
            return result;
        }
        private static void IncludeBounds(ref Bounds bounds, ref bool first, Bounds source, Matrix4x4 matrix)
        {
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(new Vector3(x == 0 ? source.min.x : source.max.x,
                    y == 0 ? source.min.y : source.max.y, z == 0 ? source.min.z : source.max.z));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
            }
        }

        private static InteractiveFoliage PrepareTree(GameObject tree, SceneTreeObstacle obstacle)
        {
            var foliage = tree.GetComponent<InteractiveFoliage>();
            if (foliage != null) return foliage;
            foliage = tree.AddComponent<InteractiveFoliage>();
            var originals = new List<InteractiveFoliage.OriginalRenderer>();
            Bounds bounds = new Bounds(); bool first = true;
            foreach (MeshFilter filter in tree.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || filter.sharedMesh == null) continue;
                Matrix4x4 meshToRoot = tree.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                originals.Add(new InteractiveFoliage.OriginalRenderer
                {
                    renderer = renderer, mesh = filter.sharedMesh, materials = renderer.sharedMaterials,
                    additionalStreams = ((MeshRenderer)renderer).additionalVertexStreams,
                    staticFlags = (int)GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)
                });
                Material[] sourceMaterials = renderer.sharedMaterials;
                Mesh stream = PrepareMesh(filter.sharedMesh, sourceMaterials, meshToRoot);
                ((MeshRenderer)renderer).additionalVertexStreams = stream;
                IncludeBounds(ref bounds, ref first, FlexibleBounds[stream], meshToRoot);
                renderer.sharedMaterials = sourceMaterials.Select(PrepareMaterial).ToArray();
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                    GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~StaticEditorFlags.BatchingStatic);
            }
            if (first) throw new InvalidOperationException("Tree has no mesh renderers: " + tree.name);
            foliage.localBounds = bounds;
            foliage.originals = originals.ToArray(); foliage.renderers = originals.Select(o => o.renderer).ToArray();
            EditorUtility.SetDirty(foliage);
            return foliage;
        }

        private static bool IsLeaf(Material material)
        {
            string text = (material.name + " " + material.shader.name).ToLowerInvariant();
            return text.Contains("leaf") || text.Contains("leaves") || text.Contains("foliage") || text.Contains("cutout")
                || text.Contains("transparent") || material.IsKeywordEnabled("_ALPHATEST_ON");
        }

        private static Mesh PrepareMesh(Mesh source, Material[] materials, Matrix4x4 meshToRoot)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long fileId);
            // Share native flexibility across instances. Per-tree scaling belongs to the displacement field.
            var keyText = new StringBuilder(guid + ":" + fileId);
            for (int i = 0; i < 16; i++) keyText.Append(':').Append(meshToRoot[i].ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            foreach (Material material in materials)
            {
                if (material == null) { keyText.Append(":null"); continue; }
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string materialGuid, out long materialId);
                keyText.Append(':').Append(materialGuid).Append(':').Append(materialId);
            }
            string key = Hash(Encoding.UTF8.GetBytes(keyText.ToString()));
            if (Meshes.TryGetValue(key, out var cached)) return cached;
            string path = AssetsRoot + "/Meshes/" + source.name.Replace('/', '_') + "_" + key + ".asset";
            Mesh result = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = result == null;
            if (created) result = new Mesh(); else result.Clear(false);
            result.name = source.name + " Flexibility"; result.indexFormat = source.indexFormat;
            Vector3[] vertices = source.vertices;
            var mask = new List<Vector2>(Enumerable.Repeat(Vector2.zero, vertices.Length));
            var assigned = new bool[vertices.Length];
            var stemPoints = new Dictionary<int, List<Vector2>>();
            float baseRadius = .1f;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                if (sub < materials.Length && materials[sub] != null && IsLeaf(materials[sub])) continue;
                foreach (int index in source.GetIndices(sub).Distinct())
                {
                    Vector3 p = meshToRoot.MultiplyPoint3x4(vertices[index]);
                    int layer = Mathf.FloorToInt(p.y / .5f);
                    if (!stemPoints.TryGetValue(layer, out var points)) stemPoints.Add(layer, points = new List<Vector2>());
                    points.Add(new Vector2(p.x, p.z));
                }
            }
            // Follow the dense, central woody stem in native coordinates. Lateral branches keep their flexibility.
            if (stemPoints.Count > 0)
            {
                var lowest = stemPoints[stemPoints.Keys.Min()];
                float nearest = lowest.Min(p => p.magnitude);
                baseRadius = Mathf.Clamp(nearest + .05f, .04f, .6f);
            }
            var stem = new Dictionary<int, Vector3>();
            foreach (var layer in stemPoints)
            {
                float nearest = layer.Value.Min(p => p.magnitude);
                if (nearest > Mathf.Max(.8f, baseRadius * 4f)) continue;
                Vector2[] central = layer.Value.Where(p => p.magnitude <= nearest + baseRadius).ToArray();
                Vector2 centre = Vector2.zero;
                foreach (Vector2 p in central) centre += p;
                centre /= central.Length;
                float radius = Mathf.Max(baseRadius, central.Max(p => Vector2.Distance(p, centre)));
                stem.Add(layer.Key, new Vector3(centre.x, centre.y, radius));
            }
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                bool leaf = sub < materials.Length && materials[sub] != null && IsLeaf(materials[sub]);
                foreach (int index in source.GetIndices(sub))
                {
                    Vector3 p = meshToRoot.MultiplyPoint3x4(vertices[index]);
                    float stemDistance = new Vector2(p.x, p.z).magnitude;
                    float stemRadius = baseRadius;
                    int heightLayer = Mathf.FloorToInt(p.y / .5f);
                    if (stem.TryGetValue(heightLayer, out var section))
                    {
                        stemDistance = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(section.x, section.y));
                        stemRadius = section.z;
                    }
                    float radial = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((stemDistance - stemRadius) / Mathf.Max(.15f, stemRadius)));
                    float height = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p.y / .35f));
                    float flexibility = radial * height;
                    Vector2 value = new Vector2(flexibility, leaf ? 0f : 1f);
                    // Shared stem/leaf vertices use the rigidder attachment, not a detached leaf edge.
                    if (!assigned[index]) { mask[index] = value; assigned[index] = true; }
                    else mask[index] = new Vector2(Mathf.Min(mask[index].x, value.x), Mathf.Max(mask[index].y, value.y));
                }
            }
            result.vertices = vertices; result.SetUVs(3, mask);
            Bounds flexibleBounds = new Bounds(); bool flexibleFirst = true;
            for (int i = 0; i < vertices.Length; i++)
                if (mask[i].x > .05f)
                {
                    if (flexibleFirst) { flexibleBounds = new Bounds(vertices[i], Vector3.zero); flexibleFirst = false; }
                    else flexibleBounds.Encapsulate(vertices[i]);
                }
            FlexibleBounds.Add(result, flexibleFirst ? source.bounds : flexibleBounds);
            Bounds bounds = source.bounds;
            // The per-instance renderer bounds are expanded in Awake, including child and nonuniform scaling.
            result.bounds = bounds;
            if (created) AssetDatabase.CreateAsset(result, path); else EditorUtility.SetDirty(result);
            Meshes.Add(key, result); return result;
        }

        private static Material PrepareMaterial(Material source)
        {
            if (source == null) return null;
            if (Materials.TryGetValue(source, out var material)) return material;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long fileId);
            bool treeCreator = source.shader.name.Contains("Tree Creator"), leaf = IsLeaf(source);
            string shaderName = treeCreator ? "Jurassic World/Interactive Tree " + (leaf ? "Leaves" : "Bark") :
                "Jurassic World/Interactive " + (leaf ? "Cutout" : "Opaque");
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Missing interaction shader: " + shaderName);
            string path = AssetsRoot + "/Materials/" + guid + "_" + fileId + ".mat";
            material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(source); else EditorUtility.CopySerialized(source, material);
            material.name = source.name + " Interactive"; material.shader = shader;
            // Additional vertex streams are not supported by Unity GPU instancing. Idle trees use original materials.
            material.shaderKeywords = Array.Empty<string>(); material.enableInstancing = false;
            if (!treeCreator)
            {
                float gloss = source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : .15f;
                material.SetFloat("_Glossiness", gloss);
                material.SetFloat("_HasMetallicGlossMap", source.HasProperty("_MetallicGlossMap") && source.GetTexture("_MetallicGlossMap") != null ? 1f : 0f);
            }
            else material.SetVector("_TreeInstanceScale", Vector4.one);
            material.renderQueue = source.renderQueue;
            if (created) AssetDatabase.CreateAsset(material, path); else EditorUtility.SetDirty(material);
            Materials.Add(source, material); return material;
        }

        private static void ApplyTreeTint(InteractiveFoliage foliage)
        {
            var block = new MaterialPropertyBlock();
            foreach (Renderer renderer in foliage.renderers)
            {
                renderer.GetPropertyBlock(block); block.SetColor("_TreeInstanceColor", foliage.instanceColor);
                block.SetColor("_TreeInstanceLightmapColor", foliage.instanceLightmapColor);
                renderer.SetPropertyBlock(block);
            }
        }

        private static DinosaurFoliageInteractor BakeInteractor(DinosaurAgent agent)
        {
            var interactor = agent.GetComponent<DinosaurFoliageInteractor>() ?? agent.gameObject.AddComponent<DinosaurFoliageInteractor>();
            var capsules = new List<DinosaurFoliageInteractor.Capsule>();
            foreach (SkinnedMeshRenderer skin in agent.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = skin.sharedMesh;
                if (mesh == null) continue;
                Vector3[] vertices = mesh.vertices; BoneWeight[] weights = mesh.boneWeights; Matrix4x4[] bindposes = mesh.bindposes;
                for (int boneIndex = 0; boneIndex < skin.bones.Length && boneIndex < bindposes.Length; boneIndex++)
                {
                    Transform bone = skin.bones[boneIndex];
                    if (bone == null) continue;
                    string name = bone.name.ToLowerInvariant();
                    if (!(name.Contains("tail") || name.Contains("spine") || name.Contains("hips") || name.Contains("pelvis") ||
                        name.Contains("chest") || name.Contains("breast") || name.Contains("neck") || name.Contains("head") || name == "root")) continue;
                    Bounds bounds = new Bounds(); bool first = true;
                    for (int i = 0; i < vertices.Length && i < weights.Length; i++)
                    {
                        BoneWeight w = weights[i];
                        float weight = (w.boneIndex0 == boneIndex ? w.weight0 : 0f) + (w.boneIndex1 == boneIndex ? w.weight1 : 0f)
                            + (w.boneIndex2 == boneIndex ? w.weight2 : 0f) + (w.boneIndex3 == boneIndex ? w.weight3 : 0f);
                        if (weight < .35f) continue;
                        Vector3 point = bindposes[boneIndex].MultiplyPoint3x4(vertices[i]);
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                    }
                    if (first) continue;
                    Vector3 extents = bounds.extents, direction;
                    float radial, length;
                    if (extents.x >= extents.y && extents.x >= extents.z) { direction = Vector3.right; radial = new Vector2(extents.y, extents.z).magnitude; length = extents.x; }
                    else if (extents.y >= extents.z) { direction = Vector3.up; radial = new Vector2(extents.x, extents.z).magnitude; length = extents.y; }
                    else { direction = Vector3.forward; radial = new Vector2(extents.x, extents.y).magnitude; length = extents.z; }
                    float boneScale = Mathf.Max(Mathf.Abs(bone.lossyScale.x), Mathf.Abs(bone.lossyScale.y), Mathf.Abs(bone.lossyScale.z));
                    capsules.Add(new DinosaurFoliageInteractor.Capsule { bone = bone, start = bounds.center - direction * length,
                        end = bounds.center + direction * length, radius = Mathf.Max(.025f, radial * boneScale) });
                }
            }
            if (capsules.Count == 0 || !capsules.Any(c => c.bone.name.IndexOf("tail", StringComparison.OrdinalIgnoreCase) >= 0))
                throw new InvalidOperationException("Body and tail bone profile required for " + agent.name);
            interactor.capsules = capsules.ToArray(); interactor.animationEnvelope = MeasureAnimationEnvelope(agent);
            EditorUtility.SetDirty(interactor);
            return interactor;
        }

        private static Bounds MeasureAnimationEnvelope(DinosaurAgent agent)
        {
            var loader = agent.GetComponent<DinosaurFbxLoader>();
            if (loader == null || loader.ModelRoot == null) throw new InvalidOperationException("Missing dinosaur visual root.");
            var holder = new GameObject("Foliage animation measurement") { hideFlags = HideFlags.HideAndDontSave };
            GameObject clone = Object.Instantiate(loader.ModelRoot, holder.transform, false);
            var baked = new Mesh(); Bounds result = new Bounds(); bool first = true;
            try
            {
                Animation animation = clone.GetComponentInChildren<Animation>(true);
                if (animation == null) throw new InvalidOperationException("Missing dinosaur animation.");
                foreach (AnimationState state in animation)
                {
                    if (state.name.EndsWith("_RM", StringComparison.OrdinalIgnoreCase)) continue;
                    for (int sample = 0; sample <= 12; sample++)
                    {
                        state.clip.SampleAnimation(animation.gameObject, state.length * sample / 12f);
                        foreach (SkinnedMeshRenderer skin in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            skin.BakeMesh(baked);
                            IncludeBounds(ref result, ref first, baked.bounds, holder.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix);
                        }
                    }
                }
                foreach (var capsule in agent.GetComponent<DinosaurFoliageInteractor>().capsules)
                {
                    Vector3 a = agent.transform.InverseTransformPoint(capsule.bone.TransformPoint(capsule.start));
                    Vector3 b = agent.transform.InverseTransformPoint(capsule.bone.TransformPoint(capsule.end));
                    var bounds = new Bounds(a, Vector3.zero); bounds.Encapsulate(b); bounds.Expand(capsule.radius * 2f);
                    IncludeBounds(ref result, ref first, bounds, Matrix4x4.identity);
                }
                return result;
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(holder); }
        }

        internal static string TreeSignature(TreeInstance[] trees, TreePrototype[] prototypes)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                foreach (TreePrototype prototype in prototypes)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prototype.prefab, out string guid, out long fileId);
                    writer.Write(guid); writer.Write(fileId); writer.Write(prototype.bendFactor);
                }
                foreach (TreeInstance tree in trees)
                {
                    writer.Write(tree.prototypeIndex); writer.Write(tree.position.x); writer.Write(tree.position.y); writer.Write(tree.position.z);
                    writer.Write(tree.rotation); writer.Write(tree.widthScale); writer.Write(tree.heightScale);
                    writer.Write(tree.color.r); writer.Write(tree.color.g); writer.Write(tree.color.b); writer.Write(tree.color.a);
                    writer.Write(tree.lightmapColor.r); writer.Write(tree.lightmapColor.g); writer.Write(tree.lightmapColor.b); writer.Write(tree.lightmapColor.a);
                }
                return Hash(stream.ToArray());
            }
        }
        private static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").Substring(0, 20);
        }

        public static void ValidateCurrent()
        {
            var ledger = Object.FindObjectOfType<FoliageTerrainConversion>();
            if (ledger == null) throw new InvalidOperationException("Missing foliage ownership ledger.");
            TerrainData data = ledger.terrain.terrainData;
            if (data.treeInstanceCount + ledger.removed.Length != ledger.originalTreeCount ||
                TreeSignature(data.treeInstances, data.treePrototypes) != ledger.retainedSignature)
                throw new InvalidOperationException("Foliage conversion changed the tree population or retained layout.");
            int converted = Object.FindObjectsOfType<InteractiveFoliage>().Count(t => t.convertedTerrainTree);
            if (converted != ledger.removed.Length) throw new InvalidOperationException("Missing/duplicate converted foliage renderer.");
            foreach (var tree in Object.FindObjectsOfType<InteractiveFoliage>())
                foreach (Renderer renderer in tree.renderers)
                {
                    if (renderer.GetComponent<MeshFilter>() == null || renderer.GetComponent<MeshFilter>().sharedMesh == null ||
                        ((MeshRenderer)renderer).additionalVertexStreams == null ||
                        ((MeshRenderer)renderer).additionalVertexStreams.vertexCount != renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount ||
                        renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(m => m == null))
                        throw new InvalidOperationException("Missing interactive tree mesh/material: " + renderer.name);
                    if (renderer.sharedMaterials.Any(m => m != null && !m.shader.name.StartsWith("Jurassic World/Interactive ", StringComparison.Ordinal)))
                        throw new InvalidOperationException("Non-interactive tree material: " + renderer.name);
                }
        }
    }
}
