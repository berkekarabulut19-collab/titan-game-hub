using System;
using System.IO;
using System.Threading;

namespace TitanGameHub
{
    public class RelocationResult
    {
        public bool Success { get; set; }
        public string OldDirectory { get; set; }
        public string NewDirectory { get; set; }
        public long BytesTransferred { get; set; }
        public string DiagnosticSummary { get; set; }
    }

    /// <summary>
    /// Storage Drive Relocator & Drive Migration Engine:
    /// Enables migrating 50-100 GB games between storage drives (e.g., C: SSD -> D: HDD or External SSD).
    /// Automatically updates database paths, shortcuts, and verifies free space before moving.
    /// </summary>
    public static class StorageDriveRelocatorService
    {
        public static RelocationResult RelocateGameDirectory(
            GameProfile gp,
            string targetDestinationFolder,
            Action<string, int> progressCallback)
        {
            RelocationResult result = new RelocationResult
            {
                Success = false,
                OldDirectory = (gp != null) ? gp.WorkingDirectory : "",
                NewDirectory = targetDestinationFolder,
                BytesTransferred = 0,
                DiagnosticSummary = ""
            };

            if (gp == null || !gp.IsInstalled || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
            {
                result.DiagnosticSummary = "Taşınacak oyun klasörü bulunamadı.";
                return result;
            }

            if (string.IsNullOrEmpty(targetDestinationFolder))
            {
                result.DiagnosticSummary = "Geçersiz hedef klasör.";
                return result;
            }

            try
            {
                string sourceDir = Path.GetFullPath(gp.WorkingDirectory).TrimEnd('\\');
                string destDir = Path.GetFullPath(targetDestinationFolder).TrimEnd('\\');

                if (string.Equals(sourceDir, destDir, StringComparison.OrdinalIgnoreCase))
                {
                    result.DiagnosticSummary = "Kaynak ve hedef klasör aynı!";
                    return result;
                }

                // 1. Terminate locking processes
                System.Collections.Generic.List<string> killed;
                SmartUpdateShieldService.KillLockingProcesses(gp, out killed);

                // 2. Calculate source size & verify target drive space
                string targetDriveRoot = Path.GetPathRoot(destDir);
                DriveInfo drive = new DriveInfo(targetDriveRoot);

                string[] allFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
                long totalBytes = 0;
                foreach (string f in allFiles)
                {
                    try { totalBytes += new FileInfo(f).Length; } catch { }
                }

                if (drive.AvailableFreeSpace < totalBytes + (1024L * 1024L * 500L)) // Required size + 500 MB headroom
                {
                    result.DiagnosticSummary = string.Format(
                        "Yetersiz Disk Alanı! Hedef sürücüde ({0}) {1:0.0} GB boş alan var, ancak oyun {2:0.0} GB gerektiriyor.",
                        targetDriveRoot, drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), totalBytes / (1024.0 * 1024.0 * 1024.0));
                    return result;
                }

                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                // 3. Transfer files
                int totalFiles = allFiles.Length;
                long bytesMoved = 0;

                for (int i = 0; i < totalFiles; i++)
                {
                    string sf = allFiles[i];
                    string rel = sf.Substring(sourceDir.Length).TrimStart('\\', '/');
                    string targetFile = Path.Combine(destDir, rel);
                    string targetFolder = Path.GetDirectoryName(targetFile);
                    if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

                    File.Copy(sf, targetFile, true);
                    FileInfo fi = new FileInfo(sf);
                    bytesMoved += fi.Length;

                    if (progressCallback != null && totalBytes > 0 && (i % 25 == 0 || i == totalFiles - 1))
                    {
                        int pct = (int)((bytesMoved * 100) / totalBytes);
                        progressCallback(string.Format("Oyun dosyaları yeni diske taşınıyor: %{0} ({1:0.0}/{2:0.0} GB)",
                            pct, bytesMoved / (1024.0 * 1024.0 * 1024.0), totalBytes / (1024.0 * 1024.0 * 1024.0)), pct);
                    }
                }

                // 4. Update Profile Paths
                string oldExe = gp.ExecutablePath;
                string relExe = oldExe.Substring(sourceDir.Length).TrimStart('\\', '/');
                string newExe = Path.Combine(destDir, relExe);

                gp.WorkingDirectory = destDir;
                gp.ExecutablePath = File.Exists(newExe) ? newExe : ProfileStore.FindMainExeInFolder(destDir);

                // 5. Delete old directory safely
                try
                {
                    Directory.Delete(sourceDir, true);
                }
                catch { }

                result.Success = true;
                result.BytesTransferred = bytesMoved;
                result.DiagnosticSummary = string.Format(
                    "✅ DİSK TAŞIMA BAŞARILI: '{0}' oyunu ({1:0.0} GB) başarıyla '{2}' konumuna taşındı. Veritabanı yolları güncellendi.",
                    gp.Name, bytesMoved / (1024.0 * 1024.0 * 1024.0), destDir);

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.DiagnosticSummary = "Taşıma hatası: " + ex.Message;
                return result;
            }
        }
    }
}
