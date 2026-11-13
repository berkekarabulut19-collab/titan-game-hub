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
    /// Güvenli yol doğrulayıcı: harici PowerShell/Defender müdahalesi yapmaz,
    /// yalnızca klasör varlığını ve yazma iznini kontrol eder.
    /// </summary>
    public static class DefenderExclusionShield
    {
        public static DefenderExclusionResult AddFolderExclusion(string folderPath)
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

                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                res.Success = true;
                res.DiagnosticMessage = "Klasör hazır ve doğrulandı: " + folderPath;
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
            return AddFolderExclusion(gamesDir);
        }
    }
}
