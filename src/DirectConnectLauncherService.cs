using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace TitanGameHub
{
    /// <summary>
    /// Direct Connect Launch Parameter Injector:
    /// Injects command-line parameters to automatically connect to a friend's server or dedicated host on game startup.
    /// Eliminates manual console typing or entering IP/port numbers inside in-game menus.
    /// </summary>
    public static class DirectConnectLauncherService
    {
        public static Process LaunchAndDirectConnect(GameProfile gp, string hostIp, int hostPort, out string launchMessage)
        {
            launchMessage = "";
            if (gp == null || !gp.IsInstalled || !File.Exists(gp.ExecutablePath))
            {
                launchMessage = "Oyun dosyası bulunamadı.";
                return null;
            }

            string ip = string.IsNullOrEmpty(hostIp) ? "127.0.0.1" : hostIp.Trim();
            int port = hostPort > 0 ? hostPort : 7777;

            try
            {
                // Inspect Engine to choose optimal direct connect parameter syntax
                EngineAnalysisResult eng = EngineInspectorService.InspectGameDirectory(gp);
                string args = "";

                if (eng.EngineType == GameEngineType.UnrealEngine4 || eng.EngineType == GameEngineType.UnrealEngine5)
                {
                    // Unreal Engine native syntax: <IP>:<Port>
                    args = string.Format("{0}:{1}", ip, port);
                }
                else if (eng.EngineType == GameEngineType.UnityMono || eng.EngineType == GameEngineType.UnityIL2CPP)
                {
                    // Unity standard syntax
                    args = string.Format("-connect {0} -port {1}", ip, port);
                }
                else
                {
                    // Standard Source / generic syntax: +connect IP:Port
                    args = string.Format("+connect {0}:{1}", ip, port);
                }

                // Also copy to clipboard in case game requires in-game IP pasting
                try
                {
                    Clipboard.SetText(string.Format("{0}:{1}", ip, port));
                }
                catch { }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = gp.ExecutablePath,
                    WorkingDirectory = gp.WorkingDirectory,
                    Arguments = args,
                    UseShellExecute = false
                };

                Process proc = Process.Start(psi);
                launchMessage = string.Format("🚀 {0} Başlatıldı ve Doğrudan Sunucuya Bağlanıyor ({1}:{2})! [Parametre: {3}]",
                    gp.Name, ip, port, args);

                return proc;
            }
            catch (Exception ex)
            {
                launchMessage = "Doğrudan bağlantı başlatma hatası: " + ex.Message;
                return null;
            }
        }
    }
}
