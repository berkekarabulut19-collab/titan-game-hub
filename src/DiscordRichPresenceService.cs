using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Pure Win32 Named Pipe Discord Rich Presence Client (C# 5.0 Strict):
    /// Directly connects to Discord IPC pipe (\\.\pipe\discord-ipc-0 ... 9).
    /// Zero external DLL dependencies (No DiscordRPC.dll required).
    /// Broadcasts live game name, lobby status, elapsed time and join buttons.
    /// </summary>
    public static class DiscordRichPresenceService
    {
        private const string DefaultClientId = "1214258994787524679"; // Titan Game Hub Discord App ID
        private static NamedPipeClientStream _pipe;
        private static Thread _workerThread;
        private static volatile bool _isRunning = false;
        private static readonly object _syncLock = new object();

        private static string _currentDetails = "Ana Menüde Geziniyor";
        private static string _currentState = "Titan Game Hub v8.5";
        private static string _largeImageKey = "titan_logo";
        private static string _largeImageText = "Titan Game Hub // Co-Op Platform";
        private static long _startTimestamp = 0;
        private static int _partySize = 0;
        private static int _partyMax = 0;
        private static string _joinSecret = null;
        private static bool _dirty = true;

        public static void Initialize(string customClientId = null)
        {
            if (_isRunning) return;
            _isRunning = true;
            _startTimestamp = GetCurrentEpochSeconds();

            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "TitanDiscordRpcWorker"
            };
            _workerThread.Start(string.IsNullOrEmpty(customClientId) ? DefaultClientId : customClientId);
        }

        public static void Shutdown()
        {
            _isRunning = false;
            ClosePipe();
        }

        public static void SetActivity(string details, string state, string largeImg = "titan_logo", string largeText = null, int pSize = 0, int pMax = 0, string secret = null)
        {
            lock (_syncLock)
            {
                _currentDetails = details ?? "Oyun Oynuyor";
                _currentState = state ?? "Co-Op Lobi";
                _largeImageKey = string.IsNullOrEmpty(largeImg) ? "titan_logo" : largeImg;
                _largeImageText = largeText ?? "Titan Game Hub";
                _partySize = pSize;
                _partyMax = pMax;
                _joinSecret = secret;
                _dirty = true;
            }
        }

        public static void SetGamePlaying(GameProfile profile, string roomCode)
        {
            if (profile == null) return;
            string details = "Oynuyor: " + profile.Name;
            string state = string.IsNullOrEmpty(roomCode) ? "Tek Oyunculu / LAN" : "Lobi: " + roomCode;
            int maxP = 4;
            int curP = 1;

            if (!string.IsNullOrEmpty(profile.MaxPlayers))
            {
                int parsed;
                if (int.TryParse(profile.MaxPlayers.Replace("P", "").Trim(), out parsed))
                {
                    maxP = Math.Max(2, parsed);
                }
            }

            lock (_syncLock)
            {
                _startTimestamp = GetCurrentEpochSeconds();
            }
            SetActivity(details, state, "game_active", profile.Name, curP, maxP, "connect_" + profile.Id);
        }

        public static void SetIdleInHub()
        {
            lock (_syncLock)
            {
                _startTimestamp = GetCurrentEpochSeconds();
            }
            SetActivity("Ana Menüde Geziniyor", "Kütüphane & Store Keşfi", "titan_logo", "Titan Game Hub v8.5", 0, 0, null);
        }

        private static void WorkerLoop(object stateObj)
        {
            string clientId = (string)stateObj;

            while (_isRunning)
            {
                try
                {
                    if (_pipe == null || !_pipe.IsConnected)
                    {
                        if (!TryConnectPipe())
                        {
                            Thread.Sleep(5000);
                            continue;
                        }

                        // Send Handshake: Opcode 0
                        string handshakeJson = string.Format("{{\"v\":1,\"client_id\":\"{0}\"}}", clientId);
                        SendFrame(0, handshakeJson);

                        // Read Handshake response
                        ReadFrame();
                        _dirty = true;
                    }

                    if (_dirty && _pipe != null && _pipe.IsConnected)
                    {
                        string activityPayload = BuildActivityJson();
                        SendFrame(1, activityPayload); // Opcode 1: FRAME
                        _dirty = false;
                    }

                    // Read possible incoming commands or heartbeats
                    if (_pipe != null && _pipe.IsConnected)
                    {
                        while (_pipe.CanRead && _pipe.IsConnected)
                        {
                            // Peek or non-blocking check
                            if (_pipe.ReadByte() == -1) break;
                        }
                    }

                    Thread.Sleep(2000);
                }
                catch
                {
                    ClosePipe();
                    Thread.Sleep(4000);
                }
            }
        }

        private static bool TryConnectPipe()
        {
            ClosePipe();

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    string pipeName = "discord-ipc-" + i;
                    NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    client.Connect(400);
                    if (client.IsConnected)
                    {
                        _pipe = client;
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private static void ClosePipe()
        {
            try
            {
                if (_pipe != null)
                {
                    _pipe.Close();
                    _pipe.Dispose();
                }
            }
            catch { }
            finally
            {
                _pipe = null;
            }
        }

        private static void SendFrame(int opcode, string json)
        {
            if (_pipe == null || !_pipe.IsConnected) return;

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] header = new byte[8];

            // 4 bytes opcode (Little Endian)
            header[0] = (byte)(opcode & 0xFF);
            header[1] = (byte)((opcode >> 8) & 0xFF);
            header[2] = (byte)((opcode >> 16) & 0xFF);
            header[3] = (byte)((opcode >> 24) & 0xFF);

            // 4 bytes payload length (Little Endian)
            int len = jsonBytes.Length;
            header[4] = (byte)(len & 0xFF);
            header[5] = (byte)((len >> 8) & 0xFF);
            header[6] = (byte)((len >> 16) & 0xFF);
            header[7] = (byte)((len >> 24) & 0xFF);

            _pipe.Write(header, 0, 8);
            _pipe.Write(jsonBytes, 0, jsonBytes.Length);
            _pipe.Flush();
        }

        private static void ReadFrame()
        {
            if (_pipe == null || !_pipe.IsConnected) return;
            byte[] header = new byte[8];
            int read = _pipe.Read(header, 0, 8);
            if (read == 8)
            {
                int len = header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24);
                if (len > 0 && len < 65536)
                {
                    byte[] payload = new byte[len];
                    _pipe.Read(payload, 0, len);
                }
            }
        }

        private static string BuildActivityJson()
        {
            string nonce = Guid.NewGuid().ToString("N");
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;

            string partyBlock = "";
            if (_partySize > 0 && _partyMax > 0)
            {
                partyBlock = string.Format(",\"party\":{{\"id\":\"titan_lobby\",\"size\":[{0},{1}]}}", _partySize, _partyMax);
            }

            string secretsBlock = "";
            if (!string.IsNullOrEmpty(_joinSecret))
            {
                secretsBlock = string.Format(",\"secrets\":{{\"join\":\"{0}\"}}", EscapeJson(_joinSecret));
            }

            string timestampsBlock = "";
            if (_startTimestamp > 0)
            {
                timestampsBlock = string.Format(",\"timestamps\":{{\"start\":{0}}}", _startTimestamp);
            }

            string json = string.Format(
                "{{\"cmd\":\"SET_ACTIVITY\",\"args\":{{\"pid\":{0},\"activity\":{{" +
                "\"details\":\"{1}\"," +
                "\"state\":\"{2}\"," +
                "\"assets\":{{\"large_image\":\"{3}\",\"large_text\":\"{4}\",\"small_image\":\"titan_icon\",\"small_text\":\"Titan Game Hub\"}}" +
                "{5}{6}{7}" +
                "}}}},\"nonce\":\"{8}\"}}",
                pid,
                EscapeJson(_currentDetails),
                EscapeJson(_currentState),
                EscapeJson(_largeImageKey),
                EscapeJson(_largeImageText),
                timestampsBlock,
                partyBlock,
                secretsBlock,
                nonce
            );

            return json;
        }

        private static long GetCurrentEpochSeconds()
        {
            TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)t.TotalSeconds;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }
    }
}
