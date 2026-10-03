using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// The link server: TCP on 127.0.0.1, one client (the RE2 plugin) at a time, newline-delimited JSON
    /// (protocol/PROTOCOL.md section 1). Networking runs on a background thread; the game thread calls
    /// <see cref="Drain"/> and <see cref="Send"/>.
    /// </summary>
    public sealed class LinkServer : IDisposable
    {
        public const int MaxLine = 64 * 1024;

        readonly int port;
        readonly object gate = new object();
        readonly Queue<Dictionary<string, object>> inbox = new Queue<Dictionary<string, object>>();
        readonly Queue<string> outbox = new Queue<string>();
        readonly AutoResetEvent outboxSignal = new AutoResetEvent(false);
        TcpListener listener;
        Thread acceptThread;
        TcpClient client;
        volatile bool running;
        int connections;

        public Action<string> Log = delegate { };

        public LinkServer(int port)
        {
            this.port = port;
        }

        public int Port { get { return listener != null ? ((IPEndPoint)listener.LocalEndpoint).Port : port; } }

        public bool Connected
        {
            get { lock (gate) return client != null; }
        }

        /// <summary>How many clients have connected so far (a new one means: send hello again).</summary>
        public int Connections
        {
            get { lock (gate) return connections; }
        }

        public void Start()
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            running = true;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "RaccoonSkylines link" };
            acceptThread.Start();
            Log("link listening on 127.0.0.1:" + Port);
        }

        public void Send(Dictionary<string, object> msg)
        {
            string line = Json.Write(msg) + "\n";
            lock (gate)
            {
                if (client == null)
                    return;
                // Never let a stalled reader grow the queue without bound: drop the oldest state messages.
                while (outbox.Count > 256)
                    outbox.Dequeue();
                outbox.Enqueue(line);
            }
            outboxSignal.Set();
        }

        /// <summary>Takes every message received since the last call.</summary>
        public List<Dictionary<string, object>> Drain()
        {
            lock (gate)
            {
                var list = new List<Dictionary<string, object>>(inbox);
                inbox.Clear();
                return list;
            }
        }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient c;
                try
                {
                    c = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    if (!running)
                        return;
                    Thread.Sleep(200);
                    continue;
                }
                c.NoDelay = true;
                // One client at a time: a reconnecting plugin is accepted once the old socket has closed.
                lock (gate)
                {
                    client = c;
                    outbox.Clear();
                    connections++;
                }
                Log("link: RE2 connected");
                var writer = new Thread(() => WriteLoop(c)) { IsBackground = true, Name = "RaccoonSkylines link writer" };
                writer.Start();
                ReadLoop(c);
                lock (gate)
                {
                    if (client == c)
                        client = null;
                }
                outboxSignal.Set();
                c.Close();
                Log("link: RE2 disconnected");
            }
        }

        void ReadLoop(TcpClient c)
        {
            var buf = new byte[65536];
            var line = new MemoryStream();
            bool dropping = false;
            try
            {
                NetworkStream s = c.GetStream();
                while (running)
                {
                    int n = s.Read(buf, 0, buf.Length);
                    if (n <= 0)
                        return;
                    for (int i = 0; i < n; i++)
                    {
                        byte b = buf[i];
                        if (b != (byte)'\n')
                        {
                            if (line.Length < MaxLine)
                                line.WriteByte(b);
                            else
                                dropping = true;
                            continue;
                        }
                        if (!dropping && line.Length > 0)
                        {
                            var msg = Json.ParseMessage(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length));
                            if (msg != null)
                                lock (gate)
                                {
                                    if (inbox.Count > 4096)
                                        inbox.Dequeue();
                                    inbox.Enqueue(msg);
                                }
                        }
                        line.SetLength(0);
                        dropping = false;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        void WriteLoop(TcpClient c)
        {
            try
            {
                NetworkStream s = c.GetStream();
                while (running)
                {
                    string next = null;
                    lock (gate)
                    {
                        if (client != c)
                            return;
                        if (outbox.Count > 0)
                            next = outbox.Dequeue();
                    }
                    if (next == null)
                    {
                        outboxSignal.WaitOne(100);
                        continue;
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(next);
                    s.Write(bytes, 0, bytes.Length);
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Dispose()
        {
            running = false;
            try
            {
                if (listener != null)
                    listener.Stop();
            }
            catch (SocketException)
            {
            }
            lock (gate)
            {
                if (client != null)
                    client.Close();
                client = null;
            }
            outboxSignal.Set();
        }
    }
}
