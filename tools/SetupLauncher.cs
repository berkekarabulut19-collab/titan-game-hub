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
using System.Windows.Threading;

[assembly: AssemblyTitle("Titan Game Hub Setup")]
[assembly: AssemblyDescription("Titan Game Hub Installer")]
[assembly: AssemblyCompany("Titan")]
[assembly: AssemblyProduct("Titan Game Hub Setup")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Titan")]
[assembly: AssemblyVersion("9.4.0.0")]
[assembly: AssemblyFileVersion("9.4.0.0")]

namespace TitanSetup
{
    public class SetupWindow : Window
    {
        private ProgressBar _pb;
        private TextBlock _txtStep;
        private TextBlock _txtDetail;
        private Button _btnAction;
        private string _rootDir;
        private bool _done;

        [STAThread]
        public static void Main()
        {
            Application app = new Application();
            app.Run(new SetupWindow());
        }

        public SetupWindow()
        {
            _rootDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            // Eğer başka PC'de Masaüstü veya İndirilenler'den tek başına çalıştırılıyorsa C:\TitanGameHub'a kur
            string lowerRoot = _rootDir.ToLowerInvariant();
            if (!File.Exists(Path.Combine(_rootDir, "bin", "TitanGameHub.exe")) &&
                (lowerRoot.EndsWith("\\desktop") || lowerRoot.EndsWith("\\downloads") || lowerRoot.Contains("\\temp")))
            {
                _rootDir = @"C:\TitanGameHub";
            }

            this.Title = "Titan Game Hub // Kurulum";
            this.Width = 510;
            this.Height = 320;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.Opacity = 0;
            this.FontFamily = new FontFamily("Segoe UI");

            string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "app.ico");
            if (!File.Exists(ico)) ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "app.ico");
            if (!File.Exists(ico)) ico = Path.Combine(_rootDir, "bin", "app.ico");
            if (File.Exists(ico))
            {
                try { this.Icon = BitmapFrame.Create(new Uri(ico, UriKind.Absolute)); } catch { }
            }

            Border card = new Border()
            {
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#151026"),
                    (Color)ColorConverter.ConvertFromString("#0B0814"),
                    90),
                BorderBrush = BrushFromHex("#7C3AED"),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(16),
                Padding = new Thickness(22),
                Effect = new DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString("#7C3AED"),
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 0.55
                }
            };
            card.MouseLeftButtonDown += delegate { try { this.DragMove(); } catch { } };
            this.Content = card;

            this.Loaded += delegate
            {
                DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320));
                this.BeginAnimation(Window.OpacityProperty, fade);
            };

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            card.Child = g;

            // Header with logo + title + close button
            Grid hdrGrid = new Grid() { Margin = new Thickness(0, 0, 0, 16) };
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            StackPanel hdr = new StackPanel() { Orientation = Orientation.Horizontal };
            string png = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "app_logo.png");
            if (!File.Exists(png)) png = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "app_logo.png");
            if (File.Exists(png))
            {
                try
                {
                    Image img = new Image()
                    {
                        Source = new BitmapImage(new Uri(png, UriKind.Absolute)),
                        Width = 40,
                        Height = 40,
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
                FontSize = 18,
                FontWeight = FontWeights.ExtraBold
            });
            titleStack.Children.Add(new TextBlock()
            {
                Text = "Sürüm v9.4 • Akıllı Kurulum & Masaüstü Kısayolu",
                Foreground = BrushFromHex("#C084FC"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });
            hdr.Children.Add(titleStack);
            Grid.SetColumn(hdr, 0);
            hdrGrid.Children.Add(hdr);

            Button btnClose = new Button()
            {
                Content = "✕",
                Width = 26,
                Height = 26,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent,
                Foreground = BrushFromHex("#9CA3AF"),
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnClose.Click += delegate { this.Close(); };
            Grid.SetColumn(btnClose, 1);
            hdrGrid.Children.Add(btnClose);

            Grid.SetRow(hdrGrid, 0);
            g.Children.Add(hdrGrid);

            // Center progress
            StackPanel mid = new StackPanel() { VerticalAlignment = VerticalAlignment.Center };
            _txtStep = new TextBlock()
            {
                Text = "Kuruluma hazır. Başlatmak için aşağıdaki butona tıklayın.",
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
                Foreground = BrushFromHex("#9333EA"),
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
            Grid.SetRow(mid, 1);
            g.Children.Add(mid);

            // Action Button
            _btnAction = new Button()
            {
                Content = "KURULUMU BAŞLAT",
                Height = 36,
                Background = BrushFromHex("#7C3AED"),
                Foreground = Brushes.White,
                BorderBrush = BrushFromHex("#A855F7"),
                BorderThickness = new Thickness(1),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _btnAction.Click += OnActionClick;
            Grid.SetRow(_btnAction, 2);
            g.Children.Add(_btnAction);
        }

        private void OnActionClick(object sender, RoutedEventArgs e)
        {
            if (_done)
            {
                LaunchAppAndClose();
                return;
            }
            _btnAction.IsEnabled = false;
            _btnAction.Content = "KURULUYOR...";
            ThreadPool.QueueUserWorkItem(delegate { RunInstall(); });
        }

        private void RunInstall()
        {
            try
            {
                string binDir = Path.Combine(_rootDir, "bin");
                string exePath = Path.Combine(binDir, "TitanGameHub.exe");
                string resDir = Path.Combine(_rootDir, "res");

                SetProgress(20, "[1/4] Sistem klasörleri hazırlanıyor...", binDir);
                Directory.CreateDirectory(binDir);
                Directory.CreateDirectory(Path.Combine(binDir, "Games"));
                Directory.CreateDirectory(Path.Combine(binDir, "data"));
                Directory.CreateDirectory(Path.Combine(binDir, "cache"));
                Thread.Sleep(250);

                SetProgress(45, "[2/4] Uygulama ve bileşenler doğrulanıyor...", "TitanGameHub.exe + WebView2 + aria2c");
                CopyIfExists(Path.Combine(resDir, "app.ico"), Path.Combine(binDir, "app.ico"));
                CopyIfExists(Path.Combine(resDir, "app_logo.png"), Path.Combine(binDir, "app_logo.png"));
                CopyIfExists(Path.Combine(resDir, "aria2", "aria2c.exe"), Path.Combine(binDir, "aria2c.exe"));
                string wvDir = Path.Combine(resDir, "webview2");
                if (Directory.Exists(wvDir))
                {
                    foreach (string dll in Directory.GetFiles(wvDir, "*.dll"))
                        CopyIfExists(dll, Path.Combine(binDir, Path.GetFileName(dll)));
                }

                if (!File.Exists(exePath))
                {
                    SetProgress(50, "[2/4] Güncel paket GitHub üzerinden indiriliyor...", "titan-game-hub (main)");
                    BootstrapFromGitHub(_rootDir);
                }

                if (!File.Exists(exePath))
                {
                    SetProgress(65, "[2/4] TitanGameHub.exe derleniyor...", "csc.exe (.NET 4.x)");
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
                    SetProgress(0, "Kurulum tamamlanamadı.", "bin\\TitanGameHub.exe bulunamadı.");
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _btnAction.IsEnabled = true;
                        _btnAction.Content = "TEKRAR DENE";
                    }));
                    return;
                }

                SetProgress(80, "[3/4] Masaüstü ve Başlat Menüsü kısayolu oluşturuluyor...", "Titan Game Hub.lnk");
                string icoPath = Path.Combine(binDir, "app.ico");
                CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), exePath, binDir, icoPath);
                CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), exePath, binDir, icoPath);
                Thread.Sleep(250);

                SetProgress(100, "[4/4] ✔ Kurulum tamamlandı! Masaüstüne kısayol eklendi.", exePath);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _done = true;
                    _btnAction.IsEnabled = true;
                    _btnAction.Background = BrushFromHex("#16A34A");
                    _btnAction.BorderBrush = BrushFromHex("#4ADE80");
                    _btnAction.Content = "UYGULAMAYI BAŞLAT";
                }));
            }
            catch (Exception ex)
            {
                SetProgress(0, "Kurulum hatası: " + ex.Message, "");
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _btnAction.IsEnabled = true;
                    _btnAction.Content = "TEKRAR DENE";
                }));
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
                DoubleAnimation anim = new DoubleAnimation(_pb.Value, val, TimeSpan.FromMilliseconds(260));
                _pb.BeginAnimation(ProgressBar.ValueProperty, anim);
                _txtStep.Text = step;
                _txtDetail.Text = detail;
            }));
        }

        private static void BootstrapFromGitHub(string targetRoot)
        {
            string tmpZip = Path.Combine(Path.GetTempPath(), "titan_setup_" + Guid.NewGuid().ToString("N") + ".zip");
            string tmpExt = Path.Combine(Path.GetTempPath(), "titan_ext_" + Guid.NewGuid().ToString("N"));
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12;
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Headers.Add("User-Agent", "TitanGameHub-Setup/9.4");
                    wc.DownloadFile("https://github.com/berkekarabulut19-collab/titan-game-hub/archive/refs/heads/main.zip", tmpZip);
                }

                if (!File.Exists(tmpZip) || new FileInfo(tmpZip).Length < 10240) return;
                ZipFile.ExtractToDirectory(tmpZip, tmpExt);

                string[] dirs = Directory.GetDirectories(tmpExt);
                if (dirs.Length == 0) return;
                string extractedRoot = dirs[0];

                foreach (string sub in new string[] { "bin", "res", "src", "tools" })
                {
                    string srcSub = Path.Combine(extractedRoot, sub);
                    string dstSub = Path.Combine(targetRoot, sub);
                    if (Directory.Exists(srcSub)) CopyDirectoryRecursive(srcSub, dstSub);
                }
            }
            catch { }
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
                CopyDirectoryRecursive(sub, Path.Combine(dstDir, Path.GetFileName(sub)));
            }
        }

        private static void CopyIfExists(string src, string dst)
        {
            try { if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst, true); } catch { }
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
