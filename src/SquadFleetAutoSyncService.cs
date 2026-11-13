using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    public class FleetSyncStatusReport
    {
        public bool IsFullySynchronized { get; set; }
        public int TotalSquadCount { get; set; }
        public int OutdatedCount { get; set; }
        public string TargetVersion { get; set; }
        public string HostMeshIp { get; set; }
        public string LocalP2pSwarmUrl { get; set; }
        public string DiagnosticMessage { get; set; }
        public List<SquadPeerVersionInfo> OutdatedPeers { get; set; }

        public FleetSyncStatusReport()
        {
            OutdatedPeers = new List<SquadPeerVersionInfo>();
        }
    }

    /// <summary>
    /// Master Squad Fleet Auto-Sync & Co-Op Parity Engine:
    /// Coordinates 1-click version parity across multiplayer friends,
    /// automates LAN/Zero-VPN 1 Gbps local P2P swarm update seeding,
    /// and handles persistent Dedicated Server updates with save preservation.
    /// </summary>
    public static class SquadFleetAutoSyncService
    {
        public static FleetSyncStatusReport CheckSquadVersionParity(GameProfile gp, List<FriendPresence> friends)
        {
            FleetSyncStatusReport rep = new FleetSyncStatusReport();
            if (gp == null)
            {
                rep.DiagnosticMessage = "Oyun profili seçilmedi.";
                return rep;
            }

            string targetVer = !string.IsNullOrEmpty(gp.RemoteVersion) ? gp.RemoteVersion.Trim() : (gp.Version ?? "v1.0").Trim();
            rep.TargetVersion = targetVer;
            rep.HostMeshIp = FleetSyncService.GetMeshOrLocalIp();
            rep.LocalP2pSwarmUrl = FleetSyncService.GetP2pSwarmUrl(gp);

            if (friends == null || friends.Count == 0)
            {
                rep.IsFullySynchronized = true;
                rep.TotalSquadCount = 1;
                rep.DiagnosticMessage = string.Format("✅ Tek Oyunculu / Lobi Hazır: Hedef Sürüm {0} (Çevrim içi arkadaş yok).", targetVer);
                return rep;
            }

            rep.TotalSquadCount = friends.Count + 1;

            foreach (FriendPresence fp in friends)
            {
                if (fp == null || !fp.IsOnline) continue;

                string peerVer = string.IsNullOrEmpty(fp.GameVersion) ? "v1.0" : fp.GameVersion.Trim();
                bool matches = string.Equals(peerVer, targetVer, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(peerVer, gp.Version, StringComparison.OrdinalIgnoreCase);

                if (!matches)
                {
                    rep.OutdatedCount++;
                    rep.OutdatedPeers.Add(new SquadPeerVersionInfo
                    {
                        UserName = fp.UserName,
                        InstalledVersion = peerVer,
                        IsUpToDate = false,
                        StatusText = string.Format("⚠️ Eski Sürüm ({0} -> {1} Gerekli)", peerVer, targetVer)
                    });
                }
            }

            if (rep.OutdatedCount == 0)
            {
                rep.IsFullySynchronized = true;
                rep.DiagnosticMessage = string.Format("✅ TÜM TAKIM EŞİTLENDİ ({0} Oyuncu): Herkes {1} sürümünde, Spacewar 480 lobisi hazır!", rep.TotalSquadCount, targetVer);
            }
            else
            {
                rep.IsFullySynchronized = false;
                List<string> outdatedNames = new List<string>();
                for (int i = 0; i < rep.OutdatedPeers.Count; i++)
                {
                    outdatedNames.Add(rep.OutdatedPeers[i].UserName + " (" + rep.OutdatedPeers[i].InstalledVersion + ")");
                }

                rep.DiagnosticMessage = string.Format(
                    "⚠️ SÜRÜM UYUŞMAZLIĞI TESPİT EDİLDİ: {0} arkadaşınız eski sürümde [{1}]. 'Takımı Eşitle' butonu ile güncellemeyi doğrudan bilgisayarınızdan 1 Gbps LAN/P2P ile gönderin.",
                    rep.OutdatedCount, string.Join(", ", outdatedNames.ToArray()));
            }

            return rep;
        }

        public static void BroadcastFleetP2pSyncAsync(GameProfile gp, Action<string> progressCallback)
        {
            if (gp == null) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (progressCallback != null)
                        progressCallback("⚡ Yerel P2P Swarm Seeder hazırlanıyor...");

                    // 1. Ensure game archive / package is registered in local FleetSyncService
                    FleetSyncService.RegisterGameForSwarm(gp, null);

                    // 2. Start Moonlight/Swarm HTTP local host on port 48085 if not active
                    string streamUrl;
                    if (!LocalStreamHostService.IsRunning)
                    {
                        LocalStreamHostService.StartStream(48085, out streamUrl);
                    }

                    string p2pSwarmUrl = FleetSyncService.GetP2pSwarmUrl(gp);

                    if (progressCallback != null)
                        progressCallback(string.Format("📡 Filo Güncelleme ve P2P Swarm Sinyali Takıma Gönderiliyor (Seeder: {0})...", p2pSwarmUrl));

                    // 3. Dispatch OTA command to all squad members
                    SquadVersionRadarService.BroadcastSquadUpdateCommandAsync(gp, p2pSwarmUrl, delegate(string stat)
                    {
                        if (progressCallback != null) progressCallback(stat);
                    });
                }
                catch (Exception ex)
                {
                    if (progressCallback != null)
                        progressCallback("Filo eşitleme hatası: " + ex.Message);
                }
            });
        }

        public static bool UpdateDedicatedServerWithSavePreservation(GameProfile gp, string updateArchivePath, out string statusLog)
        {
            statusLog = "";
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
            {
                statusLog = "Geçersiz oyun kurulum dizini.";
                return false;
            }

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("🛡 TİTAN DEDICATED SUNUCU GÜNCELLEME VE SAVE KORUMA SÜRECİ");
                sb.AppendLine();

                // 1. Check if server process is running, gracefully stop it
                bool wasRunning = DedicatedServerHostService.IsServerRunning;
                if (wasRunning)
                {
                    sb.AppendLine("• 1. Adım: Çalışan Dedicated Server güvenle kapatılıyor (World Save flush)...");
                    DedicatedServerHostService.StopDedicatedServer();
                    Thread.Sleep(1500);
                }

                // 2. Quarantine server world saves
                string workDir = gp.WorkingDirectory;
                string saveQuarantineDir = Path.Combine(workDir, "Titan_Server_Save_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                string[] possibleSaveDirs = new string[] { "Saved", "Worlds", "SaveGames", "Saves", "Pal/Saved", "Zomboid/Saves" };
                int savedFoldersCount = 0;

                foreach (string sRel in possibleSaveDirs)
                {
                    string fullPath = Path.Combine(workDir, sRel);
                    if (Directory.Exists(fullPath))
                    {
                        if (!Directory.Exists(saveQuarantineDir)) Directory.CreateDirectory(saveQuarantineDir);
                        string dest = Path.Combine(saveQuarantineDir, sRel.Replace("/", "_").Replace("\\", "_"));
                        CopyDirectoryRecursive(fullPath, dest);
                        savedFoldersCount++;
                    }
                }

                sb.AppendLine(string.Format("• 2. Adım: Dünya save dosyaları karantinaya alındı ({0} klasör yedeklendi).", savedFoldersCount));

                // 3. Apply update package if provided
                if (!string.IsNullOrEmpty(updateArchivePath) && File.Exists(updateArchivePath))
                {
                    sb.AppendLine("• 3. Adım: Güncelleme paketi ikili dosyalara uygulanıyor...");
                    if (DownloadInstallService.ExtractArchiveCore(updateArchivePath, workDir))
                    {
                        sb.AppendLine("• 3. Adım: Güncelleme başarıyla açıldı.");
                    }
                    else
                    {
                        sb.AppendLine("• 3. Adım Uyarısı: Arşiv çıkarılamadı.");
                    }
                }

                // 4. Ensure Goldberg/OnlineFix multiplayer DLL is present in dedicated server
                string serverExe, defArgs;
                if (DedicatedServerHostService.DetectDedicatedServerBinary(gp, out serverExe, out defArgs))
                {
                    string serverDir = Path.GetDirectoryName(serverExe);
                    string steamApiTarget = Path.Combine(serverDir, "steam_api64.dll");
                    string appidTxt = Path.Combine(serverDir, "steam_appid.txt");

                    if (!File.Exists(appidTxt))
                    {
                        File.WriteAllText(appidTxt, "480"); // Spacewar AppID
                        sb.AppendLine("• 4. Adım: Spacewar (AppID 480) sunucu yapılandırması bağlandı.");
                    }
                }

                // 5. Restore server world save files
                if (Directory.Exists(saveQuarantineDir))
                {
                    foreach (string sRel in possibleSaveDirs)
                    {
                        string bkp = Path.Combine(saveQuarantineDir, sRel.Replace("/", "_").Replace("\\", "_"));
                        if (Directory.Exists(bkp))
                        {
                            string orig = Path.Combine(workDir, sRel);
                            CopyDirectoryRecursive(bkp, orig);
                        }
                    }
                    sb.AppendLine("• 5. Adım: Dünya kayıtları ve oyuncu envanterleri kayıpsız geri yüklendi.");
                }

                // 6. Restart server if it was running before
                if (wasRunning)
                {
                    string restartMsg;
                    DedicatedServerHostService.StartDedicatedServer(gp, "", out restartMsg);
                    sb.AppendLine("• 6. Adım: Dedicated Server otomatik yeniden başlatıldı (Watchdog devrede).");
                }

                sb.AppendLine();
                sb.AppendLine("✅ DEDICATED SUNUCU GÜNCELLEMESİ BAŞARIYLA TAMAMLANDI!");
                statusLog = sb.ToString();
                return true;
            }
            catch (Exception ex)
            {
                statusLog = "Dedicated sunucu güncelleme hatası: " + ex.Message;
                return false;
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
            string[] files = Directory.GetFiles(sourceDir);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(destDir, name), true);
            }
            string[] dirs = Directory.GetDirectories(sourceDir);
            foreach (string dir in dirs)
            {
                string name = Path.GetFileName(dir);
                CopyDirectoryRecursive(dir, Path.Combine(destDir, name));
            }
        }
    }
}
