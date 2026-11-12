using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class TRexBodyEnvelopeAcceptance
    {
        [MenuItem("Jurassic World/Acceptance/Verify T-Rex Body and Head Envelopes")]
        public static void Run()
        {
            VerifyPlayerPoseScaling();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Stop Play and save the scene before envelope acceptance.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
            var agent = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var profile = (DinosaurInteractionProfile)new SerializedObject(agent).FindProperty("interactionProfile").objectReferenceValue;
            var skin = agent.GetComponentInChildren<Animation>(true).GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.sharedMesh.boneWeights.Length > 0);
            var holder = new GameObject("Envelope acceptance probe"); var body = holder.AddComponent<DinosaurCoreBody>();
            var trunkObject = new GameObject("Envelope acceptance hard trunk"); var trunk = trunkObject.AddComponent<SceneTreeObstacle>();
            Vector2 origin = new Vector2(500f, 500f), forward = Vector2.up;
            var oldBounds = settings.activityBoundsOverride; var oldSolids = settings.solidObstacles;
            var report = new StringBuilder("# Body/head envelope acceptance\n\n");
            try
            {
                settings.activityBoundsOverride = new Rect(400, 400, 200, 200); settings.solidObstacles = Array.Empty<Bounds>();
                foreach (var motion in profile.motions)
                {
                    foreach (var pose in motion.playerPoses)
                    {
                        Require(pose.bands.Select(b => b.layer).Distinct().Count() == pose.bands.Length,
                            "Duplicate player bone groups: " + motion.sourceName);
                        Require(pose.bands.All(b => b.layer >= 100 && b.layer / 1000 < skin.bones.Length),
                            "Player bone IDs replaced by obstacle height layers: " + motion.sourceName);
                    }
                    Require(motion.obstaclePoses.Length > 1, "Missing pose cache: " + motion.sourceName);
                    for (int i = 0; i < motion.obstaclePoses.Length; i++)
                    {
                        Require(i == 0 || motion.obstaclePoses[i].time > motion.obstaclePoses[i - 1].time, "Non-increasing pose times.");
                        foreach (var band in motion.obstaclePoses[i].bands)
                        {
                            int index = (band.part - 1) % 1000;
                            Require(index >= 0 && index < skin.bones.Length && TRexBodyEnvelopeBake.Included(skin.bones[index]), "Excluded bone in hard envelope.");
                            Require(band.layer == Mathf.FloorToInt((band.bounds.min.y + band.bounds.max.y) * .5f / .5f)
                                || band.bounds.size.y < .0001f, "Bone ID incorrectly used as height layer.");
                        }
                    }
                }
                report.AppendLine($"- PASS: all {profile.motions.Length} Main motions have ordered pose caches; all parts belong to torso, hips, neck or head; height-layer IDs are valid.");
                var idle = profile.Find(InteractionMotionRole.LookForward); body.bands = idle.obstaclePoses[0].bands;
                foreach (string region in new[] { "Spine", "Neck", "Head" })
                {
                    var band = body.bands.First(b => skin.bones[(b.part - 1) % 1000].name.Contains(region));
                    Place(trunk, origin + new Vector2(band.bounds.center.x, band.bounds.center.z));
                    Require(settings.PoseBlockReason(origin, forward, body) == "Tree", region + " collision missed.");
                    Require(settings.LastPoseObstacle == trunkObject.name, "Missing hard obstacle name.");
                    report.AppendLine("- PASS: " + region + " hard-trunk overlap rejected, obstacle name retained.");
                }
                var tail = profile.Find(InteractionMotionRole.FollowWalk).playerPoses[0].bands.Where(b => skin.bones[b.layer / 1000].name.Contains("Tail")).OrderBy(b => b.bounds.center.z).First();
                Place(trunk, origin + new Vector2(tail.bounds.center.x, tail.bounds.center.z));
                Require(settings.PoseBlockReason(origin, forward, body) == null, "Tail-only trunk overlap blocked body/head.");
                report.AppendLine("- PASS: a hard trunk in the original tail geometry does not block the body/head pose.");
                trunk.enabled = false; TerrainTreeObstacleRegistry.Invalidate();
                var grass = new GameObject("Grass Fern envelope acceptance"); grass.transform.position = new Vector3(origin.x, 2, origin.y);
                grass.AddComponent<BoxCollider>().size = Vector3.one * 30;
                Require(settings.PoseBlockReason(origin, forward, body) == null, "Soft vegetation collider entered hard-obstacle query.");
                Object.DestroyImmediate(grass);
                report.AppendLine("- PASS: overlapping grass/fern collider does not enter hard-obstacle checks.");
                settings.activityBoundsOverride = oldBounds; settings.solidObstacles = oldSolids;
                var nav = UnityEngine.AI.NavMesh.AddNavMeshData(settings.navigationData);
                try
                {
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    typeof(DinosaurAgent).GetField("interactionSettings", flags).SetValue(agent, settings);
                    // Clearing acceptance uses the real body envelope against the walking gate
                    // (bounds, solid rocks, hard trunks, NavMesh) plus the peer-body gate. Actors are
                    // left out of the peer set so the terrain/static clearing itself is measured.
                    typeof(DinosaurAgent).GetField("peers", flags).SetValue(agent, Array.Empty<DinosaurAgent>());
                    var actorBody = agent.GetComponent<DinosaurCoreBody>();
                    Require(actorBody != null, "Main actor has no hard body envelope.");
                    var peerGate = typeof(DinosaurAgent).GetMethod("FrontPeerPoseBlocked", flags, null,
                        new[] { typeof(Vector3), typeof(Quaternion), typeof(DinosaurCoreBody) }, null);
                    var layout = Object.FindObjectOfType<RainforestSampleLayout>();
                    for (int i = 0; i < layout.clearingPositions.Length; i++)
                    {
                        var p = layout.clearingPositions[i]; p.y = agent.MovementHeight;
                        Quaternion heading = layout.clearingRotations[i];
                        Vector3 facing = heading * Quaternion.Euler(0f, agent.VisualYaw, 0f) * Vector3.forward;
                        Require(UnityEngine.AI.NavMesh.SamplePosition(p, out var sample, 1.5f, UnityEngine.AI.NavMesh.AllAreas)
                            && Vector3.Distance(sample.position, p) < 1f, "Clearing is not on the navigation mesh: " + i);
                        string reason = settings.PoseBlockReason(new Vector2(p.x, p.z), new Vector2(facing.x, facing.z), actorBody);
                        Require(reason == null, "Existing clearing rejected by the walking pose: " + i + " / " + reason);
                        Require(!(bool)peerGate.Invoke(agent, new object[] { p, heading, actorBody }), "Clearing overlaps another dinosaur body: " + i);
                        report.AppendLine($"- PASS: walking pose and peer probe accept clearing {i} (bounds, trunks, solids, NavMesh).");
                    }
                }
                finally { nav.Remove(); }
                report.Insert(0, "PASS BODY_HEAD_ENVELOPES\n\n");
                File.WriteAllText(RainforestSample.Output + "/BodyHeadAcceptance.md", report.ToString());
                Debug.Log("PASS BODY_HEAD_ENVELOPES");
            }
            finally
            {
                settings.activityBoundsOverride = oldBounds; settings.solidObstacles = oldSolids;
                Object.DestroyImmediate(holder); Object.DestroyImmediate(trunkObject); TerrainTreeObstacleRegistry.Invalidate();
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            }
        }

        public static void VerifyPlayerPoseScaling()
        {
            var source = new[] {
                new DinosaurCoreBody.Band { layer = 3102, part = 7,
                    bounds = new Bounds(new Vector3(1f, 1.25f, -2f), new Vector3(2f, 1.5f, 3f)) },
                new DinosaurCoreBody.Band { layer = 4102, part = 8,
                    bounds = new Bounds(new Vector3(-1f, 1.25f, 2f), new Vector3(2f, 1.5f, 3f)) }
            };
            var scaled = DinosaurSizeAdjustment.ScalePlayerBands(source);
            Require(scaled.Length == source.Length, "Player scaling duplicated height-spanning bounds.");
            for (int i = 0; i < source.Length; i++)
            {
                Require(scaled[i].layer == source[i].layer && scaled[i].part == source[i].part,
                    "Player scaling lost bone/group identity.");
                Require(scaled[i].bounds.center == source[i].bounds.center * DinosaurSizeAdjustment.Factor
                    && scaled[i].bounds.size == source[i].bounds.size * DinosaurSizeAdjustment.Factor,
                    "Player bounds were not scaled exactly once.");
            }
            Require(source[0].bounds.size.y == 1.5f, "Player scaling modified its source data.");
            Require(DinosaurSizeAdjustment.ScalePlayerBands(Array.Empty<DinosaurCoreBody.Band>()).Length == 0,
                "Empty player cache changed.");
            Debug.Log("PASS PLAYER_POSE_SCALING");
        }

        private static void Place(SceneTreeObstacle trunk, Vector2 point)
        {
            trunk.transform.position = new Vector3(point.x, 0, point.y); trunk.SetRadius(.05f);
            trunk.trunkSections = new[] { new TreeTrunkSection { center = Vector2.zero, radius = .05f, bottom = 0f, top = 10f } };
            TerrainTreeObstacleRegistry.Invalidate();
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
