using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TitanGameHub
{
    public class ControllerOsdKeyboard : Window
    {
        private static ControllerOsdKeyboard _instance;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;

        public static bool IsOpen
        {
            get { return _instance != null && _instance.IsVisible; }
        }

        public static void ToggleKeyboard()
        {
            if (_instance != null && _instance.IsVisible)
            {
                _instance.Hide();
                return;
            }

            if (_instance == null)
            {
                _instance = new ControllerOsdKeyboard();
            }

            _instance.Show();
            _instance.Activate();
        }

        public ControllerOsdKeyboard()
        {
            this.Title = "Steam Big Picture Controller Keyboard";
            this.Width = 680;
            this.Height = 240;
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = new SolidColorBrush(Color.FromArgb(240, 14, 20, 27));
            this.Topmost = true;
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = (SystemParameters.WorkArea.Width - 680) / 2;
            this.Top = SystemParameters.WorkArea.Height - 270;
            this.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90));
            this.BorderThickness = new Thickness(1.5);

            Grid main = new Grid();
            main.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(30) });
            main.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            this.Content = main;

            // Header Bar
            Border header = new Border()
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 29, 41)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 0, 10, 0)
            };
            Grid.SetRow(header, 0);
            main.Children.Add(header);

            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
            };

            Grid hGrid = new Grid();
            hGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            hGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            header.Child = hGrid;

            TextBlock title = new TextBlock()
            {
                Text = "🎮 STEAM BIG PICTURE OSD KLAVYE [F9]",
                Foreground = new SolidColorBrush(Color.FromRgb(102, 192, 244)),
                FontSize = 9.8,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            hGrid.Children.Add(title);

            Button btnClose = new Button()
            {
                Content = "✕",
                Width = 20,
                Height = 20,
                Background = new SolidColorBrush(Color.FromRgb(36, 20, 22)),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(122, 42, 46)),
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnClose.Click += delegate { this.Hide(); };
            Grid.SetColumn(btnClose, 1);
            hGrid.Children.Add(btnClose);

            // Keyboard Keys Grid
            StackPanel kbStack = new StackPanel() { Margin = new Thickness(12, 10, 12, 10) };
            Grid.SetRow(kbStack, 1);
            main.Children.Add(kbStack);

            string[] row1 = new string[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "BACK" };
            string[] row2 = new string[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]" };
            string[] row3 = new string[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'", "ENTER" };
            string[] row4 = new string[] { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "SPACE" };

            kbStack.Children.Add(CreateKeyRow(row1));
            kbStack.Children.Add(CreateKeyRow(row2));
            kbStack.Children.Add(CreateKeyRow(row3));
            kbStack.Children.Add(CreateKeyRow(row4));
        }

        private StackPanel CreateKeyRow(string[] keys)
        {
            StackPanel row = new StackPanel() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5) };
            foreach (string k in keys)
            {
                string keyLabel = k;
                Button btn = new Button()
                {
                    Content = keyLabel,
                    Height = 34,
                    MinWidth = (keyLabel == "SPACE") ? 180 : ((keyLabel == "ENTER" || keyLabel == "BACK") ? 64 : 42),
                    Margin = new Thickness(2, 0, 2, 0),
                    Background = new SolidColorBrush(Color.FromRgb(27, 40, 56)),
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 222, 223)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 90)),
                    BorderThickness = new Thickness(1),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                btn.Click += delegate
                {
                    SendKeySimulation(keyLabel);
                };
                row.Children.Add(btn);
            }
            return row;
        }

        private void SendKeySimulation(string key)
        {
            ProceduralSoundEngine.PlaySelect();
            if (key == "SPACE")
            {
                SendVk(0x20);
            }
            else if (key == "BACK")
            {
                SendVk(0x08);
            }
            else if (key == "ENTER")
            {
                SendVk(0x0D);
            }
            else if (key.Length == 1)
            {
                char c = key[0];
                byte vk = (byte)c;
                SendVk(vk);
            }
        }

        private void SendVk(byte vk)
        {
            try
            {
                keybd_event(vk, 0, 0, UIntPtr.Zero);
                keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }
    }
}
