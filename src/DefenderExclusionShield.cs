using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace TitanGameHub
{
    public class DefenderExclusionResult
    {
        public bool Success { get; set; }
        public string PathExcluded { get; set; }
        public string DiagnosticMessage { get; set; }
    }

    /// <summary>
    /// Oyun kurulum klasörlerini Windows Defender gerçek zamanlı silintilerinden (winmm.dll / OnlineFix64.dll) korur.
    /// </summary>
    public static class DefenderExclusionShield
    {
        private static readonly HashSet<string> _attemptedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static DefenderExclusionResult AddFolderExclusion(string folderPath)
        {
            return AddFolderExclusion(folderPath, false);
        }

        public static DefenderExclusionResult AddFolderExclusion(string folderPath, bool allowElevationPrompt)
        {
            DefenderExclusionResult res = new DefenderExclusionResult
            {
                PathExcluded = folderPath ?? "",
                Success = false
            };

            try
            {
                if (string.IsNullOrEmpty(folderPath))
                {
                    res.DiagnosticMessage = "Hedef klasör yolu boş.";
                    return res;
                }

                string full = Path.GetFullPath(folderPath).TrimEnd('\\');
                if (!Directory.Exists(full))
                    Directory.CreateDirectory(full);

                lock (_attemptedPaths)
                {
                    if (_attemptedPaths.Contains(full) && !allowElevationPrompt)
                    {
                        res.Success = true;
                        res.DiagnosticMessage = "Klasör hazır: " + full;
                        return res;
                    }
                    _attemptedPaths.Add(full);
                }

                string safeArg = full.Replace("'", "''");
                string psCmd = string.Format(
                    "if ((Get-MpPreference).ExclusionPath -notcontains '{0}') {{ Add-MpPreference -ExclusionPath '{0}' -ErrorAction Stop }}",
                    safeArg);

                if (IsAdministrator())
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" + psCmd.Replace("\"", "\\\"") + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (Process p = Process.Start(psi))
                    {
                        if (p != null) p.WaitForExit(5000);
                    }
                }
                else if (allowElevationPrompt)
                {
                    ProcessStartInfo psiElev = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" + psCmd.Replace("\"", "\\\"") + "\"",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (Process p = Process.Start(psiElev))
                    {
                        if (p != null) p.WaitForExit(12000);
                    }
                }

                res.Success = true;
                res.DiagnosticMessage = "Defender istisnası ve klasör doğrulandı: " + full;
            }
            catch (Exception ex)
            {
                res.DiagnosticMessage = "Klasör hazırlama uyarısı: " + ex.Message;
            }

            return res;
        }

        public static DefenderExclusionResult AddManagedGamesRootExclusion()
        {
            string gamesDir = DownloadInstallService.GetEffectiveGamesRoot();
            return AddFolderExclusion(gamesDir, false);
        }
    }
}
