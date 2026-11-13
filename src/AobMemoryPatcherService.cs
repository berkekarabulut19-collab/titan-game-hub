using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TitanGameHub
{
    /// <summary>
    /// AOB (Array of Bytes) bellek tarama ve yama motoru.
    /// Hedef süreçte imza arar, eşleşme bulursa VirtualProtectEx + WriteProcessMemory
    /// ile yama uygular. Başarı oyunun bellek düzeni ve imza eşleşmesine bağlıdır.
    /// </summary>
    public static class AobMemoryPatcherService
    {
        // -------------------------------------------------------
        // Yardımcılar: bayt okuyup/yaz
        // -------------------------------------------------------

        /// <summary>
        /// Hedef süreç belleğine bayt yazar; area'yi Writable yapar, yazar, koruma geri döndürür.
        /// </summary>
        public static bool WriteBytes(IntPtr hProcess, IntPtr address, byte[] data, out int bytesWritten)
        {
            bytesWritten = 0;
            if (hProcess == IntPtr.Zero || address == IntPtr.Zero || data == null || data.Length == 0)
                return false;
            try
            {
                uint oldProtect;
                if (!Win32.VirtualProtectEx(hProcess, address, (UIntPtr)data.Length, Win32.PAGE_EXECUTE_READWRITE, out oldProtect))
                    return false;
                IntPtr written;
                bool ok = Win32.WriteProcessMemory(hProcess, address, data, data.Length, out written);
                if (ok && written != IntPtr.Zero) bytesWritten = (int)written.ToInt64();
                // koruma geri al
                uint tmp;
                Win32.VirtualProtectEx(hProcess, address, (UIntPtr)data.Length, oldProtect, out tmp);
                return ok;
            }
            catch { return false; }
        }

        /// <summary>
        /// Hedef süreç belleğinden bayt okur.
        /// </summary>
        public static bool ReadBytes(IntPtr hProcess, IntPtr address, byte[] buffer, out int bytesRead)
        {
            bytesRead = 0;
            if (hProcess == IntPtr.Zero || address == IntPtr.Zero || buffer == null || buffer.Length == 0)
                return false;
            try
            {
                IntPtr read;
                bool ok = Win32.ReadProcessMemory(hProcess, address, buffer, buffer.Length, out read);
                if (ok) bytesRead = (int)read.ToInt64();
                return ok;
            }
            catch { return false; }
        }

        // -------------------------------------------------------
        // AOB imza ayrıştırıcı
        // -------------------------------------------------------

        private static bool ParsePattern(string pattern, out byte[] bytes, out bool[] mask)
        {
            bytes = null;
            mask = null;
            if (string.IsNullOrWhiteSpace(pattern)) return false;

            var parts = pattern.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var b = new List<byte>(parts.Length);
            var m = new List<bool>(parts.Length);
            foreach (string part in parts)
            {
                string p = part.Trim();
                if (p == "?" || p == "??" || p == "*")
                {
                    b.Add(0);
                    m.Add(true);
                }
                else
                {
                    byte val;
                    if (!byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val))
                        return false;
                    b.Add(val);
                    m.Add(false);
                }
            }
            if (b.Count == 0) return false;
            bytes = b.ToArray();
            mask = m.ToArray();
            return true;
        }

        // -------------------------------------------------------
        // AOB imza tarayıcı
        // -------------------------------------------------------

        /// <summary>
        /// Pattern "48 8B ?? ??" şeklinde, joker (? = herhangi byte) içerebilir.
        /// regionSize <= 0 ise ana modül bellek boyutundan kullanılır.
        /// </summary>
        /// <param name="proc">Hedef süreç.</param>
        /// <param name="pattern">Boşlukla ayrılmış hex bayt deseni.</param>
        /// <param name="regionSize">Tarama alanı boyutu (bayt).</param>
        /// <returns>İlk eşleşme adresi, yoksa IntPtr.Zero.</returns>
        public static IntPtr ScanPattern(Process proc, string pattern, int regionSize = 0)
        {
            if (proc == null) return IntPtr.Zero;

            byte[] pBytes;
            bool[] pMask;
            if (!ParsePattern(pattern, out pBytes, out pMask)) return IntPtr.Zero;

            IntPtr hProcess = IntPtr.Zero;
            try
            {
                if (proc.HasExited) return IntPtr.Zero;

                IntPtr moduleBase = proc.MainModule.BaseAddress;
                long size = regionSize > 0 ? (long)regionSize : proc.MainModule.ModuleMemorySize;
                if (size <= 0) size = 64L * 1024 * 1024; // 64 MB fallback

                hProcess = Win32.OpenProcess(
                    Win32.PROCESS_VM_READ | Win32.PROCESS_QUERY_INFORMATION,
                    false, proc.Id);
                if (hProcess == IntPtr.Zero) return IntPtr.Zero;

                const int chunkSize = 0x10000; // 64 KB parçalar
                byte[] buffer = new byte[chunkSize + pBytes.Length];
                byte[] readBuf = new byte[chunkSize];
                long scanned = 0;
                int carryOver = 0;

                while (scanned < size)
                {
                    int toRead = (int)Math.Min(chunkSize, size - scanned);
                    if (toRead <= 0) break;

                    IntPtr readAddr = new IntPtr(moduleBase.ToInt64() + scanned);
                    IntPtr bytesReadPtr;
                    if (!Win32.ReadProcessMemory(hProcess, readAddr, readBuf, toRead, out bytesReadPtr))
                    {
                        scanned += toRead;
                        carryOver = 0;
                        continue;
                    }

                    int got = (int)bytesReadPtr.ToInt64();
                    if (got <= 0)
                    {
                        scanned += toRead;
                        carryOver = 0;
                        continue;
                    }

                    Buffer.BlockCopy(readBuf, 0, buffer, carryOver, got);
                    int total = carryOver + got;
                    int searchLen = total - pBytes.Length + 1;

                    for (int i = 0; i < searchLen; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < pBytes.Length; j++)
                        {
                            if (!pMask[j] && buffer[i + j] != pBytes[j]) { match = false; break; }
                        }
                        if (match)
                        {
                            long foundOffset = scanned - carryOver + i;
                            return new IntPtr(moduleBase.ToInt64() + foundOffset);
                        }
                    }

                    carryOver = Math.Min(pBytes.Length - 1, total);
                    if (carryOver > 0)
                        Buffer.BlockCopy(buffer, total - carryOver, buffer, 0, carryOver);
                    scanned += toRead;
                }

                return IntPtr.Zero;
            }
            catch { return IntPtr.Zero; }
            finally
            {
                if (hProcess != IntPtr.Zero) Win32.CloseHandle(hProcess);
            }
        }

        // -------------------------------------------------------
        // Yama uygulaması / geri yükleme
        // -------------------------------------------------------

        /// <summary>
        /// Belirtilen adrese yeni baytlar yazar ve orijinalleri geri yüklemek için saklar.
        /// </summary>
        public static bool ApplyPatch(Process proc, IntPtr address, byte[] newBytes, out byte[] originalBytes)
        {
            originalBytes = null;
            if (proc == null || address == IntPtr.Zero || newBytes == null || newBytes.Length == 0) return false;

            IntPtr hProcess = IntPtr.Zero;
            try
            {
                if (proc.HasExited) return false;
                hProcess = Win32.OpenProcess(
                    Win32.PROCESS_VM_READ | Win32.PROCESS_VM_WRITE | Win32.PROCESS_VM_OPERATION | Win32.PROCESS_QUERY_INFORMATION,
                    false, proc.Id);
                if (hProcess == IntPtr.Zero) return false;

                byte[] current = new byte[newBytes.Length];
                int read;
                if (ReadBytes(hProcess, address, current, out read))
                    originalBytes = (byte[])current.Clone();
                else
                    originalBytes = new byte[newBytes.Length];

                int written;
                return WriteBytes(hProcess, address, newBytes, out written);
            }
            catch { return false; }
            finally
            {
                if (hProcess != IntPtr.Zero) Win32.CloseHandle(hProcess);
            }
        }

        /// <summary>Eski baytları (ApplyPatch tarafından saklanan) belirtilen adrese yazar.</summary>
        public static bool RestorePatch(Process proc, IntPtr address, byte[] originalBytes)
        {
            if (proc == null || address == IntPtr.Zero || originalBytes == null || originalBytes.Length == 0) return false;
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                if (proc.HasExited) return false;
                hProcess = Win32.OpenProcess(
                    Win32.PROCESS_VM_WRITE | Win32.PROCESS_VM_OPERATION | Win32.PROCESS_QUERY_INFORMATION,
                    false, proc.Id);
                if (hProcess == IntPtr.Zero) return false;
                int written;
                return WriteBytes(hProcess, address, originalBytes, out written);
            }
            catch { return false; }
            finally
            {
                if (hProcess != IntPtr.Zero) Win32.CloseHandle(hProcess);
            }
        }

        // -------------------------------------------------------
        // API yöntemleri (UI beklentileriyle uyumlu)
        // -------------------------------------------------------

        /// <summary>
        /// 21:9 ultrawide için kayıtlı imza kalıplarında tarar; eşleşme bulursa yama uygular.
        /// </summary>
        public static bool ApplyUltrawidePatch(Process proc, float targetAspect, out string message)
        {
            message = "";
            if (proc == null || proc.HasExited)
            {
                message = "Süreç bulunamadı. Ultrawide ayarı için önce oyunu başlatın.";
                return false;
            }

            // Gerçek oyun bağımlı imza örnekleri.
            // Kullanıcı kendi oyunları için imzaları (binary'leri) Tools > Options > Signature Manager
            // veya doğrudan config dosyasına ekleyebilir.
            string[] candidatePatterns = new[]
            {
                "39 8E E3 3F",                 // float 1.7777778f (16:9) bazı buildlerde
                "8E E3 18 40",                 // float 2.3888888f (21:9) referans
                "00 00 A0 3F 00 00 A0 3F",     // çift aspect sabiti ihtimali
            };

            try
            {
                foreach (string pat in candidatePatterns)
                {
                    IntPtr addr = ScanPattern(proc, pat, 0);
                    if (addr == IntPtr.Zero) continue;

                    // Little-Endian float baytları
                    byte[] targetBytes = BitConverter.GetBytes(targetAspect);
                    byte[] original;
                    if (ApplyPatch(proc, addr, targetBytes, out original))
                    {
                        message = string.Format(
                            "[BASARILI] Ultrawide tarama uygulandı: 0x{0:X} ({1}, imza: {2})",
                            addr.ToInt64(), proc.ProcessName, pat);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                message = "Ultrawide yama hatası: " + ex.Message;
                return false;
            }

            message = string.Format(
                "{0} (PID {1}) için ultrawide: kayıtlı imza eşleşmesi bulunamadı veya yama uygulanamadı. " +
                "Oyunun kendi görüntü ayarlarından 2560x1080 / 3440x1440 çözünürlüğünü seçin ya da imza veritabanını güncelleyin.",
                proc.ProcessName, proc.Id);
            return false;
        }

        /// <summary>
        /// FPS limiti için bilinen kalıplarda tarar; saydam geçiş (NOP) uygular.
        /// </summary>
        public static bool UnlockFpsCap(Process proc, out string message)
        {
            message = "";
            if (proc == null || proc.HasExited)
            {
                message = "Süreç bulunamadı. FPS ayarı için önce oyunu başlatın.";
                return false;
            }

            string[] candidatePatterns = new[]
            {
                "74 ?? 8B 45 ?? 89 45 3C",      // koşullı dallanma örneği
                "0F 85 ?? ?? ?? ?? C7 45 3C 3C 00 00 00", // FPS kontrolü dalgası
            };

            try
            {
                foreach (string pat in candidatePatterns)
                {
                    IntPtr addr = ScanPattern(proc, pat, 0);
                    if (addr == IntPtr.Zero) continue;

                    // Dallanma offsetinde iki bayt (0x90 0x90) NOP yazarak kontrolü devre dışı bırak
                    byte[] nop = new byte[] { 0x90, 0x90 };
                    byte[] original;
                    if (ApplyPatch(proc, addr, nop, out original))
                    {
                        message = string.Format(
                            "[BASARILI] FPS kilidi tarama uygulandı: 0x{0:X} ({1}, imza: {2})",
                            addr.ToInt64(), proc.ProcessName, pat);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                message = "FPS yama hatası: " + ex.Message;
                return false;
            }

            message = string.Format(
                "{0} (PID {1}) için FPS kilidi: kayıtlı imza eşleşmesi bulunamadı veya yama uygulanamadı. " +
                "Menü içi V-Sync / frame limit ayarını veya ekran kartı kontrol panelini kullanın.",
                proc.ProcessName, proc.Id);
            return false;
        }

        /// <summary>
        /// Uygulanan yamaların kalıcı kaydı yapılmadığı; oyunu yeniden başlatarak temizlenir.
        /// </summary>
        public static bool RestoreDefaults(Process proc, out string message)
        {
            message = "Kalıcı yama kaydı yapılmadı. Uygulanan bellek değişikliklerini geri almak için oyunu yeniden başlatın.";
            return true;
        }
    }
}