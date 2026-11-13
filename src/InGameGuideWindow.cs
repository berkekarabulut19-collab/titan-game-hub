using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TitanGameHub
{
    public class InGameGuideWindow : Window
    {
        private static InGameGuideWindow _instance;
        private WebBrowser _browser;
        private TextBox _txtUrl;
        private string _activeGameName;

        public static bool IsOpen
        {
            get { return _instance != null && _instance.IsVisible; }
        }

        public static void ToggleGuide(string gameName)
        {
            if (_instance != null && _instance.IsVisible)
            {
                _instance.Hide();
                return;
            }

            if (_instance == null)
            {
                _instance = new InGameGuideWindow();
            }

            _instance.NavigateToGameGuide(gameName);
            _instance.Show();
            _instance.Activate();
        }

        public InGameGuideWindow()
        {
            this.Title = "Steam In-Game Guide & Wiki Hub";
            this.Width = 920;
            this.Height = 640;
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.CanResizeWithGrip;
            this.Background = new SolidColorBrush(Color.FromRgb(20, 29, 41));
            this.Topmost = true;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(38) });
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            this.Content = mainGrid;

            // Header Navigation Bar
            Border headerBorder = new Border()
            {
                Background = new SolidColorBrush(Color.FromRgb(14, 20, 27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 4, 10, 4)
            };
            Grid.SetRow(headerBorder, 0);
            mainGrid.Children.Add(headerBorder);

            headerBorder.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
            };

            Grid navGrid = new Grid();
            navGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            navGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            navGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            headerBorder.Child = navGrid;

            StackPanel leftControls = new StackPanel() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(leftControls, 0);
            navGrid.Children.Add(leftControls);

            Button btnBack = CreateNavBtn("◀", "Geri");
            btnBack.Click += delegate { if (_browser.CanGoBack) _browser.GoBack(); };
            leftControls.Children.Add(btnBack);

            Button btnForward = CreateNavBtn("▶", "İleri");
            btnForward.Click += delegate { if (_browser.CanGoForward) _browser.GoForward(); };
            leftControls.Children.Add(btnForward);

            Button btnRefresh = CreateNavBtn("🔄", "Yenile");
            btnRefresh.Click += delegate { _browser.Refresh(); };
            leftControls.Children.Add(btnRefresh);

            _txtUrl = new TextBox()
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 14, 20)),
                Foreground = new SolidColorBrush(Color.FromRgb(102, 192, 244)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90)),
                BorderThickness = new Thickness(1),
                FontSize = 11,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _txtUrl.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) NavigateUrl(_txtUrl.Text);
            };
            Grid.SetColumn(_txtUrl, 1);
            navGrid.Children.Add(_txtUrl);

            StackPanel presetsStack = new StackPanel() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(presetsStack, 2);
            navGrid.Children.Add(presetsStack);

            Button btnMapGenie = CreatePresetBtn("🗺 Harita", "https://mapgenie.io");
            presetsStack.Children.Add(btnMapGenie);

            Button btnWiki = CreatePresetBtn("📖 Wiki", "https://www.google.com/search?q=" + Uri.EscapeDataString((_activeGameName ?? "Steam Game") + " wiki guide"));
            presetsStack.Children.Add(btnWiki);

            Button btnClose = new Button()
            {
                Content = "✕",
                Width = 26,
                Height = 26,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(36, 20, 22)),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(122, 42, 46)),
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnClose.Click += delegate { this.Hide(); };
            presetsStack.Children.Add(btnClose);

            // WebBrowser Host
            _browser = new WebBrowser();
            _browser.Navigated += delegate(object sender, System.Windows.Navigation.NavigationEventArgs e)
            {
                if (e.Uri != null) _txtUrl.Text = e.Uri.ToString();
            };
            Grid.SetRow(_browser, 1);
            mainGrid.Children.Add(_browser);
        }

        public void NavigateToGameGuide(string gameName)
        {
            _activeGameName = gameName;
            string targetUrl = !string.IsNullOrEmpty(gameName)
                ? "https://www.google.com/search?q=" + Uri.EscapeDataString(gameName + " guide walkthrough map")
                : "https://mapgenie.io";
            NavigateUrl(targetUrl);
        }

        private void NavigateUrl(string url)
        {
            try
            {
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }
                _txtUrl.Text = url;
                _browser.Navigate(new Uri(url));
            }
            catch { }
        }

        private Button CreateNavBtn(string text, string tip)
        {
            return new Button()
            {
                Content = text,
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromRgb(27, 40, 56)),
                Foreground = new SolidColorBrush(Color.FromRgb(102, 192, 244)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90)),
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                ToolTip = tip,
                Cursor = Cursors.Hand
            };
        }

        private Button CreatePresetBtn(string text, string targetUrl)
        {
            Button btn = new Button()
            {
                Content = text,
                Height = 26,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromRgb(27, 40, 56)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(102, 192, 244)),
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btn.Click += delegate
            {
                NavigateUrl(targetUrl);
            };
            return btn;
        }
    }
}
