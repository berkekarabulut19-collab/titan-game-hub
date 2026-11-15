using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TitanGameHub
{
    public class GameProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string ExecutablePath { get; set; }
        public string WorkingDirectory { get; set; }
        public string Version { get; set; }
        public string RemoteVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string OnlineFixUrl { get; set; }
        public string SteamAppId { get; set; }
        public string Description { get; set; }
        public string MaxPlayers { get; set; }
        public string BannerUrl { get; set; }
        public string CapsuleUrl { get; set; }
        public string FileSize { get; set; }
        public string DirectMirrorUrl { get; set; }
        public bool IsFavorite { get; set; }
        public int PlayTimeMinutes { get; set; }
        public string LastPlayedText { get; set; }
        /// <summary>Güncellemeyi hangi kaynak bildirdi (ör. "FitGirl v1.2.3"). Kalıcı değildir.</summary>
        public string UpdateSourceHint { get; set; }

        // Kullanıcı etiketleri ve koleksiyonlar (virgülle ayrılmış)
        public string Tags { get; set; }
        public string Collection { get; set; }
        // Güncelleme politikası: "auto", "notify", "manual"
        public string UpdatePolicy { get; set; }
        // Son doğrulama zamanı (dosya bütünlüğü)
        public string LastIntegrityCheck { get; set; }

        public bool IsInstalled
        {
            get
            {
                if (string.IsNullOrEmpty(ExecutablePath) || !File.Exists(ExecutablePath))
                    return false;

                string lower = ExecutablePath.ToLowerInvariant();
                if (lower.Contains("packagecache") || lower.Contains(@"\library\") ||
                    lower.Contains(@"\.runtime\") || lower.Contains(@"\temp\") ||
                    lower.Contains(@"\obj\") || lower.EndsWith("dsymutil.exe") ||
                    lower.EndsWith("unitycrashhandler64.exe"))
                {
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Oyun bir Steam kütüphanesinde (steamapps\common) kuruluysa güncellemesi Steam'den gelir.
        /// Bu oyunlar için repack sitelerine göre "güncelleme var" uyarısı gösterilmez (yanlış pozitif).
        /// </summary>
        public bool IsSteamManaged
        {
            get
            {
                try
                {
                    string dir = !string.IsNullOrEmpty(WorkingDirectory) ? WorkingDirectory : ExecutablePath;
                    if (string.IsNullOrEmpty(dir)) return false;
                    string lower = dir.ToLowerInvariant();
                    return lower.Contains(@"\steamapps\common\") || lower.Contains(@"/steamapps/common/");
                }
                catch { return false; }
            }
        }

        public bool UpdateAvailable
        {
            get
            {
                // Kurulu olmayan veya Steam yönetimindeki oyunlar için güncelleme rozeti çıkarılmaz.
                if (!IsInstalled || IsSteamManaged) return false;
                if (string.Equals(Id, "pubg-mobile-gameloop", StringComparison.OrdinalIgnoreCase)) return false;

                if (!IsRemoteVersionNewer(Version, RemoteVersion)) return false;
                if (UpdateLedger.IsUpToDate(this, RemoteVersion)) return false;

                return true;
            }
        }

        public static bool IsRemoteVersionNewer(string localRaw, string remoteRaw)
        {
            string local = NormalizeVersionToken(localRaw);
            string remote = NormalizeVersionToken(remoteRaw);
            if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(remote)) return false;

            if (string.Equals(local, remote, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(NormalizeVersionLoose(local), NormalizeVersionLoose(remote), StringComparison.OrdinalIgnoreCase)) return false;

            int[] lNums = ExtractVersionNumbers(local);
            int[] rNums = ExtractVersionNumbers(remote);

            // Biri tekil büyük Build numarası (>=100000), diğeri noktalı sürüm (1.2.3) ise format uyumsuzluğu vardır -> yanlış uyarı verme
            if (lNums.Length > 0 && rNums.Length > 0)
            {
                bool lIsSingleBuild = lNums.Length == 1 && lNums[0] >= 10000;
                bool rIsSingleBuild = rNums.Length == 1 && rNums[0] >= 10000;
                if (lIsSingleBuild != rIsSingleBuild) return false;

                int maxLen = Math.Max(lNums.Length, rNums.Length);
                for (int i = 0; i < maxLen; i++)
                {
                    int lv = i < lNums.Length ? lNums[i] : 0;
                    int rv = i < rNums.Length ? rNums[i] : 0;
                    if (rv > lv) return true;
                    if (rv < lv) return false;
                }
                return false;
            }

            // Sayısal sürüm yoksa (jenerik metinler) güncelleme sayma
            return false;
        }

        private static int[] ExtractVersionNumbers(string cleanVersion)
        {
            if (string.IsNullOrEmpty(cleanVersion)) return new int[0];
            MatchCollection mc = Regex.Matches(cleanVersion, @"\d+");
            if (mc.Count == 0) return new int[0];
            List<int> nums = new List<int>();
            for (int i = 0; i < mc.Count && i < 6; i++)
            {
                int val;
                if (int.TryParse(mc[i].Value, out val))
                    nums.Add(val);
            }
            return nums.ToArray();
        }

        public static string NormalizeVersionToken(string version)
        {
            if (string.IsNullOrEmpty(version)) return "";
            string v = version.Trim();
            if (v.Length == 0) return "";

            string lower = v.ToLowerInvariant();
            if (lower == "bilinmiyor" || lower == "unknown" || lower == "n/a" || lower == "na" ||
                lower == "-" || lower == "—" || lower == "yok" || lower == "none" || lower == "null" ||
                lower == "tba" || lower == "?" || lower == "sürüm bilinmiyor" || lower == "surum bilinmiyor" ||
                lower == "belirsiz" || lower == "n/a." || lower == "bilinmiyor." ||
                lower == "kurulu" || lower == "resmi" || lower == "son sürüm" || lower == "son surum" ||
                lower == "latest" || lower == "full version" || lower == "full paket" ||
                lower == "resmi • gameloop" || lower == "gameloop (kurulu)")
                return "";

            // DLC, Full Paket, Early Access, Online-Fix gibi etiketleri soy -> sadece gerçek sürüm kalsın
            v = Regex.Replace(v, @"[•\|/]\s*\+?\d+\s*DLC.*$", "", RegexOptions.IgnoreCase);
            v = Regex.Replace(v, @"\s*[•\-–\|]\s*(Full\s*(Paket|Version|Sürüm|Surum)|Early\s*Access|Repack|Portable|Online[\s-]*Fix|Güncel|Guncel|Kurulu|Resmi).*$", "", RegexOptions.IgnoreCase);
            v = Regex.Replace(v, @"\s*\((Full\s*(Paket|Version|Sürüm)|Early\s*Access|Kurulu|Resmi|DLC|Online[\s-]*Fix)\)\s*", " ", RegexOptions.IgnoreCase);
            v = Regex.Replace(v, @"\s+", " ").Trim();
            v = Regex.Replace(v, @"^(v|ver\.?|version|build|sürüm|surum)\s*[:\-]?\s*", "", RegexOptions.IgnoreCase);
            return v.Trim();
        }

        private static string NormalizeVersionLoose(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return System.Text.RegularExpressions.Regex.Replace(v.ToLowerInvariant(), @"[^a-z0-9]+", "");
        }

        public string FormattedPlayTime
        {
            get
            {
                if (PlayTimeMinutes <= 0) return "Henüz Oynanmadı";
                if (PlayTimeMinutes < 60) return PlayTimeMinutes + " dk oynandı";
                return (PlayTimeMinutes / 60.0).ToString("0.1") + " saat oynandı";
            }
        }

        public void DeployOnlineFix(string customAppId = "480")
        {
            if (string.IsNullOrEmpty(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
                return;

            // Spacewar P2P köprüsü için temel AppID (varsayılan 480)
            string bridgeAppId = string.IsNullOrEmpty(customAppId) ? "480" : customAppId;

            try
            {
                string appIdPath = Path.Combine(WorkingDirectory, "steam_appid.txt");
                File.WriteAllText(appIdPath, bridgeAppId, Encoding.ASCII);
            }
            catch { }

            try
            {
                // Tüm Goldberg/OnlineFix uyum dosyalarını dağıt (steam_appid dahil)
                SteamEmulatorBridge.DeployFullEmulationSuite(this, Environment.UserName);
            }
            catch { }

            try
            {
                // DeployFullEmulationSuite sonrası köprü AppID'sini kesinleştir
                File.WriteAllText(Path.Combine(WorkingDirectory, "steam_appid.txt"), bridgeAppId, Encoding.ASCII);
                if (!string.IsNullOrEmpty(ExecutablePath))
                {
                    string exeDir = Path.GetDirectoryName(ExecutablePath);
                    if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir) &&
                        !string.Equals(exeDir.TrimEnd('\\'), WorkingDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        File.WriteAllText(Path.Combine(exeDir, "steam_appid.txt"), bridgeAppId, Encoding.ASCII);
                        string rootIni = Path.Combine(WorkingDirectory, "OnlineFix.ini");
                        string exeIni = Path.Combine(exeDir, "OnlineFix.ini");
                        if (File.Exists(rootIni) && !File.Exists(exeIni))
                            File.Copy(rootIni, exeIni, true);
                    }
                }
            }
            catch { }
        }

        public bool UninstallManagedGame(out string message)
        {
            message = "";
            try
            {
                string gamesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games");
                if (!string.IsNullOrEmpty(WorkingDirectory) && Directory.Exists(WorkingDirectory))
                {
                    if (WorkingDirectory.StartsWith(gamesRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.Delete(WorkingDirectory, true);
                        message = Name + " kurulum klasörü diskten tamamen silindi.";
                    }
                    else
                    {
                        message = Name + " harici disk bağlantısı kütüphaneden kaldırıldı (Orijinal klasör korundu).";
                    }
                }
                ExecutablePath = "";
                WorkingDirectory = "";
                return true;
            }
            catch (Exception ex)
            {
                message = "Kaldırma hatası: " + ex.Message;
                return false;
            }
        }
    }

    public static class ProfileStore
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games_v6.cfg");
        private static readonly string RemovedGamesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "removed_games.cfg");
        private static readonly object RemovedSync = new object();

        private static HashSet<string> LoadRemovedKeys()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                lock (RemovedSync)
                {
                    if (!File.Exists(RemovedGamesPath)) return set;
                    foreach (string raw in File.ReadAllLines(RemovedGamesPath, Encoding.UTF8))
                    {
                        string k = (raw ?? "").Trim();
                        if (!string.IsNullOrEmpty(k)) set.Add(k);
                    }
                }
            }
            catch { }
            return set;
        }

        public static void MarkGameRemoved(GameProfile gp)
        {
            if (gp == null) return;
            try
            {
                lock (RemovedSync)
                {
                    HashSet<string> set = LoadRemovedKeys();
                    if (!string.IsNullOrEmpty(gp.Id)) set.Add("id:" + gp.Id.Trim());
                    string norm = GameDeduplicationEngine.NormalizeGameTitle(gp.Name);
                    if (!string.IsNullOrEmpty(norm)) set.Add("name:" + norm);

                    string dir = Path.GetDirectoryName(RemovedGamesPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    List<string> outLines = new List<string>(set);
                    File.WriteAllLines(RemovedGamesPath, outLines.ToArray(), Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void UnmarkGameRemoved(GameProfile gp)
        {
            if (gp == null) return;
            try
            {
                lock (RemovedSync)
                {
                    if (!File.Exists(RemovedGamesPath)) return;
                    HashSet<string> set = LoadRemovedKeys();
                    bool changed = false;
                    if (!string.IsNullOrEmpty(gp.Id)) changed |= set.Remove("id:" + gp.Id.Trim());
                    string norm = GameDeduplicationEngine.NormalizeGameTitle(gp.Name);
                    if (!string.IsNullOrEmpty(norm)) changed |= set.Remove("name:" + norm);
                    if (changed)
                    {
                        List<string> outLines = new List<string>(set);
                        File.WriteAllLines(RemovedGamesPath, outLines.ToArray(), Encoding.UTF8);
                    }
                }
            }
            catch { }
        }

        public static bool IsGameRemoved(string id, string name, HashSet<string> cached = null)
        {
            try
            {
                HashSet<string> set = cached ?? LoadRemovedKeys();
                if (set.Count == 0) return false;
                if (!string.IsNullOrEmpty(id) && set.Contains("id:" + id.Trim())) return true;
                string norm = GameDeduplicationEngine.NormalizeGameTitle(name);
                if (!string.IsNullOrEmpty(norm) && set.Contains("name:" + norm)) return true;
            }
            catch { }
            return false;
        }

        public static List<string> GetRemovedGameNames()
        {
            List<string> names = new List<string>();
            try
            {
                foreach (string k in LoadRemovedKeys())
                {
                    if (k.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                    {
                        string n = k.Substring(5).Trim();
                        if (!string.IsNullOrEmpty(n)) names.Add(n);
                    }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return names;
        }

        public static GameProfile RestoreRemovedGameByName(string normName)
        {
            if (string.IsNullOrWhiteSpace(normName)) return null;
            string targetNorm = GameDeduplicationEngine.NormalizeGameTitle(normName);
            try
            {
                GameProfile matchedDef = null;
                foreach (GameProfile def in CreateDefaultCatalog())
                {
                    if (GameDeduplicationEngine.NormalizeGameTitle(def.Name) == targetNorm)
                    {
                        matchedDef = def;
                        break;
                    }
                }

                lock (RemovedSync)
                {
                    if (File.Exists(RemovedGamesPath))
                    {
                        HashSet<string> set = LoadRemovedKeys();
                        set.Remove("name:" + targetNorm);
                        if (matchedDef != null && !string.IsNullOrEmpty(matchedDef.Id))
                            set.Remove("id:" + matchedDef.Id);
                        List<string> outLines = new List<string>(set);
                        File.WriteAllLines(RemovedGamesPath, outLines.ToArray(), Encoding.UTF8);
                    }
                }
                return matchedDef;
            }
            catch { return null; }
        }

        public static bool NeedsRealSize(string fileSize)
        {
            if (string.IsNullOrWhiteSpace(fileSize)) return true;
            string s = fileSize.Trim();
            if (Regex.IsMatch(s, @"\d+(?:[.,]\d+)?\s*(?:GB|MB|KB|TB)", RegexOptions.IgnoreCase))
                return false;
            return true;
        }

        public static string ComputeFolderSizeText(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
                long total = 0;
                int count = 0;
                DirectoryInfo di = new DirectoryInfo(folder);
                foreach (FileInfo fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { total += fi.Length; } catch { }
                    if (++count >= 60000) break;
                }
                if (total <= 0) return null;
                if (total >= 1073741824L)
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0} GB", total / 1073741824.0);
                if (total >= 1048576L)
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} MB", Math.Max(1.0, total / 1048576.0));
                return "1 MB";
            }
            catch { return null; }
        }

        public static List<GameProfile> LoadProfiles()
        {
            List<GameProfile> defaults = CreateDefaultCatalog();
            List<GameProfile> list = new List<GameProfile>();
            bool dedupChanged = false;

            if (!File.Exists(ConfigPath))
            {
                AutoDiscoverInstalledGames(defaults);
                SaveProfiles(defaults);
                return defaults;
            }

            try
            {
                string[] lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] p = line.Split('|');
                    if (p.Length >= 13)
                    {
                        int ptMin = 0;
                        if (p.Length >= 18) int.TryParse(p[17], out ptMin);

                        GameProfile gp = new GameProfile
                        {
                            Id = p[0],
                            Name = p[1],
                            Category = p[2],
                            ExecutablePath = p[3],
                            WorkingDirectory = p[4],
                            Version = p[5],
                            RemoteVersion = p[6],
                            DownloadUrl = p[7],
                            OnlineFixUrl = p[8],
                            SteamAppId = p[9],
                            Description = p[10],
                            MaxPlayers = p[11],
                            BannerUrl = p[12],
                            CapsuleUrl = p.Length >= 14 ? p[13] : p[12],
                            FileSize = p.Length >= 15 ? p[14] : "3.2 GB",
                            DirectMirrorUrl = p.Length >= 16 ? p[15] : "",
                            IsFavorite = p.Length >= 17 && p[16] == "1",
                            PlayTimeMinutes = ptMin,
                            LastPlayedText = p.Length >= 19 ? p[18] : "Henüz Oynanmadı"
                        };
                        GameProfile defMatch = defaults.Find(delegate(GameProfile d) { return string.Equals(d.Id, gp.Id, StringComparison.OrdinalIgnoreCase); });
                        if (defMatch != null && string.IsNullOrEmpty(gp.DirectMirrorUrl) && !string.IsNullOrEmpty(defMatch.DirectMirrorUrl))
                        {
                            gp.DirectMirrorUrl = defMatch.DirectMirrorUrl;
                        }
                        // Recovery check: If fields were shifted, sanitize them cleanly
                        if (!string.IsNullOrEmpty(gp.SteamAppId) && gp.SteamAppId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            gp.SteamAppId = "480";
                            for (int i = 0; i < p.Length; i++)
                            {
                                int parsedId;
                                if (int.TryParse(p[i], out parsedId) && parsedId > 1000 && parsedId < 100000000)
                                {
                                    gp.SteamAppId = p[i];
                                    break;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(gp.FileSize) &&
                            (gp.FileSize.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                             gp.FileSize.Length > 24 ||
                             gp.FileSize.IndexOf("Direct Download", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            gp.FileSize = DownloadInstallService.GetDownloadSizeDisplay(gp);
                        }
                        if (NeedsRealSize(gp.FileSize))
                        {
                            gp.FileSize = DownloadInstallService.GetDownloadSizeDisplay(gp);
                        }

                        if (!string.IsNullOrEmpty(gp.DirectMirrorUrl) &&
                            !gp.DirectMirrorUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                            !gp.DirectMirrorUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                            !gp.DirectMirrorUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                        {
                            gp.DirectMirrorUrl = "";
                        }

                        if (!string.IsNullOrEmpty(gp.DownloadUrl) &&
                            (!gp.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                             gp.DownloadUrl.IndexOf("store.steampowered.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             Regex.IsMatch(gp.DownloadUrl, @"^https?://[^/]+/?$")))
                        {
                            gp.DownloadUrl = "";
                        }

                        if (!string.IsNullOrEmpty(gp.MaxPlayers) && gp.MaxPlayers.Length <= 8 && !gp.MaxPlayers.Contains(" "))
                        {
                            int np;
                            if (int.TryParse(gp.MaxPlayers, out np))
                            {
                                gp.MaxPlayers = (np > 100 ? 4 : np) + " Kişilik Co-Op";
                            }
                        }

                        list.Add(gp);
                    }
                }
            }
            catch (Exception loadEx)
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "load_error.txt"), loadEx.ToString()); } catch { }
                list = defaults;
            }

            HashSet<string> removedKeys = LoadRemovedKeys();

            if (list.Count == 0)
            {
                list = defaults;
            }
            else
            {
                // Merge any newly added default catalog games without losing existing user paths
                HashSet<string> existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (GameProfile existing in list)
                {
                    if (!string.IsNullOrEmpty(existing.Id))
                        existingIds.Add(existing.Id);
                }

                bool addedNew = false;
                foreach (GameProfile def in defaults)
                {
                    if (IsGameRemoved(def.Id, def.Name, removedKeys)) continue;
                    if (!existingIds.Contains(def.Id))
                    {
                        list.Add(def);
                        addedNew = true;
                    }
                }
                int countBefore = list.Count;
                list = GameDeduplicationEngine.Deduplicate(list);
                if (addedNew || list.Count != countBefore) { SaveProfiles(list); dedupChanged = true; }
            }

            // ÖNEMLİ SIRA: önce kurulu oyun yollarını çöz, SONRA dedupe.
            // Ters sırada, site kaydı kurulu kaydı yutuyor ve oyun "kurulu değil" görünüyordu.
            int relocated = AutoDiscoverInstalledGames(list);
            int sizeUpdated = 0;
            foreach (GameProfile gp in list)
            {
                if (gp != null && gp.IsInstalled && NeedsRealSize(gp.FileSize))
                {
                    string sz = ComputeFolderSizeText(!string.IsNullOrEmpty(gp.WorkingDirectory) ? gp.WorkingDirectory : Path.GetDirectoryName(gp.ExecutablePath));
                    if (!string.IsNullOrEmpty(sz))
                    {
                        gp.FileSize = sz;
                        sizeUpdated++;
                    }
                }
            }
            int beforeDedupe = list.Count;
            list = GameDeduplicationEngine.Deduplicate(list);
            if (relocated > 0 || sizeUpdated > 0 || dedupChanged || list.Count != beforeDedupe) SaveProfiles(list);
            return list;
        }

        private static readonly object SaveSync = new object();

        public static void SaveProfiles(List<GameProfile> profiles)
        {
            if (profiles == null) return;
            lock (SaveSync)
            {
                try
                {
                    List<GameProfile> snapshot = new List<GameProfile>(profiles);
                    List<string> lines = new List<string>();
                    lines.Add("# TitanGameHub v8.0 STEAM EDITION Catalog Database");
                    foreach (GameProfile g in snapshot)
                    {
                        if (g == null) continue;
                        string safeId = (g.Id ?? Guid.NewGuid().ToString("N")).Replace("|", "-").Trim();
                        string safeName = (g.Name ?? "Game").Replace("|", "-").Trim();
                        string safeCat = (g.Category ?? "Co-Op").Replace("|", "-").Trim();
                        string safeExe = (g.ExecutablePath ?? "").Replace("|", "-").Trim();
                        string safeWork = (g.WorkingDirectory ?? "").Replace("|", "-").Trim();
                        string safeVer = (g.Version ?? "v1.0").Replace("|", "-").Trim();
                        string safeRemVer = (g.RemoteVersion ?? "v1.0").Replace("|", "-").Trim();
                        string safeDl = (g.DownloadUrl ?? "").Replace("|", "-").Trim();
                        string safeFix = (g.OnlineFixUrl ?? "").Replace("|", "-").Trim();
                        string safeAppId = (g.SteamAppId ?? "480").Replace("|", "-").Trim();
                        string safeDesc = (g.Description ?? "").Replace("|", "/").Replace("\r", "").Replace("\n", " ").Trim();
                        string safeMaxP = (g.MaxPlayers ?? "4 Kişilik Co-Op").Replace("|", "-").Trim();
                        string safeBanner = (g.BannerUrl ?? "").Replace("|", "-").Trim();
                        string safeCapsule = (g.CapsuleUrl ?? "").Replace("|", "-").Trim();
                        string safeSize = (g.FileSize ?? "Hazır Paket").Replace("|", "-").Trim();
                        string safeDirect = (g.DirectMirrorUrl ?? "").Replace("|", "-").Trim();
                        string safeFav = g.IsFavorite ? "1" : "0";
                        string safeTime = g.PlayTimeMinutes.ToString();
                        string safeLast = (g.LastPlayedText ?? "Henüz Oynanmadı").Replace("|", "-").Trim();

                        string line = string.Join("|", new string[]
                        {
                            safeId, safeName, safeCat, safeExe, safeWork,
                            safeVer, safeRemVer, safeDl, safeFix, safeAppId,
                            safeDesc, safeMaxP, safeBanner, safeCapsule, safeSize,
                            safeDirect, safeFav, safeTime, safeLast
                        });
                        lines.Add(line);
                    }
                    string tmpPath = ConfigPath + ".tmp";
                    File.WriteAllLines(tmpPath, lines.ToArray(), Encoding.UTF8);
                    if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                    File.Move(tmpPath, ConfigPath);
                }
                catch { }
            }
        }

        public static bool TryResolveGameLoopPubgMobile(out string appMarketExe, out string workingDir)
        {
            appMarketExe = "";
            workingDir = "";
            try
            {
                List<string> candidateDirs = new List<string>();
                try
                {
                    using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Tencent\MobileGamePC\AppMarket"))
                    {
                        if (rk != null)
                        {
                            object p = rk.GetValue("InstallPath");
                            if (p != null) candidateDirs.Add(p.ToString());
                        }
                    }
                }
                catch { }
                try
                {
                    using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Tencent\MobileGamePC\AppMarket"))
                    {
                        if (rk != null)
                        {
                            object p = rk.GetValue("InstallPath");
                            if (p != null) candidateDirs.Add(p.ToString());
                        }
                    }
                }
                catch { }
                try
                {
                    using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Tencent\MobileGamePC\UI"))
                    {
                        if (rk != null)
                        {
                            object p = rk.GetValue("InstallPath");
                            if (p != null)
                            {
                                string uiDir = p.ToString().TrimEnd('\\', '/');
                                string parent = Path.GetDirectoryName(uiDir);
                                if (!string.IsNullOrEmpty(parent))
                                    candidateDirs.Add(Path.Combine(parent, "AppMarket"));
                            }
                        }
                    }
                }
                catch { }

                candidateDirs.Add(@"C:\TxGameAssistant\AppMarket");
                candidateDirs.Add(@"D:\TxGameAssistant\AppMarket");
                candidateDirs.Add(@"C:\Program Files\TxGameAssistant\AppMarket");
                candidateDirs.Add(@"D:\Program Files\TxGameAssistant\AppMarket");

                string resolvedExe = "";
                foreach (string dir in candidateDirs)
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string exe = Path.Combine(dir.TrimEnd('\\', '/'), "AppMarket.exe");
                    if (File.Exists(exe))
                    {
                        resolvedExe = exe;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(resolvedExe)) return false;

                bool hasPubgMarker = false;
                try
                {
                    using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Tencent\MobileGamePC\com.tencent.ig"))
                    {
                        if (rk != null) hasPubgMarker = true;
                    }
                }
                catch { }

                if (!hasPubgMarker)
                {
                    try
                    {
                        using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Tencent\MobileGamePC\NewOpGuidTip"))
                        {
                            if (rk != null && rk.GetValue("com.tencent.ig-dpiguid") != null)
                                hasPubgMarker = true;
                        }
                    }
                    catch { }
                }

                if (!hasPubgMarker)
                {
                    string txRoot = Path.GetDirectoryName(Path.GetDirectoryName(resolvedExe));
                    if (!string.IsNullOrEmpty(txRoot))
                    {
                        string gft = Path.Combine(txRoot, @"UI\AndroidEmulator\res\img\com.tencent.ig.gft");
                        string rootfs = Path.Combine(txRoot, @"AOW_Rootfs_100\rootfs.bin");
                        if (File.Exists(gft) && File.Exists(rootfs))
                            hasPubgMarker = true;
                    }
                }

                if (!hasPubgMarker) return false;

                appMarketExe = resolvedExe;
                workingDir = Path.GetDirectoryName(resolvedExe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static int AutoDiscoverInstalledGames(List<GameProfile> catalog)
        {
            string localGamesRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games");
            if (!Directory.Exists(localGamesRoot))
            {
                try { Directory.CreateDirectory(localGamesRoot); } catch { }
            }

            int changed = 0;

            // Tek seferlik kütüphane indeksi: 600+ oyunluk katalogda başlangıç süresini kısaltır.
            Dictionary<string, string> index = GamePathResolver.BuildLibraryIndex(false);

            foreach (GameProfile gp in catalog)
            {
                if (string.Equals(gp.Id, "pubg-mobile-gameloop", StringComparison.OrdinalIgnoreCase))
                {
                    string glExe, glDir;
                    if (TryResolveGameLoopPubgMobile(out glExe, out glDir))
                    {
                        gp.ExecutablePath = glExe;
                        gp.WorkingDirectory = glDir;
                        if (string.IsNullOrEmpty(gp.Version) || gp.Version.StartsWith("Resmi", StringComparison.OrdinalIgnoreCase))
                            gp.Version = "GameLoop (Kurulu)";
                        gp.RemoteVersion = gp.Version;
                        changed++;
                    }
                    else
                    {
                        gp.ExecutablePath = "";
                        gp.WorkingDirectory = "";
                    }
                    continue;
                }

                if (GamePathResolver.TryRelocateFromIndex(gp, index))
                {
                    if (gp.IsSteamManaged) gp.RemoteVersion = gp.Version;
                    changed++;
                    continue;
                }

                // Pahalı (tam dizin taraması) yola SADECE daha önce kurulu görünen kayıtlar için düş.
                // 600 oyunluk katalogda her oyun için tarama yapmak başlangıcı dakikalara çıkarıyordu.
                bool hadPath = !string.IsNullOrEmpty(gp.ExecutablePath);
                if (hadPath && GamePathResolver.TryRelocate(gp))
                {
                    // Steam kütüphanesindeki oyunlarda sürüm Steam'e bırakılır:
                    // repack sürümüyle kıyaslanınca yanlış "GÜNCELLE" rozeti çıkıyordu.
                    if (gp.IsSteamManaged) gp.RemoteVersion = gp.Version;
                    changed++;
                    continue;
                }

                gp.ExecutablePath = "";
                gp.WorkingDirectory = "";
            }

            // Yönetilen oyun köklerindeki (bin\Games, D:\Games vb.) klasörlerde kurulu olup katalogda eksik olan oyunları otomatik keşfet
            try
            {
                HashSet<string> knownNorms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> knownDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (GameProfile gp in catalog)
                {
                    if (gp == null) continue;
                    string kn = GameDeduplicationEngine.NormalizeGameTitle(gp.Name);
                    if (!string.IsNullOrEmpty(kn)) knownNorms.Add(kn);
                    if (!string.IsNullOrEmpty(gp.WorkingDirectory)) knownDirs.Add(gp.WorkingDirectory.TrimEnd('\\', '/'));
                }

                foreach (string root in GamePathResolver.GetLibraryRoots(false))
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                    string[] dirs;
                    try { dirs = Directory.GetDirectories(root); } catch { continue; }
                    foreach (string dir in dirs)
                    {
                        string dirName = Path.GetFileName(dir) ?? "";
                        if (string.IsNullOrEmpty(dirName) || dirName.StartsWith("_") || dirName.StartsWith(".")) continue;
                        if (dirName.EndsWith("_parts", StringComparison.OrdinalIgnoreCase)) continue;
                        if (knownDirs.Contains(dir.TrimEnd('\\', '/'))) continue;

                        string norm = GameDeduplicationEngine.NormalizeGameTitle(dirName);
                        if (string.IsNullOrEmpty(norm) || knownNorms.Contains(norm)) continue;
                        if (IsGameRemoved("local_" + norm, dirName)) continue;

                        string exe = FindMainExeInFolder(dir);
                        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                        {
                            string gameDir = Path.GetDirectoryName(exe);
                            string diskSize = ComputeFolderSizeText(gameDir) ?? "Kurulu";
                            GameProfile discovered = new GameProfile
                            {
                                Id = "local_" + norm,
                                Name = dirName,
                                Category = "Kurulu Oyun",
                                ExecutablePath = exe,
                                WorkingDirectory = gameDir,
                                Version = "Kurulu",
                                RemoteVersion = "Kurulu",
                                SteamAppId = "480",
                                Description = dirName + " — Bilgisayarda kurulu oyun.",
                                MaxPlayers = "Tek & Çok Oyunculu",
                                FileSize = diskSize
                            };
                            catalog.Add(discovered);
                            knownNorms.Add(norm);
                            changed++;
                        }
                    }
                }
            }
            catch { }

            return changed;
        }

        public static string FindMainExeInFolder(string folder)
        {
            try
            {
                List<string> exes = new List<string>();
                string[] rootExes = Directory.GetFiles(folder, "*.exe", SearchOption.TopDirectoryOnly);
                exes.AddRange(rootExes);

                string[] subDirs = Directory.GetDirectories(folder);
                foreach (string sd in subDirs)
                {
                    string sdName = (Path.GetFileName(sd) ?? "").ToLowerInvariant();
                    if (sdName == "_commonredist" || sdName == "redist" || sdName == "directx" || sdName == "support" || sdName == "jre")
                        continue;
                    try
                    {
                        exes.AddRange(Directory.GetFiles(sd, "*.exe", SearchOption.TopDirectoryOnly));
                        if (sdName == "bin" || sdName == "binaries" || sdName == "win64" || sdName == "game" || sdName == "build")
                            exes.AddRange(Directory.GetFiles(sd, "*.exe", SearchOption.AllDirectories));
                    }
                    catch { }
                }

                if (exes.Count == 0)
                {
                    try
                    {
                        string[] allSubExes = Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories);
                        exes.AddRange(allSubExes);
                    }
                    catch { }
                }

                string bestExe = null;
                long bestSize = -1;

                // Ana yurutulebilir dosya klasor adiyla uyumlu olma egilimindedir
                // (Ready or Not -> ReadyOrNot.exe). Dev kurulumculari / EOS kurucusu gibi
                // devasa ama oyun olmayan exe'lerin secilmesini bu skorlama engeller.
                string folderKey = GamePathResolver.NormalizeKey(Path.GetFileName(folder.TrimEnd('\\', '/')));
                int bestScore = -1;

                foreach (string exe in exes)
                {
                    string lower = exe.ToLowerInvariant();
                    string name = Path.GetFileName(lower);

                    if (name.Contains("unitycrash") || name.Contains("unins") || name.Contains("redist") ||
                        name.Contains("setup") || name.Contains("dxsetup") || name.Contains("vc_redist") ||
                        name.Contains("crashreport") || name.Contains("launcher") || name == "dsymutil.exe" ||
                        name.Contains("installer") || name.Contains("epiconlineservices") || name.Contains("eossdk") ||
                        name.Contains("easyanticheat") || name.Contains("battleye") || name.Contains("prereq") ||
                        name.Contains("dotnet") || name.Contains("directx") || name.Contains("subprocess") ||
                        name.Contains("unitycrashhandler") || name.StartsWith("uninstall") ||
                        name.Contains("vconsole") || name.Contains("touchup") || name.Contains("benchmark") ||
                        name.Contains("crashpad") || name.Contains("steamerrorreporter") || name.Contains("eac_") ||
                        lower.Contains("packagecache") || lower.Contains(@"\library\") || lower.Contains(@"\.runtime\"))
                        continue;

                    string exeKey = GamePathResolver.NormalizeKey(Path.GetFileNameWithoutExtension(exe));
                    int score = 1;
                    if (exeKey.Length > 0 && exeKey == folderKey) score = 3;
                    else if (folderKey.Length >= 4 && exeKey.Length >= 4 &&
                             (exeKey.Contains(folderKey) || folderKey.Contains(exeKey))) score = 2;

                    FileInfo fi = new FileInfo(exe);
                    if (score > bestScore || (score == bestScore && fi.Length > bestSize))
                    {
                        bestScore = score;
                        bestSize = fi.Length;
                        bestExe = exe;
                    }
                }
                return bestExe;
            }
            catch { return null; }
        }

        public static List<GameProfile> SearchSteamStoreLive(string query)
        {
            string status;
            List<GameProfile> liveRip = SiteParserService.FetchLiveSiteGames(0, query, 1, 24, out status);
            if (liveRip != null && liveRip.Count > 0)
                return liveRip;

            List<GameProfile> results = new List<GameProfile>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                using (WebClient wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    string url = "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(query) + "&l=turkish&cc=TR";
                    string json = wc.DownloadString(url);

                    MatchCollection matches = Regex.Matches(json, "\"id\"\\s*:\\s*(\\d+)\\s*,\\s*\"type\"\\s*:\\s*\"[^\"]+\"\\s*,\\s*\"name\"\\s*:\\s*\"([^\"]+)\"[^{}]*?\"tiny_image\"\\s*:\\s*\"([^\"]+)\"");
                    foreach (Match m in matches)
                    {
                        string appId = m.Groups[1].Value;
                        string rawName = Regex.Unescape(m.Groups[2].Value);
                        string tinyImg = Regex.Unescape(m.Groups[3].Value);
                        string headerUrl = !string.IsNullOrEmpty(tinyImg)
                            ? tinyImg.Replace("capsule_sm_120.jpg", "header.jpg").Replace("capsule_231x87.jpg", "header.jpg")
                            : ("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + appId + "/header.jpg");
                        string slug = Regex.Replace(rawName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

                        GameProfile gp = new GameProfile
                        {
                            Id = "steam_" + appId,
                            Name = rawName,
                            Category = "Co-Op & Online",
                            ExecutablePath = "",
                            WorkingDirectory = "",
                            Version = "Son Sürüm",
                            RemoteVersion = "Son Sürüm",
                            DownloadUrl = "https://steamrip.com/" + slug + "-free-download/",
                            OnlineFixUrl = "https://online-fix.me/index.php?do=search&subaction=search&story=" + Uri.EscapeDataString(rawName),
                            SteamAppId = appId,
                            Description = rawName + " — SteamRIP ve Spacewar P2P (AppID 480) üzerinden arkadaşlarınızla çevrim içi oynanabilir mağaza paketi.",
                            MaxPlayers = "Online Co-Op / P2P",
                            BannerUrl = !string.IsNullOrEmpty(tinyImg) ? tinyImg : headerUrl,
                            CapsuleUrl = !string.IsNullOrEmpty(tinyImg) ? tinyImg : headerUrl,
                            FileSize = "Hazır Arşiv",
                            DirectMirrorUrl = "",
                            IsFavorite = false,
                            PlayTimeMinutes = 0,
                            LastPlayedText = "Henüz Oynanmadı"
                        };
                        results.Add(gp);
                        if (results.Count >= 12) break;
                    }
                }
            }
            catch { }

            return results;
        }

        private static GameProfile MakeDefault(string id, string name, string cat, string ver, string dlUrl, string appId, string desc, string maxP, string size, bool fav = false, string directMirror = "")
        {
            string header = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + appId + "/header.jpg";
            return new GameProfile
            {
                Id = id,
                Name = name,
                Category = cat,
                ExecutablePath = "",
                WorkingDirectory = "",
                Version = ver,
                RemoteVersion = ver,
                DownloadUrl = dlUrl,
                OnlineFixUrl = "https://online-fix.me/index.php?do=search&subaction=search&story=" + Uri.EscapeDataString(name),
                SteamAppId = appId,
                Description = desc,
                MaxPlayers = maxP,
                BannerUrl = header,
                CapsuleUrl = header,
                FileSize = size,
                DirectMirrorUrl = directMirror ?? "",
                IsFavorite = fav,
                PlayTimeMinutes = 0,
                LastPlayedText = "Henüz Oynanmadı"
            };
        }

        private static List<GameProfile> CreateDefaultCatalog()
        {
            GameProfile moon = MakeDefault("moonsweepers", "Moon Sweepers", "Simülasyon", "v1.0.5",
                "https://steamrip.com/moon-sweepers-free-download/", "5335510",
                "Ay yüzeyini temizleme ve üs kurma simülasyonu. Spacewar (AppID 480) ile farklı internetlerdeki arkadaşlarınızla 16 kişiye kadar kesintisiz oynayın.", "16 Kişilik Online Co-Op", "505 MB", true);
            moon.BannerUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/5335510/86fae70046ddca14dc1d44312c5eeb310081eb81/capsule_231x87.jpg";
            moon.CapsuleUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/5335510/86fae70046ddca14dc1d44312c5eeb310081eb81/capsule_231x87.jpg";
            moon.DirectMirrorUrl = "https://pixeldrain.com/u/HH9K444p";

            return new List<GameProfile>
            {
                moon,

                MakeDefault("repo", "R.E.P.O.", "Korku", "v1.1.2", "https://steamrip.com/r-e-p-o-free-download/", "3241660", "Fizik tabanlı 6 kişilik çevrim içi eşli korku ve ganimet toplama oyunu. Dahili Online-Fix P2P ile anında arkadaş lobisine bağlanır.", "6 Kişilik Online Co-Op", "1.4 GB", false, "https://uploads.online-fix.me:2053/torrents/R.E.P.O./REPO.v0.5.0-OFME.torrent"),

                MakeDefault("peak", "PEAK", "Co-Op & Online", "v1.0.4", "https://steamrip.com/peak-free-download/", "3527290", "Arkadaşlarınızla ölümcül bir dağın zirvesine tırmandığınız fizik tabanlı 4 kişilik co-op hayatta kalma ve tırmanış macerası.", "4 Kişilik Online Co-Op", "2.1 GB", false, "https://gofile.io/d/wvU6sDO6"),

                MakeDefault("lethal", "Lethal Company", "Korku", "v64.0", "https://steamrip.com/lethal-company-free-download-1x/", "1966720", "Terk edilmiş uydulardan hurda toplama ve hayatta kalma oyunu. Spacewar P2P üzerinden farklı şehirlerdeki arkadaşlarınızla lobi kurun.", "4-32 Kişilik Co-Op", "1.2 GB", false, "https://gofile.io/d/jMI5xb"),

                MakeDefault("schedule1", "Schedule I", "Simülasyon", "v0.3.4", "https://steamrip.com/schedule-i-free-download/", "3164500", "Açık dünya co-op imparatorluk kurma ve yönetim simülasyonu. Arkadaşlarınızla aynı dünyada ortak operasyon yönetin.", "4 Kişilik Online Co-Op", "3.1 GB", false, "https://itsfileditchfiles.st/balpha28/b5b71504ac0a59fbe46a/Skedule-I-SteamRIP.com.rar"),

                MakeDefault("chained", "Chained Together", "Co-Op & Online", "v1.8.4", "https://steamrip.com/chained-together-free-download/", "2567870", "Arkadaşlarınızla birbirinize zincirlenmiş halde cehennemin derinliklerinden zirveye tırmandığınız fizik tabanlı co-op macera.", "4 Kişilik Online Co-Op", "4.6 GB", false, "https://gofile.io/d/ZzRKaw"),

                MakeDefault("liarsbar", "Liar's Bar", "Co-Op & Online", "v1.0.9", "https://steamrip.com/liars-bar-free-download/", "3097560", "4 kişilik çevrim içi blöf ve masa oyunları barı. Spacewar P2P lobi davetiyle arkadaşlarınızla anında masa kurun.", "4 Kişilik Online Lobi", "2.9 GB", false, "https://fileditchfiles.me/file.php?f=/alpha2/accf7b5f49c3b71a2a5c/Liars-Bar-SteamRIP.com.rar"),

                MakeDefault("supermarket", "Supermarket Together", "Simülasyon", "v1.3.0", "https://steamrip.com/supermarket-together-free-download/", "2709570", "Arkadaşlarınızla 16 kişiye kadar ortak süpermarket kurup yönettiğiniz çevrim içi co-op simülasyon.", "16 Kişilik Online Co-Op", "2.4 GB", false, "steam://run/2709570"),

                MakeDefault("fastfood", "Fast Food Simulator", "Simülasyon", "v0.4.2", "https://steamrip.com/fast-food-simulator-free-download/", "2916430", "6 kişiye kadar arkadaşlarınızla kaos dolu bir fast-food restoranını işlettiğiniz sesli sohbet destekli co-op simülasyon.", "6 Kişilik Online Co-Op", "3.8 GB", false, "https://gofile.io/d/vsLUWV"),

                MakeDefault("contentwarning", "Content Warning", "Korku", "v1.15.a", "https://steamrip.com/content-warning-free-download/", "2881650", "Arkadaşlarınızla Eski Dünya'ya inip korkunç yaratıkları kameraya çekerek SpookTube'da viral olmaya çalışın.", "4 Kişilik Co-Op", "1.6 GB", false, "https://gofile.io/d/36DApV"),

                MakeDefault("murkydivers", "Murky Divers", "Korku", "v1.4.0", "https://steamrip.com/murky-divers-free-download/", "2963880", "8 kişiye kadar denizaltı mürettebatı olup okyanus tabanındaki terk edilmiş laboratuvarlardan kanıtları temizleyin.", "8 Kişilik Online Co-Op", "4.2 GB", false, "https://gofile.io/d/36DApV"),

                MakeDefault("lockdown", "LOCKDOWN Protocol", "Co-Op & Online", "v0.14.0",
                    "https://steamrip.com/lockdown-protocol-free-download/", "2780980",
                    "8 kişilik birinci şahıs sosyal çıkarım, görev tamamlama ve sabotaj oyunu. Gerçek zamanlı yakınlık sesi ve Spacewar P2P.", "8 Kişilik Online Lobi", "2.1 GB"),

                MakeDefault("daledawson", "Dale & Dawson Stationery Supplies", "Co-Op & Online", "v1.1.0",
                    "https://steamrip.com/dale-dawson-stationery-supplies-free-download/", "2920570",
                    "Ofiste çalışanlar, kaytaranlar ve müdür arasında geçen 16 kişilik sosyal çıkarım ve ofis simülasyonu.", "16 Kişilik Online Lobi", "1.9 GB"),

                MakeDefault("buckshot", "Buckshot Roulette", "Korku", "v1.2.2", "https://steamrip.com/buckshot-roulette-free-download/", "2835570", "4 kişilik çok oyunculu masa moduna sahip gerilim dolu pompalı tüfek ruleti.", "4 Kişilik Online Lobi", "0.6 GB", false, "https://bzzhr.to/wak6fldg66j8"),

                MakeDefault("palworld", "Palworld", "Hayatta Kalma", "v0.3.10",
                    "https://steamrip.com/palworld-free-download-1f/", "1623730",
                    "Açık dünyada yaratık yakalama, üs inşa etme ve eşli hayatta kalma oyunu. Çok oyunculu davet kodu ve Spacewar P2P destekli.", "4-32 Kişilik Co-Op", "18.5 GB"),

                MakeDefault("phasmophobia", "Phasmophobia", "Korku", "v0.10.1", "https://steamrip.com/phasmophobia-free-download-2j/", "739630", "4 kişilik çevrim içi psikolojik korku ve paranormal hayalet avı simülasyonu. Sesli iletişim ve oda kodu desteği.", "4 Kişilik Co-Op", "14.2 GB", false, "https://gofile.io/d/Jd8CkNAI"),

                MakeDefault("sotf", "Sons Of The Forest", "Hayatta Kalma", "v1.0.2", "https://steamrip.com/sons-of-the-forest-free-download-1i/", "1326470", "Yamyamlarla dolu ıssız bir adada 8 kişiye kadar arkadaşlarınızla üs kurup hayatta kalın.", "8 Kişilik Co-Op", "13.8 GB", false, "https://gofile.io/d/fMSR6c"),

                MakeDefault("theforest", "The Forest", "Hayatta Kalma", "v1.12", "https://steamrip.com/the-forest-free-download/", "242760", "Uçak kazası sonrası gizemli bir ormanda 8 kişiye kadar arkadaşlarınızla kamp kurup mağaraları keşfedin.", "8 Kişilik Online Co-Op", "5.2 GB", false, "https://bzzhr.to/tf2zcgbqo0s9"),

                MakeDefault("raft", "Raft", "Hayatta Kalma", "v1.09",
                    "https://steamrip.com/raft-free-download-1p/", "648800",
                    "Sonsuz okyanusta arkadaşlarınızla salınızı büyütün, adaları keşfedin ve hayatta kalın.", "8 Kişilik Co-Op", "2.8 GB"),

                MakeDefault("backrooms", "Escape the Backrooms", "Korku", "v4.2", "https://steamrip.com/escape-the-backrooms-free-download/", "1943950", "4 kişilik co-op Backrooms bölümlerini keşfetme ve varlıklardan kaçış oyunu.", "4 Kişilik Online Co-Op", "9.6 GB", false, "https://gofile.io/d/vnTdUxRb"),

                MakeDefault("valheim", "Valheim", "Hayatta Kalma", "v0.219",
                    "https://steamrip.com/valheim-free-download/", "892970",
                    "10 kişiye kadar Viking açık dünyasında keşif, inşa ve boss savaşları içeren efsanevi co-op hayatta kalma oyunu.", "10 Kişilik Online Co-Op", "1.5 GB"),

                MakeDefault("enshrouded", "Enshrouded", "Hayatta Kalma", "v0.7.4",
                    "https://steamrip.com/enshrouded-free-download/", "1203620",
                    "16 kişiye kadar voksel tabanlı açık dünyada kale inşa etme, zindan keşfi ve RPG hayatta kalma oyunu.", "16 Kişilik Online Co-Op", "26.0 GB"),

                MakeDefault("7dtd", "7 Days to Die", "Hayatta Kalma", "v1.1 (b14)",
                    "https://steamrip.com/7-days-to-die-free-download/", "251570",
                    "Açık dünya zombi hayatta kalma, üs savunma ve üretim co-op oyunu.", "8 Kişilik Online Co-Op", "12.4 GB"),

                MakeDefault("ets2", "Euro Truck Simulator 2", "Simülasyon", "v1.53", "https://steamrip.com/euro-truck-simulator-2-free-download/", "227300", "8 kişilik resmi Convoy Co-Op modunda arkadaşlarınızla Avrupa yollarında ortak tır konvoyu kurun.", "8 Kişilik Convoy Co-Op", "21.0 GB", false, "https://bzzhr.to/65fw33nftxd9"),

                MakeDefault("readyornot", "Ready or Not", "Co-Op & Online", "v1.0 (DLC)",
                    "https://steamrip.com/ready-or-not-free-download/", "1144200",
                    "5 kişilik taktiksel SWAT operasyonu ve rehine kurtarma simülasyonu. Spacewar P2P lobi destekli.", "5 Kişilik Taktik Co-Op", "38.0 GB"),

                MakeDefault("pz", "Project Zomboid", "Hayatta Kalma", "v41.78", "https://steamrip.com/project-zomboid-free-download-1v/", "108600", "İzometrik açık dünya zombi hayatta kalma simülasyonu. Çok oyunculu P2P sunucu kurma destekli.", "32 Kişilik Co-Op", "3.4 GB", false, "https://gofile.io/d/jMI5xb"),

                MakeDefault("deeprock", "Deep Rock Galactic", "Co-Op & Online", "v1.39", "https://steamrip.com/deep-rock-galactic-free-download/", "548430", "4 kişilik uzay cücesi ekibiyle tamamen parçalanabilir mağaralarda maden kazma ve uzaylı sürülerine karşı savaşma.", "4 Kişilik Online Co-Op", "3.5 GB", false, "https://bzzhr.to/hl7j8hiqd6rr"),

                MakeDefault("satisfactory", "Satisfactory", "Simülasyon", "v1.0.1", "https://steamrip.com/satisfactory-free-download/", "526870", "Birinci şahıs açık dünyada arkadaşlarınızla devasa otomasyon fabrikaları ve konveyör hatları kurun.", "4-8 Kişilik Online Co-Op", "15.0 GB", false, "https://gofile.io/d/iIPzm1"),

                MakeDefault("grounded", "Grounded", "Hayatta Kalma", "v1.4.6", "https://steamrip.com/grounded-free-download/", "962130", "Karınca boyutuna küçültülmüş 4 arkadaş olarak arka bahçenin dev böceklerine karşı üs kurup hayatta kalın.", "4 Kişilik Online Co-Op", "11.5 GB", false, "https://gofile.io/d/c2xaeZ"),

                MakeDefault("greenhell", "Green Hell", "Hayatta Kalma", "v2.9.2", "https://steamrip.com/green-hell-free-download/", "815370", "Amazon yağmur ormanlarında 4 kişilik gerçekçi hayatta kalma ve psikolojik macera.", "4 Kişilik Online Co-Op", "6.8 GB", false, "https://gofile.io/d/37JN4g"),

                MakeDefault("abiotic", "Abiotic Factor", "Hayatta Kalma", "v0.9.2", "https://steamrip.com/abiotic-factor-free-download/", "427410", "6 kişiye kadar bilim insanı ekibiyle yeraltı araştırma tesisinde paranormal varlıklara karşı hayatta kalın.", "6 Kişilik Online Co-Op", "2.2 GB", false, "https://gofile.io/d/lxmbnN"),

                MakeDefault("devour", "DEVOUR", "Korku", "v5.1.3", "https://steamrip.com/devour-free-download/", "1274570", "4 kişilik eşli korku oyunu; şeytan tarafından ele geçirilmiş tarikat liderini ritüeli tamamlayarak durdurun.", "4 Kişilik Online Co-Op", "5.4 GB", false, "https://gofile.io/d/4gGDhx"),

                MakeDefault("outlasttrials", "The Outlast Trials", "Korku", "v1.2", "https://steamrip.com/the-outlast-trials-free-download/", "1304930", "4 kişiye kadar arkadaşlarınızla Murkoff tesisinin korkunç deneylerinden sağ çıkmaya çalışın.", "4 Kişilik Online Co-Op", "34.0 GB", false, "https://gofile.io/d/4gGDhx"),

                MakeDefault("l4d2", "Left 4 Dead 2", "Korku", "v2.2.4", "https://steamrip.com/left-4-dead-2-free-download/", "550", "Efsanevi 4-8 kişilik zombi kıyameti co-op FPS klasiği. LAN ve Spacewar P2P destekli.", "4-8 Kişilik Co-Op", "13.0 GB", false, "https://gofile.io/d/RPbTag"),

                new GameProfile
                {
                    Id = "pubg-mobile-gameloop",
                    Name = "PUBG Mobile (GameLoop)",
                    Category = "Battle Royale / FPS",
                    Version = "Resmi • GameLoop",
                    RemoteVersion = "Resmi • GameLoop",
                    DownloadUrl = "https://www.gameloop.com/game/action/pubg-mobile-on-pc",
                    OnlineFixUrl = "",
                    SteamAppId = "578080",
                    Description = "Resmi Tencent GameLoop emülatörü üzerinden PUBG Mobile PC deneyimi. Bilgisayarınızda GameLoop + PUBG Mobile yüklüyse otomatik algılanır ve tek tıkla doğrudan başlatılır; yüklü değilse resmi GameLoop yükleyicisi uygulama içinden indirilir.",
                    MaxPlayers = "100 Kişilik Battle Royale",
                    BannerUrl = "https://img.gameloop.com/syzs_cms/202611/26ca1c8e2784dddba619527229789fae.jpeg",
                    CapsuleUrl = "https://img.gameloop.com/syzs_cms/202611/26ca1c8e2784dddba619527229789fae.jpeg",
                    FileSize = "4.5 GB",
                    DirectMirrorUrl = "https://down.gameloop.com/channel/88/16229/GLP_installer_1000218273_com.tencent.ig.exe",
                    LastPlayedText = "Henüz Oynanmadı"
                }
            };
        }
    }
}
