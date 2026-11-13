using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    /// <summary>Başlangıç süresi ölçümü (TITAN_TIMING=1 iken data/startup_timing.txt dosyasına yazar).</summary>
    public static class StartupTimer
    {
        private static readonly Stopwatch Sw = Stopwatch.StartNew();
        private static readonly StringBuilder Log = new StringBuilder();
        private static readonly object Lock = new object();
        private static readonly bool Enabled =
            string.Equals(Environment.GetEnvironmentVariable("TITAN_TIMING"), "1", StringComparison.Ordinal);

        public static void Mark(string label)
        {
            if (!Enabled) return;
            lock (Lock)
            {
                Log.AppendLine(string.Format("{0,8:0.000}s  {1}", Sw.Elapsed.TotalSeconds, label));
                try
                {
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "startup_timing.txt"), Log.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }
    }
}
