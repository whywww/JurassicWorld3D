using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    // All placement and atlas rendering happens in the editor. The player only draws meshes.
    public static class RainforestLightForest
    {
        internal const string Prepared = "Assets/Environment/Backdrop/Prepared/LightForest";
        internal const string Report = "Reports/Backdrop/LightForest";
        internal const string UnderstoryName = "Boundary Understory";
        internal const string ProxiesName = "Baked Forest Groups";
        private const int BakeLayer = 30;
        private static readonly int[] Neighbours = { -1,1,-512,512 };

        [MenuItem("Jurassic World/Environment/Backdrop/Bake Lightweight Forest Atlases")]
        public static void BakeAtlases()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before baking forest atlases.");
            Directory.CreateDirectory(Prepared);
            AssetDatabase.Refresh();
            Terrain terrain = Terrain.activeTerrain;
            GameObject[] trees = terrain.terrainData.treePrototypes.Take(4).Select(t => t.prefab).ToArray();
            Mesh[] meshes = Enumerable.Range(0,4).Select(i => AssetDatabase.LoadAssetAtPath<Mesh>(
                "Assets/Environment/Backdrop/Prepared/Meshes/CanopyTree"+i+".asset")).ToArray();
            Shader bakeShader = Shader.Find("Hidden/Jurassic World/Forest Atlas Bake");
            if (bakeShader == null || ShaderUtil.ShaderHasError(bakeShader)) throw new InvalidOperationException("Forest atlas bake shader has errors.");
            Material[][] materials = trees.Select(t => t.GetComponentInChildren<MeshFilter>().GetComponent<Renderer>().sharedMaterials.Select(source =>
            {
                var material = new Material(bakeShader);
                material.SetTexture("_MainTex",source.GetTexture("_MainTex"));
                material.SetColor("_Color",source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
                material.SetFloat("_Cutoff",source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0f);
                material.SetFloat("_LeafBillboards",source.shader.name.Contains("Leaves") ? 1f : 0f);
                return material;
            }).ToArray()).ToArray();
            var cameraObject = new GameObject("Offline Forest Atlas Camera");
            var stage = new GameObject("Offline Forest Atlas Stage");
            stage.transform.position = new Vector3(1000,0,1000);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 26f;
            camera.aspect = 1; camera.nearClipPlane = 0.1f; camera.farClipPlane = 200;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << BakeLayer; camera.allowHDR = false; camera.allowMSAA = false;
            var target = new RenderTexture(512,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var tile = new Texture2D(512,512,TextureFormat.RGBA32,false);
            RenderTexture oldActive = RenderTexture.active;
            bool oldFog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false;
                camera.targetTexture = target;
                for (int variant = 0; variant < 4; variant++)
                {
                    foreach (Transform child in stage.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                    var random = new System.Random(41981+variant);
                    // Large trees plus a lower, overlapping tier fill the under-canopy silhouette.
                    for (int n = 0; n < 20; n++)
                    {
                        int type = (n+variant)%4;
                        float angle = n*2.399963f;
                        float radius = n < 9 ? 7f : 11f;
                        float height = n < 9 ? 24f+(float)random.NextDouble()*10 : 5f+(float)random.NextDouble()*10;
                        GameObject tree = RainforestBackdropStageThree.Geometry("Bake Tree",stage.transform,meshes[type],materials[type],BakeLayer);
                        tree.transform.localPosition = new Vector3(Mathf.Sin(angle)*radius,0,Mathf.Cos(angle)*radius);
                        tree.transform.localRotation = Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                        float scale = height/meshes[type].bounds.size.y;
                        tree.transform.localScale = Vector3.one*scale;
                    }
                    var atlas = new Texture2D(2048,2048,TextureFormat.RGBA32,false);
                    try
                    {
                        for (int direction = 0; direction < 16; direction++)
                        {
                            float angle = direction*22.5f*Mathf.Deg2Rad;
                            Vector3 center = stage.transform.position+Vector3.up*25;
                            camera.transform.position = center+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*80;
                            camera.transform.LookAt(center);
                            camera.Render(); RenderTexture.active = target;
                            tile.ReadPixels(new Rect(0,0,512,512),0,0); tile.Apply();
                            Color32[] pixels = tile.GetPixels32();
                            DilateColor(pixels);
                            atlas.SetPixels32((direction%4)*512,(direction/4)*512,512,512,pixels);
                        }
                        atlas.Apply();
                        string path = AtlasPath(variant);
                        File.WriteAllBytes(path,atlas.EncodeToPNG());
                        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                        importer.textureType = TextureImporterType.Default;
                        importer.alphaSource = TextureImporterAlphaSource.FromInput;
                        importer.alphaIsTransparency = true; importer.sRGBTexture = true;
                        importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp;
                        importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.CompressedHQ;
                        importer.SaveAndReimport();
                    }
                    finally { Object.DestroyImmediate(atlas); }
                }
            }
            finally
            {
                RenderSettings.fog = oldFog; RenderTexture.active = oldActive;
                camera.targetTexture = null; target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(tile);
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(stage);
                foreach (Material material in materials.SelectMany(m => m)) Object.DestroyImmediate(material);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("LIGHT_FOREST_ATLASES_READY: four 2048px atlases, sixteen directions each; no runtime baking.");
        }

        private static void DilateColor(Color32[] pixels)
        {
            // Supply RGB around cutout edges without increasing alpha coverage.
            var filled = pixels.Select(p => p.a > 0).ToArray();
            for (int pass = 0; pass < 8; pass++)
            {
                var next = (bool[])filled.Clone();
                for (int y = 1; y < 511; y++) for (int x = 1; x < 511; x++)
                {
                    int i = y*512+x;
                    if (filled[i]) continue;
                    foreach (int delta in Neighbours)
                        if (filled[i+delta])
                        {
                            Color32 p = pixels[i+delta]; p.a = pixels[i].a;
                            pixels[i] = p; next[i] = true; break;
                        }
                }
                filled = next;
            }
        }

        private static string AtlasPath(int variant) => Prepared+"/ForestGroup"+variant+".png";

        internal static Vector2 Position(Rect bounds, int index, int count, float distance, System.Random random)
        {
            float angle = (index+0.5f+((float)random.NextDouble()-0.5f)*0.5f)*Mathf.PI*2/count;
            Vector2 dir = new Vector2(Mathf.Sin(angle),Mathf.Cos(angle));
            float halfX = bounds.width/2, halfZ = bounds.height/2;
            float edge = Mathf.Min(halfX/Mathf.Max(0.0001f,Mathf.Abs(dir.x)),halfZ/Mathf.Max(0.0001f,Mathf.Abs(dir.y)));
            float low = edge, high = edge+distance*1.5f+1;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                float mid = (low+high)/2;
                if (RainforestBoundaryTransition.Distance(bounds,bounds.center+dir*mid) < distance) low = mid; else high = mid;
            }
            return bounds.center+dir*((low+high)/2);
        }

        internal static void PopulateExtras(Transform root, Terrain terrain, int layer)
        {
            Directory.CreateDirectory(Prepared);
            foreach (string name in new[] { UnderstoryName,ProxiesName })
            {
                Transform old = root.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var under = new GameObject(UnderstoryName); under.transform.SetParent(root,false); under.layer = layer;
            var random = new System.Random(41982);
            string[] sources = {
                "Assets/ThirdParty/NatureStarterKit2/Nature/bush01.prefab",
                "Assets/ThirdParty/NatureStarterKit2/Nature/bush03.prefab",
                "Assets/Environment/Vegetation/Fern02/Prepared/Prefabs/Fern02_A.prefab",
                "Assets/Environment/Vegetation/Fern02/Prepared/Prefabs/Fern02_B.prefab" };
            MeshFilter[] filters = sources.Select(s => AssetDatabase.LoadAssetAtPath<GameObject>(s).GetComponentInChildren<MeshFilter>()).ToArray();
            Mesh[] meshes = filters.Select((f,i) => RainforestBackdropStageThree.PreparedMesh("BoundaryUnderstory"+i,f)).ToArray();
            Material[][] materials = filters.Select((f,i) => f.GetComponent<Renderer>().sharedMaterials.Select((m,j) =>
                CopyMaterial("Understory"+i+"_"+j,m)).ToArray()).ToArray();
            for (int n = 0; n < 192; n++)
            {
                int type = n%4;
                float height = type < 2 ? 1.8f+(float)random.NextDouble()*1.2f : 1f+(float)random.NextDouble()*0.7f;
                float scale = height/meshes[type].bounds.size.y;
                float radius = new Vector2(meshes[type].bounds.extents.x,meshes[type].bounds.extents.z).magnitude*scale;
                Vector2 p = Position(RainforestBoundaryTransition.ProtectedBounds,n,192,
                    Mathf.Max(3f,radius+0.6f)+(n%3)*5f,random);
                GameObject plant = RainforestBackdropStageThree.Geometry("Boundary Plant "+n.ToString("000"),under.transform,meshes[type],materials[type],layer);
                plant.transform.position = new Vector3(p.x,RainforestBoundaryTransition.SurfaceHeight(terrain,p)-0.05f,p.y);
                plant.transform.rotation = Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                plant.transform.localScale = Vector3.one*scale;
            }
            var proxies = new GameObject(ProxiesName); proxies.transform.SetParent(root,false); proxies.layer = layer;
            Mesh quad = Quad();
            Material[] proxyMaterials = Enumerable.Range(0,4).Select(ProxyMaterial).ToArray();
            for (int n = 0; n < 96; n++)
            {
                int band = n/32;
                Vector2 p = Position(RainforestBoundaryTransition.ProtectedBounds,n%32,32,
                    new[] { 42f,74f,108f }[band]+((float)random.NextDouble()-0.5f)*10,random);
                GameObject proxy = RainforestBackdropStageThree.Geometry("Forest Proxy "+n.ToString("000"),proxies.transform,
                    quad,new[] { proxyMaterials[n%4] },layer);
                proxy.transform.position = new Vector3(p.x,RainforestBoundaryTransition.SurfaceHeight(terrain,p)-1.2f,p.y);
                proxy.transform.rotation = Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                proxy.transform.localScale = Vector3.one*Mathf.Lerp(0.95f,1.25f,(float)random.NextDouble());
            }
            BatchGeometry(root,layer);
        }

        internal static void BatchGeometry(Transform root, int layer)
        {
            const string name = "Boundary Static Batches";
            Transform old = root.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var batchRoot = new GameObject(name); batchRoot.transform.SetParent(root,false); batchRoot.layer = layer;
            Rect bounds = RainforestBoundaryTransition.ProtectedBounds;
            // Keep near-tree instancing and fine per-tree culling. Only batch the small understory meshes.
            Transform[] originals = root.Find(UnderstoryName).Cast<Transform>().ToArray();
            var usedPaths = new HashSet<string>();
            int batched = 0, packed = 0;
            for (int sector = 0; sector < 8; sector++)
            {
                var groups = new Dictionary<Material,List<CombineInstance>>();
                foreach (Transform original in originals)
                {
                    Vector2 p = new Vector2(original.position.x,original.position.z)-bounds.center;
                    float angle = Mathf.Repeat(Mathf.Atan2(p.x,p.y)+Mathf.PI,Mathf.PI*2);
                    if (Mathf.Min(7,Mathf.FloorToInt(angle/(Mathf.PI*2)*8)) != sector) continue;
                    Mesh mesh = original.GetComponent<MeshFilter>().sharedMesh;
                    Material[] materials = original.GetComponent<Renderer>().sharedMaterials;
                    // Tree Creator can encode camera-facing leaf offsets in normals/tangents.
                    // Such meshes must retain their object transform and are never combined.
                    if (mesh.tangents.Any(t => Mathf.Abs(t.w) < 0.5f)) { packed++; continue; }
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        Material material = materials[sub];
                        if (!groups.ContainsKey(material)) groups[material] = new List<CombineInstance>();
                        groups[material].Add(new CombineInstance { mesh=mesh,subMeshIndex=sub,transform=batchRoot.transform.worldToLocalMatrix*original.localToWorldMatrix });
                    }
                    original.GetComponent<Renderer>().enabled = false;
                    batched++;
                }
                int slot = 0;
                foreach (var group in groups.OrderBy(g => AssetDatabase.GetAssetPath(g.Key)))
                {
                    string path = Prepared+"/StaticBatch"+sector+"_"+slot+".asset";
                    usedPaths.Add(path);
                    Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
                    mesh.Clear(); mesh.indexFormat = IndexFormat.UInt32;
                    mesh.CombineMeshes(group.Value.ToArray(),true,true);
                    mesh.name = "Boundary sector "+sector+" material "+slot;
                    EditorUtility.SetDirty(mesh);
                    RainforestBackdropStageThree.Geometry(mesh.name,batchRoot.transform,mesh,new[] { group.Key },layer);
                    slot++;
                }
            }
            foreach (string path in Directory.GetFiles(Prepared,"StaticBatch*.asset"))
            {
                string normalized = path.Replace('\\','/');
                if (!usedPaths.Contains(normalized)) AssetDatabase.DeleteAsset(normalized);
            }
            Debug.Log("LIGHT_FOREST_STATIC_BATCHES: "+batched+" placed meshes combined; "+packed+" packed-billboard meshes preserved; "+batchRoot.transform.childCount+" spatial material batches.");
        }

        private static Material CopyMaterial(string name, Material source)
        {
            string path = Prepared+"/"+name+".mat";
            Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null) { result = new Material(source) { name = name, enableInstancing = true }; AssetDatabase.CreateAsset(result,path); }
            return result;
        }

        private static Material ProxyMaterial(int variant)
        {
            Shader shader = Shader.Find("Jurassic World/Forest Impostor");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Forest proxy shader has errors.");
            string path = Prepared+"/ForestGroup"+variant+".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader) { enableInstancing = true }; AssetDatabase.CreateAsset(material,path); }
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath(variant));
            if (atlas == null) throw new InvalidOperationException("Bake forest atlases before applying the lightweight boundary.");
            material.SetTexture("_MainTex",atlas);
            material.SetColor("_HazeColor",QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderSettings.fogColor.linear : RenderSettings.fogColor);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh Quad()
        {
            string path = Prepared+"/ForestProxyQuad.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "Forest Proxy - two triangles" };
            // The bake covers y=-1..51; overlap the base with the soil.
            mesh.vertices = new[] { new Vector3(-26,-1,0),new Vector3(26,-1,0),new Vector3(-26,51,0),new Vector3(26,51,0) };
            mesh.uv = new[] { Vector2.zero,Vector2.right,Vector2.up,Vector2.one };
            mesh.triangles = new[] { 0,2,1,1,2,3 };
            mesh.bounds = new Bounds(new Vector3(0,25,0),new Vector3(76,54,76));
            AssetDatabase.CreateAsset(mesh,path);
            return mesh;
        }

        internal static void Validate(Transform root, Terrain terrain)
        {
            Transform under = root.Find(UnderstoryName), proxies = root.Find(ProxiesName);
            if (under == null || under.childCount != 192 || proxies == null || proxies.childCount != 96)
                throw new InvalidOperationException("Lightweight forest group counts differ from the approved plan.");
            foreach (Transform plant in under)
            {
                if (plant.GetComponent<Renderer>().bounds.Intersects(new Bounds(
                    new Vector3(RainforestBoundaryTransition.ProtectedBounds.center.x,plant.position.y,RainforestBoundaryTransition.ProtectedBounds.center.y),
                    new Vector3(RainforestBoundaryTransition.ProtectedBounds.width,100,RainforestBoundaryTransition.ProtectedBounds.height))))
                    throw new InvalidOperationException("Understory footprint enters the gameplay protection area: "+plant.name);
                Vector2 p = new Vector2(plant.position.x,plant.position.z);
                if (Mathf.Abs(plant.position.y-(RainforestBoundaryTransition.SurfaceHeight(terrain,p)-0.05f)) > 0.02f)
                    throw new InvalidOperationException("Understory floats: "+plant.name);
            }
            foreach (Transform proxy in proxies)
                if (proxy.GetComponent<MeshFilter>().sharedMesh.triangles.Length != 6 ||
                    proxy.GetComponent<Renderer>().sharedMaterial.shader.name != "Jurassic World/Forest Impostor")
                    throw new InvalidOperationException("Forest proxy must use a single baked quad.");
            for (int n = 0; n < 4; n++)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath(n));
                if (texture == null || texture.width != 2048 || texture.height != 2048) throw new InvalidOperationException("Forest atlas missing or wrong size.");
            }
            Debug.Log("LIGHT_FOREST_VALID: 192 trees, 192 protected understory plants, 96 two-triangle proxies, four 16-view atlases.");
        }
    }
}
