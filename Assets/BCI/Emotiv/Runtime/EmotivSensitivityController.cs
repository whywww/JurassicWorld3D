using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EmotivUnityPlugin;
using Newtonsoft.Json.Linq;

namespace JurassicWorld
{
    // Socket replies are queued; all configuration and status changes happen in Tick on Unity's thread.
    public sealed class EmotivSensitivityController
    {
        private readonly ConcurrentQueue<Action> replies = new ConcurrentQueue<Action>();
        private int generation;
        private bool waiting;
        private double now;
        private double deadline;
        private List<string> actions;
        private List<int> values;

        public string SessionId { get; private set; }
        public string Status { get; private set; } = "等待连接；未覆盖的 command 沿用 profile。";
        public event Action<string> Failed;

        public void Reset()
        {
            generation++;
            waiting = false;
            SessionId = null;
            actions = null;
            values = null;
            while (replies.TryDequeue(out _)) { }
            Status = "等待连接；未覆盖的 command 沿用 profile。";
        }

        public void Begin(CortexClient client, string token, string sessionId, EmotivControlSettings settings, double time)
        {
            Reset();
            SessionId = sessionId;
            now = time;
            waiting = true;
            deadline = now + 10;
            Status = "正在读取当前 session 的激活动作及 profile 敏感度……";
            try
            {
                client.GetSessionMentalCommandActiveActions(token, sessionId, Reply(activeResult =>
                {
                    if (!(activeResult is JArray active)) throw new ArgumentException("Invalid actions reply.");
                    actions = active.ToObject<List<string>>();
                    deadline = now + 10;
                    client.GetSessionMentalCommandActionSensitivity(token, sessionId, Reply(sensitivityResult =>
                    {
                        if (!(sensitivityResult is JArray current)) throw new ArgumentException("Invalid sensitivity reply.");
                        var currentValues = current.ToObject<List<int>>();
                        var desired = settings.BuildSensitivityValues(actions, currentValues);
                        values = currentValues;
                        bool changed = false;
                        for (int i = 0; i < 4; i++) changed |= desired[i] != currentValues[i];
                        if (!changed)
                        {
                            waiting = false;
                            Status = "已读取 profile 敏感度；当前激活动作无需覆盖。";
                            return;
                        }
                        Status = "正在应用项目敏感度……";
                        deadline = now + 10;
                        client.SetSessionMentalCommandActionSensitivity(token, sessionId, desired, Reply(result =>
                        {
                            if (result?.Type != JTokenType.String || (string)result != "success")
                                throw new ArgumentException("Unexpected sensitivity acknowledgement.");
                            values = desired;
                            waiting = false;
                            Status = "项目敏感度已应用到当前 session；未激活的 command 未改动。";
                        }));
                    }));
                }));
            }
            catch (Exception error) { Fail("敏感度请求无法发送（" + error.GetType().Name + "）"); }
        }

        private Action<JToken, ErrorMsgEventArgs> Reply(Action<JToken> handle)
        {
            int requestGeneration = generation;
            return (result, error) => replies.Enqueue(() =>
            {
                if (generation != requestGeneration || !waiting) return;
                if (error != null)
                {
                    Fail("敏感度配置失败：" + error.MethodName + "，错误码 " + error.Code);
                    return;
                }
                try { handle(result); }
                catch (Exception exception) { Fail("敏感度响应无法应用（" + exception.GetType().Name + "）"); }
            });
        }

        public void Tick(double time)
        {
            now = time;
            while (replies.TryDequeue(out var reply)) reply();
            if (waiting && now >= deadline) Fail("敏感度请求超时；本次连接不再重试");
        }

        public string DescribeCommand(string command)
        {
            if (actions == null || values == null)
                return SessionId == null ? "沿用 profile 的值将在连接后读取" : "当前值尚未确认";
            int index = 0;
            foreach (string action in actions)
            {
                if (action == "neutral") continue;
                if (action == command && index < values.Count) return "当前 session：" + values[index];
                index++;
            }
            return "当前 profile 未激活此 command";
        }

        private void Fail(string message)
        {
            waiting = false;
            values = null;
            Status = message + "。脑控继续使用 Cortex 当前值。";
            Failed?.Invoke(Status);
        }
    }
}
