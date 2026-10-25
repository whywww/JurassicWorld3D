using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class RainforestGreenWallFix
    {
        internal const string Report = "Reports/Backdrop/GreenWallFix";
        private const string Prefab = "Assets/Environment/Backdrop/Prepared/RainforestBackdrop.prefab";

        [MenuItem("Jurassic World/Environment/Backdrop/Repair Green Ground Wall")]
        public static void ApplyOpenEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty ||
                EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Main.unity")
                throw new InvalidOperationException("Stop Play and save Main before repairing the ground wall.");
            ApplyCurrent();
            RainforestLightForestChecks.GreenWallCheck();
        }

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            ApplyCurrent();
        }

        private static void ApplyCurrent()
        {
            Directory.CreateDirectory(Report);
            Terrain terrain = Terrain.activeTerrain;
            GameObject root = GameObject.Find(RainforestBackdropStageThree.RootName);
            Transform ridges = root.transform.Find("Distant Cliff Ridges");
            string ridgeState = RainforestBoundaryTransition.RidgeState(ridges);
            string protectedHashes = ProtectedHashes();
            if (!File.Exists(Report+"/Main.Before.unity"))
            {
                File.Copy("Assets/Scenes/Main.unity",Report+"/Main.Before.unity");
                File.Copy(Prefab,Report+"/Backdrop.Before.prefab");
                // Preserve every overwritten resource, including the previous merged understory heights.
                string[] resources = root.GetComponentsInChildren<MeshFilter>().Select(f => AssetDatabase.GetAssetPath(f.sharedMesh))
                    .Where(p => p.StartsWith(RainforestLightForest.Prepared+"/",StringComparison.Ordinal) &&
                        (Path.GetFileName(p).StartsWith("StaticBatch",StringComparison.Ordinal) || Path.GetFileName(p) == "ForestTransitionGround.asset"))
                    .Concat(new[] { AssetDatabase.GetAssetPath(root.transform.Find("Unwalkable Forest Skirt").GetComponent<Renderer>().sharedMaterial) })
                    .Distinct().ToArray();
                foreach (string resource in resources)
                {
                    string backup = Report+"/Resources/"+resource;
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    File.Copy(resource,backup);
                }
                RainforestBoundaryTransition.Capture(Camera.main,"Before",Report);
                CaptureUserView("Before");
            }
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            MoveRoots(root.transform.Find("Outer Forest Canopy"),terrain,-0.12f);
            MoveRoots(root.transform.Find(RainforestLightForest.UnderstoryName),terrain,-0.05f);
            MoveRoots(root.transform.Find(RainforestLightForest.ProxiesName),terrain,-1.2f);
            MeshFilter ground = root.transform.Find("Unwalkable Forest Skirt").GetComponent<MeshFilter>();
            ground.sharedMesh = RainforestBoundaryTransition.SkirtMesh(terrain);
            Material material = ground.GetComponent<Renderer>().sharedMaterial;
            Color tint = new Color(0.95f,0.92f,0.84f);
            material.SetColor("_Color",QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint);
            material.SetFloat("_VertexTint",1);
            EditorUtility.SetDirty(material);
            RainforestLightForest.BatchGeometry(root.transform,root.layer);
            if (ridgeState != RainforestBoundaryTransition.RidgeState(ridges) || ProtectedHashes() != protectedHashes)
                throw new InvalidOperationException("Ground repair changed protected scene resources or ridges.");
            RainforestBackdrop binder = root.GetComponent<RainforestBackdrop>();
            binder.viewCamera = null;
            PrefabUtility.SaveAsPrefabAssetAndConnect(root,Prefab,InteractionMode.AutomatedAction);
            binder.viewCamera = Camera.main; binder.ApplyCamera();
            PrefabUtility.RecordPrefabInstancePropertyModifications(binder);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            RainforestBackdropStageThree.Validate();
            ValidateGround(terrain);
            RainforestBoundaryTransition.Capture(Camera.main,"After",Report);
            CaptureUserView("After");
            File.WriteAllText(Report+"/Checks.txt",
                "PASS: no continuous raised berm; added relief <= 0.65m; terrain edge heights continuous; isolated patches include zero-rise gaps.\n"+
                "192 trees / 192 understory plants / 96 proxies retained; original XZ, rotation, scale and counts preserved; root Y follows repaired surface.\n"+
                "Terrain/profile/navigation files and ridge transforms/mesh/material references unchanged during repair.\n"+
                "16 boundary comparisons and reproduction at X=78, Z=-17.2, yaw=76 degrees captured.\n"+
                "No extra runtime scripts, objects or texture samples. Manual Play approval pending; stage four not started.\n");
            Debug.Log("GREEN_WALL_FIX_READY: ground lowered to isolated <=0.65m relief; scene saved; pause for manual Play.");
        }

        private static void MoveRoots(Transform parent, Terrain terrain, float offset)
        {
            foreach (Transform child in parent)
            {
                Vector3 p = child.position;
                p.y = RainforestBoundaryTransition.SurfaceHeight(terrain,new Vector2(p.x,p.z))+offset;
                child.position = p;
            }
        }

        private static string ProtectedHashes()
        {
            using (SHA256 hash = SHA256.Create()) return string.Join(";",new[] {
                RainforestAtmosphereStageOne.ProfilePath,"Assets/Environment/Terrain/Prepared/MainInteractionTerrain.asset",
                "Assets/Navigation/TRexInteractionNavMesh.asset"
            }.Select(p => { using (FileStream file = File.OpenRead(p)) return BitConverter.ToString(hash.ComputeHash(file)); }));
        }

        internal static void ValidateGround(Terrain terrain)
        {
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            Rect rect = new Rect(origin.x,origin.z,size.x,size.z);
            int gaps = 0; float maxRise = 0;
            for (int i = 0; i < 256; i++)
            {
                float angle = i*Mathf.PI*2/256;
                Vector2 dir = new Vector2(Mathf.Sin(angle),Mathf.Cos(angle));
                for (int band = 0; band < 4; band++)
                {
                    Vector2 p = rect.center+dir*(160+band*20);
                    Vector3 edge = new Vector3(Mathf.Clamp(p.x,rect.xMin,rect.xMax),0,Mathf.Clamp(p.y,rect.yMin,rect.yMax));
                    float d = RainforestBoundaryTransition.Distance(rect,p);
                    float baseline = Mathf.Lerp(terrain.SampleHeight(edge)+origin.y,-1.3f,Mathf.SmoothStep(0,1,d/60));
                    float rise = RainforestBoundaryTransition.SurfaceHeight(terrain,p)-baseline;
                    if (rise < -0.001f || rise > 0.651f) throw new InvalidOperationException("Boundary relief exceeded the low mound limit.");
                    if (rise < 0.01f) gaps++;
                    maxRise = Mathf.Max(maxRise,rise);
                }
                Vector2 seam = rect.center+dir*Mathf.Min(rect.width/2/Mathf.Max(0.0001f,Mathf.Abs(dir.x)),rect.height/2/Mathf.Max(0.0001f,Mathf.Abs(dir.y)));
                float seamHeight = terrain.SampleHeight(new Vector3(seam.x,0,seam.y))+origin.y;
                if (Mathf.Abs(RainforestBoundaryTransition.SurfaceHeight(terrain,seam)-seamHeight) > 0.002f)
                    throw new InvalidOperationException("Ground repair detached from the terrain edge.");
            }
            if (gaps < 256) throw new InvalidOperationException("Ground relief still forms a continuous belt.");
            Debug.Log("GREEN_WALL_GROUND_VALID: max added rise="+maxRise.ToString("F3")+"m; zero-rise samples="+gaps+"/1024; seam continuous.");
        }

        internal static void CaptureUserView(string suffix)
        {
            Camera camera = Camera.main;
            Vector3 before = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            float aspect = camera.aspect;
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            var target = new RenderTexture(1280,552,24);
            var pixels = new Texture2D(1280,552,TextureFormat.RGB24,false);
            var atmosphere = Object.FindObjectOfType<RainforestAtmosphereController>();
            Vector3 oldCloud = atmosphere.cloudRenderer.transform.position;
            try
            {
                Vector3 eye = new Vector3(78,0,-17.2f);
                eye.y = Terrain.activeTerrain.SampleHeight(eye)+Terrain.activeTerrain.transform.position.y+1.7f;
                camera.transform.SetPositionAndRotation(eye,Quaternion.Euler(0,76,0));
                camera.targetTexture = target; camera.aspect = 1280f/552;
                atmosphere.cloudRenderer.transform.position = eye;
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,1280,552),0,0); pixels.Apply();
                File.WriteAllBytes(Report+"/UserView."+suffix+".png",pixels.EncodeToPNG());
            }
            finally
            {
                camera.transform.SetPositionAndRotation(before,rotation); camera.targetTexture = oldTarget; camera.aspect = aspect;
                atmosphere.cloudRenderer.transform.position = oldCloud; RenderTexture.active = oldActive;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
    }
}
