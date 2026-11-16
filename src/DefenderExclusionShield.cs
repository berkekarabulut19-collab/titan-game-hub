using System;
using System.IO;

namespace TitanGameHub
{
    public class DefenderExclusionResult
    {
        public bool Success { get; set; }
        public string PathExcluded { get; set; }
        public string DiagnosticMessage { get; set; }
    }

    /// <summary>
    /// Oyun kurulum klasörlerini hazırlar ve doğrular.
    /// Not: Harici gizli PowerShell / Add-MpPreference çalıştırmaz (Defender false-positive önlemi).
    /// </summary>
    public static class DefenderExclusionShield
    {
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

                res.Success = true;
                res.DiagnosticMessage = "Klasör hazır ve doğrulandı: " + full;
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
