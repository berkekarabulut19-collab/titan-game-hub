using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace TitanGameHub
{
    /// <summary>
    /// Steam / masaüstü entegrasyonu: oyun klasörüne kapak görselleri yazar,
    /// masaüstü kısayolunu kapak ikonuyla oluşturur ve Steam shortcuts.vdf dosyasına
    /// Steam dışı oyun kısayolu ekler.
    /// </summary>
    public static class SteamIntegrationService
    {
        /// <summary>Oyun klasörüne cover.jpg/header.jpg yazar (Steam grid tarzı).</summary>
        public static bool WriteCoverArt(GameProfile gp, out string message)
        {
            message = "";
            try
            {
                if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
                {
                    message = "Oyun klasörü yok.";
                    return false;
                }

                string url = !string.IsNullOrEmpty(gp.BannerUrl) ? gp.BannerUrl : gp.CapsuleUrl;
                if (string.IsNullOrEmpty(url) || !url.StartsWith("http"))
                {
                    message = "Kapak görseli URL'si yok.";
                    return false;
                }

                byte[] data;
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    data = wc.DownloadData(url);
                }

                if (data == null || data.Length < 2048) { message = "Görsel indirilemedi."; return false; }

                string cover = Path.Combine(gp.WorkingDirectory, "cover.jpg");
                File.WriteAllBytes(cover, data);

                string header = Path.Combine(gp.WorkingDirectory, "header.jpg");
                File.WriteAllBytes(header, data);

                message = "Kapak yazıldı: " + cover;
                return true;
            }
            catch (Exception ex)
            {
                message = "Kapak yazma hatası: " + ex.Message;
                return false;
            }
        }

        /// <summary>Masaüstü kısayolu (mevcut yardımcı + kapak ikonu).</summary>
        public static bool CreateShortcut(GameProfile gp, out string message)
        {
            message = "";
            try
            {
                string err;
                DesktopShortcutHelper.CreateGameDesktopShortcut(gp, out err);
                message = string.IsNullOrEmpty(err) ? "Masaüstü kısayolu hazır." : err;
                return string.IsNullOrEmpty(err);
            }
            catch (Exception ex)
            {
                message = "Kısayol hatası: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Tek bir kurulu oyunu Steam'in tüm yerel hesaplarındaki shortcuts.vdf dosyasına
        /// "Steam Dışı Oyun" olarak ekler (mevcut kısayolları korur, .bak yedeği alır).
        /// </summary>
        public static bool AddGameToSteamShortcuts(GameProfile gp, out string message)
        {
            message = "";
            if (gp == null || !gp.IsInstalled || string.IsNullOrEmpty(gp.ExecutablePath) || !File.Exists(gp.ExecutablePath))
            {
                message = "Steam'e eklemek için oyunun bilgisayarda kurulu ve .exe yolunun geçerli olması gerekir.";
                return false;
            }

            List<GameProfile> single = new List<GameProfile>();
            single.Add(gp);
            int added;
            int updated;
            int accounts;
            bool ok = InjectGamesIntoSteamShortcuts(single, out added, out updated, out accounts, out message);
            if (ok)
            {
                bool steamOpen = Process.GetProcessesByName("steam").Length > 0;
                string coopNote = "";
                if (!string.IsNullOrEmpty(gp.WorkingDirectory) &&
                    (File.Exists(Path.Combine(gp.WorkingDirectory, "OnlineFix.ini")) ||
                     Directory.Exists(Path.Combine(gp.WorkingDirectory, "steam_settings"))))
                {
                    coopNote = " | Co-Op/OnlineFix hazır";
                }
                string steamNote = steamOpen
                    ? " (Steam kapatılmadı — bir sonraki Steam açılışında görünür)"
                    : " (Steam'i açınca kütüphanede görünür)";
                message = string.Format("✔ '{0}' Steam'e eklendi ({1} hesap{2}).{3}",
                    gp.Name, accounts, coopNote, steamNote);
            }
            return ok;
        }

        /// <summary>
        /// Kurulu tüm online/co-op oyunları tek seferde Steam shortcuts.vdf dosyasına ekler.
        /// </summary>
        public static bool AddAllOnlineGamesToSteam(IEnumerable<GameProfile> profiles, out string message)
        {
            message = "";
            if (profiles == null)
            {
                message = "Oyun listesi boş.";
                return false;
            }

            List<GameProfile> targets = new List<GameProfile>();
            foreach (GameProfile gp in profiles)
            {
                if (gp == null || !gp.IsInstalled) continue;
                if (string.IsNullOrEmpty(gp.ExecutablePath) || !File.Exists(gp.ExecutablePath)) continue;

                // Steam'den zaten resmi olarak inmiş oyunları tekrar Steam-dışı kısayol olarak ekleme
                if (gp.IsSteamManaged && string.IsNullOrEmpty(gp.OnlineFixUrl))
                {
                    continue;
                }

                bool isOnlineOrCoop = !string.IsNullOrEmpty(gp.OnlineFixUrl) ||
                    (!string.IsNullOrEmpty(gp.MaxPlayers) && gp.MaxPlayers != "1") ||
                    (!string.IsNullOrEmpty(gp.Category) &&
                     (gp.Category.IndexOf("Co-Op", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      gp.Category.IndexOf("Online", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      gp.Category.IndexOf("Multiplayer", StringComparison.OrdinalIgnoreCase) >= 0));

                if (isOnlineOrCoop) targets.Add(gp);
            }

            if (targets.Count == 0)
            {
                message = "Steam dışı eklenecek kurulu online/co-op oyunu bulunamadı.";
                return false;
            }

            int added;
            int updated;
            int accounts;
            bool ok = InjectGamesIntoSteamShortcuts(targets, out added, out updated, out accounts, out message);
            if (ok)
            {
                message = string.Format("✔ {0} online/co-op oyunu Steam'e işlendi ({1} yeni, {2} güncellendi, {3} Steam hesabı). Steam'i yeniden başlatınca görünür.",
                    targets.Count, added, updated, accounts);
            }
            return ok;
        }

        private static bool InjectGamesIntoSteamShortcuts(List<GameProfile> games, out int addedCount, out int updatedCount, out int accountCount, out string error)
        {
            addedCount = 0;
            updatedCount = 0;
            accountCount = 0;
            error = "";

            string steamRoot = FindSteamInstallPath();
            if (string.IsNullOrEmpty(steamRoot) || !Directory.Exists(steamRoot))
            {
                error = "Steam kurulum klasörü bulunamadı (Steam kurulu değil veya kayıt defterinde yok).";
                return false;
            }

            string userDataDir = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userDataDir))
            {
                error = "Steam userdata klasörü bulunamadı. Steam'e en az bir kez giriş yapılmış olmalı.";
                return false;
            }

            string[] userDirs = Directory.GetDirectories(userDataDir);
            if (userDirs.Length == 0)
            {
                error = "Steam kullanıcı profili bulunamadı (userdata boş).";
                return false;
            }

            for (int u = 0; u < userDirs.Length; u++)
            {
                string folderName = Path.GetFileName(userDirs[u]);
                long parsedId;
                if (!long.TryParse(folderName, out parsedId) || parsedId <= 0) continue;

                string configDir = Path.Combine(userDirs[u], "config");
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);

                string vdfPath = Path.Combine(configDir, "shortcuts.vdf");
                List<VdfEntry> entries = new List<VdfEntry>();

                if (File.Exists(vdfPath))
                {
                    try
                    {
                        byte[] raw = File.ReadAllBytes(vdfPath);
                        entries = ParseShortcutsVdf(raw);
                    }
                    catch
                    {
                        entries = new List<VdfEntry>();
                    }

                    try { File.Copy(vdfPath, vdfPath + ".bak", true); } catch { }
                }

                int localAdded = 0;
                int localUpdated = 0;

                for (int g = 0; g < games.Count; g++)
                {
                    GameProfile gp = games[g];
                    string exeClean = Path.GetFullPath(gp.ExecutablePath);
                    string exeQuoted = "\"" + exeClean + "\"";
                    string workDir = !string.IsNullOrEmpty(gp.WorkingDirectory) && Directory.Exists(gp.WorkingDirectory)
                        ? Path.GetFullPath(gp.WorkingDirectory)
                        : Path.GetDirectoryName(exeClean);
                    string startDirQuoted = "\"" + workDir + "\"";
                    string appName = string.IsNullOrWhiteSpace(gp.Name) ? Path.GetFileNameWithoutExtension(exeClean) : gp.Name.Trim();
                    uint appId = ComputeNonSteamAppId(exeQuoted, appName);

                    VdfEntry existing = FindMatchingEntry(entries, exeClean, appName);
                    if (existing != null)
                    {
                        SetEntryFields(existing, appId, appName, exeQuoted, startDirQuoted, exeClean, "");
                        localUpdated++;
                    }
                    else
                    {
                        VdfEntry created = new VdfEntry();
                        SetEntryFields(created, appId, appName, exeQuoted, startDirQuoted, exeClean, "");
                        entries.Add(created);
                        localAdded++;
                    }

                    WriteSteamGridArtwork(configDir, appId, gp);
                }

                byte[] output = SerializeShortcutsVdf(entries);
                File.WriteAllBytes(vdfPath, output);

                accountCount++;
                if (accountCount == 1)
                {
                    addedCount = localAdded;
                    updatedCount = localUpdated;
                }
            }

            if (accountCount == 0)
            {
                error = "Geçerli Steam kullanıcı klasörü bulunamadı.";
                return false;
            }

            return true;
        }

        private static void WriteSteamGridArtwork(string configDir, uint appId, GameProfile gp)
        {
            try
            {
                if (gp == null || string.IsNullOrEmpty(configDir)) return;
                string gridDir = Path.Combine(configDir, "grid");
                if (!Directory.Exists(gridDir)) Directory.CreateDirectory(gridDir);

                string portraitPath = Path.Combine(gridDir, appId + "p.jpg");
                string widePath = Path.Combine(gridDir, appId + ".jpg");
                string heroPath = Path.Combine(gridDir, appId + "_hero.jpg");

                if (File.Exists(portraitPath) && File.Exists(heroPath)) return;

                byte[] imgBytes = null;
                if (!string.IsNullOrEmpty(gp.WorkingDirectory))
                {
                    string localCover = Path.Combine(gp.WorkingDirectory, "cover.jpg");
                    string localHeader = Path.Combine(gp.WorkingDirectory, "header.jpg");
                    if (File.Exists(localCover)) imgBytes = File.ReadAllBytes(localCover);
                    else if (File.Exists(localHeader)) imgBytes = File.ReadAllBytes(localHeader);
                }

                if (imgBytes == null)
                {
                    string url = !string.IsNullOrEmpty(gp.CapsuleUrl) ? gp.CapsuleUrl : gp.BannerUrl;
                    if (!string.IsNullOrEmpty(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        using (WebClient wc = new WebClient())
                        {
                            wc.Proxy = null;
                            wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                            imgBytes = wc.DownloadData(url);
                        }
                    }
                }

                if (imgBytes != null && imgBytes.Length >= 2048)
                {
                    if (!File.Exists(portraitPath)) File.WriteAllBytes(portraitPath, imgBytes);
                    if (!File.Exists(widePath)) File.WriteAllBytes(widePath, imgBytes);
                    if (!File.Exists(heroPath)) File.WriteAllBytes(heroPath, imgBytes);
                }
            }
            catch { }
        }

        private static string FindSteamInstallPath()
        {
            string[] regPaths = new string[]
            {
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
            };

            for (int i = 0; i < regPaths.Length; i++)
            {
                try
                {
                    object val = Registry.GetValue(regPaths[i], "SteamPath", null)
                        ?? Registry.GetValue(regPaths[i], "InstallPath", null);
                    if (val != null)
                    {
                        string p = val.ToString().Replace('/', '\\').Trim();
                        if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
                    }
                }
                catch { }
            }

            string[] fallbacks = new string[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                @"D:\Steam"
            };
            for (int i = 0; i < fallbacks.Length; i++)
            {
                if (Directory.Exists(fallbacks[i])) return fallbacks[i];
            }
            return null;
        }

        private class VdfField
        {
            public byte Type; // 0x00 = map, 0x01 = string, 0x02 = uint32
            public string Key;
            public object Value; // string, uint, or List<VdfField>
        }

        private class VdfEntry
        {
            public List<VdfField> Fields = new List<VdfField>();

            public string GetString(string key)
            {
                for (int i = 0; i < Fields.Count; i++)
                {
                    if (string.Equals(Fields[i].Key, key, StringComparison.OrdinalIgnoreCase) && Fields[i].Type == 0x01)
                        return Fields[i].Value as string ?? "";
                }
                return "";
            }

            public void Set(byte type, string key, object val)
            {
                for (int i = 0; i < Fields.Count; i++)
                {
                    if (string.Equals(Fields[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        Fields[i].Type = type;
                        Fields[i].Value = val;
                        return;
                    }
                }
                Fields.Add(new VdfField { Type = type, Key = key, Value = val });
            }
        }

        private static void SetEntryFields(VdfEntry e, uint appId, string appName, string exeQuoted, string startDirQuoted, string iconPath, string launchArgs)
        {
            e.Set(0x02, "appid", appId);
            e.Set(0x01, "AppName", appName);
            e.Set(0x01, "Exe", exeQuoted);
            e.Set(0x01, "StartDir", startDirQuoted);
            e.Set(0x01, "icon", iconPath);
            e.Set(0x01, "ShortcutPath", "");
            e.Set(0x01, "LaunchOptions", launchArgs ?? "");
            e.Set(0x02, "IsHidden", (uint)0);
            e.Set(0x02, "AllowDesktopConfig", (uint)1);
            e.Set(0x02, "AllowOverlay", (uint)1);
            e.Set(0x02, "OpenVR", (uint)0);
            e.Set(0x02, "Devkit", (uint)0);
            e.Set(0x01, "DevkitGameID", "");
            e.Set(0x02, "DevkitOverrideAppID", (uint)0);
            e.Set(0x02, "LastPlayTime", (uint)0);
            e.Set(0x01, "FlatpakAppID", "");

            List<VdfField> tags = new List<VdfField>();
            tags.Add(new VdfField { Type = 0x01, Key = "0", Value = "TitanGameHub" });
            e.Set(0x00, "tags", tags);
        }

        private static VdfEntry FindMatchingEntry(List<VdfEntry> entries, string exeClean, string appName)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                string existingExe = entries[i].GetString("Exe").Trim('\"', ' ');
                string existingName = entries[i].GetString("AppName").Trim();
                if ((!string.IsNullOrEmpty(existingExe) && string.Equals(existingExe, exeClean, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(existingName) && string.Equals(existingName, appName, StringComparison.OrdinalIgnoreCase)))
                {
                    return entries[i];
                }
            }
            return null;
        }

        private static List<VdfEntry> ParseShortcutsVdf(byte[] data)
        {
            List<VdfEntry> list = new List<VdfEntry>();
            if (data == null || data.Length < 12) return list;

            int pos = 0;
            if (data[pos] != 0x00) return list;
            pos++;
            string rootKey = ReadNullTerminatedString(data, ref pos);
            if (!string.Equals(rootKey, "shortcuts", StringComparison.OrdinalIgnoreCase)) return list;

            while (pos < data.Length)
            {
                byte t = data[pos++];
                if (t == 0x08) break; // end of shortcuts map
                if (t != 0x00) break;

                ReadNullTerminatedString(data, ref pos); // entry index ("0", "1", ...)
                VdfEntry entry = new VdfEntry();
                entry.Fields = ReadMapFields(data, ref pos);
                list.Add(entry);
            }

            return list;
        }

        private static List<VdfField> ReadMapFields(byte[] data, ref int pos)
        {
            List<VdfField> fields = new List<VdfField>();
            while (pos < data.Length)
            {
                byte t = data[pos++];
                if (t == 0x08) break;

                string key = ReadNullTerminatedString(data, ref pos);
                if (t == 0x00)
                {
                    List<VdfField> sub = ReadMapFields(data, ref pos);
                    fields.Add(new VdfField { Type = 0x00, Key = key, Value = sub });
                }
                else if (t == 0x01)
                {
                    string val = ReadNullTerminatedString(data, ref pos);
                    fields.Add(new VdfField { Type = 0x01, Key = key, Value = val });
                }
                else if (t == 0x02)
                {
                    if (pos + 4 > data.Length) break;
                    uint val = BitConverter.ToUInt32(data, pos);
                    pos += 4;
                    fields.Add(new VdfField { Type = 0x02, Key = key, Value = val });
                }
                else
                {
                    break;
                }
            }
            return fields;
        }

        private static string ReadNullTerminatedString(byte[] data, ref int pos)
        {
            int start = pos;
            while (pos < data.Length && data[pos] != 0x00) pos++;
            string s = Encoding.UTF8.GetString(data, start, pos - start);
            if (pos < data.Length && data[pos] == 0x00) pos++;
            return s;
        }

        private static byte[] SerializeShortcutsVdf(List<VdfEntry> entries)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ms.WriteByte(0x00);
                WriteNullTerminatedString(ms, "shortcuts");

                for (int i = 0; i < entries.Count; i++)
                {
                    ms.WriteByte(0x00);
                    WriteNullTerminatedString(ms, i.ToString());
                    WriteMapFields(ms, entries[i].Fields);
                }

                ms.WriteByte(0x08);
                ms.WriteByte(0x08);
                return ms.ToArray();
            }
        }

        private static void WriteMapFields(MemoryStream ms, List<VdfField> fields)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                VdfField f = fields[i];
                ms.WriteByte(f.Type);
                WriteNullTerminatedString(ms, f.Key ?? "");
                if (f.Type == 0x00)
                {
                    List<VdfField> sub = f.Value as List<VdfField> ?? new List<VdfField>();
                    WriteMapFields(ms, sub);
                }
                else if (f.Type == 0x01)
                {
                    WriteNullTerminatedString(ms, f.Value as string ?? "");
                }
                else if (f.Type == 0x02)
                {
                    uint v = (f.Value is uint) ? (uint)f.Value : 0u;
                    byte[] b = BitConverter.GetBytes(v);
                    ms.Write(b, 0, 4);
                }
            }
            ms.WriteByte(0x08);
        }

        private static void WriteNullTerminatedString(MemoryStream ms, string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            ms.Write(b, 0, b.Length);
            ms.WriteByte(0x00);
        }

        private static uint ComputeNonSteamAppId(string exeQuoted, string appName)
        {
            byte[] input = Encoding.UTF8.GetBytes((exeQuoted ?? "") + (appName ?? ""));
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < input.Length; i++)
            {
                crc ^= input[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320u;
                    else
                        crc >>= 1;
                }
            }
            crc ^= 0xFFFFFFFFu;
            return crc | 0x80000000u;
        }
    }
}
