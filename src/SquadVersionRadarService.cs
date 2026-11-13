using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    public class SquadPeerVersionInfo
    {
        public string UserName { get; set; }
        public string InstalledVersion { get; set; }
        public bool IsUpToDate { get; set; }
        public bool IsInGame { get; set; }
        public string StatusText { get; set; }
    }

    public class SquadVersionScanResult
    {
        public bool AllSynced { get; set; }
        public int TotalPeers { get; set; }
        public int SyncedPeersCount { get; set; }
        public int OutdatedPeersCount { get; set; }
        public List<SquadPeerVersionInfo> Peers { get; set; }
        public string DiagnosticSummary { get; set; }

        public SquadVersionScanResult()
        {
            Peers = new List<SquadPeerVersionInfo>();
        }
    }

    /// <summary>
    /// Squad Version Parity Radar & Squad Fleet Dispatch:
    /// Scans connected friends/lobby members for game version mismatches.
    /// Prevents co-op multiplayer connection failures due to version disparity.
    /// Enables 1-click broadcast of update packages and P2P LAN/Mesh swarm links.
    /// </summary>
    public static class SquadVersionRadarService
    {
        private const string OtaChannelUrl = "https://ntfy.sh/titan_hub_ota_sync_v5";

        public static SquadVersionScanResult AnalyzeSquadVersionParity(GameProfile gp, List<FriendPresence> friends)
        {
            SquadVersionScanResult result = new SquadVersionScanResult();
            if (gp == null)
            {
                result.DiagnosticSummary = "Oyun seçilmedi.";
                return result;
            }

            string targetVer = string.IsNullOrEmpty(gp.RemoteVersion) ? (gp.Version ?? "v1.0") : gp.RemoteVersion;
            targetVer = targetVer.Trim();

            if (friends == null || friends.Count == 0)
            {
                result.AllSynced = true;
                result.DiagnosticSummary = "👥 Takım Radarı: Lobi / Arkadaş listesinde çevrim içi kimse yok (Tek oyunculu hazır).";
                return result;
            }

            foreach (FriendPresence fp in friends)
            {
                if (fp == null || !fp.IsOnline) continue;

                SquadPeerVersionInfo info = new SquadPeerVersionInfo
                {
                    UserName = fp.UserName,
                    InstalledVersion = string.IsNullOrEmpty(fp.GameVersion) ? "Bilinmiyor" : fp.GameVersion.Trim(),
                    IsInGame = string.Equals(fp.ActiveGame, gp.Name, StringComparison.OrdinalIgnoreCase)
                };

                bool matches = string.Equals(info.InstalledVersion, targetVer, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(info.InstalledVersion, gp.Version, StringComparison.OrdinalIgnoreCase);

                info.IsUpToDate = matches;
                if (matches)
                {
                    info.StatusText = "✅ Sürüm Eşit (" + info.InstalledVersion + ")";
                    result.SyncedPeersCount++;
                }
                else
                {
                    info.StatusText = "⚠️ Eski Sürüm (" + info.InstalledVersion + " -> " + targetVer + " gerekli)";
                    result.OutdatedPeersCount++;
                }

                result.Peers.Add(info);
                result.TotalPeers++;
            }

            if (result.OutdatedPeersCount == 0)
            {
                result.AllSynced = true;
                result.DiagnosticSummary = string.Format("✅ Takım Sürümleri Uyumlu ({0} Oyuncu): Herkes {1} sürümünde, lobiye hazır!", result.TotalPeers, targetVer);
            }
            else
            {
                result.AllSynced = false;
                List<string> names = new List<string>();
                foreach (SquadPeerVersionInfo p in result.Peers)
                {
                    if (!p.IsUpToDate) names.Add(p.UserName + " (" + p.InstalledVersion + ")");
                }
                result.DiagnosticSummary = string.Format(
                    "⚠️ SÜRÜM UYUŞMAZLIĞI: {0} kişi güncel değil [{1}]. Çok oyunculu lobi kurulamayabilir! 'Takımı Güncelle' ile tek tıkla güncelleme gönderin.",
                    result.OutdatedPeersCount, string.Join(", ", names.ToArray()));
            }

            return result;
        }

        public static void BroadcastSquadUpdateCommandAsync(GameProfile gp, string p2pSwarmUrl, Action<string> onStatusCallback)
        {
            if (gp == null) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (onStatusCallback != null)
                        onStatusCallback("📡 Filo Güncelleme Emri takımdaki tüm uzak PC'lere yayınlanıyor...");

                    string targetVer = string.IsNullOrEmpty(gp.RemoteVersion) ? (gp.Version ?? "v1.0") : gp.RemoteVersion;
                    string payload = string.Join("#", new string[]
                    {
                        "OTA",
                        "FLEET_UPDATE_DISPATCH",
                        gp.Id ?? "custom",
                        (gp.Name ?? "Game").Replace("#", ""),
                        targetVer.Replace("#", ""),
                        (gp.DownloadUrl ?? "").Replace("#", ""),
                        (gp.DirectMirrorUrl ?? "").Replace("#", ""),
                        Environment.UserName.Replace("#", ""),
                        (p2pSwarmUrl ?? "").Replace("#", "")
                    });

                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers.Add("User-Agent", "TitanGameHub/8.0");
                        wc.Headers.Add("Title", "FLEET_DISPATCH#" + gp.Name);
                        wc.UploadString(OtaChannelUrl, "POST", payload);
                    }

                    if (onStatusCallback != null)
                        onStatusCallback(string.Format("✅ '{0}' v{1} güncelleme sinyali tüm uzaktaki takım üyelerine başarıyla gönderildi!", gp.Name, targetVer));
                }
                catch (Exception ex)
                {
                    if (onStatusCallback != null)
                        onStatusCallback("Filo yayın hatası: " + ex.Message);
                }
            });
        }
    }
}
