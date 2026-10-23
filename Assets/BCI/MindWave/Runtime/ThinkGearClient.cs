using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace JurassicWorld.BCI.MindWave
{
    public sealed class ThinkGearClient : IDisposable
    {
        [Serializable] private sealed class AuthorizationRequest { public string appName; public string appKey; }
        private readonly object gate = new object();
        private readonly ThinkGearStream stream;
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private readonly MindWaveSettings settings;
        private readonly string authorizationJson;
        private Thread worker;
        private TcpClient socket;
        private bool connected, disposed;
        private string status = "Not started";
        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        public ThinkGearClient(MindWaveSettings configuration)
        {
            configuration.Validate();
            settings = configuration;
            stream = new ThinkGearStream(configuration.sampleTimeout);
            authorizationJson = JsonUtility.ToJson(new AuthorizationRequest
                { appName = settings.appName, appKey = settings.appKey });
        }

        public void Start()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ThinkGearClient));
            if (worker != null) return;
            worker = new Thread(Run) { IsBackground = true, Name = "MindWave TCP" };
            worker.Start();
        }

        private void Run()
        {
            while (!stop.WaitOne(0))
            {
                TcpClient attempt = null;
                try
                {
                    lock (gate)
                    {
                        stream.Reset(); connected = false; status = "Connecting";
                        socket = attempt = new TcpClient();
                    }
                    var connect = attempt.ConnectAsync(settings.host, settings.Port);
                    while (!connect.Wait(100))
                        if (stop.WaitOne(0)) return;
                    if (stop.WaitOne(0)) return;
                    using (NetworkStream network = attempt.GetStream())
                    {
                        attempt.NoDelay = true;
                        bool configured = !settings.requestAuthorization;
                        Send(network, configured ? "{\"enableRawOutput\":false,\"format\":\"Json\"}" : authorizationJson);
                        lock (gate) { connected = true; status = configured ? "Waiting for data" : "Waiting for authorization"; }
                        byte[] buffer = new byte[8192];
                        while (!stop.WaitOne(0))
                        {
                            int count = network.Read(buffer, 0, buffer.Length);
                            if (count == 0) break;
                            bool sendConfiguration = false;
                            lock (gate)
                            {
                                stream.Feed(buffer, count, Now);
                                if (stream.Authorization == false) status = "Authorization denied";
                                else if (!configured && stream.Authorization == true)
                                {
                                    configured = true; sendConfiguration = true; status = "Waiting for data";
                                }
                                else if (configured) status = "Connected (see signal/sample age)";
                            }
                            if (sendConfiguration) Send(network, "{\"enableRawOutput\":false,\"format\":\"Json\"}");
                        }
                    }
                }
                catch (Exception error) when (error is SocketException || error is System.IO.IOException
                    || error is ObjectDisposedException || error is AggregateException || error is InvalidOperationException)
                {
                    lock (gate) status = "Connection error: " + error.GetType().Name;
                }
                finally
                {
                    attempt?.Close();
                    lock (gate)
                    {
                        socket = null; connected = false; stream.Reset();
                        if (!stop.WaitOne(0)) status += "; retry in 2s";
                    }
                }
                if (stop.WaitOne(2000)) break;
            }
        }

        private static void Send(NetworkStream network, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json + "\r");
            network.Write(bytes, 0, bytes.Length);
        }

        public Vector2 ReadMovement()
        {
            lock (gate)
                return connected && (!settings.requestAuthorization || stream.Authorization == true)
                    ? stream.ReadMovement(Now, settings.attentionThreshold, settings.sampleTimeout) : Vector2.zero;
        }

        public string Describe()
        {
            lock (gate)
                return status + "\nattention: " + stream.Attention + "    poorSignal: " + stream.Signal
                    + "\nattention age: " + stream.AttentionAge(Now).ToString("F1") + "s    signal age: "
                    + stream.SignalAge(Now).ToString("F1") + "s\ninvalid packets: " + stream.InvalidPackets;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stop.Set();
            lock (gate) { connected = false; socket?.Close(); }
            bool ended = worker == null || worker.Join(3000);
            lock (gate) { stream.Reset(); status = "Stopped"; }
            // Never dispose a wait handle that a still-unwinding background thread could access.
            if (ended) stop.Dispose();
        }
    }
}
