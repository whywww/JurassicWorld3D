using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace JurassicWorld.BCI.BrainLink
{
    public sealed class BrainLinkClient : IDisposable
    {
        private readonly object gate = new object();
        private readonly BrainLinkStream stream = new BrainLinkStream();
        private readonly BrainLinkSettings settings;
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private Thread worker;
        private TcpClient socket;
        private bool connected, disposed;
        private string status = "Not started";
        public bool IsWorkerAlive => worker != null && worker.IsAlive;
        public double SampleAge { get { lock (gate) return stream.Age(Now); } }
        public int Signal { get { lock (gate) return stream.Signal; } }
        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        public BrainLinkClient(BrainLinkSettings configuration)
        {
            configuration.Validate();
            settings = configuration;
        }

        public void Start()
        {
            if (disposed) throw new ObjectDisposedException(nameof(BrainLinkClient));
            if (worker != null) return;
            worker = new Thread(Run) { IsBackground = true, Name = "BrainLink TCP" };
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
                    var connection = attempt.ConnectAsync(settings.host, settings.port);
                    while (!connection.Wait(100)) if (stop.WaitOne(0)) return;
                    if (stop.WaitOne(0)) return;
                    attempt.NoDelay = true;
                    using (var network = attempt.GetStream())
                    {
                        lock (gate) { connected = true; status = "Connected; waiting for new SDK callback"; }
                        var buffer = new byte[4096];
                        while (!stop.WaitOne(0))
                        {
                            int count = network.Read(buffer, 0, buffer.Length);
                            if (count == 0) break;
                            lock (gate) stream.Feed(buffer, count, Now);
                        }
                    }
                }
                catch (Exception error) when (error is SocketException || error is IOException
                    || error is ObjectDisposedException || error is AggregateException || error is InvalidOperationException)
                { lock (gate) status = "Connection error: " + error.GetType().Name; }
                finally
                {
                    attempt?.Close();
                    lock (gate) { socket = null; connected = false; stream.Reset(); status = "Disconnected; retry in 2s"; }
                }
                if (stop.WaitOne(2000)) break;
            }
        }

        public Vector2 ReadMovement()
        {
            lock (gate) return connected ? stream.ReadMovement(Now, settings.attentionThreshold, settings.sampleTimeout) : Vector2.zero;
        }

        public string Describe()
        {
            lock (gate) return status + "\nmode: " + stream.Mode + "   attention: " + stream.Attention
                + "   signal: " + stream.Signal + "\nsample age: " + stream.Age(Now).ToString("F1")
                + "s   invalid packets: " + stream.InvalidPackets;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; stop.Set();
            lock (gate) { connected = false; socket?.Close(); }
            bool ended = worker == null || worker.Join(3000);
            lock (gate) { stream.Reset(); status = "Stopped"; }
            if (ended) stop.Dispose();
        }
    }
}
