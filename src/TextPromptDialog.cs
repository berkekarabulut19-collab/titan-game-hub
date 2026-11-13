using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TitanGameHub
{
    /// <summary>Steam tarzı basit metin giriş penceresi (URL yapıştırma, isim sorma vb.).</summary>
    public static class TextPromptDialog
    {
        public static string Prompt(Window owner, string title, string label, string initialValue)
        {
            Window win = new Window()
            {
                Title = title,
                Width = 620,
                Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B2838"))
            };

            Grid root = new Grid() { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            win.Content = root;

            TextBlock lbl = new TextBlock()
            {
                Text = label,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C7D5E0")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(lbl, 0);
            root.Children.Add(lbl);

            TextBox input = new TextBox()
            {
                Text = initialValue ?? "",
                Height = 32,
                FontSize = 12.5,
                Padding = new Thickness(8, 5, 8, 5),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E141B")),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A475E")),
                BorderThickness = new Thickness(1),
                CaretBrush = Brushes.White
            };
            Grid.SetRow(input, 1);
            root.Children.Add(input);

            StackPanel buttons = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Button cancel = new Button()
            {
                Content = "VAZGEÇ",
                Width = 110,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A3F5A")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            cancel.Click += delegate { win.DialogResult = false; win.Close(); };
            buttons.Children.Add(cancel);

            Button ok = new Button()
            {
                Content = "EKLE",
                Width = 130,
                Height = 30,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A9FFF")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            ok.Click += delegate { win.DialogResult = true; win.Close(); };
            buttons.Children.Add(ok);

            win.Loaded += delegate { input.Focus(); input.SelectAll(); };
            input.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { win.DialogResult = true; win.Close(); }
                else if (e.Key == Key.Escape) { win.DialogResult = false; win.Close(); }
            };

            bool? result = win.ShowDialog();
            if (result == true && !string.IsNullOrWhiteSpace(input.Text))
                return input.Text.Trim();

            return null;
        }
    }
}
