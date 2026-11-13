using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    public class FleetSyncGameItem
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string Version { get; set; }
        public string ArchivePath { get; set; }
        public long FileSizeBytes { get; set; }
    }

    /// <summary>
    /// Master-to-Fleet P2P Swarm & Remote Command Sync Engine:
    /// Enables Master PC to serve game update packages directly over local network / ZeroVpn mesh at 100 MB/s,
    /// and listens for remote OTA dispatch commands to trigger instant multi-client updates.
    /// </summary>
    public static class FleetSyncService
    {
        private static readonly Dictionary<string, FleetSyncGameItem> _sharedSwarmGames = new Dictionary<string, FleetSyncGameItem>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterGameForSwarm(GameProfile gp, string archivePath)
        {
            if (gp == null) return;
            string targetPath = archivePath;

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                // Check if an archive exists in downloads
                string gamesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games");
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string safeName = Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim();

                string[] searchDirs = new string[] { downloads, gamesRoot };
                foreach (string dir in searchDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    try
                    {
                        string[] matches = Directory.GetFiles(dir, safeName + "*.*");
                        foreach (string m in matches)
                        {
                            string ext = Path.GetExtension(m).ToLowerInvariant();
                            if (ext == ".zip" || ext == ".rar" || ext == ".7z")
                            {
                                targetPath = m;
                                break;
                            }
                        }
                    }
                    catch { }
                    if (!string.IsNullOrEmpty(targetPath)) break;
                }
            }

            if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
            {
                FileInfo fi = new FileInfo(targetPath);
                lock (_sharedSwarmGames)
                {
                    _sharedSwarmGames[gp.Id] = new FleetSyncGameItem
                    {
                        GameId = gp.Id,
                        GameName = gp.Name,
                        Version = gp.Version ?? "v1.0",
                        ArchivePath = targetPath,
                        FileSizeBytes = fi.Length
                    };
                }
            }
        }

        public static bool TryGetSwarmGame(string gameId, out FleetSyncGameItem item)
        {
            item = null;
            lock (_sharedSwarmGames)
            {
                return _sharedSwarmGames.TryGetValue(gameId, out item);
            }
        }

        public static List<FleetSyncGameItem> GetAllSwarmGames()
        {
            lock (_sharedSwarmGames)
            {
                return new List<FleetSyncGameItem>(_sharedSwarmGames.Values);
            }
        }

        public static string GetP2pSwarmUrl(GameProfile gp)
        {
            if (gp == null) return "";
            string localIp = GetMeshOrLocalIp();
            return string.Format("http://{0}:48085/api/p2p_swarm/download?id={1}", localIp, gp.Id);
        }

        public static string GetMeshOrLocalIp()
        {
            try
            {
                // If Virtual LAN mesh IP is active, prefer it
                if (!string.IsNullOrEmpty(VirtualLanMeshService.ActiveVirtualIp))
                {
                    return VirtualLanMeshService.ActiveVirtualIp;
                }

                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    if (endPoint != null) return endPoint.Address.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public static bool ParseAndExecuteRemoteDispatch(
            string otaMessage,
            List<GameProfile> catalog,
            out string promptTitle,
            out string promptMessage,
            out GameProfile targetProfile)
        {
            promptTitle = "";
            promptMessage = "";
            targetProfile = null;

            if (string.IsNullOrEmpty(otaMessage) || !otaMessage.StartsWith("OTA#FLEET_UPDATE_DISPATCH#", StringComparison.Ordinal))
                return false;

            try
            {
                string[] parts = otaMessage.Split('#');
                if (parts.Length < 8) return false;

                string gId = parts[2];
                string gName = parts[3];
                string gNewVer = parts[4];
                string gDlUrl = parts[5];
                string gDirect = parts[6];
                string gSender = parts[7];
                string gP2pUrl = parts.Length >= 9 ? parts[8] : "";

                // Find matching game profile in local catalog
                lock (catalog)
                {
                    foreach (GameProfile gp in catalog)
                    {
                        if (gp.Id == gId || gp.Name.Equals(gName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetProfile = gp;
                            break;
                        }
                    }
                }

                if (targetProfile == null) return false;

                // Update version metadata
                targetProfile.RemoteVersion = gNewVer;
                if (!string.IsNullOrEmpty(gP2pUrl))
                {
                    targetProfile.DirectMirrorUrl = gP2pUrl;
                }
                else if (!string.IsNullOrEmpty(gDirect))
                {
                    targetProfile.DirectMirrorUrl = gDirect;
                }

                if (!string.IsNullOrEmpty(gDlUrl) && string.IsNullOrEmpty(targetProfile.DownloadUrl))
                {
                    targetProfile.DownloadUrl = gDlUrl;
                }

                promptTitle = "🚀 TİTAN FİLO GÜNCELLEME EMRİ";
                promptMessage = string.Format(
                    "Takım lideri ({0}) '{1}' oyunu için yeni {2} güncellemesini yayınladı!\n\n" +
                    "🛡 Titan Güncelleme Kalkanı devrede: Mevcut save'leriniz, Türkçe yamalarınız ve modlarınız %100 korunacak.\n\n" +
                    "Şimdi güncelleme indirilsin mi?",
                    gSender, targetProfile.Name, gNewVer);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
