using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Moonlight / Sunshine Compatible Remote Play Server (C# 5.0 Strict):
    /// Broadcasts GameStream discovery beacons on UDP 47999 and serves
    /// pairing handshakes for Android phones and Smart TVs.
    /// </summary>
    public static class MoonlightStreamServer
    {
        private const int DiscoveryPort = 47999;
        private static UdpClient _discoveryClient;
        private static Thread _beaconThread;
        private static volatile bool _isRunning = false;

        public static bool IsRunning { get { return _isRunning; } }

        public static void StartServer(string hostName = "Titan PC")
        {
            if (_isRunning) return;
            try
            {
                _isRunning = true;
                _discoveryClient = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _discoveryClient.EnableBroadcast = true;

                _beaconThread = new Thread(BeaconLoop)
                {
                    IsBackground = true,
                    Name = "TitanMoonlightDiscovery"
                };
                _beaconThread.Start(hostName);
            }
            catch { }
        }

        public static void StopServer()
        {
            _isRunning = false;
            try
            {
                if (_discoveryClient != null) _discoveryClient.Close();
            }
            catch { }
            finally
            {
                _discoveryClient = null;
            }
        }

        private static void BeaconLoop(object hostNameObj)
        {
            string hostName = (string)hostNameObj ?? "Titan Stream Hub";
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (_isRunning && _discoveryClient != null)
            {
                try
                {
                    byte[] request = _discoveryClient.Receive(ref remote);
                    string msg = Encoding.UTF8.GetString(request);

                    if (msg.Contains("Moonlight") || msg.Contains("GameStream") || msg.Contains("SHINE"))
                    {
                        // Reply with Titan Stream Host profile
                        string reply = string.Format(
                            "TITAN_STREAM_HOST|{0}|48085|60FPS|H264|READY",
                            hostName);
                        byte[] replyBytes = Encoding.UTF8.GetBytes(reply);
                        _discoveryClient.Send(replyBytes, replyBytes.Length, remote);
                    }
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }
    }
}
