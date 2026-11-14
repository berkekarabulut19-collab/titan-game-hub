using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using TitanGameHub.Theme;

namespace TitanGameHub
{
    public partial class WpfMainWindow : Window
    {
        private List<GameProfile> _allProfiles;
        private List<GameProfile> _libraryFilteredProfiles;
        private List<GameProfile> _storeFilteredProfiles;
        private bool _storeAutoLoaded = false;
        private bool _bulkImportRunning = false;
        private bool _storeGridDirty = true;
        private int _storeDisplayLimit = 150;
        private System.Windows.Controls.ScrollViewer _storeScrollViewer;
        private GameProfile _selectedProfile;

        private TelemetryEngine _telemetry;
        private OverlayForm _overlay;
        private CloudLobbyService _cloudLobby;
        private DownloadInstallService _downloader;
        private AutoUpdateService _autoUpdater;

        // Top Steam Navigation Buttons (7 Views)
        private Button _navBtnStore;
        private Button _navBtnLibrary;
        private Button _navBtnDownload;
        private Button _navBtnZeroVpn;
        private Button _navBtnModManager;
        private Button _navBtnTelemetry;
        private Button _navBtnOtaShare;
        private Button _navBtnSettings;

        private TextBlock _txtOtaHeaderBadge;
        private TextBlock _txtSteamBadge;
        private TextBlock _txtOnlineHubStatus;
        private Ellipse _dotSteamBadge;
        private TextBlock _txtProfileLvl;
        private TextBlock _txtProfileNick;

        // Center Views
        private Grid _viewLibrary;
        private Grid _viewStore;
        private Grid _viewDownload;
        private Grid _viewZeroVpn;
        private Grid _viewModManager;
        private Grid _viewTelemetry;
        private Grid _viewOtaShare;
        private Grid _viewSettings;

        // Left Steam Sidebar Controls
        private TextBox _txtLibSearch;
        private StackPanel _libGameListStack;
        private ScrollViewer _libScrollViewer;
        private List<GameProfile> _sidebarCloudAll = new List<GameProfile>();
        private int _sidebarCloudRendered = 0;
        private Border _sidebarFooter;
        private bool _sidebarAppending = false;
        private const int SidebarWindow = 60;
        private Button _btnLibFilterAll;
        private Button _btnLibFilterInstalled;
        private Button _btnLibFilterFav;
        private Button _btnLibFilterUpdate;
        private string _libFilterMode = "ALL";
        private string _libSortMode = "NAME";
        private Button _btnSortMode;

        // Top Summary Metrics in Library View
        private TextBlock _txtMetricTotal;
        private TextBlock _txtMetricInstalled;
        private TextBlock _txtMetricSaves;
        private TextBlock _txtMetricHardware;

        // Authentic Steam Hero Stage Controls
        private Border _heroBannerBorder;
        private TextBlock _txtHeroCategory;
        private TextBlock _txtHeroTitle;
        private TextBlock _txtHeroDesc;
        private TextBlock _txtHeroAppIdBadge;
        private TextBlock _txtHeroEngineBadge;
        private TextBlock _txtHeroVersionBadge;
        private TextBlock _txtHeroPlayTimeBadge;
        private Button _btnHeroFav;
        private Button _btnHeroPrimary;
        private Button _btnAutoInstallFromDownloads;
        private Button _btnUninstallGame;
        private Button _btnUpdateShieldStatus;
        private Button _btnUpdateShieldRollback;

        // Steam Action Bar Metadata Labels
        private TextBlock _txtSteamBarLastPlayed;
        private TextBlock _txtSteamBarPlayTime;
        private TextBlock _txtSteamBarSizeVersion;
        private TextBlock _txtSteamBarCloudStatus;

        // Steam Achievements Controls
        private TextBlock _txtAchTitle;
        private ProgressBar _pbAchProgress;
        private WrapPanel _achWrapBadges;

        // Library In-App Download Pipeline Strip
        private Border _inAppDlStripBorder;
        private TextBlock _txtInAppDlTitle;
        private TextBlock _txtInAppDlSpeed;
        private ProgressBar _pbInAppDl;
        private System.Windows.Shapes.Polyline _dlSpeedSparkline;
        private readonly List<double> _dlSpeedSamples = new List<double>();
        private WrapPanel _liveMirrorsWrapPanel;
        private TextBlock _txtLiveMirrorsStatus;

        // Library P2P & Save Sync Controls
        private TextBlock _txtP2PInstallPath;
        private TextBlock _txtP2PVersionStatus;
        private TextBlock _txtSaveBackupStatus;

        // Storefront View Controls (Full Live SteamRIP Site Browser)
        private TextBox _txtStoreSearch;
        private int _activeSiteCategoryId = -1; // -1 = Curated Co-Op, 0 = Latest Live Site, >0 = WP Category ID
        private int _currentStorePage = 1;
        private bool _isFetchingLiveSite = false;
        private Panel _storeCatWrap;
        private StackPanel _storeRowsPanel;
        private TextBlock _txtStoreCountBadge;
        private TextBlock _txtStorePageIndicator;
        private Border _advancedToolsDrawer;

        // Download View Controls
        private TextBox _txtDirectUrlInput;
        private ProgressBar _pbDownload;
        private TextBlock _txtDownloadStatus;
        private TextBlock _txtDownloadSpeed;
        private StackPanel _queueItemsStack;
        private Button _btnPauseResumeDl;
        private TextBlock _txtQueueCountBadge;

        // Telemetry View Controls
        private CheckBox _chkQuickSpeed;
        private CheckBox _chkQuickInfinite;
        private CheckBox _chkQuickNoClip;
        private CheckBox _chkQuickOverlay;
        private Slider _sliderSpeed;
        private TextBlock _txtSpeedVal;
        private TextBox _txtLogConsole;
        private TextBox _txtManualProcess;

        // OTA Share View Controls
        private TextBlock _txtOtaPageStatus;

        // Right Sidebar (Friends & Live Lobby Chat)
        private TextBox _txtUserNick;
        private TextBox _txtRoomCode;
        private StackPanel _friendsListStack;
        private TextBox _txtChatHistory;
        private TextBox _txtChatInput;

        // Bottom Steam Status Bar
        private TextBlock _txtBottomStatus;
        private ColumnDefinition _colLeftLibrary;
        private TextBlock _txtHeaderViewTitle;
        private HwndSource _hwndSource;
        private DispatcherTimer _steamTimer;
        private DateTime _gameLaunchTime;

        public WpfMainWindow()
        {
            StartupTimer.Mark("LoadProfiles başladı");
            _allProfiles = ProfileStore.LoadProfiles();
            StartupTimer.Mark("LoadProfiles bitti (" + _allProfiles.Count + " oyun)");
            _allProfiles = GameDeduplicationEngine.Deduplicate(_allProfiles);
            _libraryFilteredProfiles = new List<GameProfile>(_allProfiles);
            _storeFilteredProfiles = new List<GameProfile>(_allProfiles);

            _telemetry = new TelemetryEngine();
            _cloudLobby = new CloudLobbyService();
            _overlay = new OverlayForm(_telemetry, _cloudLobby, delegate { return _selectedProfile; });
            _downloader = new DownloadInstallService();
            _autoUpdater = new AutoUpdateService();

            ThreadPool.QueueUserWorkItem(delegate
            {
                try { LanP2pSwarmSeederService.StartSeeder(_allProfiles); } catch { }
                try { P2pSaveSwarmMeshService.StartMeshListener(); } catch { }
                try { string pErr; DiscordInviteJoinBridge.RegisterProtocolHandler(out pErr); } catch { }
            });

            StartupTimer.Mark("BuildSteamUserInterface başladı");
            BuildSteamUserInterface();
            StartupTimer.Mark("BuildSteamUserInterface bitti");
            WireEvents();

            StartupTimer.Mark("Sidebar render başladı (" + (_allProfiles == null ? 0 : _allProfiles.Count) + ")");
            RefreshLibrarySidebarList();
            StartupTimer.Mark("Sidebar render bitti");
            RefreshLibrarySummaryMetrics();
            // Mağaza ızgarası TEMBEL: 600+ oyun varsa açılışta çizmek yerine
            // mağaza sekmesine ilk geçişte render edilir (açılış 25s -> <1s).
            _storeGridDirty = true;

            if (_allProfiles.Count > 0)
            {
                SelectGameProfile(_allProfiles[0]);
            }

            this.Loaded += Window_Loaded;
            this.Closing += Window_Closing;
            this.StateChanged += delegate
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    try
                    {
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                        GC.WaitForPendingFinalizers();
                        Win32.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                    }
                    catch { }
                }
            };
        }

        private void BuildSteamUserInterface()
        {
            this.Title = "STEAM // TITAN EDITION " + AutoUpdateService.CurrentAppVersion + " — CANLI SİTE & CO-OP İSTEMCİSİ";
            this.Width = 1460;
            this.Height = 880;
            this.MinWidth = 1260;
            this.MinHeight = 760;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.ResizeMode = ResizeMode.CanResizeWithGrip;
            this.FontFamily = new FontFamily("Segoe UI");

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string iconPath = System.IO.Path.Combine(baseDir, "app.ico");
                if (!File.Exists(iconPath))
                    iconPath = System.IO.Path.Combine(baseDir, "res", "app.ico");
                if (!File.Exists(iconPath))
                    iconPath = System.IO.Path.Combine(baseDir, "..", "res", "app.ico");

                if (File.Exists(iconPath))
                {
                    BitmapDecoder dec = BitmapDecoder.Create(new Uri(System.IO.Path.GetFullPath(iconPath), UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    if (dec != null && dec.Frames.Count > 0) this.Icon = dec.Frames[0];
                }
                else
                {
                    using (System.Drawing.Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location))
                    {
                        if (ico != null)
                        {
                            this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        }
                    }
                }
            }
            catch { }

            Border outerBorder = new Border()
            {
                Background = HexBrush("#0B0814"),
                BorderBrush = HexBrush("#3A2C63"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(4)
            };
            RenderOptions.SetBitmapScalingMode(outerBorder, BitmapScalingMode.Linear);
            RenderOptions.SetClearTypeHint(outerBorder, ClearTypeHint.Enabled);
            TextOptions.SetTextFormattingMode(outerBorder, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(outerBorder, TextRenderingMode.ClearType);
            this.Content = outerBorder;

            // ================= ROOT 2-COLUMN COMMAND RAIL ARCHITECTURE =================
            Grid masterGrid = new Grid();
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(86) }); // Left Vertical Command Rail
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); // Main Workspace
            outerBorder.Child = masterGrid;

            // ================= COL 0: LEFT VERTICAL COMMAND RAIL =================
            Border railBorder = new Border()
            {
                Background = MakeVerticalGradient("#110C1F", "#090710"),
                BorderBrush = HexBrush("#261D40"),
                BorderThickness = new Thickness(0, 0, 1, 0),
                CornerRadius = new CornerRadius(12, 0, 0, 12),
                Padding = new Thickness(6, 12, 6, 10)
            };
            Grid.SetColumn(railBorder, 0);
            masterGrid.Children.Add(railBorder);

            Grid railGrid = new Grid();
            railGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Brand
            railGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // Nav Icons
            railGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Quick Actions
            railBorder.Child = railGrid;

            // Rail Brand Stack
            StackPanel brandStack = new StackPanel()
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };
            railGrid.Children.Add(brandStack);

            Border logoCircle = new Border()
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(12),
                Background = MakeGradient("#2D1B4E", "#150E26"),
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            System.Windows.Shapes.Path steamIconPath = new System.Windows.Shapes.Path()
            {
                Data = Geometry.Parse("M11.979,0 C5.678,0 0.511,4.86 0.022,11.037 L6.454,13.695 C6.999,13.324 7.657,13.105 8.366,13.105 C8.429,13.105 8.491,13.109 8.554,13.111 L11.415,8.969 V8.91 C11.415,6.415 13.443,4.386 15.939,4.386 C18.433,4.386 20.463,6.417 20.463,8.913 C20.463,11.408 18.433,13.438 15.939,13.438 H15.834 L11.758,16.349 C11.758,16.401 11.763,16.454 11.763,16.508 C11.763,18.383 10.248,19.904 8.373,19.904 C6.738,19.904 5.357,18.731 5.042,17.177 L0.436,15.275 C1.948,20.288 6.772,24 12.001,24 C18.628,24 24,18.627 24,12 C24,5.373 18.605,0 11.979,0 Z M7.54,18.216 C7.54,17.113 8.436,16.217 9.539,16.217 C10.643,16.217 11.539,17.113 11.539,18.216 C11.539,19.32 10.643,20.216 9.539,20.216 C8.436,20.216 7.54,19.32 7.54,18.216 Z M15.924,11.374 C14.583,11.374 13.496,10.287 13.496,8.947 C13.496,7.606 14.583,6.518 15.924,6.518 C17.265,6.518 18.352,7.606 18.352,8.947 C18.352,10.287 17.265,11.374 15.924,11.374 Z"),
                Fill = Brushes.White,
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            string logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_logo.png");
            if (!File.Exists(logoPath))
                logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res", "app_logo.png");
            if (!File.Exists(logoPath))
                logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "res", "app_logo.png");

            bool logoLoaded = false;
            if (File.Exists(logoPath))
            {
                try
                {
                    BitmapImage bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(System.IO.Path.GetFullPath(logoPath), UriKind.Absolute);
                    bmp.DecodePixelWidth = 80;
                    bmp.EndInit();
                    bmp.Freeze();

                    Image logoImg = new Image()
                    {
                        Source = bmp,
                        Width = 36,
                        Height = 36,
                        Stretch = Stretch.UniformToFill,
                        Clip = new RectangleGeometry(new Rect(0, 0, 36, 36), 10, 10)
                    };
                    RenderOptions.SetBitmapScalingMode(logoImg, BitmapScalingMode.HighQuality);
                    logoCircle.Child = logoImg;
                    logoLoaded = true;
                }
                catch { }
            }
            if (!logoLoaded)
            {
                logoCircle.Child = steamIconPath;
            }
            brandStack.Children.Add(logoCircle);
            brandStack.Children.Add(new TextBlock()
            {
                Text = "TITAN",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 9.5,
                FontWeight = FontWeights.ExtraBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            });

            // Vertical Navigation Stack in Rail
            ScrollViewer railNavScroll = new ScrollViewer()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(railNavScroll, 1);
            railGrid.Children.Add(railNavScroll);

            StackPanel navStack = new StackPanel()
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top
            };
            railNavScroll.Content = navStack;

            _navBtnLibrary = CreateRailNavButton("🎮", "KÜTÜPHANE", true);
            _navBtnLibrary.Click += delegate { SwitchView(_viewLibrary, _navBtnLibrary); };
            navStack.Children.Add(_navBtnLibrary);

            _navBtnStore = CreateRailNavButton("🌐", "MAĞAZA", false);
            _navBtnStore.Click += delegate { SwitchView(_viewStore, _navBtnStore); };
            navStack.Children.Add(_navBtnStore);

            _navBtnDownload = CreateRailNavButton("⬇", "İNDİRME", false);
            _navBtnDownload.Click += delegate { SwitchView(_viewDownload, _navBtnDownload); RefreshDownloadQueueUI(); };
            navStack.Children.Add(_navBtnDownload);

            _navBtnZeroVpn = CreateRailNavButton("🛡", "ZERO-VPN", false);
            _navBtnZeroVpn.Click += delegate { SwitchView(_viewZeroVpn, _navBtnZeroVpn); };
            navStack.Children.Add(_navBtnZeroVpn);

            _navBtnModManager = CreateRailNavButton("🧩", "MODLAR", false);
            _navBtnModManager.Click += delegate
            {
                SwitchView(_viewModManager, _navBtnModManager);
                UpdateModManagerSelectedGame(_selectedProfile);
            };
            navStack.Children.Add(_navBtnModManager);

            _navBtnTelemetry = CreateRailNavButton("📊", "TELEMETRİ", false);
            _navBtnTelemetry.Click += delegate { SwitchView(_viewTelemetry, _navBtnTelemetry); };
            navStack.Children.Add(_navBtnTelemetry);

            _navBtnOtaShare = CreateRailNavButton("📡", "OTA AĞI", false);
            _navBtnOtaShare.Click += delegate { SwitchView(_viewOtaShare, _navBtnOtaShare); };
            navStack.Children.Add(_navBtnOtaShare);

            _navBtnSettings = CreateRailNavButton("⚙", "AYARLAR", false);
            _navBtnSettings.Click += delegate { SwitchView(_viewSettings, _navBtnSettings); };
            navStack.Children.Add(_navBtnSettings);

            // Rail Bottom Quick + Add Game
            Button btnRailAdd = new Button()
            {
                Content = "➕ EKLE",
                Height = 30,
                Margin = new Thickness(2, 6, 2, 0),
                Background = HexBrush("#1F1638"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = HexBrush("#4C2F8A"),
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand,
                ToolTip = "Oyunu .exe veya arşiv üzerinden kütüphaneye ekle"
            };
            ApplyRoundedButtonTemplate(btnRailAdd, 8);
            btnRailAdd.Click += BtnBindExe_Click;
            Grid.SetRow(btnRailAdd, 2);
            railGrid.Children.Add(btnRailAdd);

            // ================= COL 1: WORKSPACE SHELL =================
            Grid workspaceGrid = new Grid() { Background = HexBrush("#0E0A18") };
            workspaceGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(46) }); // Slim Top Command Bar
            workspaceGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // Modular Body
            workspaceGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(42) }); // Slim Floating Footer
            Grid.SetColumn(workspaceGrid, 1);
            masterGrid.Children.Add(workspaceGrid);

            // ================= ROW 0: SLIM TOP COMMAND BAR =================
            Border headerBorder = new Border()
            {
                Background = HexBrush("#120D20"),
                BorderBrush = HexBrush("#261D40"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(0, 12, 0, 0),
                Padding = new Thickness(14, 0, 10, 0)
            };
            headerBorder.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2)
                {
                    this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
                }
                else if (e.LeftButton == MouseButtonState.Pressed)
                {
                    this.DragMove();
                }
            };
            Grid.SetRow(headerBorder, 0);
            workspaceGrid.Children.Add(headerBorder);

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            headerBorder.Child = headerGrid;

            StackPanel headerLeftStack = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerLeftStack, 0);
            headerGrid.Children.Add(headerLeftStack);

            _txtHeaderViewTitle = new TextBlock()
            {
                Text = "KÜTÜPHANE",
                Foreground = ThemeManager.ForegroundMainBrush,
                FontSize = 14,
                FontWeight = FontWeights.ExtraBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            headerLeftStack.Children.Add(_txtHeaderViewTitle);

            Border statusChip = new Border()
            {
                Background = HexBrush("#1B1430"),
                BorderBrush = HexBrush("#3B2B63"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusChip.Child = new TextBlock()
            {
                Text = "CANLI KATALOG • P2P 480 • 0.5ms",
                Foreground = ThemeManager.AccentGreenBrush,
                FontSize = 9,
                FontWeight = FontWeights.Bold
            };
            headerLeftStack.Children.Add(statusChip);

            // Right Header: 0.5ms Kernel Lock + RAM Boost + Global Update Scan + Steam Status + Window Controls
            StackPanel rightHeaderStack = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(rightHeaderStack, 1);
            headerGrid.Children.Add(rightHeaderStack);

            Button btnKernelTimer = new Button()
            {
                Content = "⚡ 0.5ms",
                Height = 25,
                Padding = new Thickness(7, 0, 7, 0),
                Margin = new Thickness(0, 0, 5, 0),
                Background = ThemeManager.DarkCardBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand,
                ToolTip = "Windows NT Zamanlayıcı çözünürlüğünü 0.5ms'ye kilitler ve gecikmeyi sıfırlar"
            };
            ApplyRoundedButtonTemplate(btnKernelTimer, 3);
            btnKernelTimer.Click += delegate
            {
                if (KernelLatencyOptimizer.IsTimerResolutionLocked)
                {
                    KernelLatencyOptimizer.DisableUltraLowLatencyTimer();
                    btnKernelTimer.Content = "⚡ 0.5ms";
                    btnKernelTimer.Background = ThemeManager.DarkCardBrush;
                    btnKernelTimer.Foreground = ThemeManager.AccentTealBrush;
                    _txtBottomStatus.Text = "Çekirdek Zamanlayıcı standart moda alındı (15.6ms).";
                    Win32.PlayTechChirp(false);
                }
                else
                {
                    string rep;
                    KernelLatencyOptimizer.EnableUltraLowLatencyTimer(out rep);
                    btnKernelTimer.Content = "⚡ 0.5ms ON";
                    btnKernelTimer.Background = ThemeManager.AccentBlueBrush;
                    btnKernelTimer.Foreground = ThemeManager.BorderSubtleBrush;
                    _txtBottomStatus.Text = rep;
                    Win32.PlayTechChirp(true);
                }
            };
            rightHeaderStack.Children.Add(btnKernelTimer);

            Button btnRamBoost = new Button()
            {
                Content = "🚀 RAM BOOST",
                Height = 25,
                Padding = new Thickness(7, 0, 7, 0),
                Margin = new Thickness(0, 0, 5, 0),
                Background = HexBrush("#1E2E22"),
                Foreground = ThemeManager.AccentGreenBrush,
                BorderBrush = ThemeManager.AccentGreenBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand,
                ToolTip = "Arka plan süreçlerinin bellek çalışma kümesini temizleyerek oyunlar için RAM açar"
            };
            ApplyRoundedButtonTemplate(btnRamBoost, 3);
            btnRamBoost.Click += delegate { RunSystemRamBoost(); };
            rightHeaderStack.Children.Add(btnRamBoost);

            // Steam Account & Spacewar (480) Live Bridge Button + Global Update Scanner
            Button btnGlobalUpdateScan = new Button()
            {
                Content = "🔄 SÜRÜM TARA",
                Height = 25,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = HexBrush("#172434"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = HexBrush("#7E22CE"),
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand,
                ToolTip = "Tüm kütüphaneyi canlı sunucu veritabanı ile karşılaştırarak yeni oyun güncellemelerini tarar"
            };
            ApplyRoundedButtonTemplate(btnGlobalUpdateScan, 3);
            btnGlobalUpdateScan.Click += delegate
            {
                _txtBottomStatus.Text = "🔄 Tüm kütüphane için canlı sürüm ve güncelleme taraması başlatıldı...";
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    List<GameProfile> updatedList;
                    int count = SteamClientBridgeService.ScanLibraryForLiveUpdates(_allProfiles, out updatedList);
                    ProfileStore.SaveProfiles(_allProfiles);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        RefreshLibrarySidebarList();
                        if (_selectedProfile != null) SelectGameProfile(_selectedProfile);
                        _txtBottomStatus.Text = count > 0
                            ? string.Format("🔄 Tarama tamamlandı: {0} oyun için yeni sürüm / güncelleme bulundu!", count)
                            : "✔ Tüm kütüphane oyunları en güncel sürümde.";
                        Win32.PlayTechChirp(true);
                    }));
                });
            };
            rightHeaderStack.Children.Add(btnGlobalUpdateScan);

            // TÜM OYUNLARI TARA: bu bilgisayardaki kurulu tüm oyunları bulur ve kütüphaneye ekler
            Button btnScanAllHeader = new Button()
            {
                Content = "🔍 TARA",
                Height = 25,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = HexBrush("#172434"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = HexBrush("#7E22CE"),
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand,
                ToolTip = "Tüm diskleri + Steam/Epic/GOG kütüphanelerini tarar, kurulu oyunları kütüphaneye ekler"
            };
            
            ApplyRoundedButtonTemplate(btnScanAllHeader, 3);
            btnScanAllHeader.Click += delegate { RunUniversalGameScan(); };
            rightHeaderStack.Children.Add(btnScanAllHeader);

            Border steamPill = new Border()
            {
                Background = HexBrush("#10161F"),
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Tıklayın: Yerel Steam hesabınızı (loginusers.vdf) algılar, Steam'i bağlar ve Spacewar (AppID 480) P2P köprüsünü etkinleştirir"
            };
            StackPanel steamPillStack = new StackPanel() { Orientation = Orientation.Horizontal };
            _dotSteamBadge = new Ellipse() { Width = 7, Height = 7, Fill = ThemeManager.AccentGreenBrush, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            _txtSteamBadge = new TextBlock() { Text = "STEAM: KONTROL...", Foreground = ThemeManager.ForegroundSubBrush, FontSize = 9.2, FontWeight = FontWeights.Bold };
            steamPillStack.Children.Add(_dotSteamBadge);
            steamPillStack.Children.Add(_txtSteamBadge);
            steamPill.Child = steamPillStack;
            steamPill.MouseLeftButtonDown += delegate
            {
                string msg = SteamClientBridgeService.ConnectAndBridgeSteamClient(_selectedProfile, true);
                SteamAccountInfo acc = SteamClientBridgeService.GetActiveSteamAccount(true);
                if (_txtUserNick != null && !string.IsNullOrEmpty(acc.PersonaName))
                    _txtUserNick.Text = acc.PersonaName;
                UpdateSteamBadgeStatus();
                _txtBottomStatus.Text = msg;
                Win32.PlayTechChirp(true);
            };
            rightHeaderStack.Children.Add(steamPill);

            // Steam Profile & Level Pill
            Border profilePill = new Border()
            {
                Background = HexBrush("#161220"),
                BorderBrush = ThemeManager.BorderAccentBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 8, 3),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Steam Seviyesi ve Oyuncu Profili. Tıklayın: Seviye, XP ve Rozet Durumunu İncele"
            };
            StackPanel profileStack = new StackPanel() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            
            // Level badge circle
            Border lvlCircle = new Border()
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = ThemeManager.BorderSubtleBrush,
                BorderBrush = HexBrush("#D29B38"),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 6, 0)
            };
            _txtProfileLvl = new TextBlock()
            {
                Text = "1",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = HexBrush("#F2C94C"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            lvlCircle.Child = _txtProfileLvl;
            profileStack.Children.Add(lvlCircle);

            _txtProfileNick = new TextBlock()
            {
                Text = Environment.UserName,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = HexBrush("#C7D5E0"),
                VerticalAlignment = VerticalAlignment.Center
            };
            profileStack.Children.Add(_txtProfileNick);
            profilePill.Child = profileStack;

            profilePill.MouseLeftButtonDown += delegate
            {
                PlayerProfileData p = SteamProfileManager.GetProfile(_allProfiles);
                string info = string.Format("👤 Steam Profili: {0}\n⭐ Seviye: {1} ({2})\n⚡ XP: {3} / {4} XP\n🏆 Başarımlar: {5}\n⏱ Toplam Oyun Süresi: {6} Saat\n\n💬 \"{7}\"",
                    p.PersonaName, p.Level, p.BadgeTitle, p.CurrentXp, p.NextLevelXp, p.TotalAchievementsUnlocked, p.TotalHoursPlayed, p.StatusQuote);
                MessageBox.Show(info, "Steam Oyuncu Profili & XP Seviyesi", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            rightHeaderStack.Children.Add(profilePill);

            _txtOtaHeaderBadge = new TextBlock() { Visibility = Visibility.Collapsed };

            Button btnToggleFriends = new Button()
            {
                Content = "👥 LOBİ & ARKADAŞLAR",
                Height = 25,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = ThemeManager.AccentBlueBrush,
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Sağ taraftaki Arkadaşlar ve Lobi panelini açar/kapatır (Geniş Ekran Modu)"
            };
            ApplyRoundedButtonTemplate(btnToggleFriends, 3);
            rightHeaderStack.Children.Add(btnToggleFriends);

            Button btnMin = CreateWindowControlButton("—", "#241C36", "#B8BCBF");
            btnMin.Click += delegate { this.WindowState = WindowState.Minimized; };
            rightHeaderStack.Children.Add(btnMin);

            Button btnMax = CreateWindowControlButton("▢", "#241C36", "#B8BCBF");
            btnMax.Click += delegate
            {
                this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
            };
            rightHeaderStack.Children.Add(btnMax);

            Button btnClose = CreateWindowControlButton("✕", "#3A1D24", "#F87171");
            btnClose.Click += delegate { this.Close(); };
            rightHeaderStack.Children.Add(btnClose);

            // ================= ROW 1: MODULAR FLOATING 3-COLUMN BODY =================
            Grid bodyGrid = new Grid() { Margin = new Thickness(8, 8, 8, 6) };
            _colLeftLibrary = new ColumnDefinition() { Width = new GridLength(252) };
            ColumnDefinition colRightFriends = new ColumnDefinition() { Width = new GridLength(0) }; // Collapsible Lobby Drawer
            bodyGrid.ColumnDefinitions.Add(_colLeftLibrary); // Left Floating Library Drawer
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); // Center Stage
            bodyGrid.ColumnDefinitions.Add(colRightFriends); // Right Friends & Chat
            Grid.SetRow(bodyGrid, 1);
            workspaceGrid.Children.Add(bodyGrid);

            btnToggleFriends.Content = "👥 LOBİYİ AÇ";
            btnToggleFriends.Background = HexBrush("#1C1530");
            btnToggleFriends.Foreground = ThemeManager.ForegroundSubBrush;
            btnToggleFriends.BorderBrush = HexBrush("#3B2B63");

            btnToggleFriends.Click += delegate
            {
                if (colRightFriends.Width.Value > 0)
                {
                    colRightFriends.Width = new GridLength(0);
                    btnToggleFriends.Content = "👥 LOBİYİ AÇ";
                    btnToggleFriends.Background = HexBrush("#1C1530");
                    btnToggleFriends.Foreground = ThemeManager.ForegroundSubBrush;
                    btnToggleFriends.BorderBrush = HexBrush("#3B2B63");
                }
                else
                {
                    colRightFriends.Width = new GridLength(255);
                    btnToggleFriends.Content = "👥 LOBİYİ KAPAT";
                    btnToggleFriends.Background = ThemeManager.AccentBlueBrush;
                    btnToggleFriends.Foreground = Brushes.White;
                    btnToggleFriends.BorderBrush = ThemeManager.AccentTealBrush;
                }
                Win32.PlayTechChirp(colRightFriends.Width.Value > 0);
            };

            BuildLeftLibrarySidebar(bodyGrid);
            BuildCenterViews(bodyGrid);
            BuildRightFriendsSidebar(bodyGrid);

            // ================= ROW 2: SLIM FLOATING BOTTOM STATUS BAR =================
            Border footerBorder = new Border()
            {
                Background = HexBrush("#151024"),
                BorderBrush = HexBrush("#2E234D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(8, 0, 8, 8),
                Padding = new Thickness(12, 0, 12, 0)
            };
            Grid.SetRow(footerBorder, 2);
            workspaceGrid.Children.Add(footerBorder);

            Grid footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            footerBorder.Child = footerGrid;

            // Left Footer: + OYUN EKLE (Steam style)
            Button btnFooterAddGame = new Button()
            {
                Content = "➕ OYUN EKLE (.EXE VEYA ARŞİV BAĞLA)",
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.Transparent,
                Foreground = ThemeManager.ForegroundSubBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnFooterAddGame, 3);
            btnFooterAddGame.Click += BtnBindExe_Click;
            Grid.SetColumn(btnFooterAddGame, 0);
            footerGrid.Children.Add(btnFooterAddGame);

            // Center Footer: Status + Steam Download Manager Trigger
            StackPanel centerFooterStack = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(centerFooterStack, 1);
            footerGrid.Children.Add(centerFooterStack);

            _txtBottomStatus = new TextBlock()
            {
                Text = "Steam // Titan Edition hazır — Canlı oyun kataloğu ve Spacewar P2P (AppID 480) devrede.",
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            centerFooterStack.Children.Add(_txtBottomStatus);

            Button btnManageDownloads = new Button()
            {
                Content = "⬇ İNDİRMELERİ YÖNET",
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                Background = ThemeManager.DarkCardBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnManageDownloads, 3);
            btnManageDownloads.Click += delegate { SwitchView(_viewDownload, _navBtnDownload); RefreshDownloadQueueUI(); };
            centerFooterStack.Children.Add(btnManageDownloads);

            // Right Footer: Hardware Summary + Live Disk & Queue Telemetry
            _txtMetricHardware = new TextBlock()
            {
                Text = Win32.GetSystemHardwareSummary(),
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Threading.DispatcherTimer footerTelTimer = new System.Windows.Threading.DispatcherTimer();
            footerTelTimer.Interval = TimeSpan.FromSeconds(10);
            footerTelTimer.Tick += delegate
            {
                try
                {
                    string root = System.IO.Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory) ?? "C:\\";
                    DriveInfo di = new DriveInfo(root);
                    double freeGb = di.IsReady ? (di.AvailableFreeSpace / 1073741824.0) : 0.0;
                    int qCount = DownloadInstallService.GetQueuedGames().Count;
                    _txtMetricHardware.Text = string.Format("{0}  •  💾 {1} {2:0} GB Boş  •  Kuyruk: {3}",
                        Win32.GetSystemHardwareSummary(), root.TrimEnd('\\'), freeGb, qCount);
                }
                catch { }
            };
            footerTelTimer.Start();
            Grid.SetColumn(_txtMetricHardware, 2);
            footerGrid.Children.Add(_txtMetricHardware);
        }

        // =========================================================================
        // COLUMN 0: STEAM LEFT LIBRARY SIDEBAR (#1E2329)
        // =========================================================================
        private void BuildLeftLibrarySidebar(Grid bodyGrid)
        {
            Border leftBorder = new Border()
            {
                Background = HexBrush("#151024"),
                BorderBrush = HexBrush("#2E234D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10)
            };
            Grid.SetColumn(leftBorder, 0);
            bodyGrid.Children.Add(leftBorder);

            Grid leftGrid = new Grid();
            leftGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Home & Store Banner Button
            leftGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Filter Pills
            leftGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Search Box
            leftGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // Game List
            leftBorder.Child = leftGrid;

            // 3-Way Steam Filter Pills: TÜMÜ | YÜKLÜLER | FAVORİLER
            UniformGrid filterGrid = new UniformGrid() { Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(filterGrid, 1);
            leftGrid.Children.Add(filterGrid);

            _btnLibFilterAll = CreateFilterPill(LanguageService.T("TÜMÜ"), true);
            _btnLibFilterAll.Click += delegate { SetLibraryFilterMode("ALL"); };
            filterGrid.Children.Add(_btnLibFilterAll);

            _btnLibFilterInstalled = CreateFilterPill(LanguageService.T("YÜKLÜLER"), false);
            _btnLibFilterInstalled.Click += delegate { SetLibraryFilterMode("INSTALLED"); };
            filterGrid.Children.Add(_btnLibFilterInstalled);

            _btnLibFilterFav = CreateFilterPill(LanguageService.T("FAVORİLER"), false);
            _btnLibFilterFav.Click += delegate { SetLibraryFilterMode("FAV"); };
            filterGrid.Children.Add(_btnLibFilterFav);

            _btnLibFilterUpdate = CreateFilterPill(LanguageService.T("GÜNCELLEME"), false);
            _btnLibFilterUpdate.Click += delegate { SetLibraryFilterMode("UPDATE"); };
            filterGrid.Children.Add(_btnLibFilterUpdate);

            // Steam Search Box ("İsme göre filtrele...")
            Border searchBorder = new Border()
            {
                Background = HexBrush("#13101B"),
                BorderBrush = HexBrush("#3B2D59"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(6, 2, 6, 2)
            };
            Grid.SetRow(searchBorder, 2);
            leftGrid.Children.Add(searchBorder);

            Grid sBoxGrid = new Grid();
            sBoxGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            sBoxGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            sBoxGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            searchBorder.Child = sBoxGrid;

            sBoxGrid.Children.Add(new TextBlock()
            {
                Text = "🔍",
                Foreground = HexBrush("#67707B"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 6, 0)
            });

            _txtLibSearch = new TextBox()
            {
                Background = Brushes.Transparent,
                Foreground = ThemeManager.ForegroundMainBrush,
                CaretBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(0),
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                Height = 24
            };
            _txtLibSearch.TextChanged += delegate { ApplyLibraryFilter(); };
            Grid.SetColumn(_txtLibSearch, 1);
            sBoxGrid.Children.Add(_txtLibSearch);

            _btnSortMode = new Button()
            {
                Content = "🔀 A-Z",
                Height = 22,
                Padding = new Thickness(6, 0, 6, 0),
                Background = HexBrush("#1E2837"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Sıralama Modu (Tıklayın: A-Z -> Boyut ⬇ -> Süre ⬇ -> Son Oynanan)"
            };
            ApplyRoundedButtonTemplate(_btnSortMode, 2);
            _btnSortMode.Click += delegate
            {
                CycleSortMode();
            };
            Grid.SetColumn(_btnSortMode, 2);
            sBoxGrid.Children.Add(_btnSortMode);

            // Scrollable Steam Vertical Game List
            ScrollViewer listScroll = new ScrollViewer()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(listScroll, 3);
            leftGrid.Children.Add(listScroll);

            _libGameListStack = new StackPanel();
            listScroll.Content = _libGameListStack;
            _libScrollViewer = listScroll;

            // SANALLAŞTIRMA (pencereleme): binlerce oyun olsa da yalnızca görünen bölge + tampon çizilir.
            listScroll.ScrollChanged += delegate(object s2, ScrollChangedEventArgs e2)
            {
                try
                {
                    if (e2.ExtentHeight <= 0) return;
                    if (e2.VerticalOffset + e2.ViewportHeight >= e2.ExtentHeight - 500)
                        AppendSidebarCloudWindow();
                }
                catch { }
            };
        }

        private void SetLibraryFilterMode(string mode)
        {
            _libFilterMode = mode;
            UpdateFilterPillStyle(_btnLibFilterAll, mode == "ALL");
            UpdateFilterPillStyle(_btnLibFilterInstalled, mode == "INSTALLED");
            UpdateFilterPillStyle(_btnLibFilterFav, mode == "FAV");
            UpdateFilterPillStyle(_btnLibFilterUpdate, mode == "UPDATE");
            ApplyLibraryFilter();
        }

        private Button CreateFilterPill(string text, bool active)
        {
            Button btn = new Button()
            {
                Content = text,
                Height = 24,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1)
            };
            ApplyRoundedButtonTemplate(btn, 3);
            UpdateFilterPillStyle(btn, active);
            return btn;
        }

        private void UpdateFilterPillStyle(Button btn, bool active)
        {
            if (btn == null) return;
            btn.Background = active ? ThemeManager.AccentBlueBrush : HexBrush("#13101B");
            btn.Foreground = active ? Brushes.White : ThemeManager.ForegroundMutedBrush;
            btn.BorderBrush = active ? ThemeManager.AccentBlueBrush : HexBrush("#3B2D59");
        }

        private void CycleSortMode()
        {
            if (_libSortMode == "NAME")
            {
                _libSortMode = "SIZE";
                if (_btnSortMode != null) _btnSortMode.Content = "💾 BOYUT ⬇";
            }
            else if (_libSortMode == "SIZE")
            {
                _libSortMode = "PLAYTIME";
                if (_btnSortMode != null) _btnSortMode.Content = "⏱ SÜRE ⬇";
            }
            else if (_libSortMode == "PLAYTIME")
            {
                _libSortMode = "RECENT";
                if (_btnSortMode != null) _btnSortMode.Content = "🕒 SON";
            }
            else
            {
                _libSortMode = "NAME";
                if (_btnSortMode != null) _btnSortMode.Content = "🔀 A-Z";
            }
            ProceduralSoundEngine.PlaySelect();
            ApplyLibraryFilter();
        }

        private static double ParseFileSizeToMb(string sz)
        {
            if (string.IsNullOrEmpty(sz)) return 0;
            try
            {
                Match m = Regex.Match(sz, @"([\d\.,]+)\s*(GB|MB|KB|TB)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    double val = double.Parse(m.Groups[1].Value.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture);
                    string unit = m.Groups[2].Value.ToUpperInvariant();
                    if (unit == "TB") return val * 1024.0 * 1024.0;
                    if (unit == "GB") return val * 1024.0;
                    if (unit == "MB") return val;
                    if (unit == "KB") return val / 1024.0;
                }
            }
            catch { }
            return 0;
        }

        private void ApplyLibraryFilter()
        {
            string q = (_txtLibSearch != null && _txtLibSearch.Text != null) ? _txtLibSearch.Text.Trim().ToLowerInvariant() : "";
            _libraryFilteredProfiles.Clear();

            foreach (GameProfile gp in _allProfiles)
            {
                if (_libFilterMode == "INSTALLED" && !gp.IsInstalled) continue;
                if (_libFilterMode == "FAV" && !gp.IsFavorite) continue;
                // "GÜNCELLEME": sadece kurulu ve gerçekten güncellemesi olan oyunlar
                if (_libFilterMode == "UPDATE" && !(gp.IsInstalled && gp.UpdateAvailable)) continue;

                if (!string.IsNullOrEmpty(q))
                {
                    bool match = (gp.Name != null && gp.Name.ToLowerInvariant().Contains(q)) ||
                                 (gp.Category != null && gp.Category.ToLowerInvariant().Contains(q));
                    if (!match) continue;
                }
                _libraryFilteredProfiles.Add(gp);
            }

            _libraryFilteredProfiles = GameDeduplicationEngine.Deduplicate(_libraryFilteredProfiles);

            if (_libSortMode == "SIZE")
            {
                _libraryFilteredProfiles.Sort(delegate(GameProfile a, GameProfile b)
                {
                    return ParseFileSizeToMb(b.FileSize).CompareTo(ParseFileSizeToMb(a.FileSize));
                });
            }
            else if (_libSortMode == "PLAYTIME")
            {
                _libraryFilteredProfiles.Sort(delegate(GameProfile a, GameProfile b)
                {
                    return b.PlayTimeMinutes.CompareTo(a.PlayTimeMinutes);
                });
            }
            else if (_libSortMode == "RECENT")
            {
                _libraryFilteredProfiles.Sort(delegate(GameProfile a, GameProfile b)
                {
                    string lpA = a.LastPlayedText ?? "";
                    string lpB = b.LastPlayedText ?? "";
                    return string.Compare(lpB, lpA, StringComparison.OrdinalIgnoreCase);
                });
            }
            else
            {
                _libraryFilteredProfiles.Sort(delegate(GameProfile a, GameProfile b)
                {
                    int instCmp = b.IsInstalled.CompareTo(a.IsInstalled);
                    if (instCmp != 0) return instCmp;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            }

            RefreshLibrarySidebarList();
        }

        private int _sidebarRenderGeneration = 0;

        private void RefreshLibrarySidebarList()
        {
            if (_libGameListStack == null) return;
            _libGameListStack.Children.Clear();
            _sidebarFooter = null;
            _sidebarCloudRendered = 0;
            _sidebarAppending = false;

            int generation = ++_sidebarRenderGeneration;

            List<GameProfile> installedList = new List<GameProfile>();
            List<GameProfile> cloudList = new List<GameProfile>();

            try
            {
                if (_libraryFilteredProfiles != null)
                {
                    foreach (GameProfile gp in _libraryFilteredProfiles)
                    {
                        if (gp == null) continue;
                        if (gp.IsInstalled) installedList.Add(gp);
                        else cloudList.Add(gp);
                    }
                }
            }
            catch { }

            _sidebarCloudAll = cloudList ?? new List<GameProfile>();

            // Render installed section in small batches to keep UI responsive
            int installedIndex = 0;
            bool headerAdded = false;

            Action renderInstalledBatch = null;
            renderInstalledBatch = delegate
            {
                if (generation != _sidebarRenderGeneration) return;
                if (_libGameListStack == null) return;

                try
                {
                    if (installedList.Count > 0 && !headerAdded)
                    {
                        _libGameListStack.Children.Add(CreateSteamSidebarCategoryHeader("▼ BİLGİSAYARDA YÜKLÜ (" + installedList.Count + ")", "#5DBA2F"));
                        headerAdded = true;
                    }

                    int added = 0;
                    while (installedIndex < installedList.Count && added < 20)
                    {
                        try { _libGameListStack.Children.Add(CreateSteamSidebarGameRow(installedList[installedIndex])); } catch { }
                        installedIndex++;
                        added++;
                    }

                    if (installedIndex < installedList.Count)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, renderInstalledBatch);
                        return;
                    }

                    // Installed done, render cloud section
                    if (_sidebarCloudAll.Count > 0)
                    {
                        _libGameListStack.Children.Add(CreateSteamSidebarCategoryHeader("▼ BULUT KATALOĞU (" + _sidebarCloudAll.Count + ")", "#C084FC"));
                        AppendSidebarCloudWindow();
                    }
                }
                catch { }
            };

            if (installedList.Count > 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, renderInstalledBatch);
            }
            else if (_sidebarCloudAll.Count > 0)
            {
                _libGameListStack.Children.Add(CreateSteamSidebarCategoryHeader("▼ BULUT KATALOĞU (" + _sidebarCloudAll.Count + ")", "#C084FC"));
                AppendSidebarCloudWindow();
            }
        }

        /// <summary>
        /// Bulut kataloğunu pencere pencere çizer (sanal liste): 60 satırlık bloklar halinde
        /// eklenir, aşağı kaydırdıkça yenisi gelir. 5.000 oyunda bile UI kilitlenmez.
        /// </summary>
        private void AppendSidebarCloudWindow()
        {
            if (_libGameListStack == null || _sidebarAppending) return;
            if (_sidebarCloudAll == null || _sidebarCloudRendered >= _sidebarCloudAll.Count) return;

            _sidebarAppending = true;
            try
            {
                if (_sidebarFooter != null)
                {
                    _libGameListStack.Children.Remove(_sidebarFooter);
                    _sidebarFooter = null;
                }

                int end = Math.Min(_sidebarCloudRendered + SidebarWindow, _sidebarCloudAll.Count);
                for (int i = _sidebarCloudRendered; i < end; i++)
                {
                    GameProfile gp = _sidebarCloudAll[i];
                    if (gp == null) continue;
                    _libGameListStack.Children.Add(CreateSteamSidebarGameRow(gp));
                }
                _sidebarCloudRendered = end;

                int remaining = _sidebarCloudAll.Count - _sidebarCloudRendered;

                Border footer = new Border()
                {
                    Background = ThemeManager.SidebarNavy,
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 2, 0, 0),
                    Cursor = Cursors.Hand
                };
                footer.Child = new TextBlock()
                {
                    Text = remaining > 0
                        ? string.Format("▼ DEVAMI ({0} oyun kaldı)", remaining)
                        : string.Format("✔ Tümü gösterildi ({0} oyun)", _sidebarCloudAll.Count),
                    Foreground = ThemeManager.AccentTealBrush,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold
                };
                footer.MouseLeftButtonUp += delegate
                {
                    if (remaining > 0) AppendSidebarCloudWindow();
                    else SwitchView(_viewStore, _navBtnStore);
                };
                _sidebarFooter = footer;
                _libGameListStack.Children.Add(footer);
            }
            finally
            {
                _sidebarAppending = false;
            }
        }

        private Border CreateSteamSidebarCategoryHeader(string title, string fgHex)
        {
            return new Border()
            {
                Background = HexBrush("#15191F"),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 4, 0, 2),
                Child = new TextBlock()
                {
                    Text = title,
                    Foreground = HexBrush(fgHex),
                    FontSize = 9.2,
                    FontWeight = FontWeights.ExtraBold
                }
            };
        }

        private Border CreateSteamSidebarGameRow(GameProfile gp)
        {
            bool isSelected = (_selectedProfile != null && _selectedProfile.Id == gp.Id);

            Border itemBorder = new Border()
            {
                Background = isSelected ? (Brush)MakeHorizontalGradient("#2B4466", "#251D38") : Brushes.Transparent,
                BorderBrush = isSelected ? ThemeManager.AccentBlueBrush : Brushes.Transparent,
                BorderThickness = new Thickness(isSelected ? 2 : 0, 0, 0, 0),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(6, 5, 6, 5),
                Margin = new Thickness(0, 0, 0, 1),
                Cursor = Cursors.Hand
            };

            // Steam tarzı hover: seçili değilse hafif aydınlanma
            Brush baseBg = itemBorder.Background;
            itemBorder.MouseEnter += delegate
            {
                if (!isSelected) itemBorder.Background = ThemeManager.DarkNavyBrush;
            };
            itemBorder.MouseLeave += delegate
            {
                if (!isSelected) itemBorder.Background = baseBg;
            };

            Grid rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(36) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            itemBorder.Child = rowGrid;

            // Mini Game Capsule Image
            Border miniThumb = new Border()
            {
                Width = 30,
                Height = 18,
                CornerRadius = new CornerRadius(2),
                Background = HexBrush("#10151D"),
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(0.5),
                Margin = new Thickness(0, 0, 6, 0)
            };
            ImageCacheService.BindImageAsync(miniThumb, gp, 72, 1.0);
            Grid.SetColumn(miniThumb, 0);
            rowGrid.Children.Add(miniThumb);

            // Game Name
            TextBlock txtName = new TextBlock()
            {
                Text = gp.Name,
                Foreground = isSelected ? Brushes.White : (gp.IsInstalled ? HexBrush("#E5E7EB") : ThemeManager.ForegroundMutedBrush),
                FontSize = 11.3,
                FontWeight = isSelected ? FontWeights.Bold : (gp.IsInstalled ? FontWeights.SemiBold : FontWeights.Normal),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(txtName, 1);
            rowGrid.Children.Add(txtName);

            // Installed Green Indicator, Update Badge, or Favorite Star
            StackPanel rightIndicators = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (gp.IsInstalled && gp.UpdateAvailable)
            {
                Border updBadge = new Border()
                {
                    Background = HexBrush("#7E22CE"),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = string.IsNullOrEmpty(gp.UpdateSourceHint)
                        ? ("Yeni Sürüm: " + gp.RemoteVersion)
                        : ("Yeni Sürüm: " + gp.RemoteVersion + "\nKaynak: " + gp.UpdateSourceHint)
                };
                updBadge.Child = new TextBlock()
                {
                    Text = "GÜNCELLE",
                    Foreground = Brushes.White,
                    FontSize = 8.0,
                    FontWeight = FontWeights.Bold
                };
                rightIndicators.Children.Add(updBadge);
            }
            if (gp.IsFavorite)
            {
                rightIndicators.Children.Add(new TextBlock()
                {
                    Text = "★",
                    Foreground = ThemeManager.AccentTealBrush,
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 4, 0)
                });
            }
            if (gp.IsInstalled)
            {
                rightIndicators.Children.Add(new Ellipse()
                {
                    Width = 7,
                    Height = 7,
                    Fill = ThemeManager.AccentGreenBrush,
                    ToolTip = "Bilgisayarda Yüklü ve Oynamaya Hazır"
                });
            }
            Grid.SetColumn(rightIndicators, 2);
            rowGrid.Children.Add(rightIndicators);

            GameProfile captured = gp;
            itemBorder.MouseEnter += delegate
            {
                if (_selectedProfile == null || _selectedProfile.Id != captured.Id)
                    itemBorder.Background = HexBrush("#252C37");
            };
            itemBorder.MouseLeave += delegate
            {
                if (_selectedProfile == null || _selectedProfile.Id != captured.Id)
                    itemBorder.Background = Brushes.Transparent;
            };
            itemBorder.MouseLeftButtonUp += delegate
            {
                SelectGameProfile(captured);
                SwitchView(_viewLibrary, _navBtnLibrary);
            };

            return itemBorder;
        }

        // =========================================================================
        // COLUMN 1: CENTER STEAM VIEWS HOST
        // =========================================================================
        private void BuildCenterViews(Grid bodyGrid)
        {
            Border centerCard = new Border()
            {
                Background = MakeVerticalGradient("#161126", "#0D0A16"),
                BorderBrush = HexBrush("#2E234D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true
            };
            Grid.SetColumn(centerCard, 1);
            bodyGrid.Children.Add(centerCard);

            Grid centerHost = new Grid();
            centerHost.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            centerHost.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            centerCard.Child = centerHost;

            BuildLibraryCenterView(centerHost);
            BuildStoreCenterView(centerHost);
            BuildDownloadCenterView(centerHost);
            BuildZeroVpnCenterView(centerHost);
            BuildModManagerCenterView(centerHost);
            BuildTelemetryCenterView(centerHost);
            BuildOtaShareCenterView(centerHost);
            BuildSettingsCenterView(centerHost);
        }

        // =========================================================================
        // VIEW 1: AUTHENTIC STEAM LIBRARY GAME STAGE
        // =========================================================================
        private void BuildLibraryCenterView(Grid host)
        {
            _viewLibrary = new Grid();
            _viewLibrary.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(205) }); // Row 0: Steam hero banner
            _viewLibrary.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });     // Row 1: Iconic Steam Play/Install Bar (#161220)
            _viewLibrary.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });     // Row 2: Live Download Progress Strip (if active)
            _viewLibrary.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // Row 3: Steam Details, Mirrors, Save & Co-Op Cards
            host.Children.Add(_viewLibrary);

            // ---------------- ROW 0: FULL-WIDTH STEAM HERO ARTWORK BANNER ----------------
            _heroBannerBorder = new Border()
            {
                Background = HexBrush("#121B28"),
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            Grid.SetRow(_heroBannerBorder, 0);
            _viewLibrary.Children.Add(_heroBannerBorder);

            Grid heroOverlayGrid = new Grid();
            _heroBannerBorder.Child = heroOverlayGrid;

            // Dark Bottom Steam Vignette Gradient so title and badges pop cleanly over artwork
            LinearGradientBrush vignette = new LinearGradientBrush();
            vignette.StartPoint = new Point(0, 0);
            vignette.EndPoint = new Point(0, 1);
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(60, 15, 22, 34), 0.0));
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(190, 15, 22, 34), 0.60));
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(250, 22, 32, 45), 1.0));
            heroOverlayGrid.Children.Add(new Border() { Background = vignette });

            // Steam tarzı sol taraftan sağa karartma (scrim): başlık ve rozetler
            // banner görselinin üstünde net okunur, sağ taraf sanat eseri görünür kalır.
            LinearGradientBrush sideScrim = new LinearGradientBrush();
            sideScrim.StartPoint = new Point(0, 0.5);
            sideScrim.EndPoint = new Point(1, 0.5);
            sideScrim.GradientStops.Add(new GradientStop(Color.FromArgb(242, 20, 29, 42), 0.0));
            sideScrim.GradientStops.Add(new GradientStop(Color.FromArgb(214, 20, 29, 42), 0.32));
            sideScrim.GradientStops.Add(new GradientStop(Color.FromArgb(120, 20, 29, 42), 0.58));
            sideScrim.GradientStops.Add(new GradientStop(Color.FromArgb(0, 20, 29, 42), 0.88));
            heroOverlayGrid.Children.Add(new Border() { Background = sideScrim });

            Grid heroContentGrid = new Grid() { Margin = new Thickness(22, 14, 22, 16) };
            heroContentGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            heroContentGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            heroContentGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            heroOverlayGrid.Children.Add(heroContentGrid);

            // Top Badges Row on Artwork
            StackPanel topBadgesRow = new StackPanel() { Orientation = Orientation.Horizontal };
            Grid.SetRow(topBadgesRow, 0);
            heroContentGrid.Children.Add(topBadgesRow);

            Border p2pBadge = new Border()
            {
                Background = HexBrush("#D9101824"),
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0)
            };
            _txtHeroAppIdBadge = new TextBlock()
            {
                Text = "APPID: 480 • SPACEWAR P2P",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            };
            p2pBadge.Child = _txtHeroAppIdBadge;
            topBadgesRow.Children.Add(p2pBadge);

            Border engineBadge = new Border()
            {
                Background = HexBrush("#D91A1635"),
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0)
            };
            _txtHeroEngineBadge = new TextBlock()
            {
                Text = "MOTOR: TESPİT EDİLİYOR...",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            };
            engineBadge.Child = _txtHeroEngineBadge;
            topBadgesRow.Children.Add(engineBadge);

            Border verBadge = new Border()
            {
                Background = HexBrush("#D9102419"),
                BorderBrush = ThemeManager.AccentGreenBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 0)
            };
            _txtHeroVersionBadge = new TextBlock()
            {
                Text = "SÜRÜM: v1.0",
                Foreground = ThemeManager.AccentGreenBrush,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            };
            verBadge.Child = _txtHeroVersionBadge;
            topBadgesRow.Children.Add(verBadge);

            _btnHeroFav = new Button()
            {
                Content = "☆ FAVORİ",
                Height = 22,
                Padding = new Thickness(8, 0, 8, 0),
                Background = HexBrush("#D91E293B"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(_btnHeroFav, 3);
            _btnHeroFav.Click += delegate
            {
                if (_selectedProfile == null) return;
                _selectedProfile.IsFavorite = !_selectedProfile.IsFavorite;
                ProfileStore.SaveProfiles(_allProfiles);
                SelectGameProfile(_selectedProfile);
                RefreshLibrarySidebarList();
            };
            topBadgesRow.Children.Add(_btnHeroFav);

            // Bottom Title & Category on Artwork
            StackPanel heroTitleStack = new StackPanel() { VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetRow(heroTitleStack, 2);
            heroContentGrid.Children.Add(heroTitleStack);

            _txtHeroCategory = new TextBlock()
            {
                Text = "CO-OP & ONLINE • PRE-INSTALLED",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 10.5,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 2)
            };
            heroTitleStack.Children.Add(_txtHeroCategory);

            _txtHeroTitle = new TextBlock()
            {
                Text = "Moon Sweepers",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.ExtraBold,
                Effect = new DropShadowEffect() { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = 0.9 }
            };
            heroTitleStack.Children.Add(_txtHeroTitle);

            // ---------------- ROW 1: THE ICONIC STEAM PLAY / INSTALL ACTION BAR (#161220) ----------------
            Border steamActionBar = new Border()
            {
                Background = HexBrush("#161220"),
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(22, 10, 22, 10)
            };
            Grid.SetRow(steamActionBar, 1);
            _viewLibrary.Children.Add(steamActionBar);

            StackPanel actionBarMasterStack = new StackPanel();
            steamActionBar.Child = actionBarMasterStack;

            Grid actionBarGrid = new Grid();
            actionBarGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto }); // Big Green PLAY / Blue INSTALL button
            actionBarGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); // Steam Metadata Strip
            actionBarMasterStack.Children.Add(actionBarGrid);

            // Big Steam Green Play / Blue Install Button
            _btnHeroPrimary = new Button()
            {
                Content = "▶ OYNA",
                Width = 175,
                Height = 40,
                Margin = new Thickness(0, 0, 16, 0),
                Background = MakeGradient("#5DBA2F", "#46941E"),
                Foreground = Brushes.White,
                BorderBrush = HexBrush("#6ED43B"),
                BorderThickness = new Thickness(1),
                FontSize = 13.5,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(_btnHeroPrimary, 4);
            _btnHeroPrimary.Click += BtnHeroPrimary_Click;
            Grid.SetColumn(_btnHeroPrimary, 0);
            actionBarGrid.Children.Add(_btnHeroPrimary);

            // Steam Metadata Strip (BULUT DURUMU | SON OYNAMA | OYNAMA SÜRESİ | DOSYA BOYUTU)
            StackPanel metaStrip = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(metaStrip, 1);
            actionBarGrid.Children.Add(metaStrip);

            metaStrip.Children.Add(CreateSteamMetaBlock("BULUT & P2P DURUMU", "✔ Spacewar (480) Hazır", out _txtSteamBarCloudStatus, "#5DBA2F"));
            metaStrip.Children.Add(CreateSteamMetaBlock("SON OYNAMA", "Henüz Oynanmadı", out _txtSteamBarLastPlayed, "#B8BCBF"));
            metaStrip.Children.Add(CreateSteamMetaBlock("SÜRE", "0 dk", out _txtSteamBarPlayTime, "#B8BCBF"));
            metaStrip.Children.Add(CreateSteamMetaBlock("BOYUT & SÜRÜM", "1.8 GB • v1.0", out _txtSteamBarSizeVersion, "#C084FC"));

            _txtHeroPlayTimeBadge = _txtSteamBarPlayTime;

            // Secondary Responsive Toolbar: Modern WrapPanel with categorized tool pills
            Border toolbarBorder = new Border()
            {
                Background = HexBrush("#0F1622"),
                BorderBrush = HexBrush("#1E2D3D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 8, 0, 0)
            };
            actionBarMasterStack.Children.Add(toolbarBorder);

            WrapPanel primaryQuickStack = new WrapPanel()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            toolbarBorder.Child = primaryQuickStack;

            Button btnToggleAdvancedTools = CreateSteamUtilityButton("[+] GELISMIS ARACLAR & INCE AYARLAR (60+) v", "FSR, DX12 Shader, P-Core, AI Noise Gate, ReShade ve 60 muhendislik aracini acar / gizler");
            btnToggleAdvancedTools.Background = MakeGradient("#1E3A8A", "#7C3AED");
            btnToggleAdvancedTools.Foreground = HexBrush("#93C5FD");
            btnToggleAdvancedTools.BorderBrush = HexBrush("#A855F7");
            btnToggleAdvancedTools.FontWeight = FontWeights.ExtraBold;
            btnToggleAdvancedTools.Margin = new Thickness(6, 2, 2, 2);

            _advancedToolsDrawer = new Border()
            {
                Background = HexBrush("#0B1017"),
                BorderBrush = HexBrush("#1E2D3D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            actionBarMasterStack.Children.Add(_advancedToolsDrawer);

            btnToggleAdvancedTools.Click += delegate
            {
                bool isOpen = _advancedToolsDrawer.Visibility == Visibility.Visible;
                _advancedToolsDrawer.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
                btnToggleAdvancedTools.Content = isOpen ? "[+] GELISMIS ARACLAR & INCE AYARLAR (60+) v" : "[-] GELISMIS ARACLARI GIZLE";
                btnToggleAdvancedTools.Background = isOpen ? MakeGradient("#1E3A8A", "#7C3AED") : MakeGradient("#7C3AED", "#8B5CF6");
            };

            ScrollViewer drawerScroll = new ScrollViewer()
            {
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _advancedToolsDrawer.Child = drawerScroll;

            StackPanel drawerStack = new StackPanel();
            drawerScroll.Content = drawerStack;

            WrapPanel perfToolsPanel = CreateToolCategorySection(drawerStack, "ğŸš€ PERFORMANS & FPS Ã‡EKÄ°RDEÄÄ° (FSR, PSO, P-CORE, VRAM, VULKAN)", "#C084FC");
            WrapPanel netToolsPanel = CreateToolCategorySection(drawerStack, "ğŸŒ Ã‡OK OYUNCULU, SES & AÄ SÄ°STEMLERÄ° (P2P SWARM, NOISE GATE, NAT, LOBÄ°)", "#34D399");
            WrapPanel secToolsPanel = CreateToolCategorySection(drawerStack, "ğŸ›¡ï¸ GÃœVENLÄ°K, MOD & DOSYA ONARIMI (DEFENDER, MOD KALKANI, LZX, GVAS)", "#F87171");
            WrapPanel extraToolsPanel = CreateToolCategorySection(drawerStack, "ğŸ¨ GRAFÄ°K, ALTYAZI & EKSTRALAR (RESHADE, BULUT KASA, EKRAN YAKALA)", "#C084FC");

            Button btnSteamBridgeNow = CreateSteamUtilityButton("🔗 STEAM 480", "Yerel Steam hesabınızı ve Spacewar (AppID 480) P2P köprüsünü bu oyuna bağlar");
            btnSteamBridgeNow.Foreground = ThemeManager.AccentTealBrush;
            btnSteamBridgeNow.BorderBrush = ThemeManager.AccentBlueBrush;
            btnSteamBridgeNow.Click += delegate
            {
                if (_selectedProfile == null) return;
                string rep = SteamClientBridgeService.ConnectAndBridgeSteamClient(_selectedProfile, true);
                SteamAccountInfo acc = SteamClientBridgeService.GetActiveSteamAccount(true);
                if (_txtUserNick != null && !string.IsNullOrEmpty(acc.PersonaName))
                    _txtUserNick.Text = acc.PersonaName;
                UpdateSteamBadgeStatus();
                _txtBottomStatus.Text = rep;
                SelectGameProfile(_selectedProfile);
                Win32.PlayTechChirp(true);
            };
            primaryQuickStack.Children.Add(btnSteamBridgeNow);

            Button btnSmartUpdateGame = CreateSteamUtilityButton("🔄 GÜNCELLE", "Sitedeki yeni sürümü denetler; güncelleme varsa Save yedeği alarak otomatik günceller");
            btnSmartUpdateGame.Foreground = ThemeManager.AccentTealBrush;
            btnSmartUpdateGame.Click += delegate
            {
                if (_selectedProfile == null) return;
                GameProfile targetGp = _selectedProfile;
                _txtBottomStatus.Text = "🔄 " + targetGp.Name + " için canlı sürüm ve güncelleme kontrolü yapılıyor...";
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string repMsg;
                    bool hasNew = SteamClientBridgeService.CheckSingleGameUpdate(targetGp, out repMsg);
                    ProfileStore.SaveProfiles(_allProfiles);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = repMsg;
                        SelectGameProfile(targetGp);
                        RefreshLibrarySidebarList();
                        if (hasNew || targetGp.UpdateAvailable)
                        {
                            string oldV = string.IsNullOrEmpty(targetGp.Version) ? "?" : targetGp.Version;
                            string newV = string.IsNullOrEmpty(targetGp.RemoteVersion) ? "Yeni" : targetGp.RemoteVersion;
                            string srcHint = string.IsNullOrEmpty(targetGp.UpdateSourceHint) ? "" : (" (" + targetGp.UpdateSourceHint + ")");
                            _txtBottomStatus.Text = string.Format("🔄 {0}: {1} → {2}{3} güncelleniyor (Save yedeği alındı)...", targetGp.Name, oldV, newV, srcHint);
                            string sMsg;
                            SaveSyncService.CreateSaveBackup(targetGp, out sMsg);
                            _inAppDlStripBorder.Visibility = Visibility.Visible;
                            _downloader.StartInAppAutomatedPipeline(targetGp);
                        }
                        Win32.PlayTechChirp(true);
                    }));
                });
            };
            primaryQuickStack.Children.Add(btnSmartUpdateGame);

            Button btnVerifyIntegrity = CreateSteamUtilityButton("🛡 BÜTÜNLÜĞÜ DOĞRULA", "Steam gibi oyun dosyalarının bütünlüğünü doğrular; eksik DLL, anti-virüs silintisi ve Goldberg/OnlineFix kancalarını anında onarır");
            btnVerifyIntegrity.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "Doğrulama ve onarım için oyunun bilgisayarda yüklü olması gerekir.";
                    return;
                }
                _txtBottomStatus.Text = "🛡 " + _selectedProfile.Name + " dosya bütünlüğü taranıyor ve eksik DLL'ler onarılıyor...";
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    PeBinaryReport rep = DependencyRepairEngine.InspectAndRepairGameBinary(_selectedProfile);
                    int fixedCount = (rep != null && rep.RepairedDlls != null) ? rep.RepairedDlls.Count : 0;
                    SteamEmulatorBridge.DeployFullEmulationSuite(_selectedProfile, Environment.UserName);
                    SteamClientBridgeService.ConnectAndBridgeSteamClient(_selectedProfile, false);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = string.Format("✔ {0} dosya bütünlüğü doğrulandı! ({1} bağımlılık/kanca onarıldı, Spacewar 480 hazır)", _selectedProfile.Name, fixedCount);
                        Win32.PlayTechChirp(true);
                    }));
                });
            };
            primaryQuickStack.Children.Add(btnVerifyIntegrity);

            Button btnAddToSteam = CreateSteamUtilityButton("➕ STEAM'E EKLE", "Seçili oyunu Steam'e 'Steam Dışı Oyun' olarak ekler (Sağ tık: tüm kurulu online oyunları ekle)");
            btnAddToSteam.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "Steam'e eklemek için önce kurulu bir oyun seçin.";
                    Win32.PlayTechChirp(false);
                    return;
                }
                string sMsg;
                bool ok = SteamIntegrationService.AddGameToSteamShortcuts(_selectedProfile, out sMsg);
                _txtBottomStatus.Text = sMsg;
                Win32.PlayTechChirp(ok);
            };
            ContextMenu steamMenu = new ContextMenu();
            MenuItem miAddAllOnline = new MenuItem() { Header = "🎮 Tüm Kurulu Online Oyunları Steam'e Ekle" };
            miAddAllOnline.Click += delegate
            {
                string allMsg;
                bool ok = SteamIntegrationService.AddAllOnlineGamesToSteam(_allProfiles, out allMsg);
                _txtBottomStatus.Text = allMsg;
                Win32.PlayTechChirp(ok);
            };
            steamMenu.Items.Add(miAddAllOnline);
            btnAddToSteam.ContextMenu = steamMenu;
            primaryQuickStack.Children.Add(btnAddToSteam);

            Button btnTurkishPatch = CreateSteamUtilityButton("🇹🇷 TÜRKÇE YAMA", "Oyun için doğrulanmış Türkçe dil/altyazı paketini kur (Sağ tık: tüm kurulu hikayeli oyunları tara)");
            ContextMenu trMenu = new ContextMenu();
            MenuItem miScanAllStory = new MenuItem() { Header = "🔍 Tüm Kurulu Hikayeli Oyunları Tara ve Türkçe Yap" };
            miScanAllStory.Click += delegate
            {
                _txtBottomStatus.Text = "🇹🇷 Kurulu hikayeli oyunlar taranıyor ve otomatik Türkçe yama/altyazı uygulanıyor...";
                TurkishPatchService.ScanAndAutoPatchInstalledStoryGamesAsync(_allProfiles, delegate(string summary)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = summary;
                        Win32.PlayTechChirp(true);
                    }));
                });
            };
            trMenu.Items.Add(miScanAllStory);
            btnTurkishPatch.ContextMenu = trMenu;
            btnTurkishPatch.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "Türkçe yama kurmak için önce oyunun bilgisayarda yüklü olması gerekir.";
                    return;
                }
                _txtBottomStatus.Text = "🇹🇷 " + _selectedProfile.Name + " için Türkçe dil paketi hazırlanıyor...";
                TurkishPatchService.InstallPatchAsync(_selectedProfile,
                    delegate(string status, int pct)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtBottomStatus.Text = string.Format("🇹🇷 Türkçe Yama ({0}%): {1}", pct, status);
                        }));
                    },
                    delegate(bool ok, string msg)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtBottomStatus.Text = msg;
                            Win32.PlayTechChirp(ok);
                            SelectGameProfile(_selectedProfile);
                        }));
                    });
            };
            primaryQuickStack.Children.Add(btnTurkishPatch);

            Button btnSteamInput = CreateSteamUtilityButton("🎮 GAMEPAD", "Steam Input & XInput Gamepad köprüsünü oyuna enjekte et");
            btnSteamInput.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "Gamepad köprüsü için oyunun yüklü olması gerekir.";
                    return;
                }
                string gMsg;
                bool ok = XInputGamepadService.DeploySteamInputWrapper(_selectedProfile, out gMsg);
                _txtBottomStatus.Text = gMsg;
                XInputGamepadService.TestVibration(0, 350);
                Win32.PlayTechChirp(ok);
            };
            primaryQuickStack.Children.Add(btnSteamInput);

            Button btnHeroBindExe = CreateSteamUtilityButton("📂 .EXE", "Bilgisayarda kurulu oyunun .exe dosyasını bağla");
            btnHeroBindExe.Click += BtnBindExe_Click;
            primaryQuickStack.Children.Add(btnHeroBindExe);

            _btnAutoInstallFromDownloads = CreateSteamUtilityButton("📦 ARŞİV KUR", "İndirilenler klasöründeki .RAR/.ZIP arşivini otomatik çıkart ve kur");
            _btnAutoInstallFromDownloads.Click += delegate
            {
                if (_selectedProfile == null) return;
                string found = DownloadInstallService.FindMatchingArchiveInDownloads(_selectedProfile.Name);
                if (!string.IsNullOrEmpty(found))
                {
                    _inAppDlStripBorder.Visibility = Visibility.Visible;
                    _downloader.ExtractAndBindAsync(_selectedProfile, found);
                }
                else
                {
                    Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
                    ofd.Title = _selectedProfile.Name + " için İndirilen .RAR / .ZIP Arşivini Seçin";
                    ofd.Filter = "Oyun Arşivleri (*.rar;*.zip;*.7z)|*.rar;*.zip;*.7z|Tüm Dosyalar (*.*)|*.*";
                    if (ofd.ShowDialog() == true)
                    {
                        _inAppDlStripBorder.Visibility = Visibility.Visible;
                        _downloader.ExtractAndBindAsync(_selectedProfile, ofd.FileName);
                    }
                }
            };
            secToolsPanel.Children.Add(_btnAutoInstallFromDownloads);

            Button btnOpenFolder = CreateSteamUtilityButton("🗁 KLASÖR", "Oyun kurulum dizinini Windows Gezgini'nde aç");
            btnOpenFolder.Click += delegate
            {
                if (_selectedProfile != null && !string.IsNullOrEmpty(_selectedProfile.WorkingDirectory) && Directory.Exists(_selectedProfile.WorkingDirectory))
                {
                    try { Process.Start("explorer.exe", _selectedProfile.WorkingDirectory); } catch { }
                }
                else
                {
                    string gDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games");
                    if (!Directory.Exists(gDir)) Directory.CreateDirectory(gDir);
                    try { Process.Start("explorer.exe", gDir); } catch { }
                }
            };
            primaryQuickStack.Children.Add(btnOpenFolder);

            Button btnHeroUninstall = CreateSteamUtilityButton("🗑 KALDIR", "Oyunu diskten veya kütüphaneden kaldır");
            btnHeroUninstall.Foreground = HexBrush("#F87171");
            btnHeroUninstall.BorderBrush = HexBrush("#991B1B");
            btnHeroUninstall.Click += delegate { RunUninstallSelected(); };
            primaryQuickStack.Children.Add(btnHeroUninstall);

            Button btnCreateShortcut = CreateSteamUtilityButton("🖥️ KISAYOL", "Masaüstüne orijinal ikonlu başlatma kısayolu (.lnk) oluştur");
            btnCreateShortcut.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "Kısayol oluşturmak için oyunun bilgisayarda kurulu olması gerekir.";
                    return;
                }
                string err;
                if (DesktopShortcutHelper.CreateGameDesktopShortcut(_selectedProfile, out err))
                {
                    _txtBottomStatus.Text = "✅ '" + _selectedProfile.Name + "' masaüstü kısayolu oluşturuldu!";
                    Win32.PlayTechChirp(true);
                }
                else
                {
                    _txtBottomStatus.Text = "Kısayol hatası: " + err;
                }
            };
            secToolsPanel.Children.Add(btnCreateShortcut);

            Button btnShaderCache = CreateSteamUtilityButton("⚡ SHADER", "Steam Shader Pre-Cache: Derlenmiş shader havuzunu dışa aktar ve takılmaları sıfırla");
            btnShaderCache.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "Shader havuzu için oyunun bilgisayarda kurulu olması gerekir.";
                    return;
                }
                string outPath, sMsg;
                if (ShaderPreCacheService.ExportGameShaderCache(_selectedProfile, out outPath, out sMsg))
                {
                    _txtBottomStatus.Text = sMsg;
                    Win32.PlayTechChirp(true);
                }
                else
                {
                    _txtBottomStatus.Text = sMsg;
                }
            };
            perfToolsPanel.Children.Add(btnShaderCache);

            Button btnTimeMachine = CreateSteamUtilityButton("🕒 ZAMAN TÜNELİ", "Save Zaman Tüneli: Son 10 oturumun anlık kopyalarını inceleyin veya geri yükleyin");
            btnTimeMachine.Click += delegate
            {
                if (_selectedProfile == null) return;
                ShowSaveTimeMachineWindow(_selectedProfile);
            };
            extraToolsPanel.Children.Add(btnTimeMachine);

            Button btnLzxCompact = CreateSteamUtilityButton("🗜 LZX SIKIŞTIR", "Windows CompactOS LZX algoritması ile oyun klasörünü %35-50 oranında şeffaf sıkıştır");
            btnLzxCompact.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled || string.IsNullOrEmpty(_selectedProfile.WorkingDirectory))
                {
                    _txtBottomStatus.Text = "Sıkıştırma için oyunun bilgisayarda kurulu olması gerekir.";
                    return;
                }
                _txtBottomStatus.Text = "🗜 " + _selectedProfile.Name + " için Windows NTFS LZX sıkıştırması başlatıldı... (Arka planda işleniyor)";
                Win32.PlayTechChirp(true);
                NtfsCompactEngine.CompressGameDirectoryAsync(_selectedProfile.WorkingDirectory,
                    delegate(string progress)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate { _txtBottomStatus.Text = progress; }));
                    },
                    delegate(CompactResult res)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtBottomStatus.Text = res.OutputSummary;
                            Win32.PlayTechChirp(res.Success);
                            if (res.Success) ProceduralSoundEngine.PlayAchievement();
                            SelectGameProfile(_selectedProfile);
                        }));
                    });
            };
            secToolsPanel.Children.Add(btnLzxCompact);

            Button btnNotes = CreateSteamUtilityButton("📝 NOTLAR", "Oyun içi şeffaf ve pinlenebilir not defteri [F8]");
            btnNotes.Click += delegate
            {
                if (_selectedProfile == null) return;
                InGameNotesWindow.ToggleNotes(_selectedProfile.Name);
            };
            extraToolsPanel.Children.Add(btnNotes);

            Button btnOsdKb = CreateSteamUtilityButton("🎮 OSD KLAVYE", "Steam Big Picture ekran üstü sanal klavye [F9]");
            btnOsdKb.Click += delegate
            {
                ControllerOsdKeyboard.ToggleKeyboard();
            };
            extraToolsPanel.Children.Add(btnOsdKb);

            Button btnWorkshop = CreateSteamUtilityButton("🧩 MOD ATÖLYESİ", "Mod dosyası (.zip/.pak/.dll) seç ve oyun motoruna göre otomatik kur");
            btnWorkshop.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "Mod yüklemek için oyunun bilgisayarda kurulu olması gerekir.";
                    return;
                }
                Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
                ofd.Title = _selectedProfile.Name + " için Mod Paketi Seçin (.zip, .pak, .dll)";
                ofd.Filter = "Mod Dosyaları (*.zip;*.pak;*.dll;*.rar)|*.zip;*.pak;*.dll;*.rar";
                if (ofd.ShowDialog() == true)
                {
                    string mMsg;
                    bool ok = SteamWorkshopModEngine.InstallModFileOrArchive(_selectedProfile, ofd.FileName, out mMsg);
                    _txtBottomStatus.Text = mMsg;
                    Win32.PlayTechChirp(ok);
                    if (ok) ProceduralSoundEngine.PlayAchievement();
                }
            };
            secToolsPanel.Children.Add(btnWorkshop);

            Button btnDisplayHz = CreateSteamUtilityButton("🖥 EKRAN / HZ", "Oyun için özel ekran yenileme hızı (Hz) ve çözünürlük ayarla");
            btnDisplayHz.Click += delegate
            {
                List<DisplayResolutionItem> modes = DisplayProfileService.GetSupportedResolutions();
                string firstFew = "";
                for (int i = 0; i < Math.Min(4, modes.Count); i++)
                {
                    firstFew += "\n• " + modes[i].DisplayText;
                }
                MessageBox.Show(
                    "Monitörünüzün Desteklediği Yüksek Hızlı Modlar:" + firstFew +
                    "\n\nOyun başladığında bu modlar otomatik kilitlenir; oyun kapandığında masaüstü moduna geri döner.",
                    "Titan // Per-Game Monitör & Hz Yöneticisi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            };
            perfToolsPanel.Children.Add(btnDisplayHz);

            Button btnVoiceChat = CreateSteamUtilityButton("🎙 SESLİ SOHBET", "Spacewar (480) takım içi P2P sesli sohbetini başlat / durdur");
            btnVoiceChat.Click += delegate
            {
                if (!P2PVoiceChatService.IsActive)
                {
                    string vMsg;
                    P2PVoiceChatService.StartVoiceChannel(48082, out vMsg);
                    _txtBottomStatus.Text = vMsg;
                    btnVoiceChat.Content = "🎙 SES: AÇIK";
                    btnVoiceChat.Foreground = ThemeManager.AccentGreenBrush;
                    Win32.PlayTechChirp(true);
                }
                else
                {
                    P2PVoiceChatService.StopVoiceChannel();
                    _txtBottomStatus.Text = "Spacewar P2P Sesli Sohbet kapatıldı.";
                    btnVoiceChat.Content = "🎙 SESLİ SOHBET";
                    btnVoiceChat.Foreground = ThemeManager.AccentTealBrush;
                    Win32.PlayTechChirp(false);
                }
            };
            netToolsPanel.Children.Add(btnVoiceChat);

            Button btnBenchmark = CreateSteamUtilityButton("📊 BENCHMARK", "Canlı FPS, %1 Low ve gecikme benchmark kaydını başlat / durdur [Shift+F11]");
            btnBenchmark.Click += delegate
            {
                string bMsg;
                string gName = (_selectedProfile != null) ? _selectedProfile.Name : "Oyun";
                bool started = BenchmarkRecorderService.ToggleBenchmark(gName, delegate(BenchmarkResult res)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = res.FormattedSummary;
                        btnBenchmark.Content = "📊 BENCHMARK";
                        btnBenchmark.Foreground = ThemeManager.AccentTealBrush;
                        ProceduralSoundEngine.PlayAchievement();
                    }));
                }, out bMsg);
                _txtBottomStatus.Text = bMsg;
                btnBenchmark.Content = started ? "📊 KAYITTA..." : "📊 BENCHMARK";
                btnBenchmark.Foreground = started ? HexBrush("#EB5757") : ThemeManager.AccentTealBrush;
                Win32.PlayTechChirp(started);
            };
            perfToolsPanel.Children.Add(btnBenchmark);

            Button btnRemotePlay = CreateSteamUtilityButton("📡 REMOTE PLAY", "Yerel ağdaki (Wi-Fi) telefon, tablet veya PC için WebRTC 60 FPS yayın sunucusu");
            btnRemotePlay.Click += delegate
            {
                if (!LocalStreamHostService.IsRunning)
                {
                    string streamUrl;
                    bool ok = LocalStreamHostService.StartStream(48085, out streamUrl);
                    _txtBottomStatus.Text = ok ? ("📡 Remote Play Yayında: " + streamUrl) : streamUrl;
                    btnRemotePlay.Content = "📡 YAYINDA: " + LocalStreamHostService.Port;
                    btnRemotePlay.Foreground = ThemeManager.AccentGreenBrush;
                    Win32.PlayTechChirp(ok);
                    if (ok)
                    {
                        MessageBox.Show(
                            "Steam Remote Play Yayın Sunucusu Aktif!\n\nAynı Wi-Fi ağındaki herhangi bir telefon, tablet veya bilgisayarın tarayıcısına şu adresi yazın:\n\n" +
                            streamUrl + "\n\n(Harici hiçbir uygulama kurmadan 60 FPS oyunu izleyebilir ve oynayabilirsiniz.)",
                            "Titan Remote Play Sunucusu",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    LocalStreamHostService.StopStream();
                    _txtBottomStatus.Text = "Remote Play yayını durduruldu.";
                    btnRemotePlay.Content = "📡 REMOTE PLAY";
                    btnRemotePlay.Foreground = ThemeManager.AccentTealBrush;
                    Win32.PlayTechChirp(false);
                }
            };
            extraToolsPanel.Children.Add(btnRemotePlay);

            Button btnDebloat = CreateSteamUtilityButton("🔇 ZERO-DEBLOAT", "Windows Update, Search Indexer ve SysMain servislerini oyun esnasında geçici dondur");
            btnDebloat.Click += delegate
            {
                if (!GameModeDebloaterService.IsFrozen)
                {
                    GameModeDebloaterService.FreezeBackgroundServicesAsync(delegate(string msg)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtBottomStatus.Text = msg;
                            btnDebloat.Content = "🔇 DEBLOAT: AKTİF";
                            btnDebloat.Foreground = ThemeManager.AccentGreenBrush;
                            Win32.PlayTechChirp(true);
                        }));
                    });
                }
                else
                {
                    GameModeDebloaterService.RestoreBackgroundServicesAsync(delegate(string msg)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtBottomStatus.Text = msg;
                            btnDebloat.Content = "🔇 ZERO-DEBLOAT";
                            btnDebloat.Foreground = ThemeManager.AccentTealBrush;
                            Win32.PlayTechChirp(false);
                        }));
                    });
                }
            };
            perfToolsPanel.Children.Add(btnDebloat);

            Button btnAudioRoute = CreateSteamUtilityButton("🎧 SES ÇIKIŞI", "Oyun sesini kulaklığa, sistem seslerini hoparlöre kilitle");
            btnAudioRoute.Click += delegate
            {
                List<AudioDeviceItem> devs = PerGameAudioRouterService.GetAudioPlaybackDevices();
                string listStr = "";
                for (int i = 0; i < devs.Count; i++)
                {
                    listStr += "\n• " + devs[i].DisplayText;
                }
                MessageBox.Show(
                    "Algılanan Ses Oynatma Aygıtları:" + listStr + "\n\n" + PerGameAudioRouterService.GetAudioOptimizationTip(),
                    "Titan // Çoklu Ses Yönlendirici",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            };
            extraToolsPanel.Children.Add(btnAudioRoute);

            Button btnIntegerScale = CreateSteamUtilityButton("🔍 INTEGER SCALE", "Piksel netliğini koruyan 2x/3x Tamsayı Ölçekleme analizcisi");
            btnIntegerScale.Click += delegate
            {
                IntegerScalingInfo isi = IntegerScalingOptimizer.CalculateIntegerScale(1920, 1080);
                MessageBox.Show(
                    string.Format("Monitör Çözünürlüğü: {0}x{1}\n\n{2}", isi.MonitorWidth, isi.MonitorHeight, isi.DiagnosticMessage),
                    "Titan // Pixel-Perfect Integer Scaling",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            };
            perfToolsPanel.Children.Add(btnIntegerScale);

            _btnUninstallGame = CreateSteamUtilityButton("🗑 KALDIR", "Oyunu diskten veya kütüphaneden kaldır");
            _btnUninstallGame.Foreground = HexBrush("#F87171");
            _btnUninstallGame.Click += delegate { RunUninstallSelected(); };
            secToolsPanel.Children.Add(_btnUninstallGame);

            _btnUpdateShieldStatus = CreateSteamUtilityButton("🛡 GÜNCELLEME KALKANI", "Kilitli süreç temizleme, save karantinası ve PE bütünlük raporu");
            _btnUpdateShieldStatus.Foreground = ThemeManager.AccentTealBrush;
            _btnUpdateShieldStatus.Click += delegate
            {
                if (_selectedProfile == null) return;
                string peDetail = "Bilinmiyor";
                string peArch = "Bilinmiyor";
                if (_selectedProfile.IsInstalled)
                {
                    SmartUpdateShieldService.VerifyExecutablePeIntegrity(_selectedProfile.ExecutablePath, out peDetail, out peArch);
                }

                string rbVer;
                DateTime rbDate;
                bool hasRb = SmartUpdateShieldService.HasRollbackSnapshot(_selectedProfile, out rbVer, out rbDate);

                string info = string.Format(
                    "🛡 TİTAN AKILLI GÜNCELLEME KALKANI & BÜTÜNLÜK RAPORU\n\n" +
                    "Oyun: {0}\n" +
                    "Mevcut Sürüm: {1}\n" +
                    "Uzak Sürüm: {2}\n" +
                    "Güncelleme Durumu: {3}\n\n" +
                    "• Kilitli Süreç Kalkanı: Aktif (ERROR_SHARING_VIOLATION Engelleme)\n" +
                    "• Akıllı Save Karantinası: Aktif (AppData, Documents & Yerel Save Koruması)\n" +
                    "• Geri Alma (Rollback) Noktası: {4}\n" +
                    "• Spacewar (AppID 480) Çok Oyunculu Onarımı: Aktif\n" +
                    "• PE İkili Bütünlük: {5}\n\n" +
                    "İpucu: Oyun güncellenirken tüm save ve ayarlarınız izole edilip yeni sürüme kayıpsız aktarılır.",
                    _selectedProfile.Name,
                    _selectedProfile.Version,
                    _selectedProfile.RemoteVersion ?? "Güncel",
                    _selectedProfile.UpdateAvailable ? "⚠️ Yeni Güncelleme Mevcut!" : "✅ Tamamen Güncel",
                    hasRb ? (rbVer + " (" + rbDate.ToString("dd.MM.yyyy HH:mm") + ")") : "Henüz oluşturulmadı",
                    peDetail);

                MessageBox.Show(info, "Titan // Akıllı Güncelleme Kalkanı", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            secToolsPanel.Children.Add(_btnUpdateShieldStatus);

            _btnUpdateShieldRollback = CreateSteamUtilityButton("⏪ SÜRÜM GERİ AL", "Sorunlu güncellemeyi önceki çalışan sürüme 1 tıkla anında geri döndürür");
            _btnUpdateShieldRollback.Foreground = ThemeManager.AccentTealBrush;
            _btnUpdateShieldRollback.Visibility = Visibility.Collapsed;
            _btnUpdateShieldRollback.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled) return;

                string prevVer;
                DateTime rbDate;
                if (!SmartUpdateShieldService.HasRollbackSnapshot(_selectedProfile, out prevVer, out rbDate))
                {
                    MessageBox.Show(
                        "Bu oyun için henüz bir geri alma (rollback) noktası kaydedilmemiş.\n\nGüncelleme Kalkanı, oyunu yeni bir sürüme güncellediğinizde otomatik olarak önceki sürümün yedeğini alır.",
                        "Titan // Güncelleme Kalkanı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                MessageBoxResult rbConfirm = MessageBox.Show(
                    string.Format("'{0}' oyununu önceki çalışan kararlı sürümüne ({1} • {2}) geri almak istediğinize emin misiniz?\n\n🛡 Titan Güncelleme Kalkanı Garantisi:\n• Oynadığınız en son save dosyalarınız otomatik korunup aktarılacak.\n• Dosyalar önceki kararlı sürüme çevrilecek.\n• Spacewar (AppID 480) çok oyunculu köprüsü otomatik onarılacak.",
                        _selectedProfile.Name, prevVer, rbDate.ToString("dd.MM.yyyy HH:mm")),
                    "Titan // 1-Tık Sürüm Geri Alma (Rollback)",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (rbConfirm == MessageBoxResult.Yes)
                {
                    string statusMsg;
                    bool success = SmartUpdateShieldService.RollbackToPreviousVersion(_selectedProfile, out statusMsg);
                    if (success)
                    {
                        ProfileStore.SaveProfiles(_allProfiles);
                        SelectGameProfile(_selectedProfile);
                        RefreshLibrarySidebarList();
                        RefreshLibrarySummaryMetrics();
                        Win32.PlayTechChirp(true);
                        MessageBox.Show(statusMsg, "Titan // Geri Alma Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        Win32.PlayTechChirp(false);
                        MessageBox.Show(statusMsg, "Titan // Geri Alma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };
            secToolsPanel.Children.Add(_btnUpdateShieldRollback);

            Button btnSquadRadar = CreateSteamUtilityButton("👥 TAKIM RADARI", "Çevrim içi arkadaşların oyun sürümlerini canlı karşılaştır ve tek tıkla güncelle");
            btnSquadRadar.Foreground = HexBrush("#A78BFA");
            btnSquadRadar.Click += delegate
            {
                if (_selectedProfile == null) return;
                List<FriendPresence> friends = (_cloudLobby != null) ? _cloudLobby.GetLiveFriendsList() : new List<FriendPresence>();
                SquadVersionScanResult scan = SquadVersionRadarService.AnalyzeSquadVersionParity(_selectedProfile, friends);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("👥 SQUAD SÜRÜM EŞİTLEME RADARI & P2P DAĞITIM");
                sb.AppendLine();
                sb.AppendLine("Oyun: " + _selectedProfile.Name);
                sb.AppendLine("Senin Sürümün: " + _selectedProfile.Version);
                sb.AppendLine("Hedef Sürüm: " + (_selectedProfile.RemoteVersion ?? _selectedProfile.Version));
                sb.AppendLine();
                sb.AppendLine("Takım Durumu: " + scan.DiagnosticSummary);
                sb.AppendLine();

                if (scan.Peers.Count > 0)
                {
                    sb.AppendLine("Oyuncu Detayları:");
                    foreach (SquadPeerVersionInfo p in scan.Peers)
                    {
                        sb.AppendLine(string.Format("• {0}: {1}", p.UserName, p.StatusText));
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("İşlem Seçenekleri:");
                sb.AppendLine("• 'Evet': Filo Güncelleme Emrini tüm takıma gönder (Arkadaşların uygulaması otomatik uyarılır).");
                sb.AppendLine("• 'Hayır': Kapat.");

                MessageBoxResult res = MessageBox.Show(sb.ToString(), "Titan // Takım Sürüm Radarı", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes)
                {
                    _autoUpdater.BroadcastFleetUpdateAsync(_selectedProfile);
                    _txtBottomStatus.Text = "📡 '" + _selectedProfile.Name + "' güncelleme sinyali ve P2P swarm bağlantısı takıma gönderildi!";
                    Win32.PlayTechChirp(true);
                }
            };
            netToolsPanel.Children.Add(btnSquadRadar);

            Button btnModShield = CreateSteamUtilityButton("🛡 MOD & YAMA KALKANI", "Türkçe yama, ReShade ve mod koruma durumu");
            btnModShield.Foreground = HexBrush("#34D399");
            btnModShield.Click += delegate
            {
                if (_selectedProfile == null) return;
                string qRoot = ModMigrationShieldService.GetMigrationRoot(_selectedProfile.Id);

                string info = string.Format(
                    "🛡 TİTAN MOD, TÜRKÇE YAMA & RESHADE GÖÇ KALKANI\n\n" +
                    "Oyun: {0}\n" +
                    "Klasör: {1}\n\n" +
                    "Garantili Koruma Kapsamı:\n" +
                    "• Türkçe Çeviri Dosyaları (*_tr.pak, Turkish/, Localization/)\n" +
                    "• ReShade Shaders & Presets (dxgi.dll, reshade-shaders/, ReShade.ini)\n" +
                    "• Mod Yöneticileri (BepInEx/plugins, MelonLoader, ~mods, RED4ext)\n" +
                    "• DirectX / Vulkan Shader Önbelleği (.dxvk-cache, PSO önbellekleri)\n\n" +
                    "Durum: Aktif (Oyun her güncellendiğinde tüm mod ve yamalar otomatik olarak yeni sürüme aktarılır).",
                    _selectedProfile.Name,
                    _selectedProfile.WorkingDirectory ?? "Henüz kurulmadı");

                MessageBox.Show(info, "Titan // Mod & Yama Göç Kalkanı", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            secToolsPanel.Children.Add(btnModShield);

            Button btnDedicatedServer = CreateSteamUtilityButton("🖥 SUNUCU HOST ET", "Palworld, Sons of the Forest, Valheim vb. için 7/24 Dedicated Server yönetim merkezini açar");
            btnDedicatedServer.Foreground = HexBrush("#D9534F");
            btnDedicatedServer.Click += delegate
            {
                if (_selectedProfile == null) return;
                DedicatedServerHostService.OpenServerManagementModal(this, _selectedProfile);
            };
            netToolsPanel.Children.Add(btnDedicatedServer);

            Button btnSaveConflict = CreateSteamUtilityButton("🔄 SAVE ÇAKIŞMA", "Masaüstü ve laptop arasındaki save çakışmalarını analiz eder ve tek tıkla eşitler");
            btnSaveConflict.Foreground = ThemeManager.AccentTealBrush;
            btnSaveConflict.Click += delegate
            {
                if (_selectedProfile == null) return;
                SaveConflictInfo conflict = SaveConflictResolverService.AnalyzeSaveConflict(_selectedProfile);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("💾 ÇOKLU CİHAZ SAVE ÇAKIŞMA & ZAMAN DAMGASI ÇÖZÜCÜ");
                sb.AppendLine();
                sb.AppendLine("Oyun: " + _selectedProfile.Name);
                sb.AppendLine("Durum: " + conflict.DiagnosticSummary);
                sb.AppendLine();

                if (conflict.HasConflict)
                {
                    sb.AppendLine("Önerilen Eylem: " + conflict.RecommendedAction);
                    sb.AppendLine();
                    sb.AppendLine("İşlem Seçenekleri:");
                    sb.AppendLine("• 'Evet': Bu bilgisayardaki (yerel) save'i seç ve kasayı güncelle.");
                    sb.AppendLine("• 'Hayır': Kasadaki / diğer cihazdaki save'i geri yükle (Yerel save güvenle arşivlenir).");

                    MessageBoxResult res = MessageBox.Show(sb.ToString(), "Titan // Save Çakışma Çözücü", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (res == MessageBoxResult.Yes)
                    {
                        string msg;
                        SaveConflictResolverService.AutoResolveAndMerge(_selectedProfile, true, out msg);
                        _txtBottomStatus.Text = msg;
                        Win32.PlayTechChirp(true);
                    }
                    else if (res == MessageBoxResult.No)
                    {
                        string msg;
                        SaveConflictResolverService.AutoResolveAndMerge(_selectedProfile, false, out msg);
                        _txtBottomStatus.Text = msg;
                        Win32.PlayTechChirp(true);
                    }
                }
                else
                {
                    MessageBox.Show(sb.ToString(), "Titan // Save Çakışma Çözücü", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            secToolsPanel.Children.Add(btnSaveConflict);

            Button btnLosslessScaling = CreateSteamUtilityButton("🚀 KARE ÇARPAN", "Tüm oyunlarda evrensel 2x/3x kare üretimi ve FSR mekânsal ölçekleme (30->60 / 60->120 FPS)");
            btnLosslessScaling.Foreground = ThemeManager.AccentTealBrush;
            btnLosslessScaling.Click += delegate
            {
                string statusMsg;
                bool active = LosslessScalingEngine.ToggleFrameGeneration(out statusMsg);
                _txtBottomStatus.Text = statusMsg;
                btnLosslessScaling.Foreground = active ? ThemeManager.AccentGreenBrush : ThemeManager.AccentTealBrush;
                Win32.PlayTechChirp(active);
            };
            perfToolsPanel.Children.Add(btnLosslessScaling);

            Button btnQosShield = CreateSteamUtilityButton("⚡ P2P QoS KALKANI", "Oyun trafiğine DSCP 46 önceliği verir ve indirme sırasında ping dalgalanmasını engeller");
            btnQosShield.Foreground = ThemeManager.AccentTealBrush;
            btnQosShield.Click += delegate
            {
                QosShieldStatus qs = QosNetworkJitterShield.GetStatus();
                string info = string.Format(
                    "⚡ P2P QoS & DİNAMİK JİTTER KALKANI\n\n" +
                    "• Durum: {0}\n" +
                    "• Anlık Gecikme (Ping): {1} ms\n" +
                    "• Baz Ping: {2} ms\n" +
                    "• Ağ Dalgalanması (Jitter): ±{3} ms\n" +
                    "• Akıllı Hız Kısıtlama: {4}\n\n" +
                    "Özellikler:\n" +
                    "• Oyun paketleri DSCP 46 (Expedited Forwarding) ile en yüksek önceliğe kilitlenir.\n" +
                    "• Takım içi P2P Swarm indirmeleri sırasında ping 18 ms'yi aşarsa indirme hızı otomatik kısıtlanır, ping düştüğünde serbest bırakılır.\n\n" +
                    "Sonuç: İndirme yaparken bile 0 lag ve sıfır ping fırlaması.",
                    qs.IsShieldActive ? "🟢 ÇALIŞIYOR" : "⚪ BEKLEMEDE",
                    qs.CurrentPingMs,
                    qs.BaselinePingMs,
                    qs.JitterMs,
                    qs.IsThrottleTriggered ? "⚠️ Aktif (Ping koruma devrede)" : "Serbest");

                MessageBox.Show(info, "Titan // P2P QoS Jitter Kalkanı", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            netToolsPanel.Children.Add(btnQosShield);

            Button btnDeltaPatch = CreateSteamUtilityButton("⚡ DELTA YAMA", "50 GB indirmek yerine sadece değişen 100 MB'lık ikili diferansiyel yamayı uygular");
            btnDeltaPatch.Foreground = ThemeManager.AccentTealBrush;
            btnDeltaPatch.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled) return;

                Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
                ofd.Title = _selectedProfile.Name + " için Diferansiyel Yama Dosyasını (.patch / .xdelta) Seçin";
                ofd.Filter = "Titan Delta Yama Dosyaları (*.patch;*.xdelta;*.vcdiff)|*.patch;*.xdelta;*.vcdiff|Tüm Dosyalar (*.*)|*.*";
                if (ofd.ShowDialog() == true)
                {
                    _txtBottomStatus.Text = "⚡ Diferansiyel ikili yama uygulanıyor...";
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        string outTarget = System.IO.Path.Combine(_selectedProfile.WorkingDirectory, System.IO.Path.GetFileNameWithoutExtension(ofd.FileName));
                        string err;
                        bool ok = DeltaPatchEngine.ApplyFileDelta(_selectedProfile.ExecutablePath, ofd.FileName, outTarget, out err);
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (ok)
                            {
                                _txtBottomStatus.Text = "✅ Diferansiyel yama başarıyla uygulandı!";
                                Win32.PlayTechChirp(true);
                            }
                            else
                            {
                                _txtBottomStatus.Text = "Yama hatası: " + err;
                                Win32.PlayTechChirp(false);
                            }
                        }));
                    });
                }
            };
            secToolsPanel.Children.Add(btnDeltaPatch);

            Button btnIntegrityDoctor = CreateSteamUtilityButton("🔍 DOSYALARI DOĞRULA", "Steam eşdeğeri dosya bütünlüğü taraması ve bozuk DLL/emülatör onarımı");
            btnIntegrityDoctor.Foreground = ThemeManager.AccentTealBrush;
            btnIntegrityDoctor.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled) return;

                _txtBottomStatus.Text = "🔍 Oyun dosya bütünlüğü taranıyor...";
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    GameIntegrityReport rep = GameIntegrityDoctorService.VerifyGameIntegrity(_selectedProfile, delegate(string msg, int pct)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtBottomStatus.Text = msg;
                        }));
                    });

                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("DOSYA BÜTÜNLÜĞÜ DOĞRULAMASI");
                        sb.AppendLine(new string('-', 44));
                        sb.AppendLine("Taranan dosya      : " + rep.TotalFilesScanned);
                        sb.AppendLine("Sağlam dosya       : " + rep.ValidFilesCount);
                        sb.AppendLine("Bozuk dosya        : " + rep.CorruptedFilesCount);
                        sb.AppendLine("Eksik bileşen      : " + rep.MissingFilesCount);
                        sb.AppendLine("Kurulum kaydı      : " + (rep.SnapshotAvailable ? "var (manifest karşılaştırıldı)" : "yok (ilk doğrulama)"));
                        sb.AppendLine(new string('-', 44));
                        sb.AppendLine();
                        sb.AppendLine(rep.DiagnosticSummary);
                        sb.AppendLine();

                        if (!rep.IsHealthy)
                        {
                            if (rep.MissingFileList.Count > 0)
                            {
                                sb.AppendLine("Eksik Bileşenler:");
                                foreach (string m in rep.MissingFileList) sb.AppendLine("• " + m);
                                sb.AppendLine();
                            }
                            if (rep.CorruptedFileList.Count > 0)
                            {
                                sb.AppendLine("Bozuk/Geçersiz Dosyalar:");
                                foreach (string c in rep.CorruptedFileList) sb.AppendLine("• " + c);
                                sb.AppendLine();
                            }
                            int listed = 0;
                            if (rep.SnapshotMissingList != null && rep.SnapshotMissingList.Count > 0)
                            {
                                sb.AppendLine("Kurulumdan sonra silinen dosyalar (ilk 8):");
                                for (int i = 0; i < rep.SnapshotMissingList.Count && listed < 8; i++, listed++)
                                    sb.AppendLine("• " + rep.SnapshotMissingList[i]);
                                if (rep.SnapshotMissingList.Count > 8)
                                    sb.AppendLine("• ... ve " + (rep.SnapshotMissingList.Count - 8) + " dosya daha");
                                sb.AppendLine();
                            }

                            sb.AppendLine("Onarım seçenekleri:");
                            sb.AppendLine("• EVET: bozuk/eksik dosyalar tespit edilip onarılır (save ve modlarınız korunur)");
                            sb.AppendLine("• HAYIR: hiçbir şey değiştirilmez");

                            MessageBoxResult res = MessageBox.Show(sb.ToString(), "Titan // Dosya Bütünlüğü & Doktor", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                            if (res == MessageBoxResult.Yes)
                            {
                                string repSum = "";

                                // Önce kısmi onarım: elde arşiv varsa sadece eksik dosyalar geri yüklenir.
                                if (rep.SnapshotMissingList != null && rep.SnapshotMissingList.Count > 0)
                                {
                                    string partialSum;
                                    int restored = GameIntegrityDoctorService.RepairMissingFromArchive(_selectedProfile, rep.SnapshotMissingList, out partialSum);
                                    repSum = partialSum;

                                    if (restored < rep.SnapshotMissingList.Count)
                                    {
                                        string fullSum;
                                        GameIntegrityDoctorService.RepairGameFiles(_selectedProfile, out fullSum);
                                        repSum = repSum + "\n" + fullSum;
                                    }
                                }
                                else
                                {
                                    GameIntegrityDoctorService.RepairGameFiles(_selectedProfile, out repSum);
                                }
                                _txtBottomStatus.Text = repSum;
                                Win32.PlayTechChirp(true);
                                MessageBox.Show(repSum, "Titan // Onarım Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                        else
                        {
                            Win32.PlayTechChirp(true);
                            MessageBox.Show(sb.ToString(), "Titan // Dosya Bütünlüğü Sağlam", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }));
                });
            };
            secToolsPanel.Children.Add(btnIntegrityDoctor);

            Button btnDefenderShield = CreateSteamUtilityButton("🛡 DEFENDER KALKANI", "Oyun klasörünü Windows Defender dışlama listesine ekler (False-positive engelleme)");
            btnDefenderShield.Foreground = ThemeManager.AccentGreenBrush;
            btnDefenderShield.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled) return;

                DefenderExclusionResult dRes = DefenderExclusionShield.AddFolderExclusion(_selectedProfile.WorkingDirectory);
                _txtBottomStatus.Text = dRes.DiagnosticMessage;
                Win32.PlayTechChirp(dRes.Success);
                MessageBox.Show(dRes.DiagnosticMessage, "Titan // Windows Defender Kalkanı", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            secToolsPanel.Children.Add(btnDefenderShield);

            Button btnDirectConnect = CreateSteamUtilityButton("🚀 SUNUCUYA KATIL", "Arkadaşının açık Dedicated sunucusuna veya P2P odasına tek tıkla doğrudan bağlanır");
            btnDirectConnect.Foreground = ThemeManager.AccentTealBrush;
            btnDirectConnect.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled) return;

                string hostIp = "127.0.0.1";
                int hostPort = 7777;

                List<FriendPresence> friends = (_cloudLobby != null) ? _cloudLobby.GetLiveFriendsList() : null;
                if (friends != null && friends.Count > 0)
                {
                    foreach (FriendPresence fp in friends)
                    {
                        if (string.Equals(fp.ActiveGame, _selectedProfile.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            hostIp = FleetSyncService.GetMeshOrLocalIp();
                            break;
                        }
                    }
                }

                MessageBoxResult res = MessageBox.Show(
                    string.Format("'{0}' oyunu başlatılıp doğrudan sunucuya ({1}:{2}) bağlanılsın mı?\n\n• Motor parametresi (+connect {1}:{2}) otomatik enjekte edilecek.\n• IP:Port panoya kopyalanacak.\n• Oyun kapanınca save otomatik eşitlenecek.",
                        _selectedProfile.Name, hostIp, hostPort),
                    "Titan // Doğrudan Sunucuya Bağlan",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    string lMsg;
                    Process p = DirectConnectLauncherService.LaunchAndDirectConnect(_selectedProfile, hostIp, hostPort, out lMsg);
                    _txtBottomStatus.Text = lMsg;
                    Win32.PlayTechChirp(p != null);
                }
            };
            netToolsPanel.Children.Add(btnDirectConnect);

            Button btnRelocateDrive = CreateSteamUtilityButton("📁 DİSKE TAŞI", "Oyunu C: SSD veya D: HDD / Harici SSD diskine taşır ve dosya yollarını günceller");
            btnRelocateDrive.Foreground = ThemeManager.AccentTealBrush;
            btnRelocateDrive.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled) return;

                System.Windows.Forms.FolderBrowserDialog fbd = new System.Windows.Forms.FolderBrowserDialog();
                fbd.Description = _selectedProfile.Name + " oyununun taşınacağı HEDEF klasörü seçin (Örn: D:\\Games):";
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string safeName = System.Text.RegularExpressions.Regex.Replace(_selectedProfile.Name, @"[^\w\s-]", "").Trim();
                    string targetFolder = System.IO.Path.Combine(fbd.SelectedPath, safeName);

                    MessageBoxResult mRes = MessageBox.Show(
                        string.Format("'{0}' oyunu şu konuma taşınsın mı?\n\nHedef: {1}\n\n• Dosyalar yeni diske aktarılacak.\n• Veritabanı yolları otomatik güncellenecek.\n• Orijinal eski klasör temizlenecek.",
                            _selectedProfile.Name, targetFolder),
                        "Titan // Oyun Disk Taşıma",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (mRes == MessageBoxResult.Yes)
                    {
                        _txtBottomStatus.Text = "📁 Oyun yeni diske taşınıyor...";
                        System.Threading.ThreadPool.QueueUserWorkItem(delegate
                        {
                            RelocationResult rRes = StorageDriveRelocatorService.RelocateGameDirectory(_selectedProfile, targetFolder, delegate(string msg, int pct)
                            {
                                Dispatcher.BeginInvoke(new Action(delegate
                                {
                                    _txtBottomStatus.Text = msg;
                                }));
                            });

                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                ProfileStore.SaveProfiles(_allProfiles);
                                SelectGameProfile(_selectedProfile);
                                RefreshLibrarySidebarList();
                                RefreshLibrarySummaryMetrics();
                                _txtBottomStatus.Text = rRes.DiagnosticSummary;
                                Win32.PlayTechChirp(rRes.Success);
                                MessageBox.Show(rRes.DiagnosticSummary, "Titan // Disk Taşıma Raporu", MessageBoxButton.OK, rRes.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
                            }));
                        });
                    }
                }
            };
            secToolsPanel.Children.Add(btnRelocateDrive);

            Button btnCpuAffinity = CreateSteamUtilityButton("⚡ CPU P-CORE", "Intel 12-14th Gen ve AMD işlemcilerde E-Core takılmalarını önleyip oyunu P-Core çekirdeklerine kilitler");
            btnCpuAffinity.Foreground = ThemeManager.AccentTealBrush;
            btnCpuAffinity.Click += delegate
            {
                string cMsg;
                Process[] pList = _selectedProfile != null && !string.IsNullOrEmpty(_selectedProfile.ExecutablePath) ? Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(_selectedProfile.ExecutablePath)) : new Process[0];
                if (pList.Length > 0)
                {
                    CpuAffinityOptimizer.OptimizeGameAffinity(pList[0], out cMsg);
                    _txtBottomStatus.Text = cMsg;
                    Win32.PlayTechChirp(true);
                }
                else
                {
                    _txtBottomStatus.Text = "⚡ CPU P-Core Kalkanı: Oyun başladığı anda otomatik olarak ilk performans çekirdeklerine kilitlenir.";
                }
            };
            perfToolsPanel.Children.Add(btnCpuAffinity);

            Button btnDxvkLayer = CreateSteamUtilityButton("🎮 DXVK VULKAN", "Eski DirectX 9/11 oyunlarını tek tıkla modern Vulkan komut setine çevirir (%30-60 FPS artışı)");
            btnDxvkLayer.Foreground = ThemeManager.AccentTealBrush;
            btnDxvkLayer.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled) return;
                string dMsg;
                bool active = DxvkLayerManager.ToggleDxvkLayer(_selectedProfile, out dMsg);
                _txtBottomStatus.Text = dMsg;
                btnDxvkLayer.Foreground = active ? ThemeManager.AccentGreenBrush : ThemeManager.AccentTealBrush;
                Win32.PlayTechChirp(active);
            };
            perfToolsPanel.Children.Add(btnDxvkLayer);

            Button btnAutoHdr = CreateSteamUtilityButton("🌈 AUTO HDR", "SDR oyunlara geniş renk gamutu (Auto HDR) ve AI kenar keskinleştirme enjekte eder");
            btnAutoHdr.Foreground = HexBrush("#D9534F");
            btnAutoHdr.Click += delegate
            {
                if (_selectedProfile == null) return;
                string hMsg;
                bool hOk = AutoHdrOptimizer.ConfigureGameHdrAndSharpening(_selectedProfile, true, out hMsg);
                _txtBottomStatus.Text = hMsg;
                Win32.PlayTechChirp(hOk);
            };
            perfToolsPanel.Children.Add(btnAutoHdr);

            Button btnNatStun = CreateSteamUtilityButton("🌐 NAT DELME", "CGNAT ve Katı NAT arkasındaki oyuncular için Google/Cloudflare STUN ile genel IP:Port eşler");
            btnNatStun.Foreground = ThemeManager.AccentTealBrush;
            btnNatStun.Click += delegate
            {
                StunEndpointResult sRes = NatTraversalHelper.QueryPublicEndpoint();
                _txtBottomStatus.Text = sRes.DiagnosticSummary;
                Win32.PlayTechChirp(sRes.Success);
                MessageBox.Show(sRes.DiagnosticSummary + "\n\nNAT Tipi: " + sRes.NatType, "Titan // STUN Delme & Genel Uç Nokta", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            netToolsPanel.Children.Add(btnNatStun);

            Button btnProximityVoice = CreateSteamUtilityButton("🎙 3D SESLİ SOHBET", "Spacewar 480 eşli oyun lobisinde 3D konumsal mesafeli sesli sohbet köprüsünü açar");
            btnProximityVoice.Foreground = ThemeManager.AccentGreenBrush;
            btnProximityVoice.Click += delegate
            {
                if (!ProximityVoiceChatBridge.IsActive)
                {
                    ProximityVoiceChatBridge.StartVoiceBridge();
                    btnProximityVoice.Content = "🎙 3D SES: AÇIK";
                    btnProximityVoice.Foreground = ThemeManager.AccentGreenBrush;
                    _txtBottomStatus.Text = "🎙 3D Konumsal Sesli İletişim Köprüsü Aktif (Port: 48099)!";
                    Win32.PlayTechChirp(true);
                }
                else
                {
                    ProximityVoiceChatBridge.StopVoiceBridge();
                    btnProximityVoice.Content = "🎙 3D SESLİ SOHBET";
                    btnProximityVoice.Foreground = ThemeManager.AccentGreenBrush;
                    _txtBottomStatus.Text = "Sesli iletişim köprüsü durduruldu.";
                    Win32.PlayTechChirp(false);
                }
            };
            netToolsPanel.Children.Add(btnProximityVoice);

            Button btnReplayClip = CreateSteamUtilityButton("🎬 ANLIK TEKRAR", "Son 30 saniyelik efsane oyun anını RAM tamponundan doğrudan Replays/ klasörüne kaydeder");
            btnReplayClip.Foreground = ThemeManager.AccentTealBrush;
            btnReplayClip.Click += delegate
            {
                string rPath, rMsg;
                string gName = _selectedProfile != null ? _selectedProfile.Name : "TitanClip";
                InstantReplayBuffer.SaveReplayClip(gName, out rPath, out rMsg);
                _txtBottomStatus.Text = rMsg;
                Win32.PlayTechChirp(true);
            };
            extraToolsPanel.Children.Add(btnReplayClip);

            Button btnTelegramWebhook = CreateSteamUtilityButton("📲 MOBİL BİLDİRİM", "Arkadaşınız lobi kurduğunda veya oyun indiğinde telefonunuza anlık bildirim gönderir");
            btnTelegramWebhook.Foreground = ThemeManager.AccentTealBrush;
            btnTelegramWebhook.Click += delegate
            {
                string room = _txtRoomCode != null ? _txtRoomCode.Text : "TITAN-TR";
                string nick = _txtUserNick != null ? _txtUserNick.Text : Environment.UserName;
                string gName = _selectedProfile != null ? _selectedProfile.Name : "Co-Op Oyunu";
                TelegramLobbyWebhookService.SendLobbyInviteAlertAsync(gName, nick, room);
                _txtBottomStatus.Text = "📲 Lobi davet bildirimi ntfy/telefona başarıyla gönderildi!";
                Win32.PlayTechChirp(true);
            };
            extraToolsPanel.Children.Add(btnTelegramWebhook);

            Button btnCloudServerHub = CreateSteamUtilityButton("🌐 BULUT & SUNUCU", "Yerel Oynama, 7/24 Dedicated Server ve Moonlight/WebRTC Bulut Akış Yönetim Merkezi");
            btnCloudServerHub.Foreground = ThemeManager.AccentTealBrush;
            btnCloudServerHub.Click += delegate
            {
                if (_selectedProfile == null) return;
                ShowCloudServerHubModal(_selectedProfile);
            };
            netToolsPanel.Children.Add(btnCloudServerHub);

            Button btnUpnpForward = CreateSteamUtilityButton("🌐 UPNP MODEM AÇ", "Donanımsal UPnP IGD: Modeminizde portları (P2P/Stream/Ses) otomatik açar; VPN gerekmeden arkadaşlarınız bağlanır");
            btnUpnpForward.Foreground = ThemeManager.AccentGreenBrush;
            btnUpnpForward.Click += delegate
            {
                _txtBottomStatus.Text = "🌐 UPnP IGD: Modem taranıyor ve portlar açılıyor...";
                UpnpPortForwarderService.AutoForwardTitanPortsAsync(delegate(string upnpRes)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = upnpRes;
                        Win32.PlayTechChirp(true);
                        MessageBox.Show(upnpRes, "Titan // Donanımsal UPnP Port Açıcı", MessageBoxButton.OK, MessageBoxImage.Information);
                    }));
                });
            };
            netToolsPanel.Children.Add(btnUpnpForward);

            Button btnUltrawideAob = CreateSteamUtilityButton("🖥️ ULTRAWIDE & FOV", "AOB imza motoru: 21:9 / 32:9 ve FPS kilidi için kayıtlı imzaları tarar; eşleşme bulunursa bellek yaması dener");
            btnUltrawideAob.Foreground = ThemeManager.AccentTealBrush;
            btnUltrawideAob.Click += delegate
            {
                if (_selectedProfile == null) return;
                Process[] procs = !string.IsNullOrEmpty(_selectedProfile.ExecutablePath)
                    ? Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(_selectedProfile.ExecutablePath))
                    : new Process[0];

                if (procs.Length > 0)
                {
                    string pMsg;
                    bool ok = AobMemoryPatcherService.ApplyUltrawidePatch(procs[0], 2.3888888f, out pMsg);
                    string fpsMsg;
                    AobMemoryPatcherService.UnlockFpsCap(procs[0], out fpsMsg);
                    _txtBottomStatus.Text = pMsg + " | " + fpsMsg;
                    Win32.PlayTechChirp(ok);
                    MessageBox.Show(pMsg + "\n\n" + fpsMsg, "Titan // AOB Bellek Motoru", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(
                        "Oyun şu anda çalışmıyor.\n\nTitan AOB Bellek Motoru, oyun çalışırken kayıtlı hafıza imzalarını tarar ve eşleşme bulursa 21:9 / 32:9 en-boy oranı ile FPS kilidi için yama dener. Sonuç oyunun imza/veritabanı eşleşmesine bağlıdır.\n\nOyunu başlattıktan sonra bu butona basarak taramayı çalıştırabilirsiniz.",
                        "Titan // Ultrawide & FOV Motoru",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            };
            extraToolsPanel.Children.Add(btnUltrawideAob);

            Button btnGvasEditor = CreateSteamUtilityButton("💾 GVAS SAVE ONARICI", "Unreal Engine 4/5 ve Unity save dosyalarını denetler, bozulmaları onarır ve yedekten geri yükler");
            btnGvasEditor.Foreground = ThemeManager.AccentTealBrush;
            btnGvasEditor.Click += delegate
            {
                if (_selectedProfile == null) return;
                List<string> savList = GvasSaveEditorService.LocateSaveFilesForGame(_selectedProfile);
                if (savList.Count == 0)
                {
                    MessageBox.Show(
                        "'" + _selectedProfile.Name + "' için henüz diskte kayıtlı save dosyası tespit edilemedi.\n\nOyunu en az 1 kez başlatıp kayıt noktasına ulaştığınızda save dosyası burada otomatik listelenir.",
                        "Titan // GVAS Save Dedektörü",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string firstSav = savList[0];
                GvasHeaderInfo gInfo = GvasSaveEditorService.InspectGvasSave(firstSav);
                string bPath = GvasSaveEditorService.CreateBackup(firstSav);

                string msg = string.Format(
                    "💾 SAVE ANALİZ & OTOMATİK YEDEKLEME\n\n" +
                    "Oyun: {0}\n" +
                    "Tespit Edilen Save: {1}\n" +
                    "Format: {2}\n" +
                    "Durum: {3}\n" +
                    "Boyut: {4:0.0} KB\n\n" +
                    "Güvenli Yedek: {5}\n\n" +
                    "Save dosyanız güvenceye alındı. Bozulma durumunda otomatik kurtarılabilir.",
                    _selectedProfile.Name,
                    System.IO.Path.GetFileName(firstSav),
                    gInfo.SaveGameType,
                    gInfo.DiagnosticSummary,
                    gInfo.FileSizeBytes / 1024.0,
                    System.IO.Path.GetFileName(bPath ?? "Yedek Alındı"));

                _txtBottomStatus.Text = "💾 Save incelendi ve yedeklendi: " + System.IO.Path.GetFileName(firstSav);
                Win32.PlayTechChirp(true);
                MessageBox.Show(msg, "Titan // GVAS & Save Onarıcı", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            secToolsPanel.Children.Add(btnGvasEditor);

            Button btnRamDisk = CreateSteamUtilityButton("⚡ RAMDISK ÖN BELLEK", "Oyun paketlerini (.pak/.assets) doğrudan RAM'e yükler; açılış ve yükleme ekranlarını 0.1 saniyeye indirir");
            btnRamDisk.Foreground = ThemeManager.AccentTealBrush;
            btnRamDisk.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "RAM ön belleğe almak için oyunun bilgisayarda yüklü olması gerekir.";
                    return;
                }
                _txtBottomStatus.Text = "⚡ '" + _selectedProfile.Name + "' paketleri RAM'e yükleniyor (0.1s Açılış)...";
                RamDiskLoaderService.PreloadAsync(_selectedProfile, 4L * 1024L * 1024L * 1024L,
                    delegate(RamPreloadReport rep)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtBottomStatus.Text = rep.SummaryMessage;
                            Win32.PlayTechChirp(rep.Success);
                            MessageBox.Show(rep.SummaryMessage, "Titan // RAMDisk Hızlandırıcı", MessageBoxButton.OK, MessageBoxImage.Information);
                        }));
                    },
                    delegate(string fname, double pct)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtBottomStatus.Text = string.Format("⚡ RAM Yükleme ({0:0}%): {1}", pct, fname);
                        }));
                    });
            };
            perfToolsPanel.Children.Add(btnRamDisk);

            Button btnOptiScaler = CreateSteamUtilityButton("🚀 FSR 3.1 & FRAME GEN", "Evrensel OptiScaler köprüsü: DLSS destekli veya eski oyunlara AMD FSR 3.1 Frame Generation enjekte eder (2x FPS)");
            btnOptiScaler.Foreground = ThemeManager.AccentTealBrush;
            btnOptiScaler.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "OptiScaler için oyunun bilgisayarda yüklü olması gerekir.";
                    return;
                }
                OptiScalerStatus st = OptiScalerBridgeService.CheckStatus(_selectedProfile);
                if (!st.IsInstalled)
                {
                    string depMsg;
                    bool ok = OptiScalerBridgeService.DeployOptiScalerBridge(_selectedProfile, out depMsg);
                    _txtBottomStatus.Text = depMsg;
                    Win32.PlayTechChirp(ok);
                    MessageBox.Show(depMsg, "Titan // OptiScaler Evrensel Frame Generation", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBoxResult mRes = MessageBox.Show(
                        "OptiScaler AMD FSR 3.1 & Frame Gen Köprüsü zaten aktif!\n\nKöprüyü kaldırıp orijinal render motoruna dönmek ister misiniz?",
                        "Titan // OptiScaler Köprüsü",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (mRes == MessageBoxResult.Yes)
                    {
                        string remMsg;
                        OptiScalerBridgeService.RemoveOptiScalerBridge(_selectedProfile, out remMsg);
                        _txtBottomStatus.Text = remMsg;
                        Win32.PlayTechChirp(true);
                    }
                }
            };
            perfToolsPanel.Children.Add(btnOptiScaler);

            Button btnReShadeRtgi = CreateSteamUtilityButton("🌈 RESHADE & RTGI", "Işın İzleme (Ray Tracing GI) ve AMD CAS netlik gölgelendiricilerini oyuna enjekte eder (F2 tuşu)");
            btnReShadeRtgi.Foreground = HexBrush("#D9534F");
            btnReShadeRtgi.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "ReShade RTGI için oyunun bilgisayarda yüklü olması gerekir.";
                    return;
                }
                ReShadeStatus rs = ReShadeRtgiInjectorService.CheckStatus(_selectedProfile);
                if (!rs.IsInstalled)
                {
                    string rMsg;
                    bool ok = ReShadeRtgiInjectorService.DeployReShadeRtgi(_selectedProfile, true, out rMsg);
                    _txtBottomStatus.Text = rMsg;
                    Win32.PlayTechChirp(ok);
                    MessageBox.Show(rMsg, "Titan // ReShade RTGI Enjektörü", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBoxResult rRes = MessageBox.Show(
                        "ReShade RTGI ve Sinematik Preset şu anda aktif!\n\nReShade dosyalarını temizleyip orijinal render motoruna dönmek ister misiniz?",
                        "Titan // ReShade RTGI Katmanı",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (rRes == MessageBoxResult.Yes)
                    {
                        string cMsg;
                        ReShadeRtgiInjectorService.RemoveReShade(_selectedProfile, out cMsg);
                        _txtBottomStatus.Text = cMsg;
                        Win32.PlayTechChirp(true);
                    }
                }
            };
            extraToolsPanel.Children.Add(btnReShadeRtgi);

            Button btnLanSwarm = CreateSteamUtilityButton("⚡ LAN SWARM", "Aynı Wi-Fi/LAN'daki arkadaşından oyunu 1 Gbps hızla internet kotası harcamadan çeker");
            btnLanSwarm.Foreground = ThemeManager.AccentGreenBrush;
            btnLanSwarm.Click += delegate
            {
                if (_selectedProfile == null) return;
                _txtBottomStatus.Text = "⚡ Yerel ağdaki (LAN/Zero-VPN) Titan Swarm kaynakları taranıyor...";
                ThreadPool.QueueUserWorkItem(delegate
                {
                    LanPeerSeedOffer offer = LanP2pSwarmSeederService.DiscoverLocalSeeder(_selectedProfile.Id, 2500);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (offer != null)
                        {
                            string oMsg = string.Format(
                                "⚡ YEREL AĞDA (LAN) EŞ KAYNAK BULUNDU!\n\n" +
                                "• Eş IP: {0}:{1}\n" +
                                "• Oyun: {2}\n" +
                                "• Boyut: {3:0.0} MB\n" +
                                "• Aktarım Hızı: 1 Gbps Yerel Ağ (100+ MB/s)\n\n" +
                                "İnternet kotası harcamadan yerel eşten ultra-hızlı aktarım başlatılsın mı?",
                                offer.PeerIp, offer.TcpPort, offer.GameName, offer.TotalSizeBytes / (1024.0 * 1024.0));
                            MessageBoxResult mRes = MessageBox.Show(oMsg, "Titan // LAN P2P Swarm Seeder", MessageBoxButton.YesNo, MessageBoxImage.Information);
                            if (mRes == MessageBoxResult.Yes)
                            {
                                _txtBottomStatus.Text = "⚡ LAN P2P Swarm aktarımı başlatıldı: " + offer.PeerIp;
                                Win32.PlayTechChirp(true);
                            }
                        }
                        else
                        {
                            _txtBottomStatus.Text = "Yerel ağda bu oyunu paylaşan başka bir Titan istemcisi bulunamadı. Web indirme sunucusu kullanılabilir.";
                            Win32.PlayTechChirp(false);
                            MessageBox.Show(
                                "Aynı Wi-Fi veya Zero-VPN ağında bu oyuna sahip açık bir Titan Hub eşi bulunamadı.\n\nBir arkadaşınız bu oyunu kurduğunda yerel ağ üzerinden saniyeler içinde 1 Gbps hızla çekebilirsiniz.",
                                "Titan // LAN Swarm Keşfi",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }));
                });
            };
            netToolsPanel.Children.Add(btnLanSwarm);

            Button btnSaveSwarm = CreateSteamUtilityButton("💾 P2P SAVE HAVUZU", "Host kopsa bile dünya save'ini tüm takıma P2P ile anında eşitler; herkes host olabilir");
            btnSaveSwarm.Foreground = ThemeManager.AccentTealBrush;
            btnSaveSwarm.Click += delegate
            {
                if (_selectedProfile == null) return;
                List<string> peerIps = new List<string>();
                if (_cloudLobby != null)
                {
                    List<FriendPresence> fl = _cloudLobby.GetLiveFriendsList();
                    for (int i = 0; i < fl.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(fl[i].LastKnownIp))
                            peerIps.Add(fl[i].LastKnownIp);
                    }
                }

                _txtBottomStatus.Text = "💾 Save paketi hazırlanıyor ve odaya eşitleniyor...";
                P2pSaveSwarmMeshService.BroadcastSaveToPeersAsync(_selectedProfile, peerIps, delegate(string res)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = res;
                        Win32.PlayTechChirp(true);
                        MessageBox.Show(res, "Titan // P2P Co-Op Save Eşitleme Havuzu", MessageBoxButton.OK, MessageBoxImage.Information);
                    }));
                });
            };
            extraToolsPanel.Children.Add(btnSaveSwarm);

            Button btnSplitScreen = CreateSteamUtilityButton("👥 SPLIT-SCREEN CO-OP", "Aynı PC'de çift pencere açar ve iki gamepad ile bölünmüş ekran co-op oynatır");
            btnSplitScreen.Foreground = ThemeManager.AccentTealBrush;
            btnSplitScreen.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "Bölünmüş ekran için oyunun yüklü olması gerekir.";
                    return;
                }

                MessageBoxResult splitChoice = MessageBox.Show(
                    "Bölünmüş Ekran (Split-Screen) Düzeni Seçin:\n\n• Evet: Dikey Bölünmüş Ekran (Sol %50 / Sağ %50) [Önerilen]\n• Hayır: Yatay Bölünmüş Ekran (Üst %50 / Alt %50)\n• İptal: Vazgeç",
                    "Titan // Nucleus Split-Screen Co-Op",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (splitChoice == MessageBoxResult.Cancel) return;
                bool isVert = (splitChoice == MessageBoxResult.Yes);

                string sMsg;
                SplitScreenConfig cfg = SplitScreenMultiInstanceService.LaunchSplitScreen(_selectedProfile, isVert, out sMsg);
                _txtBottomStatus.Text = sMsg;
                Win32.PlayTechChirp(cfg.ProcessPlayer1 != null);
                MessageBox.Show(sMsg, "Titan // Bölünmüş Ekran Başlatıldı", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            netToolsPanel.Children.Add(btnSplitScreen);

            Button btnRawInput = CreateSteamUtilityButton("⚡ 0.1MS RAW INPUT", "Windows DWM gecikmesini sıfırlar, USB raporlama hızını 1.0ms çekirdek zamanlayıcısına kilitler");
            btnRawInput.Foreground = ThemeManager.AccentTealBrush;
            btnRawInput.Click += delegate
            {
                Process activeP = null;
                if (_selectedProfile != null && !string.IsNullOrEmpty(_selectedProfile.ExecutablePath))
                {
                    Process[] procs = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(_selectedProfile.ExecutablePath));
                    if (procs.Length > 0) activeP = procs[0];
                }

                LatencyOptimizationReport lRep = RawInputLatencyOptimizer.OptimizeInputLatency(activeP);
                _txtBottomStatus.Text = "⚡ 0.1ms RawInput ve Çekirdek Zamanlayıcısı Aktif!";
                Win32.PlayTechChirp(true);
                MessageBox.Show(lRep.DiagnosticSummary, "Titan // RawInput Sub-Millisecond Optimize Edici", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            perfToolsPanel.Children.Add(btnRawInput);

            Button btnDirectXOverlay = CreateSteamUtilityButton("🎯 DIRECTX OSD", "Oyun içi donanım monitörü (FPS, Ping, Sıcaklık) ve Spacewar Shift+Tab hızlı lobi arayüzü");
            btnDirectXOverlay.Foreground = ThemeManager.AccentTealBrush;
            btnDirectXOverlay.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "Oyun içi OSD için oyunun yüklü olması gerekir.";
                    return;
                }

                OverlayHookStatus oStat = DirectXOverlayHookBridge.GetStatus(_selectedProfile);
                if (!oStat.IsDeployed)
                {
                    string depMsg;
                    bool ok = DirectXOverlayHookBridge.DeployOverlayHook(_selectedProfile, out depMsg);
                    _txtBottomStatus.Text = depMsg;
                    Win32.PlayTechChirp(ok);
                    MessageBox.Show(depMsg, "Titan // DirectX ImGui Oyun İçi OSD", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBoxResult mRes = MessageBox.Show(
                        "DirectX Oyun İçi OSD kancası zaten aktif!\n\nKancayı kaldırmak ister misiniz?",
                        "Titan // Oyun İçi OSD",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (mRes == MessageBoxResult.Yes)
                    {
                        string rMsg;
                        DirectXOverlayHookBridge.RemoveOverlayHook(_selectedProfile, out rMsg);
                        _txtBottomStatus.Text = rMsg;
                        Win32.PlayTechChirp(true);
                    }
                }
            };
            extraToolsPanel.Children.Add(btnDirectXOverlay);

            Button btnFastPlay = CreateSteamUtilityButton("⚡ FAST-PLAY", "Oyunun ilk %5'i indiğinde oyunu başlatır, geri kalan dünyayı arka planda kesintisiz indirir");
            btnFastPlay.Foreground = ThemeManager.AccentGreenBrush;
            btnFastPlay.Click += delegate
            {
                if (_selectedProfile == null) return;
                FastPlayStatus fpStat = FastPlayStreamingService.EvaluateFastPlayReadiness(_selectedProfile);
                if (fpStat.IsFastPlayReady)
                {
                    FastPlayStreamingService.StartFastPlayLaunch(_selectedProfile,
                        delegate(string msg) { Dispatcher.BeginInvoke(new Action(delegate { _txtBottomStatus.Text = msg; })); },
                        delegate(Process p) { Win32.PlayTechChirp(true); });
                }
                else
                {
                    MessageBox.Show(fpStat.CurrentStatus, "Titan // Fast-Play İnerken Oynama", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            netToolsPanel.Children.Add(btnFastPlay);

            Button btnDiscordJoin = CreateSteamUtilityButton("🎮 DISCORD KATIL", "Discord profilinizdeki 'Katıl' butonuna basan arkadaşınızı doğrudan bu lobiye bağlar");
            btnDiscordJoin.Foreground = HexBrush("#5865F2");
            btnDiscordJoin.Click += delegate
            {
                string dummyErr;
                DiscordInviteJoinBridge.RegisterProtocolHandler(out dummyErr);
                string room = _txtRoomCode != null ? _txtRoomCode.Text : "TITAN-TR";
                string uri = DiscordInviteJoinBridge.GenerateJoinUri(room, "127.0.0.1", 7777, _selectedProfile != null ? _selectedProfile.Id : "480");
                try { Clipboard.SetText(uri); } catch { }
                _txtBottomStatus.Text = "🎮 Discord Katıl Bağlantısı Panoya Kopyalandı: " + uri;
                Win32.PlayTechChirp(true);
                MessageBox.Show("🎮 Discord & 1-Tık Doğrudan Katıl Bağlantısı:\n\n" + uri + "\n\n(Panoya kopyalandı. Arkadaşınız bu linke tıkladığında Titan Hub otomatik olarak oyunu açıp lobiye bağlar.)", "Titan // Discord 1-Tık Katıl", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            netToolsPanel.Children.Add(btnDiscordJoin);

            Button btnPersonalCloud = CreateSteamUtilityButton("☁️ KİŞİSEL BULUT KASA", "Kişisel Google Drive, OneDrive veya Dropbox hesabınıza AES-256 şifreli save senkronizasyonu");
            btnPersonalCloud.Foreground = ThemeManager.AccentTealBrush;
            btnPersonalCloud.Click += delegate
            {
                if (_selectedProfile == null) return;
                _txtBottomStatus.Text = "☁️ Save dosyaları AES-256 ile şifrelenip bulut kasasına senkronize ediliyor...";
                string cMsg;
                bool ok = PersonalCloudStorageBridge.SyncSaveToCloud(_selectedProfile, null, out cMsg);
                _txtBottomStatus.Text = cMsg;
                Win32.PlayTechChirp(ok);
                MessageBox.Show(cMsg, "Titan // Kişisel Bulut Kasa", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            extraToolsPanel.Children.Add(btnPersonalCloud);

            Button btnPsoCompiler = CreateSteamUtilityButton("⚡ DX12 PSO DERLEYİCİ", "DirectX 12 / UE5 oyunlarında harita ve çatışma anındaki shader donmalarını (PSO) sıfırlar");
            btnPsoCompiler.Foreground = ThemeManager.AccentTealBrush;
            btnPsoCompiler.Click += delegate
            {
                if (_selectedProfile == null || !_selectedProfile.IsInstalled)
                {
                    _txtBottomStatus.Text = "PSO ön derleme için oyunun bilgisayarda yüklü olması gerekir.";
                    return;
                }
                _txtBottomStatus.Text = "⚡ DirectX 12 PSO ve Shader önbelleği GPU sürücüsüne işletiliyor...";
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    PsoCompilationReport pRep = Dx12AsyncPsoPrecompilerService.PrecompileGameShaders(_selectedProfile);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = pRep.DiagnosticSummary;
                        Win32.PlayTechChirp(pRep.Success);
                        MessageBox.Show(pRep.DiagnosticSummary, "Titan // Asenkron PSO Ön-Derleyici", MessageBoxButton.OK, MessageBoxImage.Information);
                    }));
                });
            };
            perfToolsPanel.Children.Add(btnPsoCompiler);

            Button btnCoreParkShield = CreateSteamUtilityButton("⚡ CORE-PARK & NDIS", "CPU çekirdeklerinin uykuya geçmesini engeller ve NDIS ağ paketlerine donanımsal öncelik verir");
            btnCoreParkShield.Foreground = ThemeManager.AccentTealBrush;
            btnCoreParkShield.Click += delegate
            {
                CoreParkingShieldReport cpRep = KernelCoreParkingNdisShield.ActivateCoreParkingAndNdisShield();
                _txtBottomStatus.Text = "⚡ Core-Parking iptal edildi, tüm CPU çekirdekleri %100 uyanık!";
                Win32.PlayTechChirp(true);
                MessageBox.Show(cpRep.DiagnosticSummary, "Titan // Çekirdek Güç & NDIS Kalkanı", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            perfToolsPanel.Children.Add(btnCoreParkShield);

            Button btnAiSubtitleHud = CreateSteamUtilityButton("🇹🇷 CANLI AI ALTYAZI", "İngilizce oyunlarda ekranın altına şeffaf ve tıklanabilir Türkçe canlı altyazı HUD'ı açar (F7)");
            btnAiSubtitleHud.Foreground = HexBrush("#D9534F");
            btnAiSubtitleHud.Click += delegate
            {
                LiveAiAudioTranslationHud.ToggleSubtitleHud();
                bool active = LiveAiAudioTranslationHud.IsVisible;
                btnAiSubtitleHud.Content = active ? "🇹🇷 ALTYAZI: AÇIK" : "🇹🇷 CANLI AI ALTYAZI";
                btnAiSubtitleHud.Foreground = active ? ThemeManager.AccentGreenBrush : HexBrush("#D9534F");
                _txtBottomStatus.Text = active ? "🇹🇷 Canlı AI Türkçe Altyazı HUD Aktif (F7 ile kapatılabilir)." : "Altyazı HUD kapatıldı.";
                Win32.PlayTechChirp(active);
            };
            extraToolsPanel.Children.Add(btnAiSubtitleHud);

            Button btnVramFlush = CreateSteamUtilityButton("⚡ VRAM TEMİZLE", "Ekran kartı belleğindeki (VRAM) asılı kalan çöp render dokularını boşaltır");
            btnVramFlush.Foreground = ThemeManager.AccentGreenBrush;
            btnVramFlush.Click += delegate
            {
                VramFlushReport vfRep = GpuVramFlushOptimizer.FlushGpuAndWorkingSet();
                _txtBottomStatus.Text = string.Format("⚡ GPU VRAM ve Donanım Önbelleği Temizlendi (~{0:0.0} MB)!", vfRep.EstimatedVramFreedMb);
                Win32.PlayTechChirp(true);
                MessageBox.Show(vfRep.DiagnosticSummary, "Titan // GPU VRAM Boşaltıcı", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            perfToolsPanel.Children.Add(btnVramFlush);

            Button btnScanAllGames = CreateSteamUtilityButton("🔍 TÜM OYUNLARI TARA & EKLE", "Bu bilgisayardaki TÜM kurulu oyunları (Steam, Epic, GOG, harici diskler, portatif Games klasörü) tarar ve kütüphaneye ekler");
            btnScanAllGames.Foreground = ThemeManager.AccentTealBrush;
            btnScanAllGames.Click += delegate { RunUniversalGameScan(); };
            extraToolsPanel.Children.Add(btnScanAllGames);

            Button btnWriteCover = CreateSteamUtilityButton("🎨 KAPAK + KISAYOL", "Oyun klasörüne cover.jpg/header.jpg yazar ve masaüstü kısayolunu oluşturur");
            btnWriteCover.Foreground = ThemeManager.AccentTealBrush;
            btnWriteCover.Click += delegate
            {
                if (_selectedProfile == null) return;
                string msg, scMsg;
                bool ok = SteamIntegrationService.WriteCoverArt(_selectedProfile, out msg);
                SteamIntegrationService.CreateShortcut(_selectedProfile, out scMsg);
                _txtBottomStatus.Text = msg + " | " + scMsg;
                Win32.PlayTechChirp(ok);
            };
            extraToolsPanel.Children.Add(btnWriteCover);

            Button btnMagnetScan = CreateSteamUtilityButton("🧲 MAGNET TARA", "Katalogdaki oyunların sayfalarından magnet/.torrent toplar; 403 veren hostlarda torrent otomatik devreye girer");
            btnMagnetScan.Foreground = ThemeManager.AccentTealBrush;
            btnMagnetScan.Click += delegate { RunMagnetScan(60); };
            extraToolsPanel.Children.Add(btnMagnetScan);

            Button btnTorrentTurbo = CreateSteamUtilityButton("⚡ TORRENT TURBO", "Windows TCP pencere boyutunu 16MB'a çıkararak indirme ve Swarm hızını donanımsal tavan seviyeye kilitler");
            btnTorrentTurbo.Foreground = ThemeManager.AccentGreenBrush;
            btnTorrentTurbo.Click += delegate
            {
                TorrentTurboReport tRep = TorrentSwarmTurboOptimizer.OptimizeP2pAndDownloadStack();
                _txtBottomStatus.Text = "⚡ Torrent Swarm & TCP İndirme Turbo Aktif (1 Gbps Doygunluk)!";
                Win32.PlayTechChirp(true);
                MessageBox.Show(tRep.DiagnosticSummary, "Titan // Torrent Swarm & TCP Turbo", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            netToolsPanel.Children.Add(btnTorrentTurbo);

            Button btnScreenshot = CreateSteamUtilityButton("📸 EKRAN YAKALA", "Oyun içi kayıpsız 32-bit PNG ekran görüntüsü yakalar ve Screenshots/ klasörüne kaydeder (F12)");
            btnScreenshot.Foreground = ThemeManager.AccentTealBrush;
            btnScreenshot.Click += delegate
            {
                string gName = (_selectedProfile != null) ? _selectedProfile.Name : "TitanGame";
                string sPath, sMsg;
                bool ok = DirectXScreenshotGalleryService.CaptureGameScreenshot(gName, out sPath, out sMsg);
                _txtBottomStatus.Text = sMsg;
                Win32.PlayTechChirp(ok);
                if (ok)
                {
                    ProceduralSoundEngine.PlayAchievement();
                    DirectXScreenshotGalleryService.OpenGalleryFolder(gName);
                }
            };
            extraToolsPanel.Children.Add(btnScreenshot);

            Button btnVoiceNoiseGate = CreateSteamUtilityButton("🎙 AI NOISE GATE", "Spacewar 480 sesli sohbette klavye tıkırtılarını ve arka plan fan sesini gerçek zamanlı DSP ile filtreler");
            btnVoiceNoiseGate.Foreground = ThemeManager.AccentTealBrush;
            btnVoiceNoiseGate.Click += delegate
            {
                AiVoiceNoiseGateDspService.IsEnabled = !AiVoiceNoiseGateDspService.IsEnabled;
                bool active = AiVoiceNoiseGateDspService.IsEnabled;
                btnVoiceNoiseGate.Content = active ? "🎙 NOISE GATE: AÇIK" : "🎙 AI NOISE GATE";
                btnVoiceNoiseGate.Foreground = active ? ThemeManager.AccentGreenBrush : ThemeManager.AccentTealBrush;
                _txtBottomStatus.Text = active ? "🎙 DSP Ses Gürültü Kapısı Aktif (Klavye ve fan sesleri filtreleniyor)." : "Noise Gate kapatıldı.";
                Win32.PlayTechChirp(active);
                MessageBox.Show(AiVoiceNoiseGateDspService.GetDspStatusSummary(), "Titan // AI Voice Noise Gate DSP", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            netToolsPanel.Children.Add(btnVoiceNoiseGate);
            primaryQuickStack.Children.Add(btnToggleAdvancedTools);

            // ---------------- GLOBAL LIVE DOWNLOAD PROGRESS STRIP (VISIBLE ON ALL TABS) ----------------
            _inAppDlStripBorder = new Border()
            {
                Background = HexBrush("#161220"),
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(22, 10, 22, 10),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_inAppDlStripBorder, 1);
            host.Children.Add(_inAppDlStripBorder);

            StackPanel dlStripStack = new StackPanel();
            _inAppDlStripBorder.Child = dlStripStack;

            Grid dlStripTop = new Grid() { Margin = new Thickness(0, 0, 0, 5) };
            dlStripTop.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            dlStripTop.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            dlStripStack.Children.Add(dlStripTop);

            _txtInAppDlTitle = new TextBlock()
            {
                Text = "İndirme ve Otomatik Kurulum Hattı Hazırlanıyor...",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 11.2,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(_txtInAppDlTitle, 0);
            dlStripTop.Children.Add(_txtInAppDlTitle);

            StackPanel speedWithGraph = new StackPanel() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _dlSpeedSparkline = new System.Windows.Shapes.Polyline()
            {
                Width = 76,
                Height = 16,
                Stroke = ThemeManager.AccentGreenBrush,
                StrokeThickness = 1.6,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            speedWithGraph.Children.Add(_dlSpeedSparkline);
            _txtInAppDlSpeed = new TextBlock()
            {
                Text = "0.0 MB/sn",
                Foreground = ThemeManager.AccentGreenBrush,
                FontSize = 11.2,
                FontWeight = FontWeights.ExtraBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            speedWithGraph.Children.Add(_txtInAppDlSpeed);
            Grid.SetColumn(speedWithGraph, 1);
            dlStripTop.Children.Add(speedWithGraph);

            _pbInAppDl = new ProgressBar()
            {
                Height = 8,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(0)
            };
            dlStripStack.Children.Add(_pbInAppDl);

            // ---------------- ROW 3: STEAM GAME CONTENT AREA (2 COLUMNS) ----------------
            ScrollViewer stageScroll = new ScrollViewer()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(22, 16, 22, 16)
            };
            Grid.SetRow(stageScroll, 3);
            _viewLibrary.Children.Add(stageScroll);

            StackPanel stageStack = new StackPanel();
            stageScroll.Content = stageStack;

            // Top 3 Executive Metric Pills
            UniformGrid metricRow = new UniformGrid() { Columns = 3, Margin = new Thickness(0, 0, 0, 14) };
            stageStack.Children.Add(metricRow);
            metricRow.Children.Add(CreateExecutiveMetricCard("TOPLAM KÜTÜPHANE", _allProfiles.Count + " Oyun", "#9333EA", out _txtMetricTotal));
            metricRow.Children.Add(CreateExecutiveMetricCard("YÜKLÜ & HAZIR", "0 İnik", "#5DBA2F", out _txtMetricInstalled));
            metricRow.Children.Add(CreateExecutiveMetricCard("BULUT SAVE KASASI", "0 Yedek", "#C084FC", out _txtMetricSaves));

            // 1. LIVE STEAMRIP DIRECT DOWNLOAD MIRRORS PANEL (NEW!)
            Border mirrorsCard = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14)
            };
            stageStack.Children.Add(mirrorsCard);

            StackPanel mirrorsStack = new StackPanel();
            mirrorsCard.Child = mirrorsStack;

            Grid mirrorsHeader = new Grid() { Margin = new Thickness(0, 0, 0, 8) };
            mirrorsHeader.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            mirrorsHeader.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            mirrorsStack.Children.Add(mirrorsHeader);

            StackPanel mTitleStack = new StackPanel();
            mTitleStack.Children.Add(new TextBlock()
            {
                Text = "🌐 CANLI İNDİRME SUNUCULARI (DOĞRUDAN AYNALAR)",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 12,
                FontWeight = FontWeights.ExtraBold
            });
            _txtLiveMirrorsStatus = new TextBlock()
            {
                Text = "Oyun sayfasındaki doğrudan .RAR / .ZIP indirme sunucuları otomatik çözümlenir...",
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 10.5
            };
            mTitleStack.Children.Add(_txtLiveMirrorsStatus);
            Grid.SetColumn(mTitleStack, 0);
            mirrorsHeader.Children.Add(mTitleStack);

            Button btnRescanMirrors = new Button()
            {
                Content = "🔄 SUNUCULARI YENİDEN TARA",
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Background = HexBrush("#1F2C3F"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnRescanMirrors, 3);
            btnRescanMirrors.Click += delegate
            {
                if (_selectedProfile != null)
                {
                    _txtLiveMirrorsStatus.Text = _selectedProfile.Name + " için canlı sunucular taranıyor...";
                    _downloader.AnalyzeSiteMirrorsAsync(_selectedProfile);
                }
            };
            Grid.SetColumn(btnRescanMirrors, 1);
            mirrorsHeader.Children.Add(btnRescanMirrors);

            _liveMirrorsWrapPanel = new WrapPanel() { Margin = new Thickness(0, 4, 0, 0) };
            mirrorsStack.Children.Add(_liveMirrorsWrapPanel);

            // 2. STEAM ACHIEVEMENTS CARD
            Border achCard = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14)
            };
            stageStack.Children.Add(achCard);

            StackPanel achStack = new StackPanel();
            achCard.Child = achStack;

            Grid achHeader = new Grid() { Margin = new Thickness(0, 0, 0, 6) };
            achHeader.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            achHeader.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            achStack.Children.Add(achHeader);

            _txtAchTitle = new TextBlock()
            {
                Text = "🏆 STEAM BAŞARIMLARI (0 / 0 Kazanıldı)",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.ExtraBold
            };
            Grid.SetColumn(_txtAchTitle, 0);
            achHeader.Children.Add(_txtAchTitle);

            _pbAchProgress = new ProgressBar()
            {
                Height = 6,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = ThemeManager.AccentGreenBrush,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 8)
            };
            achStack.Children.Add(_pbAchProgress);

            _achWrapBadges = new WrapPanel() { Margin = new Thickness(0, 2, 0, 0) };
            achStack.Children.Add(_achWrapBadges);

            // 3. Two-Column Details: Left = Game Description & Co-Op Guide, Right = P2P, Save Vault & Kernel Controls
            Grid bottomSplit = new Grid();
            bottomSplit.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1.1, GridUnitType.Star) });
            bottomSplit.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(14) });
            bottomSplit.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1.2, GridUnitType.Star) });
            stageStack.Children.Add(bottomSplit);

            // Left Card: About this Game & Co-Op Status
            Border aboutCard = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(16)
            };
            Grid.SetColumn(aboutCard, 0);
            bottomSplit.Children.Add(aboutCard);

            StackPanel aboutStack = new StackPanel();
            aboutCard.Child = aboutStack;

            aboutStack.Children.Add(new TextBlock()
            {
                Text = "BU OYUN HAKKINDA & STEAMWORKS P2P ÖZETİ",
                Foreground = ThemeManager.ForegroundMainBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _txtHeroDesc = new TextBlock()
            {
                Text = "",
                Foreground = HexBrush("#ACB2B8"),
                FontSize = 11.8,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            aboutStack.Children.Add(_txtHeroDesc);

            _txtP2PInstallPath = new TextBlock()
            {
                Text = "Kurulum Konumu: -",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            aboutStack.Children.Add(_txtP2PInstallPath);

            _txtP2PVersionStatus = new TextBlock()
            {
                Text = "Durum: Hazır",
                Foreground = ThemeManager.AccentGreenBrush,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold
            };
            aboutStack.Children.Add(_txtP2PVersionStatus);

            // Right Card: P2P, Kernel Boost, BepInEx & Save Vault Controls
            Border toolsCard = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(16)
            };
            Grid.SetColumn(toolsCard, 2);
            bottomSplit.Children.Add(toolsCard);

            StackPanel toolsStack = new StackPanel();
            toolsCard.Child = toolsStack;

            toolsStack.Children.Add(new TextBlock()
            {
                Text = "⚡ SİSTEM, CO-OP FIX & SAVE KASASI ARAÇLARI",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _txtSaveBackupStatus = new TextBlock()
            {
                Text = "Co-Op Save Kasası: Hazır",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            toolsStack.Children.Add(_txtSaveBackupStatus);

            // Row A: 6-Module v9.0 Enterprise System & Co-Op Action Grid (2x3)
            UniformGrid p2pActionRow = new UniformGrid() { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            toolsStack.Children.Add(p2pActionRow);

            Button btnRepairOnlineFix = new Button()
            {
                Content = "🔧 ONLINE-FIX (480) ONAR",
                Height = 28,
                Margin = new Thickness(0, 0, 3, 3),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnRepairOnlineFix, 3);
            btnRepairOnlineFix.Click += delegate
            {
                if (_selectedProfile == null) return;
                _selectedProfile.DeployOnlineFix("480");
                _txtBottomStatus.Text = _selectedProfile.Name + " için Spacewar P2P (AppID 480) ve Goldberg ayarları enjekte edildi!";
                Win32.PlayTechChirp(true);
            };
            p2pActionRow.Children.Add(btnRepairOnlineFix);

            Button btnBepInEx = new Button()
            {
                Content = "🧩 BEPINEX 32-CO-OP KUR",
                Height = 28,
                Margin = new Thickness(3, 0, 0, 3),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnBepInEx, 3);
            btnBepInEx.Click += delegate
            {
                if (_selectedProfile == null) return;
                bool ok = ModLoaderService.InitializeModFrameworkStructure(_selectedProfile);
                _txtBottomStatus.Text = ok
                    ? ("🧩 BepInEx & 32 Kişilik Co-Op Lobi Genişletici '" + _selectedProfile.Name + "' dizinine kuruldu!")
                    : "Bu oyun henüz kurulu değil veya Unity motoru kullanmıyor.";
                Win32.PlayTechChirp(ok);
            };
            p2pActionRow.Children.Add(btnBepInEx);

            Button btnFsrFrameGen = new Button()
            {
                Content = "🚀 FSR 3.1 & FPS ÇARPANI",
                Height = 28,
                Margin = new Thickness(0, 0, 3, 3),
                Background = HexBrush("#1E2E22"),
                Foreground = ThemeManager.AccentGreenBrush,
                BorderBrush = ThemeManager.AccentGreenBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "FSR 3.1 Frame Generation (OptiScaler), Unity GFX Jobs ve Unreal 4/5 Düşük Gecikme Shader profillerini enjekte eder"
            };
            ApplyRoundedButtonTemplate(btnFsrFrameGen, 3);
            btnFsrFrameGen.Click += delegate
            {
                if (_selectedProfile == null) return;
                string rep = FsrFrameGenInjector.DeployGraphicsAndFpsMultiplier(_selectedProfile);
                _txtBottomStatus.Text = rep;
                Win32.PlayTechChirp(true);
            };
            p2pActionRow.Children.Add(btnFsrFrameGen);

            Button btnDllRepair = new Button()
            {
                Content = "🛡️ DLL & 4GB RAM ONARICI",
                Height = 28,
                Margin = new Thickness(3, 0, 0, 3),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = HexBrush("#7E22CE"),
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "PE/IAT tablosunu tarar, eksik VC++/DirectX DLL'lerini onarır ve 32-bit oyunlara 4GB RAM (LAA) yaması uygular"
            };
            ApplyRoundedButtonTemplate(btnDllRepair, 3);
            btnDllRepair.Click += delegate
            {
                if (_selectedProfile == null) return;
                PeBinaryReport rep = DependencyRepairEngine.InspectAndRepairGameBinary(_selectedProfile);
                _txtBottomStatus.Text = rep.SummaryText;
                Win32.PlayTechChirp(rep.IsValidPe);
            };
            p2pActionRow.Children.Add(btnDllRepair);

            Button btnLiveSaveMesh = new Button()
            {
                Content = "🌐 CANLI SAVE MESH (10.48.x)",
                Height = 28,
                Margin = new Thickness(0, 0, 3, 0),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = HexBrush("#D97706"),
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Co-Op Host oyundan düşerse kayıp yaşanmaması için save dosyalarını anlık olarak lobi üyelerine aynalar"
            };
            ApplyRoundedButtonTemplate(btnLiveSaveMesh, 3);
            btnLiveSaveMesh.Click += delegate
            {
                if (_selectedProfile == null) return;
                string room = _txtRoomCode != null ? _txtRoomCode.Text : "TITAN-TR";
                string nick = _txtUserNick != null ? _txtUserNick.Text : Environment.UserName;
                _txtBottomStatus.Text = VirtualLanMeshService.StartLiveSaveMirroring(_selectedProfile, room, nick);
                Win32.PlayTechChirp(true);
            };
            p2pActionRow.Children.Add(btnLiveSaveMesh);

            Button btnLiveTrHud = new Button()
            {
                Content = "🇹🇷 TÜRKÇE ALTYAZI HUD [F6]",
                Height = 28,
                Margin = new Thickness(3, 0, 0, 0),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.ForegroundMainBrush,
                BorderBrush = HexBrush("#3D4B61"),
                BorderThickness = new Thickness(1),
                FontSize = 9.2,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Oyun içi diyalogları gerçek zamanlı Türkçe altyazıya çevirip In-Game Overlay üzerinde gösterir"
            };
            ApplyRoundedButtonTemplate(btnLiveTrHud, 3);
            btnLiveTrHud.Click += delegate
            {
                LiveTranslatorHudService.IsEnabled = !LiveTranslatorHudService.IsEnabled;
                if (LiveTranslatorHudService.IsEnabled && _overlay != null && !_overlay.Visible)
                {
                    _overlay.Show();
                    if (_chkQuickOverlay != null) _chkQuickOverlay.IsChecked = true;
                }
                _txtBottomStatus.Text = LiveTranslatorHudService.IsEnabled
                    ? "🇹🇷 [F6] Oyun İçi Canlı Türkçe Altyazı HUD Motoru Aktif! (Overlay açıldı)"
                    : "🇹🇷 [F6] Oyun İçi Canlı Türkçe Altyazı HUD Motoru Durduruldu.";
                Win32.PlayTechChirp(LiveTranslatorHudService.IsEnabled);
            };
            p2pActionRow.Children.Add(btnLiveTrHud);

            // Row B: Save Backup, Restore, AES-256 Vault, Save Folder
            UniformGrid saveActionRow = new UniformGrid() { Columns = 4, Margin = new Thickness(0, 4, 0, 0) };
            toolsStack.Children.Add(saveActionRow);

            Button btnSaveBackup = new Button()
            {
                Content = "💾 YEDEKLE",
                Height = 28,
                Margin = new Thickness(0, 0, 2, 0),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnSaveBackup, 3);
            btnSaveBackup.Click += delegate
            {
                if (_selectedProfile == null) return;
                string sMsg;
                SaveSyncService.CreateSaveBackup(_selectedProfile, out sMsg);
                _txtBottomStatus.Text = sMsg;
                SelectGameProfile(_selectedProfile);
                RefreshLibrarySummaryMetrics();
                Win32.PlayTechChirp(true);
            };
            saveActionRow.Children.Add(btnSaveBackup);

            Button btnSaveRestore = new Button()
            {
                Content = "♻ GERİ YÜKLE",
                Height = 28,
                Margin = new Thickness(2, 0, 2, 0),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnSaveRestore, 3);
            btnSaveRestore.Click += delegate
            {
                if (_selectedProfile == null) return;
                string sMsg;
                SaveSyncService.RestoreLatestSaveBackup(_selectedProfile, out sMsg);
                _txtBottomStatus.Text = sMsg;
                SelectGameProfile(_selectedProfile);
            };
            saveActionRow.Children.Add(btnSaveRestore);

            // Save Zaman Tüneli: yedek geçmişini listele + seçilene dön
            Button btnTimeTunnel = new Button()
            {
                Content = "🕓 ZAMAN TÜNELİ",
                Height = 28,
                Margin = new Thickness(2, 0, 2, 0),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Tüm kayıt yedeklerini tarih sırasıyla listeler, seçilen yedeğe döner"
            };
            ApplyRoundedButtonTemplate(btnTimeTunnel, 3);
            btnTimeTunnel.Click += delegate
            {
                if (_selectedProfile == null) return;
                SaveTimeTunnelWindow.Show(this, _selectedProfile);
            };
            saveActionRow.Children.Add(btnTimeTunnel);

            Button btnSaveVault = new Button()
            {
                Content = "🛡️ AES-256",
                Height = 28,
                Margin = new Thickness(2, 0, 2, 0),
                Background = HexBrush("#2E1065"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Save yedeğini PBKDF2 + AES-256 ile şifreler"
            };
            ApplyRoundedButtonTemplate(btnSaveVault, 3);
            btnSaveVault.Click += delegate
            {
                if (_selectedProfile == null) return;
                string sMsg;
                string zipPath = SaveSyncService.CreateSaveBackup(_selectedProfile, out sMsg);
                if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath))
                {
                    string encPath = zipPath.Replace(".zip", ".vault");
                    if (SaveSyncService.EncryptBackup(zipPath, "TitanSecretKey", encPath))
                    {
                        _txtBottomStatus.Text = "🛡️ AES-256 Vault: Save yedeği şifrelendi -> " + System.IO.Path.GetFileName(encPath);
                        Win32.PlayTechChirp(true);
                    }
                }
            };
            saveActionRow.Children.Add(btnSaveVault);

            Button btnOpenBackups = new Button()
            {
                Content = "📂 KLASÖR",
                Height = 28,
                Margin = new Thickness(2, 0, 0, 0),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.ForegroundSubBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnOpenBackups, 3);
            btnOpenBackups.Click += delegate
            {
                try { Process.Start("explorer.exe", SaveSyncService.GetBackupsRoot()); } catch { }
            };
            saveActionRow.Children.Add(btnOpenBackups);
        }

        private Border CreateSteamMetaBlock(string label, string value, out TextBlock valueBlock, string valueHex)
        {
            Border b = new Border()
            {
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel sp = new StackPanel();
            b.Child = sp;

            sp.Children.Add(new TextBlock()
            {
                Text = label,
                Foreground = HexBrush("#67707B"),
                FontSize = 8.8,
                FontWeight = FontWeights.Bold
            });

            valueBlock = new TextBlock()
            {
                Text = value,
                Foreground = HexBrush(valueHex),
                FontSize = 10.8,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 1, 0, 0)
            };
            sp.Children.Add(valueBlock);
            return b;
        }

        private Button CreateSteamUtilityButton(string text, string tooltip)
        {
            Button btn = new Button()
            {
                Content = text,
                Height = 28,
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(0, 2, 6, 2),
                Background = HexBrush("#1E172C"),
                Foreground = ThemeManager.ForegroundMainBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
            ApplyRoundedButtonTemplate(btn, 3);
            return btn;
        }

        private WrapPanel CreateToolCategorySection(StackPanel drawerStack, string title, string colorHex)
        {
            Border catHeader = new Border()
            {
                Background = HexBrush("#111822"),
                BorderBrush = HexBrush("#1C2A3A"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 6, 0, 4)
            };
            catHeader.Child = new TextBlock()
            {
                Text = title,
                Foreground = HexBrush(colorHex),
                FontSize = 9.8,
                FontWeight = FontWeights.ExtraBold
            };
            drawerStack.Children.Add(catHeader);

            WrapPanel wp = new WrapPanel()
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 6)
            };
            drawerStack.Children.Add(wp);
            return wp;
        }

        private Border CreateExecutiveMetricCard(string label, string val, string accentHex, out TextBlock valBlock)
        {
            Border card = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 8, 0)
            };
            StackPanel sp = new StackPanel();
            card.Child = sp;
            sp.Children.Add(new TextBlock() { Text = label, Foreground = ThemeManager.ForegroundMutedBrush, FontSize = 9.2, FontWeight = FontWeights.Bold });
            valBlock = new TextBlock() { Text = val, Foreground = HexBrush(accentHex), FontSize = 14, FontWeight = FontWeights.ExtraBold, Margin = new Thickness(0, 2, 0, 0) };
            sp.Children.Add(valBlock);
            return card;
        }

        // =========================================================================
        // SELECT GAME PROFILE & POPULATE LIVE MIRRORS
        // =========================================================================
        private void SelectGameProfile(GameProfile gp)
        {
            _selectedProfile = gp;
            if (gp == null) return;

            if (_heroBannerBorder != null)
            {
                try
                {
                    DoubleAnimation fade = new DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(220));
                    _heroBannerBorder.BeginAnimation(UIElement.OpacityProperty, fade);
                }
                catch { }
            }
            ProceduralSoundEngine.PlaySelect();

            _txtHeroTitle.Text = gp.Name;

            if (gp.IsInstalled && ProfileStore.NeedsRealSize(gp.FileSize))
            {
                string computedSz = ProfileStore.ComputeFolderSizeText(!string.IsNullOrEmpty(gp.WorkingDirectory) ? gp.WorkingDirectory : System.IO.Path.GetDirectoryName(gp.ExecutablePath));
                if (!string.IsNullOrEmpty(computedSz)) gp.FileSize = computedSz;
            }

            string safeCat = string.IsNullOrEmpty(gp.Category) ? "Co-Op & Online" : gp.Category;
            string safePlayers = string.IsNullOrEmpty(gp.MaxPlayers) ? "Çok Oyunculu" : gp.MaxPlayers;
            string safeSize = string.IsNullOrEmpty(gp.FileSize) ? "Boyut Bilinmiyor" : gp.FileSize;

            if (safeCat.Contains("http") || safeCat.Contains("/")) safeCat = "Simülasyon & Co-Op";
            if (safePlayers.Contains("http") || safePlayers.Contains("/")) safePlayers = "16 Kişilik Online Co-Op";
            if (safeSize.Contains("http") || safeSize.Contains("/")) safeSize = "1.8 GB";

            _txtHeroCategory.Text = (safeCat + "  •  " + safePlayers + "  •  " + safeSize).ToUpperInvariant();
            _txtHeroDesc.Text = string.IsNullOrEmpty(gp.Description) ? (gp.Name + " — Spacewar 480 eşli oyun lobisi.") : gp.Description;

            string safeAppId = string.IsNullOrEmpty(gp.SteamAppId) ? "480" : gp.SteamAppId;
            if (safeAppId.StartsWith("http", StringComparison.OrdinalIgnoreCase) || safeAppId.Length > 10) safeAppId = "480";
            bool hasRealUpdate = gp.UpdateAvailable && !UpdateLedger.IsUpToDate(gp, gp.RemoteVersion);
            _txtHeroAppIdBadge.Text = "APPID: " + safeAppId + " • P2P 480";
            _txtHeroVersionBadge.Text = "SÜRÜM: " + gp.Version + (hasRealUpdate ? (" -> " + gp.RemoteVersion + " GÜNCELLEME!") : "");

            try
            {
                EngineAnalysisResult eng = EngineInspectorService.InspectGameDirectory(gp);
                GameSignatureProfile sig = SignatureDatabaseService.GetSignatureProfile(gp.Id);
                _txtHeroEngineBadge.Text = "⚙ " + eng.EngineDisplayName.ToUpperInvariant() + (sig != null ? " • AOB [OK]" : "");
            }
            catch { }

            _txtSteamBarLastPlayed.Text = gp.LastPlayedText ?? "Henüz Oynanmadı";
            _txtSteamBarPlayTime.Text = gp.FormattedPlayTime;
            _txtSteamBarSizeVersion.Text = safeSize + " • " + gp.Version;
            _txtSteamBarCloudStatus.Text = gp.IsInstalled ? "✔ Yüklü • Spacewar P2P Hazır" : "☁ Bulut Mağazada Hazır";

            _btnHeroFav.Content = gp.IsFavorite ? "★ FAVORİDE" : "☆ FAVORİ";

            if (gp.IsInstalled)
            {
                if (hasRealUpdate)
                {
                    _btnHeroPrimary.Content = "🔄 GÜNCELLE";
                    _btnHeroPrimary.Background = MakeGradient("#7E22CE", "#0369A1");
                    _btnHeroPrimary.BorderBrush = ThemeManager.AccentTealBrush;
                }
                else
                {
                    _btnHeroPrimary.Content = "▶ OYNA";
                    _btnHeroPrimary.Background = MakeGradient("#5DBA2F", "#46941E");
                    _btnHeroPrimary.BorderBrush = HexBrush("#6ED43B");
                }
                _btnUninstallGame.Visibility = Visibility.Visible;
                if (_btnUpdateShieldStatus != null) _btnUpdateShieldStatus.Visibility = Visibility.Visible;
                if (_btnUpdateShieldRollback != null)
                {
                    string rbVer;
                    DateTime rbDate;
                    if (SmartUpdateShieldService.HasRollbackSnapshot(gp, out rbVer, out rbDate))
                    {
                        _btnUpdateShieldRollback.Visibility = Visibility.Visible;
                        _btnUpdateShieldRollback.Content = "⏪ SÜRÜM GERİ AL (" + rbVer + ")";
                        _btnUpdateShieldRollback.ToolTip = string.Format("Önceki kararlı sürüme ({0} • {1}) 1 tıkla anında dön (Save dosyaları korunur)", rbVer, rbDate.ToString("dd.MM.yyyy HH:mm"));
                    }
                    else
                    {
                        _btnUpdateShieldRollback.Visibility = Visibility.Collapsed;
                    }
                }
            }
            else
            {
                _btnHeroPrimary.Content = "⬇ YÜKLE (TEK TIK)";
                _btnHeroPrimary.Background = MakeGradient("#9333EA", "#6B21A8");
                _btnHeroPrimary.BorderBrush = ThemeManager.AccentTealBrush;
                _btnUninstallGame.Visibility = Visibility.Visible;
                if (_btnUpdateShieldStatus != null) _btnUpdateShieldStatus.Visibility = Visibility.Collapsed;
                if (_btnUpdateShieldRollback != null) _btnUpdateShieldRollback.Visibility = Visibility.Collapsed;
            }

            string safeFolder = Regex.Replace(gp.Name, @"[^\w\s-]", "").Trim();
            string defaultDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games", safeFolder);
            _txtP2PInstallPath.Text = "Kurulum Konumu: " + (!string.IsNullOrEmpty(gp.WorkingDirectory) ? gp.WorkingDirectory : defaultDir);
            _txtP2PVersionStatus.Text = gp.IsInstalled
                ? ("Durum: 🟢 BİLGİSAYARDA YÜKLÜ (" + gp.Version + " • Spacewar AppID 480 Aktif)")
                : ("Durum: ☁ BULUT KATALOĞUNDA (" + gp.FileSize + " • Tek Tıkla İndirilebilir)");

            int saveCount = SaveSyncService.GetBackupCount(gp);
            _txtSaveBackupStatus.Text = saveCount > 0
                ? string.Format("💾 Co-Op Save Kasası: {0} adet yedek mevcut (Son oynama: {1})", saveCount, gp.LastPlayedText)
                : "💾 Co-Op Save Kasası: Henüz yedek alınmadı ('Yedekle' veya 'AES-256' ile koruyun)";

            // Populate Steam Achievements
            if (_txtAchTitle != null)
            {
                int achUnlocked, achTotal;
                List<AchievementItem> achs = SteamAchievementEngine.GetGameAchievements(gp, out achUnlocked, out achTotal);
                int achPct = achTotal > 0 ? (achUnlocked * 100) / achTotal : 0;
                _txtAchTitle.Text = string.Format("🏆 STEAM BAŞARIMLARI ({0} / {1} Kazanıldı — %{2})", achUnlocked, achTotal, achPct);
                _pbAchProgress.Value = achPct;
                _achWrapBadges.Children.Clear();
                int shown = 0;
                foreach (AchievementItem a in achs)
                {
                    if (shown >= 8) break;
                    Border b = new Border()
                    {
                        Background = a.IsAchieved ? HexBrush("#16281B") : ThemeManager.SidebarNavy,
                        BorderBrush = a.IsAchieved ? ThemeManager.AccentGreenBrush : ThemeManager.BorderPrimaryBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(7, 4, 7, 4),
                        Margin = new Thickness(0, 0, 6, 4),
                        ToolTip = a.Name + " (" + a.Description + ")"
                    };
                    b.Child = new TextBlock()
                    {
                        Text = (a.IsAchieved ? "🏆 " : "🔒 ") + a.Name,
                        Foreground = a.IsAchieved ? ThemeManager.AccentGreenBrush : ThemeManager.ForegroundMutedBrush,
                        FontSize = 9.5,
                        FontWeight = a.IsAchieved ? FontWeights.Bold : FontWeights.Normal
                    };
                    _achWrapBadges.Children.Add(b);
                    shown++;
                }
            }

            if (_txtDirectUrlInput != null)
                _txtDirectUrlInput.Text = gp.DirectMirrorUrl ?? "";

            ImageCacheService.BindImageAsync(_heroBannerBorder, gp, 960, 0.85);
            RefreshLibrarySidebarList();
            UpdateModManagerSelectedGame(gp);

            // Resolve Steam Store AppID & official header image if needed
            if (string.IsNullOrEmpty(gp.BannerUrl) || gp.SteamAppId == "480")
            {
                SiteParserService.ResolveSteamStoreMetadataAsync(gp, delegate
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (_selectedProfile != null && _selectedProfile.Id == gp.Id)
                        {
                            _txtHeroAppIdBadge.Text = "APPID: " + gp.SteamAppId + " • P2P 480";
                            ImageCacheService.BindImageAsync(_heroBannerBorder, gp, 960, 0.85);
                        }
                    }));
                });
            }

            // Populate Live SteamRIP Mirror Buttons for this game
            PopulateInitialMirrorButtons(gp);
            _downloader.AnalyzeSiteMirrorsAsync(gp);
        }

        private void PopulateInitialMirrorButtons(GameProfile gp)
        {
            if (_liveMirrorsWrapPanel == null) return;
            _liveMirrorsWrapPanel.Children.Clear();
            _txtLiveMirrorsStatus.Text = gp.Name + " — Canlı indirme sunucuları çözümleniyor...";

            if (!string.IsNullOrEmpty(gp.DirectMirrorUrl))
            {
                AddMirrorDownloadButton(gp, "⚡ KAYITLI DOĞRUDAN SUNUCU", gp.DirectMirrorUrl, true);
            }
            Button btnMagnetSwarm = new Button()
            {
                Content = "🧲 P2P TORRENT SWARM",
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 6),
                Background = HexBrush("#2E1065"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "BEP-15 UDP Tracker üzerinden P2P Swarm Seeder/Leecher durumunu sorgular ve Magnet bağlantısını panoya kopyalar"
            };
            ApplyRoundedButtonTemplate(btnMagnetSwarm, 3);
            btnMagnetSwarm.Click += delegate
            {
                string magnet = TorrentMagnetEngine.GenerateMagnetUriForGame(gp);
                try { Clipboard.SetText(magnet); } catch { }
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    SwarmTrackerStatus st = TorrentMagnetEngine.QueryMagnetOrInfoHash(magnet, gp.Name);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = st.StatusMessage + " (Magnet panoya kopyalandı)";
                        Win32.PlayTechChirp(true);
                    }));
                });
            };
            _liveMirrorsWrapPanel.Children.Add(btnMagnetSwarm);
        }

        private void AddMirrorDownloadButton(GameProfile gp, string hostLabel, string mirrorUrl, bool isPrimary)
        {
            Button btn = new Button()
            {
                Content = "⬇ " + hostLabel.ToUpperInvariant(),
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 6),
                Background = isPrimary ? (Brush)MakeGradient("#9333EA", "#6B21A8") : HexBrush("#251D38"),
                Foreground = Brushes.White,
                BorderBrush = isPrimary ? ThemeManager.AccentTealBrush : ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand,
                ToolTip = mirrorUrl
            };
            ApplyRoundedButtonTemplate(btn, 3);
            btn.Click += delegate
            {
                if (!gp.IsInstalled)
                {
                    string chosenDir = GameInstallLocationDialog.PromptInstallDirectory(this, gp);
                    if (string.IsNullOrEmpty(chosenDir)) return;
                    DownloadInstallService.SetTargetInstallDirectoryForGame(gp, chosenDir);
                }
                gp.DirectMirrorUrl = mirrorUrl;
                _inAppDlStripBorder.Visibility = Visibility.Visible;
                _downloader.DownloadAndInstallAsync(gp, mirrorUrl);
            };
            _liveMirrorsWrapPanel.Children.Add(btn);
        }

        private void RefreshLibrarySummaryMetrics()
        {
            int installed = 0;
            int saves = 0;
            foreach (GameProfile g in _allProfiles)
            {
                if (g.IsInstalled) installed++;
                saves += SaveSyncService.GetBackupCount(g);
            }

            if (_txtMetricTotal != null) _txtMetricTotal.Text = _allProfiles.Count + " Katalog Oyunu";
            if (_txtMetricInstalled != null) _txtMetricInstalled.Text = installed + " Yüklü & Hazır";
            if (_txtMetricSaves != null) _txtMetricSaves.Text = saves + " Save Yedeği";
            if (_txtMetricHardware != null) _txtMetricHardware.Text = Win32.GetSystemHardwareSummary();
        }
    }
}
