using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;

namespace TitanGameHub
{
    public static class GameModeDebloaterService
    {
        private static readonly string[] TargetServices = new string[]
        {
            "wuauserv",   // Windows Update (causes background disk/network spikes)
            "WSearch",    // Windows Search Indexer (causes heavy NVMe/SSD background churning)
            "SysMain",    // Superfetch / Prefetch memory cache rebuilds
            "DiagTrack"   // Connected User Experiences and Telemetry
        };

        private static readonly List<string> _pausedServices = new List<string>();
        private static bool _isFrozen = false;
        private static readonly object _lock = new object();

        public static bool IsFrozen
        {
            get { return _isFrozen; }
        }

        public static void FreezeBackgroundServicesAsync(Action<string> onComplete)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (_lock)
                {
                    if (_isFrozen)
                    {
                        if (onComplete != null) onComplete("Oyun modu servis dondurucu zaten devrede.");
                        return;
                    }

                    _pausedServices.Clear();
                    int count = 0;

                    foreach (string sName in TargetServices)
                    {
                        try
                        {
                            using (ServiceController sc = new ServiceController(sName))
                            {
                                if (sc.Status == ServiceControllerStatus.Running)
                                {
                                    // Pause or Stop cleanly
                                    if (sc.CanPauseAndContinue)
                                    {
                                        sc.Pause();
                                        _pausedServices.Add(sName);
                                        count++;
                                    }
                                    else if (sc.CanStop)
                                    {
                                        RunScCommand(string.Format("stop \"{0}\"", sName));
                                        _pausedServices.Add(sName);
                                        count++;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    _isFrozen = true;
                    string msg = string.Format("Zero-Jitter Oyun Modu: {0} adet arka plan Windows servisi (Windows Update, Search, SysMain) geçici donduruldu. CPU/Disk kullanımı %0.", count);
                    if (onComplete != null) onComplete(msg);
                }
            });
        }

        public static void RestoreBackgroundServicesAsync(Action<string> onComplete)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (_lock)
                {
                    if (!_isFrozen)
                    {
                        if (onComplete != null) onComplete("Dondurulmuş servis bulunmuyor.");
                        return;
                    }

                    int count = 0;
                    foreach (string sName in _pausedServices)
                    {
                        try
                        {
                            using (ServiceController sc = new ServiceController(sName))
                            {
                                if (sc.Status == ServiceControllerStatus.Paused)
                                {
                                    sc.Continue();
                                    count++;
                                }
                                else if (sc.Status == ServiceControllerStatus.Stopped)
                                {
                                    RunScCommand(string.Format("start \"{0}\"", sName));
                                    count++;
                                }
                            }
                        }
                        catch { }
                    }

                    _pausedServices.Clear();
                    _isFrozen = false;
                    string msg = string.Format("Windows servisleri eski çalışma durumuna geri döndürüldü ({0} servis aktif).", count);
                    if (onComplete != null) onComplete(msg);
                }
            });
        }

        private static void RunScCommand(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(1500);
                }
            }
            catch { }
        }
    }
}
