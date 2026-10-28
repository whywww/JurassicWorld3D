param(
    [ValidateSet('Tcp','Serial')][string]$Mode = 'Tcp',
    [int]$Port = 13855,
    [string]$SerialPort = 'COM20',
    [int]$Attention = 0,
    [int]$Signal = 0,
    [switch]$Auto,
    [switch]$DenyAuthorization,
    [int]$DurationSeconds = 0
)
$ErrorActionPreference = 'Stop'
if (-not ('MindWaveSimulator' -as [type])) {
$simulatorCode = @'
using System;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Threading;

public static class MindWaveSimulator
{
    public static byte[] Packet(int attention, int signal)
    {
        // TGAM's 1Hz summary: signal, eSense and eight 24-bit band powers.
        // All values here are synthetic; raw samples do not calculate the attention value.
        byte[] payload = new byte[32];
        payload[0] = 2; payload[1] = (byte)signal;
        payload[2] = 4; payload[3] = (byte)attention;
        payload[4] = 5; payload[5] = 50;
        payload[6] = 0x83; payload[7] = 24;
        for (int band = 0; band < 8; band++)
        {
            int power = 1000 + band * 100;
            payload[8 + band * 3] = (byte)(power >> 16);
            payload[9 + band * 3] = (byte)(power >> 8);
            payload[10 + band * 3] = (byte)power;
        }
        return Frame(payload);
    }

    public static byte[] RawPacket(short sample)
    { return Frame(new byte[] { 0x80, 2, (byte)(sample >> 8), (byte)sample }); }

    private static byte[] Frame(byte[] payload)
    {
        byte[] packet = new byte[payload.Length + 4];
        packet[0] = packet[1] = 0xAA; packet[2] = (byte)payload.Length;
        int sum = 0;
        for (int i = 0; i < payload.Length; i++) { packet[i + 3] = payload[i]; sum += payload[i]; }
        packet[packet.Length - 1] = (byte)(~sum & 255);
        return packet;
    }

    public static void Run(string mode, int port, string serialName, int attention, int signal,
        bool auto, bool deny, int duration)
    {
        if (attention < 0 || attention > 100 || signal < 0 || signal > 200)
            throw new ArgumentException("Attention: 0..100; signal: 0..200.");
        TcpListener listener = null;
        TcpClient client = null;
        SerialPort serial = null;
        StringBuilder commands = new StringBuilder(), incoming = new StringBuilder();
        bool paused = false, configured = false, rejected = false, quit = false;
        Stopwatch clock = Stopwatch.StartNew();
        double next = 0;
        double nextRaw = 0;
        int previousStage = -1;
        try
        {
            if (mode == "Serial")
            {
                serial = new SerialPort(serialName, 57600, Parity.None, 8, StopBits.One);
                serial.WriteTimeout = 1000;
                serial.Open();
                Console.WriteLine("Binary ThinkGear sender -> " + serialName + " @57600. Not a Bluetooth emulator.");
                Console.WriteLine("Synthetic raw EEG: 512 packets/s; attention/signal/band powers: 1 packet/s. Allow about 20 seconds for legacy TGC identification.");
            }
            else
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                Console.WriteLine("TGSP simulator listening on 127.0.0.1:" + port);
            }
            Console.WriteLine("Commands + Enter: a 70 | s 200 | pause | resume | drop | auto | manual | quit");
            while (!quit && (duration <= 0 || clock.Elapsed.TotalSeconds < duration))
            {
                if (listener != null && listener.Pending())
                {
                    if (client != null) client.Close();
                    client = listener.AcceptTcpClient(); client.NoDelay = true;
                    configured = rejected = false; incoming.Clear();
                    Console.WriteLine("Client connected; waiting for JSON configuration.");
                }
                if (client != null)
                {
                    try
                    {
                        NetworkStream net = client.GetStream();
                        while (net.DataAvailable)
                        {
                            byte[] bytes = new byte[4096];
                            int count = net.Read(bytes, 0, bytes.Length);
                            if (count == 0) throw new IOException("Closed");
                            incoming.Append(Encoding.UTF8.GetString(bytes, 0, count));
                            int delimiter;
                            while ((delimiter = incoming.ToString().IndexOf('\r')) >= 0)
                            {
                                string request = incoming.ToString(0, delimiter);
                                incoming.Remove(0, delimiter + 1);
                                if (request.Contains("\"appName\""))
                                {
                                    rejected = deny;
                                    Write(net, "{\"isAuthorized\":" + (deny ? "false" : "true") + "}\r");
                                    Console.WriteLine(deny ? "Authorization denied" : "Authorization accepted");
                                }
                                if (request.Contains("\"Json\"") && !rejected)
                                {
                                    configured = true;
                                    Console.WriteLine("JSON configured; streaming at 1Hz.");
                                }
                            }
                            if (incoming.Length > 65536) throw new IOException("Oversized request");
                        }
                        if (client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0)
                            throw new IOException("Client closed");
                    }
                    catch (Exception error)
                    {
                        if (!(error is IOException) && !(error is SocketException)) throw;
                        Console.WriteLine("Client disconnected"); client.Close(); client = null; configured = false;
                    }
                }
                if (auto)
                {
                    int stage = ((int)clock.Elapsed.TotalSeconds / 4) % 9;
                    if (stage != previousStage)
                    {
                        previousStage = stage;
                        int[] values = { 39, 40, 70, 100, 0, 70, 70, 70, 70 };
                        attention = values[stage]; signal = stage == 5 ? 200 : 0; paused = stage == 7;
                        if (stage == 8 && client != null) { client.Close(); client = null; configured = false; }
                        Console.WriteLine("Auto stage " + stage + ": attention=" + attention + " signal=" + signal + " paused=" + paused);
                    }
                }
                // Legacy SDK 3.2 identifies TGAM after 768 packets. A 1Hz-only stream times out first.
                // Batch writes preserve the 512Hz sample count without relying on 2ms OS timer precision.
                if (serial != null)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    if (paused) nextRaw = now;
                    else
                    {
                        int samples = Math.Min(256, Math.Max(0, (int)((now - nextRaw) * 512) + 1));
                        if (samples > 0)
                        {
                            byte[] raw = RawPacket(0);
                            byte[] batch = new byte[samples * raw.Length];
                            for (int sample = 0; sample < samples; sample++)
                                Buffer.BlockCopy(raw, 0, batch, sample * raw.Length, raw.Length);
                            serial.Write(batch, 0, batch.Length);
                            nextRaw += samples / 512.0;
                        }
                    }
                }
                if (clock.Elapsed.TotalSeconds >= next)
                {
                    next = clock.Elapsed.TotalSeconds + 1;
                    if (!paused)
                    {
                        if (serial != null) { byte[] packet = Packet(attention, signal); serial.Write(packet, 0, packet.Length); }
                        if (client != null && configured && !rejected)
                        {
                            try { Write(client.GetStream(), "{\"poorSignalLevel\":" + signal + ",\"eSense\":{\"attention\":" + attention + "}}\r"); }
                            catch (Exception error)
                            {
                                if (!(error is IOException) && !(error is SocketException)) throw;
                                client.Close(); client = null; configured = false;
                            }
                        }
                    }
                }
                // Polling console keys keeps the 1Hz producer running while a command is being typed.
                if (!Console.IsInputRedirected)
                    while (Console.KeyAvailable)
                    {
                        ConsoleKeyInfo key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Enter)
                        {
                            Console.WriteLine();
                            string command = commands.ToString().Trim().ToLowerInvariant(); commands.Clear();
                            int number;
                            if (command.StartsWith("a ") && int.TryParse(command.Substring(2), out number) && number >= 0 && number <= 100)
                            { attention = number; auto = false; }
                            else if (command.StartsWith("s ") && int.TryParse(command.Substring(2), out number) && number >= 0 && number <= 200)
                            { signal = number; auto = false; }
                            else if (command == "pause") { paused = true; auto = false; }
                            else if (command == "resume") { paused = false; auto = false; }
                            else if (command == "auto") { auto = true; previousStage = -1; }
                            else if (command == "manual") auto = false;
                            else if (command == "drop") { if (client != null) client.Close(); client = null; configured = false; }
                            else if (command == "quit") quit = true;
                            else Console.WriteLine("Unknown command or out-of-range value.");
                            Console.WriteLine("attention=" + attention + " signal=" + signal + " paused=" + paused + " auto=" + auto);
                        }
                        else if (key.Key == ConsoleKey.Backspace)
                        { if (commands.Length > 0) { commands.Length--; Console.Write("\b \b"); } }
                        else if (!char.IsControl(key.KeyChar)) { commands.Append(key.KeyChar); Console.Write(key.KeyChar); }
                    }
                Thread.Sleep(20);
            }
        }
        finally { if (client != null) client.Close(); if (listener != null) listener.Stop(); if (serial != null) serial.Dispose(); }
    }
    private static void Write(NetworkStream stream, string value)
    { byte[] bytes = Encoding.ASCII.GetBytes(value); stream.Write(bytes, 0, bytes.Length); }
}
'@
if ($PSVersionTable.PSEdition -eq 'Core') {
    $references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
    $references += [System.IO.Ports.SerialPort].Assembly.Location
    Add-Type -TypeDefinition $simulatorCode -ReferencedAssemblies $references
} else {
    Add-Type -TypeDefinition $simulatorCode
}
}
[MindWaveSimulator]::Run($Mode, $Port, $SerialPort, $Attention, $Signal, $Auto.IsPresent, $DenyAuthorization.IsPresent, $DurationSeconds)
