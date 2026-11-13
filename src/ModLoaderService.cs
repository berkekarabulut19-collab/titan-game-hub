using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TitanGameHub
{
    public class GameModEntry
    {
        public string ModName { get; set; }
        public string FilePath { get; set; }
        public bool IsEnabled { get; set; }
        public string FileSizeText { get; set; }
    }

    public static class ModLoaderService
    {
        public static bool InitializeModFrameworkStructure(GameProfile gp)
        {
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
                return false;

            try
            {
                EngineAnalysisResult engine = EngineInspectorService.InspectGameDirectory(gp);
                if (!engine.SupportsBepInEx) return false;

                string root = gp.WorkingDirectory;
                string bepRoot = Path.Combine(root, "BepInEx");
                string pluginsDir = Path.Combine(bepRoot, "plugins");
                string configDir = Path.Combine(bepRoot, "config");
                string coreDir = Path.Combine(bepRoot, "core");

                if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
                if (!Directory.Exists(coreDir)) Directory.CreateDirectory(coreDir);

                string doorstopIni = Path.Combine(root, "doorstop_config.ini");
                if (!File.Exists(doorstopIni))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("[General]");
                    sb.AppendLine("enabled=true");
                    sb.AppendLine("target_assembly=BepInEx\\core\\BepInEx.Preloader.dll");
                    sb.AppendLine("redirect_output_log=false");
                    sb.AppendLine("ignore_disable_switch=false");
                    File.WriteAllText(doorstopIni, sb.ToString(), Encoding.UTF8);
                }

                // Generate default Multiplayer Lobby Unlocker config (32-player Co-Op config)
                string lobbyCfg = Path.Combine(configDir, "TitanMultiplayerPlus.cfg");
                if (!File.Exists(lobbyCfg))
                {
                    StringBuilder cfg = new StringBuilder();
                    cfg.AppendLine("## TitanGameHub Co-Op Lobby Extender Configuration");
                    cfg.AppendLine("[Lobby]");
                    cfg.AppendLine("MaxPlayers = 32");
                    cfg.AppendLine("AllowLateJoin = true");
                    cfg.AppendLine("DisableKickTimeout = true");
                    cfg.AppendLine("SyncAllHostMods = true");
                    File.WriteAllText(lobbyCfg, cfg.ToString(), Encoding.UTF8);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static List<GameModEntry> GetInstalledMods(GameProfile gp)
        {
            List<GameModEntry> list = new List<GameModEntry>();
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
                return list;

            try
            {
                string pluginsDir = Path.Combine(gp.WorkingDirectory, "BepInEx", "plugins");
                if (!Directory.Exists(pluginsDir)) return list;

                string[] dlls = Directory.GetFiles(pluginsDir, "*.dll*", SearchOption.AllDirectories);
                foreach (string f in dlls)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".dll" && ext != ".disabled") continue;

                    FileInfo fi = new FileInfo(f);
                    bool enabled = ext == ".dll";
                    string cleanName = Path.GetFileNameWithoutExtension(f);
                    if (!enabled && cleanName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        cleanName = cleanName.Substring(0, cleanName.Length - 4);

                    list.Add(new GameModEntry
                    {
                        ModName = cleanName,
                        FilePath = f,
                        IsEnabled = enabled,
                        FileSizeText = (fi.Length / 1024.0).ToString("0.0") + " KB"
                    });
                }
            }
            catch { }

            return list;
        }

        public static bool ToggleModState(GameModEntry mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.FilePath) || !File.Exists(mod.FilePath))
                return false;

            try
            {
                string newPath;
                if (mod.IsEnabled)
                {
                    newPath = mod.FilePath + ".disabled";
                }
                else
                {
                    if (mod.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        newPath = mod.FilePath.Substring(0, mod.FilePath.Length - 9);
                    else
                        return false;
                }

                if (File.Exists(newPath)) File.Delete(newPath);
                File.Move(mod.FilePath, newPath);
                mod.FilePath = newPath;
                mod.IsEnabled = !mod.IsEnabled;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool InstallModArchive(GameProfile gp, string modArchivePath, out string statusMsg)
        {
            statusMsg = "";
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
            {
                statusMsg = "Oyun kurulum dizini bulunamadi.";
                return false;
            }

            if (string.IsNullOrEmpty(modArchivePath) || !File.Exists(modArchivePath))
            {
                statusMsg = "Mod arsivi bulunamadi.";
                return false;
            }

            try
            {
                InitializeModFrameworkStructure(gp);
                string pluginsDir = Path.Combine(gp.WorkingDirectory, "BepInEx", "plugins");

                if (modArchivePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    string dest = Path.Combine(pluginsDir, Path.GetFileName(modArchivePath));
                    File.Copy(modArchivePath, dest, true);
                    statusMsg = Path.GetFileName(modArchivePath) + " dogrudan BepInEx/plugins dizinine eklendi!";
                    return true;
                }

                if (modArchivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string modFolder = Path.Combine(pluginsDir, Path.GetFileNameWithoutExtension(modArchivePath));
                    if (Directory.Exists(modFolder)) Directory.Delete(modFolder, true);
                    ZipFile.ExtractToDirectory(modArchivePath, modFolder);
                    statusMsg = Path.GetFileNameWithoutExtension(modArchivePath) + " mod paketi basariyla kuruldu!";
                    return true;
                }

                statusMsg = "Desteklenmeyen mod dosya bicimi.";
                return false;
            }
            catch (Exception ex)
            {
                statusMsg = "Mod kurulum hatasi: " + ex.Message;
                return false;
            }
        }
    }
}
