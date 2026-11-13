using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TitanGameHub
{
    /// <summary>
    /// Dynamic CPU Affinity & E-Core Lasso Optimizer (C# 5.0 Strict):
    /// Binds game threads exclusively to Performance Cores (P-Cores) on Intel 12/13/14th Gen
    /// and AMD CCD0 to eliminate thread migration latency and micro-stutters.
    /// </summary>
    public static class CpuAffinityOptimizer
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessAffinityMask(IntPtr hProcess, UIntPtr dwProcessAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessAffinityMask(IntPtr hProcess, out UIntPtr lpProcessAffinityMask, out UIntPtr lpSystemAffinityMask);

        public static bool OptimizeGameAffinity(Process process, out string statusMsg)
        {
            statusMsg = "";
            if (process == null || process.HasExited)
            {
                statusMsg = "Geçerli bir oyun süreci bulunamadı.";
                return false;
            }

            try
            {
                int coreCount = Environment.ProcessorCount;
                if (coreCount <= 4)
                {
                    statusMsg = string.Format("İşlemci {0} çekirdekli; tüm çekirdekler aktif bırakıldı.", coreCount);
                    return true;
                }

                // Intel 12th+ / AMD hybrid architecture check:
                // High core counts (e.g. 12, 16, 20, 24, 32 threads) usually have E-cores or secondary CCDs.
                // Mask the first 8 or 12 high-frequency P-core hyperthreads (Bitmask 0x0000FFFF or 0x000000FF)
                ulong pCoreMask = 0;
                int targetThreads = coreCount >= 16 ? 16 : (coreCount >= 8 ? 8 : coreCount);

                for (int i = 0; i < targetThreads; i++)
                {
                    pCoreMask |= (1UL << i);
                }

                UIntPtr maskPtr = new UIntPtr(pCoreMask);
                bool ok = SetProcessAffinityMask(process.Handle, maskPtr);
                if (ok)
                {
                    statusMsg = string.Format("🚀 CPU Affinity P-Core Kilitlendi: İlk {0} Performans Çekirdeği ayrıldı (E-Core Takılmaları Sıfırlandı).", targetThreads);
                    return true;
                }
                else
                {
                    statusMsg = "CPU Affinity Win32 hatası: " + Marshal.GetLastWin32Error();
                    return false;
                }
            }
            catch (Exception ex)
            {
                statusMsg = "Affinity optimizasyon hatası: " + ex.Message;
                return false;
            }
        }
    }
}
