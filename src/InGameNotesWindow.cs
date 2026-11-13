using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TitanGameHub
{
    public class InGameNotesWindow : Window
    {
        private static InGameNotesWindow _instance;
        private TextBox _txtNotes;
        private Slider _sliderOpacity;
        private Button _btnPin;
        private bool _isPinned = false;
        private string _activeGameName;
        private DispatcherTimer _saveDebounceTimer;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        public static bool IsOpen
        {
            get { return _instance != null && _instance.IsVisible; }
        }

        public static void ToggleNotes(string gameName)
        {
            if (_instance != null && _instance.IsVisible)
            {
                if (_instance._isPinned)
                {
                    _instance.SetPinnedState(false);
                }
                _instance.Hide();
                return;
            }

            if (_instance == null)
            {
                _instance = new InGameNotesWindow();
            }

            _instance.LoadGameNotes(gameName);
            _instance.Show();
            _instance.Activate();
        }

        public InGameNotesWindow()
        {
            this.Title = "Steam In-Game Pinned Notes";
            this.Width = 460;
            this.Height = 380;
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.CanResizeWithGrip;
            this.Background = new SolidColorBrush(Color.FromArgb(235, 14, 20, 27));
            this.Topmost = true;
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = SystemParameters.WorkArea.Width - 490;
            this.Top = 60;
            this.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90));
            this.BorderThickness = new Thickness(1.5);

            _saveDebounceTimer = new DispatcherTimer();
            _saveDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
            _saveDebounceTimer.Tick += delegate
            {
                _saveDebounceTimer.Stop();
                SaveCurrentNotes();
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(34) }); // Title/Control Bar
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // Editor
            mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(28) }); // Status bar
            this.Content = mainGrid;

            // Header Bar
            Border header = new Border()
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 29, 41)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 0, 8, 0)
            };
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed && !_isPinned) this.DragMove();
            };

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            header.Child = headerGrid;

            StackPanel titleStack = new StackPanel() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock()
            {
                Text = "📝 STEAM NOT DEFTERİ [F8]",
                Foreground = new SolidColorBrush(Color.FromRgb(102, 192, 244)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            StackPanel controls = new StackPanel() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(controls, 1);
            headerGrid.Children.Add(controls);

            // Opacity Label & Slider
            controls.Children.Add(new TextBlock()
            {
                Text = "Saydamlık:",
                Foreground = new SolidColorBrush(Color.FromRgb(143, 152, 160)),
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });

            _sliderOpacity = new Slider()
            {
                Minimum = 0.25,
                Maximum = 1.0,
                Value = 0.92,
                Width = 70,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _sliderOpacity.ValueChanged += delegate
            {
                this.Opacity = _sliderOpacity.Value;
            };
            controls.Children.Add(_sliderOpacity);

            // Pin Button
            _btnPin = new Button()
            {
                Content = "📌 SABİTLE",
                Height = 22,
                Padding = new Thickness(6, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(27, 40, 56)),
                Foreground = new SolidColorBrush(Color.FromRgb(102, 192, 244)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90)),
                BorderThickness = new Thickness(1),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Pencereyi oyun üstüne sabitler ve tıklamaları arkadaki oyuna geçirir (Click-Through). Kapatmak için [F8] basın."
            };
            _btnPin.Click += delegate
            {
                SetPinnedState(!_isPinned);
            };
            controls.Children.Add(_btnPin);

            // Close Button
            Button btnClose = new Button()
            {
                Content = "✕",
                Width = 22,
                Height = 22,
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(36, 20, 22)),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(122, 42, 46)),
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnClose.Click += delegate { this.Hide(); };
            controls.Children.Add(btnClose);

            // Text Editor
            _txtNotes = new TextBox()
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 10, 14, 20)),
                Foreground = new SolidColorBrush(Color.FromRgb(220, 222, 223)),
                CaretBrush = new SolidColorBrush(Color.FromRgb(102, 192, 244)),
                BorderThickness = new Thickness(0),
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 11,
                Padding = new Thickness(10)
            };
            _txtNotes.TextChanged += delegate
            {
                _saveDebounceTimer.Stop();
                _saveDebounceTimer.Start();
            };
            Grid.SetRow(_txtNotes, 1);
            mainGrid.Children.Add(_txtNotes);

            // Footer Bar
            Border footer = new Border()
            {
                Background = new SolidColorBrush(Color.FromRgb(14, 20, 27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(8, 0, 8, 0)
            };
            Grid.SetRow(footer, 2);
            mainGrid.Children.Add(footer);

            TextBlock txtFooter = new TextBlock()
            {
                Text = "✔ Otomatik kaydedilir • Oyun şifreleri, görev ipuçları ve koordinatlar için hazır",
                Foreground = new SolidColorBrush(Color.FromRgb(103, 112, 123)),
                FontSize = 8.8,
                VerticalAlignment = VerticalAlignment.Center
            };
            footer.Child = txtFooter;
        }

        private void SetPinnedState(bool pin)
        {
            _isPinned = pin;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (_isPinned)
            {
                _btnPin.Content = "🟢 SABİTLENDİ";
                _btnPin.Foreground = new SolidColorBrush(Color.FromRgb(93, 186, 47));
                this.BorderBrush = new SolidColorBrush(Color.FromRgb(93, 186, 47));
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            }
            else
            {
                _btnPin.Content = "📌 SABİTLE";
                _btnPin.Foreground = new SolidColorBrush(Color.FromRgb(102, 192, 244));
                this.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90));
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
            }
        }

        public void LoadGameNotes(string gameName)
        {
            _activeGameName = gameName;
            string path = GetNotesFilePath(gameName);
            if (File.Exists(path))
            {
                try
                {
                    _txtNotes.Text = File.ReadAllText(path);
                }
                catch { }
            }
            else
            {
                _txtNotes.Text = string.Format("# {0} — Notlar\n- Görev Adımları:\n- Şifreler / Kodlar:\n- Rota Koordinatları:\n", gameName ?? "Genel");
            }
        }

        private void SaveCurrentNotes()
        {
            if (string.IsNullOrEmpty(_activeGameName)) return;
            try
            {
                string path = GetNotesFilePath(_activeGameName);
                string folder = Path.GetDirectoryName(path);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(path, _txtNotes.Text ?? "");
            }
            catch { }
        }

        private static string GetNotesFilePath(string gameName)
        {
            string safe = Regex.Replace(gameName ?? "General", @"[^\w\s-]", "").Trim().Replace(" ", "_");
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Notes", safe + "_notes.txt");
        }
    }
}
