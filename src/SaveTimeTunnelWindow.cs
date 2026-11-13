using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TitanGameHub
{
    /// <summary>
    /// Save Zaman Tüneli: oyunun kayıt yedeklerini (snapshot + oturum sonu) listeler,
    /// seçilen yedeği tek tıkla geri yükler. Steam'in "bulut kayıt geçmişi" muadili.
    /// </summary>
    public static class SaveTimeTunnelWindow
    {
        public static void Show(Window owner, GameProfile gp)
        {
            if (gp == null) return;

            Window win = new Window()
            {
                Title = "Save Zaman Tüneli — " + gp.Name,
                Width = 760,
                Height = 520,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B2838"))
            };

            Grid root = new Grid() { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            win.Content = root;

            TextBlock header = new TextBlock()
            {
                Text = "KAYIT YEDEKLERİ (en yeniden eskiye)",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66C0F4")),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            StackPanel list = new StackPanel();
            ScrollViewer scroll = new ScrollViewer()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            List<SaveBackupInfo> backups = new List<SaveBackupInfo>();
            try { backups.AddRange(SaveSyncService.GetBackupsList(gp)); } catch { }
            try
            {
                foreach (SaveBackupInfo snap in SaveSyncService.GetGameSnapshots(gp))
                    if (!backups.Exists(delegate(SaveBackupInfo b) { return b.FullPath == snap.FullPath; }))
                        backups.Add(snap);
            }
            catch { }
            backups.Sort(delegate(SaveBackupInfo a, SaveBackupInfo b) { return b.Timestamp.CompareTo(a.Timestamp); });

            if (backups.Count == 0)
            {
                list.Children.Add(new TextBlock()
                {
                    Text = "Henüz kayıt yedeği yok. Oyunu bir kez açıp kapattığınızda otomatik yedek oluşur.",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F98A0")),
                    FontSize = 11.5,
                    Margin = new Thickness(4, 8, 4, 8)
                });
            }

            foreach (SaveBackupInfo info in backups)
            {
                Border row = new Border()
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141D2B")),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                Grid rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

                StackPanel text = new StackPanel();
                text.Children.Add(new TextBlock()
                {
                    Text = info.Timestamp.ToString("dd.MM.yyyy HH:mm") + "   •   " + info.FormattedSize,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold
                });
                text.Children.Add(new TextBlock()
                {
                    Text = info.FileName,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8F98A0")),
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(text, 0);
                rowGrid.Children.Add(text);

                string capturedPath = info.FullPath;
                Button btnRestore = new Button()
                {
                    Content = "↩ BU YEDEĞE DÖN",
                    Width = 150,
                    Height = 28,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A9FFF")),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 9.8,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                btnRestore.Click += delegate
                {
                    MessageBoxResult ok = MessageBox.Show(
                        "Kayıt bu yedeğe geri alınacak. Mevcut kayıt önce yedeklenir.\n\nDevam edilsin mi?",
                        "Save Zaman Tüneli", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (ok != MessageBoxResult.Yes) return;

                    string msg;
                    bool done = SaveSyncService.RestoreSpecificBackup(gp, capturedPath, out msg);
                    MessageBox.Show(msg, done ? "Geri Yüklendi" : "Hata",
                        MessageBoxButton.OK, done ? MessageBoxImage.Information : MessageBoxImage.Warning);
                    if (done) win.Close();
                };
                Grid.SetColumn(btnRestore, 1);
                rowGrid.Children.Add(btnRestore);

                row.Child = rowGrid;
                list.Children.Add(row);
            }

            StackPanel buttons = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Button btnSnap = new Button()
            {
                Content = "📸 ŞİMDİ SNAPSHOT AL",
                Width = 190,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5DBA2F")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnSnap.Click += delegate
            {
                string msg;
                bool ok = SaveSyncService.CreateSessionSnapshot(gp, "Manuel snapshot", out msg);
                MessageBox.Show(msg, "Snapshot", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                if (ok) { win.Close(); Show(owner, gp); }
            };
            buttons.Children.Add(btnSnap);

            Button btnClose = new Button()
            {
                Content = "KAPAT",
                Width = 110,
                Height = 30,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A3F5A")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btnClose.Click += delegate { win.Close(); };
            buttons.Children.Add(btnClose);

            win.ShowDialog();
        }
    }
}
