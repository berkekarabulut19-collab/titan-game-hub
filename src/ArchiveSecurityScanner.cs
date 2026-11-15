using System;
using System.Diagnostics;
using System.IO;

namespace TitanGameHub
{
    /// <summary>
    /// İndirilen arşivi Windows Defender komut satırı (MpCmdRun.exe) ile tarar.
    /// Defender yoksa veya tarama yapılamazsa "güvenli" kabul edilir (fail-open) —
    /// çünkü tarayıcı yokluğu indirmeyi engellememeli, ama tehdit bulunursa engellenir.
    /// </summary>
    public static class ArchiveSecurityScanner
    {
        public static bool IsSafe(string filePath, out string message)
        {
            message = "";
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return true;

                // 256 MB üzeri büyük arşivlerde bloklayıcı ön-tarama yerine çıkartma sonrası gerçek zamanlı koruma + PE kontrolü kullanılır
                try
                {
                    if (new FileInfo(filePath).Length > 256L * 1024 * 1024)
                    {
                        message = "Büyük arşiv: çıkartma hızı için gerçek zamanlı korumaya devredildi.";
                        return true;
                    }
                }
                catch { }

                string mp = FindMpCmdRun();
                if (string.IsNullOrEmpty(mp))
                {
                    message = "Defender bulunamadı, tarama atlandı.";
                    return true;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = mp,
                    Arguments = "-Scan -ScanType 3 -File \"" + filePath + "\" -DisableRemediation",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process p = Process.Start(psi))
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) sb.AppendLine(e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(8000))
                    {
                        try { p.Kill(); } catch { }
                        message = "Tarama zaman aşımına uğradı, gerçek zamanlı koruma devrede.";
                        return true;
                    }

                    string output = sb.ToString();
                    int code = 0;
                    try { code = p.ExitCode; } catch { }

                    // MpCmdRun: 0 = tehdit yok, 2 = tehdit bulundu/karantina
                    if (code == 2 || (output.IndexOf("Threat", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        output.IndexOf("no threat", StringComparison.OrdinalIgnoreCase) < 0 &&
                        output.IndexOf("found no", StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        message = "Güvenlik taraması arşivde tehdit bildirdi. Dosya silindi, kurulum durduruldu.";
                        return false;
                    }
                }
                return true;
            }
            catch { return true; }
        }

        private static string FindMpCmdRun()
        {
            try
            {
                string[] candidates = new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        @"Microsoft\Windows Defender\Platform"),
                };

                // En yeni platform klasöründeki MpCmdRun.exe
                string platform = candidates[0];
                if (Directory.Exists(platform))
                {
                    string best = null;
                    DateTime bestTime = DateTime.MinValue;
                    foreach (string dir in Directory.GetDirectories(platform))
                    {
                        string exe = Path.Combine(dir, "MpCmdRun.exe");
                        if (!File.Exists(exe)) continue;
                        DateTime t = Directory.GetLastWriteTimeUtc(dir);
                        if (t > bestTime) { bestTime = t; best = exe; }
                    }
                    if (!string.IsNullOrEmpty(best)) return best;
                }

                string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"Windows Defender\MpCmdRun.exe");
                return File.Exists(fallback) ? fallback : null;
            }
            catch { return null; }
        }
    }
}
