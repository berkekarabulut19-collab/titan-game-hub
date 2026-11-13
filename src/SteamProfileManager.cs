using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    public class PlayerProfileData
    {
        public string PersonaName { get; set; }
        public int Level { get; set; }
        public int CurrentXp { get; set; }
        public int NextLevelXp { get; set; }
        public string StatusQuote { get; set; }
        public string BadgeTitle { get; set; }
        public int TotalAchievementsUnlocked { get; set; }
        public int TotalHoursPlayed { get; set; }
    }

    public static class SteamProfileManager
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profile.cfg");
        private static PlayerProfileData _currentData;

        public static PlayerProfileData GetProfile(List<GameProfile> library)
        {
            if (_currentData == null)
            {
                LoadProfile();
            }

            // Recalculate dynamic XP from library playtime and achievements
            int totalMins = 0;
            int totalAch = 0;
            if (library != null)
            {
                foreach (GameProfile gp in library)
                {
                    totalMins += gp.PlayTimeMinutes;
                    if (gp.IsInstalled)
                    {
                        int u, t;
                        SteamAchievementEngine.GetGameAchievements(gp, out u, out t);
                        totalAch += u;
                    }
                }
            }

            int hours = totalMins / 60;
            int computedXp = (hours * 50) + (totalAch * 100) + ((library != null ? library.Count : 0) * 20);

            _currentData.TotalHoursPlayed = hours;
            _currentData.TotalAchievementsUnlocked = totalAch;
            _currentData.CurrentXp = computedXp;
            _currentData.Level = Math.Max(1, (computedXp / 500) + 1);
            _currentData.NextLevelXp = _currentData.Level * 500;

            if (_currentData.Level >= 50) _currentData.BadgeTitle = "👑 TITAN EFSANESİ";
            else if (_currentData.Level >= 25) _currentData.BadgeTitle = "⭐ KORUYUCU USTA";
            else if (_currentData.Level >= 10) _currentData.BadgeTitle = "🏆 TECRÜBELİ OYUNCU";
            else _currentData.BadgeTitle = "🎮 ÇAYLAK SEVİYE";

            return _currentData;
        }

        private static void LoadProfile()
        {
            _currentData = new PlayerProfileData
            {
                PersonaName = Environment.UserName,
                Level = 1,
                CurrentXp = 0,
                NextLevelXp = 500,
                StatusQuote = "Titan Game Hub — Spacewar 480 Co-Op Lobisinde",
                BadgeTitle = "🎮 ÇAYLAK SEVİYE",
                TotalAchievementsUnlocked = 0,
                TotalHoursPlayed = 0
            };

            if (File.Exists(ConfigPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
                    foreach (string l in lines)
                    {
                        if (string.IsNullOrWhiteSpace(l) || l.StartsWith("#")) continue;
                        string[] parts = l.Split(new char[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            string k = parts[0].Trim();
                            string v = parts[1].Trim();
                            if (k.Equals("PersonaName", StringComparison.OrdinalIgnoreCase)) _currentData.PersonaName = v;
                            if (k.Equals("StatusQuote", StringComparison.OrdinalIgnoreCase)) _currentData.StatusQuote = v;
                        }
                    }
                }
                catch { }
            }
        }

        public static void SaveProfile(string personaName, string statusQuote)
        {
            if (_currentData == null) LoadProfile();
            if (!string.IsNullOrEmpty(personaName)) _currentData.PersonaName = personaName;
            if (!string.IsNullOrEmpty(statusQuote)) _currentData.StatusQuote = statusQuote;

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Titan Game Hub Player Profile");
                sb.AppendLine("PersonaName=" + _currentData.PersonaName);
                sb.AppendLine("StatusQuote=" + _currentData.StatusQuote);
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
