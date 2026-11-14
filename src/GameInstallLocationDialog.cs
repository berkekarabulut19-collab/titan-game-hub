using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TitanGameHub
{
    public static class GameInstallLocationDialog
    {
        private class LibraryChoice
        {
            public string RootPath;
            public string Display;
            public override string ToString() { return Display; }
        }

        public static string PromptInstallDirectory(Window owner, GameProfile profile)
        {
            if (profile == null) return null;

            string safeName = Regex.Replace(profile.Name ?? "Game", @"[^\w\s-]", "").Trim();
            if (string.IsNullOrEmpty(safeName)) safeName = "Game";

            try
            {
                return PromptInstallDirectoryCore(owner, profile, safeName);
            }
            catch
            {
                try
                {
                    return PromptInstallDirectoryFallback(owner, profile, safeName);
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string PromptInstallDirectoryFallback(Window owner, GameProfile profile, string safeName)
        {
            try
            {
                string initial = null;
                try { initial = DownloadInstallService.GetEffectiveGamesRoot(); } catch { }
                if (string.IsNullOrWhiteSpace(initial) || !Directory.Exists(initial))
                {
                    try { initial = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } catch { }
                }

                using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
                {
                    fbd.Description = "Kurulum kütüphane klasörünü seçin. Seçilen klasörün içine '" + safeName + "' klasörü oluşturulup kurulum oraya yapılacak.";
                    fbd.ShowNewFolderButton = true;
                    try { if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) fbd.SelectedPath = initial; } catch { }

                    System.Windows.Forms.DialogResult dr;
                    if (owner != null)
                    {
                        try { dr = fbd.ShowDialog(new Win32Window(owner)); }
                        catch { dr = fbd.ShowDialog(); }
                    }
                    else
                    {
                        dr = fbd.ShowDialog();
                    }

                    if (dr != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(fbd.SelectedPath))
                        return null;

                    string target = Path.Combine(fbd.SelectedPath, safeName);
                    try { Directory.CreateDirectory(target); } catch { }
                    try { DownloadInstallService.CustomGamesRootDirectory = fbd.SelectedPath; } catch { }
                    try { GamePathResolver.InvalidateCache(); } catch { }
                    return target;
                }
            }
            catch
            {
                return null;
            }
        }

        private sealed class Win32Window : System.Windows.Forms.IWin32Window
        {
            private readonly IntPtr _handle;
            public Win32Window(Window window)
            {
                try
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(window);
                    _handle = helper.Handle;
                }
                catch { _handle = IntPtr.Zero; }
            }
            public IntPtr Handle { get { return _handle; } }
        }

        private static string PromptInstallDirectoryCore(Window owner, GameProfile profile, string safeName)
        {
            string selectedPath = null;
            bool confirmed = false;

            Window win = new Window
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 620,
                Height = 430,
                Title = "Kurulum Konumu Seçin — " + profile.Name,
                Background = HexBrush("#0E141B"),
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                FontFamily = new FontFamily("Segoe UI")
            };

            Grid root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // library selector
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // target path
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // info
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons
            win.Content = root;

            // Row 0: Header
            StackPanel header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            header.Children.Add(new TextBlock
            {
                Text = profile.Name.ToUpperInvariant() + " — KURULUM KONUMU",
                FontSize = 14,
                FontWeight = FontWeights.ExtraBold,
                Foreground = HexBrush("#1A9FFF")
            });
            header.Children.Add(new TextBlock
            {
                Text = "Oyunun kurulacağı kütüphane klasörünü seçin (Steam'deki gibi). Oyun, seçilen kütüphane içinde kendi klasörüne kurulacaktır.",
                FontSize = 10.5,
                Foreground = HexBrush("#8F98A0"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // Row 1: Library selector
            Border selBorder = new Border
            {
                Background = HexBrush("#121820"),
                BorderBrush = HexBrush("#1F2937"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(selBorder, 1);
            root.Children.Add(selBorder);

            Grid selGrid = new Grid();
            selGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            selGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selBorder.Child = selGrid;

            TextBlock lblLib = new TextBlock
            {
                Text = "Kütüphane klasörü (Library):",
                FontSize = 10.5,
                Foreground = HexBrush("#DCDEDF"),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(lblLib, 0);
            Grid.SetColumnSpan(lblLib, 2);
            selGrid.Children.Add(lblLib);

            ComboBox cmbLibraries = new ComboBox
            {
                Height = 30,
                FontSize = 10.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0)
            };

            // Dark-theme friendly dropdown items
            try
            {
                Style itemStyle = new Style(typeof(ComboBoxItem));
                itemStyle.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, HexBrush("#141D2B")));
                itemStyle.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, Brushes.White));
                itemStyle.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(6, 4, 6, 4)));
                cmbLibraries.ItemContainerStyle = itemStyle;
                cmbLibraries.Resources[SystemColors.WindowBrushKey] = HexBrush("#141D2B");
                cmbLibraries.Resources[SystemColors.HighlightBrushKey] = HexBrush("#1A9FFF");
                cmbLibraries.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
            }
            catch { }
            Grid.SetRow(cmbLibraries, 1);
            Grid.SetColumn(cmbLibraries, 0);
            selGrid.Children.Add(cmbLibraries);

            Button btnAddLibrary = new Button
            {
                Content = "+ Klasör Ekle",
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Background = HexBrush("#1E293B"),
                Foreground = HexBrush("#60A5FA"),
                BorderBrush = HexBrush("#3B82F6"),
                BorderThickness = new Thickness(1),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetRow(btnAddLibrary, 1);
            Grid.SetColumn(btnAddLibrary, 1);
            selGrid.Children.Add(btnAddLibrary);

            // Row 2: Final target path display
            Border pathBorder = new Border
            {
                Background = HexBrush("#0B1118"),
                BorderBrush = HexBrush("#23344D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(pathBorder, 2);
            root.Children.Add(pathBorder);

            StackPanel pathStack = new StackPanel();
            pathStack.Children.Add(new TextBlock
            {
                Text = "Kurulum klasörü (oyun alt klasörü):",
                FontSize = 9.5,
                Foreground = HexBrush("#8F98A0"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            TextBlock tbTargetPath = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = HexBrush("#E5E7EB"),
                TextWrapping = TextWrapping.Wrap
            };
            pathStack.Children.Add(tbTargetPath);
            pathBorder.Child = pathStack;

            // Row 3: Info card
            Border infoCard = new Border
            {
                Background = HexBrush("#121820"),
                BorderBrush = HexBrush("#1F2937"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(infoCard, 3);
            root.Children.Add(infoCard);

            TextBlock tbDiskInfo = new TextBlock
            {
                FontSize = 10.5,
                Foreground = HexBrush("#DCDEDF"),
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap
            };
            infoCard.Child = tbDiskInfo;

            // Build library choices
            List<LibraryChoice> choices = BuildLibraryChoices(profile, safeName);

            Action refreshFromSelection = delegate
            {
                try
                {
                    LibraryChoice choice = cmbLibraries.SelectedItem as LibraryChoice;
                    string rootPath = (choice != null) ? choice.RootPath : null;
                    if (string.IsNullOrEmpty(rootPath))
                    {
                        tbTargetPath.Text = "-";
                        tbDiskInfo.Text = "Lütfen bir kütüphane klasörü seçin veya ekleyin.";
                        return;
                    }

                    string target = Path.Combine(rootPath, safeName);
                    tbTargetPath.Text = target;

                    string driveRoot = null;
                    try { driveRoot = Path.GetPathRoot(target); } catch { }
                    if (!string.IsNullOrEmpty(driveRoot))
                    {
                        try
                        {
                            DriveInfo d = new DriveInfo(driveRoot);
                            if (d.IsReady)
                            {
                                double freeGb = d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                                double totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                                double pkgGb = EstimatePackageGb(profile.FileSize);
                                double recGb = pkgGb > 0 ? pkgGb * 1.8 : 0;
                                string warnLine = (pkgGb > 0 && freeGb < recGb)
                                    ? string.Format("\n⚠️ UYARI: Arşiv+kurulum için ~{0:0.0} GB önerilir (Boş: {1:0.0} GB)!", recGb, freeGb)
                                    : "";
                                tbDiskInfo.Text = string.Format(
                                    "• Hedef sürücü: {0}  ({1:0.0} GB boş / {2:0.0} GB toplam)\n" +
                                    "• İndirilecek paket: {3}\n" +
                                    "• Kütüphane: {4}\n" +
                                    "• Çok oyunculu: Spacewar (AppID 480) & Goldberg LAN kalkanı dahil.{5}",
                                    driveRoot, freeGb, totalGb, profile.FileSize ?? "Hazır", rootPath, warnLine);
                                return;
                            }
                        }
                        catch { }
                    }

                    tbDiskInfo.Text = string.Format(
                        "• Kütüphane: {0}\n• İndirilecek paket: {1}\n• Disk bilgisi alınamadı (sürücü hazır olmayabilir).",
                        rootPath, profile.FileSize ?? "Hazır");
                }
                catch
                {
                    tbDiskInfo.Text = "• Disk alanı kontrolü hazır.";
                }
            };

            foreach (LibraryChoice c in choices) cmbLibraries.Items.Add(c);
            if (cmbLibraries.Items.Count > 0) cmbLibraries.SelectedIndex = 0;
            cmbLibraries.SelectionChanged += delegate { refreshFromSelection(); };
            refreshFromSelection();

            btnAddLibrary.Click += delegate
            {
                try
                {
                    using (System.Windows.Forms.FolderBrowserDialog fbd = new System.Windows.Forms.FolderBrowserDialog())
                    {
                        fbd.Description = "Kütüphane klasörünü seçin (oyunlar bu klasörün altına kurulacak):";
                        fbd.ShowNewFolderButton = true;
                        LibraryChoice cur = cmbLibraries.SelectedItem as LibraryChoice;
                        if (cur != null && Directory.Exists(cur.RootPath)) fbd.SelectedPath = cur.RootPath;

                        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            string chosenRoot = fbd.SelectedPath;
                            if (string.IsNullOrEmpty(chosenRoot)) return;

                            // If user picked a folder that already ends with the game name, treat its parent as library root
                            try
                            {
                                string leaf = Path.GetFileName(chosenRoot.TrimEnd('\\', '/'));
                                if (!string.IsNullOrEmpty(leaf) && string.Equals(leaf, safeName, StringComparison.OrdinalIgnoreCase))
                                {
                                    string parent = Path.GetDirectoryName(chosenRoot.TrimEnd('\\', '/'));
                                    if (!string.IsNullOrEmpty(parent)) chosenRoot = parent;
                                }
                            }
                            catch { }

                            LibraryChoice existing = null;
                            foreach (LibraryChoice c in choices)
                            {
                                if (string.Equals(c.RootPath, chosenRoot, StringComparison.OrdinalIgnoreCase))
                                { existing = c; break; }
                            }

                            if (existing == null)
                            {
                                LibraryChoice nc = MakeChoice(chosenRoot, true);
                                choices.Insert(0, nc);
                                cmbLibraries.Items.Insert(0, nc);
                                cmbLibraries.SelectedIndex = 0;
                            }
                            else
                            {
                                cmbLibraries.SelectedItem = existing;
                            }
                        }
                    }
                }
                catch { }
            };

            // Row 4: Buttons
            Grid btnGrid = new Grid();
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(btnGrid, 4);
            root.Children.Add(btnGrid);

            Button btnCancel = new Button
            {
                Content = "İPTAL",
                Height = 32,
                Width = 90,
                Margin = new Thickness(0, 0, 8, 0),
                Background = HexBrush("#1E242D"),
                Foreground = HexBrush("#8F98A0"),
                BorderBrush = HexBrush("#2A333E"),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetColumn(btnCancel, 1);
            btnGrid.Children.Add(btnCancel);
            btnCancel.Click += delegate { win.Close(); };

            Button btnConfirm = new Button
            {
                Content = "İNDİR VE OTOMATİK KUR",
                Height = 32,
                Padding = new Thickness(16, 0, 16, 0),
                Background = (Brush)new BrushConverter().ConvertFromString("#1A9FFF"),
                Foreground = Brushes.White,
                FontWeight = FontWeights.ExtraBold,
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Grid.SetColumn(btnConfirm, 2);
            btnGrid.Children.Add(btnConfirm);

            btnConfirm.Click += delegate
            {
                LibraryChoice choice = cmbLibraries.SelectedItem as LibraryChoice;
                if (choice == null || string.IsNullOrEmpty(choice.RootPath))
                {
                    MessageBox.Show(win, "Lütfen bir kütüphane klasörü seçin.", "Titan Kurulum", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string target = Path.Combine(choice.RootPath, safeName);
                try
                {
                    string driveRoot = Path.GetPathRoot(target);
                    double pkgGb = EstimatePackageGb(profile.FileSize);
                    if (!string.IsNullOrEmpty(driveRoot) && pkgGb > 0)
                    {
                        DriveInfo di = new DriveInfo(driveRoot);
                        if (di.IsReady)
                        {
                            double freeGb = di.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                            if (freeGb < pkgGb)
                            {
                                MessageBoxResult warnRes = MessageBox.Show(
                                    win,
                                    string.Format("Seçili sürücüde ({0}) yeterli boş alan olmayabilir.\n\n• Gerekli minimum: {1:0.0} GB\n• Mevcut boş alan: {2:0.0} GB\n\nYine de devam edilsin mi?", driveRoot, pkgGb, freeGb),
                                    "Titan // Yetersiz Disk Alanı",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Warning);
                                if (warnRes != MessageBoxResult.Yes) return;
                            }
                        }
                    }
                }
                catch { }
                try
                {
                    if (!Directory.Exists(target)) Directory.CreateDirectory(target);
                    selectedPath = target;
                    confirmed = true;

                    // Remember chosen library root for future installs (best-effort)
                    try { DownloadInstallService.CustomGamesRootDirectory = choice.RootPath; } catch { }
                    try { GamePathResolver.InvalidateCache(); } catch { }

                    win.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(win, "Kurulum klasörü oluşturulamadı:\n" + ex.Message, "Titan Kurulum Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            win.ShowDialog();

            return confirmed ? selectedPath : null;
        }

        private static List<LibraryChoice> BuildLibraryChoices(GameProfile profile, string safeName)
        {
            List<LibraryChoice> list = new List<LibraryChoice>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string, bool> add = delegate(string path, bool markCustom)
            {
                if (string.IsNullOrEmpty(path)) return;
                string full;
                try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch { return; }
                if (full.Length < 3) return;

                string lower = full.ToLowerInvariant();
                // Steam kütüphane içi (common) normal kurulum hedefi olarak önerilmesin
                if (lower.IndexOf("\\steamapps\\common", StringComparison.Ordinal) >= 0 && !markCustom) return;

                if (!seen.Add(full)) return;
                list.Add(MakeChoice(full, markCustom));
            };

            // 1. Mevcut/önceki seçim (profilin çalışma dizini)
            try
            {
                if (profile != null && !string.IsNullOrEmpty(profile.WorkingDirectory))
                {
                    string wd = profile.WorkingDirectory.TrimEnd('\\', '/');
                    string parent = null;
                    try { parent = Path.GetDirectoryName(wd); } catch { parent = null; }
                    string leaf = "";
                    try { leaf = Path.GetFileName(wd); } catch { leaf = ""; }

                    // working dir oyun klasörü ise ebeveyni kütüphane köküdür
                    if (!string.IsNullOrEmpty(parent) &&
                        string.Equals(leaf, safeName, StringComparison.OrdinalIgnoreCase))
                    {
                        add(parent, true);
                    }
                    else
                    {
                        add(wd, true);
                    }
                }
            }
            catch { }

            // 2. Kullanıcının daha önce seçtiği kök
            try { add(DownloadInstallService.CustomGamesRootDirectory, true); } catch { }

            // 3. Varsayılan/etkin kök
            try { add(DownloadInstallService.GetEffectiveGamesRoot(), false); } catch { }

            // 4. Bilinen kütüphane kökleri
            try
            {
                List<string> roots = GamePathResolver.GetLibraryRoots(false);
                if (roots != null)
                {
                    foreach (string r in roots) add(r, false);
                }
            }
            catch { }

            // 5. Sürücü bazlı "Games/Oyunlar" önerileri (Steam benzeri seçim)
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                foreach (DriveInfo d in drives)
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                        if (!d.IsReady) continue;
                        string driveRoot = d.RootDirectory.FullName.TrimEnd('\\');
                        add(Path.Combine(driveRoot, "Games"), false);
                        add(Path.Combine(driveRoot, "Oyunlar"), false);
                    }
                    catch { }
                }
            }
            catch { }

            // Fallback
            try { add(@"D:\Games", false); } catch { }
            try { add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games"), false); } catch { }

            if (list.Count == 0)
            {
                list.Add(MakeChoice(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games"), false));
            }

            return list;
        }

        private static LibraryChoice MakeChoice(string rootPath, bool markCustom)
        {
            string label = rootPath;
            try
            {
                string drive = null;
                try { drive = Path.GetPathRoot(rootPath); } catch { drive = null; }
                if (!string.IsNullOrEmpty(drive))
                {
                    DriveInfo di = new DriveInfo(drive);
                    if (di.IsReady)
                    {
                        double freeGb = di.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        string vol = "";
                        try { vol = di.VolumeLabel; } catch { vol = ""; }
                        if (!string.IsNullOrEmpty(vol))
                            label = string.Format("{0}  ({1})  —  {2:0.0} GB boş", rootPath, vol, freeGb);
                        else
                            label = string.Format("{0}  —  {1:0.0} GB boş", rootPath, freeGb);
                    }
                }
            }
            catch { }

            if (markCustom) label = label + "  [önerilen]";
            return new LibraryChoice { RootPath = rootPath, Display = label };
        }

        private static double EstimatePackageGb(string fileSize)
        {
            if (string.IsNullOrWhiteSpace(fileSize)) return 0;
            try
            {
                string s = fileSize.Trim().ToUpperInvariant().Replace(',', '.');
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(s, @"([0-9]+(?:\.[0-9]+)?)\s*(TB|GB|MB)");
                if (!m.Success) return 0;
                double val;
                if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val))
                    return 0;
                string unit = m.Groups[2].Value;
                if (unit == "TB") return val * 1024.0;
                if (unit == "GB") return val;
                if (unit == "MB") return Math.Max(0.1, val / 1024.0);
            }
            catch { }
            return 0;
        }

        private static SolidColorBrush HexBrush(string hex)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        }
    }
}
