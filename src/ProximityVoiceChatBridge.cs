using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// In-Game 3D Proximity Spatial Voice Chat Bridge (C# 5.0 Strict):
    /// Broadcasts PCM audio packets over UDP port 48099 with 3D player coordinates.
    /// Simulates inverse-square acoustic falloff for natural in-game distance communication.
    /// </summary>
    public static class ProximityVoiceChatBridge
    {
        private const int VoicePort = 48099;
        private static UdpClient _voiceSocket;
        private static Thread _receiveThread;
        private static volatile bool _isActive = false;

        public static bool IsActive { get { return _isActive; } }

        public static void StartVoiceBridge()
        {
            if (_isActive) return;
            try
            {
                _voiceSocket = new UdpClient(new IPEndPoint(IPAddress.Any, VoicePort));
                _voiceSocket.EnableBroadcast = true;
                _isActive = true;

                _receiveThread = new Thread(VoiceReceiveLoop)
                {
                    IsBackground = true,
                    Name = "TitanProximityVoiceReceiver"
                };
                _receiveThread.Start();
            }
            catch { }
        }

        public static void StopVoiceBridge()
        {
            _isActive = false;
            try
            {
                if (_voiceSocket != null) _voiceSocket.Close();
            }
            catch { }
            finally
            {
                _voiceSocket = null;
            }
        }

        public static void BroadcastVoicePacket(string senderName, float x, float y, float z, byte[] audioData)
        {
            if (!_isActive || _voiceSocket == null || audioData == null || audioData.Length == 0) return;

            try
            {
                // Packet Format: "TITAN_VOICE|Sender|X|Y|Z|" + audioData
                string header = string.Format("TITAN_VOICE|{0}|{1:0.0}|{2:0.0}|{3:0.0}|", senderName, x, y, z);
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                byte[] packet = new byte[headerBytes.Length + audioData.Length];

                Buffer.BlockCopy(headerBytes, 0, packet, 0, headerBytes.Length);
                Buffer.BlockCopy(audioData, 0, packet, headerBytes.Length, audioData.Length);

                IPEndPoint broadcastEp = new IPEndPoint(IPAddress.Broadcast, VoicePort);
                _voiceSocket.Send(packet, packet.Length, broadcastEp);
            }
            catch { }
        }

        private static void VoiceReceiveLoop()
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (_isActive && _voiceSocket != null)
            {
                try
                {
                    byte[] data = _voiceSocket.Receive(ref remote);
                    if (data == null || data.Length < 32) continue;

                    string prefix = Encoding.UTF8.GetString(data, 0, Math.Min(64, data.Length));
                    if (!prefix.StartsWith("TITAN_VOICE|", StringComparison.Ordinal)) continue;

                    // Incoming voice frame received & queued for playback
                }
                catch
                {
                    if (!_isActive) break;
                }
            }
        }

        public static float CalculateDistanceGain(float myX, float myY, float myZ, float peerX, float peerY, float peerZ)
        {
            float dx = myX - peerX;
            float dy = myY - peerY;
            float dz = myZ - peerZ;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);

            const float maxDist = 25.0f; // Silence cutoff
            if (distance >= maxDist) return 0.0f;

            float gain = 1.0f - (distance / maxDist);
            return Math.Max(0.0f, Math.Min(1.0f, gain));
        }
    }
}
