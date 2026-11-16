using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    public class LanRoomBeacon
    {
        public string HostName { get; set; }
        public string GameName { get; set; }
        public string AppId { get; set; }
        public string EndPoint { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public static class SteamEmulatorBridge
    {
        private const int DiscoveryPort = 47888;
        private static UdpClient _udpServer;
        private static Thread _listenThread;
        private static bool _listening = false;
        private static readonly Dictionary<string, LanRoomBeacon> _activeRooms = new Dictionary<string, LanRoomBeacon>(StringComparer.OrdinalIgnoreCase);

        public static event Action<List<LanRoomBeacon>> OnLanRoomsUpdated;

        public static void StartLanDiscovery(string userName)
        {
            if (_listening) return;
            try
            {
                _listening = true;
                _udpServer = new UdpClient();
                _udpServer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpServer.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

                _listenThread = new Thread(ListenLoop);
                _listenThread.IsBackground = true;
                _listenThread.Start();
            }
            catch { }
        }

        public static void StopLanDiscovery()
        {
            _listening = false;
            try
            {
                if (_udpServer != null)
                {
                    _udpServer.Close();
                    _udpServer = null;
                }
            }
            catch { }
        }

        public static void BroadcastRoom(string userName, string gameName, string appId)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    using (UdpClient client = new UdpClient())
                    {
                        client.EnableBroadcast = true;
                        string msg = string.Format("TITAN_LAN#{0}#{1}#{2}#{3}", userName, gameName, appId, DateTime.UtcNow.Ticks);
                        byte[] bytes = Encoding.UTF8.GetBytes(msg);
                        IPEndPoint ep = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
                        client.Send(bytes, bytes.Length, ep);
                    }
                }
                catch { }
            });
        }

        private static void ListenLoop()
        {
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            while (_listening)
            {
                try
                {
                    if (_udpServer == null) break;
                    byte[] data = _udpServer.Receive(ref remoteEp);
                    if (data == null || data.Length == 0) continue;

                    string msg = Encoding.UTF8.GetString(data);
                    if (msg.StartsWith("TITAN_LAN#", StringComparison.Ordinal))
                    {
                        string[] parts = msg.Split('#');
                        if (parts.Length >= 4)
                        {
                            string host = parts[1];
                            string game = parts[2];
                            string app = parts[3];
                            string key = host + "_" + game;

                            lock (_activeRooms)
                            {
                                LanRoomBeacon beacon;
                                if (!_activeRooms.TryGetValue(key, out beacon))
                                {
                                    beacon = new LanRoomBeacon();
                                    _activeRooms[key] = beacon;
                                }
                                beacon.HostName = host;
                                beacon.GameName = game;
                                beacon.AppId = app;
                                beacon.EndPoint = remoteEp.Address.ToString();
                                beacon.LastSeen = DateTime.Now;
                            }

                            PruneAndNotify();
                        }
                    }
                }
                catch
                {
                    if (!_listening) break;
                    Thread.Sleep(200);
                }
            }
        }

        private static void PruneAndNotify()
        {
            lock (_activeRooms)
            {
                List<string> expired = new List<string>();
                List<LanRoomBeacon> valid = new List<LanRoomBeacon>();
                DateTime now = DateTime.Now;

                foreach (KeyValuePair<string, LanRoomBeacon> kv in _activeRooms)
                {
                    if ((now - kv.Value.LastSeen).TotalSeconds > 15)
                        expired.Add(kv.Key);
                    else
                        valid.Add(kv.Value);
                }

                foreach (string k in expired) _activeRooms.Remove(k);

                if (OnLanRoomsUpdated != null)
                    OnLanRoomsUpdated(valid);
            }
        }

        // ========================================================
        // GOLDBERG / SMARTSTEAMEMU / ONLINE-FIX HIERARCHY DEPLOYMENT
        // ========================================================
        public static bool DeployFullEmulationSuite(GameProfile gp, string customUserName)
        {
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
                return false;

            try
            {
                string realAppId = string.IsNullOrEmpty(gp.SteamAppId) ? "480" : gp.SteamAppId;
                string bridgeAppId = "480";
                string user = string.IsNullOrEmpty(customUserName) ? Environment.UserName : customUserName;
                string root = gp.WorkingDirectory;

                ulong baseSteamId = 76561198000000000UL;
                string seed = (user ?? "TitanUser") + "_" + Environment.MachineName;
                uint hash = (uint)Math.Abs(seed.GetHashCode());
                ulong uniqueSteamId = baseSteamId + (hash % 900000000UL) + 1;
                string steamIdStr = uniqueSteamId.ToString();

                // 1. steam_appid.txt (Spacewar P2P bridge için 480; Steam yönetimindeki oyunlara dokunma)
                if (!gp.IsSteamManaged)
                    File.WriteAllText(Path.Combine(root, "steam_appid.txt"), bridgeAppId, Encoding.ASCII);

                // 2. OnlineFix.ini: arşivden çıkan çalışan OnlineFix.ini varsa ASLA üzerine yazma
                string ofIniPath = Path.Combine(root, "OnlineFix.ini");
                if (!gp.IsSteamManaged && !File.Exists(ofIniPath))
                {
                    StringBuilder ofIni = new StringBuilder();
                    ofIni.AppendLine("[Main]");
                    ofIni.AppendLine("RealAppId=" + realAppId);
                    ofIni.AppendLine("FakeAppId=480");
                    ofIni.AppendLine("Language=turkish");
                    ofIni.AppendLine("PlayerName=" + user);
                    ofIni.AppendLine("SteamId64=" + steamIdStr);
                    ofIni.AppendLine("UseSpacewarP2P=true");
                    ofIni.AppendLine("UnlockAllDLC=true");
                    ofIni.AppendLine("Overlay=true");
                    File.WriteAllText(ofIniPath, ofIni.ToString(), new UTF8Encoding(false));
                }

                // 3. Goldberg / emülatör ayar hiyerarşisi (farklı loader'lar için uyumlu dosya seti)
                string steamSettings = Path.Combine(root, "steam_settings");
                if (!Directory.Exists(steamSettings)) Directory.CreateDirectory(steamSettings);

                string settingsSub = Path.Combine(steamSettings, "settings");
                if (!Directory.Exists(settingsSub)) Directory.CreateDirectory(settingsSub);

                // Goldberg (settings alt klasörü)
                File.WriteAllText(Path.Combine(settingsSub, "account_name.txt"), user, Encoding.UTF8);
                File.WriteAllText(Path.Combine(settingsSub, "language.txt"), "turkish", Encoding.UTF8);
                File.WriteAllText(Path.Combine(settingsSub, "listen_port.txt"), "47584", Encoding.UTF8);
                File.WriteAllText(Path.Combine(settingsSub, "user_steam_id.txt"), steamIdStr, Encoding.UTF8);

                // Uyum kopyaları (bazı fork/loader varyantları kök steam_settings okur)
                File.WriteAllText(Path.Combine(steamSettings, "account_name.txt"), user, Encoding.UTF8);
                File.WriteAllText(Path.Combine(steamSettings, "language.txt"), "turkish", Encoding.UTF8);
                File.WriteAllText(Path.Combine(steamSettings, "listen_port.txt"), "47584", Encoding.UTF8);
                File.WriteAllText(Path.Combine(steamSettings, "user_steam_id.txt"), steamIdStr, Encoding.UTF8);
                File.WriteAllText(Path.Combine(steamSettings, "force_account_name.txt"), user, Encoding.UTF8);
                File.WriteAllText(Path.Combine(steamSettings, "force_steamid.txt"), steamIdStr, Encoding.ASCII);
                File.WriteAllText(Path.Combine(steamSettings, "force_language.txt"), "turkish", Encoding.ASCII);

                File.WriteAllText(Path.Combine(steamSettings, "DLC.txt"), "1000=All DLC Unlock Pack\r\n", Encoding.UTF8);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Writes Goldberg/LAN co-op peer discovery files (custom_broadcasts.txt and connect.txt)
        /// so remote friends can be discovered over P2P/Zero-VPN without Steam.
        /// </summary>
        public static bool WriteCoopPeerEndpoint(GameProfile gp, string peerIpOrHostPort)
        {
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
                return false;
            if (string.IsNullOrWhiteSpace(peerIpOrHostPort))
                return false;

            try
            {
                string raw = peerIpOrHostPort.Trim();
                string ipOnly = raw.Contains(":") ? raw.Split(':')[0].Trim() : raw;
                if (string.IsNullOrEmpty(ipOnly)) return false;

                string steamSettings = Path.Combine(gp.WorkingDirectory, "steam_settings");
                string settingsSub = Path.Combine(steamSettings, "settings");
                if (!Directory.Exists(steamSettings)) Directory.CreateDirectory(steamSettings);
                if (!Directory.Exists(settingsSub)) Directory.CreateDirectory(settingsSub);

                string broadcasts = ipOnly + "\r\n127.0.0.1\r\n";
                File.WriteAllText(Path.Combine(steamSettings, "custom_broadcasts.txt"), broadcasts, Encoding.ASCII);
                File.WriteAllText(Path.Combine(settingsSub, "custom_broadcasts.txt"), broadcasts, Encoding.ASCII);
                File.WriteAllText(Path.Combine(steamSettings, "connect.txt"), raw, Encoding.ASCII);
                File.WriteAllText(Path.Combine(settingsSub, "connect.txt"), raw, Encoding.ASCII);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
