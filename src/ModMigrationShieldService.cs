using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    public class ModMigrationSummary
    {
        public int TurkishPatchFilesCount { get; set; }
        public int ReShadeFilesCount { get; set; }
        public int ModLoaderFilesCount { get; set; }
        public int ShaderCacheFilesCount { get; set; }
        public int TotalAssetsPreserved { get; set; }
        public string DiagnosticMessage { get; set; }
    }

    /// <summary>
    /// Mod, Turkish Patch & ReShade Migration Shield:
    /// Automatically preserves community Turkish language patches, ReShade shaders,
    /// BepInEx/MelonLoader/Unreal mods, and DirectX/Vulkan shader caches across game updates.
    /// Eliminates the need to reinstall mods or translations after every game patch.
    /// </summary>
    public static class ModMigrationShieldService
    {
        public static string GetMigrationRoot(string gameId)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UpdateShield", "ModMigration", string.IsNullOrEmpty(gameId) ? "General" : gameId);
            if (!Directory.Exists(root))
            {
                try { Directory.CreateDirectory(root); } catch { }
            }
            return root;
        }

        public static bool ScanAndQuarantineCommunityAssets(GameProfile gp, out string quarantineDir, out ModMigrationSummary summary)
        {
            quarantineDir = "";
            summary = new ModMigrationSummary();

            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
            {
                summary.DiagnosticMessage = "Oyun klasörü mevcut değil.";
                return false;
            }

            try
            {
                string timeTag = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                quarantineDir = Path.Combine(GetMigrationRoot(gp.Id), "Community_" + timeTag);
                if (!Directory.Exists(quarantineDir)) Directory.CreateDirectory(quarantineDir);

                string workDir = gp.WorkingDirectory;
                string[] allFiles = Directory.GetFiles(workDir, "*.*", SearchOption.AllDirectories);

                HashSet<string> reshadeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "dxgi.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll", "d3d9.dll",
                    "reshade.ini", "reshadegui.ini", "dxgi.log", "d3d11.log", "reshade.log"
                };

                foreach (string f in allFiles)
                {
                    try
                    {
                        string fname = (Path.GetFileName(f) ?? "").ToLowerInvariant();
                        string ext = (Path.GetExtension(f) ?? "").ToLowerInvariant();
                        string rel = f.Substring(workDir.Length).TrimStart('\\', '/');
                        string relLower = rel.ToLowerInvariant();

                        bool isPreserved = false;

                        // 1. Turkish Translation Patches
                        if (fname.Contains("turkish") || fname.Contains("turkce") || fname.EndsWith("_tr.pak") ||
                            fname.EndsWith("_tr.dat") || relLower.Contains(@"localization\tr") || relLower.Contains(@"engine\localization\turkish"))
                        {
                            isPreserved = true;
                            summary.TurkishPatchFilesCount++;
                        }

                        // 2. ReShade Shaders & Runtimes
                        if (!isPreserved && (reshadeFiles.Contains(fname) || relLower.Contains("reshade-shaders") || relLower.Contains("reshade-presets")))
                        {
                            isPreserved = true;
                            summary.ReShadeFilesCount++;
                        }

                        // 3. Mod Loaders & Plugins (BepInEx, MelonLoader, ~mods, Unreal Paks, RED4ext)
                        if (!isPreserved && (relLower.Contains(@"bepinex\plugins") || relLower.Contains(@"bepinex\config") ||
                            relLower.Contains(@"~mods") || relLower.Contains(@"melonloader") || relLower.Contains(@"red4ext")))
                        {
                            isPreserved = true;
                            summary.ModLoaderFilesCount++;
                        }

                        // 4. Shader Caches (DXVK & PSO caches to eliminate update micro-stutter)
                        if (!isPreserved && (fname.EndsWith(".dxvk-cache") || fname == "dxvk.conf" || fname.Contains("pso_cache")))
                        {
                            isPreserved = true;
                            summary.ShaderCacheFilesCount++;
                        }

                        if (isPreserved)
                        {
                            string dest = Path.Combine(quarantineDir, rel);
                            string destDir = Path.GetDirectoryName(dest);
                            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                            File.Copy(f, dest, true);
                            summary.TotalAssetsPreserved++;
                        }
                    }
                    catch { }
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("GameId=" + gp.Id);
                sb.AppendLine("GameName=" + gp.Name);
                sb.AppendLine("Timestamp=" + DateTime.Now.ToString("O"));
                sb.AppendLine("TurkishPatchFiles=" + summary.TurkishPatchFilesCount);
                sb.AppendLine("ReShadeFiles=" + summary.ReShadeFilesCount);
                sb.AppendLine("ModLoaderFiles=" + summary.ModLoaderFilesCount);
                sb.AppendLine("ShaderCacheFiles=" + summary.ShaderCacheFilesCount);
                sb.AppendLine("TotalPreserved=" + summary.TotalAssetsPreserved);
                File.WriteAllText(Path.Combine(quarantineDir, "migration_manifest.ini"), sb.ToString(), Encoding.UTF8);

                summary.DiagnosticMessage = string.Format(
                    "Topluluk Koruması: {0} Türkçe yama, {1} ReShade, {2} mod ve {3} shader önbellek dosyası karantinaya alındı.",
                    summary.TurkishPatchFilesCount, summary.ReShadeFilesCount, summary.ModLoaderFilesCount, summary.ShaderCacheFilesCount);

                return true;
            }
            catch (Exception ex)
            {
                summary.DiagnosticMessage = "Mod karantina hatası: " + ex.Message;
                return false;
            }
        }

        public static bool RestoreAndRebindCommunityAssets(GameProfile gp, string quarantineDir, out ModMigrationSummary summary)
        {
            summary = new ModMigrationSummary();
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(quarantineDir))
            {
                summary.DiagnosticMessage = "Karantina dizini bulunamadı.";
                return false;
            }

            try
            {
                string targetDir = gp.WorkingDirectory;
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                string[] files = Directory.GetFiles(quarantineDir, "*.*", SearchOption.AllDirectories);
                foreach (string qf in files)
                {
                    try
                    {
                        string fname = Path.GetFileName(qf);
                        if (fname.Equals("migration_manifest.ini", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string rel = qf.Substring(quarantineDir.Length).TrimStart('\\', '/');
                        string dest = Path.Combine(targetDir, rel);
                        string destFolder = Path.GetDirectoryName(dest);
                        if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

                        File.Copy(qf, dest, true);
                        summary.TotalAssetsPreserved++;
                    }
                    catch { }
                }

                summary.DiagnosticMessage = string.Format(
                    "Başarılı: {0} topluluk dosyası (Türkçe yama, ReShade, Modlar & Shader önbellekleri) yeni sürüme kayıpsız aktarıldı.",
                    summary.TotalAssetsPreserved);

                return true;
            }
            catch (Exception ex)
            {
                summary.DiagnosticMessage = "Mod geri yükleme hatası: " + ex.Message;
                return false;
            }
        }
    }
}
