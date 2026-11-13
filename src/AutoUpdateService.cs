using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    public class AutoUpdateService
    {
        public const string CurrentAppVersion = "v8.1 STEAM";
        private const string OtaChannelUrl = "https://ntfy.sh/titan_hub_ota_sync_v5";

        private Timer _pollTimer;
        private readonly HashSet<string> _processedMsgIds = new HashSet<string>();
        private readonly long _localExeTimestamp;
        private bool _isUpdatingBinary = false;

        public event Action<string, int> OnOtaStatusChanged;
        public event Action<GameProfile> OnGameRemoteUpdated;
        public event Action<string, string, GameProfile> OnFleetCommandReceived;

        public AutoUpdateService()
        {
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                _localExeTimestamp = File.GetLastWriteTimeUtc(exePath).Ticks / TimeSpan.TicksPerSecond;
            }
            catch
            {
                _localExeTimestamp = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            }
        }

        public void Start(List<GameProfile> catalog)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            _pollTimer = new Timer(delegate { CheckForUpdates(catalog, false); }, null, 2000, 25000);
        }

        public void Stop()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Dispose();
                _pollTimer = null;
            }
        }

        public void CheckForUpdatesAsync(List<GameProfile> catalog, bool manualTrigger)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                CheckForUpdates(catalog, manualTrigger);
            });
        }

        private void CheckForUpdates(List<GameProfile> catalog, bool manualTrigger)
        {
            if (_isUpdatingBinary) return;

            if (manualTrigger && OnOtaStatusChanged != null)
                OnOtaStatusChanged("Bulut OTA sunucusundan uygulama ve katalog güncellemeleri denetleniyor...", 0);

            int updatedCount = 0;
            string newestBinaryUrl = null;
            string newestBinarySha = null;
            string newestBinarySig = null;
            long newestBinaryTs = 0;
            long newestBinaryTime = _localExeTimestamp + 45; // Require remote binary to be at least 45s newer than local build

            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers.Add("User-Agent", "TitanGameHub/7.0");
                    string raw = wc.DownloadString(OtaChannelUrl + "/json?poll=1&since=48h");
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        string[] lines = raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines)
                        {
                            string id = ExtractJsonField(line, "id");
                            if (!string.IsNullOrEmpty(id))
                            {
                                lock (_processedMsgIds)
                                {
                                    if (_processedMsgIds.Contains(id)) continue;
                                    _processedMsgIds.Add(id);
                                }
                            }

                            string msg = ExtractJsonField(line, "message");

                            // 1. Check if this message is a Full Application Binary OTA Upgrade (BIN_OTA#<Timestamp>)
                            // FAIL-CLOSED: the package is only accepted together with a valid RSA-2048 signature
                            // manifest (BIN_MANIFEST#ts#sha256#sig). Public topic => unsigned packages are hostile.
                            string title = ExtractJsonField(line, "title");
                            if (!string.IsNullOrEmpty(title) && title.StartsWith("BIN_OTA#", StringComparison.Ordinal))
                            {
                                string[] tp = title.Split('#');
                                long remoteTs;
                                if (tp.Length >= 2 && long.TryParse(tp[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out remoteTs))
                                {
                                    string attachUrl = ExtractAttachmentUrl(line);
                                    long manTs;
                                    string manSha;
                                    string manSig;
                                    string sigStatus;
                                    bool manifestOk = OtaSignatureVerifier.TryParseManifest(msg, out manTs, out manSha, out manSig) &&
                                                      OtaSignatureVerifier.VerifySignature(manTs, manSha, manSig, out sigStatus);

                                    if (!manifestOk)
                                    {
                                        if (OnOtaStatusChanged != null)
                                            OnOtaStatusChanged("⛔ İmzasız/geçersiz OTA paketi reddedildi (yayıncı doğrulanamadı). Uygulama değiştirilmedi.", 0);
                                    }
                                    else if (!string.IsNullOrEmpty(attachUrl) && remoteTs > newestBinaryTime)
                                    {
                                        newestBinaryTime = remoteTs;
                                        newestBinaryTs = remoteTs;
                                        newestBinaryUrl = attachUrl;
                                        newestBinarySha = manSha;
                                        newestBinarySig = manSig;
                                    }
                                }
                            }

                            // 2. Check if this message is a Game Catalog OTA Sync
                            if (string.IsNullOrEmpty(msg) || !msg.StartsWith("OTA#", StringComparison.Ordinal)) continue;

                            string[] p = msg.Split('#');
                            if (p.Length >= 2 && p[1] == "FLEET_UPDATE_DISPATCH")
                            {
                                string pTitle, pMsg;
                                GameProfile targetGp;
                                if (FleetSyncService.ParseAndExecuteRemoteDispatch(msg, catalog, out pTitle, out pMsg, out targetGp))
                                {
                                    updatedCount++;
                                    if (OnFleetCommandReceived != null)
                                        OnFleetCommandReceived(pTitle, pMsg, targetGp);
                                    if (OnGameRemoteUpdated != null)
                                        OnGameRemoteUpdated(targetGp);
                                }
                                continue;
                            }

                            if (p.Length >= 10 && p[1] == "GAME_SYNC")
                            {
                                string gId = p[2];
                                string gName = p[3];
                                string gCat = p[4];
                                string gVer = p[5];
                                string gDl = p[6];
                                string gDirect = p[7];
                                string gAppId = p[8];
                                string gMaxP = p[9];
                                string gSize = p.Length >= 11 ? p[10] : "3.0 GB";

                                GameProfile existing = null;
                                lock (catalog)
                                {
                                    foreach (GameProfile item in catalog)
                                    {
                                        if (item.Id == gId || item.Name.Equals(gName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            existing = item;
                                            break;
                                        }
                                    }

                                    if (existing != null)
                                    {
                                        bool changed = false;
                                        if (!string.IsNullOrEmpty(gVer) && existing.RemoteVersion != gVer)
                                        {
                                            existing.RemoteVersion = gVer;
                                            existing.Version = gVer;
                                            changed = true;
                                        }
                                        if (!string.IsNullOrEmpty(gDirect) && existing.DirectMirrorUrl != gDirect)
                                        {
                                            existing.DirectMirrorUrl = gDirect;
                                            changed = true;
                                        }
                                        if (!string.IsNullOrEmpty(gDl) && existing.DownloadUrl != gDl)
                                        {
                                            existing.DownloadUrl = gDl;
                                            changed = true;
                                        }
                                        if (changed)
                                        {
                                            updatedCount++;
                                            if (OnGameRemoteUpdated != null) OnGameRemoteUpdated(existing);
                                        }
                                    }
                                    else
                                    {
                                        string header = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + gAppId + "/header.jpg";
                                        GameProfile newGp = new GameProfile
                                        {
                                            Id = gId,
                                            Name = gName,
                                            Category = gCat,
                                            ExecutablePath = "",
                                            WorkingDirectory = "",
                                            Version = gVer,
                                            RemoteVersion = gVer,
                                            DownloadUrl = gDl,
                                            OnlineFixUrl = "https://online-fix.me/index.php?do=search&subaction=search&story=" + Uri.EscapeDataString(gName),
                                            SteamAppId = gAppId,
                                            Description = gName + " — Bulut OTA güncellemesi ile uzaktan kataloğa eklendi.",
                                            MaxPlayers = gMaxP,
                                            BannerUrl = header,
                                            CapsuleUrl = header,
                                            FileSize = gSize,
                                            DirectMirrorUrl = gDirect,
                                            IsFavorite = false
                                        };
                                        catalog.Add(newGp);
                                        updatedCount++;
                                        if (OnGameRemoteUpdated != null) OnGameRemoteUpdated(newGp);
                                    }
                                }
                            }
                        }
                    }
                }

                if (updatedCount > 0)
                {
                    ProfileStore.SaveProfiles(catalog);
                    if (OnOtaStatusChanged != null)
                        OnOtaStatusChanged(string.Format("Bulut OTA: {0} oyun/sunucu güncellemesi otomatik uygulandı! ({1})", updatedCount, CurrentAppVersion), updatedCount);
                }
                else if (manualTrigger && string.IsNullOrEmpty(newestBinaryUrl) && OnOtaStatusChanged != null)
                {
                    OnOtaStatusChanged("Uygulama ve Katalog Tamamen Güncel (" + CurrentAppVersion + " • Küresel Bulut OTA Aktif)", 0);
                }

                // If a newer, cryptographically signed full application package was pushed from the master PC,
                // download, verify and hot-swap automatically.
                if (!string.IsNullOrEmpty(newestBinaryUrl) && !string.IsNullOrEmpty(newestBinarySha) && !string.IsNullOrEmpty(newestBinarySig))
                {
                    ApplyRemoteBinaryUpdate(newestBinaryUrl, catalog, newestBinaryTs, newestBinarySha, newestBinarySig);
                }
            }
            catch
            {
                if (manualTrigger && OnOtaStatusChanged != null)
                    OnOtaStatusChanged("Yerel katalog aktif (" + CurrentAppVersion + "). Bulut OTA arka planda hazır.", 0);
            }
        }

        private static volatile bool _pendingAppHotSwap = false;
        public static bool HasPendingAppHotSwap { get { return _pendingAppHotSwap; } }

        public static bool IsAnyGameCurrentlyPlaying(List<GameProfile> catalog)
        {
            if (catalog == null) return false;
            foreach (GameProfile p in catalog)
            {
                if (p != null && p.IsInstalled && SmartUpdateShieldService.IsGameProcessRunning(p))
                    return true;
            }
            return false;
        }

        public static void ExecutePendingAppHotSwapIfReady()
        {
            if (!_pendingAppHotSwap) return;
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string stageDir = Path.Combine(appDir, "ota_stage");
            string stagedExe = Path.Combine(stageDir, "TitanGameHub.exe");
            if (!File.Exists(stagedExe))
            {
                _pendingAppHotSwap = false;
                return;
            }

            try
            {
                string batPath = Path.Combine(appDir, "ota_swap.bat");
                string currentExe = Assembly.GetExecutingAssembly().Location;
                int currentPid = Process.GetCurrentProcess().Id;

                StringBuilder bat = new StringBuilder();
                bat.AppendLine("@echo off");
                bat.AppendLine("ping 127.0.0.1 -n 2 >nul");
                bat.AppendLine("taskkill /f /pid " + currentPid + " >nul 2>&1");
                bat.AppendLine("ping 127.0.0.1 -n 2 >nul");
                bat.AppendLine("xcopy /y /e \"" + stageDir + "\\*\" \"" + appDir + "\" >nul 2>&1");
                bat.AppendLine("rmdir /s /q \"" + stageDir + "\" >nul 2>&1");
                bat.AppendLine("start \"\" \"" + currentExe + "\"");
                bat.AppendLine("del \"%~f0\"");

                File.WriteAllText(batPath, bat.ToString(), Encoding.Default);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = batPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = appDir
                };
                Process.Start(psi);
                Environment.Exit(0);
            }
            catch
            {
                _pendingAppHotSwap = false;
            }
        }

        private void ApplyRemoteBinaryUpdate(string zipUrl, List<GameProfile> catalog, long manifestTimestamp, string expectedSha256, string signatureBase64)
        {
            if (_isUpdatingBinary) return;
            _isUpdatingBinary = true;

            string verifyStatus = "";

            try
            {
                // Gate 0: transport + signature must hold before a single byte is written to disk.
                if (string.IsNullOrEmpty(zipUrl) || !zipUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    _isUpdatingBinary = false;
                    if (OnOtaStatusChanged != null)
                        OnOtaStatusChanged("⛔ OTA paketi HTTPS olmadığı için reddedildi.", 0);
                    return;
                }

                if (!OtaSignatureVerifier.VerifySignature(manifestTimestamp, expectedSha256, signatureBase64, out verifyStatus))
                {
                    _isUpdatingBinary = false;
                    if (OnOtaStatusChanged != null)
                        OnOtaStatusChanged("⛔ " + verifyStatus + " Uygulama değiştirilmedi.", 0);
                    return;
                }

                if (OnOtaStatusChanged != null)
                    OnOtaStatusChanged("🚀 İmzalı yeni sürüm algılandı, doğrulanıyor ve indiriliyor...", 1);

                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string stageDir = Path.Combine(appDir, "ota_stage");
                string stageZip = Path.Combine(appDir, "ota_update_pkg.zip");

                if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true);
                if (File.Exists(stageZip)) File.Delete(stageZip);
                Directory.CreateDirectory(stageDir);

                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "TitanGameHub/7.0");
                    wc.DownloadFile(zipUrl, stageZip);
                }

                // Gate 1: signed hash over the actual downloaded bytes.
                if (!OtaSignatureVerifier.VerifyPackageHash(stageZip, expectedSha256, out verifyStatus))
                {
                    try { if (File.Exists(stageZip)) File.Delete(stageZip); } catch { }
                    try { if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true); } catch { }
                    _isUpdatingBinary = false;
                    if (OnOtaStatusChanged != null)
                        OnOtaStatusChanged("⛔ " + verifyStatus + " Paket silindi, mevcut sürüm korundu.", 0);
                    return;
                }

                ZipFile.ExtractToDirectory(stageZip, stageDir);
                File.Delete(stageZip);

                string stagedExe = Path.Combine(stageDir, "TitanGameHub.exe");
                if (File.Exists(stagedExe))
                {
                    // Kesintisiz Oyun Koruma Kalkanı:
                    // Eğer kullanıcı şu an herhangi bir oyunu oynuyorsa, Titan Game Hub'ı ASLA kapatma!
                    // Oyundan çıktığı anda GameExitSaveSyncWatcher tarafından otomatik uygulat.
                    if (IsAnyGameCurrentlyPlaying(catalog))
                    {
                        _pendingAppHotSwap = true;
                        _isUpdatingBinary = false;
                        if (OnOtaStatusChanged != null)
                            OnOtaStatusChanged("🛡️ Oyun oynanıyor! Yeni Titan Game Hub sürümü arka planda hazırlandı. Oyundan çıktığınız anda kesintisiz uygulanacak.", 1);
                        return;
                    }

                    string batPath = Path.Combine(appDir, "ota_swap.bat");
                    string currentExe = Assembly.GetExecutingAssembly().Location;
                    int currentPid = Process.GetCurrentProcess().Id;

                    StringBuilder bat = new StringBuilder();
                    bat.AppendLine("@echo off");
                    bat.AppendLine("ping 127.0.0.1 -n 2 >nul");
                    bat.AppendLine("taskkill /f /pid " + currentPid + " >nul 2>&1");
                    bat.AppendLine("ping 127.0.0.1 -n 2 >nul");
                    bat.AppendLine("xcopy /y /e \"" + stageDir + "\\*\" \"" + appDir + "\" >nul 2>&1");
                    bat.AppendLine("rmdir /s /q \"" + stageDir + "\" >nul 2>&1");
                    bat.AppendLine("start \"\" \"" + currentExe + "\"");
                    bat.AppendLine("del \"%~f0\"");

                    File.WriteAllText(batPath, bat.ToString(), Encoding.Default);

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = batPath,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WorkingDirectory = appDir
                    };
                    Process.Start(psi);
                    Environment.Exit(0);
                }
            }
            catch
            {
                _isUpdatingBinary = false;
            }
        }

        public void PushFullApplicationBinaryToRemotePCsAsync(List<GameProfile> catalog)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (OnOtaStatusChanged != null)
                        OnOtaStatusChanged("🚀 Uygulama ve katalog paketlenip tüm istemcilere imzalı Bulut OTA ile gönderiliyor...", 0);

                    ProfileStore.SaveProfiles(catalog);
                    string appDir = AppDomain.CurrentDomain.BaseDirectory;
                    string tempStage = Path.Combine(Path.GetTempPath(), "TitanBinOta_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                    string tempZip = tempStage + ".zip";

                    if (Directory.Exists(tempStage))
                    {
                        try { Directory.Delete(tempStage, true); } catch { }
                    }
                    Directory.CreateDirectory(tempStage);

                    CopyDirectoryForOta(appDir, tempStage);

                    if (File.Exists(tempZip)) File.Delete(tempZip);
                    ZipFile.CreateFromDirectory(tempStage, tempZip);
                    try { Directory.Delete(tempStage, true); } catch { }

                    long nowTs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;

                    // Publisher side: refuse to broadcast an unsigned package.
                    string manifestMsg;
                    string signErr;
                    if (!OtaSignatureVerifier.TrySignPackage(tempZip, nowTs, out manifestMsg, out signErr))
                    {
                        try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                        if (OnOtaStatusChanged != null) OnOtaStatusChanged("⛔ " + signErr, 0);
                        return;
                    }

                    byte[] zipBytes = File.ReadAllBytes(tempZip);
                    try { File.Delete(tempZip); } catch { }

                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "TitanGameHub/7.0");
                        wc.Headers.Add("Filename", "TitanGameHub_RemoteUpdate.zip");
                        wc.Headers.Add("Title", "BIN_OTA#" + nowTs);
                        wc.Headers.Add("Message", manifestMsg);
                        wc.UploadData(OtaChannelUrl, "PUT", zipBytes);
                    }

                    if (OnOtaStatusChanged != null)
                        OnOtaStatusChanged("✅ İmzalı tam sürüm OTA gönderildi. Çalışan istemciler otomatik algılayıp (oyun açıksa çıkışta) güncellenecek.", 1);
                }
                catch (Exception ex)
                {
                    if (OnOtaStatusChanged != null)
                        OnOtaStatusChanged("Bulut OTA gönderim hatası: " + ex.Message, 0);
                }
            });
        }

        private static void CopyDirectoryForOta(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

            HashSet<string> skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "data", "games", "backups", "cache", "logs", "ota_stage", "profilebackups", "saves", "temp", "packages"
            };
            HashSet<string> skipFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ota_signing_key.xml", "ota_private_key.xml", "ota_swap.bat",
                "ota_update_pkg.zip", "startup_error.log", "app_crash.log"
            };

            string[] files = Directory.GetFiles(sourceDir);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string name = Path.GetFileName(file);
                if (skipFiles.Contains(name)) continue;

                string lower = name.ToLowerInvariant();
                if (lower.EndsWith(".log") || lower.EndsWith(".pdb")) continue;

                try
                {
                    File.Copy(file, Path.Combine(destDir, name), true);
                }
                catch { }
            }

            string[] dirs = Directory.GetDirectories(sourceDir);
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                string name = Path.GetFileName(dir);
                if (skipDirs.Contains(name)) continue;

                string lower = name.ToLowerInvariant();
                if (lower.StartsWith("titanbinota_", StringComparison.Ordinal)) continue;

                try
                {
                    CopyDirectoryForOta(dir, Path.Combine(destDir, name));
                }
                catch { }
            }
        }

        public void PushGameUpdateToFriendsAsync(GameProfile gp)
        {
            if (gp == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string payload = string.Join("#", new string[]
                    {
                        "OTA",
                        "GAME_SYNC",
                        gp.Id ?? "custom",
                        (gp.Name ?? "Game").Replace("#", ""),
                        (gp.Category ?? "Co-Op").Replace("#", ""),
                        (gp.Version ?? "v1.0").Replace("#", ""),
                        (gp.DownloadUrl ?? "https://steamrip.com/").Replace("#", ""),
                        (gp.DirectMirrorUrl ?? "").Replace("#", ""),
                        gp.SteamAppId ?? "480",
                        (gp.MaxPlayers ?? "Online Co-Op").Replace("#", ""),
                        (gp.FileSize ?? "3.0 GB").Replace("#", "")
                    });

                    using (WebClient wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers.Add("User-Agent", "TitanGameHub/7.0");
                        wc.UploadString(OtaChannelUrl, "POST", payload);
                    }

                    if (OnOtaStatusChanged != null)
                        OnOtaStatusChanged("'" + gp.Name + "' güncellemesi ve indirme sunucusu tüm uzak PC'lere Bulut OTA ile gönderildi!", 1);
                }
                catch { }
            });
        }

        public void PushEntireCatalogToFriendsAsync(List<GameProfile> catalog)
        {
            PushFullApplicationBinaryToRemotePCsAsync(catalog);
        }

        public void BroadcastFleetUpdateAsync(GameProfile gp)
        {
            if (gp == null) return;
            string swarmUrl = FleetSyncService.GetP2pSwarmUrl(gp);
            SquadVersionRadarService.BroadcastSquadUpdateCommandAsync(gp, swarmUrl, delegate(string status)
            {
                if (OnOtaStatusChanged != null) OnOtaStatusChanged(status, 1);
            });
        }

        private static string ExtractJsonField(string json, string field)
        {
            Match m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (m.Success)
            {
                return Regex.Unescape(m.Groups[1].Value);
            }
            return null;
        }

        private static string ExtractAttachmentUrl(string json)
        {
            Match m = Regex.Match(json, "\"attachment\"\\s*:\\s*\\{[^\\}]*\"url\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (m.Success)
            {
                return Regex.Unescape(m.Groups[1].Value);
            }
            return null;
        }
    }
}
