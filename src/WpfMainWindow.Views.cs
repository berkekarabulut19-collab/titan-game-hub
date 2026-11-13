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
        private TextBox _txtMyStunIpPort;
        private TextBox _txtPeerTargetInput;
        private TextBlock _txtTunnelLiveStatus;
        private StackPanel _lanRoomsContainer;
        private StackPanel _modsListContainer;
        private TextBlock _txtEngineInspectSummary;
        private TextBlock _txtSigDbStatusText;

        // =========================================================================
        // VIEW 2: MAĞAZA & TÜM SİTE (MODERN STEAM-STYLE STOREFRONT)
        // =========================================================================
        private void BuildStoreCenterView(Grid host)
        {
            _viewStore = new Grid() { Margin = new Thickness(14, 8, 14, 8), Visibility = Visibility.Collapsed };
            _viewStore.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Row 0: Search & Actions Header
            _viewStore.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Row 1: Sources & Categories Wrap Strip
            _viewStore.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Row 2: Pagination & Load More Bar
            _viewStore.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // Row 3: Store Grid
            host.Children.Add(_viewStore);

            // Row 0: Top Header & Search Bar
            Border storeTopBorder = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(storeTopBorder, 0);
            _viewStore.Children.Add(storeTopBorder);

            Grid storeTopGrid = new Grid();
            storeTopGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            storeTopGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            storeTopGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            storeTopBorder.Child = storeTopGrid;

            StackPanel storeTitleStack = new StackPanel()
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };
            storeTitleStack.Children.Add(new TextBlock()
            {
                Text = "🎮 OYUN MAĞAZASI",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 14,
                FontWeight = FontWeights.ExtraBold
            });
            _txtStoreCountBadge = new TextBlock()
            {
                Text = "5000+ Oyun Kataloğu • Türlere Göre Hızlı Keşfet & İndir",
                Foreground = ThemeManager.AccentGreenBrush,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            };
            storeTitleStack.Children.Add(_txtStoreCountBadge);
            Grid.SetColumn(storeTitleStack, 0);
            storeTopGrid.Children.Add(storeTitleStack);

            // Search Box (clean, uncluttered)
            Border searchInputBorder = new Border()
            {
                Background = HexBrush("#0B1118"),
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 0, 8, 0),
                Margin = new Thickness(0, 0, 10, 0),
                Height = 34,
                MinWidth = 220
            };
            Grid.SetColumn(searchInputBorder, 1);
            storeTopGrid.Children.Add(searchInputBorder);

            Grid searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            searchInputBorder.Child = searchGrid;

            TextBlock iconSearch = new TextBlock()
            {
                Text = "🔍",
                FontSize = 12,
                Foreground = ThemeManager.AccentTealBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(iconSearch, 0);
            searchGrid.Children.Add(iconSearch);

            Grid inputHostGrid = new Grid() { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(inputHostGrid, 1);
            searchGrid.Children.Add(inputHostGrid);

            TextBlock txtSearchWatermark = new TextBlock()
            {
                Text = "Oyun adı yazın (Örn: GTA V, Cyberpunk, Elden Ring, Peak)...",
                Foreground = HexBrush("#56687D"),
                FontSize = 11.2,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            inputHostGrid.Children.Add(txtSearchWatermark);

            _txtStoreSearch = new TextBox()
            {
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                CaretBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(0),
                FontSize = 11.8,
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            inputHostGrid.Children.Add(_txtStoreSearch);

            DispatcherTimer searchDebounceTimer = new DispatcherTimer();
            searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(450);
            searchDebounceTimer.Tick += delegate
            {
                searchDebounceTimer.Stop();
                string q = (_txtStoreSearch.Text ?? "").Trim();
                if (string.IsNullOrEmpty(q)) return;

                try
                {
                    List<GameProfile> sourceList = _allProfiles ?? new List<GameProfile>();
                    List<GameProfile> matched = new List<GameProfile>();
                    string ql = q.ToLowerInvariant();
                    for (int i = 0; i < sourceList.Count; i++)
                    {
                        GameProfile gp = sourceList[i];
                        if (gp == null) continue;
                        string name = gp.Name ?? "";
                        string cat = gp.Category ?? "";
                        if (name.ToLowerInvariant().Contains(ql) || cat.ToLowerInvariant().Contains(ql))
                            matched.Add(gp);
                    }
                    if (matched.Count > 0)
                    {
                        _storeFilteredProfiles = matched;
                        _txtStoreCountBadge.Text = string.Format("'{0}' için {1} yerel eşleşme (canlı taranıyor...)", q, matched.Count);
                        RefreshStorefrontGrid();
                    }
                }
                catch { }

                if (q.Length >= 2 && !_isFetchingLiveSite)
                {
                    _currentStorePage = 1;
                    TriggerLiveSiteSearchOrCategoryFetch(false);
                }
            };

            Button btnClearSearch = new Button()
            {
                Content = "✕",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Foreground = ThemeManager.ForegroundMutedBrush,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Aramayı Temizle"
            };
            btnClearSearch.Click += delegate
            {
                _txtStoreSearch.Text = "";
                txtSearchWatermark.Visibility = Visibility.Visible;
                searchDebounceTimer.Stop();
                _currentStorePage = 1;
                if (_activeSiteCategoryId == -1)
                {
                    _storeFilteredProfiles = new List<GameProfile>(_allProfiles);
                    _txtStoreCountBadge.Text = "Kütüphane & Öne Çıkanlar: " + _storeFilteredProfiles.Count + " Oyun";
                    RefreshStorefrontGrid();
                }
                else
                {
                    TriggerLiveSiteSearchOrCategoryFetch(false);
                }
            };
            Grid.SetColumn(btnClearSearch, 2);
            searchGrid.Children.Add(btnClearSearch);

            _txtStoreSearch.TextChanged += delegate
            {
                string q = (_txtStoreSearch.Text ?? "").Trim();
                txtSearchWatermark.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
                if (string.IsNullOrEmpty(q))
                {
                    searchDebounceTimer.Stop();
                    _currentStorePage = 1;
                    if (_activeSiteCategoryId == -1)
                    {
                        _storeFilteredProfiles = new List<GameProfile>(_allProfiles);
                        _txtStoreCountBadge.Text = "Kütüphane & Öne Çıkanlar: " + _storeFilteredProfiles.Count + " Oyun";
                        RefreshStorefrontGrid();
                    }
                    else
                    {
                        TriggerLiveSiteSearchOrCategoryFetch(false);
                    }
                }
                else
                {
                    searchDebounceTimer.Stop();
                    searchDebounceTimer.Start();
                }
            };

            _txtStoreSearch.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    searchDebounceTimer.Stop();
                    _currentStorePage = 1;
                    TriggerLiveSiteSearchOrCategoryFetch(false);
                }
            };

            // Right Action Buttons Strip (outside search box)
            StackPanel actionButtons = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(actionButtons, 2);
            storeTopGrid.Children.Add(actionButtons);

            Button btnLiveSearch = new Button()
            {
                Content = "🔍 ARA",
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = MakeGradient("#9333EA", "#6B21A8"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10.2,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnLiveSearch, 5);
            btnLiveSearch.Click += delegate
            {
                _currentStorePage = 1;
                TriggerLiveSiteSearchOrCategoryFetch(false);
            };
            actionButtons.Children.Add(btnLiveSearch);

            Button btnFetchLatestSite = new Button()
            {
                Content = "🔄 YENİLE",
                Height = 32,
                Padding = new Thickness(11, 0, 11, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = HexBrush("#1E172C"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderAccentBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Önbelleği temizleyip canlı sitelerden taze oyun listesi çeker"
            };
            ApplyRoundedButtonTemplate(btnFetchLatestSite, 5);
            btnFetchLatestSite.Click += delegate
            {
                SiteParserService.InvalidateCache();
                _currentStorePage = 1;
                TriggerLiveSiteSearchOrCategoryFetch(false);
            };
            actionButtons.Children.Add(btnFetchLatestSite);

            Button btnImportFromUrl = new Button()
            {
                Content = "＋ URL EKLE",
                Height = 32,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = HexBrush("#1E172C"),
                Foreground = ThemeManager.ForegroundMainBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Oyun sayfası linki yapıştırarak kütüphaneye ekle"
            };
            ApplyRoundedButtonTemplate(btnImportFromUrl, 5);
            btnImportFromUrl.Click += delegate { ImportGameFromUrlDialog(); };
            actionButtons.Children.Add(btnImportFromUrl);

            Button btnBulkImport = new Button()
            {
                Content = "⬇ KATALOG ÇEK",
                Height = 32,
                Padding = new Thickness(10, 0, 10, 0),
                Background = MakeGradient("#166534", "#15803D"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentGreenBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Tüm sitelerin ilk sayfalarını tarayıp oyunları kalıcı kütüphaneye aktarır"
            };
            ApplyRoundedButtonTemplate(btnBulkImport, 5);
            btnBulkImport.Click += delegate { RunBulkCatalogImport(3); };
            actionButtons.Children.Add(btnBulkImport);

            // Row 1: Wrapped Source & Category Pills (all visible at a glance)
            WrapPanel catWrap = new WrapPanel()
            {
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(catWrap, 1);
            _viewStore.Children.Add(catWrap);
            _storeCatWrap = catWrap;

            foreach (SiteCategoryDef catDef in SiteParserService.Categories)
            {
                SiteCategoryDef capturedCat = catDef;
                Button cBtn = new Button()
                {
                    Content = capturedCat.Title,
                    Tag = capturedCat.Id,
                    Height = 26,
                    Padding = new Thickness(10, 0, 10, 0),
                    Margin = new Thickness(0, 0, 5, 5),
                    FontSize = 9.4,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1)
                };
                ApplyRoundedButtonTemplate(cBtn, 4);
                cBtn.Click += delegate
                {
                    _activeSiteCategoryId = capturedCat.Id;
                    _currentStorePage = 1;
                    HighlightStoreCategoryButtons();
                    if (capturedCat.Id == -1)
                    {
                        _storeFilteredProfiles = new List<GameProfile>(_allProfiles);
                        _txtStoreCountBadge.Text = "Kütüphane & Öne Çıkanlar: " + _storeFilteredProfiles.Count + " Oyun";
                        _txtStorePageIndicator.Text = string.Format("KÜTÜPHANE KATALOĞU ({0} OYUN)", _storeFilteredProfiles.Count);
                        RefreshStorefrontGrid();
                    }
                    else
                    {
                        TriggerLiveSiteSearchOrCategoryFetch(false);
                    }
                };
                _storeCatWrap.Children.Add(cBtn);
            }
            HighlightStoreCategoryButtons();

            // Row 2: Pagination & Load More Strip
            Border pageBar = new Border()
            {
                Background = HexBrush("#121A26"),
                BorderBrush = HexBrush("#1E2C40"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 0, 8),
                Height = 34
            };
            Grid.SetRow(pageBar, 2);
            _viewStore.Children.Add(pageBar);

            Grid pageGrid = new Grid();
            pageGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            pageGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            pageGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            pageGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            pageBar.Child = pageGrid;

            Button btnPrevPage = new Button()
            {
                Content = "◀ ÖNCEKİ SAYFA",
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                Background = HexBrush("#251D38"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnPrevPage, 4);
            btnPrevPage.Click += delegate
            {
                if (_currentStorePage > 1)
                {
                    _currentStorePage--;
                    if (_activeSiteCategoryId == -1 && string.IsNullOrWhiteSpace(_txtStoreSearch.Text))
                        _activeSiteCategoryId = -20;
                    TriggerLiveSiteSearchOrCategoryFetch(false);
                }
            };
            Grid.SetColumn(btnPrevPage, 0);
            pageGrid.Children.Add(btnPrevPage);

            _txtStorePageIndicator = new TextBlock()
            {
                Text = "TÜM SİTELER YÜKLENİYOR...",
                Foreground = ThemeManager.ForegroundSubBrush,
                FontSize = 10.2,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtStorePageIndicator, 1);
            pageGrid.Children.Add(_txtStorePageIndicator);

            Button btnAppendMore = new Button()
            {
                Content = "＋ DAHA FAZLA OYUN YÜKLE",
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Background = HexBrush("#16301B"),
                Foreground = ThemeManager.AccentGreenBrush,
                BorderBrush = ThemeManager.AccentGreenBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Sonraki sayfadaki oyunları mevcut vitrin listesinin altına ekler"
            };
            ApplyRoundedButtonTemplate(btnAppendMore, 4);
            btnAppendMore.Click += delegate
            {
                if (_activeSiteCategoryId == -1 && string.IsNullOrWhiteSpace(_txtStoreSearch.Text))
                    _activeSiteCategoryId = -20;
                _currentStorePage++;
                _storeDisplayLimit += 120;
                TriggerLiveSiteSearchOrCategoryFetch(true);
            };
            Grid.SetColumn(btnAppendMore, 2);
            pageGrid.Children.Add(btnAppendMore);

            Button btnNextPage = new Button()
            {
                Content = "SONRAKİ SAYFA ▶",
                Height = 24,
                Padding = new Thickness(10, 0, 10, 0),
                Background = HexBrush("#251D38"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnNextPage, 4);
            btnNextPage.Click += delegate
            {
                if (_activeSiteCategoryId == -1 && string.IsNullOrWhiteSpace(_txtStoreSearch.Text))
                {
                    _activeSiteCategoryId = -20;
                    _currentStorePage = 1;
                    HighlightStoreCategoryButtons();
                }
                else
                {
                    _currentStorePage++;
                }
                TriggerLiveSiteSearchOrCategoryFetch(false);
            };
            Grid.SetColumn(btnNextPage, 3);
            pageGrid.Children.Add(btnNextPage);

            // Row 3: Scrollable Store Cards Grid
            ScrollViewer storeScroll = new ScrollViewer()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(storeScroll, 3);
            _viewStore.Children.Add(storeScroll);

            _storeRowsPanel = new StackPanel();
            storeScroll.Content = _storeRowsPanel;
            _storeScrollViewer = storeScroll;
        }

        private void HighlightStoreCategoryButtons()
        {
            if (_storeCatWrap == null) return;
            foreach (UIElement el in _storeCatWrap.Children)
            {
                Button b = el as Button;
                if (b == null) continue;
                int id = (b.Tag is int) ? (int)b.Tag : -99;
                bool active = (id == _activeSiteCategoryId);
                b.Background = active ? ThemeManager.AccentBlueBrush : ThemeManager.SidebarNavy;
                b.Foreground = active ? Brushes.White : ThemeManager.ForegroundSubBrush;
                b.BorderBrush = active ? ThemeManager.AccentTealBrush : HexBrush("#2D2345");
            }
        }

        private int _storeFetchGeneration = 0;

        private void TriggerLiveSiteSearchOrCategoryFetch(bool appendMode = false)
        {
            int fetchGen = ++_storeFetchGeneration;
            _isFetchingLiveSite = true;

            string query = (_txtStoreSearch != null && _txtStoreSearch.Text != null) ? _txtStoreSearch.Text.Trim() : "";
            int catId = _activeSiteCategoryId;
            int page = _currentStorePage;

            if (_txtStoreCountBadge != null) _txtStoreCountBadge.Text = "⏳ Oyun kataloğu hazırlanıyor...";
            if (_txtStorePageIndicator != null)
                _txtStorePageIndicator.Text = string.Format("MAĞAZA YÜKLENİYOR (SAYFA {0})...", page);

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string msg;
                    int perSite = (catId == -20 ? 24 : 36);
                    List<GameProfile> liveGames = SiteParserService.FetchLiveSiteGames(catId, query, page, perSite, out msg);
                    if (liveGames == null) liveGames = new List<GameProfile>();

                    // İlk sayfada yerel katalogdaki uygun oyunları da havuza dahil et (daha zengin vitrin)
                    if (!appendMode && string.IsNullOrEmpty(query) && page == 1 && _allProfiles != null)
                    {
                        foreach (GameProfile lp in _allProfiles)
                        {
                            if (lp == null || string.IsNullOrWhiteSpace(lp.Name)) continue;
                            if (catId == -20)
                            {
                                liveGames.Add(lp);
                            }
                            else if (catId == 1538 && ((lp.Category ?? "").ToLowerInvariant().Contains("co-op") || (lp.MaxPlayers ?? "").ToLowerInvariant().Contains("co-op") || (lp.Description ?? "").ToLowerInvariant().Contains("co-op")))
                            {
                                liveGames.Add(lp);
                            }
                            else if (catId == -30 && System.Text.RegularExpressions.Regex.IsMatch((lp.Name + " " + lp.Category + " " + lp.Description).ToLowerInvariant(), @"\b(football|soccer|fifa|fc 24|fc 25|efootball|pes|nba|sport)\b"))
                            {
                                liveGames.Add(lp);
                            }
                            else if (catId == -31 && System.Text.RegularExpressions.Regex.IsMatch((lp.Name + " " + lp.Category + " " + lp.Description).ToLowerInvariant(), @"\b(racing|forza|need for speed|nfs|assetto|f1|dirt|rally|drift|car|euro truck|beamng)\b"))
                            {
                                liveGames.Add(lp);
                            }
                            else if (catId == -32 && System.Text.RegularExpressions.Regex.IsMatch((lp.Name + " " + lp.Category + " " + lp.Description).ToLowerInvariant(), @"\b(moto|motogp|ride|motocross|motorcycle|superbike|mx)\b"))
                            {
                                liveGames.Add(lp);
                            }
                        }
                    }

                    if (liveGames.Count > 0)
                    {
                        liveGames = GameDeduplicationEngine.Deduplicate(liveGames);

                        Dictionary<string, GameProfile> localByNorm = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
                        Dictionary<string, GameProfile> localById = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
                        List<GameProfile> localSnapshot = (_allProfiles != null) ? new List<GameProfile>(_allProfiles) : new List<GameProfile>();
                        foreach (GameProfile existing in localSnapshot)
                        {
                            if (existing == null) continue;
                            string exNorm = GameDeduplicationEngine.NormalizeGameTitle(existing.Name);
                            if (!string.IsNullOrEmpty(exNorm) && !localByNorm.ContainsKey(exNorm))
                                localByNorm[exNorm] = existing;
                            if (!string.IsNullOrEmpty(existing.Id) && !localById.ContainsKey(existing.Id))
                                localById[existing.Id] = existing;
                        }

                        foreach (GameProfile lg in liveGames)
                        {
                            if (lg == null) continue;
                            GameProfile match = null;
                            string lgNorm = GameDeduplicationEngine.NormalizeGameTitle(lg.Name);
                            if (!string.IsNullOrEmpty(lgNorm)) localByNorm.TryGetValue(lgNorm, out match);
                            if (match == null && !string.IsNullOrEmpty(lg.Id)) localById.TryGetValue(lg.Id, out match);
                            if (match != null)
                            {
                                lg.ExecutablePath = match.ExecutablePath;
                                lg.WorkingDirectory = match.WorkingDirectory;
                                lg.IsFavorite = match.IsFavorite;
                                lg.PlayTimeMinutes = match.PlayTimeMinutes;
                                if (!string.IsNullOrEmpty(match.BannerUrl))
                                    lg.BannerUrl = match.BannerUrl;
                            }
                            lg.Category = SiteParserService.SanitizeDisplayCategory(lg.Category);
                        }
                    }

                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            if (fetchGen != _storeFetchGeneration) return;
                            _isFetchingLiveSite = false;
                            if (liveGames != null && liveGames.Count > 0)
                            {
                                if (appendMode && _storeFilteredProfiles != null && _storeFilteredProfiles.Count > 0)
                                {
                                    List<GameProfile> combined = new List<GameProfile>(_storeFilteredProfiles);
                                    combined.AddRange(liveGames);
                                    _storeFilteredProfiles = GameDeduplicationEngine.Deduplicate(combined);
                                }
                                else
                                {
                                    _storeFilteredProfiles = liveGames;
                                }

                                if (_txtStoreCountBadge != null) _txtStoreCountBadge.Text = msg;
                                if (_txtStorePageIndicator != null)
                                    _txtStorePageIndicator.Text = string.Format("SAYFA {0} • TOPLAM {1} BENZERSİZ OYUN LİSTELENDİ", page, _storeFilteredProfiles.Count);
                                if (_txtBottomStatus != null) _txtBottomStatus.Text = msg;
                                RefreshStorefrontGrid();
                            }
                            else
                            {
                                if (_txtStoreCountBadge != null) _txtStoreCountBadge.Text = msg;
                                if (_txtStorePageIndicator != null) _txtStorePageIndicator.Text = "Sonuç bulunamadı veya sayfa sonuna ulaşıldı.";
                            }
                        }
                        catch (Exception ex2)
                        {
                            try { if (_txtBottomStatus != null) _txtBottomStatus.Text = "Mağaza yükleme hatası: " + ex2.Message; } catch { }
                        }
                    }));
                }
                catch (Exception ex)
                {
                    if (fetchGen == _storeFetchGeneration) _isFetchingLiveSite = false;
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            try { if (_txtBottomStatus != null) _txtBottomStatus.Text = "Mağaza yükleme başlatılamadı: " + ex.Message; } catch { }
                        }));
                    }
                    catch { }
                }
            });
        }

        /// <summary>
        /// Seçili oyunu qBittorrent üzerinden indirir: sayfadan magnet/.torrent çıkarır,
        /// ilerlemeyi gösterir, bitince Defender ile tarayıp kuruluma devreder.
        /// </summary>


        /// <summary>Co-op bağlantı teşhisi: NAT/UPnP/public IP/Steam durumu tek ekranda.</summary>


        /// <summary>
        /// TOPLU KATALOG: bağlı sitelerin ilk N sayfasını tarar ve tüm oyunları kapaklarıyla ekler.
        /// </summary>
        /// <summary>Test/otomasyon: toplu katalog aktarımını arayüzsüz çalıştırır ve log yazar.</summary>




        /// <summary>URL yapıştırarak oyun ekleme: sayfadan isim/sürüm/boyut/AppID/kapak çekilir.</summary>


        /// <summary>İndirme ekranındaki kompakt araç butonu.</summary>
        private Button CreateUtilityButtonSmall(string text, string tooltip)
        {
            Button btn = new Button()
            {
                Content = text,
                Height = 28,
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(0, 0, 6, 6),
                Background = HexBrush("#1E172C"),
                Foreground = HexBrush("#C7D5E0"),
                BorderBrush = ThemeManager.BorderAccentBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.4,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
            ApplyRoundedButtonTemplate(btn, 3);
            return btn;
        }

        private int _storeRenderGeneration = 0;
        private HashSet<string> _libIdCache = null;
        private HashSet<string> _libNormCache = null;

        private void InvalidateLibraryLookupCache()
        {
            _libIdCache = null;
            _libNormCache = null;
        }

        private void EnsureLibraryLookupCache()
        {
            if (_libIdCache != null && _libNormCache != null) return;
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> norms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_allProfiles != null)
            {
                foreach (GameProfile ex in _allProfiles)
                {
                    if (ex == null) continue;
                    if (!string.IsNullOrEmpty(ex.Id)) ids.Add(ex.Id);
                    string n = GameDeduplicationEngine.NormalizeGameTitle(ex.Name);
                    if (!string.IsNullOrEmpty(n)) norms.Add(n);
                }
            }
            _libIdCache = ids;
            _libNormCache = norms;
        }

        private void RefreshStorefrontGrid()
        {
            if (_storeRowsPanel == null) return;
            if (_storeFilteredProfiles == null) _storeFilteredProfiles = new List<GameProfile>();

            InvalidateLibraryLookupCache();
            EnsureLibraryLookupCache();

            _storeRowsPanel.Children.Clear();
            try { if (_storeScrollViewer != null) _storeScrollViewer.ScrollToTop(); } catch { }

            int totalCount = _storeFilteredProfiles.Count;
            int renderCount = Math.Min(totalCount, _storeDisplayLimit);
            int cols = (this.ActualWidth >= 1560) ? 4 : 3;

            // Incremental rendering: add rows in batches to keep UI responsive
            int generation = ++_storeRenderGeneration;
            int index = 0;
            const int rowsPerBatch = 2; // render 2 rows at a time, then yield

            Action renderBatch = null;
            renderBatch = delegate
            {
                if (generation != _storeRenderGeneration) return; // cancelled by newer refresh
                if (_storeRowsPanel == null) return;

                int rowsAdded = 0;
                while (index < renderCount && rowsAdded < rowsPerBatch)
                {
                    try
                    {
                        UniformGrid rowGrid = new UniformGrid() { Columns = cols, Margin = new Thickness(0, 0, 0, 12) };
                        for (int c = 0; c < cols; c++)
                        {
                            int idx = index + c;
                            if (idx < renderCount && idx < _storeFilteredProfiles.Count)
                            {
                                try { rowGrid.Children.Add(CreateSteamStoreGameCard(_storeFilteredProfiles[idx])); }
                                catch { rowGrid.Children.Add(new Grid()); }
                            }
                            else
                            {
                                rowGrid.Children.Add(new Grid());
                            }
                        }
                        _storeRowsPanel.Children.Add(rowGrid);
                    }
                    catch { }
                    index += cols;
                    rowsAdded++;
                }

                if (index < renderCount)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, renderBatch);
                }
                else if (totalCount > renderCount)
                {
                    try
                    {
                        Button btnMore = new Button()
                        {
                            Content = string.Format("▼ DAHA FAZLA GÖSTER (+{0}) — {1} oyun daha", Math.Min(90, totalCount - renderCount), totalCount - renderCount),
                            Height = 34,
                            Margin = new Thickness(0, 6, 0, 12),
                            Background = HexBrush("#1E172C"),
                            Foreground = ThemeManager.AccentTealBrush,
                            BorderBrush = ThemeManager.BorderAccentBrush,
                            BorderThickness = new Thickness(1),
                            FontSize = 10.5,
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand
                        };
                        ApplyRoundedButtonTemplate(btnMore, 3);
                        btnMore.Click += delegate
                        {
                            _storeDisplayLimit += 90;
                            RefreshStorefrontGrid();
                        };
                        _storeRowsPanel.Children.Add(btnMore);
                    }
                    catch { }
                }
            };

            // Start rendering after current frame
            Dispatcher.BeginInvoke(DispatcherPriority.Background, renderBatch);
        }

        private Border CreateSteamStoreGameCard(GameProfile gp)
        {
            Border card = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = gp.IsInstalled ? ThemeManager.AccentGreenBrush : HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 10, 0)
            };

            StackPanel cardStack = new StackPanel();
            card.Child = cardStack;

            // 16:9 Steam Store Header Image
            Border imgHeader = new Border()
            {
                Height = 128,
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Background = ThemeManager.BorderSubtleBrush,
                Cursor = Cursors.Hand
            };
            // Yer tutucu: afiş indirilemezse siyah kutu yerine gradient + oyun adı görünür.
            Grid imgLayer = new Grid();
            Grid placeholder = new Grid();
            placeholder.Children.Add(new Border() { Background = MakeGradient("#1B2838", "#0E141B") });

            // Afişi olmayan oyunlar için tasarlanmış kapak: monogram + isim + kaynak etiketi.
            string initials = BuildInitials(gp.Name);
            placeholder.Children.Add(new TextBlock()
            {
                Text = initials,
                Foreground = ThemeManager.BorderAccentBrush,
                FontSize = 64,
                FontWeight = FontWeights.ExtraBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.5,
                Margin = new Thickness(0, -20, 0, 0)
            });
            placeholder.Children.Add(new TextBlock()
            {
                Text = gp.Name,
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 46, 16, 4),
                Opacity = 0.92
            });
            imgLayer.Children.Add(placeholder);

            bool artworkFallbackTried = false;
            Action trySteamArtworkFallback = delegate
            {
                if (artworkFallbackTried) return;
                artworkFallbackTried = true;

                string cachedArt = SteamArtworkResolver.GetCached(gp.Name);
                if (!string.IsNullOrEmpty(cachedArt) && !string.Equals(cachedArt, gp.BannerUrl, StringComparison.OrdinalIgnoreCase))
                {
                    gp.BannerUrl = cachedArt;
                    ImageCacheService.BindImageAsync(imgHeader, gp, 360, 1.0, delegate(bool loaded2)
                    {
                        if (loaded2) placeholder.Visibility = Visibility.Collapsed;
                    });
                }
                else
                {
                    SteamArtworkResolver.ResolveAsync(gp.Name, delegate(string artUrl)
                    {
                        if (string.IsNullOrEmpty(artUrl)) return;
                        gp.BannerUrl = artUrl;
                        if (imgHeader.Dispatcher.HasShutdownStarted) return;
                        imgHeader.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            ImageCacheService.BindImageAsync(imgHeader, gp, 360, 1.0, delegate(bool loaded3)
                            {
                                if (loaded3) placeholder.Visibility = Visibility.Collapsed;
                            });
                        }));
                    });
                }
            };

            if (string.IsNullOrEmpty(gp.BannerUrl) && string.IsNullOrEmpty(gp.CapsuleUrl))
            {
                trySteamArtworkFallback();
            }
            else
            {
                ImageCacheService.BindImageAsync(imgHeader, gp, 360, 1.0, delegate(bool loaded)
                {
                    if (loaded)
                        placeholder.Visibility = Visibility.Collapsed;
                    else
                        trySteamArtworkFallback();
                });
            }

            Grid overlayBadges = new Grid() { Margin = new Thickness(8) };
            Border sizeBadge = new Border()
            {
                Background = HexBrush("#D90E141B"),
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            string sizeText = (gp.FileSize ?? "").Trim();
            bool showSize = sizeText.Length > 0 &&
                            !sizeText.Equals("Hazır Paket", StringComparison.OrdinalIgnoreCase) &&
                            !sizeText.Equals("Hazır", StringComparison.OrdinalIgnoreCase) &&
                            !sizeText.Equals("Kurulu", StringComparison.OrdinalIgnoreCase) &&
                            !sizeText.Equals("Bulut", StringComparison.OrdinalIgnoreCase);
            sizeBadge.Child = new TextBlock()
            {
                Text = sizeText,
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 9.5,
                FontWeight = FontWeights.ExtraBold
            };
            sizeBadge.Visibility = showSize ? Visibility.Visible : Visibility.Collapsed;
            overlayBadges.Children.Add(sizeBadge);

            Border statusBadge = new Border()
            {
                Background = gp.IsInstalled ? HexBrush("#E616301B") : HexBrush("#D90E141B"),
                BorderBrush = gp.IsInstalled ? ThemeManager.AccentGreenBrush : HexBrush("#3D4B61"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            statusBadge.Child = new TextBlock()
            {
                Text = gp.IsInstalled ? "✔ KÜTÜPHANEDE YÜKLÜ" : SiteParserService.SanitizeDisplayCategory(gp.Category),
                Foreground = gp.IsInstalled ? ThemeManager.AccentGreenBrush : ThemeManager.ForegroundMainBrush,
                FontSize = 9,
                FontWeight = FontWeights.Bold
            };
            overlayBadges.Children.Add(statusBadge);
            imgLayer.Children.Add(overlayBadges);
            imgHeader.Child = imgLayer;

            GameProfile captured = gp;
            imgHeader.MouseLeftButtonUp += delegate
            {
                GameProfile canonical = EnsureGameInLibraryCatalog(captured) ?? captured;
                SelectGameProfile(canonical);
                SwitchView(_viewLibrary, _navBtnLibrary);
            };
            cardStack.Children.Add(imgHeader);

            // Card Info Body
            StackPanel body = new StackPanel() { Margin = new Thickness(12, 10, 12, 12) };
            cardStack.Children.Add(body);

            body.Children.Add(new TextBlock()
            {
                Text = gp.Name,
                Foreground = Brushes.White,
                FontSize = 13.5,
                FontWeight = FontWeights.ExtraBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            body.Children.Add(new TextBlock()
            {
                Text = gp.Version + "  •  " + gp.MaxPlayers,
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 6),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            body.Children.Add(new TextBlock()
            {
                Text = gp.Description,
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 10.5,
                Height = 32,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Single Action Row: [ İNDİR / OYNA ] + [ ＋ / ✔ ] + [ ♡ / ★ ]
            Grid cardActionGrid = new Grid() { Margin = new Thickness(0, 2, 0, 0) };
            cardActionGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            if (!gp.IsInstalled)
            {
                cardActionGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                cardActionGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            }
            else
            {
                cardActionGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            }
            body.Children.Add(cardActionGrid);

            string cardBtnText = gp.IsInstalled
                ? (gp.UpdateAvailable ? "🔄 GÜNCELLE" : "▶ OYNA")
                : "⬇ İNDİR";
            Brush cardBtnBg = gp.IsInstalled
                ? (gp.UpdateAvailable ? (Brush)MakeGradient("#7E22CE", "#0369A1") : (Brush)MakeGradient("#5DBA2F", "#46941E"))
                : (Brush)MakeGradient("#9333EA", "#6B21A8");
            Brush cardBtnBorder = gp.IsInstalled
                ? (gp.UpdateAvailable ? ThemeManager.AccentTealBrush : HexBrush("#6ED43B"))
                : ThemeManager.AccentTealBrush;

            Button btnPrimary = new Button()
            {
                Content = cardBtnText,
                Height = 30,
                Background = cardBtnBg,
                Foreground = Brushes.White,
                BorderBrush = cardBtnBorder,
                BorderThickness = new Thickness(1),
                FontSize = 10.2,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnPrimary, 4);
            btnPrimary.Click += delegate
            {
                GameProfile canonical = EnsureGameInLibraryCatalog(captured) ?? captured;
                SelectGameProfile(canonical);
                SwitchView(_viewLibrary, _navBtnLibrary);
                if (canonical.IsInstalled)
                    BtnHeroPrimary_Click(null, null);
                else
                {
                    string chosenDir = GameInstallLocationDialog.PromptInstallDirectory(this, canonical);
                    if (string.IsNullOrEmpty(chosenDir)) return;

                    DownloadInstallService.SetTargetInstallDirectoryForGame(canonical, chosenDir);
                    _txtInAppDlTitle.Text = canonical.Name + " — Doğrudan İndirme ve Otomatik Kurulum Başlatıldı...";
                    _txtInAppDlSpeed.Text = "Bağlanıyor...";
                    _pbInAppDl.Value = 5;
                    _inAppDlStripBorder.Visibility = Visibility.Visible;
                    _downloader.StartInAppAutomatedPipeline(canonical);
                }
            };
            Grid.SetColumn(btnPrimary, 0);
            cardActionGrid.Children.Add(btnPrimary);

            if (!gp.IsInstalled)
            {
                bool alreadyInLibrary = IsGameInLibrary(gp);
                Button btnAddLib = new Button()
                {
                    Content = alreadyInLibrary ? "✔" : "＋",
                    Width = 32,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    Background = HexBrush("#1E172C"),
                    Foreground = alreadyInLibrary ? ThemeManager.AccentGreenBrush : HexBrush("#C7D5E0"),
                    BorderBrush = alreadyInLibrary ? ThemeManager.AccentGreenBrush : ThemeManager.BorderPrimaryBrush,
                    BorderThickness = new Thickness(1),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    ToolTip = alreadyInLibrary
                        ? "Bu oyun kütüphanenizde."
                        : "Kütüphaneye Ekle (indirme başlatmaz)"
                };
                ApplyRoundedButtonTemplate(btnAddLib, 4);
                btnAddLib.IsEnabled = !alreadyInLibrary;
                btnAddLib.Click += delegate
                {
                    GameProfile canonical = EnsureGameInLibraryCatalog(captured);
                    if (canonical != null)
                    {
                        _txtBottomStatus.Text = "✔ '" + canonical.Name + "' kütüphanenize eklendi.";
                        Win32.PlayTechChirp(true);
                        InvalidateLibraryLookupCache();
                        btnAddLib.Content = "✔";
                        btnAddLib.Foreground = ThemeManager.AccentGreenBrush;
                        btnAddLib.BorderBrush = ThemeManager.AccentGreenBrush;
                        btnAddLib.IsEnabled = false;
                        RefreshLibrarySidebarList();
                        RefreshLibrarySummaryMetrics();
                    }
                };
                Grid.SetColumn(btnAddLib, 1);
                cardActionGrid.Children.Add(btnAddLib);

                bool wished = Wishlist.IsWishlisted(gp);
                Button btnWish = new Button()
                {
                    Content = wished ? "★" : "♡",
                    Width = 32,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    Background = HexBrush("#1E172C"),
                    Foreground = wished ? ThemeManager.AccentTealBrush : ThemeManager.ForegroundMutedBrush,
                    BorderBrush = ThemeManager.BorderPrimaryBrush,
                    BorderThickness = new Thickness(1),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    ToolTip = "Takip listesine ekle/çıkar"
                };
                ApplyRoundedButtonTemplate(btnWish, 4);
                btnWish.Click += delegate
                {
                    bool added = Wishlist.Toggle(gp);
                    _txtBottomStatus.Text = (added ? "★ Takibe alındı: " : "♡ Takipten çıkarıldı: ") + gp.Name;
                    Win32.PlayTechChirp(added);
                    btnWish.Content = added ? "★" : "♡";
                    btnWish.Foreground = added ? ThemeManager.AccentTealBrush : ThemeManager.ForegroundMutedBrush;
                };
                Grid.SetColumn(btnWish, 2);
                cardActionGrid.Children.Add(btnWish);
            }
            else
            {
                Button btnFav = new Button()
                {
                    Content = gp.IsFavorite ? "★" : "☆",
                    Width = 32,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    Background = HexBrush("#1E172C"),
                    Foreground = gp.IsFavorite ? ThemeManager.AccentTealBrush : HexBrush("#C7D5E0"),
                    BorderBrush = ThemeManager.BorderPrimaryBrush,
                    BorderThickness = new Thickness(1),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    ToolTip = gp.IsFavorite ? "Favorilerden çıkar" : "Favorilere ekle"
                };
                ApplyRoundedButtonTemplate(btnFav, 4);
                btnFav.Click += delegate
                {
                    gp.IsFavorite = !gp.IsFavorite;
                    foreach (GameProfile p in _allProfiles)
                    {
                        if (GameDeduplicationEngine.NormalizeGameTitle(p.Name) == GameDeduplicationEngine.NormalizeGameTitle(gp.Name))
                        { p.IsFavorite = gp.IsFavorite; break; }
                    }
                    ProfileStore.SaveProfiles(_allProfiles);
                    btnFav.Content = gp.IsFavorite ? "★" : "☆";
                    btnFav.Foreground = gp.IsFavorite ? ThemeManager.AccentTealBrush : HexBrush("#C7D5E0");
                    RefreshLibrarySidebarList();
                };
                Grid.SetColumn(btnFav, 1);
                cardActionGrid.Children.Add(btnFav);
            }

            // --- Hover: donanım hızlandırmalı kenar & arka plan vurgusu (gölge shader maliyeti yok) ---
            card.MouseEnter += delegate
            {
                card.BorderBrush = ThemeManager.AccentTealBrush;
                card.Background = HexBrush("#211838");
            };
            card.MouseLeave += delegate
            {
                card.BorderBrush = gp.IsInstalled ? ThemeManager.AccentGreenBrush : HexBrush("#2D2345");
                card.Background = ThemeManager.SidebarNavy;
            };

            return card;
        }

        /// <summary>Bu oyun zaten kütüphanede mi (klasörde olmasa bile kayıtlı mı)?</summary>
        /// <summary>Afişsiz kartlarda kullanılan monogram (ör. "Lethal Company" -> "LC").</summary>
        private static string BuildInitials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            string[] words = name.Split(new char[] { ' ', '-', '_', ':', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            string result = "";
            foreach (string w in words)
            {
                if (w.Length == 0 || !char.IsLetter(w[0])) continue;
                result += char.ToUpperInvariant(w[0]);
                if (result.Length >= 2) break;
            }
            if (result.Length == 0) return "?";
            return result;
        }

        private bool IsGameInLibrary(GameProfile gp)
        {
            if (gp == null) return false;
            EnsureLibraryLookupCache();
            if (!string.IsNullOrEmpty(gp.Id) && _libIdCache.Contains(gp.Id)) return true;
            string key = GameDeduplicationEngine.NormalizeGameTitle(gp.Name);
            return !string.IsNullOrEmpty(key) && _libNormCache.Contains(key);
        }

        private GameProfile EnsureGameInLibraryCatalog(GameProfile gp)
        {
            if (gp == null) return null;
            string newKey = GameDeduplicationEngine.NormalizeGameTitle(gp.Name);
            foreach (GameProfile ex in _allProfiles)
            {
                string exKey = GameDeduplicationEngine.NormalizeGameTitle(ex.Name);
                if (exKey == newKey || string.Equals(ex.Id, gp.Id, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(ex.DirectMirrorUrl) && !string.IsNullOrEmpty(gp.DirectMirrorUrl))
                        ex.DirectMirrorUrl = gp.DirectMirrorUrl;
                    return ex;
                }
            }
            _allProfiles.Insert(0, gp);
            _allProfiles = GameDeduplicationEngine.Deduplicate(_allProfiles);
            ProfileStore.SaveProfiles(_allProfiles);
            ApplyLibraryFilter();
            RefreshLibrarySummaryMetrics();
            return gp;
        }

        // =========================================================================
        // VIEW 3: STEAM İNDİRME YÖNETİCİSİ & ÇOKLU KUYRUK MOTORU
        // =========================================================================
        private void BuildDownloadCenterView(Grid host)
        {
            _viewDownload = new Grid() { Margin = new Thickness(20), Visibility = Visibility.Collapsed };
            host.Children.Add(_viewDownload);

            ScrollViewer scroll = new ScrollViewer() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _viewDownload.Children.Add(scroll);

            StackPanel mainStack = new StackPanel();
            scroll.Content = mainStack;

            // Card 1: Aktif İndirme ve Hızlandırıcı Kartı
            Border card = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(22),
                Margin = new Thickness(0, 0, 0, 16)
            };
            mainStack.Children.Add(card);

            StackPanel stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(new TextBlock()
            {
                Text = "STEAM // 4X PARÇALI İNDİRME & OTOMATİK KURULUM MOTORU",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 15,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            stack.Children.Add(new TextBlock()
            {
                Text = "PixelDrain, Fileditch, GoFile, Buzzheavier ve doğrudan .RAR/.ZIP bağlantıları 4 kanallı HTTP Range hızlandırıcısıyla indirilir, SHA-256 doğrulamasından geçirilir ve Spacewar (AppID 480) ile otomatik kurulur.",
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            Grid urlRow = new Grid() { Margin = new Thickness(0, 0, 0, 12) };
            urlRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            urlRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            stack.Children.Add(urlRow);

            _txtDirectUrlInput = new TextBox()
            {
                Height = 34,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                FontSize = 11.5,
                Padding = new Thickness(10, 6, 10, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtDirectUrlInput, 0);
            urlRow.Children.Add(_txtDirectUrlInput);

            Button btnStartUrlDl = new Button()
            {
                Content = "⬇ DOĞRUDAN İNDİR VE KUR",
                Height = 34,
                Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Background = MakeGradient("#9333EA", "#6B21A8"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                FontSize = 11,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnStartUrlDl, 3);
            btnStartUrlDl.Click += delegate
            {
                if (_selectedProfile == null) return;
                if (!_selectedProfile.IsInstalled)
                {
                    string chosenDir = GameInstallLocationDialog.PromptInstallDirectory(this, _selectedProfile);
                    if (string.IsNullOrEmpty(chosenDir)) return;
                    DownloadInstallService.SetTargetInstallDirectoryForGame(_selectedProfile, chosenDir);
                }
                string u = _txtDirectUrlInput.Text.Trim();
                if (string.IsNullOrEmpty(u))
                    _downloader.StartInAppAutomatedPipeline(_selectedProfile);
                else
                    _downloader.DownloadAndInstallAsync(_selectedProfile, u);
            };
            Grid.SetColumn(btnStartUrlDl, 1);
            urlRow.Children.Add(btnStartUrlDl);

            _pbDownload = new ProgressBar()
            {
                Height = 14,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(_pbDownload);

            Grid dlStatRow = new Grid() { Margin = new Thickness(0, 0, 0, 14) };
            dlStatRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            dlStatRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            stack.Children.Add(dlStatRow);

            _txtDownloadStatus = new TextBlock()
            {
                Text = "İndirme kuyruğu hazır.",
                Foreground = ThemeManager.ForegroundMainBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(_txtDownloadStatus, 0);
            dlStatRow.Children.Add(_txtDownloadStatus);

            _txtDownloadSpeed = new TextBlock()
            {
                Text = "0.0 MB/sn",
                Foreground = ThemeManager.AccentGreenBrush,
                FontSize = 12,
                FontWeight = FontWeights.ExtraBold
            };
            Grid.SetColumn(_txtDownloadSpeed, 1);
            dlStatRow.Children.Add(_txtDownloadSpeed);

            // Action Toolbar (Pause / Resume / Cancel / Folder / Rate Limits)
            WrapPanel actionToolbar = new WrapPanel() { Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(actionToolbar);

            _btnPauseResumeDl = new Button()
            {
                Content = "⏸ DURAKLAT",
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 6),
                Background = HexBrush("#D97706"),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(_btnPauseResumeDl, 3);
            _btnPauseResumeDl.Click += delegate
            {
                if (DownloadInstallService.IsPaused)
                {
                    DownloadInstallService.ResumeDownload();
                    _btnPauseResumeDl.Content = "⏸ DURAKLAT";
                    _btnPauseResumeDl.Background = HexBrush("#D97706");
                    _txtDownloadStatus.Text = "İndirme devam ettiriliyor...";
                }
                else
                {
                    DownloadInstallService.PauseDownload();
                    _btnPauseResumeDl.Content = "▶ DEVAM ET";
                    _btnPauseResumeDl.Background = ThemeManager.AccentGreenBrush;
                    _txtDownloadStatus.Text = "⏸ İndirme duraklatıldı (Kaldığı byte korundu).";
                    _txtDownloadSpeed.Text = "0.0 MB/sn (Duraklatıldı)";
                }
            };
            actionToolbar.Children.Add(_btnPauseResumeDl);

            Button btnCancelDl = new Button()
            {
                Content = "⏹ İPTAL (KISMI KORU)",
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 6),
                Background = HexBrush("#DC2626"),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand,
                ToolTip = "İndirmeyi durdurur ama indirilen kısmı silmez. Tekrar OYNA'ya basınca kaldığı yerden devam eder."
            };
            ApplyRoundedButtonTemplate(btnCancelDl, 3);
            btnCancelDl.Click += delegate
            {
                _downloader.CancelDownload();
                if (_selectedProfile != null && DownloadInstallService.HasResumableDownload(_selectedProfile))
                {
                    _txtDownloadStatus.Text = "⏸ İptal edildi — indirilen kısım diskte korundu. Tekrar OYNA'ya basınca kaldığı yerden devam eder.";
                }
                else
                {
                    _txtDownloadStatus.Text = "Aktif indirme iptal edildi (kısmi dosya korundu).";
                }
                _pbDownload.Value = 0;
                _txtDownloadSpeed.Text = "0.0 MB/sn";
                _btnPauseResumeDl.Content = "⏸ DURAKLAT";
                _btnPauseResumeDl.Background = HexBrush("#D97706");
            };
            // Torrent ile indirme (qBittorrent WebUI): 403 hostlara takılmayan, hash doğrulamalı yol
            Button btnTorrentDl = new Button()
            {
                Content = "🧲 TORRENT İLE İNDİR",
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 6),
                Background = HexBrush("#1E172C"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderAccentBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "qBittorrent kuruluysa oyunun magnet/.torrent bağlantısıyla indirir. Seed kapatılır, arşiv Defender ile taranır."
            };
            ApplyRoundedButtonTemplate(btnTorrentDl, 3);
            btnTorrentDl.Click += delegate { StartTorrentDownloadForSelected(); };
            actionToolbar.Children.Add(btnTorrentDl);
            Button btnManualLink = CreateUtilityButtonSmall("🔗 MANUEL LİNK", "Doğrudan arşiv linki veya magnet yapıştır, indir+kur");
            btnManualLink.Click += delegate { RunManualLinkInstall(); };
            actionToolbar.Children.Add(btnManualLink);

            Button btnUninstall = CreateUtilityButtonSmall("🗑 KALDIR", "Seçili oyunu diskten ve katalogdan kaldır");
            btnUninstall.Click += delegate { RunUninstallSelected(); };
            actionToolbar.Children.Add(btnUninstall);

            Button btnDisk = CreateUtilityButtonSmall("💾 DİSK", "Disk kullanım raporu ve geçici dosya temizliği");
            btnDisk.Click += delegate { RunDiskReport(); };
            actionToolbar.Children.Add(btnDisk);

            Button btnSpeed = CreateUtilityButtonSmall("⏱ BANT", "İndirme hız sınırı: sınırsız / 5 / 10 / 25 / 50 MB/sn");
            btnSpeed.Click += delegate { CycleDownloadSpeedLimit(); };
            actionToolbar.Children.Add(btnSpeed);

            Button btnSched = CreateUtilityButtonSmall("🕒 ZAMANLA", "İndirmeyi N dakika sonra başlat");
            btnSched.Click += delegate { RunScheduledDownload(); };
            actionToolbar.Children.Add(btnSched);


            actionToolbar.Children.Add(btnCancelDl);

            Button btnOpenFolder = new Button()
            {
                Content = "📂 İNDİRİLENLER KLASÖRÜ",
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 6),
                Background = HexBrush("#221B33"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = HexBrush("#A855F7"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnOpenFolder, 3);
            btnOpenFolder.Click += delegate
            {
                try
                {
                    string target = @"D:\Games";
                    if (!Directory.Exists(target)) target = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    Process.Start("explorer.exe", target);
                }
                catch { }
            };
            actionToolbar.Children.Add(btnOpenFolder);

            Button btnClearQueue = new Button()
            {
                Content = "🗑 SIRAYI TEMİZLE",
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 12, 6),
                Background = HexBrush("#334155"),
                Foreground = HexBrush("#CBD5E1"),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnClearQueue, 3);
            btnClearQueue.Click += delegate
            {
                DownloadInstallService.ClearQueue();
                RefreshDownloadQueueUI();
            };
            actionToolbar.Children.Add(btnClearQueue);

            // Speed Limit Quick Chips
            Border speedLimitWrap = new Border()
            {
                Background = ThemeManager.BorderSubtleBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 0, 6)
            };
            StackPanel spLimitStack = new StackPanel() { Orientation = Orientation.Horizontal };
            speedLimitWrap.Child = spLimitStack;

            spLimitStack.Children.Add(new TextBlock()
            {
                Text = "⚡ HIZ LİMİTİ: ",
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0)
            });

            string[] limits = new string[] { "Sınırsız", "50 MB/s", "20 MB/s", "10 MB/s" };
            long[] limitBytes = new long[] { 0L, 52428800L, 20971520L, 10485760L };
            for (int i = 0; i < limits.Length; i++)
            {
                long val = limitBytes[i];
                Button bLim = new Button()
                {
                    Content = limits[i],
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(2, 0, 2, 0),
                    Background = (val == DownloadInstallService.SpeedLimitBytesPerSec) ? ThemeManager.AccentBlueBrush : HexBrush("#221B33"),
                    Foreground = Brushes.White,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                ApplyRoundedButtonTemplate(bLim, 2);
                bLim.Click += delegate
                {
                    DownloadInstallService.SpeedLimitBytesPerSec = val;
                    _txtDownloadStatus.Text = (val == 0) ? "⚡ Hız limiti kaldırıldı (Sınırsız Turbo Mod)." : string.Format("⚡ İndirme hızı {0} ile sınırlandı.", bLim.Content);
                };
                spLimitStack.Children.Add(bLim);
            }
            actionToolbar.Children.Add(speedLimitWrap);

            // Card 2: Kuyruk Listesi Kartı (Queue List Panel)
            Border queueCard = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 20)
            };
            mainStack.Children.Add(queueCard);

            StackPanel qStack = new StackPanel();
            queueCard.Child = qStack;

            Grid qHeader = new Grid() { Margin = new Thickness(0, 0, 0, 12) };
            qHeader.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            qHeader.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            qStack.Children.Add(qHeader);

            qHeader.Children.Add(new TextBlock()
            {
                Text = "📋 İNDİRME KUYRUĞU & SIRADAKİ OYUNLAR",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 13.5,
                FontWeight = FontWeights.ExtraBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            _txtQueueCountBadge = new TextBlock()
            {
                Text = "0 Oyun Bekliyor",
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtQueueCountBadge, 1);
            qHeader.Children.Add(_txtQueueCountBadge);

            _queueItemsStack = new StackPanel();
            qStack.Children.Add(_queueItemsStack);

            RefreshDownloadQueueUI();

            DownloadInstallService.OnQueueChanged += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    RefreshDownloadQueueUI();
                }));
            };
        }

        private void RefreshDownloadQueueUI()
        {
            if (_queueItemsStack == null) return;
            _queueItemsStack.Children.Clear();

            List<GameProfile> queued = DownloadInstallService.GetQueuedGames();
            if (_txtQueueCountBadge != null)
            {
                _txtQueueCountBadge.Text = string.Format("{0} Oyun Bekliyor", queued.Count);
            }

            if (queued.Count == 0)
            {
                Border emptyCard = new Border()
                {
                    Background = ThemeManager.BorderSubtleBrush,
                    BorderBrush = ThemeManager.DarkCardBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                StackPanel spEmpty = new StackPanel();
                spEmpty.Children.Add(new TextBlock()
                {
                    Text = "🟢 İndirme kuyruğu boş.",
                    Foreground = ThemeManager.AccentGreenBrush,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                spEmpty.Children.Add(new TextBlock()
                {
                    Text = "Tüm Site veya Kütüphane sekmesinden dilediğiniz oyunun 'GÜVENLİ İNDİR' butonuna basarak aynı anda birden fazla oyunu sıraya koyabilirsiniz. Mevcut indirme tamamlandığında sıradaki oyun kaldığı yerden sıfır kayıpla otomatik başlar.",
                    Foreground = ThemeManager.ForegroundMutedBrush,
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap
                });
                emptyCard.Child = spEmpty;
                _queueItemsStack.Children.Add(emptyCard);
                return;
            }

            for (int i = 0; i < queued.Count; i++)
            {
                GameProfile gp = queued[i];
                int rank = i + 1;

                Border row = new Border()
                {
                    Background = ThemeManager.BorderSubtleBrush,
                    BorderBrush = ThemeManager.BorderPrimaryBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                Grid rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto }); // Index
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); // Info
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto }); // Actions
                row.Child = rowGrid;

                // Index Pill
                Border pill = new Border()
                {
                    Background = (rank == 1) ? ThemeManager.AccentBlueBrush : HexBrush("#221B33"),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                pill.Child = new TextBlock()
                {
                    Text = "#" + rank,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.ExtraBold
                };
                Grid.SetColumn(pill, 0);
                rowGrid.Children.Add(pill);

                // Info Stack
                StackPanel info = new StackPanel() { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock()
                {
                    Text = gp.Name,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                });
                info.Children.Add(new TextBlock()
                {
                    Text = string.Format("• Boyut: {0} • Kategori: {1} • Durum: ⏳ Sırada Bekliyor", gp.FileSize ?? "Hazır", gp.Category ?? "Co-Op"),
                    Foreground = ThemeManager.ForegroundMutedBrush,
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                Grid.SetColumn(info, 1);
                rowGrid.Children.Add(info);

                // Action Buttons
                StackPanel acts = new StackPanel() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                
                if (rank > 1)
                {
                    Button btnTop = new Button()
                    {
                        Content = "⬆ ÖNCELİK VER",
                        Height = 26,
                        Padding = new Thickness(10, 0, 10, 0),
                        Margin = new Thickness(0, 0, 6, 0),
                        Background = HexBrush("#221B33"),
                        Foreground = ThemeManager.AccentTealBrush,
                        BorderBrush = HexBrush("#A855F7"),
                        BorderThickness = new Thickness(1),
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };
                    ApplyRoundedButtonTemplate(btnTop, 2);
                    string targetId = gp.Id;
                    btnTop.Click += delegate
                    {
                        DownloadInstallService.PrioritizeInQueue(targetId);
                        RefreshDownloadQueueUI();
                    };
                    acts.Children.Add(btnTop);
                }

                Button btnRemove = new Button()
                {
                    Content = "❌ KALDIR",
                    Height = 26,
                    Padding = new Thickness(10, 0, 10, 0),
                    Background = HexBrush("#7F1D1D"),
                    Foreground = HexBrush("#FCA5A5"),
                    BorderBrush = HexBrush("#DC2626"),
                    BorderThickness = new Thickness(1),
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                ApplyRoundedButtonTemplate(btnRemove, 2);
                string remId = gp.Id;
                btnRemove.Click += delegate
                {
                    DownloadInstallService.RemoveFromQueue(remId);
                    RefreshDownloadQueueUI();
                };
                acts.Children.Add(btnRemove);

                Grid.SetColumn(acts, 2);
                rowGrid.Children.Add(acts);

                _queueItemsStack.Children.Add(row);
            }
        }


        // =========================================================================
        // VIEW 4: ZERO-VPN P2P TÜNEL & LAN ODA TARAYICISI
        // =========================================================================
        private void BuildZeroVpnCenterView(Grid host)
        {
            _viewZeroVpn = new Grid() { Margin = new Thickness(20), Visibility = Visibility.Collapsed };
            host.Children.Add(_viewZeroVpn);

            ScrollViewer scroll = new ScrollViewer() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _viewZeroVpn.Children.Add(scroll);

            StackPanel mainStack = new StackPanel();
            scroll.Content = mainStack;

            Border card = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 14)
            };
            mainStack.Children.Add(card);

            StackPanel stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(new TextBlock()
            {
                Text = "🌐 ZERO-VPN P2P TÜNEL & RFC 5389 STUN NAT ÇÖZÜMLEME",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 15,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            stack.Children.Add(new TextBlock()
            {
                Text = "Radmin VPN veya Hamachi kurmadan Google STUN üzerinden dış IP ve portunuzu çözümleyin, UDP Hole-Punching ile arkadaşınızın bilgisayarına doğrudan bağlanın.",
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            Grid stunRow = new Grid() { Margin = new Thickness(0, 0, 0, 12) };
            stunRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            stunRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            stack.Children.Add(stunRow);

            _txtMyStunIpPort = new TextBox()
            {
                Text = ZeroVpnTunnelService.LastResolvedEndpoint ?? "STUN Adresi Çözümleniyor...",
                Height = 32,
                IsReadOnly = true,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = ThemeManager.AccentGreenBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(10, 5, 10, 5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtMyStunIpPort, 0);
            stunRow.Children.Add(_txtMyStunIpPort);

            Button btnStunScan = new Button()
            {
                Content = "🔄 STUN ADRESİMİ ÇÖZÜMLE",
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Background = ThemeManager.DarkCardBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnStunScan, 3);
            btnStunScan.Click += delegate
            {
                StunNatResult res = ZeroVpnTunnelService.DiscoverPublicEndpoint();
                if (res.Success)
                {
                    _txtMyStunIpPort.Text = res.PublicIp + ":" + res.PublicPort;
                    string natInfo = string.IsNullOrEmpty(res.NatType) ? "" : "  |  NAT: " + res.NatType;
                    _txtBottomStatus.Text = "STUN Genel IP:Port Çözümlendi -> " + _txtMyStunIpPort.Text + natInfo;
                    if (_txtTunnelLiveStatus != null)
                        _txtTunnelLiveStatus.Text = "NAT Durumu: " + (res.NatType ?? "Bilinmiyor") + " (Yerel Soket: " + res.LocalBoundPort + ")";
                    Win32.PlayTechChirp(true);
                }
                else
                {
                    _txtBottomStatus.Text = "STUN başarısız: " + (res.ErrorMessage ?? "bağlantı yok");
                    Win32.PlayTechChirp(false);
                }
            };
            Grid.SetColumn(btnStunScan, 1);
            stunRow.Children.Add(btnStunScan);

            Grid peerRow = new Grid() { Margin = new Thickness(0, 0, 0, 10) };
            peerRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            peerRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            stack.Children.Add(peerRow);

            _txtPeerTargetInput = new TextBox()
            {
                Text = "",
                Height = 32,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                FontSize = 12,
                Padding = new Thickness(10, 5, 10, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Arkadaşınızın STUN IP:Port adresini girin (Örn: 85.100.20.10:47889)"
            };
            Grid.SetColumn(_txtPeerTargetInput, 0);
            peerRow.Children.Add(_txtPeerTargetInput);

            Button btnStartTunnel = new Button()
            {
                Content = "🔗 UDP HOLE-PUNCH TÜNELİ BAŞLAT",
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Background = MakeGradient("#5DBA2F", "#46941E"),
                Foreground = Brushes.White,
                BorderBrush = HexBrush("#6ED43B"),
                FontSize = 10,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnStartTunnel, 3);
            btnStartTunnel.Click += delegate
            {
                string status;
                bool ok = ZeroVpnTunnelService.StartHolePunchTunnel(_txtPeerTargetInput.Text, 27015, out status);
                _txtTunnelLiveStatus.Text = status;
                _txtBottomStatus.Text = status;
                Win32.PlayTechChirp(ok);
            };
            Grid.SetColumn(btnStartTunnel, 1);
            peerRow.Children.Add(btnStartTunnel);

            _txtTunnelLiveStatus = new TextBlock()
            {
                Text = "Tünel Durumu: Hazır",
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(_txtTunnelLiveStatus);

            Button btnBroadcastLan = new Button()
            {
                Content = "📡 YEREL AĞDA (LAN / GOLDBERG) ODA YAYINLA",
                Height = 32,
                Background = ThemeManager.DarkCardBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 10)
            };
            ApplyRoundedButtonTemplate(btnBroadcastLan, 3);
            btnBroadcastLan.Click += delegate
            {
                string gName = _selectedProfile != null ? _selectedProfile.Name : "Steam Co-Op";
                string appId = _selectedProfile != null ? _selectedProfile.SteamAppId : "480";
                SteamEmulatorBridge.BroadcastRoom(Environment.UserName, gName, appId);
                _txtBottomStatus.Text = "LAN Odası yayınlandı: " + gName;
                Win32.PlayTechChirp(true);
            };
            stack.Children.Add(btnBroadcastLan);

            _lanRoomsContainer = new StackPanel();
            stack.Children.Add(_lanRoomsContainer);
        }

        // =========================================================================
        // VIEW 5: MODLAR, BEPINEX & OYUN MOTORU SDK YÖNETİCİSİ
        // =========================================================================
        private void BuildModManagerCenterView(Grid host)
        {
            _viewModManager = new Grid() { Margin = new Thickness(20), Visibility = Visibility.Collapsed };
            host.Children.Add(_viewModManager);

            ScrollViewer scroll = new ScrollViewer() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _viewModManager.Children.Add(scroll);

            StackPanel mainStack = new StackPanel();
            scroll.Content = mainStack;

            Border card = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(20)
            };
            mainStack.Children.Add(card);

            StackPanel stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(new TextBlock()
            {
                Text = "🧩 OYUN MOTORU SDK, AOB İMZA VERİTABANI & BEPINEX MOD YÖNETİCİSİ",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 15,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _txtEngineInspectSummary = new TextBlock()
            {
                Text = "Seçili Oyun Motoru Analiz Ediliyor...",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            stack.Children.Add(_txtEngineInspectSummary);

            _txtSigDbStatusText = new TextBlock()
            {
                Text = "AOB İmza Veritabanı: signatures_v7.cfg aktif",
                Foreground = ThemeManager.AccentGreenBrush,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 14)
            };
            stack.Children.Add(_txtSigDbStatusText);

            StackPanel btnRow = new StackPanel() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            stack.Children.Add(btnRow);

            Button btnDeployBep = new Button()
            {
                Content = "🚀 TEK TIKLA BEPINEX & 32-KİŞİLİK CO-OP KUR",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 10, 0),
                Background = MakeGradient("#9333EA", "#6B21A8"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                FontSize = 10.5,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnDeployBep, 3);
            btnDeployBep.Click += delegate
            {
                if (_selectedProfile == null) return;
                bool ok = ModLoaderService.InitializeModFrameworkStructure(_selectedProfile);
                _txtBottomStatus.Text = ok
                    ? ("BepInEx & 32 Kişilik Co-Op Genişletici '" + _selectedProfile.Name + "' dizinine kuruldu!")
                    : "Oyun kurulu değil veya Unity motoru kullanmıyor.";
                RefreshInstalledModsList(_modsListContainer, _selectedProfile);
                Win32.PlayTechChirp(ok);
            };
            btnRow.Children.Add(btnDeployBep);

            Button btnAddModZip = new Button()
            {
                Content = "📂 MOD PAKETİ (.ZIP / .DLL) YÜKLE",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Background = HexBrush("#241C36"),
                Foreground = ThemeManager.ForegroundMainBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnAddModZip, 3);
            btnAddModZip.Click += delegate
            {
                if (_selectedProfile == null) return;
                Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
                ofd.Filter = "Mod Paketi (*.dll;*.zip)|*.dll;*.zip";
                if (ofd.ShowDialog() == true)
                {
                    string status;
                    bool ok = ModLoaderService.InstallModArchive(_selectedProfile, ofd.FileName, out status);
                    _txtBottomStatus.Text = status;
                    RefreshInstalledModsList(_modsListContainer, _selectedProfile);
                    Win32.PlayTechChirp(ok);
                }
            };
            btnRow.Children.Add(btnAddModZip);

            _modsListContainer = new StackPanel();
            stack.Children.Add(_modsListContainer);
        }

        private void UpdateModManagerSelectedGame(GameProfile gp)
        {
            if (gp == null || _txtEngineInspectSummary == null) return;
            try
            {
                EngineAnalysisResult eng = EngineInspectorService.InspectGameDirectory(gp);
                GameSignatureProfile sig = SignatureDatabaseService.GetSignatureProfile(gp.Id);
                _txtEngineInspectSummary.Text = string.Format("Seçili Oyun: {0}  |  Motor: {1} ({2})",
                    gp.Name, eng.EngineDisplayName, string.IsNullOrEmpty(eng.CoreModuleName) ? "x64" : eng.CoreModuleName);
                _txtSigDbStatusText.Text = sig != null
                    ? string.Format("✔ Bulut AOB İmzaları Hazır — Speed: [{0}] | Stamina: [{1}]", sig.SpeedPattern, sig.StaminaPattern)
                    : "✔ Genel Motor SDK Tarayıcısı (GWorld / TimeScale) Hazır";
                RefreshInstalledModsList(_modsListContainer, gp);
            }
            catch { }
        }

        private void RefreshInstalledModsList(StackPanel container, GameProfile gp)
        {
            if (container == null) return;
            container.Children.Clear();
            if (gp == null) return;

            List<GameModEntry> mods = ModLoaderService.GetInstalledMods(gp);
            if (mods.Count == 0)
            {
                container.Children.Add(new TextBlock()
                {
                    Text = "Henüz yüklü BepInEx eklentisi yok ('Tek Tıkla BepInEx Kur' veya 'Mod Paketi Yükle' kullanın).",
                    Foreground = ThemeManager.ForegroundMutedBrush,
                    FontSize = 11
                });
                return;
            }

            foreach (GameModEntry mod in mods)
            {
                Border b = new Border()
                {
                    Background = ThemeManager.BorderSubtleBrush,
                    BorderBrush = HexBrush("#2D2345"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                b.Child = g;

                TextBlock t = new TextBlock()
                {
                    Text = mod.ModName + " (" + mod.FileSizeText + ") — " + (mod.IsEnabled ? "AKTİF" : "DEVRE DIŞI"),
                    Foreground = mod.IsEnabled ? ThemeManager.AccentGreenBrush : ThemeManager.ForegroundMutedBrush,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(t, 0);
                g.Children.Add(t);

                Button btnToggle = new Button()
                {
                    Content = mod.IsEnabled ? "DEVRE DIŞI BIRAK" : "AKTİF ET",
                    Height = 25,
                    Padding = new Thickness(10, 0, 10, 0),
                    Background = HexBrush("#241C36"),
                    Foreground = Brushes.White,
                    BorderBrush = ThemeManager.BorderPrimaryBrush,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                ApplyRoundedButtonTemplate(btnToggle, 3);
                GameModEntry capturedMod = mod;
                btnToggle.Click += delegate
                {
                    ModLoaderService.ToggleModState(capturedMod);
                    RefreshInstalledModsList(container, gp);
                };
                Grid.SetColumn(btnToggle, 1);
                g.Children.Add(btnToggle);
                container.Children.Add(b);
            }
        }

        // =========================================================================
        // VIEW 6: TELEMETRİ HUD & TRAINER KONTROL MERKEZİ
        // =========================================================================
        private void BuildTelemetryCenterView(Grid host)
        {
            _viewTelemetry = new Grid() { Margin = new Thickness(20), Visibility = Visibility.Collapsed };
            host.Children.Add(_viewTelemetry);

            Border card = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(20)
            };
            _viewTelemetry.Children.Add(card);

            StackPanel stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(new TextBlock()
            {
                Text = "⚡ OYUN İÇİ BELLEK TELEMETRİSİ, AOB TARAYICI & OVERLAY HUD",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 15,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            Grid attachRow = new Grid() { Margin = new Thickness(0, 0, 0, 12) };
            attachRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            attachRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            stack.Children.Add(attachRow);

            _txtManualProcess = new TextBox()
            {
                Text = "REPO.exe",
                Height = 30,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                FontSize = 11.5,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtManualProcess, 0);
            attachRow.Children.Add(_txtManualProcess);

            Button btnAttach = new Button()
            {
                Content = "🎯 SÜRECE BAĞLAN & AOB TARA",
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Background = MakeGradient("#5DBA2F", "#46941E"),
                Foreground = Brushes.White,
                BorderBrush = HexBrush("#6ED43B"),
                FontSize = 10,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnAttach, 3);
            btnAttach.Click += delegate
            {
                _telemetry.ActiveGameProfile = _selectedProfile;
                _telemetry.Attach(_txtManualProcess.Text.Trim());
            };
            Grid.SetColumn(btnAttach, 1);
            attachRow.Children.Add(btnAttach);

            _chkQuickSpeed = CreateTelemetryCheckBox("[F1] Vektörel Hız Çarpanı (Speed Multiplier)");
            _chkQuickSpeed.Checked += delegate { _telemetry.ToggleSpeedVector(); };
            _chkQuickSpeed.Unchecked += delegate { _telemetry.ToggleSpeedVector(); };
            stack.Children.Add(_chkQuickSpeed);

            _chkQuickInfinite = CreateTelemetryCheckBox("[F2] Sınırsız Dayanıklılık, Kaynak & Oksijen Kilidi");
            _chkQuickInfinite.Checked += delegate { _telemetry.ToggleInfiniteResources(); };
            _chkQuickInfinite.Unchecked += delegate { _telemetry.ToggleInfiniteResources(); };
            stack.Children.Add(_chkQuickInfinite);

            _chkQuickNoClip = CreateTelemetryCheckBox("[F3] Koordinat Sabitleme & No-Clip Modu");
            _chkQuickNoClip.Checked += delegate { _telemetry.ToggleCoordinateLock(); };
            _chkQuickNoClip.Unchecked += delegate { _telemetry.ToggleCoordinateLock(); };
            stack.Children.Add(_chkQuickNoClip);

            _chkQuickOverlay = CreateTelemetryCheckBox("[F5] Oyun Üstü Şeffaf DirectX / GDI+ HUD Katmanı Göster");
            _chkQuickOverlay.Checked += delegate { if (_overlay != null) _overlay.Show(); };
            _chkQuickOverlay.Unchecked += delegate { if (_overlay != null) _overlay.Hide(); };
            stack.Children.Add(_chkQuickOverlay);

            StackPanel sliderRow = new StackPanel() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
            sliderRow.Children.Add(new TextBlock() { Text = "Hız Çarpanı: ", Foreground = ThemeManager.ForegroundSubBrush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            _sliderSpeed = new Slider() { Minimum = 1, Maximum = 10, Value = 2, Width = 220, Margin = new Thickness(8, 0, 8, 0) };
            _txtSpeedVal = new TextBlock() { Text = "2.0x", Foreground = ThemeManager.AccentTealBrush, FontSize = 11.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            _sliderSpeed.ValueChanged += delegate
            {
                if (_telemetry != null)
                {
                    _telemetry.SpeedMultiplier = (float)Math.Round(_sliderSpeed.Value, 1);
                    if (_txtSpeedVal != null) _txtSpeedVal.Text = _telemetry.SpeedMultiplier.ToString("0.0") + "x";
                }
            };
            sliderRow.Children.Add(_sliderSpeed);
            sliderRow.Children.Add(_txtSpeedVal);
            stack.Children.Add(sliderRow);

            // v9.0 Enterprise Low-Level Audit & In-Game Translation Toolbar
            UniformGrid v9AuditRow = new UniformGrid() { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
            stack.Children.Add(v9AuditRow);

            Button btnSyscallAudit = new Button()
            {
                Content = "🛡️ SYSCALL SSN & HALO'S GATE DENETLE",
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnSyscallAudit, 3);
            btnSyscallAudit.Click += delegate
            {
                string rep = InjectionSyscallEngine.AuditSyscallAndGadgetTable();
                _txtLogConsole.AppendText(rep + "\r\n");
                _txtLogConsole.ScrollToEnd();
                _txtBottomStatus.Text = rep;
                Win32.PlayTechChirp(true);
            };
            v9AuditRow.Children.Add(btnSyscallAudit);

            Button btnPeIatAudit = new Button()
            {
                Content = "🔍 PE/IAT & 4GB LAA ONAR",
                Height = 30,
                Margin = new Thickness(4, 0, 4, 0),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentGreenBrush,
                BorderBrush = ThemeManager.AccentGreenBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnPeIatAudit, 3);
            btnPeIatAudit.Click += delegate
            {
                PeBinaryReport rep = DependencyRepairEngine.InspectAndRepairGameBinary(_selectedProfile);
                _txtLogConsole.AppendText(rep.SummaryText + "\r\n");
                if (rep.ImportedDlls.Count > 0)
                    _txtLogConsole.AppendText("[IAT BAĞIMLILIKLARI] " + string.Join(", ", rep.ImportedDlls.ToArray()) + "\r\n");
                _txtLogConsole.ScrollToEnd();
                _txtBottomStatus.Text = rep.SummaryText;
                Win32.PlayTechChirp(rep.IsValidPe);
            };
            v9AuditRow.Children.Add(btnPeIatAudit);

            Button btnTestTrSubtitle = new Button()
            {
                Content = "🇹🇷 CANLI TÜRKÇE ALTYAZI TEST [F6]",
                Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                Background = HexBrush("#2E1065"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnTestTrSubtitle, 3);
            btnTestTrSubtitle.Click += delegate
            {
                LiveTranslatorHudService.IsEnabled = true;
                if (_overlay != null && !_overlay.Visible) _overlay.Show();
                LiveTranslatorHudService.TranslateDialogAsync("Warning: Oxygen levels dropping rapidly. Return to the lunar extraction pod immediately!");
                _txtLogConsole.AppendText("[CANLI ÇEVİRİ HUD] Test diyaloğu çevrildi ve In-Game Overlay altına yansıtıldı.\r\n");
                _txtLogConsole.ScrollToEnd();
                Win32.PlayTechChirp(true);
            };
            v9AuditRow.Children.Add(btnTestTrSubtitle);

            _txtLogConsole = new TextBox()
            {
                Height = 190,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = ThemeManager.AccentGreenBrush,
                BorderBrush = HexBrush("#2D2345"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Padding = new Thickness(10),
                Text = "[SİSTEM] Bellek Telemetri, AOB Tarayıcı ve 0.5ms Kernel Optimizer hazır.\r\n"
            };
            stack.Children.Add(_txtLogConsole);
        }

        private CheckBox CreateTelemetryCheckBox(string text)
        {
            return new CheckBox()
            {
                Content = text,
                Foreground = ThemeManager.ForegroundMainBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 7),
                Cursor = Cursors.Hand
            };
        }

        // =========================================================================
        // VIEW 7: OTA BULUT GÜNCELLEME & TAŞINABİLİR .ZIP PAKETLEYİCİ
        // =========================================================================
        private void BuildOtaShareCenterView(Grid host)
        {
            _viewOtaShare = new Grid() { Margin = new Thickness(20), Visibility = Visibility.Collapsed };
            host.Children.Add(_viewOtaShare);

            Border card = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(22)
            };
            _viewOtaShare.Children.Add(card);

            StackPanel stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(new TextBlock()
            {
                Text = "🔄 UZAKTAN TAM UYGULAMA (.EXE) & KATALOG OTO-GÜNCELLEME (OTA)",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 15,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _txtOtaPageStatus = new TextBlock()
            {
                Text = "🟢 Küresel Binary (.EXE) & Katalog OTA Kanalı Aktif — Uzak PC Oto-Kurulum Açık",
                Foreground = ThemeManager.AccentGreenBrush,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 14)
            };
            stack.Children.Add(_txtOtaPageStatus);

            Button btnPushBinaryOta = new Button()
            {
                Content = "🚀 TÜM UZAK BİLGİSAYARLARA TAM UYGULAMA (.EXE) & KATALOG GÜNCELLEMESİ GÖNDER",
                Height = 42,
                Margin = new Thickness(0, 0, 0, 10),
                Background = MakeGradient("#9333EA", "#6B21A8"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnPushBinaryOta, 4);
            btnPushBinaryOta.Click += delegate { _autoUpdater.PushFullApplicationBinaryToRemotePCsAsync(_allProfiles); };
            stack.Children.Add(btnPushBinaryOta);

            Button btnCheckOtaNow = new Button()
            {
                Content = "🔍 BULUT GÜNCELLEMELERİNİ ŞİMDİ DENETLE",
                Height = 36,
                Margin = new Thickness(0, 0, 0, 10),
                Background = HexBrush("#241C36"),
                Foreground = ThemeManager.ForegroundMainBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnCheckOtaNow, 4);
            btnCheckOtaNow.Click += delegate { _autoUpdater.CheckForUpdatesAsync(_allProfiles, true); };
            stack.Children.Add(btnCheckOtaNow);

            Button btnBuildZip = new Button()
            {
                Content = "📦 MASAÜSTÜNDE TAŞINABİLİR .ZIP PAKETİ OLUŞTUR (TitanGameHub_Portable.zip)",
                Height = 40,
                Background = MakeGradient("#5DBA2F", "#46941E"),
                Foreground = Brushes.White,
                BorderBrush = HexBrush("#6ED43B"),
                FontSize = 11.5,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnBuildZip, 4);
            btnBuildZip.Click += delegate { ExportPortableZipToDesktop(); };
            stack.Children.Add(btnBuildZip);
        }

        // =========================================================================
        // COLUMN 2: RIGHT STEAM FRIENDS & LIVE LOBBY CHAT SIDEBAR
        // =========================================================================
        private void BuildRightFriendsSidebar(Grid bodyGrid)
        {
            Border rightBorder = new Border()
            {
                Background = HexBrush("#151024"),
                BorderBrush = HexBrush("#2E234D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10)
            };
            Grid.SetColumn(rightBorder, 2);
            bodyGrid.Children.Add(rightBorder);

            Grid rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(150) });
            rightGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            rightBorder.Child = rightGrid;

                        StackPanel headerStack = new StackPanel() { Margin = new Thickness(0, 0, 0, 10) };
            Grid headerTitleRow = new Grid();
            headerTitleRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            headerTitleRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            headerStack.Children.Add(headerTitleRow);
            TextBlock txtHubTitle = new TextBlock() { Text = "ONLINE HUB", FontSize = 13, FontWeight = FontWeights.ExtraBold, Foreground = HexBrush("#EAF6FF") };
            Grid.SetColumn(txtHubTitle, 0);
            headerTitleRow.Children.Add(txtHubTitle);
            Border hubBadge = new Border() { Background = HexBrush("#133033"), BorderBrush = HexBrush("#1FA37A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
            hubBadge.Child = new TextBlock() { Text = "CANLI", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = HexBrush("#7CFFD1") };
            Grid.SetColumn(hubBadge, 1);
            headerTitleRow.Children.Add(hubBadge);
            headerStack.Children.Add(new TextBlock() { Text = "Arkadaşlar, lobi, davet ve sohbet — tek panelden yönet.", FontSize = 10, Foreground = HexBrush("#8FA3B8"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            _txtOnlineHubStatus = new TextBlock() { Text = "Durum: başlatılıyor...", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = HexBrush("#B7D8EA"), Margin = new Thickness(0, 5, 0, 0) };
            headerStack.Children.Add(_txtOnlineHubStatus);
            Grid.SetRow(headerStack, 0);
            rightGrid.Children.Add(headerStack);

            Border roomCard = new Border()
            {
                Background = HexBrush("#13101B"),
                BorderBrush = HexBrush("#3B2D59"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(roomCard, 1);
            rightGrid.Children.Add(roomCard);

            StackPanel roomStack = new StackPanel();
            roomCard.Child = roomStack;

            Grid nickRow = new Grid() { Margin = new Thickness(0, 0, 0, 4) };
            nickRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(58) });
            nickRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            roomStack.Children.Add(nickRow);
            nickRow.Children.Add(new TextBlock() { Text = "Oyuncu:", Foreground = ThemeManager.ForegroundMutedBrush, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            _txtUserNick = new TextBox()
            {
                Text = _cloudLobby != null ? _cloudLobby.UserName : Environment.UserName,
                Height = 22,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                FontSize = 10.5,
                Padding = new Thickness(4, 1, 4, 1),
                ToolTip = "Oyuncu adınızı buradan değiştirebilirsiniz (Odada ve arkadaşlarınızda otomatik güncellenir)"
            };
            _txtUserNick.LostFocus += delegate
            {
                string newNick = _txtUserNick.Text.Trim();
                if (!string.IsNullOrEmpty(newNick) && _cloudLobby != null && newNick != _cloudLobby.UserName)
                {
                    _cloudLobby.UserName = newNick;
                    _cloudLobby.SaveIdentity();
                    _cloudLobby.SendHeartbeat();
                    _txtBottomStatus.Text = "Oyuncu adınız güncellendi: " + newNick;
                    Win32.PlayTechChirp(true);
                }
            };
            Grid.SetColumn(_txtUserNick, 1);
            nickRow.Children.Add(_txtUserNick);

            Grid codeRow = new Grid() { Margin = new Thickness(0, 0, 0, 4) };
            codeRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(58) });
            codeRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            roomStack.Children.Add(codeRow);
            codeRow.Children.Add(new TextBlock() { Text = "Oda Kodu:", Foreground = ThemeManager.ForegroundMutedBrush, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            _txtRoomCode = new TextBox()
            {
                Text = "TITAN-TR",
                Height = 22,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(4, 1, 4, 1)
            };
            Grid.SetColumn(_txtRoomCode, 1);
            codeRow.Children.Add(_txtRoomCode);

            Grid myCodeRow = new Grid() { Margin = new Thickness(0, 0, 0, 6) };
            myCodeRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(58) });
            myCodeRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            myCodeRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            roomStack.Children.Add(myCodeRow);
            myCodeRow.Children.Add(new TextBlock() { Text = "Kodun:", Foreground = ThemeManager.ForegroundMutedBrush, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            TextBox txtMyCode = new TextBox()
            {
                Text = _cloudLobby.MyFriendCode ?? "TITAN-XXXXXX",
                IsReadOnly = true,
                Height = 22,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                FontSize = 10,
                FontWeight = FontWeights.ExtraBold,
                Padding = new Thickness(4, 1, 4, 1),
                ToolTip = "Senin benzersiz Titan ArkadaÅŸ Kodun (ArkadaÅŸÄ±na gÃ¶nder)"
            };
            Grid.SetColumn(txtMyCode, 1);
            myCodeRow.Children.Add(txtMyCode);

            Button btnCopyMyCode = new Button()
            {
                Content = "KOPYALA",
                Height = 22,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(4, 0, 0, 0),
                Background = HexBrush("#221B33"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = HexBrush("#A855F7"),
                BorderThickness = new Thickness(1),
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Arkadaş kodunu panoya kopyala"
            };
            ApplyRoundedButtonTemplate(btnCopyMyCode, 3);
            btnCopyMyCode.Click += delegate
            {
                try
                {
                    string inviteText = "Titan Game Hub'i aç, sağ üstteki 'TITAN Kodu veya Nick' alanına bu kodu yaz: " +
                                        _cloudLobby.MyFriendCode + " — sonra birlikte oynayabiliriz.";
                    Clipboard.SetText(inviteText);
                    _txtBottomStatus.Text = "Davet metni kopyalandı (" + _cloudLobby.MyFriendCode + "). Arkadaşına gönder, kodu alana yazsın.";
                    Win32.PlayTechChirp(true);
                }
                catch { }
            };
            // ===== AĞ TEŞHİSİ: NAT tipi / UPnP / public IP / Steam durumu =====
            Button btnNetDiag = new Button()
            {
                Content = "🌐 AĞ TEŞHİSİ (NAT / UPnP / PING)",
                Height = 26,
                Margin = new Thickness(0, 6, 0, 6),
                Background = HexBrush("#161220"),
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderAccentBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.4,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Co-op bağlanmıyorsa sebebini gösterir: NAT tipi, UPnP durumu, genel IP, Steam"
            };
            ApplyRoundedButtonTemplate(btnNetDiag, 3);
            btnNetDiag.Click += delegate { RunNetworkDiagnostics(); };
            roomStack.Children.Add(btnNetDiag);

            Grid.SetColumn(btnCopyMyCode, 2);
            myCodeRow.Children.Add(btnCopyMyCode);

            Button btnInviteRoom = new Button()
            {
                Content = "🎮 LOBİYE DAVET GÖNDER (480)",
                Height = 26,
                Background = MakeGradient("#5DBA2F", "#46941E"),
                Foreground = Brushes.White,
                BorderBrush = HexBrush("#6ED43B"),
                FontSize = 9.5,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnInviteRoom, 3);
            btnInviteRoom.Click += delegate
            {
                _cloudLobby.UserName = _txtUserNick.Text.Trim();
                _cloudLobby.RoomCode = _txtRoomCode.Text.Trim();
                string gName = _selectedProfile != null ? _selectedProfile.Name : "Steam Co-Op";
                _cloudLobby.SendLobbyInvite(gName);
                _txtBottomStatus.Text = gName + " için Spacewar P2P lobi daveti odaya gönderildi!";
            };
            roomStack.Children.Add(btnInviteRoom);

            Button btnSquadAutoSync = new Button()
            {
                Content = "⚡ TAKIMI EŞİTLE & P2P GÜNCELLE",
                Height = 26,
                Margin = new Thickness(0, 4, 0, 0),
                Background = MakeGradient("#8B5CF6", "#7C3AED"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                FontSize = 9.2,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand,
                ToolTip = "Arkadaşlarınla oyun sürümlerini karşılaştırır, sürüm uyuşmazlığı varsa kendi PC'nden 1 Gbps LAN/P2P ile güncellemeyi tek tıkla arkadaşına gönderir"
            };
            ApplyRoundedButtonTemplate(btnSquadAutoSync, 3);
            btnSquadAutoSync.Click += delegate
            {
                if (_selectedProfile == null)
                {
                    _txtBottomStatus.Text = "Lütfen önce kütüphaneden bir oyun seçin.";
                    return;
                }

                List<FriendPresence> friends = (_cloudLobby != null) ? _cloudLobby.GetLiveFriendsList() : new List<FriendPresence>();
                FleetSyncStatusReport rep = SquadFleetAutoSyncService.CheckSquadVersionParity(_selectedProfile, friends);

                if (rep.IsFullySynchronized)
                {
                    Win32.PlayTechChirp(true);
                    MessageBox.Show(rep.DiagnosticMessage, "Titan // Takım Sürüm Eşlik Radarı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBoxResult res = MessageBox.Show(
                        rep.DiagnosticMessage + "\n\nŞimdi tüm takıma 'Filo Güncelleme & 1 Gbps P2P Swarm' sinyali gönderilsin mi?",
                        "Titan // Takım Sürüm Eşitleme",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (res == MessageBoxResult.Yes)
                    {
                        SquadFleetAutoSyncService.BroadcastFleetP2pSyncAsync(_selectedProfile, delegate(string stat)
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                _txtBottomStatus.Text = stat;
                                Win32.PlayTechChirp(true);
                            }));
                        });
                    }
                }
            };
            roomStack.Children.Add(btnSquadAutoSync);

            // Row 2: Steam Quick Friend Addition Row
            Border addFriendBorder = new Border()
            {
                Background = HexBrush("#13101B"),
                BorderBrush = HexBrush("#3B2D59"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(addFriendBorder, 2);
            rightGrid.Children.Add(addFriendBorder);

            Grid addFriendGrid = new Grid();
            addFriendGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            addFriendGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            addFriendBorder.Child = addFriendGrid;

            TextBox txtNewFriendInput = new TextBox()
            {
                Text = "TITAN Kodu veya Nick...",
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = HexBrush("#64748B"),
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.8,
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                ToolTip = "Arkadaşınızın TITAN-XXXXXX Arkadaş Kodunu veya Kullanıcı Adını yazıp '➕ EKLE'ye basın"
            };
            txtNewFriendInput.GotFocus += delegate
            {
                if (txtNewFriendInput.Text == "TITAN Kodu veya Nick...")
                {
                    txtNewFriendInput.Text = "";
                    txtNewFriendInput.Foreground = Brushes.White;
                }
            };
            txtNewFriendInput.LostFocus += delegate
            {
                if (string.IsNullOrWhiteSpace(txtNewFriendInput.Text))
                {
                    txtNewFriendInput.Text = "TITAN Kodu veya Nick...";
                    txtNewFriendInput.Foreground = HexBrush("#64748B");
                }
            };
            Grid.SetColumn(txtNewFriendInput, 0);
            addFriendGrid.Children.Add(txtNewFriendInput);

            Button btnAddFriend = new Button()
            {
                Content = "➕ EKLE",
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(4, 0, 0, 0),
                Background = (Brush)MakeGradient("#9333EA", "#6B21A8"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnAddFriend, 3);
            btnAddFriend.Click += delegate
            {
                string rawInput = txtNewFriendInput.Text.Trim();
                if (rawInput == "TITAN Kodu veya Nick...") rawInput = "";
                string msg;
                if (_cloudLobby.AddFriend(rawInput, out msg))
                {
                    txtNewFriendInput.Text = "TITAN Kodu veya Nick...";
                    txtNewFriendInput.Foreground = HexBrush("#64748B");
                    _txtBottomStatus.Text = msg;
                    Win32.PlayTechChirp(true);
                }
                else
                {
                    _txtBottomStatus.Text = msg;
                }
            };
            Grid.SetColumn(btnAddFriend, 1);
            addFriendGrid.Children.Add(btnAddFriend);

            ScrollViewer friendsScroll = new ScrollViewer() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(friendsScroll, 3);
            rightGrid.Children.Add(friendsScroll);
            _friendsListStack = new StackPanel();
            friendsScroll.Content = _friendsListStack;

            Border chatBorder = new Border()
            {
                Background = HexBrush("#1A1526"),
                BorderBrush = HexBrush("#2E3A49"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 6)
            };
            DockPanel chatPanel = new DockPanel();
            Border chatHeader = new Border() { Background = HexBrush("#202838"), CornerRadius = new CornerRadius(6, 6, 0, 0), Padding = new Thickness(8, 5, 8, 5) };
            chatHeader.Child = new TextBlock() { Text = "LOBI SOHBETI", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = HexBrush("#9FD3FF") };
            DockPanel.SetDock(chatHeader, Dock.Top);
            chatPanel.Children.Add(chatHeader);
            _txtChatHistory = new TextBox()
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = HexBrush("#1A1526"),
                Foreground = HexBrush("#D2DAE3"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9.5,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8),
                Text = "[LOBI] Sohbet kanali hazir. Arkadaslarinla burada mesajlas.\n"
            };
            chatPanel.Children.Add(_txtChatHistory);
            chatBorder.Child = chatPanel;
            Grid.SetRow(chatBorder, 4);
            rightGrid.Children.Add(chatBorder);

            Grid chatInputRow = new Grid();
            chatInputRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            chatInputRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            Grid.SetRow(chatInputRow, 5);
            rightGrid.Children.Add(chatInputRow);

            _txtChatInput = new TextBox()
            {
                Height = 26,
                Background = HexBrush("#13101B"),
                Foreground = Brushes.White,
                BorderBrush = HexBrush("#3B2D59"),
                FontSize = 10.5,
                Padding = new Thickness(6, 3, 6, 3)
            };
            _txtChatInput.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) SendChatFromInput();
            };
            Grid.SetColumn(_txtChatInput, 0);
            chatInputRow.Children.Add(_txtChatInput);

            Button btnSendChat = new Button()
            {
                Content = "GÖNDER",
                Height = 26,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(4, 0, 0, 0),
                Background = ThemeManager.AccentBlueBrush,
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentBlueBrush,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnSendChat, 3);
            btnSendChat.Click += delegate { SendChatFromInput(); };
            Grid.SetColumn(btnSendChat, 1);
            chatInputRow.Children.Add(btnSendChat);
        }

        private void SendChatFromInput()
        {
            if (_txtChatInput == null) return;
            string txt = _txtChatInput.Text.Trim();
            if (string.IsNullOrEmpty(txt)) return;
            _cloudLobby.UserName = _txtUserNick.Text.Trim();
            _cloudLobby.RoomCode = _txtRoomCode.Text.Trim();
            _cloudLobby.SendChatMessage(txt);
            _txtChatInput.Text = "";
        }

        // =========================================================================
        // EVENT WIRING, LAUNCHER & HELPERS
        // =========================================================================
        private void WireEvents()
        {
            _telemetry.OnLogMessage += delegate(string msg)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (_txtLogConsole != null)
                    {
                        _txtLogConsole.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
                        _txtLogConsole.ScrollToEnd();
                    }
                    if (_txtBottomStatus != null)
                        _txtBottomStatus.Text = msg;
                }));
            };

            _downloader.OnProgressChanged += delegate(string status, int pct, string speed)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (_inAppDlStripBorder != null) _inAppDlStripBorder.Visibility = Visibility.Visible;
                    if (_txtInAppDlTitle != null) _txtInAppDlTitle.Text = status;
                    if (_txtInAppDlSpeed != null) _txtInAppDlSpeed.Text = speed;
                    if (_pbInAppDl != null) _pbInAppDl.Value = pct;

                    if (_txtDownloadStatus != null) _txtDownloadStatus.Text = status;
                    if (_txtDownloadSpeed != null) _txtDownloadSpeed.Text = speed;
                    if (_pbDownload != null) _pbDownload.Value = pct;
                    if (_txtBottomStatus != null) _txtBottomStatus.Text = status + " (" + speed + ")";
                }));
            };

            _downloader.OnInstallCompleted += delegate(GameProfile gp, bool ok, string msg)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (ok && gp != null)
                    {
                        foreach (GameProfile p in _allProfiles)
                        {
                            if (p.Id == gp.Id || GameDeduplicationEngine.NormalizeGameTitle(p.Name) == GameDeduplicationEngine.NormalizeGameTitle(gp.Name))
                            {
                                p.ExecutablePath = gp.ExecutablePath;
                                p.WorkingDirectory = gp.WorkingDirectory;
                                p.Version = gp.Version;
                                p.RemoteVersion = gp.Version;
                                break;
                            }
                        }
                    }
                    ProfileStore.SaveProfiles(_allProfiles);
                    try
                    {
                        DownloadHistoryService.Record(new DownloadHistoryEntry
                        {
                            GameId = gp != null ? (gp.Id ?? "") : "",
                            GameName = gp != null ? (gp.Name ?? "Oyun") : "Oyun",
                            Url = gp != null ? (gp.DirectMirrorUrl ?? gp.DownloadUrl ?? "") : "",
                            Host = gp != null && !string.IsNullOrEmpty(gp.DirectMirrorUrl) ? DownloadHostPolicy.HostLabel(gp.DirectMirrorUrl) : "In-App",
                            Status = ok ? "completed" : ((msg != null && msg.IndexOf("İptal", StringComparison.OrdinalIgnoreCase) >= 0) ? "cancelled" : "failed"),
                            Error = ok ? "" : (msg ?? ""),
                            FinishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    }
                    catch { }
                    if (ok && gp != null)
                    {
                        ShowSteamToast("✔ İndirme & Kurulum Tamamlandı", gp.Name + " oynamaya hazır!", NotificationType.Success);
                    }
                    if (!ok)
                    {
                        if (_txtInAppDlTitle != null) _txtInAppDlTitle.Text = "⚠️ " + msg;
                        if (_txtInAppDlSpeed != null) _txtInAppDlSpeed.Text = "Hata";
                        if (_pbInAppDl != null) _pbInAppDl.Value = 0;
                    }
                    if (_txtBottomStatus != null) _txtBottomStatus.Text = msg;
                    if (_selectedProfile != null && gp != null && (_selectedProfile.Id == gp.Id || GameDeduplicationEngine.NormalizeGameTitle(_selectedProfile.Name) == GameDeduplicationEngine.NormalizeGameTitle(gp.Name)))
                    {
                        _selectedProfile.ExecutablePath = gp.ExecutablePath;
                        _selectedProfile.WorkingDirectory = gp.WorkingDirectory;
                        SelectGameProfile(_selectedProfile);
                    }
                    RefreshLibrarySidebarList();
                    RefreshLibrarySummaryMetrics();
                    RefreshStorefrontGrid();
                    Win32.PlayTechChirp(ok);
                    if (ok) ProceduralSoundEngine.PlayDownloadComplete();

                    // Başarısız kurulumda bekleyen "kurulunca aç" niyeti temizlenir.
                    if (!ok && gp != null && InstallLaunchChain.HasPending)
                        InstallLaunchChain.CancelPending();

                    // ===== Steam tarzı tek basış zinciri: indirme/kurulum bitti -> oyunu aç =====
                    if (ok && gp != null && InstallLaunchChain.ConsumeIfPending(gp))
                    {
                        GameProfile launchTarget = _selectedProfile;
                        bool alreadyRunning = false;
                        try { alreadyRunning = SmartUpdateShieldService.IsGameProcessRunning(gp); }
                        catch { }

                        if (launchTarget != null && !alreadyRunning)
                        {
                            if (_txtBottomStatus != null)
                                _txtBottomStatus.Text = "🚀 " + gp.Name + " hazır! Otomatik başlatılıyor...";

                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                try { BtnHeroPrimary_Click(null, null); }
                                catch (Exception ex) { if (_txtBottomStatus != null) _txtBottomStatus.Text = "Otomatik başlatma hatası: " + ex.Message; }
                            }));
                        }
                        else if (alreadyRunning)
                        {
                            InstallLaunchChain.CancelPending();
                        }
                    }
                }));
            };

            _downloader.OnUpdateDetectedMidDownload += delegate(GameProfile gp, string oldVer, string newVer, string newUrl)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Win32.PlayTechChirp(true);
                    if (_txtInAppDlTitle != null)
                        _txtInAppDlTitle.Text = string.Format("⚠️ İNDİRME SIRASINDA YENİ SÜRÜM ÇIKTI! ({0} -> {1})", oldVer, newVer);
                    if (_txtBottomStatus != null)
                        _txtBottomStatus.Text = string.Format("🔄 Canlı Güncelleme Tespiti: {0} indirilirken sitede v{1} yayınlandı!", gp.Name, newVer);

                    MessageBoxResult res = MessageBox.Show(
                        string.Format("'{0}' oyunu indirilirken sunucuda daha yeni bir sürüm ({1} -> {2}) yayınlandı!\n\nEski sürümü indirmeye devam etmek yerine doğrudan EN SON YENİ SÜRÜME geçilsin mi?\n\n• Evet: İndirmeyi sıfırla ve yeni sürümü indir\n• Hayır: Mevcut indirmeye devam et",
                            gp.Name, oldVer, newVer),
                        "Titan Game Hub — İndirme Sırasında Canlı Güncelleme",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (res == MessageBoxResult.Yes)
                    {
                        _downloader.CancelDownload();
                        gp.Version = newVer;
                        gp.RemoteVersion = newVer;
                        if (!string.IsNullOrEmpty(newUrl)) gp.DirectMirrorUrl = newUrl;
                        ProfileStore.SaveProfiles(_allProfiles);
                        _downloader.StartInAppAutomatedPipeline(gp);
                    }
                }));
            };

            _autoUpdater.OnFleetCommandReceived += delegate(string title, string msg, GameProfile targetGp)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Win32.PlayTechChirp(true);
                    MessageBoxResult res = MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (res == MessageBoxResult.Yes && targetGp != null)
                    {
                        SelectGameProfile(targetGp);
                        _inAppDlStripBorder.Visibility = Visibility.Visible;
                        _downloader.StartInAppAutomatedPipeline(targetGp);
                    }
                }));
            };

            _downloader.OnSiteMirrorsDiscovered += delegate(GameProfile gp, SitePageAnalysis analysis)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (gp == null || _selectedProfile == null || _selectedProfile.Id != gp.Id) return;
                    if (_liveMirrorsWrapPanel == null) return;

                    _liveMirrorsWrapPanel.Children.Clear();

                    if (analysis != null && analysis.Success && analysis.Mirrors.Count > 0)
                    {
                        string bestHost = analysis.Mirrors[0].HostName;
                        _txtLiveMirrorsStatus.Text = string.Format("✔ {0} — Doğrulanmış en hızlı ve güvenli indirme sunucusu ({1}) otomatik seçildi ({2} • {3})",
                            gp.Name, bestHost, analysis.LiveFileSize ?? gp.FileSize, analysis.LiveVersion ?? gp.Version);

                        Button btnAutoSafeDownload = new Button()
                        {
                            Content = "⬇ TEK TIKLA GÜVENLİ İNDİR (OTOMATİK SUNUCU)",
                            Height = 32,
                            Padding = new Thickness(16, 0, 16, 0),
                            Margin = new Thickness(0, 0, 8, 6),
                            Background = (Brush)MakeGradient("#9333EA", "#6B21A8"),
                            Foreground = Brushes.White,
                            BorderBrush = ThemeManager.AccentTealBrush,
                            BorderThickness = new Thickness(1),
                            FontSize = 10.5,
                            FontWeight = FontWeights.ExtraBold,
                            Cursor = Cursors.Hand
                        };
                        ApplyRoundedButtonTemplate(btnAutoSafeDownload, 3);
                        btnAutoSafeDownload.Click += delegate
                        {
                            if (!gp.IsInstalled)
                            {
                                string chosenDir = GameInstallLocationDialog.PromptInstallDirectory(this, gp);
                                if (string.IsNullOrEmpty(chosenDir)) return;
                                DownloadInstallService.SetTargetInstallDirectoryForGame(gp, chosenDir);
                            }
                            _inAppDlStripBorder.Visibility = Visibility.Visible;
                            _downloader.StartInAppAutomatedPipeline(gp);
                        };
                        _liveMirrorsWrapPanel.Children.Add(btnAutoSafeDownload);

                        Border badgeSafe = new Border()
                        {
                            Background = HexBrush("#16281B"),
                            BorderBrush = ThemeManager.AccentGreenBrush,
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(3),
                            Padding = new Thickness(10, 6, 10, 6),
                            Margin = new Thickness(0, 0, 8, 6)
                        };
                        badgeSafe.Child = new TextBlock()
                        {
                            Text = "🛡️ VİRÜS KORUMASI & SHA-256 DOĞRULAMA AKTİF",
                            Foreground = ThemeManager.AccentGreenBrush,
                            FontSize = 9.8,
                            FontWeight = FontWeights.Bold
                        };
                        _liveMirrorsWrapPanel.Children.Add(badgeSafe);
                    }
                    else
                    {
                        _txtLiveMirrorsStatus.Text = gp.Name + " — Doğrudan güvenli indirme hattı hazır:";
                    }

                    if (!string.IsNullOrEmpty(gp.DownloadUrl))
                    {
                        Button btnPage = new Button()
                        {
                            Content = "🌐 STEAMRIP SAYFASI",
                            Height = 30,
                            Padding = new Thickness(12, 0, 12, 0),
                            Margin = new Thickness(0, 0, 8, 6),
                            Background = ThemeManager.DarkCardBrush,
                            Foreground = ThemeManager.ForegroundSubBrush,
                            BorderBrush = HexBrush("#374151"),
                            BorderThickness = new Thickness(1),
                            FontSize = 10,
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand
                        };
                        ApplyRoundedButtonTemplate(btnPage, 3);
                        btnPage.Click += delegate { MessageBox.Show("İndirme işlemi uygulama içi yapılıyor.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information); };
                        _liveMirrorsWrapPanel.Children.Add(btnPage);
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
                        ThreadPool.QueueUserWorkItem(delegate
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
                }));
            };

            _cloudLobby.OnFriendsUpdated += delegate(List<FriendPresence> list)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        if (_txtOnlineHubStatus != null)
                        {
                            int online = 0;
                            int total = 0;
                            if (list != null)
                            {
                                total = list.Count;
                                for (int i = 0; i < list.Count; i++)
                                {
                                    if (list[i] != null && list[i].IsOnline) online++;
                                }
                            }
                            bool running = _cloudLobby != null && _cloudLobby.IsRunning;
                            string myCode = _cloudLobby != null ? _cloudLobby.MyFriendCode : "";
                            _txtOnlineHubStatus.Text = "Durum: " + (running ? "Bagli" : "Beklemede") + "  |  " + online + "/" + total + " cevrimici  |  Kod: " + myCode;
                            _txtOnlineHubStatus.Foreground = HexBrush(running ? "#7CFFD1" : "#FFCC80");
                        }
                    }
                    catch { }
                    if (_friendsListStack == null) return;
                    _friendsListStack.Children.Clear();

                    if (list == null || list.Count == 0)
                    {
                                                Border emptyCard = new Border()
                        {
                            Background = HexBrush("#1E172C"),
                            BorderBrush = HexBrush("#3B2D59"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(10),
                            Margin = new Thickness(2, 8, 2, 4)
                        };
                        StackPanel emptySp = new StackPanel();
                        emptySp.Children.Add(new TextBlock() { Text = "Henuz arkadas yok", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = HexBrush("#D7E6F5"), HorizontalAlignment = HorizontalAlignment.Center });
                        emptySp.Children.Add(new TextBlock() { Text = "Yukaridan arkadas kodu girerek ekleyebilir veya davet gonderebilirsin.", Foreground = HexBrush("#7E93AA"), FontSize = 10, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
                        emptyCard.Child = emptySp;
                        _friendsListStack.Children.Add(emptyCard);
                        return;
                    }

                    foreach (FriendPresence fp in list)
                    {
                        FriendPresence friendRef = fp;
                        Border fb = new Border()
                        {
                            Background = HexBrush("#13101B"),
                            BorderBrush = friendRef.IsOnline ? ThemeManager.BorderAccentBrush : HexBrush("#222831"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(3),
                            Padding = new Thickness(6, 5, 6, 5),
                            Margin = new Thickness(0, 0, 0, 4)
                        };

                        Grid cardGrid = new Grid();
                        cardGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                        cardGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                        fb.Child = cardGrid;

                        StackPanel infoStack = new StackPanel() { VerticalAlignment = VerticalAlignment.Center };
                        Grid.SetColumn(infoStack, 0);
                        cardGrid.Children.Add(infoStack);

                        StackPanel nameRow = new StackPanel() { Orientation = Orientation.Horizontal };
                        nameRow.Children.Add(new TextBlock()
                        {
                            Text = friendRef.IsOnline ? "🟢 " : "⚪ ",
                            FontSize = 9,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        nameRow.Children.Add(new TextBlock()
                        {
                            Text = friendRef.UserName,
                            Foreground = friendRef.IsOnline ? ThemeManager.AccentGreenBrush : ThemeManager.ForegroundMutedBrush,
                            FontSize = 10.5,
                            FontWeight = FontWeights.Bold,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });

                        if (friendRef.IsOnline)
                        {
                            PeerPingResult pRes = PeerPingRadarService.GetPeerPing(friendRef.UserName);
                            Border pingPill = new Border()
                            {
                                Background = ThemeManager.BorderSubtleBrush,
                                BorderBrush = HexBrush(pRes.BadgeColorHex),
                                BorderThickness = new Thickness(0.8),
                                CornerRadius = new CornerRadius(2),
                                Padding = new Thickness(3, 0, 3, 0),
                                Margin = new Thickness(6, 0, 0, 0),
                                VerticalAlignment = VerticalAlignment.Center,
                                ToolTip = string.Format("Spacewar 480 P2P Gecikme: {0} ms ({1})", pRes.LatencyMs, pRes.RegionCode)
                            };
                            pingPill.Child = new TextBlock()
                            {
                                Text = pRes.BadgeText,
                                Foreground = HexBrush(pRes.BadgeColorHex),
                                FontSize = 8.0,
                                FontWeight = FontWeights.Bold
                            };
                            nameRow.Children.Add(pingPill);
                        }
                        infoStack.Children.Add(nameRow);

                        string statusText = friendRef.IsOnline
                            ? (!string.IsNullOrEmpty(friendRef.ActiveGame) ? friendRef.ActiveGame + " oynuyor" : friendRef.Status)
                            : "Çevrim Dışı";
                        infoStack.Children.Add(new TextBlock()
                        {
                            Text = statusText,
                            Foreground = friendRef.IsOnline ? ThemeManager.AccentTealBrush : HexBrush("#55606E"),
                            FontSize = 9.2,
                            Margin = new Thickness(0, 1, 0, 0),
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });

                        if (friendRef.IsOnline && !string.IsNullOrEmpty(friendRef.GameVersion))
                        {
                            bool isSameVer = (_selectedProfile != null && string.Equals(friendRef.GameVersion, _selectedProfile.Version, StringComparison.OrdinalIgnoreCase));
                            infoStack.Children.Add(new TextBlock()
                            {
                                Text = isSameVer ? ("v" + friendRef.GameVersion + " (Lobi Uyumlu ✔)") : ("v" + friendRef.GameVersion + " (Sürüm Farkı ⚠️)"),
                                Foreground = isSameVer ? ThemeManager.AccentGreenBrush : ThemeManager.AccentTealBrush,
                                FontSize = 8.5,
                                Margin = new Thickness(0, 1, 0, 0)
                            });
                        }

                        StackPanel actionStack = new StackPanel()
                        {
                            Orientation = Orientation.Horizontal,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(4, 0, 0, 0)
                        };
                        Grid.SetColumn(actionStack, 1);
                        cardGrid.Children.Add(actionStack);

                                                bool friendInGame = friendRef.IsOnline && !string.IsNullOrEmpty(friendRef.ActiveGame) && friendRef.ActiveGame != "Titan Hub";
                        Button btnInvite = new Button()
                        {
                            Content = friendInGame ? "🎮 KATIL" : "🎮 ÇAĞIR",
                            Height = 20,
                            Padding = new Thickness(5, 0, 5, 0),
                            Background = friendInGame ? (Brush)MakeGradient("#46941E", "#5DBA2F") : (Brush)MakeGradient("#9333EA", "#6B21A8"),
                            Foreground = Brushes.White,
                            BorderBrush = friendInGame ? ThemeManager.AccentGreenBrush : ThemeManager.AccentTealBrush,
                            BorderThickness = new Thickness(1),
                            FontSize = 8.2,
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand,
                            ToolTip = friendInGame ? ("Arkadaşının '" + friendRef.ActiveGame + "' lobisine anında katıl ve oyunu başlat") : "Arkadaşına Spacewar (AppID 480) lobi daveti gönderir"
                        };
                        ApplyRoundedButtonTemplate(btnInvite, 3);
                        btnInvite.Click += delegate
                        {
                            if (friendInGame)
                            {
                                // Direct join friend's game session
                                if (!string.IsNullOrEmpty(friendRef.RoomCode) && _txtRoomCode != null)
                                {
                                    _txtRoomCode.Text = friendRef.RoomCode;
                                    _cloudLobby.RoomCode = friendRef.RoomCode;
                                }

                                // Auto-start Zero-VPN P2P tunnel if friend shared a STUN endpoint
                                if (!string.IsNullOrEmpty(friendRef.LastKnownIp) && friendRef.LastKnownIp.Contains(":"))
                                {
                                    if (_txtPeerTargetInput != null)
                                        _txtPeerTargetInput.Text = friendRef.LastKnownIp;
                                    string tunnelStatus;
                                    if (ZeroVpnTunnelService.StartHolePunchTunnel(friendRef.LastKnownIp, 27015, out tunnelStatus))
                                    {
                                        if (_txtTunnelLiveStatus != null)
                                            _txtTunnelLiveStatus.Text = tunnelStatus;
                                    }
                                }

                                GameProfile targetGame = null;
                                string tKey = GameDeduplicationEngine.NormalizeGameTitle(friendRef.ActiveGame);
                                foreach (GameProfile gp in _allProfiles)
                                {
                                    if (GameDeduplicationEngine.NormalizeGameTitle(gp.Name) == tKey)
                                    {
                                        targetGame = gp;
                                        break;
                                    }
                                }

                                if (targetGame != null)
                                {
                                    if (!string.IsNullOrEmpty(friendRef.LastKnownIp))
                                        SteamEmulatorBridge.WriteCoopPeerEndpoint(targetGame, friendRef.LastKnownIp);
                                    SelectGameProfile(targetGame);
                                    SwitchView(_viewLibrary, _navBtnLibrary);
                                    if (targetGame.IsInstalled)
                                    {
                                        BtnHeroPrimary_Click(null, null);
                                        _txtBottomStatus.Text = "🟢 " + friendRef.UserName + " kullanıcısının '" + targetGame.Name + "' lobisine bağlanılıyor...";
                                    }
                                    else
                                    {
                                        string chosenDir = GameInstallLocationDialog.PromptInstallDirectory(this, targetGame);
                                        if (!string.IsNullOrEmpty(chosenDir))
                                        {
                                            DownloadInstallService.SetTargetInstallDirectoryForGame(targetGame, chosenDir);
                                            _txtInAppDlTitle.Text = targetGame.Name + " — Doğrudan İndirme ve Otomatik Kurulum Başlatıldı...";
                                            _txtInAppDlSpeed.Text = "Bağlanıyor...";
                                            _pbInAppDl.Value = 5;
                                            _inAppDlStripBorder.Visibility = Visibility.Visible;
                                            _downloader.StartInAppAutomatedPipeline(targetGame);
                                        }
                                    }
                                }
                                else
                                {
                                    try { Process.Start("steam://run/480//"); } catch { }
                                    _txtBottomStatus.Text = "Spacewar (480) başlatıldı, " + friendRef.UserName + " lobisine bağlanılıyor...";
                                }
                            }
                            else
                            {
                                string targetGame = (_selectedProfile != null && !string.IsNullOrEmpty(_selectedProfile.Name)) ? _selectedProfile.Name : "Spacewar (AppID 480)";
                                _cloudLobby.SendChatMessage("🎮 @" + friendRef.UserName + " seni lobiye davet etti! Oyun: " + targetGame);
                                _cloudLobby.SendLobbyInvite(targetGame);
                                _cloudLobby.SendDirectInvite(friendRef.FriendCode ?? friendRef.UserName, targetGame);
                                try { Clipboard.SetText("steam://run/480//"); } catch { }
                                _txtBottomStatus.Text = "🎮 " + friendRef.UserName + " kullanıcısına uzaktan Spacewar lobi çağrısı iletildi!";
                            }
                            Win32.PlayTechChirp(true);
                        };
                        actionStack.Children.Add(btnInvite);

                        Button btnDel = new Button()
                        {
                            Content = "✕",
                            Height = 20,
                            Width = 18,
                            Margin = new Thickness(3, 0, 0, 0),
                            Background = ThemeManager.DarkNavyBrush,
                            Foreground = HexBrush("#FF6B6B"),
                            BorderBrush = HexBrush("#6B2A32"),
                            BorderThickness = new Thickness(1),
                            FontSize = 9.5,
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand,
                            ToolTip = "Arkadaşı listeden kaldır"
                        };
                        ApplyRoundedButtonTemplate(btnDel, 3);
                        btnDel.Click += delegate
                        {
                            if (_cloudLobby.RemoveFriend(friendRef.UserName))
                            {
                                _txtBottomStatus.Text = friendRef.UserName + " arkadaş listenizden kaldırıldı.";
                                Win32.PlayTechChirp(false);
                            }
                        };
                        actionStack.Children.Add(btnDel);

                        _friendsListStack.Children.Add(fb);
                    }
                }));
            };

            _cloudLobby.OnChatMessageReceived += delegate(string user, string text)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (_txtChatHistory != null)
                    {
                        _txtChatHistory.AppendText(user + ": " + text + "\r\n");
                        _txtChatHistory.ScrollToEnd();
                    }
                    if (!string.Equals(user, _cloudLobby.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        ShowSteamToast("💬 " + user, text, NotificationType.Info);
                    }
                }));
            };

            _cloudLobby.OnFriendRequestReceived += delegate(string fromNick, string fromCode, string game, string room)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    string safeNick = string.IsNullOrWhiteSpace(fromNick) ? "Bilinmeyen" : fromNick;
                    string safeCode = string.IsNullOrWhiteSpace(fromCode) ? "-" : fromCode;
                    string safeGame = string.IsNullOrWhiteSpace(game) ? "Titan Hub" : game;

                    string msg = "⚡ Yeni arkadaşlık isteği\n\n"
                        + "Kullanıcı: " + safeNick + "\n"
                        + "Titan Kodu: " + safeCode + "\n"
                        + "Şu an: " + safeGame + "\n\n"
                        + "Bu kişiyi arkadaş listene eklemek istiyor musun?";

                    MessageBoxResult res = MessageBox.Show(this, msg, "Arkadaşlık İsteği", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (res == MessageBoxResult.Yes)
                    {
                        _cloudLobby.AcceptRemoteFriendRequest(fromNick, fromCode);
                        ShowToast("✅ " + safeNick + " artık arkadaş listende aktif.", NotificationType.Success);
                    }
                }));
            };

            _cloudLobby.OnFriendAccepted += delegate(string fromNick, string fromCode)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    string safeNick = string.IsNullOrWhiteSpace(fromNick) ? "Arkadaş" : fromNick;
                    string safeCode = string.IsNullOrWhiteSpace(fromCode) ? "" : " (" + fromCode + ")";
                    ShowToast("🎉 " + safeNick + safeCode + " arkadaşlık isteğini kabul etti.", NotificationType.Success);
                }));
            };

            _cloudLobby.OnInviteReceived += delegate(string fromNick, string game, string roomOrInvite)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    string safeNick = string.IsNullOrWhiteSpace(fromNick) ? "Bir arkadaşın" : fromNick;
                    string safeGame = string.IsNullOrWhiteSpace(game) ? "Titan Hub" : game;
                    string safeRoom = string.IsNullOrWhiteSpace(roomOrInvite) ? "TITAN-TR" : roomOrInvite;
                    ShowSteamToast("🎮 Lobi Daveti — " + safeNick, safeGame + " (" + safeRoom + ")", NotificationType.Success);

                    string msg = "📨 " + safeNick + " seni lobiye davet ediyor.\n\n"
                        + "🎮 Oyun/Lobi: " + safeGame + "\n"
                        + "📡 Oda: " + safeRoom + "\n\n"
                        + "Oda kodunu kullanmak ve bu odaya geçmek istiyor musun?";

                    if (MessageBox.Show(this, msg, "Lobi Daveti", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        if (!string.IsNullOrWhiteSpace(roomOrInvite) && !roomOrInvite.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                        {
                            _cloudLobby.RoomCode = roomOrInvite;
                        }
                        try { if (_txtRoomCode != null) _txtRoomCode.Text = _cloudLobby.RoomCode; } catch { }
                        _cloudLobby.CurrentStatus = "Lobiye Katılıyor";
                        _cloudLobby.SendHeartbeat();
                        _cloudLobby.RequestImmediatePoll();
                        SwitchView(null, null);
                        ShowToast("✅ " + safeNick + " daveti kabul edildi. Oda: " + _cloudLobby.RoomCode, NotificationType.Success);
                    }
                }));
            };

            _cloudLobby.OnDirectGameInvite += delegate(string fromNick, string fromCode, string game, string room, string hostIp, string virtIp)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    Win32.PlayTechChirp(true);
                    string invitePrompt = string.Format(
                        "🎮 UZAKTAN LOBİ ÇAĞRISI!\n\n" +
                        "'{0}' (Titan Kodu: {1}) seni '{2}' oynamaya çağırıyor!\n" +
                        "• Oda Kodu: {3}\n" +
                        "• Host IP: {4}\n\n" +
                        "Lobiye katılmak ve aynı oyunu başlatmak istiyor musunuz?",
                        fromNick, fromCode, game, room, hostIp);

                    MessageBoxResult invRes = MessageBox.Show(
                        invitePrompt,
                        "Titan // Aynı Oyuna Katıl",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (invRes == MessageBoxResult.Yes)
                    {
                        if (_txtRoomCode != null) _txtRoomCode.Text = room;
                        _cloudLobby.RoomCode = room;
                        VirtualLanMeshService.AllocateMeshIp(room, _cloudLobby.UserName);

                        GameProfile matched = null;
                        string targetKey = GameDeduplicationEngine.NormalizeGameTitle(game);
                        foreach (GameProfile gp in _allProfiles)
                        {
                            if (GameDeduplicationEngine.NormalizeGameTitle(gp.Name) == targetKey)
                            {
                                matched = gp;
                                break;
                            }
                        }

                        if (matched != null)
                        {
                            SelectGameProfile(matched);
                            SwitchView(_viewLibrary, _navBtnLibrary);

                            if (matched.IsInstalled)
                            {
                                try
                                {
                                    SteamEmulatorBridge.DeployFullEmulationSuite(matched, _cloudLobby.UserName);
                                    string sSub = System.IO.Path.Combine(matched.WorkingDirectory, "steam_settings", "settings");
                                    if (!System.IO.Directory.Exists(sSub)) System.IO.Directory.CreateDirectory(sSub);
                                    if (!string.IsNullOrEmpty(hostIp))
                                    {
                                        System.IO.File.WriteAllText(System.IO.Path.Combine(sSub, "connect.txt"), hostIp, System.Text.Encoding.UTF8);
                                        Clipboard.SetText(hostIp);
                                    }
                                }
                                catch { }

                                BtnHeroPrimary_Click(null, null);
                                _txtBottomStatus.Text = "🟢 " + fromNick + " lobisine bağlanıldı! Oda: " + room + " (Host IP panoya kopyalandı).";
                            }
                            else
                            {
                                string chosenDir = GameInstallLocationDialog.PromptInstallDirectory(this, matched);
                                if (!string.IsNullOrEmpty(chosenDir))
                                {
                                    DownloadInstallService.SetTargetInstallDirectoryForGame(matched, chosenDir);
                                    _txtInAppDlTitle.Text = matched.Name + " — Doğrudan İndirme ve Otomatik Kurulum Başlatıldı...";
                                    _txtInAppDlSpeed.Text = "Bağlanıyor...";
                                    _pbInAppDl.Value = 5;
                                    _inAppDlStripBorder.Visibility = Visibility.Visible;
                                    _downloader.StartInAppAutomatedPipeline(matched);
                                }
                            }
                        }
                        else
                        {
                            try { Process.Start("steam://run/480//"); } catch { }
                            _txtBottomStatus.Text = "Spacewar (480) başlatıldı, " + room + " lobisine bağlanılıyor...";
                        }
                    }
                }));
            };
        }

        private void BtnHeroPrimary_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;

            if (!_selectedProfile.IsInstalled)
            {
                string chosenDir = GameInstallLocationDialog.PromptInstallDirectory(this, _selectedProfile);
                if (string.IsNullOrEmpty(chosenDir)) return;

                // Steam davranışı: tek basış = indir + kur + oyuna gir.
                // Kurulum bittiğinde otomatik başlatma niyeti burada kaydedilir.
                InstallLaunchChain.RequestAutoLaunch(_selectedProfile);

                DownloadInstallService.SetTargetInstallDirectoryForGame(_selectedProfile, chosenDir);
                bool resumable = DownloadInstallService.HasResumableDownload(_selectedProfile);
                _txtInAppDlTitle.Text = resumable
                    ? _selectedProfile.Name + " — Yarım kalan indirme kaldığı yerden sürdürülüyor..."
                    : _selectedProfile.Name + " — Doğrudan İndirme ve Otomatik Kurulum Başlatıldı...";
                _txtInAppDlSpeed.Text = "Bağlanıyor...";
                _pbInAppDl.Value = 5;
                _inAppDlStripBorder.Visibility = Visibility.Visible;
                _downloader.StartInAppAutomatedPipeline(_selectedProfile);
                return;
            }

            if (_selectedProfile.UpdateAvailable)
            {
                // Steam Behavior: 1-Click Instant Update (No blocking dialog popups)
                // Güncelleme bitince oyun otomatik açılır (güncellemeyi başlatan oyuncu zaten oynamak istiyor).
                InstallLaunchChain.RequestAutoLaunch(_selectedProfile);
                try { string dummyMsg; SaveSyncService.CreateSaveBackup(_selectedProfile, out dummyMsg); } catch { }
                _inAppDlStripBorder.Visibility = Visibility.Visible;
                if (_txtInAppDlTitle != null)
                {
                    string srcHint = string.IsNullOrEmpty(_selectedProfile.UpdateSourceHint) ? "" : " • Kaynak: " + _selectedProfile.UpdateSourceHint;
                    _txtInAppDlTitle.Text = _selectedProfile.Name + " - Güncelleme İndiriliyor (" + _selectedProfile.RemoteVersion + ")" + srcHint;
                }
                if (_txtInAppDlSpeed != null) _txtInAppDlSpeed.Text = "Yeni sürüm dosyaları taranıyor...";
                if (_pbInAppDl != null) _pbInAppDl.Value = 5;
                if (_txtBottomStatus != null) _txtBottomStatus.Text = "🔄 '" + _selectedProfile.Name + "' güncellemesi başlatıldı...";
                _downloader.StartInAppAutomatedPipeline(_selectedProfile);
                return;
            }

            try
            {
                // Pre-Flight Launch Doctor: Auto-repairs deleted steam_api64.dll, steam_appid.txt and Defender whitelist
                PreFlightDoctorResult docRes = PreFlightLaunchDoctor.DiagnoseAndPrepareLaunch(_selectedProfile);
                if (!docRes.ReadyToLaunch)
                {
                    MessageBox.Show(docRes.DiagnosticSummary, "Titan // Uçuş Öncesi Doktoru", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _selectedProfile.LastPlayedText = "Bugün " + DateTime.Now.ToString("HH:mm");
                ProfileStore.SaveProfiles(_allProfiles);

                // Purge Windows Standby RAM to eradicate micro-stutters and hitching
                try
                {
                    double purgedMb;
                    StandbyMemoryCleaner.PurgeStandbyMemory(out purgedMb);
                }
                catch { }

                // Freeze Windows Background Services (Zero-Jitter Game Mode)
                GameModeDebloaterService.FreezeBackgroundServicesAsync(null);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _selectedProfile.ExecutablePath,
                    WorkingDirectory = _selectedProfile.WorkingDirectory,
                    UseShellExecute = true
                };
                Process proc = Process.Start(psi);
                _gameLaunchTime = DateTime.Now;

                _cloudLobby.CurrentGame = _selectedProfile.Name;
                _cloudLobby.CurrentGameVersion = _selectedProfile.Version ?? "";
                _cloudLobby.CurrentStatus = "Oyunda (Spacewar P2P)";
                _cloudLobby.SendHeartbeat();

                DiscordRpcService.SetActivity(_selectedProfile.Name, "Titan Game Hub — Spacewar 480 Co-Op");
                try { DiscordRichPresenceService.SetGamePlaying(_selectedProfile, _cloudLobby.RoomCode); } catch { }
                SteamAchievementEngine.StartMonitoring(_selectedProfile);

                if (proc != null)
                {
                    try { proc.PriorityClass = ProcessPriorityClass.High; } catch { }
                    try
                    {
                        string affMsg;
                        CpuAffinityOptimizer.OptimizeGameAffinity(proc, out affMsg);
                    }
                    catch { }
                    try { RawInputLatencyOptimizer.OptimizeInputLatency(proc); } catch { }
                    try { KernelCoreParkingNdisShield.ActivateCoreParkingAndNdisShield(); } catch { }

                    try { GameExitSaveSyncWatcher.WatchGameProcess(_selectedProfile, proc, null); } catch { }
                    try { AntiCheatSandboxBridge.StartAntiCheatSandbox(_selectedProfile); } catch { }

                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try { SteamClientBridgeService.ConnectAndBridgeSteamClient(_selectedProfile, false); } catch { }
                        try { DependencyRepairEngine.InspectAndRepairGameBinary(_selectedProfile); } catch { }
                        try { Dx12AsyncPsoPrecompilerService.PrecompileGameShaders(_selectedProfile); } catch { }

                        // Background P-Core re-lock & AOB Ultrawide hook after window creates
                        Thread.Sleep(2000);
                        if (!proc.HasExited)
                        {
                            string dummy;
                            CpuAffinityOptimizer.OptimizeGameAffinity(proc, out dummy);
                            AobMemoryPatcherService.ApplyUltrawidePatch(proc, 2.3888888f, out dummy);
                        }
                    });
                    proc.EnableRaisingEvents = true;
                    proc.Exited += delegate
                    {
                        try { AntiCheatSandboxBridge.StopAntiCheatSandbox(); } catch { }
                        try { RawInputLatencyOptimizer.RestoreLatencySettings(); } catch { }
                        DiscordRpcService.ClearActivity();
                        try { DiscordRichPresenceService.SetIdleInHub(); } catch { }
                        SteamAchievementEngine.StopMonitoring();
                        DisplayProfileService.RestoreDesktopMode();
                        GameModeDebloaterService.RestoreBackgroundServicesAsync(null);
                        if (BenchmarkRecorderService.IsRecording) BenchmarkRecorderService.StopBenchmark(null);
                        try
                        {
                            string dMsg;
                            SaveSyncService.CreateSaveBackup(_selectedProfile, out dMsg);
                            SaveSyncService.CreateSessionSnapshot(_selectedProfile, "Oturum Sonu (Zaman Tüneli)", out dMsg);

                            // P2P Auto-Sync latest save to peers in lobby
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
                            P2pSaveSwarmMeshService.BroadcastSaveToPeersAsync(_selectedProfile, peerIps, null);
                        }
                        catch { }

                        try
                        {
                            if (proc.ExitCode != 0)
                            {
                                CrashReport cr = CrashInspectorService.AnalyzeCrash(_selectedProfile, proc.ExitCode);
                                if (cr.HasCrashDetected)
                                {
                                    Dispatcher.BeginInvoke(new Action(delegate
                                    {
                                        string crashMsg = string.Format("⚠️ {0} beklenmeyen bir hatayla kapandı ({1})!\n\nTeşhis: {2}\n\nÖneri: {3}\n\nOyunu Güvenli Modda (-safe -windowed -dx11) yeniden başlatmak ister misiniz?",
                                            _selectedProfile.Name, cr.HexExitCode, cr.DiagnosticSummary, cr.RecommendedFix);
                                        MessageBoxResult mRes = MessageBox.Show(crashMsg, "Titan // Çökme Analizörü & Güvenli Mod", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                                        if (mRes == MessageBoxResult.Yes)
                                        {
                                            CrashInspectorService.LaunchInSafeMode(_selectedProfile);
                                        }
                                    }));
                                }
                            }
                        }
                        catch { }
                    };

                    string procName = System.IO.Path.GetFileNameWithoutExtension(_selectedProfile.ExecutablePath);
                    _txtManualProcess.Text = procName + ".exe";
                    _telemetry.ActiveGameProfile = _selectedProfile;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(2500);
                        _telemetry.Attach(procName);
                    });
                }

                _txtBottomStatus.Text = "▶ " + _selectedProfile.Name + " başlatıldı (Spacewar AppID 480 + 0.5ms Kernel Boost Aktif).";
                ProceduralSoundEngine.PlayLaunch();
                SelectGameProfile(_selectedProfile);
                Win32.PlayTechChirp(true);
            }
            catch (Exception ex)
            {
                _txtBottomStatus.Text = "Oyun başlatma hatası: " + ex.Message;
            }
        }

        private void BtnBindExe_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Title = _selectedProfile.Name + " için Oyun .EXE veya .RAR/.ZIP Arşivini Seçin";
            ofd.Filter = "Oyun ve Arşiv (*.exe;*.rar;*.zip;*.7z)|*.exe;*.rar;*.zip;*.7z";
            if (ofd.ShowDialog() == true)
            {
                string ext = System.IO.Path.GetExtension(ofd.FileName).ToLowerInvariant();
                if (ext == ".rar" || ext == ".zip" || ext == ".7z")
                {
                    _inAppDlStripBorder.Visibility = Visibility.Visible;
                    _downloader.ExtractAndBindAsync(_selectedProfile, ofd.FileName);
                }
                else
                {
                    _selectedProfile.ExecutablePath = ofd.FileName;
                    _selectedProfile.WorkingDirectory = System.IO.Path.GetDirectoryName(ofd.FileName);
                    _selectedProfile.DeployOnlineFix("480");
                    ProfileStore.SaveProfiles(_allProfiles);
                    SelectGameProfile(_selectedProfile);
                    RefreshLibrarySidebarList();
                    RefreshLibrarySummaryMetrics();
                    _txtBottomStatus.Text = _selectedProfile.Name + " başarıyla bağlandı ve Spacewar P2P (480) kuruldu!";
                    Win32.PlayTechChirp(true);
                }
            }
        }

        private void ExportPortableZipToDesktop()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ProfileStore.SaveProfiles(_allProfiles);
                    string desktopZip = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TitanGameHub_Portable.zip");
                    string rootDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
                    if (File.Exists(desktopZip)) File.Delete(desktopZip);
                    ZipFile.CreateFromDirectory(rootDir, desktopZip);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = "📦 Masaüstünde TitanGameHub_Portable.zip başarıyla güncellendi!";
                        Win32.PlayTechChirp(true);
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _txtBottomStatus.Text = "ZIP paketleme uyarısı: " + ex.Message;
                    }));
                }
            });
        }

        private void UpdateSteamBadgeStatus()
        {
            SteamAccountInfo acc = SteamClientBridgeService.GetActiveSteamAccount(false);
            if (_dotSteamBadge != null) _dotSteamBadge.Fill = acc.IsSteamRunning ? ThemeManager.AccentGreenBrush : ThemeManager.AccentTealBrush;
            if (_txtSteamBadge != null)
            {
                _txtSteamBadge.Text = acc.IsSteamRunning
                    ? ("STEAM: " + acc.PersonaName.ToUpperInvariant() + " (480)")
                    : "STEAM: BAĞLA (480)";
            }

            try
            {
                PlayerProfileData prof = SteamProfileManager.GetProfile(_allProfiles);
                if (_txtProfileLvl != null) _txtProfileLvl.Text = prof.Level.ToString();
                if (_txtProfileNick != null) _txtProfileNick.Text = !string.IsNullOrEmpty(acc.PersonaName) ? acc.PersonaName : prof.PersonaName;
            }
            catch { }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                WindowInteropHelper helper = new WindowInteropHelper(this);
                Win32.SetDarkTitleBar(helper.Handle);
                _hwndSource = HwndSource.FromHwnd(helper.Handle);
                if (_hwndSource != null)
                {
                    _hwndSource.AddHook(HwndHook);
                    // Win32.RegisterHotKey(helper.Handle, 1, 0, 0x70); // F1 [DEVRE DISI: Standart oyun tuslari korundu]
                    // Win32.RegisterHotKey(helper.Handle, 2, 0, 0x71); // F2 [DEVRE DISI: Standart oyun tuslari korundu]
                    // Win32.RegisterHotKey(helper.Handle, 3, 0, 0x72); // F3 [DEVRE DISI: Standart oyun tuslari korundu]
                    // Win32.RegisterHotKey(helper.Handle, 4, 0, 0x73); // F4 [DEVRE DISI: Standart oyun tuslari korundu]
                    Win32.RegisterHotKey(helper.Handle, 5, 0, 0x74); // F5
                    Win32.RegisterHotKey(helper.Handle, 6, 0, 0x75); // F6 (Live Turkish Subtitle HUD)
                    Win32.RegisterHotKey(helper.Handle, 7, 0x0004, 0x09); // Shift+Tab (Steam In-Game Overlay)
                    Win32.RegisterHotKey(helper.Handle, 8, 0, 0x7B); // F12 (Steam Screenshot)
                    Win32.RegisterHotKey(helper.Handle, 9, 0, 0x7A); // F11 (Steam Big Picture Fullscreen)
                    Win32.RegisterHotKey(helper.Handle, 10, 0, 0x76); // F7 (In-Game Wiki & Guide Browser)
                    Win32.RegisterHotKey(helper.Handle, 11, 0, 0x77); // F8 (In-Game Pinned Notes)
                    Win32.RegisterHotKey(helper.Handle, 12, 0, 0x78); // F9 (OSD Gamepad Keyboard)
                    Win32.RegisterHotKey(helper.Handle, 13, 0x0004, 0x7A); // Shift + F11 (Live Benchmark Recorder)
                    Win32.RegisterHotKey(helper.Handle, 14, 0x0001 | 0x0002, 0x46); // Ctrl + Alt + F (Universal Frame Gen)
                    QosNetworkJitterShield.StartShield();
                }
            }
            catch { }

            StartupTimer.Mark("Window_Loaded girdi");
            DownloadInstallService.CheckIsGameRunningFunc = delegate { return _telemetry != null && _telemetry.IsAttached; };

            // Tarayıcıya düşmeden önce sor: "siteye atıyor" şikayetini engeller.
            // 403 korumalı hostlar için arka plan WebView2 indirmesi: site ekranda açılmaz.
            DownloadInstallService.EmbeddedBrowserDownloader = delegate(string url, string dir, Action<int, long, long> prog, Func<bool> cancel, int minutes)
            {
                string path = null;
                try
                {
                    string referer = null;
                    try
                    {
                        string dlUrl = null;
                        Dispatcher.Invoke(new Action(delegate
                        {
                            if (_selectedProfile != null) dlUrl = _selectedProfile.DownloadUrl;
                            if (_txtBottomStatus != null)
                                _txtBottomStatus.Text = "Uygulama içi indirme hazırlanıyor...";
                        }));
                        if (!string.IsNullOrEmpty(dlUrl))
                        {
                            Uri pu = new Uri(dlUrl);
                            referer = pu.Scheme + "://" + pu.Host + "/";
                        }
                    }
                    catch { }

                    string error = null;
                    System.Threading.Thread t = new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            WebViewDownloadService.Result r = WebViewDownloadService.Download(url, dir, null, prog, cancel, minutes, referer);
                            if (r != null && r.Success) path = r.FilePath;
                            else if (r != null) error = r.Error;
                        }
                        catch (Exception ex) { error = ex.Message; }
                    });
                    t.SetApartmentState(System.Threading.ApartmentState.STA);
                    t.IsBackground = true;
                    t.Start();
                    t.Join();

                    if (!string.IsNullOrEmpty(error))
                    {
                        try
                        {
                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                if (_txtBottomStatus != null) _txtBottomStatus.Text = "Gömülü indirme: " + error;
                            }));
                        }
                        catch { }
                    }
                }
                catch { }
                return path;
            };

            // Ek site/pencere açmadan otomatik dene; kullanıcı onayı/harici site gerekmesin.
            DownloadInstallService.BrowserFallbackConfirmer = delegate(string host)
            {
                return true;
            };

            // Yeni kurulumlarda "hangi diske/klasöre kurulacak?" seçimi
            DownloadInstallService.InstallLocationPicker = delegate(GameProfile gp)
            {
                string chosen = null;
                try
                {
                    if (this.Dispatcher.CheckAccess())
                    {
                        chosen = GameInstallLocationDialog.PromptInstallDirectory(this, gp);
                    }
                    else
                    {
                        this.Dispatcher.Invoke(new Action(delegate
                        {
                            chosen = GameInstallLocationDialog.PromptInstallDirectory(this, gp);
                        }));
                    }
                }
                catch { chosen = null; }
                return chosen;
            };

            try { string scErr; DesktopShortcutHelper.CreateHubDesktopShortcut(out scErr); } catch { }

            StartupTimer.Mark("UI kuruldu, arka plan servisleri başlıyor");
            _cloudLobby.Start();
            _autoUpdater.Start(_allProfiles);
            if (SteamClientBridgeService.IsUpdateCheckEnabled())
            {
                LiveGameUpdateWatchdogService.StartWatchdog(_allProfiles, 15);

                // Arka plan ön-indirme: boştayken güncellemeleri indirir, Oyna'da anında kurulur.
                BackgroundUpdateService.Start(
                    _allProfiles,
                    delegate
                    {
                        if (_downloader.IsBusy) return true;
                        try
                        {
                            for (int i = 0; i < _allProfiles.Count; i++)
                            {
                                GameProfile p = _allProfiles[i];
                                if (p != null && p.IsInstalled && SmartUpdateShieldService.IsGameProcessRunning(p)) return true;
                            }
                        }
                        catch { }
                        return false;
                    },
                    delegate(string m) { Dispatcher.BeginInvoke(new Action(delegate { if (_txtBottomStatus != null) _txtBottomStatus.Text = m; })); },
                    30);
            }
            LiveGameUpdateWatchdogService.OnGameUpdateDetected += delegate(GameProfile gp, string oldVer, string newVer)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _txtBottomStatus.Text = string.Format("⚠️ YENİ SÜRÜM MEVCUT: {0} ({1} -> {2}) yayınlandı! Kütüphaneden 'GÜNCELLE' ile yükleyebilirsiniz.", gp.Name, oldVer, newVer);
                    Win32.PlayTechChirp(true);
                    RefreshLibrarySidebarList();
                    RefreshLibrarySummaryMetrics();
                    if (_selectedProfile != null && _selectedProfile.Id == gp.Id)
                        SelectGameProfile(_selectedProfile);
                }));
            };

            // Periyodik sessiz kütüphane güncelleme tarayıcısı (her 12 dakikada bir)
            DispatcherTimer libUpdateTimer = new DispatcherTimer();
            libUpdateTimer.Interval = TimeSpan.FromMinutes(12);
            libUpdateTimer.Tick += delegate
            {
                TriggerSilentLibraryUpdateScan();
            };
            libUpdateTimer.Start();

            // İlk açılışta 8 saniye sonra sessizce ilk taramayı yap
            DispatcherTimer initialScanTimer = new DispatcherTimer();
            initialScanTimer.Interval = TimeSpan.FromSeconds(8);
            initialScanTimer.Tick += delegate
            {
                initialScanTimer.Stop();
                TriggerSilentLibraryUpdateScan();
            };
            initialScanTimer.Start();

            _steamTimer = new DispatcherTimer();
            _steamTimer.Interval = TimeSpan.FromSeconds(4);
            _steamTimer.Tick += delegate
            {
                UpdateSteamBadgeStatus();
                if (_txtMetricHardware != null) _txtMetricHardware.Text = Win32.GetSystemHardwareSummary();
            };
            _steamTimer.Start();
            UpdateSteamBadgeStatus();

            string[] args = Environment.GetCommandLineArgs();
            foreach (string a in args)
            {
                if (a.StartsWith("--magnet-scan="))
                {
                    int lim;
                    int.TryParse(a.Substring("--magnet-scan=".Length), out lim);
                    RunMagnetScanHeadless(lim > 0 ? lim : 20);
                }
                if (a.StartsWith("--torrent-test="))
                {
                    RunTorrentTest(a.Substring("--torrent-test=".Length));
                }
                if (a.StartsWith("--wv2-test="))
                {
                    string testUrl = a.Substring("--wv2-test=".Length);
                    RunWebViewDownloadTest(testUrl);
                }
                if (a.StartsWith("--fill-covers"))
                {
                    RunCoverBackfillHeadless(200);
                }
                if (a.StartsWith("--import-catalog"))
                {
                    RunBulkImportHeadless();
                }
                if (a == "--push-ota")
                {
                    _autoUpdater.PushFullApplicationBinaryToRemotePCsAsync(_allProfiles);
                }
                if (a == "--preview-overlay")
                {
                    DispatcherTimer ovSnap = new DispatcherTimer();
                    ovSnap.Interval = TimeSpan.FromSeconds(2.5);
                    ovSnap.Tick += delegate
                    {
                        ovSnap.Stop();
                        try
                        {
                            if (_overlay != null)
                            {
                                _overlay.ShowOverlay();
                                _overlay.BringToFront();
                                System.Threading.Thread.Sleep(450);
                                int w = _overlay.Width > 0 ? _overlay.Width : 1280;
                                int h = _overlay.Height > 0 ? _overlay.Height : 720;
                                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h))
                                {
                                    _overlay.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, w, h));
                                    bmp.Save(@"D:\TitanGameHub\preview_overlay.png", System.Drawing.Imaging.ImageFormat.Png);
                                }
                                _overlay.HideOverlay();
                            }
                        }
                        catch (Exception ex)
                        {
                            System.IO.File.WriteAllText(@"D:\TitanGameHub\overlay_preview_error.txt", ex.ToString());
                        }
                        this.Close();
                        Environment.Exit(0);
                    };
                    ovSnap.Start();
                }
                if (a == "--preview" || a == "--preview-store" || a == "--preview-download")
                {
                    if (a == "--preview-store")
                    {
                        SwitchView(_viewStore, _navBtnStore);
                    }
                    else if (a == "--preview-download")
                    {
                        SwitchView(_viewDownload, _navBtnDownload);
                        RefreshDownloadQueueUI();
                    }
                    DispatcherTimer snapTimer = new DispatcherTimer();
                    double snapDelay = 2.5;
                    try { string envDelay = Environment.GetEnvironmentVariable("TITAN_PREVIEW_DELAY_SEC"); double parsedDelay; if (!string.IsNullOrEmpty(envDelay) && double.TryParse(envDelay, out parsedDelay) && parsedDelay > 0) snapDelay = parsedDelay; } catch { }
                    snapTimer.Interval = TimeSpan.FromSeconds(snapDelay);
                    snapTimer.Tick += delegate
                    {
                        snapTimer.Stop();
                        try
                        {
                            RenderTargetBitmap rtb = new RenderTargetBitmap((int)this.ActualWidth, (int)this.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            rtb.Render(this);
                            PngBitmapEncoder enc = new PngBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(rtb));
                            string outFile = (a == "--preview-store") ? @"D:\TitanGameHub\preview_store.png" : ((a == "--preview-download") ? @"D:\TitanGameHub\preview_download.png" : @"D:\TitanGameHub\preview_check.png");
                            using (FileStream fs = new FileStream(outFile, FileMode.Create))
                            {
                                enc.Save(fs);
                            }
                        }
                        catch (Exception pvEx)
                        {
                            try { File.WriteAllText(@"D:\TitanGameHub\preview_error.txt", pvEx.ToString()); } catch { }
                        }
                        this.Close();
                        Environment.Exit(0);
                    };
                    snapTimer.Start();
                }
            }
        }

        /// <summary>
        /// TÜM OYUNLARI TARA: bu bilgisayardaki kurulu tüm oyunları bulur (Steam/Epic/GOG +
        /// tüm diskler + portatif Games klasörü) ve kütüphaneye ekler.
        /// </summary>


        private void TriggerSilentLibraryUpdateScan()
        {
            if (_allProfiles == null || _allProfiles.Count == 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<GameProfile> updatedList;
                    int count = SteamClientBridgeService.ScanLibraryForLiveUpdates(_allProfiles, out updatedList);
                    if (count > 0)
                    {
                        ProfileStore.SaveProfiles(_allProfiles);
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            RefreshLibrarySidebarList();
                            if (_selectedProfile != null) SelectGameProfile(_selectedProfile);
                            _txtBottomStatus.Text = string.Format("🔄 Oto Güncelleme: {0} oyun için yeni sürüm tespit edildi! (Kütüphanede 🔄 ile işaretlendi)", count);
                            Win32.PlayTechChirp(true);
                        }));
                    }
                }
                catch { }
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _cloudLobby.Stop();
            _autoUpdater.Stop();
            SteamEmulatorBridge.StopLanDiscovery();
            ZeroVpnTunnelService.StopHolePunchTunnel();
            VirtualLanMeshService.StopLiveSaveMirroring();
            try
            {
                WindowInteropHelper helper = new WindowInteropHelper(this);
                for (int i = 1; i <= 13; i++) Win32.UnregisterHotKey(helper.Handle, i);
            }
            catch { }

            DiscordRpcService.ClearActivity();
            SteamAchievementEngine.StopMonitoring();

            if (_overlay != null) _overlay.Close();
            _telemetry.Detach();
        }

        private void RunSystemRamBoost()
        {
            double freedMb;
            int procs = Win32.BoostSystemMemoryWorkingSet(out freedMb);
            Win32.PlayTechChirp(true);
            if (_txtMetricHardware != null) _txtMetricHardware.Text = Win32.GetSystemHardwareSummary();
            _txtBottomStatus.Text = string.Format("🚀 FPS & RAM Boost Tamamlandı: {0} arka plan süreci optimize edildi, ~{1:0} MB RAM oyunlar için serbest bırakıldı!", procs, freedMb);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if ((id == 5 || id == 7) && _overlay != null) // Shift+Tab / F5 (Steam In-Game Overlay)
                {
                    if (_overlay.Visible)
                    {
                        _overlay.HideOverlay();
                        if (_chkQuickOverlay != null) _chkQuickOverlay.IsChecked = false;
                    }
                    else
                    {
                        _overlay.ShowOverlay();
                        if (_chkQuickOverlay != null) _chkQuickOverlay.IsChecked = true;
                    }
                    Win32.PlayTechChirp(_overlay.Visible);
                }
                else if (id == 6)
                {
                    LiveTranslatorHudService.IsEnabled = !LiveTranslatorHudService.IsEnabled;
                    if (LiveTranslatorHudService.IsEnabled && _overlay != null && !_overlay.Visible)
                    {
                        _overlay.Show();
                    }
                    Win32.PlayTechChirp(LiveTranslatorHudService.IsEnabled);
                }
                else if (id == 8)
                {
                    CaptureInGameScreenshot();
                }
                else if (id == 9)
                {
                    if (this.WindowStyle == WindowStyle.None && this.WindowState == WindowState.Maximized)
                    {
                        this.WindowStyle = WindowStyle.SingleBorderWindow;
                        this.WindowState = WindowState.Normal;
                    }
                    else
                    {
                        this.WindowStyle = WindowStyle.None;
                        this.WindowState = WindowState.Maximized;
                    }
                    Win32.PlayTechChirp(true);
                }
                else if (id == 10)
                {
                    string targetName = (_selectedProfile != null) ? _selectedProfile.Name : "";
                    InGameGuideWindow.ToggleGuide(targetName);
                    Win32.PlayTechChirp(true);
                }
                else if (id == 11)
                {
                    string targetName = (_selectedProfile != null) ? _selectedProfile.Name : "Genel";
                    InGameNotesWindow.ToggleNotes(targetName);
                    Win32.PlayTechChirp(true);
                }
                else if (id == 12)
                {
                    ControllerOsdKeyboard.ToggleKeyboard();
                    Win32.PlayTechChirp(true);
                }
                else if (id == 13)
                {
                    string bMsg;
                    string gName = (_selectedProfile != null) ? _selectedProfile.Name : "Oyun";
                    bool started = BenchmarkRecorderService.ToggleBenchmark(gName, delegate(BenchmarkResult res)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _txtBottomStatus.Text = res.FormattedSummary;
                            ProceduralSoundEngine.PlayAchievement();
                        }));
                    }, out bMsg);
                    _txtBottomStatus.Text = bMsg;
                    Win32.PlayTechChirp(started);
                }
                else if (id == 14)
                {
                    string fMsg;
                    bool active = LosslessScalingEngine.ToggleFrameGeneration(out fMsg);
                    _txtBottomStatus.Text = fMsg;
                    Win32.PlayTechChirp(active);
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void CaptureInGameScreenshot()
        {
            try
            {
                string gameName = (_selectedProfile != null && !string.IsNullOrEmpty(_selectedProfile.Name))
                    ? Regex.Replace(_selectedProfile.Name, @"[^\w\s-]", "").Trim()
                    : "SteamCaptures";

                string shotDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots", gameName);
                if (!Directory.Exists(shotDir)) Directory.CreateDirectory(shotDir);

                string shotFile = System.IO.Path.Combine(shotDir, string.Format("SteamShot_{0:yyyyMMdd_HHmmss}.png", DateTime.Now));

                int screenW = (int)SystemParameters.PrimaryScreenWidth;
                int screenH = (int)SystemParameters.PrimaryScreenHeight;

                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(screenW, screenH))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(screenW, screenH));
                    }
                    bmp.Save(shotFile, System.Drawing.Imaging.ImageFormat.Png);
                }

                _txtBottomStatus.Text = "📸 Steam Ekran Görüntüsü Kaydedildi: " + System.IO.Path.GetFileName(shotFile);
                Win32.PlayTechChirp(true);
            }
            catch (Exception ex)
            {
                _txtBottomStatus.Text = "Ekran görüntüsü hatası: " + ex.Message;
            }
        }

        private void BuildSettingsCenterView(Grid host)
        {
            _viewSettings = new Grid() { Margin = new Thickness(24), Visibility = Visibility.Collapsed };
            host.Children.Add(_viewSettings);

            ScrollViewer scroll = new ScrollViewer() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _viewSettings.Children.Add(scroll);

            StackPanel mainStack = new StackPanel() { MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Left };
            scroll.Content = mainStack;

            // Header Banner
            Border headerCard = new Border()
            {
                Background = HexBrush("#161220"),
                BorderBrush = HexBrush("#2B4466"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 16)
            };
            mainStack.Children.Add(headerCard);

            StackPanel headerStack = new StackPanel();
            headerCard.Child = headerStack;
            headerStack.Children.Add(new TextBlock()
            {
                Text = "⚙ STEAM & TITAN HUB GELİŞMİŞ AYARLAR VE EKOSİSTEM YÖNETİCİSİ",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 14,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            headerStack.Children.Add(new TextBlock()
            {
                Text = "İndirme yolları, otomatik güncelleme periyotları, save kasası, Shift+Tab Steam arayüzü ve çekirdek performans yapılandırması",
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 10.5
            });

            // 1. Storage & Library Folder Card
            Border storageCard = CreateSettingsCard("📁 KÜTÜPHANE & İNDİRME DEPOLAMA ALANI (STORAGE MANAGER)");
            mainStack.Children.Add(storageCard);
            StackPanel storageStack = (StackPanel)storageCard.Child;

            storageStack.Children.Add(new TextBlock()
            {
                Text = "Oyunların indirileceği ve otomatik kurulacağı ana dizin:",
                Foreground = HexBrush("#C7D5E0"),
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 0, 6)
            });

            Grid pathGrid = new Grid() { Margin = new Thickness(0, 0, 0, 10) };
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            storageStack.Children.Add(pathGrid);

            TextBox txtGamesPath = new TextBox()
            {
                Text = DownloadInstallService.GetEffectiveGamesRoot(),
                IsReadOnly = true,
                Background = ThemeManager.BorderSubtleBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(txtGamesPath, 0);
            pathGrid.Children.Add(txtGamesPath);

            Button btnChangeDir = CreateSteamUtilityButton("📁 DİZİNİ DEĞİŞTİR", "Oyunların kurulacağı klasörü seç (SSD/HDD)");
            btnChangeDir.Margin = new Thickness(6, 0, 0, 0);
            btnChangeDir.Click += delegate
            {
                System.Windows.Forms.FolderBrowserDialog fbd = new System.Windows.Forms.FolderBrowserDialog();
                fbd.Description = "Titan Game Hub — Oyunların Kurulacağı Dizin";
                fbd.SelectedPath = DownloadInstallService.GetEffectiveGamesRoot();
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    DownloadInstallService.CustomGamesRootDirectory = fbd.SelectedPath;
                    txtGamesPath.Text = fbd.SelectedPath;
                    _txtBottomStatus.Text = "Kurulum dizini güncellendi: " + fbd.SelectedPath;
                    Win32.PlayTechChirp(true);
                }
            };
            Grid.SetColumn(btnChangeDir, 1);
            pathGrid.Children.Add(btnChangeDir);

            Button btnOpenDir = CreateSteamUtilityButton("🗁 DİZİNİ AÇ", "Mevcut oyun kurulum klasörünü Windows Gezgini'nde aç");
            btnOpenDir.Margin = new Thickness(6, 0, 0, 0);
            btnOpenDir.Click += delegate
            {
                try { Process.Start("explorer.exe", DownloadInstallService.GetEffectiveGamesRoot()); } catch { }
            };
            Grid.SetColumn(btnOpenDir, 2);
            pathGrid.Children.Add(btnOpenDir);

            // 2. Auto-Update & Live Detection Card
            Border updateCard = CreateSettingsCard("🔄 GÜNCELLEME, CANLI ALGILAMA & SAVE KORUMASI");
            mainStack.Children.Add(updateCard);
            StackPanel updateStack = (StackPanel)updateCard.Child;

            CheckBox chkAutoScan = CreateTelemetryCheckBox("Arka Planda Otomatik Kütüphane Taraması (Her 12 dakikada bir)");
            chkAutoScan.IsChecked = true;
            updateStack.Children.Add(chkAutoScan);

            CheckBox chkLiveMidDl = CreateTelemetryCheckBox("İndirme Sürerken Yeni Sürüm Çıkarsa Anında Algıla ve Uyar");
            chkLiveMidDl.IsChecked = true;
            updateStack.Children.Add(chkLiveMidDl);

            CheckBox chkSaveQuarantine = CreateTelemetryCheckBox("Güncellemelerde Save Dosyalarını Otomatik Koru (Sıfır İlerleme Kaybı)");
            chkSaveQuarantine.IsChecked = true;
            updateStack.Children.Add(chkSaveQuarantine);

            CheckBox chkShield = CreateTelemetryCheckBox("🛡 Akıllı Güncelleme Kalkanı & Kilitli Süreç Temizleyici (ERROR_SHARING_VIOLATION Engelleme)");
            chkShield.IsChecked = true;
            updateStack.Children.Add(chkShield);

            CheckBox chkRollback = CreateTelemetryCheckBox("⏪ Güncellemelerde Otomatik 1-Tık Geri Alma (Rollback) Noktası Oluştur");
            chkRollback.IsChecked = true;
            updateStack.Children.Add(chkRollback);

            CheckBox chkHeal = CreateTelemetryCheckBox("🎮 Güncelleme Sonrası Spacewar (AppID 480) & Online-Fix Otomatik Onarımı");
            chkHeal.IsChecked = true;
            updateStack.Children.Add(chkHeal);

            CheckBox chkModShield = CreateTelemetryCheckBox("🛡 Mod, Türkçe Yama ve ReShade Otomatik Göç Kalkanı (Kayıpsız Geçiş)");
            chkModShield.IsChecked = true;
            updateStack.Children.Add(chkModShield);

            CheckBox chkSwarm = CreateTelemetryCheckBox("⚡ Takım İçi P2P Swarm İndirme (LAN / ZeroVpn üzerinden 100 MB/s Hızlı Dağıtım)");
            chkSwarm.IsChecked = true;
            updateStack.Children.Add(chkSwarm);

            CheckBox chkFleetCmd = CreateTelemetryCheckBox("📡 Uzaktan Filo Güncelleme Emirlerini Dinle ve Anında Bildir");
            chkFleetCmd.IsChecked = true;
            updateStack.Children.Add(chkFleetCmd);

            CheckBox chkDelta = CreateTelemetryCheckBox("⚡ xdelta3 / VCDIFF İkili Diferansiyel Yama Motoru (Delta Patching)");
            chkDelta.IsChecked = true;
            updateStack.Children.Add(chkDelta);

            CheckBox chkServerWatchdog = CreateTelemetryCheckBox("🖥 Headless Dedicated Server & Watchdog Otomatik Yeniden Başlatma");
            chkServerWatchdog.IsChecked = true;
            updateStack.Children.Add(chkServerWatchdog);

            CheckBox chkSaveConflict = CreateTelemetryCheckBox("💾 Çoklu Cihaz Save Çakışma ve Zaman Damgası Çözücü (Zero-Loss Branching)");
            chkSaveConflict.IsChecked = true;
            updateStack.Children.Add(chkSaveConflict);

            CheckBox chkFrameGen = CreateTelemetryCheckBox("🚀 Evrensel 2x Kare Çarpanı & FSR Ölçekleme [Ctrl+Alt+F]");
            chkFrameGen.IsChecked = true;
            updateStack.Children.Add(chkFrameGen);

            CheckBox chkQos = CreateTelemetryCheckBox("⚡ P2P QoS & Dinamik Jitter Kalkanı (DSCP 46 Öncelik • 0ms Ping Sıçraması)");
            chkQos.IsChecked = true;
            updateStack.Children.Add(chkQos);

            CheckBox chkDefender = CreateTelemetryCheckBox("🛡 Windows Defender Otomatik Dışlama Kalkanı (False-Positive Engelleme)");
            chkDefender.IsChecked = true;
            updateStack.Children.Add(chkDefender);

            CheckBox chkDoctor = CreateTelemetryCheckBox("🔍 Başlatma Öncesi Teşhis Doktoru (Eksik steam_api64.dll ve AppID Onarımı)");
            chkDoctor.IsChecked = true;
            updateStack.Children.Add(chkDoctor);

            CheckBox chkAutoExitSave = CreateTelemetryCheckBox("💾 Oyun Kapanınca Anında Otomatik Save Eşitleme (Auto-Sync on Exit)");
            chkAutoExitSave.IsChecked = true;
            updateStack.Children.Add(chkAutoExitSave);

            CheckBox chkDirectConn = CreateTelemetryCheckBox("🚀 Doğrudan Sunucuya Bağlanma Enjektörü (Direct Connect Launcher)");
            chkDirectConn.IsChecked = true;
            updateStack.Children.Add(chkDirectConn);

            CheckBox chkRelocate = CreateTelemetryCheckBox("📁 Depolama Sürücüsü Taşıma Motoru (Storage Drive Relocator)");
            chkRelocate.IsChecked = true;
            updateStack.Children.Add(chkRelocate);

            CheckBox chkThrottle = CreateTelemetryCheckBox("🚀 Oyun Çalışırken İndirme Hızını 2 MB/s ile Sınırla (Lag Önleme)");
            chkThrottle.IsChecked = DownloadInstallService.AutoThrottleWhenGameRunning;
            chkThrottle.Checked += delegate { DownloadInstallService.AutoThrottleWhenGameRunning = true; };
            chkThrottle.Unchecked += delegate { DownloadInstallService.AutoThrottleWhenGameRunning = false; };
            updateStack.Children.Add(chkThrottle);

            // 3. Steam Overlay & In-Game Shortcuts Card
            Border overlayCard = CreateSettingsCard("🎮 STEAM ARAYÜZÜ, KISAYOLLAR & OYUN İÇİ HUD");
            mainStack.Children.Add(overlayCard);
            StackPanel overlayStack = (StackPanel)overlayCard.Child;

            CheckBox chkShiftTab = CreateTelemetryCheckBox("[Shift + Tab] ve [F5] Oyun İçi Steam HUD Arayüzü");
            chkShiftTab.IsChecked = true;
            chkShiftTab.Checked += delegate { if (_overlay != null) _overlay.Show(); };
            chkShiftTab.Unchecked += delegate { if (_overlay != null) _overlay.Hide(); };
            overlayStack.Children.Add(chkShiftTab);

            CheckBox chkSfx = CreateTelemetryCheckBox("🔊 Steam Big Picture UI Ses Efektleri (Synthesized PCM Chimes)");
            chkSfx.IsChecked = !ProceduralSoundEngine.IsMuted;
            chkSfx.Checked += delegate { ProceduralSoundEngine.IsMuted = false; ProceduralSoundEngine.PlaySelect(); };
            chkSfx.Unchecked += delegate { ProceduralSoundEngine.IsMuted = true; };
            overlayStack.Children.Add(chkSfx);

            CheckBox chkF12 = CreateTelemetryCheckBox("[F12] Steam Ekran Görüntüsü Motoru (Screenshots/ Klasörüne Kayıt)");
            chkF12.IsChecked = true;
            overlayStack.Children.Add(chkF12);

            CheckBox chkF6 = CreateTelemetryCheckBox("[F6] Canlı Türkçe Altyazı HUD (Gerçek Zamanlı Diyalog Çevirisi)");
            chkF6.IsChecked = LiveTranslatorHudService.IsEnabled;
            chkF6.Checked += delegate { LiveTranslatorHudService.IsEnabled = true; };
            chkF6.Unchecked += delegate { LiveTranslatorHudService.IsEnabled = false; };
            overlayStack.Children.Add(chkF6);

            CheckBox chk05ms = CreateTelemetryCheckBox("[⚡ 0.5ms] Windows NT Çekirdek Zamanlayıcı Düşük Gecikme Kilidi");
            chk05ms.IsChecked = KernelLatencyOptimizer.IsTimerResolutionLocked;
            chk05ms.Checked += delegate { string rep; KernelLatencyOptimizer.EnableUltraLowLatencyTimer(out rep); };
            chk05ms.Unchecked += delegate { KernelLatencyOptimizer.DisableUltraLowLatencyTimer(); };
            overlayStack.Children.Add(chk05ms);

            // 4. Gamepad & Steam Input Manager Card
            Border gamepadCard = CreateSettingsCard("🎮 STEAM INPUT & GAMEPAD YÖNETİCİSİ");
            mainStack.Children.Add(gamepadCard);
            StackPanel gamepadStack = (StackPanel)gamepadCard.Child;

            GamepadStateInfo pad = XInputGamepadService.GetPrimaryControllerState();
            TextBlock txtPadStatus = new TextBlock()
            {
                Text = pad.IsConnected ? ("✔ " + pad.ControllerType + "  •  " + pad.BatteryLevel) : "⚪ Gamepad Bağlı Değil (XInput Bekleniyor)",
                Foreground = pad.IsConnected ? ThemeManager.AccentGreenBrush : ThemeManager.ForegroundMutedBrush,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            gamepadStack.Children.Add(txtPadStatus);

            WrapPanel padWrap = new WrapPanel() { Margin = new Thickness(0, 4, 0, 0) };
            gamepadStack.Children.Add(padWrap);

            Button btnTestVib = new Button()
            {
                Content = "📳 TİTREŞİM TESTİ YAP",
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 8),
                Background = (Brush)MakeGradient("#9333EA", "#6B21A8"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnTestVib, 3);
            btnTestVib.Click += delegate
            {
                XInputGamepadService.TestVibration(0, 600);
                _txtBottomStatus.Text = "🎮 Gamepad titreşim testi gönderildi!";
                Win32.PlayTechChirp(true);
            };
            padWrap.Children.Add(btnTestVib);

            Button btnRefreshPad = new Button()
            {
                Content = "🔄 KOLLARI YENİDEN TARA",
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 8),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = ThemeManager.AccentTealBrush,
                BorderBrush = ThemeManager.AccentBlueBrush,
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnRefreshPad, 3);
            btnRefreshPad.Click += delegate
            {
                GamepadStateInfo refreshedPad = XInputGamepadService.GetPrimaryControllerState();
                txtPadStatus.Text = refreshedPad.IsConnected ? ("✔ " + refreshedPad.ControllerType + "  •  " + refreshedPad.BatteryLevel) : "⚪ Gamepad Bağlı Değil (XInput Bekleniyor)";
                txtPadStatus.Foreground = refreshedPad.IsConnected ? ThemeManager.AccentGreenBrush : ThemeManager.ForegroundMutedBrush;
                _txtBottomStatus.Text = "Gamepad durumu güncellendi: " + txtPadStatus.Text;
                Win32.PlayTechChirp(refreshedPad.IsConnected);
            };
            padWrap.Children.Add(btnRefreshPad);

            // 5. Maintenance, Save Vault & Cache Cleaning Card
            Border maintCard = CreateSettingsCard("🛠 VERİ GÜVENLİĞİ, CO-OP SAVE KASASI & SİSTEM BAKIMI");
            mainStack.Children.Add(maintCard);
            StackPanel maintStack = (StackPanel)maintCard.Child;

            WrapPanel maintWrap = new WrapPanel() { Margin = new Thickness(0, 4, 0, 0) };
            maintStack.Children.Add(maintWrap);

            Button btnBackupAllSaves = new Button()
            {
                Content = "💾 TÜM OYUN SAVE'LERİNİ TOPLU YEDEKLE",
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 8),
                Background = (Brush)MakeGradient("#5DBA2F", "#46941E"),
                Foreground = Brushes.White,
                BorderBrush = HexBrush("#6ED43B"),
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnBackupAllSaves, 3);
            btnBackupAllSaves.Click += delegate
            {
                int count = 0;
                foreach (GameProfile gp in _allProfiles)
                {
                    if (gp.IsInstalled)
                    {
                        string sMsg;
                        SaveSyncService.CreateSaveBackup(gp, out sMsg);
                        count++;
                    }
                }
                _txtBottomStatus.Text = string.Format("💾 Başarılı: {0} kurulu oyunun tüm save dosyaları 'Backups/' kasasına yedeklendi!", count);
                Win32.PlayTechChirp(true);
            };
            maintWrap.Children.Add(btnBackupAllSaves);

            Button btnVerifyAll = new Button()
            {
                Content = "🛡 TÜM KÜTÜPHANENİN BÜTÜNLÜĞÜNÜ DOĞRULA",
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 8),
                Background = (Brush)MakeGradient("#9333EA", "#6B21A8"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnVerifyAll, 3);
            btnVerifyAll.Click += delegate
            {
                int verified = 0;
                foreach (GameProfile gp in _allProfiles)
                {
                    if (gp.IsInstalled)
                    {
                        DependencyRepairEngine.InspectAndRepairGameBinary(gp);
                        SteamEmulatorBridge.DeployFullEmulationSuite(gp, Environment.UserName);
                        verified++;
                    }
                }
                _txtBottomStatus.Text = string.Format("✔ {0} kurulu oyunun dosya bütünlüğü ve Spacewar (480) kancaları doğrulandı!", verified);
                Win32.PlayTechChirp(true);
            };
            maintWrap.Children.Add(btnVerifyAll);

            Button btnBatchLzx = new Button()
            {
                Content = "🗜 TÜM KÜTÜPHANEYİ LZX İLE ŞEFFAF SIKIŞTIR (%40 SSD KAZANCI)",
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 8),
                Background = (Brush)MakeGradient("#7E22CE", "#0369A1"),
                Foreground = Brushes.White,
                BorderBrush = ThemeManager.AccentTealBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnBatchLzx, 3);
            btnBatchLzx.Click += delegate
            {
                int started = 0;
                foreach (GameProfile gp in _allProfiles)
                {
                    if (gp.IsInstalled && !string.IsNullOrEmpty(gp.WorkingDirectory))
                    {
                        NtfsCompactEngine.CompressGameDirectoryAsync(gp.WorkingDirectory, null, delegate(CompactResult cr)
                        {
                            Dispatcher.BeginInvoke(new Action(delegate { _txtBottomStatus.Text = cr.OutputSummary; }));
                        });
                        started++;
                    }
                }
                _txtBottomStatus.Text = string.Format("🗜 {0} kurulu oyun için arka planda Windows LZX şeffaf sıkıştırma başlatıldı!", started);
                Win32.PlayTechChirp(true);
                ProceduralSoundEngine.PlaySelect();
            };
            maintWrap.Children.Add(btnBatchLzx);

            Button btnCleanCache = new Button()
            {
                Content = "🗑 ÖNBELLEK & GEÇİCİ İNDİRME ARTIKLARINI TEMİZLE",
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 8),
                Background = ThemeManager.DarkNavyBrush,
                Foreground = HexBrush("#FF6B6B"),
                BorderBrush = HexBrush("#7A2A2E"),
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnCleanCache, 3);
            btnCleanCache.Click += delegate
            {
                try
                {
                    string root = DownloadInstallService.GetEffectiveGamesRoot();
                    int deleted = 0;
                    if (Directory.Exists(root))
                    {
                        foreach (string f in Directory.GetFiles(root, "*.resume")) { File.Delete(f); deleted++; }
                        foreach (string f in Directory.GetFiles(root, "*_pkg.*")) { File.Delete(f); deleted++; }
                    }
                    _txtBottomStatus.Text = string.Format("🗑 {0} adet geçici indirme artığı ve önbellek dosyası temizlendi.", deleted);
                    Win32.PlayTechChirp(true);
                }
                catch (Exception ex)
                {
                    _txtBottomStatus.Text = "Temizleme hatası: " + ex.Message;
                }
            };
            maintWrap.Children.Add(btnCleanCache);
        }

        private Border CreateSettingsCard(string title)
        {
            Border card = new Border()
            {
                Background = ThemeManager.SidebarNavy,
                BorderBrush = HexBrush("#2D2345"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 14)
            };
            StackPanel sp = new StackPanel();
            card.Child = sp;
            sp.Children.Add(new TextBlock()
            {
                Text = title,
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });
            return card;
        }

        private void SwitchView(Grid targetView, Button activeBtn)
        {
            if (_viewLibrary != null) _viewLibrary.Visibility = Visibility.Collapsed;
            if (_viewStore != null) _viewStore.Visibility = Visibility.Collapsed;
            if (_viewDownload != null) _viewDownload.Visibility = Visibility.Collapsed;
            if (_viewZeroVpn != null) _viewZeroVpn.Visibility = Visibility.Collapsed;
            if (_viewModManager != null) _viewModManager.Visibility = Visibility.Collapsed;
            if (_viewTelemetry != null) _viewTelemetry.Visibility = Visibility.Collapsed;
            if (_viewOtaShare != null) _viewOtaShare.Visibility = Visibility.Collapsed;
            if (_viewSettings != null) _viewSettings.Visibility = Visibility.Collapsed;

            if (targetView != null)
            {
                targetView.Visibility = Visibility.Visible;
                try
                {
                    DoubleAnimation da = new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(160));
                    targetView.BeginAnimation(UIElement.OpacityProperty, da);
                }
                catch { }
            }
            ProceduralSoundEngine.PlaySelect();

            // Mağaza ilk kez açıldığında Steam gibi gerçek vitrin: bağlı tüm siteler birleşik yüklenir.
            try
            {
                if (targetView == _viewStore && !_storeAutoLoaded && _activeSiteCategoryId == -1 &&
                    (_txtStoreSearch == null || string.IsNullOrWhiteSpace(_txtStoreSearch.Text)))
                {
                    _storeAutoLoaded = true;
                    _storeGridDirty = false;
                    _activeSiteCategoryId = -20;
                    _currentStorePage = 1;
                    HighlightStoreCategoryButtons();
                    TriggerLiveSiteSearchOrCategoryFetch();
                }
                else if (targetView == _viewStore && _storeGridDirty)
                {
                    _storeGridDirty = false;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        try { RefreshStorefrontGrid(); }
                        catch { }
                    }));
                }
            }
            catch (Exception ex)
            {
                try { _txtBottomStatus.Text = "Mağaza açılırken hata: " + ex.Message; } catch { }
            }

            // Kütüphane dışındaki tam sayfa görünümlerde sol oyun listesini otomatik gizle (geniş ekran modu)
            if (_colLeftLibrary != null)
                _colLeftLibrary.Width = (targetView == _viewLibrary) ? new GridLength(252) : new GridLength(0);

            if (_txtHeaderViewTitle != null)
            {
                if (targetView == _viewLibrary) _txtHeaderViewTitle.Text = "KÜTÜPHANE";
                else if (targetView == _viewStore) _txtHeaderViewTitle.Text = "CANLI MAĞAZA (5000+)";
                else if (targetView == _viewDownload) _txtHeaderViewTitle.Text = "İNDİRME MERKEZİ";
                else if (targetView == _viewZeroVpn) _txtHeaderViewTitle.Text = "ZERO-VPN & P2P TÜNEL";
                else if (targetView == _viewModManager) _txtHeaderViewTitle.Text = "MODLAR, YAMA & SDK";
                else if (targetView == _viewTelemetry) _txtHeaderViewTitle.Text = "CANLI TELEMETRİ & BELLEK";
                else if (targetView == _viewOtaShare) _txtHeaderViewTitle.Text = "OTA AĞI & DAĞITIM";
                else if (targetView == _viewSettings) _txtHeaderViewTitle.Text = "SİSTEM AYARLARI";
            }

            Button[] btns = new Button[] { _navBtnLibrary, _navBtnStore, _navBtnDownload, _navBtnZeroVpn, _navBtnModManager, _navBtnTelemetry, _navBtnOtaShare, _navBtnSettings };
            foreach (Button b in btns)
            {
                if (b == null) continue;
                bool isAct = (b == activeBtn);
                b.Tag = isAct ? "ACTIVE" : "INACTIVE";
                b.Background = isAct ? HexBrush("#23193D") : Brushes.Transparent;
                b.Foreground = isAct ? Brushes.White : ThemeManager.ForegroundMutedBrush;
                b.BorderBrush = isAct ? ThemeManager.AccentTealBrush : Brushes.Transparent;
                b.BorderThickness = new Thickness(3, 0, 0, 0);
                b.FontWeight = isAct ? FontWeights.ExtraBold : FontWeights.SemiBold;
            }
        }

        private static ControlTemplate RailNavTemplate()
        {
            ControlTemplate tpl = new ControlTemplate(typeof(Button));

            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Border));
            root.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            root.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            root.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            root.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(cp);

            tpl.VisualTree = root;
            return tpl;
        }

        private Button CreateRailNavButton(string icon, string title, bool active)
        {
            StackPanel contentStack = new StackPanel()
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            contentStack.Children.Add(new TextBlock()
            {
                Text = icon,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            });
            contentStack.Children.Add(new TextBlock()
            {
                Text = LanguageService.T(title).ToUpperInvariant(),
                FontSize = 8.4,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });

            Button btn = new Button()
            {
                Content = contentStack,
                Tag = active ? "ACTIVE" : "INACTIVE",
                Height = 50,
                Padding = new Thickness(2, 4, 2, 4),
                Margin = new Thickness(0, 0, 0, 5),
                Background = active ? HexBrush("#23193D") : Brushes.Transparent,
                Foreground = active ? Brushes.White : ThemeManager.ForegroundMutedBrush,
                BorderBrush = active ? ThemeManager.AccentTealBrush : Brushes.Transparent,
                BorderThickness = new Thickness(3, 0, 0, 0),
                FontWeight = active ? FontWeights.ExtraBold : FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = LanguageService.T(title)
            };
            btn.Template = RailNavTemplate();

            btn.MouseEnter += delegate
            {
                if ((btn.Tag as string) != "ACTIVE")
                {
                    btn.Foreground = Brushes.White;
                    btn.Background = HexBrush("#19122B");
                }
            };
            btn.MouseLeave += delegate
            {
                if ((btn.Tag as string) != "ACTIVE")
                {
                    btn.Foreground = ThemeManager.ForegroundMutedBrush;
                    btn.Background = Brushes.Transparent;
                }
            };

            return btn;
        }

        /// <summary>Steam sekmesi için düz, yuvarlamasız şablon (alt çizgi rengi BorderBrush'tan gelir).</summary>
        private static ControlTemplate NavTabTemplate()
        {
            ControlTemplate tpl = new ControlTemplate(typeof(Button));

            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Border));
            root.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            root.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            root.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            root.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(cp);

            tpl.VisualTree = root;
            return tpl;
        }

        private Button CreateSteamNavButton(string title, bool active)
        {
            // Steam 2024 sekme görünümü: düz zemin, sadece aktif sekmede alt accent çizgisi.
            Button btn = new Button()
            {
                Content = LanguageService.T(title).ToUpperInvariant(),
                Tag = active ? "ACTIVE" : "INACTIVE",
                Height = 40,
                Padding = new Thickness(11, 0, 11, 0),
                Margin = new Thickness(0, 0, 1, 0),
                Background = Brushes.Transparent,
                Foreground = active ? Brushes.White : ThemeManager.ForegroundMutedBrush,
                BorderBrush = active ? ThemeManager.AccentBlueBrush : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2),
                FontSize = 10.5,
                FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            btn.Template = NavTabTemplate();

            btn.MouseEnter += delegate
            {
                if ((btn.Tag as string) != "ACTIVE")
                {
                    btn.Foreground = Brushes.White;
                    btn.Background = ThemeManager.DarkNavyBrush;
                }
            };
            btn.MouseLeave += delegate
            {
                if ((btn.Tag as string) != "ACTIVE")
                {
                    btn.Foreground = ThemeManager.ForegroundMutedBrush;
                    btn.Background = Brushes.Transparent;
                }
            };
            return btn;
        }

        private Button CreateWindowControlButton(string symbol, string bgHex, string fgHex)
        {
            Button btn = new Button()
            {
                Content = symbol,
                Width = 28,
                Height = 24,
                Margin = new Thickness(3, 0, 0, 0),
                Background = HexBrush(bgHex),
                Foreground = HexBrush(fgHex),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btn, 3);
            return btn;
        }

        private static SolidColorBrush HexBrush(string hex)
        {
            try
            {
                return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            }
            catch
            {
                return Brushes.White;
            }
        }

        private static LinearGradientBrush MakeGradient(string topHex, string bottomHex)
        {
            Color c1 = (Color)ColorConverter.ConvertFromString(topHex);
            Color c2 = (Color)ColorConverter.ConvertFromString(bottomHex);
            return new LinearGradientBrush(c1, c2, 90.0);
        }

        private static LinearGradientBrush MakeVerticalGradient(string topHex, string bottomHex)
        {
            Color c1 = (Color)ColorConverter.ConvertFromString(topHex);
            Color c2 = (Color)ColorConverter.ConvertFromString(bottomHex);
            return new LinearGradientBrush(c1, c2, 90.0);
        }

        private static LinearGradientBrush MakeHorizontalGradient(string leftHex, string rightHex)
        {
            Color c1 = (Color)ColorConverter.ConvertFromString(leftHex);
            Color c2 = (Color)ColorConverter.ConvertFromString(rightHex);
            return new LinearGradientBrush(c1, c2, 0.0);
        }

        private static void ApplyRoundedButtonTemplate(Button btn, double radius)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            template.VisualTree = border;
            btn.Template = template;
        }

        private void ShowSaveTimeMachineWindow(GameProfile gp)
        {
            if (gp == null) return;
            Window win = new Window()
            {
                Title = gp.Name + " — Steam // Save Zaman Tüneli & Geri Yükleme Kasası",
                Width = 720,
                Height = 480,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = HexBrush("#141A22"),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                BorderBrush = ThemeManager.BorderAccentBrush,
                BorderThickness = new Thickness(1.5)
            };

            Grid g = new Grid() { Margin = new Thickness(16) };
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Header
            g.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // Snapshot List
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Action Buttons
            win.Content = g;

            // Header
            StackPanel header = new StackPanel() { Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(new TextBlock()
            {
                Text = "🕒 " + gp.Name.ToUpperInvariant() + " — SAVE ZAMAN TÜNELİ",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 14,
                FontWeight = FontWeights.ExtraBold
            });
            header.Children.Add(new TextBlock()
            {
                Text = "Oyun oturumlarının son 10 anlık kopyası saklanır. İstediğiniz slota tek tıkla geri dönebilir veya boss öncesi yeni kayıt alabilirsiniz.",
                Foreground = ThemeManager.ForegroundMutedBrush,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetRow(header, 0);
            g.Children.Add(header);

            // Snapshot List Scroll
            ScrollViewer sv = new ScrollViewer() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12) };
            StackPanel snapStack = new StackPanel();
            sv.Content = snapStack;
            Grid.SetRow(sv, 1);
            g.Children.Add(sv);

            Action refreshSnaps = delegate
            {
                snapStack.Children.Clear();
                List<SaveBackupInfo> list = SaveSyncService.GetGameSnapshots(gp);
                if (list.Count == 0)
                {
                    snapStack.Children.Add(new TextBlock()
                    {
                        Text = "Henüz bu oyun için alınmış bir Save Anlık Görüntüsü bulunmuyor.\nAşağıdaki 'Yeni Anlık Görüntü Al' butonuna basarak ilk kaydı oluşturabilirsiniz.",
                        Foreground = HexBrush("#67707B"),
                        FontSize = 11,
                        Margin = new Thickness(10, 30, 10, 10),
                        TextAlignment = TextAlignment.Center
                    });
                    return;
                }

                foreach (SaveBackupInfo s in list)
                {
                    SaveBackupInfo captured = s;
                    Border b = new Border()
                    {
                        Background = HexBrush("#1E2837"),
                        BorderBrush = ThemeManager.BorderPrimaryBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(12, 8, 12, 8),
                        Margin = new Thickness(0, 0, 0, 6)
                    };

                    Grid rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                    b.Child = rowGrid;

                    StackPanel sInfo = new StackPanel();
                    sInfo.Children.Add(new TextBlock()
                    {
                        Text = captured.FileName,
                        Foreground = HexBrush("#C7D5E0"),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold
                    });
                    sInfo.Children.Add(new TextBlock()
                    {
                        Text = "Dosya Boyutu: " + captured.FormattedSize + " • " + captured.FullPath,
                        Foreground = ThemeManager.AccentTealBrush,
                        FontSize = 9.2,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                    Grid.SetColumn(sInfo, 0);
                    rowGrid.Children.Add(sInfo);

                    Button btnRestoreSlot = new Button()
                    {
                        Content = "⏪ BU KAYDA DÖN",
                        Height = 26,
                        Padding = new Thickness(10, 0, 10, 0),
                        Background = (Brush)MakeGradient("#9333EA", "#6B21A8"),
                        Foreground = Brushes.White,
                        BorderBrush = ThemeManager.AccentTealBrush,
                        BorderThickness = new Thickness(1),
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };
                    ApplyRoundedButtonTemplate(btnRestoreSlot, 3);
                    btnRestoreSlot.Click += delegate
                    {
                        string rMsg;
                        bool ok = SaveSyncService.RestoreSpecificBackup(gp, captured.FullPath, out rMsg);
                        _txtBottomStatus.Text = rMsg;
                        Win32.PlayTechChirp(ok);
                        if (ok) ProceduralSoundEngine.PlayAchievement();
                        MessageBox.Show(rMsg, "Save Geri Yükleme", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
                    };
                    Grid.SetColumn(btnRestoreSlot, 1);
                    rowGrid.Children.Add(btnRestoreSlot);

                    snapStack.Children.Add(b);
                }
            };
            refreshSnaps();

            // Bottom Buttons
            WrapPanel btnPanel = new WrapPanel() { HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(btnPanel, 2);
            g.Children.Add(btnPanel);

            Button btnTakeSnapshot = new Button()
            {
                Content = "💾 YENİ ANLIK GÖRÜNTÜ AL",
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Background = (Brush)MakeGradient("#5DBA2F", "#46941E"),
                Foreground = Brushes.White,
                BorderBrush = HexBrush("#6ED43B"),
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnTakeSnapshot, 3);
            btnTakeSnapshot.Click += delegate
            {
                string sMsg;
                bool ok = SaveSyncService.CreateSessionSnapshot(gp, "Kullanıcı Manuel Kaydı", out sMsg);
                _txtBottomStatus.Text = sMsg;
                Win32.PlayTechChirp(ok);
                if (ok) ProceduralSoundEngine.PlaySelect();
                refreshSnaps();
            };
            btnPanel.Children.Add(btnTakeSnapshot);

            Button btnOpenFolder = new Button()
            {
                Content = "🗁 KASAYI AÇ",
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Background = HexBrush("#241C36"),
                Foreground = HexBrush("#C7D5E0"),
                BorderBrush = ThemeManager.BorderPrimaryBrush,
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnOpenFolder, 3);
            btnOpenFolder.Click += delegate
            {
                try { Process.Start("explorer.exe", SaveSyncService.GetBackupsRoot()); } catch { }
            };
            btnPanel.Children.Add(btnOpenFolder);

            Button btnClose = new Button()
            {
                Content = "✕ KAPAT",
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Background = HexBrush("#3A1D24"),
                Foreground = HexBrush("#F87171"),
                BorderBrush = HexBrush("#7A2A2E"),
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnClose, 3);
            btnClose.Click += delegate { win.Close(); };
            btnPanel.Children.Add(btnClose);

            win.ShowDialog();
        }

        public void ShowCloudServerHubModal(GameProfile gp)
        {
            if (gp == null) return;
            Window win = new Window()
            {
                Title = gp.Name + " — Titan // Bulut & Sunucu Yönetim Merkezi",
                Width = 760,
                Height = 520,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = HexBrush("#111822"),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                BorderBrush = HexBrush("#8B5CF6"),
                BorderThickness = new Thickness(1.5)
            };

            Grid mainGrid = new Grid() { Margin = new Thickness(18) };
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // 3 Mode Cards
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // Close Button
            win.Content = mainGrid;

            // Header
            StackPanel header = new StackPanel() { Margin = new Thickness(0, 0, 0, 14) };
            header.Children.Add(new TextBlock()
            {
                Text = "🌐 " + gp.Name.ToUpperInvariant() + " — BULUT VE SUNUCU YÖNETİM MERKEZİ",
                Foreground = ThemeManager.AccentTealBrush,
                FontSize = 15,
                FontWeight = FontWeights.ExtraBold
            });
            header.Children.Add(new TextBlock()
            {
                Text = "Oyununuzu yerel makinede oynayabilir, 7/24 kesintisiz Dedicated Host olarak çalıştırabilir veya diğer cihazlarınıza buluttan yayınlayabilirsiniz.",
                Foreground = HexBrush("#94A3B8"),
                FontSize = 10.5,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Cards Scroll
            ScrollViewer sv = new ScrollViewer() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 14) };
            StackPanel cardsStack = new StackPanel();
            sv.Content = cardsStack;
            Grid.SetRow(sv, 1);
            mainGrid.Children.Add(sv);

            // CARD 1: Yerel & Spacewar 480 P2P
            Border c1 = CreateCloudModeCard(
                "🎮 1. YEREL OYNA VE P2P CO-OP (İSTEMCİ)",
                "Oyunu bu bilgisayarda doğrudan yüksek performansla başlatır. Intel/AMD P-Core işlemci kilitleri, Standby RAM temizliği ve Spacewar 480 çok oyunculu köprüsü anında devreye girer.",
                "#5DBA2F",
                "▶ YEREL BAŞLAT (P2P CO-OP)",
                delegate
                {
                    win.Close();
                    BtnHeroPrimary_Click(null, null);
                });
            cardsStack.Children.Add(c1);

            // CARD 2: 7/24 Dedicated Server Host
            string serverStatusText = DedicatedServerHostService.IsServerRunning
                ? "🟢 DEDICATED SUNUCU ŞU AN ÇALIŞIYOR"
                : "⚪ SUNUCU KAPALI (1-Tıkla Başlatmaya Hazır)";
            Border c2 = CreateCloudModeCard(
                "🖥️ 2. 7/24 KESİNTİSİZ DEDICATED SUNUCU (HOST)",
                "Palworld, Sons of the Forest, Valheim vb. oyunları arka planda headless konsol olarak 7/24 çalıştırır. Çökerse Watchdog ile anında yeniden başlar. UPnP ile modemde otomatik port açılır.\nDurum: " + serverStatusText,
                "#D9534F",
                DedicatedServerHostService.IsServerRunning ? "🛑 SUNUCUYU DURDUR" : "🖥️ DEDICATED HOST BAŞLAT",
                delegate
                {
                    if (DedicatedServerHostService.IsServerRunning)
                    {
                        DedicatedServerHostService.StopDedicatedServer();
                        _txtBottomStatus.Text = "Dedicated Server durduruldu.";
                        win.Close();
                    }
                    else
                    {
                        string sMsg;
                        DedicatedServerHostService.StartDedicatedServer(gp, "", out sMsg);
                        _txtBottomStatus.Text = sMsg;
                        win.Close();
                    }
                    Win32.PlayTechChirp(true);
                });
            cardsStack.Children.Add(c2);

            // CARD 3: Cloud Stream & Remote Play
            string streamStatusText = LocalStreamHostService.IsRunning
                ? ("🟢 YAYIN AKTİF: http://" + UpnpPortForwarderService.LocalIpAddress + ":" + LocalStreamHostService.Port)
                : "⚪ YAYIN KAPALI (WebRTC / Moonlight 60-120 FPS)";
            Border c3 = CreateCloudModeCard(
                "☁️ 3. TITAN CLOUD STREAM & REMOTE PLAY (UZAKTAN OYNA)",
                "Bu bilgisayarı donanım hızlandırmalı bir bulut oyun sunucusuna çevirir. Aynı Wi-Fi veya uzaktan telefon, tablet ya da laptopunuzun tarayıcısından hiçbir program kurmadan 60 FPS oynamanızı sağlar.\n" + streamStatusText,
                "#C084FC",
                LocalStreamHostService.IsRunning ? "🛑 YAYINI DURDUR" : "📡 BULUT YAYININI BAŞLAT",
                delegate
                {
                    if (!LocalStreamHostService.IsRunning)
                    {
                        string streamUrl;
                        bool ok = LocalStreamHostService.StartStream(48085, out streamUrl);
                        _txtBottomStatus.Text = ok ? ("📡 Titan Bulut Yayında: " + streamUrl) : streamUrl;
                        if (ok)
                        {
                            try { Clipboard.SetText(streamUrl); } catch { }
                            MessageBox.Show("Bulut Yayın Sunucusu Aktif!\n\nBağlantı Adresi (Panoya Kopyalandı):\n" + streamUrl + "\n\nTelefon veya tabletinizin tarayıcısından bu adresi açarak oyunu hemen oynayabilirsiniz.", "Titan Cloud Stream", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        LocalStreamHostService.StopStream();
                        _txtBottomStatus.Text = "Bulut yayını durduruldu.";
                    }
                    win.Close();
                    Win32.PlayTechChirp(true);
                });
            cardsStack.Children.Add(c3);

            // Close button
            Button btnClose = new Button()
            {
                Content = "KAPAT",
                Height = 32,
                Width = 100,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = HexBrush("#221B33"),
                Foreground = HexBrush("#CBD5E1"),
                BorderBrush = HexBrush("#334155"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnClose, 3);
            btnClose.Click += delegate { win.Close(); };
            Grid.SetRow(btnClose, 2);
            mainGrid.Children.Add(btnClose);

            win.ShowDialog();
        }

        private Border CreateCloudModeCard(string title, string desc, string accentHex, string actionBtnText, RoutedEventHandler onAction)
        {
            Border card = new Border()
            {
                Background = HexBrush("#182232"),
                BorderBrush = HexBrush("#2A3C52"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10)
            };

            Grid cg = new Grid();
            cg.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            cg.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            card.Child = cg;

            StackPanel info = new StackPanel();
            info.Children.Add(new TextBlock()
            {
                Text = title,
                Foreground = HexBrush(accentHex),
                FontSize = 12.5,
                FontWeight = FontWeights.Bold
            });
            info.Children.Add(new TextBlock()
            {
                Text = desc,
                Foreground = HexBrush("#94A3B8"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 12, 0)
            });
            Grid.SetColumn(info, 0);
            cg.Children.Add(info);

            Button btnAct = new Button()
            {
                Content = actionBtnText,
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = MakeGradient(accentHex, "#221B33"),
                Foreground = Brushes.White,
                BorderBrush = HexBrush(accentHex),
                BorderThickness = new Thickness(1),
                FontSize = 10.5,
                FontWeight = FontWeights.ExtraBold,
                Cursor = Cursors.Hand
            };
            ApplyRoundedButtonTemplate(btnAct, 3);
            btnAct.Click += onAction;
            Grid.SetColumn(btnAct, 1);
            cg.Children.Add(btnAct);

            return card;
        }
    }
}
