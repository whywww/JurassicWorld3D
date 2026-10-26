using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmotivUnityPlugin;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class EmotivSensitivityAcceptance
    {
        private static int checks;

        private sealed class FakeCortex : CortexClient
        {
            public readonly List<JObject> Requests = new List<JObject>();
            public override void SendTextMessage(JObject param, string method, bool hasParam = true)
                => Requests.Add(JObject.Parse(PrepareRequest(method, param, hasParam)));

            public void Respond(int index, JToken result)
                => OnMessageReceived(new JObject { ["id"] = Requests[index]["id"], ["result"] = result }.ToString());

            public void Reject(int index)
                => OnMessageReceived(new JObject
                {
                    ["id"] = Requests[index]["id"],
                    ["error"] = new JObject { ["code"] = -32602, ["message"] = "test rejection" }
                }.ToString());
        }

        [MenuItem("Jurassic World/Tests/Emotiv Sensitivity Acceptance")]
        public static void Run()
        {
            try
            {
                Debug.Log("[EmotivSensitivityAcceptance] PASS: " + Verify() + " checks. No Cortex connection opened.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        public static int Verify()
        {
            checks = 0;
            var settings = ScriptableObject.CreateInstance<EmotivControlSettings>();
            try
            {
                foreach (string command in new[] { "push", "pull", "left", "right" })
                    Require(!settings.GetSensitivity(command).useProjectValue && settings.GetSensitivity(command).value == 5,
                        "New settings inherit profile: " + command);
                settings.pushSensitivity.value = -5;
                settings.pullSensitivity.value = 50;
                settings.Validate();
                Require(settings.pushSensitivity.value == 1 && settings.pullSensitivity.value == 10, "Sensitivity range clamped");
                settings.pushSensitivity.value = 9;
                settings.pushSensitivity.useProjectValue = true;
                foreach (var order in Permutations(new[] { "push", "pull", "left", "right" }))
                {
                    var original = new[] { 2, 3, 4, 5 };
                    var desired = settings.BuildSensitivityValues(order, original);
                    for (int i = 0; i < 4; i++)
                        Require(desired[i] == (order[i] == "push" ? 9 : original[i]), "Action order respected");
                    Require(original.SequenceEqual(new[] { 2, 3, 4, 5 }), "Source sensitivity array preserved");
                }
                Require(settings.BuildSensitivityValues(new[] { "neutral", "rotateLeft", "push" }, new[] { 1, 2, 3, 4 })
                    .SequenceEqual(new[] { 1, 9, 3, 4 }), "Neutral excluded; other actions and unused slots preserved");
                Require(settings.BuildSensitivityValues(new[] { "left" }, new[] { 1, 2, 3, 4 })
                    .SequenceEqual(new[] { 1, 2, 3, 4 }), "Inactive push is not activated or changed");

                VerifyRequests(settings);
                VerifyFailures(settings);
                VerifyLegacyRouting();
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
            return checks;
        }

        private static void VerifyRequests(EmotivControlSettings settings)
        {
            var client = new FakeCortex();
            var controller = new EmotivSensitivityController();
            int legacyReplies = 0;
            client.SetMentalCommandActionSensitivityOK += (sender, success) => legacyReplies++;
            client.GetMentalCommandActionSensitivityOK += (sender, values) => legacyReplies++;
            settings.pushSensitivity.useProjectValue = false;
            controller.Begin(client, "dummy-token", "session-A", settings, 0);
            Require(client.Requests.Count == 1 && (string)client.Requests[0]["method"] == "mentalCommandActiveAction",
                "Query active actions first");
            // Exercise the socket-to-main-thread boundary, not only the pure mapping helper.
            Task.Run(() => client.Respond(0, new JArray("right", "push"))).GetAwaiter().GetResult();
            Require(client.Requests.Count == 1, "Socket reply must wait for main-thread Tick");
            controller.Tick(0.1);
            client.Respond(1, new JArray(3, 6, 7, 8));
            controller.Tick(0.2);
            Require(client.Requests.Count == 2, "All-inherit never sends a set request");
            Require(controller.DescribeCommand("push").Contains("6"), "Display actual inherited profile sensitivity");
            Require(controller.DescribeCommand("left").Contains("未激活"), "Inactive command displayed accurately");

            settings.pushSensitivity.useProjectValue = true;
            controller.Begin(client, "dummy-token", "session-B", settings, 1);
            client.Respond(2, new JArray("right", "push"));
            controller.Tick(1.1);
            client.Respond(3, new JArray(3, 6, 7, 8));
            controller.Tick(1.2);
            Require(client.Requests.Count == 5, "Reconnect applies project override");
            var set = (JObject)client.Requests[4]["params"];
            Require((string)set["status"] == "set" && (string)set["session"] == "session-B"
                && set["profile"] == null, "Set addresses session only");
            Require(set["values"].Values<int>().SequenceEqual(new[] { 3, 9, 7, 8 }), "Only requested command is replaced");
            client.Respond(4, "success");
            controller.Tick(1.3);
            Require(controller.Status.Contains("已应用") && controller.DescribeCommand("push").Contains("9"), "Success updates status");
            Require(legacyReplies == 0 && client.Requests.All(request => (string)request["method"] != "setupProfile"),
                "Session replies bypass legacy auto-save path");
            foreach (var request in client.Requests)
                Require(request["params"]["profile"] == null && request["params"]["session"] != null,
                    "All new requests use only session");
            controller.Tick(100);
            Require(client.Requests.Count == 5, "Completed configuration never repeats");

            controller.Begin(client, "dummy-token", "old-session", settings, 200);
            controller.Begin(client, "dummy-token", "new-session", settings, 201);
            client.Respond(5, new JArray("push"));
            controller.Tick(201.1);
            Require(client.Requests.Count == 7, "Old-session replies ignored after reconnect");
            client.Respond(6, new JArray("push"));
            controller.Tick(201.2);
            controller.Reset();
            client.Respond(7, new JArray(5, 5, 5, 5));
            controller.Tick(201.3);
            Require(client.Requests.Count == 8 && controller.SessionId == null, "Stopped input cannot apply late replies");
        }

        private static void VerifyFailures(EmotivControlSettings settings)
        {
            for (int failureStage = 0; failureStage < 3; failureStage++)
            {
                var client = new FakeCortex();
                var controller = new EmotivSensitivityController();
                int warnings = 0;
                controller.Failed += message => warnings++;
                controller.Begin(client, "dummy-token", "failure-session", settings, 0);
                if (failureStage > 0)
                {
                    client.Respond(0, new JArray("push"));
                    controller.Tick(0.1);
                }
                if (failureStage > 1)
                {
                    client.Respond(1, new JArray(5, 5, 5, 5));
                    controller.Tick(0.2);
                }
                client.Reject(failureStage);
                controller.Tick(0.3);
                controller.Tick(100);
                Require(warnings == 1 && client.Requests.Count == failureStage + 1,
                    "API failure logs once without retries, stage " + failureStage);
                Require(controller.Status.Contains("当前值"), "Failure retains Cortex current values");
            }
            var timeoutClient = new FakeCortex();
            var timeoutController = new EmotivSensitivityController();
            int timeoutWarnings = 0;
            timeoutController.Failed += message => timeoutWarnings++;
            timeoutController.Begin(timeoutClient, "dummy-token", "timeout", settings, 0);
            timeoutController.Tick(10);
            timeoutClient.Respond(0, new JArray("push"));
            timeoutController.Tick(20);
            Require(timeoutWarnings == 1 && timeoutClient.Requests.Count == 1, "Timeout ignores late replies and never retries");

            var malformed = new FakeCortex();
            var malformedController = new EmotivSensitivityController();
            malformedController.Begin(malformed, "dummy-token", "malformed", settings, 0);
            malformed.Respond(0, new JArray("push"));
            malformedController.Tick(0.1);
            malformed.Respond(1, new JArray(5, 5));
            malformedController.Tick(0.2);
            Require(malformed.Requests.Count == 2 && malformedController.Status.Contains("无法应用"), "Malformed values never sent back");
        }

        private static void VerifyLegacyRouting()
        {
            var client = new FakeCortex();
            int legacySets = 0;
            int sessionSets = 0;
            client.SetMentalCommandActionSensitivityOK += (sender, success) => legacySets++;
            client.MentalCommandActionSensitivity("dummy-token", "set", "legacy-session", "LegacyProfile", new List<int> { 5, 5, 5, 5 });
            client.SetSessionMentalCommandActionSensitivity("dummy-token", "project-session", new List<int> { 6, 6, 6, 6 },
                (result, error) => sessionSets++);
            client.Respond(1, "success");
            Require(sessionSets == 1 && legacySets == 0, "Session acknowledgement does not notify auto-save subscribers");
            client.Respond(0, "success");
            Require(legacySets == 1 && sessionSets == 1, "Concurrent legacy acknowledgement keeps original behavior");
        }

        private static IEnumerable<string[]> Permutations(string[] items)
        {
            if (items.Length == 0) { yield return Array.Empty<string>(); yield break; }
            for (int i = 0; i < items.Length; i++)
                foreach (var tail in Permutations(items.Where((item, index) => index != i).ToArray()))
                    yield return new[] { items[i] }.Concat(tail).ToArray();
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
