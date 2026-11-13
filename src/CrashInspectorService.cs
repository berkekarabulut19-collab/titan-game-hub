using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    public class CrashReport
    {
        public bool HasCrashDetected { get; set; }
        public int ExitCode { get; set; }
        public string HexExitCode { get; set; }
        public string FaultingModule { get; set; }
        public string DiagnosticSummary { get; set; }
        public string RecommendedFix { get; set; }
    }

    public static class CrashInspectorService
    {
        public static CrashReport AnalyzeCrash(GameProfile gp, int exitCode)
        {
            CrashReport rep = new CrashReport
            {
                ExitCode = exitCode,
                HexExitCode = string.Format("0x{0:X8}", (uint)exitCode),
                HasCrashDetected = (exitCode != 0)
            };

            uint code = (uint)exitCode;
            if (code == 0xC0000005)
            {
                rep.DiagnosticSummary = "Bellek Erişim İhlali (0xC0000005 - Access Violation). Oyun geçersiz bir bellek adresini okumaya veya çalıştırmaya çalıştı.";
                rep.RecommendedFix = "Oyun içi kaplamaları (Overlays) kapatın, 'Vulkan Fix (DXVK)' modülünü etkinleştirin veya 'Güvenli Mod' ile başlatın.";
            }
            else if (code == 0xC0000135)
            {
                rep.DiagnosticSummary = "Eksik DLL Dosyası (0xC0000135). Gerekli bir Visual C++ veya DirectX runtime kütüphanesi eksik.";
                rep.RecommendedFix = "'Bütünlüğü Doğrula' butonuna basarak eksik DLL dosyalarını otomatik onarın.";
            }
            else if (code == 0x887A0006 || code == 0x887A0005)
            {
                rep.DiagnosticSummary = "Ekran Kartı Zaman Aşımı / Çökmesi (DXGI_ERROR_DEVICE_HUNG). GPU sürücüsü yanıt vermeyi kesti.";
                rep.RecommendedFix = "Ekran kartı overclock ayarlarını sıfırlayın, grafik kalitesini düşürün veya DX11 modunu zorlayın.";
            }
            else if (code == 0xC000001D)
            {
                rep.DiagnosticSummary = "Geçersiz CPU Komutu (0xC000001D - Illegal Instruction). Oyun işlemcinizin desteklemediği bir AVX/FMA komutunu çağırdı.";
                rep.RecommendedFix = "Oyun güncellemelerini kontrol edin ve AVX fix yamasını kurun.";
            }
            else if (code == 0xC00000FD)
            {
                rep.DiagnosticSummary = "Yığın Taşması (0xC00000FD - Stack Overflow). Kod sonsuz döngüye girdi veya yığın alanı tükendi.";
                rep.RecommendedFix = "Çakışan modları devre dışı bırakın ve Save Zaman Tüneli'nden önceki bir kayda dönün.";
            }
            else
            {
                // Check recent Windows Event Log for details
                string eventFaultModule = QueryRecentApplicationCrash(gp);
                if (!string.IsNullOrEmpty(eventFaultModule))
                {
                    rep.FaultingModule = eventFaultModule;
                    rep.DiagnosticSummary = "Olay Günlüğü Hatası: " + eventFaultModule;
                    rep.RecommendedFix = "Güvenli mod ile başlatmayı deneyin.";
                }
                else
                {
                    rep.DiagnosticSummary = "Beklenmeyen Çıkış Kodu: " + rep.HexExitCode;
                    rep.RecommendedFix = "Oyun dosyalarını ve DirectX bağımlılıklarını doğrulayın.";
                }
            }

            return rep;
        }

        private static string QueryRecentApplicationCrash(GameProfile gp)
        {
            if (gp == null || string.IsNullOrEmpty(gp.ExecutablePath)) return null;
            try
            {
                string exeName = Path.GetFileName(gp.ExecutablePath);
                EventLog appLog = new EventLog("Application");
                for (int i = appLog.Entries.Count - 1; i >= Math.Max(0, appLog.Entries.Count - 40); i--)
                {
                    EventLogEntry entry = appLog.Entries[i];
                    if (entry.EntryType == EventLogEntryType.Error && (DateTime.Now - entry.TimeGenerated).TotalMinutes < 15)
                    {
                        if (entry.Message != null && entry.Message.IndexOf(exeName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Match m = Regex.Match(entry.Message, @"Hata veren modül adı:\s*([^\r\n,]+)", RegexOptions.IgnoreCase);
                            if (m.Success) return m.Groups[1].Value.Trim();
                            return "EventID: " + entry.InstanceId;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static Process LaunchInSafeMode(GameProfile gp)
        {
            if (gp == null || !File.Exists(gp.ExecutablePath)) return null;
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = gp.ExecutablePath,
                Arguments = "-safe -windowed -novid -dx11 -w 1280 -h 720",
                WorkingDirectory = gp.WorkingDirectory ?? Path.GetDirectoryName(gp.ExecutablePath)
            };
            return Process.Start(psi);
        }
    }
}
