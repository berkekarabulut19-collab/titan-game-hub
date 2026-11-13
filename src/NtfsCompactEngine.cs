using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TitanGameHub
{
    public class CompactResult
    {
        public bool Success { get; set; }
        public string GameDirectory { get; set; }
        public long OriginalBytes { get; set; }
        public long CompressedBytes { get; set; }
        public double RatioPercent { get; set; }
        public string OutputSummary { get; set; }
    }

    public static class NtfsCompactEngine
    {
        private static bool _isOperationRunning = false;
        public static bool IsOperationRunning
        {
            get { return _isOperationRunning; }
        }

        public static void CompressGameDirectoryAsync(string gameDir, Action<string> onProgress, Action<CompactResult> onComplete)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                if (onComplete != null) onComplete(new CompactResult { Success = false, OutputSummary = "Oyun dizini bulunamadı." });
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                _isOperationRunning = true;
                CompactResult result = new CompactResult { GameDirectory = gameDir };
                try
                {
                    if (onProgress != null) onProgress("Windows NTFS LZX sıkıştırması başlatılıyor... Lütfen bekleyin.");

                    long beforeBytes = CalculateDirectorySize(gameDir);
                    result.OriginalBytes = beforeBytes;

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "compact.exe",
                        Arguments = string.Format("/c /s:\"{0}\" /a /i /exe:lzx", gameDir),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (Process p = Process.Start(psi))
                    {
                        string stdout = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();

                        long afterBytes = CalculateDirectorySize(gameDir);
                        result.CompressedBytes = afterBytes;
                        result.Success = p.ExitCode == 0 || afterBytes < beforeBytes;

                        long saved = Math.Max(0, beforeBytes - afterBytes);
                        double pct = beforeBytes > 0 ? ((double)saved / beforeBytes) * 100.0 : 0;
                        result.RatioPercent = pct;

                        double gbBefore = beforeBytes / (1024.0 * 1024.0 * 1024.0);
                        double gbAfter = afterBytes / (1024.0 * 1024.0 * 1024.0);
                        double gbSaved = saved / (1024.0 * 1024.0 * 1024.0);

                        result.OutputSummary = string.Format("LZX Şeffaf Sıkıştırma Tamamlandı: {0:F1} GB -> {1:F1} GB (%{2:F0} Tasarruf, {3:F1} GB Alan Açıldı)",
                            gbBefore, gbAfter, pct, gbSaved);
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.OutputSummary = "Sıkıştırma hatası: " + ex.Message;
                }
                finally
                {
                    _isOperationRunning = false;
                    if (onComplete != null) onComplete(result);
                }
            });
        }

        public static void DecompressGameDirectoryAsync(string gameDir, Action<string> onProgress, Action<CompactResult> onComplete)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                if (onComplete != null) onComplete(new CompactResult { Success = false, OutputSummary = "Oyun dizini bulunamadı." });
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                _isOperationRunning = true;
                CompactResult result = new CompactResult { GameDirectory = gameDir };
                try
                {
                    if (onProgress != null) onProgress("Windows NTFS LZX açma işlemi başlatılıyor...");

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "compact.exe",
                        Arguments = string.Format("/u /s:\"{0}\" /a /i /exe", gameDir),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (Process p = Process.Start(psi))
                    {
                        p.WaitForExit();
                        result.Success = p.ExitCode == 0;
                        result.OutputSummary = "Klasör normal boyutuna geri döndürüldü.";
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.OutputSummary = "Açma hatası: " + ex.Message;
                }
                finally
                {
                    _isOperationRunning = false;
                    if (onComplete != null) onComplete(result);
                }
            });
        }

        public static long CalculateDirectorySize(string path)
        {
            long size = 0;
            try
            {
                if (!Directory.Exists(path)) return 0;
                string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    try { size += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return size;
        }
    }
}
