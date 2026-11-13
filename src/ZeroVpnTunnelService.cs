using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    public class StunNatResult
    {
        public bool Success { get; set; }
        public string PublicIp { get; set; }
        public int PublicPort { get; set; }
        public int LocalBoundPort { get; set; }
        public string NatType { get; set; }
        public string ErrorMessage { get; set; }
    }

    public static class ZeroVpnTunnelService
    {
        private static readonly string[] StunHosts = new string[]
        {
            "stun.l.google.com",
            "stun1.l.google.com",
            "stun2.l.google.com"
        };

        private const int StunPort = 19302;
        private static readonly byte[] PunchSynBytes = Encoding.ASCII.GetBytes("TITAN_PUNCH_SYN");
        private static readonly byte[] PunchAckBytes = Encoding.ASCII.GetBytes("TITAN_PUNCH_ACK");

        private static readonly object _sync = new object();
        private static UdpClient _tunnelClient;
        private static UdpClient _localProxyClient;
        private static Thread _relayThread;
        private static Thread _localProxyThread;
        private static Thread _punchThread;
        private static volatile bool _tunnelActive = false;
        private static IPEndPoint _remotePeerEp;
        private static IPEndPoint _lastLocalClientEp;
        private static int _localGamePort = 27015;
        private static long _lastPunchSentTicks = 0;
        private static bool _isRelayMode = false;
        private static string _relayRoomCode = "TITAN-TR";

        public static bool IsTunnelActive { get { return _tunnelActive; } }
        public static bool IsPeerConnected { get; private set; }
        public static int LastRttMs { get; private set; }
        public static DateTime LastPeerSeenUtc { get; private set; }
        public static string LastResolvedEndpoint { get; private set; }
        public static string LastNatType { get; private set; }
        public static string LastVirtualLanIp { get; private set; }

        /// <summary>
        /// Bilgisayarda aktif Radmin VPN (26.x), Tailscale (100.64-127.x), ZeroTier veya WireGuard
        /// sanal ağ IP'si varsa otomatik tespit eder.
        /// </summary>
        public static string DetectActiveVirtualLanIp()
        {
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    NetworkInterface nic = nics[i];
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    string desc = ((nic.Name ?? "") + " " + (nic.Description ?? "")).ToLowerInvariant();
                    bool isKnownVpnName = desc.Contains("radmin") || desc.Contains("tailscale") ||
                                          desc.Contains("zerotier") || desc.Contains("hamachi") ||
                                          desc.Contains("wireguard") || desc.Contains("wintun");

                    IPInterfaceProperties props = nic.GetIPProperties();
                    if (props == null || props.UnicastAddresses == null) continue;

                    for (int j = 0; j < props.UnicastAddresses.Count; j++)
                    {
                        UnicastIPAddressInformation ua = props.UnicastAddresses[j];
                        if (ua.Address == null || ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] b = ua.Address.GetAddressBytes();
                        if (b.Length != 4) continue;

                        bool isRadmin = b[0] == 26;
                        bool isHamachi = b[0] == 25;
                        bool isTailscale = (b[0] == 100 && b[1] >= 64 && b[1] <= 127);

                        if (isKnownVpnName || isRadmin || isHamachi || isTailscale)
                        {
                            LastVirtualLanIp = ua.Address.ToString();
                            return LastVirtualLanIp;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// CGNAT / Symmetric NAT varsa aktif sanal ağ IP'sini (Radmin/Tailscale/ZeroTier),
        /// yoksa çözümlenmiş STUN IP:Port adresini döndürür.
        /// </summary>
        public static string GetBestShareableEndpoint()
        {
            string virt = DetectActiveVirtualLanIp();
            bool isCgnat = !string.IsNullOrEmpty(LastNatType) &&
                (LastNatType.IndexOf("Symmetric", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 LastNatType.IndexOf("CGNAT", StringComparison.OrdinalIgnoreCase) >= 0);

            if ((isCgnat || string.IsNullOrEmpty(LastResolvedEndpoint)) && !string.IsNullOrEmpty(virt))
                return virt + ":27015";

            return LastResolvedEndpoint ?? "";
        }

        public static StunNatResult DiscoverPublicEndpoint(int localPort = 0)
        {
            StunNatResult result = new StunNatResult { Success = false, NatType = "Bilinmiyor" };

            lock (_sync)
            {
                try
                {
                    if (_tunnelClient == null || (localPort > 0 && ((IPEndPoint)_tunnelClient.Client.LocalEndPoint).Port != localPort))
                    {
                        if (!_tunnelActive && _tunnelClient != null)
                        {
                            try { _tunnelClient.Close(); } catch { }
                            _tunnelClient = null;
                        }
                        if (_tunnelClient == null)
                        {
                            _tunnelClient = localPort > 0 ? new UdpClient(localPort) : new UdpClient(0);
                            _tunnelClient.Client.ReceiveTimeout = 2500;
                            _tunnelClient.Client.SendTimeout = 2500;
                        }
                    }

                    result.LocalBoundPort = ((IPEndPoint)_tunnelClient.Client.LocalEndPoint).Port;

                    string firstIp = null;
                    int firstPort = 0;
                    string secondIp = null;
                    int secondPort = 0;

                    for (int i = 0; i < StunHosts.Length; i++)
                    {
                        string ip;
                        int port;
                        string err;
                        if (QueryStunOnSocket(_tunnelClient, StunHosts[i], out ip, out port, out err))
                        {
                            if (firstIp == null)
                            {
                                firstIp = ip;
                                firstPort = port;
                            }
                            else
                            {
                                secondIp = ip;
                                secondPort = port;
                                break;
                            }
                        }
                        else if (!string.IsNullOrEmpty(err))
                        {
                            result.ErrorMessage = err;
                        }
                    }

                    if (firstIp != null)
                    {
                        result.PublicIp = firstIp;
                        result.PublicPort = firstPort;
                        result.Success = true;

                        if (secondIp != null)
                        {
                            if (string.Equals(firstIp, secondIp, StringComparison.Ordinal) && firstPort == secondPort)
                                result.NatType = "Cone / Acik NAT (P2P Uygun)";
                            else
                                result.NatType = "Symmetric NAT / CGNAT (Port Degisiyor)";
                        }
                        else
                        {
                            result.NatType = "Tek STUN Dogrulandi";
                        }

                        LastResolvedEndpoint = result.PublicIp + ":" + result.PublicPort;
                        LastNatType = result.NatType;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                }
            }

            return result;
        }

        private static bool QueryStunOnSocket(UdpClient udp, string host, out string publicIp, out int publicPort, out string error)
        {
            publicIp = null;
            publicPort = 0;
            error = null;

            try
            {
                IPAddress[] addrs = Dns.GetHostAddresses(host);
                IPAddress stunIp = null;
                for (int i = 0; i < addrs.Length; i++)
                {
                    if (addrs[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        stunIp = addrs[i];
                        break;
                    }
                }
                if (stunIp == null) return false;

                byte[] req = new byte[20];
                req[0] = 0x00; req[1] = 0x01;
                req[2] = 0x00; req[3] = 0x00;
                req[4] = 0x21; req[5] = 0x12; req[6] = 0xA4; req[7] = 0x42;

                Random rng = new Random(Guid.NewGuid().GetHashCode());
                for (int i = 8; i < 20; i++)
                    req[i] = (byte)rng.Next(0, 256);

                IPEndPoint stunEp = new IPEndPoint(stunIp, StunPort);
                udp.Send(req, req.Length, stunEp);

                IPEndPoint recvEp = new IPEndPoint(IPAddress.Any, 0);
                byte[] resp = udp.Receive(ref recvEp);

                if (resp != null && resp.Length >= 20 && resp[0] == 0x01 && resp[1] == 0x01)
                {
                    int msgLen = (resp[2] << 8) | resp[3];
                    int idx = 20;
                    while (idx + 4 <= resp.Length && idx < 20 + msgLen)
                    {
                        int attrType = (resp[idx] << 8) | resp[idx + 1];
                        int attrLen = (resp[idx + 2] << 8) | resp[idx + 3];
                        idx += 4;

                        if (idx + attrLen > resp.Length) break;

                        if (attrType == 0x0020 && attrLen >= 8 && resp[idx + 1] == 0x01)
                        {
                            publicPort = ((resp[idx + 2] << 8) | resp[idx + 3]) ^ 0x2112;
                            byte b1 = (byte)(resp[idx + 4] ^ 0x21);
                            byte b2 = (byte)(resp[idx + 5] ^ 0x12);
                            byte b3 = (byte)(resp[idx + 6] ^ 0xA4);
                            byte b4 = (byte)(resp[idx + 7] ^ 0x42);
                            publicIp = string.Format("{0}.{1}.{2}.{3}", b1, b2, b3, b4);
                            return true;
                        }
                        else if (attrType == 0x0001 && attrLen >= 8 && resp[idx + 1] == 0x01)
                        {
                            publicPort = (resp[idx + 2] << 8) | resp[idx + 3];
                            publicIp = string.Format("{0}.{1}.{2}.{3}", resp[idx + 4], resp[idx + 5], resp[idx + 6], resp[idx + 7]);
                            return true;
                        }

                        int pad = (4 - (attrLen % 4)) % 4;
                        idx += attrLen + pad;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return false;
        }

        public static bool StartHolePunchTunnel(string peerIpPort, int localGamePort, out string statusMsg)
        {
            statusMsg = "";
            StopHolePunchTunnel(false);

            try
            {
                string[] parts = (peerIpPort ?? "").Trim().Split(':');
                if (parts.Length != 2)
                {
                    statusMsg = "Gecersiz hedef format. Ornek: 85.100.20.10:47889";
                    return false;
                }

                IPAddress peerIp = IPAddress.Parse(parts[0]);
                int peerPort = int.Parse(parts[1]);
                _remotePeerEp = new IPEndPoint(peerIp, peerPort);
                _localGamePort = localGamePort > 0 ? localGamePort : 27015;
                _lastLocalClientEp = null;
                _isRelayMode = false;
                IsPeerConnected = false;
                LastRttMs = 0;

                lock (_sync)
                {
                    // Reuse the STUN-mapped socket so NAT port mapping stays valid
                    if (_tunnelClient == null)
                        _tunnelClient = new UdpClient(0);

                    _tunnelClient.Client.ReceiveTimeout = 500;

                    try
                    {
                        _localProxyClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, _localGamePort));
                    }
                    catch
                    {
                        _localProxyClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                        _localGamePort = ((IPEndPoint)_localProxyClient.Client.LocalEndPoint).Port;
                    }
                    _localProxyClient.Client.ReceiveTimeout = 500;
                }

                _tunnelActive = true;

                _punchThread = new Thread(PunchLoop);
                _punchThread.IsBackground = true;
                _punchThread.Start();

                _relayThread = new Thread(RelayLoop);
                _relayThread.IsBackground = true;
                _relayThread.Start();

                _localProxyThread = new Thread(LocalProxyLoop);
                _localProxyThread.IsBackground = true;
                _localProxyThread.Start();

                int tunnelLocalPort = ((IPEndPoint)_tunnelClient.Client.LocalEndPoint).Port;
                statusMsg = string.Format("Zero-VPN P2P Tuneli Aktif! Hedef: {0} | Yerel Oyun Proxy: 127.0.0.1:{1} (Soket:{2})", _remotePeerEp, _localGamePort, tunnelLocalPort);
                return true;
            }
            catch (Exception ex)
            {
                statusMsg = "Tunel baslatma hatasi: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// CGNAT arkasındaki oyuncular için hafif UDP Relay (tools/titan_udp_relay.py) istemcisi başlatır.
        /// </summary>
        public static bool StartUdpRelayClient(string relayHost, int relayPort, string roomCode, int localGamePort, out string statusMsg)
        {
            statusMsg = "";
            StopHolePunchTunnel(true);

            try
            {
                if (string.IsNullOrWhiteSpace(relayHost) || relayPort <= 0)
                {
                    statusMsg = "Geçersiz UDP Relay sunucu adresi.";
                    return false;
                }

                IPAddress[] addrs = Dns.GetHostAddresses(relayHost.Trim());
                IPAddress relayIp = null;
                for (int i = 0; i < addrs.Length; i++)
                {
                    if (addrs[i].AddressFamily == AddressFamily.InterNetwork) { relayIp = addrs[i]; break; }
                }
                if (relayIp == null)
                {
                    statusMsg = "Relay sunucu IPv4 adresi çözümlenemedi.";
                    return false;
                }

                _remotePeerEp = new IPEndPoint(relayIp, relayPort);
                _localGamePort = localGamePort > 0 ? localGamePort : 27015;
                _relayRoomCode = string.IsNullOrWhiteSpace(roomCode) ? "TITAN-TR" : roomCode.Trim().ToUpperInvariant();
                _isRelayMode = true;
                _lastLocalClientEp = null;
                IsPeerConnected = false;
                LastRttMs = 0;

                lock (_sync)
                {
                    _tunnelClient = new UdpClient(0);
                    _tunnelClient.Client.ReceiveTimeout = 500;
                    try
                    {
                        _localProxyClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, _localGamePort));
                    }
                    catch
                    {
                        _localProxyClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                        _localGamePort = ((IPEndPoint)_localProxyClient.Client.LocalEndPoint).Port;
                    }
                    _localProxyClient.Client.ReceiveTimeout = 500;
                }

                _tunnelActive = true;

                _punchThread = new Thread(PunchLoop);
                _punchThread.IsBackground = true;
                _punchThread.Start();

                _relayThread = new Thread(RelayLoop);
                _relayThread.IsBackground = true;
                _relayThread.Start();

                _localProxyThread = new Thread(LocalProxyLoop);
                _localProxyThread.IsBackground = true;
                _localProxyThread.Start();

                statusMsg = string.Format("CGNAT UDP Relay Aktif! Sunucu: {0} | Oda: {1} | Yerel Proxy: 127.0.0.1:{2}", _remotePeerEp, _relayRoomCode, _localGamePort);
                return true;
            }
            catch (Exception ex)
            {
                statusMsg = "Relay başlatma hatası: " + ex.Message;
                return false;
            }
        }

        public static void StopHolePunchTunnel()
        {
            StopHolePunchTunnel(true);
        }

        private static void StopHolePunchTunnel(bool closeTunnelSocket)
        {
            _tunnelActive = false;
            IsPeerConnected = false;
            lock (_sync)
            {
                try
                {
                    if (_localProxyClient != null)
                    {
                        _localProxyClient.Close();
                        _localProxyClient = null;
                    }
                }
                catch { }

                if (closeTunnelSocket)
                {
                    try
                    {
                        if (_tunnelClient != null)
                        {
                            _tunnelClient.Close();
                            _tunnelClient = null;
                        }
                    }
                    catch { }
                }
            }
        }

        private static void PunchLoop()
        {
            byte[] relayJoinBytes = Encoding.ASCII.GetBytes("TITAN_RELAY_JOIN#" + (_relayRoomCode ?? "TITAN-TR"));
            while (_tunnelActive && _tunnelClient != null && _remotePeerEp != null)
            {
                try
                {
                    Interlocked.Exchange(ref _lastPunchSentTicks, Stopwatch.GetTimestamp());
                    if (_isRelayMode)
                        _tunnelClient.Send(relayJoinBytes, relayJoinBytes.Length, _remotePeerEp);
                    else
                        _tunnelClient.Send(PunchSynBytes, PunchSynBytes.Length, _remotePeerEp);
                }
                catch { }
                Thread.Sleep(2000);
            }
        }

        private static void LocalProxyLoop()
        {
            IPEndPoint senderEp = new IPEndPoint(IPAddress.Any, 0);
            while (_tunnelActive && _localProxyClient != null && _tunnelClient != null)
            {
                try
                {
                    byte[] data = _localProxyClient.Receive(ref senderEp);
                    if (data == null || data.Length == 0) continue;

                    _lastLocalClientEp = new IPEndPoint(senderEp.Address, senderEp.Port);
                    _tunnelClient.Send(data, data.Length, _remotePeerEp);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.TimedOut) continue;
                    if (!_tunnelActive) break;
                }
                catch
                {
                    if (!_tunnelActive) break;
                    Thread.Sleep(25);
                }
            }
        }

        private static void RelayLoop()
        {
            IPEndPoint fallbackGameEp = new IPEndPoint(IPAddress.Loopback, _localGamePort);
            IPEndPoint senderEp = new IPEndPoint(IPAddress.Any, 0);

            while (_tunnelActive && _tunnelClient != null)
            {
                try
                {
                    byte[] data = _tunnelClient.Receive(ref senderEp);
                    if (data == null || data.Length == 0) continue;

                    if (IsPacketMatch(data, PunchSynBytes))
                    {
                        IsPeerConnected = true;
                        LastPeerSeenUtc = DateTime.UtcNow;
                        _tunnelClient.Send(PunchAckBytes, PunchAckBytes.Length, senderEp);
                        continue;
                    }

                    if (IsPacketMatch(data, PunchAckBytes) || IsPacketMatch(data, Encoding.ASCII.GetBytes("TITAN_RELAY_OK")))
                    {
                        IsPeerConnected = true;
                        LastPeerSeenUtc = DateTime.UtcNow;
                        long sent = Interlocked.Read(ref _lastPunchSentTicks);
                        if (sent > 0)
                        {
                            double ms = (Stopwatch.GetTimestamp() - sent) * 1000.0 / Stopwatch.Frequency;
                            if (ms >= 0 && ms < 10000) LastRttMs = (int)Math.Round(ms);
                        }
                        continue;
                    }

                    if (senderEp.Address.Equals(_remotePeerEp.Address))
                    {
                        IsPeerConnected = true;
                        LastPeerSeenUtc = DateTime.UtcNow;
                        if (_localProxyClient != null)
                        {
                            IPEndPoint target = _lastLocalClientEp ?? fallbackGameEp;
                            _localProxyClient.Send(data, data.Length, target);
                        }
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.TimedOut) continue;
                    if (!_tunnelActive) break;
                }
                catch
                {
                    if (!_tunnelActive) break;
                    Thread.Sleep(25);
                }
            }
        }

        private static bool IsPacketMatch(byte[] data, byte[] expected)
        {
            if (data == null || expected == null || data.Length != expected.Length) return false;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != expected[i]) return false;
            }
            return true;
        }
    }
}
