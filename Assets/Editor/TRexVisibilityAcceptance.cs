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
    public static class TRexVisibilityAcceptance
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static DinosaurAgent rex;
        private static Camera camera;
        private static void Set(string name, object value) => typeof(DinosaurAgent).GetField(name, Flags).SetValue(rex, value);
        private static object Get(string name) => typeof(DinosaurAgent).GetField(name, Flags).GetValue(rex);
        private static object Call(string name, params object[] args) => typeof(DinosaurAgent).GetMethod(name, Flags).Invoke(rex, args);
        private static bool Property(string name) => (bool)typeof(DinosaurAgent).GetProperty(name, Flags).GetValue(rex);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Player(float distance, float angle = 0f)
        {
            Vector3 forward = (Vector3)Call("GetVisualForward");
            camera.transform.position = rex.transform.position + Quaternion.Euler(0, angle, 0) * forward * distance + Vector3.up * 1.7f;
        }

        [MenuItem("Jurassic World/Acceptance/Verify Unified TRex Visibility")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorSceneManager.GetActiveScene().isDirty,
                "Stop Play and save the scene before acceptance.");
            string scene = EditorSceneManager.GetActiveScene().path;
            var report = new StringBuilder("# 霸王龙统一互动范围回归\n\n");
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
                rex = Object.FindObjectsOfType<DinosaurAgent>().Single(a => a.name == "Tyrannosaurus Rex");
                camera = new GameObject("Visibility acceptance camera").AddComponent<Camera>();
                Set("viewCamera", camera);
                // Isolate visibility thresholds from forest layout; hard occlusion is injected below.
                rex.transform.SetPositionAndRotation(new Vector3(0, 10000, 0), Quaternion.identity);
                var settings = Object.FindObjectOfType<DinosaurInteractionSettings>();
                Set("interactionSettings", settings);
                settings.solidObstacles = Array.Empty<Bounds>();
                foreach (bool active in new[] { false, true })
                {
                    Set("frontActive", active);
                    foreach (float angle in new[] { -50f, -20f, 0f, 20f, 50f })
                    { Player(32f, angle); Require((bool)Call("CanSeePlayer"), "Inclusive angle/range boundary " + angle); }
                    Player(32.02f); Require(rex.PlayerVisibilityStatus == "OutOfRange", "Range must not expand during interaction.");
                    foreach (float angle in new[] { -50.02f, 50.02f, 90f })
                    { Player(24f, angle); Require(rex.PlayerVisibilityStatus == "OutOfAngle", "Angle must not expand: " + angle); }
                }
                report.AppendLine("PASS：发现与互动均为32米、±50°；±20°和±50°边界包含在内，范围不随阶段扩大。\n");
                Player(10f); Require(Property("FrontPlayerNear"), "10m enters Near.");
                Player(12f); Require(!Property("FrontPlayerNear") && !Property("FrontPlayerBeyondResume"), "Near hysteresis.");
                Player(15f); Require(!Property("FrontPlayerBeyondResume"), "15m does not resume.");
                Player(15.02f); Require(Property("FrontPlayerBeyondResume"), "Above15m resumes.");
                Player(9f, 60f); Require(!Property("FrontPlayerNear"), "Invisible player must not trigger Near.");
                report.AppendLine("PASS：10米Near、10～15米回差、>15米恢复条件；不可见玩家不能触发Near。\n");
                Player(24f);
                settings.solidObstacles = new[] { new Bounds((rex.transform.position + camera.transform.position) * .5f + Vector3.up, Vector3.one * 5f) };
                Require(rex.PlayerVisibilityStatus == "RockOcclusion", "Registered rock must occlude.");
                settings.solidObstacles = Array.Empty<Bounds>();
                var soft = GameObject.CreatePrimitive(PrimitiveType.Cube); soft.name = "Grass fern collider control";
                soft.transform.position = (rex.transform.position + camera.transform.position) * .5f + Vector3.up;
                soft.transform.localScale = Vector3.one * 10f;
                Require((bool)Call("CanSeePlayer"), "Unregistered soft collider must not occlude.");
                Object.DestroyImmediate(soft);
                var trunk = new GameObject("Visibility hard trunk control").AddComponent<SceneTreeObstacle>();
                trunk.transform.position = (rex.transform.position + camera.transform.position) * .5f;
                trunk.trunkSections = new[] { new TreeTrunkSection { radius = 1f, bottom = 9999f, top = 10010f } };
                TerrainTreeObstacleRegistry.Invalidate();
                Require(rex.PlayerVisibilityStatus == "TreeOcclusion", "Registered trunk must occlude.");
                Object.DestroyImmediate(trunk.gameObject); TerrainTreeObstacleRegistry.Invalidate();
                report.AppendLine("PASS：已登记岩石遮挡；未登记的草蕨碰撞体不影响视线（真实植被分类另见既有审计）。\n");
                var stageType = typeof(DinosaurAgent).GetField("frontStage", Flags).FieldType;
                foreach (int fps in new[] { 30, 60, 144 })
                foreach (string stage in new[] { "Intro", "Follow", "Near" })
                foreach (bool waiting in new[] { false, true })
                {
                    Set("frontStage", Enum.Parse(stageType, stage)); Set("frontWaiting", waiting);
                    Set("frontExitPending", false); Set("invisibleTime", 0f); Set("frontEndReason", null);
                    Player(33f);
                    for (int i = 0; i < fps * 2; i++) Call("UpdateFrontVisibility", 1f / fps);
                    Require(!(bool)Get("frontExitPending"), "Brief loss must not end interaction.");
                    Vector3 lastSeen = (Vector3)Get("lastSeenPlayer");
                    Require(lastSeen != camera.transform.position, "Invisible player must not update target.");
                    Player(24f); Call("UpdateFrontVisibility", 1f / fps);
                    Require(rex.PlayerLostSightTime == 0f, "Visibility recovery resets timer.");
                    Require((Vector3)Get("lastSeenPlayer") == camera.transform.position, "Visible target refreshed.");
                    Player(24f, 60f);
                    for (int i = 0; i < fps * 3 - 1; i++) Call("UpdateFrontVisibility", 1f / fps);
                    Require(!(bool)Get("frontExitPending"), "Loss ended before3seconds.");
                    for (int i = 0; i < 2; i++) Call("UpdateFrontVisibility", 1f / fps);
                    if (stage == "Intro")
                    {
                        Require(!(bool)Get("frontExitPending"), "Opening performance must finish; follow handles sight loss.");
                        report.AppendLine($"PASS：{fps} FPS模拟，Intro：短暂丢失恢复、目标冻结；开场表演不因视线丢失中止。");
                        continue;
                    }
                    Require((bool)Get("frontExitPending") && rex.FrontEndReason == "OutOfAngle", "Loss reason or timeout incorrect.");
                    report.AppendLine($"PASS：{fps} FPS模拟，{stage}，等待={waiting}：短暂丢失恢复、目标冻结、3秒结束（≤1帧误差）。");
                }
                // Follow timeout is cumulative across Near/waiting/turning and never resets on resume.
                foreach (int fps in new[] { 30, 60, 144 })
                foreach (string stage in new[] { "Follow", "Near" })
                foreach (bool waiting in new[] { false, true })
                {
                    Set("frontStage", Enum.Parse(stageType, stage)); Set("frontWaiting", waiting);
                    Set("frontExitPending", false); Set("frontEndReason", null);
                    Set("frontFollowElapsed", 10f); Set("frontTimingFollow", true);
                    for (int i = 0; i < fps * 5; i++) Call("TickFrontFollowClock", 1f / fps);
                    Require(!(bool)Get("frontExitPending"), "Follow timeout fired early during " + stage + ".");
                    for (int i = 0; i < fps * 4; i++) Call("TickFrontFollowClock", 1f / fps);
                    Require(!(bool)Get("frontExitPending") && Mathf.Abs((float)Get("frontFollowElapsed") - 19f) < .05f, "Follow time reset or drifted.");
                    for (int i = 0; i < fps * 11 + 2; i++) Call("TickFrontFollowClock", 1f / fps);
                    Require((bool)Get("frontExitPending") && rex.FrontEndReason == "FollowTimeout", "Cumulative timeout missing at " + fps + " FPS / " + stage);
                    report.AppendLine($"PASS：{fps} FPS，{stage}，等待={waiting}：中断/等待不重置跟随计时，累计30秒后结束。");
                }
                Set("frontStage", Enum.Parse(stageType, "Follow")); Set("frontWaiting", false);
                foreach (string stage in new[] { "Intro", "TurnBack", "Ending", "ReturnWalk" })
                {
                    Set("frontStage", Enum.Parse(stageType, stage)); Set("frontExitPending", false); Set("frontEndReason", null);
                    Set("frontFollowElapsed", 0f); Set("frontTimingFollow", false);
                    for (int i = 0; i < 60; i++) Call("TickFrontFollowClock", 1f);
                    Require(!(bool)Get("frontExitPending") && (float)Get("frontFollowElapsed") == 0f, "Non-follow stage counted toward timeout: " + stage);
                }
                report.AppendLine("PASS：Intro、Ending、TurnBack、ReturnWalk 不计跟随超时。");
                report.AppendLine("\n计时测试直接调用生产可见更新函数；全流程运动、安全和路线使用既有Play回归单独验证。场景未保存。\n");
                Directory.CreateDirectory(RainforestSample.Output);
                File.WriteAllText(RainforestSample.Output + "/UnifiedVisibility.md", report.ToString());
                Debug.Log("PASS TREX_UNIFIED_VISIBILITY_OK");
            }
            finally
            {
                EditorSceneManager.OpenScene(string.IsNullOrEmpty(scene) ? "Assets/Scenes/Main.unity" : scene);
            }
        }
    }
}
