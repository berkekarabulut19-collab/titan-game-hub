using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TitanGameHub
{
    public static class DiscordRpcService
    {
        private const string DefaultClientId = "121932456789012345";
        private static NamedPipeClientStream _pipeStream;
        private static bool _isConnected = false;
        private static string _currentGame = null;
        private static string _currentStatus = null;
        private static long _gameStartTimeEpoch = 0;
        private static readonly object _syncLock = new object();

        public static bool IsConnected { get { return _isConnected; } }

        public static void SetActivity(string gameName, string statusDetails)
        {
            lock (_syncLock)
            {
                _currentGame = gameName;
                _currentStatus = statusDetails ?? "Titan Game Hub — Spacewar 480 Co-Op";
                _gameStartTimeEpoch = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                EnsureConnectionAndSend();
            });
        }

        public static void ClearActivity()
        {
            lock (_syncLock)
            {
                _currentGame = null;
                _currentStatus = null;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                SendActivityPayload(null);
            });
        }

        private static void EnsureConnectionAndSend()
        {
            lock (_syncLock)
            {
                if (_pipeStream == null || !_pipeStream.IsConnected)
                {
                    _pipeStream = ConnectToDiscordPipe();
                    if (_pipeStream == null)
                    {
                        _isConnected = false;
                        return;
                    }
                    _isConnected = true;
                }

                if (!string.IsNullOrEmpty(_currentGame))
                {
                    SendActivityPayload(_currentGame);
                }
            }
        }

        private static NamedPipeClientStream ConnectToDiscordPipe()
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    NamedPipeClientStream stream = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    stream.Connect(250);
                    if (stream.IsConnected)
                    {
                        // Send Handshake Opcode 0
                        string handshakeJson = "{\"v\":1,\"client_id\":\"" + DefaultClientId + "\"}";
                        WriteFrame(stream, 0, handshakeJson);

                        // Read Handshake Response
                        byte[] header = new byte[8];
                        int read = stream.Read(header, 0, 8);
                        if (read == 8)
                        {
                            int len = BitConverter.ToInt32(header, 4);
                            byte[] respBuf = new byte[len];
                            stream.Read(respBuf, 0, len);
                        }

                        return stream;
                    }
                }
                catch
                {
                    // Pipe i is not open or Discord is closed
                }
            }
            return null;
        }

        private static void SendActivityPayload(string gameName)
        {
            try
            {
                if (_pipeStream == null || !_pipeStream.IsConnected) return;

                string json;
                if (string.IsNullOrEmpty(gameName))
                {
                    json = "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + Process.GetCurrentProcess().Id + ",\"activity\":null},\"nonce\":\"" + Guid.NewGuid().ToString("N") + "\"}";
                }
                else
                {
                    string safeGame = gameName.Replace("\"", "\\\"");
                    string safeStatus = (_currentStatus ?? "Spacewar (AppID 480) Co-Op").Replace("\"", "\\\"");

                    json = "{" +
                        "\"cmd\":\"SET_ACTIVITY\"," +
                        "\"args\":{" +
                            "\"pid\":" + Process.GetCurrentProcess().Id + "," +
                            "\"activity\":{" +
                                "\"state\":\"" + safeStatus + "\"," +
                                "\"details\":\"" + safeGame + "\"," +
                                "\"timestamps\":{\"start\":" + _gameStartTimeEpoch + "}," +
                                "\"assets\":{\"large_image\":\"steam_icon\",\"large_text\":\"" + safeGame + "\",\"small_image\":\"spacewar\",\"small_text\":\"Titan Game Hub v9.0\"}," +
                                "\"buttons\":[{\"label\":\"🎮 Lobisine Katıl\",\"url\":\"steam://run/480//\"}]" +
                            "}" +
                        "}," +
                        "\"nonce\":\"" + Guid.NewGuid().ToString("N") + "\"" +
                    "}";
                }

                WriteFrame(_pipeStream, 1, json);
            }
            catch
            {
                _isConnected = false;
                try { if (_pipeStream != null) _pipeStream.Dispose(); } catch { }
                _pipeStream = null;
            }
        }

        private static void WriteFrame(NamedPipeClientStream stream, int opcode, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            byte[] packet = new byte[8 + bytes.Length];

            BitConverter.GetBytes(opcode).CopyTo(packet, 0);
            BitConverter.GetBytes(bytes.Length).CopyTo(packet, 4);
            bytes.CopyTo(packet, 8);

            stream.Write(packet, 0, packet.Length);
            stream.Flush();
        }
    }
}
