using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TitanGameHub
{
    public class SteamAccountInfo
    {
        public bool IsSteamInstalled { get; set; }
        public bool IsSteamRunning { get; set; }
        public string SteamPath { get; set; }
        public string SteamExePath { get; set; }
        public string SteamId64 { get; set; }
        public string AccountName { get; set; }
        public string PersonaName { get; set; }
        public bool IsSpacewarReady { get; set; }
        public string StatusSummary { get; set; }

        public SteamAccountInfo()
        {
            SteamPath = "";
            SteamExePath = "";
            SteamId64 = "76561198000000000";
            AccountName = Environment.UserName;
            PersonaName = Environment.UserName;
            StatusSummary = "Steam taranıyor...";
        }
    }

    /// <summary>
    /// v9.5 Steam Native Client Bridge & Save-Protected Live Game Updater.
    /// Reads Valve Registry keys & loginusers.vdf, extracts real SteamID64 & PersonaName,
    /// bridges Spacewar (AppID 480) P2P sockets, injects shortcuts.vdf entries,
    /// and performs live SteamRIP version checks across the entire library.
    /// </summary>
    public static class SteamClientBridgeService
    {
        private static SteamAccountInfo _cachedAccount;

        public static SteamAccountInfo GetActiveSteamAccount(bool forceRefresh)
        {
            if (_cachedAccount != null && !forceRefresh)
            {
                _cachedAccount.IsSteamRunning = Process.GetProcessesByName("steam").Length > 0;
                return _cachedAccount;
            }

            SteamAccountInfo info = new SteamAccountInfo();
            try
            {
                info.IsSteamRunning = Process.GetProcessesByName("steam").Length > 0;

                // 1. Locate SteamPath from Registry or standard paths
                string steamPath = null;
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue("SteamPath");
                            if (val != null) steamPath = val.ToString().Replace('/', '\\');

                            object autoUser = key.GetValue("AutoLoginUser");
                            if (autoUser != null && !string.IsNullOrEmpty(autoUser.ToString()))
                            {
                                info.AccountName = autoUser.ToString();
                                info.PersonaName = autoUser.ToString();
                            }
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                {
                    string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    string candidate = Path.Combine(pf86, "Steam");
                    if (Directory.Exists(candidate)) steamPath = candidate;
                }

                if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(steamPath))
                {
                    info.IsSteamInstalled = true;
                    info.SteamPath = steamPath;
                    info.SteamExePath = Path.Combine(steamPath, "steam.exe");

                    // 2. Parse config\loginusers.vdf for real SteamID64 and PersonaName
                    string vdfPath = Path.Combine(steamPath, "config", "loginusers.vdf");
                    if (File.Exists(vdfPath))
                    {
                        ParseLoginUsersVdf(vdfPath, info);
                    }

                    // 3. Check if Spacewar (AppID 480) appmanifest exists or Steam is running
                    string appManifest480 = Path.Combine(steamPath, "steamapps", "appmanifest_480.acf");
                    info.IsSpacewarReady = File.Exists(appManifest480) || info.IsSteamRunning;
                }

                if (info.IsSteamRunning)
                {
                    info.StatusSummary = string.Format("🟢 STEAM BAĞLI: {0} ({1}) • AppID 480 P2P Hazır",
                        info.PersonaName, info.SteamId64);
                }
                else if (info.IsSteamInstalled)
                {
                    info.StatusSummary = string.Format("🟡 STEAM KURULU ({0}) — Tek Tıkla Başlatılabilir",
                        info.PersonaName);
                }
                else
                {
                    info.StatusSummary = "🟠 STEAM BULUNAMADI — Dahili Goldberg P2P Emülatörü Aktif";
                }
            }
            catch (Exception ex)
            {
                info.StatusSummary = "Steam köprü durumu: " + ex.Message;
            }

            _cachedAccount = info;
            return info;
        }

        private static void ParseLoginUsersVdf(string vdfPath, SteamAccountInfo info)
        {
            try
            {
                string content = File.ReadAllText(vdfPath, Encoding.UTF8);

                // Match 17-digit SteamID64 blocks
                MatchCollection idMatches = Regex.Matches(content, @"""(7656119\d{10})""\s*\{([^}]*)\}", RegexOptions.Singleline);
                foreach (Match m in idMatches)
                {
                    string steamId = m.Groups[1].Value;
                    string body = m.Groups[2].Value;

                    string accName = ExtractVdfValue(body, "AccountName");
                    string persona = ExtractVdfValue(body, "PersonaName");
                    string mostRecent = ExtractVdfValue(body, "MostRecent");

                    if (!string.IsNullOrEmpty(persona))
                    {
                        info.SteamId64 = steamId;
                        info.PersonaName = persona;
                        if (!string.IsNullOrEmpty(accName)) info.AccountName = accName;
                        if (mostRecent == "1") break; // Prefer MostRecent=1 account
                    }
                }
            }
            catch { }
        }

        private static string ExtractVdfValue(string block, string key)
        {
            Match m = Regex.Match(block, @"""" + key + @"""\s+""([^""]+)""", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Ensures Steam Desktop Client is running, activates Spacewar (AppID 480) P2P bridge,
        /// and syncs the user's real Steam PersonaName & SteamID64 into the selected game's Online-Fix config.
        /// </summary>
        public static string ConnectAndBridgeSteamClient(GameProfile selectedGame, bool launchSpacewar)
        {
            SteamAccountInfo acc = GetActiveSteamAccount(true);

            try
            {
                if (acc.IsSteamInstalled && !acc.IsSteamRunning && File.Exists(acc.SteamExePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = acc.SteamExePath,
                        Arguments = "-silent",
                        UseShellExecute = true
                    });
                    acc.IsSteamRunning = true;
                }

                if (launchSpacewar && acc.IsSteamInstalled)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "steam://install/480",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }

                if (selectedGame != null)
                {
                    selectedGame.DeployOnlineFix("480");
                    InjectSteamIdentityIntoGame(selectedGame, acc);
                }

                return string.Format("🔗 Steam Köprüsü Aktif! Hesap: {0} [{1}] • Spacewar (AppID 480) & Online-Fix senkronize edildi.",
                    acc.PersonaName, acc.SteamId64);
            }
            catch (Exception ex)
            {
                return "Steam bağlantı hatası: " + ex.Message;
            }
        }

        /// <summary>
        /// Writes the user's real Steam PersonaName and SteamID64 into the game's
        /// steam_settings and OnlineFix.ini files so Co-Op lobbies display the real identity.
        /// </summary>
        public static void InjectSteamIdentityIntoGame(GameProfile gp, SteamAccountInfo acc)
        {
            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
                return;

            try
            {
                string root = gp.WorkingDirectory;
                string persona = (acc != null && !string.IsNullOrEmpty(acc.PersonaName)) ? acc.PersonaName : Environment.UserName;
                string steamId = (acc != null && !string.IsNullOrEmpty(acc.SteamId64)) ? acc.SteamId64 : "76561198012345678";
                string realAppId = string.IsNullOrEmpty(gp.SteamAppId) ? "480" : gp.SteamAppId;

                string steamSettings = Path.Combine(root, "steam_settings");
                if (!Directory.Exists(steamSettings)) Directory.CreateDirectory(steamSettings);
                string settingsSub = Path.Combine(steamSettings, "settings");
                if (!Directory.Exists(settingsSub)) Directory.CreateDirectory(settingsSub);

                // Goldberg / fork uyumlu ayarlar
                File.WriteAllText(Path.Combine(settingsSub, "account_name.txt"), persona, Encoding.UTF8);
                File.WriteAllText(Path.Combine(settingsSub, "language.txt"), "turkish", Encoding.UTF8);
                File.WriteAllText(Path.Combine(settingsSub, "user_steam_id.txt"), steamId, Encoding.UTF8);
                if (!File.Exists(Path.Combine(settingsSub, "listen_port.txt")))
                    File.WriteAllText(Path.Combine(settingsSub, "listen_port.txt"), "47584", Encoding.UTF8);

                // Kök steam_settings uyum kopyaları
                File.WriteAllText(Path.Combine(steamSettings, "account_name.txt"), persona, Encoding.UTF8);
                File.WriteAllText(Path.Combine(steamSettings, "language.txt"), "turkish", Encoding.UTF8);
                File.WriteAllText(Path.Combine(steamSettings, "user_steam_id.txt"), steamId, Encoding.UTF8);
                File.WriteAllText(Path.Combine(steamSettings, "force_account_name.txt"), persona, Encoding.UTF8);
                File.WriteAllText(Path.Combine(steamSettings, "force_steamid.txt"), steamId, Encoding.ASCII);
                File.WriteAllText(Path.Combine(steamSettings, "force_language.txt"), "turkish", Encoding.ASCII);

                // Spacewar P2P köprüsü için AppID tutarlılığı
                try { File.WriteAllText(Path.Combine(root, "steam_appid.txt"), "480", Encoding.ASCII); } catch { }
                DirectoryInfo parent = Directory.GetParent(root);
                if (parent != null && parent.Exists)
                {
                    try { File.WriteAllText(Path.Combine(parent.FullName, "steam_appid.txt"), "480", Encoding.ASCII); } catch { }
                }

                string onlineFixIni = Path.Combine(root, "OnlineFix.ini");
                string iniContent =
                    "[Main]\r\n" +
                    "RealAppId=" + realAppId + "\r\n" +
                    "FakeAppId=480\r\n" +
                    "Language=turkish\r\n" +
                    "PlayerName=" + persona + "\r\n" +
                    "SteamId64=" + steamId + "\r\n" +
                    "UseSpacewarP2P=true\r\n" +
                    "UnlockAllDLC=true\r\n" +
                    "Overlay=true\r\n";
                File.WriteAllText(onlineFixIni, iniContent, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// Scans all games in the library against live SteamRIP REST API posts
        /// to detect newer versions and update DirectMirrorUrls automatically.
        /// </summary>
        /// <summary>titan_user.cfg: AUTO_UPDATE_CHECK=0 → sürüm taraması tamamen kapalı.</summary>
        public static bool IsUpdateCheckEnabled()
        {
            try
            {
                string cfg = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "titan_user.cfg");
                if (!System.IO.File.Exists(cfg)) return true;
                foreach (string line in System.IO.File.ReadAllLines(cfg))
                {
                    string l = line.Trim();
                    if (l.StartsWith("AUTO_UPDATE_CHECK", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = l.Contains("=") ? l.Split('=')[1].Trim() : "";
                        return !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            catch { }
            return true;
        }

        public static int ScanLibraryForLiveUpdates(List<GameProfile> profiles, out List<GameProfile> updatedGames)
        {
            updatedGames = new List<GameProfile>();
            if (!IsUpdateCheckEnabled()) return 0;
            if (profiles == null || profiles.Count == 0) return 0;

            try
            {
                // Fetch latest 30 posts from SteamRIP REST API + Multiplayer category
                string statusMsg;
                List<GameProfile> latestPosts = SiteParserService.FetchLiveSiteGames(0, null, 1, 30, out statusMsg);
                List<GameProfile> mpPosts = SiteParserService.FetchLiveSiteGames(1538, null, 1, 30, out statusMsg);

                Dictionary<string, GameProfile> liveMap = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
                if (latestPosts != null)
                {
                    foreach (GameProfile p in latestPosts) liveMap[NormalizeTitleKey(p.Name)] = p;
                }
                if (mpPosts != null)
                {
                    foreach (GameProfile p in mpPosts) liveMap[NormalizeTitleKey(p.Name)] = p;
                }

                foreach (GameProfile local in profiles)
                {
                    // Sadece KURULU oyunlarda güncelleme aranır (kurulu olmayanlar sadece indirilir).
                    if (!local.IsInstalled) continue;

                    // Steam kütüphanesindeki oyunlar Steam üzerinden güncellenir;
                    // repack siteleriyle kıyaslamak yanlış "GÜNCELLE" uyarısı üretiyordu.
                    if (local.IsSteamManaged) continue;

                    string key = NormalizeTitleKey(local.Name);
                    GameProfile remote;
                    if (liveMap.TryGetValue(key, out remote))
                    {
                        if (!string.IsNullOrEmpty(remote.Version) &&
                            !string.Equals(remote.Version.Trim(), (local.Version ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            local.RemoteVersion = remote.Version.Trim();
                            if (!string.IsNullOrEmpty(remote.DirectMirrorUrl))
                                local.DirectMirrorUrl = remote.DirectMirrorUrl;
                            if (!string.IsNullOrEmpty(remote.FileSize) && remote.FileSize != "Bulut")
                                local.FileSize = remote.FileSize;
                            if (!updatedGames.Contains(local)) updatedGames.Add(local);
                        }
                    }
                    else if (local.IsInstalled)
                    {
                        string singleMsg;
                        if (CheckSingleGameUpdate(local, out singleMsg))
                        {
                            if (!updatedGames.Contains(local)) updatedGames.Add(local);
                        }
                    }

                    // Çoklu kaynak: SteamRIP sayfası bayat olabilir; FitGirl/OvaGames
                    // aynı oyun için daha yeni bir sürüm yayınlamış olabilir.
                    // Bu uzak sürüm daha önce kurulduysa tekrar uyarı verme.
                    if (UpdateLedger.IsUpToDate(local, local.RemoteVersion))
                    {
                        local.RemoteVersion = local.Version;
                        continue;
                    }

                    if (local.IsInstalled && !updatedGames.Contains(local))
                    {
                        try
                        {
                            MultiSourceVersionResolver.Result ms = MultiSourceVersionResolver.ResolveSync(local.Name, local.Version);
                            if (ms != null && ms.IsNewerThanLocal && SameVersionFormat(local.Version, ms.Version))
                            {
                                local.RemoteVersion = ms.Version;
                                local.UpdateSourceHint = ms.SourceName + " v" + ms.Version;
                                // Güncelleme, sürümü İLK/EN YENİ yayınlayan kaynağın linkinden inecek.
                                if (!string.IsNullOrEmpty(ms.DirectUrl))
                                    local.DirectMirrorUrl = ms.DirectUrl;
                                updatedGames.Add(local);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return updatedGames.Count;
        }

        /// <summary>
        /// Sürüm biçimi koruması: "Build 26317497" ile "v1.0.4.102642" karşılaştırılamaz.
        /// Yalnızca iki taraf da noktalı sürümse çoklu kaynak sonucunu kabul et (yanlış güncelleme uyarısını önler).
        /// </summary>
        public static bool SameVersionFormat(string local, string remote)
        {
            if (string.IsNullOrEmpty(local)) return true;
            if (string.IsNullOrEmpty(remote)) return false;
            bool localDotted = System.Text.RegularExpressions.Regex.IsMatch(local, @"^\s*v?\d+\.\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            bool remoteDotted = System.Text.RegularExpressions.Regex.IsMatch(remote, @"^\s*v?\d+\.\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return localDotted && remoteDotted;
        }

        /// <summary>
        /// Checks a single game's live SteamRIP page for a newer version & fresh mirrors.
        /// </summary>
        public static bool CheckSingleGameUpdate(GameProfile gp, out string reportMessage)
        {
            reportMessage = "";
            if (gp == null) return false;

            // Kurulu olmayan oyun için sürüm kontrolü yapılmaz.
            if (!gp.IsInstalled)
            {
                reportMessage = "Oyun kurulu değil: güncelleme kontrolü yapılmaz.";
                return false;
            }

            // Steam yönetimindeki oyun: güncelleme Steam'in işi.
            if (gp.IsSteamManaged)
            {
                reportMessage = "Steam kütüphanesinde: güncelleme Steam üzerinden yapılır.";
                return false;
            }

            try
            {
                SitePageAnalysis analysis = SiteParserService.AnalyzeGameOnSteamRip(gp);
                if (analysis != null && analysis.Success)
                {
                    if (!string.IsNullOrEmpty(analysis.LiveFileSize))
                        gp.FileSize = analysis.LiveFileSize;

                    if (analysis.Mirrors.Count > 0)
                    {
                        SiteMirror direct = analysis.Mirrors.Find(delegate(SiteMirror m) { return m.IsDirectArchive; });
                        gp.DirectMirrorUrl = direct != null ? direct.Url : analysis.Mirrors[0].Url;
                    }

                    if (!string.IsNullOrEmpty(analysis.LiveVersion))
                    {
                        string cleanRemote = analysis.LiveVersion.Trim();
                        if (!string.Equals(cleanRemote, gp.Version, StringComparison.OrdinalIgnoreCase))
                        {
                            gp.RemoteVersion = cleanRemote;
                            reportMessage = string.Format("🔄 YENİ GÜNCELLEME BULUNDU! {0}: Yerel [{1}] -> Sitedeki Yeni Sürüm [{2}] ({3} sunucu hazır)",
                                gp.Name, gp.Version, gp.RemoteVersion, analysis.Mirrors.Count);
                            return true;
                        }
                    }

                    reportMessage = string.Format("✔ {0} güncel ({1}). {2} aktif indirme sunucusu doğrulandı.",
                        gp.Name, gp.Version, analysis.Mirrors.Count);
                    return false;
                }
            }
            catch (Exception ex)
            {
                reportMessage = "Güncelleme kontrol hatası: " + ex.Message;
                return false;
            }

            reportMessage = gp.Name + " sürüm kontrolü tamamlandı.";
            return false;
        }

        private static string NormalizeTitleKey(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            return Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]", "");
        }
    }
}
