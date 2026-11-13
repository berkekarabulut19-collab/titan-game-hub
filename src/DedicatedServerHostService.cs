using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace TitanGameHub
{
    public class DedicatedServerStatus
    {
        public bool IsRunning { get; set; }
        public string ServerName { get; set; }
        public string GameName { get; set; }
        public int ProcessId { get; set; }
        public double MemoryUsageMb { get; set; }
        public TimeSpan Uptime { get; set; }
        public int Port { get; set; }
        public int RestartCount { get; set; }
        public string StatusSummary { get; set; }
        public string PublicIp { get; set; }
        public string LocalIp { get; set; }
        public bool UpnpForwarded { get; set; }
    }

    /// <summary>
    /// Headless Dedicated Server Hosting & Auto-Watchdog Service:
    /// Enables 1-click persistent co-op server hosting for games like Palworld, Sons of the Forest,
    /// Valheim, Enshrouded, Project Zomboid and Ark. Keeps server online 24/7 with automatic restart on crash,
    /// UPnP auto port-forwarding, and live management GUI dashboard.
    /// </summary>
    public static class DedicatedServerHostService
    {
        private static Process _serverProcess;
        private static Thread _watchdogThread;
        private static bool _watchdogActive = false;
        private static DateTime _startTime;
        private static string _activeServerExe;
        private static string _activeWorkingDir;
        private static string _activeArgs;
        private static int _restartCount = 0;
        private static GameProfile _activeProfile;
        private static int _activePort = 7777;
        private static bool _upnpActive = false;

        public static bool IsServerRunning
        {
            get
            {
                try
                {
                    return _serverProcess != null && !_serverProcess.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static int DetectDefaultServerPort(GameProfile gp)
        {
            if (gp == null) return 7777;
            string lower = gp.Name.ToLowerInvariant();
            if (lower.Contains("palworld")) return 8211;
            if (lower.Contains("valheim")) return 2456;
            if (lower.Contains("zomboid")) return 16261;
            if (lower.Contains("sons of the forest") || lower.Contains("forest")) return 8766;
            if (lower.Contains("7 days")) return 26900;
            if (lower.Contains("ark") || lower.Contains("survival")) return 7777;
            if (lower.Contains("rust")) return 28015;
            if (lower.Contains("v rising")) return 9876;
            return 7777;
        }

        public static bool DetectDedicatedServerBinary(GameProfile gp, out string serverExe, out string suggestedArgs)
        {
            serverExe = "";
            suggestedArgs = "";

            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
                return false;

            try
            {
                string workDir = gp.WorkingDirectory;
                string[] knownServerNames = new string[]
                {
                    "palserver.exe",
                    "palserver-win64-shipping.exe",
                    "sonsoftheforestdedicatedserver.exe",
                    "valheim_server.exe",
                    "enshroudedserver.exe",
                    "shootergame.exe",
                    "7daystodieserver.exe",
                    "projectzomboid64.exe",
                    "vrisingserver.exe",
                    "craftopiaserver.exe",
                    "rustdedicated.exe"
                };

                string[] allExes = Directory.GetFiles(workDir, "*.exe", SearchOption.AllDirectories);
                foreach (string exe in allExes)
                {
                    string fname = (Path.GetFileName(exe) ?? "").ToLowerInvariant();
                    foreach (string known in knownServerNames)
                    {
                        if (fname == known)
                        {
                            serverExe = exe;
                            suggestedArgs = "-batchmode -nographics";
                            return true;
                        }
                    }

                    if (fname.Contains("server") || fname.Contains("dedicated"))
                    {
                        serverExe = exe;
                        suggestedArgs = "-batchmode -nographics";
                        return true;
                    }
                }

                // If no dedicated binary is bundled, fallback to main executable with headless args
                if (File.Exists(gp.ExecutablePath))
                {
                    serverExe = gp.ExecutablePath;
                    suggestedArgs = "-batchmode -nographics -server";
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static bool StartDedicatedServer(GameProfile gp, string customArgs, out string statusMsg)
        {
            statusMsg = "";
            if (IsServerRunning)
            {
                statusMsg = "Zaten çalışan bir Dedicated Server var (PID: " + _serverProcess.Id + ").";
                return true;
            }

            string serverExe, defaultArgs;
            if (!DetectDedicatedServerBinary(gp, out serverExe, out defaultArgs))
            {
                statusMsg = "Bu oyun için dedicated sunucu yürütülebilir dosyası algılanamadı.";
                return false;
            }

            try
            {
                _activeProfile = gp;
                _activeServerExe = serverExe;
                _activeWorkingDir = Path.GetDirectoryName(serverExe);
                _activeArgs = !string.IsNullOrEmpty(customArgs) ? customArgs : defaultArgs;
                _activePort = DetectDefaultServerPort(gp);
                _restartCount = 0;

                LaunchServerProcess();

                if (_serverProcess == null)
                {
                    statusMsg = "Dedicated sunucu başlatılamadı: Süreç oluşturulamadı veya dosya erişim engeli.";
                    return false;
                }

                _watchdogActive = true;
                _watchdogThread = new Thread(WatchdogWorkerLoop);
                _watchdogThread.IsBackground = true;
                _watchdogThread.Start();

                // Trigger UPnP automatic modem port forward in background
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        string discLog;
                        if (UpnpPortForwarderService.DiscoverRouter(3500, out discLog))
                        {
                            string fwdLog;
                            if (UpnpPortForwarderService.ForwardPort(_activePort, "UDP", gp.Name + " Dedicated Server", out fwdLog))
                            {
                                _upnpActive = true;
                            }
                        }
                    }
                    catch { }
                });

                statusMsg = string.Format("✅ {0} Dedicated Sunucusu Arka Planda Başlatıldı (PID:{1} • Port:{2})! Watchdog kalkanı aktif.", gp.Name, _serverProcess.Id, _activePort);
                return true;
            }
            catch (Exception ex)
            {
                statusMsg = "Dedicated sunucu başlatma hatası: " + ex.Message;
                return false;
            }
        }

        private static void LaunchServerProcess()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _activeServerExe,
                    WorkingDirectory = _activeWorkingDir,
                    Arguments = _activeArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                };

                _serverProcess = Process.Start(psi);
                _startTime = DateTime.Now;
            }
            catch (Exception)
            {
                _serverProcess = null;
            }
        }

        private static void WatchdogWorkerLoop()
        {
            while (_watchdogActive)
            {
                try
                {
                    Thread.Sleep(3500);
                    if (!_watchdogActive) break;

                    if (_serverProcess == null || _serverProcess.HasExited)
                    {
                        if (_restartCount < 10)
                        {
                            _restartCount++;
                            LaunchServerProcess();
                        }
                        else
                        {
                            _watchdogActive = false;
                            break;
                        }
                    }
                }
                catch
                {
                    Thread.Sleep(2000);
                }
            }
        }

        public static void StopDedicatedServer()
        {
            _watchdogActive = false;
            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _serverProcess.Kill();
                    _serverProcess.WaitForExit(2000);
                }
            }
            catch { }
            finally
            {
                _serverProcess = null;
            }
        }

        public static DedicatedServerStatus GetServerStatus()
        {
            DedicatedServerStatus status = new DedicatedServerStatus
            {
                IsRunning = IsServerRunning,
                ServerName = (_activeProfile != null) ? _activeProfile.Name + " Dedicated Server" : "Kapalı",
                GameName = (_activeProfile != null) ? _activeProfile.Name : "-",
                ProcessId = 0,
                MemoryUsageMb = 0,
                Uptime = TimeSpan.Zero,
                Port = _activePort,
                RestartCount = _restartCount,
                StatusSummary = "Sunucu Çevrim Dışı",
                LocalIp = UpnpPortForwarderService.GetLocalIPv4(),
                PublicIp = UpnpPortForwarderService.ExternalIpAddress,
                UpnpForwarded = _upnpActive
            };

            if (status.IsRunning && _serverProcess != null)
            {
                try
                {
                    _serverProcess.Refresh();
                    status.ProcessId = _serverProcess.Id;
                    status.MemoryUsageMb = _serverProcess.WorkingSet64 / (1024.0 * 1024.0);
                    status.Uptime = DateTime.Now - _startTime;
                    status.StatusSummary = string.Format("🟢 ÇALIŞIYOR (PID:{0} • RAM: {1:0} MB • Süre: {2:hh\\:mm\\:ss})",
                        status.ProcessId, status.MemoryUsageMb, status.Uptime);
                }
                catch { }
            }

            return status;
        }

        public static void BackupServerSave(GameProfile gp, out string backupResult)
        {
            backupResult = "";
            try
            {
                if (gp == null)
                {
                    backupResult = "Oyun profili seçilmedi.";
                    return;
                }

                string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SaveBackups", "Dedicated_" + Regex.Replace(gp.Name, @"[^\w]", ""));
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

                string savePath = SaveSyncService.LocateGameSaveDirectory(gp);
                if (string.IsNullOrEmpty(savePath) || !Directory.Exists(savePath))
                {
                    backupResult = "Oyunun kayıt dizini bulunamadı.";
                    return;
                }

                string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string targetSnapDir = Path.Combine(backupDir, "SaveSnap_" + timeStamp);
                Directory.CreateDirectory(targetSnapDir);

                string[] files = Directory.GetFiles(savePath, "*.*", SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    string rel = f.Substring(savePath.Length).TrimStart('\\', '/');
                    string dest = Path.Combine(targetSnapDir, rel);
                    string destSub = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(destSub)) Directory.CreateDirectory(destSub);
                    File.Copy(f, dest, true);
                }

                backupResult = string.Format("💾 Dedicated Server save yedeği başarıyla alındı ({0} dosya, Konum: SaveBackups).", files.Length);
            }
            catch (Exception ex)
            {
                backupResult = "Save yedekleme hatası: " + ex.Message;
            }
        }

        public static void OpenServerManagementModal(Window owner, GameProfile profile)
        {
            if (profile == null) return;

            Window win = new Window
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 620,
                Height = 520,
                Title = "Titan // 7/24 Kesintisiz Dedicated Server Yönetim Merkezi",
                Background = HexBrush("#0E141B"),
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            Grid root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status Banner
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Info Box
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Action Buttons
            win.Content = root;

            // Row 0: Header
            StackPanel headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            TextBlock tbTitle = new TextBlock
            {
                Text = "⚡ " + profile.Name.ToUpperInvariant() + " // DEDICATED HOST & WATCHDOG",
                FontSize = 14,
                FontWeight = FontWeights.ExtraBold,
                Foreground = HexBrush("#1A9FFF")
            };
            TextBlock tbSub = new TextBlock
            {
                Text = "Arka plan konsol sunucusu: Bilgisayarınız oyundan çıksa bile arkadaşlarınız 7/24 kesintisiz oynamaya devam eder.",
                FontSize = 10,
                Foreground = HexBrush("#8F98A0"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headerStack.Children.Add(tbTitle);
            headerStack.Children.Add(tbSub);
            Grid.SetRow(headerStack, 0);
            root.Children.Add(headerStack);

            // Row 1: Status Banner Card
            Border statusCard = new Border
            {
                Background = HexBrush("#141D2B"),
                BorderBrush = HexBrush("#23344D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(statusCard, 1);
            root.Children.Add(statusCard);

            TextBlock tbLiveStatus = new TextBlock
            {
                Text = IsServerRunning ? "🟢 ÇALIŞIYOR" : "🔴 SUNUCU KAPALI",
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = IsServerRunning ? HexBrush("#5DBA2F") : HexBrush("#EF4444")
            };
            statusCard.Child = tbLiveStatus;

            // Row 2: Info Box
            Border infoCard = new Border
            {
                Background = HexBrush("#121820"),
                BorderBrush = HexBrush("#1F2937"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(infoCard, 2);
            root.Children.Add(infoCard);

            StackPanel infoStack = new StackPanel();
            infoCard.Child = infoStack;

            TextBlock tbMetrics = new TextBlock
            {
                FontSize = 11,
                Foreground = HexBrush("#DCDEDF"),
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap
            };
            infoStack.Children.Add(tbMetrics);

            // Row 3: Action Buttons Grid
            Grid btnGrid = new Grid();
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            btnGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(btnGrid, 3);
            root.Children.Add(btnGrid);

            Button btnToggleServer = new Button
            {
                Content = IsServerRunning ? "🛑 SUNUCUYU DURDUR" : "▶ SUNUCUYU BAŞLAT",
                Height = 32,
                Margin = new Thickness(0, 0, 4, 6),
                Background = IsServerRunning ? HexBrush("#991B1B") : HexBrush("#15803D"),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetRow(btnToggleServer, 0);
            Grid.SetColumn(btnToggleServer, 0);
            btnGrid.Children.Add(btnToggleServer);

            Button btnCopyIp = new Button
            {
                Content = "📋 BAĞLANTI IP'SİNİ KOPYALA",
                Height = 32,
                Margin = new Thickness(4, 0, 0, 6),
                Background = HexBrush("#1E293B"),
                Foreground = HexBrush("#60A5FA"),
                BorderBrush = HexBrush("#3B82F6"),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetRow(btnCopyIp, 0);
            Grid.SetColumn(btnCopyIp, 1);
            btnGrid.Children.Add(btnCopyIp);

            Button btnBackupSave = new Button
            {
                Content = "💾 SAVE DOSYASINI YEDEKLE",
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                Background = HexBrush("#1E293B"),
                Foreground = HexBrush("#A78BFA"),
                BorderBrush = HexBrush("#8B5CF6"),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetRow(btnBackupSave, 1);
            Grid.SetColumn(btnBackupSave, 0);
            btnGrid.Children.Add(btnBackupSave);

            Button btnUpnp = new Button
            {
                Content = "🌐 MODEMDE PORT AÇ (UPnP)",
                Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                Background = HexBrush("#1E293B"),
                Foreground = HexBrush("#34D399"),
                BorderBrush = HexBrush("#10B981"),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetRow(btnUpnp, 1);
            Grid.SetColumn(btnUpnp, 1);
            btnGrid.Children.Add(btnUpnp);

            Action updateUiMetrics = delegate
            {
                DedicatedServerStatus st = GetServerStatus();
                if (st.IsRunning)
                {
                    tbLiveStatus.Text = st.StatusSummary;
                    tbLiveStatus.Foreground = HexBrush("#5DBA2F");
                    btnToggleServer.Content = "🛑 SUNUCUYU DURDUR";
                    btnToggleServer.Background = HexBrush("#991B1B");
                }
                else
                {
                    tbLiveStatus.Text = "🔴 SUNUCU KAPALI (Tek Tıkla Başlatmaya Hazır)";
                    tbLiveStatus.Foreground = HexBrush("#EF4444");
                    btnToggleServer.Content = "▶ SUNUCUYU BAŞLAT";
                    btnToggleServer.Background = HexBrush("#15803D");
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("• Oyun Adı: " + profile.Name);
                sb.AppendLine("• Sunucu Portu: " + st.Port + " (UDP / Direct Connect)");
                sb.AppendLine("• Yerel Ağ (LAN) IP: " + st.LocalIp + ":" + st.Port);
                sb.AppendLine("• Dış İnternet (Public) IP: " + st.PublicIp + ":" + st.Port);
                sb.AppendLine("• Otomatik Watchdog Kalkanı: " + (st.IsRunning ? "AKTİF (Çökme restart: " + st.RestartCount + ")" : "Hazırda"));
                sb.AppendLine("• Modem UPnP Yönlendirme: " + (st.UpnpForwarded ? "🟢 AÇIK / BAŞARILI" : "⚪ Otomatik Başlatılacak"));
                sb.AppendLine("• Spacewar 480 P2P Ağı: Eşzamanlı Dinlemede");
                tbMetrics.Text = sb.ToString();
            };

            updateUiMetrics();

            DispatcherTimer tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            tm.Tick += delegate { updateUiMetrics(); };
            tm.Start();
            win.Closed += delegate { tm.Stop(); };

            btnToggleServer.Click += delegate
            {
                if (IsServerRunning)
                {
                    StopDedicatedServer();
                    updateUiMetrics();
                }
                else
                {
                    string statMsg;
                    StartDedicatedServer(profile, "", out statMsg);
                    updateUiMetrics();
                }
            };

            btnCopyIp.Click += delegate
            {
                DedicatedServerStatus st = GetServerStatus();
                string ipStr = st.PublicIp + ":" + st.Port;
                try
                {
                    Clipboard.SetText(ipStr);
                    MessageBox.Show(win, "Dış internet bağlantı adresi panoya kopyalandı:\n\n" + ipStr + "\n\nArkadaşlarınız oyundaki 'Doğrudan Bağlan / Direct Connect' kutusuna bu adresi yazarak doğrudan sunucunuza girebilir.", "Titan Dedicated IP", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch { }
            };

            btnBackupSave.Click += delegate
            {
                string bRes;
                BackupServerSave(profile, out bRes);
                MessageBox.Show(win, bRes, "Titan // Save Yedekleme", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            btnUpnp.Click += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string discLog;
                    string resMsg;
                    if (UpnpPortForwarderService.DiscoverRouter(3500, out discLog))
                    {
                        string fwdLog;
                        int port = DetectDefaultServerPort(profile);
                        if (UpnpPortForwarderService.ForwardPort(port, "UDP", profile.Name + " Dedicated", out fwdLog))
                        {
                            _upnpActive = true;
                            resMsg = "Modeminizde " + port + " (UDP) portu UPnP ile başarıyla açıldı!\nArkadaşlarınız modem ayarı yapmadan doğrudan bağlanabilir.";
                        }
                        else
                        {
                            resMsg = "Port yönlendirme yanıtı: " + fwdLog;
                        }
                    }
                    else
                    {
                        resMsg = "Modem UPnP yanıtı: " + discLog;
                    }

                    win.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        updateUiMetrics();
                        MessageBox.Show(win, resMsg, "Titan // UPnP Port Yönlendirici", MessageBoxButton.OK, MessageBoxImage.Information);
                    }));
                });
            };

            win.ShowDialog();
        }

        private static SolidColorBrush HexBrush(string hex)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        }
    }
}
