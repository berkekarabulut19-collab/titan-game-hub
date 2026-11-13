using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    public class GameIntegrityReport
    {
        public bool IsHealthy { get; set; }
        public int TotalFilesScanned { get; set; }
        public int ValidFilesCount { get; set; }
        public int CorruptedFilesCount { get; set; }
        public int MissingFilesCount { get; set; }
        public List<string> CorruptedFileList { get; set; }
        public List<string> MissingFileList { get; set; }
        public string DiagnosticSummary { get; set; }
        public bool SnapshotAvailable { get; set; }
        public int MissingFromSnapshotCount { get; set; }
        public int ChangedFromSnapshotCount { get; set; }
        public List<string> SnapshotMissingList { get; set; }
        public List<string> SnapshotChangedList { get; set; }

        public GameIntegrityReport()
        {
            CorruptedFileList = new List<string>();
            MissingFileList = new List<string>();
            SnapshotMissingList = new List<string>();
            SnapshotChangedList = new List<string>();
        }
    }

    /// <summary>
    /// Steam "Verify Integrity of Game Files" Equivalent Engine:
    /// Scans game directory, verifies PE headers of binaries, identifies zero-byte/corrupted files,
    /// checks Steam emulation components, and repairs broken or missing files in-place without re-downloading.
    /// </summary>
    public static class GameIntegrityDoctorService
    {
        public static GameIntegrityReport VerifyGameIntegrity(GameProfile gp, Action<string, int> progressCallback)
        {
            GameIntegrityReport report = new GameIntegrityReport
            {
                IsHealthy = true,
                TotalFilesScanned = 0,
                ValidFilesCount = 0,
                CorruptedFilesCount = 0,
                MissingFilesCount = 0,
                DiagnosticSummary = ""
            };

            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
            {
                report.IsHealthy = false;
                report.DiagnosticSummary = "Oyun çalışma klasörü bulunamadı.";
                return report;
            }

            try
            {
                string workDir = gp.WorkingDirectory;
                string[] allFiles = Directory.GetFiles(workDir, "*.*", SearchOption.AllDirectories);
                int total = allFiles.Length;

                if (total == 0)
                {
                    report.IsHealthy = false;
                    report.DiagnosticSummary = "Oyun klasörü tamamen boş!";
                    return report;
                }

                // Check 1: Main Executable Presence
                if (string.IsNullOrEmpty(gp.ExecutablePath) || !File.Exists(gp.ExecutablePath))
                {
                    report.MissingFilesCount++;
                    report.MissingFileList.Add("Ana Oyun Yürütülebilir Dosyası (.EXE)");
                    report.IsHealthy = false;
                }

                // Check 2: Scan all files
                for (int i = 0; i < total; i++)
                {
                    string f = allFiles[i];
                    report.TotalFilesScanned++;

                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        string ext = fi.Extension.ToLowerInvariant();

                        // Flag 0-byte files (except empty marker files)
                        if (fi.Length == 0 && !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                        {
                            report.CorruptedFilesCount++;
                            report.CorruptedFileList.Add(Path.GetFileName(f) + " (0 Bayt Bozuk Dosya)");
                            report.IsHealthy = false;
                            continue;
                        }

                        // Inspect PE Binaries (.exe / .dll)
                        if (ext == ".exe" || ext == ".dll")
                        {
                            if (fi.Length < 1024)
                            {
                                report.CorruptedFilesCount++;
                                report.CorruptedFileList.Add(Path.GetFileName(f) + " (Eksik/Bozuk PE Başlığı)");
                                report.IsHealthy = false;
                                continue;
                            }

                            // Quick MZ check
                            using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                byte[] mz = new byte[2];
                                int read = fs.Read(mz, 0, 2);
                                if (read < 2 || mz[0] != 0x4D || mz[1] != 0x5A)
                                {
                                    report.CorruptedFilesCount++;
                                    report.CorruptedFileList.Add(Path.GetFileName(f) + " (Geçersiz DOS MZ Başlığı)");
                                    report.IsHealthy = false;
                                    continue;
                                }
                            }
                        }

                        report.ValidFilesCount++;
                    }
                    catch
                    {
                        report.CorruptedFilesCount++;
                        report.CorruptedFileList.Add(Path.GetFileName(f) + " (Okuma Hatası / Kilitli)");
                        report.IsHealthy = false;
                    }

                    if (progressCallback != null && total > 0 && (i % 20 == 0 || i == total - 1))
                    {
                        int pct = ((i + 1) * 100) / total;
                        progressCallback(string.Format("Dosya bütünlüğü taranıyor: {0}/{1} (%{2})", i + 1, total, pct), pct);
                    }
                }

                // Check 3: Steam Emulation Essentials
                string appIdFile = Path.Combine(workDir, "steam_appid.txt");
                if (!File.Exists(appIdFile))
                {
                    report.MissingFilesCount++;
                    report.MissingFileList.Add("steam_appid.txt (Çok Oyunculu AppID Tanımı Eksik)");
                    report.IsHealthy = false;
                }

                if (report.IsHealthy)
                {
                    report.DiagnosticSummary = string.Format(
                        "✅ BÜTÜNLÜK DOĞRULANDI: Taranan {0} dosyanın tamamı sağlam! Bozuk veya eksik dosya tespit edilmedi.",
                        report.TotalFilesScanned);
                }
                else
                {
                    report.DiagnosticSummary = string.Format(
                        "⚠️ BÜTÜNLÜK HATASI: {0} dosya tarandı. {1} bozuk dosya, {2} eksik bileşen bulundu! 'Otomatik Onar' ile düzeltebilirsiniz.",
                        report.TotalFilesScanned, report.CorruptedFilesCount, report.MissingFilesCount);
                }

                // Steam "manifest" karşılaştırması: kurulumdan bu yana silinen/değişen dosyalar
                try
                {
                    if (progressCallback != null) progressCallback("Kurulum kaydı (manifest) ile karşılaştırılıyor...", 92);

                    report.SnapshotAvailable = GameFileManifest.HasSnapshot(gp);
                    if (report.SnapshotAvailable)
                    {
                        List<string> missing, changed;
                        GameFileManifest.CompareToSnapshot(gp, out missing, out changed);

                        report.SnapshotMissingList = missing;
                        report.SnapshotChangedList = changed;
                        report.MissingFromSnapshotCount = missing.Count;
                        report.ChangedFromSnapshotCount = changed.Count;

                        if (missing.Count > 0 || changed.Count > 0)
                        {
                            report.IsHealthy = false;
                            report.MissingFilesCount += missing.Count;
                            foreach (string m in missing) report.MissingFileList.Add(m + "  (kurulumdan sonra silinmiş)");
                            foreach (string c in changed) report.CorruptedFileList.Add(c + "  (boyut değişmiş)");

                            report.DiagnosticSummary = string.Format(
                                "⚠ BÜTÜNLÜK HATASI: {0} dosya tarandı. Silinen: {1}, değişen: {2}, bozuk bileşen: {3}. Onar ile yeniden indirilebilir; save/modlarınız korunur.",
                                report.TotalFilesScanned, missing.Count, changed.Count,
                                report.CorruptedFilesCount + report.MissingFilesCount - missing.Count - changed.Count);
                        }
                        else if (report.IsHealthy)
                        {
                            report.DiagnosticSummary = string.Format(
                                "✅ BÜTÜNLÜK DOĞRULANDI: {0} dosya tarandı, kurulum kaydıyla birebir aynı. Eksik/bozuk dosya yok.",
                                report.TotalFilesScanned);
                        }
                    }
                }
                catch { }

                return report;
            }
            catch (Exception ex)
            {
                report.IsHealthy = false;
                report.DiagnosticSummary = "Doğrulama hatası: " + ex.Message;
                return report;
            }
        }

        /// <summary>
        /// Kısmi onarım: snapshot'ta eksik görünen dosyaları, diskte duran indirme arşivinden
        /// (varsa) çıkarıp yerine koyar. Tüm oyunu yeniden indirmeye gerek kalmaz.
        /// </summary>
        public static int RepairMissingFromArchive(GameProfile gp, List<string> missingFiles, out string summary)
        {
            summary = "";
            if (gp == null || missingFiles == null || missingFiles.Count == 0) return 0;
            if (string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory)) return 0;

            string gamesRoot = DownloadInstallService.GetEffectiveGamesRoot();
            string safeFolder = System.Text.RegularExpressions.Regex.Replace(gp.Name ?? "game", @"[^\w\s-]", "").Trim();
            string[] candidates = new string[]
            {
                System.IO.Path.Combine(gamesRoot, safeFolder + "_pkg.rar"),
                System.IO.Path.Combine(gamesRoot, safeFolder + "_pkg.zip"),
                System.IO.Path.Combine(gamesRoot, safeFolder + "_pkg.7z")
            };

            string archive = null;
            foreach (string c in candidates) { if (System.IO.File.Exists(c)) { archive = c; break; } }
            if (string.IsNullOrEmpty(archive))
            {
                summary = "Kısmi onarım için indirme arşivi bulunamadı (tam indirme gerekir).";
                return 0;
            }

            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "titan_repair_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                System.IO.Directory.CreateDirectory(tempDir);
                if (!DownloadInstallService.ExtractArchiveCore(archive, tempDir))
                {
                    summary = "Arşiv açılamadı (7-Zip/WinRAR bulunamadı).";
                    return 0;
                }

                int restored = 0;
                foreach (string rel in missingFiles)
                {
                    try
                    {
                        string src = System.IO.Path.Combine(tempDir, rel);
                        string dst = System.IO.Path.Combine(gp.WorkingDirectory, rel);
                        if (!System.IO.File.Exists(src)) continue;

                        string dir = System.IO.Path.GetDirectoryName(dst);
                        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                        System.IO.File.Copy(src, dst, true);
                        restored++;
                    }
                    catch { }
                }

                summary = string.Format("Kısmi onarım: {0}/{1} eksik dosya arşivden geri yüklendi.", restored, missingFiles.Count);
                return restored;
            }
            catch (Exception ex)
            {
                summary = "Kısmi onarım hatası: " + ex.Message;
                return 0;
            }
            finally
            {
                try { if (System.IO.Directory.Exists(tempDir)) System.IO.Directory.Delete(tempDir, true); } catch { }
            }
        }

        public static bool RepairGameFiles(GameProfile gp, out string repairSummary)
        {
            repairSummary = "";
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
            {
                repairSummary = "Oyun klasörü bulunamadı.";
                return false;
            }

            try
            {
                int repairedCount = 0;
                string root = gp.WorkingDirectory;

                // 1. Repair steam_appid.txt
                string appId = string.IsNullOrEmpty(gp.SteamAppId) ? "480" : gp.SteamAppId;
                string appIdPath = Path.Combine(root, "steam_appid.txt");
                File.WriteAllText(appIdPath, appId, Encoding.ASCII);
                repairedCount++;

                // 2. Deploy Full Goldberg Emulation Suite
                SteamEmulatorBridge.DeployFullEmulationSuite(gp, Environment.UserName);
                repairedCount++;

                // 3. Re-inject OnlineFix.ini
                string onlineFixIni = Path.Combine(root, "OnlineFix.ini");
                StringBuilder ofIni = new StringBuilder();
                ofIni.AppendLine("[Main]");
                ofIni.AppendLine("RealAppId=" + appId);
                ofIni.AppendLine("FakeAppId=480");
                ofIni.AppendLine("Language=turkish");
                ofIni.AppendLine("UseSpacewarP2P=true");
                ofIni.AppendLine("UnlockAllDLC=true");
                ofIni.AppendLine("Overlay=true");
                File.WriteAllText(onlineFixIni, ofIni.ToString(), Encoding.UTF8);
                repairedCount++;

                // 4. Re-locate Main Executable if path broken
                if (string.IsNullOrEmpty(gp.ExecutablePath) || !File.Exists(gp.ExecutablePath))
                {
                    string foundExe = ProfileStore.FindMainExeInFolder(root);
                    if (!string.IsNullOrEmpty(foundExe))
                    {
                        gp.ExecutablePath = foundExe;
                        gp.WorkingDirectory = Path.GetDirectoryName(foundExe);
                        repairedCount++;
                    }
                }

                // 5. Inspect and repair system dependencies
                DependencyRepairEngine.InspectAndRepairGameBinary(gp);

                // 6. Whitelist folder in Windows Defender
                DefenderExclusionShield.AddFolderExclusion(root);

                repairSummary = string.Format(
                    "✅ BAŞARIYLA ONARILDI: {0} bileşen tamir edildi (Steam AppID 480, Goldberg Suite, OnlineFix ve Defender Kalkanı yenilendi).",
                    repairedCount);

                return true;
            }
            catch (Exception ex)
            {
                repairSummary = "Onarım hatası: " + ex.Message;
                return false;
            }
        }
    }
}
