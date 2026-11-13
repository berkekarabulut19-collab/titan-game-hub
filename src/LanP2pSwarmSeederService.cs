using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    public class LanPeerSeedOffer
    {
        public string PeerIp { get; set; }
        public int TcpPort { get; set; }
        public string GameId { get; set; }
        public string GameName { get; set; }
        public long TotalSizeBytes { get; set; }
        public DateTime DiscoveredAt { get; set; }
    }

    /// <summary>
    /// Local LAN & P2P Swarm Chunk Seeder Service:
    /// Enables instantaneous peer-to-peer game and update distribution across local Wi-Fi / Ethernet subnets.
    /// When one squad member downloads an update, others pull it directly at 1 Gbps (100+ MB/s) LAN speeds
    /// without consuming external internet bandwidth.
    /// </summary>
    public static class LanP2pSwarmSeederService
    {
        private const int DiscoveryPort = 48088;
        private const int TransferPort = 48089;
        private static UdpClient _udpListener;
        private static TcpListener _tcpServer;
        private static Thread _listenThread;
        private static Thread _tcpThread;
        private static bool _isRunning = false;
        private static readonly object _sync = new object();
        private static List<GameProfile> _seedProfiles = new List<GameProfile>();

        public static bool IsRunning
        {
            get { return _isRunning; }
        }

        public static void StartSeeder(List<GameProfile> installedGames)
        {
            lock (_sync)
            {
                if (_isRunning) return;
                _seedProfiles = installedGames ?? new List<GameProfile>();

                try
                {
                    _udpListener = new UdpClient();
                    _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

                    _listenThread = new Thread(ListenForSwarmQueries);
                    _listenThread.IsBackground = true;
                    _listenThread.Start();

                    _tcpServer = new TcpListener(IPAddress.Any, TransferPort);
                    _tcpServer.Start();
                    _tcpThread = new Thread(AcceptChunkTransfers);
                    _tcpThread.IsBackground = true;
                    _tcpThread.Start();

                    _isRunning = true;
                }
                catch { }
            }
        }

        public static void StopSeeder()
        {
            lock (_sync)
            {
                _isRunning = false;
                try { if (_udpListener != null) _udpListener.Close(); } catch { }
                try { if (_tcpServer != null) _tcpServer.Stop(); } catch { }
            }
        }

        private static void ListenForSwarmQueries()
        {
            byte[] buf = new byte[2048];
            while (_isRunning)
            {
                try
                {
                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpListener.Receive(ref sender);
                    string msg = Encoding.UTF8.GetString(data);

                    if (msg.StartsWith("TITAN_SWARM_QUERY:"))
                    {
                        string qGameId = msg.Substring("TITAN_SWARM_QUERY:".Length).Trim();
                        GameProfile found = null;
                        for (int i = 0; i < _seedProfiles.Count; i++)
                        {
                            if (string.Equals(_seedProfiles[i].Id, qGameId, StringComparison.OrdinalIgnoreCase) && _seedProfiles[i].IsInstalled)
                            {
                                found = _seedProfiles[i];
                                break;
                            }
                        }

                        if (found != null && Directory.Exists(found.WorkingDirectory))
                        {
                            long totalBytes = CalculateDirectorySize(found.WorkingDirectory);
                            string offer = string.Format("TITAN_SWARM_OFFER:{0}|{1}|{2}|{3}", found.Id, found.Name, TransferPort, totalBytes);
                            byte[] respBytes = Encoding.UTF8.GetBytes(offer);
                            _udpListener.Send(respBytes, respBytes.Length, sender);
                        }
                    }
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private static void AcceptChunkTransfers()
        {
            while (_isRunning)
            {
                try
                {
                    TcpClient client = _tcpServer.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate(object state)
                    {
                        HandleTransferClient((TcpClient)state);
                    }, client);
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private static void HandleTransferClient(TcpClient client)
        {
            using (client)
            using (NetworkStream ns = client.GetStream())
            using (BinaryReader br = new BinaryReader(ns))
            using (BinaryWriter bw = new BinaryWriter(ns))
            {
                try
                {
                    string reqGameId = br.ReadString();
                    GameProfile target = null;
                    for (int i = 0; i < _seedProfiles.Count; i++)
                    {
                        if (string.Equals(_seedProfiles[i].Id, reqGameId, StringComparison.OrdinalIgnoreCase))
                        {
                            target = _seedProfiles[i];
                            break;
                        }
                    }

                    if (target == null || !Directory.Exists(target.WorkingDirectory))
                    {
                        bw.Write(false);
                        return;
                    }

                    bw.Write(true);
                    string[] files = Directory.GetFiles(target.WorkingDirectory, "*.*", SearchOption.AllDirectories);
                    bw.Write(files.Length);

                    byte[] buffer = new byte[65536];
                    string root = target.WorkingDirectory;

                    for (int i = 0; i < files.Length; i++)
                    {
                        string fullPath = files[i];
                        string relPath = fullPath.Substring(root.Length).TrimStart('\\', '/');
                        FileInfo fi = new FileInfo(fullPath);

                        bw.Write(relPath);
                        bw.Write(fi.Length);

                        using (FileStream fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            long rem = fi.Length;
                            while (rem > 0)
                            {
                                int read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, rem));
                                if (read <= 0) break;
                                bw.Write(buffer, 0, read);
                                rem -= read;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Probes local subnet to find any peer holding the required game.
        /// </summary>
        public static LanPeerSeedOffer DiscoverLocalSeeder(string gameId, int timeoutMs)
        {
            try
            {
                using (UdpClient client = new UdpClient())
                {
                    client.EnableBroadcast = true;
                    client.Client.ReceiveTimeout = timeoutMs;

                    string query = "TITAN_SWARM_QUERY:" + gameId;
                    byte[] qBytes = Encoding.UTF8.GetBytes(query);
                    IPEndPoint broadcastEp = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
                    client.Send(qBytes, qBytes.Length, broadcastEp);

                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] resp = client.Receive(ref sender);
                    string respStr = Encoding.UTF8.GetString(resp);

                    if (respStr.StartsWith("TITAN_SWARM_OFFER:"))
                    {
                        string[] parts = respStr.Substring("TITAN_SWARM_OFFER:".Length).Split('|');
                        if (parts.Length >= 4)
                        {
                            return new LanPeerSeedOffer
                            {
                                PeerIp = sender.Address.ToString(),
                                GameId = parts[0],
                                GameName = parts[1],
                                TcpPort = int.Parse(parts[2]),
                                TotalSizeBytes = long.Parse(parts[3]),
                                DiscoveredAt = DateTime.Now
                            };
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static long CalculateDirectorySize(string path)
        {
            long size = 0;
            try
            {
                string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try { size += new FileInfo(files[i]).Length; } catch { }
                }
            }
            catch { }
            return size;
        }
    }
}
