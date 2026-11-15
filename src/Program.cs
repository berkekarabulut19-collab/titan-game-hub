using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;

namespace TitanGameHub
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                string[] cliArgs = Environment.GetCommandLineArgs();

                // CLI: --selftest
                foreach (string ca in cliArgs)
                {
                    if (string.Equals(ca, "--selftest", StringComparison.OrdinalIgnoreCase))
                    {
                        int exitCode = SelfTestRunner.Run();
                        Environment.Exit(exitCode);
                    }
                }

                // CLI: --probe-wv2 <url> [referer]
                for (int i = 0; i < cliArgs.Length; i++)
                {
                    if (string.Equals(cliArgs[i], "--probe-wv2", StringComparison.OrdinalIgnoreCase) && i + 1 < cliArgs.Length)
                    {
                        string u = cliArgs[i + 1];
                        string refUrl = (i + 2 < cliArgs.Length && !cliArgs[i + 2].StartsWith("-")) ? cliArgs[i + 2] : "https://steamrip.com/";
                        string tmpDir = Path.Combine(Path.GetTempPath(), "titan_wv2_probe");
                        DateTime t0 = DateTime.UtcNow;
                        long gotProbeBytes = 0;
                        WebViewDownloadService.Result wr = WebViewDownloadService.Download(
                            u, tmpDir, null,
                            delegate(int pct, long got, long tot) { gotProbeBytes = got; },
                            delegate { return gotProbeBytes >= 65536 || (DateTime.UtcNow - t0).TotalSeconds > 20; },
                            1, refUrl);
                        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "wv2_probe.txt");
                        try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)); } catch { }
                        bool startedOk = wr.Success || gotProbeBytes >= 65536;
                        File.WriteAllText(logPath, string.Format("StartedOK={0} Success={1} GotBytes={2} Err={3}\nDOM={4}", startedOk, wr.Success, gotProbeBytes, wr.Error ?? "", WebViewDownloadService.LastDomSnapshot ?? ""), Encoding.UTF8);
                        try { if (!string.IsNullOrEmpty(wr.FilePath) && File.Exists(wr.FilePath)) File.Delete(wr.FilePath); } catch { }
                        Environment.Exit(startedOk ? 0 : 1);
                    }
                }

                // CLI: --export-catalog [output.json]
                for (int i = 0; i < cliArgs.Length; i++)
                {
                    if (string.Equals(cliArgs[i], "--export-catalog", StringComparison.OrdinalIgnoreCase))
                    {
                        string outPath = (i + 1 < cliArgs.Length && !cliArgs[i + 1].StartsWith("-"))
                            ? cliArgs[i + 1]
                            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "catalog_export.json");
                        int code = ExportCatalog(outPath);
                        Environment.Exit(code);
                    }
                }

                // titan:// protocol handling
                string protocolArg = null;
                foreach (string ca in cliArgs)
                {
                    if (ca.StartsWith("titan://", StringComparison.OrdinalIgnoreCase))
                    {
                        protocolArg = ca;
                        break;
                    }
                }

                // Multi-Core Background JIT Profile Optimization (.NET 4.5+)
                try
                {
                    string jitDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    if (!Directory.Exists(jitDir)) Directory.CreateDirectory(jitDir);
                    System.Runtime.ProfileOptimization.SetProfileRoot(jitDir);
                    System.Runtime.ProfileOptimization.StartProfile("titan_startup.jit");
                }
                catch { }

                CrashReporter.Install();
                try { LanguageService.Load(); } catch { }

                // Invariant culture initialization to prevent Turkish-I bugs across URL, token, and hash parsing
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

                // Initialize Phase 1-8 Core Subsystems (non-blocking background startup)
                try { KernelLatencyOptimizer.OptimizeNetworkStack(); } catch { }

                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { SignatureDatabaseService.LoadDatabase(); } catch { }
                    try { SteamEmulatorBridge.StartLanDiscovery(Environment.UserName); } catch { }
                    try { VirtualLanMeshService.AllocateMeshIp("TITAN-TR", Environment.UserName); } catch { }
                    try { ZeroVpnTunnelService.DiscoverPublicEndpoint(); } catch { }
                    try
                    {
                        DiscordRichPresenceService.Initialize();
                        DiscordRichPresenceService.SetIdleInHub();
                    }
                    catch { }
                });

                try { StartupTimer.Mark("Main: çekirdek servisler hazır"); } catch { }

                // Store protocol arg for window to consume if needed
                // Parse titan:// invite code: titan://invite/TITAN-XXXXXX or titan://join/TITAN-XXXXXX
                if (!string.IsNullOrEmpty(protocolArg))
                {
                    try
                    {
                        string protoFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "pending_protocol.txt");
                        string dir = Path.GetDirectoryName(protoFile);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        
                        // Extract invite code if present
                        string inviteCode = null;
                        if (protocolArg.StartsWith("titan://invite/", StringComparison.OrdinalIgnoreCase))
                            inviteCode = protocolArg.Substring("titan://invite/".Length);
                        else if (protocolArg.StartsWith("titan://join/", StringComparison.OrdinalIgnoreCase))
                            inviteCode = protocolArg.Substring("titan://join/".Length);
                        
                        if (!string.IsNullOrEmpty(inviteCode))
                        {
                            // Store only the invite code for the window to process
                            string inviteFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "pending_invite.txt");
                            File.WriteAllText(inviteFile, inviteCode, Encoding.UTF8);
                        }
                        
                        // Store full protocol arg for other uses
                        File.WriteAllText(protoFile, protocolArg, Encoding.UTF8);
                    }
                    catch { }
                }

                Application app = new Application();
                WpfMainWindow win = new WpfMainWindow();
                try { StartupTimer.Mark("WpfMainWindow oluşturuldu"); } catch { }
                app.Run(win);
            }
            catch (Exception ex)
            {
                try { CrashReporter.Report(ex, "Startup"); } catch { }
                string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_error.log");
                try { File.WriteAllText(log, ex.ToString()); } catch { }
                MessageBox.Show(ex.ToString(), "Titan Game Hub v8.1 - Baslangic Hatasi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static int ExportCatalog(string outPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Try to load from local cfg files
                List<object> items = new List<object>();
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] cfgs = new string[] { "games_v6.cfg", "games_v4.cfg" };
                foreach (string cfg in cfgs)
                {
                    string p = Path.Combine(baseDir, cfg);
                    if (!File.Exists(p)) p = Path.Combine(baseDir, "bin", cfg);
                    if (!File.Exists(p)) continue;

                    foreach (string raw in File.ReadAllLines(p, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        // Basic pipe/| or tab delimited parsing - best effort
                        string[] parts = line.Split(new char[] { '|', '\t' }, StringSplitOptions.None);
                        if (parts.Length >= 2)
                        {
                            var obj = new Dictionary<string, string>();
                            obj["raw"] = line;
                            obj["field0"] = parts.Length > 0 ? parts[0].Trim() : "";
                            obj["field1"] = parts.Length > 1 ? parts[1].Trim() : "";
                            if (parts.Length > 2) obj["field2"] = parts[2].Trim();
                            if (parts.Length > 3) obj["field3"] = parts[3].Trim();
                            items.Add(obj);
                        }
                        else
                        {
                            var obj = new Dictionary<string, string>();
                            obj["raw"] = line;
                            items.Add(obj);
                        }
                    }
                    break; // use first found
                }

                var export = new Dictionary<string, object>();
                export["exportedAt"] = DateTime.UtcNow.ToString("o");
                export["count"] = items.Count;
                export["items"] = items;

                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                string json = ser.Serialize(export);
                File.WriteAllText(outPath, json, Encoding.UTF8);
                Console.WriteLine("Katalog disa aktarildi: " + outPath + " (" + items.Count + " kayit)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Katalog disa aktarma hatasi: " + ex.Message);
                return 1;
            }
        }
    }
}
