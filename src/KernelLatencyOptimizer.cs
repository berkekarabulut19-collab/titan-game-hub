using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace TitanGameHub
{
    public static class KernelLatencyOptimizer
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint CurrentResolution);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessPriorityBoost(IntPtr hProcess, bool bDisablePriorityBoost);

        public static bool IsTimerResolutionLocked { get; private set; }
        public static double CurrentTimerMs { get; private set; }

        static KernelLatencyOptimizer()
        {
            CurrentTimerMs = 15.6;
        }

        public static bool EnableUltraLowLatencyTimer(out string report)
        {
            report = "";
            try
            {
                uint minRes, maxRes, curRes;
                NtQueryTimerResolution(out minRes, out maxRes, out curRes);

                // Target 5000 (0.5 ms in 100-ns units) or max supported precision
                uint targetRes = maxRes > 0 ? maxRes : 5000;
                uint actualRes;
                int status = NtSetTimerResolution(targetRes, true, out actualRes);

                if (status == 0)
                {
                    IsTimerResolutionLocked = true;
                    CurrentTimerMs = actualRes / 10000.0;
                    report = string.Format("Cekirdek Zamanlayici 0.5ms moduna kilitlendi (Aktif: {0:0.00} ms)", CurrentTimerMs);
                    return true;
                }
            }
            catch (Exception ex)
            {
                report = "Timer hatasi: " + ex.Message;
                return false;
            }

            report = "Standart zamanlayici aktif.";
            return false;
        }

        public static void DisableUltraLowLatencyTimer()
        {
            try
            {
                uint actualRes;
                NtSetTimerResolution(5000, false, out actualRes);
                IsTimerResolutionLocked = false;
                CurrentTimerMs = actualRes / 10000.0;
            }
            catch { }
        }

        public static void OptimizeNetworkStack()
        {
            try
            {
                ServicePointManager.UseNagleAlgorithm = false;
                ServicePointManager.Expect100Continue = false;
                if (ServicePointManager.DefaultConnectionLimit < 512)
                    ServicePointManager.DefaultConnectionLimit = 512;
                ServicePointManager.MaxServicePointIdleTime = 15000;
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)12288; // TLS 1.2 + 1.3

                try
                {
                    System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
                }
                catch { }
            }
            catch { }
        }

        public static string OptimizeGameProcess(Process proc)
        {
            if (proc == null || proc.HasExited)
                return "Hedef surec aktif degil.";

            try
            {
                string timerMsg;
                EnableUltraLowLatencyTimer(out timerMsg);
                OptimizeNetworkStack();

                try
                {
                    proc.PriorityClass = ProcessPriorityClass.High;
                    proc.PriorityBoostEnabled = true;
                }
                catch { }

                try
                {
                    SetProcessPriorityBoost(proc.Handle, false);
                }
                catch { }

                try
                {
                    int coreCount = Environment.ProcessorCount;
                    long mask = (1L << coreCount) - 1;
                    if (mask > 0)
                        proc.ProcessorAffinity = (IntPtr)mask;
                }
                catch { }

                double freedMb;
                int trimmed = Win32.BoostSystemMemoryWorkingSet(out freedMb);

                return string.Format("[OPTIMIZASYON] {0} -> High Priority | {1} Cekirdek Aktif | Timer: {2:0.00}ms | +{3:0} MB RAM Bosaltildi",
                    proc.ProcessName, Environment.ProcessorCount, CurrentTimerMs, freedMb);
            }
            catch (Exception ex)
            {
                return "Optimizasyon uygulanamadi: " + ex.Message;
            }
        }
    }
}
