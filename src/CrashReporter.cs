using System;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    /// <summary>
    /// Çökme raporlayıcı: beklenmeyen hataları data\crashes\ altına yazar.
    /// Hiçbir kişisel veri toplanmaz, hiçbir yere gönderilmez (yerel teşhis dosyası).
    /// </summary>
    public static class CrashReporter
    {
        private static readonly object Lock = new object();
        public static string LastReportPath { get; private set; }

        public static void Install()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Report(e.ExceptionObject as Exception, "AppDomain");
                };
            }
            catch { }
        }

        public static string Report(Exception ex, string source)
        {
            try
            {
                lock (Lock)
                {
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "crashes");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    string file = Path.Combine(dir, "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("=== TITAN GAME HUB ÇÖKME RAPORU ===");
                    sb.AppendLine("Zaman     : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    sb.AppendLine("Kaynak    : " + source);
                    try { sb.AppendLine("Sürüm     : " + AutoUpdateService.CurrentAppVersion); } catch { sb.AppendLine("Sürüm     : bilinmiyor"); }
                    sb.AppendLine("İşletim   : " + Environment.OSVersion);
                    sb.AppendLine("Runtime   : " + Environment.Version);
                    sb.AppendLine("64-bit OS : " + Environment.Is64BitOperatingSystem);
                    sb.AppendLine("64-bit Pr : " + Environment.Is64BitProcess);
                    sb.AppendLine("Makine    : " + Environment.MachineName);
                    sb.AppendLine("Klasör    : " + AppDomain.CurrentDomain.BaseDirectory);
                    sb.AppendLine("Bellek    : " + (Environment.WorkingSet / 1024 / 1024) + " MB");
                    sb.AppendLine();
                    sb.AppendLine(ex != null ? ex.ToString() : "(istisna yok)");
                    try
                    {
                        if (ex != null && ex.InnerException != null)
                        {
                            sb.AppendLine();
                            sb.AppendLine("--- Inner Exception ---");
                            sb.AppendLine(ex.InnerException.ToString());
                        }
                    }
                    catch { }

                    File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                    LastReportPath = file;

                    try { File.WriteAllText(Path.Combine(dir, "last_crash.txt"), file, Encoding.UTF8); } catch { }

                    try
                    {
                        string[] old = Directory.GetFiles(dir, "crash_*.log");
                        if (old.Length > 20)
                        {
                            Array.Sort(old);
                            for (int i = 0; i < old.Length - 20; i++)
                            {
                                try { File.Delete(old[i]); } catch { }
                            }
                        }
                    }
                    catch { }

                    return file;
                }
            }
            catch { return null; }
        }

        public static string GetLastCrashPath()
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "crashes");
                string marker = Path.Combine(dir, "last_crash.txt");
                if (File.Exists(marker))
                {
                    string p = File.ReadAllText(marker, Encoding.UTF8).Trim();
                    if (File.Exists(p)) return p;
                }
                if (!Directory.Exists(dir)) return null;
                string[] files = Directory.GetFiles(dir, "crash_*.log");
                if (files.Length == 0) return null;
                Array.Sort(files);
                return files[files.Length - 1];
            }
            catch { return null; }
        }

        public static string GetLastCrashSummary()
        {
            try
            {
                string p = GetLastCrashPath();
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
                string text = File.ReadAllText(p, Encoding.UTF8);
                if (text.Length > 1200) text = text.Substring(0, 1200) + "\n...";
                return text;
            }
            catch { return null; }
        }
    }
}
