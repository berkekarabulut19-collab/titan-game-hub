using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace TitanGameHub
{
    public class InstalledModItem
    {
        public string ModName { get; set; }
        public string InstalledPath { get; set; }
        public bool IsEnabled { get; set; }
        public string TargetSubFolder { get; set; }
    }

    public static class SteamWorkshopModEngine
    {
        public static string ResolveEngineModFolder(GameProfile gp)
        {
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory)) return null;

            EngineAnalysisResult eng = EngineInspectorService.InspectGameDirectory(gp);
            string baseDir = gp.WorkingDirectory;

            if (eng.EngineType == GameEngineType.UnrealEngine4 || eng.EngineType == GameEngineType.UnrealEngine5)
            {
                // Unreal engine: search for Content/Paks
                string[] paksDirs = Directory.GetDirectories(baseDir, "Paks", SearchOption.AllDirectories);
                if (paksDirs.Length > 0)
                {
                    string modsDir = Path.Combine(paksDirs[0], "~mods");
                    if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);
                    return modsDir;
                }
            }
            else if (eng.EngineType == GameEngineType.UnityMono || eng.EngineType == GameEngineType.UnityIL2CPP)
            {
                string bepDir = Path.Combine(baseDir, "BepInEx", "plugins");
                if (Directory.Exists(Path.Combine(baseDir, "BepInEx")))
                {
                    if (!Directory.Exists(bepDir)) Directory.CreateDirectory(bepDir);
                    return bepDir;
                }
                string unityMods = Path.Combine(baseDir, "Mods");
                if (!Directory.Exists(unityMods)) Directory.CreateDirectory(unityMods);
                return unityMods;
            }

            // Fallback to game root / Mods
            string fallback = Path.Combine(baseDir, "Mods");
            if (!Directory.Exists(fallback)) Directory.CreateDirectory(fallback);
            return fallback;
        }

        public static bool InstallModFileOrArchive(GameProfile gp, string sourcePath, out string statusMsg)
        {
            statusMsg = "";
            if (gp == null || !File.Exists(sourcePath))
            {
                statusMsg = "Geçersiz mod dosyası.";
                return false;
            }

            string modDir = ResolveEngineModFolder(gp);
            if (string.IsNullOrEmpty(modDir))
            {
                statusMsg = "Hedef mod klasörü tespit edilemedi.";
                return false;
            }

            try
            {
                string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                string fileName = Path.GetFileName(sourcePath);

                if (ext == ".zip")
                {
                    // Unzip directly into mod folder
                    ZipFile.ExtractToDirectory(sourcePath, modDir);
                    statusMsg = string.Format("'{0}' modu {1} klasörüne başarıyla kuruldu!", fileName, Path.GetFileName(modDir));
                    return true;
                }
                else
                {
                    string target = Path.Combine(modDir, fileName);
                    File.Copy(sourcePath, target, true);
                    statusMsg = string.Format("'{0}' modu {1} klasörüne başarıyla yerleştirildi!", fileName, Path.GetFileName(modDir));
                    return true;
                }
            }
            catch (Exception ex)
            {
                statusMsg = "Mod kurulum hatası: " + ex.Message;
                return false;
            }
        }

        public static List<InstalledModItem> GetInstalledMods(GameProfile gp)
        {
            List<InstalledModItem> list = new List<InstalledModItem>();
            string modDir = ResolveEngineModFolder(gp);
            if (string.IsNullOrEmpty(modDir) || !Directory.Exists(modDir)) return list;

            try
            {
                string[] files = Directory.GetFiles(modDir, "*.*", SearchOption.TopDirectoryOnly);
                foreach (string f in files)
                {
                    string fn = Path.GetFileName(f);
                    bool disabled = fn.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    list.Add(new InstalledModItem
                    {
                        ModName = disabled ? fn.Substring(0, fn.Length - 9) : fn,
                        InstalledPath = f,
                        IsEnabled = !disabled,
                        TargetSubFolder = Path.GetFileName(modDir)
                    });
                }
            }
            catch { }
            return list;
        }

        public static bool ToggleModState(InstalledModItem item, out string statusMsg)
        {
            statusMsg = "";
            if (item == null || !File.Exists(item.InstalledPath))
            {
                statusMsg = "Mod dosyası bulunamadı.";
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(item.InstalledPath);
                string newPath;
                if (item.IsEnabled)
                {
                    newPath = item.InstalledPath + ".disabled";
                    File.Move(item.InstalledPath, newPath);
                    item.InstalledPath = newPath;
                    item.IsEnabled = false;
                    statusMsg = item.ModName + " pasif yapıldı.";
                }
                else
                {
                    newPath = item.InstalledPath.EndsWith(".disabled")
                        ? item.InstalledPath.Substring(0, item.InstalledPath.Length - 9)
                        : item.InstalledPath;
                    File.Move(item.InstalledPath, newPath);
                    item.InstalledPath = newPath;
                    item.IsEnabled = true;
                    statusMsg = item.ModName + " aktif edildi.";
                }
                return true;
            }
            catch (Exception ex)
            {
                statusMsg = "Mod durum değiştirme hatası: " + ex.Message;
                return false;
            }
        }
    }
}
