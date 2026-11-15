using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

[assembly: AssemblyTitle("Titan Game Hub Setup")]
[assembly: AssemblyDescription("Titan Game Hub Akıllı Kurulum ve Güncelleme Sihirbazı")]
[assembly: AssemblyCompany("Titan")]
[assembly: AssemblyProduct("Titan Game Hub Setup")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Titan")]
[assembly: AssemblyVersion("14.8.0.0")]
[assembly: AssemblyFileVersion("14.8.0.0")]

namespace TitanSetup
{
    public class SetupWindow : Window
    {
        private ProgressBar _pb;
        private TextBlock _txtStep;
        private TextBlock _txtDetail;
        private TextBlock _txtDirLabel;
        private Button _btnBrowse;
        private Button _btnAction;
        private CheckBox _chkShortcut;
        private CheckBox _chkAutoLaunch;
        private string _rootDir;
        private bool _isStandalone;
        private bool _done;

        [STAThread]
        public static void Main()
        {
            Application app = new Application();
            app.Run(new SetupWindow());
        }

        public SetupWindow()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            _isStandalone = !File.Exists(Path.Combine(baseDir, "bin", "TitanGameHub.exe"));
            string lowerRoot = baseDir.ToLowerInvariant();
            if (_isStandalone && (lowerRoot.EndsWith("\\desktop") || lowerRoot.EndsWith("\\downloads") || lowerRoot.Contains("\\temp") || lowerRoot.Contains("\\appdata")))
                _rootDir = @"C:\TitanGameHub";
            else
                _rootDir = baseDir;

            this.Title = "Titan Game Hub // Kurulum Sihirbazı";
            this.Width = 560;
            this.Height = 390;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.Opacity = 0;
            this.FontFamily = new FontFamily("Segoe UI");

            string ico = Path.Combine(baseDir, "res", "app.ico");
            if (!File.Exists(ico)) ico = Path.Combine(baseDir, "bin", "app.ico");
            if (!File.Exists(ico)) ico = Path.Combine(_rootDir, "bin", "app.ico");
            if (File.Exists(ico))
            {
                try { this.Icon = BitmapFrame.Create(new Uri(ico, UriKind.Absolute)); } catch { }
            }

            Border card = new Border()
            {
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#151026"),
                    (Color)ColorConverter.ConvertFromString("#090712"),
                    90),
                BorderBrush = BrushFromHex("#7C3AED"),
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(16),
                Padding = new Thickness(24),
                Effect = new DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString("#7C3AED"),
                    BlurRadius = 24,
                    ShadowDepth = 0,
                    Opacity = 0.55
                }
            };
            card.MouseLeftButtonDown += delegate { try { this.DragMove(); } catch { } };
            this.Content = card;

            this.Loaded += delegate
            {
                DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280));
                this.BeginAnimation(Window.OpacityProperty, fade);
            };

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            card.Child = g;

            // Header
            Grid hdrGrid = new Grid() { Margin = new Thickness(0, 0, 0, 16) };
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            StackPanel hdr = new StackPanel() { Orientation = Orientation.Horizontal };
            string png = Path.Combine(baseDir, "res", "app_logo.png");
            if (!File.Exists(png)) png = Path.Combine(baseDir, "bin", "app_logo.png");
            if (File.Exists(png))
            {
                try
                {
                    Image img = new Image()
                    {
                        Source = new BitmapImage(new Uri(png, UriKind.Absolute)),
                        Width = 44,
                        Height = 44,
                        Margin = new Thickness(0, 0, 12, 0)
                    };
                    hdr.Children.Add(img);
                }
                catch { }
            }

            StackPanel titleStack = new StackPanel() { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock()
            {
                Text = "TITAN GAME HUB",
                Foreground = Brushes.White,
                FontSize = 19,
                FontWeight = FontWeights.ExtraBold
            });
            titleStack.Children.Add(new TextBlock()
            {
                Text = "v14.8 • 13.800+ Oyun • Otomatik Kurulum & Güncelleme",
                Foreground = BrushFromHex("#2DD4BF"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });
            hdr.Children.Add(titleStack);
            Grid.SetColumn(hdr, 0);
            hdrGrid.Children.Add(hdr);

            Button btnClose = new Button()
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent,
                Foreground = BrushFromHex("#9CA3AF"),
                BorderThickness = new Thickness(0),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnClose.Click += delegate { this.Close(); };
            Grid.SetColumn(btnClose, 1);
            hdrGrid.Children.Add(btnClose);
            Grid.SetRow(hdrGrid, 0);
            g.Children.Add(hdrGrid);

            // Install path selector + options
            Border dirBox = new Border()
            {
                Background = BrushFromHex("#0F0C1D"),
                BorderBrush = BrushFromHex("#2E234D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 14)
            };
            StackPanel dirPanel = new StackPanel();
            Grid pathRow = new Grid();
            pathRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            _txtDirLabel = new TextBlock()
            {
                Text = "Kurulum Klasörü: " + _rootDir,
                Foreground = BrushFromHex("#E5E7EB"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(_txtDirLabel, 0);
            pathRow.Children.Add(_txtDirLabel);

            _btnBrowse = new Button()
            {
                Content = "DEĞİŞTİR",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(8, 0, 0, 0),
                Background = BrushFromHex("#1E1B4B"),
                Foreground = BrushFromHex("#C084FC"),
                BorderBrush = BrushFromHex("#5B21B6"),
                BorderThickness = new Thickness(1),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _btnBrowse.Click += OnBrowseClick;
            Grid.SetColumn(_btnBrowse, 1);
            pathRow.Children.Add(_btnBrowse);
            dirPanel.Children.Add(pathRow);

            StackPanel optRow = new StackPanel() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            _chkShortcut = new CheckBox()
            {
                Content = "Masaüstü & Başlat Kısayolu",
                IsChecked = true,
                Foreground = BrushFromHex("#CBD5E1"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 18, 0)
            };
            _chkAutoLaunch = new CheckBox()
            {
                Content = "Kurulum bitince otomatik başlat",
                IsChecked = true,
                Foreground = BrushFromHex("#CBD5E1"),
                FontSize = 11
            };
            optRow.Children.Add(_chkShortcut);
            optRow.Children.Add(_chkAutoLaunch);
            dirPanel.Children.Add(optRow);
            dirBox.Child = dirPanel;
            Grid.SetRow(dirBox, 1);
            g.Children.Add(dirBox);

            // Progress Area
            StackPanel mid = new StackPanel() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 12) };
            _txtStep = new TextBlock()
            {
                Text = _isStandalone ? "GitHub üzerinden en güncel sürüm indirilecek ve kurulacak." : "Kurulum & bileşen doğrulamasına hazır.",
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            mid.Children.Add(_txtStep);

            _pb = new ProgressBar()
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Height = 14,
                Foreground = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#9333EA"),
                    (Color)ColorConverter.ConvertFromString("#14B8A6"),
                    0),
                Background = BrushFromHex("#0B0814"),
                BorderBrush = BrushFromHex("#2E234D"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            mid.Children.Add(_pb);

            _txtDetail = new TextBlock()
            {
                Text = "Hedef: " + Path.Combine(_rootDir, "bin", "TitanGameHub.exe"),
                Foreground = BrushFromHex("#9CA3AF"),
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            mid.Children.Add(_txtDetail);
            Grid.SetRow(mid, 2);
            g.Children.Add(mid);

            // Action Button
            _btnAction = new Button()
            {
                Content = _isStandalone ? "İNDİR VE KUR" : "KURULUMU BAŞLAT / ONAR",
                Height = 38,
                Background = BrushFromHex("#7C3AED"),
                Foreground = Brushes.White,
                BorderBrush = BrushFromHex("#A855F7"),
                BorderThickness = new Thickness(1),
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _btnAction.Click += OnActionClick;
            Grid.SetRow(_btnAction, 3);
            g.Children.Add(_btnAction);
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Titan Game Hub kurulum klasörünü seçin";
                dlg.SelectedPath = Directory.Exists(_rootDir) ? _rootDir : @"C:\";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    string picked = dlg.SelectedPath.TrimEnd('\\');
                    if (!picked.EndsWith("TitanGameHub", StringComparison.OrdinalIgnoreCase) &&
                        !File.Exists(Path.Combine(picked, "bin", "TitanGameHub.exe")))
                    {
                        picked = Path.Combine(picked, "TitanGameHub");
                    }
                    _rootDir = picked;
                    _txtDirLabel.Text = "Kurulum Klasörü: " + _rootDir;
                    _txtDetail.Text = "Hedef: " + Path.Combine(_rootDir, "bin", "TitanGameHub.exe");
                }
            }
        }

        private void OnActionClick(object sender, RoutedEventArgs e)
        {
            if (_done)
            {
                LaunchAppAndClose();
                return;
            }
            bool makeShortcut = (_chkShortcut.IsChecked == true);
            bool autoLaunch = (_chkAutoLaunch.IsChecked == true);
            _btnBrowse.IsEnabled = false;
            _chkShortcut.IsEnabled = false;
            _chkAutoLaunch.IsEnabled = false;
            _btnAction.IsEnabled = false;
            _btnAction.Content = "KURULUYOR...";
            ThreadPool.QueueUserWorkItem(delegate { RunInstall(makeShortcut, autoLaunch); });
        }

        private void RunInstall(bool makeShortcut, bool autoLaunch)
        {
            try
            {
                string binDir = Path.Combine(_rootDir, "bin");
                string exePath = Path.Combine(binDir, "TitanGameHub.exe");
                string resDir = Path.Combine(_rootDir, "res");

                SetProgress(10, "[1/5] Çalışan eski süreçler ve klasörler hazırlanıyor...", binDir);
                KillRunningHubProcesses();
                Directory.CreateDirectory(binDir);
                Directory.CreateDirectory(Path.Combine(binDir, "Games"));
                Directory.CreateDirectory(Path.Combine(binDir, "data"));
                Directory.CreateDirectory(Path.Combine(binDir, "cache"));

                // If running standalone or repairing from GitHub, fetch latest main.zip with live progress
                string localSrcExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), "bin", "TitanGameHub.exe");
                bool sameFolderRepo = string.Equals(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'), _rootDir, StringComparison.OrdinalIgnoreCase)
                                      && File.Exists(localSrcExe);

                if (!sameFolderRepo)
                {
                    SetProgress(18, "[2/5] En güncel Titan Game Hub paketi GitHub'dan indiriliyor...", "Bağlanılıyor...");
                    string errMsg;
                    if (!BootstrapFromGitHub(_rootDir, out errMsg) && !File.Exists(exePath))
                    {
                        FailUi("GitHub indirme hatası: " + errMsg);
                        return;
                    }
                }
                else
                {
                    SetProgress(45, "[2/5] Yerel dosyalar ve bileşenler güncelleniyor...", "WebView2 + aria2c + kaynaklar");
                }

                SetProgress(68, "[3/5] Çekirdek bileşenler doğrulanıyor...", "aria2c.exe + WebView2 + simgeler");
                CopyOverwriteIfExists(Path.Combine(resDir, "app.ico"), Path.Combine(binDir, "app.ico"));
                CopyOverwriteIfExists(Path.Combine(resDir, "app_logo.png"), Path.Combine(binDir, "app_logo.png"));
                CopyOverwriteIfExists(Path.Combine(resDir, "aria2", "aria2c.exe"), Path.Combine(binDir, "aria2c.exe"));
                string wvDir = Path.Combine(resDir, "webview2");
                if (Directory.Exists(wvDir))
                {
                    foreach (string dll in Directory.GetFiles(wvDir, "*.dll"))
                        CopyOverwriteIfExists(dll, Path.Combine(binDir, Path.GetFileName(dll)));
                }

                if (!File.Exists(exePath))
                {
                    SetProgress(74, "[3/5] TitanGameHub.exe derleniyor...", "csc.exe (.NET 4.x)");
                    string compileBat = Path.Combine(_rootDir, "tools", "compile.bat");
                    if (File.Exists(compileBat))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + compileBat + "\"")
                        {
                            WorkingDirectory = _rootDir,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (Process p = Process.Start(psi)) { p.WaitForExit(); }
                    }
                }

                if (!File.Exists(exePath))
                {
                    FailUi("bin\\TitanGameHub.exe oluşturulamadı.");
                    return;
                }

                SetProgress(82, "[4/5] Microsoft Edge WebView2 Runtime kontrol ediliyor...", "Sistem uyumluluğu");
                EnsureWebView2Runtime();

                if (makeShortcut)
                {
                    SetProgress(92, "[5/5] Masaüstü ve Başlat Menüsü kısayolu oluşturuluyor...", "Titan Game Hub.lnk");
                    string icoPath = Path.Combine(binDir, "app.ico");
                    CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), exePath, binDir, icoPath);
                    CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), exePath, binDir, icoPath);
                }

                SetProgress(100, "✔ Kurulum başarıyla tamamlandı! Her şey hazır.", exePath);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _done = true;
                    _btnAction.IsEnabled = true;
                    _btnAction.Background = BrushFromHex("#16A34A");
                    _btnAction.BorderBrush = BrushFromHex("#4ADE80");
                    _btnAction.Content = "UYGULAMAYI BAŞLAT";
                    if (autoLaunch)
                        LaunchAppAndClose();
                }));
            }
            catch (Exception ex)
            {
                FailUi(ex.Message);
            }
        }

        private void FailUi(string msg)
        {
            SetProgress(0, "Kurulum tamamlanamadı.", msg ?? "Bilinmeyen hata");
            Dispatcher.BeginInvoke(new Action(delegate
            {
                _btnBrowse.IsEnabled = true;
                _chkShortcut.IsEnabled = true;
                _chkAutoLaunch.IsEnabled = true;
                _btnAction.IsEnabled = true;
                _btnAction.Content = "TEKRAR DENE";
            }));
        }

        private static void KillRunningHubProcesses()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("TitanGameHub");
                foreach (Process p in procs)
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }
            catch { }
        }

        private static bool IsWebView2Installed()
        {
            string[] keys = new string[]
            {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
            };
            foreach (string k in keys)
            {
                try
                {
                    using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(k))
                    {
                        if (rk != null && !string.IsNullOrEmpty(Convert.ToString(rk.GetValue("pv")))) return true;
                    }
                    using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(k))
                    {
                        if (rk != null && !string.IsNullOrEmpty(Convert.ToString(rk.GetValue("pv")))) return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private void EnsureWebView2Runtime()
        {
            if (IsWebView2Installed()) return;
            string bs = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebView2Setup.exe");
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12;
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.DownloadFile("https://go.microsoft.com/fwlink/p/?LinkId=2124703", bs);
                }
                if (File.Exists(bs) && new FileInfo(bs).Length > 100000)
                {
                    SetProgress(86, "[4/5] WebView2 Runtime kuruluyor...", "Microsoft Edge WebView2");
                    ProcessStartInfo psi = new ProcessStartInfo(bs, "/silent /install")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (Process p = Process.Start(psi)) { p.WaitForExit(90000); }
                }
            }
            catch { }
            finally
            {
                try { if (File.Exists(bs)) File.Delete(bs); } catch { }
            }
        }

        private void LaunchAppAndClose()
        {
            try
            {
                string exePath = Path.Combine(_rootDir, "bin", "TitanGameHub.exe");
                ProcessStartInfo psi = new ProcessStartInfo(exePath)
                {
                    WorkingDirectory = Path.Combine(_rootDir, "bin"),
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch { }
            this.Close();
        }

        private void SetProgress(int val, string step, string detail)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                DoubleAnimation anim = new DoubleAnimation(_pb.Value, val, TimeSpan.FromMilliseconds(220));
                _pb.BeginAnimation(ProgressBar.ValueProperty, anim);
                _txtStep.Text = step;
                _txtDetail.Text = detail;
            }));
        }

        private bool BootstrapFromGitHub(string targetRoot, out string errorMsg)
        {
            errorMsg = "";
            string tmpZip = Path.Combine(Path.GetTempPath(), "titan_setup_" + Guid.NewGuid().ToString("N") + ".zip");
            string tmpExt = Path.Combine(Path.GetTempPath(), "titan_ext_" + Guid.NewGuid().ToString("N"));
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 32;

                bool downloaded = false;
                for (int attempt = 0; attempt < 3 && !downloaded; attempt++)
                {
                    try
                    {
                        if (File.Exists(tmpZip)) File.Delete(tmpZip);
                        using (ManualResetEvent doneEvt = new ManualResetEvent(false))
                        using (WebClient wc = new WebClient())
                        {
                            wc.Proxy = null;
                            wc.Headers.Add("User-Agent", "TitanGameHub-Setup/14.8");
                            Stopwatch sw = Stopwatch.StartNew();
                            long lastReportMs = 0;
                            Exception asyncErr = null;

                            wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs ev)
                            {
                                long now = sw.ElapsedMilliseconds;
                                if (now - lastReportMs < 200) return;
                                lastReportMs = now;
                                double mb = ev.BytesReceived / 1048576.0;
                                double totMb = ev.TotalBytesToReceive > 0 ? (ev.TotalBytesToReceive / 1048576.0) : 0;
                                double spd = sw.Elapsed.TotalSeconds > 0.2 ? (mb / sw.Elapsed.TotalSeconds) : 0;
                                int pct = ev.TotalBytesToReceive > 0 ? (18 + (int)((ev.ProgressPercentage * 44) / 100.0)) : 38;
                                string det = totMb > 0
                                    ? string.Format("{0:0.0} / {1:0.0} MB ({2:0.0} MB/sn)", mb, totMb, spd)
                                    : string.Format("{0:0.0} MB indirildi ({1:0.0} MB/sn)", mb, spd);
                                SetProgress(pct, "[2/5] En güncel Titan Game Hub paketi indiriliyor...", det);
                            };
                            wc.DownloadFileCompleted += delegate(object s, System.ComponentModel.AsyncCompletedEventArgs ev)
                            {
                                asyncErr = ev.Error;
                                doneEvt.Set();
                            };
                            wc.DownloadFileAsync(new Uri("https://github.com/berkekarabulut19-collab/titan-game-hub/archive/refs/heads/main.zip"), tmpZip);
                            doneEvt.WaitOne();
                            if (asyncErr != null) throw asyncErr;
                            downloaded = File.Exists(tmpZip) && new FileInfo(tmpZip).Length > 102400;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorMsg = ex.Message;
                        if (attempt < 2) Thread.Sleep(700);
                    }
                }

                if (!downloaded) return false;

                SetProgress(64, "[2/5] Kurulum paketi çıkartılıyor...", "Dosyalar yerleştiriliyor...");
                ZipFile.ExtractToDirectory(tmpZip, tmpExt);

                string[] dirs = Directory.GetDirectories(tmpExt);
                if (dirs.Length == 0) { errorMsg = "Arşiv içeriği boş."; return false; }
                string extractedRoot = dirs[0];

                foreach (string sub in new string[] { "bin", "res", "src", "tools" })
                {
                    string srcSub = Path.Combine(extractedRoot, sub);
                    string dstSub = Path.Combine(targetRoot, sub);
                    if (Directory.Exists(srcSub)) CopyDirectoryRecursive(srcSub, dstSub);
                }
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
                try { if (Directory.Exists(tmpExt)) Directory.Delete(tmpExt, true); } catch { }
            }
        }

        private static void CopyDirectoryRecursive(string srcDir, string dstDir)
        {
            Directory.CreateDirectory(dstDir);
            foreach (string file in Directory.GetFiles(srcDir))
            {
                string name = Path.GetFileName(file);
                if (string.Equals(name, "titan_user.cfg", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(dstDir, name), true);
            }
            foreach (string sub in Directory.GetDirectories(srcDir))
            {
                string subName = Path.GetFileName(sub);
                if (string.Equals(subName, "data", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(subName, "Games", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(subName, "cache", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.Combine(dstDir, subName));
                    continue;
                }
                CopyDirectoryRecursive(sub, Path.Combine(dstDir, subName));
            }
        }

        private static void CopyOverwriteIfExists(string src, string dst)
        {
            try
            {
                if (File.Exists(src) && !string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
                    File.Copy(src, dst, true);
            }
            catch { }
        }

        private static void CreateShortcut(string folder, string targetExe, string workDir, string iconPath)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                string lnkPath = Path.Combine(folder, "Titan Game Hub.lnk");
                object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type scType = sc.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { targetExe });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                if (File.Exists(iconPath))
                    scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { iconPath + ",0" });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Titan Game Hub" });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
