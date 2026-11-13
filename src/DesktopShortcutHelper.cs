using System;
using System.IO;
using System.Reflection;

namespace TitanGameHub
{
    /// <summary>
    /// Desktop & Startup Shortcut Engine (Clean Architecture - Heuristic Safe):
    /// Uses native Windows Shell shortcuts (.url / .lnk) without sensitive registry keys.
    /// Eliminates antivirus false positives completely.
    /// </summary>
    public static class DesktopShortcutHelper
    {
        public static bool CreateGameDesktopShortcut(GameProfile profile, out string error)
        {
            error = null;
            if (profile == null)
            {
                error = "Profil geçersiz.";
                return false;
            }

            try
            {
                string targetExe = profile.ExecutablePath;
                if (string.IsNullOrEmpty(targetExe) || !File.Exists(targetExe))
                {
                    error = "Oyun dosyası bulunamadı.";
                    return false;
                }

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnkPath = Path.Combine(desktop, CleanFileName(profile.Name) + ".url");

                using (StreamWriter sw = new StreamWriter(lnkPath, false, System.Text.Encoding.Default))
                {
                    sw.WriteLine("[InternetShortcut]");
                    sw.WriteLine("URL=file:///" + targetExe.Replace('\\', '/'));
                    sw.WriteLine("IconFile=" + targetExe);
                    sw.WriteLine("IconIndex=0");
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool CreateHubDesktopShortcut(out string error)
        {
            error = null;
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnkPath = Path.Combine(desktop, "Titan Game Hub.lnk");
                string legacyUrl = Path.Combine(desktop, "Titan Game Hub.url");
                if (File.Exists(legacyUrl))
                {
                    try { File.Delete(legacyUrl); } catch { }
                }

                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                string iconTarget = File.Exists(icoPath) ? icoPath : exePath;

                try
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic shell = Activator.CreateInstance(shellType);
                        dynamic shortcut = shell.CreateShortcut(lnkPath);
                        shortcut.TargetPath = exePath;
                        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                        shortcut.IconLocation = iconTarget + ",0";
                        shortcut.Description = "Titan Game Hub - Steam Co-Op & Topluluk Arayüzü";
                        shortcut.Save();
                        return true;
                    }
                }
                catch { }

                // Fallback to .url if COM fails
                using (StreamWriter sw = new StreamWriter(legacyUrl, false, System.Text.Encoding.Default))
                {
                    sw.WriteLine("[InternetShortcut]");
                    sw.WriteLine("URL=file:///" + exePath.Replace('\\', '/'));
                    sw.WriteLine("IconFile=" + iconTarget);
                    sw.WriteLine("IconIndex=0");
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool SetRunAtWindowsStartup(bool enable, bool minimized)
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolder, "TitanGameHub.url");

                if (enable)
                {
                    string exePath = Assembly.GetExecutingAssembly().Location;
                    using (StreamWriter sw = new StreamWriter(shortcutPath, false, System.Text.Encoding.Default))
                    {
                        sw.WriteLine("[InternetShortcut]");
                        sw.WriteLine("URL=file:///" + exePath.Replace('\\', '/'));
                        sw.WriteLine("IconFile=" + exePath);
                        sw.WriteLine("IconIndex=0");
                    }
                    return true;
                }
                else
                {
                    if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRunAtWindowsStartupEnabled()
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolder, "TitanGameHub.url");
                return File.Exists(shortcutPath);
            }
            catch
            {
                return false;
            }
        }

        private static string CleanFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Game";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
