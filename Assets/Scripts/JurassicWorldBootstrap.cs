using UnityEngine;

namespace JurassicWorld
{
    public sealed class JurassicWorldBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (FindObjectOfType<JurassicWorldBootstrap>() == null)
            {
                new GameObject("Jurassic World MVP").AddComponent<JurassicWorldBootstrap>();
            }
        }

        private void Awake()
        {
            if (FindObjectsOfType<JurassicWorldBootstrap>().Length > 1)
            {
                Destroy(gameObject);
                return;
            }

            CreateLighting();
            CreateGround();
            Camera camera = CreateCamera();
            HudController.Create(camera);
            CreateDinosaurs(camera);
        }

        private static void CreateLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.66f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.42f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.20f, 0.12f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.58f, 0.70f, 0.66f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 105f;

            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var skybox = new Material(skyShader);
                skybox.SetColor("_SkyTint", new Color(0.40f, 0.58f, 0.76f));
                skybox.SetColor("_GroundColor", new Color(0.20f, 0.26f, 0.16f));
                skybox.SetFloat("_AtmosphereThickness", 0.75f);
                skybox.SetFloat("_Exposure", 1.15f);
                RenderSettings.skybox = skybox;
            }

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.91f, 0.74f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Grassland";
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            ground.GetComponent<Renderer>().material = MakeMaterial(new Color(0.19f, 0.39f, 0.12f), 0.05f);

            var random = new System.Random(271828);
            for (int i = 0; i < 26; i++)
            {
                float x = (float)(random.NextDouble() * 88.0 - 44.0);
                float z = (float)(random.NextDouble() * 88.0 - 44.0);
                if (new Vector2(x, z).magnitude < 10f)
                    continue;

                GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = "Rock";
                rock.transform.position = new Vector3(x, 0.25f, z);
                rock.transform.localScale = new Vector3(1.4f, 0.5f, 1.1f) * (float)(0.6 + random.NextDouble());
                rock.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                rock.GetComponent<Renderer>().material = MakeMaterial(new Color(0.28f, 0.31f, 0.22f), 0f);
                Destroy(rock.GetComponent<Collider>());
            }
        }

        private static Material MakeMaterial(Color color, float smoothness)
        {
            var material = new Material(Shader.Find("Standard"));
            material.color = color;
            material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        private static Camera CreateCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.transform.position = new Vector3(0f, 3f, -16f);
            camera.transform.rotation = Quaternion.Euler(2f, 0f, 0f);
            camera.fieldOfView = 65f;
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 150f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.gameObject.AddComponent<SmoothExplorerCamera>();
            return camera;
        }

        private static void CreateDinosaurs(Camera camera)
        {
            CreateGltfDinosaur(
                camera, "Tyrannosaurus Rex", "霸王龙", DinosaurGltfLoader.RelativeModelPath,
                new Vector3(0f, 0f, 11f), Vector3.one, 0f, 2.8f, 5.2f,
                "霸王龙  Tyrannosaurus Rex\n\n生存年代：晚白垩世\n食性：肉食性\n体型：约 12–13 米\n\n强壮的顶级掠食者，拥有惊人的咬合力。",
                new Vector3(4.5f, 5.8f, 0f), "idle", "run");

            CreateFbxDinosaur(
                camera, "Rajasaurus", "胜王龙", "Dinosaurs/Rajasaurus/source/Rajasaurus",
                new Vector3(-10f, 0f, 19f), 9f, 180f, 2.4f, 1.6f,
                "胜王龙  Rajasaurus\n\n生存年代：晚白垩世\n食性：肉食性\n分布：印度\n\n头骨短而厚，是印度次大陆特有的阿贝力龙类。",
                new Vector3(3.8f, 5.0f, 0f), "idle", "walk",
                "Dinosaurs/Rajasaurus/textures/AlbedoMap", null, null, null,
                "Dinosaurs/Rajasaurus/textures/AmbientOcclusionMap",
                new[] { "Bone.L.007", "Bone.R.007" });

            CreateFbxDinosaur(
                camera, "Velociraptor", "伶盗龙", "Dinosaurs/Velociraptor/Raptor/Raptor_Animated_FBX",
                new Vector3(8f, 0f, 15f), 2.4f, 180f, 1.3f, 5.5f,
                "伶盗龙  Velociraptor\n\n生存年代：晚白垩世\n食性：肉食性\n体型：约 2 米\n\n敏捷的小型猎手，后足具有醒目的镰刀状趾爪。",
                new Vector3(2.3f, 2.8f, 0f), "idle", "run",
                "Dinosaurs/Velociraptor/Raptor/Textures/Raptor_PatternA_Albedo",
                "Dinosaurs/Velociraptor/Raptor/Textures/Other_Albedo2 1K",
                "Dinosaurs/Velociraptor/Raptor/Textures/Raptor_Normal",
                "Dinosaurs/Velociraptor/Raptor/Textures/Other_Normal 1K",
                "Dinosaurs/Velociraptor/Raptor/Textures/Raptor_AO");

            CreateFbxDinosaur(
                camera, "Argentinosaurus", "阿根廷龙", "Dinosaurs/Argentinosaurus/source/Argentinosaurus",
                new Vector3(-13f, 0f, 35f), 27f, 180f, 5.5f, 0.7f,
                "阿根廷龙  Argentinosaurus\n\n生存年代：晚白垩世\n食性：植食性\n体型：约 30–35 米\n\n已知最大的陆生动物之一，拥有极长的颈部和尾部。",
                new Vector3(8f, 10f, 0f), "idle", "walk",
                "Dinosaurs/Argentinosaurus/textures/AlbedoMap", null, null, null,
                "Dinosaurs/Argentinosaurus/textures/AmbientOcclutionMap",
                footBoneNames: new[] { "Bone.l.002", "Bone.r.002", "Bone.l.006", "Bone.r.006" },
                footLockJointDepth: 2, releaseFootLockOnRise: true, enableFootLock: true,
                footContactHeight: 0.08f, footRiseThreshold: 0.0005f, footLockDistanceLimit: 0.75f);

            CreateGltfDinosaur(
                camera, "Pterodactyl", "翼龙", "Dinosaur/Pteradactal.glb",
                new Vector3(10f, 11f, 27f), Vector3.one, 180f, 1.2f, 3.8f,
                "翼龙  Pterodactyl\n\n生存年代：晚侏罗世\n食性：肉食性\n活动方式：飞行\n\n轻盈的飞行爬行动物，会在草原上空持续巡游。",
                new Vector3(2.8f, 2.2f, 0f), "PixCap", "PixCap", 8f, false);
        }

        private static void CreateGltfDinosaur(Camera camera, string objectName, string displayName, string modelPath,
            Vector3 position, Vector3 scale, float yaw, float focusHeight, float speed,
            string description, Vector3 cardPosition, string idleHint, string moveHint,
            float targetLength = 0f, bool alignBottom = true)
        {
            GameObject dinosaur = CreateDinosaurRoot(camera, objectName, displayName, position, focusHeight, speed);
            var loader = dinosaur.AddComponent<DinosaurGltfLoader>();
            dinosaur.GetComponent<DinosaurAgent>().VisualYaw = yaw;
            loader.ModelPath = modelPath;
            loader.DisplayName = displayName;
            loader.ModelScale = scale;
            loader.ModelYaw = yaw;
            loader.TargetLength = targetLength;
            loader.AlignBottom = alignBottom;
            loader.IdleAnimationHint = idleHint;
            loader.MoveAnimationHint = moveHint;
            DinosaurInfoCard.Create(dinosaur.transform, camera, dinosaur.GetComponent<DinosaurAgent>(), description, cardPosition);
        }

        private static void CreateFbxDinosaur(Camera camera, string objectName, string displayName, string resourcePath,
            Vector3 position, float targetLength, float yaw, float focusHeight, float speed,
            string description, Vector3 cardPosition, string idleHint, string moveHint,
            string primaryAlbedo, string secondaryAlbedo, string primaryNormal, string secondaryNormal, string occlusion,
            string[] footBoneNames = null, int footLockJointDepth = 3, bool releaseFootLockOnRise = false,
            bool enableFootLock = true, float footContactHeight = -1f, float footRiseThreshold = -1f,
            float footLockDistanceLimit = -1f)
        {
            GameObject dinosaur = CreateDinosaurRoot(camera, objectName, displayName, position, focusHeight, speed);
            var loader = dinosaur.AddComponent<DinosaurFbxLoader>();
            dinosaur.GetComponent<DinosaurAgent>().VisualYaw = yaw;
            loader.ResourcePath = resourcePath;
            loader.DisplayName = displayName;
            loader.TargetLength = targetLength;
            loader.ModelYaw = yaw;
            loader.IdleAnimationHint = idleHint;
            loader.MoveAnimationHint = moveHint;
            loader.PrimaryAlbedoPath = primaryAlbedo;
            loader.SecondaryAlbedoPath = secondaryAlbedo;
            loader.PrimaryNormalPath = primaryNormal;
            loader.SecondaryNormalPath = secondaryNormal;
            loader.OcclusionPath = occlusion;
            loader.FootBoneNames = footBoneNames;
            loader.FootLockJointDepth = footLockJointDepth;
            loader.ReleaseFootLockOnRise = releaseFootLockOnRise;
            loader.EnableFootLock = enableFootLock;
            loader.FootContactHeight = footContactHeight;
            loader.FootRiseThreshold = footRiseThreshold;
            loader.FootLockDistanceLimit = footLockDistanceLimit;
            DinosaurInfoCard.Create(dinosaur.transform, camera, dinosaur.GetComponent<DinosaurAgent>(), description, cardPosition);
        }

        private static GameObject CreateDinosaurRoot(Camera camera, string objectName, string displayName,
            Vector3 position, float focusHeight, float speed)
        {
            var dinosaur = new GameObject(objectName);
            dinosaur.transform.position = position;
            var agent = dinosaur.AddComponent<DinosaurAgent>();
            agent.ViewCamera = camera;
            agent.DisplayName = displayName;
            agent.MovementHeight = position.y;
            agent.ViewFocusHeight = focusHeight;
            agent.WalkSpeed = speed;
            agent.MoveAnimationGroundSpeed = speed;
            agent.ApproachSpeed = Mathf.Max(speed * 1.35f, speed + 0.5f);
            return dinosaur;
        }
    }
}
