using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace TitanGameHub
{
    public static class P2PVoiceChatService
    {
        private static bool _isActive = false;
        private static bool _isMuted = false;
        private static bool _isPushToTalkActive = false;
        private static UdpClient _udpVoiceClient;
        private static Thread _receiveThread;
        private static int _voicePort = 48082;
        private static int _activeSquadMembers = 0;

        public static bool IsActive
        {
            get { return _isActive; }
        }

        public static bool IsMuted
        {
            get { return _isMuted; }
            set { _isMuted = value; }
        }

        public static bool IsTalking
        {
            get { return _isPushToTalkActive && !_isMuted; }
        }

        public static int ActiveSquadMembers
        {
            get { return _activeSquadMembers; }
        }

        public static bool StartVoiceChannel(int port, out string statusMsg)
        {
            if (_isActive)
            {
                statusMsg = "Sesli sohbet zaten aktif.";
                return true;
            }

            try
            {
                _voicePort = port > 0 ? port : 48082;
                _udpVoiceClient = new UdpClient();
                _udpVoiceClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpVoiceClient.Client.Bind(new IPEndPoint(IPAddress.Any, _voicePort));

                _isActive = true;
                _receiveThread = new Thread(ReceiveVoicePacketsLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                statusMsg = string.Format("Spacewar (480) P2P Takım Sesli Sohbeti başlatıldı (Port: {0}). Bas-Konuş: [V] tuşu.", _voicePort);
                return true;
            }
            catch (Exception ex)
            {
                _isActive = false;
                statusMsg = "Sesli sohbet başlatılamadı: " + ex.Message;
                return false;
            }
        }

        public static void StopVoiceChannel()
        {
            _isActive = false;
            if (_udpVoiceClient != null)
            {
                try { _udpVoiceClient.Close(); } catch { }
                _udpVoiceClient = null;
            }
        }

        public static void SetPushToTalk(bool pressed, string broadcastTargetIp)
        {
            _isPushToTalkActive = pressed;
            if (pressed && !_isMuted && _isActive && _udpVoiceClient != null && !string.IsNullOrEmpty(broadcastTargetIp))
            {
                // Send ping/voice presence packet to squad peers
                try
                {
                    byte[] pingPkt = new byte[] { 0x54, 0x56, 0x4F, 0x49, 0x43, 0x45, 0x01 }; // "TVOICE\x01"
                    IPEndPoint targetEp = new IPEndPoint(IPAddress.Parse(broadcastTargetIp), _voicePort);
                    _udpVoiceClient.Send(pingPkt, pingPkt.Length, targetEp);
                }
                catch { }
            }
        }

        private static void ReceiveVoicePacketsLoop()
        {
            IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            while (_isActive && _udpVoiceClient != null)
            {
                try
                {
                    byte[] data = _udpVoiceClient.Receive(ref remoteEp);
                    if (data != null && data.Length >= 7)
                    {
                        if (data[0] == 0x54 && data[1] == 0x56 && data[2] == 0x4F) // TVOICE header
                        {
                            _activeSquadMembers = Math.Max(1, _activeSquadMembers);
                        }
                    }
                }
                catch
                {
                    if (!_isActive) break;
                }
            }
        }
    }
}
