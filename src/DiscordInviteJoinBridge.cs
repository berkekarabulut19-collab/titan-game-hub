using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace TitanGameHub
{
    public class DiscordPartyDetails
    {
        public string PartyId { get; set; }
        public int CurrentSize { get; set; }
        public int MaxSize { get; set; }
        public string JoinSecret { get; set; }
        public string InviteUrl { get; set; }
    }

    /// <summary>
    /// Discord Rich Presence 1-Click "Join Game" & URI Protocol Handler Bridge:
    /// Registers the 'titan://' protocol in Windows and synchronizes party secrets with Discord RPC.
    /// When friends click "Join Game" in Discord or click a titan:// link,
    /// the hub automatically launches the game, connects to Spacewar 480, and attaches to the lobby.
    /// </summary>
    public static class DiscordInviteJoinBridge
    {
        private const string ProtocolScheme = "titan";

        /// <summary>
        /// Registers 'titan://' URI protocol handler in user registry.
        /// </summary>
        public static bool RegisterProtocolHandler(out string error)
        {
            error = "";
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + ProtocolScheme))
                {
                    if (key != null)
                    {
                        key.SetValue("", "URL:Titan Game Hub Protocol");
                        key.SetValue("URL Protocol", "");

                        using (RegistryKey shellKey = key.CreateSubKey("shell\\open\\command"))
                        {
                            if (shellKey != null)
                            {
                                shellKey.SetValue("", string.Format("\"{0}\" \"%1\"", exePath));
                            }
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return false;
        }

        public static string GenerateJoinUri(string roomCode, string ip, int port, string gameId)
        {
            return string.Format("titan://join?room={0}&ip={1}&port={2}&game={3}",
                Uri.EscapeDataString(roomCode ?? "TITAN-TR"),
                Uri.EscapeDataString(ip ?? "127.0.0.1"),
                port,
                Uri.EscapeDataString(gameId ?? "480"));
        }

        public static bool ParseJoinUri(string uri, out string roomCode, out string ip, out int port, out string gameId)
        {
            roomCode = "TITAN-TR";
            ip = "127.0.0.1";
            port = 7777;
            gameId = "480";

            if (string.IsNullOrEmpty(uri) || !uri.StartsWith("titan://", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                Uri u = new Uri(uri);
                string query = u.Query.TrimStart('?');
                string[] parts = query.Split('&');

                for (int i = 0; i < parts.Length; i++)
                {
                    string[] kv = parts[i].Split('=');
                    if (kv.Length == 2)
                    {
                        string k = kv[0].ToLowerInvariant();
                        string v = Uri.UnescapeDataString(kv[1]);

                        if (k == "room") roomCode = v;
                        else if (k == "ip") ip = v;
                        else if (k == "port") int.TryParse(v, out port);
                        else if (k == "game") gameId = v;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
