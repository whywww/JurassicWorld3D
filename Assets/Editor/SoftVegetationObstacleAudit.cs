using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace JurassicWorld.Editor
{
    public static class SoftVegetationObstacleAudit
    {
        private static bool Soft(string text)
        { text = text.ToLowerInvariant(); return text.Contains("fern") || text.Contains("grass") || text.Contains("understory") || text.Contains("bush"); }
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and stop Play before auditing obstacles.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var report = new StringBuilder("# Soft vegetation obstacle audit\n\n");
            var markers = Object.FindObjectsOfType<SceneTreeObstacle>();
            foreach (var marker in markers)
                report.AppendLine($"MARKER {marker.name}: p={marker.transform.position:F3}, sections={marker.trunkSections.Length}, prefab={PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(marker)}, soft={Soft(marker.name + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(marker))}");
            var terrain = Terrain.activeTerrain;
            for (int i = 0; i < terrain.terrainData.treePrototypes.Length; i++)
            {
                var prefab = terrain.terrainData.treePrototypes[i].prefab;
                report.AppendLine($"PROTOTYPE {i}: {prefab.name}, path={AssetDatabase.GetAssetPath(prefab)}, soft={Soft(prefab.name)}, instances={terrain.terrainData.treeInstances.Count(t => t.prototypeIndex == i && t.widthScale > 0)}");
            }
            var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
            report.AppendLine($"SOLIDS {settings.solidObstacles.Length}; navRadius={settings.navigationRadius}");
            var agent = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
            var profile = (DinosaurInteractionProfile)new SerializedObject(agent).FindProperty("interactionProfile").objectReferenceValue;
            var turn = profile.Find(InteractionMotionRole.Turn180Look);
            var probeObject = new GameObject("Audit turn body"); var body = probeObject.AddComponent<DinosaurCoreBody>();
            body.bands = (DinosaurCoreBody.Band[])turn.bodyBands.Clone();
            for (int i = 0; i < body.bands.Length; i++) { var b = body.bands[i].bounds; b.Expand(new Vector3(.6f, 0, .6f)); body.bands[i].bounds = b; }
            var instance = NavMesh.AddNavMeshData(settings.navigationData);
            try
            {
            var filter = new NavMeshQueryFilter { agentTypeID = settings.navigationAgentTypeId, areaMask = 1 };
            TerrainTreeObstacleRegistry.Invalidate();
            var layout = Object.FindObjectOfType<RainforestSampleLayout>();
            for (int site = 0; site < layout.clearingPositions.Length; site++)
            {
                Vector3 origin = layout.clearingPositions[site]; origin.y = agent.MovementHeight;
                Quaternion rotation = layout.clearingRotations[site];
                foreach (var role in new[] { InteractionMotionRole.Investigate, InteractionMotionRole.InvestigateOut, InteractionMotionRole.BigRoarStep })
                {
                    var motion = profile.Find(role); var end = DinosaurAgent.SampleFootFrame(motion, motion.clip.length);
                    origin += rotation * Quaternion.Euler(0, agent.VisualYaw, 0) * new Vector3(end.rootOffset.x, 0, end.rootOffset.y);
                    rotation = Quaternion.Euler(0, end.yaw, 0) * rotation;
                }
                report.AppendLine($"SITE {site} after Intro: {origin:F4}"); Vector3 previous = origin;
                for (int i = 0; i < 6; i++)
                {
                    var frame = DinosaurAgent.SampleFootFrame(turn, turn.clip.length * i / 5f);
                    Vector3 p = origin + rotation * Quaternion.Euler(0, agent.VisualYaw, 0) * new Vector3(frame.rootOffset.x, 0, frame.rootOffset.y); p.y = agent.MovementHeight;
                    Vector3 f = Quaternion.Euler(0, frame.yaw, 0) * rotation * Quaternion.Euler(0, agent.VisualYaw, 0) * Vector3.forward;
                    string reason = settings.PoseBlockReason(new Vector2(p.x, p.z), new Vector2(f.x, f.z), body);
                    bool nav = NavMesh.SamplePosition(p, out var hit, .25f, filter);
                    report.AppendLine($"TURN {i}: p={p:F4}, hard={reason}, nav={nav}, offset={(nav ? Vector3.Distance(p,hit.position) : -1):F4}, ray={NavMesh.Raycast(previous,p,out _,filter)}");
                    if (reason == "Tree") foreach (var marker in markers)
                        foreach (var band in marker.trunkSections)
                        {
                            var section = band; section.center += new Vector2(marker.transform.position.x, marker.transform.position.z);
                            if (body.Intersects(new Vector2(p.x,p.z), new Vector2(f.x,f.z), section))
                            { report.AppendLine($"BLOCKER {marker.name} at {marker.transform.position:F3}; wood section center={section.center}, radius={section.radius}, height={section.bottom}/{section.top}"); break; }
                        }
                    previous = p;
                }
                var bands = new Dictionary<int, Bounds>();
                foreach (var role in new[] { InteractionMotionRole.BigRoarStep, InteractionMotionRole.FollowWalk })
                    foreach (var band in profile.Find(role).bodyBands)
                    { if (bands.TryGetValue(band.layer, out var b)) { b.Encapsulate(band.bounds); bands[band.layer] = b; } else bands[band.layer] = band.bounds; }
                var turnBands = body.bands;
                body.bands = bands.Select(b => new DinosaurCoreBody.Band { layer=b.Key, bounds=b.Value }).ToArray();
                Vector3 facing = layout.clearingRotations[site] * Quaternion.Euler(0,agent.VisualYaw,0) * Vector3.forward;
                Vector3 player = layout.clearingPositions[site] + facing * 24f;
                Vector3 away = origin-player; away.y=0;
                Vector3 goal = player + away.normalized*10f;
                var path = new NavMeshPath();
                if (NavMesh.SamplePosition(origin,out var start,1f,filter) && NavMesh.SamplePosition(goal,out var navEnd,12f,filter)
                    && NavMesh.CalculatePath(start.position,navEnd.position,filter,path))
                {
                    report.AppendLine($"FOLLOW PATH status={path.status}, requested={goal:F3}, selected={navEnd.position:F3}, corners={path.corners.Length}");
                    Vector3 at=origin, heading=facing;
                    foreach (Vector3 corner in path.corners)
                    {
                        Vector3 direction=corner-at; direction.y=0; if(direction.magnitude<.08f) continue; direction.Normalize();
                        int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(at,corner)/.1f));
                        bool failed=false;
                        for(int k=0;k<=count;k++)
                        {
                            Vector3 point=Vector3.Lerp(at,corner,k/(float)count);
                            string why=settings.PoseBlockReason(new Vector2(point.x,point.z),new Vector2(direction.x,direction.z),body);
                            if(why==null) continue;
                            report.AppendLine($"FOLLOW BLOCK {why} at {point:F3}");
                            foreach(var marker in markers) foreach(var section0 in marker.trunkSections)
                            {
                                var section=section0;section.center+=new Vector2(marker.transform.position.x,marker.transform.position.z);
                                if(body.Intersects(new Vector2(point.x,point.z),new Vector2(direction.x,direction.z),section))
                                {report.AppendLine($"FOLLOW BLOCKER {marker.name} at {marker.transform.position:F3}, soft={Soft(marker.name)}");break;}
                            }
                            failed=true;break;
                        }
                        if(failed)break;at=corner;heading=direction;
                    }
                }
                body.bands=turnBands;
                Vector3 visual=rotation*Quaternion.Euler(0,agent.VisualYaw,0)*Vector3.forward;
                Vector2 originXZ=new Vector2(origin.x,origin.z), visualXZ=new Vector2(visual.x,visual.z);
                string clearBefore=settings.PoseBlockReason(originXZ,visualXZ,body);
                string turnBefore=TurnSignature(settings,agent,body,turn,origin,rotation,filter);
                var fern=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Vegetation/Fern02/Prepared/Prefabs/Fern02_A.prefab"),origin,Quaternion.identity);
                fern.transform.SetParent(probeObject.transform,true);
                fern.name="Audit soft fern";fern.AddComponent<BoxCollider>().size=new Vector3(10,10,10);
                TerrainTreeObstacleRegistry.Invalidate();
                string clearWithFern=settings.PoseBlockReason(originXZ,visualXZ,body);
                if(clearWithFern!=clearBefore)throw new InvalidOperationException("Soft fern changed hard-obstacle result.");
                if(TurnSignature(settings,agent,body,turn,origin,rotation,filter)!=turnBefore)throw new InvalidOperationException("Fern changed full turn prediction.");
                Object.DestroyImmediate(fern);
                var grass=new GameObject("Audit soft grass collider");grass.transform.position=origin;
                grass.transform.SetParent(probeObject.transform,true);
                grass.AddComponent<BoxCollider>().size=Vector3.one*10;
                TerrainTreeObstacleRegistry.Invalidate();
                string clearWithGrass=settings.PoseBlockReason(originXZ,visualXZ,body);
                if(clearWithGrass!=clearBefore)throw new InvalidOperationException("Soft grass collider changed hard-obstacle result.");
                if(TurnSignature(settings,agent,body,turn,origin,rotation,filter)!=turnBefore)throw new InvalidOperationException("Grass changed full turn prediction.");
                Object.DestroyImmediate(grass);
                var testBand=body.bands.First(b=>b.bounds.center.y>1f).bounds;
                var hard=new GameObject("Audit real hard trunk");
                hard.transform.SetParent(probeObject.transform,true);
                hard.transform.position=origin+rotation*Quaternion.Euler(0,agent.VisualYaw,0)*new Vector3(testBand.center.x,0,testBand.center.z);
                var hardMarker=hard.AddComponent<SceneTreeObstacle>();hardMarker.SetRadius(.3f);
                hardMarker.trunkSections=new[]{new TreeTrunkSection{center=Vector2.zero,radius=.3f,bottom=0,top=32}};
                TerrainTreeObstacleRegistry.Invalidate();
                string hardResult=settings.PoseBlockReason(originXZ,visualXZ,body);
                if(clearBefore==null && hardResult!="Tree")throw new InvalidOperationException("Real hard trunk was ignored.");
                Object.DestroyImmediate(hard);TerrainTreeObstacleRegistry.Invalidate();
                report.AppendLine($"PAIRED TEST site={site}: baseline={clearBefore ?? "clear"}; fern with overlapping collider={clearWithFern ?? "clear"}; grass collider={clearWithGrass ?? "clear"}; hard trunk={hardResult}; PASS");
            }
            File.WriteAllText("Logs/soft-vegetation-audit.txt", report.ToString());
            }
            finally
            {
                instance.Remove(); Object.DestroyImmediate(probeObject); TerrainTreeObstacleRegistry.Invalidate();
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        private static string TurnSignature(DinosaurInteractionSettings settings,DinosaurAgent agent,DinosaurCoreBody body,
            DinosaurInteractionProfile.Motion turn,Vector3 origin,Quaternion rotation,NavMeshQueryFilter filter)
        {
            var result=new StringBuilder();Vector3 previous=origin;
            for(int i=0;i<6;i++)
            {
                var sample=DinosaurAgent.SampleFootFrame(turn,turn.clip.length*i/5f);
                Vector3 p=origin+rotation*Quaternion.Euler(0,agent.VisualYaw,0)*new Vector3(sample.rootOffset.x,0,sample.rootOffset.y);p.y=agent.MovementHeight;
                Vector3 forward=Quaternion.Euler(0,sample.yaw,0)*rotation*Quaternion.Euler(0,agent.VisualYaw,0)*Vector3.forward;
                result.Append(settings.PoseBlockReason(new Vector2(p.x,p.z),new Vector2(forward.x,forward.z),body)??"clear");
                bool nav=NavMesh.SamplePosition(p,out var hit,.25f,filter);
                result.Append($":{nav}:{(nav?Vector3.Distance(p,hit.position):-1):F4}:{NavMesh.Raycast(previous,p,out _,filter)}|");previous=p;
            }
            return result.ToString();
        }
    }
}
