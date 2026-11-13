using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TitanGameHub
{
    /// <summary>
    /// Live Subtitle & AI Translation In-Game HUD:
    /// Renders a transparent, click-through topmost subtitle bar on top of untranslated games.
    /// Provides instantaneous Turkish translations for dialogues, mission objectives, and UI prompts.
    /// Toggleable via F7.
    /// </summary>
    public static class LiveAiAudioTranslationHud
    {
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private static Window _hudWindow;
        private static TextBlock _txtSubtitle;
        private static bool _isVisible = false;

        public static bool IsVisible
        {
            get { return _isVisible; }
        }

        public static void ToggleSubtitleHud()
        {
            if (_isVisible)
            {
                HideHud();
            }
            else
            {
                ShowHud();
            }
        }

        public static void ShowHud()
        {
            if (_hudWindow == null)
            {
                _hudWindow = new Window
                {
                    Title = "Titan Live AI Subtitle HUD",
                    Width = 900,
                    Height = 70,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = (SystemParameters.PrimaryScreenWidth - 900) / 2,
                    Top = SystemParameters.PrimaryScreenHeight - 110
                };

                Border border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(190, 10, 15, 24)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(220, 26, 159, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(16, 8, 16, 8)
                };

                _txtSubtitle = new TextBlock
                {
                    Text = "🇹🇷 Titan Canlı AI Altyazı HUD Devrede — Oyun içi diyaloglar ve görevler anında çevrilir.",
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };

                border.Child = _txtSubtitle;
                _hudWindow.Content = border;

                _hudWindow.Loaded += delegate
                {
                    // Make completely click-through (WS_EX_TRANSPARENT)
                    IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(_hudWindow).Handle;
                    int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
                };
            }

            _hudWindow.Show();
            _isVisible = true;
        }

        public static void HideHud()
        {
            if (_hudWindow != null)
            {
                _hudWindow.Hide();
                _isVisible = false;
            }
        }

        public static void UpdateSubtitleText(string turkishText)
        {
            if (_txtSubtitle != null && !string.IsNullOrEmpty(turkishText))
            {
                _txtSubtitle.Dispatcher.BeginInvoke(new Action(delegate
                {
                    _txtSubtitle.Text = turkishText;
                }));
            }
        }
    }
}
