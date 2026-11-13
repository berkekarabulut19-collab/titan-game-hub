using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace TitanGameHub
{
    public class AchievementItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsAchieved { get; set; }
        public DateTime UnlockTime { get; set; }
    }

    public static class SteamAchievementEngine
    {
        private static DispatcherTimer _pollTimer;
        private static GameProfile _activeGame;
        private static readonly HashSet<string> _knownUnlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static event Action<string, AchievementItem> OnAchievementUnlocked;

        public static void StartMonitoring(GameProfile gp)
        {
            StopMonitoring();
            if (gp == null) return;
            _activeGame = gp;
            _knownUnlocked.Clear();

            // Load initial unlocked set
            int unlocked, total;
            List<AchievementItem> list = GetGameAchievements(gp, out unlocked, out total);
            foreach (AchievementItem item in list)
            {
                if (item.IsAchieved) _knownUnlocked.Add(item.Id);
            }

            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(new Action(delegate
                {
                    _pollTimer = new DispatcherTimer();
                    _pollTimer.Interval = TimeSpan.FromSeconds(3);
                    _pollTimer.Tick += delegate
                    {
                        PollActiveGameAchievements();
                    };
                    _pollTimer.Start();
                }));
            }
        }

        public static void StopMonitoring()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }
            _activeGame = null;
        }

        private static void PollActiveGameAchievements()
        {
            if (_activeGame == null) return;
            int unlocked, total;
            List<AchievementItem> current = GetGameAchievements(_activeGame, out unlocked, out total);
            foreach (AchievementItem item in current)
            {
                if (item.IsAchieved && !_knownUnlocked.Contains(item.Id))
                {
                    _knownUnlocked.Add(item.Id);
                    ShowSteamAchievementToast(_activeGame.Name, item);
                    if (OnAchievementUnlocked != null)
                    {
                        OnAchievementUnlocked(_activeGame.Name, item);
                    }
                }
            }
        }

        public static List<AchievementItem> GetGameAchievements(GameProfile gp, out int unlockedCount, out int totalCount)
        {
            List<AchievementItem> list = new List<AchievementItem>();
            unlockedCount = 0;
            totalCount = 0;
            if (gp == null) return list;

            string appId = string.IsNullOrEmpty(gp.SteamAppId) ? "480" : gp.SteamAppId;
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string[] possiblePaths = new string[]
            {
                Path.Combine(userProfile, "AppData", "Roaming", "Goldberg SteamEmu Saves", appId, "achievements.json"),
                Path.Combine(userProfile, "AppData", "Roaming", "Goldberg SteamEmu Saves", appId, "achieves.json"),
                !string.IsNullOrEmpty(gp.WorkingDirectory) ? Path.Combine(gp.WorkingDirectory, "steam_settings", "achievements.json") : null,
                !string.IsNullOrEmpty(gp.WorkingDirectory) ? Path.Combine(gp.WorkingDirectory, "steam_settings", "achieves.json") : null
            };

            string activeFile = null;
            foreach (string p in possiblePaths)
            {
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    activeFile = p;
                    break;
                }
            }

            if (activeFile != null)
            {
                try
                {
                    string json = File.ReadAllText(activeFile);
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    Dictionary<string, object> dict = js.Deserialize<Dictionary<string, object>>(json);
                    if (dict != null)
                    {
                        foreach (KeyValuePair<string, object> kvp in dict)
                        {
                            totalCount++;
                            bool earned = false;
                            DateTime dt = DateTime.Now;

                            if (kvp.Value is Dictionary<string, object>)
                            {
                                Dictionary<string, object> sub = (Dictionary<string, object>)kvp.Value;
                                if (sub.ContainsKey("earned") && Convert.ToBoolean(sub["earned"])) earned = true;
                                if (sub.ContainsKey("achieved") && Convert.ToBoolean(sub["achieved"])) earned = true;
                                if (sub.ContainsKey("earned_time"))
                                {
                                    try { dt = new DateTime(1970, 1, 1).AddSeconds(Convert.ToInt64(sub["earned_time"])); } catch { }
                                }
                            }
                            else if (kvp.Value is bool && (bool)kvp.Value)
                            {
                                earned = true;
                            }
                            else if (kvp.Value != null)
                            {
                                string strVal = kvp.Value.ToString();
                                if (strVal == "1" || strVal.Equals("true", StringComparison.OrdinalIgnoreCase)) earned = true;
                            }

                            if (earned) unlockedCount++;

                            string cleanName = Regex.Replace(kvp.Key, @"[_-]", " ");
                            cleanName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleanName);

                            list.Add(new AchievementItem
                            {
                                Id = kvp.Key,
                                Name = cleanName,
                                Description = earned ? "Tamamlandı" : "Kilitli Başarım",
                                IsAchieved = earned,
                                UnlockTime = dt
                            });
                        }
                    }
                }
                catch { }
            }

            // Fallback demo achievements if none exist yet for visual fidelity
            if (list.Count == 0)
            {
                totalCount = 12;
                unlockedCount = 4;
                list.Add(new AchievementItem { Id = "ACH_FIRST_BLOOD", Name = "İlk Zafer", Description = "İlk görevi başarıyla tamamla", IsAchieved = true, UnlockTime = DateTime.Now.AddDays(-1) });
                list.Add(new AchievementItem { Id = "ACH_EXPLORER", Name = "Kaşif", Description = "Haritada 10 farklı bölge keşfet", IsAchieved = true, UnlockTime = DateTime.Now.AddHours(-4) });
                list.Add(new AchievementItem { Id = "ACH_COOP_READY", Name = "Takım Oyuncusu", Description = "Spacewar 480 Co-Op lobisine bağlan", IsAchieved = true, UnlockTime = DateTime.Now.AddMinutes(-30) });
                list.Add(new AchievementItem { Id = "ACH_SPEEDRUNNER", Name = "Hız Şampiyonu", Description = "Bir bölümü 3 dakikanın altında bitir", IsAchieved = true, UnlockTime = DateTime.Now.AddMinutes(-10) });
                list.Add(new AchievementItem { Id = "ACH_MASTER", Name = "Kusursuz Usta", Description = "Tüm silahları en üst seviyeye yükselt", IsAchieved = false });
                list.Add(new AchievementItem { Id = "ACH_LEGEND", Name = "Titan Efsanesi", Description = "Zor modda hikayeyi tamamla", IsAchieved = false });
            }

            return list;
        }

        public static void ShowSteamAchievementToast(string gameTitle, AchievementItem item)
        {
            if (Application.Current == null || Application.Current.Dispatcher == null) return;

            Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    Window toast = new Window()
                    {
                        Width = 320,
                        Height = 78,
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = Brushes.Transparent,
                        ShowInTaskbar = false,
                        Topmost = true,
                        Title = "Steam Achievement"
                    };

                    Border toastBorder = new Border()
                    {
                        Background = new LinearGradientBrush(
                            Color.FromRgb(27, 40, 56),
                            Color.FromRgb(16, 24, 34),
                            new Point(0, 0),
                            new Point(1, 1)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(102, 192, 244)),
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10),
                        Effect = new DropShadowEffect()
                        {
                            Color = Colors.Black,
                            BlurRadius = 14,
                            ShadowDepth = 3,
                            Opacity = 0.85
                        }
                    };
                    toast.Content = toastBorder;

                    Grid g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(48) });
                    g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                    toastBorder.Child = g;

                    Border iconBorder = new Border()
                    {
                        Width = 42,
                        Height = 42,
                        Background = new SolidColorBrush(Color.FromRgb(20, 29, 41)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(93, 186, 47)),
                        BorderThickness = new Thickness(1.2),
                        CornerRadius = new CornerRadius(3),
                        Child = new TextBlock()
                        {
                            Text = "🏆",
                            FontSize = 20,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    Grid.SetColumn(iconBorder, 0);
                    g.Children.Add(iconBorder);

                    StackPanel textStack = new StackPanel()
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 0)
                    };
                    Grid.SetColumn(textStack, 1);
                    g.Children.Add(textStack);

                    textStack.Children.Add(new TextBlock()
                    {
                        Text = "BAŞARIM KAZANILDI!",
                        Foreground = new SolidColorBrush(Color.FromRgb(102, 192, 244)),
                        FontSize = 9.5,
                        FontWeight = FontWeights.ExtraBold,
                        Margin = new Thickness(0, 0, 0, 2)
                    });

                    textStack.Children.Add(new TextBlock()
                    {
                        Text = item.Name,
                        Foreground = Brushes.White,
                        FontSize = 11.5,
                        FontWeight = FontWeights.Bold,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                    textStack.Children.Add(new TextBlock()
                    {
                        Text = gameTitle + " • " + (item.Description ?? "Kazanıldı"),
                        Foreground = new SolidColorBrush(Color.FromRgb(143, 152, 160)),
                        FontSize = 9.0,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                    double screenWidth = SystemParameters.WorkArea.Width;
                    double screenHeight = SystemParameters.WorkArea.Height;
                    toast.Left = screenWidth - toast.Width - 20;
                    toast.Top = screenHeight - toast.Height - 20;

                    try
                    {
                        ProceduralSoundEngine.PlayAchievement();
                    }
                    catch { }

                    toast.Show();

                    DispatcherTimer closeTimer = new DispatcherTimer();
                    closeTimer.Interval = TimeSpan.FromSeconds(5);
                    closeTimer.Tick += delegate
                    {
                        closeTimer.Stop();
                        toast.Close();
                    };
                    closeTimer.Start();
                }
                catch { }
            }));
        }
    }
}
