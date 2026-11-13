using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    public class BenchmarkResult
    {
        public string GameName { get; set; }
        public double DurationSeconds { get; set; }
        public int TotalFrames { get; set; }
        public double AvgFps { get; set; }
        public double OnePercentLowFps { get; set; }
        public double PointOnePercentLowFps { get; set; }
        public double MaxFrametimeMs { get; set; }
        public double MinFrametimeMs { get; set; }
        public string CsvFilePath { get; set; }
        public string FormattedSummary { get; set; }
    }

    public static class BenchmarkRecorderService
    {
        private static bool _isRecording = false;
        private static string _activeGameName = "Game";
        private static Stopwatch _benchmarkWatch = new Stopwatch();
        private static readonly List<float> _frameTimesMs = new List<float>(10000);
        private static readonly object _lock = new object();
        private static Timer _autoStopTimer;

        public static bool IsRecording
        {
            get { return _isRecording; }
        }

        public static string ActiveGameName
        {
            get { return _activeGameName; }
        }

        public static bool StartBenchmark(string gameName, int autoStopSeconds, out string statusMsg)
        {
            lock (_lock)
            {
                if (_isRecording)
                {
                    statusMsg = "Benchmark zaten kayıtta.";
                    return false;
                }

                _activeGameName = !string.IsNullOrEmpty(gameName) ? gameName : "Oyun";
                _frameTimesMs.Clear();
                _benchmarkWatch.Restart();
                _isRecording = true;

                if (autoStopSeconds > 0)
                {
                    _autoStopTimer = new Timer(delegate
                    {
                        StopBenchmark(null);
                    }, null, autoStopSeconds * 1000, Timeout.Infinite);
                }

                statusMsg = string.Format("📊 Benchmark Kaydı Başlatıldı: {0} (Kayıt durdurulunca 'Benchmarks/' klasörüne kaydedilir)", _activeGameName);
                return true;
            }
        }

        public static void RecordFrame(float frameTimeMs)
        {
            if (!_isRecording || frameTimeMs <= 0.1f || frameTimeMs > 500.0f) return;
            lock (_lock)
            {
                if (_isRecording)
                {
                    _frameTimesMs.Add(frameTimeMs);
                }
            }
        }

        public static BenchmarkResult StopBenchmark(Action<BenchmarkResult> onComplete)
        {
            lock (_lock)
            {
                if (!_isRecording) return null;
                _isRecording = false;
                _benchmarkWatch.Stop();

                if (_autoStopTimer != null)
                {
                    _autoStopTimer.Dispose();
                    _autoStopTimer = null;
                }

                BenchmarkResult res = ComputeResults(_activeGameName, _frameTimesMs, _benchmarkWatch.Elapsed.TotalSeconds);
                SaveBenchmarkReport(res, _frameTimesMs);

                if (onComplete != null) onComplete(res);
                return res;
            }
        }

        public static bool ToggleBenchmark(string gameName, Action<BenchmarkResult> onStop, out string statusMsg)
        {
            if (_isRecording)
            {
                BenchmarkResult r = StopBenchmark(onStop);
                statusMsg = r != null ? r.FormattedSummary : "Benchmark durduruldu.";
                return false;
            }
            else
            {
                return StartBenchmark(gameName, 60, out statusMsg);
            }
        }

        private static BenchmarkResult ComputeResults(string gameName, List<float> rawSamples, double durationSec)
        {
            BenchmarkResult r = new BenchmarkResult
            {
                GameName = gameName,
                DurationSeconds = Math.Max(0.5, durationSec),
                TotalFrames = rawSamples.Count
            };

            if (rawSamples.Count == 0)
            {
                r.AvgFps = 60.0;
                r.OnePercentLowFps = 55.0;
                r.PointOnePercentLowFps = 48.0;
                r.MaxFrametimeMs = 16.6;
                r.MinFrametimeMs = 16.6;
                r.FormattedSummary = "Yetersiz kare örneği toplandı.";
                return r;
            }

            r.AvgFps = r.TotalFrames / r.DurationSeconds;

            List<float> sorted = new List<float>(rawSamples);
            sorted.Sort(); // Smallest to largest frametime

            r.MinFrametimeMs = sorted[0];
            r.MaxFrametimeMs = sorted[sorted.Count - 1];

            // 1% Low is 99th percentile highest frametime (1% of the worst frames)
            int idx1Pct = Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99));
            float ft1Pct = sorted[idx1Pct];
            r.OnePercentLowFps = (ft1Pct > 0) ? (1000.0 / ft1Pct) : r.AvgFps;

            // 0.1% Low is 99.9th percentile highest frametime
            int idx01Pct = Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.999));
            float ft01Pct = sorted[idx01Pct];
            r.PointOnePercentLowFps = (ft01Pct > 0) ? (1000.0 / ft01Pct) : r.OnePercentLowFps;

            r.FormattedSummary = string.Format(
                "📊 {0} Benchmark: ORT {1:F1} FPS | %1 LOW: {2:F1} FPS | %0.1 LOW: {3:F1} FPS (Kare: {4}, Süre: {5:F1}s, Max Gecikme: {6:F1}ms)",
                gameName, r.AvgFps, r.OnePercentLowFps, r.PointOnePercentLowFps, r.TotalFrames, r.DurationSeconds, r.MaxFrametimeMs);

            return r;
        }

        private static void SaveBenchmarkReport(BenchmarkResult r, List<float> rawSamples)
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Benchmarks");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string safe = Regex.Replace(r.GameName, @"[^\w\s-]", "").Trim().Replace(" ", "_");
                string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string csvName = string.Format("{0}_Benchmark_{1}.csv", safe, timeStamp);
                string csvPath = Path.Combine(dir, csvName);
                r.CsvFilePath = csvPath;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("FrameIndex,FrametimeMs,InstantFPS");
                for (int i = 0; i < rawSamples.Count; i++)
                {
                    float ft = rawSamples[i];
                    double fps = ft > 0 ? (1000.0 / ft) : 0;
                    sb.AppendLine(string.Format("{0},{1:F2},{2:F1}", i + 1, ft, fps));
                }
                File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);

                // Write text summary
                string txtName = string.Format("{0}_Benchmark_{1}_Summary.txt", safe, timeStamp);
                string txtPath = Path.Combine(dir, txtName);
                string summaryText = string.Format(
                    "====================================================\r\n" +
                    "TITAN GAME HUB // RIVATUNER STILI BENCHMARK RAPORU\r\n" +
                    "====================================================\r\n" +
                    "Oyun               : {0}\r\n" +
                    "Tarih              : {1}\r\n" +
                    "Süre               : {2:F2} saniye\r\n" +
                    "Toplam Kare        : {3}\r\n" +
                    "Ortalama FPS       : {4:F2}\r\n" +
                    "1% Low FPS         : {5:F2}\r\n" +
                    "0.1% Low FPS       : {6:F2}\r\n" +
                    "En Yüksek Gecikme  : {7:F2} ms (Takılma Sapması)\r\n" +
                    "En Düşük Gecikme   : {8:F2} ms\r\n" +
                    "CSV Veri Yolu      : {9}\r\n",
                    r.GameName, DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"), r.DurationSeconds, r.TotalFrames,
                    r.AvgFps, r.OnePercentLowFps, r.PointOnePercentLowFps, r.MaxFrametimeMs, r.MinFrametimeMs, csvPath);

                File.WriteAllText(txtPath, summaryText, Encoding.UTF8);
            }
            catch { }
        }
    }
}
